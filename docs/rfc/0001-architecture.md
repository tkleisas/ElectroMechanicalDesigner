# RFC 0001 — Unified Electromechanical CAD Suite

- **Status:** Draft
- **Date:** 2026-10-31
- **Project:** ElectroMechanicalDesigner (EMD)
- **Related:** MechMaker (sibling project, digital-twin kernel)
- **Normative format:** [`schema/emd-project.schema.json`](../../schema/emd-project.schema.json)

## 1. Summary

EMD is an integration hub for designing, validating, and simulating
electromechanical systems. One project graph links mechanical geometry,
electrical nets, firmware pins, simulation variables, and manufacturing
artifacts. Each domain is edited with a mature open-source tool; EMD owns the
cross-domain identity, the build graph, the co-simulation orchestration, and
the validation report.

The first vertical slice is a motorized axis: FreeCAD geometry → MuJoCo/MJCF,
KiCad netlist → pin map, firmware in Renode, physics in MechMaker, all
co-simulated through FMI/SSP, with a validator that catches a wrong pin
assignment and exports a BOM.

## 2. Motivation

MechMaker already proves the hardest part of a digital twin: deterministic
physics at 8 kHz, a virtual MCU with a real stepper model, the Klipper wire
protocol, typed connectors, agent tooling, and hardware-in-the-loop. It does
not attempt to be a mechanical CAD system or an ECAD system, and it should not.

The gap is the **digital thread** between domains. A motor has a mechanical
mount, a shaft, phase nets, a driver pin, a firmware timer channel, a
simulation torque input, and a BOM line. Today those live in different files
with no stable cross-references. EMD makes that thread explicit and checkable.

Existing open-source tools cover the individual domains:

| Domain | Current tool | Role in EMD |
|---|---|---|
| Mechanical CAD | FreeCAD 26.3, CadQuery/build123d, OCCT | canonical geometry, assemblies, drawings, FEM, CAM |
| ECAD | KiCad 10.0.8 (11 in development), ngspice | canonical schematic, PCB, ERC/DRC, BOM, SPICE |
| Physics | MechMaker + MuJoCo 3.11 | rigid-body machine, stepper/MCU fidelity, HIL |
| Firmware | Renode 1.17, PlatformIO/Zephyr | unmodified firmware execution, virtual peripherals |
| Acausal behavior | OpenModelica, OMSimulator, FMI 3.0, SSP 2.0 | thermal/electrical/control co-simulation |
| CAM | FreeCAD Path, Klipper, LinuxCNC, CAMotics | toolpaths, post-processing, verification |
| Manufacturing | KiCad fabrication outputs, slicers | gerbers, drill, pick-and-place, additive |

## 3. Goals and non-goals

### Goals

1. **One project, many domains.** A single `emd-project` file with stable IDs
   and typed cross-domain bindings.
2. **Reuse, do not reimplement.** Integrate FreeCAD, KiCad, Renode, MechMaker,
   OpenModelica, and CAM tools at process boundaries.
3. **Deterministic digital twin.** Fixed-step co-simulation with record/replay
   and explicit fidelity modes (kinematic, rigid body, co-simulation, HIL).
4. **Headless and agent-first.** Every operation works from CLI/CI and through
   MCP; the GUI is a client of the same core.
5. **Open formats.** STEP, KiCad, MJCF/URDF/SDF, FMI/SSP, G-code, Gerber,
   glTF, JSON Schema.
6. **Traceability.** Any change can be queried for its impact across
   mechanics, electronics, firmware, simulation, and manufacturing.
7. **Reproducibility.** Pinned tool versions, content-addressed artifacts, and
   containerized CI.

### Non-goals

- Writing a new geometric kernel or constraint solver.
- Writing a new schematic capture or PCB editor.
- Replacing FreeCAD, KiCad, or MechMaker.
- Full PLM/ERP; EMD produces a BOM and manufacturing packages, not inventory.
- Simulating every physical effect at full fidelity in one run.

## 4. Existing assets and relationship to MechMaker

MechMaker is consumed as the twin kernel, not forked into EMD. Its existing
surface is already the right seam:

- `machine.json` — parts, poses, connections, boards, wiring.
- `MechMaker.Core` — catalog, validator (`mmNNN` diagnostics), MJCF compiler.
- `MechMaker.Engine` — MuJoCo runtime, virtual MCU, Klipper protocol, Lua.
- `MechMaker.Server` — MCP tools over stdio/HTTP.
- `MechMaker.Hil` — Android/OpenCV HIL seam.

EMD adds:

- a broader project graph with UUIDs and cross-domain ports;
- adapters for FreeCAD, KiCad, ngspice, Renode, OpenModelica, and CAM;
- an FMI/SSP co-simulation layer that can include MechMaker as one FMU;
- a build graph and content-addressed artifact store;
- cross-domain validation (net ↔ pin ↔ firmware ↔ simulation).

MechMaker remains useful standalone. EMD references it as a sibling
repository/submodule or as published NuGet packages (`MechMaker.Core`,
`MechMaker.Engine`). The exact packaging is an open question (Section 16).

## 5. Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  EMD Hub: Avalonia desktop · CLI · MCP server · web viewer          │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│  EMD.Core                                                           │
│  project graph · typed ports · xrefs · validation · build graph     │
└───────┬───────────────────────┬───────────────────────┬─────────────┘
        │                       │                       │
┌───────▼────────┐    ┌─────────▼─────────┐    ┌────────▼────────────┐
│ EMD.Adapters   │    │ EMD.Twin          │    │ Artifact store      │
│ process host   │    │ FMI/SSP master    │    │ CAS + manifests     │
│ JSON-RPC/stdio │    │ HIL bridges       │    │ hashes, provenance  │
└───────┬────────┘    └─────────┬─────────┘    └────────┬────────────┘
        │                       │                       │
┌───────▼───────────────────────▼───────────────────────▼─────────────┐
│ Domain adapters                                                     │
│ Mechanical: FreeCAD, CadQuery/OCCT, STEP, MJCF/URDF/SDF             │
│ Electrical: KiCad, kicad-cli, IPC API, ngspice, Gerber             │
│ Firmware:   PlatformIO/Zephyr, Renode, MechMaker virtual MCU        │
│ Behavior:   OpenModelica, FMU, SSP, MechMaker FMU                   │
│ CAM:        FreeCAD Path, Klipper/LinuxCNC, CAMotics, slicers       │
└─────────────────────────────────────────────────────────────────────┘
```

### Components

- **EMD.Core** — project model, schema validation, identity, units/frames,
  cross-domain queries, diagnostics, build-graph planning.
- **EMD.Adapters** — `IToolAdapter`: capability probe, plan, execute, collect.
  Adapters are separate processes speaking JSON-RPC over stdio (or a local
  socket), with timeouts, cancellation, and artifact manifests. The wire
  contract is documented in [adapter-protocol.md](../adapter-protocol.md).
  This keeps third-party code at arm's length and survives ABI/API churn.
- **EMD.Twin** — composes domain models into FMI 3.0 FMUs and SSP 2.0 system
  structures; drives OMSimulator/FMPy/OpenMCx; exposes record/replay and HIL
  bridges.
- **Artifact store** — content-addressed files (`artifact://sha256/...`) plus
  JSON manifests; generated files are never edited by hand.
- **EMD.Hub** — Avalonia shell and web/MCP surface. Detailed mechanical and
  electrical editing opens in FreeCAD/KiCad; the hub owns the system graph,
  3D overview, diagnostics, simulation dashboard, and BOM.

### Why process boundaries

- Licensing: third-party tools keep their own licenses and are not linked into
  the EMD binary.
- Stability: CAD/ECAD internal APIs change; CLI/IPC contracts are more stable.
- Robustness: a crashed plugin must not crash the project hub.
- Headless CI: the same adapters run in a pinned container.

## 6. System graph (the core data model)

The v0.1 schema is the normative definition; this section explains intent.

### Identity

- Every entity has a **UUID** (`id`). References always use UUIDs.
- Every entity may have a human-readable **`key`** (slug). Keys are for UI and
  diffs, not for references.
- Domain-native identifiers (`refdes`, FreeCAD component name, KiCad UUID, FMU
  variable) live in `domain_refs` and can change without breaking the graph.

### Units and frames

- SI everywhere: m, kg, s, A, K, V. Angles are degrees by default for
  compatibility with MechMaker; rotations are extrinsic XYZ.
- Right-handed frame, +Z up, +X forward.
- Mechanical connectors follow MechMaker's convention: local +Z is the mating
  direction and points outward from the part.
- Every adapter is responsible for converting its native units/frames at the
  boundary; the graph never stores mixed units.

### Parts and ports

A **part** is a system entity: a component, assembly, board, cable, or virtual
reference. Its **ports** are typed interfaces:

- mechanical: `mount`, `shaft`, `slide`, `hinge`, `fastener`
- electrical: `pin`, `power`, `ground`, `net`, `bus`
- control: `step`, `dir`, `enable`, `pwm`, `gpio`, `protocol`
- thermal/fluid: `heat`, `airflow`, `coolant`
- simulation: `fmu_input`, `fmu_output`, `variable`

Each port carries an optional `ref` to the tool-native object (FreeCAD
connector, KiCad pin, FMU variable) and optional `signal` metadata (unit,
voltage, current, protocol, range). This is the join key for the digital
thread.

### Relations

All relations are **connections** with a `kind` and two endpoints:

- `mate` / `mount` — mechanical mating.
- `wire` / `net` — electrical node membership.
- `control` — logical control link.
- `thermal` / `fluid` — physical-domain coupling.
- `firmware_pin` — MCU pin to net.
- `simulation_binding` — graph port to FMU variable.
- `harness` — cable/conductor grouping.

An endpoint can be a part port, an electrical net node, an FMU variable, or an
external URI. This keeps the graph domain-neutral while allowing native
topology to stay in KiCad/FreeCAD.

### Canonicality

- **Mechanical:** the FreeCAD document (or CadQuery script) is canonical for
  geometry. EMD stores placements, mates, and simulation metadata.
- **Electrical:** the KiCad project is canonical for schematic/PCB. EMD imports
  nets/refdes and owns cross-domain bindings.
- **Behavior:** Modelica/FMU/script sources are canonical; EMD stores
  parameter bindings and composition.
- **Cross-domain:** the EMD project is canonical for ports, bindings, and
  metadata.

Derived artifacts are rebuilt from canonical sources; no live bidirectional
geometry sync in v1.

## 7. Domain integration

### 7.1 Mechanical

- Interactive modeling: FreeCAD 26.3 (Part Design, Sketcher, Assembly,
  TechDraw, FEM, CAM). FreeCAD 26.3 adds multi-axis CAM, assembly BOM, mass
  properties, and OCCT 8 support.
- Code-generated parts and CI: CadQuery/build123d on OCCT. Both can emit STEP
  and a sidecar JSON with connector frames.
- Assembly mapping: FreeCAD joints/placements ↔ `mate` connections. A new
  exporter merges FreeCAD geometry with EMD joint/actuator metadata into MJCF
  (and optionally URDF/SDF) for MechMaker/MuJoCo.
- MCAD→ECAD: export board STEP (`kicad-cli pcb export step`) and mounting
  holes/connector positions into the mechanical assembly; use IDX/IDF or a
  JSON sidecar for placement. Keep one authoritative board outline.
- Catalog: EMD catalog entries can reference a FreeCAD/CadQuery artifact, not
  only a mesh. MechMaker's mesh-based catalog remains supported.

### 7.2 Electrical

- KiCad 10.0.8 is the ECAD baseline. `kicad-cli` provides headless ERC/DRC,
  netlist, BOM, Gerber/drill, and STEP export.
- KiCad 10's IPC API requires a running GUI; plan for KiCad 11's headless IPC
  and schematic plugins. Until then, use `kicad-cli` plus S-expression
  parsing for imports.
- Netlist import maps nets, refdes, and pins to EMD ports; cross-domain
  bindings connect them to mechanical connectors and firmware pin maps.
- ngspice runs analog/power models; the SPICE netlist can be exported as an
  FMU for system co-simulation.
- Harness/documentation: generate WireViz or SVG/PDF wiring diagrams from the
  graph plus KiCad nets.
- Cross-domain ERC: every motor phase must reach a driver pin, every driver
  input must have a firmware pin, every power port must have a source, etc.

### 7.3 Firmware

- Source projects use PlatformIO, Zephyr, CMake, Arduino, or Make.
- Renode 1.17 runs unmodified firmware against virtual MCUs and peripherals;
  its external control API bridges simulated sensors/actuators to the twin.
- MechMaker's virtual MCU remains the fast, deterministic path for stepper
  motion and Klipper protocol tests. Fidelity is selectable: MechMaker MCU for
  motion-heavy runs, Renode for firmware/peripheral fidelity.
- Pin maps are first-class: MCU pin ↔ net ↔ connector pin ↔ subsystem.
- HIL: MechMaker's Klipper and adb seams, plus MQTT/OPC UA/DCP for external
  rigs, connect the same bindings to real hardware.

### 7.4 Behavior and co-simulation

- Component behaviors: OpenModelica models (thermal, electrical, control),
  ngspice circuits, MechMaker physics, Lua/Python scenarios.
- Packaging: each model becomes an FMI 3.0 FMU (co-simulation or model
  exchange). MechMaker is wrapped as a fixed-step FMU with inputs (motor
  commands, PWM) and outputs (joint positions, endstops, temperatures).
- Composition: SSP 2.0 system structure definitions with explicit unit-aware
  connections. Masters: OMSimulator (industrial, CLI/Python), FMPy (Python,
  good for CI), OpenMCx (large distributed systems).
- Determinism: fixed step, seeded randomness, no wall-clock dependence in
  design-time runs; record all FMU inputs/outputs for replay.
- Fidelity modes:
  - `kinematic` — no dynamics; fast reach/collision checks.
  - `rigid_body` — MechMaker/MuJoCo at 8 kHz.
  - `co_simulation` — FMI/SSP with thermal/electrical/firmware models.
  - `hardware_in_the_loop` — bindings to a real controller/rig.

### 7.5 CAM and manufacturing

- FreeCAD Path (multi-axis in 26.3) generates toolpaths; post-processors emit
  Klipper, LinuxCNC, or GRBL.
- KiCad produces fabrication and assembly packages (Gerber, drill, BOM,
  pick-and-place, STEP).
- Additive: OrcaSlicer/PrusaSlicer via CLI, pinned profiles.
- Verification: CAMotics or FreeCAD's simulator; compare against the design
  model and report gouges/remaining material.
- The same machine definition that drives the twin can drive the CAM
  post-processor, keeping kinematics consistent.

## 8. Build graph and artifact store

- The project file references artifacts by UUID and content hash.
- Each generated artifact records `produced_by` (tool/version) and `inputs`.
- A change invalidates only descendants in the DAG.
- The store is content-addressed: `artifact://sha256/<hash>`; the project may
  also reference external paths for source files.
- Builds are reproducible: pinned tool versions, container images, and
  deterministic adapters (no timestamps in generated geometry where possible).
- Large binaries (FCStd, STEP, PCB, FMU, video) stay out of the project JSON.

## 9. Validation and change impact

Diagnostics are structured (`code`, `severity`, `entities`, `message`,
`details`, `quick fixes`) and machine-readable for CI and agents.

Examples:

| Code | Check |
|---|---|
| `EMD-XREF-001` | Orphaned domain reference (deleted part/refdes/net) |
| `EMD-XREF-002` | Duplicate electrical refdes |
| `EMD-MECH-001` | Assembly interference / clearance violation |
| `EMD-MECH-002` | Unreachable pose or joint limit exceeded |
| `EMD-MECH-003` | Missing mate or floating connector |
| `EMD-MECH-004` | Invalid joint or simulation-proxy geometry |
| `EMD-MECH-005` | Non-connected parts are closer than the clearance |
| `EMD-MECH-006` | Slide-joint motion sweep collides with another part |
| `EMD-MECH-007` | Hinge-joint motion sweep collides with another part |
| `EMD-ART-001` | Referenced artifact is missing |
| `EMD-ART-002` | Artifact content is missing or corrupt |
| `EMD-MJCF-001` | MJCF export has no parts |
| `EMD-MJCF-002` | Missing or unmaterialized mesh artifact |
| `EMD-MJCF-003` | Part disconnected from the assembly root |
| `EMD-MJCF-004` | Mechanical graph cycle or multiple paths |
| `EMD-ELEC-001` | Netlist and EMD wiring disagree |
| `EMD-ELEC-002` | KiCad ERC/DRC failures |
| `EMD-ELEC-003` | Motor/driver voltage or current mismatch |
| `EMD-ELEC-004` | Electrical/control port is not connected |
| `EMD-ELEC-005` | Net and port voltage mismatch |
| `EMD-ELEC-006` | Motor phase has no board/driver endpoint |
| `EMD-ELEC-007` | Multiple motor phases share one net |
| `EMD-FW-001` | Firmware pin map incomplete or conflicting |
| `EMD-FW-002` | Duplicate MCU pin assignment |
| `EMD-FW-003` | Firmware pin is not connected to its mapped net |
| `EMD-KICAD-010` | `kicad-cli` command failed |
| `EMD-KICAD-011` | Expected `kicad-cli` output is missing |
| `EMD-KICAD-020` | KiCad PCB has no usable Edge.Cuts outline |
| `EMD-KICAD-021` | Invalid KiCad PCB S-expression |
| `EMD-KICAD-022` | KiCad PCB has no mounting holes |
| `EMD-SPICE-001` | ngspice command failed |
| `EMD-KICAD-023` | Invalid STEP header/footer |
| `EMD-KICAD-024` | STEP has no usable bounding-box points |
| `EMD-SPICE-002` | Expected ngspice log is missing |
| `EMD-HARNESS-001` | Net is not point-to-point for harness export |
| `EMD-HARNESS-002` | Harness net endpoint is unresolved |
| `EMD-MECHMAKER-001` | Part has no MechMaker catalog reference |
| `EMD-MECHMAKER-002` | Connection references a skipped part |
| `EMD-MECHMAKER-003` | Net cannot be mapped to a MechMaker signal |
| `EMD-MECHMAKER-004` | Duplicate exported MechMaker id |
| `EMD-MECHMAKER-005` | No MechMaker-exportable parts |
| `EMD-MECHMAKER-010` | MechMaker compilation failed |
| `EMD-MECHMAKER-011` | Expected MechMaker MJCF output is missing |
| `EMD-MECHMAKER-020` | MechMaker MCP tool/run failed |
| `EMD-MECHMAKER-021` | MechMaker status could not be parsed |
| `EMD-MECHMAKER-022` | MechMaker stop_run failed |
| `EMD-PACKAGE-001` | Optional package member was omitted |
| `EMD-PACKAGE-002` | Required package member failed to export |
| `EMD-PACKAGE-003` | Package manifest is missing or invalid |
| `EMD-PACKAGE-004` | Package member is missing |
| `EMD-PACKAGE-005` | Package member hash/size mismatch |
| `EMD-PACKAGE-006` | Package contains an unlisted file |
| `EMD-PACKAGE-007` | Package member escapes the package directory |
| `EMD-PACKAGE-008` | Duplicate package member was ignored |
| `EMD-TRACE-001` | Trace CSV or assertion expression is invalid |
| `EMD-TRACE-002` | Assertion references an unknown trace signal |
| `EMD-TRACE-003` | Scenario assertion failed |
| `EMD-TRACE-004` | Trace has no data rows |
| `EMD-TRACE-005` | Requested scenario was not found |
| `EMD-TRACE-006` | Scenario has no assertions |
| `EMD-SIM-001` | Unbound FMU port or unit mismatch |
| `EMD-SIM-002` | Co-simulation step/rate mismatch |
| `EMD-SIM-003` | Simulation binding references an undeclared variable |
| `EMD-SIM-004` | Scenario trace artifact is missing |
| `EMD-SSP-001` | No FMU behavior models for SSP composition |
| `EMD-SSP-002` | FMU artifact is missing or unreadable |
| `EMD-SSP-003` | Simulation binding does not match FMU connectors |
| `EMD-SSP-004` | Unsupported SSP version |
| `EMD-SSP-005` | SPICE model needs an FMU artifact for SSP |
| `EMD-MFG-001` | Manufacturing output missing or stale |
| `EMD-BOM-001` | BOM/refdes mismatch |

Project diffing (`emd diff`) reports added/removed/changed entities across all
domains by stable UUID.

Cross-domain queries answer: "What breaks if I move this motor?", "Which
firmware pins are affected by this net rename?", "Which parts are not
manufacturable?"

Development is **test-first (TDD)**: every behavior change starts with a
failing test, bug fixes start with a regression test, schema changes include
valid and negative fixtures, and adapter changes include contract tests against
pinned tool versions. The Red-Green-Refactor rule is mandatory; see
[CONTRIBUTING.md](../../CONTRIBUTING.md).

## 10. UX and agent surface

- The hub is a **system editor**, not a replacement CAD/ECAD UI.
- 3D overview: render the compiled MJCF/scene and board outlines; click a part
  to inspect its mechanical, electrical, firmware, and simulation refs.
- "Open in FreeCAD" / "Open in KiCad" launches the native tool; on save, the
  adapter re-imports changed domain data and runs validation.
- Schematic/PCB and detailed modeling stay in KiCad/FreeCAD. Embedding Qt
  applications is explicitly out of scope for v1; separate windows plus
  explicit sync are more robust cross-platform.
- MCP/CLI exposes the same operations: import, bind, validate, compile,
  simulate, export BOM/CAM, open-in-tool.
- Simulation dashboard: signals, assertions, failures, replay, and a link from
  each failure to the responsible graph entities.

## 11. Security, licensing, and reproducibility

- **Untrusted projects execute scripts.** FreeCAD macros, Lua scenarios, and
  Python adapters are code. Default to a sandbox: no network, explicit
  allowlist, container or OS sandbox, resource limits.
- **Process isolation** keeps third-party tools separate; the GPL-3.0 hub
  communicates via documented CLI/IPC surfaces. Distribution must still comply
  with each bundled tool's license.
- **Supply chain:** pin tool and adapter versions; verify adapter manifests;
  hash artifacts; publish a container image for CI.
- **Data integrity:** never trust imported CAD/ECAD files; parse in the
  adapter process and validate sizes/complexity.

## 12. Roadmap

### P0 — Foundation — complete

- `EMD.Core`: schema validation, project model, UUID/key identity, ports,
  connections, cross-domain validation, content-addressed artifact store, and
  build-graph planner.
- `EMD.Adapters`: process tool host with timeouts, cancellation, stdin/stdout/
  stderr capture, argument-boundary safety, and a one-shot JSON-RPC 2.0
  `IToolAdapter` contract.
- `EMD.Cli`: `validate`, `import mechmaker`, `import kicad`, `import freecad`,
  `build plan`, `artifacts verify`.
- Deterministic MechMaker `machine.json` and KiCad XML netlist importers;
  FreeCAD artifact references with optional content-addressed storage.
- Example project, xUnit tests, format/test CI gates.

### P1 — Mechanical vertical slice — in progress

Implemented:

- CAD manifest schema and FreeCAD manifest importer: parts, connectors,
  simulation-proxy geometry, joints, artifacts, and BOM.
- Extrinsic-XYZ pose transforms and an MJCF exporter with body trees, geoms,
  hinge/slide/ball/free joints, motors, and diagnostics.
- Starter FreeCAD JSON-RPC adapter (`adapters/python`) exporting STEP/STL and a
  CAD manifest from bounding-box proxies.
- Interference, clearance, motion-sweep, and joint-limit checks: oriented-box
  SAT, box/sphere and sphere/sphere penetration/separation, explicit-mate/joint
  exemptions, slide/hinge range sampling, and slide/hinge ranges.
- CLI: `import freecad-manifest` and `export mjcf`.
- KiCad STEP export artifacts can be attached to board parts with
  `emd import kicad-step`, including header/bounding-box metadata.

Remaining:

- A richer FreeCAD property scheme or workbench export (real connector frames,
  materials, BOM, per-feature geometry).
- CadQuery catalog part generation.
- Full swept-volume checks; exact STEP outline/hole reconstruction (PCB
  Edge.Cuts remains canonical for now).

### P2 — Electrical vertical slice — in progress

Implemented:

- Headless KiCad CLI adapter: ERC/DRC, XML netlist, BOM, Gerbers/drill, and
  STEP export with structured artifacts.
- BOM exporter and `emd export bom`.
- CLI: `emd kicad erc|drc|netlist|bom|gerbers|step`.
- ngspice batch adapter and `emd spice`.
- WireViz-compatible harness export and `emd export harness`.
- KiCad PCB outline and mounting-hole import into a board part with mechanical
  ports; `emd import kicad-pcb`.
- Cross-domain ERC: unconnected electrical/control ports, duplicate MCU pins,
  duplicate refdes, net/port voltage mismatch, firmware pin-to-net
  connectivity, and basic motor phase topology (board endpoints and shared nets).

Remaining:

- Deeper motor phase-count/mapping checks.
- SPICE models are included in SSP when backed by an FMU artifact; generating
  that FMU from a raw ngspice netlist is the remaining step.

### P3 — Digital twin co-simulation — groundwork

- EMD→MechMaker `machine.json` exporter: catalog parts, mates, boards, wiring,
  with round-trip tests. **Done.**
- Simulation-binding validation against behavior-model variables. **Done.**
- MechMaker compile adapter and `emd compile mechmaker`. **Done.**
- Live MechMaker simulation over MCP and `emd simulate mechmaker`. **Done.**
- Project → machine.json → live run → trace → scenario assertion loop. **Done.**
- Scenario trace evidence persisted in the project and packaged. **Done.**
- SSP 1.0/2.0 composition of FMU behavior models and FMU-to-FMU bindings. **Done.**
- SSP execution verified with OMSimulator and FMPy using real FMUs. **Done.**
- Typed SSP connectors and Command → MechMaker end-to-end SSP test in CI. **Done.**
- MVP `move_40mm` SSP scenario: 40 mm carriage arrival, trace assertion, BOM export,
  and swapped-phase failure. **Done.**
- Behavior model parameters serialized as SSP `ParameterBindings`. **Done.**
- OMSimulator confirms parameter bindings are applied at runtime. **Done.**
- Package manufacturing artifacts, FreeCAD+KiCad import merge fixture, and
  firmware pin bindings. **Done.**
- Direct NativeAOT FMU removes the proxy process and host .NET dependency.
- Direct FMU coverage spans belt-driven, multi-axis gantry, gear-train,
  leadscrew, and fan/heater fixtures, with servo and endstop channels.
- Direct/proxy parity and Windows NativeAOT builds are covered in CI.
- Groovebox real-project integration is deferred: import PCB outlines,
  enclosure dimensions, and firmware GPIO maps; electrical nets await a
  `netlist-spec.md` parser or `kicad-cli` netlist export.
- Torque-level and HIL interfaces remain future work; both FMUs expose
  velocity/enable.
- Self-contained MechMaker FMI 2.0 proxy FMU (embedded server/catalog/machine)
  verified with FMPy. **Done.**
- Deterministic package bundle, trace attachments, and verifier (`emd package`, `emd package verify`). **Done.**
- Trace assertion evaluator and `emd trace check`. **Done.**
- MechMaker FMU wrapper (fixed step, deterministic). **Done** in both forms:
  PythonFMU + MCP proxy and direct .NET NativeAOT in-process FMU.
- OpenModelica thermal/motor FMU; Renode firmware bridge.
- Record/replay and HIL mode through Klipper/adb.

### P4 — CAM and manufacturing

- FreeCAD Path jobs and post-processors; KiCad fabrication/assembly packages;
  slicer CLI; CAMotics verification; BOM and assembly docs.

### P5 — Hub, agents, and CI

- Avalonia system view and 3D overview; open-in-tool and re-import.
- MCP tools for every domain; HTTP/web viewer; deterministic CI container.

### MVP acceptance

A motorized axis that:

1. imports FreeCAD geometry and KiCad electrical design;
2. binds motor phases, driver pins, MCU pins, and an FMU torque input;
3. runs a deterministic co-simulation and reaches the expected position;
4. fails validation when one phase net is swapped;
5. exports a BOM and a manufacturing package.

## 13. Risks

| Risk | Mitigation |
|---|---|
| Scope explosion | Integrate; never reimplement CAD/ECAD kernels |
| Dual sources of truth | Domain-native canonicality + explicit re-import + stable UUIDs |
| Tool API churn | CLI-first adapters, pinned versions, contract tests |
| Headless gaps (KiCad 10 IPC) | `kicad-cli` now; target KiCad 11 headless IPC |
| FreeCAD scripting fragility | Versioned scripts, integration tests, CadQuery fallback |
| License/distribution obligations | Process boundaries, SPDX review, separate distribution |
| Frame/unit bugs | SI + one pose convention + adapter-boundary tests |
| Non-deterministic co-sim | Fixed step, seeded, replay, CI comparisons |
| Untrusted project scripts | Sandbox, allowlist, resource limits |

## 14. Alternatives considered

1. **Extend MechMaker into a full suite.** Rejected: mixes concerns and forces
   one UI/stack on CAD and ECAD.
2. **Fork FreeCAD or KiCad and embed them.** Rejected: maintenance burden,
   licensing complexity, and a worse experience than the upstream tools.
3. **Build a new CAD kernel / ECAD editor.** Rejected: decades of work.
4. **Use only file exchange with no system graph.** Rejected: loses
   cross-domain validation and impact analysis, which is the product.
5. **Commercial tools as the backend.** Rejected for an open-source suite;
   adapters can still be added later.

## 15. Decision

- Build EMD as a **separate hub** that consumes MechMaker as the twin kernel.
- Start with the **RFC + project schema** (this document and
  `schema/emd-project.schema.json`).
- Keep domain-native files canonical; the EMD project is canonical for
  cross-domain bindings.
- Use **FMI 3.0 / SSP 2.0** for co-simulation, with **OMSimulator/FMPy** as the
  first masters.
- Integrate via **headless CLIs and IPC** at process boundaries.
- License the hub under **GPL-3.0-only**; keep third-party tools separate and
  comply with their licenses when distributing bundles.

## 16. Open questions

1. Packaging MechMaker: sibling submodule, project reference, or NuGet?
2. Should the hub also be a FreeCAD workbench (for tighter mechanical UX), or
   only a separate application?
3. Board/mechanical connector exchange: IDX/IDF, STEP AP242 annotations, or a
   JSON sidecar?
4. Default firmware fidelity: MechMaker virtual MCU or Renode?
5. Which HIL transports are first-class: Klipper, MQTT, OPC UA, DCP, ROS 2?
6. Do we adopt Asset Administration Shell / DTDL metadata for twins?
7. Distribution: separate installers per tool vs one container/appliance?
8. How should the GPL-3.0 hub be packaged with GPL/LGPL tools in installers
   and containers while keeping source and notice obligations clear?
