# Technical Specification: Main Window UI — Current State & Modernization

> Scope: the chrome of `Main_Form` (toolbar buttons, combos, labels, status).
> The media surface itself (`Picture_Box_1/2`, `Web_Browser`, LibVLC view) and the
> perspective-background effect are covered elsewhere
> ([SPECIFICATION_BACKGROUND_EFFECT.md](SPECIFICATION_BACKGROUND_EFFECT.md)).

---

## Part 1 — How the current UI is built

### 1.1 Controls are static, not generated

**No toolbar control is created at runtime.** Every button, combo, label and
checkbox is declared once in [src/Main_Form.Designer.vb](src/Main_Form.Designer.vb)
inside `InitializeComponent()` and added to `Me.Controls` there. There is **no
loop, factory, or data-driven button generation** anywhere. "How are the buttons
generated" therefore has a precise answer: *they aren't generated — they are
hand-placed Designer controls that get repositioned by code on every resize.*

The only runtime-created UI elements are:

| Element | Where created | Purpose |
|---|---|---|
| `ContextMenuStrip` of recent files | [Main_Form.vb:1891](src/Main_Form.vb#L1891) (`btn_RecentFiles_Click`) | One menu item per entry in `recent_Media_File_List`, reversed (newest first); item `.Tag` holds the path; disposed on close. |
| `vlc_Video_View` (LibVLC surface) | [Main_Form.VideoPlayer.vb](src/Main_Form.VideoPlayer.vb) | Video fallback surface, sized to track `Picture_Box_1`. |

### 1.2 The full control inventory (toolbar/chrome)

All declared in the Designer; `Friend WithEvents` fields at the bottom of the file.

| Control | Type | Caption (EN) | Role |
|---|---|---|---|
| `chkbox_Top_Most` | CheckBox | (none) | Always-on-top toggle |
| `cmbox_Sort` | ComboBox (DropDownList) | `abc/xyz/rnd/>size/<size/<time/>time/<0123/>3210` | Sort order |
| `lbl_Folder` | Label | `Folder:` | Static caption (click copies path) |
| `cmbox_Media_Folder` | ComboBox | — | Current folder path; type + Enter to navigate |
| `btn_choose_file` | Button | `file` | Open file picker |
| `btn_Select_Folder` | Button | `...` | Open folder picker |
| `btn_Review` | Button | `RE` | Reload current folder |
| `btn_Panel` | Button | `█` | Toggle image panel (F3) |
| `btn_Full_Screen` | Button | `^^` | Toggle fullscreen |
| `lbl_Slideshow_Time` | Label | `1s` | Slideshow interval (only when slideshow active) |
| `btn_Language` | Button | `RU`/`EN` | Toggle RU/EN |
| `lbl_Info` | LinkLabel | `ver` | Version + contact link |
| `lbl_Zoom` | Label | (empty) | Current zoom factor |
| `lbl_File_Number` | Label | `Files: 0` | Index / total |
| `btn_Prev_File` | Button | `<< (P)rev` | Previous file |
| `btn_Next_File` | Button | `(N)ext >>` | Next file |
| `btn_Next_Random` | Button | `(Y)Rnd>` | Random file |
| `btn_Random_Slideshow` | Button | `R>` | Random slideshow |
| `btn_Slideshow` | Button | `>>` | Slideshow |
| `btn_Move_Table` | Button | `dest folders table` | Open destination-folders table (F2) |
| `btn_Rename` | Button | `RN` | Rename (F6) |
| `bt_Delete` | Button | `(D)elete` | Delete (Del) |
| `btn_RecentFiles` | Button | `VVV` | Recent-files dropdown |
| `lbl_Current_File` | Label | `current file` | Current file name (click copies path) |
| `lbl_Status` | Label | `status` | Operation status |
| `lbl_Help_Info` | Label | (help text) | First-run overlay help (F1 toggles) |
| `Picture_Box_1/2`, `Web_Browser` | — | — | Media surface |

> Note: the **folder-shortcut keys 1–0** (`Hardkeys_to_move_mediafile`) are *not*
> buttons on the main window. They are keyboard shortcuts; their clickable
> equivalents live in `Table_Form`, not here.

### 1.3 The layout engine: manual pixel math, run on resize

There is no `TableLayoutPanel`/`FlowLayoutPanel`/`ToolStrip`. Layout is computed
imperatively in [src/Main_Form.UILayout.vb](src/Main_Form.UILayout.vb) by two
sibling methods, selected by mode:

```
Resize ──debounced 200 ms──▶ ISizeChanged()
                                  │
                if is_Full_Screen_Mode ─▶ Buttons_to_fullscreen()
                else ──────────────────▶ Buttons_to_normal()
                                  │
                          SkipZoom()  (resets Picture_Box geometry + zoom)
```

Key mechanics:

- **Resize debounce** — `Form1_Resize` restarts `ResizeDebounceTimer` (200 ms);
  only on tick does `ISizeChanged()` run. A `is_Programmatic_Resize` guard flag
  prevents the layout's own size changes from re-triggering the debounce
  ([Main_Form.UILayout.vb:63](src/Main_Form.UILayout.vb#L63)).
- **Chained absolute positioning** — every control sets
  `.Left = previousControl.Left + previousControl.Width + gap` and an explicit
  `.Top`. Widths/heights are integer multiples of two constants:
  - `the_Width_For_buttons = 15`, `the_Height_For_buttons = 20`
    ([Main_Form.vb:59-60](src/Main_Form.vb#L59)).
  - So `.Width = the_Width_For_buttons * 6` means 90 px, etc.
- **Lazy relayout** — `Buttons_to_normal()` only re-runs the positioning block if
  `Picture_Box_1` geometry actually changed (`If Not Picture_Box_1.Top = … OrElse …`),
  otherwise it just toggles visibility.

### 1.4 Order & conditions — normal (windowed) mode

`Buttons_to_normal()` lays out **three rows**, left to right, each control anchored
to the previous one:

**Row 1** (`Top = top_first_line = 0`):
`chkbox_Top_Most` → `cmbox_Sort` → `lbl_Folder` → *(`cmbox_Media_Folder` stays at
its Designer position 198,2 — it is **not** repositioned here)* → `btn_choose_file`
→ `btn_Select_Folder` → `btn_Review` (+10 gap) → `btn_Panel` (+10) →
`btn_Full_Screen` (+10) → `lbl_Slideshow_Time` (+10, **only if `Is_slide_show_mode`**)
→ `btn_Language` → `lbl_Info` → `lbl_Zoom`.

**Row 2** (`Top = cmbox_Sort.Bottom + 2`):
`lbl_File_Number` → `btn_Prev_File` (×6 wide) → `btn_Next_File` (×6) →
`btn_Next_Random` (×3) → `btn_Random_Slideshow` (+20, ×3) → `btn_Slideshow` (×3) →
`btn_Move_Table` (+20, ×7) → `btn_Rename` (+20, ×3) → `bt_Delete` (+20, ×4).

**Row 3** (`Top = btn_Prev_File.Bottom + 2`):
`btn_RecentFiles` → `lbl_Current_File`; then `lbl_Status` on the next line.

Below `lbl_Status`, `Picture_Box_1/2` (+ `Web_Browser` / VLC view, kept in sync)
fill the remaining client area.

### 1.5 Order & conditions — fullscreen & super-fullscreen

`Buttons_to_fullscreen()` has two sub-modes driven by `is_Super_Full_Screen_Mode`:

- **Super-fullscreen** (`is_Super_Full_Screen_Mode = True`): **every** chrome
  control's `.Visible = False`; `Picture_Box_1` is placed at `(0,0)` filling the
  whole screen (`Screen.FromControl(Me).Bounds`). Pure media, no UI.
- **Regular fullscreen**: a *reduced* toolbar is re-laid-out with a smaller font
  (`Arial 6pt` vs `Arial 7pt` in normal mode) and a different control subset/order:
  `chkbox_Top_Most` → `btn_choose_file` → `btn_Select_Folder` → `btn_Review` →
  `btn_Panel` → `btn_Prev_File` → `btn_Next_File` → `btn_Next_Random` →
  `btn_Random_Slideshow` → `btn_Slideshow` → `lbl_Zoom`, then `btn_Rename` and
  `bt_Delete` on a second line. `lbl_Folder`, `cmbox_Sort`, `cmbox_Media_Folder`,
  `btn_Move_Table` are hidden.

`FormBorderStyle`/`WindowState` are switched in `ISizeChanged()` only when
`is_Full_Screen_Mode` actually changed (`needs_Form_State_Change`).

### 1.6 State flags that govern the chrome

| Flag | Effect |
|---|---|
| `is_Full_Screen_Mode` | normal vs fullscreen layout method |
| `is_Super_Full_Screen_Mode` | hide all chrome, picture fills screen |
| `Is_slide_show_mode` | shows `lbl_Slideshow_Time` |
| `is_Programmatic_Resize` | suppresses debounce re-entry during layout |
| `is_form_shown` | gates `Draw_Perspective()` |

### 1.7 Localization

`LngCh()` ([Main_Form.vb:2670](src/Main_Form.vb#L2670)) swaps the `.Text` of a
**subset** of controls between Russian and English (`btn_Prev_File`,
`btn_Next_File`, `bt_Delete`, `btn_Move_Table`, `lbl_Folder`, `btn_Language`,
`lbl_Help_Info`). The symbolic buttons (`RE`, `^^`, `>>`, `R>`, `█`, `VVV`, `RN`,
`(Y)Rnd>`) are **never** localized and stay cryptic. Tooltips (the real
discoverability layer) are set once in `InitializeTooltips()`
([Main_Form.vb:288](src/Main_Form.vb#L288)) with full RU/EN strings.

### 1.8 Theming / color scheme

`Form_Color_Scheme` (0 = dynamic from the image's bottom row, 1 = black,
2 = white, 3 = most-frequent). When the background color changes, code loops over
`Me.Controls` and sets `ForeColor` (and `BackColor` for combos/checkbox) to the
"opposite" color ([Main_Form.vb:1573](src/Main_Form.vb#L1573)). Buttons keep
`UseVisualStyleBackColor`, so only their text color flips.

### 1.9 Pain points (the case for change)

1. **~240 lines of duplicated manual layout** across two methods that must be kept
   in sync; adding/moving one control means editing a chain of `.Left = prev…`.
2. **Magic-number geometry** (`the_Width_For_buttons * N`) with no relation to text
   width — captions can clip at different fonts/DPI.
3. **`cmbox_Media_Folder` is not repositioned** in `Buttons_to_normal()`; it relies
   on a hard-coded Designer position and can overlap neighbors at non-default DPI.
4. **Cryptic captions** (`RE`, `^^`, `R>`, `█`, `VVV`, `RN`) — discoverability
   depends entirely on tooltips; no icons.
5. **No overflow handling** — a narrow window pushes row-2 buttons off-screen
   instead of wrapping or showing an overflow chevron.
6. **DPI**: only `AutoScaleMode = Font`; absolute coordinates make Per-Monitor DPI
   fragile.
7. **Inconsistent fullscreen** — a third hand-maintained layout with its own font.

---

## Part 2 — Modernization options (no functionality lost)

The hard constraint is **.NET Framework 4.8 + WinForms** (per
[CLAUDE.md](CLAUDE.md)). Every option below keeps **all** existing controls,
event handlers, hotkeys, localization, theming, and the media surface intact —
only the *layout/presentation* changes. Naming the same `Friend WithEvents`
fields means every `Handles …` clause keeps working.

### Option A — Container-based responsive layout (lowest risk)
Wrap the toolbar in docked `TableLayoutPanel` / `FlowLayoutPanel` panels:
a top `FlowLayoutPanel` (row 1), a second `FlowLayoutPanel` (row 2), a
`StatusStrip`-like bottom, and the media surface `Dock = Fill` in the center.

- **Pros:** deletes most of `Buttons_to_*`; reflow + DPI scaling for free; keeps
  the exact same Button controls and handlers; small, mechanical diff.
- **Cons:** still plain buttons with cryptic captions; flow panels wrap rather
  than overflow-chevron.

### Option B — `ToolStrip` + `StatusStrip` (recommended)
Replace the two button rows with a docked **`ToolStrip`** (or two) and the status
labels with a **`StatusStrip`**. Each `Button` becomes a `ToolStripButton` that
calls the *same* handler; combos become `ToolStripComboBox`.

- **Pros:** native modern look; **automatic overflow chevron** on narrow windows;
  built-in icon+text+tooltip per item; consistent theming; renderer is
  swappable (flat/system/custom) — a single place to restyle everything;
  `StatusStrip` gives a proper bottom status bar with `lbl_Status`,
  `lbl_File_Number`, `lbl_Zoom` as spring/auto items.
- **Cons:** handlers must be repointed from `Button.Click` to
  `ToolStripButton.Click` (mechanical); fullscreen toolbar logic is rewritten to
  just toggle `ToolStrip.Visible` instead of repositioning.
- **Glyph upgrade:** use **Segoe Fluent Icons / Segoe MDL2 Assets** glyphs (or a
  small embedded icon set) so `RE`→⟳, `^^`→⛶, `>>`→▶, `█`→▦, `VVV`→🕘, etc.,
  with the existing tooltips retained.

### Option C — Visual refresh on top of A/B (cosmetic)
Layer modern cosmetics without structural change:
- Dark title bar via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`.
- A custom `ToolStripRenderer` / `FlatStyle.Flat` palette tied to the existing
  `Form_Color_Scheme` so theming stays one code path.
- Per-Monitor-V2 DPI awareness via `app.manifest` (`<dpiAwareness>PerMonitorV2`).

### Option D — Framework migration (largest, optional, future)
Port to **.NET 8 WinForms** (modern HighDPI PerMonitorV2, official dark-mode
preview, faster runtime) or to **WPF/WinUI** for a fully fluent UI. High effort,
touches build/ILMerge/release pipeline; out of scope for a "no behavior change"
pass but the natural long-term destination.

### Recommendation
**B + C on .NET Framework 4.8**, done in phases so each step is shippable and
behavior-verifiable:

1. **Phase 1 — containers (Option A).** Move existing buttons into docked
   `FlowLayoutPanel`s; delete the pixel math in `Buttons_to_*`. Verify every
   feature still works (checklist below). *Pure refactor, no UX change.*
2. **Phase 2 — ToolStrip/StatusStrip (Option B).** Swap panels for a `ToolStrip`
   + `StatusStrip`; repoint handlers; fullscreen = `ToolStrip.Visible` toggle.
3. **Phase 3 — glyphs + theming + DPI (Option C).** Icon font captions (tooltips
   kept), `Form_Color_Scheme`-driven renderer, dark title bar, Per-Monitor-V2
   manifest.
4. **Phase 4 (optional, later) — .NET 8 WinForms** migration (Option D).

### Functionality-preservation checklist (must all still pass after each phase)
- [ ] All hotkeys (P/N/Y/S/I/R/T/Del/F1/F2/F3/F5/F6, 1–0 folder moves, U undo).
- [ ] Prev/Next/Random/Slideshow/Random-slideshow navigation.
- [ ] Sort combo (`cmbox_Sort`), folder combo type-to-navigate (`cmbox_Media_Folder`).
- [ ] File ops: choose file, select folder, reload (RE), rename (RN), delete.
- [ ] Recent-files dropdown (`btn_RecentFiles`) and recent folders.
- [ ] Image panel (F3), destination-folders table (F2).
- [ ] Fullscreen + super-fullscreen toggles; always-on-top checkbox.
- [ ] Zoom/rotate display, status & file-number labels, slideshow interval label.
- [ ] RU/EN switching (`LngCh`) and all tooltips.
- [ ] Color schemes 0–3 (dynamic/black/white/most-frequent) still recolor chrome.
- [ ] Drag-drop of files, click-to-copy on `lbl_Folder` / `lbl_Current_File`.
- [ ] Media surface (`Picture_Box_1/2`, `Web_Browser`, VLC) tracks layout exactly.
