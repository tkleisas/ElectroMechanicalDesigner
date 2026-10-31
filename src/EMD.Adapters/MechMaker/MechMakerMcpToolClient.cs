using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Emd.Adapters.MechMaker;

/// <summary>MCP stdio client for the MechMaker server.</summary>
public sealed class MechMakerMcpToolClient : IMechMakerToolClient, IAsyncDisposable
{
    private readonly McpClient _client;

    private MechMakerMcpToolClient(McpClient client)
    {
        _client = client;
    }

    public static async Task<MechMakerMcpToolClient> ConnectAsync(
        string command,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var options = new StdioClientTransportOptions
        {
            Command = command,
            Arguments = arguments.ToList(),
            Name = "mechmaker",
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environmentVariables is null
                ? null
                : new Dictionary<string, string?>(environmentVariables)
        };

        var client = await McpClient.CreateAsync(
            new StdioClientTransport(options),
            cancellationToken: cancellationToken);
        return new MechMakerMcpToolClient(client);
    }

    public async Task<MechMakerToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
            var text = string.Join(
                "\n",
                result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            return new MechMakerToolResult(result.IsError == true, text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MechMakerToolResult(true, exception.Message);
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
