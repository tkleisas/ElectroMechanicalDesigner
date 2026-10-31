# Tool adapter protocol (v0.1)

Adapters run as separate processes so FreeCAD, KiCad, ngspice, Renode, and
other tools keep their own licenses, environments, and failure domains.

## Transport

- EMD starts the adapter executable with the descriptor's argument list.
- One request is written to **stdin** as JSON, then stdin is closed.
- The adapter writes exactly one JSON-RPC 2.0 response to **stdout**.
- **stderr** is free for logs and is included in failure diagnostics.
- Exit code `0` means the adapter produced a valid response. A non-zero exit
  is reported as `EMD-ADAPTER-003` even if stdout contains a response.
- The default timeout is 30 seconds; a timeout is `EMD-ADAPTER-002`.

## Request

```json
{
  "jsonrpc": "2.0",
  "id": "6f9c1d2a8b4e4f0a9c3d2e1f0a9b8c7d",
  "method": "freecad.export",
  "params": {
    "inputs": [
      {
        "path": "files/mechanical/axis.FCStd",
        "format": "fcstd",
        "sha256": "…"
      }
    ],
    "parameters": {
      "format": "step"
    }
  }
}
```

- `method` is namespaced by tool and operation (`freecad.export`,
  `kicad.export_netlist`, `sim.compose_ssp`).
- `inputs` reference EMD artifacts. `sha256` is optional but recommended.
- `parameters` is operation-specific.

## Response

```json
{
  "jsonrpc": "2.0",
  "id": "6f9c1d2a8b4e4f0a9c3d2e1f0a9b8c7d",
  "result": {
    "success": true,
    "artifacts": [
      {
        "path": "out/axis.step",
        "format": "step",
        "role": "generated",
        "sha256": "…",
        "bytes": 12345
      }
    ],
    "diagnostics": [
      {
        "code": "EMD-MECH-003",
        "severity": "warning",
        "message": "Connector is not mated.",
        "entity": "motor_x.mount",
        "path": "/parts/1/ports/0"
      }
    ],
    "data": {
      "tool_version": "26.3"
    }
  }
}
```

- `success` is the adapter's own verdict. EMD also requires every diagnostic
  to be below `error` severity; otherwise the result is a failure.
- `artifacts` are new files produced by the operation. `role` is one of
  `source`, `generated`, `visualization`, `simulation`, `manufacturing`,
  `report`, or `other`.
- `diagnostics` use the same `EMD-*` codes as the core.
- `data` is optional free-form JSON for tool-specific metadata.

## JSON-RPC errors

```json
{
  "jsonrpc": "2.0",
  "id": "…",
  "error": { "code": -32601, "message": "unknown method" }
}
```

This is reported as `EMD-ADAPTER-005`. Invalid/empty responses are
`EMD-ADAPTER-004`; a failed `result` with no diagnostics is
`EMD-ADAPTER-006`.

## Minimal adapter

```python
#!/usr/bin/env python3
import json
import sys

request = json.load(sys.stdin)
result = {
    "success": True,
    "artifacts": [],
    "diagnostics": [],
    "data": {"operation": request["method"]},
}
json.dump({"jsonrpc": "2.0", "id": request["id"], "result": result}, sys.stdout)
```

Register it with a `ToolDescriptor`:

```csharp
var descriptor = new ToolDescriptor
{
    Id = "my-tool",
    Name = "My Tool",
    Version = "1.0.0",
    Executable = "/opt/my-tool/adapter.py",
    Arguments = [],
    Capabilities = ["step"]
};

var adapter = new JsonRpcAdapter(descriptor, ProcessToolHost.Default);
var result = await adapter.ExecuteAsync(new AdapterRequest
{
    Operation = "my-tool.echo",
    Parameters = new JsonObject { ["hello"] = "world" }
});
```

## Rules

- Argument lists only; never invoke a shell.
- Pin the tool and adapter versions in the project `toolchain`.
- Do not write diagnostics to stdout; stdout is the protocol channel.
- Treat project files as untrusted; run adapters in a sandbox with resource
  limits.
- A change in adapter behavior needs a contract test and a fixture.
