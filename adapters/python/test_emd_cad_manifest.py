import hashlib
import tempfile
import unittest
from pathlib import Path

from emd_cad_manifest import matrix_to_euler_xyz_deg, sanitize_id, sha256_file


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
