<#
.SYNOPSIS
    Fixtures for tools\Build-ContextHelp.ps1 -Check (DOC-INTERNAL-QUALITY 0.12 W): the drift check passes a corpus
    the generator wrote and names each kind of drift.

.DESCRIPTION
    Builds a scratch tree (the generator's small inputs of this repository, an empty stand-in for every capture
    picture, and the corpus the generator writes there), then runs the real `-Check` against it, never against the
    committed docs/help:

      1. the corpus as the generator wrote it              -> exit 0;
      2. one page edited by hand                           -> exit 1, the page named as "differs";
      3. one page deleted                                  -> exit 1, the page named as "missing";
      4. a page the generator does not write added         -> exit 1, the page named as "stale".

.NOTES
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL (a fixture disagreed), 2 COULD NOT VERIFY (an input is missing, or
    the check itself threw). The last line is `ContextHelpDriftFixtures: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$check = 'ContextHelpDriftFixtures'
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the check itself failed'
}

$generator = Join-Path $repo 'tools\Build-ContextHelp.ps1'
$inputs = @('tools/settings-help-copy.json', 'help-map.json', 'assets/exchange-mark/png/full-256.png',
    'assets/exchange-mark/svg/exchange-mark-small.svg', 'assets/exchange-mark/svg/exchange-mark-mono.svg',
    'src/Localization/Localization.DeviceVocabulary.vb', 'docs/help/context-help-capture-evidence.json')
foreach ($rel in @($generator) + @($inputs | ForEach-Object { Join-Path $repo $_ })) {
    if (-not (Test-Path -LiteralPath $rel)) { Exit-Verdict $check 2 "input not found: $rel" }
}
Write-Subject $check 'tools/Build-ContextHelp.ps1 -Check against a scratch tree holding a freshly generated help corpus'

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('fms-help-fixtures-' + [Guid]::NewGuid().ToString('N'))
$findings = [Collections.Generic.List[string]]::new()
try {
    foreach ($rel in $inputs) {
        $dest = Join-Path $scratch $rel
        [IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
        [IO.File]::Copy((Join-Path $repo $rel), $dest, $true)
    }
    $committed = (Resolve-Path -LiteralPath (Join-Path $repo 'docs\help')).Path.TrimEnd('\')
    foreach ($png in Get-ChildItem -LiteralPath $committed -Recurse -File -Filter '*.png') {
        $rel = $png.FullName.Substring($committed.Length).TrimStart('\')
        if ($rel -like 'exchange-mark\*') { continue }
        $dest = Join-Path $scratch (Join-Path 'docs\help' $rel)
        [IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
        [IO.File]::WriteAllBytes($dest, [byte[]]@())
    }
    $log = & pwsh -NoProfile -File $generator -Root $scratch 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host $log; Exit-Verdict $check 2 'the generator could not write the scratch corpus' }

    function Invoke-Drift {
        $out = & pwsh -NoProfile -File $generator -Root $scratch -Check 2>&1 | Out-String
        return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $out }
    }
    $pageEn = Join-Path $scratch 'docs\help\en\index.html'
    $pageRu = Join-Path $scratch 'docs\help\ru\index.html'
    $stray  = Join-Path $scratch 'docs\help\en\hand-written.html'

    # 1. as generated
    $r = Invoke-Drift
    if ($r.Code -ne 0) { $findings.Add("case 1: a corpus the generator wrote was not accepted (exit $($r.Code)): $($r.Output.Trim())") }

    # 2. a hand edit
    $original = [IO.File]::ReadAllBytes($pageEn)
    [IO.File]::WriteAllBytes($pageEn, [byte[]]($original + [Text.Encoding]::UTF8.GetBytes('<!-- hand edit -->')))
    $r = Invoke-Drift
    if ($r.Code -ne 1) { $findings.Add("case 2: a hand-edited page ended with exit $($r.Code), wanted 1") }
    if ($r.Output -notmatch 'differs: en\\index\.html') { $findings.Add("case 2: the edited page is not named as differing: $($r.Output.Trim())") }
    [IO.File]::WriteAllBytes($pageEn, $original)

    # 3. a missing page
    $kept = [IO.File]::ReadAllBytes($pageRu)
    Remove-Item -LiteralPath $pageRu -Force
    $r = Invoke-Drift
    if ($r.Code -ne 1) { $findings.Add("case 3: a missing page ended with exit $($r.Code), wanted 1") }
    if ($r.Output -notmatch 'missing: ru\\index\.html') { $findings.Add("case 3: the missing page is not named: $($r.Output.Trim())") }
    [IO.File]::WriteAllBytes($pageRu, $kept)

    # 4. a stale page
    [IO.File]::WriteAllText($stray, '<!doctype html><title>x</title>')
    $r = Invoke-Drift
    if ($r.Code -ne 1) { $findings.Add("case 4: a page the generator does not write ended with exit $($r.Code), wanted 1") }
    if ($r.Output -notmatch 'stale: en\\hand-written\.html') { $findings.Add("case 4: the stale page is not named: $($r.Output.Trim())") }
    Remove-Item -LiteralPath $stray -Force
}
finally {
    if (Test-Path -LiteralPath $scratch) { [IO.Directory]::Delete($scratch, $true) }
}

if ($findings.Count -gt 0) {
    foreach ($f in $findings) { Write-Host "  $f" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($findings.Count) fixture(s) disagreed"
}
Exit-Verdict $check 0 '4 cases: a generated corpus passes; a hand edit, a missing page and a stale page are each named'
