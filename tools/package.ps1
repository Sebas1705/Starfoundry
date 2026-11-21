<#
.SYNOPSIS
  Builds a standalone Windows release of Starfoundry (an exe with the content inside).

.DESCRIPTION
  1. builds the C# solution (fails fast on errors),
  2. copies the repository's content/ folder into game/content (git-ignored; a project can only export files inside itself),
  3. runs Godot headless with the "Windows Desktop" preset from game/export_presets.cfg,
  4. removes the temporary copy, even when the export fails.

  Needs the Godot 4.7.2 Mono export templates (one-time install, see docs/packaging.md). Without them no exe can be produced:
  the script says so and exits with code 2 before changing anything.

.PARAMETER Out
  Output folder (default: build/windows under the repository root).
.PARAMETER Godot
  Path to the Godot console executable. Defaults to $env:GODOT or the winget install location.
.PARAMETER DebugBuild
  Export a debug build instead of a release build.
#>
[CmdletBinding()]
param(
    [string]$Out,
    [string]$Godot,
    [switch]$DebugBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$game = Join-Path $repo 'game'
if (-not $Out) { $Out = Join-Path $repo 'build/windows' }

if (-not $Godot) { $Godot = $env:GODOT }
if (-not $Godot) {
    $Godot = Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
}
if (-not (Test-Path $Godot)) { throw "Godot console executable not found: $Godot (pass -Godot or set `$env:GODOT)" }

# The templates must match the editor version exactly.
$templates = Join-Path $env:APPDATA 'Godot/export_templates/4.7.2.stable.mono'
$templateExe = Join-Path $templates 'windows_release_x86_64.exe'
if (-not (Test-Path $templateExe)) {
    Write-Host "Godot 4.7.2 Mono export templates are not installed: $templates" -ForegroundColor Yellow
    Write-Host @"

One-time install (about 1 GB on disk, the download is about 1 GB):
  Option A (editor): open game/ in Godot 4.7.2 Mono > Project > Export... > Manage Export Templates > Download and Install.
  Option B (manual): download Godot_v4.7.2-stable_mono_export_templates.tpz from
    https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable
  (or godotengine.org/download/archive/4.7.2-stable), then Editor > Manage Export Templates > Install From File.
  The result must be the folder: $templates

Run this script again afterwards.
"@
    exit 2
}

Write-Host '== Building the solution =='
dotnet build (Join-Path $game 'Starfoundry.Game.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$tmp = Join-Path $game 'content'
if (Test-Path $tmp) { throw "$tmp already exists; remove it first (it is a temporary, git-ignored copy)." }

New-Item -ItemType Directory -Force $Out | Out-Null
$exe = Join-Path $Out 'Starfoundry.exe'
$mode = if ($DebugBuild) { '--export-debug' } else { '--export-release' }

try {
    Write-Host '== Copying content/ into game/content =='
    Copy-Item (Join-Path $repo 'content') $tmp -Recurse

    Write-Host "== Exporting ($mode) to $exe =="
    & $Godot --headless --path $game --import
    & $Godot --headless --path $game $mode 'Windows Desktop' $exe
    if ($LASTEXITCODE -ne 0) { throw "Godot export failed (exit code $LASTEXITCODE)" }
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
}

if (-not (Test-Path $exe)) { throw "Export finished but $exe was not created." }
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB). Smoke test: & '$exe' --quit-after 300" -ForegroundColor Green
