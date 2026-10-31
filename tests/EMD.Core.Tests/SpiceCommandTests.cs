using Emd.Adapters;
using Emd.Cli;

namespace Emd.Core.Tests;

public class SpiceCommandTests
{
    [Fact]
    public void Spice_command_prints_the_log_artifact()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "axis.cir");
        var log = Path.Combine(temp.Path, "axis.log");
        File.WriteAllText(netlist, "* circuit");
        File.WriteAllText(log, "ok");
        var adapter = new NgSpiceAdapter(new FakeRunner(new ToolProcessResult(0, "", "", [])));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = SpiceCommand.Run(
            ["--netlist", netlist, "--out", log], stdout, stderr, adapter);

        Assert.Equal(0, exitCode);
        Assert.Contains("artifact:", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spice", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_output_returns_usage_error()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "axis.cir");
        File.WriteAllText(netlist, "* circuit");
        var adapter = new NgSpiceAdapter(new FakeRunner(new ToolProcessResult(0, "", "", [])));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = SpiceCommand.Run(["--netlist", netlist], stdout, stderr, adapter);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Failed_ngspice_run_returns_one()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "axis.cir");
        var log = Path.Combine(temp.Path, "axis.log");
        File.WriteAllText(netlist, "* circuit");
        var adapter = new NgSpiceAdapter(new FakeRunner(new ToolProcessResult(2, "", "boom", [])));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = SpiceCommand.Run(
            ["--netlist", netlist, "--out", log], stdout, stderr, adapter);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-SPICE-001", stderr.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeRunner : IToolProcessRunner
    {
        private readonly ToolProcessResult _result;

        public FakeRunner(ToolProcessResult result)
        {
            _result = result;
        }

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
