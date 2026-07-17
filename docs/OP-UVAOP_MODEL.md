# OP-UVAOP — UV / Advanced Oxidation Model

## Physics
First-order UV dose-response (collimated-beam kinetics) with the empirical
UV/H₂O₂ hydroxyl-radical enhancement:

```
D_eff       = D · (UVT/100) · (1 + H2O2/20)
ln(C/C0)    = −k · D_eff
log removal = k · D_eff / ln(10)
EEO         = P_lamp / (Q · log_removal)     [kWh/m³/order]  (Bolton, IUPAC)
```

EEO is exact by definition and pinned by test. Destroyed contaminant is
mineralised (advisory — no sludge stream).

## Validity
Trace organics, UVT > 60 % (pre-filtration advisory below), AOP doses
500–2000 mJ/cm². Published UV/H₂O₂ EEO: ~0.1–2.5 kWh/m³/order (warned above).

## Test anchors
- log removal = k·D_eff/ln 10 exact — identical to −log₁₀(1−destroyed).
- EEO definition exact; H₂O₂ boost closed form (20 mg/L doubles D_eff).
- Water passes untouched; determinism; results == streams.

## References
- Bolton, Bircher, Tumas & Tolman, *Pure Appl. Chem.* 73 (2001) 627–637.
- Oppenländer, *Photochemical Purification of Water and Air* (2003).
