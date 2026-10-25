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
    the generated pages out of the copy JSON. The sitemap's <lastmod> is the commit
    date of each page's file (a file that differs from HEAD changed today), never the
    build time.

    Ticket 207 (the free-promotion campaign, T-4 and T-5) added: the page title of
    every locale page (a job word, then the compact name - `pageTitle` in
    site-copy.json), the Open Graph image set with its size and a text alternative on
    every generated page (the card is made by tools/New-SocialCard.ps1 from the
    `discoverability` block of tools/site-facts.json), and two generated root pages,
    support.html and whats-new.html (the latter rendered from the released sections
    of CHANGELOG.md).

.PARAMETER Check
    Render into memory and fail if any page or marked region on disk differs, or if any
    of the 17 contract conformance checks fail. For a pre-flight sweep:
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
param([switch]$Check, [switch]$UseGitHistory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    if (-not $Check) { break }
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Build-SitePages' 2 'the check could not run'
}
if ($Check) {
    Write-Subject 'Build-SitePages' ('the trust pages, the per-language index pages, the not-found page, the Support and What''s new pages, ' +
        'robots.txt, sitemap.xml, search-index.json and the index.html / server.html marked regions against tools/site-copy.json + ' +
        'tools/trust-copy.json + CHANGELOG.md, and the social card')
}
$Root     = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib/ExchangeMark.ps1')
$exchangeTerms = Get-ExchangeMarkTerms $Root
$CopyFile = Join-Path $PSScriptRoot 'site-copy.json'
$BaseUrl  = 'https://serzhyale.github.io/FastMediaSorter_Lite'
$LangDir  = 'lang'   # the one root folder that holds every translated page
$Releases = 'https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/latest'
$Repo     = 'https://github.com/SerZhyAle/FastMediaSorter_Lite'
$Favicon  = 'assets/icons/Fast_Media_Sorter.ico'
# The discoverability fields of the site are typed once, in the `discoverability` block of tools/site-facts.json (the
# machine form of docs/guides/POSITIONING.md section 7, ticket 207): the social card's file and size, the title
# separator and suffix. The Open Graph image of EVERY page is the 1200x630 rendering of the one card made by
# tools/New-SocialCard.ps1; the 1280x640 one is the repository's own preview and no page names it.
$FactsFile = Join-Path $PSScriptRoot 'site-facts.json'
if (-not (Test-Path $FactsFile)) { throw "Missing data file: $FactsFile" }
$facts = Get-Content $FactsFile -Raw -Encoding UTF8 | ConvertFrom-Json
$disc  = $facts.discoverability
$ogSize = @($disc.socialCard.sizes | Where-Object { $_.name -eq 'open-graph' })[0]
$OgImage  = [string]$ogSize.file
$OgWidth  = [int]$ogSize.width
$OgHeight = [int]$ogSize.height
$TitleSeparator = [string]$disc.titles.separator
$TitleSuffix    = [string]$disc.titles.suffix
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
(function(){
  var t;try{t=localStorage.getItem('sza-theme');}catch(e){}
  if(t!=='dark'&&t!=='light')t=matchMedia('(prefers-color-scheme: light)').matches?'light':'dark';
  document.documentElement.setAttribute('data-theme',t);
  var l;try{l=localStorage.getItem('sza-lang');}catch(e){}
  var q=new URLSearchParams(location.search),requested=q.has('lang')?q.get('lang'):q.get('l');
  if(requested){requested=requested.toLowerCase();if(requested==='uk')requested='ua';if(requested==='en'||requested==='ru'||requested==='ua')l=requested;}
  if(l==='uk')l='ua';
  if(l!=='ru'&&l!=='en'&&l!=='ua'){var n=(navigator.language||'en').toLowerCase();l=n.indexOf('ru')==0?'ru':n.indexOf('uk')==0?'ua':'en';}
  document.documentElement.setAttribute('data-lang',l);
  document.addEventListener('click',function(e){var a=e.target.closest&&e.target.closest('a[href]');if(!a)return;try{var u=new URL(a.href,location.href);if(u.hostname==='serzhyale.github.io'||u.hostname==='sza.od.ua'){u.searchParams.delete('l');u.searchParams.set('lang',document.documentElement.getAttribute('data-lang')==='ua'?'uk':document.documentElement.getAttribute('data-lang'));a.href=u.href;}}catch(ignore){}});
})();
</script>
'@
$ThemeResolver = $ThemeResolver -replace "`r`n", "`n"

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

# The theme control (PAGE-STYLE 4.3, BUTTONS 2.0 section 2 rules 4 and 5). A toggle with a stable label exposes its pressed
# state, so the label names the state that is "on" - the dark theme - and aria-pressed says whether it is. The name is visible
# text, one string per language in `themeLabel` of tools/site-copy.json; the glyph beside it is the ICON-SET meaning app.theme
# and is decoration only (aria-hidden). Every page's script keeps aria-pressed in step with data-theme.
$ThemeGlyph = '<svg class="glyph" data-glyph="app.theme" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false"><path d="M12 22c5.52 0 10-4.48 10-10S17.52 2 12 2 2 6.48 2 12s4.48 10 10 10zm1-17.93c3.94.49 7 3.85 7 7.93s-3.05 7.44-7 7.93V4.07z" fill="currentColor"/></svg>'
# The two download commands (BUTTONS 2.0: a substantive command keeps its semantic glyph; the navigation link "All releases" does not).
$DownloadGlyph = '<svg class="glyph" data-glyph="action.download" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false"><g transform="translate(12 12) scale(0.917 0.917) translate(-12 -12)"><path d="M19.35,10.04C18.67,6.59 15.64,4 12,4 9.11,4 6.6,5.64 5.35,8.04 2.34,8.36 0,10.91 0,14c0,3.31 2.69,6 6,6h13c2.76,0 5,-2.24 5,-5 0,-2.64 -2.05,-4.78 -4.65,-4.96zM17,13l-5,5 -5,-5h3V9h4v4h3z" fill="currentColor"/></g></svg>'
function ThemeButton([string]$labelHtml) {
    "<button class=`"theme-btn`" id=`"themeBtn`" type=`"button`" aria-pressed=`"true`">$ThemeGlyph<span>$labelHtml</span></button>"
}
# A copy key of site-copy.json in the three in-page languages (en, ru, ua - the copy block of ua is `uk`), as data-l spans.
function SiteCopySpans([string]$key, [string]$wrapOpen = '', [string]$wrapClose = '') {
    (('en', 'ru', 'ua') | ForEach-Object {
        $k = if ($_ -eq 'ua') { 'uk' } else { $_ }
        '<span data-l="{0}">{1}{2}{3}</span>' -f $_, $wrapOpen, (Esc $copy.$k.$key), $wrapClose
    }) -join ''
}
# The accessible name of an external link (BUTTONS 2.0 section 2 rule 3): the visible marker is drawn by assets/fms-buttons.css
# on every a[target="_blank"], so the words that say the link opens elsewhere are the screen-reader half of it. `externalLabel`
# is one string per language in tools/site-copy.json.
function ExtName([string]$text) { '<span class="sr-only"> (' + (Esc $text) + ')</span>' }
function ExtNameSpans { '<span class="sr-only">' + (SiteCopySpans 'externalLabel' ' (' ')') + '</span>' }
# The page's <title>, its og:title and its twitter:title: the job in the page's language, then the compact name, which is
# the short title-length form (PRODUCT_NAMING section 1). The H1 and the structured data keep the full name.
function PageTitle($e) { "$($e.pageTitle)$TitleSeparator$TitleSuffix" }

# The Open Graph and Twitter image sets of canon PROMOTION section 3: the 1200x630 card, its declared size and a text
# alternative in the page's language - a crawler or a screen reader gets what the picture says.
function OgImageMeta([string]$alt) {
    @(
        "<meta property=`"og:image`" content=`"$BaseUrl/$OgImage`">",
        "<meta property=`"og:image:width`" content=`"$OgWidth`">",
        "<meta property=`"og:image:height`" content=`"$OgHeight`">",
        "<meta property=`"og:image:alt`" content=`"$(Esc $alt)`">"
    ) -join "`n"
}
function TwitterImageMeta([string]$alt) {
    @(
        "<meta name=`"twitter:image`" content=`"$BaseUrl/$OgImage`">",
        "<meta name=`"twitter:image:alt`" content=`"$(Esc $alt)`">"
    ) -join "`n"
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
        $DownloadGlyph<span data-l="en">Install with setup (.exe)</span><span data-l="ru">Установить через setup (.exe)</span><span data-l="ua">Встановити через setup (.exe)</span>
      </a>
      <a class="btn btn-ghost" id="dl-zip" href="$($copy._channels.github.url)">
        $DownloadGlyph<span data-l="en">Portable (.zip)</span><span data-l="ru">Портативная (.zip)</span><span data-l="ua">Портативна (.zip)</span>
      </a>
      <a class="btn btn-ghost" href="$($copy._channels.github.url)">
        <span data-l="en">All releases</span><span data-l="ru">Все релизы</span><span data-l="ua">Усі релізи</span>
      </a>
      <a class="btn btn-ghost" href="$($copy._channels.store.url)" target="_blank" rel="noopener noreferrer">
        <span>Microsoft Store</span>$(ExtNameSpans)
      </a>
      <a class="btn btn-ghost" href="server.html">
        <span data-l="en">$(Esc $copy.en.serverLink)</span><span data-l="ru">$(Esc $copy.ru.serverLink)</span><span data-l="ua">$(Esc $copy.uk.serverLink)</span>
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

    $languageOrder = @('ru', 'en', 'uk') + @($codes | Where-Object { $_ -notin @('ru', 'en', 'uk') })
    $switcher = ($languageOrder | ForEach-Object {
        $cls = if ($_ -eq $code) { ' class="on" aria-current="true"' } else { '' }
        $persist = if ($_ -in @('ru', 'en', 'uk')) { ' onclick="try{{localStorage.setItem(''sza-lang'',''{0}'')}}catch(e){{}}"' -f $(if ($_ -eq 'uk') { 'ua' } else { $_ }) } else { '' }
        '<a{0} hreflang="{1}" href="{2}" lang="{1}" title="{3}"{4}>{5}</a>' -f $cls, $_, (PageUrl $_), (Esc $copy.$_.name), $persist, $(if ($_ -eq 'uk') { 'UA' } else { $_.ToUpperInvariant() })
    }) -join "`n    "
    $primaryLanguages = ($languageOrder[0..2] | ForEach-Object {
        $key = if ($_ -eq 'uk') { 'ua' } else { $_ }
        $cur = if ($_ -eq $code) { ' aria-current="true"' } else { '' }
        '<a hreflang="{0}" href="{1}"{4} onclick="try{{localStorage.setItem(''sza-lang'',''{2}'')}}catch(e){{}}">{3}</a>' -f $_, (PageUrl $_), $key, $key.ToUpperInvariant(), $cur
    }) -join ''

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
    # The shared media-exchange portal lives on another origin (the Android site), so it is one extra entry after the
    # repository's own guides, opening in a new tab - not an item of the list, which is checked on disk.
    $portal = $copy._howto.portal
    $portalFooter = ''
    if ($portal) {
        $howto += "`n" + ('      <li><a href="{0}" target="_blank" rel="noopener noreferrer">{1}{2}</a></li>' -f $portal.url, (Esc $portal.label), (ExtName $e.externalLabel))
        $portalFooter = "`n" + ('    <a href="{0}" target="_blank" rel="noopener noreferrer">{1}{2}</a>' -f $portal.url, (Esc $portal.footer), (ExtName $e.externalLabel))
    }

    # The three doors (ticket 132 section 3.2): viewer, share to Android, and the reserved exchange-server slot.
    # Door 3 is future-tense text only - no link, no download, no capability claim.
    $doorHrefs = @('#get', "$BaseUrl/server.html", '')
    $doorCards = (0..2 | ForEach-Object {
        $d = $e.doors[$_]
        $exchangeImage = ''
        if (($_ + 1) -eq $copy._exchangeMark.door) {
            $alt = Esc $exchangeTerms[$code]
            $exchangeImage = "<span class=`"exchange-picture`"><img class=`"exchange-mark`" src=`"../../$($copy._exchangeMark.asset)`" width=`"32`" height=`"32`" alt=`"$alt`"></span> "
        }
        $pillClass = if ($_ -eq 2) { 'pill gold' } else { 'pill' }
        $cta = ''
        if ($d[3]) {
            $btnClass = 'btn btn-ghost'
            $cta = "`n      <div class=`"scenario-foot`"><a class=`"$btnClass`" href=`"$($doorHrefs[$_])`">$(Esc $d[3])</a></div>"
        }
        "    <article class=`"card scenario-card`" id=`"door-$($_ + 1)`">`n      <span class=`"$pillClass`">$(Body $d[0])</span>`n      <h3>$exchangeImage$(Body $d[1])</h3>`n      <p>$(Body $d[2])</p>$cta`n    </article>"
    }) -join "`n"

    $familyGrid = RenderFamilyGridForLocale $code

@"
<!DOCTYPE html>
<html lang="$code"$(if ($rtl) { ' dir="rtl"' })$scriptClass>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
$ThemeResolver
<title>$(Esc (PageTitle $e))</title>
<meta name="description" content="$(Esc $e.tagline)">
<link rel="canonical" href="$(PageUrl $code)">
$alts
<meta property="og:type" content="website">
<meta property="og:title" content="$(Esc (PageTitle $e))">
<meta property="og:description" content="$(Esc $e.tagline)">
<meta property="og:url" content="$(PageUrl $code)">
$(OgImageMeta $e.ogAlt)
<meta property="og:site_name" content="$(Esc $ProductCompact)">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc (PageTitle $e))">
<meta name="twitter:description" content="$(Esc $e.tagline)">
$(TwitterImageMeta $e.ogAlt)
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$A$Favicon" sizes="any">
<link rel="apple-touch-icon" href="${A}assets/icons/store-icon-256.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="${A}assets/sza-kit.css">
<link rel="stylesheet" href="${A}assets/fms-site.css">
<link rel="stylesheet" href="${A}assets/fms-buttons.css">
<script type="application/ld+json">$(JsonLd $code $e)</script>
</head>
<body>
<a class="skip-link" href="#main">$(Esc $e.skipLabel)</a>
<header class="site-header">
  <nav class="site-nav" aria-label="$(Esc $e.navLabel)">
  <a class="brand" href="$(PageUrl $code)" aria-label="$(Esc $ProductFull)">$(Body $ProductFull)</a>
  <a class="nav-doc" href="$BaseUrl/topics.html">$(Esc $e.docsLink)</a>
  <div class="seg" role="group" aria-label="$(Esc $e.langLabel)">$primaryLanguages</div>
  $(ThemeButton (Esc $e.themeLabel))
  <a class="btn btn-ghost" href="#get">$(Esc $e.dlSetup)</a>
  </nav>
</header>
<nav class="container language-row" aria-label="$(Esc $e.langLabel)">$switcher</nav>

<main class="container" id="main" tabindex="-1">
  <section class="hero">
    <div class="identity">
      <img class="product-icon" src="${A}assets/icons/store-icon-256.png" alt="" width="104" height="104">
      <div>
        <p class="product-name" lang="en" dir="ltr">$(Body $ProductFull)</p>
        <p class="kicker" lang="en" dir="ltr">Windows desktop app</p>
        <p class="kicker" lang="en" dir="ltr">Free &amp; open-source</p>
      </div>
    </div>
    <h1>$(Body $e.outcome)</h1>
    <p class="lead lead-narrow">$(Body $e.family)</p>
    <p class="lead lead-narrow">$(Body $e.tagline)</p>
    <p class="lead lead-narrow">$(Body $e.sharing) <a href="$BaseUrl/publish-folders-android.html" lang="en" dir="ltr">$(Esc $howtoAndroid)</a></p>
    <div class="button-group">
      <!-- PAGE-CONTENT "Copy rules": stable action labels - Install (the get-it block below) and Guides
           (the topics index). The artifact names (Installer (GitHub), winget, Store) stay in the #get
           channels, where naming each way to get the product is the rule. -->
      <a class="btn btn-primary" href="#get">$(Esc $e.installAction)</a>
      <a class="btn btn-ghost" href="$BaseUrl/topics.html">$(Esc $e.guidesAction)</a>
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
        <a class="btn btn-primary" id="dl-setup" href="$($copy._channels.github.url)">$DownloadGlyph<span>$(Esc $e.dlSetup)</span></a>
        <a class="btn btn-ghost" id="dl-zip" href="$($copy._channels.github.url)">$DownloadGlyph<span>Portable (.zip)</span></a>
        <a class="btn btn-ghost" href="$($copy._channels.github.url)">All releases</a>
        <a class="btn btn-ghost" href="$($copy._channels.store.url)" target="_blank" rel="noopener noreferrer"><span>Microsoft Store</span>$(ExtName $e.externalLabel)</a>
        <a class="btn btn-ghost" href="$BaseUrl/server.html">$(Esc $e.serverLink)</a>
        <span class="dl-meta" id="dlMeta">Offline-ready bundles - VLC runtimes and OCR packs included. Windows 10 1607+ / 11; a 32-bit build for Windows 7/8.1 is in the same download.</span>
      </div>
      <p class="dl-meta">$(Body $e.insideNote)</p>
      <p class="dl-meta">
        <a href="$(TrustUrl $code)">$(Esc $trust.$code.title)</a>
      </p>
$note    </div>
  </section>

  <section class="section" id="features">
    <div class="sec-head">
      <h2 class="section-title"><span class="hash">#</span>$(Esc $e.knowTitle)</h2>
    </div>
    <div class="grid">
$featureGroupCards
    </div>
    <details class="sec sec-spaced" id="all-features">
      <summary><span class="secnum">01</span>$(Body $e.title) - $(Esc $e.featuresLabel)</summary>
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
    <a href="$BaseUrl/glossary.html">Glossary</a>$portalFooter
    <a href="$(TrustUrl $code)">$(Esc $trust.$code.title)</a>
    <a href="$BaseUrl/docs/privacy.html">Privacy</a>
    <a href="$Repo#readme">README</a>
    <a href="$BaseUrl/whats-new.html">What's new</a>
    <a href="$Repo/blob/main/CHANGELOG.md">Changelog</a>
    <a href="$BaseUrl/support.html">Support</a>
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
    var b=document.getElementById('themeBtn'); if(b) b.setAttribute('aria-pressed', t==='light'?'false':'true');
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
        $cls = if ($_ -eq $code) { ' class="on" aria-current="true"' } else { '' }
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
$(OgImageMeta $e.ogAlt)
<meta property="og:site_name" content="$(Esc $ProductCompact)">
<meta property="og:locale" content="$code">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $t.title)">
<meta name="twitter:description" content="$(Esc $t.desc)">
$(TwitterImageMeta $e.ogAlt)
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$A$Favicon" sizes="any">
<link rel="apple-touch-icon" href="${A}assets/icons/store-icon-256.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="${A}assets/sza-kit.css">
<link rel="stylesheet" href="${A}assets/fms-site.css">
<link rel="stylesheet" href="${A}assets/fms-buttons.css">
<script type="application/ld+json">$(TrustJsonLd $code $t)</script>
</head>
<body>
<a class="skip-link" href="#main">$(Esc $e.skipLabel)</a>
<header class="site-header">
  <nav class="site-nav" aria-label="$(Esc $e.navLabel)">
  <a class="brand" href="$(PageUrl $code)" aria-label="$(Esc $ProductFull)"><span class="brand-mark"><img src="$A$Favicon" alt="" width="24" height="24"></span>$(Body $ProductFull)</a>
  $(ThemeButton (Esc $e.themeLabel))
  <a class="btn btn-ghost" href="$(PageUrl $code)">$(Esc $t.backLabel)</a>
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
    var b=document.getElementById('themeBtn'); if(b) b.setAttribute('aria-pressed', t==='light'?'false':'true');
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
<link rel="stylesheet" href="$BaseUrl/assets/fms-buttons.css">
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
  $(ThemeButton (SiteCopySpans 'themeLabel'))
  <a class="btn btn-primary" href="$BaseUrl/#get">$(Spans 'download')</a>
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
  function pressTheme(){ if(tb) tb.setAttribute('aria-pressed', doc.getAttribute('data-theme')==='light'?'false':'true'); }
  pressTheme();
  if(tb) tb.addEventListener('click',function(){
    var v=doc.getAttribute('data-theme')==='light'?'dark':'light';
    doc.setAttribute('data-theme',v);
    try{localStorage.setItem('sza-theme',v);}catch(e){}
    pressTheme();
  });
})();
</script>
</body>
</html>
"@
}

# --- Support and What's new: the two small generated root pages (ticket 207 T-5; DOCUMENTATION_CONCEPT section 4) ------
# Each has one address at the site root and is in-page trilingual (en, ru, ua) like the not-found page - the core three
# every root page of this site carries. Both are listed in the sitemap and the search index with the other root pages,
# and the topics page links them (Test-SiteQuality rule 1).
$InPageLangs = 'en', 'ru', 'ua'

# One page shell for both: head with the full SEO block, the header and footer of the root pages, the language and theme
# script of the not-found page. $titles and $descs are keyed en/ru/ua; the <title> is the English one, the switcher
# swaps document.title for the other two.
function PlainPage([string]$file, $titles, $descs, [string]$kicker, [string]$h1, [string]$lead, [string]$body) {
    $url = "$BaseUrl/$file"
    $script:RtlPage = $false
    $title = "$($titles.en)$TitleSeparator$TitleSuffix"
    $desc  = [string]$descs.en
    $nf = $copy._notFound
    function Spans([string]$key) { ($InPageLangs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (Esc $nf.$_.$key) }) -join '' }
    $ld = [ordered]@{
        '@context'   = 'https://schema.org'
        '@type'      = 'WebPage'
        'headline'   = $title
        'description' = $desc
        'url'        = $url
        'inLanguage' = 'en'
        'image'      = "$BaseUrl/$OgImage"
        'isPartOf'   = [ordered]@{ '@type' = 'WebSite'; 'name' = $ProductFull; 'url' = "$BaseUrl/" }
        'author'     = [ordered]@{ '@type' = 'Person'; 'name' = $copy._contact.github }
    }
    $metaTitles = ($InPageLangs | ForEach-Object { '{0}:"{1}"' -f $_, ('{0}{1}{2}' -f $titles.$_, $TitleSeparator, $TitleSuffix) }) -join ','
@"
<!DOCTYPE html>
<html lang="en" data-theme="dark" data-lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
$ThemeResolver
<title>$(Esc $title)</title>
<meta name="description" content="$(Esc $desc)">
<link rel="canonical" href="$url">
<link rel="alternate" hreflang="x-default" href="$url">
<meta property="og:type" content="website">
<meta property="og:title" content="$(Esc $title)">
<meta property="og:description" content="$(Esc $desc)">
<meta property="og:url" content="$url">
$(OgImageMeta ([string]$disc.socialCard.alt))
<meta property="og:site_name" content="$(Esc $ProductCompact)">
<meta property="og:locale" content="en">
<meta property="og:locale:alternate" content="ru">
<meta property="og:locale:alternate" content="uk">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="$(Esc $title)">
<meta name="twitter:description" content="$(Esc $desc)">
$(TwitterImageMeta ([string]$disc.socialCard.alt))
<meta name="theme-color" content="#0a0f0a">
<link rel="icon" href="$Favicon" sizes="any">
<link rel="apple-touch-icon" href="assets/icons/store-icon-256.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="assets/sza-kit.css">
<link rel="stylesheet" href="assets/fms-site.css">
<link rel="stylesheet" href="assets/fms-buttons.css">
<script type="application/ld+json">$($ld | ConvertTo-Json -Depth 5 -Compress)</script>
<style>
html[data-lang="ru"] [data-l]:not([data-l="ru"]),html[data-lang="en"] [data-l]:not([data-l="en"]),html[data-lang="ua"] [data-l]:not([data-l="ua"]){display:none}
.site-header>.brand{margin-right:auto}.hero{text-align:left;padding:1.5rem 0 .8rem}.hero .lead{margin:0}
.section h2{font-size:1.2rem;margin:0 0 .6rem}.section h3{font-size:1rem;margin:1rem 0 .4rem}.section ul{margin:0 0 .8rem;padding-left:1.3rem}.section li{margin:0 0 .5rem}
.section time{color:var(--text-2);font-size:.9rem;font-weight:400;margin-left:.6rem}
</style>
</head>
<body>
<a class="skip-link" href="#main">$(Spans 'skip')</a>
<header class="site-header">
  <span id="site-nav-label" class="sr-only">$(Spans 'nav')</span>
  <nav class="site-nav" aria-labelledby="site-nav-label">
  <a class="brand" href="$BaseUrl/" aria-label="$(Esc $ProductFull)"><span class="brand-mark"><img src="$Favicon" alt="" width="24" height="24"></span>$(Esc $ProductFull)</a>
  <div class="seg" role="group" aria-label="Language">
    <button data-setlang="ru">RU</button><button data-setlang="en" aria-pressed="true">EN</button><button data-setlang="ua">UA</button>
  </div>
  $(ThemeButton (SiteCopySpans 'themeLabel'))
  <a class="btn btn-primary" href="$BaseUrl/#get">$(Spans 'download')</a>
  </nav>
</header>

<main class="container" id="main" tabindex="-1">
  <section class="hero">
    <span class="kicker">$kicker</span>
    <h1>$h1</h1>
    <p class="lead">$lead</p>
  </section>

$body
</main>

<footer class="site-footer"><div class="container"><div class="footer-bottom">
  <span><a href="$BaseUrl/">$(Esc $ProductFull)</a> &middot; <a href="$BaseUrl/topics.html">$(Spans 'topicsTitle')</a> &middot; <a href="$BaseUrl/whats-new.html">$(($InPageLangs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (Esc $copy._whatsNew.short.$_) }) -join '')</a> &middot; <a href="$BaseUrl/support.html">$(($InPageLangs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (Esc $copy._support.short.$_) }) -join '')</a> &middot; <a href="$BaseUrl/docs/privacy.html">Privacy</a></span>
  <span>&copy; $(Get-Date -Format 'yyyy') <a href="https://$($copy._contact.website)">$($copy._contact.github)</a> &middot; <a href="mailto:$($copy._contact.email)">$($copy._contact.email)</a></span>
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
  function pressTheme(){ if(tb) tb.setAttribute('aria-pressed', doc.getAttribute('data-theme')==='light'?'false':'true'); }
  pressTheme();
  if(tb) tb.addEventListener('click',function(){
    var v=doc.getAttribute('data-theme')==='light'?'dark':'light';
    doc.setAttribute('data-theme',v);
    try{localStorage.setItem('sza-theme',v);}catch(e){}
    pressTheme();
  });
})();
</script>
</body>
</html>
"@
}

# A string of the copy keyed en/ru/ua, as the three in-page spans.
function InPageSpans($block, [scriptblock]$format = { param($s) Esc $s }) {
    ($InPageLangs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (& $format $block.$_) }) -join ''
}

function RenderSupport {
    $sp = $copy._support
    $mail = [string]$copy._contact.email
    $withMail = { param($s) (Esc $s).Replace('{email}', ('<a href="mailto:{0}">{0}</a>' -f $mail)) }
    $sections = foreach ($s in $sp.sections) {
        $parts = @("  <section class=`"section`" id=`"$($s.id)`">", "    <h2>$(InPageSpans $s.h)</h2>")
        if ($s.PSObject.Properties['p']) { $parts += "    <p>$(InPageSpans $s.p $withMail)</p>" }
        if ($s.PSObject.Properties['items']) {
            $n = @($s.items.en).Count
            $parts += '    <ul>'
            for ($i = 0; $i -lt $n; $i++) {
                $cell = [pscustomobject]@{ en = $s.items.en[$i]; ru = $s.items.ru[$i]; ua = $s.items.ua[$i] }
                $parts += "      <li>$(InPageSpans $cell)</li>"
            }
            $parts += '    </ul>'
        }
        if ($s.PSObject.Properties['links']) {
            $parts += '    <ul>'
            foreach ($l in $s.links) { $parts += "      <li><a href=`"$($l.url)`">$(InPageSpans $l.label)</a></li>" }
            $parts += '    </ul>'
        }
        $parts += '  </section>'
        $parts -join "`n"
    }
    PlainPage 'support.html' $sp.short $sp.desc (InPageSpans $sp.kicker) (InPageSpans $sp.h1) (InPageSpans $sp.lead) ($sections -join "`n`n")
}

# The latest released sections of CHANGELOG.md as plain data: version, date, categories, entries. [Unreleased] and anything
# older than the 'count'-th release is never read. A top-level entry (with its sub-entries) that names the component in
# development (site-facts.json development.mentionPattern) is left out - this page lists what a release shipped, and that
# component is in no package (docs/guides/POSITIONING.md section 2, specification 249).
function ReadReleases([int]$count) {
    $path = Join-Path $Root 'CHANGELOG.md'
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing release notes: $path" }
    $reserved = @([string]$facts.development.mentionPattern | Where-Object { $_ })
    $releases = [Collections.Generic.List[object]]::new()
    $cur = $null; $cat = $null; $item = $null
    foreach ($raw in [IO.File]::ReadAllLines($path, [Text.UTF8Encoding]::new($false))) {
        $line = $raw.TrimEnd()
        $m = [regex]::Match($line, '^## \[(?<v>\d+\.\d+\.\d+\.\d+)\]\s*-\s*(?<d>\d{4}-\d{2}-\d{2})')
        if ($m.Success) {
            if ($releases.Count -ge $count) { break }
            $cur = [pscustomobject]@{ Version = $m.Groups['v'].Value; Date = $m.Groups['d'].Value; Cats = [ordered]@{} }
            $releases.Add($cur); $cat = $null; $item = $null
            continue
        }
        if ($line -match '^## ') { $cur = $null; $cat = $null; $item = $null; continue }
        if (-not $cur) { continue }
        if ($line -match '^### (?<c>.+)$') {
            $cat = $Matches['c'].Trim()
            if (-not $cur.Cats.Contains($cat)) { $cur.Cats[$cat] = [Collections.Generic.List[object]]::new() }
            $item = $null
            continue
        }
        if (-not $cat) { continue }
        $b = [regex]::Match($line, '^(?<i>\s*)[-*]\s+(?<t>.+)$')
        if ($b.Success) {
            if ($b.Groups['i'].Length -ge 2 -and $item) { $item.Children.Add($b.Groups['t'].Value) }
            else {
                $item = [pscustomobject]@{ Text = $b.Groups['t'].Value; Children = [Collections.Generic.List[string]]::new() }
                $cur.Cats[$cat].Add($item)
            }
            continue
        }
        if ($line.Trim() -and $item) {
            if ($item.Children.Count -gt 0) { $item.Children[$item.Children.Count - 1] += ' ' + $line.Trim() }
            else { $item.Text += ' ' + $line.Trim() }
        }
    }
    foreach ($r in $releases) {
        foreach ($c in @($r.Cats.Keys)) {
            $kept = [Collections.Generic.List[object]]::new()
            foreach ($it in $r.Cats[$c]) {
                $all = @($it.Text) + @($it.Children)
                $hit = $false
                foreach ($rx in $reserved) { foreach ($t in $all) { if ($t -match $rx) { $hit = $true } } }
                if (-not $hit) { $kept.Add($it) }
            }
            $r.Cats[$c] = $kept
        }
    }
    , @($releases)
}

# Inline Markdown of a changelog entry: escaped first, then **bold**, `code`; a link keeps its text only (the target is a
# repository path the site does not serve).
function ChangelogInline([string]$s) {
    $h = Esc $s
    $h = [regex]::Replace($h, '\[([^\]]+)\]\([^)]*\)', '$1')
    $h = [regex]::Replace($h, '`([^`]+)`', '<code>$1</code>')
    [regex]::Replace($h, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
}

function RenderWhatsNew {
    $wn = $copy._whatsNew
    $releases = ReadReleases ([int]$wn.count)
    if ($releases.Count -eq 0) { throw 'CHANGELOG.md holds no released section for the What''s new page' }
    $blocks = foreach ($r in $releases) {
        $cats = foreach ($c in $r.Cats.Keys) {
            if ($r.Cats[$c].Count -eq 0) { continue }
            $lis = foreach ($it in $r.Cats[$c]) {
                $sub = ''
                if ($it.Children.Count -gt 0) {
                    $sub = "`n          <ul>`n" + (($it.Children | ForEach-Object { "            <li>$(ChangelogInline $_)</li>" }) -join "`n") + "`n          </ul>`n        "
                }
                "        <li>$(ChangelogInline $it.Text)$sub</li>"
            }
            "      <h3>$(Esc $c)</h3>`n      <ul>`n$($lis -join "`n")`n      </ul>"
        }
        if (@($cats).Count -eq 0) { continue }
        "  <section class=`"section`" id=`"v$($r.Version)`">`n    <h2>$($r.Version)<time datetime=`"$($r.Date)`">$($r.Date)</time></h2>`n    <div lang=`"en`" dir=`"ltr`">`n$(@($cats) -join "`n")`n    </div>`n  </section>"
    }
    $tail = "  <section class=`"section`">`n    <p class=`"note`">$(InPageSpans $wn.note)</p>`n    <p><a href=`"$Repo/releases`">$(InPageSpans $wn.all)</a></p>`n  </section>"
    PlainPage 'whats-new.html' $wn.short $wn.desc (InPageSpans $wn.kicker) (InPageSpans $wn.h1) (InPageSpans $wn.lead) ((@($blocks) + $tail) -join "`n`n")
}

# The availability matrix of docs/guides/POSITIONING.md section 4.1 as a marked region of server.html (specification
# 249): rows are the components, columns the packages, every cell the state the `availability` block of
# tools/site-facts.json holds - the same cells tools/Test-SitePositioning.ps1 holds to the packaging files. The labels are
# `_matrix` in site-copy.json (en/ru/uk, the three languages the hand-authored pages carry). On a narrow screen each row
# becomes a card whose cells carry the package name (class "lbl"), so nothing scrolls sideways.
function MatrixAt([object[]]$path) {
    $text = foreach ($lang in 'en', 'ru', 'uk') {
        $n = $copy._matrix.$lang
        foreach ($seg in $path) { $n = if ($n -is [array]) { $n[[int]$seg] } else { $n.([string]$seg) } }
        [string]$n
    }
    InPageSpans ([pscustomobject]@{ en = $text[0]; ru = $text[1]; ua = $text[2] })
}

function RenderServerMatrix {
    $av = $facts.availability
    $corner = MatrixAt @('corner')
    $head = foreach ($ch in $av.channels) { '<th scope="col">' + (MatrixAt @('channels', [string]$ch)) + '</th>' }
    $rows = foreach ($comp in $av.components) {
        $kid = [string]$comp
        $cells = foreach ($ch in $av.channels) {
            $cid = [string]$ch
            $state = [string](@($av.cells.$kid.$cid)[0])
            '<td data-state="' + $state + '"><span class="lbl">' + (MatrixAt @('channels', $cid)) + '</span>' + (MatrixAt @('states', $state)) + '</td>'
        }
        '<tr><th scope="row">' + (MatrixAt @('components', $kid)) + '</th>' + ($cells -join '') + '</tr>'
    }
    $factHead = for ($i = 0; $i -lt 4; $i++) {
        if ($i -eq 0) { '<th scope="col">' + $corner + '</th>' } else { '<th scope="col">' + (MatrixAt @('factsHead', $i)) + '</th>' }
    }
    $factRows = for ($r = 0; $r -lt @($copy._matrix.en.facts).Count; $r++) {
        $c = for ($i = 0; $i -lt 4; $i++) {
            $span = MatrixAt @('facts', $r, $i)
            if ($i -eq 0) { '<th scope="row">' + $span + '</th>' }
            else { '<td><span class="lbl">' + (MatrixAt @('factsHead', $i)) + '</span>' + $span + '</td>' }
        }
        '<tr>' + ($c -join '') + '</tr>'
    }
    @"
<!-- gen:matrix -->
<section class="section" id="matrix">
<h2>$(MatrixAt @('title'))</h2>
<p>$(MatrixAt @('lead'))</p>
<div class="tbl-wrap"><table class="cmp mx">
<thead><tr><th scope="col">$corner</th>$($head -join '')</tr></thead>
<tbody>
$($rows -join "`n")
</tbody>
</table></div>
<h3>$(MatrixAt @('factsTitle'))</h3>
<div class="tbl-wrap"><table class="cmp mx">
<thead><tr>$($factHead -join '')</tr></thead>
<tbody>
$($factRows -join "`n")
</tbody>
</table></div>
<p class="note">$(MatrixAt @('notes'))</p>
</section>
<!-- /gen:matrix -->
"@ -replace "`r`n", "`n"
}

$written = @(); $stale = @(); $compared = 0

function Complete-ChildFooter([string]$html, [string]$pageCode = '') {
    $footer = [regex]::Match($html, '(?s)<footer\b[^>]*>.*?</footer>')
    if (-not $footer.Success) { return $html }
    # A single-language page (a trust page, one per language) names the link once, in its own language where the copy has it
    # and in English otherwise, like the locale index pages; the in-page trilingual pages keep the data-l spans, because only
    # they carry the script and the rule that hides two of the three.
    $supportText = if ($pageCode) {
        $supportKey = switch ($pageCode) { 'ru' { 'ru' } 'uk' { 'ua' } default { 'en' } }
        Esc $copy._support.short.$supportKey
    } else {
        ($InPageLangs | ForEach-Object { '<span data-l="{0}">{1}</span>' -f $_, (Esc $copy._support.short.$_) }) -join ''
    }
    $contact = '<!-- gen:child-contact --><div class="container"><div class="footer-bottom"><a href="{0}/support.html">{1}</a><span><a href="https://{2}">SZA</a> &middot; <a href="mailto:{3}">{3}</a></span></div></div><!-- /gen:child-contact -->' -f $BaseUrl, $supportText, $copy._contact.website, $copy._contact.email
    $current = $footer.Value
    if ($current -match '<!-- gen:child-contact -->') {
        $completed = [regex]::Replace($current, '(?s)<!-- gen:child-contact -->.*?<!-- /gen:child-contact -->', [Text.RegularExpressions.MatchEvaluator]{ param($m) $contact })
    } elseif ($current.Contains('mailto:' + $copy._contact.email) -and $current.Contains('https://' + $copy._contact.website) -and $current.Contains('support.html')) {
        return $html
    } else {
        $completed = $current.Replace('</footer>', $contact + '</footer>')
    }
    return $html.Substring(0, $footer.Index) + $completed + $html.Substring($footer.Index + $footer.Length)
}

# The authored HTML keeps its content; these two shared regions are rendered from the same source as the locale pages.
$registry = Get-Content -LiteralPath (Join-Path $Root 'docs/DOCUMENT_REGISTRY.jsonl') | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }
foreach ($record in $registry | Where-Object { $_.role -eq 'source' -and $_.path -match '\.html$' -and $_.path -notmatch '^docs/help/' }) {
    $sourcePath = Join-Path $Root $record.path
    if (-not (Test-Path -LiteralPath $sourcePath)) { continue }
    $sourceText = [IO.File]::ReadAllText($sourcePath)
    $updated = [regex]::Replace($sourceText, '(?s)<script>\s*\(function\(\)\{(?:(?!</script>).)*?sza-theme(?:(?!</script>).)*?data-lang(?:(?!</script>).)*?</script>', [Text.RegularExpressions.MatchEvaluator]{ param($m) $ThemeResolver }, 1)
    $updated = Complete-ChildFooter $updated
    if ($sourceText -ne $updated) {
        if ($Check) { $stale += $record.path + ' shared locale resolver' }
        else { [IO.File]::WriteAllText($sourcePath, $updated, (New-Object Text.UTF8Encoding $false)); $written += $record.path + ' shared locale resolver' }
    }
}

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

# 1b. Update/check the server page's marked region (gen:matrix, specification 249). server.html is hand-authored, trilingual,
#     UTF-8 with a BOM and CRLF lines; the region is replaced in place and the file keeps its own BOM and line endings.
$serverFile = Join-Path $Root 'server.html'
if (Test-Path $serverFile) {
    $serverBytes = [IO.File]::ReadAllBytes($serverFile)
    $serverBom = ($serverBytes.Length -ge 3 -and $serverBytes[0] -eq 0xEF -and $serverBytes[1] -eq 0xBB -and $serverBytes[2] -eq 0xBF)
    $serverContent = [Text.UTF8Encoding]::new($false).GetString($serverBytes, $(if ($serverBom) { 3 } else { 0 }), $serverBytes.Length - $(if ($serverBom) { 3 } else { 0 }))
    $serverCrlf = $serverContent.Contains("`r`n")
    $serverLf = $serverContent -replace "`r`n", "`n"
    $expectedMatrix = (RenderServerMatrix).Trim()
    $matrixMatch = [regex]::Match($serverLf, '(?s)<!-- gen:matrix -->.*?<!-- /gen:matrix -->')
    if ($Check) {
        $compared++
        if (-not $matrixMatch.Success -or $matrixMatch.Value.Trim() -ne $expectedMatrix) { $stale += 'server.html (gen:matrix)' }
    } elseif ($matrixMatch.Success) {
        $newServer = $serverLf.Substring(0, $matrixMatch.Index) + $expectedMatrix + $serverLf.Substring($matrixMatch.Index + $matrixMatch.Length)
        if ($newServer -ne $serverLf) {
            if ($serverCrlf) { $newServer = $newServer -replace "`n", "`r`n" }
            [IO.File]::WriteAllText($serverFile, $newServer, [Text.UTF8Encoding]::new($serverBom))
            $written += 'server.html (marked region)'
        }
    }
}

# 2. Render/check trust pages
foreach ($code in $codes) {
    $path = if ($code -eq 'en') { Join-Path $Root 'trust.html' }
            else { Join-Path (Join-Path (Join-Path $Root $LangDir) $code) 'trust.html' }
    $html = (RenderTrust $code) -replace "`r`n", "`n"
    $html = Complete-ChildFooter $html $code

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
    $html = Complete-ChildFooter $html

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
$notFoundHtml = Complete-ChildFooter $notFoundHtml
if ($Check) {
    $compared++
    if (-not (Test-Path $notFoundPath)) { $stale += '404.html (missing)' }
    elseif ((Get-Content $notFoundPath -Raw -Encoding UTF8) -replace "`r`n", "`n" -ne $notFoundHtml) { $stale += '404.html' }
} else {
    [IO.File]::WriteAllText($notFoundPath, $notFoundHtml, (New-Object Text.UTF8Encoding $false))
    $written += '404.html'
}

# 5. Render/check the Support and What's new pages (ticket 207 T-5)
foreach ($page in @(@('support.html', (RenderSupport)), @('whats-new.html', (RenderWhatsNew)))) {
    $name = $page[0]; $html = ($page[1] -join "`n") -replace "`r`n", "`n"
    $html = Complete-ChildFooter $html
    $dest = Join-Path $Root $name
    if ($Check) {
        $compared++
        if (-not (Test-Path $dest)) { $stale += "$name (missing)" }
        elseif ((Get-Content $dest -Raw -Encoding UTF8) -replace "`r`n", "`n" -ne $html) { $stale += $name }
        continue
    }
    [IO.File]::WriteAllText($dest, $html, (New-Object Text.UTF8Encoding $false))
    $written += $name
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

# The task guides live under docs/ (docs/how-to-*.html); the same address kept at the root is a redirect stub, never a page.
function GuidePages {
    $dir = Join-Path $Root 'docs'
    if (-not (Test-Path -LiteralPath $dir)) { return }
    Get-ChildItem -LiteralPath $dir -Filter 'how-to-*.html' -File |
        Where-Object { -not (Test-RedirectStub $_.FullName) } |
        Sort-Object Name | ForEach-Object { "docs/$($_.Name)" }
}

function RenderRobots {
    @"
User-agent: *
Allow: /

Sitemap: $BaseUrl/sitemap.xml

"@ -replace "`r`n", "`n"
}

# When a page last changed, from git: the committer date of the last commit that touched its file, never the build time
# (canon PROMOTION section 3). A file that differs from HEAD, or is not tracked yet, changed today. Where git cannot answer
# (no git, no repository, a shallow clone) the map is empty and the sitemap carries no <lastmod> - a date is never invented.
$script:LastmodMap = $null     # repo-relative path -> yyyy-MM-dd
$script:DirtyPages = @{}       # repo-relative path -> $true when the working copy differs from HEAD
function Get-LastmodMap {
    if ($null -ne $script:LastmodMap) { return $script:LastmodMap }
    $map = @{}
    if ($UseGitHistory -and (Get-Command git -ErrorAction SilentlyContinue)) {
        Push-Location $Root
        try {
            $inside  = (& git rev-parse --is-inside-work-tree 2>$null)
            $shallow = (& git rev-parse --is-shallow-repository 2>$null)
            if ($inside -eq 'true' -and $shallow -ne 'true') {
                $date = $null
                foreach ($l in @(& git log --format='%x01%cs' --name-only --no-renames -- '*.html' 2>$null)) {
                    if ($l.Length -gt 0 -and $l[0] -eq [char]1) { $date = $l.Substring(1); continue }
                    if ($l -and $date -and -not $map.ContainsKey($l)) { $map[$l] = $date }
                }
                $today = Get-Date -Format 'yyyy-MM-dd'
                foreach ($l in @(& git status --porcelain --untracked-files=all -- '*.html' 2>$null)) {
                    if ($l.Length -le 3) { continue }
                    $p = $l.Substring(3).Trim().Trim('"')
                    if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1].Trim('"') }
                    $map[$p] = $today
                    $script:DirtyPages[$p] = $true
                }
            }
        } finally { Pop-Location }
    }
    $script:LastmodMap = $map
    $map
}

function RenderSitemap {
    $entries = @(SiteEntries)
    $lastmod = Get-LastmodMap
    # Ordinary filesystem-only work retains existing declared dates without inventing history.
    if (-not $UseGitHistory -and (Test-Path -LiteralPath (Join-Path $Root 'sitemap.xml'))) {
        $existing = [xml][IO.File]::ReadAllText((Join-Path $Root 'sitemap.xml'))
        foreach ($url in $existing.urlset.url) {
            if ($url.lastmod) { $lastmod[[string]$url.loc] = [string]$url.lastmod }
        }
    }

    $body = ($entries | ForEach-Object {
        $mod = if ($lastmod.ContainsKey($_.path)) { "`n    <lastmod>$($lastmod[$_.path])</lastmod>" } elseif ($lastmod.ContainsKey($_.url)) { "`n    <lastmod>$($lastmod[$_.url])</lastmod>" } else { '' }
        if ($_.url -eq "$BaseUrl/") {
            $alts = (($codes | ForEach-Object {
                '    <xhtml:link rel="alternate" hreflang="{0}" href="{1}"/>' -f $_, (PageUrl $_)
            }) -join "`n")
            "  <url>`n    <loc>$($_.url)</loc>$mod`n$alts`n    <xhtml:link rel=`"alternate`" hreflang=`"x-default`" href=`"$BaseUrl/`"/>`n  </url>"
        } else {
            "  <url>`n    <loc>$($_.url)</loc>$mod`n  </url>"
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
        $entries += [pscustomobject]@{ url = $url; lang = 'en'; title = $title; description = $desc; path = $f }
    }
    foreach ($f in (GuidePages)) {
        $html = Get-Content (Join-Path $Root $f) -Raw -Encoding UTF8
        $title = if ($html -match '(?s)<title>(.*?)</title>') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
        $desc  = if ($html -match '(?s)<meta\s+name="description"\s+content="(.*?)"') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
        if (-not $title -or -not $desc) { throw "guide page $f carries no title or description for the search index" }
        $entries += [pscustomobject]@{ url = "$BaseUrl/$f"; lang = 'en'; title = $title; description = $desc; path = $f }
    }
    # The privacy policy is a required page type (SITE-STRUCTURE section 2) and lives under docs/, so it is not a root page;
    # it carries the same title/description SEO block and is listed explicitly (specification 151, T-3).
    $pv = Get-Content (Join-Path $Root 'docs/privacy.html') -Raw -Encoding UTF8
    $pvTitle = if ($pv -match '(?s)<title>(.*?)</title>') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
    $pvDesc  = if ($pv -match '(?s)<meta\s+name="description"\s+content="(.*?)"') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { '' }
    if (-not $pvTitle -or -not $pvDesc) { throw 'docs/privacy.html carries no title or description for the search index' }
    $entries += [pscustomobject]@{ url = "$BaseUrl/docs/privacy.html"; lang = 'en'; title = $pvTitle; description = $pvDesc; path = 'docs/privacy.html' }
    foreach ($code in ($codes | Where-Object { $_ -ne 'en' })) {
        $entries += [pscustomobject]@{ url = (PageUrl $code); lang = $code; title = (PageTitle $copy.$code); description = $copy.$code.tagline; path = "$LangDir/$code/index.html" }
        $entries += [pscustomobject]@{ url = (TrustUrl $code); lang = $code; title = $trust.$code.title; description = $trust.$code.desc; path = "$LangDir/$code/trust.html" }
    }
    return $entries
}

function RenderSearchIndex {
    $entries = @(SiteEntries)
    ((@($entries | ForEach-Object {
        [ordered]@{ 'url' = $_.url; 'lang' = $_.lang; 'title' = $_.title; 'description' = $_.description }
    } | ConvertTo-Json -Depth 3) -join "`n") -replace "`r`n", "`n") + "`n"
}

# -Check reads the sitemap's <lastmod> dates with one day of grace: a page committed after midnight of the day the sitemap
# was generated has a commit date one day later than the date the generator knew, and that is not a stale sitemap. A
# machine that cannot read git history (no git, a shallow clone) renders no <lastmod> at all, so the dates on disk are
# set aside there and everything else is still compared.
function Normalize-SitemapLastmod([string]$onDisk, [string]$expected) {
    if ((Get-LastmodMap).Count -eq 0) { return [regex]::Replace($onDisk, '(?m)^[ \t]*<lastmod>[^<]*</lastmod>\r?\n', '') }
    $want = @{}
    foreach ($m in [regex]::Matches($expected, '<loc>(?<l>[^<]+)</loc>\s*<lastmod>(?<d>[^<]+)</lastmod>')) { $want[$m.Groups['l'].Value] = $m.Groups['d'].Value }
    [regex]::Replace($onDisk, '(<loc>(?<l>[^<]+)</loc>\s*<lastmod>)(?<d>\d{4}-\d{2}-\d{2})(</lastmod>)', {
        param($m)
        $d = $m.Groups['d'].Value
        $e = $want[$m.Groups['l'].Value]
        if ($e -and $e -ne $d) {
            $delta = ([datetime]::ParseExact($e, 'yyyy-MM-dd', $null) - [datetime]::ParseExact($d, 'yyyy-MM-dd', $null)).Days
            if ($delta -eq 1) { return $m.Groups[1].Value + $e + $m.Groups[4].Value }
        }
        $m.Value
    })
}

foreach ($pair in @(@('robots.txt', (RenderRobots)), @('sitemap.xml', (RenderSitemap)), @('search-index.json', (RenderSearchIndex)))) {
    $name = $pair[0]; $text = $pair[1]
    $dest = Join-Path $Root $name
    if ($Check) {
        $compared++
        if (-not (Test-Path $dest)) { $stale += "$name (missing)"; continue }
        $on = (Get-Content $dest -Raw -Encoding UTF8) -replace "`r`n", "`n"
        if ($name -eq 'sitemap.xml') { $on = Normalize-SitemapLastmod $on $text }
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

    # --- 12 Contract Conformance Checks (ticket 030 section 5, DOC-EXTERNAL-QUALITY), then 13-14 (specification 154) and 15 (the button adapter, ticket 243) ---
    # 1. Kit identity - the catalog's reference/sza-kit.css as re-vendored 2026-10-20 (the PAGE-STYLE 3.1 revision the catalog
    # published on 2026-10-17: 15926 bytes, code-surface tokens, copy-button states and the darker light accent; it replaced the
    # 1.4/1.5 file of 2026-10-07, 15661 bytes, SHA-256 27501A10.., and before it the 1.3 file, 14521 bytes, SHA-256 AEA958F8..).
    # Re-pin when the catalog moves the file again, never by hand-editing the kit.
    # Hashed with CRLF folded to LF: the catalog file is LF, and a text=auto checkout with
    # core.autocrlf turns the working copy CRLF without changing a single character of the kit.
    # assets/sza-kit.css is also marked -text in .gitattributes (PAGE-STYLE section 0 step 1), so a new clone is LF.
    $pinnedKitSha = '4ABA1DEE10CB902EE21CA4D86E4FBEED3BF7931C64B69FE15619727FD038C141'
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

    # 9b. The card is the 1200x630 rendering the pages declare (og:image:width/height are read from the data file, so a
    # card re-rendered at another size must change the declaration with it), the repository preview is the size and the
    # weight the data file declares, and both were rendered from the current words and icon (tools/New-SocialCard.ps1).
    function Get-PngSize([string]$path) {
        $b = [IO.File]::ReadAllBytes($path)
        if ($b.Length -lt 24 -or $b[0] -ne 0x89 -or $b[1] -ne 0x50 -or $b[2] -ne 0x4E -or $b[3] -ne 0x47) { return $null }
        $w = ([int]$b[16] -shl 24) -bor ([int]$b[17] -shl 16) -bor ([int]$b[18] -shl 8) -bor [int]$b[19]
        $h = ([int]$b[20] -shl 24) -bor ([int]$b[21] -shl 16) -bor ([int]$b[22] -shl 8) -bor [int]$b[23]
        @($w, $h)
    }
    foreach ($s in @($disc.socialCard.sizes)) {
        $p = Join-Path $Root (([string]$s.file) -replace '/', '\')
        if (-not (Test-Path -LiteralPath $p)) { $problems += "Social card: $($s.file) does not exist (run tools\New-SocialCard.ps1)"; continue }
        $dim = Get-PngSize $p
        if (-not $dim) { $problems += "Social card: $($s.file) is not a PNG"; continue }
        if ($dim[0] -ne [int]$s.width -or $dim[1] -ne [int]$s.height) { $problems += "Social card: $($s.file) is $($dim[0])x$($dim[1]) and the data file declares $($s.width)x$($s.height)" }
        if ($s.PSObject.Properties['maxBytes'] -and (Get-Item -LiteralPath $p).Length -ge [int64]$s.maxBytes) { $problems += "Social card: $($s.file) is not under $($s.maxBytes) bytes" }
    }
    $cardScript = Join-Path $PSScriptRoot 'New-SocialCard.ps1'
    if (-not (Test-Path -LiteralPath $cardScript)) { $problems += 'Social card: tools\New-SocialCard.ps1 (the one generator) does not exist' }
    else {
        $cardOut = @(& pwsh -NoProfile -File $cardScript -Check 2>&1)
        if ($LASTEXITCODE -ne 0) { foreach ($l in $cardOut) { $problems += "Social card: $l".TrimEnd() } }
    }

    # 9c. The mandatory pages (DOCUMENTATION_CONCEPT section 4) are linked from the landing's footer, which is hand-authored
    # on the root page and rendered on the twelve locale pages.
    $landingFooterFiles = @(Join-Path $Root 'index.html') + @($codes | Where-Object { $_ -ne 'en' } | ForEach-Object { Join-Path (Join-Path (Join-Path $Root $LangDir) $_) 'index.html' })
    foreach ($f in $landingFooterFiles) {
        if (-not (Test-Path $f)) { continue }
        $fc = Get-Content $f -Raw -Encoding UTF8
        $rel = $f.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
        foreach ($target in 'whats-new.html', 'support.html') {
            if ($fc -notmatch ('href="(?:' + [regex]::Escape($BaseUrl) + '/|\./|\.\./\.\./)?' + [regex]::Escape($target) + '"')) { $problems += "Mandatory pages: $rel does not link $target" }
        }
    }

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
    # lang/ and the task guides under docs/how-to-*.html - the product site; the rest of docs/ is the older redesign copy
    # and is not part of it.
    $sitePages = @(Get-ChildItem -LiteralPath $Root -Filter '*.html' -File | ForEach-Object { $_.FullName })
    if (Test-Path (Join-Path $Root 'lang')) {
        $sitePages += @(Get-ChildItem -LiteralPath (Join-Path $Root 'lang') -Filter '*.html' -Recurse -File | ForEach-Object { $_.FullName })
    }
    $sitePages += @(GuidePages | ForEach-Object { Join-Path $Root ($_ -replace '/', '\') })
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
        $res = [regex]::Match($fc, '(?s)<script>\s*\(function\(\)\{(?:(?!</script>).)*?sza-theme(?:(?!</script>).)*?data-lang(?:(?!</script>).)*?</script>')
        if (-not $res.Success) {
            $problems += "Theme resolver: $rel has no pre-paint resolver"
        } else {
            if ((& $fold $res.Value) -ne $canon) { $problems += "Theme resolver: $rel carries a resolver that is not the PAGE-STYLE section 7 text" }
            if ($css.Success -and $res.Index -gt $css.Index) { $problems += "Theme resolver: $rel places it after the first stylesheet link" }
        }
        # 15. The BUTTONS 2.0 adapter follows the page layer (PAGE-STYLE 3.1 section 5, "Extending the kit"): a page that loads
        # assets/fms-site.css and not assets/fms-buttons.css after it keeps the kit's pills, glow and hover lift.
        $siteLink = [regex]::Match($fc, '(?i)<link\b[^>]*\bhref\s*=\s*["''][^"'']*assets/fms-site\.css["''][^>]*>')
        if ($siteLink.Success) {
            $btnLink = [regex]::Match($fc, '(?i)<link\b[^>]*\bhref\s*=\s*["''][^"'']*assets/fms-buttons\.css["''][^>]*>')
            if (-not $btnLink.Success) { $problems += "Button adapter: $rel loads assets/fms-site.css but not assets/fms-buttons.css" }
            elseif ($btnLink.Index -lt $siteLink.Index) { $problems += "Button adapter: $rel loads assets/fms-buttons.css before assets/fms-site.css" }
        }
    }
    if ($problems.Count -gt 0) {
        Write-Host "Conformance check problems found:"
        $problems | ForEach-Object { Write-Host "  - $_" }
        Exit-Verdict 'Build-SitePages' 1 ($problems -join '; ')
    }

    Exit-Verdict 'Build-SitePages' 0 "$compared render targets and 17 conformance checks passed"
}

Write-Host "Wrote $($written.Count) pages:"
$written | ForEach-Object { Write-Host "  $_" }
