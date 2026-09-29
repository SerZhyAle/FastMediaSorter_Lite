<#
.SYNOPSIS
    Partitions the audited code into slices and writes the campaign manifest (ticket 057, section 5.1).

.DESCRIPTION
    Walks the class A files (every shipped .vb under src/ except *.Designer.vb, plus the installer
    scripts under publishing/installer/), classifies each through the committed concern map
    tools/audit-concerns.json, and packs every concern into the fewest slices that hold both limits:

      - a concern smaller than a quarter of the line limit is merged into its neighbour in map order;
      - a larger one is split into balanced slices, keeping one group together where it fits
        (Main_Form.X.vb with its helper X.vb, MainWindow.*.vb, ..);
      - a file no concern matches is listed as unmapped - it is uncovered, never silently dropped.

    Each slice record carries its files and lines, its seam files, the lines changed since the
    baseline commit, the earlier audit's findings that name its files, its sibling slices and the
    pre-scan signals with a risk score. The same tree gives the same manifest, byte for byte.

    This tool writes a file and reports no verdict; tools/Audit-Status.ps1 judges the campaign.

.PARAMETER Baseline
    The commit the changed-line counts are measured from (default HEAD). The manifest records it,
    and the current HEAD beside it.

.PARAMETER FindingTickets
    Ticket files whose "### X-NN" sections are cross-referenced by file. Default: tickets 046-055 of
    the 2026-09-25 audit, wherever they are (docs/specifications or its done/ folder).

.EXAMPLE
    .\tools\Audit-Partition.ps1 -Baseline c5106da
#>
[CmdletBinding()]
param(
    [string]$Root,
    [string]$ConcernMap,
    [string]$OutDir,
    [int]$MaxFiles = 30,
    [int]$MaxLines = 3500,
    [switch]$IncludeClassB,
    [string]$Baseline = 'HEAD',
    [string[]]$FindingTickets
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\AuditCampaign.ps1')

if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = (Resolve-Path -LiteralPath $Root).Path
if (-not $ConcernMap) { $ConcernMap = Join-Path $Root 'tools\audit-concerns.json' }
if (-not $OutDir) { $OutDir = Join-Path $Root 'docs\specifications\057_full_code_audit' }
if ($MaxFiles -lt 1 -or $MaxLines -lt 1) { throw 'MaxFiles and MaxLines must be positive' }

$map = Read-ConcernMap $ConcernMap
$files = @(Get-AuditFiles $Root $map -IncludeClassB:$IncludeClassB)

# --- baseline and findings ---------------------------------------------------
$baselineRec = [ordered]@{ requested = $Baseline; commit = 'none'; head = 'none'; headDirty = $false }
$changed = @{}
if (Test-GitRepo $Root) {
    $commit = Resolve-Commit $Root $Baseline
    if (-not $commit) { throw "baseline '$Baseline' is not a commit in $Root" }
    $baselineRec.commit = $commit
    $baselineRec.head = Resolve-Commit $Root 'HEAD'
    $st = Invoke-Git $Root @('status', '--porcelain')
    $baselineRec.headDirty = [bool]($st.Out.Count -gt 0)
    $changed = Get-ChangedLines $Root $commit ($files | Where-Object { $_.Class -in 'A', 'B' })
}

if (-not $PSBoundParameters.ContainsKey('FindingTickets')) {
    $FindingTickets = @(
        foreach ($d in 'docs\specifications', 'docs\specifications\done') {
            $p = Join-Path $Root $d
            if (Test-Path -LiteralPath $p) {
                Get-ChildItem -LiteralPath $p -Filter '*.md' -File | Where-Object { $_.Name -match '^0(4[6-9]|5[0-5])_' } | ForEach-Object { $_.FullName }
            }
        }
    )
}
$FindingTickets = @($FindingTickets | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Sort-Object { [System.IO.Path]::GetFileName($_) })
$prior = @(Get-PriorFindings $FindingTickets)
$findingsByPath = @{}
foreach ($f in $prior) {
    foreach ($p in $f.Files) {
        $k = $p.ToLowerInvariant()
        if (-not $findingsByPath.ContainsKey($k)) { $findingsByPath[$k] = [System.Collections.Generic.List[object]]::new() }
        $findingsByPath[$k].Add($f)
    }
}

# --- units: concerns after the small-concern merge ---------------------------
$auditable = @($files | Where-Object { $_.Class -in 'A', 'B' -and $_.Concern })
$unmapped = @($files | Where-Object { $_.Class -in 'A', 'B' -and -not $_.Concern })
$activeConcerns = @($map.concerns | Where-Object { $IncludeClassB -or -not (Test-IsClassBConcern $_) })
$quarter = [math]::Floor($MaxLines / 4)

$units = [System.Collections.Generic.List[object]]::new()
$carry = $null
foreach ($c in $activeConcerns) {
    $cf = @($auditable | Where-Object { $_.Concern -eq $c.id })
    if ($cf.Count -eq 0) { continue }
    $lines = ($cf | Measure-Object -Property Lines -Sum).Sum
    $isB = Test-IsClassBConcern $c
    $prev = if ($units.Count -gt 0) { $units[$units.Count - 1] } else { $null }
    if ($carry -and $carry.ClassB -eq $isB) {
        $cf = @($carry.Files) + $cf; $mergedFrom = @($carry.Ids); $carry = $null
    } else { $mergedFrom = @() }
    if ($lines -lt $quarter -and $mergedFrom.Count -eq 0) {
        if ($prev -and $prev.ClassB -eq $isB) {
            $prev.Files = @($prev.Files) + $cf
            $prev.MergedFrom = @($prev.MergedFrom) + @($c.id)
            continue
        }
        $carry = [pscustomobject]@{ Ids = @($c.id); Files = $cf; ClassB = $isB }
        continue
    }
    $units.Add([pscustomobject]@{
        Id = [string]$c.id; Title = [string]$c.title; Area = [string]$c.area045; ClassB = $isB
        MergedFrom = $mergedFrom; Files = $cf
    })
}
if ($carry) {
    # A small concern with no neighbour of its class stands alone.
    $c0 = $activeConcerns | Where-Object { $_.id -eq $carry.Ids[0] }
    $units.Add([pscustomobject]@{ Id = $carry.Ids[0]; Title = [string]$c0.title; Area = [string]$c0.area045; ClassB = $carry.ClassB; MergedFrom = @(); Files = $carry.Files })
}

# --- split: balanced bins that keep groups together --------------------------
function Split-Unit($Files) {
    $groups = [System.Collections.Generic.List[object]]::new()
    foreach ($g in ($Files | Group-Object -Property Group)) {
        $gl = ($g.Group | Measure-Object -Property Lines -Sum).Sum
        if ($g.Count -gt 1 -and ($gl -gt $MaxLines -or $g.Count -gt $MaxFiles)) {
            foreach ($f in $g.Group) { $groups.Add([pscustomobject]@{ Key = $f.Path; Files = @($f); Lines = $f.Lines }) }
        } else {
            $groups.Add([pscustomobject]@{ Key = [string]$g.Name; Files = @($g.Group); Lines = $gl })
        }
    }
    $sorted = @($groups | Sort-Object -Property @{ Expression = 'Lines'; Descending = $true }, @{ Expression = { $_.Key }; Descending = $false })
    $total = ($Files | Measure-Object -Property Lines -Sum).Sum
    $n = [math]::Max(1, [math]::Max([math]::Ceiling($total / $MaxLines), [math]::Ceiling($Files.Count / $MaxFiles)))
    while ($true) {
        $bins = @(for ($i = 0; $i -lt $n; $i++) { [pscustomobject]@{ Lines = 0; Count = 0; Files = [System.Collections.Generic.List[object]]::new() } })
        $ok = $true
        foreach ($g in $sorted) {
            $best = $null
            foreach ($b in $bins) {
                $fits = ($b.Lines + $g.Lines -le $MaxLines -and $b.Count + $g.Files.Count -le $MaxFiles) -or ($b.Count -eq 0)
                if ($fits -and (-not $best -or $b.Lines -lt $best.Lines)) { $best = $b }
            }
            if (-not $best) { $ok = $false; break }
            $best.Lines += $g.Lines; $best.Count += $g.Files.Count
            foreach ($f in $g.Files) { $best.Files.Add($f) }
        }
        if ($ok) { break }
        $n++
    }
    $res = [System.Collections.Generic.List[object]]::new()
    foreach ($b in $bins) { if ($b.Count -gt 0) { $res.Add([object[]]@(Sort-ByPath $b.Files)) } }
    $res.Sort([System.Comparison[object]] { param($x, $y) [string]::CompareOrdinal([string]$x[0].Path, [string]$y[0].Path) })
    , $res.ToArray()
}

# --- slice records -----------------------------------------------------------
$slices = [System.Collections.Generic.List[object]]::new()
$sigByPath = @{}
foreach ($f in $auditable) { $sigByPath[$f.Path] = Get-FileSignals $f.Full }

$planned = foreach ($u in $units) {
    $bins = Split-Unit $u.Files
    for ($i = 0; $i -lt $bins.Count; $i++) {
        [pscustomobject]@{ Unit = $u; Part = $i + 1; Parts = $bins.Count; Files = $bins[$i] }
    }
}
$planned = @($planned)
$width = [math]::Max(2, ([string]$planned.Count).Length)
for ($i = 0; $i -lt $planned.Count; $i++) {
    $p = $planned[$i]
    $name = 'S' + ([string]($i + 1)).PadLeft($width, '0') + '-' + $p.Unit.Id
    if ($p.Parts -gt 1) { $name += "-$($p.Part)" }
    $p | Add-Member -NotePropertyName Name -NotePropertyValue $name
}

$warnings = [System.Collections.Generic.List[string]]::new()
foreach ($p in $planned) {
    $fileRecs = foreach ($f in $p.Files) {
        $ids = @()
        $k = $f.Path.ToLowerInvariant()
        if ($findingsByPath.ContainsKey($k)) { $ids = @($findingsByPath[$k] | ForEach-Object { $_.Id } | Sort-Object -Unique) }
        [ordered]@{
            path = $f.Path; lines = [int]$f.Lines; seam = [bool]$sigByPath[$f.Path].seam
            changedLines = [int]$(if ($changed.ContainsKey($f.Path)) { $changed[$f.Path] } else { 0 })
            findings045 = @($ids)
        }
    }
    $fileRecs = @($fileRecs)
    $hits = Get-SliceSignals $p.Files $sigByPath
    $lines = [int](($p.Files | Measure-Object -Property Lines -Sum).Sum)
    $chg = [int](($fileRecs | ForEach-Object { $_.changedLines } | Measure-Object -Sum).Sum)
    $fset = @{}
    foreach ($f in $p.Files) {
        $k = $f.Path.ToLowerInvariant()
        if ($findingsByPath.ContainsKey($k)) { foreach ($x in $findingsByPath[$k]) { $fset[$x.Id] = $x } }
    }
    $fids = [string[]]@($fset.Keys); [Array]::Sort($fids, [System.StringComparer]::Ordinal)
    $findingRecs = @(foreach ($id in $fids) { [ordered]@{ id = $id; severity = $fset[$id].Severity; ticket = $fset[$id].Ticket } })
    $risk = Get-RiskScore $hits $chg $findingRecs.Count $lines $MaxLines
    $siblings = @($planned | Where-Object { $_.Unit.Id -eq $p.Unit.Id -and $_.Name -ne $p.Name } | ForEach-Object { $_.Name })
    $sigRec = [ordered]@{}
    foreach ($k in $hits.Keys) { $sigRec[$k] = @($hits[$k]) }
    if ($p.Files.Count -le 2 -and $p.Parts -gt 1) { $warnings.Add("$($p.Name) holds $($p.Files.Count) file(s): a split leftover - consider a larger -MaxLines or a concern-map change") }

    $slices.Add([ordered]@{
        name = $p.Name; concern = $p.Unit.Id; concernTitle = $p.Unit.Title; area045 = $p.Unit.Area
        mergedFrom = @($p.Unit.MergedFrom); part = $p.Part; parts = $p.Parts; classB = [bool]$p.Unit.ClassB
        files = $fileRecs; fileCount = $fileRecs.Count; lines = $lines
        seamFiles = @($fileRecs | Where-Object { $_.seam }).Count; changedLines = $chg
        findings045 = $findingRecs; siblings = $siblings; signals = $sigRec; risk = $risk
        dataSample = @()
    })
}

# The string tables are covered by tests; the slice that holds their sampling concern samples them.
$data = @($files | Where-Object { $_.Class -eq 'A-data' })
$dataRecs = foreach ($d in $data) {
    $holder = ''
    foreach ($s in $slices) {
        if ($s.concern -eq $d.SampledBy -or @($s.mergedFrom) -contains $d.SampledBy) {
            $srcFiles = @($auditable | Where-Object { $_.Concern -eq $d.SampledBy } | ForEach-Object { $_.Path })
            if (@($s.files | Where-Object { $srcFiles -contains $_.path }).Count -gt 0) { $holder = $s.name; break }
        }
    }
    if ($holder) { ($slices | Where-Object { $_.name -eq $holder }).dataSample += $d.Path }
    else { $warnings.Add("$($d.Path): its sampling concern '$($d.SampledBy)' is in no slice") }
    [ordered]@{ path = $d.Path; lines = [int]$d.Lines; coveredBy = $d.CoveredBy; sampledBy = $holder }
}
foreach ($s in $slices) { $s.dataSample = @($s.dataSample | Sort-Object) }

$riskOrder = @($slices | Sort-Object -Property @{ Expression = { $_.risk }; Descending = $true }, @{ Expression = { $_.name }; Descending = $false } | ForEach-Object { $_.name })
$excluded = @($files | Where-Object { $_.Class -eq 'excluded' } | ForEach-Object { [ordered]@{ path = $_.Path; lines = [int]$_.Lines; reason = $_.Reason } })
$unmappedRecs = @($unmapped | ForEach-Object { [ordered]@{ path = $_.Path; lines = [int]$_.Lines } })
$classA = @($auditable + $unmapped | Where-Object { $_.Class -eq 'A' })

$findingSources = @($FindingTickets | ForEach-Object { if ($_.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { ConvertTo-RelPath $Root $_ } else { $_.Replace('\', '/') } })
$manifest = [ordered]@{
    campaign = '057'; schema = 1
    baseline = $baselineRec
    limits = [ordered]@{ maxFiles = $MaxFiles; maxLines = $MaxLines }
    includeClassB = [bool]$IncludeClassB
    concernMap = $(if ($ConcernMap.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { ConvertTo-RelPath $Root $ConcernMap } else { $ConcernMap.Replace('\', '/') })
    findingSources = $findingSources
    riskWeights = $script:RiskWeights
    totals = [ordered]@{
        classAFiles = $classA.Count; classALines = [int](($classA | Measure-Object -Property Lines -Sum).Sum)
        classBFiles = @($auditable + $unmapped | Where-Object { $_.Class -eq 'B' }).Count
        dataFiles = $data.Count; dataLines = [int](($data | Measure-Object -Property Lines -Sum).Sum)
        excludedFiles = $excluded.Count; unmappedFiles = $unmappedRecs.Count; slices = $slices.Count
    }
    slices = @($slices); riskOrder = $riskOrder
    data = @($dataRecs); excluded = $excluded; unmapped = $unmappedRecs
    warnings = @($warnings | Sort-Object)
}

$outFile = Join-Path $OutDir 'manifest.json'
Write-Utf8Lf $outFile (ConvertTo-StableJson $manifest)

# --- report ------------------------------------------------------------------
Write-Host "Audit-Partition: $($slices.Count) slices from $($classA.Count) class A files ($($manifest.totals.classALines) lines); limits $MaxFiles files / $MaxLines lines; baseline $($baselineRec.commit)"
foreach ($s in $slices) {
    $merged = if (@($s.mergedFrom).Count) { " (+ $(@($s.mergedFrom) -join ', '))" } else { '' }
    Write-Host ("  {0,-30} {1,3} files {2,6} lines  risk {3,7}  changed {4,6}  045 {5,3}{6}" -f $s.name, $s.fileCount, $s.lines, $s.risk, $s.changedLines, @($s.findings045).Count, $merged)
}
Write-Host "  data (covered by tests): $($data.Count) files; excluded: $($excluded.Count); unmapped: $($unmappedRecs.Count)"
foreach ($u in $unmappedRecs) { Write-Host "  UNMAPPED $($u.path)" -ForegroundColor Yellow }
foreach ($w in $warnings) { Write-Host "  note: $w" -ForegroundColor Yellow }
Write-Host "  manifest: $outFile"
