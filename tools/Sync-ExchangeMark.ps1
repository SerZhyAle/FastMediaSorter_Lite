<# Imports or verifies the unmodified EXCHANGE-MARK artwork. #>
[CmdletBinding()]
param([string]$CatalogRoot = '', [switch]$Check)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/Verdict.ps1')
Write-Subject 'Sync-ExchangeMark' 'vendored EXCHANGE-MARK 0.3 artwork and provenance against the shared catalog'
trap { Write-Host "Sync-ExchangeMark: COULD NOT VERIFY ($($_.Exception.Message))"; exit 2 }
if (-not $CatalogRoot) { $CatalogRoot = $env:FMS_CONTRACTS_ROOT }
if (-not $CatalogRoot) { $CatalogRoot = [Environment]::GetEnvironmentVariable('FMS_CONTRACTS_ROOT', 'User') }
if (-not $CatalogRoot) { throw 'Pass -CatalogRoot or set FMS_CONTRACTS_ROOT (see CLAUDE.md)' }
$source = Join-Path $CatalogRoot 'exchange-mark/artwork'
$manifest = Get-Content (Join-Path $source 'MANIFEST.json') -Raw | ConvertFrom-Json
if ($manifest.contract -ne 'EXCHANGE-MARK' -or $manifest.version -ne '0.3') { throw 'Review the changed artwork contract before importing' }
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/exchange-mark'
$files = @($manifest.files | Where-Object { $_.path -match '^(png/(mark|mono)-\d+\.png|png/full-(128|256)\.png|ico/exchange-server\.ico|svg/exchange-mark(-small|-full|-mono)?\.svg)$' })
$provenance = @('EXCHANGE-MARK 0.3', 'Source: exchange-mark/artwork (shared contracts catalog)', '')
$drift = @()
if ($files.Count -ne 35) { throw 'The 0.3 artwork inventory changed; review it before importing' }
foreach ($file in $files) {
    $src = Join-Path $source $file.path
    if ((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Catalog hash mismatch: $($file.path)" }
    $dst = Join-Path $destination $file.path
    $provenance += "$($file.sha256)  $($file.path)"
    if ($Check) {
        if (-not (Test-Path -LiteralPath $dst) -or (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { $drift += $file.path }
    } else {
        [IO.Directory]::CreateDirectory((Split-Path $dst -Parent)) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst
    }
}
$record = ($provenance -join "`n") + "`n"
$recordPath = Join-Path $destination 'PROVENANCE.txt'
if ($Check) {
    foreach ($local in Get-ChildItem -LiteralPath $destination -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($destination, $local.FullName).Replace('\', '/')
        if ($relative -ne 'PROVENANCE.txt' -and $relative -notin $files.path) { $drift += "undeclared: $relative" }
    }
    if (-not (Test-Path -LiteralPath $recordPath) -or [IO.File]::ReadAllText($recordPath) -cne $record) { $drift += 'PROVENANCE.txt' }
} else { [IO.File]::WriteAllText($recordPath, $record, [Text.UTF8Encoding]::new($false)) }
if ($drift.Count) { $drift | ForEach-Object { Write-Host "  $_" }; Write-Host 'Sync-ExchangeMark: FAIL (artwork drift)'; exit 1 }
Write-Host "Sync-ExchangeMark: PASS ($($files.Count) files, EXCHANGE-MARK 0.3)"
exit 0
