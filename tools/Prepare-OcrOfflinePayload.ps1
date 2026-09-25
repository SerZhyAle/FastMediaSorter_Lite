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
# Windows PowerShell 5.1 redraws Invoke-WebRequest's progress bar per chunk, which
# makes a large download many times slower. Nothing here reads the progress stream.
$ProgressPreference = "SilentlyContinue"

$releaseLanguages = @(
    "eng", "rus", "ukr", "bel", "deu", "fra", "spa", "ita", "por", "nld",
    "pol", "ces", "slk", "swe", "nor", "dan", "fin", "tur", "ell", "bul",
    "ron", "hun", "jpn", "kor", "chi_sim", "chi_tra", "ara", "heb", "hin",
    "tha", "vie", "ind", "fas"
)

$stageRoot = (Resolve-Path $StageDir).Path
# Pinned to a commit, never to a branch: these files ship inside every package and
# native Tesseract parses them, so the bytes must be the ones reviewed and recorded -
# an upstream push to `main` (or a compromised upstream) must not enter a release
# unnoticed, and two builds of one version must carry the same models. Every file is
# checked against tools\tessdata.sha256 (see Assert-ModelHash). Moving to a newer
# commit means changing the SHA here AND regenerating every line of that manifest.
$fastCommit  = "87416418657359cb625c412a48b6e1d6d41c29bd"
$bestCommit  = "e12c65a915945e4c28e237a9b52bc4a8f39a0cec"
$fastBaseUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$fastCommit"
$bestBaseUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$bestCommit"

# "<sha256>  <set>/<code>.traineddata" per line, '#' comments allowed.
$manifestPath = Join-Path $PSScriptRoot "tessdata.sha256"
if (-not (Test-Path $manifestPath)) { throw "OCR model manifest not found: $manifestPath" }
$expectedHashes = @{}
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
    $parts = $trimmed -split '\s+', 2
    if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[0-9a-fA-F]{64}$') { throw "Malformed line in ${manifestPath}: $line" }
    $expectedHashes[$parts[1].Trim()] = $parts[0].ToUpperInvariant()
}

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

# Fails, naming the file, when its bytes are not the ones the manifest records.
function Assert-ModelHash {
    param([string]$FilePath, [string]$ManifestKey, [string]$Hint)

    if (-not $expectedHashes.ContainsKey($ManifestKey)) {
        throw "No SHA-256 for $ManifestKey in $manifestPath - add it (from the pinned commit) before bundling this model."
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $FilePath).Hash.ToUpperInvariant()
    if ($actual -ne $expectedHashes[$ManifestKey]) {
        throw "OCR model $FilePath does not match $ManifestKey in $manifestPath (got $actual, expected $($expectedHashes[$ManifestKey])). $Hint"
    }
}

# A transient network error must not turn a paid release run red on its own.
function Invoke-DownloadWithRetry {
    param([string]$Uri, [string]$OutFile)

    $attempts = 4
    for ($i = 1; $i -le $attempts; $i++) {
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
            return
        } catch {
            if ($i -eq $attempts) { throw }
            $delay = 5 * $i
            Write-Warning "Download of $Uri failed (attempt $i of $attempts): $($_.Exception.Message). Retrying in $delay s.."
            Start-Sleep -Seconds $delay
        }
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
        $fileName = $code + ".traineddata"
        $key = "$Label/$fileName"
        $cached = Join-Path $CacheDir $fileName
        if (-not (Test-Path $cached)) {
            $tmp = $cached + ".part"
            Write-Host "Downloading $Label model: $code"
            Invoke-DownloadWithRetry -Uri "$BaseUrl/$fileName" -OutFile $tmp
            # Checked BEFORE it enters the cache, so a bad download never becomes a
            # cached file that later runs would trust.
            try {
                Assert-ModelHash -FilePath $tmp -ManifestKey $key -Hint "The download was discarded."
            } catch {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
                throw
            }
            Move-Item -LiteralPath $tmp -Destination $cached -Force
            $downloaded++
        }
        $staged = Join-Path $TargetDir $fileName
        Copy-Item -LiteralPath $cached -Destination $staged -Force
        # The staged copy is what ships - checked for every file, cache hit or not, so
        # a corrupted or tampered cache (local, or restored by CI) stops the run here.
        Assert-ModelHash -FilePath $staged -ManifestKey $key -Hint "Delete the cached copy $cached to download it again."
    }
    $fromCache = $releaseLanguages.Count - $downloaded
    Write-Host "$Label models: $downloaded downloaded, $fromCache from cache, all $($releaseLanguages.Count) verified -> $TargetDir"
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
