using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Emd.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Emd.Server;

public static class HostWebSocketApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapHostWebSocket(this WebApplication app)
    {
        app.Map("/api/hosts/ws", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!Authorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var hello = await ReadHelloAsync(socket, context.RequestAborted);
            if (hello is null)
            {
                await socket.CloseAsync(WebSocketCloseStatus.ProtocolError, "hello required", CancellationToken.None);
                return;
            }

            var connection = new HostConnection(socket, hello.Capabilities);
            var hub = context.RequestServices.GetRequiredService<HostHub>();
            await hub.RegisterAsync(connection, context.RequestAborted);
            try
            {
                await connection.ReceiveAsync(context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Server shutdown.
            }
            finally
            {
                hub.Remove(hello.AgentId, connection);
            }
        });
    }

    private static async Task<HostHello?> ReadHelloAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;
            return JsonSerializer.Deserialize<HostHello>(message.ToArray(), JsonOptions);
        }

        return null;
    }

    private static bool Authorized(HttpContext context)
    {
        var expected = Environment.GetEnvironmentVariable("EMD_HOST_TOKEN");
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var supplied = header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..] : "";
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
