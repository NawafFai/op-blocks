# OP-MED — Multi-Effect Distillation Model

## Physics
N evaporator effects reuse each effect's vapour as the next effect's heat
source. Industrial shortcut (El-Dessouky & Ettouney):

```
GOR      = k · N            (k = GorPerEffect ≈ 0.8–0.9)
Q_steam  = D · λ / GOR
STE      = Q_steam / Q_distillate        [kWh/m³]
```

Distillate `D = recovery · feed water` — salts are non-volatile, so the
distillate is pure water and the brine carries every solute. Brine
concentration factor `CF = 1/(1 − recovery)`, flagged above 2.5 (seawater
scaling practice). Top brine temperature above ~70 °C flagged (CaSO₄/CaCO₃
scale on MED bundles).

## Validity
N 2–16, TBT 55–70 °C, recovery 20–50 %. N=8 → GOR ≈ 6.8, STE ≈ 95 kWh/m³
(published MED band ≈ 60–110 kWh_th/m³ for GOR 6–10).

## Test anchors
- GOR = 0.85·8 = 6.8 exactly at defaults; STE = λ/(3.6·GOR) exactly.
- Distillate stream is salt-free; brine CF matches 1/(1−r); TBT > 70 warns.
- Exact mass balance; determinism; results == streams.

## References
- El-Dessouky & Ettouney, *Fundamentals of Salt Water Desalination* (2002), ch. 8.
- Al-Shammiri & Safar, *Desalination* 126 (1999) 45–59.
