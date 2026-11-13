<#
.SYNOPSIS
    Starts one fresh Share Manager process that holds a Google Drive OAuth client pair for a local diagnostic run
    (specification L0306 section 4.1). Holds no value itself and writes none anywhere.

.DESCRIPTION
    ExchangeDriveClientConfig.Resolve reads FMS_DRIVE_CLIENT_ID and FMS_DRIVE_CLIENT_SECRET from the process
    environment before the build's metadata. Setting them in a shell by hand fails in three quiet ways, which
    this script closes:

      - a Share Manager that already runs (often only in the tray) takes the new launch over its single-instance
        mutex, so the new process exits and the running one never sees the pair - refused here until it is gone;
      - a value typed on a command line lands in the PSReadLine history file - here both are read with
        Read-Host -AsSecureString, never echoed, never on a command line;
      - one variable missing lets the other fall back to the build's metadata, mixing two clients - here both
        are always set together, and the id must have the shape of a Google OAuth client id.

    The pair is placed in the environment of the started process ONLY: this shell's own environment is not
    changed, and the pair disappears when that process exits. A later launch from the Start menu or the tray
    autostart is unconfigured again unless the build itself carries the metadata.

    Not a check: it prints no verdict. The artifact's own state is tools\Test-DriveClientArtifact.ps1's.

.PARAMETER ExePath
    The Share Manager to start. Default: bin\Release\FastMediaSorterCompanion.exe of this tree.

.PARAMETER UseShellEnvironment
    Take both values from this shell's FMS_DRIVE_CLIENT_ID / FMS_DRIVE_CLIENT_SECRET instead of asking.

.PARAMETER ClientJson
    Take both values from the client JSON Google Cloud Console downloads for a Desktop app client (its
    "installed" section), kept outside every repository. Nothing of it is printed; a Web application client's
    JSON is refused.

.PARAMETER DryRun
    Check the exe, the given pair (with -ClientJson or -UseShellEnvironment) and that no Share Manager runs,
    then stop before asking for anything.

.NOTES
    Exit codes: 0 started (or -DryRun found nothing in the way), 1 refused (a running Share Manager, a missing
    exe, an empty or misshapen value), 2 the process could not be started.

.EXAMPLE
    pwsh -NoProfile -File tools\Start-ShareManagerWithDriveClient.ps1
#>
[CmdletBinding()]
param(
    [string]$ExePath = '',
    [switch]$UseShellEnvironment,
    [string]$ClientJson = '',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ExePath) { $ExePath = Join-Path $repo 'bin\Release\FastMediaSorterCompanion.exe' }
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    Write-Host "Share Manager not found: $ExePath" -ForegroundColor Red
    exit 1
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path

$idShape = '^\d+-[A-Za-z0-9_]+\.apps\.googleusercontent\.com$'
# Refuses an empty, partial or swapped pair before anything is started.
function Assert-Pair([string]$Id, [string]$Secret) {
    if (-not $Id -or -not $Secret) {
        Write-Host 'Both the client ID and the client secret are needed; nothing started.' -ForegroundColor Red
        exit 1
    }
    if ($Id -notmatch $idShape) {
        $why = if ($Secret -match $idShape) { 'the two values look swapped' } else { 'the client ID is not shaped like <digits>-<token>.apps.googleusercontent.com' }
        Write-Host "Refused: $why; nothing started." -ForegroundColor Red
        exit 1
    }
}
function Get-Trimmed([string]$Value) { if ($Value) { $Value.Trim() } else { '' } }

# A given pair is judged first, so a wrong one is reported even while a Share Manager runs.
if ($ClientJson) {
    if (-not (Test-Path -LiteralPath $ClientJson -PathType Leaf)) {
        Write-Host "Client JSON not found: $ClientJson" -ForegroundColor Red
        exit 1
    }
    try { $json = Get-Content -LiteralPath $ClientJson -Raw | ConvertFrom-Json } catch { $json = $null }
    if (-not $json -or $json.PSObject.Properties.Name -notcontains 'installed') {
        Write-Host 'Refused: the JSON is not a Desktop app client (no "installed" section); nothing started.' -ForegroundColor Red
        exit 1
    }
    $id = Get-Trimmed ([string]$json.installed.client_id)
    $secret = Get-Trimmed ([string]$json.installed.client_secret)
    $json = $null
    Assert-Pair $id $secret
} elseif ($UseShellEnvironment) {
    $id = Get-Trimmed ([Environment]::GetEnvironmentVariable('FMS_DRIVE_CLIENT_ID'))
    $secret = Get-Trimmed ([Environment]::GetEnvironmentVariable('FMS_DRIVE_CLIENT_SECRET'))
    Assert-Pair $id $secret
}

$running = @(Get-Process -Name 'FastMediaSorterCompanion' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "A Share Manager runs (process id $(($running | ForEach-Object Id) -join ', ')). Quit it from its tray menu first:" -ForegroundColor Red
    Write-Host '  a new launch would hand over to it, and it never sees the client pair.' -ForegroundColor Red
    exit 1
}
if ($DryRun) {
    Write-Host "Ready: $ExePath, no Share Manager running. Nothing asked, nothing started (-DryRun)."
    exit 0
}

if (-not $UseShellEnvironment -and -not $ClientJson) {
    $id = Get-Trimmed ([Net.NetworkCredential]::new('', (Read-Host -Prompt 'Client ID (not shown)' -AsSecureString)).Password)
    $secret = Get-Trimmed ([Net.NetworkCredential]::new('', (Read-Host -Prompt 'Client secret (not shown)' -AsSecureString)).Password)
    Assert-Pair $id $secret
}

$psi = [Diagnostics.ProcessStartInfo]::new($ExePath)
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path -Parent $ExePath
$psi.Environment['FMS_DRIVE_CLIENT_ID'] = $id
$psi.Environment['FMS_DRIVE_CLIENT_SECRET'] = $secret
try {
    $p = [Diagnostics.Process]::Start($psi)
} catch {
    Write-Host "The Share Manager could not be started: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
} finally {
    $id = $null; $secret = $null
    $psi.Environment.Remove('FMS_DRIVE_CLIENT_ID') | Out-Null
    $psi.Environment.Remove('FMS_DRIVE_CLIENT_SECRET') | Out-Null
}
Write-Host "Share Manager started (process id $($p.Id)) with the client pair in its own environment only."
Write-Host 'Open Settings > Google Drive: Sign in is available. The pair is gone when this process exits.'
exit 0
