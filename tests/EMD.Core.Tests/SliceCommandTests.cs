using Emd.Adapters;
using Emd.Cli;

namespace Emd.Core.Tests;

public class SliceCommandTests
{
    [Fact]
    public void Slice_writes_and_validates_gcode()
    {
        using var temp = new TempDirectory();
        var model = Path.Combine(temp.Path, "part.stl");
        var output = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(model, "solid part");
        var runner = new Runner(output);
        var adapter = new SlicerAdapter(runner);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = SliceCommand.Run(
            ["--model", model, "--out", output, "--slicer", "orca",
             "--envelope", "0,0,0,10,10,10"], stdout, stderr, adapter);

        Assert.True(exitCode == 0, stderr.ToString());
        Assert.True(File.Exists(output));
        Assert.Contains("sliced:", stdout.ToString());
    }

    [Fact]
    public void Slice_fails_when_gcode_exceeds_the_envelope()
    {
        using var temp = new TempDirectory();
        var model = Path.Combine(temp.Path, "part.stl");
        var output = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(model, "solid part");
        var runner = new Runner(output, "G21\nG1 X20 Y0 F500\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = SliceCommand.Run(
            ["--model", model, "--out", output, "--slicer", "orca",
             "--envelope", "0,0,0,10,10,10"],
            stdout, stderr, new SlicerAdapter(runner));

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-CAM-001", stderr.ToString());
    }

    private sealed class Runner : IToolProcessRunner
    {
        private readonly string _output;
        private readonly string _gcode;

        public Runner(string output, string gcode = "G21\nG90\nG1 X1 Y1 F500\n")
        {
            _output = output;
            _gcode = gcode;
        }

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(_output, _gcode);
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }
}
