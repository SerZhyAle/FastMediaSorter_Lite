<#
.SYNOPSIS
    Hold the site's claims to one positioning source and to facts typed once, offline (tickets 155 and 249,
    SITE-REPRESENTATION rules 1 to 4).

.DESCRIPTION
    docs/guides/POSITIONING.md is the one positioning source; tools/site-facts.json is its machine form
    (the family sentence, the three components in order and the state of each, the two install profiles, the
    public editions, the component x channel availability matrix with the files that prove each cell, the words
    that mark a component in development, the language count and the platform floor, and the surfaces that must
    follow them). Judged over the current files on disk, no network:

      1  source    the data file parses; the components are unique, each shipped or in-development, none of
                   them FileDO (a capability of sharing); POSITIONING.md quotes the family sentence, lists the
                   components in the data file's order, names every capability and edition and says the viewer
                   is no longer the core of every package; PRODUCT_NAMING.md owns every edition name and the
                   in-development component's name; the family sentence carries a development marker while a
                   component is in development; the landing copy's English family sentence is the data file's
                   (rule 1);
      2  surfaces  every declared surface (landing and its locale pages, the server page, the READMEs, the
                   Store description, the winget description) has its region, names the first N components and
                   names them in the source's order (rule 2);
      3  editions  a name of the form "Fast Media Sorter & Sharing: <X>" on any scanned file is a declared
                   public edition; the in-development component is named wherever the data file lets it be,
                   always within reach of a development marker, never inside a link to a download, an installer,
                   a release, a store or a winget command, never with a date (rule 4 as the owner's decision of
                   2026-10-20 replaced the Q155-2 reservation); the Microsoft Store listing says what the Store
                   package is and is not;
      4  profiles  the two install profiles each have their entry page, their channels and the cross-link to
                   the other profile's page, and the availability matrix is complete: every component x channel
                   cell holds a known state, every cell cites evidence, and every evidence pattern still matches
                   its packaging file - so a packaging change that contradicts the matrix fails here until the
                   data file and the pages say the new truth; the matrix labels exist in en, ru and uk;
      5  facts     the language count and the platform floor are read from their real sources (the
                   Localization table, the site copy's locale keys, the Inno scripts, the Server winget
                   manifest, the MSIX manifest) and compared with the data file; every "N languages" and every
                   "Windows 10 <version>" in the scanned files is a value the data file holds (rule 3);
      6  discover  the discoverability block (ticket 207 T-1, canon PROMOTION section 2) - the tagline,
                   the GitHub description, topics, winget tags, Store search terms, page titles and the
                   social card's words - holds its channels' limits and none of the words the naming
                   standard retired; no published surface, topic, tag or search term names a competitor
                   (the names are read from the research guide's gate-terms block, never from the data
                   file); and each channel the block lists as `applied` matches the tree.

    Ends in the shared one-line verdict (CHECK-VERDICT): exit 0 PASS / 1 FAIL / 2 COULD NOT VERIFY.

.PARAMETER Root
    The tree to judge (default: this repository). The fixture self-test points it at a tiny tree.

.PARAMETER Facts
    The data file (default: tools/site-facts.json under -Root).
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Facts = ''
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'Test-SitePositioning' 2 'the check could not complete' }
$Root = [IO.Path]::GetFullPath($Root)
if (-not $Facts) { $Facts = Join-Path $Root 'tools/site-facts.json' }
Write-Subject 'Test-SitePositioning' "the positioning source, the declared surfaces, the editions, the install profiles, the availability matrix and the typed facts of the site; $(Get-TreeLabel $Root)"

if (-not (Test-Path -LiteralPath $Facts)) { Exit-Verdict 'Test-SitePositioning' 2 "missing input: $Facts" }

$problems = [Collections.Generic.List[string]]::new()
function Problem([string]$message) { $problems.Add($message) }

function Read-Utf8([string]$path) { [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false)) }
function Get-LineAt([string]$text, [int]$index) { 1 + ([regex]::Matches($text.Substring(0, [Math]::Min($index, $text.Length)), "`n")).Count }
function Rel([string]$path) { [IO.Path]::GetRelativePath($Root, $path).Replace('\', '/') }
function Norm([string]$s) { ($s -replace '&amp;', '&' -replace '(?m)^\s*>\s?', '' -replace '\s+', ' ').Trim() }

$fileCache = @{}
function Get-Text([string]$rel) {
    if (-not $fileCache.ContainsKey($rel)) {
        $full = Join-Path $Root $rel
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $null }
        $fileCache[$rel] = Read-Utf8 $full
    }
    $fileCache[$rel]
}
function Require-Text([string]$rel) {
    $t = Get-Text $rel
    if ($null -eq $t) { Exit-Verdict 'Test-SitePositioning' 2 "missing input: $rel" }
    $t
}

function Expand-Patterns([string[]]$patterns) {
    $seen = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($p in $patterns) {
        $hits = @(Get-ChildItem -Path (Join-Path $Root $p) -File -ErrorAction SilentlyContinue)
        foreach ($h in $hits) { [void]$seen.Add((Rel $h.FullName)) }
    }
    @($seen)
}

$decl = Read-Utf8 $Facts | ConvertFrom-Json
foreach ($k in 'positioning', 'profiles', 'editions', 'availability', 'development', 'facts', 'surfaces') {
    if (-not $decl.PSObject.Properties[$k]) { Exit-Verdict 'Test-SitePositioning' 2 "$Facts has no '$k'" }
}

# --- 1: the source ---------------------------------------------------------------------------------
$components = @($decl.positioning.components)
$ids = @($components | ForEach-Object { [string]$_.id })
if (-not $ids.Count) { Problem 'site-facts.json lists no component' }
if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Problem 'site-facts.json repeats a component id' }
$capabilityIds = [Collections.Generic.List[string]]::new()
foreach ($c in $components) {
    if (-not [string]$c.title) { Problem "component '$($c.id)' has no title" }
    if (@('shipped', 'in-development') -notcontains [string]$c.state) { Problem "component '$($c.id)' has the state '$($c.state)', not shipped or in-development" }
    if ([string]$c.title -match 'FileDO') { Problem "component '$($c.id)' is titled '$($c.title)': FileDO hosting is a capability of sharing, not a component of its own (specification 249)" }
    if ($c.PSObject.Properties['capabilities']) { foreach ($cap in @($c.capabilities)) { $capabilityIds.Add([string]$cap.id) } }
}
$lead = [string]$decl.positioning.lead
if (-not $lead) { Problem 'site-facts.json has no family sentence (positioning.lead)' }
if ($lead -match 'FileDO') { Problem 'the family sentence names FileDO: hosting FileDO disks is a capability of sharing, not a part of its own (specification 249)' }
$inDevelopment = @($components | Where-Object { [string]$_.state -eq 'in-development' })

$sourceRel = [string]$decl.positioning.source
$sourceText = Require-Text $sourceRel
$sourceNorm = Norm $sourceText
if ($lead -and -not $sourceNorm.Contains((Norm $lead))) { Problem "$sourceRel does not quote the family sentence of site-facts.json" }
$cursor = -1
foreach ($c in $components) {
    $at = $sourceText.IndexOf('**' + [string]$c.title + '**', [StringComparison]::Ordinal)
    if ($at -lt 0) { Problem "$sourceRel does not list the component '$($c.title)' as **$($c.title)**" }
    elseif ($at -lt $cursor) { Problem "$sourceRel lists '$($c.title)' before the component that precedes it in site-facts.json" }
    else { $cursor = $at }
    if ($c.PSObject.Properties['capabilities']) {
        foreach ($cap in @($c.capabilities)) {
            $word = ([string]$cap.title -split '\s+')[0]
            if (-not $sourceText.Contains($word)) { Problem "$sourceRel does not name the capability '$($cap.title)' of '$($c.title)'" }
        }
    }
}
if ($capabilityIds.Count -and $sourceText -notmatch '(?i)capability of') { Problem "$sourceRel never calls the capabilities of a component a 'capability of' it (specification 249: FileDO hosting is a capability of sharing)" }
$naming = Require-Text ([string]$decl.facts.sources.naming)
foreach ($e in @($decl.editions)) {
    $n = [string]$e.name
    if (-not $sourceText.Contains('`' + $n + '`')) { Problem "$sourceRel does not name the edition '$n'" }
    if (-not $naming.Contains('`' + $n + '`')) { Problem "$($decl.facts.sources.naming) does not own the edition name '$n'" }
}
foreach ($c in $inDevelopment) {
    if (-not $naming.Contains([string]$c.title)) { Problem "$($decl.facts.sources.naming) does not own the name '$($c.title)' of the component in development" }
}
$devMarkers = @($decl.development.markers | ForEach-Object { [string]$_ })
$markerRx = [regex]::new('(?i)(?:' + (($devMarkers | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')')
if ($inDevelopment.Count -and $lead -and -not $markerRx.IsMatch($lead)) { Problem "the family sentence names a component in development but carries none of the development markers (specification 249)" }
$siteCopyRel = [string]$decl.facts.sources.siteCopy
$siteCopy = Require-Text $siteCopyRel | ConvertFrom-Json
if ($siteCopy.en.family -ne $lead) { Problem "$siteCopyRel en.family is not the family sentence of site-facts.json" }

# --- 2: the components on the declared surfaces ----------------------------------------------------------
$regions = 0
$surfaceFiles = 0
foreach ($s in @($decl.surfaces)) {
    $files = if ($s.PSObject.Properties['files']) { @($s.files) } else { @($s.file) }
    $anchors = $s.anchors
    foreach ($k in $anchors.PSObject.Properties.Name) { if ($ids -notcontains $k) { Problem "surface $($files -join ', ') anchors the unknown component '$k'" } }
    $owed = @($ids | Select-Object -First ([int]$s.names))
    foreach ($file in $files) {
        $text = Get-Text $file
        if ($null -eq $text) { Problem "surface $file is not a file"; continue }
        $surfaceFiles++
        foreach ($scope in @($s.scopes)) {
            $m = [regex]::Match($text, [string]$scope)
            if (-not $m.Success) { Problem "${file}: the region /$scope/ ($($s.why)) is not found"; continue }
            $region = if ($m.Groups['region'].Success) { $m.Groups['region'].Value } else { $m.Value }
            $regions++
            $at = "${file}:$(Get-LineAt $text $m.Index)"
            $span = @{}
            foreach ($id in $ids) {
                if ($anchors.PSObject.Properties.Name -notcontains $id) { continue }
                $hits = [regex]::Matches($region, [string]$anchors.$id)
                if ($hits.Count) { $span[$id] = @($hits[0].Index, ($hits[$hits.Count - 1].Index)) }
            }
            foreach ($id in $owed) {
                if (-not $span.ContainsKey($id)) { Problem "$at ($($s.why)) does not name the component '$id', which the positioning source puts in its first $($s.names)" }
            }
            $prev = $null
            foreach ($id in $ids) {
                if (-not $span.ContainsKey($id)) { continue }
                if ($prev -and $span[$id][0] -lt $span[$prev][1]) { Problem "$at ($($s.why)) names '$id' before it is done with '$prev' - the source's order is $($ids -join ', ')" }
                $prev = $id
            }
        }
    }
}

# --- 3: the editions, the component in development, the Store listing ------------------------------------
$suffixes = @($decl.editions | Where-Object { $_.PSObject.Properties['nameSuffix'] } | ForEach-Object { [string]$_.nameSuffix })
$dev = $decl.development
$editionScan = New-Object Collections.Generic.SortedSet[string] ([StringComparer]::Ordinal)
foreach ($f in (Expand-Patterns @($dev.scan))) { [void]$editionScan.Add($f) }
foreach ($f in (Expand-Patterns @($decl.facts.scan))) { [void]$editionScan.Add($f) }
$editionHits = 0
foreach ($file in $editionScan) {
    $text = Get-Text $file
    foreach ($m in [regex]::Matches($text, 'Fast Media Sorter (?:&amp;|&) Sharing:\s+(?<s>\S+(?:\s+\S+){0,2})')) {
        $editionHits++
        $found = $m.Groups['s'].Value
        $ok = $false
        foreach ($suf in $suffixes) { if ($found.StartsWith($suf, [StringComparison]::Ordinal)) { $ok = $true } }
        if (-not $ok) { Problem "${file}:$(Get-LineAt $text $m.Index) names an edition 'Fast Media Sorter & Sharing: $found' that site-facts.json does not declare as public" }
    }
}

# the phrases of the retired model, on every published surface
$bannedPhrases = @($decl.positioning.bannedPhrases | ForEach-Object { [string]$_ })
foreach ($file in (Expand-Patterns @($dev.scan))) {
    $text = Get-Text $file
    foreach ($b in $bannedPhrases) {
        $i = $text.IndexOf($b, [StringComparison]::OrdinalIgnoreCase)
        if ($i -ge 0) { Problem "${file}:$(Get-LineAt $text $i) says '$b', a phrase of the model specification 249 retired" }
    }
}

# the component in development: named, marked, never sold
$mentionSites = 0
$devComponent = $components | Where-Object { [string]$_.id -eq [string]$dev.component } | Select-Object -First 1
if ($null -eq $devComponent) { Problem "development.component '$($dev.component)' is not a component of site-facts.json" }
elseif ([string]$devComponent.state -ne 'in-development') { Problem "the development block governs '$($dev.component)', whose state is '$($devComponent.state)' - drop the block when the component ships, and move its pages to the shipped wording" }
else {
    $window = [int]$dev.window
    $mentionRx = [regex]::new([string]$dev.mentionPattern)
    $linkRx = [regex]::new([string]$dev.downloadLink)
    $sellRx = [regex]::new('(?i)(?<!\bnot\s)(?:\b(?:download|install|get)\s+(?:the\s+)?|\b(?:скача\w+|загрузит\w+|установит\w+|завантаж\w+|встанови\w*)\s+)Media Exchange Server')
    foreach ($file in (Expand-Patterns @($dev.scan))) {
        $text = Get-Text $file
        foreach ($m in $mentionRx.Matches($text)) {
            $mentionSites++
            $at = "${file}:$(Get-LineAt $text $m.Index)"
            $lo = [Math]::Max(0, $m.Index - $window)
            $hi = [Math]::Min($text.Length, $m.Index + $m.Length + $window)
            if (-not $markerRx.IsMatch($text.Substring($lo, $hi - $lo))) { Problem "$at names '$($m.Value)' with no development marker within $window characters: a component in development is always marked as such" }
            $dlo = [Math]::Max(0, $m.Index - 60)
            $dhi = [Math]::Min($text.Length, $m.Index + $m.Length + 60)
            if ($text.Substring($dlo, $dhi - $dlo) -match '\b20\d\d\b') { Problem "$at gives a date next to '$($m.Value)': a component in development carries no date" }
            # inside an anchor whose address is a download
            $open = $text.LastIndexOf('<a ', $m.Index, [StringComparison]::OrdinalIgnoreCase)
            if ($open -ge 0) {
                $close = $text.IndexOf('</a>', $open, [StringComparison]::OrdinalIgnoreCase)
                $tagEnd = $text.IndexOf('>', $open)
                if ($close -gt $m.Index -and $tagEnd -gt $open -and $linkRx.IsMatch($text.Substring($open, $tagEnd - $open))) { Problem "$at names '$($m.Value)' inside a link to a download, an installer, a release, a store or the server page: there is nothing to download" }
            }
        }
        foreach ($m in [regex]::Matches($text, '\[[^\]\r\n]*Media Exchange Server[^\]\r\n]*\]\((?<u>[^)\r\n]*)\)')) {
            if ($linkRx.IsMatch($m.Groups['u'].Value)) { Problem "${file}:$(Get-LineAt $text $m.Index) links '$($m.Value)' to a download, an installer, a release, a store or the server page: there is nothing to download" }
        }
        foreach ($m in [regex]::Matches($text, '(?i)winget(?:\s|%20)install\s+\S*xchange|releases/download/\S*xchange')) { Problem "${file}:$(Get-LineAt $text $m.Index) has an install command or a release asset for the component in development: '$($m.Value)'" }
        foreach ($m in $sellRx.Matches($text)) { Problem "${file}:$(Get-LineAt $text $m.Index) tells the reader to '$($m.Value)': there is nothing to obtain yet" }
    }
    # a door or card of the site copy for the component in development has no call to action
    foreach ($prop in $siteCopy.PSObject.Properties) {
        if ($prop.Value -isnot [pscustomobject] -or -not $prop.Value.PSObject.Properties['doors']) { continue }
        foreach ($d in @($prop.Value.doors)) {
            $row = @($d)
            if ($row.Count -ge 4 -and $mentionRx.IsMatch([string]$row[1]) -and [string]$row[3]) { Problem "$siteCopyRel $($prop.Name).doors: the door of the component in development carries a call to action ('$($row[3])')" }
        }
    }
}

# the Microsoft Store listing says what the Store package is and is not
$sl = $dev.storeListing
if ($null -ne $sl) {
    $listing = Get-Text ([string]$sl.file)
    if ($null -eq $listing) { Problem "development.storeListing names $($sl.file), which is not a file" }
    else {
        foreach ($p in @($sl.mustSay)) { if ($listing -notmatch [string]$p) { Problem "$($sl.file) does not say /$p/: $($sl.why)" } }
        foreach ($p in @($sl.mustNotSay)) { foreach ($m in [regex]::Matches($listing, [string]$p)) { Problem "$($sl.file):$(Get-LineAt $listing $m.Index) says '$($m.Value)': $($sl.why)" } }
        foreach ($m in [regex]::Matches($listing, [string]$sl.serviceMention)) {
            $after = $listing.Substring($m.Index, [Math]::Min([int]$sl.window, $listing.Length - $m.Index))
            if ($after -notmatch [string]$sl.qualifier) { Problem "$($sl.file):$(Get-LineAt $listing $m.Index) mentions '$($m.Value)' without saying within $($sl.window) characters that it is not part of the Store version: $($sl.why)" }
        }
    }
}

# --- 4: the install profiles and the availability matrix --------------------------------------------------
$av = $decl.availability
$states = @($av.states.PSObject.Properties.Name)
$avChannels = @($av.channels | ForEach-Object { [string]$_ })
$avComponents = @($av.components | ForEach-Object { [string]$_ })
foreach ($id in ($ids + @($capabilityIds))) { if ($avComponents -notcontains $id) { Problem "availability.components does not list '$id', a component or capability of the positioning" } }
$evidenceChecked = 0
foreach ($prop in $av.evidence.PSObject.Properties) {
    $e = $prop.Value
    $t = Get-Text ([string]$e.file)
    if ($null -eq $t) { Problem "availability evidence '$($prop.Name)' names $($e.file), which is not a file"; continue }
    $evidenceChecked++
    if (-not [regex]::IsMatch($t, [string]$e.pattern)) { Problem "availability evidence '$($prop.Name)' no longer matches $($e.file) - $($e.what). A packaging change contradicts the matrix: correct the packaging, or the matrix, the pages and POSITIONING.md section 4.1 together" }
}
$cellCount = 0
$cellState = @{}
foreach ($comp in $avComponents) {
    $row = $av.cells.PSObject.Properties[$comp]
    if (-not $row) { Problem "availability.cells has no row for '$comp'"; continue }
    foreach ($ch in $avChannels) {
        $cp = $row.Value.PSObject.Properties[$ch]
        if (-not $cp) { Problem "availability.cells.$comp has no cell for the channel '$ch'"; continue }
        $cell = @($cp.Value | ForEach-Object { [string]$_ })
        $cellCount++
        $st = $cell[0]
        $cellState["$comp/$ch"] = $st
        if ($states -notcontains $st) { Problem "availability.cells.$comp.$ch has the unknown state '$st'" }
        if ($cell.Count -lt 2) { Problem "availability.cells.$comp.$ch cites no evidence: a cell is a claim about the packaging and names the files that prove it" }
        foreach ($ev in @($cell | Select-Object -Skip 1)) { if (-not $av.evidence.PSObject.Properties[$ev]) { Problem "availability.cells.$comp.$ch cites the unknown evidence '$ev'" } }
    }
}
foreach ($prop in $av.facts.PSObject.Properties) {
    foreach ($fp in $prop.Value.PSObject.Properties) { foreach ($ev in @($fp.Value)) { if (-not $av.evidence.PSObject.Properties[[string]$ev]) { Problem "availability.facts.$($prop.Name).$($fp.Name) cites the unknown evidence '$ev'" } } }
}
foreach ($c in $components) {
    foreach ($ch in $avChannels) {
        $st = $cellState["$($c.id)/$ch"]
        if ($null -eq $st) { continue }
        if ([string]$c.state -eq 'in-development' -and @('in-development', 'absent') -notcontains $st) { Problem "the component '$($c.id)' is in development and the matrix says '$st' for ${ch}: a component in development is in no package" }
        if ([string]$c.state -eq 'shipped' -and $st -eq 'in-development') { Problem "the component '$($c.id)' is shipped and the matrix says 'in-development' for $ch" }
    }
    if ($c.PSObject.Properties['capabilities']) {
        foreach ($cap in @($c.capabilities)) {
            foreach ($ch in $avChannels) {
                if ($cellState["$($c.id)/$ch"] -eq 'absent' -and $cellState["$($cap.id)/$ch"] -ne 'absent') { Problem "$ch has no '$($c.id)' and still lists its capability '$($cap.id)' as $($cellState["$($cap.id)/$ch"]): a capability of sharing is not in a package without sharing" }
            }
        }
    }
}

$profiles = @($decl.profiles)
$profileIds = @($profiles | ForEach-Object { [string]$_.id })
foreach ($p in $profiles) {
    if ((Get-Text ([string]$p.entry)) -eq $null) { Problem "profile '$($p.id)' has the entry page $($p.entry), which is not a file" }
    if ($ids -notcontains [string]$p.leadsWith) { Problem "profile '$($p.id)' leads with '$($p.leadsWith)', which is not a component" }
    if ($profileIds -notcontains [string]$p.alternative) { Problem "profile '$($p.id)' names the alternative '$($p.alternative)', which is not a profile" }
    foreach ($ch in @($p.channels)) {
        if ($avChannels -notcontains [string]$ch) { Problem "profile '$($p.id)' lists the channel '$ch', which the availability matrix does not have"; continue }
        foreach ($k in @($p.installs)) {
            $st = $cellState["$k/$ch"]
            if (@('core', 'required') -notcontains $st) { Problem "profile '$($p.id)' installs '$k' through $ch and the matrix says '$st' (core or required expected)" }
        }
        foreach ($k in @($p.optional)) {
            $st = $cellState["$k/$ch"]
            if (@('optional-on', 'optional-off', 'bundled') -notcontains $st) { Problem "profile '$($p.id)' offers '$k' as optional through $ch and the matrix says '$st'" }
        }
    }
    foreach ($x in @($p.crossLinks)) {
        $ft = Get-Text ([string]$x.file)
        if ($null -eq $ft) { Problem "profile '$($p.id)' declares a cross-link in $($x.file), which is not a file"; continue }
        if (-not [regex]::IsMatch($ft, [string]$x.pattern)) { Problem "$($x.file) lost its cross-link ($($x.what)): the two install profiles each point at the other" }
    }
}
if ($profiles.Count -ne 2) { Problem "site-facts.json declares $($profiles.Count) install profiles; specification 249 has two (viewer first, server first)" }

# the matrix labels, in the three languages the hand-authored pages carry
$matrixLabels = 0
$matrix = if ($siteCopy.PSObject.Properties['_matrix']) { $siteCopy._matrix } else { $null }
if ($null -eq $matrix) { Problem "$siteCopyRel has no _matrix: the availability matrix is rendered from it" }
else {
    function Get-LeafKeys($node, [string]$prefix) {
        if ($node -is [pscustomobject]) { foreach ($p in $node.PSObject.Properties) { Get-LeafKeys $p.Value (($prefix + '.' + $p.Name).TrimStart('.')) } }
        else { $prefix }
    }
    $enKeys = @(Get-LeafKeys $matrix.en '' | Sort-Object)
    foreach ($lang in 'ru', 'uk') {
        $other = @(Get-LeafKeys $matrix.$lang '' | Sort-Object)
        $missing = @($enKeys | Where-Object { $other -notcontains $_ })
        $extra = @($other | Where-Object { $enKeys -notcontains $_ })
        if ($missing.Count -or $extra.Count) { Problem "$siteCopyRel _matrix.$lang is out of step with _matrix.en (missing: $($missing -join ', '); extra: $($extra -join ', '))" }
    }
    foreach ($lang in 'en', 'ru', 'uk') {
        $L = $matrix.$lang
        foreach ($id in $avComponents) { if (-not [string]$L.components.$id) { Problem "$siteCopyRel _matrix.$lang.components has no label for '$id'" } }
        foreach ($id in $avChannels) { if (-not [string]$L.channels.$id) { Problem "$siteCopyRel _matrix.$lang.channels has no label for '$id'" } }
        foreach ($id in $states) { if (-not [string]$L.states.$id) { Problem "$siteCopyRel _matrix.$lang.states has no label for '$id'" } }
        $matrixLabels += $avComponents.Count + $avChannels.Count + $states.Count
    }
}


# --- 5: the typed facts --------------------------------------------------------------------------------
$src = $decl.facts.sources
$codes = @($decl.facts.languages.codes | ForEach-Object { [string]$_ })
$count = [int]$decl.facts.languages.count
$fallbackCount = [int]$decl.facts.languages.fallbackCount
if ($codes.Count -ne $count) { Problem "site-facts.json says $count languages and lists $($codes.Count) codes" }

# the sources of the language count
$loc = Require-Text ([string]$src.localization)
$modern = [regex]::Match($loc, '(?s)#Else\s+Public Shared ReadOnly Codes As String\(\) = \{(?<c>[^}]*)\}')
$legacy = [regex]::Match($loc, '(?s)#If NETFRAMEWORK Then\s+Public Shared ReadOnly Codes As String\(\) = \{(?<c>[^}]*)\}')
if (-not $modern.Success) { Problem "$($src.localization): the modern Codes array is not found" }
else {
    $real = @([regex]::Matches($modern.Groups['c'].Value, '"(\w+)"') | ForEach-Object { $_.Groups[1].Value })
    if (($real -join ',') -ne ($codes -join ',')) { Problem "$($src.localization) ships the languages [$($real -join ' ')] and site-facts.json says [$($codes -join ' ')]" }
}
if (-not $legacy.Success) { Problem "$($src.localization): the NETFRAMEWORK Codes array is not found" }
else {
    $n = @([regex]::Matches($legacy.Groups['c'].Value, '"(\w+)"')).Count
    if ($n -ne $fallbackCount) { Problem "$($src.localization): the x86 fallback ships $n languages and site-facts.json says $fallbackCount" }
}
$copyLocales = @($siteCopy.PSObject.Properties | Where-Object { $_.Value -is [pscustomobject] -and $_.Value.PSObject.Properties['dir'] } | ForEach-Object { $_.Name })
if ((($copyLocales | Sort-Object) -join ',') -ne (($codes | Sort-Object) -join ',')) { Problem "$siteCopyRel carries the locales [$($copyLocales -join ' ')] and the language list is [$($codes -join ' ')]" }

# the sources of the platform floor
$program = $decl.facts.platform.program
$store = $decl.facts.platform.store
$releases = $decl.facts.windows10Releases
$progBuild = [int]$program.build
$storeBuild = [int]$store.build
if ([int]$releases.($program.version) -ne $progBuild) { Problem "site-facts.json pairs Windows 10 $($program.version) with build $progBuild, its own table says $($releases.($program.version))" }
if ([int]$releases.($store.version) -ne $storeBuild) { Problem "site-facts.json pairs Windows 10 $($store.version) with build $storeBuild, its own table says $($releases.($store.version))" }
function Compare-Builds([string]$rel, [string]$pattern, [int]$want, [string]$what) {
    $t = Require-Text $rel
    $ms = [regex]::Matches($t, $pattern)
    if (-not $ms.Count) { Problem "${rel}: no $what found to compare with the platform floor"; return }
    foreach ($m in $ms) {
        $got = [int]$m.Groups['b'].Value
        if ($got -ne $want) { Problem "${rel}:$(Get-LineAt $t $m.Index) says $what $got and site-facts.json says $want" }
    }
}
Compare-Builds ([string]$src.installer) 'Version\.Build\s*>=\s*(?<b>\d+)' $progBuild 'the UseModernExe build floor'
Compare-Builds ([string]$src.installer) 'MinVersion:\s*10\.0\.(?<b>\d+)' $progBuild 'a component MinVersion build'
Compare-Builds ([string]$src.serverInstaller) '(?m)^MinVersion=10\.0\.(?<b>\d+)' $progBuild 'the Server installer MinVersion build'
Compare-Builds ([string]$src.serverWinget) '(?m)^MinimumOSVersion:\s*10\.0\.(?<b>\d+)' $progBuild 'the Server winget MinimumOSVersion build'
Compare-Builds ([string]$src.msix) 'TargetDeviceFamily[^>]*\sMinVersion="10\.0\.(?<b>\d+)\.\d+"' $storeBuild 'the MSIX TargetDeviceFamily build'

# the claims typed in the scanned files
$allowedCounts = @($count, $fallbackCount)
$translatedPages = [int]$decl.facts.languages.translatedPages
if ($translatedPages -ne $count - 1) { Problem "site-facts.json says $translatedPages translated pages and $count languages (the English root is the one page that is not translated)" }
$localePages = @(Expand-Patterns @('lang/*/index.html')).Count
if ($localePages -ne $translatedPages) { Problem "the tree holds $localePages lang/<code>/index.html pages and site-facts.json says $translatedPages" }
# "the other twelve languages" / "its twelve language pages": the number of translated pages, count - 1, counted
# from lang/. It is a claim about pages, so it is accepted only where the words around it say pages or other.
function Test-PagesClaim([string]$text, $m, [int]$n) {
    if ($n -ne $translatedPages) { return $false }
    $before = $text.Substring([Math]::Max(0, $m.Index - 10), [Math]::Min(10, $m.Index))
    $after = $text.Substring($m.Index + $m.Length, [Math]::Min(60, $text.Length - $m.Index - $m.Length))
    ($before -match '(?i)\bother\s+$') -or ($after -match '(?i)^\s*(?:\w+\s+)?(?:pages|страниц|сторін)')
}
$allowedVersions = @([string]$program.version, [string]$store.version)
$allowedBuilds = @($progBuild, $storeBuild)
$wordCounts = @{ 'eleven' = 11; 'twelve' = 12; 'thirteen' = 13; 'fourteen' = 14; 'fifteen' = 15; 'одиннадцат' = 11; 'двенадцат' = 12; 'тринадцат' = 13; 'четырнадцат' = 14; 'одинадцят' = 11; 'дванадцят' = 12; 'чотирнадцят' = 14 }
$langWord = '(?:UI\s+|interface\s+)?(?:languages?|язык\w*|мов\w*|Sprachen|lingue|idiomas|langues|لغة|भाषा\w*|ভাষা\w*|زبان\w*|种语言)'
$rxCount = [regex]::new("(?<![\d.])(?<n>\d{1,2})\s*(?:টি\s*)?$langWord", 'IgnoreCase')
$rxWord = [regex]::new("\b(?<w>eleven|twelve|thirteen|fourteen|fifteen|одиннадцат\w*|двенадцат\w*|тринадцат\w*|четырнадцат\w*|одинадцят\w*|дванадцят\w*|чотирнадцят\w*)\s+$langWord", 'IgnoreCase')
$rxFloor = [regex]::new('Windows 10(?:\s+(?:version|build|v\.?))?\s+(?<v>\d{4,5})\b')
$rxBuild = [regex]::new('(?i)(?:(?<![\d.])10\.0\.|\bbuild\s*(?:>=|≥|>)?\s*)(?<b>1\d{4})\b')
$factFiles = Expand-Patterns @($decl.facts.scan)
$claims = 0
foreach ($file in $factFiles) {
    $text = Get-Text $file
    foreach ($m in $rxCount.Matches($text)) {
        $claims++
        $n = [int]$m.Groups['n'].Value
        if ($allowedCounts -notcontains $n -and -not (Test-PagesClaim $text $m $n)) { Problem "${file}:$(Get-LineAt $text $m.Index) says '$($m.Value.Trim())' and the interface has $count languages (the x86 fallback $fallbackCount)" }
    }
    foreach ($m in $rxWord.Matches($text)) {
        $claims++
        $w = $m.Groups['w'].Value.ToLowerInvariant()
        $n = 0
        foreach ($k in $wordCounts.Keys) { if ($w.StartsWith($k)) { $n = $wordCounts[$k] } }
        if ($allowedCounts -notcontains $n -and -not (Test-PagesClaim $text $m $n)) { Problem "${file}:$(Get-LineAt $text $m.Index) says '$($m.Value.Trim())' and the interface has $count languages" }
    }
    foreach ($m in $rxFloor.Matches($text)) {
        $claims++
        $v = $m.Groups['v'].Value
        $ok = if ($v.Length -eq 4) { $allowedVersions -contains $v } else { $allowedBuilds -contains [int]$v }
        if (-not $ok) { Problem "${file}:$(Get-LineAt $text $m.Index) says '$($m.Value)' and the program's floor is Windows 10 $($program.version) (the Store package's $($store.version))" }
    }
    foreach ($m in $rxBuild.Matches($text)) {
        $claims++
        if ($allowedBuilds -notcontains [int]$m.Groups['b'].Value) { Problem "${file}:$(Get-LineAt $text $m.Index) says '$($m.Value)' and the floors are builds $($allowedBuilds -join ' and ')" }
    }
}
$landing = Get-Text 'index.html'
if ($null -ne $landing) {
    $os = [regex]::Match($landing, '"operatingSystem":\s*"(?<o>[^"]*)"')
    if ($os.Success -and $os.Groups['o'].Value -ne [string]$decl.facts.platform.schemaOperatingSystem) {
        Problem "index.html types operatingSystem '$($os.Groups['o'].Value)' and site-facts.json says '$($decl.facts.platform.schemaOperatingSystem)'"
    }
}

# --- 6: the discoverability fields (ticket 207 T-1; canon PROMOTION section 2) --------------------------------------
# The machine form of POSITIONING.md section 7: the tagline, the GitHub description, the topics, the winget tags, the
# Store search terms, the page titles and the social card's words. Judged: the limits of each channel, the words the
# naming standard retired, and the competitor names - which are NOT in the data file: the internal research guide
# holds them in a fenced `gate-terms` block (owner decision D-7), read here and nowhere else.
$discSummary = 'no discoverability block'
if ($decl.PSObject.Properties['discoverability']) {
    $disc = $decl.discoverability
    $lim = $disc.limits
    $applied = @($disc.applied | ForEach-Object { [string]$_ })
    $fields = [Collections.Generic.List[object]]::new()   # @(label, text) of every text the block types
    function Add-Field([string]$label, [string]$text) { $fields.Add(@($label, $text)) }
    Add-Field 'tagline' ([string]$disc.tagline)
    Add-Field 'githubDescription' ([string]$disc.githubDescription)
    foreach ($t in @($disc.topics)) { Add-Field 'topic' ([string]$t) }
    foreach ($t in @($disc.wingetTags)) { Add-Field 'wingetTag' ([string]$t) }
    foreach ($prop in $disc.storeSearchTerms.PSObject.Properties) { foreach ($t in @($prop.Value)) { Add-Field "storeSearchTerm ($($prop.Name))" ([string]$t) } }
    foreach ($t in @($disc.socialCard.lines)) { Add-Field 'socialCard line' ([string]$t) }
    Add-Field 'socialCard alt' ([string]$disc.socialCard.alt)

    # the limits
    $words = @(([string]$disc.tagline) -split '\s+' | Where-Object { $_ })
    if ($words.Count -lt [int]$lim.taglineWordsMin -or $words.Count -gt [int]$lim.taglineWordsMax) { Problem "discoverability: the tagline has $($words.Count) words, the worksheet's range is $($lim.taglineWordsMin) to $($lim.taglineWordsMax)" }
    $gd = [string]$disc.githubDescription
    if ($gd.Length -gt [int]$lim.githubDescription) { Problem "discoverability: the GitHub description is $($gd.Length) characters, over $($lim.githubDescription)" }
    if ($gd -match '[–—]') { Problem 'discoverability: the GitHub description holds an em or en dash (a hyphen only)' }
    if (-not $gd.StartsWith([string]$disc.category, [StringComparison]::OrdinalIgnoreCase)) { Problem "discoverability: the GitHub description does not open with the category noun '$($disc.category)'" }
    $compact = [string]$decl.discoverability.titles.suffix
    if (-not $gd.Contains($compact)) { Problem "discoverability: the GitHub description does not carry the compact name '$compact'" }
    $topics = @($disc.topics | ForEach-Object { [string]$_ })
    if ($topics.Count -gt [int]$lim.topics) { Problem "discoverability: $($topics.Count) topics, over GitHub's $($lim.topics)" }
    if (@($topics | Select-Object -Unique).Count -ne $topics.Count) { Problem 'discoverability: a topic is listed twice' }
    foreach ($t in $topics) { if ($t -cnotmatch '^[a-z0-9][a-z0-9-]*$' -or $t.Length -gt [int]$lim.topicLength) { Problem "discoverability: topic '$t' is not lowercase letters, digits and hyphens within $($lim.topicLength) characters" } }
    $tags = @($disc.wingetTags | ForEach-Object { [string]$_ })
    $tagOnly = @($disc.wingetOnlyTags | ForEach-Object { [string]$_ })
    if ($tags.Count -gt [int]$lim.wingetTags) { Problem "discoverability: $($tags.Count) winget tags, over $($lim.wingetTags)" }
    if (@($tags | Select-Object -Unique).Count -ne $tags.Count) { Problem 'discoverability: a winget tag is listed twice' }
    foreach ($t in $tags) { if ($topics -notcontains $t -and $tagOnly -notcontains $t) { Problem "discoverability: winget tag '$t' is neither a topic nor declared in wingetOnlyTags - the channels derive from one worksheet" } }
    foreach ($prop in $disc.storeSearchTerms.PSObject.Properties) {
        $terms = @($prop.Value | ForEach-Object { [string]$_ })
        if ($terms.Count -gt [int]$lim.storeSearchTerms) { Problem "discoverability: $($terms.Count) Store search terms for $($prop.Name), over $($lim.storeSearchTerms)" }
        if (@($terms | Select-Object -Unique).Count -ne $terms.Count) { Problem "discoverability: a Store search term of $($prop.Name) is listed twice" }
    }
    $landing = Get-Text 'index.html'
    if ($null -ne $landing) {
        $can = [regex]::Match($landing, '<link rel="canonical" href="(?<u>[^"]+)"')
        if ($can.Success -and $can.Groups['u'].Value -ne [string]$disc.homepage) { Problem "discoverability: the homepage '$($disc.homepage)' is not the landing's canonical '$($can.Groups['u'].Value)'" }
    }

    # the words the naming standard retired, in every field the block types and in every locale's page title
    $titleFields = [Collections.Generic.List[object]]::new()
    foreach ($c in $codes) {
        $pt = [string]$siteCopy.$c.pageTitle
        if (-not $pt) { Problem "discoverability: $siteCopyRel has no pageTitle for '$c'"; continue }
        $full = "$pt$($disc.titles.separator)$($disc.titles.suffix)"
        if ($full.Length -gt [int]$lim.title) { Problem "discoverability: the page title of '$c' is $($full.Length) characters, over $($lim.title): $full" }
        $titleFields.Add(@("pageTitle ($c)", $pt))
        if (-not [string]$siteCopy.$c.ogAlt) { Problem "discoverability: $siteCopyRel has no ogAlt (the card's text alternative) for '$c'" }
    }
    $banned = @($disc.forbiddenWords | ForEach-Object { [string]$_ })
    foreach ($f in @($fields) + @($titleFields)) {
        foreach ($w in $banned) {
            if ($f[1] -match ('(?i)(?<![\p{L}\p{N}])' + [regex]::Escape($w) + '(?![\p{L}\p{N}])')) { Problem "discoverability: $($f[0]) '$($f[1])' uses '$w', a word the naming standard retired" }
        }
    }

    # competitor names (D-7): read from the research guide's gate-terms block, never from this data file
    $researchRel = [string]$disc.competitorTermsSource
    $research = Get-Text $researchRel
    $terms = @()
    if ($null -eq $research) { Problem "discoverability: the research guide $researchRel (the competitor terms' only home) is missing" }
    else {
        $blk = [regex]::Match($research, '(?ms)^```gate-terms\r?\n(?<t>.*?)^```')
        if (-not $blk.Success) { Problem "discoverability: $researchRel has no fenced gate-terms block" }
        else { $terms = @($blk.Groups['t'].Value -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    }
    $competitorHits = 0
    if ($terms.Count -gt 0) {
        $rxTerms = [regex]::new('(?<![\p{L}\p{N}])(?:' + (($terms | Sort-Object { - $_.Length } | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?![\p{L}\p{N}])', 'IgnoreCase')
        $rxPhrases = [regex]::new(('(?<![\p{L}\p{N}])(?:' + ((@($disc.competitorPhrases) | ForEach-Object { [regex]::Escape([string]$_) }) -join '|') + ')(?![\p{L}\p{N}])'), 'IgnoreCase')
        foreach ($f in @($fields) + @($titleFields)) {
            if ($rxTerms.IsMatch($f[1])) { Problem "discoverability: $($f[0]) '$($f[1])' names a competitor (D-7)" }
        }
        foreach ($file in (Expand-Patterns @($disc.competitorScan))) {
            $text = Get-Text $file
            foreach ($m in $rxTerms.Matches($text)) { $competitorHits++; Problem "${file}:$(Get-LineAt $text $m.Index) names a competitor product, '$($m.Value)' (ticket 207 D-7): a published surface names none" }
            foreach ($m in $rxPhrases.Matches($text)) { $competitorHits++; Problem "${file}:$(Get-LineAt $text $m.Index) says '$($m.Value)': a published surface names no alternative to a product (D-7)" }
        }
    }

    # the channels whose fields are in the tree today
    foreach ($a in $applied) {
        switch ($a) {
            'site-titles' {
                foreach ($c in $codes) {
                    if ($c -eq 'en') { continue }
                    $page = Get-Text "lang/$c/index.html"
                    if ($null -eq $page) { Problem "discoverability: lang/$c/index.html is missing"; continue }
                    $tm = [regex]::Match($page, '(?s)<title>(?<t>.*?)</title>')
                    $want = "$([string]$siteCopy.$c.pageTitle)$($disc.titles.separator)$($disc.titles.suffix)"
                    $got = if ($tm.Success) { [Net.WebUtility]::HtmlDecode($tm.Groups['t'].Value) } else { '' }
                    if ($got -ne $want) { Problem "discoverability: lang/$c/index.html has the title '$got', the worksheet derives '$want' (run tools\Build-SitePages.ps1)" }
                }
            }
            'social-card' {
                foreach ($s in @($disc.socialCard.sizes)) { if (-not (Test-Path -LiteralPath (Join-Path $Root ([string]$s.file)) -PathType Leaf)) { Problem "discoverability: the social card $($s.file) does not exist" } }
            }
            'winget' {
                $y = Require-Text 'publishing/winget/SerZhyAle.FastMediaSorter.locale.en-US.yaml'
                $tm = [regex]::Match($y, '(?m)^Tags:[ \t]*\r?\n(?<l>(?:-[ \t]+[^\r\n]*\r?\n?)+)')
                $have = if ($tm.Success) { @($tm.Groups['l'].Value -split '\r?\n' | ForEach-Object { $_.Trim() -replace '^-\s*', '' } | Where-Object { $_ }) } else { @() }
                if ((($have | Sort-Object) -join ',') -ne (($tags | Sort-Object) -join ',')) { Problem "discoverability: the winget Tags [$($have -join ' ')] are not the worksheet's [$($tags -join ' ')]" }
            }
            'store' {
                $csv = Require-Text 'publishing/store/listingData.csv'
                $have = @([regex]::Matches($csv, '(?m)^SearchTerm\d+,\d+,Text,,(?<v>[^,\r\n]*),') | ForEach-Object { $_.Groups['v'].Value.Trim() } | Where-Object { $_ })
                $want = @($disc.storeSearchTerms.'en-us' | ForEach-Object { [string]$_ })
                if ((($have | Sort-Object) -join ',') -ne (($want | Sort-Object) -join ',')) { Problem "discoverability: the Store en-us search terms [$($have -join ', ')] are not the worksheet's [$($want -join ', ')]" }
            }
            default { Problem "discoverability: applied names an unknown channel '$a'" }
        }
    }
    $discSummary = "$($topics.Count) topics, $($tags.Count) winget tags, $($terms.Count) competitor terms over $competitorHits hits, channels held: $($applied -join '+')"
}

# --- verdict -------------------------------------------------------------------------------------------
if (-not $surfaceFiles) { Exit-Verdict 'Test-SitePositioning' 2 'no declared surface was found to judge' }
$summary = "$($ids.Count) components, $regions regions of $surfaceFiles surface files, $editionHits edition names, $mentionSites in-development mentions, $cellCount matrix cells over $evidenceChecked evidence patterns, $claims typed claims in $($factFiles.Count) files; $discSummary"
if ($problems.Count) {
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
    Exit-Verdict 'Test-SitePositioning' 1 "$($problems.Count) problems; $summary"
}
Exit-Verdict 'Test-SitePositioning' 0 $summary
