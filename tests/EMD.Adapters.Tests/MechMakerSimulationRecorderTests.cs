using Emd.Adapters.MechMaker;
using Emd.Core.Diagnostics;

namespace Emd.Adapters.Tests;

public class MechMakerSimulationRecorderTests
{
    [Fact]
    public async Task Records_a_status_trace_and_commands_the_motor()
    {
        var client = new FakeClient();
        var recorder = new MechMakerSimulationRecorder(client);

        var result = await recorder.RecordAsync(new MechMakerRunOptions(
            "machine.json",
            DurationSeconds: 0.3,
            SampleIntervalSeconds: 0.1,
            Motors: [new MechMakerMotorCommand("x_motor", 1.0)]));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var lines = result.TraceCsv!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal("time,x_motor.commanded_rev,x_motor.actual_rev,endstop.x_carriage", lines[0]);
        Assert.StartsWith("0.1,0.1,0.09,0", lines[1]);
        Assert.Equal(
            ["open_machine", "start_run", "enable_motor", "set_motor_velocity", "run_for", "run_for", "run_for", "stop_run"],
            client.Calls.Select(call => call.Tool));
        Assert.Equal("machine.json", client.Calls[0].Arguments!["path"]);
    }

    [Fact]
    public async Task Open_machine_failure_is_reported_as_EMD_MECHMAKER_020()
    {
        var client = new FakeClient { FailTool = "open_machine" };
        var recorder = new MechMakerSimulationRecorder(client);

        var result = await recorder.RecordAsync(new MechMakerRunOptions("missing.json", 0.1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MECHMAKER-020");
    }

    private sealed class FakeClient : IMechMakerToolClient
    {
        private int _runCount;

        public string? FailTool { get; init; }

        public List<(string Tool, IReadOnlyDictionary<string, object?>? Arguments)> Calls { get; } = [];

        public Task<MechMakerToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((toolName, arguments));
            if (toolName == FailTool)
                return Task.FromResult(new MechMakerToolResult(true, "failed"));
            if (toolName != "run_for")
                return Task.FromResult(new MechMakerToolResult(false, "ok"));

            _runCount++;
            var time = _runCount * 0.1;
            var actual = time * 0.9;
            var statusText =
                $"t = {time:0.###} s\n" +
                $"x_motor: commanded {time:0.###} rev / actual {actual:0.###} rev, " +
                "velocity 1 rev/s (target 1 rev/s), missed steps 0\n" +
                "endstop x_carriage: open";
            return Task.FromResult(new MechMakerToolResult(false, statusText));
        }
    }
}
