using Emd.Adapters;
using Emd.Cli;

namespace Emd.Core.Tests;

public class CamCommandTests
{
    [Fact]
    public void Verify_runs_builtin_checks()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(gcode, "G21\nG1 X5 Y5 F500\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["verify", "--gcode", gcode, "--envelope", "0,0,0,10,10,10"],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(0, exitCode);
        Assert.Contains("cam verification: valid", stdout.ToString());
    }

    [Fact]
    public void Verify_rejects_out_of_envelope_paths()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(gcode, "G21\nG1 X20 F500\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["verify", "--gcode", gcode, "--envelope", "0,0,0,10,10,10"],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-CAM-001", stderr.ToString());
    }

    private sealed class Runner : IToolProcessRunner
    {
        private readonly string? _output;
        public Runner(string? output) { _output = output; }
        public Task<ToolProcessResult> RunAsync(
            string executable, IEnumerable<string>? arguments = null, string? stdin = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (_output is not null)
                File.WriteAllText(_output, "solid");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }
}
