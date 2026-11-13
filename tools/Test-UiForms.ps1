<#
.SYNOPSIS
    Runs the UI form checks of the forms a change creates or edits, and ends in one CHECK-VERDICT line.

.DESCRIPTION
    Owner decision 2026-11-13: the UI form checks - tests that judge how a form or control looks or behaves on
    screen (layout, scale factors, clicks, keys, focus, painted pixels) - are not part of the release run or of
    tools/Run-AllTests.ps1. They run only for forms that are new or were edited during development. Each such
    test carries the xUnit traits Kind=UiForm and Form=<type name>; this script runs the ones whose Form trait
    names a type given in -Forms, in the viewer suite (net10, and net48 too with -Net48) and the Share Manager
    suite.

    Several tests click real windows, so they need an unlocked desktop: on a locked workstation LockApp covers
    every window and a click never reaches the button.

.PARAMETER Forms
    The form or shared control type names the change created or edited, as declared in src
    (e.g. Share_Settings_Form, Table_Form, DialogButtonRow). Without any the check is NOT APPLICABLE.

.PARAMETER All
    Run every UI form check instead (only on the owner's explicit request).

.PARAMETER Net48
    Also run the viewer suite's net48 leg (the x86 fallback is under the maintenance policy, so by default only
    the mainline leg runs).

.OUTPUTS
    Exit 0 PASS / NOT APPLICABLE, 1 FAIL, 2 COULD NOT VERIFY (no dotnet, a project missing, or the filter
    selected no test for a named form - a form without UI checks is reported, never read as a pass).

.EXAMPLE
    pwsh -NoProfile -File tools/Test-UiForms.ps1 -Forms Share_Settings_Form,DialogButtonRow
#>
param(
    [string[]]$Forms = @(),
    [switch]$All,
    [switch]$Net48
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-UiForms' 2 'the script itself failed'
}

$Forms = @($Forms | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($f in $Forms) {
    if ($f -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Exit-Verdict 'Test-UiForms' 2 "'$f' is not a type name" }
}
$scope = if ($All) { 'every UI form check' } else { "UI form checks of $($Forms -join ', ')" }
Write-Subject 'Test-UiForms' "$scope; $(Get-TreeLabel $root)"

if (-not $All -and $Forms.Count -eq 0) { Exit-NotApplicable 'Test-UiForms' 'no new or edited form named' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Exit-Verdict 'Test-UiForms' 2 'dotnet not found' }

$filter = if ($All) { 'Kind=UiForm' } else { 'Kind=UiForm&(' + (($Forms | ForEach-Object { "Form=$_" }) -join '|') + ')' }

$runs = @(
    [pscustomobject]@{ Name = 'viewer (net10)'; Project = 'tests\Lite.Tests\Lite.Tests.vbproj'; Framework = 'net10.0-windows' }
)
if ($Net48) { $runs += [pscustomobject]@{ Name = 'viewer (net48)'; Project = 'tests\Lite.Tests\Lite.Tests.vbproj'; Framework = 'net48' } }
$runs += [pscustomobject]@{ Name = 'share-manager'; Project = 'tests\Companion.Tests\Companion.Tests.vbproj'; Framework = '' }

$codes = @()
$parts = @()
$ranTotal = 0
foreach ($run in $runs) {
    $proj = Join-Path $root $run.Project
    if (-not (Test-Path -LiteralPath $proj)) { $codes += 2; $parts += "$($run.Name): project not found"; continue }
    Write-Host "`n=== $($run.Name) ===" -ForegroundColor Cyan
    $dotnetArgs = @('test', $proj, '-v', 'minimal', '--nologo', '--filter', $filter)
    if ($run.Framework) { $dotnetArgs += @('-f', $run.Framework) }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $log = @(& dotnet @dotnetArgs 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
        $exit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prevEap }
    $line = @($log | Select-String -Pattern '(Passed|Failed)!.*Total:\s*(\d+)' | Select-Object -Last 1)
    $noMatch = @($log | Select-String -Pattern 'No test matches the given testcase filter').Count -gt 0
    if ($line.Count -eq 1) {
        $total = [int]$line[0].Matches[0].Groups[2].Value
        $ranTotal += $total
        $counts = if ("$($line[0])" -match '(Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*\d+)') { $Matches[1] } else { "exit $exit" }
        $parts += "$($run.Name): $counts"
        $codes += $(if ($exit -eq 0) { 0 } else { 1 })
    } elseif ($noMatch) {
        $parts += "$($run.Name): no UI form check for these forms"
    } else {
        $codes += $(if ($exit -eq 0) { 2 } else { 1 })
        $parts += "$($run.Name): exit $exit, no result line"
    }
}

if ($ranTotal -eq 0 -and -not ($codes -contains 1)) {
    Exit-Verdict 'Test-UiForms' 2 "no UI form check selected for $($Forms -join ', ') - tag the form's tests Kind=UiForm, Form=<type> ($($parts -join '; '))"
}
Exit-Verdict 'Test-UiForms' (Get-AggregateCode $codes) ($parts -join '; ')
