<#
.SYNOPSIS
  Headless smoke test of a packaged Starfoundry folder (the exported game, the unpacked zip or an installed copy). No window opens.

.DESCRIPTION
  Runs the console executable with a throw-away APPDATA so the owner's saves and settings are never touched, and checks:
    1. --headless --quit-after 60                 the main menu scene loads and quits cleanly (exit code 0, no script errors);
    2. --headless -- --self-check                 content/core.json and the scenarios resolve, the credits data, the three fonts with
                                                  their licence texts and a model are in the pack (prints SELFCHECK lines, exit 0);
    3. --headless -- --blueprint-check            the game scene starts from the packaged content and its self-check prints no FAIL
                                                  (a short play phase: it builds a ship and a station and runs checks on them).
  The scripted --agent playthrough (tools/agent/) drives a project folder through the Godot MCP bridge and cannot attach to an export:
  step 3 is the packaged-build substitute (see docs/release.md).

.PARAMETER Dir      Folder that holds Starfoundry.console.exe.
.PARAMETER SkipPlay Skip step 3.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Dir,
    [string]$ExpectedVersion,
    [switch]$SkipPlay
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$Dir = (Resolve-Path -LiteralPath $Dir).Path
$console = Join-Path $Dir 'Starfoundry.console.exe'
if (-not (Test-Path -LiteralPath $console)) { throw "Starfoundry.console.exe not found in $Dir" }
if (-not (Test-Path -LiteralPath (Join-Path $Dir 'Starfoundry.pck'))) { throw "Starfoundry.pck not found in $Dir" }

$fakeAppData = Join-Path ([IO.Path]::GetTempPath()) ('sf-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fakeAppData | Out-Null
$envs = @{ APPDATA = $fakeAppData }
$failed = New-Object Collections.Generic.List[string]

function Test-Output([string]$Name, $Result, [string[]]$MustContain, [string[]]$MustNotMatch) {
    $ok = $true
    if ($Result.ExitCode -ne 0) { $failed.Add("$Name exit code $($Result.ExitCode)"); $ok = $false }
    foreach ($m in $MustContain) { if ($Result.Output -notmatch [regex]::Escape($m)) { $failed.Add("$Name output lacks '$m'"); $ok = $false } }
    foreach ($m in $MustNotMatch) { if ($Result.Output -match $m) { $failed.Add("$Name output matches /$m/: " + ($Matches[0])); $ok = $false } }
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  {0,-34} {1}" -f $Name, $(if ($ok) { 'ok' } else { 'FAILED' })) -ForegroundColor $color
    if (-not $ok) { ($Result.Output -split "`n" | Select-Object -Last 25) | ForEach-Object { Write-Host "    | $_" } }
}

try {
    Write-Host "Smoke test of $Dir (APPDATA redirected to $fakeAppData)"

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r1 = Invoke-Capture $console @('--headless', '--quit-after', '60') $envs 120
    Test-Output 'main menu (--quit-after 60)' $r1 @() @('SCRIPT ERROR', 'Unhandled exception', 'Content not available', 'Failed loading resource')
    Write-Host ("    {0:n1} s" -f $sw.Elapsed.TotalSeconds)

    $r2 = Invoke-Capture $console @('--headless', '--', '--self-check') $envs 120
    $must = @('SELFCHECK PASS', 'SELFCHECK content=')
    if ($ExpectedVersion) { $must += "SELFCHECK version=$ExpectedVersion" }
    Test-Output 'self-check (content, credits, fonts)' $r2 $must @('SELFCHECK FAIL')
    ($r2.Output -split "`n" | Where-Object { $_ -match '^SELFCHECK' }) | ForEach-Object { Write-Host "    $($_.TrimEnd())" }

    if (-not $SkipPlay) {
        $sw.Restart()
        $r3 = Invoke-Capture $console @('--headless', '--', '--blueprint-check') $envs 300
        $pass = ([regex]::Matches($r3.Output, 'BLUEPRINTCHECK PASS')).Count
        $fail = ([regex]::Matches($r3.Output, 'BLUEPRINTCHECK FAIL')).Count
        Test-Output 'play phase (--blueprint-check)' $r3 @() @('SCRIPT ERROR', 'Unhandled exception')
        if ($fail -gt 0) { $failed.Add("play phase printed $fail FAIL lines") }
        if ($pass -eq 0) { $failed.Add('play phase printed no PASS lines') }
        Write-Host ("    PASS lines: {0}, FAIL lines: {1}, {2:n1} s" -f $pass, $fail, $sw.Elapsed.TotalSeconds)
    }

    $created = @(Get-ChildItem -Path $fakeAppData -Recurse -File -ErrorAction SilentlyContinue)
    Write-Host "  files the game wrote under the fake APPDATA: $($created.Count)"
} finally {
    Remove-Item -LiteralPath $fakeAppData -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed.Count -gt 0) { Write-Host 'SMOKE FAILED' -ForegroundColor Red; $failed | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }; exit 1 }
Write-Host 'SMOKE PASSED' -ForegroundColor Green
exit 0
