<#
.SYNOPSIS
    Opens a local .html file in the REAL mainline viewer, with the optional HTML viewer off and
    then on, and judges what the Runtime did about it (specification 275, work package W10).

.DESCRIPTION
    The unit tests of the HTML viewer never meet a real WebView2 Runtime, a real window or the real
    life cycle of a user-data folder; only this does. Each scene reads facts from outside the viewer
    process - the folder tree under %LOCALAPPDATA%\SZA\FastMediaSorter\documents\webview, the process
    table, the modules loaded by the viewer, the accessibility tree of its window - and says PASS,
    FAIL or COULD NOT VERIFY with the reason:

      S1  feature OFF (the default): the fixture opens as the source preview, no user-data folder
          is created, no msedgewebview2.exe is a descendant of the viewer, WebView2Loader.dll is
          never loaded (AC-1: "initializes no HTML runtime").
      S2  feature ON, online pages OFF: the fixture opens in the HTML view - an owned
          documents\webview\local\<guid> folder appears (and none under online\), an
          msedgewebview2.exe descendant of the viewer runs on exactly that folder, and the HTML
          view's toolbar is in the window.
      S3  after the viewer closes: every folder the run created is gone. The code promises two
          routes (HtmlRuntimeService): deleted when the browser process exits with no controller
          left, else swept by the NEXT launch (BeginLaunchSweep from Main_Form_Shown, whether or not
          the viewer is switched on; this scene relaunches with the HTML view). It accepts either
          and says which one happened; a folder that survives both is a FAIL.
      S4  a second fixture with a remote <img> and a <script> is forwarded into the running viewer
          (single instance): the page still opens in the HTML view. Nothing sniffs the network.
      S5  offline egress (H07, AC-4): a page whose 29 probe URLs (images, CSS, @import, fonts, frames,
          objects, media, prefetch/preconnect, form, ping, a meta refresh and a <base>) point at a loopback
          listener owned by this script or at host names made up for the run is opened with the online
          pages OFF. The listener must record nothing. The same pages opened unrestricted in headless Edge
          are the positive control - without them a silent listener proves nothing, and the scene says
          COULD NOT VERIFY. The Windows DNS cache is read as well when its self-test shows it can see a
          resolved probe name (Chromium's own resolver usually bypasses it, and then it is "not judged").
      S6  the defect S3 found: a profile folder a killed viewer left behind is swept by the next launch
          even though the HTML viewer is switched OFF.
      S7  paging and file release (H09 form half, H12): with IncludeDocumentInNavigation lent on, a posted
          Space and the toolbar's Previous/Next button page between .html files; the shown page and the
          css/image it links can be opened exclusively and renamed (nothing holds them); a posted Delete
          (ConfirmDelete never, no Recycle Bin: lent) removes the shown page and the next one comes up in
          the HTML view. Keys are WM_KEYDOWN/WM_KEYUP posted to the viewer's own window; buttons go through
          UI Automation. Nothing is typed or clicked, so nothing can land in another application.
      S8  toolbar zoom on the real Runtime (H10): Zoom in, the zoom label (reset), Zoom out, read from the
          page heading's rectangle in the accessibility tree.
      S9  toolbar find on the real Runtime (H10): three matches far apart, Next x4 wraps, Previous goes back;
          read from the scroll position of the heading. The match counter's own text is NOT in the
          accessibility tree (its accessible name is fixed), so it is reported, not judged.

    NOT covered, and said so: keys typed while the PAGE has the focus (a posted key cannot be that), the
    mouse, print and the browser hand-off dialogs, light/dark/high contrast, DPI, RTL/CJK, a second
    monitor, a screen reader and touch (H09 page half, H14, H17), a machine without the Runtime.

    A screenshot of the viewer's window rectangle per scene is kept in WorkDir as evidence for a person;
    it is not judged, and it is a screen grab, so it can show whatever else sits over that rectangle -
    keep WorkDir out of anything shared.

    The viewer is a COPY: pass -ExePath of a build made in a scratch folder
    (dotnet build src\Modern\FastMediaSorter.Modern.vbproj -c Debug -o <dir>), or leave it out to use
    bin\Release\FastMediaSorter_LITE.exe. The viewer writes its settings and state into the owner's
    registry key HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter, so that key is
    exported first, the HTML preferences are lent for the run, and the key is deleted and imported
    back last; a second export is compared byte for byte with the first. The history limit is lent as
    0 so the run leaves no reading history behind. Fixtures live in WorkDir, never in the repository.

    Never part of a default run, never part of -Integration: it opens windows and starts a browser
    (owner decision, as for OcrOverlayScenes). Run it by hand or with tools\Run-AllTests.ps1
    -HtmlScenes.

.PARAMETER ExePath
    The mainline viewer to drive. Default: bin\Release\FastMediaSorter_LITE.exe.

.PARAMETER WorkDir
    Fixtures, screenshots and the settings export. Default: %TEMP%\fms-html-scenes.

.PARAMETER SettleSeconds
    How long a scene waits after the window shows the file before it reads the facts.

.PARAMETER StartTimeoutSeconds
    How long a cold Runtime may take to start before the scene gives up.

.PARAMETER Only
    Scene ids to run, e.g. -Only S5,S7 (default: all). S1-S4 are one chain and run together when any of
    them is named.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every scene passed. 1 = a scene failed. 2 = could not verify: no
    interactive (unlocked) desktop, no WebView2 Runtime, a viewer already running (its settings and
    the single-instance mutex would win), no viewer exe or none with the HTML payload, the settings
    key could not be saved, a scene could not run, or the key was not restored byte for byte.
    The last line is the verdict, `HtmlViewerScenes: <WORD> (..)`.
#>
[CmdletBinding()]
param(
    [string]$ExePath = '',
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-html-scenes'),
    [int]$SettleSeconds = 8,
    [int]$StartTimeoutSeconds = 45,
    [string[]]$Only = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

                       # ..\Integration -> ..\tests -> the repository root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe' }
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
$check = 'HtmlViewerScenes'
$allScenes = 'S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', 'S8', 'S9'
# -Only S5,S7 runs just those; S1-S4 are one chain (S4 needs the viewer S2 left, S3 the folders S2 made).
# (`pwsh -File x.ps1 -Only S5,S6` hands over ONE string "S5,S6", so it is split here)
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
function Want([string]$Id) { return ($Only.Count -eq 0) -or ($Only -contains $Id) }

# An unexpected terminating error before the run starts must not surface as exit 1 (a failed check).
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the harness itself failed'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class FmsRect { public int Left, Top, Right, Bottom; }
public static class FmsHtmlScenesNative {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code, uint mapType);
    public static void DpiAware() { SetProcessDPIAware(); }
    // A key as a posted WM_KEYDOWN/WM_KEYUP pair to ONE window: it can never land in another application
    // (SendKeys and AppActivate can), and the form's own key handling sees it like a typed key.
    public static void PostKey(IntPtr window, int vk) {
        uint scan = MapVirtualKeyW((uint)vk, 0);
        IntPtr down = (IntPtr)(long)(1u | (scan << 16));
        IntPtr up = (IntPtr)(long)(1u | (scan << 16) | (1u << 30) | (1u << 31));
        PostMessageW(window, 0x0100, (IntPtr)vk, down);
        PostMessageW(window, 0x0101, (IntPtr)vk, up);
    }
    // The input desktop is "Default" only while the workstation is unlocked and a user is at it.
    public static bool InputDesktopIsUsable() {
        IntPtr d = OpenInputDesktop(0, false, 0x0100);   // DESKTOP_SWITCHDESKTOP
        if (d == IntPtr.Zero) return false;
        CloseDesktop(d);
        return true;
    }
    public static FmsRect WindowRect(IntPtr window) {
        RECT r;
        if (window == IntPtr.Zero || IsIconic(window) || !GetWindowRect(window, out r)) return null;
        return new FmsRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }
}
'@
[FmsHtmlScenesNative]::DpiAware()
Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
// A loopback TCP listener that accepts on its own thread and reads each connection's first request line on a
// thread of its own, so a connection Chromium opens and drops at once is still recorded as it happens.
public sealed class FmsProbeListener : IDisposable {
    readonly TcpListener listener;
    readonly ConcurrentQueue<string> seen = new ConcurrentQueue<string>();
    volatile bool stopping;
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public FmsProbeListener() {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        new Thread(AcceptLoop) { IsBackground = true }.Start();
    }
    void AcceptLoop() {
        while (!stopping) {
            try {
                TcpClient client = listener.AcceptTcpClient();
                new Thread(() => Handle(client)) { IsBackground = true }.Start();
            } catch { if (stopping) return; }
        }
    }
    void Handle(TcpClient client) {
        string line = "(connected, sent no request)";
        try {
            client.ReceiveTimeout = 1500;
            byte[] buffer = new byte[512];
            int n = client.GetStream().Read(buffer, 0, buffer.Length);
            if (n > 0) line = Encoding.ASCII.GetString(buffer, 0, n).Split('\n')[0].Trim();
        } catch { }
        seen.Enqueue(line);
        try { client.Close(); } catch { }
    }
    public string[] Drain() {
        List<string> list = new List<string>();
        string s;
        while (seen.TryDequeue(out s)) list.Add(s);
        return list.ToArray();
    }
    public void Dispose() { stopping = true; try { listener.Stop(); } catch { } }
}
'@

$edgeClient = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$regKey     = 'HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$regPath    = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$localApp   = [Environment]::GetFolderPath('LocalApplicationData')
$udfRoot    = Join-Path $localApp 'SZA\FastMediaSorter\documents\webview'
$exeDir     = Split-Path -Parent $ExePath
$viewerLog  = Join-Path $exeDir 'current.log'

# --- could-not-verify gates: nothing is touched before all of them hold ---------------------

Write-Subject $check "$ExePath, fixtures and evidence in $WorkDir"

if (-not [Environment]::UserInteractive -or -not [FmsHtmlScenesNative]::InputDesktopIsUsable()) {
    Exit-Verdict $check 2 'no interactive desktop (a service session or a locked workstation)'
}
if (-not (Test-Path -LiteralPath $ExePath)) {
    Exit-Verdict $check 2 "no viewer at $ExePath - build one first (see -ExePath)"
}
if ((Split-Path -Leaf $ExePath) -ne 'FastMediaSorter_LITE.exe') {
    Exit-Verdict $check 2 'only the mainline FastMediaSorter_LITE.exe has an HTML viewer; the net48 x86 exe is out of scope'
}
if (-not (Test-Path -LiteralPath (Join-Path $exeDir 'webview2\win-x64\WebView2Loader.dll'))) {
    Exit-Verdict $check 2 "no webview2\win-x64\WebView2Loader.dll beside $ExePath - a build without the HTML viewer payload"
}
$runtimeVersion = ''
foreach ($hive in 'HKLM:\SOFTWARE\WOW6432Node', 'HKLM:\SOFTWARE', 'HKCU:\SOFTWARE') {
    $key = "$hive\Microsoft\EdgeUpdate\Clients\$edgeClient"
    $pv = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue)
    if ($null -ne $pv -and $pv.PSObject.Properties.Name -contains 'pv' -and $pv.pv) { $runtimeVersion = [string]$pv.pv; break }
}
if (-not $runtimeVersion) {
    Exit-Verdict $check 2 'no Microsoft Edge WebView2 Runtime is registered (EdgeUpdate client key has no pv)'
}
if (Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue) {
    Exit-Verdict $check 2 'a viewer is already running - stop it first, or its settings and the single-instance mutex win'
}
if (Get-Process -Name FastMediaSorterCompanion -ErrorAction SilentlyContinue) {
    Write-Host 'Note: the Share Manager is running; it shares the settings hive and could write to it during the run.'
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$exeVersion = (Get-Item -LiteralPath $ExePath).VersionInfo.FileVersion
Write-Host "Runtime $runtimeVersion; viewer $exeVersion"

# --- fixtures ------------------------------------------------------------------------------

$fixtureA = Join-Path $WorkDir 'fixtures\a\scene-a.html'
$fixtureB = Join-Path $WorkDir 'fixtures\b\scene-b.html'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fixtureA), (Split-Path -Parent $fixtureB) | Out-Null
$markerA = 'FMS-HTML-FIXTURE-275 rendered heading'
$markerB = 'FMS-HTML-FIXTURE-275-B remote image and script'
[IO.File]::WriteAllText($fixtureA, @"
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>FMS HTML scene A</title>
<style>body{font:30px "Segoe UI",sans-serif;background:#fff3c4;color:#1a1a1a;margin:48px}h1{color:#0a4d8c}</style></head>
<body><h1>$markerA</h1>
<p>Read as a styled page, not as markup: the HTML view is on.</p></body></html>
"@, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText($fixtureB, @'
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>FMS HTML scene B</title>
<style>body{font:30px "Segoe UI",sans-serif;background:#d9f2d9;color:#1a1a1a;margin:48px}h1{color:#14532d}</style></head>
<body><h1>FMS-HTML-FIXTURE-275-B remote image and script</h1>
<p>An image from https://example.invalid and a script sit in this page.</p>
<img src="https://example.invalid/x.png" alt="remote image" width="64" height="64">
<script>document.title = 'SCRIPT-RAN';</script></body></html>
'@, (New-Object Text.UTF8Encoding($false)))

# --- helpers -------------------------------------------------------------------------------

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds, [int]$StepMs = 300) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds $StepMs
    }
    return [bool](& $Condition)
}

# The owned user-data folders: GUID-named children of local\ and online\ (HtmlRuntimeService.OwnedChildren).
function Get-OwnedUdfs([string]$Profile) {
    $dir = Join-Path $udfRoot $Profile
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Directory -Force |
        Where-Object { $_.Name -match '^[0-9a-f]{32}$' } | ForEach-Object { $_.FullName })
}

# msedgewebview2.exe processes whose parent chain reaches the viewer. Other applications run their own
# (Windows search does), so a bare process-name test would prove nothing about this viewer.
function Get-WebViewDescendants([int]$RootPid) {
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object 'System.Collections.Generic.Queue[int]'
    $queue.Enqueue($RootPid)
    while ($queue.Count -gt 0) {
        $cur = $queue.Dequeue()
        foreach ($p in $all) {
            if ([int]$p.ParentProcessId -eq $cur -and $seen.Add([int]$p.ProcessId)) { $queue.Enqueue([int]$p.ProcessId) }
        }
    }
    return @($all | Where-Object { $_.Name -eq 'msedgewebview2.exe' -and $seen.Contains([int]$_.ProcessId) })
}

function Get-UserDataDir($Process) {
    if (-not $Process.CommandLine) { return '' }
    $m = [regex]::Match($Process.CommandLine, '--user-data-dir=(?:"([^"]+)"|(\S+))')
    if (-not $m.Success) { return '' }
    $v = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
    $v = $v.TrimEnd('\')
    # WebView2 keeps its profile in <user-data folder>\EBWebView and passes THAT to the browser process;
    # the owned folder is the one above it.
    if ((Split-Path -Leaf $v) -ieq 'EBWebView') { $v = Split-Path -Parent $v }
    return $v
}

# What the viewer recorded as the file it last showed (written when the window closes, so it can only be
# read after Stop-Viewer): the proof, in any build, that it took the fixture from its command line.
function Get-LastShownFile {
    $item = Get-ItemProperty -LiteralPath $regPath -Name 'LastPlayedFile' -ErrorAction SilentlyContinue
    if ($null -eq $item) { return '' }
    return [string]$item.LastPlayedFile
}

function Update-Viewer($Process) {
    $Process.Refresh()
    return (-not $Process.HasExited)
}

function Get-WindowHandle($Process) {
    $Process.Refresh()
    if ($Process.HasExited) { return [IntPtr]::Zero }
    return $Process.MainWindowHandle
}

# The HTML view's toolbar (BuildHtmlViewUi) is a ToolStrip whose accessible name carries "HTML"; the
# source preview has no such toolbar. $null when the window shows none, or UI Automation cannot read it.
function Find-HtmlToolbar([IntPtr]$Window) {
    if ($Window -eq [IntPtr]::Zero) { return $null }
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Window)
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ToolBar)
        foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            if ($e.Current.Name -match 'HTML') { return $e.Current.Name }
        }
    } catch { }
    return $null
}

# Informational: whether the page's own heading is in the accessibility tree (a rendered page, not markup).
function Test-RenderedText([IntPtr]$Window, [string]$Text) {
    if ($Window -eq [IntPtr]::Zero) { return $false }
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Window)
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Text)
        return ($null -ne $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition))
    } catch { return $false }
}

function Save-WindowShot([IntPtr]$Window, [string]$Name) {
    $path = Join-Path $WorkDir $Name
    $r = [FmsHtmlScenesNative]::WindowRect($Window)
    if ($null -eq $r) { return '(no window to capture)' }
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { return '(empty window rectangle)' }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size) } finally { $g.Dispose() }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bmp.Dispose() }
    return $path
}

function Get-LoadedModuleNames($Process) {
    try { return @($Process.Modules | ForEach-Object { $_.ModuleName }) } catch { return $null }
}

function Start-Viewer([string]$File) {
    return Start-Process -FilePath $ExePath -ArgumentList "`"$File`"" -WorkingDirectory $exeDir -PassThru
}

# CloseMainWindow then wait; Kill is the last resort and is reported, never silent.
function Stop-Viewer($Process) {
    if ($null -eq $Process) { return 'not started' }
    $Process.Refresh()
    if ($Process.HasExited) { return 'had already exited' }
    [void]$Process.CloseMainWindow()
    if ($Process.WaitForExit(20000)) { return 'closed by CloseMainWindow' }
    try { $Process.Kill($true) } catch { }
    [void]$Process.WaitForExit(5000)
    return 'KILLED after 20 s: the window did not close'
}

function Get-LogSize { if (Test-Path -LiteralPath $viewerLog) { return (Get-Item -LiteralPath $viewerLog).Length } else { return 0L } }

# The lines the viewer appended to current.log since $From that mention the HTML viewer or a fault.
function Get-LogEvidence([long]$From) {
    if (-not (Test-Path -LiteralPath $viewerLog)) { return @() }
    try {
        $fs = [IO.File]::Open($viewerLog, 'Open', 'Read', 'ReadWrite')
        try {
            if ($fs.Length -le $From) { return @() }
            [void]$fs.Seek($From, 'Begin')
            $text = (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd()
        } finally { $fs.Dispose() }
        return @($text -split "\r?\n" | Where-Object { $_ -match 'webview|html (view|runtime|host)|exception|unhandled' } | Select-Object -First 12)
    } catch { return @() }
}

$scenes = New-Object System.Collections.Generic.List[object]
function Add-Scene([string]$Id, [string]$Result, [string]$Reason) {
    $scenes.Add([pscustomobject]@{ Id = $Id; Result = $Result; Reason = $Reason })
    Write-Host ("{0,-3} {1,-16} {2}" -f $Id, $Result, $Reason)
}

# --- settings: export, lend, restore ------------------------------------------------------

$keyExisted = Test-Path -LiteralPath $regPath
$regBackup  = Join-Path $WorkDir 'settings-before.reg'
$regAfter   = Join-Path $WorkDir 'settings-after.reg'
if ($keyExisted) {
    & reg.exe export $regKey $regBackup /y 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $regBackup) -or (Get-Item -LiteralPath $regBackup).Length -eq 0) {
        Exit-Verdict $check 2 'the settings key could not be exported - refusing to run a viewer against an unsaved key'
    }
}
$restoreIdentical = $null   # $true / $false once the key has been put back and compared

# Lent values are not put back one by one: the whole key goes back from the export (finally block).
function Lend-Setting([string]$Name, [string]$Value) {
    if (-not (Test-Path -LiteralPath $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    Set-ItemProperty -LiteralPath $regPath -Name $Name -Value $Value
}

# Same-moment facts of a running viewer, shared by S1, S2 and S4.
function Read-Facts($Process) {
    $alive = Update-Viewer $Process
    $window = Get-WindowHandle $Process
    $webview = @(Get-WebViewDescendants $Process.Id)
    $modules = if ($alive) { Get-LoadedModuleNames $Process } else { $null }
    [pscustomobject]@{
        Alive    = $alive
        Window   = $window
        Title    = if ($alive) { $Process.MainWindowTitle } else { '' }
        WebViews = $webview
        Udfs     = @($webview | ForEach-Object { Get-UserDataDir $_ } | Where-Object { $_ } | Sort-Object -Unique)
        Loader   = if ($null -eq $modules) { $null } else { [bool]($modules -contains 'WebView2Loader.dll') }
        Toolbar  = Find-HtmlToolbar $window
    }
}

# --- helpers of the scenes S5-S9: each starts a viewer of its own on its own fixtures -----------

$extra = New-Object System.Collections.Generic.List[string]   # owned folders S5-S9 saw appear (swept in finally)

function Find-ByName([IntPtr]$Window, [string]$Name) {
    if ($Window -eq [IntPtr]::Zero) { return $null }
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Window)
        $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
    } catch { return $null }
}

# A toolbar button pressed through UI Automation's InvokePattern: no focus, no mouse, no keystroke.
function Invoke-ToolbarButton([IntPtr]$Window, [string]$Name) {
    $e = Find-ByName $Window $Name
    if ($null -eq $e) { return $false }
    $pattern = $null
    if (-not $e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { return $false }
    $pattern.Invoke()
    return $true
}

# The viewer on one fixture, HTML view on and online pages off (the caller lends the settings); returns once
# the HTML toolbar is up and the page's heading $Marker is in the accessibility tree.
function Open-HtmlScene([string]$File, [string]$Marker) {
    $baseline = @(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online')
    $v = Start-Viewer $File
    $ok = Wait-Until {
        if (-not (Update-Viewer $v)) { return $true }
        $w = Get-WindowHandle $v
        if ($w -eq [IntPtr]::Zero) { return $false }
        ($null -ne (Find-HtmlToolbar $w)) -and (Test-RenderedText $w $Marker)
    } $StartTimeoutSeconds 1000
    foreach ($u in (@(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online'))) {
        if ($baseline -notcontains $u -and -not $extra.Contains($u)) { $extra.Add($u) }
    }
    $alive = Update-Viewer $v
    if ($ok -and $alive) { Start-Sleep -Seconds 2 }
    return [pscustomobject]@{ Process = $v; Ready = ([bool]$ok -and $alive) }
}

function Wait-Marker($Process, [string]$Marker, [int]$Seconds = 20) {
    return Wait-Until { (Update-Viewer $Process) -and (Test-RenderedText (Get-WindowHandle $Process) $Marker) } $Seconds 500
}

function Get-HeadingRect([IntPtr]$Window, [string]$Marker) {
    $e = Find-ByName $Window $Marker
    if ($null -eq $e) { return $null }
    try { return $e.Current.BoundingRectangle } catch { return $null }
}

# What the loopback listener recorded since the last call: the first request line of each connection, or
# "(connected, sent no request)". It waits first - a connection that stays silent is written down only once
# its read times out (1.5 s).
function Receive-Probes($Listener, $Into) {
    Start-Sleep -Milliseconds 2000
    foreach ($line in $Listener.Drain()) { $Into.Add($line) }
}

# Probe host names this run made up and the Windows DNS client cache saw (an unreadable cache reads as none:
# the self-test in S5 then fails to see its own resolved name and the DNS half is reported as not judged).
function Get-ProbeDns([string]$Guid) {
    try {
        return @(Get-DnsClientCache -ErrorAction Stop | Where-Object { $_.Entry -like "*fms275-$Guid-*" } |
            ForEach-Object { $_.Entry } | Sort-Object -Unique)
    } catch { return @() }
}

$created = New-Object System.Collections.Generic.List[string]   # owned folders this run saw appear
$harnessError = $null
$viewer = $null
$rootExistedBefore = $null
try {
    $runBase = @('S1', 'S2', 'S3', 'S4' | Where-Object { Want $_ }).Count -gt 0
    if ($runBase) {
    # ----- S1: feature off ---------------------------------------------------------------
    Lend-Setting 'HtmlViewerEnabled' '0'
    Lend-Setting 'HtmlOnlinePagesEnabled' '0'
    Lend-Setting 'HtmlHistoryLimit' '0'
    $rootExistedBefore = Test-Path -LiteralPath $udfRoot
    $baseline = @(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online')

    $logFrom = Get-LogSize
    $viewer = Start-Viewer $fixtureA
    $shown = Wait-Until { (Update-Viewer $viewer) -and (Get-WindowHandle $viewer) -ne [IntPtr]::Zero } $StartTimeoutSeconds
    if ($shown) { Start-Sleep -Seconds $SettleSeconds }
    $f = Read-Facts $viewer
    $shot = if ($f.Alive) { Save-WindowShot $f.Window 's1-feature-off.png' } else { '(viewer gone)' }
    $now = @(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online')
    $newUdfs = @($now | Where-Object { $baseline -notcontains $_ })
    Write-Host ("  S1 facts: msedgewebview2 descendants {0}, loader loaded {1}, html toolbar '{2}', new owned folders {3}, shot {4}" -f
        $f.WebViews.Count, $f.Loader, $f.Toolbar, $newUdfs.Count, $shot)
    $s1Close = Stop-Viewer $viewer
    $viewer = $null
    Write-Host "  S1 viewer: $s1Close"
    foreach ($l in (Get-LogEvidence $logFrom)) { Write-Host "  log: $l" }
    $lastShown = Get-LastShownFile
    $why = @()
    if (-not $shown -or -not $f.Alive) {
        Add-Scene 'S1' 'COULD NOT VERIFY' 'the viewer showed no window, or exited before it could be read'
    } elseif ($lastShown -ne $fixtureA) {
        Add-Scene 'S1' 'COULD NOT VERIFY' "the viewer did not record the fixture as its last shown file (LastPlayedFile = '$lastShown'; closing: $s1Close)"
    } else {
        if ($newUdfs.Count -gt 0) { $why += "a user-data folder was created: $($newUdfs -join ', ')" }
        if ((Test-Path -LiteralPath $udfRoot) -and -not $rootExistedBefore) { $why += 'documents\webview was created' }
        if ($f.WebViews.Count -gt 0) { $why += "$($f.WebViews.Count) msedgewebview2.exe descendant(s) of the viewer" }
        if ($f.Loader -eq $true) { $why += 'WebView2Loader.dll is loaded in the viewer' }
        if ($null -ne $f.Toolbar) { $why += "the HTML view toolbar '$($f.Toolbar)' is on screen" }
        if ($why.Count -eq 0) {
            $loaderText = if ($null -eq $f.Loader) { 'module list unreadable' } else { 'WebView2Loader.dll not loaded' }
            Add-Scene 'S1' 'PASS' "feature off: the fixture was the file shown (LastPlayedFile), no user-data folder, no msedgewebview2 child, $loaderText, no HTML toolbar"
        } else { Add-Scene 'S1' 'FAIL' ($why -join '; ') }
    }
    Start-Sleep -Seconds 2

    # ----- S2: feature on, online off --------------------------------------------------------
    Lend-Setting 'HtmlViewerEnabled' '1'
    Lend-Setting 'HtmlOnlinePagesEnabled' '0'
    $baseline = @(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online')
    $logFrom = Get-LogSize
    $viewer = Start-Viewer $fixtureA
    $ready = Wait-Until {
        if (-not (Update-Viewer $viewer)) { return $true }
        $w = Get-WindowHandle $viewer
        if ($w -eq [IntPtr]::Zero) { return $false }
        $fresh = @(Get-OwnedUdfs 'local' | Where-Object { $baseline -notcontains $_ })
        if ($fresh.Count -eq 0) { return $false }
        $kids = @(Get-WebViewDescendants $viewer.Id | ForEach-Object { Get-UserDataDir $_ })
        return ($kids -contains $fresh[0].TrimEnd('\')) -and ($null -ne (Find-HtmlToolbar $w)) -and (Test-RenderedText $w $markerA)
    } $StartTimeoutSeconds 1000
    if ($ready -and (Update-Viewer $viewer)) { Start-Sleep -Seconds $SettleSeconds }
    $f = Read-Facts $viewer
    $shot = if ($f.Alive) { Save-WindowShot $f.Window 's2-feature-on.png' } else { '(viewer gone)' }
    $freshLocal = @(Get-OwnedUdfs 'local' | Where-Object { $baseline -notcontains $_ })
    $freshOnline = @(Get-OwnedUdfs 'online' | Where-Object { $baseline -notcontains $_ })
    foreach ($u in $freshLocal + $freshOnline) { if (-not $created.Contains($u)) { $created.Add($u) } }
    $rendered = if ($f.Alive) { Test-RenderedText $f.Window $markerA } else { $false }
    Write-Host ("  S2 facts: title '{0}', new local folders {1}, new online folders {2}, msedgewebview2 descendants {3} on [{4}], html toolbar '{5}', loader loaded {6}, heading in UIA tree {7}, shot {8}" -f
        $f.Title, ($freshLocal -join ','), $freshOnline.Count, $f.WebViews.Count, ($f.Udfs -join ','), $f.Toolbar, $f.Loader, $rendered, $shot)
    foreach ($l in (Get-LogEvidence $logFrom)) { Write-Host "  log: $l" }
    $s2Ok = $false
    if (-not $f.Alive) {
        Add-Scene 'S2' 'FAIL' 'the viewer exited while opening the fixture with the HTML viewer on'
    } else {
        $why = @()
        if ($freshLocal.Count -eq 0) { $why += 'no owned user-data folder appeared under documents\webview\local' }
        elseif ($freshLocal.Count -gt 1) { $why += "$($freshLocal.Count) owned local folders for one document" }
        if ($freshOnline.Count -gt 0) { $why += 'a folder appeared under online\ with online pages off' }
        if ($f.WebViews.Count -eq 0) { $why += 'no msedgewebview2.exe descendant of the viewer' }
        elseif ($freshLocal.Count -ge 1 -and ($f.Udfs -notcontains $freshLocal[0].TrimEnd('\'))) { $why += "no msedgewebview2.exe runs on the owned folder (they run on: $($f.Udfs -join ', '))" }
        if ($null -eq $f.Toolbar) { $why += 'the HTML view toolbar is not in the window (the source preview, or a failed open)' }
        if (-not $rendered) { $why += "the page's own heading is not in the window's accessibility tree (not rendered, or an error page)" }
        if ($why.Count -eq 0) {
            $s2Ok = $true
            Add-Scene 'S2' 'PASS' ("feature on: owned folder local\{0}, {1} msedgewebview2.exe descendant(s) on it, toolbar '{2}', the page's heading rendered in the window" -f (Split-Path -Leaf $freshLocal[0]), $f.WebViews.Count, $f.Toolbar)
        } else { Add-Scene 'S2' 'FAIL' ($why -join '; ') }
    }

    # ----- S4: a page with a remote image and a script, forwarded into the running viewer ---------
    if (-not $s2Ok) {
        Add-Scene 'S4' 'COULD NOT VERIFY' 'S2 did not leave a running viewer showing the HTML view'
    } else {
        $logFrom = Get-LogSize
        Start-Process -FilePath $ExePath -ArgumentList "`"$fixtureB`"" -WorkingDirectory $exeDir -Wait
        $titleOk = Wait-Until { (Update-Viewer $viewer) -and (Test-RenderedText (Get-WindowHandle $viewer) $markerB) } 30
        if ($titleOk) { Start-Sleep -Seconds $SettleSeconds }
        $f = Read-Facts $viewer
        $shot = if ($f.Alive) { Save-WindowShot $f.Window 's4-remote-image-and-script.png' } else { '(viewer gone)' }
        foreach ($u in (@(Get-OwnedUdfs 'local') + @(Get-OwnedUdfs 'online'))) {
            if ($baseline -notcontains $u -and -not $created.Contains($u)) { $created.Add($u) }
        }
        $onlineNow = @(Get-OwnedUdfs 'online' | Where-Object { $baseline -notcontains $_ })
        Write-Host ("  S4 facts: title '{0}', msedgewebview2 descendants {1}, html toolbar '{2}', online folders {3}, shot {4}" -f
            $f.Title, $f.WebViews.Count, $f.Toolbar, $onlineNow.Count, $shot)
        foreach ($l in (Get-LogEvidence $logFrom)) { Write-Host "  log: $l" }
        $why = @()
        if (-not $f.Alive) { $why += 'the viewer exited' }
        else {
            if (-not $titleOk) { $why += "the forwarded fixture's heading never appeared in the window (not shown in 30 s)" }
            if ($null -eq $f.Toolbar) { $why += 'the HTML view toolbar is not in the window' }
            if ($f.WebViews.Count -eq 0) { $why += 'no msedgewebview2.exe descendant of the viewer' }
            if ($onlineNow.Count -gt 0) { $why += 'an online profile folder appeared' }
        }
        if ($why.Count -eq 0) { Add-Scene 'S4' 'PASS' "page with a remote <img> and a <script> opened in the HTML view ('$($f.Title)'), viewer alive" }
        else { Add-Scene 'S4' 'FAIL' ($why -join '; ') }
    }

    # ----- S3: the viewer closes, the owned folders must go ---------------------------------------
    $closeResult = Stop-Viewer $viewer
    $viewer = $null
    Write-Host "  viewer closed: $closeResult"
    $created = @($created)
    if ($created.Count -eq 0) {
        Add-Scene 'S3' 'COULD NOT VERIFY' 'S2 created no owned folder, so there is nothing to see removed'
    } else {
        $goneAtExit = Wait-Until { @($created | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0 } 25 500
        if ($goneAtExit) {
            Add-Scene 'S3' 'PASS' "$($created.Count) owned folder(s) removed once the viewer closed (route: deleted at browser-process exit); viewer $closeResult"
        } else {
            $left = @($created | Where-Object { Test-Path -LiteralPath $_ })
            $strays = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $u = Get-UserDataDir $_; $u -and ($created -contains $u) })
            $leftBytes = [long](($left | ForEach-Object {
                (Get-ChildItem -LiteralPath $_ -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
            } | Measure-Object -Sum).Sum)
            Write-Host ("  S3: {0} folder(s) ({1:N0} bytes) still there 25 s after the viewer closed; msedgewebview2 still on them: {2}. Trying the second route: a new launch sweeps." -f $left.Count, $leftBytes, $strays.Count)
            $baselineNow = @(Get-OwnedUdfs 'local')
            $viewer = Start-Viewer $fixtureA
            $swept = Wait-Until {
                if (-not (Update-Viewer $viewer)) { return $true }
                (@($left | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0)
            } $StartTimeoutSeconds 1000
            $stillThere = @($left | Where-Object { Test-Path -LiteralPath $_ })
            foreach ($u in (Get-OwnedUdfs 'local')) { if ($baselineNow -notcontains $u -and $created -notcontains $u) { $created += $u } }
            Write-Host "  S3 sweep launch: viewer $(Stop-Viewer $viewer)"
            $viewer = $null
            if ($stillThere.Count -eq 0) {
                Add-Scene 'S3' 'PASS' "$($left.Count) owned folder(s) ($('{0:N0}' -f $leftBytes) bytes) survived the exit, no browser process left on them, and were swept by the next launch that opened the HTML view (route: sweep on next start)"
            } else {
                Add-Scene 'S3' 'FAIL' "owned folder(s) survived both the exit and the next launch's sweep: $($stillThere -join ', ')"
            }
        }
    }
    }   # $runBase

    # The scenes below start from a clean slate: the viewer settings they depend on are lent again, each
    # scene has a viewer of its own and closes it before the next one.
    function Lend-HtmlOn {
        Lend-Setting 'HtmlViewerEnabled' '1'
        Lend-Setting 'HtmlOnlinePagesEnabled' '0'
        Lend-Setting 'HtmlHistoryLimit' '0'
        Lend-Setting 'HtmlZoom' '100'
        Lend-Setting 'HtmlFindCase' '0'
        Lend-Setting 'HtmlFindWholeWord' '0'
    }
    $toolbarName = 'Просмотр HTML-страницы'   # the Russian UI the owner's key selects; another language cannot be driven by name

    # ----- S5: offline egress (H07, AC-4) -----------------------------------------------------------
    # A page whose every resource points at a loopback listener this script owns, or at a host name made
    # up for this run, is opened with the online pages OFF. Nothing may reach the listener, and no probe
    # name may reach the Windows DNS cache. The same page, opened unrestricted in headless Edge, is the
    # positive control: without it a silent listener proves nothing.
    if (Want 'S5') {
        $listener = New-Object FmsProbeListener
        $s5Viewer = $null
        try {
            $port = $listener.Port
            $probeDir = Join-Path $WorkDir 'fixtures\d'
            New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
            $markerD = 'FMS-HTML-EGRESS-275 offline probe page'
            $markerD2 = 'FMS-HTML-EGRESS-275 refresh probe page'
            $templateD = @'
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>FMS egress probe</title>
<link rel="stylesheet" href="http://127.0.0.1:@PORT@/css-link">
<link rel="stylesheet" href="http://fms275-@GUID@-csshost.example.invalid/a.css">
<link rel="icon" href="http://127.0.0.1:@PORT@/icon">
<link rel="prefetch" href="http://127.0.0.1:@PORT@/prefetch">
<link rel="preload" as="image" href="http://127.0.0.1:@PORT@/preload">
<link rel="preconnect" href="http://fms275-@GUID@-preconnect.example.invalid">
<link rel="dns-prefetch" href="//fms275-@GUID@-dnsprefetch.example.invalid">
<style>
@import url("http://127.0.0.1:@PORT@/css-import");
@font-face{font-family:probe;src:url("http://127.0.0.1:@PORT@/font")}
body{font:30px probe,"Segoe UI",sans-serif;background:#e6e0ff url("http://127.0.0.1:@PORT@/css-bg");margin:48px;cursor:url("http://127.0.0.1:@PORT@/cursor"),auto}
li{list-style-image:url("http://127.0.0.1:@PORT@/list-image")}
</style></head>
<body background="http://127.0.0.1:@PORT@/body-bg">
<h1>FMS-HTML-EGRESS-275 offline probe page</h1>
<ul><li>item</li></ul>
<img src="http://127.0.0.1:@PORT@/img" width="32" height="32" alt="a">
<img src="http://fms275-@GUID@-imghost.example.invalid/x.png" width="32" height="32" alt="b">
<img srcset="http://127.0.0.1:@PORT@/srcset 1x" alt="c">
<picture><source srcset="http://127.0.0.1:@PORT@/picture"><img alt="d"></picture>
<video poster="http://127.0.0.1:@PORT@/poster" src="http://127.0.0.1:@PORT@/video"></video>
<audio src="http://127.0.0.1:@PORT@/audio"></audio>
<iframe src="http://127.0.0.1:@PORT@/iframe"></iframe>
<iframe src="http://fms275-@GUID@-iframehost.example.invalid/"></iframe>
<object data="http://127.0.0.1:@PORT@/object"></object>
<embed src="http://127.0.0.1:@PORT@/embed">
<svg width="32" height="32"><image href="http://127.0.0.1:@PORT@/svg-image" width="32" height="32"/></svg>
<input type="image" src="http://127.0.0.1:@PORT@/input-image">
<form action="http://127.0.0.1:@PORT@/form" method="post"><input name="q"></form>
<a href="http://127.0.0.1:@PORT@/anchor" ping="http://127.0.0.1:@PORT@/ping">link</a>
</body></html>
'@
            $templateD2 = @'
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>FMS refresh probe</title>
<meta http-equiv="refresh" content="0;url=http://127.0.0.1:@PORT@/refresh">
<base href="http://fms275-@GUID@-basehost.example.invalid/dir/"></head>
<body><h1>FMS-HTML-EGRESS-275 refresh probe page</h1>
<img src="relative.png" width="32" height="32" alt="relative to the base">
<a href="relative.html">relative link</a></body></html>
'@
            function New-ProbeFile([string]$Name, [string]$Template, [string]$Guid) {
                $path = Join-Path $probeDir $Name
                [IO.File]::WriteAllText($path, $Template.Replace('@PORT@', "$port").Replace('@GUID@', $Guid), (New-Object Text.UTF8Encoding($false)))
                return $path
            }
            $guidControl = [guid]::NewGuid().ToString('N').Substring(0, 12)
            $guidSubject = [guid]::NewGuid().ToString('N').Substring(0, 12)
            $controlD = New-ProbeFile 'control-egress.html' $templateD $guidControl
            $controlD2 = New-ProbeFile 'control-refresh.html' $templateD2 $guidControl
            $subjectD = New-ProbeFile 'egress.html' $templateD $guidSubject
            $subjectD2 = New-ProbeFile 'refresh.html' $templateD2 $guidSubject

            # the instruments: the listener must see a connection, the DNS cache must see a name made up here
            $instrument = New-Object System.Collections.Generic.List[string]
            $self = New-Object System.Net.Sockets.TcpClient
            $self.Connect([System.Net.IPAddress]::Loopback, $port)
            $selfBytes = [Text.Encoding]::ASCII.GetBytes("GET /instrument HTTP/1.1`r`n`r`n")
            $self.GetStream().Write($selfBytes, 0, $selfBytes.Length)
            $self.Close()
            Start-Sleep -Milliseconds 300
            Receive-Probes $listener $instrument
            [void](Resolve-DnsName "fms275-$guidControl-instrument.example.invalid" -ErrorAction SilentlyContinue)
            $dnsSeesQueries = @(@(Get-ProbeDns $guidControl) | Where-Object { $_ -like '*instrument*' }).Count -gt 0

            # positive control: the same pages, unrestricted, in headless Edge
            $edge = @('C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', 'C:\Program Files\Microsoft\Edge\Application\msedge.exe') |
                Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            $controlHits = New-Object System.Collections.Generic.List[string]
            $controlRefreshHits = New-Object System.Collections.Generic.List[string]
            if ($edge) {
                foreach ($run in @(@($controlD, $controlHits), @($controlD2, $controlRefreshHits))) {
                    $profileDir = Join-Path $WorkDir ('edge-control-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
                    $url = 'file:///' + $run[0].Replace('\', '/')
                    $p = Start-Process -FilePath $edge -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $WorkDir 'edge-control-dom.txt') -ArgumentList @(
                        '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', "--user-data-dir=`"$profileDir`"", '--virtual-time-budget=8000', '--dump-dom', "`"$url`"")
                    if (-not $p.WaitForExit(40000)) { try { $p.Kill($true) } catch { } }
                    Start-Sleep -Milliseconds 500
                    Receive-Probes $listener $run[1]
                    Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue |
                        Where-Object { $_.CommandLine -like "*$profileDir*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                }
            }
            $controlDnsNames = @(Get-ProbeDns $guidControl | Where-Object { $_ -notlike '*instrument*' })
            Write-Host ("  S5 instruments: listener saw the self-test {0}; DNS cache sees a resolved probe name {1}; unrestricted Edge control reached the listener {2}x (page) and {3}x (refresh/base), {4} probe host name(s) in the DNS cache" -f
                ($instrument.Count -gt 0), $dnsSeesQueries, $controlHits.Count, $controlRefreshHits.Count, $controlDnsNames.Count)
            $controlAll = @($controlHits) + @($controlRefreshHits)
            $controlRequests = @($controlAll | Where-Object { $_ -match '^(GET|POST|HEAD|OPTIONS|CONNECT) ' } | ForEach-Object { ($_ -split ' ')[1] } | Sort-Object -Unique)
            Write-Host ("  S5 control: {0} distinct path(s) asked for with a request line, {1} bare connection(s): {2}" -f
                $controlRequests.Count, @($controlAll | Where-Object { $_ -notmatch '^(GET|POST|HEAD|OPTIONS|CONNECT) ' }).Count, ($controlRequests -join ' '))

            # the subject: the viewer, HTML view on, online pages off
            Lend-HtmlOn
            $subjectHits = New-Object System.Collections.Generic.List[string]
            $o = Open-HtmlScene $subjectD $markerD
            $s5Viewer = $o.Process
            if ($o.Ready) { Start-Sleep -Seconds $SettleSeconds }
            Receive-Probes $listener $subjectHits
            $pageRendered = $o.Ready -and (Test-RenderedText (Get-WindowHandle $s5Viewer) $markerD)
            $shot = if ($o.Ready) { Save-WindowShot (Get-WindowHandle $s5Viewer) 's5-egress-page.png' } else { '(viewer not ready)' }
            $ids = @($s5Viewer.Id) + @(Get-WebViewDescendants $s5Viewer.Id | ForEach-Object { [int]$_.ProcessId })
            $remotes = @(Get-NetTCPConnection -OwningProcess $ids -ErrorAction SilentlyContinue |
                Where-Object { $_.RemoteAddress -notin '127.0.0.1', '::1', '0.0.0.0', '::' } |
                ForEach-Object { "$($_.RemoteAddress):$($_.RemotePort)" } | Sort-Object -Unique)
            # the refresh/base page, forwarded into the running viewer
            $refreshHits = New-Object System.Collections.Generic.List[string]
            $refreshShown = $false
            if ($pageRendered) {
                Start-Process -FilePath $ExePath -ArgumentList "`"$subjectD2`"" -WorkingDirectory $exeDir -Wait
                $refreshShown = Wait-Marker $s5Viewer $markerD2 30
                if ($refreshShown) { Start-Sleep -Seconds $SettleSeconds }
                Receive-Probes $listener $refreshHits
            }
            $subjectDns = @(Get-ProbeDns $guidSubject)
            Write-Host ("  S5 subject: page rendered {0}, requests at the listener {1}, probe host names in the DNS cache {2}, refresh/base page shown {3} with {4} request(s), shot {5}" -f
                $pageRendered, $subjectHits.Count, $subjectDns.Count, $refreshShown, $refreshHits.Count, $shot)
            Write-Host ("  S5 informational: remote TCP peers of the viewer and its browser processes: " + $(if ($remotes.Count -eq 0) { 'none' } else { $remotes -join ', ' }))
            Write-Host "  S5 viewer: $(Stop-Viewer $s5Viewer)"
            $s5Viewer = $null

            if (-not $pageRendered) {
                Add-Scene 'S5' 'COULD NOT VERIFY' 'the probe page did not render in the HTML view, so nothing was asked of the network by it'
            } elseif ($subjectHits.Count -gt 0 -or $refreshHits.Count -gt 0 -or ($dnsSeesQueries -and $controlDnsNames.Count -gt 0 -and $subjectDns.Count -gt 0)) {
                $why = @()
                if ($subjectHits.Count -gt 0) { $why += "$($subjectHits.Count) request(s) reached the loopback listener from the page: $((@($subjectHits | Select-Object -First 6)) -join ' | ')" }
                if ($refreshHits.Count -gt 0) { $why += "$($refreshHits.Count) request(s) reached the listener from the refresh/base page: $((@($refreshHits | Select-Object -First 4)) -join ' | ')" }
                if ($subjectDns.Count -gt 0 -and $dnsSeesQueries -and $controlDnsNames.Count -gt 0) { $why += "probe host name(s) in the DNS cache: $($subjectDns -join ', ')" }
                Add-Scene 'S5' 'FAIL' ($why -join '; ')
            } elseif (-not $edge -or $controlHits.Count -eq 0) {
                Add-Scene 'S5' 'COULD NOT VERIFY' 'the listener stayed silent, but the unrestricted control did not reach it either (no Edge, or Edge blocked the loopback probes), so the silence proves nothing'
            } elseif ($instrument.Count -eq 0) {
                Add-Scene 'S5' 'COULD NOT VERIFY' 'the listener did not register its own self-test connection'
            } else {
                $dnsNote = if ($dnsSeesQueries -and $controlDnsNames.Count -gt 0) { 'no probe host name reached the Windows DNS cache (the control did)' }
                           elseif ($dnsSeesQueries) { 'DNS cache not judged: the unrestricted control left no probe host name in it (its resolver does not use the Windows cache)' }
                           else { 'DNS cache not judged: it does not show a resolved probe name' }
                $probeCount = ([regex]::Matches($templateD, '@PORT@|@GUID@')).Count
                Add-Scene 'S5' 'PASS' ("offline: the page ({0} probe URLs) and the refresh/base page reached the loopback listener 0 times where the unrestricted control reached it {1}x and {2}x; {3}" -f $probeCount, $controlHits.Count, $controlRefreshHits.Count, $dnsNote)
            }
        } finally {
            if ($null -ne $s5Viewer) { Write-Host "  S5 viewer (cleanup): $(Stop-Viewer $s5Viewer)" }
            $listener.Dispose()
        }
        Start-Sleep -Seconds 2
    }

    # ----- S6: a leftover profile is swept although the viewer is switched OFF -----------------------
    # The defect S3 found: the sweep of a previous run's profile folders ran only when an HTML page was next
    # opened with the viewer on. Now it runs from the first window on every launch.
    if (Want 'S6') {
        Lend-HtmlOn
        $o = Open-HtmlScene $fixtureA $markerA
        $s6Viewer = $o.Process
        $leftover = @($extra | Where-Object { Test-Path -LiteralPath $_ })
        if (-not $o.Ready -or $leftover.Count -eq 0) {
            Write-Host "  S6 viewer: $(Stop-Viewer $s6Viewer)"
            Add-Scene 'S6' 'COULD NOT VERIFY' 'the HTML view did not come up, so there is no owned folder to leave behind'
        } else {
            try { $s6Viewer.Kill($true) } catch { }
            [void]$s6Viewer.WaitForExit(10000)
            # the browser processes follow their host; the folder stays
            $browsersGone = Wait-Until { @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $u = Get-UserDataDir $_; $u -and ($leftover -contains $u) }).Count -eq 0 } 25 500
            $stillThere = @($leftover | Where-Object { Test-Path -LiteralPath $_ })
            Write-Host ("  S6: viewer killed; browser processes gone {0}; {1} owned folder(s) left behind" -f $browsersGone, $stillThere.Count)
            if (-not $browsersGone -or $stillThere.Count -eq 0) {
                Add-Scene 'S6' 'COULD NOT VERIFY' 'the killed viewer left no owned folder that no browser process held, so the sweep has nothing to show'
            } else {
                Lend-Setting 'HtmlViewerEnabled' '0'
                $s6Viewer = Start-Viewer $fixtureA
                $swept = Wait-Until { (-not (Update-Viewer $s6Viewer)) -or (@($stillThere | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) } 30 500
                $survivors = @($stillThere | Where-Object { Test-Path -LiteralPath $_ })
                $loaderLoaded = $false
                if (Update-Viewer $s6Viewer) {
                    $mods = Get-LoadedModuleNames $s6Viewer
                    $loaderLoaded = ($null -ne $mods) -and [bool]($mods -contains 'WebView2Loader.dll')
                }
                Write-Host "  S6 viewer: $(Stop-Viewer $s6Viewer)"
                if ($survivors.Count -eq 0) {
                    Add-Scene 'S6' 'PASS' ("{0} owned folder(s) left behind by a killed viewer were swept by the next launch with the HTML viewer switched OFF (WebView2Loader.dll loaded: {1})" -f $stillThere.Count, $loaderLoaded)
                } else {
                    Add-Scene 'S6' 'FAIL' "owned folder(s) survived a launch with the HTML viewer off: $($survivors -join ', ')"
                }
            }
        }
        Start-Sleep -Seconds 2
    }

    # ----- S7: paging, files released, delete from the page (H09 form half, H12) ---------------------
    # Keys are posted to the viewer's own window, never typed. While the page has no focus the existing key
    # map applies (HtmlViewOwnsInput); the files of an open page - and the resources it links - are not held.
    if (Want 'S7') {
        Lend-HtmlOn
        Lend-Setting 'ConfirmDelete' 'never'
        Lend-Setting 'DeleteToRecycleBin' '0'
        Lend-Setting 'IncludeDocumentInNavigation' '1'   # off by default: documents are then not paged to
        $eDir = Join-Path $WorkDir 'fixtures\e'
        # the page's own resources sit in a subfolder: next to it they would be pages of the folder (a picture
        # is a file to page to), and Delete would land on them
        New-Item -ItemType Directory -Force -Path (Join-Path $eDir 'res') | Out-Null
        $pic = New-Object System.Drawing.Bitmap 16, 16
        try { $pic.Save((Join-Path $eDir 'res\e2-pic.png'), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $pic.Dispose() }
        [IO.File]::WriteAllText((Join-Path $eDir 'res\e2-style.css'), 'h1{color:#7a1f1f}', (New-Object Text.UTF8Encoding($false)))
        foreach ($n in 1, 2, 3) {
            $extraHead = if ($n -eq 2) { '<link rel="stylesheet" href="res/e2-style.css">' } else { '' }
            $extraBody = if ($n -eq 2) { '<img src="res/e2-pic.png" width="16" height="16" alt="pic">' } else { '' }
            [IO.File]::WriteAllText((Join-Path $eDir "e$n.html"), "<!DOCTYPE html><html><head><meta charset=`"utf-8`"><title>E$n</title>$extraHead</head><body><h1>FMS-HTML-PAGE-E$n marker</h1><p>page $n of 3</p>$extraBody</body></html>", (New-Object Text.UTF8Encoding($false)))
        }
        $e1 = Join-Path $eDir 'e1.html'; $e2 = Join-Path $eDir 'e2.html'
        $o = Open-HtmlScene $e1 'FMS-HTML-PAGE-E1 marker'
        $s7Viewer = $o.Process
        try {
            if (-not $o.Ready) {
                Add-Scene 'S7' 'COULD NOT VERIFY' 'the first page did not come up in the HTML view'
            } else {
                $w = Get-WindowHandle $s7Viewer
                $why = @(); $facts = @()
                $tb = Find-HtmlToolbar $w
                if ($tb -ne $toolbarName) { $why = @("the toolbar is named '$tb', not '$toolbarName': the scene drives the Russian UI"); }
                else {
                    # 1. Space on the canvas (the page has no focus): next file
                    [FmsHtmlScenesNative]::PostKey($w, 0x20)
                    $toE2 = Wait-Marker $s7Viewer 'FMS-HTML-PAGE-E2 marker' 20
                    $facts += "Space -> E2: $toE2"
                    if (-not $toE2) { $why += 'a posted Space did not page to the next page' }
                    # 2. the toolbar's previous/next buttons (UI Automation, no focus needed)
                    $prevOk = Invoke-ToolbarButton $w 'Предыдущий файл'
                    $backE1 = $prevOk -and (Wait-Marker $s7Viewer 'FMS-HTML-PAGE-E1 marker' 20)
                    $facts += "toolbar Previous -> E1: $backE1"
                    if (-not $backE1) { $why += 'the toolbar Previous button did not return to the first page' }
                    $nextOk = Invoke-ToolbarButton $w 'Следующий файл'
                    $againE2 = $nextOk -and (Wait-Marker $s7Viewer 'FMS-HTML-PAGE-E2 marker' 20)
                    $facts += "toolbar Next -> E2: $againE2"
                    if (-not $againE2) { $why += 'the toolbar Next button did not page to the second page' }
                    Start-Sleep -Seconds 2
                    # 3. the shown page and the resources it links are not held open
                    if ($againE2) {
                        foreach ($leaf in 'e2.html', 'res\e2-style.css', 'res\e2-pic.png') {
                            $path = Join-Path $eDir $leaf
                            $held = $null
                            try {
                                $fs = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None'); $fs.Dispose()
                                $moved = "$path.moved"
                                Move-Item -LiteralPath $path -Destination $moved -ErrorAction Stop
                                Move-Item -LiteralPath $moved -Destination $path -ErrorAction Stop
                                $held = $false
                            } catch { $held = $true; $why += "$leaf is held by the open page: $($_.Exception.Message)" }
                            $facts += "$leaf exclusive open and rename: $(-not $held)"
                        }
                    }
                    # 4. Delete on the canvas: the file goes, the viewer shows another page and stays in the HTML view
                    if ($againE2 -and $why.Count -eq 0) {
                        [FmsHtmlScenesNative]::PostKey($w, 0x2E)
                        $gone = Wait-Until { -not (Test-Path -LiteralPath $e2) } 15 300
                        $logFrom = Get-LogSize
                        $other = $gone -and ((Wait-Marker $s7Viewer 'FMS-HTML-PAGE-E3 marker' 40) -or (Wait-Marker $s7Viewer 'FMS-HTML-PAGE-E1 marker' 5))
                        $stillHtml = (Update-Viewer $s7Viewer) -and ($null -ne (Find-HtmlToolbar (Get-WindowHandle $s7Viewer)))
                        if (-not $other -or -not $stillHtml) {
                            Write-Host ("  S7 after the delete: title '{0}', viewer alive {1}, owned folders now {2}" -f $s7Viewer.MainWindowTitle, (Update-Viewer $s7Viewer), (@(Get-OwnedUdfs 'local').Count))
                            foreach ($l in (Get-LogEvidence $logFrom)) { Write-Host "  log: $l" }
                            if (Test-Path -LiteralPath $viewerLog) { Get-Content -LiteralPath $viewerLog -Tail 25 | ForEach-Object { Write-Host "  logtail: $_" } }
                        }
                        $facts += "Delete -> e2.html gone: $gone; another page shown: $other; HTML view still up: $stillHtml"
                        if (-not $gone) { $why += 'a posted Delete did not remove the shown page' }
                        elseif (-not $other) { $why += 'after the delete no other page was shown' }
                        elseif (-not $stillHtml) { $why += 'the viewer left the HTML view or exited after the delete' }
                    }
                }
                $shot = if (Update-Viewer $s7Viewer) { Save-WindowShot (Get-WindowHandle $s7Viewer) 's7-after-delete.png' } else { '(viewer gone)' }
                Write-Host ("  S7 facts: " + ($facts -join '; ') + "; shot $shot")
                if ($why.Count -eq 0) { Add-Scene 'S7' 'PASS' ('canvas keys and toolbar page between html files, the shown page and its css/image are not held open, Delete removed the shown page and the next one came up: ' + ($facts -join '; ')) }
                else { Add-Scene 'S7' 'FAIL' ($why -join '; ') }
            }
        } finally {
            Write-Host "  S7 viewer: $(Stop-Viewer $s7Viewer)"
        }
        Start-Sleep -Seconds 2
    }

    # ----- S8 / S9: zoom and find through the toolbar, on the real Runtime (H10) -----------------------
    # Observed from outside: the page's own heading is in the accessibility tree, and its rectangle moves
    # with the zoom (S8) and with the scroll a find step makes (S9). The match counter is read when UI
    # Automation exposes its text.
    if ((Want 'S8') -or (Want 'S9')) {
        Lend-HtmlOn
        $fDir = Join-Path $WorkDir 'fixtures\f'
        New-Item -ItemType Directory -Force -Path $fDir | Out-Null
        $fFile = Join-Path $fDir 'find.html'
        $fMarker = 'FMS-HTML-FIND-275 heading'
        [IO.File]::WriteAllText($fFile, @"
<!DOCTYPE html><html><head><meta charset="utf-8"><title>FMS find</title><style>body{font:28px "Segoe UI",sans-serif;margin:40px}</style></head>
<body><h1>$fMarker</h1><p>needle one near the top</p>
<div style="height:2600px">spacer</div><p>NEEDLE two far down</p>
<div style="height:2600px">spacer</div><p>needle three further down</p><div style="height:900px">end</div></body></html>
"@, (New-Object Text.UTF8Encoding($false)))
        $o = Open-HtmlScene $fFile $fMarker
        $s8Viewer = $o.Process
        try {
            $w = if ($o.Ready) { Get-WindowHandle $s8Viewer } else { [IntPtr]::Zero }
            $tb = if ($o.Ready) { Find-HtmlToolbar $w } else { $null }
            if (-not $o.Ready) {
                foreach ($id in 'S8', 'S9') { if (Want $id) { Add-Scene $id 'COULD NOT VERIFY' 'the find fixture did not come up in the HTML view' } }
            } elseif ($tb -ne $toolbarName) {
                foreach ($id in 'S8', 'S9') { if (Want $id) { Add-Scene $id 'COULD NOT VERIFY' "the toolbar is named '$tb', not '$toolbarName': the scene drives the Russian UI" } }
            } else {
                $h0 = Get-HeadingRect $w $fMarker
                if (Want 'S8') {
                    $facts = @(); $why = @()
                    [void](Invoke-ToolbarButton $w 'Увеличить'); Start-Sleep -Milliseconds 1500
                    $hIn = Get-HeadingRect $w $fMarker
                    [void](Invoke-ToolbarButton $w 'Сбросить масштаб'); Start-Sleep -Milliseconds 1500
                    $hReset = Get-HeadingRect $w $fMarker
                    [void](Invoke-ToolbarButton $w 'Уменьшить'); Start-Sleep -Milliseconds 1500
                    $hOut = Get-HeadingRect $w $fMarker
                    [void](Invoke-ToolbarButton $w 'Сбросить масштаб'); Start-Sleep -Milliseconds 1000
                    if ($null -eq $h0 -or $null -eq $hIn -or $null -eq $hReset -or $null -eq $hOut) { $why += 'the heading rectangle could not be read through UI Automation' }
                    else {
                        $facts += ("heading height 100 %: {0:N0}, Zoom in: {1:N0}, Reset: {2:N0}, Zoom out: {3:N0}" -f $h0.Height, $hIn.Height, $hReset.Height, $hOut.Height)
                        if ($hIn.Height -le $h0.Height + 2) { $why += 'Zoom in did not enlarge the page' }
                        if ([Math]::Abs($hReset.Height - $h0.Height) -gt 2) { $why += 'the zoom label (Reset) did not return the page to 100 %' }
                        if ($hOut.Height -ge $h0.Height - 2) { $why += 'Zoom out did not shrink the page' }
                    }
                    Write-Host ("  S8 facts: " + ($facts -join '; '))
                    if ($why.Count -eq 0 -and $facts.Count -gt 0) { Add-Scene 'S8' 'PASS' ('toolbar zoom on the real Runtime: ' + ($facts -join '; ')) }
                    elseif ($why.Count -gt 0 -and $facts.Count -eq 0) { Add-Scene 'S8' 'COULD NOT VERIFY' ($why -join '; ') }
                    else { Add-Scene 'S8' 'FAIL' ($why -join '; ') }
                }
                if (Want 'S9') {
                    $facts = @(); $why = @()
                    # the query box: the first element of that name that takes a value
                    $query = $null
                    $root = [System.Windows.Automation.AutomationElement]::FromHandle($w)
                    $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Поиск на странице')
                    foreach ($c in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)) {
                        $vp = $null
                        if ($c.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { $query = $vp; break }
                    }
                    $counter = Find-ByName $w 'Совпадения'
                    if ($null -ne $counter) {
                        $vp = $null
                        $valueText = if ($counter.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { "'$($vp.Current.Value)'" } else { 'no value pattern' }
                        Write-Host "  S9 counter element: name '$($counter.Current.Name)', control type $($counter.Current.ControlType.ProgrammaticName), help text '$($counter.Current.HelpText)', value $valueText"
                    } else { Write-Host '  S9 counter element: not found by its accessible name' }
                    if ($null -eq $query) { $why += 'the find query box is not reachable through UI Automation' }
                    elseif ($null -eq $h0) { $why += 'the heading rectangle could not be read' }
                    else {
                        $query.SetValue('needle')
                        [void](Invoke-ToolbarButton $w 'Следующее совпадение'); Start-Sleep -Milliseconds 1500    # starts the find: match 1
                        $t1 = (Get-HeadingRect $w $fMarker).Top
                        [void](Invoke-ToolbarButton $w 'Следующее совпадение'); Start-Sleep -Milliseconds 1500    # match 2, far below
                        $t2 = (Get-HeadingRect $w $fMarker).Top
                        [void](Invoke-ToolbarButton $w 'Следующее совпадение'); Start-Sleep -Milliseconds 1500    # match 3, further down
                        $t3 = (Get-HeadingRect $w $fMarker).Top
                        [void](Invoke-ToolbarButton $w 'Следующее совпадение'); Start-Sleep -Milliseconds 1500    # wraps to match 1
                        $t4 = (Get-HeadingRect $w $fMarker).Top
                        [void](Invoke-ToolbarButton $w 'Предыдущее совпадение'); Start-Sleep -Milliseconds 1500   # back to match 3
                        $t5 = (Get-HeadingRect $w $fMarker).Top
                        $facts += ("heading top: start {0:N0}, match 1 {1:N0}, match 2 {2:N0}, match 3 {3:N0}, wrap {4:N0}, previous {5:N0}" -f $h0.Top, $t1, $t2, $t3, $t4, $t5)
                        if ($t2 -ge $t1 - 300) { $why += 'Next did not scroll to the second match' }
                        if ($t3 -ge $t2 - 300) { $why += 'Next did not scroll to the third match' }
                        if ([Math]::Abs($t4 - $t1) -gt 100) { $why += 'Next after the last match did not wrap to the first' }
                        if ([Math]::Abs($t5 - $t3) -gt 100) { $why += 'Previous after the wrap did not go to the last match' }
                    }
                    Write-Host ("  S9 facts: " + ($facts -join '; '))
                    if ($why.Count -eq 0) { Add-Scene 'S9' 'PASS' ('find through the toolbar on the real Runtime (3 matches, case-insensitive, wraps both ways): ' + ($facts -join '; ') + '; the counter text is not judged (see the counter line)') }
                    elseif ($null -eq $query) { Add-Scene 'S9' 'COULD NOT VERIFY' ($why -join '; ') }
                    else { Add-Scene 'S9' 'FAIL' ($why -join '; ') }
                }
            }
        } finally {
            Write-Host "  S8/S9 viewer: $(Stop-Viewer $s8Viewer)"
        }
    }
}
catch {
    $harnessError = "$($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    if ($null -ne $viewer) { Write-Host "  viewer (cleanup): $(Stop-Viewer $viewer)" }
    Get-Process -Name FastMediaSorter_LITE -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $ExePath } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    # Folders this run made and the viewer left behind: removed by exact path, GUID-named children of
    # the owned parents only. An empty documents\webview tree this run created goes too.
    foreach ($u in (@($created) + @($extra))) {
        $parent = Split-Path -Parent $u
        $isOwned = ($parent -eq (Join-Path $udfRoot 'local') -or $parent -eq (Join-Path $udfRoot 'online')) -and
            ((Split-Path -Leaf $u) -match '^[0-9a-f]{32}$')
        if ($isOwned -and (Test-Path -LiteralPath $u)) { Remove-Item -LiteralPath $u -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if ($null -ne $rootExistedBefore -and -not $rootExistedBefore) {
        foreach ($d in (Join-Path $udfRoot 'local'), (Join-Path $udfRoot 'online'), $udfRoot, (Split-Path -Parent $udfRoot)) {
            if ((Test-Path -LiteralPath $d) -and @(Get-ChildItem -LiteralPath $d -Force).Count -eq 0) {
                Remove-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue
            }
        }
    }

    # The whole key goes back: delete (so a value the viewer added goes too), import, export again, compare.
    if ($keyExisted) {
        & reg.exe delete $regKey /f 2>&1 | Out-Null
        & reg.exe import $regBackup 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "  The settings import FAILED - the export is at $regBackup" }
        & reg.exe export $regKey $regAfter /y 2>&1 | Out-Null
        if ((Test-Path -LiteralPath $regAfter) -and (Test-Path -LiteralPath $regBackup)) {
            $restoreIdentical = ((Get-FileHash -LiteralPath $regAfter -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $regBackup -Algorithm SHA256).Hash)
        } else { $restoreIdentical = $false }
    } else {
        & reg.exe delete $regKey /f 2>&1 | Out-Null
        $restoreIdentical = -not (Test-Path -LiteralPath $regPath)
    }
    Write-Host ("Settings restored; the re-export is {0} the export taken before the run." -f $(if ($restoreIdentical) { 'byte-identical to' } else { 'DIFFERENT from' }))
}

# --- verdict ---------------------------------------------------------------------------------

$scenes = @($scenes | Sort-Object Id)   # S4 runs before S3 (it needs the viewer S2 left running)
Write-Host ''
Write-Host 'Scene           Result'
foreach ($s in $scenes) { Write-Host ("{0,-3} {1,-16} {2}" -f $s.Id, $s.Result, $s.Reason) }
$summary = ($scenes | ForEach-Object { "$($_.Id) $($_.Result)" }) -join ', '

if ($null -ne $harnessError) { Exit-Verdict $check 2 "the harness failed mid-run: $harnessError; $summary" }
if (-not $restoreIdentical) { Exit-Verdict $check 2 "the settings key was not restored byte for byte (export kept in $WorkDir); $summary" }
$expected = @($allScenes | Where-Object { Want $_ })
$missing = @($expected | Where-Object { $id = $_; -not @($scenes | Where-Object { $_.Id -eq $id }) })
if ($missing.Count -gt 0) { Exit-Verdict $check 2 "scene(s) never reached: $($missing -join ', '); $summary" }
$codes = @($scenes | ForEach-Object { switch ($_.Result) { 'PASS' { 0 } 'FAIL' { 1 } default { 2 } } })
$code = Get-AggregateCode $codes
Exit-Verdict $check $code $summary
