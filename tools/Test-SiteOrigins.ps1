<#
.SYNOPSIS
    List the third-party origins every published page contacts and hold them against the declaration
    and the privacy disclosure, in both directions, offline (ticket 153, SITE-EXPERIENCE rule 14).

.DESCRIPTION
    tools/site-origins.json declares each origin (host, what loads it, what it receives, which pages
    contact it). Judged over the current files on disk, no network:

      1  the declaration parses and every record has its fields;
      2  every published page (the root *.html, lang/*/*.html, docs/*.html, assets/help/*.html) is
         scanned for what it CONTACTS: a stylesheet / preconnect / preload / icon link, a script,
         image, frame, source, video, audio, embed or object address, a fetch or XHR address, an
         @import or url() - in the page itself and in the local stylesheets and scripts it loads.
         A navigation link (<a href>), a canonical or alternate link, a schema.org or XML namespace
         is not a contact. Comments are dropped first;
      3  every contacted host that is not this site's own is declared (a new origin cannot arrive
         without a line here), and no contacted host is on the declaration's "forbidden" list
         (an analytics, tag-manager or advertising origin);
      4  every declared host is contacted by at least one page, and the set of pages that contact it
         is exactly the set the declaration names (a stale or too-wide declaration fails);
      5  docs/privacy.html names every declared host in each of its language blocks (en, ru, ua);
      6  every locale of tools/trust-copy.json carries an "origins" paragraph that names every host.

    Ends in the shared one-line verdict (CHECK-VERDICT): exit 0 PASS / 1 FAIL / 2 COULD NOT VERIFY.

.PARAMETER Root
    The tree to judge (default: this repository). The fixture self-test points it at a tiny tree.

.PARAMETER Declaration
    The declaration (default: tools/site-origins.json under -Root).
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Declaration = ''
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'Test-SiteOrigins' 2 'the check could not complete' }
$Root = [IO.Path]::GetFullPath($Root)
if (-not $Declaration) { $Declaration = Join-Path $Root 'tools/site-origins.json' }
Write-Subject 'Test-SiteOrigins' "the third-party origins of the published pages against their declaration and disclosure; $(Get-TreeLabel $Root)"

$OwnHosts = @('serzhyale.github.io')   # the site itself; keep in step with SitePages.SiteRoot
$PrivacyPage = 'docs/privacy.html'
$TrustCopy = Join-Path $Root 'tools/trust-copy.json'

foreach ($f in $Declaration, $TrustCopy, (Join-Path $Root $PrivacyPage)) {
    if (-not (Test-Path -LiteralPath $f)) { Exit-Verdict 'Test-SiteOrigins' 2 "missing input: $f" }
}

$problems = [Collections.Generic.List[string]]::new()
function Problem([string]$message) { $problems.Add($message) }

function Read-Utf8([string]$path) { [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false)) }

# --- 1: the declaration ---------------------------------------------------------------------------
$decl = Read-Utf8 $Declaration | ConvertFrom-Json
$declared = [ordered]@{}
foreach ($o in @($decl.origins)) {
    foreach ($field in 'host', 'via', 'purpose', 'receives', 'pages') {
        if (-not $o.$field) { Problem "declaration: an origin record lacks '$field' ($($o.host))" }
    }
    $h = ([string]$o.host).ToLowerInvariant()
    if (-not $h) { continue }
    if ($declared.Contains($h)) { Problem "declaration: $h is declared twice"; continue }
    $declared[$h] = $o
}
$forbidden = @($decl.forbidden | ForEach-Object { ([string]$_).ToLowerInvariant() })

# --- 2: what every published page contacts --------------------------------------------------------
$pages = [Collections.Generic.List[string]]::new()
function Add-Pages([string]$dir, [string]$prefix, [switch]$Recurse) {
    $full = Join-Path $Root $dir
    if (-not (Test-Path -LiteralPath $full)) { return }
    $files = if ($Recurse) { Get-ChildItem -LiteralPath $full -Filter '*.html' -File -Recurse } else { Get-ChildItem -LiteralPath $full -Filter '*.html' -File }
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
        $pages.Add($rel)
    }
}
Add-Pages '' ''
Add-Pages 'lang' 'lang' -Recurse
Add-Pages 'docs' 'docs'
Add-Pages 'assets/help' 'assets/help'
$pages = [Collections.Generic.List[string]]@($pages | Sort-Object -Unique)
if ($pages.Count -eq 0) { Exit-Verdict 'Test-SiteOrigins' 2 'no published page found under the root' }

function Strip-Comments([string]$text, [bool]$html) {
    $text = [regex]::Replace($text, '(?s)/\*.*?\*/', ' ')
    if ($html) { $text = [regex]::Replace($text, '(?s)<!--.*?-->', ' ') }
    return $text
}

$HostPattern = '(?i)(?:https?:)?//([a-z0-9][a-z0-9.-]*[a-z0-9])(?::\d+)?(?=[/"''\s)?#]|$)'
function Get-Hosts([string]$fragment) {
    foreach ($m in [regex]::Matches($fragment, $HostPattern)) { $m.Groups[1].Value.ToLowerInvariant() }
}

# The contact sites of a text. $html: also read the element attributes. Returns host names.
function Get-Contacts([string]$text, [bool]$html) {
    $text = Strip-Comments $text $html
    $found = [Collections.Generic.List[string]]::new()
    if ($html) {
        foreach ($tag in [regex]::Matches($text, '(?is)<(link|script|img|iframe|source|video|audio|embed|object|track|input)\b[^>]*>')) {
            $name = $tag.Groups[1].Value.ToLowerInvariant()
            $markup = $tag.Value
            if ($name -eq 'link') {
                $rel = [regex]::Match($markup, '(?i)\brel\s*=\s*["'']?([^"''>]+)')
                if ($rel.Success -and $rel.Groups[1].Value -notmatch '(?i)stylesheet|preconnect|dns-prefetch|preload|prefetch|modulepreload|icon|manifest') { continue }
                foreach ($a in [regex]::Matches($markup, '(?i)\b(?:href|imagesrcset)\s*=\s*["'']([^"'']*)["'']')) { foreach ($h in Get-Hosts $a.Groups[1].Value) { $found.Add($h) } }
            } else {
                foreach ($a in [regex]::Matches($markup, '(?i)\b(?:src|srcset|data|poster)\s*=\s*["'']([^"'']*)["'']')) { foreach ($h in Get-Hosts $a.Groups[1].Value) { $found.Add($h) } }
            }
        }
    }
    # script and style code, whether inline or in a file: fetch / XHR / import / url() with an address
    foreach ($m in [regex]::Matches($text, '(?is)\bfetch\s*\(\s*[''"`]([^''"`]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    foreach ($m in [regex]::Matches($text, '(?is)\.open\s*\(\s*[''"][A-Za-z]+[''"]\s*,\s*[''"`]([^''"`]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    foreach ($m in [regex]::Matches($text, '(?is)\bnew\s+(?:WebSocket|EventSource)\s*\(\s*[''"`]([^''"`]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    foreach ($m in [regex]::Matches($text, '(?is)@import\s+(?:url\()?\s*[''"]?([^''")\s;]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    foreach ($m in [regex]::Matches($text, '(?is)\burl\(\s*[''"]?([^''")\s]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    foreach ($m in [regex]::Matches($text, '(?is)\bimport\s*\(\s*[''"]([^''"]+)')) { foreach ($h in Get-Hosts $m.Groups[1].Value) { $found.Add($h) } }
    return $found
}

# page -> set of contacted hosts (own site excluded); the page's local stylesheets and scripts count
$contacts = [ordered]@{}
foreach ($rel in $pages) {
    $full = Join-Path $Root $rel
    $raw = Read-Utf8 $full
    $hosts = [Collections.Generic.HashSet[string]]::new()
    foreach ($h in Get-Contacts $raw $true) { [void]$hosts.Add($h) }
    $dir = Split-Path $full -Parent
    foreach ($m in [regex]::Matches((Strip-Comments $raw $true), '(?is)<(?:link[^>]*\bhref|script[^>]*\bsrc)\s*=\s*["'']([^"'':?#]+\.(?:css|js))(?:[?#][^"'']*)?["'']')) {
        $target = $m.Groups[1].Value
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:)?//') { continue }
        $path = if ($target.StartsWith('/')) { Join-Path $Root $target.TrimStart('/') } else { Join-Path $dir $target }
        if (Test-Path -LiteralPath $path) {
            foreach ($h in Get-Contacts (Read-Utf8 $path) $false) { [void]$hosts.Add($h) }
        }
    }
    foreach ($own in $OwnHosts) { [void]$hosts.Remove($own) }
    $contacts[$rel] = $hosts
}

# --- 3 and 4: both directions ---------------------------------------------------------------------
$byHost = @{}
foreach ($rel in $contacts.Keys) {
    foreach ($h in $contacts[$rel]) {
        if (-not $byHost.ContainsKey($h)) { $byHost[$h] = [Collections.Generic.List[string]]::new() }
        $byHost[$h].Add($rel)
    }
}
foreach ($h in ($byHost.Keys | Sort-Object)) {
    if (-not $declared.Contains($h)) {
        Problem "$h is contacted by $($byHost[$h].Count) page(s) ($($byHost[$h][0])$(if ($byHost[$h].Count -gt 1) { ', ..' })) and is not declared in tools/site-origins.json"
    }
    foreach ($bad in $forbidden) {
        if ($h.Contains($bad)) { Problem "$h is an analytics, tag-manager or advertising origin ('$bad') and is contacted by $($byHost[$h][0])" }
    }
}
foreach ($h in $declared.Keys) {
    $o = $declared[$h]
    $expected = [Collections.Generic.List[string]]::new()
    foreach ($rel in $pages) {
        $in = $false
        foreach ($pat in @($o.pages)) { if ($rel -like $pat) { $in = $true; break } }
        if ($in) { foreach ($pat in @($o.except)) { if ($pat -and $rel -like $pat) { $in = $false; break } } }
        if ($in) { $expected.Add($rel) }
    }
    $actual = if ($byHost.ContainsKey($h)) { @($byHost[$h]) } else { @() }
    if ($actual.Count -eq 0) { Problem "$h is declared and no published page contacts it (a stale declaration)"; continue }
    $missing = @($expected | Where-Object { $actual -notcontains $_ })
    $extra = @($actual | Where-Object { $expected -notcontains $_ })
    if ($missing.Count) { Problem "$h is declared for $($missing.Count) page(s) that do not contact it: $((($missing | Select-Object -First 3) -join ', '))$(if ($missing.Count -gt 3) { ', ..' })" }
    if ($extra.Count) { Problem "$h is contacted by $($extra.Count) page(s) the declaration does not cover: $((($extra | Select-Object -First 3) -join ', '))$(if ($extra.Count -gt 3) { ', ..' })" }
}

# --- 5: the privacy page names each host in each language block -----------------------------------
$privacy = Read-Utf8 (Join-Path $Root $PrivacyPage)
$blocks = [ordered]@{}
foreach ($lang in 'en', 'ru', 'ua') {
    $m = [regex]::Match($privacy, '(?s)<div data-l="' + $lang + '">(.*?)(?=<div data-l="(?:en|ru|ua)">|<footer\b)')
    if (-not $m.Success) { Problem "$PrivacyPage has no '$lang' block"; continue }
    $blocks[$lang] = $m.Groups[1].Value
}
foreach ($lang in $blocks.Keys) {
    foreach ($h in $declared.Keys) {
        if (-not $blocks[$lang].ToLowerInvariant().Contains($h)) { Problem "$PrivacyPage ($lang) does not name $h" }
    }
}
foreach ($m in [regex]::Matches($privacy, '(?i)<(?:link|script|img|iframe)\b[^>]*(?:href|src)\s*=\s*["'']https?://(?!serzhyale\.github\.io)[^"'']+')) {
    Problem "$PrivacyPage itself loads an outside address: $($m.Value)"
}

# --- 6: every locale of the trust copy names each host --------------------------------------------
$trust = Read-Utf8 $TrustCopy | ConvertFrom-Json
$localeCount = 0
foreach ($p in $trust.PSObject.Properties) {
    if ($p.Name.StartsWith('_')) { continue }
    $localeCount++
    $text = [string]$p.Value.origins
    if ([string]::IsNullOrWhiteSpace($text)) { Problem "trust-copy.json: locale '$($p.Name)' has no 'origins' paragraph"; continue }
    foreach ($h in $declared.Keys) {
        if (-not $text.ToLowerInvariant().Contains($h)) { Problem "trust-copy.json: locale '$($p.Name)' origins does not name $h" }
    }
}
if ($localeCount -eq 0) { Problem 'trust-copy.json carries no locale' }

# --- verdict --------------------------------------------------------------------------------------
$summary = "$($pages.Count) pages, $($declared.Count) declared origins ($((@($declared.Keys) -join ', '))), $localeCount trust locales"
if ($problems.Count) {
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
    Exit-Verdict 'Test-SiteOrigins' 1 "$($problems.Count) problem(s); $summary"
}
Exit-Verdict 'Test-SiteOrigins' 0 $summary
