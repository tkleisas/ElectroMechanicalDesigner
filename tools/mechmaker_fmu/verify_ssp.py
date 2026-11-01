#!/usr/bin/env python3
"""End-to-end SSP test: Command FMU -> MechMaker FMU through FMPy."""

import argparse
import json
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

from fmpy.ssp.simulation import simulate_ssp

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


def uid():
    return str(uuid.uuid4())


def build_command_fmu(path):
    if path.exists():
        return
    subprocess.run(
        [sys.executable, "-m", "pythonfmu", "build", "-f", "command_slave.py", "--dest", "."],
        cwd=HERE,
        check=True,
        stdout=subprocess.DEVNULL,
    )


def write_project(path, command_fmu, mechmaker_fmu, stop_time, step_size):
    command_id = uid()
    mechmaker_id = uid()
    project = {
        "schema_version": "0.1",
        "id": uid(),
        "name": "MechMaker SSP integration",
        "units": {
            "system": "SI",
            "length": "m",
            "mass": "kg",
            "time": "s",
            "angle": "rad",
            "temperature": "K",
            "current": "A",
            "voltage": "V",
        },
        "coordinate_system": {
            "handedness": "right",
            "up_axis": "+Z",
            "forward_axis": "+X",
            "rotation": {"order": ["X", "Y", "Z"], "extrinsic": True},
        },
        "parts": [
            {
                "id": uid(),
                "key": "frame",
                "name": "Frame",
                "kind": "component",
                "ports": [],
            }
        ],
        "artifacts": [
            {
                "id": command_id,
                "key": "command_fmu",
                "path": str(command_fmu),
                "format": "fmu",
                "role": "simulation",
            },
            {
                "id": mechmaker_id,
                "key": "mechmaker_fmu",
                "path": str(mechmaker_fmu),
                "format": "fmu",
                "role": "simulation",
            },
        ],
        "behavior": {
            "models": [
                {
                    "id": uid(),
                    "key": "command",
                    "name": "Command",
                    "kind": "fmu",
                    "artifact_id": command_id,
                    "outputs": [
                        {"name": "velocity", "type": "real", "variable": "velocity"},
                        {"name": "enable", "type": "boolean", "variable": "enable"},
                    ],
                },
                {
                    "id": uid(),
                    "key": "mechmaker",
                    "name": "MechMaker",
                    "kind": "fmu",
                    "artifact_id": mechmaker_id,
                    "inputs": [
                        {"name": "velocity", "type": "real", "variable": "motor1_velocity"},
                        {"name": "enable", "type": "boolean", "variable": "motor1_enable"},
                    ],
                    "outputs": [
                        {"name": "angle", "type": "real", "variable": "motor1_angle"},
                    ],
                },
            ]
        },
        "connections": [
            {
                "id": uid(),
                "key": "velocity",
                "kind": "simulation_binding",
                "from": {"artifact_id": command_id, "variable": "velocity"},
                "to": {"artifact_id": mechmaker_id, "variable": "motor1_velocity"},
            },
            {
                "id": uid(),
                "key": "enable",
                "kind": "simulation_binding",
                "from": {"artifact_id": command_id, "variable": "enable"},
                "to": {"artifact_id": mechmaker_id, "variable": "motor1_enable"},
            },
        ],
        "simulation": {
            "fidelity": "co_simulation",
            "systems": [
                {
                    "id": uid(),
                    "key": "twin",
                    "standard": "ssp2",
                    "master": "fmpy",
                    "step_size_s": step_size,
                    "start_time_s": 0,
                    "stop_time_s": stop_time,
                }
            ],
        },
    }
    path.write_text(json.dumps(project, indent=2))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mechmaker-fmu", default=str(HERE / "MechMakerSlave.fmu"))
    parser.add_argument("--command-fmu", default=str(HERE / "CommandSlave.fmu"))
    parser.add_argument("--stop-time", type=float, default=0.1)
    parser.add_argument("--step-size", type=float, default=0.01)
    parser.add_argument("--minimum-angle", type=float, default=0.05)
    args = parser.parse_args()

    mechmaker_fmu = Path(args.mechmaker_fmu).resolve()
    command_fmu = Path(args.command_fmu).resolve()
    if not mechmaker_fmu.exists():
        raise SystemExit(f"MechMaker FMU not found: {mechmaker_fmu}; run ./build.sh")
    build_command_fmu(command_fmu)

    with tempfile.TemporaryDirectory(prefix="emd-ssp-") as directory:
        work = Path(directory)
        project_path = work / "project.json"
        ssp_path = work / "system.ssp"
        write_project(project_path, command_fmu, mechmaker_fmu,
                      args.stop_time, args.step_size)

        subprocess.run(
            [
                "dotnet", "run", "--project", str(REPO / "src/EMD.Cli"),
                "--", "export", "ssp",
                "--project", str(project_path),
                "--out", str(ssp_path),
                "--version", "1.0",
            ],
            check=True,
            stdout=subprocess.DEVNULL,
        )

        result = simulate_ssp(
            str(ssp_path),
            stop_time=args.stop_time,
            step_size=args.step_size,
        )

    angle_columns = [name for name in result.dtype.names if name.endswith(".motor1_angle")]
    if not angle_columns:
        raise SystemExit(f"No motor1_angle output in {result.dtype.names}")
    angle = float(result[angle_columns[0]][-1])
    if angle < args.minimum_angle:
        raise SystemExit(f"expected motor1_angle >= {args.minimum_angle}, got {angle}")

    print(f"components: {[name for name in result.dtype.names if not name.endswith('.motor1_angle')]}")
    print(f"motor1_angle: {angle:.6f} rev")
    print("SSP integration OK")


if __name__ == "__main__":
    main()
