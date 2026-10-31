using Emd.Core.Artifacts;
using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class FreeCadArtifactImporterTests
{
    [Fact]
    public void Fcstd_is_added_as_the_mechanical_source()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "axis.FCStd");
        File.WriteAllText(path, "fake freecad document");

        var result = FreeCadArtifactImporter.Default.AddReference(CreateProject(), path);

        Assert.Empty(result.Diagnostics);
        var artifact = Assert.Single(result.Project!.Artifacts);
        Assert.Equal("fcstd", artifact.Format);
        Assert.Equal(ArtifactRole.Source, artifact.Role);
        Assert.Equal(64, artifact.Sha256!.Length);
        Assert.Equal(new FileInfo(path).Length, artifact.Bytes);
        Assert.Equal(artifact.Id, result.Project.Mechanical!.SourceArtifactId);

        var json = ProjectWriter.Serialize(result.Project);
        var loaded = ProjectLoader.Default.Load(json);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.DoesNotContain(ProjectValidator.Default.Validate(loaded.Project!),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Step_is_added_as_a_mechanical_export()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "axis.step");
        File.WriteAllText(path, "ISO-10303-21;");

        var result = FreeCadArtifactImporter.Default.AddReference(CreateProject(), path);

        var artifact = Assert.Single(result.Project!.Artifacts);
        Assert.Equal("step", artifact.Format);
        Assert.Equal(ArtifactRole.Generated, artifact.Role);
        Assert.Equal(artifact.Id, result.Project.Mechanical!.Exports!["step"]);
    }

    [Fact]
    public void Store_copies_the_file_and_sets_a_content_addressed_path()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "axis.stl");
        File.WriteAllText(path, "solid axis");
        var store = new ArtifactStore(Path.Combine(temp.Path, "store"));

        var result = FreeCadArtifactImporter.Default.AddReference(CreateProject(), path, store);

        var artifact = Assert.Single(result.Project!.Artifacts);
        Assert.StartsWith("artifact://sha256/", artifact.Path, StringComparison.Ordinal);
        Assert.True(store.Verify(artifact.Sha256!));
        Assert.Equal("stl", artifact.Format);
    }

    [Fact]
    public void Same_content_is_not_added_twice()
    {
        using var temp = new TempDirectory();
        var firstPath = Path.Combine(temp.Path, "axis.FCStd");
        var secondPath = Path.Combine(temp.Path, "copy.FCStd");
        File.WriteAllText(firstPath, "same bytes");
        File.WriteAllText(secondPath, "same bytes");

        var first = FreeCadArtifactImporter.Default.AddReference(CreateProject(), firstPath);
        var second = FreeCadArtifactImporter.Default.AddReference(first.Project!, secondPath);

        Assert.Single(second.Project!.Artifacts);
        Assert.Equal(first.Project!.Artifacts[0].Id, second.Project.Artifacts[0].Id);
    }

    [Fact]
    public void Unsupported_extension_reports_EMD_FREECAD_001()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(path, "not a CAD file");

        var result = FreeCadArtifactImporter.Default.AddReference(CreateProject(), path);

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-FREECAD-001");
    }

    [Fact]
    public void Missing_file_reports_EMD_IO_001()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "missing.FCStd");

        var result = FreeCadArtifactImporter.Default.AddReference(CreateProject(), path);

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-IO-001");
    }

    private static EmdProject CreateProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "FreeCAD artifact test",
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
}
