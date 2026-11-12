<# Summarize isolated opt-in per-picture panel colour timing lines.
   Exit 0: statistics produced or self-test passed; 1: invalid or insufficient input. #>
[CmdletBinding(DefaultParameterSetName = 'Log')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Log')]
    [string] $LogPath,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

function Get-PanelColourStats([string[]] $Lines) {
    $values = [System.Collections.Generic.List[double]]::new()
    $layouts = 0
    foreach ($line in $Lines) {
        if ($line -cnotmatch 'Panel colour timing:') { continue }
        if ($line -cnotmatch 'Panel colour timing: ([0-9]+(?:\.[0-9]+)?) ms \| layout=(true|false)\s*$') {
            throw 'Malformed panel colour timing line.'
        }
        $value = 0.0
        if (-not [double]::TryParse($Matches[1], [System.Globalization.NumberStyles]::AllowDecimalPoint,
                [System.Globalization.CultureInfo]::InvariantCulture, [ref] $value) -or
            [double]::IsInfinity($value)) {
            throw 'Invalid panel colour timing value.'
        }
        $values.Add($value)
        if ($Matches[2] -ceq 'true') { $layouts++ }
    }
    if ($values.Count -lt 100) { throw "Insufficient panel colour timing samples: $($values.Count); need at least 100." }
    $values.Sort()
    return [pscustomobject]@{
        Count = $values.Count
        P50 = $values[[int][math]::Ceiling($values.Count * 0.50) - 1]
        P95 = $values[[int][math]::Ceiling($values.Count * 0.95) - 1]
        Layouts = $layouts
    }
}

try {
    if ($SelfTest) {
        $samples = @(1..100 | ForEach-Object { "Panel colour timing: $_ ms | layout=$(if ($_ % 2) { 'true' } else { 'false' })" })
        $result = Get-PanelColourStats $samples
        if ($result.Count -ne 100 -or $result.P50 -ne 50 -or $result.P95 -ne 95 -or $result.Layouts -ne 50) {
            throw 'Statistics self-test returned incorrect values.'
        }
        $rejected = 0
        try { Get-PanelColourStats @($samples[0..98]) | Out-Null } catch { $rejected++ }
        try { Get-PanelColourStats ($samples + 'Panel colour timing: invalid') | Out-Null } catch { $rejected++ }
        try { Get-PanelColourStats ($samples + 'Panel colour timing: 1 ms | layout=True') | Out-Null } catch { $rejected++ }
        if ($rejected -ne 3) { throw 'Statistics self-test failed to reject bad input.' }
        Write-Output 'PanelColourTimingStats self-test: PASS (100 samples, p50=50, p95=95, layouts=50; bad input rejected)'
    } else {
        $result = Get-PanelColourStats @(Get-Content -LiteralPath $LogPath)
        $culture = [System.Globalization.CultureInfo]::InvariantCulture
        Write-Output ('Panel colour timing: samples={0} | p50={1} ms | p95={2} ms | layouts={3}' -f
            $result.Count, $result.P50.ToString($culture), $result.P95.ToString($culture), $result.Layouts)
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
