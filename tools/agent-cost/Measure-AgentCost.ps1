param(
    # Claude Code names a project folder after the repository path: drive colon, backslashes and underscores become '-'.
    [string]$Transcripts = (Join-Path $HOME ('.claude\projects\' + ((Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path -replace '[:\\_]', '-'))),
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'fms-agent-cost')
)
# Re-runs the whole agent-cost measurement (README.md has the 2026-10-15 baseline to compare against).
# Run no sooner than two weeks after a process change; a one-day delta is the old corpus plus one day.
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$plugin = ((Get-Content "$HOME/.claude/plugins/installed_plugins.json" -Raw | ConvertFrom-Json).plugins.'sza@sza-unified-rules' |
    Sort-Object lastUpdated -Descending | Select-Object -First 1).installPath
$miner = Join-Path $plugin 'tools\mine-agent-transcripts.py'
if (-not (Test-Path $miner) -or -not (Test-Path $Transcripts)) { Write-Host 'Measure-AgentCost: COULD NOT VERIFY (miner or transcripts missing)'; exit 2 }
python -I $miner --root $Transcripts --out $Out | Out-Null
python -I (Join-Path $PSScriptRoot 'carried.py')  $Transcripts (Join-Path $Out 'carried.json')
python -I (Join-Path $PSScriptRoot 'commands.py') $Transcripts (Join-Path $Out 'commands.json')
python -I (Join-Path $PSScriptRoot 'failures.py') $Transcripts | Set-Content (Join-Path $Out 'failures.json') -Encoding utf8
python -I (Join-Path $PSScriptRoot 'simulate.py') $Transcripts | Set-Content (Join-Path $Out 'simulate.txt') -Encoding utf8
Get-Content (Join-Path $Out 'transcript_digest.txt') -TotalCount 12
$c = Get-Content (Join-Path $Out 'carried.json') -Raw | ConvertFrom-Json
"first-request context p50: main {0:N0}, subagent {1:N0}  (baseline 113,404 / 98,129)" -f $c.prefix_main.p50, $c.prefix_nested.p50
"redundant re-reads: {0} ({1:N0} chars)  (baseline 805 / 6,717,640)" -f $c.reread_total, $c.reread_chars_total
"Output directory: $Out"
exit 0
