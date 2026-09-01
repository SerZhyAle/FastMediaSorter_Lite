<#
.SYNOPSIS
    Fill listingData.csv from the copy blocks in docs/guides/STORE_PUBLISHING.md.

.DESCRIPTION
    Partner Center's listing page can import a CSV, and it exports the same shape - which is why
    listingData.csv exists in this repo. It was maintained by hand, and drifted: the RU column had
    exactly one Feature filled out of twenty, and its Description predated four releases of new
    capability.

    STORE_PUBLISHING.md is the source of truth for the words. This script copies them into the CSV
    so the two cannot disagree, touching only these rows:

        Description, ReleaseNotes, Feature1..Feature20   (columns en-us and ru)

    Everything else - screenshot paths, logo slots, search terms, the Title/ShortDescription pair,
    the ID and Type columns - is carried through byte-for-byte from the existing file, because those
    hold Partner Center asset URLs that must not be regenerated from anything but a real export.

    WHY IT PARSES RATHER THAN ROUND-TRIPS: Import-Csv/Export-Csv would re-quote every field and drop
    the file's minimal-quoting style. The CSV is read with TextFieldParser (which handles the
    newlines inside Description) and written back with the same minimal quoting, CRLF row endings
    and UTF-8 BOM that Partner Center produced.

    AFTER A MANUAL EDIT IN PARTNER CENTER: re-export from there and diff before overwriting, or the
    asset URLs in this file go stale. This script is not a substitute for that export.

.EXAMPLE
    pwsh -NoProfile -File .\publishing\store\Build-ListingData.ps1
    pwsh -NoProfile -File .\publishing\store\Build-ListingData.ps1 -Check
#>
[CmdletBinding()]
param(
    # Verify the committed CSV already matches the document; write nothing. Exit 1 on a difference.
    [switch] $Check
)
$ErrorActionPreference = 'Stop'

$StoreDir = $PSScriptRoot
# publishing/store -> repo root is two levels up (see REPOSITORY_LAYOUT.md).
$RepoRoot = Split-Path (Split-Path $StoreDir -Parent) -Parent
$Doc      = Join-Path $RepoRoot 'docs\guides\STORE_PUBLISHING.md'
$Csv      = Join-Path $StoreDir 'listingData.csv'

foreach ($f in @($Doc, $Csv)) {
    if (-not (Test-Path $f)) { throw "Not found: $f" }
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

# What's new: one bullet per line, kept as written (the 1500-char cap was measured on this shape).
function Join-Lines {
    param([string[]] $Lines)
    return (($Lines | Where-Object { $_.Trim().Length -gt 0 }) -join "`n")
}

$descriptionEn = Join-Paragraphs (Get-FencedBlock '### Description (EN)')
$descriptionRu = Join-Paragraphs (Get-FencedBlock '### Description (RU')
$featuresEn    = @(Get-FencedBlock '### Product features (one per line') | Where-Object { $_.Trim() }
$featuresRu    = @(Get-FencedBlock '### Product features (RU')            | Where-Object { $_.Trim() }

# The What's new heading holds two fenced blocks, EN then RU, each behind a bold label.
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

# --- 2. Sanity limits, before anything is written --------------------------------------------
# These are Partner Center's, and every one of them has bitten this listing at least once.
$problems = @()
if ($releaseNotesEn.Length -gt 1500) { $problems += "ReleaseNotes EN is $($releaseNotesEn.Length) chars, cap is 1500" }
if ($releaseNotesRu.Length -gt 1500) { $problems += "ReleaseNotes RU is $($releaseNotesRu.Length) chars, cap is 1500" }
if ($descriptionEn.Length -gt 10000) { $problems += "Description EN is $($descriptionEn.Length) chars, cap is 10000" }
if ($descriptionRu.Length -gt 10000) { $problems += "Description RU is $($descriptionRu.Length) chars, cap is 10000" }
if ($featuresEn.Count -gt 20) { $problems += "EN feature list has $($featuresEn.Count) lines, cap is 20" }
if ($featuresRu.Count -gt 20) { $problems += "RU feature list has $($featuresRu.Count) lines, cap is 20" }
if ($featuresEn.Count -ne $featuresRu.Count) {
    $problems += "EN has $($featuresEn.Count) features, RU has $($featuresRu.Count) - the lists are meant to line up"
}
foreach ($f in ($featuresEn + $featuresRu)) {
    if ($f.Length -gt 200) { $problems += "Feature over 200 chars ($($f.Length)): $f" }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  ! $_" -ForegroundColor Red }
    throw "The copy in STORE_PUBLISHING.md does not fit Partner Center's limits (see above)."
}

# --- 3. Read the CSV, preserving everything this script does not own --------------------------
Add-Type -AssemblyName Microsoft.VisualBasic
$parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($Csv, [Text.Encoding]::UTF8)
$parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
$parser.SetDelimiters(',')
$parser.HasFieldsEnclosedInQuotes = $true
$parser.TrimWhiteSpace = $false

$rows = @()
try {
    while (-not $parser.EndOfData) { $rows += ,$parser.ReadFields() }
} finally {
    $parser.Close()
}
if ($rows.Count -lt 2) { throw "listingData.csv looks empty" }

$header = $rows[0]
$colEn  = [Array]::IndexOf($header, 'en-us')
$colRu  = [Array]::IndexOf($header, 'ru')
if ($colEn -lt 0 -or $colRu -lt 0) { throw "Expected 'en-us' and 'ru' columns in listingData.csv" }

# --- 4. Set the cells this script owns --------------------------------------------------------
$wanted = @{ 'Description' = @($descriptionEn, $descriptionRu)
             'ReleaseNotes' = @($releaseNotesEn, $releaseNotesRu) }
for ($i = 0; $i -lt 20; $i++) {
    $wanted["Feature$($i + 1)"] = @(
        $(if ($i -lt $featuresEn.Count) { $featuresEn[$i].Trim() } else { '' }),
        $(if ($i -lt $featuresRu.Count) { $featuresRu[$i].Trim() } else { '' })
    )
}

$changed = @()
for ($r = 1; $r -lt $rows.Count; $r++) {
    $name = $rows[$r][0]
    if (-not $wanted.ContainsKey($name)) { continue }
    $pair = $wanted[$name]
    foreach ($slot in @(@($colEn, 0), @($colRu, 1))) {
        if ($rows[$r][$slot[0]] -ne $pair[$slot[1]]) {
            $rows[$r][$slot[0]] = $pair[$slot[1]]
            $changed += "$name/$($header[$slot[0]])"
        }
    }
}

# --- 5. Write it back in Partner Center's own shape -------------------------------------------
# Minimal quoting, CRLF between rows, UTF-8 with BOM - matching the file that came out of the export.
function ConvertTo-CsvField {
    param([string] $Value)
    if ($null -eq $Value) { return '' }
    if ($Value -match '[",\r\n]') { return '"' + $Value.Replace('"', '""') + '"' }
    return $Value
}

$out = ($rows | ForEach-Object { (($_ | ForEach-Object { ConvertTo-CsvField $_ }) -join ',') }) -join "`r`n"
$out += "`r`n"

$existing = [IO.File]::ReadAllText($Csv, [Text.Encoding]::UTF8)
if ($Check) {
    if ($existing -eq $out) {
        Write-Host "listingData.csv matches STORE_PUBLISHING.md." -ForegroundColor Green
        exit 0
    }
    Write-Host "listingData.csv is out of date - re-run without -Check." -ForegroundColor Red
    $changed | Sort-Object -Unique | ForEach-Object { Write-Host "  would update $_" }
    exit 1
}

[IO.File]::WriteAllText($Csv, $out, (New-Object Text.UTF8Encoding($true)))

Write-Host "Wrote $Csv" -ForegroundColor Green
Write-Host ("  Description  EN {0,5} chars   RU {1,5} chars" -f $descriptionEn.Length, $descriptionRu.Length)
Write-Host ("  ReleaseNotes EN {0,5} chars   RU {1,5} chars   (cap 1500)" -f $releaseNotesEn.Length, $releaseNotesRu.Length)
Write-Host ("  Features     EN {0,5} lines   RU {1,5} lines   (cap 20)" -f $featuresEn.Count, $featuresRu.Count)
if ($changed.Count -eq 0) {
    Write-Host "  no cell changed - it was already in sync"
} else {
    Write-Host ("  updated {0} cells: {1}" -f ($changed | Sort-Object -Unique).Count, (($changed | Sort-Object -Unique) -join ', '))
}
