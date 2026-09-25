# Repository Guidelines

## Project Structure & Module Organization

`src/` contains the Windows Forms applications and shared VB.NET code. `FastMediaSorter.vbproj` builds the legacy x86 .NET Framework 4.8 viewer; `Modern/FastMediaSorter.Modern.vbproj` builds the modern .NET 10 viewer; `FastMediaSorterCompanion/` is the .NET 10 Share Manager. Shared viewer sources use `#If NETFRAMEWORK` seams, so preserve both branches when changing shared behavior.

Tests live in `tests/Lite.Tests` (multi-targeted viewer logic), `tests/Companion.Tests` (Share Manager), and `tests/Integration` (optional live worker checks). Keep shipped art in `assets/`, release-channel material in `publishing/`, automation in `tools/`, and internal documentation in `docs/`. Root HTML and `assets/` back the GitHub Pages site; update localized pages in `lang/<locale>/` when altering published site copy.

## Build, Test, and Development Commands

Run from the repository root in PowerShell:

```powershell
.\tools\Run-AllTests.ps1 -SkipGo  # Run both .NET test suites
.\tools\Run-AllTests.ps1           # Also test the Go worker in ..\fms_companion (-WorkerRepo to override)
.\tools\Run-AllTests.ps1 -Integration # Optional read-only live worker IPC check
.\tools\Run-AllTests.ps1 -OcrScenes   # Optional OCR overlay scenes on the built viewer (on screen, slow)
.\build.ps1 -SkipInstaller          # Build the three Windows applications
```

Every check ends in one verdict line and exits 0 (pass), 1 (defect) or 2 (could not verify - never a pass); `-SkipGo` is reported as skipped. Which runner owns which check is `tools/check-placement.jsonl`.

`build.ps1` packages installers and deploys to configured local destinations unless skipped; read its parameters before a release build. For focused work, use `dotnet test tests\Lite.Tests\Lite.Tests.vbproj` or the Companion equivalent.

## Coding Style & Naming Conventions

Write VB.NET with `Option Explicit On`, `Option Strict On`, `Option Infer On`, and `Option Compare Binary`, matching project settings. Follow nearby formatting (four-space indentation) and existing PascalCase types/members. Keep WinForms partial features in descriptive files such as `Main_Form.FileOperations.vb`; place reusable logic in domain folders (`Ocr/`, `Imaging/`, `Secure/`). Do not replace the `NETFRAMEWORK` define or silently collapse platform-specific branches.

## Testing Guidelines

Use xUnit and name test files `*Tests.vb`; use focused, behavior-based test method names. Add unit coverage for pure logic and contract changes, especially serializer, localization, and platform-seam behavior. `Lite.Tests` compiles linked production files for both `net48` and `net10.0-windows`, so run the full suite after changing a linked source. There is no stated coverage threshold; passing relevant suites is required.

## Commits and Pull Requests

Recent commits use concise imperative prefixes such as `docs:`, `chore:`, `winget:`, or a component label (`OCR:`, `Share:`). Keep each commit scoped. PRs should describe user-visible and compatibility effects, link the issue/specification when available, list test commands run, and include screenshots for UI or website changes. Never commit secrets, signing material, or generated `bin/`, `dist/`, `stage/`, or `obj/` outputs.
