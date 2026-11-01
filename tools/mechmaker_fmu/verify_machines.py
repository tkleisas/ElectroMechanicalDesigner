#!/usr/bin/env python3
"""Run the direct FMU against several MechMaker machine fixtures."""

import argparse
from pathlib import Path

from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]

CASES = [
    ("linear_axis_v0", 2, 0.005),
    ("phone_gantry_rig", 3, 0.005),
    ("parametric_gear_pair", 2, 0.001),
    ("leadscrew_z_axis", 1, 0.01),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fmu", default=str(HERE / "MechMakerDirect.fmu"))
    parser.add_argument(
        "--mechmaker-dir",
        default=str(REPO.parent / "MechMaker"),
    )
    parser.add_argument("--stop-time", type=float, default=0.5)
    parser.add_argument("--step-size", type=float, default=0.01)
    args = parser.parse_args()

    fmu = Path(args.fmu).resolve()
    mechmaker = Path(args.mechmaker_dir).resolve()
    catalog = mechmaker / "catalog"
    if not fmu.exists():
        raise SystemExit(f"FMU not found: {fmu}; run ./build_direct.sh")
    if not catalog.is_dir():
        raise SystemExit(f"MechMaker catalog not found: {catalog}")

    for name, minimum_motors, minimum_angle in CASES:
        machine = mechmaker / "examples" / f"{name}.json"
        start_values = {
            "machine_path": str(machine),
            "catalog_path": str(catalog),
            "microsteps": 16,
        }
        for channel in range(1, 4):
            start_values[f"motor{channel}_velocity"] = 0.2
            start_values[f"motor{channel}_enable"] = True

        result = simulate_fmu(
            str(fmu),
            stop_time=args.stop_time,
            step_size=args.step_size,
            start_values=start_values,
            output=["motor1_angle", "motor2_angle", "motor3_angle", "x_carriage_m"],
        )
        final = result[-1]
        angles = [float(final["motor1_angle"]), float(final["motor2_angle"]),
                  float(final["motor3_angle"])]
        moving = sum(abs(angle) >= minimum_angle for angle in angles)
        if moving < minimum_motors:
            raise SystemExit(
                f"{name}: expected {minimum_motors} moving motors, got {angles}")

        print(f"{name}: {angles[0]:+.6f} {angles[1]:+.6f} {angles[2]:+.6f} rev")

    print("machine fixture coverage OK")


if __name__ == "__main__":
    main()
