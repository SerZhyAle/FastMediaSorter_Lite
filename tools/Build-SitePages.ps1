<#
.SYNOPSIS
    Renders the per-language first pages of the site - lang/<code>/index.html -
    from tools/site-copy.json, and the install-trust page from tools/trust-copy.json,
    and generates robots.txt, sitemap.xml and search-index.json over the page set.

.DESCRIPTION
    The site is served from the repository ROOT (see .sza-canon.json: site.root ".").
    The root index.html stays the trilingual EN/RU/UK entry page; every UI language
    additionally gets its own URL with its own `lang`/`dir`, a full set of hreflang
    alternates and a 13-language switcher.

    All 12 translated pages live under ONE root folder, `lang/` - GitHub Pages is
    branch-based here (source main:/), so a clean per-language URL needs a real
    directory, and 12 single-file directories in the repository root was noise.
    `lang/` is the only knob: change $LangDir and every URL, hreflang alternate and
    output path follows. The root index.html's shared regions (channels, family) are
    marked and kept in sync by this generator.

    See docs/specifications/030_SPECIFICATION_CONTRACT_SYNC_PRODUCT_WEB_PAGES.md.

    The generated HTML is a render target - edit site-copy.json and re-run, never the
    pages themselves (canon invariant 16).

    The SECOND page this script renders is the install-trust page (INSTALL-TRUST
    contract, ticket 025 section 7): trust.html at the site root for English, and
    lang/<code>/trust.html for the other twelve. Its copy lives in tools/trust-copy.json
    and its language metadata does NOT - dir, endonym, the machine-translation flag and
    the switcher label are read from site-copy.json, so a language is described once.

    The script also renders robots.txt, sitemap.xml and search-index.json. The search
    index covers the same public page set as the sitemap (DOC-EXTERNAL-QUALITY rules 2
    and 5 - ticket 059); a hand-authored root page is read back out of its own HTML,
    the generated pages out of the copy JSON.

.PARAMETER Check
    Render into memory and fail if any page or marked region on disk differs, or if any
    of the 14 contract conformance checks fail. For a pre-flight sweep:
    it proves the committed HTML still matches its source and contracts.

.NOTES
    Exit codes with -Check (CHECK-VERDICT):
      0 - every render target and conformance check passes;
      1 - a render target differs or is missing, or a conformance check fails;
      2 - could not verify: a copy source is missing or unreadable, or rendering failed.
    The last line is the verdict, `Build-SitePages: <WORD> (..)`. Without -Check the
    script writes the pages and a failure is an ordinary error (exit 1).
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    if (-not $Check) { break }
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Build-SitePages' 2 'the check could not run'
}
if ($Check) {
    Write-Subject 'Build-SitePages' ('the trust pages, the per-language index pages, the not-found page, robots.txt, sitemap.xml, ' +
        'search-index.json and index.html marked regions against tools/site-copy.json + tools/trust-copy.json')
}
$Root     = Split-Path $PSScriptRoot -Parent
$CopyFile = Join-Path $PSScriptRoot 'site-copy.json'
$BaseUrl  = 'https://serzhyale.github.io/FastMediaSorter_Lite'
$LangDir  = 'lang'   # the one root folder that holds every translated page
$Releases = 'https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/latest'
$Repo     = 'https://github.com/SerZhyAle/FastMediaSorter_Lite'
$OgImage  = 'assets/social-preview-1280x640.png'
$Favicon  = 'assets/icons/Fast_Media_Sorter.ico'
# The product names (docs/guides/PRODUCT_NAMING.md section 1; src/ProductNames.vb). The name is NOT translated: it is
# written in Latin in all 13 languages, and "&" is part of it (Esc turns it into "&amp;" in HTML).
# $ProductCompact is the compact name: the short title-length form only, NOT a Microsoft Store name.
$ProductFull  = 'Fast Media Sorter & Sharing for Windows'
$ProductCompact = 'Fast Media Sorter & Sharing'

# The pre-paint resolver: PAGE-STYLE section 7, the one text every page carries (SITE-EXPERIENCE rule 7, specification 154).
# It runs BEFORE the first stylesheet link so the theme and the language never flash; the hand-authored pages carry the
# same text and -Check compares them whitespace-folded. sza-theme holds exactly dark|light, sza-lang exactly ru|en|ua.
$ThemeResolver = @'
<script>
(function(){try{
  var t=localStorage.getItem('sza-theme');
  if(t!=='dark'&&t!=='light')t=matchMedia('(prefers-color-scheme: light)').matches?'light':'dark';
  document.documentElement.setAttribute('data-theme',t);
  var l=localStorage.getItem('sza-lang');
  if(l==='uk')l='ua';
  if(l!=='ru'&&l!=='en'&&l!=='ua'){var n=(navigator.language||'en').toLowerCase();l=n.indexOf('ru')==0?'ru':n.indexOf('uk')==0?'ua':'en';}
  document.documentElement.setAttribute('data-lang',l);
}catch(e){}})();
</script>
'@

$TrustFile = Join-Path $PSScriptRoot 'trust-copy.json'

if (-not (Test-Path $CopyFile))  { throw "Missing copy source: $CopyFile" }
if (-not (Test-Path $TrustFile)) { throw "Missing copy source: $TrustFile" }
$copy  = Get-Content $CopyFile  -Raw -Encoding UTF8 | ConvertFrom-Json
$trust = Get-Content $TrustFile -Raw -Encoding UTF8 | ConvertFrom-Json
$codes = $copy.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('_') }

function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    $s.Replace('&','&amp;').Replace('<','&lt;').Replace('>','&gt;').Replace('"','&quot;')
}

# Body text: escaped like Esc, and on a right-to-left page the name is wrapped in <bdi> so it stays one LTR run
# (PRODUCT_NAMING.md rule 3.2) - otherwise the "&" visually swaps sides. Attributes and <title> use Esc, never this.
$script:RtlPage = $false
function Body([string]$s) {
    $h = Esc $s
    if ($script:RtlPage) { $h = [regex]::Replace($h, 'Fast Media Sorter &amp; Sharing(?: for Windows)?|«Sharing»|Media Exchange Server', '<bdi>$0</bdi>') }
    $h
}

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
        'downloadUrl'         = $copy._channels.github.url
        'isAccessibleForFree' = $true
        'offers'              = [ordered]@{ '@type' = 'Offer'; 'price' = '0'; 'priceCurrency' = 'USD' }
        'author'              = [ordered]@{ '@type' = 'Person'; 'name' = $copy._contact.github }
    }
    ($ld | ConvertTo-Json -Depth 5 -Compress)
}

function TrustJsonLd([string]$code, $t) {
    $ld = [ordered]@{
        '@context'    = 'https://schema.org'
        '@type'       = 'WebPage'
        'headline'    = $t.title
        'description' = $t.desc
        'url'         = (TrustUrl $code)
        'inLanguage'  = $code
        'image'       = "$BaseUrl/$OgImage"
        'isPartOf'    = [ordered]@{ '@type' = 'WebSite'; 'name' = $ProductFull; 'url' = "$BaseUrl/" }
        'author'      = [ordered]@{ '@type' = 'Person'; 'name' = $copy._contact.github }
    }
    ($ld | ConvertTo-Json -Depth 5 -Compress)
}

function PageUrl([string]$code) { if ($code -eq 'en') { "$BaseUrl/" } else { "$BaseUrl/$LangDir/$code/" } }
function TrustUrl([string]$code) { if ($code -eq 'en') { "$BaseUrl/trust.html" } else { "$BaseUrl/$LangDir/$code/trust.html" } }

# Where a generated page finds its own assets (stylesheets, icons): relative to its own depth, so a local or staged view
# renders with the tree's CSS and not whatever is deployed (specification 158 F-4). The page's public identity - canonical,
# og:image, hreflang - stays absolute. The root trust page (en) sits at depth 0, every other page at lang/<code>/ (depth 2).
# 404.html is the one page served from any depth, so it keeps the absolute form (RenderNotFound).
function AssetBase([string]$code) { if ($code -eq 'en') { '' } else { '../../' } }

# The copy button's two states in the page's own language (PAGE-STYLE 4.7, 1.4): the idle label and the done word - "Copied" -
# without a check mark. One row per locale of site-copy.json; the Test-SiteQuality locale count catches a locale without one.
$CopyWords = @{
    en = @('Copy', 'Copied');            ru = @('Копировать', 'Скопировано');  uk = @('Копіювати', 'Скопійовано')
    de = @('Kopieren', 'Kopiert');       it = @('Copia', 'Copiato');           es = @('Copiar', 'Copiado')
    fr = @('Copier', 'Copié');           pt = @('Copiar', 'Copiado');          ar = @('نسخ', 'تم النسخ')
    hi = @('कॉपी करें', 'कॉपी हो गया');    bn = @('কপি করুন', 'কপি হয়েছে');       ur = @('کاپی کریں', 'کاپی ہو گیا')
    zh = @('复制', '已复制')
}
# The locale list of a static page: choosing RU, EN or UA writes sza-lang before navigating, a further locale never does
# (PAGE-STYLE 4.2 and 1.6). The landing's own list offers only further locales, so only the generated pages carry this.
$LocaleListScript = @'
  document.querySelectorAll('.lang-pop a[hreflang],.footer-langs a[hreflang]').forEach(function(a){
    var k={ru:'ru',en:'en',uk:'ua'}[a.getAttribute('hreflang')];
    if(k) a.addEventListener('click',function(){try{localStorage.setItem('sza-lang',k);}catch(e){}});
  });
'@

function RenderFamilyGridForLocale([string]$code) {
    $familyTitle = $copy._family.title
    if ($copy._familyTranslations.PSObject.Properties[$code] -and $copy._familyTranslations.$code.title) {
        $familyTitle = $copy._familyTranslations.$code.title
    }

    $cards = ($copy._family.members | ForEach-Object {
        $m = $_
        $name = $m.name
        $url  = $m.url
        $mType = $m.type
        if ($copy._familyTranslations.PSObject.Properties[$code] -and $copy._familyTranslations.$code.types.PSObject.Properties[$name]) {
            $mType = $copy._familyTranslations.$code.types.$name
        }
        "    <a href=`"$url`"><b>$(Esc $name)</b><span>$(Esc $mType)</span></a>"
    }) -join "`n"

    @"
  <h2 class="sec-head sec-head-family">$(Esc $familyTitle)</h2>
  <div class="tools-grid">
$cards
  </div>
"@
}

function RenderIndexChannels {
    @"
    <!-- gen:channels -->
    <div>
      <div class="copybox"><span class="tok">winget</span> install --id $($copy._channels.winget.id)
        <button class="copy" data-copy="$($copy._channels.winget.command)">Copy</button></div>
    </div>

    <div class="dl-row" id="dlRow">
      <a class="btn btn-primary" id="dl-setup" href="$($copy._channels.github.url)">
        <span data-l="en">Install with setup (.exe)</span><span data-l="ru">Установить через setup (.exe)</span><span data-l="ua">Встановити через setup (.exe)</span>
      </a>
      <a class="btn btn-ghost" id="dl-zip" href="$($copy._channels.github.url)">
        <span data-l="en">Portable (.zip)</span><span data-l="ru">Портативная (.zip)</span><span data-l="ua">Портативна (.zip)</span>
      </a>
      <a class="btn btn-ghost btn-sm" href="$($copy._channels.github.url)">
        <span data-l="en">All releases</span><span data-l="ru">Все релизы</span><span data-l="ua">Усі релізи</span>
        <svg class="glyph" data-glyph="action.download" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false"><g transform="translate(12 12) scale(0.917 0.917) translate(-12 -12)"><path d="M19.35,10.04C18.67,6.59 15.64,4 12,4 9.11,4 6.6,5.64 5.35,8.04 2.34,8.36 0,10.91 0,14c0,3.31 2.69,6 6,6h13c2.76,0 5,-2.24 5,-5 0,-2.64 -2.05,-4.78 -4.65,-4.96zM17,13l-5,5 -5,-5h3V9h4v4h3z" fill="currentColor"/></g></svg>
      </a>
      <a class="btn btn-ghost btn-sm" href="$($copy._channels.store.url)" target="_blank" rel="noopener">
        <span>Microsoft Store</span>
      </a>
      <span class="dl-meta" id="dlMeta">
        <span data-l="en">Offline-ready bundles - VLC runtimes and OCR packs included. Windows 10 1607+ / 11; a 32-bit build for Windows 7/8.1 is in the same download.</span>
        <span data-l="ru">Готовы к работе офлайн - библиотеки VLC и пакеты OCR уже внутри. Windows 10 1607+ / 11; 32-битная сборка для Windows 7/8.1 - в том же файле.</span>
        <span data-l="ua">Готові до роботи офлайн - бібліотеки VLC та пакети OCR уже всередині. Windows 10 1607+ / 11; 32-бітна збірка для Windows 7/8.1 - у тому ж файлі.</span>
      </span>
    </div>
    <!-- /gen:channels -->
"@ -replace "`r`n", "`n"
}

function RenderIndexFamily {
    $familyItems = ($copy._family.members | ForEach-Object {
        $m = $_
        $name = $m.name
        $url  = $m.url
        $enType = $m.type
        $ruType = if ($copy._familyTranslations.ru.types.PSObject.Properties[$name]) { $copy._familyTranslations.ru.types.$name } else { $enType }
        $uaType = if ($copy._familyTranslations.uk.types.PSObject.Properties[$name]) { $copy._familyTranslations.uk.types.$name } else { $enType }
        "    <a href=`"$url`"><b>$(Esc $name)</b><span data-l=`"en`">$(Esc $enType)</span><span data-l=`"ru`">$(Esc $ruType)</span><span data-l=`"ua`">$(Esc $uaType)</span></a>"
    }) -join "`n"

    @"
  <!-- gen:family -->
  <h2 class="sec-head sec-head-family">
    <span data-l="en">$($copy._family.title)</span><span data-l="ru">$($copy._familyTranslations.ru.title)</span><span data-l="ua">$($copy._familyTranslations.uk.title)</span>
  </h2>
  <div class="tools-grid">
$familyItems
  </div>
  <!-- /gen:family -->
"@ -replace "`r`n", "`n"
}

function Render([string]$code) {
    $e = $copy.$code
    $A = AssetBase $code
    $rtl = ($e.dir -eq 'rtl')
    $script:RtlPage = $rtl
    $scriptClass = if ($code -in @('ar','ur','hi','bn','zh')) { " class=`"script-$code`"" } else { '' }

    $alts = ($codes | ForEach-Object {
        '<link rel="alternate" hreflang="{0}" href="{1}">' -f $_, (PageUrl $_)
    }) -join "`n"
    $alts += "`n" + ('<link rel="alternate" hreflang="x-default" href="{0}/">' -f $BaseUrl)

    $switcher = ($codes | ForEach-Object {
        $cls = if ($_ -eq $code) { ' class="on"' } else { '' }
        '<a{0} hreflang="{1}" href="{2}">{3}</a>' -f $cls, $_, (PageUrl $_), (Esc $copy.$_.name)
    }) -join "`n    "

    $know = ($e.know | ForEach-Object { "      <li>$(Body $_)</li>" }) -join "`n"

    $scenarioCards = ($e.features[0..2] | ForEach-Object {
        "    <article class=`"card scenario-card`">`n      <h3>$(Esc $_[0])</h3>`n      <p>$(Esc $_[1])</p>`n    </article>"
    }) -join "`n"

    $featureGroupCards = ($e.features[3..5] | ForEach-Object {
        "    <article class=`"card`">`n      <h3>$(Esc $_[0])</h3>`n      <p>$(Esc $_[1])</p>`n    </article>"
    }) -join "`n"

    $allFeatureCards = ($e.features | ForEach-Object {
        "      <article class=`"card`">`n        <h3>$(Esc $_[0])</h3>`n        <p>$(Esc $_[1])</p>`n      </article>"
    }) -join "`n"

    $howtoAndroid = ($copy._howto.items | Where-Object { $_[0] -eq 'publish-folders-android.html' } | ForEach-Object { $_[1] })
    $note = ''
    if ($e.machine) { $note = "      <p class=`"note`">$(Esc $e.machineNote)</p>`n" }

    $howto = ($copy._howto.items | ForEach-Object {
        '      <li><a href="{0}/{1}">{2}</a></li>' -f $BaseUrl, $_[0], (Esc $_[1])
    }) -join "`n"

    # The three doors (ticket 132 section 3.2): viewer, share to Android, and the reserved exchange-server slot.
    # Door 3 is future-tense text only - no link, no download, no capability claim.
    $doorHrefs = @('#get', "$BaseUrl/server.html", '')
    $doorCards = (0..2 | ForEach-Object {
        $d = $e.doors[$_]
        $pillClass = if ($_ -eq 2) { 'pill gold' } else { 'pill' }
        $cta = ''
        if ($d[3]) {
            $btnClass = if ($_ -eq 0) { 'btn btn-primary btn-sm' } else { 'btn btn-ghost btn-sm' }
            $cta = "`n      <div class=`"scenario-foot`"><a class=`"$btnClass`" href=`"$($doorHrefs[$_])`">$(Esc $d[3])</a></div>"
        }
        "    <article class=`"card scenario-card`" id=`"door-$($_ + 1)`">`n      <span class=`"$pillClass`">$(Body $d[0])</span>`n      <h3>$(Body $d[1])</h3>`n      <p>$(Body $d[2])</p>$cta`n    </article>"
    }) -join "`n"

    $familyGrid = RenderFamilyGridForLocale $code

@"
<!DOCTYPE html>
<html lang="$code"$(if ($rtl) { ' dir="rtl"' })$scriptClass>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
$ThemeResolver
<title>$(Esc $e.title)</title>
<meta name="description" content="$(Esc $e.tagline)">
<link rel="canonical" href="$(PageUrl $code)">
$alts
<meta property="og:type" content="website">
<meta property="og:title" content="$(Esc $e.title)">
<meta property="og:description" content="$(Esc $e.tagline)">
<meta property="og:url" content="$(PageUrl $code)">
<meta property="og:image" content="$BaseUrl/$OgImage">
<meta property="og:site_name" content="$(Esc $ProductCompact)">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $e.title)">
<meta name="twitter:description" content="$(Esc $e.tagline)">
<meta name="twitter:image" content="$BaseUrl/$OgImage">
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$A$Favicon" sizes="any">
<link rel="apple-touch-icon" href="${A}assets/icons/store-icon-256.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="${A}assets/sza-kit.css">
<link rel="stylesheet" href="${A}assets/fms-site.css">
<script type="application/ld+json">$(JsonLd $code $e)</script>
</head>
<body>
<a class="skip-link" href="#main">$(Esc $e.skipLabel)</a>
<header class="site-header">
  <nav class="site-nav" aria-label="$(Esc $e.navLabel)">
  <a class="brand" href="$(PageUrl $code)" aria-label="$(Esc $ProductFull)">$(Body $ProductFull)</a>
  <details class="lang-menu">
    <summary>$(Esc $e.name)</summary>
    <div class="lang-pop">
    $switcher
    </div>
  </details>
  <button class="theme-btn" id="themeBtn" aria-label="Switch theme">◐</button>
  <a class="btn btn-primary btn-sm" href="#get">$(Esc $e.dlSetup)</a>
  </nav>
</header>

<main class="container" id="main" tabindex="-1">
  <section class="hero">
    <div class="hero-id">
      <img class="app-icon" src="${A}assets/icons/store-icon-256.png" alt="" width="168" height="168">
      <div class="hero-copy">
        <span class="eyebrow">Windows desktop app</span>
        <h1>$(Body $e.title)</h1>
        <span class="kicker">Free &amp; open-source</span>
      </div>
    </div>
    <p class="lead lead-narrow">$(Body $e.family)</p>
    <p class="lead lead-narrow">$(Body $e.tagline)</p>
    <p class="lead lead-narrow">$(Body $e.sharing) <a href="$BaseUrl/publish-folders-android.html" lang="en" dir="ltr">$(Esc $howtoAndroid)</a></p>
    <div class="button-group">
      <a class="btn btn-primary" href="#get">$(Esc $e.dlSetup)</a>
      <a class="btn btn-ghost" href="#features">$(Esc $e.knowTitle)</a>
    </div>
  </section>

  <section class="section doors" id="doors" aria-labelledby="doors-title">
    <div class="sec-head">
      <h2 class="section-title" id="doors-title"><span class="hash">#</span>$(Esc $e.doorsTitle)</h2>
      <p class="section-desc">$(Body $e.doorsLead)</p>
    </div>
    <div class="grid">
$doorCards
    </div>
  </section>

  <section class="sec-head">
    <h2 class="section-title"><span class="hash">#</span>$(Esc $e.knowTitle)</h2>
    <ul class="know-list">
$know
    </ul>
  </section>

  <section class="scenarios">
    <div class="grid">
$scenarioCards
    </div>
  </section>

  <section class="download-section" id="get">
    <div class="sec-head">
      <h2 class="section-title"><span class="hash">#</span>$(Esc $e.downloadTitle)</h2>
    </div>
    <div class="get">
      <div>
        <div class="copybox"><span class="tok">winget</span> install --id $($copy._channels.winget.id)
          <button class="copy" data-copy="$($copy._channels.winget.command)">$($CopyWords[$code][0])</button></div>
      </div>
      <div class="dl-row" id="dlRow">
        <a class="btn btn-primary" id="dl-setup" href="$($copy._channels.github.url)">$(Esc $e.dlSetup)</a>
        <a class="btn btn-ghost" id="dl-zip" href="$($copy._channels.github.url)">Portable (.zip)</a>
        <a class="btn btn-ghost btn-sm" href="$($copy._channels.github.url)">All releases</a>
        <a class="btn btn-ghost btn-sm" href="$($copy._channels.store.url)" target="_blank" rel="noopener"><span>Microsoft Store</span></a>
        <span class="dl-meta" id="dlMeta">Offline-ready bundles - VLC runtimes and OCR packs included. Windows 10 1607+ / 11; a 32-bit build for Windows 7/8.1 is in the same download.</span>
      </div>
      <p class="dl-meta">$(Body $e.insideNote)</p>
      <p class="dl-meta">
        <a href="$(TrustUrl $code)">$(Esc $trust.$code.title)</a>
      </p>
$note      <p><a href="$BaseUrl/server.html">$(Esc $e.serverLink)</a></p>
    </div>
  </section>

  <section class="section" id="features">
    <div class="sec-head">
      <h2 class="section-title"><span class="hash">#</span>$(Esc $e.knowTitle)</h2>
    </div>
    <div class="grid">
$featureGroupCards
    </div>
    <details class="sec sec-spaced" id="all-features">
      <summary><span class="secnum">01</span>$(Body $e.title) - Features</summary>
      <div class="grid grid-spaced">
$allFeatureCards
      </div>
    </details>
  </section>

  <section class="section" id="howto">
    <div class="sec-head">
      <h2 class="section-title"><span class="hash">#</span>$(Esc $copy._howto.title)</h2>
      <p class="section-desc" lang="en" dir="ltr">$(Esc $copy._howto.note)</p>
    </div>
    <ul lang="en" dir="ltr">
$howto
    </ul>
  </section>

  <section class="tech-links">
    <p><a href="$BaseUrl/docs/privacy.html">Privacy</a> &middot; <a href="$Repo/blob/main/LICENSE">LICENSE</a> &middot; <a href="$Repo">GitHub</a></p>
  </section>
</main>

<footer class="site-footer"><div class="container">
  <h2 class="footer-title" lang="en" dir="ltr">Documentation</h2>
  <div class="link-row" lang="en" dir="ltr">
    <a href="#howto">$(Esc $copy._howto.title)</a>
    <a href="$BaseUrl/topics.html">Topics</a>
    <a href="$BaseUrl/glossary.html">Glossary</a>
    <a href="$(TrustUrl $code)">$(Esc $trust.$code.title)</a>
    <a href="$BaseUrl/docs/privacy.html">Privacy</a>
    <a href="$Repo#readme">README</a>
    <a href="$Repo/blob/main/CHANGELOG.md">Changelog</a>
    <a href="$Repo/issues">Issues</a>
  </div>
$familyGrid
  <div class="footer-langs">
    <span class="footer-langs-label">$(Esc $e.langLabel):</span>
    $switcher
  </div>
  <div class="author-card" lang="en" dir="ltr">
    <b><a href="https://$($copy._contact.website)">Serhii Zhyhunenko (SerZhyAle)</a></b>
    <span>&copy; $(Get-Date -Format 'yyyy')</span>
    <a href="https://$($copy._contact.website)">$($copy._contact.website)</a>
    <a href="https://github.com/$($copy._contact.github)">GitHub</a>
    <a href="$Repo">FastMediaSorter_Lite</a>
    <a href="mailto:$($copy._contact.email)">$($copy._contact.email)</a>
  </div>
</div></footer>

<script>
(function(){
  var doc=document.documentElement;
  function setTheme(t){
    doc.setAttribute('data-theme',t);
    try{localStorage.setItem('sza-theme',t);}catch(e){}
    var m=document.querySelector('meta[name="theme-color"]'); if(m) m.setAttribute('content', t==='light'?'#eef3ea':'#0a0f0a');
    var b=document.getElementById('themeBtn'); if(b) b.setAttribute('aria-label', t==='light'?'Switch to dark theme':'Switch to light theme');
  }
  var tb=document.getElementById('themeBtn');
  if(tb) tb.addEventListener('click',function(){setTheme(doc.getAttribute('data-theme')==='light'?'dark':'light');});
  setTheme(doc.getAttribute('data-theme')||'dark');

$LocaleListScript
  document.querySelectorAll('.copy').forEach(function(btn){
    btn.addEventListener('click',function(){
      var txt=btn.getAttribute('data-copy')||'';
      var done=function(){var o=btn.textContent;btn.textContent='$($CopyWords[$code][1])';btn.classList.add('done');
        setTimeout(function(){btn.textContent=o;btn.classList.remove('done');},1600);};
      if(navigator.clipboard&&navigator.clipboard.writeText){
        navigator.clipboard.writeText(txt).then(done,function(){fallback(txt);done();});
      }else{fallback(txt);done();}
    });
  });
  function fallback(txt){try{var ta=document.createElement('textarea');ta.value=txt;
    ta.style.position='fixed';ta.style.opacity='0';document.body.appendChild(ta);ta.select();
    document.execCommand('copy');document.body.removeChild(ta);}catch(e){}}

  (function(){
    fetch('https://api.github.com/repos/SerZhyAle/FastMediaSorter_Lite/releases/latest')
      .then(function(r){return r.ok?r.json():Promise.reject();})
      .then(function(rel){
        var assets=rel.assets||[];
        function find(re){for(var i=0;i<assets.length;i++){if(re.test(assets[i].name))return assets[i];}return null;}
        var setup=find(/windows-x64-setup\.exe$/);
        var zip=find(/windows-x64\.zip$/);
        var tag=rel.tag_name||'';
        var s=document.getElementById('dl-setup'), z=document.getElementById('dl-zip');
        if(setup&&s) s.href=setup.browser_download_url;
        if(zip&&z) z.href=zip.browser_download_url;
        if(tag){
          var meta=document.getElementById('dlMeta');
          if(meta){var b=document.createElement('span');b.className='meta-strong';
            b.textContent=' '+tag;meta.appendChild(b);}
        }
      })
      .catch(function(){});
  })();
})();
</script>
</body>
</html>
"@
}

function RenderTrust([string]$code) {
    $A = AssetBase $code
    $t   = $trust.$code
    $e   = $copy.$code
    $rtl = ($e.dir -eq 'rtl')
    $script:RtlPage = $rtl
    $scriptClass = if ($code -in @('ar','ur','hi','bn','zh')) { " class=`"script-$code`"" } else { '' }

    $alts = ($codes | ForEach-Object {
        '<link rel="alternate" hreflang="{0}" href="{1}">' -f $_, (TrustUrl $_)
    }) -join "`n"
    $alts += "`n" + ('<link rel="alternate" hreflang="x-default" href="{0}">' -f (TrustUrl 'en'))

    $switcher = ($codes | ForEach-Object {
        $cls = if ($_ -eq $code) { ' class="on"' } else { '' }
        '<a{0} hreflang="{1}" href="{2}">{3}</a>' -f $cls, $_, (TrustUrl $_), (Esc $copy.$_.name)
    }) -join "`n    "

    $sections = ($t.sections | ForEach-Object {
        $items = ($_.items | ForEach-Object { "      <li>$(Body $_)</li>" }) -join "`n"
        "  <section class=`"section`">`n    <h2>$(Body $_.h)</h2>`n    <p class=`"lead`">$(Body $_.p)</p>`n    <ul>`n$items`n    </ul>`n  </section>"
    }) -join "`n"

    $mt = ''
    if ($e.machine) { $mt = "  <p class=`"note`">$(Esc $e.machineNote)</p>`n" }

    $familyGrid = RenderFamilyGridForLocale $code

@"
<!DOCTYPE html>
<html lang="$code"$(if ($rtl) { ' dir="rtl"' })$scriptClass>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
$ThemeResolver
<title>$(Esc $t.title)</title>
<meta name="description" content="$(Esc $t.desc)">
<link rel="canonical" href="$(TrustUrl $code)">
$alts
<meta property="og:type" content="article">
<meta property="og:title" content="$(Esc $t.title)">
<meta property="og:description" content="$(Esc $t.desc)">
<meta property="og:url" content="$(TrustUrl $code)">
<meta property="og:image" content="$BaseUrl/$OgImage">
<meta property="og:site_name" content="$(Esc $ProductCompact)">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $t.title)">
<meta name="twitter:description" content="$(Esc $t.desc)">
<meta name="twitter:image" content="$BaseUrl/$OgImage">
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$A$Favicon" sizes="any">
<link rel="apple-touch-icon" href="${A}assets/icons/store-icon-256.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="${A}assets/sza-kit.css">
<link rel="stylesheet" href="${A}assets/fms-site.css">
<script type="application/ld+json">$(TrustJsonLd $code $t)</script>
</head>
<body>
<a class="skip-link" href="#main">$(Esc $e.skipLabel)</a>
<header class="site-header">
  <nav class="site-nav" aria-label="$(Esc $e.navLabel)">
  <a class="brand" href="$(PageUrl $code)" aria-label="$(Esc $ProductFull)"><span class="brand-mark"><img src="$A$Favicon" alt="" width="24" height="24"></span>$(Body $ProductFull)</a>
  <button class="theme-btn" id="themeBtn" aria-label="Switch theme">◐</button>
  <a class="btn btn-ghost btn-sm" href="$(PageUrl $code)">$(Esc $t.backLabel)</a>
  </nav>
</header>

<main class="container" id="main" tabindex="-1">
  <section class="hero">
    <h1>$(Body $t.title)</h1>
    <p class="lead">$(Body $t.intro)</p>
  </section>

$sections

  <p class="note">$(Body $t.note)</p>
  <p class="note" id="site-origins">$(Body $t.origins)</p>

  <div class="button-group button-group-spaced">
    <a class="btn btn-primary" href="$($copy._channels.github.url)">$(Esc $t.dlLabel)</a>
    <a class="btn btn-ghost" href="$BaseUrl/docs/privacy.html">$(Esc $t.privacyLabel)</a>
    <a class="btn btn-ghost" href="$(PageUrl $code)">$(Esc $t.backLabel)</a>
  </div>
$mt</main>

<footer class="site-footer"><div class="container">
$familyGrid
  <div class="footer-langs">
    <span class="footer-langs-label">$(Esc $e.langLabel):</span>
    $switcher
  </div>
  <div class="footer-bottom">
    <span>&copy; $(Get-Date -Format 'yyyy') <a href="https://$($copy._contact.website)">$($copy._contact.github)</a></span>
    <span><a href="https://$($copy._contact.website)">$($copy._contact.website)</a> &middot; <a href="$Repo">GitHub</a> &middot; <a href="mailto:$($copy._contact.email)">$($copy._contact.email)</a></span>
  </div>
</div></footer>

<script>
(function(){
  var doc=document.documentElement;
  function setTheme(t){
    doc.setAttribute('data-theme',t);
    try{localStorage.setItem('sza-theme',t);}catch(e){}
    var m=document.querySelector('meta[name="theme-color"]'); if(m) m.setAttribute('content', t==='light'?'#eef3ea':'#0a0f0a');
    var b=document.getElementById('themeBtn'); if(b) b.setAttribute('aria-label', t==='light'?'Switch to dark theme':'Switch to light theme');
  }
  var tb=document.getElementById('themeBtn');
  if(tb) tb.addEventListener('click',function(){setTheme(doc.getAttribute('data-theme')==='light'?'dark':'light');});
  setTheme(doc.getAttribute('data-theme')||'dark');

$LocaleListScript
})();
</script>
</body>
</html>
"@
}

# The not-found page (SITE-STRUCTURE rule 15, ticket 149). GitHub Pages serves a root 404.html for ANY unknown address under
# the site, at whatever depth - so every reference is absolute from $BaseUrl, never relative. It is noindex, it is not in
# the sitemap or the search index, and it carries the core three (en, ru, ua) in-page like the root pages do. The site has
# no search at the guide tier, so the subject index (topics.html) is the way to find a page; the guides come from _howto.
function RenderNotFound {
    $nf = $copy._notFound
    $script:RtlPage = $false
    $guides = ($copy._howto.items | ForEach-Object {
        '      <a href="{0}/{1}" lang="en" dir="ltr"><b>{2}</b></a>' -f $BaseUrl, $_[0], (Esc $_[1])
    }) -join "`n"
    $langs = 'en', 'ru', 'ua'
    function Spans([string]$key) { ($langs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (Esc $nf.$_.$key) }) -join '' }
    # Inside <script> the text is not entity-decoded, so the "&" of the name goes in raw.
    $metaTitles = ($langs | ForEach-Object { '{0}:"{1}"' -f $_, ('{0} | {1}' -f $nf.$_.short, $ProductCompact) }) -join ','
@"
<!DOCTYPE html>
<html lang="en" data-theme="dark" data-lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
$ThemeResolver
<title>$(Esc ('{0} | {1}' -f $nf.en.short, $ProductCompact))</title>
<meta name="description" content="$(Esc $nf.metaDesc)">
<meta name="robots" content="noindex">
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$BaseUrl/$Favicon" sizes="any">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="$BaseUrl/assets/sza-kit.css">
<link rel="stylesheet" href="$BaseUrl/assets/fms-site.css">
<style>
html[data-lang="ru"] [data-l]:not([data-l="ru"]),html[data-lang="en"] [data-l]:not([data-l="en"]),html[data-lang="ua"] [data-l]:not([data-l="ua"]){display:none}
.site-header>.brand{margin-right:auto}.hero{text-align:left;padding:1.5rem 0 .8rem}.hero .lead{margin:0}
.nf-group{margin-top:1.4rem}.nf-group h2{font-size:1.15rem;margin:0 0 .7rem}.nf-group>p{margin:0 0 .7rem;color:var(--text-2)}
.cards{display:grid;gap:.8rem}@media(min-width:860px){.cards{grid-template-columns:1fr 1fr}}
.cards a{display:block;background:var(--card);border:1px solid var(--border);border-radius:var(--radius);padding:.9rem 1.1rem;text-decoration:none}
.cards a b{display:block;color:var(--text);font-family:var(--font-heading);margin-bottom:.2rem}.cards a span{color:var(--text-2);font-size:.95rem}
</style>
</head>
<body>
<a class="skip-link" href="#main">$(Spans 'skip')</a>
<div class="bg-blobs" aria-hidden="true"><div class="blob blob-1"></div><div class="blob blob-2"></div><div class="blob blob-3"></div></div>
<header class="site-header">
  <span id="site-nav-label" class="sr-only">$(Spans 'nav')</span>
  <nav class="site-nav" aria-labelledby="site-nav-label">
  <a class="brand" href="$BaseUrl/" aria-label="$(Esc $ProductFull)"><span class="brand-mark"><img src="$BaseUrl/$Favicon" alt="" width="24" height="24"></span>$(Esc $ProductFull)</a>
  <div class="seg" role="group" aria-label="Language">
    <button data-setlang="ru">RU</button><button data-setlang="en" aria-pressed="true">EN</button><button data-setlang="ua">UA</button>
  </div>
  <button class="theme-btn" id="themeBtn" aria-label="Switch theme">◐</button>
  <a class="btn btn-primary btn-sm" href="$BaseUrl/#get">$(Spans 'download')</a>
  </nav>
</header>

<main class="container" id="main" tabindex="-1">
  <section class="hero">
    <span class="kicker">404</span>
    <h1>$(Spans 'h1')</h1>
    <p class="lead">$(Spans 'lead')</p>
  </section>

  <section class="nf-group">
    <div class="cards">
      <a href="$BaseUrl/"><b>$(Spans 'landingTitle')</b>$(Spans 'landingText')</a>
      <a href="$BaseUrl/topics.html"><b>$(Spans 'topicsTitle')</b>$(Spans 'topicsText')</a>
    </div>
  </section>

  <section class="nf-group">
    <h2>$(Spans 'guidesTitle')</h2>
    <p>$(Spans 'guidesNote')</p>
    <div class="cards">
$guides
    </div>
  </section>
</main>

<footer class="site-footer"><div class="container"><div class="footer-bottom">
  <span><a href="$BaseUrl/">$(Esc $ProductFull)</a> &middot; <a href="$BaseUrl/topics.html">$(Spans 'topicsTitle')</a> &middot; <a href="$BaseUrl/docs/privacy.html">Privacy</a></span>
  <span>&copy; $(Get-Date -Format 'yyyy') <a href="https://$($copy._contact.website)">$($copy._contact.github)</a></span>
</div></div></footer>

<script>
(function(){
  var doc=document.documentElement;
  var TITLES={$metaTitles};
  function setLang(l){
    if(!TITLES[l]) l='en';
    doc.setAttribute('data-lang',l);
    doc.setAttribute('lang', l==='ua'?'uk':l);
    try{localStorage.setItem('sza-lang',l);}catch(e){}
    document.querySelectorAll('.seg [data-setlang]').forEach(function(b){
      b.setAttribute('aria-pressed', b.getAttribute('data-setlang')===l ? 'true':'false');
    });
    document.title=TITLES[l];
  }
  document.querySelectorAll('.seg [data-setlang]').forEach(function(b){
    b.addEventListener('click',function(){setLang(b.getAttribute('data-setlang'));});
  });
  setLang(doc.getAttribute('data-lang')||'en');
  var tb=document.getElementById('themeBtn');
  if(tb) tb.addEventListener('click',function(){
    var v=doc.getAttribute('data-theme')==='light'?'dark':'light';
    doc.setAttribute('data-theme',v);
    try{localStorage.setItem('sza-theme',v);}catch(e){}
  });
})();
</script>
</body>
</html>
"@
}

$written = @(); $stale = @(); $compared = 0

# 1. Update/check root index.html marked regions (gen:channels, gen:family)
$indexFile = Join-Path $Root 'index.html'
if (Test-Path $indexFile) {
    $indexContent = Get-Content $indexFile -Raw -Encoding UTF8
    $expectedChannels = (RenderIndexChannels).Trim()
    $expectedFamily   = (RenderIndexFamily).Trim()

    $chanMatch = [regex]::Match($indexContent, '(?s)<!-- gen:channels -->.*?<!-- /gen:channels -->')
    $famMatch  = [regex]::Match($indexContent, '(?s)<!-- gen:family -->.*?<!-- /gen:family -->')

    if ($Check) {
        $compared++
        if (-not $chanMatch.Success -or $chanMatch.Value.Trim() -ne $expectedChannels) {
            $stale += "index.html (gen:channels)"
        }
        if (-not $famMatch.Success -or $famMatch.Value.Trim() -ne $expectedFamily) {
            $stale += "index.html (gen:family)"
        }
    } else {
        $newIndex = $indexContent
        if ($chanMatch.Success) {
            $newIndex = $newIndex.Substring(0, $chanMatch.Index) + $expectedChannels + $newIndex.Substring($chanMatch.Index + $chanMatch.Length)
        }
        $famMatchAfter = [regex]::Match($newIndex, '(?s)<!-- gen:family -->.*?<!-- /gen:family -->')
        if ($famMatchAfter.Success) {
            $newIndex = $newIndex.Substring(0, $famMatchAfter.Index) + $expectedFamily + $newIndex.Substring($famMatchAfter.Index + $famMatchAfter.Length)
        }
        if ($newIndex -ne $indexContent) {
            [IO.File]::WriteAllText($indexFile, ($newIndex -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))
            $written += 'index.html (marked regions)'
        }
    }
}

# 2. Render/check trust pages
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

# 3. Render/check per-language index pages
foreach ($code in $codes) {
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

# 4. Render/check the not-found page (404.html, SITE-STRUCTURE rule 15)
$notFoundPath = Join-Path $Root '404.html'
$notFoundHtml = (RenderNotFound) -replace "`r`n", "`n"
if ($Check) {
    $compared++
    if (-not (Test-Path $notFoundPath)) { $stale += '404.html (missing)' }
    elseif ((Get-Content $notFoundPath -Raw -Encoding UTF8) -replace "`r`n", "`n" -ne $notFoundHtml) { $stale += '404.html' }
} else {
    [IO.File]::WriteAllText($notFoundPath, $notFoundHtml, (New-Object Text.UTF8Encoding $false))
    $written += '404.html'
}

# --- robots.txt + sitemap.xml -------------------------------------------------
# A redirect stub keeps a retired address alive: noindex AND an immediate meta refresh. It is not a page of the set.
function Test-RedirectStub([string]$path) {
    $h = [IO.File]::ReadAllText($path)
    return ($h -match '<meta name="robots" content="noindex">') -and ($h -match '(?i)<meta http-equiv="refresh"')
}

function RootPages {
    # 404.html is served by the host for unknown addresses; it is noindex and never a page of the set. The same holds for a
    # redirect stub: a retired address kept alive for old links (noindex + meta refresh), which is not content and is not listed.
    Get-ChildItem -LiteralPath $Root -Filter '*.html' -File |
        Where-Object { $_.Name -ne '404.html' -and -not (Test-RedirectStub $_.FullName) } |
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
    $urls = @(SiteEntries | ForEach-Object { $_.url })

    $body = ($urls | ForEach-Object {
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

function SiteEntries {
    $entries = @()
    foreach ($f in (RootPages)) {
        $url = if ($f -eq 'index.html') { "$BaseUrl/" } else { "$BaseUrl/$f" }
        $html = Get-Content (Join-Path $Root $f) -Raw -Encoding UTF8
        $title = if ($html -match '(?s)<title>(.*?)</title>') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
        $desc  = if ($html -match '(?s)<meta\s+name="description"\s+content="(.*?)"') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
        if (-not $title -or -not $desc) { throw "root page $f carries no title or description for the search index" }
        $entries += [pscustomobject]@{ url = $url; lang = 'en'; title = $title; description = $desc }
    }
    # The privacy policy is a required page type (SITE-STRUCTURE section 2) and lives under docs/, so it is not a root page;
    # it carries the same title/description SEO block and is listed explicitly (specification 151, T-3).
    $pv = Get-Content (Join-Path $Root 'docs/privacy.html') -Raw -Encoding UTF8
    $pvTitle = if ($pv -match '(?s)<title>(.*?)</title>') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
    $pvDesc  = if ($pv -match '(?s)<meta\s+name="description"\s+content="(.*?)"') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
    if (-not $pvTitle -or -not $pvDesc) { throw 'docs/privacy.html carries no title or description for the search index' }
    $entries += [pscustomobject]@{ url = "$BaseUrl/docs/privacy.html"; lang = 'en'; title = $pvTitle; description = $pvDesc }
    foreach ($code in ($codes | Where-Object { $_ -ne 'en' })) {
        $entries += [pscustomobject]@{ url = (PageUrl $code); lang = $code; title = $copy.$code.title; description = $copy.$code.tagline }
        $entries += [pscustomobject]@{ url = (TrustUrl $code); lang = $code; title = $trust.$code.title; description = $trust.$code.desc }
    }
    return $entries
}

function RenderSearchIndex {
    $entries = @(SiteEntries)
    ((@($entries | ForEach-Object {
        [ordered]@{ 'url' = $_.url; 'lang' = $_.lang; 'title' = $_.title; 'description' = $_.description }
    } | ConvertTo-Json -Depth 3) -join "`n") -replace "`r`n", "`n") + "`n"
}

foreach ($pair in @(@('robots.txt', (RenderRobots)), @('sitemap.xml', (RenderSitemap)), @('search-index.json', (RenderSearchIndex)))) {
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
    $orphans = @($codes | Where-Object { $_ -ne 'en' -and (Test-Path (Join-Path $Root "$_/index.html")) })
    if ($compared -eq 0) { Exit-Verdict 'Build-SitePages' 2 'no render target was compared' }
    $problems = @()
    if ($stale.Count -gt 0) {
        Write-Host "Render targets out of date with their copy source: $($stale -join ', ')"
        Write-Host "  Run .\tools\Build-SitePages.ps1 to regenerate."
        $problems += "$($stale.Count) of $compared render target(s) stale"
    }
    if ($orphans.Count -gt 0) {
        Write-Host "Legacy per-language folders left in the repository root: $($orphans -join ', ')"
        Write-Host "  Delete them - the pages live under $LangDir/ now."
        $problems += "$($orphans.Count) legacy folder(s)"
    }

    # --- 12 Contract Conformance Checks (ticket 030 section 5, DOC-EXTERNAL-QUALITY), then 13-14 (specification 154) ---
    # 1. Kit identity - the catalog's reference/sza-kit.css as re-vendored 2026-10-07 (the PAGE-STYLE 1.3 revision:
    # 14521 bytes; the 1.4/1.5 revision the catalog still owes is NOT in it - re-pin when that one lands).
    # Hashed with CRLF folded to LF: the catalog file is LF, and a text=auto checkout with
    # core.autocrlf turns the working copy CRLF without changing a single character of the kit.
    # assets/sza-kit.css is also marked -text in .gitattributes (PAGE-STYLE section 0 step 1), so a new clone is LF.
    $pinnedKitSha = 'AEA958F805249D300B7417373E4CAD18B44D7EED9765954DF19ED710276C4C9E'
    $kitFile = Join-Path $Root 'assets/sza-kit.css'
    if (-not (Test-Path $kitFile)) {
        $problems += "Kit identity: missing assets/sza-kit.css"
    } else {
        $kitSha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $kitText = [System.IO.File]::ReadAllText($kitFile, (New-Object System.Text.UTF8Encoding $false)) -replace "`r`n", "`n"
            $hash = ([BitConverter]::ToString($kitSha.ComputeHash((New-Object System.Text.UTF8Encoding $false).GetBytes($kitText)))).Replace('-', '')
            if ($hash -ne $pinnedKitSha) {
                $problems += "Kit identity: assets/sza-kit.css hash $hash does not match pinned reference $pinnedKitSha"
            }
        } finally { $kitSha.Dispose() }
    }

    # 2. Switcher
    foreach ($h in @('index.html', 'publish-folders-android.html')) {
        $hp = Join-Path $Root $h
        if (Test-Path $hp) {
            $hc = Get-Content $hp -Raw -Encoding UTF8
            if ($hc -match '>UK<') { $problems += "Switcher: $h contains '>UK<'" }
            if ($hc -match "localStorage\.setItem\(['`"]sza-lang['`"],\s*['`"]uk['`"]\)") { $problems += "Switcher: $h persists 'uk'" }
        }
    }

    # 3. Family grid URLs vs pinned list
    $pinnedFamilyUrls = @(
        'https://serzhyale.github.io/FastMediaSorter_mob_v2/',
        'https://serzhyale.github.io/CyrFlip/',
        'https://serzhyale.github.io/doc-html-translate/',
        'https://serzhyale.github.io/FileDO/',
        'https://serzhyale.github.io/StreamsPlayer/',
        'https://serzhyale.github.io/OneClickRunner/',
        'https://serzhyale.github.io/universal-agent-kit/',
        'https://sza.od.ua/'
    )
    $actualFamilyUrls = @($copy._family.members | ForEach-Object { $_.url })
    $diffFamily = Compare-Object $pinnedFamilyUrls $actualFamilyUrls
    if ($diffFamily) {
        $problems += "Family grid: _family URLs in site-copy.json do not match pinned SITE-FAMILY-MAP URLs"
    }

    # 4. Contact
    $allServedFiles = @(Get-ChildItem -LiteralPath $Root -Filter '*.html' -File | ForEach-Object { $_.FullName })
    if (Test-Path (Join-Path $Root 'lang')) {
        $allServedFiles += @(Get-ChildItem -LiteralPath (Join-Path $Root 'lang') -Filter '*.html' -Recurse -File | ForEach-Object { $_.FullName })
    }
    if (Test-Path (Join-Path $Root 'docs')) {
        $allServedFiles += @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs') -Filter '*.html' -Recurse -File | ForEach-Object { $_.FullName })
    }
    $readmeFiles = @(Get-ChildItem -LiteralPath $Root -Filter 'README*.md' -File | ForEach-Object { $_.FullName })

    foreach ($f in ($allServedFiles + $readmeFiles)) {
        $fc = Get-Content $f -Raw -Encoding UTF8
        $mailMatches = [regex]::Matches($fc, 'mailto:([^\s"''<>)]+)')
        foreach ($m in $mailMatches) {
            $addr = $m.Groups[1].Value
            if ($addr -ne 'sza@ukr.net') {
                $rel = Resolve-Path -Relative $f
                $problems += "Contact: $rel has mailto:$addr (must be sza@ukr.net)"
            }
        }
    }

    # 5. No emoji
    foreach ($f in $allServedFiles) {
        $fc = Get-Content $f -Raw -Encoding UTF8
        foreach ($ch in $fc.ToCharArray()) {
            $cp = [int]$ch
            if (($cp -ge 0x2600 -and $cp -le 0x27BF -and $cp -ne 0x2713) -or ($cp -ge 0x1F300 -and $cp -le 0x1FAFF)) {
                $rel = Resolve-Path -Relative $f
                $problems += "No emoji: $rel contains forbidden symbol U+$($cp.ToString('X4'))"
                break
            }
        }
    }

    # 6. No hardcoded version
    foreach ($f in $allServedFiles) {
        $fc = Get-Content $f -Raw -Encoding UTF8
        if ($fc -match 'releases/download/v' -or $fc -match 'releases/tag/v') {
            $rel = Resolve-Path -Relative $f
            $problems += "No hardcoded version: $rel contains hardcoded release URL"
        }
    }

    # 7. No leftover palette
    foreach ($f in $allServedFiles) {
        $fc = Get-Content $f -Raw -Encoding UTF8
        if ($fc -match '#7aa2ff') {
            $rel = Resolve-Path -Relative $f
            $problems += "No leftover palette: $rel contains legacy #7aa2ff"
        }
    }

    # 8. Order: H1 -> whatfor -> scenarios -> #get
    $orderFiles = @(Join-Path $Root 'index.html') + @($codes | Where-Object { $_ -ne 'en' } | ForEach-Object { Join-Path (Join-Path (Join-Path $Root $LangDir) $_) 'index.html' })
    foreach ($f in $orderFiles) {
        if (Test-Path $f) {
            $fc = Get-Content $f -Raw -Encoding UTF8
            $body = $fc.IndexOf('<body')
            if ($body -eq -1) { $body = 0 }
            $posH1 = $fc.IndexOf('<h1', $body)
            $posWhat = [Math]::Max($fc.IndexOf('class="lead', $body), $fc.IndexOf('whatfor', $body))
            if ($posWhat -eq -1) { $posWhat = $fc.IndexOf('sec-head', $body) }
            $posScenarios = $fc.IndexOf('scenarios', $body)
            if ($posScenarios -eq -1) { $posScenarios = $fc.IndexOf('scenario-card', $body) }
            $posGet = $fc.IndexOf('id="get"', $body)
            if ($posH1 -ge 0 -and $posWhat -ge 0 -and $posScenarios -ge 0 -and $posGet -ge 0) {
                if (-not ($posH1 -lt $posWhat -and $posWhat -lt $posScenarios -and $posScenarios -lt $posGet)) {
                    $rel = Resolve-Path -Relative $f
                    $problems += "Order: $rel element order violation"
                }
            }
        }
    }

    # 9-10. The shared social assets every page names exist (DOC-EXTERNAL-QUALITY): a
    # renamed or deleted file would otherwise ship a dead preview or favicon on every page.
    if (-not (Test-Path (Join-Path $Root ($OgImage -replace '/', '\')))) { $problems += "Social assets: the og:image asset $OgImage does not exist" }
    if (-not (Test-Path (Join-Path $Root ($Favicon -replace '/', '\')))) { $problems += "Social assets: the favicon $Favicon does not exist" }

    # 11. Every page the HOW TO block links is on disk.
    foreach ($item in $copy._howto.items) {
        if (-not (Test-Path (Join-Path $Root ($item[0] -replace '/', '\')))) {
            $problems += "HOW TO block: links $($item[0]), which is not on disk"
        }
    }

    # 12. The not-found page (SITE-STRUCTURE rule 15): it exists, is noindex and outside the sitemap, offers the landing,
    # the subject index and every guide, carries the core three (en, ru, ua), and builds EVERY address from the base URL -
    # the host serves it at any depth, so one relative reference would break on a nested unknown address.
    if (-not (Test-Path $notFoundPath)) {
        $problems += '404.html: the not-found page does not exist'
    } else {
        $nfDisk = Get-Content $notFoundPath -Raw -Encoding UTF8
        if ($nfDisk -notmatch '<meta name="robots" content="noindex">') { $problems += '404.html: not marked noindex' }
        $nfWant = @("$BaseUrl/", "$BaseUrl/topics.html") + @($copy._howto.items | ForEach-Object { "$BaseUrl/$($_[0])" })
        foreach ($u in $nfWant) { if ($nfDisk -notmatch ('href="' + [regex]::Escape($u) + '"')) { $problems += "404.html: does not offer $u" } }
        foreach ($l in 'en', 'ru', 'ua') { if ($nfDisk -notmatch ('data-l="' + $l + '"')) { $problems += "404.html: no '$l' block of the core three" } }
        # A fragment-only address ("#main", the skip link) resolves against the page itself at any depth, so it is exempt.
        $nfRelative = @([regex]::Matches($nfDisk, '(?i)\b(?:href|src)="(?!https://|mailto:|#)([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
        if ($nfRelative.Count -gt 0) { $problems += "404.html: address(es) not built from the base URL: $($nfRelative -join ', ')" }
        foreach ($listing in 'sitemap.xml', 'search-index.json') {
            $lp = Join-Path $Root $listing
            if ((Test-Path $lp) -and ((Get-Content $lp -Raw -Encoding UTF8) -match '404\.html')) { $problems += "404.html: listed in $listing" }
        }
    }

    # 13-14. The served pages' presentation (SITE-EXPERIENCE rules 4 and 7, specification 154). The set is the root pages and
    # lang/ - the product site; docs/ is the older redesign copy and is not part of it.
    $sitePages = @(Get-ChildItem -LiteralPath $Root -Filter '*.html' -File | ForEach-Object { $_.FullName })
    if (Test-Path (Join-Path $Root 'lang')) {
        $sitePages += @(Get-ChildItem -LiteralPath (Join-Path $Root 'lang') -Filter '*.html' -Recurse -File | ForEach-Object { $_.FullName })
    }
    $fold = { param([string]$s) ($s -replace '\s+', ' ').Trim() }
    $canon = & $fold $ThemeResolver
    $layoutProps = '(?i)(^|;)\s*(color|background[a-z-]*|margin[a-z-]*|padding[a-z-]*|display|flex[a-z-]*|gap|grid[a-z-]*|width|height|min-[a-z]+|max-[a-z]+|font[a-z-]*|border[a-z-]*|position|top|left|right|bottom|align[a-z-]*|justify[a-z-]*|text-[a-z]+|float|overflow[a-z-]*|opacity|line-height|letter-spacing|z-index|transform|visibility)\s*:'
    foreach ($f in $sitePages) {
        $rel = $f.Substring($Root.Length).TrimStart('\', '/')
        $fc = Get-Content $f -Raw -Encoding UTF8
        # 13. A layout or colour value is the stylesheet's, never a style attribute. Scripts and comments are not markup:
        # a string a script builds at run time is the one allowed inline presentation.
        $markup = [regex]::Replace([regex]::Replace($fc, '(?s)<script\b.*?</script>', ''), '(?s)<!--.*?-->', '')
        $inline = @([regex]::Matches($markup, '(?i)\sstyle\s*=\s*("([^"]*)"|''([^'']*)'')') | Where-Object {
            $v = if ($_.Groups[2].Success) { $_.Groups[2].Value } else { $_.Groups[3].Value }
            $v -match $layoutProps })
        if ($inline.Count -gt 0) { $problems += "Inline style: $rel carries $($inline.Count) style attribute(s) with layout or colour (move them to assets/fms-site.css)" }
        # 14. The pre-paint resolver is the one text and sits before the first stylesheet link.
        $css = [regex]::Match($fc, '(?i)<link\b[^>]*\brel\s*=\s*["'']stylesheet["''][^>]*>')
        $res = [regex]::Match($fc, '(?s)<script>\s*\(function\(\)\{try\{\s*var t=localStorage\.getItem\(''sza-theme''\).*?</script>')
        if (-not $res.Success) {
            $problems += "Theme resolver: $rel has no pre-paint resolver"
        } else {
            if ((& $fold $res.Value) -ne $canon) { $problems += "Theme resolver: $rel carries a resolver that is not the PAGE-STYLE section 7 text" }
            if ($css.Success -and $res.Index -gt $css.Index) { $problems += "Theme resolver: $rel places it after the first stylesheet link" }
        }
    }
    if ($problems.Count -gt 0) {
        Write-Host "Conformance check problems found:"
        $problems | ForEach-Object { Write-Host "  - $_" }
        Exit-Verdict 'Build-SitePages' 1 ($problems -join '; ')
    }

    Exit-Verdict 'Build-SitePages' 0 "$compared render targets and 14 conformance checks passed"
}

Write-Host "Wrote $($written.Count) pages:"
$written | ForEach-Object { Write-Host "  $_" }
