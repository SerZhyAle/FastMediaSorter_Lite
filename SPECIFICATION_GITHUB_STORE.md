# Technical Specification: GitHub-Store Listing

## 1. Objective

Make `FastMediaSorter_Lite` automatically discoverable and installable through
[GitHub-Store](https://github.com/OpenHub-Store/GitHub-Store) — a cross-platform
app marketplace that indexes public GitHub repositories whose latest release
contains a recognized installable asset.

## 2. Research Findings

GitHub-Store has **no manual submission process and no manifest file**.
A repository surfaces automatically when all of the following are true:

| Requirement | Source | Current state |
|---|---|---|
| Public GitHub repository | GitHub Search API discovery | OK |
| Latest release contains at least one installable asset of a supported type | Asset extension filter | **FAIL** — only ships `.zip` |
| GitHub auto-generated source archives are **ignored** | Explicit in store README | n/a |
| Topics / language / description used for ranking | Store README | Topics likely empty |
| Stars influence Trending / Hot Release / Most Popular sections | Store README | Existing stars carry over |
| README (Markdown) is rendered on details screen | Store README | OK (README.md exists) |
| Release notes (Markdown) shown per release | Store README | Currently minimal body |
| Pre-release flag is honored | Store README | n/a |
| For auto-updates: signing fingerprint must match across releases | Store README (Android-focused; Windows behaviour undocumented) | App is not code-signed |

Supported Windows installer extensions according to the store:

- `.exe`
- `.msi`

Supported asset extensions on other platforms (informational, not in scope):
`.apk`, `.dmg`, `.pkg`, `.deb`, `.rpm`, `.AppImage`, `.pkg.tar.zst`.

### 2.1 Why the ZIP doesn't qualify

`release.yml` currently produces `FastMediaSorter-<version>-windows-x64.zip`.
GitHub-Store's asset filter only matches the extensions above, so a ZIP-only
release is invisible to the store **even though** the ZIP contains the EXE.
GitHub-Store also explicitly skips GitHub's auto-generated `Source code (zip/tar.gz)`
attachments.

### 2.2 EXE vs MSI tradeoff

| Format | Tooling | Pros | Cons |
|---|---|---|---|
| `.exe` (Inno Setup) | Inno Setup 6, free, CLI-buildable on `windows-latest` runners | Tiny installer, easy to script, no admin required for per-user install, good fit for a single-EXE .NET 4.8 app | Not a standard Windows package manager artifact |
| `.msi` (WiX Toolset) | WiX 3/4 / `wix` CLI | Native MSI, machine-wide install, group policy deploy, winget-friendly | More verbose authoring, requires admin |

Recommendation: **Inno Setup EXE** as the primary store asset
(simplest, single-file, matches a portable WinForms app). Optionally add an
MSI later for winget parity — the existing winget submission already uses the
ZIP, but a real MSI would simplify the manifest.

### 2.3 Discoverability inputs

The store surfaces results from GitHub Search; richer repo metadata = better
ranking. Recommended topics for this project:

```
windows, desktop, dotnet, vb-net, winforms, image-viewer, video-viewer,
media-player, file-manager, slideshow, photo-sorter
```

Repo "About" description should be concise and contain user-facing keywords
("Fast image and video sorter for Windows").

A social preview PNG (1280×640) drives the card thumbnail in the store grid.

## 3. Implementation Plan

### 3.1 Author an Inno Setup script

Create `installer/FastMediaSorter.iss`:

- `AppId`: stable GUID (generate once, never change).
- `AppName`: `FastMediaSorter LITE`.
- `AppPublisher`: `SerZhyAle`.
- `AppVersion`: injected from CI as `{#Version}`.
- `DefaultDirName`: `{autopf}\FastMediaSorter_LITE`.
- `DefaultGroupName`: `FastMediaSorter LITE`.
- `OutputBaseFilename`: `FastMediaSorter-<version>-windows-x64-setup`.
- `Compression`: `lzma2/ultra`.
- `SetupIconFile`: `assets\icons\Fast_Media_Sorter.ico`.
- `WizardStyle`: `modern`.
- `PrivilegesRequiredOverridesAllowed`: `dialog commandline`
  (so users can choose per-user vs machine-wide).
- `[Files]`: ship the contents of `bin\Release\` minus `*.pdb` and `*.xml`
  (mirrors today's stage step in `release.yml`).
- `[Icons]`: Start Menu shortcut + optional Desktop shortcut task.
- `[Run]`: post-install "Launch FastMediaSorter" checkbox.
- `[UninstallDelete]`: clean `%LocalAppData%\FastMediaSorter_LITE\*` on uninstall.

### 3.2 Extend `release.yml`

Add steps after the existing build, **before** the `softprops/action-gh-release` step:

1. Install Inno Setup on the runner:
   ```yaml
   - name: Install Inno Setup
     run: choco install innosetup --no-progress -y
   ```
2. Compile the installer using the staged tree as `SourceDir`:
   ```yaml
   - name: Build installer
     run: |
       $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
       & $iscc /DVersion=${{ steps.ver.outputs.version }} `
               /DSourceDir=${{ steps.stage.outputs.stage }} `
               /Odist installer/FastMediaSorter.iss
   ```
3. Hash the installer and append to release outputs:
   ```yaml
   $setup = "dist/FastMediaSorter-${{ steps.ver.outputs.version }}-windows-x64-setup.exe"
   $hash  = (Get-FileHash -Algorithm SHA256 $setup).Hash
   "$hash  $(Split-Path $setup -Leaf)" | Out-File "$setup.sha256" -Encoding ascii
   ```
4. Upload the new asset by extending the existing `files:` list:
   ```yaml
   files: |
     dist/${{ steps.pkg.outputs.name }}
     dist/${{ steps.pkg.outputs.name }}.sha256
     dist/FastMediaSorter-${{ steps.ver.outputs.version }}-windows-x64-setup.exe
     dist/FastMediaSorter-${{ steps.ver.outputs.version }}-windows-x64-setup.exe.sha256
   ```
5. Keep the existing ZIP — useful for portable users and the current winget manifest.

### 3.3 Set repo metadata (one-time, on GitHub.com)

- Add repo topics from §2.3.
- Set "About" description: `Fast image and video sorter for Windows (WinForms / .NET 4.8)`.
- Upload social preview image (1280×640 PNG, ≤1 MB) under
  Settings → Social preview.
- Confirm `LICENSE` is detected as MIT (it is, file exists).

### 3.4 Improve release notes

Update the `body:` block in `release.yml` to include a short Markdown changelog
hook (e.g. `## Highlights` + `## Full changelog: <compare-url>`).
GitHub-Store renders release-note Markdown verbatim, so structure matters.

### 3.5 (Optional) Code signing

Out of scope for first listing. SmartScreen warnings will still appear for
unsigned installers — note this in README if user friction matters. A future
EV cert would also unlock GitHub-Store's signing-fingerprint auto-update
path on Windows (behaviour undocumented but likely mirrors APK signing).

### 3.6 (Optional) MSI

If/when winget moves off the ZIP, add a WiX project producing
`FastMediaSorter-<version>-windows-x64.msi` and attach it alongside the EXE
installer.

## 4. Acceptance Criteria

1. A new tag `vYY.M.D.HHmm` produces a GitHub Release whose assets include
   `FastMediaSorter-<version>-windows-x64-setup.exe` (plus its `.sha256`),
   in addition to the existing ZIP.
2. The installer EXE, when run on a clean Windows 10/11 VM, installs to
   Program Files, creates Start Menu and optional Desktop shortcuts, runs
   the app, and uninstalls cleanly via "Apps & features".
3. Repo has ≥5 relevant topics, a non-empty "About" description, and a
   social preview image.
4. Within ~24 h of the release going live, the repo appears in
   GitHub-Store's Windows browse view when searching for `FastMediaSorter`
   or the topic `image-viewer`.
5. README, release notes, license, version, and installer asset all render
   correctly on the GitHub-Store details screen.

## 5. Open Questions

- Does GitHub-Store dedupe by `AppId` or by repo? Affects whether changing
  the Inno Setup `AppId` later is safe.
- Does GitHub-Store's Windows path do any signing-fingerprint check, or is
  that Android-only? Determines whether the unsigned EXE breaks auto-updates.
- Does the store pick the EXE over the ZIP when both are present, or list
  both? Determines whether keeping the ZIP creates a confusing duplicate
  install button.

These should be answered by submitting a test release and observing the
store's behaviour, then captured back into this document.
