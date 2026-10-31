using System.Globalization;
using Emd.Core.Diagnostics;

namespace Emd.Core.Simulation;

public sealed record TraceLoadResult(
    SimulationTrace? Trace,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Trace is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class TraceCsvLoader
{
    public static TraceCsvLoader Default { get; } = new();

    public TraceLoadResult LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new TraceLoadResult(null,
            [
                new Diagnostic("EMD-TRACE-001", DiagnosticSeverity.Error,
                    $"Could not read trace '{path}': {exception.Message}", Entity: path)
            ]);
        }
    }

    public TraceLoadResult Load(string csv, string? source = null)
    {
        var lines = csv
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count < 2)
            return Failure("Trace needs a header and at least one data row.", source);

        var signals = Split(lines[0]);
        if (signals.Count == 0 || signals.Any(string.IsNullOrWhiteSpace))
            return Failure("Trace header contains an empty signal name.", source);
        if (signals.Distinct(StringComparer.Ordinal).Count() != signals.Count)
            return Failure("Trace header contains duplicate signal names.", source);

        var rows = new List<IReadOnlyDictionary<string, double>>();
        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            var fields = Split(lines[lineIndex]);
            if (fields.Count != signals.Count)
            {
                return Failure(
                    $"Trace row {lineIndex + 1} has {fields.Count} values; expected {signals.Count}.",
                    source);
            }

            var row = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var index = 0; index < signals.Count; index++)
            {
                if (!double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    return Failure($"Trace row {lineIndex + 1} has an invalid number '{fields[index]}'.", source);
                row[signals[index]] = value;
            }

            rows.Add(row);
        }

        return new TraceLoadResult(new SimulationTrace(signals, rows), []);

        static List<string> Split(string line) =>
            line.Split(',').Select(value => value.Trim()).ToList();
    }

    private static TraceLoadResult Failure(string message, string? source) =>
        new(null,
        [
            new Diagnostic("EMD-TRACE-001", DiagnosticSeverity.Error, message, Entity: source)
        ]);
}
