param(
    # Skip the pre-build housekeeping (tools\Clean-Build.ps1) that prunes
    # superseded stage\ trees, orphaned bin\ folders and temp files.
    [switch]$NoClean,
    # Skip building the .NET 10 Companion (Fast Media Sorter: Share Manager). By
    # default this script now builds BOTH exes so you can build everything locally.
    [switch]$SkipCompanion,
    # Skip publishing the .NET 10 modern viewer (SPECIFICATION_DOTNET10_MODERN_BUILD).
    # By default it is published to bin\ModernPublish and mirrored into a "modern\"
    # subfolder of each deploy target for side-by-side testing with the net48 exe.
    [switch]$SkipModern
)

$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "FastMediaSorter.sln"
$OutputDir    = Join-Path $SolutionDir "bin\Release"
$SingleFileDir = Join-Path $SolutionDir "bin\SingleFile"
# The viewer ships as TWO exes side by side in one folder, sharing the adjacent
# libraries (codecs/OCR/worker) that are installed next to them:
#   FastMediaSorter_LITE.exe - the .NET 10 x64 mainline (frozen name; replaces the
#                              installed exe in place), published self-contained.
#   FastMediaSorter_x86.exe  - the lean net48 viewer for old/32-bit Windows.
# Both resolve their native runtimes per-process bitness out of the SAME tree
# (libvlc\win-x64 vs win-x86, x64\ vs x86\), so one folder serves both.
$ExeName      = "FastMediaSorter_LITE.exe"
$LegacyExeName = "FastMediaSorter_x86.exe"
# Android-share sidecar payload (docs/specifications/done/SPECIFICATION_ANDROID_FOLDER_SHARE.md). The
# worker exe must ship in a "companion\" subfolder next to every deployed exe,
# or the Share tab / wizard finds nothing (WorkerProcess.IsAvailable() = False).
$PayloadCompanionDir = Join-Path $SolutionDir "payload\companion"
# The .NET 10 Companion (Share Manager) - built with `dotnet publish` as a
# self-contained single-file exe and deployed next to FastMediaSorter_LITE.exe.
$CompanionProj       = Join-Path $SolutionDir "src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj"
$CompanionExeName    = "FastMediaSorterCompanion.exe"
$CompanionPublishDir = Join-Path $SolutionDir "bin\CompanionPublish"
$CompanionExe        = Join-Path $CompanionPublishDir $CompanionExeName
# The .NET 10 modern viewer. Its publish is a full standalone tree (exe + the
# libvlc plugin tree + tesseract natives + flags); bin\Release already carries
# those support trees from the net48 build, so only the exe is copied next to its
# x86 sibling there. A lone exe still self-heals: the runtime resolution falls
# back to downloading codecs/OCR into %LOCALAPPDATA% on first use.
$ModernProj       = Join-Path $SolutionDir "src\Modern\FastMediaSorter.Modern.vbproj"
$ModernPublishDir = Join-Path $SolutionDir "bin\ModernPublish"
$ModernExe        = Join-Path $ModernPublishDir $ExeName
$Destinations = @(
    "C:\GD\i\",
    "C:\GD\tc\SZA\_APP\"
)

# Mirror payload\companion\ into <targetDir>\companion\. No-op (with a warning)
# when the payload is absent, so a checkout without the vendored worker still
# builds - it just ships without the Share feature.
function Deploy-Companion([string]$TargetDir) {
    if (-not (Test-Path $PayloadCompanionDir)) {
        Write-Warning "Companion payload not found ($PayloadCompanionDir) - Share feature will be unavailable in: $TargetDir"
        return
    }
    $dest = Join-Path $TargetDir "companion"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $PayloadCompanionDir "*") -Destination $dest -Recurse -Force
    Write-Host "Deployed companion payload -> $dest"
}

# A running Android-share worker (fms-share-worker.exe) launched from a deploy
# target locks its own exe, so Copy-Item over it fails with "being used by another
# process". Stop it before deploying: best-effort graceful StopServer over the
# control pipe (releases the SFTP server + UPnP port map cleanly), then force-kill
# any survivors and wait for the file handle to be released.
function Stop-ShareWorker {
    if (-not (Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue)) { return }
    Write-Host "Stopping running Android-share worker (fms-share-worker) so its exe can be replaced.."
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "fms-companion", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(1000)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"type":"StopServer"}')
        $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
        $pipe.Dispose()
        Start-Sleep -Milliseconds 300
    } catch { }
    foreach ($p in (Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue)) {
        try { $p.Kill(); $p.WaitForExit(3000) } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# A running Companion (Share Manager) locks its own 100+ MB single-file exe, so
# Copy-Item over it fails. Stop it before deploying (it is only a tray/UI process -
# the worker it controls keeps running independently).
function Stop-Companion {
    if (-not (Get-Process -Name "FastMediaSorterCompanion" -ErrorAction SilentlyContinue)) { return }
    Write-Host "Stopping running FastMediaSorterCompanion so its exe can be replaced.."
    foreach ($p in (Get-Process -Name "FastMediaSorterCompanion" -ErrorAction SilentlyContinue)) {
        try { $p.Kill(); $p.WaitForExit(3000) } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# Copy the published Companion exe next to FastMediaSorter_LITE.exe. No-op (with a
# warning) when it wasn't built (e.g. -SkipCompanion), so LITE still deploys.
function Deploy-CompanionExe([string]$TargetDir) {
    if (-not (Test-Path $CompanionExe)) {
        Write-Warning "Companion exe not built ($CompanionExe) - skipping for: $TargetDir"
        return
    }
    $dest = Join-Path $TargetDir $CompanionExeName
    Copy-Item -Path $CompanionExe -Destination $dest -Force
    Write-Host "Deployed Companion exe -> $dest"
}

# Find MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null |
    Select-Object -First 1

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    # Fallback: try well-known VS 2022 path
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $msbuild)) {
        Write-Error "MSBuild not found. Install Visual Studio or add MSBuild to PATH."
        exit 1
    }
}

Write-Host "Using MSBuild: $msbuild"

# Housekeeping: drop superseded stage\ trees, orphaned bin\ experiment folders
# and temp scratch so they do not accumulate. Leaves dist\ alone (a local build
# does not produce installers - the installer scripts prune their own dist\).
if (-not $NoClean) {
    & (Join-Path $SolutionDir "tools\Clean-Build.ps1") -Stage -Bin -Temp
}

# Restore NuGet packages (packages.config). Needed for a fresh clone; a no-op when
# packages are already present. Downloads nuget.exe to tools\ if it isn't on PATH.
$nuget = (Get-Command nuget.exe -ErrorAction SilentlyContinue).Source
if (-not $nuget) {
    $nuget = Join-Path $SolutionDir "tools\nuget.exe"
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Path (Split-Path $nuget) -Force | Out-Null
        Write-Host "Downloading nuget.exe..."
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
    }
}
Write-Host "Restoring NuGet packages..."
& $nuget restore $SolutionFile -NonInteractive
if ($LASTEXITCODE -ne 0) {
    Write-Error "NuGet restore failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Host "Building Release..."

& $msbuild $SolutionFile /p:Configuration=Release /p:Platform="Any CPU" /t:Rebuild /v:minimal /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

$LegacyExePath = Join-Path $OutputDir $LegacyExeName
if (-not (Test-Path $LegacyExePath)) {
    Write-Error "Output not found: $LegacyExePath"
    exit 1
}

# bin\Release keeps the expanded runtime trees (both arches) for local debugging;
# the distributable x86 viewer is the exe alone - it carries its managed
# assemblies inside and unpacks the native runtime to %LOCALAPPDATA% on first start.
New-Item -ItemType Directory -Path $SingleFileDir -Force | Out-Null
$SingleFileExe = Join-Path $SingleFileDir $LegacyExeName
Copy-Item -Path $LegacyExePath -Destination $SingleFileExe -Force
Write-Host "Single-file x86 viewer staged at: $SingleFileExe"

# Build the .NET 10 Companion (Share Manager) as a self-contained single-file exe
# (needs the .NET 10 SDK; the win-x64 self-contained/single-file props live in the
# .vbproj). This is what lets you build the WHOLE package locally in one command.
if (-not $SkipCompanion) {
    $dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { $dotnet = "dotnet" }
    Write-Host "Publishing Companion (net10, self-contained single-file).."
    Stop-Companion   # release a lock on the previous exe under bin\CompanionPublish
    Remove-Item $CompanionPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    & $dotnet publish $CompanionProj -c Release -r win-x64 -o $CompanionPublishDir -v minimal --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Companion publish failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
    if (-not (Test-Path $CompanionExe)) {
        Write-Error "Companion exe not found after publish: $CompanionExe"
        exit 1
    }
    Write-Host "Companion published at: $CompanionExe"
} else {
    Write-Host "Skipping Companion build (-SkipCompanion)."
}

# Publish the .NET 10 modern viewer (self-contained single-file + loose libvlc
# tree). Same publish pattern as the Companion; the win-x64 publish props live in
# src\Modern\FastMediaSorter.Modern.vbproj.
if (-not $SkipModern) {
    $dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { $dotnet = "dotnet" }
    Write-Host "Publishing modern viewer (net10 x64, self-contained single-file).."
    Remove-Item $ModernPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    & $dotnet publish $ModernProj -c Release -r win-x64 -o $ModernPublishDir -v minimal --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Modern publish failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
    if (-not (Test-Path $ModernExe)) {
        Write-Error "Modern exe not found after publish: $ModernExe"
        exit 1
    }
    Write-Host "Modern viewer published at: $ModernExe"

    # Put the x64 mainline exe next to its x86 sibling in bin\Release: that folder
    # already holds the shared support trees (libvlc win-x64 + win-x86, x64\ + x86\
    # tesseract, flags), so this makes bin\Release the real distribution shape -
    # two exes, one set of adjacent libraries.
    Copy-Item -Path $ModernExe -Destination (Join-Path $OutputDir $ExeName) -Force
    Write-Host "Modern viewer staged next to the x86 sibling -> $(Join-Path $OutputDir $ExeName)"
} else {
    Write-Host "Skipping modern viewer publish (-SkipModern)."
}

# Copy the .NET 10 x64 mainline exe next to the x86 viewer in a deploy target.
# No support trees: a lone exe downloads the codecs/OCR it needs into
# %LOCALAPPDATA% on first use, exactly like the x86 single-file viewer does.
function Deploy-ModernExe([string]$TargetDir) {
    if ($SkipModern -or -not (Test-Path $ModernExe)) { return }
    $dest = Join-Path $TargetDir $ExeName
    Copy-Item -Path $ModernExe -Destination $dest -Force
    Write-Host "Deployed modern x64 viewer -> $dest"
}

# A running viewer locks its own exe, so replacing it needs it stopped first.
# Both names are checked: the two exes are one app (shared mutex/settings).
function Stop-Viewers {
    foreach ($name in @("FastMediaSorter_LITE", "FastMediaSorter_x86")) {
        foreach ($p in (Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            Write-Host "Stopping running $name so its exe can be replaced.."
            try { $p.Kill(); $p.WaitForExit(3000) } catch { }
        }
    }
}

# Free the worker + Companion + viewer exes (they may be running from a deploy
# target and locking themselves) before mirroring anything.
Stop-ShareWorker
Stop-Companion
Stop-Viewers

# Keep the worker beside the staging outputs too: MSBuild Rebuild does not touch
# the companion\ subfolder, but a fresh checkout has none - self-heal both. Also
# drop the Companion exe alongside so bin\Release / bin\SingleFile are runnable.
Deploy-Companion $OutputDir
Deploy-Companion $SingleFileDir
Deploy-CompanionExe $OutputDir
Deploy-CompanionExe $SingleFileDir

foreach ($Destination in $Destinations) {
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    # Both viewers land side by side, exactly as they ship: the x64 mainline and
    # the lean x86 fallback, sharing whatever adjacent libraries are installed.
    $Target = Join-Path $Destination $LegacyExeName
    Copy-Item -Path $SingleFileExe -Destination $Target -Force
    Write-Host "Deployed x86 viewer -> $Target"

    Deploy-ModernExe $Destination      # FastMediaSorter_LITE.exe (x64) -> <dest>\
    Deploy-Companion $Destination      # worker payload -> <dest>\companion\
    Deploy-CompanionExe $Destination   # FastMediaSorterCompanion.exe -> <dest>\
}

# The x64 mainline now owns the frozen FastMediaSorter_LITE.exe name, so a deploy
# target that still holds the OLD net48 exe under that name would silently keep
# the stale build. Nothing to do automatically (it was just overwritten above),
# but flag any leftover from the previous "modern\" layout this script used to
# create, so it does not confuse a manual test.
foreach ($Destination in $Destinations) {
    $staleModernDir = Join-Path $Destination "modern"
    if (Test-Path $staleModernDir) {
        Write-Warning "Leftover from the old layout - delete it: $staleModernDir"
    }
}
