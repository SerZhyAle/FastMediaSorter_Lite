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

    Only with -ServiceInstall, and only when the service is NOT registered (on a VM):
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
    [switch]$ServiceInstall
)

$ErrorActionPreference = 'Stop'

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$helper = Join-Path $repo 'publishing\installer\install-share-service.ps1'
$worker = Join-Path $repo 'payload\companion\fms-share-worker.exe'
$shell  = (Get-Process -Id $PID).Path
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
