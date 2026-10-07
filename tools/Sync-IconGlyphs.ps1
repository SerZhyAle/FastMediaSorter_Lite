<#
.SYNOPSIS
    Imports the catalog glyphs this product renders, or checks that the vendored copies still match.

.DESCRIPTION
    The iconography catalog (contracts ICON-SET / ICON-RENDER / ICON-EXTERNAL) is the source of truth.
    This repository vendors only the glyph files it renders, into assets/glyphs/ with their SHA-256 in
    assets/glyphs/PROVENANCE.txt, plus the vocabulary its tests read, into
    tests/Lite.Tests/TestData/icon-vocabulary/. Never hand-edit either folder - re-run this script.

    Without -Check the script re-imports both and rewrites both provenance files.
    With -Check it writes nothing and compares the vendored files against the CATALOG, not against the
    local provenance record: a glyph redrawn in the catalog is drift even while every local hash still
    matches PROVENANCE.txt.

    The catalog root: -CatalogRoot, then FMS_CONTRACTS_ROOT in the process, then the same variable at
    user scope. CLAUDE.md names the location; this script never carries a literal path to it.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - -Check: every vendored file matches the catalog; import: the import completed.
      1 - -Check: drift - a vendored file differs from the catalog, is missing, is not declared, or a
          provenance record disagrees with the files or the catalog.
      2 - could not verify / could not import: no catalog root, or the catalog is unreachable or lacks
          a declared glyph or the vocabulary.
#>
[CmdletBinding()]
param(
    [string]$CatalogRoot = '',
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# An unexpected terminating error must not surface as exit 1, which reads as drift.
trap {
    Write-Host "  $($_.Exception.Message)"
    Write-Host 'Sync-IconGlyphs: COULD NOT VERIFY (the script failed)'
    exit 2
}

# This is deliberately a small, declared product subset.  The catalog remains the
# source of truth; adding a mapped meaning means adding it here as well.
$glyphIds = @(
    'action.add', 'action.choose-file', 'action.choose-folder', 'action.convert-to-video', 'action.copy', 'action.crop', 'action.delete', 'action.download', 'action.edit',
    'action.extract-text', 'action.refresh', 'action.rename', 'action.search', 'action.send-to',
    'action.share', 'action.translate',
    'app.info', 'app.settings', 'content.apps', 'content.audio', 'content.disk-container', 'content.folder', 'content.image',
    'content.video', 'device.phone', 'view.screen',
    'media.exit-fullscreen', 'media.fullscreen', 'media.mute', 'media.next', 'media.pause',
    'media.play', 'media.previous', 'media.random', 'media.slideshow', 'media.volume',
    'nav.collapse', 'nav.expand', 'nav.more', 'nav.scroll-top', 'source.recent', 'source.resource',
    'status.error', 'status.locked', 'status.ok', 'status.unknown', 'status.warning',
    'system.internet', 'system.wifi', 'tool.brush', 'tool.oval', 'tool.rectangle', 'view.grid'
)

$subject = 'Sync-IconGlyphs'

function Stop-Verdict([int]$Code, [string]$Word, [string[]]$Lines = @()) {
    foreach ($l in $Lines) { Write-Host "  $l" }
    Write-Host "${subject}: $Word"
    exit $Code
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

if (-not $CatalogRoot) { $CatalogRoot = $env:FMS_CONTRACTS_ROOT }
if (-not $CatalogRoot) { $CatalogRoot = [Environment]::GetEnvironmentVariable('FMS_CONTRACTS_ROOT', 'User') }
if (-not $CatalogRoot) {
    Stop-Verdict 2 'COULD NOT VERIFY' @('no catalog root: pass -CatalogRoot or set FMS_CONTRACTS_ROOT (CLAUDE.md names the location)')
}

$iconography = Join-Path $CatalogRoot 'iconography'
$sourceGlyphs = Join-Path $iconography 'glyphs'
$sourceVocabulary = Join-Path $iconography 'vocabulary.jsonl'
$sourceReadme = Join-Path $iconography 'README.md'
foreach ($required in @($sourceGlyphs, $sourceVocabulary, $sourceReadme)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Stop-Verdict 2 'COULD NOT VERIFY' @("the iconography catalog is unreachable: '$required' was not found")
    }
}
$missingInCatalog = @($glyphIds | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sourceGlyphs ($_ + '.svg'))) })
if ($missingInCatalog.Count -gt 0) {
    Stop-Verdict 2 'COULD NOT VERIFY' @($missingInCatalog | ForEach-Object { "declared glyph '$_' is missing from the catalog" })
}

# "ICON-SET 0.13; ICON-RENDER 0.11; ICON-EXTERNAL 0.9", read from the contract blocks.
$contractVersions = [System.Collections.Generic.List[string]]::new()
$pendingId = $null
foreach ($line in Get-Content -LiteralPath $sourceReadme) {
    if ($line -match '^id:\s*(\S+)') { $pendingId = $Matches[1] }
    elseif ($line -match '^version:\s*(\S+)' -and $pendingId) { $contractVersions.Add("$pendingId $($Matches[1])"); $pendingId = $null }
}
$versionsText = $contractVersions -join '; '
$iconSetVersion = ($contractVersions | Where-Object { $_ -like 'ICON-SET *' } | Select-Object -First 1) -replace '^ICON-SET ', ''
$vocabularyHash = Get-Sha256 $sourceVocabulary

$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repoRoot 'assets\glyphs'
$glyphProvenance = Join-Path $destination 'PROVENANCE.txt'
$vocabularyDir = Join-Path $repoRoot 'tests\Lite.Tests\TestData\icon-vocabulary'
$vendoredVocabulary = Join-Path $vocabularyDir 'vocabulary.jsonl'
$vocabularyProvenance = Join-Path $vocabularyDir 'PROVENANCE.txt'

if ($Check) {
    $drift = [System.Collections.Generic.List[string]]::new()

    $recorded = @{}
    if (Test-Path -LiteralPath $glyphProvenance) {
        foreach ($line in Get-Content -LiteralPath $glyphProvenance) {
            $parts = @($line -split '\s+' | Where-Object { $_ })
            if ($parts.Count -eq 2 -and $parts[0].EndsWith('.svg')) { $recorded[$parts[0]] = $parts[1] }
        }
        if (-not (Select-String -LiteralPath $glyphProvenance -SimpleMatch "Catalog versions: $versionsText" -Quiet)) {
            $drift.Add("assets/glyphs/PROVENANCE.txt does not record the catalog versions '$versionsText'")
        }
        if (-not (Select-String -LiteralPath $glyphProvenance -SimpleMatch "Vocabulary SHA-256: $vocabularyHash" -Quiet)) {
            $drift.Add('assets/glyphs/PROVENANCE.txt records a different vocabulary SHA-256 than the catalog')
        }
    } else {
        $drift.Add('assets/glyphs/PROVENANCE.txt is missing')
    }

    foreach ($id in $glyphIds) {
        $name = $id + '.svg'
        $target = Join-Path $destination $name
        if (-not (Test-Path -LiteralPath $target)) { $drift.Add("$name is declared but not vendored"); continue }
        $vendored = Get-Sha256 $target
        if ($vendored -ne (Get-Sha256 (Join-Path $sourceGlyphs $name))) { $drift.Add("$name differs from the catalog") }
        if (-not $recorded.ContainsKey($name)) { $drift.Add("$name has no line in PROVENANCE.txt") }
        elseif ($recorded[$name] -ne $vendored) { $drift.Add("$name does not match its PROVENANCE.txt line") }
    }
    if (Test-Path -LiteralPath $destination) {
        foreach ($file in Get-ChildItem -LiteralPath $destination -Filter '*.svg') {
            if ($glyphIds -notcontains $file.BaseName) { $drift.Add("$($file.Name) is vendored but not declared in this script") }
        }
    }

    if (-not (Test-Path -LiteralPath $vendoredVocabulary)) {
        $drift.Add('the test vocabulary copy is missing')
    } elseif ((Get-Sha256 $vendoredVocabulary) -ne $vocabularyHash) {
        $drift.Add('the test vocabulary copy differs from the catalog')
    }
    if (-not (Test-Path -LiteralPath $vocabularyProvenance) -or
        -not (Select-String -LiteralPath $vocabularyProvenance -SimpleMatch "Source SHA-256: $vocabularyHash" -Quiet)) {
        $drift.Add('the test vocabulary PROVENANCE.txt does not record the catalog SHA-256')
    }

    if ($drift.Count -gt 0) {
        Stop-Verdict 1 "FAIL ($($drift.Count) drift, catalog $versionsText)" $drift
    }
    Stop-Verdict 0 "PASS ($($glyphIds.Count) glyphs and the vocabulary match the catalog, $versionsText)"
}

New-Item -ItemType Directory -Force -Path $destination | Out-Null
New-Item -ItemType Directory -Force -Path $vocabularyDir | Out-Null

# A vendored glyph the list no longer declares would otherwise linger with no provenance line.
foreach ($file in Get-ChildItem -LiteralPath $destination -Filter '*.svg') {
    if ($glyphIds -notcontains $file.BaseName) { Remove-Item -LiteralPath $file.FullName }
}

$provenance = [System.Collections.Generic.List[string]]::new()
$provenance.Add('ICONOGRAPHY VENDORED ARTIFACT')
$provenance.Add('Do not hand-edit SVG files in this folder. Regenerate with tools/Sync-IconGlyphs.ps1.')
$provenance.Add("Catalog versions: $versionsText")
$provenance.Add("Vocabulary SHA-256: $vocabularyHash")
$provenance.Add('')

foreach ($id in $glyphIds) {
    $target = Join-Path $destination ($id + '.svg')
    Copy-Item -LiteralPath (Join-Path $sourceGlyphs ($id + '.svg')) -Destination $target -Force
    $provenance.Add("$id.svg  $(Get-Sha256 $target)")
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllLines($glyphProvenance, $provenance, $utf8)

Copy-Item -LiteralPath $sourceVocabulary -Destination $vendoredVocabulary -Force
# MSBuild uses PreserveNewest for this test fixture. Catalog timestamps can predate
# an older build output, so stamp the imported copy rather than retaining the source time.
(Get-Item -LiteralPath $vendoredVocabulary).LastWriteTimeUtc = [DateTime]::UtcNow
[System.IO.File]::WriteAllLines($vocabularyProvenance, [string[]]@(
    'ICONOGRAPHY VOCABULARY ARTIFACT',
    "Catalog version: $iconSetVersion",
    "Source SHA-256: $vocabularyHash",
    'Source: iconography/vocabulary.jsonl',
    "Taken: $(Get-Date -Format 'yyyy-MM-dd')"
), $utf8)

Stop-Verdict 0 "SYNCED ($($glyphIds.Count) glyphs and the vocabulary, $versionsText)"
