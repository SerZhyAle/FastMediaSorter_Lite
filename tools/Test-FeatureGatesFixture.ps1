<#
.SYNOPSIS
    The fixtures of tools\Test-FeatureGates.ps1: small synthetic trees that prove both directions of
    the gate rule (ticket 206 T-3; the table is 206 section 2).

.DESCRIPTION
    A check that reads the real tree can only prove the state it finds (today: every gate shut). These
    fixtures prove the rule itself, on trees built in a temporary folder and read through the check's
    -Root parameter - the real repository, its working state and its gates are never touched:

      1. every gate shut, no stray switch                      -> PASS;
      2. ExchangeFeatures.Offered = True, no evidence file     -> FAIL;
      3. the same, the evidence file names the rung and a date -> PASS;
      4. LiveBroadcastFeatures.Offered = True, evidence names
         only a date (no rung and no phase)                    -> FAIL;
      5. the same evidence naming phase B1 and a date          -> PASS;
      6. an unknown "Const Offered As Boolean = True" no gate
         of the table names                                    -> FAIL;
      7. INCLUDE_EXCHANGE twice in release.yml                 -> FAIL;
      8. release.yml missing                                   -> COULD NOT VERIFY;
      9. the shipped StreamsPlayer page switch of the $shipped
         registry on with every gate shut                      -> PASS.

    Each case runs the check in its own pwsh process and holds the exit code against the expectation;
    a case that answers anything else names the code it got.

.OUTPUTS
    CHECK-VERDICT: 0 PASS (every fixture agrees), 1 FAIL (a fixture disagreed),
    2 COULD NOT VERIFY (the check or pwsh could not be run). The last line is
    `Test-FeatureGatesFixture: <WORD> (..)`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

$check = 'Test-FeatureGatesFixture'
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $PSScriptRoot 'Test-FeatureGates.ps1'
Write-Subject $check "the gate rule of Test-FeatureGates.ps1 against its fixtures; $(Get-TreeLabel $root)"

if (-not (Test-Path -LiteralPath $target)) { Exit-Verdict $check 2 'tools\Test-FeatureGates.ps1 not found' }
$pwsh = (Get-Process -Id $PID).Path

# --- the synthetic tree ---------------------------------------------------------------------------
# One workflow file, the two gate sources of the table and nothing else: the check scans src\ for
# every "Const Offered", so a third file is how case 6 plants a stray switch.
function New-FixtureTree {
    $tree = Join-Path ([IO.Path]::GetTempPath()) ("fms-gates-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $tree '.github\workflows') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tree 'src\Exchange') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tree 'src\LiveBroadcast') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tree 'docs\specifications') | Out-Null
    return $tree
}

function Set-Workflow([string]$tree, $line) {
    if ($null -eq $line) {
        Remove-Item -LiteralPath (Join-Path $tree '.github\workflows\release.yml') -ErrorAction SilentlyContinue
        return
    }
    if ($line -is [array]) { $text = $line -join "`n" } else { $text = $line }
    Set-Content -LiteralPath (Join-Path $tree '.github\workflows\release.yml') -Value $text -Encoding UTF8
}

function Set-Offered([string]$tree, [string]$file, [bool]$on) {
    $value = if ($on) { 'True' } else { 'False' }
    Set-Content -LiteralPath (Join-Path $tree $file) -Encoding UTF8 -Value @"
#If Not NETFRAMEWORK Then
Friend NotInheritable Class Features
    Public Const Offered As Boolean = $value
End Class
#End If
"@
}

function Set-Evidence([string]$tree, [string]$name, [string]$text) {
    $dir = Join-Path $tree 'docs\specifications'
    if ($null -eq $text) {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $dir | Out-Null
        return
    }
    Set-Content -LiteralPath (Join-Path $dir $name) -Value $text -Encoding UTF8
}

$shutWorkflow = "jobs:`n  build:`n    env:`n      INCLUDE_EXCHANGE: 'false'"
$openWorkflow = $shutWorkflow -replace "'false'", "'true'"

# --- the cases -------------------------------------------------------------------------------------
# One row per fixture: how to build the tree, and the exit code the check must answer.
$cases = [System.Collections.Generic.List[object]]::new()
$cases.Add([pscustomobject]@{
    Name = 'every gate shut is a pass'
    Code = 0
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
    }
})
$cases.Add([pscustomobject]@{
    Name = 'the exchange gate open without evidence is a defect'
    Code = 1
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $true
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
    }
})
$cases.Add([pscustomobject]@{
    Name = 'the exchange gate open with rung and date passes'
    Code = 0
    Build = {
        param($tree)
        Set-Workflow $tree $openWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $true
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
        Set-Evidence $tree 'exchange-gate-evidence.md' "The owner opened the gate on 2026-10-14 at rung 2."
    }
})
$cases.Add([pscustomobject]@{
    Name = 'the live gate open with a date but no rung or phase is a defect'
    Code = 1
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $true
        Set-Evidence $tree 'live-broadcast-gate-evidence.md' "The owner opened the gate on 2026-10-14."
    }
})
$cases.Add([pscustomobject]@{
    Name = 'the live gate open with a phase and a date passes'
    Code = 0
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $true
        Set-Evidence $tree 'live-broadcast-gate-evidence.md' "The owner opened audio LAN phase B1 on 2026-10-14."
    }
})
$cases.Add([pscustomobject]@{
    Name = 'an offer switch no gate names is a defect even with every gate shut'
    Code = 1
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
        New-Item -ItemType Directory -Path (Join-Path $tree 'src\Extra') | Out-Null
        Set-Offered $tree 'src\Extra\ExtraFeature.vb' $true
    }
})
$cases.Add([pscustomobject]@{
    Name = 'the packaging gate defined twice is a defect'
    Code = 1
    Build = {
        param($tree)
        Set-Workflow $tree @($shutWorkflow, "  other:`n      INCLUDE_EXCHANGE: 'false'")
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
    }
})
$cases.Add([pscustomobject]@{
    Name = 'no workflow file cannot be verified'
    Code = 2
    Build = {
        param($tree)
        Set-Workflow $tree $null
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
    }
})
$cases.Add([pscustomobject]@{
    Name = 'a shipped switch of the registry is on with every gate shut and passes'
    Code = 0
    Build = {
        param($tree)
        Set-Workflow $tree $shutWorkflow
        Set-Offered $tree 'src\Exchange\ExchangeFeatures.vb' $false
        Set-Offered $tree 'src\LiveBroadcast\LiveBroadcastFeatures.vb' $false
        New-Item -ItemType Directory -Path (Join-Path $tree 'src\FastMediaSorterCompanion\Core') | Out-Null
        Set-Offered $tree 'src\FastMediaSorterCompanion\Core\BroadcastPage.vb' $true
    }
})

# --- the run ---------------------------------------------------------------------------------------
$made = [System.Collections.Generic.List[string]]::new()
$problems = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases) {
        $tree = New-FixtureTree
        $made.Add($tree)
        & $case.Build $tree
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $out = @(& $pwsh -NoProfile -File $target -Root $tree 2>&1 | ForEach-Object { "$_" })
            $exit = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $prevEap
        }
        if ($exit -ne $case.Code) {
            $problems.Add("'{0}': expected exit {1}, got {2} ({3})" -f $case.Name, $case.Code, $exit, ($out | Select-Object -Last 1))
        }
    }
} finally {
    foreach ($tree in $made) {
        Remove-Item -LiteralPath $tree -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($problems.Count) of $($cases.Count) fixtures disagreed"
}
Exit-Verdict $check 0 "$($cases.Count) fixtures agree: shut passes, open needs the rung and the date, a stray switch, a doubled gate line and a missing workflow are named"
