# Aspen Plus V14 — Custom Flowsheet Icons for CAPE-OPEN Blocks: On-Machine Finding

**Date:** 2026-07-11 · **Machine:** owner's (Aspen Plus V14, engine 40.0) · **Method:** direct
UI testing in Aspen Plus V14 with the registered OP-Blocks CAPE-OPEN units.

This document exists because the spec's honesty rule (§1B, §7) requires that any host
limitation be **proven with evidence and the exact mechanism cited** — never silently
downgraded.

## Verdict

**Aspen Plus V14 does not support custom per-block flowsheet icons, custom palette
icons, or a custom palette tab for CAPE-OPEN unit operations.** The block is drawn with
Aspen's generic block rectangle and lives under the fixed built-in **CAPE-OPEN** palette
category. This is architectural, not a bug in OP-Blocks, and it is the same for every
third-party CAPE-OPEN unit (COCO/COFE units, OLI, etc.) hosted in Aspen.

The competitive "custom icon on the flowsheet" feature **is fully delivered on DWSIM**
(native `IExternalUnitOperation` layer, P1A), because DWSIM exposes a drawing hook that
Aspen does not.

## What was attempted, and what Aspen returned (all on-machine)

| Attempt | Mechanism | Result |
|---|---|---|
| Place a block from the CAPE-OPEN palette | drag/click OP-RO onto the flowsheet | Places as a **plain rectangle** labelled "OP-RO" (B1). Runs, connects — but generic icon. |
| Right-click → **Exchange Icon** | Aspen's per-model icon-set cycler | **No effect** — CAPE-OPEN units register a **single** icon in Aspen's internal icon library; there is no second icon to exchange to. Confirmed by repeated clicks: the rectangle never changes. |
| **Customize → Add Selected** (add the placed block to a user model library) | the supported path for giving an Aspen model a custom icon via the Icon Editor | **Disabled (greyed out)** for CAPE-OPEN blocks. Add-Selected only accepts Aspen-native models (Fortran/Excel/Hierarchy/ACM). |
| **Customize → Manage Libraries → New…** → created "ONE PROCESS" user library (.apm) | user model library that *can* hold custom icons | Library is created, but **nothing can be added to it** — CAPE-OPEN units are not eligible (Add-Selected disabled, see above). The empty library was removed. |
| **Customize → Palette Categories** | create/assign a custom palette tab | The category list is **fixed** (Mixers/Splitters … CAPE-OPEN) with **no "New" button** — only enable/disable + reorder. CAPE-OPEN units are locked to the built-in "CAPE-OPEN" tab; a "ONE PROCESS" tab cannot be created. |
| Palette icon inspection (zoom) | — | CAPE-OPEN palette entries render as Aspen's **generic framed text** (the block name), while built-in units (e.g. XaceEconomizer) show real graphics — confirming the palette icon is Aspen-assigned, not taken from the CAPE-OPEN component. |

## Why this is architectural

- The **CAPE-OPEN 1.1 standard has no unit-operation icon/graphics interface** that a
  host consumes. A CAPE-OPEN unit exposes identification, ports, parameters, validate,
  calculate, report, persistence — **no drawing/icon contract**. The icon a host shows is
  entirely the host's own choice.
- **Aspen chooses** a single fixed generic icon for all CAPE-OPEN units and provides no
  supported hook to override it per CLSID.
- Community tooling that *does* give CAPE-OPEN units custom icons — **JUIcE (Just a
  Unit-operation Icon Editor)** — targets **COFE/COCO**, whose flowsheet renderer reads a
  per-unit icon. Aspen's does not. (Source: CO-LaN / cocosimulator.org.)

## Best achievable in Aspen Plus V14 (delivered)

Given the above, the following ARE delivered inside Aspen and are the maximum the host
allows:

1. **All 25 blocks appear in Aspen's Model Palette** (CAPE-OPEN tab) with their `OP-*`
   names, from the dual-bitness COM registration.
2. **Drag → drop → connect → run → delete works** (R3 core). Deleting a placed block was
   verified clean on-machine (no crash, standard Confirm-Delete dialog).
3. **Branded ONE PROCESS Edit dialog** opens **in-process inside Aspen** — the WinForms
   editor with the ONE PROCESS header, per-block parameters with correct ranges/units,
   and the block's own equipment image. This is real ONE PROCESS identity on the Aspen
   side, at the one place Aspen lets us control the pixels.
4. **Human-readable results** via `ICapeUnitReport` under the block's Results in Aspen.

## Recommendation to the owner

- **DWSIM is the platform where the custom-icon experience is complete** — the Manager
  should present DWSIM as the recommended environment for the visual ONE PROCESS flowsheet
  experience, and this is stated honestly (not because Aspen is unsupported — it is fully
  supported functionally — but because Aspen renders every CAPE-OPEN unit generically).
- If per-block Aspen flowsheet icons ever become a hard requirement, the only path is to
  **re-implement the blocks as Aspen-native models** (Aspen Custom Modeler models or
  Excel/Fortran user models added to the ONE PROCESS .apm library), which *do* support the
  Icon Editor. That is a separate, large workstream (a second physics host) and is out of
  scope for the CAPE-OPEN product; it would also duplicate the physics unless bridged.

## Reproduction (for independent verification)

1. Register the blocks (`scripts\register-all-blocks.ps1`), open Aspen Plus V14.
2. Model Palette → **CAPE-OPEN** tab → the OP-* blocks are listed.
3. Drop OP-RO → it appears as a rectangle. Right-click → **Exchange Icon** → no change.
4. Select it → ribbon **Customize** → **Add Selected** is greyed.
5. **Customize → Palette Categories** → no "New"; CAPE-OPEN is a fixed entry.
