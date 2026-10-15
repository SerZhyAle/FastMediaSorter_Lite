<#
.SYNOPSIS
    Validate the maintained documentation corpus against DOC-INTERNAL-QUALITY.
.DESCRIPTION
    Registry coverage includes non-vendored Markdown and HTML. Source Markdown is checked for
    links, anchors, available assets, house style and offline safety. Published HTML and site render
    synchronization remain owned by Build-SitePages -Check (release preflight).
#>
[CmdletBinding()]
param([string]$Root = '')
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'Test-DocRegistry' 2 'the check could not complete' }
$Root = [IO.Path]::GetFullPath($Root)
Write-Subject 'Test-DocRegistry' "maintained documentation, registry and source facts; $(Get-TreeLabel $Root)"
$registry = Join-Path $Root 'docs/DOCUMENT_REGISTRY.jsonl'
$policyFile = Join-Path $Root 'docs/document-registry-policy.json'
if (!(Test-Path $registry) -or !(Test-Path $policyFile)) { Exit-Verdict 'Test-DocRegistry' 2 'registry or policy missing' }
$policy = Get-Content $policyFile -Raw | ConvertFrom-Json
$problems = [Collections.Generic.List[string]]::new()
function Problem([string]$message) { $problems.Add($message) }
foreach ($ignore in $policy.ignore) {
    if (!$ignore.prefix -or !$ignore.reason) { Problem 'ignore requires a prefix and reason' }
}
function Ignored([string]$path) {
    foreach ($ignore in $policy.ignore) { if ($path.StartsWith($ignore.prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
    foreach ($directory in $policy.ignoredDirectoryNames) {
        if (!$directory.name -or !$directory.reason) { Problem 'directory ignore requires name and reason'; continue }
        if ($path -match ('(^|/)' + [regex]::Escape($directory.name) + '/')) { return $true }
    }
    return $false
}
# Inspect the maintained files on disk, including new documents, without invoking Git.
$files = @(Get-WorkspaceFiles $Root)
$disk = @($files | Where-Object { ([IO.Path]::GetExtension($_) -in $policy.extensions -or $_ -in $policy.extraFiles) -and !(Ignored $_) } | Sort-Object -Unique)
$seen = @{}
$records = @(Get-Content $registry | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
if (!$records.Count) { Exit-Verdict 'Test-DocRegistry' 2 'empty registry' }
foreach ($r in $records) {
    foreach ($field in 'path','topic','area','triggers','role') {
        if (!$r.$field) { Problem "record '$($r.path)' lacks $field" }
    }
    if (!$r.path) { continue }
    if ($seen.ContainsKey($r.path)) { Problem "duplicate record: $($r.path)" }
    $seen[$r.path] = $r
    if ($r.path -match '(^/|\\|(^|/)\.\.(/|$)|:)') { Problem "non-portable path: $($r.path)"; continue }
    if ($r.role -notin 'source','render') { Problem "invalid role: $($r.path)" }
    if ($r.role -eq 'render') {
        if (!$r.generator) { Problem "render lacks generator: $($r.path)" }
        elseif (!(Test-Path -LiteralPath (Join-Path $Root (($r.generator -split '\s+')[0])) -PathType Leaf)) { Problem "render generator missing: $($r.path)" }
    }
    if ($r.triggers -isnot [array]) { Problem "triggers must be an array: $($r.path)" }
    if (!(Test-Path -LiteralPath (Join-Path $Root $r.path) -PathType Leaf)) { Problem "declared file missing: $($r.path)" }
    if ($r.path -notin $disk) { Problem "declared file outside maintained inventory: $($r.path)" }
}
foreach ($path in $disk) { if (!$seen.ContainsKey($path)) { Problem "undeclared document: $path" } }
function Without-Code([string]$text, [switch]$Inline) {
    $kept = [Collections.Generic.List[string]]::new()
    $fence = ''; $length = 0
    foreach ($line in ($text -split '\r?\n')) {
        if ($line -match '^ {0,3}(`{3,}|~{3,})') {
            $mark = $Matches[1]
            if (!$fence) { $fence = $mark.Substring(0, 1); $length = $mark.Length; continue }
            if ($mark.StartsWith($fence) -and $mark.Length -ge $length -and $line.Trim() -eq $mark) { $fence = ''; continue }
        }
        if (!$fence) { $kept.Add($line) }
    }
    $text = $kept -join "`n"
    $text = [regex]::Replace($text, '(?m)^(?: {4}|\t).*(?:\r?\n|$)', '')
    if ($Inline) { $text = [regex]::Replace($text, '(`+)[^\r\n]*?\1', '') }
    return $text
}
$anchorCache = @{}
function Anchors([string]$path) {
    if ($anchorCache.ContainsKey($path)) { return $anchorCache[$path] }
    $text = Get-Content -LiteralPath $path -Raw
    $anchors = [Collections.Generic.List[string]]::new()
    $duplicates = @{}
    if ([IO.Path]::GetExtension($path) -eq '.md') {
        $clean = Without-Code $text
        $headings = [regex]::Matches($clean, '(?m)^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$|^([^\r\n]+)\r?\n {0,3}(?:=+|-+)\s*$')
        foreach ($m in $headings) {
            $title = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
            $title = [regex]::Replace($title, '!?(\[([^\]]+)\])\([^)]+\)', '$2')
            $title = [Net.WebUtility]::HtmlDecode([regex]::Replace($title, '<[^>]+>', ''))
            $slug = [regex]::Replace($title.ToLowerInvariant(), '[^\p{L}\p{N}\p{M}_\-\s]', '') -replace '\s', '-'
            $base = $slug
            if ($duplicates.ContainsKey($base)) { $duplicates[$base]++; $slug += '-' + $duplicates[$base] } else { $duplicates[$base] = 0 }
            $anchors.Add($slug)
        }
    }
    foreach ($m in [regex]::Matches($text, '(?i)\b(?:id|name)\s*=\s*["'']([^"'']+)["'']')) { $anchors.Add($m.Groups[1].Value) }
    $anchorCache[$path] = $anchors
    return $anchors
}
$linkCount = 0
function Check-Link([string]$source, [string]$url, [bool]$image) {
    $script:linkCount++
    $url = $url.Trim() -replace '^<|>$', ''
    if ($url -match '^(https://|mailto:|tel:)') {
        if ($image) { Problem "${source}: remote image prevents offline reading: $url" }
        return
    }
    if ($url -match '^[a-zA-Z][\w+.-]*:|^//') { Problem "${source}: unsafe or nonportable link: $url"; return }
    $split = $url -split '#', 2
    $target = [Uri]::UnescapeDataString(($split[0] -split '\?', 2)[0])
    $full = if (!$target) { Join-Path $Root $source } elseif ($target.StartsWith('/')) { Join-Path $Root $target.TrimStart('/') } else { Join-Path (Split-Path (Join-Path $Root $source) -Parent) $target }
    $full = [IO.Path]::GetFullPath($full)
    if (!$full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Problem "${source}: link outside repository: $url"; return }
    $rel = [IO.Path]::GetRelativePath($Root, $full).Replace('\','/')
    if ($rel -match '^docs/specifications(/|$)') { Problem "${source}: owner-local specification link: $url"; return }
    if (!(Test-Path -LiteralPath $full)) { Problem "${source}: missing target: $url"; return }
    if ($image -and $rel -notin $files) { Problem "${source}: asset outside maintained inventory: $url" }
    if ((Test-Path -LiteralPath $full -PathType Leaf) -and $rel -notin $files) { Problem "${source}: target outside maintained inventory: $url" }
    if ($split.Count -gt 1 -and $split[1]) {
        if ($split[1] -match '^L(\d+)(?:-L(\d+))?$' -and [IO.Path]::GetExtension($full) -notin '.md','.html') {
            $lineTotal = @(Get-Content -LiteralPath $full).Count
            $start = [int]$Matches[1]; $end = if ($Matches[2]) { [int]$Matches[2] } else { $start }
            if ($start -lt 1 -or $end -lt $start -or $end -gt $lineTotal) { Problem "${source}: invalid source line bookmark: $url" }
            return
        }
        if (!(Test-Path -LiteralPath $full -PathType Leaf)) { $full = Join-Path $full 'README.md' }
        if (!(Test-Path -LiteralPath $full -PathType Leaf) -or [Uri]::UnescapeDataString($split[1]) -cnotin @(Anchors $full)) { Problem "${source}: missing anchor: $url" }
    }
}
foreach ($r in $records) {
    if ($r.role -ne 'source' -or !($r.path -like '*.md') -or !(Test-Path (Join-Path $Root $r.path))) { continue }
    $text = Get-Content -LiteralPath (Join-Path $Root $r.path) -Raw
    $prose = Without-Code $text -Inline
    if ($prose -match '[\u2013\u2014\u2018\u2019\u201C\u201D\u2026]|\.{3}') { Problem "$($r.path): house style residue outside code" }
    if ($prose -match '(?i)<(?:script|iframe)\b|http://|@import|<link\b[^>]*fonts') { Problem "$($r.path): unsafe offline content" }
    $links = Without-Code $text -Inline
    foreach ($m in [regex]::Matches($links, '(!?)\[[^\]]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["''][^\r\n]*?["''])?\s*\)')) { Check-Link $r.path $m.Groups[2].Value ($m.Groups[1].Value -eq '!') }
    $definitions = @{}
    foreach ($m in [regex]::Matches($links, '(?m)^ {0,3}\[([^\]]+)\]:\s*(<[^>]+>|\S+)')) { $definitions[$m.Groups[1].Value.ToLowerInvariant()] = $m.Groups[2].Value }
    foreach ($m in [regex]::Matches($links, '(!?)\[([^\]]+)\]\[([^\]]*)\]')) {
        $id = if ($m.Groups[3].Value) { $m.Groups[3].Value } else { $m.Groups[2].Value }
        if (!$definitions.ContainsKey($id.ToLowerInvariant())) { Problem "$($r.path): undefined reference: $id" } else { Check-Link $r.path $definitions[$id.ToLowerInvariant()] ($m.Groups[1].Value -eq '!') }
    }
    # Shortcut references use the label as their definition key. Ignore bracket prose that has
    # no definition; explicit and collapsed references above must always have one.
    foreach ($m in [regex]::Matches($links, '(?<!!)(!?)\[([^\]]+)\](?![\[(:])')) {
        $id = $m.Groups[2].Value.ToLowerInvariant()
        if ($definitions.ContainsKey($id)) { Check-Link $r.path $definitions[$id] ($m.Groups[1].Value -eq '!') }
    }
    foreach ($m in [regex]::Matches($links, '(?i)<(a|img)\b[^>]*?\b(?:href|src)\s*=\s*["'']([^"'']+)["'']')) { Check-Link $r.path $m.Groups[2].Value ($m.Groups[1].Value -eq 'img') }
}
# Numeric claims in CLAUDE are source facts, not a hand-maintained second list.
$claudeFile = Join-Path $Root '.claude/rules/settings-window.md'
if (!(Test-Path -LiteralPath $claudeFile)) { $claudeFile = Join-Path $Root 'CLAUDE.md' }
$claude = Get-Content $claudeFile -Raw
$designer = Get-Content (Join-Path $Root 'src/Table_Form.Designer.vb') -Raw
$modern = Get-Content (Join-Path $Root 'src/Table_Form.ModernLayout.vb') -Raw
$sharedCount = [regex]::Matches($designer, 'Me\.Tab_Control\.Controls\.Add\(Me\.Tab_Page_\d+\)').Count
$extraCount = [regex]::Matches($modern, 'Tab_Control\.TabPages\.(?:Add|Insert)\(').Count
$numbers = @('zero','one','two','three','four','five','six','seven','eight','nine','ten','eleven','twelve')
if (!$sharedCount -or !$extraCount -or $sharedCount + $extraCount -ge $numbers.Count) { Problem 'settings page source model changed; update fact extraction' }
elseif ($claude -notmatch "same $($numbers[$sharedCount]) Designer pages" -or $claude -notmatch "layout of $($numbers[$sharedCount + $extraCount]) pages" -or $claude -notmatch "plus $($numbers[$extraCount]) that exist only there") { Problem 'settings-window rule: settings page counts drifted from source' }
$warningLists = foreach ($project in 'src/FastMediaSorter.vbproj','src/Modern/FastMediaSorter.Modern.vbproj') {
    [regex]::Matches((Get-Content (Join-Path $Root $project) -Raw), '<WarningsAsErrors>([^<]+)</WarningsAsErrors>') | ForEach-Object { $_.Groups[1].Value }
}
if (@($warningLists | Sort-Object -Unique).Count -ne 1 -or @($warningLists).Count -lt 3) { Problem 'CLAUDE.md: viewer WarningsAsErrors parity claim drifted' }
if ($problems.Count) {
    $problems | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'Test-DocRegistry' 1 "$($problems.Count) defect(s), $($records.Count) documents, $linkCount links"
}
Exit-Verdict 'Test-DocRegistry' 0 "$($records.Count) documents, $linkCount links, source facts agree"
