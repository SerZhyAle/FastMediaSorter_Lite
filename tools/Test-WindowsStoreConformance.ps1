<#PSScriptInfo
.VERSION 1.0
.GUID 8e2a4fc1-9b23-4d8f-9c7e-298b5c6d4a1e
.AUTHOR SZA
.COMPANYNAME SZA
.COPYRIGHT Copyright (c) SZA
.TAGS Store Windows-Store Contract WINDOWS-STORE Conformance
#>

<#
.SYNOPSIS
    Verifies MSIX package conformance to WINDOWS-STORE contract rules 1-5 and 17.

.DESCRIPTION
    Implements package-level conformance checks for the WINDOWS-STORE contract:
    - Rule 1: Identity is reserved once, supplied in full
    - Rule 2: The upload is unsigned (no AppxSignature.p7x)
    - Rule 3: The identity version is the mechanical remap of the release stamp
    - Rule 4: THIRD-PARTY-NOTICES.txt is inside the packed archive
    - Rule 5: Container declarations replace HKCU writes, firewall steps, etc.
    - Rule 17: The page says what the product does (runFullTrust justification)

.PARAMETER MsixPath
    Path to the MSIX package file to verify.

.PARAMETER ExpectedVersion
    Expected version in Major.Minor.Build.0 format (from release tag).

.PARAMETER ExpectedIdentityName
    Expected Package/Identity/Name value (e.g., "SZA.FastMediaSorterLITE").

.PARAMETER ExpectedPublisher
    Expected Package/Identity/Publisher value (e.g., "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD").

.PARAMETER ExpectedPublisherDisplayName
    Expected Package/Properties/PublisherDisplayName value (e.g., "SZA").

.EXAMPLE
    Test-WindowsStoreConformance.ps1 -MsixPath "publishing\msix\dist\FastMediaSorter_LITE-26.920.1206.0-x64.msix" `
        -ExpectedVersion "26.920.1206.0" `
        -ExpectedIdentityName "SZA.FastMediaSorterLITE" `
        -ExpectedPublisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" `
        -ExpectedPublisherDisplayName "SZA"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$MsixPath,

    [Parameter(Mandatory=$true)]
    [string]$ExpectedVersion,

    [Parameter(Mandatory=$true)]
    [string]$ExpectedIdentityName,

    [Parameter(Mandatory=$true)]
    [string]$ExpectedPublisher,

    [Parameter(Mandatory=$true)]
    [string]$ExpectedPublisherDisplayName
)

$ErrorActionPreference = 'Stop'

# Result tracking
$script:results = @()
$script:allPass = $true

function Add-Result([string]$Rule, [string]$Message, $Pass) {
    # Convert $Pass to boolean - simply coerce to bool
    $boolPass = !!$Pass
    
    $script:results += @{
        Rule = $Rule
        Pass = $boolPass
        Message = $Message
    }
    if (-not $boolPass) { $script:allPass = $false }
    
    $status = if ($boolPass) { "PASS" } else { "FAIL" }
    $color = if ($boolPass) { "Green" } else { "Red" }
    Write-Host "  [$Rule] $status : $Message" -ForegroundColor $color
}

function Get-MsixFileList([string]$MsixPath) {
    if (-not (Test-Path $MsixPath)) { throw "MSIX package not found: $MsixPath" }
    
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    if (-not [System.IO.Compression.ZipFile]) { 
        throw "System.IO.Compression.FileSystem assembly not available"
    }
    
    $files = @()
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
        foreach ($entry in $zip.Entries) {
            if (-not $entry.FullName.EndsWith("/")) {
                $files += $entry.FullName
            }
        }
        return $files
    } catch {
        throw "Cannot read MSIX package as ZIP: $($_.Exception.Message)"
    } finally {
        if ($zip) { $zip.Dispose() }
    }
}

function Get-AppxManifestFromMsix([string]$MsixPath) {
    $files = Get-MsixFileList $MsixPath
    $manifestEntry = $files | Where-Object { $_ -eq "AppxManifest.xml" } | Select-Object -First 1
    if (-not $manifestEntry) { return $null }
    
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    
    $zip = $null
    $stream = $null
    $reader = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "AppxManifest.xml" } | Select-Object -First 1
        if ($entry) {
            $stream = $entry.Open()
            $reader = New-Object System.IO.StreamReader($stream)
            return $reader.ReadToEnd()
        }
    } catch {
        return $null
    } finally {
        if ($reader) { $reader.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($zip) { $zip.Dispose() }
    }
    return $null
}

function Get-IdentityFromManifest([string]$ManifestXml) {
    if (-not $ManifestXml) { return $null }
    
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

function Get-ManifestCapabilities([string]$ManifestXml) {
    $caps = @()
    if ($ManifestXml -match '<Capabilities>([\s\S]*?)</Capabilities>') {
        $capsSection = $matches[1]
        if ($capsSection -match '(?:<|\s)(rescap|desktop2):Capability\s+Name="([^"]+)"') {
            $caps += $matches[2]
        }
    }
    return $caps
}

function Get-DeclaredExtensions([string]$ManifestXml) {
    $exts = @()
    $patterns = @(
        'uap:Extension\s+Category="windows\.fileTypeAssociation"',
        'uap5:Extension\s+Category="windows\.startupTask"',
        'desktop2:Extension\s+Category="windows\.firewallRules"'
    )
    
    foreach ($pattern in $patterns) {
        if ($ManifestXml -match $pattern) {
            $exts += $matches[0]
        }
    }
    return $exts
}

# --- Main ---

Write-Host "Testing WINDOWS-STORE contract conformance for: $MsixPath" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $MsixPath)) {
    Write-Error "MSIX package not found: $MsixPath"
    exit 1
}

# Get file list from MSIX
$files = Get-MsixFileList $MsixPath

# Read manifest
$manifestXml = Get-AppxManifestFromMsix $MsixPath
if (-not $manifestXml) {
    Add-Result -Rule "1,2,3" -Message "Could not read AppxManifest.xml from MSIX" -Pass $false
    exit 1
}

# Parse manifest
$identity = Get-IdentityFromManifest $manifestXml
$displayName = Get-PublisherDisplayNameFromManifest $manifestXml
$capabilities = Get-ManifestCapabilities $manifestXml
$extensions = Get-DeclaredExtensions $manifestXml

# Rule 1: Identity is reserved once, supplied in full
Add-Result -Rule "1" -Message "Identity Name = $($identity.Name) (expected: $ExpectedIdentityName)" -Pass ($identity -and $identity.Name -eq $ExpectedIdentityName)
Add-Result -Rule "1" -Message "Identity Publisher = $($identity.Publisher) (expected: $ExpectedPublisher)" -Pass ($identity -and $identity.Publisher -eq $ExpectedPublisher)
Add-Result -Rule "1" -Message "PublisherDisplayName = $displayName (expected: $ExpectedPublisherDisplayName)" -Pass ($displayName -eq $ExpectedPublisherDisplayName)

# Rule 2: The upload is unsigned; a signed package is a local test
$hasSignature = $files | Where-Object { $_ -eq "AppxSignature.p7x" } | Select-Object -First 1
Add-Result -Rule "2" -Message "No AppxSignature.p7x found (unsigned package for Store upload)" -Pass (-not $hasSignature)

# Rule 3: The identity version is the mechanical remap of the release stamp
Add-Result -Rule "3" -Message "Identity Version = $($identity.Version) (expected: $ExpectedVersion)" -Pass ($identity -and $identity.Version -eq $ExpectedVersion)

# Rule 4: THIRD-PARTY-NOTICES.txt is inside the packed archive
$hasNotices = $files | Where-Object { $_ -eq "THIRD-PARTY-NOTICES.txt" } | Select-Object -First 1
Add-Result -Rule "4" -Message "THIRD-PARTY-NOTICES.txt $(if ($hasNotices) { 'present' } else { 'MISSING' })" -Pass $hasNotices

# Rule 5: A product that ships both packaged and unpackaged does not rely on what a package container hides
$hasStartupTask = $extensions | Where-Object { $_ -match 'startupTask' } | Select-Object -First 1
$hasFirewallRules = $extensions | Where-Object { $_ -match 'firewallRules' } | Select-Object -First 1
$hasFileTypeAssoc = $extensions | Where-Object { $_ -match 'fileTypeAssociation' } | Select-Object -First 1
$hasRunFullTrust = $capabilities | Where-Object { $_ -eq "runFullTrust" } | Select-Object -First 1

Add-Result -Rule "5" -Message "windows.startupTask declared (replaces HKCU\Run)" -Pass $hasStartupTask
Add-Result -Rule "5" -Message "windows.firewallRules declared (replaces netsh)" -Pass $hasFirewallRules
Add-Result -Rule "5" -Message "windows.fileTypeAssociation declared (replaces HKCU\Classes)" -Pass $hasFileTypeAssoc

# Rule 5 additional: Square310x310Logo must be declared together with Wide310x150Logo
$hasSquare310 = $manifestXml -match 'Square310x310Logo'
$hasWide310 = $manifestXml -match 'Wide310x150Logo'
Add-Result -Rule "5" -Message "Square310x310Logo and Wide310x150Logo consistency (neither declared = OK)" -Pass (-not ($hasSquare310 -xor $hasWide310))

# Rule 17: The page says what the product does
Add-Result -Rule "17" -Message "runFullTrust capability declared (full-trust desktop app)" -Pass $hasRunFullTrust

# Summary
Write-Host ""
$passCount = ($script:results | Where-Object { $_.Pass }).Count
$failCount = ($script:results | Where-Object { -not $_.Pass }).Count

# Recalculate allPass based on results
$script:allPass = ($failCount -eq 0)

Write-Host "Results: $passCount PASS, $failCount FAIL" -ForegroundColor $(if ($script:allPass) { "Green" } else { "Red" })

if (-not $script:allPass) {
    Write-Host ""
    Write-Host "FAILED checks:" -ForegroundColor Red
    $script:results | Where-Object { -not $_.Pass } | ForEach-Object { 
        Write-Host "  [$($_.Rule)] $($_.Message)" -ForegroundColor Red
    }
    exit 1
}

Write-Host "All WINDOWS-STORE contract package rules (1-5, 17) PASS." -ForegroundColor Green
exit 0
