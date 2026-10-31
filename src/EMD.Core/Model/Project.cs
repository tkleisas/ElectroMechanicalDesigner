using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Emd.Core.Model;

public sealed record EmdProject
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "0.1";

    public Guid Id { get; init; }

    public string? Key { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public string? Revision { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }

    public List<string>? Authors { get; init; }

    public string? License { get; init; }

    public Units Units { get; init; } = new();

    public CoordinateSystem CoordinateSystem { get; init; } = new();

    public Toolchain? Toolchain { get; init; }

    public List<CatalogRef>? Catalogs { get; init; }

    public List<Artifact> Artifacts { get; init; } = [];

    public List<Part> Parts { get; init; } = [];

    public List<Connection> Connections { get; init; } = [];

    public MechanicalSection? Mechanical { get; init; }

    public ElectricalSection? Electrical { get; init; }

    public FirmwareSection? Firmware { get; init; }

    public BehaviorSection? Behavior { get; init; }

    public SimulationSection? Simulation { get; init; }

    public ManufacturingSection? Manufacturing { get; init; }

    public ValidationSection? Validation { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record Units
{
    public string System { get; init; } = "SI";

    public string Length { get; init; } = "m";

    public string Mass { get; init; } = "kg";

    public string Time { get; init; } = "s";

    public string Angle { get; init; } = "deg";

    public string Temperature { get; init; } = "K";

    public string Current { get; init; } = "A";

    public string Voltage { get; init; } = "V";
}

public sealed record CoordinateSystem
{
    public string Handedness { get; init; } = "right";

    public string UpAxis { get; init; } = "+Z";

    public string ForwardAxis { get; init; } = "+X";

    public RotationConvention Rotation { get; init; } = new();
}

public sealed record RotationConvention
{
    public List<string> Order { get; init; } = ["X", "Y", "Z"];

    public bool Extrinsic { get; init; } = true;
}

public sealed record Toolchain
{
    public string AdapterContract { get; init; } = "";

    public List<Tool> Tools { get; init; } = [];
}

public sealed record Tool
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Version { get; init; } = "";

    public string? Executable { get; init; }

    public string? Adapter { get; init; }

    public List<string>? Capabilities { get; init; }

    public bool? Pinned { get; init; }

    public Dictionary<string, string>? Env { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record CatalogRef
{
    public string Id { get; init; } = "";

    public string Source { get; init; } = "";

    public string? Version { get; init; }

    public string? Format { get; init; }

    public Guid? ArtifactId { get; init; }
}

public sealed record PartCatalogRef
{
    public string? Catalog { get; init; }

    public string Part { get; init; } = "";

    public string? Version { get; init; }
}

public sealed record ToolRef
{
    public string Tool { get; init; } = "";

    public string? Version { get; init; }

    public string? Adapter { get; init; }
}

public sealed record Artifact
{
    public Guid Id { get; init; }

    public string? Key { get; init; }

    public string Path { get; init; } = "";

    public string Format { get; init; } = "";

    public string? MediaType { get; init; }

    public ArtifactRole Role { get; init; }

    public string? Sha256 { get; init; }

    public long? Bytes { get; init; }

    public ToolRef? ProducedBy { get; init; }

    public List<Guid> Inputs { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record Material
{
    public string? Name { get; init; }

    public double? DensityKgM3 { get; init; }

    public double? YoungsModulusPa { get; init; }

    public double? PoissonRatio { get; init; }

    public double? ThermalConductivityWMK { get; init; }

    public JsonObject? Metadata { get; init; }
}
