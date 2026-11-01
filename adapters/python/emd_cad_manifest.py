"""Pure helpers shared by CAD adapters and their tests.

This module deliberately does not import FreeCAD, so it can be tested without a
CAD installation.
"""

import hashlib
import json
import math
import re


def sanitize_id(value, fallback="part"):
    """Return a key that matches the EMD key pattern."""
    key = re.sub(r"[^A-Za-z0-9_.-]+", "_", (value or "").strip()).strip("_")
    if not key:
        key = fallback
    if not (key[0].isalpha() or key[0] == "_"):
        key = "_" + key
    return key


def sha256_file(path, chunk_size=1024 * 1024):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(chunk_size), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json_property(value):
    """Parse an optional FreeCAD JSON property; invalid JSON is ignored."""
    if not value:
        return None
    try:
        return json.loads(value)
    except (TypeError, json.JSONDecodeError):
        return None


def part_metadata(obj):
    """Read the richer EMD_* properties from a FreeCAD-like object."""
    metadata = {}
    description = getattr(obj, "EMD_Description", None)
    if description:
        metadata["description"] = str(description)
    tags = read_json_property(getattr(obj, "EMD_Tags", None))
    if isinstance(tags, list):
        metadata["tags"] = [str(tag) for tag in tags]
    features = read_json_property(getattr(obj, "EMD_Features", None))
    if isinstance(features, list):
        metadata["features"] = features
    bom = read_json_property(getattr(obj, "EMD_Bom", None))
    if isinstance(bom, dict):
        metadata["bom"] = bom
    return metadata


def matrix_to_euler_xyz_deg(matrix):
    """Convert a row-major 3x3 rotation matrix to extrinsic XYZ degrees."""
    m20 = matrix[2][0]
    y = math.asin(max(-1.0, min(1.0, -m20)))

    if abs(m20) < 0.9999999:
        x = math.atan2(matrix[2][1], matrix[2][2])
        z = math.atan2(matrix[1][0], matrix[0][0])
    else:
        x = math.atan2(-matrix[1][2], matrix[1][1])
        z = 0.0

    return [math.degrees(x), math.degrees(y), math.degrees(z)]
