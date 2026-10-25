<#
.SYNOPSIS
    Live acceptance of the viewer's SFTP consumer (ticket 007 / spec 100 T07) against a REAL SSH server:
    the worker repository's own `devserve` - the real SFTP stack serving one folder read-only - built into
    a temporary directory and started BY THE TESTS on a throw-away data directory, with SftpLiveTests run
    against it over a real SSH connection.

    Why a real server and not the in-memory fixture: the fixture proves the policy around a connection;
    only a real SSH server proves that the provider's exception classification, the host-key hook, the
    listing, the download and the refusal of writes are what SSH.NET really does - and devserve prints the
    very `.fmscfg` the Share Manager exports, so the same run proves that a real producer's document
    imports and opens with its key already verified.

    Safe by construction:
      - It never starts the worker and never touches \\.\pipe\fms-companion, so a running Share Manager or
        Server edition is not disturbed and does not block the run.
      - The server gets FMS_COMPANION_DATA_DIR on a temporary directory, so the owner's host key, credential,
        shares and listen port (all of which paired phones depend on) are never read or written. Building
        reads the worker repository and writes only to the temporary directory.
      - It removes everything it created, whatever the outcome.

    Needs: dotnet (the SDK), Go, and the worker repository beside this one (-WorkerRepo or
    FMS_WORKER_REPO). devserve is read-only by construction, so against it the write VERBS are proved by
    their refusal. Their success path - a recipient slot that names a server folder (007 section 4.2):
    probe, folder creation, a verified move and its undo, a copy that refuses to overwrite, a refused
    password, a key that is not the pin, a dropped link - runs against tests\Integration\rwsftp, a
    throw-away WRITABLE server (pkg/sftp, files in memory, 127.0.0.1 only) built offline from the Go
    module cache into the same temporary directory (RemoteSlotLiveTests).

    -SmbShare <UNC> additionally runs read-only SMB acceptance with the current Windows account:
    listing, byte-identical download, lease release and preservation of an existing Windows
    connection. It does not modify share files or store credentials. It does not cover a
    second account or the credential prompt; those need an isolated two-user environment.

    Exit codes (CHECK-VERDICT): 0 = every live case passed; 1 = a case failed; 2 = could not verify
    (no dotnet, no Go, no worker repository, the build failed). The last line is the verdict,
    `SftpConsumerLive: <WORD>`.
#>
param(
    [string]$WorkerRepo = '',
    # Explicit opt-in: read-only SMB provider acceptance with the current Windows account.
    [string]$SmbShare = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('fms-sftp-live-build-' + [Guid]::NewGuid().ToString('N'))
function Remove-FixtureBuild {
    $target = [IO.Path]::GetFullPath($work)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    if ([IO.Path]::GetDirectoryName($target).TrimEnd('\', '/') -ine $tempRoot -or
        [IO.Path]::GetFileName($target) -notmatch '^fms-sftp-live-build-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the owned fixture build directory'
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
trap {
    Write-Host "  $($_.Exception.Message)"
    try { Remove-FixtureBuild } catch {}
    Exit-Verdict 'SftpConsumerLive' 2 'the check itself failed'
}

Write-Subject 'SftpConsumerLive' 'the viewer SFTP provider and remote recipient slots against real SSH servers (devserve read-only, rwsftp writable)'
if ($SmbShare) { Write-Host '  Also checking the supplied SMB share read-only with the current Windows account.' }

function Find-Tool([string]$name, [string[]]$guesses) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($g in $guesses) { if (Test-Path $g) { return $g } }
    return $null
}

$dotnet = Find-Tool 'dotnet' @('C:\Program Files\dotnet\dotnet.exe')
if (-not $dotnet) { Exit-Verdict 'SftpConsumerLive' 2 'dotnet not found' }
$go = Find-Tool 'go' @('C:\Program Files\Go\bin\go.exe')
if (-not $go) { Exit-Verdict 'SftpConsumerLive' 2 'Go not found (it builds the server the cases talk to)' }

if (-not $WorkerRepo) { $WorkerRepo = $env:FMS_WORKER_REPO }
if (-not $WorkerRepo) { $WorkerRepo = Join-Path (Split-Path $repo -Parent) 'fms_companion' }
$WorkerRepo = [IO.Path]::GetFullPath($WorkerRepo)
if (-not (Test-Path (Join-Path $WorkerRepo 'cmd\devserve\main.go'))) {
    Exit-Verdict 'SftpConsumerLive' 2 "the worker repository was not found at $WorkerRepo (pass -WorkerRepo or set FMS_WORKER_REPO)"
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$exe = Join-Path $work 'devserve.exe'
Push-Location $WorkerRepo
try {
    # -mod=readonly: building must not edit the worker repository's go.mod/go.sum.
    & $go build -mod=readonly -o $exe ./cmd/devserve
    if ($LASTEXITCODE -ne 0) { Exit-Verdict 'SftpConsumerLive' 2 'building devserve failed' }
} finally { Pop-Location }

# The writable throw-away server for remote recipient slots (007 section 4.2): tests\Integration\rwsftp,
# built in a copy under the temporary directory - offline, from the Go module cache only (GOPROXY=off),
# so building never fetches anything and never writes a go.sum into this repository.
$rwBuild = Join-Path $work 'rwsftp'
$rwExe = Join-Path $work 'rwsftp.exe'
New-Item -ItemType Directory -Force -Path $rwBuild | Out-Null
foreach ($name in 'main.go', 'go.mod') { Copy-Item (Join-Path $PSScriptRoot "rwsftp\$name") $rwBuild }
Push-Location $rwBuild
$savedGo = @{ GOPROXY = $env:GOPROXY; GOFLAGS = $env:GOFLAGS; GOSUMDB = $env:GOSUMDB }
try {
    $env:GOPROXY = 'off'; $env:GOFLAGS = '-mod=mod'; $env:GOSUMDB = 'off'
    & $go build -o $rwExe .
    if ($LASTEXITCODE -ne 0) {
        Exit-Verdict 'SftpConsumerLive' 2 'building rwsftp failed (it needs github.com/pkg/sftp and golang.org/x/crypto in the Go module cache - build the worker repository once)'
    }
} finally {
    foreach ($k in @($savedGo.Keys)) {
        if ($null -eq $savedGo[$k]) { [Environment]::SetEnvironmentVariable($k, $null) } else { [Environment]::SetEnvironmentVariable($k, $savedGo[$k]) }
    }
    Pop-Location
}

$env:FMS_SFTP_LIVE_DEVSERVE = $exe
$env:FMS_SFTP_LIVE_RW = $rwExe
$savedSmbShare = $env:FMS_SMB_LIVE_SHARE
if ($SmbShare) { $env:FMS_SMB_LIVE_SHARE = $SmbShare }
$exit = 1
try {
    $project = Join-Path $repo 'tests\Lite.Tests\Lite.Tests.vbproj'
    $filter = 'FullyQualifiedName~SftpLiveTests|FullyQualifiedName~RemoteSlotLiveTests|FullyQualifiedName~ManagerArchiveLiveTests|FullyQualifiedName~ManagerExchangeLiveTests.Live_LoopbackWindowsExchange'
    if ($SmbShare) { $filter += '|FullyQualifiedName~SmbLiveTests' }
    & $dotnet test $project -f net10.0-windows --filter $filter --nologo
    $exit = $LASTEXITCODE
} finally {
    Remove-Item Env:FMS_SFTP_LIVE_DEVSERVE -ErrorAction SilentlyContinue
    Remove-Item Env:FMS_SFTP_LIVE_RW -ErrorAction SilentlyContinue
    $env:FMS_SMB_LIVE_SHARE = $savedSmbShare
    # The tests stop the servers they started; make sure no stray process keeps a temp exe locked.
    Get-Process -Name 'devserve', 'rwsftp' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe -or $_.Path -eq $rwExe } | ForEach-Object { try { $_.Kill() } catch {} }
    Start-Sleep -Milliseconds 300
    try { Remove-FixtureBuild } catch { Write-Host "  could not remove $work" }
}

if ($exit -eq 0) {
    $detail = 'every live case passed against real SSH servers'
    if ($SmbShare) { $detail += ' and the supplied SMB share (read-only, current Windows account)' }
    Exit-Verdict 'SftpConsumerLive' 0 $detail
}
Exit-Verdict 'SftpConsumerLive' 1 'a live case failed (see the dotnet test output above)'
