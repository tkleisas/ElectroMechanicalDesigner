using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters.Tests;

public class KiCadCliAdapterTests
{
    [Fact]
    public async Task Erc_builds_the_expected_command_and_returns_a_report()
    {
        using var temp = new DirectoryFixture();
        var schematic = temp.File("axis.kicad_sch", "schematic");
        var report = temp.File("erc.rpt", "report");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.RunErcAsync(schematic, report);

        Assert.True(result.Success);
        Assert.Equal("kicad-cli", runner.Executable);
        Assert.Equal(["sch", "erc", "--output", report, schematic], runner.Arguments);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("kicad-erc", artifact.Format);
        Assert.Equal(ArtifactRole.Report, artifact.Role);
    }

    [Fact]
    public async Task Netlist_export_uses_the_kicadxml_format()
    {
        using var temp = new DirectoryFixture();
        var schematic = temp.File("axis.kicad_sch", "schematic");
        var netlist = temp.File("axis.net", "netlist");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.ExportNetlistAsync(schematic, netlist);

        Assert.True(result.Success);
        Assert.Equal(
            ["sch", "export", "netlist", "--format", "kicadxml", "--output", netlist, schematic],
            runner.Arguments);
        Assert.Equal("netlist", Assert.Single(result.Artifacts).Format);
    }

    [Fact]
    public async Task Gerber_export_lists_the_generated_files()
    {
        using var temp = new DirectoryFixture();
        var pcb = temp.File("axis.kicad_pcb", "pcb");
        var directory = temp.CreateDirectory("gerbers");
        temp.File("gerbers/axis-F_Cu.gbr", "copper");
        temp.File("gerbers/axis.drl", "drill");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.ExportGerbersAsync(pcb, directory);

        Assert.True(result.Success);
        Assert.Equal(2, result.Artifacts.Count);
        Assert.Contains(result.Artifacts, artifact => artifact.Format == "gerber");
        Assert.Contains(result.Artifacts, artifact => artifact.Format == "drill");
        Assert.Equal(["pcb", "export", "gerbers", "--output", directory, pcb], runner.Arguments);
    }

    [Fact]
    public async Task Nonzero_exit_reports_EMD_KICAD_010()
    {
        using var temp = new DirectoryFixture();
        var schematic = temp.File("axis.kicad_sch", "schematic");
        var report = Path.Combine(temp.Path, "erc.rpt");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(2, "", "erc failed", []));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.RunErcAsync(schematic, report);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EMD-KICAD-010" &&
            diagnostic.Message.Contains("erc failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_output_reports_EMD_KICAD_011()
    {
        using var temp = new DirectoryFixture();
        var schematic = temp.File("axis.kicad_sch", "schematic");
        var report = Path.Combine(temp.Path, "never-created.rpt");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.RunErcAsync(schematic, report);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-011");
    }

    [Fact]
    public async Task Runner_diagnostics_are_passed_through()
    {
        using var temp = new DirectoryFixture();
        var schematic = temp.File("axis.kicad_sch", "schematic");
        var report = temp.File("erc.rpt", "report");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(
            -1, "", "",
            [new Diagnostic("EMD-ADAPTER-002", DiagnosticSeverity.Error, "timed out")]));
        var adapter = new KiCadCliAdapter(runner);

        var result = await adapter.RunErcAsync(schematic, report);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-002");
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "emd-kicad-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public string CreateDirectory(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            System.IO.Directory.CreateDirectory(path);
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
