using System.Net;
using System.Net.Http.Json;
using Emd.Contracts;

namespace Emd.Web.Services;

public sealed class WorkspaceClient(HttpClient http)
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<WorkspaceSummary[]>("/api/workspaces", cancellationToken) ?? [];

    public async Task<WorkspaceSummary> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("/api/workspaces", request, cancellationToken);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<WorkspaceSummary>(cancellationToken: cancellationToken))!;
    }

    public async Task<WorkspaceDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"/api/workspaces/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<WorkspaceDetail>(cancellationToken: cancellationToken);
    }

    public async Task<WorkspaceLease> AcquireLeaseAsync(Guid id, AcquireLeaseRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync($"/api/workspaces/{id}/leases", request, cancellationToken);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<WorkspaceLease>(cancellationToken: cancellationToken))!;
    }

    public async Task ReleaseLeaseAsync(Guid workspaceId, Guid leaseId, string owner, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/leases/{leaseId}/release",
            new ReleaseLeaseRequest(owner),
            cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task<WorkspaceRevision> CommitAsync(Guid id, CommitRevisionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync($"/api/workspaces/{id}/revisions", request, cancellationToken);
        await EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<WorkspaceRevision>(cancellationToken: cancellationToken))!;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"EMD Server returned {(int)response.StatusCode}: {body}");
    }
}
