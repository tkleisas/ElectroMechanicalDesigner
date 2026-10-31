using System.Text.Json.Nodes;

namespace Emd.Core.Model;

public sealed record Part
{
    public Guid Id { get; init; }

    public string? Key { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public PartKind Kind { get; init; }

    public PartCategory? Category { get; init; }

    public PartCatalogRef? CatalogRef { get; init; }

    public JsonObject? Params { get; init; }

    public Material? Material { get; init; }

    public double? MassKg { get; init; }

    public Pose? Pose { get; init; }

    public PartDomainRefs? DomainRefs { get; init; }

    public List<Port> Ports { get; init; } = [];

    public JsonObject? Metadata { get; init; }
}

public sealed record PartDomainRefs
{
    public DomainRef? Mechanical { get; init; }

    public DomainRef? Electrical { get; init; }

    public DomainRef? Firmware { get; init; }

    public DomainRef? Behavior { get; init; }

    public DomainRef? Simulation { get; init; }

    public DomainRef? Manufacturing { get; init; }
}

public sealed record DomainRef
{
    public string Tool { get; init; } = "";

    public string Ref { get; init; } = "";

    public string? Kind { get; init; }

    public Guid? ArtifactId { get; init; }

    public JsonObject? Attributes { get; init; }
}

public sealed record Signal
{
    public SignalType? Type { get; init; }

    public string? Unit { get; init; }

    public double? VoltageV { get; init; }

    public double? CurrentA { get; init; }

    public double? FrequencyHz { get; init; }

    public string? Protocol { get; init; }

    public double[]? Range { get; init; }
}

public sealed record Port
{
    public string Id { get; init; } = "";

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public PortDomain Domain { get; init; }

    public string Kind { get; init; } = "";

    public PortDirection? Direction { get; init; }

    public Signal? Signal { get; init; }

    public DomainRef? Ref { get; init; }

    public Pose? Pose { get; init; }

    public string? Units { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record Pose
{
    public double[]? Pos { get; init; }

    public double[]? Rot { get; init; }
}
