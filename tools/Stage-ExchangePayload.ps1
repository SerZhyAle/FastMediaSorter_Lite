<#
.SYNOPSIS
    Stages the exchange server's package tree (exchange\) into a package stage - the ONE place
    that says what that tree holds (specification 133, ticket 141).

.DESCRIPTION
    The tree is five files in the Server edition and four in the User edition (no service helper), and a package that claims the `exchange` component carries all of
    them or the build fails (a package claiming a component it lacks is worse than a failed
    build):

        exchange\fms-exchange-server.exe          from -Source (the module's build output)
        exchange\fms-exchange-server.exe.sha256   from -Source; checked against the exe here
        exchange\enable-exchange-server.ps1       publishing\installer\ (the firewall opt-in helper)
        exchange\install-exchange-service.ps1     publishing\installer\ (the Server edition's service helper; only with -Server)
        exchange\README.txt                       exchange-server\package\README.txt

    This script is only ever called when the shipping gate is open (-IncludeExchange of the
    packagers, INCLUDE_EXCHANGE of release.yml - owner rule, 133 section 8: nothing from the
    exchange module reaches a RELEASED package before rung 2 freezes the wire). With the gate
    shut nothing calls it, and the staged tree has no exchange\ folder.

.PARAMETER Source
    A folder holding fms-exchange-server.exe and its .sha256 sidecar: bin\Release\exchange
    (build.ps1 deployed it), bin\ExchangePublish, or the exchange\ folder of an already staged
    package.

.PARAMETER Destination
    The package stage root. exchange\ is created (and emptied first) inside it.

.OUTPUTS
    Throws on any missing or inconsistent file. It prints a plain result line, no verdict: this is
    a staging step, not a check.
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination,
    [switch]$Server
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path $PSScriptRoot -Parent
$exeName   = 'fms-exchange-server.exe'
$helpers   = @('enable-exchange-server.ps1')
# The service helper belongs to the Server edition only (133 section 6.1: a service is never created by a
# User install); the Share Manager tells the editions apart by that file being next to the exe.
if ($Server) { $helpers += 'install-exchange-service.ps1' }
$helperDir = Join-Path $repoRoot 'publishing\installer'
$readme    = Join-Path $repoRoot 'exchange-server\package\README.txt'

$exe = Join-Path $Source $exeName
$sha = "$exe.sha256"
foreach ($f in @($exe, $sha)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) {
        throw "The exchange server is not built: $f is missing. Build it first (build.ps1, or exchange-server\build.ps1) - a package never claims the component without it."
    }
}

# The sidecar form is "<hash>  <name>" (exchange-server\build.ps1); the hash must be the exe's.
$recorded = ((Get-Content -LiteralPath $sha -Raw).Trim() -split '\s+')[0]
$actual   = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if ($recorded -ine $actual) {
    throw "$sha records $recorded but $exe hashes to $actual - rebuild the exchange server instead of staging a stale pair."
}

$sources = @(
    foreach ($h in $helpers) { Join-Path $helperDir $h }
    $readme
)
foreach ($f in $sources) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) {
        throw "Exchange package file missing: $f - the component ships its helpers and README or it does not ship."
    }
}

$dest = Join-Path $Destination 'exchange'
if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item -LiteralPath $exe, $sha -Destination $dest -Force
foreach ($f in $sources) { Copy-Item -LiteralPath $f -Destination $dest -Force }

Write-Host "Staged the exchange server tree -> $dest ($((Get-ChildItem $dest -File).Count) files)"
