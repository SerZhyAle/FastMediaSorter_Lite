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
  `CustomHotkeys.Canonical` by `InputChordConformanceTests` (since 2026-10-13): 17 of the 34 keyboard rows
  conform, the other 17 are a **recorded table of what the model answers today** - the test fails the day an
  answer moves either way, so the table and the registry exception shrink together; the 18 mouse rows bind
  nothing here and are not run. **Not held, a dated registry exception
  (17 of the 34 keyboard rows of the catalog's token vectors fail when run through `CustomHotkeys`):** the
  modifier order is `Ctrl, Alt, Shift` (rule 1 says `Ctrl, Shift, Alt, Win`); the `Win` modifier and the
  aliases `Meta`, `Super`, `Esc`, `Return`, `Del`, `Ins`, `PgUp`, `PgDn`, `PrtSc`, `PrtScn` are not read; an
  empty token (`Ctrl++F12`, a trailing `+`) is not tolerated; the trigger set is wider than the canonical one
  (arrow keys, `Num +`, the OEM punctuation keys, the browser and media keys - written by name, inert to any
  other reader), which is a gap in the contract's set filed as a proposal to its owner; and an unreadable
  stored token drops the override, so the action returns to its factory key instead of showing "no chord"
  (rule 3's "a row in a user-built table may not fall back to the default").
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
