<#
.SYNOPSIS
    Turns every slice of the audit manifest into an ordinary specification ticket and queues it
    (ticket 057, section 5.2).

.DESCRIPTION
    Reads manifest.json (written by tools/Audit-Partition.ps1) and, in risk order, creates
    NNN_SPECIFICATION_AUDIT_SLICE_<S07_R_RESOURCES>.md in the specification folder from
    tools/audit-slice-template.md for every slice that has no ticket yet - in that folder or in its
    done/ folder. Each new ticket takes the next number README.md names, gets its row in README's
    ticket table, and the "next available number" there moves on. An existing slice ticket is never
    rewritten - it may hold an audit in progress.

    Appends one line per new slice to RELEASE_QUEUE.md, as a block under the umbrella's line (after
    the block's last line once it exists), status Ready. It never reorders or edits an existing line:
    the order of the queue is the owner's. A second run creates nothing and changes no byte.

    -Tail collects every class A file of the current tree that is in no slice (a file added after the
    partition) into one more slice, recorded in the manifest, before the campaign is closed.

    This tool writes files and reports no verdict; tools/Audit-Status.ps1 judges the campaign.
#>
[CmdletBinding()]
param(
    [string]$Root,
    [string]$OutDir,
    [string]$SpecDir,
    [string]$Readme,
    [string]$Queue,
    [string]$Template,
    [string]$Registry,
    [string]$Umbrella = '057_SPECIFICATION_',
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [switch]$Tail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\AuditCampaign.ps1')

if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = (Resolve-Path -LiteralPath $Root).Path
if (-not $OutDir) { $OutDir = Join-Path $Root 'docs\specifications\057_full_code_audit' }
if (-not $SpecDir) { $SpecDir = Join-Path $Root 'docs\specifications' }
if (-not $Readme) { $Readme = Join-Path $SpecDir 'README.md' }
if (-not $Queue) { $Queue = Join-Path $SpecDir 'RELEASE_QUEUE.md' }
if (-not $Template) { $Template = Join-Path $PSScriptRoot 'audit-slice-template.md' }
if (-not $Registry) {
    $Registry = @(
        foreach ($d in 'docs\specifications', 'docs\specifications\done') {
            $p = Join-Path $Root $d
            if (Test-Path -LiteralPath $p) { Get-ChildItem -LiteralPath $p -Filter '045_*.md' -File | ForEach-Object { $_.FullName } }
        }
    ) | Select-Object -First 1
}

$manifestPath = Join-Path $OutDir 'manifest.json'
$manifest = Read-Manifest $manifestPath
if (-not $manifest) { throw "no manifest at $manifestPath - run tools/Audit-Partition.ps1 first" }
if (-not (Test-Path -LiteralPath $Template)) { throw "slice template not found: $Template" }
if (-not (Test-Path -LiteralPath $Queue)) { throw "queue not found: $Queue" }
if (-not (Test-Path -LiteralPath $Readme)) { throw "ticket index not found: $Readme" }

# --- tail: files of the current tree that no slice holds ---------------------
if ($Tail) {
    $mapPath = Join-Path $Root $manifest.concernMap
    $map = Read-ConcernMap $mapPath
    $files = @(Get-AuditFiles $Root $map -IncludeClassB:([bool]$manifest.includeClassB))
    $held = @{}
    foreach ($s in $manifest.slices) { foreach ($f in $s.files) { $held[$f.path.ToLowerInvariant()] = $true } }
    $loose = @($files | Where-Object { $_.Class -in 'A', 'B' -and -not $held.ContainsKey($_.Path.ToLowerInvariant()) })
    if ($loose.Count -eq 0) {
        Write-Host 'Audit-FanOut: -Tail found no class A file outside a slice'
    } else {
        $n = @($manifest.slices).Count + 1
        $width = [math]::Max(2, ([string]$n).Length)
        $name = 'S' + ([string]$n).PadLeft($width, '0') + '-tail'
        if (@($manifest.slices | Where-Object { $_.name -eq $name }).Count) { throw "the manifest already holds $name" }
        $sig = @{}
        foreach ($f in $loose) { $sig[$f.Path] = Get-FileSignals $f.Full }
        $hits = Get-SliceSignals $loose $sig
        $sigRec = [ordered]@{}; foreach ($k in $hits.Keys) { $sigRec[$k] = @($hits[$k]) }
        $lines = [int](($loose | Measure-Object -Property Lines -Sum).Sum)
        $rec = [ordered]@{
            name = $name; concern = 'tail'; concernTitle = 'files added after the partition'; area045 = ''
            mergedFrom = @(); part = 1; parts = 1; classB = $false; tail = $true
            files = @($loose | ForEach-Object { [ordered]@{ path = $_.Path; lines = [int]$_.Lines; seam = [bool]$sig[$_.Path].seam; changedLines = 0; findings045 = @() } })
            fileCount = $loose.Count; lines = $lines; seamFiles = @($loose | Where-Object { $sig[$_.Path].seam }).Count
            changedLines = 0; findings045 = @(); siblings = @(); signals = $sigRec
            risk = (Get-RiskScore $hits 0 0 $lines ([int]$manifest.limits.maxLines)); dataSample = @()
        }
        $raw = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        $raw.slices = @($raw.slices) + @($rec)
        $raw.riskOrder = @($raw.riskOrder) + @($name)
        Write-Utf8Lf $manifestPath (ConvertTo-StableJson $raw)
        $manifest = Read-Manifest $manifestPath
        Write-Host "Audit-FanOut: tail slice $name holds $($loose.Count) file(s)"
    }
}

# --- the earlier registry: finding titles and the "found sound" coverage ------
$titles = @{}
$sound = @{}
if ($Registry -and (Test-Path -LiteralPath $Registry)) {
    $inSound = $false; $cur = $null
    foreach ($line in [System.IO.File]::ReadAllLines($Registry)) {
        if ($line -match '^\|\s*([A-Z]-\d{2,3})\s*\|\s*(P[1-4])\s*\|\s*([^|]*)\|\s*([^|]*)\|\s*([^|]*)\|\s*([^|]*)\|') {
            $titles[$Matches[1]] = [pscustomobject]@{ Confidence = $Matches[3].Trim(); Runtime = $Matches[4].Trim(); Title = $Matches[5].Trim() }
        }
        if ($line -match '^## 7\.') { $inSound = $true; continue }
        if ($inSound -and $line -match '^## ') { $inSound = $false; $cur = $null }
        if (-not $inSound) { continue }
        if ($line -match '^- \*\*([A-Z]) - ') { $cur = $Matches[1]; $sound[$cur] = [System.Collections.Generic.List[string]]::new(); $sound[$cur].Add($line); continue }
        if ($cur -and $line -match '^\s+\S') { $sound[$cur].Add($line) }
        elseif ($cur -and $line.Trim() -eq '') { }
        else { $cur = $null }
    }
}

# --- slice files ---------------------------------------------------------------
$templateText = [System.IO.File]::ReadAllText($Template)
$order = @($manifest.riskOrder)
foreach ($s in $manifest.slices) { if ($order -notcontains $s.name) { $order += $s.name } }
$byName = @{}; foreach ($s in $manifest.slices) { $byName[$s.name] = $s }
$created = [System.Collections.Generic.List[string]]::new()

# --- ticket numbers: README.md names the next one, and a number is never reused ------
function Read-TextLines([string]$Path) {
    $text = [System.IO.File]::ReadAllText($Path)
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in ($text -split "`r?`n")) { $lines.Add($l) }
    $trailing = ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '')
    if ($trailing) { $lines.RemoveAt($lines.Count - 1) }
    [pscustomobject]@{ Lines = $lines; Nl = $nl; Trailing = $trailing }
}
function Write-TextLines([string]$Path, $Doc) {
    $out = ($Doc.Lines -join $Doc.Nl) + $(if ($Doc.Trailing) { $Doc.Nl } else { '' })
    [System.IO.File]::WriteAllText($Path, $out, [System.Text.UTF8Encoding]::new($false))
}

$readmeDoc = Read-TextLines $Readme
$nextAt = -1; $next = 0
for ($i = 0; $i -lt $readmeDoc.Lines.Count; $i++) {
    $m = [regex]::Match($readmeDoc.Lines[$i], '\*\*(\d{3})\*\*')
    if ($m.Success -and ($readmeDoc.Lines[$i] -match 'next available number' -or ($i -gt 0 -and $readmeDoc.Lines[$i - 1] -match 'next available number'))) {
        $nextAt = $i; $next = [int]$m.Groups[1].Value; break
    }
}
if ($nextAt -lt 0) { throw "$Readme does not name the next available ticket number (**NNN**)" }
$used = @{}
foreach ($d in $SpecDir, (Join-Path $SpecDir 'done')) {
    if (Test-Path -LiteralPath $d) { Get-ChildItem -LiteralPath $d -File | ForEach-Object { if ($_.Name -match '^(\d{3})_') { $used[[int]$Matches[1]] = $true } } }
}
foreach ($l in $readmeDoc.Lines) { if ($l -match '^\|\s*(\d{3})\s*\|') { $used[[int]$Matches[1]] = $true } }
$lastRow = -1
for ($i = 0; $i -lt $readmeDoc.Lines.Count; $i++) { if ($readmeDoc.Lines[$i] -match '^\|\s*\d{3}\s*\|') { $lastRow = $i } }
if ($lastRow -lt 0) { throw "$Readme has no ticket table (rows '| NNN | .. |')" }
$ticketOf = @{}

function Format-Hits($list, [int]$cap = 40) {
    $all = @($list)
    if ($all.Count -eq 0) { return 'none' }
    $shown = ($all | Select-Object -First $cap | ForEach-Object { "``$_``" }) -join ', '
    if ($all.Count -gt $cap) { $shown += " .. and $($all.Count - $cap) more (see manifest.json)" }
    $shown
}

$signalNames = [ordered]@{
    addHandlerUnmatched = '`AddHandler` with no `RemoveHandler` for that event in this slice'
    asyncSub = '`Async Sub` that is not an event handler'
    silentCatch = '`Catch` that neither logs (outside `Debug`), rethrows nor returns a value'
    unownedGdi = '`Bitmap`/`Image`/`Graphics`/`Font`/`Brush`/`Pen`/`Icon` created outside `Using` and not returned'
    fileMutation = 'file or directory delete / move / replace'
    processStart = 'a process started'
    pinvoke = 'P/Invoke declaration'
    sharedMutable = '`Shared` mutable field'
    concurrency = '`SyncLock`, `Task.Run`, a thread, a timer or the thread pool'
}

for ($rank = 0; $rank -lt $order.Count; $rank++) {
    $s = $byName[$order[$rank]]
    $existing = Find-SliceFile $SpecDir $s.name
    if ($existing) { $ticketOf[$s.name] = [System.IO.Path]::GetFileName($existing); continue }
    while ($used.ContainsKey($next)) { $next++ }
    $number = ([string]$next).PadLeft(3, '0')
    $used[$next] = $true; $next++
    $fileName = Get-SliceFileName $number $s.name
    $path = Join-Path $SpecDir $fileName
    $ticketOf[$s.name] = $fileName

    $fileRows = ($s.files | ForEach-Object {
        $seam = if ($_.seam) { 'yes' } else { '' }
        $f = @($_.findings045) -join ', '
        "| ``$($_.path)`` | $($_.lines) | $seam | $($_.changedLines) | $f |"
    }) -join "`n"
    $dataSample = ''
    if (@($s.dataSample).Count -gt 0) {
        $dataSample = "`nString tables sampled by this slice (class A-data: held by LocalizationParityTests and " +
            "LocalizationCoverageTests, not read line by line). Sample them for the two things the tests cannot see: a smart " +
            "double quote (U+201C/U+201D) in a comment-adjacent literal, and a ``TF`` placeholder that is present but in the " +
            "wrong position for the sentence.`n`n" + ((@($s.dataSample) | ForEach-Object { "- ``$_``" }) -join "`n") + "`n"
    }
    $sigText = ($signalNames.Keys | ForEach-Object {
        $list = @($s.signals.$_)
        "- **$($signalNames[$_])** ($($list.Count)): $(Format-Hits $list)"
    }) -join "`n"
    $f045 = if (@($s.findings045).Count -eq 0) { 'None name these files.' } else {
        "| ID | Sev | Confidence | Runtime | Finding | Ticket |`n| --- | --- | --- | --- | --- | --- |`n" + (($s.findings045 | ForEach-Object {
            $t = $titles[$_.id]
            if ($t) { "| $($_.id) | $($_.severity) | $($t.Confidence) | $($t.Runtime) | $($t.Title) | $($_.ticket) |" }
            else { "| $($_.id) | $($_.severity) | | | (see ticket) | $($_.ticket) |" }
        }) -join "`n")
    }
    $soundText = if ($s.area045 -and $sound.ContainsKey([string]$s.area045)) { ($sound[[string]$s.area045] -join "`n") }
                 elseif (-not $s.area045) { '045 had no area for this concern: there is no earlier "found sound" record, so everything here is read fresh.' }
                 else { '(045 registry not found)' }
    $siblings = if (@($s.siblings).Count) { (@($s.siblings) | ForEach-Object { "``$_``" }) -join ', ' } else { 'none.' }
    $merged = ''
    if (@($s.mergedFrom).Count) {
        $mergedList = (@($s.mergedFrom) | ForEach-Object { '`' + $_ + '`' }) -join ', '
        $merged = ' (with the small concern(s) ' + $mergedList + ' merged in)'
    }
    $num = ($s.name -split '-')[0]

    $text = $templateText
    $vals = [ordered]@{
        NAME = $s.name; NUM = $num; NUMBER = $number; TITLE = $s.concernTitle; CONCERN = $s.concern; MERGED = $merged
        PART = "$($s.part) of $($s.parts)"; DATE = $Date
        BASELINE = $(if ($manifest.baseline.commit.Length -gt 7) { $manifest.baseline.commit.Substring(0, 7) } else { $manifest.baseline.commit })
        HEAD = $(if ($manifest.baseline.head.Length -gt 7) { $manifest.baseline.head.Substring(0, 7) } else { $manifest.baseline.head })
        RISK = [string]$s.risk; RANK = [string]($rank + 1); TOTAL = [string]$order.Count
        FILECOUNT = [string]$s.fileCount; LINES = [string]$s.lines; SEAMS = [string]$s.seamFiles; CHANGED = [string]$s.changedLines
        FILES = $fileRows; DATASAMPLE = $dataSample; SIGNALS = $sigText; FINDINGS045 = $f045; SOUND = $soundText; SIBLINGS = $siblings
    }
    foreach ($k in $vals.Keys) { $text = $text.Replace("{{$k}}", [string]$vals[$k]) }
    Write-Utf8Lf $path $text
    $created.Add("$number $($s.name)")
    $lastRow++
    $readmeDoc.Lines.Insert($lastRow, "| $number | Audit slice 057.$($s.name): $($s.concernTitle) | Ready $Date; one slice of 057 (risk rank $($rank + 1) of $($order.Count), $($s.fileCount) files, $($s.lines) lines); self-contained - no need to open 057 |")
}
if ($created.Count -gt 0) {
    while ($used.ContainsKey($next)) { $next++ }
    $readmeDoc.Lines[$nextAt] = [regex]::Replace($readmeDoc.Lines[$nextAt], '\*\*\d{3}\*\*', '**' + ([string]$next).PadLeft(3, '0') + '**')
    Write-TextLines $Readme $readmeDoc
}

# --- the queue block -----------------------------------------------------------
$qDoc = Read-TextLines $Queue
$qLines = $qDoc.Lines
$umbrellaAt = -1
for ($i = 0; $i -lt $qLines.Count; $i++) { if ($qLines[$i] -match [regex]::Escape($Umbrella)) { $umbrellaAt = $i; break } }
if ($umbrellaAt -lt 0) { throw "the queue has no line for the umbrella ticket ($Umbrella)" }

$lastSlice = -1
$present = @{}
for ($i = 0; $i -lt $qLines.Count; $i++) {
    $m = [regex]::Match($qLines[$i], '\d{3}_SPECIFICATION_AUDIT_SLICE_(S\d{2,3}_[A-Z0-9_]+)\.md')
    if ($m.Success) { $present[$m.Groups[1].Value] = $true; $lastSlice = $i }
}
$insertAt = $(if ($lastSlice -ge 0) { $lastSlice + 1 } else { $umbrellaAt + 1 })
$added = 0
foreach ($name in $order) {
    if ($present.ContainsKey((Get-SliceToken $name))) { continue }
    $qLines.Insert($insertAt, ('--  {0}  {1}  Ready' -f $ticketOf[$name].PadRight(55), $Date))
    $insertAt++; $added++
}
if ($added -gt 0) { Write-TextLines $Queue $qDoc }

Write-Host "Audit-FanOut: $($created.Count) slice ticket(s) created, $(@($manifest.slices).Count - $created.Count) already present; $added queue line(s) added"
foreach ($c in $created) { Write-Host "  + $c" }
