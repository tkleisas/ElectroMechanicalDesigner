using Emd.Core.Diff;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class ProjectDifferTests
{
    [Fact]
    public void Identical_projects_have_no_changes()
    {
        var project = LoadExample();

        var diff = ProjectDiffer.Default.Compare(project, project);

        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void Added_part_is_reported()
    {
        var before = LoadExample();
        var after = before with
        {
            Parts =
            [
                .. before.Parts,
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "extra",
                    Name = "Extra part",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ]
        };

        var diff = ProjectDiffer.Default.Compare(before, after);

        var change = Assert.Single(diff.Changes);
        Assert.Equal("added", change.Kind);
        Assert.Equal("part", change.EntityType);
        Assert.Equal("extra", change.Key);
    }

    [Fact]
    public void Removed_connection_is_reported()
    {
        var before = LoadExample();
        var after = before with
        {
            Connections = before.Connections
                .Where(connection => connection.Key != "driver_phase_a")
                .ToList()
        };

        var diff = ProjectDiffer.Default.Compare(before, after);

        Assert.Contains(diff.Changes, change =>
            change.Kind == "removed" &&
            change.EntityType == "connection" &&
            change.Key == "driver_phase_a");
    }

    [Fact]
    public void Changed_part_is_reported()
    {
        var before = LoadExample();
        var after = before with
        {
            Parts = before.Parts
                .Select(part => part.Key == "x_motor" ? part with { Name = "Changed motor" } : part)
                .ToList()
        };

        var diff = ProjectDiffer.Default.Compare(before, after);

        var change = Assert.Single(diff.Changes);
        Assert.Equal("changed", change.Kind);
        Assert.Equal("part", change.EntityType);
        Assert.Equal("x_motor", change.Key);
        Assert.Contains("Changed motor", change.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_artifact_is_reported()
    {
        var before = LoadExample();
        var after = before with
        {
            Artifacts = before.Artifacts
                .Select(artifact => artifact.Key == "axis_step"
                    ? artifact with { Sha256 = new string('b', 64) }
                    : artifact)
                .ToList()
        };

        var diff = ProjectDiffer.Default.Compare(before, after);

        Assert.Contains(diff.Changes, change =>
            change.Kind == "changed" &&
            change.EntityType == "artifact" &&
            change.Key == "axis_step");
    }

    private static EmdProject LoadExample()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        return loaded.Project!;
    }
}
