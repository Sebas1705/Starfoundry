<#
.SYNOPSIS
  Makes a throwaway copy of the game for an agent to run and drive, builds it and prints its path.
.DESCRIPTION
  Copies game/, src/ and content/ of this checkout (read only: the checkout is never modified) into a NEW directory
  (default: %TEMP%\sf-play\<id>), writes a marker file, changes custom_user_dir_name in the copy's project.godot so the
  owner's saves and settings are untouched, and runs `dotnet build game -c Debug -m:1` there.
  Prints the project path (<copy>\game) as the last line. Remove it with remove-play-copy.ps1.
.PARAMETER Path
  Destination: must not exist or be empty.
#>
param([string]$Path)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepoRoot
$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
if (-not $Path) { $Path = Join-Path $env:TEMP "sf-play\$id" }
$Path = [IO.Path]::GetFullPath($Path)
if ($Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'The copy must live outside the repository.' }
if ((Test-Path $Path) -and (Get-ChildItem -Force $Path | Select-Object -First 1)) { throw "Not empty: $Path" }
New-Item -ItemType Directory -Force $Path | Out-Null
Set-Content -LiteralPath (Join-Path $Path $script:Marker) -Value @('starfoundry-play-copy v1', "source=$root", "created=$(Get-Date -Format o)") -Encoding ascii

foreach ($d in 'game', 'src', 'content') {
    robocopy (Join-Path $root $d) (Join-Path $Path $d) /E /XD bin obj .godot .mcp /XF '*.uid' /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $d ($LASTEXITCODE)" }
}
$pg = Join-Path $Path 'game\project.godot'
$t = Get-Content -Raw -LiteralPath $pg
$new = $t -replace 'config/custom_user_dir_name="[^"]*"', "config/custom_user_dir_name=`"Godot/app_userdata/Starfoundry Agent Copy $id`""
if ($new -eq $t) { throw 'custom_user_dir_name not found in project.godot' }
[IO.File]::WriteAllText($pg, $new, (New-Object Text.UTF8Encoding($false)))

$p = Start-Process dotnet -ArgumentList @('build', (Join-Path $Path 'game'), '-c', 'Debug', '-m:1', '-nologo', '-v:q') -NoNewWindow -PassThru -Wait
if ($p.ExitCode -ne 0) { throw "dotnet build failed ($($p.ExitCode)); copy kept at $Path" }
Write-Host (Join-Path $Path 'game')
