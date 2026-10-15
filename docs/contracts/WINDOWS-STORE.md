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
- the **listing copy**: `docs/guides/STORE_PUBLISHING.md` is the source (the search terms: `tools/site-facts.json`),
  `publishing/store/Build-ListingData.ps1` copies it into `publishing/store/listingData.csv` (two locales today, `en-us`
  and `ru`;
  `publishing/store/LOCALES.md` explains why the other eleven wait for Partner Center).

Out of the contract's scope by its section 5 and not read against it: the release order and the winget and GitHub
legs (canon), the certification kit procedure (canon), the version stamp (`PACKAGE-VERSIONING`), the warning text for
an unsigned installer (`INSTALL-TRUST`).

## What this repository must do to stay conformant

The rules are in the home page. Repo-local: which code holds each one, so a change can be routed. Line numbers are
those of 2026-10-07.

| Rule | Held by | Notes |
| --- | --- | --- |
| 1 identity supplied in full | `publishing/msix/build-msix.ps1:40-42` parameter defaults (`SZA.FastMediaSorterLITE`, the account Publisher `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD`, `SZA`) | read back out of the packed archive by `tools/Test-WindowsStoreConformance.ps1`, a step of the build (`build-msix.ps1:271-285`, after `makeappx`, before signing; a package that fails is deleted) |
| 2 unsigned upload | `-SelfSign` is the only signing path; the unsigned build is the default (`build-msix.ps1:267` packs, the read-back and then the signing block follow) | no `AppxSignature.p7x` in the packed archive, read by `Test-WindowsStoreConformance.ps1` in the build, before the signing block can add one |
| 3 remap of the stamp | `build-msix.ps1:127-133` (YY.M.D.HHmm -> Major.Minor.Build.0, revision 0, parts at most 65535), checked by `tools/Test-ArtifactVersion.ps1 -Msix` (`Resolve-ExpectedMsixVersion`, `:70-73`), which derives the remap again from the stamp and reads the Identity Version out of the archive | the relation to the tag and the other channels is in [PACKAGE-VERSIONING](PACKAGE-VERSIONING.md) |
| 4 notices inside the package | `build-msix.ps1:181` copies `THIRD-PARTY-NOTICES.txt` into the stage | presence in the packed archive is asserted by `Test-WindowsStoreConformance.ps1` in the build |
| 5 container behaviour | The x64 mainline owns the frozen `FastMediaSorter_LITE.exe` name; `publishing/msix/AppxManifest.xml` declares `windows.fileTypeAssociation` (images, and the `.fmsbcast` descriptor), `windows.startupTask` (`uap5:`), `desktop2:FirewallRules`; no `Square310x310Logo` | the manifest changed on 2026-10-07 (the `.fmsbcast` association) and was read again |
| 6-13 import file | `publishing/store/Build-ListingData.ps1` (fills Description, ReleaseNotes, Feature1..20 and SearchTerm1..7 of two columns from the guide and the worksheet; checks the caps of 1500 / 10000 / 1000 / 200 / 20 lines / 7 terms, each counted after CRLF normalisation (rule 9); writes UTF-8 without a BOM, CRLF as the only line break and no trailing newline, and refuses a BOM, a bare LF or CR and a trailing newline (rule 8); `-Check` compares the committed bytes) | rule 8 and rule 9 held since 2026-10-13. **Still not** the contract's builder in two things: it does not start from a fresh export (rule 6) and has no fill-nothing round trip on a committed emptied export (rule 13) - exception |
| 14-16 listing content | `docs/guides/STORE_PUBLISHING.md`, `tools/site-facts.json`, `publishing/store/listingData.csv` | rule 14 holds (`Title` is the frozen Store name, identical in both columns); rule 15: seven search terms for `en-us` and seven for `ru` (written, not researched against Store autocomplete; the gate `tools/Test-SitePositioning.ps1` holds both sets against the forbidden words and the competitor list); "What's new" is authored for `en-us` and `ru` only (rule 16) |
| 17 page says what it does | `AppxManifest.xml` declares `runFullTrust`; the privacy page, the permission list and the listing copy tell one story (`TrustPageContractTests`) | |
| 18 age rating | filed in Partner Center at the first submission | not readable from this repository: could not verify |

**Conformance evidence.** The contract has no vectors.

- **Rung 1** (round trip): **not built** - no committed emptied export and no fill-nothing switch.
- **Rung 2** (checks refuse): **held in the writer, negative run not committed** - the caps are checked after CRLF normalisation and the builder exits non-zero without writing (`ReleaseNotes ru` was 1504 against 1500 on its first run on 2026-10-13 and the deck was trimmed); the writer refuses a BOM, a bare LF or CR and a trailing newline in its output. There is no automated negative test of the writer yet.
- **Rung 3** (package read-back): **held and wired (2026-10-07, specification 183 section 1)** - `tools/Test-WindowsStoreConformance.ps1` asserts Identity `Name`, `Publisher`, `PublisherDisplayName`, `Version`, no `AppxSignature.p7x`, `THIRD-PARTY-NOTICES.txt`, the container declarations and `runFullTrust`, and ends in the shared verdict line (`Test-WindowsStoreConformance: PASS (11 checks held on ..)`, exit 0 / 1 / 2). `build-msix.ps1` runs it, and `tools/Test-ArtifactVersion.ps1 -Msix`, on the packed archive before it is signed; a package that fails either is deleted and the build stops. `tools/check-placement.jsonl` places it (class `build`). Run 2026-10-07 on the 26.920.1206.0 package: 11 checks held, exit 0; the wrong-name, wrong-version, missing-file, not-given and not-a-ZIP cases give exit 1 / 1 / 2 / 2 / 1. No new package was built for this evidence (the build was not run), so the `build-msix.ps1` step was driven against a copy of that package.
- **Rung 4** (real import): the product is on the Store; the import report of the last submission is not kept in this repository.

**Deviations** are recorded in the catalog's `_meta/REGISTRY.md`, never here.
