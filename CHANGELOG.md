# OP-Blocks Changelog

## v1.1 (in progress)

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
