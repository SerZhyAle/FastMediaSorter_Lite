<#
.SYNOPSIS
    Builds libwebp's decoder library for the x86 fallback viewer (specification 270 R9) from the
    pinned upstream tag, proves what it imports, and lays it into payload\libwebp.

.DESCRIPTION
    The net48 x86 viewer reads a still WEBP that WIC cannot (Windows 7/8.1 have no WebP codec)
    through libwebpdecoder.dll, loaded by full path from x86\ beside the exe (x64\ for the net48
    test leg). This script is the only way that file is made:

      1. downloads the source tarball of the pinned tag and refuses it unless its SHA-256 matches;
      2. builds the decoder-only DLL with the Visual Studio C++ tools (nmake, Makefile.vc,
         CFG=release-dynamic, RTLIBCFG=static - the C runtime is linked in, so the DLL needs no
         VC++ redistributable on the old machine) for x86 and x64;
      3. refuses a DLL that imports anything but KERNEL32.dll, or any KERNEL32 function outside
         the allow-list below (every one present on Windows 7 SP1);
      4. copies both DLLs, COPYING and PATENTS into payload\libwebp and writes PROVENANCE.txt
         with the source and output hashes.

    -Check builds nothing: it re-hashes the committed files against PROVENANCE.txt and re-runs
    the import check. Exit 0 pass, 1 a check failed, 2 could not verify (no dumpbin, no files).

    A rebuild is not bit-identical (the linker stamps a time), so a rebuild is a new
    PROVENANCE.txt to commit, never a silent replacement.

.EXAMPLE
    pwsh -NoProfile -File .\tools\Build-LibWebpDecoder.ps1
    pwsh -NoProfile -File .\tools\Build-LibWebpDecoder.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-libwebp-build')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Version = '1.6.0'
$TagCommit = '4fa21912338357f89e4fd51cf2368325b59e9bd9'
$SourceUrl = "https://github.com/webmproject/libwebp/archive/refs/tags/v$Version.tar.gz"
$SourceSha256 = '93A852C2B3EFAFEE3723EFD4636DE855B46F9FE1EFDDD607E1F42F60FC8F2136'

# Every KERNEL32 import of the 1.6.0 build with the static CRT; all exist on Windows 7 SP1
# (the newest are the Vista-era condition variables, FLS and InitializeCriticalSectionEx).
$AllowedKernel32 = @(
    'CloseHandle', 'CreateFileW', 'CreateThread', 'DecodePointer', 'DeleteCriticalSection',
    'EncodePointer', 'EnterCriticalSection', 'ExitProcess', 'ExitThread', 'FindClose',
    'FindFirstFileExW', 'FindNextFileW', 'FlsAlloc', 'FlsFree', 'FlsGetValue', 'FlsSetValue',
    'FlushFileBuffers', 'FreeEnvironmentStringsW', 'FreeLibrary', 'FreeLibraryAndExitThread',
    'GetACP', 'GetCPInfo', 'GetCommandLineA', 'GetCommandLineW', 'GetConsoleMode',
    'GetConsoleOutputCP', 'GetCurrentProcess', 'GetCurrentProcessId', 'GetCurrentThreadId',
    'GetEnvironmentStringsW', 'GetFileType', 'GetLastError', 'GetModuleFileNameW',
    'GetModuleHandleExW', 'GetModuleHandleW', 'GetOEMCP', 'GetProcAddress', 'GetProcessHeap',
    'GetStartupInfoW', 'GetStdHandle', 'GetStringTypeW', 'GetSystemTimeAsFileTime', 'HeapAlloc',
    'HeapFree', 'HeapReAlloc', 'HeapSize', 'InitializeConditionVariable',
    'InitializeCriticalSectionAndSpinCount', 'InitializeCriticalSectionEx', 'InitializeSListHead',
    'InterlockedFlushSList', 'IsDebuggerPresent', 'IsProcessorFeaturePresent', 'IsValidCodePage',
    'LCMapStringW', 'LeaveCriticalSection', 'LoadLibraryExW', 'MultiByteToWideChar',
    'QueryPerformanceCounter', 'RaiseException', 'RtlCaptureContext', 'RtlLookupFunctionEntry',
    'RtlPcToFileHeader', 'RtlUnwind', 'RtlUnwindEx', 'RtlVirtualUnwind', 'SetFilePointerEx',
    'SetLastError', 'SetStdHandle', 'SetThreadPriority', 'SetUnhandledExceptionFilter',
    'SleepConditionVariableCS', 'TerminateProcess', 'TlsAlloc', 'TlsFree', 'TlsGetValue',
    'TlsSetValue', 'UnhandledExceptionFilter', 'VirtualProtect', 'WaitForSingleObjectEx',
    'WakeConditionVariable', 'WideCharToMultiByte', 'WriteConsoleW', 'WriteFile'
)

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PayloadRoot = Join-Path $RepoRoot 'payload\libwebp'
$Arches = @('x86', 'x64')

function Find-VsInstall {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    return $path.Trim()
}

function Find-Dumpbin([string]$vsRoot) {
    if (-not $vsRoot) { return $null }
    $hits = Get-ChildItem -Path (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin\Hostx64\x64\dumpbin.exe' } |
        Where-Object { Test-Path $_ }
    return ($hits | Select-Object -First 1)
}

# The DLLs a file imports and the functions taken from KERNEL32, from dumpbin /imports.
function Get-Imports([string]$dumpbin, [string]$dll) {
    $lines = & $dumpbin /nologo /imports $dll
    if ($LASTEXITCODE -ne 0) { throw "dumpbin failed on $dll" }
    $modules = @()
    $functions = @()
    $current = $null
    foreach ($line in $lines) {
        if ($line -match '^\s{4}(\S+\.dll)\s*$') { $current = $Matches[1]; $modules += $current; continue }
        if ($current -and $line -match '^\s+[0-9A-F]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*$') {
            if ($current -ieq 'KERNEL32.dll') { $functions += $Matches[1] }
        }
    }
    return [pscustomobject]@{ Modules = @($modules); Kernel32 = @($functions) }
}

function Test-Imports([string]$dumpbin, [string]$dll) {
    $imports = Get-Imports $dumpbin $dll
    $problems = @()
    foreach ($m in $imports.Modules) {
        if ($m -ine 'KERNEL32.dll') { $problems += "imports $m" }
    }
    foreach ($f in $imports.Kernel32) {
        if ($AllowedKernel32 -notcontains $f) { $problems += "imports KERNEL32!$f (not on the Windows 7 allow-list)" }
    }
    if ($imports.Kernel32.Count -eq 0) { $problems += 'no KERNEL32 imports read (dumpbin output not understood)' }
    return $problems
}

function Read-Provenance {
    $file = Join-Path $PayloadRoot 'PROVENANCE.txt'
    if (-not (Test-Path $file)) { return $null }
    $map = @{}
    foreach ($line in Get-Content $file) {
        if ($line -match '^([0-9A-F]{64})\s+(\S+)$') { $map[$Matches[2]] = $Matches[1] }
    }
    return $map
}

$vs = Find-VsInstall
$dumpbin = Find-Dumpbin $vs

if ($Check) {
    if (-not $dumpbin) { Write-Output 'Build-LibWebpDecoder -Check: COULD NOT VERIFY (no dumpbin; install the VS C++ tools)'; exit 2 }
    $recorded = Read-Provenance
    if (-not $recorded) { Write-Output "Build-LibWebpDecoder -Check: COULD NOT VERIFY (no $PayloadRoot\PROVENANCE.txt)"; exit 2 }
    $failures = @()
    foreach ($arch in $Arches) {
        $rel = "$arch/libwebpdecoder.dll"
        $dll = Join-Path $PayloadRoot "$arch\libwebpdecoder.dll"
        if (-not (Test-Path $dll)) { $failures += "$rel missing"; continue }
        $hash = (Get-FileHash $dll -Algorithm SHA256).Hash
        if ($recorded[$rel] -ne $hash) { $failures += "$rel hash $hash differs from PROVENANCE.txt" }
        $failures += @(Test-Imports $dumpbin $dll | ForEach-Object { "${rel}: $_" })
    }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Output "  $_" }
        Write-Output "Build-LibWebpDecoder -Check: FAIL ($($failures.Count) problems)"
        exit 1
    }
    Write-Output 'Build-LibWebpDecoder -Check: PASS'
    exit 0
}

if (-not $vs) { throw 'Visual Studio with the C++ x86/x64 tools was not found (vswhere).' }
if (-not $dumpbin) { throw 'dumpbin.exe was not found under the Visual Studio install.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$tarball = Join-Path $WorkDir "libwebp-$Version.tar.gz"
if (-not (Test-Path $tarball)) {
    Write-Output "Downloading $SourceUrl"
    Invoke-WebRequest -Uri $SourceUrl -OutFile $tarball -UseBasicParsing
}
$actual = (Get-FileHash $tarball -Algorithm SHA256).Hash
if ($actual -ne $SourceSha256) { throw "Source tarball SHA-256 $actual is not the pinned $SourceSha256 - refusing to build." }

$sourceDir = Join-Path $WorkDir "libwebp-$Version"
if (-not (Test-Path (Join-Path $sourceDir 'Makefile.vc'))) {
    & tar.exe -xzf $tarball -C $WorkDir
    if ($LASTEXITCODE -ne 0) { throw 'tar could not extract the source.' }
}

$objDir = Join-Path $WorkDir 'out'
$vcvarsArch = @{ x86 = 'x64_x86'; x64 = 'x64' }
foreach ($arch in $Arches) {
    $log = Join-Path $WorkDir "build-$arch.log"
    $target = "$objDir\release-dynamic\$arch\lib\libwebpdecoder_dll.lib"
    $cmd = "call `"$vcvars`" $($vcvarsArch[$arch]) >nul && cd /d `"$sourceDir`" && nmake /nologo /f Makefile.vc CFG=release-dynamic RTLIBCFG=static ARCH=$arch OBJDIR=`"$objDir`" `"$target`""
    Write-Output "Building $arch (log: $log)"
    & cmd.exe /d /c $cmd *> $log
    if ($LASTEXITCODE -ne 0) { throw "nmake failed for $arch - see $log" }
    $dll = "$objDir\release-dynamic\$arch\bin\libwebpdecoder.dll"
    if (-not (Test-Path $dll)) { throw "The $arch build produced no $dll" }
    $problems = @(Test-Imports $dumpbin $dll)
    if ($problems.Count -gt 0) { throw "The $arch DLL fails the import check: $($problems -join '; ')" }
    $dest = Join-Path $PayloadRoot $arch
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll (Join-Path $dest 'libwebpdecoder.dll') -Force
}
Copy-Item (Join-Path $sourceDir 'COPYING') (Join-Path $PayloadRoot 'COPYING') -Force
Copy-Item (Join-Path $sourceDir 'PATENTS') (Join-Path $PayloadRoot 'PATENTS') -Force

$lines = @(
    "libwebp $Version decoder library (libwebpdecoder.dll) for the x86 fallback viewer - specification 270 R9.",
    "Built by tools/Build-LibWebpDecoder.ps1; do not replace these files by hand.",
    "",
    "Source:     $SourceUrl",
    "Tag commit: $TagCommit",
    "Build:      nmake /f Makefile.vc CFG=release-dynamic RTLIBCFG=static ARCH=<arch> (static C runtime)",
    "Imports:    KERNEL32.dll only, every function on the Windows 7 SP1 allow-list in the script",
    "License:    BSD-3-Clause (COPYING) with the WebM additional IP rights grant (PATENTS)",
    "",
    "SHA-256:",
    "$SourceSha256  source/libwebp-$Version.tar.gz"
)
foreach ($arch in $Arches) {
    $hash = (Get-FileHash (Join-Path $PayloadRoot "$arch\libwebpdecoder.dll") -Algorithm SHA256).Hash
    $lines += "$hash  $arch/libwebpdecoder.dll"
}
foreach ($name in @('COPYING', 'PATENTS')) {
    $lines += "$((Get-FileHash (Join-Path $PayloadRoot $name) -Algorithm SHA256).Hash)  $name"
}
Set-Content -Path (Join-Path $PayloadRoot 'PROVENANCE.txt') -Value $lines -Encoding utf8
Write-Output "libwebp $Version laid into $PayloadRoot"
