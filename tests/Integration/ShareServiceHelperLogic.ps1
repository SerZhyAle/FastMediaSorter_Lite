<#
.SYNOPSIS
    Deterministic, NON-elevated checks of the logic inside
    publishing/installer/install-share-service.ps1 - ticket 102 (I02): the grant ledger
    (shape, publication, verification), the grant transaction, the state lock and the
    pending log.

.DESCRIPTION
    The helper's own functions are loaded out of the shipped script through the PowerShell AST
    (no copy, no re-implementation), then run against a throw-away directory with a fake
    icacls.exe and a Fail that throws instead of exiting. What this CANNOT prove, and what
    stays an elevated VM acceptance (tests/Integration/ShareServiceHelper.ps1): the real
    Administrators ownership of the ledger, real ACEs on real folders, a crash between two
    steps, the installer. Deviations forced by running unelevated, each stated in the output:
      * SYSTEM and Administrators are both aimed at the current user, because setting
        Administrators as owner needs SeRestorePrivilege;
      * icacls.exe is a function that records what it was asked and fails on demand.

    Exit codes (CHECK-VERDICT): 0 every check passed; 1 a check failed; 2 could not verify
    (helper missing, or its functions could not be loaded).

.EXAMPLE
    pwsh -NoProfile -File .\tests\Integration\ShareServiceHelperLogic.ps1
    powershell -NoProfile -File .\tests\Integration\ShareServiceHelperLogic.ps1   # Windows PowerShell 5.1 too
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$helper = Join-Path $repo 'publishing\installer\install-share-service.ps1'
if (-not (Test-Path -LiteralPath $helper)) { Exit-Verdict 'ShareServiceHelperLogic' 2 "helper not found ($helper)" }

Write-Subject 'ShareServiceHelperLogic' "install-share-service.ps1 functions via AST, $(Get-TreeLabel $repo), PowerShell $($PSVersionTable.PSVersion)"

# ---------------------------------------------------------------- load the helper's functions ----
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($helper, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) { Exit-Verdict 'ShareServiceHelperLogic' 1 "the helper does not parse: $($parseErrors[0].Message)" }

$wanted = @(
    'Get-FallbackLogPath', 'Write-PendingLog', 'Get-RawAttributes', 'Test-ReparsePoint', 'Remove-LinkOnly',
    'Get-SidDisplay', 'ConvertFrom-JsonList', 'Get-Ledger', 'Write-LockedFile', 'Save-Ledger',
    'Get-LedgerLockProblem', 'Get-ApprovedLedger', 'Undo-AppliedGrants', 'Write-GrantRecovery',
    'Get-StateLockName', 'Enter-StateLock', 'Grant-Roots', 'Revoke-Roots', 'Write-Log',
    'Write-GrantIntent', 'Remove-GrantIntents', 'Resolve-StaleGrantIntents',
    'Get-StagedExePath', 'Get-FileDoRoot', 'Get-ServiceBinPath', 'Get-WantedServiceAccount', 'Move-UntrustedWorkerStateAside')
$defs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $wanted -contains $n.Name }, $true)
$missing = @($wanted | Where-Object { $name = $_; -not ($defs | Where-Object { $_.Name -eq $name }) })
if ($missing.Count -gt 0) { Exit-Verdict 'ShareServiceHelperLogic' 2 "not found in the helper: $($missing -join ', ')" }
foreach ($def in $defs) { Invoke-Expression $def.Extent.Text }
$realWriteLogText = ($defs | Where-Object { $_.Name -eq 'Write-Log' }).Extent.Text

# ------------------------------------------------------------------------------- fixtures ----
$me = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$SidSystem = $me          # deviation 1: both well-known SIDs aimed at the current user
$SidAdmins = $me
$SidLocalService = 'S-1-5-19'
$LedgerName = 'granted-roots.json'
$script:IsDesktopEdition = ($PSVersionTable.PSEdition -ne 'Core')
$StateLockWaitMs = 600
$script:PendingLog = New-Object System.Collections.Generic.List[string]
$script:LogLines = New-Object System.Collections.Generic.List[string]
$script:IcaclsCalls = New-Object System.Collections.Generic.List[string]
$script:IcaclsFail = { param($callText) $false }
$script:GrantShortfall = 0

$stubWriteLogText = 'function Write-Log([string]$message) { $script:LogLines.Add($message) }'
Invoke-Expression $stubWriteLogText
function Fail([string]$message, [int]$code = 1) { throw ("FAIL:{0}:{1}" -f $code, $message) }
function Assert-DataDir { }
function Get-GrantRefusal([string]$folder) { return '' }
function Get-SystemPathRefusal([string]$folder) { return '' }
function Test-LocalServiceAce([string]$folder) { return $false }
function Get-InheritanceBreaks([string]$root) { return @() }
# deviation 2: a recording icacls that fails on demand
function icacls.exe {
    $callText = ($args -join ' ')
    $script:IcaclsCalls.Add($callText)
    if (& $script:IcaclsFail $callText) { $global:LASTEXITCODE = 5 } else { $global:LASTEXITCODE = 0 }
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Check = $name; Passed = $ok })
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Throws([scriptblock]$block) { try { & $block | Out-Null; return $null } catch { return $_.Exception.Message } }
function New-Sandbox {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('fms-helper-logic-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $script:DataDir = $dir
    $script:LogLines.Clear(); $script:IcaclsCalls.Clear(); $script:IcaclsFail = { param($c) $false }
    return $dir
}
function Remove-Sandbox([string]$dir) { try { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue } catch { } }
function Ledger-Path { Join-Path $script:DataDir $LedgerName }
function Write-Raw([string]$text) { [IO.File]::WriteAllText((Ledger-Path), $text, (New-Object Text.UTF8Encoding($false))) }
function Entry([string]$path, [string]$rights = 'RX') { [pscustomobject]@{ path = $path; sid = $SidLocalService; rights = $rights; preExisting = $false } }

# --------------------------------------------------------------------- 1. ledger reading ----
$dir = New-Sandbox
try {
    Check 'ledger: no file is "no ledger", not an error' ((@(Get-Ledger)).Count -eq 0)
    Write-Raw '[]'
    Check 'ledger: a valid empty array is an empty ledger' ($null -eq (Throws { Get-Ledger }) -and (@(Get-Ledger)).Count -eq 0)
    Write-Raw ''
    Check 'ledger: an empty FILE is corrupt' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '{ not json'
    Check 'ledger: malformed JSON is corrupt' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '[{"sid":"S-1-5-19","rights":"RX"}]'
    Check 'ledger: an entry without a path is corrupt, not partial history' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '{"path":"C:\\a","rights":"RX"}'
    Check 'ledger: a lone JSON object is not an array of history' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '[{"path":"C:\\a","rights":"ZZ"}]'
    Check 'ledger: an unknown rights level is corrupt' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '[{"path":"C:\\a","preExisting":"yes"}]'
    Check 'ledger: a non-boolean preExisting is corrupt' ($null -ne (Throws { Get-Ledger }))
    Write-Raw '[{"path":"C:\\a","rights":"RX","preExisting":false},{"path":"D:\\b","rights":"M","preExisting":true}]'
    $two = @(Get-Ledger)
    Check 'ledger: a valid multi-entry ledger parses with every entry' ($two.Count -eq 2 -and $two[1].path -eq 'D:\b')
} finally { Remove-Sandbox $dir }

# ------------------------------------------------------------------- 2. ledger publication ----
$dir = New-Sandbox
try {
    Save-Ledger @((Entry 'C:\one'), (Entry 'C:\two' 'M'))
    $leftovers = @(Get-ChildItem -LiteralPath $dir -Force | Where-Object { $_.Name -ne $LedgerName })
    Check 'save: a first save publishes and leaves no temp or backup' ($leftovers.Count -eq 0 -and (@(Get-Ledger)).Count -eq 2)

    Save-Ledger @((Entry 'C:\three'))
    $leftovers = @(Get-ChildItem -LiteralPath $dir -Force | Where-Object { $_.Name -ne $LedgerName })
    $after = @(Get-Ledger)
    Check 'save: a replacing save drops ONLY its own backup and reads back' ($leftovers.Count -eq 0 -and $after.Count -eq 1 -and $after[0].path -eq 'C:\three')
    Check 'save: the published ledger carries the locked form' ((Get-LedgerLockProblem (Ledger-Path)) -eq '')

    # A stranger's files next to the ledger are never touched.
    [IO.File]::WriteAllText((Join-Path $dir 'granted-roots.json.tmp'), 'someone else')
    [IO.File]::WriteAllText((Join-Path $dir 'granted-roots.json.bak'), 'someone else')
    Save-Ledger @((Entry 'C:\four'))
    Check 'save: files with the OLD fixed names that this call did not create are left alone' (
        (Get-Content -LiteralPath (Join-Path $dir 'granted-roots.json.tmp') -Raw) -eq 'someone else' -and
        (Get-Content -LiteralPath (Join-Path $dir 'granted-roots.json.bak') -Raw) -eq 'someone else')
} finally { Remove-Sandbox $dir }

$dir = New-Sandbox
try {
    Save-Ledger @((Entry 'C:\good'))
    $before = [IO.File]::ReadAllText((Ledger-Path))
    # Verification fails on the first look (a published file with the wrong guard).
    function Get-LedgerLockProblem([string]$path) { return 'injected: wrong owner' }
    $err = Throws { Save-Ledger @((Entry 'C:\bad')) }
    Check 'save: a published ledger that fails verification makes the save throw' ($null -ne $err -and $err -like '*failed verification*')
    # (re-load the real function for the checks below)
    foreach ($def in ($defs | Where-Object { $_.Name -eq 'Get-LedgerLockProblem' })) { Invoke-Expression $def.Extent.Text }
    Check 'save: the previous ledger is put back, byte for byte' ([IO.File]::ReadAllText((Ledger-Path)) -eq $before)
    $names = @(Get-ChildItem -LiteralPath $dir -Force | ForEach-Object { $_.Name })
    Check 'save: the rejected ledger is kept as evidence and no temp is left' (
        @($names | Where-Object { $_ -like '*.rejected-*' }).Count -eq 1 -and @($names | Where-Object { $_ -like '*.tmp-*' }).Count -eq 0)
} finally { Remove-Sandbox $dir }

$dir = New-Sandbox
try {
    # A directory standing at the ledger path: the swap cannot happen - the old content must survive.
    New-Item -ItemType Directory -Path (Ledger-Path) | Out-Null
    $err = Throws { Save-Ledger @((Entry 'C:\x')) }
    $names = @(Get-ChildItem -LiteralPath $dir -Force | ForEach-Object { $_.Name })
    Check 'save: a swap that cannot happen throws and removes only its own temp' ($null -ne $err -and @($names | Where-Object { $_ -like '*.tmp-*' }).Count -eq 0 -and (Test-Path -LiteralPath (Ledger-Path) -PathType Container))
} finally { Remove-Sandbox $dir }

# ------------------------------------------------------------ 3. the grant transaction (I02.6) ----
$dir = New-Sandbox
$grantFolderA = Join-Path $dir 'shareA'; $grantFolderB = Join-Path $dir 'shareB'
New-Item -ItemType Directory -Path $grantFolderA, $grantFolderB | Out-Null
try {
    # Save-Ledger cannot publish (injected: the disk is full): the ACEs this run added are taken back.
    function Save-Ledger($entries) { throw 'injected: disk full' }
    $err = Throws { Grant-Roots @($grantFolderA, $grantFolderB) @() $true }
    $removes = @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' })
    Check 'grant: a ledger that cannot be written takes the grants back and exits 17' ($err -like 'FAIL:17:*' -and $err -like '*taken back*' -and $removes.Count -eq 2)
    Check 'grant: only the folders this run touched are reverted' (($removes -join "`n") -like "*shareA*" -and ($removes -join "`n") -like "*shareB*" -and $script:IcaclsCalls.Count -eq 4)

    # ...and when taking back fails too: protected recovery evidence names them, exit 17.
    $script:LogLines.Clear(); $script:IcaclsCalls.Clear()
    $script:IcaclsFail = { param($c) $c -like '*remove:g*' }
    $err = Throws { Grant-Roots @($grantFolderA) @() $true }
    $evidence = @(Get-ChildItem -LiteralPath $dir -Filter 'granted-roots.recovery-*.json' -Force)
    Check 'grant: grants that cannot be taken back are named in a recovery file, exit 17' (
        $err -like 'FAIL:17:*' -and $evidence.Count -eq 1 -and ((Get-Content -LiteralPath $evidence[0].FullName -Raw) -like "*shareA*"))
    Check 'grant: the recovery file has the locked form' ((Get-LedgerLockProblem $evidence[0].FullName) -eq '')
    foreach ($def in ($defs | Where-Object { $_.Name -eq 'Save-Ledger' })) { Invoke-Expression $def.Extent.Text }
} finally { Remove-Sandbox $dir }

$dir = New-Sandbox
$grantFolderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $grantFolderA | Out-Null
try {
    Save-Ledger @((Entry $grantFolderA 'RX'))
    $script:IcaclsCalls.Clear()
    # A level change RX -> M, then a ledger that cannot be replaced: the old level is put back, not removed.
    function Save-Ledger($entries) { throw 'injected: disk full' }
    $err = Throws { Grant-Roots @($grantFolderA) @() $true }
    $restores = @($script:IcaclsCalls | Where-Object { $_ -like '*/grant:r*(RX)*' })
    Check 'grant: a level that was changed is restored to what it was' ($err -like 'FAIL:17:*' -and $restores.Count -eq 1 -and @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' }).Count -eq 0)
    foreach ($def in ($defs | Where-Object { $_.Name -eq 'Save-Ledger' })) { Invoke-Expression $def.Extent.Text }

    # An unreadable ledger grants nothing at all (exit 15) - before any ACE is touched.
    $script:IcaclsCalls.Clear()
    Write-Raw '{ broken'
    $err = Throws { Grant-Roots @($grantFolderA) @() $true }
    Check 'grant: an unreadable ledger grants nothing and exits 15' ($err -like 'FAIL:15:*' -and $script:IcaclsCalls.Count -eq 0)
    Check 'grant: ...and the broken ledger is byte-for-byte what it was' ([IO.File]::ReadAllText((Ledger-Path)) -eq '{ broken')
} finally { Remove-Sandbox $dir }

# ------------------------------------------------------------------------ 4. the state lock ----
$dir = New-Sandbox
try {
    $name = Get-StateLockName
    $held = New-Object System.Threading.ManualResetEventSlim($false)
    $release = New-Object System.Threading.ManualResetEventSlim($false)
    $rs = [runspacefactory]::CreateRunspace(); $rs.Open()
    $holder = [powershell]::Create(); $holder.Runspace = $rs
    [void]$holder.AddScript({
        param($lockName, $held, $release)
        $m = New-Object System.Threading.Mutex($false, $lockName)
        [void]$m.WaitOne()
        $held.Set()
        [void]$release.Wait(15000)
        $m.ReleaseMutex(); $m.Dispose()
    }).AddArgument($name).AddArgument($held).AddArgument($release)
    $handle = $holder.BeginInvoke()
    [void]$held.Wait(10000)

    $err = Throws { Enter-StateLock }
    Check 'lock: a second run waits a bounded time, changes nothing and exits 16' ($err -like 'FAIL:16:*' -and $err -like '*Nothing was changed*')
    Check 'lock: the same directory always maps to the same lock name, a different one to another' (
        (Get-StateLockName) -eq $name -and $(try { $saved = $script:DataDir; $script:DataDir = Join-Path $dir 'other'; (Get-StateLockName) -ne $name } finally { $script:DataDir = $saved }))

    $release.Set(); [void]$holder.EndInvoke($handle); $holder.Dispose(); $rs.Dispose()
    $err = Throws { Enter-StateLock }
    Check 'lock: once the other run has finished it is taken' ($null -eq $err -and $null -ne $script:StateLock)
    if ($script:StateLock) { try { $script:StateLock.ReleaseMutex() } catch { }; $script:StateLock.Dispose(); $script:StateLock = $null }
} finally { Remove-Sandbox $dir }

# ------------------------------------------------------------------------- 5. the pending log ----
$dir = New-Sandbox
try {
    $script:PendingLog.Clear(); $script:PendingLog.Add('line one'); $script:PendingLog.Add('line two')
    $unwritable = Join-Path $dir 'no-such-folder\log.txt'
    $warn = $null
    Write-PendingLog $unwritable 3>&1 | ForEach-Object { if ($_ -is [System.Management.Automation.WarningRecord]) { $warn = $_.Message } }
    Check 'log: a failed append keeps the lines and says so out loud' ($script:PendingLog.Count -eq 2 -and $warn -like '*could not be written*')

    $good = Join-Path $dir 'log.txt'
    Write-PendingLog $good
    Check 'log: the next place that can take them gets them, and only then are they cleared' (
        $script:PendingLog.Count -eq 0 -and ((Get-Content -LiteralPath $good -Raw) -like '*line one*line two*'))
} finally { Remove-Sandbox $dir }

# ----------------------------------------------------------- 6. remove: revoke is never "complete" by silence ----
$dir = New-Sandbox
$revokeFolder = Join-Path $dir 'shareR'
New-Item -ItemType Directory -Path $revokeFolder | Out-Null
try {
    # An unreadable ledger: kept byte for byte, nothing revoked, and the incomplete state is flagged.
    $script:RevokeIncomplete = $false
    Write-Raw '{ broken'
    [void](Throws { Revoke-Roots })
    Check 'revoke: an unreadable ledger is kept byte for byte, nothing is revoked' ([IO.File]::ReadAllText((Ledger-Path)) -eq '{ broken' -and $script:IcaclsCalls.Count -eq 0)
    Check 'revoke: ...and the incomplete revoke is flagged and logged' ($script:RevokeIncomplete -and (($script:LogLines -join "`n") -like '*NO recorded grant is revoked*'))

    # A clean ledger and a clean icacls: the grant is revoked, the ledger goes, no flag.
    $script:RevokeIncomplete = $false; $script:LogLines.Clear(); $script:IcaclsCalls.Clear()
    Remove-Item -LiteralPath (Ledger-Path) -Force
    Save-Ledger @((Entry $revokeFolder 'M'))
    [void](Throws { Revoke-Roots })
    Check 'revoke: a clean ledger revokes the recorded grant and is then deleted' (
        -not $script:RevokeIncomplete -and @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' -and $_ -like '*shareR*' }).Count -eq 1 -and -not (Test-Path -LiteralPath (Ledger-Path)))

    # icacls fails: the ledger - the only record of a grant still on the folder - stays.
    $script:RevokeIncomplete = $false; $script:LogLines.Clear(); $script:IcaclsCalls.Clear()
    Save-Ledger @((Entry $revokeFolder 'M'))
    $script:IcaclsFail = { param($c) $c -like '*remove:g*' }
    [void](Throws { Revoke-Roots })
    Check 'revoke: a grant that cannot be removed keeps the ledger and flags the run' ($script:RevokeIncomplete -and (Test-Path -LiteralPath (Ledger-Path)) -and (($script:LogLines -join "`n") -like '*could not be revoked*'))
    $script:IcaclsFail = { param($c) $false }

    # A recovery file names grants in no ledger: never deleted, and the revoke is not called complete.
    $script:RevokeIncomplete = $false; $script:LogLines.Clear()
    Remove-Item -LiteralPath (Ledger-Path) -Force
    $recovery = Join-Path $dir 'granted-roots.recovery-abc.json'
    [IO.File]::WriteAllText($recovery, '[{"path":"C:\\x"}]')
    [void](Throws { Revoke-Roots })
    Check 'revoke: a recovery file is kept and the revoke is flagged incomplete' ($script:RevokeIncomplete -and (Test-Path -LiteralPath $recovery) -and (($script:LogLines -join "`n") -like '*recovery*NOT revoked*'))
} finally { Remove-Sandbox $dir }

# ------------------------------------------------------- 7. a log that stops taking lines is not a silent loss ----
$dir = New-Sandbox
try {
    Invoke-Expression $realWriteLogText
    $script:PendingLog.Clear()
    $script:LogPath = Join-Path $dir 'state.log'
    Write-Log 'first line' 6>$null
    # The state log becomes unwritable mid-run: a directory takes its place.
    Remove-Item -LiteralPath $script:LogPath -Force
    New-Item -ItemType Directory -Path $script:LogPath | Out-Null
    $warn = $null
    Write-Log 'second line' 3>&1 6>$null | ForEach-Object { if ($_ -is [System.Management.Automation.WarningRecord]) { $warn = $_.Message } }
    Write-Log 'third line' 6>$null
    Check 'log: a log that stops mid-run warns out loud and hands over to the pending list' ($null -eq $script:LogPath -and $warn -like '*stopped taking lines*' -and $script:PendingLog.Count -eq 2)
    $fallback = Join-Path $dir 'fallback.log'
    Write-PendingLog $fallback
    Check 'log: the lines the state log refused reach the fallback copy, in order' ((Get-Content -LiteralPath $fallback -Raw) -match 'second line[\s\S]*third line' -and $script:PendingLog.Count -eq 0)
} finally { $script:LogPath = $null; Invoke-Expression $stubWriteLogText; Remove-Sandbox $dir }

# ------------------------------------------- 8. the write-ahead intent record (a hard kill mid-grant) ----
function Intent-Files { @(Get-ChildItem -LiteralPath $script:DataDir -Filter 'granted-roots.intent-*.json' -Force -ErrorAction SilentlyContinue) }
function Write-Intent([object[]]$items) {
    $p = Join-Path $script:DataDir ("granted-roots.intent-{0}.json" -f [guid]::NewGuid().ToString('N'))
    [void](Write-LockedFile $p ((New-Object Text.UTF8Encoding($false)).GetBytes((ConvertTo-Json -InputObject @($items) -Depth 4))))
    return $p
}
function IntentItem([string]$path, [string]$rights = 'RX', $previous = $null, [bool]$pre = $false) {
    [pscustomobject]@{ path = $path; sid = $SidLocalService; rights = $rights; previousRights = $previous; preExisting = $pre }
}

# 8a. the record is on disk BEFORE the first ACE changes, and gone once the ledger is published.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $folderA | Out-Null
try {
    $script:seenAtFirstIcacls = -1
    $script:IcaclsFail = { param($c) if ($script:seenAtFirstIcacls -lt 0) { $script:seenAtFirstIcacls = @(Intent-Files).Count }; $false }
    Grant-Roots @($folderA) @() $true
    Check 'intent: the record exists before the first ACE is changed' ($script:seenAtFirstIcacls -eq 1)
    Check 'intent: ...and is dropped once the ledger is published' ((Intent-Files).Count -eq 0 -and (@(Get-Ledger)).Count -eq 1)
} finally { Remove-Sandbox $dir }

# 8b. a hard kill between the ACL change and the ledger publication, then the next grant settles it.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'; $folderB = Join-Path $dir 'shareB'
New-Item -ItemType Directory -Path $folderA, $folderB | Out-Null
try {
    $script:IcaclsFail = { param($c) throw 'injected kill' }
    [void](Throws { Grant-Roots @($folderA) @() $true })
    $left = @(Intent-Files)
    Check 'intent: a killed run leaves its record, in the locked form, and no ledger' (
        $left.Count -eq 1 -and (Get-LedgerLockProblem $left[0].FullName) -eq '' -and -not (Test-Path -LiteralPath (Ledger-Path)) -and ((Get-Content -LiteralPath $left[0].FullName -Raw) -like '*shareA*'))
    $script:IcaclsFail = { param($c) $false }; $script:IcaclsCalls.Clear(); $script:LogLines.Clear()
    Grant-Roots @($folderB) @() $true
    $removes = @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' })
    Check 'intent: the next grant takes back what the dead run may have granted, and drops the record' (
        $removes.Count -eq 1 -and $removes[0] -like '*shareA*' -and (Intent-Files).Count -eq 0)
    $ledgerPaths = @(Get-Ledger | ForEach-Object { $_.path })
    Check 'intent: ...and the new grant itself is recorded alone' ($ledgerPaths.Count -eq 1 -and $ledgerPaths[0] -eq $folderB)
} finally { Remove-Sandbox $dir }

# 8c. what the ledger already records is never taken back; a pre-existing grant and a gone folder are left alone.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'; $folderB = Join-Path $dir 'shareB'
New-Item -ItemType Directory -Path $folderA, $folderB | Out-Null
try {
    Save-Ledger @((Entry $folderA 'RX'))
    [void](Write-Intent @((IntentItem $folderA 'RX'), (IntentItem $folderB 'RX' $null $true), (IntentItem (Join-Path $dir 'gone'))))
    $script:IcaclsCalls.Clear()
    [void](Resolve-StaleGrantIntents @(Get-Ledger))
    Check 'intent: a recorded entry, a pre-existing grant and a vanished folder cause no ACL change' ($script:IcaclsCalls.Count -eq 0 -and (Intent-Files).Count -eq 0)

    # a level that was changed (RX -> M) and not recorded is restored, not removed
    [void](Write-Intent @((IntentItem $folderA 'M' 'RX')))
    $script:IcaclsCalls.Clear()
    [void](Resolve-StaleGrantIntents @(Get-Ledger))
    Check 'intent: a level changed by the dead run is put back to what it was' (
        @($script:IcaclsCalls | Where-Object { $_ -like '*/grant:r*(RX)*' }).Count -eq 1 -and @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' }).Count -eq 0)
} finally { Remove-Sandbox $dir }

# 8d. a record that is not in the locked form is never acted on and never deleted.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $folderA | Out-Null
try {
    $foreign = Join-Path $dir 'granted-roots.intent-planted.json'
    [IO.File]::WriteAllText($foreign, ('[{"path":"' + ($folderA -replace '\\', '\\') + '","rights":"RX","preExisting":false}]'))
    $script:IcaclsCalls.Clear(); $script:LogLines.Clear()
    $attention = Resolve-StaleGrantIntents @()
    Check 'intent: a record in an unlocked form is kept, flagged and acts on nothing' (
        $attention -and (Test-Path -LiteralPath $foreign) -and $script:IcaclsCalls.Count -eq 0 -and (($script:LogLines -join "`n") -like '*not acted on*'))
    Remove-Item -LiteralPath $foreign -Force
    $broken = Join-Path $dir 'granted-roots.intent-broken.json'
    [void](Write-LockedFile $broken ([Text.Encoding]::UTF8.GetBytes('{ not json')))
    $attention = Resolve-StaleGrantIntents @()
    Check 'intent: a locked record that cannot be parsed is kept and flagged' ($attention -and (Test-Path -LiteralPath $broken) -and $script:IcaclsCalls.Count -eq 0)
} finally { Remove-Sandbox $dir }

# 8e. a take-back that fails is named in a recovery file; the record is then dropped.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $folderA | Out-Null
try {
    [void](Write-Intent @((IntentItem $folderA)))
    $script:IcaclsFail = { param($c) $c -like '*remove:g*' }
    $attention = Resolve-StaleGrantIntents @()
    $evidence = @(Get-ChildItem -LiteralPath $dir -Filter 'granted-roots.recovery-*.json' -Force)
    Check 'intent: a grant that cannot be taken back lands in a recovery file and the record goes' (
        $attention -and $evidence.Count -eq 1 -and ((Get-Content -LiteralPath $evidence[0].FullName -Raw) -like '*shareA*') -and (Intent-Files).Count -eq 0)
    $script:IcaclsFail = { param($c) $false }
} finally { Remove-Sandbox $dir }

# 8f. the record cannot be written: nothing is granted, exit 18.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $folderA | Out-Null
try {
    $script:DataDir = Join-Path $dir 'no-such-state-dir'
    $script:IcaclsCalls.Clear()
    $err = Throws { Grant-Roots @($folderA) @() $true }
    Check 'intent: when it cannot be written nothing is granted and the run exits 18' ($err -like 'FAIL:18:*' -and $script:IcaclsCalls.Count -eq 0)
} finally { Remove-Sandbox $dir }

# 8g. remove settles a dead run's record too.
$dir = New-Sandbox
$folderA = Join-Path $dir 'shareA'
New-Item -ItemType Directory -Path $folderA | Out-Null
try {
    [void](Write-Intent @((IntentItem $folderA)))
    $script:RevokeIncomplete = $false; $script:IcaclsCalls.Clear()
    [void](Throws { Revoke-Roots })
    Check 'revoke: a dead run''s unrecorded grant is taken back and the record dropped, complete' (
        -not $script:RevokeIncomplete -and @($script:IcaclsCalls | Where-Object { $_ -like '*remove:g*' -and $_ -like '*shareA*' }).Count -eq 1 -and (Intent-Files).Count -eq 0)
} finally { Remove-Sandbox $dir }

# ------------------------------------------------- 9. the FileDO bundle root on the service line ----
# The worker takes the bundle root from the executable's grandparent when no --filedo-root is
# given, and a service runs a copy staged under %ProgramData%, so that default names
# %ProgramData%\FastMediaSorterCompanion and the Program Files check refuses it. Every
# registration the helper writes (install, repair, migrate-to-server, the in-place update) goes
# through Get-ServiceBinPath, so that one function is where the root has to be named.
$BinDirName = 'bin'
$installed = 'C:\Program Files\FastMediaSorter_Server'
$installedExe = "$installed\companion\fms-share-worker.exe"
$machineDir = 'C:\ProgramData\FastMediaSorterCompanion'
$stagedExe = "$machineDir\bin\fms-share-worker.exe"
$script:RegistryImagePath = ''
function Get-ServiceRegistryValue([string]$name) { if ($name -eq 'ImagePath') { return $script:RegistryImagePath } else { return '' } }
function Binpath-For([string]$exe, [string]$explicitRoot = '', [string]$serviceExe = $stagedExe) {
    $script:FileDoRoot = $explicitRoot; $script:ExePath = $exe; $script:ServiceExePath = $serviceExe
    $script:DataDir = $machineDir; $script:ManageSid = 'S-1-5-21-1-2-3-1001'
    return (Get-ServiceBinPath)
}
$line = Binpath-For $installedExe
Check 'filedo: the service line names the installation folder as the bundle root' ($line -like '*--filedo-root "C:\Program Files\FastMediaSorter_Server"*') $line
Check 'filedo: the service line still starts with the staged worker and carries datadir and manage-sid' (
    $line.StartsWith("`"$stagedExe`" --service --datadir `"$machineDir`"") -and $line -like '*--manage-sid "S-1-5-21-1-2-3-1001"*')
$line = Binpath-For $installedExe 'D:\Apps\FMS\'
Check 'filedo: an explicit -FileDoRoot wins over the derived one, trailing backslash dropped' ($line -like '*--filedo-root "D:\Apps\FMS"' -or $line -like '*--filedo-root "D:\Apps\FMS" *') $line
$script:RegistryImagePath = "`"$stagedExe`" --service --datadir `"$machineDir`" --filedo-root `"$installed`""
$line = Binpath-For $stagedExe
Check 'filedo: a repair run started from the staged copy keeps the root the registration already names' ($line -like '*--filedo-root "C:\Program Files\FastMediaSorter_Server"*') $line
$script:RegistryImagePath = ''
$line = Binpath-For $stagedExe
Check 'filedo: from the staged copy with nothing to keep, no root is invented (never the ProgramData parent)' ($line -notlike '*--filedo-root*') $line

# ---------------------------------------------- 10. the service account follows the FileDO bundle ----
# LocalService is the rule (H-14); LocalSystem only where the optional FileDO component is on disk,
# because the worker refuses disk sharing under any other holder.
$acctRoot = Join-Path ([IO.Path]::GetTempPath()) ('fms-acct-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path (Join-Path $acctRoot 'companion') | Out-Null
    $script:RegistryImagePath = ''
    $script:ExePath = Join-Path $acctRoot 'companion\fms-share-worker.exe'; $script:FileDoRoot = ''
    Check 'account: an installation without the FileDO component runs as LocalService' ((Get-WantedServiceAccount) -eq 'NT AUTHORITY\LocalService') (Get-WantedServiceAccount)
    New-Item -ItemType Directory -Path (Join-Path $acctRoot 'filedo') | Out-Null
    Check 'account: an empty filedo folder is not the component' ((Get-WantedServiceAccount) -eq 'NT AUTHORITY\LocalService') (Get-WantedServiceAccount)
    Set-Content -LiteralPath (Join-Path $acctRoot 'filedo\filedo.exe') -Value 'x'
    Check 'account: with filedo.exe in the bundle the holder is LocalSystem' ((Get-WantedServiceAccount) -eq 'LocalSystem') (Get-WantedServiceAccount)
    $script:ExePath = ''; $script:FileDoRoot = ''
    Check 'account: no known installation root never selects LocalSystem' ((Get-WantedServiceAccount) -eq 'NT AUTHORITY\LocalService') (Get-WantedServiceAccount)
} finally { Remove-Item -LiteralPath $acctRoot -Recurse -Force -ErrorAction SilentlyContinue }

# ------------------------------------ 11. a stale filedo-state owned by someone else is set aside ----
# The worker reuses <DataDir>\filedo-state only when the system or an administrator owns it. The
# directory just created is owned by whoever runs this test, so the trusted SIDs are swapped per case.
$sandbox = New-Sandbox
try {
    $stateDir = Join-Path $sandbox 'filedo-state'
    New-Item -ItemType Directory -Path $stateDir | Out-Null
    Set-Content -LiteralPath (Join-Path $stateDir 'planted.txt') -Value 'x'
    $realOwner = (Get-Acl -LiteralPath $stateDir).GetOwner([Security.Principal.SecurityIdentifier]).Value
    $SidSystem = $realOwner; $SidAdmins = $realOwner
    Move-UntrustedWorkerStateAside
    Check 'state: a directory owned by the system or an administrator is left in place' (Test-Path -LiteralPath $stateDir -PathType Container)
    $SidSystem = 'S-1-5-18'; $SidAdmins = 'S-1-5-99-1'
    Move-UntrustedWorkerStateAside
    $aside = @(Get-ChildItem -LiteralPath $sandbox -Directory | Where-Object { $_.Name -like 'filedo-state.untrusted-*' })
    Check 'state: a directory owned by anyone else is renamed, not deleted' (-not (Test-Path -LiteralPath $stateDir) -and $aside.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $aside[0].FullName 'planted.txt')))
    Check 'state: ...and the move is logged' (@($script:LogLines | Where-Object { $_ -like 'moving*filedo-state*aside*' }).Count -eq 1)
    Move-UntrustedWorkerStateAside
    Check 'state: no directory is nothing to do' ($true)
} finally { Remove-Sandbox $sandbox; $SidSystem = $me; $SidAdmins = $me }

# --------------------------------------------------------------------------------- verdict ----
$failed =@($results | Where-Object { -not $_.Passed })
if ($failed.Count -gt 0) { Exit-Verdict 'ShareServiceHelperLogic' 1 "$($failed.Count) of $($results.Count) check(s) failed" }
Exit-Verdict 'ShareServiceHelperLogic' 0 "$($results.Count) checks; unelevated - the elevated ownership/ACE scenarios stay with ShareServiceHelper.ps1 on a VM"
