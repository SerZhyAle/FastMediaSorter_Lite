<#
.SYNOPSIS
    Assembles the GitHub Pages artifact: copies exactly the files declared in tools/site-served.json
    into -OutDir, then proves that every local reference of the copied pages resolves inside it
    (ticket L0207, findings F4, F6, F81).

.DESCRIPTION
    GitHub Pages used to serve the whole repository root, which published product executables, internal
    notes and vendored trees. Pages now deploys from an artifact that holds only the site, and
    tools/site-served.json is the one declaration of that set (`include` and `exclude` entries, each with
    a one-line reason). .github/workflows/pages.yml runs this script into `_site` and uploads the folder.

    An entry's `path` is repository-relative with forward slashes and takes one of three shapes:
      folder/          the whole folder, recursively
      dir/*.html       a file glob inside one folder (`*` and `?` never cross a `/`; `**` does)
      dir/file.ext     one file
    Matching is case-sensitive, the way the Linux host that serves the artifact sees it.

    What the run does, in order:
      1  reads the declaration; an `include` entry that matches no file is a defect (the declaration
         drifted from the tree);
      2  copies the declared files (minus the `exclude` entries) into -OutDir, byte for byte;
      3  reads the COPIED tree and resolves every local reference of every .html and .css file in it:
         href, src, poster, srcset, action, <object data>, a meta refresh target, url() and @import, and
         every absolute address of this site (canonical, hreflang, og:image, JSON-LD); an external
         address, mailto:, tel:, data:, javascript: and a bare #fragment are ignored, a query or
         fragment is stripped, a directory address resolves to its index.html, and a path is looked
         up in the copied tree with exact case. Every <loc> of sitemap.xml, every address of
         search-index.json and the Sitemap line of robots.txt are resolved the same way;
      4  lists each offender (the referring file, the attribute, the address, the reason) and ends in
         one verdict line.

    -ReportUnreferenced additionally lists copied files that nothing in the artifact refers to (a
    pruning aid for the declaration, never a failure).

    -Clean empties an existing non-empty -OutDir first (its children only, one by one, and only after
    the declaration proved usable); without it a non-empty -OutDir is refused. Inside the repository
    -Clean is allowed only under temp/ or for a folder named _site. -OutDir may not be the repository
    root, one of its parents, or a folder inside the served set.

    Cross-platform: runs under pwsh on ubuntu-latest (the Pages workflow) and on Windows.

.PARAMETER OutDir
    The folder to assemble the artifact in (created when missing).

.PARAMETER Declaration
    The declaration file; default tools/site-served.json beside this script.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - the artifact is assembled and every local reference resolves inside it;
      1 - the declaration has a malformed or empty entry, or at least one reference does not resolve;
      2 - could not run: no -OutDir, an unusable -OutDir, the declaration missing or unreadable.
    The last line is the verdict, `Build-SiteArtifact: <WORD> (..)`.
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Declaration,
    [switch]$Clean,
    [switch]$ReportUnreferenced
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'Verdict.ps1')
$checkName = 'Build-SiteArtifact'
$clock = [System.Diagnostics.Stopwatch]::StartNew()

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $checkName 2 'the check itself failed'
}

$sep = [IO.Path]::DirectorySeparatorChar
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
$declPath = if ($Declaration) { $Declaration } else { Join-Path (Join-Path $root 'tools') 'site-served.json' }

if (-not $OutDir) { Exit-Verdict $checkName 2 '-OutDir is required' }
$outFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$outFull = [IO.Path]::GetFullPath($outFull).TrimEnd('\', '/')

Write-Subject $checkName "the files declared in tools/site-served.json, assembled into $outFull; $(Get-TreeLabel $root)"

if (-not (Test-Path -LiteralPath $declPath -PathType Leaf)) { Exit-Verdict $checkName 2 "declaration not found: $declPath" }
try { $decl = Get-Content -LiteralPath $declPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { Exit-Verdict $checkName 2 "declaration is not valid JSON: $declPath" }

# --- the output folder must be safe to write and, if it holds anything, safe to empty ----------------
$rootWithSep = $root + $sep
if ($outFull -eq $root -or $rootWithSep.StartsWith($outFull + $sep, [StringComparison]::OrdinalIgnoreCase)) {
    Exit-Verdict $checkName 2 "-OutDir is the repository root or one of its parents: $outFull"
}
if (Test-Path -LiteralPath $outFull -PathType Leaf) { Exit-Verdict $checkName 2 "-OutDir is a file: $outFull" }
$existing = @()
if (Test-Path -LiteralPath $outFull -PathType Container) {
    $existing = @(Get-ChildItem -LiteralPath $outFull -Force)
    if ($existing.Count -gt 0 -and -not $Clean) { Exit-Verdict $checkName 2 "-OutDir is not empty (pass -Clean to empty it): $outFull" }
}
if ($existing.Count -gt 0 -and $outFull.StartsWith($rootWithSep, [StringComparison]::OrdinalIgnoreCase)) {
    # -Clean deletes; inside the repository it is allowed only in the scratch folder temp/ or in a folder named _site.
    $outRel = $outFull.Substring($root.Length + 1).Replace('\', '/')
    if (-not ($outRel.StartsWith('temp/', [StringComparison]::Ordinal) -or $outRel -ceq '_site')) {
        Exit-Verdict $checkName 2 "-Clean inside the repository is allowed only under temp/ or for _site, not for: $outRel"
    }
}

# --- the declaration -----------------------------------------------------------------------------------
$problems = [System.Collections.Generic.List[string]]::new()

function Get-Prop($o, [string]$name) {
    $p = $o.PSObject.Properties[$name]
    if ($p -and $null -ne $p.Value) { return $p.Value }
    return $null
}

$baseUrl = [string](Get-Prop $decl 'baseUrl')
if (-not $baseUrl -or $baseUrl -notmatch '^https://[^/]+(/[^/]+)*$') {
    Exit-Verdict $checkName 2 'declaration: baseUrl is missing or is not an https address without a trailing slash'
}
$baseUrl = $baseUrl.TrimEnd('/')

# One matcher per `path`: tree (folder/), glob (wildcards) or file. Regexes are case-sensitive.
function New-Matcher([string]$path, [string]$section) {
    $p = $path.Replace('\', '/')
    if ($p.StartsWith('/') -or $p.StartsWith('./') -or $p.Contains('..')) { $problems.Add("$section '$path': a path is repository-relative, without a leading '/' or './' and without '..'"); return $null }
    if ($p.EndsWith('/')) {
        if ($p.Contains('*') -or $p.Contains('?')) { $problems.Add("$section '$path': a folder entry (trailing '/') takes no wildcard"); return $null }
        return [pscustomobject]@{ Kind = 'tree'; Path = $path; Pattern = $p; StaticDir = $p.TrimEnd('/'); Recursive = $true; Regex = $null }
    }
    if ($p.Contains('*') -or $p.Contains('?')) {
        $segs = $p.Split('/')
        $static = [System.Collections.Generic.List[string]]::new()
        foreach ($s in $segs) { if ($s.Contains('*') -or $s.Contains('?')) { break }; $static.Add($s) }
        $sb = [System.Text.StringBuilder]::new('^')
        for ($i = 0; $i -lt $p.Length; $i++) {
            $c = $p[$i]
            if ($c -eq '*') {
                if ($i + 1 -lt $p.Length -and $p[$i + 1] -eq '*') { [void]$sb.Append('.*'); $i++ } else { [void]$sb.Append('[^/]*') }
            } elseif ($c -eq '?') { [void]$sb.Append('[^/]') }
            else { [void]$sb.Append([regex]::Escape([string]$c)) }
        }
        [void]$sb.Append('$')
        return [pscustomobject]@{ Kind = 'glob'; Path = $path; Pattern = $p; StaticDir = ($static -join '/'); Recursive = $p.Contains('**'); Regex = [regex]::new($sb.ToString()) }
    }
    $dir = if ($p.Contains('/')) { $p.Substring(0, $p.LastIndexOf('/')) } else { '' }
    return [pscustomobject]@{ Kind = 'file'; Path = $path; Pattern = $p; StaticDir = $dir; Recursive = $false; Regex = $null }
}

function Test-Match($m, [string]$rel) {
    switch ($m.Kind) {
        'tree' { return $rel.StartsWith($m.Pattern, [StringComparison]::Ordinal) }
        'file' { return $rel -ceq $m.Pattern }
        default { return $m.Regex.IsMatch($rel) }
    }
}

# True when every segment of $rel exists under the root with exactly this case: the artifact is served by a
# case-sensitive host, a case-insensitive disk would let a typo through.
function Test-ExactCase([string]$rel) {
    if (-not $rel) { return $true }
    $cur = $root
    foreach ($seg in $rel.Split('/')) {
        $hit = $false
        foreach ($e in [IO.Directory]::EnumerateFileSystemEntries($cur)) {
            if ([IO.Path]::GetFileName($e) -ceq $seg) { $hit = $true; $cur = $e; break }
        }
        if (-not $hit) { return $false }
    }
    return $true
}

function Read-Entries($list, [string]$section) {
    $out = @()
    if ($null -eq $list) { return $out }
    foreach ($e in @($list)) {
        $path = [string](Get-Prop $e 'path')
        $reason = [string](Get-Prop $e 'reason')
        if (-not $path) { $problems.Add("$section entry without a 'path'"); continue }
        if (-not $reason.Trim()) { $problems.Add("$section '$path': the one-line 'reason' is missing") }
        $m = New-Matcher $path $section
        if ($m) { $out += $m }
    }
    return $out
}

$includes = @(Read-Entries (Get-Prop $decl 'include') 'include')
$excludes = @(Read-Entries (Get-Prop $decl 'exclude') 'exclude')
if ($includes.Count -eq 0 -and $problems.Count -eq 0) { $problems.Add('include: the declaration names nothing to serve') }

# --- collect the files ---------------------------------------------------------------------------------
$picked = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)   # rel -> source path
$perEntry = @{}
foreach ($m in $includes) {
    $count = 0
    $dir = if ($m.StaticDir) { Join-Path $root ($m.StaticDir.Replace('/', [string]$sep)) } else { $root }
    $exactOk = if ($m.Kind -eq 'file') { Test-ExactCase $m.Pattern } else { Test-ExactCase $m.StaticDir }
    if ($exactOk) {
        $found = @()
        switch ($m.Kind) {
            'tree' { if (Test-Path -LiteralPath $dir -PathType Container) { $found = @(Get-ChildItem -LiteralPath $dir -File -Recurse -Force) } }
            'file' { $f = Join-Path $root ($m.Pattern.Replace('/', [string]$sep)); if (Test-Path -LiteralPath $f -PathType Leaf) { $found = @(Get-Item -LiteralPath $f -Force) } }
            default {
                if (Test-Path -LiteralPath $dir -PathType Container) {
                    $found = @(Get-ChildItem -LiteralPath $dir -File -Force -Recurse:$m.Recursive)
                }
            }
        }
        foreach ($f in $found) {
            $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
            if (-not (Test-Match $m $rel)) { continue }
            $count++
            if (-not $picked.ContainsKey($rel)) { $picked[$rel] = $f.FullName }
        }
    }
    $perEntry["include $($m.Path)"] = $count
    if ($count -eq 0) { $problems.Add("include '$($m.Path)' matches no file - the declaration drifted from the tree") }
}

$droppedBy = @{}
foreach ($m in $excludes) { $droppedBy[$m.Path] = 0 }
foreach ($rel in @($picked.Keys)) {
    foreach ($m in $excludes) {
        if (Test-Match $m $rel) { [void]$picked.Remove($rel); $droppedBy[$m.Path]++; break }
    }
}
foreach ($k in ($droppedBy.Keys | Sort-Object)) {
    if ($droppedBy[$k] -eq 0) { Write-Host "  note: exclude '$k' removes nothing (stale entry?)" -ForegroundColor Yellow }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict $checkName 1 "$($problems.Count) declaration problem(s); nothing was copied"
}

# The output folder must not be part of what is being copied.
$outWithSep = $outFull + $sep
foreach ($src in $picked.Values) {
    if ($src.StartsWith($outWithSep, [StringComparison]::OrdinalIgnoreCase)) {
        Exit-Verdict $checkName 2 "-OutDir lies inside the served set: $outFull"
    }
}

# --- empty the output folder (only after the whole declaration proved usable), then copy ---------------------
foreach ($child in $existing) { Remove-Item -LiteralPath $child.FullName -Recurse -Force }
[void][IO.Directory]::CreateDirectory($outFull)
$madeDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$bytes = [long]0
foreach ($rel in ($picked.Keys | Sort-Object)) {
    $dest = [IO.Path]::Combine($outFull, $rel.Replace('/', [string]$sep))
    $destDir = [IO.Path]::GetDirectoryName($dest)
    if ($madeDirs.Add($destDir)) { [void][IO.Directory]::CreateDirectory($destDir) }
    [IO.File]::Copy($picked[$rel], $dest, $true)
    $bytes += ([IO.FileInfo]::new($dest)).Length
}

# --- read the COPIED tree back ------------------------------------------------------------------------------
$outFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$ciMap = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($p in [IO.Directory]::EnumerateFiles($outFull, '*', [IO.SearchOption]::AllDirectories)) {
    $rel = $p.Substring($outFull.Length + 1).Replace('\', '/')
    [void]$outFiles.Add($rel)
    if (-not $ciMap.ContainsKey($rel)) { $ciMap[$rel] = $rel }
}

$referenced = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$offenders = [System.Collections.Generic.List[string]]::new()
$refCount = 0

# Resolves one address found in $fromRel ('' = an address read from outside a page, which is absolute).
# Returns '' when the address is not local to the site; otherwise records the target or an offender.
function Resolve-Ref([string]$fromRel, [string]$kind, [string]$raw) {
    $u = [System.Net.WebUtility]::HtmlDecode($raw).Trim()
    if (-not $u) { return }
    if ($u.StartsWith('#') -or $u.StartsWith('?')) { return }                 # the page itself
    $shown = if ($fromRel) { $fromRel } else { '(address list)' }
    $path = $null
    if ($u -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        if ($u.StartsWith($baseUrl + '/', [StringComparison]::Ordinal) -or $u -ceq $baseUrl -or
            $u.StartsWith($baseUrl + '?', [StringComparison]::Ordinal) -or $u.StartsWith($baseUrl + '#', [StringComparison]::Ordinal)) {
            $path = $u.Substring($baseUrl.Length)
            if ($path -eq '') { $path = '/' }
            $fromRel = ''
        } else { return }                                                      # external, mailto:, tel:, data:, javascript:
    } elseif ($u.StartsWith('//')) { return }                                  # protocol-relative: external
    $script:refCount++
    if ($null -eq $path) { $path = $u }
    $path = ($path -split '[?#]', 2)[0]
    try { $path = [Uri]::UnescapeDataString($path) } catch { }
    $path = $path.Replace('\', '/')
    if ($path.StartsWith('/') -and $fromRel) {
        $offenders.Add("${shown}: $kind $raw -> a root-absolute path points at the host root, not at the site under $baseUrl/")
        return
    }
    $segs = [System.Collections.Generic.List[string]]::new()
    if (-not $path.StartsWith('/') -and $fromRel.Contains('/')) {
        foreach ($s in $fromRel.Substring(0, $fromRel.LastIndexOf('/')).Split('/')) { $segs.Add($s) }
    }
    $parts = $path.Split('/')
    $isDir = $path -eq '' -or $path.EndsWith('/') -or $parts[$parts.Length - 1] -in '.', '..'
    foreach ($s in $parts) {
        if ($s -eq '' -or $s -eq '.') { continue }
        if ($s -eq '..') {
            if ($segs.Count -eq 0) { $offenders.Add("${shown}: $kind $raw -> climbs above the site root"); return }
            $segs.RemoveAt($segs.Count - 1); continue
        }
        $segs.Add($s)
    }
    $rel = $segs -join '/'
    $target = if ($isDir) { if ($rel) { "$rel/index.html" } else { 'index.html' } } else { $rel }
    if ($outFiles.Contains($target)) { [void]$referenced.Add($target); return }
    if (-not $isDir -and $outFiles.Contains("$rel/index.html")) { [void]$referenced.Add("$rel/index.html"); return }   # a folder address: Pages redirects to its index
    $hint = ''
    if ($ciMap.ContainsKey($target) -and $ciMap[$target] -cne $target) { $hint = " (a file with other letter case exists: $($ciMap[$target]))" }
    $offenders.Add("${shown}: $kind $raw -> not in the artifact: $target$hint")
}

$reComment = [regex]::new('(?s)<!--.*?-->')
$reLdJson = [regex]::new('(?is)<script\b[^>]*type\s*=\s*["'']?application/ld\+json["'']?[^>]*>(.*?)</script\s*>')
$reScriptBody = [regex]::new('(?is)(<script\b[^>]*>).*?(</script\s*>)')
$reTag = [regex]::new('<[a-zA-Z][^>"'']*(?:(?:"[^"]*"|''[^'']*'')[^>"'']*)*>')
$reAttr = [regex]::new('(?is)(?<![\w:-])(href|src|poster|data-src|data-href|action|formaction|data|imagesrcset|srcset)\s*=\s*(?:"([^"]*)"|''([^'']*)''|([^\s>"'']+))')
$reUrlFn = [regex]::new('(?i)url\(\s*(?:"([^"]*)"|''([^'']*)''|([^)\s"'']*))\s*\)')
$reImport = [regex]::new('(?i)@import\s+["'']([^"'']+)["'']')
$reMetaUrl = [regex]::new('(?i)content\s*=\s*["''][^"'']*?url\s*=\s*([^"'';]+)')
$reSameSite = [regex]::new([regex]::Escape($baseUrl) + '(?:[/?#][^"''\s<>)\\,;\]}]*)?')

function Add-Unique($set, [string]$kind, [string]$value) {
    if ($value) { [void]$set.Add("$kind`t$value") }
}

function Scan-Html([string]$rel, [string]$text) {
    $refs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $t = $reComment.Replace($text, '')
    foreach ($m in $reLdJson.Matches($t)) {
        foreach ($s in $reSameSite.Matches($m.Groups[1].Value)) { Add-Unique $refs 'json-ld' $s.Value.TrimEnd('.') }
    }
    $t = $reScriptBody.Replace($t, '$1$2')
    foreach ($s in $reSameSite.Matches($t)) { Add-Unique $refs 'site-url' $s.Value.TrimEnd('.') }
    foreach ($tag in $reTag.Matches($t)) {
        $tv = $tag.Value
        foreach ($a in $reAttr.Matches($tv)) {
            $name = $a.Groups[1].Value.ToLowerInvariant()
            $val = if ($a.Groups[2].Success) { $a.Groups[2].Value } elseif ($a.Groups[3].Success) { $a.Groups[3].Value } else { $a.Groups[4].Value }
            if ($name -eq 'srcset' -or $name -eq 'imagesrcset') {
                foreach ($cand in $val.Split(',')) {
                    $first = ($cand.Trim() -split '\s+')[0]
                    Add-Unique $refs $name $first
                }
            } else { Add-Unique $refs $name $val }
        }
        if ($tv -match '(?i)http-equiv\s*=\s*["'']?refresh') {
            $mm = $reMetaUrl.Match($tv)
            if ($mm.Success) { Add-Unique $refs 'meta-refresh' $mm.Groups[1].Value.Trim() }
        }
    }
    foreach ($u in $reUrlFn.Matches($t)) {
        $v = if ($u.Groups[1].Success) { $u.Groups[1].Value } elseif ($u.Groups[2].Success) { $u.Groups[2].Value } else { $u.Groups[3].Value }
        if ($v -and -not $v.StartsWith('#') -and $v -notmatch '^data:') { Add-Unique $refs 'url()' $v }
    }
    foreach ($i in $reImport.Matches($t)) { Add-Unique $refs '@import' $i.Groups[1].Value }
    foreach ($r in $refs) { $k, $v = $r -split "`t", 2; Resolve-Ref $rel $k $v }
}

function Scan-Css([string]$rel, [string]$text) {
    $refs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $t = [regex]::Replace($text, '(?s)/\*.*?\*/', '')
    foreach ($u in $reUrlFn.Matches($t)) {
        $v = if ($u.Groups[1].Success) { $u.Groups[1].Value } elseif ($u.Groups[2].Success) { $u.Groups[2].Value } else { $u.Groups[3].Value }
        if ($v -and -not $v.StartsWith('#') -and $v -notmatch '^data:') { Add-Unique $refs 'url()' $v }
    }
    foreach ($i in $reImport.Matches($t)) { Add-Unique $refs '@import' $i.Groups[1].Value }
    foreach ($s in $reSameSite.Matches($t)) { Add-Unique $refs 'site-url' $s.Value.TrimEnd('.') }
    foreach ($r in $refs) { $k, $v = $r -split "`t", 2; Resolve-Ref $rel $k $v }
}

$utf8 = [Text.UTF8Encoding]::new($false)
$pageCount = 0
$cssCount = 0
foreach ($rel in ($outFiles | Sort-Object)) {
    $ext = [IO.Path]::GetExtension($rel).ToLowerInvariant()
    if ($ext -ne '.html' -and $ext -ne '.css') { continue }
    $text = [IO.File]::ReadAllText([IO.Path]::Combine($outFull, $rel.Replace('/', [string]$sep)), $utf8)
    if ($ext -eq '.html') { $pageCount++; Scan-Html $rel $text } else { $cssCount++; Scan-Css $rel $text }
}

# --- the declared address lists of the site -------------------------------------------------------------------
$listNote = @()
if ($outFiles.Contains('sitemap.xml')) {
    $sm = [IO.File]::ReadAllText([IO.Path]::Combine($outFull, 'sitemap.xml'), $utf8)
    $n = 0
    foreach ($m in [regex]::Matches($sm, '<loc>\s*([^<]+?)\s*</loc>')) { $n++; Resolve-Ref '' 'sitemap loc' $m.Groups[1].Value }
    foreach ($m in [regex]::Matches($sm, '<xhtml:link\b[^>]*\shref="([^"]+)"')) { $n++; Resolve-Ref '' 'sitemap alternate' $m.Groups[1].Value }
    $listNote += "sitemap.xml $n addresses"
} else { $offenders.Add('sitemap.xml is not in the artifact') }
if ($outFiles.Contains('search-index.json')) {
    $idx = [IO.File]::ReadAllText([IO.Path]::Combine($outFull, 'search-index.json'), $utf8) | ConvertFrom-Json
    $n = 0
    foreach ($e in @($idx)) { $u = [string](Get-Prop $e 'url'); if ($u) { $n++; Resolve-Ref '' 'search-index url' $u } }
    $listNote += "search-index.json $n addresses"
} else { $offenders.Add('search-index.json is not in the artifact') }
if ($outFiles.Contains('robots.txt')) {
    $rb = [IO.File]::ReadAllText([IO.Path]::Combine($outFull, 'robots.txt'), $utf8)
    foreach ($m in [regex]::Matches($rb, '(?im)^\s*Sitemap:\s*(\S+)')) { Resolve-Ref '' 'robots Sitemap' $m.Groups[1].Value }
} else { $offenders.Add('robots.txt is not in the artifact') }

# --- optional pruning aid ------------------------------------------------------------------------------------
if ($ReportUnreferenced) {
    $loose = @($outFiles | Where-Object { -not $referenced.Contains($_) -and $_ -notmatch '\.html$' -and $_ -notin 'robots.txt', 'sitemap.xml', 'search-index.json', '.nojekyll' } | Sort-Object)
    Write-Host "Unreferenced non-page files in the artifact: $($loose.Count)"
    $loose | Group-Object { $parts = $_.Split('/'); if ($parts.Length -gt 2) { "$($parts[0])/$($parts[1])/" } elseif ($parts.Length -eq 2) { "$($parts[0])/" } else { '(root)' } } |
        ForEach-Object { Write-Host ("  {0,5}  {1}" -f $_.Count, $_.Name) }
    foreach ($f in $loose) { Write-Host "          $f" }
}

# --- verdict ---------------------------------------------------------------------------------------------------
$sizeMb = [Math]::Round($bytes / 1MB, 1)
$summary = "$($outFiles.Count) files, $sizeMb MB, $pageCount pages and $cssCount stylesheets read, $refCount local references resolved, $($listNote -join ', '), $([Math]::Round($clock.Elapsed.TotalSeconds, 1)) s"
Write-Host "Assembled $($outFiles.Count) files ($sizeMb MB) into $outFull"
foreach ($k in ($perEntry.Keys | Sort-Object)) { Write-Host ("  {0,6}  {1}" -f $perEntry[$k], $k) }
if ($offenders.Count -gt 0) {
    $sorted = @($offenders | Sort-Object -Unique)
    Write-Host "Unresolved references: $($sorted.Count)"
    foreach ($o in ($sorted | Select-Object -First 150)) { Write-Host "  - $o" }
    if ($sorted.Count -gt 150) { Write-Host "  .. and $($sorted.Count - 150) more" }
    Exit-Verdict $checkName 1 "$($sorted.Count) reference(s) do not resolve inside the artifact; $summary"
}
Exit-Verdict $checkName 0 $summary
