$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\lib\Verdict.ps1')
. (Join-Path $repo 'tools\lib\InstallerPayloadSelection.ps1')
trap { Exit-Verdict 'InstallerStageFilter' 1 $_.Exception.Message }

$cases = @(
    @{ Path = 'exchange\fms-exchange-server.exe'; Full = $false; Reduced = $false }
    @{ Path = 'tessdata\eng.traineddata'; Full = $false; Reduced = $false }
    @{ Path = 'tessdata-best\eng.traineddata'; Full = $false; Reduced = $false }
    @{ Path = 'x86\libwebpdecoder.dll'; Full = $true; Reduced = $true }
    @{ Path = 'x86\tesseract50.dll'; Full = $true; Reduced = $false }
    @{ Path = 'libvlc\win-x86\libvlc.dll'; Full = $true; Reduced = $false }
    @{ Path = 'runtimes\win-x86\native\lib.dll'; Full = $true; Reduced = $false }
    @{ Path = 'libvlc\win-x64\libvlc.dll'; Full = $true; Reduced = $true }
    @{ Path = 'pdfium.dll'; Full = $true; Reduced = $true }
)
foreach ($case in $cases) {
    foreach ($mode in @('Full', 'Reduced')) {
        $actual = Test-InstallerStageFile -RelativePath $case.Path -SkipOcr:($mode -eq 'Reduced')
        if ($actual -ne $case[$mode]) { throw "Unexpected $mode staging decision for $($case.Path): $actual" }
    }
}
Exit-Verdict 'InstallerStageFilter' 0 'model and exchange isolation; x86 WebP preservation; reduced runtime selection'
