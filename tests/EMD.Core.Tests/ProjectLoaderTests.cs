using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class ProjectLoaderTests
{
    [Fact]
    public void Load_valid_example_returns_a_project_without_diagnostics()
    {
        var result = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Project);
        Assert.Equal("Motorized axis MVP", result.Project!.Name);
        Assert.Equal("GPL-3.0-only", result.Project.License);
        Assert.Equal(5, result.Project.Parts.Count);
        Assert.Equal(18, result.Project.Connections.Count);
        Assert.NotNull(result.Project.Electrical);
        Assert.Equal(8, result.Project.Electrical!.Nets.Count);
        Assert.NotNull(result.Project.Firmware);
        Assert.Single(result.Project.Firmware!.Projects);
    }

    [Fact]
    public void Invalid_json_returns_EMD_JSON_001()
    {
        var result = ProjectLoader.Default.Load("{ this is not json");

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-JSON-001");
    }

    [Fact]
    public void Schema_invalid_project_returns_EMD_SCHEMA_001()
    {
        var json = File.ReadAllText(TestPaths.ExampleProject)
            .Replace("\"name\": \"Motorized axis MVP\"", "\"name\": 42", StringComparison.Ordinal);

        var result = ProjectLoader.Default.Load(json);

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-SCHEMA-001");
    }

    [Fact]
    public void Example_round_trips_through_the_model()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));

        var json = ProjectWriter.Serialize(loaded.Project!);
        var reloaded = ProjectLoader.Default.Load(json);

        Assert.True(reloaded.IsValid, string.Join(Environment.NewLine, reloaded.Diagnostics));
        Assert.Equal(loaded.Project!.Parts.Count, reloaded.Project!.Parts.Count);
        Assert.Equal(loaded.Project.Connections.Count, reloaded.Project.Connections.Count);
        Assert.Equal(loaded.Project.Electrical!.Nets.Count, reloaded.Project.Electrical!.Nets.Count);
        Assert.Equal(loaded.Project.Artifacts.Count, reloaded.Project.Artifacts.Count);
    }

    [Fact]
    public void Unknown_top_level_property_is_rejected()
    {
        var json = File.ReadAllText(TestPaths.ExampleProject);
        json = json.Insert(1, "\"bogus\": true,");

        var result = ProjectLoader.Default.Load(json);

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-SCHEMA-001");
    }
}
