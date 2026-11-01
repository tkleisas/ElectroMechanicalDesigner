# MechMaker FMU (FMI 2.0 co-simulation proxy)

`mechmaker_slave.py` wraps the MechMaker MCP server as an FMI 2.0 co-simulation
FMU. The FMU starts `MechMaker.Server`, opens a `machine.json`, runs the
virtual MCU/physics loop, and exposes up to four stepper channels.

`direct/DirectFmu.cs` is the direct NativeAOT implementation: it loads
`MechMaker.Engine` inside the FMU process, with no server, MCP, subprocess, or
host .NET runtime. The direct FMU is about 8 MB instead of ~183 MB and exposes
the same variables.

The default build embeds a self-contained MechMaker server, the part catalog,
and `linear_axis_v0.json` in the FMU resources. The host therefore does not need
a .NET runtime; the parameters below are optional overrides.

Building the FMU still requires a sibling MechMaker checkout so the server can
be published once. The resulting FMU is large (~183 MB).

## Build

```bash
python3 -m pip install pythonfmu
./build.sh
```

This produces `MechMakerSlave.fmu`.

For the direct FMU (requires the .NET 10 SDK, `clang`, and `zlib1g-dev`):

```bash
./build_direct.sh
python3 verify_ssp.py --mechmaker-fmu MechMakerDirect.fmu
```

This produces `MechMakerDirect.fmu` and runs the same MVP scenario against it.
`verify_omsim.py --mechmaker-fmu MechMakerDirect.fmu` also verifies the direct
FMU and its parameter bindings in OMSimulator.

## Parameters

| Parameter | Meaning |
|---|---|
| `machine_path` | Optional path to a MechMaker `machine.json` (defaults to the embedded machine) |
| `server_path` | Optional path to a MechMaker server executable or DLL |
| `catalog_path` | Optional catalog directory (defaults to the embedded catalog) |
| `microsteps` | Microsteps per full step (default 16) |
| `carriage_m_per_rev` | Carriage travel per motor revolution (default 0.04 m) |

## Variables

For channels 1–4:

- inputs: `motorN_velocity` (rev/s), `motorN_enable` (Boolean)
- outputs: `motorN_angle` (actual revolutions)

There is also an output `x_carriage_m`, computed as
`motor1_angle * carriage_m_per_rev`.

Channels are assigned to the first four wired `step` components in the machine.

## Verify with FMPy

```bash
python3 -m pip install fmpy
python3 verify_fmpy.py --velocity 1.0 --stop-time 0.1 --step-size 0.01
```

The script accepts optional `--machine`, `--server`, and `--catalog` overrides.

The FMU can also be referenced from an SSP composition.

## SSP integration test

`verify_ssp.py` builds a small `CommandSlave.fmu`, loads the MVP project
template, exports an SSP 1.0 archive, and co-simulates Command → MechMaker
through FMPy. It runs the `move_40mm` scenario for 5 s, writes a trace, and
checks the scenario assertion through `emd trace check`. It also exports a BOM
and verifies that a swapped motor phase fails validation with `EMD-ELEC-007`.

```bash
./build.sh
python3 verify_ssp.py
```

The test is also run in CI after publishing MechMaker at a pinned revision.

The generated SSP carries behavior-model parameters as standard SSP
`ParameterBindings`; the MVP project sets `microsteps` and
`carriage_m_per_rev` this way. `verify_omsim.py` runs the same archive through
OMSimulator and confirms that a `PI.yMax` binding changes the controller output
from 12 to 6.

`verify_imports.py` runs the real FreeCAD-manifest and KiCad-netlist importers,
merges a linear-axis fixture, adds firmware pin bindings, and validates the
combined project.

## Fidelity limit

Both FMUs expose motor velocity and enable per channel, not an external torque
input. MechMaker computes stepper magnetic torque internally from the commanded
motion. The direct FMU removes the process boundary but still models the motor
through MechMaker's stepper/MCU loop; true torque-level and HIL interfaces
remain future work.
