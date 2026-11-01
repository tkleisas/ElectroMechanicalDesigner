#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

MECHMAKER_DIR="${MECHMAKER_DIR:-$(cd ../../../MechMaker && pwd)}"
STAGE="direct-fmu"
OUTPUT="MechMakerDirect.fmu"

rm -rf "$STAGE" "$OUTPUT"
mkdir -p "$STAGE/binaries/linux64" "$STAGE/resources/payload/catalog"

echo "Publishing NativeAOT MechMaker FMU..."
dotnet publish direct/MechMaker.Fmu.Direct.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:MechMakerDir="$MECHMAKER_DIR" \
  -o "$STAGE/binaries/linux64" >/dev/null

mv "$STAGE/binaries/linux64/MechMaker.Fmu.Direct.so" \
   "$STAGE/binaries/linux64/MechMakerDirect.so"
rm -f "$STAGE/binaries/linux64"/*.dbg "$STAGE/binaries/linux64"/*.pdb

cp direct/modelDescription.xml "$STAGE/"
cp "$MECHMAKER_DIR/examples/linear_axis_v0.json" "$STAGE/resources/payload/machine.json"
cp "$MECHMAKER_DIR"/catalog/*.json "$STAGE/resources/payload/catalog/"

python3 - "$STAGE" "$OUTPUT" <<'PY'
import os
import sys
import zipfile
from pathlib import Path

stage, output = Path(sys.argv[1]), Path(sys.argv[2])
files = sorted(path for path in stage.rglob("*") if path.is_file())
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for path in files:
        info = zipfile.ZipInfo(path.relative_to(stage).as_posix(), (1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        archive.writestr(info, path.read_bytes())
PY

rm -rf "$STAGE"
echo "Built $(pwd)/$OUTPUT"
