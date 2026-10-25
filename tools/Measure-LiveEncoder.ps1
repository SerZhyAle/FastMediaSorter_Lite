<#
.SYNOPSIS
    Hand-run encoder measurements for the live broadcaster (ticket 263 T-1, Q199-1).

.DESCRIPTION
    Candidate-agnostic: it reads whatever serves MPEG-TS over HTTP at -Url, so the same run measures
    (a) the present FFmpeg child, (b) a LibVLC stream output (for example
    `vlc <source> --sout "#transcode{vcodec=h264,acodec=mpga}:std{access=http,mux=ts,dst=:8780/live.ts}"`)
    and (c) a Media Foundation H.264 encoder with a TS muxer, once one exists. Start the candidate by
    hand, with the source of the row (screen 1080p30, or the camera's highest negotiated mode), then run:

        pwsh -NoProfile -File tools\Measure-LiveEncoder.ps1 -Url http://192.168.1.20:8768/<token>/live.ts `
             -Candidate a-ffmpeg -Source screen -ProcessName FastMediaSorterCompanion

    What it measures (all from the desk side of the wire, no receiver needed):
      * time to first byte and time to first keyframe, over -Joins separate connections (LIVE-BROADCAST
        item I; the budget is keyframeIntervalMs, 2000 when absent, plus 1000 ms);
      * the real keyframe interval on one long connection of -DurationSeconds (joinable at least every 2 s,
        target 1 s), both as arrival spacing and as presentation-timestamp spacing;
      * the stream's average bitrate;
      * CPU of the producer's whole process tree (the process named -ProcessName and every descendant,
        so the FFmpeg child is inside the figure), as percent of ONE core and of the whole machine.

    What it can NOT measure: producer-to-picture latency. That needs a receiver. The hand method: show a
    running millisecond clock on the broadcast screen, play the stream on the NAMED receiver beside it,
    photograph both with one phone camera, and take the difference of the two clocks; repeat at least
    every minute for 10 minutes and write the steady-state median and the worst value. (199 target
    <= 1000 ms producer-to-picture; the receiver-side criterion AC-1 stays <= 1.5 s.)

    The script prints measurements next to the targets and NEVER a pass/fail verdict: the decision is the
    owner's, written with the table into docs/specifications/live-broadcast-gate-evidence.md (Q263-1).
    Packaging cost (what ships, its size, its licence line) is filled by hand from the candidate.

    A protected LAN endpoint answers HTTP Digest; pass -User / -Password for it.

.PARAMETER Url             The candidate's MPEG-TS endpoint (the LAN live.ts URL, or any http TS source).
.PARAMETER Candidate       Label for the table row.
.PARAMETER Source          Label for the table row: screen or camera.
.PARAMETER ProcessName     Process whose tree is sampled for CPU (no extension). Empty skips the CPU figure.
.PARAMETER DurationSeconds Length of the steady-state run (the ticket asks for at least 600).
.PARAMETER Joins           Number of separate connections for the time-to-first-picture figures.
.PARAMETER OutFile         Where the dated markdown row and the raw numbers go (default %TEMP%\fms-bench\).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Url,
    [string]$User = '',
    [string]$Password = '',
    [ValidateSet('a-ffmpeg', 'b-libvlc', 'c-mediafoundation', 'other')][string]$Candidate = 'a-ffmpeg',
    [ValidateSet('screen', 'camera')][string]$Source = 'screen',
    [string]$ProcessName = 'FastMediaSorterCompanion',
    [ValidateRange(10, 7200)][int]$DurationSeconds = 600,
    [ValidateRange(1, 100)][int]$Joins = 10,
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Net.Http, System.Net.Primitives, System.Collections, System.Runtime, System.Threading, System.Threading.Tasks -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class TsProbeResult
{
    public double FirstByteMs = -1;
    public double FirstKeyframeMs = -1;
    public long Bytes;
    public double ElapsedMs;
    public List<double> KeyframeArrivalMs = new List<double>();
    public List<double> KeyframePtsMs = new List<double>();
    public string Error = "";
}

// Reads an MPEG-TS stream over HTTP and notes every keyframe of the first video elementary stream:
// PAT -> PMT -> the H.264 (0x1B) or HEVC (0x24) PID, then each PES start that carries the
// random-access flag in its adaptation field or an IDR NAL in the first packet.
public static class TsProbe
{
    public static Task<TsProbeResult> RunAsync(string url, string user, string password, double stopAfterMs, double stopAfterKeyframes)
    {
        return Task.Run(() => Run(url, user, password, stopAfterMs, stopAfterKeyframes));
    }

    public static TsProbeResult Run(string url, string user, string password, double stopAfterMs, double stopAfterKeyframes)
    {
        var result = new TsProbeResult();
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(user))
            handler.Credentials = new NetworkCredential(user, password);
        using (var client = new HttpClient(handler))
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            var clock = Stopwatch.StartNew();
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(stopAfterMs + 15000)))
                using (var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = "HTTP " + (int)response.StatusCode;
                        return result;
                    }
                    using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    {
                        var buf = new byte[64 * 1024];
                        var carry = new byte[188];
                        int carryLen = 0;
                        int pmtPid = -1, videoPid = -1;
                        while (clock.Elapsed.TotalMilliseconds < stopAfterMs
                               && (stopAfterKeyframes <= 0 || result.KeyframeArrivalMs.Count < stopAfterKeyframes))
                        {
                            int n;
                            try { n = stream.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult(); }
                            catch (OperationCanceledException) { break; }
                            if (n <= 0) break;
                            double now = clock.Elapsed.TotalMilliseconds;
                            if (result.FirstByteMs < 0) result.FirstByteMs = now;
                            result.Bytes += n;

                            // Re-frame on 188-byte packets across read boundaries.
                            int pos = 0;
                            while (pos < n)
                            {
                                if (carryLen > 0)
                                {
                                    int take = Math.Min(188 - carryLen, n - pos);
                                    Buffer.BlockCopy(buf, pos, carry, carryLen, take);
                                    carryLen += take; pos += take;
                                    if (carryLen < 188) break;
                                    Packet(carry, 0, now, result, ref pmtPid, ref videoPid);
                                    carryLen = 0;
                                    continue;
                                }
                                if (buf[pos] != 0x47) { pos++; continue; }
                                if (n - pos < 188)
                                {
                                    Buffer.BlockCopy(buf, pos, carry, 0, n - pos);
                                    carryLen = n - pos; pos = n;
                                    break;
                                }
                                Packet(buf, pos, now, result, ref pmtPid, ref videoPid);
                                pos += 188;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { if (result.Error.Length == 0) result.Error = ex.GetType().Name + ": " + ex.Message; }
            result.ElapsedMs = clock.Elapsed.TotalMilliseconds;
        }
        return result;
    }

    static void Packet(byte[] b, int o, double now, TsProbeResult r, ref int pmtPid, ref int videoPid)
    {
        int pid = ((b[o + 1] & 0x1F) << 8) | b[o + 2];
        bool pusi = (b[o + 1] & 0x40) != 0;
        int afc = (b[o + 3] >> 4) & 0x3;
        int payload = o + 4;
        bool randomAccess = false;
        if ((afc & 0x2) != 0)
        {
            int afLen = b[o + 4];
            if (afLen > 0 && (b[o + 5] & 0x40) != 0) randomAccess = true;
            payload = o + 5 + afLen;
        }
        if ((afc & 0x1) == 0 || payload >= o + 188) return;

        if (pid == 0 && pusi && pmtPid < 0)
        {
            int p = payload + 1 + b[payload]; // pointer_field
            if (p + 12 < o + 188 && b[p] == 0)
            {
                int sectionLen = ((b[p + 1] & 0x0F) << 8) | b[p + 2];
                int entries = (sectionLen - 9) / 4;
                for (int i = 0; i < entries; i++)
                {
                    int q = p + 8 + 4 * i;
                    if (q + 3 >= o + 188) break;
                    int program = (b[q] << 8) | b[q + 1];
                    if (program != 0) { pmtPid = ((b[q + 2] & 0x1F) << 8) | b[q + 3]; break; }
                }
            }
            return;
        }
        if (pmtPid >= 0 && pid == pmtPid && pusi && videoPid < 0)
        {
            int p = payload + 1 + b[payload];
            if (p + 12 < o + 188 && b[p] == 2)
            {
                int sectionLen = ((b[p + 1] & 0x0F) << 8) | b[p + 2];
                int progInfo = ((b[p + 10] & 0x0F) << 8) | b[p + 11];
                int q = p + 12 + progInfo;
                int end = Math.Min(p + 3 + sectionLen - 4, o + 188);
                while (q + 4 < end)
                {
                    int streamType = b[q];
                    int espid = ((b[q + 1] & 0x1F) << 8) | b[q + 2];
                    int esInfo = ((b[q + 3] & 0x0F) << 8) | b[q + 4];
                    if (streamType == 0x1B || streamType == 0x24) { videoPid = espid; break; }
                    q += 5 + esInfo;
                }
            }
            return;
        }
        if (videoPid >= 0 && pid == videoPid && pusi)
        {
            bool idr = randomAccess;
            if (!idr)
            {
                // PES header: 00 00 01 E0 len(2) flags(2) hdrLen(1) ..., then the ES bytes; look for an IDR slice NAL.
                int end = o + 188;
                for (int i = payload; i + 4 < end; i++)
                {
                    if (b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 1 && (b[i + 3] & 0x1F) == 5) { idr = true; break; }
                }
            }
            if (!idr) return;
            if (r.FirstKeyframeMs < 0) r.FirstKeyframeMs = now;
            r.KeyframeArrivalMs.Add(now);
            // PTS (90 kHz) when the PES header carries one.
            if (b[payload] == 0 && b[payload + 1] == 0 && b[payload + 2] == 1 && (b[payload + 7] & 0x80) != 0 && payload + 14 < o + 188)
            {
                int t = payload + 9;
                long pts = ((long)(b[t] & 0x0E) << 29) | ((long)b[t + 1] << 22) | ((long)(b[t + 2] & 0xFE) << 14)
                         | ((long)b[t + 3] << 7) | ((long)(b[t + 4] & 0xFE) >> 1);
                r.KeyframePtsMs.Add(pts / 90.0);
            }
        }
    }
}
'@

function Get-Stats([double[]]$values) {
    if (-not $values -or $values.Count -eq 0) { return $null }
    $sorted = $values | Sort-Object
    $count = $sorted.Count
    $p95Index = [Math]::Min($count - 1, [int][Math]::Ceiling(0.95 * $count) - 1)
    [pscustomobject]@{
        Count  = $count
        Min    = [Math]::Round($sorted[0], 0)
        Median = [Math]::Round($sorted[[int][Math]::Floor(($count - 1) / 2)], 0)
        P95    = [Math]::Round($sorted[$p95Index], 0)
        Max    = [Math]::Round($sorted[$count - 1], 0)
    }
}

function Get-Spacing([System.Collections.Generic.List[double]]$series) {
    $gaps = New-Object System.Collections.Generic.List[double]
    for ($i = 1; $i -lt $series.Count; $i++) {
        $gap = $series[$i] - $series[$i - 1]
        if ($gap -gt 0) { $gaps.Add($gap) }
    }
    return , $gaps.ToArray()
}

function Get-TreeIds([string]$name) {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name
    $roots = @($all | Where-Object { $_.Name -ieq "$name.exe" } | ForEach-Object { [int]$_.ProcessId })
    if ($roots.Count -eq 0) { return @() }
    $ids = New-Object System.Collections.Generic.HashSet[int]
    foreach ($r in $roots) { [void]$ids.Add($r) }
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($p in $all) {
            if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $grew = $true }
        }
    }
    return @($ids)
}

function Get-TreeCpuSeconds([int[]]$ids) {
    $total = 0.0
    foreach ($id in $ids) {
        try { $total += (Get-Process -Id $id -ErrorAction Stop).TotalProcessorTime.TotalSeconds } catch { }
    }
    return $total
}

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm'
$cores = [Environment]::ProcessorCount
Write-Host "Measure-LiveEncoder: $Candidate / $Source, $Joins joins, steady run $DurationSeconds s, $cores logical cores"

# --- time to first picture ----------------------------------------------------------------------
$firstByte = New-Object System.Collections.Generic.List[double]
$firstKey = New-Object System.Collections.Generic.List[double]
for ($i = 1; $i -le $Joins; $i++) {
    $join = [TsProbe]::Run($Url, $User, $Password, 12000, 1)
    if ($join.Error -and $join.FirstKeyframeMs -lt 0) {
        Write-Host ("  join {0}: {1}" -f $i, $join.Error)
    }
    else {
        Write-Host ("  join {0}: first byte {1:N0} ms, first keyframe {2:N0} ms" -f $i, $join.FirstByteMs, $join.FirstKeyframeMs)
        if ($join.FirstByteMs -ge 0) { $firstByte.Add($join.FirstByteMs) }
        if ($join.FirstKeyframeMs -ge 0) { $firstKey.Add($join.FirstKeyframeMs) }
    }
    Start-Sleep -Seconds 1
}

# --- steady state: keyframe cadence, bitrate, CPU of the tree ----------------------------------
$cpuSamples = New-Object System.Collections.Generic.List[double]
$task = [TsProbe]::RunAsync($Url, $User, $Password, $DurationSeconds * 1000.0, 0)

$prevCpu = $null
$prevAt = $null
$treeSeen = $false
while (-not $task.IsCompleted) {
    Start-Sleep -Seconds 2
    if (-not $ProcessName) { continue }
    $ids = Get-TreeIds $ProcessName
    if ($ids.Count -eq 0) { continue }
    $treeSeen = $true
    $cpu = Get-TreeCpuSeconds $ids
    $at = [DateTime]::UtcNow
    if ($null -ne $prevCpu -and $cpu -ge $prevCpu) {
        $wall = ($at - $prevAt).TotalSeconds
        if ($wall -gt 0) { $cpuSamples.Add(100.0 * ($cpu - $prevCpu) / $wall) }   # percent of ONE core
    }
    $prevCpu = $cpu
    $prevAt = $at
}
$steady = $task.GetAwaiter().GetResult()

$keyArrival = Get-Stats (Get-Spacing $steady.KeyframeArrivalMs)
$keyPts = Get-Stats (Get-Spacing $steady.KeyframePtsMs)
$cpuStats = Get-Stats $cpuSamples.ToArray()
$bitrateKbps = if ($steady.ElapsedMs -gt 0) { [Math]::Round(($steady.Bytes * 8.0) / $steady.ElapsedMs, 0) } else { 0 }

function Format-Ms($s) { if ($s) { "median $($s.Median) / p95 $($s.P95) / max $($s.Max)" } else { 'n/a' } }
$joinByte = Get-Stats $firstByte.ToArray()
$joinKey = Get-Stats $firstKey.ToArray()
$cpuOne = if ($cpuStats) { "avg of samples: median $($cpuStats.Median) % / p95 $($cpuStats.P95) % / max $($cpuStats.Max) % of one core; of the machine: median $([Math]::Round($cpuStats.Median / $cores, 1)) %" } elseif ($ProcessName -and -not $treeSeen) { "process '$ProcessName' not found" } else { 'n/a' }

$lines = @(
    "| $stamp | $Candidate | $Source | joins: first byte $(Format-Ms $joinByte) ms; first keyframe $(Format-Ms $joinKey) ms | keyframe spacing (arrival) $(Format-Ms $keyArrival) ms; (PTS) $(Format-Ms $keyPts) ms | $bitrateKbps kbit/s | $cpuOne | latency: by hand | packaging: by hand |"
)

Write-Host ''
Write-Host "Targets for reading the row (not a verdict): first keyframe within keyframeIntervalMs (default 2000) + 1000 ms;"
Write-Host "keyframe spacing joinable at least every 2000 ms (target 1000 ms); producer-to-picture latency <= 1000 ms (by hand)."
if ($steady.Error) { Write-Host "Steady run ended early: $($steady.Error)" }
Write-Host ("Steady run: {0:N0} s, {1:N0} keyframes, {2:N1} MB" -f ($steady.ElapsedMs / 1000), $steady.KeyframeArrivalMs.Count, ($steady.Bytes / 1MB))
Write-Host ''
Write-Host '| Date | Candidate | Source | Time to first picture | Real keyframe interval | Bitrate | Producer-tree CPU | Latency | Packaging |'
Write-Host '| --- | --- | --- | --- | --- | --- | --- | --- | --- |'
$lines | ForEach-Object { Write-Host $_ }

if (-not $OutFile) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) 'fms-bench'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $OutFile = Join-Path $dir ("live-encoder-{0}-{1}-{2}.md" -f $Candidate, $Source, (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$lines | Set-Content -Path $OutFile -Encoding utf8
Write-Host ''
Write-Host "Row written to $OutFile"
