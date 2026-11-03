using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Emd.Contracts;

namespace Emd.Server;

public sealed class HostHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, HostConnection> _connections = new();

    public IReadOnlyList<HostCapabilities> List() =>
        _connections.Values.Select(connection => connection.Capabilities).ToList();

    public Task RegisterAsync(HostConnection connection, CancellationToken cancellationToken = default)
    {
        _connections[connection.AgentId] = connection;
        return Task.CompletedTask;
    }

    public void Remove(string agentId, HostConnection connection) =>
        _connections.TryRemove(new KeyValuePair<string, HostConnection>(agentId, connection));

    public async Task<HostCommandResult> ExecuteAsync(
        string? agentId,
        HostCommandRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var connection = string.IsNullOrWhiteSpace(agentId)
            ? _connections.Values.FirstOrDefault()
            : _connections.GetValueOrDefault(agentId);
        if (connection is null)
            throw new InvalidOperationException("No host agent is connected.");
        return await connection.ExecuteAsync(request, timeout, cancellationToken);
    }
}

public sealed class HostConnection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HostCommandResult>> _pending = new();

    public HostConnection(WebSocket socket, HostCapabilities capabilities)
    {
        _socket = socket;
        Capabilities = capabilities;
    }

    public string AgentId => Capabilities.AgentId;
    public HostCapabilities Capabilities { get; }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));
        await _send.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }

    public async Task<HostCommandResult> ExecuteAsync(
        HostCommandRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<HostCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Could not allocate a host request id.");
        try
        {
            await SendAsync(new HostCommandEnvelope("command", id, request), cancellationToken);
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public void Complete(string id, HostCommandResult result)
    {
        if (_pending.TryGetValue(id, out var completion))
            completion.TrySetResult(result);
    }

    public async Task ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;
            var json = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);
            using var document = JsonDocument.Parse(json);
            var type = document.RootElement.TryGetProperty("type", out var typeNode)
                ? typeNode.GetString()
                : null;
            if (type == "result" &&
                document.RootElement.TryGetProperty("id", out var idNode) &&
                document.RootElement.TryGetProperty("result", out var resultNode))
            {
                var id = idNode.GetString();
                var payload = resultNode.Deserialize<HostCommandResult>(JsonOptions);
                if (id is not null && payload is not null)
                    Complete(id, payload);
            }
        }
    }
}
