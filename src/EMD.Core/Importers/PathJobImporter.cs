using System.Text.Json;
using Emd.Core.Diagnostics;

namespace Emd.Core.Importers;

public sealed record PathJobInfo(
    string Name,
    double[] StockMin,
    double[] StockMax,
    int ToolCount,
    int OperationCount,
    string? GCodePath,
    string Postprocessor);

public sealed record PathJobLoadResult(
    PathJobInfo? Job,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Job is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class PathJobImporter
{
    public static PathJobImporter Default { get; } = new();

    private static readonly HashSet<string> Postprocessors =
        new(["klipper", "linuxcnc", "grbl", "marlin"], StringComparer.OrdinalIgnoreCase);

    public PathJobLoadResult ImportFile(string path)
    {
        try
        {
            return Import(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("EMD-IO-001", $"Could not read path job '{path}': {exception.Message}", path);
        }
    }

    public PathJobLoadResult Import(string json, string? source = null)
    {
        PathJobDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PathJobDto>(json, CoreJson.Options);
        }
        catch (JsonException exception)
        {
            return Failure("EMD-CAM-008", $"Invalid FreeCAD Path job JSON: {exception.Message}", source);
        }

        if (dto is null)
            return Failure("EMD-CAM-008", "FreeCAD Path job deserialized to null.", source);
        if (dto.SchemaVersion != "0.1")
            return Failure("EMD-CAM-008", "Path job schema_version must be 0.1.", source);
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Failure("EMD-CAM-008", "Path job has no name.", source);
        if (!string.Equals(dto.Units, "mm", StringComparison.OrdinalIgnoreCase))
            return Failure("EMD-CAM-008", "Path job units must be mm.", source);
        if (dto.Stock is null || dto.Stock.Min is not { Length: 3 } ||
            dto.Stock.Max is not { Length: 3 })
            return Failure("EMD-CAM-008", "Path job stock needs 3-axis min/max.", source);
        if (dto.Stock.Min.Zip(dto.Stock.Max).Any(pair => pair.First >= pair.Second))
            return Failure("EMD-CAM-008", "Path job stock max must exceed min.", source);
        if (dto.Tools is not { Count: > 0 })
            return Failure("EMD-CAM-008", "Path job needs at least one tool.", source);
        if (dto.Tools.Any(tool => tool.Id <= 0 || tool.DiameterMm <= 0))
            return Failure("EMD-CAM-008", "Path job tools need positive ids/diameters.", source);
        if (dto.Tools.Select(tool => tool.Id).Distinct().Count() != dto.Tools.Count)
            return Failure("EMD-CAM-008", "Path job tool ids must be unique.", source);
        if (dto.Operations is not { Count: > 0 })
            return Failure("EMD-CAM-008", "Path job needs at least one operation.", source);
        var tools = dto.Tools.Select(tool => tool.Id).ToHashSet();
        if (dto.Operations.Any(operation => !tools.Contains(operation.Tool)))
            return Failure("EMD-CAM-008", "Path job operation references an unknown tool.", source);

        var postprocessor = string.IsNullOrWhiteSpace(dto.Postprocessor)
            ? "klipper"
            : dto.Postprocessor.Trim().ToLowerInvariant();
        if (!Postprocessors.Contains(postprocessor))
            return Failure(
                "EMD-CAM-008",
                $"Path job postprocessor '{postprocessor}' is not supported.",
                source);

        string? gcode = null;
        var diagnostics = new List<Diagnostic>();
        if (!string.IsNullOrWhiteSpace(dto.Gcode))
        {
            gcode = Path.IsPathRooted(dto.Gcode)
                ? dto.Gcode
                : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(source ?? ".")) ?? ".",
                    dto.Gcode);
            if (!File.Exists(gcode))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-009",
                    DiagnosticSeverity.Warning,
                    $"Path job G-code '{gcode}' was not found.",
                    Entity: source));
            }
        }

        return new PathJobLoadResult(
            new PathJobInfo(
                dto.Name.Trim(),
                dto.Stock.Min,
                dto.Stock.Max,
                dto.Tools.Count,
                dto.Operations.Count,
                gcode,
                postprocessor),
            diagnostics);
    }

    private static PathJobLoadResult Failure(string code, string message, string? source) =>
        new(null, [new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: source)]);

    private sealed record PathJobDto
    {
        public string? SchemaVersion { get; init; }
        public string? Name { get; init; }
        public string? Units { get; init; }
        public PathStockDto? Stock { get; init; }
        public List<PathToolDto>? Tools { get; init; }
        public List<PathOperationDto>? Operations { get; init; }
        public string? Gcode { get; init; }
        public string? Postprocessor { get; init; }
    }

    private sealed record PathStockDto
    {
        public double[]? Min { get; init; }
        public double[]? Max { get; init; }
    }

    private sealed record PathToolDto
    {
        public int Id { get; init; }
        public double DiameterMm { get; init; }
        public string? Type { get; init; }
    }

    private sealed record PathOperationDto
    {
        public string? Name { get; init; }
        public int Tool { get; init; }
        public string? Gcode { get; init; }
    }
}
