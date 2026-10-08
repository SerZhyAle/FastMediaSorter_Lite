<#
.SYNOPSIS
    Fixture self-test of tools/Test-ArtifactVersion.ps1 (specification 183 section 1): the check passes a
    right version and fails, for the right reason, each way an artifact can carry the wrong one.

.DESCRIPTION
    Builds throw-away executables under %TEMP% with the .NET Framework C# compiler (so each carries a real
    version resource of the stamp it is given), and throw-away MSIX-shaped ZIP files, then runs the check
    against them as a child process and expects an exit code and a message per case. The case this exists
    for is a stamp whose HHmm is below 1000: the resource keeps 26.7.30.0108 as 26.7.30.108, and a textual
    compare of the file-version string is what regressed on 2026-10-03. Nothing in the repository is read
    or written. About a dozen child processes; run it after a change to the check, not at every closure.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'ArtifactVersion' 2 'the self-test could not run' }
Write-Subject 'ArtifactVersion' 'tools/Test-ArtifactVersion.ps1 against throw-away executables and MSIX-shaped archives'

$Check = Join-Path $RepoRoot 'tools\Test-ArtifactVersion.ps1'
$pwsh = (Get-Process -Id $PID).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { Exit-Verdict 'ArtifactVersion' 2 'the .NET Framework C# compiler (csc.exe) was not found' }
$work = Join-Path ([IO.Path]::GetTempPath()) ("fms-artifact-version-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

# An exe whose version resource holds exactly these four numbers ($null = no version attribute at all).
function New-Exe([string]$name, [string]$version) {
    $src = Join-Path $work "$name.cs"
    $attr = if ($version) { "[assembly: System.Reflection.AssemblyFileVersion(`"$version`")]`n[assembly: System.Reflection.AssemblyVersion(`"$version`")]`n" } else { '' }
    [IO.File]::WriteAllText($src, "${attr}class P { static void Main() { } }`n")
    $exe = Join-Path $work "$name.exe"
    $out = & $csc /nologo /target:exe "/out:$exe" $src 2>&1
    if ($LASTEXITCODE -ne 0) { throw "csc failed for ${name}: $out" }
    $exe
}

# An MSIX-shaped archive: AppxManifest.xml with this Identity Version (or none), nothing else.
function New-Msix([string]$name, [string]$identityVersion, [bool]$withManifest = $true) {
    $path = Join-Path $work "$name.msix"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
    try {
        $entryName = if ($withManifest) { 'AppxManifest.xml' } else { 'readme.txt' }
        $entry = $zip.CreateEntry($entryName)
        $w = [IO.StreamWriter]::new($entry.Open())
        try {
            if ($withManifest) {
                $w.Write("<?xml version=`"1.0`" encoding=`"utf-8`"?><Package xmlns=`"http://schemas.microsoft.com/appx/manifest/foundation/windows10`"><Identity Name=`"X.Y`" Publisher=`"CN=Z`" Version=`"$identityVersion`" ProcessorArchitecture=`"x64`" /></Package>")
            } else { $w.Write('no manifest') }
        } finally { $w.Dispose() }
    } finally { $zip.Dispose() }
    $path
}

$failures = [Collections.Generic.List[string]]::new()
function Expect([string]$name, [int]$code, [string]$message, [string[]]$arguments) {
    $out = @(& $pwsh -NoProfile -File $Check @arguments 2>&1 | ForEach-Object { "$_" })
    $got = $LASTEXITCODE
    $text = $out -join "`n"
    $ok = ($got -eq $code) -and (-not $message -or $text.Contains($message))
    if ($ok) { Write-Host "  ok    $name" -ForegroundColor Green }
    else { Write-Host "  WRONG $name (wanted exit $code '$message', got exit $got)" -ForegroundColor Red; $text -split "`n" | ForEach-Object { Write-Host "        $_" }; $failures.Add($name) }
}

try {
    $early  = New-Exe 'early'  '26.7.30.108'       # stamped 26.7.30.0108
    $late   = New-Exe 'late'   '26.7.30.1080'      # stamped 26.7.30.1080
    $stale  = New-Exe 'stale'  '26.7.29.2359'      # the previous build, left behind
    $bare   = New-Exe 'bare'   $null               # a publish that was passed no version
    $nov    = New-Exe 'oct'    '26.10.2.59'        # October: the remap is 26.1002.59.0

    Expect 'a stamp with HHmm below 1000 passes (0108 is 108)' 0 'Test-ArtifactVersion: PASS' @('-Expect', '26.7.30.0108', '-Path', $early)
    Expect 'the same exe fails against the stamp 1080' 1 'carries 26.7.30.108, not 26.7.30.1080' @('-Expect', '26.7.30.1080', '-Path', $early)
    Expect 'a stale exe fails and is named' 1 'stale.exe carries 26.7.29.2359' @('-Expect', '26.7.30.1080', '-Path', $stale)
    Expect 'an exe with no version fails' 1 'carries no version resource' @('-Expect', '26.7.30.0108', '-Path', $bare)
    Expect 'a comma-joined list (as the release preflight passes it) is split' 0 'Test-ArtifactVersion: PASS (2 file(s)' @('-Expect', '26.7.30.0108', '-Path', "$early,$early")
    Expect 'one stale file in a comma-joined list fails the whole' 1 'Test-ArtifactVersion: FAIL (1 of 2' @('-Expect', '26.7.30.1080', '-Path', "$late,$stale")
    # build.ps1 calls the check in-process with an array; `-File` cannot bind one, so this case uses -Command.
    $arrayOut = @(& $pwsh -NoProfile -Command "& '$Check' -Expect 26.7.30.1080 -Path @('$late','$late'); exit `$LASTEXITCODE" 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -eq 0 -and ($arrayOut -join "`n").Contains('Test-ArtifactVersion: PASS (2 file(s)')) { Write-Host '  ok    two paths as an array (as build.ps1 passes them) pass' -ForegroundColor Green }
    else { Write-Host "  WRONG two paths as an array (exit $LASTEXITCODE)" -ForegroundColor Red; $arrayOut | ForEach-Object { Write-Host "        $_" }; $failures.Add('an array of paths') }
    Expect 'a missing file is could-not-verify, never a pass' 2 'COULD NOT VERIFY (1 of 1 file(s) not found)' @('-Expect', '26.7.30.0108', '-Path', (Join-Path $work 'nope.exe'))
    Expect 'a stamp that is not YY.M.D.HHmm is could-not-verify' 2 'is not a YY.M.D.HHmm stamp' @('-Expect', '26.7.30.108', '-Path', $early)
    Expect 'no file named is could-not-verify' 2 'no file named in -Path' @('-Expect', '26.7.30.0108')

    $msixOk    = New-Msix 'ok'    '26.730.108.0'
    $msixOct   = New-Msix 'oct'   '26.1002.59.0'
    $msixOld   = New-Msix 'old'   '26.729.2359.0'
    $msixEmpty = New-Msix 'empty' '' $false
    Expect 'an MSIX at the remap of the stamp passes' 0 'carries 26.730.108.0' @('-Expect', '26.7.30.0108', '-Msix', '-Path', $msixOk)
    Expect 'the remap carries M*100+D for a two-digit month' 0 'carries 26.1002.59.0' @('-Expect', '26.10.2.0059', '-Msix', '-Path', $msixOct)
    Expect 'an MSIX of the previous build fails' 1 'carries 26.729.2359.0, not 26.730.108.0' @('-Expect', '26.7.30.0108', '-Msix', '-Path', $msixOld)
    Expect 'an archive with no manifest fails' 1 'no readable AppxManifest.xml' @('-Expect', '26.7.30.0108', '-Msix', '-Path', $msixEmpty)
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Exit-Verdict 'ArtifactVersion' 1 "$($failures.Count) case(s) wrong: $($failures -join '; ')"
}
Exit-Verdict 'ArtifactVersion' 0 'the check passes a right version and fails each wrong one for its own reason'
