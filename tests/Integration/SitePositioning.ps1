<#
.SYNOPSIS
    Fixture self-test of tools/Test-SitePositioning.ps1 (ticket 155): the gate passes a clean tiny tree
    and fails, for the right reason, each way the positioning, the editions or a typed fact can drift.

.DESCRIPTION
    Builds a throw-away site under %TEMP% (a landing and two locale pages, a README, the positioning
    source, the naming table, the Localization table, the installer scripts, the Server winget manifest
    and the MSIX manifest, and a data file that declares them), runs the gate against it with -Root and
    expects an exit code and a message per case. Nothing in the repository is read or written.
    About 23 child processes; run it after a change to the gate, not at every closure.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'SitePositioning' 2 'the self-test could not run' }
Write-Subject 'SitePositioning' 'tools/Test-SitePositioning.ps1 against a throw-away tree and twenty-two broken variants of it'

$Gate = Join-Path $RepoRoot 'tools\Test-SitePositioning.ps1'
$pwsh = (Get-Process -Id $PID).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("fms-site-positioning-" + [guid]::NewGuid().ToString('N'))

function Write-Text([string]$rel, [string]$text) {
    $p = Join-Path $work $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p -Parent) | Out-Null
    [IO.File]::WriteAllText($p, $text, [Text.UTF8Encoding]::new($false))
}
function Read-Work([string]$rel) { [IO.File]::ReadAllText((Join-Path $work $rel), [Text.UTF8Encoding]::new($false)) }
function Edit-Work([string]$rel, [string]$old, [string]$new) {
    $t = Read-Work $rel
    if (-not $t.Contains($old)) { throw "the fixture file $rel has no '$old' to replace" }
    Write-Text $rel $t.Replace($old, $new)
}

function Edit-Facts([scriptblock]$change) {
    $f = Read-Work 'tools/site-facts.json' | ConvertFrom-Json
    & $change $f
    Write-Text 'tools/site-facts.json' ($f | ConvertTo-Json -Depth 12)
}

$Lead = 'View and sort your media; the same program is also a folder-share server and a host for FileDO virtual disks.'
$FullName = 'Fast Media Sorter & Sharing for Windows'
$ServerName = 'Fast Media Sorter & Sharing: Folder Share Server'
$DoorBlock = '<article class="card scenario-card" id="door-3"><span class="pill gold">Planned</span><p>Media Exchange Server is planned.</p></article>'

function New-Fixture {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    $facts = [ordered]@{
        positioning = [ordered]@{
            source = 'docs/guides/POSITIONING.md'; lead = $Lead
            pillars = @(
                [ordered]@{ id = 'viewer'; title = 'View and sort' },
                [ordered]@{ id = 'share'; title = 'Share folders' },
                [ordered]@{ id = 'disks'; title = 'FileDO virtual disks' })
        }
        editions = @(
            [ordered]@{ id = 'full'; public = $true; name = $FullName },
            [ordered]@{ id = 'server'; public = $true; name = $ServerName; nameSuffix = 'Folder Share Server' })
        reservations = @([ordered]@{
            id = 'exchange'; name = 'Media Exchange Server'; public = $false; mentionPattern = 'Media Exchange'
            surfaces = @([ordered]@{ file = 'index.html'; scope = '(?s)<article class="card scenario-card" id="door-3">.*?</article>'; marker = 'pill gold' })
            scan = @('*.html', 'lang/*/*.html', 'README.md')
        })
        facts = [ordered]@{
            sources = [ordered]@{
                localization = 'src/Localization/Localization.vb'; siteCopy = 'tools/site-copy.json'
                installer = 'publishing/installer/FastMediaSorter.iss'; serverInstaller = 'publishing/installer/FastMediaSorterServer.iss'
                serverWinget = 'publishing/winget/server/Server.installer.yaml'; msix = 'publishing/msix/AppxManifest.xml'
                naming = 'docs/guides/PRODUCT_NAMING.md'
            }
            windows10Releases = [ordered]@{ '1607' = 14393; '1809' = 17763 }
            languages = [ordered]@{ count = 3; codes = @('ru', 'en', 'uk'); fallbackCount = 2; translatedPages = 2 }
            platform = [ordered]@{
                program = [ordered]@{ os = 'Windows 10'; version = '1607'; build = 14393 }
                store = [ordered]@{ os = 'Windows 10'; version = '1809'; build = 17763 }
                schemaOperatingSystem = 'Windows 10, Windows 11'
            }
            scan = @('*.html', 'lang/*/*.html', 'README.md')
        }
        discoverability = [ordered]@{
            source = 'docs/guides/POSITIONING.md'; category = 'media sorter'; synonyms = @('viewer')
            tagline = 'Sort your media with one key per file'
            githubDescription = 'Media sorter for Windows: one key per file. Fast Media Sorter & Sharing, free.'
            homepage = 'https://example.test/site/'
            topics = @('media-sorter', 'viewer'); wingetTags = @('media-sorter', 'viewer'); wingetOnlyTags = @()
            storeSearchTerms = [ordered]@{ 'en-us' = @('media sorter') }
            limits = [ordered]@{ taglineWordsMin = 6; taglineWordsMax = 10; githubDescription = 350; topics = 20; topicLength = 50; wingetTags = 16; storeSearchTerms = 7; title = 60; metaDescription = 160 }
            titles = [ordered]@{ separator = ' | '; suffix = 'Fast Media Sorter & Sharing' }
            forbiddenWords = @('lite', 'lean')
            competitorTermsSource = 'docs/guides/RESEARCH.md'
            competitorPhrases = @('alternative to')
            competitorScan = @('*.html', 'lang/*/*.html', 'README.md')
            socialCard = [ordered]@{ lines = @('Line one'); alt = 'A card'; sizes = @() }
            applied = @('site-titles')
        }
        surfaces = @(
            [ordered]@{
                file = 'index.html'; names = 3; why = 'landing family sentence'
                scopes = @('(?s)<p class="family">(?<region>.*?)</p>')
                anchors = [ordered]@{ viewer = 'View and sort'; share = 'folder-share server'; disks = 'FileDO virtual disks' }
            },
            [ordered]@{
                file = 'README.md'; names = 2; why = 'README door table'
                scopes = @('(?s)\| Door \|(?<region>.*?)\r?\n\r?\n')
                anchors = [ordered]@{ viewer = '\*\*Viewer\*\*'; share = '\*\*Share\*\*' }
            })
    }
    Write-Text 'tools/site-facts.json' ($facts | ConvertTo-Json -Depth 12)
    Write-Text 'docs/guides/POSITIONING.md' ("# Positioning`n`n> $Lead`n`n| # | Pillar |`n| --- | --- |`n| 1 | **View and sort** |`n| 2 | **Share folders** |`n| 3 | **FileDO virtual disks** |`n`n``$FullName`` and ``$ServerName```n`nThe reservation: Media Exchange Server.`n")
    Write-Text 'docs/guides/PRODUCT_NAMING.md' ("``$FullName`` ``$ServerName`` Media Exchange Server`n")
    Write-Text 'tools/site-copy.json' (@{ en = @{ dir = 'ltr'; family = $Lead; pageTitle = 'Sorting EN'; ogAlt = 'a' }; ru = @{ dir = 'ltr'; pageTitle = 'Sorting RU'; ogAlt = 'a' }; uk = @{ dir = 'ltr'; pageTitle = 'Sorting UK'; ogAlt = 'a' } } | ConvertTo-Json -Depth 4)
    $fence = '`' * 3
    Write-Text 'docs/guides/RESEARCH.md' ("# Research`n`n${fence}gate-terms`nAcmeViewer`n${fence}`n")
    Write-Text 'src/Localization/Localization.vb' "#If NETFRAMEWORK Then`n    Public Shared ReadOnly Codes As String() = {`"ru`", `"en`"}`n#Else`n    Public Shared ReadOnly Codes As String() = {`n        `"ru`", `"en`", `"uk`"}`n#End If`n"
    Write-Text 'publishing/installer/FastMediaSorter.iss' "Name: `"share`"; MinVersion: 10.0.14393`nResult := (Version.Major > 10) or ((Version.Major = 10) and (Version.Build >= 14393));`n"
    Write-Text 'publishing/installer/FastMediaSorterServer.iss' "[Setup]`nMinVersion=10.0.14393`n"
    Write-Text 'publishing/winget/server/Server.installer.yaml' "MinimumOSVersion: 10.0.14393`n"
    Write-Text 'publishing/msix/AppxManifest.xml' '<Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" /></Dependencies>'
    Write-Text 'index.html' ('<html><head><link rel="canonical" href="https://example.test/site/"><script type="application/ld+json">{"operatingSystem": "Windows 10, Windows 11"}</script></head><body>' +
        "<p class=`"family`">$Lead</p><p>3 languages; needs Windows 10 1607+. The other two languages have pages.</p><p>$ServerName is the Server edition.</p>" +
        "<section id=`"doors`"><article id=`"door-1`">Viewer</article><article id=`"door-2`">Share</article>$DoorBlock</section></body></html>")
    Write-Text 'lang/ru/index.html' '<html><head><title>Sorting RU | Fast Media Sorter &amp; Sharing</title></head><body><p>ru</p></body></html>'
    Write-Text 'lang/uk/index.html' '<html><head><title>Sorting UK | Fast Media Sorter &amp; Sharing</title></head><body><p>uk</p></body></html>'
    Write-Text 'README.md' "# Readme`n`n| Door | You want |`n| --- | --- |`n| **Viewer** | view |`n| **Share** | share |`n`nThe end.`n"
}

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

    New-Fixture;  Edit-Work 'index.html' "<p class=`"family`">$Lead</p>" '<p class="family">The same program is a folder-share server and a host for FileDO virtual disks; it is also View and sort for your media.</p>'
    Expect 'a family sentence that lists the pillars out of order fails' 1 'before it is done with'

    New-Fixture;  Edit-Work 'index.html' "<p class=`"family`">$Lead</p>" '<p class="family">View and sort your media; the same program is also a folder-share server.</p>'
    Expect 'a surface that drops the third pillar fails' 1 "does not name the pillar 'disks'"

    New-Fixture;  Edit-Work 'index.html' 'class="family"' 'class="intro"'
    Expect 'a surface whose region is gone fails' 1 'is not found'

    New-Fixture;  Edit-Work 'README.md' "| **Viewer** | view |`n| **Share** | share |" "| **Share** | share |`n| **Viewer** | view |"
    Expect 'the README door table in the wrong order fails' 1 'before it is done with'

    New-Fixture;  Edit-Work 'docs/guides/POSITIONING.md' "> $Lead" '> A different sentence.'
    Expect 'a positioning source that lost the family sentence fails' 1 'does not quote the family sentence'

    New-Fixture;  Edit-Work 'index.html' '<p>3 languages;' '<p>Get Fast Media Sorter &amp; Sharing: Home Edition today. 3 languages;'
    Expect 'an edition the data file does not declare fails' 1 'does not declare as public'

    New-Fixture;  Edit-Work 'README.md' 'The end.' 'Coming: the Media Exchange Server.'
    Expect 'a mention of the planned server outside its declared place fails' 1 'outside the place the reservation declares'

    New-Fixture;  Edit-Work 'index.html' '<p>Media Exchange Server is planned.</p>' '<p>Media Exchange Server is planned: <a href="x.html">download</a></p>'
    Expect 'a link inside the reservation fails' 1 'links somewhere from the reservation'

    New-Fixture;  Edit-Work 'index.html' 'Planned</span>' 'Planned for 2027</span>'
    Expect 'a date inside the reservation fails' 1 'gives a date'

    New-Fixture;  Edit-Work 'index.html' '<p>3 languages;' '<p>12 languages;'
    Expect 'a wrong language count fails' 1 "says '12 languages'"

    New-Fixture;  Edit-Work 'index.html' 'Windows 10 1607+' 'Windows 10 1703+'
    Expect 'a wrong platform floor fails' 1 "says 'Windows 10 1703'"

    New-Fixture;  Edit-Work 'src/Localization/Localization.vb' '"ru", "en", "uk"}' '"ru", "en", "uk", "de"}'
    Expect 'a language added in the code and not in the facts fails' 1 'ships the languages'

    New-Fixture;  Edit-Work 'publishing/installer/FastMediaSorter.iss' 'Version.Build >= 14393' 'Version.Build >= 17763'
    Expect 'an installer floor that moved fails' 1 'the UseModernExe build floor'

    New-Fixture;  Edit-Work 'publishing/msix/AppxManifest.xml' '10.0.17763.0' '10.0.19041.0'
    Expect 'a Store manifest floor that moved fails' 1 'the MSIX TargetDeviceFamily build'
    New-Fixture;  Edit-Facts { param($f) $f.discoverability.topics = @(1..21 | ForEach-Object { "topic-$_" }); $f.discoverability.wingetTags = @() }
    Expect 'more topics than GitHub allows fails' 1 "over GitHub's 20"

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.tagline = 'Sort your lite media with one key per file' }
    Expect 'a word the naming standard retired in the tagline fails' 1 'a word the naming standard retired'

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.wingetTags = @('media-sorter', 'random-tag') }
    Expect 'a winget tag that is neither a topic nor declared fails' 1 'neither a topic nor declared'

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.topics = @('media-sorter', 'acmeviewer'); $f.discoverability.wingetTags = @('media-sorter') }
    Expect 'a competitor name among the topics fails' 1 'names a competitor (D-7)'

    New-Fixture;  Edit-Work 'index.html' "<p>$ServerName is the Server edition.</p>" "<p>$ServerName is the Server edition, like AcmeViewer.</p>"
    Expect 'a competitor name on a page fails' 1 'names a competitor product'

    New-Fixture;  Edit-Work 'README.md' 'The end.' 'An alternative to something.'
    Expect 'an "alternative to" phrase on a surface fails' 1 'names no alternative to a product'

    New-Fixture;  Edit-Work 'lang/ru/index.html' '<title>Sorting RU |' '<title>Something else |'
    Expect 'a locale page title that is not the derived one fails' 1 'the worksheet derives'

    New-Fixture;  Write-Text 'docs/guides/RESEARCH.md' "# Research`n"
    Expect 'a research guide without its gate-terms block fails' 1 'no fenced gate-terms block'
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

if ($failures.Count) { Exit-Verdict 'SitePositioning' 1 "$($failures.Count) cases wrong: $($failures -join '; ')" }
Exit-Verdict 'SitePositioning' 0 'the gate passes a clean tree and fails each of twenty-two drifts for its own reason'
