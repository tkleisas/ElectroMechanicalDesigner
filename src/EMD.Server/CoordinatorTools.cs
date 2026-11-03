using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Emd.Contracts;
using Emd.Coordinator;
using ModelContextProtocol.Server;

namespace Emd.Server;

[McpServerToolType]
public static class CoordinatorTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static CoordinatorService? _coordinator;
    private static HttpClient? _http;
    private static Uri? _coordinatorUrl;

    public static void Configure(CoordinatorService coordinator, string? coordinatorUrl = null)
    {
        _coordinator = coordinator;
        if (string.IsNullOrWhiteSpace(coordinatorUrl))
        {
            _http = null;
            _coordinatorUrl = null;
            return;
        }

        _coordinatorUrl = new Uri(coordinatorUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _http = new HttpClient { BaseAddress = _coordinatorUrl };
        var token = Environment.GetEnvironmentVariable("EMD_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    private static CoordinatorService Coordinator =>
        _coordinator ?? throw new InvalidOperationException("Coordinator is not configured.");

    private static bool Remote => _http is not null;

    private static async Task<string> GetJson(string path) =>
        await _http!.GetStringAsync(path);

    private static async Task<string> PostJson<T>(string path, T value)
    {
        var response = await _http!.PostAsJsonAsync(path, value, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
            return body;
        return $"error: {(int)response.StatusCode} {body}";
    }

    private static async Task<string> ReleaseRemote(Guid workspaceId, Guid leaseId, string owner)
    {
        var response = await _http!.PostAsJsonAsync(
            $"api/workspaces/{workspaceId}/leases/{leaseId}/release",
            new ReleaseLeaseRequest(owner),
            JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode ? "released" : $"error: {(int)response.StatusCode} {body}";
    }

    [McpServerTool(Name = "list_workspaces")]
    [Description("List coordinator workspaces, revisions, and current lease state.")]
    public static async Task<string> ListWorkspaces()
    {
        if (Remote)
            return await GetJson("api/workspaces");
        var workspaces = await Coordinator.ListWorkspacesAsync();
        return JsonSerializer.Serialize(workspaces, JsonOptions);
    }

    [McpServerTool(Name = "create_workspace")]
    [Description("Register a Git-backed EMD workspace with the coordinator.")]
    public static async Task<string> CreateWorkspace(
        [Description("Stable workspace key")] string key,
        [Description("Display name")] string name,
        [Description("Canonical Git repository URL")] string repository)
    {
        try
        {
            if (Remote)
                return await PostJson("api/workspaces", new CreateWorkspaceRequest(key, name, repository));
            var workspace = await Coordinator.CreateWorkspaceAsync(
                new CreateWorkspaceRequest(key, name, repository));
            return JsonSerializer.Serialize(workspace, JsonOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or CoordinatorConflictException)
        {
            return $"error: {exception.Message}";
        }
    }

    [McpServerTool(Name = "get_workspace")]
    [Description("Return one coordinator workspace with its lease and revisions.")]
    public static async Task<string> GetWorkspace(
        [Description("Workspace UUID")] string workspaceId)
    {
        if (!Guid.TryParse(workspaceId, out var id))
            return "error: invalid workspace UUID";
        try
        {
            if (Remote)
            {
                var response = await _http!.GetAsync($"api/workspaces/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return "error: workspace not found";
                var body = await response.Content.ReadAsStringAsync();
                return response.IsSuccessStatusCode
                    ? body
                    : $"error: {(int)response.StatusCode} {body}";
            }

            var detail = await Coordinator.GetWorkspaceAsync(id);
            return detail is null
                ? "error: workspace not found"
                : JsonSerializer.Serialize(detail, JsonOptions);
        }
        catch (CoordinatorNotFoundException exception)
        {
            return $"error: {exception.Message}";
        }
    }

    [McpServerTool(Name = "acquire_workspace_lease")]
    [Description("Acquire or renew an exclusive workspace/branch write lease.")]
    public static async Task<string> AcquireLease(
        [Description("Workspace UUID")] string workspaceId,
        [Description("Lease owner identity")] string owner,
        [Description("Job branch")] string branch,
        [Description("Lease TTL in seconds")] int ttlSeconds = 900)
    {
        if (!Guid.TryParse(workspaceId, out var id))
            return "error: invalid workspace UUID";
        try
        {
            if (Remote)
                return await PostJson(
                    $"api/workspaces/{id}/leases",
                    new AcquireLeaseRequest(owner, branch, ttlSeconds));
            var lease = await Coordinator.AcquireLeaseAsync(
                id, new AcquireLeaseRequest(owner, branch, ttlSeconds));
            return JsonSerializer.Serialize(lease, JsonOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or CoordinatorConflictException or CoordinatorNotFoundException)
        {
            return $"error: {exception.Message}";
        }
    }

    [McpServerTool(Name = "release_workspace_lease")]
    [Description("Release a workspace lease owned by the caller.")]
    public static async Task<string> ReleaseLease(
        [Description("Workspace UUID")] string workspaceId,
        [Description("Lease UUID")] string leaseId,
        [Description("Lease owner identity")] string owner)
    {
        if (!Guid.TryParse(workspaceId, out var workspace) ||
            !Guid.TryParse(leaseId, out var lease))
            return "error: invalid workspace or lease UUID";
        try
        {
            if (Remote)
                return await ReleaseRemote(workspace, lease, owner);
            await Coordinator.ReleaseLeaseAsync(workspace, lease, owner);
            return "released";
        }
        catch (Exception exception) when (exception is CoordinatorConflictException or CoordinatorNotFoundException)
        {
            return $"error: {exception.Message}";
        }
    }

    [McpServerTool(Name = "commit_workspace_revision")]
    [Description("Record a job-branch commit and artifact references for a leased workspace.")]
    public static async Task<string> CommitRevision(
        [Description("Workspace UUID")] string workspaceId,
        [Description("Lease owner identity")] string owner,
        [Description("Job branch")] string branch,
        [Description("Git commit SHA")] string commit,
        [Description("Optional commit message")] string? message = null,
        [Description("Comma-separated artifact references")] string? artifacts = null)
    {
        if (!Guid.TryParse(workspaceId, out var id))
            return "error: invalid workspace UUID";
        try
        {
            var request = new CommitRevisionRequest(
                owner,
                branch,
                commit,
                message,
                string.IsNullOrWhiteSpace(artifacts)
                    ? []
                    : artifacts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (Remote)
                return await PostJson($"api/workspaces/{id}/revisions", request);
            var revision = await Coordinator.CommitRevisionAsync(id, request);
            return JsonSerializer.Serialize(revision, JsonOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or CoordinatorConflictException or CoordinatorNotFoundException)
        {
            return $"error: {exception.Message}";
        }
    }
}
