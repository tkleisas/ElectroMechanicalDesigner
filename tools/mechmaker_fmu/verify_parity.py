#!/usr/bin/env python3
"""Compare the direct and proxy MechMaker FMUs on the machine fixtures."""

import argparse
from pathlib import Path

from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]

CASES = [
    ("linear_axis_v0", 2),
    ("phone_gantry_rig", 3),
    ("parametric_gear_pair", 2),
    ("leadscrew_z_axis", 1),
]

OUTPUTS = ["motor1_angle", "motor2_angle", "motor3_angle", "motor4_angle"]


def simulate(fmu, machine, catalog, stop_time, step_size):
    start_values = {
        "machine_path": str(machine),
        "catalog_path": str(catalog),
        "microsteps": 16,
    }
    for channel in range(1, 5):
        start_values[f"motor{channel}_velocity"] = 0.2
        start_values[f"motor{channel}_enable"] = True

    result = simulate_fmu(
        str(fmu),
        stop_time=stop_time,
        step_size=step_size,
        start_values=start_values,
        output=OUTPUTS,
    )
    return [float(result[-1][name]) for name in OUTPUTS]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--direct-fmu", default=str(HERE / "MechMakerDirect.fmu"))
    parser.add_argument("--proxy-fmu", default=str(HERE / "MechMakerSlave.fmu"))
    parser.add_argument("--mechmaker-dir", default=str(REPO.parent / "MechMaker"))
    parser.add_argument("--stop-time", type=float, default=0.5)
    parser.add_argument("--step-size", type=float, default=0.01)
    parser.add_argument("--tolerance", type=float, default=0.002)
    args = parser.parse_args()

    direct = Path(args.direct_fmu).resolve()
    proxy = Path(args.proxy_fmu).resolve()
    catalog = Path(args.mechmaker_dir).resolve() / "catalog"
    if not direct.exists() or not proxy.exists():
        raise SystemExit("both direct and proxy FMUs are required")

    for name, channels in CASES:
        machine = Path(args.mechmaker_dir).resolve() / "examples" / f"{name}.json"
        direct_angles = simulate(direct, machine, catalog, args.stop_time, args.step_size)
        proxy_angles = simulate(proxy, machine, catalog, args.stop_time, args.step_size)

        for index in range(channels):
            delta = abs(direct_angles[index] - proxy_angles[index])
            if delta > args.tolerance:
                raise SystemExit(
                    f"{name}: motor{index + 1} direct={direct_angles[index]:.6f} "
                    f"proxy={proxy_angles[index]:.6f} delta={delta:.6f}")

        deltas = [
            abs(direct_angles[index] - proxy_angles[index])
            for index in range(channels)
        ]
        print(
            f"{name}: max delta {max(deltas):.6f} rev "
            f"(direct {direct_angles[0]:+.6f}, proxy {proxy_angles[0]:+.6f})")

    print("direct/proxy parity OK")


if __name__ == "__main__":
    main()
