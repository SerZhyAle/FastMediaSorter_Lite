# Pointer - `PACKAGE-VERSIONING`

| | |
| --- | --- |
| **Id** | `PACKAGE-VERSIONING` |
| **Version** | 0.2, draft (read 2026-10-02). Owner: FastMediaSorter Android |
| **Home** | `package-versioning/README.md` in the shared contracts catalog |
| **Role here** | declared consumer - every shippable package of this product (both viewer exes, the Share Manager, the installer and the portable ZIP) already carries one mechanical stamp, but the contract had no pointer or registry row for this product until 2026-10-02 |

## Where this product stands

- **The substance holds.** One mechanical, memoised stamp per build (`build.ps1` picks one instant up
  front and passes it to msbuild and both `dotnet publish` runs); the release orchestrator pins the
  override pair (`Release.ps1 -Version` -> `Build-OfflineRelease.ps1` -> `-p:ReleaseVersion`, and CI
  passes the tag's version); the artifact, not the source, is checked
  (`tools/Test-ArtifactVersion.ps1`, contract `BUILD-EVIDENCE` rule 2); all three programs of one package
  report one version; nothing ships with the sentinel.
- **The spelling is this product's own rendering.** The repo stamps `YY.M.D.HHmm` (e.g. `26.10.2.1556`,
  tag `v26.10.2.1556`), which is neither of the two renderings the contract's Windows table registers
  (`v<yyMMddHHmm>` compact, `v<yy>.<MMdd>.<HHmm>` dotted). It is a registered historical scheme of its
  own, and the Store channel's remap (`YY.(M*100+D).HHmm.0`) is the same stamp's MSIX spelling. Filed as
  `PROPOSAL-2026-10-02-fms-windows-version-rendering.md` beside the contract - a MINOR amendment asking
  for the third row, not a deviation to fix locally.
- The Android-specific partitions (`Y.YM.MDDH.Hmm` name, the 9-digit versionCode) do not reach Windows:
  the contract's own profile says none is invented for a package family that does not use them.
