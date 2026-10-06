<#
.SYNOPSIS
    Runs the automated test suites for all three programs of Fast Media Sorter & Sharing
    package and ends in one CHECK-VERDICT line.

    1. Viewer (net48 + net10)       -> tests/Lite.Tests        (dotnet test / xUnit)
    2. Share Manager (net10)        -> tests/Companion.Tests   (dotnet test / xUnit)
    3. SFTP worker (Go)             -> the fms_companion repository (go test ./...)
    3b. Exchange server (Go)        -> exchange-server\ of this repository: its unit suite (and the
                                       manual-proof tool's module) with a 64-bit Go, plus a
                                       GOOS=linux go vet (no Windows assumption outside the
                                       deployment layer)
    4. Check placement              -> tools/Test-CheckPlacement.ps1 (the registry vs its runners)

    5. Documentation registry       -> tools/Test-DocRegistry.ps1 (coverage, links, style, facts)
    5b. Product naming              -> tools/Test-ProductNaming.ps1 (no old name form without a written reason)
    6. Published site quality       -> tools/Test-SiteQuality.ps1 (DOC-EXTERNAL-QUALITY rules 1-7
                                       over every page the sitemap announces)
    6b. Site addresses held outside -> tools/Test-SiteAddresses.ps1 (every address the programs, listings and
                                       READMEs hold resolves against the page set; offline)
    6c. Third-party origins         -> tools/Test-SiteOrigins.ps1 (every outside host a published page contacts is declared,
                                       named on the privacy page and in each trust locale; offline)
    6d. Positioning and typed facts -> tools/Test-SitePositioning.ps1 (the pillars in the source's order on every declared
                                       surface, the editions, the reserved door, the language count and the platform floor; offline)

    These are UNIT/logic suites - fast, no network, no phone. Two live suites are opt-in:
    -Integration sends GetStatus to a RUNNING worker and validates the answer (read-only - it
    never starts a worker or touches its data), and -OcrScenes drives the built mainline viewer
    over the OCR overlay scenes - minutes, a window on screen, the OCR settings borrowed and put
    back - so it is never part of a default run.

    Every suite runs whatever the one before it returned, and the summary names all of them.

.PARAMETER Integration
    Also run tests/Integration/WorkerRoundTrip.ps1 (needs a running worker - start the Share
    Manager first; without one the suite is COULD NOT VERIFY, not a pass).

.PARAMETER OcrScenes
    Also run tests/Integration/OcrOverlayScenes.ps1 (needs a built bin\Release viewer and no
    viewer running; about 45 s per scene).

.PARAMETER SkipGo
    Skip the Go worker suite. It is reported as SKIPPED, never as passed.

.PARAMETER SkipExchangeGo
    Skip the exchange server's Go suite. It is reported as SKIPPED, never as passed. Without a
    64-bit Go the suite is COULD NOT VERIFY (exit 2), also never a pass.

.PARAMETER WorkerRepo
    The fms_companion checkout. Default: FMS_WORKER_REPO, else the fms_companion folder beside
    this repository.

.OUTPUTS
    Exit codes (CHECK-VERDICT):
      0 - every suite that ran passed (a suite switched off with -SkipGo is named as skipped);
      1 - a suite ran and failed;
      2 - could not verify: a suite could not run (missing project, dotnet, go.exe, worker
          repository, live worker, built viewer) and nothing ran and failed.
    The last line is `Run-AllTests: PASS (5 passed, skipped: worker-go)` or the FAIL /
    COULD NOT VERIFY form naming the suites that decided it.

.EXAMPLE
    .\tools\Run-AllTests.ps1
    .\tools\Run-AllTests.ps1 -Integration -SkipGo
#>
param(
    [switch]$Integration,
    [switch]$OcrScenes,
    [switch]$SkipGo,
    [switch]$SkipExchangeGo,
    [string]$WorkerRepo = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

# An unexpected terminating error in the runner itself must not surface as exit 1, which reads as
# "a suite failed".
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Run-AllTests' 2 'the runner itself failed'
}

if (-not $WorkerRepo) { $WorkerRepo = $env:FMS_WORKER_REPO }
if (-not $WorkerRepo) { $WorkerRepo = Join-Path (Split-Path $root -Parent) 'fms_companion' }

$plan = @('viewer', 'share-manager', $(if ($SkipGo) { 'worker-go (skipped)' } else { 'worker-go' }),
          $(if ($SkipExchangeGo) { 'exchange-go (skipped)' } else { 'exchange-go' }),
          'placement', 'documentation', 'naming', 'site', 'site-addresses')
if ($Integration) { $plan += 'share-identity', 'share-helper-logic', 'worker-ipc' }
if ($OcrScenes)   { $plan += 'ocr-scenes' }
Write-Subject 'Run-AllTests' "$($plan -join ', '); $(Get-TreeLabel $root)"

# One row per suite. Code: 0 / 1 / 2, or $null for a suite the caller switched off.
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string]$id, [string]$name, $code, [string]$detail) {
    $results.Add([pscustomobject]@{ Id = $id; Suite = $name; Code = $code; Detail = $detail })
}

function Invoke-DotnetTest([string]$id, [string]$name, [string]$proj) {
    Write-Host "`n=== $name ($proj) ===" -ForegroundColor Cyan
    if (-not (Test-Path $proj)) { Add-Result $id $name 2 'project not found'; return }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Add-Result $id $name 2 'dotnet not found'; return }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # dotnet writes warnings to stderr; they are output here
    try {
        $log = @(& dotnet test $proj -v minimal --nologo 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
        $exit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }

    # A multi-targeted project (Lite.Tests runs under BOTH net48 and net10) prints one
    # result line PER framework. Summarize every one of them - taking just the first
    # would silently drop a whole runtime's numbers from the summary.
    $lines = @($log | Select-String -Pattern 'Passed!|Failed!')
    $detail = "exit $exit"
    if ($lines.Count -gt 0) {
        $parts = foreach ($l in $lines) {
            $text = $l.ToString()
            $counts = if ($text -match '(Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*\d+,\s*Total:\s*\d+)') { $Matches[1] } else { $text.Trim() }
            $tfm = if ($text -match '\(([^)]+)\)\s*$') { $Matches[1] } else { '' }
            if ($tfm) { "[$tfm] $counts" } else { $counts }
        }
        $detail = ($parts -join '  |  ')
    }
    if ($exit -ne 0) { Add-Result $id $name 1 $detail; return }
    # Exit 0 with no result line means no test ran - nothing was inspected.
    if ($lines.Count -eq 0) { Add-Result $id $name 2 'dotnet test reported no test run'; return }
    Add-Result $id $name 0 $detail
}

function Invoke-ScriptSuite([string]$id, [string]$name, [string]$script, [string[]]$arguments = @()) {
    Write-Host "`n=== $name ($script) ===" -ForegroundColor Cyan
    $r = Invoke-ChildCheck $script $arguments
    Add-Result $id $name $r.Code $r.Verdict
}

# --- 1 & 2: .NET suites -------------------------------------------------------
# The viewer suite is multi-targeted: the same linked sources are tested under BOTH
# shipped runtimes (net48 = FastMediaSorter_x86.exe, net10 = the x64 mainline).
Invoke-DotnetTest 'viewer'        'Viewer (net48 + net10)' (Join-Path $root 'tests\Lite.Tests\Lite.Tests.vbproj')
Invoke-DotnetTest 'share-manager' 'Share Manager (net10)'  (Join-Path $root 'tests\Companion.Tests\Companion.Tests.vbproj')

# --- 3: Go worker suite -------------------------------------------------------
if ($SkipGo) {
    Add-Result 'worker-go' 'SFTP worker (Go)' $null 'skipped by -SkipGo'
} else {
    Write-Host "`n=== SFTP worker (Go) ($WorkerRepo) ===" -ForegroundColor Cyan
    $go = (Get-Command go -ErrorAction SilentlyContinue).Source
    if (-not $go) { $go = Join-Path $env:ProgramFiles 'Go\bin\go.exe' }
    if (-not (Test-Path $WorkerRepo)) {
        Add-Result 'worker-go' 'SFTP worker (Go)' 2 "repository not found ($WorkerRepo; pass -WorkerRepo or set FMS_WORKER_REPO)"
    } elseif (-not (Test-Path $go)) {
        Add-Result 'worker-go' 'SFTP worker (Go)' 2 'go.exe not found'
    } else {
        Push-Location $WorkerRepo
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $log = @(& $go test './...' 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
            $exit = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $prevEap
            Pop-Location
        }
        $okCount   = @($log | Select-String -Pattern '^ok\s').Count
        $failCount = @($log | Select-String -Pattern '^(FAIL|--- FAIL)').Count
        $noTest    = @($log | Select-String -Pattern 'no test files').Count
        $detail = "$okCount pkg ok, $failCount fail, $noTest pkg without tests"
        $code = if ($exit -ne 0) { 1 } elseif ($okCount -eq 0) { 2 } else { 0 }
        if ($code -eq 2) { $detail = "go test reported no package tested; $detail" }
        Add-Result 'worker-go' 'SFTP worker (Go)' $code $detail
    }
}

# --- 3b: the exchange server's Go suite (specification 133, ticket 135) --------
# Its module lives in THIS repository. A 64-bit Go is resolved explicitly: the `go` first on PATH
# can be the 32-bit toolchain, and the suite must run (and the amd64 binary build) the way the
# shipped exe does. No Go is "could not verify", never a pass.
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

# Run one go command in a directory with temporary environment variables; returns exit and output.
function Invoke-GoStep([string]$go, [string]$dir, [hashtable]$envVars, [string[]]$goArgs) {
    $saved = @{}
    foreach ($k in $envVars.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k, 'Process'); [Environment]::SetEnvironmentVariable($k, $envVars[$k], 'Process') }
    Push-Location $dir
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $log = @(& $go @goArgs 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
        return [pscustomobject]@{ Exit = $LASTEXITCODE; Log = $log }
    } finally {
        $ErrorActionPreference = $prevEap
        Pop-Location
        foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k], 'Process') }
    }
}

if ($SkipExchangeGo) {
    Add-Result 'exchange-go' 'Exchange server (Go)' $null 'skipped by -SkipExchangeGo'
} else {
    $exchangeDir = Join-Path $root 'exchange-server'
    Write-Host "`n=== Exchange server (Go) ($exchangeDir) ===" -ForegroundColor Cyan
    $go64 = Resolve-Go64
    if (-not (Test-Path (Join-Path $exchangeDir 'go.mod'))) {
        Add-Result 'exchange-go' 'Exchange server (Go)' 2 'exchange-server\go.mod not found'
    } elseif (-not $go64) {
        Add-Result 'exchange-go' 'Exchange server (Go)' 2 'no 64-bit go.exe found'
    } else {
        $amd64 = @{ GOARCH = 'amd64'; CGO_ENABLED = '0'; GOFLAGS = '' }
        $steps = @(
            @{ Name = 'module';       Dir = $exchangeDir;                                  Env = $amd64;                                   Args = @('test', '-count=1', './...') },
            @{ Name = 'manual proof'; Dir = (Join-Path $exchangeDir 'cmd\exchange-fakeproducer'); Env = $amd64;                            Args = @('test', '-count=1', './...') },
            @{ Name = 'linux vet';    Dir = $exchangeDir;                                  Env = @{ GOOS = 'linux'; GOARCH = 'amd64'; CGO_ENABLED = '0'; GOFLAGS = '' }; Args = @('vet', './...') }
        )
        # The race detector needs cgo and a C compiler (MinGW-w64: `winget install BrechtSanders.WinLibs.POSIX.UCRT`).
        # With one it is a real step of the suite; without one the detail line says so - never silently absent.
        $gccDir = $null
        $gccCmd = Get-Command gcc -ErrorAction SilentlyContinue
        if ($gccCmd) { $gccDir = Split-Path $gccCmd.Source } else {
            $pkgRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
            if (Test-Path $pkgRoot) {
                $found = Get-ChildItem $pkgRoot -Recurse -Filter gcc.exe -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($found) { $gccDir = $found.DirectoryName }
            }
        }
        $raceNote = 'race not run (no C compiler)'
        if ($gccDir) {
            $raceNote = 'race ok'
            $steps += @{ Name = 'race'; Dir = $exchangeDir; Env = @{ GOARCH = 'amd64'; CGO_ENABLED = '1'; GOFLAGS = ''; PATH = "$gccDir;$($env:PATH)" }; Args = @('test', '-race', '-count=1', './...') }
        }
        $okCount = 0; $failCount = 0; $worst = 0; $parts = @()
        foreach ($s in $steps) {
            Write-Host "--- $($s.Name)" -ForegroundColor DarkCyan
            $r = Invoke-GoStep $go64 $s.Dir $s.Env $s.Args
            $ok   = @($r.Log | Select-String -Pattern '^ok\s').Count
            $fail = @($r.Log | Select-String -Pattern '^(FAIL|--- FAIL)').Count
            if ($s.Args[0] -eq 'test' -and $s.Name -ne 'race') { $okCount += $ok; $failCount += $fail }
            if ($r.Exit -ne 0) { $worst = 1; $parts += "$($s.Name) failed" }
            elseif ($s.Args[0] -eq 'test' -and $ok -eq 0) { if ($worst -eq 0) { $worst = 2 }; $parts += "$($s.Name): no package tested" }
        }
        $detail = "$okCount pkg ok, $failCount fail" + $(if ($parts.Count) { '; ' + ($parts -join '; ') } else { "; linux vet ok; $raceNote" })
        Add-Result 'exchange-go' 'Exchange server (Go)' $worst $detail
    }
}

# --- 4: the placement registry against its runners ----------------------------
Invoke-ScriptSuite 'placement' 'Check placement' (Join-Path $root 'tools\Test-CheckPlacement.ps1')

Invoke-ScriptSuite 'documentation' 'Documentation registry' (Join-Path $root 'tools\Test-DocRegistry.ps1')
Invoke-ScriptSuite 'naming' 'Product naming' (Join-Path $root 'tools\Test-ProductNaming.ps1')

Invoke-ScriptSuite 'site' 'Published site quality' (Join-Path $root 'tools\Test-SiteQuality.ps1')
Invoke-ScriptSuite 'site-addresses' 'Site addresses held outside the site' (Join-Path $root 'tools\Test-SiteAddresses.ps1')
Invoke-ScriptSuite 'site-origins' 'Third-party origins of the site' (Join-Path $root 'tools\Test-SiteOrigins.ps1')
Invoke-ScriptSuite 'site-positioning' 'Positioning source and typed facts of the site' (Join-Path $root 'tools\Test-SitePositioning.ps1')

# --- optional live suites -----------------------------------------------------
if ($Integration) {
    Invoke-ScriptSuite 'share-identity' 'Share identity migration' (Join-Path $root 'tests\Integration\ShareIdentityMigration.ps1')
    Invoke-ScriptSuite 'share-helper-logic' 'Share helper ledger and lock logic' (Join-Path $root 'tests\Integration\ShareServiceHelperLogic.ps1')
    Invoke-ScriptSuite 'worker-ipc'     'Live IPC round-trip'      (Join-Path $root 'tests\Integration\WorkerRoundTrip.ps1')
}
if ($OcrScenes) {
    Invoke-ScriptSuite 'ocr-scenes' 'OCR overlay scenes' (Join-Path $root 'tests\Integration\OcrOverlayScenes.ps1')
}

# --- summary ------------------------------------------------------------------
Write-Host "`n================ TEST SUMMARY ================" -ForegroundColor Yellow
$results | Format-Table -AutoSize @(
    @{ L = 'Suite'; E = { $_.Suite } },
    @{ L = 'Result'; E = { if ($null -eq $_.Code) { 'SKIPPED' } else { Get-VerdictWord $_.Code } } },
    @{ L = 'Detail'; E = { $_.Detail } }
) | Out-String | Write-Host

$ran     = @($results | Where-Object { $null -ne $_.Code })
$failed  = @($ran | Where-Object { $_.Code -eq 1 })
$unknown = @($ran | Where-Object { $_.Code -eq 2 })
$passed  = @($ran | Where-Object { $_.Code -eq 0 })
$skipped = @($results | Where-Object { $null -eq $_.Code })

$parts = @()
if ($failed.Count)  { $parts += 'failed: ' + (($failed | ForEach-Object Id) -join ', ') }
if ($unknown.Count) { $parts += 'not verified: ' + (($unknown | ForEach-Object Id) -join ', ') }
$parts += "$($passed.Count) passed"
if ($skipped.Count) { $parts += 'skipped: ' + (($skipped | ForEach-Object Id) -join ', ') }

Exit-Verdict 'Run-AllTests' (Get-AggregateCode @($ran | ForEach-Object Code)) ($parts -join ', ')
