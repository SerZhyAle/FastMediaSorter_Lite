[CmdletBinding()]
param([string]$Destination)
# The FileDO that ships beside the worker is never pinned: every run takes the freshest one and rewrites
# payload\filedo (exe + filedo.sha256 + release.json). Owner decision 2026-10-04: no fixing on releases,
# always build with the fresh one. The sha256 and release.json are RECORDS of what was staged this run,
# not expectations the next run must meet.
#
# Where "fresh" comes from, in order:
#   1. $env:FMS_FILEDO_EXE - an explicit filedo.exe.
#   2. The FileDO repository's own build output, <FMS_FILEDO_REPO or ..\FileDo>\exe_to_download\filedo.exe.
#   3. The latest stable GitHub release of SerZhyAle/FileDO (a clean checkout / CI has no local FileDO).
# Nothing here falls back to an older copy: if none of the three yields an exe the run fails.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $repoRoot 'payload\filedo'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$exe = Join-Path $payload 'filedo.exe'

$localExe = $null
if ($env:FMS_FILEDO_EXE) {
    if (-not (Test-Path -LiteralPath $env:FMS_FILEDO_EXE)) { throw "FMS_FILEDO_EXE does not exist: $env:FMS_FILEDO_EXE" }
    $localExe = (Resolve-Path -LiteralPath $env:FMS_FILEDO_EXE).Path
} else {
    $fileDoRepo = if ($env:FMS_FILEDO_REPO) { $env:FMS_FILEDO_REPO } else { Join-Path (Split-Path $repoRoot -Parent) 'FileDo' }
    $candidate = Join-Path $fileDoRepo 'exe_to_download\filedo.exe'
    if (Test-Path -LiteralPath $candidate) { $localExe = (Resolve-Path -LiteralPath $candidate).Path }
}

if ($localExe) {
    Copy-Item -LiteralPath $localExe -Destination $exe -Force
    $binaryHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion; origin = 'local build'; source = $localExe; binarySha256 = $binaryHash }
} else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $release = Invoke-RestMethod 'https://api.github.com/repos/SerZhyAle/FileDO/releases/latest' -Headers @{ 'User-Agent' = 'FMS-FileDO-Payload' }
    if ($release.tag_name -notmatch '^v\d{10}$' -or $release.draft -or $release.prerelease) { throw 'Latest FileDO release is not a stable stamped release.' }
    $asset = @($release.assets | Where-Object name -like '*-windows-x64.zip')
    if ($asset.Count -ne 1) { throw 'Expected exactly one FileDO x64 zip asset.' }
    $checksum = @($release.assets | Where-Object name -eq ($asset[0].name + '.sha256'))
    if ($checksum.Count -ne 1) { throw 'FileDO release is missing the archive checksum.' }
    # Keep the download evidence in an ignored build directory. No recursive cleanup.
    $scratch = Join-Path $repoRoot ('bin\filedo-download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $zipPath = Join-Path $scratch 'release.zip'
    $hashPath = Join-Path $scratch 'release.sha256'
    Invoke-WebRequest $asset[0].browser_download_url -OutFile $zipPath
    Invoke-WebRequest $checksum[0].browser_download_url -OutFile $hashPath
    $expected = (Get-Content -LiteralPath $hashPath -Raw).Trim().Split([char[]]" `t`r`n", [StringSplitOptions]::RemoveEmptyEntries)[0]
    if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release SHA-256.' }
    if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $expected) { throw 'FileDO archive SHA-256 mismatch.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name -ceq 'filedo.exe' })
        if ($entries.Count -ne 1) { throw 'Expected exactly one filedo.exe in release zip.' }
        # Extract only this member to an explicit destination; zip paths are never used.
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $exe, $true)
    } finally { $archive.Dispose() }
    $binaryHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ version = $release.tag_name; origin = 'github release'; asset = $asset[0].name; archiveSha256 = $expected.ToLowerInvariant(); binarySha256 = $binaryHash; source = $asset[0].browser_download_url }
}

[IO.File]::WriteAllText((Join-Path $payload 'filedo.sha256'), "$binaryHash  filedo.exe`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $payload 'release.json'), ($manifest | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))

# The disk-sharing worker refuses a FileDO that has no `vd` command; say so at build time, not at the first share.
$probe = Join-Path $env:TEMP ('filedo-vd-probe-' + [Guid]::NewGuid().ToString('N') + '.jsonl')
try {
    # The probe is best-effort by design (see the finally below), but under the caller's
    # inherited $ErrorActionPreference 'Stop' a redirected native stderr line becomes a
    # terminating NativeCommandError - and a FileDO that HAS the vd command but wants a
    # verb under it answers the bare probe with exactly that. Swallow the invocation's own
    # failure; only the "Unknown command" verdict matters.
    try { & $exe vd --events $probe --no-history *> $null } catch { }
    $text = if (Test-Path -LiteralPath $probe) { Get-Content -LiteralPath $probe -Raw } else { '' }
    if ($text -match 'Unknown command') { Write-Warning ('Staged FileDO ' + $manifest.version + ' has no "vd" command: disk sharing will be refused by the worker.') }
} finally {
    Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    # The probe's own exit code (2: usage) must not become this script's.
    $global:LASTEXITCODE = 0
}

if ($Destination) {
    $target = Join-Path $Destination 'filedo'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($fileName in @('filedo.exe', 'filedo.sha256', 'release.json')) {
        Copy-Item -LiteralPath (Join-Path $payload $fileName) -Destination $target -Force
    }
}
Write-Host ('Staged FileDO ' + $manifest.version + ' (' + $manifest.origin + '): ' + $binaryHash)
