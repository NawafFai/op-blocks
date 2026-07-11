# CapeOpen.dll — CAPE-OPEN .NET class library

`CapeOpen.dll` is the CO-LaN CAPE-OPEN .NET interface + helper class library
(namespace `CapeOpen`, assembly `CapeOpen, Version=1.0.0.0,
PublicKeyToken=90d5303f0e924b64`). It provides:

- The full set of CAPE-OPEN 1.0 / 1.1 interface definitions with the standard
  IIDs (`ICapeUnit`, `ICapeUtilities`, `ICapeIdentification`, `ICapeUnitReport`,
  `ICapeThermoMaterialObject`, `ICapeThermoMaterial`, `ICapeCollection`, ...).
- Helper base classes we build on: `CapeUnitBase`, `CapeObjectBase`,
  `RealParameter` / `IntegerParameter` / `BooleanParameter` / `OptionParameter`,
  `UnitPort`, `PortCollection`, `ParameterCollection`, `MaterialObjectWrapper`.
- The `ECapeUser` exception hierarchy (`CapeSolvingErrorException`,
  `CapeComputationException`, `CapeInvalidArgumentException`, ...).
- `[ComRegisterFunction]` / `[ComUnregisterFunction]` on `CapeUnitBase` that
  auto-writes the CapeUnitOperation CATID + CapeDescription registry keys on
  `regasm`, so a block only needs `[Guid]` + the `Cape*` attributes.

## Why we vendor it
Building against the same interop assembly the host loads guarantees IID/GUID
compatibility with **both Aspen Plus V14 and DWSIM**, which is exactly the
interop layer spec §3 calls for. Referencing it (rather than hand-writing COM
interop) removes the single highest-risk part of M0.

## Provenance
This copy was taken from the DWSIM installation on the build machine
(`%LOCALAPPDATA%\DWSIM\CapeOpen.dll`). It is the community-standard CO-LaN
managed library and is redistributable. For a signed release build, replace it
with the official CO-LaN distribution of the same assembly identity; the API is
identical.

**Do not edit this DLL.** It is a binary dependency, referenced by
`src/OPBlocks.Core` and copied next to every block DLL at build time.
