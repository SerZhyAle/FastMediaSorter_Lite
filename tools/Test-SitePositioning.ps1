<#
.SYNOPSIS
    Hold the site's claims to one positioning source and to facts typed once, offline (ticket 155,
    SITE-REPRESENTATION rules 1 to 4).

.DESCRIPTION
    docs/guides/POSITIONING.md is the one positioning source; tools/site-facts.json is its machine form
    (the family sentence, the pillars in order, the public editions, the reserved door, the language count
    and the platform floor, and the surfaces that must follow them). Judged over the current files on disk,
    no network:

      1  source    the data file parses; the pillars are unique; POSITIONING.md quotes the family sentence,
                   lists the pillars in the data file's order and names every edition and the reservation;
                   PRODUCT_NAMING.md owns every edition name; the landing copy's English family sentence is
                   the data file's (rule 1);
      2  pillars   every declared surface (landing and its locale pages, the server page, the READMEs, the
                   Store description, the winget description) has its region, names the first N pillars and
                   names them in the source's order (rule 2);
      3  editions  a name of the form "Fast Media Sorter & Sharing: <X>" on any scanned file is a declared
                   public edition; the planned Media Exchange Server is mentioned only inside the places the
                   data file declares, with no link, no date and no download there, and marked planned where
                   a marker is declared (rule 4 and the owner's ruling Q155-2);
      4  facts     the language count and the platform floor are read from their real sources (the
                   Localization table, the site copy's locale keys, the Inno scripts, the Server winget
                   manifest, the MSIX manifest) and compared with the data file; every "N languages" and every
                   "Windows 10 <version>" in the scanned files is a value the data file holds (rule 3);
      5  discover  the discoverability block (ticket 207 T-1, canon PROMOTION section 2) - the tagline,
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
Write-Subject 'Test-SitePositioning' "the positioning source, the declared surfaces, the editions and the typed facts of the site; $(Get-TreeLabel $Root)"

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
foreach ($k in 'positioning', 'editions', 'facts', 'surfaces') {
    if (-not $decl.PSObject.Properties[$k]) { Exit-Verdict 'Test-SitePositioning' 2 "$Facts has no '$k'" }
}

# --- 1: the source ---------------------------------------------------------------------------------
$pillars = @($decl.positioning.pillars)
$ids = @($pillars | ForEach-Object { [string]$_.id })
if (-not $ids.Count) { Problem 'site-facts.json lists no pillar' }
if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Problem 'site-facts.json repeats a pillar id' }
foreach ($p in $pillars) { if (-not [string]$p.title) { Problem "pillar '$($p.id)' has no title" } }
$lead = [string]$decl.positioning.lead
if (-not $lead) { Problem 'site-facts.json has no family sentence (positioning.lead)' }

$sourceRel = [string]$decl.positioning.source
$sourceText = Require-Text $sourceRel
$sourceNorm = Norm $sourceText
if ($lead -and -not $sourceNorm.Contains((Norm $lead))) { Problem "$sourceRel does not quote the family sentence of site-facts.json" }
$cursor = -1
foreach ($p in $pillars) {
    $at = $sourceText.IndexOf('**' + [string]$p.title + '**', [StringComparison]::Ordinal)
    if ($at -lt 0) { Problem "$sourceRel does not list the pillar '$($p.title)' as **$($p.title)**" }
    elseif ($at -lt $cursor) { Problem "$sourceRel lists '$($p.title)' before the pillar that precedes it in site-facts.json" }
    else { $cursor = $at }
}
$naming = Require-Text ([string]$decl.facts.sources.naming)
foreach ($e in @($decl.editions)) {
    $n = [string]$e.name
    if (-not $sourceText.Contains('`' + $n + '`')) { Problem "$sourceRel does not name the edition '$n'" }
    if (-not $naming.Contains('`' + $n + '`')) { Problem "$($decl.facts.sources.naming) does not own the edition name '$n'" }
}
foreach ($r in @($decl.reservations)) {
    if (-not $sourceText.Contains([string]$r.name)) { Problem "$sourceRel does not record the reservation '$($r.name)'" }
    if (-not $naming.Contains([string]$r.name)) { Problem "$($decl.facts.sources.naming) does not own the name '$($r.name)'" }
}
$siteCopyRel = [string]$decl.facts.sources.siteCopy
$siteCopy = Require-Text $siteCopyRel | ConvertFrom-Json
if ($siteCopy.en.family -ne $lead) { Problem "$siteCopyRel en.family is not the family sentence of site-facts.json" }

# --- 2: the pillars on the declared surfaces -----------------------------------------------------------
$regions = 0
$surfaceFiles = 0
foreach ($s in @($decl.surfaces)) {
    $files = if ($s.PSObject.Properties['files']) { @($s.files) } else { @($s.file) }
    $anchors = $s.anchors
    foreach ($k in $anchors.PSObject.Properties.Name) { if ($ids -notcontains $k) { Problem "surface $($files -join ', ') anchors the unknown pillar '$k'" } }
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
                if (-not $span.ContainsKey($id)) { Problem "$at ($($s.why)) does not name the pillar '$id', which the positioning source puts in its first $($s.names)" }
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

# --- 3: the editions and the reserved door -------------------------------------------------------------
$suffixes = @($decl.editions | Where-Object { $_.PSObject.Properties['nameSuffix'] } | ForEach-Object { [string]$_.nameSuffix })
$editionScan = New-Object Collections.Generic.SortedSet[string] ([StringComparer]::Ordinal)
foreach ($r in @($decl.reservations)) { foreach ($f in (Expand-Patterns @($r.scan))) { [void]$editionScan.Add($f) } }
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

$mentionSites = 0
foreach ($r in @($decl.reservations)) {
    if ([bool]$r.public) { Problem "reservation '$($r.name)' is declared public - a public edition belongs in 'editions'"; continue }
    $allowed = @{}   # file -> list of [start, end] of its declared regions
    foreach ($s in @($r.surfaces)) {
        $files = if ($s.PSObject.Properties['files']) { @($s.files) } else { @($s.file) }
        foreach ($file in $files) {
            $text = Get-Text $file
            if ($null -eq $text) { Problem "reservation '$($r.name)' declares $file, which is not a file"; continue }
            $m = [regex]::Match($text, [string]$s.scope)
            if (-not $m.Success) { Problem "${file}: the declared place of '$($r.name)' /$($s.scope)/ is not found - drop the declaration if the door is gone"; continue }
            $allowed[$file] = @($m.Index, ($m.Index + $m.Length))
            $at = "${file}:$(Get-LineAt $text $m.Index)"
            if ($m.Value -notmatch [string]$r.mentionPattern) { Problem "$at is declared as the place of '$($r.name)' but does not name it" }
            if ($m.Value -match '(?i)href\s*=') { Problem "$at links somewhere from the reservation of '$($r.name)': a non-public variant carries no link or download" }
            if ($m.Value -match '\]\(') { Problem "$at links somewhere from the reservation of '$($r.name)': a non-public variant carries no link or download" }
            if ($m.Value -match '\b20\d\d\b') { Problem "$at gives a date for '$($r.name)': the reservation carries no date" }
            if ($s.PSObject.Properties['marker'] -and -not $m.Value.Contains([string]$s.marker)) { Problem "$at lost its planned marker '$($s.marker)'" }
        }
    }
    foreach ($file in (Expand-Patterns @($r.scan))) {
        $text = Get-Text $file
        foreach ($m in [regex]::Matches($text, [string]$r.mentionPattern)) {
            $mentionSites++
            $inside = $allowed.ContainsKey($file) -and $m.Index -ge $allowed[$file][0] -and $m.Index -lt $allowed[$file][1]
            if (-not $inside) { Problem "${file}:$(Get-LineAt $text $m.Index) mentions '$($r.name)', a variant that is not public, outside the place the reservation declares (SITE-REPRESENTATION rule 4, ruling Q155-2)" }
        }
    }
}

# --- 4: the typed facts --------------------------------------------------------------------------------
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

# --- 5: the discoverability fields (ticket 207 T-1; canon PROMOTION section 2) --------------------------------------
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
$summary = "$($ids.Count) pillars, $regions regions of $surfaceFiles surface files, $editionHits edition names, $mentionSites reservation mentions, $claims typed claims in $($factFiles.Count) files; $discSummary"
if ($problems.Count) {
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
    Exit-Verdict 'Test-SitePositioning' 1 "$($problems.Count) problems; $summary"
}
Exit-Verdict 'Test-SitePositioning' 0 $summary
