# Specification - Share Manager: a compact, resizable main window

> Status: **built - Ф1..Ф4 shipped 2026-08-15** (written 2026-08-14, revision 1). What building it
> corrected is §12; the manual scenes of §11 are still the owner's to walk.
> Owner ask: *"Это окно нужно сделать удобным небольшим, но растягивающимся, объединить элементы в
> сворачиваемые группы, оставить пользователю только важное (не убирать ничего, но перенести в
> сворачивающиеся группы или внутренние окошки управления сервисом/воркером, окошко настроек).
> В реальном использовании пользователю нужно всего 1-10 ресурсов, чаще несколько."*
>
> Scope: **the Companion's main window only** - [src/FastMediaSorterCompanion/Forms/MainWindow.vb](../../../src/FastMediaSorterCompanion/Forms/MainWindow.vb),
> one new reusable control, one new settings window. **No worker change, no IPC change, no `.fmscfg`
> change, no LITE change, no installer change, no new dependency.** Every behaviour stays; only where
> it is presented moves.
>
> Related: [SPECIFICATION_SHARE_COMPANION_APP.md](SPECIFICATION_SHARE_COMPANION_APP.md) (§4.5 the
> two-wizard model this window implements - the model is preserved, not revised),
> [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md) (§3.4 the
> status window that already exists and that the main window currently duplicates),
> [SPECIFICATION_SHARE_SYSTEM_SERVICE.md](SPECIFICATION_SHARE_SYSTEM_SERVICE.md) (the Hosting
> console and the Server edition, whose line must stay visible - §3.5),
> [SPECIFICATION_THIRTEEN_UI_LANGUAGES.md](../SPECIFICATION_THIRTEEN_UI_LANGUAGES.md) (§7.2 layout test,
> extended here to the new section headers),
> [SPECIFICATION_SHARE_MANUAL_PORT.md](../SPECIFICATION_SHARE_MANUAL_PORT.md) (its two controls land in the
> new settings window instead of the main one - §3.5).

---

## 0. Why this exists (the measurements, not the taste)

### 0.1 The window cannot be small - the code forbids it

```vb
' MainWindow.vb:113-114
Me.MinimumSize = New Size(980, 700)
Me.ClientSize  = New Size(1320, 880)
```

These are 96-DPI design units and `DpiLayout.ApplyAutoScale` multiplies them by the display factor.
On the owner's machine (175 %) that is a **minimum** of 1715 x 1225 physical px. The screenshot that
motivated this specification is 2518 x 1831 physical, i.e. a working area of about 1439 x 1046
logical px - so the scaled minimum **height** (1225) already exceeds the screen (1046), and
[DpiLayout.RelaxMinimumSize](../../../src/FastMediaSorterCompanion/Core/DpiLayout.vb) cuts the minimum
down to the working area while `ClampToWorkingArea` sizes the window to it.

**Consequence: on a 150-175 % display the Share Manager is a full-height window and the user cannot
shrink it - it is already at its minimum.** That is not a preference the user chose; it is a
computed floor. The safety net that keeps the window on-screen is currently doing the sizing.

### 0.2 The right-hand column holds twelve blocks; a session needs two

`grpServer` is `Dock = Right, Width = 500` (`MainWindow.vb:162`) and stacks, top to bottom:

state line, a six-row address grid, two prose paragraphs, a test button, a router link, a guide
button, the Start/Stop button, two checkboxes, a hosting line, a hosting button, a connection-limit
spinner, a reachability note, a four-row statistics block. Thirteen blocks in one scrolling column.

In a normal session the user touches **the Start/Stop button** and **"Поделиться"**. Everything else
is either read once during setup (addresses, credentials, router guide), configured once
(autostart, connection limit, hosting), or is pure reporting (statistics).

### 0.3 The one object the window is about gets the leftovers

The folder list carries the four columns `170 + 130 + 300 + 50 = 650` logical px
(`ApplyDpiScaledAssets`, `MainWindow.vb:579-588`), and it is `Dock = Fill`, so it grows with the
window. The owner's own framing is **1-10 shares, usually a few**. The attached screenshot shows the
real ratio: **one** folder row against roughly 1100 px of empty list and a full-height column of
controls beside it.

This also contradicts a standing preference already recorded for this repo: lists are sized to their
content, not stretched past it.

### 0.4 Three things are on screen permanently that should not be

- **The SFTP password in plain text** (`PasswordValue`, `MainWindow.vb:179`). Any screenshot of this
  window - including the one that produced this specification - publishes a live credential. Nothing
  about the normal flow (scan a QR) needs it visible.
- **The statistics block** (`MainWindow.vb:258-267`) duplicates
  [Share_Status_Form](../../../src/FastMediaSorterCompanion/Forms/Share_Status_Form.vb), a window that
  already exists, is reachable from the tray, and is the only place that can *reset* the counters.
- **Machine administration** - the hosting line + console button and, in Server mode, service state -
  sits between two consumer checkboxes and a connection spinner.

### 0.5 What this specification is not

Not a redesign of the two-wizard model (§4.5 of the Companion spec): level 1 is still the share list,
level 2 is still the package wizard behind "Поделиться". Not a re-flow of the package wizard, the
per-folder dialog, the hosting console or the internet-access guide. Not a theming pass.

---

## 1. Current reality - the full inventory

Every element of the window today, with the line that creates it. **This table is the contract for
§3.7: nothing in it may disappear.**

| # | Element | Line | What it is |
|---|---|---|---|
| 1 | `lblIntro` | :127 | One-line intro ("Откройте папки этого ПК на телефоне..") |
| 2 | `btnShare` | :128 | The big "Поделиться" action -> `PackageWizardForm` |
| 3 | `grpShares` + `pnlListButtons` | :272-286 | "Общие папки" group: `btnAdd`, `btnAddCurrent` ("+ Текущая", only with a folder argument), `btnRemove`, `btnParams` |
| 4 | `lvFolders` | :288-301 | The share list: checkbox + Название / Тип / Папка / RO, double-click configures |
| 5 | `lblState` | :167 | "Раздача с этого ПК работает" / "выключена" (bold, green/grey) |
| 6 | Address grid, 6 rows | :170-180 | Через интернет, Дома (Wi-Fi), IPv6, Ключ узла, Логин, Пароль - each with a copy button |
| 7 | `lblAccessState` | :187 | What actually works right now (`ShareText.AccessStateLine`), colour-coded |
| 8 | `lblAccessNext` | :189 | The one next step (`ShareText.AccessNextStepLine`) |
| 9 | `btnTest` | :193 | "Проверить доступ из интернета" -> `Share_Access_Test_Form` |
| 10 | `lnkRouter` | :198 | "Роутер: <model>" -> opens the gateway page |
| 11 | `btnGuide` | :202 | "Как настроить доступ через интернет" -> `InternetAccessForm` |
| 12 | `btnToggle` | :205 | Start / Stop sharing |
| 13 | `chkAutostart` | :208 | "Запускать при входе в Windows" |
| 14 | `chkOpenOnStart` | :217 | "Открывать окно менеджера при запуске" (+ tooltip) |
| 15 | `lblHosting` | :227 | "Хостинг: служба Windows" / user-mode line (`HostingText.HostModeLine`) |
| 16 | `btnHosting` | :230 | "Управление хостингом.." -> `Share_Hosting_Form` |
| 17 | `numMaxConns` + label | :237-247 | "Макс. одновременных подключений:" (+ tooltip) |
| 18 | `lblNetNote` | :252 | The calm reachability note (`ShareText.NetworkReachNote`) |
| 19 | `pnlStats` | :258-267 | "Статистика раздачи": last connection, connections, files served (+ tooltip) |
| 20 | `lblHint` | :140 | The bottom one-line status strip |
| 21 | Five links | :142-150 | Android app, site guide, router-model search, open the viewer, language |
| 22 | `progressBar` | :315 | Marquee strip while busy |
| 23 | `pnlEnable` | :333-344 | The `ServerFeatures` opt-in gate overlay (replaces the whole content) |
| 24 | `_statsTimer` | :122 | 10 s status refresh while the window is open |

Public surface the tray depends on and that must keep working unchanged:
`ShareFolderFromWakeAsync` (:627), `OpenShareWizardFromTray` (:1193), the `ServerStateChanged` event
(:79), and the `MainWindow(initialFolder)` constructor.

---

## 2. The rules this design is judged by

1. **Nothing is removed.** Every row of §1 has a new address in §3.7. "Moved into a collapsed section
   or into a window" is allowed; "dropped because it is rarely used" is not.
2. **Collapsed is not hidden.** A collapsed section header carries a live one-line summary, so the
   folded state still answers the question the section exists for (address + login, whether the
   internet path is proven, how much has been served).
3. **A problem opens its own section.** A state that needs the user (the external check ran and
   failed, the service is not running, the worker is unreachable) expands its section automatically
   and once - never re-collapsing something the user has just folded by hand in the same session.
4. **The default window fits a small screen.** 760 x 560 logical, which is 1330 x 980 at 175 % - it
   fits the owner's working area with `ClampToWorkingArea` as a *safety net* rather than as the thing
   that decides the size.
5. **Ten shares fit without scrolling at the default size.** The owner's stated working range is the
   sizing target, not an afterthought.
6. **Growing the window feeds the list.** Extra height goes to `lvFolders`; sections keep their
   content height.
7. **State survives a restart** - window size, position, maximised flag, and which sections are open.
8. **Nothing that is only interesting once lives on the main window.** Autostart, the connection cap,
   hosting and the reachability note move into a settings window reachable in one click.
9. **The password is not on screen by default.** Masked, with a reveal toggle; copying never requires
   revealing.
10. **The tray contract is untouched** (the four members listed in §1).

---

## 3. Design

### 3.1 Geometry, and the state that is remembered

| Value | Today | New |
|---|---|---|
| `MinimumSize` | 980 x 700 | **560 x 420** |
| Default `ClientSize` | 1320 x 880 | **760 x 560** |
| Size/position memory | none | persisted (below) |
| `ClampToWorkingArea` in `OnLoad` | yes | **yes - keep it** (it stops being load-bearing, but a restored size from a bigger monitor still needs it) |

New registry values in [ShareSettings.vb](../../../src/FastMediaSorterCompanion/Core/ShareSettings.vb),
same hive and same `ReadInt`/`WriteBool` helpers as `Share_MaxConnections`:

```vb
Public Property WindowX As Integer = -1          ' Share_WindowX; -1 = never saved -> CenterScreen
Public Property WindowY As Integer = -1          ' Share_WindowY
Public Property WindowWidth As Integer = 0       ' Share_WindowWidth  (client, logical px)
Public Property WindowHeight As Integer = 0      ' Share_WindowHeight
Public Property WindowMaximized As Boolean = False ' Share_WindowMaximized
Public Property ExpandedSections As String = ""  ' Share_ExpandedSections; CSV of section keys
```

`ExpandedSections` is **one CSV value, not one flag per section**: a section added later simply is not
in the list and starts collapsed, with no migration and no orphan key.

Restore rules (all failures degrade to the default, never to an unusable window):

- A saved size is clamped to `MinimumSize` and to the working area before it is applied.
- A saved position is used **only if the restored rectangle intersects some screen's working area**;
  otherwise the window centres. (A share manager that opens off-screen because the laptop left the
  docking station is a support ticket.)
- Maximised is restored as `WindowState`, with the restore bounds taken from the saved size.
- Saving happens in `HandleFormClosing`, which already calls `_settings.Save()` (:653-660) - the
  values are read from `RestoreBounds` when maximised, so a maximised session does not overwrite the
  remembered normal size.

### 3.2 `CollapsibleSection` - one new control

New file `src/FastMediaSorterCompanion/Forms/CollapsibleSection.vb`. No new dependency; the Companion
project is SDK-style, so **no `<Compile Include>` entry is needed** (unlike LITE - see CLAUDE.md).

```vb
Public NotInheritable Class CollapsibleSection
    Inherits Panel

    Public Sub New(key As String, title As String)      ' key: the token stored in Share_ExpandedSections
    Public ReadOnly Property Key As String
    Public Property Title As String                      ' never ellipsized - see the layout test, §7
    Public Property Summary As String                    ' the live one-liner shown while collapsed; AutoEllipsis
    Public Property SummaryColor As Color
    Public Property Expanded As Boolean
    Public ReadOnly Property Body As Panel               ' host for the section's controls
    Public Event ExpandedChanged(sender As Object, e As EventArgs)
    Public Sub FlagAttention(reason As String)           ' expands once (rule 3 of §2) and tints the summary
End Class
```

Behaviour and mechanics that must not be traded away:

- **Header** = a full-width, focusable, flat button-like row: chevron (`▸`/`▾`, code-drawn like the
  existing glyphs in `BuildGlyphs`, so it re-renders on `OnDpiChanged`), bold title, then the summary
  right-aligned with `AutoEllipsis`. Click anywhere on the header toggles.
- **Keyboard**: `TabStop = True`, `Space`/`Enter` toggle, `Left`/`Right` collapse/expand. The header
  exposes its state through `AccessibleRole.ButtonDropDown` + an accessible name of
  `title & " - " & summary`, so a screen reader gets the same information a sighted user gets from
  the folded row.
- **Layout**: the section is `AutoSize` + `AutoSizeMode.GrowAndShrink` in a single-column
  `TableLayoutPanel`. Collapsing sets `Body.Visible = False`; the row shrinks to the header height.
  **The window is never resized by a toggle** (rule G4 below), so a toggle can never move the buttons
  under the user's cursor.
- **G4 - who gives up the space.** Expanding takes height from `lvFolders` down to a floor of three
  rows; if the list is already at its floor **and** the window is smaller than the working area, the
  window grows downwards only, capped by the working area; if it cannot grow, the outer column scrolls
  (`AutoScroll`, single `Percent 100` column - the same shape the current right column uses to avoid a
  horizontal scrollbar). Collapsing gives the height back to the list, never to empty space.
- **RTL**: nothing but `RightToLeft = Yes` is used (mirroring the chevron is a `RightToLeft` property
  read, not a `RightToLeftLayout` flag) - see the localization spec's hard rule.

### 3.3 The new layout

Compact state (default size, all sections collapsed, one share):

```
┌ Fast Media Sorter: Share Manager ───────────────────────────────────┐
│ ● Раздача работает - 1 папка, порт 64048     [⚙] [Стоп] [Поделиться]│
├─────────────────────────────────────────────────────────────────────┤
│ Общие папки        [+ Добавить папку..] [Убрать] [Настроить..]      │
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │ ☑ Downloads      Все файлы     C:\Users\sza\Downloads       RO  │ │
│ │                                                                 │ │
│ └─────────────────────────────────────────────────────────────────┘ │
│ ▸ Доступ с телефона            192.168.50.43:64048 - fms            │
│ ▸ Доступ из интернета          проверен - работает                  │
│ ▸ Статистика                   7 подключений, 396 файлов            │
├─────────────────────────────────────────────────────────────────────┤
│ Скопировано в буфер.                            [Справка ▾] Русский │
└─────────────────────────────────────────────────────────────────────┘
```

Structure, top to bottom, all inside one `TableLayoutPanel` (`ColumnCount = 1`, `Percent 100`):

1. **Header strip** (`Dock = Top`, auto-height):
   - a state dot + `lblState`, extended to carry the count and the port:
     `Раздача работает - 3 папки, порт 64048` / `Раздача выключена`. In **Server edition** the same
     line carries the hosting fact (`HostingText.HostModeLine`) as a second, grey sub-line - the one
     piece of §1's row 15 that stays on the main window (rule: never let the main window imply that a
     checkbox governs availability when the service does).
   - `[⚙]` - the new settings window (§3.5). Icon button with a tooltip, `AccessibleName` set.
   - `[Начать раздачу]` / `[Остановить раздачу]` - `btnToggle`, unchanged handler, now a normal-weight
     button in the strip (it is a secondary action: adding a folder already starts the share).
   - `[Поделиться]` - `btnShare` keeps its glyph, bold font and accent position at the right edge.
   - `lblIntro` (§1 row 1) is shown **in place of the state line only while the share list is empty** -
     i.e. exactly on the first run, when it is the instruction, and never afterwards, when it is
     wallpaper. (Open decision D.)
2. **Share list block** - `grpShares` loses its `GroupBox` frame (the header row already names it) and
   keeps `pnlListButtons` above `lvFolders`, both unchanged. `lvFolders` stays `Dock = Fill`; the
   block is the only `Percent 100` row, so all spare height lands here (rule 6).
3. **The three sections** (§3.4), in the fixed order: access, internet, statistics.
4. **Status strip** (`Dock = Bottom`): `lblHint` on the left, a `[Справка ▾]` drop-down on the right
   holding the four link items of §1 row 21, and the language link left visible next to it - it names
   the current language in its own script and is the one control a user who cannot read the UI must
   still find. `progressBar` stays docked above the strip.

**Wide mode (optional, Ф4, open decision A).** At a client width of 1040 logical px and above, the
container switches to two columns - list on the left, the three sections stacked on the right - which
is today's shape, minus the settings clutter. Hysteresis: switch to two columns at >= 1040, back to
one at <= 1000, so a drag along the threshold cannot flicker.

### 3.4 The three sections and their summaries

| Section | `key` | Contains (from §1) | Collapsed summary | Auto-expand (rule 3) |
|---|---|---|---|---|
| **Доступ с телефона** | `access` | rows 6 (all six address rows + copy buttons) | `<LAN address> - <login>`; `-` when not serving | never |
| **Доступ из интернета** | `internet` | rows 7, 8, 9, 10, 11 | the short verdict from `ShareText.AccessStateLine`, in the colour `AccessStateColor` already computes | when `reach.ExternalPortChecked AndAlso Not reach.ExternalPortOpen` (the amber case: the user set something up and it does not answer) |
| **Статистика** | `stats` | row 19 + a new `[Подробнее..]` button opening the existing `Share_Status_Form` (where the counters can also be reset) | `N подключений, M файлов`; hidden entirely when the worker sends no stats, exactly as `UpdateStatsBlock` decides today | never |

Details that carry the design:

- **The summary is refreshed by the same code path as the body.** `ApplyStatusToUi` and the 10 s
  `_statsTimer` set both; a collapsed section is live, not frozen. This is what makes rule 2 true
  rather than aspirational.
- **The password row is masked** (§3.6) but its copy button works while masked.
- **Section 2 keeps `btnTest`'s existing visibility logic** (`RefreshTestButton`) - the button appears
  only when there is an external address to test. A section whose body is empty because nothing is
  running collapses and shows `-`; it is never removed.
- **`lblNetNote`** (row 18) moves to the settings window next to the connection cap, where it explains
  the setting it belongs to.

### 3.5 The new settings window - `Share_Settings_Form`

New file `src/FastMediaSorterCompanion/Forms/Share_Settings_Form.vb`, built in code like every other
Companion dialog: `AutoSize` + `TableLayoutPanel`, `DpiLayout.ApplyAutoScale` as the **last** builder
line, `UiLanguage.ApplyTo(Me)` first, app icon, `CenterParent`.

| Group | Contents | Source |
|---|---|---|
| **Запуск** | `chkAutostart` (+ its packaged/Server-mode tooltips), `chkOpenOnStart` (+ tooltip) | §1 rows 13, 14 |
| **Сеть** | `numMaxConns` + label + tooltip, then `lblNetNote` as the group's footnote | §1 rows 17, 18 |
| **Хостинг** | `lblHosting` (full line) + `btnHosting` -> `Share_Hosting_Form` | §1 rows 15, 16 |

Rules:

- **The handlers move verbatim.** `OnAutostartChanged`, `OnOpenOnStartChanged`, `OnMaxConnsChanged`
  (persist) and `OnMaxConnsCommit` (persist + `PushNetworkPolicyAsync` on `Leave`) keep their split
  semantics; the `_loading` guard idiom moves with them. Nothing about *when* a value is pushed to the
  worker changes.
- **The window reports back.** It exposes `Changed As Boolean` like `Share_Hosting_Form` does, and the
  caller re-reads status when an elevated hosting action changed the machine - reusing
  `OnHostingClicked`'s existing pattern (:1137-1149).
- **It is the natural home for the fixed port** of
  [SPECIFICATION_SHARE_MANUAL_PORT.md](../SPECIFICATION_SHARE_MANUAL_PORT.md) §3.4, which currently
  targets the main window's options row. If that ships after this, its checkbox + spinner go into the
  **Сеть** group; that spec's §4 file table changes by one row and nothing else.
- **Not in this window**: anything about a *specific* share (that is `Share_Root_Params_Form`), the
  server-features gate (`Share_Enable_Form`), or the language (the status strip owns it).

### 3.6 The password, and what a screenshot publishes

The password row renders as `••••••••` with a small reveal toggle in place of the copy button's
neighbour. Reveal is **per session and never persisted**; it re-masks when the section collapses or
the window loses focus.. and the copy button copies the real value in both states. The host key stays
truncated with `AutoEllipsis` as today.

This is a real leak, not a hypothetical: the screenshot that motivated this specification contains a
working SFTP password, and support screenshots are exactly what users send to strangers.

### 3.7 Where everything went - the map

The contract from §2 rule 1. Nothing may be missing from the right-hand column.

| # | Element | New home |
|---|---|---|
| 1 | `lblIntro` | Header strip, shown only while the list is empty (§3.3, decision D) |
| 2 | `btnShare` | Header strip, right edge, unchanged glyph + accent |
| 3 | Add / +Текущая / Убрать / Настроить | Unchanged, above the list (frame dropped) |
| 4 | `lvFolders` | Unchanged; now the only growing element |
| 5 | `lblState` | Header strip, extended with folder count + port (+ hosting sub-line in Server mode) |
| 6 | Six address rows + copy buttons | Section **Доступ с телефона**; password masked (§3.6) |
| 7, 8 | `lblAccessState`, `lblAccessNext` | Section **Доступ из интернета**; state also feeds the collapsed summary |
| 9 | `btnTest` | Same section, same visibility rule |
| 10 | `lnkRouter` | Same section |
| 11 | `btnGuide` | Same section |
| 12 | `btnToggle` | Header strip |
| 13, 14 | Autostart, open-on-start | `Share_Settings_Form` -> Запуск |
| 15 | `lblHosting` | `Share_Settings_Form` -> Хостинг; **short form stays** in the header in Server mode |
| 16 | `btnHosting` | `Share_Settings_Form` -> Хостинг |
| 17 | `numMaxConns` | `Share_Settings_Form` -> Сеть |
| 18 | `lblNetNote` | `Share_Settings_Form` -> Сеть (as the footnote of the setting it describes) |
| 19 | `pnlStats` | Section **Статистика** + `[Подробнее..]` -> the existing `Share_Status_Form` |
| 20 | `lblHint` | Status strip, unchanged `SetHint` API |
| 21 | Five links | `[Справка ▾]` drop-down; the language link stays visible beside it |
| 22 | `progressBar` | Unchanged, above the status strip |
| 23 | `pnlEnable` | Unchanged - still replaces the whole content when `ServerFeatures.IsEnabled()` is false |
| 24 | `_statsTimer` | Unchanged; now also refreshes collapsed summaries |

### 3.8 What must not change while doing this

- **The two-wizard model.** Level 1 is the list, level 2 is `PackageWizardForm`. This is layout work,
  not a model change.
- **Every handler body.** `OnToggle`, `ApplySharedFoldersAsync`, `EnsureServiceAccessAsync`,
  `PollReachabilityAsync`, `OnItemCheck`/`OnItemChecked` (including the `_suppressCheck`
  double-click guard), `AddShareRow`'s `all_files` default, `RestripeList`, `SetBusy`'s per-control
  enabling and its `RefreshTestButton()` tail comment - all move or stay, none are rewritten.
- **The tray contract** - `ShareFolderFromWakeAsync`, `OpenShareWizardFromTray`, `ServerStateChanged`,
  the `initialFolder` constructor, and the deferred `_openShareWhenReady` flag with its one-shot
  consumption in `OnShownFirst`.
- **The gate.** `ApplyGate` still swaps the whole content for `pnlEnable`; no section, summary or
  settings window may render anything before consent.
- **DPI discipline** - `ApplyAutoScale` last, `ClampToWorkingArea` in `OnLoad`, `LogicalToDeviceUnits`
  for ListView columns and code-drawn glyphs, glyph rebuild in `OnDpiChanged`.
- **IPC, `.fmscfg`, the QR, the worker, the host key, `schemaVersion`.** Untouched.

### 3.9 File decomposition (part of Ф1, not optional)

`MainWindow.vb` is 1386 lines today and this adds sections, summaries and geometry code. The class
becomes `Partial` and splits along the same concern lines LITE's `Main_Form` uses:

| File | Holds |
|---|---|
| `MainWindow.vb` | state, constructor, lifecycle, gate, busy/hint, the tray entry points |
| `MainWindow.Layout.vb` | `BuildUi`, header strip, status strip, the section scaffolding, geometry persistence |
| `MainWindow.Access.vb` | the address rows, value providers, `ApplyStatusToUi`, summaries, reachability |
| `MainWindow.Folders.vb` | list population, add/remove/configure, `ApplySharedFoldersAsync` |

### 3.10 Phases

Each phase is shippable on its own and leaves the window in a working state; the order is chosen so
the riskiest structural move happens while the content is still the old content.

| Phase | Content | Done when |
|---|---|---|
| **Ф1 - the frame** | `MainWindow` becomes `Partial` and splits (§3.9); the new geometry + `Share_*` window values (§3.1); the header strip and the status strip (§3.3 items 1 and 4). Content still laid out as one column, sections not yet introduced. | The window opens at 760 x 560, remembers size/position, and every §1 element is still reachable. |
| **Ф2 - the sections** | `CollapsibleSection` (§3.2) + the three sections with live summaries and the amber auto-expand (§3.4); `Share_ExpandedSections` persistence; the password mask (§3.6). | Acceptance 3, 5, 6, 7, 9. |
| **Ф3 - the settings window** | `Share_Settings_Form` (§3.5); rows 13-18 move out of the main window; the hosting short line stays in the header. | Acceptance 11 + the §11.6 round trip. |
| **Ф4 - polish** | The `[Справка ▾]` drop-down (§3.3 item 4), the intro-line rule (decision D), the wide two-column mode (decision A), `SectionLayoutTests` (§7.8). | Acceptance 8 + the full 13-language sweep. |

The dangerous phase is **Ф1**, not Ф2: the split plus a new layout container is where a handler can
quietly lose its `AddHandler`. Ф1 therefore changes no captions and no behaviour, so any difference a
tester sees in it is a defect by definition.

---

## 4. Files to touch

| Area | File | Change |
|---|---|---|
| New control | `src/FastMediaSorterCompanion/Forms/CollapsibleSection.vb` | §3.2 |
| New window | `src/FastMediaSorterCompanion/Forms/Share_Settings_Form.vb` | §3.5 |
| Main window | [MainWindow.vb](../../../src/FastMediaSorterCompanion/Forms/MainWindow.vb) (+ 3 new partials) | §3.3, §3.7, §3.9 |
| Settings | [Core/ShareSettings.vb](../../../src/FastMediaSorterCompanion/Core/ShareSettings.vb) | six values of §3.1 + their `Load`/`Save` lines |
| Strings | [Localization/Localization.Manager.vb](../../../src/FastMediaSorterCompanion/Localization/Localization.Manager.vb) | the new keys of §5, all 13 columns |
| Status window | [Forms/Share_Status_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Share_Status_Form.vb) | none - it is reused as-is by the `[Подробнее..]` button |
| Hosting console | [Forms/Share_Hosting_Form.vb](../../../src/FastMediaSorterCompanion/Forms/Share_Hosting_Form.vb) | none - the caller moves, the window does not |
| Tray | [TrayContext.vb](../../../src/FastMediaSorterCompanion/TrayContext.vb) | none required; verify the three entry points (§7.10) |
| Tests | `tests/Companion.Tests/SectionLayoutTests.vb` (new) | §7.8 - the 13-language header/summary measurement |
| Docs | `CLAUDE.md` | one clause in the Android Folder Share section when this ships |
| Docs | [ROADMAP_SPECIFICATION_QUEUE.md](../../roadmaps/ROADMAP_SPECIFICATION_QUEUE.md) | one row (added with this spec) |

**No vbproj change** (SDK-style project). **No LITE change** - invariant 8 of the Companion migration
still holds: `grep Companion src/*.vb` surfaces only the launcher.

---

## 5. User-visible copy (RU keys; the tables carry the other 12 languages)

House style: plain hyphen, `..` not `...`, Russian `ё`. The Russian string **is** the key
(`Localization.T`), so these go into `Localization.Manager.vb` with all twelve translations.

### 5.1 Section headers (short on purpose - they are measured, see §7.8)

- `Доступ с телефона` / `Phone access`
- `Доступ из интернета` / `Internet access`
- `Статистика` / `Statistics`

### 5.2 Header strip

- `Раздача работает - {0}, порт {1}` (`TF`, `{0}` = the pluralized folder count) / `Sharing is on - {0}, port {1}`
- `Раздача выключена` / `Sharing is off` (existing key, reused)
- Folder count, three plural forms via `TF`: `{0} папка` / `{0} папки` / `{0} папок`
- Settings button tooltip: `Настройки менеджера..` / `Manager settings..`

### 5.3 Collapsed summaries

- Not serving: `-` (no string)
- Statistics: `{0} подключений, {1} файлов` / `{0} connections, {1} files`
- The internet summary reuses `ShareText.AccessStateLine` verbatim - **no new wording**, so the folded
  and unfolded states cannot drift apart.

### 5.4 New buttons

- `Подробнее..` / `Details..` (statistics -> `Share_Status_Form`)
- `Справка ▾` / `Help ▾` (status-strip drop-down)
- `Показать пароль` / `Show password`, `Скрыть пароль` / `Hide password` (tooltips on the reveal toggle)

### 5.5 Settings window

- Title: `Настройки менеджера` / `Manager settings`
- Groups: `Запуск` / `Startup`, `Сеть` / `Network`, `Хостинг` / `Hosting`
- Every control keeps its existing caption and tooltip key - **no re-translation of moved strings.**

---

## 6. Security, privacy and honesty

- **The password stops being ambient** (§3.6). No new storage, no new transport - only masking at the
  point of display. This is the only security-relevant change in the whole specification.
- **Nothing new is exposed before consent.** The gate overlay still replaces the entire content, and
  sections are built but never populated while `ServerFeatures.IsEnabled()` is false.
- **No elevation moves.** The only elevated actions remain inside `Share_Hosting_Form` and
  `EnsureServiceAccessAsync`; moving the *button* into the settings window does not move the prompt,
  and routine Start/Stop stays plain IPC.
- **Availability is not hidden behind a fold.** In Server edition the hosting line stays on the main
  window (§3.3), because "which host keeps this reachable" is the one administration fact a user
  reads while looking at a running share.
- **Collapsing never suppresses a warning** - rule 3 of §2 plus the amber auto-expand of §3.4.

---

## 7. Acceptance criteria

1. **Default size.** A first run with no saved geometry opens at 760 x 560 logical. At 175 % display
   scaling the window fits the working area **without** `ClampToWorkingArea` having to shrink it
   (assert: `Size` after `OnLoad` equals the scaled default).
2. **The minimum is usable.** `MinimumSize` 560 x 420 survives `RelaxMinimumSize` untouched at 175 %
   (980 x 735 physical), i.e. the user can actually make the window small.
3. **Ten shares, no scrollbar.** With 10 folders in the list and all sections collapsed at the default
   size, every row is visible and `lvFolders` shows no vertical scrollbar.
4. **Nothing lost.** Every row of §1 is reachable in the new UI; walked item by item against §3.7.
5. **Memory.** Resize + move + expand two sections + close + reopen -> same size, same position, same
   two sections open. A saved rectangle that no longer intersects any screen centres instead.
6. **Collapsed is informative.** While serving, with everything collapsed, the three headers show the
   LAN address + login, the access verdict in its colour, and the two counters - and the counters
   change within 10 s of a phone connecting, with no section opened.
7. **A failed external check opens its section** once, and a manual collapse in the same session is
   not overridden.
8. **13 languages.** `SectionLayoutTests` (new, modelled on
   [SettingsLayoutTests](../../../tests/Lite.Tests/SettingsLayoutTests.vb)) measures every section header
   title in all 13 languages at the minimum window width and fails if a title needs more room than it
   gets; summaries must be `AutoEllipsis`, titles must not be. `LocalizationParityTests` and
   `LocalizationCoverageTests` (Companion) stay green.
9. **The password is masked** on open, copy works while masked, reveal does not persist across a
   window reopen.
10. **The tray contract.** With the window open: "Поделиться.." from the tray opens the wizard;
    "share this folder" from LITE adds the folder and opens the wizard; the tray icon state still
    follows `ServerStateChanged`. With the window closed: all three still work through the
    constructor/deferred path.
11. **Server edition.** On a machine where the service is the host, the hosting line is visible on the
    main window without opening anything, and the Hosting console is one click from the settings
    window.
12. **The gate.** Before opt-in, the window shows only `pnlEnable`; after `Share_Enable_Form` returns
    OK, the full layout appears with no restart.
13. **No contract drift.** `git diff` touches no file under `Core/WorkerIpc.vb`,
    `Core/ShareConfigBuilder.vb`, `payload/`, `src/*.vb` (LITE), `publishing/`.
14. **Build gate.** `.\build.ps1` -> 0 errors, 0 new warnings, all three programs; `dotnet test` for
    both test projects green. **No `v*` tag** as part of this work.

---

## 8. Invariant impact

Checked against [SPECIFICATION_ANDROID_FOLDER_SHARE.md](SPECIFICATION_ANDROID_FOLDER_SHARE.md) §3
and the Companion migration invariants:

| # | Invariant | Impact |
|---|---|---|
| 1 | One exe, differences at runtime | Untouched - Companion-only, no `#If` seam. |
| 2 | Worker is a sibling payload | Untouched. |
| 3 | Never touch the worker data dir from VB | **Respected** - no new file access at all. |
| 4 | No silent elevation, no service created here | **Respected** - elevated actions stay in the Hosting console; moving its button changes no prompt. |
| 5 | IPC schema 1, mismatch surfaced | Untouched - no IPC field, no new request. |
| 6 | RU + EN.. now 13 languages, house style | §5, plus the new layout test of §7.8. |
| 7 | New `.vb` needs a vbproj entry | N/A for the SDK-style Companion; **nothing is added under `src/*.vb`**, so LITE's hand-maintained `<Compile Include>` list is not involved. |
| 8 | No `v*` tag in this work | Respected (§7.14). |
| 8* | LITE knows nothing about the worker | **Respected** - zero LITE-side changes. |
| - | Store/MSIX behaviour | Unchanged: no new capability, no manifest change, `AutostartManager.IsPackaged()` handling moves with the checkbox verbatim. |

---

## 9. Channel impact

**None.** No installer change, no manifest change, no new dependency, no size change, no worker
re-vendoring. The Store listing screenshots show this window and would ideally be refreshed at the
next listing update, but nothing is invalidated: the app name, features and permissions are identical.

---

## 10. Open decisions

- **A. Two columns on a wide window (§3.3).** Default in this spec: **yes, in Ф4**, threshold
  1040/1000 with hysteresis. Cost is one container swap; the alternative is a single column that
  simply gets a very wide list on a large monitor, which is not wrong, only plain.
- **B. Auto-grow on expand (§3.2 G4).** Default: **yes, downwards only, capped by the working area,
  only when the list is already at its three-row floor.** The alternative (never resize, always
  scroll) is more predictable but makes a first expand on a small window feel broken.
- **C. Links: drop-down vs. a fourth section.** Default: **drop-down in the status strip**, language
  left visible. A fourth collapsible section would be tidier structurally but adds a row to a window
  whose whole point is having fewer.
- **D. The intro line only while the list is empty (§3.3).** Default: **yes**. It is instruction on
  the first run and decoration on every later one. Flip to "always visible" if the owner reads its
  absence as a loss.
- **E. First-run section state.** Default: **all collapsed**, with §3.4's amber auto-expand as the only
  exception. Alternative considered and rejected for now: expand "Доступ с телефона" once, the first
  time a share actually starts, so the user sees what appeared.
- **F. Where the folder count lives.** Default: in the state line (`Раздача работает - 3 папки,
  порт 64048`). Alternative: on the list block's header. The state line wins because it is the line a
  user reads to answer "is anything being shared right now".

---

## 11. Test plan

1. **DPI sweep** at 100 / 125 / 150 / 175 / 200 %: open, resize to the minimum, expand each section,
   drag between two monitors with different scaling (exercises `OnDpiChanged` -> `BuildGlyphs` +
   `ApplyDpiScaledAssets` + the new chevron glyph).
2. **Geometry memory**: resize/move/maximise/restore, close via the X, via the tray Quit, and via a
   kill (a killed process must not leave a corrupt value - the restore path clamps).
3. **Scale of the list**: 0, 1, 10 and 60 folders. At 10 no scrollbar at the default size; at 60 the
   list scrolls and the sections do not move.
4. **Live summaries**: connect a phone while everything is collapsed; the statistics summary changes
   within one 10 s tick, and the access summary follows a stop/start.
5. **Amber path**: force a failing external check (close the router forward) and confirm the internet
   section opens itself once, then stays closed after a manual collapse for the rest of the session.
6. **Settings window round trip**: toggle autostart (verify the HKCU Run value), toggle open-on-start
   (verify a `--tray` launch honours it), change the connection cap (verify `SetNetworkPolicy` is
   pushed on `Leave`, not per tick), open the Hosting console and perform an elevated action, confirm
   the main window re-reads status.
7. **Server edition**: with the service registered and running, confirm the hosting line on the main
   window, that Start/Stop still never elevates, and that the folder-grant prompt still appears when a
   root is inaccessible to LOCAL SERVICE.
8. **Gate**: a machine with `ServerFeatures.IsEnabled()` false - only the overlay; then opt in and
   confirm the full layout without a restart.
9. **Tray**: all three entry points, window open and closed (§7.10).
10. **Language sweep**: all 13 languages over the new strings, plus one RTL language (Arabic) for the
    chevron direction and the header/summary alignment.
11. **Screenshot check**: take a fresh screenshot of a running share and confirm no credential is
    readable in it.

---

## 12. What building it corrected (2026-08-15)

Five places where the design above was incomplete or wrong, recorded next to the text they correct.

**12.1 The list-height computation had to measure the sections' PREFERRED size, not their height
(§3.2 G4).** A `TableLayoutPanel` with no Percent row hands its leftover space to the LAST row, and
the sections are in it. With everything collapsed the sections panel measured 210 px where its
content needed 60, so `RelayoutContent` told the list it had 150 px less than it really did - the
default state of the window, and precisely the state the whole redesign is about. Measured, then
fixed: at the default size the folder list now gets 390 px (a 340 px `ListView`, ~16 rows) instead of
240. The same reason turned `_pnlSections` into `Dock = Top`: in a row that can be over-tall, the
sections must hug the list rather than float in the middle of it.

**12.2 The list row is `Absolute` and recomputed, not `Percent`.** §3.3 says the list block is "the
only `Percent 100` row", which cannot express G4's three-row floor - a percent row simply shrinks to
whatever is left. It is an `Absolute` row whose height is `max(floor, viewport - header - sections)`,
recomputed on every resize, DPI change and section toggle. `AutoSize` + `AutoScroll` on one panel
(the other obvious shape) is a documented WinForms conflict, so the scrolling lives on the root
panel, which is the single-Percent-column shape §3.2 asked for anyway.

**12.3 Growing on expand needs two pixels of slack (§3.2 G4, decision B).** Growing to exactly the
needed height leaves `AutoScroll` on its boundary and it shows a scrollbar for content that fits.

**12.4 The settings window forced a re-read before saving (§3.5).** `Share_Settings_Form` owns
`Share_MaxConnections` / `Share_OpenWindowOnStartup` / `Share_AutostartEnabled` and writes them
through its own `ShareSettings` instance. `MainWindow.HandleFormClosing` already called
`_settings.Save()`, which writes the WHOLE POCO - so its stale copy would have clobbered a change
made minutes earlier in the settings window. It now reloads, stamps only the geometry, and saves.

**12.5 The chevron is painted, not a code-drawn bitmap (§3.2).** Owner-drawing the whole header row
means the chevron scales with the font with nothing to rebuild in `OnDpiChanged`, the title/summary
ellipsis asymmetry is one flag rather than two controls, and the RTL mirror is a `RightToLeft`
property read. For the same reason the settings gear IS a code-drawn bitmap rather than the `⚙`
character: the app swaps its font family per script (Nirmala UI, YaHei), and a family without that
code point would put a substituted box on the one button that has no caption.

**12.6 The Hosting console was merged INTO the settings window, as a third collapsible group
(§3.5).** Owner observation on the finished build: moving the hosting button off the main window made
the path to an elevated action four levels deep - main window, settings, console, UAC - where it had
been three. Since §3.5 also left the settings window as `Share_Hosting_Form`'s *only* caller, the
console did not have to be a window at all. `Share_Settings_Form` is now one page of three
`CollapsibleSection`s (Запуск / Сеть / Хостинг) - the same control and the same rules as §3.2, so the
two windows read as one program - and `Share_Hosting_Form.vb` is gone, its body moved verbatim into
`Share_Settings_Form.Hosting.vb`. The hosting group starts **folded** and the two small ones open, so
the dialog opens at 337 px and grows to 709 only if the console is asked for; its live summary is
`HostingText.HostModeLine` verbatim, so the folded row still answers "who keeps this reachable".
Nothing about what elevates, or about the single auditable helper it goes through, changed.

Two things had to be got right in that move, both about measuring controls that will not measure
themselves. `FitToContent` sizes the dialog from the groups' `PreferredSize` and **never** from
`Control.Visible`: it runs from `OnLoad`, where the form is not on screen yet and every child
therefore reports `Visible = False` (the property is ancestor-aware), which measured the page as empty
and opened the dialog at its 220 px minimum with a scrollbar over content that fits. And the hosting
group is laid out **before** the first measurement, deliberately probe-free (`ApplyHostingState(False)`)
- everything deciding which controls *exist* comes from the SCM and from whether the elevated helper
is installed, both local and cheap - so the dialog is sized for the state it will actually draw
rather than for all nine actions at once. The live worker probe still happens, once, the first time
the group is unfolded.

**12.7 `CollapsibleSection` did not shrink when folded (§3.2).** Found by the merge, but the defect
was in the shared control and the main window had it too. `AutoSize` caches the preferred size, and
HIDING a docked child does not clear that cache - measured, a folded 26 px header kept reporting
398 px. Growing was never affected, because showing a child invalidates the cache on its own, so the
symptom was exactly "collapsing gives nothing back": on the main window `SectionsHeight()` kept
subtracting the height of a section that was no longer drawn, and the list never got its space back -
which is the second half of G4 and the reason to have a fold at all. One `Me.PerformLayout()` in the
`Expanded` setter. Verified on both windows: the main window's list row now goes 390 -> 144 -> 482 px
across expand and collapse, the settings dialog 337 -> 709 -> 337.

Two notes that are not corrections. The new `Share_Window*` values store the WINDOW size (outer
bounds, what `MinimumSize` and `Size` measure) rather than the client size §3.1 names - `RestoreBounds`
is window bounds, and mixing the two would need border arithmetic that changes when the window is
maximised. And `Share_WindowX`/`Y` stay in raw screen px, unscaled: a "logical" screen coordinate has
no meaning on a multi-monitor desktop, which is why the restore validates against
`Screen.AllScreens` instead.
