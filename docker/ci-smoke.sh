#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test EMD.slnx --nologo

(
  cd adapters/python
  python3 -m unittest discover -s . -p 'test_*.py'
)
(
  cd tools/cadquery
  python3 -m unittest discover -s . -p 'test_*.py'
)
python3 tools/spice_fmu/verify_spice_fmu.py
