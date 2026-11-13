<# Prepare the M-5 scene matrix. Exit 0: matrix written or self-test passed; 1: invalid input or write failure. #>
[CmdletBinding(DefaultParameterSetName = 'Generate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Generate')][string] $InputDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Generate')][string] $PhotoPath,
    [Parameter(Mandatory, ParameterSetName = 'Generate')][string] $OutputPath,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')][switch] $SelfTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Get-InputNames {
    'black', 'white', 'mid-grey', 'green-0-205-90', 'black-white-split'
    1..24 | ForEach-Object { 'patch-{0:D2}' -f $_ }
}

function Write-SceneMatrix([string] $Directory, [string] $Photo, [string] $Output) {
    $directoryPath = [IO.Path]::GetFullPath($Directory, $PWD.Path)
    $photoFullPath = [IO.Path]::GetFullPath($Photo, $PWD.Path)
    $outputFullPath = [IO.Path]::GetFullPath($Output, $PWD.Path)
    if (Test-Path -LiteralPath $outputFullPath) { throw "Output already exists: $outputFullPath" }
    if (-not (Test-Path -LiteralPath $photoFullPath -PathType Leaf)) { throw "Photo missing: $photoFullPath" }
    $inputs = [System.Collections.Generic.List[object]]::new()
    foreach ($name in @(Get-InputNames)) {
        $path = Join-Path $directoryPath ($name + '.png')
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Scene input missing: $path" }
        $inputs.Add([pscustomobject]@{ Name = $name; Path = $path })
    }
    $inputs.Add([pscustomobject]@{ Name = 'busy-photo'; Path = $photoFullPath })
    $scenes = foreach ($inputImage in $inputs) {
        foreach ($scheme in @('black', 'white', 'dynamic')) {
            foreach ($opacity in @(100, 86, 60, 49, 30, 0)) {
                foreach ($theme in @('light', 'dark')) {
                    foreach ($scale in @(100, 175)) {
                        [pscustomobject]@{
                            SceneId = '{0}_{1}_{2}_{3}_{4}' -f $inputImage.Name, $scheme, $opacity, $theme, $scale
                            ImagePath = $inputImage.Path; Scheme = $scheme; Opacity = $opacity
                            Theme = $theme; ScalePercent = $scale
                        }
                    }
                }
            }
        }
    }
    # CreateNew keeps a competing process from replacing an output after the existence check.
    $stream = [IO.File]::Open($outputFullPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $scenes | ConvertTo-Csv -NoTypeInformation | ForEach-Object { $writer.WriteLine($_) } }
        finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
    return $scenes.Count
}

try {
    if ($SelfTest) {
        $root = Join-Path $repo ('temp/L0303/matrix-selftest-' + [guid]::NewGuid().ToString('N'))
        $inputs = Join-Path $root 'inputs'
        New-Item -ItemType Directory -Path $inputs -Force | Out-Null
        foreach ($name in @(Get-InputNames)) { [IO.File]::WriteAllText((Join-Path $inputs ($name + '.png')), '') }
        $photo = Join-Path $root 'photo.jpg'
        [IO.File]::WriteAllText($photo, '')
        $out = Join-Path $root 'scenes.csv'
        if ((Write-SceneMatrix $inputs $photo $out) -ne 2160) { throw 'Wrong scene count.' }
        $rows = @(Import-Csv -LiteralPath $out)
        if ($rows.Count -ne 2160 -or @($rows.SceneId | Sort-Object -Unique).Count -ne 2160 -or
            @($rows | Where-Object { $_.SceneId -eq 'busy-photo_dynamic_0_dark_175' }).Count -ne 1 -or
            @($rows | Where-Object { $_.SceneId -eq 'patch-24_white_49_light_100' }).Count -ne 1) {
            throw 'Scene matrix axes or identifiers are incorrect.'
        }
        $refused = 0
        try { Write-SceneMatrix $inputs $photo $out | Out-Null } catch { $refused++ }
        try { Write-SceneMatrix $inputs (Join-Path $root 'missing.jpg') (Join-Path $root 'missing-photo.csv') | Out-Null } catch { $refused++ }
        try { Write-SceneMatrix (Join-Path $root 'missing') $photo (Join-Path $root 'missing-input.csv') | Out-Null } catch { $refused++ }
        if ($refused -ne 3 -or (Test-Path -LiteralPath (Join-Path $root 'missing-photo.csv')) -or
            (Test-Path -LiteralPath (Join-Path $root 'missing-input.csv'))) { throw 'Input or overwrite refusal failed.' }
        Write-Output 'PanelColourSceneMatrix self-test: PASS (2160 unique scenes, axes and refusal checks)'
    } else {
        $count = Write-SceneMatrix $InputDirectory $PhotoPath $OutputPath
        Write-Output "PanelColourSceneMatrix: PASS ($count scenes in $OutputPath)"
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
