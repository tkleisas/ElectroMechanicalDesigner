#!/usr/bin/env python3
"""Starter FreeCAD manifest adapter for EMD.

Run under FreeCAD's Python (`freecadcmd`) or a wrapper that provides the
`FreeCAD`, `Import`, and `Mesh` modules. It reads one JSON-RPC request from
stdin and writes one JSON-RPC response to stdout.

Convention:
- Each solid object becomes a part with box simulation-proxy geometry.
- Optional custom string properties: EMD_Id, EMD_Category, EMD_MassKg,
  EMD_Connectors (JSON), EMD_Material (JSON).
- Optional sidecar `<document>.emd.json` with {"root": "...", "joints": [...]}.
"""

import json
import os
import sys
from pathlib import Path

from emd_cad_manifest import matrix_to_euler_xyz_deg, sanitize_id, sha256_file


class RpcError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def main():
    try:
        request = json.load(sys.stdin)
    except Exception as exc:
        json.dump(error(None, -32700, f"Parse error: {exc}"), sys.stdout)
        return 0

    request_id = request.get("id")
    try:
        response = {"jsonrpc": "2.0", "id": request_id, "result": handle(request)}
    except RpcError as exc:
        response = error(request_id, exc.code, exc.message)
    except Exception as exc:
        response = error(request_id, -32000, str(exc))

    json.dump(response, sys.stdout)
    return 0


def handle(request):
    if request.get("method") != "freecad.export_manifest":
        raise RpcError(-32601, f"Unknown method: {request.get('method')}")

    params = request.get("params") or {}
    inputs = params.get("inputs") or []
    if not inputs or not inputs[0].get("path"):
        raise RpcError(-32602, "inputs[0].path is required")

    try:
        import FreeCAD
    except Exception as exc:
        raise RpcError(-32001, f"FreeCAD is not available: {exc}") from exc

    source = inputs[0]["path"]
    parameters = params.get("parameters") or {}
    output_dir = Path(parameters.get("output_dir") or "emd-out")
    output_dir.mkdir(parents=True, exist_ok=True)
    formats = parameters.get("formats") or ["step", "stl"]

    document = FreeCAD.openDocument(source)
    parts = []
    for obj in document.Objects:
        shape = getattr(obj, "Shape", None)
        if shape is None or not hasattr(shape, "BoundBox"):
            continue

        bounds = shape.BoundBox
        size = [float(bounds.XLength), float(bounds.YLength), float(bounds.ZLength)]
        if min(size) <= 0:
            continue

        part_id = sanitize_id(
            getattr(obj, "EMD_Id", None) or obj.Label or obj.Name
        )
        placement = obj.Placement
        part = {
            "id": part_id,
            "name": obj.Label or part_id,
            "pose": {
                "pos": [float(placement.Base.x), float(placement.Base.y), float(placement.Base.z)],
                "rot": matrix_to_euler_xyz_deg(rotation_matrix(placement.Rotation)),
            },
            "geometry": {"kind": "box", "size": size},
        }

        category = getattr(obj, "EMD_Category", None)
        if category:
            part["category"] = category

        mass = getattr(obj, "EMD_MassKg", None)
        if mass is not None:
            part["mass_kg"] = float(mass)

        connectors = getattr(obj, "EMD_Connectors", None)
        if connectors:
            try:
                part["connectors"] = json.loads(connectors)
            except json.JSONDecodeError:
                pass

        material = getattr(obj, "EMD_Material", None)
        if material:
            try:
                part["material"] = json.loads(material)
            except json.JSONDecodeError:
                pass

        parts.append(part)

    if not parts:
        raise RpcError(-32002, "No solid objects with positive bounds were found")

    root, joints = read_sidecar(Path(source))
    stem = Path(source).stem
    artifacts = []
    diagnostics = []

    if "step" in formats:
        step_path = output_dir / f"{stem}.step"
        try:
            import Import

            Import.export(document.Objects, str(step_path))
            artifacts.append(artifact(step_path, "step"))
        except Exception as exc:
            diagnostics.append(warning(f"STEP export failed: {exc}"))

    if "stl" in formats:
        stl_path = output_dir / f"{stem}.stl"
        try:
            import Mesh

            Mesh.export(document.Objects, str(stl_path))
            artifacts.append(artifact(stl_path, "stl"))
        except Exception as exc:
            diagnostics.append(warning(f"STL export failed: {exc}"))

    manifest = {
        "schema_version": "0.1",
        "source": Path(source).name,
        "units": "m",
        "root": root or parts[0]["id"],
        "parts": parts,
        "joints": joints,
        "artifacts": [item for item in artifacts if item is not None],
    }

    manifest_path = Path(
        parameters.get("manifest_path") or output_dir / f"{stem}.manifest.json"
    )
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    artifacts.append(artifact(manifest_path, "cad-manifest"))

    return {
        "success": True,
        "artifacts": [item for item in artifacts if item is not None],
        "diagnostics": diagnostics,
        "data": {
            "manifest_path": str(manifest_path),
            "part_count": len(parts),
        },
    }


def rotation_matrix(rotation):
    matrix = rotation.toMatrix()
    return [
        [matrix.A11, matrix.A12, matrix.A13],
        [matrix.A21, matrix.A22, matrix.A23],
        [matrix.A31, matrix.A32, matrix.A33],
    ]


def read_sidecar(source):
    sidecar = source.with_suffix(".emd.json")
    if not sidecar.exists():
        return None, []

    data = json.loads(sidecar.read_text(encoding="utf-8"))
    return data.get("root"), data.get("joints") or []


def artifact(path, fmt, role="generated"):
    path = Path(path)
    if not path.exists():
        return None
    return {
        "path": str(path),
        "format": fmt,
        "role": role,
        "sha256": sha256_file(path),
        "bytes": path.stat().st_size,
    }


def warning(message):
    return {"code": "EMD-FREECAD-001", "severity": "warning", "message": message}


def error(request_id, code, message):
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "error": {"code": code, "message": message},
    }


if __name__ == "__main__":
    sys.exit(main())
