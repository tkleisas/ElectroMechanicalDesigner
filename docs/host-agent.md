# EMD host agent protocol (v0.1)

The host agent is the local bridge to GUI applications. It runs on the
workstation that owns FreeCAD/KiCad and exposes a small authenticated HTTP
JSON API. EMD Server proxies to it; the browser never talks to the agent
directly.

Current transport is localhost HTTP. For a remote EMD Server, expose the agent
through an SSH tunnel or a reverse proxy until the outbound WebSocket transport
is implemented.

## Configuration

```bash
EMD_HOST_WORKSPACE_ROOT=/home/me/emd-workspaces
EMD_HOST_TOKEN=change-me
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

## Security and scope

- The agent binds to `127.0.0.1` and requires `EMD_HOST_TOKEN` when set.
- Workspace keys are restricted to `[A-Za-z0-9_.-]` and cannot escape the root.
- Git arguments are passed directly; no shell is used.
- The agent currently supports checkout/commit/push and opening GUI documents.
  Tool-specific export automation is the next command family.
- The host agent does not update coordinator revisions automatically; the
  caller supplies the returned commit SHA to the coordinator.
