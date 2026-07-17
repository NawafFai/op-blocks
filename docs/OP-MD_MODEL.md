# OP-MD — Membrane Distillation (DCMD) Model

## Physics
Water **vapour** crosses a hydrophobic microporous membrane, driven by the
vapour-pressure difference between hot brine and cold permeate:

```
J  = Bm · (a_w · Psat(T_hot) − Psat(T_cold))     [kg/m²/s]
Bm = K · ε · d_pore / (τ · δ)                    [kg/m²/s/Pa]
```

Knudsen/molecular-diffusion scaling (Schofield form): porosity `ε` and pore
diameter help; tortuosity `τ` and thickness `δ` hurt; `K` lumps gas-phase
diffusivity and temperature polarization. Non-volatile solutes cannot
evaporate → **complete salt rejection by mechanism**; the brine's water
activity `a_w` lowers the hot-side driving pressure. Psat via Antoine
(`ProcessOps.PsatWaterPa`). Latent duty `= J·A·λ` (λ ≈ 2333 kJ/kg at 60 °C).

## Validity
T_hot 40–85 °C, T_cold 15–35 °C, pores 0.1–1 µm, DCMD flux ≈ 5–60 LMH.

## Test anchors
- Psat(60 °C) ≈ 19.95 kPa, Psat(20 °C) ≈ 2.34 kPa (Antoine, ±1 %).
- Zero flux when T_hot = T_cold and a_w = 1 (exact); flux > 0 when hot > cold.
- Lower a_w → lower flux (brine effect); Bm formula exact.
- Only water crosses (salt stays on the hot side); exact mass balance;
  determinism; results == streams.

## References
- Schofield, Fane & Fell, *J. Membr. Sci.* 33 (1987) 299–313.
- Lawson & Lloyd, *J. Membr. Sci.* 124 (1997) 1–25.
- Khayet, *Adv. Colloid Interface Sci.* 164 (2011) 56–88.
