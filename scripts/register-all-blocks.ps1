<#
.SYNOPSIS
  Registers EVERY block family DLL in the deployment blocks\ folder (x64 + x86),
  then applies the correct CapeDescription metadata from each manifest. Elevated.
  Mirrors what the Manager's Install button does, in one bulk pass.
.PARAMETER Unregister  Unregister instead.
#>
param([switch]$Unregister)
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$blocksRoot = Join-Path $root "blocks"
$regasm64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$regasm32 = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
$catid = "{678C09A5-7D66-11D2-A67D-00105A42887F}"

Get-ChildItem $blocksRoot -Recurse -Filter "*.opblocks.json" | ForEach-Object {
    $manifest = Get-Content $_.FullName -Raw | ConvertFrom-Json
    $dll = Join-Path (Split-Path -Parent $_.FullName) $manifest.dll
    if (-not (Test-Path $dll)) { Write-Host "MISSING $dll" -ForegroundColor Red; return }

    foreach ($ra in @($regasm64, $regasm32)) {
        $bit = if ($ra -like '*Framework64*') { 'x64' } else { 'x86' }
        $args = if ($Unregister) { @("`"$dll`"", "/unregister") } else { @("`"$dll`"", "/codebase") }
        & $ra @args | Out-Null
        Write-Host ("{0} [{1}] {2}" -f ($(if($Unregister){"unreg"}else{"reg"}), $bit, $manifest.dll))
    }

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
Write-Host "Done." -ForegroundColor Green
