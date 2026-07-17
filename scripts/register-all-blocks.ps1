<#
.SYNOPSIS
  Registers EVERY ONE PROCESS block as a CAPE-OPEN unit operation so it appears
  in Aspen Plus V14 (and DWSIM). Self-elevates, removes the Mark-of-the-Web that
  downloaded files carry, registers each family DLL in the x64 + x86 COM hives,
  applies the CapeDescription metadata, and prints a clear summary.

.DESCRIPTION
  This is what the "Install / Register" button in the OP-Blocks Manager runs, in
  one bulk pass. It is safe to run repeatedly (re-registration is idempotent).

.PARAMETER Unregister
  Remove the blocks instead of installing them.
#>
param([switch]$Unregister)
$ErrorActionPreference = "Stop"

# --- self-elevate (COM registration writes HKLM) ---------------------------
function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-Admin)) {
    Write-Host "Administrator rights are required - launching the UAC prompt..." -ForegroundColor Yellow
    $a = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    if ($Unregister) { $a += "-Unregister" }
    try { Start-Process powershell.exe -Verb RunAs -ArgumentList $a } catch {
        Write-Host "Elevation was cancelled. Right-click this file and 'Run with PowerShell' as admin." -ForegroundColor Red
    }
    return
}

$root       = Split-Path -Parent $PSScriptRoot
$blocksRoot = Join-Path $root "blocks"
$regasm64   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$regasm32   = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"

# --- prerequisites ---------------------------------------------------------
if (-not (Test-Path $blocksRoot)) {
    Write-Host "ERROR: blocks folder not found at $blocksRoot" -ForegroundColor Red
    Write-Host "Run this script from inside the extracted ONE PROCESS Blocks folder." -ForegroundColor Red
    Read-Host "Press Enter to close"; return
}
$regasms = @()
if (Test-Path $regasm64) { $regasms += @{ Path = $regasm64; Bit = "x64" } }
if (Test-Path $regasm32) { $regasms += @{ Path = $regasm32; Bit = "x86" } }
if ($regasms.Count -eq 0) {
    Write-Host "ERROR: .NET Framework 4.x RegAsm.exe not found (need .NET Framework 4.8)." -ForegroundColor Red
    Write-Host "Install .NET Framework 4.8 from Microsoft, then re-run." -ForegroundColor Red
    Read-Host "Press Enter to close"; return
}

# --- remove Mark-of-the-Web (downloaded files are blocked; .NET refuses to  --
#     load a blocked CapeOpen.dll -> Aspen cannot create the block) ----------
if (-not $Unregister) {
    Write-Host "==> Unblocking downloaded files (Mark-of-the-Web)" -ForegroundColor Cyan
    Get-ChildItem $blocksRoot -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
}

# --- register every family -------------------------------------------------
$action  = if ($Unregister) { "Unregistering" } else { "Registering" }
Write-Host "==> $action ONE PROCESS blocks" -ForegroundColor Cyan
$okCount = 0; $failCount = 0; $blockCount = 0

Get-ChildItem $blocksRoot -Recurse -Filter "*.opblocks.json" | ForEach-Object {
    $manifest = Get-Content $_.FullName -Raw | ConvertFrom-Json
    $dll = Join-Path (Split-Path -Parent $_.FullName) $manifest.dll
    if (-not (Test-Path $dll)) { Write-Host "  MISSING $dll" -ForegroundColor Red; $failCount++; return }

    $familyOk = $true
    foreach ($ra in $regasms) {
        $flag = if ($Unregister) { "/unregister" } else { "/codebase" }
        $out = & $ra.Path "`"$dll`"" $flag 2>&1
        if ($LASTEXITCODE -ne 0) {
            $familyOk = $false
            Write-Host ("  FAILED [{0}] {1} (exit {2})" -f $ra.Bit, $manifest.dll, $LASTEXITCODE) -ForegroundColor Red
            Write-Host ("    $out") -ForegroundColor DarkGray
        }
    }
    if ($familyOk) {
        $blockCount += $manifest.blocks.Count
        Write-Host ("  {0}  {1}  ({2} blocks)" -f ($(if($Unregister){"removed "}else{"OK      "}), $manifest.dll, $manifest.blocks.Count)) -ForegroundColor Green
        $okCount++
    } else { $failCount++ }

    # apply CapeDescription metadata (install only)
    if (-not $Unregister) {
        foreach ($b in $manifest.blocks) {
            $g = "{$($b.clsid)}"
            foreach ($base in @("HKLM:\SOFTWARE\Classes\CLSID", "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID")) {
                $cd = Join-Path (Join-Path $base $g) "CapeDescription"
                if (Test-Path $cd) {
                    if ($b.capeVersion) { New-ItemProperty $cd -Name CapeVersion -Value $b.capeVersion -PropertyType String -Force | Out-Null }
                    if ($b.vendorUrl)   { New-ItemProperty $cd -Name VendorURL   -Value $b.vendorUrl   -PropertyType String -Force | Out-Null }
                }
            }
        }
    }
}

Write-Host ""
if ($failCount -eq 0) {
    if ($Unregister) {
        Write-Host "All ONE PROCESS blocks removed." -ForegroundColor Green
    } else {
        Write-Host ("SUCCESS: {0} families / {1} blocks registered." -f $okCount, $blockCount) -ForegroundColor Green
        Write-Host "Open Aspen Plus V14 -> Model Palette -> CAPE-OPEN tab to use them." -ForegroundColor Green
    }
} else {
    Write-Host ("Completed with {0} failure(s). See the red lines above." -f $failCount) -ForegroundColor Yellow
    Write-Host "Most common cause: not running as Administrator, or .NET Framework 4.8 missing." -ForegroundColor Yellow
}
if (-not $env:OPBLOCKS_NOWAIT) { Read-Host "Press Enter to close" | Out-Null }
