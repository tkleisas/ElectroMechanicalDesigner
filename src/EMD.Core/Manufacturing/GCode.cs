using System.Globalization;
using Emd.Core.Diagnostics;

namespace Emd.Core.Manufacturing;

public sealed record GCodeMove(
    int Line,
    double[] Start,
    double[] End,
    double FeedMmMin,
    bool Rapid)
{
    public double[] RotaryStart { get; init; } = [0, 0, 0];
    public double[] RotaryEnd { get; init; } = [0, 0, 0];
}

public sealed record GCodeProgram(
    IReadOnlyList<GCodeMove> Moves,
    double[] Min,
    double[] Max,
    double CutLengthMm,
    double RapidLengthMm,
    int LineCount)
{
    public double[] RotaryMin { get; init; } = [0, 0, 0];
    public double[] RotaryMax { get; init; } = [0, 0, 0];
}

public sealed record GCodeLoadResult(
    GCodeProgram? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Program is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class GCodeParser
{
    public static GCodeParser Default { get; } = new();

    public GCodeLoadResult Parse(string text, string? source = null)
    {
        var diagnostics = new List<Diagnostic>();
        var moves = new List<GCodeMove>();
        var position = new[] { 0d, 0d, 0d };
        var rotary = new[] { 0d, 0d, 0d };
        var absolute = true;
        var scale = 1.0;
        var feed = 0.0;
        var lineNumber = 0;
        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        var rotaryMin = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var rotaryMax = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        var cutLength = 0.0;
        var rapidLength = 0.0;

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
                continue;

            var values = new Dictionary<char, double>();
            foreach (var token in Tokenize(line))
            {
                if (token.Length < 2 || !char.IsLetter(token[0]))
                    continue;

                var letter = char.ToUpperInvariant(token[0]);
                if (!double.TryParse(
                        token[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    if (letter is 'X' or 'Y' or 'Z' or 'F')
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-CAM-004",
                            DiagnosticSeverity.Error,
                            $"G-code line {lineNumber} has an invalid {letter} value.",
                            Entity: source));
                    }
                    continue;
                }

                values[letter] = value;
            }

            if (!values.TryGetValue('G', out var gCode))
            {
                if (values.TryGetValue('F', out var feedOnly))
                    feed = feedOnly * scale;
                continue;
            }

            var code = (int)gCode;
            switch (code)
            {
                case 20:
                    scale = 25.4;
                    continue;
                case 21:
                    scale = 1.0;
                    continue;
                case 90:
                    absolute = true;
                    continue;
                case 91:
                    absolute = false;
                    continue;
                case 92:
                    foreach (var axis in new[] { 'X', 'Y', 'Z' })
                    {
                        if (values.TryGetValue(axis, out var set))
                            position[AxisIndex(axis)] = set * scale;
                    }
                    foreach (var axis in new[] { 'A', 'B', 'C' })
                    {
                        if (values.TryGetValue(axis, out var set))
                            rotary[axis - 'A'] = set;
                    }
                    continue;
                case 28:
                case 30:
                    continue;
                case 2:
                case 3:
                    diagnostics.Add(new Diagnostic(
                        "EMD-CAM-004",
                        DiagnosticSeverity.Warning,
                        $"G-code line {lineNumber} uses an arc; only G0/G1 are checked.",
                        Entity: source));
                    continue;
            }

            if (code is not (0 or 1))
                continue;

            if (values.TryGetValue('F', out var feedValue))
                feed = feedValue * scale;

            var next = (double[])position.Clone();
            var nextRotary = (double[])rotary.Clone();
            foreach (var axis in new[] { 'X', 'Y', 'Z' })
            {
                if (!values.TryGetValue(axis, out var value))
                    continue;
                var index = AxisIndex(axis);
                next[index] = absolute ? value * scale : next[index] + value * scale;
            }
            foreach (var axis in new[] { 'A', 'B', 'C' })
            {
                if (!values.TryGetValue(axis, out var value))
                    continue;
                var index = axis - 'A';
                nextRotary[index] = absolute ? value : nextRotary[index] + value;
            }

            var rapid = code == 0;
            var delta = Distance(position, next);
            if (rapid)
                rapidLength += delta;
            else
                cutLength += delta;

            var start = (double[])position.Clone();
            var startRotary = (double[])rotary.Clone();
            moves.Add(new GCodeMove(lineNumber, start, next, feed, rapid)
            {
                RotaryStart = startRotary,
                RotaryEnd = nextRotary
            });
            position = next;
            rotary = nextRotary;
            for (var axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis], Math.Min(start[axis], next[axis]));
                max[axis] = Math.Max(max[axis], Math.Max(start[axis], next[axis]));
                rotaryMin[axis] = Math.Min(rotaryMin[axis],
                    Math.Min(startRotary[axis], nextRotary[axis]));
                rotaryMax[axis] = Math.Max(rotaryMax[axis],
                    Math.Max(startRotary[axis], nextRotary[axis]));
            }
        }

        if (moves.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-CAM-004",
                DiagnosticSeverity.Error,
                "G-code contains no G0/G1 motion.",
                Entity: source));
            return new GCodeLoadResult(null, diagnostics);
        }

        if (min[0] == double.PositiveInfinity)
            min = [0, 0, 0];
        if (max[0] == double.NegativeInfinity)
            max = [0, 0, 0];

        return new GCodeLoadResult(
            new GCodeProgram(moves, min, max, cutLength, rapidLength, lineNumber)
            {
                RotaryMin = rotaryMin[0] == double.PositiveInfinity ? [0, 0, 0] : rotaryMin,
                RotaryMax = rotaryMax[0] == double.NegativeInfinity ? [0, 0, 0] : rotaryMax
            },
            diagnostics);
    }

    private static int AxisIndex(char axis) => axis switch
    {
        'X' => 0,
        'Y' => 1,
        _ => 2
    };

    private static IEnumerable<string> Tokenize(string line)
    {
        var start = 0;
        for (var index = 1; index <= line.Length; index++)
        {
            if (index == line.Length || char.IsLetter(line[index]))
            {
                var token = line[start..index].Trim();
                if (token.Length > 0)
                    yield return token;
                start = index;
            }
        }
    }

    private static string StripComment(string line)
    {
        var semicolon = line.IndexOf(';');
        if (semicolon >= 0)
            line = line[..semicolon];
        var open = line.IndexOf('(');
        while (open >= 0)
        {
            var close = line.IndexOf(')', open + 1);
            if (close < 0)
                break;
            line = line[..open] + line[(close + 1)..];
            open = line.IndexOf('(', open);
        }
        return line;
    }

    private static double Distance(double[] a, double[] b)
    {
        var dx = b[0] - a[0];
        var dy = b[1] - a[1];
        var dz = b[2] - a[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public sealed class GCodeValidator
{
    public static GCodeValidator Default { get; } = new();

    public IReadOnlyList<Diagnostic> Validate(
        GCodeProgram program,
        double[]? envelopeMin = null,
        double[]? envelopeMax = null,
        double? maxFeedMmMin = null,
        double[]? rotaryMin = null,
        double[]? rotaryMax = null)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var move in program.Moves)
        {
            if (envelopeMin is { Length: >= 3 } && envelopeMax is { Length: >= 3 } &&
                (Outside(move.Start) || Outside(move.End)))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-001",
                    DiagnosticSeverity.Error,
                    $"G-code line {move.Line} leaves the work envelope."));
            }

            if (!move.Rapid && move.FeedMmMin <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-002",
                    DiagnosticSeverity.Error,
                    $"G-code line {move.Line} is a cutting move without a positive feed."));
            }

            if (rotaryMin is { Length: >= 3 } && rotaryMax is { Length: >= 3 } &&
                (move.RotaryEnd[0] < rotaryMin[0] || move.RotaryEnd[0] > rotaryMax[0] ||
                 move.RotaryEnd[1] < rotaryMin[1] || move.RotaryEnd[1] > rotaryMax[1] ||
                 move.RotaryEnd[2] < rotaryMin[2] || move.RotaryEnd[2] > rotaryMax[2]))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-011",
                    DiagnosticSeverity.Error,
                    $"G-code line {move.Line} leaves the rotary envelope."));
            }

            if (maxFeedMmMin is { } limit && move.FeedMmMin > limit)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-003",
                    DiagnosticSeverity.Error,
                    $"G-code line {move.Line} feed {move.FeedMmMin:0.###} exceeds {limit:0.###} mm/min."));
            }
        }

        return diagnostics;

        bool Outside(double[] point) =>
            point[0] < envelopeMin![0] || point[0] > envelopeMax![0] ||
            point[1] < envelopeMin[1] || point[1] > envelopeMax[1] ||
            point[2] < envelopeMin[2] || point[2] > envelopeMax[2];
    }
}

public sealed class GCodePostProcessor
{
    private static readonly System.Text.RegularExpressions.Regex AxisToken = new(
        @"([XYZABC])([-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][-+]?[0-9]+)?)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static GCodePostProcessor Default { get; } = new();

    public string Process(
        string text,
        string flavor,
        IReadOnlyDictionary<char, char>? axisMap = null)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        var (header, footer) = flavor.ToLowerInvariant() switch
        {
            "klipper" => ("; EMD postprocessor: Klipper", ""),
            "linuxcnc" => ("; EMD postprocessor: LinuxCNC", "M30"),
            "grbl" => ("; EMD postprocessor: GRBL", "M2"),
            "marlin" => ("; EMD postprocessor: Marlin", "M84"),
            "reprap" => ("; EMD postprocessor: RepRap", "M2"),
            _ => ($"; EMD postprocessor: {flavor}", "")
        };
        if (axisMap is { Count: > 0 })
            normalized = MapAxes(normalized, axisMap);
        return header + "\n" + normalized + (footer.Length == 0 ? "\n" : $"\n{footer}\n");
    }

    private static string MapAxes(string text, IReadOnlyDictionary<char, char> axisMap)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var semicolon = lines[index].IndexOf(';');
            var code = semicolon < 0 ? lines[index] : lines[index][..semicolon];
            var comment = semicolon < 0 ? "" : lines[index][semicolon..];
            code = AxisToken.Replace(code, match =>
            {
                var axis = char.ToUpperInvariant(match.Groups[1].Value[0]);
                var mapped = axisMap.TryGetValue(axis, out var target)
                    ? char.ToUpperInvariant(target)
                    : axis;
                return mapped + match.Groups[2].Value;
            });
            lines[index] = code + comment;
        }
        return string.Join('\n', lines);
    }
}
