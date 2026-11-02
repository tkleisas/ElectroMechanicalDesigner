#!/usr/bin/env python3
"""Drive MechMaker from a UART command stream and assert motor motion."""

import argparse
import csv
import subprocess
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mechmaker", required=True)
    parser.add_argument("--machine", required=True)
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="emd-bridge-") as directory:
        work = Path(directory)
        uart = work / "uart.txt"
        trace = work / "trace.csv"
        uart.write_text(
            "MOTOR motor_left 0.2\n"
            "RUN 0.5\n"
            "STOP motor_left\n",
            encoding="utf-8",
        )
        subprocess.run(
            [
                "dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
                "renode", "bridge",
                "--machine", args.machine,
                "--mechmaker", args.mechmaker,
                "--uart-file", str(uart),
                "--out", str(trace),
            ],
            check=True,
        )
        with trace.open(newline="", encoding="utf-8") as stream:
            rows = list(csv.DictReader(stream))
        if not rows:
            raise SystemExit("bridge produced no trace rows")
        angle = abs(float(rows[-1]["motor_left_angle"]))
        if angle < 0.01:
            raise SystemExit(f"bridge did not move the motor: {angle}")
        print(f"bridge motor_left_angle: {angle:.6f} rev")
        print("Renode bridge integration OK")


if __name__ == "__main__":
    main()
