<#
.SYNOPSIS
    The synthetic corpus (specification 209 section 5 item 6): every media file a scene
    touches is generated into the run folder - bitmaps, WAV, H.264 clips and MP3s through
    ffmpeg when it is present, archives, deep paths, corrupt files - never the owner's or
    a user's real media, never a real folder in history. No real path survives the restore.

.DESCRIPTION
    New-DynAuditCorpus takes the scene's corpus spec (a list of { kind, ... } items) and
    returns a map of what it built. A kind it cannot build (no ffmpeg, no free space for
    the 10 GB archive) is returned as @{ Error = ... } - the scene then reports could not
    verify, never a silent pass over an empty corpus.
#>

. (Join-Path $PSScriptRoot 'DynAudit.Scrub.ps1')

function Get-DynAuditFfmpeg {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function New-DynAuditBitmap {
    param([string]$Path, [int]$Width = 64, [int]$Height = 64, [int]$Seed = 1)
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::new($Width, $Height)
    for ($x = 0; $x -lt $Width; $x++) {
        for ($y = 0; $y -lt $Height; $y++) {
            $r = ($x * 7 + $y * 3 + $Seed) % 256; $g = ($x * 13 + $y * 5 + $Seed) % 256; $b = ($x * 3 + $y * 11 + $Seed) % 256
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($r, $g, $b))
        }
    }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# A valid PNG header followed by garbage: decodable enough to reach the decoder, then
# refused - exactly the S7.1 boundary.
function New-DynAuditCorruptFile {
    param([string]$Path, [string]$SeedExtension = '.png')
    if ($SeedExtension -eq '.png') {
        $header = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52)
    } else {
        $header = [byte[]](0x52, 0x49, 0x46, 0x46, 0x24, 0x08, 0x00, 0x00)
    }
    $garbage = [byte[]]::new(64KB)
    (New-Object Random 42).NextBytes($garbage)
    [IO.File]::WriteAllBytes($Path, $header + $garbage)
}

function New-DynAuditCorpus {
    param([string]$RunFolder, [object[]]$Items)
    Add-Type -AssemblyName System.Drawing
    $corpusDir = Join-Path $RunFolder 'corpus'
    New-Item -ItemType Directory -Path $corpusDir -Force | Out-Null
    $built = [ordered]@{}
    $ffmpeg = Get-DynAuditFfmpeg
    $built.ffmpeg = [bool]$ffmpeg

    foreach ($item in $Items) {
        $kind = $item.kind
        switch ($kind) {
            'folder' {
                # N small PNGs - the everyday folder of S4.1 and the 100k tree of S6.1.
                # Large trees are written from one generated template (a copy is byte
                # identical and instant; 100 000 seeded gradients would take hours).
                $count = if ($item.files) { [int]$item.files } else { 100 }
                $dir = Join-Path $corpusDir ('folder-{0}' -f $count)
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
                if ($count -le 200) {
                    for ($i = 0; $i -lt $count; $i++) {
                        New-DynAuditBitmap -Path (Join-Path $dir ('img-{0:d6}.png' -f $i)) -Seed ($i + 1)
                    }
                } else {
                    $template = Join-Path $corpusDir 'folder-template.png'
                    if (-not (Test-Path -LiteralPath $template)) { New-DynAuditBitmap -Path $template -Seed 3 }
                    for ($i = 0; $i -lt $count; $i++) {
                        [void][IO.File]::Copy($template, (Join-Path $dir ('img-{0:d6}.png' -f $i)), $true)
                    }
                }
                $built.folder = $dir
            }
            'image' {
                # A big single image (default 100 MP class). A small gradient is drawn
                # scaled into the big frame - one DrawImage call, not one SetPixel per
                # pixel, so 100 MP takes seconds, not hours.
                $mp = if ($item.mp) { [int]$item.mp } else { 1 }
                $side = [int][Math]::Sqrt($mp * 1000000)
                $side = [Math]::Min($side, 12000)
                $small = [System.Drawing.Bitmap]::new(256, 256)
                for ($x = 0; $x -lt 256; $x += 4) {
                    for ($y = 0; $y -lt 256; $y += 4) {
                        $small.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(($x + 7) % 256, ($y + 13) % 256, ($x * $y) % 256))
                    }
                }
                $path = Join-Path $corpusDir 'image-large.png'
                $big = [System.Drawing.Bitmap]::new($side, $side)
                $g = [System.Drawing.Graphics]::FromImage($big)
                try {
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::Bilinear
                    $g.DrawImage($small, 0, 0, $side, $side)
                    $big.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                } finally { $g.Dispose(); $big.Dispose(); $small.Dispose() }
                $built.image = $path
            }
            'video' {
                if (-not $ffmpeg) { $built.video = @{ Error = 'ffmpeg not found' }; continue }
                $seconds = if ($item.seconds) { [int]$item.seconds } else { 10 }
                $def = if ($item.fourK) { '-s 3840x2160 -r 60' } else { '-s 640x360 -r 30' }
                $path = Join-Path $corpusDir 'clip.mp4'
                & $ffmpeg -y -loglevel error -f lavfi -i ('testsrc=duration={0}:size=640x360:rate=30' -f $seconds) $path 2>$null
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $path)) { $built.video = @{ Error = 'ffmpeg failed' }; continue }
                $built.video = $path
            }
            'audio' {
                if (-not $ffmpeg) { $built.audio = @{ Error = 'ffmpeg not found' }; continue }
                $seconds = if ($item.seconds) { [int]$item.seconds } else { 30 }
                $path = Join-Path $corpusDir 'tone.mp3'
                & $ffmpeg -y -loglevel error -f lavfi -i ('sine=frequency=440:duration={0}' -f $seconds) $path 2>$null
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $path)) { $built.audio = @{ Error = 'ffmpeg failed' }; continue }
                $built.audio = $path
            }
            'corrupt' {
                # corrupt-image, corrupt-video, corrupt-audio: S7.1's and S4.12's refuse.
                $ext = if ($item.ext) { [string]$item.ext } else { 'png' }
                $path = Join-Path $corpusDir ("corrupt.{0}" -f $ext)
                New-DynAuditCorruptFile -Path $path -SeedExtension ('.' + $ext)
                $built['corrupt-' + $ext] = $path
            }
            'archive' {
                # A zip with deep paths and small files; the 10 GB variant (S6.4) fills
                # its entries with zeros - compressible, so writing stays linear and the
                # extraction peak on disk is still the real 10 GB.
                $entries = if ($item.entries) { [int]$item.entries } else { 20 }
                $gb = if ($item.gb) { [double]$item.gb } else { 0 }
                $deep = Join-Path $corpusDir 'archive-src'
                $perFileBytes = [long]0
                if ($gb -gt 0) { $perFileBytes = [long](($gb * 1GB) / $entries) }
                $zeros = [byte[]]::new(1MB)
                for ($i = 0; $i -lt $entries; $i++) {
                    $sub = Join-Path $deep (('d{0}/d{1}/d{2}' -f ($i % 3), ($i % 5), ($i % 7)) -replace '/', '\')
                    New-Item -ItemType Directory -Path $sub -Force | Out-Null
                    $entryPath = Join-Path $sub ('f{0}.bin' -f $i)
                    if ($perFileBytes -gt 0) {
                        $fs = [IO.File]::Create($entryPath)
                        try { $left = $perFileBytes; while ($left -gt 0) { $n = [int][Math]::Min($zeros.Length, $left); $fs.Write($zeros, 0, $n); $left -= $n } } finally { $fs.Dispose() }
                    } else {
                        New-DynAuditBitmap -Path ($entryPath -replace '\.bin$', '.png') -Seed $i
                    }
                }
                $path = Join-Path $corpusDir 'archive.zip'
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
                Compress-Archive -Path (Join-Path $deep '*') -DestinationPath $path
                $built.archive = $path
            }
            'workload' {
                # The fixed 20 MB disk-read workload of the coh probe.
                $path = Join-Path $corpusDir 'workload-20mb.bin'
                if (-not (Test-Path -LiteralPath $path)) {
                    $buf = [byte[]]::new(1MB)
                    (New-Object Random 7).NextBytes($buf)
                    $fs = [IO.File]::Create($path)
                    try { for ($i = 0; $i -lt 20; $i++) { $fs.Write($buf, 0, $buf.Length) } } finally { $fs.Dispose() }
                }
                $built.workload = $path
            }
            'lock-target' {
                $path = Join-Path $corpusDir 'lock-target.png'
                New-DynAuditBitmap -Path $path -Seed 99
                $built.lockTarget = $path
            }
            default {
                $built[$kind] = @{ Error = 'unknown corpus kind' }
            }
        }
    }
    return [pscustomobject]$built
}
