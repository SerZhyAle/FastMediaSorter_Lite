#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fast Media Sorter for Windows - project scripts launcher
.DESCRIPTION
    Quick alias launcher for the common project scripts (analog of the mobile
    repo's a.ps1, remapped to this .NET/MSBuild project's tooling).

    Build / run (local "сборка" - never tags, free):
      b    - Local build (build.ps1): MSBuild Rebuild + deploy single-file exe + Companion
      bs   - Local build, SKIP the .NET 10 Companion (build.ps1 -SkipCompanion)
      bn   - Local build, SKIP pre-build housekeeping (build.ps1 -NoClean)
      ri   - Reinstall: stop, rebuild, redeploy and LAUNCH locally (reinstall.ps1)
      rit  - Reinstall and launch minimized to tray (reinstall.ps1 -Tray)
      rin  - Reinstall without launching (reinstall.ps1 -NoLaunch)

    Packaging (still local, no tag):
      bi   - Build the copy-anywhere offline installer setup.exe (tools\Build-Installer.ps1)
      bib  - ..plus the heavy tessdata_best OCR models (Build-Installer -IncludeBest)
      off  - Mirror the CI packaging flow -> portable ZIP + installer (tools\Build-OfflineRelease.ps1)
      msix - Build the Microsoft Store MSIX package (publishing\msix\build-msix.ps1)
      mss  - Build a sideload-testable self-signed MSIX (build-msix.ps1 -SelfSign)

    Release (PAID - pushes a v* tag -> billable GitHub Actions):
      rel  - Release DRY-RUN: local check-build, NO tag pushed (tools\Release.ps1)
      relp - Release for real: create + PUSH the v* tag (tools\Release.ps1 -Push)

    Utilities:
      t    - Run all unit-test suites (tools\Run-AllTests.ps1)
      ti   - ..including integration tests (Run-AllTests -Integration)
      cl   - Clean stage/dist/bin/temp build artifacts (tools\Clean-Build.ps1)
      ocr  - Prepare/refresh the offline OCR payload (tools\Prepare-OcrOfflinePayload.ps1)
      c    - git commit -a + push
      cc   - git commit -a (no push)
.EXAMPLE
    .\a.ps1 b
    .\a b
.EXAMPLE
    .\a.ps1 bi -IncludeBest -Open
    .\a.ps1 rel -Version 26.7.15.2030
    .\a.ps1 c "Fix overlay font sizing"
#>

param(
    [string]$Command = ''
)

# Everything after <Command> is forwarded verbatim to the target script (after its
# preset Args), e.g. `.\a.ps1 bi -Open` or `.\a.ps1 rel -Version 26.7.15.2030`.
# Reading the automatic $args (simple-script mode) rather than a declared
# ValueFromRemainingArguments parameter is deliberate: $args captures -flag tokens
# too, which an advanced-binding param would reject.
$Rest = $args

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot

# Script mapping.
#
# Args MUST be a hashtable, not a string array: `& $script @hashtableArgs` binds by
# name, so strings starting with `-` never leak into the first positional param.
# Empty hashtable means "no preset args". Each value is $true for a switch, or a
# string/int for a typed param.
$scripts = @{
    'b'    = @{ Path = 'build.ps1'; Args = @{} }
    'bs'   = @{ Path = 'build.ps1'; Args = @{ SkipCompanion = $true } }
    'bn'   = @{ Path = 'build.ps1'; Args = @{ NoClean = $true } }
    'ri'   = @{ Path = 'reinstall.ps1'; Args = @{} }
    'rit'  = @{ Path = 'reinstall.ps1'; Args = @{ Tray = $true } }
    'rin'  = @{ Path = 'reinstall.ps1'; Args = @{ NoLaunch = $true } }
    'bi'   = @{ Path = 'tools\Build-Installer.ps1'; Args = @{} }
    'bib'  = @{ Path = 'tools\Build-Installer.ps1'; Args = @{ IncludeBest = $true } }
    'off'  = @{ Path = 'tools\Build-OfflineRelease.ps1'; Args = @{} }
    'msix' = @{ Path = 'publishing\msix\build-msix.ps1'; Args = @{} }
    'mss'  = @{ Path = 'publishing\msix\build-msix.ps1'; Args = @{ SelfSign = $true } }
    'rel'  = @{ Path = 'tools\Release.ps1'; Args = @{} }
    'relp' = @{ Path = 'tools\Release.ps1'; Args = @{ Push = $true } }
    't'    = @{ Path = 'tools\Run-AllTests.ps1'; Args = @{} }
    'ti'   = @{ Path = 'tools\Run-AllTests.ps1'; Args = @{ Integration = $true } }
    'cl'   = @{ Path = 'tools\Clean-Build.ps1'; Args = @{} }
    'ocr'  = @{ Path = 'tools\Prepare-OcrOfflinePayload.ps1'; Args = @{} }
}

# git shortcuts (no dedicated scripts in this repo, unlike the mobile one).
$gitCommands = @('c', 'cc')

function Show-Help {
    Write-Host "Fast Media Sorter for Windows - scripts launcher" -ForegroundColor Green
    Write-Host ""
    Write-Host "Build / run (local build - free, never tags):" -ForegroundColor Yellow
    Write-Host "  b    - Local build (MSBuild Rebuild + deploy exe + Companion)" -ForegroundColor Cyan
    Write-Host "  bs   - Local build, skip the .NET 10 Companion" -ForegroundColor Cyan
    Write-Host "  bn   - Local build, skip pre-build housekeeping" -ForegroundColor Cyan
    Write-Host "  ri   - Reinstall: stop, rebuild, redeploy and LAUNCH" -ForegroundColor Cyan
    Write-Host "  rit  - Reinstall and launch minimized to tray" -ForegroundColor Cyan
    Write-Host "  rin  - Reinstall without launching" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Packaging (local, no tag):" -ForegroundColor Yellow
    Write-Host "  bi   - Copy-anywhere offline installer setup.exe" -ForegroundColor Cyan
    Write-Host "  bib  - ..plus heavy tessdata_best OCR models" -ForegroundColor Cyan
    Write-Host "  off  - CI-style portable ZIP + installer" -ForegroundColor Cyan
    Write-Host "  msix - Microsoft Store MSIX package" -ForegroundColor Cyan
    Write-Host "  mss  - Sideload-testable self-signed MSIX" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Release (PAID - pushes a v* tag -> billable Actions):" -ForegroundColor Yellow
    Write-Host "  rel  - Release DRY-RUN (local check-build, no tag)" -ForegroundColor Cyan
    Write-Host "  relp - Release for real (create + PUSH the v* tag)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Utilities:" -ForegroundColor Yellow
    Write-Host "  t    - Run all unit-test suites" -ForegroundColor Cyan
    Write-Host "  ti   - ..including integration tests" -ForegroundColor Cyan
    Write-Host "  cl   - Clean stage/dist/bin/temp artifacts" -ForegroundColor Cyan
    Write-Host "  ocr  - Prepare/refresh the offline OCR payload" -ForegroundColor Cyan
    Write-Host "  c    - git commit -a + push" -ForegroundColor Cyan
    Write-Host "  cc   - git commit -a (no push)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:   .\a.ps1 <command> [extra script args..]" -ForegroundColor Gray
    Write-Host "Example: .\a.ps1 b" -ForegroundColor Gray
    Write-Host "Example: .\a.ps1 bi -Open" -ForegroundColor Gray
}

# --- git shortcuts -----------------------------------------------------------
if ($gitCommands -contains $Command) {
    Push-Location $ProjectRoot
    try {
        # A commit message can be passed positionally: `.\a.ps1 c "msg"`. Anything
        # else in $Rest is forwarded to `git commit` verbatim.
        $msg = $null
        $extra = @()
        foreach ($tok in $Rest) {
            if ($null -eq $msg -and $tok -isnot [System.Management.Automation.SwitchParameter] -and $tok -notlike '-*') {
                $msg = [string]$tok
            } else {
                $extra += $tok
            }
        }
        if (-not $msg) { $msg = "WIP" }

        Write-Host "git commit -a -m `"$msg`"" -ForegroundColor Green
        git commit -a -m $msg @extra
        $commitExit = $LASTEXITCODE
        if ($commitExit -ne 0) { exit $commitExit }

        if ($Command -eq 'c') {
            Write-Host "git push" -ForegroundColor Green
            git push
            exit $LASTEXITCODE
        }
        exit 0
    }
    finally {
        Pop-Location
    }
}

# --- script dispatch ---------------------------------------------------------
if (-not $scripts.ContainsKey($Command)) {
    if ($Command) { Write-Host "Unknown command: $Command" -ForegroundColor Red; Write-Host "" }
    Show-Help
    exit $(if ($Command) { 1 } else { 0 })
}

$scriptEntry = $scripts[$Command]
$scriptPath  = Join-Path $ProjectRoot $scriptEntry.Path
$scriptArgs  = $scriptEntry.Args

if (-not (Test-Path $scriptPath)) {
    Write-Host "Script not found: $scriptPath" -ForegroundColor Red
    exit 1
}

# Extra safety for the one paid command: require an explicit confirmation.
if ($Command -eq 'relp') {
    Write-Host "'relp' will PUSH a v* tag -> a BILLABLE GitHub Actions release run." -ForegroundColor Red
    $answer = Read-Host "Type 'release' to proceed"
    if ($answer -ne 'release') {
        Write-Host "Aborted (nothing pushed)." -ForegroundColor Yellow
        exit 1
    }
}

$argsDisplay = ($scriptArgs.GetEnumerator() | ForEach-Object {
    if ($_.Value -is [bool]) { "-$($_.Key)" } else { "-$($_.Key) $($_.Value)" }
}) -join ' '
Write-Host "Executing: $($scriptEntry.Path) $argsDisplay $($Rest -join ' ')" -ForegroundColor Green
Write-Host ""

& $scriptPath @scriptArgs @Rest

exit $LASTEXITCODE
