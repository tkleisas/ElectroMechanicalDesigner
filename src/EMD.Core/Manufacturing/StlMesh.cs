using System.Globalization;
using System.Text;
using Emd.Core.Diagnostics;

namespace Emd.Core.Manufacturing;

public sealed record StlTriangle(double[] A, double[] B, double[] C);

public sealed record StlLoadResult(
    IReadOnlyList<StlTriangle>? Triangles,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Triangles is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class StlMeshLoader
{
    public static StlMeshLoader Default { get; } = new();

    public StlLoadResult LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllBytes(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("EMD-CAM-010", $"Could not read STL '{path}': {exception.Message}", path);
        }
    }

    public StlLoadResult Load(byte[] bytes, string? source = null)
    {
        var triangles = new List<StlTriangle>();
        if (LooksBinary(bytes))
        {
            var count = BitConverter.ToUInt32(bytes, 80);
            for (var index = 0; index < count; index++)
            {
                var offset = 84 + index * 50;
                triangles.Add(new StlTriangle(
                    Vector(bytes, offset + 12),
                    Vector(bytes, offset + 24),
                    Vector(bytes, offset + 36)));
            }
            return new StlLoadResult(triangles, []);
        }

        var text = Encoding.UTF8.GetString(bytes);
        var vertices = new List<double[]>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase))
                continue;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 ||
                !TryDouble(fields[1], out var x) ||
                !TryDouble(fields[2], out var y) ||
                !TryDouble(fields[3], out var z))
                return Failure("EMD-CAM-010", "STL contains an invalid vertex.", source);
            vertices.Add([x, y, z]);
        }

        if (vertices.Count == 0 || vertices.Count % 3 != 0)
            return Failure("EMD-CAM-010", "STL contains no triangle vertices.", source);

        for (var index = 0; index < vertices.Count; index += 3)
            triangles.Add(new StlTriangle(vertices[index], vertices[index + 1], vertices[index + 2]));

        return new StlLoadResult(triangles, []);
    }

    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length < 84)
            return false;
        var count = BitConverter.ToUInt32(bytes, 80);
        return bytes.Length >= 84 + count * 50L;
    }

    private static double[] Vector(byte[] bytes, int offset) =>
    [
        BitConverter.ToSingle(bytes, offset),
        BitConverter.ToSingle(bytes, offset + 4),
        BitConverter.ToSingle(bytes, offset + 8)
    ];

    private static bool TryDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static StlLoadResult Failure(string code, string message, string? source) =>
        new(null, [new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: source)]);
}
