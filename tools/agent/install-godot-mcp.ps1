<#
.SYNOPSIS
  Installs godot-mcp-runtime 4.0.1 (pinned) OUTSIDE the repo and prints the .mcp.json snippet to register it.
.DESCRIPTION
  npm install --ignore-scripts into %LOCALAPPDATA%\sf-tools\godot-mcp-runtime, then checks the tarball integrity recorded
  in package-lock.json against the hash in docs/prototypes/godot-mcp-eval.md. Registers nothing (the owner does that).
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$ver = '4.0.1'
$expect = 'sha512-8jzL6Vnp5nQ5MJMceUrWfPUd+nu1y60yEixCz3TlurBcsHAlCV2NHWahd2+RXK+ntMnG55/BPZXm0kOmXSGUBg=='
$dir = Join-Path $env:LOCALAPPDATA 'sf-tools\godot-mcp-runtime'
New-Item -ItemType Directory -Force $dir | Out-Null
if (-not (Test-Path (Join-Path $dir 'package.json'))) { Set-Content (Join-Path $dir 'package.json') '{"name":"sf-godot-mcp","private":true}' -Encoding ascii }
& npm install "godot-mcp-runtime@$ver" --ignore-scripts --no-audit --no-fund --save-exact --prefix $dir | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
$js = "const l=require(process.argv[1]).packages['node_modules/godot-mcp-runtime'];console.log(l.version+' '+l.integrity)"
$got = (& node -e $js (Join-Path $dir 'package-lock.json')).Trim()
if ($got -ne "$ver $expect") { throw "Version or integrity mismatch: $got" }
$entry = (Join-Path $dir 'node_modules\godot-mcp-runtime\dist\index.js')
if (-not (Test-Path $entry)) { throw 'dist/index.js missing' }
Write-Host "OK godot-mcp-runtime $ver integrity verified at $dir"
$cfg = [ordered]@{ mcpServers = [ordered]@{ 'godot-runtime' = [ordered]@{ command = 'node'; args = @($entry); env = [ordered]@{ GODOT_PATH = (Get-GodotConsole); GODOT_MCP_DISABLE_ELICITATION = 'true' } } } }
Write-Host ($cfg | ConvertTo-Json -Depth 6)
