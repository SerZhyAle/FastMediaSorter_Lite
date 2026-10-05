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
