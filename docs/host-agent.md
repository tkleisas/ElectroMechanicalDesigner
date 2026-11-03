# EMD host agent protocol (v0.1)

The host agent is the local bridge to GUI applications. It runs on the
workstation that owns FreeCAD/KiCad and exposes a small authenticated HTTP
JSON API. EMD Server proxies to it; the browser never talks to the agent
directly.

The agent supports localhost HTTP and an outbound WebSocket connection to the
coordinator. WebSocket mode is preferred for remote agents because it needs no
inbound firewall rule.

## Configuration

```bash
EMD_HOST_WORKSPACE_ROOT=/home/me/emd-workspaces
EMD_HOST_TOKEN=change-me
EMD_COORDINATOR_WS_URL=ws://emd.example.test/api/hosts/ws
dotnet run --project src/EMD.Host -- --http 8090
```

Server side:

```bash
EMD_HOST_URL=http://127.0.0.1:8090
EMD_HOST_TOKEN=change-me
```

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/health` | liveness, no auth |
| GET | `/api/host` | capabilities and detected tool versions |
| POST | `/api/host/execute` | execute one allowlisted host command |
| WS | `/api/hosts/ws` | outbound agent registration and command channel |
| GET | `/api/hosts` | connected agents and capabilities |

Server proxy endpoints use the same shapes at `/api/host` and
`/api/host/execute`; they return 503 when no agent is configured.

## Commands

### `git.checkout`

```json
{
  "command": "git.checkout",
  "workspaceKey": "nsr1",
  "repository": "https://…/groovebox.git",
  "branch": "job/browser-edit"
}
```

Clones into `<workspace-root>/<workspaceKey>` from a local path, HTTPS, or SSH
URL. Existing checkouts are fetched and switched to the requested branch.

### `git.commit`

```json
{
  "command": "git.commit",
  "workspaceKey": "nsr1",
  "branch": "job/browser-edit",
  "message": "apply browser edits",
  "authorName": "EMD Host",
  "authorEmail": "emd@example.test"
}
```

Runs `git add -A`, creates a commit with the supplied author, and pushes the
branch to `origin`. The result contains the commit SHA. Recording the revision
in the coordinator is a separate API call so review/merge policy stays explicit.

### `gui.open`

```json
{
  "command": "gui.open",
  "tool": "freecad",
  "path": "/home/me/emd-workspaces/nsr1/enclosure.FCStd"
}
```

Allowed tools are `freecad` and `kicad`. Paths must resolve inside the
configured workspace root.

### `gui.export`

```json
{
  "command": "gui.export",
  "tool": "freecad",
  "path": "/home/me/emd-workspaces/nsr1/enclosure.FCStd",
  "outputPath": "/home/me/emd-workspaces/nsr1/out/enclosure.step",
  "format": "step"
}
```

FreeCAD supports STEP/STL. `kicad-cli` supports step, gerbers, drc, erc, bom,
and netlist. Exports are written inside the workspace and returned as hashed
artifacts.

### `script.run`

Runs `python3` or `freecadcmd` on a script under
`<workspace-root>/.emd/scripts/`. Arbitrary paths and tools are rejected.

## Security and scope

- The agent binds to `127.0.0.1` and requires `EMD_HOST_TOKEN` when set.
- Workspace keys are restricted to `[A-Za-z0-9_.-]` and cannot escape the root.
- Git arguments are passed directly; no shell is used.
- The agent supports checkout/commit/push, GUI opening, headless exports, and
  allowlisted scripts. OS-level UI automation is intentionally not part of
  this protocol.
- The host agent does not update coordinator revisions automatically; the
  caller supplies the returned commit SHA to the coordinator.
