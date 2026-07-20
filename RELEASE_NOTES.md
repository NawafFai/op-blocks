# ONE PROCESS Blocks v1.1.3

**25 open-source CAPE-OPEN unit operations for water, desalination, lithium and
green-energy flowsheets — for Aspen Plus and DWSIM.**

A stabilization & UX release. Every change came from using the product through
the live Aspen GUI: smarter errors, clearer identity, a readable input form,
redrawn icons, and a one-click Aspen palette that just appears.

> ### 👉 Which file do I download?
> **Most people: download `OPBlocks_Setup.exe`, run it, done.**
> Prefer no installer? Download `OPBlocks-1.1.3-portable.zip` instead and run
> `INSTALL.bat`. *(The "Source code" zip/tar.gz are added automatically by
> GitHub and are only for developers who want to build from source.)*

## What's new in v1.1.3

**Errors that help instead of confuse.**
- When the applied pressure doesn't exceed the feed's osmotic pressure, an
  osmotic block (OP-RO/NF/FO/PRO) now **completes with zero permeate and a
  clear warning** — honest physics, not the cryptic "CAPE-OPEN UNIT CALCULATE
  CALL FAILED. SEE HISTORY". Raise the pressure or dilute the feed.
- Feeding a brine beyond a process's physical envelope (e.g. RO on a
  near-saturated feed — NaCl saturates ~26 wt%) is rejected early with a
  message stating the salinity, the limit, and the right equipment to use
  instead (evaporator / crystallizer).

**Every block tells you what it is.**
- The block editor opens with the full name, the physics it performs, and a
  "typical use" hint. Parameters have descriptive labels with units, logical
  sections, and a hover tooltip carrying the full description and allowed range.
- In DWSIM the palette and flowsheet are code-first ("OP-RO — Reverse Osmosis")
  with the block code drawn on the icon.

**A redrawn icon system.** All 25 icons were redesigned so each block is
recognizable at a glance — a distinct silhouette per process, the physics it
does, a family colour, and the block code baked into the artwork.

**One-click Aspen palette.** "Enable in Aspen" now activates the OP Blocks
palette automatically in every new simulation — no more Customize ▸ Manage
Libraries by hand. Run it with Aspen closed (Aspen rewrites its library list on
exit).

**Choosing a property package.** The README now spells it out: IDEAL for dilute
feeds, ELECNRTL for seawater and brines, and never the pure-water steam tables
for anything saline.

## Quality

- OP-MIXER-DEMO no longer logs a spurious persistence error on every file open.
- Unit suite **397** (from 382): P1 zero-permeate + envelope regressions, block
  identity and form coverage, and a render watchdog that builds every block's
  editor without hanging. A new registry regression covers the Aspen palette
  activation. **OP-RO re-verified live** — anchor permeate 686.484746 kg/h,
  zero regression across the whole series. DWSIM host suite: all 25 blocks pass,
  now painting their new icons.

## From v1.1.2

- Host-accuracy fixes: the steam-table property-error trap (Aspen) and the
  Thermo-1.0-first outlet fix (DWSIM); OP-CHLORALK mass balance and OP-PPT wet
  sludge; a live 25/25 converged Aspen sweep.

## Install

**Installer:** download `OPBlocks_Setup.exe`, run it, approve the Administrator
prompt. All 25 blocks register automatically.

**Portable:** download `OPBlocks-1.1.3-portable.zip`, right-click > Properties >
Unblock, extract, and double-click `INSTALL.bat`.

Then in **Aspen Plus**: run the Manager, click **Enable in Aspen** (with Aspen
closed), open Aspen — the OP Blocks palette tab is already there. All 25 blocks
also appear in the **CAPE-OPEN** palette tab. For **DWSIM**: click **Enable in
DWSIM**, restart DWSIM, and the OP-BLOCKS group appears with icons.

Requirements: Windows 10/11 x64, a CAPE-OPEN host (Aspen Plus V11–V14 or DWSIM),
.NET Framework 4.8 (in-box).

## Assets

- `OPBlocks_Setup.exe` — one-click installer (self-contained; ~50 MB)
- `OPBlocks-1.1.3-portable.zip` — portable distribution with `INSTALL.bat`

## License

MIT © ONE PROCESS Simulation. Bundles the public-domain EPA CAPE-OPEN .NET
class library (see `THIRD-PARTY.md`). Not affiliated with Aspen Technology, Inc.
or the DWSIM project.
