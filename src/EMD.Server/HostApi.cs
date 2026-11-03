using Emd.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Emd.Server;

public static class HostApi
{
    public static void MapHostApi(this WebApplication app)
    {
        app.MapGet("/api/hosts", (HostHub hub) => Results.Ok(hub.List()));

        app.MapGet("/api/host", async (HostHub hub, HostClient client, CancellationToken cancellationToken) =>
        {
            var capabilities = hub.List().FirstOrDefault();
            if (capabilities is null)
                capabilities = await client.ProbeAsync(cancellationToken);
            return capabilities is null
                ? Results.Json(new { error = "host agent is not configured" }, statusCode: 503)
                : Results.Ok(capabilities);
        });

        app.MapPost("/api/host/execute", async (
            HostCommandRequest request,
            HostHub hub,
            HostClient client,
            CancellationToken cancellationToken) =>
        {
            if (hub.List().Count > 0)
                return Results.Ok(await hub.ExecuteAsync(null, request, TimeSpan.FromSeconds(60), cancellationToken));
            var result = await client.ExecuteAsync(request, cancellationToken);
            return result is null
                ? Results.Json(new { error = "host agent is not configured" }, statusCode: 503)
                : Results.Ok(result);
        });
    }
}
