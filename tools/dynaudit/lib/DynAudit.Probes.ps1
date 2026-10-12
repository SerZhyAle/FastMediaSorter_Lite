<#
.SYNOPSIS
    The DynAudit probes (specification 209 section 4): ping, key, coh, clip, lock, samp,
    inv, err, uia, pix, start, nrt. Every probe measures from outside the product - by
    window handle or process id - and writes its series into the run folder scrubbed
    (section 12 item 2). Nothing here patches or attaches to the product.
#>

. (Join-Path $PSScriptRoot 'DynAudit.Native.ps1')
. (Join-Path $PSScriptRoot 'DynAudit.Scrub.ps1')
Use-DynAuditNative

# --- helpers -------------------------------------------------------------------

function Get-Percentile {
    param([double[]]$Values, [double]$P)
    if (-not $Values -or $Values.Count -eq 0) { return 0.0 }
    $sorted = [double[]]($Values | Sort-Object)
    $rank = [Math]::Ceiling(($P / 100.0) * $sorted.Count) - 1
    if ($rank -lt 0) { $rank = 0 }
    if ($rank -ge $sorted.Count) { $rank = $sorted.Count - 1 }
    return [double]$sorted[[int]$rank]
}

function Get-LatencySummary {
    param([string]$Name, [double[]]$Ms, [int]$Timeouts, [int]$Expected, [int]$Actual)
    return [pscustomobject]@{
        Name      = $Name
        N         = $Actual
        Expected  = $Expected
        DroppedPct = if ($Expected -gt 0) { [Math]::Round(100.0 * ($Expected - $Actual) / $Expected, 2) } else { 0 }
        P50Ms     = [Math]::Round((Get-Percentile $Ms 50), 2)
        P95Ms     = [Math]::Round((Get-Percentile $Ms 95), 2)
        P99Ms     = [Math]::Round((Get-Percentile $Ms 99), 2)
        MaxMs     = if ($Ms.Count) { [Math]::Round(($Ms | Measure-Object -Maximum).Maximum, 2) } else { 0 }
        Timeouts  = $Timeouts
    }
}

# --- ping: SendMessageTimeout(WM_NULL) at Hz against a window ------------------

function Invoke-PingProbe {
    param([IntPtr]$Hwnd, [double]$Seconds = 30, [double]$Hz = 10, [string]$CsvPath = '', [uint32]$TimeoutMs = 1000)
    if ($Hwnd -eq [IntPtr]::Zero) { return $null }
    $intervalMs = 1000.0 / $Hz
    # Absolute-time scheduling with catch-up: a sample the previous sleep overshot is
    # taken immediately, so sleep granularity never shows up as "dropped samples" - the
    # 1% drop rule of section 7 must catch a starved probe, not the timer.
    $swAll = [Diagnostics.Stopwatch]::StartNew()
    $latencies = [System.Collections.Generic.List[double]]::new()
    $timeouts = 0
    $rows = [System.Collections.Generic.List[object]]::new()
    $targetMs = 0.0
    while ($targetMs / 1000.0 -lt $Seconds) {
        $waitMs = $targetMs - $swAll.Elapsed.TotalMilliseconds
        if ($waitMs -gt 0) { Start-Sleep -Milliseconds $waitMs }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $ok = [DynAudit.Native]::Ping($Hwnd, $TimeoutMs)
        $sw.Stop()
        if (-not $ok) { $timeouts++ }
        $ms = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        $latencies.Add($ms)
        $rows.Add(@("$([Math]::Round($swAll.Elapsed.TotalHours, 6))", "$ms", "$(if ($ok) { 1 } else { 0 })"))
        $targetMs += $intervalMs
    }
    if ($CsvPath) { Write-ScrubbedCsv $CsvPath @('hours', 'ms', 'ok') ($rows.ToArray()) }
    $expected = [int][Math]::Floor($Seconds * $Hz)
    return Get-LatencySummary 'ping' $latencies.ToArray() $timeouts $expected $latencies.Count
}

# Freeze events out of a ping series: a sample >= 500 ms is a freeze; adjacent freeze
# samples within 1 s of each other are one event. Returns @{ Count; MaxMs; Gte5s }
function Get-FreezeEvents {
    param([string]$CsvPath, [double]$FreezeMs = 500)
    if (-not (Test-Path -LiteralPath $CsvPath)) { return @{ Count = 0; MaxMs = 0; Gte5s = 0 } }
    $events = 0; $max = 0.0; $gte5 = 0; $inEvent = $false
    foreach ($line in ((Get-Content -LiteralPath $CsvPath | Select-Object -Skip 1) | Where-Object { $_.Trim() })) {
        $parts = $line -split ','
        $ms = 0.0
        if (-not [double]::TryParse($parts[1], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$ms)) { continue }
        if ($ms -gt $max) { $max = $ms }
        if ($ms -ge 5000) { $gte5++ }
        if ($ms -ge $FreezeMs) {
            if (-not $inEvent) { $events++; $inEvent = $true }
        } else { $inEvent = $false }
    }
    return @{ Count = $events; MaxMs = [Math]::Round($max, 2); Gte5s = $gte5 }
}

# --- key: SendInput one key, poll the visible effect -----------------------------

function Get-DynAuditWindowTextMap {
    param([IntPtr]$Hwnd)
    $map = @{}
    $map[[int64]$Hwnd.ToInt64()] = [DynAudit.Native]::GetText($Hwnd)
    foreach ($child in [DynAudit.Native]::ChildrenOf($Hwnd)) {
        $map[[int64]$child.ToInt64()] = [DynAudit.Native]::GetText($child)
    }
    return $map
}

function Test-DynAuditTextMapChanged {
    param($Before, [IntPtr]$Hwnd)
    $after = Get-DynAuditWindowTextMap $Hwnd
    foreach ($k in @($after.Keys)) {
        if (-not $Before.ContainsKey($k) -or $Before[$k] -ne $after[$k]) { return $true }
    }
    foreach ($k in @($Before.Keys)) {
        if (-not $after.ContainsKey($k)) { return $true }
    }
    return $false
}

function Invoke-KeyProbe {
    param([IntPtr]$Hwnd, [int]$Count = 10, [uint32]$KeyVk = 0x74, [double]$PollIntervalMs = 2, [double]$EffectTimeoutMs = 2000, [string]$CsvPath = '', [ValidateSet('caption', 'tree')][string]$EffectMode = 'caption')
    # 0x74 = VK_F5: the fixture advances its caption counter on it; product scenes pass
    # the navigation key of the scene and read the effect from the caption ('caption') or
    # from any child-window text change ('tree' - the HUD file label is a child control,
    # not the caption).
    $latencies = [System.Collections.Generic.List[double]]::new()
    $rows = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $Count; $i++) {
        [void][DynAudit.Native]::SetForegroundWindow($Hwnd)
        Start-Sleep -Milliseconds 30
        $before = if ($EffectMode -eq 'tree') { Get-DynAuditWindowTextMap $Hwnd } else { [DynAudit.Native]::GetText($Hwnd) }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        [void][DynAudit.Native]::SendKey($KeyVk)
        $sawEffect = $false
        while ($sw.Elapsed.TotalMilliseconds -lt $EffectTimeoutMs) {
            if ($EffectMode -eq 'tree') { $sawEffect = Test-DynAuditTextMapChanged $before $Hwnd }
            else { $sawEffect = ([DynAudit.Native]::GetText($Hwnd) -ne $before) }
            if ($sawEffect) { break }
            Start-Sleep -Milliseconds $PollIntervalMs
        }
        $sw.Stop()
        $ms = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        if ($sawEffect) { $latencies.Add($ms) }
        $rows.Add(@("$i", "$ms", "$(if ($sawEffect) { 1 } else { 0 })"))
        Start-Sleep -Milliseconds 50
    }
    if ($CsvPath) { Write-ScrubbedCsv $CsvPath @('key', 'ms', 'effect') ($rows.ToArray()) }
    $summary = Get-LatencySummary 'key' $latencies.ToArray() 0 $Count $latencies.Count
    $summary | Add-Member -NotePropertyName NoEffect -NotePropertyValue ($Count - $latencies.Count)
    return $summary
}

# --- coh: the cooperating host, a separate process with its own window -----------

# Runs tools\dynaudit\lib\DynAudit-Cohost.ps1 in its own process: a window that pings
# itself and reads a fixed 20 MB workload for the duration. This is the B probe - another
# program on the machine, measuring what the product's scene does to it.
function Invoke-CohProbe {
    param([double]$Seconds = 30, [string]$WorkloadFile, [string]$OutJsonPath, [string]$RunFolder)
    $cohost = Join-Path $PSScriptRoot 'DynAudit-Cohost.ps1'
    if (-not (Test-Path -LiteralPath $WorkloadFile)) { return $null }
    $out = if ($OutJsonPath) { $OutJsonPath } else { Join-Path $RunFolder ('coh-{0}.json' -f (Get-Date -Format 'HHmmss')) }
    $pwsh = (Get-Process -Id $PID).Path
    & $pwsh -NoProfile -File $cohost -Seconds $Seconds -WorkloadFile $WorkloadFile -OutJsonPath $out | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $out)) { return $null }
    return Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
}

# --- clip: open the clipboard at Hz; failure/latency = contention ----------------

function Invoke-ClipProbe {
    param([double]$Seconds = 10, [double]$Hz = 5, [string]$CsvPath = '')
    $saved = $null
    try { $saved = [DynAudit.Native]::GetClipText() } catch { }
    $hadText = ($null -ne $saved)
    $intervalMs = 1000.0 / $Hz
    # Absolute-time scheduling with catch-up, like the ping probe: the timer never lies
    # about dropped samples (section 7).
    $swAll = [Diagnostics.Stopwatch]::StartNew()
    $targetMs = 0.0
    $expected = 0; $failures = 0; $slowOpens = 0
    $latencies = [System.Collections.Generic.List[double]]::new()
    $rows = [System.Collections.Generic.List[object]]::new()
    while ($targetMs / 1000.0 -lt $Seconds) {
        $waitMs = $targetMs - $swAll.Elapsed.TotalMilliseconds
        if ($waitMs -gt 0) { Start-Sleep -Milliseconds $waitMs }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $ok = [DynAudit.Native]::OpenClip()
        if ($ok) { [void][DynAudit.Native]::CloseClip() }
        $sw.Stop()
        $ms = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        if (-not $ok) { $failures++ } else {
            $latencies.Add($ms)
            if ($ms -gt 50) { $slowOpens++ }
        }
        $rows.Add(@("$([Math]::Round($swAll.Elapsed.TotalHours, 6))", "$ms", "$(if ($ok) { 1 } else { 0 })"))
        $targetMs += $intervalMs
        $expected++
    }
    if ($CsvPath) { Write-ScrubbedCsv $CsvPath @('hours', 'ms', 'ok') ($rows.ToArray()) }
    # Restore: the text format is saved and put back; binary formats are not preserved and
    # the artifact says so (section 12 item 4 - the probe restores what it can and records it).
    $restored = $false
    if ($hadText) { $restored = [DynAudit.Native]::SetClipText($saved) }
    elseif ([DynAudit.Native]::OpenClip()) { [void][DynAudit.Native]::EmptyClip(); [void][DynAudit.Native]::CloseClip(); $restored = $true }
    return [pscustomobject]@{
        Name = 'clip'; N = $latencies.Count; Expected = $expected
        P50Ms = [Math]::Round((Get-Percentile $latencies.ToArray() 50), 2)
        P95Ms = [Math]::Round((Get-Percentile $latencies.ToArray() 95), 2)
        Failures = $failures; SlowOpens = $slowOpens
        RestoredText = $restored; HadText = $hadText
    }
}

# --- lock: rename/delete/move attempts against a held file ----------------------

function Invoke-LockProbe {
    param([string]$Path, [double]$SampleSeconds = 5, [double]$Hz = 5, [scriptblock]$ReleaseScript = $null, [double]$ReleaseTimeoutSec = 30)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $tmp = "$Path.dynaudit-probe"
    $intervalMs = 1000.0 / $Hz
    $attempts = 0; $refusals = 0
    $refusalLatencies = [System.Collections.Generic.List[double]]::new()
    $held = $false
    $releaseMs = $null
    $swAll = [Diagnostics.Stopwatch]::StartNew()
    $released = $false
    while ($swAll.Elapsed.TotalSeconds -lt $SampleSeconds -and -not $released) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $moved = $false
        try { Move-Item -LiteralPath $Path -Destination $tmp -ErrorAction Stop; $moved = $true } catch { }
        $sw.Stop()
        $attempts++
        if ($moved) {
            try { Move-Item -LiteralPath $tmp -Destination $Path -ErrorAction Stop } catch { }
            if ($held -and $releaseSw) {
                $releaseMs = [Math]::Round($releaseSw.Elapsed.TotalMilliseconds, 2)
                $released = $true
            }
        } else {
            $held = $true
            $refusals++
            $refusalLatencies.Add([Math]::Round($sw.Elapsed.TotalMilliseconds, 2))
        }
        if (-not $moved) { Start-Sleep -Milliseconds $intervalMs }
    }
    # Phase two: the release trigger (pause/stop/close, or the fixture's release message),
    # then the attempts continue until the file moves - the distance is the release time.
    if ($held -and -not $released) {
        if ($ReleaseScript) { & $ReleaseScript }
        $releaseSw = [Diagnostics.Stopwatch]::StartNew()
        while ($releaseSw.Elapsed.TotalSeconds -lt $ReleaseTimeoutSec) {
            $moved = $false
            try { Move-Item -LiteralPath $Path -Destination $tmp -ErrorAction Stop; $moved = $true } catch { }
            if ($moved) {
                try { Move-Item -LiteralPath $tmp -Destination $Path -ErrorAction Stop } catch { }
                $releaseMs = [Math]::Round($releaseSw.Elapsed.TotalMilliseconds, 2)
                $released = $true
                break
            }
            Start-Sleep -Milliseconds 50
        }
    }
    return [pscustomobject]@{
        Name = 'lock'; Attempts = $attempts; Held = $held
        RefusalP50Ms = if ($refusalLatencies.Count) { [Math]::Round((Get-Percentile $refusalLatencies.ToArray() 50), 2) } else { $null }
        ReleaseMs = $releaseMs; Released = $released
    }
}

# --- samp: the 1 Hz external sampler -------------------------------------------

function Invoke-SampProbe {
    param([int]$ProcId, [double]$Seconds = 30, [double]$Hz = 1, [string]$CsvPath = '', [string[]]$AlsoProcIds = @())
    $pids = @($ProcId) + @($AlsoProcIds | Where-Object { $_ -and $_ -ne $ProcId })
    $intervalMs = 1000.0 / $Hz
    $expected = [int][Math]::Floor($Seconds * $Hz)
    # Absolute-time scheduling with catch-up (as the ping and clip probes): the sampler
    # holds its own clock; a missed-second sample is taken at once, so the 1% drop rule
    # of section 7 catches a starved probe, not the timer.
    $rows = [System.Collections.Generic.List[object]]::new()
    $prevCpu = @{}; $prevWall = [Diagnostics.Stopwatch]::StartNew()
    $cores = [Environment]::ProcessorCount
    $gcCounter = $null; $gcTried = $false
    $swAll = [Diagnostics.Stopwatch]::StartNew()
    $targetMs = 0.0
    while ($targetMs / 1000.0 -lt $Seconds) {
        $waitMs = $targetMs - $swAll.Elapsed.TotalMilliseconds
        if ($waitMs -gt 0) { Start-Sleep -Milliseconds $waitMs }
        $tick = [Math]::Round($swAll.Elapsed.TotalHours, 6)
        $proc = Get-Process -Id $ProcId -ErrorAction SilentlyContinue
        if (-not $proc) { break }
        $gdi = -1; $user = -1
        $h = [DynAudit.Native]::OpenProc($ProcId)
        if ($h -ne [IntPtr]::Zero) {
            $g = [DynAudit.Native]::GuiResources($h, 0); $u = [DynAudit.Native]::GuiResources($h, 1)
            [void][DynAudit.Native]::CloseProc($h)
            if ($g -ne [uint32]::MaxValue) { $gdi = [long]$g }
            if ($u -ne [uint32]::MaxValue) { $user = [long]$u }
        }
        $cpuTotal = $proc.TotalProcessorTime.TotalSeconds
        $cpuPct = ''
        if ($prevCpu.ContainsKey($ProcId)) {
            $dWall = $prevWall.Elapsed.TotalSeconds
            if ($dWall -gt 0) { $cpuPct = [Math]::Round(100.0 * ($cpuTotal - $prevCpu[$ProcId]) / $dWall / $cores, 2) }
        }
        $prevCpu[$ProcId] = $cpuTotal
        [void]$prevWall.Restart()
        $gc = ''
        if (-not $gcTried) { $gcCounter = Get-DynAuditGcCounter $ProcId $proc.ProcessName; $gcTried = $true }
        if ($gcCounter) { try { $gc = [string][long]$gcCounter.RawValue } catch { $gcCounter = $null; $gc = '' } }
        $extra = foreach ($op in $AlsoProcIds) {
            $oproc = Get-Process -Id $op -ErrorAction SilentlyContinue
            if ($oproc) { "op{0}h={1};op{0}t={2};op{0}b={3}" -f $op, $oproc.HandleCount, $oproc.Threads.Count, $oproc.PrivateMemorySize64 }
        }
        $conns = 0
        try {
            $conns = @(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue |
                Where-Object { $pids -contains $_.OwningProcess -and $_.RemoteAddress -notin @('127.0.0.1', '::1', '0.0.0.0', '::') -and $null -ne $_.RemoteAddress }).Count
        } catch { }
        $rows.Add(@("$tick", "$gdi", "$user", "$($proc.HandleCount)", "$($proc.Threads.Count)", "$($proc.PrivateMemorySize64)", "$($proc.WorkingSet64)", "$cpuPct", "$gc", "$conns", ($extra -join '|')))
        $targetMs += $intervalMs
    }
    if ($CsvPath) {
        Write-ScrubbedCsv $CsvPath @('hours', 'gdi', 'user', 'handles', 'threads', 'privateBytes', 'workingSet', 'cpuPct', 'gcHeapBytes', 'nonLoopback', 'others') ($rows.ToArray())
    }
    return [pscustomobject]@{
        Name = 'samp'; N = $rows.Count; Expected = $expected
        DroppedPct = if ($expected -gt 0) { [Math]::Round(100.0 * ($expected - $rows.Count) / $expected, 2) } else { 0 }
    }
}

# Maps a pid to its ".NET CLR Memory" counter instance once per probe session; an
# unreadable counter is remembered as absent (never re-attempted each second - the
# lookup is expensive and a single-file .NET 10 exe often publishes nothing readable
# from outside). The instance-to-pid mapping goes through the Process category's
# "ID Process" counter: the CLR category has no pid counter of its own.
function Get-DynAuditGcCounter {
    param([int]$ProcId, [string]$ProcName)
    try {
        $cat = New-Object System.Diagnostics.PerformanceCounterCategory('.NET CLR Memory')
        foreach ($inst in $cat.GetInstanceNames()) {
            if ($inst -ne $ProcName -and -not $inst.StartsWith($ProcName)) { continue }
            $pidCounter = New-Object System.Diagnostics.PerformanceCounter('Process', 'ID Process', $inst, $false)
            if ($pidCounter.RawValue -eq $ProcId) {
                return New-Object System.Diagnostics.PerformanceCounter('.NET CLR Memory', '# Bytes in all Heaps', $inst, $false)
            }
        }
    } catch { }
    return $null
}

# The first and last value of one samp column (scene-end deltas against scene start).
function Get-SampEndpoints {
    param([string]$CsvPath, [string]$Column)
    if (-not (Test-Path -LiteralPath $CsvPath)) { return $null }
    $lines = @(Get-Content -LiteralPath $CsvPath | Where-Object { $_.Trim() })
    if ($lines.Count -lt 2) { return $null }
    $header = ($lines[0] -split ',')
    $col = [array]::IndexOf($header, $Column)
    if ($col -lt 0) { return $null }
    $vals = [System.Collections.Generic.List[double]]::new()
    $maxVal = [double]::MinValue
    foreach ($line in ($lines | Select-Object -Skip 1)) {
        $parts = $line -split ','
        if ($parts.Count -le $col) { continue }
        $v = 0.0
        if (-not [double]::TryParse($parts[$col], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$v)) { continue }
        $vals.Add($v)
        if ($v -gt $maxVal) { $maxVal = $v }
    }
    if ($vals.Count -lt 2) { return $null }
    return @{ First = [double]$vals[0]; Last = [double]$vals[$vals.Count - 1]; Max = [double]$maxVal; N = $vals.Count }
}

# --- start: cold/warm start clock to first responsive ping -----------------------

function Invoke-StartProbe {
    param([string]$ExePath, [string[]]$Arguments = @(), [string]$WorkingDirectory = '', [double]$TimeoutSec = 30, [double]$PingTimeoutMs = 250)
    if (-not (Test-Path -LiteralPath $ExePath)) { return $null }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory } else { $psi.WorkingDirectory = (Split-Path $ExePath -Parent) }
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $hwnd = [IntPtr]::Zero
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        Start-Sleep -Milliseconds 100
        $p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            if ([DynAudit.Native]::Ping($p.MainWindowHandle, [uint32]$PingTimeoutMs)) { $hwnd = $p.MainWindowHandle; break }
        }
    }
    $sw.Stop()
    return [pscustomobject]@{
        Name = 'start'; Ms = [Math]::Round($sw.Elapsed.TotalMilliseconds, 0)
        Ready = ($hwnd -ne [IntPtr]::Zero); Pid = $proc.Id; Hwnd = $hwnd
    }
}

# --- inv: inventory deltas -------------------------------------------------------

# One snapshot of everything property L counts across a session boundary: app-attributable
# temp files and bytes, the LOCALAPPDATA data folder, the two logs, the registry hive.
function Get-InventorySnapshot {
    param([string[]]$LogPaths = @())
    $snap = [ordered]@{ TakenUtc = (Get-Date).ToUniversalTime().ToString('o') }

    $tempFiles = 0; $tempBytes = [long]0
    if (Test-Path -LiteralPath $env:TEMP) {
        $marker = 'fastmediasorter|fms|fmsl|\.fms-part|fastmediasortercompanion'
        foreach ($f in (Get-ChildItem -LiteralPath $env:TEMP -Recurse -Force -File -ErrorAction SilentlyContinue)) {
            if ($f.Name -imatch $marker) { $tempFiles++; $tempBytes += $f.Length }
        }
    }
    $snap.tempFiles = $tempFiles; $snap.tempBytes = $tempBytes

    $dataDir = Join-Path $env:LOCALAPPDATA 'SZA\FastMediaSorter'
    $dirFiles = 0; $dirBytes = [long]0
    if (Test-Path -LiteralPath $dataDir) {
        foreach ($f in (Get-ChildItem -LiteralPath $dataDir -Recurse -Force -File -ErrorAction SilentlyContinue)) { $dirFiles++; $dirBytes += $f.Length }
    }
    $snap.dataDirExists = (Test-Path -LiteralPath $dataDir)
    $snap.dataDirFiles = $dirFiles; $snap.dataDirBytes = $dirBytes

    $logs = @{}
    foreach ($lp in $LogPaths) {
        if ($lp -and (Test-Path -LiteralPath $lp)) { $logs[$lp] = (Get-Item -LiteralPath $lp).Length } else { $logs[$lp] = 0 }
    }
    $snap.logs = $logs

    $hive = 'HKCU:\SZA\FastMediaSorter'
    $valueCount = 0; $valueBytes = [long]0; $subKeys = 0
    if (Test-Path -LiteralPath $hive) {
        $keys = @(Get-ChildItem -LiteralPath $hive -Recurse -ErrorAction SilentlyContinue) + @(Get-Item -LiteralPath $hive)
        foreach ($k in $keys) {
            $p = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
            if ($p) {
                foreach ($prop in $p.PSObject.Properties) {
                    if ($prop.Name -in 'PSPath', 'PSParentPath', 'PSChildName', 'PSDrive', 'PSProvider') { continue }
                    $valueCount++
                    if ($prop.Value -is [string]) { $valueBytes += [Text.Encoding]::UTF8.GetByteCount([string]$prop.Value) } else { $valueBytes += 8 }
                }
            }
        }
        $subKeys = @(Get-ChildItem -LiteralPath $hive -Recurse -ErrorAction SilentlyContinue).Count
    }
    $snap.hiveExists = (Test-Path -LiteralPath $hive)
    $snap.hiveValues = $valueCount; $snap.hiveValueBytes = $valueBytes; $snap.hiveSubKeys = $subKeys
    return [pscustomobject]$snap
}

function Get-InventoryDelta {
    param($Before, $After)
    if (-not $Before -or -not $After) { return $null }
    return [pscustomobject]@{
        TempFilesDelta   = $After.tempFiles - $Before.tempFiles
        TempBytesDelta   = $After.tempBytes - $Before.tempBytes
        DataDirFilesDelta = $After.dataDirFiles - $Before.dataDirFiles
        DataDirBytesDelta = $After.dataDirBytes - $Before.dataDirBytes
        LogBytesDelta    = $After.logs.PSObject.Properties | ForEach-Object {
            $name = $_.Name; @{ Log = $name; Delta = $After.logs.$name - $Before.logs.$name }
        }
        HiveValuesDelta  = $After.hiveValues - $Before.hiveValues
    }
}

# --- err: the wrap-criteria checker ----------------------------------------------

# The five criteria of property E, judged from what can be read from outside: the process
# survived, a log line appeared, the UI text is a known outcome and not a raw exception,
# the new log lines hold no secret, and the next action still works.
function Test-RawExceptionText {
    param([string]$Text)
    if (-not $Text) { return $false }
    return ($Text -match 'System\.[\w.]+Exception' -or $Text -match '(?m)^\s+at [\w$.]+\(' -or $Text -match 'HRESULT[: ]]?0x')
}

function Test-SecretInText {
    param([string]$Text)
    if (-not $Text) { return $null }
    $m = @([regex]::Matches($Text, '(?i)(password|passwd|token|apikey|api_key|secret)\s*[=:]'))
    if ($m.Count -eq 0) { return $null }
    return ($m | ForEach-Object { $_.Value })
}

function Get-WrapCriteria {
    param([bool]$ProcessAlive, [string[]]$NewLogLines, [string]$UiText, [string[]]$ExpectedDialogWords, [bool]$NextActionOk)
    $hasDialog = $UiText -and $UiText.Trim()
    $knownOutcome = $false
    if ($hasDialog) {
        foreach ($w in $ExpectedDialogWords) {
            if ($w -and $UiText -match [regex]::Escape($w)) { $knownOutcome = $true; break }
        }
    }
    $rawException = Test-RawExceptionText $UiText
    $secrets = @()
    foreach ($l in $NewLogLines) {
        $s = Test-SecretInText $l
        if ($s) { $secrets += $s }
    }
    $caught = ($ProcessAlive -and (($NewLogLines | Measure-Object).Count -gt 0 -or $hasDialog))
    return [pscustomobject]@{
        Caught        = [bool]$caught
        KnownOutcome  = [bool]($knownOutcome -and -not $rawException)
        NoSecrets     = [bool]($secrets.Count -eq 0)
        HumanWords    = [bool]($hasDialog -and -not $rawException -and $knownOutcome)
        Consistent    = [bool]$NextActionOk
        RawExceptionInUi = [bool]$rawException
        Secrets       = $secrets
        Fraction      = [double]((@($caught, ($knownOutcome -and -not $rawException), ($secrets.Count -eq 0), ($hasDialog -and -not $rawException -and $knownOutcome), $NextActionOk) | Where-Object { $_ }).Count) / 5.0
    }
}

# --- uia: the interactive-surface tree read ----------------------------------------

# Reader "win32" (deterministic, works everywhere): every child window with its text (the
# Name), class-derived role, tab-stop bit (keyboard reachability) and, for buttons, the
# check state read from outside via BM_GETCHECK - the toggle state the A property needs.
function Invoke-UiaWin32Walk {
    param([IntPtr]$RootHwnd, [int]$MaxDepth = 8)
    $entries = [System.Collections.Generic.List[object]]::new()
    $rows = [System.Collections.Generic.List[object]]::new()
    $walk = {
        param([IntPtr]$hwnd, [int]$depth)
        if ($depth -gt $MaxDepth) { return }
        $style = [DynAudit.Native]::GetStyle($hwnd)
        $class = [DynAudit.Native]::GetClass($hwnd)
        $text = [DynAudit.Native]::GetText($hwnd)
        $visible = (($style -band 0x10000000) -ne 0)   # WS_VISIBLE
        $tabstop = (($style -band 0x00010000) -ne 0)   # WS_TABSTOP
        $role = 'pane'
        if ($class -match 'BUTTON') { $role = 'button' }
        elseif ($class -match 'EDIT') { $role = 'edit' }
        elseif ($class -match 'COMBO|LIST') { $role = 'list' }
        elseif ($class -match 'STATIC') { $role = 'text' }
        elseif ($class -match 'MSCTL|SCROLLBAR') { $role = 'control' }
        $check = $null
        if ($class -match 'BUTTON') {
            $check = [DynAudit.Native]::SendAppMessage($hwnd, 0x00F8, [IntPtr]::Zero, [IntPtr]::Zero)  # BM_GETCHECK
        }
        $interactive = ($role -in 'button', 'edit', 'list' -or $tabstop)
        $rows.Add(@("$($hwnd.ToInt64())", "$class", "$text", "$role", "$(if ($visible) { 1 } else { 0 })", "$(if ($tabstop) { 1 } else { 0 })", "$(if ($null -ne $check) { "$check" } else { '' })"))
        $entries.Add([pscustomobject]@{
            Hwnd = $hwnd.ToInt64(); Class = $class; Name = $text; Role = $role
            Visible = $visible; TabStop = $tabstop; Interactive = $interactive
            ToggleState = $(if ($check -ne $null) { [int]$check } else { $null })
        })
        foreach ($child in [DynAudit.Native]::ChildrenOf($hwnd)) { & $walk $child ($depth + 1) }
    }
    & $walk $RootHwnd 0
    return [pscustomobject]@{
        Reader = 'win32'; Root = $RootHwnd.ToInt64(); Controls = $entries
    }
}

# Reader "uia": the real UI Automation tree, read by a Windows PowerShell 5.1 child (the
# UIA client assemblies are .NET Framework only; pwsh cannot load them). Falls back to the
# win32 walk when the child or the assemblies are absent - the artifact records which
# reader produced it, so a win32-read A cell is never mistaken for a full UIA pass.
function Invoke-UiaProbe {
    param([IntPtr]$RootHwnd, [string]$ArtifactDir, [string]$Tag = 'uia')
    $win32 = Invoke-UiaWin32Walk -RootHwnd $RootHwnd
    $result = $win32
    $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ((Test-Path -LiteralPath $ps51) -and $RootHwnd -ne [IntPtr]::Zero) {
        $uia = Invoke-DynAuditUiaChild -Ps51 $ps51 -RootHwnd $RootHwnd
        # The UIA child can fail without saying so (a minimized or fast-closing window reads
        # as one element): a walk that sees LESS than the deterministic win32 reader is a
        # failed read, never a thinner truth - the artifact records which reader produced it.
        if ($uia -and @($uia.Controls).Count -gt @($win32.Controls).Count) { $result = $uia }
    }
    $outPath = Join-Path $ArtifactDir "$Tag.json"
    Write-ScrubbedJson $outPath $result
    # The H13 counts: interactive controls without any name, and toggle states that cannot
    # be read at all. Empty names on non-interactive panes are not violations.
    $interactive = @($result.Controls | Where-Object { $_.Interactive })
    $unnamed = @($interactive | Where-Object { -not $_.Name -and $_.Visible })
    $unreadableToggles = @($interactive | Where-Object { $_.Role -eq 'button' -and $null -eq $_.ToggleState })
    return [pscustomobject]@{
        Name = 'uia'; Reader = $result.Reader
        Controls = $result.Controls.Count; Interactive = $interactive.Count
        Unnamed = $unnamed.Count; UnreadableToggles = $unreadableToggles.Count
        Artifact = $outPath
    }
}

function Invoke-DynAuditUiaChild {
    param([string]$Ps51, [IntPtr]$RootHwnd)
    $script = @'
param([long]$RootHwndRaw)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$out = New-Object System.Collections.Generic.List[object]
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$RootHwndRaw)
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$stack = New-Object System.Collections.Stack
$stack.Push(@{ El = $root; Depth = 0 })
while ($stack.Count -gt 0) {
    $frame = $stack.Pop()
    $el = $frame.El; $d = $frame.Depth
    if ($d -gt 8) { continue }
    $cur = $el.Current
    $toggle = $null
    if ($cur.IsTogglePatternAvailable) {
        try { $toggle = [int]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState } catch { }
    }
    $out.Add([pscustomobject]@{
        Hwnd = $cur.NativeWindowHandle; Class = $cur.ClassName; Name = $cur.Name
        Role = $cur.ControlType.ProgrammaticName; Visible = (-not $cur.IsOffscreen)
        TabStop = $cur.IsKeyboardFocusable; Interactive = $cur.IsKeyboardFocusable
        ToggleState = $toggle
    })
    $child = $walker.GetFirstChildElement($el)
    while ($child) {
        $stack.Push(@{ El = $child; Depth = ($d + 1) })
        $child = $walker.GetNextSiblingElement($child)
    }
}
$out | ConvertTo-Json -Depth 4 -Compress
'@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    try {
        $json = & $Ps51 -NoProfile -EncodedCommand $encoded -RootHwndRaw $RootHwnd.ToInt64() 2>$null
        $text = ($json -join "`n")
        if (-not $text.Trim()) { return $null }
        $controls = $text | ConvertFrom-Json
        return [pscustomobject]@{ Reader = 'uia'; Root = $RootHwnd.ToInt64(); Controls = $controls }
    } catch { return $null }
}

# --- pix: before/after screenshot diff ---------------------------------------------

function Invoke-PixCapture {
    param([IntPtr]$Hwnd, [string]$OutPngPath)
    Add-Type -AssemblyName System.Drawing
    # The window is rendered by PrintWindow with PW_RENDERFULLCONTENT: the capture is the
    # window itself - no screen-coordinate space to mismatch across mixed-DPI monitors,
    # no covering window, no focus stealing. A window PrintWindow cannot render falls
    # back to the screen read (foreground first, so the region is its own).
    $r = [DynAudit.Native]::Rect($Hwnd)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0 -or $r.Left -le -30000) { return $null }
    $bmp = [System.Drawing.Bitmap]::new($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $printed = $false
    try { $printed = [DynAudit.Native]::PrintWindowFull($Hwnd, $hdc) } finally { $g.ReleaseHdc($hdc) }
    if (-not $printed) {
        $g.Dispose()
        $bmp.Dispose()
        [void][DynAudit.Native]::SetForegroundWindow($Hwnd)
        Start-Sleep -Milliseconds 120
        $bmp = [System.Drawing.Bitmap]::new($w, $h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, [System.Drawing.Size]::new($w, $h)) } finally { $g.Dispose() }
    } else { $g.Dispose() }
    $bmp.Save($OutPngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $OutPngPath
}

function Get-PixDiff {
    param([string]$BeforePng, [string]$AfterPng)
    Add-Type -AssemblyName System.Drawing
    if (-not ((Test-Path -LiteralPath $BeforePng) -and (Test-Path -LiteralPath $AfterPng))) { return $null }
    $a = [System.Drawing.Bitmap]::FromFile($BeforePng); $b = [System.Drawing.Bitmap]::FromFile($AfterPng)
    try {
        $w = [Math]::Min($a.Width, $b.Width); $h = [Math]::Min($a.Height, $b.Height)
        $diff = [long]0
        for ($x = 0; $x -lt $w; $x += 2) {
            for ($y = 0; $y -lt $h; $y += 2) {
                if ($a.GetPixel($x, $y) -ne $b.GetPixel($x, $y)) { $diff++ }
            }
        }
        return @{ DiffPixels = $diff; SampledPixels = ([long]($w / 2) * [long]($h / 2)) }
    } finally { $a.Dispose(); $b.Dispose() }
}

# --- nrt: the scripted accessibility-tool pass -------------------------------------

# Narrator is resolved and its transcript format is validated; the scripted walk itself is
# a hand-run D-6 step (the campaign runs it on the metal session, Q209-7), so the probe's
# machinery is what the self-test verifies, not a spoken pass.
function Get-NarratorPath {
    $candidate = Join-Path $env:SystemRoot 'System32\Narrator.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    return $null
}

function New-NarratorTranscript {
    param([string]$Path, [object]$Controls, [string]$Language)
    # The transcript the campaign records per language: one announced line per control,
    # name + state, scrubbed on write like every artifact.
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("language=$Language")
    foreach ($c in $Controls) {
        if (-not $c.Interactive -or -not $c.Visible) { continue }
        $state = if ($null -ne $c.ToggleState) { "state=$($c.ToggleState)" } else { '' }
        $lines.Add(("announce: name='{0}' role={1} {2}" -f $c.Name, $c.Role, $state))
    }
    Write-ScrubbedLines $Path $lines.ToArray()
    return $lines.Count
}

# --- fixture management ------------------------------------------------------------

function Start-DynAuditFixture {
    param([string]$RunFolder, [string]$LockFile = '', [int]$ReadyTimeoutSec = 20)
    $ready = Join-Path $RunFolder ('fixture-ready-{0}.txt' -f (Get-Date -Format 'HHmmssfff'))
    if (Test-Path -LiteralPath $ready) { Remove-Item -LiteralPath $ready -Force }
    $fixtureScript = Join-Path $PSScriptRoot 'DynAudit-Fixture.ps1'
    $pwsh = (Get-Process -Id $PID).Path
    $args = @('-NoProfile', '-File', $fixtureScript, '-ReadyFile', $ready)
    if ($LockFile) { $args += @('-LockFile', $LockFile) }
    $proc = Start-Process -FilePath $pwsh -ArgumentList $args -PassThru -WindowStyle Minimized
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $ready) {
            # A short settle: the form restores itself on Shown (the minimized start of the
            # child would otherwise leave it at -32000,-32000), and CenterScreen applies
            # only once it is visible.
            Start-Sleep -Milliseconds 400
            $parts = (Get-Content -LiteralPath $ready -Raw).Trim() -split "`t"
            return [pscustomobject]@{ FixturePid = [int]$parts[0]; Hwnd = [IntPtr]::new([int64]$parts[1]); Process = $proc; ReadyFile = $ready }
        }
        if ($proc.HasExited) { return $null }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Stop-DynAuditFixture {
    param($Fixture)
    if (-not $Fixture) { return }
    foreach ($msg in @(0x8004)) {
        [void][DynAudit.Native]::SendAppMessage($Fixture.Hwnd, $msg, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        if ($Fixture.Process.HasExited) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $Fixture.Process.HasExited) { try { $Fixture.Process | Stop-Process -Force } catch { } }
    if (Test-Path -LiteralPath $Fixture.ReadyFile) { Remove-Item -LiteralPath $Fixture.ReadyFile -Force -ErrorAction SilentlyContinue }
}

function Send-DynAuditFixtureMessage {
    param([IntPtr]$Hwnd, [uint32]$Msg, [int]$WParam = 0)
    return [DynAudit.Native]::SendAppMessage($Hwnd, $Msg, [IntPtr]::new($WParam), [IntPtr]::Zero)
}
