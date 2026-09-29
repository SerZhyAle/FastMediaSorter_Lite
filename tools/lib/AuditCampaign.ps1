# Shared by the audit-campaign tools of ticket 057 (Audit-Partition.ps1, Audit-FanOut.ps1,
# Audit-Status.ps1). Dot-source it:
#     . (Join-Path $PSScriptRoot 'lib\AuditCampaign.ps1')
#
# Everything here is deterministic: the same tree and the same concern map give the same answer, in
# the same order, so a manifest written from it is byte-for-byte repeatable.

Set-StrictMode -Version 3.0

# --- paths and globs ---------------------------------------------------------

function ConvertTo-RelPath([string]$Root, [string]$Full) {
    $rel = $Full.Substring($Root.TrimEnd('\', '/').Length).TrimStart('\', '/')
    $rel.Replace('\', '/')
}

# '*' stays inside one folder, '**' crosses folders ('**/' also matches no folder at all).
function ConvertTo-GlobRegex([string]$Glob) {
    $sb = [System.Text.StringBuilder]::new('^')
    $i = 0
    while ($i -lt $Glob.Length) {
        $c = $Glob[$i]
        if ($c -eq '*') {
            if ($i + 1 -lt $Glob.Length -and $Glob[$i + 1] -eq '*') {
                if ($i + 2 -lt $Glob.Length -and $Glob[$i + 2] -eq '/') { [void]$sb.Append('(?:.*/)?'); $i += 3; continue }
                [void]$sb.Append('.*'); $i += 2; continue
            }
            [void]$sb.Append('[^/]*')
        } elseif ($c -eq '?') {
            [void]$sb.Append('[^/]')
        } else {
            [void]$sb.Append([regex]::Escape([string]$c))
        }
        $i++
    }
    [void]$sb.Append('$')
    $sb.ToString()
}

function Test-Glob([string]$Path, [string]$Glob) {
    [regex]::IsMatch($Path, (ConvertTo-GlobRegex $Glob), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Test-AnyGlob([string]$Path, $Globs) {
    foreach ($g in @($Globs)) { if ($g -and (Test-Glob $Path $g)) { return $true } }
    $false
}

# --- the concern map ---------------------------------------------------------

function Read-ConcernMap([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "concern map not found: $Path" }
    $map = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($f in 'roots', 'concerns') {
        if (-not $map.PSObject.Properties[$f]) { throw "concern map ${Path}: '$f' is missing" }
    }
    $ids = @{}
    foreach ($c in $map.concerns) {
        if (-not $c.id) { throw "concern map ${Path}: a concern has no id" }
        if ($c.id -notmatch '^[A-Z]-[a-z0-9-]+$') { throw "concern map ${Path}: id '$($c.id)' is not <LETTER>-<slug>" }
        if ($ids.ContainsKey($c.id)) { throw "concern map ${Path}: id '$($c.id)' appears twice" }
        $ids[$c.id] = $true
    }
    $map
}

function Get-MapList($Map, [string]$Name) {
    $p = $Map.PSObject.Properties[$Name]
    if ($p -and $p.Value) { return @($p.Value) }
    @()
}

function Test-IsClassBConcern($Concern) {
    $p = $Concern.PSObject.Properties['classB']
    [bool]($p -and $p.Value)
}

# Group key: Main_Form.X.vb -> X; any other file -> its name before the first dot; then the alias map.
function Get-GroupKey([string]$RelPath, $Map) {
    $name = [System.IO.Path]::GetFileName($RelPath)
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($name)
    if ($stem -match '^Main_Form\.(.+)$') { $key = $Matches[1] } else { $key = ($stem -split '\.')[0] }
    $aliases = $Map.PSObject.Properties['groupAliases']
    if ($aliases -and $aliases.Value) {
        $a = $aliases.Value.PSObject.Properties[$key]
        if ($a) { $key = [string]$a.Value }
    }
    $key
}

function Get-LineCount([string]$Full) {
    [System.IO.File]::ReadAllLines($Full).Count
}

# Walks the tree and classifies every candidate file. Returns objects sorted by Path (ordinal):
#   Path, Full, Lines, Class ('A' | 'A-data' | 'excluded' | 'B'), Concern, Reason, CoveredBy, SampledBy, Group
function Get-AuditFiles([string]$Root, $Map, [switch]$IncludeClassB) {
    $roots = @(Get-MapList $Map 'roots')
    $bRoots = @(Get-MapList $Map 'classBRoots')
    $skip = @(Get-MapList $Map 'skipDirs')
    $concerns = @($Map.concerns | Where-Object { $IncludeClassB -or -not (Test-IsClassBConcern $_) })

    $all = Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
        $rel = ConvertTo-RelPath $Root $_.FullName
        $parts = $rel -split '/'
        -not ($parts | Select-Object -SkipLast 1 | Where-Object { $skip -contains $_ -or $_ -eq '.git' })
    }

    $out = foreach ($f in $all) {
        $rel = ConvertTo-RelPath $Root $f.FullName
        $isA = Test-AnyGlob $rel $roots
        $isB = (-not $isA) -and $IncludeClassB -and (Test-AnyGlob $rel $bRoots)
        if (-not ($isA -or $isB)) { continue }

        $o = [ordered]@{
            Path = $rel; Full = $f.FullName; Lines = (Get-LineCount $f.FullName)
            Class = $(if ($isA) { 'A' } else { 'B' }); Concern = ''; Reason = ''; CoveredBy = ''; SampledBy = ''; Group = ''
        }
        $done = $false
        foreach ($e in (Get-MapList $Map 'excluded')) {
            if (Test-Glob $rel $e.pattern) { $o.Class = 'excluded'; $o.Reason = [string]$e.reason; $done = $true; break }
        }
        if (-not $done -and $isA) {
            foreach ($d in (Get-MapList $Map 'data')) {
                $except = @()
                if ($d.PSObject.Properties['except']) { $except = @($d.except) }
                if ((Test-Glob $rel $d.pattern) -and -not ($except | Where-Object { $_ -ieq $rel })) {
                    $o.Class = 'A-data'; $o.CoveredBy = [string]$d.coveredBy; $o.SampledBy = [string]$d.sampledBy; $done = $true; break
                }
            }
        }
        if (-not $done) {
            foreach ($c in $concerns) {
                if (Test-AnyGlob $rel $c.patterns) { $o.Concern = [string]$c.id; break }
            }
            $o.Group = Get-GroupKey $rel $Map
        }
        [pscustomobject]$o
    }
    Sort-ByPath @($out)
}

function Sort-ByPath($Items) {
    $list = [System.Collections.Generic.List[object]]::new()
    foreach ($i in @($Items)) { if ($null -ne $i) { $list.Add($i) } }
    $list.Sort([System.Comparison[object]] { param($a, $b) [string]::CompareOrdinal([string]$a.Path, [string]$b.Path) })
    $list.ToArray()
}

# --- signals -----------------------------------------------------------------

# The pre-scan of spec 057 section 5.1. Each signal is a list of 1-based line numbers, so a slice can
# answer every hit. Comment lines are skipped. Handler symmetry needs the slice's RemoveHandler set,
# so it is returned as raw event names and judged by Get-SliceSignals.
function Get-FileSignals([string]$Full) {
    $lines = [System.IO.File]::ReadAllLines($Full)
    $isVb = $Full -match '\.vb$'
    $sig = [ordered]@{
        addHandler = [System.Collections.Generic.List[object]]::new()
        removeHandlerEvents = [System.Collections.Generic.List[string]]::new()
        asyncSub = [System.Collections.Generic.List[int]]::new()
        silentCatch = [System.Collections.Generic.List[int]]::new()
        unownedGdi = [System.Collections.Generic.List[int]]::new()
        fileMutation = [System.Collections.Generic.List[int]]::new()
        processStart = [System.Collections.Generic.List[int]]::new()
        pinvoke = [System.Collections.Generic.List[int]]::new()
        sharedMutable = [System.Collections.Generic.List[int]]::new()
        concurrency = [System.Collections.Generic.List[int]]::new()
        seam = $false
    }
    $ic = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        $n = $i + 1
        if ($isVb) {
            if ($t.StartsWith("'")) { continue }
            if ($t -match '^#If\s+(Not\s+)?NETFRAMEWORK\b') { $sig.seam = $true }
            $m = [regex]::Match($t, '\bAddHandler\s+[\w.()]*?(\w+)\s*,', $ic)
            if ($m.Success) { $sig.addHandler.Add([pscustomobject]@{ Line = $n; Event = $m.Groups[1].Value }) }
            $m = [regex]::Match($t, '\bRemoveHandler\s+[\w.()]*?(\w+)\s*,', $ic)
            if ($m.Success) { $sig.removeHandlerEvents.Add($m.Groups[1].Value) }
            if ($t -match '\bAsync\s+Sub\s+\w+' -and $t -notmatch '\bHandles\b' -and $t -notmatch '\(\s*sender\s+As\b' -and $t -notmatch '\bAs\s+\w*EventArgs\b') { $sig.asyncSub.Add($n) }
            if ($t -match '^Catch\b') {
                # The block runs to the next Catch / Finally / End Try at the same depth.
                $depth = 0; $body = [System.Collections.Generic.List[string]]::new()
                for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                    $u = $lines[$j].Trim()
                    if ($u -match '^Try\b') { $depth++ }
                    if ($depth -eq 0 -and $u -match '^(Catch\b|Finally\b|End\s+Try\b)') { break }
                    if ($u -match '^End\s+Try\b') { $depth-- }
                    if (-not $u.StartsWith("'")) { $body.Add($u) }
                }
                $text = $body -join "`n"
                $handled = ($text -match '\bThrow\b') -or ($text -match '\bReturn\s+\S') -or
                           ($text -match '(AppFileLogger|\bLog\w*\s*\(|\.Log\w*\s*\(|Logger\.|ReportFault|UiFaultReporter)') -or
                           ($t -match '\bThrow\b')
                if (-not $handled) { $sig.silentCatch.Add($n) }
            }
            if ($t -match '(\bNew\s+(Bitmap|Font|Pen|\w*Brush|Icon|Metafile)\b|\bImage\.From\w+|\bBitmap\.From\w+|\bGraphics\.From\w+)' -and $t -notmatch '^(Using|Return)\b') { $sig.unownedGdi.Add($n) }
            if ($t -match '^(<[^>]*>\s*)*((Private|Public|Friend|Protected)\s+)?Shared\s+(?!ReadOnly\b|Function\b|Sub\b|Property\b|Operator\b|Event\b|Async\b|Iterator\b|Overrides\b|Overloads\b|Narrowing\b|Widening\b|Custom\b|Declare\b)(WithEvents\s+)?\w+\s+As\b') { $sig.sharedMutable.Add($n) }
            if ($t -match '(<DllImport|<LibraryImport|\bDeclare\s+(Auto\s+|Ansi\s+|Unicode\s+)?(Function|Sub)\b)') { $sig.pinvoke.Add($n) }
        } else {
            if ($t.StartsWith('#') -or $t.StartsWith(';') -or $t.StartsWith('//')) { continue }
            if ($t -match '\b(Remove-Item|Move-Item|Rename-Item|DeleteFile|DelTree|RenameFile)\b') { $sig.fileMutation.Add($n) }
            if ($t -match '\b(Start-Process|Exec\s*\(|ShellExec\s*\(|sc\.exe|netsh)\b') { $sig.processStart.Add($n) }
            if ($t -match '\b(catch\s*\{\s*\}|SilentlyContinue)\b') { $sig.silentCatch.Add($n) }
            continue
        }
        if ($t -match '\b(IO\.)?(File|Directory)\.(Delete|Move|Replace)\s*\(') { $sig.fileMutation.Add($n) }
        if ($t -match '\bProcess\.Start\s*\(') { $sig.processStart.Add($n) }
        if ($t -match '(\bSyncLock\b|\bTask\.Run\b|\bNew\s+(Threading\.)?Thread\s*\(|\bNew\s+[\w.]*Timer\b|\bThreadPool\.)') { $sig.concurrency.Add($n) }
    }
    [pscustomobject]$sig
}

# Judges handler symmetry across a slice and flattens every signal into sorted "path:line" hit lists.
function Get-SliceSignals($FileRecords, [hashtable]$SignalsByPath) {
    $removed = @{}
    foreach ($f in $FileRecords) { foreach ($e in $SignalsByPath[$f.Path].removeHandlerEvents) { $removed[$e.ToLowerInvariant()] = $true } }
    $names = 'addHandlerUnmatched', 'asyncSub', 'silentCatch', 'unownedGdi', 'fileMutation', 'processStart', 'pinvoke', 'sharedMutable', 'concurrency'
    $hits = [ordered]@{}
    foreach ($n in $names) { $hits[$n] = [System.Collections.Generic.List[string]]::new() }
    foreach ($f in $FileRecords) {
        $s = $SignalsByPath[$f.Path]
        foreach ($a in $s.addHandler) { if (-not $removed.ContainsKey($a.Event.ToLowerInvariant())) { $hits.addHandlerUnmatched.Add("$($f.Path):$($a.Line)") } }
        foreach ($n in $names | Select-Object -Skip 1) { foreach ($l in $s.$n) { $hits[$n].Add("$($f.Path):$l") } }
    }
    $hits
}

# The risk score: a separate function, so a new signal never changes the partition. Per 1,000 lines,
# with the divisor floored at a quarter of the line limit so a small slice does not lead on one hit.
# Changed lines weigh highest: one point per 5 changed lines, which on a tree just through a fix wave
# outweighs the static signals - they are the code no one has read as a whole.
$script:RiskWeights = [ordered]@{
    addHandlerUnmatched = 1; asyncSub = 2; silentCatch = 1; unownedGdi = 1; fileMutation = 2
    processStart = 2; pinvoke = 1; sharedMutable = 1; concurrency = 1; changedLinesPer = 5; finding045 = 3
}

function Get-RiskScore($Hits, [int]$ChangedLines, [int]$Findings045, [int]$Lines, [int]$MaxLines) {
    $points = 0.0
    foreach ($k in $Hits.Keys) { $points += $script:RiskWeights[$k] * $Hits[$k].Count }
    $points += $ChangedLines / $script:RiskWeights.changedLinesPer
    $points += $script:RiskWeights.finding045 * $Findings045
    $divisor = [math]::Max($Lines, [math]::Floor($MaxLines / 4))
    if ($divisor -le 0) { return 0.0 }
    [math]::Round($points * 1000.0 / $divisor, 1)
}

# --- git ---------------------------------------------------------------------

function Invoke-Git([string]$Root, [string[]]$GitArgs) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = @(& git -C $Root -c core.safecrlf=false @GitArgs 2>$null)
        [pscustomobject]@{ Code = $LASTEXITCODE; Out = $out }
    } finally { $ErrorActionPreference = $prev }
}

function Test-GitRepo([string]$Root) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $false }
    $r = Invoke-Git $Root @('rev-parse', '--show-toplevel')
    if ($r.Code -ne 0 -or -not $r.Out) { return $false }
    # A fixture folder inside the repository must not borrow the repository's history.
    $top = ([string]$r.Out[0]).Replace('\', '/').TrimEnd('/')
    $here = (Resolve-Path -LiteralPath $Root).Path.Replace('\', '/').TrimEnd('/')
    $top -ieq $here
}

function Resolve-Commit([string]$Root, [string]$Rev) {
    $r = Invoke-Git $Root @('rev-parse', '--verify', '--quiet', "$Rev^{commit}")
    if ($r.Code -ne 0 -or -not $r.Out) { return $null }
    ([string]$r.Out[0]).Trim()
}

# Changed lines (added + deleted) per path between a commit and the working tree; an untracked file
# counts every line.
function Get-ChangedLines([string]$Root, [string]$Commit, $Paths) {
    $map = @{}
    $r = Invoke-Git $Root @('diff', '--numstat', '--no-renames', $Commit, '--')
    foreach ($l in $r.Out) {
        $p = ([string]$l) -split "`t"
        if ($p.Count -lt 3) { continue }
        $a = 0; $d = 0
        [void][int]::TryParse($p[0], [ref]$a); [void][int]::TryParse($p[1], [ref]$d)
        $map[$p[2].ToLowerInvariant()] = $a + $d
    }
    $u = Invoke-Git $Root @('ls-files', '--others', '--exclude-standard')
    $untracked = @{}
    foreach ($l in $u.Out) { $untracked[([string]$l).ToLowerInvariant()] = $true }
    $res = @{}
    foreach ($f in $Paths) {
        $k = $f.Path.ToLowerInvariant()
        if ($untracked.ContainsKey($k)) { $res[$f.Path] = $f.Lines }
        elseif ($map.ContainsKey($k)) { $res[$f.Path] = $map[$k] }
        else { $res[$f.Path] = 0 }
    }
    $res
}

# --- 045 cross-reference -----------------------------------------------------

# Reads the finding sections ("### C-01 (P1, Confirmed) ..") of the given ticket files and returns
# one object per finding: Id, Severity, Ticket, Files (repository paths named in its body).
function Get-PriorFindings([string[]]$TicketFiles) {
    $res = [System.Collections.Generic.List[object]]::new()
    foreach ($tf in $TicketFiles) {
        $ticket = ([System.IO.Path]::GetFileName($tf) -split '_')[0]
        $cur = $null
        foreach ($line in [System.IO.File]::ReadAllLines($tf)) {
            if ($line -match '^###\s+([A-Z]-\d{2,3})\b(.*)$') {
                if ($cur) { $res.Add([pscustomobject]$cur) }
                $id = $Matches[1]; $rest = $Matches[2]; $sev = ''
                if ($rest -match '\b(P[1-4])\b') { $sev = $Matches[1] }
                $cur = [ordered]@{ Id = $id; Severity = $sev; Ticket = $ticket; Files = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase) }
                continue
            }
            if ($line -match '^#{1,3}\s') { if ($cur) { $res.Add([pscustomobject]$cur) }; $cur = $null; continue }
            if ($cur) {
                foreach ($m in [regex]::Matches($line, '((?:src|publishing)/[\w./ -]*?\.(?:vb|ps1|iss))\b')) { [void]$cur.Files.Add($m.Groups[1].Value) }
            }
        }
        if ($cur) { $res.Add([pscustomobject]$cur) }
    }
    @($res)
}

# --- deterministic JSON --------------------------------------------------------

function ConvertTo-JsonString([string]$s) {
    $sb = [System.Text.StringBuilder]::new('"')
    foreach ($ch in $s.ToCharArray()) {
        switch ($ch) {
            '"' { [void]$sb.Append('\"') }
            '\' { [void]$sb.Append('\\') }
            "`n" { [void]$sb.Append('\n') }
            "`r" { [void]$sb.Append('\r') }
            "`t" { [void]$sb.Append('\t') }
            default {
                if ([int]$ch -lt 0x20) { [void]$sb.Append(('\u{0:x4}' -f [int]$ch)) } else { [void]$sb.Append($ch) }
            }
        }
    }
    [void]$sb.Append('"')
    $sb.ToString()
}

# Keys sorted ordinally at every level, two-space indent, LF, invariant numbers: the same value always
# serializes to the same bytes.
function ConvertTo-StableJson($Value, [int]$Indent = 0) {
    $pad = '  ' * $Indent
    $pad1 = '  ' * ($Indent + 1)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    if ($Value -is [string]) { return ConvertTo-JsonString $Value }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
        return ([double]$Value).ToString('0.################', [cultureinfo]::InvariantCulture)
    }
    $dict = $null
    if ($Value -is [System.Collections.IDictionary]) {
        $dict = @{}; foreach ($k in $Value.Keys) { $dict[[string]$k] = $Value[$k] }
    } elseif ($Value -is [pscustomobject]) {
        $dict = @{}; foreach ($p in $Value.PSObject.Properties) { $dict[$p.Name] = $p.Value }
    }
    if ($null -ne $dict) {
        if ($dict.Count -eq 0) { return '{}' }
        $keys = [string[]]@($dict.Keys); [Array]::Sort($keys, [System.StringComparer]::Ordinal)
        $parts = foreach ($k in $keys) { $pad1 + (ConvertTo-JsonString $k) + ': ' + (ConvertTo-StableJson $dict[$k] ($Indent + 1)) }
        return "{`n" + ($parts -join ",`n") + "`n$pad}"
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @($Value)
        if ($items.Count -eq 0) { return '[]' }
        $parts = foreach ($it in $items) { $pad1 + (ConvertTo-StableJson $it ($Indent + 1)) }
        return "[`n" + ($parts -join ",`n") + "`n$pad]"
    }
    ConvertTo-JsonString ([string]$Value)
}

function Write-Utf8Lf([string]$Path, [string]$Text) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir -Force) }
    $Text = $Text.Replace("`r`n", "`n")
    if (-not $Text.EndsWith("`n")) { $Text += "`n" }
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Read-Manifest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

# --- slice files -------------------------------------------------------------

# A slice is an ordinary ticket: NNN_SPECIFICATION_AUDIT_SLICE_<S07_R_RESOURCES>.md in the
# specification folder, or in its done/ folder once it has shipped.
function Get-SliceToken([string]$Name) { $Name.ToUpperInvariant().Replace('-', '_') }

function Get-SliceFileName([string]$Number, [string]$Name) { "${Number}_SPECIFICATION_AUDIT_SLICE_$(Get-SliceToken $Name).md" }

function Find-SliceFile([string]$SpecDir, [string]$Name) {
    $pattern = '^\d{3}_SPECIFICATION_AUDIT_SLICE_' + [regex]::Escape((Get-SliceToken $Name)) + '\.md$'
    foreach ($d in $SpecDir, (Join-Path $SpecDir 'done')) {
        if (-not (Test-Path -LiteralPath $d)) { continue }
        $hit = Get-ChildItem -LiteralPath $d -Filter '*_SPECIFICATION_AUDIT_SLICE_*.md' -File | Where-Object { $_.Name -match $pattern } | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    $null
}

# State and findings of one slice file. State is the word on its "> Status: **X**" line; a slice
# counts as Done only when that says Done AND its "## Last Audit" block is filled.
function Read-SliceFile([string]$Path) {
    $text = [System.IO.File]::ReadAllText($Path)
    $state = ''
    $m = [regex]::Match($text, '(?m)^>\s*Status:\s*\*\*([A-Za-z]+)\*\*')
    if ($m.Success) { $state = $m.Groups[1].Value }
    $lastAudit = ''
    $m = [regex]::Match($text, '(?ms)^## Last Audit\s*\r?\n(.*)\z')
    if ($m.Success) { $lastAudit = $m.Groups[1].Value }
    $filled = [bool]($lastAudit -and ($lastAudit -notmatch 'NOT YET AUDITED') -and ($lastAudit.Trim().Length -gt 0))

    # Findings table rows: | 057.S07-3 | P2 | Confirmed | both | where | scenario | test | disposition |
    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($row in [regex]::Matches($text, '(?m)^\|\s*(057\.S\d{2,3}-\d+)\s*\|(.*)\|\s*$')) {
        $cells = @($row.Groups[2].Value -split '\|' | ForEach-Object { $_.Trim() })
        $sev = if ($cells.Count -ge 1) { $cells[0] } else { '' }
        $disp = if ($cells.Count -ge 1) { $cells[$cells.Count - 1] } else { '' }
        $findings.Add([pscustomobject]@{ Id = $row.Groups[1].Value; Severity = $sev; Disposition = $disp })
    }
    [pscustomobject]@{ State = $state; LastAuditFilled = $filled; LastAudit = $lastAudit; Findings = @($findings); Text = $text }
}
