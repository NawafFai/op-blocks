# OP-RPB — Rotating Packed Bed (HiGee) Model

## Physics
Centrifugal-field intensified absorption: hundreds of g shear the liquid
into films/droplets, raising k_L·a by 10–100× over a static column.

```
NTU     = k_cal · √RPM         (a ∝ ω²; Chen's k_La correlations reduce
                                to this at fixed flows)
removal = 1 − e^(−NTU)         (plug-flow transfer units)
P_rotor = c · RPM²             (indicative windage/acceleration)
```

Absorbs CO₂ (or the most abundant non-water gas solute, with a warning)
into the liquid stream.

## Test anchors
- NTU = k·√RPM exact; removal closed form exact; rotor power ∝ RPM² exact.
- Higher RPM → higher removal (monotonic).
- Absorbed solute moves gas → liquid; exact 4-stream mass balance;
  determinism; results == streams.

## References
- Ramshaw & Mallinson, US Patent 4,283,255 (1981).
- Chen, Lin & Liu, *Ind. Eng. Chem. Res.* 44 (2005) 7868–7875.
