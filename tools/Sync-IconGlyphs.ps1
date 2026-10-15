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
          a declared glyph or the vocabulary, or its vocabulary.version carrier is malformed or names
          a MAJOR higher than the one this tool was built against (ICON-SET section 5).
#>
[CmdletBinding()]
param(
    [string]$CatalogRoot = '',
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

# An unexpected terminating error must not surface as exit 1, which reads as drift.
trap {
    Write-Host "  $($_.Exception.Message)"
    Write-Host 'Sync-IconGlyphs: COULD NOT VERIFY (the script failed)'
    exit 2
}

# This is deliberately a small, declared product subset.  The catalog remains the
# source of truth; adding a mapped meaning means adding it here as well.
$glyphIds = @(
    # Dialog answers and command verbs (WINDOWS-UI 0.4 section 11: a glyph on every command button).
    'action.confirm', 'action.cancel', 'nav.close', 'action.save', 'action.remove', 'action.reset', 'action.clear-all', 'action.clear-input',
    'action.import', 'action.export', 'action.swap', 'action.revert', 'action.test-connection', 'action.install',
    'action.reconnect', 'action.verify', 'action.show-code', 'action.paste', 'action.move', 'action.run', 'action.undo',
    'action.redo', 'action.adjust', 'action.filter', 'action.email', 'action.change-password', 'action.mount-disk',
    'action.unmount-disk', 'action.auto-mount', 'action.convert', 'action.upload', 'action.secure',
    'nav.back', 'nav.forward', 'nav.open-external', 'nav.exit', 'media.stop', 'media.cast', 'media.audio-track', 'app.help',
    'system.network-off', 'system.power',
    'action.add', 'action.choose-file', 'action.choose-folder', 'action.convert-to-video', 'action.copy', 'action.crop', 'action.delete', 'action.download', 'action.edit',
    'action.extract-text', 'action.refresh', 'action.rename', 'action.search', 'action.send-to',
    'action.share', 'action.translate',
    # The recipients table's and the chrome panels' pin button: the catalog's push-pin, filled while pinned and
    # its outlined state variant while free (a line-drawn tilted pin read as a second pencil next to action.edit).
    'action.pin', 'action.pin--off',
    'app.info', 'app.settings', 'content.apps', 'content.audio', 'content.disk-container', 'content.folder', 'content.image',
    'content.video', 'device.phone', 'view.screen',
    'media.exit-fullscreen', 'media.fullscreen', 'media.mute', 'media.next', 'media.pause',
    'media.play', 'media.previous', 'media.random', 'media.slideshow', 'media.volume',
    'nav.collapse', 'nav.expand', 'nav.more', 'nav.scroll-top', 'source.recent', 'source.resource',
    'status.error', 'status.locked', 'status.ok', 'status.unknown', 'status.warning',
    'system.internet', 'system.wifi', 'tool.brush', 'tool.oval', 'tool.rectangle', 'view.grid'
)

# ICON-SET section 15 (0.30): a meaning the catalog has no record for is drawn by the product, shipped under the
# id it proposes, and proposed in the same change. These files are NOT compared with the catalog - they are the
# product's own drawings, held by a provenance line - and the script fails the day the catalog holds the id, so the
# product converges on the catalog's drawing (section 15, "What the owner does").
$productGlyphs = @(
    'action.replace'   # PROPOSAL-2026-10-11-fms-action-replace.md - overwrite an existing item with the incoming one
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

# BUILD-EVIDENCE rule 1: say what this run judges before it judges anything.
Write-Subject $subject "$(if ($Check) { '-Check: the vendored glyphs and the test vocabulary against' } else { 'import: the glyphs and the vocabulary from' }) the catalog at '$(if ($CatalogRoot) { $CatalogRoot } else { 'no root given' })'"

if (-not $CatalogRoot) {
    Stop-Verdict 2 'COULD NOT VERIFY' @('no catalog root: pass -CatalogRoot or set FMS_CONTRACTS_ROOT (CLAUDE.md names the location)')
}

$iconography = Join-Path $CatalogRoot 'iconography'
$sourceGlyphs = Join-Path $iconography 'glyphs'
$sourceVocabulary = Join-Path $iconography 'vocabulary.jsonl'
$sourceVersion = Join-Path $iconography 'vocabulary.version'
$sourceReadme = Join-Path $iconography 'README.md'
foreach ($required in @($sourceGlyphs, $sourceVocabulary, $sourceVersion, $sourceReadme)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Stop-Verdict 2 'COULD NOT VERIFY' @("the iconography catalog is unreachable: '$required' was not found")
    }
}

# ICON-SET section 5: the carrier is one line, "ICON-SET <MAJOR>.<MINOR>". A build-time reader refuses a
# MAJOR higher than the one it was built against (a MAJOR re-learns meanings: a glyph changing shape, a
# meaning split or merged, a canonical name replaced) and accepts a higher MINOR. This tool and the code
# that reads what it vendors were last read against MAJOR 0.
$builtAgainstMajor = 0
$carrier = (Get-Content -LiteralPath $sourceVersion -TotalCount 1).Trim()
if ($carrier -notmatch '^ICON-SET (\d+)\.(\d+)$') {
    Stop-Verdict 2 'COULD NOT VERIFY' @("vocabulary.version holds '$carrier', not 'ICON-SET <MAJOR>.<MINOR>' (ICON-SET section 5)")
}
if ([int]$Matches[1] -gt $builtAgainstMajor) {
    Stop-Verdict 2 'COULD NOT VERIFY' @("the catalog is at $carrier, a higher MAJOR than this tool was built against (ICON-SET $builtAgainstMajor.x): a MAJOR amendment re-learns meanings, so read the amendment and the code that uses the vendored glyphs and vocabulary against it, then raise `$builtAgainstMajor here (ICON-SET section 5)")
}
$nowInCatalog = @($productGlyphs | Where-Object { Test-Path -LiteralPath (Join-Path $sourceGlyphs ($_ + '.svg')) })
if ($nowInCatalog.Count -gt 0) {
    Stop-Verdict 1 'FAIL (a product-drawn glyph is now in the catalog)' @($nowInCatalog | ForEach-Object { "'$_' is in the catalog now: move it from \ to \ and take the catalog drawing (ICON-SET section 15)" })
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

    foreach ($id in $productGlyphs) {
        $name = $id + '.svg'
        $target = Join-Path $destination $name
        if (-not (Test-Path -LiteralPath $target)) { $drift.Add("$name is declared as product-drawn but the file is missing"); continue }
        if (-not $recorded.ContainsKey($name)) { $drift.Add("$name has no line in PROVENANCE.txt") }
        elseif ($recorded[$name] -ne (Get-Sha256 $target)) { $drift.Add("$name does not match its PROVENANCE.txt line") }
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
            if (($glyphIds -notcontains $file.BaseName) -and ($productGlyphs -notcontains $file.BaseName)) { $drift.Add("$($file.Name) is vendored but not declared in this script") }
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
    if (($glyphIds -notcontains $file.BaseName) -and ($productGlyphs -notcontains $file.BaseName)) { Remove-Item -LiteralPath $file.FullName }
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
    # Catalog timestamps can predate the last build output: stamp the imported copy so an incremental build of
    # the embedded-resource projects (the Share Manager and the viewer) notices the file (a glyph whose catalog
    # file is older than the last build was silently left out of the assembly).
    (Get-Item -LiteralPath $target).LastWriteTimeUtc = [DateTime]::UtcNow
    $provenance.Add("$id.svg  $(Get-Sha256 $target)")
}

$provenance.Add('')
$provenance.Add('Product-drawn (not from the catalog; compared with the catalog only to notice that it now holds the id):')
foreach ($id in $productGlyphs) {
    $target = Join-Path $destination ($id + '.svg')
    if (-not (Test-Path -LiteralPath $target)) { Stop-Verdict 2 'COULD NOT IMPORT' @("the product-drawn glyph '$id' has no file in assets/glyphs") }
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
