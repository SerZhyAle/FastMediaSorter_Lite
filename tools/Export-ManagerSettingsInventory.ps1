<#
.SYNOPSIS
    Regenerates, or checks, the Share Manager settings inventory (APP-SETTINGS rule 11, ticket L0333).

.DESCRIPTION
    docs/guides/SHARE_MANAGER_SETTINGS_INVENTORY.md lists every setting the Share Manager settings search
    finds - page, group, caption, editor and default - read from the built windows, never typed. The
    source is the windows themselves: tests/Companion.Tests/ManagerSettingsInventoryTests.vb builds the
    Manager and the local exchange server settings windows on an STA thread over a scratch settings hive
    (the exchange component and the SFTP worker faked, nothing shown, no profile read or written) and
    walks the rows of Share_Settings_Form.SettingsSearchEntries. This script runs that one fact:

      (default)  sets FMS_SETTINGS_INVENTORY_WRITE=1, so the fact writes the document;
      -Check     leaves the variable unset, so the fact compares the built windows with the committed
                 document and fails on any difference (a setting added, renamed, moved or given another
                 default without regenerating).

    It restores and compiles the Share Manager test project, so it is a release-class run or a run by
    hand after a change to a settings page - never a between-releases check (TESTING_AND_QA section 2).
    The same fact is selected by tools/Test-UiForms.ps1 -Forms Share_Settings_Form.

.PARAMETER Check
    Compare only; write nothing.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - the document was regenerated, or -Check found it equal to the built windows;
      1 - -Check found a difference, or the fact failed;
      2 - could not verify: no dotnet, no test project, or the test run left no result line.
    The last line is the verdict, `Export-ManagerSettingsInventory: <WORD> (..)`.
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Export-ManagerSettingsInventory' 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests\Companion.Tests\Companion.Tests.vbproj'
$document = Join-Path $root 'docs\guides\SHARE_MANAGER_SETTINGS_INVENTORY.md'
$variable = 'FMS_SETTINGS_INVENTORY_WRITE'
$mode = if ($Check) { 'check' } else { 'write' }

Write-Subject 'Export-ManagerSettingsInventory' "${mode}: the built Share Manager settings windows against docs/guides/SHARE_MANAGER_SETTINGS_INVENTORY.md; $(Get-TreeLabel $root)"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Exit-Verdict 'Export-ManagerSettingsInventory' 2 'dotnet not found' }
if (-not (Test-Path -LiteralPath $project)) { Exit-Verdict 'Export-ManagerSettingsInventory' 2 'tests\Companion.Tests\Companion.Tests.vbproj not found' }

$dotnetArgs = @('test', $project, '-v', 'minimal', '--nologo', '--filter', 'FullyQualifiedName~ManagerSettingsInventoryTests')
$previous = [Environment]::GetEnvironmentVariable($variable, 'Process')
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    [Environment]::SetEnvironmentVariable($variable, $(if ($Check) { $null } else { '1' }), 'Process')
    $log = @(& dotnet @dotnetArgs 2>&1 | ForEach-Object { $text = "$_"; Write-Host $text; $text })
    $exit = $LASTEXITCODE
} finally {
    [Environment]::SetEnvironmentVariable($variable, $previous, 'Process')
    $ErrorActionPreference = $previousPreference
}

$result = @($log | Select-String -Pattern '(Passed|Failed)!.*Total:\s*(\d+)' | Select-Object -Last 1)
if ($result.Count -ne 1 -or [int]$result[0].Matches[0].Groups[2].Value -lt 1) {
    Exit-Verdict 'Export-ManagerSettingsInventory' 2 "the inventory fact did not run (exit $exit, no result line with a test in it)"
}
if ($exit -ne 0) {
    $why = if ($Check) { 'the committed document differs from the built windows - run without -Check to regenerate' } else { 'the fact failed while writing' }
    Exit-Verdict 'Export-ManagerSettingsInventory' 1 $why
}
if (-not $Check) {
    $written = if (Test-Path -LiteralPath $document) { [IO.File]::ReadAllText($document) } else { '' }
    if (-not $written -or $written.Contains('<!-- inventory:not-generated -->')) {
        Exit-Verdict 'Export-ManagerSettingsInventory' 2 'the fact passed but the document was not written'
    }
    Exit-Verdict 'Export-ManagerSettingsInventory' 0 'regenerated docs/guides/SHARE_MANAGER_SETTINGS_INVENTORY.md; review its Default column and keep the result'
}
Exit-Verdict 'Export-ManagerSettingsInventory' 0 'the committed document equals the built windows'
