<#
.SYNOPSIS
    The scene matrix of the form scale model (specification L0361 section 6.4, ticket L0365) on the real
    Settings window of the modern viewer, with the font audit of rule S9 at every stop.

.DESCRIPTION
    Builds tests/Integration/SettingsLayoutScenes (the real Table_Form, in process, on a scratch settings hive,
    shown at opacity 0) and runs its --font-matrix mode once per interface scale U (-InterfaceScales, default
    100 and 150). Each run visits every page where the window opened, then drags the window across the monitors
    in the order 100 -> 175 -> 225 -> 100 % (a real WM_DPICHANGED at each stop), visits every page again and asks
    the window for its own audit: every control's effective font against its recipe at the current dpi. One line
    per mismatch is printed.

    Coverage is the baseline plus the dimensions this desk offers, never the Cartesian product (UI-SIZING
    section 4), and every dimension that was not measured is named on the verdict line:
      - D: the monitors present; a drag stop whose dpi no monitor has is omitted.
      - U: the -InterfaceScales values, one process each (the window reads U once, at construction).
      - T: the Windows Text size is READ, never changed; the run measures the current factor only. Run again
        after changing Settings > Accessibility > Text size to cover 125 %.
      - Resolution classes (910 x 512, 1280 x 672, 1536 x 824, 4:3) belong to ticket L0364 and are not run here.

    The owner's registry values are neither read nor written. It needs an unlocked desktop and the dotnet SDK,
    restores and compiles the viewer, and is far above 5 s: a hand-run check, never part of a default run.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 = every measured stop without a mismatch; 1 = a mismatch; 2 = could not
    verify (no dotnet, the harness did not build, nothing measured, no font recipe recorded). The last line is
    `FormScaleSceneMatrix: <WORD> (..)`.
#>
[CmdletBinding()]
param(
    [int[]]$InterfaceScales = @(100, 150),
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-form-scale-matrix')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'FormScaleSceneMatrix' 2 'the script itself failed'
}

Write-Subject 'FormScaleSceneMatrix' "Settings window font audit, U = $($InterfaceScales -join '/') %; $(Get-TreeLabel $repoRoot)"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Exit-Verdict 'FormScaleSceneMatrix' 2 'no dotnet SDK on PATH' }
$project = Join-Path $repoRoot 'tests\Integration\SettingsLayoutScenes\SettingsLayoutScenes.csproj'
New-Item -ItemType Directory -Force $WorkDir | Out-Null
$out = Join-Path $WorkDir 'probe'
Write-Host 'Building the in-process harness..'
& $dotnet.Source build $project -c Debug -o $out --nologo -v:q 2>&1 | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "  $_" }
$probe = Join-Path $out 'SettingsLayoutScenes.exe'
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $probe)) { Exit-Verdict 'FormScaleSceneMatrix' 2 'the harness did not build' }

$codes = New-Object System.Collections.Generic.List[int]
$omitted = New-Object System.Collections.Generic.List[string]
$measured = 0
$textFactor = $null
foreach ($u in $InterfaceScales) {
    $json = Join-Path $WorkDir "font-matrix-u$u.json"
    Write-Host "U = $u %:"
    & $probe $json --font-matrix --interface-scale $u 2>&1 | ForEach-Object { Write-Host "  $_" }
    $code = $LASTEXITCODE
    $codes.Add($code)
    if (-not (Test-Path -LiteralPath $json)) { $omitted.Add("U $u %: no result file"); continue }
    $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
    $textFactor = $doc.textFactor
    $measured += @($doc.stops).Count
    foreach ($o in @($doc.omitted)) { $omitted.Add("U $u %: $o") }
}
$omitted.Add("T: only the current Text size ($textFactor) measured")
$omitted.Add('resolution classes: ticket L0364')
foreach ($o in $omitted) { Write-Host "  omitted - $o" }
$detail = "$measured stop(s) measured; omitted: $($omitted -join '; ')"
Exit-Verdict 'FormScaleSceneMatrix' (Get-AggregateCode $codes.ToArray()) $detail
