# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FastMediaSorter LITE** is a Windows Forms application for viewing and managing image and video files. It runs on .NET Framework 4.8 and supports a broad range of media formats through native IE WebBrowser playback (H.264/MP4) with LibVLC fallback for unsupported codecs (AVI, ZMBV, VP9, MKV, etc.).

## Build & Run

### Prerequisites
- .NET Framework 4.8
- Visual Studio 2022 (or MSBuild CLI)
- NuGet packages auto-restore via [src/FastMediaSorter.vbproj](src/FastMediaSorter.vbproj) (the project file lives in `src/`; the solution `FastMediaSorter.sln` is at the repo root)
- Project identity: `RootNamespace = fmsl`, `AssemblyName = FastMediaSorter_LITE`, startup object `fmsl.My.MyApplication`
- ILMerge (3.0.41) is referenced to bundle dependencies into the single output exe

### Build Commands

Build debug:
```powershell
msbuild FastMediaSorter.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Build release:
```powershell
msbuild FastMediaSorter.sln /p:Configuration=Release /p:Platform="Any CPU"
```

Run the app (after building):
```powershell
.\bin\Release\FastMediaSorter_LITE.exe
```

Or open in Visual Studio and press F5 to debug.

### Version Auto-Generation
The build system automatically generates version numbers in format `YY.M.D.HHmm` (e.g., `26.6.2.0124`) at build time via the `UpdateVersion` target in the .vbproj file. This updates `My Project\VersionInfo.vb` before compilation.

### Release Pipeline (CI)
Releases are cut by **pushing a `vYY.M.D.HHmm` tag** (or running the `Release` workflow manually with a tag input). [.github/workflows/release.yml](.github/workflows/release.yml) runs on `windows-latest` and:
1. Restores NuGet, then `msbuild ... /t:Rebuild` in Release.
2. Stages everything in `bin/Release` **except `.pdb`/`.xml`** (plus `README.md`, `LICENSE`) into `stage/FastMediaSorter-<version>-windows-x64/`.
3. Packages two assets from that staged tree: a **portable ZIP** and an **Inno Setup installer EXE** (compiled via [installer/FastMediaSorter.iss](installer/FastMediaSorter.iss); Inno Setup installed on the runner with `choco install innosetup`). Each gets a `.sha256` sidecar.
4. Publishes a GitHub Release with all four files attached.

The version in the tag is authoritative for asset names but is independent of the build-time `UpdateVersion` stamp — keep them consistent. See [SPECIFICATION_GITHUB_STORE.md](SPECIFICATION_GITHUB_STORE.md) for the rationale behind shipping an EXE installer (GitHub-Store discoverability; a ZIP-only release is invisible to that marketplace).

## Architecture

### Core Components

**Main_Form** — Primary UI window, split across one `partial class` per concern (all `Partial Class Main_Form`; edit the relevant file, not just the main one):
- **[Main_Form.vb](src/Main_Form.vb)** (~3350 LOC) — core: file browsing, slideshow, keyboard shortcuts, drag-drop, recent files/folders (~100 max), English/Russian switching via `Is_Russian_Language`. Hosts the `COPYDATASTRUCT` declaration and `ProcessArgument()` entry point used for cross-instance arg forwarding (see Application_Events below).
- **[Main_Form.UILayout.vb](src/Main_Form.UILayout.vb)** — control sizing/positioning and responsive layout
- **[Main_Form.FileOperations.vb](src/Main_Form.FileOperations.vb)** — copy/move/rename/delete wiring from UI events to `FileManager`
- **[Main_Form.VideoPlayer.vb](src/Main_Form.VideoPlayer.vb)** — WebBrowser (H.264) + LibVLC fallback playback control
- **[Main_Form.PerspectiveBackground.vb](src/Main_Form.PerspectiveBackground.vb)** — Ambilight-like background fill (see below)
- Uses WinForms controls: PictureBox, WebBrowser (for videos), Label, Button, Timer

**[Application_Events.vb](src/Application_Events.vb)** — `My.MyApplication` startup hooks for single-instance behavior
- On `Startup`: a path-independent named mutex (`FastMediaSorterSingleInstanceMutex`) detects an already-running instance (VB's built-in `IsSingleInstance` only matches same exe path, so debug/release/renamed copies would each start their own). If found, the new process forwards its command-line file path to the running window via `WM_COPYDATA` and cancels (`e.Cancel = True`).
- On `StartupNextInstance`: the running instance receives the new args, calls `Main_Form.ProcessArgument()`, and deliberately avoids stealing focus (restores the previously-foreground window).

**[FileManager.vb](src/FileManager.vb)** — File I/O module
- `LoadImageWithStream()` — Load image + keep MemoryStream open (prevents file-lock issues)
- `RenameFile()`, `CopyFile()`, `MoveFile()`, `DeleteFile()` — Standard file operations
- GIF frame-count detection (catches corrupt GIFs early)

**[Common_Module.vb](src/Common_Module.vb)** — Global state & P/Invoke
- Public flags: `Is_Russian_Language`, `Is_Copying_not_Moving`, `Is_Pespective` (perspective background), `Is_No_Background_Tasks`
- Color scheme selector: `Form_Color_Scheme` (0=dynamic, 1=black, 2=white, 3=most-frequent)
- WinAPI calls: `ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow` (for single-instance enforcement)
- Hotkey storage: `Hardkeys_to_move_mediafile(10)` — Maps keyboard keys to folder shortcuts
- Current media state: `Current_File_Name`, `Current_Image_Path`

**[Image_Panel_Form.vb](src/Image_Panel_Form.vb)** — Quick-access thumbnail panel
- Small window of file thumbnails
- Double-click to load, drag-drop to the main window

**[Table_Form.vb](src/Table_Form.vb)** — File list table view
- Spreadsheet-like grid of files in current folder
- Columns: name, size, date (configurable visibility)

**[Utils.vb](src/Utils.vb)** — Helper functions (details TBD; inspect directly)

### Key Dependencies
- **LibVLCSharp** (3.9.3) — Video codec fallback (LibVLC binaries included in release)
- **WebView2** (1.0.3240) — HTML renderer (legacy; mostly unused)
- **System.Drawing** — Image rendering
- **System.Windows.Forms** — UI framework
- **VideoLAN.LibVLC.Windows** (3.0.21) — Native LibVLC binaries bundled at build time

### Perspective Background Effect
See [SPECIFICATION_BACKGROUND_EFFECT.md](SPECIFICATION_BACKGROUND_EFFECT.md) for the full algorithm. Summary:
- Fills "black bars" (pillarbox/letterbox) when image aspect ratio ≠ viewport aspect ratio
- Two modes:
  - **Uniform**: Edge is solid color → fill with average color
  - **Perspective**: Edge is complex → extend each edge row/column into the bar
- Triggered on image load/resize; runs async to avoid UI lag
- Constants in Main_Form.vb (e.g., `percent_of_color_deviation = 4`, `step_size_while_color_Search = 100`)

## Code Style & Constraints

**VB.NET Strict Mode** — All modules enforce:
```vb
Option Explicit On    ' All variables must be declared
Option Strict On      ' No implicit type conversions
Option Compare Binary ' Case-sensitive string comparison
Option Infer On       ' Allow type inference where possible
```

**File Organization**:
- `.vb` files are code-behind; `.Designer.vb` files are auto-generated (don't edit manually)
- Form classes inherit from `System.Windows.Forms.Form`
- Modules are for shared utilities and P/Invoke declarations
- `Main_Form` is one logical class spread across multiple `Partial Class Main_Form` files (`Main_Form.*.vb`). Fields/methods are shared across all of them — put a change in the file matching its concern, and watch for name collisions across partials.
- Build treats a fixed set of VB warnings as errors (`WarningsAsErrors` in the .vbproj: 41999, 42016–42022, 42032, 42036 — mostly implicit-conversion and unused/return-path warnings), so these will fail the build, not just warn.

**Keyboard Shortcuts** — Most features driven by hotkeys, not menus:
- Stored in app settings (persisted to registry/config)
- Hardcoded hotkey array in Common_Module for folder shortcuts
- Inspect Main_Form keyboard event handlers for the full mapping

**UI Threading**:
- Image/video loading happens on background threads to avoid UI freeze
- Use `Invoke()` or `BeginInvoke()` when updating UI controls from worker threads
- LibVLC video playback is asynchronous; use `MediaPlayer.Play()` callbacks

**File Locking**:
- `LoadImageWithStream()` keeps the MemoryStream open to allow Windows to release the file handle
- Disposed explicitly when image is no longer needed
- Critical for fast file operations in a media sorter

## Testing & Validation

No automated test suite. Validation is manual:
1. **Build**: `msbuild FastMediaSorter.sln /p:Configuration=Release` — confirms syntax
2. **Run**: Open the built `.exe` and exercise features: load folders, navigate files, test slideshow, verify video playback
3. **Code analysis**: Visual Studio's built-in analyzer (enabled in Debug config: `<RunCodeAnalysis>true</RunCodeAnalysis>`)

## Git & PR Workflow

- Single solution/project; no monorepo complexity
- VB.NET agent ([.github/agents/vb-net-profy.agent.md](.github/agents/vb-net-profy.agent.md)) available for focused VB.NET tasks
- Prefer small, targeted commits over broad rewrites
- Preserve existing UI behavior unless explicitly requested

## Common Development Tasks

### Adding a New File Operation
1. Add function to [FileManager.vb](src/FileManager.vb)
2. Call from Main_Form event handler (e.g., button click, keyboard shortcut)
3. Update UI state (enable/disable buttons, refresh file list)
4. Test with actual files in a temp folder

### Tuning the Perspective Background
1. Adjust constants in Main_Form.vb top (e.g., `percent_of_color_deviation`, `step_size_while_color_Search`)
2. Trigger a UI resize or image reload to see effect
3. Monitor `Debug.WriteLine()` output for timing data

### Supporting a New Media Format
1. Add extension to `Image_File_Extensions` or `video_File_Extensions` in Main_Form.vb
2. Test playback; if native player fails, LibVLC fallback handles it automatically
3. Verify no memory leaks (dispose Image/Bitmap, MemoryStream)

## Environment Notes

- Windows 7/10/11 support; developed primarily on Windows 11
- Executable built to `bin/Release/FastMediaSorter_LITE.exe`
- Settings/state persisted to Windows registry
- Single-instance enforced via app mutex (`app_Mutex_Name = "FastMediaSorterSingleInstanceMutex"`)
