param(
    [Parameter(Mandatory = $true)]
    [string]$StageDir,

    [switch]$IncludeBest
)

$ErrorActionPreference = "Stop"

$releaseLanguages = @(
    "eng", "rus", "ukr", "bel", "deu", "fra", "spa", "ita", "por", "nld",
    "pol", "ces", "slk", "swe", "nor", "dan", "fin", "tur", "ell", "bul",
    "ron", "hun", "jpn", "kor", "chi_sim", "chi_tra", "ara", "heb", "hin",
    "tha", "vie", "ind", "fas"
)

$stageRoot = (Resolve-Path $StageDir).Path
$fastBaseUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main"
$bestBaseUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/main"

function Remove-IfExists {
    param([string]$PathToRemove)

    if (Test-Path $PathToRemove) {
        Remove-Item -LiteralPath $PathToRemove -Recurse -Force
        Write-Host "Removed x86-only payload: $PathToRemove"
    }
}

function Download-LanguageSet {
    param(
        [string]$BaseUrl,
        [string]$TargetDir,
        [string]$Label
    )

    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

    foreach ($code in $releaseLanguages) {
        $dest = Join-Path $TargetDir ($code + ".traineddata")
        if (Test-Path $dest) {
            Write-Host "Keeping existing $Label model: $code"
            continue
        }

        $tmp = $dest + ".part"
        $uri = "$BaseUrl/$code.traineddata"
        Write-Host "Downloading $Label model: $code"
        Invoke-WebRequest -Uri $uri -OutFile $tmp
        Move-Item -LiteralPath $tmp -Destination $dest -Force
    }
}

Remove-IfExists (Join-Path $stageRoot "x86")
Remove-IfExists (Join-Path $stageRoot "libvlc\win-x86")
Remove-IfExists (Join-Path $stageRoot "runtimes\win-x86")

Download-LanguageSet -BaseUrl $fastBaseUrl -TargetDir (Join-Path $stageRoot "tessdata") -Label "fast"

if ($IncludeBest) {
    Download-LanguageSet -BaseUrl $bestBaseUrl -TargetDir (Join-Path $stageRoot "tessdata-best") -Label "best"
}

Write-Host "Offline OCR payload prepared in: $stageRoot"
