<#
.SYNOPSIS
  Tests the installer end to end without a window and without touching the owner's saves: silent install into a temp folder, checks,
  headless smoke test of the installed copy, an in-place upgrade over it, silent uninstall, check of the clean removal.

.DESCRIPTION
  Uses the documented silent flags: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="..." /LOG="..." (uninstall: unins000.exe /VERYSILENT).
  Per-user installs write to HKCU (the Apps & features entry) and to the current user's Start Menu; both are checked and must be gone after the uninstall.
  Refuses to run when Starfoundry is already installed for this user (same AppId): it would replace the real installation.
  The game is started only through smoke-test.ps1, which redirects APPDATA to a temp folder.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Setup,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$Setup = (Resolve-Path -LiteralPath $Setup).Path
$appId = '{B3F1D3C2-7A55-4E0B-9C61-5E2F7D0A4B18}'
$regKeys = @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1", "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1")
foreach ($k in $regKeys) { if (Test-Path $k) { throw "Starfoundry is already installed on this machine ($k). Uninstall it first; this test must not replace a real installation." } }

$root = Join-Path ([IO.Path]::GetTempPath()) ('sf-install-' + [guid]::NewGuid().ToString('N'))
$dir = Join-Path $root 'Starfoundry'
New-Item -ItemType Directory -Force $root | Out-Null
$log = Join-Path $root 'install.log'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Starfoundry.lnk'
$failures = New-Object Collections.Generic.List[string]
function Check([string]$what, [bool]$ok) {
    Write-Host ("  {0,-62} {1}" -f $what, $(if ($ok) { 'ok' } else { 'FAILED' })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { $failures.Add($what) }
}
function Install([string]$what) {
    $p = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$dir`"", "/LOG=`"$log`"") -Wait -PassThru
    Check "$what exit code 0" ($p.ExitCode -eq 0)
}
function Wait-Gone([string]$path, [int]$seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Test-Path -LiteralPath $path) -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 500 }
    -not (Test-Path -LiteralPath $path)
}

try {
    Write-Host "Installer test: $Setup"
    Write-Host "  install folder (temporary): $dir"

    Install 'silent install'
    foreach ($f in 'Starfoundry.exe', 'Starfoundry.console.exe', 'Starfoundry.pck', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'unins000.exe', 'data_Starfoundry.Game_windows_x86_64\Starfoundry.Game.dll', 'licenses\inter-OFL.txt') {
        Check "installed: $f" (Test-Path -LiteralPath (Join-Path $dir $f))
    }
    $reg = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
    $regOk = Test-Path $reg
    Check 'uninstall entry (Apps & features) written for the current user' $regOk
    if ($regOk) {
        $entry = Get-ItemProperty $reg
        Check "uninstall entry says version $ExpectedVersion" ((-not $ExpectedVersion) -or ($entry.DisplayVersion -eq $ExpectedVersion))
        Check 'uninstall entry points into the install folder' ($entry.InstallLocation -like "$dir*")
    }
    Check 'Start Menu shortcut created' (Test-Path -LiteralPath $startMenu)
    Check 'no Desktop shortcut by default' (-not (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Starfoundry.lnk')))
    $ver = (Get-Item -LiteralPath (Join-Path $dir 'Starfoundry.exe')).VersionInfo
    Check "exe version info: Starfoundry $($ver.ProductVersion)" ($ver.ProductName -eq 'Starfoundry' -and ((-not $ExpectedVersion) -or $ver.ProductVersion -eq $ExpectedVersion))

    Write-Host '  smoke test of the installed copy:'
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -Dir $dir -ExpectedVersion $ExpectedVersion
    Check 'installed copy passes the headless smoke test' ($LASTEXITCODE -eq 0)

    # Upgrade in place: a stale assembly of an "older version" must be gone afterwards and the install must succeed over a used folder.
    $stale = Join-Path $dir 'data_Starfoundry.Game_windows_x86_64\stale-from-old-version.dll'
    Set-Content -LiteralPath $stale -Value 'old'
    Install 'in-place upgrade (same folder)'
    Check 'upgrade removed the stale file of the old version' (-not (Test-Path -LiteralPath $stale))
    Check 'upgrade kept a single uninstall entry' (@(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { $_.PSChildName -like "${appId}*" }).Count -eq 1)

    Write-Host '  uninstalling (unins000.exe /VERYSILENT):'
    $un = Start-Process -FilePath (Join-Path $dir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    Check 'silent uninstall exit code 0' ($un.ExitCode -eq 0)
    # unins000.exe hands over to a temporary copy of itself and returns at once, so wait for the folder to disappear.
    Check 'install folder removed' (Wait-Gone $dir 90)
    Check 'uninstall entry removed' (-not (Test-Path $reg))
    Check 'Start Menu shortcut removed' (-not (Test-Path -LiteralPath $startMenu))
}
finally {
    $un = Join-Path $dir 'unins000.exe'
    if (Test-Path -LiteralPath $un) { try { Start-Process -FilePath $un -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait | Out-Null; Wait-Gone $dir 60 | Out-Null } catch { } }
    if ($failures.Count -gt 0 -and (Test-Path -LiteralPath $log)) { Write-Host '  last lines of the installer log:'; Get-Content -LiteralPath $log -Tail 15 | ForEach-Object { Write-Host "    | $_" } }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host 'INSTALLER TEST FAILED' -ForegroundColor Red; exit 1 }
Write-Host 'INSTALLER TEST PASSED' -ForegroundColor Green
exit 0
