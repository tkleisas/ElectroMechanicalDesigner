using System.Security.Cryptography;
using System.Text;
using Emd.Contracts;
using Emd.Host;

var workspaceRoot = Environment.GetEnvironmentVariable("EMD_HOST_WORKSPACE_ROOT")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emd-host");
var rootArg = Array.IndexOf(args, "--workspace-root");
if (rootArg >= 0 && rootArg + 1 < args.Length)
    workspaceRoot = args[rootArg + 1];
var agentId = Environment.GetEnvironmentVariable("EMD_HOST_ID")
    ?? $"{Environment.MachineName}-{Environment.ProcessId}";
var port = 8090;
var httpArg = Array.IndexOf(args, "--http");
if (httpArg >= 0 && httpArg + 1 < args.Length && int.TryParse(args[httpArg + 1], out var requested))
    port = requested;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var executor = new HostExecutor(workspaceRoot, agentId);
builder.Services.AddSingleton(executor);

var app = builder.Build();
var token = Environment.GetEnvironmentVariable("EMD_HOST_TOKEN");
if (!string.IsNullOrWhiteSpace(token))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.Equals("/api/health") ||
            HttpMethods.IsOptions(context.Request.Method))
        {
            await next();
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var supplied = header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..] : "";
        var expectedBytes = Encoding.UTF8.GetBytes(token);
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

app.MapGet("/api/health", () => Results.Json(new { status = "ok", agent = agentId }));
app.MapGet("/api/host", async (HostExecutor host, CancellationToken cancellationToken) =>
    Results.Ok(await host.ProbeAsync(cancellationToken)));
app.MapPost("/api/host/execute", async (
        HostCommandRequest request,
        HostExecutor host,
        CancellationToken cancellationToken) =>
    Results.Ok(await host.ExecuteAsync(request, cancellationToken)));

var coordinatorWs = Environment.GetEnvironmentVariable("EMD_COORDINATOR_WS_URL");
if (!string.IsNullOrWhiteSpace(coordinatorWs))
{
    var client = new HostWebSocketClient(executor, new Uri(coordinatorWs), token);
    _ = Task.Run(() => client.RunAsync(app.Lifetime.ApplicationStopping));
}

app.Run();
