<#
.SYNOPSIS
  Runs a command at Idle priority, pinned to the LAST TWO logical processors, with small .NET footprints, so an agent run
  cannot disturb something else on the machine (a game with anti-cheat, for example). Children inherit both settings.
.DESCRIPTION
  Sets DOTNET_PROCESSOR_COUNT=2 and switches off compiler/build servers for the child, starts it, then sets
  PriorityClass=Idle and ProcessorAffinity=<last two cores> before it can spawn work. Waits and returns its exit code.
.PARAMETER Exe
  Program to run (full path or a name on PATH).
.PARAMETER ArgLine
  The argument line, as one string (quote paths yourself).
.PARAMETER Out
  Optional file receiving stdout (stderr goes to "<Out>.err").
.EXAMPLE
  powershell -File tools/agent/quiet.ps1 -Exe node -ArgLine 'tools/agent/playthrough.mjs ...' -Out run.log
#>
param([Parameter(Mandatory)][string]$Exe, [string]$ArgLine = '', [string]$Out)
$ErrorActionPreference = 'Stop'
$n = [Environment]::ProcessorCount
if ($n -lt 3) { $mask = [int64]((1 -shl $n) - 1) } else { $mask = [int64]3 -shl ($n - 2) }   # last two logical cores
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# Owner rule: nothing may open a window, flash a console or take focus. The child always starts with a hidden window style;
# there is deliberately no switch to turn that off. Output must be redirected to files, because a hidden window has no console to read.
if (-not $Out) { $Out = Join-Path $env:TEMP ('sf-quiet-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.log') }
$sp = @{ FilePath = $Exe; WindowStyle = 'Hidden'; PassThru = $true }
if ($sp.WindowStyle -ne 'Hidden') { throw 'quiet.ps1 refuses to start a process without a hidden window style.' }
if ($ArgLine) { $sp.ArgumentList = $ArgLine }
if ($Out) { $sp.RedirectStandardOutput = $Out; $sp.RedirectStandardError = "$Out.err" }
$p = Start-Process @sp
try { $p.PriorityClass = 'Idle'; $p.ProcessorAffinity = [IntPtr]$mask } catch { Write-Warning "could not throttle: $_" }
$p.WaitForExit()
exit $p.ExitCode
