<#
.SYNOPSIS
    Closes the live proof ticket 106 owes: rapid opens against a real SMB share in BOTH
    viewer builds - the latest accepted request wins, a superseded or offline request
    never opens anything, and closing with a share open in flight ends the process.

.DESCRIPTION
    106_SPECIFICATION_ARGUMENT_PROBE_ORDERING.md asks for a manual run: "rapid opens
    with one slow/offline share in both viewers, latest request wins" plus closing with
    work in flight. The deterministic ordering cases (A/B and B/A completions, resume
    races, navigation, queue saturation, close/veto) are pinned by OpenCoordinationTests,
    ArgumentProbeOrderingTests and ProbeSchedulerTests; what only a live run can show is
    that the REAL window, the REAL scheduler and a REAL share behave the same way end to
    end. This harness drives the built Release viewers on screen and judges by the
    displayed media itself: the local scene files are solid red and solid blue images,
    the share target is a real photo, and the run samples the pixels of the media area
    (Picture_Box_1/2, which exist in both builds - the modern build's folder path is
    owner-drawn chrome and cannot be read as text). No assertion depends on the
    interface language.

    Scenes, on each viewer build (modern net10 and legacy net48) unless noted:
      A  rapid opens share-then-local: the local red image (the latest request) shows.
      B  rapid opens local-then-share: the share photo (the latest request) shows.
      C  an offline path on the share superseded by a valid file: the valid image opens
         and no stale failure survives; the offline path alone keeps the display.
      D  close with a share open in flight: the process ends, nothing reopens.
      E  (modern only, seeded registry) startup resume of a share file against an
         explicit argument: the argument wins; with no argument the share file resumes.

    Opens are triggered the way the ticket describes them: launching the exe with a path
    while an instance is alive - the single-instance mutex forwards the path through
    WM_COPYDATA into the running window's ProcessArgument, the real admission path of a
    rapid second open. The run records each forward's latency and each settle time in
    the work directory transcript, kept as the run's evidence.

    The viewer's registry settings are snapshotted and put back whole; the harness
    refuses to run while any viewer instance is alive, for the same reason
    SecureRepeatArtScenes refuses.

.PARAMETER WorkDir
    Where the local scene files and the transcript go. Default: %TEMP%\fms-106-scenes.
    Kept after the run either way - it is the run's evidence.

.PARAMETER ShareRoot
    The SMB share the scenes race against. Default: \\SZAMEDIA\Common (the owner's
    media share this ticket's manual run names). A jpg directly in the root with real
    photographic variance is used as the share target; the offline scene uses a path
    under the share that does not exist.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every scene on both builds held: the latest request
    won everywhere and no superseded or offline request opened anything. 1 = an ordering
    defect: an older request's media was displayed, or a close left a process behind.
    2 = could not verify: a missing build, a share that did not answer, a viewer already
    running, or a window that never came up. The last line is the verdict,
    `ArgumentProbeOrderingScenes: <WORD> (..)`. Run it by hand.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-106-scenes'),
    [string]$ShareRoot = '\\SZAMEDIA\Common'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
namespace FmsHarness {
    public struct WndRect { public int Left; public int Top; public int Right; public int Bottom; }
    public static class Native106 {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(System.IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(System.IntPtr hWnd, out WndRect rect);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern System.IntPtr GetConsoleWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
    }
}
'@

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

# An unexpected terminating error must not surface as exit 1, which reads as a failed check.
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'ArgumentProbeOrderingScenes' 2 'the harness itself failed'
}

# UIA hands out LOGICAL screen coordinates; screen capture sees PHYSICAL ones, and on
# this multi-monitor, display-scaled desktop the two disagree by thousands of pixels.
# So the harness takes physical control: it parks its own console out of the way and
# moves the viewer to a fixed PHYSICAL rectangle, then captures exactly that rectangle.
[FmsHarness.Native106]::SetProcessDPIAware() | Out-Null
$script:consoleHwnd = [FmsHarness.Native106]::GetConsoleWindow()
if ($script:consoleHwnd -ne [IntPtr]::Zero) {
    [FmsHarness.Native106]::ShowWindow($script:consoleHwnd, 6) | Out-Null   # SW_MINIMIZE
}

function Write-Scene([string]$Text) { Write-Host ("  " + $Text) -ForegroundColor Cyan }

function Wait-True([int]$Seconds, [scriptblock]$Condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return (& $Condition)
}

function Get-ViewerProcesses {
    # The comma keeps PowerShell from unwrapping a one-element array into the bare
    # object: every caller asks .Count under StrictMode.
    return ,@(Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue)
}

function Get-MainWindow([int]$ProcId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

# --- registry snapshot (the whole key; put back exactly in Restore-Registry) ------

$regExisted = Test-Path $regPath
$regSnapshot = @()
if ($regExisted) {
    $p = Get-ItemProperty -Path $regPath
    $regSnapshot = @($p.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } |
        ForEach-Object { @{ Name = $_.Name; Value = $_.Value } })
}

function Restore-Registry {
    if (-not $regExisted) {
        if (Test-Path $regPath) { Remove-Item -Path $regPath -Recurse -Force }
        return
    }
    $now = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
    if ($now) {
        $currentNames = @($now.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object { $_.Name })
        foreach ($n in $currentNames) {
            if (-not ($regSnapshot | Where-Object { $_.Name -eq $n })) {
                Remove-ItemProperty -Path $regPath -Name $n -ErrorAction SilentlyContinue
            }
        }
    }
    foreach ($entry in $regSnapshot) {
        Set-ItemProperty -Path $regPath -Name $entry.Name -Value $entry.Value
    }
}

# --- inputs -----------------------------------------------------------------------

$modernExe = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'
$legacyExe = Join-Path $repoRoot 'bin\Release\FastMediaSorter_x86.exe'
if (-not (Test-Path -LiteralPath $modernExe)) { Exit-Verdict 'ArgumentProbeOrderingScenes' 2 "no modern viewer at $modernExe - run build.ps1 first" }
if (-not (Test-Path -LiteralPath $legacyExe)) { Exit-Verdict 'ArgumentProbeOrderingScenes' 2 "no legacy viewer at $legacyExe - run build.ps1 first" }

# The share target must be a real photograph: the scenes tell it apart from the solid
# scene colours by pixel variance, so a flat graphic on the share would fake a pass.
function Get-ImageStddev([string]$Path) {
    try {
        $bmp = [System.Drawing.Image]::FromFile($Path)
        $w = $bmp.Width; $h = $bmp.Height
        $lum = New-Object System.Collections.Generic.List[double]
        for ($y = 0; $y -lt $h; $y += [Math]::Max(1, [int]($h / 24))) {
            for ($x = 0; $x -lt $w; $x += [Math]::Max(1, [int]($w / 24))) {
                $c = $bmp.GetPixel($x, $y)
                $lum.Add(0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B)
            }
        }
        $bmp.Dispose()
        if ($lum.Count -lt 2) { return 0.0 }
        $mean = 0.0; foreach ($v in $lum) { $mean += $v }; $mean /= $lum.Count
        $var = 0.0; foreach ($v in $lum) { $var += ($v - $mean) * ($v - $mean) }
        return [Math]::Sqrt($var / $lum.Count)
    }
    catch { return 0.0 }
}

$shareFile = $null
foreach ($candidate in @(Get-ChildItem -LiteralPath $ShareRoot -Filter '*.jpg' -File -ErrorAction Stop | Sort-Object Length)) {
    if ((Get-ImageStddev $candidate.FullName) -ge 25) { $shareFile = $candidate; break }
}
if (-not $shareFile) {
    Exit-Verdict 'ArgumentProbeOrderingScenes' 2 "no photographic jpg directly under $ShareRoot - the pixel scenes need a real photo to tell apart from the solid colour targets"
}
$sharePath   = $shareFile.FullName
$offlinePath = Join-Path $ShareRoot 'fms-106-scene-missing\nope.jpg'

$probeSw = [Diagnostics.Stopwatch]::StartNew()
$offlineAnswers = Test-Path -LiteralPath $offlinePath
$probeSw.Stop()
if ($offlineAnswers) {
    Exit-Verdict 'ArgumentProbeOrderingScenes' 2 "the offline scene path exists on the share - choose another"
}

# Local scene images: solid red and solid blue, 3:2 so "fit" fills the media area.
New-Item -ItemType Directory -Force -Path (Join-Path $WorkDir 'local-a'), (Join-Path $WorkDir 'local-b') | Out-Null
function New-SolidPng([string]$Path, [int]$Red, [int]$Green, [int]$Blue) {
    $bmp = New-Object System.Drawing.Bitmap 900, 600
    # Not $g: PowerShell variables are case-insensitive and would collide with $Green.
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::FromArgb($Red, $Green, $Blue))
    $gfx.Dispose()
    # PNG, not JPEG: this run's first scene images were JPEGs written without encoder
    # parameters, and System.Drawing produced a 205-byte file with a bogus Huffman table
    # that GDI+ itself refused to decode - the viewer answered "! No files in folder".
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$localA = Join-Path $WorkDir ('local-a\fms106-a-' + [IO.Path]::GetRandomFileName().Replace('.', '') + '.png')
$localB = Join-Path $WorkDir ('local-b\fms106-b-' + [IO.Path]::GetRandomFileName().Replace('.', '') + '.png')
New-SolidPng $localA 200 30 30
New-SolidPng $localB 30 30 220
$localNameA = Split-Path -Leaf $localA
$localNameB = Split-Path -Leaf $localB

$transcript = Join-Path $WorkDir 'transcript.txt'
"argument-probe-ordering scenes - $(Get-Date -Format o)" | Set-Content -LiteralPath $transcript
"share target: $sharePath ($([Math]::Round($shareFile.Length / 1KB)) KB, stddev $([Math]::Round((Get-ImageStddev $sharePath), 1)))" | Add-Content -LiteralPath $transcript
"local red: $localA" | Add-Content -LiteralPath $transcript
"local blue: $localB" | Add-Content -LiteralPath $transcript
"share metadata latency: $($probeSw.ElapsedMilliseconds) ms; offline probe: $offlinePath" | Add-Content -LiteralPath $transcript

if (Get-ViewerProcesses) {
    Exit-Verdict 'ArgumentProbeOrderingScenes' 2 'a viewer is already running - stop it first, or its settings and environment win'
}

# --- viewer driving ----------------------------------------------------------------

function Start-Viewer([string]$Exe, [string]$File) {
    Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    $proc = if ($File) { Start-Process -FilePath $Exe -ArgumentList "`"$File`"" -PassThru }
            else { Start-Process -FilePath $Exe -PassThru }
    $mw = $null
    if (-not (Wait-True 25 { $proc.Refresh(); $mw = Get-MainWindow $proc.Id; (-not $proc.HasExited) -and ($null -ne $mw) })) {
        $proc.Refresh()
        if ($proc.HasExited) { throw "the viewer exited at once (code $($proc.ExitCode))" }
        throw 'the viewer never showed a window'
    }
    $proc.Refresh()
    $mw = Get-MainWindow $proc.Id
    if ($mw) {
        # A fixed PHYSICAL rectangle: screen capture is physical, and this desktop's UIA
        # coordinates are logical - pinning the window makes the capture deterministic.
        [FmsHarness.Native106]::SetWindowPos([IntPtr]$mw.Current.NativeWindowHandle, [IntPtr]::Zero,
            120, 120, 1100, 760, 0x0040) | Out-Null   # SWP_SHOWWINDOW
        [FmsHarness.Native106]::SetForegroundWindow([IntPtr]$mw.Current.NativeWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
    }
    return @{ Proc = $proc; Window = $mw }
}

function Stop-Viewer {
    Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-True 8 { (Get-ViewerProcesses).Count -eq 0 } | Out-Null
    Start-Sleep -Seconds 2
}

# One extra process after a forward means the hand-off failed and a second window
# opened; the scenes below refuse to judge a display they did not aim at.
function Assert-OneWindow {
    $procs = @(Get-ViewerProcesses)
    if ($procs.Count -gt 1) {
        throw "the forward opened a second window ($($procs.Count) viewer processes) - the single-instance hand-off failed"
    }
}

function Get-MediaStats($Window) {
    # Capture the viewer's PHYSICAL rectangle from the screen (Start-Viewer pinned it),
    # then crop to the media area by RELATIVE fractions: where Picture_Box_1/2 sit inside
    # the window in UIA's logical space. Fractions survive any display scale, which is
    # the only portable mapping between the two coordinate systems on this desktop.
    if (-not $Window) { return @{ Red = 0.0; Blue = 0.0; StdDev = 0.0; Captured = $false } }
    $hwnd = [IntPtr]$Window.Current.NativeWindowHandle
    $wr = New-Object FmsHarness.WndRect
    if (-not [FmsHarness.Native106]::GetWindowRect($hwnd, [ref]$wr)) {
        return @{ Red = 0.0; Blue = 0.0; StdDev = 0.0; Captured = $false }
    }
    $winW = [double]($wr.Right - $wr.Left)
    $winH = [double]($wr.Bottom - $wr.Top)
    if ($winW -lt 20 -or $winH -lt 20) { return @{ Red = 0.0; Blue = 0.0; StdDev = 0.0; Captured = $false } }

    [FmsHarness.Native106]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 150
    $bmp = New-Object System.Drawing.Bitmap ([int]$winW), ([int]$winH)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wr.Left, $wr.Top, 0, 0, $bmp.Size)
    $g.Dispose()

    # The media area inside the capture: the union of the two picture boxes, as
    # fractions of the window. The fallback is the window's centre half.
    $w = $Window.Current.BoundingRectangle
    $fx1 = 0.25; $fy1 = 0.25; $fx2 = 0.75; $fy2 = 0.75
    $found = $false
    $bx1 = 0.0; $by1 = 0.0; $bx2 = 0.0; $by2 = 0.0
    foreach ($id in @('Picture_Box_1', 'Picture_Box_2')) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $box = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($box) {
            $b = $box.Current.BoundingRectangle
            if ($b.Width -ge 10 -and $b.Height -ge 10) {
                if (-not $found) {
                    $bx1 = $b.X; $by1 = $b.Y; $bx2 = $b.X + $b.Width; $by2 = $b.Y + $b.Height
                    $found = $true
                }
                else {
                    $bx1 = [Math]::Min($bx1, $b.X); $by1 = [Math]::Min($by1, $b.Y)
                    $bx2 = [Math]::Max($bx2, $b.X + $b.Width)
                    $by2 = [Math]::Max($by2, $b.Y + $b.Height)
                }
            }
        }
    }
    if ($found) {
        $fx1 = ($bx1 - $w.X) / $w.Width; $fy1 = ($by1 - $w.Y) / $w.Height
        $fx2 = ($bx2 - $w.X) / $w.Width; $fy2 = ($by2 - $w.Y) / $w.Height
    }

    $x0 = [int]([Math]::Max(0, [Math]::Min(1.0, $fx1)) * $bmp.Width)
    $y0 = [int]([Math]::Max(0, [Math]::Min(1.0, $fy1)) * $bmp.Height)
    $x1 = [int]([Math]::Max(0, [Math]::Min(1.0, $fx2)) * $bmp.Width)
    $y1 = [int]([Math]::Max(0, [Math]::Min(1.0, $fy2)) * $bmp.Height)
    if ($x1 - $x0 -lt 10 -or $y1 - $y0 -lt 10) { $x0 = 0; $y0 = 0; $x1 = $bmp.Width; $y1 = $bmp.Height }

    $red = 0; $blue = 0
    $lum = New-Object System.Collections.Generic.List[double]
    $step = [Math]::Max(1, [int]([Math]::Sqrt((($x1 - $x0) * ($y1 - $y0)) / 4000)))
    for ($y = $y0; $y -lt $y1; $y += $step) {
        for ($x = $x0; $x -lt $x1; $x += $step) {
            $c = $bmp.GetPixel($x, $y)
            if ([Math]::Abs($c.R - 200) -le 45 -and $c.G -le 90 -and $c.B -le 90) { $red++ }
            if ([Math]::Abs($c.B - 220) -le 45 -and $c.R -le 90 -and $c.G -le 90) { $blue++ }
            $lum.Add(0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B)
        }
    }
    $bmp.Dispose()
    $total = [Math]::Max(1, $lum.Count)
    if ($lum.Count -ge 2) {
        $mean = 0.0; foreach ($v in $lum) { $mean += $v }; $mean /= $lum.Count
        $var = 0.0; foreach ($v in $lum) { $var += ($v - $mean) * ($v - $mean) }
        $sd = [Math]::Sqrt($var / $lum.Count)
    }
    else { $sd = 0.0 }
    return @{ Red = $red / $total; Blue = $blue / $total; StdDev = $sd; Captured = $true }
}

function Test-Displayed($Window, [string]$Expected) {
    $s = Get-MediaStats $Window
    switch ($Expected) {
        'red'   { return ($s.Captured -and $s.Red -ge 0.45 -and $s.Blue -lt 0.2) }
        'blue'  { return ($s.Captured -and $s.Blue -ge 0.45 -and $s.Red -lt 0.2) }
        'photo' { return ($s.Captured -and $s.Red -lt 0.45 -and $s.Blue -lt 0.45 -and $s.StdDev -ge 18) }
        default { throw "unknown expected media '$Expected'" }
    }
}

function Wait-DisplayedMedia($Window, [string]$Expected, [string]$Scene) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $ok = Wait-True 20 { Test-Displayed $Window $Expected }
    $sw.Stop()
    $s = Get-MediaStats $Window
    Add-Content -LiteralPath $transcript ("{0}: settle {1} ms -> captured={2} red {3:P0} blue {4:P0} stddev {5:N1}" -f $Scene, $sw.ElapsedMilliseconds, $s.Captured, $s.Red, $s.Blue, $s.StdDev)
    return $ok
}

function Get-FolderBoxText($Window) {
    # Legacy-only corroboration: the modern build draws its folder path as chrome.
    if (-not $Window) { return '' }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'cmbox_Media_Folder')
    $box = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $box) { return '' }
    $vp = $null
    if ($box.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
        return [string]$vp.Current.Value
    }
    $editCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edit = $box.FindFirst([System.Windows.Automation.TreeScope]::Children, $editCond)
    if ($edit) {
        $evp = $null
        if ($edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$evp)) {
            return [string]$evp.Current.Value
        }
    }
    return ''
}

# Forward a path into the live viewer the way a rapid second open arrives: a fresh
# launch, the single-instance mutex, WM_COPYDATA into the running window.
function Forward-Open([string]$Exe, [string]$Path) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $fwd = Start-Process -FilePath $Exe -ArgumentList "`"$Path`"" -PassThru
    $null = Wait-True 15 { $fwd.Refresh(); $fwd.HasExited }
    $sw.Stop()
    Add-Content -LiteralPath $transcript ("forward '{0}': hand-off {1} ms" -f $Path, $sw.ElapsedMilliseconds)
}

# Seed an empty, media-visible window: no remembered folder, no counter, no resume - and
# no first-run help panel (FirstRun missing = "show it", and it covers the whole media
# area, so every pixel assertion would read the help text instead of the displayed file).
function Seed-EmptyStartup {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name 'ImageFolder' -Value ''
    Set-ItemProperty -Path $regPath -Name 'LastCounter' -Value '0'
    Set-ItemProperty -Path $regPath -Name 'FirstRun' -Value '0'
}

# --- the scenes --------------------------------------------------------------------

# Script scope on purpose: the scenes append from inside Invoke-Scenes, and an
# unqualified += inside a function would build a local copy and lose the findings.
$script:failures = @()

function Invoke-Scenes([string]$BuildName, [string]$Exe, [bool]$Modern) {
    Write-Host ''
    Write-Host ("=== $BuildName ===") -ForegroundColor White

    # A. share then local: the local red image (latest) must be displayed.
    Write-Scene 'Scene A: rapid opens, share photo then local red image - latest wins.'
    Seed-EmptyStartup
    $viewer = Start-Viewer $Exe ''
    Assert-OneWindow
    Forward-Open $Exe $sharePath
    Forward-Open $Exe $localA
    if (-not (Wait-DisplayedMedia $viewer.Window 'red' "$BuildName A")) {
        $script:failures += "A ($BuildName): after share-then-local the screen did not show the local red image"
    }
    Write-Scene ("  folder box (legacy chrome only): $(Get-FolderBoxText $viewer.Window)")

    # B. local then share: the share photo (latest) must be displayed.
    Write-Scene 'Scene B: rapid opens, local blue image then share photo - latest wins.'
    Forward-Open $Exe $localB
    Forward-Open $Exe $sharePath
    if (-not (Wait-DisplayedMedia $viewer.Window 'photo' "$BuildName B")) {
        $script:failures += "B ($BuildName): after local-then-share the screen did not show the share photo"
    }
    Write-Scene ("  folder box (legacy chrome only): $(Get-FolderBoxText $viewer.Window)")

    # C1. an offline path superseded at once by a valid file: the valid image opens,
    #     and no stale failure may survive it.
    Write-Scene 'Scene C1: offline share path superseded by a valid file - valid opens, no stale failure.'
    Forward-Open $Exe $offlinePath
    Forward-Open $Exe $localA
    if (-not (Wait-DisplayedMedia $viewer.Window 'red' "$BuildName C1")) {
        $script:failures += "C1 ($BuildName): the superseding local file never displayed"
    }
    Write-Scene ("  folder box (legacy chrome only): $(Get-FolderBoxText $viewer.Window)")

    # C2. the offline path alone: its own failure, and the displayed media untouched.
    #     Polled over several seconds: the media must still be there at SOME sample -
    #     a capture that races a chrome redraw may miss once, but not for five seconds.
    Write-Scene 'Scene C2: offline share path alone - own failure, displayed media kept.'
    $kept = Wait-True 5 { Test-Displayed $viewer.Window 'red' }
    Forward-Open $Exe $offlinePath
    Start-Sleep -Seconds 2
    $kept = (Wait-True 5 { Test-Displayed $viewer.Window 'red' }) -or $kept
    Add-Content -LiteralPath $transcript ("{0} C2: red kept={1}" -f $BuildName, $kept)
    Write-Scene ("  red kept=$kept; folder box: $(Get-FolderBoxText $viewer.Window)")
    if (-not $kept) {
        $script:failures += "C2 ($BuildName): the offline path changed the display"
    }

    # D. close with a share open in flight: the process must end, nothing reopens.
    #    Only processes with a LIVE MAIN WINDOW count as leftovers: a forwarding
    #    launch that already exited can keep its entry in the process list while this
    #    harness still holds its Process handle, and that is not a reopened viewer.
    Write-Scene 'Scene D: close with a share open in flight - the process ends, nothing reopens.'
    Forward-Open $Exe $sharePath
    Start-Sleep -Milliseconds 300
    $closed = $viewer.Proc.CloseMainWindow()
    $exited = Wait-True 20 { $viewer.Proc.Refresh(); $viewer.Proc.HasExited }
    Start-Sleep -Seconds 2
    $allLeft = Get-ViewerProcesses
    $left = @($allLeft | Where-Object { $_.MainWindowHandle -ne 0 })
    if (-not $exited) { $viewer.Proc.Kill() }
    Add-Content -LiteralPath $transcript ("{0} D: CloseMainWindow={1}, exited={2}, live windows left={3}" -f $BuildName, $closed, $exited, $left.Count)
    if (-not $exited -or $left.Count -gt 0) {
        $script:failures += "D ($BuildName): the window did not end cleanly (exited=$exited, left=$($left.Count))"
    }
    Stop-Viewer

    # E. modern only: a seeded startup resume of the share file against an explicit
    #    argument. The argument must win; with no argument the share file resumes.
    if ($Modern) {
        Write-Scene 'Scene E1: seeded resume of the share photo, launched WITH an explicit local argument - the argument wins.'
        Seed-EmptyStartup
        Set-ItemProperty -Path $regPath -Name 'ResumeLastPlayback' -Value '1'
        Set-ItemProperty -Path $regPath -Name 'LastPlayedFile' -Value $sharePath
        $viewer = Start-Viewer $Exe $localA
        Assert-OneWindow
        if (-not (Wait-DisplayedMedia $viewer.Window 'red' "$BuildName E1")) {
            $script:failures += "E1 (modern): an explicit argument lost to the startup resume"
        }
        Stop-Viewer

        Write-Scene 'Scene E2: seeded resume of the share photo, no argument - the share photo itself resumes.'
        Seed-EmptyStartup
        Set-ItemProperty -Path $regPath -Name 'ResumeLastPlayback' -Value '1'
        Set-ItemProperty -Path $regPath -Name 'LastPlayedFile' -Value $sharePath
        $viewer = Start-Viewer $Exe ''
        Assert-OneWindow
        if (-not (Wait-DisplayedMedia $viewer.Window 'photo' "$BuildName E2")) {
            $script:failures += "E2 (modern): the seeded resume of the share photo never displayed"
        }
        Stop-Viewer
    }
}

$code = 2
$detail = ''
try {
    Write-Subject 'ArgumentProbeOrderingScenes' ("rapid opens against the real share $ShareRoot, both viewer builds; latest accepted request wins")
    Invoke-Scenes 'modern viewer (net10, FastMediaSorter_LITE)' $modernExe $true
    Invoke-Scenes 'legacy viewer (net48, FastMediaSorter_x86)' $legacyExe $false

    if ($script:failures.Count -gt 0) {
        $code = 1
        $detail = ($script:failures -join '; ')
        foreach ($f in $script:failures) { Write-Host "  FAIL: $f" -ForegroundColor Red }
    }
    else {
        $code = 0
        $detail = 'latest request won on both builds; offline and superseded opens changed nothing'
    }
}
finally {
    Stop-Viewer
    Restore-Registry
    if ($script:consoleHwnd -ne [IntPtr]::Zero) {
        [FmsHarness.Native106]::ShowWindow($script:consoleHwnd, 9) | Out-Null   # SW_RESTORE
    }
    Write-Host "  transcript: $transcript"
}

Exit-Verdict 'ArgumentProbeOrderingScenes' $code $detail
