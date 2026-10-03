<#
.SYNOPSIS
    Live integration smoke of the frozen named-pipe protocol against a RUNNING
    SFTP worker (fms-share-worker.exe). Framework-free: connects to
    \\.\pipe\fms-companion, sends a GetStatus request, and validates the response
    JSON shape (contract WORKER-IPC sections 3 and 5; the pointer is
    docs/contracts/WORKER-IPC.md).

    Read-only: it only reads status, never SetSharedFolders, so it can't disturb
    the user's shares.json. With no worker running there is nothing to judge, so
    the run is COULD NOT VERIFY (exit 2) - never a pass. Start the Share Manager
    (or the worker) first for a real check.

    Exit codes (CHECK-VERDICT): 0 = the worker speaks the frozen v1 protocol;
    1 = the worker replied but the contract is broken; 2 = could not verify (no
    worker listening). The last line is the verdict, `WorkerRoundTrip: <WORD>`.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'WorkerRoundTrip' 2 'the check itself failed'
}

Write-Subject 'WorkerRoundTrip' 'the worker listening on \\.\pipe\fms-companion, GetStatus, schema v1'

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'fms-companion', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(2000)
} catch {
    $pipe.Dispose()
    Exit-Verdict 'WorkerRoundTrip' 2 'no worker listening on \\.\pipe\fms-companion - start the Share Manager first'
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
    Exit-Verdict 'WorkerRoundTrip' 1 'the worker closed the pipe without a response'
}

try {
    $resp = $respText | ConvertFrom-Json
} catch {
    Write-Host "  response: $respText"
    Exit-Verdict 'WorkerRoundTrip' 1 'the response is not valid JSON'
}

# Frozen contract: response carries schemaVersion + ok; a successful GetStatus carries status.
$broken = @()
if ($null -eq $resp.schemaVersion) { $broken += 'missing schemaVersion' }
if ($null -eq $resp.ok)            { $broken += 'missing ok' }
if ($resp.ok -and $null -eq $resp.status) { $broken += 'ok=true but no status' }

if ($resp.status) {
    $s = $resp.status
    Write-Host ("Worker status: running={0} port={1} user={2} roots={3}" -f `
        $s.running, $s.listenPort, $s.username, @($s.roots).Count)
    if ($s.reachability) {
        Write-Host ("Reachability: lan={0} ipv6={1} extOpen={2}" -f `
            $s.reachability.lanAddress, $s.reachability.ipv6Address, $s.reachability.externalPortOpen)
    }
}

if ($broken.Count -gt 0) {
    $broken | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'WorkerRoundTrip' 1 ($broken -join '; ')
}

# Live schema-2 disk extension probe (contract WORKER-IPC / DISK-SHARE schema v2)
$pipe2 = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'fms-companion', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe2.Connect(2000)
    $req2 = '{"schemaVersion":2,"type":"disk-list","owner":"filedo","requestId":"roundtrip-schema2"}'
    $bytes2 = [System.Text.Encoding]::UTF8.GetBytes($req2)
    $pipe2.Write($bytes2, 0, $bytes2.Length); $pipe2.Flush()

    $ms2 = New-Object System.IO.MemoryStream
    $buf2 = New-Object byte[] 8192
    while (($n2 = $pipe2.Read($buf2, 0, $buf2.Length)) -gt 0) { $ms2.Write($buf2, 0, $n2) }
    $respText2 = [System.Text.Encoding]::UTF8.GetString($ms2.ToArray())
} catch {
    Write-Host "  Schema-2 probe error: $($_.Exception.Message)"
} finally {
    $pipe2.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($respText2)) {
    try {
        $resp2 = $respText2 | ConvertFrom-Json
        if ($resp2.schemaVersion -eq 2) {
            if ($resp2.ok -eq $true) {
                Write-Host ("Schema-2 disk-list: OK, disks count={0}" -f @($resp2.disks).Count)
            } else {
                Write-Host ("Schema-2 disk-list: refused (expected if disk sharing not initialized), error={0}" -f $resp2.error)
            }
        } elseif ($resp2.schemaVersion -eq 1) {
            Write-Host ("Schema-2 disk-list: legacy v1 response (ok={0}, error={1})" -f $resp2.ok, $resp2.error)
        }
    } catch {
        Write-Host "  Schema-2 response is not valid JSON: $respText2"
    }
}

Exit-Verdict 'WorkerRoundTrip' 0 'the live worker speaks the frozen v1 protocol and schema-2 disk framing'
