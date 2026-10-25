<#
.SYNOPSIS
    Opt-in real captures of live-broadcast windows and the Drive settings page (specification 267).
.DESCRIPTION
    Needs an unlocked desktop. Uses isolated viewer/manager registry profiles, restores them in finally,
    and never starts capture, sign-in, a worker or a service. The output is 78 PNGs and a hash manifest.
    Existing general settings captures are preserved. This check is hand-run, never a default test step.
#>
[CmdletBinding()]
param([string]$OutDir = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib/Verdict.ps1')
trap { Write-Host $_.Exception.Message; Exit-Verdict 'Capture-ContextHelp' 2 'capture could not complete' }
if (!$OutDir) { $OutDir = Join-Path $root 'docs/help' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
Write-Subject 'Capture-ContextHelp' "real live-broadcast and Drive windows in 13 locales, light/dark; $OutDir"
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-Tests.ps1') -Build modern
if ($LASTEXITCODE -ne 0) { Exit-Verdict 'Capture-ContextHelp' $LASTEXITCODE 'viewer build did not pass' }
& dotnet run --project (Join-Path $root 'tests/Integration/ContextHelpCaptures/ContextHelpCaptures.csproj') -- $OutDir
$captureCode = $LASTEXITCODE
if ($captureCode -notin @(0, 1, 2)) { $captureCode = 2 }
if ($captureCode -ne 0) { Exit-Verdict 'Capture-ContextHelp' $captureCode 'see capture diagnostics above; no complete capture set accepted' }
Exit-Verdict 'Capture-ContextHelp' 0 '78 real captures and their hash manifest; settings overview captures preserved'
