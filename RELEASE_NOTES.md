# ONE PROCESS Blocks v1.1.2

**25 open-source CAPE-OPEN unit operations for water, desalination, lithium and
green-energy flowsheets — for Aspen Plus and DWSIM.**

An accuracy release: two host-integration defects fixed (one per host), two
block-physics defects caught by a new live 25-block Aspen sweep — and, for the
first time, **every block host-verified** on the shipped build.

> ### 👉 Which file do I download?
> **Most people: download `OPBlocks_Setup.exe`, run it, done.**
> Prefer no installer? Download `OPBlocks-1.1.2-portable.zip` instead and run
> `INSTALL.bat`. *(The "Source code" zip/tar.gz are added automatically by
> GitHub and are only for developers who want to build from source.)*

## Fixed in v1.1.2

- **Aspen: "completed with physical property errors" on saline feeds.** Cause:
  pure-water steam-table property methods (STEAMNBS / STEAM-TA) cannot represent
  salt — the block's physics was identical on every method; only the property
  system erred. The blocks now flash outlets in the known single phase (with an
  all-phases fallback), which removes the steam-table error path entirely, and
  raise **one clear warning** when the selected package returns near-pure-water
  density for a clearly saline feed. Use ELECNRTL (or IDEAL/NRTL) for saline
  cases — the bundled ONE PROCESS Aspen template already does.
- **DWSIM: outlet streams showed zero flow** while the block computed correctly.
  Cause: the thermo bridge preferred CAPE-OPEN Thermo 1.1 on streams that
  implement both generations, starving DWSIM's 1.0 outlet path. The bridge now
  prefers Thermo 1.0 with a 1.1 fallback (Aspen is 1.1-only and unaffected).
  End-to-end mass balance in DWSIM: 7.8e-16.
- **OP-CHLORALK over-produced mass by ~7 %** — the cathode's water consumption
  (2 H₂O per Cl₂ produced) was never deducted. The model now does explicit
  water bookkeeping; the live Aspen run closes to 1.4e-10.
- **OP-PPT could crash the Aspen engine** when its sludge left bone-dry (an
  Aspen-side pure-component data gap on dry-salt flashes). Sludge is now **wet**
  by physics (`SludgeSolids` parameter, default 20 wt %) — honest and crash-free.

## Verified in this release (the headline)

| Evidence | Result |
|---|---|
| Live Aspen Plus V14 sweep, all 25 blocks, physics-appropriate case each | **25/25 converged**, exact mass balances (1e-9 … 1e-16) |
| DWSIM host suite against the real engine | **ALL CHECKS PASSED (25 blocks)**, incl. a salt e2e: 35,000 ppm → 181 ppm, reported recovery == stream value |
| Unit tests | **382/382 green** |

Every catalog entry in the README is now **✅ Host-verified**. We do not mark a
block host-verified without evidence from a live converged run.

## From v1.1.x

- **Enable in DWSIM — one click.** The Manager deploys the native DWSIM adapter
  so all 25 blocks appear in DWSIM's palette **with their own icons**, under an
  OP-BLOCKS grouping. One button, no elevation, matching one-click removal.
- **Bulk actions.** *Install all* / *Remove all* register or unregister the
  whole library in a **single** UAC prompt.
- **Osmotic-pressure fix for molecular-salt packages** (v1.1.1): package water
  activity is trusted only when it actually models the salt (γw < 1); otherwise
  van 't Hoff with a stated assumption — no more 0 bar on NRTL/UNIQUAC.
- **Redesigned Manager** in the ONE PROCESS navy + blue palette, bilingual
  EN / AR (RTL) throughout.

## Install

**Installer:** download `OPBlocks_Setup.exe`, run it, approve the Administrator
prompt. All 25 blocks register automatically.

**Portable:** download `OPBlocks-1.1.2-portable.zip`, right-click > Properties >
Unblock, extract, and double-click `INSTALL.bat`.

Then in **Aspen Plus**: Model Palette → **CAPE-OPEN** tab → drag any OP-… block.
For **DWSIM**: open the Manager and click **Enable in DWSIM**, restart DWSIM, and
the OP-BLOCKS group appears in the palette with icons.

Requirements: Windows 10/11 x64, a CAPE-OPEN host (Aspen Plus V11–V14 or DWSIM),
.NET Framework 4.8 (in-box).

## Assets

- `OPBlocks_Setup.exe` — one-click installer (self-contained; ~50 MB)
- `OPBlocks-1.1.2-portable.zip` — portable distribution with `INSTALL.bat`

## License

MIT © ONE PROCESS Simulation. Bundles the public-domain EPA CAPE-OPEN .NET
class library (see `THIRD-PARTY.md`). Not affiliated with Aspen Technology, Inc.
or the DWSIM project.
