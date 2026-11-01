#!/usr/bin/env python3
import argparse

from fmpy.simulation import simulate_fmu


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fmu", default="MechMakerSlave.fmu")
    parser.add_argument("--machine", default="")
    parser.add_argument("--server", default="")
    parser.add_argument("--catalog", default="")
    parser.add_argument("--velocity", type=float, default=1.0)
    parser.add_argument("--stop-time", type=float, default=0.1)
    parser.add_argument("--step-size", type=float, default=0.01)
    args = parser.parse_args()

    start_values = {
        "microsteps": 16,
        "motor1_velocity": args.velocity,
        "motor1_enable": True,
    }
    if args.machine:
        start_values["machine_path"] = args.machine
    if args.server:
        start_values["server_path"] = args.server
    if args.catalog:
        start_values["catalog_path"] = args.catalog

    result = simulate_fmu(
        args.fmu,
        stop_time=args.stop_time,
        step_size=args.step_size,
        start_values=start_values,
        output=["motor1_angle", "motor2_angle", "motor1_velocity", "x_carriage_m"],
    )

    for row in result:
        print(row)


if __name__ == "__main__":
    main()
