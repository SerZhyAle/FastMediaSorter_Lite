<#
.SYNOPSIS
    Proves on the BUILT artifacts that the image backend is what specification 270 says it is:
    no ImageSharp anywhere, and every native library the replacement needs is present.

.DESCRIPTION
    A clean package list proves nothing about a folder that an earlier build left a DLL in, nor
    about what a single-file bundle or the x86 exe's embedded payload carries. So this reads the
    artifacts themselves, for every folder named in -Path:

      1. No file whose name contains "ImageSharp" or starts with "SixLabors".
      2. No .exe or .dll that carries the string "SixLabors.ImageSharp" - which catches a managed
         reference, a FmsPayloadmanaged/ resource of the x86 exe and an entry of a single-file
         bundle's manifest alike (ASCII and UTF-16 are both searched).
      3. Beside FastMediaSorter_x86.exe: x86\libwebpdecoder.dll, byte-identical to the pinned
         payload\libwebp\x86\libwebpdecoder.dll of this tree (the fallback's only WEBP decoder on
         Windows 7/8.1). The x64 copy is for the net48 test leg and is not required.
      4. Beside FastMediaSorter_LITE.exe: Magick.Native-Q8-x64.dll (the mainline publishes it loose).
      5. FastMediaSorterCompanion.exe carries Magick.Native-Q8-x64.dll inside its single-file bundle
         (IncludeNativeLibrariesForSelfExtract), so a Manager-only Server installation needs nothing
         of the viewer's payload.
      6. A THIRD-PARTY-NOTICES.txt in the folder names libwebp when the x86 exe is there and
         Magick.NET when the mainline or the Manager is, and does not name SixLabors.ImageSharp.

    Runs where the artifacts are produced: build.ps1 on bin\Release before anything is deployed,
    tools\Release.ps1 on the staged packages, publishing\msix\build-msix.ps1 on its stage - see
    tools\check-placement.jsonl.

.PARAMETER Path
    The folders to judge. `pwsh -File` binds "a,b" as one string, so commas separate folders too.

.PARAMETER AllowRollbackCopies
    Skip <name>.previous-<stamp>.exe, the copy build.ps1 keeps of the exe it replaced. Only for
    bin\Release, the working folder; a package stage never carries one, so there it is a finding.

.NOTES
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL (a finding), 2 COULD NOT VERIFY (no folder named, a
    named folder missing, or a folder with none of the three programs in it).
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Path, [switch]$AllowRollbackCopies)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
$check = 'Test-ImageBackendArtifacts'

$folders = @($Path | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($folders.Count -eq 0) { Exit-Verdict $check 2 'no folder named' }
foreach ($f in $folders) { if (-not (Test-Path -LiteralPath $f -PathType Container)) { Exit-Verdict $check 2 "folder not found: $f" } }
Write-Subject $check ($folders -join ', ')

$needleAscii = [Text.Encoding]::ASCII.GetBytes('SixLabors.ImageSharp')
$needleUtf16 = [Text.Encoding]::Unicode.GetBytes('SixLabors.ImageSharp')
# Latin-1 maps every byte to one char, so an ordinal string search is a byte search.
$latin1 = [Text.Encoding]::Latin1
function Test-Carries([string]$File, [byte[]]$Needle) {
    $haystack = $latin1.GetString([IO.File]::ReadAllBytes($File))
    return $haystack.IndexOf($latin1.GetString($Needle), [StringComparison]::Ordinal) -ge 0
}
$pinnedWebp = Join-Path $repo 'payload\libwebp\x86\libwebpdecoder.dll'

$findings = [Collections.Generic.List[string]]::new()
$inspected = 0
foreach ($folder in $folders) {
    $root = (Resolve-Path -LiteralPath $folder).Path
    # build.ps1 keeps the replaced exe as <name>.previous-<stamp>.exe for a rollback - in bin\Release
    # only. In a stage it is a pre-migration program riding into the package, so it is judged there.
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { -not $AllowRollbackCopies -or $_.Name -notlike '*.previous-*' })
    $rel = { param($p) $p.Substring($root.Length).TrimStart('\') }

    foreach ($file in $files) {
        if ($file.Name -like '*ImageSharp*' -or $file.Name -like 'SixLabors*') { $findings.Add("${root}: file $(& $rel $file.FullName)") }
    }
    foreach ($file in $files | Where-Object { $_.Extension -in '.exe', '.dll' }) {
        if ((Test-Carries $file.FullName $needleAscii) -or (Test-Carries $file.FullName $needleUtf16)) {
            $findings.Add("${root}: $(& $rel $file.FullName) carries SixLabors.ImageSharp")
        }
    }

    $x86 = Join-Path $root 'FastMediaSorter_x86.exe'
    $lite = Join-Path $root 'FastMediaSorter_LITE.exe'
    $manager = Join-Path $root 'FastMediaSorterCompanion.exe'
    $programs = @($x86, $lite, $manager | Where-Object { Test-Path -LiteralPath $_ })
    if ($programs.Count -eq 0) { Exit-Verdict $check 2 "none of the three programs is in $root" }
    $inspected += $programs.Count

    if (Test-Path -LiteralPath $x86) {
        $webp = Join-Path $root 'x86\libwebpdecoder.dll'
        if (-not (Test-Path -LiteralPath $webp)) { $findings.Add("${root}: x86\libwebpdecoder.dll missing beside FastMediaSorter_x86.exe") }
        elseif ((Test-Path -LiteralPath $pinnedWebp) -and (Get-FileHash $webp).Hash -ne (Get-FileHash $pinnedWebp).Hash) {
            $findings.Add("${root}: x86\libwebpdecoder.dll is not the pinned payload\libwebp build")
        }
    }
    if ((Test-Path -LiteralPath $lite) -and -not (Test-Path -LiteralPath (Join-Path $root 'Magick.Native-Q8-x64.dll'))) {
        $findings.Add("${root}: Magick.Native-Q8-x64.dll missing beside FastMediaSorter_LITE.exe")
    }
    if ((Test-Path -LiteralPath $manager) -and -not (Test-Carries $manager ([Text.Encoding]::ASCII.GetBytes('Magick.Native-Q8-x64.dll')))) {
        $findings.Add("${root}: FastMediaSorterCompanion.exe does not bundle Magick.Native-Q8-x64.dll")
    }

    $notices = Join-Path $root 'THIRD-PARTY-NOTICES.txt'
    if (Test-Path -LiteralPath $notices) {
        $text = [IO.File]::ReadAllText($notices)
        if ($text -match 'SixLabors\.ImageSharp') { $findings.Add("${root}: THIRD-PARTY-NOTICES.txt still names SixLabors.ImageSharp") }
        if ((Test-Path -LiteralPath $x86) -and $text -notmatch 'libwebp') { $findings.Add("${root}: THIRD-PARTY-NOTICES.txt does not name libwebp") }
        if (((Test-Path -LiteralPath $lite) -or (Test-Path -LiteralPath $manager)) -and $text -notmatch 'Magick\.NET') { $findings.Add("${root}: THIRD-PARTY-NOTICES.txt does not name Magick.NET") }
    }
}

if ($findings.Count) {
    foreach ($f in $findings) { Write-Host "  $f" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($findings.Count) finding(s) in $($folders.Count) folder(s)"
}
Exit-Verdict $check 0 "$($folders.Count) folder(s), $inspected program(s): no ImageSharp, natives present"
