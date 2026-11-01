import hashlib
import tempfile
import unittest
from pathlib import Path

from types import SimpleNamespace

from emd_cad_manifest import (
    matrix_to_euler_xyz_deg,
    part_metadata,
    read_json_property,
    sanitize_id,
    sha256_file,
)


class SanitizeIdTests(unittest.TestCase):
    def test_preserves_a_valid_key(self):
        self.assertEqual(sanitize_id("frame_2020"), "frame_2020")

    def test_replaces_spaces_and_prefixes_digits(self):
        self.assertEqual(sanitize_id("17 motor plate"), "_17_motor_plate")

    def test_uses_a_fallback_for_empty_values(self):
        self.assertEqual(sanitize_id("   "), "part")


class Sha256Tests(unittest.TestCase):
    def test_hashes_a_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "data.bin"
            path.write_bytes(b"hello manifest")
            self.assertEqual(
                sha256_file(path),
                hashlib.sha256(b"hello manifest").hexdigest(),
            )


class JsonPropertyTests(unittest.TestCase):
    def test_reads_valid_json_and_ignores_invalid(self):
        self.assertEqual({"a": 1}, read_json_property('{"a": 1}'))
        self.assertIsNone(read_json_property("{not json"))
        self.assertIsNone(read_json_property(None))

    def test_part_metadata_reads_rich_properties(self):
        obj = SimpleNamespace(
            EMD_Description="Belt tensioner",
            EMD_Tags='["mechanical", "printed"]',
            EMD_Features='[{"name": "boss", "geometry": {"kind": "cylinder", "size": [0.01, 0.02, 0]}}]',
            EMD_Bom='{"manufacturer": "EMD", "manufacturer_part_number": "BT-01"}',
        )

        metadata = part_metadata(obj)

        self.assertEqual("Belt tensioner", metadata["description"])
        self.assertEqual(["mechanical", "printed"], metadata["tags"])
        self.assertEqual("boss", metadata["features"][0]["name"])
        self.assertEqual("BT-01", metadata["bom"]["manufacturer_part_number"])


class EulerTests(unittest.TestCase):
    def test_identity(self):
        self.assertClose([0, 0, 0], matrix_to_euler_xyz_deg([
            [1, 0, 0],
            [0, 1, 0],
            [0, 0, 1],
        ]))

    def test_rotation_about_z(self):
        self.assertClose([0, 0, 90], matrix_to_euler_xyz_deg([
            [0, -1, 0],
            [1, 0, 0],
            [0, 0, 1],
        ]))

    def test_rotation_about_x(self):
        self.assertClose([90, 0, 0], matrix_to_euler_xyz_deg([
            [1, 0, 0],
            [0, 0, -1],
            [0, 1, 0],
        ]))

    def assertClose(self, expected, actual, tolerance=1e-9):
        self.assertEqual(len(expected), len(actual))
        for left, right in zip(expected, actual):
            self.assertAlmostEqual(left, right, delta=tolerance)


if __name__ == "__main__":
    unittest.main()
