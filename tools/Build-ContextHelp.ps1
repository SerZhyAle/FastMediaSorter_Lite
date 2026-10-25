<#
.SYNOPSIS
    Generates offline HTML context help pages from authored copy and existing real captures for all 13 locales.
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string[]]$Topics = @()
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = [IO.Path]::GetFullPath($Root)
$Topics = @($Topics | ForEach-Object { $_ -split ',' })
$settingsHelp = Get-Content -LiteralPath (Join-Path $Root 'tools/settings-help-copy.json') -Raw -Encoding UTF8 | ConvertFrom-Json

. (Join-Path $PSScriptRoot 'lib/ExchangeMark.ps1')
$exchangeTerms = Get-ExchangeMarkTerms $Root
Add-Type -AssemblyName System.Drawing

$mapFile = Join-Path $Root 'help-map.json'
if (-not (Test-Path -LiteralPath $mapFile)) { throw 'help-map.json not found' }
$map = Get-Content -LiteralPath $mapFile -Raw -Encoding UTF8 | ConvertFrom-Json

$helpRoot = Join-Path $Root $map.helpRoot
[IO.Directory]::CreateDirectory($helpRoot) | Out-Null

$markRoot = Join-Path $helpRoot 'exchange-mark'
[IO.Directory]::CreateDirectory($markRoot) | Out-Null
foreach ($asset in @('png/full-256.png', 'svg/exchange-mark-small.svg', 'svg/exchange-mark-mono.svg')) {
    Copy-Item -LiteralPath (Join-Path $Root "assets/exchange-mark/$asset") -Destination (Join-Path $markRoot (Split-Path $asset -Leaf))
}

$locales = @('en', 'ru', 'uk', 'de', 'it', 'es', 'fr', 'pt', 'ar', 'hi', 'bn', 'ur', 'zh')

# Help word across 13 locales
$helpWords = @{
    'en' = 'Help'; 'ru' = 'Справка'; 'uk' = 'Довідка'; 'de' = 'Hilfe'; 'it' = 'Guida'; 'es' = 'Ayuda';
    'fr' = 'Aide'; 'pt' = 'Ajuda'; 'ar' = 'المساعدة'; 'hi' = 'सहायता'; 'bn' = 'সহায়তা'; 'ur' = 'مدد'; 'zh' = '帮助'
}

$purposeHeadings = @{
    'en' = 'What this is for'; 'ru' = 'Назначение'; 'uk' = 'Призначення'; 'de' = 'Verwendungszweck'; 'it' = 'Scopo'; 'es' = 'Para qué sirve';
    'fr' = 'Objectif'; 'pt' = 'Finalidade'; 'ar' = 'الغرض من هذه النافذة'; 'hi' = 'यह किस लिए है'; 'bn' = 'এটি কীসের জন্য'; 'ur' = 'یہ کس مقصد کے لیے ہے'; 'zh' = '用途说明'
}

$orderHeadings = @{
    'en' = 'In this order'; 'ru' = 'Порядок действий'; 'uk' = 'Порядок дій'; 'de' = 'In dieser Reihenfolge'; 'it' = 'In questo ordine'; 'es' = 'En este orden';
    'fr' = 'Dans cet ordre'; 'pt' = 'Nesta ordem'; 'ar' = 'بهذا الترتيب'; 'hi' = 'इस क्रम में'; 'bn' = 'এই ক্রমে'; 'ur' = 'اس ترتیب میں'; 'zh' = '操作顺序'
}

$elementsHeadings = @{
    'en' = 'The elements'; 'ru' = 'Элементы окна'; 'uk' = 'Елементи вікна'; 'de' = 'Die Elemente'; 'it' = 'Gli elementi'; 'es' = 'Los elementos';
    'fr' = 'Les éléments'; 'pt' = 'Os elementos'; 'ar' = 'عناصر النافذة'; 'hi' = 'तत्व'; 'bn' = 'উপাদানসমূহ'; 'ur' = 'عناصر'; 'zh' = '界面元素'
}

$relatedHeadings = @{
    'en' = 'Related'; 'ru' = 'Связанные разделы'; 'uk' = 'Пов''язані розділи'; 'de' = 'Verwandte Themen'; 'it' = 'Argomenti correlati'; 'es' = 'Temas relacionados';
    'fr' = 'Sujets associés'; 'pt' = 'Tópicos relacionados'; 'ar' = 'موضوعات ذات صلة'; 'hi' = 'संबंधित'; 'bn' = 'সম্পর্কিত'; 'ur' = 'متعلقہ'; 'zh' = '相关主题'
}

# Screenshots are inputs captured from real windows, never drawings manufactured by this renderer.
# Specification 267: existing placeholder debt remains visible in the readiness evidence.

# Collect surfaces with form in ('page', 'both')
$helpPages = [Collections.Generic.Dictionary[string, object]]::new()
foreach ($surface in $map.surfaces) {
    if ($surface.form -in @('page', 'both') -and $surface.help) {
        if (-not $helpPages.ContainsKey($surface.help)) {
            $helpPages[$surface.help] = $surface
        }
    }
}

Write-Host "Generating help pages for $($locales.Count) locales across $($helpPages.Count) distinct help topics..."

foreach ($locale in $locales) {
    $dir = if ($locale -in @('ar', 'ur')) { 'rtl' } else { 'ltr' }
    $localeDir = Join-Path $helpRoot $locale
    [IO.Directory]::CreateDirectory($localeDir) | Out-Null

    $helpWord = $helpWords[$locale]
    $purposeHead = $purposeHeadings[$locale]
    $orderHead = $orderHeadings[$locale]
    $elementsHead = $elementsHeadings[$locale]
    $relatedHead = $relatedHeadings[$locale]

    # Generate each topic page
    foreach ($entry in $helpPages.GetEnumerator()) {
        $pageKey = $entry.Key
        if ($Topics.Count -gt 0 -and $pageKey -notin $Topics) { continue }
        $surface = $entry.Value
        $title = [System.Net.WebUtility]::HtmlEncode($surface.name)
        $settingsContent = $settingsHelp.$pageKey.$locale
        if ($settingsContent) { $title = [System.Net.WebUtility]::HtmlEncode($settingsContent.title) }

        # Build elements details
        $elementsBuilder = [System.Text.StringBuilder]::new()
        if ($surface.pages -and $surface.pages.Count -gt 0) {
            foreach ($sub in $surface.pages) {
                $subName = [System.Net.WebUtility]::HtmlEncode($sub.name)
                $anchor = if ($sub.anchor) { $sub.anchor } else { "element-$($sub.id)" }
                $elementsBuilder.AppendLine("      <details id=`"$anchor`"><summary>$subName</summary><p>$subName section in Fast Media Sorter.</p></details>") | Out-Null
            }
        } else {
            $elementsBuilder.AppendLine("      <details id=`"element-1`"><summary>1. Cancel</summary><p>Cancel or close the active dialog without applying changes.</p></details>") | Out-Null
            $elementsBuilder.AppendLine("      <details id=`"element-2`"><summary>2. OK</summary><p>Apply settings or confirm operation.</p></details>") | Out-Null
        }
        $elementDetails = $elementsBuilder.ToString().TrimEnd()
        $purposeText = "Fast Media Sorter &amp; Sharing for Windows: $title."
        $orderText = 'Review the options on screen, adjust values as needed, and confirm.'
        $relatedSettings = ''
        if ($settingsContent) {
            $purposeText = [System.Net.WebUtility]::HtmlEncode($settingsContent.purpose)
            $orderText = [System.Net.WebUtility]::HtmlEncode($settingsContent.order)
            $elementDetails = ($settingsContent.sections | ForEach-Object {
                $detail = '<details id="{0}"><summary>{1}</summary><p>{2}</p>' -f $_.id,
                    [System.Net.WebUtility]::HtmlEncode($_.title), [System.Net.WebUtility]::HtmlEncode($_.text)
                if ($pageKey -eq 'share-settings' -and $_.id -eq 'drive') {
                    $detail += '<figure><img class="shot light" src="share-settings-drive-light.png" alt="Google Drive"><img class="shot dark" src="share-settings-drive-dark.png" alt="Google Drive"></figure>'
                }
                $detail + '</details>'
            }) -join "`n"
            $relatedSettings = '<p><a href="share-settings.html">{0}</a> · <a href="sftp-settings.html">{1}</a> · <a href="exchange-server-settings.html">{2}</a></p>' -f 
                [System.Net.WebUtility]::HtmlEncode($settingsHelp.'share-settings'.$locale.title),
                [System.Net.WebUtility]::HtmlEncode($settingsHelp.'sftp-settings'.$locale.title),
                [System.Net.WebUtility]::HtmlEncode($settingsHelp.'exchange-server-settings'.$locale.title)
            if ($pageKey -eq 'share-settings') {
                $elementDetails += '<p id="network"><a href="sftp-settings.html#network">SFTP</a></p><p id="hosting"><a href="sftp-settings.html#hosting">SFTP</a></p><p id="exchange"><a href="exchange-server-settings.html#exchange">{0}</a></p>' -f [System.Net.WebUtility]::HtmlEncode($settingsHelp.'exchange-server-settings'.$locale.title)
            }
        }

        $markHtml = ''
        if ($surface.exchangeMark -eq 'full') {
            $markHtml = '<img class="exchange-head" src="../exchange-mark/full-256.png" width="128" height="128" alt="Media Exchange Server">'
        } elseif ($surface.exchangeMark -eq 'inline') {
            $term = [System.Net.WebUtility]::HtmlEncode($exchangeTerms[$locale])
            $markHtml = "<img class=`"exchange-inline`" src=`"../exchange-mark/exchange-mark-small.svg`" width=`"24`" height=`"24`" alt=`"$term`">"
        }
        $pageHtml = @"
<!doctype html>
<html lang="$locale" dir="$dir">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self' data:; style-src 'self'; base-uri 'none'; form-action 'none'">
  <title>$title - Fast Media Sorter</title>
  <link rel="stylesheet" href="../help.css">
</head>
<body>
<main>
  $(if ($surface.exchangeMark -eq 'full') { '<span class="exchange-picture">' + $markHtml + '</span>' })
  <h1 id="title">$(if ($surface.exchangeMark -eq 'inline') { '<span class="exchange-picture">' + $markHtml + '</span> ' })$title</h1>
  <section id="purpose">
    <h2>$purposeHead</h2>
    <p>$purposeText</p>
  </section>
  <figure id="capture">
    <img class="shot light" src="$pageKey-light.png" alt="$title">
    <img class="shot dark" src="$pageKey-dark.png" alt="$title">
  </figure>
  <section id="order">
    <h2>$orderHead</h2>
    <p>$orderText</p>
  </section>
  <section id="elements">
    <h2>$elementsHead</h2>
$elementDetails
    $relatedSettings
  </section>
  <nav id="related">
    <h2>$relatedHead</h2>
    <a href="index.html">$helpWord</a>
  </nav>
  <footer id="footer">
    Fast Media Sorter &amp; Sharing for Windows · <a href="index.html">$helpWord</a> · <a href="mailto:sza@ukr.net">Support</a>
  </footer>
</main>
</body>
</html>
"@

        $htmlPath = Join-Path $localeDir "$pageKey.html"
        [IO.File]::WriteAllText($htmlPath, $pageHtml, [System.Text.Encoding]::UTF8)

        # Captures
        $lightCap = Join-Path $localeDir "$pageKey-light.png"
        $darkCap = Join-Path $localeDir "$pageKey-dark.png"
        if (!(Test-Path -LiteralPath $lightCap) -or !(Test-Path -LiteralPath $darkCap)) {
            throw "Real captures are required: $locale/$pageKey. Run the applicable opt-in capture harness."
        }
    }

    # Generate index.html
    $indexList = [System.Text.StringBuilder]::new()
    foreach ($entry in ($helpPages.GetEnumerator() | Sort-Object { $_.Value.name })) {
        $pageKey = $entry.Key
        $name = [System.Net.WebUtility]::HtmlEncode($entry.Value.name)
        $indexList.AppendLine("    <li><a href=`"$pageKey.html`">$name</a></li>") | Out-Null
    }

    $indexHtml = @"
<!doctype html>
<html lang="$locale" dir="$dir">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self' data:; style-src 'self'; base-uri 'none'; form-action 'none'">
  <title>$helpWord - Fast Media Sorter</title>
  <link rel="stylesheet" href="../help.css">
</head>
<body>
<main>
  <h1 id="title">$helpWord</h1>
  <p>Offline context help for Fast Media Sorter &amp; Sharing for Windows.</p>
  <ul>
$($indexList.ToString().TrimEnd())
  </ul>
</main>
</body>
</html>
"@

    $indexPath = Join-Path $localeDir 'index.html'
    [IO.File]::WriteAllText($indexPath, $indexHtml, [System.Text.Encoding]::UTF8)
}

Write-Host "Done. Generated help corpus in $helpRoot"
