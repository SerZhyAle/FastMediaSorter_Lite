<#
.SYNOPSIS
    The gates of the device-exchange and live-broadcast program against their evidence, in the form the
    release preflight reads (ticket 204 section 3 item 3; the table is 206 section 2).

.DESCRIPTION
    A release never opens a gate by itself. The three gates of 206 section 2:

      * INCLUDE_EXCHANGE (the exchange server binary in a package) - .github\workflows\release.yml;
      * ExchangeFeatures.Offered (the exchange client features) - src\Exchange\ExchangeFeatures.vb;
      * LiveBroadcastFeatures.Offered (the PC as a live-broadcast producer) - src\LiveBroadcast\LiveBroadcastFeatures.vb.

    and every other "Const Offered As Boolean" in src (today the Share Manager's broadcasts page,
    159 section 3.2, held by BroadcastWorkerCapabilityTests) are read here, and the rule is one sentence:
    a gate that is OPEN must name, in a committed evidence file, the rung (the live-broadcast gate: the
    rung or the phase of 172) and the date it was opened at; a gate that is shut, and any switch that is
    on without a gate of the table behind it, is a defect.

    The evidence files are docs\specifications\exchange-gate-evidence.md and
    docs\specifications\live-broadcast-gate-evidence.md - the same files the suite half of the guard
    requires (ExchangeClientGuardTests, LiveBroadcastGateGuardTests), so a gate cannot be open for the
    tag and shut for the suite. This check READS only: it opens no gate, writes nothing, needs no
    network, and takes about a second.

.OUTPUTS
    CHECK-VERDICT: 0 PASS (every gate is shut, or open with its evidence), 1 FAIL (a gate is open
    without the evidence naming the rung and the date, or an offer switch is on that no gate names),
    2 COULD NOT VERIFY (the workflow file or a gate's source cannot be read).
    The last line is `Test-FeatureGates: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

$check = 'Test-FeatureGates'
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
Write-Subject $check "the exchange and live-broadcast gates against their evidence; $(Get-TreeLabel $root)"

# The gates of the table, with the evidence file each one needs when it is open. A switch that is on
# but not in this table is a defect on its own: no evidence convention names it.
$gates = @(
    @{ Name = 'INCLUDE_EXCHANGE';            Evidence = 'exchange-gate-evidence.md' },
    @{ Name = 'ExchangeFeatures.Offered';    Evidence = 'exchange-gate-evidence.md' },
    @{ Name = 'LiveBroadcastFeatures.Offered'; Evidence = 'live-broadcast-gate-evidence.md' }
)

# --- the packaging gate: INCLUDE_EXCHANGE in the release workflow -------------------------------
$workflow = Join-Path $root '.github\workflows\release.yml'
if (-not (Test-Path -LiteralPath $workflow)) {
    Exit-Verdict $check 2 '.github\workflows\release.yml not found - the packaging gate cannot be read'
}
$workflowText = Get-Content -LiteralPath $workflow -Raw
$includeMatches = [regex]::Matches($workflowText, "(?m)^\s*INCLUDE_EXCHANGE\s*:\s*'?(true|false)'?\s*$")
if ($includeMatches.Count -ne 1) {
    Exit-Verdict $check 1 "release.yml must define INCLUDE_EXCHANGE once (found $($includeMatches.Count) line(s))"
}
$open = @{}
$open['INCLUDE_EXCHANGE'] = ($includeMatches[0].Groups[1].Value -eq 'true')

# --- the offer constants in the sources ----------------------------------------------------------
# Every compiled-in "Offered" switch, the same scan the suite half uses (DualModeGateGuardTests). The
# net48 viewer project lists none of these files and every one of them is whole-file modern-only.
$known = @{}
$known['src\Exchange\ExchangeFeatures.vb'] = 'ExchangeFeatures.Offered'
$known['src\LiveBroadcast\LiveBroadcastFeatures.vb'] = 'LiveBroadcastFeatures.Offered'
$scanned = 0
$switches = @{}
foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter *.vb -File -Recurse)) {
    if ($f.FullName -match '\\(obj|bin)\\') { continue }
    $scanned++
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $f.FullName -Raw),
                                    '(?im)^\s*(Public|Friend)\s+Const\s+Offered\s+As\s+Boolean\s*=\s*(True|False)')) {
        $rel = $f.FullName.Substring($root.Length + 1)
        $switches[$rel] = ($m.Groups[2].Value -eq 'True')
    }
}
if ($scanned -eq 0) {
    Exit-Verdict $check 2 'no source file was read from src\ - the scan cannot see the gates'
}
foreach ($rel in $known.Keys) {
    if (-not $switches.ContainsKey($rel)) {
        Exit-Verdict $check 2 "$rel does not define 'Const Offered As Boolean' - the gate's source is missing or renamed"
    }
    $open[$known[$rel]] = $switches[$rel]
}

# --- the rule: a gate that is open must name the rung and the date it was opened at --------------
$problems = [System.Collections.Generic.List[string]]::new()
$shut = [System.Collections.Generic.List[string]]::new()
$opened = [System.Collections.Generic.List[string]]::new()
foreach ($g in $gates) {
    if (-not $open[$g.Name]) { $shut.Add($g.Name); continue }
    $evidencePath = Join-Path (Join-Path $root 'docs\specifications') $g.Evidence
    if (-not (Test-Path -LiteralPath $evidencePath)) {
        $problems.Add("$($g.Name) is open but docs\specifications\$($g.Evidence) does not exist - opening a gate needs the owner's word and an evidence file naming the rung and the date")
        continue
    }
    $text = Get-Content -LiteralPath $evidencePath -Raw
    if ($g.Name -eq 'LiveBroadcastFeatures.Offered') {
        if (-not ($text -match '(?i)(rung\s*[2-5]|phase\s*B[0-3])')) { $problems.Add("$($g.Name) is open but its evidence file names no rung or phase (2-5 / B0-B3)") }
    } elseif (-not ($text -match '(?i)rung\s*[2-5]')) {
        $problems.Add("$($g.Name) is open but its evidence file names no rung (2-5)")
    }
    if (-not ($text -match '20\d\d-\d\d-\d\d')) { $problems.Add("$($g.Name) is open but its evidence file names no date") }
    if ($problems.Count -eq 0) { $opened.Add($g.Name) }
}
foreach ($rel in $switches.Keys) {
    if ($switches[$rel] -and -not $known.ContainsKey($rel)) {
        $problems.Add("$rel holds an offer switch that is ON but no gate of the table (206 section 2) names it")
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($problems.Count) gate problem(s); $($shut.Count) shut, $($opened.Count) open with evidence, $scanned source files scanned"
}
$state = if ($opened.Count -gt 0) { "open with evidence: $($opened -join ', ')" } else { "shut" }
Exit-Verdict $check 0 "$($gates.Count) gates $state; $($shut.Count) shut; every other offer switch off; $scanned source files scanned"
