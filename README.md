# ElectroMechanicalDesigner (EMD)

An open-source CAD/CAM suite for electromechanical systems: draw the mechanics,
draw the electronics, write the firmware, and simulate the whole machine as a
digital twin from one traceable project.

**Status: P0–P3 core slices implemented; P4 manufacturing/CAM and P5 agent/server
in progress.**

Current capabilities:

- **Project graph and lifecycle:** JSON Schema, stable IDs, content-addressed
  artifacts, build planning, project diff/impact, deterministic packages, and
  package verification.
- **Imports/exports:** MechMaker machines, KiCad netlists/PCB outlines/STEP,
  FreeCAD manifests and artifacts, MJCF, BOM, harness, SSP, and assembly docs.
- **Validation:** cross-domain electrical/mechanical/firmware checks, motor
  phase count/mapping topology, firmware pin-map/board consistency, artifact
  integrity, and simulation bindings.
- **Digital twin:** direct in-process NativeAOT MechMaker FMU (~7 MB), legacy
  PythonFMU proxy, SSP 1.0/2.0, FMPy/OMSimulator execution, parameter bindings,
  trace assertions, and the runnable 40 mm axis MVP.
- **Manufacturing/CAM:** G-code validation and postprocessing, KiCad fabrication
  packages, OrcaSlicer/PrusaSlicer adapters, CAMotics checks, 2.5D heightmap
  material-removal comparison, and assembly documentation.
- **Firmware:** Renode headless execution with firmware/platform artifact
  resolution, UART log evidence, and pin-map verification.
- **Agent/server:** MCP stdio and streamable HTTP tools plus a read-only project
  graph, isometric scene, trace chart, package, validation, and G-code viewer.
- **Desktop:** Avalonia system view with project loading, part list, validation,
  and a 2D system canvas.

Known gaps: dedicated FreeCAD workbench export; closed-loop SPICE;
OpenModelica thermal/motor models; firmware↔MechMaker co-simulation; HIL; true
multi-axis/3D CAM; groovebox real-project integration.

The suite has 279 xUnit tests, 12 Python adapter tests, and 2 CadQuery tests.

## Principle

Do not rebuild CAD, ECAD, or CAM. Integrate the best open-source tools behind a
single system graph:

| Domain | Tool(s) |
|---|---|
| Mechanical | FreeCAD 26.x, CadQuery/build123d on OCCT, STEP AP242 |
| Electrical | KiCad 10/11, `kicad-cli`, ngspice, IPC API |
| Firmware | Renode, PlatformIO/Zephyr, MechMaker virtual MCU |
| Twin | MechMaker + MuJoCo, OpenModelica, FMI 3.0 / SSP 2.0, OMSimulator/FMPy |
| CAM | FreeCAD Path, Klipper, LinuxCNC, CAMotics, slicers |

MechMaker (sibling repository) is consumed as the deterministic physics +
virtual-MCU kernel. Domain-native files (`.FCStd`, `.kicad_sch`, `.kicad_pcb`)
remain canonical for their domain; the EMD project graph is canonical for
cross-domain identity and bindings.

## Start here

- [RFC 0001 — Unified Electromechanical CAD Suite](docs/rfc/0001-architecture.md)
- [Project schema v0.1](schema/emd-project.schema.json)
- [FreeCAD Path job schema v0.1](schema/path-job.schema.json)
- [CAD manifest schema v0.1](schema/cad-manifest.schema.json)
- [Example project](examples/project.json)
- [Tool adapter protocol](docs/adapter-protocol.md)
- [Starter FreeCAD adapter](adapters/python/README.md)
- [MCP and HTTP server](docs/mcp-server.md)
- [Renode setup](docs/renode.md)
- [Contributing guide (TDD is mandatory)](CONTRIBUTING.md)

Run the MCP server or desktop viewer:

```bash
dotnet run --project src/EMD.Server -- --http 8080
dotnet run --project src/EMD.Desktop
```

Run the suite and the CLI:

```bash
dotnet test EMD.slnx
dotnet run --project src/EMD.Cli -- validate examples/project.json
dotnet run --project src/EMD.Cli -- import mechmaker \
  ../MechMaker/examples/linear_axis_v0.json \
  --out out/linear_axis.json --catalog ../MechMaker/catalog
dotnet run --project src/EMD.Cli -- import kicad \
  tests/EMD.Core.Tests/Fixtures/kicad_netlist.xml --out out/kicad.json
dotnet run --project src/EMD.Cli -- import freecad-manifest \
  tests/EMD.Core.Tests/Fixtures/freecad_manifest.json --out out/axis.json
dotnet run --project src/EMD.Cli -- export mjcf \
  --project out/axis.json --out out/axis.xml
python3 -m jsonschema -i examples/project.json schema/emd-project.schema.json
```

CLI commands:

```text
emd validate <project.json>
emd import mechmaker <machine.json> --out <project.json> [--catalog <dir>]
emd import kicad <netlist.xml> --out <project.json>
emd import freecad <file.FCStd|.step|.stl|...> --project <project.json> [--out <project.json>] [--store <dir>]
emd import freecad-manifest <manifest.json> --out <project.json> [--store <dir>]
emd import kicad-pcb <board.kicad_pcb> --project <project.json> [--out <project.json>]
emd import kicad-step --step <board.step> --project <project.json> [--board <key>] [--out <project.json>]
emd export mjcf --project <project.json> --out <scene.xml>
emd export bom --project <project.json> --out <bom.csv>
emd export harness --project <project.json> --out <harness.yml>
emd export mechmaker --project <project.json> --out <machine.json>
emd export ssp --project <project.json> --out <file.ssp> [--version 1.0|2.0]
emd compile mechmaker --project <project.json> --out <scene.xml> --mechmaker <cli.dll|exe> [--work-dir <dir>]
emd package --project <project.json> --out-dir <dir> [--trace <csv>]...
emd package verify --dir <dir>
emd trace check --project <project.json> --trace <trace.csv> [--scenario <key>]
emd renode run --project <project.json> [--firmware-project <key>] [--firmware <elf>] [--platform <repl>] [--out <log>] [--duration <s>]
emd simulate mechmaker --machine <machine.json>|--project <project.json> --out <trace.csv> --mechmaker <server.dll|exe> [--scenario <key>] [--save-trace] [--duration <s>] [--motor <id:rev/s>]
emd diff --before <project.json> --after <project.json> [--json]
emd kicad erc|drc|netlist|bom|gerbers|pos|step --schematic|--pcb <file> --out <path>
emd kicad fabrication --schematic <file.kicad_sch> --pcb <file.kicad_pcb> --out axis-fab.zip
emd slice --model <file.stl> --out <file.gcode> --slicer orca --profile <profile.ini>
emd cam verify --gcode <file.gcode> --envelope 0,0,0,400,400,100
emd cam verify --gcode <file.gcode> --project out/axis.json
emd cam simulate --gcode <file.gcode> --target design.stl --stock 0,0,0,400,400,100 --out cam-report.json
emd import freecad-path --job <path-job.json> --project out/axis.json
emd export assembly-doc --project out/axis.json --out out/assembly.md
emd gcode check --gcode <file.gcode> --envelope 0,0,0,400,400,100
emd gcode post --gcode <file.gcode> --out <file.klipper.gcode> --flavor klipper|linuxcnc|grbl|marlin|reprap
emd spice --netlist <file.cir> --out <log>
emd spice fmu --netlist <file.cir> --out <file.fmu> \
  --signals v(out),i(v1) --stop-time 0.01 --step-size 0.0001
emd artifacts verify <project.json> [--store <dir>]
emd build plan --project <project.json>
```

## Layout

```
src/EMD.Core            project graph, schema, validation, importers, MJCF exporter
src/EMD.Adapters        process host, JSON-RPC contract, tool adapters
src/EMD.Cli             validate/import/export/kicad/build/CAM commands
src/EMD.Server          MCP tools and read-only HTTP project viewer
src/EMD.Desktop         Avalonia system view
adapters/python         FreeCAD adapter + manifest helpers
docker                  deterministic CI image and smoke script
tools/mechmaker_fmu     MechMaker proxy and direct NativeAOT FMU
tools/spice_fmu         ngspice trace source-FMU generator
tools/cadquery          parametric catalog generator
tests/EMD.Core.Tests    unit, schema, importer, exporter, CLI, CAM tests
tests/EMD.Adapters.Tests process host + tool adapter tests
tests/EMD.Server.Tests  MCP tool, graph, and viewer contract tests
```

Planned additions:

```
src/EMD.Mechanical      dedicated FreeCAD workbench, richer MCAD<->ECAD
src/EMD.Electrical      closed-loop SPICE, netlist<->wiring
src/EMD.Twin            OpenModelica bridges, firmware co-simulation, HIL
src/EMD.Cam             true multi-axis and full 3D CAM verification
src/EMD.Hub             richer 3D system view and web UI
```

## Development

This project is developed **test-first (TDD)**. Every behavior change starts
with a failing test, then the minimum implementation, then a refactor. Bug
fixes start with a regression test. See [CONTRIBUTING.md](CONTRIBUTING.md) for
the required workflow, test layers, and pull-request checklist.

## Versioning

This project follows [Semantic Versioning 2.0](https://semver.org/). Releases
are annotated git tags named `vMAJOR.MINOR.PATCH`; the current release is
`v0.37.0`. See [CHANGELOG.md](CHANGELOG.md).

## License

EMD is licensed under the GNU General Public License v3.0 only
(SPDX: `GPL-3.0-only`). See [LICENSE](LICENSE).

FreeCAD (LGPL-2.1+), KiCad (GPL-3.0), ngspice (GPL), CalculiX (GPL), and
other third-party tools are integrated at process boundaries and distributed
as separate programs.
