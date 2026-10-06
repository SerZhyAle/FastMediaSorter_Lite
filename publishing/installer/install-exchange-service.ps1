<#
.SYNOPSIS
    The single elevated, auditable helper for the Media Exchange Server as a Windows service
    (ticket 144; umbrella 133 section 6.1, owner decision Q-133-4).

.DESCRIPTION
    Every machine-affecting step of the exchange service lives here and nowhere else:
    registering / repairing / removing the service `FastMediaSorterExchangeServer`, the machine
    state directory with its ACLs, the SCM security descriptor that lets the enrolled user start
    and stop it without UAC, the program-scoped firewall rule, and the one-time identity
    migration from the user's store. It is the exchange counterpart of install-share-service.ps1
    (copied and adapted, not imported) and is called by the elevated Server installer and, at
    runtime, by the Share Manager through ShellExecute "runas" (one UAC prompt). Nothing on the
    network-facing path can reach it.

    Rules the code below keeps everywhere:
      * The TLS identity (tls\cert.pem + tls\key.pem) is NEVER regenerated or overwritten. A
        migration copies it only when the machine store holds no identity yet, compares the
        fingerprint (`fms-exchange-server inspect`) before and after, and aborts BEFORE the
        service is registered on a mismatch: a changed certificate unpins every producer.
      * `remove` takes the service, its firewall rule and the staged binary away and KEEPS the
        data directory - it holds the identity the producers pinned.
      * `repair` runs only when the service exists. A fresh registration is `install`.
      * The management SID is validated against ^S-1-[0-9]+(-[0-9]+)+\z BEFORE it enters an
        SDDL string (the ACE-injection rule) or an ACL; any other input is rejected (exit 2).

    State layout (all under -DataDir, %ProgramData%\FastMediaSorterExchange by default):
      exchange.json   enrolled user: Modify on this FILE only; LocalService, SYSTEM, Admins from
                      the directory
      tls\cert.pem    enrolled user: Read (the certificate is public)
      tls\key.pem     LocalService + SYSTEM + Administrators ONLY, owner Administrators,
                      inheritance off - the enrolled user is excluded
      bin\            the staged fms-exchange-server.exe (+ .sha256), code: LocalService may read
                      and execute it, nobody but SYSTEM / Administrators may write it
      install-exchange-service.log

    Exit codes: 0 ok | 1 unexpected error | 2 missing or invalid argument / unusable exe |
    3 the service did not start | 4 an ACL (file or SCM) could not be set | 5 not elevated |
    6 state did not validate | 7 certificate (fingerprint) mismatch | 8 firewall |
    9 service not installed | 10 several identities (migration source ambiguous) |
    11 the state directory (or bin) cannot be trusted | 13 the service account is not
    LocalService | 14 the old registration is still marked for deletion | 16 another run of
    this helper holds the state lock (nothing was changed).

    The log is appended to <DataDir>\install-exchange-service.log once the directory is
    verified - and to the elevated account's own %TEMP% copy when it cannot be trusted - and the
    fallback copy is written on SUCCESS too.

.PARAMETER Action
    install | repair | remove | start | stop | restart | migrate-to-service

.PARAMETER ExePath
    Absolute path of fms-exchange-server.exe in the installed folder (it is staged into
    <DataDir>\bin, and the service and the firewall rule name the STAGED copy).

.PARAMETER DataDir
    Machine state directory. Default %ProgramData%\FastMediaSorterExchange.

.PARAMETER UserDataDir
    Per-user store of the enrolling user (%LOCALAPPDATA%\FastMediaSorterExchange): the
    migration source. Only a hint - see Resolve-UserDataDir.

.PARAMETER ManageSid
    String SID(s) of the enrolled user(s) allowed to start/stop the service and to change
    exchange.json. Comma-separated. Omitted: the profile that owns -UserDataDir, else the
    elevated account.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('install', 'repair', 'remove', 'start', 'stop', 'restart', 'migrate-to-service')]
    [string]$Action,

    [string]$ExePath,

    [string]$DataDir = (Join-Path $env:ProgramData 'FastMediaSorterExchange'),

    [string]$UserDataDir,

    [string]$ManageSid
)

$ErrorActionPreference = 'Stop'

$ServiceName    = 'FastMediaSorterExchangeServer'
$ServiceDisplay = 'Media Exchange Server'
$ServiceDesc    = 'Relays media between your devices through this PC: the Media Exchange Server of Fast Media Sorter & Sharing. Serves with nobody signed in.'
$FirewallRule   = 'FastMediaSorter Media Exchange Server'
$ExeName        = 'fms-exchange-server.exe'
$ConfigName     = 'exchange.json'
$TlsDirName     = 'tls'
$CertName       = 'cert.pem'
$KeyName        = 'key.pem'
$BinDirName     = 'bin'
$LogName        = 'install-exchange-service.log'

# Set by Sync-ServiceBinaries: the executable the SERVICE is registered against and the one the
# firewall rule must name. Until then, the caller-supplied path.
$script:ServiceExePath = $ExePath

# The copy of the exchange server this script EXECUTES (inspect). Once staged into the
# administrators-only bin directory it is that copy, never the one in the application folder:
# a per-user installation leaves that folder writable by the user, and a file that can change
# under us must not be what an elevated step runs.
$script:InspectExePath = $ExePath

# Well-known SIDs, used instead of names: they resolve identically on every Windows language.
$SidSystem           = 'S-1-5-18'
$SidLocalService     = 'S-1-5-19'
$SidAdmins           = 'S-1-5-32-544'
$SidTrustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'

# Windows PowerShell (.NET Framework) and PowerShell 7 (.NET) reach the ACL-at-creation APIs
# differently; the installers run powershell.exe, 7 only has to work.
$script:IsDesktopEdition = ($PSVersionTable.PSEdition -ne 'Core')

# --- pure helpers (extracted and exercised by tests\Companion.Tests\ExchangeHelperTests.vb) ---

# $true only for a well-formed string SID. [0-9] rather than \d (which matches non-ASCII digits
# in .NET) and \z rather than $ (which also matches before a trailing newline): the value is
# about to be spliced into an SDDL string, so nothing but a SID may get through.
function Test-ManageSid([string]$sid) {
    if (-not $sid) { return $false }
    return [bool]($sid -match '^(?i:S-1-[0-9]+(-[0-9]+)+)\z')
}

function Split-Sids([string]$value) {
    if (-not $value) { return @() }
    return @($value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# The SCM security descriptor: the default service DACL (SYSTEM, Administrators, interactive
# users, service users - Windows' own) plus one ACE per enrolled SID granting
# SERVICE_START (RP) | SERVICE_STOP (WP) | SERVICE_QUERY_STATUS (LC) | SERVICE_INTERROGATE (LO).
# Built ONLY from validated SIDs; a value that is not a SID throws before it can reach the string.
function New-ServiceSddl([string[]]$sids) {
    $defaultDacl = '(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'
    $aces = ''
    $seen = @{}
    foreach ($sid in @($sids)) {
        if (-not (Test-ManageSid $sid)) { throw "refusing to build a service security descriptor from '$sid': it is not a string SID" }
        $key = $sid.ToUpperInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $aces += ('(A;;RPWPLCLO;;;{0})' -f $sid)
    }
    return ('D:' + $defaultDacl + $aces)
}

# The service command line. Exactly this shape: the Share Manager recognises "our" registration
# by it (serve --service plus the exe name), and the Go binary parses it.
function Get-ServiceCommandLine([string]$exe, [string]$dataDir) {
    return ('"{0}" serve --service --config "{1}"' -f $exe, ([System.IO.Path]::Combine($dataDir.TrimEnd('\'), 'exchange.json')))
}

# --- logging ------------------------------------------------------------------

$script:LogPath = $null
$script:PendingLog = New-Object System.Collections.Generic.List[string]

function Get-FallbackLogPath {
    try { return (Join-Path ([System.IO.Path]::GetTempPath()) $LogName) } catch { return $null }
}

function Write-PendingLog([string]$path) {
    if (-not $path -or $script:PendingLog.Count -eq 0) { return }
    try {
        Add-Content -LiteralPath $path -Value $script:PendingLog.ToArray() -Encoding UTF8
        $script:PendingLog.Clear()
    } catch {
        Write-Warning ("the helper's log could not be written to '{0}' ({1}); {2} line(s) kept in memory" -f $path, $_.Exception.Message, $script:PendingLog.Count)
    }
}

# Opens the log inside the state directory - ONLY after that directory was verified, and never
# by creating it (creating it here, before the ownership check, is one way a directory owned by
# someone else gets adopted by the next elevated run).
function Initialize-Log {
    if (-not $DataDir) { return }
    $attr = Get-RawAttributes $DataDir
    if ($null -eq $attr -or -not ($attr -band [System.IO.FileAttributes]::Directory)) { return }
    $candidate = Join-Path $DataDir $LogName
    # A log file that is a link would turn every elevated Add-Content into a write wherever the
    # link points. It is only our own text, so the link goes, never its target.
    try { if (Test-ReparsePoint $candidate) { Remove-LinkOnly $candidate } } catch { return }
    $script:LogPath = $candidate
    Write-PendingLog $candidate
}

# Write-Host, not Write-Output: a log line on the success stream becomes part of the RETURN VALUE
# of whichever function logged it.
function Write-Log([string]$message) {
    $line = "{0} {1}" -f (Get-Date).ToUniversalTime().ToString('s'), $message
    Write-Host $line
    if ($script:LogPath) {
        try {
            Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
        } catch {
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

function Assert-Exe {
    if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        Fail "exchange server executable not found: '$ExePath'" 2
    }
}

function Assert-DataDir {
    if (-not $DataDir) { Fail 'no -DataDir given' 2 }
}

# --- filesystem safety ----------------------------------------------------------
#
# Everything this script writes lands in a directory an elevated process has to be able to
# trust, and %ProgramData% is not such a place by default: any standard user may create a
# subfolder there and becomes its owner. A junction or a symbolic link makes an elevated write or
# ACL change land wherever it points. So: no link is ever followed, an existing directory is
# adopted only when an administrator or the system owns it, and a directory this script creates
# carries its final ACL from the first instant.

$TrustedOwnerSids = @($SidSystem, $SidAdmins, $SidTrustedInstaller, $SidLocalService)

function Get-RawAttributes([string]$path) {
    if (-not $path) { return $null }
    try { return [System.IO.File]::GetAttributes($path) } catch { return $null }
}

function Test-ReparsePoint([string]$path) {
    $attr = Get-RawAttributes $path
    if ($null -eq $attr) { return $false }
    return [bool]($attr -band [System.IO.FileAttributes]::ReparsePoint)
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

# Deletes a directory tree without ever following a link inside it.
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
    # Under the "objects created by administrators are owned by the creator" policy, a directory
    # this same elevated account made earlier is owned by the account itself.
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
        return ("'$path' belongs to '{0}', not to an administrator or the system. Whoever owns it could read the TLS key and the password hash kept there and change what the service runs, so it is not used. Nothing was changed. Check its contents, then delete it or make Administrators its owner (icacls ""$path"" /setowner *S-1-5-32-544), and run this again." -f (Get-SidDisplay $owner))
    }
    return ''
}

# $true when $path is an existing directory this script may adopt, $false when nothing is there
# yet (the caller creates it). Anything else stops the whole action BEFORE a byte is written.
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

# A directory descriptor built from nothing: owned by Administrators, no inheritance from the
# parent, SYSTEM and Administrators in full control, and ONLY what the caller adds.
function New-LockedDirectorySecurity {
    $sec = New-Object System.Security.AccessControl.DirectorySecurity
    $sec.SetOwner((New-Object System.Security.Principal.SecurityIdentifier($SidAdmins)))
    $sec.SetAccessRuleProtection($true, $false)
    $sec.AddAccessRule((New-AllowRule $SidSystem 'FullControl' 'tree'))
    $sec.AddAccessRule((New-AllowRule $SidAdmins 'FullControl' 'tree'))
    return $sec
}

# The state directory. LocalService may create, change and delete the FILES of the state (its
# log, and tls\ on a first run) but holds no DELETE on the directory itself - with it the account
# could rename the whole store away while the service is stopped. The enrolled user gets NOTHING
# here: exchange.json carries an explicit entry of its own, and tls\ stays out of their reach.
function New-DataDirSecurity {
    $sec = New-LockedDirectorySecurity
    $sec.AddAccessRule((New-AllowRule $SidLocalService 'ReadAndExecute, Write' 'this'))
    $sec.AddAccessRule((New-AllowRule $SidLocalService 'Modify' 'children'))
    return $sec
}

# The service's binary directory: code, so read+execute for the account that runs it and for the
# enrolled user (to compare it with the installed copy) - never write.
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

# Creates a directory WITH its descriptor, so there is no moment in which it carries the parent's
# "Users may create files here". If something appeared in between, the create does not apply the
# descriptor - which is why every caller verifies the result instead of assuming it.
function New-SecureDirectory([string]$path, $security) {
    $info = New-Object System.IO.DirectoryInfo($path)
    if ($script:IsDesktopEdition) { $info.Create($security) }
    else { [System.IO.FileSystemAclExtensions]::Create($info, $security) }
}

function Get-FileSecurity([string]$path) {
    $info = New-Object System.IO.FileInfo($path)
    if ($script:IsDesktopEdition) { return $info.GetAccessControl() }
    return [System.IO.FileSystemAclExtensions]::GetAccessControl($info)
}

function Set-FileSecurity([string]$path, $security) {
    $info = New-Object System.IO.FileInfo($path)
    if ($script:IsDesktopEdition) { $info.SetAccessControl($security) }
    else { [System.IO.FileSystemAclExtensions]::SetAccessControl($info, $security) }
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

# '' when exchange.json carries entries for the service identities and the enrolled user(s)
# only AND every enrolled user holds an explicit entry; otherwise what is wrong with it.
function Get-ConfigFileProblem([string]$path) {
    try { $acl = Get-Acl -LiteralPath $path } catch { return "its ACL cannot be read: $($_.Exception.Message)" }
    $manage = @(Split-Sids $ManageSid)
    $allowed = @($SidSystem, $SidAdmins, $SidLocalService) + $manage
    $explicit = @{}
    foreach ($rule in $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        if ($allowed -notcontains $sid) { return "it carries an unexpected entry for '$(Get-SidDisplay $sid)'" }
        if (-not $rule.IsInherited) { $explicit[$sid] = $true }
    }
    foreach ($sid in $manage) {
        if (-not $explicit.ContainsKey($sid)) { return "the enrolled user '$(Get-SidDisplay $sid)' has no entry on it" }
    }
    return ''
}

# --- exchange server invocation -------------------------------------------------

# Runs the exchange server and returns its exit code and streams. Through Process with redirected
# streams (read BEFORE WaitForExit, so a full pipe buffer cannot deadlock the wait) rather than
# "& $exe ..": a GUI-subsystem image is neither waited for nor captured when called directly.
function Invoke-Exchange([string]$exeArgs) {
    if (-not (Test-Path -LiteralPath $script:InspectExePath -PathType Leaf)) { Fail "exchange server executable not found: '$script:InspectExePath'" 2 }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $script:InspectExePath
    $psi.Arguments              = $exeArgs
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.CreateNoWindow         = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; StdOut = $out; StdErr = $err }
}

function Quote-Path([string]$path) {
    # A trailing backslash right before the closing quote is an escape to the Windows argument parser.
    return '"' + $path.TrimEnd('\') + '"'
}

# Read-only verdict on a state directory, from the exchange server itself, so the installer and
# the thing that later loads the certificate agree by construction. Keys of its JSON report:
# dataDir, configPresent, passwordSet, passwordHashValid, certPresent, fingerprint, listen,
# portRange, error.
function Get-StateInfo([string]$dir) {
    $config = Join-Path $dir $ConfigName
    $run  = Invoke-Exchange ('inspect --config ' + (Quote-Path $config))
    $text = ("$($run.StdOut)`n$($run.StdErr)").Trim()
    if ($run.ExitCode -ne 0) { Fail "state directory '$dir' did not validate (exit $($run.ExitCode)): $text" 6 }
    try { return ($run.StdOut | ConvertFrom-Json) } catch { Fail "could not read the state verdict for '$dir': $text" 6 }
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
    # Bounded wait: a file-replacing install must not start until the listener is released.
    try { (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } catch { }
    $svc = Get-ServiceOrNull
    if ($svc -and $svc.Status -ne 'Stopped') { Write-Log "WARNING: $ServiceName is still $($svc.Status)" }
}

function Remove-ServiceRegistration {
    if (-not (Get-ServiceOrNull)) { return }
    Stop-ServiceGracefully
    Write-Log "deleting $ServiceName .."
    & sc.exe delete $ServiceName | Out-Null
    # The SCM keeps a deleted service until the last handle closes (an open Services console is
    # enough); give it time so an immediately following create does not hit
    # ERROR_SERVICE_MARKED_FOR_DELETE.
    for ($i = 0; $i -lt 120 -and (Get-ServiceOrNull); $i++) { Start-Sleep -Milliseconds 250 }
    if (Get-ServiceOrNull) { Write-Log "WARNING: $ServiceName is still marked for deletion - a Services console or another tool keeps a handle to it open" }
}

function Get-StagedExePath {
    if (-not $DataDir) { return '' }
    return (Join-Path (Join-Path $DataDir $BinDirName) $ExeName)
}

# The hash a .sha256 sidecar names: its first token, 64 hex digits. $null when there is no
# sidecar; throws when there is one that is not a hash.
function Get-SidecarHash([string]$exe) {
    $sidecar = "$exe.sha256"
    if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { return $null }
    $text = (Get-Content -LiteralPath $sidecar -Raw)
    if ($text -notmatch '^\s*([0-9a-fA-F]{64})(\s|$)') { throw "the checksum file '$sidecar' does not start with a SHA-256 hash" }
    return $Matches[1].ToLowerInvariant()
}

function Get-FileSha256([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Puts the service's own copy of the exchange server in place and points $script:ServiceExePath at
# it, after checking the source against its .sha256 sidecar. Idempotent, and a no-op copy-wise
# when the caller already handed us the staged path.
#   * a per-user installation lives under %LOCALAPPDATA%\Programs, whose ACL does not grant
#     LocalService - a service registered against it cannot even start;
#   * the service holds its binary open, so an application update could not replace it;
#   * the service keeps working when the app is uninstalled.
function Sync-ServiceBinaries {
    Assert-Exe
    Assert-DataDir

    $staged = Get-StagedExePath
    $binDir = Split-Path -Parent $staged
    $expected = $null
    try { $expected = Get-SidecarHash $ExePath } catch { Fail $_.Exception.Message 2 }
    $actual = Get-FileSha256 $ExePath
    if ($expected -and $expected -ne $actual) {
        Fail "'$ExePath' does not match its .sha256 (expected $expected, found $actual) - the executable is damaged or was replaced. Nothing was staged." 2
    }
    if (-not $expected) { Write-Log "WARNING: no .sha256 beside '$ExePath' - its integrity could not be checked (sha256 $actual)" }

    if ([IO.Path]::GetFullPath($ExePath) -ieq [IO.Path]::GetFullPath($staged)) {
        [void](Assert-TrustedDirectory $binDir 'the service binary directory')
        $script:ServiceExePath = $staged
        $script:InspectExePath = $staged
        Write-Log "exchange server already staged at '$staged'"
        return
    }

    Stop-ServiceGracefully

    # Never reused: whatever is there was put there by someone at some point, and it is about to
    # become the image a service runs. Deleted (links removed as links, never followed) and
    # recreated with its final ACL is the only state that proves itself.
    if ($null -ne (Get-RawAttributes $binDir)) {
        Write-Log "replacing '$binDir'"
        Remove-TreeNoFollow $binDir
    }
    New-SecureDirectory $binDir (New-BinDirSecurity)
    [void](Assert-TrustedDirectory $binDir 'the service binary directory')
    $problem = Get-LockdownProblem $binDir (@($SidSystem, $SidAdmins, $SidLocalService) + @(Split-Sids $ManageSid))
    if ($problem) { Fail "could not secure '$binDir': $problem" 4 }

    Write-Log "staging exchange server: '$ExePath' -> '$staged'"
    Copy-Item -LiteralPath $ExePath -Destination $staged -Force
    if ((Get-FileSha256 $staged) -ne $actual) { Fail "the staged copy '$staged' does not match its source - the copy was damaged" 11 }
    [System.IO.File]::WriteAllText("$staged.sha256", ("{0}  {1}`n" -f $actual, $ExeName), (New-Object System.Text.UTF8Encoding($false)))

    $script:ServiceExePath = $staged
    $script:InspectExePath = $staged
}

# Drops the staged copy when the service goes away. The state directory beside it is deliberately
# kept (it holds the pinned identity) - this removes only code.
function Remove-ServiceBinaries {
    $staged = Get-StagedExePath
    if (-not $staged) { return }
    $binDir = Split-Path -Parent $staged
    if ($null -eq (Get-RawAttributes $binDir)) { return }
    try {
        # Never Remove-Item -Recurse here: on Windows PowerShell it can walk INTO a junction.
        Remove-TreeNoFollow $binDir
        Write-Log "removed the staged exchange server in '$binDir'"
    } catch {
        # A file still held by a stopping service must not fail the whole action - the
        # registration is already gone, which is what "remove" means.
        Write-Log "could not remove '$binDir': $_"
    }
}

function Get-ServiceBinPath {
    return (Get-ServiceCommandLine $script:ServiceExePath $DataDir)
}

function Get-ServiceRegistryValue([string]$name) {
    try {
        return [string](Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name $name -ErrorAction Stop).$name
    } catch { return '' }
}

function New-ServiceRegistration {
    Assert-Exe
    Assert-DataDir

    # New-Service (not "sc.exe create") on purpose: the binary path carries quoted paths, and
    # passing that through a native command's command line is exactly the quoting that breaks on
    # one Windows PowerShell version and not another. New-Service hands the string to
    # CreateService as-is.
    $binPath = Get-ServiceBinPath
    Write-Log "registering $ServiceName -> $binPath"
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $ServiceDisplay `
                -Description $ServiceDesc -StartupType Automatic | Out-Null

    # New-Service registers LocalSystem; LocalService is the rule. Checked and verified below.
    & sc.exe config $ServiceName obj= 'NT AUTHORITY\LocalService' | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the service account returned $LASTEXITCODE" }
    Set-ServiceOptions
}

# Re-points an EXISTING registration at this script's command line and account without deleting
# it: delete + create is the step that can strand a machine with no service while any handle to
# the old one is open. Win32_Service.Change takes the path as a plain string. $false = could not.
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
            StartName     = 'NT AUTHORITY\LocalService'
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

# The options New-Service cannot express. Each exit code is read.
function Set-ServiceOptions {
    # DELAYED automatic start: the service needs a network stack.
    & sc.exe config $ServiceName start= delayed-auto | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the delayed start returned $LASTEXITCODE" }

    # Restart on process failure with a bounded, widening cadence (5 s / 30 s / 120 s).
    # failureflag=1 is the half that is easy to miss: without it Windows only reacts to a service
    # that died WITHOUT reporting a stop, and the host deliberately reports a stop with a
    # non-zero exit code when its main loop ends unexpectedly.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/120000 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the recovery actions returned $LASTEXITCODE" }
    & sc.exe failureflag $ServiceName 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "WARNING: setting the failure flag returned $LASTEXITCODE" }
}

# The account is read back, never assumed. A registration that would run the server as anything
# but LocalService - LocalSystem above all, New-Service's default - is removed rather than started.
function Assert-ServiceAccount {
    $account = Get-ServiceRegistryValue 'ObjectName'
    if (@('NT AUTHORITY\LocalService', 'NT AUTHORITY\LOCAL SERVICE') -contains $account) {
        Write-Log "service account: $account"
        return
    }
    Write-Log "the service account is '$account', not 'NT AUTHORITY\LocalService' - removing the registration so nothing runs with more rights than intended"
    & sc.exe delete $ServiceName | Out-Null
    Fail "could not set the service account to LocalService (it read '$account')" 13
}

# The SCM security descriptor: lets the enrolled user start, stop and query the service without
# UAC (the Share Manager's OpenServiceW path). Built only from validated SIDs; read back after.
function Set-ServiceSecurity {
    $sids = @(Split-Sids $ManageSid)
    foreach ($sid in $sids) {
        if (-not (Test-ManageSid $sid)) { Fail "'$sid' is not a string SID - the service security descriptor was not changed" 2 }
    }
    $sddl = New-ServiceSddl $sids
    Write-Log "setting the service security descriptor for $($sids.Count) enrolled user(s)"
    & sc.exe sdset $ServiceName $sddl | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "could not set the service security descriptor (sc sdset exit $LASTEXITCODE)" 4 }
    # SDDL read back shortens well-known SIDs to aliases, so the check counts the entries rather
    # than matching each SID's text.
    $shown = (@(& sc.exe sdshow $ServiceName) -join '')
    $have = ([regex]::Matches($shown, '\(A;;RPWPLCLO;;;')).Count
    if ($have -ne @($sids | Select-Object -Unique).Count) {
        Fail "the service security descriptor did not take (expected $(@($sids | Select-Object -Unique).Count) start/stop entries, read $have)" 4
    }
}

# Brings the registration to this script's command line, account, start type and recovery
# actions - updating an existing one in place, creating one otherwise - and its SCM descriptor.
function Register-Service {
    Assert-Exe
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
    Set-ServiceSecurity
}

function Start-ServiceAndVerify {
    # A console instance holds the listen port, and the service would then die on its bind.
    Get-Process -Name 'fms-exchange-server' -ErrorAction SilentlyContinue | ForEach-Object {
        $procPath = $null
        try { $procPath = $_.Path } catch { }
        Write-Log ("WARNING: an exchange server process is running outside the service (pid {0}, session {1}, '{2}') - it may hold the listen port" -f $_.Id, $_.SessionId, $procPath)
    }
    Write-Log "starting $ServiceName .."
    Start-Service -Name $ServiceName
    try { (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) } catch { }
    $svc = Get-ServiceOrNull
    if (-not $svc -or $svc.Status -ne 'Running') {
        Fail "the service did not reach Running (state: $(if ($svc) { $svc.Status } else { 'absent' }))" 3
    }
    Write-Log "$ServiceName is running"
}

# install / repair / migrate start the service only when it has a usable password: the server
# never runs password-less, so on a fresh machine the service is registered and left stopped
# until a password is set - which is a normal state, not a failure of the registration.
function Start-ServiceIfConfigured {
    $info = Get-StateInfo $DataDir
    if (-not ($info.passwordSet -and $info.passwordHashValid)) {
        Write-Log "WARNING: $ServiceName is registered but NOT started: no usable server password is set in '$(Join-Path $DataDir $ConfigName)'. Set one, then start the service."
        return
    }
    Start-ServiceAndVerify
}

# --- state directory ----------------------------------------------------------

function Set-MachineDataDirAcl {
    Assert-DataDir
    [void](Assert-TrustedDirectory $DataDir 'the machine state directory')
    # A descriptor built from nothing: the inherited "Users: read" that %ProgramData% hands down
    # must go, and so must ANY other entry. The owner becomes Administrators, which also takes the
    # implicit right to rewrite the DACL away from whoever created it.
    Write-Log "locking down $DataDir"
    Set-DirectorySecurity $DataDir (New-DataDirSecurity)
    $problem = Get-LockdownProblem $DataDir @($SidSystem, $SidAdmins, $SidLocalService)
    if ($problem) { Fail "could not secure '$DataDir': $problem" 4 }
}

# The one way the machine state directory comes into use by an action that writes to it: an
# existing directory is adopted only when an administrator or the system owns it and it is no
# link; a missing one is CREATED with its final ACL. Then the ACL is rebuilt and verified, and only
# then is the log opened there.
function Initialize-MachineDataDir {
    Assert-DataDir
    if (Assert-TrustedDirectory $DataDir 'the machine state directory') {
        Write-Log "state directory '$DataDir' exists and is owned by an administrator or the system"
    } else {
        Write-Log "creating '$DataDir' with its final ACL"
        New-SecureDirectory $DataDir (New-DataDirSecurity)
        if (-not (Assert-TrustedDirectory $DataDir 'the machine state directory')) { Fail "could not create '$DataDir'" 4 }
    }
    Set-MachineDataDirAcl
    Initialize-Log
}

# Seeds exchange.json with `{}` (every member keeps its default, no password) when it is absent:
# the enrolled user's entry needs a file to live on. Written without a BOM - the Go parser
# refuses one. Never overwrites an existing config.
function Initialize-ConfigFile {
    $path = Join-Path $DataDir $ConfigName
    if (Test-ReparsePoint $path) { Remove-LinkOnly $path }
    if ($null -eq (Get-RawAttributes $path)) {
        Write-Log "seeding '$path' with the defaults (no password yet)"
        [System.IO.File]::WriteAllText($path, "{}`n", (New-Object System.Text.UTF8Encoding($false)))
    }
}

# The ACLs of the three files that matter, rebuilt every run so a changed -ManageSid leaves no
# stale entry behind, and verified after.
function Set-StateFileAcls {
    $manage = @(Split-Sids $ManageSid)

    # exchange.json: inherits the directory's entries (LocalService, SYSTEM, Administrators);
    # the enrolled user(s) get Modify on THIS file and nowhere else.
    $config = Join-Path $DataDir $ConfigName
    if (Test-Path -LiteralPath $config -PathType Leaf) {
        if (Test-ReparsePoint $config) { Fail "'$config' is a link - refused" 11 }
        $sec = Get-FileSecurity $config
        $sec.SetAccessRuleProtection($false, $false)
        foreach ($rule in @($sec.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))) {
            [void]$sec.RemoveAccessRuleAll($rule)
        }
        foreach ($sid in $manage) {
            $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                (New-Object System.Security.Principal.SecurityIdentifier($sid)),
                [System.Security.AccessControl.FileSystemRights]::Modify,
                [System.Security.AccessControl.AccessControlType]::Allow)))
        }
        Set-FileSecurity $config $sec
        $problem = Get-ConfigFileProblem $config
        if ($problem) { Fail "could not secure '$config': $problem" 4 }
    }

    $tlsDir = Join-Path $DataDir $TlsDirName
    if (Test-ReparsePoint $tlsDir) { Fail "'$tlsDir' is a link - refused" 11 }

    # key.pem: LocalService + SYSTEM + Administrators ONLY, owner Administrators, no inheritance.
    # The enrolled user is deliberately not among them.
    $key = Join-Path $tlsDir $KeyName
    if (Test-Path -LiteralPath $key -PathType Leaf) {
        if (Test-ReparsePoint $key) { Fail "'$key' is a link - refused" 11 }
        $sec = New-Object System.Security.AccessControl.FileSecurity
        $sec.SetOwner((New-Object System.Security.Principal.SecurityIdentifier($SidAdmins)))
        $sec.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($SidSystem, $SidAdmins)) {
            $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                (New-Object System.Security.Principal.SecurityIdentifier($sid)),
                [System.Security.AccessControl.FileSystemRights]::FullControl,
                [System.Security.AccessControl.AccessControlType]::Allow)))
        }
        $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            (New-Object System.Security.Principal.SecurityIdentifier($SidLocalService)),
            [System.Security.AccessControl.FileSystemRights]::Read,
            [System.Security.AccessControl.AccessControlType]::Allow)))
        Set-FileSecurity $key $sec
        $problem = Get-LockdownProblem $key @($SidSystem, $SidAdmins, $SidLocalService)
        if ($problem) { Fail "could not secure '$key': $problem" 4 }
    }

    # cert.pem is public: the enrolled user may read it (to show the fingerprint) and nothing more.
    $cert = Join-Path $tlsDir $CertName
    if (Test-Path -LiteralPath $cert -PathType Leaf) {
        if (Test-ReparsePoint $cert) { Fail "'$cert' is a link - refused" 11 }
        $sec = Get-FileSecurity $cert
        $sec.SetAccessRuleProtection($false, $false)
        foreach ($rule in @($sec.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))) {
            [void]$sec.RemoveAccessRuleAll($rule)
        }
        foreach ($sid in $manage) {
            $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                (New-Object System.Security.Principal.SecurityIdentifier($sid)),
                [System.Security.AccessControl.FileSystemRights]::Read,
                [System.Security.AccessControl.AccessControlType]::Allow)))
        }
        Set-FileSecurity $cert $sec
    }
}

# The SID of the profile a path lives under, read from the profile list rather than guessed from
# the folder name.
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

# Who may start/stop the service and change exchange.json. It cannot come from the service (its
# identity is LocalService), and the installer cannot supply it either when Setup runs elevated
# as another administrator: every per-user path it expands then names THAT profile. The user
# store being migrated names the right profile; with nothing to migrate the elevated account is
# the honest default.
function Resolve-ManagementSid([string]$explicit, [string]$stateDir) {
    if ($explicit) { return $explicit }
    $sid = Get-ProfileSidForPath $stateDir
    if ($sid) { return $sid }
    return ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
}

# --- identity migration -------------------------------------------------------

# $true when $dir holds a TLS identity of this product.
function Test-HoldsIdentity([string]$dir) {
    return [bool]($dir -and (Test-Path -LiteralPath (Join-Path (Join-Path $dir $TlsDirName) $CertName) -PathType Leaf))
}

# Finds the user's store. -UserDataDir is only a HINT: this script runs elevated, and when the
# elevation went through a different administrator account every per-user path points at THAT
# profile. The hint is used when it really carries an identity, otherwise the user profiles are
# scanned. Several candidates is a deliberate stop rather than a guess: picking the wrong one
# would migrate the wrong identity and unpin every producer of the right one.
function Resolve-UserDataDir([string]$hint, [string]$UsersRoot = '') {
    if ($hint -and (Test-HoldsIdentity $hint)) { return $hint }

    $users = if ($UsersRoot) { $UsersRoot } else { Join-Path $env:SystemDrive 'Users' }
    if (-not (Test-Path -LiteralPath $users)) { return $hint }
    $found = @(Get-ChildItem -LiteralPath $users -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $candidate = Join-Path $_.FullName 'AppData\Local\FastMediaSorterExchange'
        if (Test-HoldsIdentity $candidate) { $candidate }
    })
    if ($found.Count -eq 1) {
        Write-Log "found the user store in '$($found[0])'"
        return $found[0]
    }
    if ($found.Count -gt 1) {
        Fail ("several user profiles hold a Media Exchange Server identity (" + ($found -join '; ') +
              "). Migration stopped - re-run this helper with -UserDataDir naming the one to keep, so the wrong identity is not adopted.") 10
    }
    return $hint
}

# Copies exchange.json and tls\ from the user's store into the machine store - ONCE.
#   -OnlyWhenTargetIsFresh: migration is a one-time move. When the machine store already holds an
#   identity it is the LIVE state the service has been serving from; copying the user's store (a
#   snapshot of whenever the console server last ran) over it would roll back the settings and,
#   worse, could swap the certificate, unpinning every producer. So an existing identity is kept
#   and nothing is copied.
# After a copy the fingerprint is compared with the source's: a difference stops the action
# (exit 7) BEFORE the service is registered, and the copied files are taken back.
function Copy-State([string]$from, [string]$to, [switch]$OnlyWhenTargetIsFresh) {
    if (-not $from -or -not (Test-Path -LiteralPath $from)) {
        Write-Log "no state to migrate from '$from' - starting fresh"
        return
    }
    # This runs elevated between two directories other accounts can write to, so neither end may
    # be a link: reading through one could copy another account's files where the enrolled user
    # can read them, writing through one could land anywhere.
    foreach ($p in @($from, (Join-Path $from $TlsDirName), $to, (Join-Path $to $TlsDirName))) {
        if (Test-ReparsePoint $p) { Fail "'$p' is a junction or a symbolic link - migration refused. Nothing was copied." 11 }
    }
    $source = Get-StateInfo $from
    if (-not $source.certPresent) {
        Write-Log "'$from' holds no certificate yet - nothing to preserve"
    }

    $target = Get-StateInfo $to
    if ($target.certPresent) {
        if ($OnlyWhenTargetIsFresh) {
            if ($source.certPresent -and $target.fingerprint -ne $source.fingerprint) {
                Write-Log "'$to' already holds an identity ($($target.fingerprint)) that differs from the user store's ($($source.fingerprint)) - the machine state wins, nothing migrated"
            } else {
                Write-Log "'$to' already holds this identity - keeping the machine state as it is (nothing migrated)"
            }
            return
        }
        if ($source.certPresent -and $target.fingerprint -ne $source.fingerprint) {
            Fail "'$to' already holds a DIFFERENT certificate ($($target.fingerprint)) than '$from' ($($source.fingerprint)). Migration stopped - remove or back up one of them by hand, otherwise producers would have to re-pin." 7
        }
    }

    Write-Log "migrating state '$from' -> '$to'"
    $configFrom = Join-Path $from $ConfigName
    $configTo   = Join-Path $to $ConfigName
    $previousConfig = $null
    if (Test-Path -LiteralPath $configTo -PathType Leaf) { $previousConfig = [System.IO.File]::ReadAllBytes($configTo) }
    $copied = New-Object System.Collections.Generic.List[string]

    if (Test-Path -LiteralPath $configFrom -PathType Leaf) {
        if (Test-ReparsePoint $configFrom) {
            Write-Log "WARNING: not migrating '$configFrom': it is a link"
        } else {
            if (Test-ReparsePoint $configTo) { Remove-LinkOnly $configTo }
            Copy-Item -LiteralPath $configFrom -Destination $configTo -Force
        }
    }

    $tlsFrom = Join-Path $from $TlsDirName
    $tlsTo   = Join-Path $to $TlsDirName
    if (Test-Path -LiteralPath $tlsFrom -PathType Container) {
        if (-not (Test-Path -LiteralPath $tlsTo)) { New-Item -ItemType Directory -Path $tlsTo | Out-Null }
        Get-ChildItem -LiteralPath $tlsFrom -File | ForEach-Object {
            # A link among the SOURCE files is skipped, one at the destination is removed as a
            # link first - the copy neither reads nor writes anything outside the two stores.
            if ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                Write-Log "WARNING: not migrating '$($_.FullName)': it is a link"
                return
            }
            $dest = Join-Path $tlsTo $_.Name
            if (Test-ReparsePoint $dest) { Remove-LinkOnly $dest }
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
            $copied.Add($dest)
        }
    }

    if ($source.certPresent) {
        $after = Get-StateInfo $to
        if ($after.fingerprint -ne $source.fingerprint) {
            # Back to the state this call found: the target held no identity, so removing what
            # was copied restores it.
            foreach ($file in $copied) { try { Remove-LinkOnly $file } catch { } }
            if ($null -ne $previousConfig) { [System.IO.File]::WriteAllBytes($configTo, $previousConfig) }
            elseif (Test-Path -LiteralPath $configTo) { try { Remove-LinkOnly $configTo } catch { } }
            Fail "the certificate fingerprint changed during the copy ('$($source.fingerprint)' -> '$($after.fingerprint)'). Migration stopped BEFORE the service was registered; the copied files were taken back and producers are untouched." 7
        }
        Write-Log "certificate fingerprint preserved: $($after.fingerprint)"
    }
}

# --- firewall -----------------------------------------------------------------

# Program-scoped (never port-scoped) inbound allow, all three profiles - the same shape as
# enable-exchange-server.ps1 (ticket 143), inlined so one act covers the service and its rule.
# The program is the STAGED copy: a rule naming the application folder's copy would not cover the
# process the service starts. Withdrawal deletes by PROGRAM, never by name.
function Set-FirewallRule([bool]$present) {
    if (-not $present) {
        foreach ($program in @($ExePath, (Get-StagedExePath))) {
            if ($program) { & netsh.exe advfirewall firewall delete rule name=all program="$program" | Out-Null }
        }
        Write-Log 'firewall rule removed'
        return
    }
    & netsh.exe advfirewall firewall delete rule name="$FirewallRule" | Out-Null
    & netsh.exe advfirewall firewall add rule name="$FirewallRule" dir=in action=allow `
        program="$script:ServiceExePath" protocol=TCP profile=domain,private,public enable=yes | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "could not add the firewall rule (netsh exit $LASTEXITCODE)" 8 }
    Write-Log 'firewall rule in place'
}

# --- the state lock ------------------------------------------------------------

# Two helpers on the same state directory would read the same state, copy over each other and
# register twice. Every action but `stop` takes this machine-wide lock FIRST; waiting is bounded,
# and a run that cannot get it changes nothing and says so (exit 16).
$StateLockWaitMs = 10000
$script:StateLock = $null

function Get-StateLockName {
    $normalized = ([System.IO.Path]::GetFullPath($DataDir)).TrimEnd('\').ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized)) | ForEach-Object { $_.ToString('x2') }) -join '' } finally { $sha.Dispose() }
    return 'Global\FastMediaSorterExchangeHelper-' + $hash.Substring(0, 24)
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

# Any terminating error from here on - a locked file under Copy-Item, New-Service, Start-Service -
# would otherwise end the script with exit 1 and NO reason in the log the UI tells the user to send.
trap {
    Write-Log ("FAILED: {0}" -f (($_ | Out-String).Trim()))
    if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }
    exit 1
}

# Elevation first, and nothing written anywhere before it: a non-elevated run must not leave a
# directory behind for the next elevated one to adopt.
Assert-Elevated

# Inputs are checked before anything is touched. A quote in a path would end its quoted form in
# the service command line early; a ManageSid that is not a SID must never reach an ACL or an SDDL.
foreach ($pair in @(@('-ExePath', $ExePath), @('-DataDir', $DataDir), @('-UserDataDir', $UserDataDir))) {
    if ($pair[1] -and ($pair[1].Contains('"') -or -not [System.IO.Path]::IsPathRooted($pair[1]))) {
        Fail "$($pair[0]) must be an absolute path without quotes: '$($pair[1])'" 2
    }
}
foreach ($sid in @(Split-Sids $ManageSid)) {
    if (-not (Test-ManageSid $sid)) { Fail "-ManageSid carries '$sid', which is not a string SID (S-1-..). Nothing was changed." 2 }
}

Write-Log "action=$Action exe='$ExePath' dataDir='$DataDir'"

# An existing state directory is checked BEFORE anything is logged into it, copied into it or read
# from it. `remove` still takes the registration and the firewall rule away from an untrusted one
# - leaving a service that serves out of it would be worse - and `stop` only ever stops;
# everything else refuses.
$script:DataDirDistrust = if ($DataDir) { Get-DirectoryDistrust $DataDir } else { '' }
if ($script:DataDirDistrust -and $Action -ne 'remove' -and $Action -ne 'stop') {
    Fail "the machine state directory refused: $script:DataDirDistrust" 11
}
if (-not $script:DataDirDistrust) { Initialize-Log }

if ($Action -ne 'stop') { Enter-StateLock }

switch ($Action) {

    'install' {
        Assert-Exe
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Initialize-MachineDataDir
        Sync-ServiceBinaries
        # `install` never migrates. A machine that has a user store with an identity and no
        # machine identity yet is migrate-to-service's job: running install first would let the
        # service create a NEW certificate, and the user's would then never be adopted.
        if ((Test-HoldsIdentity $UserDataDir) -and -not (Test-HoldsIdentity $DataDir)) {
            Write-Log "WARNING: '$UserDataDir' holds a certificate and '$DataDir' does not yet. 'install' does not migrate it - run 'migrate-to-service' instead, or producers that pinned that certificate will have to re-pin."
        }
        Initialize-ConfigFile
        Set-StateFileAcls
        Register-Service
        Set-FirewallRule $true
        Start-ServiceIfConfigured
        # The service may have created its certificate on this first run: lock it the same way.
        Set-StateFileAcls
    }

    'repair' {
        # Only when the service exists: a fresh registration is `install`, and an upgrade where
        # the service was never chosen must not acquire one.
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed - nothing to repair (use install to register it)" 9 }
        Assert-Exe
        Assert-DataDir
        $ManageSid = Resolve-ManagementSid $ManageSid $UserDataDir
        Write-Log "management SID(s): $ManageSid"
        Initialize-MachineDataDir
        # Also re-stages the exchange server, which makes this the "the app updated but the
        # service is still running the old copy" repair as well as a registration one.
        Sync-ServiceBinaries
        Initialize-ConfigFile
        Set-StateFileAcls
        Register-Service
        Set-FirewallRule $true
        Start-ServiceIfConfigured
        Set-StateFileAcls
    }

    'remove' {
        # Service, firewall rule and staged binary go; the data directory STAYS - it holds the
        # identity the producers pinned. Deleting it is a separate, explicit choice.
        Remove-ServiceRegistration
        Set-FirewallRule $false
        if ($script:DataDirDistrust) {
            # Nothing inside an untrusted directory is deleted: it could be steered by its owner.
            Fail "the service registration and the firewall rule were removed, but the machine state directory was left untouched: $script:DataDirDistrust" 11
        }
        Remove-ServiceBinaries
        Write-Log "service removed. State kept in '$DataDir' - delete it by hand only if you want producers to re-pin."
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
        # One elevated step for what a user wants after changing something the server only reads
        # at start-up - rather than two UAC prompts with a window in between.
        if (-not (Get-ServiceOrNull)) { Fail "$ServiceName is not installed" 9 }
        Stop-ServiceGracefully
        Start-ServiceAndVerify
    }

    'migrate-to-service' {
        Assert-Exe
        Assert-DataDir
        if (-not $UserDataDir) { Fail 'no -UserDataDir given' 2 }
        $sourceDir = Resolve-UserDataDir $UserDataDir
        # Resolved from the store that actually holds the identity, not from the elevating
        # administrator's profile - see Resolve-ManagementSid.
        $ManageSid = Resolve-ManagementSid $ManageSid $sourceDir
        Write-Log "management SID(s): $ManageSid"
        # The machine directory is verified (or created with its final ACL) BEFORE the identity is
        # copied into it, and the exchange server is staged into its administrators-only bin
        # before anything runs it - Copy-State executes it to inspect both stores.
        Initialize-MachineDataDir
        Sync-ServiceBinaries
        # -OnlyWhenTargetIsFresh: the Server installer may call this on every install, so it runs
        # on ordinary upgrades too. Only the first one is a migration; after that the machine
        # directory is the live state and must win. Aborts with 7 before registration on a mismatch.
        Copy-State $sourceDir $DataDir -OnlyWhenTargetIsFresh
        Initialize-ConfigFile
        Set-StateFileAcls
        Register-Service
        Set-FirewallRule $true
        Start-ServiceIfConfigured
        Set-StateFileAcls
    }
}

Write-Log "action=$Action completed"
# The same rule the trap keeps: when the log never opened, the lines go to the fallback copy
# instead of being discarded, and a SUCCESS is on record just like a failure.
if (-not $script:LogPath) { Write-PendingLog (Get-FallbackLogPath) }
exit 0
