<#
.SYNOPSIS
    Reproducible fixture generator for specification 262 file manager acceptance.
.DESCRIPTION
    Creates reproducible local fixture hierarchies beneath an operation-owned directory:
    - 5,000-file bulk folder for virtual list and responsiveness checks (G1)
    - Typed activation samples (media, documents, blocked scripts, archives, containers) (G2)
    - Local command trees (mixed files, folders, hidden files, collision sources) (G3)
    - Archive creation and extraction fixtures (G5)
    Computes SHA-256 manifest for all generated items.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$FixtureRoot,
    [switch]$SkipBulk
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $FixtureRoot)) {
    [void](New-Item -ItemType Directory -Path $FixtureRoot -Force)
}

$manifest = [System.Collections.Generic.List[object]]::new()
$sha256 = [System.Security.Cryptography.SHA256]::Create()

function Write-FixtureFile([string]$relativePath, [byte[]]$bytes) {
    $full = Join-Path $FixtureRoot $relativePath
    $parent = Split-Path $full -Parent
    if (-not (Test-Path -LiteralPath $parent)) {
        [void](New-Item -ItemType Directory -Path $parent -Force)
    }
    [System.IO.File]::WriteAllBytes($full, $bytes)
    $hash = [BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    $manifest.Add([pscustomobject]@{
        Path = $relativePath.Replace('\', '/')
        Length = $bytes.Length
        Sha256 = $hash
    })
    return $full
}

function Write-TextFile([string]$relativePath, [string]$content) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
    return Write-FixtureFile $relativePath $bytes
}

# 1. Gate 1 Fixtures: Navigation & Bulk
$g1Dir = Join-Path $FixtureRoot 'g1_nav'
[void](New-Item -ItemType Directory -Path (Join-Path $g1Dir 'empty_folder') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $g1Dir 'sub_alpha') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $g1Dir 'sub_beta') -Force)

[void](Write-TextFile 'g1_nav/sub_alpha/item1.txt' 'alpha item 1')
[void](Write-TextFile 'g1_nav/sub_beta/item2.txt' 'beta item 2')

if (-not $SkipBulk) {
    $bulkDir = Join-Path $g1Dir 'bulk_5000'
    [void](New-Item -ItemType Directory -Path $bulkDir -Force)
    $dummyBytes = [System.Text.Encoding]::ASCII.GetBytes("FMS-BULK-DATA`r`n")
    for ($i = 1; $i -le 5000; $i++) {
        $name = "file_{0:D4}.txt" -f $i
        $target = Join-Path $bulkDir $name
        [System.IO.File]::WriteAllBytes($target, $dummyBytes)
    }
    $manifest.Add([pscustomobject]@{
        Path = 'g1_nav/bulk_5000/'
        Length = 5000
        Sha256 = 'bulk-5000-files-generated'
    })
}

# 2. Gate 2 Fixtures: Typed Activation
# Minimal valid JPEG header (SOI + APP0 + EOI)
$jpgBytes = [byte[]]@(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00, 0xFF, 0xD9)
[void](Write-FixtureFile 'g2_activation/sample_image.jpg' $jpgBytes)

# Minimal MP4 signature (ftyp)
$mp4Bytes = [byte[]]@(0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x00, 0x01, 0x69, 0x73, 0x6F, 0x6D)
[void](Write-FixtureFile 'g2_activation/sample_video.mp4' $mp4Bytes)

# Minimal PDF header
$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n%EOF`n")
[void](Write-FixtureFile 'g2_activation/sample_doc.pdf' $pdfBytes)

[void](Write-TextFile 'g2_activation/readme.txt' 'Standard readable plain text document.')
[void](Write-TextFile 'g2_activation/data.xyz123' 'Unknown binary or custom data extension.')

# Blocked executable and script samples
$dosHeader = [byte[]]@(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00) # MZ
[void](Write-FixtureFile 'g2_activation/blocked_app.exe' $dosHeader)
[void](Write-TextFile 'g2_activation/blocked_script.bat' '@echo off`r`necho test`r`n')
[void](Write-TextFile 'g2_activation/blocked_ps.ps1' 'Write-Host "test"`r`n')
[void](Write-TextFile 'g2_activation/blocked_link.lnk' 'dummy-shortcut')

# Secure container sample (.fd-sec header magic: "FDSEC" or container mark)
$fdSecBytes = [System.Text.Encoding]::ASCII.GetBytes("FDSEC-CONTAINER-ENCRYPTED-PAYLOAD-V1")
[void](Write-FixtureFile 'g2_activation/protected_vault.fd-sec' $fdSecBytes)

# 3. Gate 3 Fixtures: Local Commands & Hierarchy
[void](Write-TextFile 'g3_local/folder_a/doc_original.txt' 'Initial content for move/copy/rename')
[void](Write-TextFile 'g3_local/folder_a/case_rename.txt' 'Testing case-only rename')
[void](Write-TextFile 'g3_local/folder_a/to_delete.txt' 'Disposable item for recycle bin and permanent delete')
[void](Write-TextFile 'g3_local/folder_a/hidden_child.txt' 'Hidden file content')
$hiddenFile = Join-Path $FixtureRoot 'g3_local/folder_a/hidden_child.txt'
[System.IO.File]::SetAttributes($hiddenFile, [System.IO.FileAttributes]::Hidden)

[void](New-Item -ItemType Directory -Path (Join-Path $FixtureRoot 'g3_local/empty_child') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $FixtureRoot 'g3_local/folder_b') -Force)

# Collision targets
[void](Write-TextFile 'g3_local/collision/source/clash.txt' 'Newer version of file (source)')
[void](Write-TextFile 'g3_local/collision/target/clash.txt' 'Existing version of file (destination)')

# 4. Gate 5 Fixtures: Archives
[void](Write-TextFile 'g5_archive/source_files/file_one.txt' 'Archive candidate file 1')
[void](Write-TextFile 'g5_archive/source_files/file_two.txt' 'Archive candidate file 2')
[void](New-Item -ItemType Directory -Path (Join-Path $FixtureRoot 'g5_archive/source_files/empty_sub') -Force)

# Write a valid small zip file using .NET ZipArchive
$safeZipPath = Join-Path $FixtureRoot 'g5_archive/safe_sample.zip'
if (Test-Path -LiteralPath $safeZipPath) { Remove-Item -LiteralPath $safeZipPath -Force }
$zipStream = [System.IO.File]::Create($safeZipPath)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry1 = $archive.CreateEntry("inner_file.txt")
        $writer1 = [System.IO.StreamWriter]::new($entry1.Open())
        try {
            $writer1.Write("Content inside safe archive")
        } finally {
            $writer1.Dispose()
        }
        $entry2 = $archive.CreateEntry("sub/nested.txt")
        $writer2 = [System.IO.StreamWriter]::new($entry2.Open())
        try {
            $writer2.Write("Nested file inside archive")
        } finally {
            $writer2.Dispose()
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    $zipStream.Dispose()
}

$safeZipBytes = [System.IO.File]::ReadAllBytes($safeZipPath)
$manifest.Add([pscustomobject]@{
    Path = 'g5_archive/safe_sample.zip'
    Length = $safeZipBytes.Length
    Sha256 = [BitConverter]::ToString($sha256.ComputeHash($safeZipBytes)).Replace('-', '').ToLowerInvariant()
})

# Corrupt zip (header with corrupted trailer)
$corruptZipBytes = [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00, 0xDE, 0xAD, 0xBE, 0xEF)
[void](Write-FixtureFile 'g5_archive/corrupt_readback.zip' $corruptZipBytes)

$sha256.Dispose()

$manifestJson = $manifest | ConvertTo-Json -Depth 4
$manifestPath = Join-Path $FixtureRoot 'fixture-manifest.json'
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.Encoding]::UTF8)

Write-Host "Fixture generation complete: $($manifest.Count) entries at $FixtureRoot"
return $manifestPath
