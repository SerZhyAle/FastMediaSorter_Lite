<#
.SYNOPSIS
    The published-site gate for DOC-EXTERNAL-QUALITY 0.9 (ticket 059), over every page
    the sitemap announces.

.DESCRIPTION
    The published corpus is the repository root as GitHub Pages serves it: the root
    *.html pages (the hand-authored portal, guides and reference pages) plus the
    generated lang/<code>/index.html and lang/<code>/trust.html renders - the exact
    set tools/Build-SitePages.ps1 lists in sitemap.xml. This check holds that set to:

      rule 1  the subject index (topics.html) links every other root page; the glossary
              (glossary.html) defines every termbase term (both directions);
      rule 2  search-index.json covers the same page set as sitemap.xml, with each
              page's real title and description;
      rule 3  every translated locale carries a fingerprint of the English copy it was
              made from (tools/translation-fingerprints.json); a locale whose record
              does not match the current English is reported stale BY NAME. The record
              is re-stamped with -Record after a translation fan-out - batching that
              fan-out to the release boundary is allowed by the contract (section 6);
      rule 4  the termbase (tools/site-termbase.json): a term's approved forms appear
              where the contract looks for them, forbidden variants appear nowhere in
              the pages of their locale, and the product name is on every page;
      rule 5  the full SEO block on every page: title <= 60, description <= 160,
              canonical at the page's real URL, the Open Graph and Twitter sets (with
              title and description agreeing with the page's own), parseable JSON-LD
              whose url is the canonical, the hreflang set where translations exist
              and x-default everywhere, and the og:image asset present on disk;
      rule 6  a guide that declares data-ui-steps carries >= 1 screenshot from
              assets/guides/ with alt text whose PNG's true size matches its width and
              height attributes; every <img> on any page resolves, is tracked and has
              alt text;
      rule 7  every relative link, same-site absolute link, image, stylesheet and
              script resolves to a file GitHub Pages would serve, anchors resolve to
              id/name targets, and no http:// or protocol-relative reference exists.
      rule 9  keyboard and landmarks (SITE-EXPERIENCE rules 15, 16): a skip link first in the body leading to the one main, a
              labelled nav, lang equal to the page language and updated by the in-page switcher, dir=rtl for ar and ur;
      rule 8  full width: no page, header, footer, heading or paragraph carries a fixed width cap and
              `--wide` is 100% (owner decision 2026-10-06, PAGE-CONTENT layout contract);

    The link walker mirrors tools/Test-DocRegistry.ps1 (ticket 058) for the HTML half
    of the corpus: that script walks the Markdown sources, this one walks the pages.

.PARAMETER Record
    Re-stamp tools/translation-fingerprints.json against the current English copy.
    Run it AFTER a translation fan-out, never instead of one.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every rule holds over the whole page set;
      1 - a rule is violated (stale locales, a broken link, a missing SEO tag, ...);
      2 - could not verify: a copy source, the sitemap, the index or the termbase is
          missing or unreadable, so the page set itself could not be established.
    The last line is the verdict, `Test-SiteQuality: <WORD> (..)`.
#>
[CmdletBinding()]
param([switch]$Record)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
Add-Type -AssemblyName System.Drawing

trap {
    if ($Record) { break }
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-SiteQuality' 2 'the check could not run'
}

$Root    = Split-Path $PSScriptRoot -Parent
$BaseUrl = 'https://serzhyale.github.io/FastMediaSorter_Lite'
$LangDir = 'lang'   # keep in step with Build-SitePages.ps1's $LangDir
$OgImage = 'assets/social-preview-1280x640.png'

$CopyFile   = Join-Path $Root 'tools\site-copy.json'
$TrustFile  = Join-Path $Root 'tools\trust-copy.json'
$TermFile   = Join-Path $Root 'tools\site-termbase.json'
$FingerFile = Join-Path $Root 'tools\translation-fingerprints.json'
$Sitemap    = Join-Path $Root 'sitemap.xml'
$SearchIdx  = Join-Path $Root 'search-index.json'

foreach ($f in $CopyFile, $TrustFile) {
    if (-not (Test-Path $f)) { Exit-Verdict 'Test-SiteQuality' 2 "missing copy source: $f" }
}
$copy  = Get-Content $CopyFile  -Raw -Encoding UTF8 | ConvertFrom-Json
$trust = Get-Content $TrustFile -Raw -Encoding UTF8 | ConvertFrom-Json
$codes = @($copy.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('_') })
if ($codes.Count -eq 0) { Exit-Verdict 'Test-SiteQuality' 2 'site-copy.json declares no language' }

Write-Subject 'Test-SiteQuality' ("the published site: sitemap page set, SEO block, links, freshness, termbase, " +
    "search index, screenshots; $(Get-TreeLabel $Root)")

# --- -Record: re-stamp the freshness record and stop ---------------------------
function Get-CopyFingerprint($obj) {
    $json = $obj | ConvertTo-Json -Depth 20
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($json)))).
            Replace('-', '').ToLowerInvariant().Substring(0, 16)
    } finally { $sha.Dispose() }
}

if ($Record) {
    $locales = [ordered]@{}
    foreach ($c in ($codes | Where-Object { $_ -ne 'en' })) {
        $locales[$c] = [ordered]@{ site = Get-CopyFingerprint $copy.en; trust = Get-CopyFingerprint $trust.en }
    }
    $doc = [ordered]@{
        '_comment' = @(
            'Freshness record for the translated site copy (DOC-EXTERNAL-QUALITY rule 3, ticket 059).',
            'Per locale, the fingerprint of the ENGLISH copy (site-copy.json and trust-copy.json)',
            'that locale''s translation was last made from, stamped by',
            '  pwsh -NoProfile -File tools\Test-SiteQuality.ps1 -Record',
            'Test-SiteQuality reports a locale whose record no longer matches the current English',
            'as stale, by name. Re-translate the named locales first - batching the fan-out to the',
            'release boundary is allowed - then re-record. Never edit the hashes by hand.'
        )
        'recorded' = (Get-Date -Format 'yyyy-MM-dd')
        'siteFingerprint'     = (Get-CopyFingerprint $copy.en)
        'trustFingerprint'    = (Get-CopyFingerprint $trust.en)
        'locales'             = $locales
    }
    [IO.File]::WriteAllText($FingerFile, (($doc | ConvertTo-Json -Depth 6) -replace "`r`n", "`n") + "`n",
        (New-Object Text.UTF8Encoding $false))
    Exit-Verdict 'Test-SiteQuality' 0 "recorded fingerprints for $($locales.Count) locale(s) into tools/translation-fingerprints.json"
}

# --- the page set, the way the sitemap enumerates it ---------------------------
function Expected-Canonical([string]$file) {
    if ($file -eq 'index.html') { return "$BaseUrl/" }
    if ($file -match "^$LangDir/([^/]+)/index\.html$") { return "$BaseUrl/$LangDir/$($Matches[1])/" }
    if ($file -match "^$LangDir/([^/]+)/trust\.html$") { return "$BaseUrl/$LangDir/$($Matches[1])/trust.html" }
    return "$BaseUrl/$file"
}

$pages = @()
# 404.html is the host's not-found page (SITE-STRUCTURE rule 15): noindex, never in the sitemap, so not a page of this set
# - its links are walked on their own below.
Get-ChildItem -LiteralPath $Root -Filter '*.html' -File | Where-Object { $_.Name -ne '404.html' } | Sort-Object Name | ForEach-Object {
    $kind = if ($_.Name -eq 'index.html') { 'landing' } elseif ($_.Name -eq 'trust.html') { 'trust' } else { 'root' }
    $pages += @{ File = $_.Name; Lang = 'en'; Kind = $kind }
}
# The privacy policy is a required page type that lives under docs/ and is listed in the sitemap (specification 151, T-3).
if (Test-Path (Join-Path $Root 'docs/privacy.html')) { $pages += @{ File = 'docs/privacy.html'; Lang = 'en'; Kind = 'privacy' } }
foreach ($c in ($codes | Where-Object { $_ -ne 'en' })) {
    foreach ($kind in 'index', 'trust') {
        $rel = "$LangDir/$c/$kind.html"
        if (-not (Test-Path (Join-Path $Root $rel))) {
            Write-Host "  - missing generated page: $rel (run tools\Build-SitePages.ps1)"
            Exit-Verdict 'Test-SiteQuality' 1 "missing generated page: $rel"
        }
        $pages += @{ File = $rel; Lang = $c; Kind = $kind }
    }
}

# Any directory under lang/ that is not a declared locale would still be served - the
# same orphan argument Build-SitePages -Check makes about the repository root.
$stray = @(Get-ChildItem -LiteralPath (Join-Path $Root $LangDir) -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $codes } | ForEach-Object { "$LangDir/$($_.Name)/" })

$problems = [System.Collections.Generic.List[string]]::new()
function Problem([string]$m) { $problems.Add($m) }

# --- HTML helpers ---------------------------------------------------------------
function Page-Html([string]$rel) { Get-Content (Join-Path $Root $rel) -Raw -Encoding UTF8 }

# Visible text: head scripts, styles and markup stripped, entities decoded. Titles are
# page content the reader sees, so Page-Text includes the <title> unless told not to.
function Page-Text([string]$html, [switch]$NoTitle) {
    $t = [regex]::Replace($html, '(?is)<script\b.*?</script>', ' ')
    $t = [regex]::Replace($t, '(?is)<style\b.*?</style>', ' ')
    $t = [regex]::Replace($t, '(?s)<!--.*?-->', ' ')
    if ($NoTitle) { $t = [regex]::Replace($t, '(?is)<title\b.*?</title>', ' ') }
    $t = [Net.WebUtility]::HtmlDecode([regex]::Replace($t, '<[^>]+>', ' '))
    [regex]::Replace($t, '\s+', ' ')
}
function Page-Anchors([string]$html) {
    @([regex]::Matches($html, '(?i)\b(?:id|name)\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
}
function Attr([string]$html, [string]$pattern) {
    $m = [regex]::Match($html, $pattern)
    if ($m.Success) { [Net.WebUtility]::HtmlDecode($m.Groups[1].Value) } else { $null }
}

$inventory = @(Get-WorkspaceFiles $Root)

$anchorCache = @{}
function Target-Anchors([string]$rel) {
    if ($anchorCache.ContainsKey($rel)) { return $anchorCache[$rel] }
    $a = Page-Anchors (Page-Html $rel)
    $anchorCache[$rel] = $a
    $a
}

# Resolve one reference the way GitHub Pages would serve it. Returns the repo-relative
# path, or $null after reporting the problem.
function Resolve-SiteLink([string]$page, [string]$url, [string]$what) {
    $u = $url.Trim()
    if ($u -match '^(mailto:|tel:|data:)') { return '' }   # not a site reference
    if ($u -match '^http://') { Problem "${page}: $what uses http:// - the site is https-only: $url"; return $null }
    if ($u -match '^//') { Problem "${page}: $what is protocol-relative: $url"; return $null }
    if ($u -match '^[a-zA-Z][\w+.-]*:') {
        if ($u.StartsWith("$BaseUrl/")) { $u = $u.Substring($BaseUrl.Length); if ($u -eq '') { $u = '/' } }
        else { return '' }   # an external https reference is fine as-is
    }
    $u = $u -replace '^/\.$', '/'
    $frag = ''
    if ($u -match '^([^#]*)#(.*)$') { $u = $Matches[1]; $frag = $Matches[2] }
    $path = [Uri]::UnescapeDataString(($u -split '\?', 2)[0])
    $full = if (-not $path -and $frag) { Join-Path $Root $page }   # "#anchor" alone is an anchor of the page itself
            elseif (-not $path -or $path -eq '/') { Join-Path $Root 'index.html' }
            elseif ($path.StartsWith('/')) { Join-Path $Root $path.TrimStart('/') }
            else { Join-Path (Split-Path (Join-Path $Root $page) -Parent) $path }
    $full = [IO.Path]::GetFullPath($full)
    if (-not $full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Problem "${page}: $what points outside the site: $url"; return $null
    }
    # A directory URL ("./", "/lang/ru/") is served as its index page.
    if (Test-Path -LiteralPath $full -PathType Container) { $full = Join-Path $full 'index.html' }
    $rel2 = [IO.Path]::GetRelativePath($Root, $full).Replace('\', '/')
    if (-not (Test-Path -LiteralPath $full)) { Problem "${page}: $what target missing: $url"; return $null }
    if ($rel2 -notin $inventory) { Problem "${page}: $what target is not tracked - a clone would serve a 404: $url"; return $null }
    if ($frag) {
        if ($frag -cnotin (Target-Anchors $rel2)) { Problem "${page}: $what anchor missing: $url" }
    }
    return $rel2
}

# --- per-page gates: rules 5, 6, 7 ---------------------------------------------
$linkCount = 0
$imgCount = 0
foreach ($p in $pages) {
    $rel = $p.File
    $h = Page-Html $rel
    $can = Expected-Canonical $rel

    # rule 5 - title and description
    $title = Attr $h '<title>([^<]*)</title>'
    if (-not $title) { Problem "${rel}: no <title>" }
    elseif ($title.Length -gt 60) { Problem "${rel}: title is $($title.Length) chars, over 60: $title" }
    $desc = Attr $h '(?i)<meta\s+name="description"\s+content="([^"]*)"'
    if (-not $desc) { Problem "${rel}: no meta description" }
    elseif ($desc.Length -gt 160) { Problem "${rel}: description is $($desc.Length) chars, over 160" }

    # rule 5 - canonical at the page's real URL
    $canSeen = Attr $h '(?i)<link\s+rel="canonical"\s+href="([^"]*)"'
    if (-not $canSeen) { Problem "${rel}: no canonical link" }
    elseif ($canSeen -ne $can) { Problem "${rel}: canonical $canSeen is not the page's URL $can" }

    # rule 5 - Open Graph and Twitter, agreeing with the page's own copy
    foreach ($prop in 'og:type', 'og:site_name', 'og:title', 'og:description', 'og:url', 'og:image', 'og:locale') {
        if ($h -notmatch [regex]::Escape("property=`"$prop`"")) { Problem "${rel}: missing og meta: $prop" }
    }
    foreach ($name in 'twitter:card', 'twitter:title', 'twitter:description', 'twitter:image') {
        if ($h -notmatch [regex]::Escape("name=`"$name`"")) { Problem "${rel}: missing twitter meta: $name" }
    }
    $ogTitle = Attr $h '(?i)<meta\s+property="og:title"\s+content="([^"]*)"'
    if ($ogTitle -and $title -and $ogTitle -ne $title) { Problem "${rel}: og:title disagrees with the title tag" }
    $ogDesc = Attr $h '(?i)<meta\s+property="og:description"\s+content="([^"]*)"'
    if ($ogDesc -and $desc -and $ogDesc -ne $desc) { Problem "${rel}: og:description disagrees with the meta description" }
    $ogUrl = Attr $h '(?i)<meta\s+property="og:url"\s+content="([^"]*)"'
    if ($ogUrl -and $ogUrl -ne $can) { Problem "${rel}: og:url is not the canonical" }
    $ogImg = Attr $h '(?i)<meta\s+property="og:image"\s+content="([^"]*)"'
    if ($ogImg -and $ogImg -ne "$BaseUrl/$OgImage") { Problem "${rel}: og:image is not $BaseUrl/$OgImage" }
    $twDesc = Attr $h '(?i)<meta\s+name="twitter:description"\s+content="([^"]*)"'
    if ($twDesc -and $desc -and $twDesc -ne $desc) { Problem "${rel}: twitter:description disagrees with the meta description" }

    # rule 5 - structured data
    $ldRaw = Attr $h '(?is)<script\s+type="application/ld\+json">(.*?)</script>'
    if (-not $ldRaw) { Problem "${rel}: no JSON-LD block" }
    else {
        try {
            $ld = $ldRaw | ConvertFrom-Json
            if (-not $ld.'@context' -or $ld.'@context' -ne 'https://schema.org') { Problem "${rel}: JSON-LD has no schema.org context" }
            if (-not ($ld.PSObject.Properties['name'] -or $ld.PSObject.Properties['headline'])) { Problem "${rel}: JSON-LD names nothing" }
            if ($ld.url -and $ld.url -ne $can) { Problem "${rel}: JSON-LD url is not the canonical" }
            if (-not $ld.inLanguage) { Problem "${rel}: JSON-LD declares no inLanguage" }
        } catch { Problem "${rel}: JSON-LD does not parse ($($_.Exception.Message))" }
    }

    # rule 5 - hreflang: the full set where a translation exists, x-default everywhere
    $alts = @([regex]::Matches($h, '(?i)<link\s+rel="alternate"\s+hreflang="([^"]*)"\s+href="([^"]*)"') |
        ForEach-Object { [pscustomobject]@{ Lang = $_.Groups[1].Value; Href = $_.Groups[2].Value } })
    $hasTranslations = $p.Kind -in 'landing', 'index', 'trust'
    if ($hasTranslations) {
        $wantHref = @{}
        foreach ($c in $codes) {
            $wantHref[$c] = if ($p.Kind -eq 'trust') {
                if ($c -eq 'en') { "$BaseUrl/trust.html" } else { "$BaseUrl/$LangDir/$c/trust.html" }
            } else {
                if ($c -eq 'en') { "$BaseUrl/" } else { "$BaseUrl/$LangDir/$c/" }
            }
        }
        foreach ($c in $codes) {
            $a = $alts | Where-Object { $_.Lang -eq $c }
            if (-not $a) { Problem "${rel}: hreflang set is missing '$c'" }
            elseif ($a.Href -ne $wantHref[$c]) { Problem "${rel}: hreflang '$c' points at $($a.Href), not $($wantHref[$c])" }
        }
        $xd = $alts | Where-Object { $_.Lang -eq 'x-default' }
        $wantXd = if ($p.Kind -eq 'trust') { "$BaseUrl/trust.html" } else { "$BaseUrl/" }
        if (-not $xd) { Problem "${rel}: hreflang set is missing x-default" }
        elseif ($xd.Href -ne $wantXd) { Problem "${rel}: x-default points at $($xd.Href), not $wantXd" }
    } else {
        $xd = $alts | Where-Object { $_.Lang -eq 'x-default' }
        if (-not $xd) { Problem "${rel}: no hreflang=x-default (a page without translations still declares its one canonical language)" }
        elseif ($xd.Href -ne $can) { Problem "${rel}: x-default points at $($xd.Href), not the canonical" }
        $extra = @($alts | Where-Object { $_.Lang -ne 'x-default' })
        if ($extra.Count -gt 0) { Problem "${rel}: declares hreflang alternates ($(@($extra | ForEach-Object Lang) -join ', ')) for translations that do not exist" }
    }

    # rule 6 - screenshots: a guide that declares multi-step UI must show the UI
    if ($h -match 'data-ui-steps') {
        $shots = @([regex]::Matches($h, '(?i)<img\b[^>]*\bsrc="((?:assets/guides|assets\\guides)/[^"]+)"[^>]*>'))
        if ($shots.Count -eq 0) { Problem "${rel}: declares data-ui-steps but carries no assets/guides/ screenshot" }
        foreach ($m in $shots) {
            $tag = $m.Groups[0].Value
            $src = $m.Groups[1].Value
            $alt = Attr $tag 'alt="([^"]*)"'
            if (-not $alt) { Problem "${rel}: screenshot without alt text: $src" }
            $w = Attr $tag 'width="(\d+)"'; $ht = Attr $tag 'height="(\d+)"'
            if (-not $w -or -not $ht) { Problem "${rel}: screenshot without width/height attributes: $src"; continue }
            $png = Join-Path $Root ($src -replace '/', '\')
            if (-not (Test-Path $png)) { Problem "${rel}: screenshot missing on disk: $src"; continue }
            if ($src -notin $inventory) { Problem "${rel}: screenshot not tracked: $src"; continue }
            try {
                $bmp = [System.Drawing.Bitmap]::new($png)
                try {
                    if ($bmp.Width -ne [int]$w -or $bmp.Height -ne [int]$ht) {
                        Problem "${rel}: screenshot $src is really $($bmp.Width)x$($bmp.Height), not the declared ${w}x${ht}"
                    }
                } finally { $bmp.Dispose() }
            } catch { Problem "${rel}: screenshot unreadable: $src" }
        }
    }

    # rule 6/7 - every image resolves, is tracked, and is named
    foreach ($m in [regex]::Matches($h, '(?i)<img\b[^>]*?\bsrc="([^"]+)"[^>]*>')) {
        $imgCount++
        $tag = $m.Groups[0].Value
        if ($tag -notmatch '(?i)\balt="[^"]*"') { Problem "${rel}: image without alt attribute: $($m.Groups[1].Value)" }
        $null = Resolve-SiteLink $rel $m.Groups[1].Value 'image'
    }

    # rule 7 - links: anchors, stylesheets, scripts, and every href
    foreach ($m in [regex]::Matches($h, '(?i)<(?:a|link)\b[^>]*?\bhref="([^"]+)"')) {
        $linkCount++
        $null = Resolve-SiteLink $rel $m.Groups[1].Value 'link'
    }
    foreach ($m in [regex]::Matches($h, '(?i)<script\b[^>]*?\bsrc="([^"]+)"')) {
        $linkCount++
        $null = Resolve-SiteLink $rel $m.Groups[1].Value 'script'
    }
}

# --- the not-found page: rule 7 over its links, and rule 5's noindex ------------
if (Test-Path (Join-Path $Root '404.html')) {
    $nfHtml = Page-Html '404.html'
    if ($nfHtml -notmatch '<meta name="robots" content="noindex">') { Problem '404.html: the not-found page is not marked noindex' }
    foreach ($m in [regex]::Matches($nfHtml, '(?i)<(?:a|link)\b[^>]*?\bhref="([^"]+)"')) { $linkCount++; $null = Resolve-SiteLink '404.html' $m.Groups[1].Value 'link' }
    foreach ($m in [regex]::Matches($nfHtml, '(?i)<img\b[^>]*?\bsrc="([^"]+)"')) { $imgCount++; $null = Resolve-SiteLink '404.html' $m.Groups[1].Value 'image' }
} else { Problem '404.html (the not-found page) does not exist' }

# --- sitemap and search index: rule 2 ------------------------------------------
if (-not (Test-Path $Sitemap)) { Exit-Verdict 'Test-SiteQuality' 2 'sitemap.xml not found - run tools\Build-SitePages.ps1' }
$smText = Get-Content $Sitemap -Raw -Encoding UTF8
$locs = @([regex]::Matches($smText, '<loc>([^<]+)</loc>') | ForEach-Object { $_.Groups[1].Value })
$expected = @($pages | ForEach-Object { Expected-Canonical $_.File })
foreach ($e in $expected) { if ($e -notin $locs) { Problem "sitemap.xml does not list $e" } }
foreach ($l in $locs) { if ($l -notin $expected) { Problem "sitemap.xml lists a page this corpus does not have: $l" } }

if (-not (Test-Path $SearchIdx)) { Problem 'search-index.json not found - run tools\Build-SitePages.ps1' }
else {
    try {
        $idx = @(Get-Content $SearchIdx -Raw -Encoding UTF8 | ConvertFrom-Json)
        $idxUrls = @($idx | ForEach-Object { $_.url })
        foreach ($e in $expected) { if ($e -notin $idxUrls) { Problem "search-index.json does not cover $e" } }
        foreach ($u in $idxUrls) { if ($u -notin $expected) { Problem "search-index.json lists a page outside the sitemap set: $u" } }
        foreach ($e in $idx) {
            $rel = if ($e.url -eq "$BaseUrl/") { 'index.html' }
                   elseif ($e.url -match '/$') { $e.url.Substring($BaseUrl.Length + 1) + 'index.html' }
                   else { $e.url.Substring($BaseUrl.Length + 1) }
            $h = Page-Html $rel
            $t = Attr $h '<title>([^<]*)</title>'
            $d = Attr $h '(?i)<meta\s+name="description"\s+content="([^"]*)"'
            if (-not $e.title -or -not $e.description) { Problem "search-index.json entry without title or description: $($e.url)" }
            elseif (($e.title -ne $t) -or ($e.description -ne $d)) { Problem "search-index.json title/description do not match the page: $($e.url)" }
            if (-not $e.lang) { Problem "search-index.json entry without a language: $($e.url)" }
        }
    } catch { Problem "search-index.json does not parse ($($_.Exception.Message))" }
}

# --- rule 1: the subject index and the glossary ---------------------------------
if (-not (Test-Path (Join-Path $Root 'topics.html'))) { Problem 'topics.html (the subject index) does not exist' }
else {
    $tp = Page-Html 'topics.html'
    foreach ($p in ($pages | Where-Object { $_.File -ne 'topics.html' -and $_.Kind -in 'landing', 'root' })) {
        $want = if ($p.File -eq 'index.html') { './' } else { $p.File }
        $linked = [regex]::Matches($tp, [regex]::Escape("href=`"$want`""))
        if ($linked.Count -eq 0) { Problem "topics.html does not link $($p.File)" }
    }
}
if (-not (Test-Path (Join-Path $Root 'glossary.html'))) { Problem 'glossary.html (the terminology glossary) does not exist' }
$glossaryHtml = if (Test-Path (Join-Path $Root 'glossary.html')) { Page-Html 'glossary.html' } else { $null }

# --- rule 3: translation freshness ----------------------------------------------
if (-not (Test-Path $FingerFile)) {
    Problem 'tools/translation-fingerprints.json does not exist - no locale records the English copy it was translated from'
} else {
    try {
        $fp = Get-Content $FingerFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $siteFp = Get-CopyFingerprint $copy.en
        $trustFp = Get-CopyFingerprint $trust.en
        foreach ($c in $codes) {
            if ($c -eq 'en') { continue }
            $rec = $fp.locales.PSObject.Properties[$c]
            if (-not $rec) { Problem "locale '$c' has no freshness record - was it translated from the current English?" ; continue }
            if ($rec.Value.site -ne $siteFp) { Problem "site copy stale: '$c' was translated from an older English landing page - re-translate, then re-record" }
            if ($rec.Value.trust -ne $trustFp) { Problem "trust copy stale: '$c' was translated from an older English trust page - re-translate, then re-record" }
        }
        foreach ($prop in $fp.locales.PSObject.Properties) {
            if ($prop.Name -notin $codes) { Problem "freshness record names '$($prop.Name)', which is not a declared locale" }
        }
    } catch { Exit-Verdict 'Test-SiteQuality' 2 "translation-fingerprints.json is unreadable: $($_.Exception.Message)" }
}

# --- rule 4: the termbase --------------------------------------------------------
if (-not (Test-Path $TermFile)) { Problem 'tools/site-termbase.json does not exist' }
else {
    try {
        $tb = Get-Content $TermFile -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($c in $tb.locales) { if ($c -notin $codes) { Problem "termbase names locale '$c', which the site does not declare" } }
        foreach ($c in $codes) { if ($c -notin $tb.locales) { Problem "termbase does not cover locale '$c'" } }
        $guides = @($pages | Where-Object { $_.File -like 'how-to-*.html' })
        foreach ($t in $tb.terms) {
            if (-not $t.id) { Problem 'termbase has a term without an id'; continue }
            $forbiddenByLocale = @{}
            foreach ($prop in $t.forbidden.PSObject.Properties) {
                if ($prop.Name -notin $codes) { Problem "termbase term '$($t.id)' forbids variants for unknown locale '$($prop.Name)'" }
                $forbiddenByLocale[$prop.Name] = @($prop.Value)
            }
            if ($t.everyPage) {
                $needle = @($t.everyPage)
                foreach ($p in $pages) {
                    $hay = Page-Text (Page-Html $p.File)
                    foreach ($n in $needle) {
                        if ($hay.IndexOf($n, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                            Problem "termbase: '$($t.id)' approved form missing on page $($p.File): $n"
                        }
                    }
                }
            }
            if ($t.scope -eq 'lang' -or $t.scope -eq 'guides') {
                $stemLocales = @($t.stems.PSObject.Properties.Name)
                $wantLocales = if ($t.scope -eq 'lang') { $codes } else { @('en', 'ru', 'uk') }
                foreach ($c in $wantLocales) { if ($c -notin $stemLocales) { Problem "termbase term '$($t.id)' has no approved form for '$c'" } }
                foreach ($c in $stemLocales) { if ($c -notin $wantLocales) { Problem "termbase term '$($t.id)' declares form for '$c' outside its scope" } }
                foreach ($prop in $t.stems.PSObject.Properties) {
                    $c = $prop.Name
                    $hayPages = if ($t.scope -eq 'lang') { @($pages | Where-Object { $_.Lang -eq $c }) } else { $guides }
                    if ($hayPages.Count -eq 0) { continue }
                    $hay = ($hayPages | ForEach-Object { Page-Text (Page-Html $_.File) }) -join ' '
                    $hit = @($prop.Value | Where-Object { $hay.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
                    if ($hit.Count -eq 0) {
                        Problem "termbase: '$($t.id)' approved form for '$c' appears on no page of its scope: $($prop.Value -join ' / ')"
                    }
                }
                foreach ($c in $forbiddenByLocale.Keys) {
                    $hayPages = if ($t.scope -eq 'lang') { @($pages | Where-Object { $_.Lang -eq $c }) } else { $guides }
                    $hay = ($hayPages | ForEach-Object { Page-Text (Page-Html $_.File) }) -join ' '
                    foreach ($bad in $forbiddenByLocale[$c]) {
                        if ($hay.IndexOf($bad, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                            Problem "termbase: forbidden variant of '$($t.id)' in pages of '$c': $bad"
                        }
                    }
                }
            }
            # rule 1: the glossary defines every term, and only termbase terms
            if ($glossaryHtml -and $glossaryHtml -notmatch "data-term=`"$($t.id)`"") {
                Problem "glossary.html does not define termbase term '$($t.id)'"
            }
        }
        if ($glossaryHtml) {
            foreach ($m in [regex]::Matches($glossaryHtml, 'data-term="([^"]+)"')) {
                $id = $m.Groups[1].Value
                $known = @($tb.terms | Where-Object { $_.id -eq $id })
                if ($known.Count -eq 0) { Problem "glossary.html defines '$id', which is not a termbase term" }
            }
        }
    } catch { Exit-Verdict 'Test-SiteQuality' 2 "site-termbase.json is unreadable: $($_.Exception.Message)" }
}

# --- rule 8: full width (owner decision 2026-10-06, restating PAGE-CONTENT "Layout contract") --
# Every page uses the whole width of the screen. A page, its header, its footer, its headings and its
# text carry no fixed measure (px/ch/rem cap) and no `--wide` other than 100%: narrow measures belong
# inside a card or a callout, never to the page. This is the check that stops the centred column
# from coming back through a new page, a new template or a copied rule.
$widthCorpus = [System.Collections.Generic.List[string]]::new()
foreach ($p in $pages) { $widthCorpus.Add($p.File) }
$widthCorpus.Add('404.html')
Get-ChildItem -LiteralPath (Join-Path $Root 'docs') -Filter '*.html' -File -ErrorAction SilentlyContinue |
    ForEach-Object { $widthCorpus.Add("docs/$($_.Name)") }
$widthCorpus.Add('assets/fms-site.css')
$pageLevelSelector = '(?i)(^|[\s>+~,])(html|body|main|header|footer|h1|h2|\.container|\.wrap|\.site-header|\.site-footer|\.hero|\.hero-copy|\.lead|\.lead-narrow|\.sec-head|\.section|\.section-desc|\.get|\.gloss|\.tech-links|\.download-section)(?![\w-])'
foreach ($rel in $widthCorpus) {
    $fullPath = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $fullPath)) { continue }
    $raw = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
    $css = if ($rel -like '*.css') { $raw } else {
        (@([regex]::Matches($raw, '(?is)<style\b[^>]*>(.*?)</style>') | ForEach-Object { $_.Groups[1].Value })) -join "`n"
    }
    $css = [regex]::Replace($css, '(?s)/\*.*?\*/', ' ')
    foreach ($m in [regex]::Matches($css, '(?i)(?<![\w-])--wide\s*:\s*([^;}]+)')) {
        if ($m.Groups[1].Value.Trim() -notmatch '^(100%|none)$') {
            Problem "${rel}: --wide is '$($m.Groups[1].Value.Trim())' - the page uses the full screen width (rule 8)"
        }
    }
    foreach ($rule in [regex]::Matches($css, '([^{}]+)\{([^{}]*)\}')) {
        $sel = $rule.Groups[1].Value.Trim()
        if ($sel -notmatch $pageLevelSelector) { continue }
        foreach ($d in [regex]::Matches($rule.Groups[2].Value, '(?i)(?<![\w-])max-width\s*:\s*([^;]+)')) {
            $v = $d.Groups[1].Value.Trim()
            if ($v -notmatch '^(none|100%|100vw|var\(--wide\)|calc\(\s*100%.*)$') {
                Problem "${rel}: '$sel' caps its width at '$v' - page-level text, header and footer use the full screen width (rule 8)"
            }
        }
    }
    foreach ($m in [regex]::Matches($raw, '(?i)\bstyle\s*=\s*"[^"]*max-width\s*:\s*([^";]+)')) {
        $v = $m.Groups[1].Value.Trim()
        if ($v -notmatch '^(none|100%|100vw|var\(--wide\)|calc\(\s*100%.*)$') { Problem "${rel}: an inline style caps a width at '$v' (rule 8)" }
    }
}

# --- rule 10 / rule 9: the in-page locale floor (specification 151) ---------------
# A page that switches language in-page (data-l spans or blocks) carries the core three, in the same number, and its
# language control offers exactly the languages the page has. The landing and the trust page are generated per
# locale and are held by Build-SitePages -Check, so only the root guides, the reference pages, the edition page and
# the privacy page are read here.
foreach ($p in ($pages | Where-Object { $_.Lang -eq 'en' -and $_.Kind -in 'root', 'privacy' })) {
    $h = Page-Html $p.File
    $n = @{}
    foreach ($code in 'en', 'ru', 'ua') { $n[$code] = ([regex]::Matches($h, "\sdata-l=`"$code`"")).Count }
    if (($n['en'] + $n['ru'] + $n['ua']) -eq 0) { continue }
    foreach ($code in 'ru', 'ua') {
        if ($n[$code] -ne $n['en']) { Problem "$($p.File): $($n[$code]) '$code' in-page blocks against $($n['en']) in English - the core three (en, ru, uk) must all be complete (rule 10)" }
    }
    $buttons = @([regex]::Matches($h, 'data-setlang="([a-z]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $have = @('en', 'ru', 'ua' | Where-Object { $n[$_] -gt 0 })
    if (($buttons -join ',') -ne (($have | Sort-Object) -join ',')) { Problem "$($p.File): the language control offers [$($buttons -join ', ')] but the page carries [$($have -join ', ')] (rule 9)" }
}

# --- rule 9: keyboard and landmarks (SITE-EXPERIENCE rules 15 and 16, specification 152) ---------
# Every page: a skip link as the first link of the body that leads to the one main; a labelled nav; a lang attribute equal
# to the page's language (a page that switches language in-page must set it when it switches); dir=rtl for ar and ur.
foreach ($p in $pages) {
    $h = Page-Html $p.File
    $b = $h.IndexOf('<body')
    if ($b -lt 0) { Problem "$($p.File): no <body> (rule 9)"; continue }
    $body = $h.Substring($b)
    $first = [regex]::Match($body, '<a\b[^>]*>')
    if (-not $first.Success -or $first.Value -notmatch 'class="skip-link"') { Problem "$($p.File): the skip link is not the first link of the page (rule 9)" }
    else {
        $target = [regex]::Match($first.Value, 'href="#([^"]+)"').Groups[1].Value
        $mains = [regex]::Matches($h, '<main\b[^>]*>')
        if ($mains.Count -ne 1) { Problem "$($p.File): $($mains.Count) <main> elements, expected exactly one (rule 9)" }
        elseif ($mains[0].Value -notmatch "\sid=`"$([regex]::Escape($target))`"") { Problem "$($p.File): the skip link leads to #$target but <main> does not carry that id (rule 9)" }
    }
    if ($h -notmatch '<nav\b[^>]*\saria-label(ledby)?="[^"]+"') { Problem "$($p.File): no labelled <nav> (rule 9)" }
    $htmlTag = [regex]::Match($h, '<html\b[^>]*>').Value
    $want = $p.Lang
    $have = [regex]::Match($htmlTag, '\slang="([^"]*)"').Groups[1].Value
    if ($have -ne $want) { Problem "$($p.File): <html lang=`"$have`"> but the page is in '$want' (rule 9)" }
    if (($h -match '\sdata-setlang=') -and ($h -notmatch "\.lang\s*=\s*v\s*===\s*'ua'|setAttribute\('lang'")) { Problem "$($p.File): the language control does not update <html lang> when it switches (rule 9)" }
    $rtl = $p.Lang -in 'ar', 'ur'
    if ($rtl -and $htmlTag -notmatch '\sdir="rtl"') { Problem "$($p.File): a right-to-left page without dir=`"rtl`" (rule 9)" }
}

# --- verdict ---------------------------------------------------------------------
if ($stray.Count -gt 0) { $problems.Add("undeclared folders under ${LangDir}/: $($stray -join ', ')") }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'Test-SiteQuality' 1 "$($problems.Count) defect(s): $($pages.Count) pages, $linkCount links, $imgCount images"
}
Exit-Verdict 'Test-SiteQuality' 0 "$($pages.Count) pages, $linkCount links, $imgCount images, $($codes.Count) locales, rules 1-7, the full-width rule and the keyboard-and-landmarks rule held"
