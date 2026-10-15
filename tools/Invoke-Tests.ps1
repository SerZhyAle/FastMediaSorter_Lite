<#
.SYNOPSIS
    Inner-loop compile check and filtered test run with a bounded digest and a kept log.

.DESCRIPTION
    Runs ONE project (a build or one test suite), writes the full output to
    %TEMP%\fms-tests\<name>-<stamp>.log and prints only what a person or an agent needs:
    each compile error once (file, line, code, message), each failing test with its first
    message line, the totals, the elapsed time and the log path. Grep the log for more; do not
    re-run to see more.

    It is a development helper, not a gate: Run-AllTests.ps1 stays the step before "done".

.PARAMETER Build
    modern    src\Modern\FastMediaSorter.Modern.vbproj  (the net10 x64 mainline, Debug)
    companion src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj

.PARAMETER Suite
    lite       tests\Lite.Tests\Lite.Tests.vbproj       (multi-targeted net48 + net10)
    companion  tests\Companion.Tests\Companion.Tests.vbproj

.PARAMETER Filter
    One or more test-name fragments separated by '|' (matched with FullyQualifiedName~).

.PARAMETER Tfm
    net10 (default), net48 or both - the viewer suite only; the Companion suite has one target.

.EXAMPLE
    pwsh -NoProfile -File tools\Invoke-Tests.ps1 -Build modern
    pwsh -NoProfile -File tools\Invoke-Tests.ps1 -Suite lite -Filter OcrOverlayFit
    pwsh -NoProfile -File tools\Invoke-Tests.ps1 -Suite lite -Tfm both

.NOTES
    Exit codes: 0 passed, 1 a compile error or a failing test, 2 could not verify (no dotnet, a
    missing project, or output that matched nothing this script understands).
#>
[CmdletBinding()]
param(
    [ValidateSet('modern', 'companion')][string]$Build,
    [ValidateSet('lite', 'companion')][string]$Suite,
    [string]$Filter = '',
    [ValidateSet('net10', 'net48', 'both')][string]$Tfm = 'net10',
    [switch]$NoRestore,
    [int]$MaxLines = 40
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Stop-Run([int]$code, [string]$text) {
    Write-Host "Invoke-Tests: $text"
    exit $code
}

if (-not $Build -and -not $Suite) {
    Stop-Run 2 'COULD NOT VERIFY (pass -Build modern|companion or -Suite lite|companion)'
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not $dotnet) { Stop-Run 2 'COULD NOT VERIFY (dotnet not found on PATH)' }

$projects = @{
    'build:modern'    = 'src\Modern\FastMediaSorter.Modern.vbproj'
    'build:companion' = 'src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj'
    'test:lite'       = 'tests\Lite.Tests\Lite.Tests.vbproj'
    'test:companion'  = 'tests\Companion.Tests\Companion.Tests.vbproj'
}
$kind = if ($Build) { 'build' } else { 'test' }
$name = if ($Build) { $Build } else { $Suite }
$proj = Join-Path $root $projects["${kind}:${name}"]
if (-not (Test-Path -LiteralPath $proj)) { Stop-Run 2 "COULD NOT VERIFY (project missing: $proj)" }

$logDir = Join-Path ([IO.Path]::GetTempPath()) 'fms-tests'
New-Item -ItemType Directory -Force $logDir | Out-Null
Get-ChildItem $logDir -Filter *.log -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip 25 | Remove-Item -Force -ErrorAction SilentlyContinue

# Target frameworks to run: only the multi-targeted viewer suite has a choice.
$tfms = @($null)
if ($kind -eq 'test' -and $name -eq 'lite') {
    $tfms = switch ($Tfm) { 'net10' { @('net10.0-windows') } 'net48' { @('net48') } default { @('net10.0-windows', 'net48') } }
}

function Invoke-One([string]$framework) {
    $label = if ($framework) { "$name $framework" } else { $name }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $log = Join-Path $logDir ("{0}-{1}.log" -f ($label -replace '[^\w\.-]+', '-'), $stamp)

    $a = [System.Collections.Generic.List[string]]::new()
    if ($kind -eq 'build') { $a.Add('build'); $a.Add($proj); $a.Add('-c'); $a.Add('Debug') }
    else { $a.Add('test'); $a.Add($proj) }
    if ($framework) { $a.Add('-f'); $a.Add($framework) }
    $a.Add('--nologo'); $a.Add('-v'); $a.Add('q'); $a.Add('-p:NuGetAudit=false')
    if ($NoRestore) { $a.Add('--no-restore') }
    if ($kind -eq 'test' -and $Filter) {
        $f = ($Filter -split '\|' | Where-Object { $_.Trim() } | ForEach-Object { 'FullyQualifiedName~' + $_.Trim() }) -join '|'
        $a.Add('--filter'); $a.Add($f)
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $lines = @(& $dotnet @a 2>&1 | ForEach-Object { $_.ToString() })
    $exit = $LASTEXITCODE
    $sw.Stop()
    Set-Content -LiteralPath $log -Value $lines -Encoding utf8

    # compile / restore errors, once each
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        $m = [regex]::Match($l, '^(?<file>.+?)\((?<line>\d+),\d+\): error (?<code>[A-Z]+\d+): (?<msg>.+?)(?: \[[^\]]+\])?\s*$')
        if (-not $m.Success) {
            $m = [regex]::Match($l, '^(?<pre>.*?)(?<file>MSBUILD|[^\s:]+)\s*: error (?<code>[A-Z]+\d+): (?<msg>.+?)(?: \[[^\]]+\])?\s*$')
            if (-not $m.Success) { continue }
        }
        $file = $m.Groups['file'].Value.Replace($root + '\', '')
        $where = if ($m.Groups['line'].Success) { "$file($($m.Groups['line'].Value))" } else { $file }
        $row = "$where $($m.Groups['code'].Value) $($m.Groups['msg'].Value)"
        if ($seen.Add($row)) { $errors.Add($row) }
    }

    # failing tests: "  Failed Namespace.Class.Method [n ms]" then "Error Message:" and its first line
    $failed = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = [regex]::Match($lines[$i], '^\s*Failed\s+(?<t>[\w\.\+`<>\[\], ]+?)(?:\s+\[[^\]]*\])?\s*$')
        if (-not $m.Success) { continue }
        $msg = ''
        for ($j = $i + 1; $j -lt [Math]::Min($i + 12, $lines.Count); $j++) {
            if ($lines[$j] -match 'Error Message') {
                for ($k = $j + 1; $k -lt [Math]::Min($j + 6, $lines.Count); $k++) {
                    if ($lines[$k].Trim()) { $msg = $lines[$k].Trim(); break }
                }
                break
            }
        }
        if ($msg.Length -gt 200) { $msg = $msg.Substring(0, 200) }
        $failed.Add("$($m.Groups['t'].Value) :: $msg")
    }

    $totals = @($lines | Where-Object { $_ -match '^(Passed|Failed)!\s+-\s+Failed:' } | ForEach-Object { $_.Trim() })
    $locked = @($lines | Where-Object { $_ -match 'being used by another process|Could not copy' } | Select-Object -First 1)
    $buildOk = [bool]($lines | Where-Object { $_ -match 'Build succeeded|0 Error\(s\)' } | Select-Object -First 1)

    Write-Host "== $label  ($([Math]::Round($sw.Elapsed.TotalSeconds, 1)) s)  log: $log"
    $shown = 0
    foreach ($e in $errors) { if ($shown -ge $MaxLines) { Write-Host "  .. $($errors.Count - $shown) more compile errors in the log"; break }; Write-Host "  ERROR $e"; $shown++ }
    $shown = 0
    foreach ($f in $failed) { if ($shown -ge $MaxLines) { Write-Host "  .. $($failed.Count - $shown) more failing tests in the log"; break }; Write-Host "  FAIL  $f"; $shown++ }
    foreach ($t in $totals) { Write-Host "  $t" }
    if ($locked) { Write-Host '  HINT  a running exe holds a file: close FastMediaSorter_LITE / FastMediaSorter_x86 / FastMediaSorterCompanion and rerun' }

    $verdict = 'ok'
    if ($exit -ne 0) {
        $verdict = if ($errors.Count -or $failed.Count -or ($totals | Where-Object { $_ -like 'Failed!*' })) { 'failed' } else { 'unverified' }
    } elseif ($kind -eq 'test' -and -not $totals.Count) {
        $verdict = 'unverified'   # exit 0 with no totals: the filter matched no test
    } elseif ($kind -eq 'build' -and -not $buildOk -and -not $errors.Count) {
        $verdict = 'ok'
    }
    if ($verdict -eq 'unverified' -and $exit -ne 0) {
        Write-Host '  last lines of the log:'
        $lines | Select-Object -Last 8 | ForEach-Object { Write-Host "    $_" }
    }
    return [pscustomobject]@{ Label = $label; Verdict = $verdict; Errors = $errors.Count; Failed = $failed.Count; Log = $log }
}

$results = foreach ($t in $tfms) { Invoke-One $t }

$bad = @($results | Where-Object { $_.Verdict -eq 'failed' })
$unv = @($results | Where-Object { $_.Verdict -eq 'unverified' })
$summary = ($results | ForEach-Object { "$($_.Label) $($_.Verdict)" }) -join '; '
if ($bad.Count) { Write-Host "Invoke-Tests: FAIL ($summary)"; exit 1 }
if ($unv.Count) { Write-Host "Invoke-Tests: COULD NOT VERIFY ($summary; a filter that matches no test is this too)"; exit 2 }
Write-Host "Invoke-Tests: PASS ($summary)"
exit 0
