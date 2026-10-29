<#
.SYNOPSIS
    Proves on the BUILT artifacts that the WebView2 host of the EPUB reader (274) and the HTML page
    viewer (275) ships in the shape specification 275 section 9.2 requires.

.DESCRIPTION
    A package list proves nothing about the loader: NuGet flattens a native asset into the publish
    root unless told not to, the package's own targets add runtimes\win-*\native copies, and the
    packagers copy loose files by wildcard. So this reads the artifacts, for every folder in -Path:

      1. Beside FastMediaSorter_LITE.exe: webview2\win-x64\WebView2Loader.dll.
      2. That loader is a PE32+ image for AMD64 (machine 0x8664, optional header 0x20B) - a file
         that merely exists under the right name is not enough.
      3. Its file version is the Microsoft.Web.WebView2 version pinned in
         src\Modern\FastMediaSorter.Modern.vbproj, and when that package is in the NuGet cache its
         bytes equal the package's runtimes\win-x64\native\WebView2Loader.dll (provenance).
      4. No other WebView2Loader.dll in the folder except the legacy 32-bit one the net48 build
         keeps under runtimes\win-x86\native (owner decision, dead wiring): no root copy, no
         second x64 copy - the viewer points the SDK at webview2\win-x64 and nowhere else.
      5. The mainline's single-file bundle carries Microsoft.Web.WebView2.Core.dll,
         Microsoft.Web.WebView2.WinForms.dll and AngleSharp.dll, and not the unused
         Microsoft.Web.WebView2.Wpf.dll (RemoveUnusedWebViewWpfReference).
      6. A folder without the mainline (the Manager-only Server stage) gains no browser component:
         no webview2\ tree, no WebView2Loader.dll other than rule 4's legacy one, and
         FastMediaSorterCompanion.exe does not carry Microsoft.Web.WebView2.Core.dll.
      7. A THIRD-PARTY-NOTICES.txt beside the mainline names the WebView2 SDK and AngleSharp at
         their pinned versions.

    Runs where the artifacts are produced: build.ps1 on bin\Release before anything is deployed,
    tools\Build-Installer.ps1 on its stage before ISCC, tools\Release.ps1 on the staged packages,
    publishing\msix\build-msix.ps1 on its stage - see tools\check-placement.jsonl. release.yml
    refuses a publish output without the loader while it stages (the mirror the preflight judges
    is Build-OfflineRelease's).

.PARAMETER Path
    The folders to judge. `pwsh -File` binds "a,b" as one string, so commas separate folders too.

.NOTES
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL (a finding), 2 COULD NOT VERIFY (no folder named, a
    named folder missing, a folder with neither the mainline nor the Share Manager in it, or no
    pinned version in the modern project).
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Path)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
$check = 'Test-HtmlHostArtifacts'

$folders = @($Path | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($folders.Count -eq 0) { Exit-Verdict $check 2 'no folder named' }
foreach ($f in $folders) { if (-not (Test-Path -LiteralPath $f -PathType Container)) { Exit-Verdict $check 2 "folder not found: $f" } }

$project = [IO.File]::ReadAllText((Join-Path $repo 'src\Modern\FastMediaSorter.Modern.vbproj'))
function Get-Pin([string]$Package) {
    $m = [regex]::Match($project, '<PackageReference\s+Include="' + [regex]::Escape($Package) + '"\s+Version="([^"]+)"')
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}
$sdkVersion = Get-Pin 'Microsoft.Web.WebView2'
$parserVersion = Get-Pin 'AngleSharp'
if (-not $sdkVersion -or -not $parserVersion) { Exit-Verdict $check 2 'no Microsoft.Web.WebView2 or AngleSharp pin in src\Modern\FastMediaSorter.Modern.vbproj' }
Write-Subject $check ("{0}; WebView2 SDK {1}, AngleSharp {2}" -f ($folders -join ', '), $sdkVersion, $parserVersion)

$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$pinnedLoader = Join-Path $cache "microsoft.web.webview2\$sdkVersion\runtimes\win-x64\native\WebView2Loader.dll"
$provenance = if (Test-Path -LiteralPath $pinnedLoader) { 'bytes and version' } else { 'version only (package not in the NuGet cache)' }

# Latin-1 maps every byte to one char, so an ordinal string search is a byte search.
$latin1 = [Text.Encoding]::Latin1
function Test-Carries([string]$File, [string]$Name) {
    return $latin1.GetString([IO.File]::ReadAllBytes($File)).IndexOf($Name, [StringComparison]::Ordinal) -ge 0
}
function Get-PeShape([string]$File) {
    $bytes = [IO.File]::ReadAllBytes($File)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { return 'not a PE image' }
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($pe -lt 0 -or $pe + 0x1A -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) { return 'not a PE image' }
    return ('machine 0x{0:X4}, optional header 0x{1:X3}' -f [BitConverter]::ToUInt16($bytes, $pe + 4), [BitConverter]::ToUInt16($bytes, $pe + 0x18))
}
$x64Shape = 'machine 0x8664, optional header 0x20B'
$legacyRelative = 'runtimes\win-x86\native\WebView2Loader.dll'
$loaderRelative = 'webview2\win-x64\WebView2Loader.dll'

$findings = [Collections.Generic.List[string]]::new()
$inspected = 0
foreach ($folder in $folders) {
    $root = (Resolve-Path -LiteralPath $folder).Path
    $lite = Join-Path $root 'FastMediaSorter_LITE.exe'
    $manager = Join-Path $root 'FastMediaSorterCompanion.exe'
    $hasLite = Test-Path -LiteralPath $lite
    $hasManager = Test-Path -LiteralPath $manager
    if (-not $hasLite -and -not $hasManager) { Exit-Verdict $check 2 "neither FastMediaSorter_LITE.exe nor FastMediaSorterCompanion.exe is in $root" }
    $inspected++

    # Rule 4 (and rule 6's loader half): every loader outside its one place must be the legacy x86 one.
    # A nested folder with its own mainline (the Server stage's viewer\) is a viewer of its own,
    # judged when it is named itself, so its files are not this folder's strays.
    $nested = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'FastMediaSorter_LITE.exe' |
        Where-Object { $_.DirectoryName -ne $root } | ForEach-Object { $_.DirectoryName + '\' })
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'WebView2Loader.dll')) {
        $owned = $false
        foreach ($n in $nested) { if ($file.FullName.StartsWith($n, [StringComparison]::OrdinalIgnoreCase)) { $owned = $true } }
        if ($owned) { continue }
        $relative = $file.FullName.Substring($root.Length).TrimStart('\')
        if ($hasLite -and $relative -eq $loaderRelative) { continue }
        if ($relative -eq $legacyRelative -and (Get-PeShape $file.FullName) -like 'machine 0x014C*') { continue }
        $findings.Add("${root}: stray $relative ($(Get-PeShape $file.FullName)) - the only loader is $loaderRelative beside the mainline")
    }

    if ($hasLite) {
        $loader = Join-Path $root $loaderRelative
        if (-not (Test-Path -LiteralPath $loader)) {
            $findings.Add("${root}: $loaderRelative missing beside FastMediaSorter_LITE.exe")
        } else {
            $shape = Get-PeShape $loader
            if ($shape -ne $x64Shape) { $findings.Add("${root}: $loaderRelative is not an x64 PE32+ image ($shape)") }
            $version = (Get-Item -LiteralPath $loader).VersionInfo.FileVersion
            if ($version -ne $sdkVersion) { $findings.Add("${root}: $loaderRelative is version '$version', the pinned SDK is $sdkVersion") }
            if ((Test-Path -LiteralPath $pinnedLoader) -and (Get-FileHash -LiteralPath $loader).Hash -ne (Get-FileHash -LiteralPath $pinnedLoader).Hash) {
                $findings.Add("${root}: $loaderRelative is not the pinned SDK's runtimes\win-x64\native\WebView2Loader.dll")
            }
        }
        foreach ($name in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'AngleSharp.dll') {
            if (-not (Test-Carries $lite $name)) { $findings.Add("${root}: FastMediaSorter_LITE.exe does not bundle $name") }
        }
        if (Test-Carries $lite 'Microsoft.Web.WebView2.Wpf.dll') { $findings.Add("${root}: FastMediaSorter_LITE.exe bundles the unused Microsoft.Web.WebView2.Wpf.dll") }

        $notices = Join-Path $root 'THIRD-PARTY-NOTICES.txt'
        if (Test-Path -LiteralPath $notices) {
            $text = [IO.File]::ReadAllText($notices)
            if ($text -notmatch ('WebView2 SDK[^\r\n]*\r?\n\s*Version ' + [regex]::Escape($sdkVersion))) {
                $findings.Add("${root}: THIRD-PARTY-NOTICES.txt does not name the WebView2 SDK $sdkVersion")
            }
            if ($text -notmatch ('AngleSharp[^\r\n]*\r?\n\s*Version ' + [regex]::Escape($parserVersion))) {
                $findings.Add("${root}: THIRD-PARTY-NOTICES.txt does not name AngleSharp $parserVersion")
            }
        }
    } else {
        if (Test-Path -LiteralPath (Join-Path $root 'webview2')) { $findings.Add("${root}: a webview2\ tree in a stage without the viewer") }
    }
    if ($hasManager -and (Test-Carries $manager 'Microsoft.Web.WebView2.Core.dll')) {
        $findings.Add("${root}: FastMediaSorterCompanion.exe carries Microsoft.Web.WebView2.Core.dll - the Share Manager has no browser component")
    }
}

if ($findings.Count) {
    foreach ($f in $findings) { Write-Host "  $f" -ForegroundColor Red }
    Exit-Verdict $check 1 "$($findings.Count) finding(s) in $($folders.Count) folder(s)"
}
Exit-Verdict $check 0 "$($folders.Count) folder(s), $inspected judged: one x64 loader in webview2\win-x64, provenance by $provenance, bundles and notices agree"
