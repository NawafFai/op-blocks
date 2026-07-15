# OP-ED — Model & References

Electrodialysis block for ONE PROCESS Blocks (CAPE-OPEN + DWSIM). The equations
below are implemented once in `OPBlocks.Core.EdModel` (the single source of truth
shared by the block and its validation tests) and are also printed in the block's
own report under **"Model & References"**, so they travel with every simulation.

All stream thermodynamics — temperature, pressure, densities, molecular weights —
come from the **user-selected Property Package**. Nothing is pinned inside the
block (spec requirement R4). This block is deliberately *different physics* from
the membrane blocks — no pressure, no osmotic driving force — to prove the factory
survives an **electrochemical** model.

## Ports
- **DiluateIn** (inlet) / **DiluateOut** (outlet, product) — the stream being
  deionised.
- **ConcentrateIn** (inlet) / **ConcentrateOut** (outlet) — the receiving brine.

## Parameters (inputs)
| Name | Default | Unit | Meaning |
|---|---|---|---|
| CalcMode | 0 (Rating) | — | 0 = Rating (voltage → removal), 1 = Design (target removal → current/voltage) |
| CellPairs | 100 | — | Number of cell pairs (integer, carried as a real) |
| AppliedVoltage | 24 | V | Stack voltage (Rating input; computed in Design) |
| StackResistance | 5 | Ω | Stack electrical resistance |
| CurrentEfficiency | 90 | % | Faradaic (current) efficiency |
| IonValence | 1 | — | Ion valence z (integer, carried as a real) |
| WaterTransport | 8 | mol H₂O/mol | Electro-osmotic water transport number |
| TargetRemoval | 90 | % | Target salt removal (Design mode) |

Every parameter is a `RealParameter` (the only type Aspen's grid renders). Mode,
cell-pair count and valence are integer-valued but carried as real codes — an
`IntegerParameter`/`OptionParameter` would blank the whole Aspen grid.

## Model

**Ion transport — Faraday's law across a cell-pair stack:**

    N_salt = η · I · N_cp / (z · F)          [mol/s]

- `η` = current efficiency, `I` = stack current, `N_cp` = cell pairs,
  `z` = ion valence, `F` = 96485.33 C/mol (Faraday constant).

**Stack current (Rating)** — Ohmic:

    I = AppliedVoltage / StackResistance

**Design mode** inverts Faraday's law for the current that hits a target removal:

    I_req = N_salt,target · z · F / (η · N_cp)
    V_req = I_req · StackResistance

**Water transport** — electro-osmotic drag, `t_w` mol water per mol ion:

    N_water = t_w · N_salt        (capped at 50% of the diluate water)

**Depletion / limiting current** — the salt actually transferred is capped at
`MaxDepletion = 98%` of the ions present in the diluate feed. Beyond the limiting
current density the diluate cannot supply ions fast enough and extra voltage splits
water instead; the block flags this regime rather than transferring salt that isn't
there.

**Salt split** — the transferred salt is apportioned across the diluate's ionic
components in proportion to their feed flows; the material balance closes exactly
(diluate in + concentrate in = diluate out + concentrate out, component-wise).

**Energy:**

    P = V · I              SEC = P / Q_product   (product = diluate-out volume)

Product volumetric flow comes from the property package (package mass ÷ package
density), robust to any host mass-unit convention.

## Engineering advisories (non-blocking warnings)
- diluate has no dissolved species (nothing to transfer);
- limiting-current / depletion regime (applied current exceeds what the diluate
  can supply — stage the stack or lower the current);
- Design mode needs an impractically high stack voltage (>500 V);
- property-package fallbacks used (missing MW).

## Neglected effects (lumped cell-pair model, v1)
Concentration-polarisation boundary layers, back-diffusion of salt, a resistance
that rises as the diluate depletes, per-compartment hydraulics and pH/scaling on
the membranes are not resolved. `CurrentEfficiency`, `StackResistance` and `t_w`
are lumped stack-average constants.

## References
1. H. Strathmann, *Ion-Exchange Membrane Separation Processes*, Membrane Science
   and Technology Series 9, Elsevier (2004).
2. H. Strathmann, "Electrodialysis, a mature technology with a multitude of new
   applications," *Desalination* **264** (2010) 268–288.
3. R. W. Baker, *Membrane Technology and Applications*, 3rd ed., Wiley (2012), ch. 10.

## Validation status
Physics is pinned by `EdCapeOpenValidationTests` (both Thermo 1.0 and 1.1 mock
backends). The exact anchor is **Faraday's law**: at I = F ≈ 96485.33 A, one cell
pair, 100% efficiency, z = 1, exactly 1 mol/s of monovalent ions is transferred
(asserted to 1e-9); at I = 1 A the transfer is 1/F = 1.0364×10⁻⁵ mol/s. Doubling
the valence halves the molar transfer; removal scales with current efficiency; the
depletion cap and its warning fire when the current outruns the available salt;
exact mass balance; 20 consecutive runs identical to 1e-8; results equal the outlet
streams. **Live Aspen V14 GUI acceptance is pending on the owner's machine.**
