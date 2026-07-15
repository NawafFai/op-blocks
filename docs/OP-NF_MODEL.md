# OP-NF — Model & References

Nanofiltration block for ONE PROCESS Blocks (CAPE-OPEN + DWSIM). The equations
below are implemented once in `OPBlocks.Core.NfModel` (the single source of truth
shared by the block and its validation tests) and are also printed in the block's
own report under **"Model & References"**, so they travel with every simulation.

All stream thermodynamics — temperature, pressure, densities, molecular weights,
water activity — come from the **user-selected Property Package**. Nothing is
pinned inside the block (spec requirement R4).

## What makes NF *not* RO
Nanofiltration is a "leaky", **selective** membrane sitting between RO and UF:
- **Multivalent ions** (Mg²⁺, Ca²⁺, SO₄²⁻) are rejected almost completely;
  **monovalent ions** (Na⁺, Cl⁻, K⁺) pass substantially. The block therefore
  carries *two* rejections and classifies each solute by its component id.
- Because solute partially permeates, the membrane does not feel the full osmotic
  pressure. The driving force is scaled by a **reflection coefficient σ**
  (Spiegler–Kedem); σ = 1 recovers the RO solution-diffusion limit.

## Ports
- **Feed** (inlet).
- **Concentrate** (outlet) — reject, at the applied pressure.
- **Permeate** (outlet) — product, delivered at 1 atm.

## Parameters (inputs)
| Name | Default | Unit | Meaning |
|---|---|---|---|
| CalcMode | 0 (Rating) | — | 0 = Rating (area+pressure → performance), 1 = Design (target recovery → area+pressure) |
| Area | 40 | m² | Membrane area (Rating input; computed in Design) |
| WaterPermA | 8 | L·m⁻²·h⁻¹·bar⁻¹ | Water permeability A (NF ≈ 5–15) |
| MultivalRejection | 97 | % | Rejection of multivalent ions (Mg, Ca, SO₄) |
| MonovalRejection | 50 | % | Rejection of monovalent ions (Na, Cl, K) |
| AppliedPressure | 10 | bar | Applied feed pressure (Rating input; computed in Design) |
| ReflectionSigma | 0.95 | — | Reflection coefficient σ (Spiegler–Kedem; 1 = RO limit) |
| VantHoffI | 2 | — | van 't Hoff dissociation factor |
| PumpEff | 80 | % | Feed pump efficiency |
| MaxRecovery | 80 | % | Maximum design water recovery (brackish NF ≈ 80–90%) |
| TargetRecovery | 75 | % | Target recovery (Design mode) |
| DesignFlux | 40 | L·m⁻²·h⁻¹ | Design average permeate flux (Design mode) |

Every parameter is a `RealParameter` (the only type Aspen's grid renders — see the
factory rule). Mode is an integer code (0/1) carried as a real number.

## Model

**Water flux — solution-diffusion with reflection coefficient (Spiegler–Kedem):**

    Jw = A · (ΔP − σ · Δπ_avg)          [L·m⁻²·h⁻¹]

- `A` = water permeability, `ΔP` = applied − permeate (≈ atmospheric) pressure.
- `σ` = reflection coefficient. σ = 1 → full osmotic barrier (RO limit);
  σ < 1 → the leaky membrane feels only part of Δπ.
- `Δπ_avg = ½(π_feed + π_conc)` — average osmotic pressure over the module, so π
  rises with recovery and the module self-limits.

**Osmotic pressure** (per stream, any feed composition):

    π = i · c · R · T                      (van 't Hoff), or
    π = −(R·T / V̄w) · ln(a_w)              when the package supplies a_w

The block prefers the package water activity only when it actually models the
solution (`γ_w ≠ 1`); an ideal package reporting `γ_w = 1` carries no electrolyte
information, so the block falls back to van 't Hoff and says so in the report.

**Selective salt passage** — each solute `i` splits to permeate with

    fraction_i = (1 − Rejection_i) · r ,   Rejection_i = MultivalRejection if i is
                 multivalent (Mg/Ca/SO₄…) else MonovalRejection

water splits with the recovery `r` itself. The material balance closes exactly
(feed = permeate + concentrate, component-wise).

- **Rating mode** solves the implicit `r ↔ π_avg(r)` coupling by a deterministic
  **bisection** on the strictly-monotonic residual → bit-identical every run.
- **Design mode** inverts the model:

      required area     = Q_perm / DesignFlux
      required pressure = σ · π_avg + DesignFlux / A

**Feed-pump power** (NF is low-pressure; no energy-recovery device):

    W_pump = Q_feed · ΔP / η_pump          SEC = W_pump / Q_perm

Volumetric flows come from the property package (package mass ÷ package density),
robust to any host mass-unit convention.

## Engineering advisories (non-blocking warnings)
- no net driving pressure (applied ≤ σ·avg-osmotic);
- recovery limited by `MaxRecovery` (area possibly oversized);
- applied/required pressure above the typical NF element limit (~41 bar);
- very high recovery → low reject flow → CaSO₄/CaCO₃ scaling risk;
- no multivalent ion recognised in the feed (NF's selectivity advantage vanishes);
- property-package fallbacks used (ideal activity, missing MW/density).

## Neglected effects (lumped one-parameter model, v1)
Concentration polarisation, Donnan/dielectric exclusion detail, membrane pressure
drop, temperature/pressure dependence of `A`, and per-element staging are not
resolved. σ and the two rejections are lumped module-average constants, not a
Donnan-steric pore model.

## References
1. O. Kedem, A. Katchalsky, "Thermodynamic analysis of the permeability of
   biological membranes to non-electrolytes," *Biochim. Biophys. Acta* **27**
   (1958) 229–246.
2. K. S. Spiegler, O. Kedem, "Thermodynamics of hyperfiltration (reverse osmosis):
   criteria for efficient membranes," *Desalination* **1** (1966) 311–326.
3. A. W. Mohammad et al., "Nanofiltration membranes review: recent advances and
   future prospects," *Desalination* **356** (2015) 226–254.
4. R. W. Baker, *Membrane Technology and Applications*, 3rd ed., Wiley (2012), ch. 5.

## Validation status
Physics is pinned by `NfCapeOpenValidationTests` (both Thermo 1.0 and 1.1 mock
backends): feed osmotic pressure matches the van 't Hoff textbook value for
brackish water (~2.5 bar at ~3000 ppm NaCl, the ≈0.8 bar/1000 ppm rule of thumb);
multivalent ions permeate far less than monovalent (selectivity); σ < 1 raises the
net driving pressure relative to the RO limit; exact mass balance; 20 consecutive
runs identical to 1e-8; results table equals the outlet-stream values.
**Live Aspen V14 GUI acceptance is pending on the owner's machine** (this session
could not run Aspen — see HANDOFF §manual-verification).
