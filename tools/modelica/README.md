# OpenModelica FMUs

`MotorThermal.mo` and `MotorDrive.mo` are small co-simulation models built with
OpenModelica.

```bash
python3 tools/modelica/build_fmus.py --out tools/modelica/out
python3 tools/modelica/verify_modelica.py --out tools/modelica/out
```

`verify_modelica.py` exercises both FMUs with FMPy: the thermal model heats
under load and the motor model spins under applied voltage.
