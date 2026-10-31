using System.Text.Json.Nodes;

namespace Emd.Core.Model;

public sealed record Endpoint
{
    public Guid? PartId { get; init; }

    public string? PortId { get; init; }

    public Guid? NetId { get; init; }

    public string? NodeId { get; init; }

    public Guid? ArtifactId { get; init; }

    public string? Variable { get; init; }

    public string? Uri { get; init; }
}

public sealed record Connection
{
    public Guid Id { get; init; }

    public string? Key { get; init; }

    public ConnectionKind Kind { get; init; }

    public Endpoint From { get; init; } = new();

    public Endpoint To { get; init; } = new();

    public Pose? Transform { get; init; }

    public JsonObject? Attributes { get; init; }

    public JsonObject? Metadata { get; init; }
}
