param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm")
)

$ErrorActionPreference = "Stop"

$solutionDir = Split-Path $PSScriptRoot -Parent
$solutionFile = Join-Path $solutionDir "FastMediaSorter.sln"
$releaseDir = Join-Path $solutionDir "bin\Release"
$stageDir = Join-Path $solutionDir ("stage\FastMediaSorter-" + $Version + "-windows-x64")
$distDir = Join-Path $solutionDir "dist"

function Resolve-MsBuild {
    $fromVsWhere = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
        -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null |
        Select-Object -First 1

    if ($fromVsWhere -and (Test-Path $fromVsWhere)) {
        return $fromVsWhere
    }

    $fallback = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $fallback) {
        return $fallback
    }

    throw "MSBuild not found."
}

function Resolve-NuGet {
    $cmd = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $nuget = Join-Path $solutionDir "tools\nuget.exe"
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Path (Split-Path $nuget) -Force | Out-Null
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
    }

    return $nuget
}

function Resolve-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return ""
}

$msbuild = Resolve-MsBuild
$nuget = Resolve-NuGet
$iscc = Resolve-Iscc

Write-Host "Using MSBuild: $msbuild"
Write-Host "Using NuGet:   $nuget"
Write-Host "Version:       $Version"

& $nuget restore $solutionFile -NonInteractive
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed with exit code $LASTEXITCODE."
}

& $msbuild $solutionFile /p:Configuration=Release /p:Platform="Any CPU" /p:ReleaseVersion=$Version /t:Rebuild /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path (Join-Path $releaseDir "FastMediaSorter_LITE.exe"))) {
    throw "Release executable not found in $releaseDir"
}

Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Get-ChildItem $releaseDir -Recurse -File |
    Where-Object { $_.Extension -notin ".pdb", ".xml" } |
    ForEach-Object {
        $relative = Resolve-Path -Relative -Path $_.FullName -RelativeBasePath (Resolve-Path $releaseDir).Path
        $destination = Join-Path $stageDir $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item $_.FullName $destination -Force
    }

foreach ($extra in @("README.md", "LICENSE")) {
    $source = Join-Path $solutionDir $extra
    if (Test-Path $source) {
        Copy-Item $source $stageDir -Force
    }
}

& (Join-Path $PSScriptRoot "Prepare-OcrOfflinePayload.ps1") -StageDir $stageDir -IncludeBest

$zipName = "FastMediaSorter-$Version-windows-x64.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
$zipHash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash
"$zipHash  $zipName" | Out-File -FilePath ($zipPath + ".sha256") -Encoding ascii
Write-Host "ZIP:    $zipPath"
Write-Host "SHA256: $zipHash"

if (Test-Path $iscc) {
    $stageAbs = (Resolve-Path $stageDir).Path
    $distAbs = (Resolve-Path $distDir).Path
    & $iscc "/DVersion=$Version" "/DSourceDir=$stageAbs" "/O$distAbs" (Join-Path $solutionDir "installer\FastMediaSorter.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    $setupName = "FastMediaSorter-$Version-windows-x64-setup.exe"
    $setupPath = Join-Path $distDir $setupName
    $setupHash = (Get-FileHash -Algorithm SHA256 $setupPath).Hash
    "$setupHash  $setupName" | Out-File -FilePath ($setupPath + ".sha256") -Encoding ascii
    Write-Host "SETUP:  $setupPath"
    Write-Host "SHA256: $setupHash"
} else {
    Write-Warning "Inno Setup was not found at $iscc. ZIP was built, installer was skipped."
}
