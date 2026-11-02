#!/usr/bin/env python3
"""Exercise the FreeCAD EMD workbench export with a real FreeCAD document."""

import argparse
import json
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freecadcmd", default="freecadcmd")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="emd-freecad-") as directory:
        work = Path(directory)
        harness = work / "harness.py"
        harness.write_text(f"""
import sys
sys.path.insert(0, {str(REPO / "adapters/freecad")!r})
import FreeCAD
from EMD.emd_export import build_manifest

out = {str(work / "out")!r}
doc = FreeCAD.newDocument("EMDTest")
box = doc.addObject("Part::Box", "Frame")
box.Length = 40
box.Width = 20
box.Height = 10
for name, value in [
    ("EMD_Id", "frame"),
    ("EMD_Category", "structure"),
    ("EMD_Description", "Test frame"),
    ("EMD_Tags", '["frame", "test"]'),
    ("EMD_Connectors", '[{{"name": "motor_mount", "type": "bolt_m3", "pose": {{"pos": [0, 0, 0.005]}}}}]'),
    ("EMD_Bom", '{{"manufacturer": "EMD", "manufacturer_part_number": "FR-1"}}'),
]:
    box.addProperty("App::PropertyString", name)
    setattr(box, name, value)
doc.recompute()
print("MANIFEST=" + str(build_manifest(doc, out)))
""", encoding="utf-8")

        run = subprocess.run(
            [args.freecadcmd, str(harness)],
            check=True,
            capture_output=True,
            text=True,
        )
        manifest_line = next(
            (line for line in run.stdout.splitlines() if line.startswith("MANIFEST=")),
            None)
        if manifest_line is None:
            raise SystemExit(f"FreeCAD did not emit a manifest:\n{run.stdout}\n{run.stderr}")
        manifest = Path(manifest_line.split("=", 1)[1])

        project = work / "project.json"
        subprocess.run(
            ["dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
             "import", "freecad-manifest", str(manifest), "--out", str(project)],
            check=True,
            stdout=subprocess.DEVNULL,
        )
        subprocess.run(
            ["dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
             "validate", str(project)],
            check=True,
            stdout=subprocess.DEVNULL,
        )
        data = json.loads(project.read_text(encoding="utf-8"))
        part = data["parts"][0]
        size = part["geometry"]["size"]
        if abs(size[0] - 0.04) > 1e-6 or abs(size[1] - 0.02) > 1e-6:
            raise SystemExit(f"unexpected part size: {size}")
        if part["metadata"]["bom"]["manufacturer_part_number"] != "FR-1":
            raise SystemExit("BOM metadata was not preserved")
        print(f"FreeCAD manifest: {manifest}")
        print(f"part size: {size}")
        print("FreeCAD workbench integration OK")


if __name__ == "__main__":
    main()
