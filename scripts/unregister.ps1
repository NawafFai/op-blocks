<#
.SYNOPSIS
  Convenience wrapper: unregisters all OP-Blocks block DLLs from both COM hives.
  Equivalent to `register.ps1 -Unregister`.
#>
param([string]$Configuration = "Release")
& (Join-Path $PSScriptRoot "register.ps1") -Configuration $Configuration -Unregister
