<# Create the synthetic image inputs for the panel colour scene driver.
   Exit 0: inputs created or self-test passed; 1: invalid input or generation failed. #>
[CmdletBinding(DefaultParameterSetName = 'Generate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Generate')]
    [string] $OutputDirectory,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Get-SceneColours {
    $corpus = Get-Content -Raw -LiteralPath (Join-Path $repo 'docs/specifications/L0303_panel-label-colour-model/PanelColourBaseline.cs')
    $section = [regex]::Match($corpus, '(?s)static List<Named> ColorChecker\(\).*?static List<Named>')
    if (-not $section.Success) { throw 'ColorChecker corpus is missing.' }
    $matches = [regex]::Matches($section.Value, 'new Named\("([^"]+)", new Rgb\((\d+),(\d+),(\d+)\)\)')
    if ($matches.Count -ne 24) { throw "Expected 24 ColorChecker colours, found $($matches.Count)." }
    $colours = [System.Collections.Generic.List[object]]::new()
    $colours.Add([pscustomobject]@{ Name = 'black'; R = 0; G = 0; B = 0 })
    $colours.Add([pscustomobject]@{ Name = 'white'; R = 255; G = 255; B = 255 })
    $colours.Add([pscustomobject]@{ Name = 'mid-grey'; R = 128; G = 128; B = 128 })
    $colours.Add([pscustomobject]@{ Name = 'green-0-205-90'; R = 0; G = 205; B = 90 })
    $index = 0
    foreach ($match in $matches) {
        $index++
        $colours.Add([pscustomobject]@{
            Name = ('patch-{0:D2}' -f $index)
            R = [int]$match.Groups[2].Value; G = [int]$match.Groups[3].Value; B = [int]$match.Groups[4].Value
        })
    }
    return $colours
}

function New-SceneInputs([string] $Path, [int] $Width, [int] $Height) {
    $Path = [System.IO.Path]::GetFullPath($Path, $PWD.Path)
    if (Test-Path -LiteralPath $Path) { throw "Output already exists: $Path" }
    $colours = @(Get-SceneColours)
    New-Item -ItemType Directory -Path $Path -ErrorAction Stop | Out-Null
    foreach ($colour in $colours) {
        $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.Clear([System.Drawing.Color]::FromArgb($colour.R, $colour.G, $colour.B)) }
            finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $Path ($colour.Name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Black)
            $graphics.FillRectangle([System.Drawing.Brushes]::White, [int]($Width / 2), 0, [int]($Width / 2), $Height)
        } finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $Path 'black-white-split.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

try {
    Add-Type -AssemblyName System.Drawing
    if ($SelfTest) {
        $testPath = Join-Path $repo ('temp/L0303/scene-input-selftest-' + [guid]::NewGuid().ToString('N'))
        New-SceneInputs $testPath 32 24
        $colours = @(Get-SceneColours)
        if ($colours[4].R -ne 115 -or $colours[4].G -ne 82 -or $colours[4].B -ne 68 -or
            $colours[27].R -ne 52 -or $colours[27].G -ne 52 -or $colours[27].B -ne 52) {
            throw 'ColorChecker corpus endpoints are incorrect.'
        }
        if (@(Get-ChildItem -LiteralPath $testPath -Filter '*.png').Count -ne 29) { throw 'Expected 29 PNG inputs.' }
        foreach ($colour in $colours) {
            $bitmap = [System.Drawing.Bitmap]::new((Join-Path $testPath ($colour.Name + '.png')))
            try {
                $pixel = $bitmap.GetPixel(16, 12)
                if ($bitmap.Width -ne 32 -or $bitmap.Height -ne 24 -or $pixel.R -ne $colour.R -or $pixel.G -ne $colour.G -or $pixel.B -ne $colour.B) {
                    throw "Unexpected pixels in $($colour.Name)."
                }
            } finally { $bitmap.Dispose() }
        }
        $bitmap = [System.Drawing.Bitmap]::new((Join-Path $testPath 'black-white-split.png'))
        try {
            if ($bitmap.GetPixel(0, 12).ToArgb() -ne [System.Drawing.Color]::Black.ToArgb() -or
                $bitmap.GetPixel(31, 12).ToArgb() -ne [System.Drawing.Color]::White.ToArgb()) { throw 'Split pixels are incorrect.' }
        } finally { $bitmap.Dispose() }
        $rejected = $false
        try { New-SceneInputs $testPath 32 24 } catch { $rejected = $true }
        if (-not $rejected) { throw 'Existing output directory was not rejected.' }
        Write-Output 'PanelColourSceneInputs self-test: PASS (29 PNGs, solid centres and split endpoints; existing output rejected)'
    } else {
        New-SceneInputs $OutputDirectory 1024 768
        Write-Output "PanelColourSceneInputs: PASS (29 PNGs in $OutputDirectory)"
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
