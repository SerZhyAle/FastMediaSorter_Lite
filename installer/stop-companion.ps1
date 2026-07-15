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
# the uninstaller. LITE itself is deliberately left to Inno's AppMutex, which closes it
# gracefully so it still saves its settings on exit.
#
# Never throws and always exits 0 - a hiccup here must not fail the whole install.
$ErrorActionPreference = "SilentlyContinue"

# Use Process.Kill() (not Stop-Process) so no confirmation prompt can ever surface
# under Setup, and wait briefly so the file handle is released before Setup copies.
function Stop-Quiet([string]$procName) {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); $_.WaitForExit(3000) } catch { }
    }
}

# 1. Ask the worker to stop over its control pipe first (releases the SFTP server +
#    UPnP port mapping cleanly) before anything is force-killed.
if (Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue) {
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
Stop-Quiet "FastMediaSorterCompanion"

# 3. Force-kill any worker that survived the graceful stop.
Stop-Quiet "fms-share-worker"
Start-Sleep -Milliseconds 300

exit 0
