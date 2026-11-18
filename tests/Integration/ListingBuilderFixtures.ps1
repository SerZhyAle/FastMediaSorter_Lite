<#
.SYNOPSIS
    Fixtures for publishing\store\Build-ListingData.ps1 (WINDOWS-STORE 0.2): the 21-word search-term cap
    refuses an overflow, and a BOM export is written back in the rule 8 default dialect.

.DESCRIPTION
    Runs the real builder in a child process against scratch copies of its three inputs, never against
    the committed files, and judges what it wrote:

      1. copy of the committed inputs, write run          -> exit 0, listingData.csv unchanged (the fixture is sound);
      2. en-us search terms at exactly 21 words           -> exit 0 (the cap is a limit, not a threshold below it);
      3. en-us search terms at 22 words                   -> exit non-zero, the output names en-us and 22, and the
                                                             CSV is byte for byte what it was (nothing written);
      4. ru search terms at 22 words                      -> exit non-zero, the output names ru and 22, nothing written;
      5. the committed CSV preceded by a BOM, write run   -> exit 0, the output equals the committed bytes and
                                                             does not start with a BOM (the one-dialect reading,
                                                             PROPOSAL-2026-11-15-fastmediasorter-lite-bom-dialect (a)).

    Case 5 pins today's behaviour of an open catalog question; if the catalog answers (b) or (c) this case is
    the one to change together with the builder.

.NOTES
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL (a fixture disagreed), 2 COULD NOT VERIFY (an input of the
    builder is missing, or the check itself threw). The last line is `ListingBuilderFixtures: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$check = 'ListingBuilderFixtures'
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the check itself failed'
}

$builder = Join-Path $repo 'publishing\store\Build-ListingData.ps1'
$doc     = Join-Path $repo 'docs\guides\STORE_PUBLISHING.md'
$facts   = Join-Path $repo 'tools\site-facts.json'
$csv     = Join-Path $repo 'publishing\store\listingData.csv'
foreach ($f in @($builder, $doc, $facts, $csv)) {
    if (-not (Test-Path -LiteralPath $f)) { Exit-Verdict $check 2 "input not found: $f" }
}
Write-Subject $check 'publishing/store/Build-ListingData.ps1 against scratch copies of its inputs (search-term word cap, BOM export round trip)'

$work = Join-Path ([IO.Path]::GetTempPath()) ('fms-listing-fixtures-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$scratchFiles = [Collections.Generic.List[string]]::new()
function New-ScratchFile([string]$Name, [byte[]]$Bytes) {
    $p = Join-Path $work $Name
    [IO.File]::WriteAllBytes($p, $Bytes)
    $scratchFiles.Add($p)
    return $p
}

$findings = [Collections.Generic.List[string]]::new()
try {
    $docBytes   = [IO.File]::ReadAllBytes($doc)
    $csvBytes   = [IO.File]::ReadAllBytes($csv)
    $factsText  = [IO.File]::ReadAllText($facts, (New-Object Text.UTF8Encoding($false)))
    $utf8NoBom  = New-Object Text.UTF8Encoding($false)
    $docCopy    = New-ScratchFile 'STORE_PUBLISHING.md' $docBytes

    function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

    # Runs the real builder in its own process; returns its exit code and everything it printed.
    function Invoke-Builder([string]$FactsFile, [string]$CsvFile) {
        $out = & pwsh -NoProfile -File $builder -DocPath $docCopy -FactsPath $FactsFile -CsvPath $CsvFile 2>&1 | Out-String
        return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $out }
    }

    # A facts copy whose <language> search terms hold exactly $Words words: the first term is padded with
    # distinct made-up words (none on the forbidden list), the others are left as committed.
    function New-FactsWithWords([string]$Name, [string]$Language, [int]$Words) {
        $json = $factsText | ConvertFrom-Json
        $terms = @($json.discoverability.storeSearchTerms.$Language | ForEach-Object { [string]$_ })
        $sum = 0
        foreach ($t in $terms) { $sum += [regex]::Matches($t, '\S+').Count }
        $pad = $Words - $sum
        if ($pad -lt 0) { throw "the committed $Language terms already hold $sum words, more than the fixture's $Words" }
        if ($pad -gt 0) {
            $extra = (1..$pad | ForEach-Object { "zqx$_" }) -join ' '
            $terms[0] = $terms[0] + ' ' + $extra
        }
        $json.discoverability.storeSearchTerms.$Language = $terms
        return New-ScratchFile $Name ($utf8NoBom.GetBytes(($json | ConvertTo-Json -Depth 64)))
    }

    # 1. the fixture is sound: committed inputs in, the committed CSV out
    $csv1 = New-ScratchFile 'listing-1.csv' $csvBytes
    $factsCopy = New-ScratchFile 'facts-1.json' ($utf8NoBom.GetBytes($factsText))
    $r = Invoke-Builder $factsCopy $csv1
    if ($r.Code -ne 0) { $findings.Add("case 1: the builder exited $($r.Code) on the committed inputs: $($r.Output.Trim())") }
    elseif ((Get-Hash $csv1) -ne (Get-Hash $csv)) { $findings.Add('case 1: a write run on copies of the committed inputs changed the CSV (the committed file is not what the builder writes - run Build-ListingData.ps1 -Check)') }

    # 2. exactly 21 words passes
    $csv2 = New-ScratchFile 'listing-2.csv' $csvBytes
    $r = Invoke-Builder (New-FactsWithWords 'facts-2.json' 'en-us' 21) $csv2
    if ($r.Code -ne 0) { $findings.Add("case 2: en-us at exactly 21 words was refused (exit $($r.Code)): $($r.Output.Trim())") }

    # 3 and 4. 22 words is an overflow: non-zero, the language and the count named, nothing written
    foreach ($case in @(@(3, 'en-us'), @(4, 'ru'))) {
        $n = $case[0]; $lang = $case[1]
        $csvN = New-ScratchFile "listing-$n.csv" $csvBytes
        $before = Get-Hash $csvN
        $r = Invoke-Builder (New-FactsWithWords "facts-$n.json" $lang 22) $csvN
        if ($r.Code -eq 0) { $findings.Add("case ${n}: $lang at 22 words was accepted (exit 0)") }
        if ($r.Output -notmatch ('(?m)' + [regex]::Escape($lang) + ' search terms hold 22 words')) { $findings.Add("case ${n}: the refusal does not name $lang and 22 words: $($r.Output.Trim())") }
        if ((Get-Hash $csvN) -ne $before) { $findings.Add("case ${n}: the CSV was written although the cap was exceeded") }
    }

    # 5. a BOM export is written back without the BOM (the rule 8 default dialect)
    $bom = [byte[]](0xEF, 0xBB, 0xBF)
    $csv5 = New-ScratchFile 'listing-5.csv' ([byte[]]($bom + $csvBytes))
    $r = Invoke-Builder $factsCopy $csv5
    $after = [IO.File]::ReadAllBytes($csv5)
    if ($r.Code -ne 0) { $findings.Add("case 5: the builder exited $($r.Code) on a BOM export: $($r.Output.Trim())") }
    elseif ($after.Length -ge 3 -and $after[0] -eq 0xEF -and $after[1] -eq 0xBB -and $after[2] -eq 0xBF) { $findings.Add('case 5: the output of a BOM export starts with a BOM') }
    elseif ((Get-Hash $csv5) -ne (Get-Hash $csv)) { $findings.Add('case 5: the output of a BOM export differs from the committed CSV beyond the BOM') }
}
finally {
    foreach ($p in $scratchFiles) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force } }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Force }
}

if ($findings.Count -gt 0) {
    foreach ($f in $findings) { Write-Host "  $f" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($findings.Count) fixture(s) disagreed"
}
Exit-Verdict $check 0 '5 cases: sound copy, 21 words pass, 22 words refused for en-us and ru with nothing written, BOM export written without the BOM'
