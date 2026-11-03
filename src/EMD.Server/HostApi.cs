using Emd.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Emd.Server;

public static class HostApi
{
    public static void MapHostApi(this WebApplication app)
    {
        app.MapGet("/api/host", async (HostClient client, CancellationToken cancellationToken) =>
        {
            var capabilities = await client.ProbeAsync(cancellationToken);
            return capabilities is null
                ? Results.Json(new { error = "host agent is not configured" }, statusCode: 503)
                : Results.Ok(capabilities);
        });

        app.MapPost("/api/host/execute", async (
            HostCommandRequest request,
            HostClient client,
            CancellationToken cancellationToken) =>
        {
            var result = await client.ExecuteAsync(request, cancellationToken);
            return result is null
                ? Results.Json(new { error = "host agent is not configured" }, statusCode: 503)
                : Results.Ok(result);
        });
    }
}
