<#
    Builds an MSIX package for FastMediaSorter LITE (Microsoft Store / sideload).

    What it does:
      1. Builds Release with MSBuild (unless -NoBuild) and locates FastMediaSorter_LITE.exe.
      2. Derives a Store-legal 4-part version (revision forced to 0) from the exe's YY.M.D.HHmm stamp.
      3. Stages the full offline payload (exe + LibVLC + tessdata + flags, minus *.pdb/*.xml) and
         generates the required logo PNGs from assets\icons\store-icon-256.png.
      4. Fills the AppxManifest.xml placeholders (Identity Name / Publisher / display name / version).
      5. Packs it into msix\dist\FastMediaSorter_LITE-<version>-x64.msix with makeappx.

    For the STORE you submit the UNSIGNED .msix - Microsoft re-signs it during certification, so you
    don't need a paid code-signing certificate. Set -IdentityName / -Publisher / -PublisherDisplayName
    to the exact values reserved for you in Partner Center (Product > Product identity).

    For LOCAL testing add -SelfSign: it creates a self-signed cert whose subject equals -Publisher,
    signs the package, and prints how to trust + install it. (Self-signing requires the manifest
    Publisher to match the cert subject - keep them equal.)

    Examples:
      # Store-ready package (fill these from Partner Center):
      .\build-msix.ps1 -IdentityName "SZA.FastMediaSorterLITE" -Publisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" -PublisherDisplayName "SZA"

      # Local sideload test (self-signed), reusing an existing Release build:
      .\build-msix.ps1 -SelfSign -NoBuild

    Tessdata: by default only the smaller `tessdata_fast` packs are bundled (offline OCR works out of
    the box; `best` models download on demand to %LOCALAPPDATA%). Add -IncludeBestOcr to also bundle
    the larger `tessdata_best` packs, or -SkipOcrPayload to skip the download entirely (faster local
    iteration; OCR then downloads packs on first use).
#>
[CmdletBinding()]
param(
    [string] $IdentityName         = 'SZA.FastMediaSorterLITE',
    [string] $Publisher            = 'CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD',
    [string] $PublisherDisplayName = 'SZA',
    [string] $Configuration        = 'Release',
    [switch] $NoBuild,
    [switch] $SelfSign,
    [switch] $IncludeBestOcr,
    [switch] $SkipOcrPayload
)
$ErrorActionPreference = 'Stop'

$MsixDir    = $PSScriptRoot
$RepoRoot   = Split-Path $MsixDir -Parent
$Solution   = Join-Path $RepoRoot 'FastMediaSorter.sln'
$ReleaseDir = Join-Path $RepoRoot ("bin\" + $Configuration)
$IconPng    = Join-Path $RepoRoot 'assets\icons\store-icon-256.png'
$OcrPayload = Join-Path $RepoRoot 'tools\Prepare-OcrOfflinePayload.ps1'
$Stage      = Join-Path $MsixDir 'stage'
$Dist       = Join-Path $MsixDir 'dist'
$Manifest   = Join-Path $MsixDir 'AppxManifest.xml'

function Find-SdkTool([string] $name) {
    $tool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK)." }
    return $tool.FullName
}

function Resolve-MsBuild {
    $fromVsWhere = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
        -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2>$null |
        Select-Object -First 1
    if ($fromVsWhere -and (Test-Path $fromVsWhere)) { return $fromVsWhere }
    $fallback = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $fallback) { return $fallback }
    throw "MSBuild not found (install Visual Studio 2022 or the Build Tools)."
}

function Resolve-NuGet {
    $cmd = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $nuget = Join-Path $RepoRoot 'tools\nuget.exe'
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Path (Split-Path $nuget) -Force | Out-Null
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
    }
    return $nuget
}

function Get-RelativePath([string] $BasePath, [string] $TargetPath) {
    $baseResolved   = (Resolve-Path $BasePath).Path.TrimEnd('\') + '\'
    $targetResolved = (Resolve-Path $TargetPath).Path
    $baseUri   = New-Object System.Uri($baseResolved)
    $targetUri = New-Object System.Uri($targetResolved)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', '\'))
}

# --- 1. Build --------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host 'Building Release...' -ForegroundColor Cyan
    $msbuild = Resolve-MsBuild
    $nuget   = Resolve-NuGet
    & $nuget restore $Solution -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed (exit $LASTEXITCODE)." }
    & $msbuild $Solution /p:Configuration=$Configuration /p:Platform="Any CPU" /t:Rebuild /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed (exit $LASTEXITCODE)." }
}
$Exe = Join-Path $ReleaseDir 'FastMediaSorter_LITE.exe'
if (-not (Test-Path $Exe)) { throw "FastMediaSorter_LITE.exe not found at $Exe (build first, or drop -NoBuild)." }

# --- 2. Store-legal version (revision must be 0) ---------------------------
# Exe is stamped YY.M.D.HHmm. Map to Major.Minor.Build.0 within the 0..65535 per-part limit:
#   Major = YY, Minor = M*100+D, Build = HHmm, Revision = 0  (monotonic over time, unique per minute).
$fileVer = (Get-Item $Exe).VersionInfo.FileVersion
$p = $fileVer.Split('.')
if ($p.Count -lt 4) { throw "Unexpected exe version '$fileVer' (want YY.M.D.HHmm)." }
$yy = [int]$p[0]; $m = [int]$p[1]; $d = [int]$p[2]; $hhmm = [int]$p[3]
$MsixVersion = "$yy.$($m*100+$d).$hhmm.0"
Write-Host "Exe version $fileVer  ->  MSIX version $MsixVersion" -ForegroundColor Green

# --- 3. Stage the offline payload ------------------------------------------
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Stage 'Assets') -Force | Out-Null

Write-Host 'Staging release payload (excluding *.pdb / *.xml)...'
Get-ChildItem $ReleaseDir -Recurse -File |
    Where-Object { $_.Extension -notin '.pdb', '.xml' } |
    ForEach-Object {
        $rel  = Get-RelativePath $ReleaseDir $_.FullName
        $dest = Join-Path $Stage $rel
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        Copy-Item $_.FullName $dest -Force
    }
foreach ($extra in 'README.md', 'LICENSE') {
    $src = Join-Path $RepoRoot $extra
    if (Test-Path $src) { Copy-Item $src $Stage -Force }
}

# Android Folder Share payload: the Companion app (Share Manager, net10) published
# self-contained next to the LITE exe, plus the committed Go SFTP worker under
# companion\. The manifest declares both as <Application> nodes; these are their files.
$companionProj = Join-Path $RepoRoot 'src\FastMediaSorterCompanion\FastMediaSorterCompanion.vbproj'
$companionPub  = Join-Path $MsixDir 'companion-publish-tmp'
Write-Host 'Publishing Companion (Share Manager, net10 self-contained)...'
& dotnet publish $companionProj -c Release -r win-x64 -o $companionPub -v minimal --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Companion) failed (exit $LASTEXITCODE)." }
$companionExe = Join-Path $companionPub 'FastMediaSorterCompanion.exe'
if (-not (Test-Path $companionExe)) { throw "Companion exe not found at $companionExe after publish." }
Copy-Item $companionExe $Stage -Force
Remove-Item $companionPub -Recurse -Force

$workerSrc = Join-Path $RepoRoot 'payload\companion'
if (-not (Test-Path (Join-Path $workerSrc 'fms-share-worker.exe'))) { throw "SFTP worker missing at $workerSrc (should be committed in the repo)." }
$stageCompanion = Join-Path $Stage 'companion'
New-Item -ItemType Directory -Path $stageCompanion -Force | Out-Null
Copy-Item (Join-Path $workerSrc '*') $stageCompanion -Recurse -Force
Write-Host 'Staged Companion exe + SFTP worker.'

# Trim x86-only trees and bundle offline tessdata (same tool the GitHub release uses).
if (-not $SkipOcrPayload) {
    if (-not (Test-Path $OcrPayload)) { throw "OCR payload tool not found: $OcrPayload" }
    if ($IncludeBestOcr) { & $OcrPayload -StageDir $Stage -IncludeBest }
    else                 { & $OcrPayload -StageDir $Stage }
}

# Generate the logo PNGs from the 256px master.
if (-not (Test-Path $IconPng)) { throw "Icon master not found: $IconPng" }
Add-Type -AssemblyName System.Drawing
$srcImg = [System.Drawing.Image]::FromFile($IconPng)
try {
    $logos = @{
        'Square44x44Logo.png'   = 44
        'StoreLogo.png'         = 50
        'Square71x71Logo.png'   = 71
        'Square150x150Logo.png' = 150
    }
    foreach ($kv in $logos.GetEnumerator()) {
        $size = $kv.Value
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($srcImg, 0, 0, $size, $size)
        $bmp.Save((Join-Path $Stage "Assets\$($kv.Key)"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    }
}
finally { $srcImg.Dispose() }
Write-Host 'Generated logo assets.'

# --- 4. Fill the manifest placeholders -------------------------------------
$xml = Get-Content $Manifest -Raw
$xml = $xml.Replace('__IDENTITY_NAME__',     $IdentityName)
$xml = $xml.Replace('__PUBLISHER__',         $Publisher)
$xml = $xml.Replace('__PUBLISHER_DISPLAY__', $PublisherDisplayName)
$xml = $xml.Replace('__VERSION__',           $MsixVersion)
# AppxManifest.xml must be at the package root.
Set-Content -Path (Join-Path $Stage 'AppxManifest.xml') -Value $xml -Encoding UTF8

# --- 5. Pack ----------------------------------------------------------------
New-Item -ItemType Directory -Path $Dist -Force | Out-Null
$MsixPath = Join-Path $Dist "FastMediaSorter_LITE-$MsixVersion-x64.msix"
$makeappx = Find-SdkTool 'makeappx.exe'
& $makeappx pack /d $Stage /p $MsixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)." }
Write-Host "Packed: $MsixPath" -ForegroundColor Green

# --- 6. Optional self-sign for local sideload testing -----------------------
if ($SelfSign) {
    Write-Host 'Self-signing for local testing...' -ForegroundColor Cyan
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
            -KeyUsage DigitalSignature -FriendlyName 'FastMediaSorter MSIX test cert' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "Created test cert: $($cert.Thumbprint)"
    }
    $signtool = Find-SdkTool 'signtool.exe'
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $MsixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }

    $cer = Join-Path $Dist 'FastMediaSorter-test-cert.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Write-Host ''
    Write-Host 'Signed. To install locally, trust the cert once (RUN AS ADMIN):' -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host 'Then install the package:'
    Write-Host "  Add-AppxPackage `"$MsixPath`""
    Write-Host 'Launch it (Add-AppxPackage installs but does not start the app):'
    Write-Host "  explorer.exe shell:AppsFolder\$IdentityName`_$($cert.Subject -replace '^CN=','')!FastMediaSorterLITE   # or just use the Start menu"
}
else {
    Write-Host ''
    Write-Host 'Unsigned package ready for the Store (Microsoft re-signs on certification).' -ForegroundColor Yellow
    Write-Host 'For a LOCAL test build instead, re-run with -SelfSign.'
}
