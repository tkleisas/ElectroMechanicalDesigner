# ElectroMechanicalDesigner (EMD)

An open-source CAD/CAM suite for electromechanical systems: draw the mechanics,
draw the electronics, write the firmware, and simulate the whole machine as a
digital twin from one traceable project.

**Status: P2 in progress.** P0 and the P1 mechanical slice are complete:
RFC/schema, .NET 10 core, cross-domain validation, content-addressed artifacts,
build planning, deterministic MechMaker/KiCad importers, FreeCAD manifest and
startup adapter, pose transforms, MJCF export, and interference/joint-limit
checks. P2 adds BOM export, a headless `kicad-cli` adapter (ERC/DRC/netlist/
BOM/Gerbers/STEP), KiCad PCB outline/mounting-hole import, an ngspice batch
adapter, WireViz harness export, a round-trippable MechMaker machine exporter,
and cross-domain ERC checks (refdes uniqueness, net/port voltages, firmware
pin-to-net connectivity), simulation-binding validation, and a MechMaker
compile hand-off. P3 groundwork adds live MechMaker simulation over MCP and
trace-assertion checking. P1 adds motor phase topology, clearance checks, and
slide/hinge motion sweeps. The package exporter bundles the verifiable
design/manufacturing artifacts. The runnable MVP SSP test moves the linear axis
40 mm, checks the scenario trace, exports a BOM, and rejects a swapped motor
phase. The suite has 234 xUnit tests, 12 Python adapter tests, and 2 CadQuery tests.

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
- [CAD manifest schema v0.1](schema/cad-manifest.schema.json)
- [Example project](examples/project.json)
- [Tool adapter protocol](docs/adapter-protocol.md)
- [Starter FreeCAD adapter](adapters/python/README.md)
- [Contributing guide (TDD is mandatory)](CONTRIBUTING.md)

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
emd export mjcf --project <project.json> --out <scene.xml>
emd export bom --project <project.json> --out <bom.csv>
emd export harness --project <project.json> --out <harness.yml>
emd export mechmaker --project <project.json> --out <machine.json>
emd export ssp --project <project.json> --out <file.ssp> [--version 1.0|2.0]
emd compile mechmaker --project <project.json> --out <scene.xml> --mechmaker <cli.dll|exe> [--work-dir <dir>]
emd package --project <project.json> --out-dir <dir> [--trace <csv>]...
emd package verify --dir <dir>
emd trace check --project <project.json> --trace <trace.csv> [--scenario <key>]
emd simulate mechmaker --machine <machine.json>|--project <project.json> --out <trace.csv> --mechmaker <server.dll|exe> [--scenario <key>] [--save-trace] [--duration <s>] [--motor <id:rev/s>]
emd diff --before <project.json> --after <project.json> [--json]
emd kicad erc|drc|netlist|bom|gerbers|step --schematic|--pcb <file> --out <path>
emd spice --netlist <file.cir> --out <log>
emd spice fmu --netlist <file.cir> --out <file.fmu> \
  --signals v(out),i(v1) --stop-time 0.01 --step-size 0.0001
emd artifacts verify <project.json> [--store <dir>]
emd build plan --project <project.json>
```

## Layout

```
src/EMD.Core            project graph, schema, validation, importers, MJCF exporter
src/EMD.Adapters        process host, JSON-RPC contract, KiCad CLI adapter
src/EMD.Cli             validate/import/export/kicad/build/artifacts commands
adapters/python         starter FreeCAD adapter + pure manifest helpers
tests/EMD.Core.Tests    unit, schema, importer, exporter, CLI tests
tests/EMD.Adapters.Tests process host + JSON-RPC adapter tests
```

Planned additions:

```
src/EMD.Mechanical      richer FreeCAD/CadQuery export, MCAD<->ECAD
src/EMD.Electrical      KiCad/ngspice adapter, netlist<->wiring, harness
src/EMD.Twin            FMU wrappers, SSP composer, OMSimulator, HIL
src/EMD.Cam             CAM and manufacturing outputs
src/EMD.Hub             Avalonia shell, MCP server, web viewer
tools/mechmaker_fmu     self-contained MechMaker proxy and direct NativeAOT FMU
```

## Development

This project is developed **test-first (TDD)**. Every behavior change starts
with a failing test, then the minimum implementation, then a refactor. Bug
fixes start with a regression test. See [CONTRIBUTING.md](CONTRIBUTING.md) for
the required workflow, test layers, and pull-request checklist.

## Versioning

This project follows [Semantic Versioning 2.0](https://semver.org/). Releases
are annotated git tags named `vMAJOR.MINOR.PATCH`; the current release is
`v0.31.0`. See [CHANGELOG.md](CHANGELOG.md).

## License

EMD is licensed under the GNU General Public License v3.0 only
(SPDX: `GPL-3.0-only`). See [LICENSE](LICENSE).

FreeCAD (LGPL-2.1+), KiCad (GPL-3.0), ngspice (GPL), CalculiX (GPL), and
other third-party tools are integrated at process boundaries and distributed
as separate programs.
