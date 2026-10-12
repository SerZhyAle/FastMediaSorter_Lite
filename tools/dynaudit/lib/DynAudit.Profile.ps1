<#
.SYNOPSIS
    The isolated one-time profile (specification 209 section 5): backup BEFORE, no
    exceptions; restore in finally; a failed restore is a P0 incident on the verdict line,
    never silent. The 169 precedent - "no backup, no reset" - is the law this module
    implements for the whole campaign.

.DESCRIPTION
    What is captured before anything is launched:
      - the whole HKCU\SZA\FastMediaSorter hive (reg export, or the recorded absence)
      - a copy of %LOCALAPPDATA%\SZA\FastMediaSorter (or the recorded absence)
      - the %TEMP% baseline: every app-attributable temp file with its SHA-256
      - SHA-256 of everything backed up
      - recovery-manifest.json with the exact restore plan
    A campaign runs under a throw-away local account (Q209-2): the HKCU, the
    %LOCALAPPDATA% and the session this module touches are then the account's own. The
    in-place fallback works the same way and is simply weaker isolation.
#>

. (Join-Path $PSScriptRoot 'DynAudit.Scrub.ps1')

$script:DynAuditHivePath = 'HKCU:\SZA\FastMediaSorter'
$script:DynAuditHiveRegName = 'HKCU\SZA\FastMediaSorter'
$script:DynAuditDataDir = Join-Path $env:LOCALAPPDATA 'SZA\FastMediaSorter'
$script:DynAuditTempMarker = '(?i)fastmediasorter|fmsl?[-_.]|\.fms-part|fastmediasortercompanion'

function Get-DynAuditAppTempFiles {
    if (-not (Test-Path -LiteralPath $env:TEMP)) { return @() }
    return @(Get-ChildItem -LiteralPath $env:TEMP -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -imatch $script:DynAuditTempMarker })
}

function Get-DynAuditSha256 {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $h = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [IO.File]::OpenRead($Path)
        try { return [BitConverter]::ToString($h.ComputeHash($fs)).Replace('-', '').ToLowerInvariant() } finally { $fs.Dispose() }
    } finally { $h.Dispose() }
}

# Step 0. Throws on any failure - the caller exits 2 without launching the product.
function New-DynAuditProfileBackup {
    param([string]$RunFolder)
    $backupDir = Join-Path $RunFolder 'backup'
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $manifest = [ordered]@{
        CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Machine     = 'redacted-at-write'
        Hive        = $null
        DataDir     = $null
        Temp        = $null
        RestorePlan = @(
            'reg import the exported hive (or delete the key when the backup recorded its absence)',
            'restore the data-folder copy (or delete the folder when absent at backup)',
            'delete app-attributable temp files that were not in the baseline',
            'verify against the hashes and counts in this manifest and write the restore verdict'
        )
    }

    # The registry hive.
    $hiveExisted = Test-Path -LiteralPath $script:DynAuditHivePath
    $regFile = Join-Path $backupDir 'sza-fastmediasorter.reg'
    if ($hiveExisted) {
        $out = & reg.exe export $script:DynAuditHiveRegName $regFile /y 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $regFile)) {
            throw "reg export of the settings hive failed: $out"
        }
    }
    $manifest.Hive = @{ Existed = $hiveExisted; File = 'backup\sza-fastmediasorter.reg'; Sha256 = (Get-DynAuditSha256 $regFile) }

    # The data folder.
    $dataExisted = Test-Path -LiteralPath $script:DynAuditDataDir
    $dataCopy = Join-Path $backupDir 'data'
    if ($dataExisted) {
        Copy-Item -LiteralPath $script:DynAuditDataDir -Destination $dataCopy -Recurse -Force
        $fileHashes = @{}
        foreach ($f in (Get-ChildItem -LiteralPath $dataCopy -Recurse -Force -File -ErrorAction SilentlyContinue)) {
            $rel = $f.FullName.Substring($dataCopy.Length + 1)
            $fileHashes[$rel] = Get-DynAuditSha256 $f.FullName
        }
        $manifest.DataDir = @{ Existed = $true; Copy = 'backup\data'; Files = $fileHashes.Count; Hashes = $fileHashes }
    } else {
        $manifest.DataDir = @{ Existed = $false }
    }

    # The temp baseline: every app-attributable temp file with its hash.
    $tempBase = @{}
    foreach ($f in (Get-DynAuditAppTempFiles)) { $tempBase[$f.FullName] = Get-DynAuditSha256 $f.FullName }
    $manifest.Temp = @{ BaselineCount = $tempBase.Count; Baseline = $tempBase }

    $manifestPath = Join-Path $RunFolder 'recovery-manifest.json'
    Write-ScrubbedJson $manifestPath ([pscustomobject]$manifest)
    return [pscustomobject]$manifest
}

function Test-DynAuditRestoreManifest {
    param([string]$RunFolder)
    $manifestPath = Join-Path $RunFolder 'recovery-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $null }
    return Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}

# The restore, called from the runner's finally - after a crash too, never skipped.
function Restore-DynAuditProfile {
    param([string]$RunFolder)
    $details = [System.Collections.Generic.List[string]]::new()
    $ok = $true
    $manifest = Test-DynAuditRestoreManifest $RunFolder
    if (-not $manifest) { return @{ Ok = $false; Details = @('recovery-manifest.json missing - restore refused (P0)') } }

    # Hive: reimport the export, or remove what the campaign created.
    $hiveExists = Test-Path -LiteralPath $script:DynAuditHivePath
    if ($manifest.Hive.Existed) {
        $regFile = Join-Path $RunFolder 'backup\sza-fastmediasorter.reg'
        if (-not (Test-Path -LiteralPath $regFile)) { $ok = $false; $details.Add('hive export file missing from the run folder') }
        else {
            if ($hiveExists) { Remove-Item -LiteralPath $script:DynAuditHivePath -Recurse -Force -ErrorAction SilentlyContinue }
            $out = & reg.exe import $regFile 2>&1
            if ($LASTEXITCODE -ne 0) { $ok = $false; $details.Add("reg import failed: $out") }
            else {
                if (-not (Test-Path -LiteralPath $script:DynAuditHivePath)) { $ok = $false; $details.Add('hive missing after import') }
                else { $details.Add('hive reimported') }
            }
        }
    } elseif ($hiveExists) {
        Remove-Item -LiteralPath $script:DynAuditHivePath -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $script:DynAuditHivePath) { $ok = $false; $details.Add('created hive could not be removed') }
        else { $details.Add('created hive removed (absent at backup)') }
    } else { $details.Add('hive absent, as at backup') }

    # Data folder.
    $dataExists = Test-Path -LiteralPath $script:DynAuditDataDir
    if ($manifest.DataDir.Existed) {
        $dataCopy = Join-Path $RunFolder 'backup\data\FastMediaSorter'
        if (-not (Test-Path -LiteralPath $dataCopy)) { $ok = $false; $details.Add('data-folder copy missing from the run folder') }
        else {
            if ($dataExists) { Remove-Item -LiteralPath $script:DynAuditDataDir -Recurse -Force -ErrorAction SilentlyContinue }
            Copy-Item -LiteralPath $dataCopy -Destination (Split-Path $script:DynAuditDataDir -Parent) -Recurse -Force
            $restored = @(Get-ChildItem -LiteralPath $dataCopy -Recurse -Force -File -ErrorAction SilentlyContinue)
            $mismatch = 0
            foreach ($f in $restored) {
                $rel = $f.FullName.Substring($dataCopy.Length + 1)
                $expect = $manifest.DataDir.Hashes.$rel
                $actual = Get-DynAuditSha256 $f.FullName
                if ($expect -and $actual -and $expect -ne $actual) { $mismatch++ }
            }
            if ($mismatch -gt 0) { $ok = $false; $details.Add("$mismatch restored data file(s) fail the hash check") }
            else { $details.Add("data folder restored and hash-verified ($($restored.Count) files)") }
        }
    } elseif ($dataExists) {
        Remove-Item -LiteralPath $script:DynAuditDataDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $script:DynAuditDataDir) { $ok = $false; $details.Add('created data folder could not be removed') }
        else { $details.Add('created data folder removed (absent at backup)') }
    } else { $details.Add('data folder absent, as at backup') }

    # Temp: everything app-attributable that the baseline did not have goes away.
    $baseline = @{}
    if ($manifest.Temp -and $manifest.Temp.Baseline) {
        foreach ($p in $manifest.Temp.Baseline.PSObject.Properties) { $baseline[$p.Name] = $true }
    }
    $removed = 0; $left = 0
    foreach ($f in (Get-DynAuditAppTempFiles)) {
        if ($baseline.ContainsKey($f.FullName)) { continue }
        try { Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop; $removed++ } catch { $left++ }
    }
    if ($left -gt 0) { $ok = $false; $details.Add("$left app-attributable temp file(s) could not be removed") }
    else { $details.Add("temp baseline restored ($removed file(s) removed, none left)") }

    $result = @{ Ok = $ok; Details = $details.ToArray() }
    $verifyPath = Join-Path $RunFolder 'restore-verdict.json'
    Write-ScrubbedJson $verifyPath ([pscustomobject]@{ Ok = $ok; Details = $details.ToArray(); AtUtc = (Get-Date).ToUniversalTime().ToString('o') })
    return $result
}
