---
paths:
  - "src/FileManager/**"
  - "src/Image_Panel_Form.FileManager*.vb"
  - "src/Image_Panel_Form.Manager*.vb"
  - "src/Main_Form.Manager*.vb"
  - "src/Resources/IManagerDirectoryProvider.vb"
  - "src/Localization/Localization.Manager*.vb"
  - "src/Localization/Localization.FileManagerSettings.vb"
  - "tests/Lite.Tests/Manager*.vb"
  - "tests/Integration/FileManagerScenes.ps1"
  - "tests/Integration/ManagerExchangeLive.ps1"
---

# File manager mode of the Grid panel

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

The file manager is the **file manager mode of the F3 Grid panel** (`Image_Panel_Form`), **modern build only**; triage stays the default and every opening starts in it. `src/FileManager/` is a FOLDER, not the single file `src/FileManager.vb` ([viewer-mainform.md](viewer-mainform.md)). Guide: [FILE_MANAGER.md](../../docs/guides/FILE_MANAGER.md). Design: local specifications 256 (umbrella) and 257..262. Neighbours: [resources-secure.md](resources-secure.md) (providers, `TransferExecutor`), [imaging-icons-archives.md](imaging-icons-archives.md), [exchange-client.md](exchange-client.md), [localization.md](localization.md).

**Components - one owner per decision**
- **Place and location**: `ManagerPlace` is a browsable root with an id; `ManagerLocation` is a place plus ONE directory and never holds a cache path, a credential or a locator that carries one; `ManagerPathRules` (same file) is the pure path-family rules.
- **Listing and open**: `ManagerBrowser` (async, newest result only, owns the connection leases), `ManagerNavigation` (pure history, one door: `Apply`), `ManagerAddress` (pure), `ManagerProviderOpener`, `IManagerDirectoryProvider` (`src/Resources/`); `ManagerOpenPolicy` decides what a click does, `ManagerContentPolicy` what is listed.
- **Commands**: `ManagerCommandPlanner` (pure plan) -> `ManagerTreePlan` (manifest) -> `ManagerCommandRequest` (immutable facts, owns the endpoint leases) -> `ManagerOperation.Execute` (the ONLY caller of provider mutations). Also `ManagerArchiveJob`, `ManagerExchangePlaces`, `ManagerExchangeAccess`. Host: `Image_Panel_Form.FileManager*.vb`, `Image_Panel_Form.Manager*.vb`, `Main_Form.Manager*.vb`.

**Behaviour**
- **Paths**: disk, UNC and SMB compare case-insensitively; POSIX (`ResourceKind.Sftp`) is case-sensitive and a remote path is NEVER lowercased. A `..` segment or NUL refuses the path; `Contains` compares whole segments (`/MOV2` is not under `/MOV`). Up stops at a resource's configured root or a drive or share root.
- **A listing that did not succeed changes nothing**; a truncated listing is shown as incomplete and is never the manifest of a bulk command.
- **Typing an address never creates a resource**: a locator must name an existing configured one (Ok, Invalid, UnknownResource, AmbiguousResource). Places: drives plus resources of kind Sftp, Smb, Unc, never Drive.
- **The manager never prompts and never reuses the viewer's session**: `ManagerProviderOpener` connects through `ResourceConnect.ConnectAsync` on a worker, the trust prompt answers False, the PIN store never stores; a failure is a typed status (SignInNeeded, HostKeyUnknown, ..).
- **Browsing leaves the viewer untouched** (no `SnapshotFileList`, no `Choosen_Picture_From_Panel`); the one way back is a ViewerHandoff (`ManagerChoice`, then `Main_Form.Image_Panel_Form_FormClosed`). Names compare with `StringComparer.Ordinal` (triage: OrdinalIgnoreCase). Enter and leave are guarded by `manager_Epoch` and refused while `Main_Form.BatchIsRunning`; `LeaveManagerMode` is idempotent and `EndSession` calls it, so a window closed in manager mode reopens as triage.
- **Open order**: the deny set (`MandatoryDenyExtensions` + `ScriptDenyExtensions`, after `NormalizeExtensionForGuard`) -> `AssocIsDangerous` (a failing query refuses) -> supported media, including the `.zip .rar .7z .fd-sec` containers, = ViewerHandoff -> ExternalOpen. Symlinks are never entered or followed. The materialised file is rechecked every time; remote copies live in `%LOCALAPPDATA%\FastMediaSorter\manager_external_leases`, pruned to `RemoteCacheMaxMb`. The Media and Documents listings drop junk (`MEDIA-CLASSIFICATION` rule 5).
- **One capability projection**: `ManagerCommandPlanner.Allows` serves menus, admission and execution - never a second copy. `CurrentManagerPolicy` (host) strips capabilities for a PIN, unsupported protection and `ReadOnlyHint`. A new failure is a `ManagerFailure` value plus its `ReasonText`.
- **ONE door into the queue**: `Main_Form.StartManagerCommandAsync` (the shared queue, `BatchRunKind.Manager`, refused while a batch is busy): capture, plan, collision answer ("ask" is provisional and rebuilt), preflight, confirmation, `RevalidateManagerManifest`, start. Revalidate after every prompt. `ValidLeaf` refuses reserved Windows device names and a trailing dot or space; a case-only rename goes through `.<guid>.fms-rename`.
- **Mutation**: `ManagerOperation.Execute` re-checks the refusal policy, `ManagerTreePlan.ValidatePath` (every ancestor, no traversal through a link) and the entry version. Capture refuses links, the root, out-of-place paths, truncated listings and version drift. A transfer is `TransferExecutor.Execute(.., allowServerSideMove:=False)` after a scratch-space preflight. A remote delete to the recycle bin is refused.
- **Undo** is a conditional inverse (`ManagerUndoData`) on the viewer's undo stack; an archive job turns ordinary undo off. No manager confirmation setting exists: `NameCollisionPolicy`, `ConfirmDelete` and `DeleteToRecycleBin` are the viewer's preferences, passed unchanged - never remove the user's opt-out.
- **Archives**: extract one archive (`.zip .cbz .7z .rar .cbr`) or write a ZIP with `VerifiedZipWriter` (never `ZipFile.CreateFromDirectory`); `ProvePublication` reads the result back. Delete-originals is OFF by default, runs only after that proof, file by file, never recursively.
- **Exchange device places** need `ExchangeFeatures.IsEnabled()` AND `FileManagerShowDevicePlaces`. Discovery never attaches; attaching is explicit (`OpenManagerExchangeNode`: a Yes/No consent with default No; a PIN when locked). Menus read `ManagerExchangeAccess.CachedState` (never dials); `Validate` at the mutation boundary is a fresh read: no attach, no permission expansion, no mutation retry.
- **Keys** (`HandleManagerKey`, called first from the panel's key handler in `Image_Panel_Form.Modern.vb`): Alt+Left/Right/Up, Alt+D, Alt+Enter, F5, Esc, Ctrl+C/X/V/Z, F2, Ctrl+Shift+N, Del / Shift+Del (permanent), `0..9` move into a slot folder, Shift + a TOP-ROW digit copies.
- **Settings** (`ModernViewerPreferences`): `FileManagerViewMode`, `FileManagerContentMode`, `FileManagerShowHiddenFiles`, `FileManagerNonMediaOpenAction`, the three `FileManagerShow*Places`, `FileManagerRememberSort`, `FileManagerPlaceSorts` (JSON, personal data: out of a profile export unless asked for), `RemoteCacheMaxMb` 100..16384.

**Seams, membership, tests**
- Every file of the area is whole-file `#If Not NETFRAMEWORK`; the modern project takes them by its glob. Three lists are by hand: [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) (net48, compiles to nothing; the `src/FileManager` files, the host partials, `Localization.Manager*`), [Lite.Tests.vbproj](../../tests/Lite.Tests/Lite.Tests.vbproj) (the `src/FileManager` files one by one, NOT the host partials), and the Share Manager project (ONLY `ManagerLocation.vb` and `IManagerDirectoryProvider.vb`, so those two may reference only types the Share Manager also compiles).
- `ManagerBoundaryGuard.CheckSafeExpendableRoot` guards the live tests and has no production caller; the runtime boundary is `ManagerPathRules.Contains` + `IManagerDirectoryProvider.IsWithinRoot` + `ManagerTreePlan.ValidatePath`.
- Extend `ManagerFakeProvider` (`tests/Lite.Tests/ManagerCommandFixture.vb`), not a new fake. `ManagerArchitectureTests` is a source scan: no `File.Delete(` / `File.Move(` / `Directory.Delete(` in `Image_Panel_Form.FileManager*.vb`; no `Renci.SshNet` there or in `src/FileManager/*.vb`; `VerifiedZipWriter` required in `ManagerArchiveCommands.vb`; the deny list stays Blocked, `.fd-sec` stays ViewerHandoff.
- Opt-in live runs: `ManagerExchangeLiveTests` (needs `FMS261_RESOURCE`, `FMS261_ROOT`), `ManagerArchiveLiveTests`, `tests/Integration/ManagerExchangeLive.ps1`, `FileManagerScenes.ps1`. The one `Kind=UiForm` test: `tools/Test-UiForms.ps1 -Forms ManagerDestinationForm,ManagerArchiveForm`.
