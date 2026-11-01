#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

MECHMAKER_DIR="${MECHMAKER_DIR:-$(cd ../../../MechMaker && pwd)}"
if command -v cygpath >/dev/null 2>&1; then
  MECHMAKER_DIR="$(cygpath -u "$MECHMAKER_DIR")"
fi
PYTHON="${PYTHON:-python3}"
STAGE="direct-fmu"
OUTPUT="MechMakerDirect.fmu"

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    RID="win-x64"
    PLATFORM="win64"
    LIBRARY="MechMakerDirect.dll"
    SOURCE_LIBRARY="MechMaker.Fmu.Direct.dll"
    ;;
  Linux*)
    RID="linux-x64"
    PLATFORM="linux64"
    LIBRARY="MechMakerDirect.so"
    SOURCE_LIBRARY="MechMaker.Fmu.Direct.so"
    ;;
  *)
    echo "Unsupported build host: $(uname -s)" >&2
    exit 1
    ;;
esac

rm -rf "$STAGE" "$OUTPUT"
mkdir -p "$STAGE/binaries/$PLATFORM" "$STAGE/resources/payload/catalog"

echo "Publishing NativeAOT MechMaker FMU for $RID..."
dotnet publish direct/MechMaker.Fmu.Direct.csproj \
  -c Release -r "$RID" --self-contained true \
  -p:MechMakerDir="$MECHMAKER_DIR" \
  -o "$STAGE/binaries/$PLATFORM" >/dev/null

mv "$STAGE/binaries/$PLATFORM/$SOURCE_LIBRARY" \
   "$STAGE/binaries/$PLATFORM/$LIBRARY"
if [ ! -f "$STAGE/binaries/$PLATFORM/$LIBRARY" ]; then
  echo "NativeAOT output not found: $LIBRARY" >&2
  ls -l "$STAGE/binaries/$PLATFORM" >&2
  exit 1
fi
rm -f "$STAGE/binaries/$PLATFORM"/*.dbg "$STAGE/binaries/$PLATFORM"/*.pdb \
  "$STAGE/binaries/$PLATFORM"/*.lib "$STAGE/binaries/$PLATFORM"/*.exp

cp direct/modelDescription.xml "$STAGE/"
cp "$MECHMAKER_DIR/examples/linear_axis_v0.json" "$STAGE/resources/payload/machine.json"
cp "$MECHMAKER_DIR"/catalog/*.json "$STAGE/resources/payload/catalog/"

"$PYTHON" - "$STAGE" "$OUTPUT" <<'PY'
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
