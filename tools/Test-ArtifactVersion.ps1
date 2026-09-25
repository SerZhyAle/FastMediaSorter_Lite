<#
.SYNOPSIS
    Proves that built executables carry the version they are judged at (contract BUILD-EVIDENCE rule 2).

.DESCRIPTION
    Reads the version resource of every file named in -Path - the viewers, the Share Manager, the Inno
    setup.exe files - and compares its four numeric parts with -Expect.

    Only the artifact can answer this. A stale exe agrees with every check that reads the version out of
    the source, because the source is exactly what it was built from; the failures this exists for are an
    exe left over from an earlier build (the previous stamp) and a publish that was passed no version at
    all (1.0.0.0, the SDK default).

    The comparison is numeric, never textual: the resource keeps the stamp 26.7.30.0108 as the parts
    26.7.30.108, and the string field is whatever the compiler copied.

    Runs where the artifacts are produced (build.ps1, on the exes it just built) and in the release
    preflight (tools\Release.ps1, on the staged package and both setup.exe files) - see
    tools\check-placement.jsonl.

.PARAMETER Expect
    The stamp the build was given, YY.M.D.HHmm (the tag without its "v").

.PARAMETER Path
    The files to judge. `pwsh -File` binds "a,b" as one string, so commas separate files here too.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every file carries -Expect;
      1 - a file carries another version, or no version resource at all;
      2 - could not verify: -Expect is not a YY.M.D.HHmm stamp, no file was named, or a named file
          does not exist.
    The last line is the verdict, `Test-ArtifactVersion: <WORD> (..)`.

.EXAMPLE
    .\tools\Test-ArtifactVersion.ps1 -Expect 26.9.25.1700 -Path bin\Release\FastMediaSorter_LITE.exe,bin\Release\FastMediaSorter_x86.exe
#>
[CmdletBinding()]
param(
    [string]$Expect = '',
    [string[]]$Path = @()
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Test-ArtifactVersion' 2 'the check itself failed'
}

$files = @($Path | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
Write-Subject 'Test-ArtifactVersion' "$($files.Count) file(s), expected version '$Expect'"

if ($Expect -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    Exit-Verdict 'Test-ArtifactVersion' 2 "-Expect '$Expect' is not a YY.M.D.HHmm stamp"
}
if ($files.Count -eq 0) {
    Exit-Verdict 'Test-ArtifactVersion' 2 'no file named in -Path'
}
$missing = @($files | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    $missing | ForEach-Object { Write-Host "  missing: $_" }
    Exit-Verdict 'Test-ArtifactVersion' 2 "$($missing.Count) of $($files.Count) file(s) not found"
}

$wantText = (@($Expect.Split('.') | ForEach-Object { [int]$_ })) -join '.'   # 0108 -> 108
$wrong = @()
foreach ($file in $files) {
    $info = (Get-Item -LiteralPath $file).VersionInfo
    $got = @($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
    $gotText = $got -join '.'
    $leaf = Split-Path $file -Leaf
    if (($got | Measure-Object -Sum).Sum -eq 0) {
        Write-Host "  $leaf carries no version resource"
        $wrong += $leaf
    } elseif ($gotText -ne $wantText) {
        $hint = if ($gotText -eq '1.0.0.0') { ' - the SDK default: the build passed it no version' } else { '' }
        Write-Host "  $leaf carries $gotText, not $Expect$hint"
        $wrong += $leaf
    } else {
        Write-Host "  $leaf carries $Expect"
    }
}

if ($wrong.Count -gt 0) {
    Exit-Verdict 'Test-ArtifactVersion' 1 "$($wrong.Count) of $($files.Count) file(s) not at $Expect`: $($wrong -join ', ')"
}
Exit-Verdict 'Test-ArtifactVersion' 0 "$($files.Count) file(s) at $Expect"
