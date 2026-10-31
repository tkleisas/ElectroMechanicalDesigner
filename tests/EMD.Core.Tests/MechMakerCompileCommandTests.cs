using Emd.Adapters;
using Emd.Cli;
using Emd.Core.Importers;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class MechMakerCompileCommandTests
{
    [Fact]
    public void Compile_command_exports_machine_json_and_writes_mjcf()
    {
        using var temp = new TempDirectory();
        var imported = MechMakerImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("linear_axis.json")), "linear_axis.json");
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(imported.Project!, projectPath);
        var output = Path.Combine(temp.Path, "scene.xml");
        var workDir = Path.Combine(temp.Path, "work");
        var runner = new OutputCreatingRunner();
        var adapter = new MechMakerCompileAdapter(runner, "dotnet", ["fake-mechmaker.dll"]);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = MechMakerCompileCommand.Run(
            ["--project", projectPath, "--out", output, "--work-dir", workDir],
            stdout,
            stderr,
            adapter);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output));
        Assert.True(File.Exists(Path.Combine(workDir, "machine.json")));
        Assert.Contains("parts", File.ReadAllText(Path.Combine(workDir, "machine.json")), StringComparison.Ordinal);
        Assert.Contains("compiled:", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(output, runner.Arguments[^1]);
    }

    [Fact]
    public void Missing_output_returns_usage_error()
    {
        using var temp = new TempDirectory();
        var imported = MechMakerImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("linear_axis.json")), "linear_axis.json");
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(imported.Project!, projectPath);
        var adapter = new MechMakerCompileAdapter(new OutputCreatingRunner(), "dotnet");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = MechMakerCompileCommand.Run(
            ["--project", projectPath], stdout, stderr, adapter);

        Assert.Equal(2, exitCode);
    }

    private sealed class OutputCreatingRunner : IToolProcessRunner
    {
        public string[] Arguments { get; private set; } = [];

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = (arguments ?? []).ToArray();
            var output = Arguments[^1];
            File.WriteAllText(output, "<mujoco model=\"test\"/>");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }
}
