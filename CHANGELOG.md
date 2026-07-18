# OP-Blocks Changelog

## v1.0.0 — 2026-07-17 (first public release)

- **25 CAPE-OPEN unit operations** across five families (membranes, thermal
  desalination, electrochemical, lithium & sorption, energy & gas), each with
  a pure physics engine in Core, published references, a per-block model sheet
  in `docs/`, and a validation suite — **369 tests, all green**.
- **OP-RO host-verified** in Aspen Plus V14 (converged IDEAL + ELECNRTL runs,
  exact mass balance, 20-run determinism, results == stream table). The other
  24 blocks are physics-validated (unit suites + live COM activation + Aspen
  palette); their converged in-host runs are the next milestone.
- Golden-rule compliance throughout: RealParameter-only inputs (six legacy
  Integer parameters converted), output-parameter Results grids, Model &
  References block reports, engineering warnings.
- Tooling: per-user and elevated COM registration scripts, blocks packager,
  portable-zip + Inno Setup installer pipeline, live-verification tools
  (COM activation gate, per-user registrar).
- License: MIT with ONE PROCESS trademark notice.

## v1.1.1 — 2026-07-19

### Fix — osmotic pressure with molecular-salt property packages (DWSIM)

- **Osmotic blocks (OP-RO / OP-NF / OP-FO / OP-PRO)** reported a **feed osmotic
  pressure of 0 bar** for a clearly saline feed when the flowsheet used a
  general (non-electrolyte) property package such as **NRTL / UNIQUAC** — packages
  that treat NaCl as a neutral molecule and return a water activity coefficient
  γw ≥ 1. The block took that at face value, clamped the water activity to 1, and
  got ln(1) = 0.
- Root cause / fix (`ProcessOps.OsmoticPressureBar`): a dissolved salt ALWAYS
  lowers water activity (a_w = γw·xw < xw ⟹ γw < 1), so the package activity is
  now trusted **only when γw < 1** (real electrolyte behaviour, e.g. ELECNRTL
  ≈ 0.99). For γw ≥ 1 the block falls back to the ideal-solution van 't Hoff
  estimate and states the assumption — no more 0 bar.
- Unchanged and re-verified: Aspen IDEAL (γw = 1 → van 't Hoff) and Aspen
  ELECNRTL (γw < 1 → package activity) behave exactly as in the host-verified
  runs. Unit suite **371/371** (two new regression tests pin the γw ≥ 1 and
  γw < 1 routes).
- Guidance: for electrolyte feeds in DWSIM use a native aqueous package (or an
  ideal one so van 't Hoff engages); **Steam Tables** supports pure water only,
  and **Reaktoro** requires a configured Python distribution.

## v1.1.0 — 2026-07-18

### OP-Blocks Manager — professional redesign + one-click DWSIM

- **"Enable in DWSIM"** one-click button: deploys the native DWSIM adapter
  (`OPBlocks.DWSIM.dll` + `OPBlocks.*` + `CapeOpen.dll`) into
  `%LOCALAPPDATA%\DWSIM\unitops\` so the blocks appear in DWSIM's palette **with
  custom icons**, under an OP-BLOCKS grouping. The button is state-aware
  (Enable ⇄ Disable) and needs no elevation (per-user folder).
- **Bulk actions** — *Install all* / *Remove all* register or unregister the
  whole library in a **single** UAC elevation instead of one prompt per family.
- **Visual redesign** to the ONE PROCESS navy (`#1B3A5C`) + blue (`#29ABE2`)
  palette: a proper WPF design system (brand/neutral/semantic brushes, reusable
  button/card/chip styles), a gradient header, accent-rail host cards, a library
  toolbar with a live block count, a card-framed block list, and a dark activity
  console. Bilingual EN/AR (RTL) throughout.

### Priority 1B — Aspen Plus V14 custom icons: proven architectural limitation
- Tested every Aspen V14 mechanism for custom CAPE-OPEN icons on the owner's
  machine: Exchange Icon (single fixed icon, no effect), Add Selected to a
  user library (disabled for CAPE-OPEN), a new ONE PROCESS .apm library
  (nothing can be added to it), and Palette Categories (fixed list, no custom
  tab). All documented with evidence in `docs/ASPEN_ICON_FINDING.md`.
- Root cause: the CAPE-OPEN standard has no unit-operation icon interface;
  Aspen draws all CAPE-OPEN units with its generic block icon and exposes no
  per-CLSID override. Community custom-icon tooling (JUIcE) targets COFE/COCO,
  not Aspen.
- Delivered in Aspen (the host maximum): blocks in the CAPE-OPEN palette,
  drag/drop/connect/run/delete, the branded in-process ONE PROCESS Edit
  dialog, and ICapeUnitReport results. The full custom-icon-on-flowsheet
  feature is delivered on DWSIM (P1A), where the host allows it.


### Priority 1A — DWSIM-native blocks with custom flowsheet icons
- **New `OPBlocks.DWSIM.dll`**: all 25 blocks now ship as DWSIM-native external
  unit operations (`IExternalUnitOperation`, loaded from DWSIM's `unitops`
  folder) — no CAPE-OPEN COM involved in DWSIM anymore.
- **Custom icons on the DWSIM flowsheet**: each block draws its own ONE PROCESS
  icon on the PFD canvas (SkiaSharp), with port anchors on the icon edges, plus
  the same icon in the palette/object tree. Icon failures degrade silently to a
  branded fallback glyph (R3 > R2).
- **Zero physics duplication**: the native layer wraps the existing CAPE-OPEN
  block classes in-process; DWSIM's `MaterialStream` natively implements
  `ICapeThermoMaterialObject`, so `ThermoProxy` reads/writes DWSIM streams
  directly. Any physics fix applies to both platforms automatically.
- **Outlet propagation fix (DWSIM)**: discovered that the CAPE-OPEN
  `SetProp("flow")` path writes per-compound molar flows but never updates
  mole/mass fractions or phase totals that DWSIM's flash and solver read —
  outlet streams silently kept stale flows. The native adapter normalizes each
  connected outlet after calculation (fractions, totals, mass flows, stream
  spec), the same job DWSIM's own CAPE-OPEN wrapper only partially performs.
  This latent defect affected the CAPE-OPEN-in-DWSIM path in v1.0.
- **Persistence**: parameters and last results survive save/reopen and
  copy/paste via a single serialized state property carried by DWSIM's XML and
  JSON pipelines.
- **Evidence**: `tests/DwsimHostTest` replays DWSIM's unitops discovery
  pipeline (full-folder `Assembly.LoadFile` scan) and then runs a real headless
  DWSIM engine calculation (Automation2: water feed → OP-EVAPPOND →
  concentrate + vapour). All 25 blocks pass identity/connector/draw/icon/state
  checks; e2e mass balance closes to 8e-16. Run:
  `DwsimHostTest.exe <staged-unitops-folder>`.
- Note: DWSIM's unitops scanner probes the OPBlocks family DLLs too and logs a
  benign "error loading types" console line for them (they carry no native unit
  ops and are resolved lazily by the adapter). Harmless by design.
