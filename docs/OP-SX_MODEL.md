# OP-SX — Solvent Extraction Model

## Physics
Counter-current mixer-settler cascade, Kremser closed form:

```
E = D · (O/A)                          (extraction factor)
f = (E^(N+1) − E) / (E^(N+1) − 1)      (fraction extracted)
f = N/(N+1) exactly at E = 1
```

times the Murphree stage efficiency. E < 1 flagged: complete extraction is
unreachable at any stage count (asymptote < 1).

**Golden-rule fix**: `Stages` was an IntegerParameter → REAL integer code.

## Test anchors
- **Exact**: E=2, N=3 → f = 14/15 (Kremser closed form, asserted 1e-10).
- **Exact**: E=1 limit → f = N/(N+1).
- E < 1 warns; exact 4-stream mass balance; determinism; results == streams.

## References
- Kremser, *Natl. Petroleum News* 22 (1930) 43–49.
- Seader, Henley & Roper, *Separation Process Principles*, 3rd ed. (2011), ch. 5.
- Treybal, *Mass-Transfer Operations*, 3rd ed. (1980), ch. 10.
