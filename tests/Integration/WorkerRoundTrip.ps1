<#
.SYNOPSIS
    Live integration smoke of the frozen named-pipe protocol against a RUNNING
    SFTP worker (fms-share-worker.exe). Framework-free: connects to
    \\.\pipe\fms-companion, sends a GetStatus request, and validates the response
    JSON shape (SPECIFICATION_ANDROID_FOLDER_SHARE.md Appendix A).

    Read-only: it only reads status, never SetSharedFolders, so it can't disturb
    the user's shares.json. If no worker is running it SKIPS (exit 0) rather than
    failing - start the Share Manager (or the worker) first for a real check.

    Exit: 0 = pass or skip (no worker); 1 = worker replied but the contract is broken.
#>
$ErrorActionPreference = 'Stop'

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'fms-companion', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(2000)
} catch {
    Write-Host "SKIP: no worker listening on \\.\pipe\fms-companion (start the Share Manager first)." -ForegroundColor Yellow
    exit 0
}

try {
    $req = '{"schemaVersion":1,"type":"GetStatus"}'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($req)
    $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()

    $ms = New-Object System.IO.MemoryStream
    $buf = New-Object byte[] 8192
    while (($n = $pipe.Read($buf, 0, $buf.Length)) -gt 0) { $ms.Write($buf, 0, $n) }
    $respText = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
} finally {
    $pipe.Dispose()
}

if ([string]::IsNullOrWhiteSpace($respText)) {
    Write-Host "FAIL: worker closed the pipe without a response." -ForegroundColor Red
    exit 1
}

try {
    $resp = $respText | ConvertFrom-Json
} catch {
    Write-Host "FAIL: response is not valid JSON: $respText" -ForegroundColor Red
    exit 1
}

# Frozen contract: response carries schemaVersion + ok; a successful GetStatus carries status.
$ok = $true
if ($null -eq $resp.schemaVersion) { Write-Host "FAIL: missing schemaVersion"; $ok = $false }
if ($null -eq $resp.ok)            { Write-Host "FAIL: missing ok"; $ok = $false }
if ($resp.ok -and $null -eq $resp.status) { Write-Host "FAIL: ok=true but no status"; $ok = $false }

if ($resp.status) {
    $s = $resp.status
    Write-Host ("Worker status: running={0} port={1} user={2} roots={3}" -f `
        $s.running, $s.listenPort, $s.username, @($s.roots).Count)
    if ($s.reachability) {
        Write-Host ("Reachability: lan={0} ipv6={1} extOpen={2}" -f `
            $s.reachability.lanAddress, $s.reachability.ipv6Address, $s.reachability.externalPortOpen)
    }
}

if ($ok) { Write-Host "PASS: live worker speaks the frozen v1 protocol." -ForegroundColor Green; exit 0 }
exit 1
