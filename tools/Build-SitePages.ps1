<#
.SYNOPSIS
    Renders the per-language first pages of the site - lang/<code>/index.html -
    from tools/site-copy.json, and the install-trust page from tools/trust-copy.json.

.DESCRIPTION
    The site is served from the repository ROOT (see .sza-canon.json: site.root ".").
    The root index.html stays the trilingual EN/RU/UK entry page; every UI language
    additionally gets its own URL with its own `lang`/`dir`, a full set of hreflang
    alternates and a 13-language switcher.

    All 12 translated pages live under ONE root folder, `lang/` - GitHub Pages is
    branch-based here (source main:/), so a clean per-language URL needs a real
    directory, and 12 single-file directories in the repository root was noise.
    `lang/` is the only knob: change $LangDir and every URL, hreflang alternate and
    output path follows. The root index.html's own switcher is hand-authored and
    must be kept in step by hand.

    See docs/specifications/013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block B.

    The generated HTML is a render target - edit site-copy.json and re-run, never the
    pages themselves (canon invariant 16).

    The SECOND page this script renders is the install-trust page (INSTALL-TRUST
    contract, ticket 025 section 7): trust.html at the site root for English, and
    lang/<code>/trust.html for the other twelve. Its copy lives in tools/trust-copy.json
    and its language metadata does NOT - dir, endonym, the machine-translation flag and
    the switcher label are read from site-copy.json, so a language is described once.
    It is rendered here rather than hand-authored like the how-to pages precisely because
    the contract requires it in all thirteen locales in one edit (canon invariant 17).

.PARAMETER Check
    Render into memory and fail if any page on disk differs. For a pre-flight sweep:
    it proves the committed HTML still matches its source. tools\Release.ps1 runs it
    before every tag (tools\check-placement.jsonl).

.NOTES
    Exit codes with -Check (CHECK-VERDICT):
      0 - every render target on disk matches its source;
      1 - a render target differs or is missing, or a legacy per-language folder is left
          in the repository root;
      2 - could not verify: a copy source is missing or unreadable, or rendering failed.
    The last line is the verdict, `Build-SitePages: <WORD> (..)`. Without -Check the
    script writes the pages and a failure is an ordinary error (exit 1).
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

# In check mode an unexpected terminating error - an unreadable copy file, a render that
# throws - means nothing was compared, which is not the same fact as "a page is stale".
trap {
    if (-not $Check) { break }
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Build-SitePages' 2 'the check could not run'
}
if ($Check) {
    Write-Subject 'Build-SitePages' ('the trust pages, the per-language index pages, robots.txt and sitemap.xml ' +
        'against tools/site-copy.json + tools/trust-copy.json')
}
$Root     = Split-Path $PSScriptRoot -Parent
$CopyFile = Join-Path $PSScriptRoot 'site-copy.json'
$BaseUrl  = 'https://serzhyale.github.io/FastMediaSorter_Lite'
$LangDir  = 'lang'   # the one root folder that holds every translated page
$Releases = 'https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/latest'
$Repo     = 'https://github.com/SerZhyAle/FastMediaSorter_Lite'
$OgImage  = 'assets/social-preview-1280x640.png'          # verified present; assets/og-image.png never was
$Favicon  = 'assets/icons/Fast_Media_Sorter.ico'          # ditto for assets/favicon.ico

$TrustFile = Join-Path $PSScriptRoot 'trust-copy.json'

if (-not (Test-Path $CopyFile))  { throw "Missing copy source: $CopyFile" }
if (-not (Test-Path $TrustFile)) { throw "Missing copy source: $TrustFile" }
$copy  = Get-Content $CopyFile  -Raw -Encoding UTF8 | ConvertFrom-Json
$trust = Get-Content $TrustFile -Raw -Encoding UTF8 | ConvertFrom-Json
# Anything starting with '_' is shared copy, not a language: '_comment', '_howto'.
$codes = $copy.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('_') }

function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    $s.Replace('&','&amp;').Replace('<','&lt;').Replace('>','&gt;').Replace('"','&quot;')
}

# Structured data. Built through ConvertTo-Json so the localized strings are escaped as JSON
# rather than as HTML - Esc() is the wrong tool inside a script block and would emit &quot;.
function JsonLd([string]$code, $e) {
    $ld = [ordered]@{
        '@context'            = 'https://schema.org'
        '@type'               = 'SoftwareApplication'
        'name'                = $e.title
        'description'         = $e.tagline
        'url'                 = (PageUrl $code)
        'inLanguage'          = $code
        'applicationCategory' = 'MultimediaApplication'
        'operatingSystem'     = 'Windows 7, Windows 8.1, Windows 10, Windows 11'
        'image'               = "$BaseUrl/$OgImage"
        'downloadUrl'         = $Releases
        'isAccessibleForFree' = $true
        'offers'              = [ordered]@{ '@type' = 'Offer'; 'price' = '0'; 'priceCurrency' = 'USD' }
        'author'              = [ordered]@{ '@type' = 'Person'; 'name' = 'SerZhyAle' }
    }
    # No softwareVersion: a version baked into a page goes stale on the next release (SZA-VER04).
    ($ld | ConvertTo-Json -Depth 5 -Compress)
}

# Absolute paths from the site root: a relative href breaks one directory down.
function PageUrl([string]$code) { if ($code -eq 'en') { "$BaseUrl/" } else { "$BaseUrl/$LangDir/$code/" } }
function TrustUrl([string]$code) { if ($code -eq 'en') { "$BaseUrl/trust.html" } else { "$BaseUrl/$LangDir/$code/trust.html" } }

function Render([string]$code) {
    $e = $copy.$code
    $rtl = ($e.dir -eq 'rtl')

    $alts = ($codes | ForEach-Object {
        '<link rel="alternate" hreflang="{0}" href="{1}">' -f $_, (PageUrl $_)
    }) -join "`n"
    $alts += "`n" + ('<link rel="alternate" hreflang="x-default" href="{0}/">' -f $BaseUrl)

    $switcher = ($codes | ForEach-Object {
        $cls = if ($_ -eq $code) { ' class="on"' } else { '' }
        '<a{0} hreflang="{1}" href="{2}">{3}</a>' -f $cls, $_, (PageUrl $_), (Esc $copy.$_.name)
    }) -join "`n      "

    $cards = ($e.features | ForEach-Object {
        "      <article class=`"card`">`n        <h3>$(Esc $_[0])</h3>`n        <p>$(Esc $_[1])</p>`n      </article>"
    }) -join "`n"

    $know = ($e.know | ForEach-Object { "        <li>$(Esc $_)</li>" }) -join "`n"

    $note = ''
    if ($e.machine) { $note = "    <p class=`"note`">$(Esc $e.machineNote)</p>`n" }

    # The HOW TO block is the same English copy on every page - see '_howto' in
    # site-copy.json for why it is not translated.
    $howto = ($copy._howto.items | ForEach-Object {
        '      <li><a href="{0}/{1}">{2}</a></li>' -f $BaseUrl, $_[0], (Esc $_[1])
    }) -join "`n"

@"
<!DOCTYPE html>
<html lang="$code"$(if ($rtl) { ' dir="rtl"' })>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$(Esc $e.title)</title>
<meta name="description" content="$(Esc $e.tagline)">
<link rel="canonical" href="$(PageUrl $code)">
$alts
<meta property="og:type" content="website">
<meta property="og:title" content="$(Esc $e.title)">
<meta property="og:description" content="$(Esc $e.tagline)">
<meta property="og:url" content="$(PageUrl $code)">
<meta property="og:image" content="$BaseUrl/$OgImage">
<meta property="og:site_name" content="Fast Media Sorter for Windows">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $e.title)">
<meta name="twitter:description" content="$(Esc $e.tagline)">
<meta name="twitter:image" content="$BaseUrl/$OgImage">
<meta name="theme-color" content="#0d1017">
<link rel="icon" href="$BaseUrl/$Favicon" sizes="any">
<script type="application/ld+json">$(JsonLd $code $e)</script>
<style>
  :root{--bg:#0d1017;--bg2:#171b26;--fg:#e8ecf4;--muted:#aab2be;--accent:#7aa2ff;--border:rgba(255,255,255,.10)}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--fg);
       font:16px/1.6 "Segoe UI",system-ui,-apple-system,"Noto Sans","Noto Sans Arabic","Noto Sans Devanagari","Noto Sans Bengali","Microsoft YaHei",sans-serif}
  .wrap{max-width:980px;margin:0 auto;padding:0 20px}
  header{padding:56px 0 8px}
  h1{font-size:clamp(28px,5vw,44px);line-height:1.15;margin:0 0 14px}
  .tagline{font-size:clamp(16px,2.2vw,20px);color:var(--muted);margin:0 0 28px;max-width:62ch}
  h2{font-size:20px;margin:40px 0 14px}
  .grid{display:grid;gap:14px;grid-template-columns:repeat(auto-fit,minmax(260px,1fr))}
  .card{background:var(--bg2);border:1px solid var(--border);border-radius:14px;padding:18px 20px}
  .card h3{margin:0 0 8px;font-size:17px}
  .card p{margin:0;color:var(--muted);font-size:15px}
  ul{padding-inline-start:22px;color:var(--muted)}
  li{margin:6px 0}
  .btns{display:flex;flex-wrap:wrap;gap:10px;margin:14px 0 6px}
  .btn{display:inline-block;padding:11px 18px;border-radius:10px;text-decoration:none;
       border:1px solid var(--border);color:var(--fg);background:var(--bg2)}
  .btn.primary{background:var(--accent);color:#0d1017;border-color:transparent;font-weight:600}
  .note{margin:22px 0 0;padding:12px 16px;border-inline-start:3px solid var(--accent);
        background:rgba(122,162,255,.07);border-radius:0 10px 10px 0;color:var(--muted);font-size:14px}
  .langs{display:flex;flex-wrap:wrap;gap:8px 14px;margin:10px 0 0;font-size:14px}
  .langs a{color:var(--muted);text-decoration:none}
  .langs a:hover{color:var(--fg)}
  .langs a.on{color:var(--accent);font-weight:600}
  footer{margin:56px 0 40px;padding-top:20px;border-top:1px solid var(--border);color:var(--muted);font-size:14px}
  footer a{color:var(--muted)}
  a{color:var(--accent)}
</style>
</head>
<body>
<div class="wrap">
  <header>
    <h1>$(Esc $e.title)</h1>
    <p class="tagline">$(Esc $e.tagline)</p>
  </header>

  <section>
    <h2>$(Esc $e.knowTitle)</h2>
    <ul>
$know
    </ul>
  </section>

  <section>
    <div class="grid">
$cards
    </div>
  </section>

  <section id="get">
    <h2>$(Esc $e.downloadTitle)</h2>
    <div class="btns">
      <a class="btn primary" href="$Releases">$(Esc $e.dlSetup)</a>
      <a class="btn" href="$Repo#winget">$(Esc $e.dlWinget)</a>
      <a class="btn" href="https://apps.microsoft.com/search?query=FastMediaSorter">$(Esc $e.dlStore)</a>
    </div>
    <p><a href="$BaseUrl/how-to-viewer.html">$(Esc $e.guide)</a></p>
    <p><a href="$BaseUrl/server.html">$(Esc $e.serverLink)</a></p>
    <p><a href="$(TrustUrl $code)">$(Esc $trust.$code.title)</a></p>
$note  </section>

  <section id="howto">
    <h2>$(Esc $copy._howto.title)</h2>
    <p class="tagline" lang="en" dir="ltr">$(Esc $copy._howto.note)</p>
    <ul lang="en" dir="ltr">
$howto
    </ul>
  </section>

  <footer>
    <p>$(Esc $e.langLabel):</p>
    <nav class="langs">
      $switcher
    </nav>
    <p><a href="$Repo">GitHub</a> &middot; <a href="$BaseUrl/docs/privacy.html">Privacy</a></p>
  </footer>
</div>
</body>
</html>
"@
}

function RenderTrust([string]$code) {
    $t   = $trust.$code
    $e   = $copy.$code          # language metadata only - one description per language
    $rtl = ($e.dir -eq 'rtl')

    $alts = ($codes | ForEach-Object {
        '<link rel="alternate" hreflang="{0}" href="{1}">' -f $_, (TrustUrl $_)
    }) -join "`n"
    $alts += "`n" + ('<link rel="alternate" hreflang="x-default" href="{0}">' -f (TrustUrl 'en'))

    $switcher = ($codes | ForEach-Object {
        $cls = if ($_ -eq $code) { ' class="on"' } else { '' }
        '<a{0} hreflang="{1}" href="{2}">{3}</a>' -f $cls, $_, (TrustUrl $_), (Esc $copy.$_.name)
    }) -join "`n      "

    # The four sections INSTALL-TRUST rule 1 fixes, plus rule 6's list, in the order the
    # contract states. The order is the JSON's; nothing here reorders it.
    $sections = ($t.sections | ForEach-Object {
        $items = ($_.items | ForEach-Object { "        <li>$(Esc $_)</li>" }) -join "`n"
        "  <section>`n    <h2>$(Esc $_.h)</h2>`n    <p class=`"tagline`">$(Esc $_.p)</p>`n    <ul>`n$items`n    </ul>`n  </section>"
    }) -join "`n"

    $mt = ''
    if ($e.machine) { $mt = "    <p class=`"note`">$(Esc $e.machineNote)</p>`n" }

@"
<!DOCTYPE html>
<html lang="$code"$(if ($rtl) { ' dir="rtl"' })>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$(Esc $t.title)</title>
<meta name="description" content="$(Esc $t.desc)">
<link rel="canonical" href="$(TrustUrl $code)">
$alts
<meta property="og:type" content="article">
<meta property="og:title" content="$(Esc $t.title)">
<meta property="og:description" content="$(Esc $t.desc)">
<meta property="og:url" content="$(TrustUrl $code)">
<meta property="og:image" content="$BaseUrl/$OgImage">
<meta property="og:site_name" content="Fast Media Sorter for Windows">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $t.title)">
<meta name="twitter:description" content="$(Esc $t.desc)">
<meta name="twitter:image" content="$BaseUrl/$OgImage">
<meta name="theme-color" content="#0d1017">
<link rel="icon" href="$BaseUrl/$Favicon" sizes="any">
<style>
  :root{--bg:#0d1017;--bg2:#171b26;--fg:#e8ecf4;--muted:#aab2be;--accent:#7aa2ff;--border:rgba(255,255,255,.10)}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--fg);
       font:16px/1.6 "Segoe UI",system-ui,-apple-system,"Noto Sans","Noto Sans Arabic","Noto Sans Devanagari","Noto Sans Bengali","Microsoft YaHei",sans-serif}
  .wrap{max-width:820px;margin:0 auto;padding:0 20px}
  header{padding:56px 0 8px}
  h1{font-size:clamp(26px,4.4vw,38px);line-height:1.2;margin:0 0 14px}
  .tagline{font-size:16px;color:var(--muted);margin:0 0 14px;max-width:70ch}
  h2{font-size:20px;margin:40px 0 10px}
  ul{padding-inline-start:22px;color:var(--muted)}
  li{margin:10px 0}
  .note{margin:28px 0 0;padding:12px 16px;border-inline-start:3px solid var(--accent);
        background:rgba(122,162,255,.07);border-radius:0 10px 10px 0;color:var(--muted);font-size:14px}
  .btns{display:flex;flex-wrap:wrap;gap:10px;margin:28px 0 6px}
  .btn{display:inline-block;padding:11px 18px;border-radius:10px;text-decoration:none;
       border:1px solid var(--border);color:var(--fg);background:var(--bg2)}
  .langs{display:flex;flex-wrap:wrap;gap:8px 14px;margin:10px 0 0;font-size:14px}
  .langs a{color:var(--muted);text-decoration:none}
  .langs a:hover{color:var(--fg)}
  .langs a.on{color:var(--accent);font-weight:600}
  footer{margin:56px 0 40px;padding-top:20px;border-top:1px solid var(--border);color:var(--muted);font-size:14px}
  footer a{color:var(--muted)}
  a{color:var(--accent)}
</style>
</head>
<body>
<div class="wrap">
  <header>
    <h1>$(Esc $t.title)</h1>
    <p class="tagline">$(Esc $t.intro)</p>
  </header>

$sections

  <p class="note">$(Esc $t.note)</p>

  <div class="btns">
    <a class="btn" href="$(PageUrl $code)">$(Esc $t.backLabel)</a>
    <a class="btn" href="$Releases">$(Esc $t.dlLabel)</a>
    <a class="btn" href="$BaseUrl/docs/privacy.html">$(Esc $t.privacyLabel)</a>
  </div>
$mt
  <footer>
    <p>$(Esc $e.langLabel):</p>
    <nav class="langs">
      $switcher
    </nav>
    <p><a href="$Repo">GitHub</a></p>
  </footer>
</div>
</body>
</html>
"@
}

$written = @(); $stale = @(); $compared = 0

# The install-trust page, English at the site root and the other twelve beside their
# index. Written BEFORE the sitemap block below, which globs the root for *.html.
foreach ($code in $codes) {
    $path = if ($code -eq 'en') { Join-Path $Root 'trust.html' }
            else { Join-Path (Join-Path (Join-Path $Root $LangDir) $code) 'trust.html' }
    $html = (RenderTrust $code) -replace "`r`n", "`n"

    if ($Check) {
        $compared++
        if (-not (Test-Path $path)) { $stale += "trust:$code (missing)"; continue }
        $on = (Get-Content $path -Raw -Encoding UTF8) -replace "`r`n", "`n"
        if ($on -ne $html) { $stale += "trust:$code" }
        continue
    }

    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($path, $html, (New-Object Text.UTF8Encoding $false))
    $written += (Resolve-Path -Relative $path)
}

foreach ($code in $codes) {
    # English is the root entry page, which is hand-authored and trilingual.
    if ($code -eq 'en') { continue }

    $dir  = Join-Path (Join-Path $Root $LangDir) $code
    $path = Join-Path $dir 'index.html'
    $html = (Render $code) -replace "`r`n", "`n"

    if ($Check) {
        $compared++
        if (-not (Test-Path $path)) { $stale += "$code (missing)" ; continue }
        $on = (Get-Content $path -Raw -Encoding UTF8) -replace "`r`n", "`n"
        if ($on -ne $html) { $stale += $code }
        continue
    }

    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    [IO.File]::WriteAllText($path, $html, (New-Object Text.UTF8Encoding $false))
    $written += "$LangDir/$code/index.html"
}

# --- robots.txt + sitemap.xml -------------------------------------------------
# Both are render targets, not hand-maintained files: the public set is the root
# .html pages plus one directory per translated language, and a hand-written
# sitemap goes stale the first time either set changes. The root page is listed
# as the bare directory URL, which is what canonical says, so a crawler is never
# offered index.html and / as two things.
function RootPages {
    Get-ChildItem -LiteralPath $Root -Filter '*.html' -File |
        Sort-Object Name | ForEach-Object { $_.Name }
}

function RenderRobots {
    @"
User-agent: *
Allow: /

Sitemap: $BaseUrl/sitemap.xml

"@ -replace "`r`n", "`n"
}

function RenderSitemap {
    $urls = @()
    foreach ($f in RootPages) {
        $urls += if ($f -eq 'index.html') { "$BaseUrl/" } else { "$BaseUrl/$f" }
    }
    foreach ($code in ($codes | Where-Object { $_ -ne 'en' })) {
        $urls += (PageUrl $code)
        # The trust page exists in every language, so its twelve translated URLs need
        # listing too; the English one is already picked up by the root *.html glob.
        $urls += (TrustUrl $code)
    }

    $body = ($urls | ForEach-Object {
        # The entry page carries the hreflang set, so it is the one that declares the
        # alternates; a per-page alternate block on a how-to that has no translation
        # would be a lie.
        if ($_ -eq "$BaseUrl/") {
            $alts = (($codes | ForEach-Object {
                '    <xhtml:link rel="alternate" hreflang="{0}" href="{1}"/>' -f $_, (PageUrl $_)
            }) -join "`n")
            "  <url>`n    <loc>$_</loc>`n$alts`n    <xhtml:link rel=`"alternate`" hreflang=`"x-default`" href=`"$BaseUrl/`"/>`n  </url>"
        } else {
            "  <url>`n    <loc>$_</loc>`n  </url>"
        }
    }) -join "`n"

    @"
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"
        xmlns:xhtml="http://www.w3.org/1999/xhtml">
$body
</urlset>
"@ -replace "`r`n", "`n"
}

foreach ($pair in @(@('robots.txt', (RenderRobots)), @('sitemap.xml', (RenderSitemap)))) {
    $name = $pair[0]; $text = $pair[1]
    $dest = Join-Path $Root $name
    if ($Check) {
        $compared++
        if (-not (Test-Path $dest)) { $stale += "$name (missing)"; continue }
        $on = (Get-Content $dest -Raw -Encoding UTF8) -replace "`r`n", "`n"
        if ($on -ne $text) { $stale += $name }
        continue
    }
    [IO.File]::WriteAllText($dest, $text, (New-Object Text.UTF8Encoding $false))
    $written += $name
}
if ($Check) {
    # The pages used to live one per folder in the repository root. A leftover there
    # would still be served by Pages and would silently outrank the real page.
    $orphans = @($codes | Where-Object { $_ -ne 'en' -and (Test-Path (Join-Path $Root "$_/index.html")) })
    if ($compared -eq 0) { Exit-Verdict 'Build-SitePages' 2 'no render target was compared' }
    # Both findings are reported together: stopping at the first turns one fix into two runs.
    $problems = @()
    if ($orphans.Count -gt 0) {
        Write-Host "Legacy per-language folders left in the repository root: $($orphans -join ', ')"
        Write-Host "  Delete them - the pages live under $LangDir/ now."
        $problems += "$($orphans.Count) legacy folder(s)"
    }
    if ($stale.Count -gt 0) {
        Write-Host "Render targets out of date with their copy source: $($stale -join ', ')"
        Write-Host "  Run .\tools\Build-SitePages.ps1 to regenerate."
        $problems += "$($stale.Count) of $compared render target(s) stale"
    }
    if ($problems.Count -gt 0) { Exit-Verdict 'Build-SitePages' 1 ($problems -join ', ') }
    Exit-Verdict 'Build-SitePages' 0 "$compared render targets match tools/site-copy.json + tools/trust-copy.json"
}

Write-Host "Wrote $($written.Count) pages:"
$written | ForEach-Object { Write-Host "  $_" }
