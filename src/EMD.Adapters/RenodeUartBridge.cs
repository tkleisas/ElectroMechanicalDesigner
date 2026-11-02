using System.Globalization;
using System.Text;
using Emd.Adapters.MechMaker;
using Emd.Core.Diagnostics;
using Emd.Core.Manufacturing;

namespace Emd.Adapters;

public sealed record RenodeBridgeResult(
    string? TraceCsv,
    IReadOnlyList<Diagnostic> Diagnostics,
    int Commands);

public sealed class RenodeUartBridge
{
    public async Task<RenodeBridgeResult> RunAsync(
        TextReader uart,
        IMechMakerToolClient client,
        string machinePath,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            await Call(client, "open_machine", new Dictionary<string, object?>
            {
                ["path"] = machinePath
            }, diagnostics, cancellationToken);
            await Call(client, "start_run", new Dictionary<string, object?>
            {
                ["microsteps"] = 16
            }, diagnostics, cancellationToken);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-BRIDGE-001", DiagnosticSeverity.Error,
                $"Could not start MechMaker: {exception.Message}"));
            return new RenodeBridgeResult(null, diagnostics, 0);
        }

        var rows = new List<(double Time, IReadOnlyList<MechMakerMotorStatus> Motors)>();
        var commands = 0;
        try
        {
            string? line;
            while ((line = await uart.ReadLineAsync(cancellationToken)) is not null)
            {
                if (!FirmwareCommandParser.TryParse(line, out var command, out var error) ||
                    command is null)
                {
                    if (error is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-BRIDGE-002", DiagnosticSeverity.Warning,
                            $"Ignored firmware command: {error}"));
                    }
                    continue;
                }

                commands++;
                switch (command.Kind)
                {
                    case "motor":
                        await Call(client, "enable_motor", Args(
                            ("instanceId", command.Target), ("enabled", true)), diagnostics, cancellationToken);
                        await Call(client, "set_motor_velocity", Args(
                            ("instanceId", command.Target), ("revPerSec", command.Value)), diagnostics, cancellationToken);
                        break;
                    case "enable":
                        await Call(client, "enable_motor", Args(
                            ("instanceId", command.Target), ("enabled", command.Value != 0)), diagnostics, cancellationToken);
                        break;
                    case "stop":
                        await Call(client, "enable_motor", Args(
                            ("instanceId", command.Target), ("enabled", false)), diagnostics, cancellationToken);
                        break;
                    case "run":
                        await Call(client, "run_for", Args(
                            ("seconds", command.Value)), diagnostics, cancellationToken);
                        var statusText = await Call(client, "get_run_status", null,
                            diagnostics, cancellationToken);
                        if (MechMakerStatusParser.TryParse(statusText, out var status, out var statusError) &&
                            status is not null)
                        {
                            rows.Add((status.Time, status.Motors));
                        }
                        else
                        {
                            diagnostics.Add(new Diagnostic(
                                "EMD-BRIDGE-003", DiagnosticSeverity.Warning,
                                $"Could not parse MechMaker status: {statusError}"));
                        }
                        break;
                }
            }
        }
        finally
        {
            try
            {
                await Call(client, "stop_run", null, diagnostics, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best effort cleanup.
            }
        }

        return new RenodeBridgeResult(BuildTrace(rows), diagnostics, commands);
    }

    private static string? BuildTrace(
        IReadOnlyList<(double Time, IReadOnlyList<MechMakerMotorStatus> Motors)> rows)
    {
        if (rows.Count == 0)
            return null;
        var names = rows.SelectMany(row => row.Motors.Select(motor => motor.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var builder = new StringBuilder("time");
        foreach (var name in names)
            builder.Append(',').Append(name).Append("_angle");
        builder.AppendLine();
        foreach (var row in rows)
        {
            builder.Append(row.Time.ToString("0.######", CultureInfo.InvariantCulture));
            foreach (var name in names)
            {
                var motor = row.Motors.FirstOrDefault(m => m.Id == name);
                builder.Append(',').Append(
                    (motor?.ActualRev ?? 0).ToString("R", CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static async Task<string> Call(
        IMechMakerToolClient client,
        string tool,
        IReadOnlyDictionary<string, object?>? arguments,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken);
        if (result.IsError)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-BRIDGE-004", DiagnosticSeverity.Error,
                $"MechMaker tool '{tool}' failed: {result.Text}"));
        }
        return result.Text;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value);
}
