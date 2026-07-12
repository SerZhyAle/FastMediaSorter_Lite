# Best-effort graceful stop of a running companion worker (fms-share-worker.exe),
# so Setup/Uninstall can replace or remove its exe. Mirrors build.ps1's
# Stop-ShareWorker: ask the worker to stop over its control pipe first (releases
# the SFTP server + UPnP port mapping cleanly), then force-kill any survivor.
# Never throws and always exits 0 - a hiccup here must not fail the whole install.
$ErrorActionPreference = "SilentlyContinue"

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

    Get-Process -Name "fms-share-worker" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

exit 0
