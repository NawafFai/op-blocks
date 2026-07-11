# ONE PROCESS Custom Blocks — Technical Specification v1.0

**Project codename:** OP-Blocks
**Owner:** Nawaf — ONE PROCESS Simulation
**Executor:** Claude Code (Opus 4.8 / Fable 5)
**Date:** 2026-07-10
**Status:** Ready for implementation handoff

---

## 1. Product Definition

A Windows desktop product consisting of:

1. **OP-Blocks Manager (`OPBlocksManager.exe`)** — a GUI installer/manager that:
   - Detects installed Aspen Plus versions (via Windows Registry) and DWSIM installations.
   - Lists the OP-Blocks library with icons, descriptions, and validation status.
   - Registers/unregisters selected blocks as CAPE-OPEN Unit Operations (COM registration, requires Administrator elevation via UAC manifest).
   - Runs a built-in **Self-Test** per block (see §9) and shows PASS/FAIL before the user ever opens Aspen.

2. **OP-Blocks Library (`OPBlocks.Core.dll` + one DLL per block family)** — CAPE-OPEN 1.1 compliant Unit Operations written in **C# (.NET Framework 4.8, x86 + x64 builds)** using a CAPE-OPEN .NET interop layer.

After registration, blocks appear **natively inside the simulator's own palette**:
- **Aspen Plus:** Model Palette → *Customize / CAPE-OPEN* section → category **"ONE PROCESS"**. Drag-and-drop onto the flowsheet like any built-in block.
- **DWSIM:** Object Palette → CAPE-OPEN Unit Operation → ONE PROCESS category.

**Explicit non-goal for v1.0:** Aspen HYSYS support. HYSYS does not host CAPE-OPEN Unit Operations the way Aspen Plus does; HYSYS requires its own Extension (EDF) pathway. This is deferred to v2.0 and must NOT be attempted in v1.0.

---

## 2. Hard Requirements (from owner — implement literally)

| # | Requirement | Implementation interpretation |
|---|---|---|
| R1 | Many blocks | 10 blocks in v1.0, listed in §6, phased in 3 milestones |
| R2 | Ready icons bound to each block | Icon pipeline in §7. SVG master → ICO/BMP per host requirements. Manager auto-installs Aspen palette icons where the host allows it |
| R3 | Drag to flowsheet with zero errors | Robustness contract in §8: every CAPE-OPEN interface method wrapped, no unhandled exceptions ever cross the COM boundary, graceful `ECapeUser` errors with human-readable messages, and the Self-Test suite (§9) must pass 100% before a block is allowed to register |
| R4 | Thermodynamics correct and consistent with the selected components + property package | Thermo delegation contract in §5: blocks NEVER hardcode properties. All enthalpy, density, fugacity, VLE calls go through the host's Material Object (CAPE-OPEN Thermo 1.1) so results always match the user's chosen Property Package and component list |

---

## 3. Architecture

```
OPBlocks/
├── src/
│   ├── OPBlocks.Core/               # Shared infrastructure
│   │   ├── CapeOpen/                # Interop: ICapeUnit, ICapeUtilities,
│   │   │   ├── Interfaces.cs        #   ICapeThermoMaterialObject (thermo 1.1),
│   │   │   ├── ComRegistration.cs   #   CATID registration (CapeUnitOperation_CATID)
│   │   │   └── ErrorHandling.cs     #   ECapeUser / ECapeUnknown implementations
│   │   ├── UnitBase.cs              # Abstract base: ports, parameters, validate/calculate lifecycle
│   │   ├── ThermoProxy.cs           # ALL property calls funnel through here (§5)
│   │   ├── Solvers/                 # Newton-Raphson, Brent, RK4, tridiagonal (TDMA)
│   │   └── Reporting.cs             # Calculation report string exposed to host
│   ├── OPBlocks.Water/              # Blocks 1–5 (§6)
│   ├── OPBlocks.Lithium/            # Block 6
│   ├── OPBlocks.Energy/             # Blocks 7–8
│   ├── OPBlocks.Electro/            # Blocks 9–10
│   └── OPBlocksManager/             # WPF manager app (.NET 8, self-contained publish)
│       ├── RegistryScanner.cs       # Detect Aspen Plus V11–V15, DWSIM
│       ├── BlockRegistrar.cs        # regasm-equivalent via RegistrationServices, x86+x64 hives
│       ├── IconInstaller.cs         # §7
│       └── SelfTestRunner.cs        # §9, runs against COCO/COFE headless if present
├── icons/                           # SVG masters + generated .ico/.bmp
├── tests/
│   ├── UnitTests/                   # per-block math vs. reference data (§9)
│   └── HostTests/                   # COM instantiation, port negotiation, save/restore
├── installer/                       # Inno Setup script → OPBlocks_Setup.exe
└── docs/                            # per-block model documentation (equations + refs)
```

**Key decisions (do not change):**
- Language: **C#**. Reason: first-class COM interop, deterministic registration, single-file deployment. No Python at runtime.
- CAPE-OPEN version: **1.1** interfaces (Unit + Thermo 1.1), with Thermo 1.0 fallback shims because Aspen Plus exposes Material Objects through version-dependent interfaces.
- Both **x86 and x64** DLLs are built and registered; Aspen Plus versions differ in bitness and a missing bitness registration is the #1 cause of "block not in palette" failures.
- Persistence: implement `IPersistStream` so blocks save/reload inside `.apw/.bkp` and `.dwxmz` files without data loss. A block that drops its parameters on file reopen counts as a §8 violation.

---

## 4. CAPE-OPEN Compliance Contract

Every block implements, minimum:

- `ICapeIdentification` — name/description shown in palette.
- `ICapeUnit` — `Validate()`, `Calculate()`, `ports`, `ValStatus`.
- `ICapeUtilities` — `Initialize()`, `Terminate()`, `parameters`, `Edit()` (GUI), simulation context.
- `ICapeUnitReport` — human-readable results report (Aspen shows this under block results).
- Port collection: `ICapeCollection` of `ICapeUnitPort` (material ports; energy ports where noted in §6).
- Parameter collection: `ICapeCollection` of typed parameters (`ICapeRealParameter`, `ICapeIntegerParameter`, `ICapeOptionParameter`) with bounds, defaults, and units metadata.
- Error interfaces: every thrown error implements `ECapeUser` with `.description` set to an actionable message, e.g. *"Feed stream must contain LiCl. Add lithium chloride to the component list."* Never let a raw .NET exception propagate.
- Registration: class registered under CATID `{678C09A5-7D66-11D2-A67D-00105A42887F}` (CapeUnitOperation) plus `CapeDescription` registry keys (Name, Description, CapeVersion, VendorURL = oneprocess.sim placeholder, About).

**`Edit()` GUI:** WinForms dialog per block (works inside Aspen's process). Layout: parameters grid with units, a schematic image of the block, and a "Restore Defaults" button. No WPF inside the host process (loader conflicts in older Aspen versions).

---

## 5. Thermodynamic Delegation Contract (Requirement R4)

This section is the core of correctness. **Blocks must not contain any hardcoded pure-component property, activity model, or EOS.** All of the following go through `ThermoProxy`, which wraps the host Material Object:

| Need | Material Object call |
|---|---|
| Stream enthalpy / entropy | `CalcProp("enthalpy"/"entropy", phase, "mixture")` |
| Density, MW | `CalcProp("density")`, `GetComponentConstant("molecularWeight")` |
| VLE / bubble / dew | `CalcEquilibrium(flashType)` — TP, PH, PVF flashes as needed |
| Vapor pressure, activity | via equilibrium calls, never Antoine hardcoded |
| Component identity | `ComponentIds` matched by CAS number when available, name fallback |

Rules:
1. **Energy balances** close using host enthalpies: `Q = Σ(ṁ·h)_out − Σ(ṁ·h)_in`. Never a hardcoded Cp.
2. **Outlet stream setting order** (Aspen is strict): set composition → set T,P (or P,H) → call flash on the outlet Material Object → only then release it. Violating this order causes silent zero-flow outlets.
3. Where a block needs a property the package cannot supply (e.g., adsorption isotherm constants, membrane permeability), it is a **block parameter with a literature default**, clearly separated in the GUI under "Model Parameters (block-specific)" vs everything thermodynamic which is delegated.
4. `Validate()` must check the component list and fail with a clear `ECapeUser` message if required species are absent (e.g., water for evaporation blocks, Li⁺ source for DLE).
5. Electrolyte-sensitive blocks (§6: DLE, ED, chlor-alkali, CDI) must detect whether the package is electrolyte-capable (ELECNRTL/ENRTL-RK in Aspen, electrolyte packages in DWSIM). If not, run in "apparent-component mode" with a warning line in the report — not an error (R3), but the report must state the assumption.

---

## 6. Block Library v1.0 (10 blocks)

Each block below ships with: icon, Edit GUI, model doc page (equations + 2+ literature refs), self-test dataset, and report template. Model fidelity target: **rating-quality steady-state models**, validated against published data to within stated tolerance.

### Milestone A — Water & Desalination (Blocks 1–5)

**B1. Solar Evaporation Pond (`OP-EVAPPOND`)**
- Ports: 1 brine feed, 1 concentrated brine out, 1 vapor loss out (to atmosphere pseudo-stream), optional precipitated solids out.
- Model: area-based evaporation flux `E = A·(a + b·v_wind)·(p_w,surf − p_w,air)/P` (Dalton-type), with activity-corrected surface vapor pressure pulled from the property package (water activity in brine). Solar/climate inputs as parameters: irradiance, air T, RH, wind speed, pond area, depth.
- Outputs: evaporation rate, concentration factor, days-to-saturation estimate, solids onset flag.
- Validation ref: Class-A pan correlations + published Dead Sea / SQM pond data; tolerance ±15% on evaporation rate.

**B2. Membrane Distillation Module — DCMD (`OP-MD`)**
- Ports: hot feed in/out, cold permeate in/out (4 material ports).
- Model: 1-D counter-current discretization (N=30 cells). Flux `J = B_m·(p_hot,m − p_cold,m)`; membrane coefficient from Knudsen-molecular combined model (pore dia, porosity, tortuosity, thickness as parameters). Temperature polarization via film coefficients (Nu correlations). Energy balance per cell through host enthalpies.
- Outputs: permeate flux (LMH), GOR, thermal efficiency, outlet temperatures.
- Validation: Alkhudhiri/Khayet published DCMD datasets; ±10% flux.

**B3. Forward Osmosis Module (`OP-FO`)**
- Ports: feed in/out, draw solution in/out.
- Model: `J_w = A·(σ·Δπ_eff − ΔP)` with internal/external concentration polarization (ICP via K = t·τ/(D·ε)); osmotic pressures from van 't Hoff with activity correction when the package supplies it, else ideal with report note. Reverse solute flux `J_s = B·ΔC`.
- Validation: HTI/Toray membrane published A, B, S values; ±10% water flux.

**B4. Electrodialysis Stack (`OP-ED`)**
- Ports: diluate in/out, concentrate in/out. Energy port: electrical duty.
- Model: cell-pair model. Current from applied voltage and stack resistance (membrane + solution resistances, solution conductivity from concentration correlation parameterized per salt); Faraday transport `Ṅ = ξ·I·N_cp/(z·F)`; limiting current density check with warning; water transport by osmosis+electroosmosis terms.
- Outputs: outlet concentrations, specific energy consumption (kWh/m³), current efficiency.
- Validation: standard NaCl ED literature (Strathmann); ±10% on SEC.

**B5. Capacitive Deionization (`OP-CDI`)**
- Ports: feed in, product out, waste (regeneration) out. Electrical energy port.
- Model: cycle-averaged steady-state representation of a batch process: salt adsorption capacity (SAC, mg/g) and charge efficiency as parameters; water recovery from cycle timing parameters; average product/waste concentrations by mass balance.
- Outputs: product TDS, SEC, water recovery.
- Validation: published mSAC/SEC ranges (Suss et al. review); sanity-band check.

### Milestone B — Lithium & Energy (Blocks 6–8)

**B6. DLE Adsorption Column (`OP-DLE`)** — flagship block, port of owner's existing ACM v5.3 framework to C#.
- Ports: brine feed, treated brine out, eluate out (cycle-averaged), wash water in.
- Model: Langmuir isotherm `q = q_max·K·C/(1+K·C)` with LDF kinetics `dq/dt = k_LDF·(q* − q)`; column as N=20 CSTRs in series, integrated to cyclic steady state internally (RK4 from Core.Solvers), then exposed as steady-state cycle-averaged streams. Parameters: q_max, K, k_LDF, bed dims, cycle times, sorbent density — defaults from the owner's v5.3 model (to be supplied as `dle_defaults.json` by owner; use placeholder literature values LiCl/Al-LDH until then).
- Outputs: Li recovery %, eluate Li concentration, Mg/Li selectivity, productivity (kg Li/m³ sorbent/day).
- Validation: against owner's ACM v5.3 results (primary) + published Al-LDH sorbent data; ±5% vs ACM.

**B7. Rotating Packed Bed Absorber — HiGee (`OP-RPB`)**
- Ports: gas in/out, liquid in/out.
- Model: NTU/HTU with centrifugal-field mass transfer correlation (Chen et al. `k_L a` correlation, rotor speed a parameter); equilibrium line from host flash calls at stage conditions (this is where R4 matters: CO₂/MEA equilibrium comes from the package, e.g. ELECNRTL).
- Outputs: removal %, HTU, rotor power estimate.
- Validation: published CO₂-MEA RPB pilot data; ±15% removal.

**B8. PEM Electrolyzer (`OP-PEM`)**
- Ports: water feed, H₂ product, O₂ product. Electrical energy port.
- Model: polarization curve `V_cell = V_rev(T,P) + η_act(i) + η_ohm(i) + η_conc(i)` (Butler-Volmer activation, membrane ohmic with conductivity(T, hydration)); Faraday's law production with 99% Faradaic efficiency default; V_rev from Nernst using host-supplied or standard Gibbs data (documented exception to §5 rule 3: ΔG° of water splitting as internal constant, since property packages don't expose electrode potentials — state this in the report).
- Outputs: H₂ rate, cell voltage, stack efficiency (LHV), specific energy (kWh/kg H₂).
- Validation: published PEM polarization curves (e.g., Carmo et al.); ±5% on cell voltage at reference conditions.

### Milestone C — Electrochemical & Advanced (Blocks 9–10)

**B9. Chlor-Alkali Membrane Cell (`OP-CHLORALK`)**
- Ports: brine feed, depleted brine out, catholyte (NaOH) out, Cl₂ out, H₂ out. Electrical port.
- Model: Faradaic production at specified current with current efficiency parameter; cell voltage = E_rev + overpotentials + ohmic (k-factor model); water transport number across membrane; caustic strength by balance.
- Validation: industrial membrane cell benchmarks (~2.4–3.4 kA/m², ~3.0–3.5 V); band check ±10%.

**B10. UV/AOP Reactor (`OP-UVAOP`)**
- Ports: liquid in/out.
- Model: UV dose–response `C/C₀ = exp(−k·D)` per target contaminant (up to 5 user-mapped components), UV transmittance and reactor hydraulics (F-factor) as parameters; H₂O₂ dosing option with EEO reporting.
- Outputs: log removal per component, EEO (kWh/m³/order), lamp power.
- Validation: published EEO tables (Bolton); band check.

**Component mapping rule (all blocks):** each block's Edit GUI includes a "Component Mapping" tab where the user assigns roles (e.g., "Lithium species → LICL", "Target contaminant 1 → PHENOL") from the flowsheet's actual component list. Mappings persist with the file. `Validate()` fails clearly if unmapped.

---

## 7. Icon Pipeline (Requirement R2)

Master art: one **SVG per block** in a consistent ONE PROCESS style — 2px stroke, ISO-10628-inspired symbol language, brand accent color `#0E7C66`, transparent background.

**Icon quality acceptance criteria (owner requirement — "advanced, clear, professional"):**
- Each icon must be **instantly distinguishable at 32×32 px** (palette size) — no two blocks may share a silhouette.
- Must render crisply at 16/32/64/256 px with no anti-aliasing artifacts (test render at each size in CI).
- Symbol must communicate the unit's function without reading the label (e.g., DLE column shows packed bed layers + Li marker; FO shows membrane with opposing flux arrows; evaporation pond shows liquid surface + vapor wisps).
- Consistent visual grammar across all 10: same stroke weight, same port-stub positions matching actual CAPE-OPEN port layout, same accent-color usage.
- Owner approves the first 3 SVGs before the remaining 7 are drawn (style gate).
- **Priority rule: R3 > R2 is absolute.** Icon installation failure at any point degrades silently to the host's generic CAPE-OPEN symbol with a log entry — never a dialog, never a registration failure, never a flowsheet error.

Deliverables generated by build script (`icons/build_icons.ps1` using ImageMagick/rsvg):

| Target | Format | Use |
|---|---|---|
| Manager app | 256px PNG | library gallery |
| DWSIM | PNG (DWSIM renders CO units with the image the unit exposes where supported) | palette + flowsheet |
| Aspen Plus flowsheet | BMP icon set generated per block | see honesty note below |
| Edit GUI | embedded PNG schematic | parameter dialog header |

**Honest constraint (do not hide from user):** Aspen Plus renders CAPE-OPEN units on the flowsheet with its generic CAPE-OPEN icon by default. Custom per-block icons inside Aspen's palette require installing icon definitions via Aspen's icon files/user model library mechanism, which is version-dependent and partially undocumented. Implementation order:
1. Ship all blocks with correct icons in **Manager + DWSIM + Edit GUI** (fully controllable — guaranteed).
2. Implement Aspen icon injection targeting **V14 specifically** via the user icon library path; feature-flag it (`--aspen-icons`), verify in HostTests on V14, and fall back silently to the generic CAPE-OPEN symbol if the version check fails. A failed icon install must NEVER block registration or cause flowsheet errors (R3 > R2).

---

## 8. Zero-Error Robustness Contract (Requirement R3)

Definition of done for R3 — all must hold on Aspen Plus V14 (owner's installed version — primary target) and DWSIM latest stable:

1. Drag block from palette → drops on flowsheet with no dialog, no crash, no COM error.
2. Connect/disconnect streams in any order → no error.
3. Open Edit GUI before connecting streams → GUI opens with defaults, no null-reference.
4. Run with missing/invalid spec → `Validate()` returns false with a clear message in the host's status pane; run is blocked gracefully, never a crash.
5. Run with valid spec → converges; if the internal solver fails, block reports `ECapeSolvingError` with the last residual and suggested fix, and outlet streams are left flash-consistent at feed conditions (never NaN, never zero-flow garbage).
6. Save file → close host → reopen → all parameters and mappings intact, re-run reproduces identical results to 1e-8 relative.
7. Delete block, undo, copy-paste block → no crash.
8. Kill switch: any unexpected exception at the COM boundary is caught by a top-level guard in `UnitBase`, logged to `%LOCALAPPDATA%\OPBlocks\logs\`, and converted to `ECapeUnknown` with the log path in the message.

CI gate: a block DLL is only included in the installer if HostTests pass on the CI matrix (COCO/COFE headless as CAPE-OPEN reference host + DWSIM scripted). Aspen tests run on the owner's machine via `SelfTestRunner` before first registration.

---

## 9. Validation & Self-Test (Requirement R4 verification)

Per block, three test layers:

1. **Math unit tests** — model equations vs. hand calculations and literature datapoints (tolerances listed per block in §6). Run in CI on every commit.
2. **Thermo consistency tests** — run the block in COCO with two different property packages; verify (a) energy balance closes to <0.1% of duty, (b) mass balance to <1e-6 relative, (c) results shift when the package shifts (proves delegation is real, not hardcoded).
3. **Host smoke tests** — the §8 checklist automated where the host allows scripting (DWSIM Python interface, COFE COM), manual checklist doc for Aspen.

`SelfTestRunner` in the Manager re-runs layer 1+2 on the end-user machine and shows a green check per block. A block cannot be registered with a red status unless the user overrides with an explicit warning.

---

## 10. Manager App (WPF, .NET 8)

Screens:
1. **Detect** — found simulators table (name, version, bitness, path) from registry scan (`HKLM\SOFTWARE\AspenTech\Aspen Plus\*`, DWSIM install keys + default paths).
2. **Library** — card grid: icon, name, category, one-line description, validation badge, Install/Remove toggle.
3. **Install** — elevation prompt, registers selected DLLs (both bitness hives), installs icons, runs Self-Test, shows summary.
4. **Docs** — renders each block's model documentation (equations as images, refs).
5. **About/Update** — version, license (owner to decide: freeware portfolio vs. commercial — build supports a simple license-key gate behind a feature flag, disabled by default).

Distribution: Inno Setup single `OPBlocks_Setup.exe`, self-contained .NET 8 for the Manager, .NET FX 4.8 assumed present for block DLLs (Windows 10/11 default). Code-signing placeholder in the build script (owner supplies certificate later; unsigned builds will trigger SmartScreen — document this).

---

## 11. Milestones & Order of Work (for Claude Code)

| Milestone | Scope | Exit criteria |
|---|---|---|
| M0 | Core infrastructure: UnitBase, ThermoProxy, registration, ports/params, persistence, error guard + **one skeleton block** ("OP-MIXER-DEMO", trivial mixer) | Demo block passes full §8 checklist in COCO + DWSIM; registers and drops in Aspen Plus on owner's machine |
| M1 | Milestone A blocks (B1–B5) + Manager Detect/Library/Install screens + icon pipeline | All 5 pass §9 layers 1–2 in CI; owner validates in Aspen |
| M2 | Milestone B blocks (B6–B8); B6 calibrated against owner's ACM v5.3 outputs | ±5% match on DLE reference cases |
| M3 | Milestone C blocks (B9–B10), Aspen icon injection feature, installer, docs | Full library installer, signed checklist |

**M0 is mandatory first and must be demonstrated on a real Aspen Plus install before any physics work begins.** If M0 fails on Aspen (palette visibility, bitness, persistence), everything else is worthless — de-risk first.

## 12. Open items owner must supply

1. `dle_defaults.json` — parameter set from ACM v5.3 (q_max, K, k_LDF, bed geometry, cycle times) + 3 reference cases with results for B6 calibration.
2. ~~Aspen Plus version(s) installed on target machine~~ — **RESOLVED: Aspen Plus V14** is the sole Aspen target for HostTests and icon injection.
3. Brand decision: product name final ("ONE PROCESS Blocks"?) + license model.
4. SVG style approval after first 3 icons are drafted.

— End of specification —
