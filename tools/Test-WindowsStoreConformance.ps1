<#
.SYNOPSIS
    Reads an MSIX package back and holds it to WINDOWS-STORE rules 1-5 and 17 (conformance rung 3).

.DESCRIPTION
    Package-level conformance checks for the WINDOWS-STORE contract, read out of the packed archive and
    not out of the sources that made it:
    - Rule 1: Identity is reserved once, supplied in full
    - Rule 2: The upload is unsigned (no AppxSignature.p7x)
    - Rule 3: The identity version is the mechanical remap of the release stamp
    - Rule 4: THIRD-PARTY-NOTICES.txt is inside the packed archive
    - Rule 5: Container declarations replace HKCU writes, firewall steps, etc.
    - Rule 17: The page says what the product does (runFullTrust justification)

    Runs in publishing\msix\build-msix.ps1 on the package it has just packed and before it is signed
    (a -SelfSign package carries a signature on purpose and is a local test), so a package that breaks a
    rule is never offered. The expected version is the remap the builder derived; the independent derivation
    from the release stamp is tools\Test-ArtifactVersion.ps1 -Msix, which the build also runs.

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

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every rule holds;
      1 - the package was read and a rule does not hold, or it is not a readable package;
      2 - could not verify: the package does not exist or an -Expected* value was not given.
    The last line is the verdict, `Test-WindowsStoreConformance: <WORD> (..)`.

.EXAMPLE
    Test-WindowsStoreConformance.ps1 -MsixPath "publishing\msix\dist\FastMediaSorter_LITE-26.920.1206.0-x64.msix" `
        -ExpectedVersion "26.920.1206.0" `
        -ExpectedIdentityName "SZA.FastMediaSorterLITE" `
        -ExpectedPublisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" `
        -ExpectedPublisherDisplayName "SZA"
#>

[CmdletBinding()]
param(
    [string]$MsixPath = '',
    [string]$ExpectedVersion = '',
    [string]$ExpectedIdentityName = '',
    [string]$ExpectedPublisher = '',
    [string]$ExpectedPublisherDisplayName = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-WindowsStoreConformance' 2 'the check itself failed'
}

Write-Subject 'Test-WindowsStoreConformance' "MSIX package '$MsixPath', WINDOWS-STORE rules 1-5 and 17"

# What the caller owes: a package and the four values the Store reserved. Not given = nothing to judge against.
$owed = [ordered]@{
    MsixPath                     = $MsixPath
    ExpectedVersion              = $ExpectedVersion
    ExpectedIdentityName         = $ExpectedIdentityName
    ExpectedPublisher            = $ExpectedPublisher
    ExpectedPublisherDisplayName = $ExpectedPublisherDisplayName
}
$notGiven = @($owed.Keys | Where-Object { [string]::IsNullOrWhiteSpace($owed[$_]) })
if ($notGiven.Count -gt 0) {
    Exit-Verdict 'Test-WindowsStoreConformance' 2 "not given: $(($notGiven | ForEach-Object { "-$_" }) -join ', ')"
}
if (-not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) {
    Exit-Verdict 'Test-WindowsStoreConformance' 2 "MSIX package not found: $MsixPath"
}

# Result tracking
$script:results = @()

function Add-Result([string]$Rule, [string]$Message, $Pass) {
    $boolPass = [bool]$Pass
    $script:results += @{ Rule = $Rule; Pass = $boolPass; Message = $Message }
    $status = if ($boolPass) { 'PASS' } else { 'FAIL' }
    $color = if ($boolPass) { 'Green' } else { 'Red' }
    Write-Host "  [$Rule] $status : $Message" -ForegroundColor $color
}

# The archive's file names and its manifest text, read in one open. A package that is not a readable ZIP
# is a defective package (1), not an unrun check.
function Read-MsixArchive([string]$PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    } catch {
        Write-Host "  cannot read the package as a ZIP archive: $($_.Exception.Message)"
        Exit-Verdict 'Test-WindowsStoreConformance' 1 'the file is not a readable MSIX package'
    }
    try {
        $files = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object { $_.FullName })
        $manifest = $null
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
        if ($entry) {
            $reader = [System.IO.StreamReader]::new($entry.Open())
            try { $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        return @{ Files = $files; Manifest = $manifest }
    } finally { $zip.Dispose() }
}

function Get-IdentityFromManifest([string]$ManifestXml) {
    if ($ManifestXml -match '<Identity\s+Name="([^"]+)"\s+Publisher="([^"]+)"\s+Version="([^"]+)"') {
        return @{ Name = $Matches[1]; Publisher = $Matches[2]; Version = $Matches[3] }
    }
    return $null
}

function Get-PublisherDisplayNameFromManifest([string]$ManifestXml) {
    if ($ManifestXml -match '<PublisherDisplayName>([^<]+)</PublisherDisplayName>') { return $Matches[1].Trim() }
    return $null
}

# Every declared capability name (rescap:, uap: and the unprefixed general ones), not only the first.
function Get-ManifestCapabilities([string]$ManifestXml) {
    $caps = @()
    if ($ManifestXml -match '<Capabilities>([\s\S]*?)</Capabilities>') {
        foreach ($m in [regex]::Matches($Matches[1], '<(?:\w+:)?Capability\s+Name="([^"]+)"')) { $caps += $m.Groups[1].Value }
    }
    return $caps
}

function Get-DeclaredExtensions([string]$ManifestXml) {
    $patterns = @(
        'uap:Extension\s+Category="windows\.fileTypeAssociation"',
        'uap5:Extension\s+Category="windows\.startupTask"',
        'desktop2:Extension\s+Category="windows\.firewallRules"'
    )
    $exts = @()
    foreach ($pattern in $patterns) {
        if ($ManifestXml -match $pattern) { $exts += $Matches[0] }
    }
    return $exts
}

# --- Main ---

Write-Host "Testing WINDOWS-STORE contract conformance for: $MsixPath" -ForegroundColor Cyan
Write-Host ""

$archive = Read-MsixArchive $MsixPath
$files = $archive.Files
$manifestXml = $archive.Manifest
if (-not $manifestXml) {
    Add-Result -Rule '1,2,3' -Message 'Could not read AppxManifest.xml from MSIX' -Pass $false
    Exit-Verdict 'Test-WindowsStoreConformance' 1 'the package carries no AppxManifest.xml'
}

# Parse manifest
$identity = Get-IdentityFromManifest $manifestXml
$displayName = Get-PublisherDisplayNameFromManifest $manifestXml
$capabilities = Get-ManifestCapabilities $manifestXml
$extensions = Get-DeclaredExtensions $manifestXml

# Rule 1: Identity is reserved once, supplied in full
Add-Result -Rule '1' -Message "Identity Name = $($identity.Name) (expected: $ExpectedIdentityName)" -Pass ($identity -and $identity.Name -eq $ExpectedIdentityName)
Add-Result -Rule '1' -Message "Identity Publisher = $($identity.Publisher) (expected: $ExpectedPublisher)" -Pass ($identity -and $identity.Publisher -eq $ExpectedPublisher)
Add-Result -Rule '1' -Message "PublisherDisplayName = $displayName (expected: $ExpectedPublisherDisplayName)" -Pass ($displayName -eq $ExpectedPublisherDisplayName)

# Rule 2: The upload is unsigned; a signed package is a local test
$hasSignature = $files | Where-Object { $_ -eq 'AppxSignature.p7x' } | Select-Object -First 1
Add-Result -Rule '2' -Message 'No AppxSignature.p7x found (unsigned package for Store upload)' -Pass (-not $hasSignature)

# Rule 3: The identity version is the mechanical remap of the release stamp
Add-Result -Rule '3' -Message "Identity Version = $($identity.Version) (expected: $ExpectedVersion)" -Pass ($identity -and $identity.Version -eq $ExpectedVersion)

# Rule 4: THIRD-PARTY-NOTICES.txt is inside the packed archive
$hasNotices = $files | Where-Object { $_ -eq 'THIRD-PARTY-NOTICES.txt' } | Select-Object -First 1
Add-Result -Rule '4' -Message "THIRD-PARTY-NOTICES.txt $(if ($hasNotices) { 'present' } else { 'MISSING' })" -Pass $hasNotices

# Rule 5: A product that ships both packaged and unpackaged does not rely on what a package container hides
$hasStartupTask = $extensions | Where-Object { $_ -match 'startupTask' } | Select-Object -First 1
$hasFirewallRules = $extensions | Where-Object { $_ -match 'firewallRules' } | Select-Object -First 1
$hasFileTypeAssoc = $extensions | Where-Object { $_ -match 'fileTypeAssociation' } | Select-Object -First 1
$hasRunFullTrust = $capabilities | Where-Object { $_ -eq 'runFullTrust' } | Select-Object -First 1

Add-Result -Rule '5' -Message 'windows.startupTask declared (replaces HKCU\Run)' -Pass $hasStartupTask
Add-Result -Rule '5' -Message 'windows.firewallRules declared (replaces netsh)' -Pass $hasFirewallRules
Add-Result -Rule '5' -Message 'windows.fileTypeAssociation declared (replaces HKCU\Classes)' -Pass $hasFileTypeAssoc

# Rule 5 additional: Square310x310Logo must be declared together with Wide310x150Logo
$hasSquare310 = $manifestXml -match 'Square310x310Logo'
$hasWide310 = $manifestXml -match 'Wide310x150Logo'
Add-Result -Rule '5' -Message 'Square310x310Logo and Wide310x150Logo consistency (neither declared = OK)' -Pass (-not ($hasSquare310 -xor $hasWide310))

# Rule 17: The page says what the product does
Add-Result -Rule '17' -Message 'runFullTrust capability declared (full-trust desktop app)' -Pass $hasRunFullTrust

# Summary
Write-Host ""
$passCount = @($script:results | Where-Object { $_.Pass }).Count
$failed = @($script:results | Where-Object { -not $_.Pass })
$leaf = Split-Path $MsixPath -Leaf

if ($failed.Count -gt 0) {
    Write-Host 'FAILED checks:' -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  [$($_.Rule)] $($_.Message)" -ForegroundColor Red }
    Exit-Verdict 'Test-WindowsStoreConformance' 1 "$($failed.Count) of $($script:results.Count) checks failed on ${leaf}: rule(s) $((($failed | ForEach-Object { $_.Rule }) | Select-Object -Unique) -join ', ')"
}
Exit-Verdict 'Test-WindowsStoreConformance' 0 "$passCount checks held on $leaf (rules 1-5 and 17)"
