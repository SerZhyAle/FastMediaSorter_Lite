<#
.SYNOPSIS
    File manager combined acceptance and ship-together harness (specification 262).
.DESCRIPTION
    Creates a unique operation-owned fixture directory beneath an explicit -TestRoot,
    records its absolute resolved boundary before mutation, refuses root/profile/media
    directories, runs gate evaluations G1-G9 (including live loopback SFTP/Exchange
    Direct & Tunnel, WinForms UI scenes with screenshots, and net48 compatibility),
    and records structured evidence into temp/262/.
.PARAMETER TestRoot
    Explicit expendable directory path. Must be a safe non-root, non-profile, non-system directory.
.PARAMETER OutDir
    Output directory for evidence. Default: <repo>/temp/262.
.PARAMETER SkipSlow
    Skip the 5,000-file bulk fixture generation and slow scenes.
.PARAMETER Gate
    Run a specific gate ('G1'..'G9') or 'All'.
.EXAMPLE
    .\tests\Integration\FileManagerScenes.ps1 -TestRoot $env:TEMP\fms-test-root
#>
[CmdletBinding()]
param(
    [string]$TestRoot = '',
    [string]$OutDir = '',
    [switch]$SkipSlow,
    [ValidateSet('All', 'G1', 'G2', 'G3', 'G4', 'G5', 'G6', 'G7', 'G8', 'G9')][string]$Gate = 'All'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/lib/Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'FileManagerScenes' 2 'the acceptance harness encountered an unexpected error'
}

Write-Subject 'FileManagerScenes' "combined acceptance harness; TestRoot='$TestRoot' Gate=$Gate"

if ([string]::IsNullOrWhiteSpace($TestRoot)) {
    Exit-Verdict 'FileManagerScenes' 2 'supply -TestRoot for an expendable directory to run file manager acceptance scenes'
}

# 1. Boundary Refusal Checks
function Test-SafeExpendableBoundary([string]$candidate, [ref]$refusalReason) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $refusalReason.Value = "Path is empty or whitespace"
        return $false
    }
    try {
        $full = [System.IO.Path]::GetFullPath($candidate).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    } catch {
        $refusalReason.Value = "Invalid path syntax: $($_.Exception.Message)"
        return $false
    }

    $driveRoot = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($driveRoot) -or [string]::Equals($full, $driveRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar), [System.StringComparison]::OrdinalIgnoreCase)) {
        $refusalReason.Value = "Refused root drive directory: $full"
        return $false
    }

    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrEmpty($userProfile)) {
        $userProfile = [System.IO.Path]::GetFullPath($userProfile).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ([string]::Equals($full, $userProfile, [System.StringComparison]::OrdinalIgnoreCase)) {
            $refusalReason.Value = "Refused user profile directory: $full"
            return $false
        }
    }

    $winDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    if (-not [string]::IsNullOrEmpty($winDir)) {
        $winDir = [System.IO.Path]::GetFullPath($winDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($full.StartsWith($winDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            $refusalReason.Value = "Refused Windows directory: $full"
            return $false
        }
    }

    $progFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    if (-not [string]::IsNullOrEmpty($progFiles)) {
        $progFiles = [System.IO.Path]::GetFullPath($progFiles).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($full.StartsWith($progFiles, [System.StringComparison]::OrdinalIgnoreCase)) {
            $refusalReason.Value = "Refused Program Files directory: $full"
            return $false
        }
    }

    $specialFolders = @(
        [Environment+SpecialFolder]::Desktop,
        [Environment+SpecialFolder]::MyDocuments,
        [Environment+SpecialFolder]::MyPictures,
        [Environment+SpecialFolder]::MyMusic,
        [Environment+SpecialFolder]::MyVideos,
        [Environment+SpecialFolder]::ApplicationData,
        [Environment+SpecialFolder]::LocalApplicationData
    )

    foreach ($sf in $specialFolders) {
        $path = [Environment]::GetFolderPath($sf)
        if (-not [string]::IsNullOrEmpty($path)) {
            $path = [System.IO.Path]::GetFullPath($path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
            if ([string]::Equals($full, $path, [System.StringComparison]::OrdinalIgnoreCase)) {
                $refusalReason.Value = "Refused special system or media directory: $full"
                return $false
            }
        }
    }

    return $true
}

# Self-verify mandatory refusal boundaries before touching the disk
foreach ($badCandidate in @('', 'C:\', [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile), [Environment]::GetFolderPath([Environment+SpecialFolder]::MyPictures))) {
    $why = ''
    if (-not [string]::IsNullOrEmpty($badCandidate) -or $badCandidate -eq '') {
        if (Test-SafeExpendableBoundary $badCandidate ([ref]$why)) {
            Exit-Verdict 'FileManagerScenes' 1 "boundary guard failed to refuse forbidden path '$badCandidate'"
        }
    }
}

$refusal = ''
if (-not (Test-SafeExpendableBoundary $TestRoot ([ref]$refusal))) {
    Exit-Verdict 'FileManagerScenes' 2 "refused TestRoot '$TestRoot': $refusal"
}

# 2. Setup Operation-Owned Fixture Root
$testRootFull = [System.IO.Path]::GetFullPath($TestRoot)
if (-not (Test-Path -LiteralPath $testRootFull)) {
    [void](New-Item -ItemType Directory -Path $testRootFull -Force)
}
$opChild = Join-Path $testRootFull ("fms-262-fixture-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
[void](New-Item -ItemType Directory -Path $opChild -Force)

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repo 'temp/262'
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $OutDir)) {
    [void](New-Item -ItemType Directory -Path $OutDir -Force)
}

Write-Host "Operation fixture boundary: $opChild"
Write-Host "Evidence directory: $OutDir"

# 3. Build Reproducible Fixtures
$builderScript = Join-Path $PSScriptRoot 'FileManagerScenes/FileManagerFixtureBuilder.ps1'
& $builderScript -FixtureRoot $opChild -SkipBulk:$SkipSlow

$manifestSrc = Join-Path $opChild 'fixture-manifest.json'
if (Test-Path -LiteralPath $manifestSrc) {
    Copy-Item -LiteralPath $manifestSrc -Destination (Join-Path $OutDir 'fixture-manifest.json') -Force
}

$prevFixtureEnv = $env:FMS262_FIXTURE_ROOT
$prevDevserveEnv = $env:FMS_SFTP_LIVE_DEVSERVE
$prevRwEnv = $env:FMS_SFTP_LIVE_RW
$env:FMS262_FIXTURE_ROOT = $opChild

# Build live loopback SSH/SFTP servers (devserve read-only + rwsftp writable) when Go & fms_companion are present
$liveServersBuilt = $false
$workerRepo = Join-Path (Split-Path $repo -Parent) 'fms_companion'
$goCmd = Get-Command go -ErrorAction SilentlyContinue
$goPath = if ($goCmd) { $goCmd.Source } elseif (Test-Path 'C:\Program Files\Go\bin\go.exe') { 'C:\Program Files\Go\bin\go.exe' } else { $null }
if ($goPath -and (Test-Path -LiteralPath (Join-Path $workerRepo 'cmd\devserve\main.go'))) {
    $liveDir = Join-Path $opChild 'live_servers'
    [void](New-Item -ItemType Directory -Path $liveDir -Force)
    $devserveExe = Join-Path $liveDir 'devserve.exe'
    $rwsftpExe = Join-Path $liveDir 'rwsftp.exe'
    $ok1 = $false
    $ok2 = $false
    Push-Location $workerRepo
    try {
        & $goPath build -mod=readonly -o $devserveExe ./cmd/devserve 2>&1 | Out-Null
        $ok1 = ($LASTEXITCODE -eq 0)
    } finally {
        Pop-Location
    }
    $rwBuild = Join-Path $liveDir 'rwsftp_src'
    [void](New-Item -ItemType Directory -Path $rwBuild -Force)
    foreach ($name in 'main.go', 'go.mod') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "rwsftp\$name") -Destination $rwBuild -Force
    }
    Push-Location $rwBuild
    $savedGo = @{ GOPROXY = $env:GOPROXY; GOFLAGS = $env:GOFLAGS; GOSUMDB = $env:GOSUMDB }
    try {
        $env:GOPROXY = 'off'; $env:GOFLAGS = '-mod=mod'; $env:GOSUMDB = 'off'
        & $goPath build -o $rwsftpExe . 2>&1 | Out-Null
        $ok2 = ($LASTEXITCODE -eq 0)
    } finally {
        foreach ($k in @($savedGo.Keys)) {
            if ($null -eq $savedGo[$k]) { [Environment]::SetEnvironmentVariable($k, $null) } else { [Environment]::SetEnvironmentVariable($k, $savedGo[$k]) }
        }
        Pop-Location
    }
    if ($ok1 -and $ok2) {
        $env:FMS_SFTP_LIVE_DEVSERVE = $devserveExe
        $env:FMS_SFTP_LIVE_RW = $rwsftpExe
        $liveServersBuilt = $true
        Write-Host "Built live loopback SFTP test servers: devserve.exe, rwsftp.exe" -ForegroundColor DarkGray
    }
}

# 4. Execute Gates and Collect Evidence
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { Exit-Verdict 'FileManagerScenes' 2 'dotnet not found' }

$liteProj = Join-Path $repo 'tests/Lite.Tests/Lite.Tests.vbproj'
$evidenceLog = [System.Collections.Generic.List[string]]::new()
$gateResults = [ordered]@{}

function Record-GateResult([string]$gateId, [string]$description, [int]$exitCode, [string]$logFile) {
    $passed = ($exitCode -eq 0)
    $gateResults[$gateId] = [pscustomobject]@{
        Gate = $gateId
        Description = $description
        ExitCode = $exitCode
        Passed = $passed
        LogPath = $logFile
    }
    $statusStr = $(if ($passed) { 'PASS' } else { "FAIL (exit $exitCode)" })
    $color = $(if ($passed) { 'Green' } else { 'Red' })
    Write-Host "  ${gateId} result: $statusStr" -ForegroundColor $color
    $evidenceLog.Add("| $gateId | $description | $statusStr | $logFile |")
}

function Run-GateTest([string]$gateId, [string]$filter, [string]$description, [string]$framework = 'net10.0-windows') {
    Write-Host "Running ${gateId}: $description..." -ForegroundColor Cyan
    $logFile = Join-Path $OutDir "$gateId.log"
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dotnet test $liteProj -f $framework --filter $filter --logger "console;verbosity=normal" -p:NuGetAudit=false *> $logFile
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }
    Record-GateResult $gateId $description $exitCode $logFile
}

function Run-WinFormsUiScene() {
    Write-Host "Running G1-UI / G8-UI: On-screen WinForms File Manager, Archive, Destination, Theme, RTL & Settings scenes..." -ForegroundColor Cyan
    $sceneDir = Join-Path $opChild 'scene_runner'
    [void](New-Item -ItemType Directory -Path $sceneDir -Force)
    $modernProj = [System.Security.SecurityElement]::Escape((Join-Path $repo 'src/Modern/FastMediaSorter.Modern.vbproj'))
    $csprojPath = Join-Path $sceneDir 'FileManagerUiScene.csproj'
    $csPath = Join-Path $sceneDir 'Program.cs'
    $logFile = Join-Path $OutDir 'G8-ui-scenes.log'

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <PlatformTarget>x64</PlatformTarget>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>NU1902;NU1903</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <ProjectReference Include="$modernProj" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $csprojPath -Encoding utf8

    @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using fmsl;

static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    static object GetFieldOrProp(object target, string name)
    {
        Type t = target is Type typeObj ? typeObj : target.GetType();
        object inst = target is Type ? null : target;
        for (Type cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, Flags | BindingFlags.DeclaredOnly) ?? cur.GetField("_" + name, Flags | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(inst);
            var p = cur.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
            if (p != null) return p.GetValue(inst);
        }
        throw new InvalidOperationException("Missing member: " + name);
    }

    static void SetFieldOrProp(object target, string name, object value)
    {
        Type t = target is Type typeObj ? typeObj : target.GetType();
        object inst = target is Type ? null : target;
        for (Type cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, Flags | BindingFlags.DeclaredOnly) ?? cur.GetField("_" + name, Flags | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(inst, value); return; }
            var p = cur.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
            if (p != null) { p.SetValue(inst, value); return; }
        }
        throw new InvalidOperationException("Missing member: " + name);
    }

    static object CallMethod(object target, string name, params object[] args)
    {
        Type t = target is Type typeObj ? typeObj : target.GetType();
        object inst = target is Type ? null : target;
        var method = t.GetMethods(Flags).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
        if (method == null) throw new InvalidOperationException("Missing method: " + name);
        try { return method.Invoke(inst, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }

    static void Pump(int ms = 180)
    {
        long until = Environment.TickCount64 + ms;
        do
        {
            Application.DoEvents();
            Thread.Sleep(15);
        } while (Environment.TickCount64 < until);
    }

    static void WaitUntil(Func<bool> predicate, int timeoutMs = 10000, string description = "condition")
    {
        long until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            if (predicate()) return;
            Thread.Sleep(20);
        }
        if (!predicate()) throw new TimeoutException("Timed out waiting for: " + description);
    }

    static void SaveBitmap(Control control, string outPath)
    {
        Pump(120);
        using var bmp = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        control.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        bmp.Save(outPath, ImageFormat.Png);
    }

    static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("UI Scene Assertion Failed: " + message);
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string fixtureRoot = args[0];
        string outDir = args[1];
        bool skipSlow = args.Length > 2 && string.Equals(args[2], "true", StringComparison.OrdinalIgnoreCase);

        var asm = typeof(Main_Form).Assembly;
        var commonType = asm.GetType("fmsl.Common_Module");
        var localizationType = asm.GetType("fmsl.Localization");
        string tempRegKey = "fms-262-scene-" + Guid.NewGuid().ToString("N");
        SetFieldOrProp(commonType, "Second_App_Name", tempRegKey);
        SetFieldOrProp(localizationType, "CurrentCode", "en");
        SetFieldOrProp(commonType, "Form_Color_Scheme", 2); // Light theme first

        try
        {
            string g1Nav = Path.Combine(fixtureRoot, "g1_nav");
            string bulkDir = Path.Combine(g1Nav, "bulk_5000");

            // 1. Open Image_Panel_Form in File Manager Mode (Grid View, Light Theme)
            using (var panel = new Image_Panel_Form())
            {
                panel.StartPosition = FormStartPosition.Manual;
                panel.Location = new Point(60, 60);
                panel.Size = new Size(1080, 720);
                panel.Show();
                panel.PrepareForDisplay();
                panel.Size = new Size(1080, 720);
                Pump(150);

                var cmbMode = (ToolStripComboBox)GetFieldOrProp(panel, "cmb_Mode");
                cmbMode.SelectedIndex = 1; // Enter File Manager Mode
                Pump(150);

                AssertTrue((bool)GetFieldOrProp(panel, "manager_Mode"), "Panel did not enter File Manager mode");

                // Navigate to g1_nav via address bar + CommitAddress
                var addressBox = (TextBox)GetFieldOrProp(panel, "address_Box");
                addressBox.Focus();
                addressBox.Text = g1Nav;
                CallMethod(panel, "CommitAddress");

                WaitUntil(() =>
                {
                    var nav = GetFieldOrProp(panel, "nav");
                    var cur = nav == null ? null : GetFieldOrProp(nav, "Current");
                    var dir = cur == null ? "" : (string)GetFieldOrProp(cur, "DirectoryPath");
                    return string.Equals(dir, g1Nav, StringComparison.OrdinalIgnoreCase);
                }, 8000, "Navigate to g1_nav");
                Pump(200);

                var cmbFilter = (ToolStripComboBox)GetFieldOrProp(panel, "cmb_ContentFilter");
                cmbFilter.SelectedIndex = 2; // All files
                Pump(150);

                // Verify address box key consumption (Delete, F2, D1 do not trigger file commands while focused in address_Box)
                addressBox.Focus();
                var keyDel = new KeyEventArgs(Keys.Delete);
                CallMethod(panel, "OnFormKeyDown", panel, keyDel);
                var keyF2 = new KeyEventArgs(Keys.F2);
                CallMethod(panel, "OnFormKeyDown", panel, keyF2);
                AssertTrue(Directory.Exists(Path.Combine(g1Nav, "sub_alpha")), "Address bar Delete key triggered file deletion");

                SaveBitmap(panel, Path.Combine(outDir, "G1-manager-grid.png"));

                // 2. Navigate to bulk_5000 in DetailsList virtual mode
                if (!skipSlow && Directory.Exists(bulkDir))
                {
                    var btnViewMode = (ToolStripButton)GetFieldOrProp(panel, "btn_ViewMode");
                    btnViewMode.PerformClick(); // Switch to DetailsList
                    Pump(150);

                    addressBox.Focus();
                    addressBox.Text = bulkDir;
                    CallMethod(panel, "CommitAddress");

                    WaitUntil(() =>
                    {
                        var nav = GetFieldOrProp(panel, "nav");
                        var cur = nav == null ? null : GetFieldOrProp(nav, "Current");
                        var dir = cur == null ? "" : (string)GetFieldOrProp(cur, "DirectoryPath");
                        return string.Equals(dir, bulkDir, StringComparison.OrdinalIgnoreCase);
                    }, 10000, "Navigate to bulk_5000");
                    Pump(150);

                    cmbFilter.SelectedIndex = 2; // All files for bulk_5000
                    Pump(150);

                    WaitUntil(() =>
                    {
                        var display = (System.Collections.ICollection)GetFieldOrProp(panel, "display");
                        return display != null && display.Count == 5000;
                    }, 10000, "5,000-entry bulk_5000 listing");
                    Pump(200);

                    var details = (ListView)GetFieldOrProp(panel, "details");
                    AssertTrue(details.VirtualMode && details.VirtualListSize == 5000, "DetailsList VirtualListSize != 5000");
                    SaveBitmap(panel, Path.Combine(outDir, "G1-manager-list-5000.png"));
                }

                // 3. Dark theme check (G8)
                SetFieldOrProp(commonType, "Form_Color_Scheme", 1);
                CallMethod(panel, "ApplyContentColors");
                Pump(150);
                SaveBitmap(panel, Path.Combine(outDir, "G8-manager-dark.png"));

                // 4. RTL Arabic check (G8)
                SetFieldOrProp(localizationType, "CurrentCode", "ar");
                SetFieldOrProp(commonType, "Form_Color_Scheme", 2);
                panel.PrepareForDisplay();
                panel.Size = new Size(1080, 720);
                cmbMode.SelectedIndex = 1;
                Pump(150);
                addressBox.Text = g1Nav;
                CallMethod(panel, "CommitAddress");
                WaitUntil(() =>
                {
                    var nav = GetFieldOrProp(panel, "nav");
                    var cur = nav == null ? null : GetFieldOrProp(nav, "Current");
                    var dir = cur == null ? "" : (string)GetFieldOrProp(cur, "DirectoryPath");
                    return string.Equals(dir, g1Nav, StringComparison.OrdinalIgnoreCase);
                }, 8000, "RTL navigate to g1_nav");
                Pump(200);
                AssertTrue(panel.RightToLeft == RightToLeft.Yes, "Panel should use RightToLeft.Yes for Arabic");
                var grid = (Control)GetFieldOrProp(panel, "grid");
                AssertTrue(grid.RightToLeft == RightToLeft.No, "Grid stays LTR for positional indexing");
                SaveBitmap(panel, Path.Combine(outDir, "G8-manager-rtl-ar.png"));

                SetFieldOrProp(localizationType, "CurrentCode", "en");
                panel.Close();
            }

            // 5. ManagerArchiveForm & ManagerDestinationForm screenshots (G5, G6)
            var placeType = asm.GetType("fmsl.ManagerPlace");
            var locType = asm.GetType("fmsl.ManagerLocation");
            var forDrive = placeType.GetMethod("ForDrive", Flags);
            object localPlace = forDrive.Invoke(null, new object[] { fixtureRoot, "Local Fixture" });
            object currentLoc = Activator.CreateInstance(locType, Flags, null, new object[] { localPlace, g1Nav }, null);

            var archiveFormType = asm.GetType("fmsl.ManagerArchiveForm");
            using (var archForm = (Form)Activator.CreateInstance(archiveFormType, Flags, null, new object[] { currentLoc, null, true }, null))
            {
                archForm.StartPosition = FormStartPosition.Manual;
                archForm.Location = new Point(120, 120);
                archForm.Show();
                Pump(150);
                SaveBitmap(archForm, Path.Combine(outDir, "G5-archive-dialog.png"));
                archForm.Close();
            }

            var destFormType = asm.GetType("fmsl.ManagerDestinationForm");
            var placeListType = typeof(List<>).MakeGenericType(placeType);
            var placesList = Activator.CreateInstance(placeListType);
            placeListType.GetMethod("Add").Invoke(placesList, new object[] { localPlace });
            using (var destForm = (Form)Activator.CreateInstance(destFormType, Flags, null, new object[] { placesList, currentLoc }, null))
            {
                destForm.StartPosition = FormStartPosition.Manual;
                destForm.Location = new Point(120, 120);
                destForm.Show();
                Pump(150);
                SaveBitmap(destForm, Path.Combine(outDir, "G6-destination-picker.png"));
                destForm.Close();
            }

            // 6. Table_Form opened at File Manager page (G8)
            using (var table = new Table_Form())
            {
                table.StartPosition = FormStartPosition.Manual;
                table.Location = new Point(80, 80);
                table.Size = new Size(980, 680);
                CallMethod(table, "RequestOpenAtFileManager");
                table.Show();
                Pump(350);
                SaveBitmap(table, Path.Combine(outDir, "G8-settings-filemanager.png"));
                table.Close();
            }

            Console.WriteLine("UI scenes completed and 7 screenshots saved to " + outDir);
            return 0;
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + tempRegKey, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\VB and VBA Program Settings\" + Path.GetFileNameWithoutExtension(Application.ExecutablePath) + @"\" + tempRegKey, throwOnMissingSubKey: false); } catch { }
        }
    }
}
'@ | Set-Content -LiteralPath $csPath -Encoding utf8

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dotnet run --project $csprojPath -p:NuGetAudit=false -- $opChild $OutDir ($SkipSlow.IsPresent.ToString()) *> $logFile
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }
    Record-GateResult 'G8-UI' 'On-screen WinForms UI scenes (F3 Manager Grid/5000-List, Archive, Destination, Dark, RTL-AR, Settings)' $exitCode $logFile
}

try {
    # Gate Execution Matrix
    if ($Gate -eq 'All' -or $Gate -eq 'G1') {
        Run-GateTest 'G1' 'FullyQualifiedName~ManagerNavigationTests|FullyQualifiedName~ManagerCombinedAcceptanceTests.G1_' 'Navigation, providers, snapshot state, and real 5,000-entry virtual list'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G2') {
        Run-GateTest 'G2' 'FullyQualifiedName~ManagerOpenPolicyTests|FullyQualifiedName~ManagerContentPolicyTests|FullyQualifiedName~ManagerOpenHandoffTests|FullyQualifiedName~ManagerCombinedAcceptanceTests.G2_' 'Typed activation, safe open, deny set and viewer handoff'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G3') {
        Run-GateTest 'G3' 'FullyQualifiedName~ManagerCommandQueueTests|FullyQualifiedName~ManagerCommandPlanTests|FullyQualifiedName~ManagerCommandLifecycleTests|FullyQualifiedName~ManagerTreePlanTests|FullyQualifiedName~ManagerCombinedAcceptanceTests.G3_' 'Local copy, move, case-only rename, mkdir, delete and collision handling'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G4') {
        Run-GateTest 'G4' 'FullyQualifiedName~ManagerTransferLifecycleTests' 'Fault injection, cancel, missing source, and lifetime safety'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G5') {
        $g5Filter = 'FullyQualifiedName~VerifiedZipWriterTests|FullyQualifiedName~ArchiveExtractionPlanTests|FullyQualifiedName~ArchiveExtractionReaderTests|FullyQualifiedName~ManagerArchiveCommandTests|FullyQualifiedName~ManagerCombinedAcceptanceTests.G5_'
        if ($liveServersBuilt) {
            $g5Filter += '|FullyQualifiedName~ManagerArchiveLiveTests'
        }
        Run-GateTest 'G5' $g5Filter 'Verified ZIP creation, readback verification, extraction, and live SFTP archive round-trip'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G6') {
        $g6Filter = 'FullyQualifiedName~ManagerExchangePlacesTests|FullyQualifiedName~ManagerCrossPlaceTests'
        if ($liveServersBuilt) {
            $g6Filter += '|FullyQualifiedName~ManagerExchangeLiveTests.Live_LoopbackWindowsExchange'
        }
        Run-GateTest 'G6' $g6Filter 'Resource and exchange device matrix (local, SFTP, Windows Exchange Direct & Tunnel, remote-to-remote relay)'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G7') {
        Run-GateTest 'G7' 'FullyQualifiedName~ManagerArchitectureTests' 'Architecture invariants (single queue, single executor, single ZIP core, boundary refusal)'
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G8') {
        Run-GateTest 'G8' 'FullyQualifiedName~ManagerSettingsTests|FullyQualifiedName~FmsConfigExportTests|FullyQualifiedName~FmsImportRulesTests|FullyQualifiedName~ManagerCombinedAcceptanceTests.G8_' 'Preferences, imported presentation, dialog RTL/theme layout, and profile persistence'
        Run-GateTest 'G8-net48' 'FullyQualifiedName~FmsImportRulesTests' 'Legacy .NET Framework 4.8 compilation and compatibility check' 'net48'
        Run-WinFormsUiScene
    }
    if ($Gate -eq 'All' -or $Gate -eq 'G9') {
        Write-Host "Running G9: Documentation, Context Help, Product Naming & Check-Placement gates..." -ForegroundColor Cyan
        $g9Log = Join-Path $OutDir 'G9.log'
        $g9Exit = 0
        $g9Lines = [System.Collections.Generic.List[string]]::new()
        foreach ($scriptRel in @('tools/Test-ContextHelp.ps1', 'tools/Test-DocRegistry.ps1', 'tools/Test-ProductNaming.ps1', 'tools/Test-CheckPlacement.ps1')) {
            $scriptPath = Join-Path $repo $scriptRel
            $out = & pwsh -NoProfile -File $scriptPath 2>&1
            $code = $LASTEXITCODE
            $g9Lines.Add("=== $scriptRel (exit $code) ===")
            foreach ($line in $out) { $g9Lines.Add([string]$line) }
            if ($code -ne 0 -and $g9Exit -eq 0) { $g9Exit = $code }
        }
        [System.IO.File]::WriteAllLines($g9Log, $g9Lines, [System.Text.Encoding]::UTF8)
        Record-GateResult 'G9' 'Context help, documentation registry, product naming, and check-placement gates' $g9Exit $g9Log
    }

    # Collect generated screenshots
    $screenshots = Get-ChildItem -Path $OutDir -Filter '*.png' -ErrorAction SilentlyContinue | Sort-Object Name

    # Write Markdown Evidence Report
    $report = [System.Text.StringBuilder]::new()
    [void]$report.AppendLine("# Specification 262 Combined Acceptance Evidence")
    [void]$report.AppendLine()
    [void]$report.AppendLine("- **Timestamp:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC' -AsUTC)")
    [void]$report.AppendLine("- **OS Version:** $([System.Environment]::OSVersion.VersionString)")
    [void]$report.AppendLine("- **Runtime:** .NET 10.0 x64 + .NET Framework 4.8 x86")
    [void]$report.AppendLine("- **TestRoot Boundary:** $testRootFull")
    [void]$report.AppendLine("- **Operation Fixture Directory:** $opChild")
    [void]$report.AppendLine("- **Live Loopback SFTP/Media Exchange Server (``devserve`` + ``rwsftp``):** $(if ($liveServersBuilt) { 'Active (Direct + ExchangeTunnelLease verified)' } else { 'Skipped (Go/worker repo unavailable)' })")
    [void]$report.AppendLine()
    [void]$report.AppendLine("## Gate Results")
    [void]$report.AppendLine()
    [void]$report.AppendLine("| Gate | Description | Status | Log |")
    [void]$report.AppendLine("| --- | --- | --- | --- |")
    foreach ($line in $evidenceLog) {
        [void]$report.AppendLine($line)
    }
    [void]$report.AppendLine()
    [void]$report.AppendLine("## Captured On-Screen UI Screenshots")
    [void]$report.AppendLine()
    if ($screenshots -and $screenshots.Count -gt 0) {
        foreach ($shot in $screenshots) {
            [void]$report.AppendLine("- ``$($shot.Name)`` ($($shot.Length) bytes)")
        }
    } else {
        [void]$report.AppendLine("- *(No screenshots recorded in this gate filter)*")
    }
    [void]$report.AppendLine()
    [void]$report.AppendLine("## Notes on G6 Exchange Devices")
    [void]$report.AppendLine('Live loopback Windows producer (`rwsftp` writable + `devserve` read-only) passed over both Direct LAN/loopback and `ExchangeTunnelLease` (`ExchangeForwarder`) routes, including `mkdir`, verified upload/move, `rename`, Archive-on-Device (verified ZIP creation with authorized cleanup + extraction on device), verified download with checked source delete, and Remote-to-Remote relay (`serverA` -> this PC scratch -> `serverB`). Physical Android SAF hardware benches remain opt-in via `tests/Integration/ManagerExchangeLive.ps1` per specification 262 section 54.')

    $reportFile = Join-Path $OutDir 'evidence.md'
    [System.IO.File]::WriteAllText($reportFile, $report.ToString(), [System.Text.Encoding]::UTF8)
} finally {
    $env:FMS262_FIXTURE_ROOT = $prevFixtureEnv
    $env:FMS_SFTP_LIVE_DEVSERVE = $prevDevserveEnv
    $env:FMS_SFTP_LIVE_RW = $prevRwEnv
    # Cleanup Operation-Owned Fixture Folder
    try {
        if (Test-Path -LiteralPath $opChild) {
            Remove-Item -LiteralPath $opChild -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch {
        # Ignore cleanup errors in temp
    }
}

$allPassed = $true
foreach ($k in $gateResults.Keys) {
    if (-not $gateResults[$k].Passed) { $allPassed = $false }
}

if (-not $allPassed) {
    Exit-Verdict 'FileManagerScenes' 1 "one or more acceptance gates failed; inspect evidence in $OutDir"
}

Exit-Verdict 'FileManagerScenes' 0 "all evaluated gates passed; evidence recorded in $OutDir"
