# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] - 2026-10-31

### Added

- Interference and joint-limit checks: oriented-box SAT, box/sphere and
  sphere/sphere penetration, mate/joint overlap exemptions, interference
  tolerance, and slide/hinge range checks (`EMD-MECH-001`/`002`).
- Pose-math collision helpers and conservative proxy bounds for box, cylinder,
  sphere, capsule, and ellipsoid geometry.

## [0.3.0] - 2026-10-31

### Added

- Project schema: simulation-proxy `part.geometry` and `mechanical.joints`
  (fixed/hinge/slide/ball/free, axis, range, actuator, damping, stiffness).
- Extrinsic-XYZ pose transforms (`PoseMath`) with relative/compose operations.
- MJCF exporter: body tree from mates and joints, box/cylinder/sphere/capsule/
  ellipsoid/mesh geoms, hinge/slide/ball/free joints, motors, and
  `EMD-MJCF-*` diagnostics.
- CAD manifest schema (`cad-manifest.schema.json`) and FreeCAD manifest
  importer: parts, connectors, geometry proxies, joints, artifacts, and BOM.
- Starter FreeCAD JSON-RPC adapter under `adapters/python`, with pure manifest
  helpers and Python tests (no FreeCAD required for the test suite).
- CLI: `import freecad-manifest` and `export mjcf`.
- CI: CAD manifest schema validation and Python adapter tests.

## [0.2.0] - 2026-10-31

### Added

- .NET 10 solution with `EMD.Core`, `EMD.Adapters`, and `EMD.Cli`.
- Schema validation, project loading/writing, and the typed project model.
- Cross-domain validator: `EMD-XREF`, `EMD-ELEC`, `EMD-FW`, `EMD-SIM`,
  `EMD-MECH`, `EMD-ART`, `EMD-ID`, and `EMD-PORT` diagnostics.
- Content-addressed artifact store with SHA-256 verification.
- Build planner with topological ordering, missing-input, duplicate-node, and
  cycle diagnostics.
- Deterministic MechMaker `machine.json` importer (UUID v5, stable across
  imports) with optional catalog metadata and wiring-to-net mapping.
- Deterministic KiCad XML netlist importer with component/pin/net mapping and
  power-symbol filtering.
- FreeCAD artifact importer for `.FCStd`, STEP, IGES, STL, OBJ, glTF, and GLB;
  optional content-addressed store integration and mechanical source/export
  wiring.
- Process tool host with argument boundaries, stdin/stdout/stderr capture,
  timeout handling, and cancellation.
- `IToolAdapter` and a one-shot JSON-RPC 2.0 stdio adapter with structured
  artifacts, diagnostics, and error handling (`EMD-ADAPTER-001`..`006`).
- Tool adapter protocol documentation with request/response examples and a
  minimal Python adapter.
- CLI commands: `validate`, `import mechmaker`, `import kicad`,
  `import freecad`, `build plan`, and `artifacts verify`.
- 72 xUnit tests and CI format/test gates.

### Changed

- Project schema: parts now carry an optional `pose` (position/rotation),
  preserving MechMaker placements during import.

## [0.1.0] - 2026-10-31

### Added

- RFC 0001: architecture for a unified electromechanical CAD/CAM suite.
- EMD project schema v0.1 with typed ports, cross-domain connections, artifacts,
  toolchain pinning, and co-simulation metadata.
- Motorized-axis example project covering FreeCAD geometry, KiCad electrical
  design, firmware pin mapping, and an SSP/FMU twin.
- Contributor guide with a mandatory test-driven development (TDD) workflow.
- GPL-3.0-only license.

[Unreleased]: https://github.com/tkleisas/ElectroMechanicalDesigner/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/tkleisas/ElectroMechanicalDesigner/releases/tag/v0.4.0
[0.3.0]: https://github.com/tkleisas/ElectroMechanicalDesigner/releases/tag/v0.3.0
[0.2.0]: https://github.com/tkleisas/ElectroMechanicalDesigner/releases/tag/v0.2.0
[0.1.0]: https://github.com/tkleisas/ElectroMechanicalDesigner/releases/tag/v0.1.0
