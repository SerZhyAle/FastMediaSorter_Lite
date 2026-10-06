# Best-effort stop of the Android-Share background processes so Setup/Uninstall can
# replace or remove their files: the tray-resident Companion app
# (FastMediaSorterCompanion.exe) AND its headless SFTP worker (fms-share-worker.exe).
#
# Neither can be closed by Inno's AppMutex check or the Restart Manager: the Companion
# is an ApplicationContext tray app that autostarts at logon (--tray) and does NOT exit
# on a window-close - it just returns to the tray - so a Restart Manager shutdown
# request leaves it running, holding its own exe (and keeping the worker alive). The
# worker is a windowless background process RM cannot message either. A file-replacing
# install/uninstall therefore has to terminate them here - this runs from
# PrepareToInstall (before the Restart Manager's "Closing applications" step) and from
# the uninstaller. The viewer itself is deliberately left to Inno's AppMutex, which closes it
# gracefully so it still saves its settings on exit.
#
# In the Server edition the worker is owned by the Windows SCM, not by the tray app.
# There, force-killing the process is the wrong move twice over: the configured
# recovery action restarts it right back (so the file is still locked when Setup
# copies), and the abrupt exit skips the clean release of the SFTP listener and the
# UPnP mapping. So the service is stopped through the SCM FIRST, with a bounded wait,
# and only what survives is killed.
#
# A service that is STILL running afterwards - a per-user, non-elevated Setup cannot
# stop it - is left entirely alone (ticket 054 H-04): its binary lives under
# %ProgramData%\FastMediaSorterCompanion\bin, not in the folder being replaced, so the
# install does not need it down. Sending it StopServer over the pipe used to switch the
# always-on share off until the next reboot, while the service itself kept "running".
#
# The optional Media Exchange Server (fms-exchange-server.exe, specification 133, ticket 141) is
# stopped too, under two rules that make it safe to share this one mechanism (never an
# AppMutex): only a copy that RUNS FROM <AppDir>\exchange\ - with no installation known it is
# left alone, because killing by name could take down another installation's server - and never
# the process of a Windows service. The Server edition's service (FastMediaSorterExchangeServer)
# is stopped by its own helper verb or by the next repair, not killed here: the configured
# recovery action would restart it at once and the abrupt exit skips its clean shutdown.
#
# Never throws and always exits 0 - a hiccup here must not fail the whole install.
param(
    # The installation whose files are about to be replaced or removed. When it is known
    # - passed by Setup, or this copy runs from inside it, as the uninstaller's does -
    # only processes started from THAT folder are stopped: another account's sharing,
    # from another installation, is none of this Setup's business (ticket 054 H-26).
    # Unknown, it falls back to stopping every copy by name, as it always did.
    [string]$AppDir
)
$ErrorActionPreference = "SilentlyContinue"

$serviceName = "FastMediaSorterCompanionSFTP"

if (-not $AppDir -and $PSScriptRoot) {
    foreach ($marker in @('FastMediaSorterCompanion.exe', 'FastMediaSorter_LITE.exe')) {
        if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $marker)) { $AppDir = $PSScriptRoot; break }
    }
}
$appRoot = $null
if ($AppDir) {
    try { $appRoot = [System.IO.Path]::GetFullPath($AppDir).TrimEnd('\') + '\' } catch { $appRoot = $null }
}

$isElevated = $false
try {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { }

$machineBin = $null
if ($env:ProgramData) { $machineBin = (Join-Path $env:ProgramData 'FastMediaSorterCompanion\bin').TrimEnd('\') + '\' }

function Get-ProcessPath($proc) {
    try { return $proc.Path } catch { return $null }
}

# Started from the installation being replaced? With no installation known, every copy
# counts (the historical behaviour).
function Test-FromApp($proc) {
    if (-not $appRoot) { return $true }
    $path = Get-ProcessPath $proc
    if (-not $path) { return $false }   # cannot tell - then it is not ours to stop
    return $path.StartsWith($appRoot, [StringComparison]::OrdinalIgnoreCase)
}

# The service's own worker: its staged copy under %ProgramData%.
function Test-ServiceWorker($proc) {
    if (-not $machineBin) { return $false }
    $path = Get-ProcessPath $proc
    return ($path -and $path.StartsWith($machineBin, [StringComparison]::OrdinalIgnoreCase))
}

# Use Process.Kill() (not Stop-Process) so no confirmation prompt can ever surface
# under Setup, and wait briefly so the file handle is released before Setup copies.
function Stop-Quiet([string]$procName, [bool]$spareServiceWorker) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not (Test-FromApp $_)) { return }
        if ($spareServiceWorker -and (Test-ServiceWorker $_)) { return }
        try { $_.Kill(); [void]$_.WaitForExit(3000) } catch { }
    }
}

# 0. Server edition: stop the Windows service. Skipped silently when it is not
#    installed, which is the normal User edition case - and skipped when this Setup is
#    not elevated: the stop is refused then, and waiting 30 s for a stop that was never
#    going to happen only stalled Setup.
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne "Stopped" -and $isElevated) {
    try {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        # Bounded: past this the kill below is the fallback, and Setup still proceeds.
        (Get-Service -Name $serviceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    } catch { }
}
$serviceStillUp = $false
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne "Stopped") { $serviceStillUp = $true }

# 1. Ask the worker to stop over its control pipe first (releases the SFTP server +
#    UPnP port mapping cleanly) before anything is force-killed. Never while the service
#    is still up - the pipe is the SERVICE's then - and only when a worker of this
#    installation is running, since whoever holds the pipe otherwise is not ours.
$ourWorker = @(Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue | Where-Object { Test-FromApp $_ })
if (-not $serviceStillUp -and $ourWorker.Count -gt 0) {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "fms-companion", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(1000)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"type":"StopServer"}')
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()
        $pipe.Dispose()
        Start-Sleep -Milliseconds 300
    } catch { }
}

# 2. Terminate the tray-resident Companion app (it holds its own exe and would keep
#    the worker alive / respawn it - RM cannot close it, so force it).
Stop-Quiet "FastMediaSorterCompanion" $false

# 3. Force-kill any worker that survived the graceful stop - except the running
#    service's own, which holds nothing of the folder being replaced.
Stop-Quiet "fms-share-worker" $serviceStillUp
Start-Sleep -Milliseconds 300

# 4. The exchange server, when one of THIS installation's own is running (a console instance the
#    Share Manager started). Never a service instance - see the header.
$exchangeServiceName = "FastMediaSorterExchangeServer"
$exchangeServicePid = 0
$exchangeSvc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$exchangeServiceName'" -ErrorAction SilentlyContinue
if ($exchangeSvc -and $exchangeSvc.ProcessId) { $exchangeServicePid = [int]$exchangeSvc.ProcessId }
if ($appRoot) {
    $exchangeRoot = $appRoot + 'exchange\'
    Get-Process -Name "fms-exchange-server" -ErrorAction SilentlyContinue | ForEach-Object {
        if ($exchangeServicePid -ne 0 -and $_.Id -eq $exchangeServicePid) { return }
        $path = Get-ProcessPath $_
        if (-not $path -or -not $path.StartsWith($exchangeRoot, [StringComparison]::OrdinalIgnoreCase)) { return }
        try { $_.Kill(); [void]$_.WaitForExit(3000) } catch { }
    }
}

exit 0
