<#
.SYNOPSIS
    Builds a standalone offline installer (setup.exe) you can copy to any Windows
    machine and run - no GitHub release, no tag, no internet needed at install time.

.DESCRIPTION
    A convenient LOCAL flow ("сборка", not "релиз" - never pushes a v* tag) that
    produces ONLY the copy-anywhere Inno Setup installer in dist\:

        dist\FastMediaSorter-<version>-windows-x64-setup.exe (+ .sha256)

    It reuses the frozen installer definition (installer\FastMediaSorter.iss) and
    the OCR offline-payload helper - nothing under installer\ / winget\ / msix\ is
    modified, and every technical identifier (AppId, exe name, ProgIDs) stays as
    published, so this installer coexists with the winget/Store channels.

    The installer is per-user by default (PrivilegesRequired=lowest) - it installs
    without admin rights, so a plain copy-and-double-click works on any machine.

    Difference from tools\Build-OfflineRelease.ps1: that one builds BOTH the
    portable ZIP and the installer and always bundles the heavy tessdata_best
    models (~hundreds of MB). This script builds JUST the installer and, by
    default, bundles only the lighter fast OCR models - a smaller, quicker package
    to carry around. Use -IncludeBest for a fully-offline-including-best build.

.PARAMETER Version
    Version stamp (default: current yy.M.d.HHmm). Feeds both the build and the
    installer file name; independent of the build-time auto-stamp.

.PARAMETER IncludeBest
    Also bundle the tessdata_best OCR models (larger installer, best-quality OCR
    fully offline). Off by default; best models otherwise download on demand.

.PARAMETER SkipOcr
    Smallest/fastest installer: bundle no tessdata at all. OCR models download on
    demand the first time OCR is used (needs internet then).

.PARAMETER SkipBuild
    Reuse the existing bin\Release output instead of rebuilding (faster when you
    are only re-packaging). Fails if bin\Release has no exe yet.

.PARAMETER Open
    Reveal the finished installer in Explorer when done.

.EXAMPLE
    .\tools\Build-Installer.ps1
    Build + package a fast-OCR offline installer into dist\.

.EXAMPLE
    .\tools\Build-Installer.ps1 -IncludeBest
    Fully offline installer including the best-quality OCR models.

.EXAMPLE
    .\tools\Build-Installer.ps1 -SkipBuild -SkipOcr -Open
    Re-package the current build into the smallest installer and open dist\.
#>
param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm"),
    [switch]$IncludeBest,
    [switch]$SkipOcr,
    [switch]$SkipBuild,
    [switch]$Open
)

$ErrorActionPreference = "Stop"

$solutionDir  = Split-Path $PSScriptRoot -Parent
$solutionFile = Join-Path $solutionDir "FastMediaSorter.sln"
$releaseDir   = Join-Path $solutionDir "bin\Release"
$payloadDir   = Join-Path $solutionDir "payload\companion"
$stageDir     = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64")
$distDir      = Join-Path $solutionDir "dist"
$issFile      = Join-Path $solutionDir "installer\FastMediaSorter.iss"
$exeName      = "FastMediaSorter_LITE.exe"

function Resolve-MsBuild {
    $fromVsWhere = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
        -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null |
        Select-Object -First 1
    if ($fromVsWhere -and (Test-Path $fromVsWhere)) { return $fromVsWhere }

    foreach ($fallback in @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe")) {
        if (Test-Path $fallback) { return $fallback }
    }
    throw "MSBuild not found. Install Visual Studio or add MSBuild to PATH."
}

function Resolve-NuGet {
    $cmd = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $nuget = Join-Path $solutionDir "tools\nuget.exe"
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Path (Split-Path $nuget) -Force | Out-Null
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
    }
    return $nuget
}

function Resolve-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { return $candidate }
    }
    return ""
}

function Get-RelativePath {
    param([string]$BasePath, [string]$TargetPath)
    $baseResolved = (Resolve-Path $BasePath).Path.TrimEnd('\') + '\'
    $targetResolved = (Resolve-Path $TargetPath).Path
    $baseUri = New-Object System.Uri($baseResolved)
    $targetUri = New-Object System.Uri($targetResolved)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', '\'))
}

# Inno Setup is a hard requirement to emit setup.exe - fail early with a fix hint
# instead of building for minutes and only then discovering it is missing.
$iscc = Resolve-Iscc
if (-not $iscc) {
    Write-Error @"
Inno Setup 6 (ISCC.exe) was not found - it is required to build the installer.
Install it, then re-run:
    winget install JRSoftware.InnoSetup
(or download from https://jrsoftware.org/isdl.php)
"@
    exit 1
}

$msbuild = Resolve-MsBuild
Write-Host "MSBuild:     $msbuild"
Write-Host "Inno Setup:  $iscc"
Write-Host "Version:     $Version"

# --- build ------------------------------------------------------------------
if ($SkipBuild) {
    Write-Host "Skipping build - reusing existing bin\Release."
} else {
    $nuget = Resolve-NuGet
    Write-Host "NuGet:       $nuget"
    & $nuget restore $solutionFile -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed with exit code $LASTEXITCODE." }

    & $msbuild $solutionFile /p:Configuration=Release /p:Platform="Any CPU" /p:ReleaseVersion=$Version /t:Rebuild /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path (Join-Path $releaseDir $exeName))) {
    throw "Release executable not found in $releaseDir. Run without -SkipBuild first."
}

# --- stage (bin\Release minus pdb/xml) --------------------------------------
Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Get-ChildItem $releaseDir -Recurse -File |
    Where-Object { $_.Extension -notin ".pdb", ".xml" } |
    ForEach-Object {
        $relative = Get-RelativePath -BasePath $releaseDir -TargetPath $_.FullName
        $destination = Join-Path $stageDir $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item $_.FullName $destination -Force
    }

foreach ($extra in @("README.md", "LICENSE")) {
    $source = Join-Path $solutionDir $extra
    if (Test-Path $source) { Copy-Item $source $stageDir -Force }
}

# The Android-share worker must ship in companion\ next to the exe or the Share
# tab/wizard find nothing at runtime. MSBuild does not stage it - mirror the
# vendored payload in explicitly (same rule as build.ps1's Deploy-Companion).
if (Test-Path $payloadDir) {
    $companionDest = Join-Path $stageDir "companion"
    New-Item -ItemType Directory -Path $companionDest -Force | Out-Null
    Copy-Item -Path (Join-Path $payloadDir "*") -Destination $companionDest -Recurse -Force
    Write-Host "Bundled companion worker -> companion\"
} else {
    Write-Warning "Companion payload not found ($payloadDir) - the installer will ship without the Android Share worker."
}

# --- OCR offline payload ----------------------------------------------------
if ($SkipOcr) {
    Write-Host "Skipping OCR payload (-SkipOcr) - models download on demand."
    # Still trim the x86-only runtime trees the x64 installer never needs.
    foreach ($x86 in @("x86", "libvlc\win-x86", "runtimes\win-x86")) {
        $p = Join-Path $stageDir $x86
        if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
} else {
    $ocrArgs = @{ StageDir = $stageDir }
    if ($IncludeBest) { $ocrArgs.IncludeBest = $true }
    & (Join-Path $PSScriptRoot "Prepare-OcrOfflinePayload.ps1") @ocrArgs
}

# --- compile installer ------------------------------------------------------
$stageAbs = (Resolve-Path $stageDir).Path
$distAbs  = (Resolve-Path $distDir).Path
& $iscc "/DVersion=$Version" "/DSourceDir=$stageAbs" "/O$distAbs" $issFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setupName = "FastMediaSorter-$Version-windows-x64-setup.exe"
$setupPath = Join-Path $distDir $setupName
if (-not (Test-Path $setupPath)) { throw "Installer was not produced at $setupPath." }

$setupHash = (Get-FileHash -Algorithm SHA256 $setupPath).Hash
"$setupHash  $setupName" | Out-File -FilePath ($setupPath + ".sha256") -Encoding ascii

$sizeMb = [Math]::Round((Get-Item $setupPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer ready - copy this file to any Windows machine and run it:" -ForegroundColor Green
Write-Host "  $setupPath  ($sizeMb MB)"
Write-Host "  SHA256: $setupHash"

if ($Open) { Start-Process explorer.exe "/select,`"$setupPath`"" }
