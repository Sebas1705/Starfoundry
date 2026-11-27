<#
.SYNOPSIS
  Lists the files inside a Godot .pck (format 2 to 4, not encrypted), one "path<TAB>size" object per file. Used by the release scripts to
  check that content, fonts and credits are in the pack and that dev-only files are not.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Pck)

$ErrorActionPreference = 'Stop'
$fs = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Pck).Path)
try {
    $r = New-Object IO.BinaryReader $fs
    if ([Text.Encoding]::ASCII.GetString($r.ReadBytes(4)) -ne 'GDPC') { throw "Not a Godot pack (no GDPC magic): $Pck" }
    $format = $r.ReadUInt32()
    if ($format -lt 2 -or $format -gt 4) { throw "Unsupported pack format $format" }
    [void]$r.ReadUInt32(); [void]$r.ReadUInt32(); [void]$r.ReadUInt32() # engine major, minor, patch
    $flags = $r.ReadUInt32()
    if ($flags -band 1) { throw 'The pack directory is encrypted.' }
    $fileBase = $r.ReadUInt64()
    $dirOffset = $r.ReadUInt64()
    $fs.Position = [int64]$dirOffset
    $count = $r.ReadUInt32()
    if ($count -gt 200000) { throw "Implausible file count $count; the pack format may have changed." }
    for ($i = 0; $i -lt $count; $i++) {
        $len = $r.ReadUInt32()
        $path = [Text.Encoding]::UTF8.GetString($r.ReadBytes([int]$len)).TrimEnd([char]0)
        [void]$r.ReadUInt64()          # offset
        $size = $r.ReadUInt64()
        [void]$r.ReadBytes(16)         # md5
        [void]$r.ReadUInt32()          # flags
        [pscustomobject]@{ Path = $path; Size = [int64]$size }
    }
} finally { $fs.Dispose() }
