# OP-RO — Model & References

Reverse-osmosis reference block for ONE PROCESS Blocks (CAPE-OPEN + DWSIM).
The equations below are implemented once in `OPBlocks.Core.RoModel` (the single
source of truth shared by the block and its validation reference) and are also
printed in the block's own report under **"Model & References"**, so they travel
with every simulation.

All stream thermodynamics — temperature, pressure, densities, molecular weights,
water activity — come from the **user-selected Property Package**. Nothing is
pinned inside the block (spec requirement R4).

## Ports
- **Feed** (inlet) — pressurised feed.
- **Permeate** (outlet) — product water, delivered at 1 atm.
- **Concentrate** (outlet) — brine, at the feed/applied pressure.

## Parameters (inputs)
| Name | Default | Unit | Meaning |
|---|---|---|---|
| CalcMode | Rating | — | `Rating` (area+pressure → performance) or `Design` (target recovery → area+pressure) |
| Area | 40 | m² | Membrane area (Rating input; computed in Design) |
| WaterPermA | 1.0 | L·m⁻²·h⁻¹·bar⁻¹ | Water permeability A (seawater ≈ 1, brackish ≈ 3–8) |
| SaltRejection | 99.5 | % | Intrinsic salt rejection |
| AppliedPressure | 60 | bar | Applied feed pressure (Rating input; computed in Design) |
| VantHoffI | 2 | — | van 't Hoff dissociation factor (2 for NaCl) |
| PumpEff | 80 | % | High-pressure pump efficiency |
| MaxRecovery | 50 | % | Maximum design water recovery (single-stage SWRO ≈ 45–50%) |
| TargetRecovery | 45 | % | Target water recovery (Design mode) |
| DesignFlux | 15 | L·m⁻²·h⁻¹ | Design average permeate flux (Design mode) |
| ERDType | None | — | Energy-recovery device: `None` / `PX` / `Turbine` |
| ERDEff | 96 | % | ERD efficiency (used when ERDType ≠ None; PX ≈ 96%, turbine ≈ 80%) |

Pressure (`bar` → Pa) and area (`m²`) carry CAPE-OPEN dimensionality, so Aspen
offers unit conversion on them; the user may enter any compatible unit and Aspen
converts. Percentages and specialised units (permeability, flux) are shown as
entered.

## Model

**Water flux — solution-diffusion:**

    Jw = A · (ΔP − Δπ)          [L·m⁻²·h⁻¹]

- `A` = water permeability, `ΔP` = applied − permeate (≈ atmospheric) pressure.
- **Δπ uses the average of the feed and concentrate osmotic pressures**,
  `Δπ = ½(π_feed + π_conc)`. Osmotic pressure rises as the module concentrates,
  so `π_avg > π_feed` and recovery self-limits — the behaviour a single-point
  feed-π model misses.

**Osmotic pressure** (per stream, any feed composition):

    π = i · c · R · T                      (van 't Hoff), or
    π = −(R·T / V̄w) · ln(a_w)              when the package supplies a_w

The block prefers the package water activity `a_w` only when it actually models
the solution (`γ_w ≠ 1`); an ideal package reporting `γ_w = 1` carries no
electrolyte information, so the block falls back to van 't Hoff and says so in
the report.

**Recovery** `r = permeate water / feed water`, capped at `MaxRecovery`.

- **Rating mode** solves the implicit coupling `r ↔ π_avg(r)` by a deterministic
  **bisection** on the strictly-monotonic residual `g(r) = r − h(r)` (where
  `h(r)` is the recovery the flux would produce). Bisection always converges to
  machine precision → bit-identical every run.
- **Design mode** inverts the model:

      required area     = Q_perm / DesignFlux
      required pressure = π_avg + DesignFlux / A

**Salt passage:** each non-water component splits with
`fraction to permeate = (1 − SaltRejection) · r`; water splits with `r`. The
material balance closes exactly (feed = permeate + concentrate, component-wise).

**Pump power** — the HP pump raises the whole feed from atmospheric intake to the
applied pressure:

    W_pump = Q_feed · ΔP / η_pump

**Energy recovery** — an optional device returns the high-pressure brine's
hydraulic power:

    W_ERD  = Q_conc · ΔP · η_ERD          (PX ≈ 96%, turbine ≈ 80%)
    W_net  = W_pump − W_ERD
    SEC    = W_pump / Q_perm              (gross)
    SEC_net = W_net / Q_perm
    saving = (W_pump − W_net) / W_pump

Volumetric flows come from the property package (package mass flow ÷ package
density), which is robust to any host mass-unit convention.

## Engineering advisories (non-blocking warnings)
The block never blocks a run; it flags, in the report:
- recovery limited by `MaxRecovery` (area possibly oversized for the feed);
- applied/required pressure above the typical seawater element limit (~82 bar);
- very high recovery → low brine flow → scaling/fouling risk;
- no net driving pressure (applied ≤ average osmotic pressure);
- property-package fallbacks used (ideal activity, missing MW/density).

## Neglected effects (lumped one-parameter model, v1)
Concentration polarisation, membrane pressure drop, temperature/pressure
dependence of `A`, and per-element staging are not resolved; the model is a
module-lumped design/rating tool, not an element-by-element solver.

## References
1. R. W. Baker, *Membrane Technology and Applications*, 3rd ed., Wiley (2012), ch. 5.
2. C. Fritzmann, J. Löwenberg, T. Wintgens, T. Melin, "State-of-the-art of reverse
   osmosis desalination," *Desalination* **216** (2007) 1–76.
3. N. Voutchkov, *Desalination Engineering: Planning and Design*, McGraw-Hill (2013), ch. 8.

## Validation status (2026-07-14, live Aspen Plus V14)
Seawater 35,000 ppm NaCl, 1524 kg/h water feed at 60 bar, IDEAL package:
- Rating: **45.0% recovery**, flux 17.2 LMH, π_feed 30.5 bar, gross SEC 4.57 kWh/m³.
- Rating + PX: **net SEC 2.62 kWh/m³** (43% saving) — within the industrial 2–3 band.
- Design (target 45%): required area 40 m², required pressure ~58–60 bar.
- Results table equals the outlet-stream values; 20 consecutive runs identical.
