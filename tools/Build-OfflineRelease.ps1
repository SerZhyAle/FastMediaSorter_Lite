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

# ".log" is not cosmetic: bin\Release is where the app is RUN from, so AppFileLogger
# leaves its current.log right here, full of this machine's paths. Shipped, it lands in
# {app} and the user's own log starts with our sessions - which the new "send the logs
# to the author" button would then mail back to us. The MSIX packager already filtered
# it; the ZIP, the installer and release.yml did not.
Get-ChildItem $releaseDir -Recurse -File |
    Where-Object { $_.Extension -notin ".pdb", ".xml", ".log" } |
    ForEach-Object {
        $relative = Get-RelativePath -BasePath $releaseDir -TargetPath $_.FullName
        $destination = Join-Path $stageDir $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item $_.FullName $destination -Force
    }

foreach ($extra in @("README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt")) {
    $source = Join-Path $solutionDir $extra
    if (Test-Path $source) {
        Copy-Item $source $stageDir -Force
    }
}

# The .NET 10 x64 mainline viewer (mirror of release.yml). msbuild above only
# produced the net48 x86 sibling; this is the exe that carries the frozen name and
# replaces the installed one. Self-contained single-file, so no runtime install.
# The support trees it needs (libvlc\win-x64, x64\ tesseract natives, flags\) are
# already in the staged bin\Release tree - but the publish output itself is NOT just
# the exe: IncludeNativeLibrariesForSelfExtract is false, so Magick.Native-Q8-x64.dll
# (the AVIF/HEIC/HEIF decoder) stays loose beside it. Staging the exe by name dropped
# it, shipping a viewer that could not open the iPhone photos it advertises.
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
Get-ChildItem $modernOut -File |
    Where-Object { $_.Extension -notin ".pdb", ".xml" } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $stageDir $_.Name) -Force }
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

    # SERVER edition - the always-on Folder Share Server (a Windows service). A separate
    # product entry with its own AppId, ARP name and winget package, so it is a separate
    # asset rather than a mode of the one above.
    #
    # It is built HERE, and not only by build.ps1, because this script is what
    # tools\Release.ps1 runs as the free local check before the billable tag push: an
    # asset the release workflow builds but the pre-flight does not is an asset whose
    # first proof of compiling is a paid, red CI job.
    #
    # Mirror of release.yml's "Build Server edition installer" step, including its lean
    # stage - the Share Manager, the Go worker and the two elevated helper scripts only.
    # No viewer, no VLC codecs, no OCR models: a headless server runs none of them, and
    # reusing the stage above would ship hundreds of megabytes it never opens.
    $serverStage = Join-Path $solutionDir "stage\FastMediaSorter-$Version-windows-x64-server"
    if (Test-Path $serverStage) {
        Remove-Item -LiteralPath $serverStage -Recurse -Force
    }
    New-Item -ItemType Directory -Path $serverStage -Force | Out-Null

    foreach ($extra in @("README.md", "LICENSE", "THIRD-PARTY-NOTICES.txt")) {
        $source = Join-Path $solutionDir $extra
        if (Test-Path $source) {
            Copy-Item $source $serverStage -Force
        }
    }

    # Both come from the staged User tree, already published above - the two editions
    # must ship the SAME Share Manager build and the SAME worker binary, or a phone
    # paired against one could meet a different protocol on the other.
    Copy-Item (Join-Path $stageDir "FastMediaSorterCompanion.exe") $serverStage -Force
    $serverCompanion = Join-Path $serverStage "companion"
    New-Item -ItemType Directory -Path $serverCompanion -Force | Out-Null
    Copy-Item (Join-Path $stageCompanion "*") $serverCompanion -Recurse -Force

    $serverStageAbs = (Resolve-Path $serverStage).Path
    & $iscc "/DVersion=$Version" "/DSourceDir=$serverStageAbs" "/O$distAbs" (Join-Path $solutionDir "publishing\installer\FastMediaSorterServer.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup (Server edition) failed with exit code $LASTEXITCODE."
    }

    $serverName = "FastMediaSorter-$Version-windows-x64-server-setup.exe"
    $serverPath = Join-Path $distDir $serverName
    if (-not (Test-Path $serverPath)) {
        throw "Server installer not produced at $serverPath."
    }
    $serverHash = (Get-FileHash -Algorithm SHA256 $serverPath).Hash
    "$serverHash  $serverName" | Out-File -FilePath ($serverPath + ".sha256") -Encoding ascii
    Write-Host "SERVER: $serverPath"
    Write-Host "SHA256: $serverHash"
} else {
    Write-Warning "Inno Setup was not found at $iscc. ZIP was built, both installers were skipped."
}
