namespace Emd.Contracts;

public sealed record HostTool(
    string Name,
    string? Version,
    bool Gui);

public sealed record HostCapabilities(
    string AgentId,
    string MachineName,
    string WorkspaceRoot,
    IReadOnlyList<HostTool> Tools,
    DateTimeOffset StartedAt);

public sealed record HostArtifact(
    string Path,
    string? Sha256,
    long Bytes);

public sealed record HostCommandRequest(
    string Command,
    string? WorkspaceKey = null,
    string? Repository = null,
    string? Branch = null,
    string? Path = null,
    string? Message = null,
    string? AuthorName = null,
    string? AuthorEmail = null,
    string? Tool = null);

public sealed record HostCommandResult(
    bool Success,
    string? Message = null,
    string? Commit = null,
    string? Path = null,
    IReadOnlyList<HostArtifact>? Artifacts = null,
    IReadOnlyList<string>? Logs = null);
