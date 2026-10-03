<#
.SYNOPSIS
    Holds tools\check-placement.jsonl (contract CHECK-PLACEMENT) against the runners, in both directions.

.DESCRIPTION
    Every check here has one record in tools\check-placement.jsonl naming the runner(s) that own it and
    why. A placement written in a comment cannot be queried or checked; this is the one place it is
    declared, and this script is what makes the declaration binding:

      forward  - every runner a record names really runs the check;
      backward - every check a runner really runs has a record naming that runner, and every script that
                 reports a CHECK-VERDICT verdict (it dot-sources tools\lib\Verdict.ps1 or names the
                 contract) has a record at all.

    "Runs" means the runner's CODE names the check - its file name, or the record's "match" text for a
    check that is not a file here. Comments are dropped with the PowerShell tokenizer first: a runner
    that only talks about a check does not run it.

    Classes: agent-closure (tools\Run-AllTests.ps1, which CLAUDE.md makes the step before "done"),
    release (tools\Release.ps1, before the tag), build (where the artifact is produced), hand-run (an
    opt-in switch or a person). Every record is "judged"; a "seeded" one would need rule 7's open-owner
    check, which this script does not implement, so it refuses one.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every record and every runner agree;
      1 - a record is malformed, or a record and a runner disagree;
      2 - could not verify: the registry is missing or a line of it cannot be read.
    The last line is the verdict, `Test-CheckPlacement: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-CheckPlacement' 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
$registry = Join-Path $PSScriptRoot 'check-placement.jsonl'
$classes = @('agent-closure', 'release', 'build', 'hand-run')
# The runners that exist here whether or not a record names them - a check they run without a
# record is exactly what the backward direction is for.
$fixedRunners = @('tools/Run-AllTests.ps1', 'tools/Release.ps1', 'build.ps1')

Write-Subject 'Test-CheckPlacement' "tools/check-placement.jsonl against its runners; $(Get-TreeLabel $root)"

if (-not (Test-Path -LiteralPath $registry)) {
    Exit-Verdict 'Test-CheckPlacement' 2 'tools/check-placement.jsonl not found'
}

$records = @()
$lineNo = 0
foreach ($line in Get-Content -LiteralPath $registry -Encoding UTF8) {
    $lineNo++
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $r = $line | ConvertFrom-Json } catch {
        Exit-Verdict 'Test-CheckPlacement' 2 "line $lineNo of tools/check-placement.jsonl is not JSON"
    }
    $records += $r
}
if ($records.Count -eq 0) { Exit-Verdict 'Test-CheckPlacement' 2 'tools/check-placement.jsonl holds no record' }

$problems = [System.Collections.Generic.List[string]]::new()
function Get-Field($r, [string]$name) {
    $p = $r.PSObject.Properties[$name]
    if ($p -and $p.Value) { return [string]$p.Value }
    return ''
}

# --- the records themselves ---------------------------------------------------
# CHECK-PLACEMENT 0.11 rule 8: "per-change runner: none" is a declared state, carried by the
# registry itself as one declaration record (a reason is required) - not by the pointer's prose.
$declarations = @($records | Where-Object { Get-Field $_ 'declaration' })
$records = @($records | Where-Object { -not (Get-Field $_ 'declaration') })
$declarationNote = ''
if ($declarations.Count -gt 0) {
    if ($declarations.Count -gt 1) { $problems.Add('the registry may declare "per-change runner: none" once') }
    foreach ($d in $declarations) {
        if ((Get-Field $d 'declaration') -ne 'per-change runner: none') {
            $problems.Add("unknown declaration '$(Get-Field $d 'declaration')' - the only one rule 8 names is 'per-change runner: none'")
        }
        if (-not (Get-Field $d 'reason')) { $problems.Add('the per-change declaration carries no reason') }
    }
    $declarationNote = '; per-change runner: none (declared)'
}

$seen = @{}
$placed = foreach ($r in $records) {
    $check = Get-Field $r 'check'
    foreach ($f in 'check', 'class', 'runner', 'decided', 'ticket', 'basis', 'reason') {
        if (-not (Get-Field $r $f)) { $problems.Add("record '$check': '$f' is missing or empty") }
    }
    if (-not $check) { continue }
    if ($seen.ContainsKey($check)) { $problems.Add("record '$check' appears twice") }
    $seen[$check] = $true

    $class = Get-Field $r 'class'
    if ($class -and $classes -notcontains $class) { $problems.Add("record '$check': class '$class' is not one of $($classes -join ', ')") }
    $basis = Get-Field $r 'basis'
    if ($basis -and $basis -ne 'judged') { $problems.Add("record '$check': basis '$basis' - this registry holds judged records only") }
    $decided = Get-Field $r 'decided'
    if ($decided -and $decided -notmatch '^\d{4}-\d{2}-\d{2}$') { $problems.Add("record '$check': decided '$decided' is not a date") }

    $match = Get-Field $r 'match'
    if (-not $match) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $check))) { $problems.Add("record '$check': no such file (a check outside this repository needs a 'match')") }
        $match = Split-Path $check -Leaf
    }

    $runnerFiles = @()
    foreach ($entry in ((Get-Field $r 'runner') -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
        $first = ($entry -split '\s+')[0]
        $isFile = $first -match '\.ps1$'
        if (-not $isFile) {
            if ($class -ne 'hand-run') { $problems.Add("record '$check': runner '$entry' is not a script - only a hand-run check may name a person") }
            continue
        }
        if (-not (Test-Path -LiteralPath (Join-Path $root $first))) { $problems.Add("record '$check': runner '$first' does not exist"); continue }
        $runnerFiles += $first
    }
    [pscustomobject]@{ Check = $check; Class = $class; Match = $match; Runners = $runnerFiles }
}
$placed = @($placed)

# --- forward and backward, per runner -----------------------------------------
function Get-CodeText([string]$path) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    ($tokens | Where-Object { $_.Kind -ne 'Comment' } | ForEach-Object { $_.Text }) -join ' '
}

$runnerSet = @($fixedRunners + @($placed | ForEach-Object { $_.Runners }) | Sort-Object -Unique)
foreach ($runner in $runnerSet) {
    $full = Join-Path $root $runner
    if (-not (Test-Path -LiteralPath $full)) { $problems.Add("runner '$runner' does not exist"); continue }
    $code = Get-CodeText $full
    foreach ($p in $placed) {
        if ($p.Check -eq $runner) { continue }
        # A whole name, not a substring: "Build-OfflineRelease.ps1" does not run "Release.ps1".
        $runs = [regex]::IsMatch($code, '(?<![\w.-])' + [regex]::Escape($p.Match))
        $declared = $p.Runners -contains $runner
        if ($runs -and -not $declared) { $problems.Add("$runner runs '$($p.Check)', but its record does not name $runner") }
        if ($declared -and -not $runs) { $problems.Add("'$($p.Check)' names $runner as its runner, but $runner does not run it") }
    }
}

# --- backward: every verdict-reporting script is placed -----------------------
$scripts = @(
    Get-ChildItem -LiteralPath $root -Filter *.ps1 -File
    Get-ChildItem -LiteralPath (Join-Path $root 'tools') -Filter *.ps1 -File
    Get-ChildItem -LiteralPath (Join-Path $root 'tests\Integration') -Filter *.ps1 -File -ErrorAction SilentlyContinue
)
$verdictScripts = 0
foreach ($s in $scripts) {
    $text = Get-Content -LiteralPath $s.FullName -Raw
    if ($text -notmatch 'CHECK-VERDICT|lib\\Verdict\.ps1') { continue }
    $verdictScripts++
    $rel = $s.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    if (-not ($placed | Where-Object { $_.Check -eq $rel })) {
        $problems.Add("$rel reports a CHECK-VERDICT verdict but has no record in tools/check-placement.jsonl")
    }
}

$byClass = ($classes | ForEach-Object { $c = $_; "$c $(@($placed | Where-Object { $_.Class -eq $c }).Count)" }) -join ', '
$summary = "$($placed.Count) records ($byClass), $($runnerSet.Count) runners, $verdictScripts verdict scripts$declarationNote"
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'Test-CheckPlacement' 1 "$($problems.Count) disagreement(s); $summary"
}
Exit-Verdict 'Test-CheckPlacement' 0 $summary
