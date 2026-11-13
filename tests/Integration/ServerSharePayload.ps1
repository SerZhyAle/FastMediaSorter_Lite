$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')
trap { Exit-Verdict 'ServerSharePayload' 1 $_.Exception.Message }
$helper = Join-Path $repo 'tools\Stage-ServerSharePayload.ps1'
$fixture = Join-Path $env:TEMP ('fms-share-parity-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'user'
$target = Join-Path $fixture 'server'
New-Item -ItemType Directory -Path (Join-Path $source 'companion'), (Join-Path $source 'filedo') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $source 'FastMediaSorterCompanion.exe') -Value 'manager'
Set-Content -LiteralPath (Join-Path $source 'companion\fms-share-worker.exe') -Value 'worker'
Set-Content -LiteralPath (Join-Path $source 'filedo\filedo.exe') -Value 'filedo'
Set-Content -LiteralPath (Join-Path $source 'filedo\release.json') -Value '{}'
$hash = (Get-FileHash -LiteralPath (Join-Path $source 'filedo\filedo.exe') -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $source 'filedo\filedo.sha256') -Value "$hash  filedo.exe"

& $helper -UserStage $source -Destination $target
foreach ($relative in @('FastMediaSorterCompanion.exe', 'companion\fms-share-worker.exe',
                        'filedo\filedo.exe', 'filedo\filedo.sha256', 'filedo\release.json')) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash) {
        throw "Staged file differs: $relative"
    }
}

Set-Content -LiteralPath (Join-Path $source 'filedo\filedo.exe') -Value 'corrupt'
$rejected = $false
try { & $helper -UserStage $source -Destination (Join-Path $fixture 'bad-hash') } catch { $rejected = $_.Exception.Message -like '*checksum*' }
if (-not $rejected) { throw 'A mismatched FileDO checksum was not rejected.' }

Remove-Item -LiteralPath (Join-Path $source 'companion\fms-share-worker.exe')
$rejected = $false
try { & $helper -UserStage $source -Destination (Join-Path $fixture 'missing-worker') } catch { $rejected = $_.Exception.Message -like '*fms-share-worker.exe*' }
if (-not $rejected) { throw 'A missing worker was not rejected.' }
Exit-Verdict 'ServerSharePayload' 0 'matching sharing files copied; corrupt checksum and missing worker rejected'
