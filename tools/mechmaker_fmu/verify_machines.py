#!/usr/bin/env python3
"""Run the direct FMU against several MechMaker machine fixtures."""

import argparse
from pathlib import Path

from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]

CASES = [
    {
        "name": "linear_axis_v0",
        "machine": "examples/linear_axis_v0.json",
        "motors": 2,
        "min_angle": 0.005,
        "start": {"endstop_joint": "j_carriage", "endstop_trigger_m": 1.0},
        "endstop": True,
    },
    {
        "name": "phone_gantry_rig",
        "machine": "examples/phone_gantry_rig.json",
        "motors": 3,
        "min_angle": 0.005,
        "start": {"servo_target": 0.02},
        "servo_target": 0.02,
    },
    {
        "name": "parametric_gear_pair",
        "machine": "examples/parametric_gear_pair.json",
        "motors": 2,
        "min_angle": 0.001,
    },
    {
        "name": "leadscrew_z_axis",
        "machine": "examples/leadscrew_z_axis.json",
        "motors": 1,
        "min_angle": 0.01,
    },
    {
        "name": "aux_fan_heater",
        "machine": "fixtures/aux_fan_heater.json",
        "motors": 0,
        "min_angle": 0.0,
        "start": {"fan_duty": 1.0, "heater_duty": 1.0},
        "fan": True,
        "heater": True,
    },
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

    for case in CASES:
        machine = HERE / case["machine"] if case["machine"].startswith("fixtures/") \
            else mechmaker / case["machine"]
        start_values = {
            "machine_path": str(machine),
            "catalog_path": str(catalog),
            "microsteps": 16,
        }
        for channel in range(1, 4):
            start_values[f"motor{channel}_velocity"] = 0.2
            start_values[f"motor{channel}_enable"] = True
        start_values.update(case.get("start", {}))

        result = simulate_fmu(
            str(fmu),
            stop_time=args.stop_time,
            step_size=args.step_size,
            start_values=start_values,
            output=[
                "motor1_angle", "motor2_angle", "motor3_angle", "x_carriage_m",
                "servo_position", "endstop_triggered", "fan_rpm",
                "heater_temperature_c",
            ],
        )
        final = result[-1]
        angles = [float(final["motor1_angle"]), float(final["motor2_angle"]),
                  float(final["motor3_angle"])]
        moving = sum(abs(angle) >= case["min_angle"] for angle in angles)
        if moving < case["motors"]:
            raise SystemExit(
                f"{case['name']}: expected {case['motors']} moving motors, got {angles}")

        if "servo_target" in case:
            servo = float(final["servo_position"])
            if abs(servo - case["servo_target"]) > 0.005:
                raise SystemExit(
                    f"{case['name']}: servo_position {servo} outside expectation")

        if case.get("endstop") and not bool(final["endstop_triggered"]):
            raise SystemExit(f"{case['name']}: endstop did not trigger")

        if case.get("fan") and float(final["fan_rpm"]) <= 100:
            raise SystemExit(f"{case['name']}: fan did not spin")

        if case.get("heater") and float(final["heater_temperature_c"]) <= 25:
            raise SystemExit(f"{case['name']}: heater did not heat")

        print(
            f"{case['name']}: {angles[0]:+.6f} {angles[1]:+.6f} {angles[2]:+.6f} rev; "
            f"servo={float(final['servo_position']):+.4f}; "
            f"endstop={int(bool(final['endstop_triggered']))}; "
            f"fan={float(final['fan_rpm']):.0f} rpm; "
            f"heater={float(final['heater_temperature_c']):.2f} C")

    print("machine fixture coverage OK")


if __name__ == "__main__":
    main()
