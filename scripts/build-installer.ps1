<#
.SYNOPSIS
  Builds the end-user distribution for ONE PROCESS Blocks:
    1. publishes the Manager (self-contained win-x64),
    2. stages Manager + blocks + templates + register script + README,
    3. compiles OPBlocks_Setup.exe with Inno Setup (if installed),
    4. also zips a portable copy.
  Output: installer\Output\OPBlocks_Setup.exe  and  build\OPBlocks-portable.zip
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root  = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $root "build\stage\app"

Write-Host "==> Publishing Manager (self-contained)" -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
dotnet publish (Join-Path $root "src\OPBlocksManager\OPBlocksManager.csproj") -c $Configuration `
    -r win-x64 --self-contained true -p:PublishSingleFile=false -o $stage -v minimal --nologo | Out-Null

Write-Host "==> Staging blocks / templates / scripts / docs" -ForegroundColor Cyan
Copy-Item (Join-Path $root "blocks")            (Join-Path $stage "blocks")    -Recurse -Force
Copy-Item (Join-Path $root "installer\templates") (Join-Path $stage "templates") -Recurse -Force

# Native DWSIM adapter -> stage\dwsim (what the Manager's "Enable in DWSIM" copies
# into %LOCALAPPDATA%\DWSIM\unitops). Built net48; skip gracefully if the DWSIM
# build output is absent on this machine (e.g. no DWSIM SDK layout to compile against).
$dwsimBin = Join-Path $root "src\OPBlocks.DWSIM\bin\$Configuration"
$adapter  = Join-Path $dwsimBin "OPBlocks.DWSIM.dll"
if (Test-Path $adapter) {
    $dwsimStage = Join-Path $stage "dwsim"
    New-Item -ItemType Directory -Force -Path $dwsimStage | Out-Null
    Copy-Item (Join-Path $dwsimBin "OPBlocks*.dll") $dwsimStage -Force
    $cape = Join-Path $root "libs\CapeOpen\CapeOpen.dll"
    if (Test-Path $cape) { Copy-Item $cape $dwsimStage -Force }
    Write-Host "    staged native DWSIM adapter -> $dwsimStage" -ForegroundColor DarkGray
} else {
    Write-Warning "Native DWSIM adapter not built ($adapter) - 'Enable in DWSIM' won't ship. Build src\OPBlocks.DWSIM first."
}
New-Item -ItemType Directory -Force -Path (Join-Path $stage "scripts") | Out-Null
Copy-Item (Join-Path $root "scripts\register-all-blocks.ps1") (Join-Path $stage "scripts") -Force
Copy-Item (Join-Path $root "installer\README-EndUser.txt") (Join-Path $stage "README.txt") -Force
# one-click portable install/uninstall wrappers (bypass ExecutionPolicy, self-elevate)
Copy-Item (Join-Path $root "installer\portable\INSTALL.bat")   (Join-Path $stage "INSTALL.bat")   -Force
Copy-Item (Join-Path $root "installer\portable\UNINSTALL.bat") (Join-Path $stage "UNINSTALL.bat") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stage "docs") | Out-Null
Copy-Item (Join-Path $root "docs\block-catalog.html") (Join-Path $stage "docs") -Force -ErrorAction SilentlyContinue

Write-Host "==> Portable ZIP" -ForegroundColor Cyan
$zip = Join-Path $root "build\OPBlocks-1.1.2-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "    $zip" -ForegroundColor DarkGray

Write-Host "==> Inno Setup" -ForegroundColor Cyan
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc (Join-Path $root "installer\OPBlocks.iss")
    Write-Host "==> Setup: $(Join-Path $root 'installer\Output\OPBlocks_Setup.exe')" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup (ISCC.exe) not found - portable ZIP built, but OPBlocks_Setup.exe was not compiled."
}
