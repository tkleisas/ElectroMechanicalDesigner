# EMD MCP and HTTP server

`src/EMD.Server` exposes the EMD core to agents and a read-only web viewer.

## stdio MCP

```bash
dotnet run --project src/EMD.Server
```

Tools:

- `validate_project`
- `project_summary`
- `check_trace`
- `check_gcode`
- `package_project`
- `verify_package`
- `export_ssp`

## HTTP MCP + viewer

```bash
dotnet run --project src/EMD.Server -- --http 8080
```

- MCP streamable HTTP: `http://127.0.0.1:8080/mcp`
- Viewer: `http://127.0.0.1:8080/`
- `GET /api/project?path=...`
- `GET /api/trace?path=...`
- `GET /api/package?dir=...`
- `GET /api/gcode?path=...&envelope=...`

The viewer is read-only and binds to localhost by default.
