# ONE PROCESS Blocks v1.0.0

**25 open-source CAPE-OPEN unit operations for water, desalination, lithium and
green-energy flowsheets — built for Aspen Plus V14.**

This first release delivers a complete, physics-validated block library plus a
one-click installer, with an honest, evidence-based verification status for
every block.

## Highlights

- **25 CAPE-OPEN blocks** across five families — membranes, thermal
  desalination, electrochemical, lithium & sorption, energy & gas.
- **369 unit tests, all green** — each block's results are pinned to published
  data anchors on two CAPE-OPEN thermo backends, with determinism and exact
  mass-balance checks.
- **One-click install** — signed-free installer or portable ZIP, both hardened
  against the Mark-of-the-Web pitfall that breaks most downloaded COM add-ins.
- **Professional Manager app** to register/unregister the whole library.

## Verification status (honest by design)

| Tier | Blocks |
|---|---|
| ✅ **Host-verified** (ran converged in Aspen Plus V14) | OP-RO |
| 🧪 **Physics-validated** (full test suite + live COM activation + Aspen palette) | the other 24 |

We do not mark a block "host-verified" without evidence from a live converged
run. See the README status table and `docs/OP-*_MODEL.md` for each block's
equations, references and test anchors.

## Install

**Installer:** download `OPBlocks_Setup.exe`, run it, approve the Administrator
prompt. All 25 blocks register automatically.

**Portable:** download `OPBlocks-1.0.0-portable.zip`, right-click > Properties >
Unblock, extract, and double-click `INSTALL.bat`.

Then in Aspen Plus V14: Model Palette → **CAPE-OPEN** tab → drag any OP-… block.

Requirements: Windows 10/11 x64, Aspen Plus V14, .NET Framework 4.8 (in-box).

## Assets

- `OPBlocks_Setup.exe` — one-click installer (self-contained; ~50 MB)
- `OPBlocks-1.0.0-portable.zip` — portable distribution with `INSTALL.bat`

## License

MIT © ONE PROCESS Simulation. Bundles the public-domain EPA CAPE-OPEN .NET
class library (see `THIRD-PARTY.md`). Not affiliated with Aspen Technology, Inc.
