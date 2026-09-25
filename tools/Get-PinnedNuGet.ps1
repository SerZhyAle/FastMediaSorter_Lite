<#
.SYNOPSIS
    Returns the path of the nuget.exe the local build scripts run.

.DESCRIPTION
    A nuget.exe on PATH wins - that one is the developer's own install. Otherwise the
    script uses tools\nuget.exe, downloading it first when it is missing.

    The download is PINNED: a versioned URL and its SHA-256. It used to come from
    ".../latest/nuget.exe", unverified, and was then executed - so whatever that URL
    served on the day was what restored the packages that went into a release. A copy
    that does not match the pin is refused, never run: delete tools\nuget.exe to fetch
    the pinned one again. Moving to a newer nuget.exe means changing BOTH values below.

    Shared by build.ps1, tools\Build-Installer.ps1, tools\Build-OfflineRelease.ps1 and
    publishing\msix\build-msix.ps1, so the pin exists exactly once. CI does not use it
    (release.yml installs nuget through NuGet/setup-nuget).
#>
$ErrorActionPreference = "Stop"

$NuGetVersion = "7.6.0"
$NuGetSha256  = "751EE5E79481626A428C1241DC7F94BCA2739B32588E669715BC5FB54D8FB8A2"

$onPath = Get-Command nuget.exe -ErrorAction SilentlyContinue
if ($onPath) { return $onPath.Source }

$nuget = Join-Path $PSScriptRoot "nuget.exe"
if (-not (Test-Path -LiteralPath $nuget)) {
    $tmp = $nuget + ".part"
    Write-Host "Downloading nuget.exe $NuGetVersion (pinned).."
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/v$NuGetVersion/nuget.exe" -OutFile $tmp -UseBasicParsing
    $got = (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash
    if ($got -ne $NuGetSha256) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        throw "Downloaded nuget.exe $NuGetVersion has SHA-256 $got, expected $NuGetSha256 - refused, not run."
    }
    Move-Item -LiteralPath $tmp -Destination $nuget -Force
}

$have = (Get-FileHash -Algorithm SHA256 -LiteralPath $nuget).Hash
if ($have -ne $NuGetSha256) {
    throw "$nuget has SHA-256 $have, which is not the pinned nuget.exe $NuGetVersion ($NuGetSha256) - refused, not run. Delete it to download the pinned copy."
}
return $nuget
