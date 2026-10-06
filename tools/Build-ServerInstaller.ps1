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
    scripts - no VLC codecs, no OCR models. The viewer is an OPTIONAL, unticked component
    (ticket 132; tools\Stage-ServerViewerPayload.ps1 stages it from the User stage of the
    same version), so a machine whose job is to keep folders reachable installs no image
    sorter unless asked, and the default package stays hundreds of megabytes smaller.

    The Server edition is a packaging and host-mode variant, NOT a fork: it ships the
    same worker binary, the same IPC schema and the same .fmscfg format as the User
    edition, so a phone paired against one keeps working against the other.

.PARAMETER Version
    Version stamp (default: current yy.M.d.HHmm). Feeds the installer file name.
    Keep it identical to the User edition build of the same release.

.PARAMETER SkipBuild
    Reuse an existing Share Manager build instead of re-publishing the Companion:
    the one staged by the User installer of the SAME version
    (stage\FastMediaSorter-<version>-windows-x64\), else bin\Release.

.PARAMETER MaxCompress
    Use the published-release compression (lzma2/ultra, solid) - smallest file but
    several minutes slower. Off by default, like Build-Installer.ps1.

.PARAMETER IncludeExchange
    THE SHIPPING GATE of the exchange server (specification 133 section 8, ticket 141): off by
    default. Off, the stage has no exchange\ folder and ISCC is called with /DNoExchange - the
    optional `exchange` component and the `exchangeservice` task compile out. On, exchange\ is
    staged (tools\Stage-ExchangePayload.ps1) from the User stage of the SAME version, else from
    bin\Release\exchange, and the build throws when the binary or its sidecar is missing.

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
    # The User installer of the same version publishes the Share Manager fresh into
    # its own stage, so take THAT one when it exists: the two setup.exe files of one
    # build must carry the same Share Manager. bin\Release is only the fallback - it
    # can hold an exe from an earlier build (build.ps1 -SkipCompanion rebuilds none,
    # and msbuild Rebuild never removes it), which is how dist\ used to end up with
    # two setups of one version carrying different Share Managers.
    $userStageExe = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64\FastMediaSorterCompanion.exe")
    $binExe = Join-Path $solutionDir "bin\Release\FastMediaSorterCompanion.exe"
    if (Test-Path $userStageExe) {
        $prebuilt = $userStageExe
    } elseif (Test-Path $binExe) {
        $prebuilt = $binExe
    } else {
        throw "-SkipBuild was given but neither $userStageExe nor $binExe exists. Run without -SkipBuild first."
    }
    Copy-Item $prebuilt $stageDir -Force
    Write-Host "Reused the existing Share Manager build: $prebuilt"
} else {
    $companionPub = Join-Path $stageDir "companion-publish-tmp"
    Write-Host "Publishing the Share Manager (net10, self-contained single-file).."
    & dotnet publish $companionProj -c Release -r win-x64 -p:ReleaseVersion=$Version -o $companionPub -v minimal --nologo
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
    & (Join-Path $PSScriptRoot "Stage-FileDoPayload.ps1") -Destination $stageDir
if (-not (Test-Path (Join-Path $companionDest "fms-share-worker.exe"))) {
    throw "fms-share-worker.exe is missing from $companionDest - the service would have no executable to run."
}
Write-Host "Bundled the SFTP worker -> companion\"

# The OPTIONAL viewer component (ticket 132): staged from the User stage of THIS version, so the
# viewer in the Server package is the one the same build produced. Absent stage = no component
# (a warning, never a half-viewer); build.ps1 stages the User installer first, so a full local
# build always has it. The shipping packagers pass -Require.
& (Join-Path $PSScriptRoot "Stage-ServerViewerPayload.ps1") `
    -UserStage (Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64")) `
    -Destination $stageDir

# The exchange server (specification 133, ticket 141) - behind the shipping gate. The two
# editions of one build carry the SAME binary, so it is taken from the User stage of this
# version when that stage holds it, else from bin\Release\exchange (build.ps1 deploys it).
# Stage-ExchangePayload.ps1 throws when the exe or its sidecar is missing: a Server setup that
# claims the component without the binary is worse than a failed build.
if ($IncludeExchange) {
    $userExchange = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64\exchange")
    $binExchange  = Join-Path $solutionDir "bin\Release\exchange"
    if (Test-Path (Join-Path $userExchange "fms-exchange-server.exe")) {
        $exchangeSource = $userExchange
    } else {
        $exchangeSource = $binExchange
    }
    Write-Host "Exchange server source: $exchangeSource"
    & (Join-Path $PSScriptRoot "Stage-ExchangePayload.ps1") -Source $exchangeSource -Destination $stageDir -Server
} else {
    Write-Host "Exchange server: not included (shipping gate shut - pass -IncludeExchange once rung 2 has frozen the wire)."
}

# --- compile installer -------------------------------------------------------
$stageAbs = (Resolve-Path $stageDir).Path
$distAbs  = (Resolve-Path $distDir).Path
$isccArgs = @("/DVersion=$Version", "/DSourceDir=$stageAbs", "/O$distAbs")
# The gate, proven on the stage: the component compiles in exactly when the stage carries it.
if ($IncludeExchange) {
    if (-not (Test-Path (Join-Path $stageDir "exchange\fms-exchange-server.exe"))) {
        throw "-IncludeExchange was given but the stage has no exchange\fms-exchange-server.exe."
    }
} else {
    if (Test-Path (Join-Path $stageDir "exchange")) { throw "The stage carries an exchange\ folder but the shipping gate is shut (-IncludeExchange not given)." }
    $isccArgs += "/DNoExchange"
}
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
