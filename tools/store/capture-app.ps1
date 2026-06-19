<#
    Launches the built app on the sample folder, sizes its main window to a
    target size, and captures a REAL screenshot of the running app via PrintWindow.

    Usage: capture-app.ps1 [-Width 1366] [-Height 768] [-OutName screenshot-EN-1366x768]
#>
param(
    [int]$Width  = 1366,
    [int]$Height = 768,
    [string]$OutName = 'app-1366x768',
    [string]$Image = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$Root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Exe     = Join-Path $Root 'bin\Release\FastMediaSorter_LITE.exe'
$Samples = Join-Path $env:TEMP 'fms-store-samples'
$OutDir  = Join-Path $Root 'assets\store'
if (-not (Test-Path $Exe)) { throw "Missing exe: $Exe" }
if ($Image -eq '') { $Image = Join-Path $Samples '01-sunset-ridge.png' }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
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

# kill any running instance so our launch owns the window
Get-Process FastMediaSorter_LITE -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Write-Host "Launching: $Exe `"$Image`""
Start-Process -FilePath $Exe -ArgumentList "`"$Image`""
Start-Sleep -Seconds 6   # let it load + draw the Ambilight background

# find the main window by title
$script:hwnd = [IntPtr]::Zero
$cb = [Win+EnumProc]{
    param($h,$p)
    if ([Win]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 256
        [void][Win]::GetWindowText($h,$sb,256)
        if ($sb.ToString() -like '*Fast Media Sorter*') { $script:hwnd = $h; return $false }
    }
    return $true
}
[void][Win]::EnumWindows($cb,[IntPtr]::Zero)
if ($script:hwnd -eq [IntPtr]::Zero) { throw "Could not find the app window." }
Write-Host ("Window handle: 0x{0:X}" -f [int64]$script:hwnd)

# size & position, then let it settle / redraw the background bars
[void][Win]::ShowWindow($script:hwnd, 9)          # SW_RESTORE
[void][Win]::MoveWindow($script:hwnd, 60, 60, $Width, $Height, $true)
[void][Win]::SetForegroundWindow($script:hwnd)
Start-Sleep -Seconds 3

$r = New-Object Win+RECT
[void][Win]::GetWindowRect($script:hwnd, [ref]$r)
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
Write-Host "Captured window rect: ${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok  = [Win]::PrintWindow($script:hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()

if (-not $ok) {
    Write-Host "PrintWindow returned false; falling back to screen copy."
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w,$h)))
    $g2.Dispose()
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$out = Join-Path $OutDir "$OutName.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Wrote $out  (${w}x${h})"
