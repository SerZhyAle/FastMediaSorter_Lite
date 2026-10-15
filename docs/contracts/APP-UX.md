# Pointer - desktop UX and `WINDOWS-UI`

| | |
| --- | --- |
| **Ids** | `APP-BEHAVIOUR`, `APP-STYLE`, `APP-SETTINGS`, `WINDOWS-UI` |
| **Version** | catalog 0.12 / 0.12 / 0.4 / 0.4, draft (settings/profile read 2026-10-08; `WINDOWS-UI` 0.4 section 11 read 2026-10-11). Behaviour and style keep their 2026-10-02 audit (`APP-BEHAVIOUR` is unchanged since 0.12, `APP-STYLE` 0.12 section 10 was read 2026-10-05); the 0.2 / 0.3 amendments of `WINDOWS-UI` and `APP-SETTINGS` were read 2026-10-07, and `WINDOWS-UI` 0.2 sections 5 and 9 and `APP-STYLE` section 5 (the audio overlay) were worked against the viewer on 2026-10-13. Owners: StreamsPlayer (the first two), CyrFlip, and this Windows reference respectively |
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
- **The audio surface is a declared content overlay** (APP-STYLE 0.10 section 5, condition 3; `WINDOWS-UI`
  0.4 section 6 item 7, 2026-10-13): the plate and the ink over a cover or the WAVE-PARTICLES backdrop are one
  table, `AudioOverlayColours`, which says where it is defined that it follows the content and not the theme;
  `Main_Form.AudioView.vb` carries no colour literal. `AudioOverlayContrastTests` holds the pair to 4.5 : 1 for
  text and 3 : 1 for strokes over a white and a black cover and every grey and cube corner between (white ink
  8.06 : 1 and the quieter format line 5.88 : 1 over the worst, a white cover). The backdrop's own lines and
  particles are the catalog's colours and opacities and are not gated - measured and recorded, a finding for the
  contract's owner, not a local edit.
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

## Windows reference profile - owner decision 2026-10-05, read again 2026-10-07

`WINDOWS-UI` is the shared Windows settings/dialog/working-viewer profile requested by the portfolio owner
(0.1 on 2026-10-05; 0.2 and 0.3 on 2026-10-07). `APP-SETTINGS` 0.2 section 8 accepts the independent groups and
remembered context; `APP-STYLE` 0.12 section 10 clarifies complete theme lifecycle coverage. The modern viewer
is the reference. Adopt by id and section; the shared catalog holds the normative text and the source-derived
metric/hash artifact.

What the 2026-10-07 versions ask, and where this product stands (source read, nothing driven; the line-level
record is in the registry rows and in the addendum of the [Windows UI audit](../guides/WINDOWS_UI_AUDIT.md)):

- `WINDOWS-UI` 0.3 section 2 item 6 (destinations under a navigation group header stand one indent in): not
  applicable today - the viewer and Share Manager settings both use flat navigation lists. It binds the day either gets a group header.
- `WINDOWS-UI` 0.2 section 5 (a page is laid out once; a scroll position is restored only after that layout)
  and section 9 (every page and secondary window is timed), viewer, 2026-10-13: **a page switch and a group
  toggle hold their layout and lay out the shown page once.** A group holds its own layout and its page's while
  it changes and raises `ExpansionChanged` only after that pass (`SettingsCollapsibleGroup.Expanded`); a page
  is sized with the page and its group bodies held (`Table_Form.ResizeModernPageItems`,
  `LayoutModernSettingsPage`), the other ten pages are not re-measured, and the remembered position is read
  and restored after the layout. Gated by `SettingsLayoutPassTests` (the group runtime half and a source scan
  of the entry points, each with a self-test) and, by hand, by `tests/Integration/SettingsLayoutScenes`, which
  counts Layout events and compares every control's bounds, scroll range and rendered pixels with another
  build (identical at 100 % and at this machine's 175 %, 110 snapshots). **Every page and every window the
  dialog layer opens is timed on request**: `FMS_UI_TIMING=1` makes the viewer append one line per shown
  surface to its own `current.log` (`UiTiming`, `UiTimingBudget`; nothing else, nothing leaves the machine;
  `UiTimingBudgetTests`), and `tests/Integration/SettingsTimingScenes.ps1` (hand-run) drives the built viewer
  and prints the table. Measured 2026-10-13 on this machine: every page is within 500 / 150 ms and every
  dialog within 500 ms; **the settings window itself opens in 0.5 to 2.4 s (cold, then warm), over its 500 ms
  budget** - all eleven pages are still built in the constructor; the registry keeps that, and the physical
  reference-hardware measurement, as a dated exception (until 2026-12-31). The Share Manager keeps its own row.
- `APP-SETTINGS` 0.3 section 9 (an irreversible operation on a page of a surface with no Commit): the viewer's
  settings apply on touch and close with Close, so the section applies. Its cache Clear buttons, "restore
  defaults" and the settings import are buttons with their own confirmation (safe answer focused), and no value
  control starts one. Both gaps recorded on 2026-10-07 are closed (specification 183): a cache measured at 0
  bytes says "Nothing to clear" and does not ask, and rung 3 is built - `SettingsIrreversibleHandlerTests` reads
  every value-event handler of the settings window's sources (64 on that date) and what each reaches, two calls
  deep, and fails on a file, folder, registry or disk-cache deletion; a handler it cannot place is a failure of
  the sweep, never a pass. Spec 169 (reset all settings, About page) is the planned operation this section makes
  permissible; it is not built, and it cites section 9.

Full runtime, physical DPI, high-contrast and spoken-accessibility conformance remains separately verified, and
the net48 maintenance policy is unchanged.

The [2026-10-05 Windows UI audit](../guides/WINDOWS_UI_AUDIT.md) records a bounded construction/theme pass
over all 37 viewer and Share Manager form types, seven rendered dialog types, native numeric checks,
menu construction and corrected shared defects. Full interaction, physical DPI, live high contrast and
spoken accessibility acceptance remains open; the audit does not declare whole-profile conformity.
The Share Manager settings implement `WINDOWS-UI` sections 2-5 and `APP-SETTINGS` surface anatomy
(2026-10-08): General first, About last, glyph/caption navigation, independent groups inside pages,
visible hints beside trailing editors, pane search and expand/collapse commands. About includes version,
embedded MIT license, privacy and the existing support-bundle action. Language changes update open windows
in place. Stable page/group IDs preserve expansion and anchored viewport context; the old single-page
anchor migrates. Page switches and group changes hold layout and preserve the window rectangle; numeric
and dropdown wheel messages scroll the pane without changing a value. Stored preference and operation
semantics are preserved.

`WindowsUiContractTests` exercises page geometry/context, visible hint bounds, About anatomy, native
accessibility state, live language refresh and native editor theme behavior. Exchange administration UI
checks exercise the optional server page with its groups expanded. These checks and rendered screenshots
are bounded acceptance evidence, not physical DPI, mixed-monitor, high-contrast or spoken screen-reader
acceptance. The dated 2026-10-07 whole-profile audit remains historical; full profile acceptance is open.


Owner corrections, 2026-10-09: Share Manager settings have no bottom Close/footer strip; the native
caption Close and Escape remain. The support-bundle entry inside settings is one button on About.
Navigation identity inks reuse the centralized light/dark table; pane commands and settings disclosure
glyphs also use colored ink with contrast resolution and a high-contrast system-color override.
Theme application holds the complete subtree, including checkbox normalization; asynchronous hosting
and exchange snapshots hold the owning page. Width fitting caches the last fitted width per viewport.
Button/checkbox design sizes stay in 96-DPI units until the single form scaling pass.
