# Pointer - `APP-BEHAVIOUR` and `APP-STYLE`

| | |
| --- | --- |
| **Ids** | `APP-BEHAVIOUR` (the twelve shared moments) and `APP-STYLE` (theme mechanism, palette roles) |
| **Version** | 0.10 / 0.10, draft (`APP-BEHAVIOUR` re-read 2026-09-24). Owner: StreamsPlayer. **Implemented: none**, partly conformant |
| **Home** | `desktop-app-ux/README.md` in the shared contracts catalog |
| **Role here** | consumer, twice - the viewer (.NET 10 mainline) and the Share Manager each have their own registry row; audit and phases in [specification 028](../specifications/done/028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md) |

## What this repository must do to stay conformant

- Every dialog has one no-action exit, and Escape in a window never reaches the viewer's own shortcuts
  (`SettingsKeyForwardingTests`).
- A failure shows a cause and an action - `FailureText.Describe(ex)` in the viewer - and the exception
  goes to the log; both programs can send their log. Gated: `DesktopUxConformanceTests`.
- `TF` never throws, and every call passes the arguments its key names (`LocalizationCallSiteTests`).
- Direction comes from `Localization.RightToLeftByCode`, never from a code list; a media transport bar
  is pinned left-to-right; a glyph-only control carries a name.
- A download runs behind `Download_Progress_Form` (progress, Cancel as an outcome); nothing - OCR
  language data included - is fetched without the user asking.
- An irreversible action confirms first; the product has undo, so a recycled delete may skip it. The
  one deviation: `ConfirmDelete = never` (an explicit user choice, never a default or a migration)
  skips the question for a permanent delete too - owner decision 2026-09-27, a dated exception to
  rule 5 in the catalog registry.
- Window geometry is written in normal-state coordinates and restored to the nearest display
  (`WindowPlacement`).
- Both programs offer system / light / dark (`UiTheme`, shared hive) through one palette table and one
  applier ([specification 060](../specifications/060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md)); a window
  outside the theme implements `IThemeAware` and says why where it is defined. The viewer's main window and
  thumbnail panel are content-coloured windows, accepted by the contract at 0.10.
- The net48 x86 fallback takes bug fixes only; its gaps are one dated exception in the registry.
