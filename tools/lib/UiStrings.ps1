# The product's own interface strings, read from the localization tables, for the checks that hold a page's
# quoted names against the program (SITE-REPRESENTATION rule 8, specification 157 T-2).
#
# The tables are Visual Basic source: Add("<ru key>", "<en>", "<uk>", ...) and AddC("<context>", "<ru key>", "<en>",
# "<uk>", ...). The Russian text is the key, so the three columns the site guides are written in are the first three
# string arguments (after the context for AddC). A string literal may be split by `&` and a line continuation; both
# are joined. Read as text - nothing is compiled or run.

function Normalize-UiName([string]$s) {
    $x = [Net.WebUtility]::HtmlDecode($s) -replace '<[^>]+>', '' -replace '[\s ]+', ' '
    $quotes = [char[]]@(0x00AB, 0x00BB, 0x201C, 0x201D, 0x2018, 0x2019, 0x0022, 0x0027)
    foreach ($q in $quotes) { $x = $x.Replace([string]$q, '') }
    # one trailing "..", ".", ":" or "," is punctuation around a name, not part of it
    $x = $x.Trim().TrimEnd('.', ':', ',').Trim()
    $x.ToLowerInvariant()
}

# Returns @{ en = HashSet; ru = HashSet; uk = HashSet } of normalized product strings.
function Get-UiStrings([string]$Root) {
    $sets = @{ en = [System.Collections.Generic.HashSet[string]]::new(); ru = [System.Collections.Generic.HashSet[string]]::new(); uk = [System.Collections.Generic.HashSet[string]]::new() }
    $dirs = @('src\Localization', 'src\FastMediaSorterCompanion\Localization') | ForEach-Object { Join-Path $Root $_ } | Where-Object { Test-Path $_ }
    foreach ($f in (Get-ChildItem -LiteralPath $dirs -Filter '*.vb' -File)) {
        $t = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
        foreach ($call in 'AddC(', 'Add(') {
            $i = 0
            while (($i = $t.IndexOf($call, $i, [StringComparison]::Ordinal)) -ge 0) {
                # not the tail of a longer identifier (AddC( contains no "Add(" but "XAdd(" would)
                if ($i -gt 0 -and ([char]::IsLetterOrDigit($t[$i - 1]) -or $t[$i - 1] -eq '_')) { $i += $call.Length; continue }
                $k = $i + $call.Length
                $strs = [System.Collections.Generic.List[string]]::new()
                $sb = [System.Text.StringBuilder]::new()
                $inStr = $false; $depth = 1
                while ($k -lt $t.Length) {
                    $ch = $t[$k]
                    if ($inStr) {
                        if ($ch -eq '"') {
                            if ($k + 1 -lt $t.Length -and $t[$k + 1] -eq '"') { [void]$sb.Append('"'); $k += 2; continue }
                            $inStr = $false; $k++; continue
                        }
                        [void]$sb.Append($ch); $k++; continue
                    }
                    if ($ch -eq '"') { $inStr = $true; $k++; continue }
                    if ($ch -eq "'") { while ($k -lt $t.Length -and $t[$k] -ne "`n") { $k++ }; continue }
                    if ($ch -eq '(') { $depth++ }
                    elseif ($ch -eq ')') { $depth--; if ($depth -eq 0) { $strs.Add($sb.ToString()); break } }
                    elseif ($ch -eq ',' -and $depth -eq 1) { $strs.Add($sb.ToString()); [void]$sb.Clear() }
                    $k++
                }
                $skip = if ($call -eq 'AddC(') { 1 } else { 0 }
                if ($strs.Count -ge $skip + 3) {
                    [void]$sets.ru.Add((Normalize-UiName $strs[$skip]))
                    [void]$sets.en.Add((Normalize-UiName $strs[$skip + 1]))
                    [void]$sets.uk.Add((Normalize-UiName $strs[$skip + 2]))
                }
                $i = [Math]::Max($k, $i + $call.Length)
            }
        }
    }
    $sets
}

# The content of every <span data-l="en|ru|ua"> of a page, nesting respected: @( @('en', '<inner html>'), .. ).
function Get-LangSpans([string]$Html) {
    $res = [System.Collections.Generic.List[object]]::new()
    foreach ($m in [regex]::Matches($Html, '(?i)<span data-l="(en|ru|ua)">')) {
        $start = $m.Index + $m.Length; $depth = 1; $j = $start
        while ($depth -gt 0) {
            $o = $Html.IndexOf('<span', $j, [StringComparison]::OrdinalIgnoreCase)
            $c = $Html.IndexOf('</span>', $j, [StringComparison]::OrdinalIgnoreCase)
            if ($c -lt 0) { break }
            if ($o -ge 0 -and $o -lt $c) { $depth++; $j = $o + 5 }
            else {
                $depth--; $j = $c + 7
                if ($depth -eq 0) { $res.Add(@($(if ($m.Groups[1].Value -eq 'ua') { 'uk' } else { $m.Groups[1].Value }), $Html.Substring($start, $c - $start))) }
            }
        }
    }
    , $res
}

# Every bold name in a guide, per language, that is neither a product string of that language nor listed as exempt.
# A bold item may be a path ("Files / Basic settings" or "A -> B"): each part is held on its own. $Exempt is
# @{ any = @(..); en = @(..); ru = @(..); uk = @(..) } of normalized strings. Returns "page|lang|name" lines.
function Get-GuideUiMisses([string]$Root, [string[]]$Pages, $Sets, $Exempt) {
    $script:UiSeen = [System.Collections.Generic.HashSet[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $Pages) {
        $html = [IO.File]::ReadAllText((Join-Path $Root $p), [Text.Encoding]::UTF8)
        foreach ($span in (Get-LangSpans $html)) {
            $lang = $span[0]
            foreach ($b in [regex]::Matches($span[1], '(?s)<b\b[^>]*>(.*?)</b>')) {
                $whole = Normalize-UiName $b.Groups[1].Value
                if (-not $whole) { continue }
                [void]$script:UiSeen.Add("$lang|$whole")
                $parts = if ($Sets[$lang].Contains($whole)) { @($whole) } else { @($whole) + @($whole -split ' / | -> |\u2192| - ' | ForEach-Object { $_.Trim().TrimEnd('.', ':').Trim() } | Where-Object { $_ }) }
                foreach ($part in $parts) { [void]$script:UiSeen.Add("$lang|$part") }
                $ok = $false
                if ($Sets[$lang].Contains($whole)) { $ok = $true }
                elseif ($Exempt.any -contains $whole -or $Exempt[$lang] -contains $whole) { $ok = $true }
                elseif ($parts.Count -gt 1) {
                    $ok = $true
                    foreach ($part in $parts[1..($parts.Count - 1)]) {
                        if (-not ($Sets[$lang].Contains($part) -or $Exempt.any -contains $part -or $Exempt[$lang] -contains $part)) { $ok = $false }
                    }
                }
                if (-not $ok) { $line = "$p|$lang|$whole"; if ($seen.Add($line)) { $out.Add($line) } }
            }
        }
    }
    , $out
}
