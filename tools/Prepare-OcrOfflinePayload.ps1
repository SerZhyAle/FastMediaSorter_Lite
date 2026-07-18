param(
    [Parameter(Mandatory = $true)]
    [string]$StageDir,

    [switch]$IncludeBest,

    # Keep the 32-bit native trees (libvlc\win-x86, x86\tesseract) in the stage.
    # Off by default: the x64 package's mainline is FastMediaSorter_LITE.exe (x64),
    # and its FastMediaSorter_x86.exe sibling is only an escape hatch there - it
    # downloads the 32-bit codecs/OCR it needs into %LOCALAPPDATA% on first use
    # (OptionalRuntimeManager), so bundling ~100 MB of x86 natives for every user
    # is not worth it. Switch it on for a genuinely 32-bit, offline-first artifact.
    [switch]$KeepX86
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

# Persistent, version-independent download cache. The staging directory this
# script populates lives under stage\FastMediaSorter-<version>-...\ and its
# caller (Build-Installer.ps1 / Build-OfflineRelease.ps1) deletes and recreates
# it fresh on every run, so a "skip if already in the stage dir" check never
# actually skips anything - every local build re-downloaded all ~33 languages.
# Caching one level up, outside the per-version stage dir, makes repeat builds
# copy from disk instead of re-downloading.
$cacheRoot = Join-Path $PSScriptRoot "ocr-cache"

function Remove-IfExists {
    param([string]$PathToRemove)

    if (Test-Path $PathToRemove) {
        Remove-Item -LiteralPath $PathToRemove -Recurse -Force
        Write-Host "Removed x86-only payload: $PathToRemove"
    }
}

function Sync-LanguageSet {
    param(
        [string]$BaseUrl,
        [string]$CacheDir,
        [string]$TargetDir,
        [string]$Label
    )

    New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

    $downloaded = 0
    foreach ($code in $releaseLanguages) {
        $cached = Join-Path $CacheDir ($code + ".traineddata")
        if (-not (Test-Path $cached)) {
            $tmp = $cached + ".part"
            Write-Host "Downloading $Label model: $code"
            Invoke-WebRequest -Uri "$BaseUrl/$code.traineddata" -OutFile $tmp
            Move-Item -LiteralPath $tmp -Destination $cached -Force
            $downloaded++
        }
        Copy-Item -LiteralPath $cached -Destination (Join-Path $TargetDir ($code + ".traineddata")) -Force
    }
    $fromCache = $releaseLanguages.Count - $downloaded
    Write-Host "$Label models: $downloaded downloaded, $fromCache from cache -> $TargetDir"
}

if ($KeepX86) {
    Write-Host "Keeping the 32-bit native trees (-KeepX86): FastMediaSorter_x86.exe runs offline."
} else {
    Remove-IfExists (Join-Path $stageRoot "x86")
    Remove-IfExists (Join-Path $stageRoot "libvlc\win-x86")
    Remove-IfExists (Join-Path $stageRoot "runtimes\win-x86")
}

Sync-LanguageSet -BaseUrl $fastBaseUrl -CacheDir (Join-Path $cacheRoot "fast") -TargetDir (Join-Path $stageRoot "tessdata") -Label "fast"

if ($IncludeBest) {
    Sync-LanguageSet -BaseUrl $bestBaseUrl -CacheDir (Join-Path $cacheRoot "best") -TargetDir (Join-Path $stageRoot "tessdata-best") -Label "best"
}

Write-Host "Offline OCR payload prepared in: $stageRoot"
