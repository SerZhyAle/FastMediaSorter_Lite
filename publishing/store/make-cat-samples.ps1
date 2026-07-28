<#
    Draws a handful of deliberately simple, flat-design cat pictures used to
    populate the app for the Store screenshots.

    Why drawn and not photographed: a Store listing image must be free of any
    third-party rights, and a generated picture is provably ours. "Primitive"
    is also the point - the screenshot must show the APP, so the picture in it
    has to be readable at a glance and never compete for attention.

    Output: C:\Users\Public\Pictures\Cats\*.png  (five 1600x1067 landscape frames)

    The folder is deliberately under Public and deliberately called "Cats": the
    app shows its current folder in the toolbar and the full file path in the
    status bar, and both land in the Store screenshot. A path under %TEMP%
    would publish the developer's Windows account name to the listing.
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:PUBLIC 'Pictures\Cats')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Dir = $OutDir
New-Item -ItemType Directory -Path $Dir -Force | Out-Null
Get-ChildItem $Dir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

function HexColor([string]$h) {
    $h = $h.TrimStart('#')
    return [System.Drawing.Color]::FromArgb(
        [Convert]::ToInt32($h.Substring(0,2),16),
        [Convert]::ToInt32($h.Substring(2,2),16),
        [Convert]::ToInt32($h.Substring(4,2),16))
}

# name, background top, background bottom, cat body, cat shade, eye colour
$specs = @(
    @('01-ginger-cat',  '#2b1b3f', '#7c4a8d', '#f0a05a', '#d67e3c', '#2f9e6e'),
    @('02-grey-cat',    '#10222e', '#2f6f7d', '#b9c4cc', '#93a1ab', '#e8b04a'),
    @('03-black-cat',   '#3a1020', '#8d3d5c', '#2d2f3a', '#1e2029', '#f2d24b'),
    @('04-cream-cat',   '#123024', '#3f8f63', '#f4e3c4', '#dcc59d', '#5a7fd6'),
    @('05-blue-cat',    '#241038', '#5b47c8', '#8fa8d8', '#6f88b8', '#e07a5f')
)

foreach ($s in $specs) {
    $name, $bgA, $bgB, $furHex, $shadeHex, $eyeHex = $s
    $W = 1600; $H = 1067

    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # --- background: vertical gradient + a soft moon, so the Ambilight fill
    #     has real colour to extend into the pillarbox bars ---------------
    $rect  = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, (HexColor $bgA), (HexColor $bgB), 90)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    $moonR = 130
    $moonX = 1230; $moonY = 240
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($moonX - $moonR, $moonY - $moonR, $moonR * 2, $moonR * 2)
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $pgb.CenterColor    = [System.Drawing.Color]::FromArgb(230, 255, 248, 214)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 248, 214))
    $g.FillPath($pgb, $path)
    $pgb.Dispose(); $path.Dispose()

    # ground
    $groundY = 830
    $gb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
    $g.FillRectangle($gb, 0, $groundY, $W, $H - $groundY)
    $gb.Dispose()

    $fur   = New-Object System.Drawing.SolidBrush (HexColor $furHex)
    $shade = New-Object System.Drawing.SolidBrush (HexColor $shadeHex)

    $cx = 800          # cat centre x
    $baseY = $groundY  # where the cat sits

    # --- tail: a thick arc sweeping to the right --------------------------
    $tailPen = New-Object System.Drawing.Pen ((HexColor $shadeHex), 54)
    $tailPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $tailPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(
        (New-Object System.Drawing.Point(($cx + 140), ($baseY - 30))),
        (New-Object System.Drawing.Point(($cx + 320), ($baseY - 10))),
        (New-Object System.Drawing.Point(($cx + 380), ($baseY - 230))),
        (New-Object System.Drawing.Point(($cx + 250), ($baseY - 300))))
    $g.DrawPath($tailPen, $tailPath)
    $tailPath.Dispose(); $tailPen.Dispose()

    # --- body: a wide teardrop sitting on the ground ----------------------
    $bodyW = 380; $bodyH = 420
    $g.FillEllipse($fur, ($cx - $bodyW / 2), ($baseY - $bodyH), $bodyW, $bodyH)
    # chest highlight
    $g.FillEllipse($shade, ($cx - 90), ($baseY - 220), 180, 200)

    # --- head -------------------------------------------------------------
    $headR = 190
    $headY = $baseY - $bodyH - 90
    $g.FillEllipse($fur, ($cx - $headR), ($headY - $headR), $headR * 2, $headR * 2)

    # ears: two triangles, with an inner triangle in the shade colour
    foreach ($side in @(-1, 1)) {
        $ex = $cx + $side * 120
        $outer = @(
            (New-Object System.Drawing.Point(($ex - 70), ($headY - 130))),
            (New-Object System.Drawing.Point(($ex + 70), ($headY - 130))),
            (New-Object System.Drawing.Point(($ex + $side * 10), ($headY - 300)))
        )
        $g.FillPolygon($fur, [System.Drawing.Point[]]$outer)
        $inner = @(
            (New-Object System.Drawing.Point(($ex - 38), ($headY - 145))),
            (New-Object System.Drawing.Point(($ex + 38), ($headY - 145))),
            (New-Object System.Drawing.Point(($ex + $side * 6), ($headY - 250)))
        )
        $g.FillPolygon($shade, [System.Drawing.Point[]]$inner)
    }

    # --- face -------------------------------------------------------------
    $eyeBrush   = New-Object System.Drawing.SolidBrush (HexColor $eyeHex)
    $pupilBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 30))
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))

    foreach ($side in @(-1, 1)) {
        $ex = $cx + $side * 72
        $ey = $headY - 20
        $g.FillEllipse($eyeBrush,   ($ex - 42), ($ey - 50), 84, 100)   # iris
        $g.FillEllipse($pupilBrush, ($ex - 13), ($ey - 44), 26, 88)    # slit pupil
        $g.FillEllipse($whiteBrush, ($ex + 6),  ($ey - 40), 20, 20)    # glint
    }

    # nose
    $nose = @(
        (New-Object System.Drawing.Point(($cx - 26), ($headY + 62))),
        (New-Object System.Drawing.Point(($cx + 26), ($headY + 62))),
        (New-Object System.Drawing.Point($cx,        ($headY + 98)))
    )
    $noseBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 122, 138))
    $g.FillPolygon($noseBrush, [System.Drawing.Point[]]$nose)
    $noseBrush.Dispose()

    # mouth: two small arcs under the nose
    $mouthPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 30, 30, 36), 7)
    $mouthPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $mouthPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($mouthPen, ($cx - 60), ($headY + 88), 60, 46, 20, 140)
    $g.DrawArc($mouthPen, $cx,        ($headY + 88), 60, 46, 20, 140)
    $mouthPen.Dispose()

    # whiskers
    $whiskerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 255, 255, 255), 5)
    $whiskerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $whiskerPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($side in @(-1, 1)) {
        for ($i = 0; $i -lt 3; $i++) {
            $y0 = $headY + 50 + $i * 26
            $y1 = $headY + 26 + $i * 34
            $g.DrawLine($whiskerPen,
                ($cx + $side * 48), $y0,
                ($cx + $side * 250), $y1)
        }
    }
    $whiskerPen.Dispose()

    # --- front paws -------------------------------------------------------
    foreach ($side in @(-1, 1)) {
        $px = $cx + $side * 105
        $g.FillEllipse($shade, ($px - 62), ($baseY - 70), 124, 70)
    }

    $eyeBrush.Dispose(); $pupilBrush.Dispose(); $whiteBrush.Dispose()
    $fur.Dispose(); $shade.Dispose()
    $g.Dispose()

    $out = Join-Path $Dir "$name.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $out"
}

Write-Host ""
Write-Host "Cat samples in: $Dir"
