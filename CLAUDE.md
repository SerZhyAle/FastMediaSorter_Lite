# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FastMediaSorter LITE** is a Windows Forms application for viewing and managing image and video files. It runs on .NET Framework 4.8 and supports a broad range of media formats through native IE WebBrowser playback (H.264/MP4) with LibVLC fallback for unsupported codecs (AVI, ZMBV, VP9, MKV, etc.).

## Build & Run

### Prerequisites
- .NET Framework 4.8
- Visual Studio 2022 (or MSBuild CLI)
- NuGet packages auto-restore via FastMediaSorter.vbproj

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

## Architecture

### Core Components

**[Main_Form.vb](src/Main_Form.vb)** — Primary UI window
- Handles file browsing, slideshow, keyboard shortcuts, drag-drop
- Manages image/video display with perspective background effect (Ambilight-like)
- Tracks recent files and folders (~100 max)
- Keyboard-driven workflow with configurable hotkeys
- Multi-language support (English/Russian via `Is_Russian_Language` flag)
- ~3500 LOC; uses WinForms controls: PictureBox, WebBrowser (for videos), Label, Button, Timer

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
