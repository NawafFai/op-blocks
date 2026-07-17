<#
.SYNOPSIS
  Registers (or unregisters) OP-Blocks block DLLs as CAPE-OPEN Unit Operations in
  BOTH the x64 and x86 COM hives, so they appear in Aspen Plus V14 and DWSIM.

.DESCRIPTION
  Uses the .NET Framework RegAsm with /codebase (the DLLs live in their build
  folder, not the GAC). The block classes derive from CapeOpen.CapeUnitBase, whose
  [ComRegisterFunction] writes the CapeUnitOperation CATID + CapeDescription keys.

  Registering writes to HKLM\SOFTWARE\Classes and therefore needs Administrator
  rights; the script self-elevates via UAC (spec section 1). This mirrors what the
  OP-Blocks Manager does behind its elevation prompt.

.PARAMETER Configuration
  Build configuration whose output to register (default Release).
.PARAMETER Unregister
  Unregister instead of register.
#>
param(
    [string]$Configuration = "Release",
    [switch]$Unregister
)
$ErrorActionPreference = "Stop"

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object System.Security.Principal.WindowsPrincipal($id)).IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Self-elevate if needed.
if (-not (Test-Admin)) {
    Write-Host "Elevation required for COM registration - launching UAC prompt..." -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"",
                 "-Configuration", $Configuration)
    if ($Unregister) { $argList += "-Unregister" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    return
}

$root = Split-Path -Parent $PSScriptRoot
$regasm64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$regasm32 = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"

# Every registerable block DLL (Core is infrastructure and is not registered).
$dlls = Get-ChildItem (Join-Path $root "src") -Recurse -Filter "OPBlocks.*.dll" |
    Where-Object { $_.FullName -match "\\bin\\$Configuration\\" -and $_.Name -ne "OPBlocks.Core.dll" }

if (-not $dlls) { throw "No block DLLs found for '$Configuration'. Build first (scripts\build.ps1)." }

$action = if ($Unregister) { "Unregistering" } else { "Registering" }
foreach ($dll in $dlls) {
    foreach ($regasm in @($regasm64, $regasm32)) {
        $bit = if ($regasm -eq $regasm64) { "x64" } else { "x86" }
        Write-Host "==> $action $($dll.Name) [$bit]" -ForegroundColor Cyan
        $regArgs = @("`"$($dll.FullName)`"")
        if ($Unregister) { $regArgs += "/unregister" } else { $regArgs += "/codebase" }
        & $regasm @regArgs
        if ($LASTEXITCODE -ne 0) { Write-Host "   RegAsm returned $LASTEXITCODE for $bit" -ForegroundColor Red }
    }
}

if (-not $Unregister) {
    # Correct the informational CapeDescription values the library shifts (see script header).
    Write-Host "==> Correcting CapeDescription metadata" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "fix-registration-metadata.ps1") -Configuration $Configuration
}

Write-Host "==> Done. In Aspen Plus V14: Model Palette -> Customize/CAPE-OPEN -> 'ONE PROCESS'." -ForegroundColor Green
Write-Host "    (This window stays open so you can read the results.)" -ForegroundColor DarkGray
if ($MyInvocation.MyCommand.Path -and -not $env:OPBLOCKS_NOWAIT) { Read-Host "Press Enter to close" | Out-Null }
