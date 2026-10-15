<#
.SYNOPSIS
    Answers the questions an agent asks of the specification ledger without reading it whole.

.DESCRIPTION
    docs\specifications\README.md is ~90 KB (one table row per ticket). It was read in full ~25 times in
    the measured sessions to answer three questions. This script answers them in a few lines.

    -Next        the next free ticket number (re-read on every call: parallel sessions take numbers)
    -Id 231      the ledger row of one ticket, its file and where it lives (active or done\)
    -Find text   rows whose title or note contains the text (case-insensitive, first 25)
    -List        the newest rows, compact (-Top N, default 30)

.EXAMPLE
    pwsh -NoProfile -File tools\Get-Spec.ps1 -Next
    pwsh -NoProfile -File tools\Get-Spec.ps1 -Id 231
    pwsh -NoProfile -File tools\Get-Spec.ps1 -Find exchange -Top 10

.NOTES
    Read-only. Exit 0 answered, 2 could not answer (ledger missing).
#>
[CmdletBinding()]
param(
    [switch]$Next,
    [string]$Id,
    [string]$Find,
    [switch]$List,
    [int]$Top = 30,
    [int]$NoteWidth = 420
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root 'docs\specifications'
$ledger = Join-Path $dir 'README.md'
if (-not (Test-Path -LiteralPath $ledger)) { Write-Host "Get-Spec: ledger missing: $ledger"; exit 2 }

$rows = foreach ($l in [IO.File]::ReadAllLines($ledger)) {
    $m = [regex]::Match($l, '^\|\s*(?<n>\d{3})\s*\|\s*\[(?<t>.+?)\]\((?<f>[^)]+)\)\s*\|\s*(?<note>.*?)\s*\|?\s*$')
    if ($m.Success) {
        [pscustomobject]@{ Ticket = [int]$m.Groups['n'].Value; Title = $m.Groups['t'].Value; File = $m.Groups['f'].Value; Note = $m.Groups['note'].Value }
    }
}
$rows = @($rows)

function Short([string]$s, [int]$n) { if ($s.Length -le $n) { $s } else { $s.Substring(0, $n) + ' [..]' } }

if ($Next) {
    $stated = ([regex]::Match([IO.File]::ReadAllText($ledger), 'next available number is \*\*(\d+)\*\*')).Groups[1].Value
    $max = 0
    foreach ($d in @($dir, (Join-Path $dir 'done'))) {
        Get-ChildItem -LiteralPath $d -File -Filter '*.md' -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -match '^(\d{3})_') { $max = [Math]::Max($max, [int]$Matches[1]) }
        }
    }
    $fromFiles = $max + 1
    Write-Host "next free ticket: $fromFiles (highest file number $max; the ledger sentence says $stated)"
    if ($stated -and [int]$stated -ne $fromFiles) {
        Write-Host "  note: the ledger sentence and the files disagree - use the larger, then fix the sentence in the same edit"
    }
    exit 0
}

if ($Id) {
    $n = [int]$Id
    $r = $rows | Where-Object { $_.Ticket -eq $n } | Select-Object -First 1
    $active = Get-ChildItem -LiteralPath $dir -File -Filter ('{0:D3}_*.md' -f $n) -ErrorAction SilentlyContinue | Select-Object -First 1
    $done = Get-ChildItem -LiteralPath (Join-Path $dir 'done') -File -Filter ('{0:D3}_*.md' -f $n) -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $r -and -not $active -and -not $done) { Write-Host "ticket ${n} - not in the ledger and no file"; exit 0 }
    if ($r) { Write-Host "ticket ${n}  $($r.Title)"; Write-Host "  note: $(Short $r.Note 900)" }
    foreach ($f in @($active, $done) | Where-Object { $_ }) {
        $where = if ($f.DirectoryName -like '*\done') { 'done\' } else { 'active' }
        Write-Host ("  file: {0}  ({1:N0} B, {2})" -f $f.FullName.Replace($root + '\', ''), $f.Length, $where)
    }
    exit 0
}

if ($Find) {
    $hit = @($rows | Where-Object { $_.Title -match [regex]::Escape($Find) -or $_.Note -match [regex]::Escape($Find) })
    Write-Host "$($hit.Count) row(s) match '$Find'"
    foreach ($r in $hit | Sort-Object Ticket -Descending | Select-Object -First ([Math]::Min($Top, 25))) {
        Write-Host ("{0}  {1}" -f $r.Ticket, (Short $r.Title 90))
        Write-Host ("     {0}" -f (Short $r.Note $NoteWidth))
    }
    exit 0
}

# default and -List: newest rows, compact
Write-Host "$($rows.Count) ledger rows; newest $Top (use -Id N for one row, -Find text, -Next)"
foreach ($r in $rows | Sort-Object Ticket -Descending | Select-Object -First $Top) {
    Write-Host ("{0}  {1}" -f $r.Ticket, (Short $r.Title 100))
}
exit 0
