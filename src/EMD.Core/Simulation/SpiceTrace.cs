using System.Globalization;
using Emd.Core.Diagnostics;

namespace Emd.Core.Simulation;

public sealed record SpiceTrace(
    IReadOnlyList<string> Signals,
    IReadOnlyList<double> Times,
    IReadOnlyList<double[]> Values);

public sealed record SpiceTraceLoadResult(
    SpiceTrace? Trace,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Trace is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class SpiceTraceLoader
{
    public static SpiceTraceLoader Default { get; } = new();

    public SpiceTraceLoadResult Load(
        string text,
        IReadOnlyList<string>? expectedSignals = null,
        string? source = null)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToList();
        if (lines.Count < 2)
            return Failure("EMD-SPICE-005", "SPICE trace has no data rows.", source);

        var header = Split(lines[0]);
        var firstIsNumber = double.TryParse(
            header[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        var signals = firstIsNumber
            ? (expectedSignals ?? []).Select(Sanitize).ToList()
            : header.Skip(1).Select(Sanitize).ToList();
        if (signals.Count == 0)
            return Failure("EMD-SPICE-005", "SPICE trace has no signal columns.", source);

        var start = firstIsNumber ? 0 : 1;
        var times = new List<double>();
        var values = signals.Select(_ => new List<double>()).ToList();
        for (var lineIndex = start; lineIndex < lines.Count; lineIndex++)
        {
            var fields = Split(lines[lineIndex]);
            if (fields.Count != signals.Count + 1)
                return Failure(
                    "EMD-SPICE-005",
                    $"SPICE trace row {lineIndex + 1} has {fields.Count} columns; " +
                    $"expected {signals.Count + 1}.",
                    source);
            if (!TryNumber(fields[0], out var time))
                return Failure("EMD-SPICE-005",
                    $"SPICE trace row {lineIndex + 1} has an invalid time.", source);

            times.Add(time);
            for (var signal = 0; signal < signals.Count; signal++)
            {
                if (!TryNumber(fields[signal + 1], out var value))
                    return Failure("EMD-SPICE-005",
                        $"SPICE trace row {lineIndex + 1} has an invalid value.", source);
                values[signal].Add(value);
            }
        }

        if (times.Count < 2)
            return Failure("EMD-SPICE-005", "SPICE trace needs at least two samples.", source);

        return new SpiceTraceLoadResult(
            new SpiceTrace(
                signals,
                times,
                values.Select(column => column.ToArray()).ToArray()),
            []);
    }

    public static string Sanitize(string signal)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in signal)
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        var name = builder.ToString().Trim('_');
        if (name.Length == 0)
            name = "signal";
        if (char.IsDigit(name[0]))
            name = "spice_" + name;
        return name;
    }

    private static List<string> Split(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static SpiceTraceLoadResult Failure(string code, string message, string? source) =>
        new(null, [new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: source)]);
}
