# WINDOWS-STORE - pointer

| | |
| --- | --- |
| **Id** | `WINDOWS-STORE` |
| **Version** | 0.1 (draft) |
| **Home** | `Contracts/windows-store/README.md` |
| **Owner** | StreamsPlayer |
| **This product's role** | **producer** of the MSIX package for Store upload |

## What this product hands to the Store

- the **MSIX** built by `publishing/msix/build-msix.ps1` (unsigned for the upload, `-SelfSign` for a local test only);
- the **listing import file** will be built from a fresh Partner Center export by dedicated tooling (not yet implemented, see exceptions below).

Out of the contract's scope by its section 5 and not read against it: the release order and the winget and GitHub
legs (canon), the certification kit procedure (canon), the version stamp (`PACKAGE-VERSIONING`), the warning text for
an unsigned installer (`INSTALL-TRUST`).

## What this repository must do to stay conformant

The rules are in the home page. Repo-local: which code holds each one, so a change can be routed.

| Rule | Held by | Notes |
| --- | --- | --- |
| 1 identity supplied in full | `publishing/msix/build-msix.ps1` parameter defaults (`SZA.FastMediaSorterLITE`, the account Publisher `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD`, `SZA`); **read-back is not built yet** (exception) |
| 2 unsigned upload | `-SelfSign` is the only signing path; the unsigned build is the default; no AppxSignature.p7x in the packed archive (verified on `dist/FastMediaSorter_LITE-26.920.1206.0-x64.msix`) |
| 3 remap of the stamp | `publishing/msix/build-msix.ps1` lines 82-85: YY.M.D.HHmm -> Major.Minor.Build.0, revision forced to 0, parts at most 65535 |
| 4 notices inside the package | `THIRD-PARTY-NOTICES.txt` copied to the staging folder by `publishing/msix/build-msix.ps1` line 187; **assertion on the packed file is not built yet** (exception) |
| 5 container behaviour | The x64 mainline owns the frozen `FastMediaSorter_LITE.exe` name (rule 5 note); `windows.fileTypeAssociation`, `windows.startupTask` (uap5:), `desktop2:FirewallRules` declared in `AppxManifest.xml`; no `Square310x310Logo` without `Wide310x150Logo` |
| 6 fresh export | **Not implemented yet** - listing import file generation pending |
| 7 columns by name | **Not implemented yet** - listing import file generation pending |
| 8 encoding | **Not implemented yet** - listing import file generation pending |
| 9 caps checked | **Not implemented yet** - listing import file generation pending |
| 10 import per language | **Not implemented yet** - listing import file generation pending |
| 11 images referenced | **Not implemented yet** - listing import file generation pending |
| 12 copy source of truth | **Not implemented yet** - listing import file generation pending |
| 13 round trip | **Not implemented yet** - listing import file generation pending |
| 14 identifiers identical | **Not implemented yet** - listing import file generation pending |
| 15 search terms per language | **Not implemented yet** - listing import file generation pending |
| 16 "What's new" authored | **Not implemented yet** - listing import file generation pending |
| 17 page says what it does | `publishing/msix/AppxManifest.xml` declares `runFullTrust`; the privacy and permissions story is consistent with the full-trust desktop app model |
| 18 age rating | **Not implemented yet** - IARC questionnaire pending for Store submission |

**Conformance evidence.** The contract has no vectors.

- **Rung 1** (round trip): **Not built yet** - the committed export fixture and fill-nothing run pending listing tooling
- **Rung 2** (checks refuse): **Not built yet** - deck line over cap, BOM, bare LF refusal pending listing tooling  
- **Rung 3** (package read-back): **Partially built** - Identity Name/Publisher/Version/PublisherDisplayName filled and written to `AppxManifest.xml` in the package; no AppxSignature.p7x verified; **THIRD-PARTY-NOTICES.txt presence in the packed archive not asserted by build**
- **Rung 4** (real import): **Not yet** - Partner Center import not performed for this product

**Deviations** are recorded in the catalog's `_meta/REGISTRY.md`, never here.
