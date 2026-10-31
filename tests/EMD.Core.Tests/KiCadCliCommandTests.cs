using Emd.Adapters;
using Emd.Cli;

namespace Emd.Core.Tests;

public class KiCadCliCommandTests
{
    [Fact]
    public void Netlist_command_prints_the_generated_artifact()
    {
        using var temp = new TempDirectory();
        var schematic = Path.Combine(temp.Path, "axis.kicad_sch");
        var netlist = Path.Combine(temp.Path, "axis.net");
        File.WriteAllText(schematic, "schematic");
        File.WriteAllText(netlist, "netlist");
        var runner = new FakeRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new KiCadCliAdapter(runner);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = KiCadCliCommand.Run(
            ["netlist", "--schematic", schematic, "--out", netlist], stdout, stderr, adapter);

        Assert.Equal(0, exitCode);
        Assert.Contains("netlist", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artifact:", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["sch", "export", "netlist", "--format", "kicadxml", "--output", netlist, schematic],
            runner.Arguments);
    }

    [Fact]
    public void Missing_output_returns_usage_error()
    {
        using var temp = new TempDirectory();
        var schematic = Path.Combine(temp.Path, "axis.kicad_sch");
        File.WriteAllText(schematic, "schematic");
        var adapter = new KiCadCliAdapter(new FakeRunner(new ToolProcessResult(0, "", "", [])));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = KiCadCliCommand.Run(
            ["erc", "--schematic", schematic], stdout, stderr, adapter);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Failed_kicad_command_returns_one()
    {
        using var temp = new TempDirectory();
        var schematic = Path.Combine(temp.Path, "axis.kicad_sch");
        var report = Path.Combine(temp.Path, "erc.rpt");
        File.WriteAllText(schematic, "schematic");
        var adapter = new KiCadCliAdapter(new FakeRunner(new ToolProcessResult(2, "", "boom", [])));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = KiCadCliCommand.Run(
            ["erc", "--schematic", schematic, "--out", report], stdout, stderr, adapter);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-KICAD-010", stderr.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeRunner : IToolProcessRunner
    {
        private readonly ToolProcessResult _result;

        public FakeRunner(ToolProcessResult result)
        {
            _result = result;
        }

        public string[] Arguments { get; private set; } = [];

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = (arguments ?? []).ToArray();
            return Task.FromResult(_result);
        }
    }
}
