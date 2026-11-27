<#
.SYNOPSIS
  Draws the Starfoundry application icon procedurally and writes game/assets/icon/starfoundry.ico and icon.png.

.DESCRIPTION
  The logo is our own drawing (no third-party art, nothing to credit): a deep-space tile, a ring in the UI accent blue and a
  four-pointed amber spark, the "foundry fire", at its centre. The .ico holds PNG-compressed images at 16, 24, 32, 48, 64, 128 and 256 px.
  The output is committed; run this script again only to change the design. Windows PowerShell 5.1 compatible (System.Drawing).
#>
[CmdletBinding()]
param([string]$OutDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'game\assets\icon' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-LogoBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$Size

    # Rounded tile with a vertical gradient.
    $r = $s * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 18, 28, 52)), ([System.Drawing.Color]::FromArgb(255, 6, 10, 22)), 90.0
    $g.FillPath($bg, $path)

    # A few fixed stars (skipped when the icon is too small to show them).
    if ($Size -ge 48) {
        $star = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 190, 215, 255))
        foreach ($p in @(@(0.18, 0.2, 0.012), @(0.8, 0.16, 0.009), @(0.86, 0.72, 0.012), @(0.14, 0.78, 0.009), @(0.62, 0.9, 0.008))) {
            $rr = [Math]::Max(1.0, $s * $p[2])
            $g.FillEllipse($star, [float]($s * $p[0] - $rr), [float]($s * $p[1] - $rr), [float]($rr * 2), [float]($rr * 2))
        }
        $star.Dispose()
    }

    # The ring in the UI accent blue (0.35, 0.65, 1.0).
    $ringW = [Math]::Max(1.5, $s * 0.07)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 89, 166, 255)), ([float]$ringW)
    $m = $s * 0.2
    $g.DrawEllipse($pen, [float]$m, [float]$m, [float]($s - 2 * $m), [float]($s - 2 * $m))

    # The four-pointed amber spark at the centre.
    $c = $s / 2
    $long = $s * 0.30
    $short = $s * 0.075
    $pts = @(
        (New-Object System.Drawing.PointF $c, ($c - $long)), (New-Object System.Drawing.PointF ($c + $short), ($c - $short)),
        (New-Object System.Drawing.PointF ($c + $long), $c), (New-Object System.Drawing.PointF ($c + $short), ($c + $short)),
        (New-Object System.Drawing.PointF $c, ($c + $long)), (New-Object System.Drawing.PointF ($c - $short), ($c + $short)),
        (New-Object System.Drawing.PointF ($c - $long), $c), (New-Object System.Drawing.PointF ($c - $short), ($c - $short))
    )
    $fire = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 176, 64))
    $g.FillPolygon($fire, [System.Drawing.PointF[]]$pts)
    $core = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 236, 200))
    $cr = $s * 0.05
    $g.FillEllipse($core, [float]($c - $cr), [float]($c - $cr), [float]($cr * 2), [float]($cr * 2))

    $g.Dispose(); $bg.Dispose(); $pen.Dispose(); $fire.Dispose(); $core.Dispose(); $path.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray(); $ms.Dispose()
    , $bytes
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @()
foreach ($sz in $sizes) {
    $bmp = New-LogoBitmap $sz
    $images += , @{ Size = $sz; Png = (Get-PngBytes $bmp) }
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO container: ICONDIR (6 bytes) + one ICONDIRENTRY (16 bytes) per image + the PNG payloads.
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $dim = if ($i.Size -ge 256) { 0 } else { $i.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$i.Png.Length); $w.Write([uint32]$offset)
    $offset += $i.Png.Length
}
foreach ($i in $images) { $w.Write($i.Png) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'starfoundry.ico'), $ms.ToArray())
$w.Dispose()
Write-Host "Icon written to $OutDir (starfoundry.ico, icon.png)"
