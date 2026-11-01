"""Deterministic command source for the MechMaker SSP integration test."""

from pythonfmu import Boolean, Fmi2Causality, Fmi2Slave, Real


class CommandSlave(Fmi2Slave):
    author = "ElectroMechanicalDesigner"
    description = "Constant velocity/enable command for MechMaker co-simulation"

    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.velocity = 0.2
        self.enable = True

        self.register_variable(Real(
            "velocity", causality=Fmi2Causality.output))
        self.register_variable(Boolean(
            "enable", causality=Fmi2Causality.output))

    def do_step(self, current_time, step_size):
        return True
