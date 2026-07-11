# OP-Blocks Architecture & Key Decisions

This records the decisions taken while building M0, and the evidence behind them,
so later milestones stay consistent.

## Environment (build machine, verified)

| Component | Finding |
|---|---|
| Aspen Plus | **V14** (registry engine `40.0`), `C:\Program Files\AspenTech\Aspen Plus V14.0` — the sole Aspen target (spec §12.2) |
| DWSIM | installed; ships the CO-LaN `CapeOpen.dll` we build against |
| .NET Framework | 4.8 runtime present (block DLL runtime target) |
| .NET SDK | none initially → installed **.NET 8 SDK 8.0.422** via winget |
| Build tools | no Visual Studio / MSBuild / Roslyn beyond the legacy FX 4.0 one → the SDK provides modern MSBuild + Roslyn |

## Decision 1 — Build on the CO-LaN CAPE-OPEN .NET class library

`libs/CapeOpen/CapeOpen.dll` (assembly `CapeOpen 1.0.0.0`, PublicKeyToken
`90d5303f0e924b64`) is the community-standard CO-LaN managed library. It provides
the interface definitions **with the correct IIDs** plus helper base classes:

- `CapeUnitBase` — implements `ICapeUnit`, `ICapeUtilities`, `ICapeIdentification`,
  `ICapeUnitReport`, `ECapeUser`; and carries `[ComRegisterFunction]` /
  `[ComUnregisterFunction]` that write the **CapeUnitOperation CATID**
  (`{678C09A5-...}`) and the `CapeDescription` keys on `regasm`.
- `RealParameter` / `IntegerParameter` / `BooleanParameter` / `OptionParameter`,
  `UnitPort`, `PortCollection` (`BindingList<UnitPort>`), `ParameterCollection`.
- `MaterialObjectWrapper(object)` — typed wrapper over the Thermo 1.0
  `ICapeThermoMaterialObject` Aspen hands a unit.
- The `ECapeUser` exception hierarchy (`CapeSolvingErrorException`,
  `CapeComputationException`, `CapeInvalidArgumentException`, …), all deriving
  from `ApplicationException` and implementing `ECapeUser`/`ECapeRoot`, so throwing
  them across the COM boundary gives the host a proper actionable error (R3).

**Why:** it is the same interop the hosts load, guaranteeing GUID compatibility
with Aspen V14 **and** DWSIM, and it removes the single riskiest part of M0
(hand-written COM interop + CATID registration). `UnitBase` derives from
`CapeUnitBase`.

## Decision 2 — `net48` block DLLs, AnyCPU, registered in both hives

- Blocks target **net48** (present on all Win10/11); compiled with the .NET 8 SDK
  using `Microsoft.NETFramework.ReferenceAssemblies` for the reference assemblies
  (no targeting-pack install needed).
- **AnyCPU (MSIL)** output so a single DLL works for both bitnesses; `RegAsm` is
  run from **both** `Framework64` and `Framework` so the class is present in the
  x64 and x86 COM hives — a missing bitness is the #1 "block not in palette"
  failure (spec §3).

## Decision 3 — Cross-cutting guarantees live in `UnitBase`

`CapeUnitBase` does not implement persistence, so `UnitBase` adds the parts the
spec requires of *every* block:

- **Boundary guard (§8.8):** `OnCalculate`/`ProduceReport`/`Edit`/`Validate` are
  wrapped; unexpected exceptions are logged to `%LOCALAPPDATA%\OPBlocks\logs\` and
  surfaced as a `CapeUnknownException` carrying the log path. No raw .NET
  exception crosses COM.
- **Persistence (§8.6):** `UnitBase` implements **both** `IPersistStream` and
  `IPersistStreamInit` (declared locally with their canonical IIDs, since the CLR
  ships neither) — Aspen uses `IPersistStreamInit`, COFE/DWSIM use
  `IPersistStream`. A versioned, length-prefixed payload stores every parameter by
  name; blocks add extra state (e.g. component mappings) via `SaveExtra`/`LoadExtra`.
- **Thermo delegation (§5, R4):** subclasses get material objects only through
  `GetConnectedMaterial(port) → ThermoProxy`, which funnels every property call to
  the host package and enforces the strict outlet set order
  (composition → P → H → flash).

## Decision 4 — Testable physics separate from COM

Pure model math (e.g. `MixerMath`) has no CAPE-OPEN types, so §9 layer-1 tests run
without a host. COM-level behavior (persistence) is tested by driving
`IPersistStreamInit` over an in-memory `IStream`.

## Open / deferred

- **Elevation:** registration writes `HKLM\SOFTWARE\Classes` and needs admin; the
  Manager will elevate via UAC (spec §1). A per-user (`HKCU\Software\Classes`)
  fallback for non-elevated installs is a possible Manager enhancement.
- **Edit GUI:** M0 uses the library's generic parameter editor (guarded). Spec §4
  WinForms per-block dialogs come with the real blocks (M1+).
- **Solvers, icons, Manager app:** M1+.
