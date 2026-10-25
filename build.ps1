param(
    # Skip the pre-build housekeeping (tools\Clean-Build.ps1) that prunes
    # superseded stage\ trees, orphaned bin\ folders and temp files.
    [switch]$NoClean,
    # Skip building the .NET 10 Companion (the Share Manager). By
    # default this script now builds BOTH exes so you can build everything locally.
    [switch]$SkipCompanion,
    # Leave the exchange server (exchange-server\, a Go module - specification 133) out of this
    # build entirely: no Go build, no exchange\ folder in bin\Release or the work folders, no
    # `exchange` component in either setup. The exchange server is IN by default, so a plain
    # .\build.ps1 builds and packages everything; a machine without a 64-bit Go then stops here
    # rather than quietly packaging setups without it.
    # This switch is NOT the shipping gate. tools\Build-OfflineRelease.ps1 (Release.ps1) and
    # release.yml keep INCLUDE_EXCHANGE off until rung 2 freezes the wire (specification 133
    # section 8, ticket 141). A local build never creates a tag or uploads anything.
    [switch]$SkipExchange,
    # Skip publishing the .NET 10 modern viewer (SPECIFICATION_DOTNET10_MODERN_BUILD).
    # By default it is published to bin\ModernPublish and mirrored into a "modern\"
    # subfolder of each deploy target for side-by-side testing with the net48 exe.
    [switch]$SkipModern,
    # Skip opening the freshly built modern viewer for manual testing.
    [switch]$SkipLaunch,
    # Skip the final packaging step (tools\Build-Installer.ps1). By default a local
    # build ends by packaging the copy-anywhere offline installer into dist\, so the
    # thing you hand to someone else is never older than the exes you just tested.
    # Still a "сборка", never a "релиз": no v* tag is created or pushed.
    [switch]$SkipInstaller,
    # Skip the SERVER edition installer (tools\Build-ServerInstaller.ps1) - the
    # always-on Folder Share Server, hosted by a Windows service
    # (docs/specifications/done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md). It is a SEPARATE
    # product entry, not a mode of the one above: its own AppId, ARP name, install
    # directory and winget package. Built by default so the two setup.exe files in
    # dist\ are always from the same sources - a Server package left behind by an
    # earlier build is exactly how a stale worker gets handed to a server.
    # Cheap: it reuses the Companion published above and carries no VLC codecs and no
    # OCR models; the viewer rides along as an optional, unticked component staged
    # from the User installer step above (tools\Stage-ServerViewerPayload.ps1).
    [switch]$SkipServerInstaller,
    # Installer payload, forwarded to tools\Build-Installer.ps1. Default: the lighter
    # fast OCR models (offline OCR out of the box). -InstallerIncludeBest also bundles
    # the heavy tessdata_best models; -InstallerSkipOcr bundles none of them (smallest
    # and quickest package - the models download on first OCR use instead).
    [switch]$InstallerIncludeBest,
    [switch]$InstallerSkipOcr
)

$SolutionDir = $PSScriptRoot
# Derived, not a switch: every local build carries the exchange server unless -SkipExchange leaves it out.
$IncludeExchange = -not $SkipExchange
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
# The exchange server (specification 133): a Go program compiled from the exchange-server\
# module of THIS repository (nothing is vendored), windows/amd64 only. It lands in an
# "exchange\" tree beside the viewer exes - the same shape as companion\ - and never
# installs, starts or opens a port by itself.
$ExchangeBuildScript = Join-Path $SolutionDir "exchange-server\build.ps1"
$ExchangeExeName     = "fms-exchange-server.exe"
$ExchangePublishDir  = Join-Path $SolutionDir "bin\ExchangePublish"
$ExchangeExe         = Join-Path $ExchangePublishDir $ExchangeExeName
$ExchangeBuilt       = $false   # set only when THIS run built it
# The .NET 10 modern viewer. Its publish is a full standalone tree (exe + the
# libvlc plugin tree + tesseract natives + flags); bin\Release already carries
# those support trees from the net48 build, so only the publish output itself is
# copied next to the x86 sibling there. A lone exe still self-heals: the runtime
# resolution falls back to downloading codecs/OCR into %LOCALAPPDATA% on first use.
$ModernProj       = Join-Path $SolutionDir "src\Modern\FastMediaSorter.Modern.vbproj"
$ModernPublishDir = Join-Path $SolutionDir "bin\ModernPublish"
$ModernExe        = Join-Path $ModernPublishDir $ExeName
$Destinations = @(
    "C:\GD\i\",
    "C:\GD\tc\SZA\_APP\"
)

# Copy a single-file publish output into a target folder: the exe AND whatever
# loose files sit beside it, minus debug/doc artefacts. Single-file publish does not
# mean single file - this project sets IncludeNativeLibrariesForSelfExtract=false, so
# a native library stays on disk next to the exe (Magick.Native-Q8-x64.dll, which is
# what decodes AVIF/HEIC/HEIF). Every packager used to copy the exe BY NAME and drop
# it, so the shipped viewer could not open an iPhone photo it advertised support for.
function Copy-PublishOutput([string]$PublishDir, [string]$TargetDir) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Get-ChildItem $PublishDir -File |
        Where-Object { $_.Extension -notin ".pdb", ".xml" } |
        ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $TargetDir $_.Name) -Force
            if ($_.Extension -ne ".exe") { Write-Host "  loose publish asset -> $($_.Name)" }
        }
}

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
    & (Join-Path $SolutionDir "tools\Stage-FileDoPayload.ps1") -Destination $TargetDir
    Write-Host "Deployed companion payload -> $dest"
}

# Directories this build WRITES: the repo (bin\, stage\ ..) and the deploy targets.
# A process running from anywhere else - the user's installed copy under Program
# Files or %LOCALAPPDATA%\Programs - is not ours to close: this build never touches
# its files, so killing it buys nothing and takes away something the user was using.
function Test-RunsFromOwnedDir([System.Diagnostics.Process]$Process) {
    $path = $null
    try { $path = $Process.Path } catch { }   # a process we may not query is not ours
    if (-not $path) { return $false }
    foreach ($dir in (@($SolutionDir) + @($Destinations))) {
        if ($dir -and $path.StartsWith((Join-Path $dir ''), [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# A running Android-share worker (fms-share-worker.exe) launched from a deploy
# target locks its own exe, so Copy-Item over it fails with "being used by another
# process". Stop it before deploying: best-effort graceful StopServer over the
# control pipe (releases the SFTP server + UPnP port map cleanly), then force-kill
# any survivors and wait for the file handle to be released.
#
# Only a worker running from a directory this build writes (Test-RunsFromOwnedDir).
# The installed User edition's worker serves the user's phone out of the install
# directory, which nothing here replaces - killing it only cut a live share on every
# local build. The pipe is shared by whichever worker is up, so StopServer is sent
# only when every running worker is one of ours.
function Stop-ShareWorker {
    $workers = @(Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue)
    if ($workers.Count -eq 0) { return }

    # The Server edition runs the SAME exe as a Windows service, out of its own
    # install directory - a copy this build never touches. Killing that process is
    # both pointless (no file of ours is locked by it) and destructive: the SCM
    # failure actions restart it as if it had crashed, and the developer's phone
    # loses the share mid-build. A local build must not disturb a machine service,
    # so leave it alone and say so.
    $svc = Get-Service -Name "FastMediaSorterCompanionSFTP" -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Write-Host "Server edition detected (FastMediaSorterCompanionSFTP is running) - leaving its worker alone."
        Write-Host "  It serves from its own install directory; nothing here is locked by it."
        return
    }

    $ours = @($workers | Where-Object { Test-RunsFromOwnedDir $_ })
    foreach ($p in ($workers | Where-Object { -not (Test-RunsFromOwnedDir $_) })) {
        $where = $null; try { $where = $p.Path } catch { }
        Write-Host "Leaving fms-share-worker alone - it runs from $where, which this build does not write."
    }
    if ($ours.Count -eq 0) { return }

    Write-Host "Stopping running Android-share worker (fms-share-worker) so its exe can be replaced.."
    if ($ours.Count -eq $workers.Count) {
        try {
            $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "fms-companion", [System.IO.Pipes.PipeDirection]::InOut)
            $pipe.Connect(1000)
            $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"type":"StopServer"}')
            $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
            $pipe.Dispose()
            Start-Sleep -Milliseconds 300
        } catch { }
    }
    foreach ($p in $ours) {
        try { $p.Kill(); $p.WaitForExit(3000) } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# A running Companion (Share Manager) locks its own 100+ MB single-file exe, so
# Copy-Item over it fails. Stop it before deploying (it is only a tray/UI process -
# the worker it controls keeps running independently). Same scope as the viewers
# and the worker: only a copy running from a directory this build writes - the
# installed tray app is left alone.
function Stop-Companion {
    $procs = @(Get-Process -Name "FastMediaSorterCompanion" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return }
    foreach ($p in $procs) {
        if (-not (Test-RunsFromOwnedDir $p)) {
            $where = $null; try { $where = $p.Path } catch { }
            Write-Host "Leaving FastMediaSorterCompanion alone - it runs from $where, which this build does not write."
            continue
        }
        Write-Host "Stopping running FastMediaSorterCompanion so its exe can be replaced.."
        try { $p.Kill(); $p.WaitForExit(3000) } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# Copy the published Companion exe next to FastMediaSorter_LITE.exe. No-op (with a
# warning) when it wasn't built (e.g. -SkipCompanion), so the viewer still deploys.
function Deploy-CompanionExe([string]$TargetDir) {
    if (-not (Test-Path $CompanionExe)) {
        Write-Warning "Companion exe not built ($CompanionExe) - skipping for: $TargetDir"
        return
    }
    $dest = Join-Path $TargetDir $CompanionExeName
    Copy-Item -Path $CompanionExe -Destination $dest -Force
    Write-Host "Deployed Companion exe -> $dest"
}

# A running exchange server locks its own exe, so Copy-Item over it fails. Same scope as the
# viewers, the worker and the Share Manager: only a copy running from a directory this build
# writes. An installed copy under Program Files - and the Server edition's Windows service,
# which runs from its own install directory - is not ours to stop.
function Stop-Exchange {
    $procs = @(Get-Process -Name "fms-exchange-server" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return }
    foreach ($p in $procs) {
        if (-not (Test-RunsFromOwnedDir $p)) {
            $where = $null; try { $where = $p.Path } catch { }
            Write-Host "Leaving fms-exchange-server alone - it runs from $where, which this build does not write."
            continue
        }
        Write-Host "Stopping running fms-exchange-server so its exe can be replaced.."
        try { $p.Kill(); $p.WaitForExit(3000) } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# Copy the exchange server this run built into <targetDir>\exchange\: the exe and its .sha256,
# plus the package tree of ticket 141 - everything under exchange-server\package\ (README.txt)
# and the two helpers from publishing\installer\ (enable-exchange-server.ps1,
# install-exchange-service.ps1). The package files are written by other tickets and may not
# exist yet: a missing one is skipped, never an error - the packagers (Stage-ExchangePayload.ps1)
# are the ones that insist on them, and only when the shipping gate is open.
# Nothing to do - and nothing claimed - when it was skipped or there is no Go: an old copy left in
# a deploy target by an earlier build is never refreshed by pretending.
function Deploy-Exchange([string]$TargetDir) {
    if (-not $ExchangeBuilt) { return }
    $dest = Join-Path $TargetDir "exchange"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path $ExchangeExe -Destination $dest -Force
    Copy-Item -Path "$ExchangeExe.sha256" -Destination $dest -Force
    $packageDir = Join-Path $SolutionDir "exchange-server\package"
    if (Test-Path -LiteralPath $packageDir) {
        Copy-Item -Path (Join-Path $packageDir "*") -Destination $dest -Recurse -Force
    }
    foreach ($helper in @("enable-exchange-server.ps1", "install-exchange-service.ps1")) {
        $helperPath = Join-Path $SolutionDir ("publishing\installer\" + $helper)
        if (Test-Path -LiteralPath $helperPath) { Copy-Item -Path $helperPath -Destination $dest -Force }
    }
    Write-Host "Deployed exchange server -> $dest"
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
# and temp scratch so they do not accumulate. Leaves dist\ alone - the installer
# step at the end runs its own pass that prunes dist\ while keeping this version.
if (-not $NoClean) {
    & (Join-Path $SolutionDir "tools\Clean-Build.ps1") -Stage -Bin -Temp
}

# Restore NuGet packages (packages.config). Needed for a fresh clone; a no-op when
# packages are already present. Uses nuget.exe from PATH, else the PINNED copy in
# tools\ (downloaded and hash-checked on first use - tools\Get-PinnedNuGet.ps1).
$nuget = & (Join-Path $SolutionDir "tools\Get-PinnedNuGet.ps1")
Write-Host "Restoring NuGet packages..."
& $nuget restore $SolutionFile -NonInteractive
if ($LASTEXITCODE -ne 0) {
    Write-Error "NuGet restore failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

# ONE version stamp for everything this build produces: the x86 viewer (msbuild),
# the x64 mainline and the Share Manager (dotnet publish) and both installers.
# Each used to stamp its own compile minute, so bin\Release and the deploy targets
# held a FastMediaSorter_LITE.exe and a FastMediaSorter_x86.exe with different
# versions - and what was hand-tested was not what got packaged. Kept as TEXT from
# here on: the exe's numeric FileVersion turns 26.7.30.0108 into 26.7.30.108.
$BuildVersion = Get-Date -Format "yy.M.d.HHmm"
Write-Host "Building Release (version $BuildVersion)..."

& $msbuild $SolutionFile /p:Configuration=Release /p:Platform="Any CPU" /p:ReleaseVersion=$BuildVersion /t:Rebuild /v:minimal /nologo

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
    & $dotnet publish $CompanionProj -c Release -r win-x64 -p:ReleaseVersion=$BuildVersion -o $CompanionPublishDir -v minimal --nologo
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

# Build the exchange server (specification 133). The module's own script resolves a 64-bit
# Go, stamps the exe with the ONE version of this run and checks it; exit 2 means "no Go" and
# is a skip, never a build failure - and never a claim that it was built.
if (-not $SkipExchange) {
    Write-Host "Building the exchange server (Go, windows/amd64).."
    Stop-Exchange   # release a lock on the previous exe under bin\ExchangePublish
    Remove-Item $ExchangePublishDir -Recurse -Force -ErrorAction SilentlyContinue
    & $ExchangeBuildScript -Version $BuildVersion -OutDir $ExchangePublishDir
    switch ($LASTEXITCODE) {
        0 { $ExchangeBuilt = $true }
        2 {
            # No 64-bit Go means no exchange server, and the setups below carry it: stop, do not
            # package without it. -SkipExchange is the deliberate way to build without it.
            Write-Error "exchange server: no 64-bit Go - install Go, or build without the exchange server with -SkipExchange."
            exit 2
        }
        default {
            Write-Error "Exchange server build failed (exit code $LASTEXITCODE)."
            exit $LASTEXITCODE
        }
    }
} else {
    Write-Host "Skipping exchange server build (-SkipExchange)."
}

# Publish the .NET 10 modern viewer (self-contained single-file + loose libvlc
# tree). Same publish pattern as the Companion; the win-x64 publish props live in
# src\Modern\FastMediaSorter.Modern.vbproj.
if (-not $SkipModern) {
    $dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { $dotnet = "dotnet" }
    Write-Host "Publishing modern viewer (net10 x64, self-contained single-file).."
    Remove-Item $ModernPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    & $dotnet publish $ModernProj -c Release -r win-x64 -p:ReleaseVersion=$BuildVersion -o $ModernPublishDir -v minimal --nologo
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
    #
    # The publish output is NOT just the exe. IncludeNativeLibrariesForSelfExtract is
    # false, so a native library stays LOOSE beside it - today Magick.Native-Q8-x64.dll,
    # the thing that actually decodes AVIF/HEIC/HEIF. Cherry-picking the exe by name
    # silently shipped a viewer whose iPhone-photo support could not load. Copy the
    # whole publish output (minus debug/doc artefacts) so any loose native rides along.
    Copy-PublishOutput -PublishDir $ModernPublishDir -TargetDir $OutputDir
    Write-Host "Modern viewer staged next to the x86 sibling -> $(Join-Path $OutputDir $ExeName)"
} else {
    Write-Host "Skipping modern viewer publish (-SkipModern)."
}

# BUILD-EVIDENCE rule 2: every exe this run produced must carry $BuildVersion - read out of the
# exe's own version resource, since a stale exe agrees with anything that reads the source. It
# judges only what THIS run built (a -Skip* leaves the previous exe in place on purpose) and runs
# before anything is deployed, so a mismatched exe never reaches a work folder.
$builtExes = @($SingleFileExe)
if (-not $SkipModern)    { $builtExes += $ModernExe }
if (-not $SkipCompanion) { $builtExes += $CompanionExe }
if ($ExchangeBuilt)      { $builtExes += $ExchangeExe }   # judged by its PE version resource
& (Join-Path $SolutionDir "tools\Test-ArtifactVersion.ps1") -Expect $BuildVersion -Path $builtExes
if ($LASTEXITCODE -ne 0) {
    Write-Host "The built exes do not all carry $BuildVersion - nothing was deployed." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Copy the .NET 10 x64 mainline exe next to the x86 viewer in a deploy target,
# together with the loose natives published beside it (see Copy-PublishOutput).
# No support trees: a lone exe downloads the codecs/OCR it needs into
# %LOCALAPPDATA% on first use, exactly like the x86 single-file viewer does.
function Deploy-ModernExe([string]$TargetDir) {
    if ($SkipModern -or -not (Test-Path $ModernExe)) { return }
    Copy-PublishOutput -PublishDir $ModernPublishDir -TargetDir $TargetDir
    Write-Host "Deployed modern x64 viewer -> $(Join-Path $TargetDir $ExeName)"
}

# A running viewer locks its own exe, so replacing it needs it stopped first.
# Both names are checked: the two exes are one app (shared mutex/settings).
#
# Only a viewer running from somewhere this build WRITES is stopped. The user's
# installed copy under Program Files is not ours to close: this build never touches
# that path, so killing it buys nothing and takes away the window they were using -
# which is exactly what it did until now, by matching on process name alone.
function Stop-Viewers {
    $ownedDirs = @($SolutionDir) + @($Destinations)
    foreach ($name in @("FastMediaSorter_LITE", "FastMediaSorter_x86")) {
        foreach ($p in (Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            $path = $null
            try { $path = $p.Path } catch { }   # a process we may not query is not ours
            if (-not $path) { continue }
            $ours = $false
            foreach ($dir in $ownedDirs) {
                if ($dir -and $path.StartsWith((Join-Path $dir ''), [StringComparison]::OrdinalIgnoreCase)) {
                    $ours = $true; break
                }
            }
            if (-not $ours) {
                Write-Host "Leaving $name alone - it runs from $path, which this build does not write."
                continue
            }
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
Deploy-Exchange $OutputDir
Deploy-Exchange $SingleFileDir

# Specification 270: no ImageSharp in what is about to be deployed, and every native the image
# backend needs is in place - judged on the artifacts, since a clean package list says nothing
# about a file an earlier build left behind. MSBuild never removes a file it no longer produces,
# so the net48 project's retired WEBP library is taken out of bin\Release by name first. A
# -SkipModern / -SkipCompanion run leaves the previous exe in place on purpose, which this check
# would judge as if it were new, so it runs only when this run built all three programs.
foreach ($retired in 'SixLabors.ImageSharp.dll', 'SixLabors.ImageSharp.xml') {
    $stale = Join-Path $OutputDir $retired
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force; Write-Host "Removed the retired $retired from bin\Release" }
}
if (-not $SkipModern -and -not $SkipCompanion) {
    & (Join-Path $SolutionDir "tools\Test-ImageBackendArtifacts.ps1") -Path $OutputDir -AllowRollbackCopies
    if ($LASTEXITCODE -ne 0) {
        Write-Host "bin\Release does not hold the image backend specification 270 requires - nothing was deployed." -ForegroundColor Red
        exit $LASTEXITCODE
    }
} else {
    Write-Host "Image backend artifact check not run: a -Skip* build leaves an earlier exe in bin\Release." -ForegroundColor Yellow
}
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
    Deploy-Exchange $Destination       # fms-exchange-server.exe -> <dest>\exchange\
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

# --- installer package ------------------------------------------------------
# The distributable end of a local build: bin\Release is already the distribution
# shape, so tools\Build-Installer.ps1 runs with -SkipBuild and only re-stages that
# tree, bundles the OCR payload and compiles dist\..-setup.exe. It still does its
# own `dotnet publish` of the modern viewer and the Companion, because the staged
# tree must carry exes stamped with the version passed below.
#
# This is packaging, not publishing: no v* tag, no upload, nothing billable - the
# same wall between "сборка" and "релиз" as everywhere else (docs/guides/BUILD_AND_RELEASE.md).
if (-not $SkipInstaller) {
    # The one stamp this build passed to msbuild and to both publishes (see
    # $BuildVersion above), so the package is named after the exes it carries.
    $installerVersion = $BuildVersion

    # HASHTABLE splatting, never an array of "-Switch" strings: splatting an ARRAY
    # passes its elements as POSITIONAL values, so "-SkipBuild" would silently land
    # in the first positional parameter (-Version) - which then rebuilt the solution
    # with /p:ReleaseVersion=-SkipBuild and died on BC36962 ("version string does not
    # conform"), after Rebuild had already wiped bin\Release. Named binding only.
    $installerArgs = @{ SkipBuild = $true; Version = $installerVersion }
    if ($NoClean)              { $installerArgs.NoClean     = $true }
    if ($InstallerIncludeBest) { $installerArgs.IncludeBest = $true }
    if ($InstallerSkipOcr)     { $installerArgs.SkipOcr     = $true }
    if ($IncludeExchange)      { $installerArgs.IncludeExchange = $true }   # in by default; -SkipExchange leaves it out

    Write-Host ""
    Write-Host "Packaging the offline installer (version $installerVersion).."
    try {
        & (Join-Path $SolutionDir "tools\Build-Installer.ps1") @installerArgs
        if ($LASTEXITCODE -ne 0) { throw "tools\Build-Installer.ps1 exited with code $LASTEXITCODE." }
    } catch {
        # Loud, not a warning: a skipped packaging step leaves a stale dist\ setup.exe
        # sitting next to a fresh build, which is exactly how an old package gets shipped.
        Write-Error "Installer packaging failed: $($_.Exception.Message)"
        exit 1
    }
} else {
    Write-Host "Skipping installer packaging (-SkipInstaller)."
}

# --- SERVER edition installer -----------------------------------------------
# The optional always-on variant: the same worker, hosted by the Windows SCM
# instead of by an interactive session, so folders stay reachable from boot with
# nobody signed in (SPECIFICATION_SHARE_SYSTEM_SERVICE.md).
#
# Built here rather than only in CI for one reason: it must never lag behind the
# regular installer. The two setup.exe files share the vendored worker and the
# elevated helper script, and a dist\ holding a fresh User package next to a
# months-old Server package is how a machine ends up with a service running a
# worker that does not match its console.
#
# -SkipBuild reuses a Share Manager built by THIS run: the one the User installer
# step just staged (Build-ServerInstaller prefers it), else the one this script
# published into bin\Release. Neither exists when -SkipCompanion AND -SkipInstaller
# are both given - bin\Release then holds whatever an earlier build left there - so
# in that case the Server step publishes the Share Manager itself instead.
# Still packaging, never publishing: no v* tag, no upload.
if (-not $SkipServerInstaller) {
    $serverVersion = $BuildVersion

    Write-Host ""
    Write-Host "Packaging the Server edition installer (version $serverVersion).."
    try {
        # Hashtable splatting, never an array of "-Switch" strings - see the note on
        # $installerArgs above for what an array does to the first positional param.
        # It also has to go through a VARIABLE: an inline @{..} is a hashtable VALUE
        # bound positionally, not a splat, so it would land in -Version.
        $serverArgs = @{ Version = $serverVersion }
        if (-not ($SkipCompanion -and $SkipInstaller)) { $serverArgs.SkipBuild = $true }
        if ($IncludeExchange) { $serverArgs.IncludeExchange = $true }   # in by default; -SkipExchange leaves it out
        & (Join-Path $SolutionDir "tools\Build-ServerInstaller.ps1") @serverArgs
        if ($LASTEXITCODE -ne 0) { throw "tools\Build-ServerInstaller.ps1 exited with code $LASTEXITCODE." }
    } catch {
        Write-Error "Server installer packaging failed: $($_.Exception.Message)"
        exit 1
    }
} else {
    Write-Host "Skipping Server edition installer (-SkipServerInstaller)."
}

# Open the freshly built x64 viewer with the adjacent runtime trees and sidecars.
# Never launch a previous build when this run skipped publishing the modern viewer.
if (-not $SkipLaunch -and -not $SkipModern) {
    $ViewerToLaunch = Join-Path $OutputDir $ExeName
    try {
        if (-not (Test-Path -LiteralPath $ViewerToLaunch -PathType Leaf)) {
            throw "Viewer exe not found: $ViewerToLaunch"
        }
        Write-Host "Launching modern viewer for testing: $ViewerToLaunch"
        Start-Process -FilePath $ViewerToLaunch -WorkingDirectory $OutputDir -ErrorAction Stop
    } catch {
        Write-Error "Viewer launch failed: $($_.Exception.Message)"
        exit 1
    }
} else {
    Write-Host "Skipping viewer launch (-SkipLaunch or -SkipModern)."
}
