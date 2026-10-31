using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters.Tests;

public class JsonRpcAdapterTests
{
    [Fact]
    public async Task Execute_sends_a_jsonrpc_request_and_parses_a_result()
    {
        const string response = """
            {
              "jsonrpc": "2.0",
              "id": "1",
              "result": {
                "success": true,
                "artifacts": [
                  {
                    "path": "out/axis.step",
                    "format": "step",
                    "role": "generated",
                    "sha256": "abc123",
                    "bytes": 123
                  }
                ],
                "diagnostics": [],
                "data": { "note": "ok" }
              }
            }
            """;
        var runner = new FakeToolProcessRunner(new ToolProcessResult(0, response, "", []));
        var adapter = new JsonRpcAdapter(
            new ToolDescriptor
            {
                Id = "freecad",
                Name = "FreeCAD",
                Version = "26.3",
                Executable = "freecad-adapter",
                Arguments = ["--rpc"],
                Capabilities = ["step"]
            },
            runner);

        var result = await adapter.ExecuteAsync(new AdapterRequest
        {
            Operation = "freecad.export",
            Inputs = [new AdapterInput { Path = "axis.FCStd", Format = "fcstd", Sha256 = "def456" }],
            Parameters = new JsonObject { ["format"] = "step" }
        });

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("out/axis.step", artifact.Path);
        Assert.Equal("step", artifact.Format);
        Assert.Equal(ArtifactRole.Generated, artifact.Role);
        Assert.Equal("abc123", artifact.Sha256);
        Assert.Equal(123, artifact.Bytes);
        Assert.Equal("ok", result.Data!["note"]!.GetValue<string>());

        Assert.Equal("freecad-adapter", runner.Executable);
        Assert.Equal(["--rpc"], runner.Arguments);

        var request = JsonNode.Parse(runner.Stdin!)!.AsObject();
        Assert.Equal("2.0", request["jsonrpc"]!.GetValue<string>());
        Assert.Equal("freecad.export", request["method"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(request["id"]!.GetValue<string>()));
        Assert.Equal("axis.FCStd", request["params"]!["inputs"]![0]!["path"]!.GetValue<string>());
        Assert.Equal("fcstd", request["params"]!["inputs"]![0]!["format"]!.GetValue<string>());
        Assert.Equal("step", request["params"]!["parameters"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public async Task Jsonrpc_error_is_reported_as_EMD_ADAPTER_005()
    {
        const string response = """
            {
              "jsonrpc": "2.0",
              "id": "1",
              "error": { "code": -32601, "message": "unknown method" }
            }
            """;
        var adapter = CreateAdapter(response, exitCode: 0);

        var result = await adapter.ExecuteAsync(new AdapterRequest { Operation = "nope" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EMD-ADAPTER-005" &&
            diagnostic.Message.Contains("unknown method", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nonzero_exit_is_reported_as_EMD_ADAPTER_003()
    {
        var adapter = CreateAdapter("", exitCode: 2, stderr: "boom");

        var result = await adapter.ExecuteAsync(new AdapterRequest { Operation = "freecad.export" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EMD-ADAPTER-003" &&
            diagnostic.Message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_json_response_is_reported_as_EMD_ADAPTER_004()
    {
        var adapter = CreateAdapter("not json");

        var result = await adapter.ExecuteAsync(new AdapterRequest { Operation = "freecad.export" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-004");
    }

    [Fact]
    public async Task Runner_diagnostics_are_passed_through()
    {
        var runner = new FakeToolProcessRunner(new ToolProcessResult(
            -1, "", "",
            [new Diagnostic("EMD-ADAPTER-002", DiagnosticSeverity.Error, "timed out")]));
        var adapter = new JsonRpcAdapter(
            new ToolDescriptor { Id = "freecad", Name = "FreeCAD", Version = "26.3", Executable = "freecad-adapter" },
            runner);

        var result = await adapter.ExecuteAsync(new AdapterRequest { Operation = "freecad.export" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-002");
    }

    [Fact]
    public async Task Unsuccessful_result_without_diagnostics_gets_EMD_ADAPTER_006()
    {
        const string response = """
            { "jsonrpc": "2.0", "id": "1", "result": { "success": false } }
            """;
        var adapter = CreateAdapter(response);

        var result = await adapter.ExecuteAsync(new AdapterRequest { Operation = "freecad.export" });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-ADAPTER-006");
    }

    private static JsonRpcAdapter CreateAdapter(string response, int exitCode = 0, string stderr = "")
    {
        var runner = new FakeToolProcessRunner(new ToolProcessResult(exitCode, response, stderr, []));
        return new JsonRpcAdapter(
            new ToolDescriptor
            {
                Id = "freecad",
                Name = "FreeCAD",
                Version = "26.3",
                Executable = "freecad-adapter"
            },
            runner);
    }
}
