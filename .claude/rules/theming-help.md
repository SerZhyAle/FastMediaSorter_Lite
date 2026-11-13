---
paths:
  - "src/Theming/**"
  - "src/Main_Form.Theme.vb"
  - "src/Help/**"
  - "help-map.json"
  - "tools/Build-ContextHelp.ps1"
  - "tools/Test-ContextHelp.ps1"
  - "tools/Capture-ContextHelp.ps1"
  - "tools/settings-help-copy.json"
  - "docs/help/**"
  - "tests/Lite.Tests/Theme*.vb"
  - "tests/Lite.Tests/ContextHelp*.vb"
  - "tests/Lite.Tests/RecipientPaletteTests.vb"
  - "tests/Companion.Tests/Theme*.vb"
---

# Application theme and context help

> Area rules for the paths above (auto-loaded). Index and the always-on rules: CLAUDE.md.

Both halves are mainline only and shared with the Share Manager. The viewer's floating panels (`Chrome/ChromeTheme`, `PanelColourModel`) are a different colour system: [viewer-chrome-dialogs.md](viewer-chrome-dialogs.md). The Settings window's own palette: [settings-window.md](settings-window.md).

**Application theme** (local specification 060; `APP-STYLE`)
- **Mode and switch.** Three modes (System, Light, Dark), one value `UiTheme` in the shared `SZA\FastMediaSorter` hive, read by the viewer and the Share Manager alike. The switch is live, no restart: System follows Windows (`AppsUseLightTheme`), and a change made in either program reaches the other through the registered broadcast `FastMediaSorter.UiThemeChanged`. **High contrast always resolves to light** (system colours). Each host program calls `ThemeService.Initialize()` once at start-up; a new host does the same.
- **One palette** ([ThemePalette.vb](../../src/Theming/ThemePalette.vb)): 18 `ThemeRole`s, one light and one dark value each. Light values are live `SystemColors`, never copied numbers; dark values are a charcoal ladder. A new colour is a role with both columns, never a second table. The accent is Windows' own (`AccentColors`, **read once per process** - a change shows at the next start); the dark accent is darkened until white ink reads 4.5:1.
- **One applier** ([ThemeApplier.vb](../../src/Theming/ThemeApplier.vb)): it remembers per control which role a system colour stood for and repaints the role, so light-after-dark puts back exactly what was there, and an inherited colour stays inherited. **A literal colour is the code's own and is left alone** - except the status literals (ForestGreen, Firebrick, the attention amber) and the muted greys (DimGray, Gray, DarkGray), restated through `ReadableText` at 4.5:1. So a surface that must follow the theme uses a `SystemColors` value; a literal means "the same in both themes".
- **Who themes a window.** The `ThemeService` message filter themes a form on its first `WM_PAINT`, before that paint is dispatched; a hidden window catches up by the revision counter. A new form needs nothing for this; `ThemeService.Attach(form)` exists only to be right before the first paint. Owner-drawn code reads `ThemeService.Palette` and redraws on `ThemeService.Changed` - a **static** event, so every subscriber unsubscribes.
- **`IThemeAware.ThemesItself`.** True: the service does not walk the window, and **the reason is written where the window is defined**. False: the service walks first, then `ApplyTheme(palette)` paints the owner-drawn rest. A drawn check-box row implements `IThemedOptionRow`. Content-coloured windows (`Main_Form`, the thumbnail panel, `ChromeHost`) are True because their colour is the picture's (`APP-STYLE` rule 5); `Main_Form` paints its one Designer-white label by hand in `ApplyTheme` ([Main_Form.Theme.vb](../../src/Main_Form.Theme.vb)).
- **The bridges to the floating chrome are fixed**: every chrome popup takes `ChromeTheme.ForMenu(ThemeService.Palette)` (a popup is a menu - opaque, application theme, never content-tinted), `AccentColors.ForChrome`, and `ThemeService.Changed` reconfigures the chrome.
- **Deliberate, do not "fix".** Light `Warning` (239,108,0) is about 3:1 on purpose - the shared tone of `ICON-RENDER`; text on it goes through `ReadableText`. `ThemedComboBox` owner-draws in **both** themes, only for `DropDownList` and only while `DrawMode = Normal`; `RowFittingComboBox` (same file) is the variant for flow and table rows, so the list never covers the next row. `ThemedMenuRenderer` is installed in **dark only**; light restores the original renderer. `CheckBoxLayout` forces box left, text left and no ellipsis on **every** CheckBox. A disabled flat button is repainted by the applier because the framework ignores its `ForeColor`.
- **`RecipientPalette`** is the one Theming file that compiles on both runtimes, and the Share Manager does not link it. The mainline blends are 0.75 / 0.75 / 0.68; **net48 keeps its old 0.6 / 0.7 / 0.55** behind a `#If NETFRAMEWORK` seam (maintenance policy).
- **Seams and lists.** Every other Theming file and both `Help\` files are whole-file `#If Not NETFRAMEWORK`, yet the x86 [project](../../src/FastMediaSorter.vbproj) lists all of them and `Main_Form.Theme.vb` by hand, and the Share Manager project links each shared one by name. A new file under `src/Theming/` or `src/Help/` goes into **both** lists in the same commit. The x86 exe has no theme, no `?` button and no F1 help.

**Context help** (`CONTEXT-HELP`, pointer [CONTEXT-HELP.md](../../docs/contracts/CONTEXT-HELP.md))
- **Declaration.** `help-map.json` (repo root, `schemaVersion` 1, embedded as the manifest resource `help-map.json` in the modern and the Share Manager projects) has one row per window: `type` is `Type.FullName`, `form` is page / both / inherit (with `parent`) / none, `help` names the topic file, `ask: "off"` means the window takes neither the button nor F1. A Form without a row fails `Test-ContextHelp`. `Table_Form` and `Share_Settings_Form` resolve per page through their `ActiveHelpPageId`.
- **Language and opening.** [ContextHelp.vb](../../src/Help/ContextHelp.vb) resolves `<locale>/<help>.html`, then `en/<help>.html`, then `en/index.html`; the locale is one of the 13 `Localization.Codes`; an anchor is appended only if it is plain letters, digits, `_` and `-`. The page is a **local file** opened in the default browser through the replaceable `ContextHelp.Launch` - never the site.
- **Button and key** ([ContextHelpUi.vb](../../src/Help/ContextHelpUi.vb)). `ThemeService` calls `ContextHelpUi.Attach` for every form it themes, so each gets a corner `?` (`BadgeEdge` 44, glyph `app.help`, the key `Справка` plus `(F1)`) and a non-global F1. `Main_Form` is skipped on purpose: its help card and the chrome own it.
- **Source chain.** The only authored text is `tools/settings-help-copy.json` (every topic in all 13 languages; a missing cell makes the generator throw). [Build-ContextHelp.ps1](../../tools/Build-ContextHelp.ps1) combines it with `help-map.json` and `docs/help/context-help-capture-evidence.json` into `docs/help/<locale>/<topic>.html` and `index.html`. **Never edit a generated page by hand.** The PNG captures (hand-run `tools/Capture-ContextHelp.ps1`, unlocked desktop) and `help.css` are inputs. The pt pages are pt-BR like the pt UI.
- **Page rules.** No script, no remote request, `no-referrer`, `noindex` (`CONTEXT-HELP` rule 12); light and dark follow the system through `help.css` alone.
- **Gate.** [Test-ContextHelp.ps1](../../tools/Test-ContextHelp.ps1) is release tier: every Form type has a row, all 13 locales are complete, page anatomy, the page rules above, European Portuguese markers fail, every topic is accounted for in the capture evidence.
- **A new window or a new topic** adds its row to `help-map.json`, its 13-language copy to `settings-help-copy.json`, then runs the generator; nothing else.

**Tests**
- `ThemePaletteTests`, the Share Manager's `ThemeApplierTests` and `ThemeReopenTests` (collection `Theme service state`, not parallel), `RecipientPaletteTests`, `ContextHelpTests`, `ContextHelpCornerTests`.
- **Colour discipline is enforced narrowly, not repo-wide**: `SettingsColorGateTests` scans the Settings window sources, `PanelColourSourceTests` the panel files. Other forms may still hold literals - do not claim otherwise.
- Tests tagged `Kind=UiForm` (`CheckBoxLayoutTests`, `RowFittingComboBoxTests`, the settings check-box and option-help classes, `The_real_settings_dialog_is_fully_themed`) never run in the release: `tools/Test-UiForms.ps1 -Forms <type>` for a form the change creates or edits.
