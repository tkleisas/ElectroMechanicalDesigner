#!/usr/bin/env python3
"""Build the EMD thermal and motor FMUs with OpenModelica."""

import argparse
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent


def build(omc, model, output):
    script = f"""
loadFile("{HERE / (model + '.mo')}");
cd("{output}");
buildModelFMU({model}, version="2.0", fmuType="cs");
"""
    with tempfile.NamedTemporaryFile("w", suffix=".mos", delete=False) as stream:
        stream.write(script)
        script_path = stream.name
    try:
        result = subprocess.run(
            [omc, script_path],
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            raise SystemExit(result.stderr or result.stdout)
    finally:
        Path(script_path).unlink(missing_ok=True)
    fmu = Path(output) / f"{model}.fmu"
    if not fmu.exists():
        raise SystemExit(f"OpenModelica did not create {fmu}")
    return fmu


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--omc", default="omc")
    parser.add_argument("--out", default=str(HERE / "out"))
    args = parser.parse_args()
    output = Path(args.out).resolve()
    output.mkdir(parents=True, exist_ok=True)
    for model in ("MotorThermal", "MotorDrive"):
        print(build(args.omc, model, output))
    for path in output.iterdir():
        if path.is_file() and path.suffix != ".fmu":
            path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
