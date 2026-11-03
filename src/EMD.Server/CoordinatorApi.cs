using Emd.Contracts;
using Emd.Coordinator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Emd.Server;

public static class CoordinatorApi
{
    public static void MapCoordinatorApi(this WebApplication app)
    {
        var workspaces = app.MapGroup("/api/workspaces");

        workspaces.MapGet("/", async (CoordinatorService coordinator) =>
            Results.Ok(await coordinator.ListWorkspacesAsync()));

        workspaces.MapPost("/", async (CreateWorkspaceRequest request, CoordinatorService coordinator) =>
            await Wrap(async () => Results.Ok(await coordinator.CreateWorkspaceAsync(request))));

        workspaces.MapGet("/{workspaceId:guid}", async (Guid workspaceId, CoordinatorService coordinator) =>
            await Wrap(async () =>
            {
                var detail = await coordinator.GetWorkspaceAsync(workspaceId);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            }));

        workspaces.MapPost("/{workspaceId:guid}/leases",
            async (Guid workspaceId, AcquireLeaseRequest request, CoordinatorService coordinator) =>
                await Wrap(async () => Results.Ok(await coordinator.AcquireLeaseAsync(workspaceId, request))));

        workspaces.MapPost("/{workspaceId:guid}/leases/{leaseId:guid}/release",
            async (Guid workspaceId, Guid leaseId, ReleaseLeaseRequest request,
                CoordinatorService coordinator) =>
                await Wrap(async () =>
                {
                    await coordinator.ReleaseLeaseAsync(workspaceId, leaseId, request.Owner);
                    return Results.NoContent();
                }));

        workspaces.MapGet("/{workspaceId:guid}/revisions",
            async (Guid workspaceId, CoordinatorService coordinator) =>
                await Wrap(async () => Results.Ok(await coordinator.ListRevisionsAsync(workspaceId))));

        workspaces.MapPost("/{workspaceId:guid}/revisions",
            async (Guid workspaceId, CommitRevisionRequest request, CoordinatorService coordinator) =>
                await Wrap(async () => Results.Ok(await coordinator.CommitRevisionAsync(workspaceId, request))));
    }

    private static async Task<IResult> Wrap(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CoordinatorNotFoundException exception)
        {
            return Results.Json(new { error = exception.Message }, statusCode: 404);
        }
        catch (CoordinatorConflictException exception)
        {
            return Results.Json(new { error = exception.Message }, statusCode: 409);
        }
        catch (ArgumentException exception)
        {
            return Results.Json(new { error = exception.Message }, statusCode: 400);
        }
    }
}
