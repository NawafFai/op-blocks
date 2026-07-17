<#
.SYNOPSIS
  Corrects the informational CapeDescription registry values after RegAsm.

.DESCRIPTION
  The CO-LaN CapeOpen.dll registration writes the palette-critical keys correctly
  (Name, Description, the CapeUnitOperation CATID, InprocServer32) but shifts the
  informational ones - it puts the VendorURL into the CapeVersion value and leaves
  VendorURL empty. This step reads each block's actual [CapeVersion]/[CapeVendorURL]
  /[CapeHelpURL] attributes and writes them to the correct CapeDescription values in
  BOTH the x64 and WOW6432 (x86) hives. Must run elevated (writes HKLM\...\Classes).

  Called automatically by register.ps1; safe to run standalone after registration.
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Get-AttrValue($type, $attrName, $propName, $fallback) {
    $a = @($type.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq $attrName })
    if ($a.Count -eq 0) { return $fallback }
    $v = $a[0].GetType().GetProperty($propName).GetValue($a[0])
    if ($null -eq $v) { return $fallback } else { return [string]$v }
}

$dlls = Get-ChildItem (Join-Path $root "src") -Recurse -Filter "OPBlocks.*.dll" |
    Where-Object { $_.FullName -match "\\bin\\$Configuration\\" -and $_.Name -ne "OPBlocks.Core.dll" }

foreach ($dll in $dlls) {
    $asm = [System.Reflection.Assembly]::LoadFrom($dll.FullName)
    foreach ($t in $asm.GetTypes()) {
        if (-not $t.IsClass) { continue }
        $isBlock = @($t.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'CapeNameAttribute' }).Count -gt 0
        if (-not $isBlock) { continue }

        $ver  = Get-AttrValue $t 'CapeVersionAttribute'   'Version'   '1.0'
        $url  = Get-AttrValue $t 'CapeVendorURLAttribute' 'VendorURL' ''
        $help = Get-AttrValue $t 'CapeHelpURLAttribute'   'HelpURL'   ''
        $guid = '{' + $t.GUID.ToString() + '}'

        foreach ($clsidBase in @("HKLM:\SOFTWARE\Classes\CLSID", "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID")) {
            $cd = Join-Path (Join-Path $clsidBase $guid) 'CapeDescription'
            if (Test-Path $cd) {
                New-ItemProperty -Path $cd -Name 'CapeVersion' -Value $ver  -PropertyType String -Force | Out-Null
                New-ItemProperty -Path $cd -Name 'VendorURL'   -Value $url  -PropertyType String -Force | Out-Null
                New-ItemProperty -Path $cd -Name 'HelpURL'     -Value $help -PropertyType String -Force | Out-Null
                Write-Host "  fixed CapeDescription for $($t.Name) [$([IO.Path]::GetFileName($clsidBase))]: CapeVersion=$ver VendorURL=$url"
            }
        }
    }
}
