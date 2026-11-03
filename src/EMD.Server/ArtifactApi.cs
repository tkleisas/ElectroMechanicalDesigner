using Emd.Core.Artifacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Emd.Server;

public static class ArtifactApi
{
    public static void MapArtifactApi(this WebApplication app)
    {
        app.MapPost("/api/artifacts", async (HttpRequest request, ArtifactStore store) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            var result = store.Put(buffer.ToArray());
            return Results.Ok(result);
        });

        app.MapGet("/api/artifacts/{sha256}", (string sha256, ArtifactStore store) =>
        {
            if (!store.Contains(sha256))
                return Results.NotFound();
            return Results.File(store.GetPath(sha256), "application/octet-stream");
        });
    }
}
