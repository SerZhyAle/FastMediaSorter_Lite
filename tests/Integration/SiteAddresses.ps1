<#
.SYNOPSIS
    Fixture self-test of tools/Test-SiteAddresses.ps1 (ticket 150): the gate passes a clean tiny tree
    and fails, for the right reason, each way an address can go wrong.

.DESCRIPTION
    Builds a throw-away site under %TEMP% (a landing and a trust page in English and Russian, the
    privacy page, a sitemap, a README as the one holder), runs the gate against it with -Root and
    expects an exit code and a message per case. Nothing in the repository is read or written.
    About 10 child processes; run it after a change to the gate, not at every closure.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'SiteAddresses' 2 'the self-test could not run' }
Write-Subject 'SiteAddresses' 'tools/Test-SiteAddresses.ps1 against a throw-away tree and eight broken variants of it'

$Gate = Join-Path $RepoRoot 'tools\Test-SiteAddresses.ps1'
$pwsh = (Get-Process -Id $PID).Path
$B = 'https://serzhyale.github.io/FastMediaSorter_Lite/'
$work = Join-Path ([IO.Path]::GetTempPath()) ("fms-site-addresses-" + [guid]::NewGuid().ToString('N'))

function Write-Text([string]$rel, [string]$text) {
    $p = Join-Path $work $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p -Parent) | Out-Null
    [IO.File]::WriteAllText($p, $text, [Text.UTF8Encoding]::new($false))
}

function New-Fixture {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    Write-Text 'index.html' '<html><body><section id="get"></section></body></html>'
    Write-Text 'trust.html' '<html></html>'
    Write-Text 'docs/privacy.html' '<html></html>'
    Write-Text 'lang/ru/index.html' '<html></html>'
    Write-Text 'lang/ru/trust.html' '<html></html>'
    Write-Text 'tools/site-copy.json' '{"_comment":"x","en":{},"ru":{}}'
    Write-Text 'sitemap.xml' ('<?xml version="1.0"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' +
        "<url><loc>$B</loc></url><url><loc>${B}trust.html</loc></url><url><loc>${B}lang/ru/</loc></url><url><loc>${B}lang/ru/trust.html</loc></url></urlset>")
    Write-Text 'README.md' ("See $B and [trust]($($B)trust.html)." + "`n")
    Write-List @(
        '{"group":"landing","locale":"lang/<code>/index.html","sitemap":true}',
        '{"group":"trust","locale":"lang/<code>/trust.html","sitemap":true}',
        '{"group":"privacy","locale":null,"sitemap":false}',
        ('{"address":"' + $B + '","holder":"README.md","kind":"landing"}'),
        ('{"address":"' + $B + 'trust.html","holder":"README.md","kind":"trust"}'),
        ('{"address":"' + $B + 'docs/privacy.html","holder":"Partner Center","kind":"privacy","carried":"external"}'))
}
function Write-List([string[]]$lines) { Write-Text 'tools/site-addresses.jsonl' (($lines -join "`n") + "`n") }
function Get-ListLines { @(Get-Content -LiteralPath (Join-Path $work 'tools/site-addresses.jsonl') -Encoding UTF8) }

$failures = [Collections.Generic.List[string]]::new()
function Expect([string]$name, [int]$code, [string]$message) {
    $out = @(& $pwsh -NoProfile -File $Gate -Root $work 2>&1 | ForEach-Object { "$_" })
    $got = $LASTEXITCODE
    $text = $out -join "`n"
    $ok = ($got -eq $code) -and (-not $message -or $text.Contains($message))
    if ($ok) { Write-Host "  ok    $name" -ForegroundColor Green }
    else { Write-Host "  WRONG $name (wanted exit $code '$message', got exit $got)" -ForegroundColor Red; $text -split "`n" | ForEach-Object { Write-Host "        $_" }; $failures.Add($name) }
}

try {
    New-Fixture;  Expect 'a clean tree passes' 0 'PASS'

    New-Fixture;  Write-List ((Get-ListLines) + ('{"address":"' + $B + 'missing.html","holder":"README.md","kind":"landing"}'))
    Add-Content (Join-Path $work 'README.md') "${B}missing.html"
    Expect 'an address that points at no page fails' 1 'points at no page'

    New-Fixture;  Write-List ((Get-ListLines) + ('{"address":"' + $B + '#nowhere","holder":"README.md","kind":"landing"}'))
    Add-Content (Join-Path $work 'README.md') "${B}#nowhere"
    Expect 'an anchor the page lacks fails' 1 'anchor the page does not have'

    New-Fixture;  Write-List ((Get-ListLines) + '{"address":"https://example.org/x.html","holder":"README.md","kind":"landing"}')
    Expect 'an address off the site fails' 1 'is not on the site'

    New-Fixture;  Write-Text 'README.md' "nothing here`n"
    Expect 'a holder that lost its address fails' 1 'does not carry'

    New-Fixture;  Add-Content (Join-Path $work 'README.md') "and ${B}docs/privacy.html too"
    Expect 'an address held but not listed fails' 1 'does not list for it'

    New-Fixture;  Remove-Item (Join-Path $work 'lang/ru/trust.html')
    Expect 'a missing locale sibling fails' 1 'the ru sibling has no page'

    New-Fixture;  $sm = Join-Path $work 'sitemap.xml'; [IO.File]::WriteAllText($sm, ([IO.File]::ReadAllText($sm).Replace("<url><loc>${B}trust.html</loc></url>", '')))
    Expect 'a page the sitemap does not list fails' 1 'sitemap.xml does not list it'

    New-Fixture;  Remove-Item (Join-Path $work 'tools/site-addresses.jsonl')
    Expect 'a missing list is could-not-verify, not a pass' 2 'COULD NOT VERIFY'
} finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

if ($failures.Count) { Exit-Verdict 'SiteAddresses' 1 ("$($failures.Count) case(s) wrong: " + ($failures -join '; ')) }
Exit-Verdict 'SiteAddresses' 0 'the gate passes a clean tree and fails the eight broken ones, each for its own reason'
