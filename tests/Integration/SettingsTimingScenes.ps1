<#
.SYNOPSIS
    Times every page of the viewer's settings window and the windows it can reach against the budget of
    WINDOWS-UI section 5 (amended 0.2): 500 ms the first time a page is shown, 150 ms when it was shown
    before, 500 ms for a dialog - and holds the layout of the window to what it was before it was optimised.

.DESCRIPTION
    Two scenes, each opt-out (-SkipLayout, -SkipViewer):

    LAYOUT (in process, no desktop input). Builds tests/Integration/SettingsLayoutScenes - the real Table_Form
    of the modern viewer, on a scratch settings hive, shown off screen - and runs it twice: DPI-unaware
    (100 %) and per-monitor aware on the primary monitor. It visits all eleven pages twice, toggles every
    group, expands and collapses a page, re-opens the window and resizes it, counting the layout passes
    (Layout events) of every step and timing it. With -CompareLayoutWith <json from another build> it also
    compares every control's bounds, every scroll range and the rendered pixels of every step with that run
    (WINDOWS-UI section 9: "compare the rendered page with the one produced without the optimization").

    VIEWER (the built exe, on screen). Starts the viewer with FMS_UI_TIMING=1 - the viewer's own opt-in,
    local-only measurement (src/UiTiming.vb): one line per shown page or window in its current.log, nothing
    leaves the machine. Opens Settings with F2 (a cold open, then two warm ones), visits the eleven pages twice
    through their navigation buttons and opens the dialogs it can reach without changing anything (file types,
    hotkeys, history, settings search), then reads the lines back and prints the table.

    The owner's own viewer may be running: this script refuses to start while ANY viewer process exists, never
    stops a process it did not start, and snapshots and restores the viewer's registry hive around the run.

    -SelfTest proves the parser and the verdict arithmetic on canned lines and exits NOT APPLICABLE - it
    measured nothing, and says so.

.PARAMETER Exe
    The viewer to drive. Default: the newest of bin\Release\FastMediaSorter_LITE.exe and
    src\Modern\bin\Release\net10.0-windows\win-x64\FastMediaSorter_LITE.exe. It must be built from this tree
    (an older build writes no timing lines and the viewer scene reports COULD NOT VERIFY).

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every measured page and window within budget and the layout compare (when
    asked for) identical. 1 = a surface over its budget, or a layout that differs. 2 = could not verify: no
    desktop, a viewer already running, a viewer that never showed Settings, a missing measurement. The last
    line is the verdict, `SettingsTimingScenes: <WORD> (..)`. Run it by hand; never part of a default run.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-settings-timing-scenes'),
    [string]$Exe = '',
    [string]$CompareLayoutWith = '',
    [switch]$SkipLayout,
    [switch]$SkipViewer,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
$check = 'SettingsTimingScenes'

# An unexpected terminating error must not surface as exit 1, which reads as a failed check.
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the harness itself failed'
}

# --- the budget (WINDOWS-UI section 5, amended 0.2; mirrors src/UiTimingBudget.vb, held by UiTimingBudgetTests) ---
$BudgetFirstVisitMs  = 500
$BudgetRepeatVisitMs = 150
$BudgetDialogMs      = 500
$LinePrefix          = 'UI timing: '    # src/UiTimingBudget.vb LinePrefix

function Get-BudgetMs([string]$Kind) {
    switch ($Kind) {
        'page-first'  { $BudgetFirstVisitMs }
        'page-repeat' { $BudgetRepeatVisitMs }
        default       { $BudgetDialogMs }
    }
}

# "UI timing: page-repeat | settings/audio | 38.4 ms | budget 150 ms | PASS" -> object, or $null for any other line.
function ConvertFrom-TimingLine([string]$Line) {
    $at = $Line.IndexOf($LinePrefix, [StringComparison]::Ordinal)
    if ($at -lt 0) { return $null }
    $parts = $Line.Substring($at + $LinePrefix.Length) -split ' \| '
    if ($parts.Count -ne 5) { return $null }
    if (@('page-first', 'page-repeat', 'dialog') -notcontains $parts[0]) { return $null }
    if ($parts[2] -notmatch '^(?<ms>[0-9]+(\.[0-9]+)?) ms$') { return $null }
    [pscustomobject]@{
        Kind    = $parts[0]
        Name    = $parts[1]
        Ms      = [double]::Parse($Matches['ms'], [Globalization.CultureInfo]::InvariantCulture)
        Budget  = Get-BudgetMs $parts[0]
    }
}

# The verdict arithmetic, apart from any desktop: what was measured against what the run was expected to show.
# Returns @{ Code; Detail; Over = @(records); Missing = @(strings) }.
function Get-TimingVerdict($Records, [string[]]$ExpectedPages, [int]$ExpectedOpens, [int]$MinDialogs) {
    $over = @($Records | Where-Object { $_.Ms -ge $_.Budget })
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($page in $ExpectedPages) {
        $seen = @($Records | Where-Object { $_.Name -eq "settings/$page" })
        if ($seen.Count -lt 2) { $missing.Add("settings/$page (saw $($seen.Count) of 2 visits)") }
    }
    $firstWithoutFirst = @($ExpectedPages | Where-Object {
        $name = "settings/$_"
        @($Records | Where-Object { $_.Name -eq $name -and $_.Kind -eq 'page-first' }).Count -eq 0
    })
    if ($firstWithoutFirst.Count -gt 1) { $missing.Add("a first visit of more than one page was not recognised: $($firstWithoutFirst -join ', ')") }
    $opens = @($Records | Where-Object { $_.Name -eq 'settings/window' })
    if ($opens.Count -lt $ExpectedOpens) { $missing.Add("settings/window (saw $($opens.Count) of $ExpectedOpens opens)") }
    $dialogs = @($Records | Where-Object { $_.Kind -eq 'dialog' -and $_.Name -ne 'settings/window' })
    if ($dialogs.Count -lt $MinDialogs) { $missing.Add("secondary windows (saw $($dialogs.Count), wanted at least $MinDialogs)") }

    if ($over.Count -gt 0) {
        $worst = $over | Sort-Object { $_.Ms - $_.Budget } -Descending | Select-Object -First 1
        return @{ Code = 1; Over = $over; Missing = $missing.ToArray()
                 Detail = "$($over.Count) of $($Records.Count) measurements over budget, worst $($worst.Name) $($worst.Kind) $([math]::Round($worst.Ms)) ms against $($worst.Budget)" }
    }
    if ($missing.Count -gt 0) {
        return @{ Code = 2; Over = @(); Missing = $missing.ToArray(); Detail = 'not everything was measured: ' + ($missing -join '; ') }
    }
    return @{ Code = 0; Over = @(); Missing = @(); Detail = "$($Records.Count) measurements, every one within its budget" }
}

function Write-TimingTable($Records) {
    Write-Host ''
    Write-Host ('{0,-12} {1,-26} {2,3} {3,8} {4,8} {5,8} {6,7}  {7}' -f 'kind', 'surface', 'n', 'min ms', 'median', 'max ms', 'budget', 'verdict')
    $groups = $Records | Group-Object { "$($_.Kind)|$($_.Name)" } | Sort-Object { $_.Group[0].Kind }, Name
    foreach ($g in $groups) {
        $ms = @($g.Group | ForEach-Object { $_.Ms } | Sort-Object)
        $median = $ms[[int][math]::Floor(($ms.Count - 1) / 2)]
        $max = $ms[$ms.Count - 1]
        $budget = $g.Group[0].Budget
        $verdict = if ($max -lt $budget) { 'PASS' } else { 'OVER' }
        Write-Host ('{0,-12} {1,-26} {2,3} {3,8:0.0} {4,8:0.0} {5,8:0.0} {6,7}  {7}' -f $g.Group[0].Kind, $g.Group[0].Name, $ms.Count, $ms[0], $median, $max, $budget, $verdict)
    }
    Write-Host ''
}

# --- self-test: the parser and the arithmetic, no desktop, nothing measured -----------------------------------

if ($SelfTest) {
    Write-Subject $check 'the parser and the verdict arithmetic on canned lines (nothing is measured, no viewer is started)'
    $pages = 'general', 'recipients', 'images'
    $canned = @(
        '2026-10-13 18:00:00.001  UI timing: dialog | settings/window | 312.4 ms | budget 500 ms | PASS'
        '2026-10-13 18:00:01.001  UI timing: page-first | settings/recipients | 88.0 ms | budget 500 ms | PASS'
        '2026-10-13 18:00:01.501  UI timing: page-first | settings/images | 71.5 ms | budget 500 ms | PASS'
        '2026-10-13 18:00:01.801  UI timing: page-repeat | settings/general | 24.0 ms | budget 150 ms | PASS'
        '2026-10-13 18:00:02.001  UI timing: page-repeat | settings/general | 20.0 ms | budget 150 ms | PASS'
        '2026-10-13 18:00:02.501  UI timing: page-repeat | settings/recipients | 18.2 ms | budget 150 ms | PASS'
        '2026-10-13 18:00:03.001  UI timing: page-repeat | settings/images | 19.9 ms | budget 150 ms | PASS'
        '2026-10-13 18:00:04.001  UI timing: dialog | Hotkeys_Form | 140.0 ms | budget 500 ms | PASS'
        '2026-10-13 18:00:05.001  Reading audio tags: song.mp3'
    )
    $records = @($canned | ForEach-Object { ConvertFrom-TimingLine $_ } | Where-Object { $null -ne $_ })
    $problems = New-Object System.Collections.Generic.List[string]
    if ($records.Count -ne 8) { $problems.Add("parsed $($records.Count) of 8 canned lines") }
    if ($null -ne (ConvertFrom-TimingLine 'UI timing: wide | x | 1.0 ms | budget 500 ms | PASS')) { $problems.Add('an unknown kind was parsed') }
    $clean = Get-TimingVerdict $records $pages 1 1
    if ($clean.Code -ne 0) { $problems.Add("a clean run was judged $($clean.Code): $($clean.Detail)") }
    $slow = @($records) + @([pscustomobject]@{ Kind = 'page-repeat'; Name = 'settings/video'; Ms = 150.0; Budget = 150 })
    $over = Get-TimingVerdict $slow $pages 1 1
    if ($over.Code -ne 1) { $problems.Add('150 ms against the 150 ms budget was not OVER') }
    $absent = Get-TimingVerdict @($records | Where-Object { $_.Name -ne 'settings/images' }) $pages 1 1
    if ($absent.Code -ne 2) { $problems.Add('a page that was never measured was not COULD NOT VERIFY') }
    $noWindow = Get-TimingVerdict @($records | Where-Object { $_.Name -ne 'settings/window' }) $pages 1 1
    if ($noWindow.Code -ne 2) { $problems.Add('a missing window open was not COULD NOT VERIFY') }
    Write-TimingTable $records
    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "  self-test: $_" }
        Exit-Verdict $check 1 'the parser or the verdict arithmetic is wrong (see above)'
    }
    Exit-NotApplicable $check 'self-test only: the parser and the verdict arithmetic hold on canned lines; no page, window or viewer was measured'
}

# --- the subject --------------------------------------------------------------------------------------------

if ($SkipLayout -and $SkipViewer) { Exit-NotApplicable $check 'both scenes were switched off' }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$scenes = @()
if (-not $SkipLayout) { $scenes += 'layout (in process, scratch hive)' }
if (-not $SkipViewer) { $scenes += 'viewer (the built exe, FMS_UI_TIMING=1)' }
Write-Subject $check ("the settings window of the modern viewer in $repoRoot; scenes: " + ($scenes -join ', ') + "; evidence in $WorkDir")

$layoutCode = 0
$layoutDetail = ''
$viewerCode = 0
$viewerDetail = ''

# ==================================================================================================
# LAYOUT scene
# ==================================================================================================

function Invoke-LayoutScene {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return @{ Code = 2; Detail = 'no dotnet SDK on PATH - the layout scene is built from source' } }
    $project = Join-Path $repoRoot 'tests\Integration\SettingsLayoutScenes\SettingsLayoutScenes.csproj'
    if (-not (Test-Path -LiteralPath $project)) { return @{ Code = 2; Detail = "missing $project" } }
    $out = Join-Path $WorkDir 'layout-probe'
    Write-Host 'Layout scene: building the in-process harness..'
    & $dotnet.Source build $project -c Release -o $out --nologo -v:q 2>&1 | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "  $_" }
    $probe = Join-Path $out 'SettingsLayoutScenes.exe'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $probe)) { return @{ Code = 2; Detail = 'the layout harness did not build' } }

    $records = New-Object System.Collections.Generic.List[object]
    $code = 0
    $notes = New-Object System.Collections.Generic.List[string]
    foreach ($mode in @(@{ Name = 'unaware'; Args = @('--unaware') }, @{ Name = 'primary'; Args = @() })) {
        $json = Join-Path $WorkDir "layout-$($mode.Name).json"
        Write-Host "Layout scene: $($mode.Name) (every page twice, every group, expand/collapse all, reopen, resize)"
        & $probe $json @($mode.Args) 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $json)) { return @{ Code = 2; Detail = "the layout harness failed in the $($mode.Name) mode" } }
        $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        Write-Host "  scale: $([math]::Round($doc.dpi / 96.0 * 100)) % ($($doc.dpi) DPI), $($doc.steps.Count) steps; cold open $($doc.coldOpenMs) ms"
        # The first open of a cold process - construct, PrepareForDisplay, first idle - is a dialog.
        $records.Add([pscustomobject]@{ Kind = 'dialog'; Name = "$($mode.Name)/cold-open"; Ms = [double]$doc.coldOpenMs; Budget = $BudgetDialogMs })
        foreach ($step in $doc.steps) {
            $kind = $null
            $name = $step.step
            if ($name -like 'page:first:*') { $kind = 'page-first' }
            elseif ($name -like 'page:repeat:*' -or $name -like 'toggle:*' -or $name -like 'collapseAll:*' -or $name -like 'expandAll:*' -or $name -like 'afterGrow:*' -or $name -like 'afterShrink:*') { $kind = 'page-repeat' }
            elseif ($name -like 'reopen:*') { $kind = 'dialog' }
            if ($kind) {
                $records.Add([pscustomobject]@{ Kind = $kind; Name = "$($mode.Name)/$name"; Ms = [double]$step.ms; Budget = (Get-BudgetMs $kind) })
            }
        }
        if ($CompareLayoutWith) {
            $compareCode = 0
            & $probe compare $CompareLayoutWith $json 2>&1 | ForEach-Object { Write-Host "  $_" }
            $compareCode = $LASTEXITCODE
            if ($compareCode -ne 0) { $code = 1; $notes.Add("the $($mode.Name) layout differs from $CompareLayoutWith") }
            else { $notes.Add("the $($mode.Name) layout is identical to $CompareLayoutWith") }
        }
    }
    # The in-process window is not a fresh viewer: its first steps pay the JIT of a cold process, which is
    # counted (a person's first F2 pays it too) - the cold open is reported by the viewer scene, not here.
    $over = @($records | Where-Object { $_.Ms -ge $_.Budget })
    Write-TimingTable ($records | ForEach-Object { [pscustomobject]@{ Kind = $_.Kind; Name = ($_.Name -replace ':\d+(:\d)?$', ':*'); Ms = $_.Ms; Budget = $_.Budget } })
    if ($over.Count -gt 0) {
        $code = 1
        $worst = $over | Sort-Object { $_.Ms - $_.Budget } -Descending | Select-Object -First 1
        $notes.Add("$($over.Count) of $($records.Count) steps over budget, worst $($worst.Name) $([math]::Round($worst.Ms)) ms against $($worst.Budget)")
    } else {
        $notes.Add("$($records.Count) steps, every one within its budget (both scales)")
    }
    return @{ Code = $code; Detail = ($notes -join '; ') }
}

if (-not $SkipLayout) {
    $r = Invoke-LayoutScene
    $layoutCode = $r.Code
    $layoutDetail = $r.Detail
    Write-VerdictLine "$check[layout]" $layoutCode $layoutDetail
}

# ==================================================================================================
# VIEWER scene
# ==================================================================================================

$regPath = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$regKey  = 'HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$navIds  = 'settingsNavGeneral', 'settingsNavRecipients', 'settingsNavViewing', 'settingsNavVideo', 'settingsNavAudio', 'settingsNavFiles',
           'settingsNavResources', 'settingsNavOcr', 'settingsNavTranslation', 'settingsNavStorage', 'settingsNavAbout'
$pageIdsByNav = 'general', 'recipients', 'images', 'video', 'audio', 'files', 'resources', 'ocr', 'translation', 'storage', 'about'
$dialogRows = 'settingsRow_included_extensions', 'settingsRow_custom_hotkeys', 'settingsRow_recent_history'

function Invoke-ViewerScene {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -Namespace FmsTiming -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool IsWindow(System.IntPtr hWnd);
public delegate bool EnumProc(System.IntPtr hWnd, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool EnumWindows(EnumProc callback, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint processId);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool IsWindowVisible(System.IntPtr hWnd);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern int GetWindowText(System.IntPtr hWnd, System.Text.StringBuilder text, int count);
public struct RECT { public int Left, Top, Right, Bottom; }
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetWindowRect(System.IntPtr hWnd, out RECT rect);

// The visible top-level windows of one process: "handle|title|width|height" lines.
public static System.Collections.Generic.List<string> VisibleWindows(int processId) {
    var found = new System.Collections.Generic.List<string>();
    EnumWindows(delegate (System.IntPtr h, System.IntPtr l) {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        if (pid != (uint)processId || !IsWindowVisible(h)) return true;
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(h, sb, 256);
        RECT r;
        GetWindowRect(h, out r);
        found.Add(h.ToInt64() + "|" + sb + "|" + (r.Right - r.Left) + "|" + (r.Bottom - r.Top));
        return true;
    }, System.IntPtr.Zero);
    return found;
}
'@

    # -- which viewer ---------------------------------------------------------------------------------
    $exe = $Exe
    if (-not $exe) {
        $candidates = @(
            (Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'),
            (Join-Path $repoRoot 'src\Modern\bin\Release\net10.0-windows\win-x64\FastMediaSorter_LITE.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ }
        $exe = $candidates | Sort-Object { (Get-Item -LiteralPath $_).LastWriteTime } -Descending | Select-Object -First 1
    }
    if (-not $exe -or -not (Test-Path -LiteralPath $exe)) {
        return @{ Code = 2; Detail = 'no modern viewer built - run build.ps1 or dotnet build src\Modern\FastMediaSorter.Modern.vbproj -c Release -r win-x64' }
    }
    $exeDir = Split-Path -Parent $exe
    $exeInfo = Get-Item -LiteralPath $exe
    Write-Host "Viewer scene: $exe (built $($exeInfo.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"

    # -- the desktop and the viewer are this run's alone ---------------------------------------------------
    $session = (Get-Process -Id $PID).SessionId
    if (Get-Process -Name LogonUI -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session }) {
        return @{ Code = 2; Detail = 'the workstation is locked - the windows cannot be reached' }
    }
    $running = @(Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        return @{ Code = 2; Detail = "a viewer is already running (pid $($running[0].Id)) - it is not this run's to touch; close it and run again" }
    }
    try {
        $probe = [System.Threading.Mutex]::OpenExisting('FastMediaSorterSingleInstanceMutex')
        $probe.Dispose()
        return @{ Code = 2; Detail = 'the viewer single-instance mutex exists while no process matches - a foreign or elevated instance is alive' }
    } catch [System.Threading.WaitHandleCannotBeOpenedException] { }

    # -- a picture to open, so the viewer never goes looking for the owner's last folder ----------------------
    $sceneDir = Join-Path $WorkDir 'pictures'
    New-Item -ItemType Directory -Force -Path $sceneDir | Out-Null
    $picture = Join-Path $sceneDir 'timing-1.png'
    foreach ($index in 1, 2) {
        $bmp = New-Object System.Drawing.Bitmap 640, 400
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(40 * $index, 90, 160))
        $g.Dispose()
        $bmp.Save((Join-Path $sceneDir "timing-$index.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }

    # -- the settings hive: exported whole, restored whole --------------------------------------------------
    $regBackup = Join-Path $WorkDir 'hive-before.reg'
    $regExisted = Test-Path $regPath
    if (Test-Path -LiteralPath $regBackup) { Remove-Item -LiteralPath $regBackup -Force }
    if ($regExisted) {
        & reg.exe export $regKey $regBackup /y | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $regBackup) -or (Get-Item -LiteralPath $regBackup).Length -lt 10) {
            return @{ Code = 2; Detail = 'the viewer settings could not be exported, so they cannot be restored - nothing was started' }
        }
    }
    function Restore-Hive {
        if ($regExisted) {
            if (Test-Path $regPath) { & reg.exe delete $regKey /f | Out-Null }
            & reg.exe import $regBackup | Out-Null
        } elseif (Test-Path $regPath) {
            & reg.exe delete $regKey /f | Out-Null
        }
    }

    # -- UI Automation helpers -----------------------------------------------------------------------------
    $AE = [System.Windows.Automation.AutomationElement]
    $TS = [System.Windows.Automation.TreeScope]
    # A window that closes while the tree is being read raises "the target element corresponds to UI that is no
    # longer available": that is an answer ("not there"), never a failure of the run.
    function Get-ProcessWindows([int]$ProcId) {
        try {
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
            @($AE::RootElement.FindAll($TS::Children, $cond) | ForEach-Object { $_ } | Where-Object { $_.Current.ProcessId -eq $ProcId })
        } catch { @() }
    }
    function Find-ById($Scope, [string]$Id) {
        try {
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id)
            $Scope.FindFirst($TS::Descendants, $cond)
        } catch { $null }
    }
    # Windows 11 exposes an OWNED window (the settings window, a dialog) under its owner in the UIA tree, not under
    # the desktop: the main window's descendants are searched when the process' own top-level windows do not have it.
    $script:mainElement = $null
    function Find-WindowById([int]$ProcId, [string]$Id) {
        foreach ($w in (Get-ProcessWindows $ProcId)) { if ($w.Current.AutomationId -eq $Id) { return $w } }
        if ($script:mainElement) {
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id)
            $hit = $script:mainElement.FindFirst($TS::Descendants, $cond)
            if ($hit) { return $hit }
        }
        return $null
    }
    # What is visible now, by handle - minus the viewer's own floating chrome, which comes and goes by itself.
    $chromeTitle = 'Toolbar, row|Status bar|Window controls|Recipients'
    function Get-VisibleHandles([int]$ProcId) {
        @([FmsTiming.Native]::VisibleWindows($ProcId) | ForEach-Object {
            $p = $_ -split '\|'
            if ($p[1] -notmatch $chromeTitle -and [int]$p[2] -ge 120 -and [int]$p[3] -ge 80) { [Int64]$p[0] }
        })
    }
    # BM_CLICK posted to the button's own window: asynchronous, so a click that opens a modal dialog returns at once
    # (UIA Invoke would wait for the dialog), and it needs no focus - nothing is typed into another program.
    function Send-ButtonClick($Element) {
        $handle = [IntPtr]$Element.Current.NativeWindowHandle
        if ($handle -eq [IntPtr]::Zero) { throw "the element '$($Element.Current.AutomationId)' has no window handle to click" }
        [FmsTiming.Native]::PostMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    function Send-Close([int]$ProcId, $Window) {
        [FmsTiming.Native]::PostMessage([IntPtr]$Window.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    function Wait-True([int]$Seconds, [scriptblock]$Condition) {
        $deadline = (Get-Date).AddSeconds($Seconds)
        while ((Get-Date) -lt $deadline) {
            if (& $Condition) { return $true }
            Start-Sleep -Milliseconds 100
        }
        return $false
    }

    # -- the log: only what this run wrote --------------------------------------------------------------------
    $logPath = Join-Path $exeDir 'current.log'
    function Read-LogLines {
        if (-not (Test-Path -LiteralPath $logPath)) { return @() }
        $stream = [System.IO.File]::Open($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            $text = $reader.ReadToEnd()
        } finally { $stream.Dispose() }
        @($text -split "`r?`n")
    }
    $logMark = @(Read-LogLines).Count
    function Get-NewTimings {
        $lines = @(Read-LogLines)
        if ($lines.Count -le $logMark) { return @() }
        @($lines[$logMark..($lines.Count - 1)] | ForEach-Object { ConvertFrom-TimingLine $_ } | Where-Object { $null -ne $_ })
    }
    function Wait-Timings([int]$Count, [int]$Seconds) {
        Wait-True $Seconds { @(Get-NewTimings).Count -ge $Count } | Out-Null
        @(Get-NewTimings).Count
    }

    # -- start ------------------------------------------------------------------------------------------------
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = '"' + $picture + '"'
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $exeDir
    $psi.EnvironmentVariables['FMS_UI_TIMING'] = '1'
    $proc = $null
    $script:mainWin = $null
    $script:opened = $null
    $code = 2
    $detail = ''
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $main = $null
        if (-not (Wait-True 25 { $proc.Refresh(); if ($proc.HasExited) { return $true }; $script:mainWin = Find-WindowById $proc.Id 'Main_Form'; $null -ne $script:mainWin })) {
            return @{ Code = 2; Detail = 'the viewer never showed its window' }
        }
        if ($proc.HasExited) { return @{ Code = 2; Detail = "the viewer exited at once (code $($proc.ExitCode))" } }
        $main = $script:mainWin
        $script:mainElement = $main
        $mainHandle = [IntPtr]$main.Current.NativeWindowHandle
        # Start-up still holds the thread for a few seconds (the first picture, the destination slots' background probes):
        # the timing is of Settings after the viewer has settled, not of the viewer starting.
        Start-Sleep -Seconds 8

        function Open-Settings {
            # F2, posted to the viewer's own window - the settings key (Main_Form.KeyboardInput.vb).
            [FmsTiming.Native]::PostMessage($mainHandle, 0x0100, [IntPtr]0x71, [IntPtr]0x003C0001) | Out-Null
            [FmsTiming.Native]::PostMessage($mainHandle, 0x0101, [IntPtr]0x71, [IntPtr]0xC03C0001) | Out-Null
            $ok = Wait-True 20 { $null -ne (Find-WindowById $proc.Id 'Table_Form') }
            if (-not $ok) { throw 'F2 did not open the settings window within 20 s (a modal window may be in the way)' }
            return (Find-WindowById $proc.Id 'Table_Form')
        }

        # ---- cold open, then two warm ones --------------------------------------------------------------------
        Write-Host 'Viewer scene: settings window, cold open..'
        $settings = Open-Settings
        if ((Wait-Timings 1 15) -lt 1) { throw 'the viewer wrote no "UI timing:" line for the opened window - it is not built from this tree, or FMS_UI_TIMING was not read' }
        foreach ($warm in 1, 2) {
            Send-Close $proc.Id $settings
            if (-not (Wait-True 10 { $null -eq (Find-WindowById $proc.Id 'Table_Form') })) { throw 'the settings window did not close' }
            Start-Sleep -Milliseconds 600
            Write-Host "Viewer scene: settings window, warm open $warm.."
            $before = @(Get-NewTimings).Count
            $settings = Open-Settings
            if ((Wait-Timings ($before + 1) 15) -le $before) { throw "warm open $warm wrote no timing line" }
        }

        # ---- the pages, twice -------------------------------------------------------------------------------------
        foreach ($pass in 1, 2) {
            Write-Host "Viewer scene: pages, pass $pass.."
            for ($i = 0; $i -lt $navIds.Count; $i++) {
                $button = Find-ById $settings $navIds[$i]
                if (-not $button) { throw "the navigation button $($navIds[$i]) was not found" }
                $before = @(Get-NewTimings).Count
                Send-ButtonClick $button
                $after = Wait-Timings ($before + 1) 6
                if ($after -le $before) { Write-Host "  no measurement for $($pageIdsByNav[$i]) in pass $pass" }
                Start-Sleep -Milliseconds 150
            }
        }

        # ---- the windows the settings can reach, closed without changing a thing -------------------------------
        $reached = 0
        foreach ($rowId in $dialogRows + @('settingsToolSearch')) {
            # the row lives on its page: go there first (the Files page holds all three, the header holds the search tool)
            if ($rowId -eq 'settingsToolSearch') { $target = Find-ById $settings $rowId }
            else {
                Send-ButtonClick (Find-ById $settings 'settingsNavFiles')
                Start-Sleep -Milliseconds 500
                $row = Find-ById $settings $rowId
                $target = $null
                if ($row) {
                    $buttonCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
                    $target = $row.FindFirst($TS::Descendants, $buttonCond)
                }
            }
            if (-not $target) { Write-Host "  $rowId was not found - skipped"; continue }
            $known = @(Get-VisibleHandles $proc.Id)
            $before = @(Get-NewTimings).Count
            Write-Host "Viewer scene: opening the window behind $rowId.."
            Send-ButtonClick $target
            $script:opened = 0
            Wait-True 10 {
                $fresh = Get-VisibleHandles $proc.Id | Where-Object { $known -notcontains $_ } | Select-Object -First 1
                $script:opened = if ($null -ne $fresh) { [Int64]$fresh } else { 0 }
                $script:opened -ne 0
            } | Out-Null
            if ($script:opened) {
                $reached++
                Wait-Timings ($before + 1) 6 | Out-Null
                [FmsTiming.Native]::PostMessage([IntPtr]$script:opened, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_CLOSE
                Wait-True 8 { -not [FmsTiming.Native]::IsWindow([IntPtr]$script:opened) } | Out-Null
                Start-Sleep -Milliseconds 400
            } else {
                Write-Host "  no window opened for $rowId"
            }
        }
        Write-Host "Viewer scene: $reached secondary window(s) reached."

        $records = @(Get-NewTimings)
        if ($records.Count -eq 0) { return @{ Code = 2; Detail = 'the viewer wrote no timing lines' } }
        Write-TimingTable $records
        $verdict = Get-TimingVerdict $records $pageIdsByNav 3 3
        return @{ Code = $verdict.Code; Detail = $verdict.Detail }
    }
    catch {
        return @{ Code = 2; Detail = $_.Exception.Message }
    }
    finally {
        if ($proc -and -not $proc.HasExited) {
            # only the process this run started: ask it to close, and kill it only if it will not
            try { $null = $proc.CloseMainWindow() } catch { }
            if (-not $proc.WaitForExit(8000)) { try { $proc.Kill() } catch { } }
        }
        Restore-Hive
    }
}

if (-not $SkipViewer) {
    $r = Invoke-ViewerScene
    $viewerCode = $r.Code
    $viewerDetail = $r.Detail
    Write-VerdictLine "$check[viewer]" $viewerCode $viewerDetail
}

# --- the verdict -----------------------------------------------------------------------------------------------

$codes = @()
$details = @()
if (-not $SkipLayout) { $codes += $layoutCode; $details += "layout: $layoutDetail" }
if (-not $SkipViewer) { $codes += $viewerCode; $details += "viewer: $viewerDetail" }
$aggregate = Get-AggregateCode ([int[]]$codes)
Exit-Verdict $check $aggregate ($details -join ' | ')
