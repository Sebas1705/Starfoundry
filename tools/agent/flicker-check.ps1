<#
.SYNOPSIS
  Prints a flicker score for a clip of a steadily moving or static camera.
.DESCRIPTION
  For each pair of consecutive frames ffmpeg computes the mean absolute luma difference (tblend difference + signalstats).
  A steady orbit gives a flat series; flicker (z-fighting, popping edges, shimmering textures) shows as spikes. Prints
    meanDiff   mean difference (0-255 luma): how much changes per frame in total
    spike      p99 / median of the per-frame difference: 1.0 is perfectly steady, above about 1.5 something flickers
    jitter     mean |d(t+1) - d(t)| / meanDiff: the frame-to-frame wobble of that difference
.PARAMETER Video
  The clip (mp4/avi).
#>
param([Parameter(Mandatory)][string]$Video)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$tmp = Join-Path $env:TEMP ("flk-{0}.txt" -f [guid]::NewGuid().ToString('N'))
try {
    $vf = "tblend=all_mode=difference,signalstats,metadata=print:key=lavfi.signalstats.YAVG:file='" + ($tmp -replace '\', '/' -replace ':', '\:') + "'"
    & (Get-Ffmpeg) -y -loglevel error -i $Video -vf $vf -f null - 
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
    $v = @(Select-String -Path $tmp -Pattern 'YAVG=([0-9.]+)' | ForEach-Object { [double]::Parse($_.Matches[0].Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) })
    $v = $v | Select-Object -Skip 1 # the first frame has nothing to blend with
    if ($v.Count -lt 3) { throw 'Too few frames' }
    $s = $v | Sort-Object
    $mean = ($v | Measure-Object -Average).Average
    $med = $s[[int]($s.Count / 2)]; $p99 = $s[[math]::Min($s.Count - 1, [int]($s.Count * 0.99))]
    $j = 0.0; for ($i = 1; $i -lt $v.Count; $i++) { $j += [math]::Abs($v[$i] - $v[$i - 1]) }; $j = $j / ($v.Count - 1)
    $inv = [Globalization.CultureInfo]::InvariantCulture
    Write-Host ("frames={0} meanDiff={1} spike={2} jitter={3}" -f $v.Count, $mean.ToString('F3', $inv), ($p99 / [math]::Max($med, 1e-6)).ToString('F2', $inv), ($j / [math]::Max($mean, 1e-6)).ToString('F3', $inv))
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
