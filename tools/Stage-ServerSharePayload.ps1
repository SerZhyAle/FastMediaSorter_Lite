[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UserStage,
    [Parameter(Mandatory)][string]$Destination
)

$ErrorActionPreference = 'Stop'

# Both editions must use the exact files selected for the User package. In particular,
# preparing FileDO a second time can fetch a different upstream release mid-build.
foreach ($relative in @('FastMediaSorterCompanion.exe', 'companion\fms-share-worker.exe',
                        'filedo\filedo.exe', 'filedo\filedo.sha256', 'filedo\release.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $UserStage $relative) -PathType Leaf)) {
        throw "Missing $relative in User stage '$UserStage'. Build the matching User installer first and retry."
    }
}
$filedo = Join-Path $UserStage 'filedo\filedo.exe'
$record = (Get-Content -LiteralPath (Join-Path $UserStage 'filedo\filedo.sha256') -Raw).Trim()
if ($record -notmatch '^([a-fA-F0-9]{64})\s+\*?filedo\.exe$' -or
    (Get-FileHash -LiteralPath $filedo -Algorithm SHA256).Hash -ne $Matches[1]) {
    throw "FileDO checksum is invalid in User stage '$UserStage'. Rebuild the matching User installer."
}

foreach ($directory in @('companion', 'filedo')) {
    $target = Join-Path $Destination $directory
    if (Test-Path -LiteralPath $target) {
        throw "Destination '$target' already exists. Use an empty Server stage."
    }
}
if (Test-Path -LiteralPath (Join-Path $Destination 'FastMediaSorterCompanion.exe')) {
    throw "Destination already contains FastMediaSorterCompanion.exe. Use an empty Server stage."
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $UserStage 'FastMediaSorterCompanion.exe') -Destination $Destination -Force
foreach ($directory in @('companion', 'filedo')) {
    $target = Join-Path $Destination $directory
    Copy-Item -LiteralPath (Join-Path $UserStage $directory) -Destination $target -Recurse
}
Write-Host 'Staged Share Manager, worker and FileDO from the matching User stage.'
