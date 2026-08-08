# MSIX package (Microsoft Store)

Packages the unpackaged `FastMediaSorter_LITE.exe` (plus its offline payload - LibVLC, Tesseract
`tessdata`, flag assets) as a **full-trust MSIX** for the Microsoft Store.

**x64 only, mainline only.** The viewer ships as two exes side by side (the .NET 10 x64 mainline
`FastMediaSorter_LITE.exe` and the net48 `FastMediaSorter_x86.exe` for Win7/8.1 and 32-bit Windows),
but the Store package carries **only the mainline**: the Store gates by architecture anyway, and the
x86 sibling exists precisely for machines that cannot get the package at all. `build-msix.ps1`
excludes `FastMediaSorter_x86.exe` from the stage.

**Why the Store path:** the developer account is **free** (individuals since late 2025, companies
since May 2026), and **Microsoft re-signs the package** during certification - so you get a trusted
signature and reputation **without buying a code-signing certificate**. A Store-signed,
Store-distributed build is also the most effective answer to antivirus heuristic false positives.

## Files

| File | Purpose |
| --- | --- |
| [AppxManifest.xml](AppxManifest.xml) | Package manifest: two full-trust `<Application>` nodes - the viewer (`FastMediaSorter_LITE.exe`) and the Share Manager (`FastMediaSorterCompanion.exe`) - plus `runFullTrust`, image file associations, a Companion `uap5:StartupTask` and the worker's firewall rule. Has `__PLACEHOLDERS__`. |
| [build-msix.ps1](build-msix.ps1) | Builds Release, **publishes the .NET 10 mainline viewer**, stages the offline payload, generates logos, fills the manifest, packs the `.msix`. |

The `stage/` and `dist/` folders are produced by the script and are git-ignored.

## How the app adapts under MSIX

The **same x64 mainline `FastMediaSorter_LITE.exe`** ships packaged and unpackaged, with **no code
change**:

- MSIX runs the app in a light container where the **install dir is read-only** and
  `%LOCALAPPDATA%` / `HKCU` are virtualized per-package. FastMediaSorter already writes everything
  mutable to safe locations - downloaded `tessdata`, the OCR cache, and language data go to
  `%LOCALAPPDATA%\SZA\FastMediaSorter` (see `src/Ocr/TesseractOcrEngine.vb` → `OcrPaths`), and
  settings go to the registry. Both are writable inside the container.
- Bundled `tessdata` ships next to the exe (read-only - only read, never written), so OCR works
  offline out of the box.
- `runFullTrust` keeps every Win32 API working: native LibVLC playback (the mainline's video engine -
  the IE WebBrowser player is a net48-only path, compiled out of this exe), GDI+, user-level file
  access for sorting media across folders/`\\server` shares, and the optional local HTTP calls to an
  Ollama / LibreTranslate endpoint.
- **File associations** are declared in the manifest (`windows.fileTypeAssociation`) instead of the
  per-user registry writes the Inno installer does, so the Store build can be set as a default
  image handler from *Settings ▸ Apps ▸ Default apps*.

## One-time setup in Partner Center

1. Sign in to [Partner Center](https://partner.microsoft.com/dashboard) with your developer account.
2. **Apps and games ▸ New product ▸ MSIX or PWA app** → reserve the name **FastMediaSorter LITE**
   (or another available variant).
3. Open **Product ▸ Product identity** and copy the three values Microsoft assigned:
   - **Package/Identity/Name** → pass as `-IdentityName`
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-…`) → pass as `-Publisher`
   - **Package/Properties/PublisherDisplayName** → pass as `-PublisherDisplayName`

These **must match exactly**, or the Store rejects the upload.

## Build a Store-ready package

Requires the Windows SDK (`makeappx` + `signtool`): `winget install Microsoft.WindowsSDK`,
Visual Studio 2022 / MSBuild for the Release build, **and the .NET 10 SDK** - the mainline viewer and
the Companion are `dotnet publish` outputs, not MSBuild ones.

```powershell
.\build-msix.ps1 `
  -ReleaseVersion       "<the release tag's YY.M.D.HHmm>" `
  -IdentityName         "<Package/Identity/Name from Partner Center>" `
  -Publisher            "<Package/Identity/Publisher from Partner Center>" `
  -PublisherDisplayName "<PublisherDisplayName from Partner Center>"
```

`-ReleaseVersion` pins the packaged exes to the version of the release this package belongs to.
Leave it out and both projects stamp the current minute instead, so a package built after the tag
carries a version no other artifact of that release has. Only a throwaway local build should omit it.

What the script packages (order matters - see [BUILD_AND_RELEASE.md](../../docs/guides/BUILD_AND_RELEASE.md)):

1. MSBuild Release, whose `bin\Release` tree supplies the **support payload** (LibVLC, `tessdata`,
   flags). Its `FastMediaSorter_x86.exe` output is filtered out of the stage.
2. `dotnet publish src\Modern\FastMediaSorter.Modern.vbproj -c Release -r win-x64` - this is the
   **only** step that produces the mainline `FastMediaSorter_LITE.exe` the manifest points at.
   MSBuild alone never yields it, so a `-NoBuild` run still publishes the viewer.
3. `dotnet publish` of the Companion, plus the committed Go SFTP worker into `companion\`.
4. `Prepare-OcrOfflinePayload.ps1` (trims the x86-only native trees), logos, manifest, `makeappx`.

Output: `publishing/msix/dist/FastMediaSorter_LITE-<version>-x64.msix`, **unsigned** - that's correct, upload it
as-is. The package version is read from **the published mainline exe** (not the MSBuild output): its
`YY.M.D.HHmm` stamp is remapped to a Store-legal `Major.Minor.Build.0` (`YY.(M*100+D).HHmm.0`; the
revision must be 0 - the script does this automatically).

By default only the smaller `tessdata_fast` packs are bundled (offline OCR works; `best` models
download on demand). Add `-IncludeBestOcr` to also bundle `tessdata_best`, or `-SkipOcrPayload` for
a quick build that downloads packs on first use.

Then in Partner Center: create a submission, upload the `.msix`, fill the listing (reuse the copy in
[STORE_PUBLISHING.md](../../docs/guides/STORE_PUBLISHING.md)), add a privacy-policy URL
([docs/privacy.html](../../docs/privacy.html) on GitHub Pages), add a screenshot (`publishing/store/make-screenshot.ps1`),
set the age rating, and submit. Certification typically takes a few business days.

## Test locally before submitting (self-signed)

To sideload and run the package on your own machine, sign it with a throwaway cert (its subject must
equal `-Publisher`, so keep the default or pass a matching `CN=`):

```powershell
.\build-msix.ps1 -SelfSign            # add -NoBuild to reuse the current bin\Release payload
```

`-NoBuild` skips only MSBuild (the support payload); the mainline viewer and the Companion are
published every run regardless, since nothing else produces them.

The script signs the package and prints the two commands to (1) trust the test cert in
`LocalMachine\TrustedPeople` (run as admin) and (2) `Add-AppxPackage` the `.msix`. Note that
`Add-AppxPackage` **installs but does not launch** - start FastMediaSorter from the Start menu.

> Self-signed packages are for local testing only. **Do not** sign the package you upload to the
> Store - Microsoft signs that one.
>
> A path-independent single-instance mutex makes the packaged copy exit if a dev/Release copy is
> already running. Close other copies when testing the package - including `FastMediaSorter_x86.exe`,
> which shares the same mutex (the two exes are one app).
