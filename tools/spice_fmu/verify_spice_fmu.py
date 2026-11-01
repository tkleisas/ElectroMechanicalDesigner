#!/usr/bin/env python3
"""Build a SPICE trace FMU with emd and simulate it with FMPy."""

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ngspice", default="ngspice")
    parser.add_argument("--python", default=sys.executable)
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="emd-spice-fmu-") as directory:
        work = Path(directory)
        netlist = work / "rc.cir"
        netlist.write_text(
            "* RC low pass\n"
            "V1 in 0 PULSE(0 1 0 1u 1u 10m 20m)\n"
            "R1 in out 1k\n"
            "C1 out 0 1u\n",
            encoding="utf-8",
        )
        fmu = work / "rc.fmu"

        subprocess.run(
            [
                "dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
                "spice", "fmu",
                "--netlist", str(netlist),
                "--out", str(fmu),
                "--signals", "v(out)",
                "--stop-time", "0.01",
                "--step-size", "0.0001",
                "--ngspice", args.ngspice,
                "--python", args.python,
            ],
            check=True,
        )

        result = simulate_fmu(
            str(fmu),
            stop_time=0.01,
            step_size=0.0001,
            output=["v_out"],
        )
        final = float(result[-1]["v_out"])
        if final < 0.9:
            raise SystemExit(f"expected RC output near 1 V, got {final}")
        print(f"SPICE FMU output: {final:.6f} V")
        print("SPICE FMU integration OK")


if __name__ == "__main__":
    main()
