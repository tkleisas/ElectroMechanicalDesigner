using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record KiCadPcbImportResult(
    Part? Board,
    Board? BoardEntry,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Board is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Reads the Edge.Cuts outline and through-hole mounting pads from a
/// <c>.kicad_pcb</c> file and turns them into a board part with mechanical ports.
/// </summary>
public sealed class KiCadPcbOutlineImporter
{
    public static KiCadPcbOutlineImporter Default { get; } = new();

    public KiCadPcbImportResult ImportFile(string path)
    {
        try
        {
            return Import(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("EMD-IO-001",
                $"Could not read KiCad PCB '{path}': {exception.Message}", path);
        }
    }

    public KiCadPcbImportResult Import(string text, string? source = null)
    {
        SExpr root;
        try
        {
            if (!TryTokenize(text, out var tokens, out var error))
                return Failure("EMD-KICAD-021", $"Invalid KiCad PCB: {error}", source);

            var index = 0;
            var parsed = Parse(tokens, ref index);
            if (parsed is null)
                return Failure("EMD-KICAD-021", "Invalid KiCad PCB: empty S-expression.", source);
            root = parsed;
        }
        catch (FormatException exception)
        {
            return Failure("EMD-KICAD-021", $"Invalid KiCad PCB: {exception.Message}", source);
        }

        if (root.Head != "kicad_pcb")
            return Failure("EMD-KICAD-021", "S-expression is not a KiCad PCB.", source);

        var points = new List<(double X, double Y)>();
        foreach (var node in Lists(root))
        {
            if (node.Head is not ("gr_rect" or "gr_line" or "gr_arc" or "gr_circle"))
                continue;

            var layer = Child(node, "layer");
            if (layer is null || Value(layer, 1) != "Edge.Cuts")
                continue;

            AddPoint(Child(node, "start"));
            AddPoint(Child(node, "end"));
        }

        if (points.Count < 2)
            return Failure("EMD-KICAD-020", "KiCad PCB has no Edge.Cuts outline.", source);

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var widthMm = maxX - minX;
        var heightMm = maxY - minY;
        if (widthMm <= 0 || heightMm <= 0)
            return Failure("EMD-KICAD-020", "KiCad PCB Edge.Cuts outline has zero area.", source);

        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        var thicknessMm = ReadThickness(root);
        var holes = ReadHoles(root);
        var diagnostics = new List<Diagnostic>();
        if (holes.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-KICAD-022",
                DiagnosticSeverity.Warning,
                "KiCad PCB has no through-hole mounting pads.",
                Entity: source));
        }

        var ns = EmdNamespaces.KiCad;
        var name = ProjectName(source);
        var partId = DeterministicGuid.Create(ns, $"board:{source ?? name}");
        var ports = new List<Port>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < holes.Count; index++)
        {
            var hole = holes[index];
            var baseKey = string.IsNullOrWhiteSpace(hole.Reference)
                ? $"H{index + 1}"
                : hole.Reference.Trim();
            var id = Slug.Key(baseKey, "hole");
            while (!usedIds.Add(id))
                id = Slug.Key($"{baseKey}_{index + 1}", "hole");

            ports.Add(new Port
            {
                Id = id,
                Key = baseKey,
                Name = baseKey,
                Domain = PortDomain.Mechanical,
                Kind = "mount",
                Direction = PortDirection.Passive,
                Pose = new Pose
                {
                    Pos = [(hole.X - centerX) / 1000, (hole.Y - centerY) / 1000, 0]
                },
                Metadata = new JsonObject
                {
                    ["x_mm"] = hole.X,
                    ["y_mm"] = hole.Y,
                    ["drill_mm"] = hole.DiameterMm
                }
            });
        }

        var part = new Part
        {
            Id = partId,
            Key = Slug.Create(name, "board"),
            Name = name,
            Kind = PartKind.Board,
            Category = PartCategory.Electronics,
            Pose = new Pose { Pos = [centerX / 1000, centerY / 1000, 0] },
            Geometry = new PartGeometry
            {
                Kind = GeometryKind.Box,
                Size = [widthMm / 1000, heightMm / 1000, thicknessMm / 1000],
                Rgba = "0.1 0.35 0.2 1"
            },
            DomainRefs = new PartDomainRefs
            {
                Electrical = new DomainRef { Tool = "kicad", Ref = source ?? name }
            },
            Ports = ports.OrderBy(port => port.Id, StringComparer.Ordinal).ToList(),
            Metadata = new JsonObject
            {
                ["width_mm"] = widthMm,
                ["height_mm"] = heightMm,
                ["thickness_mm"] = thicknessMm
            }
        };

        var boardEntry = new Board
        {
            Id = DeterministicGuid.Create(ns, $"board-entry:{source ?? name}"),
            Key = part.Key,
            PartId = part.Id,
            Refdes = name
        };

        return new KiCadPcbImportResult(part, boardEntry, diagnostics);

        void AddPoint(SExpr? point)
        {
            if (point is null)
                return;
            var x = Number(point, 1);
            var y = Number(point, 2);
            if (x is null || y is null)
                return;
            points.Add((x.Value, y.Value));
        }
    }

    private static double ReadThickness(SExpr root)
    {
        var general = Child(root, "general");
        var thickness = general is null ? null : Child(general, "thickness");
        return thickness is null ? 1.6 : Number(thickness, 1) ?? 1.6;
    }

    private static List<BoardHole> ReadHoles(SExpr root)
    {
        var holes = new List<BoardHole>();
        foreach (var footprint in Lists(root).Where(node => node.Head is "footprint" or "module"))
        {
            var at = Child(footprint, "at");
            if (at is null)
                continue;

            var footprintX = Number(at, 1) ?? 0;
            var footprintY = Number(at, 2) ?? 0;
            var reference = ReferenceOf(footprint);
            var footprintName = Value(footprint, 1);
            var isMountingHole =
                (footprintName?.Contains("MountingHole", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (reference?.StartsWith('H') == true && reference.Length > 1 &&
                 char.IsDigit(reference[1]));
            if (!isMountingHole)
                continue;

            foreach (var pad in Children(footprint, "pad"))
            {
                var drill = Child(pad, "drill");
                if (drill is null)
                    continue;

                var padAt = Child(pad, "at");
                var padX = padAt is null ? 0 : Number(padAt, 1) ?? 0;
                var padY = padAt is null ? 0 : Number(padAt, 2) ?? 0;
                holes.Add(new BoardHole(
                    reference,
                    footprintX + padX,
                    footprintY + padY,
                    Number(drill, 1)));
            }
        }

        return holes;
    }

    private static string? ReferenceOf(SExpr footprint)
    {
        var property = Children(footprint, "property")
            .FirstOrDefault(item => Value(item, 1) == "Reference");
        if (property is not null)
            return Value(property, 2);

        var text = Children(footprint, "fp_text")
            .FirstOrDefault(item => Value(item, 1) == "reference");
        return text is null ? null : Value(text, 2);
    }

    private static string ProjectName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "board";
        var name = Path.GetFileNameWithoutExtension(source);
        return string.IsNullOrWhiteSpace(name) ? "board" : name;
    }

    private static bool TryTokenize(string text, out List<string> tokens, out string error)
    {
        tokens = [];
        error = "";

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character))
                continue;
            if (character is '(' or ')')
            {
                tokens.Add(character.ToString());
                continue;
            }
            if (character == '"')
            {
                var builder = new StringBuilder();
                index++;
                var closed = false;
                while (index < text.Length)
                {
                    if (text[index] == '\\' && index + 1 < text.Length)
                    {
                        builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }
                    if (text[index] == '"')
                    {
                        closed = true;
                        break;
                    }
                    builder.Append(text[index]);
                    index++;
                }

                if (!closed)
                {
                    error = "unterminated string";
                    return false;
                }

                tokens.Add(builder.ToString());
                continue;
            }

            var start = index;
            while (index < text.Length &&
                   !char.IsWhiteSpace(text[index]) &&
                   text[index] is not '(' and not ')')
            {
                index++;
            }

            tokens.Add(text[start..index]);
            index--;
        }

        return true;
    }

    private static SExpr? Parse(List<string> tokens, ref int index)
    {
        if (index >= tokens.Count)
            return null;

        var token = tokens[index++];
        if (token == "(")
        {
            var items = new List<SExpr>();
            while (index < tokens.Count && tokens[index] != ")")
            {
                var child = Parse(tokens, ref index);
                if (child is null)
                    throw new FormatException("unexpected end of input");
                items.Add(child);
            }

            if (index >= tokens.Count)
                throw new FormatException("missing ')'");
            index++;
            return new SExpr(null, items);
        }

        if (token == ")")
            throw new FormatException("unexpected ')'");
        return new SExpr(token, null);
    }

    private static IEnumerable<SExpr> Lists(SExpr expression)
    {
        if (expression.Items is null)
            yield break;

        yield return expression;
        foreach (var child in expression.Items)
        {
            foreach (var nested in Lists(child))
                yield return nested;
        }
    }

    private static SExpr? Child(SExpr expression, string name) =>
        expression.Items?.FirstOrDefault(item => item.Head == name);

    private static IEnumerable<SExpr> Children(SExpr expression, string name) =>
        expression.Items?.Where(item => item.Head == name) ?? [];

    private static string? Value(SExpr expression, int index) =>
        expression.Items is { Count: > 0 } && expression.Items.Count > index
            ? expression.Items[index].Atom
            : null;

    private static double? Number(SExpr expression, int index)
    {
        var value = Value(expression, index);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static KiCadPcbImportResult Failure(string code, string message, string? entity) =>
        new(null, null,
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: entity)
        ]);

    private sealed record SExpr(string? Atom, List<SExpr>? Items)
    {
        public string? Head => Items is { Count: > 0 } ? Items[0].Atom : null;
    }

    private readonly record struct BoardHole(
        string? Reference,
        double X,
        double Y,
        double? DiameterMm);
}
