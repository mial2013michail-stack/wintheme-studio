# Generates the original WinTheme Studio application icon (no third-party artwork).
# Produces src\WinThemeStudio\Assets\app.ico and docs\app-icon.png.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\WinThemeStudio\Assets'
$docs = Join-Path $root 'docs'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
New-Item -ItemType Directory -Path $docs -Force | Out-Null

function New-RoundedRectPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-StarPath {
    param([float]$Scale)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.StartFigure()
    # Four-point sparkle with softly curved sides (original artwork).
    $p.AddBezier(128*$Scale, 38*$Scale, 137*$Scale, 102*$Scale, 154*$Scale, 119*$Scale, 218*$Scale, 128*$Scale)
    $p.AddBezier(218*$Scale, 128*$Scale, 154*$Scale, 137*$Scale, 137*$Scale, 154*$Scale, 128*$Scale, 218*$Scale)
    $p.AddBezier(128*$Scale, 218*$Scale, 119*$Scale, 154*$Scale, 102*$Scale, 137*$Scale, 38*$Scale, 128*$Scale)
    $p.AddBezier(38*$Scale, 128*$Scale, 102*$Scale, 119*$Scale, 119*$Scale, 102*$Scale, 128*$Scale, 38*$Scale)
    $p.CloseFigure()
    return $p
}

function New-IconPngBytes {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size / 256.0
    $outer = New-RoundedRectPath (10*$s) (10*$s) (236*$s) (236*$s) (58*$s)

    $rect = New-Object System.Drawing.RectangleF (10*$s), (10*$s), (236*$s), (236*$s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 124, 92, 255),
        [System.Drawing.Color]::FromArgb(255, 36, 132, 255),
        45.0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(255, 124, 92, 255),
        [System.Drawing.Color]::FromArgb(255, 84, 113, 255),
        [System.Drawing.Color]::FromArgb(255, 36, 168, 255)
    )
    $blend.Positions = @(0.0, 0.55, 1.0)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $outer)

    # Soft top-left highlight.
    $g.SetClip($outer)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF 0, 0, $Size, $Size),
        [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90.0)
    $g.FillEllipse($hl, -0.25*$Size, -0.45*$Size, 1.1*$Size, 0.95*$Size)
    $g.ResetClip()

    # Sparkle.
    $star = New-StarPath $s
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillPath($white, $star)

    # Small companion sparkle.
    $small = New-Object System.Drawing.Drawing2D.GraphicsPath
    $small.AddEllipse(182*$s, 52*$s, 20*$s, 20*$s)
    $g.FillPath($white, $small)

    # Inner border.
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255), (2.0*$s))
    $g.DrawPath($pen, $outer)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()

    $pen.Dispose(); $white.Dispose(); $star.Dispose(); $hl.Dispose(); $brush.Dispose(); $outer.Dispose()
    $g.Dispose(); $bmp.Dispose(); $ms.Dispose()

    return ,$bytes
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$images = @()
foreach ($size in $sizes) {
    $images += ,(New-IconPngBytes -Size $size)
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $images[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$dim)     # width
    $bw.Write([Byte]$dim)     # height
    $bw.Write([Byte]0)        # palette
    $bw.Write([Byte]0)        # reserved
    $bw.Write([UInt16]1)      # planes
    $bw.Write([UInt16]32)     # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Flush()

$icoPath = Join-Path $assets 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

# Preview PNG for documentation.
$preview = New-IconPngBytes -Size 512
[System.IO.File]::WriteAllBytes((Join-Path $docs 'app-icon.png'), $preview)

Write-Host "Icon written: $icoPath"
