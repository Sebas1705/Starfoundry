<#
.SYNOPSIS
  Records a scripted game run with Godot Movie Maker and converts it to mp4 with ffmpeg.
.PARAMETER GameArgs
  Game flags (after "--"), default "--structure-demo --demo-orbit=30": the starter ship with the camera orbiting at 30 degrees a second.
  Alias: -Args. Other scripted modes with motion: --flight-shot, --streak-shot, --cockpit-shot (these quit by themselves).
.PARAMETER Seconds
  Length of the clip (movie time, so frames are exact whatever the machine speed). Default 8.
.PARAMETER Out
  Output .mp4 (default: %TEMP%\sf-movies\<stamp>.mp4). Keep it OUTSIDE the repo.
.PARAMETER Project
  A play copy's game folder; when omitted a fresh copy is made and removed at the end.
.PARAMETER Fps
  Fixed frame rate (default 30). Size is --Resolution (default 1280x720).
#>
param([Alias('Args')][string]$GameArgs = '--structure-demo --demo-orbit=30', [double]$Seconds = 8, [string]$Out, [string]$Project, [int]$Fps = 30, [string]$Resolution = '1280x720')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$made = $false
if (-not $Project) { $Project = (& (Join-Path $PSScriptRoot 'new-play-copy.ps1') | Select-Object -Last 1); $made = $true }
if (-not $Out) { $d = Join-Path $env:TEMP 'sf-movies'; New-Item -ItemType Directory -Force $d | Out-Null; $Out = Join-Path $d ("clip-{0}.mp4" -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Out = [IO.Path]::GetFullPath($Out); New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
if ($Out.StartsWith((Get-RepoRoot), [StringComparison]::OrdinalIgnoreCase)) { throw 'Write the clip outside the repository.' }
$avi = [IO.Path]::ChangeExtension($Out, '.avi')
$frames = [int][math]::Ceiling($Seconds * $Fps)
try {
    $a = @('--path', $Project, '--windowed', '--resolution', $Resolution, '--write-movie', $avi, '--fixed-fps', $Fps, '--quit-after', $frames, '--') + ($GameArgs -split '\s+' | Where-Object { $_ })
    $p = Start-Process (Get-GodotConsole) -ArgumentList $a -NoNewWindow -PassThru -Wait -RedirectStandardOutput ([IO.Path]::ChangeExtension($Out, '.log'))
    if (-not (Test-Path $avi)) { throw "Godot wrote no movie (exit $($p.ExitCode)); see $([IO.Path]::ChangeExtension($Out, '.log'))" }
    & (Get-Ffmpeg) -y -loglevel error -i $avi -c:v libx264 -crf 23 -pix_fmt yuv420p -an $Out
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
    Remove-Item $avi
    Write-Host $Out
} finally { if ($made) { & (Join-Path $PSScriptRoot 'remove-play-copy.ps1') -Path $Project | Out-Null } }
