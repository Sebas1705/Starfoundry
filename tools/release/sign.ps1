<#
.SYNOPSIS
  Optional Authenticode signing hook. Does nothing, successfully, unless signing secrets are configured.

.DESCRIPTION
  No certificate, key or token lives in this repository. Two modes, chosen by which environment variables exist (in CI they come from
  GitHub Actions secrets of the same names; locally from Doppler, e.g. `doppler run -p starfoundry -c release -- ...`; names only, values
  are never printed):

  Azure Trusted Signing (recommended, cloud-held key, no .pfx):
    SF_SIGN_AZURE_ENDPOINT   e.g. https://eus.codesigning.azure.net
    SF_SIGN_AZURE_ACCOUNT    the Trusted Signing account name
    SF_SIGN_AZURE_PROFILE    the certificate profile name
    SF_SIGN_DLIB             full path to Azure.CodeSigning.Dlib.dll (from the Microsoft.Trusted.Signing.Client package)
    AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET   a service principal with the "Trusted Signing Certificate Profile Signer" role

  A code-signing certificate as a .pfx:
    SF_SIGN_PFX_BASE64       the .pfx, base64 encoded
    SF_SIGN_PFX_PASSWORD     its password
    SF_SIGN_TIMESTAMP_URL    optional, default http://timestamp.digicert.com

  SignTool.exe comes with the Windows SDK (pre-installed on windows-latest runners); SF_SIGNTOOL can point at it.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Files)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    if ($env:SF_SIGNTOOL -and (Test-Path -LiteralPath $env:SF_SIGNTOOL)) { return $env:SF_SIGNTOOL }
    $c = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $f = Get-ChildItem -Path $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($f) { $f.FullName } else { $null }
}

$azure = $env:SF_SIGN_AZURE_ENDPOINT -and $env:SF_SIGN_AZURE_ACCOUNT -and $env:SF_SIGN_AZURE_PROFILE -and $env:SF_SIGN_DLIB
$pfx = [bool]$env:SF_SIGN_PFX_BASE64
if (-not ($azure -or $pfx)) {
    Write-Host 'Signing skipped: no signing secrets are configured (SF_SIGN_AZURE_* or SF_SIGN_PFX_BASE64). The artifacts are unsigned; see docs/release.md.' -ForegroundColor Yellow
    return
}
$signtool = Find-SignTool
if (-not $signtool) { throw 'Signing secrets are present but SignTool.exe was not found (install the Windows SDK or set SF_SIGNTOOL).' }
$ts = if ($env:SF_SIGN_TIMESTAMP_URL) { $env:SF_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }

$tmp = $null
try {
    if ($azure) {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('sf-sign-' + [guid]::NewGuid().ToString('N') + '.json')
        $meta = @{ Endpoint = $env:SF_SIGN_AZURE_ENDPOINT; CodeSigningAccountName = $env:SF_SIGN_AZURE_ACCOUNT; CertificateProfileName = $env:SF_SIGN_AZURE_PROFILE }
        [IO.File]::WriteAllText($tmp, ($meta | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        $base = @('sign', '/v', '/fd', 'sha256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'sha256', '/dlib', $env:SF_SIGN_DLIB, '/dmdf', $tmp)
    } else {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('sf-sign-' + [guid]::NewGuid().ToString('N') + '.pfx')
        [IO.File]::WriteAllBytes($tmp, [Convert]::FromBase64String($env:SF_SIGN_PFX_BASE64))
        $base = @('sign', '/fd', 'sha256', '/tr', $ts, '/td', 'sha256', '/f', $tmp, '/p', $env:SF_SIGN_PFX_PASSWORD)
    }
    foreach ($f in $Files) {
        Write-Host "Signing $(Split-Path -Leaf $f)"
        & $signtool @base $f
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed for $f (exit code $LASTEXITCODE)" }
    }
    foreach ($f in $Files) { & $signtool verify /pa $f | Out-Null; if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $f" } }
    Write-Host 'Signed and verified.' -ForegroundColor Green
} finally {
    if ($tmp -and (Test-Path -LiteralPath $tmp)) { Remove-Item -LiteralPath $tmp -Force }
}
