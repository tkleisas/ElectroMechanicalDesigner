# SPICE source FMU

`emd spice fmu` runs an ngspice transient analysis and emits a deterministic
FMI 2.0 FMU whose outputs interpolate the captured vectors. This is a source
FMU for system co-simulation; it does not currently feed FMU inputs back into
ngspice.

```bash
emd spice fmu --netlist rc.cir --out rc.fmu \
  --signals v(out),i(v1) --stop-time 0.01 --step-size 0.0001
python3 tools/spice_fmu/verify_spice_fmu.py
```

Requires `ngspice` and `pythonfmu`.
