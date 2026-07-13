param(
    # Skip the pre-build housekeeping (tools\Clean-Build.ps1) that prunes
    # superseded stage\ trees, orphaned bin\ folders and temp files.
    [switch]$NoClean,
    # Skip building the .NET 10 Companion (Fast Media Sorter: Share Manager). By
    # default this script now builds BOTH exes so you can build everything locally.
    [switch]$SkipCompanion
)

$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "FastMediaSorter.sln"
$OutputDir    = Join-Path $SolutionDir "bin\Release"
$SingleFileDir = Join-Path $SolutionDir "bin\SingleFile"
$ExeName      = "FastMediaSorter_LITE.exe"
# Android-share sidecar payload (docs/specifications/SPECIFICATION_ANDROID_FOLDER_SHARE.md). The
# worker exe must ship in a "companion\" subfolder next to every deployed exe,
# or the Share tab / wizard finds nothing (WorkerProcess.IsAvailable() = False).
$PayloadCompanionDir = Join-Path $SolutionDir "payload\companion"
# The .NET 10 Companion (Share Manager) - built with `dotnet publish` as a
# self-contained single-file exe and deployed next to FastMediaSorter_LITE.exe.
$CompanionProj       = Join-Path $SolutionDir "src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj"
$CompanionExeName    = "FastMediaSorterCompanion.exe"
$CompanionPublishDir = Join-Path $SolutionDir "bin\CompanionPublish"
$CompanionExe        = Join-Path $CompanionPublishDir $CompanionExeName
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

$ExePath = Join-Path $OutputDir $ExeName
if (-not (Test-Path $ExePath)) {
    Write-Error "Output not found: $ExePath"
    exit 1
}

# The release output still contains the expanded runtime tree for local debugging,
# but the distributable build is now the exe alone: it contains the managed
# assemblies and unpacks the native runtime to %LOCALAPPDATA% on first start.
New-Item -ItemType Directory -Path $SingleFileDir -Force | Out-Null
$SingleFileExe = Join-Path $SingleFileDir $ExeName
Copy-Item -Path $ExePath -Destination $SingleFileExe -Force
Write-Host "Single-file build staged at: $SingleFileExe"

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

# Free the worker + Companion exes (they may be running from a deploy target and
# locking themselves) before mirroring anything.
Stop-ShareWorker
Stop-Companion

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

    $Target = Join-Path $Destination $ExeName
    Copy-Item -Path $SingleFileExe -Destination $Target -Force

    Write-Host "Deployed single-file exe -> $Target"
    Deploy-Companion $Destination      # worker payload -> <dest>\companion\
    Deploy-CompanionExe $Destination   # FastMediaSorterCompanion.exe -> <dest>\
}
