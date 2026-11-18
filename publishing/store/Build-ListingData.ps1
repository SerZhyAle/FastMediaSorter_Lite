<#
.SYNOPSIS
    Fill listingData.csv from the copy blocks in docs/guides/STORE_PUBLISHING.md and the Store search
    terms of tools/site-facts.json, and write it in the shape WINDOWS-STORE rule 8 requires.

.DESCRIPTION
    Partner Center's listing page can import a CSV, and it exports the same shape - which is why
    listingData.csv exists in this repo. It was maintained by hand, and drifted: the RU column had
    exactly one Feature filled out of twenty, and its Description predated four releases of new
    capability.

    STORE_PUBLISHING.md is the source of truth for the words, and the `discoverability.storeSearchTerms`
    block of tools/site-facts.json for the search terms (the one worksheet every channel's discoverability
    fields derive from). This script copies them into the CSV so they cannot disagree, touching only
    these rows:

        Description, ReleaseNotes, Feature1..Feature20, SearchTerm1..SearchTerm7   (columns en-us and ru)

    Everything else - screenshot paths, logo slots, the Title/ShortDescription pair, the ID and Type
    columns - is carried through unchanged from the existing file, because those hold Partner Center
    asset URLs that must not be regenerated from anything but a real export.

    THE SHAPE OF THE FILE (WINDOWS-STORE rule 8): UTF-8 WITHOUT a BOM, CRLF as the only line break -
    inside a quoted cell as much as between records - no trailing newline, embedded quotes doubled.
    Every cell is canonicalised to CRLF on the way out, and the writer refuses an output that holds a
    BOM, a bare LF or CR, or a trailing newline. (Until 2026-10-13 this script wrote a BOM and left a
    bare LF inside the multi-line cells; rule 8 measured both as refusals at Partner Center.)

    CAPS (rule 9) are counted in the unit Partner Center counts - characters after the line breaks the
    file will actually carry, so a CRLF is two - and one cell over its cap stops the build before
    anything is written, naming the language, the field, the length and the deck file. The sum of words
    over a language's seven search-term cells is capped at 21 (WINDOWS-STORE 0.2 (a); a word is a
    non-whitespace run), and an overflow names the language and the count the same way.

    A BOM EXPORT is read (the BOM is stripped) and written back in the rule 8 default dialect, without the
    BOM, whether or not the run changes copy: this builder keeps one dialect, the default. Whether a
    no-change round trip should carry the export's BOM instead is an open catalog question
    (PROPOSAL-2026-11-15-fastmediasorter-lite-bom-dialect, option (a) recommended = what happens here);
    tests\Integration\ListingBuilderFixtures.ps1 pins the current behaviour.

    WHY IT PARSES BY HAND: Import-Csv/Export-Csv would re-quote every field and drop the file's
    minimal-quoting style, and TextFieldParser silently drops an empty line inside a quoted cell. The
    reader below keeps every cell exactly as written, whatever line break it holds.

    NOT DONE HERE (still dated exceptions in the catalog's registry): the file is not started from a
    fresh export (rule 6) and there is no emptied committed export with a fill-nothing round trip
    (rule 13). AFTER A MANUAL EDIT IN PARTNER CENTER: re-export from there and diff before overwriting,
    or the asset URLs in this file go stale. This script is not a substitute for that export.

.EXAMPLE
    pwsh -NoProfile -File .\publishing\store\Build-ListingData.ps1
    pwsh -NoProfile -File .\publishing\store\Build-ListingData.ps1 -Check
#>
[CmdletBinding()]
param(
    # Verify the committed CSV is byte for byte what this script would write; write nothing. Exit 1 on a difference.
    [switch] $Check,
    # Alternative inputs, for the fixtures of tests\Integration\ListingBuilderFixtures.ps1 (overflow and BOM round trip);
    # each defaults to the committed file, and a normal run names none of them.
    [string] $DocPath,
    [string] $FactsPath,
    [string] $CsvPath
)
$ErrorActionPreference = 'Stop'

$StoreDir = $PSScriptRoot
# publishing/store -> repo root is two levels up (see REPOSITORY_LAYOUT.md).
$RepoRoot = Split-Path (Split-Path $StoreDir -Parent) -Parent
$Doc      = if ($DocPath)   { $DocPath }   else { Join-Path $RepoRoot 'docs\guides\STORE_PUBLISHING.md' }
$Facts    = if ($FactsPath) { $FactsPath } else { Join-Path $RepoRoot 'tools\site-facts.json' }
$Csv      = if ($CsvPath)   { $CsvPath }   else { Join-Path $StoreDir 'listingData.csv' }
$DeckName = 'docs/guides/STORE_PUBLISHING.md'

foreach ($f in @($Doc, $Facts, $Csv)) {
    if (-not (Test-Path $f)) { throw "Not found: $f" }
}

# One place for the two byte rules of rule 8: the encoding has no BOM, and every line break is CRLF.
$Utf8NoBom = New-Object Text.UTF8Encoding($false)
function ConvertTo-Crlf {
    param([string] $Value)
    if ($null -eq $Value) { return '' }
    return [regex]::Replace($Value, '\r\n|\r|\n', "`r`n")
}

# --- 1. Pull the copy blocks out of the guide ------------------------------------------------
# Each block is the fenced section that follows a known "### " heading. Headings are matched by
# their distinctive prefix so a later edit to the parenthetical part does not break the script.
$docLines = Get-Content $Doc

function Get-FencedBlock {
    param([string] $HeadingPrefix)

    $start = $null
    for ($i = 0; $i -lt $docLines.Count; $i++) {
        if ($docLines[$i].StartsWith($HeadingPrefix)) { $start = $i; break }
    }
    if ($null -eq $start) { throw "Heading not found in STORE_PUBLISHING.md: $HeadingPrefix" }

    $open = $null
    for ($i = $start + 1; $i -lt $docLines.Count; $i++) {
        if ($docLines[$i] -eq '```') { $open = $i; break }
        # A new heading before the fence means the block is gone or was moved.
        if ($docLines[$i].StartsWith('### ')) { throw "No fenced block under: $HeadingPrefix" }
    }
    if ($null -eq $open) { throw "No fenced block under: $HeadingPrefix" }

    $close = $null
    for ($i = $open + 1; $i -lt $docLines.Count; $i++) {
        if ($docLines[$i] -eq '```') { $close = $i; break }
    }
    if ($null -eq $close) { throw "Unterminated fenced block under: $HeadingPrefix" }

    return ,@($docLines[($open + 1)..($close - 1)])
}

# Description: the paragraphs are hard-wrapped in the document for review; the Store wants flowing
# text, so unwrap inside a paragraph and keep the blank lines between paragraphs.
function Join-Paragraphs {
    param([string[]] $Lines)

    $paras = @()
    $current = @()
    foreach ($line in $Lines) {
        if ($line.Trim().Length -eq 0) {
            if ($current.Count -gt 0) { $paras += ($current -join ' '); $current = @() }
        } else {
            $current += $line.Trim()
        }
    }
    if ($current.Count -gt 0) { $paras += ($current -join ' ') }
    return ($paras -join "`n`n")
}

# What's new: one bullet per line, kept as written (the 1500-char cap is measured on this shape, after CRLF).
function Join-Lines {
    param([string[]] $Lines)
    return (($Lines | Where-Object { $_.Trim().Length -gt 0 }) -join "`n")
}

$descriptionEn = Join-Paragraphs (Get-FencedBlock '### Description (EN)')
$descriptionRu = Join-Paragraphs (Get-FencedBlock '### Description (RU')
$featuresEn    = @(Get-FencedBlock '### Product features (one per line') | Where-Object { $_.Trim() }
$featuresRu    = @(Get-FencedBlock '### Product features (RU')            | Where-Object { $_.Trim() }

# The What's new heading holds fenced blocks: EN, RU, and optionally UK, each behind a bold label.
$whatsNewStart = ($docLines | Select-String -SimpleMatch "### What's new in this version" | Select-Object -First 1).LineNumber
if (-not $whatsNewStart) { throw "What's new heading not found" }
$fences = @()
for ($i = $whatsNewStart; $i -lt $docLines.Count; $i++) {
    if ($docLines[$i].StartsWith('### ')) { break }
    if ($docLines[$i] -eq '```') { $fences += $i }
}
if ($fences.Count -lt 4) { throw "Expected an EN and an RU block under What's new" }
$releaseNotesEn = Join-Lines $docLines[($fences[0] + 1)..($fences[1] - 1)]
$releaseNotesRu = Join-Lines $docLines[($fences[2] + 1)..($fences[3] - 1)]
$releaseNotesUk = if ($fences.Count -ge 6) { Join-Lines $docLines[($fences[4] + 1)..($fences[5] - 1)] } else { $releaseNotesEn }

# All 13 languages supported by the product (portfolio standard, see tools/site-facts.json).
# WINDOWS-STORE rule 16: What's new is authored only in the languages the product proofreads
# (EN, RU, UK); the other ten machine-translated columns carry the English text.
$deckLangs = @(
    @{ Code = 'en-us';   Desc = $descriptionEn; Feats = $featuresEn; Notes = $releaseNotesEn }
    @{ Code = 'ru';      Desc = $descriptionRu; Feats = $featuresRu; Notes = $releaseNotesRu }
    @{ Code = 'uk';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (UK)')); Feats = @(Get-FencedBlock '### Product features (UK') | Where-Object { $_.Trim() }; Notes = $releaseNotesUk }
    @{ Code = 'de';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (DE)')); Feats = @(Get-FencedBlock '### Product features (DE') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'it';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (IT)')); Feats = @(Get-FencedBlock '### Product features (IT') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'es';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (ES)')); Feats = @(Get-FencedBlock '### Product features (ES') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'fr';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (FR)')); Feats = @(Get-FencedBlock '### Product features (FR') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'pt-br';   Desc = (Join-Paragraphs (Get-FencedBlock '### Description (PT-BR)')); Feats = @(Get-FencedBlock '### Product features (PT-BR') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'ar';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (AR)')); Feats = @(Get-FencedBlock '### Product features (AR') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'hi';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (HI)')); Feats = @(Get-FencedBlock '### Product features (HI') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'bn';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (BN)')); Feats = @(Get-FencedBlock '### Product features (BN') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'ur';      Desc = (Join-Paragraphs (Get-FencedBlock '### Description (UR)')); Feats = @(Get-FencedBlock '### Product features (UR') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
    @{ Code = 'zh-hans'; Desc = (Join-Paragraphs (Get-FencedBlock '### Description (ZH-HANS)')); Feats = @(Get-FencedBlock '### Product features (ZH-HANS') | Where-Object { $_.Trim() }; Notes = $releaseNotesEn }
)

# --- 2. The search terms: the worksheet, one list per language ---------------------------------
$factsJson = [IO.File]::ReadAllText($Facts, $Utf8NoBom) | ConvertFrom-Json
$disc      = $factsJson.discoverability
if (-not $disc -or -not $disc.storeSearchTerms) { throw "tools/site-facts.json has no discoverability.storeSearchTerms block" }
$termsEn = @($disc.storeSearchTerms.'en-us' | ForEach-Object { [string]$_ })
$termsRu = @($disc.storeSearchTerms.'ru'    | ForEach-Object { [string]$_ })
$forbidden = @($disc.forbiddenWords | ForEach-Object { [string]$_ })

# --- 3. Read the CSV, keeping every cell exactly as written ------------------------------------
$rawBytes = [IO.File]::ReadAllBytes($Csv)
$hadBom = ($rawBytes.Length -ge 3 -and $rawBytes[0] -eq 0xEF -and $rawBytes[1] -eq 0xBB -and $rawBytes[2] -eq 0xBF)
$text = (New-Object Text.UTF8Encoding($false, $true)).GetString($rawBytes)
if ($hadBom) { $text = $text.Substring(1) }
$bareLfBefore = [regex]::Matches($text, '(?<!\r)\n|\r(?!\n)').Count
$endsWithNewlineBefore = $text.EndsWith("`n")

function Read-CsvRecords {
    param([string] $Text)
    $records = New-Object 'System.Collections.Generic.List[string[]]'
    $fields  = New-Object 'System.Collections.Generic.List[string]'
    $cell    = New-Object Text.StringBuilder
    $inQuote = $false
    $started = $false          # something of the current record has been read
    $n = $Text.Length
    $i = 0
    while ($i -lt $n) {
        $c = $Text[$i]
        if ($inQuote) {
            if ($c -eq '"') {
                if ($i + 1 -lt $n -and $Text[$i + 1] -eq '"') { [void]$cell.Append('"'); $i += 2; continue }
                $inQuote = $false; $i++; continue
            }
            [void]$cell.Append($c); $i++; continue
        }
        if ($c -eq '"') { $inQuote = $true; $started = $true; $i++; continue }
        if ($c -eq ',') { $fields.Add($cell.ToString()); [void]$cell.Clear(); $started = $true; $i++; continue }
        if ($c -eq "`r" -or $c -eq "`n") {
            if ($c -eq "`r" -and $i + 1 -lt $n -and $Text[$i + 1] -eq "`n") { $i++ }
            $fields.Add($cell.ToString()); [void]$cell.Clear()
            $records.Add($fields.ToArray()); $fields.Clear(); $started = $false
            $i++; continue
        }
        [void]$cell.Append($c); $started = $true; $i++
    }
    if ($inQuote) { throw 'listingData.csv ends inside a quoted cell' }
    if ($started -or $fields.Count -gt 0 -or $cell.Length -gt 0) {
        $fields.Add($cell.ToString()); $records.Add($fields.ToArray())
    }
    return ,$records
}

$records = Read-CsvRecords $text
if ($records.Count -lt 2) { throw "listingData.csv looks empty" }
$rows = @($records | ForEach-Object { , ([string[]]$_) })

$header = $rows[0]
$colEn  = [Array]::IndexOf($header, 'en-us')
$colRu  = [Array]::IndexOf($header, 'ru')
if ($colEn -lt 0 -or $colRu -lt 0) { throw "Expected 'en-us' and 'ru' columns in listingData.csv" }
foreach ($r in $rows) {
    if ($r.Length -ne $header.Length) { throw "listingData.csv has a record of $($r.Length) cells where the header has $($header.Length) (row starting '$($r[0])')" }
}

# --- 4. Sanity limits, before anything is written --------------------------------------------
# These are Partner Center's (WINDOWS-STORE rule 9), and every one of them has bitten a listing at least
# once. A cap is counted after CRLF normalisation: a line break is two characters in the file.
$problems = @()
$SearchTermWordCap = 21
function Test-Cap {
    param([string] $Language, [string] $Field, [string] $Value, [int] $Cap)
    $len = (ConvertTo-Crlf $Value).Length
    if ($len -gt $Cap) {
        $script:problems += "$Language $Field is $len characters after CRLF normalisation, cap is $Cap (deck: $DeckName)"
    }
}
foreach ($dl in $deckLangs) {
    $lang = $dl.Code
    $terms = if ($disc.storeSearchTerms.PSObject.Properties[$lang]) { @($disc.storeSearchTerms.$lang | ForEach-Object { [string]$_ }) } else { @() }
    Test-Cap $lang 'ReleaseNotes' $dl.Notes 1500
    Test-Cap $lang 'Description'  $dl.Desc 10000
    $feats = @($dl.Feats | ForEach-Object { $_ } | Where-Object { $_.Trim() })
    if ($feats.Count -ne 20) { $problems += "$lang feature list has $($feats.Count) lines, cap is 20 (deck: $DeckName)" }
    foreach ($f in $feats) { Test-Cap $lang 'Feature' $f 200 }
    if ($terms.Count -gt 7) { $problems += "$lang has $($terms.Count) search terms, cap is 7 (tools/site-facts.json)" }
    if (@($terms | Select-Object -Unique).Count -ne $terms.Count) { $problems += "$lang repeats a search term (tools/site-facts.json)" }
    # WINDOWS-STORE 0.2 (a): the sum of words over a language's seven search-term cells is capped at 21; a word is a
    # non-whitespace run. A twenty-second word can make Partner Center refuse the whole import with an empty error report.
    $words = 0
    foreach ($t in $terms) { $words += [regex]::Matches($t, '\S+').Count }
    if ($words -gt $SearchTermWordCap) { $problems += "$lang search terms hold $words words, the cap over the seven cells is $SearchTermWordCap (tools/site-facts.json)" }
    foreach ($t in $terms) {
        foreach ($w in $forbidden) {
            if ($t -match ('(?i)(?<![\p{L}\p{N}])' + [regex]::Escape($w) + '(?![\p{L}\p{N}])')) { $problems += "$lang search term '$t' uses '$w', a word on the forbidden list (tools/site-facts.json)" }
        }
    }
}
# ShortDescription is not ours to write, but a cell over its cap loses the language all the same.
for ($r = 1; $r -lt $rows.Count; $r++) {
    if ($rows[$r][0] -eq 'ShortDescription') {
        if ($colEn -ge 0) { Test-Cap 'en-us' 'ShortDescription' $rows[$r][$colEn] 1000 }
        if ($colRu -ge 0) { Test-Cap 'ru'    'ShortDescription' $rows[$r][$colRu] 1000 }
    }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  ! $_" -ForegroundColor Red }
    throw "The copy does not fit Partner Center's limits (see above); nothing was written."
}

# --- 5. Set the cells this script owns --------------------------------------------------------
$deckMap = @{}
foreach ($dl in $deckLangs) {
    $feats = @($dl.Feats | ForEach-Object { $_ } | Where-Object { $_.Trim() })
    $terms = if ($disc.storeSearchTerms.PSObject.Properties[$dl.Code]) { @($disc.storeSearchTerms.$($dl.Code) | ForEach-Object { [string]$_ }) } else { @() }
    $colWanted = @{
        'Description'  = $dl.Desc
        'ReleaseNotes' = $dl.Notes
    }
    for ($i = 0; $i -lt 20; $i++) {
        $colWanted["Feature$($i + 1)"] = if ($i -lt $feats.Count) { $feats[$i].Trim() } else { '' }
    }
    for ($i = 0; $i -lt 7; $i++) {
        $colWanted["SearchTerm$($i + 1)"] = if ($i -lt $terms.Count) { $terms[$i].Trim() } else { '' }
    }
    $deckMap[$dl.Code] = $colWanted
}

$changed = @()
for ($c = 0; $c -lt $header.Length; $c++) {
    $colName = $header[$c]
    if (-not $deckMap.ContainsKey($colName)) { continue }
    $colWanted = $deckMap[$colName]
    for ($r = 1; $r -lt $rows.Count; $r++) {
        $name = $rows[$r][0]
        if (-not $colWanted.ContainsKey($name)) { continue }
        $wantedVal = $colWanted[$name]
        if ((ConvertTo-Crlf $rows[$r][$c]) -ne (ConvertTo-Crlf $wantedVal)) {
            $changed += "$name/$colName"
        }
        $rows[$r][$c] = $wantedVal
    }
}

# --- 6. Write it back in the shape rule 8 requires --------------------------------------------
# Minimal quoting, CRLF between rows and inside cells, UTF-8 without a BOM, no trailing newline.
function ConvertTo-CsvField {
    param([string] $Value)
    $v = ConvertTo-Crlf $Value
    if ($v -match '[",\r\n]') { return '"' + $v.Replace('"', '""') + '"' }
    return $v
}

$out = ($rows | ForEach-Object { (($_ | ForEach-Object { ConvertTo-CsvField $_ }) -join ',') }) -join "`r`n"

# The writer refuses what rule 8 refuses, whatever produced it.
if ($out.Length -gt 0 -and $out[0] -eq [char]0xFEFF) { throw 'Refusing to write: the output starts with a BOM (WINDOWS-STORE rule 8).' }
if ([regex]::IsMatch($out, '(?<!\r)\n|\r(?!\n)')) { throw 'Refusing to write: the output holds a bare LF or CR (WINDOWS-STORE rule 8).' }
if ($out.EndsWith("`n")) { throw 'Refusing to write: the output ends in a newline (WINDOWS-STORE rule 8).' }

$outBytes = $Utf8NoBom.GetBytes($out)

if ($Check) {
    $same = ($outBytes.Length -eq $rawBytes.Length)
    if ($same) {
        for ($i = 0; $i -lt $outBytes.Length; $i++) { if ($outBytes[$i] -ne $rawBytes[$i]) { $same = $false; break } }
    }
    if ($same) {
        Write-Host "listingData.csv matches STORE_PUBLISHING.md, the search terms of tools/site-facts.json and WINDOWS-STORE rule 8 (no BOM, CRLF only, no trailing newline)." -ForegroundColor Green
        exit 0
    }
    Write-Host "listingData.csv is out of date - re-run without -Check." -ForegroundColor Red
    if ($hadBom) { Write-Host "  starts with a BOM (rule 8 refuses it)" }
    if ($bareLfBefore -gt 0) { Write-Host "  holds $bareLfBefore bare LF or CR (rule 8: CRLF only)" }
    if ($endsWithNewlineBefore) { Write-Host "  ends in a newline (rule 8: none)" }
    $changed | Sort-Object -Unique | ForEach-Object { Write-Host "  would update $_" }
    exit 1
}

[IO.File]::WriteAllBytes($Csv, $outBytes)

Write-Host "Wrote $Csv" -ForegroundColor Green
Write-Host ("  Description  EN {0,5} chars   RU {1,5} chars   (after CRLF)" -f (ConvertTo-Crlf $descriptionEn).Length, (ConvertTo-Crlf $descriptionRu).Length)
Write-Host ("  ReleaseNotes EN {0,5} chars   RU {1,5} chars   (cap 1500, after CRLF)" -f (ConvertTo-Crlf $releaseNotesEn).Length, (ConvertTo-Crlf $releaseNotesRu).Length)
Write-Host ("  Features     EN {0,5} lines   RU {1,5} lines   (cap 20)" -f @($featuresEn).Count, @($featuresRu).Count)
Write-Host ("  SearchTerms  EN {0,5}         RU {1,5}         (cap 7)" -f $termsEn.Count, $termsRu.Count)
Write-Host ("  shape        BOM {0}, bare LF/CR {1} -> 0, trailing newline {2} -> no" -f $(if ($hadBom) { 'removed' } else { 'none' }), $bareLfBefore, $(if ($endsWithNewlineBefore) { 'removed' } else { 'none' }))
if ($changed.Count -eq 0) {
    Write-Host "  no cell of copy changed - it was already in sync"
} else {
    Write-Host ("  updated {0} cells: {1}" -f ($changed | Sort-Object -Unique).Count, (($changed | Sort-Object -Unique) -join ', '))
}
