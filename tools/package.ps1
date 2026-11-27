<#
.SYNOPSIS
  Kept for old habits: builds the exe and the portable zip (no installer). The real pipeline is tools/release/build-windows.ps1.

.DESCRIPTION
  Forwards to `tools/release/build-windows.ps1 -NoInstaller`, which exports the game, stamps the icon and version, smoke-tests the
  export and writes artifacts/windows/ (see docs/release.md). Run that script directly for the installer.
.PARAMETER Out
  Output root (default: artifacts under the repository root).
.PARAMETER Godot
  Path to the Godot console executable.
#>
[CmdletBinding()]
param(
    [string]$Out,
    [string]$Godot,
    [switch]$InstallTemplates
)
$ErrorActionPreference = 'Stop'
$forward = @{ NoInstaller = $true }
if ($Out) { $forward.Out = $Out }
if ($Godot) { $forward.Godot = $Godot }
if ($InstallTemplates) { $forward.InstallTemplates = $true }
& (Join-Path $PSScriptRoot 'release\build-windows.ps1') @forward
exit $LASTEXITCODE
