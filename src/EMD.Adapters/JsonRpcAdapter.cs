using System.Text.Json;
using System.Text.Json.Nodes;
using Emd.Core;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

/// <summary>
/// A one-shot JSON-RPC 2.0 adapter over stdio. The request is written to the
/// adapter's stdin; a single response object is read from stdout. Diagnostics are
/// never trusted from stderr, which remains free for tool logs.
/// </summary>
public sealed class JsonRpcAdapter : IToolAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public JsonRpcAdapter(
        ToolDescriptor descriptor,
        IToolProcessRunner runner,
        TimeSpan? timeout = null)
    {
        Descriptor = descriptor;
        _runner = runner;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public ToolDescriptor Descriptor { get; }

    public async Task<AdapterResult> ExecuteAsync(
        AdapterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Descriptor.Executable))
            return Failure("EMD-ADAPTER-001", $"Adapter '{Descriptor.Id}' has no executable.");
        if (string.IsNullOrWhiteSpace(request.Operation))
            return Failure("EMD-ADAPTER-001", "Adapter operation is empty.");

        var inputs = new JsonArray();
        foreach (var input in request.Inputs)
        {
            inputs.Add(new JsonObject
            {
                ["path"] = input.Path,
                ["format"] = input.Format,
                ["sha256"] = input.Sha256
            });
        }

        var parameters = new JsonObject
        {
            ["inputs"] = inputs,
            ["parameters"] = request.Parameters?.DeepClone() ?? new JsonObject()
        };

        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["method"] = request.Operation,
            ["params"] = parameters
        };

        var run = await _runner.RunAsync(
            Descriptor.Executable,
            Descriptor.Arguments,
            payload.ToJsonString(),
            _timeout,
            cancellationToken);

        if (run.Diagnostics.Count > 0)
        {
            return new AdapterResult
            {
                Success = false,
                Diagnostics = run.Diagnostics
            };
        }

        if (run.ExitCode != 0)
        {
            return Failure(
                "EMD-ADAPTER-003",
                $"Adapter '{Descriptor.Id}' exited with code {run.ExitCode}: {run.Stderr.Trim()}");
        }

        RpcResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<RpcResponse>(run.Stdout, CoreJson.Options);
        }
        catch (JsonException exception)
        {
            return Failure(
                "EMD-ADAPTER-004",
                $"Adapter '{Descriptor.Id}' returned invalid JSON: {exception.Message}");
        }

        if (response is null)
            return Failure("EMD-ADAPTER-004", $"Adapter '{Descriptor.Id}' returned an empty response.");
        if (response.Error is not null)
        {
            var code = response.Error.Code is { } errorCode ? $" ({errorCode})" : "";
            return Failure(
                "EMD-ADAPTER-005",
                $"Adapter '{Descriptor.Id}' returned a JSON-RPC error{code}: {response.Error.Message}");
        }

        if (response.Result is null)
            return Failure("EMD-ADAPTER-004", $"Adapter '{Descriptor.Id}' response has no result.");

        var diagnostics = (response.Result.Diagnostics ?? [])
            .Select(diagnostic => new Diagnostic(
                diagnostic.Code ?? "EMD-ADAPTER-005",
                diagnostic.Severity ?? DiagnosticSeverity.Error,
                diagnostic.Message ?? "Adapter diagnostic.",
                diagnostic.Entity,
                diagnostic.Path))
            .ToList();

        var artifacts = (response.Result.Artifacts ?? [])
            .Select(artifact => new AdapterArtifact
            {
                Path = artifact.Path,
                Format = artifact.Format,
                Role = artifact.Role,
                Sha256 = artifact.Sha256,
                Bytes = artifact.Bytes
            })
            .ToList();

        if (!response.Result.Success && diagnostics.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-ADAPTER-006",
                DiagnosticSeverity.Error,
                $"Adapter '{Descriptor.Id}' reported failure without diagnostics."));
        }

        var succeeded = response.Result.Success
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

        return new AdapterResult
        {
            Success = succeeded,
            Artifacts = artifacts,
            Diagnostics = diagnostics,
            Data = response.Result.Data
        };
    }

    private static AdapterResult Failure(string code, string message) => new()
    {
        Success = false,
        Diagnostics = [new Diagnostic(code, DiagnosticSeverity.Error, message)]
    };

    private sealed record RpcResponse
    {
        public string? Jsonrpc { get; init; }

        public JsonElement? Id { get; init; }

        public RpcResult? Result { get; init; }

        public RpcError? Error { get; init; }
    }

    private sealed record RpcResult
    {
        public bool Success { get; init; }

        public List<RpcArtifact>? Artifacts { get; init; }

        public List<RpcDiagnostic>? Diagnostics { get; init; }

        public JsonObject? Data { get; init; }
    }

    private sealed record RpcArtifact
    {
        public string Path { get; init; } = "";

        public string Format { get; init; } = "";

        public ArtifactRole Role { get; init; }

        public string? Sha256 { get; init; }

        public long? Bytes { get; init; }
    }

    private sealed record RpcDiagnostic
    {
        public string? Code { get; init; }

        public DiagnosticSeverity? Severity { get; init; }

        public string? Message { get; init; }

        public string? Entity { get; init; }

        public string? Path { get; init; }
    }

    private sealed record RpcError
    {
        public int? Code { get; init; }

        public string? Message { get; init; }
    }
}
