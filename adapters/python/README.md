# Python adapters

## FreeCAD manifest adapter

`freecad_adapter.py` is a starter JSON-RPC adapter. Run it under FreeCAD's
Python (`freecadcmd`) so the `FreeCAD`, `Import`, and `Mesh` modules exist.

```bash
printf '%s' '{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "freecad.export_manifest",
  "params": {
    "inputs": [{ "path": "axis.FCStd", "format": "fcstd" }],
    "parameters": {
      "output_dir": "out",
      "formats": ["step", "stl"]
    }
  }
}' | freecadcmd adapters/python/freecad_adapter.py
```

The response is a JSON-RPC result containing the manifest and exported
artifacts. Feed the manifest to EMD:

```bash
emd import freecad-manifest out/axis.manifest.json --out out/axis.json
emd export mjcf --project out/axis.json --out out/axis.xml
```

### Document conventions

- Every solid object becomes a part with **box** simulation-proxy geometry sized
  from its bounding box.
- Optional custom string properties on an object:
  - `EMD_Id` — stable part key (defaults to the label).
  - `EMD_Category` — one of `structure`, `motor`, `transmission`,
    `linear_motion`, `sensor`, `electronics`, `tool`, `fastener`, `other`.
  - `EMD_MassKg` — override mass.
  - `EMD_Connectors` — JSON array of `{ "name": "...", "type": "...", "pose":
    { "pos": [...], "rot": [...] } }`.
  - `EMD_Material` — JSON object with `name`, `density_kg_m3`,
    `youngs_modulus_pa`, `poisson_ratio`, `thermal_conductivity_w_m_k`.
  - `EMD_Description` — human-readable part description.
  - `EMD_Tags` — JSON array of catalog/search tags.
  - `EMD_Features` — JSON array of `{ "name", "pose", "geometry", "material" }`
    records for per-feature geometry metadata.
  - `EMD_Bom` — JSON object with `manufacturer`, `manufacturer_part_number`,
    `supplier`, `supplier_part_number`, `unit_cost`, `currency`, `quantity`.
- Optional sidecar `axis.emd.json` next to `axis.FCStd`:

```json
{
  "root": "frame",
  "joints": [
    {
      "key": "x_slide",
      "type": "slide",
      "parent": "frame",
      "child": "carriage",
      "axis": [1, 0, 0],
      "range": [-0.2, 0.2],
      "actuator": "x_motor",
      "damping": 0.01
    }
  ]
}
```

The adapter uses bounding boxes for simulation proxies and preserves real
connector frames, materials, BOM data, and feature metadata from the properties.
It cannot infer joints without the sidecar; a dedicated workbench export remains
future work.

## Tests

The pure helpers and the JSON-RPC framing are covered without FreeCAD:

```bash
cd adapters/python
python3 -m unittest discover -s . -p 'test_*.py'
```
