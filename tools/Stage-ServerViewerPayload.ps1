<#
.SYNOPSIS
    Stages the OPTIONAL viewer component of the Server edition installer (ticket 132, Q132-1).

.DESCRIPTION
    The Server edition leads with the server role; the media viewer rides along as an unchecked
    component. This script lays the viewer's files down under <Destination>\viewer\, where
    FastMediaSorterServer.iss looks for them (the component exists only when
    <Destination>\viewer\FastMediaSorter_LITE.exe does).

    The payload is the User installer's "compact" shape taken from the SAME version's User
    stage: both viewer exes with the libraries they load, and nothing optional - no VLC codecs
    (libvlc\), no OCR models (tessdata\, tessdata-best\), no PDFium, and nothing of the share
    side (the Share Manager exe, companion\, filedo\ are staged by the Server edition itself).
    Codecs and models download on first use, exactly as they do after a Compact install.

    One stage, one version: the viewer is copied from the User stage of the build that is
    packaging, never from a folder an earlier build left behind - the one-stamp rule of
    build.ps1 ("a Server package left behind by an earlier build is exactly how a stale worker
    gets handed to a server") applies to the viewer in the Server package too.

.PARAMETER UserStage
    The User edition's staged tree (stage\FastMediaSorter-<version>-windows-x64).

.PARAMETER Destination
    The Server edition's stage; the files land in <Destination>\viewer\.

.PARAMETER Require
    Fail when the User stage has no mainline viewer exe. The packagers that SHIP a release pass
    it; a local convenience build leaves it off and gets a Server installer with no viewer
    component (and a warning), never a half-viewer.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UserStage,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$Require
)

$ErrorActionPreference = 'Stop'

$mainline = 'FastMediaSorter_LITE.exe'
$target = Join-Path $Destination 'viewer'

if (-not (Test-Path -LiteralPath (Join-Path $UserStage $mainline))) {
    $message = "No $mainline in the User stage '$UserStage' - the Server installer cannot offer the optional viewer."
    if ($Require) { throw $message }
    Write-Warning ($message + ' Built WITHOUT the viewer component; run build.ps1 (or Build-Installer.ps1) first for the full Server package.')
    return
}

New-Item -ItemType Directory -Path $target -Force | Out-Null

# Robocopy: exit codes 0-7 are success (1 = files copied), 8 and above are failures.
$excludeDirs = @('libvlc', 'tessdata', 'tessdata-best', 'companion', 'filedo')
$excludeFiles = @('FastMediaSorterCompanion.exe', 'pdfium.dll', '*.log', '*.previous-*.exe')
& robocopy $UserStage $target /E /NFL /NDL /NJH /NJS /NP /XD @excludeDirs /XF @excludeFiles | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed staging the viewer payload (exit code $LASTEXITCODE)." }
$global:LASTEXITCODE = 0

if (-not (Test-Path -LiteralPath (Join-Path $target $mainline))) {
    throw "The viewer payload was staged without $mainline - refusing to hand the installer a half-viewer."
}
$sizeMb = [Math]::Round(((Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Staged the optional viewer -> viewer\ ($sizeMb MB)"
