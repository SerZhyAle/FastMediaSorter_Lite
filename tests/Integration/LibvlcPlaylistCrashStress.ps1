<#
.SYNOPSIS
    The 50-run proof ticket 115 owes: the modern viewer, driven the way the crash dumps say it
    died (audio tag parse, rapid next/previous, leaving a track mid-parse, closing mid-parse),
    must finish every run without a native crash dump and without hanging on close.

.DESCRIPTION
    115_SPECIFICATION_LIBVLC_NATIVE_CRASH_PLAYLIST_THREAD.md: six dumps, all c0000005 on a
    libvlc worker thread entering a deleted critical section; the owner's trigger is MP3.
    The ownership fix (VlcNativeLease, serialised tag parse, awaited pool stop, bounded
    shutdown) is proved by unit tests only for its ORDER; whether the real libvlc still dies
    can only be shown by running the real viewer many times. This is that run.

    Every run starts the built bin\Release\FastMediaSorter_LITE.exe on one file of a scratch
    folder of MP3s (copied from -Source, so nothing of the owner's is ever touched) and then
    does one of four things, rotating:
      A  flip burst: Space / B at 60-250 ms intervals for -FlipSeconds, then a normal close
      B  early close: a normal close 0.3-2.0 s after the window appears (during the first parse)
      C  hammer: Space at ~25 ms for -FlipSeconds (a new parse on top of the last, every track)
      D  flip burst, then a normal close in the middle of the next flip
    A run is GOOD when the process exits on its own within 20 s of the close and no new
    FastMediaSorter_LITE.exe.*.dmp appeared in %LOCALAPPDATA%\CrashDumps. A process that
    has to be killed is a HANG (a failure of its own); a new dump is a CRASH.

    The viewer's registry settings are snapshotted and put back whole (muted, no recent
    folders carried); the harness refuses to run while a viewer is alive, because the
    single-instance mutex would forward the file to that window. Keys are posted as window
    messages (never SendKeys), so nothing lands in another application. The audio is muted
    by the seeded VideoMuted setting; libvlc still opens the output device.

    Evidence that the scene really exercised the parse: the viewer's own current.log gets a
    "Reading audio tags: <name>" line for every parse (ticket 115 breadcrumb). The run
    counts them; fewer than 2 per run on average means the keys never reached the viewer and
    the verdict is COULD NOT VERIFY, not a pass.

.PARAMETER Runs
    How many viewer sessions to run. Default 50 (the number the ticket names).

.PARAMETER Source
    A folder with MP3 files; up to -Files of them are copied to the scratch folder.
    Default: C:\Common\test_media. Add a network copy with -NetworkSource to compare.

.PARAMETER NetworkSource
    Optional second folder (e.g. \\SZAMEDIA\Common\test_media); its MP3s are used IN PLACE
    (read only) for the runs whose index is odd. Unreachable = COULD NOT VERIFY.

.PARAMETER WorkDir
    Scratch folder (music copy + transcript). Default %TEMP%\fms-115-stress. Kept after the run.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every run exited cleanly and left no dump; 1 = at least one
    crash dump or hang; 2 = could not verify (no build, no MP3s, a viewer already running, the
    keys never registered). Last line: `LibvlcPlaylistCrashStress: <WORD> (..)`. Run it by hand.
#>
[CmdletBinding()]
param(
    [int]$Runs = 50,
    [string]$Source = 'C:\Common\test_media',
    [string]$NetworkSource = '',
    [int]$Files = 14,
    [int]$FlipSeconds = 8,
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-115-stress')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
namespace FmsHarness {
    public static class Native115 {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
'@

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$exe      = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'
$viewerLog = Join-Path $repoRoot 'bin\Release\current.log'
$dumpDir  = Join-Path $env:LOCALAPPDATA 'CrashDumps'

. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Write-Host "  at $($_.InvocationInfo.PositionMessage)"
    try { Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue | Stop-Process -Force } catch {}
    try { Restore-Registry } catch {}
    Exit-Verdict 'LibvlcPlaylistCrashStress' 2 'the harness itself failed'
}

$WM_KEYDOWN = 0x0100; $WM_KEYUP = 0x0101
$VK_SPACE = 0x20; $VK_B = 0x42

function Get-ViewerProcesses { return ,@(Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue) }

function Wait-True([int]$Seconds, [scriptblock]$Condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return (& $Condition)
}

function Get-MainWindow([int]$ProcId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Send-Key([IntPtr]$Hwnd, [int]$Vk) {
    [FmsHarness.Native115]::PostMessage($Hwnd, $WM_KEYDOWN, [IntPtr]$Vk, [IntPtr]1) | Out-Null
    Start-Sleep -Milliseconds 15
    [FmsHarness.Native115]::PostMessage($Hwnd, $WM_KEYUP, [IntPtr]$Vk, [IntPtr]0xC0000001) | Out-Null
}

function Get-DumpNames {
    if (-not (Test-Path -LiteralPath $dumpDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dumpDir -Filter 'FastMediaSorter_LITE.exe.*.dmp' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
}

# --- registry snapshot --------------------------------------------------------------

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
        foreach ($n in @($now.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object { $_.Name })) {
            if (-not ($regSnapshot | Where-Object { $_.Name -eq $n })) {
                Remove-ItemProperty -Path $regPath -Name $n -ErrorAction SilentlyContinue
            }
        }
    }
    foreach ($entry in $regSnapshot) { Set-ItemProperty -Path $regPath -Name $entry.Name -Value $entry.Value }
}

function Seed-Startup {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name 'ImageFolder' -Value ''
    Set-ItemProperty -Path $regPath -Name 'LastCounter' -Value '0'
    Set-ItemProperty -Path $regPath -Name 'FirstRun' -Value '0'
    Set-ItemProperty -Path $regPath -Name 'VideoMuted' -Value '1'
}

# --- inputs -------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $exe)) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "no modern viewer at $exe - run build.ps1 first" }
if (Get-ViewerProcesses) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 'a viewer is already running - stop it first' }
if (-not (Test-Path -LiteralPath $Source)) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "no source folder $Source" }

$music = Join-Path $WorkDir 'music'
New-Item -ItemType Directory -Force -Path $music | Out-Null
Get-ChildItem -LiteralPath $music -File -ErrorAction SilentlyContinue | Remove-Item -Force
$picked = @(Get-ChildItem -LiteralPath $Source -Filter *.mp3 -File | Sort-Object Name | Select-Object -First $Files)
if ($picked.Count -lt 4) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "fewer than 4 MP3 files in $Source" }
foreach ($f in $picked) { Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $music ("{0:D2}_{1}" -f ([array]::IndexOf($picked, $f)), ($f.Name -replace '[^\w\.\- ]', '_'))) }
$localFiles = @(Get-ChildItem -LiteralPath $music -Filter *.mp3 -File | Sort-Object Name)

$netFiles = @()
if ($NetworkSource) {
    if (-not (Test-Path -LiteralPath $NetworkSource)) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "network source $NetworkSource is not reachable" }
    $netFiles = @(Get-ChildItem -LiteralPath $NetworkSource -Filter *.mp3 -File | Sort-Object Name | Select-Object -First $Files)
    if ($netFiles.Count -lt 4) { Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "fewer than 4 MP3 files in $NetworkSource" }
}

$transcript = Join-Path $WorkDir 'transcript.txt'
"libvlc crash stress - $(Get-Date -Format o) - runs=$Runs files=$($localFiles.Count) network=$($netFiles.Count)" | Set-Content -LiteralPath $transcript

$dumpsBefore = Get-DumpNames
$logLengthBefore = if (Test-Path -LiteralPath $viewerLog) { (Get-Item -LiteralPath $viewerLog).Length } else { 0 }
$results = New-Object System.Collections.Generic.List[object]
$rng = New-Object System.Random

function Invoke-Run([int]$Index) {
    $mode = 'ABCD'[$Index % 4]
    $pool = if ($netFiles.Count -gt 0 -and ($Index % 2) -eq 1) { $netFiles } else { $localFiles }
    $file = $pool[$rng.Next($pool.Count)].FullName
    $dumpsPre = Get-DumpNames
    $logPre = if (Test-Path -LiteralPath $viewerLog) { (Get-Item -LiteralPath $viewerLog).Length } else { 0 }

    $proc = Start-Process -FilePath $exe -ArgumentList "`"$file`"" -PassThru
    $mw = $null
    if (-not (Wait-True 40 { $proc.Refresh(); $mw = Get-MainWindow $proc.Id; (-not $proc.HasExited) -and ($null -ne $mw) })) {
        $proc.Refresh()
        return @{ Run = $Index; Mode = $mode; Outcome = $(if ($proc.HasExited) { "EARLY-EXIT($($proc.ExitCode))" } else { 'NO-WINDOW' }); Dumps = @() }
    }
    $proc.Refresh()
    $mw = Get-MainWindow $proc.Id
    if (-not $mw) { return @{ Run = $Index; Mode = $mode; Outcome = 'NO-WINDOW'; Dumps = @() } }
    $hwnd = [IntPtr]$mw.Current.NativeWindowHandle
    [FmsHarness.Native115]::SetWindowPos($hwnd, [IntPtr]::Zero, 120, 120, 1000, 700, 0x0040) | Out-Null

    switch ($mode) {
        'A' {
            $end = [DateTime]::UtcNow.AddSeconds($FlipSeconds)
            $i = 0
            while ([DateTime]::UtcNow -lt $end -and -not $proc.HasExited) {
                Send-Key $hwnd $(if (($i % 5) -eq 4) { $VK_B } else { $VK_SPACE }); $i++
                Start-Sleep -Milliseconds $rng.Next(60, 250)
            }
        }
        'B' { Start-Sleep -Milliseconds $rng.Next(300, 2000) }
        'C' {
            Start-Sleep -Milliseconds 600
            $end = [DateTime]::UtcNow.AddSeconds($FlipSeconds)
            while ([DateTime]::UtcNow -lt $end -and -not $proc.HasExited) { Send-Key $hwnd $VK_SPACE; Start-Sleep -Milliseconds 10 }
        }
        'D' {
            $end = [DateTime]::UtcNow.AddSeconds([Math]::Max(3, $FlipSeconds / 2))
            while ([DateTime]::UtcNow -lt $end -and -not $proc.HasExited) { Send-Key $hwnd $VK_SPACE; Start-Sleep -Milliseconds $rng.Next(80, 200) }
            Send-Key $hwnd $VK_SPACE            # the flip that starts a parse ..
            Start-Sleep -Milliseconds $rng.Next(5, 120)   # .. and the close right on top of it
        }
    }

    $outcome = 'CLEAN'
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        if (-not (Wait-True 20 { $proc.Refresh(); $proc.HasExited })) {
            $outcome = 'HANG'
            $proc.Kill()
            $proc.WaitForExit(5000) | Out-Null
        }
    }
    $proc.Refresh()
    Start-Sleep -Milliseconds 1500   # WER writes the dump after the process is gone
    $new = @(Get-DumpNames | Where-Object { $dumpsPre -notcontains $_ })
    if ($new.Count -gt 0) { $outcome = 'CRASH' }
    elseif ($outcome -eq 'CLEAN' -and $proc.ExitCode -ne 0) { $outcome = "EXIT($($proc.ExitCode))" }

    $parses = 0
    if (Test-Path -LiteralPath $viewerLog) {
        $len = (Get-Item -LiteralPath $viewerLog).Length
        if ($len -gt $logPre) {
            $fs = [System.IO.File]::Open($viewerLog, 'Open', 'Read', 'ReadWrite')
            try {
                $fs.Seek($logPre, 'Begin') | Out-Null
                $buf = New-Object byte[] ($len - $logPre)
                $n = $fs.Read($buf, 0, $buf.Length)
                $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
                $parses = ([regex]::Matches($text, 'Reading audio tags:')).Count
            } finally { $fs.Dispose() }
        }
    }
    return @{ Run = $Index; Mode = $mode; Outcome = $outcome; Dumps = $new; Parses = $parses; File = (Split-Path -Leaf $file) }
}

try {
    Seed-Startup
    for ($i = 0; $i -lt $Runs; $i++) {
        $r = Invoke-Run $i
        $results.Add($r)
        $line = "run {0,2} mode {1}: {2}  parses={3}  {4}" -f ($i + 1), $r.Mode, $r.Outcome, $(if ($r.ContainsKey('Parses')) { $r.Parses } else { '-' }), $(if ($r.ContainsKey('File')) { $r.File } else { '' })
        Write-Host "  $line" -ForegroundColor $(if ($r.Outcome -eq 'CLEAN') { 'Green' } else { 'Red' })
        $line | Add-Content -LiteralPath $transcript
        if ($r.Dumps.Count -gt 0) { "   dump: $($r.Dumps -join ', ')" | Add-Content -LiteralPath $transcript }
        Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 700
    }
} finally {
    Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Restore-Registry
}

$bad = @($results | Where-Object { $_.Outcome -ne 'CLEAN' })
$totalParses = 0
foreach ($r in $results) { if ($r.ContainsKey('Parses')) { $totalParses += $r.Parses } }
$summary = "$($results.Count) runs, $($results.Count - $bad.Count) clean, $totalParses tag parses; transcript $transcript"
$summary | Add-Content -LiteralPath $transcript

if ($bad.Count -gt 0) {
    $kinds = ($bad | ForEach-Object { $_.Outcome } | Group-Object | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
    Exit-Verdict 'LibvlcPlaylistCrashStress' 1 "$kinds of $($results.Count) runs - $summary"
}
if ($totalParses -lt (2 * $results.Count)) {
    Exit-Verdict 'LibvlcPlaylistCrashStress' 2 "no defect seen, but only $totalParses tag parses in $($results.Count) runs - the keys did not drive the viewer, so nothing was proved; $summary"
}
Exit-Verdict 'LibvlcPlaylistCrashStress' 0 $summary
