# Contributing to ONE PROCESS Blocks

Thanks for your interest. This project ships production-grade CAPE-OPEN unit
operations, so contributions follow the same engineered pattern every existing
block was held to. The bar is deliberately high — the payoff is that a reviewer
can trust a new block on sight.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK (builds the `net48` block DLLs and the `net8` Manager)
- Aspen Plus V14 for host verification (optional for a pull request; the
  maintainers run the live pass)

## Build & test

```powershell
dotnet build tests\UnitTests\UnitTests.csproj -c Release   # builds all block families
dotnet test  tests\UnitTests\UnitTests.csproj -c Release   # full validation suite
```

A pull request must keep the suite green.

## Adding a new block (the "block factory")

Every block is built against the same six gates — mirror an existing block of a
similar family (e.g. `src/OPBlocks.Desalination/FamilyB_Membranes.cs` for a
membrane, `FamilyE_Energy.cs` for an electrochemical/energy unit):

1. **Pure engine in `OPBlocks.Core`.** Put the physics in a `static class
   XxxModel` with plain arrays/doubles — no CAPE-OPEN, no host types. The block
   and the tests both call it, so they cannot drift. Hand-write the equations
   with citations; do not copy another block's physics.
2. **Model sheet.** Add `docs/OP-XX_MODEL.md`: equations, references, validity
   range, and the exact test anchors.
3. **The block class.** Named material ports, **RealParameter inputs only**
   (Integer/Option/Boolean parameters blank Aspen's grid — this is the single
   most important host rule), `AddOutputParameter`/`SetOutputParameter` for the
   Results grid, engineering `ReportWarning`s, and a `BuildReport` override with
   a "Model & References" block. Pull all thermo from the property package via
   `ThermoProxy`; get volumetric flow from package mass ÷ package density.
4. **Validation suite** in `tests/UnitTests`, using `TestKit.cs`:
   - structural: block == engine within 0.1 % on canonical cases, on **both**
     the Thermo 1.0 and 1.1 mock hosts;
   - physical anchors: closed-form / published-data checks;
   - determinism: 20 runs stable below 1e-8;
   - results == streams (exact) and total mass balance to 1e-9;
   - ports / defaults / RealParameter-only / Model&References-in-report.
5. **Register/package wiring.** Add the block to the family's
   `*.opblocks.json` manifest (new `clsid`, `capeVersion`, `vendorUrl`).
6. **Honest status.** New blocks enter the README table as
   🧪 *Physics-validated*. Only a maintainer's converged in-Aspen run promotes a
   block to ✅ *Host-verified* — never mark a block host-verified without
   evidence from a live run.

## Style

Match the surrounding code: ASCII-only in files that Aspen's report viewer
renders, the same naming and comment density as the neighbouring block, and
citations in the engine header.

## Reporting issues

Please open a GitHub issue and include: the block, the host (Aspen Plus V14),
the property package, the feed composition, and what you expected vs. observed.
Registration problems are almost always a missing "Run as Administrator" or a
downloaded ZIP that was not unblocked — see the README troubleshooting section
first. For anything else: **alahmadnf@outlook.com**.
