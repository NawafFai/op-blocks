# OP-PEM — PEM Electrolyzer Model

## Physics
```
I     = j · A_cell                       [A per cell]
N_H2  = η_F · I · N_cells / (2F);  N_O2 = N_H2/2;  H2O consumed = N_H2
SEC   = 26.59 · V / η_F                  [kWh/kg H2]   (exact closed form)
eff_HHV = 1.481/V · η_F;   eff_LHV = 1.253/V · η_F
```

26.59 kWh/kg/V = 2F/(3600·0.00201588·1000). Production capped by the water
fed (water-limited warning). **Golden-rule fix**: `CellCount` was an
IntegerParameter → REAL integer code.

## Validity
PEM: j 1–4 A/cm², V 1.7–2.1 V. At 1.9 V / 99 %: SEC ≈ 51 kWh/kg — the
published PEM band (Carmo).

## Test anchors
- Faraday exact: per-cell I = 2F at 100 % → exactly 1 mol/s H₂ per cell.
- SEC closed form exact; SEC at defaults in 45–60 kWh/kg band.
- O₂ = H₂/2 exact; water-limited warning; H/O atom balance across outlets.
- Determinism; results == streams; RealParameter-only.

## References
- Carmo, Fritz, Mergel & Stolten, *Int. J. Hydrogen Energy* 38 (2013) 4901–4934.
- Ursúa, Gandía & Sanchis, *Proc. IEEE* 100 (2012) 410–426.
- Barbir, *Solar Energy* 78 (2005) 661–669.
