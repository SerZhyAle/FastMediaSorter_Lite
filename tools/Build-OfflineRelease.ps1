param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm"),
    [switch]$NoClean
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
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
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

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $baseResolved = (Resolve-Path $BasePath).Path.TrimEnd('\') + '\'
    $targetResolved = (Resolve-Path $TargetPath).Path
    $baseUri = New-Object System.Uri($baseResolved)
    $targetUri = New-Object System.Uri($targetResolved)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', '\'))
}

$msbuild = Resolve-MsBuild
$nuget = Resolve-NuGet
$iscc = Resolve-Iscc

Write-Host "Using MSBuild: $msbuild"
Write-Host "Using NuGet:   $nuget"
Write-Host "Version:       $Version"

# Housekeeping: prune superseded stage\ trees, old dist\ artifacts and temp
# files before packaging (keeps this run's version). Leaves bin\Release / obj.
if (-not $NoClean) {
    & (Join-Path $PSScriptRoot "Clean-Build.ps1") -Stage -Dist -Temp -KeepVersion $Version
}

& $nuget restore $solutionFile -NonInteractive
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed with exit code $LASTEXITCODE."
}

& $msbuild $solutionFile /p:Configuration=Release /p:Platform="Any CPU" /p:ReleaseVersion=$Version /t:Rebuild /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

# The viewer ships as two exes (see CLAUDE.md "Project identity"): msbuild yields
# the net48 FastMediaSorter_x86.exe here; the .NET 10 mainline
# FastMediaSorter_LITE.exe comes from the dotnet publish staged further down.
if (-not (Test-Path (Join-Path $releaseDir "FastMediaSorter_x86.exe"))) {
    throw "Release executable not found in $releaseDir"
}

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
    if (Test-Path $source) {
        Copy-Item $source $stageDir -Force
    }
}

# The .NET 10 x64 mainline viewer (mirror of release.yml). msbuild above only
# produced the net48 x86 sibling; this is the exe that carries the frozen name and
# replaces the installed one. Self-contained single-file, so no runtime install.
# Only the exe is staged - the support trees it needs (libvlc\win-x64, x64\
# tesseract natives, flags\) are already in the staged bin\Release tree.
$modernProj = Join-Path $solutionDir "src\Modern\FastMediaSorter.Modern.vbproj"
$modernOut  = Join-Path $stageDir "modern-publish-tmp"
Write-Host "Publishing the .NET 10 x64 viewer (self-contained single-file).."
& dotnet publish $modernProj -c Release -r win-x64 -p:ReleaseVersion=$Version -o $modernOut -v minimal --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (modern viewer) failed with exit code $LASTEXITCODE."
}
$modernExe = Join-Path $modernOut "FastMediaSorter_LITE.exe"
if (-not (Test-Path $modernExe)) {
    throw "Modern viewer exe not found at $modernExe after publish."
}
Copy-Item $modernExe $stageDir -Force
Remove-Item -LiteralPath $modernOut -Recurse -Force -ErrorAction SilentlyContinue

# Android Folder Share payload (mirror of release.yml): publish the Companion app
# (Share Manager, net10) as a self-contained single-file exe next to the LITE exe,
# and stage the committed Go worker under companion\. Both feed the installer's
# `share` component and the portable ZIP.
$companionProj = Join-Path $solutionDir "src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj"
$companionOut  = Join-Path $stageDir "companion-publish-tmp"
Write-Host "Publishing Companion (Share Manager, net10 self-contained).."
& dotnet publish $companionProj -c Release -r win-x64 -o $companionOut -v minimal --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (Companion) failed with exit code $LASTEXITCODE."
}
$companionExe = Join-Path $companionOut "FastMediaSorterCompanion.exe"
if (-not (Test-Path $companionExe)) {
    throw "Companion exe not found at $companionExe after publish."
}
Copy-Item $companionExe $stageDir -Force
Remove-Item -LiteralPath $companionOut -Recurse -Force -ErrorAction SilentlyContinue

$workerSrc = Join-Path $solutionDir "payload\companion"
$worker    = Join-Path $workerSrc "fms-share-worker.exe"
if (-not (Test-Path $worker)) {
    throw "SFTP worker not found at $worker (should be committed in the repo)."
}
$stageCompanion = Join-Path $stageDir "companion"
New-Item -ItemType Directory -Path $stageCompanion -Force | Out-Null
Copy-Item (Join-Path $workerSrc "*") $stageCompanion -Recurse -Force

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
    & $iscc "/DVersion=$Version" "/DSourceDir=$stageAbs" "/O$distAbs" (Join-Path $solutionDir "publishing\installer\FastMediaSorter.iss")
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
