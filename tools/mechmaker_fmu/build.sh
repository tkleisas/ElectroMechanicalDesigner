#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
python3 -m pythonfmu build -f mechmaker_slave.py --dest .
