using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters.Tests;

public class NgSpiceAdapterTests
{
    [Fact]
    public async Task Run_builds_a_batch_command_and_returns_the_log()
    {
        using var temp = new TempFixture();
        var netlist = temp.File("axis.cir", "* circuit");
        var log = temp.File("axis.log", "ok");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new NgSpiceAdapter(runner);

        var result = await adapter.RunAsync(netlist, log);

        Assert.True(result.Success);
        Assert.Equal("ngspice", runner.Executable);
        Assert.Equal(["-b", "-o", log, netlist], runner.Arguments);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("spice-log", artifact.Format);
        Assert.Equal(ArtifactRole.Report, artifact.Role);
    }

    [Fact]
    public async Task Nonzero_exit_reports_EMD_SPICE_001()
    {
        using var temp = new TempFixture();
        var netlist = temp.File("axis.cir", "* circuit");
        var log = Path.Combine(temp.Path, "axis.log");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(2, "", "ngspice failed", []));
        var adapter = new NgSpiceAdapter(runner);

        var result = await adapter.RunAsync(netlist, log);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EMD-SPICE-001" &&
            diagnostic.Message.Contains("ngspice failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_log_reports_EMD_SPICE_002()
    {
        using var temp = new TempFixture();
        var netlist = temp.File("axis.cir", "* circuit");
        var log = Path.Combine(temp.Path, "never.log");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new NgSpiceAdapter(runner);

        var result = await adapter.RunAsync(netlist, log);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SPICE-002");
    }

    [Fact]
    public async Task Runner_diagnostics_are_passed_through()
    {
        using var temp = new TempFixture();
        var netlist = temp.File("axis.cir", "* circuit");
        var log = temp.File("axis.log", "ok");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(
            -1, "", "",
            [new Diagnostic("EMD-ADAPTER-002", DiagnosticSeverity.Error, "timed out")]));
        var adapter = new NgSpiceAdapter(runner);

        var result = await adapter.RunAsync(netlist, log);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-002");
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "emd-ngspice-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
