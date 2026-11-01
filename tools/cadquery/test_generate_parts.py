import json
import tempfile
import unittest
from pathlib import Path

from generate_parts import generate_plan, write_plan


class CatalogPlanTests(unittest.TestCase):
    def test_plan_has_unique_parts_and_assets(self):
        plan = generate_plan()
        ids = [part["id"] for part in plan]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertGreaterEqual(len(plan), 3)
        for part in plan:
            self.assertTrue(part["params"])
            self.assertTrue(part["assets"])

    def test_write_plan_is_valid_json(self):
        with tempfile.TemporaryDirectory() as directory:
            path = write_plan(directory)
            data = json.loads(Path(path).read_text(encoding="utf-8"))
            self.assertEqual("0.1", data["schema_version"])
            self.assertEqual(len(plan_parts()), len(data["parts"]))


def plan_parts():
    return generate_plan()


if __name__ == "__main__":
    unittest.main()
