# EMD MCP and HTTP server

`src/EMD.Server` exposes the EMD core to agents and a read-only web viewer.

## stdio MCP

```bash
dotnet run --project src/EMD.Server
```

Tools:

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

## Desktop

```bash
dotnet run --project src/EMD.Desktop
```

The Avalonia view loads a project, lists parts and diagnostics, and draws the
part layout in a 2D system canvas.
