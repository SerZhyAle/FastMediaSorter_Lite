$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "FastMediaSorter.sln"
$OutputDir    = Join-Path $SolutionDir "bin\Release"
$ExeName      = "FastMediaSorter_LITE.exe"
$Destinations = @(
    "C:\GD\i\",
    "C:\GD\tc\SZA\_APP\"
)

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

# The app now ships with native LibVLC (libvlc\win-x64|win-x86) and the managed
# LibVLCSharp assemblies, so the whole output tree must be deployed - not just the exe.
# Skip debug/doc artefacts (.pdb/.xml), mirroring the CI release staging.
$OutputRoot = (Resolve-Path $OutputDir).Path
$DeployFiles = Get-ChildItem $OutputRoot -Recurse -File |
    Where-Object { $_.Extension -notin '.pdb', '.xml' }

foreach ($Destination in $Destinations) {
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    foreach ($File in $DeployFiles) {
        $Relative = $File.FullName.Substring($OutputRoot.Length).TrimStart('\')
        $Target   = Join-Path $Destination $Relative
        $TargetDir = Split-Path $Target -Parent
        if (-not (Test-Path $TargetDir)) {
            New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
        }
        Copy-Item -Path $File.FullName -Destination $Target -Force
    }

    Write-Host "Deployed $($DeployFiles.Count) files -> $Destination"
}
