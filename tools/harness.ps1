#requires -Version 7.0
<#
.SYNOPSIS
    Forwards one call to the canon-shipped harness (the sza plugin's tools/harness) for this repository.

.DESCRIPTION
    The harness is adopted, not copied: this file locates the installed plugin and hands the call over.
    What this repository configures lives in .sza-profile.json at the root and nowhere else. Written by
    the task-flow scaffold (tools/harness/scaffold/init-task-flow.ps1) from its forwarder template, and
    always resolved against the newest installed plugin, so a plugin update never leaves it pointing at a
    removed version.

        pwsh -NoProfile -File tools/harness.ps1 spec_catalog/validate
        pwsh -NoProfile -File tools/harness.ps1 spec_catalog/insert -Name "x" -Slug x
        pwsh -NoProfile -File tools/harness.ps1 spec_catalog/release-queue -List

    The first argument is the harness-relative script path, with or without '.ps1'. Everything after it is
    passed through untouched. The script declares no parameters on purpose: a declared name would swallow
    a prefix of the forwarded script's own parameters.

    Where the harness is looked up, first hit wins:
      1. $env:SZA_HARNESS_ROOT (a harness directory, for a test or a local checkout);
      2. the installed plugin versions under the user's Claude plugin cache, newest first, compared as
         versions and not as strings (2026.1002.10 sorts above 2026.1002.9).

Exit codes: the forwarded script's own; 2 when the call shape is wrong or the harness or the named script
cannot be found.
#>

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$args[0])) {
    Write-Host 'usage: tools/harness.ps1 <cluster>/<script> [arguments]   e.g. spec_catalog/validate'
    exit 2
}

$scriptPath = ([string]$args[0]).Trim().Replace('\', '/')
if ($scriptPath.EndsWith('.ps1')) { $scriptPath = $scriptPath.Substring(0, $scriptPath.Length - 4) }
$passThrough = @($args | Select-Object -Skip 1)

function Get-HarnessRoots {
    if ($env:SZA_HARNESS_ROOT) { $env:SZA_HARNESS_ROOT }

    $home_ = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
    if (-not $home_) { return }
    $cache = Join-Path $home_ '.claude/plugins/cache/sza-unified-rules/sza'
    if (-not (Test-Path -LiteralPath $cache)) { return }

    Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            $v = $null
            $isVersion = [version]::TryParse($_.Name, [ref]$v)
            [pscustomobject]@{ Dir = $_.FullName; IsVersion = $isVersion; Version = $v }
        } |
        Sort-Object -Property @{ Expression = 'IsVersion'; Descending = $true },
                              @{ Expression = 'Version'; Descending = $true } |
        ForEach-Object { Join-Path $_.Dir 'tools/harness' }
}

$target = $null
foreach ($root in Get-HarnessRoots) {
    $candidate = Join-Path $root "$scriptPath.ps1"
    if (Test-Path -LiteralPath $candidate) { $target = $candidate; break }
}
if (-not $target) {
    Write-Host "harness: '$scriptPath.ps1' not found. Install the sza plugin (sza@sza-unified-rules) or set SZA_HARNESS_ROOT."
    exit 2
}

$env:SZA_PROJECT_ROOT = $repoRoot
& pwsh -NoProfile -File $target @passThrough
exit $LASTEXITCODE
