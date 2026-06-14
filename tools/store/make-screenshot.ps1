<#
    Generates a Microsoft Store listing screenshot (1366x768 PNG — the Store minimum) by composing
    the committed brand assets (social preview banner) on a dark canvas matching the app's palette.
    Output: assets/store/screenshot-1366x768.png

    The Store requires at least one screenshot >= 1366x768. You can also upload real app screenshots
    (e.g. the ones in docs/images/) directly in Partner Center; this just guarantees one compliant asset.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root   = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # repo root
$Assets = Join-Path $Root 'assets'
$Banner = Join-Path $Assets 'social-preview-1280x640.png'
$OutDir = Join-Path $Assets 'store'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$OutFile = Join-Path $OutDir 'screenshot-1366x768.png'

if (-not (Test-Path $Banner)) { throw "Brand asset not found: $Banner" }

$W = 1366; $H = 768
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

try {
    # Background — vertical gradient matching the social preview's dark navy palette.
    $top  = [System.Drawing.Color]::FromArgb(23, 27, 38)   # #171b26
    $bot  = [System.Drawing.Color]::FromArgb(13, 16, 23)   # #0d1017
    $rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bot, 90)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    function Draw-CenteredImage($path, $targetW, $y) {
        $img = [System.Drawing.Image]::FromFile($path)
        try {
            $h = [int]($targetW * $img.Height / $img.Width)
            $x = [int](($W - $targetW) / 2)
            $g.DrawImage($img, $x, $y, $targetW, $h)
            return $y + $h
        } finally { $img.Dispose() }
    }

    function Draw-CenteredText($text, $font, $color, $y) {
        $size = $g.MeasureString($text, $font)
        $x = ($W - $size.Width) / 2
        $sb = New-Object System.Drawing.SolidBrush($color)
        $g.DrawString($text, $font, $sb, $x, $y)
        $sb.Dispose()
        return $y + $size.Height
    }

    # Social preview banner (1280x640) scaled to 1120 wide, centered near the top.
    $afterBanner = Draw-CenteredImage $Banner 1120 70

    # Feature caption lines.
    $white = [System.Drawing.Color]::White
    $grey  = [System.Drawing.Color]::FromArgb(170, 178, 190)
    $h1    = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold)
    $h2    = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Regular)
    $afterT1 = Draw-CenteredText 'Slideshow  ·  Panel navigation  ·  One-key Move / Copy / Rename / Delete' $h1 $white ($afterBanner + 26)
    [void](Draw-CenteredText 'H.264 + LibVLC playback  ·  Ambilight background  ·  Offline OCR translation' $h2 $grey ($afterT1 + 8))
    $h1.Dispose(); $h2.Dispose()

    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $OutFile ($W x $H)"
}
finally {
    $g.Dispose(); $bmp.Dispose()
}
