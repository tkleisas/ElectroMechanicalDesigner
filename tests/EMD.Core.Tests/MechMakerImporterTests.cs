using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class MechMakerImporterTests
{
    private static string FixtureJson => File.ReadAllText(TestPaths.Fixture("linear_axis.json"));

    [Fact]
    public void Import_maps_parts_connections_boards_and_wiring()
    {
        var result = MechMakerImporter.Default.Import(FixtureJson, "linear_axis.json");

        Assert.Empty(result.Diagnostics);
        var project = Assert.IsType<EmdProject>(result.Project);
        Assert.Equal("test_axis", project.Name);
        Assert.Equal("test_axis", project.Key);
        Assert.Equal(3, project.Parts.Count);
        Assert.Equal(5, project.Connections.Count);
        Assert.Equal(2, project.Electrical!.Nets.Count);
        Assert.Single(project.Electrical.Boards);

        var motor = project.Parts.Single(part => part.Key == "motor");
        Assert.Equal(PartCategory.Motor, motor.Category);
        Assert.Equal(new double[] { 0, 0, 0.1 }, motor.Pose!.Pos);
        Assert.Equal(new double[] { 0, 0, 90 }, motor.Pose.Rot);
        Assert.Contains(motor.Ports, port => port.Id == "mount" && port.Domain == PortDomain.Mechanical);
        Assert.Contains(motor.Ports, port => port.Id == "step" && port.Domain == PortDomain.Control);
        Assert.Contains(motor.Ports, port => port.Id == "dir" && port.Domain == PortDomain.Control);

        var board = project.Parts.Single(part => part.Key == "main");
        Assert.Equal(PartKind.Board, board.Kind);
        Assert.Contains(board.Ports, port => port.Id == "P2_2");
        Assert.Contains(board.Ports, port => port.Id == "P2_6");

        Assert.Contains(project.Connections, connection =>
            connection.Key == "motor_on_beam" && connection.Kind == ConnectionKind.Mate);

        Assert.All(project.Electrical.Nets, net =>
            Assert.Equal(2, project.Connections.Count(connection =>
                connection.From.NetId == net.Id || connection.To.NetId == net.Id)));
    }

    [Fact]
    public void Imported_project_round_trips_through_schema_and_semantic_validation()
    {
        var imported = MechMakerImporter.Default.Import(FixtureJson, "linear_axis.json");
        var json = ProjectWriter.Serialize(imported.Project!);

        var loaded = ProjectLoader.Default.Load(json);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.DoesNotContain(ProjectValidator.Default.Validate(loaded.Project!),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Import_is_deterministic()
    {
        var first = MechMakerImporter.Default.Import(FixtureJson, "linear_axis.json");
        var second = MechMakerImporter.Default.Import(FixtureJson, "linear_axis.json");

        Assert.Equal(first.Project!.Id, second.Project!.Id);
        Assert.Equal(
            first.Project.Parts.Select(part => part.Id),
            second.Project.Parts.Select(part => part.Id));
        Assert.Equal(
            ProjectWriter.Serialize(first.Project),
            ProjectWriter.Serialize(second.Project));
    }

    [Fact]
    public void Catalog_supplies_part_names_and_categories()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "nema17_stepper.json"),
            """
            {
              "id": "nema17_stepper",
              "name": "NEMA 17 stepper",
              "category": "motor"
            }
            """);

        var catalog = MechMakerCatalog.Load(temp.Path);
        var result = MechMakerImporter.Default.Import(FixtureJson, "linear_axis.json", catalog);

        var motor = result.Project!.Parts.Single(part => part.Key == "motor");
        Assert.Equal("NEMA 17 stepper", motor.Name);
        Assert.Equal(PartCategory.Motor, motor.Category);
    }

    [Fact]
    public void Missing_board_reference_reports_EMD_IMPORT_001()
    {
        var json = FixtureJson.Replace("\"board\": \"main\"", "\"board\": \"missing\"", StringComparison.Ordinal);

        var result = MechMakerImporter.Default.Import(json, "linear_axis.json");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-IMPORT-001");
        Assert.NotNull(result.Project);
    }

    [Fact]
    public void Missing_component_reference_reports_EMD_IMPORT_001()
    {
        var json = FixtureJson.Replace("\"component\": \"motor\"", "\"component\": \"missing\"", StringComparison.Ordinal);

        var result = MechMakerImporter.Default.Import(json, "linear_axis.json");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-IMPORT-001");
        Assert.NotNull(result.Project);
    }

    [Fact]
    public void Imports_the_real_mechmaker_example_when_present()
    {
        var path = Path.GetFullPath(Path.Combine(
            TestPaths.RepoRoot, "..", "MechMaker", "examples", "linear_axis_v0.json"));
        if (!File.Exists(path))
            return;

        var result = MechMakerImporter.Default.ImportFile(path);

        Assert.Empty(result.Diagnostics);
        var project = Assert.IsType<EmdProject>(result.Project);
        Assert.Equal(12, project.Parts.Count);
        Assert.Equal(7, project.Electrical!.Nets.Count);
        Assert.DoesNotContain(ProjectValidator.Default.Validate(project),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Invalid_json_reports_EMD_IMPORT_002()
    {
        var result = MechMakerImporter.Default.Import("{ this is not json");

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-IMPORT-002");
    }
}
