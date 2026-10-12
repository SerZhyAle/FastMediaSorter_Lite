<#
.SYNOPSIS
    The "today" baseline of ticket 204 (dual mode): a machine with no exchange account and no Google
    sign-in must be unable to tell that the exchange client exists. Captures what the CURRENT build of
    the viewer and the Share Manager does on a clean profile, or compares a fresh capture with the
    committed capture.

.DESCRIPTION
    204 section 3 item 1. For each program that can be started here it records, on a CLEAN profile:

      * the registry: the value NAMES (never the data) under SZA\FastMediaSorter before, while it runs and
        after it closed, and the names that appeared; the value names of the per-user Run key before/after;
      * the files created under %LOCALAPPDATA%\SZA (relative names only);
      * every TCP connection / UDP endpoint the process owned at any moment of the run (sampled twice a
        second), listening or not, and the number of each;
      * the child processes it started;
      * whether the program's own log mentions the exchange at all.

    Capture mode writes <OutDir>\viewer-baseline.json (and sharemanager-baseline.json when the Share Manager
    could be run) - the files the committed baseline under tests\Lite.Tests\TestData\dual-mode is made of.
    Compare mode captures again and fails when the new run shows anything the committed baseline does not:
    a listener, a connection, a child process, a new file, a new registry value name, or any exchange value.

    CLEAN PROFILE means the SZA\FastMediaSorter settings key is exported, deleted, the program run, and the
    key put back (delete + import, byte-compared with a fresh export) in a finally block. The Run key and
    %LOCALAPPDATA% files are never deleted except what the run itself added to the Run key. The harness
    REFUSES a program whose single-instance mutex is already held: a second copy would forward its launch
    to the running one and exit, so nothing of the new build would run. A viewer run is refused by another
    viewer; a Share Manager run by another Share Manager (the owner's tray copy always is one) and by a
    viewer, which empties the key the tray program shares. A refused program is recorded as not captured
    with the reason - never as a pass.

    The scratch builds are Debug copies made into a temporary folder when no exe is given: Release strips
    Debug.WriteLine, and the repository's bin\Release is never touched.

    Nothing here installs anything, opens a firewall rule, elevates or uses the network.

.PARAMETER Mode
    Capture (default) or Compare.
.PARAMETER ViewerExe
    The modern viewer to run. Default: a scratch Debug build of src\Modern\FastMediaSorter.Modern.vbproj.
.PARAMETER ShareManagerExe
    The Share Manager to run (started with --tray, closed by killing it: it has no window to close).
    Default: a scratch Debug build of src\FastMediaSorterCompanion.
.PARAMETER X86ViewerExe
    The net48 x86 fallback viewer to run as a third program ('viewer-x86', file viewer-x86-baseline.json); not
    built here (build src\FastMediaSorter.vbproj with the real VS MSBuild into a scratch folder, never bin\Release).
    Optional: without it the x86 half is not run. It shares the viewer's mutex and settings key.
.PARAMETER Seconds
    How long each program runs before it is closed. Default 12.
.PARAMETER OutDir
    Capture: where the JSON files go. Default tests\Lite.Tests\TestData\dual-mode.
.PARAMETER BaselineDir
    Compare: the committed baseline. Default the same folder.
.PARAMETER WriteProvenance
    Capture: also (re)write PROVENANCE.txt in OutDir (SHA-256 of every other file there, which the suites
    check first).
.OUTPUTS
    CHECK-VERDICT: 0 PASS (Compare: nothing beyond the baseline / Capture: every program captured),
    1 FAIL (Compare found a difference), 2 COULD NOT VERIFY (nothing could be run, or a program refused).
    The last line is `ExchangeBaseline: <WORD> (..)`. Hand-run (tools\check-placement.jsonl).
#>
[CmdletBinding()]
param(
    [ValidateSet('Capture', 'Compare', 'Provenance')][string]$Mode = 'Capture',
    [string]$ViewerExe = '',
    [string]$ShareManagerExe = '',
    [string]$X86ViewerExe = '',
    [int]$Seconds = 12,
    [string]$OutDir = '',
    [string]$BaselineDir = '',
    [switch]$WriteProvenance
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
$check = 'ExchangeBaseline'
trap {
    Write-Host "  $($_.Exception.Message)"
    Write-Host "  at $($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor DarkGray
    Exit-Verdict $check 2 'the harness itself failed'
}

$defaultDir = Join-Path $repoRoot 'tests\Lite.Tests\TestData\dual-mode'
if (-not $OutDir) { $OutDir = $defaultDir }
if (-not $BaselineDir) { $BaselineDir = $defaultDir }

$hiveSubKey = 'Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$hiveReg    = "HKCU\$hiveSubKey"
$hivePs     = "HKCU:\$hiveSubKey"
$runPs      = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$szaLocal   = Join-Path $env:LOCALAPPDATA 'SZA'

# What may never appear on a machine with no account: any value of the client side of the exchange.
# (The server-side Exchange_* preferences of the Share Manager's own group are not written by a start.)
$forbiddenNames = '(?i)(^|\\)(Exchange_(DeviceId|Account|Login|Server|Endpoint|Pin|Agent|Features|Paired|Enrolled)|Exchange.?Client|FmsX_|DeviceExchange)'

# What may not run beside a capture. The VIEWER's single-instance mutex is its own, so only another viewer
# blocks a viewer run; the Share Manager's mutex is its own too, and it also reads the same settings key a
# viewer run empties, so it is blocked by another Share Manager AND by a viewer. A running Share Manager does
# not block a VIEWER run: the key is put back byte for byte, and what that costs a tray program that happens
# to read it inside those seconds is a default read, never a write.
$viewerNames = @('FastMediaSorter_LITE', 'FastMediaSorter_x86')
$blockedBy = @{
    viewer       = $viewerNames
    'viewer-x86' = $viewerNames
    sharemanager = ($viewerNames + @('FastMediaSorterCompanion'))
}

function Get-RunningOf([string[]]$Names) {
    return ,@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $Names -contains $_.ProcessName })
}

# ---- registry -----------------------------------------------------------------------------

function Get-ValueNames([string]$PsPath) {
    # Value names only, recursively ("subkey\name"); never a datum.
    $names = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $PsPath)) { return ,@() }
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
    return ,@($names | Sort-Object -Unique)
}

function Export-Hive([string]$File) {
    if (Test-Path -LiteralPath $File) { Remove-Item -LiteralPath $File -Force }
    & reg.exe export $hiveReg $File /y 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    return (Test-Path -LiteralPath $File) -and ((Get-Item -LiteralPath $File).Length -gt 0)
}

function Get-FileNames([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return ,@() }
    $base = (Resolve-Path -LiteralPath $Root).Path
    return ,@(Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName.Substring($base.Length).TrimStart('\') } | Sort-Object)
}

# ---- sampling -------------------------------------------------------------------------------

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
    return ,@($list)
}

# A local port the OS chose differs from run to run, so what is recorded about an endpoint is its KIND:
# protocol, state, whether the local address is loopback, and the remote address and port. A listener's
# own port is a number of the machine, not a fact of the program.
function ConvertTo-EndpointKind([string]$Line) {
    $m = [regex]::Match($Line, '^(tcp|udp)(?: (\w+))? local=([^ ]+?):(\d+)(?: remote=([^ ]+?):(\d+))?$')
    if (-not $m.Success) { return $Line }
    $proto = $m.Groups[1].Value
    $state = $m.Groups[2].Value
    $localAddr = $m.Groups[3].Value
    $scope = if ($localAddr -in '127.0.0.1', '::1') { 'loopback' } elseif ($localAddr -in '0.0.0.0', '::') { 'any' } else { 'address' }
    $remote = if ($m.Groups[5].Success -and $m.Groups[5].Value -notin '0.0.0.0', '::') { "$($m.Groups[5].Value):$($m.Groups[6].Value)" } else { '-' }
    return "$proto $state $scope remote=$remote"
}

# ---- scratch builds ---------------------------------------------------------------------------

$scratchRoot = Join-Path ([IO.Path]::GetTempPath()) ('fms-dualmode-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

function Build-Scratch([string]$Project, [string]$Name, [string]$ExeName) {
    $out = Join-Path $scratchRoot $Name
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Write-Host "  building a scratch Debug copy of $Name .." -ForegroundColor Cyan
    $log = & dotnet build (Join-Path $repoRoot $Project) -c Debug -o $out --nologo -v:q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $log | Select-Object -Last 15 | ForEach-Object { Write-Host "    $_" }
        throw "the scratch build of $Name failed"
    }
    $exe = Join-Path $out $ExeName
    if (-not (Test-Path -LiteralPath $exe)) { throw "the scratch build of $Name produced no $ExeName" }
    return $exe
}

# ---- one program --------------------------------------------------------------------------------

function Invoke-Capture([string]$Name, [string]$Exe, [string[]]$ProgramArgs, [bool]$CloseWindow) {
    $running = Get-RunningOf $blockedBy[$Name]
    if ($running.Count -gt 0) {
        $who = ($running | ForEach-Object { "$($_.ProcessName) (pid $($_.Id))" }) -join ', '
        return [ordered]@{
            program = $Name; captured = $false
            reason  = "$who is already running: its single-instance mutex is machine-session wide (a second copy would forward its launch to it and exit) and the settings key is shared"
        }
    }
    if (-not (Test-Path -LiteralPath $Exe)) {
        return [ordered]@{ program = $Name; captured = $false; reason = "no executable at $Exe" }
    }

    $work = Join-Path $scratchRoot "run-$Name"
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $backup = Join-Path $work 'hive-backup.reg'
    $hiveExisted = Test-Path -LiteralPath $hivePs
    if ($hiveExisted) {
        if (-not (Export-Hive $backup)) { throw "could not export $hiveReg before the run - nothing was changed" }
    }
    $runBefore = Get-ValueNames $runPs
    $filesBefore = Get-FileNames $szaLocal
    $proc = $null
    $result = $null
    try {
        if ($hiveExisted) { Remove-Item -LiteralPath $hivePs -Recurse -Force }   # the clean profile
        $namesBefore = Get-ValueNames $hivePs

        $startInfo = @{ FilePath = $Exe; PassThru = $true; WorkingDirectory = (Split-Path -Parent $Exe) }
        if ($ProgramArgs -and $ProgramArgs.Count -gt 0) { $startInfo.ArgumentList = $ProgramArgs }
        $proc = Start-Process @startInfo
        $exeVersion = (Get-Item -LiteralPath $Exe).VersionInfo.FileVersion

        $endpoints = New-Object 'System.Collections.Generic.HashSet[string]'
        $children  = New-Object 'System.Collections.Generic.HashSet[string]'
        $rawSeen   = New-Object 'System.Collections.Generic.HashSet[string]'   # console diagnosis only, never written to the capture
        $maxListen = 0; $maxConn = 0
        $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
        while ([DateTime]::UtcNow -lt $deadline -and -not $proc.HasExited) {
            $now = Get-Endpoints $proc.Id
            $listen = @($now | Where-Object { $_ -match ' Listen |^udp ' }).Count
            $conn = @($now | Where-Object { $_ -match '^tcp (?!Listen)' }).Count
            if ($listen -gt $maxListen) { $maxListen = $listen }
            if ($conn -gt $maxConn) { $maxConn = $conn }
            foreach ($e in $now) { [void]$endpoints.Add((ConvertTo-EndpointKind $e)); [void]$rawSeen.Add($e) }
            foreach ($c in (Get-Descendants $proc.Id)) { [void]$children.Add($c.Name) }
            Start-Sleep -Milliseconds 500
        }
        foreach ($r in @($rawSeen | Select-Object -First 6)) { Write-Host "    endpoint seen ($Name): $r" -ForegroundColor DarkGray }
        if ($proc.HasExited) { throw "$Name exited by itself after $([int]((Get-Date) - $proc.StartTime).TotalSeconds) s (code $($proc.ExitCode))" }

        $namesRunning = Get-ValueNames $hivePs

        $closedGracefully = $false
        if ($CloseWindow) {
            [void]$proc.CloseMainWindow()
            $closedGracefully = $proc.WaitForExit(15000)
        }
        if (-not $proc.HasExited) { $proc.Kill(); [void]$proc.WaitForExit(10000) }
        Start-Sleep -Milliseconds 400   # settings are flushed on the way out

        $namesAfter = Get-ValueNames $hivePs
        $runAfter = Get-ValueNames $runPs
        $filesAfter = Get-FileNames $szaLocal
        $logText = ''
        $logPath = Join-Path (Split-Path -Parent $Exe) $(if ($Name -like 'viewer*') { 'current.log' } else { 'companion.log' })
        if (Test-Path -LiteralPath $logPath) { $logText = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue }
        if ($null -eq $logText) { $logText = '' }

        $added = @($namesAfter | Where-Object { $namesBefore -notcontains $_ })
        $result = [ordered]@{
            program          = $Name
            captured         = $true
            exeFileVersion   = $exeVersion
            profile          = 'clean (the settings key was absent for the run)'
            secondsObserved  = $Seconds
            closedGracefully = $closedGracefully
            registry         = [ordered]@{
                key                  = "HKCU\$hiveSubKey"
                valueNamesBefore     = @($namesBefore)
                valueNamesWhileRunning = @($namesRunning)
                valueNamesAfterClose = @($namesAfter)
                added                = $added
                runKeyAdded          = @($runAfter | Where-Object { $runBefore -notcontains $_ })
            }
            files            = [ordered]@{
                root    = '%LOCALAPPDATA%\SZA'
                created = @($filesAfter | Where-Object { $filesBefore -notcontains $_ })
            }
            network          = [ordered]@{
                maxListeningOrUdpEndpoints = $maxListen
                maxConnections             = $maxConn
                endpointKinds              = @($endpoints | Sort-Object)
            }
            childProcesses   = @($children | Sort-Object)
            log              = [ordered]@{
                file           = (Split-Path -Leaf $logPath)
                present        = (Test-Path -LiteralPath $logPath)
                lines          = @($logText -split "`n" | Where-Object { $_.Trim().Length -gt 0 }).Count
                # The Share Manager's "State:" header (HostingReport, ticket 182 L-3) always names the exchange SERVER service
                # (`exchangeService=absent`); it reads the service manager and is not the device client, so that one field
                # is cut before the match. Any other mention still counts.
                mentionsExchange = [bool](($logText -replace '(?i)\bexchangeService=\S+', '') -match '(?i)exchange|fmsx|deviceToken|pairing')
            }
        }
        $badNames = @($added + $namesRunning | Where-Object { $_ -match $forbiddenNames } | Sort-Object -Unique)
        $result.registry.forbiddenValueNames = $badNames
    }
    finally {
        if ($proc -and -not $proc.HasExited) { try { $proc.Kill() } catch { } }
        # put the run key back: only the values THIS run added are removed
        try {
            foreach ($n in @(Get-ValueNames $runPs | Where-Object { $runBefore -notcontains $_ })) {
                Remove-ItemProperty -LiteralPath $runPs -Name $n -ErrorAction SilentlyContinue
            }
        } catch { }
        # put the settings key back, byte for byte
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
    return $result
}

function Write-Provenance([string]$Dir) {
    $lines = @(
        '# Provenance of the dual-mode baseline (ticket 204). The suites check these hashes FIRST: a hand edit of a',
        '# fixture fails them. Regenerate: tests\Integration\ExchangeBaseline.ps1 -Mode Capture (the JSON captures),',
        '# the Companion test DualModeBaselineGenerator with FMS_WRITE_DUAL_MODE_BASELINE=<this folder> (the',
        '# reference-share files), then tests\Integration\ExchangeBaseline.ps1 -Mode Provenance (this file).',
        "# written: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm')) UTC",
        '# FILE<TAB>SHA-256')
    foreach ($f in @(Get-ChildItem -LiteralPath $Dir -File | Where-Object { $_.Name -ne 'PROVENANCE.txt' } | Sort-Object Name)) {
        $lines += "$($f.Name)`t$((Get-FileHash -Algorithm SHA256 -LiteralPath $f.FullName).Hash.ToLowerInvariant())"
    }
    [IO.File]::WriteAllText((Join-Path $Dir 'PROVENANCE.txt'), (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "  wrote PROVENANCE.txt ($(@($lines | Where-Object { $_ -notmatch '^#' }).Count) file hash(es))" -ForegroundColor Green
}
# ---- run ----------------------------------------------------------------------------------------

if ($Mode -eq 'Provenance') {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Provenance $OutDir
    Exit-Verdict $check 0 "provenance of $OutDir rewritten"
}
Write-Subject $check "$Mode of the exchange 'today' baseline - current files on disk, scratch Debug builds"
$script:restoreFailed = $false

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (-not $ViewerExe -or -not $ShareManagerExe)) {
    Exit-Verdict $check 2 'dotnet is not on PATH and no executable was given'
}
# A program that cannot be run here is not built either (a scratch build costs minutes).
if (-not $ViewerExe -and (Get-RunningOf $blockedBy['viewer']).Count -eq 0) { $ViewerExe = Build-Scratch 'src\Modern\FastMediaSorter.Modern.vbproj' 'viewer' 'FastMediaSorter_LITE.exe' }
if (-not $ShareManagerExe -and (Get-RunningOf $blockedBy['sharemanager']).Count -eq 0) { $ShareManagerExe = Build-Scratch 'src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj' 'sharemanager' 'FastMediaSorterCompanion.exe' }

$captures = [ordered]@{}
Write-Host "  running the modern viewer on a clean profile for $Seconds s .." -ForegroundColor Cyan
$captures['viewer'] = Invoke-Capture 'viewer' $ViewerExe @() $true
if ($X86ViewerExe) {
    Write-Host "  running the x86 fallback viewer on a clean profile for $Seconds s .." -ForegroundColor Cyan
    $captures['viewer-x86'] = Invoke-Capture 'viewer-x86' $X86ViewerExe @() $true
}
Write-Host "  running the Share Manager (--tray) on a clean profile for $Seconds s .." -ForegroundColor Cyan
$captures['sharemanager'] = Invoke-Capture 'sharemanager' $ShareManagerExe @('--tray') $false

if ($script:restoreFailed) { Exit-Verdict $check 2 'a settings key could not be verified identical after the run - see the message above' }

function Write-Json([string]$Path, $Object) {
    $text = ($Object | ConvertTo-Json -Depth 10) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($Path, $text + "`n", (New-Object Text.UTF8Encoding($false)))
}

$fileFor = @{ viewer = 'viewer-baseline.json'; 'viewer-x86' = 'viewer-x86-baseline.json'; sharemanager = 'sharemanager-baseline.json' }

if ($Mode -eq 'Capture') {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $notCaptured = @()
    foreach ($k in $captures.Keys) {
        $c = $captures[$k]
        if ($c.captured) {
            Write-Json (Join-Path $OutDir $fileFor[$k]) $c
            Write-Host "  wrote $($fileFor[$k]): added $(@($c.registry.added).Count) value name(s), $(@($c.files.created).Count) new file(s), $($c.network.maxListeningOrUdpEndpoints) listener(s), $($c.network.maxConnections) connection(s), children: $(@($c.childProcesses).Count)" -ForegroundColor Green
            if (@($c.registry.forbiddenValueNames).Count -gt 0 -or $c.log.mentionsExchange) {
                Exit-Verdict $check 1 "$k already shows an exchange value or an exchange log line on a clean profile - that is not a baseline of 'nothing'"
            }
        } else {
            $notCaptured += "$k ($($c.reason))"
            Write-Host "  NOT captured: $k - $($c.reason)" -ForegroundColor Magenta
        }
    }
    if ($WriteProvenance) { Write-Provenance $OutDir }
    if ($notCaptured.Count -gt 0) { Exit-Verdict $check 2 ("captured only part: " + ($notCaptured -join '; ')) }
    Exit-Verdict $check 0 "$(@($captures.Keys).Count) program(s) captured into $OutDir"
}

# ---- Compare ----------------------------------------------------------------------------------

$problems = New-Object System.Collections.Generic.List[string]
$compared = 0
$notRun = @()
foreach ($k in $captures.Keys) {
    $basePath = Join-Path $BaselineDir $fileFor[$k]
    $fresh = $captures[$k]
    if (-not (Test-Path -LiteralPath $basePath)) {
        Write-Host "  no committed baseline for $k ($($fileFor[$k])) - nothing to compare against" -ForegroundColor DarkGray
        continue
    }
    if (-not $fresh.captured) { $notRun += "$k ($($fresh.reason))"; continue }
    $compared++
    $base = Get-Content -LiteralPath $basePath -Raw | ConvertFrom-Json
    foreach ($n in @($fresh.registry.added)) {
        if (@($base.registry.added) -notcontains $n) { $problems.Add("${k}: new registry value name '$n'") }
    }
    foreach ($n in @($fresh.registry.forbiddenValueNames)) { $problems.Add("${k}: an exchange value '$n'") }
    foreach ($f in @($fresh.files.created)) {
        if (@($base.files.created) -notcontains $f) { $problems.Add("${k}: new file under %LOCALAPPDATA%\SZA: $f") }
    }
    if ($fresh.network.maxListeningOrUdpEndpoints -gt $base.network.maxListeningOrUdpEndpoints) {
        $problems.Add("${k}: $($fresh.network.maxListeningOrUdpEndpoints) listening/UDP endpoint(s), the baseline had $($base.network.maxListeningOrUdpEndpoints)")
    }
    if ($fresh.network.maxConnections -gt $base.network.maxConnections) {
        $problems.Add("${k}: $($fresh.network.maxConnections) connection(s), the baseline had $($base.network.maxConnections)")
    }
    foreach ($e in @($fresh.network.endpointKinds)) {
        if (@($base.network.endpointKinds) -notcontains $e) { $problems.Add("${k}: a new kind of endpoint: $e") }
    }
    foreach ($c in @($fresh.childProcesses)) {
        if (@($base.childProcesses) -notcontains $c) { $problems.Add("${k}: a new child process: $c") }
    }
    if ($fresh.log.mentionsExchange) { $problems.Add("${k}: its log mentions the exchange") }
}
foreach ($p in $problems) { Write-Host "  DIFFERENCE: $p" -ForegroundColor Red }
if ($problems.Count -gt 0) { Exit-Verdict $check 1 "$($problems.Count) difference(s) from the baseline" }
if ($compared -eq 0) {
    Exit-Verdict $check 2 ("nothing was compared: " + $(if ($notRun.Count) { $notRun -join '; ' } else { 'no committed baseline' }))
}
if ($notRun.Count -gt 0) { Exit-Verdict $check 2 ("$compared program(s) match the baseline; not run: " + ($notRun -join '; ')) }
Exit-Verdict $check 0 "$compared program(s) match the baseline"
