<#
.SYNOPSIS
    Holds the Media Exchange Server's conformance evidence (ticket 205) against the code: the generated
    vectors are byte for byte what the generator produces now, and the probe passes against a server
    started from the binary this repository builds.

.DESCRIPTION
    Two checks, one verdict:

      1. exchange-vectors -check  - testdata\vectors\frames-v2.json and records-v1.json are regenerated
                                    in memory by running the REAL server against a scripted client, and
                                    compared byte for byte with the files on disk; PROVENANCE.txt must
                                    name the hashes of the files that are there (a hand edit fails).
      2. exchange-probe           - fms-exchange-server and exchange-probe are built from the module, a
                                    server is started on loopback with a fresh data directory and one
                                    account, and the probe runs its whole flow against it (enroll, hello,
                                    publish, list, pairing, subscribe, tunnels by resourceId and shareId,
                                    relay upload and listen, cast, revoke) and must answer PASS.

    Loopback only: no service, no firewall rule, no elevation, no installer. The data directory is a
    temporary folder and is removed, the server process is stopped. Needs a 64-bit Go; without one the
    check is COULD NOT VERIFY, never a pass (-SkipExchangeGo of Run-AllTests reports it SKIPPED).

.NOTES
    Exit codes (CHECK-VERDICT): 0 both passed; 1 the vectors differ from the generator, or the probe
    failed, or the module does not build; 2 could not verify (no Go, no module, the server did not
    start). The last line is `Test-ExchangeConformance: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-ExchangeConformance' 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
$module = Join-Path $root 'exchange-server'
Write-Subject 'Test-ExchangeConformance' "exchange-server\testdata\vectors against the generator, and exchange-probe against a local server; $(Get-TreeLabel $root)"

function Resolve-Go64 {
    $candidates = @()
    foreach ($base in @($env:ProgramW6432, $env:ProgramFiles)) {
        if ($base) {
            $p = Join-Path $base 'Go\bin\go.exe'
            if (Test-Path $p) { $candidates += $p }
        }
    }
    foreach ($c in @(Get-Command go -All -ErrorAction SilentlyContinue)) { $candidates += $c.Source }
    foreach ($c in ($candidates | Select-Object -Unique)) {
        $arch = $null
        try { $arch = (& $c env GOHOSTARCH 2>$null) } catch { }
        if ($arch -eq 'amd64') { return $c }
    }
    return $null
}

if (-not (Test-Path -LiteralPath (Join-Path $module 'go.mod'))) { Exit-Verdict 'Test-ExchangeConformance' 2 'exchange-server\go.mod not found' }
$go = Resolve-Go64
if (-not $go) { Exit-Verdict 'Test-ExchangeConformance' 2 'no 64-bit go.exe found' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('fms-exchange-conformance-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$saved = @{}
foreach ($k in 'GOARCH', 'CGO_ENABLED', 'GOFLAGS', 'FMS_EXCHANGE_DATA_DIR') { $saved[$k] = [Environment]::GetEnvironmentVariable($k, 'Process') }
$server = $null
$script:code = 0
$script:detail = ''
# the steps run in a script block so that a failed step can `return` out of them and still reach the cleanup below
& {
Push-Location $module
try {
    # Ordinary checks must not invoke Git indirectly through Go's automatic VCS stamping.
    $env:GOARCH = 'amd64'; $env:CGO_ENABLED = '0'; $env:GOFLAGS = '-buildvcs=false'

    # --- 1: the vectors ---
    Write-Host '--- exchange-vectors -check' -ForegroundColor DarkCyan
    $vLog = @(& $go run ./cmd/exchange-vectors -check -out testdata/vectors 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
    $vCode = $LASTEXITCODE
    $vLine = (@($vLog | Where-Object { $_ -match '^exchange-vectors: ' }) | Select-Object -Last 1)
    if (-not $vLine) { $vLine = "exchange-vectors: (no verdict line; exit $vCode)" }
    if ($vCode -ne 0) {
        $script:code = if ($vCode -eq 1) { 1 } else { 2 }
        $script:detail = "vectors: $($vLine -replace '^exchange-vectors: ', '')"
        return
    }

    # --- 2: the binaries ---
    Write-Host '--- build fms-exchange-server and exchange-probe' -ForegroundColor DarkCyan
    $serverExe = Join-Path $work 'fms-exchange-server.exe'
    $probeExe = Join-Path $work 'exchange-probe.exe'
    $bLog = @(& $go build -o $serverExe ./cmd/fms-exchange-server 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { $bLog | ForEach-Object { Write-Host "  $_" }; $script:code = 1; $script:detail = 'fms-exchange-server does not build'; return }
    $bLog = @(& $go build -o $probeExe ./cmd/exchange-probe 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { $bLog | ForEach-Object { Write-Host "  $_" }; $script:code = 1; $script:detail = 'exchange-probe does not build'; return }

    # --- 3: a fresh deployment on loopback: one account, a free port, a public endpoint that is only a name ---
    $data = Join-Path $work 'data'
    $env:FMS_EXCHANGE_DATA_DIR = $data
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $addr = "127.0.0.1:$port"
    $password = (@(& $serverExe account add probe --generate 2>&1) | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $password) { $script:code = 2; $script:detail = 'could not create the probe account'; return }
    $password = "$password".Trim()
    & $serverExe set-listen $addr | Out-Null
    if ($LASTEXITCODE -ne 0) { $script:code = 2; $script:detail = 'could not set the listen address'; return }
    # the endpoint a server TELLS its clients (a documentation address; the probe dials the address it was given):
    # without one the server hides tunnels and the relay, which the probe is there to exercise
    & $serverExe set-endpoint 203.0.113.5:22022 | Out-Null
    if ($LASTEXITCODE -ne 0) { $script:code = 2; $script:detail = 'could not set the public endpoint'; return }

    $outFile = Join-Path $work 'server.out'
    $errFile = Join-Path $work 'server.err'
    $server = Start-Process -FilePath $serverExe -ArgumentList 'serve' -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru -WindowStyle Hidden
    $fingerprint = $null
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not $fingerprint) {
        Start-Sleep -Milliseconds 200
        if ($server.HasExited) { break }
        if (Test-Path -LiteralPath $outFile) {
            $m = Select-String -LiteralPath $outFile -Pattern '^tls fingerprint (SHA256:\S+)' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($m) { $fingerprint = $m.Matches[0].Groups[1].Value }
        }
    }
    if (-not $fingerprint) {
        $script:code = 2; $script:detail = 'the server did not start on loopback (no fingerprint line)'
        if (Test-Path -LiteralPath $errFile) { Get-Content -LiteralPath $errFile -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" } }
        return
    }

    # --- 4: the probe ---
    Write-Host '--- exchange-probe against the local server' -ForegroundColor DarkCyan
    $pLog = @($password | & $probeExe --server $addr --fingerprint $fingerprint --login probe --password-stdin 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
    $pCode = $LASTEXITCODE
    $pLine = (@($pLog | Where-Object { $_ -match '^exchange-probe: ' }) | Select-Object -Last 1)
    if (-not $pLine) { $pLine = "exchange-probe: (no verdict line; exit $pCode)" }
    if ($pCode -ne 0 -or $pLine -notmatch '^exchange-probe: PASS') {
        $script:code = if ($pCode -eq 2) { 2 } else { 1 }
        $script:detail = "probe: $($pLine -replace '^exchange-probe: ', '')"
        return
    }
    $script:detail = "vectors byte for byte; probe $($pLine -replace '^exchange-probe: ', '') against a local server"
}
finally {
    Pop-Location
    if ($server -and -not $server.HasExited) { try { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue; $server.WaitForExit(5000) | Out-Null } catch { } }
    foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k], 'Process') }
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
}

Exit-Verdict 'Test-ExchangeConformance' $script:code $script:detail
