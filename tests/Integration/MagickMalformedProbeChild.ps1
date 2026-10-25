<#
.SYNOPSIS
    One disposable child of MagickMalformedProbes.ps1: opens ONE crafted file through the
    mainline's real Magick.NET entry points and reports what each did. Not run by hand.

.DESCRIPTION
    Loads the linked viewer sources from the built Lite.Tests.dll (net10, the same code the
    mainline exe compiles) and drives, in turn: the decoder's payload and image paths, its
    header-only size probe, the editor's donor-metadata read and the QR intake. Every entry
    point is wrapped: a managed exception is an ordinary outcome ("threw:<Type>"), a native
    fault or a hang takes this process down, which is exactly what the parent watches for.
    Output, one line each: "RESULT <entry> <outcome> <ms>", then "PEAK <bytes>".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Assembly,
    [Parameter(Mandatory)][string]$File
)
$ErrorActionPreference = 'Stop'

$dir = Split-Path $Assembly -Parent
$magick = [Reflection.Assembly]::LoadFrom((Join-Path $dir 'Magick.NET-Q8-x64.dll'))
[Reflection.Assembly]::LoadFrom((Join-Path $dir 'Magick.NET.Core.dll')) | Out-Null
$magick.GetType('ImageMagick.MagickNET', $true).GetMethod('SetNativeLibraryDirectory', [type[]]@([string])).Invoke(
    $null, [object[]]@([string](Join-Path $dir 'runtimes\win-x64\native')))
$asm = [Reflection.Assembly]::LoadFrom($Assembly)

$any = [Reflection.BindingFlags]'Public, NonPublic, Instance, Static'
$decoderType = $asm.GetType('fmsl.ModernMagickDecoder', $true)
$encoderType = $asm.GetType('fmsl.ModernMagickEncoder', $true)
$qrType = $asm.GetType('fmsl.QrCodeDecoder', $true)
$decoder = [Activator]::CreateInstance($decoderType, $true)
$bytes = [IO.File]::ReadAllBytes($File)

function Invoke-Entry([string]$Name, [scriptblock]$Body) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $outcome = try {
        $result = & $Body
        if ($null -eq $result) { 'refused' } else { $result }
    } catch {
        $e = $_.Exception
        while ($e -is [Reflection.TargetInvocationException] -or $e -is [Management.Automation.MethodInvocationException]) {
            if ($null -eq $e.InnerException) { break }
            $e = $e.InnerException
        }
        'threw:' + $e.GetType().Name
    }
    Write-Output ("RESULT {0} {1} {2}" -f $Name, $outcome, $watch.ElapsedMilliseconds)
}

Invoke-Entry 'payload' {
    $p = $decoderType.GetMethod('DecodeToPayload', $any).Invoke($decoder, @([IO.MemoryStream]::new($bytes), ''))
    if ($null -eq $p) { $null } else { 'decoded:{0}B' -f $p.Bytes.Length }
}
Invoke-Entry 'image' {
    $img = $decoderType.GetMethod('DecodeToImage', $any).Invoke($decoder, @([IO.MemoryStream]::new($bytes)))
    if ($null -eq $img) { $null } else { try { 'decoded:{0}x{1}' -f $img.Width, $img.Height } finally { $img.Dispose() } }
}
Invoke-Entry 'size' {
    $s = $decoderType.GetMethod('TryGetPixelSize', $any).Invoke($decoder, @([IO.MemoryStream]::new($bytes)))
    if ($s.IsEmpty) { $null } else { 'size:{0}x{1}' -f $s.Width, $s.Height }
}
Invoke-Entry 'donor' {
    $m = $encoderType.GetMethod('ReadDonorMetadata', $any).Invoke($null, @(, $bytes))
    if ($null -eq $m) { $null } else { 'read' }
}
Invoke-Entry 'qr' {
    $r = $qrType.GetMethod('DecodeBytes', $any).Invoke($null, @(, $bytes))
    'qr:' + $r.Outcome
}
Write-Output ("PEAK {0}" -f [Diagnostics.Process]::GetCurrentProcess().PeakWorkingSet64)
