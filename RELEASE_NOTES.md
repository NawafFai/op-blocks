# ONE PROCESS Blocks v1.1.1

**25 open-source CAPE-OPEN unit operations for water, desalination, lithium and
green-energy flowsheets — for Aspen Plus and DWSIM.**

A bug-fix release on top of v1.1.0 (one-click DWSIM enablement, bulk
install/remove, redesigned Manager).

> ### 👉 Which file do I download?
> **Most people: download `OPBlocks_Setup.exe`, run it, done.**
> Prefer no installer? Download `OPBlocks-1.1.1-portable.zip` instead and run
> `INSTALL.bat`. *(The "Source code" zip/tar.gz are added automatically by
> GitHub and are only for developers who want to build from source.)*

## Fixed in v1.1.1

- **Osmotic pressure with general property packages (OP-RO / OP-NF / OP-FO /
  OP-PRO).** In DWSIM, using a non-electrolyte package (NRTL, UNIQUAC, …) on a
  saline feed made the block report a **feed osmotic pressure of 0 bar**: such
  packages treat NaCl as a neutral molecule and return a water activity
  coefficient ≥ 1, which the block over-trusted. It now trusts the package
  activity only when it actually models the salt (γw < 1, e.g. ELECNRTL) and
  otherwise falls back to the van 't Hoff estimate — no more 0 bar. Aspen IDEAL /
  ELECNRTL results are unchanged; unit suite **371/371**.
- **Property-package guidance for DWSIM:** for electrolyte feeds use a native
  aqueous package (or an ideal one). **Steam Tables** supports pure water only;
  **Reaktoro** needs a configured Python distribution.

## From v1.1.0

- **Enable in DWSIM — one click.** The Manager now deploys the **native DWSIM
  adapter** into DWSIM's `unitops` folder, so all 25 blocks appear in the DWSIM
  palette **with their own icons**, under an OP-BLOCKS grouping. One button,
  no elevation, and a matching one-click removal.
- **Bulk actions.** *Install all* / *Remove all* register or unregister the
  whole library in a **single** UAC prompt instead of one per family.
- **Redesigned Manager.** A calmer, more professional interface in the ONE
  PROCESS navy + blue palette — gradient header, detected-simulator cards, a
  block-library toolbar with a live count, a clean card-framed list, and a dark
  activity log. Bilingual EN / AR (RTL) throughout.

## Highlights (carried from v1.0.0)

- **25 CAPE-OPEN blocks** across five families — membranes, thermal
  desalination, electrochemical, lithium & sorption, energy & gas.
- **369 unit tests, all green** — each block's results are pinned to published
  data anchors on two CAPE-OPEN thermo backends, with determinism and exact
  mass-balance checks.
- **One-click install** — signed-free installer or portable ZIP, both hardened
  against the Mark-of-the-Web pitfall that breaks most downloaded COM add-ins.

## Verification status (honest by design)

| Tier | Blocks |
|---|---|
| ✅ **Host-verified** (ran converged in Aspen Plus V14; native blocks live in DWSIM) | OP-RO |
| 🧪 **Physics-validated** (full test suite + live COM activation + Aspen palette) | the other 24 |

We do not mark a block "host-verified" without evidence from a live converged
run. See the README status table and `docs/OP-*_MODEL.md` for each block's
equations, references and test anchors.

## Install

**Installer:** download `OPBlocks_Setup.exe`, run it, approve the Administrator
prompt. All 25 blocks register automatically.

**Portable:** download `OPBlocks-1.1.1-portable.zip`, right-click > Properties >
Unblock, extract, and double-click `INSTALL.bat`.

Then in **Aspen Plus**: Model Palette → **CAPE-OPEN** tab → drag any OP-… block.
For **DWSIM**: open the Manager and click **Enable in DWSIM**, restart DWSIM, and
the OP-BLOCKS group appears in the palette with icons.

Requirements: Windows 10/11 x64, a CAPE-OPEN host (Aspen Plus V11–V14 or DWSIM),
.NET Framework 4.8 (in-box).

## Assets

- `OPBlocks_Setup.exe` — one-click installer (self-contained; ~50 MB)
- `OPBlocks-1.1.1-portable.zip` — portable distribution with `INSTALL.bat`

## License

MIT © ONE PROCESS Simulation. Bundles the public-domain EPA CAPE-OPEN .NET
class library (see `THIRD-PARTY.md`). Not affiliated with Aspen Technology, Inc.
