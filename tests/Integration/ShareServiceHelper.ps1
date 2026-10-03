<#
.SYNOPSIS
    Elevated integration checks for publishing/installer/install-share-service.ps1 -
    the grant and service-account guarantees of ticket 054 (H-09, H-13, H-14).

.DESCRIPTION
    Everything runs against a throw-away directory under %ProgramData%, never against the
    real machine state directory, and every ACE this script causes is removed again.

    Always run (need elevation, touch no service):
      * H-13: grant-roots exits 12, not 0, when a requested folder was not granted
        (missing, or refused), logs why, and still grants the rest;
      * H-09: grant-roots refuses a system folder, and a start/restart-style re-grant
        (Grant-StateRoots, loaded out of the helper itself) re-applies ONLY folders an
        administrator approved in the locked ledger - never one merely listed in
        shares.json, and nothing at all when the ledger lost its locked form.

      * ticket 102 S24-2: a "[]" ledger permits a first grant; an empty / malformed / shapeless
        ledger grants nothing, exits 15 and stays byte for byte; repeated grants keep the whole
        history in a locked ledger with no .tmp/.bak/.rejected leak; four helpers racing on one
        data directory lose nothing (grant-roots only - no service, no firewall rule).

    Only with -ServiceInstall, and only when the service is NOT registered (on a VM):
      * ticket 102 S24-1 / S24-2: "remove" with no state directory exits 0, does not recreate it
        and leaves its run in the elevated account's %TEMP% log; "remove" with an unreadable
        ledger keeps it byte for byte and reports "WITH WARNINGS".
      * H-14: "install" registers the service as NT AUTHORITY\LocalService; "remove"
        then takes it away again. That "remove" also deletes the firewall rule named
        "FastMediaSorter Companion SFTP" and every rule aimed at the repository's worker
        - so run it on a machine whose own sharing does not matter.

    Exit codes (CHECK-VERDICT): 0 = every check passed; 1 = a check failed; 2 = could not verify
    (not elevated, helper missing, or -ServiceInstall refused because a service exists). The last
    line is the verdict, `ShareServiceHelper: <WORD> (..)`.

.EXAMPLE
    # From an elevated PowerShell:
    pwsh -NoProfile -File .\tests\Integration\ShareServiceHelper.ps1
    pwsh -NoProfile -File .\tests\Integration\ShareServiceHelper.ps1 -ServiceInstall   # VM only
#>
[CmdletBinding()]
param(
    [switch]$ServiceInstall,

    # The PowerShell the HELPER runs under (default: the one running this script). The installers
    # run powershell.exe (5.1); pass it from pwsh to exercise both editions of the same helper.
    [string]$HelperShell
)

$ErrorActionPreference = 'Stop'

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$helper = Join-Path $repo 'publishing\installer\install-share-service.ps1'
$worker = Join-Path $repo 'payload\companion\fms-share-worker.exe'
$shell  = if ($HelperShell) { $HelperShell } else { (Get-Process -Id $PID).Path }
$SidLocalService = 'S-1-5-19'
$SidAdmins = 'S-1-5-32-544'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Exit-Verdict 'ShareServiceHelper' 2 'not elevated - the helper refuses to run otherwise'
}
if (-not (Test-Path -LiteralPath $helper)) {
    Exit-Verdict 'ShareServiceHelper' 2 "helper not found ($helper)"
}

$me = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$results = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Check = $name; Passed = $ok; Detail = $detail })
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $color
}

# Explicit (not inherited) Allow ACEs for LOCAL SERVICE on $path, as FileSystemRights.
function Get-LocalServiceRights([string]$path) {
    $rights = 0
    foreach ($ace in (Get-Acl -LiteralPath $path).GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])) {
        if ($ace.IdentityReference.Value -eq $SidLocalService -and $ace.AccessControlType -eq 'Allow') {
            $rights = $rights -bor [int]$ace.FileSystemRights
        }
    }
    return $rights
}
function Test-CanWrite([int]$rights) {
    return ($rights -band [int][Security.AccessControl.FileSystemRights]::WriteData) -ne 0
}

# Runs the helper the way the Share Manager does (a separate elevated process) and
# returns its exit code and the log it wrote into the data directory.
function Invoke-Helper([string]$dataDir, [string[]]$helperArgs) {
    $log = Join-Path $dataDir 'install-share-service.log'
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
    $out = & $shell -NoProfile -ExecutionPolicy Bypass -File $helper @helperArgs -DataDir $dataDir -ManageSid $me 2>&1
    $code = $LASTEXITCODE
    $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { ($out | Out-String) }
    return [pscustomobject]@{ Code = $code; Log = [string]$text }
}

$root = Join-Path $env:ProgramData ('FmsHelperIntegration-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$touched = New-Object System.Collections.Generic.List[string]
New-Item -ItemType Directory -Path $root | Out-Null

try {
    # ------------------------------------------------------------ H-13 / H-09: grant-roots
    $data = Join-Path $root 'data'
    New-Item -ItemType Directory -Path $data | Out-Null

    $missing = Join-Path $root 'does-not-exist'
    $r = Invoke-Helper $data @('-Action', 'grant-roots', '-Roots', $missing, '-ReadOnlyRoots', '|')
    Check 'H-13 a missing folder is exit 12, not "done"' ($r.Code -eq 12) "exit $($r.Code)"
    Check 'H-13 the log says why' ($r.Log -match 'SKIPPED' -and $r.Log -match 'FAILED:') ''

    $system = Join-Path $env:SystemRoot 'Temp'
    $before = Get-LocalServiceRights $system
    $r = Invoke-Helper $data @('-Action', 'grant-roots', '-Roots', $system, '-ReadOnlyRoots', '|')
    Check 'H-09 a system folder is refused (exit 12)' ($r.Code -eq 12 -and $r.Log -match 'REFUSED') "exit $($r.Code)"
    Check 'H-09 ...and gains no LOCAL SERVICE entry' ((Get-LocalServiceRights $system) -eq $before) ''

    $share = Join-Path $root 'share'
    New-Item -ItemType Directory -Path $share | Out-Null
    $touched.Add($share)
    $r = Invoke-Helper $data @('-Action', 'grant-roots', '-Roots', $share, '-ReadOnlyRoots', '|')
    Check 'grant-roots grants a plain folder (exit 0)' ($r.Code -eq 0) "exit $($r.Code)"
    Check '...read/write, as a writable root' (Test-CanWrite (Get-LocalServiceRights $share)) ''
    $ledger = Join-Path $data 'granted-roots.json'
    $ledgerAcl = if (Test-Path -LiteralPath $ledger) { Get-Acl -LiteralPath $ledger } else { $null }
    Check 'H-09 the ledger is written locked (Administrators, no inheritance)' (
        $null -ne $ledgerAcl -and $ledgerAcl.AreAccessRulesProtected -and
        $ledgerAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $SidAdmins) ''

    $share2 = Join-Path $root 'share2'
    New-Item -ItemType Directory -Path $share2 | Out-Null
    $touched.Add($share2)
    $r = Invoke-Helper $data @('-Action', 'grant-roots', '-Roots', "$share2|$missing", '-ReadOnlyRoots', $share2)
    Check 'H-13 a partial grant is still exit 12' ($r.Code -eq 12) "exit $($r.Code)"
    $rights2 = Get-LocalServiceRights $share2
    Check '...while the folder that could be granted is, read-only' ($rights2 -ne 0 -and -not (Test-CanWrite $rights2)) ''

    # ------------------------------------------------ H-09: the start/restart re-grant
    # Grant-StateRoots runs inside install/repair/start/restart, all of which need a
    # registered service. It is loaded out of the helper instead - every top-level
    # function and assignment, nothing else (no trap, no elevation call, no action).
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($helper, [ref]$null, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) { throw "the helper does not parse: $($errors[0].Message)" }
    $definitions = foreach ($st in $ast.EndBlock.Statements) {
        if ($st -is [System.Management.Automation.Language.FunctionDefinitionAst] -or
            $st -is [System.Management.Automation.Language.AssignmentStatementAst]) { $st.Extent.Text }
    }
    $definitions = $definitions -join "`n"

    $stateData = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $stateData | Out-Null
    $approved = Join-Path $root 'approved'
    $listedOnly = Join-Path $root 'listed-only'
    foreach ($d in @($approved, $listedOnly)) { New-Item -ItemType Directory -Path $d | Out-Null; $touched.Add($d) }

    $outcome = & {
        param($text, $DataDir, $ManageSid, $ExePath, $approved, $listedOnly)
        . ([scriptblock]::Create($text))
        $ErrorActionPreference = 'Stop'
        $shares = @(
            [pscustomobject]@{ hostPath = $approved; readOnly = $false },
            [pscustomobject]@{ hostPath = $listedOnly; readOnly = $false })
        Set-Content -LiteralPath (Join-Path $DataDir 'shares.json') -Value (ConvertTo-Json -InputObject $shares) -Encoding UTF8
        Save-Ledger @([pscustomobject]@{ path = $approved; sid = 'S-1-5-19'; rights = 'M'; preExisting = $false })
        Grant-StateRoots 'integration test'
        $first = ($script:PendingLog -join "`n")
        $script:PendingLog.Clear()

        # A ledger that lost its locked form (rewritten through the directory's own
        # write access) must re-grant nothing - not even what it names.
        & icacls.exe $approved /remove:g '*S-1-5-19' | Out-Null
        $ledgerPath = Join-Path $DataDir 'granted-roots.json'
        Remove-Item -LiteralPath $ledgerPath -Force
        Set-Content -LiteralPath $ledgerPath -Encoding UTF8 -Value (ConvertTo-Json -InputObject @(
            [pscustomobject]@{ path = $approved; sid = 'S-1-5-19'; rights = 'M'; preExisting = $false },
            [pscustomobject]@{ path = $listedOnly; sid = 'S-1-5-19'; rights = 'M'; preExisting = $false }))
        Grant-StateRoots 'integration test, tampered ledger'
        $second = ($script:PendingLog -join "`n")
        [pscustomobject]@{ First = $first; Second = $second }
    } $definitions $stateData $me $worker $approved $listedOnly

    # The first pass granted $approved; the tampered pass removed nothing and added nothing.
    Check 'H-09 a merely listed folder is not granted on start' ((Get-LocalServiceRights $listedOnly) -eq 0) ''
    Check '...and the log says so' ($outcome.First -match [regex]::Escape("NOT granting '$listedOnly'")) ''
    Check 'H-09 a tampered ledger re-grants nothing' ((Get-LocalServiceRights $approved) -eq 0 -and $outcome.Second -match 'not used to re-grant') ''
    Check '...the approved folder was re-granted on the first pass' ($outcome.First -match [regex]::Escape("granting LOCAL SERVICE read/write access on '$approved'")) ''

    # ------------------------------------------- ticket 102 / S24-2: the ledger, read and published
    # Only grant-roots on throw-away data directories and folders: no service, no firewall rule.
    function Read-LedgerPaths([string]$dataDir) {
        $text = Get-Content -LiteralPath (Join-Path $dataDir 'granted-roots.json') -Raw
        return @($text | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $_.path })
    }
    function Get-ByteHash([string]$file) { return (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    function New-Share([string]$name) {
        $d = Join-Path $root $name
        New-Item -ItemType Directory -Path $d | Out-Null
        $touched.Add($d)
        return $d
    }

    # Missing and a valid empty array both permit a first grant.
    $dEmpty = Join-Path $root 'ledger-empty'; New-Item -ItemType Directory -Path $dEmpty | Out-Null
    $a = New-Share 'ledger-a'
    Set-Content -LiteralPath (Join-Path $dEmpty 'granted-roots.json') -Value '[]' -Encoding UTF8
    $r = Invoke-Helper $dEmpty @('-Action', 'grant-roots', '-Roots', $a, '-ReadOnlyRoots', '|')
    Check 'S24-2 a valid empty array "[]" permits a first grant (exit 0)' ($r.Code -eq 0 -and (Read-LedgerPaths $dEmpty) -contains $a) "exit $($r.Code)"

    # Corrupt / empty / unreadable input grants nothing, exits 15, and is retained byte for byte.
    $b = New-Share 'ledger-b'
    foreach ($case in @(
            @{ Name = 'an empty file'; Text = '' },
            @{ Name = 'malformed JSON'; Text = '{ broken' },
            @{ Name = 'an entry without a path'; Text = '[{"sid":"S-1-5-19","rights":"RX"}]' })) {
        $dBad = Join-Path $root ('ledger-bad-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)); New-Item -ItemType Directory -Path $dBad | Out-Null
        $bad = Join-Path $dBad 'granted-roots.json'
        [IO.File]::WriteAllText($bad, [string]$case.Text, (New-Object Text.UTF8Encoding($false)))
        $hash = Get-ByteHash $bad
        $rightsBefore = Get-LocalServiceRights $b
        $r = Invoke-Helper $dBad @('-Action', 'grant-roots', '-Roots', $b, '-ReadOnlyRoots', '|')
        Check "S24-2 $($case.Name): grant exits 15, grants nothing, keeps the file byte for byte" (
            $r.Code -eq 15 -and (Get-LocalServiceRights $b) -eq $rightsBefore -and (Get-ByteHash $bad) -eq $hash) "exit $($r.Code)"
        $leaks = @(Get-ChildItem -LiteralPath $dBad -Force | Where-Object { $_.Name -match '\.(tmp|bak)-' })
        Check "S24-2 $($case.Name): no temp or backup is left behind" ($leaks.Count -eq 0) ''
    }

    # Valid multi-root ledger, repeated grants: history preserved, final ACL locked, nothing leaked.
    $dMulti = Join-Path $root 'ledger-multi'; New-Item -ItemType Directory -Path $dMulti | Out-Null
    $m1 = New-Share 'multi-1'; $m2 = New-Share 'multi-2'; $m3 = New-Share 'multi-3'
    [void](Invoke-Helper $dMulti @('-Action', 'grant-roots', '-Roots', $m1, '-ReadOnlyRoots', '|'))
    [void](Invoke-Helper $dMulti @('-Action', 'grant-roots', '-Roots', $m2, '-ReadOnlyRoots', $m2))
    $r = Invoke-Helper $dMulti @('-Action', 'grant-roots', '-Roots', "$m1|$m3", '-ReadOnlyRoots', '|')
    $paths = Read-LedgerPaths $dMulti
    Check 'S24-2 repeated grants keep every previously approved entry' ($r.Code -eq 0 -and $paths -contains $m1 -and $paths -contains $m2 -and $paths -contains $m3) "exit $($r.Code); $($paths.Count) entries"
    $mAcl = Get-Acl -LiteralPath (Join-Path $dMulti 'granted-roots.json')
    $foreign = @($mAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object { @('S-1-5-18', $SidAdmins) -notcontains $_.IdentityReference.Value })
    Check 'S24-2 the published ledger is Administrators-owned, protected, SYSTEM/Administrators only' (
        $mAcl.AreAccessRulesProtected -and $mAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $SidAdmins -and $foreign.Count -eq 0) ''
    Check 'S24-2 no .tmp/.bak/.rejected generation leaks after successful publications' (
        @(Get-ChildItem -LiteralPath $dMulti -Force | Where-Object { $_.Name -match '\.(tmp|bak|rejected)-' }).Count -eq 0) ''

    # Two helpers at once on one data directory: no lost history, no exit 16 inside the 10 s wait.
    $dRace = Join-Path $root 'ledger-race'; New-Item -ItemType Directory -Path $dRace | Out-Null
    $race = 1..4 | ForEach-Object { New-Share "race-$_" }
    $procs = foreach ($folder in $race) {
        Start-Process -FilePath $shell -PassThru -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$helper`"", '-Action', 'grant-roots',
            '-Roots', "`"$folder`"", '-ReadOnlyRoots', '"|"', '-DataDir', "`"$dRace`"", '-ManageSid', $me)
    }
    $procs | Wait-Process -Timeout 120
    $codes = @($procs | ForEach-Object { $_.ExitCode })
    $racePaths = Read-LedgerPaths $dRace
    $missingFromLedger = @($race | Where-Object { $racePaths -notcontains $_ })
    Check 'S24-2 concurrent helpers on one data directory lose no history' (($codes | Where-Object { $_ -ne 0 }).Count -eq 0 -and $missingFromLedger.Count -eq 0) "exit codes $($codes -join ','); missing $($missingFromLedger.Count)"

    # ------------------------------------------------------------ H-14: the service account
    if ($ServiceInstall) {
        if (Get-Service -Name 'FastMediaSorterCompanionSFTP' -ErrorAction SilentlyContinue) {
            Write-Host 'NOT RUN: H-14 - FastMediaSorterCompanionSFTP is already registered on this machine; run -ServiceInstall on a VM without it.' -ForegroundColor Yellow
            $results.Add([pscustomobject]@{ Check = 'H-14 service account'; Passed = $null; Detail = 'not run' })
        } elseif (-not (Test-Path -LiteralPath $worker)) {
            Write-Host "NOT RUN: H-14 - worker not found ($worker)." -ForegroundColor Yellow
            $results.Add([pscustomobject]@{ Check = 'H-14 service account'; Passed = $null; Detail = 'not run' })
        } else {
            $svcData = Join-Path $root 'svc'
            try {
                $r = Invoke-Helper $svcData @('-Action', 'install', '-ExePath', $worker)
                Check 'H-14 install succeeds' ($r.Code -eq 0) "exit $($r.Code)"
                $svc = Get-CimInstance -ClassName Win32_Service -Filter "Name='FastMediaSorterCompanionSFTP'"
                Check 'H-14 the service runs as LOCAL SERVICE' ($null -ne $svc -and $svc.StartName -ieq 'NT AUTHORITY\LocalService') "StartName '$($svc.StartName)'"
            } finally {
                $r = Invoke-Helper $svcData @('-Action', 'remove', '-ExePath', $worker)
                Check 'H-14 remove takes the service away' ($r.Code -eq 0 -and -not (Get-Service -Name 'FastMediaSorterCompanionSFTP' -ErrorAction SilentlyContinue)) "exit $($r.Code)"
            }

            # ticket 102 S24-1: the state directory is gone, the action still succeeds, and its lines
            # are on record in the ELEVATED account's own %TEMP% - the directory is not recreated for it.
            $gone = Join-Path $root 'svc-gone'
            $fallbackLog = Join-Path ([IO.Path]::GetTempPath()) 'install-share-service.log'
            $sizeBefore = if (Test-Path -LiteralPath $fallbackLog) { (Get-Item -LiteralPath $fallbackLog).Length } else { 0 }
            $r = Invoke-Helper $gone @('-Action', 'remove', '-ExePath', $worker)
            $fallbackTail = if (Test-Path -LiteralPath $fallbackLog) { (Get-Content -LiteralPath $fallbackLog -Raw) } else { '' }
            $sizeAfter = if (Test-Path -LiteralPath $fallbackLog) { (Get-Item -LiteralPath $fallbackLog).Length } else { 0 }
            Check 'S24-1 remove with no state directory exits 0' ($r.Code -eq 0) "exit $($r.Code)"
            Check 'S24-1 ...the state directory is not recreated just for logging' (-not (Test-Path -LiteralPath $gone)) ''
            Check 'S24-1 ...and the fallback log in this account''s TEMP holds the run' (
                $sizeAfter -gt $sizeBefore -and $fallbackTail -match 'action=remove completed') "$fallbackLog ($sizeBefore -> $sizeAfter bytes)"

            # ticket 102 S24-2 / I02.3: remove with an unreadable ledger keeps it byte for byte and says so.
            $dBadRemove = Join-Path $root 'svc-bad-ledger'; New-Item -ItemType Directory -Path $dBadRemove | Out-Null
            $badLedger = Join-Path $dBadRemove 'granted-roots.json'
            [IO.File]::WriteAllText($badLedger, '{ broken', (New-Object Text.UTF8Encoding($false)))
            $badHash = Get-ByteHash $badLedger
            $r = Invoke-Helper $dBadRemove @('-Action', 'remove', '-ExePath', $worker)
            Check 'S24-2 remove keeps an unreadable ledger byte for byte' ((Get-ByteHash $badLedger) -eq $badHash) ''
            Check 'S24-2 ...and does not call the revoke complete (WARNING + "WITH WARNINGS")' (
                $r.Log -match 'WARNING' -and $r.Log -match 'WITH WARNINGS' -and $r.Log -notmatch 'server role removed\. State kept') "exit $($r.Code)"
        }
    }
} finally {
    foreach ($d in $touched) { if (Test-Path -LiteralPath $d) { & icacls.exe $d /remove:g "*$SidLocalService" | Out-Null } }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

$failed = @($results | Where-Object { $_.Passed -eq $false })
$notRun = @($results | Where-Object { $null -eq $_.Passed })
if ($failed.Count -gt 0) { Exit-Verdict 'ShareServiceHelper' 1 "$($failed.Count) of $($results.Count) check(s) failed" }
if ($notRun.Count -gt 0) { Exit-Verdict 'ShareServiceHelper' 2 "$($notRun.Count) of $($results.Count) check(s) not run" }
Exit-Verdict 'ShareServiceHelper' 0 "$($results.Count) check(s) passed"
