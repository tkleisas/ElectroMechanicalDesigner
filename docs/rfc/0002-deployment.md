# RFC 0002 — Hybrid deployment, browser UI, central store, and GUI automation

- **Status:** Accepted
- **Date:** 2026-11-03
- **Project:** ElectroMechanicalDesigner (EMD)
- **Related:** RFC 0001, `docs/adapter-protocol.md`, `docs/mcp-server.md`

## 1. Summary

EMD is split into a central coordinator, a reproducible headless toolchain, a
browser-first UI, and a local host agent for GUI applications. The browser
client is Blazor WebAssembly (C#/.NET WASM). The desktop Avalonia app remains an
optional client. GUI applications are never run inside the toolchain
container and displays are never forwarded with X11/Wayland.

The central store is Git for canonical sources plus an S3-compatible object
store for large artifacts. A coordinator owns workspace revisions, artifact
references, jobs, and write leases. Local checkouts are disposable copies;
completed work is committed to a job branch and pushed back.

## 2. Goals

1. A browser UI that can run anywhere and talks only to EMD Server.
2. A reproducible containerized headless toolchain.
3. Local GUI automation for FreeCAD, KiCad, and slicers.
4. One canonical workspace revision even with local and container MCP servers.
5. Local working copies with explicit leases, commits, and pushes.
6. No GUI in the container; no display forwarding as a supported path.
7. Start with command-level automation; add OS-level UI automation only if a
   real tool cannot be driven through its API.

## 3. Roles

| Component | Location | Responsibility |
|---|---|---|
| Coordinator | Server | workspace registry, revisions, leases, jobs, artifact references |
| EMD Server | Server | HTTP/WebSocket API, MCP, validation, packaging, orchestration |
| Browser UI | Any browser | Blazor WASM project graph, validation, jobs, revisions |
| Avalonia Desktop | Optional local/remote | desktop client of the same server API |
| Toolchain container | Server or workstation | kicad-cli, freecadcmd, ngspice, Renode, OpenModelica, CAM |
| Host agent | Workstation with GUI tools | automates FreeCAD, KiCad, and slicers locally |
| Git repository | Central | canonical source revisions and job branches |
| Object store | Central | large generated artifacts and content-addressed blobs |

MCP servers are peers. They are not peer owners of mutable state. All mutations
go through the coordinator's lease and revision API.

## 4. Browser UI

The primary remote UI is **Blazor WebAssembly**. It uses the EMD HTTP/WebSocket
API and a shared `EMD.Contracts` client package. It must not access local files
or local GUI processes.

- Authentication: OIDC/OAuth2 bearer tokens; local development may use a
  single-user token.
- File exchange: uploads/downloads through the server; large artifacts use
  signed object-store URLs.
- 3D: server-generated glTF/JSON plus JS interop for rendering; do not ship
  STEP/STL through the WASM bundle.
- The Avalonia desktop app remains a client. It is not required to be on the
  server host.
- Running Avalonia inside a container with VNC/Xvfb is not a supported default.

## 5. Central store and revisions

Canonical source files live in Git. Large binaries use Git LFS or live in the
object store with pointers/manifests in Git.

A workspace is a Git repository plus coordinator metadata. A job checks out a
revision into a disposable workspace. Local host agents may cache a checkout
for GUI work.

Mutation flow:

1. Client acquires a lease for `workspace + branch + TTL`.
2. Coordinator returns the revision and a workspace path/token.
3. Headless tools run in the container; GUI tools run through the host agent.
4. The client/server commits changes to a job branch.
5. The branch is pushed to Git; large artifacts are uploaded to the object store.
6. The lease is released; review/merge happens outside automatic writes.
7. Main is never written directly by an automated job.

Leases are exclusive per workspace in the first implementation; the job branch
is recorded on the lease. Reads do not need a lease. Expired leases may be
reclaimed. Per-branch concurrency can be added after merge/conflict handling is
proven. The first implementation uses SQLite for coordinator state and a local
content-addressed artifact store; later revisions add Git CLI services and an
S3-compatible object-store adapter.

## 6. Host agent and GUI automation

The host agent runs on the workstation that owns the GUI applications. It opens
an **outbound** WebSocket to the coordinator, so no inbound firewall rule is
needed.

The command API is intentionally narrow:

- `probe` — tool versions and capabilities.
- `open` — open a workspace-relative file in a tool.
- `save` — save the active document, optionally with a generated revision.
- `export` — run an allowlisted export (manifest, STEP, STL, Gerber, BOM).
- `execute` — run an allowlisted command/macro declared by a capability.
- `status`, `cancel` — job progress and cancellation.

Tool-specific first implementations:

- **FreeCAD:** EMD workbench plus an embedded Python JSON-RPC handler. Use the
  `FreeCAD`/`FreeCADGui` API for open/save/export and approved macros.
- **KiCad:** use `kicad-cli` in the container for ERC/DRC/export. For GUI-only
  operations, a KiCad action plugin/IPC bridge; KiCad 11 headless IPC may remove
  part of this bridge later.
- **Slicers:** use CLI modes. No GUI automation unless a specific slicer lacks a
  CLI equivalent.

The host agent enforces path allowlists, method allowlists, per-user tokens, and
audit logs. Arbitrary shells are not part of the protocol.

## 7. MCP peers

Local and container MCP servers are first-class peers:

- Each registers identity and capabilities.
- Read-only tools can run anywhere.
- Mutating tools acquire a coordinator lease.
- A mutation without a lease fails with an actionable diagnostic.
- If the coordinator is unavailable, reads may continue from cache; writes
  fail closed.
- A job branch is the unit of conflict isolation.

This gives local GUI-aware MCP tools and containerized CI/agent tools without
two competing project states.

## 8. Deployment profiles

1. **Local development** — all tools local, no server required beyond MCP.
2. **Hybrid desktop** — local EMD Server/CLI + local GUI + toolchain container.
3. **Hybrid remote** — remote coordinator/server + browser UI + local host
   agent + local GUI + shared Git/object store.
4. **Headless CI** — container only; GUI operations report that no host agent is
   available.

The supported defaults are 2 and 3. Profile 4 is required for CI.

## 9. Security

- Coordinator and object store require authenticated users/tokens.
- A static `EMD_API_TOKEN` is the bootstrap/local mode; public deployments use
  OIDC with short-lived tokens. A token embedded in the WASM appsettings is
  visible to the browser and must not be treated as a secret.
- Host agent connects outbound; tokens are per-user and revocable.
- No Docker socket in the application container.
- Containers run non-root, with a read-only root filesystem and writable
  workspace/cache only.
- GUI automation commands are allowlisted; macros must be declared and audited.
- Artifacts are content-addressed and verified before use.
- Project files remain untrusted input.

## 10. Phased implementation

1. `EMD.Contracts` + `EMD.Coordinator`: workspace, revision, lease, artifact
   metadata, SQLite store and tests. **Done.**
2. Server endpoints and MCP peer operations for the coordinator. **Done.**
3. Blazor WASM workspace/revision/lease UI. **Done.**
4. Git CLI + object-store adapters and job branches. **Partial:** the host
   agent performs local checkout/commit/push; coordinator-managed job branches
   and the S3 adapter remain.
5. Local host agent with FreeCAD/KiCad command adapters. **Partial:**
   authenticated probe, Git checkout/commit/push, and GUI open are implemented;
   export/script automation and outbound WebSocket transport remain.
6. Remote job queue, audit, and multi-user authorization.
7. Richer 3D/diff/PR workflows.

## 11. Open questions

- Final database choice (SQLite for single-host, PostgreSQL for multi-host).
- Object-store provider and signed-URL policy.
- Authentication provider and token lifetime.
- Which FreeCAD/KiCad operations are safe enough to automate first.
