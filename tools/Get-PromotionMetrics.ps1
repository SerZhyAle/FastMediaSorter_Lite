<#
.SYNOPSIS
    Reads the free-promotion campaign's counters (ticket 207, T-0; canon PROMOTION section 7) and writes one dated
    checkpoint file. Read-only: every call is a GET against GitHub or the public site, and nothing is changed anywhere.

.DESCRIPTION
    The measures are the baseline table of the ticket (section 1.1) and its metrics row (section 9): stars, forks,
    watchers, releases, asset downloads with the checksum files excluded, the traffic API's last fourteen days (views,
    clones, referrers), issues ever opened, the repository's description, homepage and topics, whether a custom social
    preview is set, the Pages state, the answers of the site's own files, the winget package versions upstream and the
    Store product page - plus what the tree itself says (sitemap size and dates, the landing's head, the banned
    markup). A number the script cannot read is written as "unknown" with the reason; it is never estimated. No
    analytics, no tracker, no network call beyond the platforms' own public counters (rule 2).

    The traffic API forgets after fourteen days, so a checkpoint is never more than fourteen days after the previous
    one while outreach is active; the script prints the age of the newest earlier checkpoint it finds.

.PARAMETER OutDir
    Where the dated file goes. Default: docs/specifications/207_promotion/metrics (a local, Git-ignored working folder,
    like the specifications themselves - the numbers belong to the ticket's record, not to the product tree).

.PARAMETER Repo
    owner/name of the repository to read (default SerZhyAle/FastMediaSorter_Lite).

.PARAMETER Offline
    Read only the tree (no GitHub, no HTTP): for a machine without the network, or a quick look at the tree's side.

.EXAMPLE
    pwsh -NoProfile -File tools\Get-PromotionMetrics.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [string]$Repo = 'SerZhyAle/FastMediaSorter_Lite',
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $Root 'docs\specifications\207_promotion\metrics' }
$today = Get-Date -Format 'yyyy-MM-dd'
$HostRoot = 'https://serzhyale.github.io'
$Site = "$HostRoot/$($Repo.Split('/')[1])"
$unknown = 'unknown'

$measures = [ordered]@{}
$notes = [Collections.Generic.List[string]]::new()
function Note([string]$m) { $notes.Add($m) }

# --- GitHub (read-only GET) -----------------------------------------------------------------------------------------
$ghReady = $false
if (-not $Offline) {
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        & gh auth status *> $null
        $ghReady = ($LASTEXITCODE -eq 0)
        if (-not $ghReady) { Note 'gh is not signed in: every GitHub measure is unknown' }
    } else { Note 'gh is not installed: every GitHub measure is unknown' }
}

function Invoke-GhApi([string]$path) {
    # One GET. Returns the parsed JSON, or $null after a note (a traffic endpoint answers 403 without push access).
    if (-not $ghReady) { return $null }
    $raw = & gh api $path 2>&1
    if ($LASTEXITCODE -ne 0) { Note "gh api $path -> $(($raw | Select-Object -First 1))"; return $null }
    try { ($raw -join "`n") | ConvertFrom-Json } catch { Note "gh api $path -> not JSON"; $null }
}

function Get-AllPages([string]$path) {
    $all = [Collections.Generic.List[object]]::new()
    for ($page = 1; $page -le 20; $page++) {
        $sep = if ($path.Contains('?')) { '&' } else { '?' }
        $chunk = Invoke-GhApi "$path${sep}per_page=100&page=$page"
        if ($null -eq $chunk) { if ($page -eq 1) { return $null } else { break } }
        $items = @($chunk)
        foreach ($i in $items) { $all.Add($i) }
        if ($items.Count -lt 100) { break }
    }
    , @($all)
}

$repoInfo = Invoke-GhApi "repos/$Repo"
if ($repoInfo) {
    $measures.stars = [int]$repoInfo.stargazers_count
    $measures.forks = [int]$repoInfo.forks_count
    $measures.watchers = [int]$repoInfo.subscribers_count
    $measures.description = [string]$repoInfo.description
    $measures.homepage = [string]$repoInfo.homepage
    $measures.topics = @($repoInfo.topics)
    $measures.hasWiki = [bool]$repoInfo.has_wiki
    $measures.hasDiscussions = [bool]$repoInfo.has_discussions
} else {
    foreach ($k in 'stars', 'forks', 'watchers', 'description', 'homepage', 'topics', 'hasWiki', 'hasDiscussions') { $measures[$k] = $unknown }
}

$releases = Get-AllPages "repos/$Repo/releases"
if ($null -ne $releases) {
    $measures.releases = $releases.Count
    $measures.prereleases = @($releases | Where-Object { $_.prerelease }).Count
    $latest = @($releases | Where-Object { -not $_.draft -and -not $_.prerelease } | Sort-Object { [datetime]$_.published_at } -Descending)[0]
    $measures.latestRelease = if ($latest) { [ordered]@{ tag = [string]$latest.tag_name; published = ([datetime]$latest.published_at).ToString('yyyy-MM-dd') } } else { $unknown }
    $sum = 0; $sumAll = 0; $sumLatest = 0
    foreach ($r in $releases) {
        foreach ($a in @($r.assets)) {
            $sumAll += [int]$a.download_count
            if (-not ([string]$a.name).EndsWith('.sha256', [StringComparison]::OrdinalIgnoreCase)) {
                $sum += [int]$a.download_count
                if ($latest -and $r.tag_name -eq $latest.tag_name) { $sumLatest += [int]$a.download_count }
            }
        }
    }
    $measures.downloads = $sum                      # checksum files excluded (canon section 7)
    $measures.downloadsWithChecksums = $sumAll
    $measures.downloadsLatestRelease = $sumLatest
} else {
    foreach ($k in 'releases', 'prereleases', 'latestRelease', 'downloads', 'downloadsWithChecksums', 'downloadsLatestRelease') { $measures[$k] = $unknown }
}

$views = Invoke-GhApi "repos/$Repo/traffic/views"
$clones = Invoke-GhApi "repos/$Repo/traffic/clones"
$refs = Invoke-GhApi "repos/$Repo/traffic/popular/referrers"
$measures.views14d = if ($views) { [ordered]@{ count = [int]$views.count; uniques = [int]$views.uniques } } else { $unknown }
$measures.clones14d = if ($clones) { [ordered]@{ count = [int]$clones.count; uniques = [int]$clones.uniques } } else { $unknown }
if ($null -ne $refs) {
    $list = @($refs | ForEach-Object { [ordered]@{ referrer = [string]$_.referrer; count = [int]$_.count; uniques = [int]$_.uniques } })
    $measures.referrers14d = $list
    $searchRx = '(?i)google|bing|duckduckgo|yandex|baidu|ecosia|yahoo|brave|startpage|seznam|naver'
    $measures.searchReferrers14d = @($list | Where-Object { $_.referrer -match $searchRx })
} elseif ($ghReady) { $measures.referrers14d = $unknown; $measures.searchReferrers14d = $unknown }
else { $measures.referrers14d = $unknown; $measures.searchReferrers14d = $unknown }

$issues = Invoke-GhApi "search/issues?q=repo:$Repo+is:issue&per_page=1"
$measures.issuesEverOpened = if ($issues) { [int]$issues.total_count } else { $unknown }

$measures.usesCustomOpenGraphImage = $unknown
$measures.openGraphImageUrl = $unknown
if ($ghReady) {
    $owner, $name = $Repo.Split('/')
    $q = 'query($o:String!,$n:String!){repository(owner:$o,name:$n){usesCustomOpenGraphImage openGraphImageUrl}}'
    $g = & gh api graphql -f "query=$q" -f "o=$owner" -f "n=$name" 2>&1
    if ($LASTEXITCODE -eq 0) {
        try {
            $r = (($g -join "`n") | ConvertFrom-Json).data.repository
            $measures.usesCustomOpenGraphImage = [bool]$r.usesCustomOpenGraphImage
            $measures.openGraphImageUrl = [string]$r.openGraphImageUrl
        } catch { Note 'GraphQL usesCustomOpenGraphImage -> not JSON' }
    } else { Note "GraphQL usesCustomOpenGraphImage -> $(($g | Select-Object -First 1))" }
}

$pages = Invoke-GhApi "repos/$Repo/pages"
$measures.pages = if ($pages) { [ordered]@{ status = [string]$pages.status; buildType = [string]$pages.build_type; source = "$($pages.source.branch):$($pages.source.path)"; cname = [string]$pages.cname; httpsEnforced = [bool]$pages.https_enforced } } else { $unknown }

# winget upstream: the version directories of the two packages
function Get-WingetVersions([string]$package) {
    $c = Invoke-GhApi "repos/microsoft/winget-pkgs/contents/manifests/s/SerZhyAle/$package"
    if ($null -eq $c) { return $unknown }
    # In winget-pkgs a package id is a path: SerZhyAle.FastMediaSorter.Server lives under SerZhyAle/FastMediaSorter/Server, so
    # the listing of the first package also holds a directory named Server that is not one of its versions.
    $dirs = @($c | Where-Object { $_.type -eq 'dir' -and $_.name -match '^\d+(\.\d+)+$' } | ForEach-Object { [string]$_.name })
    $newest = @($dirs | Sort-Object { try { [version]$_ } catch { [version]'0.0.0.0' } } -Descending)[0]
    [ordered]@{ versions = $dirs.Count; newest = $newest }
}
$measures.winget = [ordered]@{
    'SerZhyAle.FastMediaSorter'        = Get-WingetVersions 'FastMediaSorter'
    'SerZhyAle.FastMediaSorter.Server' = Get-WingetVersions 'FastMediaSorter/Server'
}
$openPrs = Invoke-GhApi 'search/issues?q=repo:microsoft/winget-pkgs+is:pr+is:open+SerZhyAle+in:title&per_page=1'
$measures.wingetOpenPullRequests = if ($openPrs) { [int]$openPrs.total_count } else { $unknown }

# --- the public site and the Store page (plain GET, status code only) ---------------------------------------------
$script:Http = $null
function Get-HttpStatus([string]$url) {
    # The status the address itself answers: a redirect is reported as 301/302, not followed (a wiki that "is on" but
    # answers 302 to the repository is exactly the fact to record).
    if ($Offline) { return $unknown }
    try {
        if (-not $script:Http) {
            $handler = [System.Net.Http.HttpClientHandler]::new()
            $handler.AllowAutoRedirect = $false
            $script:Http = [System.Net.Http.HttpClient]::new($handler)
            $script:Http.Timeout = [TimeSpan]::FromSeconds(25)
            $script:Http.DefaultRequestHeaders.UserAgent.ParseAdd('FastMediaSorter-promotion-metrics/1.0 (read-only status check)')
        }
        $resp = $script:Http.GetAsync($url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try { [int]$resp.StatusCode } finally { $resp.Dispose() }
    } catch {
        Note "GET $url -> $($_.Exception.Message)"
        $unknown
    }
}
$copy = Get-Content (Join-Path $PSScriptRoot 'site-copy.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$measures.http = [ordered]@{
    landing         = Get-HttpStatus "$Site/"
    sitemap         = Get-HttpStatus "$Site/sitemap.xml"
    projectRobots   = Get-HttpStatus "$Site/robots.txt"
    hostRootRobots  = Get-HttpStatus "$HostRoot/robots.txt"
    wiki            = Get-HttpStatus "https://github.com/$Repo/wiki"
    storePage       = Get-HttpStatus ([string]$copy._channels.store.url)
}

# --- the tree's own side ----------------------------------------------------------------------------------------------
$tree = [ordered]@{}
$sitemapPath = Join-Path $Root 'sitemap.xml'
if (Test-Path -LiteralPath $sitemapPath) {
    $sm = [IO.File]::ReadAllText($sitemapPath, [Text.UTF8Encoding]::new($false))
    $tree.sitemapUrls = ([regex]::Matches($sm, '<loc>')).Count
    $tree.sitemapLastmod = ([regex]::Matches($sm, '<lastmod>')).Count
} else { $tree.sitemapUrls = $unknown; $tree.sitemapLastmod = $unknown }
$landing = Join-Path $Root 'index.html'
if (Test-Path -LiteralPath $landing) {
    $h = [IO.File]::ReadAllText($landing, [Text.UTF8Encoding]::new($false))
    $tree.landingHead = [ordered]@{
        canonical = [bool]($h -match '<link rel="canonical" href="https://')
        ogImageWidth = [bool]($h -match 'og:image:width')
        ogImageHeight = [bool]($h -match 'og:image:height')
        ogImageAlt = [bool]($h -match 'og:image:alt')
        twitterCard = [bool]($h -match 'twitter:card')
        jsonLdBlocks = ([regex]::Matches($h, 'application/ld\+json')).Count
    }
}
$served = @(Get-ChildItem -LiteralPath $Root -Filter '*.html' -File) + @(Get-ChildItem -LiteralPath (Join-Path $Root 'lang') -Filter '*.html' -Recurse -File -ErrorAction SilentlyContinue) +
    @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs') -Filter '*.html' -File -ErrorAction SilentlyContinue)
$banned = [ordered]@{ metaKeywords = 'name="keywords"'; aggregateRating = 'aggregateRating'; softwareVersion = 'softwareVersion'; faqPage = 'FAQPage'; howTo = '"HowTo"'; utm = 'utm_' }
$bannedCounts = [ordered]@{}
foreach ($k in $banned.Keys) { $bannedCounts[$k] = 0 }
foreach ($f in $served) {
    $t = [IO.File]::ReadAllText($f.FullName, [Text.UTF8Encoding]::new($false))
    foreach ($k in $banned.Keys) { if ($t.Contains($banned[$k])) { $bannedCounts[$k]++ } }
}
$tree.pagesWithBannedMarkup = $bannedCounts
$tree.mandatoryPages = [ordered]@{
    landing = (Test-Path -LiteralPath $landing)
    privacy = (Test-Path -LiteralPath (Join-Path $Root 'docs\privacy.html'))
    guides = @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs') -Filter 'how-to-*.html' -File -ErrorAction SilentlyContinue).Count
    whatsNew = (Test-Path -LiteralPath (Join-Path $Root 'whats-new.html'))
    support = (Test-Path -LiteralPath (Join-Path $Root 'support.html'))
}
$measures.tree = $tree

# --- the dated file and the section 9 row ----------------------------------------------------------------------------
$doc = [ordered]@{
    '_comment' = 'Written by tools/Get-PromotionMetrics.ps1 (ticket 207 T-0): read-only counters, "unknown" where a number could not be read. Never edited by hand.'
    date       = $today
    repository = $Repo
    site       = $Site
    measures   = $measures
    notes      = @($notes)
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$file = Join-Path $OutDir "metrics-$today.json"
[IO.File]::WriteAllText($file, (($doc | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))

function Cell($v) { if ($v -is [string] -and $v -eq $unknown) { $unknown } else { "$v" } }
$m = $measures
$uv = if ($m.views14d -is [System.Collections.IDictionary]) { "$($m.views14d.count) ($($m.views14d.uniques))" } else { $unknown }
$uc = if ($m.clones14d -is [System.Collections.IDictionary]) { "$($m.clones14d.count) ($($m.clones14d.uniques))" } else { $unknown }
$sr = if ($m.searchReferrers14d -is [string]) { $unknown } elseif (@($m.searchReferrers14d).Count -eq 0) { 'none' } else { (@($m.searchReferrers14d) | ForEach-Object { "$($_.referrer) $($_.count)" }) -join ', ' }
Write-Host ''
Write-Host "Promotion metrics $today -> $file"
Write-Host '| Date | Stars | Forks | Downloads (excl. .sha256) | Views 14 d (uniq) | Clones 14 d (uniq) | Search referrers 14 d | Store acquisitions | Search Console clicks / impressions | Indexed pages |'
Write-Host ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | unknown | unknown | unknown |" -f $today, (Cell $m.stars), (Cell $m.forks), (Cell $m.downloads), $uv, $uc, $sr)
Write-Host '(Store acquisitions, Search Console and the indexed-page count live in owner-held consoles: they stay "unknown" until read there.)'
foreach ($n in $notes) { Write-Host "  note: $n" }
$prev = @(Get-ChildItem -LiteralPath $OutDir -Filter 'metrics-*.json' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "metrics-$today.json" } | Sort-Object Name -Descending)
if ($prev.Count -gt 0) {
    $prevDate = [datetime]::ParseExact($prev[0].BaseName.Substring('metrics-'.Length), 'yyyy-MM-dd', $null)
    $age = [int]((Get-Date).Date - $prevDate).TotalDays
    Write-Host "  previous checkpoint: $($prev[0].Name), $age day(s) ago$(if ($age -gt 14) { ' - OVER fourteen days: the traffic API has forgotten the gap' })"
}
