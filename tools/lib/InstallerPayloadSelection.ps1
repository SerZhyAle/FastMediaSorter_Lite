# The shared payload rules of the two Windows installers (L0307). Dot-source it:
#     . (Join-Path $PSScriptRoot 'lib\InstallerPayloadSelection.ps1')
#
# One User stage per version feeds BOTH setups. This file holds what that stage must carry, how the
# stage is judged complete and recorded (a payload manifest beside it), how the Server setup's stage is
# derived from it, and which ISCC defines the recorded options turn into - so the two installers cannot
# disagree about the pair's options. The component and file mapping lives in tools\installer-components.json.

$script:InstallerLibRoot = $PSScriptRoot

function Test-InstallerStageFile {
    param([Parameter(Mandatory)][string]$RelativePath, [switch]$SkipOcr)

    # Models are populated separately from the verified manifest, according to the options.
    if ($RelativePath -like 'exchange\*' -or $RelativePath -like 'tessdata\*' -or
        $RelativePath -like 'tessdata-best\*') { return $false }
    if ($SkipOcr -and (($RelativePath -like 'x86\*' -and $RelativePath -ne 'x86\libwebpdecoder.dll') -or
        $RelativePath -like 'libvlc\win-x86\*' -or $RelativePath -like 'runtimes\win-x86\*')) { return $false }
    return $true
}

function Get-InstallerComponentMap {
    $path = Join-Path $script:InstallerLibRoot '..\installer-components.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The installer component map is missing: $path" }
    Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

# YY.M.D.HHmm -> the numeric form a version resource holds (26.7.30.0108 is stored as 26.7.30.108).
function ConvertTo-InstallerVersionNumbers {
    param([Parameter(Mandatory)][string]$Version)
    if ($Version -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') { throw "Invalid version '$Version'; expected YY.M.D.HHmm." }
    ($Version.Split('.') | ForEach-Object { [int]$_ }) -join '.'
}

function Get-InstallerPayloadManifestPath {
    param([Parameter(Mandatory)][string]$Stage)
    $Stage.TrimEnd('\', '/') + '.payload.json'
}

# Does a capability's payload belong in a stage built with these options?
function Test-InstallerCapabilityExpected {
    param([Parameter(Mandatory)]$Capability, [Parameter(Mandatory)][string]$OcrModels, [Parameter(Mandatory)][bool]$Exchange)
    switch ([string]$Capability.payload) {
        'always'   { return $true }
        'ocr-fast' { return @('fast', 'best') -contains $OcrModels }
        'ocr-best' { return $OcrModels -eq 'best' }
        'gate'     { return $Exchange }
        default    { throw "Unknown payload rule '$($Capability.payload)' for '$($Capability.id)' in installer-components.json." }
    }
}

# The version resource of one staged executable must equal the requested version.
function Assert-InstallerExeVersion {
    param([Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Relative, [Parameter(Mandatory)][string]$Version, [string]$Remedy)
    $expected = ConvertTo-InstallerVersionNumbers $Version
    $path = Join-Path $Stage $Relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing $Relative in the User stage '$Stage'. $Remedy"
    }
    $info = (Get-Item -LiteralPath $path).VersionInfo
    $actual = @($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart) -join '.'
    if ($actual -ne $expected) {
        throw "User stage '$Stage' contains $Relative version $actual, expected $expected. $Remedy"
    }
}

function Get-InstallerStageRelativePaths {
    param([Parameter(Mandatory)][string]$Stage)
    $root = (Resolve-Path -LiteralPath $Stage).Path.TrimEnd('\')
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $file.FullName.Substring($root.Length).TrimStart('\')
    }
}

function Test-InstallerStagePathPresent {
    param([Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Relative)
    $path = Join-Path $Stage $Relative
    if (Test-Path -LiteralPath $path -PathType Container) {
        return [bool](Get-ChildItem -LiteralPath $path -Recurse -File -Force | Select-Object -First 1)
    }
    return (Test-Path -LiteralPath $path -PathType Leaf)
}

# The stage-relative files a capability owns ('viewer' owns the rest of the stage).
function Get-InstallerCapabilityFiles {
    param([Parameter(Mandatory)]$Map, [Parameter(Mandatory)]$Capability, [Parameter(Mandatory)][string[]]$StageFiles)
    if ([string]$Capability.id -eq 'viewer') {
        $claimed = @($Map.capabilities | Where-Object { [string]$_.id -ne 'viewer' } | ForEach-Object { @($_.stage) })
        foreach ($rel in $StageFiles) {
            $taken = $false
            foreach ($prefix in $claimed) {
                if ($rel -eq $prefix -or $rel.StartsWith($prefix + '\', [StringComparison]::OrdinalIgnoreCase)) { $taken = $true; break }
            }
            if (-not $taken) { $rel }
        }
        return
    }
    foreach ($prefix in @($Capability.stage)) {
        foreach ($rel in $StageFiles) {
            if ($rel -eq $prefix -or $rel.StartsWith($prefix + '\', [StringComparison]::OrdinalIgnoreCase)) { $rel }
        }
    }
}

# Judge a freshly built User stage against the options the pair was requested with. Throws an
# actionable message on the first thing that is wrong; returns nothing.
function Assert-InstallerStageComplete {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][ValidateSet('none', 'fast', 'best')][string]$OcrModels,
        [Parameter(Mandatory)][bool]$Exchange
    )
    $remedy = "Run tools\Build-Installer.ps1 -Version $Version with the intended payload options, then retry."
    if (-not (Test-Path -LiteralPath $Stage -PathType Container)) { throw "The User stage '$Stage' does not exist. $remedy" }
    foreach ($exe in @('FastMediaSorter_LITE.exe', 'FastMediaSorter_x86.exe', 'FastMediaSorterCompanion.exe')) {
        Assert-InstallerExeVersion -Stage $Stage -Relative $exe -Version $Version -Remedy $remedy
    }

    $sharing = @('companion\fms-share-worker.exe', 'filedo\filedo.exe', 'filedo\filedo.sha256', 'filedo\release.json')
    foreach ($rel in $sharing) {
        if (-not (Test-Path -LiteralPath (Join-Path $Stage $rel) -PathType Leaf)) { throw "Missing $rel in the User stage '$Stage'. $remedy" }
    }
    $record = (Get-Content -LiteralPath (Join-Path $Stage 'filedo\filedo.sha256') -Raw).Trim()
    if ($record -notmatch '^([a-fA-F0-9]{64})\s+\*?filedo\.exe$' -or
        (Get-FileHash -LiteralPath (Join-Path $Stage 'filedo\filedo.exe') -Algorithm SHA256).Hash -ne $Matches[1]) {
        throw "The FileDO checksum is invalid in the User stage '$Stage'. $remedy"
    }

    $map = Get-InstallerComponentMap
    foreach ($capability in $map.capabilities) {
        if (-not $capability.stage) { continue }   # the viewer owns the rest of the stage, no named path
        $expected = Test-InstallerCapabilityExpected -Capability $capability -OcrModels $OcrModels -Exchange $Exchange
        foreach ($rel in @($capability.stage)) {
            $present = Test-InstallerStagePathPresent -Stage $Stage -Relative $rel
            if ($expected -and -not $present) {
                throw "The User stage '$Stage' has no $rel, the payload of '$($capability.label)' (options: OCR models '$OcrModels', exchange gate $(if ($Exchange) { 'open' } else { 'shut' })). A setup is never packaged without a payload its options ask for. $remedy"
            }
            if (-not $expected -and $present) {
                throw "The User stage '$Stage' carries $rel, the payload of '$($capability.label)', but the options say it is not part of this package (OCR models '$OcrModels', exchange gate $(if ($Exchange) { 'open' } else { 'shut' })). Rebuild the stage with the options you mean."
            }
        }
    }
    $deep = @('libvlc\win-x64\libvlc.dll', 'libvlc\win-x64\libvlccore.dll')
    if ($OcrModels -ne 'none') { $deep += 'tessdata\eng.traineddata' }
    if ($OcrModels -eq 'best') { $deep += 'tessdata-best\eng.traineddata' }
    if ($Exchange) { $deep += 'exchange\fms-exchange-server.exe' }
    foreach ($rel in $deep) {
        if (-not (Test-Path -LiteralPath (Join-Path $Stage $rel) -PathType Leaf)) { throw "Missing $rel in the User stage '$Stage'. $remedy" }
    }
}

# Record the stage: version, the pair's options and every file with its size and SHA-256. The record
# sits BESIDE the stage (never inside it: the User setup compiles the stage's wildcard into core).
function New-InstallerPayloadManifest {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][ValidateSet('none', 'fast', 'best')][string]$OcrModels,
        [Parameter(Mandatory)][bool]$Exchange
    )
    Assert-InstallerStageComplete -Stage $Stage -Version $Version -OcrModels $OcrModels -Exchange $Exchange
    $root = (Resolve-Path -LiteralPath $Stage).Path.TrimEnd('\')
    $files = foreach ($rel in (Get-InstallerStageRelativePaths -Stage $root | Sort-Object)) {
        $full = Join-Path $root $rel
        [ordered]@{ path = $rel; size = (Get-Item -LiteralPath $full).Length; sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash }
    }
    $manifest = [ordered]@{
        schema    = 1
        version   = $Version
        ocrModels = $OcrModels
        exchange  = $Exchange
        files     = @($files)
    }
    $path = Get-InstallerPayloadManifestPath -Stage $root
    [IO.File]::WriteAllText($path, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Write-Host "Payload manifest: $path ($(@($files).Count) files; OCR models '$OcrModels'; exchange gate $(if ($Exchange) { 'open' } else { 'shut' }))"
    Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Read-InstallerPayloadManifest {
    param([Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Version)
    $remedy = "Run tools\Build-Installer.ps1 -Version $Version with the intended payload options first (it records the stage), then retry."
    $path = Get-InstallerPayloadManifestPath -Stage $Stage
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The User stage '$Stage' has no payload manifest ($path): it was not produced by this version's User packaging, so its options and freshness cannot be established. $remedy"
    }
    $manifest = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([int]$manifest.schema -ne 1) { throw "Unsupported payload manifest schema '$($manifest.schema)' in $path." }
    if ([string]$manifest.version -ne $Version) {
        throw "The payload manifest $path records version $($manifest.version), but this packaging is for $Version. The stage belongs to another build. $remedy"
    }
    if (@('none', 'fast', 'best') -notcontains [string]$manifest.ocrModels) { throw "The payload manifest $path records an unknown OCR mode '$($manifest.ocrModels)'." }
    $manifest
}

# The stage on disk must still be exactly what the manifest recorded: no file missing, none added,
# none changed. A stale or half-rebuilt stage cannot pass.
function Assert-InstallerStageMatchesManifest {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Stage)
    $root = (Resolve-Path -LiteralPath $Stage).Path.TrimEnd('\')
    $remedy = "Rebuild the User stage with tools\Build-Installer.ps1 -Version $($Manifest.version) and retry."
    $expected = @{}
    foreach ($f in $Manifest.files) { $expected[[string]$f.path] = $f }
    $actual = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($rel in Get-InstallerStageRelativePaths -Stage $root) { [void]$actual.Add($rel) }
    $missing = @($expected.Keys | Where-Object { -not $actual.Contains($_) } | Sort-Object)
    $extra = @($actual | Where-Object { -not $expected.ContainsKey($_) } | Sort-Object)
    $changed = New-Object Collections.Generic.List[string]
    foreach ($rel in ($expected.Keys | Where-Object { $actual.Contains($_) } | Sort-Object)) {
        $full = Join-Path $root $rel
        $f = $expected[$rel]
        if ((Get-Item -LiteralPath $full).Length -ne [int64]$f.size -or
            (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne [string]$f.sha256) { $changed.Add($rel) }
    }
    if ($missing.Count -or $extra.Count -or $changed.Count) {
        $parts = @()
        if ($missing.Count) { $parts += "missing: $((@($missing | Select-Object -First 5)) -join ', ')$(if ($missing.Count -gt 5) { ' ..' })" }
        if ($extra.Count)   { $parts += "unexpected: $((@($extra | Select-Object -First 5)) -join ', ')$(if ($extra.Count -gt 5) { ' ..' })" }
        if ($changed.Count) { $parts += "changed: $((@($changed | Select-Object -First 5)) -join ', ')$(if ($changed.Count -gt 5) { ' ..' })" }
        throw "The User stage '$Stage' no longer matches its payload manifest ($($parts -join '; ')). The stage is stale or was modified after it was recorded. $remedy"
    }
}

# The ISCC defines both setups take from the pair's recorded options.
function Get-InstallerIsccDefines {
    param([Parameter(Mandatory)]$Manifest)
    $defines = @()
    if (-not [bool]$Manifest.exchange) { $defines += '/DNoExchange' }
    if ([string]$Manifest.ocrModels -eq 'none') { $defines += '/DNoOcrModels' }
    if ([string]$Manifest.ocrModels -eq 'best') { $defines += '/DOcrBest' }
    , $defines
}
