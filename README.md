# ONE PROCESS Blocks (OP-Blocks)

Custom CAPE-OPEN Unit Operations for **Aspen Plus V14** and **DWSIM**, plus a
manager app to install/register them. See
[`ONE_PROCESS_Custom_Blocks_Spec_v1.md`](docs/ONE_PROCESS_Custom_Blocks_Spec_v1.md)
for the full specification, and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
for the implementation decisions.

## Status — Milestone M0 (de-risk) ✅ code complete

M0 delivers the shared infrastructure and one skeleton block, so the whole
plumbing can be proven on real Aspen before any physics work (spec §11).

| Piece | State |
|---|---|
| `OPBlocks.Core` — `UnitBase`, `ThermoProxy`, reporting, logging, IPersistStream(Init) | ✅ builds |
| `OP-MIXER-DEMO` skeleton block | ✅ builds, COM-visible |
| Unit tests (mixer math §9.1 + persistence roundtrip §8.6) | ✅ 9/9 pass |
| COM registration (both bitness hives) | scripts ready — run `scripts\register.ps1` (elevates) |
| Drag-drop in Aspen V14 / DWSIM | manual verification step (needs the elevated registration first) |

## Build & test

```powershell
# .NET 8 SDK required (block DLLs target net48, Manager targets net8)
scripts\build.ps1              # build Release + run unit tests
```

## Register the blocks (Administrator / UAC)

```powershell
scripts\register.ps1           # registers every block DLL in x64 + x86 hives
scripts\unregister.ps1         # removes them
```

After registering, in **Aspen Plus V14**: Model Palette → *Customize / CAPE-OPEN*
→ category **ONE PROCESS**. In **DWSIM**: Object Palette → CAPE-OPEN Unit
Operation → ONE PROCESS.

## Layout

```
src/OPBlocks.Core/     shared infrastructure (net48)
src/OPBlocks.Demo/     OP-MIXER-DEMO skeleton block (net48)
libs/CapeOpen/         vendored CO-LaN CAPE-OPEN .NET class library
tests/UnitTests/       xUnit tests (net48)
scripts/               build / register / unregister
docs/                  spec + architecture + per-block docs
```

## Toolchain

- **.NET 8 SDK** (installed) — builds both `net48` block DLLs and the future
  `net8` Manager. Block DLLs pull .NET Framework reference assemblies from the
  `Microsoft.NETFramework.ReferenceAssemblies` NuGet package.
- **.NET Framework 4.8** runtime — required at runtime by the block DLLs
  (present on Windows 10/11 by default).
- Registration uses the .NET Framework `RegAsm` (both bitnesses).
