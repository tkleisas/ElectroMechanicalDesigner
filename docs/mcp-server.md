# EMD MCP and HTTP server

`src/EMD.Server` exposes the EMD core to agents and a read-only web viewer.

## stdio MCP

```bash
dotnet run --project src/EMD.Server
```

Tools:

- `list_workspaces`, `create_workspace`, `get_workspace`
- `acquire_workspace_lease`, `release_workspace_lease`
- `commit_workspace_revision`
- `validate_project`
- `project_summary`
- `list_scenarios`
- `import_mechmaker`, `import_kicad`, `import_freecad_manifest`
- `export_mjcf`, `export_bom`, `export_mechmaker`,
  `export_assembly_doc`
- `check_trace`, `check_gcode`, `simulate_cam`
- `package_project`, `verify_package`, `verify_artifacts`
- `export_ssp`

## HTTP MCP + viewer

```bash
dotnet run --project src/EMD.Server -- --http 8080
```

- MCP streamable HTTP: `http://127.0.0.1:8080/mcp`
- Viewer: `http://127.0.0.1:8080/`
- `GET /api/project?path=...`
- `GET /api/graph?path=...`
- `GET /api/scene?path=...`
- `GET /api/validate?path=...`
- `GET /api/trace?path=...`
- `GET /api/trace-chart?path=...`
- `GET /api/package?dir=...`
- `GET /api/gcode?path=...&envelope=...`

The viewer is read-only and binds to localhost by default.

## Coordinator API

`EMD.Server --http` also exposes the workspace/revision/lease API used by the
Blazor WebAssembly client:

- `GET /api/workspaces`
- `POST /api/workspaces`
- `GET /api/workspaces/{id}`
- `POST /api/workspaces/{id}/leases`
- `POST /api/workspaces/{id}/leases/{leaseId}/release`
- `GET /api/workspaces/{id}/revisions`
- `POST /api/workspaces/{id}/revisions`
- `POST /api/artifacts`
- `GET /api/artifacts/{sha256}`
- `GET /api/host`
- `POST /api/host/execute`

Set `EMD_COORDINATOR_ROOT` (or pass `--coordinator-root`) to choose the
coordinator state directory and `EMD_ARTIFACT_ROOT` for the content-addressed
artifact store (defaults to `<coordinator-root>/artifacts`). Set
`EMD_COORDINATOR_URL` on additional MCP
servers to make them peers of an existing coordinator instead of opening the
local state file. Set `EMD_API_TOKEN` to require a bearer token on all `/api`
routes other than `/api/health`, and to add it to remote MCP/Web client calls. `EMD_WEB_ORIGINS` controls the browser CORS allowlist; it
defaults to `http://localhost:5005,http://127.0.0.1:5005`.

The browser client is in `src/EMD.Web` and reads its server URL and optional
bearer token from `wwwroot/appsettings.json` (`EmdServer`, `ApiToken`). It is a
Blazor WebAssembly app, not a server-rendered page. The static `ApiToken` is
only suitable for local/trusted deployments; public deployments should use
OIDC and short-lived tokens.

## Desktop

```bash
dotnet run --project src/EMD.Desktop
```

The Avalonia view loads a project, lists parts and diagnostics, and draws the
part layout in a 2D system canvas.
