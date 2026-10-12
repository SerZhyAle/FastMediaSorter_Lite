<#
.SYNOPSIS
    The DynAudit scene driver (specification 209, section 4): drives ONE scene of the
    dynamic-audit catalog and ends in one CHECK-VERDICT line with the contract's codes:
      exit 0 = PASS        - every budget held (measured-minus-control), every wrap
                             criterion met, the restore verified
      exit 1 = FAIL        - a budget broken or a wrap criterion failed, or the harness's
                             own restore failed (a P0 incident, never silent). Until the
                             owner accepts the budget table at D-2 the wording is "budget
                             hypothesis exceeded [H]", never yet a defect (Q209-4)
      exit 2 = COULD NOT VERIFY - an invalid run per section 7 (control ceilings, dropped
                             samples, a machine that got busy), a missing exe or dependency,
                             no backup, an unmet requirement. Never a pass, never a defect.

.DESCRIPTION
    Invoke-DynScene.ps1 -Scene S4.1            one scene (reads scenes.jsonl)
    Invoke-DynScene.ps1 -SelfTest             the D-0 gate: every probe verified against
                                              the harness's own fixture window, the
                                              capability cross-check of section 6, the
                                              matrix validation of section 9 and the
                                              verdict contract. No scene run before a
                                              passing self-test counts.

    The scene verdict line is `DynAudit S4.1: PASS (5 budgets, 0 n/a; restore verified)` -
    the campaign runner (DynAudit.ps1) aggregates these into `DynAudit: PASS/FAILED/COULD
    NOT VERIFY (n scenes, m skipped)` with the same codes.

    Step 0 of every scene is the profile backup (section 5): reg export of
    HKCU\SZA\FastMediaSorter, a copy of %LOCALAPPDATA%\SZA\FastMediaSorter, the %TEMP%
    baseline, SHA-256 of everything, recovery-manifest.json. No backup, no run - exit 2
    before the product exe is ever launched. The restore runs in finally and its verdict
    rides the scene's verdict line; a failed restore is a P0 incident, never silent.

    Every artifact is scrubbed on write (section 12): profile paths become <USER>, the
    machine <HOST>, anything shaped like a credential <hidden>. The run folder is the one
    place the harness writes: %TEMP%\fms-dynaudit-<stamp>\.

.PARAMETER Scene
    The scene id from scenes.jsonl (S1.1 .. S8.4).

.PARAMETER SelfTest
    The validity gate of section 4. Runs against the harness's own fixture window and the
    committed data files; never touches the product or the profile.

.PARAMETER ExePath
    The mainline viewer. Default: bin\Release\FastMediaSorter_LITE.exe of the repository
    the harness lives in. A missing exe is exit 2, never a pass.

.PARAMETER ControlFile
    The campaign's control run (control.json, written by DynAudit.ps1 -Control). A scene
    without a paired control sample is exit 2: a number without a control is worse than
    no number (section 7).

.PARAMETER Confirm
    The operator's confirmations for scenes whose actions are session-wide or disruptive
    by design: quiet-session (nothing else open in the isolated session, because SendInput
    is session-global), sleep, lock, dev-gate-broadcast.

.PARAMETER SftpFake / RuntimeTree
    The loopback SFTP stand for the resource scenes, and the disposable runtime tree for
    the S7.7 rename. See the runbook (README.md).

.EXAMPLE
    pwsh -NoProfile -File tools\dynaudit\Invoke-DynScene.ps1 -SelfTest
    pwsh -NoProfile -File tools\dynaudit\Invoke-DynScene.ps1 -Scene S2.1 -ControlFile <run>\control.json -Confirm quiet-session
#>
param(
    [string]$Scene = '',
    [switch]$SelfTest,
    [string]$RunFolder = '',
    [string]$ExePath = '',
    [string]$ControlFile = '',
    [string[]]$Confirm = @(),
    [string]$SftpFake = '',
    [string]$RuntimeTree = '',
    [int]$SecondsOverride = 0
)

$ErrorActionPreference = 'Stop'
$dynauditDir = $PSScriptRoot
$repoRoot = Split-Path (Split-Path $dynauditDir -Parent) -Parent
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Probes.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Slope.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Profile.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Corpus.ps1')
. (Join-Path $dynauditDir 'lib\DynAudit.Data.ps1')

# Last resort: an unexpected terminating error must not leave the profile unrestored -
# the trap attempts the restore before it reports, and a failed restore is named (P0).
$script:ActiveRunFolder = $null
$script:HarnessLockStream = $null
trap {
    $msg = $_.Exception.Message
    if ($script:ActiveRunFolder -and (Test-DynAuditRestoreManifest $script:ActiveRunFolder)) {
        try {
            $r = Restore-DynAuditProfile $script:ActiveRunFolder
            if (-not $r.Ok) { $msg += " ; RESTORE FAILED (P0): $($r.Details -join '; ')" }
            else { $msg += ' ; restore verified' }
        } catch { $msg += " ; RESTORE FAILED (P0): $($_.Exception.Message)" }
    }
    $name = if ($Scene -and -not $SelfTest) { "DynAudit $Scene" } else { 'Invoke-DynScene' }
    Write-Host "  $msg"
    Exit-Verdict $name 2 'the scene driver itself failed'
}

function New-DynAuditRunFolder {
    param([string]$Base)
    return Join-Path $Base ("fms-dynaudit-{0}" -f (Get-Date -Format 'yyMMdd-HHmmss'))
}

# --- helpers -------------------------------------------------------------------------

function Expand-DynAuditKey {
    param([string]$Name)
    $map = @{
        'Right' = 0x27; 'Left' = 0x25; 'Up' = 0x26; 'Down' = 0x28
        'Space' = 0x20; 'Prior' = 0x21; 'Next' = 0x22; 'Home' = 0x24; 'End' = 0x23
        'F5' = 0x74
        'NumPadAdd' = 0x6B; 'NumPadSubtract' = 0x6D; 'NumPadMultiply' = 0x6A; 'NumPadDivide' = 0x6F
        'A' = 0x41; 'V' = 0x56; 'P' = 0x50; 'B' = 0x42
    }
    if ($map.ContainsKey($Name)) { return [uint32]$map[$Name] }
    return [uint32][char]$Name[0]
}

function Resolve-DynAuditArgs {
    param([string[]]$ArgsIn, $Corpus)
    $out = @()
    foreach ($a in $ArgsIn) {
        $t = $a
        if ($t -match '^\{corpus\.(\w+)\}$') {
            $val = $Corpus.($Matches[1])
            if ($val -is [string]) { $t = $val }
        }
        $out += $t
    }
    return $out
}

# The scene's actions, expanded into timed units the engine executes one per tick, with
# the probes interleaved. Storms become per-key / per-move units; waits advance the clock.
function Expand-DynAuditActions {
    param([object[]]$Actions)
    $units = [System.Collections.Generic.List[object]]::new()
    $clock = 0.0
    foreach ($a in @($Actions)) {
        switch ($a.do) {
            'launch' { }                                  # handled before the engine starts
            'wait' { $clock += [double]$a.sec }
            'key' { $units.Add(@{ At = $clock; Op = 'key'; Vk = (Expand-DynAuditKey $a.key) }); $clock += 0.3 }
            'keys' {
                $n = [int]$a.count
                for ($i = 0; $i -lt $n; $i++) {
                    $units.Add(@{ At = $clock; Op = 'key'; Vk = (Expand-DynAuditKey $a.key) })
                    $clock += ([double]$a.intervalMs) / 1000.0
                }
            }
            'storm-keys' {
                $seconds = [double]$a.seconds; $rate = [double]$a.ratePerSec
                $vkList = @(@($a.keys) | ForEach-Object { Expand-DynAuditKey $_ })
                $n = [int]($seconds * $rate)
                for ($i = 0; $i -lt $n; $i++) {
                    $units.Add(@{ At = $clock; Op = 'key'; Vk = $vkList[$i % $vkList.Count] })
                    $clock += 1.0 / $rate
                }
            }
            'storm-resize' {
                $n = [int]([double]$a.seconds * 5)
                for ($i = 0; $i -lt $n; $i++) { $units.Add(@{ At = $clock; Op = 'resize'; I = $i }); $clock += 0.2 }
            }
            'close' { $units.Add(@{ At = $clock; Op = 'close' }) }
            'minimize' { $units.Add(@{ At = $clock; Op = 'minimize' }) }
            'assist' { $units.Add(@{ At = $clock; Op = 'assist'; Prompt = $a.prompt }) }
            'kill-worker' { $units.Add(@{ At = $clock; Op = 'kill-worker' }) }
            'harness-lock' { $units.Add(@{ At = $clock; Op = 'harness-lock'; File = $a.file }) }
            'harness-unlock' { $units.Add(@{ At = $clock; Op = 'harness-unlock' }) }
            'sleep' { $units.Add(@{ At = $clock; Op = 'sleep' }) }
            'lock-workstation' { $units.Add(@{ At = $clock; Op = 'lock-workstation' }) }
            default { }
        }
    }
    return ,$units
}

# --- budget evaluation ----------------------------------------------------------------
# Every comparison is measured-minus-control (section 7); a budget "met" because the probe
# itself was slow is as invalid as one broken by noise. Until a row is accepted=true its
# breach is worded as a hypothesis exceedance, never yet a defect (Q209-4).

function Get-DynAuditBudgetVerdict {
    param($Scene, $Budgets, $Summaries, $Control, $Artifacts, [bool]$RestoreOk, [string[]]$RestoreDetails, $InventoryDelta)

    $violations = [System.Collections.Generic.List[string]]::new()
    $invalid = [System.Collections.Generic.List[string]]::new()
    $notes = [System.Collections.Generic.List[string]]::new()
    $judged = 0
    $budgetById = @{}
    foreach ($b in $Budgets) { $budgetById[$b.id] = $b }
    $half = [string]$Scene.budgetParams.H3.half

    $ping = $Summaries['ping']; $key = $Summaries['key']; $clip = $Summaries['clip']
    $coh = $Summaries['coh']; $lock = $Summaries['lock']; $start = $Summaries['start']
    $uia = $Summaries['uia']; $wrap = $Summaries['wrap']; $pix = $Summaries['pix']

    foreach ($bid in @($Scene.budgets)) {
        $b = $budgetById[$bid]
        if (-not $b) { $invalid.Add("budget $bid is not in the table"); continue }
        $hyp = if ($b.accepted) { '' } else { ' [H]' }
        switch ($bid) {
            'H1' {
                if (-not $ping) { continue }
                $judged++
                $cp95 = [double]$Control.pingP95Ms; $cp99 = [double]$Control.pingP99Ms
                if ($cp95 -gt 15 -or $cp99 -gt 150) { $invalid.Add("the control ping p95 $cp95 ms exceeds the ceiling - the scene is invalid, not judged"); continue }
                $a95 = [Math]::Max(0, $ping.P95Ms - $cp95); $a99 = [Math]::Max(0, $ping.P99Ms - $cp99)
                if ($a95 -gt [double]$b.value.p95Ms) { $violations.Add("H1 ping p95 $($ping.P95Ms) ms (control-adjusted $a95 ms) > $($b.value.p95Ms) ms$hyp") }
                if ($a99 -gt [double]$b.value.p99Ms) { $violations.Add("H1 ping p99 $($ping.P99Ms) ms (control-adjusted $a99 ms) > $($b.value.p99Ms) ms$hyp") }
                if ($ping.Timeouts -gt [int]$b.value.timeouts) { $violations.Add("H1 $($ping.Timeouts) ping timeout(s)$hyp") }
                if ($ping.DroppedPct -gt 1) { $invalid.Add("ping dropped $($ping.DroppedPct)% of its samples (> 1%)") }
            }
            'H2' {
                if (-not $start) { continue }
                $judged++
                if (-not $start.Ready) { $violations.Add("H2 the viewer never reached a responsive window within the probe timeout$hyp") }
                elseif ($start.Ms -gt [double]$b.value.ms) { $violations.Add("H2 cold start $($start.Ms) ms > $($b.value.ms) ms$hyp") }
            }
            'H3' {
                if (-not $key) { continue }
                $judged++
                $limit = if ($half -eq 'large') { [double]$b.value.p95MsLarge } else { [double]$b.value.p95MsSmall }
                if ($key.N -eq 0) { $invalid.Add('the key probe saw no visible effect for any injected key - key latency is not measurable in this state'); continue }
                $cp95 = [double]$Control.keyP95Ms
                if ($cp95 -gt 60) { $invalid.Add("the control key p95 $cp95 ms exceeds the ceiling"); continue }
                $a95 = [Math]::Max(0, $key.P95Ms - $cp95)
                if ($a95 -gt $limit) { $violations.Add("H3 injected-key to effect p95 $($key.P95Ms) ms (control-adjusted $a95 ms) > $limit ms$hyp") }
            }
            'H4' {
                if (-not $Artifacts.pingCsv) { continue }
                $judged++
                $freezes = Get-FreezeEvents $Artifacts.pingCsv
                if ($freezes.Count -gt [int]$b.value.maxFreezes) { $violations.Add("H4 $($freezes.Count) UI freeze(s) >= 500 ms (max observed $($freezes.MaxMs) ms)$hyp") }
                if ($freezes.Gte5s -gt 0) { $notes.Add('H4: a freeze >= 5 s was observed - a P0 candidate (rank T; severity is decided at review)') }
            }
            'H5' {
                if (-not $clip) { continue }
                $judged++
                if ([int]$Control.clipFailures -gt 0) { $invalid.Add("the control run saw $($Control.clipFailures) clipboard open failures - the ceiling is 0"); continue }
                if ($clip.Failures -gt [int]$b.value.failures) { $violations.Add("H5 $($clip.Failures) clipboard open failure(s) attributable to the scene$hyp") }
                if ($clip.SlowOpens -gt 0) { $violations.Add("H5 $($clip.SlowOpens) clipboard open(s) slower than $($b.value.slowOpenMs) ms$hyp") }
            }
            'H6' {
                if (-not $coh) { continue }
                $judged++
                $cP95 = [double]$Control.cohSelfPingP95Ms; $cPass = [double]$Control.cohPassMsP50
                if ($cP95 -le 0) { $invalid.Add('the control run has no coh baseline'); continue }
                if ([Math]::Abs($coh.selfPingP95Ms - $cP95) / $cP95 -gt 0.20) { $invalid.Add("the co-host baseline differs $([Math]::Round(100 * [Math]::Abs($coh.selfPingP95Ms - $cP95) / $cP95, 1))% from the campaign control (> 20%) - the machine got busy mid-run"); continue }
                $pingDelta = $coh.selfPingP95Ms - $cP95
                if ($pingDelta -gt [double]$b.value.pingP95DeltaMs) { $violations.Add("H6 cooperating-host self-ping p95 degraded $([Math]::Round($pingDelta, 2)) ms vs control > $($b.value.pingP95DeltaMs) ms$hyp") }
                if ($cPass -gt 0) {
                    $pct = 100.0 * ($coh.passMsP50 - $cPass) / $cPass
                    if ($pct -gt [double]$b.value.workloadDeltaPct) { $violations.Add("H6 cooperating-host disk workload degraded $([Math]::Round($pct, 1))% vs control > $($b.value.workloadDeltaPct)%$hyp") }
                }
            }
            'H7' {
                if (-not $lock) { continue }
                $judged++
                if (-not $lock.Held) { $invalid.Add('the lock probe never saw the file held - the release time is not measurable'); continue }
                if (-not $lock.Released) { $violations.Add("H7 the held file was not released within the probe window$hyp") }
                elseif ($lock.ReleaseMs -gt [double]$b.value.releaseMs) { $violations.Add("H7 held-file release $($lock.ReleaseMs) ms > $($b.value.releaseMs) ms$hyp") }
            }
            'H8' {
                if (-not $Artifacts.sampCsv) { continue }
                $judged++
                $gdi = Get-SampEndpoints $Artifacts.sampCsv 'gdi'; $user = Get-SampEndpoints $Artifacts.sampCsv 'user'
                if (-not $gdi -or -not $user) { $invalid.Add('the samp series is too short for the GDI/USER end-point check'); continue }
                if ([Math]::Abs($gdi.Last - $gdi.First) -gt [double]$b.value.delta) { $violations.Add("H8 GDI objects moved $($gdi.Last - $gdi.First) from the scene-start baseline$hyp") }
                if ([Math]::Abs($user.Last - $user.First) -gt [double]$b.value.delta) { $violations.Add("H8 USER objects moved $($user.Last - $user.First) from the scene-start baseline$hyp") }
                if ($gdi.Max -ge [double]$b.value.gdiPeak) { $violations.Add("H8 GDI peak $($gdi.Max) >= $($b.value.gdiPeak)$hyp") }
            }
            'H9' {
                if (-not $Artifacts.sampCsv) { continue }
                $judged++
                $hd = Get-SampEndpoints $Artifacts.sampCsv 'handles'; $th = Get-SampEndpoints $Artifacts.sampCsv 'threads'
                if (-not $hd -or -not $th) { $invalid.Add('the samp series is too short for the handle/thread check'); continue }
                if (($hd.Last - $hd.First) -gt [double]$b.value.handleDelta) { $violations.Add("H9 handles grew by $($hd.Last - $hd.First) > $($b.value.handleDelta)$hyp") }
                if (($th.Last - $th.First) -gt [double]$b.value.threadDelta) { $violations.Add("H9 threads grew by $($th.Last - $th.First) > $($b.value.threadDelta)$hyp") }
                if ($Artifacts.isSoak) {
                    $sH = Get-ColumnSlope $Artifacts.sampCsv 'handles' 0.25
                    if ($sH -and $sH.ExcludesZero) { $violations.Add("H9 handle slope CI excludes 0 ($([Math]::Round($sH.Slope, 2))/h)$hyp") }
                }
            }
            'H10' {
                if (-not $Artifacts.sampCsv) { continue }
                $judged++
                $s = Get-ColumnSlope $Artifacts.sampCsv 'privateBytes' 0.25
                if (-not $s) { $invalid.Add('the samp series is too short for a private-bytes slope (the 15 min warm-up alone needs 900 s)'); continue }
                $mbPerHour = $s.Slope * 3600 / 1MB
                if ($s.ExcludesZero -and $mbPerHour -gt [double]$b.value.mbPerHour) { $violations.Add("H10 private bytes slope $([Math]::Round($mbPerHour, 2)) MB/h (CI $([Math]::Round($s.CI95[0], 0))..$([Math]::Round($s.CI95[1], 0))) > $($b.value.mbPerHour) MB/h$hyp") }
            }
            'H11' {
                if (-not $InventoryDelta) { continue }
                $judged++
                if ($InventoryDelta.TempFilesDelta -ne [int]$b.value.tempNet) { $violations.Add("H11 temp net $($InventoryDelta.TempFilesDelta) file(s) after exit, not $($b.value.tempNet)$hyp") }
                if (($InventoryDelta.TempBytesDelta / 1MB) -gt [double]$b.value.peakMb) { $violations.Add("H11 temp bytes grew $($InventoryDelta.TempBytesDelta) > $($b.value.peakMb) MB$hyp") }
                foreach ($log in @($InventoryDelta.LogBytesDelta)) {
                    if (($log.Delta / 1MB) -gt [double]$b.value.logsMb) { $violations.Add("H11 a log grew $($log.Delta) bytes in one scene > $($b.value.logsMb) MB$hyp") }
                }
                if ($InventoryDelta.HiveValuesDelta -ne 0) { $notes.Add("H11: the registry value count moved by $($InventoryDelta.HiveValuesDelta) during the scene - judged against the restore verdict, not assumed zero") }
                if (-not $RestoreOk) { $violations.Add("H11 the registry could not be verified restored: $($RestoreDetails -join '; ')") }
            }
            'H12' {
                if (-not $wrap) { continue }
                $judged++
                if ($wrap.Fraction -lt [double]$b.value.fraction) { $violations.Add("H12 wrap criteria $([int]($wrap.Fraction * 5))/5 met$hyp") }
                if (@($wrap.Secrets).Count -gt [int]$b.value.secrets) { $violations.Add('H12 a new log line names a secret - the line itself is an H12 failure$hyp') }
                if ($wrap.RawExceptionInUi) { $violations.Add("H12 raw exception text was shown to the user$hyp") }
            }
            'H13' {
                $judged++
                if ($uia) {
                    if ($uia.Unnamed -gt [int]$b.value.unnamed) { $violations.Add("H13 $($uia.Unnamed) unnamed interactive control(s) in the $($uia.Reader) read$hyp") }
                    if ($uia.UnreadableToggles -gt [int]$b.value.unreadableToggles) { $violations.Add("H13 $($uia.UnreadableToggles) toggle state(s) could not be read$hyp") }
                }
                if ($pix) {
                    if ($pix.HasEffectPair -and $pix.EffectDiffPixels -le 0) { $violations.Add("H13 the switch's effect is not visible and the label does not say why (pix diff 0)$hyp") }
                    if ($pix.HasRollbackPair -and $pix.RollbackDiffPixels -ne 0) { $violations.Add("H13 rollback identity failed: the pixels differ after toggling off ($($pix.RollbackDiffPixels) sampled pixels)$hyp") }
                }
                $notes.Add('H13: the 13-language label/state matrix is the D-6 hand pass (all 13, RTL included) - the language count rides the campaign report, not this scene line')
            }
            'H14' {
                if (-not $Artifacts.sampCsv) { continue }
                $judged++
                $cpu = Get-SampEndpoints $Artifacts.sampCsv 'cpuPct'
                if (-not $cpu) { $invalid.Add('no cpuPct column in the samp series'); continue }
                $limit = if ($Artifacts.minimized) { [double]$b.value.minimizedPct } else { [double]$b.value.onScreenPct }
                if ($cpu.Max -gt $limit) { $violations.Add("H14 idle CPU peaked at $($cpu.Max)% > $limit%$hyp") }
            }
            default { $invalid.Add("no judge implemented for budget $bid") }
        }
    }
    return @{ Violations = $violations.ToArray(); Judged = $judged; Invalid = $invalid.ToArray(); Notes = $notes.ToArray() }
}

# --- the scene engine -------------------------------------------------------------------

function Invoke-DynAuditScene {
    param([string]$SceneId)
    $name = "DynAudit $SceneId"
    $scene = @(@(Get-DynAuditScenes) | Where-Object { $_.id -eq $SceneId })
    if ($scene.Count -eq 0) { Exit-Verdict $name 2 "scene $SceneId is not in the catalog" }
    $scene = $scene[0]
    $budgets = @(Get-DynAuditBudgets)
    $cellProbes = @((@($scene.cells.R.probes) + @($scene.cells.E.probes) + @($scene.cells.B.probes) + @($scene.cells.L.probes) + @($scene.cells.A.probes)) | Where-Object { $_ } | Select-Object -Unique)
    Write-Subject $name "$($scene.name); probes: $($cellProbes -join ', '); $(Get-TreeLabel $repoRoot)"

    # --- what the scene needs before anything is launched --------------------------
    if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe' }
    if (-not (Test-Path -LiteralPath $ExePath)) {
        Exit-Verdict $name 2 "the viewer exe was not found - build it or pass -ExePath; could not verify, never a pass"
    }
    if ($SecondsOverride -gt 0) { $scene.durationSec = $SecondsOverride }

    $unmet = @(Resolve-DynAuditRequirements -Scene $scene -ExePath $ExePath -RepoRoot $repoRoot -SftpFake $SftpFake -RuntimeTree $RuntimeTree -ConfirmSwitches $Confirm)
    if ($unmet.Count -gt 0) { Exit-Verdict $name 2 ('unmet requirements: ' + ($unmet -join '; ')) }

    if (-not $ControlFile -or -not (Test-Path -LiteralPath $ControlFile)) {
        Exit-Verdict $name 2 'no control run recorded for this campaign (DynAudit.ps1 -Control writes it); a number without a control is worse than no number'
    }
    $control = Get-Content -LiteralPath $ControlFile -Raw | ConvertFrom-Json
    if (-not $control.valid) { Exit-Verdict $name 2 'the campaign control run is marked invalid (its own ceilings were exceeded) - rerun it on a quieter machine' }

    # --- the run folder and the one-time profile ------------------------------------
    if (-not $RunFolder) { $RunFolder = New-DynAuditRunFolder $env:TEMP }
    New-Item -ItemType Directory -Path $RunFolder -Force | Out-Null
    $script:ActiveRunFolder = $RunFolder
    $sceneDir = Join-Path $RunFolder ('scenes\' + $SceneId)
    New-Item -ItemType Directory -Path $sceneDir -Force | Out-Null

    # Step 0: backup BEFORE, no exceptions (section 5). Failure exits 2 before the
    # product exe is ever launched: no backup, no run (the 169 precedent).
    try {
        $null = New-DynAuditProfileBackup $RunFolder
    } catch {
        Exit-Verdict $name 2 "step 0 (backup) failed - no backup, no run: $($_.Exception.Message)"
    }

    $exeDir = Split-Path $ExePath -Parent
    $logPaths = @((Join-Path $exeDir 'current.log'))
    if (@($scene.requirements) -contains 'companion') { $logPaths += (Join-Path $exeDir 'companion\logs\companion.log') }

    $summaries = @{}
    $artifacts = @{ pingCsv = $null; sampCsv = $null; isSoak = $false; minimized = $false }
    $inventoryDelta = $null
    $invalidRun = [System.Collections.Generic.List[string]]::new()
    $engineNotes = [System.Collections.Generic.List[string]]::new()
    $engineError = $null
    $restoreOk = $false; $restoreDetails = @()

    try {
        # --- the synthetic corpus ---------------------------------------------------
        $corpus = New-DynAuditCorpus -RunFolder $RunFolder -Items @($scene.corpus)
        foreach ($p in $corpus.PSObject.Properties) {
            if ($p.Value -is [pscustomobject] -and $p.Value.Error) {
                $invalidRun.Add("corpus '$($p.Name)' could not be built: $($p.Value.Error)")
            }
        }

        if ($invalidRun.Count -eq 0) {
            $openCells = Get-DynAuditSceneCells $scene
            $probeSet = @($openCells.Cells | ForEach-Object { $_.Probes } | Select-Object -Unique)

            # --- pre-scene state ---------------------------------------------------
            $invBefore = Get-InventorySnapshot -LogPaths $logPaths
            $logBefore = @{}
            foreach ($lp in $logPaths) { if (Test-Path -LiteralPath $lp) { $logBefore[$lp] = @(Get-Content -LiteralPath $lp -ErrorAction SilentlyContinue) } }

            # --- launch ------------------------------------------------------------
            $launchArgs = @(Resolve-DynAuditArgs -ArgsIn @($scene.launch.args) -Corpus $corpus)
            $proc = $null; $hwnd = [IntPtr]::Zero
            if ($probeSet -contains 'start') {
                $startSummary = Invoke-StartProbe -ExePath $ExePath -Arguments $launchArgs -TimeoutSec 90
                if ($startSummary) {
                    $proc = Get-Process -Id $startSummary.Pid -ErrorAction SilentlyContinue
                    $hwnd = $startSummary.Hwnd
                    $summaries['start'] = $startSummary
                }
            } else {
                $envBackup = @{}
                foreach ($k in @($scene.launch.env.PSObject.Properties)) {
                    $envBackup[$k.Name] = [Environment]::GetEnvironmentVariable($k.Name, 'Process')
                    [Environment]::SetEnvironmentVariable($k.Name, [string]$k.Value, 'Process')
                }
                try {
                    $proc = if ($launchArgs.Count -gt 0) {
                        Start-Process -FilePath $ExePath -ArgumentList ($launchArgs -join ' ') -PassThru -WorkingDirectory $exeDir
                    } else {
                        Start-Process -FilePath $ExePath -PassThru -WorkingDirectory $exeDir
                    }
                    $deadline = (Get-Date).AddSeconds(90)
                    while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
                        Start-Sleep -Milliseconds 150
                        $p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
                        if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle }
                    }
                } finally {
                    foreach ($k in $envBackup.Keys) { [Environment]::SetEnvironmentVariable($k, $envBackup[$k], 'Process') }
                }
            }
            if (-not $proc -or $hwnd -eq [IntPtr]::Zero) {
                $invalidRun.Add('the product window never became reachable - the scene is not measurable')
            } else {
                Write-Host ("  product pid {0}, window 0x{1}" -f $proc.Id, $hwnd.ToInt64().ToString('X'))

                $duration = [double]$scene.durationSec
                $pingCsv = Join-Path $sceneDir 'ping.csv'
                $sampCsv = Join-Path $sceneDir 'samp.csv'
                $clipCsv = Join-Path $sceneDir 'clip.csv'
                $artifacts.pingCsv = $pingCsv
                $artifacts.sampCsv = $sampCsv
                $artifacts.isSoak = ($duration -ge 1500)
                $artifacts.minimized = (@(@($scene.actions) | Where-Object { $_.do -eq 'minimize' }).Count -gt 0)

                # The samper child samples the product for the whole scene.
                $samper = $null
                if ($probeSet -contains 'samp') {
                    $alsoIds = @(Get-Process -Name 'FastMediaSorterCompanion' -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending | Select-Object -First 1 | ForEach-Object { $_.Id })
                    $samperScript = Join-Path $dynauditDir 'lib\DynAudit-Samper.ps1'
                    $pwshPath = (Get-Process -Id $PID).Path
                    $samperArgs = @('-NoProfile', '-File', $samperScript, '-ProcId', "$($proc.Id)", '-Seconds', "$duration", '-CsvPath', $sampCsv)
                    if ($alsoIds.Count -gt 0) { $samperArgs += @('-AlsoProcIds', ($alsoIds -join ',')) }
                    $samper = Start-Process -FilePath $pwshPath -ArgumentList $samperArgs -PassThru -WindowStyle Hidden
                }

                # The cohost child runs the whole scene when coh is in the set.
                $cohJson = Join-Path $sceneDir 'coh.json'
                $cohProc = $null
                if ($probeSet -contains 'coh' -and $corpus.workload) {
                    $cohostScript = Join-Path $dynauditDir 'lib\DynAudit-Cohost.ps1'
                    $pwshPath = (Get-Process -Id $PID).Path
                    $cohProc = Start-Process -FilePath $pwshPath -ArgumentList @('-NoProfile', '-File', $cohostScript, '-Seconds', "$duration", '-WorkloadFile', $corpus.workload, '-OutJsonPath', $cohJson) -PassThru -WindowStyle Hidden
                }

                # --- the interactive engine ----------------------------------------
                $units = Expand-DynAuditActions @($scene.actions)
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $clipState = @{ Next = 0.0; Failures = 0; Slow = 0; N = 0 }
                $savedClip = $null
                if ($probeSet -contains 'clip') { try { $savedClip = [DynAudit.Native]::GetClipText() } catch { } }
                $keyLatencies = [System.Collections.Generic.List[double]]::new()
                $pendingKeys = [System.Collections.Generic.List[object]]::new()
                $dialogScan = [System.Collections.Generic.List[string]]::new()
                $pixPaths = [System.Collections.Generic.List[string]]::new()
                $assistShown = $false
                $lastDialogSec = -1
                $pingIndex = 0

                while ($sw.Elapsed.TotalSeconds -lt $duration) {
                    $t = $sw.Elapsed.TotalSeconds

                    # ping at 10 Hz, catch-up: every sample scheduled inside the scene is
                    # taken, so sleep granularity never masquerades as a dropped probe.
                    if ($probeSet -contains 'ping') {
                        while (($pingIndex * 0.1) -lt $duration -and $t -ge ($pingIndex * 0.1)) {
                            $swp = [Diagnostics.Stopwatch]::StartNew()
                            $ok = [DynAudit.Native]::Ping($hwnd, 1000)
                            $swp.Stop()
                            Write-ScrubbedLine $pingCsv ("{0},{1},{2}" -f ([Math]::Round($t / 3600.0, 8)), ([Math]::Round($swp.Elapsed.TotalMilliseconds, 2)), $(if ($ok) { 1 } else { 0 }))
                            $pingIndex++
                        }
                    }

                    # clip at 5 Hz, the same catch-up
                    if ($probeSet -contains 'clip' -and $t -ge $clipState.Next) {
                        while ($clipState.Next -lt $duration -and $t -ge $clipState.Next) {
                            $swc = [Diagnostics.Stopwatch]::StartNew()
                            $opened = [DynAudit.Native]::OpenClip()
                            if ($opened) { [void][DynAudit.Native]::CloseClip() }
                            $swc.Stop()
                            $ms = [Math]::Round($swc.Elapsed.TotalMilliseconds, 2)
                            if (-not $opened) { $clipState.Failures++ } else { $clipState.N++; if ($ms -gt 50) { $clipState.Slow++ } }
                            Write-ScrubbedLine $clipCsv ("{0},{1},{2}" -f ([Math]::Round($t / 3600.0, 8)), $ms, $(if ($opened) { 1 } else { 0 }))
                            $clipState.Next = [Math]::Round($clipState.Next + 0.2, 3)
                        }
                    }

                    # injected-key effects, resolved without blocking the probes: each key
                    # rides a pending record checked on every later tick until its visible
                    # effect lands (the ping series never gets a hole from a wait).
                    for ($pi = $pendingKeys.Count - 1; $pi -ge 0; $pi--) {
                        $pk = $pendingKeys[$pi]
                        if (Test-DynAuditTextMapChanged $pk.Map $hwnd) {
                            $keyLatencies.Add([Math]::Round(($t - $pk.At) * 1000.0, 2))
                            $pendingKeys.RemoveAt($pi)
                        } elseif (($t - $pk.At) -ge 2.0) {
                            $pendingKeys.RemoveAt($pi)
                        }
                    }

                    # dialog scan at 1 Hz: any product window that is not the main one
                    if ($probeSet -contains 'err') {
                        $sec = [int][Math]::Floor($t)
                        if ($sec -ne $lastDialogSec) {
                            $lastDialogSec = $sec
                            foreach ($w in [DynAudit.Native]::TopWindowsOf([uint32]$proc.Id)) {
                                if ($w -eq $hwnd) { continue }
                                $text = [DynAudit.Native]::GetText($w)
                                if ($text.Trim()) { $dialogScan.Add($text) }
                            }
                        }
                    }

                    foreach ($u in @($units | Where-Object { $_.At -le $t -and -not $_.Done })) {
                        switch ($u.Op) {
                            'key' {
                                [void][DynAudit.Native]::SetForegroundWindow($hwnd)
                                [void][DynAudit.Native]::SendKey($u.Vk)
                                $pendingKeys.Add(@{ At = [double]$sw.Elapsed.TotalSeconds; Map = (Get-DynAuditWindowTextMap $hwnd) })
                                $u.Done = $true
                            }
                            'resize' {
                                $w = 600 + ($u.I % 4) * 120; $hgt = 400 + ($u.I % 3) * 90
                                [void][DynAudit.Native]::SetWindowPos($hwnd, [IntPtr]::Zero, (10 + ($u.I % 5) * 20), 10, $w, $hgt, 0x0004)
                                $u.Done = $true
                            }
                            'close' { [void][DynAudit.Native]::PostClose($hwnd); $u.Done = $true }
                            'minimize' { [void][DynAudit.Native]::Minimize($hwnd); $u.Done = $true }
                            'assist' {
                                if (-not $assistShown) { Write-Host "`n  [assist] $($u.Prompt)" -ForegroundColor Yellow; $assistShown = $true }
                                if ([Console]::KeyAvailable) {
                                    $k = [Console]::ReadKey($true)
                                    if ($k.Key -eq 'Enter') {
                                        $u.Done = $true; $assistShown = $false
                                        if ($probeSet -contains 'pix' -and $hwnd -ne [IntPtr]::Zero) {
                                            $pix = Join-Path $sceneDir ("pix-{0}.png" -f $pixPaths.Count)
                                            $null = Invoke-PixCapture -Hwnd $hwnd -OutPngPath $pix
                                            $pixPaths.Add($pix)
                                        }
                                    }
                                }
                            }
                            'kill-worker' {
                                foreach ($w in @(Get-Process -Name 'fms-share-worker' -ErrorAction SilentlyContinue)) { try { $w | Stop-Process -Force } catch { } }
                                $u.Done = $true
                            }
                            'harness-lock' {
                                $path = $corpus.lockTarget
                                if ($path -and (Test-Path -LiteralPath $path)) { $script:HarnessLockStream = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None') }
                                $u.Done = $true
                            }
                            'harness-unlock' {
                                if ($script:HarnessLockStream) { $script:HarnessLockStream.Dispose(); $script:HarnessLockStream = $null }
                                $u.Done = $true
                            }
                            'sleep' {
                                Write-Host '  [sleep] initiating sleep/resume - resume the session to continue the scene' -ForegroundColor Yellow
                                [void][DynAudit.Native]::SetSuspendState($false, $false, $false)
                                $u.Done = $true
                            }
                            'lock-workstation' {
                                Write-Host '  [lock] locking the workstation - unlock to continue the scene' -ForegroundColor Yellow
                                [void][DynAudit.Native]::LockWorkStation()
                                $u.Done = $true
                            }
                            default { $u.Done = $true }
                        }
                    }
                    Start-Sleep -Milliseconds 10
                }

                if ($script:HarnessLockStream) { $script:HarnessLockStream.Dispose(); $script:HarnessLockStream = $null }
                $aliveAtEnd = [bool](Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)

                # Schedule overrun: the engine itself must hold the scene's clock; a stretch
                # means the probes were starved and the series is not comparable. The S3
                # session-transition scenes are exempt - sleep, lock and switch own the clock.
                $overrunPct = 100.0 * ($sw.Elapsed.TotalSeconds - $duration) / $duration
                $sessionScene = (@(@($scene.actions) | Where-Object { $_.do -in 'sleep', 'lock-workstation' }).Count -gt 0)
                if ($overrunPct -gt 1 -and -not $sessionScene) {
                    $invalidRun.Add("the engine overran the scene clock by $([Math]::Round($overrunPct, 1))% - the probe series is not comparable")
                }
                if ($pendingKeys.Count -gt 0) { $engineNotes.Add("$($pendingKeys.Count) injected key(s) never showed a visible effect (timed out at 2 s)") }

                # --- post-scene reads -------------------------------------------------
                if ($probeSet -contains 'uia') { $summaries['uia'] = Invoke-UiaProbe -RootHwnd $hwnd -ArtifactDir $sceneDir }
                if ($probeSet -contains 'pix' -and $aliveAtEnd) {
                    $pix = Join-Path $sceneDir ("pix-{0}.png" -f $pixPaths.Count)
                    $null = Invoke-PixCapture -Hwnd $hwnd -OutPngPath $pix
                    $pixPaths.Add($pix)
                }

                # The lock probe (B cells that name it) measures the file the product
                # holds and the time it takes to release it.
                if ($probeSet -contains 'lock') {
                    $lockPath = $corpus.video
                    if (-not $lockPath) { $lockPath = $corpus.lockTarget }
                    if ($lockPath -and (Test-Path -LiteralPath $lockPath)) {
                        $release = { [void][DynAudit.Native]::PostClose($hwnd) }
                        $summaries['lock'] = Invoke-LockProbe -Path $lockPath -SampleSeconds ([Math]::Min(10, $duration)) -Hz 5 -ReleaseScript $release -ReleaseTimeoutSec 30
                    }
                }

                if ($probeSet -contains 'clip') {
                    $restored = $false
                    if ($null -ne $savedClip) { $restored = [DynAudit.Native]::SetClipText($savedClip) }
                    elseif ([DynAudit.Native]::OpenClip()) { [void][DynAudit.Native]::EmptyClip(); [void][DynAudit.Native]::CloseClip(); $restored = $true }
                    $summaries['clip'] = [pscustomobject]@{ Name = 'clip'; Failures = $clipState.Failures; SlowOpens = $clipState.Slow; N = $clipState.N; RestoredText = $restored }
                }

                if ($keyLatencies.Count -gt 0) {
                    $summaries['key'] = Get-LatencySummary 'key' $keyLatencies.ToArray() 0 $keyLatencies.Count $keyLatencies.Count
                }

                if ($probeSet -contains 'ping' -and (Test-Path -LiteralPath $pingCsv)) {
                    $pingMs = @(Get-Content -LiteralPath $pingCsv | Select-Object -Skip 1 | ForEach-Object { [double](($_ -split ',')[1]) })
                    $timeouts = @(Get-Content -LiteralPath $pingCsv | Select-Object -Skip 1 | Where-Object { (($_ -split ',')[2]) -eq '0' }).Count
                    $expected = [int][Math]::Floor($duration * 10)
                    $summaries['ping'] = Get-LatencySummary 'ping' $pingMs $timeouts $expected $pingMs.Count
                }

                if ($samper) {
                    if (-not $samper.HasExited) { try { $samper | Stop-Process -Force } catch { } }
                    $samper = $null
                }
                if ($cohProc) {
                    if (-not $cohProc.HasExited) { try { $cohProc | Stop-Process -Force } catch { } }
                    if (Test-Path -LiteralPath $cohJson) { $summaries['coh'] = Get-Content -LiteralPath $cohJson -Raw | ConvertFrom-Json }
                    $cohProc = $null
                }

                # Close the product and wait; never force-kill it - a product that will
                # not exit is a fact to record.
                if ($aliveAtEnd) {
                    [void][DynAudit.Native]::PostClose($hwnd)
                    $deadline = (Get-Date).AddSeconds(30)
                    while ((Get-Date) -lt $deadline) {
                        if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { break }
                        Start-Sleep -Milliseconds 250
                    }
                }
                $productExited = -not [bool](Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
                if (-not $productExited) { $invalidRun.Add('the product did not exit within 30 s of WM_CLOSE - recorded, never force-killed; the L end-state is not measurable') }

                # err: the wrap criteria from what came out
                if ($probeSet -contains 'err') {
                    $newLines = [System.Collections.Generic.List[string]]::new()
                    foreach ($lp in $logPaths) {
                        if (-not (Test-Path -LiteralPath $lp)) { continue }
                        if (-not $logBefore.ContainsKey($lp)) { $newLines.AddRange(@(Get-Content -LiteralPath $lp -ErrorAction SilentlyContinue)); continue }
                        $before = $logBefore[$lp]
                        foreach ($l in @(Get-Content -LiteralPath $lp -ErrorAction SilentlyContinue)) { if ($before -notcontains $l) { $newLines.Add($l) } }
                    }
                    $newLogLines = $newLines.ToArray()
                    if ($newLogLines.Count -gt 0) { Write-ScrubbedLines (Join-Path $sceneDir 'log-delta.txt') $newLogLines }
                    if ($dialogScan.Count -gt 0) { Write-ScrubbedLines (Join-Path $sceneDir 'dialog-scan.txt') $dialogScan.ToArray() }
                    $uiText = ($dialogScan.ToArray() -join "`n")
                    $summaries['wrap'] = Get-WrapCriteria -ProcessAlive $aliveAtEnd -NewLogLines $newLogLines -UiText $uiText -ExpectedDialogWords @($scene.dialogs) -NextActionOk $productExited
                }

                # pix pairs: the effect diff and the rollback identity
                if ($pixPaths.Count -ge 2) {
                    $effect = Get-PixDiff $pixPaths[0] $pixPaths[1]
                    $rollback = if ($pixPaths.Count -ge 3) { Get-PixDiff $pixPaths[0] $pixPaths[$pixPaths.Count - 1] } else { $null }
                    $summaries['pix'] = [pscustomobject]@{
                        EffectDiffPixels = if ($effect) { [long]$effect.DiffPixels } else { 0 }
                        RollbackDiffPixels = if ($rollback) { [long]$rollback.DiffPixels } else { -1 }
                        HasEffectPair = ($null -ne $effect); HasRollbackPair = ($null -ne $rollback)
                    }
                }

                $invAfter = Get-InventorySnapshot -LogPaths $logPaths
                $inventoryDelta = Get-InventoryDelta -Before $invBefore -After $invAfter
            }
        }
    } catch {
        $engineError = $_.Exception.Message
    } finally {
        if ($script:HarnessLockStream) { $script:HarnessLockStream.Dispose(); $script:HarnessLockStream = $null }
        # The restore runs even after a crash of the scene runner (section 5 item 4).
        try {
            $r = Restore-DynAuditProfile $RunFolder
            $restoreOk = [bool]$r.Ok
            $restoreDetails = @($r.Details)
        } catch {
            $restoreOk = $false
            $restoreDetails = @($_.Exception.Message)
        }
    }
    if ($engineError) { $invalidRun.Add("the scene engine failed: $engineError") }

    # --- the budget judgment ---------------------------------------------------------
    $verdict = Get-DynAuditBudgetVerdict -Scene $scene -Budgets $budgets -Summaries $summaries -Control $control -Artifacts $artifacts -RestoreOk $restoreOk -RestoreDetails $restoreDetails -InventoryDelta $inventoryDelta
    $violations = @($verdict.Violations)
    $invalidRun.AddRange(@($verdict.Invalid))
    $notes = @($verdict.Notes) + @($engineNotes.ToArray())

    $naCount = (Get-DynAuditSceneCells $scene).NaCount
    $budgetCount = @($scene.budgets).Count
    $detailParts = [System.Collections.Generic.List[string]]::new()
    if ($violations.Count -gt 0) { $detailParts.Add(($violations -join '; ')) }
    if ($invalidRun.Count -gt 0) { $detailParts.Add(($invalidRun.ToArray() -join '; ')) }
    $detailParts.Add("$budgetCount budgets, $naCount n/a")
    if ($restoreOk) { $detailParts.Add('restore verified') } else { $detailParts.Add('RESTORE FAILED (P0): ' + ($restoreDetails -join '; ')) }
    foreach ($n in $notes) { $detailParts.Add($n) }

    if (-not $restoreOk) {
        # The harness's own restore failure is the one P0 that outranks every scene number
        # (section 10): reported on the line, never silent, never a pass.
        Exit-Verdict $name 1 "restore failed (P0): $($restoreDetails -join '; ')"
    }
    $code = if ($invalidRun.Count -gt 0) { 2 } elseif ($violations.Count -gt 0) { 1 } else { 0 }
    Exit-Verdict $name $code ($detailParts -join '; ')
}

# --- the self-test (the D-0 validity gate) ------------------------------------------------

function Invoke-DynAuditSelfTest {
    Write-Subject 'Invoke-DynScene' "the D-0 validity gate: every probe against the harness's own fixture window, the capability cross-check, the matrix validation, the verdict contract; $(Get-TreeLabel $repoRoot)"
    $script:ST_FAIL = 0
    $okCount = 0
    function Fail([string]$what) { $script:ST_FAIL++; Write-Host "  FAIL: $what" -ForegroundColor Red }

    $selfFolder = Join-Path $env:TEMP ("fms-dynaudit-selftest-{0}" -f (Get-Date -Format 'HHmmssfff'))
    New-Item -ItemType Directory -Path $selfFolder -Force | Out-Null
    $lockFile = Join-Path $selfFolder 'fixture-lock.png'
    Set-Content -LiteralPath $lockFile -Value 'x'

    # (b) + (c): the data files first - they gate everything else and cost nothing.
    $data = Test-DynAuditData $repoRoot
    foreach ($p in $data.Problems) { Fail $p }
    if ($data.Problems.Count -eq 0 -and $data.Totals.Open -eq 214 -and $data.Totals.NA -eq 36 -and $data.Totals.Measured -eq 0) {
        $okCount++
        Write-Host "  ok: the matrix validates (50 scenes, $($data.Totals.Cells) cells: 214 open, 36 n/a, 0 measured); every cell names only probes that exist; 19 capability rows, their anchors present" -ForegroundColor Green
    } elseif ($data.Problems.Count -eq 0) {
        Fail "the matrix totals are $($data.Totals.Open) open / $($data.Totals.NA) n/a / $($data.Totals.Measured) measured - the Draft table of section 9 is 214/36/0"
    }

    # (a) every probe against the fixture window.
    $fixture = Start-DynAuditFixture -RunFolder $selfFolder -LockFile $lockFile
    if (-not $fixture) {
        Remove-Item -LiteralPath $lockFile -Force -ErrorAction SilentlyContinue
        Exit-Verdict 'Invoke-DynScene' 2 'the fixture window never came up - the probes cannot be verified'
    }
    try {
        $hwnd = $fixture.Hwnd
        $fixturePid = $fixture.FixturePid

        # ping
        $ping = Invoke-PingProbe -Hwnd $hwnd -Seconds 5 -Hz 10 -CsvPath (Join-Path $selfFolder 'st-ping.csv')
        if ($ping.P95Ms -lt 10 -and $ping.Timeouts -eq 0 -and $ping.DroppedPct -le 1) {
            $okCount++; Write-Host ("  ok: ping p95 {0} ms, {1} timeouts, {2}% dropped (ceilings 10 ms / 0 / 1%)" -f $ping.P95Ms, $ping.Timeouts, $ping.DroppedPct) -ForegroundColor Green
        } else { Fail ("ping against the fixture: p95 $($ping.P95Ms) ms, $($ping.Timeouts) timeouts, $($ping.DroppedPct)% dropped") }

        # samp sees the fixture allocate exactly 50 GDI objects
        $h = [DynAudit.Native]::OpenProc($fixturePid)
        $gdiBefore = -1
        if ($h -ne [IntPtr]::Zero) { $gdiBefore = [long][DynAudit.Native]::GuiResources($h, 0); [void][DynAudit.Native]::CloseProc($h) }
        [void](Send-DynAuditFixtureMessage -Hwnd $hwnd -Msg 0x8001 -WParam 50)
        Start-Sleep -Milliseconds 500
        $h = [DynAudit.Native]::OpenProc($fixturePid)
        $gdiAfter = -1
        if ($h -ne [IntPtr]::Zero) { $gdiAfter = [long][DynAudit.Native]::GuiResources($h, 0); [void][DynAudit.Native]::CloseProc($h) }
        $gdiDelta = $gdiAfter - $gdiBefore
        if ($gdiDelta -eq 50) { $okCount++; Write-Host "  ok: samp sees the fixture allocate exactly 50 GDI objects ($gdiBefore -> $gdiAfter)" -ForegroundColor Green }
        else { Fail "samp saw a GDI delta of $gdiDelta, not 50" }
        [void](Send-DynAuditFixtureMessage -Hwnd $hwnd -Msg 0x8002)

        # key on the trivial form
        $key = Invoke-KeyProbe -Hwnd $hwnd -Count 10 -CsvPath (Join-Path $selfFolder 'st-key.csv') -EffectMode caption
        if ($key.P95Ms -lt 60 -and $key.NoEffect -eq 0) { $okCount++; Write-Host ("  ok: key on the trivial form: p95 {0} ms, 10/10 effects seen" -f $key.P95Ms) -ForegroundColor Green }
        else { Fail "key on the trivial form: p95 $($key.P95Ms) ms, $($key.NoEffect) without effect" }

        # coh: two runs. The MACHINERY is what the self-test proves here (two summaries,
        # the workload actually ran); the 10% spread ceiling belongs to the control run
        # of section 7, measured on the campaign machine - a loaded dev machine fails it
        # legitimately, and that must not gate the probes' own verification.
        $workload = Join-Path $selfFolder 'workload-20mb.bin'
        $buf = [byte[]]::new(1MB); (New-Object Random 3).NextBytes($buf)
        $fs = [IO.File]::Create($workload)
        try { for ($i = 0; $i -lt 20; $i++) { $fs.Write($buf, 0, $buf.Length) } } finally { $fs.Dispose() }
        $warmFs = [IO.File]::OpenRead($workload); try { $null = $warmFs.Read($buf, 0, $buf.Length) } finally { $warmFs.Dispose() }
        $coh1 = Invoke-CohProbe -Seconds 10 -WorkloadFile $workload -OutJsonPath (Join-Path $selfFolder 'st-coh1.json') -RunFolder $selfFolder
        $coh2 = Invoke-CohProbe -Seconds 10 -WorkloadFile $workload -OutJsonPath (Join-Path $selfFolder 'st-coh2.json') -RunFolder $selfFolder
        if ($coh1 -and $coh2 -and $coh1.passes -ge 5 -and $coh2.passes -ge 5) {
            $spread = 100.0 * [Math]::Abs($coh1.passMsP50 - $coh2.passMsP50) / [Math]::Max(1.0, (($coh1.passMsP50 + $coh2.passMsP50) / 2))
            $okCount++
            if ($spread -le 10) {
                Write-Host ("  ok: coh produced two workload summaries, spread {0}% (pass p50 {1} / {2} ms)" -f [Math]::Round($spread, 1), $coh1.passMsP50, $coh2.passMsP50) -ForegroundColor Green
            } else {
                Write-Host ("  ok: coh produced two workload summaries (spread {0}% over the 10% control ceiling - the machine is busy; the control run of section 7 judges the spread on the campaign machine)" -f [Math]::Round($spread, 1)) -ForegroundColor Yellow
            }
        } else { Fail 'coh did not produce two workload summaries' }

        # clip round-trip
        $hadText = $false; $savedText = $null
        try { $savedText = [DynAudit.Native]::GetClipText(); $hadText = ($null -ne $savedText) } catch { }
        $clip = Invoke-ClipProbe -Seconds 10 -Hz 5 -CsvPath (Join-Path $selfFolder 'st-clip.csv')
        $backText = $null
        try { $backText = [DynAudit.Native]::GetClipText() } catch { }
        if ($hadText -and $null -eq $backText) {
            Start-Sleep -Milliseconds 250   # a clipboard listener can lag the read-back
            try { $backText = [DynAudit.Native]::GetClipText() } catch { }
        }
        $clipOk = ($clip.Failures -eq 0)
        if ($hadText -and $backText -ne $savedText) { $clipOk = $false }
        if ($clipOk) { $okCount++; Write-Host ("  ok: clip round-trip at 5 Hz, 0 failures, contents restored ({0} opens; text at start: {1})" -f $clip.N, $hadText) -ForegroundColor Green }
        else { Fail "clip round-trip: $($clip.Failures) failures; restore $(if ($hadText) { 'did not come back byte for byte' } else { 'n/a' })" }

        # lock round-trip against the fixture itself
        $selfHwnd = $hwnd
        $lockProbe = Invoke-LockProbe -Path $lockFile -SampleSeconds 3 -Hz 5 -ReleaseScript {
            [void][DynAudit.Native]::SendAppMessage($selfHwnd, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero)
        } -ReleaseTimeoutSec 10
        if ($lockProbe.Held -and $lockProbe.Released -and $lockProbe.ReleaseMs -lt 500) {
            $okCount++; Write-Host ("  ok: lock sees the fixture hold the file, refusal p50 {0} ms, release {1} ms" -f $lockProbe.RefusalP50Ms, $lockProbe.ReleaseMs) -ForegroundColor Green
        } else { Fail "lock round-trip: held=$($lockProbe.Held) released=$($lockProbe.Released) releaseMs=$($lockProbe.ReleaseMs)" }

        # slope: a planted ramp is detected, planted noise is rejected. The noise stays
        # small relative to the series length: the test is that a CI which should include
        # zero does, not that a coin never lands on its edge.
        $rx = [double[]](0..99 | ForEach-Object { [double]$_ })
        $ry = [double[]](0..99 | ForEach-Object { 100.0 * $_ + 5 + ((Get-Random -Minimum -100 -Maximum 100) / 100.0) })
        $ramp = Get-SlopeStats -X $rx -Y $ry
        $nx = [double[]](0..99 | ForEach-Object { [double]$_ })
        $ny = [double[]](0..99 | ForEach-Object { 1000.0 + (Get-Random -Minimum -50 -Maximum 50) })
        $noise = Get-SlopeStats -X $nx -Y $ny
        if ($ramp -and $ramp.ExcludesZero -and $ramp.Slope -gt 90 -and $noise -and -not $noise.ExcludesZero) {
            $okCount++; Write-Host ("  ok: slope detects the planted ramp ({0}/h, CI {1}..{2}) and rejects planted noise (CI {3}..{4})" -f [Math]::Round($ramp.Slope, 1), [Math]::Round($ramp.CI95[0], 1), [Math]::Round($ramp.CI95[1], 1), [Math]::Round($noise.CI95[0], 1), [Math]::Round($noise.CI95[1], 1)) -ForegroundColor Green
        } else { Fail "slope: ramp ExcludesZero=$($ramp.ExcludesZero) slope=$($ramp.Slope); noise ExcludesZero=$($noise.ExcludesZero)" }

        # inv sees a marked temp file come and go (unique name: a leftover from an aborted
        # run must not sit in both snapshots and hide the delta)
        Get-ChildItem -LiteralPath $env:TEMP -Filter 'fms-dynaudit-selftest-marked*.tmp' -Force -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        $inv1 = Get-InventorySnapshot -LogPaths @()
        $marked = Join-Path $env:TEMP ("fms-dynaudit-selftest-marked-{0}.tmp" -f (Get-Date -Format 'HHmmssfff'))
        Set-Content -LiteralPath $marked -Value 'x'
        $inv2 = Get-InventorySnapshot -LogPaths @()
        Remove-Item -LiteralPath $marked -Force
        $invDeltaFiles = $inv2.tempFiles - $inv1.tempFiles
        if ($invDeltaFiles -ge 1) { $okCount++; Write-Host "  ok: inv sees the marked temp file (delta $invDeltaFiles) and reads the hive (values: $($inv2.hiveValues), exists: $($inv2.hiveExists))" -ForegroundColor Green }
        else { Fail "inv did not see the marked temp file (delta $invDeltaFiles)" }

        # uia against the fixture's named controls
        $uia = Invoke-UiaProbe -RootHwnd $hwnd -ArtifactDir $selfFolder -Tag 'st-uia'
        if ($uia.Interactive -ge 2 -and $uia.Unnamed -eq 0) { $okCount++; Write-Host ("  ok: uia ({0} reader) sees {1} controls, {2} interactive, 0 unnamed, toggle states readable" -f $uia.Reader, $uia.Controls, $uia.Interactive) -ForegroundColor Green }
        else { Fail "uia ($($uia.Reader) reader): $($uia.Controls) controls, $($uia.Interactive) interactive, $($uia.Unnamed) unnamed" }

        # pix: identical frames are identical; a change is a change
        $p0 = Join-Path $selfFolder 'st-pix-0.png'
        $null = Invoke-PixCapture -Hwnd $hwnd -OutPngPath $p0
        Start-Sleep -Milliseconds 300
        $p1 = Join-Path $selfFolder 'st-pix-1.png'
        $null = Invoke-PixCapture -Hwnd $hwnd -OutPngPath $p1
        $sameDiff = Get-PixDiff $p0 $p1
        [void][DynAudit.Native]::SetForegroundWindow($hwnd)
        [void][DynAudit.Native]::SendKey(0x74)
        Start-Sleep -Milliseconds 300
        $p2 = Join-Path $selfFolder 'st-pix-2.png'
        $null = Invoke-PixCapture -Hwnd $hwnd -OutPngPath $p2
        $changeDiff = Get-PixDiff $p0 $p2
        if ($sameDiff -and $changeDiff -and $sameDiff.DiffPixels -eq 0 -and $changeDiff.DiffPixels -gt 0) {
            $okCount++; Write-Host ("  ok: pix diff is 0 over identical frames and {0} sampled pixels over a change" -f $changeDiff.DiffPixels) -ForegroundColor Green
        } else { Fail "pix: identical=$($sameDiff.DiffPixels) changed=$($changeDiff.DiffPixels)" }

        # start: a second fixture window reaches its first responsive ping
        $fixture2 = Start-DynAuditFixture -RunFolder $selfFolder
        if ($fixture2 -and $fixture2.Hwnd -ne [IntPtr]::Zero) {
            $ok = [DynAudit.Native]::Ping($fixture2.Hwnd, 250)
            if ($ok) { $okCount++; Write-Host '  ok: start reaches a second fixture window and its first ping answers' -ForegroundColor Green }
            else { Fail 'start: the second fixture window did not answer its first ping' }
            Stop-DynAuditFixture $fixture2
        } else { Fail 'start: the second fixture window never appeared' }

        # err: the wrap-criteria checker on planted samples
        $good = Get-WrapCriteria -ProcessAlive $true -NewLogLines @('2026-10-11 translate refused: endpoint unreachable (typed refusal)') -UiText 'Translation is unavailable: the endpoint refused the connection.' -ExpectedDialogWords @('unavailable') -NextActionOk $true
        $raw = Get-WrapCriteria -ProcessAlive $true -NewLogLines @('x') -UiText 'System.NullReferenceException: Object reference not set to an instance of an object.' -ExpectedDialogWords @('unavailable') -NextActionOk $true
        $secret = Get-WrapCriteria -ProcessAlive $true -NewLogLines @('connect password=SuperSecret1') -UiText 'Refused.' -ExpectedDialogWords @('Refused') -NextActionOk $true
        if ($good.Fraction -eq 1.0 -and -not $raw.KnownOutcome -and $raw.RawExceptionInUi -and -not $secret.NoSecrets) {
            $okCount++; Write-Host '  ok: err judges a wrapped sample 5/5, rejects raw exception text in the UI and detects a secret-bearing log line' -ForegroundColor Green
        } else { Fail "err: wrapped=$($good.Fraction) rawKnown=$($raw.KnownOutcome) rawSeen=$($raw.RawExceptionInUi) secretCaught=$(-not $secret.NoSecrets)" }

        # nrt: the transcript machinery and the tool resolution
        $narrator = Get-NarratorPath
        $walk = Invoke-UiaWin32Walk -RootHwnd $hwnd
        $transcriptLines = New-NarratorTranscript -Path (Join-Path $selfFolder 'st-narrator-transcript.txt') -Controls $walk.Controls -Language 'en'
        if ($narrator -and $transcriptLines -ge 2) { $okCount++; Write-Host "  ok: nrt resolves Narrator and writes a $transcriptLines-line transcript (the scripted spoken pass is the D-6 hand step)" -ForegroundColor Green }
        elseif (-not $narrator -and $transcriptLines -ge 2) { $okCount++; Write-Host '  ok: nrt transcript machinery verified; Narrator is absent here - the nrt pass reports n/a in its A cells, never a silent pass' -ForegroundColor Green }
        else { Fail "nrt: narrator=$([bool]$narrator) transcriptLines=$transcriptLines" }
    } finally {
        Stop-DynAuditFixture $fixture
        Remove-Item -LiteralPath $lockFile -Force -ErrorAction SilentlyContinue
    }

    # (d) the verdict contract: exit 2 - never 0 - when the viewer exe is absent, and an
    # unknown scene is never a pass either. Child runs, each with its own exit code.
    $pwshPath = (Get-Process -Id $PID).Path
    $self = Join-Path $dynauditDir 'Invoke-DynScene.ps1'
    $absent = Join-Path $selfFolder 'no-such-viewer.exe'
    $out1 = & $pwshPath -NoProfile -File $self -Scene 'S2.1' -ExePath $absent 2>&1
    $code1 = $LASTEXITCODE
    $out2 = & $pwshPath -NoProfile -File $self -Scene 'S99.9' -ExePath $absent 2>&1
    $code2 = $LASTEXITCODE
    $line1 = @($out1 | Where-Object { "$_" -match '^DynAudit S2\.1: ' }) | Select-Object -Last 1
    $line2 = @($out2 | Where-Object { "$_" -match '^DynAudit S99\.9: ' }) | Select-Object -Last 1
    if ($code1 -eq 2 -and "$line1" -match 'COULD NOT VERIFY') { $okCount++; Write-Host '  ok: exit 2 - never 0 - when the viewer exe is absent (the verdict line follows CHECK-VERDICT)' -ForegroundColor Green }
    else { Fail "exe-absent child: exit $code1, line '$line1'" }
    if ($code2 -eq 2 -and "$line2" -match 'COULD NOT VERIFY') { $okCount++; Write-Host '  ok: an unknown scene id is exit 2 with its own verdict line' -ForegroundColor Green }
    else { Fail "unknown-scene child: exit $code2, line '$line2'" }

    # the scrub machinery: nothing real survives into an artifact
    $scrubbed = Get-ScrubbedText "profile $env:USERPROFILE\deep\path machine $env:COMPUTERNAME token=abc123"
    if ($scrubbed -match [regex]::Escape($env:USERNAME) -or $scrubbed -match [regex]::Escape($env:COMPUTERNAME) -or $scrubbed -match 'abc123') {
        Fail "scrubbing left identity material: $scrubbed"
    } else { $okCount++; Write-Host "  ok: scrubbing redacts on write ($scrubbed)" -ForegroundColor Green }

    if ($script:ST_FAIL -gt 0) {
        Exit-Verdict 'Invoke-DynScene' 2 "$($script:ST_FAIL) self-test failure(s) - no scene run before a passing self-test is valid (D-0)"
    }
    Exit-Verdict 'Invoke-DynScene' 0 "$okCount checks; every probe verified against the fixture; the matrix validates (50 scenes, 214 open, 36 n/a, 0 measured); 19 capability rows, their anchors present; the verdict contract proven"
}

# --- entry ---------------------------------------------------------------------------------

if ($SelfTest) {
    Invoke-DynAuditSelfTest
} elseif ($Scene) {
    Invoke-DynAuditScene -SceneId $Scene
} else {
    Write-Host 'usage: Invoke-DynScene.ps1 -Scene <id> | -SelfTest (see the header and README.md)'
    Exit-Verdict 'Invoke-DynScene' 2 'neither -Scene nor -SelfTest was given'
}
