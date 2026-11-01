using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class FreeCadManifestImporterTests
{
    private static string FixtureJson => File.ReadAllText(TestPaths.Fixture("freecad_manifest.json"));

    [Fact]
    public void Import_maps_parts_geometry_connectors_joints_and_artifacts()
    {
        var result = FreeCadManifestImporter.Default.Import(FixtureJson, "axis.FCStd");

        Assert.Empty(result.Diagnostics);
        var project = Assert.IsType<EmdProject>(result.Project);
        Assert.Equal("axis", project.Name);
        Assert.Equal(2, project.Parts.Count);
        Assert.Equal(3, project.Artifacts.Count);

        var frame = project.Parts.Single(part => part.Key == "frame");
        Assert.Equal(PartCategory.Structure, frame.Category);
        Assert.Equal(0.85, frame.MassKg);
        Assert.Equal(GeometryKind.Box, frame.Geometry!.Kind);
        Assert.Equal(new double[] { 0.4, 0.2, 0.02 }, frame.Geometry.Size);
        var connector = Assert.Single(frame.Ports);
        Assert.Equal("motor_mount", connector.Key);
        Assert.Equal("bolt_m3", connector.Kind);
        Assert.Equal(PortDomain.Mechanical, connector.Domain);

        var carriage = project.Parts.Single(part => part.Key == "carriage");
        Assert.Equal(PartCategory.LinearMotion, carriage.Category);

        Assert.Equal(frame.Id, project.Mechanical!.AssemblyRootPartId);
        var joint = Assert.Single(project.Mechanical.Joints!);
        Assert.Equal("x_slide", joint.Key);
        Assert.Equal(MechanicalJointType.Slide, joint.Type);
        Assert.Equal(frame.Id, joint.ParentPartId);
        Assert.Equal(carriage.Id, joint.ChildPartId);
        Assert.Equal(new double[] { 1, 0, 0 }, joint.Axis);
        Assert.Equal(new double[] { -0.2, 0.2 }, joint.Range);
        Assert.Equal("x_motor", joint.Actuator);

        var source = project.Artifacts.Single(artifact => artifact.Format == "fcstd");
        Assert.Equal(source.Id, project.Mechanical.SourceArtifactId);
        var step = project.Artifacts.Single(artifact => artifact.Format == "step");
        Assert.Equal(step.Id, project.Mechanical.Exports!["step"]);
        var bom = project.Artifacts.Single(artifact => artifact.Format == "bom");
        Assert.Equal(bom.Id, project.Manufacturing!.BomArtifactId);
    }

    [Fact]
    public void Imported_project_round_trips_through_schema_and_semantic_validation()
    {
        var imported = FreeCadManifestImporter.Default.Import(FixtureJson, "axis.FCStd");
        var json = ProjectWriter.Serialize(imported.Project!);

        var loaded = ProjectLoader.Default.Load(json);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.DoesNotContain(ProjectValidator.Default.Validate(loaded.Project!),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Import_is_deterministic()
    {
        var first = FreeCadManifestImporter.Default.Import(FixtureJson, "axis.FCStd");
        var second = FreeCadManifestImporter.Default.Import(FixtureJson, "axis.FCStd");

        Assert.Equal(first.Project!.Id, second.Project!.Id);
        Assert.Equal(
            ProjectWriter.Serialize(first.Project),
            ProjectWriter.Serialize(second.Project));
    }

    [Fact]
    public void Importer_preserves_rich_part_metadata()
    {
        var result = FreeCadManifestImporter.Default.Import(FixtureJson, "axis.FCStd");

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var frame = result.Project!.Parts.Single(part => part.Key == "frame");
        Assert.Equal("Machined frame plate", (string?)frame.Metadata!["description"]);
        Assert.Equal("FR-2020", (string?)frame.Metadata["bom"]!["manufacturer_part_number"]);
        Assert.Equal("motor_boss", (string?)frame.Metadata["features"]![0]!["name"]);
        Assert.Equal("frame", (string?)frame.Metadata["tags"]![0]);
    }

    [Fact]
    public void Malformed_json_reports_EMD_IMPORT_002()
    {
        var result = FreeCadManifestImporter.Default.Import("{ not json");

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-IMPORT-002");
    }

    [Fact]
    public void Missing_joint_child_reports_EMD_MECH_004()
    {
        var json = FixtureJson.Replace("\"child\": \"carriage\"", "\"child\": \"missing\"", StringComparison.Ordinal);

        var result = FreeCadManifestImporter.Default.Import(json, "axis.FCStd");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-004");
        Assert.NotNull(result.Project);
        Assert.Empty(result.Project!.Mechanical!.Joints!);
    }

    [Fact]
    public void Mesh_geometry_with_unknown_artifact_reports_EMD_ART_001()
    {
        var json = FixtureJson.Replace(
            "\"kind\": \"box\",\n        \"size\": [0.4, 0.2, 0.02],\n        \"rgba\": \"0.7 0.7 0.75 1\"",
            "\"kind\": \"mesh\",\n        \"mesh\": \"missing.stl\"",
            StringComparison.Ordinal);

        var result = FreeCadManifestImporter.Default.Import(json, "axis.FCStd");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ART-001");
    }
}
