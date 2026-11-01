# CadQuery catalog generator

`generate_parts.py` defines a small parametric catalog for EMD. `--plan` writes
the metadata without CadQuery; with CadQuery installed it also exports STEP/STL
geometry.

```bash
python3 tools/cadquery/generate_parts.py --plan --out out/catalog
python3 tools/cadquery/generate_parts.py --out out/catalog --formats step stl
```
