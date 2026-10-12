<#
.SYNOPSIS
    The CONFIGURED half of ticket 204 (dual mode): a throw-away Media Exchange Server deployment on
    loopback, and the two scenarios of 204 section 2 run against it. Hand-run at each gate opening
    (177 T-5) and on the test host beside the hand rows of 182 and 187.

.DESCRIPTION
    204 section 3 item 4 (146's fixture, re-aimed) and acceptance 3: the CONFIGURED run shows the new
    behaviour and the UNCONFIGURED run beside it shows none. What it does, in order:

      1. builds or takes fms-exchange-server.exe (default: exchange-server\build.ps1 into a scratch
         folder; pass -ServerExe to take one) and exchange-probe (178's scripted client; -ProbeExe);
      2. creates a throw-away data directory and a config of its own, sets the listen address to
         127.0.0.1:<port> (a free loopback port, -ListenPort to pin one) and a small third-party port
         range. The public endpoint is set to THIS machine's own address (or -EndpointHost): the
         server refuses a loopback endpoint ("no other device could reach it"), and the endpoint is
         what turns the tunnel, relay and cast features on in `welcome`. It is a fact of this machine
         and nothing dials it - every stream the probe opens goes to the loopback listener; without a
         usable address the endpoint is skipped and the probe runs with --skip tunnel,relay,cast,
         saying so on its verdict line;
      3. creates a boot account (serve refuses to start with no account, Q161-1), starts `serve`,
         then creates the fixture account WITH THE SERVER RUNNING through the admin channel
         (`account add <login> --generate`): the generated password is printed once by the CLI, is
         kept in memory only and never appears in the facts, the console or a file;
      4. prints the facts as JSON: version, listen, port range, public endpoint, fingerprint, login,
         the account count, the probe's verdict - never the password;
      5. the CONFIGURED scenario: exchange-probe enrolls with that password and runs the whole wire
         against the deployment - hello, publish, list, pairing, tunnels, relay, cast, revoke - and
         must answer PASS. It revokes every device it enrolled, so the account is left as it was.
         This is the wire-level proof of the new behaviour; the UI rows of a configured client (the
         dialogs of 187, the account group of 180) are the hand rows of 182/187, run by a person
         against this or the owner's deployment;
      6. the UNCONFIGURED scenario: the Share Manager (and, with -ViewerExe, the viewer) runs on a
         CLEAN profile BESIDE the live server, and the scriptable rows of 204 section 2 must hold:
         no outbound connection (in particular none to the server), no listening or UDP endpoint, no
         child process, no exchange value name in the settings key, no new file under
         %LOCALAPPDATA%\SZA (no exchange folder, no token file), no exchange word in its own log.
         The clean profile and the mutex refusal are the same discipline as ExchangeBaseline.ps1
         (the settings key is exported, deleted and put back byte for byte; a program whose
         single-instance mutex is held - the owner's tray copy, any viewer - is recorded as not run
         and makes the verdict COULD NOT VERIFY, never a pass);
      7. it stops the server cleanly (`stop`, the same path as Ctrl+C) and deletes the throw-away
         data directory when everything passed; on any other verdict the directory is kept and its
         path printed. -StopAfter keeps serving N extra seconds first (a standing deployment for
         the hand rows).

    Nothing here installs anything, opens a firewall rule, elevates or leaves the loopback.

.PARAMETER ServerExe
    Take an existing fms-exchange-server.exe instead of building one.
.PARAMETER ProbeExe
    Take an existing exchange-probe.exe instead of building one.
.PARAMETER Login
    The fixture account's login. Default 'fixture'.
.PARAMETER ShareManagerExe
    The Share Manager for the unconfigured run (started with --tray). Default: a scratch Debug build.
.PARAMETER ViewerExe
    Optional: also run the viewer on a clean profile beside the server (never built here - the
    viewer's unconfigured rows are ExchangeBaseline.ps1's; give the exe to run it too).
.PARAMETER Seconds
    How long the unconfigured program runs. Default 12.
.PARAMETER ListenPort
    Pin the loopback listen port. Default: a free one found at run time.
.PARAMETER EndpointHost
    The host of the public endpoint (the switch that turns the tunnel, relay and cast features on).
    Default: this machine's own non-loopback IPv4; without one the endpoint is skipped and the probe
    runs with --skip tunnel,relay,cast.
.PARAMETER StopAfter
    Keep serving this many seconds after the scenarios (for the hand rows). Default 0.
.PARAMETER OutJson
    Also write the facts JSON to this file.

.OUTPUTS
    CHECK-VERDICT: 0 PASS (the deployment answers the whole wire and the unconfigured program(s)
    beside it show none of section 2), 1 FAIL (the probe failed, or an unconfigured program showed
    an exchange row), 2 COULD NOT VERIFY (the server or the probe could not be built or started, or
    a program could not be run - its mutex held, no dotnet, no exe). The last line is
    `ExchangeFixture: <WORD> (..)`. Hand-run (tools\check-placement.jsonl).
#>
[CmdletBinding()]
param(
    [string]$ServerExe = '',
    [string]$ProbeExe = '',
    [string]$Login = 'fixture',
    [string]$ShareManagerExe = '',
    [string]$ViewerExe = '',
    [int]$Seconds = 12,
    [int]$ListenPort = 0,
    [string]$EndpointHost = '',
    [int]$StopAfter = 0,
    [string]$OutJson = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
$check = 'ExchangeFixture'

# The scratch root of the CLIENT half must not contain the word "exchange": the client's own log names
# its exe folder, and 177's record already met that false positive (renamed fms-dualmode-* there).
$scratchRoot = Join-Path ([IO.Path]::GetTempPath()) ('fms-fixture-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$dataDir     = Join-Path ([IO.Path]::GetTempPath()) ('fms-exchange-fixture-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$configPath  = Join-Path $dataDir 'exchange.json'

$script:serverProc  = $null
$script:serverExe   = $ServerExe
$script:configPath  = $configPath

function Stop-Server {
    # `stop` asks the console server to stop cleanly (the Ctrl+C path). A kill is only the fallback.
    if ($script:serverProc -and -not $script:serverProc.HasExited) {
        try { & $script:serverExe stop --config "$($script:configPath)" 2>$null | Out-Null } catch { }
        if (-not $script:serverProc.WaitForExit(10000)) {
            try { $script:serverProc.Kill() } catch { }
            try { $script:serverProc.WaitForExit(5000) } catch { }
        }
    }
}

trap {
    Stop-Server
    Write-Host "  $($_.Exception.Message)"
    Write-Host "  at $($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor DarkGray
    if (Test-Path -LiteralPath $dataDir) { Write-Host "  the deployment's data directory is kept for reading: $dataDir" -ForegroundColor DarkGray }
    Exit-Verdict $check 2 'the harness itself failed'
}

Write-Subject $check "the configured half of the dual-mode regression (204 section 3 item 4); $(Get-TreeLabel $repoRoot)"

# --- the server and the probe: build or take ------------------------------------------------------

function Resolve-Go64 {
    # The `go` first on PATH here can be the 32-bit toolchain - a 64-bit Go is resolved explicitly
    # (the same guard exchange-server\build.ps1 applies), and GOARCH=amd64 is pinned below.
    $candidates = @()
    foreach ($root in @($env:ProgramW6432, $env:ProgramFiles)) {
        if ($root) {
            $p = Join-Path $root 'Go\bin\go.exe'
            if (Test-Path -LiteralPath $p) { $candidates += $p }
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

New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
$builtHere = @()

if (-not $ServerExe) {
    $serverDir = Join-Path $scratchRoot 'server'
    New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
    Write-Host "  building a scratch copy of fms-exchange-server.exe .." -ForegroundColor Cyan
    & (Join-Path $repoRoot 'exchange-server\build.ps1') -OutDir $serverDir -Version (Get-Date -Format 'yy.M.d.HHmm') | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Exit-Verdict $check 2 "exchange-server\build.ps1 could not build the server (exit $LASTEXITCODE; exit 2 of it means: no 64-bit Go on this machine - install Go or pass -ServerExe)"
    }
    $ServerExe = Join-Path $serverDir 'fms-exchange-server.exe'
    if (-not (Test-Path -LiteralPath $ServerExe)) { Exit-Verdict $check 2 'the server build reported success but produced no fms-exchange-server.exe' }
    $script:serverExe = $ServerExe
    $builtHere += (Split-Path -Parent $ServerExe)
}
if (-not $ProbeExe) {
    $go = Resolve-Go64
    if (-not $go) { Exit-Verdict $check 2 'no 64-bit Go found - pass -ProbeExe (or install Go) to run the configured scenario' }
    $ProbeExe = Join-Path $scratchRoot 'exchange-probe.exe'
    Write-Host "  building a scratch copy of exchange-probe.exe .." -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot 'exchange-server')
    $script:goArchBefore = $env:GOARCH
    try {
        $env:GOARCH = 'amd64'
        & $go build -o $ProbeExe ./cmd/exchange-probe 2>&1 | Out-Null
        $code = $LASTEXITCODE
    } finally {
        if ($null -ne $script:goArchBefore) { $env:GOARCH = $script:goArchBefore } else { Remove-Item Env:GOARCH -ErrorAction SilentlyContinue }
        Pop-Location
    }
    if ($code -ne 0 -or -not (Test-Path -LiteralPath $ProbeExe)) { Exit-Verdict $check 2 'go build of ./cmd/exchange-probe failed (the probe is the configured scenario - pass -ProbeExe to take one)' }
    $builtHere += $scratchRoot
}
$serverVersion = (& $ServerExe version).Trim()
Write-Host "  server $serverVersion, probe ready." -ForegroundColor DarkGray

# --- the throw-away deployment -------------------------------------------------------------------

if ($ListenPort -le 0) {
    $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $l.Start()
    $ListenPort = ([Net.IPEndPoint]$l.LocalEndpoint).Port
    $l.Stop()
}
$listen    = "127.0.0.1:$ListenPort"
$portRange = "{0}-{1}" -f ($ListenPort + 10), ($ListenPort + 19)   # a small third-party range, clear of the listen port
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

# The public endpoint: the server refuses a loopback one, and it is only the switch that turns the
# tunnel, relay and cast features on in `welcome` - every stream the probe opens dials the loopback
# listener above. This machine's own address is a fact of the machine and nothing dials it; without
# one the parts that need the endpoint are skipped and the probe says so on its verdict line.
if (-not $EndpointHost) {
    $EndpointHost = [string](@(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -First 1).IPAddress)
}
$probeSkip = @()
if ($EndpointHost) {
    $publicEndpoint = "{0}:{1}" -f $EndpointHost, $ListenPort
} else {
    $publicEndpoint = ''
    $probeSkip = @('--skip', 'tunnel,relay,cast')
    Write-Host "  no non-loopback address on this machine: the public endpoint is skipped and the probe skips tunnel, relay and cast." -ForegroundColor Yellow
}

# serve refuses to start with no account (Q161-1), so a boot account exists first; the account the
# scenarios use is created AFTER the server is up - through the admin channel, the operator's path.
$null = & $ServerExe account add fixture-boot --generate --config "$configPath" 2>&1
if ($LASTEXITCODE -ne 0) { Exit-Verdict $check 2 "account add (boot) failed with exit $LASTEXITCODE" }

$setSteps = @(
    @{ Op = 'set-listen';     Arg = $listen },
    @{ Op = 'set-port-range'; Arg = $portRange })
if ($publicEndpoint) { $setSteps += @{ Op = 'set-endpoint'; Arg = $publicEndpoint } }
foreach ($step in $setSteps) {
    $null = & $ServerExe $step.Op $step.Arg --config "$configPath" 2>&1
    if ($LASTEXITCODE -ne 0) { Exit-Verdict $check 2 "$($step.Op) $($step.Arg) failed with exit $LASTEXITCODE" }
}

$serveOut = Join-Path $scratchRoot 'serve-out.txt'
$serveErr = Join-Path $scratchRoot 'serve-err.txt'
Write-Host "  starting serve on $listen (port range $portRange) .." -ForegroundColor Cyan
$script:serverProc = Start-Process -FilePath $ServerExe -ArgumentList ('serve --config "{0}"' -f $configPath) `
    -RedirectStandardOutput $serveOut -RedirectStandardError $serveErr -PassThru -WindowStyle Hidden
$ready = $false
for ($i = 0; $i -lt 40 -and -not $script:serverProc.HasExited; $i++) {
    $listening = @(Get-NetTCPConnection -LocalPort $ListenPort -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $script:serverProc.Id })
    if ($listening.Count -gt 0) { $ready = $true; break }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) {
    $tail = @((Get-Content -LiteralPath $serveOut -ErrorAction SilentlyContinue) + (Get-Content -LiteralPath $serveErr -ErrorAction SilentlyContinue)) |
        Where-Object { $_ } | Select-Object -Last 5
    $tail | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    Stop-Server
    Exit-Verdict $check 2 "serve did not listen on $listen within 10 s (the tail of its output is above; the data directory is kept: $dataDir)"
}

# the fixture account, through the admin channel, with the server running
$generated = (& $ServerExe account add $Login --generate --config "$configPath" 2>$null | Where-Object { $_ })
if ($LASTEXITCODE -ne 0 -or -not $generated) {
    Stop-Server
    Exit-Verdict $check 2 "account add $Login --generate through the admin channel failed with exit $LASTEXITCODE"
}
$password = [string](@($generated)[0])   # printed once by the CLI, kept in memory only, never printed here

$inspect = (& $ServerExe inspect --config "$configPath" 2>$null | ConvertFrom-Json)
if (-not $inspect.fingerprint) {
    Stop-Server
    Exit-Verdict $check 2 'inspect printed no fingerprint - the deployment cannot be pinned'
}
Write-Host "  serving: fingerprint $($inspect.fingerprint), accounts $($inspect.accounts), data dir $dataDir" -ForegroundColor DarkGray

# --- the CONFIGURED scenario: the probe runs the whole wire ---------------------------------------

$probeErr = Join-Path $scratchRoot 'probe-err.txt'
Write-Host "  the configured scenario: exchange-probe enrolls with the generated password and runs the wire .." -ForegroundColor Cyan
$probeLine = ''
$probeCode = 0
try {
    $probeOut = $password | & $ProbeExe (@('--server', $listen, '--fingerprint', [string]$inspect.fingerprint,
        '--login', $Login, '--password-stdin', '--timeout', '120s') + $probeSkip) 2> $probeErr
    $probeCode = $LASTEXITCODE
    $probeLine = [string](@($probeOut | Where-Object { $_ } | Select-Object -Last 1))
} catch {
    $probeCode = 2
    $probeLine = "the probe could not be started: $($_.Exception.Message)"
}
$configuredOk = ($probeCode -eq 0)
Write-Host "  $probeLine" -ForegroundColor $(if ($configuredOk) { 'Green' } else { 'Red' })

# --- the UNCONFIGURED scenario: the client programs beside the live server -----------------------

$hiveSubKey = 'Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$hiveReg    = "HKCU\$hiveSubKey"
$hivePs     = "HKCU:\$hiveSubKey"
$runPs      = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$szaLocal   = Join-Path $env:LOCALAPPDATA 'SZA'

# What may never appear on a machine with no account: any value of the client side of the exchange.
$forbiddenNames = '(?i)(^|\\)(Exchange_(DeviceId|Account|Login|Server|Endpoint|Pin|Agent|Features|Paired|Enrolled)|Exchange.?Client|FmsX_|DeviceExchange)'

# The Share Manager's own mutex blocks a second copy, and the clean profile empties the settings key
# a running viewer or tray copy also uses, so the capture refuses to run beside any of them.
$blockedNames = @('FastMediaSorter_LITE', 'FastMediaSorter_x86', 'FastMediaSorterCompanion')

function Get-ValueNames([string]$PsPath) {
    $names = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $PsPath)) { return , @() }
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($PsPath)
    while ($stack.Count -gt 0) {
        $p = $stack.Pop()
        $key = Get-Item -LiteralPath $p
        $rel = $p.Substring($PsPath.Length).TrimStart('\')
        foreach ($n in $key.GetValueNames()) {
            $label = if ($n -eq '') { '(default)' } else { $n }
            $names.Add($(if ($rel) { "$rel\$label" } else { $label }))
        }
        foreach ($sub in $key.GetSubKeyNames()) { $stack.Push((Join-Path $p $sub)) }
    }
    return , @($names | Sort-Object -Unique)
}

function Export-Hive([string]$File) {
    if (Test-Path -LiteralPath $File) { Remove-Item -LiteralPath $File -Force }
    & reg.exe export $hiveReg $File /y 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    return (Test-Path -LiteralPath $File) -and ((Get-Item -LiteralPath $File).Length -gt 0)
}

function Get-FileNames([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return , @() }
    $base = (Resolve-Path -LiteralPath $Root).Path
    return , @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName.Substring($base.Length).TrimStart('\') } | Sort-Object)
}

function Get-Descendants([int]$RootPid) {
    $found = @()
    $pending = @($RootPid)
    while ($pending.Count -gt 0) {
        $parent = $pending[0]
        $pending = @($pending | Select-Object -Skip 1)
        foreach ($c in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $parent" -ErrorAction SilentlyContinue)) {
            $found += [pscustomobject]@{ Pid = [int]$c.ProcessId; Name = [string]$c.Name }
            $pending += [int]$c.ProcessId
        }
    }
    return ,$found
}

function Get-Endpoints([int]$ProcId) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($c in @(Get-NetTCPConnection -OwningProcess $ProcId -ErrorAction SilentlyContinue)) {
        $list.Add("tcp $($c.State) local=$($c.LocalAddress):$($c.LocalPort) remote=$($c.RemoteAddress):$($c.RemotePort)")
    }
    foreach ($u in @(Get-NetUDPEndpoint -OwningProcess $ProcId -ErrorAction SilentlyContinue)) {
        $list.Add("udp local=$($u.LocalAddress):$($u.LocalPort)")
    }
    return , @($list)
}

# One program on a clean profile: the scriptable rows of 204 section 2, observed - not trusted.
function Invoke-UnconfiguredRun([string]$Name, [string]$Exe, [string[]]$ProgramArgs, [bool]$CloseWindow) {
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $blockedNames -contains $_.ProcessName })
    if ($running.Count -gt 0) {
        $who = ($running | ForEach-Object { "$($_.ProcessName) (pid $($_.Id))" }) -join ', '
        return [ordered]@{ program = $Name; ran = $false; reason = "$who is already running - the clean profile would empty the settings key it uses" }
    }
    if (-not (Test-Path -LiteralPath $Exe)) {
        return [ordered]@{ program = $Name; ran = $false; reason = "no executable at $Exe" }
    }

    $work = Join-Path $scratchRoot "run-$Name"
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $backup = Join-Path $work 'hive-backup.reg'
    $hiveExisted = Test-Path -LiteralPath $hivePs
    if ($hiveExisted -and -not (Export-Hive $backup)) { throw "could not export $hiveReg before the run - nothing was changed" }
    $runBefore = Get-ValueNames $runPs
    $filesBefore = Get-FileNames $szaLocal
    $proc = $null
    $restoreFailed = $false
    try {
        if ($hiveExisted) { Remove-Item -LiteralPath $hivePs -Recurse -Force }   # the clean profile
        $namesBefore = Get-ValueNames $hivePs

        $startInfo = @{ FilePath = $Exe; PassThru = $true; WorkingDirectory = (Split-Path -Parent $Exe) }
        if ($ProgramArgs -and $ProgramArgs.Count -gt 0) { $startInfo.ArgumentList = $ProgramArgs }
        $proc = Start-Process @startInfo
        $exeVersion = (Get-Item -LiteralPath $Exe).VersionInfo.FileVersion

        $connections = 0; $listeners = 0
        $children = New-Object System.Collections.Generic.HashSet[string]
        $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
        while ([DateTime]::UtcNow -lt $deadline -and -not $proc.HasExited) {
            $now = Get-Endpoints $proc.Id
            $l = @($now | Where-Object { $_ -match ' Listen |^udp ' }).Count
            $c = @($now | Where-Object { $_ -match '^tcp (?!Listen)' }).Count
            if ($l -gt $listeners) { $listeners = $l }
            if ($c -gt $connections) { $connections = $c }
            foreach ($ch in (Get-Descendants $proc.Id)) { [void]$children.Add($ch.Name) }
            Start-Sleep -Milliseconds 500
        }
        if ($proc.HasExited) { throw "$Name exited by itself (code $($proc.ExitCode))" }

        $closedGracefully = $false
        if ($CloseWindow) {
            [void]$proc.CloseMainWindow()
            $closedGracefully = $proc.WaitForExit(15000)
        }
        if (-not $proc.HasExited) { $proc.Kill(); [void]$proc.WaitForExit(10000) }
        Start-Sleep -Milliseconds 400   # settings are flushed on the way out

        $added = @(Get-ValueNames $hivePs | Where-Object { $namesBefore -notcontains $_ })
        $filesAfter = Get-FileNames $szaLocal
        $logText = ''
        $logPath = Join-Path (Split-Path -Parent $Exe) $(if ($Name -like 'viewer*') { 'current.log' } else { 'companion.log' })
        if (Test-Path -LiteralPath $logPath) { $logText = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue }
        if ($null -eq $logText) { $logText = '' }

        return [ordered]@{
            program          = $Name
            ran              = $true
            exeFileVersion   = $exeVersion
            profile          = 'clean (the settings key was absent for the run)'
            secondsObserved  = $Seconds
            closedGracefully = $closedGracefully
            rows             = [ordered]@{
                connections               = $connections        # section 2 Network: nothing is sent
                listeningOrUdpEndpoints   = $listeners          # section 2 Listening: no new port
                childProcesses            = @($children | Sort-Object)  # section 2 Processes
                newFilesUnderSza          = @($filesAfter | Where-Object { $filesBefore -notcontains $_ })
                registryAdded             = $added              # non-exchange names are recorded, not judged
                forbiddenExchangeValues   = @($added | Where-Object { $_ -match $forbiddenNames })
                # The Share Manager's "State:" header (HostingReport, 182 L-3) always names the exchange SERVER
                # service (`exchangeService=absent`) - that field is cut before the match, as in ExchangeBaseline.
                logMentionsExchange       = [bool](($logText -replace '(?i)\bexchangeService=\S+', '') -match '(?i)exchange|fmsx|deviceToken|pairing')
            }
        }
    }
    finally {
        if ($proc -and -not $proc.HasExited) { try { $proc.Kill() } catch { } }
        try {
            foreach ($n in @(Get-ValueNames $runPs | Where-Object { $runBefore -notcontains $_ })) {
                Remove-ItemProperty -LiteralPath $runPs -Name $n -ErrorAction SilentlyContinue
            }
        } catch { }
        if (Test-Path -LiteralPath $hivePs) { Remove-Item -LiteralPath $hivePs -Recurse -Force -ErrorAction SilentlyContinue }
        if ($hiveExisted) {
            & reg.exe import $backup 2>&1 | Out-Null
            $verify = Join-Path $work 'hive-verify.reg'
            if ((Export-Hive $verify) -and ((Get-Content -LiteralPath $backup -Raw) -eq (Get-Content -LiteralPath $verify -Raw))) {
                Write-Host "  $hiveReg restored and verified identical." -ForegroundColor DarkGray
            } else {
                Write-Host "  !! $hiveReg could NOT be verified identical after the restore. The backup is $backup - run: reg import `"$backup`"" -ForegroundColor Red
                $script:restoreFailed = $true
            }
        }
    }
}

$script:restoreFailed = $false
$programs = [ordered]@{}

# the Share Manager is the default client (the program whose device agent would touch the network);
# a scratch Debug copy is built when no exe is given, as ExchangeBaseline does
if (-not $ShareManagerExe) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $programs['sharemanager'] = [ordered]@{ program = 'sharemanager'; ran = $false; reason = 'dotnet is not on PATH and no -ShareManagerExe was given' }
    } else {
        $out = Join-Path $scratchRoot 'sharemanager'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        Write-Host "  building a scratch Debug copy of the Share Manager .." -ForegroundColor Cyan
        $log = & dotnet build (Join-Path $repoRoot 'src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj') -c Debug -o $out --nologo -v:q 2>&1
        if ($LASTEXITCODE -ne 0) {
            $log | Select-Object -Last 15 | ForEach-Object { Write-Host "    $_" }
            throw 'the scratch build of the Share Manager failed'
        }
        $ShareManagerExe = Join-Path $out 'FastMediaSorterCompanion.exe'
    }
}
if ($ShareManagerExe) {
    Write-Host "  the unconfigured scenario: the Share Manager (--tray) on a clean profile for $Seconds s, beside the server .." -ForegroundColor Cyan
    $programs['sharemanager'] = Invoke-UnconfiguredRun 'sharemanager' $ShareManagerExe @('--tray') $false
}
if ($ViewerExe) {
    Write-Host "  the unconfigured scenario: the viewer on a clean profile for $Seconds s, beside the server .." -ForegroundColor Cyan
    $programs['viewer'] = Invoke-UnconfiguredRun 'viewer' $ViewerExe @() $true
}

if ($script:restoreFailed) {
    Stop-Server
    Exit-Verdict $check 2 'a settings key could not be verified identical after the run - see the message above'
}

# --- the facts, as JSON ---------------------------------------------------------------------------

function Get-SwitchState([string]$Name) {
    # What the developer switches said during the run (177: the unconfigured rows must hold either way).
    $parts = foreach ($scope in @('Process', 'User', 'Machine')) {
        $v = $null
        try { $v = [Environment]::GetEnvironmentVariable($Name, $scope) } catch { }
        "$scope=" + [bool]($v -eq '1')
    }
    return ($parts -join ' ')
}

$accountAfter = (& $ServerExe account list --json --config "$configPath" 2>$null | ConvertFrom-Json)
$facts = [ordered]@{
    takenAt     = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm') + ' UTC'
    server      = [ordered]@{
        version        = $serverVersion
        exe            = $ServerExe
        listen         = $listen
        portRange      = $portRange
        publicEndpoint = $publicEndpoint   # this machine's own address; nothing dials it - see the header
        fingerprint    = [string]$inspect.fingerprint
        dataDir        = $dataDir
        accounts       = [int]$inspect.accounts
    }
    account     = [ordered]@{
        login                      = $Login
        createdThroughAdminChannel = $true
        accountsOnServer           = @($accountAfter).Count
        # no password, no token: neither ever leaves the process above
    }
    scenarios   = [ordered]@{
        configured   = [ordered]@{ probe = $probeLine; passed = $configuredOk }
        unconfigured = $programs
    }
    switches    = [ordered]@{
        FMS_EXCHANGE_CLIENT = Get-SwitchState 'FMS_EXCHANGE_CLIENT'
        FMS_LIVE_BROADCAST  = Get-SwitchState 'FMS_LIVE_BROADCAST'
    }
}
$factsJson = ($facts | ConvertTo-Json -Depth 8)
Write-Host ""
Write-Host $factsJson
if ($OutJson) { [IO.File]::WriteAllText($OutJson, $factsJson + "`n", (New-Object Text.UTF8Encoding($false))) }

# --- stop cleanly, then the verdict ----------------------------------------------------------------

if ($StopAfter -gt 0) {
    Write-Host "  -StopAfter: serving for $StopAfter more second(s) for the hand rows .." -ForegroundColor Cyan
    Start-Sleep -Seconds $StopAfter
}
Stop-Server
Write-Host "  serve stopped cleanly." -ForegroundColor DarkGray

$problems = [System.Collections.Generic.List[string]]::new()
if (-not $configuredOk) { $problems.Add("the configured scenario failed: $probeLine") }
$notRun = @()
foreach ($k in $programs.Keys) {
    $p = $programs[$k]
    if (-not $p.ran) { $notRun += "$k ($($p.reason))"; continue }
    $r = $p.rows
    if ($r.connections -gt 0)        { $problems.Add("$($p.program): $($r.connections) outbound connection(s) beside the server - section 2 Network") }
    if ($r.listeningOrUdpEndpoints -gt 0) { $problems.Add("$($p.program): $($r.listeningOrUdpEndpoints) listening/UDP endpoint(s) - section 2 Listening") }
    if (@($r.childProcesses).Count -gt 0) { $problems.Add("$($p.program): started a child process: $(@($r.childProcesses) -join ', ') - section 2 Processes") }
    if (@($r.newFilesUnderSza).Count -gt 0) { $problems.Add("$($p.program): new file(s) under %LOCALAPPDATA%\SZA: $(@($r.newFilesUnderSza) -join ', ') - section 2 Files") }
    if (@($r.forbiddenExchangeValues).Count -gt 0) { $problems.Add("$($p.program): an exchange value name: $(@($r.forbiddenExchangeValues) -join ', ') - section 2 Registry") }
    if ($r.logMentionsExchange) { $problems.Add("$($p.program): its log mentions the exchange - section 2 Files") }
}
foreach ($p in $problems) { Write-Host "  DIFFERENCE: $p" -ForegroundColor Red }

if ($problems.Count -gt 0) {
    Write-Host "  the deployment's data directory is kept for reading: $dataDir" -ForegroundColor DarkGray
    Exit-Verdict $check 1 "$($problems.Count) row(s) of section 2 broken; the configured scenario $(if ($configuredOk) { 'answered' } else { 'failed' })"
}
if ($notRun.Count -gt 0) {
    Write-Host "  the deployment's data directory is kept for reading: $dataDir" -ForegroundColor DarkGray
    Exit-Verdict $check 2 ("the deployment answered (probe PASS) but the unconfigured half could not be run: " + ($notRun -join '; '))
}

# everything passed: remove what this run created (nothing else)
try { Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction Stop } catch { Write-Host "  the data directory could not be removed: $dataDir" -ForegroundColor Yellow }
Exit-Verdict $check 0 "the configured deployment answers the whole wire (probe PASS, fingerprint $($inspect.fingerprint)) and the unconfigured $(@($programs.Keys) -join ' and ') beside it shows none of section 2"
