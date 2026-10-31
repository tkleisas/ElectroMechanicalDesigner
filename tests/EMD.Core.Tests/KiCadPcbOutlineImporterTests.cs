using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class KiCadPcbOutlineImporterTests
{
    private static string Fixture => File.ReadAllText(TestPaths.Fixture("board.kicad_pcb"));

    [Fact]
    public void Import_extracts_the_outline_and_mounting_holes()
    {
        var result = KiCadPcbOutlineImporter.Default.Import(Fixture, "board.kicad_pcb");

        Assert.Empty(result.Diagnostics);
        var board = Assert.IsType<Part>(result.Board);
        Assert.Equal(PartKind.Board, board.Kind);
        Assert.Equal(PartCategory.Electronics, board.Category);
        Assert.Equal(GeometryKind.Box, board.Geometry!.Kind);
        AssertClose([0.1, 0.06, 0.0016], board.Geometry.Size!);
        AssertClose([0.05, 0.03, 0], board.Pose!.Pos!);

        Assert.Equal(2, board.Ports.Count);
        var h1 = board.Ports.Single(port => port.Key == "H1");
        var h2 = board.Ports.Single(port => port.Key == "H2");
        Assert.Equal(PortDomain.Mechanical, h1.Domain);
        Assert.Equal("mount", h1.Kind);
        AssertClose([-0.04, -0.02, 0], h1.Pose!.Pos!);
        AssertClose([0.04, 0.02, 0], h2.Pose!.Pos!);
        Assert.NotNull(result.BoardEntry);
        Assert.Equal(board.Id, result.BoardEntry!.PartId);
    }

    [Fact]
    public void Missing_edge_cuts_reports_EMD_KICAD_020()
    {
        var text = Fixture.Replace("(layer \"Edge.Cuts\")", "(layer \"F.Cu\")", StringComparison.Ordinal);

        var result = KiCadPcbOutlineImporter.Default.Import(text, "board.kicad_pcb");

        Assert.Null(result.Board);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-020");
    }

    [Fact]
    public void Malformed_sexpression_reports_EMD_KICAD_021()
    {
        var result = KiCadPcbOutlineImporter.Default.Import("(kicad_pcb", "board.kicad_pcb");

        Assert.Null(result.Board);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-021");
    }

    [Fact]
    public void Import_is_deterministic()
    {
        var first = KiCadPcbOutlineImporter.Default.Import(Fixture, "board.kicad_pcb");
        var second = KiCadPcbOutlineImporter.Default.Import(Fixture, "board.kicad_pcb");

        Assert.Equal(first.Board!.Id, second.Board!.Id);
        Assert.Equal(
            first.Board.Ports.Select(port => port.Id),
            second.Board.Ports.Select(port => port.Id));
    }

    [Fact]
    public void Board_part_round_trips_through_schema_and_validation()
    {
        var imported = KiCadPcbOutlineImporter.Default.Import(Fixture, "board.kicad_pcb");
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "board test",
            Parts = [imported.Board!],
            Electrical = new ElectricalSection { Boards = [imported.BoardEntry!] }
        };

        var json = ProjectWriter.Serialize(project);
        var loaded = ProjectLoader.Default.Load(json);

        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.DoesNotContain(ProjectValidator.Default.Validate(loaded.Project!),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertClose(double[] expected, double[] actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(actual[index], expected[index] - tolerance, expected[index] + tolerance);
    }
}
