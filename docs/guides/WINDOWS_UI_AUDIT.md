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


## Share Manager settings follow-up, 2026-10-08

This bounded implementation/acceptance pass supersedes the Share Manager settings findings above;
it does not supersede the viewer findings or certify the whole profile. Normative references:
`WINDOWS-UI` 0.3 sections 2-5 and 9, `APP-SETTINGS` 0.4 section 2, `APP-STYLE` 0.12 and `ICON-SET`.
Product names continue to follow [PRODUCT_NAMING.md](PRODUCT_NAMING.md).

The settings window now has General first and About last, centralized glyph mappings, selected/focused
navigation states, a fixed pane command strip, independently collapsible inner groups, visible hints
with trailing editors, search by localized caption/description and stable page/group/viewport context.
Search reveals and focuses the owning editor. About contains the embedded MIT license, version,
privacy link and existing support-bundle action. Language changes refresh open windows instead of closing
the settings window. The owner monitor is used when an old rectangle belongs to another monitor.
Native value controls retain their semantics; wheel input scrolls the pane without modifying a value.
Page switches and group changes hold layout; numeric values use the reference digit font and width.

Evidence: `WindowsUiContractTests` drives page/scroll restoration, fixed geometry, group focus and
accessible state, visible hint bounds, About anatomy and live language refresh without changing editor
values. Locale cases cover Russian, German, Arabic, Hindi and Chinese; localization parity covers all
13 shipped languages. `ExchangeAdminGroupUiTests` drives the optional server page and accounts/devices
with its groups expanded. Rendered General, Network, Hosting, About and optional server screenshots
were inspected; that inspection found and corrected hint clipping and stale content widths.

A local Release probe through construction, Show and the pending message queue measured dialog 414 ms,
first Network visit 276 ms, repeat 26 ms. This sample meets the 500/500/150 ms budgets on this workstation;
it is not a cold-process, every-page or reference-hardware benchmark. Set `FMS_SETTINGS_CAPTURE` to a
scratch directory when running the UI tests to reproduce screenshots and the timing sample.

Physical 100/150/200% scaling, mixed-monitor movement, live Windows high contrast and spoken
screen-reader acceptance remain unverified. Programmatic RTL, accessibility-state and palette checks
cannot replace those observations. No whole-profile conformance claim is made.


Validation on the current files: Release Share Manager build succeeded with zero warnings/errors;
Share Manager full suite passed 1,316 tests (2 skipped), and the final Release UI contract subset
passed 14 tests. The viewer suites passed 803 net48 and 3,816 modern tests (27 skipped) with test
collections serialized using a scratch xUnit runsettings file. The ordinary parallel aggregate run
reported two viewer popup-state failures; the affected menu/iconography subset passed all 67 tests
in isolation, and the complete serialized viewer run passed. This suggests test interference, but
does not establish its cause. Check placement, documentation registry, product naming and the site
gates passed. Go worker, Go exchange and exchange-conformance steps were explicitly skipped;
the ordinary aggregate runner therefore retains its viewer FAIL verdict, not an invented overall PASS.


## Settings performance and owner corrections, 2026-10-09

The 2026-10-08 in-test timing did not initialize the production theme message filter or enter the
process with PerMonitorV2 and the production default font. It was insufficient evidence for the
reported slow interface. A separate process now initializes PerMonitorV2 and ThemeService before
constructing settings; the available monitor reports 168 DPI (175%). The server component/process
responses are stand-ins; no live service or server operation is exercised by this probe.

Two repeated-work paths were corrected: theme application used to hold only the form while ambient
color changes remeasured nested AutoSize containers; fitting a viewport repeatedly walked and assigned
widths throughout every page. Theme application now suspends the full subtree, and width fitting caches
each viewport's width and holds only the affected subtree. Hosting/exchange snapshots also suspend their
page while applying text/visibility changes. Numeric/button/checkbox design dimensions no longer pass
through LogicalToDeviceUnits before the form's own DPI pass, fixing inflated command targets.

The comparable themed probe measured opening at 3,329 ms before and 753 ms after these changes.
The final probe also pins the production Segoe UI 9 pt default: opening 670 ms, Light/Dark/Light theme
application 14-18 ms, first exchange-configuration expansion 211 ms and collapse under 1 ms. Other
measured group transitions were below 34 ms. This does not reproduce or claim to explain every reported
10-second stall; cold opening still exceeds the profile's 500 ms budget in this process. Server replies
are simulated, and this is one physical scale/monitor, not mixed-monitor acceptance.

`WindowsUiContractTests` adds repeat group-transition budget checks at programmatic scales 1.0, 1.75,
2.0, with the full exchange configuration visible and its backend disabled; each repeated toggle must
complete below 150 ms without changing the window bounds. These scale cases are layout stress checks,
not a substitute for physical 100/200% monitor checks. Palette tests cover chromatic navigation inks,
3:1 contrast and high-contrast fallback. Anatomy tests require no bottom Close action and exactly one
support action inside settings, located on About. Native caption Close and Escape remain available.
The owner-requested colored glyphs reuse the shared palette table; meaningful text stays in text roles.


Final repeated standalone sample: cold opening 660 ms, opening a second settings window in the same
process 354 ms, Light/Dark/Light application 12-19 ms, maximum measured group transition 85 ms
(exchange configuration; Network 45 ms). The earlier 211 ms configuration sample remains recorded
above rather than being hidden. The About screenshot at 175% confirms one normally scaled support
button, colored glyphs and no footer; license LF line endings are normalized for the native edit.

Validation: `Run-AllTests.ps1 -SkipGo -SkipExchangeGo`, with a scratch `RunSettingsFilePath` that
serializes xUnit collections, returned exit 0 and `Run-AllTests: PASS (9 passed)`; 803 net48, 3,817 modern
viewer and 1,339 Share Manager tests passed, with 29 tests and all three explicitly excluded Go steps
reported skipped. The final license-line-ending and all-state icon-contrast checks each passed in
focused Release runs after the aggregate suite. Release Share Manager build returned exit 0, zero
warnings/errors. Physical 100/200%, mixed-monitor movement and spoken accessibility remain outside
this measured pass.

## Full settings-group geometry and first setup, 2026-10-09

A second audit found that the previous timing checks could pass while expanded content was clipped.
Resuming ancestor layouts without laying out the group itself left its allocated height at the closed
header height. Auto-sized nested flow columns could also retain an old narrow width. Group disclosure
now holds the body subtree and ancestors, then lays them out from the children upwards. Width fitting
accounts for ancestor padding/margins and gives full-width nested columns and setting rows a matching
minimum width. Ordinary rows directly measure and arrange their two columns, avoiding the additional
nested table layout cost. Range labels stay with their associated numeric editor when the strip wraps.

New tests visit every destination, expand every group and assert containment of editors, buttons,
lists and nested flows, hint height, and separation of captions/hints from editors. They exercise
640/820 logical-pixel client widths and four simulated exchange states: no configuration, stopped,
registered service stopped and console running. The developer-only client page is enabled for these
tests without changing the persistent feature switch. The exchange port note is now the row's single
hint, and the console keep-running explanation follows the visibility/enabled state of its checkbox.

The first-server wizard now has a scrollable body and a bounded window rather than an auto-growing
form that can clip its final actions. Its UI test performs initial setup through stand-in ports,
checks the generated password remains masked, reduces the viewport and scrolls to the final action.
No real account, service, firewall or enrollment operation is performed by these tests. Screenshots of
all available destinations and relevant editors were captured on the physical 175% monitor at both
widths; this remains distinct from physical mixed-monitor, high-contrast or screen-reader acceptance.

The executable audit also found that the prior settings changes had only been built in the project
output. The two work-folder copies and the CompanionPublish copy still reported 26.10.9.0016; a newly
running installed Server copy reported 26.10.8.2354. Building source alone did not update those copies.

The initial aggregate reruns caught additional timing failures (167-196 ms, and one 753 ms sample)
while geometry checks passed. The 150 ms threshold was retained. Disclosure now stops layout holding
at the fixed scroll viewport, reserves scrollbar width so its appearance does not refit all descendants,
and reports the already-sized body's height to ancestor tables instead of recursively remeasuring it.
Setting-row text measurements are cached by width and invalidated for text, font and margin changes.
After these changes the focused Debug UI/wizard/admin run passed all 67 tests in 23 seconds (exit 0),
including the same repeated-transition budget assertions. The final aggregate run is recorded below.

Final aggregate validation: `Run-AllTests.ps1 -SkipGo -SkipExchangeGo`, with serialized xUnit
collections via a scratch RunSettingsFilePath, returned exit 0 and `Run-AllTests: PASS (9 passed)`.
Viewer: 803 net48 and 3,817 modern tests passed. Share Manager: 1,342 passed, 2 skipped. Together
29 tests were skipped; the Go worker, Go exchange and exchange-conformance steps were explicitly
excluded. All documentation, naming, check-placement and site gates passed. The earlier failing
performance runs remain recorded above; the threshold was not loosened or removed.

A final standalone probe with asynchronous state reads enabled reproduced a separate 20-second stall
twice (20,014 and 20,027 ms). The budget tests had disabled these reads and therefore did not cover it.
A dotnet-stack capture located the UI thread in GetWindowText during TableLayout preferred-size
measurement; native HWND ownership showed the wizard row and button belonged to a pool thread.
When no synchronization context was captured, the exchange read continuation changed hidden controls
on that pool thread, creating their native windows there. A later expansion then synchronously queried
windows whose owner had no UI message loop.

Exchange and hosting read results now always pass through Control.InvokeAsync before changing UI.
The new regression check clears the synchronization context before refreshing, enables illegal
cross-thread checks and asserts every created child HWND belongs to the form's thread. All 68 focused
UI/wizard/admin tests passed (exit 0, 20 seconds). The standalone 175% probe then measured a maximum
transition of 50.2 ms across six toggles of every group, opening 521.5 ms and reopening 279.2 ms.
Cold opening remains just above the profile's 500 ms budget in this sample; no claim of complete
performance-profile acceptance is made. The former 20-second sample is retained as evidence, not
excluded from the audit. No live backend mutation was used in these probes.

Validation after the UI-thread correction: `Run-AllTests.ps1 -SkipGo -SkipExchangeGo` with the same
serialized collection settings returned exit 0 and `Run-AllTests: PASS (9 passed)`: 803 net48,
3,817 modern viewer and 1,343 Share Manager tests passed; 29 tests and the three named Go steps
were skipped. The delayed-inspect regression was rerun separately after strengthening its asynchronous
setup and passed (exit 0). The final standalone geometry/wizard captures were regenerated at 175%.

Self-contained win-x64 publication returned exit 0. Version 26.10.9.0133 was copied to both work folders
and the CompanionPublish/Release outputs; all four copies have SHA256
B17EB7A01EA8593BF0E0DF5B0BD51123BA138C0138FEC68DB3ABAF33E72D927C.
The prior binaries were backed up in a scratch directory. The installed Server copy in Program Files
was not replaced: this session lacks administrator rights. No worker/exchange payload or service
configuration was replaced, and no service was stopped to deliver the manager update.

## Addendum 2026-10-11 - a glyph on every command button (WINDOWS-UI 0.4 section 11, ICON-RENDER 0.19 section 14)

The owner asked for simple recognizable icons on the interface buttons, before the caption, and for the
answers of a dialog to draw the eye (OK green, Cancel red). The recommendation was adopted as written; the
decisions that were the product's to make are listed here, the numbers are in the registry rows.

- **One module owns it.** `src/Icons/ButtonGlyphs.vb` (linked into both programs; whole-file modern):
  `ButtonGlyphs.Apply(button, glyphId, role)` renders the catalog path at 16 logical px of the button's DPI in one
  colour with the 6 px gap inside the bitmap (so the gap scales and mirrors with the reading direction), and keeps it
  current through the theme (`ThemeService` hands over the palette), DPI, direction, Enabled and system colours.
  No dialog draws, tints or re-themes a picture of its own. `DialogLayout.WidenButtons` counts the glyph, so a
  fixed-width button grows for its caption instead of clipping it.
- **Roles.** Colour is spent on the answers of a dialog and nowhere else: the accept answer is green
  (`Affirmative`), the cancel path red (`Dismissing`), a continue-anyway answer amber (`Caution`); the acting
  button of an irreversible confirmation takes the danger face with its own ink and its dismissing neighbour keeps a red
  glyph and no tint; everything else is the mono look. The hue is the theme's own Success / Danger / Warning pair,
  moved along lightness only as far as 3 : 1 against the tinted face needs (`ButtonGlyphs.Readable`).
- **Not taken, and why** (dated exceptions in the registry): the operating system's message boxes and the one
  `TaskDialog` (their buttons are drawn by Windows); the random-slideshow mark, the Translate button's language
  text, the speed readout, the recipients overlay rows in right-to-left, the editor's swatches and hand-drawn tools.
  Left for the owner (a contract-boundary change): `Video_Replace_Confirm_Form` does not hold the default focus on
  its safe answer.
- **A bug found on the way.** MSBuild reads the penultimate extension of a resource file name as a culture:
  `action.run.svg` is Rundi (`run`), so that glyph was silently moved to a satellite assembly and left out of both
  executables. Both projects now say `WithCulture=false` on the glyph item (`The_glyph_resources_are_not_read_as_cultures`).
  `media.stop`, which the transport bar names, was not vendored at all; it is now.
- **Evidence.** `tests/Lite.Tests/AnswerButtonGlyphTests.vb` and `tests/Companion.Tests/ButtonGlyphCoverageTests.vb`:
  roles per answer, 3 : 1 and 4.5 : 1 in both themes (figures written to `answer-glyph-contrast.csv`), live theme
  change, the unavailable state, the gap per direction, the sizes at 144 and 192 dpi, the widest answer row in the 13
  languages (`answer-row-widths.csv`, 356 to 471 px, nothing clipped), the tint, a source scan that every glyph id a
  button names is vendored, and a walk of the Share Manager's windows that fails on a captioned button without a
  picture. `FMS_SHOT_DIR=<folder>` renders the windows light and dark to PNG.
- **Not driven:** the live Windows high-contrast switch, a physical 150 / 200 percent monitor, right-to-left by
  eye, and the viewer's own settings window and image editor on screen (their buttons are exercised through the
  shared module and the width rules, not rendered).
