# Third-party components

OP-Blocks itself is MIT-licensed (see [LICENSE](LICENSE)). It depends on the
following third-party components.

## Redistributed binary

### CapeOpen.dll — EPA CAPE-OPEN .NET class library

- **What**: The CAPE-OPEN 1.0/1.1 interface definitions and helper base
  classes for .NET (`CapeUnitBase`, parameter/port collections, the
  `ECapeUser` exception hierarchy, COM registration helpers). Assembly
  identity `CapeOpen, Version=1.0.0.0, PublicKeyToken=90d5303f0e924b64`.
- **Author / origin**: W. M. Barrett, United States Environmental Protection
  Agency; first published 2007 as **EPA/600/C-07/013**
  ("CAPE-OPEN .NET Class Library", EPA Science Inventory record 181705).
  Version resource: "Copyright © EPA 2011".
- **License status**: A work of the United States Government prepared by an
  EPA employee as part of official duties. Under **17 U.S.C. § 105** such
  works have no U.S. copyright protection (public domain in the United
  States). The library has been redistributed freely for many years by the
  COCO simulator installer and the DWSIM project. No separate license text
  accompanies the binary.
- **Provenance of this copy**: taken from a DWSIM installation
  (see `libs/CapeOpen/ORIGIN.md`). For a signed release build we recommend
  swapping in the official EPA/CO-LaN distribution of the same assembly
  identity; the API is identical.
- **Interaction with our license**: the DLL is a separate, unmodified binary
  dependency. Our MIT license covers OP-Blocks code only and imposes nothing
  on this component.

## NuGet packages

| Package | Used by | License |
|---|---|---|
| SharpVectors.Reloaded 1.8.4 | Manager (SVG icon rendering) | BSD-3-Clause |
| Microsoft.Win32.Registry 5.0.0 | Manager (registration status) | MIT |
| System.Memory 4.5.5 | build-time only | MIT |
| Microsoft.NETFramework.ReferenceAssemblies | build-time only (net48 targeting) | MIT |
| xunit 2.9.2 / xunit.runner.visualstudio 2.8.2 | tests only (not distributed) | Apache-2.0 |
| Microsoft.NET.Test.Sdk 17.11.1 | tests only (not distributed) | MIT |

## Standards

**CAPE-OPEN** is an open interface standard maintained by
[CO-LaN](https://www.colan.org) (the CAPE-OPEN Laboratories Network); the
type libraries and interface specifications are distributed by CO-LaN under
a free license. This project implements the standard and is not endorsed by
or affiliated with CO-LaN, Aspen Technology, Inc., or the DWSIM project.
