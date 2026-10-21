<#
.SYNOPSIS
    Fixture self-test of tools/lib/Verdict.ps1's reading side (specification 183, process contracts):
    the readers know the word NOT APPLICABLE before any check gains a path that prints it.

.DESCRIPTION
    CHECK-VERDICT 0.12 section 10 item A adds one word that travels with exit 0 - a check whose work the
    caller deliberately switched off - and says a reader that does not know it must never count it as a
    pass. No check in this repository has such a path yet, so nothing exercises the readers; this builds
    throw-away child checks under %TEMP% (one per word, and one that lies) and drives Invoke-ChildCheck,
    Test-AllNotApplicable, Get-AggregateCode and Write-VerdictLine against them. Nothing in the repository
    is read or written. A handful of child processes; run it after a change to the helper.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$VerdictLib = Join-Path $RepoRoot 'tools\lib\Verdict.ps1'
. $VerdictLib
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'VerdictHelper' 2 'the self-test could not run' }
Write-Subject 'VerdictHelper' 'tools/lib/Verdict.ps1 readers against throw-away child checks'

$work = Join-Path ([IO.Path]::GetTempPath()) ("fms-verdict-helper-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

function New-Child([string]$name, [string]$body) {
    $path = Join-Path $work "$name.ps1"
    [IO.File]::WriteAllText($path, ". '$VerdictLib'`n$body`n")
    $path
}

$failures = [Collections.Generic.List[string]]::new()
function Assert-That([string]$name, [bool]$condition, [string]$detail = '') {
    if ($condition) { Write-Host "  ok    $name" -ForegroundColor Green }
    else { Write-Host "  WRONG $name $detail" -ForegroundColor Red; $failures.Add($name) }
}

try {
    $na     = New-Child 'na-check'   "Exit-NotApplicable 'na-check' '--dry-run'"
    $pass   = New-Child 'pass-check' "Exit-Verdict 'pass-check' 0 'ok'"
    $fail   = New-Child 'fail-check' "Exit-Verdict 'fail-check' 1 'broken'"
    $cnv    = New-Child 'cnv-check'  "Exit-Verdict 'cnv-check' 2 'no input'"
    $liar   = New-Child 'liar-check' "Write-Host 'liar-check: NOT APPLICABLE (but it failed)'; exit 1"
    $silent = New-Child 'silent-check' 'exit 0'

    $rNa = Invoke-ChildCheck $na; $rPass = Invoke-ChildCheck $pass; $rFail = Invoke-ChildCheck $fail
    $rCnv = Invoke-ChildCheck $cnv; $rLiar = Invoke-ChildCheck $liar; $rSilent = Invoke-ChildCheck $silent

    Assert-That 'a NOT APPLICABLE child is exit 0 and flagged' ($rNa.Code -eq 0 -and $rNa.NotApplicable) "(code $($rNa.Code), flag $($rNa.NotApplicable))"
    Assert-That 'its quoted verdict line keeps the word and the reason' ($rNa.Verdict -eq 'na-check: NOT APPLICABLE (--dry-run)') "($($rNa.Verdict))"
    Assert-That 'a PASS child is not flagged' ($rPass.Code -eq 0 -and -not $rPass.NotApplicable)
    Assert-That 'a FAIL child is exit 1 and not flagged' ($rFail.Code -eq 1 -and -not $rFail.NotApplicable)
    Assert-That 'a COULD NOT VERIFY child is exit 2 and not flagged' ($rCnv.Code -eq 2 -and -not $rCnv.NotApplicable)
    Assert-That 'the word over a non-zero exit is read by the code, never believed' ($rLiar.Code -eq 1 -and -not $rLiar.NotApplicable) "(code $($rLiar.Code), flag $($rLiar.NotApplicable))"
    Assert-That 'a child with no verdict line is not flagged' (-not $rSilent.NotApplicable -and $rSilent.Verdict -like '*(no verdict line*')
    Assert-That 'a child that exits 0 and prints no verdict line is COULD NOT VERIFY, never a pass (section 6, rule 11)' ($rSilent.Code -eq 2) "(code $($rSilent.Code))"
    Assert-That 'and the aggregate over it is not a pass either' ((Get-AggregateCode @($rPass.Code, $rSilent.Code)) -eq 2)

    Assert-That 'every quoted child NOT APPLICABLE: the aggregate is NOT APPLICABLE too' (Test-AllNotApplicable @($rNa, $rNa))
    Assert-That 'one PASS among them: it is not' (-not (Test-AllNotApplicable @($rNa, $rPass)))
    Assert-That 'one FAIL among them: it is not' (-not (Test-AllNotApplicable @($rNa, $rFail)))
    Assert-That 'nothing quoted: it is not (nothing inspected is not an applicable-less pass)' (-not (Test-AllNotApplicable @()))
    Assert-That 'a skipped row (no code) is not quoted' (Test-AllNotApplicable @($rNa, [pscustomobject]@{ Code = $null; NotApplicable = $false }))

    Assert-That 'NOT APPLICABLE next to PASS aggregates to 0' ((Get-AggregateCode @($rNa.Code, $rPass.Code)) -eq 0)
    Assert-That 'NOT APPLICABLE next to COULD NOT VERIFY aggregates to 2' ((Get-AggregateCode @($rNa.Code, $rCnv.Code)) -eq 2)
    Assert-That 'NOT APPLICABLE next to FAIL aggregates to 1' ((Get-AggregateCode @($rNa.Code, $rFail.Code)) -eq 1)

    $line = (& { Write-VerdictLine 'x' 0 'detail' 'NOT APPLICABLE' } *>&1 | Out-String).Trim()
    Assert-That 'the word is printed with exit 0' ($line -eq 'x: NOT APPLICABLE (detail)') "($line)"
    $line = (& { Write-VerdictLine 'x' 2 'detail' 'NOT APPLICABLE' } *>&1 | Out-String).Trim()
    Assert-That 'the word over exit 2 is ignored: it prints the code''s own word' ($line -eq 'x: COULD NOT VERIFY (detail)') "($line)"
    $line = (& { Write-VerdictLine 'x' 0 '' } *>&1 | Out-String).Trim()
    Assert-That 'no word given: exit 0 stays PASS' ($line -eq 'x: PASS') "($line)"
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Exit-Verdict 'VerdictHelper' 1 "$($failures.Count) case(s) wrong: $($failures -join '; ')"
}
Exit-Verdict 'VerdictHelper' 0 'the readers know NOT APPLICABLE: exit 0, counted with the skipped, never a pass, never believed over a non-zero exit'
