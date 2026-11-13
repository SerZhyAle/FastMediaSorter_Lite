<#
.SYNOPSIS
    Stages the OPTIONAL viewer component of the Server edition installer and its offline support
    payloads (ticket 132 Q132-1, L0307).

.DESCRIPTION
    The Server edition leads with the server role; the media and file manager rides along as an
    unchecked component. This script lays the viewer's files down under <Destination>\viewer\ and the
    offline support payloads it can be given under <Destination>\viewer-support\, where
    FastMediaSorterServer.iss looks for them (the viewer component exists only when
    <Destination>\viewer\FastMediaSorter_LITE.exe does).

    Everything comes from the User stage of THE SAME build, taken through its payload manifest
    (tools\lib\InstallerPayloadSelection.ps1; the manifest is written by tools\Build-Installer.ps1 and
    records the version, the pair's OCR and exchange options and every file's SHA-256):

        viewer\            both viewer exes and the libraries they load - the User setup's `core`
        viewer-support\    libvlc\ (video codecs), pdfium.dll (PDF preview), tessdata\ and
                           tessdata-best\ (OCR models) - exactly the trees the manifest records, so the
                           Server setup offers the same offline set the User setup does

    Nothing of the share side (the Share Manager exe, companion\, filedo\: staged by
    Stage-ServerSharePayload.ps1) and nothing of the exchange tree is copied here: the exchange gate is
    judged on its own by the packagers, and a viewer-only selection can never carry the tree.

    A missing or mismatched manifest, a stage that no longer matches it, or a file that did not arrive
    stops the script with an actionable message; nothing is ever staged with a warning.

.PARAMETER UserStage
    The User edition's staged tree (stage\FastMediaSorter-<version>-windows-x64).

.PARAMETER Destination
    The Server edition's stage.

.PARAMETER Version
    Expected version of the staged viewer executables and of the payload manifest.

.PARAMETER Require
    Retained for callers of the earlier script. A missing viewer is always an error.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UserStage,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Version,
    [switch]$Require
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\InstallerPayloadSelection.ps1')

$mainline = 'FastMediaSorter_LITE.exe'
$remedy = "Run tools\Build-Installer.ps1 -Version $Version with the required payload options first, then retry the Server package."

[void](ConvertTo-InstallerVersionNumbers $Version)
foreach ($exe in @($mainline, 'FastMediaSorter_x86.exe')) {
    Assert-InstallerExeVersion -Stage $UserStage -Relative $exe -Version $Version -Remedy $remedy
}
$manifest = Read-InstallerPayloadManifest -Stage $UserStage -Version $Version
Assert-InstallerStageMatchesManifest -Manifest $manifest -Stage $UserStage

$map = Get-InstallerComponentMap
$userRoot = (Resolve-Path -LiteralPath $UserStage).Path.TrimEnd('\')
$stageFiles = @($manifest.files | ForEach-Object { [string]$_.path })
$sizes = @{}
foreach ($f in $manifest.files) { $sizes[[string]$f.path] = [int64]$f.size }

# What goes where: the viewer's own files under viewer\, each support capability the pair carries under
# viewer-support\. A capability the options leave out (no OCR models, ..) was already proven absent from
# the stage; it is simply not in the manifest.
$plan = New-Object Collections.Generic.List[object]
foreach ($capability in $map.capabilities) {
    $root = [string]$capability.server.root
    if ($capability.server.component -ne 'viewer' -and $root -ne [string]$map.serverSupportRoot) { continue }
    if (-not (Test-InstallerCapabilityExpected -Capability $capability -OcrModels ([string]$manifest.ocrModels) -Exchange ([bool]$manifest.exchange))) { continue }
    $files = @(Get-InstallerCapabilityFiles -Map $map -Capability $capability -StageFiles $stageFiles)
    if ($files.Count -eq 0) { throw "The payload manifest of '$UserStage' records no files for '$($capability.label)'. $remedy" }
    foreach ($rel in $files) { $plan.Add([pscustomobject]@{ Capability = [string]$capability.id; Source = $rel; Target = (Join-Path $root $rel) }) }
}
if (-not ($plan | Where-Object { $_.Source -eq $mainline })) { throw "The viewer payload was planned without $mainline - refusing to hand the installer a half-viewer." }

foreach ($root in @([string]$map.serverViewerRoot, [string]$map.serverSupportRoot)) {
    $dir = Join-Path $Destination $root
    if (Test-Path -LiteralPath $dir) { throw "Destination '$dir' already exists. Use an empty Server stage." }
}
foreach ($step in $plan) {
    $target = Join-Path $Destination $step.Target
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $userRoot $step.Source) -Destination $target -Force
}

# Verify what arrived: every planned file is there with the recorded size, and each root holds nothing else.
foreach ($step in $plan) {
    $target = Join-Path $Destination $step.Target
    if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Item -LiteralPath $target).Length -ne $sizes[$step.Source]) {
        throw "Staging failed for $($step.Source) ($($step.Capability)): the copy under '$($step.Target)' is missing or has another size."
    }
}
foreach ($root in @([string]$map.serverViewerRoot, [string]$map.serverSupportRoot)) {
    $dir = Join-Path $Destination $root
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    $wanted = @($plan | Where-Object { $_.Target -like "$root\*" }).Count
    $found = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force).Count
    if ($found -ne $wanted) { throw "The Server stage folder '$root\' holds $found files but the payload manifest plans $wanted." }
}

$viewerMb = [Math]::Round((@($plan | Where-Object { $_.Target -like "$($map.serverViewerRoot)\*" } | ForEach-Object { $sizes[$_.Source] } | Measure-Object -Sum).Sum) / 1MB, 1)
$supportMb = [Math]::Round((@($plan | Where-Object { $_.Target -like "$($map.serverSupportRoot)\*" } | ForEach-Object { $sizes[$_.Source] } | Measure-Object -Sum).Sum) / 1MB, 1)
Write-Host "Staged the optional viewer -> $($map.serverViewerRoot)\ ($viewerMb MB) and its offline support -> $($map.serverSupportRoot)\ ($supportMb MB; OCR models '$($manifest.ocrModels)')"
