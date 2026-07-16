<#
.SYNOPSIS
    Fast local test loop: silently kill every running instance, rebuild EVERYTHING
    (LITE + Share Manager companion + worker payload) via build.ps1, then relaunch
    the fresh exes locally so a change can be eyeballed immediately.

.DESCRIPTION
    "reinstall" here = "replace the local build and restart it" - the quick dev
    cycle, NOT a run of the real Inno setup.exe. It is a thin wrapper around
    build.ps1 (same rebuild + deploy to the work folders) with two extras:
      1. BEFORE building - stop LITE, the Share Manager and the Go worker (build.ps1
         stops the worker/companion but not LITE; a LITE still running from a deploy
         target would lock its own exe and fail the copy).
      2. AFTER building - Start-Process both exes from bin\Release so they run at once.

    Everything is silent (no confirmation prompts): a graceful StopServer over the
    worker's control pipe first (releases the SFTP server + UPnP port map cleanly),
    then a force-kill of any survivors.

    This never creates or pushes a v* tag - it is a LOCAL "сборка", not a release.
    For the real offline installer, use tools\Build-Installer.ps1.

.PARAMETER NoClean
    Passed through to build.ps1 (skip tools\Clean-Build.ps1 housekeeping).

.PARAMETER SkipCompanion
    Passed through to build.ps1 (build LITE only; Share Manager is not rebuilt).

.PARAMETER NoLaunch
    Rebuild + redeploy but do not relaunch anything.

.PARAMETER Tray
    Launch the Share Manager silently to the tray (--tray) instead of opening its
    window. Default: open the window so you can test the UI right away.

.EXAMPLE
    .\reinstall.ps1
    .\reinstall.ps1 -NoClean          # faster: skip the stage/bin/temp housekeeping
    .\reinstall.ps1 -SkipCompanion    # LITE only
#>
param(
    [switch]$NoClean,
    [switch]$SkipCompanion,
    [switch]$NoLaunch,
    [switch]$Tray
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- 1. silently stop any running instances --------------------------------------

# Best-effort graceful worker shutdown over the named-pipe control channel (mirrors
# build.ps1 Stop-ShareWorker): lets it release the SFTP server + UPnP port map before
# we kill it. Ignored if no worker is up or the pipe refuses.
try {
    if (Get-Process -Name 'fms-share-worker' -ErrorAction SilentlyContinue) {
        Write-Host "Asking the Android-share worker to stop cleanly.." -ForegroundColor DarkCyan
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'fms-companion', [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(1000)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"type":"StopServer"}')
        $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
        $pipe.Dispose()
        Start-Sleep -Milliseconds 300
    }
} catch { }

# Force-kill (no prompts). $_.Kill() avoids the Stop-Process confirmation dialog.
function Stop-Quiet([string]$procName) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  killing $($_.ProcessName) (pid $($_.Id)).." -ForegroundColor DarkGray
        try { $_.Kill(); $_.WaitForExit(3000) } catch { }
    }
}

Write-Host "Stopping running instances (LITE, Share Manager, worker).." -ForegroundColor Cyan
Stop-Quiet 'FastMediaSorter_LITE'
Stop-Quiet 'FastMediaSorter_x86'
Stop-Quiet 'FastMediaSorterCompanion'
Stop-Quiet 'fms-share-worker'
Start-Sleep -Milliseconds 300

# --- 2. rebuild + redeploy everything (reuse build.ps1) --------------------------

$buildArgs = @()
if ($NoClean)       { $buildArgs += '-NoClean' }
if ($SkipCompanion) { $buildArgs += '-SkipCompanion' }

Write-Host "Rebuilding via build.ps1 $($buildArgs -join ' ').." -ForegroundColor Cyan
& (Join-Path $root 'build.ps1') @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "build.ps1 failed (exit code $LASTEXITCODE) - not launching."
    exit $LASTEXITCODE
}

# --- 3. relaunch locally from bin\Release ----------------------------------------

if ($NoLaunch) {
    Write-Host "Build complete. -NoLaunch set - nothing started." -ForegroundColor Green
    return
}

$runDir       = Join-Path $root 'bin\Release'
$liteExe      = Join-Path $runDir 'FastMediaSorter_LITE.exe'
$companionExe = Join-Path $runDir 'FastMediaSorterCompanion.exe'

if (Test-Path $liteExe) {
    Write-Host "Launching Fast Media Sorter (LITE): $liteExe" -ForegroundColor Green
    Start-Process -FilePath $liteExe -WorkingDirectory $runDir
} else {
    Write-Warning "LITE exe not found at $liteExe"
}

if (Test-Path $companionExe) {
    $mode = if ($Tray) { 'tray' } else { 'window' }
    Write-Host "Launching Share Manager ($mode): $companionExe" -ForegroundColor Green
    if ($Tray) {
        Start-Process -FilePath $companionExe -WorkingDirectory $runDir -ArgumentList '--tray'
    } else {
        Start-Process -FilePath $companionExe -WorkingDirectory $runDir
    }
} else {
    Write-Warning "Share Manager exe not found at $companionExe (built with -SkipCompanion?)"
}

Write-Host "Reinstall + relaunch complete." -ForegroundColor Cyan
