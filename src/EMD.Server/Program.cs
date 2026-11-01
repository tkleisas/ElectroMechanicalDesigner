using Emd.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    web.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = web.Build();
    app.MapMcp("/mcp");
    app.MapGet("/", () => Results.Content(WebUi.Html, "text/html"));
    app.MapGet("/api/health", () => Results.Json(new { status = "ok", server = ServerInfo.Name }));
    app.MapGet("/api/project", (string path) =>
    {
        var loaded = Emd.Core.Projects.ProjectLoader.Default.LoadFile(path);
        return loaded.Project is null
            ? Results.Json(new { error = "invalid project", diagnostics = loaded.Diagnostics.Select(d => d.ToString()) }, statusCode: 400)
            : Results.Json(EmdTools.SummaryJson(loaded.Project));
    });
    app.MapGet("/api/trace", (string path) => Results.Json(EmdTools.TraceSummaryJson(path)));
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
