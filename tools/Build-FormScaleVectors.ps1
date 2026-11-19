<#
.SYNOPSIS
    Regenerates the form scale vectors (specification L0361 section 6.3) from the formulas of rules S1-S4.

.DESCRIPTION
    Writes tests/Lite.Tests/TestData/form-scale-vectors/form-scale-vectors.json. The file is a render target:
    never edit it by hand, run this script. The script states the rules in their language-neutral form, apart
    from the VB resolver (src/Chrome/FormScale.vb) that FormScaleTests replays against the file, so a resolver
    that is self-consistent but wrong fails.

    Font rows: {kind, logical, M, c, S, D, expectAssigned, expectOnScreenAtD}. expectOnScreenAtD is the
    independent statement of the rule, logical x M x D / 96, never derived from expectAssigned.
    Px rows: {logical, D, M, expectPx} with expectPx = floor(logical x max(1, D/96) x M + 0.5).

    Exit codes: 0 written, 1 failed.
#>
[CmdletBinding()]
param(
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path -Parent $PSScriptRoot
    if (-not $OutFile) {
        $OutFile = Join-Path $root 'tests\Lite.Tests\TestData\form-scale-vectors\form-scale-vectors.json'
    }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    function N([double]$v) { [Math]::Round($v, 6).ToString('R', $inv) }

    $systems = 96, 120, 144, 168, 192
    $windows = 96, 120, 144, 168, 192, 216, 288
    $multipliers = 0.75, 1.0, 1.25, 1.5, 2.0, 2.5
    $logicals = 9, 12

    $fontRows = [Collections.Generic.List[string]]::new()
    foreach ($s in $systems) { foreach ($d in $windows) { foreach ($m in $multipliers) { foreach ($l in $logicals) {
        foreach ($kind in 'pixel', 'point') { foreach ($basis in 'S', 'D') {
            $c = if ($basis -eq 'S') { $s } else { $d }
            $k = [Math]::Max(1.0, $c / 96.0)
            $assigned = if ($kind -eq 'pixel') { $l * $m * $k } else { 0.75 * $l * $m * $c / $s }
            $onScreen = $l * $m * $d / 96.0
            $fontRows.Add(('{{"kind":"{0}","logical":{1},"M":{2},"c":{3},"S":{4},"D":{5},"expectAssigned":{6},"expectOnScreenAtD":{7}}}' -f
                $kind, $l, (N $m), $c, $s, $d, (N $assigned), (N $onScreen)))
        } }
    } } } }

    $pxRows = [Collections.Generic.List[string]]::new()
    foreach ($l in 1, 8, 12, 780) { foreach ($d in $windows) { foreach ($m in $multipliers) {
        $px = [Math]::Floor($l * [Math]::Max(1.0, $d / 96.0) * $m + 0.5)
        $pxRows.Add(('{{"logical":{0},"D":{1},"M":{2},"expectPx":{3}}}' -f $l, $d, (N $m), [int]$px))
    } } }

    $nl = "`n"
    $text = '{' + $nl +
        '  "generator": "tools/Build-FormScaleVectors.ps1",' + $nl +
        '  "rules": "WINDOWS-SCALE 0.1 rules S1-S4 (specification L0361 section 5)",' + $nl +
        '  "font": [' + $nl + '    ' + ($fontRows -join (',' + $nl + '    ')) + $nl + '  ],' + $nl +
        '  "px": [' + $nl + '    ' + ($pxRows -join (',' + $nl + '    ')) + $nl + '  ]' + $nl +
        '}' + $nl
    $dir = Split-Path -Parent $OutFile
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [IO.File]::WriteAllText($OutFile, $text, [Text.UTF8Encoding]::new($false))
    Write-Output ("Build-FormScaleVectors: wrote {0} font rows and {1} px rows to {2}" -f $fontRows.Count, $pxRows.Count, $OutFile)
    exit 0
}
catch {
    Write-Error ("Build-FormScaleVectors: FAIL - " + $_.Exception.Message) -ErrorAction Continue
    exit 1
}
