# Shared helpers for the tools/agent scripts (dot-sourced). Windows PowerShell 5.1 compatible.
$script:Marker = '.sf-play-copy'

function Get-RepoRoot { (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }

function Get-GodotConsole {
    if ($env:GODOT_PATH -and (Test-Path $env:GODOT_PATH)) { return $env:GODOT_PATH }
    $pk = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $g = Get-ChildItem -Path $pk -Recurse -Filter 'Godot_v4.7*_mono_win64_console.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $g) { throw 'Godot 4.7 Mono console exe not found; set GODOT_PATH.' }
    $g.FullName
}

function Get-Ffmpeg {
    $c = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $pk = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $f = Get-ChildItem -Path $pk -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $f) { throw 'ffmpeg not found (winget install Gyan.FFmpeg).' }
    $f.FullName
}

# True only for a directory that new-play-copy.ps1 created (marker file with the expected header).
function Test-PlayCopy([string]$Path) {
    $m = Join-Path $Path $script:Marker
    (Test-Path -LiteralPath $m) -and ((Get-Content -LiteralPath $m -TotalCount 1) -eq 'starfoundry-play-copy v1')
}
