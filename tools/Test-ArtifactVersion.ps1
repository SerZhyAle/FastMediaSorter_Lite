<#
.SYNOPSIS
    Proves that built artifacts carry the version they are judged at (contract BUILD-EVIDENCE rule 2).

.DESCRIPTION
    Reads the version resource of every file named in -Path - the viewers, the Share Manager, the exchange
    server, the Inno setup.exe files - and compares its four numeric parts with -Expect. With -Msix it reads
    the Identity Version out of each MSIX package instead and compares it with the Store remap of -Expect
    (WINDOWS-STORE rule 3, PACKAGE-VERSIONING W2).

    Only the artifact can answer this. A stale exe agrees with every check that reads the version out of
    the source, because the source is exactly what it was built from; the failures this exists for are an
    exe left over from an earlier build (the previous stamp) and a publish that was passed no version at
    all (1.0.0.0, the SDK default).

    The comparison is numeric, never textual: the resource keeps the stamp 26.7.30.0108 as the parts
    26.7.30.108, and the string field is whatever the compiler copied. A stamp whose HHmm is below 1000
    is exactly the case a textual compare gets wrong.

    Runs where the artifacts are produced (build.ps1, on the exes it just built) and in the release
    preflight (tools\Release.ps1, on the staged package and both setup.exe files) - see
    tools\check-placement.jsonl. publishing\msix\build-msix.ps1 runs the -Msix mode on the package it packed.

.PARAMETER Expect
    The stamp the build was given, YY.M.D.HHmm (the tag without its "v"). Also in -Msix mode: the remap
    YY.(M*100+D).HHmm.0 is derived here, so the check does not trust the builder's own arithmetic.

.PARAMETER Path
    The files to judge. `pwsh -File` binds "a,b" as one string, so commas separate files here too.

.PARAMETER Msix
    Judge each -Path as an MSIX package: its AppxManifest.xml Identity Version must equal the remap of -Expect.
    Identity, signature, notices and the container declarations are tools\Test-WindowsStoreConformance.ps1's.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every file carries -Expect;
      1 - a file carries another version, or no version resource at all (or no readable Identity Version);
      2 - could not verify: -Expect is not a YY.M.D.HHmm stamp, no file was named, or a named file
          does not exist.
    The last line is the verdict, `Test-ArtifactVersion: <WORD> (..)`.

.EXAMPLE
    .\tools\Test-ArtifactVersion.ps1 -Expect 26.9.25.1700 -Path bin\Release\FastMediaSorter_LITE.exe,bin\Release\FastMediaSorter_x86.exe

.EXAMPLE
    .\tools\Test-ArtifactVersion.ps1 -Expect 26.9.20.1206 -Msix -Path publishing\msix\dist\FastMediaSorter_LITE-26.920.1206.0-x64.msix
#>
[CmdletBinding()]
param(
    [string]$Expect = '',
    [string[]]$Path = @(),
    [switch]$Msix
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-ArtifactVersion' 2 'the check itself failed'
}

# The stamp YY.M.D.HHmm as four integers: 0108 -> 108, which is what the version resource keeps.
function ConvertTo-StampParts([string]$Stamp) {
    @($Stamp.Split('.') | ForEach-Object { [int]$_ })
}

# WINDOWS-STORE rule 3 / PACKAGE-VERSIONING W2: Major = YY, Minor = M*100+D, Build = HHmm, Revision = 0.
function Resolve-ExpectedMsixVersion([string]$Stamp) {
    $p = ConvertTo-StampParts $Stamp
    @($p[0], ($p[1] * 100 + $p[2]), $p[3], 0)
}

# The Identity Version of an MSIX, read out of the packed archive, as four integers - or $null.
function Get-MsixIdentityParts([string]$PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
        if (-not $entry) { return $null }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    if ($text -notmatch '<Identity\b[^>]*\bVersion="(\d+(?:\.\d+){3})"') { return $null }
    @($Matches[1].Split('.') | ForEach-Object { [int]$_ })
}

$files = @($Path | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$kind = if ($Msix) { 'MSIX package(s), Identity Version' } else { 'file(s), version resource' }
Write-Subject 'Test-ArtifactVersion' "$($files.Count) $kind, expected stamp '$Expect'"

if ($Expect -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    Exit-Verdict 'Test-ArtifactVersion' 2 "-Expect '$Expect' is not a YY.M.D.HHmm stamp"
}
if ($files.Count -eq 0) {
    Exit-Verdict 'Test-ArtifactVersion' 2 'no file named in -Path'
}
$missing = @($files | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    $missing | ForEach-Object { Write-Host "  missing: $_" }
    Exit-Verdict 'Test-ArtifactVersion' 2 "$($missing.Count) of $($files.Count) file(s) not found"
}

$want = if ($Msix) { Resolve-ExpectedMsixVersion $Expect } else { ConvertTo-StampParts $Expect }
$wantText = $want -join '.'
$wrong = @()
foreach ($file in $files) {
    $leaf = Split-Path $file -Leaf
    if ($Msix) {
        $got = Get-MsixIdentityParts $file
        if ($null -eq $got) {
            Write-Host "  $leaf has no readable AppxManifest.xml Identity Version"
            $wrong += $leaf
            continue
        }
    } else {
        $info = (Get-Item -LiteralPath $file).VersionInfo
        $got = @($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
        if (($got | Measure-Object -Sum).Sum -eq 0) {
            Write-Host "  $leaf carries no version resource"
            $wrong += $leaf
            continue
        }
    }
    $gotText = $got -join '.'
    if ($gotText -ne $wantText) {
        $hint = if ($gotText -eq '1.0.0.0') { ' - the SDK default: the build passed it no version' } else { '' }
        Write-Host "  $leaf carries $gotText, not $wantText$hint"
        $wrong += $leaf
    } else {
        Write-Host "  $leaf carries $wantText"
    }
}

if ($wrong.Count -gt 0) {
    Exit-Verdict 'Test-ArtifactVersion' 1 "$($wrong.Count) of $($files.Count) file(s) not at $Expect`: $($wrong -join ', ')"
}
Exit-Verdict 'Test-ArtifactVersion' 0 "$($files.Count) file(s) at $Expect"
