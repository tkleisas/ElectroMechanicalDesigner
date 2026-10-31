import json
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


class FreeCadAdapterProtocolTests(unittest.TestCase):
    def run_adapter(self, payload):
        return subprocess.run(
            [sys.executable, str(HERE / "freecad_adapter.py")],
            input=payload,
            text=True,
            capture_output=True,
            cwd=HERE,
            check=False,
        )

    def test_unknown_method_returns_jsonrpc_error(self):
        result = self.run_adapter(json.dumps({
            "jsonrpc": "2.0",
            "id": "1",
            "method": "freecad.nope",
            "params": {},
        }))

        response = json.loads(result.stdout)
        self.assertEqual("1", response["id"])
        self.assertEqual(-32601, response["error"]["code"])

    def test_malformed_json_returns_parse_error(self):
        result = self.run_adapter("{ not json")

        response = json.loads(result.stdout)
        self.assertEqual(-32700, response["error"]["code"])

    def test_missing_input_returns_invalid_params_before_freecad(self):
        result = self.run_adapter(json.dumps({
            "jsonrpc": "2.0",
            "id": "2",
            "method": "freecad.export_manifest",
            "params": {},
        }))

        response = json.loads(result.stdout)
        self.assertEqual(-32602, response["error"]["code"])


if __name__ == "__main__":
    unittest.main()
