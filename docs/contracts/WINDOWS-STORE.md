# WINDOWS-STORE - pointer

| | |
| --- | --- |
| **Id** | `WINDOWS-STORE` |
| **Version** | 0.1 (draft; read 2026-10-03, re-read 2026-10-07 - the catalog's version has not moved) |
| **Home** | `windows-store/README.md` in the shared contracts catalog |
| **Owner** | StreamsPlayer |
| **This product's role** | **producer** of the MSIX package for Store upload and of the listing copy |

## What this product hands to the Store

- the **MSIX** built by `publishing/msix/build-msix.ps1` (unsigned for the upload, `-SelfSign` for a local test only);
- the **listing copy**: `docs/guides/STORE_PUBLISHING.md` is the source, `publishing/store/Build-ListingData.ps1` copies
  it into `publishing/store/listingData.csv` (two locales today, `en-us` and `ru`;
  `publishing/store/LOCALES.md` explains why the other eleven wait for Partner Center).

Out of the contract's scope by its section 5 and not read against it: the release order and the winget and GitHub
legs (canon), the certification kit procedure (canon), the version stamp (`PACKAGE-VERSIONING`), the warning text for
an unsigned installer (`INSTALL-TRUST`).

## What this repository must do to stay conformant

The rules are in the home page. Repo-local: which code holds each one, so a change can be routed. Line numbers are
those of 2026-10-07.

| Rule | Held by | Notes |
| --- | --- | --- |
| 1 identity supplied in full | `publishing/msix/build-msix.ps1:40-42` parameter defaults (`SZA.FastMediaSorterLITE`, the account Publisher `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD`, `SZA`) | read-back out of the packed archive is a hand-run script (`tools/Test-WindowsStoreConformance.ps1`), not a step of the build (exception) |
| 2 unsigned upload | `-SelfSign` is the only signing path; the unsigned build is the default (`build-msix.ps1:263-265` packs, the signing block follows) | no `AppxSignature.p7x` in the packed archive, read by `Test-WindowsStoreConformance.ps1` on `dist/FastMediaSorter_LITE-26.920.1206.0-x64.msix` |
| 3 remap of the stamp | `build-msix.ps1:127-133` (YY.M.D.HHmm -> Major.Minor.Build.0, revision 0, parts at most 65535), checked by `tools/Test-ArtifactVersion.ps1 -Msix` (`:69-76`) | the relation to the tag and the other channels is in [PACKAGE-VERSIONING](PACKAGE-VERSIONING.md) |
| 4 notices inside the package | `build-msix.ps1:178` copies `THIRD-PARTY-NOTICES.txt` into the stage | presence in the packed archive is read by `Test-WindowsStoreConformance.ps1`, not asserted by the build (exception) |
| 5 container behaviour | The x64 mainline owns the frozen `FastMediaSorter_LITE.exe` name; `publishing/msix/AppxManifest.xml` declares `windows.fileTypeAssociation` (images, and the `.fmsbcast` descriptor), `windows.startupTask` (`uap5:`), `desktop2:FirewallRules`; no `Square310x310Logo` | the manifest changed on 2026-10-07 (the `.fmsbcast` association) and was read again |
| 6-13 import file | `publishing/store/Build-ListingData.ps1` (fills Description, ReleaseNotes, Feature1..20 of two columns from the guide, checks the caps of 1500 / 10000 / 200 / 20 lines, `-Check` for drift) | **not** the contract's builder: it does not start from a fresh export, writes a UTF-8 BOM and leaves bare LF inside quoted cells (rule 8), has no fill-nothing round trip (rule 13) - exception |
| 14-16 listing content | `docs/guides/STORE_PUBLISHING.md`, `publishing/store/listingData.csv` | rule 14 holds (`Title` is the frozen Store name, identical in both columns); rule 15: seven search terms exist for `en-us`, none for `ru` (gap); "What's new" is authored for `en-us` and `ru` only (rule 16) |
| 17 page says what it does | `AppxManifest.xml` declares `runFullTrust`; the privacy page, the permission list and the listing copy tell one story (`TrustPageContractTests`) | |
| 18 age rating | filed in Partner Center at the first submission | not readable from this repository: could not verify |

**Conformance evidence.** The contract has no vectors.

- **Rung 1** (round trip): **not built** - no committed emptied export and no fill-nothing switch.
- **Rung 2** (checks refuse): **partly** - the caps are checked and the builder exits non-zero without writing; BOM and bare-LF refusal are not (the file is written with both).
- **Rung 3** (package read-back): **a script exists, unwired** - `tools/Test-WindowsStoreConformance.ps1` asserts Identity `Name`, `Publisher`, `PublisherDisplayName`, `Version`, no `AppxSignature.p7x`, `THIRD-PARTY-NOTICES.txt`, the container declarations and `runFullTrust`; run 2026-10-07 on the 26.920.1206.0 package: 11 PASS, 0 FAIL, exit 0. It reports no CHECK-VERDICT line, is in no runner, and the build does not call it.
- **Rung 4** (real import): the product is on the Store; the import report of the last submission is not kept in this repository.

**Deviations** are recorded in the catalog's `_meta/REGISTRY.md`, never here.
