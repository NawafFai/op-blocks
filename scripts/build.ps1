<#
.SYNOPSIS
  Builds the OP-Blocks solution (Core + block DLLs + tests) and runs unit tests.
.PARAMETER Configuration
  Release (default) or Debug.
.PARAMETER SkipTests
  Build only, do not run the unit tests.
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> Building OPBlocks.sln ($Configuration)" -ForegroundColor Cyan
dotnet build (Join-Path $root "OPBlocks.sln") -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipTests) {
    Write-Host "==> Running unit tests" -ForegroundColor Cyan
    dotnet test (Join-Path $root "tests\UnitTests\UnitTests.csproj") -c $Configuration -v minimal --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "==> Build complete. Block DLLs:" -ForegroundColor Green
Get-ChildItem (Join-Path $root "src") -Recurse -Filter "OPBlocks.*.dll" |
    Where-Object { $_.FullName -match "\\bin\\$Configuration\\" } |
    Select-Object -ExpandProperty FullName
