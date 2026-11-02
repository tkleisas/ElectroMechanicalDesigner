using Emd.Adapters;
using Emd.Adapters.MechMaker;
using Emd.Core.Manufacturing;

namespace Emd.Adapters.Tests;

public class KlipperUartBridgeTests
{
    [Fact]
    public async Task Bridge_forwards_commands_and_acks()
    {
        var client = new FakeClient();
        var input = new MemoryStream();
        input.Write(KlipperWire.Frame(KlipperWire.EncodeVlqAll(4, 4), 0));
        input.Position = 0;
        var output = new MemoryStream();

        var result = await new KlipperUartBridge().RunAsync(
            input, output, client, "machine.json");

        Assert.Contains("allocate_oids", client.Commands);
        Assert.Contains("motor_left_angle", result.TraceCsv!);
        output.Position = 0;
        var parser = new KlipperBlockParser();
        parser.Feed(output.ToArray());
        Assert.NotNull(parser.TryParse());
    }

    private sealed class FakeClient : IMechMakerToolClient
    {
        public List<string> Commands { get; } = [];

        public Task<MechMakerToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            if (toolName == "klipper_send")
                Commands.Add((string)arguments!["command"]!);
            if (toolName == "get_run_status")
                return Task.FromResult(new MechMakerToolResult(false,
                    "t = 0.1 s\nmotor_left: commanded 0.1 rev / actual 0.1 rev, velocity 1 rev/s, missed steps 0"));
            return Task.FromResult(new MechMakerToolResult(false,
                toolName == "klipper_send" ? "ack" : "ok"));
        }
    }
}
