<#
.SYNOPSIS
    Closes the proof 092 owes: replaying a secure container's video for "Repeat" must keep
    the LibVLC art-cache watch, so no cover is re-extracted to disk.

.DESCRIPTION
    SPECIFICATION_SECURE_COVER_RE_EXTRACTED_ON_REPEAT_REOPEN.md section 3 asked for a run
    "with a sealed MKV or MP4 that carries a cover" and 068 section 8 recorded that no
    harness existed for it. This is that harness. It drives the built modern viewer on
    screen - the art extraction happens inside LibVLC's demuxer the moment an input opens,
    so a unit test cannot see it - and judges by numbers:

    Scene A (sensitivity): the same video UNSEALED, cover and artist/album metadata on it,
    played by the viewer. LibVLC must extract the cover into its art cache. If it does not,
    the environment is wrong and any "empty" result about the sealed scene is worthless,
    so the run answers COULD NOT VERIFY, never PASS.

    Scene B (the ticket): the same video sealed into a .fd-sec container. The viewer asks
    for the password, the video plays (the G-04 watch scrubs the first extraction), the
    harness opens the video's right-click menu, invokes "Повторять" - which reopens the
    media through RestartCurrentVideoPreservingPosition, the exact call site the ticket
    fixed - and then checks the art roots: the cover must NOT come back. A final
    "next file" navigation checks the last scrub left nothing behind either.

    Where the cache is: VlcArtCache.ArtRoots - "<exe dir>\portable\art" when a portable
    directory sits beside the executable, otherwise %APPDATA%\vlc\art. The harness creates
    the portable directory beside the built exe, so LibVLC's cache - and its config - go to
    a tree the run owns and the real %APPDATA%\vlc is only watched, never written. That
    watch is also an assertion: after every scene the user's own VLC tree must be unchanged.

    The viewer's registry settings are snapshotted and put back whole; the harness refuses
    to run while any viewer instance is alive, for the same reason OcrOverlayScenes refuses.

    The seal itself is built by a throwaway console project the harness writes into the
    work directory, compiling the same src/Secure sources - and the same test-only
    FdSecTestContainer.Pack writer - that Lite.Tests compiles. It exists only in this work
    directory: no shipped path can reach it (026 section 13 keeps the writer out of src on
    purpose).

.PARAMETER WorkDir
    Where scenes, the seal tool and the transcripts go. Default: %TEMP%\fms-sec-art-scenes.
    Kept after the run either way - it is the run's evidence.

.PARAMETER Ffmpeg
    Full path to ffmpeg.exe. Default: searched on PATH, then the Virtual Desktop bundled
    copy, then the winget ffmpeg packages.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = the sealed scene stayed clean and the sensitivity scene
    proved extraction works. 1 = a cover reached an art root (the defect the ticket is
    about), or a scene's assertion failed outright. 2 = could not verify: a missing exe or
    tool, a viewer already running, the password dialog or the video menu never came up.
    The last line is the verdict, `SecureRepeatArtScenes: <WORD> (..)`. Run it by hand.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-sec-art-scenes'),
    [string]$Ffmpeg = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace FmsHarness -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, System.UIntPtr dwExtraInfo);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetProcessDPIAware();
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
'@

# UIA hands out PHYSICAL screen coordinates; a DPI-unaware PowerShell would scale every
# cursor move and blit into its own virtualised space, and every click would land beside
# the element it aimed at. Take physical pixels before anything else draws.
[FmsHarness.Native]::SetProcessDPIAware() | Out-Null

                       # ..\Integration -> ..\tests -> the repository root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe      = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe'
$regPath  = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'

. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

# An unexpected terminating error must not surface as exit 1, which reads as a failed check.
# The finally blocks below still run first.
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'SecureRepeatArtScenes' 2 'the harness itself failed'
}

# The strings the UI will show, taken from the shipped tables (Localization.Menus.vb,
# Localization.Secure.vb) rather than guessed. This machine runs Russian; the sets cover
# every language the viewer ships, so the script survives a language change.
$repeatItemNames = @('Повторять', 'Repeat', 'Повторювати', 'Wiederholen', 'Ripeti', 'Repetir',
                     'Répéter', 'تكرار', 'दोहराएँ', 'পুনরাবৃত্তি', 'دہرائیں', '循环')
$repeatOnNames   = @('Повтор включён', 'Repeat on', 'Повтор увімкнено', 'Wiederholung ein',
                     'Ripetizione attiva', 'Repetición activada', 'Répétition activée',
                     'Repetição ligada', '循环已开启')

$credential = 'fms092-ArtProbe'          # SendKeys-safe: letters, digits, hyphen
$artist     = 'FMS092 Artist'
$album      = 'Repeat Cover Probe'

# --- tools --------------------------------------------------------------------

function Find-Ffmpeg {
    if ($Ffmpeg -and (Test-Path -LiteralPath $Ffmpeg)) { return $Ffmpeg }
    $onPath = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($known in @('C:\Program Files\Virtual Desktop Streamer\ffmpeg.exe')) {
        if (Test-Path -LiteralPath $known) { return $known }
    }
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $packages) {
        $hit = Get-ChildItem $packages -Directory -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -match '^(Gyan|yt-dlp)\.FFmpeg' } |
               ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return ''
}

function Find-Dotnet {
    $onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { return 'C:\Program Files\dotnet\dotnet.exe' }
    return ''
}

# --- art roots ----------------------------------------------------------------
# VlcArtCache.ArtRoots, mirrored for the assertions: both places LibVLC may write, plus
# the two parent trees that get listed before and after every scene.

$exeDir      = Split-Path -Parent $exe
$portableDir = Join-Path $exeDir 'portable'
$appdataVlc  = Join-Path $env:APPDATA 'vlc'

function Get-TreeListing([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName.Substring($Root.Length).TrimStart('\'), $_.Length, $_.LastWriteTimeUtc.Ticks }
}

# --- the viewer is mine alone -------------------------------------------------

if (-not (Test-Path -LiteralPath $exe)) {
    Exit-Verdict 'SecureRepeatArtScenes' 2 "no modern viewer at $exe - run build.ps1 first"
}
$ffmpeg = Find-Ffmpeg
if (-not $ffmpeg) { Exit-Verdict 'SecureRepeatArtScenes' 2 'no ffmpeg.exe found - pass -Ffmpeg' }
$dotnet = Find-Dotnet
if (-not $dotnet) { Exit-Verdict 'SecureRepeatArtScenes' 2 'no dotnet SDK found - the seal tool needs one' }

$running = Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue
if ($running) {
    Exit-Verdict 'SecureRepeatArtScenes' 2 'a viewer is already running - stop it first, or its settings and environment win'
}
try {
    $probe = [System.Threading.Mutex]::OpenExisting('FastMediaSorterSingleInstanceMutex')
    $probe.Dispose()
    Exit-Verdict 'SecureRepeatArtScenes' 2 'the viewer single-instance mutex exists while no process matches - a foreign or elevated instance is alive'
} catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # nothing holds it - the run owns the viewer
}

$exeVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
Write-Subject 'SecureRepeatArtScenes' "$exe ($exeVersion), ffmpeg $ffmpeg, scenes in $WorkDir"

# --- scenes -------------------------------------------------------------------

$scenesDir = Join-Path $WorkDir 'scenes'
$plainMp4  = Join-Path $scenesDir 'cover-video.mp4'
$coverJpg  = Join-Path $scenesDir 'cover.jpg'
$sealedDir = Join-Path $WorkDir 'sealed'
$sealedFd  = Join-Path $sealedDir 'video.fd-sec'

function New-Scenes {
    New-Item -ItemType Directory -Force -Path $scenesDir | Out-Null

    # The cover: unmistakable on purpose, so a leaked art.jpg is recognisable as THIS run's.
    $bmp = New-Object System.Drawing.Bitmap 480, 480
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(126, 24, 96))
    $g.FillEllipse([System.Drawing.Brushes]::Gold, 90, 90, 300, 300)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = New-Object System.Drawing.Font 'Arial', 40, ([System.Drawing.FontStyle]::Bold)
    $g.DrawString('092', $font, [System.Drawing.Brushes]::Black, 170, 200)
    $g.Dispose(); $font.Dispose()
    $bmp.Save($coverJpg, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bmp.Dispose()

    # A minute of picture, so nothing "ends" while the harness is still driving. The
    # artist and album are the sealed names LibVLC would turn into directory names.
    & $ffmpeg -y -hide_banner -loglevel error `
        -f lavfi -i 'testsrc2=duration=60:size=320x240:rate=8' `
        -c:v libx264 -preset ultrafast -crf 34 -pix_fmt yuv420p `
        -metadata "artist=$artist" -metadata "album=$album" `
        $plainMp4
    if ($LASTEXITCODE -ne 0) {
        # no libx264 in this ffmpeg - the built-in mpeg4 encoder plays just as well here
        & $ffmpeg -y -hide_banner -loglevel error `
            -f lavfi -i 'testsrc2=duration=60:size=320x240:rate=8' `
            -c:v mpeg4 -q:v 6 `
            -metadata "artist=$artist" -metadata "album=$album" `
            $plainMp4
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed building the video (exit $LASTEXITCODE)" }
    }

    & $ffmpeg -y -hide_banner -loglevel error `
        -i $plainMp4 -i $coverJpg `
        -map 0:v -map 1:v -c copy -c:v:1 mjpeg -disposition:v:1 attached_pic `
        -metadata "artist=$artist" -metadata "album=$album" `
        $plainMp4.Replace('.mp4', '.cover.mp4')
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed attaching the cover (exit $LASTEXITCODE)" }
    Move-Item -Force ($plainMp4.Replace('.mp4', '.cover.mp4')) $plainMp4
}

# --- the seal tool -------------------------------------------------------------

$sealToolDir = Join-Path $WorkDir 'seal-tool'

function New-SealTool {
    New-Item -ItemType Directory -Force -Path $sealToolDir | Out-Null
    Copy-Item (Join-Path $repoRoot 'tests\Lite.Tests\FdSecTestContainer.vb') $sealToolDir -Force

    # The same sources Lite.Tests links for src/Secure, by absolute path: the tool is
    # written into the work directory and compiles the repo's own bytes.
    $secure = Join-Path $repoRoot 'src\Secure'
    $compile = (@('FdSecOutcome.vb', 'FdSecPrimitives.vb', 'FdSecKdf.vb', 'FdSecHead.vb') |
        ForEach-Object { "    <Compile Include=""$(Join-Path $secure $_)"" />" }) -join "`r`n"

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>fmsl</RootNamespace>
    <OptionExplicit>On</OptionExplicit>
    <OptionStrict>On</OptionStrict>
    <OptionInfer>On</OptionInfer>
    <OptionCompare>Binary</OptionCompare>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BouncyCastle.Cryptography" Version="2.6.2" />
$compile
    <Compile Include="FdSecTestContainer.vb" />
    <Compile Include="Program.vb" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $sealToolDir 'SealTool.vbproj') -Encoding UTF8

    @"
Option Strict On

Module Program
    ' args: <content> <sealed name> <credential> <output>
    Sub Main(args As String())
        Dim content = IO.File.ReadAllBytes(args(0))
        ' The SHIPPED V1 profile, not the suite's fast one: the viewer opens with
        ' OrV1(Nothing) - always V1, the format records no profile - so the credential
        ' wrap only opens over a V1 seal. It costs one 64 MiB Argon2id here and one in
        ' the viewer; the session key cache absorbs every later open.
        Dim packed = FdSecTestContainer.Pack(content, args(1), args(2), FdSecProfile.V1)
        IO.File.WriteAllBytes(args(3), packed)
    End Sub
End Module
"@ | Set-Content -LiteralPath (Join-Path $sealToolDir 'Program.vb') -Encoding UTF8
}

function Invoke-Seal {
    New-Item -ItemType Directory -Force -Path $sealedDir | Out-Null
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    & $dotnet run --project $sealToolDir -c Release -- $plainMp4 'cover-video.mp4' $credential $sealedFd
    if ($LASTEXITCODE -ne 0) { throw "the seal tool failed (exit $LASTEXITCODE)" }
    if (-not (Test-Path -LiteralPath $sealedFd) -or (Get-Item -LiteralPath $sealedFd).Length -lt 1KB) {
        throw 'the sealed container came out empty'
    }
}

# --- driving the viewer --------------------------------------------------------

function Wait-True([int]$Seconds, [scriptblock]$Condition) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-PidWindows([int]$ProcId) {
    $pids = @($ProcId)
    $procs = Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue
    if ($procs) { $pids += ($procs | ForEach-Object { $_.Id }) }
    $pids = $pids | Select-Object -Unique

    $allWins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) | ForEach-Object { $_ })
    return @($allWins | Where-Object { $pids -contains $_.Current.ProcessId })
}

function Get-MainWindow([int]$ProcId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Main_Form')
    $main = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
    if ($main) { return $main }

    $wins = @(Get-PidWindows $ProcId)
    $main = $wins | Where-Object { $_.Current.AutomationId -eq 'Main_Form' -or $_.Current.Name -match 'Fast Media Sorter' } | Select-Object -First 1
    if ($main) { return $main }
    return ($wins | Where-Object { $_.Current.BoundingRectangle.Width -gt 200 -and $_.Current.BoundingRectangle.Height -gt 200 } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending |
        Select-Object -First 1)
}

function Find-PasswordField([int]$ProcId) {
    foreach ($w in (Get-PidWindows $ProcId)) {
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'secure_Password')),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
        $field = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($field) { return @{ Window = $w; Field = $field } }
    }
    return $null
}

function Find-VlcSupportPrompt([int]$ProcId) {
    # "Поддержка VLC ещё не установлена..." - the runtime-offer dialog. Its presence means
    # the playback this scene is about could never have started; say so instead of
    # driving a dead UI.
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Поддержка VLC ещё не установлена. Скачать и установить её сейчас?')
    foreach ($w in (Get-PidWindows $ProcId)) {
        $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($hit) { return $true }
    }
    return $false
}

function Submit-Password($Dialog, [string]$Credential) {
    # The dialog does not necessarily hold the desktop foreground when it opens (the
    # foreground lock lets a background process show a modal that never activates), so
    # SendKeys alone can type into whoever IS foreground. ValuePattern needs no keyboard
    # and no focus; the OK button is invoked by id the same way.
    [FmsHarness.Native]::SetForegroundWindow($Dialog.Window.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 250
    $pattern = $null
    if ($Dialog.Field.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        $pattern.SetValue($Credential)
    } else {
        $Dialog.Field.SetFocus()
        Start-Sleep -Milliseconds 200
        [System.Windows.Forms.SendKeys]::SendWait($Credential)
    }
    Start-Sleep -Milliseconds 250
    try {
        $held = $Dialog.Field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($held -ne $Credential) { Write-Host "  note: the field reads back $($held.Length) chars" }
    } catch {
        Write-Host '  note: the field value is not readable back (masked)'
    }
    $btnCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'secure_Ok')),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $btn = $Dialog.Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    $invoke = $null
    if ($btn -and $btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
        $invoke.Invoke()
    } else {
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    }
}

function Get-DialogWarning($Dialog) {
    try {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'secure_Warning')
        $label = $Dialog.Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($label) { return $label.Current.Name }
    } catch { }
    return ''
}

function Show-ViewerLogTail {
    $log = Join-Path $exeDir 'current.log'
    if (Test-Path -LiteralPath $log) {
        Write-Host '  viewer log tail:'
        Get-Content -LiteralPath $log -Tail 6 | ForEach-Object { Write-Host "    $_" }
    }
}

function Save-Screenshot([System.Windows.Automation.AutomationElement]$Scope, [string]$Name) {
    # eyes on the driven state: the run keeps a frame per step in the work directory
    try {
        $r = $Scope.Current.BoundingRectangle
        $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $g.Dispose()
        $bmp.Save((Join-Path $WorkDir $Name), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "  frame kept: $Name"
    } catch {
        Write-Host "  screenshot failed: $($_.Exception.Message)"
    }
}

function Find-TextByName([System.Windows.Automation.AutomationElement]$Scope, [string[]]$Names) {
    foreach ($n in $Names) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $n)
        $hit = $Scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($hit) { return $n }
    }
    return ''
}

function Start-Viewer([string]$File) {
    Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $proc = Start-Process -FilePath $exe -ArgumentList "`"$File`"" -PassThru
    $mw = $null
    if (-not (Wait-True 20 {
            $proc.Refresh()
            $mw = Get-MainWindow $proc.Id
            ($proc.HasExited -eq $false) -and ($null -ne $mw)
        })) {
        $proc.Refresh()
        if ($proc.HasExited) { throw "the viewer exited at once (code $($proc.ExitCode))" }
        throw 'the viewer never showed a window'
    }
    $proc.Refresh()
    $mw = Get-MainWindow $proc.Id
    if ($mw) {
        [FmsHarness.Native]::SetWindowPos([IntPtr]$mw.Current.NativeWindowHandle, [IntPtr]::Zero, 200, 200, 0, 0, 0x0001 -bor 0x0004) | Out-Null
        [FmsHarness.Native]::SetForegroundWindow([IntPtr]$mw.Current.NativeWindowHandle) | Out-Null
    }
    return $proc
}

function Stop-Viewer([System.Diagnostics.Process]$Proc) {
    Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($Proc -and -not $Proc.HasExited) { try { $Proc.Kill() } catch { } }
    Wait-True 8 { -not (Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86 -ErrorAction SilentlyContinue) } | Out-Null
    Start-Sleep -Seconds 2
}

function Get-ClickTarget([System.Windows.Automation.AutomationElement]$MainWindow) {
    # The video surface sits at Picture_Box_1's place inside panel_Media
    # (Main_Form.VideoPlayer.vb) - aim there when the tree exposes it, at the window
    # centre otherwise.
    foreach ($id in @('panel_Media', 'Picture_Box_1')) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $hit = $MainWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($hit) { return $hit }
    }
    return $MainWindow
}

function Send-ClickAt([int]$X, [int]$Y, [bool]$Right) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($X, $Y)
    Start-Sleep -Milliseconds 150
    $down = 0x0002; $up = 0x0004                 # left
    if ($Right) { $down = 0x0008; $up = 0x0010 } # right
    [FmsHarness.Native]::mouse_event($down, 0, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [FmsHarness.Native]::mouse_event($up, 0, 0, 0, [System.UIntPtr]::Zero)
}

function Send-Click([System.Windows.Automation.AutomationElement]$Target, [bool]$Right) {
    $r = $Target.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2)
    $y = [int]($r.Y + $r.Height / 2)
    try {
        $under = [System.Windows.Automation.AutomationElement]::FromPoint(
            (New-Object System.Windows.Point($x, $y)))
        Write-Host ("  click at {0}x{1} lands on: {2} / {3}" -f $x, $y, $under.Current.ControlType.ProgrammaticName, $under.Current.Name)
    } catch {
        Write-Host "  click at ${x}x${y} (nothing readable under the cursor)"
    }
    Send-ClickAt $x $y $Right
}

function Show-PidWindows([int]$ProcId) {
    $lines = Get-PidWindows $ProcId | ForEach-Object {
        '{0} | {1} | {2}x{3}' -f $_.Current.Name, $_.Current.ClassName, $_.Current.BoundingRectangle.Width, $_.Current.BoundingRectangle.Height
    }
    Write-Host ("  windows of the viewer: " + ($lines -join ' ;; '))
}

function Invoke-VideoMenuItem([int]$ProcId, [string[]]$Names) {
    # The open strip is a window of the same process and its items are readable, so the
    # item is reached by rect with the mouse, then by InvokePattern, then by keys - each
    # step checked against the menu actually closing, because a click beside an open
    # strip only dismisses it. Returns when the strip is gone.
    $deliver = {
        param([System.Windows.Automation.AutomationElement[]]$Items, [string[]]$Labels, [int]$Mode)
        # 0 = mouse at the item rectangle, 1 = InvokePattern, 2 = keyboard fallback
        for ($i = 0; $i -lt $Items.Count; $i++) {
            if (-not $Labels[$i] -or ($Names -notcontains $Labels[$i])) { continue }
            if ($Mode -eq 0) {
                $r = $Items[$i].Current.BoundingRectangle
                $x = [int]($r.X + $r.Width / 2)
                $y = [int]($r.Y + $r.Height / 2)
                Write-Host "  clicking item '$($Labels[$i])' at ${x}x${y}"
                Send-ClickAt $x $y $false
            } elseif ($Mode -eq 1) {
                $invoke = $null
                if ($Items[$i].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
                    Write-Host "  invoking item '$($Labels[$i])' through UIA"
                    $invoke.Invoke()
                } else {
                    return $false
                }
            } else {
                Write-Host '  sending keys to whatever holds them'
                [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{ENTER}')
            }
            return $true
        }
        return $false
    }

    for ($mode = 0; $mode -le 2; $mode++) {
        foreach ($w in (Get-PidWindows $ProcId)) {
            $menuCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Menu)
            $menus = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond) | ForEach-Object { $_ })
            foreach ($menu in $menus) {
                $items = @($menu.FindAll([System.Windows.Automation.TreeScope]::Children,
                    [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_ })
                $labels = @($items | ForEach-Object { $_.Current.Name })
                Write-Host ("  menu: " + ($labels -join ' | '))
                if (& $deliver $items $labels $mode) {
                    Start-Sleep -Milliseconds 500
                    # delivered: the strip must be gone - a click that missed only dismissed it
                    $still = $false
                    foreach ($w2 in (Get-PidWindows $ProcId)) {
                        $hit = $w2.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
                        if ($hit) { $still = $true; break }
                    }
                    if (-not $still) { return $true }
                    Write-Host '  the strip is still up - the delivery did not land'
                }
            }
        }
    }
    return $false
}

# --- registry snapshot ---------------------------------------------------------

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

# --- run -----------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
if (-not (Test-Path -LiteralPath $plainMp4) -or -not (Test-Path -LiteralPath $coverJpg)) { New-Scenes }
New-SealTool
Invoke-Seal

$appdataBefore = Get-TreeListing $appdataVlc
$portableExistedBefore = Test-Path -LiteralPath $portableDir
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null

# The ticket's scene is "repeat was off, the toggle turns it on". The user's own
# IsVideoLoop would start the first play with the repeat option already baked, the menu
# item already checked, and the toggle would run the wrong way - so the run starts from
# off; the registry snapshot puts the user's value back in finally.
if ($regExisted) { Set-ItemProperty -Path $regPath -Name 'IsVideoLoop' -Value '0' }

$logMark = 0
$logPath = Join-Path $exeDir 'current.log'
if (Test-Path -LiteralPath $logPath) { $logMark = @(Get-Content -LiteralPath $logPath).Count }

$viewer = $null
$code = 2
$detail = ''
try {
    # ---- scene A: the unsealed cover-carrying video must produce art --------------
    Write-Host 'Scene A: plain cover-carrying video - extraction must be observed.'
    $viewer = Start-Viewer $plainMp4
    Start-Sleep -Seconds 8
    $plainArt = @(Get-TreeListing (Join-Path $portableDir 'art'))
    if ($plainArt.Count -eq 0) {
        $plainArt = @(Get-TreeListing (Join-Path $appdataVlc 'art'))
    }
    Stop-Viewer $viewer
    $viewer = $null
    if ($plainArt.Count -eq 0) {
        Exit-Verdict 'SecureRepeatArtScenes' 2 'the plain video produced no art: extraction was not observed, so the sealed scene could not mean anything'
    }
    Write-Host ("  extraction observed: " + ($plainArt -join '; '))

    # back to a clean slate for the sealed scene
    if (Test-Path -LiteralPath (Join-Path $portableDir 'art')) {
        Remove-Item -LiteralPath (Join-Path $portableDir 'art') -Recurse -Force
    }
    $diffA = @(Compare-Object $appdataBefore (Get-TreeListing $appdataVlc))
    if ($diffA.Count -gt 0) {
        $code = 1
        $detail = 'the plain scene wrote outside the portable tree: ' + (($diffA | ForEach-Object { $_.InputObject }) -join '; ')
        throw $detail
    }

    # ---- scene B: the sealed video, Repeat toggled, art must stay away ------------
    # A concurrent build wipes bin\ModernPublish before republishing (build.ps1); a run
    # that starts inside that window finds no libvlc and the viewer offers to install
    # support instead of playing. Detect the missing tree rather than drive a dead UI.
    if (-not (Test-Path -LiteralPath (Join-Path $exeDir 'libvlc\win-x64\libvlc.dll'))) {
        Exit-Verdict 'SecureRepeatArtScenes' 2 "libvlc is missing from $exeDir - a build is rewriting the tree underneath this run; retry when it settles"
    }
    Write-Host 'Scene B: sealed video - password, playback, "Повторять", art must stay empty.'
    $viewer = Start-Viewer $sealedFd

    if (-not (Wait-True 8 { $null -ne (Find-PasswordField $viewer.Id) })) {
        # the auto-prompt only fires on a settled, active window - the placeholder's own
        # click gesture always asks (SecureUnlockCurrent)
        $mw = Get-MainWindow $viewer.Id
        if ($mw) { Send-Click $mw $false }
        if (-not (Wait-True 12 { $null -ne (Find-PasswordField $viewer.Id) })) {
            throw 'the password dialog never appeared'
        }
    }
    $dialog = Find-PasswordField $viewer.Id
    Submit-Password $dialog $credential

    # V1 KDF (64 MiB Argon2id, three passes) then the open - the dialog must go away.
    if (-not (Wait-True 15 { $null -eq (Find-PasswordField $viewer.Id) })) {
        $warning = Get-DialogWarning (Find-PasswordField $viewer.Id)
        Show-ViewerLogTail
        throw "the password dialog stayed up after submit$(if ($warning) { ': ' + $warning })"
    }

    if (-not (Wait-True 8 { $null -ne (Get-MainWindow $viewer.Id) })) {
        throw 'the viewer window is gone after the unlock'
    }
    $mainWindow = Get-MainWindow $viewer.Id
    $condPane = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Pane)
    function Get-ClockPane([System.Windows.Automation.AutomationElement]$Scope) {
        $win = Get-MainWindow $viewer.Id
        if (-not $win) { return '' }
        foreach ($pane in @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condPane) | ForEach-Object { $_ })) {
            if ($pane.Current.Name -match '^\d\d:\d\d / \d\d:\d\d$') { return $pane.Current.Name }
        }
        return ''
    }

    # The clock must tick: a covered or dead video surface would make every later step theatre.
    $clock1 = ''
    $clock2 = ''
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        $c = Get-ClockPane $mainWindow
        if ($c) {
            if (-not $clock1) {
                $clock1 = $c
            } elseif ($c -ne $clock1) {
                $clock2 = $c
                break
            }
        }
        Start-Sleep -Milliseconds 400
    }

    try {
        if (-not $clock1 -or -not $clock2 -or $clock1 -eq $clock2) {
            throw "playback is not running (clock '$clock1' -> '$clock2') - is something covering or stopping the video?"
        }
    } catch {
        Write-Host ("  clock probe: " + $_.InvocationInfo.PositionMessage)
        throw
    }
    Write-Host "  playback confirmed: $clock1 -> $clock2"

    Start-Sleep -Seconds 3   # open, first play, first scrub
    if ($null -ne (Find-PasswordField $viewer.Id)) {
        throw 'the password dialog is back - the credential did not open the container'
    }
    if (Find-VlcSupportPrompt $viewer.Id) {
        throw 'the viewer is offering to install VLC support - the runtime probe failed this session, playback never started'
    }

    $preToggle = @(Get-TreeListing (Join-Path $portableDir 'art'))
    if ($preToggle.Count -ne 0) {
        $code = 1
        $detail = 'art present BEFORE the repeat toggle (the first play path, not the ticket''s): ' + ($preToggle -join '; ')
        throw $detail
    }

    # open the menu, deliver the item, confirm through the status line - the whole cycle
    # up to three times, because a menu click shares the desktop with whatever else is
    # going on and one delivery in three can miss
    $on = ''
    for ($attempt = 1; $attempt -le 3 -and -not $on; $attempt++) {
        [FmsHarness.Native]::SetForegroundWindow($mainWindow.Current.NativeWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
        if (Find-VlcSupportPrompt $viewer.Id) {
            throw 'the VLC support offer appeared after playback started - it owns the UI thread, the menu cannot open'
        }
        if ($attempt -gt 1) { Write-Host "  toggle attempt $attempt" }
        Save-Screenshot $mainWindow 'b1-playback.png'
        Send-Click (Get-ClickTarget $mainWindow) $true
        Start-Sleep -Milliseconds 900
        Show-PidWindows $viewer.Id
        Save-Screenshot $mainWindow 'b2-menu.png'

        Invoke-VideoMenuItem $viewer.Id $repeatItemNames | Out-Null

        # the status line may lag the menu closing by a frame - poll rather than snapshot
        for ($i = 0; $i -lt 12 -and -not $on; $i++) {
            Start-Sleep -Milliseconds 250
            $on = Find-TextByName $mainWindow $repeatOnNames
        }
    }
    Save-Screenshot $mainWindow 'b3-after-item-click.png'
    if (-not $on) {
        # what IS on screen, for the run record - the status line may expose under a
        # name this probe does not guess
        $named = @($mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_ } |
            Where-Object { $_.Current.Name } | Select-Object -First 40 |
            ForEach-Object { '{0}:{1}' -f $_.Current.ControlType.ProgrammaticName, $_.Current.Name })
        Write-Host ("  named elements: " + ($named -join ' | '))
        Show-ViewerLogTail
        throw 'could not confirm the repeat toggle ran (no "repeat on" status text) - an empty result would not mean anything'
    }
    Write-Host "  repeat confirmed on: $on"

    # the reopen the toggle ordered must not have failed: that failure is logged and
    # nothing else (AppFileLogger, Main_Form.VideoMenu.vb)
    $reopenFailure = @(Get-Content -LiteralPath $logPath | Select-Object -Skip $logMark |
        Where-Object { $_ -match 'reopening the video to apply Repeat failed' })
    if ($reopenFailure.Count -gt 0) {
        $code = 1
        $detail = 'the reopen behind the repeat toggle failed: ' + ($reopenFailure -join '; ')
        throw $detail
    }

    Start-Sleep -Seconds 2
    $afterToggle = @(Get-TreeListing (Join-Path $portableDir 'art'))
    if ($afterToggle.Count -ne 0) {
        $code = 1
        $detail = 'the reopened media re-extracted the cover - the leak ticket 092 closes: ' + ($afterToggle -join '; ')
        throw $detail
    }

    # navigate away (wraps to the same file; the credential is remembered for the session)
    [System.Windows.Forms.SendKeys]::SendWait(' ')
    Start-Sleep -Seconds 3
    if ($null -ne (Find-PasswordField $viewer.Id)) {
        throw 'the password dialog came back on navigation'
    }
    $afterNavigation = @(Get-TreeListing (Join-Path $portableDir 'art'))
    if ($afterNavigation.Count -ne 0) {
        $code = 1
        $detail = 'art appeared after navigating away: ' + ($afterNavigation -join '; ')
        throw $detail
    }

    $diffB = @(Compare-Object $appdataBefore (Get-TreeListing $appdataVlc))
    if ($diffB.Count -gt 0) {
        $code = 1
        $detail = 'the sealed scene wrote outside the portable tree: ' + (($diffB | ForEach-Object { $_.InputObject }) -join '; ')
        throw $detail
    }

    $code = 0
    $detail = 'cover-carrying sealed video: playback, repeat toggle and navigation left the art cache empty (scene A proved extraction works)'
} catch {
    if ($code -ne 1) { $detail = $_.Exception.Message }
    Write-Host "  scene step failed: $detail"
    Write-Host ($_.InvocationInfo.PositionMessage)
    Write-Host ($_.ScriptStackTrace)
    Show-ViewerLogTail
} finally {
    Stop-Viewer $viewer
    Restore-Registry
    if (-not $portableExistedBefore -and (Test-Path -LiteralPath $portableDir)) {
        Remove-Item -LiteralPath $portableDir -Recurse -Force
    }
}

Exit-Verdict 'SecureRepeatArtScenes' $code $detail
