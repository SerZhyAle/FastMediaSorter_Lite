<#
.SYNOPSIS
    The single elevated, auditable helper for the Fast Media Sorter & Sharing: Folder Share Server edition
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
    the log is appended to <DataDir>\install-share-service.log - or, when that
    directory is missing or cannot be trusted, to install-share-service.log in the
    elevated account's own %TEMP% - and that fallback copy is written on SUCCESS too,
    so a run whose log never opened is still on record. The codes: 1 unexpected
    error, 2 missing argument or worker, 3 service did not start, 4 an ACL could not
    be set, 5 not elevated, 6 state did not validate, 7 host-key mismatch, 8
    firewall, 9 service not installed, 10 several identities, 11 the state
    directory (or bin) cannot be trusted, 12 some requested folders were not
    granted, 13 the service account is not LOCAL SERVICE, 14 the old registration
    is still marked for deletion, 15 the grant ledger cannot be read, 16 another run of
    this helper holds the state lock (nothing was changed), 17 the grants this run made
    could not be recorded (they were taken back, or - if that failed too - named in a
    protected recovery file beside the ledger), 18 the intent record that precedes every
    grant could not be written (nothing was granted).

    A grant is written ahead: before the first ACE changes, the changes about to be made are
    recorded in a protected granted-roots.intent-<guid>.json beside the ledger, and dropped once
    the ledger is published. A run that is killed in between leaves that file; the next grant or
    remove settles it (takes back what the ledger does not record, or names it in a recovery
    file). An intent record that is not in the locked form is never acted on or deleted.

    "remove" (and "migrate-to-user") exit 0 once the service is gone, but a revoke that could
    not be complete - an unreadable ledger, a recovery file, an icacls failure - is never
    reported as complete: the ledger and recovery files are kept untouched, a WARNING line
    names each cause, and the closing line says "completed WITH WARNINGS".

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

    # The application installation folder (the one holding companion\ and filedo\). The service
    # is registered with it as --filedo-root. Omitted: derived from -ExePath, which is always
    # <app>\companion\fms-share-worker.exe.
    [string]$FileDoRoot,

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
    [string]$ReadOnlyRoots,

    # "remove" only: the installation stays an ordinary (User) edition, whose own opt-in
    # firewall rule names the installed worker. Removing the service role deletes every
    # rule aimed at that program, so without this the User edition was left listening
    # with no inbound allow at all (ticket 054 H-05). The Server edition's uninstaller
    # never passes it - nothing of that installation is left to listen.
    [switch]$RestoreUserRule
)

$ErrorActionPreference = 'Stop'

$ServiceName    = 'FastMediaSorterCompanionSFTP'
$ServiceDisplay = 'Fast Media Sorter & Sharing: Folder Share Server'
$ServiceDesc    = 'Serves the folders you selected in Fast Media Sorter & Sharing over SFTP, so an Android phone can reach them even when nobody is signed in.'
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

# The copy of the worker this script EXECUTES (--inspect-datadir). Once staged into the
# administrators-only bin directory it is that copy, never the one in the application
# folder: a per-user installation leaves that folder writable by the user, and a file
# that can change under us must not be what an elevated step runs (ticket 054 H-08).
$script:InspectExePath = $ExePath

# Well-known SIDs, used instead of names: icacls resolves "*S-1-5-19" identically on
# every Windows language, while "LOCAL SERVICE" fails outright on a localized system.
$SidSystem      = 'S-1-5-18'
$SidLocalService= 'S-1-5-19'
$SidAdmins      = 'S-1-5-32-544'
$SidTrustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'

# Windows PowerShell (.NET Framework) and PowerShell 7 (.NET) reach the ACL-at-creation
# APIs differently; the installers always run powershell.exe, 7 only has to work.
$script:IsDesktopEdition = ($PSVersionTable.PSEdition -ne 'Core')

# --- logging ------------------------------------------------------------------

$script:LogPath = $null

# Lines logged before the log file may be opened. Kept, and written out as soon as there
# is somewhere safe to put them (the state directory once it is verified, or the
# fallback when the action stops before that).
$script:PendingLog = New-Object System.Collections.Generic.List[string]

# Where the log goes when the state directory cannot be used: this elevated account's
# own temporary folder, which no other standard user can write to.
function Get-FallbackLogPath {
    try { return (Join-Path ([System.IO.Path]::GetTempPath()) 'install-share-service.log') } catch { return $null }
}

function Write-PendingLog([string]$path) {
    if (-not $path -or $script:PendingLog.Count -eq 0) { return }
    # Cleared ONLY after the append took. A failed append keeps the lines (the next place that
    # can take them gets them) and says so on the console - a visible secondary diagnostic that
    # does not go through the log it is about, so it cannot recurse (ticket 102 I02.1).
    try {
        Add-Content -LiteralPath $path -Value $script:PendingLog.ToArray() -Encoding UTF8
        $script:PendingLog.Clear()
    } catch {
        Write-Warning ("the helper's log could not be written to '{0}' ({1}); {2} line(s) kept in memory" -f $path, $_.Exception.Message, $script:PendingLog.Count)
    }
}
# Opens the log inside the state directory - ONLY after that directory was verified
# (Assert-TrustedDirectory / Initialize-MachineDataDir), and never by creating it. It used
# to create %ProgramData%\FastMediaSorterCompanion right here, before the elevation check
# and before any ownership check, which is one way a directory owned by someone else ended
# up adopted by the next elevated run (ticket 046 H-02).
function Initialize-Log {
    if (-not $DataDir) { return }
    $attr = Get-RawAttributes $DataDir
    if ($null -eq $attr -or -not ($attr -band [System.IO.FileAttributes]::Directory)) { return }
    $candidate = Join-Path $DataDir 'install-share-service.log'
    # A log file that is a link would turn every elevated Add-Content into a write wherever
    # the link points. It is only our own text, so the link goes, never its target.
    try { if (Test-ReparsePoint $candidate) { Remove-LinkOnly $candidate } } catch { return }
    $script:LogPath = $candidate
    Write-PendingLog $candidate
}

# Write-Host, not Write-Output: a log line on the success stream becomes part of the
# RETURN VALUE of whichever function logged it. Resolve-UserDataDir logged the profile it
# found and then returned it, so the caller got "<log line> <path>" as one string - a
# directory that does not exist - and the migration "started fresh", generating a NEW
# host key that unpaired every phone.
function Write-Log([string]$message) {
    $line = "{0} {1}" -f (Get-Date).ToUniversalTime().ToString('s'), $message
    Write-Host $line
    if ($script:LogPath) {
        try {
            Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
        } catch {
            # Never a silent loss (ticket 102 I02.1): this line and every later one are kept for the
            # fallback copy the exit path writes, and the console says where the log stopped. The
            # warning does not go through Write-Log, so it cannot recurse.
            Write-Warning ("the helper's log '{0}' stopped taking lines ({1}); the rest goes to the fallback log" -f $script:LogPath, $_.Exception.Message)
            $script:LogPath = $null
            $script:PendingLog.Add($line)
        }
    } else {
        $script:PendingLog.Add($line)
    }
}

function Fail([string]$message, [int]$code = 1) {
    Write-Log "FAILED: $message"
    if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }
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

# --- filesystem safety (ticket 046 H-02) ---------------------------------------
#
# Everything this script writes lands in a directory an elevated process has to be able
# to trust, and %ProgramData% is not such a place by default: any standard user may create
# a subfolder there, becomes its owner, and keeps an ACE of their own that "/inheritance:r
# /grant:r" never removes. A junction or a symbolic link, in turn, makes an elevated write
# or ACL change land wherever it points. So: no link is ever followed, an existing
# directory is adopted only when an administrator or the system owns it, and a directory
# this script creates carries its final ACL from the first instant.

# Owners an existing state or bin directory may have. LOCAL SERVICE is the service's own
# account, which recreates the directory if it vanished while the service was registered.
$TrustedOwnerSids = @($SidSystem, $SidAdmins, $SidTrustedInstaller, $SidLocalService)

# Attributes WITHOUT following a link (GetFileAttributes reads the link itself, even a
# dangling one). $null when nothing exists at the path.
function Get-RawAttributes([string]$path) {
    if (-not $path) { return $null }
    try { return [System.IO.File]::GetAttributes($path) } catch { return $null }
}

function Test-ReparsePoint([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return $false }
    return [bool]($attr -band [System.IO.FileAttributes]::ReparsePoint)
}

# A junction or a symbolic link: a path that stands for another one. Test-ReparsePoint is
# the strict test for our own directories and files, where no reparse point belongs at
# all; a folder someone SHARES may well be one of another kind - every OneDrive folder is
# a cloud-files reparse point - and is an ordinary folder for this purpose.
function Test-NameLink([string]$path) {
    try { $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop } catch { return $false }
    return (@('Junction', 'SymbolicLink') -contains [string]$item.LinkType)
}

# Removes a link itself - or a plain file - never what a link points at.
function Remove-LinkOnly([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return }
    if ($attr -band [System.IO.FileAttributes]::Directory) {
        [System.IO.Directory]::Delete($path, $false)
        return
    }
    if (-not ($attr -band [System.IO.FileAttributes]::ReparsePoint)) {
        [System.IO.File]::SetAttributes($path, [System.IO.FileAttributes]::Normal)
    }
    [System.IO.File]::Delete($path)
}

# Deletes a directory tree without ever following a link inside it: a link is removed as
# a link, so a junction planted in the tree cannot turn this into a delete somewhere else.
function Remove-TreeNoFollow([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return }
    if (($attr -band [System.IO.FileAttributes]::ReparsePoint) -or -not ($attr -band [System.IO.FileAttributes]::Directory)) {
        Remove-LinkOnly $path
        return
    }
    foreach ($entry in [System.IO.Directory]::GetFileSystemEntries($path)) { Remove-TreeNoFollow $entry }
    [System.IO.Directory]::Delete($path, $false)
}

function Get-OwnerSid([string]$path) {
    try { return (Get-Acl -LiteralPath $path).GetOwner([System.Security.Principal.SecurityIdentifier]).Value } catch { return $null }
}

function Get-SidDisplay([string]$sid) {
    if (-not $sid) { return '(unknown)' }
    try { return (New-Object System.Security.Principal.SecurityIdentifier($sid)).Translate([System.Security.Principal.NTAccount]).Value } catch { return $sid }
}

function Test-TrustedOwner([string]$sid) {
    if (-not $sid) { return $false }
    if ($TrustedOwnerSids -contains $sid) { return $true }
    # Under the "objects created by administrators are owned by the creator" policy, a
    # directory this same elevated account made earlier is owned by the account itself.
    return ($sid -eq ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value)
}

# '' when $path is absent or may be adopted, otherwise the reason it may not.
function Get-DirectoryDistrust([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return '' }
    if ($attr -band [System.IO.FileAttributes]::ReparsePoint) {
        return "'$path' is a junction or a symbolic link, and an elevated step never follows one - it could point anywhere. Remove the link by hand, then run this again."
    }
    if (-not ($attr -band [System.IO.FileAttributes]::Directory)) {
        return "'$path' exists but is a file, not a directory. Remove it by hand, then run this again."
    }
    $owner = Get-OwnerSid $path
    if (-not (Test-TrustedOwner $owner)) {
        return ("'$path' belongs to '{0}', not to an administrator or the system. Whoever owns it could read the host key and the SFTP password kept there and change what the service runs, so it is not used. Nothing was changed. Check its contents, then delete it or make Administrators its owner (icacls ""$path"" /setowner *S-1-5-32-544), and run this again." -f (Get-SidDisplay $owner))
    }
    return ''
}

# $true when $path is an existing directory this script may adopt, $false when nothing is
# there yet (the caller creates it). Anything else stops the whole action BEFORE a byte is
# written into it - no log line, no copied secret, no ACL change.
function Assert-TrustedDirectory([string]$path, [string]$what) {
    $why = Get-DirectoryDistrust $path
    if ($why) { Fail "$what refused: $why" 11 }
    return ($null -ne (Get-RawAttributes $path))
}

function New-AllowRule([string]$sid, [string]$rights, [string]$scope) {
    $id = New-Object System.Security.Principal.SecurityIdentifier($sid)
    $inherit = [System.Security.AccessControl.InheritanceFlags]::None
    $propagate = [System.Security.AccessControl.PropagationFlags]::None
    if ($scope -ne 'this') {
        $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        # 'children': the rule reaches everything inside, but not the directory itself.
        if ($scope -eq 'children') { $propagate = [System.Security.AccessControl.PropagationFlags]::InheritOnly }
    }
    return New-Object System.Security.AccessControl.FileSystemAccessRule($id,
        [System.Security.AccessControl.FileSystemRights]$rights, $inherit, $propagate,
        [System.Security.AccessControl.AccessControlType]::Allow)
}

# A directory descriptor built from nothing: owned by Administrators, no inheritance from
# the parent, SYSTEM and Administrators in full control, and ONLY what the caller adds.
function New-LockedDirectorySecurity {
    $sec = New-Object System.Security.AccessControl.DirectorySecurity
    $sec.SetOwner((New-Object System.Security.Principal.SecurityIdentifier($SidAdmins)))
    $sec.SetAccessRuleProtection($true, $false)
    $sec.AddAccessRule((New-AllowRule $SidSystem 'FullControl' 'tree'))
    $sec.AddAccessRule((New-AllowRule $SidAdmins 'FullControl' 'tree'))
    return $sec
}

# The state directory. LOCAL SERVICE and the enrolled management user(s) may create,
# change and delete the FILES of the state (stats, shares, settings) - but hold no DELETE
# on the directory itself. With it, a standard management user could rename the whole
# store away while the service is stopped and put a directory of their own, with a bin of
# their own, where the service looks for its binary.
function New-DataDirSecurity {
    $sec = New-LockedDirectorySecurity
    foreach ($sid in (@($SidLocalService) + @(Split-Sids $ManageSid))) {
        $sec.AddAccessRule((New-AllowRule $sid 'ReadAndExecute, Write' 'this'))
        $sec.AddAccessRule((New-AllowRule $sid 'Modify' 'children'))
    }
    return $sec
}

# The service's binary directory: code, so read+execute for the account that runs it and
# read for the management user (to compare it with the installed copy) - never write.
function New-BinDirSecurity {
    $sec = New-LockedDirectorySecurity
    foreach ($sid in (@($SidLocalService) + @(Split-Sids $ManageSid))) {
        $sec.AddAccessRule((New-AllowRule $sid 'ReadAndExecute' 'tree'))
    }
    return $sec
}

function Set-DirectorySecurity([string]$path, $security) {
    $info = New-Object System.IO.DirectoryInfo($path)
    if ($script:IsDesktopEdition) { $info.SetAccessControl($security) }
    else { [System.IO.FileSystemAclExtensions]::SetAccessControl($info, $security) }
}

# Creates a directory WITH its descriptor, so there is no moment in which it carries the
# parent's "Users may create files here". The caller has checked that nothing is there;
# if something appeared in between, the create does not apply the descriptor - which is
# why every caller verifies the result instead of assuming it.
function New-SecureDirectory([string]$path, $security) {
    $info = New-Object System.IO.DirectoryInfo($path)
    if ($script:IsDesktopEdition) { $info.Create($security) }
    else { [System.IO.FileSystemAclExtensions]::Create($info, $security) }
}

# '' when $path is owned by Administrators, inherits nothing and carries entries for
# $allowedSids only; otherwise what is wrong with it.
function Get-LockdownProblem([string]$path, [string[]]$allowedSids) {
    try { $acl = Get-Acl -LiteralPath $path } catch { return "its ACL cannot be read: $($_.Exception.Message)" }
    $owner = $acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value
    if ($owner -ne $SidAdmins) { return "its owner is '$(Get-SidDisplay $owner)', not Administrators" }
    if (-not $acl.AreAccessRulesProtected) { return 'it still inherits entries from its parent' }
    foreach ($rule in $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])) {
        if ($rule.IsInherited) { return 'it still carries inherited entries' }
        if ($allowedSids -notcontains $rule.IdentityReference.Value) {
            return "it carries an unexpected entry for '$(Get-SidDisplay $rule.IdentityReference.Value)'"
        }
    }
    return ''
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
    if (-not (Test-Path -LiteralPath $script:InspectExePath)) { Fail "worker executable not found: '$script:InspectExePath'" 2 }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $script:InspectExePath
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
    # The SCM keeps a deleted service until the last handle closes (an open Services
    # console is enough); give it time so an immediately following create does not hit
    # ERROR_SERVICE_MARKED_FOR_DELETE. Register-Service avoids the delete altogether
    # whenever it can, because this is the step that can leave a machine with no service.
    for ($i = 0; $i -lt 120 -and (Get-ServiceOrNull); $i++) { Start-Sleep -Milliseconds 250 }
    if (Get-ServiceOrNull) { Write-Log "WARNING: $ServiceName is still marked for deletion - a Services console or another tool keeps a handle to it open" }
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
    $binDir = Split-Path -Parent $staged
    if ([IO.Path]::GetFullPath($ExePath) -ieq [IO.Path]::GetFullPath($staged)) {
        [void](Assert-TrustedDirectory $binDir 'the service binary directory')
        $script:ServiceExePath = $staged
        $script:InspectExePath = $staged
        Write-Log "worker already staged at '$staged'"
        return
    }

    # Nothing may hold the file while it is replaced; on install/repair the service is
    # re-registered right after this anyway.
    Stop-ServiceGracefully

    # Never reused (ticket 046 H-02): whatever is there was put there by someone at some
    # point, and it is about to become the image a service runs. Deleting it - links
    # removed as links, never followed - and recreating it with its final ACL is the only
    # state that proves itself. The ACL is right for code: the account that RUNS the
    # binary must not be able to rewrite it (read+execute for LOCAL SERVICE, read for the
    # management user, full for SYSTEM/Administrators).
    if ($null -ne (Get-RawAttributes $binDir)) {
        Write-Log "replacing '$binDir'"
        Remove-TreeNoFollow $binDir
    }
    New-SecureDirectory $binDir (New-BinDirSecurity)
    [void](Assert-TrustedDirectory $binDir 'the service binary directory')
    $problem = Get-LockdownProblem $binDir (@($SidSystem, $SidAdmins, $SidLocalService) + @(Split-Sids $ManageSid))
    if ($problem) { Fail "could not secure '$binDir': $problem" 4 }

    # Staged FIRST, executed after: from here on the only copy this script runs is the
    # one in an administrators-only directory. What it was copied FROM is still the
    # application folder - a per-user installation's, writable by that user - so the copy
    # is only as trustworthy as that folder; see ticket 054 H-08 for what closing that
    # needs (a signed worker, verified here).
    Write-Log "staging worker: '$ExePath' -> '$staged'"
    Copy-Item -LiteralPath $ExePath -Destination $staged -Force
    $sidecar = "$ExePath.sha256"
    if (Test-Path -LiteralPath $sidecar) {
        Copy-Item -LiteralPath $sidecar -Destination "$staged.sha256" -Force
    }

    $script:ServiceExePath = $staged
    $script:InspectExePath = $staged
}

# Drops the staged copy when the server role goes away. The state directory beside it
# is deliberately kept (it holds the pinned identity) - this removes only code.
function Remove-ServiceBinaries {
    $staged = Get-StagedExePath
    if (-not $staged) { return }
    $binDir = Split-Path -Parent $staged
    if ($null -eq (Get-RawAttributes $binDir)) { return }
    try {
        # Never Remove-Item -Recurse here: on Windows PowerShell it can walk INTO a
        # junction and delete what it points at.
        Remove-TreeNoFollow $binDir
        Write-Log "removed the staged worker in '$binDir'"
    } catch {
        # A file still held by a stopping service must not fail the whole action - the
        # registration is already gone, which is what "remove the role" means.
        Write-Log "could not remove '$binDir': $_"
    }
}

# The root the service's worker looks for the bundled FileDO (<root>\filedo\) in. Without
# --filedo-root the worker uses the grandparent of its own executable, and the service runs
# the copy staged under %ProgramData%, so that default is the machine state directory's
# parent - not under Program Files, which the bundle check refuses for a service (disk sharing
# then reports "service FileDO must be inside its Program Files installation").
# Order: an explicit -FileDoRoot; the installation derived from -ExePath; when this run was
# started FROM the staged copy (no installation to derive from) the root the existing
# registration already names; otherwise '' - never a guess.
function Get-FileDoRoot {
    if ($FileDoRoot) { return $FileDoRoot.TrimEnd('\') }
    if ($ExePath) {
        $staged = Get-StagedExePath
        if (-not $staged -or ([IO.Path]::GetFullPath($ExePath) -ine [IO.Path]::GetFullPath($staged))) {
            return (Split-Path -Parent (Split-Path -Parent $ExePath)).TrimEnd('\')
        }
    }
    if ((Get-ServiceRegistryValue 'ImagePath') -match '--filedo-root\s+"([^"]+)"') { return $Matches[1] }
    return ''
}

function Get-ServiceBinPath {
    $binPath = '"{0}" --service --datadir "{1}"' -f $script:ServiceExePath, $DataDir
    if ($ManageSid) { $binPath += ' --manage-sid "{0}"' -f $ManageSid }
    $root = Get-FileDoRoot
    if ($root) { $binPath += ' --filedo-root "{0}"' -f $root }
    return $binPath
}

# The account the service is registered as. LocalService is the rule (ticket 054 H-14); the one
# exception is a machine that carries the optional FileDO component, because mounting a virtual
# disk needs a LocalSystem holder (the worker refuses disk sharing under any other account,
# internal/service/disksharing.go). The component is unticked by default, so an installation
# without it never runs as SYSTEM. Derived from the bundle on disk rather than passed as a switch,
# so repair, migrate-to-server and the Hosting console's UAC run all reach the same answer.
function Get-WantedServiceAccount {
    $root = Get-FileDoRoot
    if ($root -and (Test-Path -LiteralPath (Join-Path $root 'filedo\filedo.exe') -PathType Leaf)) { return 'LocalSystem' }
    return 'NT AUTHORITY\LocalService'
}

function Get-ServiceRegistryValue([string]$name) {
    try {
        return [string](Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name $name -ErrorAction Stop).$name
    } catch { return '' }
}

function New-ServiceRegistration {
    Assert-Worker
    Assert-DataDir

    # New-Service (not "sc.exe create") on purpose: the binary path carries quoted
    # paths, and passing that through a native command's command line is exactly the
    # kind of quoting that breaks on one Windows PowerShell version and not another.
    # New-Service hands the string to CreateService as-is.
    $binPath = Get-ServiceBinPath
    Write-Log "registering $ServiceName -> $binPath"
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $ServiceDisplay `
                -Description $ServiceDesc -StartupType Automatic | Out-Null

    # LocalService: low local privilege, and it presents anonymous credentials on the
    # network. That last part is why a UNC root does not work under it - documented,
    # not worked around. New-Service registers LocalSystem, so this call is what keeps
    # the SFTP server from running as SYSTEM unless the FileDO component asks for it
    # (Get-WantedServiceAccount) - checked, and verified below.
    & sc.exe config $ServiceName obj= (Get-WantedServiceAccount) | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the service account returned $LASTEXITCODE" }
    Set-ServiceOptions
}

# Re-points an EXISTING registration at this script's command line and account without
# deleting it. Delete + create was how repair used to work, and the delete is the step
# that can strand a machine with no service at all: while any handle to the old one is
# open, the SCM keeps it "marked for deletion" and the create fails (ticket 054 H-13).
# Win32_Service.Change takes the path as a plain string, so none of the native-command
# quoting New-Service exists to avoid comes back. $false = could not, fall back.
function Update-ServiceRegistration {
    $binPath = Get-ServiceBinPath
    Write-Log "updating the registration of $ServiceName in place -> $binPath"
    try {
        $svc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
        if (-not $svc) { return $false }
        $result = Invoke-CimMethod -InputObject $svc -MethodName Change -ErrorAction Stop -Arguments @{
            DisplayName   = $ServiceDisplay
            PathName      = $binPath
            StartMode     = 'Automatic'
            StartName     = (Get-WantedServiceAccount)
            StartPassword = ''
        }
        if ($result.ReturnValue -ne 0) {
            Write-Log "Win32_Service.Change returned $($result.ReturnValue)"
            return $false
        }
    } catch {
        Write-Log "the in-place update failed: $($_.Exception.Message)"
        return $false
    }
    if ((Get-ServiceRegistryValue 'ImagePath') -ne $binPath) {
        Write-Log "the in-place update did not take (the image path reads '$(Get-ServiceRegistryValue 'ImagePath')')"
        return $false
    }
    & sc.exe description $ServiceName $ServiceDesc | Out-Null
    Set-ServiceOptions
    return $true
}

# The options New-Service cannot express. Each exit code is read: a failure here used to
# vanish into Out-Null.
function Set-ServiceOptions {
    # DELAYED automatic start: the worker needs a network stack for mDNS, UPnP and the
    # reachability probe. (The worker also retries on its own - SCM recovery cannot
    # help a service that started healthy but too early - but starting late is cheaper
    # than retrying.)
    & sc.exe config $ServiceName start= delayed-auto | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the delayed start returned $LASTEXITCODE" }

    # Restart on process failure with a bounded, widening cadence. failureflag=1 is the
    # half that is easy to miss: without it Windows only reacts to a service that died
    # WITHOUT reporting a stop, and the worker host deliberately reports a stop with a
    # service-specific non-zero exit code when its main loop ends unexpectedly.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/120000 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the recovery actions returned $LASTEXITCODE" }
    & sc.exe failureflag $ServiceName 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the failure flag returned $LASTEXITCODE" }
}

# The account is read back, never assumed (ticket 054 H-14). A registration that would
# run the SFTP server as anything but the wanted account - LocalSystem above all when the
# FileDO component is absent, New-Service's default - is removed rather than started: every
# root-confinement bug would otherwise be a full-disk exposure.
function Assert-ServiceAccount {
    $account = Get-ServiceRegistryValue 'ObjectName'
    $wanted = Get-WantedServiceAccount
    # The SCM stores LocalSystem as "LocalSystem"; accept its long spelling too.
    $accepted = if ($wanted -eq 'LocalSystem') { @('LocalSystem', 'NT AUTHORITY\SYSTEM') } else { @($wanted) }
    if ($accepted -contains $account) {
        Write-Log "service account: $account"
        return
    }
    Write-Log "the service account is '$account', not '$wanted' - removing the registration so nothing runs with more rights than intended"
    & sc.exe delete $ServiceName | Out-Null
    Fail "could not set the service account to '$wanted' (it read '$account')" 13
}

# Brings the registration to this script's command line, account, start type and
# recovery actions - updating an existing one in place, creating one otherwise.
function Register-Service {
    Assert-Worker
    Assert-DataDir
    if (Get-ServiceOrNull) {
        Stop-ServiceGracefully
        if (-not (Update-ServiceRegistration)) {
            Write-Log 're-creating the registration instead'
            Remove-ServiceRegistration
            if (Get-ServiceOrNull) {
                Fail "$ServiceName is still marked for deletion - a Services console or another tool keeps a handle to it open. Close it (or restart Windows), then run this again." 14
            }
            New-ServiceRegistration
        }
    } else {
        New-ServiceRegistration
    }
    Assert-ServiceAccount
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
    [void](Assert-TrustedDirectory $DataDir 'the machine state directory')
    # A descriptor built from scratch, not "/inheritance:r /grant:r": the directory holds
    # the private host key and the SFTP password, so the inherited "Users: read" that
    # %ProgramData% hands down must go - and so must ANY other entry, which the icacls
    # form left in place (ticket 046 H-02). The owner becomes Administrators, which also
    # takes the implicit right to rewrite the DACL away from whoever created it.
    #
    # LocalService gets Modify on the files (it writes stats, shares, settings). The
    # enrolled management user gets it TOO, and that is deliberate rather than lax: from
    # the moment this directory exists it is the ONE state store both hosts use, so the
    # foreground worker - which runs as that user whenever the service is stopped - has
    # to be able to write the same shares.json, settings.json and stats.json. Read-only
    # here would silently split the two editions into separate folder lists. It is not a
    # privilege escalation either: everything in the files (password included) is already
    # handed to that same user over the control pipe. What neither gets is DELETE on the
    # directory itself - see New-DataDirSecurity.
    Write-Log "locking down $DataDir"
    Set-DirectorySecurity $DataDir (New-DataDirSecurity)
    $problem = Get-LockdownProblem $DataDir (@($SidSystem, $SidAdmins, $SidLocalService) + @(Split-Sids $ManageSid))
    if ($problem) { Fail "could not secure '$DataDir': $problem" 4 }
    Move-UntrustedWorkerStateAside
}

# The worker keeps its FileDO event files in <DataDir>\filedo-state and refuses to reuse
# that directory when anyone but the system or an administrator owns it. A foreground
# worker that ran as the signed-in user before the service existed leaves one owned by
# that user, and then every FileDO call of the service fails before FileDO starts - the
# Server edition reports disk sharing as unavailable while everything else looks fine.
# The directory is transient (per-run event files), so it is renamed aside - never
# followed or deleted, since its owner may have put a link inside - and the worker
# creates a fresh one with its own ACL.
function Move-UntrustedWorkerStateAside {
    $dir = Join-Path $DataDir 'filedo-state'
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return }
    $owner = $null
    try { $owner = (Get-Acl -LiteralPath $dir).GetOwner([Security.Principal.SecurityIdentifier]).Value } catch { return }
    if ($owner -eq $SidSystem -or $owner -eq $SidAdmins) { return }
    $aside = '{0}.untrusted-{1:yyyyMMddHHmmss}' -f $dir, (Get-Date)
    Write-Log "moving '$dir' (owned by $owner) aside to '$aside'"
    Move-Item -LiteralPath $dir -Destination $aside -Force
}

# The one way the machine state directory comes into use by an action that writes to it
# (install, repair, migrate-to-server): an existing directory is adopted only when an
# administrator or the system owns it and it is no link; a missing one is CREATED with its
# final ACL, so no other account ever had a moment in which to put a file of its own in
# it. Then the ACL is rebuilt and verified, and only then is the log opened there.
function Initialize-MachineDataDir {
    Assert-DataDir
    if (Assert-TrustedDirectory $DataDir 'the machine state directory') {
        Write-Log "state directory '$DataDir' exists and is owned by an administrator or the system"
    } else {
        Write-Log "creating '$DataDir' with its final ACL"
        New-SecureDirectory $DataDir (New-DataDirSecurity)
        # Something may have appeared between the check and the create, and then the
        # create applied nothing - so the owner is checked again rather than assumed.
        if (-not (Assert-TrustedDirectory $DataDir 'the machine state directory')) { Fail "could not create '$DataDir'" 4 }
    }
    Set-MachineDataDirAcl
    Initialize-Log
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

# The folders of -Roots / -ReadOnlyRoots. The app sends each without its trailing
# backslash - "D:\" right before a closing quote is an escaped quote to the Windows argument
# parser, which swallowed the rest of the command line (ticket 054 H-03) - so a bare drive
# letter gets its backslash back here: "D:" alone means "the current folder on D:".
function Split-RootList([string]$value) {
    if (-not $value) { return @() }
    return @($value -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object {
        if ($_ -match '^[A-Za-z]:$') { $_ + '\' } else { $_ }
    })
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
function Resolve-UserDataDir([string]$hint, [string]$UsersRoot = '') {
    if ($hint -and (Test-Path -LiteralPath (Join-Path $hint 'hostkey'))) { return $hint }

    $users = if ($UsersRoot) { $UsersRoot } else { Join-Path $env:SystemDrive 'Users' }
    if (-not (Test-Path -LiteralPath $users)) { return $hint }
    $found = @(Get-ChildItem -LiteralPath $users -Directory -ErrorAction SilentlyContinue | ForEach-Object {
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

function Copy-State([string]$from, [string]$to, [switch]$OnlyWhenTargetIsFresh) {
    if (-not (Test-Path -LiteralPath $from)) {
        Write-Log "no state to migrate from '$from' - starting fresh"
        return
    }
    # This runs elevated between two directories other accounts can write to, so neither
    # end may be a link: reading through one could copy another account's files where the
    # management user can read them, writing through one could land anywhere.
    if (Test-ReparsePoint $from) { Fail "'$from' is a junction or a symbolic link - migration refused. Nothing was copied." 11 }
    if (Test-ReparsePoint $to) { Fail "'$to' is a junction or a symbolic link - migration refused. Nothing was copied." 11 }
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
        if ($OnlyWhenTargetIsFresh -and $target.hostKeyPresent) {
            # Migration is a ONE-TIME move, and it has already happened: this directory
            # holds the identity and is what the service has been serving from. Every
            # later install would otherwise copy the per-user directory - a snapshot of
            # whenever the User edition last ran - back over it, rolling the LIVE state
            # backwards: the listen port (silently invalidating the router forward and
            # every QR already handed out), the share list, the credential, the counters.
            # Same identity, nothing to preserve, so nothing to copy.
            Write-Log "'$to' already holds this identity - keeping the machine state as it is (nothing migrated)"
            return
        }
    } else {
        New-Item -ItemType Directory -Path $to -Force | Out-Null
    }

    Write-Log "migrating state '$from' -> '$to'"
    Get-ChildItem -LiteralPath $from -File | Where-Object { $_.Name -ne 'service.log' -and $_.Name -ne 'install-share-service.log' -and $_.Name -ne $LedgerName } | ForEach-Object {
        # A link among the SOURCE files is skipped, one at the destination is removed as a
        # link first - so the copy neither reads nor writes anything outside the two stores.
        if ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            Write-Log "WARNING: not migrating '$($_.FullName)': it is a link"
            return
        }
        $dest = Join-Path $to $_.Name
        if (Test-ReparsePoint $dest) { Remove-LinkOnly $dest }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
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

# The elements of a JSON array, one by one. Windows PowerShell 5.1 - which is what the
# installers and the Share Manager run - emits a parsed JSON array as ONE object, so
# @(... | ConvertFrom-Json) is a one-element array holding the real one. Every reader
# below then saw a single "entry" whose properties were arrays: the share list read as
# empty (no folder was ever granted from it), and with two or more ledger entries every
# one looked "pre-existing", so revoke removed nothing.
function ConvertFrom-JsonList([string]$text) {
    if (-not $text -or -not $text.Trim()) { return }
    foreach ($item in ($text | ConvertFrom-Json)) { $item }
}

# @() only when there is no ledger. A ledger that EXISTS but cannot be read or parsed
# THROWS: an unreadable ledger is the one record of what an administrator approved, and
# a caller that took it for empty would then write over it (ticket 057.S24-2). Every
# caller decides what an unreadable ledger means for it - abort, warn, keep the file.
# A file holding no JSON at all counts as unreadable too: only Save-Ledger writes here,
# and it never writes less than one JSON array.
function Get-Ledger {
    $path = Join-Path $DataDir $LedgerName
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $raw = Get-Content -LiteralPath $path -Raw
    if (-not $raw -or -not $raw.Trim()) { throw "the file holds no JSON at all" }
    # Save-Ledger only ever writes an array; a lone object is somebody else's file, not history.
    if (-not $raw.TrimStart([char]0xFEFF, ' ', "`t", "`r", "`n").StartsWith('[')) { throw "the file is not a JSON array" }
    $entries = @(ConvertFrom-JsonList $raw)
    # Shape, not just syntax: a partly malformed array is never taken for valid history. A valid
    # empty array "[]" is a ledger with nothing in it; an empty FILE (above) is corrupt.
    $index = 0
    foreach ($entry in $entries) {
        $index++
        if ($null -eq $entry -or $entry -isnot [System.Management.Automation.PSCustomObject]) { throw "entry $index is not an object" }
        $pathProp = $entry.PSObject.Properties['path']
        if (-not $pathProp -or $pathProp.Value -isnot [string] -or -not $pathProp.Value.Trim()) { throw "entry $index has no 'path'" }
        $rightsProp = $entry.PSObject.Properties['rights']
        if ($rightsProp -and @('RX', 'M') -notcontains [string]$rightsProp.Value) { throw "entry $index has an unknown 'rights' value '$($rightsProp.Value)'" }
        $preProp = $entry.PSObject.Properties['preExisting']
        if ($preProp -and $preProp.Value -isnot [bool]) { throw "entry $index has a non-boolean 'preExisting'" }
    }
    return $entries
}

# The ledger is the one record of what an ADMINISTRATOR approved, and the state
# directory around it is writable by the management user by design - so the ledger is
# written with an ACL of its own that only SYSTEM and Administrators can change, created
# with it (no moment in which it inherits the management user's Modify), and read back
# for a re-grant only while it still has that ACL (ticket 054 H-09).
# Creates $path exclusively with the locked ACL (only SYSTEM and Administrators), already in place
# at creation - no moment in which it inherits the management user's Modify - and writes $bytes.
function Write-LockedFile([string]$path, [byte[]]$bytes) {
    $sec = New-Object System.Security.AccessControl.FileSecurity
    $sec.SetOwner((New-Object System.Security.Principal.SecurityIdentifier($SidAdmins)))
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($SidSystem, $SidAdmins)) {
        $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            (New-Object System.Security.Principal.SecurityIdentifier($sid)),
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow)))
    }
    if ($null -ne (Get-RawAttributes $path)) { Remove-LinkOnly $path }
    if ($script:IsDesktopEdition) {
        $stream = New-Object System.IO.FileStream($path, [System.IO.FileMode]::CreateNew,
            [System.Security.AccessControl.FileSystemRights]::FullControl, [System.IO.FileShare]::None,
            4096, [System.IO.FileOptions]::None, $sec)
    } else {
        $stream = [System.IO.FileSystemAclExtensions]::Create((New-Object System.IO.FileInfo($path)),
            [System.IO.FileMode]::CreateNew, [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::None, $sec)
    }
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    return $sec
}

# The ledger is the one record of what an ADMINISTRATOR approved, and the state
# directory around it is writable by the management user by design - so the ledger is
# written with an ACL of its own that only SYSTEM and Administrators can change, created
# with it, and read back for a re-grant only while it still has that ACL (ticket 054 H-09).
#
# Publication is a transaction on names THIS call owns (ticket 102 I02): a unique temporary
# and a unique backup, so two runs can never share one, and nothing here deletes a file it
# did not create. The previous ledger survives every failure; the backup is dropped only
# after the published file has been read back and its ACL verified.
function Save-Ledger($entries) {
    $path = Join-Path $DataDir $LedgerName
    $token = [guid]::NewGuid().ToString('N')
    $temp = "$path.tmp-$token"
    $backup = "$path.bak-$token"
    $text = ConvertTo-Json -InputObject @($entries) -Depth 4
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes([string]$text)

    $sec = Write-LockedFile $temp $bytes
    $published = $false
    try {
        # A link at the destination goes, never its target - and the move below then cannot
        # follow it either.
        if (Test-ReparsePoint $path) { Remove-LinkOnly $path }
        if ($null -eq (Get-RawAttributes $path)) {
            [System.IO.File]::Move($temp, $path)
        } else {
            # .NET Framework cannot pass a null backup name to File.Replace, so the replaced
            # content goes to OUR backup name, kept until the result is verified.
            [System.IO.File]::Replace($temp, $path, $backup)
            # The swap keeps the REPLACED file's descriptor and drops the temporary's own,
            # so the locked one is re-applied: whatever stood at the path, the ledger must
            # carry its final ACL from this moment on.
            if ($script:IsDesktopEdition) {
                (New-Object System.IO.FileInfo($path)).SetAccessControl($sec)
            } else {
                [System.IO.FileSystemAclExtensions]::SetAccessControl((New-Object System.IO.FileInfo($path)), $sec)
            }
        }
        $published = $true

        $problem = Get-LedgerLockProblem $path
        if ($problem) { throw "the published ledger failed verification: $problem" }
        $readBack = @(Get-Ledger)
        if ($readBack.Count -ne @($entries).Count) { throw "the published ledger does not read back with its $(@($entries).Count) entries" }
    } catch {
        # Nothing is deleted on a failed verification. A temporary that was never published is
        # ours to remove; a published file that failed its check is swapped back for the previous
        # ledger (kept in the backup), and the rejected one stays beside it as evidence.
        if (-not $published -and $null -ne (Get-RawAttributes $temp)) { Remove-LinkOnly $temp }
        if ($published -and $null -ne (Get-RawAttributes $backup)) {
            try {
                [System.IO.File]::Replace($backup, $path, "$path.rejected-$token")
                Write-Log "the previous ledger was put back; the rejected one is kept as '$path.rejected-$token'"
            } catch {
                Write-Log "the previous ledger is kept as '$backup'"
            }
        } elseif ($null -ne (Get-RawAttributes $backup)) {
            Write-Log "the previous ledger is kept as '$backup'"
        }
        throw
    }
    # Verified: the backup (ours) goes.
    if ($null -ne (Get-RawAttributes $backup)) { Remove-LinkOnly $backup }
}

# '' when the ledger at $path is in the locked form Save-Ledger writes, otherwise why not.
function Get-LedgerLockProblem([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return 'it does not exist' }
    if ($attr -band [System.IO.FileAttributes]::ReparsePoint) { return 'it is a link' }
    try {
        $acl = Get-Acl -LiteralPath $path
        if ($acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value -ne $SidAdmins) { return 'it is not owned by Administrators' }
        if (-not $acl.AreAccessRulesProtected) { return 'it inherits the state directory''s write access' }
        foreach ($rule in $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])) {
            if (@($SidSystem, $SidAdmins) -notcontains $rule.IdentityReference.Value) { return "it carries an entry for '$(Get-SidDisplay $rule.IdentityReference.Value)'" }
        }
    } catch { return "its ACL cannot be read: $($_.Exception.Message)" }
    return ''
}
# The ledger entries an administrator approved - or nothing, when the ledger is not in the
# locked form Save-Ledger writes (written by an older version, or changed by someone
# else). Used to decide what may be RE-granted without showing anyone a list; revoking
# still reads the plain ledger, since removing a grant can never widen access.
function Get-ApprovedLedger {
    $path = Join-Path $DataDir $LedgerName
    if ($null -eq (Get-RawAttributes $path)) { return @() }
    $why = Get-LedgerLockProblem $path
    if ($why) {
        Write-Log "the grant ledger is not used to re-grant anything: $why"
        return @()
    }
    try {
        return @(Get-Ledger)
    } catch {
        # Fails closed: nothing is re-granted from a ledger that cannot be parsed, and
        # this decision path rewrites nothing.
        Write-Log "the grant ledger is not used to re-grant anything: it cannot be read ($($_.Exception.Message))"
        return @()
    }
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

# Paths an elevated grant must never touch, whatever list asked for them (ticket 054
# H-09): '' when $folder may be granted, otherwise why not. The share list that feeds a
# grant is writable by the management user, and a grant is (OI)(CI) plus a deep pass that
# follows every inheritance break down - so a parent of other accounts' profiles, or the
# system drive, would hand LOCAL SERVICE (and through the SFTP login, that user) far more
# than any shared folder.
function Get-SystemPathRefusal([string]$folder) {
    $full = $null
    try { $full = [System.IO.Path]::GetFullPath($folder).TrimEnd('\') } catch { return 'not a valid path' }
    $systemDrive = [System.IO.Path]::GetPathRoot($env:SystemRoot).TrimEnd('\')
    if ($full -ieq $systemDrive) { return 'the whole system drive cannot be given to the service account' }
    foreach ($protected in @($env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $DataDir)) {
        if (-not $protected) { continue }
        $p = $protected.TrimEnd('\')
        if ($full -ieq $p -or $full.StartsWith($p + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return "it is inside '$p', a system or application folder"
        }
    }
    return ''
}

function Get-GrantRefusal([string]$folder) {
    $full = $null
    try { $full = [System.IO.Path]::GetFullPath($folder).TrimEnd('\') } catch { return 'not a valid path' }
    if (Test-NameLink $full) { return 'it is a junction or a symbolic link - share the folder it points to instead' }
    $system = Get-SystemPathRefusal $full
    if ($system) { return $system }
    $allowed = @(Split-Sids $ManageSid)
    $listKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
    foreach ($entry in (Get-ChildItem -Path $listKey -ErrorAction SilentlyContinue)) {
        $profileDir = (Get-ItemProperty -Path $entry.PSPath -Name 'ProfileImagePath' -ErrorAction SilentlyContinue).ProfileImagePath
        if (-not $profileDir) { continue }
        $profileDir = $profileDir.TrimEnd('\')
        if ($allowed -contains $entry.PSChildName) { continue }
        if ($full -ieq $profileDir -or $full.StartsWith($profileDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return "it is inside the profile of another account ($(Get-SidDisplay $entry.PSChildName))"
        }
        if ($profileDir.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return "it contains the profile of another account ($(Get-SidDisplay $entry.PSChildName))"
        }
    }
    return ''
}

# Requested roots that did NOT end up granted in the last Grant-Roots - skipped, refused
# or failed. "grant-roots" turns a non-zero count into its own exit code: it used to exit 0
# with every folder skipped, and the app said "Done." over a row that stayed red.
$script:GrantShortfall = 0

# Set by Revoke-Roots when a recorded grant is - or may still be - on a folder after it ran
# (unreadable ledger, a recovery file, a failed icacls). The action still exits 0 - the service
# IS gone - but its closing lines must never read as a complete revoke (ticket 102 I02.3).
$script:RevokeIncomplete = $false

# Grants (OI)(CI)(RX) to LOCAL SERVICE on each folder and records exactly what was
# added. Never recursive-rewrites an ACL and never touches an existing administrator
# grant: it only ADDS one ACE, and the ledger remembers whether it had to.
function Grant-Roots([string[]]$folders, [string[]]$readOnlyFolders, [bool]$rightsKnown) {
    Assert-DataDir
    $script:GrantShortfall = 0
    try {
        $ledger = @(Get-Ledger)
    } catch {
        # Saving would persist ONLY this run's entries over a ledger that could not be
        # read: the record of every folder an administrator approved would be dropped
        # while its ACEs stay on the folders (ticket 057.S24-2). Nothing is granted.
        Fail ("the grant ledger '{0}' cannot be read ({1}) - the record of the approved folders is not replaced by this run's list alone. Nothing was granted and the file is left as it is; fix it by hand, or delete it to start a new record, then grant again." -f (Join-Path $DataDir $LedgerName), $_.Exception.Message) 15
    }
    # Intent records an earlier, killed run left behind are settled BEFORE this run changes anything.
    [void](Resolve-StaleGrantIntents $ledger)
    $plan = New-Object System.Collections.Generic.List[object]
    $intents = New-Object System.Collections.Generic.List[string]
    $readOnlySet = @{}
    foreach ($ro in @($readOnlyFolders)) {
        if ($ro) { $readOnlySet[$ro.TrimEnd('\')] = $true }
    }
    # What was granted, and at which level, so the deep pass below can carry the SAME
    # level down a branch - a subfolder must never end up with more access than the
    # shared folder it sits under.
    $granted = @{}
    # Every ACE change THIS run made, with what it replaced - so a ledger that cannot be written
    # does not leave grants on the folders that no record names (ticket 102 I02.6).
    $applied = New-Object System.Collections.Generic.List[object]
    foreach ($folder in $folders) {
        if (-not $folder) { continue }
        if ($folder -like '\\*') {
            # LocalService presents anonymous credentials on the network, so a UNC root
            # cannot be made to work by an ACL here. Say so precisely instead of adding
            # an ACE that changes nothing.
            Write-Log "SKIPPED '$folder': a network path cannot be served under LOCAL SERVICE (it authenticates anonymously). Use a local path, or a supported alternative service account."
            $script:GrantShortfall++
            continue
        }
        if (-not (Test-Path -LiteralPath $folder)) {
            # The usual reason besides a folder that is really gone: a MAPPED network drive.
            # Drive letters belong to the logon session that mapped them, so this elevated
            # step - and the service - never see it (ticket 054 H-19).
            Write-Log "SKIPPED '$folder': not found here. A mapped network drive letter is not visible to the service (it belongs to the signed-in user); share a local folder instead."
            $script:GrantShortfall++
            continue
        }
        $refusal = Get-GrantRefusal $folder
        if ($refusal) {
            Write-Log "REFUSED '$folder': $refusal"
            $script:GrantShortfall++
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
                # Still walked below: the root's ACE has not changed, but a folder that
                # refuses inherited ACEs may have been CREATED inside it since the last
                # run - which is the whole failure mode this pass exists for.
                $granted[$folder] = $rights
                continue
            }
            Write-Log "re-granting '$folder': $had -> $rights"
        }

        if (@($plan | Where-Object { $_.path -eq $folder }).Count -gt 0) { continue }   # listed twice
        $preExisting = if ($existing) { [bool]$existing.preExisting } else { Test-LocalServiceAce $folder }
        $plan.Add([pscustomobject]@{ path = $folder; rights = $rights; previousRights = $(if ($existing) { $had } else { $null }); preExisting = [bool]$preExisting })
    }

    # Decided first, written down second, applied third: the intent record names every ACE change
    # BEFORE the first one is made, so a run that is killed between an ACL change and the ledger
    # publication leaves protected evidence of exactly what it was about to change (ticket 102).
    if ($plan.Count -gt 0) { $intents.Add((Write-GrantIntent $plan)) }
    foreach ($item in $plan) {
        Write-Log ("granting LOCAL SERVICE {0} access on '{1}'" -f $(if ($item.rights -eq 'M') { 'read/write' } else { 'read' }), $item.path)
        & icacls.exe $item.path /grant ('*{0}:(OI)(CI)({1})' -f $SidLocalService, $item.rights) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Log "WARNING: icacls failed on '$($item.path)' (exit $LASTEXITCODE)"
            $script:GrantShortfall++
            continue
        }
        $applied.Add([pscustomobject]@{ path = $item.path; previousRights = $item.previousRights; preExisting = $item.preExisting })
        $ledger = @($ledger | Where-Object { $_.path -ne $item.path })
        $ledger += [pscustomobject]@{ path = $item.path; sid = $SidLocalService; rights = $item.rights; preExisting = $item.preExisting }
        $granted[$item.path] = $item.rights
    }

    # Second pass: the grant above is (OI)(CI), and inheritance is exactly what a
    # folder with a PROTECTED ACL does not accept. Every such folder inside a shared
    # root is therefore a wall the root grant never reaches past - and so is everything
    # below it.
    #
    # This is not hypothetical. It is what the 2026-09-01 incident actually was: the
    # share root carried a proper LOCAL SERVICE ACE, two intermediate folders had
    # inheritance disabled and carried only a stale copy of an older root ACL, and the
    # whole tree below them was being served through the Everyone ACE in that copy. The
    # day a tool created a folder without Everyone, it went dark - and re-enabling
    # inheritance on it, the obvious repair, only ever restored Everyone again.
    $breakPlan = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $granted.GetEnumerator()) {
        foreach ($break in (Get-InheritanceBreaks $entry.Key)) {
            if ($granted.ContainsKey($break)) { continue }
            if (@($breakPlan | Where-Object { $_.path -eq $break }).Count -gt 0) { continue }
            if (Test-LocalServiceAce $break) { continue }   # already has its own ACE
            $breakPlan.Add([pscustomobject]@{ path = $break; rights = $entry.Value; previousRights = $null; preExisting = $false })
        }
    }
    if ($breakPlan.Count -gt 0) { $intents.Add((Write-GrantIntent $breakPlan)) }
    foreach ($item in $breakPlan) {
        Write-Log ("granting LOCAL SERVICE {0} access on inheritance break '{1}'" -f $(if ($item.rights -eq 'M') { 'read/write' } else { 'read' }), $item.path)
        & icacls.exe $item.path /grant ('*{0}:(OI)(CI)({1})' -f $SidLocalService, $item.rights) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Log "WARNING: icacls failed on '$($item.path)' (exit $LASTEXITCODE)"
            continue
        }
        $applied.Add([pscustomobject]@{ path = $item.path; previousRights = $null; preExisting = $false })
        $ledger = @($ledger | Where-Object { $_.path -ne $item.path })
        $ledger += [pscustomobject]@{ path = $item.path; sid = $SidLocalService; rights = $item.rights; preExisting = $false }
    }

    try {
        Save-Ledger $ledger
    } catch {
        $why = $_.Exception.Message
        Write-Log "WARNING: the grant ledger could not be written ($why) - taking back the $($applied.Count) grant(s) this run made"
        $unresolved = @(Undo-AppliedGrants $applied)
        if ($unresolved.Count -gt 0) {
            # The recovery file is written BEFORE the intent records go: if writing it throws, the
            # intent records are still there to name the grants.
            $evidence = Write-GrantRecovery $unresolved
            Remove-GrantIntents $intents
            Fail ("the grants made by this run could not be recorded ({0}) and {1} of them could not be taken back; they are named in '{2}' - remove them by hand or run 'remove'." -f $why, $unresolved.Count, $evidence) 17
        }
        Remove-GrantIntents $intents
        Fail ("the grants made by this run could not be recorded ({0}); they were taken back, so nothing changed." -f $why) 17
    }
    # Published and verified: the ledger now says what the intent records said, and they have done their job.
    Remove-GrantIntents $intents
}

# The write-ahead half of a grant (ticket 102): BEFORE any ACE is changed, the changes this run is
# about to make are written to a protected file of their own - what is granted, at which level, what
# the level was, and whether LOCAL SERVICE already held a grant. The ledger alone cannot cover a
# process that is killed between the first icacls call and the ledger publication: the ACEs are on
# the folders and no record names them. An intent record that is still on disk at the start of the
# next run IS that record (Resolve-StaleGrantIntents). Same locked form as the ledger, a name of its
# own, never shared between runs. Returns the path. Nothing is granted when it cannot be written.
function Write-GrantIntent($items) {
    $intent = Join-Path $DataDir ("granted-roots.intent-{0}.json" -f [guid]::NewGuid().ToString('N'))
    try {
        $text = ConvertTo-Json -InputObject @($items | ForEach-Object {
            [pscustomobject]@{ path = $_.path; sid = $SidLocalService; rights = $_.rights; previousRights = $_.previousRights; preExisting = [bool]$_.preExisting }
        }) -Depth 4
        [void](Write-LockedFile $intent ((New-Object System.Text.UTF8Encoding($false)).GetBytes([string]$text)))
    } catch {
        Fail ("the intent record of the grants could not be written ({0}) - nothing was granted." -f $_.Exception.Message) 18
    }
    Write-Log "grant intent recorded in '$intent'"
    return $intent
}

# Drops the intent records THIS run created - never one it did not write.
function Remove-GrantIntents($paths) {
    foreach ($p in @($paths)) {
        if (-not $p) { continue }
        try { Remove-LinkOnly $p } catch { Write-Log "WARNING: the intent record '$p' could not be deleted ($($_.Exception.Message)); the next run reads it and finds nothing left to do" }
    }
}

# Settles the intent records a run LEFT BEHIND - one that was killed, or lost power, after it began
# changing ACEs. Called with the state lock held (no live run can own a record) and with the ledger
# already read, from Grant-Roots and Revoke-Roots. For every record in the locked form:
#   * an entry the ledger already records at the intended level was published - nothing to do;
#   * any other entry is a change the dead run may have made and nothing names: it is taken back
#     (an added grant removed, a changed level restored, a pre-existing grant left alone), the same
#     reversal a failed publication gets;
#   * what cannot be taken back is named in a protected recovery file, which Revoke-Roots always
#     surfaces, and the record is then dropped.
# A record that is not in the locked form, or cannot be parsed, is somebody else's file or damaged
# evidence: it is NEVER acted on and NEVER deleted - a WARNING names it. Returns $true when
# something is left that an administrator has to look at.
function Resolve-StaleGrantIntents($ledger) {
    $attention = $false
    if (-not $DataDir -or -not (Test-Path -LiteralPath $DataDir)) { return $false }
    foreach ($file in @(Get-ChildItem -LiteralPath $DataDir -Filter 'granted-roots.intent-*.json' -Force -ErrorAction SilentlyContinue)) {
        $problem = Get-LedgerLockProblem $file.FullName
        if ($problem) {
            Write-Log "WARNING: '$($file.FullName)' looks like a grant intent record but $problem, so it is not acted on and is kept - check it by hand."
            $attention = $true
            continue
        }
        $items = @()
        try {
            $items = @(ConvertFrom-JsonList (Get-Content -LiteralPath $file.FullName -Raw))
            foreach ($item in $items) {
                if ($item -isnot [System.Management.Automation.PSCustomObject] -or $item.path -isnot [string] -or -not $item.path.Trim()) { throw 'an entry has no path' }
            }
        } catch {
            Write-Log "WARNING: the grant intent record '$($file.FullName)' cannot be read ($($_.Exception.Message)); it is not acted on and is kept - check it by hand."
            $attention = $true
            continue
        }
        Write-Log "an earlier run ended before it recorded its grants; settling '$($file.FullName)'"
        $open = New-Object System.Collections.Generic.List[object]
        foreach ($item in $items) {
            $wanted = if ($item.PSObject.Properties['rights'] -and $item.rights) { [string]$item.rights } else { 'RX' }
            $recorded = @($ledger | Where-Object {
                $_.path -eq $item.path -and $(if ($_.PSObject.Properties['rights'] -and $_.rights) { [string]$_.rights } else { 'RX' }) -eq $wanted
            })
            if ($recorded.Count -gt 0) { continue }
            if (-not (Test-Path -LiteralPath $item.path)) {
                Write-Log "the folder '$($item.path)' of that run is gone - nothing to take back"
                continue
            }
            $open.Add([pscustomobject]@{ path = [string]$item.path; previousRights = $(if ($item.PSObject.Properties['previousRights'] -and $item.previousRights) { [string]$item.previousRights } else { $null }); preExisting = [bool]$item.preExisting })
        }
        $unresolved = @()
        if ($open.Count -gt 0) {
            Write-Log "taking back $($open.Count) grant(s) the earlier run may have made without recording them"
            $unresolved = @(Undo-AppliedGrants $open)
        }
        if ($unresolved.Count -gt 0) {
            $evidence = Write-GrantRecovery $unresolved
            Write-Log "WARNING: $($unresolved.Count) grant(s) of the earlier run could not be taken back; they are named in '$evidence'"
            $attention = $true
        }
        # Written to the recovery file or taken back: the record has said everything it can.
        Remove-GrantIntents @($file.FullName)
    }
    return $attention
}

# Reverts only what THIS run changed: a grant it ADDED is removed, a level it CHANGED is put back,
# and a pre-existing grant it merely recorded is never touched. Returns the entries it could not
# revert. Existing ancestor grants are not this run's, so they are not here.
function Undo-AppliedGrants($applied) {
    $unresolved = New-Object System.Collections.Generic.List[object]
    foreach ($a in $applied) {
        try {
            if ($a.previousRights) {
                & icacls.exe $a.path /grant:r ('*{0}:(OI)(CI)({1})' -f $SidLocalService, $a.previousRights) | Out-Null
            } elseif (-not $a.preExisting) {
                & icacls.exe $a.path /remove:g ('*{0}' -f $SidLocalService) | Out-Null
            } else {
                continue
            }
            if ($LASTEXITCODE -ne 0) { $unresolved.Add($a) }
        } catch {
            $unresolved.Add($a)
        }
    }
    return $unresolved.ToArray()
}

# The protected record of grants that are on folders but in no ledger: same locked ACL, a name
# of its own, never replaced or deleted by this script. Returns its path.
function Write-GrantRecovery($entries) {
    $evidence = Join-Path $DataDir ("granted-roots.recovery-{0}.json" -f [guid]::NewGuid().ToString('N'))
    $text = ConvertTo-Json -InputObject @($entries | ForEach-Object { [pscustomobject]@{ path = $_.path; sid = $SidLocalService; note = 'granted by an interrupted run; not in the ledger' } }) -Depth 4
    [void](Write-LockedFile $evidence ((New-Object System.Text.UTF8Encoding($false)).GetBytes([string]$text)))
    Write-Log "recovery evidence written to '$evidence'"
    return $evidence
}

# Maximum directories one deep pass will look at. A shared root can be a whole disk,
# and this runs inside a UAC-elevated step the user is waiting on.
$DeepScanLimit = 20000

# The folders inside $root whose ACL is PROTECTED - i.e. which refuse inherited ACEs,
# and past which an (OI)(CI) grant on the root has no effect.
#
# Only these are reported, not every unreadable folder: a folder that merely lacks the
# ACE inherits it the moment the root grant is applied, so granting it individually
# would add an explicit ACE where an inherited one already does the job, and leave a
# ledger entry to remove later for no reason.
function Get-InheritanceBreaks([string]$root) {
    $found = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $root)) { return $found }

    $pending = New-Object System.Collections.Generic.Stack[string]
    $pending.Push($root)
    $seen = 0
    while ($pending.Count -gt 0) {
        if ($seen -ge $DeepScanLimit) {
            Write-Log "deep ACL scan of '$root' stopped at $DeepScanLimit folders - there may be more inheritance breaks below"
            break
        }
        $dir = $pending.Pop()
        $seen++
        try {
            foreach ($child in [System.IO.Directory]::GetDirectories($dir)) {
                # Never follow a junction/symlink: it can point back up the tree or out
                # of the share entirely, and widening an ACL somewhere the user never
                # shared is not this script's business.
                try {
                    $attr = [System.IO.File]::GetAttributes($child)
                    if ($attr -band [System.IO.FileAttributes]::ReparsePoint) { continue }
                } catch { continue }
                try {
                    if ((Get-Acl -LiteralPath $child -ErrorAction Stop).AreAccessRulesProtected) {
                        $found.Add($child)
                        # Its children inherit from IT, so the grant we are about to put
                        # here reaches them; no need to walk further down this branch.
                        continue
                    }
                } catch { continue }
                $pending.Push($child)
            }
        } catch {
            # Unreadable even elevated, or gone mid-walk. Nothing to grant on a folder
            # we cannot enumerate, and guessing would only produce noise in the ledger.
            continue
        }
    }
    return $found
}

# Reads a property whatever case the writer used. The share list is written by the Go
# worker and edited by the Share Manager; a case difference between them must not turn
# into "no folders found", which would look exactly like a correct no-op.
function Get-JsonProp($obj, [string[]]$names) {
    if ($null -eq $obj) { return $null }
    foreach ($n in $names) {
        $p = $obj.PSObject.Properties[$n]
        if ($p -and $null -ne $p.Value) { return $p.Value }
    }
    return $null
}

# The folders the service is about to serve, read from the state directory it serves
# them FROM.
#
# This exists because -Roots is only ever passed by the Share Manager. The Server
# installer cannot pass it - at ssPostInstall there is no folder list in its hands -
# and the old code read that as "there are no folders yet". True for a fresh install,
# FALSE for the case the Server installer explicitly supports: installing over a User
# edition that was already sharing. There Copy-State brings a populated shares.json
# across, the service starts, publishes every one of those folders, and holds
# permission on none of them - a share that answers the phone with "permission denied"
# on every listing while the app shows it as working.
function Get-StateRoots {
    if (-not $DataDir) { return @() }
    $path = Join-Path $DataDir 'shares.json'
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    try {
        $raw = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
        if (-not $raw -or -not $raw.Trim()) { return @() }
        $items = @(ConvertFrom-JsonList $raw)
    } catch {
        Write-Log "WARNING: could not read the share list '$path': $($_.Exception.Message)"
        return @()
    }
    $out = @()
    foreach ($item in $items) {
        $folder = Get-JsonProp $item @('HostPath', 'hostPath', 'Path', 'path')
        if (-not $folder) { continue }
        $ro = Get-JsonProp $item @('ReadOnly', 'readOnly')
        $out += [pscustomobject]@{ path = [string]$folder; readOnly = [bool]$ro }
    }
    return $out
}

# Grants the service account access to everything the state directory says is shared.
#
# Idempotent by construction: Grant-Roots skips a root already recorded at the same
# level, so running this on every start costs a ledger read and logs nothing new. That
# is what makes it safe to call from install/repair/start as well as from the
# migration - an installation that came up without its grants heals on the next start
# instead of staying silently broken until someone finds the Hosting console.
#
# RE-applies only, since ticket 054 H-09: shares.json is writable by the management user
# (by design) and settable over the pipe without elevation, and every elevated start,
# restart, repair and Server upgrade used to grant whatever it listed - a standard user
# could have LOCAL SERVICE, and through the SFTP login themselves, handed any folder on
# the machine by an administrator who only clicked "Restart service". Now a folder is
# re-granted here only when an administrator already approved it (the locked ledger), and
# never above the level approved; a folder that is merely listed is left for the Share
# Manager, which asks with the list in front of the person approving it.
function Grant-StateRoots([string]$reason) {
    $roots = @(Get-StateRoots)
    if ($roots.Count -eq 0) { return }
    $approved = @{}
    foreach ($entry in @(Get-ApprovedLedger)) {
        if ($entry.path) { $approved[[string]$entry.path] = [string]$entry.rights }
    }
    $again = @()
    $againReadOnly = @()
    foreach ($r in $roots) {
        if (-not $approved.ContainsKey($r.path)) {
            Write-Log "NOT granting '$($r.path)' ($reason): no administrator has approved it yet. The Share Manager offers the grant with the folder list shown."
            continue
        }
        $again += $r.path
        if ($r.readOnly -or $approved[$r.path] -ne 'M') { $againReadOnly += $r.path }
    }
    if ($again.Count -eq 0) { return }
    Write-Log ("re-applying the approved service access on {0} shared folder(s) ({1})" -f $again.Count, $reason)
    Grant-Roots $again $againReadOnly $true
}

# Removes ONLY the ACEs this script recorded, and only where LOCAL SERVICE had no
# grant of its own before we arrived.
#
# Always the LOCAL SERVICE SID, never the one written in the ledger: the plain ledger may
# have been edited, and "remove this SID from that folder" run elevated must not become a
# way to strip Administrators from a system folder. System folders are never touched.
function Revoke-Roots {
    if (-not $DataDir -or -not (Test-Path -LiteralPath $DataDir)) { return }
    $path = Join-Path $DataDir $LedgerName
    # Evidence this script never deletes: a recovery file names grants that sit on folders but in
    # no ledger, so a revoke that ignored it would read as complete when it was not.
    foreach ($recovery in @(Get-ChildItem -LiteralPath $DataDir -Filter 'granted-roots.recovery-*.json' -Force -ErrorAction SilentlyContinue)) {
        Write-Log "WARNING: '$($recovery.FullName)' names folder grants that are in no ledger; they are NOT revoked here and the file is kept - remove them by hand."
        $script:RevokeIncomplete = $true
    }
    try {
        $entries = @(Get-Ledger)
    } catch {
        # An unreadable ledger is the only record naming the roots whose ACEs the loop
        # below revokes - deleting it would leave those grants on the folders with no
        # record of them (ticket 057.S24-2). The file is kept, and nothing is revoked.
        $script:RevokeIncomplete = $true
        Write-Log "WARNING: the grant ledger '$path' cannot be read ($($_.Exception.Message)), so it is kept as it is and NO recorded grant is revoked. Fix the file by hand and remove again, or delete it once the shared folders have been checked."
        return
    }
    # Grants a killed run made without recording them are taken back first (or named in a recovery file).
    if (Resolve-StaleGrantIntents $entries) { $script:RevokeIncomplete = $true }
    $failedRevokes = 0
    foreach ($entry in $entries) {
        if (-not $entry.path) { continue }
        if ($entry.preExisting) {
            Write-Log "keeping the pre-existing LOCAL SERVICE grant on '$($entry.path)'"
            continue
        }
        if (-not (Test-Path -LiteralPath $entry.path)) { continue }
        $system = Get-SystemPathRefusal ([string]$entry.path)
        if ($system) {
            Write-Log "not touching '$($entry.path)': $system"
            continue
        }
        Write-Log "removing our LOCAL SERVICE grant on '$($entry.path)'"
        & icacls.exe $entry.path /remove:g ('*{0}' -f $SidLocalService) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            $script:RevokeIncomplete = $true
            $failedRevokes++
            Write-Log "WARNING: icacls could not remove the LOCAL SERVICE grant on '$($entry.path)' (exit $LASTEXITCODE)"
        }
    }
    if ($failedRevokes -gt 0) {
        # The record of a grant that is still on a folder is not thrown away.
        Write-Log "WARNING: $failedRevokes recorded grant(s) could not be revoked, so the ledger '$path' is kept; run remove again once they can be."
        return
    }
    if (Test-Path -LiteralPath $path) {
        try { Remove-Item -LiteralPath $path -Force -ErrorAction Stop } catch {
            $script:RevokeIncomplete = $true
            Write-Log "WARNING: the grant ledger '$path' could not be deleted ($($_.Exception.Message))"
        }
    }
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

# --- the state lock ------------------------------------------------------------

# Two helpers on the same state directory would read the same ledger, grant on top of each
# other and publish over each other's result - the later one silently dropping the first's
# approved history (ticket 102 I02.5). Every action that can read or write the ledger takes
# this machine-wide lock FIRST; waiting is bounded, and a run that cannot get it changes
# nothing and says so (exit 16). Held by the process until it exits: Windows releases a named
# mutex whose owner is gone, and the next waiter is told so (Abandoned) and logs it.
$StateLockWaitMs = 10000
$script:StateLock = $null

function Get-StateLockName {
    $normalized = ([System.IO.Path]::GetFullPath($DataDir)).TrimEnd('\').ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized)) | ForEach-Object { $_.ToString('x2') }) -join '' } finally { $sha.Dispose() }
    return 'Global\FastMediaSorterCompanionHelper-' + $hash.Substring(0, 24)
}

function Enter-StateLock {
    if (-not $DataDir) { return }
    $mutex = New-Object System.Threading.Mutex($false, (Get-StateLockName))
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne($StateLockWaitMs)
    } catch [System.Threading.AbandonedMutexException] {
        $acquired = $true
        Write-Log 'the previous holder of the state lock ended without releasing it; taking it over'
    }
    if (-not $acquired) {
        $mutex.Dispose()
        Fail ("another run of this helper holds the state lock for '{0}' (waited {1} ms). Nothing was changed; run it again when the other one has finished." -f $DataDir, $StateLockWaitMs) 16
    }
    $script:StateLock = $mutex
}

# --- actions ------------------------------------------------------------------

# Any terminating error from here on - a locked file under Copy-Item, New-Service,
# Start-Service - used to end the script with exit 1 and NO reason in the log the UI tells
# the user to send (ticket 054 H-13).
trap {
    Write-Log ("FAILED: {0}" -f (($_ | Out-String).Trim()))
    if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }
    exit 1
}

# Elevation first, and nothing written anywhere before it: a non-elevated run must not
# leave a directory behind for the next elevated one to adopt.
Assert-Elevated
Write-Log "action=$Action exe='$ExePath' dataDir='$DataDir'"

# An existing state directory is checked BEFORE anything is logged into it, copied into
# it or read from it (ticket 046 H-02). "remove" still takes the registration and the
# firewall rule away from an untrusted one - leaving a service that serves out of it would
# be worse - and "stop" only ever stops; everything else refuses.
$script:DataDirDistrust = if ($DataDir) { Get-DirectoryDistrust $DataDir } else { '' }
if ($script:DataDirDistrust -and $Action -ne 'remove' -and $Action -ne 'stop') {
    Fail "the machine state directory refused: $script:DataDirDistrust" 11
}
if (-not $script:DataDirDistrust) { Initialize-Log }

# Everything that can read or write the grant ledger is serialized per state directory.
if ($Action -ne 'stop') { Enter-StateLock }

switch ($Action) {

    'install' {
        Assert-Worker
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Initialize-MachineDataDir
        Sync-ServiceBinaries
        # Updated in place over a previous registration, created otherwise.
        Register-Service
        Set-FirewallRule $true
        # Every path that brings the service up also makes sure it can read what it is
        # about to publish - as far as an administrator already approved (ticket 054 H-09).
        # A grant is the one part of the setup that lives outside the service
        # registration - on the folders themselves - so it is the part an older
        # installer, a restored state directory or a hand-edited share list can leave
        # missing, with nothing in the service's own state to show for it.
        Grant-StateRoots 'service install'
        Start-ServiceAndVerify
    }

    'repair' {
        Assert-Worker
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Initialize-MachineDataDir
        # Also re-stages the worker, which is what makes this the "the app updated but
        # the service is still running the old copy" repair as well as a registration one.
        Sync-ServiceBinaries
        # The command line, the account, the start type and the recovery actions all end
        # up matching this script again, whatever an earlier version or a hand edit left
        # behind - updated in place, so a handle someone holds on the old registration
        # cannot leave the machine without a service (ticket 054 H-13).
        Register-Service
        Set-FirewallRule $true
        Grant-StateRoots 'service repair'
        Start-ServiceAndVerify
    }

    'remove' {
        Remove-ServiceRegistration
        Set-FirewallRule $false
        if ($RestoreUserRule -and $ExePath -and (Test-Path -LiteralPath $ExePath)) {
            # The User edition serves from the installed worker again; its opt-in rule
            # comes back under the same name the opt-in helper uses.
            $script:ServiceExePath = $ExePath
            Set-FirewallRule $true
        }
        if ($script:DataDirDistrust) {
            # Nothing inside an untrusted directory is read (the ledger) or deleted (bin):
            # either could be steered by whoever owns it.
            Fail "the service registration and the firewall rule were removed, but the machine state directory was left untouched: $script:DataDirDistrust" 11
        }
        Revoke-Roots
        Remove-ServiceBinaries
        # The state directory is deliberately LEFT in place: it holds the identity the
        # phones pinned. Deleting it is a separate, explicit choice.
        if ($script:RevokeIncomplete) {
            Write-Log "server role removed, but the folder grants made for it were NOT all revoked (see the WARNING lines above). State kept in '$DataDir'."
        } else {
            Write-Log "server role removed. State kept in '$DataDir' - delete it by hand only if you want the phones to re-pair."
        }
    }

    'start' {
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed" 9 }
        Grant-StateRoots 'service start'
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
        Grant-StateRoots 'service restart'
        Start-ServiceAndVerify
    }

    'migrate-to-server' {
        Assert-Worker
        Assert-DataDir
        if (-not $UserDataDir) { Fail 'no -UserDataDir given' 2 }
        # Order matters: the User edition worker must be down before its files are read,
        # and the service must not be registered until the identity has been validated
        # in its new home (spec §2 - migration is a transaction). Every worker on the
        # machine is stopped, not only this installation's: the one whose state is being
        # migrated may run from another installation, and any worker left running would
        # hold the control pipe the service is about to need. Each is named in the log.
        Get-Process -Name 'fms-share-worker' -ErrorAction SilentlyContinue | ForEach-Object {
            $procPath = $null
            try { $procPath = $_.Path } catch { }
            Write-Log ("stopping worker pid {0} (session {1}, '{2}')" -f $_.Id, $_.SessionId, $procPath)
            try { $_.Kill(); [void]$_.WaitForExit(5000) } catch { }
        }
        $sourceDir = Resolve-UserDataDir $UserDataDir
        # Resolved from the state directory that actually holds the identity, not from
        # the elevating administrator's profile - see Resolve-ManagementSid.
        $ManageSid = Resolve-ManagementSid $ManageSid $sourceDir
        Write-Log "management SID(s): $ManageSid"
        # The machine directory is verified (or created with its final ACL) BEFORE the
        # identity is copied into it (ticket 046 H-02), and the worker is staged into its
        # administrators-only bin before anything runs it (ticket 054 H-08) - Copy-State
        # executes it to inspect both stores.
        Initialize-MachineDataDir
        Sync-ServiceBinaries
        # -OnlyWhenTargetIsFresh: the Server installer passes /MIGRATEFROMUSER on EVERY
        # install, so this runs on ordinary upgrades too. Only the first one is a
        # migration; after that the machine directory is the live state and must win.
        Copy-State $sourceDir $DataDir -OnlyWhenTargetIsFresh
        # Again after the copy: the copied files take the directory's entries.
        Set-MachineDataDirAcl
        Register-Service
        Set-FirewallRule $true
        # Folders a user picked are invisible to LOCAL SERVICE until they are granted,
        # so a switch made from the app would otherwise end in a service that runs
        # perfectly and serves nothing - and the fix would be a SECOND UAC prompt right
        # after the first. Same elevation, same transaction.
        #
        # BEFORE the service starts, not after: the worker publishes whatever the share
        # list names the moment it comes up, and granting afterwards leaves a window in
        # which a phone is offered folders that refuse to open.
        if ($Roots) {
            $rightsKnown = $PSBoundParameters.ContainsKey('ReadOnlyRoots')
            Grant-Roots `
                @(Split-RootList $Roots) `
                @(Split-RootList $ReadOnlyRoots) `
                $rightsKnown
            if ($script:GrantShortfall -gt 0) {
                Write-Log "WARNING: $script:GrantShortfall of the shared folders were not granted - see the SKIPPED/REFUSED lines above"
            }
        } else {
            # The Server installer's path: it has no folder list to pass. What the state
            # names is re-granted only as far as an administrator approved it before; the
            # rest waits for the Share Manager, which the installer opens next and which
            # asks with the list shown (ticket 054 H-09).
            Grant-StateRoots 'migrated state'
        }
        Start-ServiceAndVerify
    }

    'migrate-to-user' {
        Assert-Worker
        if (-not $UserDataDir) { Fail 'no -UserDataDir given' 2 }
        # The inspection runs the service's own copy when it is there: that one sits in
        # an administrators-only directory (ticket 054 H-08).
        $staged = Get-StagedExePath
        if ($staged -and (Test-Path -LiteralPath $staged) -and -not (Get-DirectoryDistrust (Split-Path -Parent $staged))) {
            $script:InspectExePath = $staged
        }
        Stop-ServiceGracefully
        Copy-State $DataDir $UserDataDir
        Remove-ServiceRegistration
        Revoke-Roots
        # The firewall opening follows whichever copy listens next. The service ran the
        # staged worker, which is about to be deleted; the User edition serves from the
        # installed one, a different program as far as the firewall is concerned. So the
        # rule is re-pointed rather than left naming a file that will not exist.
        if ($staged) { & netsh.exe advfirewall firewall delete rule name=all program="$staged" | Out-Null }
        $script:ServiceExePath = $ExePath
        Set-FirewallRule $true
        Remove-ServiceBinaries
        Write-Log 'returned to User edition hosting. Start the Share Manager to resume sharing.'
    }

    'grant-roots' {
        Assert-DataDir
        if (-not $Roots) { Fail 'no -Roots given' 2 }
        # Only where the service role lives. Creating the machine directory here would
        # make it the state store of a User edition worker too, splitting its settings.
        if ($null -eq (Get-RawAttributes $DataDir)) { Fail "'$DataDir' does not exist - the service role is not installed" 9 }
        # "Was -ReadOnlyRoots passed at all", not "is it empty": an empty value is a
        # legitimate "every root is writable", while an absent parameter is an older
        # caller that never had the notion, and must keep getting read-only.
        $rightsKnown = $PSBoundParameters.ContainsKey('ReadOnlyRoots')
        Grant-Roots `
            @(Split-RootList $Roots) `
            @(Split-RootList $ReadOnlyRoots) `
            $rightsKnown
        if ($script:GrantShortfall -gt 0) {
            Fail "$script:GrantShortfall of the requested folders were not granted - see the SKIPPED/REFUSED/WARNING lines above" 12
        }
    }
}

if ($script:RevokeIncomplete) {
    Write-Log "action=$Action completed WITH WARNINGS: some recorded folder grants were not revoked"
} else {
    Write-Log "action=$Action completed"
}
# The same rule the trap keeps: when the log never opened - the state directory is
# gone, so Initialize-Log bailed, or it stopped taking lines mid-run - the lines go to
# the fallback copy instead of being discarded, and a SUCCESS is on record just like a
# failure (ticket 057.S24-1).
if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }
exit 0
