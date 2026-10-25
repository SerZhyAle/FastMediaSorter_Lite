<#
.SYNOPSIS
    Opt-in manager transfer proof against an already attached Windows or Android exchange root.
.DESCRIPTION
    Requires an explicitly supplied expendable directory strictly below the saved resource root.
    Creates one unique child there. Uses existing credentials and pins; never enrolls an account,
    prompts for trust, starts a worker or installs a server. Direct and Tunnel are forced routes.
    Writable checks mkdir, verified upload/move, rename, verified download/checked delete and rmdir.
    ReadOnly records the observed mkdir refusal only; it does not claim other verbs were tested.
    A successful run proves these verbs and hashes, not the UI, SAF grant metadata or conformance.
.EXAMPLE
    .\tests\Integration\ManagerExchangeLive.ps1 -ResourceId saved-id -TestRoot /Photos/expendable -Producer Android -Route Tunnel -Access Writable
#>
[CmdletBinding()]
param(
    [string]$ResourceId = '',
    [string]$TestRoot = '',
    [ValidateSet('Windows', 'Android', '')][string]$Producer = '',
    [ValidateSet('Direct', 'Tunnel')][string]$Route = 'Direct',
    [ValidateSet('Writable', 'ReadOnly')][string]$Access = 'Writable'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/lib/Verdict.ps1')
trap { Write-Host $_.Exception.Message; Exit-Verdict 'ManagerExchangeLive' 2 'the harness could not verify the supplied root' }
Write-Subject 'ManagerExchangeLive' "manager transfers; producer=$Producer route=$Route access=$Access"
if (-not $ResourceId -or -not $TestRoot -or -not $Producer) {
    Exit-Verdict 'ManagerExchangeLive' 2 'supply -ResourceId, -TestRoot and -Producer for an expendable test directory'
}
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { Exit-Verdict 'ManagerExchangeLive' 2 'dotnet not found' }
$names = @('FMS261_RESOURCE', 'FMS261_ROOT', 'FMS261_PRODUCER', 'FMS261_ROUTE', 'FMS261_ACCESS')
$values = @($ResourceId, $TestRoot, $Producer, $Route, $Access)
$previous = @{}
$log = Join-Path ([IO.Path]::GetTempPath()) ('fms-261-live-' + [Guid]::NewGuid().ToString('N') + '.log')
try {
    for ($i = 0; $i -lt $names.Count; $i++) {
        $previous[$names[$i]] = [Environment]::GetEnvironmentVariable($names[$i], 'Process')
        [Environment]::SetEnvironmentVariable($names[$i], $values[$i], 'Process')
    }
    $project = Join-Path $repo 'tests/Lite.Tests/Lite.Tests.vbproj'
    & $dotnet test $project -f net10.0-windows --filter 'FullyQualifiedName~ManagerExchangeLiveTests' --logger 'console;verbosity=detailed' -p:NuGetAudit=false *> $log
    $testExit = $LASTEXITCODE
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
Write-Host "Log: $log"
$evidence = @(Select-String -LiteralPath $log -Pattern 'FMS261-EVIDENCE ')
foreach ($row in $evidence) { Write-Host $row.Line.Trim() }
if ($testExit -ne 0) {
    Get-Content -LiteralPath $log -Tail 18 | ForEach-Object { Write-Host $_ }
    Exit-Verdict 'ManagerExchangeLive' 1 'the requested live check failed; inspect the kept log'
}
if ($evidence.Count -ne 1) { Exit-Verdict 'ManagerExchangeLive' 2 'no unique live evidence record; a skipped test is not proof' }
Exit-Verdict 'ManagerExchangeLive' 0 'observed verbs and byte hashes recorded; UI and shared conformance remain unverified'
