# OP-FC — PEM Fuel Cell Model

## Physics
```
H2 consumed = utilization · H2 fed, capped by O2 available (2 H2 per O2)
I = 2F · N_H2;    P = V_cell · I
eff_LHV = V_cell / 1.253         (exact; 0.68 V → 54.3 %)
water produced = H2 consumed
```

The exhaust carries depleted air + product water + slip H₂ (single-outlet
balance is exact).

## Validity
PEM under load: V 0.6–0.8 V, utilization 80–95 %.

## Test anchors
- Faraday exact: 1 mol/s H₂ consumed → I = 2F exactly; P = V·I exact.
- eff_LHV closed form exact (0.68 → 54.27 %).
- Air-limited case: H₂ consumption capped at 2·O₂ with warning.
- Exact element balance across the exhaust; determinism; results == streams.

## References
- O'Hayre, Cha, Colella & Prinz, *Fuel Cell Fundamentals*, 3rd ed. (2016), ch. 2.
- Barbir, *PEM Fuel Cells: Theory and Practice*, 2nd ed. (2013).
- Larminie & Dicks, *Fuel Cell Systems Explained*, 2nd ed. (2003).
