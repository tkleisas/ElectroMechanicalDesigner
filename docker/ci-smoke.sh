#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

renode --version
script=$(mktemp --suffix=.resc)
cat > "$script" <<EOF
mach create
machine LoadPlatformDescription @/opt/renode/platforms/cpus/fomu.repl
quit
EOF
renode --console --disable-gui "$script" >/tmp/renode-smoke.log
grep -q "machine-0: Disposed" /tmp/renode-smoke.log
rm -f "$script"

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
python3 tools/spice_fmu/verify_closed_loop.py
python3 tools/renode/verify_renode.py \
  --renode /usr/local/bin/renode \
  --platform /opt/renode/platforms/cpus/nrf52840.repl
