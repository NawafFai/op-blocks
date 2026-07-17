# OP-IX — Ion Exchange Column Model

## Physics
Equivalents-based fixed-bed service model (softening duty):

```
load [eq/s]   = Σ (target mol/s) · z · removal
service time  = ResinVolume · Capacity / load
bed volumes   = Q_feed · service_time / V_bed
```

Target ions = **multivalent hardness** (Ca²⁺/Mg²⁺, z=2 default), following
the strong-acid resin selectivity order Ca²⁺ > Mg²⁺ ≫ Na⁺ (Helfferich);
monovalent background ions pass. Removed ions leave with the spent
regenerant — the 4-stream mass balance is exact.

## Validity
SAC softening: capacity 1–1.4 eq/L, removal 90–99 %. Service runs under 8 h
are flagged (excessive regeneration frequency).

## Test anchors
- Service time = capacity/load exact against hand evaluation.
- Selectivity: Ca/Mg removed at the configured removal, Na passes untouched.
- No-hardness feed warns; short service run warns.
- Exact 4-stream mass balance; determinism; results == streams.

## References
- Helfferich, *Ion Exchange*, McGraw-Hill (1962).
- Crittenden et al. (MWH), *Water Treatment: Principles and Design*, 3rd ed. (2012), ch. 16.
