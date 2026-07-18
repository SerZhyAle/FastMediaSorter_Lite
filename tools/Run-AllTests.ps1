<#
.SYNOPSIS
    Runs the automated test suites for all three programs of the Fast Media Sorter
    package and prints one summary. Exit code is non-zero if any suite fails.

    1. Sorter (LITE, net48)      -> tests/Lite.Tests        (dotnet test / xUnit)
    2. Share Manager (net10)     -> tests/Companion.Tests   (dotnet test / xUnit)
    3. SFTP worker (Go)          -> P:\windows\fms_companion (go test ./...)

    These are UNIT/logic suites - fast, no network, no phone. The optional live
    IPC round-trip against the real worker is run with -Integration (read-only:
    EnsureRunning/GetStatus + build a .fmscfg; never mutates worker data).

.PARAMETER Integration
    Also run the live worker IPC round-trip (needs the worker payload present).

.PARAMETER SkipGo
    Skip the Go worker suite (e.g. when the fms_companion repo is not checked out).

.EXAMPLE
    .\tools\Run-AllTests.ps1
    .\tools\Run-AllTests.ps1 -Integration
#>
param(
    [switch]$Integration,
    [switch]$SkipGo
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Suite = $name; Passed = $ok; Detail = $detail })
}

function Invoke-DotnetTest([string]$name, [string]$proj) {
    Write-Host "`n=== $name ($proj) ===" -ForegroundColor Cyan
    if (-not (Test-Path $proj)) { Add-Result $name $false "project not found"; return }
    $log = & dotnet test $proj -v minimal --nologo 2>&1
    $log | ForEach-Object { Write-Host $_ }
    $ok = ($LASTEXITCODE -eq 0)

    # A multi-targeted project (Lite.Tests runs under BOTH net48 and net10) prints one
    # result line PER framework. Summarize every one of them - taking just the first
    # would silently drop a whole runtime's numbers from the summary.
    $lines = @($log | Select-String -Pattern 'Passed!|Failed!')
    if ($lines.Count -gt 0) {
        $parts = foreach ($l in $lines) {
            $text = $l.ToString()
            $counts = if ($text -match '(Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*\d+,\s*Total:\s*\d+)') { $Matches[1] } else { $text.Trim() }
            $tfm = if ($text -match '\(([^)]+)\)\s*$') { $Matches[1] } else { '' }
            if ($tfm) { "[$tfm] $counts" } else { $counts }
        }
        $detail = ($parts -join '  |  ')
    } else {
        $detail = "exit $LASTEXITCODE"
    }
    Add-Result $name $ok $detail
}

# --- 1 & 2: .NET suites -------------------------------------------------------
# The viewer suite is multi-targeted: the same linked sources are tested under BOTH
# shipped runtimes (net48 = FastMediaSorter_x86.exe, net10 = the x64 mainline).
Invoke-DotnetTest "Viewer (net48 + net10)"   (Join-Path $root 'tests\Lite.Tests\Lite.Tests.vbproj')
Invoke-DotnetTest "Share Manager (net10)"    (Join-Path $root 'tests\Companion.Tests\Companion.Tests.vbproj')

# --- 3: Go worker suite -------------------------------------------------------
if (-not $SkipGo) {
    $workerRepo = 'P:\windows\fms_companion'
    Write-Host "`n=== SFTP worker (Go) ($workerRepo) ===" -ForegroundColor Cyan
    $go = (Get-Command go -ErrorAction SilentlyContinue).Source
    if (-not $go) { $go = 'C:\Program Files\Go\bin\go.exe' }
    if (-not (Test-Path $workerRepo)) {
        Add-Result "SFTP worker (Go)" $false "repo not found ($workerRepo)"
    } elseif (-not (Test-Path $go)) {
        Add-Result "SFTP worker (Go)" $false "go.exe not found"
    } else {
        Push-Location $workerRepo
        try {
            $log = & $go test './...' 2>&1
            $log | ForEach-Object { Write-Host $_ }
            $ok = ($LASTEXITCODE -eq 0)
            $okCount   = ($log | Select-String -Pattern '^ok\s').Count
            $failCount = ($log | Select-String -Pattern '^(FAIL|--- FAIL)').Count
            $noTest    = ($log | Select-String -Pattern 'no test files').Count
            Add-Result "SFTP worker (Go)" $ok "$okCount pkg ok, $failCount fail, $noTest pkg without tests"
        } finally { Pop-Location }
    }
} else {
    Add-Result "SFTP worker (Go)" $true "skipped (-SkipGo)"
}

# --- optional live integration ------------------------------------------------
if ($Integration) {
    Write-Host "`n=== Live worker IPC round-trip (integration) ===" -ForegroundColor Cyan
    $audit = Join-Path $root 'tests\Integration\WorkerRoundTrip.ps1'
    if (Test-Path $audit) {
        & $audit
        Add-Result "Live IPC round-trip" ($LASTEXITCODE -eq 0) "see output above"
    } else {
        Add-Result "Live IPC round-trip" $false "harness not found ($audit)"
    }
}

# --- summary ------------------------------------------------------------------
Write-Host "`n================ TEST SUMMARY ================" -ForegroundColor Yellow
$results | Format-Table -AutoSize @(
    @{ L = 'Suite'; E = { $_.Suite } },
    @{ L = 'Result'; E = { if ($_.Passed) { 'PASS' } else { 'FAIL' } } },
    @{ L = 'Detail'; E = { $_.Detail } }
) | Out-String | Write-Host

$failed = @($results | Where-Object { -not $_.Passed })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) suite(s) FAILED." -ForegroundColor Red
    exit 1
}
Write-Host "All suites passed." -ForegroundColor Green
exit 0
