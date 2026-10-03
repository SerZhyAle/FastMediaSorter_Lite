<#
.SYNOPSIS
    Exercise the documentation gate in an isolated miniature git repository.
#>
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../tools/lib/Verdict.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('fms-doc-registry-' + [guid]::NewGuid().ToString('N'))
$gate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tools/Test-DocRegistry.ps1'))
$failures = [Collections.Generic.List[string]]::new()
$checks = 0
Write-Subject 'DocRegistry' 'isolated registry, link, anchor, asset, style and source-fact fixtures'
try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'docs'), (Join-Path $fixture 'src/Modern') -Force | Out-Null
    & git -C $fixture init -q
    if ($LASTEXITCODE) { throw 'fixture git init failed' }
    function Put([string]$path, [string]$text) { [IO.File]::WriteAllText((Join-Path $fixture $path), $text) }
    Put 'CLAUDE.md' 'The same five Designer pages become a layout of nine pages, plus four that exist only there.'
    Put 'src/Table_Form.Designer.vb' ((1..5 | ForEach-Object { "Me.Tab_Control.Controls.Add(Me.Tab_Page_$_)" }) -join "`n")
    Put 'src/Table_Form.ModernLayout.vb' ((1..4 | ForEach-Object { 'Tab_Control.TabPages.Add(page)' }) -join "`n")
    Put 'src/FastMediaSorter.vbproj' '<WarningsAsErrors>41999</WarningsAsErrors><WarningsAsErrors>41999</WarningsAsErrors>'
    Put 'src/Modern/FastMediaSorter.Modern.vbproj' '<WarningsAsErrors>41999</WarningsAsErrors>'
    Put 'asset.svg' '<svg/>'
    Put '.gitignore' "docs/specifications/`n"
    Put 'docs/document-registry-policy.json' '{"extensions":[".md",".html"],"ignore":[{"prefix":"docs/specifications/","reason":"local queue"}],"localSpecificationLinks":"forbidden"}'
    $baseline = @'
# Sample `heading`
# Sample `heading`
[duplicate](#sample-heading-1)
[source](../src/Table_Form.Designer.vb#L5)
![asset](../asset.svg)
[reference][guide]
[guide]: ../CLAUDE.md
````
... smart “quotes” and a missing [link](missing.md)
````
Inline `...` is a literal.
'@
    Put 'docs/sample.md' $baseline
    $rows = foreach ($p in 'CLAUDE.md','docs/sample.md') { @{path=$p;topic='Fixture';area='engineering';triggers=@('Fixture changes');role='source'} | ConvertTo-Json -Compress }
    $registry = $rows -join "`n"
    Put 'docs/DOCUMENT_REGISTRY.jsonl' $registry
    & git -C $fixture add .
    if ($LASTEXITCODE) { throw 'fixture git add failed' }
    function Assert-Gate([string]$name, [int]$expected, [string]$diagnostic = '') {
        $script:checks++
        $output = @(& pwsh -NoProfile -File $gate -Root $fixture 2>&1)
        $actual = $LASTEXITCODE
        if ($actual -ne $expected -or ($diagnostic -and ($output -join "`n") -notmatch [regex]::Escape($diagnostic))) {
            $failures.Add("${name}: expected $expected / '$diagnostic', got $actual; $($output -join ' ')")
        }
    }
    Assert-Gate 'valid anchors, code exclusions and line bookmark' 0
    $cases = @(
        @('broken link','[bad](missing.md)','missing target'),
        @('broken anchor','[bad](#absent)','missing anchor'),
        @('local queue','[bad](specifications/058.md)','owner-local specification'),
        @('style','Prose ...','house style'),
        @('smart quotes','Prose “text”','house style'),
        @('dash','Prose — text','house style'),
        @('http','[bad](http://example.com)','unsafe'),
        @('remote image','![bad](https://example.com/pixel.png)','remote image'),
        @('missing asset','![bad](absent.png)','missing target'),
        @('undefined reference','[bad][absent]','undefined reference'),
        @('bad source line','[bad](../src/Table_Form.Designer.vb#L99)','invalid source line')
    )
    foreach ($case in $cases) { Put 'docs/sample.md' ($baseline + "`n" + $case[1]); Assert-Gate $case[0] 1 $case[2] }
    Put 'docs/sample.md' $baseline
    Put 'docs/new.md' '# Undeclared'
    Assert-Gate 'reverse coverage' 1 'undeclared document'
    Remove-Item -LiteralPath (Join-Path $fixture 'docs/new.md')
    Put 'untracked.svg' '<svg/>'
    Put 'docs/sample.md' ($baseline + "`n![new](../untracked.svg)")
    Assert-Gate 'untracked asset' 1 'asset is not tracked'
    Put 'docs/sample.md' $baseline
    Put 'docs/DOCUMENT_REGISTRY.jsonl' ($registry + "`n" + $rows[0])
    Assert-Gate 'duplicate declaration' 1 'duplicate record'
    Put 'docs/DOCUMENT_REGISTRY.jsonl' ($registry.Replace('docs/sample.md','docs/absent.md'))
    Assert-Gate 'forward coverage' 1 'declared file missing'
    Put 'docs/DOCUMENT_REGISTRY.jsonl' $registry
    Put 'CLAUDE.md' 'The same five Designer pages become a layout of eight pages, plus three that exist only there.'
    Assert-Gate 'source facts' 1 'settings page counts drifted'
    Put 'src/Modern/FastMediaSorter.Modern.vbproj' '<WarningsAsErrors>42000</WarningsAsErrors>'
    Assert-Gate 'warning parity' 1 'WarningsAsErrors parity'
    Put 'docs/DOCUMENT_REGISTRY.jsonl' 'invalid json'
    Assert-Gate 'unreadable registry' 2
    Remove-Item -LiteralPath (Join-Path $fixture 'docs/DOCUMENT_REGISTRY.jsonl')
    Assert-Gate 'missing registry' 2
} catch { Write-Host $_.Exception.Message; Exit-Verdict 'DocRegistry' 2 'fixture could not run' }
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -like 'fms-doc-registry-*') { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue }
}
if ($failures.Count) { $failures | ForEach-Object { Write-Host "  - $_" }; Exit-Verdict 'DocRegistry' 1 "$($failures.Count) failed fixture(s)" }
Exit-Verdict 'DocRegistry' 0 "$checks behavior fixtures"
