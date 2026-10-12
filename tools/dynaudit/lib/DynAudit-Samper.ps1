<#
.SYNOPSIS
    The samp sampler as a child process (specification 209, probe `samp`): 1 Hz over the
    product's pid for the whole scene, writing the CSV series the scene is judged from.
    Runs beside the interactive engine so the series covers the scene, not just its ends.
#>
param(
    [Parameter(Mandatory = $true)][int]$ProcId,
    [Parameter(Mandatory = $true)][double]$Seconds,
    [Parameter(Mandatory = $true)][string]$CsvPath,
    [int[]]$AlsoProcIds = @()
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DynAudit.Probes.ps1')
Invoke-SampProbe -ProcId $ProcId -Seconds $Seconds -Hz 1 -CsvPath $CsvPath -AlsoProcIds $AlsoProcIds
exit 0
