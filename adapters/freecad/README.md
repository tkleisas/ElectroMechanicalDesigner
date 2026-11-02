# FreeCAD EMD workbench

The `EMD` addon adds an **EMD → Export EMD manifest** command. It exports the
active document as a cad-manifest plus STEP/STL artifacts, including the
`EMD_*` properties documented in `adapters/python/README.md`.

Install for the current user:

```bash
mkdir -p ~/.local/share/FreeCAD/Mod
ln -sfn "$PWD/adapters/freecad/EMD" ~/.local/share/FreeCAD/Mod/EMD
```

Restart FreeCAD and select the **EMD** workbench.
