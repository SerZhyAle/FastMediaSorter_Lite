<#
.SYNOPSIS
    Collects the state of this machine's Fast Media Sorter hosting - the Share service, the Media Exchange
    Server, the viewer - into ONE ZIP, without needing the Share Manager to run. Read-only.

.DESCRIPTION
    The "Send the log to the author" button lives in the Share Manager. When the Share Manager does not
    start (or the machine is remote and has no mail client), this script is the backstop: it reads, it never
    changes anything, never starts or stops a service, never opens a network connection to anywhere but
    127.0.0.1 (a one-second connect test of the ports our own processes listen on).

    The ZIP holds
      state.txt     OS, hardware, session, the two services, firewall rules, listening sockets, installed
                    files with versions and SHA-256 of our executables, ACLs (icacls text) of the data
                    directories, the registry's value NAMES, FMS_* environment variables, the token's
                    elevation, time sync
      events.txt    Service Control Manager and application-crash events that name our services or exes
      defender.txt  Defender status and detections that name our files
      logs\...      the tail (2 MB) of every log we know, user-profile paths replaced by <USER>

    It NEVER collects: exchange.json (it holds password-hash records), anything under tls\, accounts or device
    stores, tokens, keys, the registry's secret values, file contents other than the logs above. A last scan
    drops any staged file that still looks like a secret. The result is one file you can send by hand.

    Run it from the Start menu entry ("Collect host state") or:
        powershell -NoProfile -ExecutionPolicy Bypass -File collect-host-state.ps1
    More is visible when it runs elevated (the SCM security descriptor, some event channels); it says so.

.PARAMETER OutDir
    Where the ZIP goes. Default: the Desktop of the current user, else %TEMP%.
.PARAMETER Days
    The event-log window. Default 3.
.PARAMETER NoEventLog
    Skip events.txt (slow on a machine with a huge log).
.PARAMETER NoOpen
    Do not select the ZIP in Explorer and do not put it on the clipboard.
.PARAMETER Pause
    Wait for Enter at the end (the Start menu entry uses it so the window stays readable).
.PARAMETER AppDir
    The installation directory (default: the folder this script is in).
.PARAMETER ProgramDataRoot / LocalAppDataRoot / TempRoot
    Test hooks: replace %ProgramData%, %LOCALAPPDATA% and %TEMP%.

.OUTPUTS
    One line "collect-host-state: DONE <path>" (exit 0) or "collect-host-state: FAILED <why>" (exit 1).
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [int]$Days = 3,
    [switch]$NoEventLog,
    [switch]$NoOpen,
    [switch]$Pause,
    [string]$AppDir,
    [string]$ProgramDataRoot,
    [string]$LocalAppDataRoot,
    [string]$TempRoot
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$ShareService    = 'FastMediaSorterCompanionSFTP'
$ExchangeService = 'FastMediaSorterExchangeServer'
$ProcessNames    = @('FastMediaSorter_LITE', 'FastMediaSorter_x86', 'FastMediaSorterCompanion', 'fms-share-worker', 'fms-exchange-server')
$MaxLogBytes     = 2MB

if (-not $AppDir)           { $AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $ProgramDataRoot)  { $ProgramDataRoot = $env:ProgramData }
if (-not $LocalAppDataRoot) { $LocalAppDataRoot = $env:LOCALAPPDATA }
if (-not $TempRoot)         { $TempRoot = [System.IO.Path]::GetTempPath() }

$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$work    = Join-Path $TempRoot ("fms-host-state-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$report  = New-Object System.Text.StringBuilder
$notes   = New-Object System.Collections.Generic.List[string]

function Redact([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    foreach ($p in @($env:USERPROFILE, $LocalAppDataRoot, $env:APPDATA)) {
        if ($p -and $p.Length -gt 8) {
            $root = $p
            if ($p -eq $LocalAppDataRoot -or $p -eq $env:APPDATA) { $root = Split-Path -Parent (Split-Path -Parent $p) }
            if ($root -and $root.Length -gt 8 -and $root -like '*\*') {
                $text = [regex]::Replace($text, [regex]::Escape($root), '<USER>', 'IgnoreCase')
            }
        }
    }
    return $text
}

function Say([string]$line) { [void]$report.AppendLine((Redact $line)) }
function Head([string]$title) { [void]$report.AppendLine(); [void]$report.AppendLine('== ' + $title + ' =='); }

# One section, never fatal: a failure is written where the section would have been, never swallowed.
function Section([string]$title, [scriptblock]$body) {
    Head $title
    try { & $body }
    catch { Say ('(this section failed: ' + $_.Exception.GetType().Name + ': ' + $_.Exception.Message + ')'); $notes.Add($title + ' failed') }
}

function Format-Rows($rows) { ($rows | Format-Table -AutoSize -Wrap | Out-String -Width 220).TrimEnd() }

# ---------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $work | Out-Null

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Say ('Fast Media Sorter - host state, collected ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
Say ('script folder: ' + $AppDir)
Say ('elevated: ' + $isAdmin + $(if (-not $isAdmin) { '   (run elevated to see more: the SCM descriptor, some event channels)' } else { '' }))

Section 'Operating system and hardware' {
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    Say ('OS: ' + $os.Caption + '  version ' + $os.Version + '  build ' + $os.BuildNumber + '  ' + $os.OSArchitecture)
    Say ('product type: ' + $os.ProductType + '  (1 = workstation, 2 = domain controller, 3 = server)')
    Say ('installed: ' + $os.InstallDate + '   last boot: ' + $os.LastBootUpTime)
    Say ('computer: ' + $cs.Name + '  domain/workgroup: ' + $cs.Domain + '  part of domain: ' + $cs.PartOfDomain)
    Say ('RAM: ' + [math]::Round($cs.TotalPhysicalMemory / 1GB, 1) + ' GB   logical CPUs: ' + $cs.NumberOfLogicalProcessors)
    foreach ($d in (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3')) {
        Say ('disk ' + $d.DeviceID + ' free ' + [math]::Round($d.FreeSpace / 1GB, 1) + ' GB of ' + [math]::Round($d.Size / 1GB, 1) + ' GB')
    }
    Say ('time zone: ' + (Get-TimeZone).Id + '   now (UTC): ' + [DateTime]::UtcNow.ToString('u'))
    try { $w = (& w32tm.exe /query /status 2>&1 | Out-String).Trim(); Say 'w32tm /query /status:'; Say ($w -replace '(?m)^', '    ') } catch { Say '(w32tm not available)' }
}

Section 'Session and token' {
    Say ('user: ' + $identity.Name + '   SID: ' + $identity.User.Value)
    Say ('session name: ' + $env:SESSIONNAME + '   RDP: ' + ($env:SESSIONNAME -like 'RDP*'))
    try { $sid = (Get-Process -Id $PID).SessionId; Say ('session id: ' + $sid) } catch { }
    try {
        $g = (& whoami.exe /groups /fo csv 2>$null | ConvertFrom-Csv)
        $integrity = ($g | Where-Object { $_.'Group Name' -like 'Mandatory Label*' } | Select-Object -First 1).'Group Name'
        Say ('integrity: ' + $integrity)
        $adm = $g | Where-Object { $_.SID -eq 'S-1-5-32-544' } | Select-Object -First 1
        if ($adm) { Say ('BUILTIN\Administrators in the token: ' + $adm.Attributes) } else { Say 'BUILTIN\Administrators in the token: no' }
    } catch { Say '(whoami /groups failed)' }
}

Section 'Installed product (Add/Remove Programs)' {
    $keys = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
    $rows = foreach ($k in $keys) {
        Get-ItemProperty -Path $k -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'Fast Media Sorter*' -or $_.DisplayName -like 'FastMediaSorter*' } |
            Select-Object DisplayName, DisplayVersion, InstallLocation, @{ n = 'Key'; e = { $_.PSChildName } }
    }
    if ($rows) { Say (Format-Rows $rows) } else { Say '(no Fast Media Sorter entry found)' }
}

Section 'Windows services' {
    foreach ($name in @($ShareService, $ExchangeService)) {
        $svc = Get-CimInstance Win32_Service -Filter ("Name='" + $name + "'") -ErrorAction SilentlyContinue
        if (-not $svc) { Say ($name + ': not registered'); continue }
        Say ($name + ': state ' + $svc.State + ', start mode ' + $svc.StartMode + ', account ' + $svc.StartName + ', exit code ' + $svc.ExitCode + ', service-specific exit code ' + $svc.ServiceSpecificExitCode + ', process id ' + $svc.ProcessId)
        Say ('    command line: ' + $svc.PathName)
        foreach ($sub in @('qfailure', 'qfailureflag')) {
            try { $o = (& sc.exe $sub $name 2>&1 | Out-String).Trim(); Say ('    sc ' + $sub + ': ' + ($o -replace '\s+', ' ')) } catch { }
        }
    }
}

Section 'Processes' {
    $rows = Get-Process -ErrorAction SilentlyContinue | Where-Object { $ProcessNames -contains $_.ProcessName } |
        Select-Object Id, ProcessName, StartTime, @{ n = 'MB'; e = { [math]::Round($_.WorkingSet64 / 1MB, 1) } }, Path
    if ($rows) { Say (Format-Rows $rows) } else { Say '(none of our processes is running)' }
}

$listeners = @()
Section 'Listening TCP sockets (all) and a local connect test of ours' {
    $procById = @{}
    Get-Process -ErrorAction SilentlyContinue | ForEach-Object { $procById[$_.Id] = $_.ProcessName }
    $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{ Address = $_.LocalAddress; Port = $_.LocalPort; Pid = $_.OwningProcess; Process = $procById[[int]$_.OwningProcess] }
    } | Sort-Object Port, Address)
    Say (Format-Rows $listeners)
    Say ''
    foreach ($l in ($listeners | Where-Object { $ProcessNames -contains $_.Process })) {
        $ok = 'no'
        try {
            $c = New-Object System.Net.Sockets.TcpClient
            $iar = $c.BeginConnect('127.0.0.1', [int]$l.Port, $null, $null)
            if ($iar.AsyncWaitHandle.WaitOne(1000) -and $c.Connected) { $ok = 'yes' }
            $c.Close()
        } catch { }
        Say ('127.0.0.1:' + $l.Port + ' (' + $l.Process + ') accepts a connection: ' + $ok)
    }
}

Section 'Firewall rules that name our programs' {
    $rows = @()
    foreach ($f in (Get-NetFirewallApplicationFilter -ErrorAction Stop)) {
        if ($f.Program -and ($f.Program -like '*FastMediaSorter*' -or $f.Program -like '*fms-share-worker*' -or $f.Program -like '*fms-exchange-server*')) {
            $r = $f | Get-NetFirewallRule -ErrorAction SilentlyContinue
            if ($r) { $rows += [pscustomobject]@{ Rule = $r.DisplayName; Enabled = $r.Enabled; Direction = $r.Direction; Action = $r.Action; Profile = $r.Profile; Program = $f.Program } }
        }
    }
    if ($rows.Count) { Say (Format-Rows $rows) } else { Say '(no rule names one of our programs)' }
    $fw = Get-NetFirewallProfile -ErrorAction SilentlyContinue | Select-Object Name, Enabled, DefaultInboundAction
    if ($fw) { Say ''; Say (Format-Rows $fw) }
}

Section 'Network addresses' {
    Say (Format-Rows (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object InterfaceAlias, IPAddress, PrefixLength))
    $gw = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($gw) { Say ('default route: ' + $gw.NextHop + ' via ' + $gw.InterfaceAlias) }
}

Section 'Installed files (versions; SHA-256 of our executables)' {
    if (-not (Test-Path -LiteralPath $AppDir)) { Say ('(folder not found: ' + $AppDir + ')'); return }
    $dirs = @($AppDir) + @(Get-ChildItem -LiteralPath $AppDir -Directory -ErrorAction SilentlyContinue | Where-Object { @('companion', 'exchange', 'viewer', 'filedo') -contains $_.Name } | ForEach-Object { $_.FullName })
    foreach ($d in $dirs) {
        Say ('-- ' + $d)
        $rows = Get-ChildItem -LiteralPath $d -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in @('.exe', '.ps1', '.sha256', '.txt', '.log') -or $_.Name -like '*.dll' -and $d -eq $AppDir -and $_.Name -like 'FastMedia*' } |
            ForEach-Object {
                $hash = ''
                if ($_.Extension -eq '.exe') { try { $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } catch { $hash = '(unreadable)' } }
                [pscustomobject]@{ Name = $_.Name; Bytes = $_.Length; Modified = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm'); FileVersion = $_.VersionInfo.FileVersion; SHA256 = $hash }
            }
        if ($rows) { Say (Format-Rows $rows) } else { Say '(no matching files)' }
    }
}

$dataDirs = @(
    (Join-Path $ProgramDataRoot 'FastMediaSorterExchange'),
    (Join-Path $ProgramDataRoot 'FastMediaSorterCompanion'),
    (Join-Path $LocalAppDataRoot 'FastMediaSorterExchange'),
    (Join-Path $LocalAppDataRoot 'FastMediaSorterCompanion')
)
if ($env:FMS_EXCHANGE_DATA_DIR) { $dataDirs = @($env:FMS_EXCHANGE_DATA_DIR) + $dataDirs }

Section 'Data directories: listing (names, sizes, times - never contents) and ACLs' {
    foreach ($d in $dataDirs) {
        if (-not (Test-Path -LiteralPath $d)) { Say ($d + ': does not exist'); continue }
        Say ('-- ' + $d)
        try {
            $rows = Get-ChildItem -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue | Select-Object -First 80 |
                ForEach-Object { [pscustomobject]@{ Path = $_.FullName.Substring($d.Length).TrimStart('\'); Bytes = $(if ($_.PSIsContainer) { '<dir>' } else { $_.Length }); Modified = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm') } }
            if ($rows) { Say (Format-Rows $rows) } else { Say '(empty or not listable by this user)' }
        } catch { Say ('(not listable: ' + $_.Exception.Message + ')') }
        foreach ($sub in @($d, (Join-Path $d 'logs'), (Join-Path $d 'tls'))) {
            if (Test-Path -LiteralPath $sub) {
                try { $acl = (& icacls.exe $sub 2>&1 | Out-String).Trim(); Say ('ACL of ' + $sub + ':'); Say ($acl -replace '(?m)^', '    ') } catch { }
            }
        }
    }
}

Section 'Registry: value names under HKCU\Software\SZA\FastMediaSorter (no values except a short list)' {
    $key = 'HKCU:\Software\SZA\FastMediaSorter'
    if (-not (Test-Path $key)) { Say '(key not found)'; return }
    $show = @('UiLanguage', 'Share_ListenPort', 'Share_ServerFeaturesEnabled')
    $p = Get-ItemProperty -Path $key
    $names = $p.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { $_.Name }
    Say ('values: ' + $names.Count)
    foreach ($n in ($names | Sort-Object)) {
        if ($show -contains $n) { Say ('    ' + $n + ' = ' + $p.$n) } else { Say ('    ' + $n) }
    }
}

Section 'Environment: FMS_* variables' {
    $any = $false
    foreach ($scope in @('Process', 'User', 'Machine')) {
        $vars = [Environment]::GetEnvironmentVariables($scope)
        foreach ($k in ($vars.Keys | Where-Object { $_ -like 'FMS_*' } | Sort-Object)) { Say ($scope + ': ' + $k + ' = ' + $vars[$k]); $any = $true }
    }
    if (-not $any) { Say '(none set)' }
}

Section 'Mail handler (for the "Send the log" button)' {
    $found = @()
    foreach ($k in @('HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\mailto\UserChoice', 'Registry::HKEY_CLASSES_ROOT\mailto\shell\open\command', 'HKLM:\SOFTWARE\Clients\Mail')) {
        if (Test-Path $k) { $found += $k }
    }
    if ($found.Count) { Say ('registered: ' + ($found -join '; ')) } else { Say 'no mailto handler and no mail client is registered' }
}

# ---------------------------------------------------------------------------------------------------------
# Events
if (-not $NoEventLog) {
    $ev = New-Object System.Text.StringBuilder
    try {
        $since = (Get-Date).AddDays(-[math]::Abs($Days))
        $names = @($ShareService, $ExchangeService, 'FastMediaSorter')
        $lines = @()
        try {
            $lines += Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; StartTime = $since } -ErrorAction Stop |
                Where-Object { $m = $_.Message; ($names | Where-Object { $m -like ('*' + $_ + '*') }) } | Select-Object -First 120
        } catch { [void]$ev.AppendLine('(System/Service Control Manager: ' + $_.Exception.Message + ')') }
        try {
            $lines += Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 1, 2; StartTime = $since } -ErrorAction Stop |
                Where-Object { $m = $_.Message; ($ProcessNames | Where-Object { $m -like ('*' + $_ + '*') }) } | Select-Object -First 120
        } catch { [void]$ev.AppendLine('(Application errors: ' + $_.Exception.Message + ')') }
        foreach ($e in ($lines | Sort-Object TimeCreated)) {
            $msg = ($e.Message -replace '\s+', ' ')
            if ($msg.Length -gt 400) { $msg = $msg.Substring(0, 400) }
            [void]$ev.AppendLine(('{0:yyyy-MM-dd HH:mm:ss}  {1,5}  {2}  {3}  {4}' -f $e.TimeCreated, $e.Id, $e.LevelDisplayName, $e.ProviderName, $msg))
        }
        if (-not $lines.Count) { [void]$ev.AppendLine('(no event names our services or programs in the last ' + $Days + ' days)') }
    } catch { [void]$ev.AppendLine('events failed: ' + $_.Exception.Message) }
    [IO.File]::WriteAllText((Join-Path $work 'events.txt'), (Redact $ev.ToString()), (New-Object Text.UTF8Encoding($false)))
}

# Defender
$df = New-Object System.Text.StringBuilder
try {
    $st = Get-MpComputerStatus -ErrorAction Stop
    [void]$df.AppendLine(('RealTimeProtectionEnabled: ' + $st.RealTimeProtectionEnabled + '   AMServiceEnabled: ' + $st.AMServiceEnabled + '   signatures: ' + $st.AntivirusSignatureVersion + ' (' + $st.AntivirusSignatureLastUpdated + ')'))
    $det = Get-MpThreatDetection -ErrorAction SilentlyContinue | Where-Object { ($_.Resources -join ' ') -match 'FastMediaSorter|fms-share-worker|fms-exchange-server' }
    if ($det) { foreach ($d in $det) { [void]$df.AppendLine(('{0}  threat id {1}  {2}' -f $d.InitialDetectionTime, $d.ThreatID, ($d.Resources -join '; '))) } }
    else { [void]$df.AppendLine('no detection names our files') }
} catch { [void]$df.AppendLine('(Defender status not available: ' + $_.Exception.Message + ')') }
[IO.File]::WriteAllText((Join-Path $work 'defender.txt'), (Redact $df.ToString()), (New-Object Text.UTF8Encoding($false)))

# ---------------------------------------------------------------------------------------------------------
# Logs
$logIndex = New-Object System.Collections.Generic.List[string]

function Copy-LogTail([string]$source, [string]$entry) {
    try {
        $fs = New-Object System.IO.FileStream($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $len = $fs.Length
            $truncated = $len -gt $MaxLogBytes
            if ($truncated) { [void]$fs.Seek($len - $MaxLogBytes, [IO.SeekOrigin]::Begin) }
            $buf = New-Object byte[] ([int][math]::Min($len, $MaxLogBytes))
            $read = 0
            while ($read -lt $buf.Length) { $n = $fs.Read($buf, $read, $buf.Length - $read); if ($n -le 0) { break }; $read += $n }
        } finally { $fs.Dispose() }
        $text = [Text.Encoding]::UTF8.GetString($buf, 0, $read)
        if ($truncated) { $nl = $text.IndexOf("`n"); if ($nl -ge 0) { $text = $text.Substring($nl + 1) } }
        $text = Redact $text
        if ($truncated) { $text = '[.. truncated: original ' + $len + ' bytes, keeping the last ' + $MaxLogBytes + ' ..]' + "`n" + $text }
        $dest = Join-Path $work ('logs\' + $entry)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        [IO.File]::WriteAllText($dest, $text, (New-Object Text.UTF8Encoding($false)))
        $logIndex.Add($entry + '  (' + $read + ' bytes read)')
    } catch {
        $logIndex.Add($entry + '  NOT READ: ' + $_.Exception.Message + '   [' + $source + ']')
        $notes.Add('unreadable log: ' + $entry)
    }
}

function Collect-Logs([string]$dir, [string]$prefix, [string[]]$patterns, [int]$Newest = 0) {
    if (-not (Test-Path -LiteralPath $dir)) { return }
    foreach ($pat in $patterns) {
        $files = @(Get-ChildItem -LiteralPath $dir -File -Filter $pat -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        # Setup writes one log per run into %TEMP%; the last two are the ones that matter.
        if ($Newest -gt 0 -and $files.Count -gt $Newest) { $files = $files | Select-Object -First $Newest }
        foreach ($f in $files) { Copy-LogTail $f.FullName ($prefix + '/' + $f.Name) }
    }
}

$logPatterns = @('*.log', '*.log.1', '*.log.old')
Collect-Logs (Join-Path $ProgramDataRoot 'FastMediaSorterExchange\logs') 'exchange-machine' $logPatterns
Collect-Logs (Join-Path $ProgramDataRoot 'FastMediaSorterExchange') 'exchange-machine-root' $logPatterns
Collect-Logs (Join-Path $LocalAppDataRoot 'FastMediaSorterExchange\logs') 'exchange-user' $logPatterns
Collect-Logs (Join-Path $LocalAppDataRoot 'FastMediaSorterExchange') 'exchange-user-root' $logPatterns
if ($env:FMS_EXCHANGE_DATA_DIR) {
    Collect-Logs (Join-Path $env:FMS_EXCHANGE_DATA_DIR 'logs') 'exchange-override' $logPatterns
    Collect-Logs $env:FMS_EXCHANGE_DATA_DIR 'exchange-override-root' $logPatterns
}
Collect-Logs (Join-Path $ProgramDataRoot 'FastMediaSorterCompanion') 'share-machine' $logPatterns
Collect-Logs (Join-Path $ProgramDataRoot 'FastMediaSorterCompanion\logs') 'share-machine-logs' $logPatterns
Collect-Logs (Join-Path $LocalAppDataRoot 'FastMediaSorterCompanion') 'share-user' $logPatterns
Collect-Logs $AppDir 'app' $logPatterns
Collect-Logs (Join-Path $LocalAppDataRoot 'SZA\FastMediaSorter\logs') 'viewer-user' $logPatterns
Collect-Logs $TempRoot 'temp' @('install-*-service.log', 'enable-*-server.log', 'fms-setup*.log', 'Setup Log *.txt') 2
Collect-Logs (Join-Path $env:SystemRoot 'Temp') 'system-temp' @('install-*-service.log', 'enable-*-server.log', 'fms-setup*.log', 'Setup Log *.txt') 2
Collect-Logs 'C:\fms-check' 'fms-check' @('*.log', '*.txt') 4

Head 'Logs collected'
if ($logIndex.Count) { foreach ($l in $logIndex) { Say ('    ' + $l) } } else { Say '(no log of ours was found in any known place)' }
if ($notes.Count) { Head 'Notes'; foreach ($n in $notes) { Say ('    ' + $n) } }

[IO.File]::WriteAllText((Join-Path $work 'state.txt'), $report.ToString(), (New-Object Text.UTF8Encoding($false)))

# ---------------------------------------------------------------------------------------------------------
# Last line of defence: nothing that looks like a secret leaves in the ZIP.
$secretMarkers = @('BEGIN PRIVATE KEY', 'BEGIN EC PRIVATE KEY', 'BEGIN RSA PRIVATE KEY', 'BEGIN OPENSSH PRIVATE KEY', '"passwordHash"', '"tokenHash"', '$argon2id$')
foreach ($f in (Get-ChildItem -LiteralPath $work -Recurse -File)) {
    $head = ''
    try { $head = [IO.File]::ReadAllText($f.FullName) } catch { }
    foreach ($m in $secretMarkers) {
        if ($head.Contains($m)) {
            Remove-Item -LiteralPath $f.FullName -Force
            Add-Content -LiteralPath (Join-Path $work 'state.txt') -Value ("`n[dropped from the archive: " + $f.FullName.Substring($work.Length).TrimStart('\') + " - it matched a secret marker]")
            break
        }
    }
}

# ---------------------------------------------------------------------------------------------------------
# The ZIP, with forward-slash entry names whatever the PowerShell edition.
$result = 0
$zipPath = ''
try {
    if (-not $OutDir) {
        $desk = [Environment]::GetFolderPath('Desktop')
        $OutDir = if ($desk -and (Test-Path -LiteralPath $desk)) { $desk } else { $TempRoot }
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $zipPath = Join-Path $OutDir ('fms-host-state-' + $env:COMPUTERNAME + '-' + $stamp + '.zip')
    Add-Type -AssemblyName System.IO.Compression
    $zs = New-Object IO.FileStream($zipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $za = New-Object IO.Compression.ZipArchive($zs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in (Get-ChildItem -LiteralPath $work -Recurse -File)) {
                $rel = $f.FullName.Substring($work.Length).TrimStart('\').Replace('\', '/')
                $entry = $za.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
                $es = $entry.Open()
                try { $src = [IO.File]::OpenRead($f.FullName); try { $src.CopyTo($es) } finally { $src.Dispose() } } finally { $es.Dispose() }
            }
        } finally { $za.Dispose() }
    } finally { $zs.Dispose() }
} catch {
    $result = 1
    Write-Host ('collect-host-state: FAILED ' + $_.Exception.Message)
} finally {
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

if ($result -eq 0) {
    $size = (Get-Item -LiteralPath $zipPath).Length
    Write-Host ('collect-host-state: DONE ' + $zipPath + ' (' + [math]::Round($size / 1KB) + ' KB)')
    Write-Host 'Send this file by hand. It holds logs, versions, ports, service states and ACL listings - no keys, no passwords, no file contents beyond the logs.'
    if (-not $NoOpen) {
        try { Set-Clipboard -Path $zipPath } catch { }
        try { Start-Process explorer.exe -ArgumentList ('/select,"' + $zipPath + '"') } catch { }
    }
}
if ($Pause) { [void](Read-Host 'Press Enter to close') }
exit $result
