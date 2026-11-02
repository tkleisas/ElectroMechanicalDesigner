# Renode setup

The deterministic CI image includes Renode 1.17.0. For a local install without
root, download the portable release and symlink the launcher:

```bash
mkdir -p ~/.local/opt ~/.local/bin
curl -L -o /tmp/renode.tar.gz \
  https://github.com/renode/renode/releases/download/v1.17.0/renode-1.17.0.linux-portable.tar.gz
tar -xzf /tmp/renode.tar.gz -C ~/.local/opt
ln -sfn ~/.local/opt/renode_1.17.0-portable/renode ~/.local/bin/renode
renode --version
```

If `~/.local/bin` is not on `PATH`, either add it or invoke
`~/.local/bin/renode` directly.

`emd renode run` executes project firmware headlessly and writes the UART/log
stream to the requested output file. `--events` writes parsed UART messages as
JSON.

Docker CI also downloads a pinned Zephyr ELF and asserts the Renode UART shell
prompt, so the adapter is covered against a real emulator, not only fakes.

## UART ↔ MechMaker bridge

Generate a Renode socket script:

```bash
emd renode bridge-script --firmware axis.elf --platform board.repl \
  --out bridge.resc --uart uart0 --port 3456
```

Run Renode with that script, then connect the bridge:

```bash
emd renode bridge --machine machine.json --mechmaker MechMaker.Server \
  --endpoint 127.0.0.1:3456 --out bridge-trace.csv
```

Firmware commands are newline-delimited: `MOTOR <id> <rev/s>`, `ENABLE <id>
<0|1>`, `RUN <seconds>`, `STOP <id>`. The same endpoint seam is the HIL path.
