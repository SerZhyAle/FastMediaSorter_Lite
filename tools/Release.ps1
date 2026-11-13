<#
.SYNOPSIS
    The RELEASE orchestrator of Fast Media Sorter & Sharing for Windows. See ../docs/guides/BUILD_AND_RELEASE.md.

.DESCRIPTION
    "Release" = build on GitHub + publish. GitHub Actions (release.yml) runs ONLY when a 'v*' tag
    is pushed, so the one paid operation here is the tag push.

    By default the script is a DRY RUN: it prints the version and tag, runs the whole free local
    preflight and pushes nothing. The tag is created and pushed only with an explicit -Push - a
    guard against paying for Actions by accident.

    The preflight is canon invariant 5 made mechanical: the tag push takes a written PASS naming the
    version it judged, and a FAIL or no verdict at all stops it. Each step runs in its own process
    and reports under contract CHECK-VERDICT (who runs which check: tools\check-placement.jsonl):
      1. tools\Run-AllTests.ps1            - the viewer (net48 + net10), Share Manager and Go worker
                                             suites; release.yml runs no tests, so this is the last
                                             point a red suite can stop a release;
      2. tools\Build-SitePages.ps1 -Check  - the generated site pages still match their copy sources;
      3. tools\Test-SiteQuality.ps1        - the published site holds DOC-EXTERNAL-QUALITY rules 1-7
                                             over the whole page set (ticket 059);
      4. tools\Sync-IconGlyphs.ps1 -Check  - the vendored glyphs still match the catalog;
      5. tools\Sync-ExchangeMark.ps1 -Check - the exchange mark's artwork and provenance match the catalog;
      6. tools\Test-FeatureGates.ps1      - an open exchange or live-broadcast gate names, in a committed
                                             evidence file, the rung and the date it was opened at (177
                                             section 3 item 3); a release never opens a gate by itself;
      7. the Store channel (tools\lib\StoreChannel.ps1, the canon's shared read-only Store-Preflight;
                                             WINDOWS-STORE 0.4 section D, rules 21 and 23) - stops the
                                             release when a Store submission is already pending, when
                                             the package version is not above the live one or when the
                                             price is not Free; with the channel unconfigured, expired
                                             or refused it prints one line that the Store step is manual
                                             and the release goes on, as it does without the channel;
      8. tools\Build-OfflineRelease.ps1    - builds locally, for free, what release.yml builds: the
                                             ZIP and both installers;
      9. tools\Test-ArtifactVersion.ps1    - every staged exe and both setup.exe files carry the
                                             version being tagged (BUILD-EVIDENCE rule 2);
     10. tools\Test-ImageBackendArtifacts.ps1 - no ImageSharp in any staged program, bundle or
                                             embedded payload, and the image backend's natives are
                                             present (specification 270);
     11. tools\Test-HtmlHostArtifacts.ps1    - one x64 WebView2 loader from the pinned SDK in
                                             webview2\win-x64 beside each staged viewer, none in the
                                             Manager-only Server root (specification 275).
    Steps 1-7 all run whatever the one before returned. The build runs only when they all passed:
    a release that is already blocked does not need the slowest step to say so.

    The preflight ends in one line, `Release-Preflight: <WORD> (v<version>; ..)`. Only a bare PASS
    lets -Push through. A step switched off with -SkipTests or -SkipLocalCheck makes the verdict
    COULD NOT VERIFY - a skipped step is a step nobody verified - so those switches serve a quicker
    dry run and can never ship a tag.

.PARAMETER Version
    The version, YY.M.D.HHmm. Default: the current date and time.

.PARAMETER Push
    Really create and push the tag 'v<Version>' (starts the paid GitHub Actions run). Without it the
    script only shows the plan and runs the preflight.

.PARAMETER SkipLocalCheck
    Skip the local package build (step 7) and with it the checks of what it built (steps 8-10). The
    preflight is then COULD NOT VERIFY, so -Push refuses.

.PARAMETER SkipTests
    Skip the test run (step 1). The preflight is then COULD NOT VERIFY, so -Push refuses.

.PARAMETER AllowDirty
    Allow a working tree with uncommitted changes (refused by default). The preflight then judges a
    tree the tag does not carry, and says so. This is permitted for dry runs only.

.OUTPUTS
    Exit codes: 0 - the preflight passed (and, with -Push, the tag was pushed); 1 - a preflight step
    failed, the git state was refused, or the tag could not be created or pushed; 2 - the preflight
    could not verify everything (a step could not run, or was skipped).

.EXAMPLE
    .\tools\Release.ps1
    Dry run: version, tag, the whole preflight. Nothing is pushed.

.EXAMPLE
    .\tools\Release.ps1 -Push
    Full release: the preflight, then the tag is created and pushed -> GitHub Actions builds and publishes.
#>
[CmdletBinding()]
param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm"),
    [switch]$Push,
    [switch]$SkipLocalCheck,
    [switch]$SkipTests,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$tag = "v$Version"
. (Join-Path $PSScriptRoot 'lib\Verdict.ps1')
. (Join-Path $PSScriptRoot 'lib\StoreChannel.ps1')

function Say([string]$msg, [string]$color = "Cyan") { Write-Host $msg -ForegroundColor $color }
function Step([string]$msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Yellow }

# An unexpected terminating error must still end in a verdict, and never in a pushed tag.
trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict 'Release-Preflight' 2 "$tag; the release script itself failed"
}

Say "Fast Media Sorter & Sharing for Windows - RELEASE" "Green"
Say "Version : $Version"
Say "Tag     : $tag"
Say ("Mode    : " + $(if ($Push) { "PUSH (starts the paid GitHub Actions run)" } else { "DRY RUN (nothing is pushed)" })) `
    $(if ($Push) { "Red" } else { "Gray" })

if ($Version -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    Exit-Verdict 'Release-Preflight' 2 "$tag; '$Version' is not a YY.M.D.HHmm version"
}

$branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
$head = (git -C $repoRoot rev-parse --verify HEAD).Trim()
Write-Subject 'Release-Preflight' "$tag on branch $branch, $(Get-TreeLabel $repoRoot)"

# --- Git state ------------------------------------------------------------------
Step "Git state"

Say "Branch: $branch"
if ($branch -ne "main") {
    Write-Warning "A release is normally cut from 'main'. Current branch: $branch."
}

$dirty = (git -C $repoRoot status --porcelain)
if ($dirty) {
    if ($Push) {
        Exit-Verdict 'Release-Preflight' 1 "$tag; cannot push a release from a dirty working tree: the tag would not carry the files tested"
    }
    if (-not $AllowDirty) {
        $dirty | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
        Exit-Verdict 'Release-Preflight' 1 "$tag; the working tree is not clean - commit or stash, or pass -AllowDirty"
    }
    Write-Warning "Uncommitted changes (allowed by -AllowDirty). The preflight judges them; the tag will not carry them."
} else {
    Say "The working tree is clean."
}
if ($Push) {
    # The net48 build regenerates this tracked file. It must already carry the candidate
    # version in the commit, or the build would change the tree after it was judged.
    $versionSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\My Project\VersionInfo.vb') -Raw
    $escapedVersion = [regex]::Escape($Version)
    if ($versionSource -notmatch "AssemblyVersion\(`"$escapedVersion`"\)" -or
        $versionSource -notmatch "AssemblyFileVersion\(`"$escapedVersion`"\)") {
        Exit-Verdict 'Release-Preflight' 1 "$tag; commit the generated VersionInfo.vb for this version before the release preflight"
    }
}

if (git -C $repoRoot tag --list $tag) {
    Exit-Verdict 'Release-Preflight' 1 "$tag; the tag already exists - choose another -Version"
}
if (-not $SkipLocalCheck) {
    # The packager recreates its version-specific stages and artifacts even with -NoClean.
    # Refuse an occupied version so a release preflight cannot erase earlier local work.
    $baseName = "FastMediaSorter-$Version-windows-x64"
    $outputs = @(
        (Join-Path $repoRoot "stage\$baseName"),
        (Join-Path $repoRoot "stage\$baseName-server")
    )
    foreach ($name in @("$baseName.zip", "$baseName-setup.exe", "$baseName-server-setup.exe")) {
        $outputs += Join-Path $repoRoot "dist\$name"
        $outputs += Join-Path $repoRoot "dist\$name.sha256"
    }
    $occupied = @($outputs | Where-Object { Test-Path -LiteralPath $_ })
    if ($occupied.Count -gt 0) {
        Exit-Verdict 'Release-Preflight' 1 "$tag; package output already exists at $($occupied[0]) - choose another -Version to preserve it"
    }
}

# --- The preflight ----------------------------------------------------------------
# One row per step. Code: 0 / 1 / 2 when it ran, $null when it did not (State says why).
$steps = [System.Collections.Generic.List[object]]::new()
function Add-Step([string]$name, $code, [string]$state, [string]$verdict) {
    $steps.Add([pscustomobject]@{ Name = $name; Code = $code; State = $state; Verdict = $verdict })
}
function Invoke-Step([string]$title, [string]$script, [string[]]$arguments = @()) {
    Step $title
    $r = Invoke-ChildCheck (Join-Path $PSScriptRoot $script) $arguments
    # A step that answered NOT APPLICABLE inspected nothing: for a release gate that is a step that did
    # not run, so it reads like a skipped one (it never counts as passed, and the preflight cannot pass).
    if ($r.NotApplicable) { Add-Step $r.Name $null 'not applicable' $r.Verdict }
    else { Add-Step $r.Name $r.Code 'ran' $r.Verdict }
}

# 1. Tests (free; a red run must stop the release before it is paid for).
if ($SkipTests) {
    Write-Warning "Tests skipped (-SkipTests). The preflight cannot pass without them."
    Add-Step 'Run-AllTests' $null 'skipped' 'skipped by -SkipTests'
} else {
    # A missing exchange server binary fails the real-binary facts here instead of skipping them (L0278 7.5).
    $env:FMS_REQUIRE_BINARY = '1'
    Invoke-Step "Tests (viewer + Share Manager + worker)" 'Run-AllTests.ps1'
}

# 2 to 5. The generated artifacts still match their sources, the published pages hold the
# DOC-EXTERNAL-QUALITY gates (SEO block, links, freshness, termbase - ticket 059), and the vendored
# glyphs and the exchange mark match the catalog.
Invoke-Step "Site pages match their copy sources" 'Build-SitePages.ps1' @('-Check')
Invoke-Step "Published site quality" 'Test-SiteQuality.ps1'
Invoke-Step "Vendored glyphs match the catalog" 'Sync-IconGlyphs.ps1' @('-Check')
Invoke-Step "Exchange mark matches the catalog" 'Sync-ExchangeMark.ps1' @('-Check')

# 6. The gates of the exchange and the live broadcast (204 section 3 item 3, the table of 206 section 2):
#    an open gate needs its evidence file, and the release itself never opens one.
Invoke-Step "Exchange and live-broadcast gates name their evidence" 'Test-FeatureGates.ps1'

# 7. The Store channel, read-only (L0305; WINDOWS-STORE 0.4 section D). The shared preflight reads the live
#    submission through the Store CLI and refuses a release that the Store step could not follow: a
#    submission already pending, a version not above the live one, a price that is not Free (rule 23).
#    Unconfigured, expired or refused, the channel is only a manual Store step and never a failed release
#    (rule 21): that answer prints one line and adds no row to the verdict.
Step "Microsoft Store channel (read-only)"
$storeCheck = Invoke-StoreChannelCheck -RepoRoot $repoRoot -Version $Version
foreach ($line in $storeCheck.Lines) { Write-Host "  $line" }
switch ($storeCheck.Mode) {
    'ready'   { Add-Step 'Store-Preflight' 0 'ran' "Store-Preflight: PASS ($($storeCheck.Detail))" }
    'refused' { Add-Step 'Store-Preflight' 1 'ran' "Store-Preflight: FAIL ($($storeCheck.Detail))" }
    default   { Say "  The Store step is manual; the other channels go on as without the channel." "Gray" }
}

# 8 to 11. The local package build (free; catches a CI failure before it is paid for), the version
# inside what it built, and the image backend and WebView2 host of its stages.
$blocked = @($steps | Where-Object { $_.Code -in 1, 2 })
if ($SkipLocalCheck) {
    Write-Warning "Local package build skipped (-SkipLocalCheck). The preflight cannot pass without it."
    Add-Step 'Build-OfflineRelease' $null 'skipped' 'skipped by -SkipLocalCheck'
    Add-Step 'Test-ArtifactVersion' $null 'skipped' 'skipped with the build'
    Add-Step 'Test-ImageBackendArtifacts' $null 'skipped' 'skipped with the build'
    Add-Step 'Test-HtmlHostArtifacts' $null 'skipped' 'skipped with the build'
} elseif ($blocked.Count -gt 0) {
    Say "`nThe package build is not run: $(($blocked | ForEach-Object Name) -join ', ') did not pass." "Yellow"
    Add-Step 'Build-OfflineRelease' $null 'not run' 'not run - an earlier step did not pass'
    Add-Step 'Test-ArtifactVersion' $null 'not run' 'not run - nothing was built'
    Add-Step 'Test-ImageBackendArtifacts' $null 'not run' 'not run - nothing was built'
    Add-Step 'Test-HtmlHostArtifacts' $null 'not run' 'not run - nothing was built'
} else {
    Say "`nBuilds everything release.yml builds: the ZIP, the User installer and the Server installer." "Gray"
    # Preserve existing stage, dist and temp files; the package builder still recreates this version's own stage.
    Invoke-Step "Local package build (as on CI) - so a failing GitHub job is not paid for" 'Build-OfflineRelease.ps1' @('-Version', $Version, '-NoClean')
    if ($steps[$steps.Count - 1].Code -eq 0) {
        $stage = Join-Path $repoRoot "stage\FastMediaSorter-$Version-windows-x64"
        $dist  = Join-Path $repoRoot 'dist'
        $artifacts = @(
            (Join-Path $stage 'FastMediaSorter_LITE.exe'),
            (Join-Path $stage 'FastMediaSorter_x86.exe'),
            (Join-Path $stage 'FastMediaSorterCompanion.exe'),
            (Join-Path "$stage-server" 'FastMediaSorterCompanion.exe'),
            (Join-Path $dist "FastMediaSorter-$Version-windows-x64-setup.exe"),
            (Join-Path $dist "FastMediaSorter-$Version-windows-x64-server-setup.exe")
        )
        Invoke-Step "The built package carries $Version" 'Test-ArtifactVersion.ps1' @('-Expect', $Version, '-Path', ($artifacts -join ','))
        # Specification 270: no ImageSharp in any staged program, bundle or embedded payload; natives present.
        $imageStages = @($stage, "$stage-server", (Join-Path "$stage-server" 'viewer')) | Where-Object { Test-Path -LiteralPath $_ }
        Invoke-Step "The staged packages carry the image backend of specification 270" 'Test-ImageBackendArtifacts.ps1' @('-Path', ($imageStages -join ','))
        # Specification 275 section 9.2: one x64 WebView2 loader beside each staged viewer, none in the
        # Manager-only Server root.
        Invoke-Step "The staged packages carry the WebView2 host of specification 275" 'Test-HtmlHostArtifacts.ps1' @('-Path', ($imageStages -join ','))
    } else {
        Add-Step 'Test-ArtifactVersion' $null 'not run' 'not run - the build failed'
        Add-Step 'Test-ImageBackendArtifacts' $null 'not run' 'not run - the build failed'
        Add-Step 'Test-HtmlHostArtifacts' $null 'not run' 'not run - the build failed'
    }
}

# --- The verdict --------------------------------------------------------------------
Step "Preflight summary"
foreach ($s in $steps) {
    $label = if ($null -eq $s.Code) { $s.State.ToUpperInvariant() } else { Get-VerdictWord $s.Code }
    Write-Host ("  {0,-22} {1,-17} {2}" -f $s.Name, $label, $s.Verdict)
}

$ran     = @($steps | Where-Object { $null -ne $_.Code })
$code    = Get-AggregateCode @($ran | ForEach-Object Code)
$missing = @($steps | Where-Object { $null -eq $_.Code })
if ($code -eq 0 -and $missing.Count -gt 0) { $code = 2 }   # a skipped step is never a pass

$parts = @($tag)
$failed  = @($ran | Where-Object { $_.Code -eq 1 })
$unknown = @($ran | Where-Object { $_.Code -eq 2 })
if ($failed.Count)  { $parts += 'failed: ' + (($failed | ForEach-Object Name) -join ', ') }
if ($unknown.Count) { $parts += 'not verified: ' + (($unknown | ForEach-Object Name) -join ', ') }
foreach ($state in 'skipped', 'not run', 'not applicable') {
    $these = @($missing | Where-Object { $_.State -eq $state })
    if ($these.Count) { $parts += "${state}: " + (($these | ForEach-Object Name) -join ', ') }
}
$parts += "$(@($ran | Where-Object { $_.Code -eq 0 }).Count) of $($steps.Count) steps passed"
if ($dirty) { $parts += 'judged uncommitted changes' }
if ($Push -and $code -eq 0) {
    $currentHead = (git -C $repoRoot rev-parse --verify HEAD).Trim()
    $currentBranch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
    $currentDirty = (git -C $repoRoot status --porcelain)
    if ($currentHead -ne $head -or $currentBranch -ne $branch -or $currentDirty) {
        $code = 1
        $parts += 'the commit, branch or working tree changed during preflight; no tag was created'
    }
}

Write-Host ""
Write-VerdictLine 'Release-Preflight' $code ($parts -join '; ')

if ($code -ne 0) {
    if ($Push) { Say "The tag was NOT created and nothing was pushed. Paid minutes spent: 0." "Red" }
    exit $code
}

# --- Tag + push (the one paid operation) ------------------------------------------
if (-not $Push) {
    Step "DRY RUN finished"
    Say "The tag was NOT created and GitHub Actions was NOT started. Paid minutes spent: 0." "Green"
    Say "For the real release, repeat with -Push:" "Gray"
    Say "    .\tools\Release.ps1 -Version $Version -Push" "Gray"
    exit 0
}

Step "Creating and pushing the tag $tag (starts GitHub Actions)"
git -C $repoRoot tag -a $tag -m "Fast Media Sorter & Sharing for Windows $Version"
if ($LASTEXITCODE -ne 0) { Say "Could not create the tag $tag." "Red"; exit 1 }

git -C $repoRoot push origin $tag
if ($LASTEXITCODE -ne 0) { Say "Could not push the tag $tag. It exists locally - delete it (git tag -d $tag) before retrying." "Red"; exit 1 }

Say "Tag $tag pushed. GitHub Actions is building the release." "Green"

# One evidence line per released version in the release record (WINDOWS-STORE 0.4 section D, rule 26): the Store ID,
# the package version and the preflight's sanitized answer, no account identifier and no secret. It is
# written once the tag exists, so a dry run or a refused run leaves the tree as it found it.
$storeEvidencePath = Join-Path $repoRoot 'publishing\store\channel-evidence.jsonl'
try { Add-StoreChannelEvidence $storeEvidencePath $tag $storeCheck.Evidence }
catch { Write-Warning "The Store channel evidence line could not be written: $($_.Exception.Message)" }

# --- After the release (manual steps) ------------------------------------------
Step "Next, by hand (see docs/guides/BUILD_AND_RELEASE.md)"
@"
1) GitHub: wait for the build and its 6 assets - setup.exe + zip + server-setup.exe and three .sha256
   https://github.com/SerZhyAle/FastMediaSorter_Lite/actions
2) Site: pwsh -NoProfile -File tools/Set-ReleaseManifest.ps1 -Version $Version
   (writes tools/release-manifest.json from the published release and re-renders the pages), then commit and push the site.
3) winget, User edition: a PR to microsoft/winget-pkgs for SerZhyAle.FastMediaSorter
   (the Inno setup.exe directly, no dependencies, no Scope) -> docs/specifications/done/SPECIFICATION_WINGET_PUBLISHING.md
4) winget, Server edition: a separate PR for SerZhyAle.FastMediaSorter.Server
   (manifests in publishing/winget/server/; Scope: machine and MinimumOSVersion belong there)
   -> docs/guides/SERVER_EDITION_BUILD_AND_TEST.md
5) Both PRs: fill the description with gh pr edit (never leave it empty).
6) Microsoft Store (optional): cd publishing\msix; .\build-msix.ps1 -ReleaseVersion $Version (NO -SelfSign; the identity is read from identity.json),
   upload the unsigned .msix to Partner Center -> docs/guides/STORE_PUBLISHING.md
   The upload is manual; this script starts no Store submission. Before the tag it read the live Store state through the shared
   read-only preflight (step 7) and appended its one evidence line to publishing\store\channel-evidence.jsonl - commit that line
   with the site. A write through the Store CLI needs the owner's words that name the product and the package version.
   The Store takes the User edition only; the Server edition never goes there.
"@ | Write-Host -ForegroundColor Cyan
