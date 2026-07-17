# OP-CDI — Capacitive Deionization Model

## Physics
Cycle-averaged electrosorption into porous carbon double layers, with two
independent limits:

```
capacity:  N_cap  = SAC · m_electrode / (MW_salt · t_cycle)     [mol/s]
charge:    N_salt = Λ · Q / F     →    Q = N_salt · F / Λ
energy:    E = Q · V_cell         (charging energy, no recovery — conservative)
SEC = E / Q_product
```

`Λ` (charge efficiency) is the standard CDI figure tying salt to charge
(Porada). Removed salt reports to the regeneration waste stream; water
splits by the recovery.

## Validity
Brackish feeds (< ~3000 ppm), SAC 5–30 mg/g, V_cell 0.8–1.4 V (above ~1.4 V
electrolysis begins). Published brackish SEC ≈ 0.1–1 kWh/m³; the block warns
above 1.5 (CDI loses to RO at higher salinity).

## Test anchors
- Charge–salt inversion exact: `E = N·F·V/Λ` reproduced by the block.
- Removal capped by SAC capacity; feed-limited warning when oversized.
- SEC in the published band for the default brackish case.
- Exact mass balance; determinism; results == streams.

## References
- Porada, Zhao, van der Wal, Presser & Biesheuvel, *Prog. Mater. Sci.* 58 (2013) 1388–1442.
- Suss et al., *Energy Environ. Sci.* 8 (2015) 2296–2319.
