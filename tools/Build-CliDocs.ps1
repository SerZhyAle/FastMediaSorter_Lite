<#
.SYNOPSIS
  Renders docs/cli-commands.json, docs/cli-commands.share-manager.json, docs/CLI.md and
  docs/guides/COMMAND_LINE_RECIPES.md from the one command table of each program in code
  (src/AppCommands/ViewerCommandTable.vb and ShareManagerCommandTable.vb; contract APP-COMMANDS rules 18 and 20).

.DESCRIPTION
  Compiles the pure profile library with the Roslyn vbc of the installed Visual Studio (about 1 s) into
  %TEMP%\fms-cli-docs and runs it. -Check writes nothing and exits 1 when a document is stale.
  Exit 0 = up to date or written, 1 = stale (with -Check), 2 = could not verify (no compiler).
  The same comparison runs as AppCommandsDocsTests in tests/Lite.Tests.
#>
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vbc = $null
if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -property installationPath
    if ($vs) { $vbc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\vbc.exe' }
}
if (-not $vbc -or -not (Test-Path $vbc)) { Write-Output 'Build-CliDocs: COULD NOT VERIFY (no Roslyn vbc.exe)'; exit 2 }
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$work = Join-Path ([IO.Path]::GetTempPath()) 'fms-cli-docs'
New-Item -ItemType Directory -Force $work | Out-Null
$exe = Join-Path $work 'CliDocsGenerator.exe'
$src = @(
    (Join-Path $root 'src\ProductNames.vb'),
    (Join-Path $root 'src\AppCommands\AppCommandsProfile.vb'),
    (Join-Path $root 'src\AppCommands\ViewerCommandTable.vb'),
    (Join-Path $root 'src\AppCommands\ShareManagerCommandTable.vb'),
    (Join-Path $root 'src\AppCommands\CliDocument.vb'),
    (Join-Path $root 'src\AppCommands\CliRecipes.vb'),
    (Join-Path $PSScriptRoot 'cli-docs\CliDocsGenerator.vb'))
$vbcArgs = @('-nologo', '-noconfig', '-target:exe', "-out:$exe", '-optionstrict+', '-optionexplicit+', '-optioninfer+',
    '-imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.IO', '-rootnamespace:fmsl', '-nostdlib',
    "-sdkpath:$fw", "-r:$fw\mscorlib.dll", "-r:$fw\System.dll", "-r:$fw\System.Core.dll", "-r:$fw\Microsoft.VisualBasic.dll",
    '-main:fmsl.CliDocsGenerator')
& $vbc @vbcArgs @src
if ($LASTEXITCODE -ne 0) { Write-Output 'Build-CliDocs: COULD NOT VERIFY (compile failed)'; exit 2 }
& $exe (Join-Path $root 'docs') ($(if ($Check) { 'check' } else { 'write' }))
$code = $LASTEXITCODE
Write-Output ("Build-CliDocs: " + $(if ($code -eq 0) { 'PASS' } else { 'FAIL (stale - run tools\Build-CliDocs.ps1)' }))
exit $code
