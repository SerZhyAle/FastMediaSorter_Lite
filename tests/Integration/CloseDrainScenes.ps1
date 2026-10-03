<#
.SYNOPSIS
    Closes the live proof ticket 107 owes: the close drain against a real slow SMB copy -
    the repeated close is refused during the wait, the "wait longer" prompt is answered
    both ways, and a cancelled Windows shutdown leaves a fully usable window.

.DESCRIPTION
    107_SPECIFICATION_CLOSE_DRAIN_REENTRANCY.md asks for the manual/live acceptance its
    deterministic suites cannot give: a slow cross-share copy with the window closed on
    top of it, a REPEATED close while the drain is pending (the ticket's own defect - the
    second close used to bypass the drain and kill the process with the copy unfinished),
    the 15 s "wait longer" prompt answered Yes (drain to completion, then close) and No
    (discard now), and a Windows shutdown QUERY that is later cancelled (045 F-01's
    scenario: nothing may be torn down by a question). The coordinator mechanics, the
    admission races and the fault paths are pinned by CloseDrainTests,
    FileOpQueueAdmissionTests and FileOpCloseFlowTests; what only a live run can show is
    that the real window, the real queue and a real 5 GB SMB copy behave the same way.

    Scenes (modern Release viewer only - the drain is modern):
      A  slow share copy + close: the first close is absorbed by the drain, the
         "Closing the app" prompt (waited for, its queue text recorded) is answered
         "Yes" until the copy finishes; the process then exits and the copy in the
         slot is byte-identical (SHA-256) to the share original.
      B  the same copy, then close TWICE during the 15 s wait: the re-entrant close
         must be refused - the window stays alive with the drain still pending - and
         answering "No" then discards the unfinished copy: the process exits, the
         source is intact and no final file is in the slot (a staged remnant may be;
         it is recorded and removed by the run).
      C  a cancelled shutdown: WM_QUERYENDSESSION then WM_ENDSESSION(FALSE) leave the
         window alive AND fully usable - a recipient copy still runs afterwards,
         then an ordinary close still ends the process cleanly.

    The copy source is the largest file directly under the share root (a multi-GB video
    over SMB is the slow operation; the run refuses - COULD NOT VERIFY - when no file
    there is at least 1 GB, because a copy that finishes inside the first 15 s slice
    exercises no prompt at all). The destination slot is a local folder the run owns;
    the share is only ever READ. The viewer's registry settings are snapshotted and put
    back whole; the harness refuses to run while any viewer instance is alive.

.PARAMETER WorkDir
    Where the slot folder, the small scene file and the transcript go. Default:
    %TEMP%\fms-107-scenes. Kept after the run either way - it is the run's evidence.

.PARAMETER ShareRoot
    The SMB share the slow copy reads from. Default: \\SZAMEDIA\Common.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every scene held - the drain absorbed the closes,
    the re-entrant close was refused, both prompt answers did what they promise, and the
    cancelled shutdown left a usable window. 1 = a drain defect (a repeated close ended
    the process with work outstanding, a discarded copy left a final file, or the
    shutdown query broke the window). 2 = could not verify: a missing build, a share
    without a big file, a viewer already running, or a window that never came up. The
    last line is the verdict, `CloseDrainScenes: <WORD> (..)`. Run it by hand.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-107-scenes'),
    [string]$ShareRoot = '\\SZAMEDIA\Common'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
namespace FmsHarness {
    public static class Native107 {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(System.IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern System.IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint lpdwProcessId);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern System.IntPtr GetConsoleWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam, uint flags, uint timeout, out System.IntPtr result);
    }
}
'@

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$exe      = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'

. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

# An unexpected terminating error must not surface as exit 1, which reads as a failed check.
trap {
    Write-Host "  $($_.Exception.Message)"
    Write-Host "  at $($_.InvocationInfo.PositionMessage)"
    Exit-Verdict 'CloseDrainScenes' 2 'the harness itself failed'
}

[FmsHarness.Native107]::SetProcessDPIAware() | Out-Null
# SendKeys types into whoever is foreground; keep this console out of the way.
$script:consoleHwnd = [FmsHarness.Native107]::GetConsoleWindow()
if ($script:consoleHwnd -ne [IntPtr]::Zero) {
    [FmsHarness.Native107]::ShowWindow($script:consoleHwnd, 6) | Out-Null   # SW_MINIMIZE
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
    # The comma keeps PowerShell from unwrapping a one-element array into the bare object.
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

if (-not (Test-Path -LiteralPath $exe)) { Exit-Verdict 'CloseDrainScenes' 2 "no modern viewer at $exe - run build.ps1 first" }

# The slow operation: the largest file directly under the share root. Under 1 GB the
# copy answers inside the first 15 s slice and no prompt ever exists to answer.
$slowFile = Get-ChildItem -LiteralPath $ShareRoot -File -ErrorAction Stop |
    Sort-Object Length -Descending | Select-Object -First 1
if (-not $slowFile -or $slowFile.Length -lt 1GB) {
    Exit-Verdict 'CloseDrainScenes' 2 "no file of at least 1 GB directly under $ShareRoot - a faster copy would finish inside the drain's first wait slice and prove nothing"
}
$slowPath     = $slowFile.FullName
$slowName     = $slowFile.Name
$slowSizeGb   = [Math]::Round($slowFile.Length / 1GB, 2)

$slotDir  = Join-Path $WorkDir 'slot-out'
New-Item -ItemType Directory -Force -Path $slotDir, (Join-Path $WorkDir 'small') | Out-Null
$smallPath = Join-Path $WorkDir 'small\fms107-small.png'
if (-not (Test-Path -LiteralPath $smallPath)) {
    $bmp = New-Object System.Drawing.Bitmap 400, 300
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::FromArgb(30, 30, 220))
    $gfx.Dispose()
    $bmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$transcript = Join-Path $WorkDir 'transcript.txt'
"close-drain scenes - $(Get-Date -Format o)" | Set-Content -LiteralPath $transcript
"slow copy source: $slowPath ($slowSizeGb GB)" | Add-Content -LiteralPath $transcript
"destination slot: $slotDir" | Add-Content -LiteralPath $transcript

if (Get-ViewerProcesses) {
    Exit-Verdict 'CloseDrainScenes' 2 'a viewer is already running - stop it first, or its settings and environment win'
}

# --- viewer driving ----------------------------------------------------------------

function Seed-Startup {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name 'ImageFolder' -Value ''
    Set-ItemProperty -Path $regPath -Name 'LastCounter' -Value '0'
    Set-ItemProperty -Path $regPath -Name 'FirstRun' -Value '0'
    Set-ItemProperty -Path $regPath -Name 'MoveOn1' -Value $slotDir
    Set-ItemProperty -Path $regPath -Name 'VideoMuted' -Value '1'
    # -Path (not -LiteralPath) so the wildcard actually enumerates: a staged remnant of
    # an earlier scene left behind here reads as a copy of the next scene.
    Get-ChildItem -Path $slotDir -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

function Start-Viewer([string]$File) {
    Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    $proc = Start-Process -FilePath $exe -ArgumentList "`"$File`"" -PassThru
    $mw = $null
    if (-not (Wait-True 40 { $proc.Refresh(); $mw = Get-MainWindow $proc.Id; (-not $proc.HasExited) -and ($null -ne $mw) })) {
        $proc.Refresh()
        if ($proc.HasExited) { throw "the viewer exited at once (code $($proc.ExitCode))" }
        throw 'the viewer never showed a window'
    }
    $proc.Refresh()
    $mw = Get-MainWindow $proc.Id
    if ($mw) {
        [FmsHarness.Native107]::SetWindowPos([IntPtr]$mw.Current.NativeWindowHandle, [IntPtr]::Zero, 120, 120, 1100, 760, 0x0040) | Out-Null
        [FmsHarness.Native107]::SetForegroundWindow([IntPtr]$mw.Current.NativeWindowHandle) | Out-Null
    }
    return @{ Proc = $proc; Window = $mw }
}

function Stop-Viewer {
    Get-ViewerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-True 8 { (Get-ViewerProcesses).Count -eq 0 } | Out-Null
    Start-Sleep -Seconds 2
}

# The drain prompt is a real MessageBox owned by the main window, class #32770, titled
# "Closing the app" (Localization.Runtime.vb) - and as an OWNED dialog it surfaces in
# UIA INSIDE the main window's tree, not as a root-level window (finding it at the root
# made the prompt look absent; the run watched for it and saw it appear at exactly the
# 15 s slice, under the owner). The prompt's message text - the live queue state - is
# recorded. Returns the dialog element or $null.
function Find-DrainPrompt($MainWindow) {
    if (-not $MainWindow) { return $null }
    $clsCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
    $hit = $MainWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $clsCond)
    if ($hit) { return $hit }
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Closing the app')
    return $MainWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
}

# The whole prompt - text and buttons - recorded for the transcript before answering.
function Read-PromptText($Dialog) {
    if (-not $Dialog) { return '' }
    $all = $Dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $parts = @()
    for ($i = 0; $i -lt $all.Count; $i++) {
        $n = $all[$i].Current.Name
        if (-not [String]::IsNullOrEmpty($n)) { $parts += $n }
    }
    return ($parts -join ' / ')
}

function Invoke-PromptButton($Dialog, [string[]]$Names) {
    foreach ($n in $Names) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $n)
        $scope = [System.Windows.Automation.TreeScope]::Descendants
        $btn = $Dialog.FindFirst($scope, $cond)
        if (-not $btn) {
            # the found element may be the title text rather than the dialog: walk up once
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($Dialog)
            if ($parent) { $btn = $parent.FindFirst($scope, $cond) }
        }
        if ($btn) {
            $inv = $null
            if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
                $inv.Invoke()
                return $true
            }
        }
    }
    return $false
}

# A background process is denied SetForegroundWindow by the system's foreground lock -
# SendKeys would type into whoever IS foreground. AttachThreadInput borrows the input
# state of the foreground thread, the documented workaround, and the caller verifies.
function Focus-Viewer([IntPtr]$Hwnd) {
    $myTid = [FmsHarness.Native107]::GetCurrentThreadId()
    $dstPid = [uint32]0
    $dstTid = [FmsHarness.Native107]::GetWindowThreadProcessId($Hwnd, [ref]$dstPid)
    $fg = [FmsHarness.Native107]::GetForegroundWindow()
    $fgPid = [uint32]0
    $fgTid = [FmsHarness.Native107]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    [FmsHarness.Native107]::AttachThreadInput($myTid, $dstTid, $true) | Out-Null
    if ($fgTid -ne 0 -and $fgTid -ne $dstTid) {
        [FmsHarness.Native107]::AttachThreadInput($fgTid, $dstTid, $true) | Out-Null
    }
    try {
        [FmsHarness.Native107]::SetForegroundWindow($Hwnd) | Out-Null
        Start-Sleep -Milliseconds 150
        return ([FmsHarness.Native107]::GetForegroundWindow() -eq $Hwnd)
    }
    finally {
        if ($fgTid -ne 0 -and $fgTid -ne $dstTid) {
            [FmsHarness.Native107]::AttachThreadInput($fgTid, $dstTid, $false) | Out-Null
        }
        [FmsHarness.Native107]::AttachThreadInput($myTid, $dstTid, $false) | Out-Null
    }
}

# Shift + top-row 1 = copy the displayed file into slot 1 (Main_Form.KeyboardInput.vb).
# Delivered only when the viewer verifiably owns the foreground, and verified AGAIN by
# the staged OR the final file appearing in the slot - a small file can rename into its
# final name before the staged poll sees it, and a key that lands nowhere must not be
# mistaken for a copy that started.
function Send-CopyKey([int]$ProcId, $Window, [string]$SourceName) {
    $hwnd = [IntPtr]$Window.Current.NativeWindowHandle
    $final = Join-Path $slotDir $SourceName
    for ($try = 1; $try -le 4; $try++) {
        if (-not (Focus-Viewer $hwnd)) {
            Write-Scene "  foreground not obtained (try $try); retrying."
            Start-Sleep -Milliseconds 500
            continue
        }
        [System.Windows.Forms.SendKeys]::SendWait('+1')
        if (Wait-True 10 {
                (Test-Path -Path (Join-Path $slotDir '*.fms-part')) -or
                (Test-Path -LiteralPath $final)
            }) {
            return $true
        }
        Write-Scene "  key sent (try $try) but no staged or final copy appeared; retrying."
        Start-Sleep -Milliseconds 300
    }
    Add-Content -LiteralPath $transcript "copy key for '$SourceName' (pid $ProcId) never started a staged copy"
    return $false
}

function Wait-ViewerExit([System.Diagnostics.Process]$Proc, [int]$Seconds) {
    return Wait-True $Seconds { $Proc.Refresh(); $Proc.HasExited }
}

function Send-QueryEndSession([IntPtr]$Hwnd) {
    $result = [IntPtr]::Zero
    [FmsHarness.Native107]::SendMessageTimeout($Hwnd, 0x0011, [IntPtr]::Zero, [IntPtr]::Zero, 0, 5000, [ref]$result) | Out-Null
    return $result
}
function Send-EndSession([IntPtr]$Hwnd, [bool]$Ending) {
    $result = [IntPtr]::Zero
    $w = If ($ending) { [IntPtr]::new(1) } else { [IntPtr]::Zero }
    [FmsHarness.Native107]::SendMessageTimeout($Hwnd, 0x0016, $w, [IntPtr]::Zero, 0, 5000, [ref]$result) | Out-Null
}

# --- the scenes --------------------------------------------------------------------

# Script scope on purpose: the scenes append from inside functions below.
$script:failures = @()

function Invoke-SceneA {
    Write-Host ''
    Write-Host '=== Scene A: slow share copy, close, answer the wait prompt Yes until it finishes ===' -ForegroundColor White
    Seed-Startup
    $viewer = Start-Viewer $slowPath
    Start-Sleep -Seconds 10                      # let LibVLC open the big file
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (-not (Send-CopyKey $viewer.Proc.Id $viewer.Window $slowName)) {
        $viewer.Proc | Stop-Process -Force
        throw 'scene A could not start the slow copy - the drain would have nothing to wait for'
    }
    Write-Scene '  the staged copy is growing in the slot.'
    Start-Sleep -Seconds 3
    $null = $viewer.Proc.CloseMainWindow()       # close #1: absorbed by the drain
    Start-Sleep -Seconds 2
    if ($viewer.Proc.HasExited) {
        $script:failures += 'A: the first close ended the process instead of starting the drain'
        return
    }
    Write-Scene '  close #1 absorbed; the drain is waiting.'
    $prompts = 0
    $exited = $false
    while ($sw.Elapsed.TotalMinutes -lt 12) {
        $prompt = $null
        try { $prompt = Find-DrainPrompt $viewer.Window } catch { $prompt = $null }
        if ($prompt) {
            $promptText = ''
            try { $promptText = (Read-PromptText $prompt) -replace "`r`n", ' ' } catch { $promptText = '' }
            if ($promptText.Length -gt 0) {
                $prompts++
                Add-Content -LiteralPath $transcript ("A prompt {0} at {1:N0} s: '{2}'" -f $prompts, $sw.Elapsed.TotalSeconds, $promptText)
                Write-Scene ("  prompt: {0}" -f $promptText)
                try { $null = Invoke-PromptButton $prompt ('Yes', '&Yes', 'Да', '&Да') } catch { }
                Start-Sleep -Seconds 2
            }
        }
        if ($viewer.Proc.HasExited) { $exited = $true; break }
        Start-Sleep -Milliseconds 200
    }
    $sw.Stop()
    if (-not $exited) {
        $viewer.Proc | Stop-Process -Force
        $script:failures += 'A: the process never exited within 12 minutes of the drain'
        return
    }
    $dest = Join-Path $slotDir $slowName
    Add-Content -LiteralPath $transcript ("A: exited after {0:N0} s, {1} prompt(s)" -f $sw.Elapsed.TotalSeconds, $prompts)
    Write-Scene ("  exited after {0:N0} s, {1} prompt(s)." -f $sw.Elapsed.TotalSeconds, $prompts)
    if (-not (Test-Path -LiteralPath $dest)) {
        $script:failures += 'A: the drain closed cleanly but the copy never landed in the slot'
        return
    }
    $srcHash = (Get-FileHash -LiteralPath $slowPath -Algorithm SHA256).Hash
    $dstHash = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
    Add-Content -LiteralPath $transcript ("A: source sha256 {0}" -f $srcHash.Substring(0, 16))
    Add-Content -LiteralPath $transcript ("A: copy   sha256 {0}" -f $dstHash.Substring(0, 16))
    if ($srcHash -ne $dstHash) {
        $script:failures += 'A: the copied file in the slot is not byte-identical to the share original'
    }
    else {
        Write-Scene ("  copy byte-identical (sha256 {0}..)." -f $dstHash.Substring(0, 16))
    }
    Remove-Item -LiteralPath $dest -Force      # 5+ GB of scene evidence: hash recorded, disk reclaimed
    Stop-Viewer
}

function Invoke-SceneB {
    Write-Host ''
    Write-Host '=== Scene B: the repeated close during the drain is refused; No discards the copy ===' -ForegroundColor White
    Seed-Startup
    $viewer = Start-Viewer $slowPath
    Start-Sleep -Seconds 10
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (-not (Send-CopyKey $viewer.Proc.Id $viewer.Window $slowName)) {
        $viewer.Proc | Stop-Process -Force
        throw 'scene B could not start the slow copy - the drain would have nothing to wait for'
    }
    Write-Scene '  the staged copy is growing in the slot.'
    Start-Sleep -Seconds 3
    $null = $viewer.Proc.CloseMainWindow()       # close #1: drain starts
    Start-Sleep -Seconds 2
    if ($viewer.Proc.HasExited) {
        $script:failures += 'B: the first close ended the process instead of starting the drain'
        return
    }
    Write-Scene '  close #1 absorbed; sending close #2 while the drain is pending.'
    $null = $viewer.Proc.CloseMainWindow()       # close #2: the ticket's defect - must be refused
    Start-Sleep -Seconds 3
    if ($viewer.Proc.HasExited) {
        $script:failures += 'B: the REPEATED close ended the process with the copy outstanding - the 107 defect itself'
        return
    }
    Write-Scene '  close #2 refused; the window and the drain survived it.'
    # Polling in THIS scope, not through a scriptblock: an assignment inside a
    # scriptblock would build its own $prompt and leave the outer one null. A #32770
    # candidate is only accepted once its text can be read - a dialog still being
    # created (or a spurious empty one) reads as empty and is polled again.
    $prompt = $null
    $promptText = ''
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        $candidate = $null
        try { $candidate = Find-DrainPrompt $viewer.Window } catch { $candidate = $null }
        if ($candidate) {
            $t = ''
            try { $t = (Read-PromptText $candidate) -replace "`r`n", ' ' } catch { $t = '' }
            if ($t.Length -gt 0) {
                Add-Content -LiteralPath $transcript ("B candidate read at {0:N0} s: '{1}'" -f $sw.Elapsed.TotalSeconds, $t)
                $prompt = $candidate
                $promptText = $t
                break
            }
        }
        if ($viewer.Proc.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $prompt) {
        # Diagnose: record every window of the process so a missing prompt is
        # distinguishable from a prompt the finder cannot see.
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $viewer.Proc.Id)
        $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        for ($i = 0; $i -lt $wins.Count; $i++) {
            Add-Content -LiteralPath $transcript ("B window: '{0}' class={1}" -f $wins[$i].Current.Name, $wins[$i].Current.ClassName)
        }
        Add-Content -LiteralPath $transcript ("B: staged at timeout: {0}" -f ((Get-ChildItem -Path $slotDir -Filter '*.fms-part' -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1MB))
        $viewer.Proc | Stop-Process -Force
        $script:failures += 'B: the drain never showed its wait prompt'
        return
    }
    Add-Content -LiteralPath $transcript ("B prompt at {0:N0} s: '{1}'" -f $sw.Elapsed.TotalSeconds, $promptText)
    Write-Scene ("  prompt: {0}" -f $promptText)
    $clicked = $false
    for ($try = 1; $try -le 3 -and -not $clicked; $try++) {
        try { $clicked = Invoke-PromptButton $prompt ('No', '&No', 'Нет', '&Нет') } catch { $clicked = $false }
        if (-not $clicked) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $clicked) {
        $viewer.Proc | Stop-Process -Force
        $script:failures += 'B: the discard answer (No) could not be delivered to the prompt'
        return
    }
    $exited = Wait-ViewerExit $viewer.Proc 30
    $sw.Stop()
    Add-Content -LiteralPath $transcript ("B: exited={0} after {1:N0} s" -f $exited, $sw.Elapsed.TotalSeconds)
    if (-not $exited) {
        $viewer.Proc | Stop-Process -Force
        $script:failures += 'B: answering No did not close the app'
        return
    }
    Write-Scene '  No discarded the unfinished copy; the process ended.'
    if (-not (Test-Path -LiteralPath $slowPath)) {
        $script:failures += 'B: the discarded copy somehow touched the share original'
        return
    }
    $final = Join-Path $slotDir $slowName
    if (Test-Path -LiteralPath $final) {
        $script:failures += 'B: the discarded copy left a FINAL file in the slot'
        Remove-Item -LiteralPath $final -Force
    }
    else {
        Write-Scene '  no final file in the slot; the unfinished copy was never recorded as done.'
    }
    $remnants = @(Get-ChildItem -LiteralPath $slotDir -Force -ErrorAction SilentlyContinue)
    foreach ($r in $remnants) {
        Add-Content -LiteralPath $transcript ("B: staged remnant recorded then removed: {0} ({1:N0} MB)" -f $r.Name, ($r.Length / 1MB))
        Remove-Item -LiteralPath $r.FullName -Force
    }
    Stop-Viewer
}

function Invoke-SceneC {
    Write-Host ''
    Write-Host '=== Scene C: a cancelled Windows shutdown leaves a fully usable window ===' -ForegroundColor White
    Seed-Startup
    $viewer = Start-Viewer $smallPath
    Start-Sleep -Seconds 5
    $hwnd = [IntPtr]$viewer.Window.Current.NativeWindowHandle
    $answer = Send-QueryEndSession $hwnd
    Add-Content -LiteralPath $transcript ("C: WM_QUERYENDSESSION answered {0}" -f $answer)
    Start-Sleep -Seconds 2
    if ($viewer.Proc.HasExited) {
        $script:failures += 'C: the shutdown QUERY alone ended the process'
        return
    }
    Write-Scene ("  query answered {0}; window alive. Cancelling the session." -f $answer)
    Send-EndSession $hwnd $false
    Start-Sleep -Seconds 2
    if ($viewer.Proc.HasExited) {
        $script:failures += 'C: the CANCELLED shutdown ended the process'
        return
    }
    Write-Scene '  session cancelled; window alive. Proving it still works with a real copy.'
    if (-not (Send-CopyKey $viewer.Proc.Id $viewer.Window (Split-Path -Leaf $smallPath))) {
        $viewer.Proc | Stop-Process -Force
        throw 'scene C could not run the post-shutdown copy - usability could not be judged'
    }
    $dest = Join-Path $slotDir (Split-Path -Leaf $smallPath)
    if (-not (Wait-True 30 { Test-Path -LiteralPath $dest })) {
        $script:failures += 'C: after the cancelled shutdown the window could no longer run a recipient copy'
        $viewer.Proc | Stop-Process -Force
        return
    }
    Write-Scene '  the copy after the cancelled shutdown landed in the slot.'
    Add-Content -LiteralPath $transcript 'C: copy after cancelled shutdown landed in the slot'
    $null = $viewer.Proc.CloseMainWindow()
    $exited = Wait-ViewerExit $viewer.Proc 30
    Add-Content -LiteralPath $transcript ("C: ordinary close exited={0}" -f $exited)
    if (-not $exited) {
        $viewer.Proc | Stop-Process -Force
        $script:failures += 'C: an ordinary close after the cancelled shutdown no longer ended the process'
        return
    }
    Write-Scene '  an ordinary close still ends the process cleanly.'
    Stop-Viewer
}

# --- run ---------------------------------------------------------------------------

$code = 2
$detail = ''
try {
    if (-not (Test-Path -LiteralPath $exe)) { throw 'no modern viewer' }
    Write-Subject 'CloseDrainScenes' ("the modern viewer's close drain against a real $slowSizeGb GB share copy, the repeated close, both prompt answers and a cancelled shutdown")
    Invoke-SceneA
    Invoke-SceneB
    Invoke-SceneC

    if ($script:failures.Count -gt 0) {
        $code = 1
        $detail = ($script:failures -join '; ')
        foreach ($f in $script:failures) { Write-Host "  FAIL: $f" -ForegroundColor Red }
    }
    else {
        $code = 0
        $detail = 'the drain absorbed both closes, both prompt answers held, the cancelled shutdown left a working window'
    }
}
finally {
    Stop-Viewer
    Restore-Registry
    if ($script:consoleHwnd -ne [IntPtr]::Zero) {
        [FmsHarness.Native107]::ShowWindow($script:consoleHwnd, 9) | Out-Null   # SW_RESTORE
    }
    Write-Host "  transcript: $transcript"
}

Exit-Verdict 'CloseDrainScenes' $code $detail
