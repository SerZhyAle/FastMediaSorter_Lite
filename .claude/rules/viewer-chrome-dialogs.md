---
paths:
  - "src/Chrome/**"
  - "src/Main_Form.Chrome.vb"
  - "src/Main_Form.WindowBorder.vb"
  - "src/Main_Form.Notify.vb"
  - "src/DialogLayer*.vb"
  - "src/Dialog*.vb"
  - "src/Ask*.vb"
  - "src/NotifyToast.vb"
  - "src/UiTiming*.vb"
  - "src/Welcome/**"
---

# Viewer chrome and the dialog layer

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

- **The dialog layer** (ticket 124; modern-only, whole-file `#If Not NETFRAMEWORK`, every file listed in the net48 project). **Every window the viewer opens goes through `DialogLayer.ShowViewerDialog` / `ShowViewerWindow`** ([DialogLayer.vb](../../src/DialogLayer.vb); the viewer-side preparation - z-order band, owner monitor, reading direction, theme - is [DialogLayer.Viewer.vb](../../src/DialogLayer.Viewer.vb), installed from `Form1_Load`): a bare `ShowDialog(Me)` opens BEHIND a viewer pinned "on top" and a modal one then looks hung. `DialogLayerTests` reads the source and fails on one (native common dialogs and a show right after `PinToViewerBand` are the only exceptions).
  - A code-built window ends its builder with `DialogLayout.ApplyAutoScale(Me)` (96-dpi positions scaled by the DPI ratio; the viewer is PerMonitorV2, a "pinned to DpiUnaware" comment describes net48 only) and calls `DialogLayout.FitContent(Me)` from `OnLoad`, so a caption that wraps in a long language moves what is under it instead of being cut (`DialogReflow`, pure).
  - A window that REMEMBERS its rectangle (the editor, the thumbnail panel) applies the size only AFTER it has scaled itself - from `OnLoad` after `PerformAutoScale()`, and for the panel (built once, shown many times) also from `PrepareForDisplay` - and stores it in device pixels with the DPI it was measured at (`EditorDpi` / `PicturePanelDpi`; `WindowPlacement.SizeForDpi`, held by `WindowPlacementTests`). Restoring a device-pixel size BEFORE scaling multiplies it by the DPI again and the window grows every session; a size stored without a DPI is device pixels at the current DPI, so an upgrade resizes nothing.
  - **Questions** are [Ask_Dialog.vb](../../src/Ask_Dialog.vb) with a pure model ([AskModel.vb](../../src/AskModel.vb)) and call-site specs in [AskSpecs.vb](../../src/AskSpecs.vb): named buttons, a default that is never the destructive one (`AskModelTests`), a prompt that keeps what was typed and says why beside the field. **The system box is `ViewerBox.Show`** (right-to-left reading, an owner), never `MessageBox.Show` (a test fails on the stock call).
  - **News is not a question**: `Main_Form.Notify(text, NotifyLevel)` ([Main_Form.Notify.vb](../../src/Main_Form.Notify.vb)) replaces the leading `!` - every write to `lbl_Status` (the legacy `"! .."` ones included) is classified once by `NotifyRules.Classify`; in full screen without panels a warning or an error raises a toast ([NotifyToast.vb](../../src/NotifyToast.vb)) and an error plays the system sound. `SayProgress` is for "working on it" lines (information, not a refusal).
  - One accent source, `AccentColors` (Windows' own accent), for the chrome and the dark palette; the light palette stays the system colours (spec 060 rule 5).
  - Text style gates in `DialogGlueTests`: no em dash in a source string, no `Is_Russian_Language` read, no `"word: " & value` in the named feedback paths.
  - **Every window through `ShowViewerDialog` / `ShowViewerWindow` is timed when the process has `FMS_UI_TIMING=1`** ([UiTiming.vb](../../src/UiTiming.vb): one line per window in `current.log`, nothing sent; the `WINDOWS-UI` section 5 budget is [UiTimingBudget.vb](../../src/UiTimingBudget.vb)) - a new way to open a window goes through the layer and is timed for free. `tests/Integration/SettingsTimingScenes.ps1` (hand-run) prints the table.
- **[src/Chrome/](../../src/Chrome) + [Main_Form.Chrome.vb](../../src/Main_Form.Chrome.vb)** - the modern viewer's floating chrome: two ordered toolbar rows and a status strip on one `ChromeHost`. Legacy controls are hidden event/state carriers; invoke their existing handlers with `InvokeOnClick`.
  - `F10` deliberately enters keyboard mode, Escape restores viewer routing; mouse clicks never activate a host except deliberate native path editing. One timer polls/reveals/fades the chrome; zero alpha hides the window.
  - Placement is flush at the start edge (left; right under RTL) and the top/bottom of the media area (`ChromeMetrics.PanelEdgeInset`), never centred.
  - Each panel carries a corner pin (`ChromeSurface.PinItem`, laid out apart from the row so overflow never takes it): per-panel `ChromeRow1Pinned`/`ChromeRow2Pinned`/`ChromeStatusPinned`, with the older `ChromePinned` meaning "all three" (unpinning one while it is on splits it into the three flags).
  - The window-controls panel of the borderless window (spec 208) is a sixth surface on the same algebra: `chrome_WindowControls` is an explicit case there (never the status fallback), its pin `ChromeWindowControlsPinned` defaults True and persists, and Minimize, Maximize/Restore and Close are laid out `PinAtStart` in one PHYSICAL order whatever the language, at the upper-right corner the top rows must route around (`ChromeLayout.PlaceRowBesideCornerPanel` caps or wraps them; `ChromeSurface.MinimumRowWidth` is the floor).
  - `ChromeSize` (small/medium/large/adaptive, `UiScale.SizeMultiplier`) multiplies the monitor scale/`InterfaceScalePercent`; `DeviceDpi` supplies the automatic scale under PerMonitorV2. The `ModernChrome` setting is ignored by the viewer - do not read it; it stays only so old profiles round-trip.
  - On-screen scenes (hand-run): `tests/Integration/ModernChromeScenes.ps1`.
  - The modern main window itself is borderless by [Main_Form.WindowBorder.vb](../../src/Main_Form.WindowBorder.vb) (spec 188): `WM_NCCALCSIZE` insets the client by a real WIDENED non-client frame (`ChromeMetrics.WindowResizeBand`; the bare frame while maximized), so every edge and corner resizes natively - the media must never reach the frame, a child surface there would swallow the edge hit-test; `WM_NCHITTEST` reclassifies the top strip because WS_CAPTION would otherwise answer HTCAPTION; `WM_GETMINMAXINFO` floors the window at the panel footprint (`ChromeMetrics.WindowControlsLogicalWidth`); the panels' grip drag runs the native move loop (`ChromeBeginWindowDrag`, `WM_NCLBUTTONDOWN` `HTCAPTION`). Never reach for `FormBorderStyle.None` on this window: fullscreen keeps its existing None transition, and the x86 viewer keeps its caption.
