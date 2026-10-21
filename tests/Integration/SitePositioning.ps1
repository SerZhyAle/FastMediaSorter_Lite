<#
.SYNOPSIS
    Fixture self-test of tools/Test-SitePositioning.ps1 (tickets 155 and 249): the gate passes a clean tiny tree
    and fails, for the right reason, each way the positioning, the components, the availability matrix, the
    component in development, the editions or a typed fact can drift.

.DESCRIPTION
    Builds a throw-away site under %TEMP% (a landing, a Server edition page, two locale pages, a README, the
    positioning source, the naming table, the Localization table, the installer scripts, the release workflow,
    the MSIX manifest, a Store listing and a data file that declares them), runs the gate against it with -Root
    and expects an exit code and a message per case. Nothing in the repository is read or written.
    About 40 child processes; run it after a change to the gate, not at every closure.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'SitePositioning' 2 'the self-test could not run' }
Write-Subject 'SitePositioning' 'tools/Test-SitePositioning.ps1 against a throw-away tree and the broken variants of it'

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
    Write-Text 'tools/site-facts.json' ($f | ConvertTo-Json -Depth 14)
}
function Edit-SiteCopy([scriptblock]$change) {
    $c = Read-Work 'tools/site-copy.json' | ConvertFrom-Json
    & $change $c
    Write-Text 'tools/site-copy.json' ($c | ConvertTo-Json -Depth 8)
}

$Lead = 'Three optional parts: a media viewer and sorter, a local SFTP folder-sharing service, and a Media Exchange Server (in development, not released yet).'
$FullName = 'Fast Media Sorter & Sharing for Windows'
$ServerName = 'Fast Media Sorter & Sharing: Folder Share Server'
$States = @('core', 'bundled', 'optional-on', 'optional-off', 'required', 'absent', 'in-development')
$Components = @('viewer', 'share', 'filedo', 'exchange')
$Channels = @('full-installer', 'store', 'server-edition')

function New-MatrixLabels([string]$lang) {
    $m = [ordered]@{ components = [ordered]@{}; channels = [ordered]@{}; states = [ordered]@{} }
    foreach ($c in $Components) { $m.components[$c] = "$lang $c" }
    foreach ($c in $Channels) { $m.channels[$c] = "$lang $c" }
    foreach ($s in $States) { $m.states[$s] = "$lang $s" }
    $m
}

function New-Fixture {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    $facts = [ordered]@{
        positioning = [ordered]@{
            source = 'docs/guides/POSITIONING.md'; lead = $Lead
            components = @(
                [ordered]@{ id = 'viewer'; title = 'Media viewer and sorter'; state = 'shipped' },
                [ordered]@{ id = 'share'; title = 'Local SFTP folder sharing'; state = 'shipped'
                    capabilities = @([ordered]@{ id = 'filedo'; title = 'FileDO virtual disks' }) },
                [ordered]@{ id = 'exchange'; title = 'Media Exchange Server'; state = 'in-development' })
            bannedPhrases = @('core of every package', 'third pillar', 'reserved door')
        }
        profiles = @(
            [ordered]@{ id = 'viewer-first'; entry = 'index.html'; leadsWith = 'viewer'; channels = @('full-installer', 'store')
                installs = @('viewer'); optional = @('share'); alternative = 'server-first'
                crossLinks = @([ordered]@{ file = 'index.html'; pattern = 'href="server\.html"'; what = 'the landing links the Server edition page' }) },
            [ordered]@{ id = 'server-first'; entry = 'server.html'; leadsWith = 'share'; channels = @('server-edition')
                installs = @('share'); optional = @('viewer', 'filedo'); alternative = 'viewer-first'
                crossLinks = @([ordered]@{ file = 'server.html'; pattern = 'href="\./"'; what = 'the Server edition page links the landing' }) })
        editions = @(
            [ordered]@{ id = 'full'; public = $true; name = $FullName },
            [ordered]@{ id = 'server'; public = $true; name = $ServerName; nameSuffix = 'Folder Share Server' })
        availability = [ordered]@{
            states = [ordered]@{ 'core' = 'always installed'; 'bundled' = 'in the package'; 'optional-on' = 'ticked'; 'optional-off' = 'unticked'; 'required' = 'the package is this'; 'absent' = 'not in this channel'; 'in-development' = 'in no released package' }
            channels = $Channels
            components = $Components
            evidence = [ordered]@{
                'e-core'   = [ordered]@{ file = 'publishing/installer/FastMediaSorter.iss'; pattern = '(?m)^Name: "core";'; what = 'the core component' }
                'e-share'  = [ordered]@{ file = 'publishing/installer/FastMediaSorter.iss'; pattern = '(?m)^Name: "share";'; what = 'the share component' }
                'e-msix'   = [ordered]@{ file = 'publishing/msix/AppxManifest.xml'; pattern = 'Application Id='; what = 'the Store package has an application' }
                'e-server' = [ordered]@{ file = 'publishing/installer/FastMediaSorterServer.iss'; pattern = '(?m)^PrivilegesRequired=admin'; what = 'the Server edition needs administrator rights' }
                'e-gate'   = [ordered]@{ file = '.github/workflows/release.yml'; pattern = "INCLUDE_EXCHANGE: 'false'"; what = 'the shipping gate is shut' }
            }
            cells = [ordered]@{
                viewer   = [ordered]@{ 'full-installer' = @('core', 'e-core'); store = @('core', 'e-msix'); 'server-edition' = @('optional-off', 'e-server') }
                share    = [ordered]@{ 'full-installer' = @('optional-on', 'e-share'); store = @('bundled', 'e-msix'); 'server-edition' = @('required', 'e-server') }
                filedo   = [ordered]@{ 'full-installer' = @('optional-off', 'e-share'); store = @('absent', 'e-msix'); 'server-edition' = @('optional-on', 'e-server') }
                exchange = [ordered]@{ 'full-installer' = @('in-development', 'e-gate'); store = @('absent', 'e-msix'); 'server-edition' = @('in-development', 'e-gate') }
            }
            facts = [ordered]@{ elevation = [ordered]@{ 'full-installer' = @('e-core'); 'server-edition' = @('e-server') } }
        }
        development = [ordered]@{
            component = 'exchange'; mentionPattern = 'Media Exchange Server'
            markers = @('in development', 'в разработке', 'у розробці')
            window = 420
            downloadLink = '(?i)(?:releases/download|/releases(?:/|\b)|winget(?:\s|%20)install|apps\.microsoft\.com|\.exe\b|-setup\b|#get\b|(?:^|/)server\.html\b)'
            scan = @('*.html', 'lang/*/*.html', 'README.md', 'publishing/store/listingData.csv', 'tools/site-copy.json')
            storeListing = [ordered]@{
                file = 'publishing/store/listingData.csv'
                mustSay = @('not part of the Store version'); mustNotSay = @('(?i)media exchange')
                serviceMention = '(?i)windows service'; qualifier = '(?i)not part of the Store version'; window = 320
                why = 'the Store package cannot register a Windows service and carries no exchange server'
            }
        }
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
                anchors = [ordered]@{ viewer = 'media viewer and sorter'; share = 'folder-sharing service'; exchange = 'Media Exchange Server' }
            },
            [ordered]@{
                file = 'index.html'; names = 3; why = 'landing doors: viewer first, server first, then the component in development'
                scopes = @('(?s)id="doors".*?</section>')
                anchors = [ordered]@{ viewer = 'id="door-1"'; share = 'id="door-2"'; exchange = 'id="door-3"' }
            },
            [ordered]@{
                file = 'README.md'; names = 3; why = 'README parts table'
                scopes = @('(?s)\| Part \|(?<region>.*?)\r?\n\r?\n')
                anchors = [ordered]@{ viewer = '\*\*Media viewer and sorter\*\*'; share = '\*\*Local SFTP folder sharing\*\*'; exchange = '\*\*Media Exchange Server\*\*' }
            })
    }
    Write-Text 'tools/site-facts.json' ($facts | ConvertTo-Json -Depth 14)
    Write-Text 'docs/guides/POSITIONING.md' ("# Positioning`n`n> $Lead`n`n| # | Component |`n| --- | --- |`n| 1 | **Media viewer and sorter** |`n| 2 | **Local SFTP folder sharing** |`n| 3 | **Media Exchange Server** |`n`nFileDO virtual disks are a capability of sharing.`n`n``$FullName`` and ``$ServerName```n")
    Write-Text 'docs/guides/PRODUCT_NAMING.md' ("``$FullName`` ``$ServerName`` Media Exchange Server`n")
    $copy = [ordered]@{
        en = [ordered]@{ dir = 'ltr'; family = $Lead; pageTitle = 'Sorting EN'; ogAlt = 'a'
            doors = @(
                @('Viewer first', 'I need a viewer', 'Sort photos and videos.', 'Download the full package'),
                @('Server first', 'I want a server', 'Open folders on a phone.', 'Server edition'),
                @('In development', 'Media Exchange Server', 'In development and in no package.', '')) }
        ru = [ordered]@{ dir = 'ltr'; pageTitle = 'Sorting RU'; ogAlt = 'a' }
        uk = [ordered]@{ dir = 'ltr'; pageTitle = 'Sorting UK'; ogAlt = 'a' }
        _matrix = [ordered]@{ en = New-MatrixLabels 'en'; ru = New-MatrixLabels 'ru'; uk = New-MatrixLabels 'uk' }
    }
    Write-Text 'tools/site-copy.json' ($copy | ConvertTo-Json -Depth 8)
    $fence = '`' * 3
    Write-Text 'docs/guides/RESEARCH.md' ("# Research`n`n${fence}gate-terms`nAcmeViewer`n${fence}`n")
    Write-Text 'src/Localization/Localization.vb' "#If NETFRAMEWORK Then`n    Public Shared ReadOnly Codes As String() = {`"ru`", `"en`"}`n#Else`n    Public Shared ReadOnly Codes As String() = {`n        `"ru`", `"en`", `"uk`"}`n#End If`n"
    Write-Text 'publishing/installer/FastMediaSorter.iss' "Name: `"core`"; Flags: fixed`nName: `"share`"; MinVersion: 10.0.14393`nResult := (Version.Major > 10) or ((Version.Major = 10) and (Version.Build >= 14393));`n"
    Write-Text 'publishing/installer/FastMediaSorterServer.iss' "[Setup]`nMinVersion=10.0.14393`nPrivilegesRequired=admin`n"
    Write-Text 'publishing/winget/server/Server.installer.yaml' "MinimumOSVersion: 10.0.14393`n"
    Write-Text 'publishing/msix/AppxManifest.xml' '<Package><Applications><Application Id="Viewer" /></Applications><Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" /></Dependencies></Package>'
    Write-Text '.github/workflows/release.yml' "env:`n  INCLUDE_EXCHANGE: 'false'`n"
    Write-Text 'publishing/store/listingData.csv' "Description,2,Text,,`"Sort your media. The always-on Server edition runs as a Windows service; it is not part of the Store version.`"`n"
    Write-Text 'index.html' ('<html><head><link rel="canonical" href="https://example.test/site/"><script type="application/ld+json">{"operatingSystem": "Windows 10, Windows 11"}</script></head><body>' +
        "<p class=`"family`">$Lead</p><p>3 languages; needs Windows 10 1607+. The other two languages have pages.</p><p>$ServerName is the Server edition. <a href=`"server.html`">Server edition page</a></p>" +
        '<section id="doors"><article id="door-1">Viewer</article><article id="door-2">Share</article><article id="door-3"><span class="pill gold">In development</span><p>Media Exchange Server is in development.</p></article></section></body></html>')
    Write-Text 'server.html' '<html><head><title>Server</title></head><body><p>Folder sharing as a service. <a href="./">Start with the viewer</a></p></body></html>'
    Write-Text 'lang/ru/index.html' '<html><head><title>Sorting RU | Fast Media Sorter &amp; Sharing</title></head><body><p>ru</p></body></html>'
    Write-Text 'lang/uk/index.html' '<html><head><title>Sorting UK | Fast Media Sorter &amp; Sharing</title></head><body><p>uk</p></body></html>'
    Write-Text 'README.md' "# Readme`n`n**Three optional parts.** A media viewer and sorter, a local SFTP folder-sharing service, and a Media Exchange Server (in development, not released yet).`n`n| Part | You want |`n| --- | --- |`n| **Media viewer and sorter** | view |`n| **Local SFTP folder sharing** | share |`n| **Media Exchange Server** | *in development* |`n`nThe end.`n"
}

$failures = [Collections.Generic.List[string]]::new()
$cases = 0
function Expect([string]$name, [int]$code, [string]$message) {
    $script:cases++
    $out = @(& $pwsh -NoProfile -File $Gate -Root $work 2>&1 | ForEach-Object { "$_" })
    $got = $LASTEXITCODE
    $text = $out -join "`n"
    $ok = ($got -eq $code) -and (-not $message -or $text.Contains($message))
    if ($ok) { Write-Host "  ok    $name" -ForegroundColor Green }
    else { Write-Host "  WRONG $name (wanted exit $code '$message', got exit $got)" -ForegroundColor Red; $text -split "`n" | ForEach-Object { Write-Host "        $_" }; $failures.Add($name) }
}

try {
    New-Fixture;  Expect 'a clean tree passes' 0 'PASS'

    # --- the components on the surfaces ---
    New-Fixture;  Edit-Work 'index.html' "<p class=`"family`">$Lead</p>" '<p class="family">A Media Exchange Server (in development) is separate; there is also a local SFTP folder-sharing service and a media viewer and sorter.</p>'
    Expect 'a family sentence that lists the components out of order fails' 1 'before it is done with'

    New-Fixture;  Edit-Work 'index.html' "<p class=`"family`">$Lead</p>" '<p class="family">A media viewer and sorter and a local SFTP folder-sharing service.</p>'
    Expect 'a surface that drops the third component fails' 1 "does not name the component 'exchange'"

    New-Fixture;  Edit-Work 'index.html' 'class="family"' 'class="intro"'
    Expect 'a surface whose region is gone fails' 1 'is not found'

    New-Fixture;  Edit-Work 'README.md' "| **Media viewer and sorter** | view |`n| **Local SFTP folder sharing** | share |" "| **Local SFTP folder sharing** | share |`n| **Media viewer and sorter** | view |"
    Expect 'the README parts table in the wrong order fails' 1 'before it is done with'

    New-Fixture;  Edit-Work 'docs/guides/POSITIONING.md' "> $Lead" '> A different sentence.'
    Expect 'a positioning source that lost the family sentence fails' 1 'does not quote the family sentence'

    New-Fixture;  Edit-Work 'index.html' '<p>3 languages;' '<p>Get Fast Media Sorter &amp; Sharing: Home Edition today. 3 languages;'
    Expect 'an edition the data file does not declare fails' 1 'does not declare as public'

    # --- FileDO is a capability of sharing, never a part of its own ---
    New-Fixture;  Edit-Facts { param($f) $f.positioning.components[1].title = 'Local SFTP folder sharing with FileDO' }
    Expect 'a component titled with FileDO fails' 1 'FileDO hosting is a capability of sharing'

    New-Fixture;  Edit-Facts { param($f) $f.positioning.lead = $f.positioning.lead + ' FileDO disks too.' }
    Expect 'a family sentence that names FileDO fails' 1 'the family sentence names FileDO'

    New-Fixture;  Edit-Work 'index.html' 'Viewer</article>' 'Viewer: the core of every package</article>'
    Expect 'a phrase of the retired model fails' 1 'a phrase of the model specification 249 retired'

    # --- the component in development: named, marked, never sold ---
    New-Fixture;  Write-Text 'about.html' '<html><body><p>Media Exchange Server is coming soon.</p></body></html>'
    Expect 'the component in development without a marker fails' 1 'with no development marker within 420 characters'

    New-Fixture;  Write-Text 'about.html' '<html><body><p><a href="https://example.test/releases/latest">Media Exchange Server (in development)</a></p></body></html>'
    Expect 'the component in development inside a download link fails' 1 'inside a link to a download'

    New-Fixture;  Write-Text 'about.html' '<html><body><p>Media Exchange Server is in development, due 2027.</p></body></html>'
    Expect 'a date next to the component in development fails' 1 'gives a date next to'

    New-Fixture;  Write-Text 'about.html' '<html><body><p>Download the Media Exchange Server (in development).</p></body></html>'
    Expect 'an invitation to get the component in development fails' 1 "tells the reader to 'Download the Media Exchange Server'"

    New-Fixture;  Edit-SiteCopy { param($c) $c.en.doors[2][3] = 'Get it' }
    Expect 'a call to action on the door of the component in development fails' 1 'carries a call to action'

    # --- the Store listing says what the Store package is and is not ---
    New-Fixture;  Edit-Work 'publishing/store/listingData.csv' 'it is not part of the Store version' 'it is great'
    Expect 'a Store listing that never says what the Store version lacks fails' 1 'does not say /not part of the Store version/'

    New-Fixture;  Edit-Work 'publishing/store/listingData.csv' 'Sort your media.' 'Sort your media with the Media Exchange Server.'
    Expect 'a Store listing that names the exchange server fails' 1 "says 'Media Exchange'"

    # --- the availability matrix and the install profiles ---
    New-Fixture;  Edit-Facts { param($f) $f.availability.cells.viewer.'full-installer' = @('core') }
    Expect 'a matrix cell without evidence fails' 1 'cites no evidence'

    New-Fixture;  Edit-Work 'publishing/installer/FastMediaSorter.iss' 'Name: "core"' 'Name: "base"'
    Expect 'a packaging change that contradicts the matrix fails' 1 'no longer matches'

    New-Fixture;  Edit-Facts { param($f) $f.availability.cells.exchange.store = @('optional-on', 'e-gate') }
    Expect 'the component in development shipped in a channel fails' 1 'is in development and the matrix says'

    New-Fixture;  Edit-Facts { param($f) $f.availability.cells.share.store = @('absent', 'e-msix'); $f.availability.cells.filedo.store = @('optional-on', 'e-msix') }
    Expect 'a capability of sharing in a package without sharing fails' 1 'a capability of sharing is not in a package without sharing'

    New-Fixture;  Edit-Facts { param($f) $f.availability.cells.viewer.store = @('sometimes', 'e-msix') }
    Expect 'an unknown matrix state fails' 1 "has the unknown state 'sometimes'"

    New-Fixture;  Edit-Work 'server.html' 'href="./"' 'href="x.html"'
    Expect 'an install profile that lost its cross-link fails' 1 'lost its cross-link'

    New-Fixture;  Edit-Facts { param($f) $f.profiles = @($f.profiles[0]) }
    Expect 'one install profile instead of two fails' 1 'declares 1 install profiles'

    New-Fixture;  Edit-SiteCopy { param($c) $c._matrix.uk.states.PSObject.Properties.Remove('core') }
    Expect 'a matrix label missing in a language fails' 1 'is out of step with _matrix.en'

    # --- the typed facts ---
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

    # --- discoverability ---
    New-Fixture;  Edit-Facts { param($f) $f.discoverability.topics = @(1..21 | ForEach-Object { "topic-$_" }); $f.discoverability.wingetTags = @() }
    Expect 'more topics than GitHub allows fails' 1 "over GitHub's 20"

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.tagline = 'Sort your lite media with one key per file' }
    Expect 'a word the naming standard retired in the tagline fails' 1 'a word the naming standard retired'

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.wingetTags = @('media-sorter', 'random-tag') }
    Expect 'a winget tag that is neither a topic nor declared fails' 1 'neither a topic nor declared'

    New-Fixture;  Edit-Facts { param($f) $f.discoverability.topics = @('media-sorter', 'acmeviewer'); $f.discoverability.wingetTags = @('media-sorter') }
    Expect 'a competitor name among the topics fails' 1 'names a competitor (D-7)'

    New-Fixture;  Edit-Work 'index.html' "<p>$ServerName is the Server edition." "<p>$ServerName is the Server edition, like AcmeViewer."
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

if ($failures.Count) { Exit-Verdict 'SitePositioning' 1 "$($failures.Count) of $cases cases wrong: $($failures -join '; ')" }
Exit-Verdict 'SitePositioning' 0 "the gate passes a clean tree and fails each of $($cases - 1) drifts for its own reason"
