<#
.SYNOPSIS
    Builds fms-exchange-server.exe (the exchange server, specification 133) reproducibly:
    windows/amd64, no cgo, stamped with the build version, with a PE version resource and a
    .sha256 sidecar.

.DESCRIPTION
    This is a BUILD step, not a check: it prints a plain result instead of a CHECK-VERDICT line.

    Two guards copied from the Share worker's build (docs: R2 section 1 of 133_research):
      * The `go` first on PATH here can be the 32-bit toolchain. A 64-bit Go is resolved explicitly
        (never PATH order alone) and GOARCH=amd64 is pinned.
      * The PE machine type of the result is read back and must be 0x8664.

    The version stamp reaches the exe twice, and the second is what the artifact check reads:
      * -ldflags "-X main.version=<stamp>" (printed by `fms-exchange-server --version`);
      * a PE version resource made by goversioninfo v1.4.1 (FileVersion string = the stamp
        exactly), so tools\Test-ArtifactVersion.ps1 can judge the exe (PACKAGE-VERSIONING P5).
      The resource is generated for this one build and removed again - it is never committed.

.PARAMETER Version
    The build stamp, YY.M.D.HHmm. Default: the current minute (a caller that builds several
    programs passes ONE stamp to all of them).

.PARAMETER OutDir
    Output folder. Default: exchange-server\bin.

.PARAMETER Linux
    Also cross-build GOOS=linux GOARCH=amd64 into <OutDir>\linux\ as a compile proof that no
    Windows assumption left the deployment layer (133 section 4). Not a shipped artifact.

.OUTPUTS
    Exit codes: 0 built; 1 a build step failed; 2 could not build because no 64-bit Go is
    installed (a clone without Go builds everything else and says the exchange server was
    skipped - it is never reported as built).

.EXAMPLE
    .\exchange-server\build.ps1 -Version 26.10.6.1630
#>
[CmdletBinding()]
param(
    [string]$Version = (Get-Date -Format 'yy.M.d.HHmm'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin'),
    [switch]$Linux
)

$ErrorActionPreference = 'Stop'
$ModuleDir = $PSScriptRoot
$CmdDir    = Join-Path $ModuleDir 'cmd\fms-exchange-server'
$ExeName   = 'fms-exchange-server.exe'
$Syso      = Join-Path $CmdDir 'resource_windows_amd64.syso'
$GoVersionInfo = 'github.com/josephspurrier/goversioninfo/cmd/goversioninfo@v1.4.1'

if ($Version -notmatch '^(\d{1,2})\.(\d{1,2})\.(\d{1,2})\.(\d{4})$') {
    Write-Host "exchange server: build FAILED - version '$Version' is not YY.M.D.HHmm."
    exit 1
}
$vMajor = [int]$Matches[1]; $vMinor = [int]$Matches[2]; $vPatch = [int]$Matches[3]; $vBuild = [int]$Matches[4]

# --- a 64-bit Go, resolved explicitly ----------------------------------------------------------
function Resolve-Go64 {
    $candidates = @()
    foreach ($root in @($env:ProgramW6432, $env:ProgramFiles)) {
        if ($root) {
            $p = Join-Path $root 'Go\bin\go.exe'
            if (Test-Path $p) { $candidates += $p }
        }
    }
    foreach ($c in @(Get-Command go -All -ErrorAction SilentlyContinue)) { $candidates += $c.Source }
    foreach ($c in ($candidates | Select-Object -Unique)) {
        $arch = $null
        try { $arch = (& $c env GOHOSTARCH 2>$null) } catch { }
        if ($arch -eq 'amd64') { return $c }
    }
    return $null
}

$go = Resolve-Go64
if (-not $go) {
    Write-Host 'exchange server: SKIPPED (no 64-bit Go found - install Go and re-run; nothing was built)'
    exit 2
}

# Run a native command with temporary environment variables and restore them afterwards.
function Invoke-Go([hashtable]$Env, [string]$WorkDir, [string[]]$GoArgs) {
    $saved = @{}
    foreach ($k in $Env.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k, 'Process'); [Environment]::SetEnvironmentVariable($k, $Env[$k], 'Process') }
    Push-Location $WorkDir
    try {
        $prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $out = @(& $go @GoArgs 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prevEap
        return [pscustomobject]@{ Code = $code; Output = $out }
    } finally {
        Pop-Location
        foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k], 'Process') }
    }
}

$windowsEnv = @{ GOOS = 'windows'; GOARCH = 'amd64'; CGO_ENABLED = '0'; GOFLAGS = '' }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$Exe    = Join-Path $OutDir $ExeName
$Sha    = "$Exe.sha256"
Remove-Item -LiteralPath $Exe, $Sha -Force -ErrorAction SilentlyContinue

try {
    # --- the PE version resource ---------------------------------------------------------------
    $gviArgs = @('run', $GoVersionInfo, '-64',
        '-ver-major', $vMajor, '-ver-minor', $vMinor, '-ver-patch', $vPatch, '-ver-build', $vBuild,
        '-product-ver-major', $vMajor, '-product-ver-minor', $vMinor, '-product-ver-patch', $vPatch, '-product-ver-build', $vBuild,
        '-file-version', $Version, '-product-version', $Version,
        '-o', $Syso, (Join-Path $ModuleDir 'versioninfo.json'))
    $neutral = $env:TEMP
    $r = Invoke-Go @{ GOOS = ''; GOARCH = ''; CGO_ENABLED = '0'; GOFLAGS = '' } $neutral $gviArgs
    if ($r.Code -ne 0) {
        # Offline: the same module version from the local module cache, as a file proxy.
        $cache = (Invoke-Go @{} $neutral @('env', 'GOMODCACHE')).Output | Select-Object -First 1
        $proxy = 'file:///' + ((Join-Path $cache 'cache\download') -replace '\\', '/')
        $r = Invoke-Go @{ GOOS = ''; GOARCH = ''; CGO_ENABLED = '0'; GOFLAGS = '-mod=mod'; GOPROXY = $proxy; GOSUMDB = 'off' } $neutral $gviArgs
    }
    if ($r.Code -ne 0 -or -not (Test-Path $Syso)) {
        $r.Output | ForEach-Object { Write-Host "  $_" }
        Write-Host 'exchange server: build FAILED - could not generate the PE version resource (goversioninfo v1.4.1).'
        exit 1
    }

    # --- the build -----------------------------------------------------------------------------
    $ldflags = "-s -w -X main.version=$Version"
    $r = Invoke-Go $windowsEnv $ModuleDir @('build', '-trimpath', '-ldflags', $ldflags, '-o', $Exe, './cmd/fms-exchange-server')
    if ($r.Code -ne 0 -or -not (Test-Path $Exe)) {
        $r.Output | ForEach-Object { Write-Host "  $_" }
        Write-Host 'exchange server: build FAILED (go build).'
        exit 1
    }

    if ($Linux) {
        $linuxDir = Join-Path $OutDir 'linux'
        New-Item -ItemType Directory -Path $linuxDir -Force | Out-Null
        $r = Invoke-Go @{ GOOS = 'linux'; GOARCH = 'amd64'; CGO_ENABLED = '0'; GOFLAGS = '' } $ModuleDir `
            @('build', '-trimpath', '-ldflags', $ldflags, '-o', (Join-Path $linuxDir 'fms-exchange-server'), './cmd/fms-exchange-server')
        if ($r.Code -ne 0) {
            $r.Output | ForEach-Object { Write-Host "  $_" }
            Write-Host 'exchange server: build FAILED (GOOS=linux cross-build: a Windows assumption left the deployment layer).'
            exit 1
        }
        Write-Host "exchange server: linux cross-build OK -> $linuxDir"
    }
} finally {
    Remove-Item -LiteralPath $Syso -Force -ErrorAction SilentlyContinue
}

# --- prove the artifact, not the command -------------------------------------------------------
$bytes = [System.IO.File]::ReadAllBytes($Exe)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$machine  = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x8664) {
    Remove-Item -LiteralPath $Exe -Force
    Write-Host ("exchange server: build FAILED - PE machine type is 0x{0:X4}, want 0x8664 (amd64). A 32-bit binary would see %ProgramFiles% as Program Files (x86)." -f $machine)
    exit 1
}
$fileVersion = (Get-Item -LiteralPath $Exe).VersionInfo.FileVersion
if ($fileVersion -ne $Version) {
    Remove-Item -LiteralPath $Exe -Force
    Write-Host "exchange server: build FAILED - the exe carries FileVersion '$fileVersion', want '$Version'."
    exit 1
}
$printed = (& $Exe --version 2>$null | Select-Object -First 1)
if ($printed -ne $Version) {
    Remove-Item -LiteralPath $Exe -Force
    Write-Host "exchange server: build FAILED - '--version' prints '$printed', want '$Version'."
    exit 1
}

# <hash>  <name>, ASCII, no trailing newline: the worker's sidecar form.
$hash = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText($Sha, "$hash  $ExeName", [System.Text.Encoding]::ASCII)

Write-Host "exchange server: built $Exe (version $Version, amd64, sha256 $($hash.Substring(0, 12)).. )"
exit 0
