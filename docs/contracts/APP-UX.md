# Pointer - desktop UX and `WINDOWS-UI`

| | |
| --- | --- |
| **Ids** | `APP-BEHAVIOUR`, `APP-STYLE`, `APP-SETTINGS`, `WINDOWS-UI` |
| **Version** | 0.12 / 0.12 / 0.2 / 0.1, draft. Windows profile and amendments read 2026-10-05; general behaviour retains its 2026-10-02 audit. Owners: StreamsPlayer, CyrFlip, and this Windows reference respectively |
| **Home** | `desktop-app-ux/README.md` in the shared contracts catalog |
| **Role here** | consumer, twice - the viewer (.NET 10 mainline) and the Share Manager each have their own registry row; audit and phases in `specification 028` (local specification) |

## What this repository must do to stay conformant

- Every dialog has one no-action exit, and Escape in a window never reaches the viewer's own shortcuts
  (`SettingsKeyForwardingTests`).
- **A confirmation of a destructive action defaults to the safe answer** (rule 5 as amended in 0.11):
  the destructive answer never holds the default focus, so Enter declines and Escape leaves. Held since
  2026-10-02 in both programs - every destructive `MessageBox` passes
  `MessageBoxDefaultButton.Button2` (Cancel for the settings-import `YesNoCancel`); the Share Manager
  already did.
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
- **The application's own per-user shell entries are its own state** (rule 4 as added in 0.12): repair
  of a kind the user consented to and that is still on is maintenance, not a new consent event - HKCU
  only, own name/exe, brings the entry into line with the consent, every write logged. Held: the viewer
  registers nothing at start-up (`CheckAndOfferImageAssociations` only records that the question was
  asked), and every registration path is the explicit settings action. Adding a new kind of registration
  still needs consent.
- Window geometry is written in normal-state coordinates and restored to the nearest display
  (`WindowPlacement`).
- Both programs offer system / light / dark (`UiTheme`, shared hive) through one palette table and one
  applier (`specification 060` (local specification)); a window
  outside the theme implements `IThemeAware` and says why where it is defined. The viewer's main window and
  thumbnail panel are content-coloured windows, accepted by the contract's own amendment (APP-STYLE 0.10
  section 5, from this product's proposal).
- **The palette roles are contract vocabulary, product values** (APP-STYLE 0.11 section 4):
  `danger` marks an irreversible action, `warning` a "may not do what you expect" caveat, `success` a
  positive mark; on a confirmation the role marks the acting button, never the focus. The shared day tone
  of `state.warning`, pinned by `ICON-RENDER` 0.14 section 10 D, is `#EF6C00` - `ThemePalette` follows it
  since 2026-10-02, and no window paints a role colour as a literal. The one deliberate exception is the
  settings navigation's per-page identity inks (specification 128, 2026-10-05), accepted by
  `ICON-RENDER` 0.16 section 11 and `APP-STYLE` 0.12 section 10. The inks live in one gated table,
  never at a call site; actual row-state contrast and high contrast require their own acceptance.
- **The language selector follows `APP-SETTINGS` rules 5 and 7** (read against this product 2026-10-04, owner
  direction): on the first settings page, every shipped language by its endonym, no flag, applied at once
  to every open window (`LanguageSelectorContractTests`); the settings drop-down lists share one pixel font
  and an item height of at least 28 logical px. The modern revision now places language and theme together
  on General, with independent collapsible groups and persisted page/viewport context
  (`Table_Form.SettingsGroups.vb`, `SettingsUiContext.vb`). `APP-SETTINGS` 0.1 was mapped on 2026-10-04;
  the catalog registry distinguishes the implemented revision (search, independent groups, remembered context,
  owned operations, license and source-derived inventory) from physical DPI/touch/text-scale and screen-reader
  acceptance still unverified. Group/navigation/value targets are enlarged; real spoken output is not inferred
  from accessibility properties. Later findings of audit 122 remain separate open follow-ups.
- The net48 x86 fallback takes bug fixes only; its gaps are one dated exception in the registry.

## Windows reference profile - owner decision 2026-10-05

`WINDOWS-UI` 0.1 is the shared Windows settings/dialog/working-viewer profile requested by the portfolio
owner. `APP-SETTINGS` 0.2 section 8 accepts the previously proposed independent groups and remembered
context; `APP-STYLE` 0.12 section 10 clarifies complete theme lifecycle coverage. Earlier 0.1 references
above describe historical audit evidence, not the current catalog version.

The modern viewer is the reference. Adopt by ID and section; the shared catalog holds the normative
text and source-derived metric/hash artifact. Full runtime, physical DPI, high-contrast and spoken
accessibility conformance remains separately verified. The net48 maintenance policy is unchanged.

The [2026-10-05 Windows UI audit](../guides/WINDOWS_UI_AUDIT.md) records a bounded construction/theme pass
over all 37 viewer and Share Manager form types, seven rendered dialog types, native numeric checks,
menu construction and corrected shared defects. Full interaction, physical DPI, live high contrast and
spoken accessibility acceptance remains open; the audit does not declare whole-profile conformity.
