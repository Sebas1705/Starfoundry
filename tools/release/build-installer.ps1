<#
.SYNOPSIS
  Compiles the Inno Setup installer for an exported game folder and writes its SHA-256 sums file.

.PARAMETER Stage    The exported game folder (artifacts/windows/Starfoundry).
.PARAMETER OutDir   Where the setup exe goes (artifacts/installer).
.PARAMETER Iscc     Path of ISCC.exe (found automatically when omitted).
.PARAMETER Sign     Sign the installer (needs SignTool and the secrets documented in docs/release.md; see sign.ps1).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Iscc,
    [string]$Version,
    [switch]$InstallInnoSetup,
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $Version) { $Version = Get-ProductVersion }
if (-not $Iscc) { $Iscc = Find-Iscc }
if (-not $Iscc) {
    if ($InstallInnoSetup) { $Iscc = Install-InnoSetup } else { Show-InnoHelp; exit 2 }
}
$repo = Get-RepoRoot
$Stage = (Resolve-Path -LiteralPath $Stage).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$ico = Join-Path $repo 'game\assets\icon\starfoundry.ico'

$isccArgs = @("/DAppVersion=$Version", "/DSourceDir=$Stage", "/DOutputDir=$OutDir", "/DIconFile=$ico", '/Qp')
$isccArgs += (Join-Path $PSScriptRoot 'installer.iss')
Write-Host "ISCC: $Iscc"
& $Iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $OutDir "Starfoundry-$Version-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "The installer was not produced: $setup" }
# The setup exe is signed after compiling (the uninstaller inside it stays unsigned; see docs/release.md).
if ($Sign) { & (Join-Path $PSScriptRoot 'sign.ps1') -Files @($setup) }
Write-Sha256Sums (Join-Path $OutDir 'SHA256SUMS.txt') @($setup)
Write-Host ('Installer: {0} ({1:n1} MB)' -f $setup, ((Get-Item -LiteralPath $setup).Length / 1MB))
exit 0
