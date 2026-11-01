using System.Text.Json.Nodes;
using Emd.Server;

namespace Emd.Server.Tests;

public class EmdToolsTests
{
    private static string ExampleProject =>
        Path.Combine(RepositoryRoot(), "examples", "project.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "schema", "emd-project.schema.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "emd-server-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Validate_project_returns_valid_for_the_example()
    {
        var result = EmdTools.ValidateProject(ExampleProject);

        Assert.StartsWith("valid:", result);
    }

    [Fact]
    public void Project_summary_contains_domain_counts()
    {
        var json = JsonNode.Parse(EmdTools.ProjectSummary(ExampleProject))!.AsObject();

        Assert.Equal("Motorized axis MVP", (string?)json["name"]);
        Assert.True((int)json["parts"]! > 0);
        Assert.True((int)json["nets"]! > 0);
        Assert.Equal(0, (int)json["validation_errors"]!);
    }

    [Fact]
    public void Check_gcode_reports_envelope_errors()
    {
        using var temp = new TemporaryDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(gcode, "G21\nG1 X20 F500\n");

        var result = EmdTools.CheckGCode(gcode, "0,0,0,10,10,10");

        Assert.Contains("EMD-CAM-001", result);
    }

    [Fact]
    public void Package_project_writes_and_verifies()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "package");

        var result = EmdTools.PackageProject(ExampleProject, output);

        Assert.Contains("verified", result);
        Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
        Assert.Contains("\"valid\":true", EmdTools.PackageSummaryJson(output).ToJsonString());
    }

    [Fact]
    public void Web_ui_contains_the_project_viewer()
    {
        Assert.Contains("EMD project viewer", WebUi.Html);
        Assert.Contains("/api/project", WebUi.Html);
    }
}
