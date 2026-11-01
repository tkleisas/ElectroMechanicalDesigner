using System.Text.Json;
using System.Text.Json.Serialization;
using MechMaker.Core;
using MechMaker.Core.Mathematics;
using MechMaker.Core.Model;

namespace MechMaker.Fmu.Direct;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(MachineDto))]
[JsonSerializable(typeof(PartDefinitionDto))]
internal partial class DirectJsonContext : JsonSerializerContext
{
}

internal static class DirectJson
{
    public static MachineDefinition LoadMachine(string path) =>
        Map(JsonSerializer.Deserialize(
            File.ReadAllText(path),
            DirectJsonContext.Default.MachineDto)
            ?? throw new InvalidDataException("Machine JSON deserialized to null."));

    public static PartCatalog LoadCatalog(string path)
    {
        var parts = Directory.EnumerateFiles(path, "*.json")
            .Select(file => Map(JsonSerializer.Deserialize(
                File.ReadAllText(file),
                DirectJsonContext.Default.PartDefinitionDto)
                ?? throw new InvalidDataException($"Catalog part '{file}' deserialized to null.")))
            .ToList();
        return new PartCatalog(parts, path);
    }

    private static MachineDefinition Map(MachineDto dto) => new()
    {
        SchemaVersion = dto.SchemaVersion ?? MachineDefinition.CurrentSchemaVersion,
        Name = dto.Name ?? "machine",
        Parts = (dto.Parts ?? [])
            .Select(part => new PartInstance
            {
                Id = part.Id ?? "",
                Part = part.Part ?? "",
                Pose = Map(part.Pose),
                Params = part.Params ?? []
            })
            .ToList(),
        Connections = (dto.Connections ?? [])
            .Select(connection => new Connection
            {
                Id = connection.Id ?? "",
                PartA = connection.PartA ?? "",
                ConnectorA = connection.ConnectorA ?? "",
                PartB = connection.PartB ?? "",
                ConnectorB = connection.ConnectorB ?? ""
            })
            .ToList(),
        Boards = (dto.Boards ?? [])
            .Select(board => new BoardDefinition
            {
                Id = board.Id ?? "",
                Type = board.Type ?? "skr-pico",
                Pins = board.Pins ?? []
            })
            .ToList(),
        Wiring = (dto.Wiring ?? [])
            .Select(wire => new Wire
            {
                Component = wire.Component ?? "",
                Signal = wire.Signal ?? "",
                Board = wire.Board ?? "",
                Pin = wire.Pin ?? ""
            })
            .ToList()
    };

    private static PartDefinition Map(PartDefinitionDto dto) => new()
    {
        Id = dto.Id ?? "",
        Name = dto.Name ?? "",
        Category = Parse(dto.Category, PartCategory.Structure),
        Description = dto.Description ?? "",
        HilDevice = dto.HilDevice,
        Bodies = (dto.Bodies ?? []).Select(Map).ToList(),
        Connectors = (dto.Connectors ?? []).Select(Map).ToList(),
        Motor = dto.Motor is null ? null : Map(dto.Motor),
        Params = dto.Params ?? [],
        ParamsExpr = dto.ParamsExpr,
        IsTransmissionElement = dto.IsTransmissionElement ?? false
    };

    private static PartBody Map(PartBodyDto dto) => new()
    {
        Name = dto.Name ?? "root",
        MassKg = dto.MassKg ?? 0,
        ParentBody = dto.ParentBody,
        RelativePose = Map(dto.RelativePose),
        Actuated = Parse(dto.Actuated, JointActuation.None),
        Shapes = (dto.Shapes ?? []).Select(Map).ToList()
    };

    private static Shape Map(ShapeDto dto) => new()
    {
        Kind = Parse(dto.Kind, ShapeKind.Box),
        Extents = Vector(dto.Extents, new Vec3(0.01, 0.01, 0.01)),
        ExtentsExpr = dto.ExtentsExpr,
        MeshFile = dto.MeshFile,
        MeshScale = Vector(dto.MeshScale, new Vec3(1, 1, 1)),
        Collision = dto.Collision ?? true,
        Visible = dto.Visible ?? true,
        ContributesMass = dto.ContributesMass ?? true,
        SmoothNormal = dto.SmoothNormal ?? false,
        RelativePose = Map(dto.RelativePose),
        PosExpr = dto.PosExpr,
        Rgba = dto.Rgba ?? "0.6 0.6 0.65 1"
    };

    private static ConnectorDefinition Map(ConnectorDto dto) => new()
    {
        Name = dto.Name ?? "",
        Type = Parse(dto.Type, ConnectorType.BoltM3),
        Pose = Map(dto.Pose),
        PosExpr = dto.PosExpr,
        Axis = Vector(dto.Axis, Vec3.UnitZ),
        Body = dto.Body ?? "root"
    };

    private static MotorSpec Map(MotorDto dto) => new()
    {
        Kind = Parse(dto.Kind, MotorKind.Stepper),
        StepAngleDeg = dto.StepAngleDeg ?? 1.8,
        HoldingTorqueNm = dto.HoldingTorqueNm ?? 0.4,
        RatedCurrentA = dto.RatedCurrentA ?? 1.7,
        MaxSpeedRps = dto.MaxSpeedRps ?? 10,
        RotorInertiaKgM2 = dto.RotorInertiaKgM2 ?? 4e-5
    };

    private static Pose Map(PoseDto? dto) => new()
    {
        Position = Vector(dto?.Pos, Vec3.Zero),
        RotationEulerDeg = Vector(dto?.Rot, Vec3.Zero)
    };

    private static Vec3 Vector(double[]? values, Vec3 fallback) =>
        values is { Length: >= 3 }
            ? new Vec3(values[0], values[1], values[2])
            : fallback;

    private static T Parse<T>(string? value, T fallback) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Replace("_", "");
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name.Replace("_", ""), normalized,
                    StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<T>(name);
        }
        return fallback;
    }
}

internal sealed class MachineDto
{
    public string? SchemaVersion { get; set; }
    public string? Name { get; set; }
    public List<PartDto>? Parts { get; set; }
    public List<ConnectionDto>? Connections { get; set; }
    public List<BoardDto>? Boards { get; set; }
    public List<WireDto>? Wiring { get; set; }
}

internal sealed class PartDto
{
    public string? Id { get; set; }
    public string? Part { get; set; }
    public PoseDto? Pose { get; set; }
    public Dictionary<string, double>? Params { get; set; }
}

internal sealed class ConnectionDto
{
    public string? Id { get; set; }
    public string? PartA { get; set; }
    public string? ConnectorA { get; set; }
    public string? PartB { get; set; }
    public string? ConnectorB { get; set; }
}

internal sealed class BoardDto
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, string>? Pins { get; set; }
}

internal sealed class WireDto
{
    public string? Component { get; set; }
    public string? Signal { get; set; }
    public string? Board { get; set; }
    public string? Pin { get; set; }
}

internal sealed class PartDefinitionDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? HilDevice { get; set; }
    public List<PartBodyDto>? Bodies { get; set; }
    public List<ConnectorDto>? Connectors { get; set; }
    public MotorDto? Motor { get; set; }
    public Dictionary<string, double>? Params { get; set; }
    public Dictionary<string, string>? ParamsExpr { get; set; }
    public bool? IsTransmissionElement { get; set; }
}

internal sealed class PartBodyDto
{
    public string? Name { get; set; }
    public double? MassKg { get; set; }
    public List<ShapeDto>? Shapes { get; set; }
    public string? ParentBody { get; set; }
    public PoseDto? RelativePose { get; set; }
    public string? Actuated { get; set; }
}

internal sealed class ShapeDto
{
    public string? Kind { get; set; }
    public double[]? Extents { get; set; }
    public string[]? ExtentsExpr { get; set; }
    public string? MeshFile { get; set; }
    public double[]? MeshScale { get; set; }
    public bool? Collision { get; set; }
    public bool? Visible { get; set; }
    public bool? ContributesMass { get; set; }
    public bool? SmoothNormal { get; set; }
    public PoseDto? RelativePose { get; set; }
    public string[]? PosExpr { get; set; }
    public string? Rgba { get; set; }
}

internal sealed class ConnectorDto
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public PoseDto? Pose { get; set; }
    public string[]? PosExpr { get; set; }
    public double[]? Axis { get; set; }
    public string? Body { get; set; }
}

internal sealed class MotorDto
{
    public string? Kind { get; set; }
    public double? StepAngleDeg { get; set; }
    public double? HoldingTorqueNm { get; set; }
    public double? RatedCurrentA { get; set; }
    public double? MaxSpeedRps { get; set; }
    public double? RotorInertiaKgM2 { get; set; }
}

internal sealed class PoseDto
{
    public double[]? Pos { get; set; }
    public double[]? Rot { get; set; }
}
