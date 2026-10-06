<#
.SYNOPSIS
    Resolve every address of the product site that something outside the site holds, against the
    page set, offline (ticket 150, SITE-STRUCTURE rule 8).

.DESCRIPTION
    The programs, the installer, the winget manifests, the release body, the Store listing and the
    READMEs all hold addresses of the site. tools/site-addresses.jsonl is the one list of them:

      {"group": ..}     one record per page group - the form its locale sibling takes
                        ("lang/<code>/trust.html", or null when the group has none) and whether
                        the group is in sitemap.xml. This is the single statement of that form.
      {"address": ..}   one record per (address, holder): the address, the file that holds it,
                        the page group (kind), and how the holder carries it:
                          literal   (default) the address appears verbatim in the holder;
                          built     the holder assembles it at run time - "fragment" is the text
                                    that must appear in the holder instead (a program constant
                                    is compared with this list by the unit tests as well);
                          external  the holder is outside the tree (the Store listing): the
                                    address is resolved, the holder is not read.

    Judged, over the current files on disk and with no network:

      1  the list parses, every record has its fields, no (address, holder) is listed twice, every
         kind names a declared group;
      2  every address is on the site's base URL and resolves to a page the generated set holds:
         a file on disk (a path ending in "/" is its index.html), in sitemap.xml when the group
         says so, and - when the address carries a "#anchor" - an id or name on that page. A query
         string is not part of the address;
      3  the locale-sibling form of each group in use is stated once and holds: a group with a
         "locale" form has that page for every language of tools/site-copy.json, on disk and in
         the sitemap;
      4  every holder that is a file exists and carries its address (literal) or its fragment
         (built);
      5  completeness: every address of the site found in the holder files (src, publishing,
         .github, the READMEs, assets/help) is listed for that holder - a new address cannot
         arrive without a line here.

    -Live is the separate, hand-run mode: it then requests each distinct address from the published
    site (GET, redirects followed) and fails on any answer that is not 200. No answer at all (offline,
    DNS) is COULD NOT VERIFY, never a pass. It is never part of a test run or the release preflight:
    the pages go live from main, so a request before the push would judge the previous publication.

    Ends in the shared one-line verdict (CHECK-VERDICT): exit 0 PASS / 1 FAIL / 2 COULD NOT VERIFY.

.PARAMETER Root
    The tree to judge (default: this repository). The fixture self-test points it at a tiny tree.

.PARAMETER List
    The address list (default: tools/site-addresses.jsonl under -Root).

.PARAMETER Live
    Also request every distinct address from the published site.
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$List = '',
    [switch]$Live
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'Test-SiteAddresses' 2 'the check could not complete' }
$Root = [IO.Path]::GetFullPath($Root)
if (-not $List) { $List = Join-Path $Root 'tools/site-addresses.jsonl' }
Write-Subject 'Test-SiteAddresses' "the addresses of the site held outside it, resolved against the page set; $(Get-TreeLabel $Root)"

$BaseUrl = 'https://serzhyale.github.io/FastMediaSorter_Lite/'   # keep in step with SitePages.SiteRoot and Build-SitePages.ps1
$LangDir = 'lang'

foreach ($f in $List, (Join-Path $Root 'sitemap.xml'), (Join-Path $Root 'tools/site-copy.json')) {
    if (-not (Test-Path -LiteralPath $f)) { Exit-Verdict 'Test-SiteAddresses' 2 "missing input: $f" }
}

$problems = [Collections.Generic.List[string]]::new()
function Problem([string]$message) { $problems.Add($message) }

# --- the list -------------------------------------------------------------------------------------
$groups = @{}
$entries = [Collections.Generic.List[object]]::new()
$seenPair = @{}
$lineNo = 0
foreach ($raw in Get-Content -LiteralPath $List -Encoding UTF8) {
    $lineNo++
    if (-not $raw.Trim() -or $raw.TrimStart().StartsWith('#')) { continue }
    try { $r = $raw | ConvertFrom-Json } catch { Problem "line ${lineNo}: not valid JSON"; continue }
    if ($r.PSObject.Properties['group']) {
        if (-not "$($r.group)".Trim()) { Problem "line ${lineNo}: group record without a name"; continue }
        if ($groups.ContainsKey([string]$r.group)) { Problem "line ${lineNo}: group '$($r.group)' declared twice"; continue }
        if (-not $r.PSObject.Properties['locale'] -or -not $r.PSObject.Properties['sitemap']) { Problem "line ${lineNo}: group '$($r.group)' lacks 'locale' or 'sitemap'"; continue }
        if ($r.locale -and "$($r.locale)" -notmatch '<code>') { Problem "line ${lineNo}: group '$($r.group)' locale form has no <code>" }
        $groups[[string]$r.group] = $r
        continue
    }
    $bad = $false
    foreach ($field in 'address', 'holder', 'kind') {
        if (-not $r.$field -or -not "$($r.$field)".Trim()) { Problem "line ${lineNo}: record lacks '$field'"; $bad = $true }
    }
    if ($bad) { continue }
    $carried = if ($r.PSObject.Properties['carried']) { [string]$r.carried } else { 'literal' }
    if ($carried -notin 'literal', 'built', 'external') { Problem "line ${lineNo}: carried '$carried' is not literal, built or external"; continue }
    if ($carried -eq 'built' -and -not $r.fragment) { Problem "line ${lineNo}: a built address needs the fragment its holder must carry"; continue }
    $pair = "$($r.address)|$($r.holder)"
    if ($seenPair.ContainsKey($pair)) { Problem "line ${lineNo}: $($r.address) is listed twice for $($r.holder)"; continue }
    $seenPair[$pair] = $true
    $entries.Add([pscustomobject]@{ Line = $lineNo; Address = [string]$r.address; Holder = [string]$r.holder; Kind = [string]$r.kind; Carried = $carried; Fragment = [string]$r.fragment })
}
foreach ($e in $entries) {
    if (-not $groups.ContainsKey($e.Kind)) { Problem "line $($e.Line): kind '$($e.Kind)' is not a declared group" }
}

# --- the page set ---------------------------------------------------------------------------------
$sitemap = @{}
[xml]$smDoc = Get-Content -LiteralPath (Join-Path $Root 'sitemap.xml') -Raw -Encoding UTF8
foreach ($u in $smDoc.GetElementsByTagName('loc')) { $sitemap[$u.InnerText.Trim()] = $true }
if ($sitemap.Count -eq 0) { Exit-Verdict 'Test-SiteAddresses' 2 'sitemap.xml lists no page' }
$copy = Get-Content -LiteralPath (Join-Path $Root 'tools/site-copy.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$codes = @($copy.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('_') -and $_ -ne 'en' })
if ($codes.Count -eq 0) { Exit-Verdict 'Test-SiteAddresses' 2 'tools/site-copy.json names no language' }

function Get-PageFile([string]$relative) {
    if ($relative -eq '' -or $relative.EndsWith('/')) { $relative += 'index.html' }
    return Join-Path $Root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

# The sitemap names a directory page by its directory ("lang/ru/"), never by its index.html.
function Get-MapUrl([string]$relative) {
    if ($relative -eq 'index.html' -or $relative.EndsWith('/index.html')) { $relative = $relative.Substring(0, $relative.Length - 'index.html'.Length) }
    return $BaseUrl + $relative
}

# a page's anchors, read once
$anchorCache = @{}
function Get-Anchors([string]$file) {
    if (-not $anchorCache.ContainsKey($file)) {
        $t = [IO.File]::ReadAllText($file)
        $set = [Collections.Generic.HashSet[string]]::new()
        foreach ($m in [regex]::Matches($t, '(?:\bid|\bname)\s*=\s*["'']([^"'']+)["'']')) { [void]$set.Add($m.Groups[1].Value) }
        $anchorCache[$file] = $set
    }
    return $anchorCache[$file]
}

# --- 2: every address resolves --------------------------------------------------------------------
$resolved = 0
foreach ($e in $entries) {
    if (-not $e.Address.StartsWith($BaseUrl, [StringComparison]::Ordinal)) { Problem "line $($e.Line): $($e.Address) is not on the site ($BaseUrl)"; continue }
    $rest = $e.Address.Substring($BaseUrl.Length)
    $anchor = ''
    $h = $rest.IndexOf('#'); if ($h -ge 0) { $anchor = $rest.Substring($h + 1); $rest = $rest.Substring(0, $h) }
    $q = $rest.IndexOf('?'); if ($q -ge 0) { $rest = $rest.Substring(0, $q) }
    if ($rest -match '(^|/)\.\.(/|$)|\\') { Problem "line $($e.Line): $($e.Address) is not a plain site path"; continue }
    $file = Get-PageFile $rest
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Problem "line $($e.Line): $($e.Address) points at no page (no file $($file.Substring($Root.Length + 1).Replace('\', '/')))"; continue }
    $g = $groups[$e.Kind]
    if ($g -and $g.sitemap) {
        $mapUrl = Get-MapUrl $rest
        if (-not $sitemap.ContainsKey($mapUrl)) { Problem "line $($e.Line): $($e.Address) is a $($e.Kind) page but sitemap.xml does not list it" }
    }
    if ($anchor) {
        if (-not (Get-Anchors $file).Contains($anchor)) { Problem "line $($e.Line): $($e.Address) names an anchor the page does not have (#$anchor)" }
    }
    $resolved++
}

# --- 3: the locale sibling of each group in use ---------------------------------------------------
foreach ($kind in @($entries | ForEach-Object Kind | Sort-Object -Unique)) {
    $g = $groups[$kind]
    if (-not $g -or -not $g.locale) { continue }
    foreach ($code in $codes) {
        $rel = ([string]$g.locale).Replace('<code>', $code)
        if (-not (Test-Path -LiteralPath (Get-PageFile $rel) -PathType Leaf)) { Problem "group '$kind': the $code sibling has no page ($rel)"; continue }
        if ($g.sitemap -and -not $sitemap.ContainsKey((Get-MapUrl $rel))) { Problem "group '$kind': the $code sibling $rel is not in sitemap.xml" }
    }
}

# --- 4: each holder carries its address -----------------------------------------------------------
$externalCount = 0
foreach ($e in $entries) {
    if ($e.Carried -eq 'external') { $externalCount++; continue }
    $hf = Join-Path $Root $e.Holder
    if (-not (Test-Path -LiteralPath $hf -PathType Leaf)) { Problem "line $($e.Line): the holder $($e.Holder) is not a file"; continue }
    $text = [IO.File]::ReadAllText($hf)
    $needle = if ($e.Carried -eq 'literal') { $e.Address } else { $e.Fragment }
    if (-not $text.Contains($needle)) { Problem "line $($e.Line): $($e.Holder) does not carry $(if ($e.Carried -eq 'literal') { $e.Address } else { "the fragment [$needle]" })" }
}

# --- 5: nothing is held that the list does not name -----------------------------------------------
$skip = '^(packages/|bin/|obj/|dist/|payload/|stage/|tools/ocr-cache/|node_modules/|docs/specifications/|\.git/)|(^|/)(bin|obj)/'
function Get-InspectableFiles {
    if (Get-Command rg -ErrorAction SilentlyContinue) { return @(Get-WorkspaceFiles $Root) }
    $prune = '^(\.git|bin|obj|dist|packages|payload|stage|node_modules|ocr-cache)$'
    $found = [Collections.Generic.List[string]]::new()
    $stack = [Collections.Generic.Stack[string]]::new(); $stack.Push($Root)
    while ($stack.Count) {
        $dir = $stack.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($dir)) {
            $name = [IO.Path]::GetFileName($entry)
            if ([IO.Directory]::Exists($entry)) { if ($name -notmatch $prune) { $stack.Push($entry) } }
            else { $found.Add($entry.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')) }
        }
    }
    return $found.ToArray()
}
# The holders: what ships or is published from outside the site's own pages. The site's pages link each
# other (Test-SiteQuality walks them), a ticket or the changelog is history, a test pins a constant.
$holderScope = '^(src/.+\.(vb|html|js|ps1)|publishing/.+|\.github/.+|README[^/]*\.md|assets/help/.+)$'
$holderSkip  = '^publishing/(msix/(dist|stage)|store/screenshots)/|^publishing/store/listingData\.csv$'
$listed = @{}
foreach ($e in $entries) { $listed["$($e.Holder)|$($e.Address)"] = $true }
$holderFiles = @(Get-InspectableFiles | Where-Object {
        $_ -notmatch $skip -and $_ -match $holderScope -and $_ -notmatch $holderSkip -and
        ([IO.Path]::GetExtension($_).ToLowerInvariant() -in '.vb', '.html', '.js', '.ps1', '.iss', '.yaml', '.yml', '.md', '.xml', '.txt', '.csv')
    })
$rxUrl = [regex]('(?<![A-Za-z0-9])' + [regex]::Escape($BaseUrl.TrimEnd('/')) + '(?:/[^\s"''<>)\]`,;|]*)?')
$foundCount = 0
foreach ($rel in $holderFiles) {
    $text = [IO.File]::ReadAllText((Join-Path $Root $rel))
    $seen = [Collections.Generic.HashSet[string]]::new()
    foreach ($m in $rxUrl.Matches($text)) {
        $url = $m.Value.TrimEnd('.')
        if ($url -eq $BaseUrl.TrimEnd('/')) { $url = $BaseUrl }
        $q = $url.IndexOf('?'); if ($q -ge 0) { $url = $url.Substring(0, $q) }   # the page reads the query; the address is the page
        if (-not $seen.Add($url)) { continue }
        $foundCount++
        if (-not $listed.ContainsKey("$rel|$url")) { Problem "$rel holds $url, which tools/site-addresses.jsonl does not list for it" }
    }
}

# --- the live mode --------------------------------------------------------------------------------
$liveNote = ''
$liveUnreachable = 0
if ($Live) {
    $distinct = @($entries | ForEach-Object { ($_.Address -replace '[?#].*$', '') } | Sort-Object -Unique)
    $ok = 0
    foreach ($url in $distinct) {
        try {
            $resp = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 30 -MaximumRedirection 5
            if ([int]$resp.StatusCode -eq 200) { $ok++ } else { Problem "live: $url answered $([int]$resp.StatusCode)" }
        } catch {
            $status = $null
            if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            if ($status) { Problem "live: $url answered $status" } else { $liveUnreachable++; Write-Host "  live: no answer for $url ($($_.Exception.Message))" }
        }
    }
    $liveNote = "; live $ok/$($distinct.Count)"
}

# --- verdict --------------------------------------------------------------------------------------
if ($entries.Count -eq 0) { Exit-Verdict 'Test-SiteAddresses' 2 'the list holds no address' }
if ($problems.Count) {
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
    Exit-Verdict 'Test-SiteAddresses' 1 "$($problems.Count) problem(s) over $($entries.Count) entries"
}
if ($Live -and $liveUnreachable) { Exit-Verdict 'Test-SiteAddresses' 2 "offline part clean, but $liveUnreachable address(es) got no answer from the site" }
Exit-Verdict 'Test-SiteAddresses' 0 "$($entries.Count) entries, $resolved resolved, $foundCount held in $($holderFiles.Count) files, $externalCount outside the tree, $($groups.Count) groups, $($codes.Count + 1) languages$liveNote"
