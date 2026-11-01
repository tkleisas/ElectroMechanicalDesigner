#!/usr/bin/env python3
"""Runnable MVP co-simulation test: command FMU -> MechMaker -> SSP trace."""

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
CLI = REPO / "src/EMD.Cli"
TEMPLATE = REPO / "examples/project.json"


def uid():
    return str(uuid.uuid4())


def build_command_fmu():
    subprocess.run(
        [sys.executable, "-m", "pythonfmu", "build", "-f", "command_slave.py", "--dest", "."],
        cwd=HERE,
        check=True,
        stdout=subprocess.DEVNULL,
    )


def write_project(path, mechmaker_fmu, stop_time, step_size, swapped=False):
    project = json.loads(TEMPLATE.read_text(encoding="utf-8"))
    project["id"] = uid()
    project["name"] = "Runnable motorized axis MVP"

    command_artifact = uid()
    mechmaker_artifact = uid()
    project["artifacts"].extend([
        {
            "id": command_artifact,
            "key": "mvp_command_fmu",
            "path": str(HERE / "CommandSlave.fmu"),
            "format": "fmu",
            "role": "simulation",
        },
        {
            "id": mechmaker_artifact,
            "key": "mvp_mechmaker_fmu",
            "path": str(mechmaker_fmu),
            "format": "fmu",
            "role": "simulation",
        },
    ])

    project["behavior"] = {
        "models": [
            {
                "id": uid(),
                "key": "command",
                "name": "40 mm command",
                "kind": "fmu",
                "artifact_id": command_artifact,
                "outputs": [
                    {"name": "velocity", "type": "real", "variable": "velocity"},
                    {"name": "enable", "type": "boolean", "variable": "enable"},
                ],
            },
            {
                "id": uid(),
                "key": "mechmaker",
                "name": "MechMaker linear axis",
                "kind": "fmu",
                "artifact_id": mechmaker_artifact,
                "parameters": {
                    "microsteps": 16,
                    "carriage_m_per_rev": 0.04,
                },
                "inputs": [
                    {"name": "velocity", "type": "real", "variable": "motor1_velocity"},
                    {"name": "enable", "type": "boolean", "variable": "motor1_enable"},
                ],
                "outputs": [
                    {"name": "carriage", "type": "real", "variable": "x_carriage_m"},
                ],
            },
        ]
    }

    velocity_binding = uid()
    enable_binding = uid()
    project["connections"] = [
        connection for connection in project["connections"]
        if connection["kind"] != "simulation_binding"
    ]
    project["connections"].extend([
        {
            "id": velocity_binding,
            "key": "command_velocity",
            "kind": "simulation_binding",
            "from": {"artifact_id": command_artifact, "variable": "velocity"},
            "to": {"artifact_id": mechmaker_artifact, "variable": "motor1_velocity"},
        },
        {
            "id": enable_binding,
            "key": "command_enable",
            "kind": "simulation_binding",
            "from": {"artifact_id": command_artifact, "variable": "enable"},
            "to": {"artifact_id": mechmaker_artifact, "variable": "motor1_enable"},
        },
    ])

    system = project["simulation"]["systems"][0]
    system["master"] = "fmpy"
    system["step_size_s"] = step_size
    system["start_time_s"] = 0
    system["stop_time_s"] = stop_time
    system["bindings"] = [velocity_binding, enable_binding]
    old_scenario = project["simulation"]["scenarios"][0]
    project["simulation"]["scenarios"] = [
        {
            "id": uid(),
            "key": "move_40mm",
            "name": "Move carriage 40 mm",
            "artifact_id": old_scenario["artifact_id"],
            "duration_s": stop_time,
            "seed": 42,
            "assertions": [
                {
                    "name": "carriage reaches 40 mm",
                    "expression": "abs(x_carriage_m - 0.040) < 0.001",
                    "unit": "m",
                    "tolerance": 0.001,
                    "severity": "error",
                }
            ],
            "record": ["x_carriage_m"],
        }
    ]

    if swapped:
        m1a = next(
            net["id"] for net in project["electrical"]["nets"]
            if net["key"] == "m1a"
        )
        phase_b = next(
            connection for connection in project["connections"]
            if connection.get("key") == "motor_phase_b"
        )
        phase_b["to"]["net_id"] = m1a

    path.write_text(json.dumps(project, indent=2), encoding="utf-8")


def run_cli(*args, check=True):
    return subprocess.run(
        ["dotnet", "run", "--project", str(CLI), "--", *args],
        capture_output=True,
        text=True,
        check=check,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mechmaker-fmu", default=str(HERE / "MechMakerSlave.fmu"))
    parser.add_argument("--stop-time", type=float, default=5.0)
    parser.add_argument("--step-size", type=float, default=0.01)
    args = parser.parse_args()

    mechmaker_fmu = Path(args.mechmaker_fmu).resolve()
    if not mechmaker_fmu.exists():
        raise SystemExit(f"MechMaker FMU not found: {mechmaker_fmu}; run ./build.sh")
    build_command_fmu()

    with tempfile.TemporaryDirectory(prefix="emd-mvp-") as directory:
        work = Path(directory)
        project_path = work / "project.json"
        swapped_path = work / "project_swapped.json"
        ssp_path = work / "axis.ssp"
        trace_path = work / "trace.csv"
        bom_path = work / "bom.csv"

        write_project(project_path, mechmaker_fmu, args.stop_time, args.step_size)
        write_project(swapped_path, mechmaker_fmu, args.stop_time, args.step_size,
                      swapped=True)

        valid = run_cli("validate", str(project_path))
        if valid.returncode != 0:
            raise SystemExit(f"baseline validation failed:\n{valid.stderr}")

        swapped = run_cli("validate", str(swapped_path), check=False)
        swapped_line = next(
            (line for line in swapped.stderr.splitlines() if "EMD-ELEC-007" in line),
            "",
        )
        if swapped.returncode == 0 or not swapped_line:
            raise SystemExit(
                "swapped motor phase did not fail validation with EMD-ELEC-007:\n"
                f"{swapped.stdout}{swapped.stderr}")

        bom = run_cli("export", "bom", "--project", str(project_path),
                      "--out", str(bom_path))
        if not bom_path.read_text(encoding="utf-8").startswith("item,"):
            raise SystemExit(f"BOM export is invalid:\n{bom.stdout}{bom.stderr}")

        run_cli("export", "ssp", "--project", str(project_path),
                "--out", str(ssp_path), "--version", "1.0")

        result = simulate_ssp(
            str(ssp_path),
            stop_time=args.stop_time,
            step_size=args.step_size,
        )

        carriage_columns = [
            name for name in result.dtype.names
            if name.endswith(".x_carriage_m")
        ]
        if not carriage_columns:
            raise SystemExit(f"No x_carriage_m output in {result.dtype.names}")
        carriage_column = carriage_columns[0]
        carriage = float(result[carriage_column][-1])

        with trace_path.open("w", encoding="utf-8") as trace:
            trace.write("time,x_carriage_m\n")
            for row in result:
                trace.write(f"{float(row['time']):.9g},{float(row[carriage_column]):.9g}\n")

        tracecheck = run_cli(
            "trace", "check",
            "--project", str(project_path),
            "--trace", str(trace_path),
            "--scenario", "move_40mm",
        )

    if abs(carriage - 0.040) >= 0.001:
        raise SystemExit(f"expected carriage at 40 mm, got {carriage:.6f} m")

    print(f"baseline validation: {valid.stdout.strip()}")
    print(f"swapped phase: {swapped_line.strip()}")
    print(f"bom: {bom.stdout.strip()}")
    print(f"trace check: {tracecheck.stdout.strip()}")
    print(f"x_carriage_m: {carriage:.6f} m")
    print("MVP SSP integration OK")


if __name__ == "__main__":
    main()
