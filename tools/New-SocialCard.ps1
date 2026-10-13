<#
.SYNOPSIS
    Renders the one social-card artwork at both of its sizes from one source: 1200x630 (Open Graph - the og:image of every
    page) and 1280x640 (the repository's social preview, a PNG under 1 MB).

.DESCRIPTION
    Ticket 207 T-4 (canon PROMOTION section 4 item 2). The words on the card are not typed here: the tagline, the three
    lines and the language count are read from the `discoverability` block of tools/site-facts.json (the machine form of
    docs/guides/POSITIONING.md), so the card and the pages say the same thing and a changed tagline is one edit and one
    run of this script. The program icon is assets/icons/store-icon-256.png.

    Writes assets/social-card-1200x630.png, assets/social-preview-1280x640.png and the stamp assets/social-card.json,
    which records the SHA-256 of everything the artwork was made from (this script, the words, the icon). The stamp is
    what -Check compares: a PNG cannot be compared pixel for pixel across machines (fonts, GDI+ versions), but "made
    from other inputs than the current ones" is exact.

    The repository preview is uploaded to GitHub in the owner's browser (GraphQL `usesCustomOpenGraphImage` reads it
    back); nothing here talks to the network.

.PARAMETER Check
    Write nothing. Exit 0 when both PNGs exist with the declared size, the 1280x640 one is under its byte limit and the
    stamp matches the current inputs; exit 1 and name the reason otherwise. Build-SitePages.ps1 -Check calls it.
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root     = Split-Path $PSScriptRoot -Parent
$Facts    = Join-Path $PSScriptRoot 'site-facts.json'
$IconFile = Join-Path $Root 'assets\icons\store-icon-256.png'
$StampRel = 'assets/social-card.json'
$Stamp    = Join-Path $Root ($StampRel -replace '/', '\')

if (-not (Test-Path -LiteralPath $Facts))    { throw "missing input: $Facts" }
if (-not (Test-Path -LiteralPath $IconFile)) { throw "missing input: $IconFile" }
$decl = [IO.File]::ReadAllText($Facts, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
$d    = $decl.discoverability
if (-not $d -or -not $d.socialCard) { throw 'tools/site-facts.json has no discoverability.socialCard' }
$languages = [int]$decl.facts.languages.count
$tagline   = [string]$d.tagline
$lines     = @($d.socialCard.lines | ForEach-Object { ([string]$_).Replace('{languages}', "$languages") })
$sizes     = @($d.socialCard.sizes)

function Get-Sha256Hex([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

# What the artwork is made from. The script text is part of it, so a layout change makes every older card stale.
$scriptText = ([IO.File]::ReadAllText($PSCommandPath, [Text.UTF8Encoding]::new($false))) -replace "`r`n", "`n"
$inputs = (@('script', $scriptText, 'tagline', $tagline, 'lines', ($lines -join "`n"), 'languages', "$languages",
        'icon', (Get-Sha256Hex ([IO.File]::ReadAllBytes($IconFile)))) -join "`u{1F}")
$inputsHash = Get-Sha256Hex ([Text.Encoding]::UTF8.GetBytes($inputs))

if ($Check) {
    $bad = [Collections.Generic.List[string]]::new()
    foreach ($s in $sizes) {
        $p = Join-Path $Root ([string]$s.file -replace '/', '\')
        if (-not (Test-Path -LiteralPath $p)) { $bad.Add("$($s.file) does not exist (run tools\New-SocialCard.ps1)"); continue }
        $bmp = [Drawing.Bitmap]::new($p)
        try {
            if ($bmp.Width -ne [int]$s.width -or $bmp.Height -ne [int]$s.height) {
                $bad.Add("$($s.file) is $($bmp.Width)x$($bmp.Height), not the declared $($s.width)x$($s.height)")
            }
        } finally { $bmp.Dispose() }
        if ($s.PSObject.Properties['maxBytes'] -and (Get-Item -LiteralPath $p).Length -ge [int64]$s.maxBytes) {
            $bad.Add("$($s.file) is $((Get-Item -LiteralPath $p).Length) bytes, not under $($s.maxBytes)")
        }
    }
    if (-not (Test-Path -LiteralPath $Stamp)) { $bad.Add("$StampRel does not exist (run tools\New-SocialCard.ps1)") }
    else {
        $st = [IO.File]::ReadAllText($Stamp, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
        if ($st.inputsSha256 -ne $inputsHash) { $bad.Add('the social card was rendered from other inputs than the current tagline, lines, icon and generator - run tools\New-SocialCard.ps1') }
    }
    if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "  - $_" }; exit 1 }
    Write-Host "New-SocialCard: the card matches its inputs ($($sizes.Count) renderings)"
    exit 0
}

# --- the artwork, laid out on a 1200x630 design grid and scaled uniformly to each size -----------------------------
function Get-Font([string]$name, [float]$px, [Drawing.FontStyle]$style) {
    $family = try { [Drawing.FontFamily]::new($name) } catch { [Drawing.FontFamily]::GenericSansSerif }
    [Drawing.Font]::new($family, $px, $style, [Drawing.GraphicsUnit]::Pixel)
}

# The widest a line may be on the 1200-wide design grid: from the text column to the right margin. A longer line shrinks
# its font until it fits - a tagline changed in the data file must never run off the card.
$TextLeft = 450
$TextWidth = 1200 - 80 - $TextLeft

function Get-FittedFont([Drawing.Graphics]$g, [string]$text, [string]$name, [float]$px, [double]$scale, [Drawing.StringFormat]$fmt) {
    $size = $px
    while ($true) {
        $font = Get-Font $name ($size * $scale) ([Drawing.FontStyle]::Regular)
        $w = $g.MeasureString($text, $font, [int]::MaxValue, $fmt).Width
        if ($w -le $TextWidth * $scale -or $size -le 12) { return $font }
        $font.Dispose()
        $size -= 1
    }
}

function Render-Card([int]$W, [int]$H, [string]$outFile) {
    $bmp = [Drawing.Bitmap]::new($W, $H, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $s  = [Math]::Min($W / 1200.0, $H / 630.0)
        $ox = ($W - 1200 * $s) / 2.0
        $oy = ($H - 630 * $s) / 2.0
        function X([double]$v) { [float]($ox + $v * $s) }
        function Y([double]$v) { [float]($oy + $v * $s) }

        $rect = [Drawing.Rectangle]::new(0, 0, $W, $H)
        $bg = [Drawing.Drawing2D.LinearGradientBrush]::new($rect, [Drawing.Color]::FromArgb(14, 19, 30), [Drawing.Color]::FromArgb(40, 58, 92), 25.0)
        $g.FillRectangle($bg, $rect); $bg.Dispose()

        $icon = [Drawing.Image]::FromFile($IconFile)
        try { $g.DrawImage($icon, (X 96), (Y 150), [float](300 * $s), [float](300 * $s)) } finally { $icon.Dispose() }

        $white  = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(246, 248, 252))
        $accent = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(120, 172, 255))
        $muted  = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(170, 182, 204))
        $fmt    = [Drawing.StringFormat]::new([Drawing.StringFormat]::GenericTypographic)
        $fonts  = [Collections.Generic.List[Drawing.Font]]::new()
        try {
            $nameFont = Get-FittedFont $g 'Fast Media Sorter' 'Segoe UI Semilight' 74 $s $fmt; $fonts.Add($nameFont)
            $subFont  = Get-FittedFont $g '& Sharing for Windows' 'Segoe UI' 40 $s $fmt; $fonts.Add($subFont)
            $jobFont  = Get-FittedFont $g $tagline 'Segoe UI Semibold' 33 $s $fmt; $fonts.Add($jobFont)
            $g.DrawString('Fast Media Sorter', $nameFont, $white, (X $TextLeft), (Y 118), $fmt)
            $g.DrawString('& Sharing for Windows', $subFont, $accent, (X ($TextLeft + 4)), (Y 214), $fmt)
            $g.DrawString($tagline, $jobFont, $white, (X $TextLeft), (Y 308), $fmt)
            $y = 372
            foreach ($line in $lines) {
                $f = Get-FittedFont $g $line 'Segoe UI' 23 $s $fmt; $fonts.Add($f)
                $g.DrawString($line, $f, $muted, (X $TextLeft), (Y $y), $fmt)
                $y += 42
            }
            $bar = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(77, 155, 255))
            $g.FillRectangle($bar, (X 80), (Y 552), [float](1040 * $s), [float](6 * $s)); $bar.Dispose()
        } finally {
            $fmt.Dispose(); foreach ($f in $fonts) { $f.Dispose() }
            $white.Dispose(); $accent.Dispose(); $muted.Dispose()
        }
    } finally { $g.Dispose() }
    try { $bmp.Save($outFile, [Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
}

foreach ($s in $sizes) {
    $p = Join-Path $Root ([string]$s.file -replace '/', '\')
    New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null
    Render-Card ([int]$s.width) ([int]$s.height) $p
    $len = (Get-Item -LiteralPath $p).Length
    if ($s.PSObject.Properties['maxBytes'] -and $len -ge [int64]$s.maxBytes) {
        throw "$($s.file) is $len bytes; the limit is $($s.maxBytes) - simplify the artwork"
    }
    Write-Host ("  {0}  {1}x{2}  {3:N0} bytes" -f $s.file, $s.width, $s.height, $len)
}

$stampDoc = [ordered]@{
    '_comment'    = 'Written by tools/New-SocialCard.ps1; compared by its -Check (Build-SitePages -Check runs it). Never edit by hand.'
    'generator'   = 'tools/New-SocialCard.ps1'
    'inputsSha256' = $inputsHash
    'renderings'  = @($sizes | ForEach-Object { [ordered]@{ file = [string]$_.file; width = [int]$_.width; height = [int]$_.height } })
}
[IO.File]::WriteAllText($Stamp, (($stampDoc | ConvertTo-Json -Depth 4) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "  $StampRel"
