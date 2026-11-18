<#
.SYNOPSIS
    Non-elevated integration checks for the Share Manager's custody of guarantee 3
    (stable identity across migrations and host-mode changes; contract SHARE-SESSION).
    Specification 036 (Ф-2).

.DESCRIPTION
    Validates publishing/installer/install-share-service.ps1 state inspection and
    migration logic against temporary directories using the vendored Go worker
    (payload/companion/fms-share-worker.exe --inspect-datadir):

      1. Fresh target receives identity and fingerprint is unchanged;
      2. Target already holding same identity is left untouched (-OnlyWhenTargetIsFresh);
      3. Target holding a DIFFERENT identity stops migration with exit code 7;
      4. Two candidate user directories stop instead of choosing (exit code 10);
      5. A damaged host key (source or target) stops the migration at the inspection with exit code 6,
         copies nothing and overwrites nothing (WORKER-LAUNCH section 1, "corrupt identity");
      6. Malformed worker flags - --datadir or --inspect-datadir without a value, --inspect-datadir together
         with --service, an unknown flag - are refused with a non-zero exit (WORKER-LAUNCH section 1, "malformed flags").

    Exit codes (CHECK-VERDICT):
      0 - all identity migration checks passed;
      1 - a check failed (e.g. identity lost, different key overwritten, wrong exit code);
      2 - could not verify (worker or helper missing, ssh-keygen unavailable).
    The last line is the verdict, `ShareIdentityMigration: <WORD> (..)`.

.EXAMPLE
    pwsh -NoProfile -File .\tests\Integration\ShareIdentityMigration.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$helper = Join-Path $repo 'publishing\installer\install-share-service.ps1'
$worker = Join-Path $repo 'payload\companion\fms-share-worker.exe'
$shell  = (Get-Process -Id $PID).Path

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'ShareIdentityMigration' 2 'the check itself failed'
}

Write-Subject 'ShareIdentityMigration' 'install-share-service.ps1 state migration & custody of SHARE-SESSION guarantee 3'

if (-not (Test-Path -LiteralPath $helper)) {
    Exit-Verdict 'ShareIdentityMigration' 2 "helper script not found ($helper)"
}
if (-not (Test-Path -LiteralPath $worker)) {
    Exit-Verdict 'ShareIdentityMigration' 2 "worker binary not found ($worker)"
}

# Verify ssh-keygen is available to generate ed25519 test keys
$sshKeygen = (Get-Command 'ssh-keygen' -ErrorAction SilentlyContinue)
if (-not $sshKeygen) {
    Exit-Verdict 'ShareIdentityMigration' 2 'ssh-keygen not found in PATH'
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Check = $name; Passed = $ok; Detail = $detail })
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $color
}

# Helper to run a test action in a separate PowerShell process using the extracted definitions
function Invoke-StateAction([string]$actionScript) {
    $runnerTemplate = @'
param($helperScript, $workerExe, $actionScript)
$ErrorActionPreference = 'Stop'
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($helperScript, [ref]$null, [ref]$errors)
if ($errors -and $errors.Count -gt 0) { throw "the helper does not parse: $($errors[0].Message)" }
$definitions = ($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -or $_ -is [System.Management.Automation.Language.AssignmentStatementAst] } | ForEach-Object { $_.Extent.Text }) -join "`n"
. ([scriptblock]::Create($definitions))
$ExePath = $workerExe
$script:InspectExePath = $workerExe

. ([scriptblock]::Create($actionScript))
'@
    $tempPs1 = Join-Path ([System.IO.Path]::GetTempPath()) ("fms-test-migration-" + [Guid]::NewGuid().ToString('N') + ".ps1")
    Set-Content -LiteralPath $tempPs1 -Value $runnerTemplate -Encoding UTF8
    try {
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $tempPs1 $helper $worker $actionScript 2>&1
        $code = $LASTEXITCODE
        return [pscustomobject]@{ ExitCode = $code; Output = ($output | Out-String) }
    } finally {
        if (Test-Path -LiteralPath $tempPs1) { Remove-Item -LiteralPath $tempPs1 -Force -ErrorAction SilentlyContinue }
    }
}

function New-TestHostKey([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $keyFile = Join-Path $dir 'hostkey'
    if (Test-Path -LiteralPath $keyFile) { Remove-Item -LiteralPath $keyFile -Force }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $sshKeygen.Source
    $psi.Arguments = "-t ed25519 -N `"`" -f `"$keyFile`" -q"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) {
        throw "ssh-keygen failed with exit code $($p.ExitCode)"
    }
    if (Test-Path -LiteralPath "$keyFile.pub") { Remove-Item -LiteralPath "$keyFile.pub" -Force }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FmsIdentityMigrationTest-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    # ------------------------------------------------------------
    # 1. Fresh target receives identity and fingerprint is unchanged
    # ------------------------------------------------------------
    $src1 = Join-Path $testRoot 'case1-src'
    $dst1 = Join-Path $testRoot 'case1-dst'
    New-TestHostKey $src1
    Set-Content -LiteralPath (Join-Path $src1 'shares.json') -Value '[{"hostPath":"C:\\Media","readOnly":false}]' -Encoding UTF8

    $fp1SrcJson = (Invoke-StateAction "Get-StateInfo '$src1' | ConvertTo-Json").Output
    $fp1Src = ($fp1SrcJson | ConvertFrom-Json).fingerprint

    $r1 = Invoke-StateAction "Copy-State '$src1' '$dst1'"
    $fp1DstJson = (Invoke-StateAction "Get-StateInfo '$dst1' | ConvertTo-Json").Output
    $fp1Dst = if ($fp1DstJson) { ($fp1DstJson | ConvertFrom-Json).fingerprint } else { '' }

    Check '1. Fresh target receives identity' ($r1.ExitCode -eq 0 -and (Test-Path (Join-Path $dst1 'hostkey'))) "exit $($r1.ExitCode)"
    Check '...and fingerprint matches source' ($fp1Dst -ne '' -and $fp1Dst -eq $fp1Src) "src=$fp1Src dst=$fp1Dst"
    Check '...and state files are copied' (Test-Path (Join-Path $dst1 'shares.json')) ''

    # ------------------------------------------------------------
    # 2. Target already holding same identity is left untouched (-OnlyWhenTargetIsFresh)
    # ------------------------------------------------------------
    $src2 = Join-Path $testRoot 'case2-src'
    $dst2 = Join-Path $testRoot 'case2-dst'
    New-TestHostKey $src2
    # Copy identical host key to target
    New-Item -ItemType Directory -Path $dst2 -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $src2 'hostkey') -Destination (Join-Path $dst2 'hostkey')

    # Source has stale state, target has live state
    Set-Content -LiteralPath (Join-Path $src2 'settings.json') -Value '{"port": 5001}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $dst2 'settings.json') -Value '{"port": 5002}' -Encoding UTF8

    $r2 = Invoke-StateAction "Copy-State '$src2' '$dst2' -OnlyWhenTargetIsFresh"
    $dst2Settings = if (Test-Path (Join-Path $dst2 'settings.json')) { Get-Content -LiteralPath (Join-Path $dst2 'settings.json') -Raw } else { '' }

    Check '2. Target with same identity is left untouched (-OnlyWhenTargetIsFresh)' ($r2.ExitCode -eq 0) "exit $($r2.ExitCode)"
    Check '...and live target settings are preserved' ($dst2Settings.Trim() -eq '{"port": 5002}') "settings=$($dst2Settings.Trim())"

    # ------------------------------------------------------------
    # 3. Target holding a DIFFERENT identity stops migration (exit code 7)
    # ------------------------------------------------------------
    $src3 = Join-Path $testRoot 'case3-src'
    $dst3 = Join-Path $testRoot 'case3-dst'
    New-TestHostKey $src3
    New-TestHostKey $dst3

    $fp3DstBefore = ((Invoke-StateAction "Get-StateInfo '$dst3' | ConvertTo-Json").Output | ConvertFrom-Json).fingerprint
    $r3 = Invoke-StateAction "Copy-State '$src3' '$dst3'"
    $fp3DstAfter = ((Invoke-StateAction "Get-StateInfo '$dst3' | ConvertTo-Json").Output | ConvertFrom-Json).fingerprint

    Check '3. Target with different identity stops migration' ($r3.ExitCode -eq 7) "exit $($r3.ExitCode) (expected 7)"
    Check '...and destination host key is NOT overwritten' ($fp3DstAfter -eq $fp3DstBefore) "dst fingerprint preserved"

    # ------------------------------------------------------------
    # 4. Two candidate user directories stop instead of choosing (exit code 10)
    # ------------------------------------------------------------
    $mockUsers = Join-Path $testRoot 'case4-users'
    $userADir = Join-Path $mockUsers 'UserA\AppData\Local\FastMediaSorterCompanion'
    $userBDir = Join-Path $mockUsers 'UserB\AppData\Local\FastMediaSorterCompanion'
    New-TestHostKey $userADir
    New-TestHostKey $userBDir

    $r4 = Invoke-StateAction "Resolve-UserDataDir '' '$mockUsers'"

    Check '4. Multiple candidate user directories stop migration' ($r4.ExitCode -eq 10) "exit $($r4.ExitCode) (expected 10)"
    Check '...and log explains multiple identities' ($r4.Output -match 'several user profiles hold a Fast Media Sorter share identity') ''

    # ------------------------------------------------------------
    # 5. A damaged identity stops at the inspection (exit code 6) and nothing is copied or overwritten
    #    (WORKER-LAUNCH section 1: "refuse corrupt keys"; the worker refuses to parse the key, Get-StateInfo turns that into Fail 6)
    # ------------------------------------------------------------
    $bad5 = Join-Path $testRoot 'case5-corrupt'
    New-Item -ItemType Directory -Path $bad5 -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $bad5 'hostkey') -Value 'this is not an ssh private key' -Encoding ASCII

    $r5a = Invoke-StateAction "Get-StateInfo '$bad5' | ConvertTo-Json"
    Check '5. A damaged host key makes the state inspection fail' ($r5a.ExitCode -eq 6) "exit $($r5a.ExitCode) (expected 6)"

    # 5b. a damaged SOURCE: the migration stops before it creates the destination
    $dst5b = Join-Path $testRoot 'case5b-dst'
    $r5b = Invoke-StateAction "Copy-State '$bad5' '$dst5b'"
    Check '...and a damaged source stops the migration (exit 6)' ($r5b.ExitCode -eq 6) "exit $($r5b.ExitCode) (expected 6)"
    Check '...and nothing was created at the destination' (-not (Test-Path -LiteralPath $dst5b)) ''

    # 5c. a damaged TARGET next to a good source: the migration stops and the damaged file is left as it was
    $src5c = Join-Path $testRoot 'case5c-src'
    $dst5c = Join-Path $testRoot 'case5c-dst'
    New-TestHostKey $src5c
    New-Item -ItemType Directory -Path $dst5c -Force | Out-Null
    $damaged = 'this is not an ssh private key either'
    Set-Content -LiteralPath (Join-Path $dst5c 'hostkey') -Value $damaged -Encoding ASCII
    $r5c = Invoke-StateAction "Copy-State '$src5c' '$dst5c'"
    $dst5cText = (Get-Content -LiteralPath (Join-Path $dst5c 'hostkey') -Raw).Trim()
    Check '...and a damaged target stops the migration (exit 6)' ($r5c.ExitCode -eq 6) "exit $($r5c.ExitCode) (expected 6)"
    Check '...and the damaged target is not overwritten' ($dst5cText -eq $damaged) ''

    # ------------------------------------------------------------
    # 6. Malformed worker flags are refused with a non-zero exit and leave no state behind
    #    (WORKER-LAUNCH section 1 test line: "malformed flags"; --inspect-datadir cannot be combined with --service)
    # ------------------------------------------------------------
    function Invoke-WorkerFlags([string[]]$flagArgs) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $worker
        # one command-line string, so the same code runs under Windows PowerShell 5.1 and pwsh 7 (no ArgumentList)
        $psi.Arguments = (($flagArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ')
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdout = $p.StandardOutput.ReadToEndAsync()
        $stderr = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(15000)) {
            try { $p.Kill() } catch { }
            return [pscustomobject]@{ ExitCode = -1; Text = 'timed out'; TimedOut = $true }
        }
        return [pscustomobject]@{ ExitCode = $p.ExitCode; Text = ("$($stdout.Result) $($stderr.Result)").Trim(); TimedOut = $false }
    }

    $empty6 = Join-Path $testRoot 'case6-empty'
    New-Item -ItemType Directory -Path $empty6 -Force | Out-Null
    $flagCases = @(
        @{ Name = '--datadir without a value';                  Args = @('--datadir') },
        @{ Name = '--inspect-datadir without a value';          Args = @('--inspect-datadir') },
        @{ Name = '--inspect-datadir together with --service';  Args = @('--inspect-datadir', $empty6, '--service') },
        @{ Name = 'an unknown flag';                            Args = @('--malformed-flag-for-the-launch-check') }
    )
    foreach ($case in $flagCases) {
        $run = Invoke-WorkerFlags $case.Args
        $refused = (-not $run.TimedOut) -and $run.ExitCode -ne 0
        $detail = "exit $($run.ExitCode) $($run.Text)"
        if ($detail.Length -gt 120) { $detail = $detail.Substring(0, 120) }
        Check ('6. The worker refuses ' + $case.Name) $refused $detail
    }
    Check '...and a refused flag left the inspected directory empty' (@(Get-ChildItem -LiteralPath $empty6 -Force).Count -eq 0) ''

} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# --- summary --------------------------------------------------
$failed = @($results | Where-Object { -not $_.Passed })
$passed = @($results | Where-Object { $_.Passed })
$summary = "$($passed.Count) passed, $($failed.Count) failed"

if ($failed.Count -gt 0) {
    Exit-Verdict 'ShareIdentityMigration' 1 "$($failed.Count) check(s) failed; $summary"
}
Exit-Verdict 'ShareIdentityMigration' 0 $summary
