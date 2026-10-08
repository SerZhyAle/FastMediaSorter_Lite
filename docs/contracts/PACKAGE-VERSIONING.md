# Pointer - `PACKAGE-VERSIONING`

| | |
| --- | --- |
| **Id** | `PACKAGE-VERSIONING` |
| **Version** | 0.5, draft (read 2026-10-07; first read 2026-10-02 against 0.2). Owner: FastMediaSorter Android |
| **Home** | `package-versioning/README.md` in the shared contracts catalog |
| **Role here** | consumer, **Windows desktop profile** (W1-W5): every shippable package of this product - both viewer exes, the Share Manager, the installer, the Server installer, the portable ZIP, the winget manifests and the Store MSIX - carries one mechanical stamp. Registered rendering: `dotted-loose` (0.4), the historical scheme of this product |

## How this product's tag and channel versions relate to the contract

The stamp is `YY.M.D.HHmm` - year of two digits, month and day **not** zero-padded, `HHmm` the 24-hour clock padded
to four digits (`26.7.30.0108`, `26.10.2.1556`). The release tag is `v` + the stamp. This is the registered
`dotted-loose` rendering: `v<yy>.<M>.<D>.<HHmm>`.

- **The regex in the contract's table is the stamp's, not the tag's.** `^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$` is what
  `tools/Release.ps1:96` and `publishing/msix/build-msix.ps1:89` test (the stamp, "the release tag without its
  `v`"). The tag itself is tested with the `v`: `.github/workflows/release.yml:53` (`^v[0-9]{2}\.[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{4}\z`,
  case-sensitive) after `tags: ['v*']` at line 5, and made at `tools/Release.ps1:78` (`$tag = "v$Version"`). The
  table row's tag column (`v26.10.2.1556`-shaped) and its regex column therefore agree once the regex is read as the
  stamp's. A clarification for the registry row, not a deviation.
- **The instant.** P1 is one instant of the build machine's clock: `Release.ps1:68` defaults `-Version` to
  `Get-Date -Format "yy.M.d.HHmm"`; a plain build stamps the same format itself (`src/Modern/FastMediaSorter.Modern.vbproj:65-66`,
  `src/FastMediaSorter.vbproj:963-970`, the Companion project likewise), and `build.ps1` picks one instant for all
  three programs.
- **The pin (P4, W3).** The orchestrator passes the stamp whole to every invocation: `-p:ReleaseVersion=` to msbuild and
  both `dotnet publish` runs (`tools/Build-OfflineRelease.ps1:120,188,208`, `tools/Build-Installer.ps1:172,255,274`,
  `release.yml:105,113`), `/DVersion=` to Inno Setup (`Build-OfflineRelease.ps1:257,314`), `-ReleaseVersion` to the MSIX build.
- **setup.exe.** `AppVersion`, `VersionInfoVersion` and the output name are `{#Version}` = the stamp
  (`publishing/installer/FastMediaSorter.iss:57,79,108`). The numeric PE reading drops leading zeros (`26.7.30.0108`
  reads as `26.7.30.108`), which is the part-by-part decimal reading `UPDATE-MANIFEST` rule 6 prescribes.
- **winget.** `PackageVersion` is the stamp as text, the tag without the `v` (`publishing/winget/*.yaml`, both editions,
  `26.9.20.1206` today).
- **Store MSIX (W2).** The remap is `YY.(M*100+D).HHmm.0`, each part an integer (so `0108` is `108`), the revision
  forced to 0: `publishing/msix/build-msix.ps1:127-133`, mirrored for the check at `tools/Test-ArtifactVersion.ps1:70-73` (`Resolve-ExpectedMsixVersion`).
  Every part is far below 65535 (`M*100+D` at most 1231, `HHmm` at most 2359), and the remap orders releases the way
  the stamp does: year, then date, then time of day. A package of this product reads `26.920.1206.0`.
- **Proof (P5, W4).** `tools/Test-ArtifactVersion.ps1` reads the version resource of every exe and both setup.exe
  files, and (`-Msix`) the Identity `Version` of an MSIX, back out of the produced artifact (`BUILD-EVIDENCE` rule 2),
  comparing numeric parts; the release preflight runs it for the staged package and the setups, `build.ps1` for the exes
  it built and `build-msix.ps1` for the package it packed.

## What this repository must do to stay conformant

- Keep the stamp mechanical: nothing types a version; a build that reads its own clock is not shippable (W3).
- Keep the remap in one formula in two places that agree (`build-msix.ps1` and `Test-ArtifactVersion.ps1`), and keep
  `release.yml` and `Release.ps1` on the same grammar.
- The Android partitions (`Y.YM.MDDH.Hmm`, the 9-digit `versionCode`) do not reach Windows: the profile invents none.
- No edition releases on an independent clock (section 8, E1-E4): the Server edition and the User edition are cut from
  one tag and one pin, so rule 1's identical stamp holds.
