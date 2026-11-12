---
paths:
  - "build.ps1"
  - "publishing/**"
  - ".github/**"
  - "src/*.vbproj"
  - "src/Modern/*.vbproj"
  - "src/FastMediaSorterCompanion/*.vbproj"
  - "FastMediaSorter.sln"
  - "exchange-server/build.ps1"
  - "tools/Build-*.ps1"
  - "tools/Release.ps1"
  - "tools/Prepare-*.ps1"
  - "tools/Store-*.ps1"
  - "tools/Stage-*.ps1"
  - "tools/Test-ArtifactVersion.ps1"
  - "tools/Get-PinnedNuGet.ps1"
---

# Build, release and packaging details

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

## Between-release checks (L0282, 2026-10-29)

Canon `TESTING_AND_QA` section 2: between releases run only recorded checks of at most 5 seconds and a
direct check of the change; an unknown duration is release-only. The owner may explicitly request a
slower run. Record skipped build and test rungs as RELEASE items naming the commands and report "not built,
not tested until the release".

| Area or check | Command and measured process wall time | Date and source |
| --- | --- | --- |
| Engineering placement and documentation | `pwsh -NoProfile -File tools/Invoke-Tests.ps1 -Fast -Area engineering` - 2.445 s, exit 0 | 2026-10-29, direct timed call |
| Placement gate, direct | `pwsh -NoProfile -File tools/Test-CheckPlacement.ps1` - 0.782 s, exit 0 | 2026-10-29, direct timed call |
| Document registry, direct | `pwsh -NoProfile -File tools/Test-DocRegistry.ps1` - 1.322 s, exit 0 | 2026-10-29, bounded direct timed call |
| Audit campaign fixture | `pwsh -NoProfile -File tests/Integration/AuditCampaign.ps1` - about 8 s; release only | 2026-09-26, `tools/check-placement.jsonl` |
| Site render targets, write (a generator, not a check) | `pwsh -NoProfile -File tools/Build-SitePages.ps1` - 2.93 s, exit 0, with 15 one-source pages in three languages (2.6 s with three on 2026-11-08) | 2026-11-10, L0301 direct timed call |
| Site render targets, conformance | `pwsh -NoProfile -File tools/Build-SitePages.ps1 -Check` - 5.773 s; release only | 2026-11-07, L0207 direct timed call |
| Site Pages artifact, assemble and resolve every link | `pwsh -NoProfile -File tools/Build-SiteArtifact.ps1 -OutDir temp/<ticket>/site-artifact` - 5.17 s (4.9 s inside), exit 0, 2114 files; release only (3.76 s before the ru/uk sibling pages) | 2026-11-08, L0207 direct timed call |
| Window help pages, write (a generator, not a check) | `pwsh -NoProfile -File tools/Build-ContextHelp.ps1` - 1.1 s, exit 0, 702 pages | 2026-11-07, L0207 direct timed call |
| All unmeasured build, test and gate commands | Release only until measured | 2026-10-29, L0282 inventory |

The full release command is `pwsh -NoProfile -File tools/Release.ps1`: its preflight runs
`tools/Run-AllTests.ps1` and `tools/Build-OfflineRelease.ps1`. Other areas have no measured fast selector;
do not treat that absence as a passing test.

### Prerequisites
- **.NET 10 SDK** builds the mainline x64 viewer and the Companion (both via `dotnet publish`); the **.NET Framework 4.8** targeting pack builds the x86 fallback viewer; plus Visual Studio 2022 (or MSBuild CLI). A full build spans two toolchains (MSBuild/net48 + dotnet/net10) across **three programs**, and `build.ps1` orchestrates all of them. **`msbuild` alone does NOT produce the mainline exe** - the sln yields only the x86 viewer + Companion in `bin\Release`; the self-contained single-file x64 build comes only out of `dotnet publish` (which is why every packager runs its own).
- **Go (64-bit)**, the version pinned by `exchange-server/go.mod`, builds the exchange server (`exchange-server/`, spec 133, `fms-exchange-server.exe`) via `exchange-server/build.ps1`, which `build.ps1` calls. A 32-bit `go` can be first on `PATH`: the scripts resolve a 64-bit one and pin `GOARCH=amd64`. Without Go, `build.ps1` stops with an error unless `-SkipExchange` is given, so setups are never packaged without the server by accident; `Run-AllTests.ps1 -SkipExchangeGo` skips the Go suite and says so. `-race` runs inside `Run-AllTests.ps1` only with a MinGW-w64 gcc installed (`winget install BrechtSanders.WinLibs.POSIX.UCRT`), otherwise the row says `race not run`; the `GOOS=linux` WSL run is a hand step (`exchange-server/README.md`).
- **Shipping gate:** nothing from the exchange module is in any RELEASED installer, ZIP or package until rung 2 freezes the wire. Its packaging (ticket 141) is behind `-IncludeExchange` on the packagers / `INCLUDE_EXCHANGE`, both off; only the local `build.ps1` includes the server by default.
- NuGet: the net48 project restores via `packages.config` (vendored `packages/`), the SDK-style projects via `PackageReference`. Both viewers: `RootNamespace = fmsl`, startup object `fmsl.My.MyApplication`.
- **ILMerge never runs.** [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) only imports `ILMerge.props` (sets `ILMergeConsolePath`, read by nothing) and errors when the package is missing. The x86 build's managed dependencies are embedded as `FmsPayloadmanaged/*.dll` resources and resolved by [RuntimeBootstrap.vb](../../src/RuntimeBootstrap.vb) (see "Single-exe support infrastructure"). `Microsoft.Web.WebView2` is the same dead wiring - referenced by no code, yet restored, imported and presence-enforced (it drops `runtimes\win-x86\native\WebView2Loader.dll` into `bin\Release`). Both **stay, by owner decision**: do not remove them. The mainline uses single-file publish instead (no ILMerge, no managed payload resources).

### Version Auto-Generation
Both viewers stamp the same `YY.M.D.HHmm` version at build time: net48 via its `UpdateVersion` target (rewrites `My Project\VersionInfo.vb` before compilation), modern via its `AutoVersion` property. Both accept a release override (`-p:ReleaseVersion=..`); the two exes' stamps must stay identical. `build.ps1` picks ONE stamp up front and passes it to msbuild and both `dotnet publish` runs; every packager and `release.yml` pass it to the Companion publish too (same `AutoVersion`/`ReleaseVersion` block), so all three programs of one package report one version. That is **checked on the artifact, not the source** ([tools/Test-ArtifactVersion.ps1](../../tools/Test-ArtifactVersion.ps1), `BUILD-EVIDENCE` rule 2): `build.ps1` deploys nothing unless every exe it built carries its stamp, and the release preflight checks the staged package and both setup.exe files against the tag. The comparison is numeric - the resource holds `26.7.30.0108` as `26.7.30.108`.

### Release Pipeline (CI)
The workflow runs on **pushing a `vYY.M.D.HHmm` tag** (or manually with a tag input). It validates the tag against that pattern (the dispatch input goes through `env`, never spliced into a script), checks out **the tag itself** and proves `HEAD` is the tagged commit, so a manual re-run cannot build `main` under an old tag. What [.github/workflows/release.yml](../../.github/workflows/release.yml) does that is easy to break:
1. msbuild yields only the **x86** viewer, so the workflow then runs **`dotnet publish` of the modern project** (`-o modern-publish`, `-p:ReleaseVersion=` so both exes carry the tag's version) and of the Companion. Every packager runs its own publish: [tools/Build-Installer.ps1](../../tools/Build-Installer.ps1), [tools/Build-OfflineRelease.ps1](../../tools/Build-OfflineRelease.ps1), `release.yml`, [publishing/msix/build-msix.ps1](../../publishing/msix/build-msix.ps1).
2. The stage is `bin/Release` (minus `.pdb`/`.xml`) plus the published `FastMediaSorter_LITE.exe` next to its x86 sibling: **two exes, one set of support trees**.
3. [tools/Prepare-OcrOfflinePayload.ps1](../../tools/Prepare-OcrOfflinePayload.ps1) fetches the OCR packs from **pinned upstream commits**, every file checked against the committed [tools/tessdata.sha256](../../tools/tessdata.sha256) (a mismatch stops the run naming the file; moving to a newer commit means updating the script's SHAs and that manifest together). It **trims the 32-bit native trees by default** (~100 MB; the x86 viewer downloads them on demand via `OptionalRuntimeManager`) - `-KeepX86` keeps the x86 fallback fully offline. `x86\libwebpdecoder.dll` survives the trim (spec 270: the fallback's only WEBP decoder on Windows 7/8.1, never downloaded on demand); `tools/Test-ImageBackendArtifacts.ps1` fails a stage without it.
4. The **portable ZIP** and the **Inno Setup installer EXE** ([publishing/installer/FastMediaSorter.iss](../../publishing/installer/FastMediaSorter.iss)) are offline-ready. The installer ships **both** exes and points the shortcuts, the post-install launch and the file associations at the exe that **runs on that OS** (`UseModernExe`: Windows 10 build >= 14393 gets the mainline, older gets `FastMediaSorter_x86.exe`); `MinVersion=6.1` still lets Setup run on Windows 7.
5. The Server edition's `..-server-setup.exe` is built from its own lean stage; the GitHub Release attaches the ZIP, both setups and a `.sha256` sidecar for each.

Local release-style build: `.\tools\Build-OfflineRelease.ps1` mirrors the CI packaging and emits to `dist/`. It is also the free preflight `tools/Release.ps1` runs before the tag push, so it **fails without Inno Setup** instead of building a ZIP and skipping both installers. Local scripts restore with a **pinned** `nuget.exe` ([tools/Get-PinnedNuGet.ps1](../../tools/Get-PinnedNuGet.ps1): versioned URL + SHA-256; a copy that does not match is refused, never run).

### Standalone copy-anywhere installer ("сборка", not a release)
[tools/Build-Installer.ps1](../../tools/Build-Installer.ps1) - also the last step of `build.ps1` (called with `-SkipBuild`) - produces **only** the offline per-user `setup.exe` in `dist/` (no admin, no internet at install time), reusing the frozen `FastMediaSorter.iss` and `Prepare-OcrOfflinePayload.ps1`, and **never creates or pushes a `v*` tag**: a local convenience, not `tools/Release.ps1`. Unlike `Build-OfflineRelease.ps1` it skips the portable ZIP and, by default, `tessdata_best`. Flags: `-IncludeBest` (fully offline incl. best OCR), `-SkipOcr` (smallest; OCR downloads on demand), `-SkipBuild` (repackage the current `bin/Release`), `-Open` (reveal in Explorer). Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`); fails fast with that hint if missing.

The tag's version is authoritative for asset names; keep it consistent with the build-time stamp. Ship an EXE installer, not only a ZIP: a ZIP-only release is invisible to GitHub-Store (`SPECIFICATION_GITHUB_STORE.md`).

### Publishing to winget - read before touching the manifest
**The winget manifest must point at the Inno `setup.exe` directly (`InstallerType: inno`) with NO declared dependencies** (`SPECIFICATION_WINGET_PUBLISHING.md`). Rules:
- **Never** point winget at `single-exe.zip` - Defender ML flags the self-extracting bootstrap as `Program:Script/Wacapew.A!ml` (persistent false positive). It is a GitHub-only convenience download.
- **Don't** use the portable `windows-x64.zip` shape - the ~99 MB payload aborts the install with `0x80004004` after extraction.
- **Don't** declare the `Microsoft.VCRedist.2015+.x64` dependency - it sends winget into a resolution loop that aborts with `0x8A150044`; the app installs fine without it (validation never launches the app).
- **Don't** add `Scope: user` - it forces a non-elevated `--scope user` flow and the install fails with `0x8A150044` ("no suitable installer found for --scope user"). Omit `Scope` so validation runs elevated/machine.
- The "Missing `NestedInstallerType` / fewer `Tags`" inconsistency note is a non-blocking `Validation-Guide`, not a failure.
- Get the real failure reason from the build's `InstallationVerificationLogs` artifact, not the generic GitHub bot comments (the spec has a copy-paste snippet).
- **Before the winget PR** (and, with `-Wack` in an elevated console, before the Partner Center upload) run [tools/Store-Prepublish-Checks.ps1](../../tools/Store-Prepublish-Checks.ps1) `-Expect <version>`: `winget validate` of both editions, `InstallerSha256` against the published release's `.sha256` sidecars (never against `dist\`), and the manifests' consistency and frozen anchors (ticket `024`). It needs the release to exist, so it is a hand-run check, not part of the pre-tag preflight.

### Publishing to the Microsoft Store (MSIX) - see [STORE_PUBLISHING.md](../../docs/guides/STORE_PUBLISHING.md)
The Store path (MSIX) is **additive** - it does not change the GitHub release or winget flows. Playbook and filled listing copy: [STORE_PUBLISHING.md](../../docs/guides/STORE_PUBLISHING.md); packaging detail: [publishing/msix/README.md](../../publishing/msix/README.md). Key facts:
- **Keep the app MSIX-safe** (no code change is needed today): everything mutable goes to `%LOCALAPPDATA%\SZA\FastMediaSorter` (OCR via `OcrPaths`) or the registry; bundled `tessdata` is only read (the install dir is read-only under MSIX).
- **MSIX is x64 only and packages the mainline exe only** - the x86 fallback is never packaged (the Store floor is well above Windows 7).
- [publishing/msix/build-msix.ps1](../../publishing/msix/build-msix.ps1) stages the offline payload as the `bin/Release` tree minus `*.pdb`/`*.xml` and everything that exists only for the x86 exe (its exe and `.exe.config`, the loose net48 managed assemblies, the `x86\`, `libvlc\win-x86\` and `runtimes\win-x86\` trees). Pass `-ReleaseVersion <tag without v>` for a Store upload, or the package is stamped with the current minute. `-SelfSign` makes a sideload-testable build; **no `-SelfSign` for the Store** (Microsoft re-signs on certification - no paid cert needed).
- [publishing/msix/AppxManifest.xml](../../publishing/msix/AppxManifest.xml) wraps the exe as a **full-trust** desktop app (`rescap:runFullTrust` keeps LibVLC/GDI+/file-access/local-HTTP working) and declares the image **file associations**. Identity (`Name`/`Publisher`/`PublisherDisplayName`) is `__PLACEHOLDER__`, filled at pack time from [publishing/msix/identity.json](../../publishing/msix/identity.json) (the values of Partner Center, Product > Product identity; the same file records `StoreId`, `PackageFamilyName`, `IarcRatingId`; the script stops before it builds without it; `-IdentityName`/`-Publisher`/`-PublisherDisplayName` override one value) - must match **exactly** or the upload is rejected. The Store upload stays manual: an agent runs only the read-only `msstore apps list`, `submission status`, `submission get`; any `msstore` write needs the owner's words in chat naming the product and the package version (L0305 hard limits). `tools/Release.ps1` step 7 calls the canon's shared read-only Store preflight before the tag (`tools/lib/StoreChannel.ps1`, `WINDOWS-STORE` 0.4 section D rules 21, 23, 26): a pending submission, a package version not above the live one or a price other than `Free` stops the release; an unconfigured, expired or refused channel prints one "Store step is manual" line and the release goes on; one evidence line per tagged release is appended to `publishing/store/channel-evidence.jsonl`. Do not write another wrapper around `msstore`; `SZA_STORE_CREDENTIAL_EXPIRY` carries the owner's recorded credential expiry date, `SZA_STORE_PREFLIGHT` overrides where the shared script is looked up (tests).
- **Version remap (Store requires revision = 0):** the script maps the exe's `YY.M.D.HHmm` stamp to `YY.(M*100+D).HHmm.0` (each part <= 65535). Don't hand-edit it.
- Needs the Windows SDK (`winget install Microsoft.WindowsSDK`) for `makeappx.exe`/`signtool.exe`.

### Key dependencies (versions live in the .vbproj files)
- **LibVLCSharp** + **VideoLAN.LibVLC.Windows** - the video engine; the modern project takes the **x64 payload only** (`VlcWindowsX86Enabled=false`).
- Modern only: **Magick.NET-Q8-x64 15.0.0** (the whole Imaging seam: every decode and the editor's encoder, spec `270`; no OS codec, so WEBP works on Server editions and HEIC opens without the paid Store "HEVC Video Extensions"; its `Magick.Native-*.dll` publishes loose next to the exe like libvlc) and **SharpCompress** (ZIP/CBZ, [src/Archives/](../../src/Archives)), **Microsoft.Web.WebView2 1.0.4314.35** (the EPUB reader of spec `274` and the optional HTML page viewer of spec `275`; the WPF reference is stripped by the `RemoveUnusedWebViewWpfReference` target and the x64 `WebView2Loader.dll` is laid down in `webview2\win-x64\` - every host goes through `HtmlRuntimeService.PrepareLoader()`, because the loader folder can be set only once per process; the Edge WebView2 Runtime itself is never bundled, downloaded or installed) and **AngleSharp 1.8.5** (managed HTML parser behind the spec-275 sanitizer). The Share Manager references the same Magick.NET and bundles Magick.Native inside its single-file exe (`IncludeNativeLibrariesForSelfExtract`), so a Manager-only Server installation needs nothing of the viewer's payload. **Do not re-add** to the modern project: ImageSharp (any version - spec 270 removed it for its licence key; `tools/Test-ImageBackendArtifacts.ps1` fails on it in any artifact), QRCoder (unused in the viewer; the net48 exe only carries a vestigial copy), the WebView2 WPF assembly or a second copy of the loader outside `webview2\win-x64\`, the `System.*` shims and ProtectedData (all inbox on `net10.0-windows`; NU1510 flags them as redundant), ILMerge (single-file publish instead).
- x86/net48 only: **libwebp 1.6.0** `libwebpdecoder.dll` (BSD-3-Clause + PATENTS) is the WEBP decode fallback when WIC cannot handle a file. It is built from the pinned upstream tag by `tools/Build-LibWebpDecoder.ps1` (static CRT, imports checked against Windows 7's KERNEL32) into `payload/libwebp/` with a `PROVENANCE.txt`; the net48 project copies it to `x86\` (and `x64\` for the test leg). It decodes still WEBP only, so on Windows 7/8.1 (no WIC WebP codec) animated WEBP still does not open in the x86 viewer - the same known gap as before.
