# OP-CRYST — Crystallizer Model

## Physics
Solubility-limited yield (Mullin): the mother liquor leaves **saturated** at
the crystallizer temperature.

```
crystals [kg/s] = max(0, m_salt − S/100 · m_water·(1 − EvapFrac))
yield           = crystals / m_salt
```

`S` = solubility [g/100 g water] at T — a user input from solubility tables
(NaCl: 36.0 g/100 g at 25 °C, CRC Handbook). `EvapFrac` models evaporative
crystallization (0 = pure cooling). Undersaturated feed → nothing
crystallizes, with a warning. This replaces the old stub's arbitrary
"Yield %" input with a real solubility balance.

## Test anchors
- **Exact hand case**: 100 g/s water + 50 g/s NaCl at S=36 → 14 g/s crystals.
- Undersaturated feed → 0 crystals + warning.
- Evaporation raises the yield per the closed form.
- Exact mass balance (feed = liquor + crystals + vapour); determinism;
  results == streams.

## References
- Mullin, *Crystallization*, 4th ed. (2001), ch. 3.
- Myerson (ed.), *Handbook of Industrial Crystallization*, 2nd ed. (2002), ch. 1.
- CRC Handbook of Chemistry and Physics, aqueous solubility tables.
