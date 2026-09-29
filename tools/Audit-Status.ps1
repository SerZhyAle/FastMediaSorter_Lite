<#
.SYNOPSIS
    Measures the state of the pre-release full code audit (ticket 057, section 5.4) and ends in one
    CHECK-VERDICT line.

.DESCRIPTION
    Reads the manifest (tools/Audit-Partition.ps1), the current tree and the slice tickets
    (NNN_SPECIFICATION_AUDIT_SLICE_*.md, written by tools/Audit-FanOut.ps1, in the specification
    folder or its done/ folder), and reports:
      - class A files of the current tree in no slice (uncovered, unmapped included) and in two
        slices (doubled); files a slice lists that the tree no longer has;
      - slices by state - Ready / Partial / Done - where Done needs both the status line and a
        filled "## Last Audit";
      - findings by severity, and every P1/P2 with neither a ticket nor an inline fix with evidence;
      - the tickets the slices spawned and their status in RELEASE_QUEUE.md.

    The campaign is red by design until its last slice closes, so this check is hand-run by whoever
    closes ticket 057 (tools/check-placement.jsonl), never part of a test run.

.PARAMETER Summary
    Also write summary.md beside the manifest: the campaign summary that becomes 057's "## Last Audit".

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - PASS: no uncovered or doubled file, every slice Done, every P1/P2 accounted for;
      1 - FAIL: any of those not true (each named above the verdict line);
      2 - COULD NOT VERIFY: no manifest, a missing or unreadable slice file, or a baseline commit that
          is not in the repository.
    The last line is the verdict, `Audit-Status: <WORD> (..)`.
#>
[CmdletBinding()]
param(
    [string]$Root,
    [string]$OutDir,
    [string]$SpecDir,
    [string]$Queue,
    [switch]$Summary
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
. (Join-Path $PSScriptRoot 'lib\AuditCampaign.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Audit-Status' 2 'the check itself failed'
}

if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$Root = (Resolve-Path -LiteralPath $Root).Path
if (-not $OutDir) { $OutDir = Join-Path $Root 'docs\specifications\057_full_code_audit' }
if (-not $SpecDir) { $SpecDir = Join-Path $Root 'docs\specifications' }
if (-not $Queue) { $Queue = Join-Path $SpecDir 'RELEASE_QUEUE.md' }

Write-Subject 'Audit-Status' "the audit campaign in $(ConvertTo-RelPath $Root $OutDir) against $(Get-TreeLabel $Root)"

$manifestPath = Join-Path $OutDir 'manifest.json'
$manifest = $null
try { $manifest = Read-Manifest $manifestPath } catch { Exit-Verdict 'Audit-Status' 2 "manifest.json cannot be read: $($_.Exception.Message)" }
if (-not $manifest) { Exit-Verdict 'Audit-Status' 2 "no manifest at $manifestPath - run tools/Audit-Partition.ps1" }

if ($manifest.baseline.commit -and $manifest.baseline.commit -ne 'none') {
    if (-not (Test-GitRepo $Root) -or -not (Resolve-Commit $Root $manifest.baseline.commit)) {
        Exit-Verdict 'Audit-Status' 2 "baseline commit $($manifest.baseline.commit) is not in this repository"
    }
}

$problems = [System.Collections.Generic.List[string]]::new()

# --- coverage ------------------------------------------------------------------
$map = Read-ConcernMap (Join-Path $Root $manifest.concernMap)
$files = @(Get-AuditFiles $Root $map -IncludeClassB:([bool]$manifest.includeClassB))
$auditable = @($files | Where-Object { $_.Class -in 'A', 'B' })
$owners = @{}
foreach ($s in $manifest.slices) {
    foreach ($f in $s.files) {
        $k = $f.path.ToLowerInvariant()
        if (-not $owners.ContainsKey($k)) { $owners[$k] = [System.Collections.Generic.List[string]]::new() }
        $owners[$k].Add($s.name)
    }
}
$uncovered = @($auditable | Where-Object { -not $owners.ContainsKey($_.Path.ToLowerInvariant()) })
$doubled = @($owners.Keys | Where-Object { $owners[$_].Count -gt 1 } | Sort-Object)
$treeSet = @{}; foreach ($f in $auditable) { $treeSet[$f.Path.ToLowerInvariant()] = $true }
$gone = @($owners.Keys | Where-Object { -not $treeSet.ContainsKey($_) } | Sort-Object)

foreach ($u in $uncovered) {
    $why = if ($u.Concern) { "concern $($u.Concern), added after the partition" } else { 'unmapped - no concern in the map matches it' }
    $problems.Add("uncovered: $($u.Path) ($why)")
}
foreach ($d in $doubled) { $problems.Add("doubled: $d is in $($owners[$d] -join ' and ')") }

# --- slices ----------------------------------------------------------------------
$states = [ordered]@{ Ready = 0; Partial = 0; Done = 0 }
$bySev = [ordered]@{ P1 = 0; P2 = 0; P3 = 0; P4 = 0 }
$spawned = @{}
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($s in $manifest.slices) {
    $path = Find-SliceFile $SpecDir $s.name
    if (-not $path) { Exit-Verdict 'Audit-Status' 2 "no ticket for slice $($s.name) (NNN_SPECIFICATION_AUDIT_SLICE_$(Get-SliceToken $s.name).md) - run tools/Audit-FanOut.ps1" }
    try { $r = Read-SliceFile $path } catch { Exit-Verdict 'Audit-Status' 2 "slice ticket unreadable: $([System.IO.Path]::GetFileName($path))" }

    $state = $r.State
    if ($state -notin 'Ready', 'Partial', 'Done') { $problems.Add("$($s.name): status '$state' is not Ready, Partial or Done"); $state = 'Ready' }
    if ($state -eq 'Done' -and -not $r.LastAuditFilled) { $problems.Add("$($s.name): says Done but '## Last Audit' is not filled - counted as Partial"); $state = 'Partial' }
    $states[$state]++
    if ($state -ne 'Done') { $problems.Add("open slice: $($s.name) ($state)") }

    $hasInline = $false
    foreach ($f in $r.Findings) {
        $sev = ($f.Severity -replace '[^P1-4]', '')
        if ($bySev.Contains($sev)) { $bySev[$sev]++ }
        $disp = $f.Disposition
        foreach ($m in [regex]::Matches($disp, '\b(?:ticket|follow-up)\s+(\d{3})\b')) { $spawned[$m.Groups[1].Value] = $true }
        if ($disp -match '^\s*inline') { $hasInline = $true }
        if ($sev -in 'P1', 'P2') {
            $ok = ($disp -match '^\s*ticket\s+\d{3}\b') -or ($disp -match '^\s*inline\b') -or ($disp -match '^\s*045\s+[A-Z]-\d{2,3}\b')
            if (-not $ok) { $problems.Add("$($f.Id) ($sev) in $($s.name) has neither a ticket nor an inline fix: '$disp'") }
        }
    }
    if ($hasInline) {
        $la = $r.LastAudit
        $build = $la -match 'build\.ps1[^\n]*exit\s*(code\s*)?0\b'
        $tests = $la -match 'Run-AllTests\.ps1[^\n]*exit\s*(code\s*)?0\b'
        if ($state -eq 'Done' -and -not ($build -and $tests)) {
            $problems.Add("$($s.name): inline fixes without quoted evidence - '## Last Audit' must quote build.ps1 and Run-AllTests.ps1 with exit 0")
        }
    }
    $rows.Add([pscustomobject]@{ Slice = $s.name; State = $state; Files = $s.fileCount; Lines = $s.lines; Risk = $s.risk; Findings = $r.Findings.Count })
}

# --- spawned tickets ---------------------------------------------------------------
$ticketStatus = [ordered]@{}
$queueText = if (Test-Path -LiteralPath $Queue) { [System.IO.File]::ReadAllText($Queue) } else { '' }
foreach ($t in (@($spawned.Keys) | Sort-Object)) {
    $m = [regex]::Match($queueText, "(?m)^\S+\s+${t}_\S+\s+\S+\s+(\S+)")
    if ($m.Success) { $ticketStatus[$t] = $m.Groups[1].Value }
    else {
        $done = Get-ChildItem -LiteralPath (Join-Path $SpecDir 'done') -Filter "${t}_*.md" -File -ErrorAction SilentlyContinue
        if ($done) { $ticketStatus[$t] = 'done/' } else { $ticketStatus[$t] = 'NOT QUEUED'; $problems.Add("ticket $t is cited by a slice but has no line in RELEASE_QUEUE.md") }
    }
}

# --- report --------------------------------------------------------------------
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$coverage = "class A files $($auditable.Count): uncovered $($uncovered.Count), doubled $($doubled.Count), gone from the tree $($gone.Count)"
$stateLine = "slices $(@($manifest.slices).Count): Ready $($states.Ready), Partial $($states.Partial), Done $($states.Done)"
$sevLine = "findings: P1 $($bySev.P1), P2 $($bySev.P2), P3 $($bySev.P3), P4 $($bySev.P4)"
$ticketLine = if ($ticketStatus.Count) { 'tickets spawned: ' + (($ticketStatus.Keys | ForEach-Object { "$_ ($($ticketStatus[$_]))" }) -join ', ') } else { 'tickets spawned: none' }
Write-Host "  $coverage"
Write-Host "  $stateLine"
Write-Host "  $sevLine"
Write-Host "  $ticketLine"
foreach ($g in $gone) { Write-Host "  note: $g is in a slice but no longer in the tree" -ForegroundColor Yellow }

if ($Summary) {
    $sum = @(
        "## Last Audit", '',
        "- Date: $(Get-Date -Format 'yyyy-MM-dd'); baseline ``$($manifest.baseline.commit)``; $(Get-TreeLabel $Root).",
        "- Coverage: $coverage.",
        "- $($stateLine.Substring(0,1).ToUpper())$($stateLine.Substring(1)).",
        "- $($sevLine.Substring(0,1).ToUpper())$($sevLine.Substring(1)).",
        "- $($ticketLine.Substring(0,1).ToUpper())$($ticketLine.Substring(1)).",
        "- Open problems: $($problems.Count).", ''
    ) -join "`n"
    Write-Utf8Lf (Join-Path $OutDir 'summary.md') $sum
    Write-Host "  summary: $(Join-Path $OutDir 'summary.md')"
}

$detail = "$($uncovered.Count) uncovered, $($doubled.Count) doubled, $($states.Done)/$(@($manifest.slices).Count) slices done, P1 $($bySev.P1) P2 $($bySev.P2)"
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" }
    Exit-Verdict 'Audit-Status' 1 $detail
}
Exit-Verdict 'Audit-Status' 0 $detail
