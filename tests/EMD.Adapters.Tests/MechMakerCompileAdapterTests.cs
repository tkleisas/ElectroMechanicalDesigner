using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters.Tests;

public class MechMakerCompileAdapterTests
{
    [Fact]
    public async Task Compile_builds_the_expected_command_and_returns_mjcf()
    {
        using var temp = new TempFixture();
        var machine = temp.File("machine.json", "{}");
        var output = temp.File("scene.xml", "<mujoco/>");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new MechMakerCompileAdapter(
            runner,
            "dotnet",
            ["run", "--project", "../MechMaker/src/MechMaker.Cli", "--"]);

        var result = await adapter.CompileAsync(machine, output);

        Assert.True(result.Success);
        Assert.Equal("dotnet", runner.Executable);
        Assert.Equal(
            ["run", "--project", "../MechMaker/src/MechMaker.Cli", "--", machine, output],
            runner.Arguments);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("mjcf", artifact.Format);
        Assert.Equal(ArtifactRole.Simulation, artifact.Role);
    }

    [Fact]
    public async Task Nonzero_exit_reports_EMD_MECHMAKER_010()
    {
        using var temp = new TempFixture();
        var machine = temp.File("machine.json", "{}");
        var output = Path.Combine(temp.Path, "scene.xml");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(2, "", "compile failed", []));
        var adapter = new MechMakerCompileAdapter(runner, "dotnet");

        var result = await adapter.CompileAsync(machine, output);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EMD-MECHMAKER-010" &&
            diagnostic.Message.Contains("compile failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_output_reports_EMD_MECHMAKER_011()
    {
        using var temp = new TempFixture();
        var machine = temp.File("machine.json", "{}");
        var output = Path.Combine(temp.Path, "never.xml");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, "", "", []));
        var adapter = new MechMakerCompileAdapter(runner, "dotnet");

        var result = await adapter.CompileAsync(machine, output);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MECHMAKER-011");
    }

    [Fact]
    public async Task Runner_diagnostics_are_passed_through()
    {
        using var temp = new TempFixture();
        var machine = temp.File("machine.json", "{}");
        var output = temp.File("scene.xml", "<mujoco/>");
        var runner = new FakeToolProcessRunner(new ToolProcessResult(
            -1, "", "",
            [new Diagnostic("EMD-ADAPTER-002", DiagnosticSeverity.Error, "timed out")]));
        var adapter = new MechMakerCompileAdapter(runner, "dotnet");

        var result = await adapter.CompileAsync(machine, output);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-002");
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "emd-mechmaker-tests",
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
