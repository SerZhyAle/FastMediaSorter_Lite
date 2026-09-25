<#
.SYNOPSIS
    The pre-publish checks of the winget and Microsoft Store channels (ticket 024): the committed winget
    manifests of both editions, the GitHub release they point at, and optionally the Store MSIX.

.DESCRIPTION
    Runs after the GitHub Release exists and before anything is sent to a channel - the winget-pkgs PR
    and the Partner Center upload - so a defect surfaces here and not after days in a queue. It does not
    replace the channels' own validation; the numbering follows the ticket's section 1.

      P1  winget validate, both editions. Each edition's three files are copied into a clean temporary
          directory first: winget accepts only a directory holding a complete set and no subdirectory,
          and publishing\winget holds server\.
      P2  InstallerSha256 of every installer manifest against the release it points at - the ".sha256"
          sidecar the same CI run published beside the setup.exe (-Strict downloads the setup.exe and
          hashes it instead). Never against dist\: a local build of the same version has other bytes,
          and that is not a mismatch.
      P3  Consistency: one PackageVersion in all six files (and -Expect, when given); one
          ManifestVersion, which each file's $schema line names too; the frozen PackageIdentifier and
          PackageName of each edition; Silent and SilentWithProgress switches on every installer; the
          InstallerUrl names the tag and the asset of that version; and none of the keys that failed
          validation before (Dependencies, NestedInstallerType; Scope and MinimumOSVersion in the User
          edition) - see docs/specifications/done/SPECIFICATION_WINGET_PUBLISHING.md.
      P4  WACK (-Wack only): appcert.exe from the Windows SDK on the MSIX of the manifests' version in
          publishing\msix\dist (or -MsixPath). It needs an elevated console; without one it is COULD NOT
          VERIFY, never a pass. Not asked for, it is SKIPPED and named so on the verdict line.

    P5 of the ticket (winget install --manifest) is not here on purpose: it changes the machine, and the
    winget-pkgs pipeline installs the package on every PR.

.PARAMETER Expect
    The version being published, YY.M.D.HHmm (the tag without its "v"). Optional; when given, the
    manifests must carry it.

.PARAMETER Strict
    P2 downloads each setup.exe and hashes it, instead of reading the release's .sha256 sidecar.

.PARAMETER Wack
    Also run P4. Needs an elevated console.

.PARAMETER MsixPath
    The MSIX for P4. Default: publishing\msix\dist\FastMediaSorter_LITE-<YY.(M*100+D).HHmm.0>-x64.msix,
    the Store remap of the manifests' version.

.NOTES
    Exit codes (CHECK-VERDICT):
      0 - every step that ran passed (P4 not asked for is named as skipped);
      1 - a step inspected something and found it wrong;
      2 - could not verify: winget, the network, a release asset, appcert, elevation or the MSIX is
          missing, and nothing was found wrong.
    The last line is the verdict, `Store-Prepublish-Checks: <WORD> (..)`.

.EXAMPLE
    .\tools\Store-Prepublish-Checks.ps1 -Expect 26.9.20.1206
.EXAMPLE
    # in an elevated console
    .\tools\Store-Prepublish-Checks.ps1 -Expect 26.9.20.1206 -Wack
#>
[CmdletBinding()]
param(
    [string]$Expect = '',
    [switch]$Strict,
    [switch]$Wack,
    [string]$MsixPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')

$name = 'Store-Prepublish-Checks'
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $name 2 'the check itself failed'
}

$root = Split-Path $PSScriptRoot -Parent
$wingetDir = Join-Path $root 'publishing\winget'

# The two editions and their frozen anchors (CLAUDE.md "Naming rule"; the server manifest's own header).
# ForbiddenKeys failed validation before, or would hide the package from the machines it serves.
$editions = @(
    [pscustomobject]@{
        Id = 'user'; Dir = $wingetDir
        PackageIdentifier = 'SerZhyAle.FastMediaSorter'
        PackageName = 'FastMediaSorter LITE'
        Asset = 'FastMediaSorter-{0}-windows-x64-setup.exe'
        ForbiddenKeys = @('Dependencies', 'NestedInstallerType', 'Scope', 'MinimumOSVersion')
    },
    [pscustomobject]@{
        Id = 'server'; Dir = (Join-Path $wingetDir 'server')
        PackageIdentifier = 'SerZhyAle.FastMediaSorter.Server'
        PackageName = 'Fast Media Sorter Folder Share Server'
        Asset = 'FastMediaSorter-{0}-windows-x64-server-setup.exe'
        ForbiddenKeys = @('Dependencies', 'NestedInstallerType')
    }
)

Write-Subject $name "winget manifests of both editions$(if ($Expect) { ", expected version '$Expect'" }); $(Get-TreeLabel $root)"

if ($Expect -and $Expect -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    Exit-Verdict $name 2 "-Expect '$Expect' is not a YY.M.D.HHmm stamp"
}

# --- reading the manifests -----------------------------------------------------
# A line reader, not a YAML parser: these files are flat apart from InstallerSwitches and Installers,
# and every rule below is about a key being there or not. Comments are dropped first - the headers
# talk about exactly the keys that must not be present.
function Read-Manifest([string]$path) {
    $raw = Get-Content -LiteralPath $path -Encoding UTF8
    $schema = ($raw | Where-Object { $_ -match '^#\s*yaml-language-server:\s*\$schema=' } | Select-Object -First 1)
    $code = @($raw | Where-Object { $_ -notmatch '^\s*#' -and $_.Trim() })
    [pscustomobject]@{
        Path = $path
        Leaf = Split-Path $path -Leaf
        Schema = [string]$schema
        Lines = $code
    }
}

# The value of a key anywhere in the file (a list item's "- Key:" included), unquoted; '' when absent.
function Get-Value($manifest, [string]$key) {
    foreach ($line in $manifest.Lines) {
        if ($line -match "^\s*(-\s+)?$([regex]::Escape($key)):\s*(.*)$") {
            return $Matches[2].Trim().Trim('"', "'")
        }
    }
    return ''
}
function Get-Values($manifest, [string]$key) {
    @(foreach ($line in $manifest.Lines) {
        if ($line -match "^\s*(-\s+)?$([regex]::Escape($key)):\s*(.*)$") { $Matches[2].Trim().Trim('"', "'") }
    })
}
function Test-Key($manifest, [string]$key) {
    [bool](@($manifest.Lines | Where-Object { $_ -match "^\s*(-\s+)?$([regex]::Escape($key)):" }).Count)
}

$missingFiles = @()
foreach ($e in $editions) {
    $files = @(
        "$($e.PackageIdentifier).yaml",
        "$($e.PackageIdentifier).installer.yaml",
        "$($e.PackageIdentifier).locale.en-US.yaml"
    ) | ForEach-Object { Join-Path $e.Dir $_ }
    $absent = @($files | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    $missingFiles += $absent
    $e | Add-Member -NotePropertyName Files -NotePropertyValue $files
    if (-not $absent) {
        $e | Add-Member -NotePropertyName Manifests -NotePropertyValue @($files | ForEach-Object { Read-Manifest $_ })
        $e | Add-Member -NotePropertyName Installer -NotePropertyValue ($e.Manifests | Where-Object { $_.Leaf -like '*.installer.yaml' })
    }
}
if ($missingFiles) {
    $missingFiles | ForEach-Object { Write-Host "  missing: $_" }
    Exit-Verdict $name 2 "$($missingFiles.Count) manifest file(s) not found"
}

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$id, $code, [string]$detail) {
    $results.Add([pscustomobject]@{ Id = $id; Code = $code; Detail = $detail })
    $word = if ($null -eq $code) { 'SKIPPED' } else { Get-VerdictWord $code }
    Write-Host "  $id -> $word$(if ($detail) { ": $detail" })"
}

# --- P3 first: the version the other steps judge comes out of it ----------------
Write-Host ''
Write-Host 'P3 manifest consistency' -ForegroundColor Cyan
$p3 = [System.Collections.Generic.List[string]]::new()
$all = @($editions | ForEach-Object { $_.Manifests })

$versions = @($all | ForEach-Object { Get-Value $_ 'PackageVersion' } | Sort-Object -Unique)
if ($versions.Count -ne 1 -or -not $versions[0]) {
    foreach ($m in $all) { $p3.Add("$($m.Leaf): PackageVersion '$(Get-Value $m 'PackageVersion')'") }
    $p3.Add("PackageVersion differs across the six files: $($versions -join ', ')")
}
$version = [string]$versions[0]
if ($Expect -and $version -and $versions.Count -eq 1 -and $version -ne $Expect) {
    $p3.Add("the manifests carry $version, not $Expect")
}
if ($version -and $version -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    $p3.Add("PackageVersion '$version' is not a YY.M.D.HHmm stamp")
}

$schemaVersions = @($all | ForEach-Object { Get-Value $_ 'ManifestVersion' } | Sort-Object -Unique)
if ($schemaVersions.Count -ne 1 -or -not $schemaVersions[0]) {
    $p3.Add("ManifestVersion differs across the six files: $($schemaVersions -join ', ')")
}
foreach ($m in $all) {
    $mv = Get-Value $m 'ManifestVersion'
    $type = Get-Value $m 'ManifestType'
    if ($m.Schema -notmatch "winget-manifest\.$([regex]::Escape($type))\.$([regex]::Escape($mv))\.schema\.json") {
        $p3.Add("$($m.Leaf): the `$schema line does not name the $type $mv schema")
    }
}

foreach ($e in $editions) {
    foreach ($m in $e.Manifests) {
        $id = Get-Value $m 'PackageIdentifier'
        if ($id -ne $e.PackageIdentifier) { $p3.Add("$($m.Leaf): PackageIdentifier '$id', frozen as '$($e.PackageIdentifier)'") }
        foreach ($k in $e.ForbiddenKeys) {
            if (Test-Key $m $k) { $p3.Add("$($m.Leaf): '$k' is declared - it must not be in the $($e.Id) edition") }
        }
    }
    if ($e.PackageIdentifier -notmatch '^[^.\s]+\.[^\s]+$') { $p3.Add("$($e.PackageIdentifier) is not Publisher.Package") }

    $locale = $e.Manifests | Where-Object { $_.Leaf -like '*.locale.*' }
    $pn = Get-Value $locale 'PackageName'
    if ($pn -ne $e.PackageName) { $p3.Add("$($locale.Leaf): PackageName '$pn', frozen as '$($e.PackageName)'") }

    $inst = $e.Installer
    foreach ($k in 'Silent', 'SilentWithProgress') {
        if (-not (Get-Value $inst $k)) { $p3.Add("$($inst.Leaf): InstallerSwitches.$k is missing") }
    }
    if ((Get-Value $inst 'InstallerType') -ne 'inno') { $p3.Add("$($inst.Leaf): InstallerType is not 'inno'") }
    $urls = @(Get-Values $inst 'InstallerUrl')
    $hashes = @(Get-Values $inst 'InstallerSha256')
    if ($urls.Count -eq 0) { $p3.Add("$($inst.Leaf): no InstallerUrl") }
    if ($urls.Count -ne $hashes.Count) { $p3.Add("$($inst.Leaf): $($urls.Count) InstallerUrl but $($hashes.Count) InstallerSha256") }
    foreach ($h in $hashes) { if ($h -notmatch '^[0-9A-Fa-f]{64}$') { $p3.Add("$($inst.Leaf): InstallerSha256 '$h' is not a SHA-256") } }
    if ($version) {
        $want = "/releases/download/v$version/" + ($e.Asset -f $version)
        foreach ($u in $urls) {
            if (-not $u.EndsWith($want, [StringComparison]::Ordinal)) { $p3.Add("$($inst.Leaf): InstallerUrl does not end in $want") }
        }
    }
}

if ($p3.Count) {
    $p3 | ForEach-Object { Write-Host "    - $_" }
    Add-Result 'P3' 1 "$($p3.Count) problem(s)"
} else {
    Add-Result 'P3' 0 "version $version in 6 files, schema $($schemaVersions[0]), anchors and switches in place"
}

# --- P1 winget validate -----------------------------------------------------------
Write-Host ''
Write-Host 'P1 winget validate' -ForegroundColor Cyan
$winget = Get-Command winget.exe -ErrorAction SilentlyContinue
if (-not $winget) {
    Add-Result 'P1' 2 'winget.exe not found'
} else {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "fms-winget-validate-$PID"
    $p1 = @()
    try {
        foreach ($e in $editions) {
            $dir = Join-Path $tmp $e.Id
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $e.Files | ForEach-Object { Copy-Item -LiteralPath $_ -Destination $dir }
            $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            $out = @(& $winget.Source validate --manifest $dir --disable-interactivity 2>&1 | ForEach-Object { "$_" })
            $rc = $LASTEXITCODE
            $ErrorActionPreference = $prev
            $out | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "    [$($e.Id)] $_" }
            # 0 is a clean validation; 0x8A150028 is winget's own "succeeded with warnings" (returnCodes.md);
            # anything else - a schema error, an unparsable file - is a failure of that edition.
            if ($rc -eq 0) { continue }
            if ($rc -eq 0x8A150028) { Write-Host "    [$($e.Id)] passed with warnings (non-blocking)"; continue }
            $p1 += "$($e.Id) (exit $rc)"
        }
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($p1) { Add-Result 'P1' 1 "rejected: $($p1 -join ', ')" } else { Add-Result 'P1' 0 'both editions valid' }
}

# --- P2 SHA-256 against the release -----------------------------------------------
Write-Host ''
Write-Host "P2 InstallerSha256 against the GitHub release$(if ($Strict) { ' (strict: the setup.exe itself)' } else { ' (.sha256 sidecar)' })" -ForegroundColor Cyan
$p2Bad = @(); $p2Unknown = @(); $p2Ok = 0
$dl = Join-Path ([System.IO.Path]::GetTempPath()) "fms-prepublish-$PID"
try {
    foreach ($e in $editions) {
        $urls = @(Get-Values $e.Installer 'InstallerUrl')
        $hashes = @(Get-Values $e.Installer 'InstallerSha256')
        for ($i = 0; $i -lt [Math]::Min($urls.Count, $hashes.Count); $i++) {
            $url = $urls[$i]; $want = $hashes[$i].ToUpperInvariant()
            $leaf = Split-Path $url -Leaf
            try {
                if ($Strict) {
                    New-Item -ItemType Directory -Force -Path $dl | Out-Null
                    $file = Join-Path $dl $leaf
                    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -TimeoutSec 600
                    $got = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToUpperInvariant()
                } else {
                    $resp = Invoke-WebRequest -Uri "$url.sha256" -UseBasicParsing -TimeoutSec 60
                    $text = if ($resp.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($resp.Content) } else { [string]$resp.Content }
                    if ($text -notmatch '(?i)\b([0-9a-f]{64})\b') { throw "no SHA-256 in $leaf.sha256" }
                    $got = $Matches[1].ToUpperInvariant()
                }
            } catch {
                # A missing asset (the CI run not finished, or the tag never pushed) and an unreachable
                # network both mean nothing was compared - never a pass, and not a mismatch either.
                Write-Host "    $leaf`: could not fetch - $($_.Exception.Message)"
                $p2Unknown += $leaf
                continue
            }
            if ($got -eq $want) {
                Write-Host "    $leaf`: $want matches"
                $p2Ok++
            } else {
                Write-Host "    $leaf`: manifest $want, release $got"
                $p2Bad += $leaf
            }
        }
    }
} finally {
    Remove-Item -LiteralPath $dl -Recurse -Force -ErrorAction SilentlyContinue
}
if ($p2Bad) { Add-Result 'P2' 1 "mismatch: $($p2Bad -join ', ')" }
elseif ($p2Unknown) { Add-Result 'P2' 2 "not fetched: $($p2Unknown -join ', ')" }
elseif ($p2Ok -eq 0) { Add-Result 'P2' 2 'no installer to compare' }
else { Add-Result 'P2' 0 "$p2Ok installer(s) match the release" }

# --- P4 WACK ------------------------------------------------------------------------
Write-Host ''
Write-Host 'P4 WACK (Windows App Certification Kit)' -ForegroundColor Cyan
if (-not $Wack) {
    Add-Result 'P4' $null 'not asked for (-Wack)'
} else {
    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $MsixPath -and $version -match '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') {
        # The Store remap of build-msix.ps1: YY.M.D.HHmm -> YY.(M*100+D).HHmm.0.
        $storeVersion = '{0}.{1}.{2}.0' -f [int]$Matches[1], ([int]$Matches[2] * 100 + [int]$Matches[3]), [int]$Matches[4]
        $MsixPath = Join-Path $root "publishing\msix\dist\FastMediaSorter_LITE-$storeVersion-x64.msix"
    }
    if (-not (Test-Path -LiteralPath $appcert)) {
        Add-Result 'P4' 2 "appcert.exe not found (Windows SDK App Certification Kit): $appcert"
    } elseif (-not $elevated) {
        Add-Result 'P4' 2 'appcert.exe needs an elevated console'
    } elseif (-not $MsixPath -or -not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) {
        Add-Result 'P4' 2 "MSIX not found: $MsixPath (build it with publishing\msix\build-msix.ps1 -ReleaseVersion $version)"
    } else {
        $report = Join-Path ([System.IO.Path]::GetTempPath()) "fms_wack_report_$PID.xml"
        Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
        Write-Host "    package: $MsixPath"
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $appcert reset 2>&1 | ForEach-Object { Write-Host "    $_" }
        & $appcert test -apptype windowsstorepackage -packagepath $MsixPath -reportoutputpath $report 2>&1 |
            ForEach-Object { Write-Host "    $_" }
        $rc = $LASTEXITCODE
        $ErrorActionPreference = $prev
        if (-not (Test-Path -LiteralPath $report)) {
            Add-Result 'P4' 2 "appcert wrote no report (exit $rc)"
        } else {
            $overall = ''
            if ((Get-Content -LiteralPath $report -Raw) -match 'OVERALL_RESULT="([^"]+)"') { $overall = $Matches[1] }
            if (-not $overall) { Add-Result 'P4' 2 "no OVERALL_RESULT in $report" }
            elseif ($overall -eq 'PASS') { Add-Result 'P4' 0 "Overall result: PASS ($(Split-Path $MsixPath -Leaf))" }
            else { Add-Result 'P4' 1 "Overall result: $overall - report kept at $report" }
        }
    }
}

# --- verdict ------------------------------------------------------------------------
Write-Host ''
$ran     = @($results | Where-Object { $null -ne $_.Code })
$failed  = @($ran | Where-Object { $_.Code -eq 1 })
$unknown = @($ran | Where-Object { $_.Code -eq 2 })
$passed  = @($ran | Where-Object { $_.Code -eq 0 })
$skipped = @($results | Where-Object { $null -eq $_.Code })

$parts = @()
if ($version) { $parts += "v$version" }
if ($failed.Count)  { $parts += 'failed: ' + (($failed | ForEach-Object Id) -join ', ') }
if ($unknown.Count) { $parts += 'not verified: ' + (($unknown | ForEach-Object Id) -join ', ') }
$parts += "$($passed.Count) passed"
if ($skipped.Count) { $parts += 'skipped: ' + (($skipped | ForEach-Object Id) -join ', ') }

Exit-Verdict $name (Get-AggregateCode @($ran | ForEach-Object Code)) ($parts -join ', ')
