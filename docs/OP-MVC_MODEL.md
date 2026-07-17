# OP-MVC — Mechanical Vapour Compression Model

## Physics
All-electric evaporation: the generated vapour is compressed (raising its
saturation temperature) and returned as the evaporator's heat source.
Specific compression work for steam over a modest ratio (ideal-gas form):

```
w   = cp_v · T_sat · [ CR^((γ−1)/γ) − 1 ] / η_isentropic
      cp_v = 1.88 kJ/kg·K,  (γ−1)/γ = 0.248 (steam)
P   = w · m_vapour;   SEC = P / Q_distillate       [kWh/m³]
```

The compression ratio must clear the brine's boiling-point elevation or no
temperature driving force remains (advisory at CR < 1.1). Distillate is pure
water; brine `CF = 1/(1 − recovery)`, flagged above 3 (MVC concentrator
practice).

## Validity
CR 1.1–2, recovery 30–60 %, T_evap 50–75 °C → SEC ≈ 8–16 kWh/m³
(published MVC band).

## Test anchors
- `w` formula exact against hand evaluation (CR=1.3, T=333 K, η=0.75 →
  ≈ 56 kJ/kg → SEC ≈ 15.7 kWh/m³, inside the published band).
- SEC in 8–18 kWh/m³ at defaults; CR < 1.1 warns; distillate salt-free.
- Exact mass balance; determinism; results == streams.

## References
- El-Dessouky & Ettouney, *Fundamentals of Salt Water Desalination* (2002), ch. 7.
- Aly & El-Fiqi, *Desalination* 158 (2003) 143–150.
