using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Emd.Contracts;

namespace Emd.Host;

public sealed class HostWebSocketClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HostExecutor _executor;
    private readonly Uri _url;
    private readonly string? _token;

    public HostWebSocketClient(HostExecutor executor, Uri url, string? token)
    {
        _executor = executor;
        _url = url;
        _token = token;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Reconnect below.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectOnceAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(_token))
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_token}");
        await socket.ConnectAsync(_url, cancellationToken);
        var capabilities = await _executor.ProbeAsync(cancellationToken);
        await SendAsync(socket, new HostHello("hello", capabilities.AgentId, capabilities), cancellationToken);

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;
            var json = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);
            var command = JsonSerializer.Deserialize<HostCommandEnvelope>(json, JsonOptions);
            if (command?.Type != "command" || string.IsNullOrWhiteSpace(command.Id))
                continue;
            var execution = await _executor.ExecuteAsync(command.Request, cancellationToken);
            await SendAsync(
                socket,
                new HostResultEnvelope("result", command.Id, execution),
                cancellationToken);
        }
    }

    private static Task SendAsync<T>(ClientWebSocket socket, T value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
