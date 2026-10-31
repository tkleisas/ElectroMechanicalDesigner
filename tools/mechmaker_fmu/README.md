# MechMaker FMU (FMI 2.0 co-simulation proxy)

`mechmaker_slave.py` wraps the MechMaker MCP server as an FMI 2.0 co-simulation
FMU. The FMU starts `MechMaker.Server`, opens a `machine.json`, runs the
virtual MCU/physics loop, and exposes up to four stepper channels.

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

## Parameters

| Parameter | Meaning |
|---|---|
| `machine_path` | Optional path to a MechMaker `machine.json` (defaults to the embedded machine) |
| `server_path` | Optional path to a MechMaker server executable or DLL |
| `catalog_path` | Optional catalog directory (defaults to the embedded catalog) |
| `microsteps` | Microsteps per full step (default 16) |

## Variables

For channels 1–4:

- inputs: `motorN_velocity` (rev/s), `motorN_enable` (Boolean)
- outputs: `motorN_angle` (actual revolutions)

Channels are assigned to the first four wired `step` components in the machine.

## Verify with FMPy

```bash
python3 -m pip install fmpy
python3 verify_fmpy.py --velocity 1.0 --stop-time 0.1 --step-size 0.01
```

The script accepts optional `--machine`, `--server`, and `--catalog` overrides.

The FMU can also be referenced from an SSP composition.
