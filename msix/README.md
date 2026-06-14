# MSIX package (Microsoft Store)

Packages the unpackaged `FastMediaSorter_LITE.exe` (plus its offline payload — LibVLC, Tesseract
`tessdata`, flag assets) as a **full-trust MSIX** for the Microsoft Store.

**Why the Store path:** the developer account is **free** (individuals since late 2025, companies
since May 2026), and **Microsoft re-signs the package** during certification — so you get a trusted
signature and reputation **without buying a code-signing certificate**. A Store-signed,
Store-distributed build is also the most effective answer to antivirus heuristic false positives.

## Files

| File | Purpose |
| --- | --- |
| [AppxManifest.xml](AppxManifest.xml) | Package manifest (full-trust app, `runFullTrust`, image file associations). Has `__PLACEHOLDERS__`. |
| [build-msix.ps1](build-msix.ps1) | Builds Release, stages the offline payload, generates logos, fills the manifest, packs the `.msix`. |

The `stage/` and `dist/` folders are produced by the script and are git-ignored.

## How the app adapts under MSIX

The **same `FastMediaSorter_LITE.exe`** ships packaged and unpackaged, with **no code change**:

- MSIX runs the app in a light container where the **install dir is read-only** and
  `%LOCALAPPDATA%` / `HKCU` are virtualized per-package. FastMediaSorter already writes everything
  mutable to safe locations — downloaded `tessdata`, the OCR cache, and language data go to
  `%LOCALAPPDATA%\SZA\FastMediaSorter` (see `src/Ocr/TesseractOcrEngine.vb` → `OcrPaths`), and
  settings go to the registry. Both are writable inside the container.
- Bundled `tessdata` ships next to the exe (read-only — only read, never written), so OCR works
  offline out of the box.
- `runFullTrust` keeps every Win32 API working: the IE WebBrowser (H.264) player, native LibVLC
  fallback, GDI+, user-level file access for sorting media across folders/`\\server` shares, and
  the optional local HTTP calls to an Ollama / LibreTranslate endpoint.
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
and Visual Studio 2022 / MSBuild for the Release build.

```powershell
.\build-msix.ps1 `
  -IdentityName         "<Package/Identity/Name from Partner Center>" `
  -Publisher            "<Package/Identity/Publisher from Partner Center>" `
  -PublisherDisplayName "<PublisherDisplayName from Partner Center>"
```

Output: `msix/dist/FastMediaSorter_LITE-<version>-x64.msix`, **unsigned** — that's correct, upload it
as-is. The package version is derived from the exe's `YY.M.D.HHmm` stamp and remapped to a
Store-legal `Major.Minor.Build.0` (`YY.(M*100+D).HHmm.0`; the revision must be 0 — the script does
this automatically).

By default only the smaller `tessdata_fast` packs are bundled (offline OCR works; `best` models
download on demand). Add `-IncludeBestOcr` to also bundle `tessdata_best`, or `-SkipOcrPayload` for
a quick build that downloads packs on first use.

Then in Partner Center: create a submission, upload the `.msix`, fill the listing (reuse the copy in
[../STORE_PUBLISHING.md](../STORE_PUBLISHING.md)), add a privacy-policy URL
([docs/privacy.html](../docs/privacy.html) on GitHub Pages), add a screenshot (`tools/store/make-screenshot.ps1`),
set the age rating, and submit. Certification typically takes a few business days.

## Test locally before submitting (self-signed)

To sideload and run the package on your own machine, sign it with a throwaway cert (its subject must
equal `-Publisher`, so keep the default or pass a matching `CN=`):

```powershell
.\build-msix.ps1 -SelfSign            # or add -NoBuild to reuse the current bin\Release build
```

The script signs the package and prints the two commands to (1) trust the test cert in
`LocalMachine\TrustedPeople` (run as admin) and (2) `Add-AppxPackage` the `.msix`. Note that
`Add-AppxPackage` **installs but does not launch** — start FastMediaSorter from the Start menu.

> Self-signed packages are for local testing only. **Do not** sign the package you upload to the
> Store — Microsoft signs that one.
>
> A path-independent single-instance mutex makes the packaged copy exit if a dev/Release copy is
> already running. Close other copies when testing the package.
