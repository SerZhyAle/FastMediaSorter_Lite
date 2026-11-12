<#
.SYNOPSIS
    Read-only scan of the sibling repositories for what they say about this product (specification 273, T-9, class H).

.DESCRIPTION
    Walks the sibling repositories named in an untracked local roots file, finds every mention of this product (its names,
    aliases and URLs from docs/guides/PRODUCT_NAMING.md), classifies each one and compares it with what the product now says
    about itself (tools/site-facts.json: positioning.blurb, the capability register, retiredPhrases). It never writes to a
    sibling repository. Hand-run only: it is not part of Run-AllTests (tools/check-placement.jsonl, class hand-run).

    Classes
      link-label        the anchor text of a link to this product. Expected: the full name (the compact name is accepted
                        in a label-length place); a legacy form, a bare "Fast Media Sorter", "FastMediaSorter" or a label
                        without the name beside a link to the site root or the repository is a mismatch.
      link-description  the text beside such a link. Expected: positioning.blurb.<lang> of site-facts.json, verbatim.
      feature-claim     a sentence saying what the product is or does. Judged against the register: a retired phrase, a claim
                        of a planned capability, a gated capability (guest links, Google Drive), a definition that calls the
                        product only a photo/media sorter or viewer, or an offer to download the Media Exchange Server is a
                        mismatch. A name that defines the Windows program only as a "folder share companion" is one too.
      name-only         the name with no description around it (a heading, a list item). Informational; a legacy form is noted.
      technical         an alias (FMS_Win), an identifier or URL in config, a fixture or test vector, a code comment.
      generated         a render target: the hit sits in a file produced by a generator. Reported with the generator or
                        template to edit instead (the roots file `generated` rules, or a "generated" header). The inner
                        class (link-label ...) is kept in `innerClass` and judged the same way.

    Verdict per hit: ok | mismatch | info. A link into the product site is also checked against this repository's files
    (AC-11): a target that does not exist is a mismatch. The overall verdict (CHECK-VERDICT, tools/lib/Verdict.ps1):
      0 PASS             no link-label / link-description / feature-claim mismatch and every configured root was scanned
      1 FAIL             at least one such mismatch
      2 COULD NOT VERIFY the roots file or site-facts.json is absent or unusable, or a configured root path is missing and
                         nothing else failed

.PARAMETER RootsFile
    Default tools/product-mention-roots.local.json (untracked: it holds machine paths). Shape:
      { "roots": [
          { "name": "Streams_Player",                 // repository name used in the output and the packet file name
            "path": "P:\\WINDOWS\\Streams_Player",      // absolute path, scanned read-only
            "note": "text shown at the top of its packet",            // optional
            "exclude": [ "regex on the relative path" ],              // optional, extra to the built-in build-output list
            "generated": [ { "match": "regex on the relative path (forward slashes)",
                             "editThrough": "the generator, template or source to change instead" } ] } ] }

.PARAMETER OutFile      Default docs/specifications/273_handoff/mentions.jsonl (one hit per line).
.PARAMETER PacketDir    Default: the folder of OutFile. One <name>.md hand-off packet per repository with hits.
.PARAMETER NoPackets    Do not write the packets.
.PARAMETER NoWrite      Write neither the JSONL nor the packets (digest and verdict only).
.PARAMETER Repo         Only these repositories of the roots file.
.PARAMETER Top          How many mismatches the digest lists (default 20).
#>
[CmdletBinding()]
param(
    [string]$RootsFile,
    [string]$OutFile,
    [string]$PacketDir,
    [switch]$NoPackets,
    [switch]$NoWrite,
    [string[]]$Repo,
    [int]$Top = 20
)

$ErrorActionPreference = 'Stop'
$toolsDir = $PSScriptRoot
$repoRoot = Split-Path $toolsDir -Parent
. (Join-Path $toolsDir 'lib\Verdict.ps1')
$check = 'Get-ProductMentions'

if (-not $RootsFile) { $RootsFile = Join-Path $toolsDir 'product-mention-roots.local.json' }
if (-not $OutFile) { $OutFile = Join-Path $repoRoot 'docs\specifications\273_handoff\mentions.jsonl' }
if (-not $PacketDir) { $PacketDir = Split-Path $OutFile -Parent }

Write-Subject $check "sibling repositories of tools/product-mention-roots.local.json against tools/site-facts.json; this repository: $(Get-TreeLabel $repoRoot)"

# ---------------------------------------------------------------- inputs
if (-not (Test-Path -LiteralPath $RootsFile)) {
    Exit-Verdict $check 2 'tools/product-mention-roots.local.json not found - an untracked file listing the sibling repositories (shape: see the script help)'
}
function Get-Prop($o, [string]$name, $default = $null) {
    if ($null -eq $o) { return $default }
    $p = $o.PSObject.Properties[$name]
    if ($p -and $null -ne $p.Value) { return $p.Value }
    return $default
}
try { $rootsDoc = Get-Content -LiteralPath $RootsFile -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Exit-Verdict $check 2 "the roots file is not valid JSON: $($_.Exception.Message)" }
$roots = @(Get-Prop $rootsDoc 'roots' @())
if ($roots.Count -eq 0) { Exit-Verdict $check 2 'the roots file lists no repository' }
if ($Repo) { $roots = @($roots | Where-Object { $Repo -contains (Get-Prop $_ 'name' '') }) }
if ($roots.Count -eq 0) { Exit-Verdict $check 2 'no configured repository matches -Repo' }

$factsPath = Join-Path $toolsDir 'site-facts.json'
if (-not (Test-Path -LiteralPath $factsPath)) { Exit-Verdict $check 2 'tools/site-facts.json not found' }
try { $facts = Get-Content -LiteralPath $factsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Exit-Verdict $check 2 "tools/site-facts.json is not valid JSON: $($_.Exception.Message)" }
$blurb = @{}
$blurbObj = $facts.positioning.blurb
if (-not $blurbObj) { Exit-Verdict $check 2 'tools/site-facts.json has no positioning.blurb (specification 273 T-0)' }
foreach ($p in $blurbObj.PSObject.Properties) { if ($p.Name -ne '_about') { $blurb[$p.Name] = [string]$p.Value } }
$retired = @($facts.retiredPhrases.phrases | ForEach-Object { [pscustomobject]@{ Text = [string]$_.text; Ground = [string]$_.ground } })
$plannedRx = [System.Collections.Generic.List[regex]]::new()
foreach ($it in @($facts.capabilities.items)) {
    if ($it.state -ne 'planned') { continue }
    $pc = Get-Prop $it 'plannedClaims'
    if (-not $pc) { continue }
    foreach ($lp in $pc.PSObject.Properties) { foreach ($x in @($lp.Value)) { $plannedRx.Add([regex]::new([string]$x, 'IgnoreCase')) } }
}

$fullName = 'Fast Media Sorter & Sharing for Windows'
$compactName = 'Fast Media Sorter & Sharing'

# ---------------------------------------------------------------- name patterns (PRODUCT_NAMING sections 1 and 2)
$amp = '(?:&amp;amp;|&amp;|&&|\\u0026|&)'
$fms = 'Fast\s?Media\s?Sorter'
# Kind: name = a visible product name; alias = alias or identifier; url = a link into the product
$patterns = @(
    @{ Form = 'url-site';        Kind = 'url';   Rx = 'serzhyale\.github\.io/FastMediaSorter_Lite[^\s"''<>)\]\\`|,;*]*' }
    @{ Form = 'url-repo';        Kind = 'url';   Rx = 'github\.com/SerZhyAle/FastMediaSorter_Lite[^\s"''<>)\]\\`|,;*]*' }
    @{ Form = 'full';            Kind = 'name';  Rx = "$fms\s+$amp\s*Sharing\s+for\s+Windows" }
    @{ Form = 'and-form';        Kind = 'name';  Rx = "$fms\s+and\s+Sharing" }
    @{ Form = 'compact';         Kind = 'name';  Rx = "$fms\s+$amp\s*Sharing" }
    @{ Form = 'legacy-windows';  Kind = 'name';  Rx = "$fms\s+for\s+Windows" }
    @{ Form = 'lite-visible';    Kind = 'name';  Rx = "$fms[ ](?:LITE|Lite)\b" }
    @{ Form = 'viewer-title';    Kind = 'name';  Rx = "$fms\s+by\s+SZA" }
    @{ Form = 'share-manager';   Kind = 'name';  Rx = 'Share\s+Manager' }
    @{ Form = 'server-edition';  Kind = 'name';  Rx = 'Folder\s+Share\s+Server' }
    @{ Form = 'old-companion';   Kind = 'name';  Rx = 'Windows\s+Folder\s+Share(?:\s*\(PC\s+Companion\))?|PC\s+Companion' }
    @{ Form = 'winget-id';       Kind = 'alias'; Rx = 'SerZhyAle\.FastMediaSorter(?:\.Server)?' }
    @{ Form = 'identifier';      Kind = 'alias'; Rx = 'FastMediaSorter(?:_Lite|_LITE|_x86|Companion\w*|SingleInstanceMutex)' }
    @{ Form = 'alias-FMS_Win';   Kind = 'alias'; Rx = '(?-i:\bFMS_Win\b)' }
    @{ Form = 'alias-FMS_W';     Kind = 'alias'; Rx = '(?-i:\bFMS_W\b)' }
    @{ Form = 'file-name';       Kind = 'alias'; Rx = '\bfms[-_]lite\b' }
)
$opt = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
foreach ($p in $patterns) { $p.Compiled = [regex]::new($p.Rx, $opt) }
$combined = [regex]::new('(?:' + (($patterns | ForEach-Object { $_.Rx }) -join '|') + ')', $opt -bor [System.Text.RegularExpressions.RegexOptions]::Compiled)
$rxProductUrl = [regex]::new('serzhyale\.github\.io/FastMediaSorter_Lite|github\.com/SerZhyAle/FastMediaSorter_Lite', 'IgnoreCase')
$rxAnchor = [regex]::new('<a\b([^>]*)>(.*?)<\\?/a\s*>', 'IgnoreCase,Singleline')
$rxHref = [regex]::new('href\s*=\s*\\?["'']([^"''\\]*)', 'IgnoreCase')
$rxMd = [regex]::new('\[((?:[^\]\\]|\\.)*)\]\(\s*<?([^)\s>]*)>?(?:\s+"[^"]*")?\s*\)')
$rxSpan = [regex]::new('<span\b([^>]*)>(.*?)</span\s*>', 'IgnoreCase,Singleline')
$rxLabelB = [regex]::new('<(b|strong)\b[^>]*>(.*?)</\1\s*>', 'IgnoreCase,Singleline')
$rxPlaceholder = [regex]::new('^\s*(?:\{\{.*\}\}|\{%.*%\}|<\?.*\?>|\$\{.*\}|%\w+%)\s*$')
# a generic caption of a link beside a card that names the product in its heading
$rxGenericLabel = [regex]::new('^\s*(?:(?:the\s+)?(?:GitHub\s+)?(?:(?:product|project|official|download|install(?:ation)?)\s+(?:site|website|page)|repository|repo|source(?:\s+code)?|website|web\s*site|site|home\s*page|project|open(?:\s+project)?|view|learn\s+more|read\s+more|more|details|download)|(?:(?:на\s+)?(?:сайт\w*|страниц\w*|сторінк\w*)\s+(?:продукт\w*|проект\w*|установк\w*|встановлен\w*)|репозиторий|репозиторій|исходники|сайт|проект|открыть|відкрити|подробнее|детальніше)(?:\s+GitHub)?|Installationsseite|Produktseite|page\s+d.installation|page\s+du\s+produit|p.gina\s+de\s+instalaci.n)\s*[.:>»→]*\s*$', 'IgnoreCase')

# claim checks (specification 273 section 3 and the common brief)
$rxOldCategory = [regex]::new('\b(?:(?:photo|image|picture|video|media)s?(?:\s+and\s+(?:video|photo)s?)?[\s-]+(?:sorter|sorting|viewer|organi[sz]er)|viewer\s+and\s+sorter|sorter)\b|сортировщик|сортувальник|Sortierer|clasificador|trieur|ordinatore|organizador de (?:fotos|medios)', 'IgnoreCase')
$rxFileManager = [regex]::new('file\s+manager|файлов\w+\s+менеджер|Dateimanager|gestor\w*\s+de\s+(?:medios\s+y\s+|média\s+e\s+)?(?:archivos|ficheiros)|gestionnaire\s+de\s+(?:médias\s+et\s+)?fichiers|gestore\s+di\s+media\s+e\s+file|文件管理', 'IgnoreCase')
$rxGated = [regex]::new('guest\s+(?:invitation|link)|invitation\s+link|гостев\w+\s+(?:приглашени|ссылк)|гостьов\w+\s+(?:запрошенн|посилан)|Share\s+Manager[^.]{0,80}google\s+drive|google\s+drive[^.]{0,80}Share\s+Manager', 'IgnoreCase')
$rxMesDownload = [regex]::new('(?:download|install|winget|скача|устан|завантаж|встанов)[^.]{0,60}Media\s+Exchange\s+Server|Media\s+Exchange\s+Server[^.]{0,60}(?:download|install|winget|скача|устан|завантаж|встанов)', 'IgnoreCase')
$rxOwnNames = [regex]::new("$fms(?:\s+$amp\s*(?:Sharing|Organizer)(?:\s+for\s+Windows)?|\s+for\s+Windows|\s+LITE|\s+by\s+SZA)?|Media\s+Exchange\s+Server", 'IgnoreCase')
$rxContinuation = [regex]::new('^\s*(?:It|Its|This|These|They|The\s+(?:program|app|application|product|tool|Windows)|Он|Она|Оно|Это|Эта|Эти|Её|Его|Вона|Воно|Це|Ця)\b', 'IgnoreCase')
$rxStaleModel =[regex]::new('three\s+optional\s+(?:parts|components)[^.]{0,160}\bviewer\b|три\s+необязательн\w+\s+част\w+[^.]{0,160}просмотрщик|три\s+необов.язков\w+\s+частин\w+[^.]{0,160}переглядач', 'IgnoreCase')

# ---------------------------------------------------------------- file walking
$excludeDirs = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    '.git', 'node_modules', 'bin', 'obj', 'build', 'dist', 'out', '.gradle', '.idea', '.vs', '.cxx', '__pycache__', '.next', 'target',
    'coverage', '.cache', '.dart_tool', '.pytest_cache', '.mypy_cache', 'venv', '.venv', 'Pods', 'intermediates', '.terraform', '.kotlin',
    # agent scratch, copies of a repository and archived history (history is not rewritten)
    # (.claude is agent memory and agent definitions; stage is a build staging copy of pages that are scanned at their source)
    'worktrees', 'temp', 'tmp', '.tmp', 'scratch', 'gradle-tmp', 'archive', 'captures', '.claude', 'stage'), [StringComparer]::OrdinalIgnoreCase)
$skipExt = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    '.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico', '.bmp', '.svg', '.psd', '.avif', '.jxl', '.heic', '.mp3', '.mp4', '.mkv', '.avi', '.mov',
    '.wav', '.flac', '.ogg', '.zip', '.7z', '.rar', '.gz', '.tgz', '.tar', '.jar', '.aar', '.apk', '.aab', '.exe', '.dll', '.pdb', '.so',
    '.dylib', '.class', '.o', '.obj', '.lib', '.msi', '.msix', '.appx', '.pdf', '.ttf', '.otf', '.woff', '.woff2', '.eot', '.db', '.sqlite',
    '.bin', '.dat', '.map', '.lock', '.suo', '.user', '.nupkg', '.snk', '.keystore', '.jks', '.pfx', '.cer', '.fdd', '.fd-sec', '.spres', '.log'), [StringComparer]::OrdinalIgnoreCase)
$codeExt = [System.Collections.Generic.HashSet[string]]::new([string[]]@(
    '.kt', '.kts', '.java', '.go', '.cs', '.vb', '.ps1', '.psm1', '.py', '.ts', '.tsx', '.js', '.mjs', '.cjs', '.gradle', '.properties', '.toml',
    '.ini', '.cfg', '.csproj', '.vbproj', '.props', '.targets', '.sln', '.bat', '.cmd', '.sh', '.c', '.h', '.cpp', '.rs', '.swift', '.dart',
    '.iss', '.wxs', '.manifest', '.pro', '.cmake', '.mod', '.sum'), [StringComparer]::OrdinalIgnoreCase)
$dataExt = [System.Collections.Generic.HashSet[string]]::new([string[]]@('.json', '.jsonl', '.yml', '.yaml', '.csv', '.tsv'), [StringComparer]::OrdinalIgnoreCase)
$maxBytes = 3MB
$testPathRx = [regex]::new('(?i)(?:^|/)(?:tests?|__tests__|testdata|test-data|fixtures?|vectors|androidTest|test-resources|testResources)(?:/|$)|\.tests?(?:/|$)|/src/test/')

function Get-FileText([System.IO.FileInfo]$fi) {
    $bytes = [System.IO.File]::ReadAllBytes($fi.FullName)
    if ($bytes.Length -eq 0) { return $null }
    if ($bytes.Length -ge 2 -and (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) {
        return [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    $n = [Math]::Min($bytes.Length, 8000)
    if ([System.Array]::IndexOf($bytes, [byte]0, 0, $n) -ge 0) { return $null }
    $t = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($t.Length -gt 0 -and $t[0] -eq [char]0xFEFF) { $t = $t.Substring(1) }
    return $t
}

# ---------------------------------------------------------------- text helpers
function ConvertTo-Plain([string]$s) {
    $t = [regex]::Replace($s, '<\\?/?[a-zA-Z!][^>]*>', ' ')
    $t = $t.Replace('\"', '"').Replace('\n', ' ').Replace('\t', ' ').Replace('\/', '/')
    $t = [System.Net.WebUtility]::HtmlDecode($t)
    $t = [regex]::Replace($t, '\[([^\]]*)\]\([^)]*\)', '$1')
    $t = $t -replace '\*{1,3}|`', ''
    $t = $t -replace '\s+', ' '
    return $t.Trim()
}
function Get-Norm([string]$s) {
    $t = [System.Net.WebUtility]::HtmlDecode($s)
    $t = ($t -replace '\s+', ' ').Trim().TrimEnd('.', ' ').ToLowerInvariant()
    return $t
}
function Limit([string]$s, [int]$n = 300) { if ($s.Length -gt $n) { return $s.Substring(0, $n - 3) + '...' } return $s }
function Get-HintLang([string]$rel) {
    foreach ($seg in ($rel -split '/')) {
        foreach ($tok in ($seg -split '\.')) {
            if ($tok -match '^(ru|en|uk|ua|de|it|es|fr|pt|ar|hi|bn|ur|zh)(?:[-_][a-zA-Z0-9]+)?$') {
                $l = $Matches[1].ToLowerInvariant(); if ($l -eq 'ua') { $l = 'uk' }; return $l
            }
        }
    }
    return ''
}
function Get-Lang([string]$text, [string]$hint) {
    if ($text -match '[\u0400-\u04FF]') {
        if ($hint -in 'ru', 'uk') { return $hint }
        if ($text -match '[іїєґІЇЄҐ]') { return 'uk' } else { return 'ru' }
    }
    if ($text -match '[\u0600-\u06FF]') { if ($hint -in 'ar', 'ur') { return $hint } else { return 'ar' } }
    if ($text -match '[\u0900-\u097F]') { return 'hi' }
    if ($text -match '[\u0980-\u09FF]') { return 'bn' }
    if ($text -match '[\u4E00-\u9FFF]') { return 'zh' }
    # Latin text on a page of a known language is judged against that language (an English footer on the ar page expects the ar blurb);
    # the English source side of a translation table (the JSON key) is judged as English
    if ($script:keyEnd -gt 0 -and $script:hitPos -lt $script:keyEnd) { return 'en' }
    if ($hint) { return $hint }
    return 'en'
}
function Get-Escaped([string]$name, [string]$ext, [string]$line) {
    if ($ext -in '.html', '.htm', '.xml', '.json', '.jsonl' -or $line -match '&amp;') { return $name.Replace('&', '&amp;') }
    return $name
}

function Find-Spans([string]$s) {
    $all = New-Object System.Collections.Generic.List[object]
    foreach ($p in $patterns) {
        foreach ($m in $p.Compiled.Matches($s)) {
            $all.Add([pscustomobject]@{ Start = $m.Index; Len = $m.Length; End = $m.Index + $m.Length; Form = $p.Form; Kind = $p.Kind; Text = $m.Value })
        }
    }
    $kept = New-Object System.Collections.Generic.List[object]
    $lastEnd = -1
    foreach ($x in ($all | Sort-Object @{ Expression = 'Start' }, @{ Expression = 'Len'; Descending = $true })) {
        if ($x.Start -ge $lastEnd) { $kept.Add($x); $lastEnd = $x.End }
    }
    return $kept.ToArray()
}

# Everything wrong with a sentence that describes the product, judged against the register.
function Get-ClaimProblems([string]$text) {
    $r = New-Object System.Collections.Generic.List[string]
    foreach ($p in $retired) {
        if ($p.Text -and $text.IndexOf($p.Text, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $r.Add("retired phrase '$($p.Text)'") }
    }
    foreach ($rx in $plannedRx) { if ($rx.IsMatch($text)) { $r.Add('names a planned capability (reading documents, e-books, texts)') ; break } }
    if ($rxGated.IsMatch($text)) { $r.Add('names a gated capability (guest links or Google Drive)') }
    if ($rxMesDownload.IsMatch($text)) { $r.Add('offers the Media Exchange Server for download or install (its gate is shut)') }
    if ($rxStaleModel.IsMatch($text)) { $r.Add("old three-part model (the first part is the media and file manager, not a viewer)") }
    # the product's own names contain "Media Sorter": judge the words around them
    $around = $rxOwnNames.Replace($text, ' ')
    if ($rxOldCategory.IsMatch($around) -and -not $rxFileManager.IsMatch($around)) { $r.Add('defines the product as only a sorter or viewer (the category is media and file manager)') }
    return , $r
}

function Get-TargetProblem([string]$url) {
    if ($url -notmatch '(?i)serzhyale\.github\.io/FastMediaSorter_Lite(/[^?#\s]*)?') { return '' }
    $path = $Matches[1]
    if ([string]::IsNullOrEmpty($path) -or $path -eq '/') { return '' }
    $rel = $path.TrimStart('/')
    $rel = [System.Uri]::UnescapeDataString($rel)
    $candidates = @($rel)
    if ($rel.EndsWith('/')) { $candidates = @($rel + 'index.html') } elseif ($rel -notmatch '\.[A-Za-z0-9]+$') { $candidates = @($rel + '.html', $rel + '/index.html') }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath (Join-Path $repoRoot ($c -replace '/', '\'))) { return '' } }
    return "link target '$path' is not a file of this repository (AC-11)"
}

function Test-RootLink([string]$href) {
    return [bool]($href -match '(?i)^(?:https?:)?//serzhyale\.github\.io/FastMediaSorter_Lite/?(?:index\.html)?(?:[?#].*)?$' -or
        $href -match '(?i)^(?:https?:)?//github\.com/SerZhyAle/FastMediaSorter_Lite/?(?:[?#].*)?$' -or
        $href -match '(?i)^(?:https?:)?//serzhyale\.github\.io/FastMediaSorter_Lite/(?:lang/[a-z-]+/?)(?:index\.html)?$')
}

# ---------------------------------------------------------------- hit collection
$hits = New-Object System.Collections.Generic.List[object]
$script:ctx = $null   # per-file context: Repo, Rel, Ext, Gen, Hint, IsTest

function Add-Hit([int]$Line, [string]$Class, [string]$Text, [string]$Expected, [string]$Verdict, [string]$Reason, [string]$Form, [string]$Lang, [string]$Kind = '') {
    $c = $script:ctx
    $outClass = $Class; $inner = ''; $edit = ''
    if ($c.Gen) { $inner = $Class; $outClass = 'generated'; $edit = $c.Gen }
    $hits.Add([pscustomobject]@{
            repo = $c.Repo; file = $c.Rel; line = $Line; class = $outClass; innerClass = $inner; form = $Form; lang = $Lang
            text = (Limit $Text); expected = (Limit $Expected); verdict = $Verdict; reason = $Reason; editThrough = $edit
        })
}

function Get-LinkDescription([string]$tail, [bool]$isJson) {
    if ($isJson) { $q = [regex]::Match($tail, '(?<!\\)"'); if ($q.Success) { $tail = $tail.Substring(0, $q.Index) } }
    $t = $tail
    $cut = [regex]::Match($t, '<\\?/(?:li|td|p|div|dd|dt|h[1-6])\b|<br\b|<a\b', 'IgnoreCase')
    if ($cut.Success) { $t = $t.Substring(0, $cut.Index) }
    $t = $t -replace '^[\s*_`]+', ''
    if ($t -match '^\s*\|') { $t = ($t -replace '^\s*\|', ''); $bar = $t.IndexOf('|'); if ($bar -ge 0) { $t = $t.Substring(0, $bar) } }
    elseif ($t -match '^\s*(?:[-\u2013\u2014:]|--)\s+') { $t = $t -replace '^\s*(?:[-\u2013\u2014:]|--)\s+', '' }
    else { return '' }
    $plain = ConvertTo-Plain $t
    return $plain
}

function Add-LinkHits([int]$Line, [string]$label, [string]$href, [string[]]$descs, [string[]]$descLangs, [string]$Raw, [bool]$isJson) {
    $c = $script:ctx
    $labelPlain = ConvertTo-Plain $label
    $labelSpans = @(Find-Spans $labelPlain | Where-Object { $_.Kind -eq 'name' })
    $form = if ($labelSpans.Count -gt 0) { $labelSpans[0].Form } else { 'none' }
    $lang = Get-Lang $labelPlain $c.Hint
    $escName = Get-Escaped $fullName $c.Ext $Raw
    $targetProblem = Get-TargetProblem $href
    $isUrlLabel = $labelPlain -match '(?i)^(?:https?://|www\.)|serzhyale\.github\.io|github\.com/'
    $root = Test-RootLink $href
    if ($isUrlLabel) {
        Add-Hit $Line 'technical' $labelPlain 'n/a' $(if ($targetProblem) { 'mismatch' } else { 'info' }) $(if ($targetProblem) { $targetProblem } else { 'the link text is the address itself' }) 'url-label' $lang
    }
    elseif ($form -in 'full', 'compact') {
        Add-Hit $Line 'link-label' $labelPlain $fullName $(if ($targetProblem) { 'mismatch' } else { 'ok' }) $(if ($targetProblem) { $targetProblem } else { "label carries the $form name" }) $form $lang
    }
    elseif ($href -and -not $rxProductUrl.IsMatch($href) -and $form -notin 'legacy-windows', 'lite-visible', 'and-form') {
        # the link leads elsewhere: the name in its label is a mention, judged as running text (and by the name-form rows), not as a link to the product
        Add-Hit $Line 'name-only' $labelPlain 'n/a' 'info' 'the link does not lead to the product; the name is a mention in its label' $form $lang
    }
    elseif ($form -eq 'none' -and -not $root -and $href -notmatch '(?i)^$') {
        # a deep link to a topic page: the label names the topic, not the product
        Add-Hit $Line 'link-label' $labelPlain 'n/a (deep link: the label names the topic)' $(if ($targetProblem) { 'mismatch' } else { 'ok' }) $(if ($targetProblem) { $targetProblem } else { 'deep link; the label names the topic' }) 'deep-link' $lang
    }
    elseif ($form -eq 'none' -and ($script:anchorAttrs -match '(?i)class\s*=\s*\\?["''][^"'']*\b(?:btn|button|cta)\b' -or $rxGenericLabel.IsMatch($labelPlain))) {
        Add-Hit $Line 'link-label' $labelPlain $fullName $(if ($targetProblem) { 'mismatch' } else { 'info' }) $(if ($targetProblem) { $targetProblem } else { 'call-to-action or generic repository link; the name belongs to the card title beside it (not checked here)' }) 'cta' $lang
    }
    else {
        $why = switch ($form) {
            'legacy-windows' { "legacy name 'Fast Media Sorter for Windows' (PRODUCT_NAMING section 1)" }
            'lite-visible' { "visible 'Lite' form (PRODUCT_NAMING section 3 rule 4)" }
            'and-form' { "the name is written with 'and' instead of '&' (PRODUCT_NAMING section 3 rule 1)" }
            'none' { 'the label does not carry the product name beside a link to the product' }
            default { "label form '$form' is not the first-mention name" }
        }
        if ($labelPlain -match '^(?i)Fast\s?Media\s?Sorter$') { $why = "bare '$labelPlain' is not the first-mention name" }
        if ($targetProblem) { $why += '; ' + $targetProblem }
        Add-Hit $Line 'link-label' $labelPlain $escName 'mismatch' $why $form $lang
    }
    for ($i = 0; $i -lt $descs.Count; $i++) {
        $d = $descs[$i]
        if ([string]::IsNullOrWhiteSpace($d)) { continue }
        # the words after a link to a topic page describe that page, not the product
        if ($href -and -not $root) { continue }
        $dl = if ($descLangs[$i]) { $descLangs[$i] } else { Get-Lang $d $c.Hint }
        $exp = if ($blurb.ContainsKey($dl)) { $blurb[$dl] } else { $blurb['en'] }
        if ($rxPlaceholder.IsMatch($d)) {
            Add-Hit $Line 'link-description' $d $exp 'info' 'template placeholder: the value lives in the generator copy files (see the generated hits)' 'placeholder' $dl
            continue
        }
        $problems = Get-ClaimProblems $d
        $ok = (Get-Norm $d).Contains((Get-Norm $exp))
        if ($ok -and $problems.Count -eq 0) { Add-Hit $Line 'link-description' $d $exp 'ok' 'the approved blurb, verbatim' 'blurb' $dl }
        else {
            $why = if (-not $ok) { 'not the approved blurb (positioning.blurb.' + $dl + ')' } else { '' }
            if ($problems.Count -gt 0) { $why = (@($why) + $problems | Where-Object { $_ }) -join '; ' }
            Add-Hit $Line 'link-description' $d $exp 'mismatch' $why 'description' $dl
        }
    }
}

function Add-TextHit([int]$Line, [string]$ln, $span, [bool]$isJson) {
    $c = $script:ctx
    $form = $span.Form
    $spanText = $span.Text
    $hintLang = $c.Hint
    # aliases, identifiers, URLs: technical
    if ($span.Kind -eq 'url') {
        $tp = Get-TargetProblem $spanText
        Add-Hit $Line 'technical' (Limit (ConvertTo-Plain $ln) 200) 'n/a' $(if ($tp) { 'mismatch' } else { 'info' }) $(if ($tp) { $tp } else { 'address of the product (AC-11: kept as is)' }) $form (Get-Lang $ln $hintLang)
        return
    }
    if ($span.Kind -eq 'alias') {
        $why = switch ($form) {
            'alias-FMS_W' { 'retired alias FMS_W (PRODUCT_NAMING section 1); FMS_Win in new text' }
            'alias-FMS_Win' { 'internal alias, allowed in technical text' }
            'file-name' { 'a file or prompt name' }
            default { 'identifier or frozen anchor: kept as is' }
        }
        Add-Hit $Line 'technical' (Limit (ConvertTo-Plain $ln) 200) $(if ($form -eq 'alias-FMS_W') { 'FMS_Win' } else { 'n/a' }) 'info' $why $form 'en'
        return
    }
    if ($c.IsTest) {
        Add-Hit $Line 'technical' (Limit (ConvertTo-Plain $ln) 200) 'n/a' 'info' 'fixture, test vector or test source' $form 'en'
        return
    }
    if ($codeExt.Contains($c.Ext)) {
        Add-Hit $Line 'technical' (Limit (ConvertTo-Plain $ln) 200) 'n/a' 'info' 'source code, comment or build file' $form 'en'
        return
    }
    # the sentence around the name
    $marked = $ln.Substring(0, $span.Start) + [string][char]1 + $ln.Substring($span.End)
    if ($marked.Length -gt 1600) {
        # a generated search index or a minified line: judge a window, not the whole line
        $from = [Math]::Max(0, $span.Start - 500)
        $marked = $marked.Substring($from, [Math]::Min(1000, $marked.Length - $from))
    }
    $plain = ConvertTo-Plain $marked
    $idx = $plain.IndexOf([char]1)
    if ($idx -lt 0) { $plain = ConvertTo-Plain $ln; $idx = 0 }
    $nameShown = ConvertTo-Plain $spanText
    $start = 0
    for ($j = $idx - 1; $j -ge 0; $j--) {
        if (($plain[$j] -eq '.' -or $plain[$j] -eq '!' -or $plain[$j] -eq '?' -or $plain[$j] -eq [char]0x3002) -and ($j + 1 -ge $plain.Length -or $plain[$j + 1] -eq ' ')) { $start = $j + 1; break }
    }
    $end = $plain.Length
    $end1 = $plain.Length
    for ($j = $idx; $j -lt $plain.Length; $j++) {
        if (($plain[$j] -eq '.' -or $plain[$j] -eq '!' -or $plain[$j] -eq '?' -or $plain[$j] -eq [char]0x3002) -and ($j + 1 -ge $plain.Length -or $plain[$j + 1] -eq ' ')) { $end1 = $j + 1; break }
    }
    $end = $end1
    # a following sentence that continues the subject ("It ..", "The program ..") is part of the claim
    if ($end1 -lt $plain.Length -and $rxContinuation.IsMatch($plain.Substring($end1))) {
        for ($j = $end1 + 1; $j -lt $plain.Length; $j++) {
            if (($plain[$j] -eq '.' -or $plain[$j] -eq '!' -or $plain[$j] -eq '?' -or $plain[$j] -eq [char]0x3002) -and ($j + 1 -ge $plain.Length -or $plain[$j + 1] -eq ' ')) { $end = $j + 1; break }
        }
        if ($end -eq $end1) { $end = $plain.Length }
    }
    $first = $plain.Substring($start, $end - $start).Trim()
    $sentence = $first.Replace([string][char]1, $nameShown)
    $own = ($plain.Substring($start, $end1 - $start)) -replace [string][char]1, ''
    $words = @($own -split '\s+' | Where-Object { $_ }).Count
    $lang = Get-Lang $sentence $hintLang
    $heading = $ln -match '^\s*(?:#{1,6}\s|<h[1-6]\b|title\s*:|name\s*:|\s*"(?:title|name)"\s*:)'
    $legacy = switch ($form) {
        'legacy-windows' { "legacy name 'Fast Media Sorter for Windows' (PRODUCT_NAMING section 1)" }
        'lite-visible' { "visible 'Lite' form (PRODUCT_NAMING section 3 rule 4; right only where it names the Store or winget package)" }
        'and-form' { "the name is written with 'and' instead of '&' (PRODUCT_NAMING section 3 rule 1)" }
        default { '' }
    }
    $isProse = $true
    if ($dataExt.Contains($c.Ext) -and $words -lt 4) { $isProse = $false }
    if (-not $isProse) {
        Add-Hit $Line 'technical' (Limit $sentence 200) 'n/a' 'info' 'a name inside a data field' $form $lang
        return
    }
    $problems = Get-ClaimProblems $sentence
    if ($form -eq 'old-companion') {
        $problems.Add('names the Windows program only as a folder-share companion (it is the media and file manager; local sharing and scheduled file operations are two further parts)')
    }
    if ($heading -or $words -lt 5) {
        $note = if ($legacy) { $legacy } else { 'name with no description around it' }
        if ($form -eq 'old-companion') {
            Add-Hit $Line 'feature-claim' $sentence $fullName 'mismatch' ($problems -join '; ') $form $lang
        }
        else {
            Add-Hit $Line 'name-only' $sentence $(if ($legacy) { Get-Escaped $fullName $c.Ext $ln } else { 'n/a' }) 'info' $note $form $lang
        }
        return
    }
    $note2 = if ($legacy) { $legacy } else { '' }
    if ($problems.Count -gt 0) {
        $all = (@($problems) + @($note2) | Where-Object { $_ }) -join '; '
        Add-Hit $Line 'feature-claim' $sentence 'state what the product does per the capability register (released only); category: media and file manager (positioning.lead)' 'mismatch' $all $form $lang
    }
    else {
        # a consistent claim that still carries an old name form is not gating: it goes to the name-form table of the packet
        Add-Hit $Line 'feature-claim' $sentence $(if ($note2) { Get-Escaped $fullName $c.Ext $ln } else { 'consistent with the capability register' }) $(if ($note2) { 'info' } else { 'ok' }) $(if ($note2) { $note2 + '; the claim itself is consistent' } else { 'the claim is consistent with the register' }) $form $lang
    }
}

function Test-Inside($ranges, [int]$pos) {
    foreach ($r in $ranges) { if ($pos -ge $r[0] -and $pos -lt $r[1]) { return $true } }
    return $false
}

function Read-Line([string]$ln, [int]$lineNo) {
    $c = $script:ctx
    $isJson = $dataExt.Contains($c.Ext)
    $script:keyEnd = 0; $script:hitPos = 0
    if ($isJson) { $km = [regex]::Match($ln, '^\s*"((?:[^"\\]|\\.)*)"\s*:\s*"'); if ($km.Success) { $script:keyEnd = $km.Groups[1].Index + $km.Groups[1].Length } }
    $consumed = New-Object System.Collections.Generic.List[object]
    foreach ($m in $rxAnchor.Matches($ln)) {
        $href = ''
        $hm = $rxHref.Match($m.Groups[1].Value)
        if ($hm.Success) { $href = $hm.Groups[1].Value }
        $inner = $m.Groups[2].Value
        $innerSpans = @(Find-Spans (ConvertTo-Plain $inner) | Where-Object { $_.Kind -eq 'name' })
        if (-not $rxProductUrl.IsMatch($href) -and $innerSpans.Count -eq 0) { continue }
        $consumed.Add(@($m.Index, ($m.Index + $m.Length)))
        # label and descriptions
        $descs = New-Object System.Collections.Generic.List[string]
        $descLangs = New-Object System.Collections.Generic.List[string]
        $labelHtml = $inner
        $spans = @($rxSpan.Matches($inner))
        # a button's spans are parts of its caption (text, icon, screen-reader note), not a description of the product
        if ($m.Groups[1].Value -match '(?i)class\s*=\s*\\?["''][^"'']*\b(?:btn|button|cta)\b') { $spans = @() }
        if ($spans.Count -gt 0) {
            foreach ($sp in $spans) {
                $dl = ''
                $dm = [regex]::Match($sp.Groups[1].Value, 'data-l\w*\s*=\s*\\?["'']([a-zA-Z-]+)')
                if ($dm.Success) { $dl = $dm.Groups[1].Value.ToLowerInvariant(); if ($dl -eq 'ua') { $dl = 'uk' } }
                $descs.Add((ConvertTo-Plain $sp.Groups[2].Value)); $descLangs.Add($dl)
            }
            $labelHtml = $rxSpan.Replace($inner, ' ')
        }
        $bm = $rxLabelB.Match($labelHtml)
        if ($bm.Success) { $labelHtml = $bm.Groups[2].Value }
        $label = ConvertTo-Plain $labelHtml
        if ($spans.Count -eq 0) {
            $d = Get-LinkDescription $ln.Substring($m.Index + $m.Length) $isJson
            if ($d) { $descs.Add($d); $descLangs.Add('') }
        }
        $script:hitPos = $m.Index
        $script:anchorAttrs = $m.Groups[1].Value
        Add-LinkHits $lineNo $label $href $descs.ToArray() $descLangs.ToArray() $ln $isJson
    }
    foreach ($m in $rxMd.Matches($ln)) {
        if (Test-Inside $consumed $m.Index) { continue }
        if ($m.Index -gt 0 -and $ln[$m.Index - 1] -eq '!') { continue }   # an image: its alt text is running text, not a link label
        $label = $m.Groups[1].Value; $href = $m.Groups[2].Value
        $labelSpans = @(Find-Spans (ConvertTo-Plain $label) | Where-Object { $_.Kind -eq 'name' })
        if (-not $rxProductUrl.IsMatch($href) -and $labelSpans.Count -eq 0) { continue }
        $consumed.Add(@($m.Index, ($m.Index + $m.Length)))
        $descs = @(); $dls = @()
        $d = Get-LinkDescription $ln.Substring($m.Index + $m.Length) $isJson
        if ($d) { $descs = @($d); $dls = @('') }
        $script:hitPos = $m.Index
        $script:anchorAttrs = ''
        Add-LinkHits $lineNo $label $href $descs $dls $ln $isJson
    }
    # what is left outside the links
    $left = @(Find-Spans $ln | Where-Object { -not (Test-Inside $consumed $_.Start) })
    $urls = @($left | Where-Object { $_.Kind -eq 'url' })
    $names = @($left | Where-Object { $_.Kind -eq 'name' -and $_.Form -ne 'share-manager' -and $_.Form -ne 'server-edition' })
    $paired = New-Object System.Collections.Generic.HashSet[object]
    foreach ($n in $names) {
        $u = $urls | Where-Object { [Math]::Abs($_.Start - $n.End) -le 80 -or [Math]::Abs($n.Start - $_.End) -le 80 } | Select-Object -First 1
        if ($u -and -not $paired.Contains($u)) {
            [void]$paired.Add($n); [void]$paired.Add($u)
            $script:hitPos = $n.Start
            $script:anchorAttrs = ''
            Add-LinkHits $lineNo $n.Text $u.Text @() @() $ln $isJson
        }
    }
    foreach ($s in $left) {
        if ($paired.Contains($s)) { continue }
        $script:hitPos = $s.Start
        Add-TextHit $lineNo $ln $s $isJson
    }
}

# ---------------------------------------------------------------- scan
$summary = New-Object System.Collections.Generic.List[object]
$missingRoots = New-Object System.Collections.Generic.List[string]
$sw = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($root in $roots) {
    $name = [string](Get-Prop $root 'name' '')
    $path = [string](Get-Prop $root 'path' '')
    if (-not $name -or -not $path) { $missingRoots.Add("(unnamed root: name and path are required)"); continue }
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { $missingRoots.Add("$name ($path)"); continue }
    $genRules = @(Get-Prop $root 'generated' @())
    $extraExclude = @(Get-Prop $root 'exclude' @() | ForEach-Object { [regex]::new([string]$_, 'IgnoreCase') })
    $rootLen = ([System.IO.Path]::GetFullPath($path)).TrimEnd('\').Length
    $before = $hits.Count
    $files = 0; $matched = 0
    $stack = New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
    $stack.Push([System.IO.DirectoryInfo]::new($path))
    while ($stack.Count -gt 0) {
        $d = $stack.Pop()
        try {
            foreach ($sd in $d.EnumerateDirectories()) {
                if ($excludeDirs.Contains($sd.Name)) { continue }
                if ($sd.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
                $stack.Push($sd)
            }
            $fileList = @($d.EnumerateFiles())
        }
        catch { continue }
        foreach ($fi in $fileList) {
            if ($skipExt.Contains($fi.Extension) -or $fi.Length -gt $maxBytes -or $fi.Name -match '\.min\.(?:js|css)$' -or $fi.Name -eq 'package-lock.json') { continue }
            $rel = $fi.FullName.Substring($rootLen).TrimStart('\').Replace('\', '/')
            if ($extraExclude | Where-Object { $_.IsMatch($rel) }) { continue }
            $files++
            $nameHit = [regex]::IsMatch($rel, '(?i)fms[-_]lite')
            try { $text = Get-FileText $fi } catch { continue }
            if ($null -eq $text) { continue }
            if (-not $combined.IsMatch($text) -and -not $nameHit) { continue }
            $matched++
            $lines = $text -split '\r?\n'
            $gen = ''
            foreach ($g in $genRules) { if ([regex]::IsMatch($rel, [string]$g.match)) { $gen = [string](Get-Prop $g 'editThrough' 'the generator of this file'); break } }
            if (-not $gen) {
                $head = ($lines | Select-Object -First 8) -join "`n"
                if ($head -match '(?i)auto-?generated|@generated|do not edit|generated (?:by|from|file)|this file is generated') { $gen = 'the generator named in the file header (not identified by the scan)' }
            }
            $script:ctx = @{
                Repo = $name; Rel = $rel; Ext = $fi.Extension.ToLowerInvariant(); Gen = $gen
                Hint = (Get-HintLang $rel); IsTest = $testPathRx.IsMatch($rel)
            }
            if ($nameHit) { Add-Hit 0 'technical' $rel 'n/a' 'info' 'a file name that carries an old alias (fms-lite)' 'file-name' 'en' }
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if (-not $combined.IsMatch($lines[$i])) { continue }
                Read-Line $lines[$i] ($i + 1)
            }
        }
    }
    $summary.Add([pscustomobject]@{ Repo = $name; Files = $files; FilesWithHits = $matched; Hits = ($hits.Count - $before) })
}

# ---------------------------------------------------------------- output
$ordered = @($hits | Sort-Object repo, file, line, class)
$judged = @('link-label', 'link-description', 'feature-claim')
$mismatches = @($ordered | Where-Object { $_.verdict -eq 'mismatch' })
$gating = @($mismatches | Where-Object { ($_.class -in $judged) -or ($_.class -eq 'generated' -and $_.innerClass -in $judged) -or $_.reason -like '*AC-11*' })

if (-not $NoWrite) {
    $dir = Split-Path $OutFile -Parent
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
    $jopt = [System.Text.Json.JsonSerializerOptions]::new()
    $jopt.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $out = foreach ($h in $ordered) {
        $dict = [System.Collections.Generic.Dictionary[string, object]]::new()
        foreach ($k in 'repo', 'file', 'line', 'class', 'innerClass', 'form', 'lang', 'text', 'expected', 'verdict', 'reason', 'editThrough') { $dict[$k] = $h.$k }
        [System.Text.Json.JsonSerializer]::Serialize($dict, $jopt)
    }
    [System.IO.File]::WriteAllText($OutFile, ((@($out) -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-Cell([string]$s) { return ($s -replace '\|', '\|' -replace '\r?\n', ' ').Trim() }
function Get-Proposal($h) {
    if ($h.verdict -eq 'info') { return '' }
    $c = if ($h.class -eq 'generated') { $h.innerClass } else { $h.class }
    switch ($c) {
        'link-label' { return "label: ``$($h.expected)``" }
        'link-description' { return "description: ``$($h.expected)``" }
        'feature-claim' {
            if ($h.form -eq 'old-companion') { return "name the program ``$($h.expected)``; the sharing part is the local sharing service, and scheduled file operations are a separate part installed together with the Share Manager (positioning.components)" }
            return "rewrite from positioning.lead / the register; $($h.reason)"
        }
        default { return $h.expected }
    }
}
function Write-Packet($repoName, $repoHits, $rootObj) {
    $L = New-Object System.Collections.Generic.List[string]
    $L.Add("# Hand-off packet: $repoName")
    $L.Add('')
    $L.Add("Source: ``tools/Get-ProductMentions.ps1`` (specification 273, T-9), scanned $(Get-Date -Format 'yyyy-MM-dd'). **Proposals only**: nothing here was applied in the sibling repository (owner decision Q273-7 = A). Every edit goes through that repository's own rules and tickets. Raw hits: ``mentions.jsonl`` beside this file.")
    $L.Add('')
    $note = [string](Get-Prop $rootObj 'note' '')
    if ($note) { $L.Add($note); $L.Add('') }
    $cnt = $repoHits | Group-Object { if ($_.class -eq 'generated') { "generated ($($_.innerClass))" } else { $_.class } } | Sort-Object Name
    $L.Add('Hits by class: ' + (($cnt | ForEach-Object { "$($_.Name) $($_.Count)" }) -join ', ') + '.')
    $L.Add('')
    $mm = @($repoHits | Where-Object { $_.verdict -eq 'mismatch' })
    if ($mm.Count -gt 0) {
        $L.Add("## Mismatches to change ($($mm.Count) hits)")
        $L.Add('')
        $L.Add('| File : line | Class | Current text | Proposed replacement | Edit through |')
        $L.Add('| --- | --- | --- | --- | --- |')
        $groups = $mm | Group-Object { "$($_.class)|$($_.innerClass)|$($_.text)|$($_.editThrough)|$($_.reason)" }
        foreach ($g in ($groups | Sort-Object { $_.Group[0].file }, { $_.Group[0].line })) {
            $h = $g.Group[0]
            $where = @($g.Group | Select-Object -First 3 | ForEach-Object { "``$($_.file):$($_.line)``" }) -join ', '
            if ($g.Count -gt 3) { $where += " (+$($g.Count - 3) more)" }
            $cls = if ($h.class -eq 'generated') { "generated ($($h.innerClass))" } else { $h.class }
            $edit = if ($h.editThrough) { $h.editThrough } else { 'the file itself' }
            $L.Add("| $(ConvertTo-Cell $where) | $cls | $(ConvertTo-Cell $h.text) | $(ConvertTo-Cell (Get-Proposal $h)) - why: $(ConvertTo-Cell $h.reason) | $(ConvertTo-Cell $edit) |")
        }
        $L.Add('')
    }
    $refresh = @($repoHits | Where-Object { $_.verdict -eq 'info' -and $_.reason -match "legacy name|visible 'Lite' form|instead of '&'" })
    if ($refresh.Count -gt 0) {
        $L.Add("## Name forms to refresh ($($refresh.Count) hits, not gating)")
        $L.Add('')
        $L.Add('| File : line | Current text | Proposed | Edit through |')
        $L.Add('| --- | --- | --- | --- |')
        foreach ($g in ($refresh | Group-Object { "$($_.text)|$($_.editThrough)" } | Sort-Object { $_.Group[0].file })) {
            $h = $g.Group[0]
            $where = @($g.Group | Select-Object -First 3 | ForEach-Object { "``$($_.file):$($_.line)``" }) -join ', '
            if ($g.Count -gt 3) { $where += " (+$($g.Count - 3) more)" }
            $L.Add("| $(ConvertTo-Cell $where) | $(ConvertTo-Cell $h.text) | ``$($h.expected)`` - $(ConvertTo-Cell $h.reason) | $(ConvertTo-Cell $(if ($h.editThrough) { $h.editThrough } else { 'the file itself' })) |")
        }
        $L.Add('')
    }
    $okc = @($repoHits | Where-Object { $_.verdict -eq 'ok' }).Count
    $infoGroups = @($repoHits | Where-Object { $_.verdict -eq 'info' } | Group-Object { "$($_.class): $($_.reason)" } | Sort-Object Count -Descending)
    $L.Add('## Left as is, with the reason')
    $L.Add('')
    if ($okc -gt 0) { $L.Add("- $okc hit(s) already say what the register says (verdict ok).") }
    foreach ($g in $infoGroups) { $L.Add("- $($g.Count) x $($g.Name)") }
    $L.Add('')
    [System.IO.File]::WriteAllText((Join-Path $PacketDir "$repoName.md"), (($L -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

if (-not $NoWrite -and -not $NoPackets) {
    if (-not (Test-Path -LiteralPath $PacketDir)) { [void](New-Item -ItemType Directory -Path $PacketDir) }
    foreach ($g in ($ordered | Group-Object repo)) {
        $rootObj = $roots | Where-Object { (Get-Prop $_ 'name' '') -eq $g.Name } | Select-Object -First 1
        Write-Packet $g.Name @($g.Group) $rootObj
    }
}

# digest
Write-Host ''
Write-Host ("{0,-24} {1,6} {2,6} {3,6}" -f 'repository', 'files', 'hits', 'mismatch')
foreach ($s in $summary) {
    $mc = @($mismatches | Where-Object { $_.repo -eq $s.Repo }).Count
    Write-Host ("{0,-24} {1,6} {2,6} {3,6}" -f $s.Repo, $s.Files, $s.Hits, $mc)
}
Write-Host ''
Write-Host 'by class: ' -NoNewline
Write-Host ((($ordered | Group-Object class | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" })) -join ', ')
Write-Host ('by verdict: ' + ((($ordered | Group-Object verdict | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" })) -join ', '))
if ($mismatches.Count -gt 0) {
    Write-Host ''
    Write-Host "first $([Math]::Min($Top, $mismatches.Count)) of $($mismatches.Count) mismatch(es):"
    foreach ($h in ($mismatches | Select-Object -First $Top)) {
        $cls = if ($h.class -eq 'generated') { "generated/$($h.innerClass)" } else { $h.class }
        Write-Host ("  {0}: {1}:{2} [{3}] {4}" -f $h.repo, $h.file, $h.line, $cls, (Limit $h.reason 110))
    }
}
foreach ($m in $missingRoots) { Write-Host "  root not found, not scanned: $m" -ForegroundColor Magenta }
if (-not $NoWrite) { Write-Host ''; Write-Host "written: $OutFile ($($ordered.Count) hits)$(if (-not $NoPackets) { "; packets in $PacketDir" })" }
Write-Host ("scan time {0:N1}s" -f $sw.Elapsed.TotalSeconds)

$repoCount = $summary.Count
if ($gating.Count -gt 0) {
    Exit-Verdict $check 1 "$($gating.Count) mismatch(es) in link-label / link-description / feature-claim across $repoCount repositories; $($ordered.Count) hits in all"
}
if ($missingRoots.Count -gt 0) {
    Exit-Verdict $check 2 "$($missingRoots.Count) configured root(s) not found, so not every sibling was inspected; $repoCount scanned repositories show no mismatch"
}
if ($repoCount -eq 0) { Exit-Verdict $check 2 'no repository was scanned' }
Exit-Verdict $check 0 "$($ordered.Count) hits in $repoCount repositories, no mismatch in link-label / link-description / feature-claim"
