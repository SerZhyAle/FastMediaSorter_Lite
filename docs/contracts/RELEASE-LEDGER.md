# `RELEASE-LEDGER` - pointer

| | |
| --- | --- |
| **Id** | `RELEASE-LEDGER` |
| **Version** | `0.1`, draft (read 2026-11-18, ticket L0344) |
| **Home** | `release-ledger/README.md` in the shared contracts catalog |
| **Owner** | `sza-unified-rules` |
| **This product's role** | **producer** of the repository release ledger under `docs/releases/` (`channels.json`, `builds.json`, `history.jsonl`, rendered `RELEASES.md`), written through `tools/harness.ps1 releases/release-ledger` |

## Packages and channels declared (`docs/releases/channels.json`)

| Package | Unit | Channels |
| --- | --- | --- |
| `user` | User edition (`YY.M.D.HHmm`: setup EXE + portable ZIP) | `github` (`SerZhyAle/FastMediaSorter_Lite`), `winget` (`SerZhyAle.FastMediaSorter`), `installer` (`url` probe on the live site landing) |
| `user-msix` | Microsoft Store MSIX (`YY.(M*100+D).HHmm.0`, split from `user` because the Store remap does not match `YY.M.D.HHmm` under rule 7 and ships on its own cadence) | `msstore` (`9N59Z73WZ53C`) |
| `server` | Folder Share Server edition (`YY.M.D.HHmm`: server setup EXE) | `winget-server` (`SerZhyAle.FastMediaSorter.Server`), `installer-server` (`url` probe on `server.html`) |
| `exchange-server` | Media Exchange Server (`fms-exchange-server.exe`) | none declared while `INCLUDE_EXCHANGE` is `'false'` |

## What this repository must do to stay conformant

- **One writer:** every write to `docs/releases/` goes through `tools/harness.ps1 releases/release-ledger` (called by `tools/lib/ReleaseLedgerHook.ps1`). Never hand-edit `docs/releases/RELEASES.md` (rule 10).
- **Build hooks:** `build.ps1`, `tools/Build-Installer.ps1`, `tools/Build-ServerInstaller.ps1`, `tools/Build-OfflineRelease.ps1`, `publishing/msix/build-msix.ps1` and `exchange-server/build.ps1` call `Invoke-ReleaseLedgerBuild` as their last step, report warnings on failure and never fail a build.
- **Release hooks:** `tools/Release.ps1 -Push` records `kind: release` builds and `github` `submitted`; `tools/Set-ReleaseManifest.ps1` marks `github` `live`; `winget`, `winget-server`, `msstore`, `installer` and `installer-server` are recorded via `publish` / `probe` per `docs/guides/BUILD_AND_RELEASE.md`.
- **Privacy (rule 9):** no tokens, keys, seller/tenant/client ids, absolute local paths or machine/user names in `docs/releases/`.
