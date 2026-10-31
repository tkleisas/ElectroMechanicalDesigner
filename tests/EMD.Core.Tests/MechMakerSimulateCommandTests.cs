using Emd.Adapters.MechMaker;
using Emd.Cli;

namespace Emd.Core.Tests;

public class MechMakerSimulateCommandTests
{
    [Fact]
    public async Task Simulate_command_writes_a_trace()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "trace.csv");
        var client = new FakeClient();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--machine", "machine.json", "--out", output,
             "--duration", "0.2", "--interval", "0.1", "--motor", "x_motor:1"],
            stdout,
            stderr,
            client);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output));
        Assert.Contains("simulated:", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(output);
        Assert.Equal(3, lines.Length);
        Assert.Contains("x_motor.actual_rev", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_machine_returns_usage_error()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--out", "trace.csv"], stdout, stderr, new FakeClient());

        Assert.Equal(2, exitCode);
    }

    private sealed class FakeClient : IMechMakerToolClient
    {
        private int _runs;

        public Task<MechMakerToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            if (toolName != "run_for")
                return Task.FromResult(new MechMakerToolResult(false, "ok"));

            _runs++;
            var time = _runs * 0.1;
            var status =
                $"t = {time:0.###} s\n" +
                $"x_motor: commanded {time:0.###} rev / actual {time:0.###} rev, " +
                "velocity 1 rev/s (target 1 rev/s), missed steps 0\n" +
                "endstop x_carriage: open";
            return Task.FromResult(new MechMakerToolResult(false, status));
        }
    }
}
