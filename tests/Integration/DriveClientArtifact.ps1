<#
.SYNOPSIS
    Fixture self-test of tools/Test-DriveClientArtifact.ps1 (specification L0306 section 4.4): the check finds a
    real AssemblyMetadata client pair, passes the expected state, fails each broken delivery for its own reason,
    and never prints a value.

.DESCRIPTION
    Compiles throw-away executables under %TEMP% with the .NET Framework C# compiler, each holding a class named
    ExchangeDriveClientConfig (the check's "managed code is readable" marker) and zero, one or two
    [assembly: AssemblyMetadata] attributes - the same serialization the Share Manager build emits, so the blob
    parser is judged against a real compiler, not a hand-made byte string. Packs one into a ZIP and one into an
    MSIX-shaped archive. Runs the check against them as a child process and expects an exit code and a message
    per case; every child's output must hold neither fake value. The fake values are generated at run time.
    Nothing in the repository is read or written. About fifteen child processes: run it after a change to the
    check, not at every closure (tools\check-placement.jsonl).

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
$name = 'DriveClientArtifact'
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict $name 2 'the self-test could not run' }
Write-Subject $name 'tools/Test-DriveClientArtifact.ps1 against throw-away executables and archives'

$Check = Join-Path $RepoRoot 'tools\Test-DriveClientArtifact.ps1'
$pwsh = (Get-Process -Id $PID).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { Exit-Verdict $name 2 'the .NET Framework C# compiler (csc.exe) was not found' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('fms-drive-client-artifact-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Fake values with the real shapes, made here so no file holds one.
$rand = { param($n) -join (1..$n | ForEach-Object { 'abcdefghijklmnopqrstuvwxyz0123456789'[(Get-Random -Maximum 36)] }) }
$idA = "$(Get-Random -Minimum 100000000 -Maximum 999999999)-$(& $rand 32).apps.googleusercontent.com"
$secretA = 'GOCSPX' + '-' + (& $rand 28)
$idB = "$(Get-Random -Minimum 100000000 -Maximum 999999999)-$(& $rand 32).apps.googleusercontent.com"
$secretB = 'GOCSPX' + '-' + (& $rand 28)
$secretValues = @($idA, $secretA, $idB, $secretB)

# A Share-Manager-shaped exe in its own folder, named FastMediaSorterCompanion.exe.
# $pairs: ordered list of key/value; $marker = $false leaves out the ExchangeDriveClientConfig type.
function New-Exe([string]$folder, [object[]]$pairs, [bool]$marker = $true) {
    $dir = Join-Path $work $folder
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $src = Join-Path $dir 'p.cs'
    $sb = [Text.StringBuilder]::new()
    for ($i = 0; $i -lt $pairs.Count; $i += 2) {
        [void]$sb.AppendLine("[assembly: System.Reflection.AssemblyMetadata(`"$($pairs[$i])`", `"$($pairs[$i + 1])`")]")
    }
    [void]$sb.AppendLine('[assembly: System.Reflection.AssemblyMetadata("Unrelated", "value")]')
    $type = if ($marker) { 'ExchangeDriveClientConfig' } else { 'SomethingElse' }
    [void]$sb.AppendLine("class $type { static void Main() { } }")
    [IO.File]::WriteAllText($src, $sb.ToString())
    $exe = Join-Path $dir 'FastMediaSorterCompanion.exe'
    $out = & $csc /nologo /target:exe "/out:$exe" $src 2>&1
    if ($LASTEXITCODE -ne 0) { throw "csc failed for ${folder}: $out" }
    Remove-Item -LiteralPath $src
    $exe
}

function New-Archive([string]$fileName, [string]$exe, [string]$entryName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $path = Join-Path $work $fileName
    $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
    try { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $exe, $entryName) } finally { $zip.Dispose() }
    $path
}

$failures = [Collections.Generic.List[string]]::new()
function Expect([string]$case, [int]$code, [string]$message, [string[]]$arguments, [hashtable]$envPair = $null) {
    $saved = @{ id = $env:FMS_DRIVE_CLIENT_ID; secret = $env:FMS_DRIVE_CLIENT_SECRET }
    $env:FMS_DRIVE_CLIENT_ID = if ($envPair) { $envPair.id } else { $null }
    $env:FMS_DRIVE_CLIENT_SECRET = if ($envPair) { $envPair.secret } else { $null }
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $text = (& $pwsh -NoProfile -File $Check @arguments 2>&1 | Out-String)
        $got = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
        $env:FMS_DRIVE_CLIENT_ID = $saved.id
        $env:FMS_DRIVE_CLIENT_SECRET = $saved.secret
    }
    $ok = ($got -eq $code) -and ($text -like "*$message*")
    foreach ($v in $secretValues) { if ($text.Contains($v)) { $ok = $false; $failures.Add("${case}: output holds a client value") } }
    if ($text -notmatch '(?m)^Test-DriveClientArtifact: (PASS|FAIL|COULD NOT VERIFY)') { $ok = $false; $failures.Add("${case}: no verdict line") }
    $mark = if ($ok) { 'ok  ' } else { 'FAIL' }
    Write-Host "  $mark $case (exit $got, want $code)" -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { $failures.Add("$case (exit $got, want $code; '$message' $(if ($text -like "*$message*") { 'seen' } else { 'not seen' }))"); Write-Host $text }
}

try {
    $configured = New-Exe 'configured' @('FmsDriveClientId', $idA, 'FmsDriveClientSecret', $secretA)
    $configuredB = New-Exe 'configuredB' @('FmsDriveClientId', $idB, 'FmsDriveClientSecret', $secretB)
    $unconfigured = New-Exe 'unconfigured' @()
    $idOnly = New-Exe 'idonly' @('FmsDriveClientId', $idA)
    $swapped = New-Exe 'swapped' @('FmsDriveClientId', $secretA, 'FmsDriveClientSecret', $idA)
    $noMarker = New-Exe 'nomarker' @('FmsDriveClientId', $idA, 'FmsDriveClientSecret', $secretA) $false
    $zipPath = New-Archive 'portable.zip' $configured 'FastMediaSorter/FastMediaSorterCompanion.exe'
    $msixPath = New-Archive 'package.msix' $configured 'FastMediaSorterCompanion.exe'
    $setup = Join-Path $work 'FastMediaSorter-1.2.3-windows-x64-setup.exe'
    [IO.File]::WriteAllBytes($setup, [byte[]](1..64))
    $emptyDir = Join-Path $work 'empty'; New-Item -ItemType Directory -Force -Path $emptyDir | Out-Null

    Expect 'configured exe passes Configured'         0 'client pair present'            @('-Path', $configured)
    Expect 'configured exe fails Unconfigured'        1 'client pair present'            @('-Path', $configured, '-Expect', 'Unconfigured')
    Expect 'unconfigured exe passes Unconfigured'     0 'client pair absent'             @('-Path', $unconfigured, '-Expect', 'Unconfigured')
    Expect 'unconfigured exe fails Configured'        1 'client pair absent'             @('-Path', $unconfigured)
    Expect 'id alone is a broken pair'                1 'id present, secret absent'      @('-Path', $idOnly)
    Expect 'swapped id and secret'                    1 'look swapped'                   @('-Path', $swapped)
    Expect 'unreadable code is not a pass'            2 'not readable'                   @('-Path', $noMarker)
    Expect 'folder input'                             0 'client pair present'            @('-Path', (Split-Path $configured -Parent))
    Expect 'zip input'                                0 'portable.zip!FastMediaSorter/'  @('-Path', $zipPath)
    Expect 'msix input'                               0 'package.msix!'                  @('-Path', $msixPath)
    Expect 'same pair across three channels'          0 '3 artifact(s)'                  @('-Path', "$configured,$zipPath,$msixPath")
    Expect 'mixed pairs across channels'              1 'different client pairs'         @('-Path', "$configured,$configuredB")
    Expect 'Inno setup is not readable'               2 'install it'                     @('-Path', $setup)
    Expect 'folder without a Share Manager'           2 'no FastMediaSorterCompanion'    @('-Path', $emptyDir)
    Expect 'missing path'                             2 'path not found'                 @('-Path', (Join-Path $work 'nope.exe'))
    Expect 'nothing named'                            2 'no path named'                  @()
    Expect 'matches the shell pair'                   0 'matches this shell'             @('-Path', $configured, '-CompareEnvironment') @{ id = $idA; secret = $secretA }
    Expect 'differs from the shell pair'              1 'differs from this shell'        @('-Path', $configured, '-CompareEnvironment') @{ id = $idB; secret = $secretB }
    Expect 'no shell pair to compare with'            2 'not both set'                   @('-Path', $configured, '-CompareEnvironment')
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count) { Exit-Verdict $name 1 "$($failures.Count) case(s) wrong: $($failures -join '; ')" }
Exit-Verdict $name 0 'the check finds a real metadata pair, judges each delivery state and prints no value'
