#!/usr/bin/env python3
import argparse
"""Verify that SSP ParameterBindings are applied by OMSimulator."""

import csv
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import urllib.request
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
CLI = REPO / "src/EMD.Cli"
CACHE = HERE / ".cache"
FMU_BASE = "https://github.com/CATIA-Systems/FMPy/raw/v0.3.32/tests/resources"


def uid():
    return str(uuid.uuid4())


def fmu(name, override):
    if override:
        path = Path(override).resolve()
        if not path.exists():
            raise SystemExit(f"FMU not found: {path}")
        return path

    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / f"{name}.fmu"
    if not path.exists():
        print(f"downloading {name}.fmu", file=sys.stderr)
        urllib.request.urlretrieve(f"{FMU_BASE}/{name}.fmu", path)
    return path


def run_cli(*args, check=True):
    return subprocess.run(
        ["dotnet", "run", "--project", str(CLI), "--", *args],
        capture_output=True,
        text=True,
        check=check,
    )


def write_project(path, controller, drivetrain, controller_parameters):
    controller_id = uid()
    drivetrain_id = uid()
    project = {
        "schema_version": "0.1",
        "id": uid(),
        "name": "OMSimulator parameter binding check",
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
                "key": "plant",
                "name": "Plant",
                "kind": "virtual",
                "ports": [],
            }
        ],
        "artifacts": [
            {
                "id": controller_id,
                "key": "controller_fmu",
                "path": str(controller),
                "format": "fmu",
                "role": "simulation",
            },
            {
                "id": drivetrain_id,
                "key": "drivetrain_fmu",
                "path": str(drivetrain),
                "format": "fmu",
                "role": "simulation",
            },
        ],
        "behavior": {
            "models": [
                {
                    "id": uid(),
                    "key": "controller",
                    "name": "Controller",
                    "kind": "fmu",
                    "artifact_id": controller_id,
                    "parameters": controller_parameters,
                    "inputs": [{"name": "u_s", "type": "real", "variable": "u_s"}],
                    "outputs": [{"name": "y", "type": "real", "variable": "y"}],
                },
                {
                    "id": uid(),
                    "key": "drivetrain",
                    "name": "Drivetrain",
                    "kind": "fmu",
                    "artifact_id": drivetrain_id,
                    "inputs": [{"name": "tau", "type": "real", "variable": "tau"}],
                    "outputs": [{"name": "w", "type": "real", "variable": "w"}],
                },
            ]
        },
        "connections": [
            {
                "id": uid(),
                "key": "controller_to_drivetrain",
                "kind": "simulation_binding",
                "from": {"artifact_id": controller_id, "variable": "y"},
                "to": {"artifact_id": drivetrain_id, "variable": "tau"},
            },
            {
                "id": uid(),
                "key": "drivetrain_to_controller",
                "kind": "simulation_binding",
                "from": {"artifact_id": drivetrain_id, "variable": "w"},
                "to": {"artifact_id": controller_id, "variable": "u_s"},
            },
        ],
        "simulation": {
            "fidelity": "co_simulation",
            "systems": [
                {
                    "id": uid(),
                    "key": "twin",
                    "standard": "ssp2",
                    "master": "omsimulator",
                    "step_size_s": 0.001,
                    "start_time_s": 0,
                    "stop_time_s": 1,
                }
            ],
        },
    }
    path.write_text(json.dumps(project, indent=2), encoding="utf-8")


def omsimulator_environment():
    spec = importlib.util.find_spec("OMSimulator")
    if spec is None or not spec.submodule_search_locations:
        raise SystemExit("OMSimulator Python package is not installed")

    environment = os.environ.copy()
    library_path = str(Path(list(spec.submodule_search_locations)[0]))
    current = environment.get("LD_LIBRARY_PATH")
    environment["LD_LIBRARY_PATH"] = (
        library_path + (os.pathsep + current if current else ""))
    return environment


def simulate(ssp_path, result_path, environment, intervals=1000):
    subprocess.run(
        [
            sys.executable, "-m", "OMSimulator",
            "--resultFile", str(result_path),
            "--intervals", str(intervals),
            str(ssp_path),
        ],
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )

    lines = result_path.read_text(encoding="utf-8").splitlines()
    if len(lines) < 2:
        raise SystemExit(f"OMSimulator produced no result rows: {result_path}")
    header = next(csv.reader([lines[0]]))
    final = next(csv.reader([lines[-1]]))
    return dict(zip(header, final))


def value(values, suffix):
    found = next(
        (float(value) for name, value in values.items() if name.endswith(suffix)),
        None,
    )
    if found is None:
        raise SystemExit(f"No {suffix} in OMSimulator result: {list(values)}")
    return found


def write_direct_project(path, mechmaker_fmu):
    artifact_id = uid()
    project = {
        "schema_version": "0.1",
        "id": uid(),
        "name": "Direct MechMaker parameter check",
        "units": {
            "system": "SI", "length": "m", "mass": "kg", "time": "s",
            "angle": "rad", "temperature": "K", "current": "A", "voltage": "V",
        },
        "coordinate_system": {
            "handedness": "right", "up_axis": "+Z", "forward_axis": "+X",
            "rotation": {"order": ["X", "Y", "Z"], "extrinsic": True},
        },
        "parts": [{"id": uid(), "key": "plant", "name": "Plant",
                   "kind": "virtual", "ports": []}],
        "artifacts": [{"id": artifact_id, "key": "mechmaker_fmu",
                       "path": str(mechmaker_fmu), "format": "fmu",
                       "role": "simulation"}],
        "behavior": {"models": [{
            "id": uid(), "key": "mechmaker", "name": "MechMaker",
            "kind": "fmu", "artifact_id": artifact_id,
            "parameters": {
                "microsteps": 16,
                "carriage_m_per_rev": 0.04,
                "motor1_velocity": 0.2,
                "motor1_enable": True,
            },
        }]},
        "simulation": {"fidelity": "co_simulation", "systems": [{
            "id": uid(), "key": "twin", "standard": "ssp2",
            "master": "omsimulator", "step_size_s": 0.01,
            "start_time_s": 0, "stop_time_s": 0.1,
        }]},
    }
    path.write_text(json.dumps(project, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mechmaker-fmu", default="")
    args = parser.parse_args()
    controller = fmu("Controller", os.environ.get("EMD_CONTROLLER_FMU", ""))
    drivetrain = fmu("Drivetrain", os.environ.get("EMD_DRIVETRAIN_FMU", ""))
    environment = omsimulator_environment()

    with tempfile.TemporaryDirectory(prefix="emd-omsim-") as directory:
        work = Path(directory)
        results = {}
        for name, parameters in (("baseline", {}), ("limited", {"PI.yMax": 6.0})):
            project = work / f"{name}.json"
            ssp = work / f"{name}.ssp"
            result = work / f"{name}.csv"
            write_project(project, controller, drivetrain, parameters)
            run_cli("export", "ssp", "--project", str(project),
                    "--out", str(ssp), "--version", "2.0")
            results[name] = simulate(ssp, result, environment)

        baseline = value(results["baseline"], ".controller.y")
        limited = value(results["limited"], ".controller.y")
        if abs(baseline - 12.0) > 0.01:
            raise SystemExit(f"baseline controller.y should be 12, got {baseline}")
        if abs(limited - 6.0) > 0.01:
            raise SystemExit(
                "PI.yMax parameter binding was not applied; "
                f"controller.y={limited}")

        direct_carriage = None
        if args.mechmaker_fmu:
            mechmaker_fmu = Path(args.mechmaker_fmu).resolve()
            if not mechmaker_fmu.exists():
                raise SystemExit(f"FMU not found: {mechmaker_fmu}")
            project = work / "direct.json"
            ssp = work / "direct.ssp"
            result = work / "direct.csv"
            write_direct_project(project, mechmaker_fmu)
            run_cli("export", "ssp", "--project", str(project),
                    "--out", str(ssp), "--version", "2.0")
            direct_carriage = value(
                simulate(ssp, result, environment, intervals=100),
                ".x_carriage_m")
            if direct_carriage <= 0:
                raise SystemExit(
                    f"direct FMU did not move: x_carriage_m={direct_carriage}")

    print(f"baseline controller.y: {baseline:.3f}")
    print(f"PI.yMax=6 controller.y: {limited:.3f}")
    if direct_carriage is not None:
        print(f"direct x_carriage_m: {direct_carriage:.6f}")
    print("OMSimulator parameter bindings OK")


if __name__ == "__main__":
    main()
