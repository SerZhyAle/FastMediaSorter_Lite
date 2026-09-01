<#
.SYNOPSIS
    Renders one Microsoft Store screenshot per UI locale (13 of them) into
    publishing/store/screenshots/, ready to attach to a Partner Center listing.

.DESCRIPTION
    Each screenshot is 1920x1080: a localized caption block over a REAL capture
    of the running app showing a generated cat picture.

    Two-stage on purpose:

      1. CAPTURE - the app is launched on the cat folder, sized, and grabbed
         with PrintWindow. One capture per *chrome* language.
      2. COMPOSE - the caption text for each of the 13 locales is drawn over
         the matching capture.

    Why the split: the app's own interface today exists only in Russian and
    English (13-language UI is the plan in
    docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md, block A). So
    `ru` gets Russian chrome and the other twelve get English chrome, while the
    caption - the part a shopper actually reads on the listing card - is in all
    13. When block A ships, ChromeFor() below returns the locale itself and the
    script captures 13 times with no other change.

    The caption text is NOT in this file: it lives in screenshot-copy.json.
    The PNGs are render targets - regenerate them, never touch them by hand.

.PARAMETER Locales
    Subset to render, e.g. -Locales en,ru. Default: all 13.

.PARAMETER SkipCapture
    Reuse the captures already in %TEMP%\fms-store-shots. Use while iterating
    on the caption layout - it avoids relaunching the app.

.EXAMPLE
    .\publishing\store\make-store-screenshots.ps1
    .\publishing\store\make-store-screenshots.ps1 -Locales de,ar -SkipCapture
#>
[CmdletBinding()]
param(
    [string[]]$Locales,
    [switch]$SkipCapture
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root     = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Exe      = Join-Path $Root 'bin\Release\FastMediaSorter_LITE.exe'
$CopyFile = Join-Path $PSScriptRoot 'screenshot-copy.json'
$CatMaker = Join-Path $PSScriptRoot 'make-cat-samples.ps1'
$OutDir   = Join-Path $PSScriptRoot 'screenshots'
$CatDir   = Join-Path $env:PUBLIC 'Pictures\Cats'
$ShotDir  = Join-Path $env:TEMP 'fms-store-shots'
$RegPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

# Settings forced for the duration of the shoot, then put back. Everything here
# is something that would otherwise leak the developer's own machine into a
# public listing image.
#   ShowRecipientsOverlay - draws the real destination-folder list (UNC paths!)
#                           on top of the picture.
#   ShowInfoOverlay       - filename HUD; off keeps the picture clean.
$ShootSettings = @{
    'ShowRecipientsOverlay' = '0'
    'ShowInfoOverlay'       = '0'
}

# Final canvas + where the app capture sits inside it.
$CanvasW = 1920; $CanvasH = 1080
$ShotW   = 1600; $ShotH   = 760
$ShotX   = [int](($CanvasW - $ShotW) / 2); $ShotY = 268

if (-not (Test-Path $Exe))      { throw "Missing exe: $Exe  - run .\build.ps1 first." }
if (-not (Test-Path $CopyFile)) { throw "Missing caption source: $CopyFile" }

$copy = Get-Content $CopyFile -Raw -Encoding UTF8 | ConvertFrom-Json
$allCodes = $copy.PSObject.Properties.Name | Where-Object { $_ -ne '_comment' }
if ($Locales) {
    $unknown = $Locales | Where-Object { $allCodes -notcontains $_ }
    if ($unknown) { throw "Unknown locale(s): $($unknown -join ', '). Known: $($allCodes -join ', ')" }
    $codes = $Locales
} else {
    $codes = $allCodes
}

# The app's UI language for a given listing locale. Since block A of
# 013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md shipped, the viewer itself speaks all 13,
# so a locale is captured in its own language - one launch per locale.
function ChromeFor([string]$code) { $code }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class FmsShot {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr hdc,uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
}
"@

function Find-AppWindow {
    $script:hwnd = [IntPtr]::Zero
    $cb = [FmsShot+EnumProc]{
        param($h, $p)
        if ([FmsShot]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][FmsShot]::GetWindowText($h, $sb, 256)
            if ($sb.ToString() -like '*Fast Media Sorter*') { $script:hwnd = $h; return $false }
        }
        return $true
    }
    [void][FmsShot]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:hwnd
}

# PrintWindow fills the bitmap only as far as the window actually painted. On a
# scaled display this PowerShell process is DPI-unaware, so GetWindowRect hands
# back virtualized pixels while the window renders at its physical size - the
# capture then carries a black L-shaped margin down the right and bottom edges.
# Rather than guess the scale factor, measure where the paint stops: the app's
# title bar and window border are never pure black, the unpainted margin always
# is. Returns the size of the painted area.
function Get-PaintedSize([System.Drawing.Bitmap]$bmp) {
    $ink = 24     # sum of R+G+B above which a pixel counts as painted
    $w = 0; $h = 0
    foreach ($y in @(2, 6, 12)) {
        for ($x = $bmp.Width - 1; $x -ge 0; $x--) {
            $c = $bmp.GetPixel($x, $y)
            if (($c.R + $c.G + $c.B) -gt $ink) { if (($x + 1) -gt $w) { $w = $x + 1 }; break }
        }
    }
    foreach ($x in @(2, 6, 12)) {
        for ($y = $bmp.Height - 1; $y -ge 0; $y--) {
            $c = $bmp.GetPixel($x, $y)
            if (($c.R + $c.G + $c.B) -gt $ink) { if (($y + 1) -gt $h) { $h = $y + 1 }; break }
        }
    }
    if ($w -lt ($bmp.Width * 0.4) -or $h -lt ($bmp.Height * 0.4)) {
        throw "Painted area ${w}x${h} is implausibly small for a ${($bmp.Width)}x${($bmp.Height)} capture - refusing to crop blindly."
    }
    return @{ W = $w; H = $h }
}

function Capture-Chrome([string]$chrome, [string]$image, [string]$outPath) {
    Write-Host "  launching app (UI: $chrome) .."
    Get-Process FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700

    # UiLanguage is the setting the viewer reads (the legacy Is_Russian_Language flag
    # is only a mirror it writes back); set both so an interrupted run cannot leave a
    # half-migrated pair behind.
    Set-ItemProperty -Path $RegPath -Name 'UiLanguage' -Value $chrome
    Set-ItemProperty -Path $RegPath -Name 'Is_Russian_Language' -Value $(if ($chrome -eq 'ru') { '1' } else { '0' })

    Start-Process -FilePath $Exe -ArgumentList "`"$image`""
    Start-Sleep -Seconds 7          # load the image + draw the Ambilight bars

    $h = Find-AppWindow
    if ($h -eq [IntPtr]::Zero) { throw "Could not find the app window (UI: $chrome)." }

    [void][FmsShot]::ShowWindow($h, 9)                                  # SW_RESTORE
    [void][FmsShot]::MoveWindow($h, 40, 40, $ShotW, $ShotH, $true)
    [void][FmsShot]::SetForegroundWindow($h)
    Start-Sleep -Seconds 3          # let the perspective bars redraw at the new size

    $r = New-Object FmsShot+RECT
    [void][FmsShot]::GetWindowRect($h, [ref]$r)
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top

    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok  = [FmsShot]::PrintWindow($h, $hdc, 2)                          # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()

    if (-not $ok) {
        Write-Warning "  PrintWindow failed; falling back to a screen copy."
        $g2 = [System.Drawing.Graphics]::FromImage($bmp)
        $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
        $g2.Dispose()
    }

    $painted = Get-PaintedSize $bmp
    if ($painted.W -ne $w -or $painted.H -ne $ht) {
        Write-Host "  window painted $($painted.W)x$($painted.H) inside a ${w}x${ht} capture (display scaling) - cropping"
        $crop = $bmp.Clone(
            (New-Object System.Drawing.Rectangle(0, 0, $painted.W, $painted.H)),
            $bmp.PixelFormat)
        $bmp.Dispose()
        $bmp = $crop
    }

    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  saved $($bmp.Width)x$($bmp.Height) -> $outPath"
    $bmp.Dispose()
    Get-Process FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
}

# A font that actually covers the script. An absent family silently falls back
# to a default that draws boxes, so this is checked, not assumed.
function Get-Font([string]$family, [single]$size, [System.Drawing.FontStyle]$style) {
    $installed = New-Object System.Drawing.Text.InstalledFontCollection
    $has = $installed.Families | Where-Object { $_.Name -eq $family }
    if (-not $has) { throw "Font '$family' is not installed - the caption would render as boxes." }
    return New-Object System.Drawing.Font($family, $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

# Draw centred, shrinking the font until the line fits the width. German and
# Portuguese run 15-30% longer than English; clipping is not an option.
function Draw-Fitted {
    param(
        [System.Drawing.Graphics]$G, [string]$Text, [string]$Family,
        [single]$Size, [System.Drawing.FontStyle]$Style,
        [System.Drawing.Brush]$Brush, [int]$CenterX, [int]$Y, [int]$MaxWidth, [bool]$Rtl
    )
    if ([string]::IsNullOrWhiteSpace($Text)) { return }
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    if ($Rtl) { $fmt.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft }

    $size = $Size
    while ($size -gt 10) {
        $f = Get-Font $Family $size $Style
        $m = $G.MeasureString($Text, $f)
        if ($m.Width -le $MaxWidth) {
            $G.DrawString($Text, $f, $Brush, $CenterX, $Y, $fmt)
            $f.Dispose(); $fmt.Dispose()
            return
        }
        $f.Dispose()
        $size = $size - 2
    }
    throw "Caption does not fit even at 10 px: '$Text'"
}

function Compose-Screenshot([string]$code, $entry, [string]$capturePath, [string]$outPath) {
    $canvas = New-Object System.Drawing.Bitmap($CanvasW, $CanvasH)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # backdrop - the same palette the logo/banner assets use
    $rect = New-Object System.Drawing.Rectangle(0, 0, $CanvasW, $CanvasH)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(23, 27, 38),
        [System.Drawing.Color]::FromArgb(13, 16, 23), 90)
    $g.FillRectangle($bg, $rect); $bg.Dispose()

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $grey  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(170, 178, 190))
    $rtl   = [bool]$entry.rtl
    $cxT   = [int]($CanvasW / 2)
    $maxW  = $CanvasW - 160

    Draw-Fitted -G $g -Text $entry.title -Family $entry.font -Size 46 `
                -Style ([System.Drawing.FontStyle]::Bold) -Brush $white `
                -CenterX $cxT -Y 62 -MaxWidth $maxW -Rtl $rtl
    Draw-Fitted -G $g -Text $entry.line1 -Family $entry.font -Size 24 `
                -Style ([System.Drawing.FontStyle]::Regular) -Brush $grey `
                -CenterX $cxT -Y 150 -MaxWidth $maxW -Rtl $rtl
    Draw-Fitted -G $g -Text $entry.line2 -Family $entry.font -Size 24 `
                -Style ([System.Drawing.FontStyle]::Regular) -Brush $grey `
                -CenterX $cxT -Y 194 -MaxWidth $maxW -Rtl $rtl

    # the app capture, with a soft frame so it reads as a window
    # Fit the capture into the reserved box without distorting it - the capture's
    # aspect ratio depends on the display scaling, so it is never assumed.
    $shot  = [System.Drawing.Image]::FromFile($capturePath)
    $scale = [Math]::Min($ShotW / $shot.Width, $ShotH / $shot.Height)
    $dw = [int]($shot.Width * $scale); $dh = [int]($shot.Height * $scale)
    $dx = $ShotX + [int](($ShotW - $dw) / 2)
    $dy = $ShotY + [int](($ShotH - $dh) / 2)

    $frame  = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(80, 255, 255, 255)), 1
    $shadow = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(70, 0, 0, 0))
    $g.FillRectangle($shadow, ($dx + 6), ($dy + 8), $dw, $dh)
    $g.DrawImage($shot, $dx, $dy, $dw, $dh)
    $g.DrawRectangle($frame, $dx, $dy, $dw, $dh)
    $shadow.Dispose(); $frame.Dispose(); $shot.Dispose()

    $white.Dispose(); $grey.Dispose(); $g.Dispose()
    $canvas.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
}

# ---------------------------------------------------------------- run --------

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
New-Item -ItemType Directory -Path $ShotDir -Force | Out-Null

if (-not (Test-Path (Join-Path $CatDir '01-ginger-cat.png'))) {
    Write-Host "Generating cat samples .."
    & $CatMaker | Out-Null
}
$catImage = Join-Path $CatDir '01-ginger-cat.png'

$chromes = $codes | ForEach-Object { ChromeFor $_ } | Sort-Object -Unique
$saved = @{}
$restoreNeeded = $false

try {
    if (-not $SkipCapture) {
        # Back up every setting the shoot overrides, INCLUDING the UI language.
        # Restored in finally - a run interrupted with Ctrl+C must not leave the
        # developer's app on another language with its overlays switched off.
        $props = Get-ItemProperty -Path $RegPath -ErrorAction SilentlyContinue
        foreach ($name in (@('UiLanguage', 'Is_Russian_Language') + $ShootSettings.Keys)) {
            $saved[$name] = $props.$name
        }
        $restoreNeeded = $true
        Write-Host "Saved: $(($saved.GetEnumerator() | ForEach-Object { "$($_.Key)='$($_.Value)'" }) -join ', ')"

        foreach ($kv in $ShootSettings.GetEnumerator()) {
            Set-ItemProperty -Path $RegPath -Name $kv.Key -Value $kv.Value
        }

        foreach ($chrome in $chromes) {
            Write-Host "Capture: $chrome"
            Capture-Chrome $chrome $catImage (Join-Path $ShotDir "app-$chrome.png")
        }
    } else {
        foreach ($chrome in $chromes) {
            $p = Join-Path $ShotDir "app-$chrome.png"
            if (-not (Test-Path $p)) { throw "-SkipCapture given but $p does not exist. Run once without it." }
        }
        Write-Host "Reusing captures in $ShotDir"
    }
}
finally {
    if ($restoreNeeded) {
        foreach ($kv in $saved.GetEnumerator()) {
            if ($null -eq $kv.Value) {
                # The value did NOT exist before the shoot. Putting it back means
                # DELETING it, not leaving whatever the last locale wrote - skipping
                # here is how a first run left the developer's app in Chinese.
                Remove-ItemProperty -Path $RegPath -Name $kv.Key -ErrorAction SilentlyContinue
            } else {
                Set-ItemProperty -Path $RegPath -Name $kv.Key -Value $kv.Value
            }
        }
        Write-Host ("Restored: " + (($saved.GetEnumerator() | ForEach-Object {
            if ($null -eq $_.Value) { "$($_.Key)=(removed)" } else { "$($_.Key)='$($_.Value)'" } }) -join ', '))
    }
}

Write-Host ""
$written = @(); $skipped = @()
foreach ($code in $codes) {
    $entry = $copy.$code
    $out = Join-Path $OutDir ("screenshot-{0}-{1}x{2}.png" -f $entry.store, $CanvasW, $CanvasH)
    try {
        Compose-Screenshot $code $entry (Join-Path $ShotDir ("app-{0}.png" -f (ChromeFor $code))) $out
        $written += [pscustomobject]@{
            Locale = $code; Store = $entry.store; Chrome = (ChromeFor $code); File = (Split-Path $out -Leaf)
        }
    } catch {
        $skipped += [pscustomobject]@{ Locale = $code; Reason = $_.Exception.Message }
    }
}

Write-Host "Rendered $($written.Count) of $($codes.Count) locales into $OutDir`n"
$written | Format-Table -AutoSize

# Never report a partial run as a clean one.
if ($skipped.Count -gt 0) {
    Write-Host ""
    Write-Warning "SKIPPED $($skipped.Count) locale(s) - these were NOT produced:"
    $skipped | Format-Table -AutoSize
    exit 1
}
