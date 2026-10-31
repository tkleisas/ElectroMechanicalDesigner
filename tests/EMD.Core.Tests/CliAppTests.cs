using Emd.Cli;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class CliAppTests
{
    [Fact]
    public void Validate_example_returns_zero_and_prints_a_summary()
    {
        var (exitCode, stdout, stderr) = Run("validate", TestPaths.ExampleProject);

        Assert.Equal(0, exitCode);
        Assert.Contains("valid:", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMD-", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_invalid_json_returns_one_and_reports_the_code()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bad.json");
        File.WriteAllText(path, "{ not json");

        var (exitCode, _, stderr) = Run("validate", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-JSON-001", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_mechmaker_writes_a_valid_project()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "imported.json");

        var (exitCode, stdout, stderr) = Run(
            "import", "mechmaker", TestPaths.Fixture("linear_axis.json"), "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("imported:", stdout, StringComparison.OrdinalIgnoreCase);

        var loaded = ProjectLoader.Default.LoadFile(output);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(3, loaded.Project!.Parts.Count);
    }

    [Fact]
    public void Import_kicad_writes_a_valid_project()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "netlist_import.json");

        var (exitCode, stdout, stderr) = Run(
            "import", "kicad", TestPaths.Fixture("kicad_netlist.xml"), "--out", output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("imported:", stdout, StringComparison.OrdinalIgnoreCase);

        var loaded = ProjectLoader.Default.LoadFile(output);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal(2, loaded.Project!.Parts.Count);
    }

    [Fact]
    public void Import_freecad_adds_an_artifact_to_a_project()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(MinimalProject(), projectPath);
        var stepPath = Path.Combine(temp.Path, "axis.step");
        File.WriteAllText(stepPath, "ISO-10303-21;");

        var (exitCode, stdout, stderr) = Run(
            "import", "freecad", stepPath, "--project", projectPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("added:", stdout, StringComparison.OrdinalIgnoreCase);
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        var artifact = Assert.Single(loaded.Project!.Artifacts);
        Assert.Equal(artifact.Id, loaded.Project.Mechanical!.Exports!["step"]);
    }

    [Fact]
    public void Import_without_out_returns_usage_error()
    {
        var (exitCode, _, stderr) = Run(
            "import", "mechmaker", TestPaths.Fixture("linear_axis.json"));

        Assert.Equal(2, exitCode);
        Assert.Contains("usage", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_plan_orders_artifact_dependencies()
    {
        var (exitCode, stdout, stderr) = Run("build", "plan", "--project", TestPaths.ExampleProject);

        Assert.Equal(0, exitCode);
        Assert.Contains("axis_fcstd", stdout, StringComparison.Ordinal);
        Assert.Contains("axis_step", stdout, StringComparison.Ordinal);
        Assert.True(
            stdout.IndexOf("axis_fcstd", StringComparison.Ordinal) <
            stdout.IndexOf("axis_step", StringComparison.Ordinal),
            stdout + stderr);
    }

    [Fact]
    public void Artifacts_verify_reports_a_missing_hash()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "artifact test",
            Artifacts =
            [
                new Artifact
                {
                    Id = Guid.NewGuid(),
                    Key = "missing",
                    Path = "files/missing.bin",
                    Format = "bin",
                    Role = ArtifactRole.Source,
                    Sha256 = new string('a', 64)
                }
            ],
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Name = "part",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ]
        };
        ProjectWriter.Save(project, projectPath);

        var (exitCode, _, stderr) = Run("artifacts", "verify", projectPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-ART-002", stderr, StringComparison.Ordinal);
    }

    private static EmdProject MinimalProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "CLI import test",
        Parts =
        [
            new Part
            {
                Id = Guid.NewGuid(),
                Name = "frame",
                Kind = PartKind.Component,
                Ports = []
            }
        ]
    };

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliApp.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
