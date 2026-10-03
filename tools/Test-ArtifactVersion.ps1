<#PSScriptInfo
.VERSION 1.0
.GUID 13e6b3b1-4b48-4197-a39e-978b2c4e7d59
.AUTHOR SZA
.COMPANYNAME SZA
.COPYRIGHT Copyright (c) SZA
.TAGS Store Windows-Store Contract WINDOWS-STORE

.LINK https://github.com/sza-unified-rules

.EXTERNALMODULEDEPENDENCIES

.REQUIREDSCRIPTS

.EXTERNALSCRIPTDEPENDENCIES

.RELEASENOTES
1.0 - Initial version for WINDOWS-STORE contract conformance
#>

<#
.SYNOPSIS
    Asserts that built artifacts carry the expected version stamp.

.DESCRIPTION
    BUILD-EVIDENCE rule 2 for the WINDOWS-STORE contract: every exe this run produced
    must carry $BuildVersion. Reads the version resource out of the exe's own
    FileVersion, since a stale exe agrees with anything that reads the source.
    Judges only what THIS run built (a -Skip* leaves the previous exe in place on purpose)
    and runs before anything is deployed, so a mismatched exe never reaches a work folder.

    For the MSIX package (rule 3): asserts Identity Name, Publisher, Version,
    PublisherDisplayName, no AppxSignature.p7x, and THIRD-PARTY-NOTICES.txt present.

.PARAMETER Expect
    The version string to expect in every artifact (YY.M.D.HHmm format).

.PARAMETER Path
    One or more paths to exe files or MSIX packages to check.

.PARAMETER Msix
    Switch: treat paths as MSIX packages and verify manifest Identity and contents.

.EXAMPLE
    Test-ArtifactVersion.ps1 -Expect "26.9.20.1206" -Path "bin\Release\FastMediaSorter_LITE.exe"
    
.EXAMPLE
    Test-ArtifactVersion.ps1 -Expect "26.9.20.1206" -Path "publishing\msix\dist\FastMediaSorter_LITE-26.920.1206.0-x64.msix" -Msix

.LINK
    Contracts/windows-store/README.md - WINDOWS-STORE contract
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Expect,

    [Parameter(Mandatory=$true, ValueFromPipeline=$true)]
    [string[]]$Path,

    [switch]$Msix,

    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Resolve-ExpectedMsixVersion([string]$ExeVersion) {
    # Exe is stamped YY.M.D.HHmm. Map to Major.Minor.Build.0:
    #   Major = YY, Minor = M*100+D, Build = HHmm, Revision = 0
    $p = $ExeVersion.Split('.')
    if ($p.Count -lt 4) { throw "Unexpected exe version '$ExeVersion' (want YY.M.D.HHmm)." }
    $yy = [int]$p[0]; $m = [int]$p[1]; $d = [int]$p[2]; $hhmm = [int]$p[3]
    return "$yy.$($m*100+$d).$hhmm.0"
}

function Get-ZipFileList([string]$ZipPath) {
    if (-not (Test-Path $ZipPath)) { throw "MSIX package not found: $ZipPath" }
    $tempDir = Join-Path $env:TEMP ([System.Guid]::NewGuid().Guid)
    try {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        # Use Expand-Archive which can read MSIX (it's a ZIP64 archive)
        Expand-Archive -Path $ZipPath -DestinationPath $tempDir -Force
        $files = Get-ChildItem $tempDir -Recurse -File | ForEach-Object { 
            $rel = $_.FullName.Substring($tempDir.Length + 1)
            if ($rel -eq '') { "." } else { $rel }
        }
        return $files
    } finally {
        if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Get-AppxManifestFromMsix([string]$MsixPath) {
    $files = Get-ZipFileList $MsixPath
    $manifestPath = $files | Where-Object { $_ -eq "AppxManifest.xml" } | Select-Object -First 1
    if (-not $manifestPath) { return $null }
    
    $tempDir = Join-Path $env:TEMP ([System.Guid]::NewGuid().Guid)
    try {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        Expand-Archive -Path $MsixPath -DestinationPath $tempDir -Force
        $manifestContent = Get-Content (Join-Path $tempDir "AppxManifest.xml") -Raw
        return $manifestContent
    } finally {
        if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Get-IdentityFromManifest([string]$ManifestXml) {
    if (-not $ManifestXml) { return $null }
    
    # Simple regex extraction - Identity element
    if ($ManifestXml -match '<Identity\s+Name="([^"]+)"\s+Publisher="([^"]+)"\s+Version="([^"]+)"') {
        return @{
            Name = $matches[1]
            Publisher = $matches[2]
            Version = $matches[3]
        }
    }
    return $null
}

function Get-PublisherDisplayNameFromManifest([string]$ManifestXml) {
    if (-not $ManifestXml) { return $null }
    if ($ManifestXml -match '<PublisherDisplayName>([^<]+)</PublisherDisplayName>') {
        return $matches[1].Trim()
    }
    return $null
}

# --- Main ---

$failed = 0

foreach ($artifact in $Path) {
    $artifact = $artifact.Trim()
    
    if ($Msix) {
        # WINDOWS-STORE contract rule 1: Identity supplied in full
        # rule 2: upload is unsigned (no AppxSignature.p7x)
        # rule 3: identity version is the mechanical remap
        # rule 4: THIRD-PARTY-NOTICES.txt is inside the packed archive
        
        Write-Host "Checking MSIX package: $artifact" -ForegroundColor Cyan
        
        # Rule 2: Check for AppxSignature.p7x
        $files = Get-ZipFileList $artifact
        $hasSignature = $files | Where-Object { $_ -eq "AppxSignature.p7x" } | Select-Object -First 1
        if ($hasSignature) {
            Write-Error "FAIL rule 2: MSIX package has AppxSignature.p7x (signed package - must be unsigned for Store upload): $artifact"
            $failed++
            continue
        }
        if (-not $Quiet) { Write-Host "  rule 2 PASS: no AppxSignature.p7x (unsigned package)" -ForegroundColor Green }
        
        # Rule 4: Check for THIRD-PARTY-NOTICES.txt
        $hasNotices = $files | Where-Object { $_ -eq "THIRD-PARTY-NOTICES.txt" } | Select-Object -First 1
        if (-not $hasNotices) {
            Write-Error "FAIL rule 4: MSIX package missing THIRD-PARTY-NOTICES.txt: $artifact"
            $failed++
            continue
        }
        if (-not $Quiet) { Write-Host "  rule 4 PASS: THIRD-PARTY-NOTICES.txt present" -ForegroundColor Green }
        
        # Rule 1 & 3: Read manifest Identity
        $manifestXml = Get-AppxManifestFromMsix $artifact
        if (-not $manifestXml) {
            Write-Error "FAIL: Could not read AppxManifest.xml from MSIX: $artifact"
            $failed++
            continue
        }
        
        $identity = Get-IdentityFromManifest $manifestXml
        $displayName = Get-PublisherDisplayNameFromManifest $manifestXml
        
        if (-not $identity) {
            Write-Error "FAIL rule 1: Could not parse Identity from manifest: $artifact"
            $failed++
            continue
        }
        
        if (-not $Quiet) { 
            Write-Host "  Identity: Name=$($identity.Name), Publisher=$($identity.Publisher), Version=$($identity.Version)" -ForegroundColor DarkGray
            Write-Host "  PublisherDisplayName: $displayName" -ForegroundColor DarkGray
        }
        
        # Rule 1: Identity must be supplied in full
        if ([string]::IsNullOrEmpty($identity.Name) -or [string]::IsNullOrEmpty($identity.Publisher) -or [string]::IsNullOrEmpty($displayName)) {
            Write-Error "FAIL rule 1: Missing Identity components in manifest: $artifact"
            $failed++
            continue
        }
        if (-not $Quiet) { Write-Host "  rule 1 PASS: Identity supplied in full" -ForegroundColor Green }
        
        # Rule 3: Version must match the expected remap
        # For MSIX, $Expect should already be in Major.Minor.Build.0 format
        if ($identity.Version -ne $Expect) {
            Write-Error "FAIL rule 3: MSIX version '$($identity.Version)' does not match expected '$Expect': $artifact"
            $failed++
            continue
        }
        if (-not $Quiet) { Write-Host "  rule 3 PASS: Version matches $Expect" -ForegroundColor Green }
        
        if (-not $Quiet) { Write-Host "MSIX package PASS: $artifact" -ForegroundColor Green }
    }
    else {
        # Check exe FileVersion
        if (-not (Test-Path $artifact)) {
            Write-Error "FAIL: File not found: $artifact"
            $failed++
            continue
        }
        
        $fileVer = (Get-Item $artifact).VersionInfo.FileVersion
        
        if ($fileVer -ne $Expect) {
            Write-Error "FAIL: File version '$fileVer' does not match expected '$Expect': $artifact"
            $failed++
            continue
        }
        
        if (-not $Quiet) { Write-Host "PASS: $artifact (version $fileVer)" -ForegroundColor Green }
    }
}

if ($failed -gt 0) {
    Write-Host "$failed artifacts FAILED version check." -ForegroundColor Red
    exit $failed
}

if (-not $Quiet) { Write-Host "All $($Path.Count) artifacts carry version $Expect." -ForegroundColor Green }
exit 0
