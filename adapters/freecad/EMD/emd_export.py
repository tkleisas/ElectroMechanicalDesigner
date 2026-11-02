"""FreeCAD workbench export helpers for EMD."""

import json
import math
import re
from pathlib import Path


def _sanitize(value, fallback="part"):
    key = re.sub(r"[^A-Za-z0-9_.-]+", "_", (value or "").strip()).strip("_")
    if not key:
        key = fallback
    if not (key[0].isalpha() or key[0] == "_"):
        key = "_" + key
    return key


def _euler(matrix):
    m20 = matrix.A31
    y = math.asin(max(-1.0, min(1.0, -m20)))
    if abs(m20) < 0.9999999:
        x = math.atan2(matrix.A32, matrix.A33)
        z = math.atan2(matrix.A21, matrix.A11)
    else:
        x = math.atan2(-matrix.A23, matrix.A22)
        z = 0.0
    return [math.degrees(x), math.degrees(y), math.degrees(z)]


def _pose(value):
    return {
        "pos": [float(value.Base.x) / 1000, float(value.Base.y) / 1000,
                float(value.Base.z) / 1000],
        "rot": _euler(value.Rotation.toMatrix()),
    }


def _json_property(obj, name, expected):
    raw = getattr(obj, name, None)
    if not raw:
        return None
    try:
        value = json.loads(raw)
    except (TypeError, json.JSONDecodeError):
        return None
    return value if isinstance(value, expected) else None


def build_manifest(document, output_dir, formats=("step", "stl"), source_name=None):
    """Export document geometry and return the manifest path."""
    import FreeCAD
    import Import
    import Mesh

    output = Path(output_dir).resolve()
    output.mkdir(parents=True, exist_ok=True)
    parts = []
    for obj in document.Objects:
        shape = getattr(obj, "Shape", None)
        if shape is None or not hasattr(shape, "BoundBox"):
            continue
        bounds = shape.BoundBox
        size = [float(bounds.XLength) / 1000, float(bounds.YLength) / 1000,
                float(bounds.ZLength) / 1000]
        if min(size) <= 0:
            continue

        part = {
            "id": _sanitize(getattr(obj, "EMD_Id", None) or obj.Label or obj.Name),
            "name": obj.Label or obj.Name,
            "pose": _pose(obj.Placement),
            "geometry": {"kind": "box", "size": size},
        }
        category = getattr(obj, "EMD_Category", None)
        if category:
            part["category"] = str(category)
        mass = getattr(obj, "EMD_MassKg", None)
        if mass is not None:
            part["mass_kg"] = float(mass)
        connectors = _json_property(obj, "EMD_Connectors", list)
        if connectors:
            part["connectors"] = connectors
        material = _json_property(obj, "EMD_Material", dict)
        if material:
            part["material"] = material
        description = getattr(obj, "EMD_Description", None)
        if description:
            part["description"] = str(description)
        tags = _json_property(obj, "EMD_Tags", list)
        if tags:
            part["tags"] = [str(tag) for tag in tags]
        features = _json_property(obj, "EMD_Features", list)
        if features:
            part["features"] = features
        bom = _json_property(obj, "EMD_Bom", dict)
        if bom:
            part["bom"] = bom
        parts.append(part)

    if not parts:
        raise RuntimeError("No solid objects with positive bounds were found")

    stem = Path(source_name or document.FileName or document.Name).stem or "cad"
    artifacts = []
    if "step" in formats:
        step = output / f"{stem}.step"
        Import.export(document.Objects, str(step))
        artifacts.append({"path": str(step), "format": "step", "role": "generated"})
    if "stl" in formats:
        stl = output / f"{stem}.stl"
        Mesh.export(document.Objects, str(stl))
        artifacts.append({"path": str(stl), "format": "stl", "role": "generated"})

    manifest = {
        "schema_version": "0.1",
        "source": Path(source_name or document.FileName or document.Name).name,
        "units": "m",
        "root": parts[0]["id"],
        "parts": parts,
        "joints": [],
        "artifacts": artifacts,
    }
    manifest_path = output / f"{stem}.manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest_path


def export_active_document(output_dir=None):
    """GUI command helper; asks for a directory when none is supplied."""
    import FreeCAD
    if FreeCAD.ActiveDocument is None:
        raise RuntimeError("Open a FreeCAD document first")
    if output_dir is None:
        from PySide import QtGui
        output_dir = QtGui.QFileDialog.getExistingDirectory(
            None, "Export EMD manifest")
    if not output_dir:
        return None
    return build_manifest(FreeCAD.ActiveDocument, output_dir)
