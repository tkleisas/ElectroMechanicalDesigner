using System.Globalization;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;

namespace Emd.Core.Manufacturing;

public sealed record HeightmapReport(
    int Cells,
    int TargetCells,
    double StockTopMm,
    double RemovedVolumeMm3,
    int GougeCells,
    double MaxGougeMm,
    double GougeVolumeMm3,
    int RemainingCells,
    double MaxRemainingMm,
    double RemainingVolumeMm3);

public sealed class Heightmap
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double CellMm { get; init; }
    public required double[] Min { get; init; }
    public required double[] Max { get; init; }
    public required double[] Heights { get; init; }

    public static Heightmap Stock(double[] min, double[] max, double cellMm)
    {
        var (width, height) = Dimensions(min, max, cellMm);
        var heights = new double[width * height];
        Array.Fill(heights, max[2]);
        return new Heightmap
        {
            Width = width,
            Height = height,
            CellMm = cellMm,
            Min = min,
            Max = max,
            Heights = heights
        };
    }

    public static Heightmap FromStl(
        IReadOnlyList<StlTriangle> triangles,
        double[] min,
        double[] max,
        double cellMm)
    {
        var stock = Stock(min, max, cellMm);
        var heights = new double[stock.Width * stock.Height];
        Array.Fill(heights, double.NaN);
        for (var y = 0; y < stock.Height; y++)
        {
            for (var x = 0; x < stock.Width; x++)
            {
                var px = min[0] + (x + 0.5) * cellMm;
                var py = min[1] + (y + 0.5) * cellMm;
                var top = double.NegativeInfinity;
                foreach (var triangle in triangles)
                {
                    if (TryIntersect(triangle, px, py, max[2] + 1, out var z) && z > top)
                        top = z;
                }
                if (!double.IsNegativeInfinity(top))
                    heights[y * stock.Width + x] = top;
            }
        }
        return new Heightmap
        {
            Width = stock.Width,
            Height = stock.Height,
            CellMm = cellMm,
            Min = min,
            Max = max,
            Heights = heights
        };
    }

    public void Simulate(GCodeProgram program, double toolDiameterMm)
    {
        var radius = toolDiameterMm / 2;
        var step = Math.Min(CellMm, Math.Max(radius / 2, 0.01));
        foreach (var move in program.Moves.Where(move => !move.Rapid))
        {
            var dx = move.End[0] - move.Start[0];
            var dy = move.End[1] - move.Start[1];
            var length = Math.Sqrt(dx * dx + dy * dy);
            var samples = Math.Max(1, (int)Math.Ceiling(length / step));
            for (var sample = 0; sample <= samples; sample++)
            {
                var fraction = samples == 0 ? 0 : (double)sample / samples;
                var x = move.Start[0] + dx * fraction;
                var y = move.Start[1] + dy * fraction;
                var z = move.Start[2] + (move.End[2] - move.Start[2]) * fraction;
                Cut(x, y, z, radius);
            }
        }
    }

    public HeightmapReport Compare(Heightmap target, double toleranceMm = 0.01)
    {
        var gougeCells = 0;
        var remainingCells = 0;
        var targetCells = 0;
        var maxGouge = 0.0;
        var maxRemaining = 0.0;
        var cellArea = CellMm * CellMm;
        var gougeVolume = 0.0;
        var remainingVolume = 0.0;
        var removed = 0.0;
        for (var index = 0; index < Heights.Length; index++)
        {
            var targetHeight = target.Heights[index];
            if (double.IsNaN(targetHeight))
                continue;
            targetCells++;
            var height = Heights[index];
            if (height + toleranceMm < targetHeight)
            {
                gougeCells++;
                var depth = targetHeight - height;
                maxGouge = Math.Max(maxGouge, depth);
                gougeVolume += depth * cellArea;
            }
            else if (height > targetHeight + toleranceMm)
            {
                remainingCells++;
                var remaining = height - targetHeight;
                maxRemaining = Math.Max(maxRemaining, remaining);
                remainingVolume += remaining * cellArea;
            }

            removed += (Max[2] - height) * cellArea;
        }

        return new HeightmapReport(
            Heights.Length,
            targetCells,
            Max[2],
            Math.Max(0, removed),
            gougeCells,
            maxGouge,
            gougeVolume,
            remainingCells,
            maxRemaining,
            remainingVolume);
    }

    public JsonObject ToJson(HeightmapReport report) => new()
    {
        ["cells"] = report.Cells,
        ["target_cells"] = report.TargetCells,
        ["stock_top_mm"] = report.StockTopMm,
        ["removed_volume_mm3"] = report.RemovedVolumeMm3,
        ["gouge_cells"] = report.GougeCells,
        ["max_gouge_mm"] = report.MaxGougeMm,
        ["gouge_volume_mm3"] = report.GougeVolumeMm3,
        ["remaining_cells"] = report.RemainingCells,
        ["max_remaining_mm"] = report.MaxRemainingMm,
        ["remaining_volume_mm3"] = report.RemainingVolumeMm3
    };

    private void Cut(double x, double y, double z, double radius)
    {
        var minX = Math.Max(0, (int)Math.Floor((x - radius - Min[0]) / CellMm));
        var maxX = Math.Min(Width - 1, (int)Math.Ceiling((x + radius - Min[0]) / CellMm));
        var minY = Math.Max(0, (int)Math.Floor((y - radius - Min[1]) / CellMm));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling((y + radius - Min[1]) / CellMm));
        for (var cy = minY; cy <= maxY; cy++)
        {
            for (var cx = minX; cx <= maxX; cx++)
            {
                var px = Min[0] + (cx + 0.5) * CellMm;
                var py = Min[1] + (cy + 0.5) * CellMm;
                var dx = px - x;
                var dy = py - y;
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                var index = cy * Width + cx;
                if (Heights[index] > z)
                    Heights[index] = z;
            }
        }
    }

    private static (int Width, int Height) Dimensions(double[] min, double[] max, double cellMm)
    {
        if (cellMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellMm));
        var width = Math.Max(1, (int)Math.Ceiling((max[0] - min[0]) / cellMm));
        var height = Math.Max(1, (int)Math.Ceiling((max[1] - min[1]) / cellMm));
        if ((long)width * height > 4_000_000)
            throw new InvalidOperationException("Heightmap resolution is too large.");
        return (width, height);
    }

    private static bool TryIntersect(
        StlTriangle triangle,
        double px,
        double py,
        double rayZ,
        out double z)
    {
        z = 0;
        var ax = triangle.A[0];
        var ay = triangle.A[1];
        var az = triangle.A[2];
        var e1x = triangle.B[0] - ax;
        var e1y = triangle.B[1] - ay;
        var e1z = triangle.B[2] - az;
        var e2x = triangle.C[0] - ax;
        var e2y = triangle.C[1] - ay;
        var e2z = triangle.C[2] - az;

        var hx = e2y;
        var hy = -e2x;
        var hz = 0.0;
        var a = e1x * hx + e1y * hy + e1z * hz;
        if (Math.Abs(a) < 1e-12)
            return false;

        var f = 1.0 / a;
        var sx = px - ax;
        var sy = py - ay;
        var sz = rayZ - az;
        var u = f * (sx * hx + sy * hy + sz * hz);
        if (u < 0 || u > 1)
            return false;

        var qx = sy * e1z - sz * e1y;
        var qy = sz * e1x - sx * e1z;
        var qz = sx * e1y - sy * e1x;
        var v = f * (0 * qx + 0 * qy + -1 * qz);
        if (v < 0 || u + v > 1)
            return false;

        var t = f * (e2x * qx + e2y * qy + e2z * qz);
        if (t <= 0)
            return false;
        z = rayZ - t;
        return true;
    }
}
