using Emd.Contracts;
using Emd.Coordinator;

namespace Emd.Coordinator.Tests;

public sealed class CoordinatorServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "emd-coordinator-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    public CoordinatorServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private CoordinatorService Service() => new(_root, _time);

    [Fact]
    public async Task Workspace_create_list_and_get_persist()
    {
        var service = Service();
        var created = await service.CreateWorkspaceAsync(new CreateWorkspaceRequest(
            "nsr1", "NSR-1", "https://example.test/groovebox.git"));

        Assert.Equal("nsr1", created.Key);
        Assert.Equal("main", created.Revision);

        var reloaded = Service();
        var list = await reloaded.ListWorkspacesAsync();
        Assert.Single(list);
        var detail = await reloaded.GetWorkspaceAsync(created.Id);
        Assert.NotNull(detail);
        Assert.Equal("NSR-1", detail!.Workspace.Name);
        Assert.Empty(detail.Revisions);
    }

    [Fact]
    public async Task Duplicate_workspace_key_is_rejected()
    {
        var service = Service();
        await service.CreateWorkspaceAsync(new CreateWorkspaceRequest("nsr1", "NSR-1", "repo"));
        await Assert.ThrowsAsync<CoordinatorConflictException>(() =>
            service.CreateWorkspaceAsync(new CreateWorkspaceRequest("nsr1", "Other", "repo")));
    }

    [Fact]
    public async Task Lease_is_exclusive_and_expires()
    {
        var service = Service();
        var workspace = await service.CreateWorkspaceAsync(new CreateWorkspaceRequest("nsr1", "NSR-1", "repo"));
        var lease = await service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("alice", "job/a", 60));
        Assert.NotNull(lease);

        await Assert.ThrowsAsync<CoordinatorConflictException>(() =>
            service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("bob", "job/a", 60)));

        _time.Advance(TimeSpan.FromSeconds(61));
        var next = await service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("bob", "job/a", 60));
        Assert.NotNull(next);
        Assert.Equal("bob", next!.Owner);
    }

    [Fact]
    public async Task Commit_requires_the_active_lease_owner_and_branch()
    {
        var service = Service();
        var workspace = await service.CreateWorkspaceAsync(new CreateWorkspaceRequest("nsr1", "NSR-1", "repo"));
        await service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("alice", "job/a", 60));

        await Assert.ThrowsAsync<CoordinatorConflictException>(() =>
            service.CommitRevisionAsync(workspace.Id, new CommitRevisionRequest(
                "bob", "job/a", "deadbeef", "wrong owner")));
        await Assert.ThrowsAsync<CoordinatorConflictException>(() =>
            service.CommitRevisionAsync(workspace.Id, new CommitRevisionRequest(
                "alice", "job/b", "deadbeef", "wrong branch")));

        var revision = await service.CommitRevisionAsync(workspace.Id, new CommitRevisionRequest(
            "alice", "job/a", "deadbeef", "route boards", ["artifact://sha256/abc"]));
        Assert.Equal("deadbeef", revision.Commit);
        Assert.Single(revision.Artifacts);

        var detail = await service.GetWorkspaceAsync(workspace.Id);
        Assert.Equal("deadbeef", detail!.Workspace.Revision);
        Assert.Single(detail.Revisions);
    }

    [Fact]
    public async Task Release_lease_allows_another_owner()
    {
        var service = Service();
        var workspace = await service.CreateWorkspaceAsync(new CreateWorkspaceRequest("nsr1", "NSR-1", "repo"));
        var lease = await service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("alice", "job/a", 60));
        await service.ReleaseLeaseAsync(workspace.Id, lease!.Id, "alice");

        var next = await service.AcquireLeaseAsync(workspace.Id, new AcquireLeaseRequest("bob", "job/a", 60));
        Assert.Equal("bob", next!.Owner);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
