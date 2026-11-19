# Release ledger hook helpers for build and release scripts (RELEASE-LEDGER 0.1, ticket L0344).
# Dot-source from build.ps1, tools/Build-*.ps1, publishing/msix/build-msix.ps1, exchange-server/build.ps1,
# tools/Release.ps1 and tools/Set-ReleaseManifest.ps1:
#     . (Join-Path $repoRoot 'tools\lib\ReleaseLedgerHook.ps1')
#
# Non-fatal by contract (RELEASE_AND_DISTRIBUTION section 10): a ledger failure or missing harness is
# reported as a warning and never fails or aborts the caller's build or release step.

function Invoke-ReleaseLedgerCall {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$ActionLabel
    )
    $harness = Join-Path $RepoRoot 'tools\harness.ps1'
    if (-not (Test-Path -LiteralPath $harness -PathType Leaf)) {
        Write-Warning "release-ledger: $ActionLabel skipped (tools/harness.ps1 not found)."
        return
    }
    $pwsh = (Get-Process -Id $PID).Path
    if (-not $pwsh -or -not (Test-Path -LiteralPath $pwsh -PathType Leaf)) { $pwsh = 'pwsh' }
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $pwsh -NoProfile -File $harness @Arguments 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        $global:LASTEXITCODE = 0
        if ($code -ne 0) {
            $detail = ($output | Where-Object { $_ } | Select-Object -First 3) -join ' | '
            Write-Warning "release-ledger: $ActionLabel exited $code$(if ($detail) { ": $detail" })"
        } else {
            foreach ($line in $output) {
                if ($line) { Write-Host $line -ForegroundColor DarkGray }
            }
        }
    } catch {
        $global:LASTEXITCODE = 0
        Write-Warning "release-ledger: $ActionLabel failed: $($_.Exception.Message)"
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

function Invoke-ReleaseLedgerBuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][ValidateSet('release', 'test', 'debug')][string]$Kind,
        [Parameter(Mandatory)][string]$Version,
        [string]$Flavor = '',
        [string]$Artifact = '',
        [string]$Note = ''
    )
    $a = [System.Collections.Generic.List[string]]::new()
    $a.Add('releases/release-ledger')
    $a.Add('build')
    $a.Add('-Package'); $a.Add($Package)
    $a.Add('-Kind'); $a.Add($Kind)
    $a.Add('-Version'); $a.Add($Version)
    if ($Flavor)   { $a.Add('-Flavor'); $a.Add($Flavor) }
    if ($Artifact) { $a.Add('-Artifact'); $a.Add($Artifact) }
    if ($Note)     { $a.Add('-Note'); $a.Add($Note) }
    Invoke-ReleaseLedgerCall -RepoRoot $RepoRoot -Arguments $a.ToArray() -ActionLabel "build $Package/$Flavor $Kind $Version"
}

function Invoke-ReleaseLedgerPublish {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][ValidateSet('pending', 'submitted', 'live', 'blocked', 'rejected', 'withdrawn', 'n/a')][string]$State,
        [string]$Ref = '',
        [string]$Url = '',
        [string]$Note = '',
        [string]$Source = ''
    )
    $a = [System.Collections.Generic.List[string]]::new()
    $a.Add('releases/release-ledger')
    $a.Add('publish')
    $a.Add('-Key'); $a.Add($Key)
    $a.Add('-Version'); $a.Add($Version)
    $a.Add('-State'); $a.Add($State)
    if ($Ref)    { $a.Add('-Ref'); $a.Add($Ref) }
    if ($Url)    { $a.Add('-Url'); $a.Add($Url) }
    if ($Note)   { $a.Add('-Note'); $a.Add($Note) }
    if ($Source) { $a.Add('-Source'); $a.Add($Source) }
    Invoke-ReleaseLedgerCall -RepoRoot $RepoRoot -Arguments $a.ToArray() -ActionLabel "publish $Key $Version $State"
}
