using System.Text.Json.Nodes;
using Emd.Coordinator;
using Emd.Server;

namespace Emd.Server.Tests;

public sealed class CoordinatorToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "emd-server-coordinator-tests", Guid.NewGuid().ToString("N"));

    public CoordinatorToolsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Coordinator_tools_create_lease_and_commit()
    {
        CoordinatorTools.Configure(new CoordinatorService(_root));
        var created = JsonNode.Parse(await CoordinatorTools.CreateWorkspace(
            "nsr1", "NSR-1", "https://example.test/groovebox.git"))!.AsObject();
        var workspaceId = (string)created["id"]!;

        var lease = JsonNode.Parse(await CoordinatorTools.AcquireLease(
            workspaceId, "alice", "job/a", 60))!.AsObject();
        Assert.Equal("alice", (string)lease["owner"]!);

        var revision = JsonNode.Parse(await CoordinatorTools.CommitRevision(
            workspaceId, "alice", "job/a", "deadbeef", "smoke", "artifact://sha256/abc"))!.AsObject();
        Assert.Equal("deadbeef", (string)revision["commit"]!);

        var detail = JsonNode.Parse(await CoordinatorTools.GetWorkspace(workspaceId))!.AsObject();
        Assert.Equal("deadbeef", (string)detail["workspace"]!["revision"]!);
        Assert.Single(detail["revisions"]!.AsArray());
    }
}
