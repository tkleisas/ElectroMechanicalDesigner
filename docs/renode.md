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
