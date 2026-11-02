using Emd.Adapters;
using Emd.Adapters.MechMaker;

namespace Emd.Adapters.Tests;

public class RenodeUartBridgeTests
{
    [Fact]
    public async Task Bridge_drives_motors_and_records_angles()
    {
        var client = new FakeClient();
        var bridge = new RenodeUartBridge();
        using var uart = new StringReader("""
            MOTOR motor_left 1.0
            RUN 0.1
            STOP motor_left
            """);

        var result = await bridge.RunAsync(uart, client, "machine.json");

        Assert.NotNull(result.TraceCsv);
        Assert.Contains("motor_left_angle", result.TraceCsv!);
        Assert.Contains("0.1", result.TraceCsv!);
        Assert.Equal(3, result.Commands);
        Assert.Contains("open_machine", client.Calls);
        Assert.Contains("set_motor_velocity", client.Calls);
        Assert.Contains("run_for", client.Calls);
        Assert.Contains("stop_run", client.Calls);
    }

    [Fact]
    public async Task Bridge_reports_unknown_commands_as_warnings()
    {
        var client = new FakeClient();
        var bridge = new RenodeUartBridge();
        using var uart = new StringReader("NOPE\nRUN 0.1\n");

        var result = await bridge.RunAsync(uart, client, "machine.json");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-BRIDGE-002");
    }

    private sealed class FakeClient : IMechMakerToolClient
    {
        public List<string> Calls { get; } = [];

        public Task<MechMakerToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(toolName);
            return Task.FromResult(toolName == "get_run_status"
                ? new MechMakerToolResult(false,
                    "t = 0.1 s\nmotor_left: commanded 0.1 rev / actual 0.1 rev, velocity 1 rev/s, missed steps 0")
                : new MechMakerToolResult(false, "ok"));
        }
    }
}
