<#
    Generates a few neutral, non-copyrighted sample images into a temp folder,
    used purely to populate the app for a real Store screenshot.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Dir = Join-Path $env:TEMP 'fms-store-samples'
New-Item -ItemType Directory -Path $Dir -Force | Out-Null
Get-ChildItem $Dir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

# (name, w, h, colorA, colorB, accent)
$specs = @(
    @('01-sunset-ridge',      1600, 1067, '#1b2a4a', '#e8794a', '#ffd27a'),
    @('02-teal-dunes',        1600, 1067, '#08323a', '#36b5a0', '#bff3e6'),
    @('03-violet-peaks',      1067, 1600, '#241038', '#7a4ae8', '#d7b3ff'),
    @('04-amber-fields',      1600, 1067, '#3a2a08', '#e8b04a', '#fff0c0'),
    @('05-rose-coast',        1067, 1600, '#3a0820', '#e84a86', '#ffc0d8'),
    @('06-forest-mist',       1600, 1067, '#0c2410', '#4ab56a', '#d0f3c0')
)

function HexColor($h) {
    $h = $h.TrimStart('#')
    return [System.Drawing.Color]::FromArgb(
        [Convert]::ToInt32($h.Substring(0,2),16),
        [Convert]::ToInt32($h.Substring(2,2),16),
        [Convert]::ToInt32($h.Substring(4,2),16))
}

foreach ($s in $specs) {
    $name,$w,$h,$a,$b,$ac = $s
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $rect  = New-Object System.Drawing.Rectangle(0,0,$w,$h)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (HexColor $a), (HexColor $b), 60)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    # soft "sun" / accent orb
    $orbR = [int]($h * 0.28)
    $orbX = [int]($w * 0.66); $orbY = [int]($h * 0.30)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($orbX-$orbR, $orbY-$orbR, $orbR*2, $orbR*2)
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $pgb.CenterColor = HexColor $ac
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, (HexColor $ac)))
    $g.FillPath($pgb, $path)
    $pgb.Dispose(); $path.Dispose()

    # rolling "hills" silhouette
    for ($i=0; $i -lt 3; $i++) {
        $base = [int]($h * (0.6 + $i*0.13))
        $pts = @()
        $pts += New-Object System.Drawing.Point(0, $h)
        for ($x=0; $x -le $w; $x += [int]($w/8)) {
            $yy = $base - [int](($h*0.06) * [Math]::Sin(($x/$w)*6.28 + $i*1.7))
            $pts += New-Object System.Drawing.Point($x, $yy)
        }
        $pts += New-Object System.Drawing.Point($w, $h)
        $shade = [int](30 + $i*22)
        $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, $shade, $shade, [int]($shade*1.2)))
        $g.FillPolygon($sb, [System.Drawing.Point[]]$pts)
        $sb.Dispose()
    }

    $g.Dispose()
    $out = Join-Path $Dir "$name.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $out"
}
Write-Host "`nSamples in: $Dir"
