<#
.SYNOPSIS
    Fixture self-test of the version-carrier guard in tools/Sync-IconGlyphs.ps1 (specification 183, icons):
    a catalog at a higher MAJOR is refused, a higher MINOR is accepted.

.DESCRIPTION
    ICON-SET section 5: the catalog's vocabulary.version carries "ICON-SET <MAJOR>.<MINOR>"; a build-time
    reader refuses a MAJOR higher than the one it was built against (a MAJOR re-learns meanings) with one
    message naming the contract, and accepts a higher MINOR. This builds throw-away catalog folders under
    %TEMP% - each with only what the tool reads before the glyphs - and runs the tool against them with
    -CatalogRoot, expecting an exit code and a message per case. Nothing in the repository is read or
    written. A handful of child processes; run it after a change to the guard.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'IconCatalogCarrier' 2 'the self-test could not run' }
Write-Subject 'IconCatalogCarrier' 'tools/Sync-IconGlyphs.ps1 -Check against throw-away catalogs that differ only in vocabulary.version'

$Tool = Join-Path $RepoRoot 'tools\Sync-IconGlyphs.ps1'
$pwsh = (Get-Process -Id $PID).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("fms-icon-carrier-" + [guid]::NewGuid().ToString('N'))

# A catalog with the files the tool checks for before it looks at a glyph. $carrier = $null leaves the carrier out.
function New-Catalog([string]$name, $carrier) {
    $root = Join-Path $work $name
    $dir = Join-Path $root 'iconography'
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'glyphs') | Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'vocabulary.jsonl'), "{}`n")
    [IO.File]::WriteAllText((Join-Path $dir 'README.md'), "x`n")
    if ($null -ne $carrier) { [IO.File]::WriteAllText((Join-Path $dir 'vocabulary.version'), "$carrier`n") }
    $root
}

$failures = [Collections.Generic.List[string]]::new()
function Expect([string]$name, [int]$code, [string]$message, [string]$notMessage, [string[]]$arguments) {
    $out = @(& $pwsh -NoProfile -File $Tool @arguments 2>&1 | ForEach-Object { "$_" })
    $got = $LASTEXITCODE
    $text = $out -join "`n"
    $ok = ($got -eq $code) -and (-not $message -or $text.Contains($message)) -and (-not $notMessage -or -not $text.Contains($notMessage))
    if ($ok) { Write-Host "  ok    $name" -ForegroundColor Green }
    else { Write-Host "  WRONG $name (wanted exit $code '$message' without '$notMessage', got exit $got)" -ForegroundColor Red; ($text -split "`n" | Select-Object -First 6) | ForEach-Object { Write-Host "        $_" }; $failures.Add($name) }
}

try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $major = New-Catalog 'major' 'ICON-SET 1.0'
    $minor = New-Catalog 'minor' 'ICON-SET 0.99'
    $same  = New-Catalog 'same'  'ICON-SET 0.28'
    $junk  = New-Catalog 'junk'  'version 3'
    $none  = New-Catalog 'none'  $null

    Expect 'a higher MAJOR is refused, once, naming the contract' 2 'a higher MAJOR than this tool was built against (ICON-SET 0.x)' 'declared glyph' @('-Check', '-CatalogRoot', $major)
    Expect 'a much higher MAJOR is refused the same way' 2 'ICON-SET 7.3, a higher MAJOR' '' @('-Check', '-CatalogRoot', (New-Catalog 'major7' 'ICON-SET 7.3'))
    Expect 'a higher MINOR is accepted: the run reaches the glyph check' 2 'declared glyph' 'higher MAJOR' @('-Check', '-CatalogRoot', $minor)
    Expect 'the MAJOR it was built against is accepted' 2 'declared glyph' 'higher MAJOR' @('-Check', '-CatalogRoot', $same)
    Expect 'a malformed carrier is could-not-verify, not a guess' 2 'not ''ICON-SET <MAJOR>.<MINOR>''' 'declared glyph' @('-Check', '-CatalogRoot', $junk)
    Expect 'a catalog with no carrier is unreachable, not a pass' 2 'vocabulary.version'' was not found' '' @('-Check', '-CatalogRoot', $none)
    Expect 'the import mode refuses a higher MAJOR too, before it writes anything' 2 'a higher MAJOR' '' @('-CatalogRoot', $major)
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Exit-Verdict 'IconCatalogCarrier' 1 "$($failures.Count) case(s) wrong: $($failures -join '; ')"
}
Exit-Verdict 'IconCatalogCarrier' 0 'a higher MAJOR is refused, a higher MINOR accepted, a malformed or missing carrier never guessed'
