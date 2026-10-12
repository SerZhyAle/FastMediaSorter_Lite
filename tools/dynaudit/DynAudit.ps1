<#
.SYNOPSIS
    The DynAudit campaign runner (specification 209, section 4): aggregates the scenes into
    `DynAudit: PASS/FAILED/COULD NOT VERIFY (n scenes, m skipped)` with the CHECK-VERDICT
    codes (0/1/2). The suite joins tools\check-placement.jsonl only as the opt-in hand-run
    switch -DynAudit of tools\Run-AllTests.ps1 (Q209-5) - it drives a real interactive
    session, so it is never part of a default run.

.DESCRIPTION
    DynAudit.ps1                        the default campaign: self-test gate, control run,
                                        then every scene whose requirements are met and
                                        whose duration fits -MaxSceneMinutes
    DynAudit.ps1 -Scene S2.1,S4.1       only the named scenes
    DynAudit.ps1 -Phase D-1             the scenes of one phase (D-1..D-5)
    DynAudit.ps1 -All                   every scene, the 2 h and 8 h ones included
    DynAudit.ps1 -Control               write control.json: the probes' own jitter and the
                                        machine's floor, measured against the fixture
                                        window without the product (section 7)

    The order is the law of section 11: no scene runs before a passing self-test (D-0),
    and no scene is judged without a control row. Two consecutive exit-2 runs park a
    scene as open instead of thrashing the machine (Q209-10). Every scene's artifacts and
    the final matrix live under the run folder; the restore verdict travels with the
    campaign report (section 12 item 7).

.PARAMETER Confirm
    Passed through to Invoke-DynScene.ps1: quiet-session, sleep, lock, dev-gate-broadcast.
    Without -Confirm quiet-session every scene that injects input refuses to run - exit 2,
    never a silent pass - because SendInput is session-global.

.EXAMPLE
    pwsh -NoProfile -File tools\dynaudit\DynAudit.ps1 -Control -RunFolder C:\dynrun
    pwsh -NoProfile -File tools\dynaudit\DynAudit.ps1 -Phase D-1 -RunFolder C:\dynrun -Confirm quiet-session
#>
param(
    [string[]]$Scenes = @(),
    [ValidateSet('', 'D-1', 'D-2', 'D-3', 'D-4', 'D-5')][string]$Phase = '',
    [switch]$All,
    [switch]$Control,
    [string]$RunFolder = '',
    [string]$ExePath = '',
    [string[]]$Confirm = @(),
    [string]$SftpFake = '',
    [string]$RuntimeTree = '',
    [int]$ControlSeconds = 60,
    [double]$MaxSceneMinutes = 15
)

$ErrorActionPreference = 'Stop'
$dynauditDir = $PSScriptRoot
$repoRoot = Split-Path (Split-Path $dynauditDir -Parent) -Parent
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Probes.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Slope.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Profile.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Data.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'DynAudit' 2 'the campaign runner itself failed'
}

# --- the control run (section 7): the probes' own jitter, the machine's floor -----------

function Invoke-DynAuditControl {
    param([string]$Folder, [int]$Seconds)
    Write-Subject 'DynAudit' "the control run: the probes against the fixture window, the product never launched; $(Get-TreeLabel $repoRoot)"
    if (-not $Folder) { $Folder = Join-Path $env:TEMP ("fms-dynaudit-control-{0}" -f (Get-Date -Format 'yyMMdd-HHmmss')) }
    New-Item -ItemType Directory -Path $Folder -Force | Out-Null

    $workload = Join-Path $Folder 'workload-20mb.bin'
    $buf = [byte[]]::new(1MB); (New-Object Random 11).NextBytes($buf)
    $fs = [IO.File]::Create($workload)
    try { for ($i = 0; $i -lt 20; $i++) { $fs.Write($buf, 0, $buf.Length) } } finally { $fs.Dispose() }
    # Pre-warm: the first cold-cache pass would measure the disk, not the cohost's jitter.
    $warmFs = [IO.File]::OpenRead($workload); try { $null = $warmFs.Read($buf, 0, $buf.Length) } finally { $warmFs.Dispose() }

    $fixture = Start-DynAuditFixture -RunFolder $Folder
    if (-not $fixture) { Exit-Verdict 'DynAudit' 2 'the fixture window never came up - the control run cannot be recorded' }
    try {
        $hwnd = $fixture.Hwnd
        Write-Host '  sampling ping...'
        $ping = Invoke-PingProbe -Hwnd $hwnd -Seconds $Seconds -Hz 10 -CsvPath (Join-Path $Folder 'control-ping.csv')
        $key = Invoke-KeyProbe -Hwnd $hwnd -Count 20 -CsvPath (Join-Path $Folder 'control-key.csv') -EffectMode caption
        Write-Host '  sampling the cohost (twice)...'
        $coh1 = Invoke-CohProbe -Seconds ([Math]::Max(10, $Seconds / 2)) -WorkloadFile $workload -OutJsonPath (Join-Path $Folder 'control-coh1.json') -RunFolder $Folder
        $coh2 = Invoke-CohProbe -Seconds ([Math]::Max(10, $Seconds / 2)) -WorkloadFile $workload -OutJsonPath (Join-Path $Folder 'control-coh2.json') -RunFolder $Folder
        Write-Host '  sampling the clipboard...'
        $clip = Invoke-ClipProbe -Seconds ([Math]::Max(10, $Seconds / 2)) -Hz 5 -CsvPath (Join-Path $Folder 'control-clip.csv')
        Write-Host '  sampling the idle series (twice)...'
        $samp1 = Join-Path $Folder 'control-samp1.csv'
        $samp2 = Join-Path $Folder 'control-samp2.csv'
        Invoke-SampProbe -ProcId $fixture.FixturePid -Seconds ([Math]::Max(15, $Seconds / 2)) -CsvPath $samp1
        Invoke-SampProbe -ProcId $fixture.FixturePid -Seconds ([Math]::Max(15, $Seconds / 2)) -CsvPath $samp2

        $ws1 = Get-SampEndpoints $samp1 'workingSet'; $ws2 = Get-SampEndpoints $samp2 'workingSet'
        $sampSpread = 100.0
        if ($ws1 -and $ws2 -and $ws1.Last -gt 0) {
            $sampSpread = [Math]::Round(100.0 * [Math]::Abs($ws1.Last - $ws2.Last) / [Math]::Max(1.0, (($ws1.Last + $ws2.Last) / 2)), 2)
        }
        $cohSpread = 100.0
        if ($coh1 -and $coh2 -and $coh1.passMsP50 -gt 0) {
            $cohSpread = [Math]::Round(100.0 * [Math]::Abs($coh1.passMsP50 - $coh2.passMsP50) / [Math]::Max(1.0, (($coh1.passMsP50 + $coh2.passMsP50) / 2)), 1)
        }

        $ceilings = [System.Collections.Generic.List[string]]::new()
        if ($ping.P95Ms -gt 15) { $ceilings.Add("ping p95 $($ping.P95Ms) ms > 15 ms") }
        if ($key.P95Ms -gt 60) { $ceilings.Add("key p95 $($key.P95Ms) ms > 60 ms") }
        if ($clip.Failures -ne 0) { $ceilings.Add("$($clip.Failures) clipboard open failures (ceiling 0)") }
        if ($sampSpread -gt 1) { $ceilings.Add("idle samp series differ $sampSpread% (ceiling 1%)") }
        if ($cohSpread -gt 10) { $ceilings.Add("coh workload spread $cohSpread% (ceiling 10%)") }

        $control = [pscustomobject]@{
            Valid            = ($ceilings.Count -eq 0)
            Ceilings         = $ceilings.ToArray()
            PingP95Ms        = $ping.P95Ms
            PingP99Ms        = $ping.P99Ms
            KeyP95Ms         = $key.P95Ms
            ClipFailures     = $clip.Failures
            CohSelfPingP95Ms = [Math]::Round((($coh1.selfPingP95Ms + $coh2.selfPingP95Ms) / 2), 2)
            CohPassMsP50     = [Math]::Round((($coh1.passMsP50 + $coh2.passMsP50) / 2), 2)
            SampIdleSpreadPct = $sampSpread
            Seconds          = $Seconds
            CreatedAtUtc     = (Get-Date).ToUniversalTime().ToString('o')
        }
        $path = Join-Path $Folder 'control.json'
        $control | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8
        if ($ceilings.Count -gt 0) {
            foreach ($c in $ceilings) { Write-Host "  ceiling exceeded: $c" -ForegroundColor Red }
            Exit-Verdict 'DynAudit' 2 ('the control ceilings were exceeded - rerun on a quieter machine; ' + ($ceilings -join '; '))
        }
        Exit-Verdict 'DynAudit' 0 ("the control run is valid; control.json written to $path (ping p95 $($ping.P95Ms) ms, key p95 $($key.P95Ms) ms, 0 clipboard failures, coh spread $cohSpread%, idle samp spread $sampSpread%)")
    } finally {
        Stop-DynAuditFixture $fixture
    }
}

# --- the campaign -----------------------------------------------------------------------

if ($Control) { Invoke-DynAuditControl -Folder $RunFolder -Seconds $ControlSeconds }

$allScenes = @(Get-DynAuditScenes)
$selection = @()
if ($Scenes.Count -gt 0) { $selection = @($allScenes | Where-Object { $Scenes -contains $_.id }) }
elseif ($Phase) { $selection = @($allScenes | Where-Object { $_.phase -eq $Phase }) }
else { $selection = $allScenes }

if ($selection.Count -eq 0) { Exit-Verdict 'DynAudit' 2 'no scene in the catalog matches the requested selection' }

if (-not $RunFolder) { $RunFolder = Join-Path $env:TEMP ("fms-dynaudit-{0}" -f (Get-Date -Format 'yyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $RunFolder -Force | Out-Null
$controlFile = Join-Path $RunFolder 'control.json'
$statePath = Join-Path $RunFolder 'campaign-state.json'
$driver = Join-Path $dynauditDir 'Invoke-DynScene.ps1'
$pwshPath = (Get-Process -Id $PID).Path

Write-Subject 'DynAudit' "$($selection.Count) scene(s) of the dynamic audit; run folder: <USER>\Temp\$(Split-Path $RunFolder -Leaf); $(Get-TreeLabel $repoRoot)"

# --- gate 1: the self-test (D-0) - no scene run before a passing self-test counts ------
$selfOut = & $pwshPath -NoProfile -File $driver -SelfTest 2>&1
$selfCode = $LASTEXITCODE
@($selfOut) | ForEach-Object { Write-Host "$_" }
if ($selfCode -ne 0) {
    Exit-Verdict 'DynAudit' 2 'the self-test did not pass - no scene counts (D-0: validity first)'
}

# --- gate 2: the control run ------------------------------------------------------------
if (-not (Test-Path -LiteralPath $controlFile)) {
    Write-Host '  no control.json in the run folder - recording the control run first (section 7)'
    $controlOut = & $pwshPath -NoProfile -File ($PSCommandPath) -Control -RunFolder $RunFolder -ControlSeconds $ControlSeconds 2>&1
    $controlCode = $LASTEXITCODE
    @($controlOut) | ForEach-Object { Write-Host "$_" }
    if ($controlCode -ne 0) {
        Exit-Verdict 'DynAudit' 2 'the control run failed or exceeded its ceilings - no scene can be judged; rerun on a quieter machine'
    }
} else {
    $existing = Get-Content -LiteralPath $controlFile -Raw | ConvertFrom-Json
    if (-not $existing.valid) { Exit-Verdict 'DynAudit' 2 'control.json is marked invalid - rerun the control on a quieter machine' }
    Write-Host "  control.json present (ping p95 $($existing.pingP95Ms) ms)"
}

# --- the state of consecutive invalids (the parking rule of Q209-10) --------------------
# A plain hashtable over a PSCustomObject view of the json, so the runner works under
# Windows PowerShell 5.1 and pwsh alike (-AsHashtable is pwsh-only).
$state = @{}
if (Test-Path -LiteralPath $statePath) {
    $loaded = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    foreach ($p in @($loaded.PSObject.Properties)) {
        $state[$p.Name] = @{ consecutiveInvalid = [int]$p.Value.consecutiveInvalid }
    }
}

$results = [System.Collections.Generic.List[object]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
$matrix = [System.Collections.Generic.List[object]]::new()

foreach ($s in $selection) {
    # Parked scenes stay open: two consecutive exit-2 runs park the scene instead of
    # thrashing the machine (Q209-10).
    $parked = 0
    if ($state.ContainsKey($s.id)) { $parked = [int]$state[$s.id].consecutiveInvalid }
    if ($parked -ge 2) {
        $skipped.Add("$($s.id) (parked open: 2 consecutive invalid runs)")
        $results.Add([pscustomobject]@{ Scene = $s.id; Code = 2; Verdict = "DynAudit $($s.id): parked open (2 consecutive invalid runs)"; Skipped = $true })
        continue
    }
    # Long scenes are switched off by -MaxSceneMinutes unless -All: a skipped scene is
    # named on the verdict line, never counted as passed.
    if (-not $All -and ([double]$s.durationSec / 60.0) -gt $MaxSceneMinutes) {
        $skipped.Add("$($s.id) (long scene: $([Math]::Round($s.durationSec / 60.0, 0)) min > $MaxSceneMinutes; run with -All or by hand)")
        $results.Add([pscustomobject]@{ Scene = $s.id; Code = $null; Verdict = "DynAudit $($s.id): SKIPPED (long scene)"; Skipped = $true })
        continue
    }

    Write-Host "`n=== scene $($s.id): $($s.name) ===" -ForegroundColor Cyan
    $args = @('-NoProfile', '-File', $driver, '-Scene', $s.id, '-RunFolder', $RunFolder, '-ControlFile', $controlFile)
    if ($ExePath) { $args += @('-ExePath', $ExePath) }
    if ($Confirm.Count -gt 0) { $args += (@('-Confirm') + $Confirm) }
    if ($SftpFake) { $args += @('-SftpFake', $SftpFake) }
    if ($RuntimeTree) { $args += @('-RuntimeTree', $RuntimeTree) }
    $out = & $pwshPath @args 2>&1
    $code = $LASTEXITCODE
    @($out) | ForEach-Object { Write-Host "$_" }
    $line = @($out | Where-Object { "$_" -match "^DynAudit $($s.id): " }) | Select-Object -Last 1
    if (-not $line) { $line = "DynAudit $($s.id): (no verdict line; exit $code)" }

    # the parking state: only a rerun on the same run folder carries the count forward
    if (-not $state.ContainsKey($s.id)) { $state[$s.id] = @{ consecutiveInvalid = 0 } }
    if ($code -eq 2) { $state[$s.id].consecutiveInvalid = [int]$state[$s.id].consecutiveInvalid + 1 }
    else { $state[$s.id].consecutiveInvalid = 0 }
    $stateView = [pscustomobject]::new()
    foreach ($k in @($state.Keys)) { $stateView | Add-Member -MemberType NoteProperty -Name $k -Value ([pscustomobject]$state[$k]) }
    $stateView | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8

    $results.Add([pscustomobject]@{ Scene = $s.id; Code = $code; Verdict = "$line"; Skipped = $false })
}

# --- the matrix reprint with the final states (section 9) --------------------------------
foreach ($s in $allScenes) {
    $r = @($results | Where-Object { $_.Scene -eq $s.id })
    $run = if ($r.Count -gt 0 -and -not $r[0].Skipped) { $r[0] } else { $null }
    $cells = [ordered]@{}
    foreach ($prop in @('R', 'E', 'B', 'L', 'A')) {
        $cell = $s.cells.$prop
        if ($cell.state -eq 'n/a') { $cells[$prop] = 'n/a' }
        elseif ($run -and $run.Code -in 0, 1) { $cells[$prop] = 'measured' }
        else { $cells[$prop] = 'open' }
    }
    $matrix.Add([pscustomobject]@{
        Id = $s.id; Phase = $s.phase; Name = $s.name
        State = if (-not $run) { 'skipped' } elseif ($run.Code -eq 0) { 'pass' } elseif ($run.Code -eq 1) { 'fail' } else { 'could-not-verify' }
        Cells = $cells
    })
}
$matrixPath = Join-Path $RunFolder 'matrix-final.json'
$matrix | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $matrixPath -Encoding UTF8

$summaryPath = Join-Path $RunFolder 'campaign-summary.csv'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('scene,verdict,exit')
foreach ($r in $results) { $lines.Add(("$($r.Scene),$($r.Verdict -replace ',',';'),$($r.Code)"))}
[IO.File]::WriteAllText($summaryPath, ($lines -join "`r`n"))

# --- the aggregate ------------------------------------------------------------------------
$ran = @($results | Where-Object { -not $_.Skipped })
$passed = @($ran | Where-Object { $_.Code -eq 0 })
$failed = @($ran | Where-Object { $_.Code -eq 1 })
$notVerified = @($ran | Where-Object { $_.Code -eq 2 })
$codes = [int[]]($ran | ForEach-Object { $_.Code })
$code = Get-AggregateCode $codes

$parts = @("$($passed.Count) scene(s) passed")
if ($failed.Count) { $parts += "failed: " + (($failed | ForEach-Object Scene) -join ', ') }
if ($notVerified.Count) { $parts += "could not verify: " + (($notVerified | ForEach-Object Scene) -join ', ') }
if ($skipped.Count) { $parts += "skipped: " + ($skipped -join ', ') }
$parts += "matrix: $matrixPath"

Exit-Verdict 'DynAudit' $code (($parts -join '; '))
