# Main_Form.vb Decomposition Plan

> Status: **proposed** · Created 2026-06-04 · Branch `ui-modernization`

## Goal

Continue splitting the monolithic `src/Main_Form.vb` (~3,355 LOC) into focused
`Partial Class Main_Form` files, following the pattern already established with
`Main_Form.UILayout.vb`, `Main_Form.FileOperations.vb`, `Main_Form.VideoPlayer.vb`,
and `Main_Form.PerspectiveBackground.vb`.

## Why this is low-risk

- VB.NET **partial classes share all fields and methods**, so extracting code is
  pure cut-and-paste - no signature changes, no state threading, no `Me`-passing.
- `Handles` clauses keep working across partial files.
- The build treats a fixed set of warnings as errors (`WarningsAsErrors` in the
  `.vbproj`), so any stray/dangling reference fails the build immediately rather
  than silently compiling.

## Caveat - organizational, not architectural

These splits make the code **navigable**, not **decoupled**. Every partial still
shares the same mutable form state through fields, so coupling is unchanged.
True decoupling (later, bigger lift) would extract non-UI logic into standalone
testable classes - best candidates: a `FileScanner` service and a static
`FileAssociationManager`.

---

## Tier 1 - Clean, self-contained extractions (do first)

| Proposed file | Methods | ~LOC | Lines | Risk |
|---|---|---|---|---|
| **Main_Form.FileAssociation.vb** | `IsRunningAsAdministrator`, `IsJpgAssociatedWithThisApp`, `AssociateJpgWithThisApp`, `CheckAndOfferJpgAssociation`, `AreImageTypesAssociatedWithThisApp`, `IsExtensionAssociatedWithThisApp`, `AssociateImageTypesWithThisApp`, `AssociateExtensionWithThisApp`, `CheckAndOfferImageAssociations`, `AssociateAllImageFormatsWithThisApp` | ~190 | 2919–3108 | **Very low** - touches only registry + message boxes + `Image_File_Extensions`. Zero UI coupling. |
| **Main_Form.NativeMethods.vb** | `ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow`, `SendMessage`, `StrCmpLogicalW`, `SHChangeNotify`, `NaturalStringComparer`, `COPYDATASTRUCT` struct | ~70 | 221–280 (scattered) | **Very low** - pure P/Invoke + comparer declarations. |
| **Main_Form.GifPlayback.vb** | `StartGifLoopPlayback`, `StopGifLoopPlayback`, `Gif_Restart_Timer_Tick` | ~65 | 3204–3267 | **Low** |
| **Main_Form.Slideshow.vb** | `Button6_Click`, `SetSlideShow`, `SlideShow_Elapsed`, `SlideShowStop`, `Button8_Click`, `Button9_Click`, `SlideShowStart`, `SetRandomSlideShow` | ~90 | 2762–2851 | **Low** |
| **Main_Form.Localization.vb** | `LngCh`, `ButtonLNG_Click` | ~60 | 2670–2724, 2857 | **Low** |

## Tier 2 - Larger, cohesive groupings (more impact, slightly more care)

| Proposed file | Methods | ~LOC | Lines | Risk |
|---|---|---|---|---|
| **Main_Form.MediaLoading.vb** | `ReadShowMediaFile`, `UpdateFileIndexAndList`, `LoadFilesForRandomOrSlideshow`, `LoadFilesForExternalInput`, `LoadFiles`, `LoadStandardImageInPictureBox`, `UpdateCurrentFileAndDisplay` | ~750 | 840–1404, 1608–1793 | **Medium** - the central pipeline, high-traffic but cohesive. Biggest single win. |
| **Main_Form.FileScanning.vb** | `BgWorker_DoWork`, `BgWorker_ProgressChanged`, `BgWorker_RunWorkerCompleted`, `GetFiles`, `FileInfo` class | ~320 | 483–735, 1793–1863 | **Medium** |
| **Main_Form.MouseInput.vb** | `HandlePictureBoxMouseDown`, `MouseUse`, `PictureBox1/2_MouseDown`, `Form1_MouseDown`, `Form1_MouseWheel`, `lbl_Zoom_MouseDown`, `Picture_Box_1/2_MouseMove`, `Pic_MouseMove` | ~280 | 2081–2339, 3269–3300 | **Low–med** |
| **Main_Form.KeyboardInput.vb** | `Form1_KeyDown`, `GetWas_slideshow`, `KeybUse`, `DoKey`, `Picture_Box_1/2_KeyDown` | ~200 | 2456–2649, 2909–2918 | **Low–med** |
| **Main_Form.Lifecycle.vb** | `InitNew`, `Form1_Load`, `Form1_FormClosing`, `InitializeTooltips`, `InitializeExtensionLists`, `SetWebBrowserCompatibilityMode`, `External_message`, `ProcessArgument` | ~430 | 236–481, 737, 1912–2068, 2340–2455 | **Medium** |

## Expected residue in `Main_Form.vb`

After all extractions, `Main_Form.vb` should hold the natural "shell" - constants,
field declarations, and the small button/label click handlers - roughly **400–500 LOC**.

---

## Suggested order

1. **Main_Form.FileAssociation.vb** + **Main_Form.NativeMethods.vb** - near-zero risk; proves the build stays green.
2. **Main_Form.GifPlayback.vb**, **Main_Form.Slideshow.vb**, **Main_Form.Localization.vb** - quick wins.
3. **Main_Form.MediaLoading.vb** - biggest payoff.
4. **Main_Form.FileScanning.vb**, **Main_Form.MouseInput.vb**, **Main_Form.KeyboardInput.vb**, **Main_Form.Lifecycle.vb**.

After each extraction: rebuild Release (`msbuild FastMediaSorter.sln /p:Configuration=Release`)
and confirm green before moving on.

## Process notes / gotchas

- Add each new `.vb` file to `src/FastMediaSorter.vbproj` (`<Compile Include="..." />`)
  if the project doesn't auto-include by glob.
- Each new file: same header `Option Strict On`, required `Imports`, and
  `Partial Class Main_Form ... End Class` wrapper.
- Watch for **name collisions** across partials (fields/consts defined once only).
- Line numbers above are a snapshot from 2026-06-04 and will drift as edits land -
  re-locate by method name, not by line.
