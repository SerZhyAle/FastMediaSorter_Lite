# Technical Specification: `Main_Form.vb` Decomposition

> Status: **implemented** · Created 2026-06-04 · Branch `ui-modernization`
> Derived from [PLAN/Main_Form_Decomposition.md](Main_Form_Decomposition.md).
> Code anchors verified against `src/Main_Form.vb` as of 2026-06-04 (was 3,356 LOC).
>
> **Outcome:** all 10 partials extracted; `Main_Form.vb` reduced from 3,356 → **653 LOC**.
> Release build green with **0 errors and 0 new warnings** (the 3 pre-existing
> `Image_Panel_Form.vb` warnings are unchanged). Line-anchor numbers in the tables
> below reflect the *pre-split* file and are kept for historical traceability -
> locate members by name in the new partials.

---

## 1. Purpose & scope

### 1.1 Goal

Split the monolithic [src/Main_Form.vb](../../../src/Main_Form.vb) (**3,356 LOC**) into a set
of focused `Partial Class Main_Form` files, continuing the pattern already
established by the existing partials. The result must be **byte-for-byte equivalent
behavior** - this is a pure code-organization change, not a refactor.

### 1.2 What this is *not*

This is an **organizational** change, not an architectural one. Partial classes
share all fields, constants, and methods; moving code between them changes nothing
about coupling, lifetime, or threading. True decoupling (extracting non-UI logic
into standalone, testable services such as a `FileScanner` or a static
`FileAssociationManager`) is explicitly **out of scope** here and tracked as future
work in §9.

### 1.3 Success definition

After each extraction step:
- `msbuild FastMediaSorter.sln /p:Configuration=Release /p:Platform="Any CPU"`
  completes with **zero errors and zero new warnings**.
- No method body is altered - only relocated (and re-anchored at the top of the
  destination partial).
- `Main_Form.vb` shrinks toward a ~400–500 LOC "shell" (constants, field
  declarations, small click handlers, designer-bound glue).

---

## 2. Verified current state

### 2.1 Existing partials

`Main_Form` is already spread across these files (all `Partial Public Class
Main_Form`, all listed in the project with `<DependentUpon>Main_Form.vb</DependentUpon>`):

| File | LOC | Concern |
|---|---|---|
| [src/Main_Form.vb](../../../src/Main_Form.vb) | 3,356 | Core / shell (this spec drains it) |
| [src/Main_Form.Designer.vb](../../../src/Main_Form.Designer.vb) | 461 | Auto-generated - **never hand-edit** |
| [src/Main_Form.PerspectiveBackground.vb](../../../src/Main_Form.PerspectiveBackground.vb) | 431 | Ambilight background fill |
| [src/Main_Form.FileOperations.vb](../../../src/Main_Form.FileOperations.vb) | 356 | Copy/move/rename/delete wiring |
| [src/Main_Form.ModernLayout.vb](../../../src/Main_Form.ModernLayout.vb) | 318 | Modern toolbar layout/styling |
| [src/Main_Form.VideoPlayer.vb](../../../src/Main_Form.VideoPlayer.vb) | 302 | WebBrowser + LibVLC playback |
| [src/Main_Form.UILayout.vb](../../../src/Main_Form.UILayout.vb) | 131 | Responsive control sizing |

> Note: the plan predates `Main_Form.ModernLayout.vb`; it lists only four prior
> partials. The decomposition pattern is unchanged regardless.

### 2.2 Build configuration facts that govern this work

- **Option statements are project-wide**, set in [src/FastMediaSorter.vbproj](../../../src/FastMediaSorter.vbproj):
  `OptionExplicit=On`, `OptionStrict=On`, `OptionCompare=Binary`, `OptionInfer=On`.
  A per-file `Option Strict On` line is therefore redundant but is the **established
  convention** (the existing partials repeat it) - match it.
- **No glob auto-include.** Every source file has an explicit `<Compile Include>`
  entry. A new `.vb` file that is not registered **will not compile** (and worse,
  may silently appear to "work" in the editor while being absent from the build).
- **WarningsAsErrors** is active for a fixed set (41999, 42016–42022, 42032, 42036).
  A dangling reference or unreachable path created by a botched move **fails the
  build**, which is the safety net this whole effort relies on.

### 2.3 File header template for every new partial

Match [src/Main_Form.FileOperations.vb](../../../src/Main_Form.FileOperations.vb#L1-L8):

```vb
Option Strict On

Imports System.ComponentModel      ' include only what the moved code uses
Imports System.IO
' ... (per-file, minimal)

Partial Public Class Main_Form

    ' moved methods here

End Class
```

Imports are **per-file** in VB.NET; copy from `Main_Form.vb`'s import block
([src/Main_Form.vb:19-28](../../../src/Main_Form.vb#L19-L28)) only the namespaces the moved
methods actually reference. Unused imports are warnings, not errors, but keep them
tight.

### 2.4 Required `.vbproj` entry per new file

Insert into the `<ItemGroup>` that holds the other `Main_Form.*` partials
([src/FastMediaSorter.vbproj:138-164](../../../src/FastMediaSorter.vbproj#L138-L164)),
following the exact existing shape:

```xml
<Compile Include="Main_Form.FileAssociation.vb">
  <DependentUpon>Main_Form.vb</DependentUpon>
  <SubType>Form</SubType>
</Compile>
```

`<DependentUpon>` nests the file under `Main_Form.vb` in Solution Explorer;
`<SubType>Form</SubType>` matches the sibling entries. Both are cosmetic to the
compiler but required for consistency and IDE grouping.

---

## 3. Invariants (must hold after every step)

1. **No behavior change.** Method bodies are copied verbatim. No renames, no
   signature changes, no reordering of statements within a method.
2. **`Handles` clauses stay intact.** They bind by control-field name and work
   across partials - a moved `Private Sub Button6_Click(...) Handles btn_Slideshow.Click`
   keeps firing. Do not strip the `Handles` clause.
3. **Each field/const/type declared exactly once.** Moving a method that *uses* a
   field is fine (fields are shared); moving a *declaration* requires that no other
   partial re-declares it. See §8 collision list.
4. **Cross-partial references are legal and expected.** E.g. `AssociateAllImageFormatsWithThisApp`
   (moving to `FileAssociation`) calls `SHChangeNotify` (moving to `NativeMethods`).
   Because both remain `Main_Form` members, the call resolves regardless of file.
5. **Build green before proceeding.** Never stack two extractions before a Release
   build confirms the prior one.

---

## 4. Mechanical procedure (per extraction)

For each target file `Main_Form.<Concern>.vb`:

1. **Create** the file with the §2.3 header.
2. **Register** it in the `.vbproj` per §2.4.
3. **Cut** each listed method/type from `Main_Form.vb` (locate by **name**, not by
   the line numbers below - they drift as edits land). Take the full span from the
   declaration line through its matching `End Sub` / `End Function` / `End Structure`
   / `End Class`, **including the leading comment block and attributes** attached to
   that member.
4. **Paste** into the new partial, preserving original order where it aids reading.
5. **Resolve imports**: add any `Imports` the moved code needs; leave `Main_Form.vb`'s
   import block alone (removing a now-unused import is optional cleanup, not required).
6. **Build Release.** Fix dangling references (usually a missed helper or a moved
   declaration) until green.
7. **Commit** the single extraction with a focused message
   (e.g. `Extract Main_Form.FileAssociation.vb`).

> Cherry-pick warning: most target groups are **not contiguous** in the source.
> Methods of one concern are interleaved with unrelated handlers (see the per-file
> tables). Extract by method name; do not slice a line range blindly.

---

## 5. Tier 1 - Clean, self-contained extractions (do first)

### 5.1 `Main_Form.FileAssociation.vb`  ·  risk: **very low**

Touches only the registry, message boxes, and `Image_File_Extensions`. Zero UI-state
coupling. Methods form a near-contiguous block at the tail of the file.

| Method | Anchor (current) |
|---|---|
| `IsRunningAsAdministrator` | [Main_Form.vb:2920](../../../src/Main_Form.vb#L2920) |
| `IsJpgAssociatedWithThisApp` | [Main_Form.vb:2927](../../../src/Main_Form.vb#L2927) |
| `AssociateJpgWithThisApp` | [Main_Form.vb:2947](../../../src/Main_Form.vb#L2947) |
| `CheckAndOfferJpgAssociation` | [Main_Form.vb:2969](../../../src/Main_Form.vb#L2969) |
| `AreImageTypesAssociatedWithThisApp` | [Main_Form.vb:2978](../../../src/Main_Form.vb#L2978) |
| `IsExtensionAssociatedWithThisApp` | [Main_Form.vb:2984](../../../src/Main_Form.vb#L2984) |
| `AssociateImageTypesWithThisApp` | [Main_Form.vb:3004](../../../src/Main_Form.vb#L3004) |
| `AssociateExtensionWithThisApp` | [Main_Form.vb:3012](../../../src/Main_Form.vb#L3012) |
| `CheckAndOfferImageAssociations` | [Main_Form.vb:3031](../../../src/Main_Form.vb#L3031) |
| `AssociateAllImageFormatsWithThisApp` | [Main_Form.vb:3046](../../../src/Main_Form.vb#L3046) |

**Imports needed:** `Microsoft.Win32`, `System.Security.Principal`, `System.IO`.
**Cross-partial dependency:** `AssociateAllImageFormatsWithThisApp` calls
`SHChangeNotify` ([Main_Form.vb:3084](../../../src/Main_Form.vb#L3084)), which lives in the
NativeMethods group - fine post-split.

### 5.2 `Main_Form.NativeMethods.vb`  ·  risk: **very low**, but **scattered**

Pure P/Invoke + comparer declarations. ⚠️ This region is **interleaved** with
unrelated consts and a couple of small methods - cherry-pick the items below and
leave the rest in place.

| Item | Anchor | Kind |
|---|---|---|
| `ShowWindow` | [Main_Form.vb:221](../../../src/Main_Form.vb#L221) | `Declare`/`DllImport` shared fn |
| `SetForegroundWindow` | [Main_Form.vb:225](../../../src/Main_Form.vb#L225) | shared fn |
| `GetForegroundWindow` | [Main_Form.vb:229](../../../src/Main_Form.vb#L229) | shared fn |
| `COPYDATASTRUCT` | [Main_Form.vb:245](../../../src/Main_Form.vb#L245) | `Public Structure` |
| `SendMessage` | [Main_Form.vb:251](../../../src/Main_Form.vb#L251) | `<DllImport>` shared fn (takes `ByRef COPYDATASTRUCT`) |
| `MapViewOfFile` / `UnmapViewOfFile` / `CloseHandle` | [Main_Form.vb:255-257](../../../src/Main_Form.vb#L255-L257) | `Declare` fns |
| `StrCmpLogicalW` | [Main_Form.vb:265](../../../src/Main_Form.vb#L265) | shared fn |
| `SHChangeNotify` | [Main_Form.vb:269](../../../src/Main_Form.vb#L269) | shared sub |
| `NaturalFilenameComparer` | [Main_Form.vb:272](../../../src/Main_Form.vb#L272) | `Public Class` (uses `StrCmpLogicalW`) |

**Corrections vs. the plan:**
- The comparer is named **`NaturalFilenameComparer`**, not `NaturalStringComparer`.
- `SendMessage` **is** present at [Main_Form.vb:251](../../../src/Main_Form.vb#L251) (declared
  via `<DllImport("user32.dll", CharSet:=CharSet.Auto)>`, not `Declare`), so the
  plan is correct to list it. It takes a `ByRef COPYDATASTRUCT`, so it must travel
  with the struct.
- `ShowWindow`/`SetForegroundWindow`/`GetForegroundWindow` are **duplicated**: the
  `Main_Form` copies (private, lines 221–229) are distinct from the `Public`
  copies in [Common_Module.vb:22-30](../../../src/Common_Module.vb#L22-L30) used by
  `Application_Events`. Move **only** the `Main_Form` copies; leave `Common_Module`
  untouched. (Deduplicating these two sets is future cleanup, not this task.)

**Leave in place** (interleaved but unrelated): the consts at lines 204
(`WmCopyData`), 242 (`WM_COPYDATA`), 259–261 (`WM_USER`/`MY_CUSTOM_MESSAGE`/
`FILE_MAP_READ`), 262 (`minimum_time_before_next_media_file`), and the methods
`InitializeExtensionLists` (236) and `Image_Panel_Form_FormClosed` (281). These
belong to other concerns (Lifecycle / shell). *Optionally* the message-map consts
may travel with NativeMethods if it reads cleaner - decide during the move, but
keep each const declared exactly once.

### 5.3 `Main_Form.GifPlayback.vb`  ·  risk: **low** (contiguous)

| Method | Anchor |
|---|---|
| `StartGifLoopPlayback` | [Main_Form.vb:3205](../../../src/Main_Form.vb#L3205) |
| `StopGifLoopPlayback` | [Main_Form.vb:3242](../../../src/Main_Form.vb#L3242) |
| `Gif_Restart_Timer_Tick` *(`Handles gif_Restart_Timer.Tick`)* | [Main_Form.vb:3248](../../../src/Main_Form.vb#L3248) |

### 5.4 `Main_Form.Slideshow.vb`  ·  risk: **low**, **scattered**

⚠️ Interleaved with `Form1_ResizeEnd`, `Button5_Click`, `Label1_MouseClick`,
`StatusL_MouseClick` - extract by name only.

| Method | Anchor |
|---|---|
| `Button6_Click` *(`Handles btn_Slideshow.Click`)* | [Main_Form.vb:2763](../../../src/Main_Form.vb#L2763) |
| `SetSlideShow` | [Main_Form.vb:2768](../../../src/Main_Form.vb#L2768) |
| `SlideShow_Elapsed` *(`Handles SlideShowTimer.Tick`)* | [Main_Form.vb:2782](../../../src/Main_Form.vb#L2782) |
| `SlideShowStop` | [Main_Form.vb:2793](../../../src/Main_Form.vb#L2793) |
| `Button8_Click` *(`Handles btn_Next_Random.Click`)* | [Main_Form.vb:2823](../../../src/Main_Form.vb#L2823) |
| `Button9_Click` *(`Handles btn_Random_Slideshow.Click`)* | [Main_Form.vb:2829](../../../src/Main_Form.vb#L2829) |
| `SlideShowStart` | [Main_Form.vb:2833](../../../src/Main_Form.vb#L2833) |
| `SetRandomSlideShow` | [Main_Form.vb:2839](../../../src/Main_Form.vb#L2839) |

### 5.5 `Main_Form.Localization.vb`  ·  risk: **low**, two locations

| Method | Anchor |
|---|---|
| `LngCh` | [Main_Form.vb:2671](../../../src/Main_Form.vb#L2671) |
| `ButtonLNG_Click` *(`Handles btn_Language.Click`)* | [Main_Form.vb:2858](../../../src/Main_Form.vb#L2858) |

---

## 6. Tier 2 - Larger, cohesive groupings

### 6.1 `Main_Form.MediaLoading.vb`  ·  risk: **medium** - biggest single win

The central load/display pipeline: high-traffic but cohesive.

| Method | Anchor |
|---|---|
| `ReadShowMediaFile` | [Main_Form.vb:840](../../../src/Main_Form.vb#L840) |
| `UpdateFileIndexAndList` | [Main_Form.vb:917](../../../src/Main_Form.vb#L917) |
| `LoadFilesForRandomOrSlideshow` | [Main_Form.vb:1100](../../../src/Main_Form.vb#L1100) |
| `LoadFilesForExternalInput` | [Main_Form.vb:1164](../../../src/Main_Form.vb#L1164) |
| `LoadFiles` | [Main_Form.vb:1224](../../../src/Main_Form.vb#L1224) |
| `LoadStandardImageInPictureBox` | [Main_Form.vb:1265](../../../src/Main_Form.vb#L1265) |
| `UpdateCurrentFileAndDisplay` | [Main_Form.vb:1590](../../../src/Main_Form.vb#L1590) |

**Decision required - `UpdateControlVisibility`** ([Main_Form.vb:1406](../../../src/Main_Form.vb#L1406))
sits **between** `LoadStandardImageInPictureBox` and `UpdateCurrentFileAndDisplay`.
The plan omits it. It is tightly called by the display path; recommend moving it
**with** MediaLoading (it is display-state plumbing). If it turns out to be shared
broadly with the toolbar/shell, leave it in `Main_Form.vb`. Confirm call sites
before deciding.

### 6.2 `Main_Form.FileScanning.vb`  ·  risk: **medium**

Background folder enumeration + its data type.

| Item | Anchor | Kind |
|---|---|---|
| `BgWorker_DoWork` *(`Handles BgWorker.DoWork`)* | [Main_Form.vb:483](../../../src/Main_Form.vb#L483) | sub |
| `BgWorker_ProgressChanged` *(`Handles BgWorker.ProgressChanged`)* | [Main_Form.vb:599](../../../src/Main_Form.vb#L599) | sub |
| `BgWorker_RunWorkerCompleted` *(`Handles BgWorker.RunWorkerCompleted`)* | [Main_Form.vb:665](../../../src/Main_Form.vb#L665) | sub |
| `FileEntry` | [Main_Form.vb:1775](../../../src/Main_Form.vb#L1775) | `Private Structure` |
| `GetFiles` | [Main_Form.vb:1782](../../../src/Main_Form.vb#L1782) | function |

**Correction vs. the plan:** the helper type is a **`Structure FileEntry`**, not a
`FileInfo` class. (`FileInfo` is a BCL type; don't confuse the two.) Move the
structure with this group since `GetFiles`/`BgWorker_DoWork` are its only users.

### 6.3 `Main_Form.MouseInput.vb`  ·  risk: **low–medium**, scattered

| Method | Anchor |
|---|---|
| `HandlePictureBoxMouseDown` | [Main_Form.vb:2081](../../../src/Main_Form.vb#L2081) |
| `PictureBox1_MouseDown` *(`Handles Picture_Box_1.MouseDown`)* | [Main_Form.vb:2217](../../../src/Main_Form.vb#L2217) |
| `PictureBox2_MouseDown` *(`Handles Picture_Box_2.MouseDown`)* | [Main_Form.vb:2221](../../../src/Main_Form.vb#L2221) |
| `MouseUse` | [Main_Form.vb:2225](../../../src/Main_Form.vb#L2225) |
| `Form1_MouseDown` *(`Handles MyBase.MouseDown`)* | [Main_Form.vb:2651](../../../src/Main_Form.vb#L2651) |
| `Form1_MouseWheel` *(`Handles Me.MouseWheel`)* | [Main_Form.vb:2656](../../../src/Main_Form.vb#L2656) |
| `lbl_Zoom_MouseDown` *(`Handles lbl_Zoom.MouseDown`)* | [Main_Form.vb:3270](../../../src/Main_Form.vb#L3270) |
| `Picture_Box_1_MouseMove` *(`Handles Picture_Box_1.MouseMove`)* | [Main_Form.vb:3274](../../../src/Main_Form.vb#L3274) |
| `Picture_Box_2_MouseMove` *(`Handles Picture_Box_2.MouseMove`)* | [Main_Form.vb:3278](../../../src/Main_Form.vb#L3278) |
| `Pic_MouseMove` | [Main_Form.vb:3282](../../../src/Main_Form.vb#L3282) |

### 6.4 `Main_Form.KeyboardInput.vb`  ·  risk: **low–medium**, scattered

| Method | Anchor |
|---|---|
| `Form1_KeyDown` *(`Handles MyBase.KeyDown`)* | [Main_Form.vb:2457](../../../src/Main_Form.vb#L2457) |
| `GetWas_slideshow` | [Main_Form.vb:2462](../../../src/Main_Form.vb#L2462) |
| `KeybUse` | [Main_Form.vb:2466](../../../src/Main_Form.vb#L2466) |
| `DoKey` | [Main_Form.vb:2744](../../../src/Main_Form.vb#L2744) |
| `Picture_Box_1_KeyDown` *(`Handles Picture_Box_1.KeyDown`)* | [Main_Form.vb:2910](../../../src/Main_Form.vb#L2910) |
| `Picture_Box_2_KeyDown` *(`Handles Picture_Box_2.KeyDown`)* | [Main_Form.vb:2915](../../../src/Main_Form.vb#L2915) |

### 6.5 `Main_Form.Lifecycle.vb`  ·  risk: **medium**, scattered across whole file

Startup/shutdown, init, and cross-instance argument intake.

| Method | Anchor |
|---|---|
| `InitializeExtensionLists` | [Main_Form.vb:236](../../../src/Main_Form.vb#L236) |
| `InitializeTooltips` | [Main_Form.vb:288](../../../src/Main_Form.vb#L288) |
| `External_message` | [Main_Form.vb:395](../../../src/Main_Form.vb#L395) |
| `SetWebBrowserCompatibilityMode` | [Main_Form.vb:428](../../../src/Main_Form.vb#L428) |
| `InitNew` | [Main_Form.vb:444](../../../src/Main_Form.vb#L444) |
| `ProcessArgument` | [Main_Form.vb:737](../../../src/Main_Form.vb#L737) |
| `Form1_Load` *(`Handles MyBase.Load`)* | [Main_Form.vb:1894](../../../src/Main_Form.vb#L1894) |
| `Form1_FormClosing` *(`Handles MyBase.FormClosing`)* | [Main_Form.vb:2341](../../../src/Main_Form.vb#L2341) |

> `ProcessArgument` is the documented cross-instance entry point (`WM_COPYDATA`
> forwarding from `Application_Events`). It is `Public` and called externally -
> moving it between partials does not change its accessibility or signature, so the
> single-instance path keeps working. Verify after the move by launching a second
> instance with a file argument.

---

## 7. Expected residue & ordering

### 7.1 Residue in `Main_Form.vb`

After all extractions, `Main_Form.vb` should hold the natural shell - the class
header + `<ComVisible(True)>` attribute, the constant block
([Main_Form.vb:34-66](../../../src/Main_Form.vb#L34-L66)), field declarations, and the small
button/label click handlers not claimed by a concern above (e.g. `Button1_Click`,
`Button2_Click`, `Button3_Click`, `ButI_Click`, `ChkTopMost_CheckedChanged`,
`SortComboBox_SelectedIndexChanged`, `Choose_file`, `Jump_To_file_Number`,
`ShowImagePanelForm`, drag-drop handlers, etc.). Target: **~400–500 LOC**.

### 7.2 Recommended order (each step gated on a green Release build)

1. `Main_Form.FileAssociation.vb` + `Main_Form.NativeMethods.vb` - near-zero risk;
   proves the build/registration loop works.
2. `Main_Form.GifPlayback.vb`, `Main_Form.Slideshow.vb`, `Main_Form.Localization.vb` - quick wins.
3. `Main_Form.MediaLoading.vb` - biggest payoff, do once the loop is trusted.
4. `Main_Form.FileScanning.vb`, `Main_Form.MouseInput.vb`,
   `Main_Form.KeyboardInput.vb`, `Main_Form.Lifecycle.vb`.

One commit per file. Do not batch.

---

## 8. Name-collision watch list

Each of these must remain declared **exactly once** across all partials. When moving
a declaration (not just a method that uses it), confirm no sibling partial re-declares it:

- Type `COPYDATASTRUCT` (struct) - moves to NativeMethods.
- Type `NaturalFilenameComparer` (class) - moves to NativeMethods.
- Type `FileEntry` (struct) - moves to FileScanning.
- P/Invoke fns `ShowWindow`/`SetForegroundWindow`/`GetForegroundWindow` - the
  `Main_Form` copies move to NativeMethods; the **`Common_Module` copies are
  separate** and stay.
- Message-map consts (`WM_COPYDATA`, `WM_USER`, `MY_CUSTOM_MESSAGE`, `FILE_MAP_READ`,
  `WmCopyData`) - pick **one** home (shell or NativeMethods) and do not duplicate.

If a build error like *"'X' is already declared"* appears, a declaration was copied
instead of moved - delete the source copy.

---

## 9. Out of scope / future work

The following are deliberately **not** part of this decomposition (they change
behavior surface and require design + testing):

- Extracting non-UI logic into standalone classes (`FileScanner` service, static
  `FileAssociationManager`) for unit-testability.
- Deduplicating the `ShowWindow`/`SetForegroundWindow`/`GetForegroundWindow`
  P/Invoke pair shared between `Main_Form` and `Common_Module`.
- Any rename, signature change, or threading-model change.

---

## 10. Acceptance checklist (per file)

- [ ] New `Main_Form.<Concern>.vb` created with the §2.3 header.
- [ ] `<Compile Include>` added to `.vbproj` with `<DependentUpon>` + `<SubType>` (§2.4).
- [ ] All listed members moved verbatim (bodies unchanged), `Handles` clauses intact.
- [ ] Source copies deleted from `Main_Form.vb` (no duplicate declarations).
- [ ] `msbuild ... /p:Configuration=Release` → **0 errors, 0 new warnings**.
- [ ] Smoke test the moved concern (e.g. for Lifecycle: second-instance arg
      forwarding; for MediaLoading: open a folder and navigate; for Slideshow:
      start/stop). 
- [ ] Single focused commit.
