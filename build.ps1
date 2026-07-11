$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "FastMediaSorter.sln"
$OutputDir    = Join-Path $SolutionDir "bin\Release"
$SingleFileDir = Join-Path $SolutionDir "bin\SingleFile"
$ExeName      = "FastMediaSorter_LITE.exe"
# Android-share sidecar payload (docs/specifications/SPECIFICATION_ANDROID_FOLDER_SHARE.md). The
# worker exe must ship in a "companion\" subfolder next to every deployed exe,
# or the Share tab / wizard finds nothing (WorkerProcess.IsAvailable() = False).
$PayloadCompanionDir = Join-Path $SolutionDir "payload\companion"
$Destinations = @(
    "C:\GD\i\",
    "C:\GD\tc\SZA\_APP\"
)

# Mirror payload\companion\ into <targetDir>\companion\. No-op (with a warning)
# when the payload is absent, so a checkout without the vendored worker still
# builds - it just ships without the Share feature.
function Deploy-Companion([string]$TargetDir) {
    if (-not (Test-Path $PayloadCompanionDir)) {
        Write-Warning "Companion payload not found ($PayloadCompanionDir) - Share feature will be unavailable in: $TargetDir"
        return
    }
    $dest = Join-Path $TargetDir "companion"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $PayloadCompanionDir "*") -Destination $dest -Recurse -Force
    Write-Host "Deployed companion payload -> $dest"
}

# Find MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null |
    Select-Object -First 1

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    # Fallback: try well-known VS 2022 path
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $msbuild)) {
        Write-Error "MSBuild not found. Install Visual Studio or add MSBuild to PATH."
        exit 1
    }
}

Write-Host "Using MSBuild: $msbuild"

# Restore NuGet packages (packages.config). Needed for a fresh clone; a no-op when
# packages are already present. Downloads nuget.exe to tools\ if it isn't on PATH.
$nuget = (Get-Command nuget.exe -ErrorAction SilentlyContinue).Source
if (-not $nuget) {
    $nuget = Join-Path $SolutionDir "tools\nuget.exe"
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Path (Split-Path $nuget) -Force | Out-Null
        Write-Host "Downloading nuget.exe..."
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
    }
}
Write-Host "Restoring NuGet packages..."
& $nuget restore $SolutionFile -NonInteractive
if ($LASTEXITCODE -ne 0) {
    Write-Error "NuGet restore failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Host "Building Release..."

& $msbuild $SolutionFile /p:Configuration=Release /p:Platform="Any CPU" /t:Rebuild /v:minimal /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

$ExePath = Join-Path $OutputDir $ExeName
if (-not (Test-Path $ExePath)) {
    Write-Error "Output not found: $ExePath"
    exit 1
}

# The release output still contains the expanded runtime tree for local debugging,
# but the distributable build is now the exe alone: it contains the managed
# assemblies and unpacks the native runtime to %LOCALAPPDATA% on first start.
New-Item -ItemType Directory -Path $SingleFileDir -Force | Out-Null
$SingleFileExe = Join-Path $SingleFileDir $ExeName
Copy-Item -Path $ExePath -Destination $SingleFileExe -Force
Write-Host "Single-file build staged at: $SingleFileExe"

# Keep the worker beside the staging outputs too: MSBuild Rebuild does not touch
# the companion\ subfolder, but a fresh checkout has none - self-heal both.
Deploy-Companion $OutputDir
Deploy-Companion $SingleFileDir

foreach ($Destination in $Destinations) {
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    $Target = Join-Path $Destination $ExeName
    Copy-Item -Path $SingleFileExe -Destination $Target -Force

    Write-Host "Deployed single-file exe -> $Target"
    Deploy-Companion $Destination
}
