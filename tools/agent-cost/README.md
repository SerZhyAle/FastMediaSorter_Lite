# Agent cost - measurement scripts and the 2026-10-15 baseline

What an AI-agent session costs in this repository, measured from the Claude Code transcripts instead of argued.
The method is the `agent-cost` skill of the `sza` plugin (canon `AI_USAGE` sections 3 and 5): the miner it ships
deduplicates by `requestId`, walks nested sub-agent sessions, counts a hard failure by the error flag only and
segments on compaction boundaries. The four scripts here answer the repository-specific questions on top of it.

## Run it

```powershell
pwsh -NoProfile -File tools\agent-cost\Measure-AgentCost.ps1                 # all transcripts of this project
pwsh -NoProfile -File tools\agent-cost\Measure-AgentCost.ps1 -Out C:\scratch\cost-2026-11
```

Exit 0 written, 2 could not verify (miner or transcripts missing). The Bash tool has no `python` on PATH - use the
PowerShell tool or the wrapper above.

| Script | Answers |
| --- | --- |
| `carried.py` | context size at the first request (static prefix), carried tokens per tool / file / command, redundant re-reads, hard-failure classes |
| `commands.py` | wall-clock and failure sub-classes of build and test commands, the most expensive main and sub-agent sessions |
| `failures.py` | exact counts of the recurring failure classes (hook refusals, sleep polls, command not found, quoting errors) |
| `simulate.py` | what-if model: a smaller static prefix and an earlier compaction, output tokens per turn by model |

## Baseline (corpus 2026-09-19 .. 2026-10-15, taken 2026-10-15, before any change of that day)

Measured now, not an effect of any change. Re-measure **no sooner than 2026-10-29** over a window that
mostly postdates the change; a shorter window is the old corpus plus the days of the work itself.

| Figure | Value |
| --- | --- |
| Sessions / unique requests | 315 / 22,793 (172 main, 143 nested sub-agent sessions) |
| Cached input read | 6.69 B tokens; output 26.8 M tokens |
| First-request context (static prefix), p50 | 113k main, 98k sub-agent |
| Cache read by context band | 60% of it in requests whose context was above 300k |
| Compaction context at the boundary | p50 274k, p90 616k (17 compactions: 15 manual, 2 automatic) |
| Hard tool failures | 1,149 of 29,261 calls (3.9%); soft band 13.7% combined |
| Sub-agent runs on the session model | 51 of 143 on Opus; `general-purpose` carries no model pin |
| Redundant re-reads of an unchanged file | 805 re-reads, 6.7 M characters |
| Build and test wall-clock | about 400 min (`dotnet test` viewer suite 92 min, Companion 102 min, `Run-AllTests` 77 min, build 65 min, gates 47 min, go 39 min); 40% of runs red, compile errors a third of them |

Recurring failures (counts over the whole corpus): `python`/`dotnet`/`go` not on the Bash PATH 39,
hook refusals 78, sleep-poll refusals 33, nested-quote `unexpected EOF` 51, wrong project extension
`MSB1009` 13, `Remove-Item` guard 20, whole-file read refused 9.

What-if model (`simulate.py`, token-turns of cached context, the cost term that dominates the bill):

| Change | Token-turns saved |
| --- | --- |
| Static prefix -20k / -30k / -40k | 6.7% / 10.0% / 13.3% |
| Compact at 400k / 300k / 250k / 200k | 23.5% / 34.0% / 39.9% / 46.4% |
| Prefix -35k and compact at 300k together | 41.6% |

## What changed on 2026-10-15 (to be judged against the baseline, not assumed)

- `CLAUDE.md` split into a ~37 KB core plus eleven path-scoped rule files in `.claude/rules/` (the static prefix
  was ~158 KB of instructions in every request of every agent), then pruned of status text and duplicates.
- `tools/Invoke-Tests.ps1` (bounded digest, kept log) and `tools/Get-Spec.ps1` (ledger answers) for the inner loop.
- Project agents `implementer` and `auditor` (Sonnet, turn-capped) in `.claude/agents/`.
- `CLAUDE_CODE_AUTO_COMPACT_WINDOW=300000` in `.claude/settings.json`.
- Duplicate user-level hooks removed (the `sza` plugin registers the same guards).

## Counter-metrics to watch

A smaller prefix and a narrower read raise the chance of editing against partial context. Track alongside the saving:
the failed-edit rate (`EDIT-NO-MATCH` in `carried.py` output; baseline 32 hard failures of 5,044 `Edit` calls), the
number of re-reads of a file already in context, the first-request context size, and the count of sessions that
needed an explicit "read the rule file" because the path-scoped load did not fire.
