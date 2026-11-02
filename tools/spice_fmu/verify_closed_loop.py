#!/usr/bin/env python3
"""Build and verify a closed-loop SPICE FMU with FMPy."""

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from fmpy.simulation import simulate_fmu

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ngspice", default="ngspice")
    parser.add_argument("--python", default=sys.executable)
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="emd-spice-cl-") as directory:
        work = Path(directory)
        netlist = work / "divider.cir"
        netlist.write_text(
            "* divider\n"
            "V1 in 0 {{vin}}\n"
            "R1 in out 1k\n"
            "R2 out 0 1k\n"
            ".op\n"
            ".print op v(out)\n"
            ".end\n",
            encoding="utf-8",
        )
        fmu = work / "divider.fmu"
        subprocess.run(
            [
                "dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
                "spice", "fmu",
                "--netlist", str(netlist),
                "--out", str(fmu),
                "--signals", "v(out)",
                "--inputs", "vin",
                "--closed-loop",
                "--python", args.python,
            ],
            check=True,
        )

        times = np.arange(0, 0.51, 0.1)
        inputs = np.array([(time, 10.0) for time in times],
                          dtype=[("time", "f8"), ("vin", "f8")])
        result = simulate_fmu(
            str(fmu),
            stop_time=0.5,
            step_size=0.1,
            input=inputs,
            start_values={"ngspice_path": args.ngspice},
            output=["v_out"],
        )
        value = float(result[-1]["v_out"])
        if abs(value - 5.0) > 1e-3:
            raise SystemExit(f"expected divider output 5 V, got {value}")
        print(f"closed-loop SPICE v_out: {value:.6f} V")
        print("closed-loop SPICE integration OK")


if __name__ == "__main__":
    main()
