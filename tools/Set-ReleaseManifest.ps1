<#
.SYNOPSIS
    Writes tools/release-manifest.json - the release facts every download control of the site shows - from a PUBLISHED
    GitHub release, then re-renders the site pages from it.

.DESCRIPTION
    The version, the versioned asset links, their .sha256 sidecars and the release date on the landing, the 12 language
    landings and the Server edition page are build-time facts (canon DOCUMENTATION_CONCEPT section 4, SITE_CONFIGURATION
    section 2; ticket 207 site audit F3, F31, F35): tools/Build-SitePages.ps1 renders them from tools/release-manifest.json,
    and no page asks api.github.com for them.

    This script is the only writer of that manifest. It is a step of the release flow AFTER the tag's GitHub Actions run
    has published the release: it reads the release back (gh release view), refuses one that is a draft or a
    pre-release or that lacks any of the six assets release.yml attaches (the User setup, the portable ZIP, the Server
    setup and a .sha256 sidecar for each), writes the manifest - with the size and the SHA-256 GitHub reports for each
    asset, where it reports them (ticket L0301) - and runs tools/Build-SitePages.ps1. A download link on the
    site therefore never names an asset that is not published. Commit and push the regenerated pages with the manifest.

    Between the CHANGELOG section of a new release and this step the pages keep offering the previous release; the
    conformance check of tools/Build-SitePages.ps1 -Check (check 6) accepts the manifest naming the newest released
    section of CHANGELOG.md or the one before it, so a release whose step was forgotten blocks the next release's
    preflight.

.PARAMETER Version
    The published release, YY.M.D.HHmm (the tag without its 'v').

.PARAMETER NoRender
    Write the manifest only; do not run tools/Build-SitePages.ps1.

.OUTPUTS
    Exit codes: 0 - the manifest was written (and the pages re-rendered); 1 - the release is a draft or a pre-release,
    lacks an asset, or the version is malformed, or the re-render failed; 2 - the release could not be read (no gh, no
    network, no sign-in, or no such release).

.EXAMPLE
    pwsh -NoProfile -File tools/Set-ReleaseManifest.ps1 -Version 26.10.25.1830
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$NoRender
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$RepoSlug = 'SerZhyAle/FastMediaSorter_Lite'
$ManifestFile = Join-Path $PSScriptRoot 'release-manifest.json'

function Fail([int]$code, [string]$msg) {
    Write-Error $msg -ErrorAction Continue
    exit $code
}

if ($Version -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') { Fail 1 "'$Version' is not a YY.M.D.HHmm version" }
$tag = "v$Version"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail 2 'gh (GitHub CLI) is not on PATH: the published release cannot be read' }
$json = & gh release view $tag --repo $RepoSlug --json tagName,publishedAt,isDraft,isPrerelease,assets 2>&1
if ($LASTEXITCODE -ne 0) { Fail 2 "gh release view $tag failed: $($json -join ' ')" }
$rel = ($json -join "`n") | ConvertFrom-Json

if ($rel.tagName -ne $tag) { Fail 1 "gh returned the release $($rel.tagName), not $tag" }
if ($rel.isDraft) { Fail 1 "$tag is a draft: it is not published" }
if ($rel.isPrerelease) { Fail 1 "$tag is a pre-release: the site offers published releases only" }

# The asset names release.yml attaches (its "pkg", "setup" and "serversetup" steps), each with its .sha256 sidecar.
$assets = [ordered]@{
    setup       = "FastMediaSorter-$Version-windows-x64-setup.exe"
    zip         = "FastMediaSorter-$Version-windows-x64.zip"
    serverSetup = "FastMediaSorter-$Version-windows-x64-server-setup.exe"
}
$published = @($rel.assets | ForEach-Object { [string]$_.name })
$missing = @(foreach ($n in $assets.Values) { foreach ($f in $n, "$n.sha256") { if ($published -notcontains $f) { $f } } })
if ($missing.Count -gt 0) { Fail 1 "$tag does not carry: $($missing -join ', ')" }

$date = ([datetime]$rel.publishedAt).ToUniversalTime().ToString('yyyy-MM-dd')
# The size and SHA-256 of each asset as GitHub reports them (ticket L0301, Q300-2). A fact GitHub does not report is left
# out, never guessed: the site then shows the link without it.
$files = [ordered]@{}
foreach ($key in $assets.Keys) {
    $a = @($rel.assets | Where-Object { [string]$_.name -eq $assets[$key] })[0]
    $facts = [ordered]@{}
    if ($a.PSObject.Properties['size'] -and [long]$a.size -gt 0) { $facts['size'] = [long]$a.size }
    if ($a.PSObject.Properties['digest'] -and [string]$a.digest -match '^sha256:([0-9a-f]{64})$') { $facts['sha256'] = $Matches[1] }
    $files[$key] = $facts
}
$manifest = [ordered]@{
    _about  = 'The release facts the site shows (ticket 207 F3, ticket L0301). Written by tools/Set-ReleaseManifest.ps1 from the published GitHub release, read by tools/Build-SitePages.ps1; never edited by hand.'
    version = $Version
    date    = $date
    assets  = $assets
    files   = $files
}
$text = (($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n") + "`n"
[IO.File]::WriteAllText($ManifestFile, $text, (New-Object Text.UTF8Encoding $false))
$withFacts = @($files.Values | Where-Object { $_.Contains('size') -and $_.Contains('sha256') }).Count
Write-Host "Wrote tools/release-manifest.json: $tag, published $date, $($assets.Count) assets with their .sha256 sidecars, $withFacts with size and SHA-256."

if ($NoRender) { exit 0 }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build-SitePages.ps1')
if ($LASTEXITCODE -ne 0) { Fail 1 "tools/Build-SitePages.ps1 failed (exit $LASTEXITCODE) after the manifest was written" }
Write-Host 'Site pages re-rendered from the manifest. Commit and push them together with tools/release-manifest.json.'
exit 0
