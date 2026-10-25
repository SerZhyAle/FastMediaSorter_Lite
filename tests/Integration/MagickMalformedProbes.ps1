<#
.SYNOPSIS
    Specification 270 section 5: malformed BigTIFF counts and chains, ICC size/tag
    inconsistencies and over-budget headers, each opened by the mainline's real Magick.NET
    entry points inside its own bounded, disposable child process.

.DESCRIPTION
    The fixtures are crafted here, byte by byte, into a run-owned folder under %TEMP% (nothing
    of the owner's is read or touched). For each one a fresh pwsh child
    (MagickMalformedProbeChild.ps1) loads the linked viewer code from the built Lite.Tests.dll
    and drives the decoder (payload, GDI+ image, header size), the editor's donor-metadata read
    and the QR intake. A pixel limit checked after a hanging header parse would not catch a
    hang, so the parent bounds the child itself:

      HANG     the child did not finish within -TimeoutSeconds - it is killed      -> FAIL
      CRASH    the child ended without its full report (native fault, fail-fast)  -> FAIL
      MEMORY   the child's peak working set exceeded -PeakLimitMB                  -> FAIL
      BUDGET   an over-budget header was decoded instead of refused               -> FAIL

    A managed exception is an outcome, not a failure: every caller of these entry points
    catches. Two well-formed controls (a 1x1 BigTIFF and a 1x1 PNG with a valid ICC profile)
    must decode, or the malformed cases proved nothing about the BigTIFF/ICC readers -> COULD
    NOT VERIFY.

.PARAMETER Configuration
    Which Lite.Tests build to load (Debug is what tools/Invoke-Tests.ps1 builds). Default Debug.

.OUTPUTS
    Exit codes (CHECK-VERDICT): 0 PASS, 1 FAIL, 2 COULD NOT VERIFY (no build, a control did
    not decode). Last line: `MagickMalformedProbes: <WORD> (..)`.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$TimeoutSeconds = 60,
    [int]$PeakLimitMB = 1536,
    [string]$WorkDir = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'tools\lib\Verdict.ps1')
$check = 'MagickMalformedProbes'

$assembly = Join-Path $repo "tests\Lite.Tests\bin\$Configuration\net10.0-windows\Lite.Tests.dll"
if (-not (Test-Path $assembly)) {
    Exit-Verdict $check 2 "no build at $assembly - run tools/Invoke-Tests.ps1 -Suite lite first"
}
$pwshExe = (Get-Process -Id $PID).Path
if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) ("fms-270-malformed-" + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
Write-Subject $check "Lite.Tests.dll $Configuration ($((Get-Item $assembly).LastWriteTime.ToString('yyyy-MM-dd HH:mm'))), fixtures in $WorkDir"

# ------------------------------------------------------------------ byte helpers ----

function U16LE([int]$v) { [byte[]]@(($v -band 0xFF), (($v -shr 8) -band 0xFF)) }
function U32LE([uint64]$v) { [byte[]](0..3 | ForEach-Object { [byte](($v -shr (8 * $_)) -band 0xFF) }) }
function U64LE([uint64]$v) { [byte[]](0..7 | ForEach-Object { [byte](($v -shr (8 * $_)) -band 0xFF) }) }
function U32BE([uint64]$v) { [byte[]](3..0 | ForEach-Object { [byte](($v -shr (8 * $_)) -band 0xFF) }) }
function U16BE([int]$v) { [byte[]]@((($v -shr 8) -band 0xFF), ($v -band 0xFF)) }
function Ascii([string]$s) { [Text.Encoding]::ASCII.GetBytes($s) }
function Join-Bytes { $list = [Collections.Generic.List[byte]]::new(); foreach ($part in $args) { $list.AddRange([byte[]]$part) }; , $list.ToArray() }

$crcTable = [uint32[]]::new(256)
for ($n = 0; $n -lt 256; $n++) {
    [uint32]$c = $n
    for ($k = 0; $k -lt 8; $k++) { $c = if ($c -band 1) { [uint32](0xEDB88320u -bxor ($c -shr 1)) } else { [uint32]($c -shr 1) } }
    $crcTable[$n] = $c
}
function Crc32([byte[]]$data) {
    [uint32]$c = 0xFFFFFFFFu
    foreach ($b in $data) { $c = [uint32]($crcTable[($c -bxor $b) -band 0xFF] -bxor ($c -shr 8)) }
    [uint32]($c -bxor 0xFFFFFFFFu)
}
function Zlib([byte[]]$data) {
    $ms = [IO.MemoryStream]::new()
    $z = [IO.Compression.ZLibStream]::new($ms, [IO.Compression.CompressionLevel]::Optimal)
    $z.Write($data, 0, $data.Length); $z.Dispose()
    , $ms.ToArray()
}
function PngChunk([string]$type, [byte[]]$data) {
    $typed = Join-Bytes (Ascii $type) $data
    Join-Bytes (U32BE $data.Length) $typed (U32BE (Crc32 $typed))
}
$pngSignature = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

# A 1x1 RGB PNG, optionally with an iCCP chunk carrying $Icc.
function New-Png([byte[]]$Icc = $null, [uint32]$Width = 1, [uint32]$Height = 1) {
    $ihdr = Join-Bytes (U32BE $Width) (U32BE $Height) ([byte[]]@(8, 2, 0, 0, 0))
    $parts = @($pngSignature, (PngChunk 'IHDR' $ihdr))
    if ($Icc) { $parts += , (PngChunk 'iCCP' (Join-Bytes (Ascii 'icc') ([byte[]]@(0, 0)) (Zlib $Icc))) }
    $parts += , (PngChunk 'IDAT' (Zlib ([byte[]]@(0, 0x80, 0x40, 0x20))))
    $parts += , (PngChunk 'IEND' ([byte[]]@()))
    Join-Bytes @parts
}

# An ICC v4 display profile header (128 bytes) + tag table. $DeclaredSize overrides the size field.
function New-Icc([object[]]$Tags = @(), [int64]$TagCount = -1, [uint64]$DeclaredSize = 0, [byte[]]$Body = @()) {
    if ($TagCount -lt 0) { $TagCount = $Tags.Count }
    $table = Join-Bytes (U32BE $TagCount)
    foreach ($t in $Tags) { $table = Join-Bytes $table (Ascii $t[0]) (U32BE $t[1]) (U32BE $t[2]) }
    $length = 128 + $table.Length + $Body.Length
    $size = if ($DeclaredSize) { $DeclaredSize } else { $length }
    $header = Join-Bytes (U32BE $size) (Ascii 'none') ([byte[]]@(4, 0x30, 0, 0)) (Ascii 'mntr') (Ascii 'RGB ') (Ascii 'XYZ ') `
        ([byte[]]::new(12)) (Ascii 'acsp') ([byte[]]::new(24)) `
        (U32BE 0x0000F6D6) (U32BE 0x00010000) (U32BE 0x0000D32D) ([byte[]]::new(4)) ([byte[]]::new(16)) ([byte[]]::new(28))
    Join-Bytes $header $table $Body
}

# A well-formed sRGB-ish profile: wtpt + rXYZ/gXYZ/bXYZ + three gamma curves sharing one 'curv'.
function New-ValidIcc {
    $xyz = { param($x, $y, $z) Join-Bytes (Ascii 'XYZ ') ([byte[]]::new(4)) (U32BE $x) (U32BE $y) (U32BE $z) }
    $curv = Join-Bytes (Ascii 'curv') ([byte[]]::new(4)) (U32BE 1) (U16BE 0x0233) ([byte[]]::new(2))
    $tagNames = 'wtpt', 'rXYZ', 'gXYZ', 'bXYZ'
    $values = @((& $xyz 0xF6D6 0x10000 0xD32D), (& $xyz 0x6FA2 0x38F5 0x0390), (& $xyz 0x6299 0xB785 0x18DA), (& $xyz 0x24A0 0x0F84 0xB6CF))
    $tableLength = 4 + 12 * 7
    $offset = 128 + $tableLength
    $tags = @(); $body = [byte[]]@()
    for ($i = 0; $i -lt 4; $i++) { $tags += , @($tagNames[$i], $offset, 20); $body = Join-Bytes $body $values[$i]; $offset += 20 }
    foreach ($trc in 'rTRC', 'gTRC', 'bTRC') { $tags += , @($trc, $offset, $curv.Length) }
    $body = Join-Bytes $body $curv
    New-Icc -Tags $tags -Body $body
}

function New-Jpeg([byte[]]$Icc) {
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::new(8, 8)
    $ms = [IO.MemoryStream]::new()
    try { $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Jpeg) } finally { $bmp.Dispose() }
    $jpeg = $ms.ToArray()
    $app2 = Join-Bytes (Ascii "ICC_PROFILE") ([byte[]]@(0, 1, 1)) $Icc
    $segment = Join-Bytes ([byte[]]@(0xFF, 0xE2)) (U16BE ($app2.Length + 2)) $app2
    Join-Bytes ([byte[]]@(0xFF, 0xD8)) $segment ($jpeg[2..($jpeg.Length - 1)])
}

# TIFF / BigTIFF, little-endian, one IFD at the start. Entries: @(tag, type, count, value).
# $NextIfd: 0 ends the chain, any other value is written as the next-IFD offset (16/8 = itself).
function New-Tiff([switch]$Big, [object[]]$Entries, [uint64]$NextIfd = 0, [uint64]$DeclaredCount = 0, [byte[]]$Tail = @()) {
    $count = if ($DeclaredCount) { $DeclaredCount } else { [uint64]$Entries.Count }
    if ($Big) {
        $out = Join-Bytes (Ascii 'II') (U16LE 0x2B) (U16LE 8) (U16LE 0) (U64LE 16) (U64LE $count)
        foreach ($e in $Entries) { $out = Join-Bytes $out (U16LE $e[0]) (U16LE $e[1]) (U64LE $e[2]) (U64LE $e[3]) }
        $out = Join-Bytes $out (U64LE $NextIfd)
    } else {
        $out = Join-Bytes (Ascii 'II') (U16LE 0x2A) (U32LE 8) (U16LE ([int][Math]::Min($count, 65535)))
        foreach ($e in $Entries) { $out = Join-Bytes $out (U16LE $e[0]) (U16LE $e[1]) (U32LE $e[2]) (U32LE $e[3]) }
        $out = Join-Bytes $out (U32LE $NextIfd)
    }
    Join-Bytes $out $Tail
}
# A 1x1 8-bit greyscale strip; the pixel byte follows the IFD.
function Gray1x1Entries([uint64]$PixelOffset, [uint64]$Width = 1, [uint64]$Height = 1, [uint64]$StripCount = 1, [uint64]$StripBytes = 1) {
    $long8 = 16; $short = 3
    @(@(256, $short, 1, $Width), @(257, $short, 1, $Height), @(258, $short, 1, 8), @(259, $short, 1, 1),
      @(262, $short, 1, 1), @(273, $long8, $StripCount, $PixelOffset), @(277, $short, 1, 1), @(278, $short, 1, $Height),
      @(279, $long8, 1, $StripBytes))
}
$bigIfdEnd = 16 + 8 + 9 * 20 + 8   # header + count + 9 entries + next

# ---------------------------------------------------------------------- fixtures ----

$cases = [ordered]@{}
# Controls: must decode, or the readers below were never reached.
$cases['control-bigtiff-1x1.tif'] = @{ Kind = 'control'; Bytes = (New-Tiff -Big (Gray1x1Entries $bigIfdEnd) -Tail ([byte[]]@(0x80))) }
$cases['control-png-valid-icc.png'] = @{ Kind = 'control'; Bytes = (New-Png (New-ValidIcc)) }
# BigTIFF counts and chains.
$cases['bigtiff-ifd-count-2e63.tif'] = @{ Kind = 'malformed'; Bytes = (New-Tiff -Big (Gray1x1Entries $bigIfdEnd) -DeclaredCount 0x7FFFFFFFFFFFFFFF -Tail ([byte[]]@(0x80))) }
$cases['bigtiff-ifd-chain-loop.tif'] = @{ Kind = 'malformed'; Bytes = (New-Tiff -Big (Gray1x1Entries $bigIfdEnd) -NextIfd 16 -Tail ([byte[]]@(0x80))) }
$cases['bigtiff-strip-past-eof.tif'] = @{ Kind = 'malformed'; Bytes = (New-Tiff -Big (Gray1x1Entries 0x7FFFFFFFFFFF 4000 4000 1 0xFFFFFFFFu) -Tail ([byte[]]@(0x80))) }
$cases['bigtiff-strip-count-2e60.tif'] = @{ Kind = 'malformed'; Bytes = (New-Tiff -Big (Gray1x1Entries 16 1 1 0x1000000000000000) -Tail ([byte[]]@(0x80))) }
$cases['tiff-ifd-chain-loop.tif'] = @{ Kind = 'malformed'; Bytes = (New-Tiff (Gray1x1Entries (8 + 2 + 9 * 12 + 4) | ForEach-Object { , @($_[0], $(if ($_[1] -eq 16) { 4 } else { $_[1] }), $_[2], $_[3]) }) -NextIfd 8 -Tail ([byte[]]@(0x80))) }
# ICC size / tag inconsistencies, in a JPEG APP2 and a PNG iCCP.
$cases['icc-declared-2gb.jpg'] = @{ Kind = 'malformed'; Bytes = (New-Jpeg (New-Icc -DeclaredSize 0x7FFFFFF0)) }
$cases['icc-tag-count-4g.jpg'] = @{ Kind = 'malformed'; Bytes = (New-Jpeg (New-Icc -TagCount 0xFFFFFFFFu)) }
$cases['icc-tag-out-of-bounds.png'] = @{ Kind = 'malformed'; Bytes = (New-Png (New-Icc -Tags @(, @('desc', 0xFFFFFF00u, 0xFFFF)))) }
$cases['icc-declared-smaller-than-header.png'] = @{ Kind = 'malformed'; Bytes = (New-Png (New-Icc -DeclaredSize 16)) }
$cases['icc-tag-overlaps-header.jpg'] = @{ Kind = 'malformed'; Bytes = (New-Jpeg (New-Icc -Tags @(@('rXYZ', 0, 0xFFFFFFFFu), @('rTRC', 4, 0x7FFFFFFF)))) }
# Over-budget headers: must be refused, never decoded.
$cases['png-ihdr-1e6-squared.png'] = @{ Kind = 'budget'; Bytes = (New-Png -Width 1000000 -Height 1000000) }
$cases['gif-screen-65535-squared.gif'] = @{ Kind = 'budget'; Bytes = (Join-Bytes (Ascii 'GIF89a') (U16LE 0xFFFF) (U16LE 0xFFFF) ([byte[]]@(0, 0, 0, 0x2C)) (U16LE 0) (U16LE 0) (U16LE 0xFFFF) (U16LE 0xFFFF) ([byte[]]@(0x80, 0, 0, 0, 0xFF, 0xFF, 0xFF, 2, 2, 0x44, 1, 0, 0x3B))) }
$cases['bigtiff-65535-squared.tif'] = @{ Kind = 'budget'; Bytes = (New-Tiff -Big (Gray1x1Entries $bigIfdEnd 65535 65535 1 1) -Tail ([byte[]]@(0x80))) }

# ------------------------------------------------------------------------- runs ----

$results = @()
foreach ($name in $cases.Keys) {
    $case = $cases[$name]
    $path = Join-Path $WorkDir $name
    [IO.File]::WriteAllBytes($path, $case.Bytes)

    $info = [Diagnostics.ProcessStartInfo]::new($pwshExe)
    foreach ($a in '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'MagickMalformedProbeChild.ps1'), '-Assembly', $assembly, '-File', $path) { $info.ArgumentList.Add($a) }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $child = [Diagnostics.Process]::Start($info)
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    $finished = $child.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) { $child.Kill($true); $child.WaitForExit() }
    $elapsed = $watch.ElapsedMilliseconds
    $lines = @($stdout.Result -split "`r?`n" | Where-Object { $_ })
    $entries = [ordered]@{}
    foreach ($l in $lines) { if ($l -match '^RESULT (\S+) (\S+) (\d+)$') { $entries[$Matches[1]] = $Matches[2] } }
    $peakLine = $lines | Where-Object { $_ -match '^PEAK \d+$' } | Select-Object -Last 1
    $peakMb = if ($peakLine) { [math]::Round([double]($peakLine -replace 'PEAK ', '') / 1MB) } else { -1 }

    $problem = ''
    if (-not $finished) { $problem = "HANG (killed after $TimeoutSeconds s)" }
    elseif ($entries.Count -lt 5 -or -not $peakLine) { $problem = "CRASH (exit $($child.ExitCode); " + (($stderr.Result -split "`r?`n" | Where-Object { $_ } | Select-Object -First 1) -as [string]) + ')' }
    elseif ($peakMb -gt $PeakLimitMB) { $problem = "MEMORY (peak $peakMb MB)" }
    elseif ($case.Kind -eq 'budget' -and (@($entries['payload'], $entries['image']) -match '^decoded')) { $problem = 'BUDGET (an over-budget header was decoded)' }
    elseif ($case.Kind -eq 'control' -and -not ($entries['payload'] -match '^decoded')) { $problem = 'CONTROL (the well-formed control did not decode: ' + $entries['payload'] + ')' }

    $summary = ($entries.Keys | ForEach-Object { "$_=$($entries[$_])" }) -join ' '
    Write-Host ("{0,-40} {1,6} ms  peak {2,5} MB  {3}{4}" -f $name, $elapsed, $peakMb, $summary, $(if ($problem) { "  <-- $problem" } else { '' }))
    $results += [pscustomobject]@{ Name = $name; Kind = $case.Kind; Problem = $problem; Sha256 = (Get-FileHash $path -Algorithm SHA256).Hash }
}

$failed = @($results | Where-Object { $_.Problem -and $_.Kind -ne 'control' })
$controls = @($results | Where-Object { $_.Problem -and $_.Kind -eq 'control' })
$detail = "$($results.Count) files, child limit $TimeoutSeconds s / $PeakLimitMB MB"
if ($failed.Count) { Exit-Verdict $check 1 ("$($failed.Count) failed: " + (($failed | ForEach-Object { "$($_.Name) $($_.Problem)" }) -join '; ')) }
if ($controls.Count) { Exit-Verdict $check 2 ("control did not decode: " + (($controls | ForEach-Object { $_.Name }) -join ', ')) }
Remove-Item -LiteralPath $WorkDir -Recurse -Force
Exit-Verdict $check 0 $detail
