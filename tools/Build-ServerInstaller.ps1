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

    The Share Manager, worker and FileDO are copied from the matching User stage. The viewer remains
    an optional, unticked component, and the offline support the User setup carries (VLC codecs,
    PDFium and the OCR models the pair was built with) is offered under it as optional items taken
    from the same stage through its payload manifest (L0307).

    The Server edition is a packaging and host-mode variant, NOT a fork: it ships the
    same worker binary, the same IPC schema and the same .fmscfg format as the User
    edition, so a phone paired against one keeps working against the other.

.PARAMETER Version
    Version stamp (default: current yy.M.d.HHmm). Feeds the installer file name.
    A User stage for this exact version is required. Run Build-Installer.ps1 with
    the same -Version and desired payload options before invoking this script.

.PARAMETER SkipBuild
    Retained for existing callers. The Share Manager always comes from the matching
    User stage, regardless of this switch.

.PARAMETER MaxCompress
    Use the published-release compression (lzma2/ultra, solid) - smallest file but
    several minutes slower. Off by default, like Build-Installer.ps1.

.PARAMETER IncludeExchange
    THE SHIPPING GATE of the exchange server (specification 133 section 8, ticket 141): off by
    default. Off, the stage has no exchange\ folder and ISCC is called with /DNoExchange - the
    optional `exchange` component and the `exchangeservice` task compile out. On, exchange\ is
    staged (tools\Stage-ExchangePayload.ps1) from the User stage of the SAME version,
    and the build throws when the binary or its sidecar is missing.

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
    [switch]$IncludeExchange,
    [switch]$Open
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'lib\InstallerPayloadSelection.ps1')

$solutionDir   = Split-Path $PSScriptRoot -Parent
$userStage     = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64")
$stageDir      = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64-server")
$distDir       = Join-Path $solutionDir "dist"
$issFile       = Join-Path $solutionDir "publishing\installer\FastMediaSorterServer.iss"

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

# The OPTIONAL viewer component (ticket 132) and its offline support - the VLC codecs, PDFium and the
# OCR models the pair was built with (L0307): staged from the User stage of THIS version, through its
# payload manifest, so the Server package offers the same set the User package does and the viewer in
# it is the one the same build produced. A missing manifest, mismatched executables or a stage changed
# after it was recorded fail packaging; build.ps1 / Build-Installer.ps1 stage the User installer first.
# It runs FIRST: it is the step that proves the User stage is the build's own before anything is
# copied out of it.
& (Join-Path $PSScriptRoot "Stage-ServerViewerPayload.ps1") `
    -UserStage $userStage `
    -Destination $stageDir -Version $Version -Require

# Both packages use the same staged sharing files, including the same FileDO release.
& (Join-Path $PSScriptRoot "Stage-ServerSharePayload.ps1") -UserStage $userStage -Destination $stageDir

# The options of the pair are the User stage's recorded ones; this run's switches must agree with them.
$payloadManifest = Read-InstallerPayloadManifest -Stage $userStage -Version $Version
if ([bool]$payloadManifest.exchange -ne [bool]$IncludeExchange) {
    $recorded = if ($payloadManifest.exchange) { "open (built with -IncludeExchange)" } else { "shut" }
    throw "The User stage was built with the exchange gate $recorded but this run has -IncludeExchange $([bool]$IncludeExchange). Use the same gate option for both installers."
}

# The exchange server (specification 133, ticket 141) - behind the shipping gate. The two
# editions of one build carry the SAME binary, so it is taken from the User stage of this
# version; a missing tree cannot be replaced by another build.
# Stage-ExchangePayload.ps1 throws when the exe or its sidecar is missing: a Server setup that
# claims the component without the binary is worse than a failed build.
if ($IncludeExchange) {
    $exchangeSource = Join-Path $userStage "exchange"
    Write-Host "Exchange server source: $exchangeSource"
    & (Join-Path $PSScriptRoot "Stage-ExchangePayload.ps1") -Source $exchangeSource -Destination $stageDir -Server
} else {
    Write-Host "Exchange server: not included (shipping gate shut - pass -IncludeExchange once rung 2 has frozen the wire)."
}

# --- compile installer -------------------------------------------------------
$stageAbs = (Resolve-Path $stageDir).Path
$distAbs  = (Resolve-Path $distDir).Path
$isccArgs = @("/DVersion=$Version", "/DSourceDir=$stageAbs", "/O$distAbs")
# The gate, proven on the stage: the component compiles in exactly when the stage carries it. The
# defines (/DNoExchange, /DNoOcrModels, /DOcrBest) come from the same record the User setup was
# compiled with.
if ($IncludeExchange) {
    if (-not (Test-Path (Join-Path $stageDir "exchange\fms-exchange-server.exe"))) {
        throw "-IncludeExchange was given but the stage has no exchange\fms-exchange-server.exe."
    }
} else {
    if (Test-Path (Join-Path $stageDir "exchange")) { throw "The stage carries an exchange\ folder but the shipping gate is shut (-IncludeExchange not given)." }
}
$isccArgs += Get-InstallerIsccDefines -Manifest $payloadManifest
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
