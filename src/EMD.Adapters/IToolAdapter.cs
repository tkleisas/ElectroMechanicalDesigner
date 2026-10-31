using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

public interface IToolAdapter
{
    ToolDescriptor Descriptor { get; }

    Task<AdapterResult> ExecuteAsync(
        AdapterRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ToolDescriptor
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Version { get; init; } = "";

    public string Executable { get; init; } = "";

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public string AdapterContract { get; init; } = "0.1";
}

public sealed record AdapterInput
{
    public string Path { get; init; } = "";

    public string Format { get; init; } = "";

    public string? Sha256 { get; init; }
}

public sealed record AdapterRequest
{
    public string Operation { get; init; } = "";

    public JsonObject? Parameters { get; init; }

    public IReadOnlyList<AdapterInput> Inputs { get; init; } = [];
}

public sealed record AdapterArtifact
{
    public string Path { get; init; } = "";

    public string Format { get; init; } = "";

    public ArtifactRole Role { get; init; }

    public string? Sha256 { get; init; }

    public long? Bytes { get; init; }
}

public sealed record AdapterResult
{
    public bool Success { get; init; }

    public IReadOnlyList<AdapterArtifact> Artifacts { get; init; } = [];

    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];

    public JsonObject? Data { get; init; }
}
