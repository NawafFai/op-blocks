# OP-AEL — Alkaline Electrolyzer Model

## Physics
Same Faradaic core as [OP-PEM](OP-PEM_MODEL.md) (shared
`ElectrolyzerModel` engine) at alkaline operating ranges:

```
N_H2 = η_F · I · N_cells / (2F);   SEC = 26.59 · V / η_F;   eff_HHV = 1.481/V · η_F
```

KOH concentration is an advisory input (electrolyte conductivity peaks near
30 wt%); KOH and excess water leave with the O₂/anolyte stream.
**Golden-rule fix**: `CellCount` IntegerParameter → REAL integer code.

## Validity
Alkaline: j 0.2–0.6 A/cm², V 1.8–2.2 V → SEC ≈ 48–55 kWh/kg (Ursúa).

## Test anchors
- Shared-engine equivalence: same spec in AEL block == PEM engine output.
- SEC at alkaline defaults inside 45–60 kWh/kg; KOH stays in the anolyte
  stream (mass balance).
- Determinism; results == streams; RealParameter-only.

## References
- Ursúa, Gandía & Sanchis, *Proc. IEEE* 100 (2012) 410–426.
- Carmo, Fritz, Mergel & Stolten, *Int. J. Hydrogen Energy* 38 (2013) 4901–4934.
