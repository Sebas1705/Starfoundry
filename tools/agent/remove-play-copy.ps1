<#
.SYNOPSIS
  Deletes a directory made by new-play-copy.ps1 (and its user-data folder). Refuses anything without the marker file.
.PARAMETER Path
  The copy root or its game folder, as printed by new-play-copy.ps1.
#>
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$p = [IO.Path]::GetFullPath($Path).TrimEnd('\')
if ((Split-Path $p -Leaf) -eq 'game' -and (Test-PlayCopy (Split-Path $p -Parent))) { $p = Split-Path $p -Parent }
if (-not (Test-PlayCopy $p)) { throw "Refusing: $p has no play-copy marker." }
if ($p.StartsWith((Get-RepoRoot), [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing: inside the repository.' }
$pg = Join-Path $p 'game\project.godot'
$ud = $null
if (Test-Path $pg) { $m = Select-String -Path $pg -Pattern 'custom_user_dir_name="Godot/app_userdata/(Starfoundry Agent Copy [0-9a-f]+)"'; if ($m) { $ud = Join-Path $env:APPDATA ('Godot\app_userdata\' + $m.Matches[0].Groups[1].Value) } }
Remove-Item -LiteralPath $p -Recurse -Force
if ($ud -and (Test-Path $ud)) { Remove-Item -LiteralPath $ud -Recurse -Force }
Write-Host "Removed $p"
