using System.Text;
using Emd.Adapters.MechMaker;
using Emd.Core.Diagnostics;
using Emd.Core.Manufacturing;

namespace Emd.Adapters;

public sealed record KlipperBridgeResult(
    string? TraceCsv,
    IReadOnlyList<Diagnostic> Diagnostics,
    int Commands);

/// <summary>
/// Forwards Klipper wire blocks from a UART/socket into MechMaker's MCP Klipper
/// session and writes MCU acknowledgements/responses back to the transport.
/// </summary>
public sealed class KlipperUartBridge
{
    public async Task<KlipperBridgeResult> RunAsync(
        Stream input,
        Stream output,
        IMechMakerToolClient client,
        string machinePath,
        double runStep = 0.01,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        var parser = new KlipperBlockParser();
        var buffer = new byte[4096];
        var commands = 0;
        string? statusText = null;
        try
        {
            await Call(client, "open_machine", Args(("path", Path.GetFullPath(machinePath))),
                diagnostics, cancellationToken);
            var connect = await Call(client, "klipper_connect", null, diagnostics, cancellationToken);
            if (connect.Contains("error", StringComparison.OrdinalIgnoreCase))
                return new KlipperBridgeResult(null, diagnostics, 0);

            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;
                parser.Feed(buffer.AsSpan(0, read).ToArray());
                while (parser.TryParse() is { } block)
                {
                    commands++;
                    var values = KlipperWire.DecodeVlqAll(block.Content);
                    if (values.Count == 0)
                        continue;
                    if (!KlipperWire.CommandNames.TryGetValue((int)values[0], out var name))
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-KLIPPER-001", DiagnosticSeverity.Warning,
                            $"Unknown Klipper command id {values[0]}."));
                        continue;
                    }

                    var args = values.Skip(1).Select(value => (double)value).ToArray();
                    var response = await Call(client, "klipper_send",
                        Args(("command", name), ("args", args)), diagnostics, cancellationToken);
                    await ForwardResponse(output, block.Sequence, response, cancellationToken);

                    if (runStep > 0)
                        await Call(client, "run_for", Args(("seconds", runStep)),
                            diagnostics, cancellationToken);
                }
            }
            statusText = await Call(client, "get_run_status", null, diagnostics, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-KLIPPER-002", DiagnosticSeverity.Error,
                $"Klipper bridge failed: {exception.Message}"));
        }
        finally
        {
            try { await Call(client, "stop_run", null, diagnostics, CancellationToken.None); }
            catch (Exception) { }
        }

        return new KlipperBridgeResult(
            BuildTrace(statusText),
            diagnostics,
            commands);
    }

    private static async Task ForwardResponse(
        Stream output,
        byte hostSequence,
        string response,
        CancellationToken cancellationToken)
    {
        foreach (var raw in response.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line == "ack")
            {
                await output.WriteAsync(KlipperWire.Frame([], (byte)((hostSequence + 1) & 0x0f)), cancellationToken);
            }
            else if (line == "nak")
            {
                await output.WriteAsync(KlipperWire.Frame([], (byte)((hostSequence - 1) & 0x0f)), cancellationToken);
            }
            else if (line.StartsWith("response ", StringComparison.Ordinal))
            {
                var values = line["response ".Length..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => long.TryParse(value, out var number) ? number : (long?)null)
                    .ToArray();
                if (values.Any(value => value is null))
                    continue;
                var content = KlipperWire.EncodeVlqAll(values.Select(value => value!.Value).ToArray());
                await output.WriteAsync(
                    KlipperWire.Frame(content, (byte)((hostSequence + 1) & 0x0f)),
                    cancellationToken);
            }
        }
        await output.FlushAsync(cancellationToken);
    }

    private static string? BuildTrace(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText) ||
            !MechMakerStatusParser.TryParse(statusText, out var status, out _) || status is null)
            return null;
        var builder = new StringBuilder("time");
        foreach (var motor in status.Motors)
            builder.Append(',').Append(motor.Id).Append("_angle");
        builder.AppendLine();
        builder.Append(status.Time.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var motor in status.Motors)
            builder.Append(',').Append(motor.ActualRev.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine();
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
                "EMD-KLIPPER-002", DiagnosticSeverity.Error,
                $"MechMaker tool '{tool}' failed: {result.Text}"));
        }
        return result.Text;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value);
}
