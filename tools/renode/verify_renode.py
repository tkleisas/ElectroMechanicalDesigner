#!/usr/bin/env python3
"""Run a pinned Zephyr ELF in Renode and assert UART evidence."""

import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ELF_URL = (
    "https://dl.antmicro.com/projects/renode/"
    "renode-nrf52840-zephyr_shell_module.elf-gf8d05cf-s_1310072-"
    "c00fbffd6b65c6238877c4fe52e8228c2a38bf1f"
)
ELF_SHA256 = "596906f502af17463c02c57e5a6ba526d4b0ea29e68463d5a046e1edda5a19cb"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--renode", default="renode")
    parser.add_argument(
        "--platform",
        default="/opt/renode/platforms/cpus/nrf52840.repl")
    args = parser.parse_args()

    renode = Path(args.renode).resolve()
    if not renode.exists():
        raise SystemExit(f"Renode not found: {renode}")
    platform = Path(args.platform)
    if not platform.exists():
        raise SystemExit(f"Renode platform not found: {platform}")

    with tempfile.TemporaryDirectory(prefix="emd-renode-") as directory:
        work = Path(directory)
        elf = work / "zephyr.elf"
        urllib.request.urlretrieve(ELF_URL, elf)
        digest = hashlib.sha256(elf.read_bytes()).hexdigest()
        if digest != ELF_SHA256:
            raise SystemExit(f"unexpected Renode demo ELF hash: {digest}")

        project = json.loads((REPO / "examples/project.json").read_text(encoding="utf-8"))
        for artifact in project["artifacts"]:
            if artifact["id"] == "00000000-0000-4000-8000-000000000016":
                artifact["path"] = str(elf)
        for firmware in project["firmware"]["projects"]:
            firmware["renode_platform"] = str(platform)
        project_path = work / "project.json"
        project_path.write_text(json.dumps(project, indent=2), encoding="utf-8")

        log = work / "renode.log"
        events = work / "uart.json"
        env = os.environ.copy()
        env["PATH"] = str(renode.parent) + os.pathsep + env.get("PATH", "")
        subprocess.run(
            [
                "dotnet", "run", "--project", str(REPO / "src/EMD.Cli"), "--",
                "renode", "run",
                "--project", str(project_path),
                "--out", str(log),
                "--events", str(events),
                "--uart", "uart0",
                "--duration", "2",
            ],
            check=True,
            env=env,
        )

        data = json.loads(events.read_text(encoding="utf-8"))
        if not any("uart:~$" in event for event in data):
            raise SystemExit(
                f"no UART shell prompt in Renode events: {data[:3]}")
        print(f"Renode UART events: {len(data)}")
        print("Renode integration OK")


if __name__ == "__main__":
    main()
