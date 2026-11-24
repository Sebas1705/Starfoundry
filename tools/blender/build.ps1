# Builds the Blender-authored assets: runs the generators headless and writes the .glb files the game loads.
#   pwsh tools/blender/build.ps1 [-Preview <dir>]
# Needs Blender 4.2+ (tested with 5.3). Set $env:BLENDER to blender.exe when it is not in the default place.
param([string]$Preview = "")
$ErrorActionPreference = "Stop"
$blender = $env:BLENDER
if (-not $blender) { $blender = "C:\Program Files\Blender Foundation\Blender 5.3\blender.exe" }
if (-not (Test-Path $blender)) { throw "Blender not found at '$blender'. Set `$env:BLENDER." }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cmd = @("--background", "--factory-startup", "--python", (Join-Path $here "cockpit.py"), "--")
if ($Preview) { $cmd += @("--preview", $Preview) }
& $blender @cmd
if ($LASTEXITCODE -ne 0) { throw "Blender failed with exit code $LASTEXITCODE" }
