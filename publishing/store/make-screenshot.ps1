<#
    Generates Microsoft Store listing images in all required sizes and in English and Russian.

    Output (assets/store/):
      screenshot-EN-1366x768.png   screenshot-RU-1366x768.png
      screenshot-EN-720x1080.png   screenshot-RU-720x1080.png
      screenshot-EN-1080x1080.png  screenshot-RU-1080x1080.png
      logo-300x300.png
      logo-150x150.png
      logo-71x71.png
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Banner  = Join-Path $Root 'assets\social-preview-1280x640.png'
$Icon    = Join-Path $Root 'assets\icons\store-icon-256.png'
$OutDir  = Join-Path $Root 'assets\store'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$Dark1 = [System.Drawing.Color]::FromArgb(23, 27, 38)    # #171b26
$Dark2 = [System.Drawing.Color]::FromArgb(13, 16, 23)    # #0d1017
$White = [System.Drawing.Color]::White
$Grey  = [System.Drawing.Color]::FromArgb(170, 178, 190)

$Langs = @{
    EN = @{
        Line1 = 'Slideshow  ·  Panel navigation  ·  One-key Move / Copy / Rename / Delete'
        Line2 = 'H.264 + LibVLC playback  ·  Ambilight background  ·  Offline OCR translation'
        Title = 'Fast media viewer and sorter for Windows'
    }
    RU = @{
        Line1 = 'Слайдшоу  ·  Панель навигации  ·  Одна клавиша: Переместить / Скопировать / Удалить'
        Line2 = 'Воспроизведение H.264 + LibVLC  ·  Фон Ambilight  ·  OCR-перевод офлайн'
        Title = 'Быстрый просмотр и сортировка медиафайлов'
    }
}

function New-Canvas([int]$W, [int]$H) {
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    # gradient background
    $rect  = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $Dark1, $Dark2, 90)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()
    return $bmp, $g
}

function Draw-Banner($g, [int]$canvasW, [int]$targetW, [int]$y) {
    $img = [System.Drawing.Image]::FromFile($Banner)
    try {
        $h = [int]($targetW * $img.Height / $img.Width)
        $x = [int](($canvasW - $targetW) / 2)
        $g.DrawImage($img, $x, $y, $targetW, $h)
        return $y + $h
    } finally { $img.Dispose() }
}

function Draw-CenteredText($g, [int]$canvasW, [string]$text, $font, $color, [int]$y) {
    $size = $g.MeasureString($text, $font)
    $x    = [int](($canvasW - $size.Width) / 2)
    if ($x -lt 10) { $x = 10 }
    $sb = New-Object System.Drawing.SolidBrush($color)
    $g.DrawString($text, $font, $sb, $x, $y)
    $sb.Dispose()
    return $y + [int]$size.Height
}

# --- Screenshots ---
foreach ($lang in $Langs.Keys) {
    $L = $Langs[$lang]

    # 1366x768 landscape
    $W = 1366; $H = 768
    $bmp, $g = New-Canvas $W $H
    try {
        $afterBanner = Draw-Banner $g $W 1120 50
        $f1 = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
        $f2 = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Regular)
        $y  = Draw-CenteredText $g $W $L.Line1 $f1 $White ($afterBanner + 24)
              Draw-CenteredText $g $W $L.Line2 $f2 $Grey  ($y + 8) | Out-Null
        $f1.Dispose(); $f2.Dispose()
        $out = Join-Path $OutDir "screenshot-$lang-1366x768.png"
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Wrote $out"
    } finally { $g.Dispose(); $bmp.Dispose() }

    # 720x1080 portrait
    $W = 720; $H = 1080
    $bmp, $g = New-Canvas $W $H
    try {
        $afterBanner = Draw-Banner $g $W 680 90
        $f0 = New-Object System.Drawing.Font('Segoe UI', 17, [System.Drawing.FontStyle]::Bold)
        $f1 = New-Object System.Drawing.Font('Segoe UI', 19, [System.Drawing.FontStyle]::Bold)
        $f2 = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Regular)
        $y  = Draw-CenteredText $g $W $L.Title  $f0 $Grey  ($afterBanner + 30)
        $y  = Draw-CenteredText $g $W $L.Line1  $f1 $White ($y + 14)
              Draw-CenteredText $g $W $L.Line2  $f2 $Grey  ($y + 8) | Out-Null
        $f0.Dispose(); $f1.Dispose(); $f2.Dispose()
        $out = Join-Path $OutDir "screenshot-$lang-720x1080.png"
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Wrote $out"
    } finally { $g.Dispose(); $bmp.Dispose() }

    # 1080x1080 square
    $W = 1080; $H = 1080
    $bmp, $g = New-Canvas $W $H
    try {
        $afterBanner = Draw-Banner $g $W 1000 80
        $f1 = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
        $f2 = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Regular)
        $y  = Draw-CenteredText $g $W $L.Line1 $f1 $White ($afterBanner + 30)
              Draw-CenteredText $g $W $L.Line2 $f2 $Grey  ($y + 10) | Out-Null
        $f1.Dispose(); $f2.Dispose()
        $out = Join-Path $OutDir "screenshot-$lang-1080x1080.png"
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Wrote $out"
    } finally { $g.Dispose(); $bmp.Dispose() }
}

# --- Logos (language-independent) ---
$icon = [System.Drawing.Image]::FromFile($Icon)
foreach ($size in @(300, 150, 71)) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($icon, 0, 0, $size, $size)
    $g.Dispose()
    $out = Join-Path $OutDir "logo-${size}x${size}.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $out"
}
$icon.Dispose()

Write-Host "`nAll store assets written to: $OutDir"
