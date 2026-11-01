#!/usr/bin/env python3
"""Generate a small parametric EMD catalog with CadQuery.

`--plan` writes the catalog metadata without importing CadQuery. Add
`--formats step stl` to export geometry when CadQuery is installed.
"""

import argparse
import json
from pathlib import Path

PARTS = [
    {
        "id": "emd_standoff_m3",
        "name": "M3 standoff, 10 mm",
        "params": {"diameter_m": 0.006, "length_m": 0.010, "hole_m": 0.0032},
        "assets": ["emd_standoff_m3.step", "emd_standoff_m3.stl"],
    },
    {
        "id": "emd_spacer_2020",
        "name": "2020 spacer, 5 mm",
        "params": {"width_m": 0.020, "length_m": 0.020, "height_m": 0.005},
        "assets": ["emd_spacer_2020.step", "emd_spacer_2020.stl"],
    },
    {
        "id": "emd_belt_clamp",
        "name": "GT2 belt clamp",
        "params": {"width_m": 0.016, "length_m": 0.024, "height_m": 0.008},
        "assets": ["emd_belt_clamp.step", "emd_belt_clamp.stl"],
    },
]


def generate_plan():
    return [
        {
            "id": part["id"],
            "name": part["name"],
            "params": dict(part["params"]),
            "assets": list(part["assets"]),
        }
        for part in PARTS
    ]


def write_plan(directory):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / "catalog.json"
    path.write_text(
        json.dumps({"schema_version": "0.1", "parts": generate_plan()}, indent=2),
        encoding="utf-8",
    )
    return path


def build_with_cadquery(directory, formats):
    import cadquery as cq

    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    for part in PARTS:
        params = part["params"]
        if part["id"] == "emd_standoff_m3":
            shape = (
                cq.Workplane("XY")
                .circle(params["diameter_m"] / 2)
                .extrude(params["length_m"])
                .faces(">Z")
                .workplane()
                .hole(params["hole_m"])
            )
        else:
            shape = cq.Workplane("XY").box(
                params["length_m"], params["width_m"], params["height_m"])
        for fmt in formats:
            cq.exporters.export(shape, str(directory / f"{part['id']}.{fmt}"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="out/catalog")
    parser.add_argument("--plan", action="store_true")
    parser.add_argument("--formats", nargs="+", default=["step", "stl"])
    args = parser.parse_args()

    catalog = write_plan(args.out)
    if args.plan:
        print(f"catalog plan: {catalog}")
        return

    build_with_cadquery(args.out, args.formats)
    print(f"catalog parts: {catalog}")


if __name__ == "__main__":
    main()
