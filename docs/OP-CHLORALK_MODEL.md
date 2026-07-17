# OP-CHLORALK — Chlor-Alkali Membrane Cell Model

## Physics
Membrane-cell brine electrolysis (Faradaic):

```
anode:    2 Cl⁻ → Cl₂ + 2 e⁻
cathode:  2 H₂O + 2 e⁻ → H₂ + 2 OH⁻
N_Cl2 = η · I / (2F);   N_H2 = N_Cl2;   N_NaOH = 2·N_Cl2
```

2 NaCl consumed per Cl₂; Na⁺ crosses the cation membrane with electro-osmotic
water drag (`WaterTransport` mol H₂O per mol Na⁺). Production is capped by
the NaCl actually fed (brine-limited warning). `SEC = V·I / m_Cl2`.

## Validity
Membrane cells: V ≈ 3.0–3.3 V, current efficiency 94–97 %. At the defaults
(3.1 V, 96 %) SEC = **2.44 kWh/kg Cl₂**, inside the published band
2.3–2.7 kWh/kg Cl₂ (≈ 2100–2400 kWh/t NaOH).

## Test anchors
- Faraday exact: I = 2F A at 100 % → exactly 1 mol/s Cl₂, 1 mol/s H₂, 2 mol/s NaOH.
- SEC at defaults = 2.44 ± 1 % (hand evaluation), inside 2.3–2.7.
- Brine-limited case: production capped by feed NaCl/2 with warning.
- 5-stream exact mass balance on Na and Cl atoms via species bookkeeping;
  determinism; results == streams.

## References
- O'Brien, Bommaraju & Hine, *Handbook of Chlor-Alkali Technology* (2005), vol. I ch. 2.
- Schmittinger (ed.), *Chlorine: Principles and Industrial Practice* (2000).
