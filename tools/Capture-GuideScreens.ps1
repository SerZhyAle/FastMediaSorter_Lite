<#
.SYNOPSIS
    Captures the current-UI screenshots the task guides embed (DOC-EXTERNAL-QUALITY
    rule 6, ticket 059) into assets/guides/.

.DESCRIPTION
    Launches the built modern viewer (bin\Release\FastMediaSorter_LITE.exe) and, for
    the Share state, the Share Manager (bin\Release\FastMediaSorterCompanion.exe),
    drives them with the keyboard, and grabs real window content with PrintWindow -
    the same core publishing\store\capture-app.ps1 uses for the store shots.

    States, each gated by its own switch so a failure in one does not lose the others:

      -Main      the viewer on a generated sample picture          -> viewer-main.png
      -Settings  the F2 window, one shot per Ctrl+Tab page         -> settings-page-NN.png
                 (a page already captured in this run is not shot again - the cycle
                  repeats after the last tab; curate the keepers afterwards)
      -Ocr       the viewer on a generated text picture after T    -> ocr-overlay.png
                 (needs the local OCR models; a failed shot is inspected, not hidden)
      -Share     the Share Manager main window                     -> share-manager.png

    -All (the default) runs every state.

    Two machines states are borrowed for the run and restored afterwards:

      the interface language - forced to English (the language the guides are written
      in; that is what makes the captures language-neutral), read from and restored in
      HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter (UiLanguage and the
      legacy Is_Russian_Language mirror);

      the destination-folder rows - replaced by sample paths for the run, so no real
      folder of this machine leaks into a published screenshot.

    Windows are matched by the PID we started, never by title alone: the user's own
    Windows Settings window is also titled "Settings" and must never be touched.

    This is a HAND-RUN tool (tools/check-placement.jsonl): it needs a built exe, a real
    desktop and about a minute of on-screen driving, so it never runs inside a suite.

.EXAMPLE
    .\tools\Capture-GuideScreens.ps1
    .\tools\Capture-GuideScreens.ps1 -Settings
#>
[CmdletBinding()]
param(
    [switch]$Main,
    [switch]$Settings,
    [switch]$Ocr,
    [switch]$Share,
    [switch]$All
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Rect APIs must agree with PrintWindow's pixels: the viewer is DPI-aware, so this
# process has to be too, or the client crop lands at the wrong scale.
$DpiPrelude = @'
using System;
using System.Runtime.InteropServices;
public static class DpiAware { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
Add-Type -TypeDefinition $DpiPrelude
[void][DpiAware]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms

if (-not ($Main -or $Settings -or $Ocr -or $Share)) { $All = $true }

$Root      = Split-Path $PSScriptRoot -Parent
$Exe       = Join-Path $Root 'bin\Release\FastMediaSorter_LITE.exe'
$Companion = Join-Path $Root 'bin\Release\FastMediaSorterCompanion.exe'
$OutDir    = Join-Path $Root 'assets\guides'
# Not $env:TEMP: the viewer shows the picture's path in its folder box and status bar,
# and a temp path would print the user's account name into a published screenshot.
$Work      = 'C:\Users\Public\FastMediaSorterGuideShots'
$RegKey    = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

if (-not (Test-Path $Exe)) { throw "Missing exe: $Exe (build first - build.ps1 -SkipInstaller)" }
if ($Share -and -not (Test-Path $Companion)) { Write-Host "Share Manager exe missing, -Share skipped: $Companion"; $Share = $false }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr hdc,uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
}
"@

# The user may be working - keys are POSTED to the window we mean to drive, never
# injected into the desktop: no focus is taken, nothing can land in another app.
function Post-Key([IntPtr]$hwnd, [byte]$vk, [int]$settleMs = 900) {
    $WM_KEYDOWN = 0x0100; $WM_KEYUP = 0x0101
    [void][Win]::PostMessage($hwnd, $WM_KEYDOWN, [IntPtr]$vk, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [void][Win]::PostMessage($hwnd, $WM_KEYUP, [IntPtr]$vk, [IntPtr]::Zero)
    Start-Sleep -Milliseconds $settleMs
}

function Get-ProcessWindows([int]$procId) {
    $list = [System.Collections.Generic.List[IntPtr]]::new()
    $cb = [Win+EnumProc]{
        param($h, $p)
        $wpid = [uint32]0
        [void][Win]::GetWindowThreadProcessId($h, [ref]$wpid)
        if ($wpid -eq $procId -and [Win]::IsWindowVisible($h)) { $list.Add($h) }
        return $true
    }
    [void][Win]::EnumWindows($cb, [IntPtr]::Zero)
    return $list
}

function Find-WindowOfProcess([int]$procId, [IntPtr]$not) {
    foreach ($h in (Get-ProcessWindows $procId)) {
        if ($h -ne $not) {
            $sb = New-Object System.Text.StringBuilder 512
            [void][Win]::GetWindowText($h, $sb, 512)
            if ($sb.ToString().Trim().Length -gt 0) { return $h }
        }
    }
    return [IntPtr]::Zero
}

function Save-Window([IntPtr]$hwnd, [string]$name) {
    [void][Win]::ShowWindow($hwnd, 9)   # SW_RESTORE
    [void][Win]::MoveWindow($hwnd, 100, 100, 1180, 760, $true)
    Start-Sleep -Milliseconds 1500   # let the form relayout and repaint at the new size
    # Read the rect AFTER the settle - the form may clamp the size - and crop to the
    # client area (the window rect carries DWM shadow margins). This process is
    # DPI-aware, so rect pixels and PrintWindow pixels are the same.
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $full = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    $ok = [Win]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    if (-not $ok) {
        $g2 = [System.Drawing.Graphics]::FromImage($full)
        $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
        $g2.Dispose()
    }
    $c = New-Object Win+RECT
    [void][Win]::GetClientRect($hwnd, [ref]$c)
    $pt = New-Object Win+POINT
    [void][Win]::ClientToScreen($hwnd, [ref]$pt)
    $ox = $pt.X - $r.Left; $oy = $pt.Y - $r.Top
    $cw = [Math]::Min($c.Right, $w - $ox); $ch = [Math]::Min($c.Bottom, $h - $oy)
    $bmp = $full.Clone([System.Drawing.Rectangle]::new($ox, $oy, $cw, $ch), $full.PixelFormat)
    $full.Dispose()
    $bmp = Trim-BlackMargin $bmp
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $out = Join-Path $OutDir "$name.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $size = "$($bmp.Width)x$($bmp.Height)"
    $bmp.Dispose()
    Write-Host "  wrote assets/guides/$name.png ($size)"
}

# The app's painted surface can lag the window rect (a DPI-virtualized form resizes in
# its own scale), which leaves pure-black unpainted bands around the content. Trim
# them: the frame of the shot is the first row/column that is not black.
function Trim-BlackMargin([System.Drawing.Bitmap]$bmp) {
    $rect = [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $bytes = New-Object byte[] ($stride * $bmp.Height)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $i = $row + $x * 4
            if ($bytes[$i] -gt 12 -or $bytes[$i + 1] -gt 12 -or $bytes[$i + 2] -gt 12) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return $bmp }
    $crop = [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
    return $bmp.Clone($crop, $bmp.PixelFormat)
}

function New-SamplePicture([string]$path, [switch]$WithText) {
    $bmp = New-Object System.Drawing.Bitmap(1280, 860)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    if ($WithText) {
        $g.Clear([System.Drawing.Color]::White)
        $font = New-Object System.Drawing.Font('Segoe UI', 34)
        $ink = [System.Drawing.Color]::FromArgb(20, 20, 20)
        $y = 60
        foreach ($line in 'Fast Media Sorter reads the text on a picture.',
                          'Press T and the words are recognized offline,',
                          'then the translation is laid over the original.') {
            $g.DrawString($line, $font, (New-Object System.Drawing.SolidBrush($ink)), 60, $y)
            $y += 90
        }
        $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(122, 162, 255))), 60, $y + 40, 420, 120)
    } else {
        # A gradient sky, a sun and hills - a photo-like frame no real photo can leak.
        $rect = New-Object System.Drawing.Rectangle(0, 0, 1280, 860)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
            [System.Drawing.Color]::FromArgb(24, 32, 56), [System.Drawing.Color]::FromArgb(122, 162, 255), 70)
        $g.FillRectangle($brush, $rect)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 209, 102))), 900, 90, 180, 180)
        $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 44, 66))),
            @([System.Drawing.Point]::new(0, 860), [System.Drawing.Point]::new(0, 560), [System.Drawing.Point]::new(420, 720), [System.Drawing.Point]::new(760, 540), [System.Drawing.Point]::new(1280, 700), [System.Drawing.Point]::new(1280, 860)))
    }
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# --- borrowed machine state, saved once and always restored ------------------------
$saveValues = @{}
foreach ($name in 'UiLanguage', 'Is_Russian_Language',
        'MoveOn1', 'MoveOn2', 'MoveOn3', 'MoveOn4', 'MoveOn5', 'MoveOn6', 'MoveOn7', 'MoveOn8', 'MoveOn9') {
    $p = Get-ItemProperty -Path $RegKey -Name $name -ErrorAction SilentlyContinue
    $saveValues[$name] = if ($p) { $p.$name } else { $null }
}

function Borrow-MachineState {
    # English chrome for language-neutral captures, and a sample destination palette
    # where the owner's real folder paths would otherwise be.
    $sampleRows = @{
        'MoveOn1' = 'C:\Users\Public\Pictures\keep'
        'MoveOn2' = 'C:\Users\Public\Pictures\edit'
        'MoveOn3' = 'C:\Users\Public\Pictures\send'
        'MoveOn4' = 'C:\Users\Public\Pictures\archive'
    }
    foreach ($name in $sampleRows.Keys) {
        New-ItemProperty -Path $RegKey -Name $name -Value $sampleRows[$name] -PropertyType String -Force | Out-Null
    }
    New-ItemProperty -Path $RegKey -Name 'UiLanguage' -Value 'en' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $RegKey -Name 'Is_Russian_Language' -Value '0' -PropertyType String -Force | Out-Null
}

function Restore-MachineState {
    foreach ($name in $saveValues.Keys) {
        $v = $saveValues[$name]
        if ($null -eq $v) {
            Remove-ItemProperty -Path $RegKey -Name $name -ErrorAction SilentlyContinue
        } else {
            New-ItemProperty -Path $RegKey -Name $name -Value $v -PropertyType String -Force | Out-Null
        }
    }
}

# --- sample folder -----------------------------------------------------------------
New-Item -ItemType Directory -Path $Work -Force | Out-Null
$picPlain = Join-Path $Work '01-sample-picture.png'
$picText  = Join-Path $Work '02-sample-text.png'
New-SamplePicture $picPlain
New-SamplePicture $picText -WithText

Get-Process FastMediaSorter_LITE, FastMediaSorterCompanion -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Borrow-MachineState

function Start-Viewer([string]$image) {
    # The viewer is single-instance: a second launch forwards its argument to the
    # first process and exits, so every state starts from a fresh process.
    Get-Process FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $script:viewerProc = Start-Process -FilePath $Exe -ArgumentList "`"$image`"" -PassThru
    Start-Sleep -Seconds 7
    $main = (Get-Process -Id $script:viewerProc.Id).MainWindowHandle
    if ($main -eq [IntPtr]::Zero) { throw 'could not find the viewer window' }
    return $main
}

function Shot-Hash([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path)))).Replace('-', '')
    } finally { $sha.Dispose() }
}

try {
    # --- main window ---------------------------------------------------------------
    if ($Main) {
        Write-Host 'Main: viewer on the sample picture'
        $hwnd = Start-Viewer $picPlain
        Save-Window $hwnd 'viewer-main'
    }

    # --- settings pages --------------------------------------------------------------
    if ($Settings) {
        Write-Host 'Settings: F2 posted, one capture per UIA tab page'
        $mainHwnd = Start-Viewer $picPlain
        Post-Key $mainHwnd 0x71 2500   # F2 - the app opens its Settings window itself
        $settingsHwnd = Find-WindowOfProcess $script:viewerProc.Id $mainHwnd
        if ($settingsHwnd -eq [IntPtr]::Zero) { throw 'the Settings window did not open' }
        $rootEl = [System.Windows.Automation.AutomationElement]::FromHandle($settingsHwnd)
        $tabCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $tabs = @($rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond))
        if ($tabs.Count -eq 0) { throw 'no tab pages found in the Settings window' }
        for ($i = 0; $i -lt $tabs.Count; $i++) {
            $sel = $tabs[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $sel.Select()
            Start-Sleep -Milliseconds 1200
            Save-Window $settingsHwnd ('settings-page-{0:d2}' -f ($i + 1))
        }
        Post-Key $settingsHwnd 0x1B 800   # Escape closes the dialog
    }

    # --- OCR overlay -----------------------------------------------------------------
    if ($Ocr) {
        Write-Host 'Ocr: the text picture, then T posted'
        $hwnd = Start-Viewer $picText
        Post-Key $hwnd 0x54 15000   # T: recognize + translate, models load on first use
        Save-Window $hwnd 'ocr-overlay'
    }

    # --- Share Manager -----------------------------------------------------------------
    if ($Share) {
        Write-Host 'Share: the Share Manager window'
        $companionProc = Start-Process -FilePath $Companion -ArgumentList '--show' -PassThru
        Start-Sleep -Seconds 8
        $shareHwnd = Find-WindowOfProcess $companionProc.Id ([IntPtr]::Zero)
        if ($shareHwnd -eq [IntPtr]::Zero) { Write-Host '  no Share Manager window appeared (tray start?) - capture skipped' }
        else { Save-Window $shareHwnd 'share-manager' }
    }
} finally {
    Get-Process FastMediaSorter_LITE, FastMediaSorterCompanion -ErrorAction SilentlyContinue | Stop-Process -Force
    Restore-MachineState
}
Write-Host 'Done. Curate assets/guides/: keep the shots the guides embed, delete the rest.'
