#!/usr/bin/env python3
"""Import FreeCAD + KiCad fixtures, merge them, and verify firmware pin bindings."""

import argparse
import json
import subprocess
import tempfile
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
CLI = REPO / "src/EMD.Cli"


def uid():
    return str(uuid.uuid4())


def run_cli(*args, check=True):
    return subprocess.run(
        ["dotnet", "run", "--project", str(CLI), "--", *args],
        capture_output=True,
        text=True,
        check=check,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--freecad-manifest",
        default=str(REPO / "tests/EMD.Core.Tests/Fixtures/freecad_manifest.json"))
    parser.add_argument(
        "--kicad-netlist",
        default=str(HERE / "fixtures/linear_axis_netlist.xml"))
    parser.add_argument("--out", default="")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="emd-import-") as directory:
        work = Path(directory)
        freecad_path = work / "freecad.json"
        kicad_path = work / "kicad.json"
        combined_path = Path(args.out) if args.out else work / "combined.json"

        freecad_import = run_cli(
            "import", "freecad-manifest", args.freecad_manifest,
            "--out", str(freecad_path))
        kicad_import = run_cli(
            "import", "kicad", args.kicad_netlist,
            "--out", str(kicad_path))

        freecad_valid = run_cli("validate", str(freecad_path))
        kicad_valid = run_cli("validate", str(kicad_path))

        freecad = json.loads(freecad_path.read_text(encoding="utf-8"))
        kicad = json.loads(kicad_path.read_text(encoding="utf-8"))

        freecad_parts = {part["key"] for part in freecad["parts"]}
        if not {"frame", "carriage"} <= freecad_parts:
            raise SystemExit(f"FreeCAD import is missing parts: {freecad_parts}")
        joints = {joint["key"] for joint in freecad["mechanical"]["joints"]}
        if "x_slide" not in joints:
            raise SystemExit(f"FreeCAD import is missing x_slide: {joints}")

        kicad_parts = {part["key"] for part in kicad["parts"]}
        if not {"M1", "A1", "U1", "P1"} <= kicad_parts:
            raise SystemExit(f"KiCad import is missing parts: {kicad_parts}")
        nets = {net["key"]: net["id"] for net in kicad["electrical"]["nets"]}
        if not {"M1A", "M1B", "M1C", "STEP", "DIR", "ENABLE"} <= nets.keys():
            raise SystemExit(f"KiCad import is missing nets: {nets.keys()}")

        combined = freecad
        combined["name"] = "Linear axis imports"
        combined["parts"] += kicad["parts"]
        combined["connections"] += kicad["connections"]
        combined["electrical"] = kicad["electrical"]

        motor = next(part for part in combined["parts"] if part["key"] == "M1")
        motor["ports"].append({
            "id": "mount",
            "key": "mount",
            "name": "mount",
            "domain": "mechanical",
            "kind": "mount",
            "direction": "passive",
            "pose": {},
        })
        frame = next(part for part in combined["parts"] if part["key"] == "frame")
        combined["connections"].append({
            "id": uid(),
            "key": "motor_on_frame",
            "kind": "mate",
            "from": {"part_id": motor["id"], "port_id": "mount"},
            "to": {"part_id": frame["id"], "port_id": "motor_mount"},
        })

        firmware_source = work / "firmware.c"
        firmware_source.write_text("int main(void) { return 0; }\n", encoding="utf-8")
        firmware_artifact = uid()
        combined["artifacts"].append({
            "id": firmware_artifact,
            "key": "firmware_source",
            "path": str(firmware_source),
            "format": "c",
            "role": "source",
        })
        mcu = next(part for part in combined["parts"] if part["key"] == "U1")
        combined["firmware"] = {
            "projects": [
                {
                    "id": uid(),
                    "key": "axis_fw",
                    "name": "Axis firmware",
                    "source_artifact_id": firmware_artifact,
                    "build_system": "platformio",
                    "board_part_id": mcu["id"],
                    "mcu": "STM32F407",
                    "pin_map": [
                        {"mcu_pin": "PA0", "net_id": nets["STEP"],
                         "board_part_id": mcu["id"], "port_id": "_1", "function": "step"},
                        {"mcu_pin": "PA1", "net_id": nets["DIR"],
                         "board_part_id": mcu["id"], "port_id": "_2", "function": "dir"},
                        {"mcu_pin": "PA2", "net_id": nets["ENABLE"],
                         "board_part_id": mcu["id"], "port_id": "_3", "function": "enable"},
                    ],
                }
            ]
        }

        combined_path.parent.mkdir(parents=True, exist_ok=True)
        combined_path.write_text(json.dumps(combined, indent=2), encoding="utf-8")
        combined_valid = run_cli("validate", str(combined_path))

    print(f"freecad: {freecad_import.stdout.strip()}")
    print(f"freecad validation: {freecad_valid.stdout.strip()}")
    print(f"kicad: {kicad_import.stdout.strip()}")
    print(f"kicad validation: {kicad_valid.stdout.strip()}")
    print(f"firmware pins: {len(combined['firmware']['projects'][0]['pin_map'])}")
    print(f"combined validation: {combined_valid.stdout.strip()}")
    print(f"combined project: {combined_path}")
    print("import integration OK")


if __name__ == "__main__":
    main()
