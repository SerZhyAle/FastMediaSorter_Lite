<#
.SYNOPSIS
    Prove that every visible text says the product's current name and that no old form survives
    without a written reason (ticket 111, docs/guides/PRODUCT_NAMING.md).
.DESCRIPTION
    Two judgements, both over the current files on disk (never Git history):

      1. FORBIDDEN forms - "FastMediaSorter LITE", "Fast Media Sorter LITE", "Fast Media Sorter Lite",
         the previous full name "Fast Media Sorter for Windows", a bare LITE / Lite word, and the exchange
         server's name cut short to a bare "Exchange Server" (owner rule 133 section 4). A hit is
         accepted only when a record of tools/product-naming-allow.jsonl covers it: a path glob, a
         regular expression the offending line must match, and a one-sentence reason. A record
         without a reason, a glob or a pattern is itself a failure, and so is a record that covers
         nothing (a stale allowance hides the next regression).
      2. REQUIRED forms - the places where the current name must be spelled out: the main window's
         title bar, both assembly metadata sets, the installer's AppName, the MSIX DisplayName, the
         names module (including the exchange server's constant) and the naming table's exchange anchors.

    Ends in the shared one-line verdict (CHECK-VERDICT): exit 0 PASS / 1 FAIL / 2 COULD NOT VERIFY.
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowAllowed,
    [int]$Limit = 60
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'Test-ProductNaming' 2 'the check could not complete' }
$Root = [IO.Path]::GetFullPath($Root)
Write-Subject 'Test-ProductNaming' "visible product names and the allow-list; $(Get-TreeLabel $Root)"

$allowFile = Join-Path $Root 'tools/product-naming-allow.jsonl'
if (-not (Test-Path -LiteralPath $allowFile)) { Exit-Verdict 'Test-ProductNaming' 2 'tools/product-naming-allow.jsonl is missing' }

$problems = [Collections.Generic.List[string]]::new()
function Problem([string]$message) { $problems.Add($message) }

# --- the allow-list -------------------------------------------------------------------------------
$allow = [Collections.Generic.List[object]]::new()
$lineNo = 0
foreach ($raw in Get-Content -LiteralPath $allowFile -Encoding UTF8) {
    $lineNo++
    if (-not $raw.Trim() -or $raw.TrimStart().StartsWith('#')) { continue }
    try { $r = $raw | ConvertFrom-Json } catch { Problem "allow-list line ${lineNo}: not valid JSON"; continue }
    foreach ($field in 'path', 'pattern', 'reason') {
        if (-not $r.$field -or -not "$($r.$field)".Trim()) { Problem "allow-list line ${lineNo}: record lacks '$field'" }
    }
    if (-not $r.path -or -not $r.pattern) { continue }
    try { $rx = [regex]::new([string]$r.pattern, 'IgnoreCase') } catch { Problem "allow-list line ${lineNo}: pattern is not a regex"; continue }
    $allow.Add([pscustomobject]@{ Line = $lineNo; Path = [string]$r.path; Rx = $rx; Reason = [string]$r.reason; Hits = 0 })
}

# --- the files ------------------------------------------------------------------------------------
$textExt = '.vb', '.vbproj', '.props', '.targets', '.config', '.resx', '.sln', '.md', '.html', '.htm', '.json', '.jsonl',
           '.yaml', '.yml', '.iss', '.ps1', '.psm1', '.xml', '.csv', '.txt', '.js', '.css', '.manifest', '.appxmanifest', '.toml', '.go'
# Tickets (docs/specifications/, active and done) are not scanned: a ticket describes what it was written
# about and is never rewritten (ticket 111 section 5.6). They are Git-ignored working documents, so a
# scan with rg would skip them anyway; naming the folder here keeps the result the same with or without rg.
$skip = '^(packages/|bin/|obj/|dist/|payload/|stage/|tools/ocr-cache/|node_modules/|docs/specifications/|\.git/)|(^|/)(bin|obj)/'
# rg applies the workspace ignore rules; a machine without it walks the tree instead, pruning the
# directories that are never sources (build output, vendored packages, caches).
function Get-InspectableFiles {
    if (Get-Command rg -ErrorAction SilentlyContinue) { return @(Get-WorkspaceFiles $Root) }
    $prune = '^(\.git|bin|obj|dist|packages|payload|stage|node_modules|ocr-cache)$'
    $found = [Collections.Generic.List[string]]::new()
    $stack = [Collections.Generic.Stack[string]]::new(); $stack.Push($Root)
    while ($stack.Count) {
        $dir = $stack.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($dir)) {
            $name = [IO.Path]::GetFileName($entry)
            if ([IO.Directory]::Exists($entry)) { if ($name -notmatch $prune) { $stack.Push($entry) } }
            else { $found.Add($entry.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')) }
        }
    }
    return $found.ToArray()
}
$files = @(Get-InspectableFiles | Where-Object {
        $_ -notmatch $skip -and ([IO.Path]::GetExtension($_).ToLowerInvariant() -in $textExt)
    })
if ($files.Count -eq 0) { Exit-Verdict 'Test-ProductNaming' 2 'no files to inspect' }

$forbidden = @(
    @{ Name = 'FastMediaSorter LITE'; Rx = [regex]::new('FastMediaSorter LITE', 'IgnoreCase') },
    @{ Name = 'Fast Media Sorter LITE/Lite'; Rx = [regex]::new('Fast Media Sorter Lite', 'IgnoreCase') },
    @{ Name = 'previous full name'; Rx = [regex]::new('Fast Media Sorter for Windows') },
    @{ Name = 'bare LITE/Lite word'; Rx = [regex]::new('\bLITE\b|\bLite\b') },
    # Owner rule, specification 133 section 4: the exchange server is never named by its last two words
    # alone, which reads as Microsoft's product. Only a sentence that genuinely means Microsoft's product
    # may carry it, through an allow-list record that says so.
    @{ Name = 'bare Exchange Server (not preceded by Media)'; Rx = [regex]::new('(?<!Media )Exchange Server') },
    # Specification 206 section 3, the exchange and live-broadcast vocabulary (PRODUCT_NAMING section 6).
    # The forms below are the mechanically separable ones; the contextual rule (never "broadcast" for a
    # live one without "live") is held per surface by the ticket's own parity tests. The sanctioned role
    # description "Fast Media Sorter exchange relay server" (PRODUCT_NAMING section 1) carries its
    # allowance in the lookbehind, and the vocabulary section that quotes the forbidden forms to define
    # them is allow-listed.
    @{ Name = 'relay server (not preceded by exchange)'; Rx = [regex]::new('(?<!exchange )relay servers?\b', 'IgnoreCase') },
    @{ Name = 'FMS server'; Rx = [regex]::new('\bFMS servers?\b', 'IgnoreCase') },
    @{ Name = 'the cloud (not the cloud clipboard)'; Rx = [regex]::new('\bthe cloud\b(?!\s+clipboard)', 'IgnoreCase') },
    @{ Name = 'cloud account'; Rx = [regex]::new('\bcloud accounts?\b', 'IgnoreCase') },
    @{ Name = 'login to Fast Media Sorter'; Rx = [regex]::new('log ?in to Fast Media Sorter', 'IgnoreCase') },
    @{ Name = 'upload my folders'; Rx = [regex]::new('upload my folders', 'IgnoreCase') },
    @{ Name = 'transmission'; Rx = [regex]::new('\btransmissions?\b', 'IgnoreCase') },
    @{ Name = 'start/stop broadcast (not a live one)'; Rx = [regex]::new('(?<!live )(start|stop) broadcast(s|ing)?\b', 'IgnoreCase') }
)

$hits = 0; $allowedHits = 0
foreach ($f in $files) {
    $full = Join-Path $Root $f
    $lines = [IO.File]::ReadAllLines($full, [Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $text = $lines[$i]
        if ($text.Length -lt 4) { continue }
        foreach ($form in $forbidden) {
            if (-not $form.Rx.IsMatch($text)) { continue }
            $hits++
            $covering = $null
            foreach ($a in $allow) {
                if ($f -like $a.Path -and $a.Rx.IsMatch($text)) { $covering = $a; break }
            }
            if ($covering) {
                $covering.Hits++; $allowedHits++
                if ($ShowAllowed) { Write-Host ("  allowed  {0}:{1} [{2}] {3}" -f $f, ($i + 1), $form.Name, $covering.Reason) -ForegroundColor DarkGray }
            } else {
                $shown = $text.Trim(); if ($shown.Length -gt 140) { $shown = $shown.Substring(0, 140) + '..' }
                Problem ("forbidden '{0}': {1}:{2}: {3}" -f $form.Name, $f, ($i + 1), $shown)
            }
            break   # one report per line, whichever form matched first
        }
    }
}
foreach ($a in $allow) {
    if ($a.Hits -eq 0) { Problem "allow-list line $($a.Line) covers nothing (path '$($a.Path)'): remove it, a stale allowance hides the next regression" }
}

# --- required forms -------------------------------------------------------------------------------
$required = @(
    @{ Path = 'src/ProductNames.vb';                         Text = 'Full As String = "Fast Media Sorter & Sharing for Windows"' },
    @{ Path = 'src/ProductNames.vb';                         Text = 'Compact As String = "Fast Media Sorter & Sharing"' },
    @{ Path = 'src/ProductNames.vb';                         Text = 'ExchangeServer As String = "Media Exchange Server"' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';               Text = '`Media Exchange Server`' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';               Text = 'FastMediaSorterExchangeServer' },
    @{ Path = 'src/Main_Form.Designer.vb';                   Text = '"Fast Media Sorter by SZA"' },
    @{ Path = 'src/My Project/AssemblyInfo.vb';              Text = 'AssemblyTitle("Fast Media Sorter & Sharing for Windows")' },
    @{ Path = 'src/My Project/AssemblyInfo.vb';              Text = 'AssemblyProduct("Fast Media Sorter & Sharing for Windows")' },
    @{ Path = 'src/Modern/FastMediaSorter.Modern.vbproj';    Text = '<Product>Fast Media Sorter &amp; Sharing for Windows</Product>' },
    @{ Path = 'src/Modern/FastMediaSorter.Modern.vbproj';    Text = '<AssemblyTitle>Fast Media Sorter &amp; Sharing for Windows</AssemblyTitle>' },
    @{ Path = 'publishing/installer/FastMediaSorter.iss';    Rx = '#define\s+AppName\s+"Fast Media Sorter &{1,2} Sharing for Windows"' },
    @{ Path = 'publishing/msix/AppxManifest.xml';            Text = '<DisplayName>FastMediaSorter LITE</DisplayName>' },
    @{ Path = 'publishing/msix/AppxManifest.xml';            Rx = '(?s)VisualElements[^>]*DisplayName="FastMediaSorter LITE"' },
    @{ Path = 'publishing/store/listingData.csv';            Rx = '(?m)^Title,\d+,Text,,FastMediaSorter LITE,' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';              Text = 'Fast Media Sorter & Sharing for Windows' },
    # The vocabulary section of 206 section 3 must stay: the table is the one place the exchange and
    # live-broadcast words are defined, and the gate's own forbidden forms cite it.
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';              Text = '## 6. The exchange and live-broadcast vocabulary' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';              Text = '**Live broadcast**' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';              Text = '**Exchange account**' },
    @{ Path = 'docs/guides/PRODUCT_NAMING.md';              Text = '**Connection string**' }
)
foreach ($req in $required) {
    $p = Join-Path $Root $req.Path
    if (-not (Test-Path -LiteralPath $p)) { Problem "required file missing: $($req.Path)"; continue }
    $content = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
    $ok = if ($req.Rx) { [regex]::IsMatch($content, $req.Rx) } else { $content.Contains($req.Text) }
    if (-not $ok) {
        $want = if ($req.Rx) { $req.Rx } else { $req.Text }
        Problem "required form missing in $($req.Path): $want"
    }
}

# --- verdict --------------------------------------------------------------------------------------
if ($problems.Count -gt 0) {
    $shownCount = [Math]::Min($Limit, $problems.Count)
    $problems | Select-Object -First $shownCount | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if ($problems.Count -gt $shownCount) { Write-Host "  .. and $($problems.Count - $shownCount) more" -ForegroundColor Red }
    Exit-Verdict 'Test-ProductNaming' 1 "$($problems.Count) problem(s); $hits forbidden-form line(s), $allowedHits allowed"
}
Exit-Verdict 'Test-ProductNaming' 0 "$($files.Count) files, $allowedHits allowed survivors on $($allow.Count) records, $($required.Count) required forms present"
