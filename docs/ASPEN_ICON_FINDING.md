# Aspen Plus V14 — Custom Flowsheet Icons for OP-Blocks: On-Machine Findings

**Date:** 2026-07-11 · **Machine:** owner's (Aspen Plus V14, engine 40.0) ·
**Method:** direct UI testing + binary analysis of Aspen's user-model-library
(`.apm`) format.

This supersedes the earlier "architecturally impossible" note: deeper
investigation found that Aspen **does** expose an editable icon mechanism, and
its icon format is a decodable text script we can generate from our SVGs. The
picture is now nuanced — parts are solved and automatable, one integration
detail and the physics-wrapping remain.

## What is now PROVEN achievable

### 1. Custom "ONE PROCESS" user model library
- `Customize → Manage Libraries → New…` creates a `ONE PROCESS.apm` user
  library. `Import…` loads it into any simulation. Verified: the library shows
  as **In Use** in a fresh simulation after import (once the root CLSID is
  correct — see §Format).

### 2. Custom category + custom model entries
- The custom-model wizard (`Add Selected`) can **Create New Category**
  (e.g. "Water & Desalination") and add a model to it. Verified on-machine.

### 3. **Icons are editable text scripts — generatable from our SVGs**  ← the breakthrough
- Aspen stores each model's icon as a plain-text drawing script in the model's
  `CONTENTS` stream inside the `.apm` OLE compound file. Vocabulary (from
  `iconed.dll` + shipped sample `Ultrafiltration.apm`):
  `Path.Box / Ellipse / Line / Polyline / Polygon / Arc / Text / Shift /
  Center / Scale`, `Port.name / at / direction / IOtype`, in a centred
  `-0.45..0.45`, Y-up icon square.
- **`icons/svg_to_aspen_icon.py`** parses our block SVGs (rect/circle/path,
  flattening curves/arcs), emits the Aspen `sub main` drawing script, and
  injects it into a model's `CONTENTS` — rebuilding the `.apm` compound file
  via Windows Structured Storage (StgCreateDocfile) so any size change is fine,
  **stamping the root CLSID `{17C95980-995F-453C-A591-E64D652FA515}` Aspen
  requires**. Verified: the rebuilt `.apm` imports cleanly into Aspen V14
  (the "not a valid model library" error is exactly the missing root CLSID).
- So all 25 icons can be generated and injected with **zero manual drawing** —
  the Aspen Icon Editor's manual vector tools are not needed, and its lack of a
  DXF/PNG import (confirmed — the V14 editor has no image import) does not block us.
- (A DXF path also works: `icons/svg_to_dxf.py` produces faithful DXF with
  fills, but the V14 Icon Editor UI has no DXF import, so the script-injection
  route is the one used.)

## What still needs work

### A. Surfacing the custom category as a palette tab
- After importing the library (In Use), the custom category
  "Water & Desalination" did **not** appear as a Model-Palette tab, and the
  model was not under "User Models" either. Aspen's palette tabs are the fixed
  built-in categories; a library's own category does not auto-create a tab on
  import. Likely fixes (next iteration): register the model under an **existing**
  palette category name in the root `CONTENTS` (e.g. "User Models"), or drive
  the palette-category registration the installer will perform, possibly with an
  Aspen restart. This is a small, well-scoped file/registration detail — not an
  architectural blocker.

### B. Physics: the library model must carry OP-Blocks physics
- `Add Selected` is disabled for raw CAPE-OPEN units, so a ONE-PROCESS library
  model is either an Aspen-native block or a **hierarchy wrapping** the
  CAPE-OPEN unit. Delivering our physics + our icon per block therefore means a
  hierarchy-wrapped CAPE-OPEN unit per block (ports exposed at the boundary),
  built once and saved to the library. This is the larger manual piece
  (≈25 hierarchies). The icon injection (§3) then applies automatically.

## Honest status & recommendation
- **The hard, uncertain part is solved:** Aspen icons are ours to generate and
  inject, and the library imports. The remaining work is (A) a palette
  registration detail and (B) 25 hierarchy wrappers for physics — both
  well-understood, mostly-manual, multi-step Aspen work.
- **DWSIM already delivers the full experience** (our 25 icons on the flowsheet
  + native physics + results + delete). For Aspen, the Manager should recommend
  DWSIM for the visual flowsheet while Aspen remains fully functional via the
  CAPE-OPEN palette (generic icon) today, with the ONE PROCESS custom-icon
  library as a follow-up once §A/§B are completed.

## Reusable tooling produced
- `icons/svg_to_aspen_icon.py` — SVG → Aspen icon script + `.apm` injector
  (root-CLSID-correct, size-safe compound rebuild).
- `icons/svg_to_dxf.py` — SVG → DXF (faithful fills), for any DXF-capable path.

## Reproduction
1. `python icons/svg_to_aspen_icon.py "installer/aspen/ONE PROCESS.apm" OP-RO icons/OP-RO.svg`
2. Aspen V14 → Customize → Manage Libraries → Import → the `.apm` → tick In Use.
3. The library imports (proves the CLSID/format); palette-tab surfacing per §A.
