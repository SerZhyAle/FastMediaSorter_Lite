<#
.SYNOPSIS
    The single elevated, auditable helper for the Fast Media Sorter Server edition
    (SPECIFICATION_SHARE_SYSTEM_SERVICE.md §3.2, §3.3, §3.4, §4.3).

.DESCRIPTION
    Every machine-affecting step of the always-on Folder Share Server lives here and
    nowhere else: registering / repairing / removing the Windows service, creating the
    machine state directory with its ACL, granting a shared folder read access for the
    service account, the firewall rule, and the two state migrations.

    It is called by the elevated Server installer and, at runtime, by the Share
    Manager's Hosting console through one ShellExecute "runas" (a single UAC prompt).
    Nothing on the phone-facing path can reach it: no IPC command, no silent User
    edition install and no worker code path creates a service or widens an ACL.

    Two rules the code below keeps everywhere:
      * the host key is NEVER regenerated or overwritten. A migration validates the
        fingerprint before and after the copy and aborts on any mismatch, because a
        new identity would silently break every phone that TOFU-pinned the old one.
      * only what this script added is removed. Root ACEs are recorded in a ledger,
        and a pre-existing LOCAL SERVICE grant is left alone on revoke.

    Exit code 0 = success. Any non-zero code is surfaced by the caller as a failure;
    the reason is appended to <DataDir>\install-share-service.log.

.PARAMETER Action
    install | repair | remove | start | stop | migrate-to-server | migrate-to-user |
    grant-roots
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('install', 'repair', 'remove', 'start', 'stop', 'restart', 'migrate-to-server', 'migrate-to-user', 'grant-roots')]
    [string]$Action,

    # Absolute path of companion\fms-share-worker.exe in the installed folder.
    [string]$ExePath,

    # Machine state directory (%ProgramData%\FastMediaSorterCompanion).
    [string]$DataDir,

    # Per-user state directory of the User edition (the migration source/target).
    [string]$UserDataDir,

    # String SID(s) allowed to drive the control pipe. Comma-separated.
    [string]$ManageSid,

    # Pipe-separated folder paths for grant-roots.
    [string]$Roots,

    # Pipe-separated subset of -Roots that is shared READ-ONLY. Those get (RX); every
    # other root gets (M), because a writable share the account cannot write to is a
    # share that fails at the moment the phone tries to use it - and it fails as an
    # SFTP permission error, far from the folder list that promised otherwise.
    # Omitting the parameter entirely keeps the old read-only-for-everything behaviour,
    # so an older caller never silently widens access.
    [string]$ReadOnlyRoots
)

$ErrorActionPreference = 'Stop'

$ServiceName    = 'FastMediaSorterCompanionSFTP'
$ServiceDisplay = 'Fast Media Sorter Folder Share'
$ServiceDesc    = 'Serves the folders you selected in Fast Media Sorter over SFTP, so an Android phone can reach them even when nobody is signed in.'
$FirewallRule   = 'FastMediaSorter Companion SFTP'
$LedgerName     = 'granted-roots.json'

# The service runs its OWN copy of the worker, staged under the machine state
# directory, never the one inside the application folder. Three reasons, in order of
# how badly each bites:
#   * a per-user installation lives under %LOCALAPPDATA%\Programs, whose ACL grants
#     the user, SYSTEM and Administrators - and NOT LOCAL SERVICE. A service
#     registered against that path cannot even start (access denied), which is exactly
#     the installation shape winget produces, and now the shape that can switch itself
#     into service mode from the Share Manager;
#   * the service holds its binary open, so an application update could not replace a
#     file it is running - the copy moves that conflict out of the installer's way;
#   * the service keeps working when the app is uninstalled, which is the honest
#     behaviour for a machine-wide role someone deliberately enabled.
$BinDirName = 'bin'

# Set by Sync-ServiceBinaries: the executable the SERVICE is registered against and
# the one the firewall rule must name. Until then, the caller-supplied path.
$script:ServiceExePath = $ExePath

# Well-known SIDs, used instead of names: icacls resolves "*S-1-5-19" identically on
# every Windows language, while "LOCAL SERVICE" fails outright on a localized system.
$SidSystem      = 'S-1-5-18'
$SidLocalService= 'S-1-5-19'
$SidAdmins      = 'S-1-5-32-544'

# --- logging ------------------------------------------------------------------

$script:LogPath = $null

function Initialize-Log {
    if (-not $DataDir) { return }
    try {
        if (-not (Test-Path -LiteralPath $DataDir)) {
            New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
        }
        $script:LogPath = Join-Path $DataDir 'install-share-service.log'
    } catch { $script:LogPath = $null }
}

function Write-Log([string]$message) {
    $line = "{0} {1}" -f (Get-Date).ToUniversalTime().ToString('s'), $message
    Write-Output $line
    if ($script:LogPath) {
        try { Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 } catch { }
    }
}

function Fail([string]$message, [int]$code = 1) {
    Write-Log "FAILED: $message"
    exit $code
}

# --- guards -------------------------------------------------------------------

function Assert-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Fail 'this helper must run elevated (administrator rights are required for the service, its ACLs and the firewall rule)' 5
    }
}

function Assert-Worker {
    if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) {
        Fail "worker executable not found: '$ExePath'" 2
    }
}

function Assert-DataDir {
    if (-not $DataDir) { Fail 'no -DataDir given' 2 }
}

# --- worker invocation --------------------------------------------------------

# Runs the worker and returns its exit code and streams.
#
# It has to go through Process with redirected streams rather than the obvious
# "& $ExePath ..": the worker is linked as a WINDOWS GUI image (PE subsystem 2 -
# it must never flash a console on the user's desktop), and PowerShell neither
# waits for nor captures the output of such a process when it is called directly.
# The call returned instantly, $LASTEXITCODE stayed EMPTY and the captured text
# was "", which every caller here read as "the verdict failed" - that is what
# aborted the migration with an empty reason. Redirected streams work the same
# for a GUI image as for a console one.
function Invoke-Worker([string]$workerArgs) {
    Assert-Worker
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $ExePath
    $psi.Arguments              = $workerArgs
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.CreateNoWindow         = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    # Read BEFORE WaitForExit: a worker that filled a pipe buffer would deadlock
    # against a parent that waits first.
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; StdOut = $out; StdErr = $err }
}

# Quotes one path for the worker's command line. TrimEnd('\') matters: a trailing
# backslash right before the closing quote is an escape to the Windows argument
# parser, so "C:\dir\" would arrive as C:\dir" and the path would not resolve.
function Quote-Path([string]$path) {
    return '"' + $path.TrimEnd('\') + '"'
}

# --- service ------------------------------------------------------------------

function Get-ServiceOrNull {
    return (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)
}

function Stop-ServiceGracefully {
    $svc = Get-ServiceOrNull
    if (-not $svc) { return }
    if ($svc.Status -eq 'Stopped') { return }
    Write-Log "stopping $ServiceName .."
    try { Stop-Service -Name $ServiceName -Force -ErrorAction Stop } catch { Write-Log "stop returned: $_" }
    # Bounded wait: the worker releases the SFTP listener, the UPnP mapping and the
    # mDNS registration on the way out, and a file-replacing install must not start
    # until it has. Past the bound we report rather than hang the installer.
    try { (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } catch { }
    $svc = Get-ServiceOrNull
    if ($svc -and $svc.Status -ne 'Stopped') { Write-Log "WARNING: $ServiceName is still $($svc.Status)" }
}

function Remove-ServiceRegistration {
    if (-not (Get-ServiceOrNull)) { return }
    Stop-ServiceGracefully
    Write-Log "deleting $ServiceName .."
    & sc.exe delete $ServiceName | Out-Null
    # The SCM keeps a deleted service until the last handle closes; give it a moment
    # so an immediately following create does not hit ERROR_SERVICE_MARKED_FOR_DELETE.
    for ($i = 0; $i -lt 20 -and (Get-ServiceOrNull); $i++) { Start-Sleep -Milliseconds 250 }
}

function Get-StagedExePath {
    if (-not $DataDir -or -not $ExePath) { return '' }
    return (Join-Path (Join-Path $DataDir $BinDirName) (Split-Path -Leaf $ExePath))
}

# Puts the service's own copy of the worker in place and points $script:ServiceExePath
# at it. Idempotent, and a no-op when the caller already handed us the staged path
# (a repair run started from the staged copy would otherwise copy a file onto itself).
function Sync-ServiceBinaries {
    Assert-Worker
    Assert-DataDir

    $staged = Get-StagedExePath
    if ([IO.Path]::GetFullPath($ExePath) -ieq [IO.Path]::GetFullPath($staged)) {
        $script:ServiceExePath = $staged
        Write-Log "worker already staged at '$staged'"
        return
    }

    # Nothing may hold the file while it is replaced; on install/repair the service is
    # torn down right after this anyway.
    Stop-ServiceGracefully

    $binDir = Split-Path -Parent $staged
    if (-not (Test-Path -LiteralPath $binDir)) {
        New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    }
    Write-Log "staging worker: '$ExePath' -> '$staged'"
    Copy-Item -LiteralPath $ExePath -Destination $staged -Force
    $sidecar = "$ExePath.sha256"
    if (Test-Path -LiteralPath $sidecar) {
        Copy-Item -LiteralPath $sidecar -Destination "$staged.sha256" -Force
    }

    # The parent directory's ACL hands LOCAL SERVICE and the management user Modify,
    # which is right for state and wrong for code: the account that RUNS the binary
    # must not be able to rewrite it. Read+execute for LOCAL SERVICE, read for the
    # management user (the Share Manager compares this copy against the installed one
    # to notice when an app update left the service behind), full for SYSTEM/Admins.
    $aclArgs = @($binDir, '/inheritance:r',
                 '/grant:r', ('*{0}:(OI)(CI)F' -f $SidSystem),
                 '/grant:r', ('*{0}:(OI)(CI)F' -f $SidAdmins),
                 '/grant:r', ('*{0}:(OI)(CI)RX' -f $SidLocalService))
    foreach ($sid in (Split-Sids $ManageSid)) {
        $aclArgs += @('/grant:r', ('*{0}:(OI)(CI)RX' -f $sid))
    }
    & icacls.exe @aclArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "icacls could not secure '$binDir' (exit $LASTEXITCODE)" 4 }

    $script:ServiceExePath = $staged
}

# Drops the staged copy when the server role goes away. The state directory beside it
# is deliberately kept (it holds the pinned identity) - this removes only code.
function Remove-ServiceBinaries {
    $staged = Get-StagedExePath
    if (-not $staged) { return }
    $binDir = Split-Path -Parent $staged
    if (-not (Test-Path -LiteralPath $binDir)) { return }
    try {
        Remove-Item -LiteralPath $binDir -Recurse -Force -ErrorAction Stop
        Write-Log "removed the staged worker in '$binDir'"
    } catch {
        # A file still held by a stopping service must not fail the whole action - the
        # registration is already gone, which is what "remove the role" means.
        Write-Log "could not remove '$binDir': $_"
    }
}

function New-ServiceRegistration {
    Assert-Worker
    Assert-DataDir

    # New-Service (not "sc.exe create") on purpose: the binary path carries quoted
    # paths, and passing that through a native command's command line is exactly the
    # kind of quoting that breaks on one Windows PowerShell version and not another.
    # New-Service hands the string to CreateService as-is.
    $binPath = '"{0}" --service --datadir "{1}"' -f $script:ServiceExePath, $DataDir
    if ($ManageSid) { $binPath += ' --manage-sid "{0}"' -f $ManageSid }

    Write-Log "registering $ServiceName -> $binPath"
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $ServiceDisplay `
                -Description $ServiceDesc -StartupType Automatic | Out-Null

    # DELAYED automatic start: the worker needs a network stack for mDNS, UPnP and the
    # reachability probe. (The worker also retries on its own - SCM recovery cannot
    # help a service that started healthy but too early - but starting late is cheaper
    # than retrying.)
    & sc.exe config $ServiceName start= delayed-auto | Out-Null

    # LocalService: low local privilege, and it presents anonymous credentials on the
    # network. That last part is why a UNC root does not work under it - documented,
    # not worked around.
    & sc.exe config $ServiceName obj= 'NT AUTHORITY\LocalService' | Out-Null

    # Restart on process failure with a bounded, widening cadence. failureflag=1 is the
    # half that is easy to miss: without it Windows only reacts to a service that died
    # WITHOUT reporting a stop, and the worker host deliberately reports a stop with a
    # service-specific non-zero exit code when its main loop ends unexpectedly.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/120000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null
}

function Start-ServiceAndVerify {
    Write-Log "starting $ServiceName .."
    Start-Service -Name $ServiceName
    try { (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) } catch { }
    $svc = Get-ServiceOrNull
    if (-not $svc -or $svc.Status -ne 'Running') {
        Fail "the service did not reach Running (state: $(if ($svc) { $svc.Status } else { 'absent' }))" 3
    }
    Write-Log "$ServiceName is running"
}

# --- state directory ----------------------------------------------------------

function Set-MachineDataDirAcl {
    Assert-DataDir
    if (-not (Test-Path -LiteralPath $DataDir)) {
        New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    }
    # /inheritance:r + an explicit list: the directory holds the private host key and
    # the SFTP password, so the inherited "Users: read" that %ProgramData% hands down
    # by default must go.
    #
    # LocalService gets Modify (it writes stats, shares, settings). The enrolled
    # management user gets Modify TOO, and that is deliberate rather than lax: from
    # the moment this directory exists it is the ONE state store both hosts use, so
    # the foreground worker - which runs as that user whenever the service is stopped
    # - has to be able to write the same shares.json, settings.json and stats.json.
    # Read-only here would silently split the two editions into separate folder lists.
    # It is not a privilege escalation either: everything in the file (password
    # included) is already handed to that same user over the control pipe.
    Write-Log "locking down $DataDir"
    # NB: not $args - that is an automatic variable inside a function.
    $aclArgs = @($DataDir, '/inheritance:r',
                 '/grant:r', ('*{0}:(OI)(CI)F' -f $SidSystem),
                 '/grant:r', ('*{0}:(OI)(CI)F' -f $SidAdmins),
                 '/grant:r', ('*{0}:(OI)(CI)M' -f $SidLocalService))
    foreach ($sid in (Split-Sids $ManageSid)) {
        $aclArgs += @('/grant:r', ('*{0}:(OI)(CI)M' -f $sid))
    }
    & icacls.exe @aclArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "icacls could not secure '$DataDir' (exit $LASTEXITCODE)" 4 }
}

# The SID of the profile a path lives under, read from the profile list rather than
# guessed from the folder name (a profile directory does not have to match the account
# name, and a roamed or renamed one usually does not).
function Get-ProfileSidForPath([string]$path) {
    if (-not $path) { return $null }
    $full = $null
    try { $full = [IO.Path]::GetFullPath($path) } catch { return $null }
    $listKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
    foreach ($entry in (Get-ChildItem -Path $listKey -ErrorAction SilentlyContinue)) {
        $profileDir = (Get-ItemProperty -Path $entry.PSPath -Name 'ProfileImagePath' -ErrorAction SilentlyContinue).ProfileImagePath
        if (-not $profileDir) { continue }
        if ($full.StartsWith($profileDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return $entry.PSChildName
        }
    }
    return $null
}

# Who may drive the control pipe and write the shared state directory (spec §3.3:
# "The Server installer persists the authorized management SID(s)").
#
# It cannot come from the worker - in Session 0 its identity is LocalService, not the
# management user - and the Server installer cannot supply it either: Setup runs
# elevated, so every per-user path it expands names the ELEVATING administrator rather
# than the account that owns the share identity. Here the answer is knowable: the state
# directory being migrated names the right profile. With nothing to migrate (a fresh
# install) the installing user is the honest default. Without this the DACL would end
# up SYSTEM + Administrators only, and the Share Manager - an ordinary, non-elevated
# process - could not open the pipe at all.
function Resolve-ManagementSid([string]$explicit, [string]$stateDir) {
    if ($explicit) { return $explicit }
    $sid = Get-ProfileSidForPath $stateDir
    if ($sid) { return $sid }
    return ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
}

function Split-Sids([string]$value) {
    if (-not $value) { return @() }
    return @($value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# --- identity inspection / migration -----------------------------------------

# Asks the worker itself for a read-only verdict on a state directory. Using the
# worker (rather than re-implementing an ed25519 parse here) means the installer and
# the thing that will later load the key agree by construction.
function Get-StateInfo([string]$dir) {
    $run  = Invoke-Worker ('--inspect-datadir ' + (Quote-Path $dir))
    $text = ("$($run.StdOut)`n$($run.StdErr)").Trim()
    if ($run.ExitCode -ne 0) { Fail "state directory '$dir' did not validate (exit $($run.ExitCode)): $text" 6 }
    try { return ($text | ConvertFrom-Json) } catch { Fail "could not read the state verdict for '$dir': $text" 6 }
}

# Finds the User edition's state directory.
#
# -UserDataDir is only a HINT. This script runs elevated, and when the elevation went
# through a different administrator account, every per-user path the installer expanded
# points at THAT profile - not at the profile whose Share Manager actually holds the
# host key. So the hint is used when it really carries an identity, and otherwise the
# user profiles are scanned. Several candidates is a deliberate stop rather than a
# guess: picking the wrong one would migrate the wrong identity and silently unpair
# every phone that trusted the right one.
function Resolve-UserDataDir([string]$hint) {
    if ($hint -and (Test-Path -LiteralPath (Join-Path $hint 'hostkey'))) { return $hint }

    $usersRoot = Join-Path $env:SystemDrive 'Users'
    if (-not (Test-Path -LiteralPath $usersRoot)) { return $hint }
    $found = @(Get-ChildItem -LiteralPath $usersRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $candidate = Join-Path $_.FullName 'AppData\Local\FastMediaSorterCompanion'
        if (Test-Path -LiteralPath (Join-Path $candidate 'hostkey')) { $candidate }
    })
    if ($found.Count -eq 1) {
        Write-Log "found the User edition state in '$($found[0])'"
        return $found[0]
    }
    if ($found.Count -gt 1) {
        Fail ("several user profiles hold a Fast Media Sorter share identity (" + ($found -join '; ') +
              "). Migration stopped - re-run this helper with -UserDataDir naming the one to keep, so the wrong identity is not adopted.") 10
    }
    return $hint
}

function Copy-State([string]$from, [string]$to) {
    if (-not (Test-Path -LiteralPath $from)) {
        Write-Log "no state to migrate from '$from' - starting fresh"
        return
    }
    $source = Get-StateInfo $from
    if (-not $source.hostKeyPresent) {
        Write-Log "'$from' holds no host key yet - nothing to preserve"
    }

    if (Test-Path -LiteralPath $to) {
        $target = Get-StateInfo $to
        if ($target.hostKeyPresent -and $source.hostKeyPresent -and $target.fingerprint -ne $source.fingerprint) {
            # Two different identities. Overwriting either one breaks the phones paired
            # to it, so this is a stop, not a merge.
            Fail "'$to' already holds a DIFFERENT host key ($($target.fingerprint)) than '$from' ($($source.fingerprint)). Migration stopped - remove or back up one of them by hand, otherwise paired phones would need re-pairing." 7
        }
    } else {
        New-Item -ItemType Directory -Path $to -Force | Out-Null
    }

    Write-Log "migrating state '$from' -> '$to'"
    Get-ChildItem -LiteralPath $from -File | Where-Object { $_.Name -ne 'service.log' -and $_.Name -ne 'install-share-service.log' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $to $_.Name) -Force
    }

    if ($source.hostKeyPresent) {
        $after = Get-StateInfo $to
        if ($after.fingerprint -ne $source.fingerprint) {
            Fail "the host-key fingerprint changed during the copy ('$($source.fingerprint)' -> '$($after.fingerprint)'). Migration stopped BEFORE the service was registered; paired phones are untouched." 7
        }
        Write-Log "host-key fingerprint preserved: $($after.fingerprint)"
    }
}

# --- root ACL ledger ----------------------------------------------------------

function Get-Ledger {
    $path = Join-Path $DataDir $LedgerName
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    try { return @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json) } catch { return @() }
}

function Save-Ledger($entries) {
    $path = Join-Path $DataDir $LedgerName
    ($entries | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $path -Encoding UTF8
}

# Did LOCAL SERVICE already have an explicit grant here before we arrived?
#
# Compared by SID, never by the printed account name: icacls resolves the name through
# LookupAccountSid, so on a localized Windows it prints something other than
# "LOCAL SERVICE" and a text match would report "no pre-existing grant" on every
# non-English machine - after which the uninstaller would happily delete an ACE the
# administrator put there.
function Test-LocalServiceAce([string]$folder) {
    try {
        $acl = Get-Acl -LiteralPath $folder -ErrorAction Stop
    } catch {
        # Cannot read the ACL -> assume there IS a pre-existing grant, so revoke leaves
        # it alone. Failing safe here means "do not delete something we cannot see".
        return $true
    }
    foreach ($ace in $acl.Access) {
        if ($ace.IsInherited) { continue }   # only explicit ACEs are ours to worry about
        try {
            $sid = $ace.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
        } catch {
            continue
        }
        if ($sid -eq $SidLocalService -and $ace.AccessControlType -eq 'Allow') { return $true }
    }
    return $false
}

# Grants (OI)(CI)(RX) to LOCAL SERVICE on each folder and records exactly what was
# added. Never recursive-rewrites an ACL and never touches an existing administrator
# grant: it only ADDS one ACE, and the ledger remembers whether it had to.
function Grant-Roots([string[]]$folders, [string[]]$readOnlyFolders, [bool]$rightsKnown) {
    Assert-DataDir
    $ledger = @(Get-Ledger)
    $readOnlySet = @{}
    foreach ($ro in @($readOnlyFolders)) {
        if ($ro) { $readOnlySet[$ro.TrimEnd('\')] = $true }
    }
    foreach ($folder in $folders) {
        if (-not $folder) { continue }
        if ($folder -like '\\*') {
            # LocalService presents anonymous credentials on the network, so a UNC root
            # cannot be made to work by an ACL here. Say so precisely instead of adding
            # an ACE that changes nothing.
            Write-Log "SKIPPED '$folder': a network path cannot be served under LOCAL SERVICE (it authenticates anonymously). Use a local path, or a supported alternative service account."
            continue
        }
        if (-not (Test-Path -LiteralPath $folder)) {
            Write-Log "SKIPPED '$folder': not found"
            continue
        }
        # (M) for a writable root, (RX) for a read-only one. A root already in the
        # ledger is re-applied when its LEVEL changed - a folder flipped to writable in
        # the folder list has to gain write here too, and the old "already granted"
        # short-circuit is exactly how it would silently stay read-only.
        $rights = if (-not $rightsKnown -or $readOnlySet.ContainsKey($folder.TrimEnd('\'))) { 'RX' } else { 'M' }
        $existing = $ledger | Where-Object { $_.path -eq $folder } | Select-Object -First 1
        if ($existing) {
            $had = if ($existing.PSObject.Properties['rights']) { [string]$existing.rights } else { 'RX' }
            if ($had -eq $rights) {
                Write-Log "already granted ($rights): $folder"
                continue
            }
            Write-Log "re-granting '$folder': $had -> $rights"
        }

        $preExisting = if ($existing) { [bool]$existing.preExisting } else { Test-LocalServiceAce $folder }
        Write-Log ("granting LOCAL SERVICE {0} access on '{1}'" -f $(if ($rights -eq 'M') { 'read/write' } else { 'read' }), $folder)
        & icacls.exe $folder /grant ('*{0}:(OI)(CI)({1})' -f $SidLocalService, $rights) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Log "WARNING: icacls failed on '$folder' (exit $LASTEXITCODE)"
            continue
        }
        $ledger = @($ledger | Where-Object { $_.path -ne $folder })
        $ledger += [pscustomobject]@{ path = $folder; sid = $SidLocalService; rights = $rights; preExisting = [bool]$preExisting }
    }
    Save-Ledger $ledger
}

# Removes ONLY the ACEs this script recorded, and only where LOCAL SERVICE had no
# grant of its own before we arrived.
function Revoke-Roots {
    if (-not $DataDir -or -not (Test-Path -LiteralPath $DataDir)) { return }
    foreach ($entry in @(Get-Ledger)) {
        if ($entry.preExisting) {
            Write-Log "keeping the pre-existing LOCAL SERVICE grant on '$($entry.path)'"
            continue
        }
        if (-not (Test-Path -LiteralPath $entry.path)) { continue }
        Write-Log "removing our LOCAL SERVICE grant on '$($entry.path)'"
        & icacls.exe $entry.path /remove:g ('*{0}' -f $entry.sid) | Out-Null
    }
    $path = Join-Path $DataDir $LedgerName
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}

# --- firewall -----------------------------------------------------------------

function Set-FirewallRule([bool]$enable) {
    # Idempotent: drop any rule of this name first ("No rules match" exits non-zero
    # when there was none, which is fine).
    & netsh.exe advfirewall firewall delete rule name="$FirewallRule" | Out-Null
    if (-not $enable) {
        # By NAME alone this leaves rules behind for good. Ours is named, but the
        # worker also collects rules Windows itself writes: the first time it listens,
        # the firewall prompt appears and an "Allow" creates a rule named after the
        # program (fms-share-worker) that no removal of ours has ever touched. Delete
        # by PROGRAM path too - it catches every rule aimed at this exe whatever its
        # name, and cannot match another installation's, which has another path.
        # BOTH paths: the installed worker and the service's staged copy are two
        # different programs to the firewall.
        foreach ($program in @($ExePath, (Get-StagedExePath))) {
            if ($program) { & netsh.exe advfirewall firewall delete rule name=all program="$program" | Out-Null }
        }
        Write-Log 'firewall rule removed'
        return
    }
    Assert-Worker
    # Program-scoped (not port-scoped) so the rule survives the worker's dynamically
    # assigned listen port; all three profiles because a dedicated server's network is
    # usually classified Public. The program is whichever copy will actually listen -
    # the staged one once the service is registered against it, since a rule naming the
    # application folder's copy would not cover the process the service starts.
    & netsh.exe advfirewall firewall add rule name="$FirewallRule" dir=in action=allow `
        program="$script:ServiceExePath" protocol=TCP profile=domain,private,public enable=yes | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "could not add the firewall rule (netsh exit $LASTEXITCODE)" 8 }
    Write-Log 'firewall rule in place'
}

# --- actions ------------------------------------------------------------------

Initialize-Log
Assert-Elevated
Write-Log "action=$Action exe='$ExePath' dataDir='$DataDir'"

switch ($Action) {

    'install' {
        Assert-Worker
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Set-MachineDataDirAcl
        Sync-ServiceBinaries
        Remove-ServiceRegistration   # idempotent re-install over a previous registration
        New-ServiceRegistration
        Set-FirewallRule $true
        Start-ServiceAndVerify
    }

    'repair' {
        Assert-Worker
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Set-MachineDataDirAcl
        # Also re-stages the worker, which is what makes this the "the app updated but
        # the service is still running the old copy" repair as well as a registration one.
        Sync-ServiceBinaries
        # Re-registering (rather than "sc config binPath=") is how the command line,
        # the account, the start type and the recovery actions all end up matching this
        # script again, whatever an earlier version or a hand edit left behind.
        Remove-ServiceRegistration
        New-ServiceRegistration
        Set-FirewallRule $true
        Start-ServiceAndVerify
    }

    'remove' {
        Remove-ServiceRegistration
        Revoke-Roots
        Set-FirewallRule $false
        Remove-ServiceBinaries
        # The state directory is deliberately LEFT in place: it holds the identity the
        # phones pinned. Deleting it is a separate, explicit choice.
        Write-Log "server role removed. State kept in '$DataDir' - delete it by hand only if you want the phones to re-pair."
    }

    'start' {
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed" 9 }
        Start-ServiceAndVerify
    }

    'stop' {
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed" 9 }
        Stop-ServiceGracefully
    }

    'restart' {
        # One elevated step for the thing a user actually wants after changing
        # something the worker only reads at start-up - rather than two UAC prompts
        # with a window in between where nothing is serving.
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed" 9 }
        Stop-ServiceGracefully
        Start-ServiceAndVerify
    }

    'migrate-to-server' {
        Assert-Worker
        Assert-DataDir
        if (-not $UserDataDir) { Fail 'no -UserDataDir given' 2 }
        # Order matters: the User edition worker must be down before its files are read,
        # and the service must not be registered until the identity has been validated
        # in its new home (spec §2 - migration is a transaction).
        Get-Process -Name 'fms-share-worker' -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.Kill(); $_.WaitForExit(5000) } catch { }
        }
        $sourceDir = Resolve-UserDataDir $UserDataDir
        # Resolved from the state directory that actually holds the identity, not from
        # the elevating administrator's profile - see Resolve-ManagementSid.
        $ManageSid = Resolve-ManagementSid $ManageSid $sourceDir
        Write-Log "management SID(s): $ManageSid"
        Copy-State $sourceDir $DataDir
        Set-MachineDataDirAcl
        Sync-ServiceBinaries
        Remove-ServiceRegistration
        New-ServiceRegistration
        Set-FirewallRule $true
        Start-ServiceAndVerify
    }

    'migrate-to-user' {
        Assert-Worker
        if (-not $UserDataDir) { Fail 'no -UserDataDir given' 2 }
        Stop-ServiceGracefully
        Copy-State $DataDir $UserDataDir
        Remove-ServiceRegistration
        Revoke-Roots
        # The firewall opening follows whichever copy listens next. The service ran the
        # staged worker, which is about to be deleted; the User edition serves from the
        # installed one, a different program as far as the firewall is concerned. So the
        # rule is re-pointed rather than left naming a file that will not exist.
        $staged = Get-StagedExePath
        if ($staged) { & netsh.exe advfirewall firewall delete rule name=all program="$staged" | Out-Null }
        $script:ServiceExePath = $ExePath
        Set-FirewallRule $true
        Remove-ServiceBinaries
        Write-Log 'returned to User edition hosting. Start the Share Manager to resume sharing.'
    }

    'grant-roots' {
        Assert-DataDir
        if (-not $Roots) { Fail 'no -Roots given' 2 }
        # "Was -ReadOnlyRoots passed at all", not "is it empty": an empty value is a
        # legitimate "every root is writable", while an absent parameter is an older
        # caller that never had the notion, and must keep getting read-only.
        $rightsKnown = $PSBoundParameters.ContainsKey('ReadOnlyRoots')
        Grant-Roots `
            @($Roots -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) `
            @($ReadOnlyRoots -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) `
            $rightsKnown
    }
}

Write-Log "action=$Action completed"
exit 0
