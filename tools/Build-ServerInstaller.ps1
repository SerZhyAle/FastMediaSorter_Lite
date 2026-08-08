<#
.SYNOPSIS
    Builds the SERVER edition installer (setup.exe) - the always-on Folder Share
    Server, hosted by a Windows service. LOCAL flow ("сборка", not "релиз"): it never
    creates or pushes a v* tag.

.DESCRIPTION
    Produces ONLY:

        dist\FastMediaSorter-<version>-windows-x64-server-setup.exe (+ .sha256)

    from publishing\installer\FastMediaSorterServer.iss - see
    docs/specifications/done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md.

    The staged payload is deliberately LEAN and unlike the User installer's: the Share
    Manager (self-contained .NET 10), the Go SFTP worker and the two elevated helper
    scripts - no viewer, no VLC codecs, no OCR models. A machine whose job is to keep
    folders reachable does not need an image sorter, and the difference is hundreds of
    megabytes.

    The Server edition is a packaging and host-mode variant, NOT a fork: it ships the
    same worker binary, the same IPC schema and the same .fmscfg format as the User
    edition, so a phone paired against one keeps working against the other.

.PARAMETER Version
    Version stamp (default: current yy.M.d.HHmm). Feeds the installer file name.
    Keep it identical to the User edition build of the same release.

.PARAMETER SkipBuild
    Reuse the existing publish output instead of re-publishing the Companion.

.PARAMETER MaxCompress
    Use the published-release compression (lzma2/ultra, solid) - smallest file but
    several minutes slower. Off by default, like Build-Installer.ps1.

.PARAMETER Open
    Reveal the finished installer in Explorer when done.

.EXAMPLE
    .\tools\Build-ServerInstaller.ps1
    Build + package the Server installer into dist\.
#>
param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm"),
    [switch]$SkipBuild,
    [switch]$MaxCompress,
    [switch]$Open
)

$ErrorActionPreference = "Stop"

$solutionDir   = Split-Path $PSScriptRoot -Parent
$payloadDir    = Join-Path $solutionDir "payload\companion"
$stageDir      = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64-server")
$distDir       = Join-Path $solutionDir "dist"
$issFile       = Join-Path $solutionDir "publishing\installer\FastMediaSorterServer.iss"
$companionProj = Join-Path $solutionDir "src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj"

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

# Fail early with a fix hint instead of publishing for a minute and only then
# discovering the compiler is missing.
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

Write-Host "Inno Setup:  $iscc"
Write-Host "Version:     $Version"

# --- stage -------------------------------------------------------------------
Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

foreach ($extra in @("README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt")) {
    $source = Join-Path $solutionDir $extra
    if (Test-Path $source) { Copy-Item $source $stageDir -Force }
}

# The Share Manager. Self-contained single-file, so this one exe is the whole .NET
# side of the package - there is no shared runtime to depend on on a bare server.
if (-not (Test-Path $companionProj)) {
    throw "Companion project not found ($companionProj) - the Server package would have nothing to install."
}
if ($SkipBuild) {
    $prebuilt = Join-Path $solutionDir "bin\Release\FastMediaSorterCompanion.exe"
    if (-not (Test-Path $prebuilt)) { throw "-SkipBuild was given but $prebuilt does not exist. Run without -SkipBuild first." }
    Copy-Item $prebuilt $stageDir -Force
    Write-Host "Reused the existing Share Manager build."
} else {
    $companionPub = Join-Path $stageDir "companion-publish-tmp"
    Write-Host "Publishing the Share Manager (net10, self-contained single-file).."
    & dotnet publish $companionProj -c Release -r win-x64 -o $companionPub -v minimal --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Companion) failed with exit code $LASTEXITCODE." }
    $companionExe = Join-Path $companionPub "FastMediaSorterCompanion.exe"
    if (-not (Test-Path $companionExe)) { throw "Companion exe not found at $companionExe after publish." }
    Copy-Item $companionExe $stageDir -Force
    Remove-Item -LiteralPath $companionPub -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Bundled the Share Manager -> FastMediaSorterCompanion.exe"
}

# The Go SFTP worker - the thing the Windows service actually runs. Without it the
# package installs a console with nothing to manage, so this is a hard failure, not a
# warning like in the User installer (where the viewer still works on its own).
if (-not (Test-Path $payloadDir)) {
    throw "Companion worker payload not found ($payloadDir). The Server edition IS the worker - build it in its own repository (P:\windows\fms_companion) and vendor it to payload\companion first."
}
$companionDest = Join-Path $stageDir "companion"
New-Item -ItemType Directory -Path $companionDest -Force | Out-Null
Copy-Item -Path (Join-Path $payloadDir "*") -Destination $companionDest -Recurse -Force
if (-not (Test-Path (Join-Path $companionDest "fms-share-worker.exe"))) {
    throw "fms-share-worker.exe is missing from $companionDest - the service would have no executable to run."
}
Write-Host "Bundled the SFTP worker -> companion\"

# --- compile installer -------------------------------------------------------
$stageAbs = (Resolve-Path $stageDir).Path
$distAbs  = (Resolve-Path $distDir).Path
$isccArgs = @("/DVersion=$Version", "/DSourceDir=$stageAbs", "/O$distAbs")
if (-not $MaxCompress) {
    $isccArgs += "/DFastCompression"
    Write-Host "Compression: lzma2/fast (local build - pass -MaxCompress for the smaller ultra build)"
} else {
    Write-Host "Compression: lzma2/ultra (max ratio)"
}
& $iscc @isccArgs $issFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setupName = "FastMediaSorter-$Version-windows-x64-server-setup.exe"
$setupPath = Join-Path $distDir $setupName
if (-not (Test-Path $setupPath)) { throw "Server installer was not produced at $setupPath." }

$setupHash = (Get-FileHash -Algorithm SHA256 $setupPath).Hash
"$setupHash  $setupName" | Out-File -FilePath ($setupPath + ".sha256") -Encoding ascii

$sizeMb = [Math]::Round((Get-Item $setupPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Server installer ready (run it ELEVATED on the target machine):" -ForegroundColor Green
Write-Host "  $setupPath  ($sizeMb MB)"
Write-Host "  SHA256: $setupHash"

if ($Open) { Start-Process explorer.exe "/select,`"$setupPath`"" }
