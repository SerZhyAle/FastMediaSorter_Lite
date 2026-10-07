# Test driver for publishing/installer/install-exchange-service.ps1 (ticket 182 L-1): runs the helper's OWN
# functions - not a copy - against real folders, without elevation and without touching any machine state.
#
#   param 1: the helper script.
#
# Everything the elevated helper does to a machine needs the Administrators group as owner and a service
# account; this driver stands the CURRENT USER in for "Administrators" (the one SID a non-elevated process may
# make the owner of what it creates), and Authenticated Users in for "the enrolled user". The helper's code
# paths are the same ones the real run takes: Assert-TrustedDirectory, New-SecureDirectory,
# Set-DirectorySecurity, Set-MachineDataDirAcl, Initialize-LogsDir, Set-LogsAcls, Initialize-Log, Write-Log
# and the verifiers. What it cannot show - the real Administrators group, LocalService writing, the SCM - is
# the elevated hand run C-2 of ticket 182.
#
# Output: one "KEY|value" line per fact. Nothing outside a scratch folder under %TEMP% is touched.
param([string]$Helper)
$ErrorActionPreference = 'Stop'

$ast = [System.Management.Automation.Language.Parser]::ParseFile($Helper, [ref]$null, [ref]$null)
# Every function of the helper and none of its top-level code (which would demand elevation and a service).
foreach ($fd in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $fd.Extent.Text
}

$me = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$SidSystem = 'S-1-5-18'; $SidLocalService = 'S-1-5-19'; $SidAdmins = $me
$SidTrustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
$TrustedOwnerSids = @($SidSystem, $SidAdmins, $SidTrustedInstaller, $SidLocalService)
$script:IsDesktopEdition = ($PSVersionTable.PSEdition -ne 'Core')
$script:PendingLog = New-Object System.Collections.Generic.List[string]
$script:LogPath = $null
$LogsDirName = 'logs'; $LogName = 'install-exchange-service.log'; $ConfigName = 'exchange.json'; $TlsDirName = 'tls'
$ManageSid = 'S-1-5-11'
# The helper's Fail exits the process; here it throws, so the driver can report it.
function Fail([string]$message, [int]$code = 1) { throw ('FAIL(' + $code + '): ' + $message) }

function Out-Fact([string]$key, $value) { Write-Output ($key + '|' + $value) }
function Get-Rules($path) {
    return @((Get-Acl -LiteralPath $path).Access)
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fms-logs-driver-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$DataDir = Join-Path $root 'state'
$logs = Join-Path $DataDir 'logs'
try {
    # --- 1. the first install: the data directory, then logs\, created with their final descriptors ---------
    [void](Assert-TrustedDirectory $DataDir 'the machine state directory')
    New-SecureDirectory $DataDir (New-DataDirSecurity)
    Set-MachineDataDirAcl
    Initialize-LogsDir
    Out-Fact 'LOGSDIR-EXISTS' (Test-Path -LiteralPath $logs -PathType Container)
    Out-Fact 'LOGPATH' $script:LogPath
    Write-Log 'hello from the helper'
    Out-Fact 'HELPERLOG-HAS-LINE' ([bool](Select-String -LiteralPath (Join-Path $logs $LogName) -Pattern 'hello from the helper' -SimpleMatch))
    Out-Fact 'HELPERLOG-NOT-IN-DATADIR' (-not (Test-Path -LiteralPath (Join-Path $DataDir $LogName)))

    # Fact 1: the enrolled user holds nothing on the state directory. Fact 2: read only on logs\.
    Out-Fact 'DATA-PROBLEM' (Get-DataDirProblem $DataDir)
    Out-Fact 'LOGS-PROBLEM' (Get-LogsDirProblem $logs)
    Out-Fact 'DATA-ENROLLED-ENTRIES' @(Get-Rules $DataDir | Where-Object { $_.IdentityReference.Value -match 'Authenticated Users' }).Count
    $enrolled = @(Get-Rules $logs | Where-Object { $_.IdentityReference.Value -match 'Authenticated Users' })
    Out-Fact 'LOGS-ENROLLED-ENTRIES' $enrolled.Count
    Out-Fact 'LOGS-ENROLLED-RIGHTS' $enrolled[0].FileSystemRights
    Out-Fact 'LOGS-PROTECTED' ((Get-Acl -LiteralPath $logs).AreAccessRulesProtected)
    $ls = @(Get-Rules $logs | Where-Object { $_.IdentityReference.Value -match 'LOCAL SERVICE' })
    Out-Fact 'LOGS-LOCALSERVICE' (($ls | ForEach-Object { $_.FileSystemRights.ToString() + '/' + $_.PropagationFlags }) -join ' ; ')

    # --- 2. a file the service made: it inherits exactly the directory's entries ---------------------------
    $file = Join-Path $logs 'exchange.log'
    Set-Content -LiteralPath $file -Value 'a line the service wrote'
    Out-Fact 'FILE-PROBLEM-INITIAL' (Get-LogFileProblem $file)
    $fileEnrolled = @(Get-Rules $file | Where-Object { $_.IdentityReference.Value -match 'Authenticated Users' })
    Out-Fact 'FILE-ENROLLED-RIGHTS' $fileEnrolled[0].FileSystemRights
    Out-Fact 'FILE-ENROLLED-INHERITED' $fileEnrolled[0].IsInherited

    # --- 3. a file with a wrong descriptor is caught, then put right by the next pass ------------------------
    $sec = Get-FileSecurity $file
    $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($ManageSid)), 'Write', 'Allow')))
    Set-FileSecurity $file $sec
    Out-Fact 'FILE-PROBLEM-BROKEN' (Get-LogFileProblem $file)
    Set-LogsAcls
    Out-Fact 'FILE-PROBLEM-FIXED' (Get-LogFileProblem $file)

    # --- 4. logs\ made by something else first (the service, with the data directory's inherited entries) ---
    #        is ADOPTED (owner trusted, no link) and re-secured, and the file in it ends up read only for the user
    Remove-Item -LiteralPath $logs -Recurse -Force
    New-Item -ItemType Directory -Path $logs | Out-Null
    Set-Content -LiteralPath (Join-Path $logs 'service.log') -Value 'service line'
    Out-Fact 'ADOPT-PROBLEM-BEFORE' (Get-LogsDirProblem $logs)
    $script:LogPath = $null
    Initialize-LogsDir
    Out-Fact 'ADOPT-PROBLEM-AFTER' (Get-LogsDirProblem $logs)
    Out-Fact 'ADOPT-FILE-PROBLEM' (Get-LogFileProblem (Join-Path $logs 'service.log'))
    Out-Fact 'ADOPT-LOG-REOPENED' ($script:LogPath -eq (Join-Path $logs $LogName))

    # --- 5. a junction named logs is refused, never followed -------------------------------------------------
    Remove-Item -LiteralPath $logs -Recurse -Force
    $elsewhere = Join-Path $root 'elsewhere'
    New-Item -ItemType Directory -Path $elsewhere | Out-Null
    New-Item -ItemType Junction -Path $logs -Target $elsewhere | Out-Null
    try { Initialize-LogsDir; Out-Fact 'JUNCTION-REFUSED' $false } catch { Out-Fact 'JUNCTION-REFUSED' ($_.Exception.Message -match 'FAIL\(11\)') }
    Out-Fact 'JUNCTION-TARGET-UNTOUCHED' (@(Get-ChildItem -LiteralPath $elsewhere -Force).Count -eq 0)
}
catch {
    Out-Fact 'EXCEPTION' $_.Exception.Message
}
finally {
    # A junction is removed as a link, never through it.
    try { if (Test-ReparsePoint $logs) { [System.IO.Directory]::Delete($logs, $false) } } catch { }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
