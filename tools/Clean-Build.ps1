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
      -Temp   stale temp\ scratch and the stale current.log runtime diagnostic

    It never deletes bin\Release, bin\Debug or bin\SingleFile (MSBuild owns
    those), and callers that pass -KeepVersion keep the artifacts they just made.

    Several sessions work in one tree at once (L0360), so the clean never takes
    what another session may still be using:
      - a stage\ tree or dist\ artifact whose version stamp (yy.M.d.HHmm) is
        younger than -MinAgeHours is kept: it may be a sibling build still
        packaging. An entry without a stamp is judged by its last write time.
      - temp\ holds the queue runner's locks and leases, agent chat and every
        ticket's scratch folder; the build itself writes nothing there. Only an
        entry whose newest file is older than -TempMinAgeDays is removed, and
        the shared-state folders (*.QUEUE, *.LEASES, *.LOCK, LEASE-HANDOFF,
        spec-queue, metrics, AGENT-CHAT) and their files are never removed.

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
    Remove stale temp\ scratch (see -TempMinAgeDays) and delete current.log.

.PARAMETER MinAgeHours
    A stage\ tree or dist\ artifact younger than this is kept even when it is
    not -KeepVersion (a sibling build may be packaging it). Default 6; 0 turns
    the guard off.

.PARAMETER TempMinAgeDays
    A temp\ entry is removed only when its newest file is older than this.
    Default 7. The shared-state folders are kept at any age.

.PARAMETER SolutionDir
    Tree to clean. Default: the repository this script lives in (a fixture
    passes its own throw-away tree).

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
    [switch]$All,
    [double]$MinAgeHours = 6,
    [double]$TempMinAgeDays = 7,
    [string]$SolutionDir = ""
)

$ErrorActionPreference = "Stop"

# No switch selected = clean everything.
if (-not ($Stage -or $Dist -or $Bin -or $Temp -or $All)) { $All = $true }
if ($All) { $Stage = $true; $Dist = $true; $Bin = $true; $Temp = $true }

$solutionDir = if ($SolutionDir) { (Resolve-Path -LiteralPath $SolutionDir).Path } else { Split-Path $PSScriptRoot -Parent }

$script:reclaimed = 0L
$now = Get-Date

# temp\ entries other sessions own: lock and lease state of the queue runner and the
# agent chat. Matched on the top-level name, case-insensitively.
$sharedTempPatterns = @("*.QUEUE", "*.LEASES", "*.LOCK", "*.LOCKS", "LEASE-HANDOFF", "spec-queue", "spec-next-*", "metrics", "AGENT-CHAT")

function Test-SharedTempName([string]$Name) {
    foreach ($pattern in $sharedTempPatterns) { if ($Name -like $pattern) { return $true } }
    return $false
}

# Start time of the build that made a stage\ or dist\ entry: its yy.M.d.HHmm stamp,
# else its last write time.
function Get-EntryTime([System.IO.FileSystemInfo]$Item) {
    if ($Item.Name -match '-(\d{2}\.\d{1,2}\.\d{1,2}\.\d{4})-') {
        $parsed = [datetime]::MinValue
        if ([datetime]::TryParseExact($Matches[1], "yy.M.d.HHmm", [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref]$parsed)) { return $parsed }
    }
    return $Item.LastWriteTime
}

function Test-TooYoung([System.IO.FileSystemInfo]$Item) {
    if ($MinAgeHours -le 0) { return $false }
    return (Get-EntryTime $Item) -gt $now.AddHours(-$MinAgeHours)
}

# Newest file write anywhere under a temp\ entry (a folder's own time does not move when
# a nested file changes); an empty folder is judged by its own time.
function Get-NewestWrite([System.IO.FileSystemInfo]$Item) {
    if (-not $Item.PSIsContainer) { return $Item.LastWriteTime }
    $newest = [datetime]::MinValue
    Get-ChildItem -LiteralPath $Item.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.LastWriteTime -gt $newest) { $newest = $_.LastWriteTime }
    }
    if ($newest -eq [datetime]::MinValue) { return $Item.LastWriteTime }
    return $newest
}

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
            if (Test-TooYoung $_) { Write-Host "  kept stage\$($_.Name) (younger than $MinAgeHours h)"; return }
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
            if (Test-TooYoung $_) { Write-Host "  kept dist\$($_.Name) (younger than $MinAgeHours h)"; return }
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
        $tempCutoff = $now.AddDays(-$TempMinAgeDays)
        Get-ChildItem -LiteralPath $tempDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
            if (Test-SharedTempName $_.Name) { return }
            if ((Get-NewestWrite $_) -gt $tempCutoff) { return }
            Remove-Old $_.FullName "temp\$($_.Name)"
        }
    }
    Remove-Old (Join-Path $solutionDir "current.log") "current.log"
}

$totalMb = [Math]::Round($script:reclaimed / 1MB, 1)
Write-Host "Clean-up done - reclaimed $totalMb MB." -ForegroundColor Green
