using System.Net.Http.Json;
using Emd.Contracts;

namespace Emd.Server;

public sealed class HostClient
{
    private readonly HttpClient? _http;

    public HostClient(string? baseUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute) };
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public bool IsConfigured => _http is not null;

    public async Task<HostCapabilities?> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (_http is null)
            return null;
        return await _http.GetFromJsonAsync<HostCapabilities>("api/host", cancellationToken);
    }

    public async Task<HostCommandResult?> ExecuteAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_http is null)
            return null;
        var response = await _http.PostAsJsonAsync("api/host/execute", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<HostCommandResult>(cancellationToken: cancellationToken);
    }
}
