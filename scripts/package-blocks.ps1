<#
.SYNOPSIS
  Assembles the runtime block library under blocks\ : for every block family
  (each *.opblocks.json under src\), copies the built DLLs + CapeOpen.dll and the
  manifest into blocks\<Family>\. This is the folder the Manager reads and
  registers block DLLs from (a stable /codebase path, not the transient bin).
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$blocksRoot = Join-Path $root "blocks"

Get-ChildItem (Join-Path $root "src") -Recurse -Filter "*.opblocks.json" | ForEach-Object {
    $manifestPath = $_.FullName
    $projDir = Split-Path -Parent $manifestPath
    $binDir  = Join-Path $projDir "bin\$Configuration"
    if (-not (Test-Path $binDir)) { Write-Warning "No build output for $($_.Name) at $binDir - build first."; return }

    $family = (Get-Content $manifestPath -Raw | ConvertFrom-Json).family
    $dest = Join-Path $blocksRoot $family
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    Copy-Item (Join-Path $binDir "*.dll") $dest -Force
    Copy-Item $manifestPath $dest -Force
    # Deploy each block's equipment icon (PNG) next to the DLL for the in-host editor.
    $pngDir = Join-Path $root "icons\png"
    $manifestObj = Get-Content $manifestPath -Raw | ConvertFrom-Json
    foreach ($b in $manifestObj.blocks) {
        $png = Join-Path $pngDir "$($b.code).png"
        if (Test-Path $png) { Copy-Item $png $dest -Force }
    }
    Write-Host "packaged '$family' -> $dest" -ForegroundColor Green
    Get-ChildItem $dest | Select-Object -ExpandProperty Name | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
}
