<#
.SYNOPSIS
    The scene catalog, the capability list and the budget table (specification 209
    sections 6, 8 and 9), loaded and validated. The self-test's steps (b) and (c) run
    through here: a cell in an illegal state, a probe that does not exist, a capability
    with no covering scene, a scene citing a capability that is not in the list, or an
    anchor that no longer exists in the tree - each is a self-test failure, exit 2.
#>

$script:DynAuditDir = Split-Path $PSScriptRoot -Parent
$script:DynAuditProbeNames = @('ping', 'key', 'coh', 'clip', 'lock', 'samp', 'slope', 'inv', 'err', 'uia', 'pix', 'nrt', 'start')
$script:DynAuditCellStates = @('open', 'n/a', 'measured')
$script:DynAuditProperties = @('R', 'E', 'B', 'L', 'A')

function Get-DynAuditJsonl {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "not found: $Path" }
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $rows.Add(($line | ConvertFrom-Json))
    }
    return $rows
}

function Get-DynAuditScenes {
    return Get-DynAuditJsonl (Join-Path $script:DynAuditDir 'scenes.jsonl')
}

function Get-DynAuditCapabilities {
    return Get-DynAuditJsonl (Join-Path $script:DynAuditDir 'capabilities.jsonl')
}

function Get-DynAuditBudgets {
    return Get-DynAuditJsonl (Join-Path $script:DynAuditDir 'budgets.jsonl')
}

# Validates the whole data set; returns a list of problems (empty = valid). $RepoRoot is
# needed for the anchor existence check.
function Test-DynAuditData {
    param([string]$RepoRoot)
    $problems = [System.Collections.Generic.List[string]]::new()
    $scenes = @(Get-DynAuditScenes)
    $caps = @(Get-DynAuditCapabilities)
    $budgets = @(Get-DynAuditBudgets)
    $sceneIds = @{}
    $capIds = @{}
    $budgetIds = @{}

    if ($scenes.Count -ne 50) { $problems.Add("the catalog holds $($scenes.Count) scenes, not 50") }
    foreach ($b in $budgets) { $budgetIds[$b.id] = $b }
    foreach ($c in $caps) { $capIds[[int]$c.id] = $c }

    # Capabilities: anchors exist, covering scenes exist, every capability is covered.
    foreach ($c in $caps) {
        foreach ($anchor in @($c.anchors)) {
            $p = Join-Path $RepoRoot $anchor.path
            if (-not (Test-Path -LiteralPath $p)) {
                $problems.Add("capability $($c.id) '$($c.capability)': anchor $($anchor.name) ($($anchor.path)) no longer exists - re-derive the list (section 6)")
            }
        }
        $hasNaReason = ($c.naReason -and $c.naReason.Trim())
        if (-not $c.scenes -and -not $hasNaReason) {
            $problems.Add("capability $($c.id) '$($c.capability)' has no covering scene cell that is not n/a")
        }
        foreach ($sid in @($c.scenes)) {
            if ($sceneIds.ContainsKey($sid)) { continue }
            $scene = @($scenes | Where-Object { $_.id -eq $sid })
            if ($scene.Count -eq 0) { $problems.Add("capability $($c.id) cites scene $sid, which is not in the catalog") }
            elseif (-not $hasNaReason -and $scene[0].capabilities -notcontains [int]$c.id) {
                $problems.Add("capability $($c.id) covers scene $sid, but the scene does not cite it back")
            }
        }
    }
    foreach ($id in 1..18) {
        if (-not $capIds.ContainsKey($id)) { $problems.Add("capability $id is missing from capabilities.jsonl") }
    }

    # Scenes: shape, cells, probes, budgets, capability citations.
    $open = 0; $na = 0; $measured = 0
    foreach ($s in $scenes) {
        if ($s.id -notmatch '^S[1-8]\.[1-9][0-9]?$') { $problems.Add("scene id '$($s.id)' is not S<group>.<n>") }
        if ($sceneIds.ContainsKey($s.id)) { $problems.Add("scene $($s.id) appears twice") }
        $sceneIds[$s.id] = $true

        foreach ($prop in $script:DynAuditProperties) {
            $cell = $s.cells.$prop
            if (-not $cell) { $problems.Add("scene $($s.id): cell $prop is missing"); continue }
            if ($cell.state -notin $script:DynAuditCellStates) {
                $problems.Add("scene $($s.id) cell $($prop): state '$($cell.state)' is not one of measured, n/a, open")
            }
            if ($cell.state -eq 'n/a' -and -not ($cell.reason -and $cell.reason.Trim())) {
                $problems.Add("scene $($s.id) cell $prop is n/a with no written reason")
            }
            if ($cell.state -eq 'n/a' -and @(@($cell.probes) | Where-Object { $_ }).Count -gt 0) {
                $problems.Add("scene $($s.id) cell $($prop) is n/a but names probes - the campaign will not try it")
            }
            if ($cell.state -eq 'open') { $open++ }
            elseif ($cell.state -eq 'n/a') { $na++ }
            else { $measured++ }
            foreach ($probe in @(@($cell.probes) | Where-Object { $_ })) {
                if ($script:DynAuditProbeNames -notcontains $probe) {
                    $problems.Add("scene $($s.id) cell $prop names probe '$probe', which does not exist")
                }
            }
        }
        foreach ($bid in @($s.budgets)) {
            if (-not $budgetIds.ContainsKey($bid)) { $problems.Add("scene $($s.id) cites budget $bid, which is not in budgets.jsonl") }
        }
        foreach ($cid in @($s.capabilities)) {
            if (-not $capIds.ContainsKey([int]$cid)) { $problems.Add("scene $($s.id) cites capability $cid, which is not in the list") }
        }
        if (-not $s.phase) { $problems.Add("scene $($s.id) carries no phase (D-0..D-7)") }
        if (-not $s.name) { $problems.Add("scene $($s.id) carries no name") }
    }

    # The matrix totals the specification states at Draft: 250 cells, 214 open, 36 n/a,
    # 0 measured (section 9). A scene that has run rewrites its own copy of the matrix
    # in the campaign report, never this committed one.
    $totalCells = $open + $na + $measured
    if ($totalCells -ne 250) { $problems.Add("the matrix holds $totalCells cells, not 250") }
    if ($measured -ne 0) { $problems.Add("the committed matrix holds $measured measured cells - at Draft every cell is open or n/a") }

    return @{
        Problems  = $problems.ToArray()
        Scenes    = $scenes
        Capabilities = $caps
        Budgets   = $budgets
        Totals    = @{ Open = $open; NA = $na; Measured = $measured; Cells = $totalCells }
    }
}

# A scene's effective cell set for a run: the open cells (n/a cells are skipped and
# counted on the verdict line; measured cells are re-measured - the campaign may re-run
# any scene; the committed matrix stays open until a campaign rewrites the report copy).
function Get-DynAuditSceneCells {
    param($Scene)
    $cells = @()
    $skippedNa = 0
    foreach ($prop in $script:DynAuditProperties) {
        $cell = $Scene.cells.$prop
        if ($cell.state -eq 'n/a') { $skippedNa++; continue }
        $cells += [pscustomobject]@{ Property = $prop; Probes = @($cell.probes); Note = $cell.note }
    }
    return @{ Cells = $cells; NaCount = $skippedNa }
}

# Requirements a scene names, resolved against the machine: returns the unmet ones.
function Resolve-DynAuditRequirements {
    param($Scene, [string]$ExePath, [string]$RepoRoot, [string]$SftpFake, [string]$RuntimeTree, [string[]]$ConfirmSwitches)
    $unmet = [System.Collections.Generic.List[string]]::new()
    foreach ($req in @($Scene.requirements)) {
        if (-not $req) { continue }
        switch -Regex ($req) {
            '^viewer-exe$' {
                if (-not (Test-Path -LiteralPath $ExePath)) { $unmet.Add('the mainline viewer exe was not found (build it or pass -ExePath)') }
            }
            '^companion$' {
                $companion = Join-Path (Split-Path $ExePath -Parent) 'FastMediaSorterCompanion.exe'
                if (-not (Test-Path -LiteralPath $companion)) { $unmet.Add('FastMediaSorterCompanion.exe was not found beside the viewer') }
            }
            '^ffmpeg$' {
                if (-not (Get-DynAuditFfmpeg)) { $unmet.Add('ffmpeg was not found on PATH') }
            }
            '^clean-profile$' {
                if (Test-Path -LiteralPath 'HKCU:\SZA\FastMediaSorter') { $unmet.Add('the settings hive already exists - the scene needs the one-time clean profile (Q209-2)') }
            }
            '^quiet-session$' {
                # SendInput is session-global (section 12 item 4): the switch is the operator's
                # declaration that the isolated account has nothing else open.
                if ($ConfirmSwitches -notcontains 'quiet-session') { $unmet.Add("requires -Confirm quiet-session (SendInput is session-global)") }
            }
            '^sftp-loopback$' {
                if (-not $SftpFake) { $unmet.Add('no loopback SFTP stand was supplied (-SftpFake); the real devserve belongs to the worker repository') }
            }
            '^runtime-tree$' {
                if (-not $RuntimeTree) { $unmet.Add('no disposable runtime tree was supplied (-RuntimeTree) for the S7.7 rename') }
            }
            '^dev-gate-broadcast$' {
                if ($ConfirmSwitches -notcontains 'dev-gate-broadcast') { $unmet.Add('requires -Confirm dev-gate-broadcast (the live-broadcast gate is shut outside the dev environment, Q209-3)') }
            }
            '^quota-volume$' { $unmet.Add('needs a small-quota volume prepared by hand (see the runbook README)') }
            '^sleep-metal$' { $unmet.Add('needs real metal that can sleep (Q209-8); a VM cannot') }
            '^rdp-session$' { $unmet.Add('needs an RDP console-takeover session (Q209-8)') }
            '^fast-user-switch$' { $unmet.Add('needs fast user switching on the metal console (Q209-8)') }
            '^battery$' { $unmet.Add('needs a machine on battery with energy saver on') }
            '^disk-([0-9]+)gb-free$' {
                $need = [int]$Matches[1]
                $drive = Get-PSDrive -Name ($RepoRoot.Substring(0, 1)) -ErrorAction SilentlyContinue
                if (-not $drive -or ($drive.Free / 1GB) -lt $need) { $unmet.Add("needs $need GB free on the corpus drive") }
            }
            '^confirm:(.+)$' {
                if ($ConfirmSwitches -notcontains $Matches[1]) { $unmet.Add("requires -Confirm $($Matches[1]) - the action is disruptive by design") }
            }
            default { $unmet.Add("unknown requirement '$req'") }
        }
    }
    return $unmet.ToArray()
}
