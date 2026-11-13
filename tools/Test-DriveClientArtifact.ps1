<#
.SYNOPSIS
    Judges whether built Share Manager artifacts carry the Google Drive OAuth client pair - without ever
    printing a value (specification L0306 section 4.4, A1 and A2; delivery design 269 Q269-7).

.DESCRIPTION
    A workflow that names the secrets proves nothing about the exe it produced: an unset secret is an empty
    string, and then the build silently ships "not configured". Only the artifact can answer, so this reads
    the artifact.

    A configured build carries two AssemblyMetadata attributes, FmsDriveClientId and FmsDriveClientSecret
    (src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj emits both or neither). Their serialized
    custom-attribute blobs (prolog 01 00, the key and the value as length-prefixed UTF-8 strings, 00 00) are
    searched for in the bytes of each FastMediaSorterCompanion.exe; the single-file bundle stores its managed
    assemblies uncompressed, so the blobs are readable in place. Each artifact is judged:

      1. the managed code is readable: the type name ExchangeDriveClientConfig is present (else a compressed
         bundle or not a Share Manager build - COULD NOT VERIFY, never a pass);
      2. both values or neither (one alone is a broken delivery); one value per key;
      3. the id has the shape of a Google OAuth client id (<digits>-<token>.apps.googleusercontent.com) and the
         secret does not - a swapped pair is caught here;
      4. -Expect Configured: the pair is present; -Expect Unconfigured: it is absent;
      5. every configured artifact of the run carries the SAME pair (portable, installed and MSIX copies of one
         release must not mix clients), compared by an in-memory SHA-256 that is never printed;
      6. with -CompareEnvironment: that pair equals the one in this shell's FMS_DRIVE_CLIENT_ID and
         FMS_DRIVE_CLIENT_SECRET (the owner's private shell; absent variables are COULD NOT VERIFY).

    Output names files, lengths of nothing, and the words present / absent / matches / differs. No value, no
    hash and no prefix of either is ever printed, logged or returned.

    Input kinds per -Path entry:
      - a FastMediaSorterCompanion.exe (or any exe holding the Share Manager's managed code);
      - a folder (bin\Release, a package stage, an installed or extracted copy): every
        FastMediaSorterCompanion.exe below it;
      - a .zip, .msix or .appx: every FastMediaSorterCompanion.exe entry, read in memory, nothing extracted;
      - an Inno *-setup.exe is compressed and cannot be read: COULD NOT VERIFY with the hint to install it and
        name the installed folder.

    Hand-run (tools\check-placement.jsonl): the expected answer depends on whether the owner supplied the
    client to that build, which no default run can know.

.PARAMETER Path
    Files, folders or archives to judge. `pwsh -File` binds "a,b" as one string, so commas separate them too.

.PARAMETER Expect
    Configured (default) or Unconfigured.

.PARAMETER CompareEnvironment
    Also require the artifacts' pair to equal this shell's FMS_DRIVE_CLIENT_ID / FMS_DRIVE_CLIENT_SECRET.

.PARAMETER AllowRollbackCopies
    Skip <name>.previous-<stamp>.exe, the copy build.ps1 keeps in bin\Release.

.NOTES
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL (a finding), 2 COULD NOT VERIFY (nothing named, a named path
    missing, no Share Manager found, an unreadable artifact, or -CompareEnvironment without both variables).
    The last line is the verdict, `Test-DriveClientArtifact: <WORD> (..)`.

.EXAMPLE
    pwsh -NoProfile -File tools\Test-DriveClientArtifact.ps1 -Path bin\Release -Expect Unconfigured -AllowRollbackCopies

.EXAMPLE
    pwsh -NoProfile -File tools\Test-DriveClientArtifact.ps1 -Path "dist\portable.zip,C:\Program Files\FastMediaSorter" -CompareEnvironment
#>
[CmdletBinding()]
param(
    [string[]]$Path = @(),
    [ValidateSet('Configured', 'Unconfigured')][string]$Expect = 'Configured',
    [switch]$CompareEnvironment,
    [switch]$AllowRollbackCopies
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
$check = 'Test-DriveClientArtifact'

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the check itself failed'
}

$exeName = 'FastMediaSorterCompanion.exe'
$idKey = 'FmsDriveClientId'
$secretKey = 'FmsDriveClientSecret'
$codeMarker = 'ExchangeDriveClientConfig'
$idShape = '^\d+-[A-Za-z0-9_]+\.apps\.googleusercontent\.com$'
# Latin-1 maps every byte to one char, so an ordinal string search is a byte search.
$latin1 = [Text.Encoding]::Latin1
$utf8 = [Text.Encoding]::UTF8

$targets = @($Path | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($targets.Count -eq 0) { Exit-Verdict $check 2 'no path named' }
foreach ($t in $targets) { if (-not (Test-Path -LiteralPath $t)) { Exit-Verdict $check 2 "path not found: $t" } }
Write-Subject $check (($targets -join ', ') + "; expect $Expect" + $(if ($CompareEnvironment) { '; compared with this shell' } else { '' }))

function Get-PairHash([string]$Id, [string]$Secret) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToBase64String($sha.ComputeHash($utf8.GetBytes($Id + "`n" + $Secret))) } finally { $sha.Dispose() }
}

# Every value serialized for one AssemblyMetadata key: 01 00 <len><key> <len><value> 00 00. A null value (FF)
# and a blob that does not end in 00 00 are not counted.
function Get-MetadataValues([string]$Haystack, [string]$Key) {
    $needle = [string][char]1 + [char]0 + [char]$Key.Length + $Key
    $values = [Collections.Generic.List[string]]::new()
    $at = 0
    while (($at = $Haystack.IndexOf($needle, $at, [StringComparison]::Ordinal)) -ge 0) {
        $p = $at + $needle.Length
        $at = $p
        if ($p -ge $Haystack.Length) { break }
        $b0 = [int]$Haystack[$p]
        if ($b0 -eq 0xFF) { continue }
        if (($b0 -band 0x80) -eq 0) { $len = $b0; $p += 1 }
        elseif (($b0 -band 0xC0) -eq 0x80 -and $p + 1 -lt $Haystack.Length) { $len = (($b0 -band 0x3F) -shl 8) -bor [int]$Haystack[$p + 1]; $p += 2 }
        else { continue }
        if ($p + $len + 2 -gt $Haystack.Length) { continue }
        if ([int]$Haystack[$p + $len] -ne 0 -or [int]$Haystack[$p + $len + 1] -ne 0) { continue }
        $values.Add($utf8.GetString($latin1.GetBytes($Haystack.Substring($p, $len))))
    }
    return , $values
}

# One artifact judged; returns { Name; Code; Note; Hash } - Hash only in memory, never printed.
function Measure-Artifact([string]$Name, [byte[]]$Bytes) {
    $haystack = $latin1.GetString($Bytes)
    if ($haystack.IndexOf($codeMarker, [StringComparison]::Ordinal) -lt 0) {
        return [pscustomobject]@{ Name = $Name; Code = 2; Note = 'Share Manager code not readable (compressed bundle or not a Share Manager build)'; Hash = $null }
    }
    $ids = @((Get-MetadataValues $haystack $idKey) | Select-Object -Unique)
    $secrets = @((Get-MetadataValues $haystack $secretKey) | Select-Object -Unique)
    if ($ids.Count -gt 1 -or $secrets.Count -gt 1) {
        return [pscustomobject]@{ Name = $Name; Code = 1; Note = 'more than one value for a client key'; Hash = $null }
    }
    if ($ids.Count -eq 0 -and $secrets.Count -eq 0) {
        $code = if ($Expect -eq 'Unconfigured') { 0 } else { 1 }
        return [pscustomobject]@{ Name = $Name; Code = $code; Note = 'client pair absent (the Drive page says "not configured")'; Hash = $null }
    }
    if ($ids.Count -ne $secrets.Count) {
        $which = if ($ids.Count) { 'id present, secret absent' } else { 'secret present, id absent' }
        return [pscustomobject]@{ Name = $Name; Code = 1; Note = "broken pair: $which"; Hash = $null }
    }
    $id = [string]$ids[0]; $secret = [string]$secrets[0]
    if ($id -notmatch $idShape) {
        $why = if ($secret -match $idShape) { 'id and secret look swapped' } else { 'the id is not shaped like a Google OAuth client id' }
        return [pscustomobject]@{ Name = $Name; Code = 1; Note = "client pair present, but $why"; Hash = $null }
    }
    $code = if ($Expect -eq 'Configured') { 0 } else { 1 }
    return [pscustomobject]@{ Name = $Name; Code = $code; Note = 'client pair present'; Hash = (Get-PairHash $id $secret) }
}

$results = [Collections.Generic.List[object]]::new()
foreach ($t in $targets) {
    $full = (Resolve-Path -LiteralPath $t).Path
    if (Test-Path -LiteralPath $full -PathType Container) {
        $found = @(Get-ChildItem -LiteralPath $full -Recurse -File -Filter $exeName)
        $found += @(Get-ChildItem -LiteralPath $full -Recurse -File -Filter 'FastMediaSorterCompanion.previous-*.exe' | Where-Object { -not $AllowRollbackCopies })
        if ($found.Count -eq 0) { $results.Add([pscustomobject]@{ Name = $full; Code = 2; Note = "no $exeName in the folder"; Hash = $null }); continue }
        foreach ($f in $found) { $results.Add((Measure-Artifact $f.FullName ([IO.File]::ReadAllBytes($f.FullName)))) }
        continue
    }
    $ext = [IO.Path]::GetExtension($full).ToLowerInvariant()
    if ($ext -in '.zip', '.msix', '.appx') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $zip = [IO.Compression.ZipFile]::OpenRead($full)
        try {
            $entries = @($zip.Entries | Where-Object { $_.Name -eq $exeName -or ($_.Name -like 'FastMediaSorterCompanion.previous-*.exe' -and -not $AllowRollbackCopies) })
            if ($entries.Count -eq 0) { $results.Add([pscustomobject]@{ Name = $full; Code = 2; Note = "no $exeName in the archive"; Hash = $null }) }
            foreach ($e in $entries) {
                $ms = [IO.MemoryStream]::new()
                $s = $e.Open(); try { $s.CopyTo($ms) } finally { $s.Dispose() }
                $results.Add((Measure-Artifact "$full!$($e.FullName)" $ms.ToArray()))
            }
        } finally { $zip.Dispose() }
        continue
    }
    if ([IO.Path]::GetFileName($full) -like '*-setup.exe') {
        $results.Add([pscustomobject]@{ Name = $full; Code = 2; Note = 'an Inno setup is compressed: install it and name the installed folder'; Hash = $null })
        continue
    }
    $results.Add((Measure-Artifact $full ([IO.File]::ReadAllBytes($full))))
}

# 5. one client across every configured artifact of the run
$hashes = @($results | Where-Object { $_.Hash } | ForEach-Object { $_.Hash } | Select-Object -Unique)
$mixed = $hashes.Count -gt 1
# 6. the owner's shell
$envNote = ''
$envCode = 0
if ($CompareEnvironment) {
    $envId = [Environment]::GetEnvironmentVariable('FMS_DRIVE_CLIENT_ID')
    $envSecret = [Environment]::GetEnvironmentVariable('FMS_DRIVE_CLIENT_SECRET')
    if ([string]::IsNullOrWhiteSpace($envId) -or [string]::IsNullOrWhiteSpace($envSecret)) {
        $envCode = 2; $envNote = 'FMS_DRIVE_CLIENT_ID and FMS_DRIVE_CLIENT_SECRET are not both set in this shell'
    } elseif ($hashes.Count -gt 0) {
        $envHash = Get-PairHash $envId.Trim() $envSecret.Trim()
        $differs = @($results | Where-Object { $_.Hash -and $_.Hash -ne $envHash })
        foreach ($r in $differs) { $r.Code = 1; $r.Note = 'client pair present, but differs from this shell''s pair' }
        foreach ($r in @($results | Where-Object { $_.Hash -and $_.Hash -eq $envHash })) { $r.Note = 'client pair present, matches this shell''s pair' }
    }
}

foreach ($r in $results) {
    $color = switch ($r.Code) { 0 { 'Green' } 2 { 'Magenta' } default { 'Red' } }
    Write-Host "  $($r.Name): $($r.Note)" -ForegroundColor $color
}
if ($mixed) { Write-Host "  the configured artifacts carry $($hashes.Count) different client pairs" -ForegroundColor Red }
if ($envNote) { Write-Host "  $envNote" -ForegroundColor Magenta }

$failed = @($results | Where-Object { $_.Code -eq 1 }).Count
$unverified = @($results | Where-Object { $_.Code -eq 2 }).Count
$n = $results.Count
if ($failed -gt 0 -or $mixed) {
    $detail = "$failed of $n artifact(s) judged defective"
    if ($mixed) { $detail += '; mixed client pairs' }
    Exit-Verdict $check 1 $detail
}
if ($unverified -gt 0 -or $envCode -eq 2) {
    Exit-Verdict $check 2 "$unverified of $n artifact(s) not readable$(if ($envCode -eq 2) { '; no client pair in this shell to compare with' })"
}
Exit-Verdict $check 0 "$n artifact(s), expect $Expect$(if ($CompareEnvironment -and $Expect -eq 'Configured') { ', matching this shell' })"
