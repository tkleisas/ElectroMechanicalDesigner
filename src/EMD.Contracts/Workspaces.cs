namespace Emd.Contracts;

public sealed record CreateWorkspaceRequest(
    string Key,
    string Name,
    string Repository);

public sealed record WorkspaceSummary(
    Guid Id,
    string Key,
    string Name,
    string Repository,
    string Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AcquireLeaseRequest(
    string Owner,
    string Branch,
    int TtlSeconds = 900);

public sealed record ReleaseLeaseRequest(string Owner);

public sealed record CommitRevisionRequest(
    string Owner,
    string Branch,
    string Commit,
    string? Message,
    IReadOnlyList<string>? Artifacts = null);

public sealed record WorkspaceLease(
    Guid Id,
    Guid WorkspaceId,
    string Owner,
    string Branch,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt);

public sealed record WorkspaceRevision(
    Guid Id,
    Guid WorkspaceId,
    string Branch,
    string Commit,
    string? Message,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Artifacts);

public sealed record WorkspaceDetail(
    WorkspaceSummary Workspace,
    WorkspaceLease? Lease,
    IReadOnlyList<WorkspaceRevision> Revisions);
