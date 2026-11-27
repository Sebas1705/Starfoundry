# Shared helpers for the tools/release scripts (dot-sourced). Windows PowerShell 5.1 compatible.
# Nothing here downloads anything unless a caller passes the matching -Install* switch; see docs/release.md.

. (Join-Path $PSScriptRoot '..\agent\common.ps1') # Get-RepoRoot, Get-GodotConsole

$script:GodotReleases = 'https://github.com/godotengine/godot-builds/releases'

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text ==" -ForegroundColor Cyan }

# The single source of the product version is the VERSION file at the repository root.
function Get-ProductVersion {
    $f = Join-Path (Get-RepoRoot) 'VERSION'
    if (-not (Test-Path -LiteralPath $f)) { throw "VERSION file not found: $f" }
    $v = (Get-Content -LiteralPath $f -TotalCount 1).Trim()
    if ($v -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "VERSION must be a semantic version (1.2.3 or 1.2.3-rc.1), found '$v'" }
    $v
}

# The Godot version is read from the game's SDK reference (Godot.NET.Sdk/4.7.2), so there is one place to change it.
function Get-GodotVersion {
    $csproj = Get-Content -Raw -LiteralPath (Join-Path (Get-RepoRoot) 'game\Starfoundry.Game.csproj')
    if ($csproj -notmatch 'Godot\.NET\.Sdk/(\d+\.\d+(\.\d+)?)') { throw 'Could not read the Godot version from game/Starfoundry.Game.csproj' }
    $Matches[1]
}

function Get-TemplatesDir { Join-Path $env:APPDATA ('Godot\export_templates\{0}.stable.mono' -f (Get-GodotVersion)) }

function Test-Templates { Test-Path -LiteralPath (Join-Path (Get-TemplatesDir) 'windows_release_x86_64.exe') }

function Get-FileHash256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

# Downloads one asset of the official godot-builds release, verifying its size against the GitHub API and its SHA-512 against the
# release's own SHA512-SUMS.txt. Nothing is written to the destination unless both match.
function Save-GodotAsset([string]$AssetName, [string]$Destination) {
    $ver = Get-GodotVersion
    $tag = "$ver-stable"
    $headers = @{ 'User-Agent' = 'starfoundry-release-script' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $rel = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/godotengine/godot-builds/releases/tags/$tag"
    $asset = $rel.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    $sums = $rel.assets | Where-Object { $_.name -eq 'SHA512-SUMS.txt' } | Select-Object -First 1
    if (-not $asset -or -not $sums) { throw "Release $tag of godotengine/godot-builds has no asset $AssetName (or no SHA512-SUMS.txt)." }
    $mb = [math]::Round($asset.size / 1MB, 1)
    Write-Host "Downloading $AssetName ($mb MB, $($asset.size) bytes)"
    Write-Host "  from $($asset.browser_download_url)"
    $sumText = (Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $sums.browser_download_url).Content
    if ($sumText -is [byte[]]) { $sumText = [Text.Encoding]::UTF8.GetString($sumText) }
    $line = ($sumText -split "`n" | Where-Object { $_ -match [regex]::Escape($AssetName) + '\s*$' } | Select-Object -First 1)
    if (-not $line) { throw "SHA512-SUMS.txt has no line for $AssetName" }
    $expected = ($line.Trim() -split '\s+')[0].ToLowerInvariant()
    $tmp = "$Destination.part"
    New-Item -ItemType Directory -Force (Split-Path -Parent $Destination) | Out-Null
    $oldProgress = $ProgressPreference; $ProgressPreference = 'SilentlyContinue' # the progress bar makes Invoke-WebRequest very slow in 5.1
    try { Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $asset.browser_download_url -OutFile $tmp } finally { $ProgressPreference = $oldProgress }
    if ((Get-Item -LiteralPath $tmp).Length -ne $asset.size) { Remove-Item -LiteralPath $tmp -Force; throw "Size mismatch for $AssetName" }
    $actual = (Get-FileHash -Algorithm SHA512 -LiteralPath $tmp).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { Remove-Item -LiteralPath $tmp -Force; throw "SHA-512 mismatch for $AssetName (expected $expected, got $actual)" }
    Move-Item -LiteralPath $tmp -Destination $Destination -Force
    Write-Host "  size and SHA-512 verified" -ForegroundColor Green
}

# Installs the Windows x86_64 part of the Godot Mono export templates (version.txt plus the four windows_*_x86_64 executables).
function Install-Templates {
    $ver = Get-GodotVersion
    $name = "Godot_v$ver-stable_mono_export_templates.tpz"
    $dest = Get-TemplatesDir
    $dl = Join-Path ([IO.Path]::GetTempPath()) ('sf-templates-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $dl | Out-Null
    try {
        $tpz = Join-Path $dl $name
        Save-GodotAsset $name $tpz
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        New-Item -ItemType Directory -Force $dest | Out-Null
        $zip = [IO.Compression.ZipFile]::OpenRead($tpz)
        try {
            foreach ($e in $zip.Entries) {
                if ($e.FullName -eq 'templates/version.txt' -or $e.FullName -match '^templates/windows_(release|debug)_x86_64(_console)?\.exe$') {
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $dest $e.Name), $true)
                }
            }
        } finally { $zip.Dispose() }
        Write-Host "Templates installed to $dest (Windows x86_64 only; the other platforms are not needed here)" -ForegroundColor Green
    } finally { Remove-Item -LiteralPath $dl -Recurse -Force -ErrorAction SilentlyContinue }
}

function Show-TemplatesHelp {
    $ver = Get-GodotVersion
    $dir = Get-TemplatesDir
    Write-Host "Godot $ver Mono export templates are not installed: $dir" -ForegroundColor Yellow
    Write-Host @"

They are required to produce an exe. What to get (official release page, nothing else is needed):
  asset  : Godot_v$ver-stable_mono_export_templates.tpz  (about 1.2 GB; the Windows part used here is about 220 MB unpacked)
  source : $script:GodotReleases/tag/$ver-stable   (godotengine/godot-builds)
  check  : the SHA-512 in that release's SHA512-SUMS.txt

Either let this script do it (it verifies the size and the SHA-512 and unpacks only the Windows x86_64 files):
  .\tools\release\build-windows.ps1 -InstallTemplates
or install them from the editor: Project > Export > Manage Export Templates > Download and Install.
The result must be the folder: $dir
"@
}

# Godot console executable. -Install downloads the portable Godot Mono (official asset, verified) into $ToolsDir when none is found.
function Resolve-Godot([string]$Explicit, [switch]$Install, [string]$ToolsDir) {
    if ($Explicit) { if (-not (Test-Path -LiteralPath $Explicit)) { throw "Godot not found: $Explicit" }; return (Resolve-Path -LiteralPath $Explicit).Path }
    try { return (Get-GodotConsole) } catch { }
    $ver = Get-GodotVersion
    if (-not $ToolsDir) { $ToolsDir = Join-Path $env:LOCALAPPDATA 'Starfoundry\release-tools' }
    $folder = Join-Path $ToolsDir "godot-$ver"
    $exe = Get-ChildItem -Path $folder -Recurse -Filter "Godot_v$ver*_mono_win64_console.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($exe) { return $exe.FullName }
    if (-not $Install) {
        throw "Godot $ver Mono (console exe) not found. Set GODOT_PATH, pass -Godot, or pass -InstallGodot to download the portable build (official godot-builds release, verified)."
    }
    $name = "Godot_v$ver-stable_mono_win64.zip"
    $zip = Join-Path $ToolsDir $name
    Save-GodotAsset $name $zip
    New-Item -ItemType Directory -Force $folder | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $folder)
    Remove-Item -LiteralPath $zip -Force
    $exe = Get-ChildItem -Path $folder -Recurse -Filter "Godot_v$ver*_mono_win64_console.exe" | Select-Object -First 1
    if (-not $exe) { throw "The Godot archive did not contain the console executable." }
    $exe.FullName
}

# Inno Setup compiler: INNOSETUP_PATH, PATH, the usual folders, the uninstall registry keys.
function Find-Iscc {
    if ($env:INNOSETUP_PATH -and (Test-Path -LiteralPath $env:INNOSETUP_PATH)) { return $env:INNOSETUP_PATH }
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $dirs = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) | Where-Object { $_ }
    foreach ($d in $dirs) {
        foreach ($sub in 'Inno Setup 6', 'Inno Setup 7') {
            $p = Join-Path $d "$sub\ISCC.exe"
            if (Test-Path -LiteralPath $p) { return $p }
        }
    }
    foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall') {
        $e = Get-ChildItem $k -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like 'Inno Setup*' } | Select-Object -First 1
        if ($e) {
            $loc = (Get-ItemProperty $e.PSPath).InstallLocation
            if ($loc -and (Test-Path -LiteralPath (Join-Path $loc 'ISCC.exe'))) { return (Join-Path $loc 'ISCC.exe') }
        }
    }
    $null
}

function Show-InnoHelp {
    Write-Host 'Inno Setup 6 (the installer compiler, free) is not installed.' -ForegroundColor Yellow
    Write-Host @'

Install it yourself (official site https://jrsoftware.org/isdl.php, or the winget package):
  winget install --id JRSoftware.InnoSetup -e --scope user
or let this script do it with -InstallInnoSetup (it runs exactly that winget command, falling back to
`choco install innosetup -y` when winget is missing; the installer comes from jrsoftware.org through the package manager).
'@
}

function Install-InnoSetup {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Host 'Installing Inno Setup: winget install --id JRSoftware.InnoSetup -e --scope user (package source: winget community repository, download from jrsoftware.org)' -ForegroundColor Yellow
        & winget install --id JRSoftware.InnoSetup -e --scope user --silent --accept-package-agreements --accept-source-agreements | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "winget exited with code $LASTEXITCODE; retrying machine-wide (may need admin)..." -ForegroundColor Yellow
            & winget install --id JRSoftware.InnoSetup -e --silent --accept-package-agreements --accept-source-agreements | Out-Host
        }
    } elseif (Get-Command choco -ErrorAction SilentlyContinue) {
        Write-Host 'Installing Inno Setup: choco install innosetup -y (package source: Chocolatey community repository)' -ForegroundColor Yellow
        & choco install innosetup -y --no-progress | Out-Host
    } else { throw 'Neither winget nor choco is available to install Inno Setup.' }
    $iscc = Find-Iscc
    if (-not $iscc) { throw 'Inno Setup was installed but ISCC.exe was not found; set INNOSETUP_PATH.' }
    $iscc
}

# Writes SHA256SUMS.txt (GNU style: "<hash> *<name>") for the given files, names relative to the folder of the sums file.
function Write-Sha256Sums([string]$SumsFile, [string[]]$Files) {
    $base = Split-Path -Parent $SumsFile
    $lines = foreach ($f in $Files) {
        $rel = $f.Substring($base.Length).TrimStart('\', '/') -replace '\\', '/'
        '{0} *{1}' -f (Get-FileHash256 $f), $rel
    }
    [IO.File]::WriteAllText($SumsFile, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}

# Runs a Godot console exe, collects stdout and stderr and returns @{ ExitCode; Output } (no window: callers pass --headless).
function Invoke-Capture([string]$Exe, [string[]]$Arguments, [hashtable]$Environment, [int]$TimeoutSeconds = 300) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = Split-Path -Parent $Exe
    if ($Environment) { foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] } }
    $p = New-Object Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    $out = $p.StandardOutput.ReadToEndAsync() # read both pipes asynchronously so neither can fill up and block the child
    $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        try { $p.Kill() } catch { }
        return @{ ExitCode = -1; Output = "TIMEOUT after $TimeoutSeconds s" }
    }
    $p.WaitForExit()
    @{ ExitCode = $p.ExitCode; Output = $out.Result + $err.Result }
}
