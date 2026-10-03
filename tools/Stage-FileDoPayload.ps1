[CmdletBinding()]
param([switch]$Refresh, [string]$Destination)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $repoRoot 'payload\filedo'
if ($Refresh) {
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
        $exePath = Join-Path $scratch 'filedo.exe'
        # Extract only this member to an explicit destination; zip paths are never used.
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $exePath)
    } finally { $archive.Dispose() }
    $binaryHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -LiteralPath $exePath -Destination (Join-Path $payload 'filedo.exe') -Force
    [IO.File]::WriteAllText((Join-Path $payload 'filedo.sha256'), "$binaryHash  filedo.exe`n", [Text.UTF8Encoding]::new($false))
    $manifest = [ordered]@{ version = $release.tag_name; asset = $asset[0].name; archiveSha256 = $expected.ToLowerInvariant(); binarySha256 = $binaryHash; source = $asset[0].browser_download_url }
    [IO.File]::WriteAllText((Join-Path $payload 'release.json'), ($manifest | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
}
$exe = Join-Path $payload 'filedo.exe'
$hashFile = Join-Path $payload 'filedo.sha256'
if (-not (Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath (Join-Path $payload 'release.json'))) {
    # A clean checkout fetches the committed release, never "latest" during a build.
    $pinnedRelease = Get-Content -LiteralPath (Join-Path $payload 'release.json') -Raw | ConvertFrom-Json
    if ($pinnedRelease.version -notmatch '^v\d{10}$' -or
        $pinnedRelease.source -cne ('https://github.com/SerZhyAle/FileDO/releases/download/' + $pinnedRelease.version + '/' + $pinnedRelease.asset) -or
        $pinnedRelease.archiveSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid pinned FileDO provenance.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $scratch = Join-Path $repoRoot ('bin\filedo-download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $zipPath = Join-Path $scratch 'release.zip'
    Invoke-WebRequest $pinnedRelease.source -OutFile $zipPath
    if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $pinnedRelease.archiveSha256) { throw 'Pinned archive SHA-256 mismatch.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name -ceq 'filedo.exe' })
        if ($entries.Count -ne 1) { throw 'Expected exactly one filedo.exe in pinned archive.' }
        $downloaded = Join-Path $scratch 'filedo.exe'
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $downloaded)
    } finally { $archive.Dispose() }
    if ((Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash -ne $pinnedRelease.binarySha256) { throw 'Pinned binary SHA-256 mismatch.' }
    Copy-Item -LiteralPath $downloaded -Destination $exe
}
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $hashFile)) { throw 'FileDO payload is missing; run Stage-FileDoPayload.ps1 -Refresh.' }
$pinned = (Get-Content -LiteralPath $hashFile -Raw).Trim().Split(' ')[0]
if ($pinned -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $pinned) { throw 'Pinned FileDO SHA-256 verification failed.' }
# filedo.sha256 sits next to the exe, so replacing both together would pass the check above.
# release.json is the committed provenance record: the exe must match its binarySha256 too.
$provenancePath = Join-Path $payload 'release.json'
if (-not (Test-Path -LiteralPath $provenancePath)) { throw 'FileDO payload provenance (release.json) is missing; run Stage-FileDoPayload.ps1 -Refresh.' }
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
if ($provenance.binarySha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid pinned FileDO binarySha256 in release.json.' }
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $provenance.binarySha256) { throw 'FileDO binary does not match the binarySha256 pinned in release.json.' }
if ($Destination) {
    $target = Join-Path $Destination 'filedo'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($fileName in @('filedo.exe', 'filedo.sha256', 'release.json')) {
        Copy-Item -LiteralPath (Join-Path $payload $fileName) -Destination $target -Force
    }
}
Write-Host ('Verified FileDO payload: ' + $pinned)
