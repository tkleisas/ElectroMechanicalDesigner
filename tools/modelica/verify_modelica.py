#!/usr/bin/env python3
"""Build the OpenModelica FMUs and verify them with FMPy."""

import argparse
import subprocess
import sys
from pathlib import Path

import numpy as np
from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--omc", default="omc")
    parser.add_argument("--out", default=str(HERE / "out"))
    args = parser.parse_args()

    subprocess.run(
        [sys.executable, str(HERE / "build_fmus.py"), "--omc", args.omc, "--out", args.out],
        check=True,
    )
    output = Path(args.out)

    t = np.arange(0, 10.01, 0.1)
    thermal_input = np.array([(time, 10.0) for time in t], dtype=[("time", "f8"), ("lossW", "f8")])
    thermal = simulate_fmu(
        str(output / "MotorThermal.fmu"),
        stop_time=10,
        step_size=0.1,
        input=thermal_input,
        output=["temperatureC"],
    )
    temperature = float(thermal[-1]["temperatureC"])
    if temperature <= 25:
        raise SystemExit(f"thermal FMU did not heat: {temperature}")

    t = np.arange(0, 1.01, 0.01)
    drive_input = np.array([(time, 12.0) for time in t], dtype=[("time", "f8"), ("voltage", "f8")])
    drive = simulate_fmu(
        str(output / "MotorDrive.fmu"),
        stop_time=1,
        step_size=0.01,
        input=drive_input,
        output=["speed"],
    )
    speed = abs(float(drive[-1]["speed"]))
    if speed <= 0:
        raise SystemExit(f"motor FMU did not spin: {speed}")

    print(f"MotorThermal temperature: {temperature:.3f} C")
    print(f"MotorDrive speed: {speed:.3f} rad/s")
    print("OpenModelica FMU integration OK")


if __name__ == "__main__":
    main()
