# Windows UI contract audit - 2026-10-05

The .NET 10 viewer and Share Manager were reviewed against WINDOWS-UI 0.1, APP-SETTINGS 0.2,
APP-STYLE 0.12 and ICON-RENDER 0.16. The verdict is **partial conformance with fixes**. The net48
fallback retains its maintenance-only scope and existing exceptions. The headless SFTP worker has no
Windows UI; the server surfaces under review are the Share Manager and its dialogs.

## Coverage and evidence

The [integration probe](../../tests/Integration/WindowsUiAudit/Program.cs) discovers every concrete Form
type in both current assemblies. It constructed all 37 types, applied five alternating Dark/Light
palettes and checked 145 native numeric-button instances across those cycles. It builds the actual
image/video menu item collections without executing their actions. Seven dialog types additionally
receive an off-screen Show/Load and bitmap render in both palettes. The report records each type,
control counts and actual DPI. The available display in this run reports 216 DPI (225%).

Reproduce from the repository root:

```powershell
dotnet run --project tests/Integration/WindowsUiAudit/WindowsUiAudit.csproj
.\tools\Run-AllTests.ps1 -SkipGo
```

Generated evidence goes to `stage/windows-ui-audit/surface-inventory.json` and adjacent Light/Dark PNGs.
The probe verdict is limited to the scope named in that JSON; it is not a whole-contract accessibility
or rendered-state verdict. Main viewer/server hosts are constructed, not started. No listening server,
elevation, file operation, export, download or menu action is executed. The probe must remain hand-run.
The ordinary runner passed net48 (799), modern viewer (3105, 27 skipped) and Companion (399, one skipped)
before the final additional dropdown pixel regression; its six checks passed and Go was explicitly skipped.
The final Companion rerun passed 400 tests with one explicitly skipped; documentation and check-placement
gates also passed after registering this report and probe.

## Findings corrected

| Contract area | Finding and resulting behavior |
| --- | --- |
| Sections 4, 7: native numbers | Secondary viewer/server windows lacked the settings numeric-button helper. The common theme walk now applies it to every NumericUpDown, including changes in monitor DPI, retaining native validation and value semantics. |
| Sections 4, 6: dropdowns | Native closed fields and arrow zones could remain white in Dark despite assigned BackColor. A shared palette-aware field/list renderer now covers ordinary dropdown-list controls while preserving native selection and accessibility; existing specialized renderers retain ownership. |
| Section 3: group focus/state | Server groups hid the focused editor and exposed expansion only as description text. Collapse now returns focus to the header, and the accessible object exposes expanded/collapsed state and a default activation action. |
| Sections 3, 5: settings context | Server settings resized on each group toggle and lacked their own remembered context. Toggling now preserves user geometry; stable group IDs, anchor-relative viewport and normal window bounds are retained as private UI state, separately from exported server preferences. |
| Section 4: row alignment | Theme, language, connection cap and port followed their caption widths. They now use a common trailing editor column; port commands remain separate explicit actions. |
| Sections 5, 6: live system colors | A Light/Dark comparison could suppress a system-color/high-contrast refresh. Forced system refreshes now advance a revision, including hidden/reused windows on their next paint. |
| Section 6: navigation contrast | Identity inks were gated against only the unselected sidebar. Resolution now checks normal, hover and pressed fills, adjusts the tone, and substitutes system foreground/fills in high contrast. |
| Sections 6, 7: state text | Warning summaries and status text could use a glyph tone below text contrast. Text tones now reach 4.5:1 on their actual background without changing the semantic glyph palette; server groups/list surfaces use roles. |
| Sections 6, 8: thumbnail chrome | The thumbnail toolbar opted out of the shared renderer and its hosted dropdown escaped the theme walk. Command/status surfaces and hosted controls now follow the theme while the picture grid stays content-colored. |
| Sections 6, 7: QR action | The package wizard retained a literal blue/white action across palette changes. Its action face, ink and glyph now resolve through the current palette; QR image content deliberately remains white for scanning. |
| Sections 2, 4: RTL | Shared checkbox preparation forced LTR regardless of language. It now preserves inherited direction and places the description inset on the matching side. |

Regression coverage includes real native numeric controls, dropdown bitmap pixels, hosted toolbar
editors, group focus/accessible state, geometry on group changes, hidden-window theme revisions,
RTL hint indentation and actual navigation-state contrast. Existing settings/chrome/dialog/menu tests
cover discovery, context restoration, editor limits, keyboard routing, overflow and operation parity.

## Acceptance still required

Full conformity is not declared for every interaction/state. Remaining driven acceptance includes:

- Physical 100/150/200% scaling, mixed-monitor movement and Windows text scale; the 225% render and
  arithmetic scale tests do not replace these.
- Live OS high contrast, theme/accent changes, screen-reader speech and the coarse-input target floor;
  properties and injected palette states alone do not prove these.
- Main viewer/server rendered states with actual media, populated folders, active playback and every
  popup/submenu; construction and menu-builder coverage do not exercise those interactions.
- All long translations and RTL pages in the server's complete operational states.

These are open acceptance items, not passing tests or reasons to alter the accepted contract.

## Addendum 2026-10-07 - read against WINDOWS-UI 0.3 and APP-SETTINGS 0.3

The catalog moved after the audit above: `WINDOWS-UI` 0.2 (show a page with one layout pass, time every page),
0.3 (nesting under a navigation group header) and `APP-SETTINGS` 0.3 (section 9, an operation on the page of a
surface that has no Commit). This addendum records what the current source does and does not do; it ran no
driven session and takes no timing, so it adds no acceptance evidence to the section above.

| Requirement | State in the .NET 10 viewer | State in the Share Manager |
| --- | --- | --- |
| `WINDOWS-UI` section 2 item 6 (destinations under a group header stand one indent in) | Not applicable: the settings navigation is a flat list of eleven buttons with no group header (`Table_Form.ModernLayout.vb` `BuildModernSettingsNavigation`). The rule binds the day a header appears. | Not applicable: no navigation list; its settings are collapsible sections in one scrolling column. |
| Section 5, layout of a subtree held and laid out once | Partly. The window is built under one `SuspendLayout` / `ResumeLayout(True)` (`Table_Form.ModernLayout.vb`, construction), and `DialogLayout.FitContent` holds layout around its reflow. NOT held on a page switch or a group toggle: `QueueRestoreSettingsContext` and the group `ExpansionChanged` handler call `LayoutModernSettingsPages`, which re-measures every page, not only the shown one (`Table_Form.SettingsGroups.vb`). All eleven pages are built in the constructor; no part is built lazily. | Not held. `CollapsibleSection` toggles a body's visibility with the layout live; the only suspended layout is the content-mode switch of the main window (`MainWindow.Layout.vb` `ApplyContentMode`). |
| Section 5, a remembered scroll position is restored only after that layout | Yes in order: the restore runs from a `BeginInvoke`, calls `LayoutModernSettingsPages`, then reads the anchor and sets `AutoScrollPosition` (`Table_Form.SettingsGroups.vb` `QueueRestoreSettingsContext`). | Yes: restored in `OnShown`, after the form's layout (`Share_Settings_Form.UiContext.vb`). |
| Section 5 budget (first visit 500 ms, repeat 150 ms, dialog 500 ms) and section 9 timing | Not measured. No stopwatch, test or probe times a page or a dialog; the probe above builds and renders but records no times. | Not measured. |
| Section 9, compare the rendered page with the unoptimized one | Not done (nothing was optimized for this rule). | Not done. |
| `APP-SETTINGS` section 9 (an operation: dedicated button, rule 5 confirmation, never on a value control) | Held by reading: each cache has a Clear button that asks first with the safe answer focused (`Table_Form.ExpandedSettings.vb` `AddCacheUsageRow`), "Restore defaults" and the settings import are buttons with their own confirmation, and no value control starts one. Two gaps: a Clear button asks even when the measured usage is 0 MB (section 9 item 2 wants "nothing to do" said, not asked), and no static sweep (section 5 rung 3) exists. | The Manager's settings dialog was not mapped against the twelve rules in this pass. |
| Spec 169 (reset all viewer settings on the About page) | Planned, not built. Its design - one button on the About page, one confirmation, applied at the next start with a backup - is what section 9 permits; it no longer needs to avoid the page for lack of a rule. | Untouched. |

Each unmet row is a dated exception in the catalog registry (until 2026-12-31) and one item of the consolidated
follow-up; none changes a stored value or a visible behaviour today.
