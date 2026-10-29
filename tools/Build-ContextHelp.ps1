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

$indexIntros = @{
    'en' = 'Offline help for the windows of Fast Media Sorter &amp; Sharing for Windows: the media and file manager, the local sharing and scheduling service and the Media Exchange Server.'
    'ru' = 'Встроенная справка по окнам Fast Media Sorter &amp; Sharing for Windows: медиа- и файлового менеджера, локальной службы раздачи и расписаний и Media Exchange Server.'
    'uk' = 'Вбудована довідка з вікон Fast Media Sorter &amp; Sharing for Windows: медіа- та файлового менеджера, локальної служби роздачі та розкладів і Media Exchange Server.'
    'de' = 'Offline-Hilfe zu den Fenstern von Fast Media Sorter &amp; Sharing for Windows: Medien- und Dateimanager, lokaler Dienst für Freigabe und Zeitplanung und Media Exchange Server.'
    'it' = 'Guida offline per le finestre di Fast Media Sorter &amp; Sharing for Windows: il gestore di media e file, il servizio locale di condivisione e pianificazione e Media Exchange Server.'
    'es' = 'Ayuda sin conexión para las ventanas de Fast Media Sorter &amp; Sharing for Windows: el gestor de medios y archivos, el servicio local de uso compartido y programación y Media Exchange Server.'
    'fr' = 'Aide hors ligne pour les fenêtres de Fast Media Sorter &amp; Sharing for Windows : le gestionnaire de médias et fichiers, le service local de partage et de planification et Media Exchange Server.'
    'pt' = 'Ajuda offline para as janelas do Fast Media Sorter &amp; Sharing for Windows: o gestor de média e ficheiros, o serviço local de partilha e agendamento e o Media Exchange Server.'
    'ar' = 'مساعدة دون اتصال لنوافذ Fast Media Sorter &amp; Sharing for Windows: مدير الوسائط والملفات، خدمة المشاركة والجدولة المحلية، Media Exchange Server.'
    'hi' = 'Fast Media Sorter &amp; Sharing for Windows की विंडो के लिए ऑफ़लाइन सहायता: मीडिया और फ़ाइल मैनेजर, स्थानीय साझाकरण और शेड्यूलिंग सेवा तथा Media Exchange Server।'
    'bn' = 'Fast Media Sorter &amp; Sharing for Windows-এর উইন্ডোগুলোর অফলাইন সহায়তা: মিডিয়া ও ফাইল ম্যানেজার, স্থানীয় শেয়ারিং ও সময়সূচি পরিষেবা এবং Media Exchange Server।'
    'ur' = 'Fast Media Sorter &amp; Sharing for Windows کی ونڈوز کے لیے آف لائن مدد: میڈیا اور فائل مینیجر، مقامی شیئرنگ اور شیڈولنگ سروس اور Media Exchange Server۔'
    'zh' = 'Fast Media Sorter &amp; Sharing for Windows 各窗口的离线帮助：媒体与文件管理器、本地共享与计划服务以及 Media Exchange Server。'
}

# "1. .. 2. .." becomes the numbered order list; a number that does not continue the sequence stays text.
function ConvertTo-OrderHtml([string]$text) {
    $markers = [regex]::Matches($text, '(?<=^|\s)(\d{1,2})[.)]\s+')
    $steps = [Collections.Generic.List[string]]::new()
    $expected = 1; $start = -1; $bodyStart = 0
    foreach ($m in $markers) {
        # \d also matches Arabic-Indic, Devanagari and Bengali digits; read the value of any of them.
        $value = 0
        foreach ($c in $m.Groups[1].Value.ToCharArray()) { $value = $value * 10 + [int][char]::GetNumericValue($c) }
        if ($value -ne $expected) { continue }
        if ($start -ge 0) { $steps.Add($text.Substring($bodyStart, $m.Index - $bodyStart).Trim()) }
        elseif ($m.Index -ne 0) { break }
        $start = $m.Index; $bodyStart = $m.Index + $m.Length; $expected++
    }
    if ($steps.Count -lt 1) { return '<p>' + [Net.WebUtility]::HtmlEncode($text) + '</p>' }
    $steps.Add($text.Substring($bodyStart).Trim())
    '<ol class="order">' + (($steps | ForEach-Object { '<li>' + [Net.WebUtility]::HtmlEncode($_) + '</li>' }) -join '') + '</ol>'
}

# Screenshots are inputs captured from real windows, never drawings manufactured by this renderer.
# Specification 267: a window that cannot be captured without a side effect is declared in the capture evidence
# (capture 'none', with its reason) and its page says so in place of the picture - missing stays visibly missing.
$noShotNotes = @{
    'en' = 'There is no screenshot of this window: opening it starts a device, a network listener or the whole viewer, so it is not captured.'
    'ru' = 'Снимка этого окна нет: при открытии оно запускает устройство, сетевое подключение или весь просмотрщик, поэтому его не снимают.'
    'uk' = 'Знімка цього вікна немає: під час відкриття воно запускає пристрій, мережеве підключення або весь переглядач, тому його не знімають.'
    'de' = 'Für dieses Fenster gibt es keinen Screenshot: Beim Öffnen startet es ein Gerät, einen Netzwerkempfang oder den ganzen Betrachter, daher wird es nicht aufgenommen.'
    'it' = 'Non c''è uno screenshot di questa finestra: all''apertura avvia un dispositivo, un ascolto di rete o l''intero visualizzatore, quindi non viene acquisita.'
    'es' = 'No hay captura de esta ventana: al abrirse pone en marcha un dispositivo, una escucha de red o todo el visor, por eso no se captura.'
    'fr' = 'Cette fenêtre n''a pas de capture : à l''ouverture, elle démarre un appareil, une écoute réseau ou toute la visionneuse, elle n''est donc pas capturée.'
    'pt' = 'Não há captura desta janela: ao abrir, ela inicia um dispositivo, uma escuta de rede ou todo o visualizador, por isso não é capturada.'
    'ar' = 'لا توجد لقطة شاشة لهذه النافذة: عند فتحها تشغّل جهازًا أو استماعًا على الشبكة أو العارض كله، لذلك لا تُلتقط.'
    'hi' = 'इस विंडो का स्क्रीनशॉट नहीं है: खुलते ही यह कोई डिवाइस, नेटवर्क लिसनर या पूरा व्यूअर शुरू कर देती है, इसलिए इसे कैप्चर नहीं किया जाता।'
    'bn' = 'এই উইন্ডোর কোনো স্ক্রিনশট নেই: খোলার সময় এটি একটি ডিভাইস, নেটওয়ার্ক লিসেনার বা পুরো ভিউয়ার চালু করে, তাই এটি ক্যাপচার করা হয় না।'
    'ur' = 'اس ونڈو کا کوئی اسکرین شاٹ نہیں: کھلتے ہی یہ کوئی ڈیوائس، نیٹ ورک لسنر یا پورا ویور شروع کر دیتی ہے، اس لیے اسے کیپچر نہیں کیا جاتا۔'
    'zh' = '此窗口没有截图：它一打开就会启动设备、网络监听或整个查看器，因此不截取。'
}
$evidenceFile = Join-Path $helpRoot 'context-help-capture-evidence.json'
$uncaptured = @()
if (Test-Path -LiteralPath $evidenceFile) {
    $uncaptured = @((Get-Content -LiteralPath $evidenceFile -Raw -Encoding UTF8 | ConvertFrom-Json).declared |
        Where-Object { $_.capture -eq 'none' } | ForEach-Object { $_.topic })
}

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
        if ($pageKey -eq 'index') { continue }   # written below as the list of topics; it has no window to capture
        $surface = $entry.Value
        $settingsContent = $settingsHelp.$pageKey.$locale
        # Specification 267: no generic page. A topic without authored copy in this language stops the build.
        if (-not $settingsContent) { throw "Authored help copy is required: $locale/$pageKey in tools/settings-help-copy.json." }
        $title = [System.Net.WebUtility]::HtmlEncode($settingsContent.title)
        $relatedSettings = ''
        if ($settingsContent) {
            $purposeText = [System.Net.WebUtility]::HtmlEncode($settingsContent.purpose)
            $orderHtml = ConvertTo-OrderHtml $settingsContent.order
            $elementDetails = ($settingsContent.sections | ForEach-Object {
                $detail = '<details id="{0}"><summary>{1}</summary><p>{2}</p>' -f $_.id,
                    [System.Net.WebUtility]::HtmlEncode($_.title), [System.Net.WebUtility]::HtmlEncode($_.text)
                if ($pageKey -eq 'share-settings' -and $_.id -eq 'drive') {
                    $detail += '<figure><img class="shot light" src="share-settings-drive-light.png" alt="Google Drive"><img class="shot dark" src="share-settings-drive-dark.png" alt="Google Drive"></figure>'
                }
                $detail + '</details>'
            }) -join "`n"
            # A Manager window points at the Manager's settings; a viewer window at the viewer's own Settings.
            if ($surface.type -like 'FastMediaSorterCompanion.*' -or $pageKey -eq 'live-broadcast') {
                $relatedSettings = '<p><a href="share-settings.html">{0}</a> · <a href="sftp-settings.html">{1}</a> · <a href="exchange-server-settings.html">{2}</a></p>' -f
                    [System.Net.WebUtility]::HtmlEncode($settingsHelp.'share-settings'.$locale.title),
                    [System.Net.WebUtility]::HtmlEncode($settingsHelp.'sftp-settings'.$locale.title),
                    [System.Net.WebUtility]::HtmlEncode($settingsHelp.'exchange-server-settings'.$locale.title)
            } elseif ($pageKey -ne 'settings') {
                $viewerSettings = if ($settingsHelp.'settings'.$locale) { $settingsHelp.'settings'.$locale.title } else { 'Settings' }
                $relatedSettings = '<p><a href="settings.html">{0}</a></p>' -f [System.Net.WebUtility]::HtmlEncode($viewerSettings)
            }
            if ($pageKey -eq 'share-settings') {
                $elementDetails += '<p id="network"><a href="sftp-settings.html#network">SFTP</a></p><p id="hosting"><a href="sftp-settings.html#hosting">SFTP</a></p><p id="exchange"><a href="exchange-server-settings.html#exchange">{0}</a></p>' -f [System.Net.WebUtility]::HtmlEncode($settingsHelp.'exchange-server-settings'.$locale.title)
            }
        }

        $figureHtml = if ($pageKey -in $uncaptured) {
            '<figure id="capture" class="no-shot"><figcaption>' + [Net.WebUtility]::HtmlEncode($noShotNotes[$locale]) + '</figcaption></figure>'
        } else {
            "<figure id=`"capture`">`n    <img class=`"shot light`" src=`"$pageKey-light.png`" alt=`"$title`">`n    <img class=`"shot dark`" src=`"$pageKey-dark.png`" alt=`"$title`">`n  </figure>"
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
  $figureHtml
  <section id="order">
    <h2>$orderHead</h2>
    $orderHtml
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
        if ($pageKey -notin $uncaptured -and (!(Test-Path -LiteralPath $lightCap) -or !(Test-Path -LiteralPath $darkCap))) {
            throw "Real captures are required: $locale/$pageKey. Run the applicable opt-in capture harness."
        }
    }

    # Generate index.html
    $indexList = [System.Text.StringBuilder]::new()
    $culture = [Globalization.CultureInfo]::GetCultureInfo($locale)
    $indexEntries = foreach ($entry in $helpPages.GetEnumerator()) {
        if ($entry.Key -eq 'index') { continue }
        $localized = $settingsHelp.($entry.Key).$locale
        [pscustomobject]@{ Key = $entry.Key; Name = $(if ($localized) { $localized.title } else { $entry.Value.name }) }
    }
    foreach ($entry in ($indexEntries | Sort-Object Name -Culture $culture.Name)) {
        $name = [System.Net.WebUtility]::HtmlEncode($entry.Name)
        $indexList.AppendLine("    <li><a href=`"$($entry.Key).html`">$name</a></li>") | Out-Null
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
  <p>$($indexIntros[$locale])</p>
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
