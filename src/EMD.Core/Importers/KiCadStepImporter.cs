using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record KiCadStepInfo(
    string? FileName,
    string? Schema,
    int PointCount,
    double WidthMm,
    double HeightMm,
    double DepthMm);

public sealed record KiCadStepImportResult(
    Artifact? Artifact,
    KiCadStepInfo? Info,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Artifact is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Registers a KiCad STEP export and extracts its header and Cartesian-point
/// bounding box. The PCB remains canonical for exact Edge.Cuts and hole data;
/// this artifact is the mechanical assembly reference.
/// </summary>
public sealed partial class KiCadStepImporter
{
    public static KiCadStepImporter Default { get; } = new();

    public KiCadStepImportResult ImportFile(string path)
    {
        try
        {
            return Import(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new KiCadStepImportResult(null, null,
            [
                new Diagnostic(
                    "EMD-IO-001",
                    DiagnosticSeverity.Error,
                    $"Could not read STEP file '{path}': {exception.Message}",
                    Entity: path)
            ]);
        }
    }

    public KiCadStepImportResult Import(string text, string? source = null)
    {
        var diagnostics = new List<Diagnostic>();
        if (!text.StartsWith("ISO-10303-21;", StringComparison.Ordinal) ||
            !text.Contains("END-ISO-10303-21;", StringComparison.Ordinal))
        {
            return new KiCadStepImportResult(null, null,
            [
                new Diagnostic(
                    "EMD-KICAD-023",
                    DiagnosticSeverity.Error,
                    "STEP file is missing ISO-10303-21 header/footer.",
                    Entity: source)
            ]);
        }

        var fileName = FileNamePattern().Match(text) is { Success: true } name
            ? name.Groups[1].Value
            : null;
        var schema = SchemaPattern().Match(text) is { Success: true } schemaMatch
            ? schemaMatch.Groups[1].Value
            : null;

        var xs = new List<double>();
        var ys = new List<double>();
        var zs = new List<double>();
        foreach (Match match in PointPattern().Matches(text))
        {
            var values = match.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => double.TryParse(
                    item, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : (double?)null)
                .ToArray();
            if (values.Length < 3 || values.Any(value => value is null))
                continue;

            xs.Add(values[0]!.Value);
            ys.Add(values[1]!.Value);
            zs.Add(values[2]!.Value);
        }

        if (xs.Count < 2)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-KICAD-024",
                DiagnosticSeverity.Warning,
                "STEP file has no usable CARTESIAN_POINT bounding box.",
                Entity: source));
        }

        var width = xs.Count == 0 ? 0 : xs.Max() - xs.Min();
        var height = ys.Count == 0 ? 0 : ys.Max() - ys.Min();
        var depth = zs.Count == 0 ? 0 : zs.Max() - zs.Min();
        var info = new KiCadStepInfo(fileName, schema, xs.Count, width, height, depth);

        var content = System.Text.Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var artifact = new Artifact
        {
            Id = DeterministicGuid.Create(EmdNamespaces.KiCad, $"step:{sha}"),
            Key = Slug.Create(
                Path.GetFileNameWithoutExtension(source ?? fileName ?? "board"),
                "board_step"),
            Path = source is null ? "" : Path.GetFullPath(source),
            Format = "step",
            Role = ArtifactRole.Generated,
            Sha256 = sha,
            Bytes = content.LongLength,
            ProducedBy = new ToolRef { Tool = "kicad" },
            Metadata = new JsonObject
            {
                ["step_file_name"] = fileName,
                ["step_schema"] = schema,
                ["point_count"] = xs.Count,
                ["bounding_box_mm"] = new JsonObject
                {
                    ["width"] = width,
                    ["height"] = height,
                    ["depth"] = depth
                }
            }
        };

        return new KiCadStepImportResult(artifact, info, diagnostics);
    }

    [GeneratedRegex(@"FILE_NAME\s*\(\s*'([^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();

    [GeneratedRegex(@"CARTESIAN_POINT\s*\(\s*'[^']*'\s*,\s*\(\s*([^)]*)\)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex PointPattern();
}
