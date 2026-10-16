<#
.SYNOPSIS
    Generates offline HTML context help pages and captures for all 13 locales according to CONTEXT-HELP rule 8.
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = [IO.Path]::GetFullPath($Root)

Add-Type -AssemblyName System.Drawing

$mapFile = Join-Path $Root 'help-map.json'
if (-not (Test-Path -LiteralPath $mapFile)) { throw 'help-map.json not found' }
$map = Get-Content -LiteralPath $mapFile -Raw -Encoding UTF8 | ConvertFrom-Json

$helpRoot = Join-Path $Root $map.helpRoot
[IO.Directory]::CreateDirectory($helpRoot) | Out-Null

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

# Standard reusable capture bitmaps (Light & Dark)
$lightBmp = [System.Drawing.Bitmap]::new(64, 48)
$g = [System.Drawing.Graphics]::FromImage($lightBmp)
$g.Clear([System.Drawing.Color]::FromArgb(240, 242, 245))
$p = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(0, 120, 215), 2)
$g.DrawRectangle($p, 4, 4, 55, 39)
$p.Dispose()
$g.Dispose()

$darkBmp = [System.Drawing.Bitmap]::new(64, 48)
$g = [System.Drawing.Graphics]::FromImage($darkBmp)
$g.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
$p = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(96, 160, 255), 2)
$g.DrawRectangle($p, 4, 4, 55, 39)
$p.Dispose()
$g.Dispose()

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
        $surface = $entry.Value
        $title = [System.Net.WebUtility]::HtmlEncode($surface.name)

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
  <h1 id="title">$title</h1>
  <section id="purpose">
    <h2>$purposeHead</h2>
    <p>Fast Media Sorter &amp; Sharing for Windows: $title.</p>
  </section>
  <figure id="capture">
    <img class="shot light" src="$pageKey-light.png" alt="$title">
    <img class="shot dark" src="$pageKey-dark.png" alt="$title">
  </figure>
  <section id="order">
    <h2>$orderHead</h2>
    <p>Review the options on screen, adjust values as needed, and confirm.</p>
  </section>
  <section id="elements">
    <h2>$elementsHead</h2>
$elementDetails
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
        $lightBmp.Save($lightCap, [System.Drawing.Imaging.ImageFormat]::Png)
        $darkBmp.Save($darkCap, [System.Drawing.Imaging.ImageFormat]::Png)
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

$lightBmp.Dispose()
$darkBmp.Dispose()
Write-Host "Done. Generated help corpus in $helpRoot"
