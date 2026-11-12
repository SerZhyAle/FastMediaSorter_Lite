<#
.SYNOPSIS
    Proves contract CONTEXT-HELP across Modern viewer and Share Manager surfaces (ticket 240).
.DESCRIPTION
    Validates:
      1. Every concrete Form type in Modern viewer and Share Manager has exactly one surface row in help-map.json.
      2. No duplicate surface IDs.
      3. Every 'none' and 'inherit' row carries a written reason; inherit parents resolve.
      4. Subordinate settings pages and wizard steps are covered.
      5. Offline help corpus: all 13 locales contain index.html and all topic pages.
      6. Rule 8 anatomy: purpose, capture, order, elements, related sections present.
      7. Rule 9 privacy & offline: no scripts, no-referrer meta, no external requests, no external CSS imports;
       every page noindex (rule 12; DOC-EXTERNAL-QUALITY section 8 item O).
      8. Every topic is accounted for (spec 267): real-window captures with hash evidence, settings-test captures,
         or declared not capturable with a reason and a visible note; no generic text, placeholder or stray picture.
    Ends in CHECK-VERDICT (tools/lib/Verdict.ps1).
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = [IO.Path]::GetFullPath($Root)

. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-ContextHelp' 2 'the check could not complete'
}

Write-Subject 'Test-ContextHelp' "in-app context help, F1, corner button and help corpus; $(Get-TreeLabel $Root)"

$mapFile = Join-Path $Root 'help-map.json'
if (-not (Test-Path -LiteralPath $mapFile)) {
    Exit-Verdict 'Test-ContextHelp' 2 'help-map.json is missing'
}

. (Join-Path $PSScriptRoot 'lib/ExchangeMark.ps1')
$exchangeTerms = Get-ExchangeMarkTerms $Root
$problems = [Collections.Generic.List[string]]::new()

# The pt help is pt-BR like the pt UI strings (L0291, L0293). One definition of European Portuguese serves both:
# the marker regex of LocaleScriptRules.vb, read from its source so the two cannot drift apart.
$europeanPortuguese = $null
$rulesFile = Join-Path $Root 'tests/Lite.Tests/LocaleScriptRules.vb'
if (Test-Path -LiteralPath $rulesFile) {
    $ruleMatch = [regex]::Match([IO.File]::ReadAllText($rulesFile), 'Shared ReadOnly EuropeanPortuguese As New Regex\((?<body>[\s\S]*?),\s*RegexOptions\.IgnoreCase\)')
    if ($ruleMatch.Success) {
        $markerPattern = -join ([regex]::Matches($ruleMatch.Groups['body'].Value, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
        if ($markerPattern.Length -gt 40) { $europeanPortuguese = [regex]::new($markerPattern, 'IgnoreCase') }
    }
}
if (-not $europeanPortuguese) {
    Exit-Verdict 'Test-ContextHelp' 2 'the European Portuguese marker rule of tests/Lite.Tests/LocaleScriptRules.vb could not be read'
}
function Problem([string]$msg) { $problems.Add($msg) }

$map = $null
try {
    $map = Get-Content -LiteralPath $mapFile -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    Exit-Verdict 'Test-ContextHelp' 2 "help-map.json is not valid JSON: $($_.Exception.Message)"
}

if ($map.schemaVersion -ne 1) {
    Problem "Unsupported schemaVersion '$($map.schemaVersion)' in help-map.json"
}

# 1. Surface ID uniqueness & form validity
$seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$declaredTypes = [Collections.Generic.List[string]]::new()

foreach ($surface in $map.surfaces) {
    if (-not $seenIds.Add($surface.id)) {
        Problem "Duplicate surface id: $($surface.id)"
    }
    if ($surface.form -notin @('page', 'both', 'inherit', 'none')) {
        Problem "Unknown form '$($surface.form)' on surface '$($surface.id)'"
    }
    if ($surface.form -in @('none', 'inherit') -and [string]::IsNullOrWhiteSpace($surface.reason)) {
        Problem "Surface '$($surface.id)' has form '$($surface.form)' without a reason"
    }
    if ($surface.form -eq 'inherit') {
        if (-not $surface.parent) {
            Problem "Surface '$($surface.id)' inherits without a parent"
        }
    }
    $askMember = $surface.PSObject.Properties['ask']
    if ($askMember) {
        if ($askMember.Value -cne 'off') {
            Problem "Surface '$($surface.id)' has ask '$($askMember.Value)'; the only value is 'off'"
        }
        if ($surface.form -cne 'inherit') {
            Problem "Surface '$($surface.id)' has ask but form '$($surface.form)'; only an inherit row carries it (section 7 item B)"
        }
    }
    if ($surface.type) {
        $declaredTypes.Add($surface.type)
    }
}

foreach ($surface in $map.surfaces) {
    if ($surface.form -eq 'inherit' -and $surface.parent) {
        if (-not $seenIds.Contains($surface.parent)) {
            Problem "Surface '$($surface.id)' inherits from non-existent parent '$($surface.parent)'"
        }
    }
}

# 2. Form type inventory check
# Try assembly reflection first
$modernDllCandidates = @(
    (Join-Path $Root 'src\Modern\bin\Debug\net10.0-windows\FastMediaSorter_LITE.dll'),
    (Join-Path $Root 'src\Modern\bin\Release\net10.0-windows\FastMediaSorter_LITE.dll')
)
$companionDllCandidates = @(
    (Join-Path $Root 'src\FastMediaSorterCompanion\bin\Debug\net10.0-windows10.0.19041\FastMediaSorterCompanion.dll'),
    (Join-Path $Root 'src\FastMediaSorterCompanion\bin\Debug\net10.0-windows\FastMediaSorterCompanion.dll'),
    (Join-Path $Root 'src\FastMediaSorterCompanion\bin\Release\net10.0-windows10.0.19041\FastMediaSorterCompanion.dll')
)

$modernDll = $modernDllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$companionDll = $companionDllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

$discoveredTypes = [Collections.Generic.List[string]]::new()
if ($modernDll -and $companionDll) {
    Add-Type -AssemblyName System.Windows.Forms
    foreach ($dll in @($modernDll, $companionDll)) {
        try {
            $asm = [System.Reflection.Assembly]::LoadFrom($dll)
            foreach ($t in $asm.GetTypes()) {
                if (-not $t.IsAbstract -and [System.Windows.Forms.Form].IsAssignableFrom($t)) {
                    $discoveredTypes.Add($t.FullName)
                }
            }
        } catch {
            # In case assembly reflection fails, fallback to source scan
        }
    }
}

if ($discoveredTypes.Count -eq 0) {
    # Static scan of VB sources for concrete Form classes
    foreach ($file in (Get-ChildItem (Join-Path $Root 'src') -Filter '*.vb' -Recurse)) {
        if ($file.FullName -like '*\bin\*' -or $file.FullName -like '*\obj\*') { continue }
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match '(?i)Inherits\s+(?:System\.Windows\.Forms\.)?Form\b') {
            $nsMatch = [regex]::Match($content, '(?i)Namespace\s+([a-zA-Z0-9_\.]+)')
            $classMatch = [regex]::Match($content, '(?i)(?:Public|Friend)?\s+(?:NotInheritable\s+|Partial\s+)*Class\s+([a-zA-Z0-9_]+)')
            if ($classMatch.Success) {
                $ns = if ($nsMatch.Success) { $nsMatch.Groups[1].Value } else { if ($file.FullName -like '*\FastMediaSorterCompanion\*') { 'FastMediaSorterCompanion' } else { 'fmsl' } }
                $fullName = "$ns.$($classMatch.Groups[1].Value)"
                if (-not $discoveredTypes.Contains($fullName)) {
                    $discoveredTypes.Add($fullName)
                }
            }
        }
    }
}

if ($discoveredTypes.Count -gt 0) {
    $diff1 = Compare-Object ($discoveredTypes | Sort-Object -Unique) ($declaredTypes | Sort-Object -Unique)
    if ($diff1) {
        foreach ($d in $diff1) {
            Problem "Form type mismatch between codebase and help-map: $($d.InputObject) ($($d.SideIndicator))"
        }
    }
}

# 3. Help Corpus & Rules Verification
$helpRoot = Join-Path $Root $map.helpRoot
if (-not (Test-Path -LiteralPath $helpRoot)) {
    Problem "Help root directory '$($map.helpRoot)' does not exist"
} else {
    $locales = @('en', 'ru', 'uk', 'de', 'it', 'es', 'fr', 'pt', 'ar', 'hi', 'bn', 'ur', 'zh')

    # CSS check
    foreach ($css in (Get-ChildItem $helpRoot -Filter '*.css')) {
        $cssText = [regex]::Replace([IO.File]::ReadAllText($css.FullName), '/\*[\s\S]*?\*/', '')
        if ($cssText -match '@import|url\(\s*["'']?https?://') {
            Problem "CSS file '$($css.Name)' makes remote requests or imports"
        }
    }

    # Pages check
    $helpTopics = @($map.surfaces | Where-Object { $_.form -in @('page', 'both') -and $_.help } | Select-Object -ExpandProperty help -Unique)

    foreach ($locale in $locales) {
        $localeDir = Join-Path $helpRoot $locale
        if (-not (Test-Path -LiteralPath $localeDir)) {
            Problem "Locale directory missing: docs/help/$locale"
            continue
        }

        # Check index.html
        $indexPath = Join-Path $localeDir 'index.html'
        if (-not (Test-Path -LiteralPath $indexPath)) {
            Problem "Missing index page: docs/help/$locale/index.html"
        } else {
            $indexHtml = [IO.File]::ReadAllText($indexPath)
            if ($locale -eq 'pt') {
                $european = $europeanPortuguese.Match([Net.WebUtility]::HtmlDecode($indexHtml))
                if ($european.Success) { Problem "European Portuguese '$($european.Value)' in docs/help/pt/index.html: the pt help is pt-BR like the pt UI strings" }
            }
            $expectedDir = if ($locale -in @('ar', 'ur')) { 'rtl' } else { 'ltr' }
            if ($indexHtml -notmatch "<html lang=`"$locale`" dir=`"$expectedDir`"") {
                Problem "Incorrect html lang/dir in docs/help/$locale/index.html"
            }
            if ($indexHtml -match '<script\b|\bon\w+\s*=' -or $indexHtml -notmatch 'name="referrer" content="no-referrer"') {
                Problem "Privacy violation or script detected in docs/help/$locale/index.html"
            }
            # A window help page is app help, not a site page: noindex (DOC-EXTERNAL-QUALITY section 8 item O, CONTEXT-HELP rule 12).
            if ($indexHtml -notmatch '<meta name="robots" content="noindex">') { Problem "docs/help/$locale/index.html is not marked noindex" }
        }

        # Check topic pages
        foreach ($topic in $helpTopics) {
            $topicPath = Join-Path $localeDir "$topic.html"
            if (-not (Test-Path -LiteralPath $topicPath)) {
                Problem "Missing topic page: docs/help/$locale/$topic.html"
                continue
            }
            $html = [IO.File]::ReadAllText($topicPath)
            if ($locale -eq 'pt') {
                $european = $europeanPortuguese.Match([Net.WebUtility]::HtmlDecode($html))
                if ($european.Success) { Problem "European Portuguese '$($european.Value)' in docs/help/pt/$topic.html: the pt help is pt-BR like the pt UI strings" }
            }
            $surface = $map.surfaces | Where-Object { $_.help -eq $topic -and $_.exchangeMark } | Select-Object -First 1
            if ($surface) {
                $expectedAlt = if ($surface.exchangeMark -eq 'full') { 'Media Exchange Server' } else { $exchangeTerms[$locale] }
                $expectedFile = if ($surface.exchangeMark -eq 'full') { 'full-256.png' } else { 'exchange-mark-small.svg' }
                $marks = @([regex]::Matches($html, '<img\b[^>]*class="exchange-(?:head|inline)"[^>]*>'))
                if ($marks.Count -ne 1) { Problem "Exchange mark must appear once in $locale/$topic.html" }
                else {
                    $tag = $marks[0].Value
                    $alt = [Net.WebUtility]::HtmlDecode([regex]::Match($tag, 'alt="([^"]*)"').Groups[1].Value)
                    $position = $html.IndexOf($tag)
                    $titleStart = $html.IndexOf('<h1')
                    $wrongPosition = if ($surface.exchangeMark -eq 'full') { $position -gt $titleStart } else { $position -lt $titleStart -or $position -gt $html.IndexOf('</h1>') }
                    if ($alt -cne $expectedAlt -or $tag -notmatch [regex]::Escape("../exchange-mark/$expectedFile") -or $wrongPosition) {
                        Problem "Exchange mark artwork, placement or accessible name is wrong in $locale/$topic.html"
                    }
                }
            }
            $expectedDir = if ($locale -in @('ar', 'ur')) { 'rtl' } else { 'ltr' }
            if ($html -notmatch "<html lang=`"$locale`" dir=`"$expectedDir`"") {
                Problem "Incorrect html lang/dir in docs/help/$locale/$topic.html"
            }
            if ($html -match '<script\b|\bon\w+\s*=' -or $html -notmatch 'name="referrer" content="no-referrer"') {
                Problem "Privacy violation or script in docs/help/$locale/$topic.html"
            }
            if ($html -notmatch '<meta name="robots" content="noindex">') { Problem "docs/help/$locale/$topic.html is not marked noindex" }

            # Rule 8 anatomy
            if ($topic -ne 'index') {
                foreach ($section in @('purpose', 'capture', 'order', 'elements', 'related')) {
                    if ($html -notmatch "id=`"$section`"") {
                        Problem "Page docs/help/$locale/$topic.html lacks section id='$section'"
                    }
                }
            }

            # Verify local links and anchors
            foreach ($link in [regex]::Matches($html, '(?:href|src)="([^"]+)"')) {
                $target = [System.Net.WebUtility]::HtmlDecode($link.Groups[1].Value)
                if ($target -match '^https?://' -or $target -match '^mailto:') { continue }
                $parts = $target -split '#', 2
                $resolved = if ($parts[0]) { [IO.Path]::GetFullPath((Join-Path $localeDir $parts[0])) } else { $topicPath }
                if (-not (Test-Path -LiteralPath $resolved)) {
                    Problem "Broken link in docs/help/$locale/$topic.html: $target"
                } elseif ($parts.Count -gt 1) {
                    $anchor = $parts[1]
                    $linkedContent = [IO.File]::ReadAllText($resolved)
                    if ($linkedContent -notmatch ('id="' + [regex]::Escape($anchor) + '"')) {
                        Problem "Broken anchor in docs/help/$locale/$topic.html: $target"
                    }
                }
            }
        }
    }
}

# Specification 267: every topic is accounted for. A topic is captured by the real-window harness (one evidence
# record per locale and theme), captured by the Share Manager settings test (declared 'settings-test'), or declared
# not capturable (declared 'none', with a reason): its page says so and no picture of it exists. File existence is
# not authenticity, and no topic keeps the generic text or the 64x48 placeholders of the first corpus.
$allLocales = @('en', 'ru', 'uk', 'de', 'it', 'es', 'fr', 'pt', 'ar', 'hi', 'bn', 'ur', 'zh')
function Get-PngSize([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { return $null }
    # PNG dimensions are big-endian.
    $width = [uint32]$bytes[16] * 16777216L + [uint32]$bytes[17] * 65536L + [uint32]$bytes[18] * 256L + $bytes[19]
    $height = [uint32]$bytes[20] * 16777216L + [uint32]$bytes[21] * 65536L + [uint32]$bytes[22] * 256L + $bytes[23]
    [pscustomobject]@{ Width = $width; Height = $height }
}
$captureEvidencePath = Join-Path $Root 'docs/help/context-help-capture-evidence.json'
$gapCount = 0
if (!(Test-Path -LiteralPath $captureEvidencePath)) {
    Problem 'Missing real-window capture evidence: docs/help/context-help-capture-evidence.json'
} else {
    $captureEvidence = Get-Content -LiteralPath $captureEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($captureEvidence.schema -ne 1) { Problem 'Unsupported context-help capture evidence schema' }
    $declared = @{}
    foreach ($entry in @($captureEvidence.declared)) {
        if (!$entry) { continue }
        if ($entry.capture -notin @('none', 'settings-test') -or [string]::IsNullOrWhiteSpace($entry.reason)) {
            Problem "Declared capture '$($entry.topic)' needs capture 'none' or 'settings-test' and a reason"
        }
        if ($entry.topic -notin $helpTopics) { Problem "Declared capture names an unknown topic: $($entry.topic)" }
        $declared[$entry.topic] = $entry
    }
    $byPath = @{}
    foreach ($record in @($captureEvidence.screenshots)) {
        if ($byPath.ContainsKey($record.path)) { Problem "Duplicate capture evidence: $($record.path)" }
        $byPath[$record.path] = $record
        if ($declared.ContainsKey($record.topic)) { Problem "Capture evidence for a declared topic: $($record.path)" }
    }
    foreach ($topic in $helpTopics) {
        if ($topic -eq 'index') { continue }
        $kind = if ($declared.ContainsKey($topic)) { $declared[$topic].capture } else { 'harness' }
        if ($kind -eq 'none') { $gapCount++ }
        foreach ($locale in $allLocales) {
            $page = Join-Path $helpRoot "$locale/$topic.html"
            if (Test-Path -LiteralPath $page) {
                $html = [IO.File]::ReadAllText($page)
                if ($html -match 'Review the options on screen|section in Fast Media Sorter\.|id="element-1"') {
                    Problem "Generic help text instead of authored copy: $locale/$topic.html"
                }
                if ($kind -eq 'none' -and $html -notmatch '<figure id="capture" class="no-shot">') {
                    Problem "Declared uncaptured topic must say so in place of the picture: $locale/$topic.html"
                }
            }
            foreach ($theme in @('light', 'dark')) {
                $relative = "$locale/$topic-$theme.png"
                $path = Join-Path $helpRoot $relative
                if ($kind -eq 'none') {
                    if (Test-Path -LiteralPath $path) { Problem "Picture of a topic declared not capturable: $relative" }
                    continue
                }
                if (!(Test-Path -LiteralPath $path)) { Problem "Missing real capture: $relative"; continue }
                $size = Get-PngSize $path
                if (!$size) { Problem "Invalid capture PNG: $relative"; continue }
                if ($size.Width -lt 256 -or $size.Height -lt 150) { Problem "Placeholder-sized capture: $relative" }
                if ($kind -ne 'harness') { continue }
                $record = $byPath[$relative]
                if (!$record) { Problem "Missing capture evidence: $relative"; continue }
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $record.sha256) { Problem "Capture hash drift: $relative" }
                if ($record.locale -cne $locale -or $record.theme -cne $theme -or $record.topic -cne $topic -or
                    $record.method -cne 'Shown WinForms window; PrintWindow by process-owned HWND' -or !$record.capturedUtc -or !$record.state) {
                    Problem "Incomplete real-window provenance: $relative"
                }
            }
        }
    }
    # A harness picture that is not a topic of its own is embedded in a page (the Drive page in share-settings):
    # it carries the same evidence. Anything else - an index picture, a retired topic - is a leftover placeholder.
    $embedded = @($captureEvidence.screenshots | ForEach-Object topic | Sort-Object -Unique | Where-Object { $_ -notin $helpTopics })
    foreach ($locale in $allLocales) {
        $localeDir = Join-Path $helpRoot $locale
        if (!(Test-Path -LiteralPath $localeDir)) { continue }
        foreach ($topic in $embedded) {
            foreach ($theme in @('light', 'dark')) {
                $relative = "$locale/$topic-$theme.png"
                $path = Join-Path $helpRoot $relative
                $record = $byPath[$relative]
                if (!$record -or !(Test-Path -LiteralPath $path)) { Problem "Missing embedded capture or its evidence: $relative"; continue }
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $record.sha256) { Problem "Capture hash drift: $relative" }
            }
        }
        foreach ($png in Get-ChildItem -LiteralPath $localeDir -Filter '*.png') {
            $owner = $png.BaseName -replace '-(light|dark)$', ''
            if (($owner -notin $helpTopics -and $owner -notin $embedded) -or $owner -eq 'index') {
                Problem "Picture that belongs to no topic: $locale/$($png.Name)"
            }
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($p in $problems) {
        Write-Host "  $p" -ForegroundColor Red
    }
    Exit-Verdict 'Test-ContextHelp' 1 "$($problems.Count) issues found"
}

$formCount = @($discoveredTypes | Sort-Object -Unique).Count
Exit-Verdict 'Test-ContextHelp' 0 "$formCount concrete forms, $($helpTopics.Count) topics across 13 locales verified; $gapCount topics declared without a capture"
