<#
.SYNOPSIS
    Build the SERVER edition from these sources and install it on THIS machine, then
    prove the running Windows service really is the build that was just made.

.DESCRIPTION
    The Server-edition twin of reinstall.ps1. That script covers the interactive
    products (the viewer + the Share Manager it launches); this one covers the always-on
    variant - the same Go SFTP worker, hosted by the Windows service
    FastMediaSorterCompanionSFTP instead of by a signed-in session
    (docs/specifications/done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md).

    Three steps:

      1. BUILD  - tools\Build-ServerInstaller.ps1 publishes the Share Manager, stages it
                  with the vendored worker and compiles dist\..-server-setup.exe.
                  Unelevated, and no v* tag: a local "сборка", never a "релиз".
      2. INSTALL- runs that setup.exe silently. This is the ONLY step that elevates, so
                  it is also the only UAC prompt. Inno's own AppId makes it an in-place
                  upgrade of whatever Server edition is already there.
      3. VERIFY - see below. This is the point of the script.

    WHY THE VERIFY BLOCK EXISTS. Setup exits 0 even when the service could not be
    registered: the .iss reports that through a message box, and a silent install is
    run with /SUPPRESSMSGBOXES, which answers it. So "setup.exe returned 0" says almost
    nothing here, and each of these is checked instead:

      * the ARP DisplayVersion is the version just built - i.e. the package really was
        replaced, rather than an older one being left in place;
      * the service exists, is Running, and starts automatically;
      * the worker the SERVICE is registered against hashes equal to
        payload\companion\fms-share-worker.exe - the one check that answers "am I
        actually testing my build". The service runs a staged COPY of the worker under
        the machine state directory, so the file next to the console is not evidence;
      * the host-key fingerprint is unchanged. Phones TOFU-pin it, so a reinstall that
        silently rotated the identity would unpair every one of them - and it would
        look like a perfectly successful install until someone picked up a phone;
      * the control pipe answers the frozen v1 protocol
        (tests\Integration\WorkerRoundTrip.ps1).

    A failing check prints what it saw and sets a non-zero exit code; the two logs
    worth reading are named at the end.

    STATE IS KEPT, ALWAYS. Neither the upgrade nor -Clean touches
    %ProgramData%\FastMediaSorterCompanion - the host key, the credentials, the folder
    list and the stats all survive, by design of the uninstaller. Removing that
    directory is a separate, deliberate act and this script never does it.

.PARAMETER Version
    Version stamped into the package and shown in "Apps & features". Defaults to a
    fresh yy.M.d.HHmm timestamp, which is what makes "the installed version is the one
    I just built" a check rather than a hope.

.PARAMETER SkipBuild
    Install the newest dist\..-server-setup.exe instead of building one. Useful for
    re-testing the installer itself, not for testing a source change.

.PARAMETER Clean
    Uninstall the installed Server edition before installing, instead of upgrading in
    place. Slower, and it drops the service registration, the firewall rule and the
    folder ACEs before putting them back - which is exactly what makes it worth having
    when the thing being tested IS the install path. State is still kept.

.PARAMETER NoLaunch
    Do not open the Share Manager afterwards.

.PARAMETER Force
    Skip the confirmation asked for a FIRST migration (see below). Nothing else prompts.

.EXAMPLE
    .\reinstall-server.ps1
    Build, upgrade in place, verify, open the Share Manager.

.EXAMPLE
    .\reinstall-server.ps1 -Clean
    Full uninstall/install cycle - use when the install path itself is what changed.

.EXAMPLE
    .\reinstall-server.ps1 -SkipBuild -NoLaunch
    Reinstall the last package built, and just report.
#>
param(
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$Clean,
    [switch]$NoLaunch,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$ServiceName   = 'FastMediaSorterCompanionSFTP'
$PipeName      = 'fms-companion'
$MachineDataDir= Join-Path $env:ProgramData 'FastMediaSorterCompanion'
$VendoredWorker= Join-Path $root 'payload\companion\fms-share-worker.exe'
# The Server edition's own AppId (publishing\installer\FastMediaSorterServer.iss) - NOT
# the User edition's. Reading ARP through it is how this script knows which product it
# is looking at on a machine that has both.
$ServerArpKey  = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}_is1'
$UserArpLeaf   = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}_is1'

$failures = @()
function Check([string]$label, [bool]$ok, [string]$detail) {
    if ($ok) {
        Write-Host ("  PASS  {0}" -f $label) -ForegroundColor Green
        if ($detail) { Write-Host ("        {0}" -f $detail) -ForegroundColor DarkGray }
    } else {
        Write-Host ("  FAIL  {0}" -f $label) -ForegroundColor Red
        if ($detail) { Write-Host ("        {0}" -f $detail) -ForegroundColor Yellow }
        $script:failures += $label
    }
}

function Get-ServerArp {
    Get-ItemProperty -LiteralPath $ServerArpKey -ErrorAction SilentlyContinue
}

function Test-UserEditionInstalled {
    # Checked in both hives because the User installer offers a per-user and an
    # all-users mode.
    (Test-Path -LiteralPath "HKLM:\$UserArpLeaf") -or (Test-Path -LiteralPath "HKCU:\$UserArpLeaf")
}

# The exe the SERVICE is registered against, pulled out of its quoted ImagePath. The
# registration is "<exe>" --service --datadir "<dir>" [--manage-sid "<sids>"], so the
# first quoted token is the worker.
function Get-ServiceWorkerPath {
    $svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path -LiteralPath $svcKey)) { return $null }
    $image = (Get-ItemProperty -LiteralPath $svcKey -ErrorAction SilentlyContinue).ImagePath
    if (-not $image) { return $null }
    $m = [regex]::Match($image, '^\s*"([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value }
    return ($image -split '\s+')[0]
}

# The worker's own read-only verdict on a state directory. Asking the worker rather
# than parsing ed25519 here is the same choice install-share-service.ps1 makes: the
# installer and the thing that will load the key agree by construction.
function Get-HostKeyFingerprint {
    $worker = Get-ServiceWorkerPath
    if (-not $worker -or -not (Test-Path -LiteralPath $worker)) { $worker = $VendoredWorker }
    if (-not (Test-Path -LiteralPath $worker)) { return $null }
    try {
        $raw = & $worker --inspect-datadir $MachineDataDir 2>&1 | Out-String
        $info = $raw.Trim() | ConvertFrom-Json
        if ($info.hostKeyPresent) { return [string]$info.fingerprint }
    } catch { }
    return $null
}

function Get-Sha256([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

# Declining the UAC prompt surfaces as a Win32Exception out of ShellExecute, which
# would otherwise end the run in a stack trace. Refusing elevation is a decision, not
# a fault - say so and stop.
function Start-Elevated([string]$path, [string[]]$arguments) {
    try {
        return Start-Process -FilePath $path -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    } catch [System.ComponentModel.Win32Exception] {
        Write-Host "Elevation was declined - nothing was installed." -ForegroundColor Yellow
        exit 1
    }
}

# --- 0. what is on this machine right now ----------------------------------------

Write-Host "Fast Media Sorter - Folder Share SERVER edition: build + reinstall locally" -ForegroundColor Cyan
Write-Host ""

$arpBefore          = Get-ServerArp
$fingerprintBefore  = Get-HostKeyFingerprint
$hasMachineIdentity = Test-Path -LiteralPath (Join-Path $MachineDataDir 'hostkey')
$userEdition        = Test-UserEditionInstalled

Write-Host "Installed now:  $(if ($arpBefore) { $arpBefore.DisplayVersion } else { '(none)' })"
Write-Host "Service:        $(if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { (Get-Service -Name $ServiceName).Status } else { '(not registered)' })"
Write-Host "Host key:       $(if ($fingerprintBefore) { $fingerprintBefore } else { '(none yet)' })"
Write-Host "User edition:   $(if ($userEdition) { 'installed' } else { 'not installed' })"
Write-Host ""

# The one genuinely one-way step in this script. A first Server install on a machine
# that has been sharing from the User edition MOVES that per-user state - host key,
# password, folder list, port - into the machine store and hands the sharing to the
# service. Re-running it later is a plain upgrade, so this asks exactly once.
if ($userEdition -and -not $hasMachineIdentity -and -not $Force) {
    Write-Host "A User edition is installed and there is no machine identity yet." -ForegroundColor Yellow
    Write-Host "Installing the Server edition will MIGRATE the per-user share state (host key," -ForegroundColor Yellow
    Write-Host "password, folder list, port) into $MachineDataDir and hand sharing to the service." -ForegroundColor Yellow
    Write-Host "Paired phones keep working - the host key is carried over, never regenerated." -ForegroundColor Yellow
    $answer = Read-Host "Type 'migrate' to proceed (anything else aborts)"
    if ($answer -ne 'migrate') {
        Write-Host "Aborted - nothing was built or installed." -ForegroundColor Yellow
        exit 1
    }
    Write-Host ""
}

# --- 1. build the package ---------------------------------------------------------

if ($SkipBuild) {
    $setup = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*-server-setup.exe' -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { Write-Error "-SkipBuild was given but dist\ holds no *-server-setup.exe. Run without -SkipBuild first."; exit 1 }
    $setupPath = $setup.FullName
    # The version to verify against comes from the file name, since nothing was stamped
    # in this run.
    $m = [regex]::Match($setup.Name, '^FastMediaSorter-(.+)-windows-x64-server-setup\.exe$')
    $Version = if ($m.Success) { $m.Groups[1].Value } else { '' }
    Write-Host "Reusing the existing package: $setupPath" -ForegroundColor Cyan
} else {
    if (-not $Version) { $Version = Get-Date -Format 'yy.M.d.HHmm' }
    Write-Host "Building the Server installer, version $Version.." -ForegroundColor Cyan
    & (Join-Path $root 'tools\Build-ServerInstaller.ps1') -Version $Version
    if ($LASTEXITCODE -ne 0) { Write-Error "tools\Build-ServerInstaller.ps1 failed (exit $LASTEXITCODE) - nothing installed."; exit $LASTEXITCODE }
    $setupPath = Join-Path $root ("dist\FastMediaSorter-$Version-windows-x64-server-setup.exe")
    if (-not (Test-Path -LiteralPath $setupPath)) { Write-Error "Expected package not found: $setupPath"; exit 1 }
}

Write-Host ""

# --- 2. uninstall first, if asked -------------------------------------------------

if ($Clean -and $arpBefore -and $arpBefore.UninstallString) {
    # Inno's uninstaller copies itself to %TEMP% and the process that was started
    # returns immediately, so -Wait proves nothing. The ARP key disappearing is the
    # real completion signal.
    $unins = $arpBefore.UninstallString.Trim('"')
    Write-Host "Uninstalling the installed Server edition first (-Clean).." -ForegroundColor Cyan
    Start-Elevated $unins @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') | Out-Null
    $deadline = (Get-Date).AddSeconds(120)
    while ((Test-Path -LiteralPath $ServerArpKey) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (Test-Path -LiteralPath $ServerArpKey) {
        Write-Error "The uninstaller did not finish within 120 s - stopping rather than installing over a half-removed product."
        exit 1
    }
    Write-Host "Uninstalled (the state directory was deliberately kept)." -ForegroundColor DarkGray
    Write-Host ""
}

# --- 3. install -------------------------------------------------------------------

$setupLog = Join-Path $env:TEMP ("fms-server-setup-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

# /MIGRATEFROMUSER is what a SILENT install needs when a User edition is present: the
# .iss refuses to migrate state unattended without it (spec §1.4). It only lifts that
# refusal - with nothing to migrate the post-install step is a plain fresh install - so
# it is passed unconditionally and the confirmation above is where the decision is
# really made.
$setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
               '/MIGRATEFROMUSER=yes', "/LOG=$setupLog")

Write-Host "Installing (this is the one UAC prompt).." -ForegroundColor Cyan
$proc = Start-Elevated $setupPath $setupArgs
if ($proc.ExitCode -ne 0) {
    Write-Error "setup.exe exited with code $($proc.ExitCode). Log: $setupLog"
    exit $proc.ExitCode
}

# The service is registered from ssPostInstall and starts delayed-auto, so it can still
# be in StartPending for a moment after Setup returns.
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') { break }
    Start-Sleep -Milliseconds 500
}

Write-Host ""

# --- 4. verify --------------------------------------------------------------------

Write-Host "Verifying the installation:" -ForegroundColor Cyan

$arpAfter = Get-ServerArp
Check "the installed package is the one just built" `
      ($null -ne $arpAfter -and (-not $Version -or $arpAfter.DisplayVersion -eq $Version)) `
      ("expected $Version, ARP says $(if ($arpAfter) { $arpAfter.DisplayVersion } else { '(not installed)' })")

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Check "the Windows service is registered and running" `
      ($null -ne $svc -and $svc.Status -eq 'Running') `
      ("status=$(if ($svc) { $svc.Status } else { '(not registered)' }) startType=$(if ($svc) { $svc.StartType } else { '-' })")

# The check that answers "am I testing MY build". The service runs a staged COPY under
# the machine state directory, so the exe sitting next to the console proves nothing.
$serviceWorker = Get-ServiceWorkerPath
$serviceHash   = Get-Sha256 $serviceWorker
$vendoredHash  = Get-Sha256 $VendoredWorker
Check "the service runs the worker vendored in this repository" `
      ($null -ne $serviceHash -and $serviceHash -eq $vendoredHash) `
      ("service: $serviceWorker`n        sha256 service=$serviceHash repo=$vendoredHash")

# Phones TOFU-pin this. A reinstall that rotated it would look entirely successful
# right up to the moment somebody picked up a phone.
$fingerprintAfter = Get-HostKeyFingerprint
if ($fingerprintBefore) {
    Check "the host key survived the reinstall (paired phones keep working)" `
          ($fingerprintAfter -eq $fingerprintBefore) `
          ("before $fingerprintBefore`n        after  $fingerprintAfter")
} else {
    Check "a host key exists" ($null -ne $fingerprintAfter) ("fingerprint $fingerprintAfter (new identity - nothing was paired before)")
}

$pipeUp = Test-Path -LiteralPath "\\.\pipe\$PipeName"
Check "the control pipe is listening" $pipeUp "\\.\pipe\$PipeName"

if ($pipeUp) {
    # The protocol check itself is already written and already used by the test runner -
    # reuse it rather than re-implementing the round trip. Note it SKIPS (exit 0) when
    # no worker answers, which is why the pipe is asserted separately above.
    Write-Host "  ---- tests\Integration\WorkerRoundTrip.ps1 ----" -ForegroundColor DarkGray
    & (Join-Path $root 'tests\Integration\WorkerRoundTrip.ps1')
    Check "the worker speaks the frozen v1 protocol" ($LASTEXITCODE -eq 0) "WorkerRoundTrip.ps1 exit $LASTEXITCODE"
}

Write-Host ""

# --- 5. launch the console --------------------------------------------------------

$installDir   = if ($arpAfter) { $arpAfter.InstallLocation } else { $null }
$companionExe = if ($installDir) { Join-Path $installDir 'FastMediaSorterCompanion.exe' } else { $null }

if (-not $NoLaunch -and $companionExe -and (Test-Path -LiteralPath $companionExe)) {
    # --show, not a bare launch: the Manager's own "open the window at startup" option
    # is off by default, so without it this would start a tray-only process and read as
    # a click that did nothing. Unelevated on purpose - it drives the service over the
    # pipe as the enrolled management user, which is exactly the path being tested.
    Write-Host "Opening the Share Manager: $companionExe" -ForegroundColor Green
    Start-Process -FilePath $companionExe -ArgumentList '--show' -WorkingDirectory $installDir
} elseif (-not $NoLaunch) {
    Write-Warning "Share Manager exe not found (install location: $installDir)"
}

Write-Host ""
Write-Host "Setup log:    $setupLog" -ForegroundColor DarkGray
Write-Host "Service log:  $(Join-Path $MachineDataDir 'install-share-service.log')" -ForegroundColor DarkGray

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host ("$($failures.Count) check(s) FAILED: " + ($failures -join '; ')) -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Server edition $Version is installed and serving." -ForegroundColor Green
exit 0
