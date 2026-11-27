<#
.SYNOPSIS
  One command from a checkout to the Windows release artifacts: the exe, the portable zip, the installer and their SHA-256 sums.

.DESCRIPTION
  1.  reads the version from VERSION (single source) and checks Godot, the export templates and (for the installer) Inno Setup,
  2.  builds the C# solution (dotnet build, one node, no shared compilation server),
  3.  exports the "Windows Desktop" preset of game/export_presets.cfg headless (release, pack beside the exe),
  4.  stamps the icon and the version information into the exes (Set-ExeResources.ps1: Win32 resource API, no rcedit download),
  5.  adds the licence, the third-party notices and the font licences, then checks the pack (content, fonts, credits in; dev files out),
  6.  runs the headless smoke test (smoke-test.ps1) on the result,
  7.  writes artifacts/windows/Starfoundry-<version>-windows-x64.zip and SHA256SUMS.txt,
  8.  compiles the Inno Setup installer to artifacts/installer/Starfoundry-<version>-setup.exe with its SHA256SUMS.txt.

  Layout (all git-ignored):
    artifacts/windows/Starfoundry/                      the game folder (exe, console exe, pck, data_* assemblies, licences)
    artifacts/windows/Starfoundry-<v>-windows-x64.zip   portable build, one top-level folder
    artifacts/windows/SHA256SUMS.txt
    artifacts/installer/Starfoundry-<v>-setup.exe
    artifacts/installer/SHA256SUMS.txt

  Nothing is downloaded unless you pass an -Install* switch. Missing prerequisites make the script print exactly what to get and exit 2.
  See docs/release.md.

.PARAMETER Godot             Godot 4.x Mono console exe (default: $env:GODOT_PATH, then the winget location).
.PARAMETER InstallGodot      Download the portable Godot Mono (official godot-builds asset, size and SHA-512 verified) when none is found.
.PARAMETER InstallTemplates  Download the Windows export templates (official asset, verified) when missing.
.PARAMETER InstallInnoSetup  Install Inno Setup with winget (JRSoftware.InnoSetup) or choco when missing.
.PARAMETER NoInstaller       Stop after the zip.
.PARAMETER NoSmoke           Skip the headless smoke test.
.PARAMETER TestInstaller     Also run test-installer.ps1 (silent install into a temp folder, smoke test, uninstall).
.PARAMETER Out               Output root (default: artifacts under the repository root).
.PARAMETER Sign              Sign the exes and the installer when SignTool and the documented secrets are present (see docs/release.md).
#>
[CmdletBinding()]
param(
    [string]$Godot,
    [switch]$InstallGodot,
    [switch]$InstallTemplates,
    [switch]$InstallInnoSetup,
    [switch]$NoInstaller,
    [switch]$NoSmoke,
    [switch]$TestInstaller,
    [switch]$Sign,
    [string]$Out
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Get-RepoRoot
$game = Join-Path $repo 'game'
if (-not $Out) { $Out = Join-Path $repo 'artifacts' }
$Out = [IO.Path]::GetFullPath($Out)
$winDir = Join-Path $Out 'windows'
$stage = Join-Path $winDir 'Starfoundry'
$instDir = Join-Path $Out 'installer'
$exe = Join-Path $stage 'Starfoundry.exe'
$timings = [ordered]@{}
function Mark([string]$name, [scriptblock]$body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $body
    $timings[$name] = $sw.Elapsed
}

# ---- 0. keep the machine calm: one build node, no resident compiler servers --------------------------------------------------------
if (-not $env:DOTNET_PROCESSOR_COUNT) { $env:DOTNET_PROCESSOR_COUNT = '2' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:UseSharedCompilation = 'false' # no resident Roslyn server: it would inherit Godot's output pipe and keep the export hanging
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'

# ---- 1. preflight ---------------------------------------------------------------------------------------------------------------
Write-Step 'Preflight'
$version = Get-ProductVersion
Write-Host "Version     : $version (VERSION file)"
$godotExe = Resolve-Godot -Explicit $Godot -Install:$InstallGodot -ToolsDir $env:SF_TOOLS_DIR
Write-Host "Godot       : $godotExe"
if (-not (Test-Templates)) {
    if ($InstallTemplates) { Write-Step 'Installing the Godot export templates'; Install-Templates }
    else { Show-TemplatesHelp; exit 2 }
}
Write-Host "Templates   : $(Get-TemplatesDir)"
$iscc = $null
if (-not $NoInstaller) {
    $iscc = Find-Iscc
    if (-not $iscc) {
        if ($InstallInnoSetup) { Write-Step 'Installing Inno Setup'; $iscc = Install-InnoSetup }
        else { Show-InnoHelp; Write-Host 'Re-run with -InstallInnoSetup, or with -NoInstaller to build only the exe and the zip.'; exit 2 }
    }
    Write-Host "Inno Setup  : $iscc"
}
$tmpContent = Join-Path $game 'content'
$tmpSln = Join-Path $game 'Starfoundry.Game.sln'
if (Test-Path -LiteralPath $tmpSln) { throw "$tmpSln already exists; remove it first (it is a temporary, git-ignored file made by this script)." }
if (Test-Path -LiteralPath $tmpContent) { throw "$tmpContent already exists; remove it first (it is a temporary, git-ignored copy made by this script)." }

# ---- 2. clean outputs ----------------------------------------------------------------------------------------------------------
if (Test-Path -LiteralPath $winDir) { Remove-Item -LiteralPath $winDir -Recurse -Force }
if (Test-Path -LiteralPath $instDir) { Remove-Item -LiteralPath $instDir -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

try {
    # ---- 3. dotnet build --------------------------------------------------------------------------------------------------------
    Mark 'dotnet build' {
        Write-Step 'Building the solution (dotnet build, Release)'
        & dotnet build (Join-Path $game 'Starfoundry.Game.csproj') -c Release -m:1 -p:UseSharedCompilation=false -nologo -v:q -clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
    }

    # ---- 4. export --------------------------------------------------------------------------------------------------------------
    Mark 'godot export' {
        Write-Step 'Exporting the "Windows Desktop" preset (release)'
        # A project can only export files inside itself, and content/ is a sibling of game/: copy it for the export (docs/packaging.md).
        Copy-Item -LiteralPath (Join-Path $repo 'content') -Destination $tmpContent -Recurse
        # The exporter wants game/<assembly>.sln. A permanent one would make `dotnet build game` ambiguous (a folder with a project and a
        # solution), so a throw-away solution that holds just the game project exists only for the export.
        & dotnet new sln -n Starfoundry.Game -o $game --force | Out-Null
        & dotnet sln $tmpSln add (Join-Path $game 'Starfoundry.Game.csproj') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the temporary solution for the export' }
        $imp = Invoke-Capture $godotExe @('--headless', '--path', $game, '--import') $null 600
        if ($imp.ExitCode -ne 0) { Write-Host $imp.Output; throw "Godot --import failed ($($imp.ExitCode))" }
        $exp = Invoke-Capture $godotExe @('--headless', '--path', $game, '--export-release', 'Windows Desktop', $exe) $null 1800
        ($exp.Output -split "`n" | Where-Object { $_ -match 'ERROR|WARNING: .*export' } | Select-Object -First 20) | ForEach-Object { Write-Host "  | $($_.TrimEnd())" }
        if ($exp.ExitCode -ne 0) { Write-Host $exp.Output; throw "Godot export failed (exit code $($exp.ExitCode))" }
    }
}
finally {
    if (Test-Path -LiteralPath $tmpContent) { Remove-Item -LiteralPath $tmpContent -Recurse -Force }
    if (Test-Path -LiteralPath $tmpSln) { Remove-Item -LiteralPath $tmpSln -Force }
}

$pck = Join-Path $stage 'Starfoundry.pck'
$consoleExe = Join-Path $stage 'Starfoundry.console.exe'
foreach ($f in $exe, $pck, $consoleExe) { if (-not (Test-Path -LiteralPath $f)) { throw "The export did not produce $f" } }
$dataDir = Get-ChildItem -LiteralPath $stage -Directory -Filter 'data_*' | Select-Object -First 1
if (-not $dataDir) { throw 'The export did not produce the data_* folder with the C# assemblies.' }
if (-not (Test-Path -LiteralPath (Join-Path $dataDir.FullName 'Starfoundry.Game.dll'))) { throw "Starfoundry.Game.dll is missing from $($dataDir.Name)" }

# ---- 5. icon, version information, licences -----------------------------------------------------------------------------------------
Mark 'stamp and licences' {
    Write-Step 'Stamping icon and version information'
    $ico = Join-Path $game 'assets\icon\starfoundry.ico'
    & (Join-Path $PSScriptRoot 'Set-ExeResources.ps1') -Exe $exe -Icon $ico -Version $version -FileDescription 'Starfoundry' -OriginalFilename 'Starfoundry.exe'
    & (Join-Path $PSScriptRoot 'Set-ExeResources.ps1') -Exe $consoleExe -Icon $ico -Version $version -FileDescription 'Starfoundry (console)' -OriginalFilename 'Starfoundry.console.exe' -InternalName 'Starfoundry.console'

    Write-Step 'Adding licences and credits'
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
    $licDir = Join-Path $stage 'licenses'
    New-Item -ItemType Directory -Force $licDir | Out-Null
    foreach ($d in Get-ChildItem -LiteralPath (Join-Path $game 'assets\fonts') -Directory) {
        foreach ($t in Get-ChildItem -LiteralPath $d.FullName -Filter '*.txt') { Copy-Item -LiteralPath $t.FullName -Destination (Join-Path $licDir ('{0}-{1}' -f $d.Name, $t.Name)) }
    }
    # THIRD-PARTY-NOTICES.txt is generated from ASSETS.md (the source of the in-game Credits page), so the two cannot drift.
    $rows = Get-Content -LiteralPath (Join-Path $repo 'ASSETS.md') | Where-Object { $_ -match '^\| `' } | ForEach-Object { , @(($_.Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() }) }
    $n = New-Object Text.StringBuilder
    [void]$n.AppendLine("Starfoundry $version - third-party notices")
    [void]$n.AppendLine('')
    [void]$n.AppendLine('Starfoundry is released under the MIT licence (LICENSE.txt). It bundles the following third-party components.')
    [void]$n.AppendLine('The same list is on the Credits page of the game (main menu and Settings).')
    [void]$n.AppendLine('')
    [void]$n.AppendLine('Bundled fonts and sounds (full licence texts in the licenses folder):')
    foreach ($r in $rows) {
        [void]$n.AppendLine("  - $($r[1]): $($r[5])")
        [void]$n.AppendLine("      Author: $($r[4]); licence: $($r[3]); source: $($r[2]); file: $(($r[0] -replace '`', ''))")
    }
    [void]$n.AppendLine('')
    [void]$n.AppendLine('Engine and runtime:')
    [void]$n.AppendLine('  - Godot Engine 4.7 (.NET): MIT licence, Copyright (c) 2014-present Godot Engine contributors, Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur. https://godotengine.org/license (the engine bundles further third-party components, listed there).')
    [void]$n.AppendLine('  - .NET runtime (Microsoft): MIT licence. https://dotnet.microsoft.com')
    [void]$n.AppendLine('')
    [void]$n.AppendLine('Original art, 3D models, sounds and the application icon were made for this project.')
    [IO.File]::WriteAllText((Join-Path $stage 'THIRD-PARTY-NOTICES.txt'), $n.ToString(), (New-Object Text.UTF8Encoding($false)))
    $readme = @"
Starfoundry $version (portable build)

Run Starfoundry.exe. Starfoundry.console.exe is the same game with a console window for logs and bug reports.
Keep Starfoundry.pck and the data_* folder next to the exe; the game does not need to be installed.
Saves and settings live outside this folder: %APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype
Delete this folder to remove the game; that user folder is yours to keep or delete.

LICENSE.txt and THIRD-PARTY-NOTICES.txt list the licences. Unsigned build: Windows SmartScreen may warn on first run (More info, Run anyway).
"@
    [IO.File]::WriteAllText((Join-Path $stage 'README.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))
}

# ---- 6. optional signing --------------------------------------------------------------------------------------------------------------
if ($Sign) { & (Join-Path $PSScriptRoot 'sign.ps1') -Files @($exe, $consoleExe) }

# ---- 7. check the pack ----------------------------------------------------------------------------------------------------------------
Write-Step 'Checking the pack'
$files = @(& (Join-Path $PSScriptRoot 'Get-PckFiles.ps1') -Pck $pck)
$paths = $files | ForEach-Object { $_.Path -replace '^res://', '' }
# Imported resources (fonts, models, scenes) appear as <name>.import plus converted data under .godot/imported, scenes as <name>.remap.
$required = 'content/core.json', 'content/ship_handling.json', 'content/cockpit_layouts.json', 'content/scenarios/campaign.json', 'assets/credits.tsv',
    'assets/fonts/inter/Inter-Variable.ttf.import', 'assets/fonts/rajdhani/Rajdhani-SemiBold.ttf.import', 'assets/fonts/jetbrains-mono/JetBrainsMono-Regular.ttf.import',
    'assets/fonts/inter/OFL.txt', 'assets/fonts/rajdhani/OFL.txt', 'assets/fonts/jetbrains-mono/OFL.txt', 'assets/models/ship.glb.import', 'MainMenu.tscn.remap', 'Play.tscn.remap'
$missing = $required | Where-Object { $paths -notcontains $_ }
# C# files are 1-byte stubs in the pack (script_export_mode and include_scripts_content=false); a real size means source leaked.
$leaked = $files | Where-Object { $_.Path -match '\.cs$' -and $_.Size -gt 16 } | ForEach-Object { $_.Path }
$forbidden = @($paths | Where-Object { $_ -match '\.uid$|\.md$|^Gallery\.tscn|^Main\.tscn|^assets/icon/|\.csproj$|\.sln$|^docs/|^tools/|^tests/|^src/' }) + @($leaked)
Write-Host ('Pack        : {0} files, {1:n1} MB of content' -f $files.Count, (($files | Measure-Object Size -Sum).Sum / 1MB))
if ($missing) { throw ('The pack is missing: ' + ($missing -join ', ')) }
if ($forbidden) { throw ('The pack contains files that must not ship: ' + (($forbidden | Select-Object -First 10) -join ', ')) }
Write-Host 'Pack check  : required files present, no dev-only files' -ForegroundColor Green

# ---- 8. smoke ----------------------------------------------------------------------------------------------------------------------
if (-not $NoSmoke) {
    Mark 'smoke test' {
        Write-Step 'Smoke test (headless)'
        & (Join-Path $PSScriptRoot 'smoke-test.ps1') -Dir $stage -ExpectedVersion $version
        if ($LASTEXITCODE -ne 0) { throw 'Smoke test failed' }
    }
}

# ---- 9. zip + checksums -----------------------------------------------------------------------------------------------------------
Mark 'zip' {
    Write-Step 'Writing the portable zip'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $winDir "Starfoundry-$version-windows-x64.zip"
    $top = "Starfoundry-$version"
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem -LiteralPath $stage -Recurse -File) {
            $rel = $f.FullName.Substring($stage.Length).TrimStart('\') -replace '\\', '/'
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, "$top/$rel", [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }
    Write-Sha256Sums (Join-Path $winDir 'SHA256SUMS.txt') @($zipPath)
}
$zipPath = Join-Path $winDir "Starfoundry-$version-windows-x64.zip"

# ---- 10. installer --------------------------------------------------------------------------------------------------------------------
$setup = $null
if (-not $NoInstaller) {
    Mark 'installer' {
        Write-Step 'Compiling the installer (Inno Setup)'
        & (Join-Path $PSScriptRoot 'build-installer.ps1') -Stage $stage -OutDir $instDir -Iscc $iscc -Version $version -Sign:$Sign
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    }
    $setup = Join-Path $instDir "Starfoundry-$version-setup.exe"
    if ($TestInstaller) {
        Mark 'installer test' {
            Write-Step 'Testing the installer (silent install, smoke, silent uninstall)'
            & (Join-Path $PSScriptRoot 'test-installer.ps1') -Setup $setup -ExpectedVersion $version
            if ($LASTEXITCODE -ne 0) { throw 'Installer test failed' }
        }
    }
}

# ---- 11. summary ------------------------------------------------------------------------------------------------------------------------
Write-Step 'Done'
function Size([string]$p) { if ($p -and (Test-Path -LiteralPath $p)) { '{0,8:n1} MB' -f ((Get-Item -LiteralPath $p).Length / 1MB) } else { '       -' } }
$dataBytes = (Get-ChildItem -LiteralPath $dataDir.FullName -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ('  Starfoundry.exe          {0}' -f (Size $exe))
Write-Host ('  Starfoundry.console.exe  {0}' -f (Size $consoleExe))
Write-Host ('  Starfoundry.pck          {0}' -f (Size $pck))
Write-Host ('  {0,-24} {1,8:n1} MB (C# assemblies and the .NET runtime files)' -f $dataDir.Name, ($dataBytes / 1MB))
Write-Host ('  game folder total        {0,8:n1} MB' -f ((Get-ChildItem -LiteralPath $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Host ('  portable zip             {0}  {1}' -f (Size $zipPath), $zipPath)
if ($setup) { Write-Host ('  installer                {0}  {1}' -f (Size $setup), $setup) }
foreach ($k in $timings.Keys) { Write-Host ('  time {0,-18} {1:mm\:ss}' -f $k, $timings[$k]) }
Write-Host ('  total time               {0:mm\:ss}' -f $clock.Elapsed)

& dotnet build-server shutdown *> $null
