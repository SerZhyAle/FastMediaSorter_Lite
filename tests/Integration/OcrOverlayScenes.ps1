<#
.SYNOPSIS
    Drives the built mainline viewer over a set of scenes with the OCR overlay diagnostics on,
    and judges the result by number.

.DESCRIPTION
    The acceptance items of SPECIFICATION_OCR_OVERLAY_ACCURACY.md section 9 are about what the
    overlay LOOKS like, and unit tests cannot see that: the clustering, the fit ladder and the
    sampled plate colours only meet each other inside a real Paint, over a real Tesseract result.
    This runs the whole pipeline in the exe a user gets and reads the S5 dump back.

    Each scene is generated here, so its ground truth is known in advance and every check is a
    number rather than an opinion. Scenes go to a work directory OUTSIDE the repository by
    default - build.ps1 wipes temp\ (Clean-Build.ps1 -Temp), which silently emptied an earlier
    version of this harness and left it opening files that were no longer there.

    The viewer's OCR settings are borrowed for the run and put back afterwards.

    A scene that produced NO plates is reported apart and fails, never averaged in: its
    "largest plate covers 0.0000 of the frame" and its "0 plates trimmed" are the same numbers
    a perfect scene gives, so folding it into the aggregates makes a gate green by excluding
    exactly the scenes that went wrong (section 16.5). For those scenes the dump now also
    carries what the thresholds refused, which is the difference between "the engine read
    nothing" and "a threshold threw away what it read" - two facts that looked identical
    before, because neither wrote a line.

.PARAMETER WorkDir
    Where scenes and the dump go. Default: %TEMP%\fms-ocr-scenes.

.PARAMETER SecondsPerScene
    How long to wait after each file before reading the dump. OCR is several scored Tesseract
    passes and the translator adds a round trip; a cold local model can take most of a minute.

.PARAMETER KeepScenes
    Reuse scenes already in WorkDir instead of drawing them again.

.OUTPUTS
    Exit 0 = every check passed. 2 = a viewer was already running (its settings and environment
    would be the ones in force). 3 = the viewer produced no dump. 4 = a check failed.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-ocr-scenes'),
    [int]$SecondsPerScene = 45,
    [switch]$KeepScenes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

                       # ..\Integration -> ..\tests -> the repository root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe      = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'
$dump     = Join-Path $WorkDir 'diag.jsonl'
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

# --- scenes -------------------------------------------------------------------
# Each one targets ONE acceptance item, and the expectation is written beside it.

function New-Canvas([int]$W, [int]$H, [System.Drawing.Color]$Back) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($Back)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    @{ Bitmap = $bmp; Graphics = $g }
}

function Add-Text($C, [string]$Text, [int]$X, [int]$Y, [int]$SizePx, [System.Drawing.Color]$Ink, [switch]$Bold) {
    $style = if ($Bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    $font = New-Object System.Drawing.Font 'Arial', $SizePx, $style, ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush $Ink
    $C.Graphics.DrawString($Text, $font, $brush, [single]$X, [single]$Y)
    $font.Dispose(); $brush.Dispose()
}

function Save-Canvas($C, [string]$Path) {
    $C.Graphics.Dispose()
    $C.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $C.Bitmap.Dispose()
}

$ink   = [System.Drawing.Color]::FromArgb(20, 20, 20)
$paper = [System.Drawing.Color]::FromArgb(245, 243, 238)

function Build-Scenes([string]$dir) {
    # 1 - small caps: no descenders, so each ink box is far shorter than its line. The gap rule
    #     this stage replaced broke this balloon into five plates.
    $c = New-Canvas 900 700 $paper
    'WE HAVE TO TALK', 'ABOUT WHAT', 'HAPPENED HERE', 'LAST NIGHT AT', 'THE OLD STATION' |
        ForEach-Object -Begin { $y = 120 } -Process { Add-Text $c $_ 120 $y 46 $ink; $y += 76 }
    Save-Canvas $c (Join-Path $dir 'a-small-caps-balloon.png')

    # 1 (paired) - two balloons far apart must stay two plates.
    $c = New-Canvas 900 1100 $paper
    Add-Text $c 'ARE YOU SURE' 100 90 44 $ink
    Add-Text $c 'THIS IS THE PLACE' 100 162 44 $ink
    Add-Text $c 'I AM CERTAIN' 480 760 44 $ink
    Add-Text $c 'LOOK AT THE SIGN' 480 832 44 $ink
    Save-Canvas $c (Join-Path $dir 'a2-two-balloons.png')

    # 1a - heading over paragraph: one column, one pitch family. Only the type size separates them.
    $c = New-Canvas 1000 800 ([System.Drawing.Color]::FromArgb(248, 246, 240))
    Add-Text $c 'CLOSED' 80 70 120 $ink -Bold
    'The station will not open again', 'until the repairs are finished',
    'and the inspector has signed', 'the certificate of safety' |
        ForEach-Object -Begin { $y = 260 } -Process { Add-Text $c $_ 80 $y 40 $ink; $y += 60 }
    Save-Canvas $c (Join-Path $dir 'b-heading-over-text.png')

    # 1a (paired) - the price of that rule: one inscription whose own lines differ in height.
    $c = New-Canvas 900 900 ([System.Drawing.Color]::FromArgb(246, 244, 239))
    'common sense', 'THE ALL', 'seasons come', 'AND GO', 'nature answers',
    'WITH RAIN', 'summer ends', 'AT ONCE', 'winter waits' |
        ForEach-Object -Begin { $y = 80 } -Process { Add-Text $c $_ 110 $y 44 $ink; $y += 88 }
    Save-Canvas $c (Join-Path $dir 'b2-uneven-inscription.png')

    # 11 - separate regions in one size at one pitch in one column: the dissolve rule's case.
    $c = New-Canvas 640 563 ([System.Drawing.Color]::FromArgb(252, 252, 252))
    Add-Text $c 'Accounts' 15 12 24 $ink -Bold
    Add-Text $c 'Signed in on this device' 15 48 17 ([System.Drawing.Color]::FromArgb(90, 90, 90))
    $rows = 'Personal mailbox', 'Work mailbox', 'Photo library', 'Shared drive', 'Backup archive', 'Guest profile'
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $y = 99 + $i * 81
        $avatar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 120 + $i * 20, 60))
        $c.Graphics.FillEllipse($avatar, 15, $y, 44, 44)
        $avatar.Dispose()
        Add-Text $c $rows[$i] 75 ($y + 10) 26 $ink
    }
    Save-Canvas $c (Join-Path $dir 'c-window-list.png')

    # 3 - light text on a dark coloured panel: the plate has to come out dark.
    $c = New-Canvas 900 600 ([System.Drawing.Color]::FromArgb(24, 34, 58))
    'THE NIGHT TRAIN', 'LEAVES AT MIDNIGHT', 'FROM PLATFORM NINE' |
        ForEach-Object -Begin { $y = 180 } -Process {
            Add-Text $c $_ 120 $y 52 ([System.Drawing.Color]::FromArgb(238, 236, 228)); $y += 80 }
    Save-Canvas $c (Join-Path $dir 'd-dark-panel.png')

    # 4 - heavy display lettering: sampled INSIDE the box the strokes are the majority and the
    #     pair would come out inverted. The ring outside has to catch it.
    $c = New-Canvas 900 500 ([System.Drawing.Color]::FromArgb(18, 18, 18))
    Add-Text $c 'HEAVY' 90 120 190 ([System.Drawing.Color]::FromArgb(244, 244, 240)) -Bold
    Add-Text $c 'TYPE' 90 300 190 ([System.Drawing.Color]::FromArgb(244, 244, 240)) -Bold
    Save-Canvas $c (Join-Path $dir 'd2-display-lettering.png')
}

# What each scene must produce. `MaxPlates` left at 0 means "no upper bound".
$expected = @{
    'a-small-caps-balloon.png'  = @{ MinPlates = 1; MaxPlates = 1; Why = 'one balloon is one plate' }
    'a2-two-balloons.png'       = @{ MinPlates = 2; MaxPlates = 2; Why = 'two balloons stay two plates' }
    'b-heading-over-text.png'   = @{ MinPlates = 2; MaxPlates = 2; Why = 'the heading separates by type size' }
    'b2-uneven-inscription.png' = @{ MinPlates = 1; MaxPlates = 1; Why = 'uneven lines of one inscription hold together' }
    'c-window-list.png'         = @{ MinPlates = 6; MaxPlates = 0; Why = 'the page-wide plate dissolves per line' }
    'd-dark-panel.png'          = @{ MinPlates = 1; MaxPlates = 0; MaxBgLuma = 100; Why = 'a dark panel gets a dark plate' }
    'd2-display-lettering.png'  = @{ MinPlates = 1; MaxPlates = 0; MaxBgLuma = 100; Why = 'the ring test keeps the pair the right way round' }
}

function Get-Luma([string]$rgb) {
    if ([string]::IsNullOrWhiteSpace($rgb)) { return -1 }
    $m = [regex]::Match($rgb, 'rgb\((\d+),(\d+),(\d+)\)')
    if (-not $m.Success) { return -1 }
    [int](([int]$m.Groups[1].Value * 299 + [int]$m.Groups[2].Value * 587 + [int]$m.Groups[3].Value * 114) / 1000)
}

# --- run ----------------------------------------------------------------------

if (-not (Test-Path $exe)) {
    Write-Error "No viewer at $exe - run build.ps1 first." -ErrorAction Continue; exit 4
}
if (Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue) {
    Write-Error 'A viewer is already running - stop it first, or its settings and environment win.' -ErrorAction Continue
    exit 2
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
if (-not $KeepScenes) { Build-Scenes $WorkDir }

$scenes = Get-ChildItem $WorkDir -Filter *.png -File | Sort-Object Name
if ($scenes.Count -eq 0) { Write-Error "No scenes in $WorkDir." -ErrorAction Continue; exit 4 }
Write-Host "Scenes: $($scenes.Count) in $WorkDir"

$restore = @{}
foreach ($n in 'OcrEnabled', 'OcrAutoMode', 'OcrDiskCache', 'OverlayVisible') {
    $restore[$n] = (Get-ItemProperty -Path $regPath -Name $n -ErrorAction SilentlyContinue).$n
}
Set-ItemProperty -Path $regPath -Name 'OcrEnabled'     -Value '1'
Set-ItemProperty -Path $regPath -Name 'OcrAutoMode'    -Value '1'
Set-ItemProperty -Path $regPath -Name 'OverlayVisible' -Value '1'
Set-ItemProperty -Path $regPath -Name 'OcrDiskCache'   -Value '0'   # recompute every scene

if (Test-Path $dump) { Remove-Item $dump }
$env:FMS_OCR_DIAG = '1'
$env:FMS_OCR_DIAG_FILE = $dump

try {
    $viewer = $null
    foreach ($s in $scenes) {
        if ($null -eq $viewer) {
            $viewer = Start-Process -FilePath $exe -ArgumentList "`"$($s.FullName)`"" -PassThru
            Start-Sleep -Seconds 6
        } else {
            # A second launch forwards the path to the running window (single instance).
            Start-Process -FilePath $exe -ArgumentList "`"$($s.FullName)`"" -Wait
        }
        Start-Sleep -Seconds $SecondsPerScene
        Write-Host ("  {0,-32} done" -f $s.Name)
    }
} finally {
    Get-Process -Name FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    foreach ($n in $restore.Keys) {
        if ($null -ne $restore[$n]) { Set-ItemProperty -Path $regPath -Name $n -Value $restore[$n] }
    }
    Write-Host 'Settings restored.'
}

# --- judge --------------------------------------------------------------------

if (-not (Test-Path $dump)) {
    Write-Error 'The viewer produced no diagnostics dump.' -ErrorAction Continue; exit 3
}

$bytes = [IO.File]::ReadAllBytes($dump)
$failures = @()
if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    $failures += 'the dump starts with a BOM - a plain JSON parser cannot read line 1'
}

$seen = @{}
$measured = 0
$blank = @()
foreach ($line in Get-Content $dump) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $doc = $line | ConvertFrom-Json
    $name = Split-Path $doc.file -Leaf
    $seen[$name] = $true
    $frame = [double]$doc.width * $doc.height

    $maxCov = 0.0; $grown = 0; $trimmed = 0; $maxBg = -1
    foreach ($b in $doc.blocks) {
        $plateH = $b.plateY1 - $b.y0
        $cov = (($b.x1 - $b.x0) * [double]$plateH) / $frame
        if ($cov -gt $maxCov) { $maxCov = $cov }
        if ($plateH -gt ($b.y1 - $b.y0)) { $grown++ }
        if ($b.truncated) { $trimmed++ }
        $bg = Get-Luma $b.background
        if ($bg -gt $maxBg) { $maxBg = $bg }
    }

    # "dropped" is an array when the run measured its refusals, null when nobody did (a
    # document served from the disk cache). Distinguishing the two is the whole point:
    # reporting "not measured" as "nothing was dropped" is how a gate goes green while
    # excluding every scene that found no text (section 16.5).
    $hasDropField = $doc.PSObject.Properties.Name -contains 'dropped'
    $dropCount = if (-not $hasDropField -or $null -eq $doc.dropped) { -1 } else { @($doc.dropped).Count }
    $dropText  = if ($dropCount -lt 0) { 'n/a' } else { $dropCount.ToString() }

    Write-Host ("{0,-32} plates {1,2}  maxCover {2}  grown {3}  trimmed {4}  bgLuma {5}  dropped {6}" -f
                $name, $doc.blocks.Count, $maxCov.ToString('F4'), $grown, $trimmed, $maxBg, $dropText)

    # A scene with no plates measures NOTHING - its maxCover of 0 and its trimmed of 0 are
    # the flattering zeros, not a clean result - so it is reported apart and never folded
    # into the aggregates below.
    if ($doc.blocks.Count -eq 0) {
        $blank += $name
        if ($dropCount -gt 0) {
            $rules = (@($doc.dropped) | ForEach-Object { $_.rule } | Sort-Object -Unique) -join ', '
            Write-Host ("  no plates; the thresholds refused {0} line(s): {1}" -f $dropCount, $rules)
            foreach ($d in @($doc.dropped)) {
                Write-Host ("    [{0}] {1}" -f $d.rule, $d.text)
            }
        } elseif ($dropCount -eq 0) {
            Write-Host '  no plates, and no threshold refused anything - the engine read nothing here.'
        }
        $failures += "${name}: no plates at all (dropped: $dropText)"
        continue
    }
    $measured++

    # Section 11: no plate may cover more of the frame than the dissolve rule allows.
    if ($maxCov -gt 0.52) { $failures += "${name}: a plate covers $($maxCov.ToString('F4')) of the frame" }
    # Section 2: trimming is the last rung and should not be reached on these scenes.
    if ($trimmed -gt 0) { $failures += "${name}: $trimmed plate(s) had text trimmed" }
    # Section 9: what the filter threw away, on a scene that DID produce plates. Not a
    # failure by itself - some refusals are the filter doing its job - but it is the number
    # the acceptance item asks for, and it used to be unobtainable without a second build.
    if ($dropCount -gt 0) {
        foreach ($d in @($doc.dropped)) {
            Write-Host ("  dropped [{0}] {1}" -f $d.rule, $d.text)
        }
    }

    if ($expected.ContainsKey($name)) {
        $e = $expected[$name]
        if ($doc.blocks.Count -lt $e.MinPlates) {
            $failures += "${name}: $($doc.blocks.Count) plates, expected at least $($e.MinPlates) - $($e.Why)"
        }
        if ($e.MaxPlates -gt 0 -and $doc.blocks.Count -gt $e.MaxPlates) {
            $failures += "${name}: $($doc.blocks.Count) plates, expected at most $($e.MaxPlates) - $($e.Why)"
        }
        if ($e.ContainsKey('MaxBgLuma') -and $maxBg -gt $e.MaxBgLuma) {
            $failures += "${name}: plate background luma $maxBg, expected under $($e.MaxBgLuma) - $($e.Why)"
        }
    }
}

foreach ($s in $scenes) {
    if (-not $seen.ContainsKey($s.Name)) { $failures += "$($s.Name): never reached the overlay - no dump line" }
}

Write-Host ''
Write-Host ("Scenes: {0} dumped, {1} measured, {2} without plates{3}" -f
            $seen.Count, $measured, $blank.Count,
            $(if ($blank.Count -gt 0) { " ($($blank -join ', '))" } else { '' }))

if ($failures.Count -gt 0) {
    Write-Host 'FAILED:'
    $failures | ForEach-Object { Write-Host "  - $_" }
    Write-Error "$($failures.Count) check(s) failed." -ErrorAction Continue
    exit 4
}
Write-Host 'PASS - every scene met its expectation.'
exit 0
