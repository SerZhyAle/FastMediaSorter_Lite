<#
.SYNOPSIS
    Deletes old build output, orphaned binaries and temporary files so they do
    not accumulate between builds. A LOCAL housekeeping helper - never touches
    git, never creates or pushes a tag.

.DESCRIPTION
    Each build stamps a fresh yy.M.d.HHmm version and drops a new tree under
    stage\ and a new artifact set under dist\, so both folders grow without
    bound (dist\ alone reaches gigabytes of superseded ZIPs/installers). This
    script prunes them, keeping only the build identified by -KeepVersion (the
    one a build script is about to produce, or nothing when run standalone).

    What it can clean (pick with switches, or -All / no switch = everything):
      -Stage  stage\*  staged payload trees   (except -KeepVersion)
      -Dist   dist\*   ZIP/EXE artifacts+hash (except -KeepVersion)
      -Bin    obj\ and orphaned bin\ subfolders - anything other than the
              Release / Debug / SingleFile outputs the real flows produce
      -Temp   temp\ scratch contents and the stale current.log runtime diagnostic

    It never deletes bin\Release, bin\Debug or bin\SingleFile (MSBuild owns
    those), and callers that pass -KeepVersion keep the artifacts they just made.

.PARAMETER KeepVersion
    Version stamp (yy.M.d.HHmm) whose stage\ tree and dist\ artifacts are
    preserved. Empty = keep nothing (a full purge of old builds).

.PARAMETER Stage
    Prune stage\ (staged payload trees).

.PARAMETER Dist
    Prune dist\ (packaged ZIP/EXE artifacts and their .sha256 sidecars).

.PARAMETER Bin
    Remove obj\ and orphaned bin\ subfolders (leftover experiment outputs).

.PARAMETER Temp
    Clear temp\ scratch contents and delete current.log.

.PARAMETER All
    Clean everything. Also the default when no switch is given.

.EXAMPLE
    .\tools\Clean-Build.ps1
    Purge every old build, orphaned binary and temp file (keep nothing).

.EXAMPLE
    .\tools\Clean-Build.ps1 -Dist -Stage -KeepVersion 26.7.12.0325
    Remove all staged trees and dist artifacts except that one version.
#>
param(
    [string]$KeepVersion = "",
    [switch]$Stage,
    [switch]$Dist,
    [switch]$Bin,
    [switch]$Temp,
    [switch]$All
)

$ErrorActionPreference = "Stop"

# No switch selected = clean everything.
if (-not ($Stage -or $Dist -or $Bin -or $Temp -or $All)) { $All = $true }
if ($All) { $Stage = $true; $Dist = $true; $Bin = $true; $Temp = $true }

$solutionDir = Split-Path $PSScriptRoot -Parent

$script:reclaimed = 0L

function Get-PathSize([string]$Path) {
    if (-not (Test-Path $Path)) { return 0L }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer) {
        return (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    }
    return $item.Length
}

function Remove-Old([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path)) { return }
    $bytes = [long](Get-PathSize $Path)
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        $script:reclaimed += $bytes
        $mb = [Math]::Round($bytes / 1MB, 1)
        Write-Host "  removed $Label ($mb MB)"
    } catch {
        Write-Warning "  could not remove $Label - $($_.Exception.Message)"
    }
}

Write-Host "Cleaning old build output in $solutionDir"
if ($KeepVersion) { Write-Host "Keeping version: $KeepVersion" }

# --- stage\ : versioned payload trees ---------------------------------------
if ($Stage) {
    $stageDir = Join-Path $solutionDir "stage"
    if (Test-Path $stageDir) {
        Get-ChildItem -LiteralPath $stageDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
            if ($KeepVersion -and $_.Name -like "*-$KeepVersion-*") { return }
            Remove-Old $_.FullName "stage\$($_.Name)"
        }
    }
}

# --- dist\ : packaged artifacts + .sha256 sidecars --------------------------
if ($Dist) {
    $distDir = Join-Path $solutionDir "dist"
    if (Test-Path $distDir) {
        Get-ChildItem -LiteralPath $distDir -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
            if ($KeepVersion -and $_.Name -like "*-$KeepVersion-*") { return }
            Remove-Old $_.FullName "dist\$($_.Name)"
        }
    }
}

# --- bin\ orphans + obj\ ----------------------------------------------------
# Keep only the outputs the real build flows own; everything else in bin\ is a
# leftover experiment folder safe to drop (all of bin\ is gitignored).
if ($Bin) {
    $binKeep = @("Release", "Debug", "SingleFile")
    $binDir = Join-Path $solutionDir "bin"
    if (Test-Path $binDir) {
        Get-ChildItem -LiteralPath $binDir -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            if ($binKeep -contains $_.Name) { return }
            Remove-Old $_.FullName "bin\$($_.Name)"
        }
    }
    Remove-Old (Join-Path $solutionDir "obj") "obj"
}

# --- temp\ scratch + stale runtime log --------------------------------------
if ($Temp) {
    $tempDir = Join-Path $solutionDir "temp"
    if (Test-Path $tempDir) {
        Get-ChildItem -LiteralPath $tempDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Old $_.FullName "temp\$($_.Name)"
        }
    }
    Remove-Old (Join-Path $solutionDir "current.log") "current.log"
}

$totalMb = [Math]::Round($script:reclaimed / 1MB, 1)
Write-Host "Clean-up done - reclaimed $totalMb MB." -ForegroundColor Green
