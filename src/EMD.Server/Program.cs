using System.Security.Cryptography;
using System.Text;
using Emd.Coordinator;
using Emd.Core.Artifacts;
using Emd.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var coordinatorRoot = Environment.GetEnvironmentVariable("EMD_COORDINATOR_ROOT");
var coordinatorArg = Array.IndexOf(args, "--coordinator-root");
if (coordinatorArg >= 0 && coordinatorArg + 1 < args.Length)
    coordinatorRoot = args[coordinatorArg + 1];
coordinatorRoot ??= Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "emd", "coordinator");
var coordinator = new CoordinatorService(coordinatorRoot);
var artifactRoot = Environment.GetEnvironmentVariable("EMD_ARTIFACT_ROOT")
    ?? Path.Combine(coordinatorRoot, "artifacts");
var artifacts = new ArtifactStore(artifactRoot);
var host = new HostClient(
    Environment.GetEnvironmentVariable("EMD_HOST_URL"),
    Environment.GetEnvironmentVariable("EMD_HOST_TOKEN"));
CoordinatorTools.Configure(coordinator, Environment.GetEnvironmentVariable("EMD_COORDINATOR_URL"));

if (args.Contains("--http"))
{
    var port = 8080;
    var httpIndex = Array.IndexOf(args, "--http");
    if (httpIndex >= 0 && httpIndex + 1 < args.Length &&
        int.TryParse(args[httpIndex + 1], out var requested))
        port = requested;

    var web = WebApplication.CreateBuilder(args);
    web.Logging.ClearProviders();
    web.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    web.WebHost.UseUrls($"http://127.0.0.1:{port}");
    var webOrigins = (Environment.GetEnvironmentVariable("EMD_WEB_ORIGINS")
        ?? "http://localhost:5005,http://127.0.0.1:5005")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    web.Services.AddCors(options => options.AddPolicy(
        "emd-web",
        policy => policy.WithOrigins(webOrigins).AllowAnyHeader().AllowAnyMethod()));
    web.Services.AddSingleton(coordinator);
    web.Services.AddSingleton(artifacts);
    web.Services.AddSingleton(host);
    web.Services.AddSingleton<HostHub>();
    web.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = web.Build();
    app.UseCors("emd-web");
    app.UseWebSockets();
    var apiToken = Environment.GetEnvironmentVariable("EMD_API_TOKEN");
    if (!string.IsNullOrWhiteSpace(apiToken))
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.Equals("/api/health") ||
                context.Request.Path.Equals("/api/hosts/ws") ||
                HttpMethods.IsOptions(context.Request.Method))
            {
                await next();
                return;
            }

            var header = context.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            var supplied = header.StartsWith(prefix, StringComparison.Ordinal)
                ? header[prefix.Length..]
                : "";
            var expectedBytes = Encoding.UTF8.GetBytes(apiToken);
            var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            if (expectedBytes.Length != suppliedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }

            await next();
        });
    }
    app.MapMcp("/mcp");
    app.MapCoordinatorApi();
    app.MapArtifactApi();
    app.MapHostApi();
    app.MapHostWebSocket();
    app.MapGet("/", () => Results.Content(WebUi.Html, "text/html"));
    app.MapGet("/api/health", () => Results.Json(new { status = "ok", server = ServerInfo.Name }));
    app.MapGet("/api/project", (string path) =>
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(path);
        return loaded.Project is null
            ? Results.Json(new { error = "invalid project", diagnostics = loaded.Diagnostics.Select(d => d.ToString()) }, statusCode: 400)
            : Results.Json(EmdTools.SummaryJson(loaded.Project));
    });
    app.MapGet("/api/graph", (string path) =>
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(path);
        return loaded.Project is null
            ? Results.Json(new { error = "invalid project" }, statusCode: 400)
            : Results.Json(EmdTools.GraphJson(loaded.Project));
    });
    app.MapGet("/api/scene", (string path) =>
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(path);
        return loaded.Project is null
            ? Results.Json(new { error = "invalid project" }, statusCode: 400)
            : Results.Json(EmdTools.SceneJson(loaded.Project));
    });
    app.MapGet("/api/validate", (string path) =>
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(path);
        return loaded.Project is null
            ? Results.Json(new { error = "invalid project" }, statusCode: 400)
            : Results.Json(EmdTools.ValidationJson(loaded.Project));
    });
    app.MapGet("/api/trace", (string path) => Results.Json(EmdTools.TraceSummaryJson(path)));
    app.MapGet("/api/trace-chart", (string path) => Results.Json(EmdTools.TraceChartJson(path)));
    app.MapGet("/api/package", (string dir) => Results.Json(EmdTools.PackageSummaryJson(dir)));
    app.MapGet("/api/gcode", (string path, string? envelope, double? maxFeed) =>
        Results.Text(EmdTools.CheckGCode(path, envelope, maxFeed), "text/plain"));
    app.Run();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
