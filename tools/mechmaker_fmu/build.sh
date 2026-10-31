#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

MECHMAKER_DIR="${MECHMAKER_DIR:-$(cd ../../../MechMaker && pwd)}"
PAYLOAD="payload"

if ! python3 -c "import pythonfmu" >/dev/null 2>&1; then
  echo "pythonfmu is required: python3 -m pip install pythonfmu" >&2
  exit 1
fi

rm -rf "$PAYLOAD" resources
mkdir -p "$PAYLOAD"

echo "Publishing self-contained MechMaker server..."
dotnet publish "$MECHMAKER_DIR/src/MechMaker.Server/MechMaker.Server.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$PAYLOAD/server" >/dev/null

rm -rf "$PAYLOAD/server/x86" "$PAYLOAD/server"/*.pdb

mkdir -p "$PAYLOAD/catalog"
cp "$MECHMAKER_DIR"/catalog/*.json "$PAYLOAD/catalog/"
cp "$MECHMAKER_DIR/examples/linear_axis_v0.json" "$PAYLOAD/machine.json"

echo "Building FMU..."
python3 -m pythonfmu build -f mechmaker_slave.py --dest . "$PAYLOAD" >/dev/null

echo "Built $(pwd)/MechMakerSlave.fmu"
