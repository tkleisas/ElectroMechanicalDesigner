# MechMaker FMU (FMI 2.0 co-simulation proxy)

`mechmaker_slave.py` wraps the MechMaker MCP server as an FMI 2.0 co-simulation
FMU. The FMU starts `MechMaker.Server`, opens a `machine.json`, runs the
virtual MCU/physics loop, and exposes up to four stepper channels.

This is a **proxy FMU**: the host needs the .NET runtime and a built MechMaker
server. It is intended for co-simulation experiments and SSP systems, not for
standalone distribution.

## Build

```bash
python3 -m pip install pythonfmu
./build.sh
```

This produces `MechMakerSlave.fmu`.

## Parameters

| Parameter | Meaning |
|---|---|
| `machine_path` | Absolute path to a MechMaker `machine.json` |
| `server_path` | Absolute path to `MechMaker.Server.dll` |
| `catalog_path` | MechMaker catalog directory (`MECHMAKER_CATALOG`) |
| `microsteps` | Microsteps per full step (default 16) |

## Variables

For channels 1–4:

- inputs: `motorN_velocity` (rev/s), `motorN_enable` (Boolean)
- outputs: `motorN_angle` (actual revolutions)

Channels are assigned to the first four wired `step` components in the machine.

## Verify with FMPy

```bash
python3 -m pip install fmpy
python3 verify_fmpy.py \
  --machine /path/to/MechMaker/examples/linear_axis_v0.json \
  --server /path/to/MechMaker/src/MechMaker.Server/bin/Debug/net10.0/MechMaker.Server.dll \
  --catalog /path/to/MechMaker/catalog \
  --velocity 1.0 --stop-time 0.1 --step-size 0.01
```

The FMU can also be referenced from an SSP composition.
