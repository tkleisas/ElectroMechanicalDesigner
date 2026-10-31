# ElectroMechanicalDesigner (EMD)

An open-source CAD/CAM suite for electromechanical systems: draw the mechanics,
draw the electronics, write the firmware, and simulate the whole machine as a
digital twin from one traceable project.

**Status: architecture draft.** This repository currently contains the RFC and
the v0.1 project schema. Implementation starts with the P0 scaffold.

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
- [Example project](examples/project.json)
- [Contributing guide (TDD is mandatory)](CONTRIBUTING.md)

Validate the example:

```bash
python3 -m jsonschema -i examples/project.json schema/emd-project.schema.json
```

## Planned layout

```
src/EMD.Core            system graph, ports, xrefs, validation, build graph
src/EMD.Adapters        IToolAdapter, JSON-RPC process host, artifact store
src/EMD.Mechanical      FreeCAD/CadQuery adapter, STEP<->MJCF, MCAD<->ECAD
src/EMD.Electrical      KiCad/ngspice adapter, netlist<->wiring, harness
src/EMD.Twin            FMU wrappers, SSP composer, OMSimulator, HIL
src/EMD.Cam             CAM and manufacturing outputs
src/EMD.Hub             Avalonia shell, MCP server, web viewer
adapters/python/        FreeCAD/KiCad/OpenModelica scripts
tests/                  unit, adapter-contract, integration, end-to-end
```

## Development

This project is developed **test-first (TDD)**. Every behavior change starts
with a failing test, then the minimum implementation, then a refactor. Bug
fixes start with a regression test. See [CONTRIBUTING.md](CONTRIBUTING.md) for
the required workflow, test layers, and pull-request checklist.

## Versioning

This project follows [Semantic Versioning 2.0](https://semver.org/). Releases
are annotated git tags named `vMAJOR.MINOR.PATCH`; the current release is
`v0.1.0`. See [CHANGELOG.md](CHANGELOG.md).

## License

EMD is licensed under the GNU General Public License v3.0 only
(SPDX: `GPL-3.0-only`). See [LICENSE](LICENSE).

FreeCAD (LGPL-2.1+), KiCad (GPL-3.0), ngspice (GPL), CalculiX (GPL), and
other third-party tools are integrated at process boundaries and distributed
as separate programs.
