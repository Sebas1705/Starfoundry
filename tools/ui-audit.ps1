<#
.SYNOPSIS
  Runs the in-game text-fit audit (--ui-audit) for Spanish and English, the campaign and a scenario game, and fails when any text overflows.
.PARAMETER Godot
  Path to the Godot 4.7 Mono console executable (default: $env:GODOT, then the winget install location).
.PARAMETER Sizes
  Window sizes to audit, comma separated.
.EXAMPLE
  pwsh tools/ui-audit.ps1 -Sizes 1280x720
#>
param(
  [string]$Godot = $env:GODOT,
  [string]$Sizes = "800x600,1024x600,1280x720,1920x1080",
  [string]$Out = ""
)
$ErrorActionPreference = "Stop"
if (-not $Godot) {
  $Godot = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
}
$project = Join-Path (Split-Path $PSScriptRoot -Parent) "game"
if (-not $Out) { $Out = Join-Path ([IO.Path]::GetTempPath()) "starfoundry-ui-audit" }
New-Item -ItemType Directory -Force $Out | Out-Null
dotnet build $project -c Debug -m:1 | Out-Null
$failed = 0
foreach ($lang in "es", "en") {
  foreach ($mode in @(@(), @("--scenario=03_under_siege"))) {
    $tag = "$lang" + $(if ($mode.Count) { ".scenario" } else { ".campaign" })
    $log = Join-Path $Out "$tag.log"
    $args = @("--path", $project, "--resolution", "1280x720", "--", "--ui-audit", "--ui-lang=$lang", "--audit-sizes=$Sizes") + $mode
    $p = Start-Process -FilePath $Godot -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    try { $p.PriorityClass = "BelowNormal" } catch {}
    $p.WaitForExit()
    $result = Select-String -Path $log -Pattern "UIAUDIT RESULT" | Select-Object -Last 1
    $fails = (Select-String -Path $log -Pattern "UIAUDIT .* FAIL ").Count
    if (-not $result) { Write-Host "[$tag] no result (the run did not finish); see $log"; $failed++ }
    else { Write-Host "[$tag] $($result.Line.Trim())"; if ($fails -gt 0) { $failed++; Select-String -Path $log -Pattern "UIAUDIT .* FAIL " | ForEach-Object { Write-Host "  $($_.Line)" } } }
  }
}
if ($failed -gt 0) { Write-Host "ui-audit: FAILED ($failed run(s)); logs in $Out"; exit 1 }
Write-Host "ui-audit: clean (logs in $Out)"
