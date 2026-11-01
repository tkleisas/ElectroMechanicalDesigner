using System.Text;
using Emd.Core.Manufacturing;

namespace Emd.Core.Tests;

public class HeightmapTests
{
    private const string TopFaceStl = """
        solid top
        facet normal 0 0 1
          outer loop
            vertex 0 0 5
            vertex 10 0 5
            vertex 10 10 5
          endloop
        endfacet
        facet normal 0 0 1
          outer loop
            vertex 0 0 5
            vertex 10 10 5
            vertex 0 10 5
          endloop
        endfacet
        endsolid top
        """;

    [Fact]
    public void Simulation_matches_a_flat_target_surface()
    {
        var (stock, target) = Maps();
        var program = GCodeParser.Default.Parse("G21\nG90\nG0 X5 Y5 Z10 F1000\nG1 Z5 F500\n").Program!;

        Assert.Equal(5, target.Heights[0]);
        stock.Simulate(program, 40);
        var report = stock.Compare(target);

        Assert.Equal(100, report.TargetCells);
        Assert.Equal(0, report.GougeCells);
        Assert.Equal(0, report.RemainingCells);
        Assert.True(report.RemovedVolumeMm3 > 0);
    }

    [Fact]
    public void Simulation_reports_gouges_when_cutting_too_deep()
    {
        var (stock, target) = Maps();
        var program = GCodeParser.Default.Parse("G21\nG90\nG0 X5 Y5 Z10 F1000\nG1 Z3 F500\n").Program!;

        stock.Simulate(program, 40);
        var report = stock.Compare(target);

        Assert.Equal(100, report.GougeCells);
        Assert.Equal(2, report.MaxGougeMm, 6);
    }

    [Fact]
    public void Simulation_reports_remaining_material()
    {
        var (stock, target) = Maps();
        var program = GCodeParser.Default.Parse("G21\nG90\nG0 X5 Y5 Z10 F1000\nG1 Z8 F500\n").Program!;

        stock.Simulate(program, 40);
        var report = stock.Compare(target);

        Assert.Equal(100, report.RemainingCells);
        Assert.Equal(3, report.MaxRemainingMm, 6);
    }

    [Fact]
    public void Stl_loader_reads_ascii_triangles()
    {
        var result = StlMeshLoader.Default.Load(Encoding.UTF8.GetBytes(TopFaceStl));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(2, result.Triangles!.Count);
    }

    private static (Heightmap Stock, Heightmap Target) Maps()
    {
        var min = new[] { 0d, 0d, 0d };
        var max = new[] { 10d, 10d, 10d };
        var triangles = StlMeshLoader.Default
            .Load(Encoding.UTF8.GetBytes(TopFaceStl)).Triangles!;
        return (
            Heightmap.Stock(min, max, 1),
            Heightmap.FromStl(triangles, min, max, 1));
    }
}
