# Pointer - `INPUT-CHORD` and `INPUT-PARITY`

| | |
| --- | --- |
| **Ids** | `INPUT-CHORD` (how a combination is stored, shown, refused) and `INPUT-PARITY` (the shared action vocabulary with a binding per device) |
| **Version** | 0.3 / 0.3, draft (read 2026-10-02, re-read 2026-10-07: the catalog is still at 0.3 for both). Owner: CyrFlip - the chord notation and the keyboard and mouse columns; the device columns are `shared` with a steward per column |
| **Home** | `input-controls/README.md` in the shared contracts catalog |
| **Role here** | consumer, **partial** (corrected 2026-10-07; the 2026-10-02 pointer said "declared, not implementing", which was wrong): the .NET 10 viewer stores, shows and captures key combinations - the "Keyboard shortcuts" dialog (`src/Hotkeys_Form.vb`) and the model behind it (`src/CustomHotkeys.vb`, `src/Main_Form.CustomHotkeys.vb`), kept in the profile as the `CustomHotkeys` JSON object `{ actionId: "Ctrl+Shift+F5" }`. It is an in-window table: no global hook, no mouse chords, so rules 4, 7 and 10 to 15 bind nothing here. The x86 fallback has none |

## What this repository must do to stay conformant

- **The stored token is rule 1's, or inert (rules 1 to 3).** Held: modifiers and the trigger joined by `+`,
  case-insensitive reading, whitespace around `+` tolerated, a repeated modifier tolerated, two triggers or an
  unknown trigger make the chord unusable and never another chord. The catalog's token vectors are vendored
  (`tests/Lite.Tests/TestData/input-chord-vectors/`, digest asserted first) and run through
  `CustomHotkeys.Canonical` by `InputChordConformanceTests`: **34/34 keyboard rows held on 2026-10-16
  (183 follow-up)**. Canonical notation is separate from assignment policy: it spells Win and OS-reserved
  chords without assigning them. The viewer's `Parse` still refuses those bindings. Empty separators,
  aliases and the Ctrl/Shift/Alt/Win order normalize on save. An unusable, reserved or duplicate stored
  binding becomes the explicit no-shortcut marker, retiring its factory key across the next save/load.
  Unknown action ids are ignored and redundant defaults are omitted. The 18 mouse rows bind nothing here.
  **Remaining vocabulary exception:** existing arrow, `Num +` / `Num 5`, OEM, browser and media trigger
  names remain readable and writable so old viewer profiles and its factory bindings keep working;
  `NumPad5` is inert as the catalog vector requires. The viewer-vocabulary proposal remains with the owner.
- **Refusals (rule 6) are a shorter list than the contract's minimum** (`CustomHotkeys.IsReserved`: F10, F11,
  Esc, the Windows keys, the Menu key, `Alt+Tab`, `Alt+F4`, `Ctrl+Alt+Delete`). `Ctrl+C`, `Shift`-only
  letters, `Shift+Insert`, `Alt+Space` and `Ctrl+Alt+<character>` can be assigned. The purpose of the rule is
  met by the window rather than the list - the one editable control, the path box, takes every key while it
  has the focus (`Main_Form.KeyboardInput.vb`, "key skip - in editing") - and the gap is recorded, not
  claimed closed.
- **One owner per chord (rule 8):** `Hotkeys_Form.CaptureCombo` asks `CustomHotkeys.OwnerOfCombo` and offers
  swap or clear, whether or not the other action is enabled. It does not ask about the *extra* factory keys
  an action answers to (`D` beside `Delete`, `Space` beside `Right`): a binding onto one of those silently
  wins. The capture dialog is modal, so the viewer's own chords are suspended while it is open (rule 14).
- **`INPUT-PARITY`'s keyboard column is partly the viewer's and partly not.** Held where read: `context`
  (`Menu` and `Shift+F10` always open the media menu), `play-pause` on the media key, `Home`/`End`/
  `PageUp`/`PageDown` as first/last/previous/next file, `Esc` leaving full screen. Different by this
  product's design and recorded as an exception: `Space` is
  *next file* (the column says `play-pause` in a player view), `Left`/`Right` step files (the column says
  `seek`), `Alt+Left` is a medium seek back (the column says `back`), `Backspace` is *previous file*, and the
  media keys `Next`/`Previous` are unbound. Every one is remappable in the dialog. Rung 3 of the contract
  (the per-screen keyboard-only walk) has not been run.
- **Do not add a mouse chord, a global hook or a second chord format** without reading the whole contract
  first; the day the viewer persists a chord for another feature, it goes through `CustomHotkeys`, not a
  new parser.
