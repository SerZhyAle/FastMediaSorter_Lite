<#
.SYNOPSIS
    Generates offline HTML context help pages from authored copy and existing real captures for all 13 locales.

.DESCRIPTION
    The corpus under docs/help (the helpRoot of help-map.json) is a render target: every page is written here from
    tools/settings-help-copy.json, help-map.json and the capture list, never by hand (DOC-INTERNAL-QUALITY 0.12 W).

    -Check writes nothing under -Root. It copies the small inputs of -Root (the copy, the map, the exchange mark, the
    device vocabulary, the capture evidence) into a scratch folder, creates an empty stand-in for every capture
    picture the committed corpus holds (the generator only needs a picture to exist), runs this generator there, and
    compares the result with the committed pages byte for byte: a page that differs, a page the generator would write
    that is missing, a page the generator no longer writes that is still committed, and the three exchange-mark files.
    The pictures themselves are evidence, judged by tools/Test-ContextHelp.ps1, not here.

.PARAMETER Root
    The tree to generate into (or, with -Check, to judge). Defaults to this repository.

.PARAMETER Topics
    Generate only these topics. Not allowed with -Check, which judges the whole corpus.

.PARAMETER Check
    Report through tools\lib\Verdict.ps1: a subject banner and one closing line
    "Build-ContextHelp: PASS|FAIL|COULD NOT VERIFY"; exit 0 the corpus equals a fresh generation, 1 it does not,
    2 the check could not run (an input missing, or the generator refused the copy).

.EXAMPLE
    pwsh -NoProfile -File .\tools\Build-ContextHelp.ps1
    pwsh -NoProfile -File .\tools\Build-ContextHelp.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string[]]$Topics = @(),
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/Verdict.ps1')
# Only -Check carries a verdict; a generation run keeps its own terminating errors.
trap {
    if (-not $Check) { break }
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Build-ContextHelp' 2 'the check could not run'
}
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = [IO.Path]::GetFullPath($Root)

if ($Check) {
    $checkName = 'Build-ContextHelp'
    Write-Subject $checkName "the committed help pages and exchange-mark files of $Root against a fresh generation from its tools/settings-help-copy.json, help-map.json and capture list"
    if (@($Topics | Where-Object { $_ }).Count -gt 0) { Exit-Verdict $checkName 2 '-Topics is a partial generation; -Check judges the whole corpus' }
    $inputs = @('tools/settings-help-copy.json', 'help-map.json', 'assets/exchange-mark/png/full-256.png',
        'assets/exchange-mark/svg/exchange-mark-small.svg', 'assets/exchange-mark/svg/exchange-mark-mono.svg',
        'src/Localization/Localization.DeviceVocabulary.vb')
    foreach ($rel in $inputs) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $rel))) { Exit-Verdict $checkName 2 "input not found: $rel" }
    }
    $helpRel = [string](Get-Content -LiteralPath (Join-Path $Root 'help-map.json') -Raw -Encoding UTF8 | ConvertFrom-Json).helpRoot
    $committed = Join-Path $Root $helpRel
    if (-not (Test-Path -LiteralPath $committed -PathType Container)) { Exit-Verdict $checkName 2 "the committed corpus folder was not found: $helpRel" }

    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('fms-help-drift-' + [Guid]::NewGuid().ToString('N'))
    $problems = [Collections.Generic.List[string]]::new()
    $pages = 0
    try {
        foreach ($rel in $inputs) {
            $dest = Join-Path $scratch $rel
            [IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
            [IO.File]::Copy((Join-Path $Root $rel), $dest, $true)
        }
        $scratchHelp = Join-Path $scratch $helpRel
        [IO.Directory]::CreateDirectory($scratchHelp) | Out-Null
        $evidence = Join-Path $committed 'context-help-capture-evidence.json'
        if (Test-Path -LiteralPath $evidence) { [IO.File]::Copy($evidence, (Join-Path $scratchHelp 'context-help-capture-evidence.json'), $true) }
        # The generator checks that each capture picture exists and never reads it: an empty stand-in is enough.
        $committedFull = (Resolve-Path -LiteralPath $committed).Path.TrimEnd('\')
        foreach ($png in Get-ChildItem -LiteralPath $committedFull -Recurse -File -Filter '*.png') {
            $rel = $png.FullName.Substring($committedFull.Length).TrimStart('\')
            if ($rel -like 'exchange-mark\*') { continue }
            $dest = Join-Path $scratchHelp $rel
            [IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
            [IO.File]::WriteAllBytes($dest, [byte[]]@())
        }

        $log = & pwsh -NoProfile -File $PSCommandPath -Root $scratch 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            Write-Host (($log -split "`r?`n" | Where-Object { $_ } | Select-Object -Last 6) -join "`n")
            Exit-Verdict $checkName 2 'the generator refused the inputs (see above)'
        }

        $fresh = @{}
        $scratchFull = (Resolve-Path -LiteralPath $scratchHelp).Path.TrimEnd('\')
        foreach ($f in Get-ChildItem -LiteralPath $scratchFull -Recurse -File) {
            $rel = $f.FullName.Substring($scratchFull.Length).TrimStart('\')
            if ($f.Extension -eq '.html' -or $rel -like 'exchange-mark\*') { $fresh[$rel] = $f.FullName }
        }
        $have = @{}
        foreach ($f in Get-ChildItem -LiteralPath $committedFull -Recurse -File) {
            $rel = $f.FullName.Substring($committedFull.Length).TrimStart('\')
            if ($f.Extension -eq '.html' -or $rel -like 'exchange-mark\*') { $have[$rel] = $f.FullName }
        }
        foreach ($rel in ($fresh.Keys | Sort-Object)) {
            if (-not $have.ContainsKey($rel)) { $problems.Add("missing: $rel (the generator writes it)"); continue }
            $pages++
            $a = [IO.File]::ReadAllBytes($fresh[$rel]); $b = [IO.File]::ReadAllBytes($have[$rel])
            if ($a.Length -ne $b.Length -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($a)) -ne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($b))) { $problems.Add("differs: $rel") }
        }
        foreach ($rel in ($have.Keys | Sort-Object)) {
            if (-not $fresh.ContainsKey($rel)) { $problems.Add("stale: $rel (the generator no longer writes it)") }
        }
    }
    finally {
        if (Test-Path -LiteralPath $scratch) { [IO.Directory]::Delete($scratch, $true) }
    }
    if ($problems.Count -gt 0) {
        $problems | Select-Object -First 25 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        if ($problems.Count -gt 25) { Write-Host "  .. and $($problems.Count - 25) more" -ForegroundColor Red }
        Exit-Verdict $checkName 1 "$($problems.Count) file(s) differ from a fresh generation; run Build-ContextHelp.ps1 without -Check, never edit a page by hand"
    }
    Exit-Verdict $checkName 0 "$pages generated file(s) equal the committed corpus"
}

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
    'en' = 'Offline help for the windows of Fast Media Sorter &amp; Sharing for Windows: the media and file manager, the local sharing service, scheduled file operations and the Media Exchange Server.'
    'ru' = 'Встроенная справка по окнам Fast Media Sorter &amp; Sharing for Windows. Четыре части: медиа- и файловый менеджер, локальная служба раздачи, файловые операции по расписанию и Media Exchange Server.'
    'uk' = 'Вбудована довідка з вікон Fast Media Sorter &amp; Sharing for Windows. Чотири частини: медіа- та файловий менеджер, локальна служба роздачі, файлові операції за розкладом і Media Exchange Server.'
    'de' = 'Offline-Hilfe zu den Fenstern von Fast Media Sorter &amp; Sharing for Windows: Medien- und Dateimanager, ein lokaler Freigabedienst, geplante Dateivorgänge und Media Exchange Server.'
    'it' = 'Guida offline per le finestre di Fast Media Sorter &amp; Sharing for Windows: il gestore di media e file, il servizio locale di condivisione, le operazioni pianificate sui file e Media Exchange Server.'
    'es' = 'Ayuda sin conexión para las ventanas de Fast Media Sorter &amp; Sharing for Windows: el gestor de medios y archivos, el servicio local de uso compartido, las operaciones de archivos programadas y Media Exchange Server.'
    'fr' = 'Aide hors ligne pour les fenêtres de Fast Media Sorter &amp; Sharing for Windows : le gestionnaire de médias et fichiers, le service local de partage, les opérations planifiées sur les fichiers et Media Exchange Server.'
    'pt' = 'Ajuda offline para as janelas do Fast Media Sorter &amp; Sharing for Windows: o gerenciador de mídia e arquivos, o serviço local de compartilhamento, as operações de arquivo agendadas e o Media Exchange Server.'
    'ar' = 'مساعدة دون اتصال لنوافذ Fast Media Sorter &amp; Sharing for Windows: مدير الوسائط والملفات، خدمة محلية للمشاركة، عمليات الملفات المجدولة، Media Exchange Server.'
    'hi' = 'Fast Media Sorter &amp; Sharing for Windows की विंडो के लिए ऑफ़लाइन सहायता: मीडिया और फ़ाइल मैनेजर, स्थानीय साझाकरण सेवा, शेड्यूल किए गए फ़ाइल ऑपरेशन तथा Media Exchange Server।'
    'bn' = 'Fast Media Sorter &amp; Sharing for Windows-এর উইন্ডোগুলোর অফলাইন সহায়তা: মিডিয়া ও ফাইল ম্যানেজার, স্থানীয় শেয়ারিং সেবা, নির্ধারিত ফাইল অপারেশন এবং Media Exchange Server।'
    'ur' = 'Fast Media Sorter &amp; Sharing for Windows کی ونڈوز کے لیے آف لائن مدد: میڈیا اور فائل مینیجر، مقامی شیئرنگ سروس، شیڈولڈ فائل آپریشنز اور Media Exchange Server۔'
    'zh' = 'Fast Media Sorter &amp; Sharing for Windows 各窗口的离线帮助：媒体与文件管理器、本地共享服务、计划文件操作以及 Media Exchange Server。'
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
                # Specification L0308 section 3.2: a section with a numbered guide shows it opened, the steps before the text, so the
                # link that lands on its anchor lands on the steps.
                $sectionSteps = @($_.steps | Where-Object { $_ })
                $openAttr = if ($sectionSteps.Count -gt 0) { ' open' } else { '' }
                $stepsHtml = if ($sectionSteps.Count -gt 0) {
                    '<ol class="order">' + (($sectionSteps | ForEach-Object { '<li>' + [System.Net.WebUtility]::HtmlEncode($_) + '</li>' }) -join '') + '</ol>'
                } else { '' }
                $detail = '<details id="{0}"{3}><summary>{1}</summary>{4}<p>{2}</p>' -f $_.id,
                    [System.Net.WebUtility]::HtmlEncode($_.title), [System.Net.WebUtility]::HtmlEncode($_.text), $openAttr, $stepsHtml
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
        # CONTEXT-HELP rule 11, SITE-FAMILY-MAP rule 4 pair (1.16): the help of the shared exchange
        # function names the browser sibling in Related. The anchor is the product's landing - its
        # map help cell is a dash, so no web help address is ever linked (L0349). An empty string
        # leaves the other topics' pages byte-identical.
        $webSiblingLink = ''
        if ($pageKey -like 'exchange-*') {
            $webSiblingLink = "`n    <a href=`"https://serzhyale.github.io/FastMediaSorter_WEB/`">FastMediaSorter for Web</a>"
        }
        $pageHtml = @"
<!doctype html>
<html lang="$locale" dir="$dir">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <meta name="robots" content="noindex">
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
    <a href="index.html">$helpWord</a>$webSiblingLink
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
  <meta name="robots" content="noindex">
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
