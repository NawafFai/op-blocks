# OP-GAC — Activated Carbon Adsorption Model

## Physics
```
Freundlich:  q0 = K_F · C0^(1/n)      [mg/g]  (q0 = K_F exactly at C0 = 1 mg/L)
Removal:     R  = 1 − e^(−EBCT/τ)
CUR:         (C0 − Ce) / q0           [g carbon / L treated]   (Crittenden)
Bed life:    m_bed / (CUR · Q)
```

Feed concentration C0 computed from the stream (package MW + volume).
Adsorbed contaminant is retained on the bed (no sludge stream — advisory).

## Validity
Trace organics, favourable isotherms (1/n 0.2–0.8), EBCT 5–30 min
(drinking-water practice).

## Test anchors
- `q(C=1) = K_F` exact; CUR closed form exact; removal = 1−e^(−EBCT/τ) exact.
- Short bed life warns; no-contaminant feed warns.
- Water passes untouched; mass balance (feed = treated + retained);
  determinism; results == streams.

## References
- Freundlich, *Z. Phys. Chem.* 57 (1906) 385–470.
- Crittenden et al. (MWH), *Water Treatment*, 3rd ed. (2012), ch. 15.
- Sontheimer, Crittenden & Summers, *Activated Carbon for Water Treatment*, 2nd ed. (1988).
