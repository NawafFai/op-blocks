# OP-UF — Ultrafiltration Model

## Physics
Size-exclusion membrane with **no osmotic barrier**. Pure Darcy flux:

```
Jw = Lp · TMP · FF        [L/m2/h]
```

- `Lp` — membrane permeability (polymeric UF 20–1000 L/m²/h/bar)
- `TMP` — trans-membrane pressure (0.1–5 bar; UF is low-pressure)
- `FF` — fouling derating factor (practical resistance-in-series at fixed fouling state)

**Selectivity**: dissolved salts/ions pass freely (permeate TDS = feed TDS);
macro solutes (proteins, humics, colloids, oil) are rejected with `Rejection`.
Component classification travels with `UfModel.IsDissolvedSalt`.

Recovery `r = permeate water / feed water`, capped at `MaxRecovery`.
Pump: `W = Q_feed · TMP / η`; `SEC = W / Q_permeate`.

## Validity
TMP 0.1–5 bar; Lp 20–1000 L/m²/h/bar; MWCO 1–500 kDa species treated as "macro".

## Test anchors
- `Lp=100, TMP=1, FF=0.7 → Jw = 70 L/m2/h` exactly (Darcy).
- NaCl-only feed → permeate TDS = feed TDS (no osmotic barrier) + advisory warning.
- Protein (macro) rejected at the configured 95%.
- Exact mass balance; determinism (20 runs < 1e-8); results == streams.

## References
- Cheryan, *Ultrafiltration and Microfiltration Handbook*, 2nd ed. (1998), ch. 4.
- Baker, *Membrane Technology and Applications*, 3rd ed. (2012), ch. 6.
- Crittenden et al. (MWH), *Water Treatment: Principles and Design*, 3rd ed. (2012), ch. 12.
