"""PythonFMU co-simulation wrapper around the MechMaker MCP server.

The FMU is a proxy: it starts `MechMaker.Server`, opens a machine, and exposes
up to four stepper channels. Parameters point at the machine, the server DLL,
and the MechMaker catalog on the host.
"""

import json
import os
import re
import subprocess

from pythonfmu import (
    Boolean,
    DefaultExperiment,
    Fmi2Causality,
    Fmi2Slave,
    Fmi2Variability,
    Integer,
    Real,
    String,
)

CHANNELS = 4
MOTOR_RE = re.compile(
    r"^(?P<id>[^:]+?)(?: \(disabled\))?: commanded "
    r"(?P<commanded>[-+]?[0-9]+(?:[.,][0-9]+)?) rev / actual "
    r"(?P<actual>[-+]?[0-9]+(?:[.,][0-9]+)?) rev"
)


class MechMakerSlave(Fmi2Slave):
    author = "ElectroMechanicalDesigner"
    description = "MechMaker stepper-machine co-simulation proxy"

    def __init__(self, **kwargs):
        super().__init__(**kwargs)

        self.machine_path = ""
        self.server_path = ""
        self.catalog_path = ""
        self.microsteps = 16
        self.carriage_m_per_rev = 0.04
        self.x_carriage_m = 0.0

        for channel in range(1, CHANNELS + 1):
            setattr(self, f"motor{channel}_velocity", 0.0)
            setattr(self, f"motor{channel}_enable", False)
            setattr(self, f"motor{channel}_angle", 0.0)

        self.register_variable(String(
            "machine_path", causality=Fmi2Causality.parameter,
            variability=Fmi2Variability.tunable))
        self.register_variable(String(
            "server_path", causality=Fmi2Causality.parameter,
            variability=Fmi2Variability.tunable))
        self.register_variable(String(
            "catalog_path", causality=Fmi2Causality.parameter,
            variability=Fmi2Variability.tunable))
        self.register_variable(Integer(
            "microsteps", start=16, causality=Fmi2Causality.parameter,
            variability=Fmi2Variability.tunable))
        self.register_variable(Real(
            "carriage_m_per_rev", start=0.04,
            causality=Fmi2Causality.parameter,
            variability=Fmi2Variability.tunable))

        for channel in range(1, CHANNELS + 1):
            self.register_variable(Real(
                f"motor{channel}_velocity", start=0.0,
                causality=Fmi2Causality.input))
            self.register_variable(Boolean(
                f"motor{channel}_enable", start=False,
                causality=Fmi2Causality.input,
                variability=Fmi2Variability.discrete))
            self.register_variable(Real(
                f"motor{channel}_angle",
                causality=Fmi2Causality.output))

        self.register_variable(Real(
            "x_carriage_m", causality=Fmi2Causality.output))

        self.default_experiment = DefaultExperiment(
            start_time=0.0, stop_time=1.0, step_size=0.01)

        self._process = None
        self._next_id = 0
        self._steppers = []
        self._enabled = [False] * CHANNELS
        self._velocity = [0.0] * CHANNELS

    def exit_initialization_mode(self):
        self._start()

    def do_step(self, current_time, step_size):
        if self._process is None:
            self._start()

        for index, instance_id in enumerate(self._steppers):
            enabled = bool(getattr(self, f"motor{index + 1}_enable"))
            velocity = float(getattr(self, f"motor{index + 1}_velocity"))

            if enabled != self._enabled[index]:
                self._call_tool("enable_motor", {
                    "instanceId": instance_id,
                    "enabled": enabled,
                })
                self._enabled[index] = enabled

            if velocity != self._velocity[index]:
                self._call_tool("set_motor_velocity", {
                    "instanceId": instance_id,
                    "revPerSec": velocity,
                })
                self._velocity[index] = velocity

        status = self._parse_status(
            self._call_tool("run_for", {"seconds": float(step_size)}))

        for index, instance_id in enumerate(self._steppers):
            actual = status.get(instance_id)
            if actual is not None:
                setattr(self, f"motor{index + 1}_angle", actual)

        self.x_carriage_m = (
            float(self.motor1_angle) * float(self.carriage_m_per_rev))
        return True

    def terminate(self):
        if self._process is None:
            return

        try:
            self._call_tool("stop_run")
        except Exception:
            pass

        try:
            self._process.terminate()
            self._process.wait(timeout=5)
        except Exception:
            self._process.kill()
        finally:
            self._process = None

    def _start(self):
        if self._process is not None:
            return

        base = self.resources or os.path.dirname(__file__)
        roots = [base, os.path.join(base, "payload")]

        def find(relative):
            for root in roots:
                candidate = os.path.join(root, relative)
                if os.path.exists(candidate):
                    return candidate
            return os.path.join(roots[-1], relative)

        machine = (self.machine_path or "").strip() or find("machine.json")
        server = (self.server_path or "").strip()
        if not server:
            server = find(os.path.join("server", "MechMaker.Server"))
            if not os.path.exists(server):
                server = find(os.path.join("server", "MechMaker.Server.dll"))
        catalog = (self.catalog_path or "").strip() or find("catalog")

        if not os.path.exists(machine):
            raise RuntimeError(f"MechMaker machine not found: {machine}")
        if not os.path.exists(server):
            raise RuntimeError(f"MechMaker server not found: {server}")

        if not server.endswith(".dll") and not os.access(server, os.X_OK):
            os.chmod(server, 0o755)

        environment = os.environ.copy()
        if os.path.isdir(catalog):
            environment["MECHMAKER_CATALOG"] = catalog

        command = ["dotnet", server] if server.endswith(".dll") else [server]
        self._process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            bufsize=1,
            cwd=os.path.dirname(server) or None,
            env=environment,
        )

        self._request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "emd-mechmaker-fmu", "version": "0.1"},
        })
        self._write({"jsonrpc": "2.0", "method": "notifications/initialized"})

        self._call_tool("open_machine", {"path": machine})
        self._call_tool("start_run", {"microsteps": int(self.microsteps)})

        machine = json.loads(self._call_tool("get_machine_json"))
        steppers = []
        for wire in machine.get("wiring", []):
            if wire.get("signal") == "step":
                component = wire.get("component")
                if component and component not in steppers:
                    steppers.append(component)
        self._steppers = steppers[:CHANNELS]

        for index, instance_id in enumerate(self._steppers):
            enabled = bool(getattr(self, f"motor{index + 1}_enable"))
            velocity = float(getattr(self, f"motor{index + 1}_velocity"))
            self._call_tool("enable_motor", {
                "instanceId": instance_id,
                "enabled": enabled,
            })
            self._call_tool("set_motor_velocity", {
                "instanceId": instance_id,
                "revPerSec": velocity,
            })
            self._enabled[index] = enabled
            self._velocity[index] = velocity

    def _call_tool(self, name, arguments=None):
        result = self._request("tools/call", {
            "name": name,
            "arguments": arguments or {},
        })
        if result.get("isError"):
            raise RuntimeError(f"MechMaker tool '{name}' failed")

        content = result.get("content", [])
        return "\n".join(
            item.get("text", "")
            for item in content
            if item.get("type") == "text"
        )

    def _request(self, method, params):
        self._next_id += 1
        request_id = self._next_id
        self._write({
            "jsonrpc": "2.0",
            "id": request_id,
            "method": method,
            "params": params,
        })

        while True:
            line = self._process.stdout.readline()
            if not line:
                raise RuntimeError("MechMaker server closed its stdout")

            message = json.loads(line)
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(message["error"].get("message", "JSON-RPC error"))
            return message.get("result", {})

    def _write(self, message):
        self._process.stdin.write(json.dumps(message) + "\n")
        self._process.stdin.flush()

    @staticmethod
    def _parse_status(text):
        status = {}
        for line in text.splitlines():
            match = MOTOR_RE.match(line.strip())
            if match:
                status[match.group("id")] = float(
                    match.group("actual").replace(",", "."))
        return status
