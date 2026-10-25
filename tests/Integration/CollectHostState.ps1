<#
.SYNOPSIS
    Hand-run proof of publishing\installer\collect-host-state.ps1 (ticket 182 L-9): plants logs and secrets
    in throw-away roots, runs the collector against them, and checks what is - and is not - in the ZIP.
    Read-only on the machine; everything is created under a temporary folder. Run it under both editions:
        pwsh -NoProfile -File tests\Integration\CollectHostState.ps1
        powershell -NoProfile -File tests\Integration\CollectHostState.ps1
    Exit 0 = every check passed, 1 = a check failed (each failure is printed), 2 = could not run.
#>
$ErrorActionPreference = 'Stop'
$repo      = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')
trap { Write-Host $_.Exception.Message; Exit-Verdict 'CollectHostState' 2 'the check itself failed' }
Write-Subject 'CollectHostState' 'the offline support collector against synthetic logs and credentials'
$collector = Join-Path $repo 'publishing\installer\collect-host-state.ps1'
if (-not (Test-Path $collector)) { Exit-Verdict 'CollectHostState' 2 'collector not found' }

$root = Join-Path ([IO.Path]::GetTempPath()) ('fms-chs-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$pd   = Join-Path $root 'ProgramData'
$la   = Join-Path $root 'LocalAppData'
$tmp  = Join-Path $root 'Temp'
$app  = Join-Path $root 'App'
$out  = Join-Path $root 'Out'
foreach ($d in @($pd, $la, $tmp, $app, $out,
                 (Join-Path $pd 'FastMediaSorterExchange\logs'), (Join-Path $pd 'FastMediaSorterExchange\tls'),
                 (Join-Path $pd 'FastMediaSorterCompanion'), (Join-Path $la 'FastMediaSorterCompanion'))) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}
$utf8 = New-Object Text.UTF8Encoding($false)
function Plant([string]$path, [string]$text) { [IO.File]::WriteAllText($path, $text, $utf8) }

$profileLine = 'opened ' + $env:USERPROFILE + '\Pictures\secret-holiday.jpg'
Plant (Join-Path $pd 'FastMediaSorterExchange\logs\exchange.log')   ("2026-10-07T10:00:00Z`tINFO`tstart`tversion=26.10.7.1`n" + $profileLine + "`n")
Plant (Join-Path $pd 'FastMediaSorterExchange\logs\exchange.log.1') "old rotation line`n"
Plant (Join-Path $pd 'FastMediaSorterExchange\logs\service.log')    "2026-10-07T10:00:01Z listen failed`n"
Plant (Join-Path $pd 'FastMediaSorterExchange\logs\install-exchange-service.log') "install ok`n"
Plant (Join-Path $pd 'FastMediaSorterExchange\exchange.json')       '{"passwordHash":"$argon2id$v=19$m=65536,t=3,p=4$NEEDLEPASSWORDHASH","listen":":22022"}'
Plant (Join-Path $pd 'FastMediaSorterExchange\tls\key.pem')         "-----BEGIN PRIVATE KEY-----`nNEEDLEKEY`n-----END PRIVATE KEY-----`n"
Plant (Join-Path $pd 'FastMediaSorterCompanion\service.log')        "share service line`n"
Plant (Join-Path $pd 'FastMediaSorterCompanion\install-share-service.log') "share helper line`n"
Plant (Join-Path $la 'FastMediaSorterCompanion\companion.log')      "companion line`n"
Plant (Join-Path $app 'current.log')                                 "viewer line`n"
Plant (Join-Path $tmp 'install-share-service.log')                   "helper in temp`n"
# a log that quotes a secret by mistake: the last scan must drop it
Plant (Join-Path $pd 'FastMediaSorterCompanion\leaky.log')          "oops -----BEGIN PRIVATE KEY----- NEEDLELEAK`n"
Plant (Join-Path $pd 'FastMediaSorterCompanion\hash.log') '{"passwordHash":"NEEDLEHASH"}'
Plant (Join-Path $pd 'FastMediaSorterCompanion\identity.log') 'deviceToken=NEEDLETOKEN pairingCode=NEEDLEPAIR serverAddress=NEEDLEHOST certFingerprint=NEEDLEPIN'
Plant (Join-Path $pd 'FastMediaSorterCompanion\large.log') (("safe line`n" * 300000) + "token=NEEDLETAIL`n")
# a log that cannot be read: another handle holds it with no sharing at all
$lockedPath = Join-Path $pd 'FastMediaSorterCompanion\locked.log'
Plant $lockedPath "locked`n"
$lock = New-Object IO.FileStream($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

$failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$ok, [string]$what) { if (-not $ok) { $failures.Add($what); Write-Host ('  FAIL: ' + $what) } else { Write-Host ('  ok:   ' + $what) } }

try {
    $runArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $collector,
              '-NoEventLog', '-NoOpen', '-OutDir', $out, '-AppDir', $app,
              '-ProgramDataRoot', $pd, '-LocalAppDataRoot', $la, '-TempRoot', $tmp)
    $exe = (Get-Process -Id $PID).Path
    $outText = & $exe @runArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Host ($outText.Trim())
    Check ($code -eq 0) 'the collector exits 0'
    Check ($outText -match 'collect-host-state: DONE') 'it prints the DONE line'

    $zip = Get-ChildItem -LiteralPath $out -Filter 'fms-host-logs-*.zip' | Select-Object -First 1
    Check ($null -ne $zip) 'a ZIP is written to OutDir'
    if ($zip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $za = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try {
            $names = @($za.Entries | ForEach-Object { $_.FullName })
            function ReadEntry([string]$n) {
                $e = $za.GetEntry($n); if (-not $e) { return $null }
                $r = New-Object IO.StreamReader($e.Open()); try { return $r.ReadToEnd() } finally { $r.Dispose() }
            }
            Check (@($names | Where-Object { $_ -like '*\*' }).Count -eq 0) 'entry names use forward slashes only'
            foreach ($n in @('state.txt', 'defender.txt',
                             'logs/exchange-machine/exchange.log', 'logs/exchange-machine/exchange.log.1',
                             'logs/exchange-machine/service.log', 'logs/exchange-machine/install-exchange-service.log',
                             'logs/share-machine/service.log', 'logs/share-machine/install-share-service.log',
                             'logs/share-user/companion.log', 'logs/app/current.log', 'logs/temp/install-share-service.log')) {
                Check ($names -contains $n) ('the ZIP holds ' + $n)
            }
            $all = ($names | ForEach-Object { ReadEntry $_ }) -join "`n"
            foreach ($needle in @('NEEDLEPASSWORDHASH', 'NEEDLEKEY', 'NEEDLELEAK', 'NEEDLEHASH', 'NEEDLETOKEN', 'NEEDLEPAIR', 'NEEDLEHOST', 'NEEDLEPIN', 'NEEDLETAIL', 'passwordHash', 'PRIVATE KEY')) {
                Check (-not $all.Contains($needle)) ('no trace of ' + $needle + ' anywhere in the ZIP')
            }
            Check (@($names | Where-Object { $_ -like '*exchange.json*' -or $_ -like '*key.pem*' -or $_ -like '*tls/*' }).Count -eq 0) 'exchange.json and tls\ are never collected'
            Check ((ReadEntry 'logs/share-machine/leaky.log') -match '\[REDACTED\]') 'the private key is redacted before compaction'
            Check (-not ($names -contains 'logs/share-machine/hash.log')) 'a remaining password hash is dropped by the last scan'
            Check ((ReadEntry 'environment.txt') -match '^schema_version=1') 'environment.txt starts with schema_version'
            Check ((ReadEntry 'logs/share-machine/large.log') -match '\[Diag\] LOG TRUNCATED \| dropped_middle_bytes=\d+ \| kept_head_bytes=\d+ \| kept_tail_bytes=\d+') 'oversized redacted logs use the contract marker'
            $ex = ReadEntry 'logs/exchange-machine/exchange.log'
            Check ($ex -and $ex.Contains('<USER>') -and -not $ex.Contains($env:USERPROFILE)) 'the user-profile path is replaced by <USER>'
            $state = ReadEntry 'state.txt'
            Check ($state -match 'NOT READ' -and $state -match 'locked\.log') 'an unreadable log is NAMED in state.txt, never silently missing'
            Check ($state -match '== Windows services ==') 'state.txt has the services section'
            Check ($state -match '== Listening TCP sockets') 'state.txt has the sockets section'
            Check ($state -match 'dropped from the archive') 'state.txt says which file the last scan dropped'
        } finally { $za.Dispose() }
    }
} finally {
    $lock.Dispose()
    $resolved = [IO.Path]::GetFullPath($root)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\', '/') -ine $expectedParent -or [IO.Path]::GetFileName($resolved) -notmatch '^fms-chs-test-[0-9a-f]{8}$') { throw 'Refusing cleanup outside the fixture root' }
    Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -eq 0) { Exit-Verdict 'CollectHostState' 0 'all synthetic archive checks passed' }
Exit-Verdict 'CollectHostState' 1 ($failures.Count.ToString() + ' checks failed')
