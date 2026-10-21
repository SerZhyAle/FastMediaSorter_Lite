# Shared by every check script and runner here: the CHECK-VERDICT exit-code vocabulary, the verdict
# line and the subject banner (BUILD-EVIDENCE rule 1). Dot-source it:
#     . (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
#
#   0 PASS                  something was inspected and nothing was wrong
#   1 FAIL                  something was inspected and judged defective
#   2 COULD NOT VERIFY      nothing was inspected, or the check could not run: a missing project,
#                           tool, repository, input or live process
#   3 PASS WITH ADVISORIES  reserved by the contract; no check in this repository emits it
#
# No other code carries a verdict, and 2 is never a pass. A check ends in exactly one line
# "<Name>: <WORD> [(detail)]", where <Name> is the script's base name - that is what a runner matches
# on - on the success path and on the failure path alike. A step the caller switched off is SKIPPED:
# named on the verdict line, never counted as passed.
#
# NOT APPLICABLE (CHECK-VERDICT 0.12, section 10 item A) is the one word that travels with exit 0
# besides PASS: a check whose work the CALLER deliberately switched off (a --dry-run, a selection that
# has nothing for it) prints "<Name>: NOT APPLICABLE (what was switched off)" and exits 0 - it did not
# inspect anything and is not a pass. No check here has such a path yet; a producer prints it with
# Exit-NotApplicable, and the readers below (Invoke-ChildCheck, Test-AllNotApplicable) already know it, so
# the first producer cannot be counted as a pass. A requested check that did not run for any other
# reason is 2, never this word.
#
# Write-Host only, never Write-Error, before an exit: under $ErrorActionPreference = 'Stop' a
# Write-Error is a terminating error, so the `exit N` after it dies as 1 while the message still
# prints (CHECK-VERDICT rule 3).

function Get-VerdictWord([int]$Code) {
    switch ($Code) {
        0 { 'PASS' }
        1 { 'FAIL' }
        2 { 'COULD NOT VERIFY' }
        3 { 'PASS WITH ADVISORIES' }
        default { 'FAIL' }   # an unknown code is read as not-passed, never as a pass
    }
}

function Get-NotApplicableWord { 'NOT APPLICABLE' }

# $Word overrides the word for the one case the code does not name: NOT APPLICABLE, which only ever
# travels with exit 0 - on any other code the word is the code's own.
function Write-VerdictLine([string]$Check, [int]$Code, [string]$Detail, [string]$Word = '') {
    $notApplicable = ($Word -eq (Get-NotApplicableWord)) -and ($Code -eq 0)
    $text = if ($notApplicable) { Get-NotApplicableWord } else { Get-VerdictWord $Code }
    $line = "${Check}: $text"
    if ($Detail) { $line += " ($Detail)" }
    $color = if ($notApplicable) { 'Gray' } else { switch ($Code) { 0 { 'Green' } 2 { 'Magenta' } 3 { 'Yellow' } default { 'Red' } } }
    Write-Host $line -ForegroundColor $color
}

# Prints the verdict line and exits the calling script with its code.
function Exit-Verdict([string]$Check, [int]$Code, [string]$Detail, [string]$Word = '') {
    Write-VerdictLine $Check $Code $Detail $Word
    exit $Code
}

# A check whose work the caller deliberately switched off: exit 0, its own word, never PASS.
function Exit-NotApplicable([string]$Check, [string]$Detail) {
    Exit-Verdict $Check 0 $Detail (Get-NotApplicableWord)
}

# The subject banner: what this run judged, printed first so a result is never quoted as evidence
# about something it did not look at.
function Write-Subject([string]$Check, [string]$Subject) {
    Write-Host "${Check}: subject = $Subject" -ForegroundColor Cyan
}

# Ordinary checks inspect the current files; AGENTS.md reserves Git for explicitly
# requested GitHub work. A revision label would not identify uncommitted input anyway.
function Get-TreeLabel([string]$RepoRoot) {
    return "current files on disk at $([IO.Path]::GetFullPath($RepoRoot))"
}

# rg applies the workspace ignore rules without reading Git history or its index.
# Include dotfiles but never enumerate Git's internal storage.
function Get-WorkspaceFiles([string]$RepoRoot) {
    if (-not (Get-Command rg -ErrorAction SilentlyContinue)) { throw 'rg is required for the workspace file inventory' }
    Push-Location -LiteralPath $RepoRoot
    try {
        $files = @(& rg --files --hidden -g '!.git/**')
        if ($LASTEXITCODE -ne 0) { throw 'workspace file inventory unavailable' }
        $files | ForEach-Object { $_.Replace('\', '/') }
    } finally { Pop-Location }
}

# Runs one check in its own pwsh process, so its exit code is its own and nothing it sets leaks into
# the caller, streams its output, and returns { Name; Code; Verdict }. Verdict is the child's own
# verdict line when it printed one. A code outside the vocabulary is read as a failure (CHECK-VERDICT
# section 6); the contract's reserved non-verdict 4 ("wait for a lock") becomes 2 here, because
# nothing in this repository queues. NotApplicable is true when the child answered NOT APPLICABLE with
# exit 0 - its work was switched off by the caller: it is counted with the skipped, never the passed.
function Invoke-ChildCheck([string]$Script, [string[]]$Arguments = @()) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Script)
    if (-not (Test-Path -LiteralPath $Script)) {
        $verdict = "${name}: COULD NOT VERIFY (script not found: $Script)"
        Write-Host $verdict -ForegroundColor Magenta
        return [pscustomobject]@{ Name = $name; Code = 2; Verdict = $verdict; NotApplicable = $false }
    }
    $pwsh = (Get-Process -Id $PID).Path
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # a child's stderr line is output here, not an error
    try {
        $lines = @(& $pwsh -NoProfile -File $Script @Arguments 2>&1 | ForEach-Object { $t = "$_"; Write-Host $t; $t })
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }
    $pattern = '^' + [regex]::Escape($name) + ': (PASS WITH ADVISORIES|PASS|FAIL|COULD NOT VERIFY|' + [regex]::Escape((Get-NotApplicableWord)) + ')'
    $verdict = @($lines | Where-Object { $_ -match $pattern }) | Select-Object -Last 1
    $noVerdictLine = -not $verdict
    if ($noVerdictLine) { $verdict = "${name}: (no verdict line; exit $code)" }
    if ($code -eq 4) { $code = 2 }
    elseif ($code -notin 0, 1, 2, 3) { $code = 1 }
    # A child that exits 0 and prints no verdict word proved nothing: CHECK-VERDICT section 6 reads a word the
    # reader does not know as not passed, and rule 11 owes the line on every path. It is COULD NOT VERIFY, never a pass.
    if ($noVerdictLine -and $code -eq 0) {
        $code = 2
        $verdict = "${name}: (no verdict line; exit 0 - read as COULD NOT VERIFY)"
    }
    # The word is only believed with exit 0: a line saying NOT APPLICABLE over any other code is read by the code.
    $notApplicable = ($code -eq 0) -and ($verdict -match ('^' + [regex]::Escape($name) + ': ' + [regex]::Escape((Get-NotApplicableWord)) + '\b'))
    [pscustomobject]@{ Name = $name; Code = $code; Verdict = $verdict; NotApplicable = $notApplicable }
}

# The aggregate code of several results: any FAIL is a FAIL; otherwise any could-not-verify is a
# COULD NOT VERIFY, because a green line over an unrun check is the lie the contract forbids;
# advisories only colour an otherwise clean run. Nothing at all is nothing inspected. A child that
# answered NOT APPLICABLE carries exit 0, so it never moves this code - Test-AllNotApplicable says
# whether the zero it returns is a PASS or the aggregate's own NOT APPLICABLE.
function Get-AggregateCode([int[]]$Codes) {
    if (-not $Codes -or $Codes.Count -eq 0) { return 2 }
    if ($Codes -contains 1) { return 1 }
    if ($Codes -contains 2) { return 2 }
    if ($Codes -contains 3) { return 3 }
    return 0
}

# CHECK-VERDICT 2.14 / 10 A.4: an aggregator that quotes at least one child, all of which answered
# NOT APPLICABLE, answers NOT APPLICABLE (exit 0) itself; any other mix is counted by Get-AggregateCode
# and the not-applicable children are counted with the skipped, never with the passed.
function Test-AllNotApplicable($Results) {
    $r = @($Results | Where-Object { $null -ne $_ -and $null -ne $_.Code })
    return ($r.Count -gt 0) -and (@($r | Where-Object { -not $_.NotApplicable }).Count -eq 0)
}
