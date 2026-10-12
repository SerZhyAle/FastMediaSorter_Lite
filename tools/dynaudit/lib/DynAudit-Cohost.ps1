<#
.SYNOPSIS
    The DynAudit cooperating host (specification 209 section 4, probe `coh`): a separate
    process with its own window that pings itself and runs a fixed 20 MB disk-read workload
    for the duration of a scene. This is what property B is measured with - another program
    on the machine, judging what the product's scene does to IT.

.DESCRIPTION
    Run as a child process. It writes its summary as JSON to -OutJsonPath:
    { selfPingP50Ms, selfPingP95Ms, selfPingMaxMs, passes, passMsP50, passMsP95 }
    The control run (section 7) is the same invocation with the product absent; the scene
    report carries the control row next to the measured row, and only the degradation
    between them is ever judged.
#>
param(
    [Parameter(Mandatory = $true)][double]$Seconds,
    [Parameter(Mandatory = $true)][string]$WorkloadFile,
    [Parameter(Mandatory = $true)][string]$OutJsonPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$native = Join-Path $PSScriptRoot 'DynAudit.Native.ps1'
. $native
Use-DynAuditNative

$form = [System.Windows.Forms.Form]::new()
$form.Text = 'DynAuditCohost'
$form.Width = 240; $form.Height = 80
$form.StartPosition = 'Manual'
$form.Location = [System.Drawing.Point]::new(-32000, -32000)   # off-screen: it must exist, not distract
$form.Show()
$form.Refresh()

$buffer = [byte[]]::new(1MB)
$pingLatencies = [System.Collections.Generic.List[double]]::new()
$passLatencies = [System.Collections.Generic.List[double]]::new()
$swAll = [Diagnostics.Stopwatch]::StartNew()
$lastPass = [double]::MinValue
while ($swAll.Elapsed.TotalSeconds -lt $Seconds) {
    [void][System.Windows.Forms.Application]::DoEvents()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    [void][DynAudit.Native]::Ping($form.Handle, 1000)
    $sw.Stop()
    $pingLatencies.Add([Math]::Round($sw.Elapsed.TotalMilliseconds, 3))
    if (($swAll.Elapsed.TotalSeconds - $lastPass) -ge 1.0) {
        $lastPass = $swAll.Elapsed.TotalSeconds
        $swPass = [Diagnostics.Stopwatch]::StartNew()
        $read = [long]0
        try {
            $fs = [IO.File]::OpenRead($WorkloadFile)
            try { while (($n = $fs.Read($buffer, 0, $buffer.Length)) -gt 0) { $read += $n } } finally { $fs.Dispose() }
        } catch { }
        $swPass.Stop()
        $passLatencies.Add([Math]::Round($swPass.Elapsed.TotalMilliseconds, 3))
    }
    Start-Sleep -Milliseconds 5
}
$form.Close()

function Get-Pct([double[]]$v, [double]$p) {
    if (-not $v -or $v.Count -eq 0) { return 0 }
    $s = [double[]]($v | Sort-Object)
    $i = [Math]::Ceiling(($p / 100.0) * $s.Count) - 1
    if ($i -lt 0) { $i = 0 }
    if ($i -ge $s.Count) { $i = $s.Count - 1 }
    return [Math]::Round($s[[int]$i], 2)
}

$summary = [pscustomobject]@{
    selfPingP50Ms  = Get-Pct $pingLatencies.ToArray() 50
    selfPingP95Ms  = Get-Pct $pingLatencies.ToArray() 95
    selfPingMaxMs  = if ($pingLatencies.Count) { [Math]::Round(($pingLatencies | Measure-Object -Maximum).Maximum, 2) } else { 0 }
    selfPingN      = $pingLatencies.Count
    passes         = $passLatencies.Count
    passMsP50      = Get-Pct $passLatencies.ToArray() 50
    passMsP95      = Get-Pct $passLatencies.ToArray() 95
}
$summary | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $OutJsonPath -Encoding UTF8
exit 0
