using System.Globalization;
using System.Text;
using Emd.Core.Diagnostics;

namespace Emd.Adapters.MechMaker;

public sealed record MechMakerMotorCommand(
    string Component,
    double RevPerSec,
    bool Enable = true);

public sealed record MechMakerRunOptions(
    string MachinePath,
    double DurationSeconds,
    double SampleIntervalSeconds = 0.1,
    int Microsteps = 16,
    IReadOnlyList<MechMakerMotorCommand>? Motors = null);

public sealed record MechMakerRunResult(
    string? TraceCsv,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => TraceCsv is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Runs a MechMaker session through MCP and records a CSV trace.</summary>
public sealed class MechMakerSimulationRecorder
{
    private readonly IMechMakerToolClient _client;

    public MechMakerSimulationRecorder(IMechMakerToolClient client)
    {
        _client = client;
    }

    public async Task<MechMakerRunResult> RecordAsync(
        MechMakerRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        if (options.DurationSeconds <= 0)
            return Failure("EMD-MECHMAKER-020", "Run duration must be positive.");
        if (options.SampleIntervalSeconds <= 0 || options.SampleIntervalSeconds > 10)
            return Failure("EMD-MECHMAKER-020", "Sample interval must be in (0, 10] seconds.");

        var open = await CallAsync(
            "open_machine",
            new Dictionary<string, object?> { ["path"] = options.MachinePath },
            cancellationToken);
        if (open.IsError)
            return Failure("EMD-MECHMAKER-020", $"open_machine failed: {open.Text}");

        var start = await CallAsync(
            "start_run",
            new Dictionary<string, object?> { ["microsteps"] = options.Microsteps },
            cancellationToken);
        if (start.IsError)
            return Failure("EMD-MECHMAKER-020", $"start_run failed: {start.Text}");

        try
        {
            foreach (var motor in options.Motors ?? [])
            {
                if (motor.Enable)
                {
                    var enable = await CallAsync(
                        "enable_motor",
                        new Dictionary<string, object?>
                        {
                            ["instanceId"] = motor.Component,
                            ["enabled"] = true
                        },
                        cancellationToken);
                    if (enable.IsError)
                        return Failure("EMD-MECHMAKER-020", $"enable_motor failed: {enable.Text}");
                }

                var velocity = await CallAsync(
                    "set_motor_velocity",
                    new Dictionary<string, object?>
                    {
                        ["instanceId"] = motor.Component,
                        ["revPerSec"] = motor.RevPerSec
                    },
                    cancellationToken);
                if (velocity.IsError)
                    return Failure("EMD-MECHMAKER-020", $"set_motor_velocity failed: {velocity.Text}");
            }

            List<string>? signals = null;
            var rows = new List<Dictionary<string, double>>();
            var iterations = (int)Math.Ceiling(options.DurationSeconds / options.SampleIntervalSeconds);

            for (var index = 0; index < iterations; index++)
            {
                var run = await CallAsync(
                    "run_for",
                    new Dictionary<string, object?> { ["seconds"] = options.SampleIntervalSeconds },
                    cancellationToken);
                if (run.IsError)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECHMAKER-020",
                        DiagnosticSeverity.Error,
                        $"run_for failed: {run.Text}"));
                    break;
                }

                if (!MechMakerStatusParser.TryParse(run.Text, out var status, out var parseError) || status is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECHMAKER-021",
                        DiagnosticSeverity.Error,
                        $"Could not parse MechMaker status: {parseError}"));
                    break;
                }

                if (signals is null)
                {
                    signals = ["time"];
                    foreach (var motor in status.Motors)
                    {
                        signals.Add($"{motor.Id}.commanded_rev");
                        signals.Add($"{motor.Id}.actual_rev");
                    }

                    foreach (var endstop in status.Endstops)
                        signals.Add($"endstop.{endstop.Joint}");
                }

                var row = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["time"] = status.Time
                };
                foreach (var motor in status.Motors)
                {
                    row[$"{motor.Id}.commanded_rev"] = motor.CommandedRev;
                    row[$"{motor.Id}.actual_rev"] = motor.ActualRev;
                }

                foreach (var endstop in status.Endstops)
                    row[$"endstop.{endstop.Joint}"] = endstop.Pressed ? 1 : 0;

                rows.Add(row);
            }

            if (rows.Count == 0 || signals is null)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-020",
                    DiagnosticSeverity.Error,
                    "MechMaker produced no trace rows."));
                return new MechMakerRunResult(null, diagnostics);
            }

            return new MechMakerRunResult(BuildCsv(signals, rows), diagnostics);
        }
        finally
        {
            var stop = await CallAsync("stop_run", null, cancellationToken);
            if (stop.IsError)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-022",
                    DiagnosticSeverity.Warning,
                    $"stop_run failed: {stop.Text}"));
            }
        }
    }

    private async Task<MechMakerToolResult> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CallToolAsync(tool, arguments, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MechMakerToolResult(true, exception.Message);
        }
    }

    private static string BuildCsv(
        IReadOnlyList<string> signals,
        IReadOnlyList<Dictionary<string, double>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", signals));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", signals.Select(signal =>
                row.TryGetValue(signal, out var value)
                    ? value.ToString("0.######", CultureInfo.InvariantCulture)
                    : "0")));
        }

        return builder.ToString();
    }

    private static MechMakerRunResult Failure(string code, string message) =>
        new(null,
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message)
        ]);
}
