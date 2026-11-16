# Draws the app icon (two game windows side by side on the accent colour) and saves it as MhoCoop.ico.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }

    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x6C, 0x5C, 0xF0))
    $dark   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1E, 0x1E, 0x1E))
    $light  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xED, 0xED, 0xF0))

    $g.FillPath($accent, (RoundRect (2*$s) (2*$s) (60*$s) (60*$s) (12*$s)))
    # two screens
    $g.FillPath($dark, (RoundRect (9*$s) (15*$s) (21*$s) (34*$s) (3*$s)))
    $g.FillPath($dark, (RoundRect (34*$s) (15*$s) (21*$s) (34*$s) (3*$s)))
    # a player dot in each
    $g.FillEllipse($light, [single](15.5*$s), [single](27*$s), [single](8*$s), [single](8*$s))
    $g.FillEllipse($light, [single](40.5*$s), [single](27*$s), [single](8*$s), [single](8*$s))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @()
foreach ($sz in $sizes) { $images += ,(New-IconPng $sz) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'MhoCoop.ico'), $out.ToArray())
"Wrote MhoCoop.ico"
