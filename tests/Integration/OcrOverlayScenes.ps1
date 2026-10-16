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

    The viewer's OCR settings are borrowed for the run and put back afterwards. A viewer also
    writes its own state at start and exit - the last file, the recent folders, the run counters -
    so the whole settings key is exported first and imported back last: without that, the next
    launch of the owner's viewer would open one of these scenes. The export stays in WorkDir.

    A scene that produced NO plates is reported apart and fails, never averaged in: its
    "largest plate covers 0.0000 of the frame" and its "0 plates released" are the same numbers
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

.PARAMETER JudgeDump
    Judge an existing dump file and nothing else: no scenes are drawn, no viewer is started,
    no setting is touched. For re-reading a run, and for proving the judge itself - a dump line
    edited by hand must fail the way the rule says it does (ticket 033 Ф-2).

.PARAMETER ForcedOnly
    Run only the forced-release scenes (below) and skip the nine scenes that use the
    translator the user has configured. About two minutes instead of eight.

    The forced scenes are the one place the harness controls the translator. Releasing the
    plate (OCR-OVERLAY rule 9, the last rung) needs a translation longer than its source can
    hold, and no real translator promises that, so a second viewer run points the provider at
    a stub LibreTranslate on 127.0.0.1 that answers a long text for a source containing FORCE
    and echoes any other source. Two scenes share one geometry, a small plate with no room
    below it: the one that must release and the control that must not, so a pass shows the
    judge telling the two apart and not merely counting a flag (ticket 033 F2).

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every check passed. 1 = a check failed. 2 = could not
    verify: no built viewer, no scenes, a viewer already running (its settings and environment
    would be the ones in force), or the viewer produced no dump. The last line is the verdict,
    `OcrOverlayScenes: <WORD> (..)`. Run it by hand or through tools\Run-AllTests.ps1 -OcrScenes.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-ocr-scenes'),
    [int]$SecondsPerScene = 45,
    [switch]$KeepScenes,
    [string]$JudgeDump = '',
    [switch]$ForcedOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

                       # ..\Integration -> ..\tests -> the repository root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe      = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'
$dump     = Join-Path $WorkDir 'diag.jsonl'
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

# An unexpected terminating error must not surface as exit 1, which reads as a failed check. The
# finally block below that puts the OCR settings back still runs first.
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'OcrOverlayScenes' 2 'the harness itself failed'
}

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

    # 18 - S7: one balloon either side of a figure. Tesseract's own layout analysis returns
    #      BOTH as one 1574x189 text line, so before the split this drew a single plate across
    #      the artwork carrying both replies as one sentence. Two plates, and neither may
    #      reach into the x 900..1150 band the figure occupies.
    $c = New-Canvas 2048 1400 $paper
    $fig = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 78, 96))
    $c.Graphics.FillEllipse($fig, 930, 560, 190, 190)
    $c.Graphics.FillRectangle($fig, 950, 760, 150, 420)
    $fig.Dispose()
    Add-Text $c 'WHO GOES THERE' 200 600 56 $ink
    Add-Text $c 'ONLY THE WIND' 1330 600 56 $ink
    Save-Canvas $c (Join-Path $dir 'e-stitched-balloons.png')

    # 18 (paired) - two columns over a narrow gutter, every row stitched across it. This is the
    #      case cutting ALONE makes worse: the runs come out interleaved and a single-pass
    #      clustering fragments both columns, so it is really a check on the regrouping.
    $c = New-Canvas 1400 1000 $paper
    'the door was open', 'and the lamp still lit', 'nobody had waited' |
        ForEach-Object -Begin { $y = 200 } -Process { Add-Text $c $_ 100 $y 36 $ink; $y += 70 }
    'he counted the steps', 'twice on the way down', 'and lost the number' |
        ForEach-Object -Begin { $y = 200 } -Process { Add-Text $c $_ 760 $y 36 $ink; $y += 70 }
    Save-Canvas $c (Join-Path $dir 'e2-stitched-columns.png')
}

# The two forced-release scenes (ticket 033 F2). Same canvas, same line, same place: a single
# line low in a short picture, so the plate's growth budget is a few dozen pixels. What
# differs is the TRANSLATION the stub below hands back, chosen by the word FORCE in the source -
# one long enough that no rung of the ladder can hold it, one that is the source itself.
function Build-ForcedScenes([string]$dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $c = New-Canvas 900 360 $paper
    Add-Text $c 'FORCE RELEASE' 120 270 46 $ink
    Save-Canvas $c (Join-Path $dir 'f1-forced-release.png')

    $c = New-Canvas 900 360 $paper
    Add-Text $c 'SHORT AND FIT' 120 270 46 $ink
    Save-Canvas $c (Join-Path $dir 'f2-forced-control.png')
}

# A LibreTranslate on 127.0.0.1 that the viewer is pointed at for the forced scenes. It runs in
# a runspace of this process, listens on loopback only, and exists for the length of one run.
# POST /translate answers one string per q item; GET /languages answers 200 (the viewer's probe).
function Start-StubTranslator {
    $tcp = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $tcp.Start(); $port = $tcp.LocalEndpoint.Port; $tcp.Stop()

    $state = [hashtable]::Synchronized(@{ Requests = 0; Listener = $null; Error = '' })
    $long  = 'This translation is deliberately much longer than the text it replaces. ' * 10

    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($port, $state, $long)
        $l = [System.Net.HttpListener]::new()
        $l.Prefixes.Add("http://127.0.0.1:$port/")
        try {
            $l.Start()
            $state.Listener = $l
            while ($l.IsListening) {
                $ctx = $l.GetContext()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes('{}')
                    if ($ctx.Request.HttpMethod -eq 'POST' -and $ctx.Request.Url.AbsolutePath -eq '/translate') {
                        $raw = [IO.StreamReader]::new($ctx.Request.InputStream, [Text.Encoding]::UTF8).ReadToEnd()
                        $q = @((ConvertFrom-Json -InputObject $raw).q)
                        $out = [string[]]@($q | ForEach-Object { if ("$_" -match 'FORCE') { $long } else { "$_" } })
                        $json = ConvertTo-Json -InputObject @{ translatedText = $out } -Compress -Depth 4
                        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
                        $state.Requests = $state.Requests + 1
                    }
                    $ctx.Response.ContentType = 'application/json; charset=utf-8'
                    $ctx.Response.ContentLength64 = $bytes.Length
                    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                } finally {
                    $ctx.Response.Close()
                }
            }
        } catch {
            # Stop() from the harness ends a blocked GetContext with an exception; that is the
            # normal exit. Anything else is kept so the verdict can say the stub itself failed.
            if ($null -eq $state.Listener -or $l.IsListening) { $state.Error = $_.Exception.Message }
        }
    }).AddArgument($port).AddArgument($state).AddArgument($long)
    $handle = $ps.BeginInvoke()

    $until = (Get-Date).AddSeconds(10)
    while ($null -eq $state.Listener -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 100 }
    if ($null -eq $state.Listener) {
        $ps.Dispose()
        throw "the stub translator did not start listening on 127.0.0.1: $($state.Error)"
    }
    @{ Port = $port; State = $state; Shell = $ps; Handle = $handle }
}

function Stop-StubTranslator($Stub) {
    if ($null -eq $Stub) { return }
    try { $Stub.State.Listener.Stop(); $Stub.State.Listener.Close() } catch { }
    try { [void]$Stub.Shell.EndInvoke($Stub.Handle) } catch { }
    $Stub.Shell.Dispose()
}

# Waits until the dump carries a line naming this scene, instead of sleeping the whole budget:
# a forced scene needs no translator round trip, so most of $SecondsPerScene would be dead time.
function Wait-ForDumpLine([string]$Leaf, [int]$Seconds) {
    $until = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $until) {
        try {
            if ((Test-Path $dump) -and (Select-String -Path $dump -SimpleMatch $Leaf -Quiet)) { return $true }
        } catch { }   # the viewer holds the file for the instant of an append
        Start-Sleep -Seconds 2
    }
    $false
}

# What each scene must produce. `MaxPlates` left at 0 means "no upper bound". `MinReleased`
# turns the general "no plate may reach the last rung" check round for a scene built to reach it.
$expected = @{
    'a-small-caps-balloon.png'  = @{ MinPlates = 1; MaxPlates = 1; Why = 'one balloon is one plate' }
    'a2-two-balloons.png'       = @{ MinPlates = 2; MaxPlates = 2; Why = 'two balloons stay two plates' }
    'b-heading-over-text.png'   = @{ MinPlates = 2; MaxPlates = 2; Why = 'the heading separates by type size' }
    'b2-uneven-inscription.png' = @{ MinPlates = 1; MaxPlates = 1; Why = 'uneven lines of one inscription hold together' }
    'c-window-list.png'         = @{ MinPlates = 6; MaxPlates = 0; Why = 'the page-wide plate dissolves per line' }
    'd-dark-panel.png'          = @{ MinPlates = 1; MaxPlates = 0; MaxBgLuma = 100; Why = 'a dark panel gets a dark plate' }
    'd2-display-lettering.png'  = @{ MinPlates = 1; MaxPlates = 0; MaxBgLuma = 100; Why = 'the ring test keeps the pair the right way round' }
    # MaxWidthShare is the check that matters here, not the count: a plate the full width of
    # the frame IS the defect, and two plates of the right count could still both be bars.
    'e-stitched-balloons.png'   = @{ MinPlates = 2; MaxPlates = 2; MaxWidthShare = 0.35; Why = 'the stitched line is cut, one plate per balloon' }
    'e2-stitched-columns.png'   = @{ MinPlates = 2; MaxPlates = 2; MaxWidthShare = 0.35; Why = 'the cut runs are regrouped into their two columns' }
    # The forced pair (ticket 033 F2): one geometry, two translations. The first must reach the
    # last rung and say so; the second, which falls under the general rule, must not.
    'f1-forced-release.png'     = @{ MinPlates = 1; MaxPlates = 1; MinReleased = 1; Why = 'a translation far longer than its box, with no room below, reaches the last rung' }
    'f2-forced-control.png'     = @{ MinPlates = 1; MaxPlates = 1; Why = 'the same geometry with a translation that fits does not' }
}

function Get-Luma([string]$rgb) {
    if ([string]::IsNullOrWhiteSpace($rgb)) { return -1 }
    $m = [regex]::Match($rgb, 'rgb\((\d+),(\d+),(\d+)\)')
    if (-not $m.Success) { return -1 }
    [int](([int]$m.Groups[1].Value * 299 + [int]$m.Groups[2].Value * 587 + [int]$m.Groups[3].Value * 114) / 1000)
}

# --- run ----------------------------------------------------------------------

$forcedScenes = @()
$stubRequests = -1   # -1: the stub did not run; 0 would mean the viewer never asked it anything

if ($JudgeDump) {
    # Judge only: the scenes are whatever the dump names, so "never reached the overlay" has
    # nothing to compare against and is skipped.
    Write-Subject 'OcrOverlayScenes' "judging $JudgeDump (no viewer run)"
    $dump = $JudgeDump
    $scenes = @()
} else {

$exeVersion = if (Test-Path $exe) { (Get-Item $exe).VersionInfo.FileVersion } else { 'missing' }
Write-Subject 'OcrOverlayScenes' "$exe ($exeVersion), scenes in $WorkDir"

if (-not (Test-Path $exe)) {
    Exit-Verdict 'OcrOverlayScenes' 2 "no viewer at $exe - run build.ps1 first"
}
if (Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue) {
    Exit-Verdict 'OcrOverlayScenes' 2 'a viewer is already running - stop it first, or its settings and environment win'
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
if (-not $KeepScenes -and -not $ForcedOnly) { Build-Scenes $WorkDir }
# The forced scenes are two small deterministic drawings: always redrawn, in a folder of their
# own so the scenes judged against the user's translator never list them.
$forcedDir = Join-Path $WorkDir 'forced'
Build-ForcedScenes $forcedDir

$mainScenes = @()   # not `= if (..) { @() }`: that assigns $null, and strict mode has no .Count on it
if (-not $ForcedOnly) { $mainScenes = @(Get-ChildItem $WorkDir -Filter *.png -File | Sort-Object Name) }
$forcedScenes = @(Get-ChildItem $forcedDir -Filter *.png -File | Sort-Object Name)
$scenes = @($mainScenes) + @($forcedScenes)
if (-not $ForcedOnly -and $mainScenes.Count -eq 0) { Exit-Verdict 'OcrOverlayScenes' 2 "no scenes in $WorkDir" }
Write-Host "Scenes: $($mainScenes.Count) with the configured translator, $($forcedScenes.Count) forced, in $WorkDir"

# Every setting borrowed is read first and put back after; one that did not exist is removed
# again, not left behind holding the value the run lent it.
$restore = @{}
function Lend-Setting([string]$Name, [string]$Value) {
    if (-not $restore.ContainsKey($Name)) {
        $restore[$Name] = (Get-ItemProperty -Path $regPath -Name $Name -ErrorAction SilentlyContinue).$Name
    }
    Set-ItemProperty -Path $regPath -Name $Name -Value $Value
}

if (Test-Path $dump) { Remove-Item $dump }
$env:FMS_OCR_DIAG = '1'
$env:FMS_OCR_DIAG_FILE = $dump

# The whole key, not only what is lent: see .DESCRIPTION. Without a usable export the per-value
# restore below is all there is, and the run says so.
$regKey    = 'HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$regBackup = Join-Path $WorkDir 'settings-before.reg'
& reg.exe export $regKey $regBackup /y 2>&1 | Out-Null
$haveBackup = ($LASTEXITCODE -eq 0) -and (Test-Path $regBackup) -and ((Get-Item $regBackup).Length -gt 0)
if (-not $haveBackup) { Write-Host 'No export of the settings key - only the values lent to the run will be put back.' }

$stub = $null
try {
    Lend-Setting 'OcrEnabled'     '1'
    Lend-Setting 'OcrAutoMode'    '1'
    Lend-Setting 'OverlayVisible' '1'
    Lend-Setting 'OcrDiskCache'   '0'   # recompute every scene

    # Phase 1 - the translator the user has configured.
    if ($mainScenes.Count -gt 0) {
        $viewer = $null
        foreach ($s in $mainScenes) {
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
        # The provider is read when a viewer starts, so phase 2 needs a new process.
        Get-Process -Name FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }

    # Phase 2 - the stub translator, for the scenes that must reach the last rung.
    if ($forcedScenes.Count -gt 0) {
        $stub = Start-StubTranslator
        Lend-Setting 'TranslateProvider'   'libretranslate'
        Lend-Setting 'TranslateEndpoint'   "http://127.0.0.1:$($stub.Port)"
        Lend-Setting 'TranslateTargetLang' 'en'
        Write-Host "Forced scenes: stub translator on 127.0.0.1:$($stub.Port)"
        $viewer = $null
        foreach ($s in $forcedScenes) {
            if ($null -eq $viewer) {
                $viewer = Start-Process -FilePath $exe -ArgumentList "`"$($s.FullName)`"" -PassThru
                Start-Sleep -Seconds 6
            } else {
                Start-Process -FilePath $exe -ArgumentList "`"$($s.FullName)`"" -Wait
            }
            $arrived = Wait-ForDumpLine $s.Name $SecondsPerScene
            Write-Host ("  {0,-32} {1}" -f $s.Name, $(if ($arrived) { 'done' } else { 'no dump line in time' }))
        }
        $stubRequests = [int]$stub.State.Requests
        if ($stub.State.Error) { Write-Host "  the stub reported: $($stub.State.Error)" }
    }
} finally {
    Get-Process -Name FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
    Stop-StubTranslator $stub
    Start-Sleep -Seconds 2
    foreach ($n in $restore.Keys) {
        if ($null -ne $restore[$n]) {
            Set-ItemProperty -Path $regPath -Name $n -Value $restore[$n]
        } else {
            Remove-ItemProperty -Path $regPath -Name $n -ErrorAction SilentlyContinue
        }
    }
    if ($haveBackup) {
        # Delete then import, so a value the viewer added during the run goes too. The export in
        # WorkDir is kept either way: if the import fails it is the only copy left.
        & reg.exe delete $regKey /f 2>&1 | Out-Null
        & reg.exe import $regBackup 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "  The settings import FAILED - the export is at $regBackup" }
    }
    Write-Host 'Settings restored.'
}

}   # end of the viewer run ($JudgeDump not given)

# --- judge --------------------------------------------------------------------

if (-not (Test-Path $dump)) {
    Exit-Verdict 'OcrOverlayScenes' 2 'the viewer produced no diagnostics dump'
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

    $maxCov = 0.0; $grown = 0; $released = 0; $unmeasured = 0; $maxBg = -1; $maxWidthShare = 0.0
    foreach ($b in $doc.blocks) {
        $plateH = $b.plateY1 - $b.y0
        $cov = (($b.x1 - $b.x0) * [double]$plateH) / $frame
        if ($cov -gt $maxCov) { $maxCov = $cov }
        # Width alone, because the S7 defect is wide but SHORT: a bar carrying two balloons
        # measured 1574x189 on a 2048x1400 scene, which is 0.10 of the frame against a 0.52
        # coverage bound - the rule that would have caught it never fires.
        $ws = ($b.x1 - $b.x0) / [double]$doc.width
        if ($ws -gt $maxWidthShare) { $maxWidthShare = $ws }
        if ($plateH -gt ($b.y1 - $b.y0)) { $grown++ }
        # "released" since ticket 025 removed the ellipsis; "truncated" is what a dump written
        # by an older build calls the same condition. A block with NEITHER was not measured,
        # and counting it as 0 is the flattering zero OCR-OVERLAY rule 14 forbids - the read
        # of the old name alone went on reporting 0 for every scene after the rename (033 F2).
        $names = $b.PSObject.Properties.Name
        if ($names -contains 'released') {
            if ($b.released) { $released++ }
        } elseif ($names -contains 'truncated') {
            if ($b.truncated) { $released++ }
        } else {
            $unmeasured++
        }
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

    $releasedText = if ($unmeasured -gt 0) { 'n/a' } else { $released.ToString() }
    Write-Host ("{0,-32} plates {1,2}  maxCover {2}  maxWidth {3}  grown {4}  released {5}  bgLuma {6}  dropped {7}" -f
                $name, $doc.blocks.Count, $maxCov.ToString('F4'), $maxWidthShare.ToString('F2'),
                $grown, $releasedText, $maxBg, $dropText)

    # A scene with no plates measures NOTHING - its maxCover of 0 and its released of 0 are
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
    # Rule 14: a plate whose last-rung flag is missing was not measured - a failure, not a 0.
    if ($unmeasured -gt 0) { $failures += "${name}: $unmeasured plate(s) carry no released flag - not measured" }
    # Section 2: releasing is the last rung of the ladder and should not be reached on these
    # scenes - except on the one built to reach it (ticket 033 F2), where NOT reaching it is the
    # failure: a scene that cannot force the rung proves nothing about the rung.
    $mustRelease = $expected.ContainsKey($name) -and $expected[$name].ContainsKey('MinReleased')
    if ($released -gt 0 -and -not $mustRelease) { $failures += "${name}: $released plate(s) reached the last rung (released)" }
    if ($mustRelease -and $unmeasured -eq 0 -and $released -lt $expected[$name].MinReleased) {
        $failures += "${name}: $released plate(s) released, expected at least $($expected[$name].MinReleased) - $($expected[$name].Why)"
    }
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
        if ($e.ContainsKey('MaxWidthShare') -and $maxWidthShare -gt $e.MaxWidthShare) {
            $failures += "${name}: a plate spans $($maxWidthShare.ToString('F2')) of the width, expected under $($e.MaxWidthShare) - $($e.Why)"
        }
    }
}

foreach ($s in $scenes) {
    if (-not $seen.ContainsKey($s.Name)) { $failures += "$($s.Name): never reached the overlay - no dump line" }
}
# A forced scene's long translation comes from the stub; if the stub was never asked, the
# viewer used some other translator and the forced scenes measured something else.
if ($forcedScenes.Count -gt 0 -and $stubRequests -eq 0) {
    $failures += 'the viewer never asked the stub translator - the forced scenes did not run against it'
}

Write-Host ''
Write-Host ("Scenes: {0} dumped, {1} measured, {2} without plates{3}" -f
            $seen.Count, $measured, $blank.Count,
            $(if ($blank.Count -gt 0) { " ($($blank -join ', '))" } else { '' }))

$sceneCount = if ($JudgeDump) { $seen.Count } else { $scenes.Count }
if ($failures.Count -gt 0) {
    Write-Host 'FAILED:'
    $failures | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'OcrOverlayScenes' 1 "$($failures.Count) check(s) failed over $sceneCount scene(s)"
}
Exit-Verdict 'OcrOverlayScenes' 0 "$sceneCount scene(s), every one met its expectation"
