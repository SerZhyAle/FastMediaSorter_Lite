<#
.SYNOPSIS
    Opt-in real captures of the context-help windows of the viewer and the Share Manager (specification 267).
.DESCRIPTION
    Needs an unlocked desktop. Uses isolated viewer/manager registry profiles, restores them in finally,
    and never starts capture, sign-in, a worker, a listener or a service: every window is shown with synthetic
    sample data and closed by disposal. The output is one PNG per topic, locale and theme plus a hash manifest
    (docs/help/context-help-capture-evidence.json); its hand-kept "declared" list (topics captured by the settings
    test, topics that cannot be captured) is preserved. This check is hand-run, never a default test step.
    -Topics limits the run to some topics; their records replace only their own in the manifest.
#>
[CmdletBinding()]
param([string]$OutDir = '', [string[]]$Topics = @())
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib/Verdict.ps1')
trap { Write-Host $_.Exception.Message; Exit-Verdict 'Capture-ContextHelp' 2 'capture could not complete' }
if (!$OutDir) { $OutDir = Join-Path $root 'docs/help' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
Write-Subject 'Capture-ContextHelp' "real viewer and Share Manager windows in 13 locales, light/dark; $OutDir"
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-Tests.ps1') -Build modern
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'Capture-ContextHelp' $LASTEXITCODE 'viewer build did not pass' }
$harnessArgs = @($OutDir)
if ($Topics.Count -gt 0) { $harnessArgs += @('--topics', ($Topics -join ',')) }
& dotnet run --project (Join-Path $root 'tests/Integration/ContextHelpCaptures/ContextHelpCaptures.csproj') -- @harnessArgs
$captureCode = $LASTEXITCODE
if ($captureCode -notin @(0, 1, 2)) { $captureCode = 2 }
if ($captureCode -ne 0) { Exit-Verdict 'Capture-ContextHelp' $captureCode 'see capture diagnostics above; no complete capture set accepted' }
$evidence = Get-Content -LiteralPath (Join-Path $OutDir 'context-help-capture-evidence.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$count = @($evidence.screenshots).Count
Exit-Verdict 'Capture-ContextHelp' 0 "$count real captures in the hash manifest; $(@($evidence.declared).Count) topics declared there"
