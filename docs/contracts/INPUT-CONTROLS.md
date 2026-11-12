# Pointer - `INPUT-CHORD` and `INPUT-PARITY`

| | |
| --- | --- |
| **Ids** | `INPUT-CHORD` (how a combination is stored, shown, refused) and `INPUT-PARITY` (the shared action vocabulary with a binding per device) |
| **Version** | 0.5 / 0.5, draft (read 2026-10-30, specification 294: the 2026-10-29 consolidation, items A-H; 0.4 read 2026-10-20, specification 244; the 2026-10-17 consolidation added the shared media-viewer keyboard and more trigger names, the 0.3 text was read 2026-10-02 and 2026-10-07). Owner: CyrFlip - the chord notation and the keyboard and mouse columns; the device columns are `shared` with a steward per column |
| **Home** | `input-controls/README.md` in the shared contracts catalog |
| **Role here** | consumer, **partial** (corrected 2026-10-07; the 2026-10-02 pointer said "declared, not implementing", which was wrong): the .NET 10 viewer stores, shows and captures key combinations - the "Keyboard shortcuts" dialog (`src/Hotkeys_Form.vb`) and the model behind it (`src/CustomHotkeys.vb`, `src/Main_Form.CustomHotkeys.vb`), kept in the profile as the `CustomHotkeys` JSON object `{ actionId: "Ctrl+Shift+F5" }`. It is an in-window table: no global hook, no mouse chords, so rules 4, 7 and 10 to 15 bind nothing here. The x86 fallback has none |

## What this repository must do to stay conformant

- **The stored token is rule 1's, or inert (rules 1 to 3).** Held: modifiers and the trigger joined by `+`,
  case-insensitive reading, whitespace around `+` tolerated, a repeated modifier tolerated, two triggers or an
  unknown trigger make the chord unusable and never another chord. The catalog's token vectors are vendored
  (`tests/Lite.Tests/TestData/input-chord-vectors/`, digest asserted first) and run through
  `CustomHotkeys.Canonical` by `InputChordConformanceTests`: **34/34 keyboard rows held on 2026-10-16
  (183 follow-up; `InputChord` re-run 2026-10-20, exit 0)**. Canonical notation is separate from assignment policy: it spells Win and OS-reserved
  chords without assigning them. The viewer's `Parse` still refuses those bindings. Empty separators,
  aliases and the Ctrl/Shift/Alt/Win order normalize on save. An unusable or reserved stored binding is
  inert (`CustomHotkeys.IsInert`: shown as no shortcut, binds nothing, its factory key retired) and is kept
  as it was stored, so the next save writes it back (specification 294, 2026-10-30: before, it was rewritten
  as `-`); a duplicate becomes the explicit no-shortcut marker; a blank value means no shortcut.
  Unknown action ids are ignored and redundant defaults are omitted. The 18 mouse rows bind nothing here.
  **The 0.4 trigger vocabulary** (the physical arrows, `Numpad0..9` and `NumpadAdd` with its siblings,
  `BracketLeft` / `BracketRight`, `Period`, `Comma`, `Equal`, `Minus`, `Backslash`, the media and volume keys)
  is canonical: a writer emits only canonical spellings, a token a reader does not know is inert and never
  another chord (rule 3), and a save keeps a stored token it cannot read instead of replacing it
  (`VERSIONING` section 4 rule 5). The viewer's older spellings (`Num +`, `Num 5`, OEM, browser and media names)
  stay readable so old profiles and the factory bindings keep working. The vectors file was not regenerated for
  0.4: it holds no 0.4 trigger and still marks `NumPad5` inert; the conformance test reads that row 28 as 0.5
  item B says (`Ctrl+Shift+Numpad5`), and `CustomHotkeysTests` holds one case-insensitive spelling of every
  admitted family through parse, write, load and save until the catalog's regenerated edition arrives. A key
  outside the canonical set (`OemQuotes`, `OemSemicolon`, `Oemtilde`, `OemQuestion`, the `Browser*` keys,
  `Pause`, `Scroll` ..) is still written in its .NET name - a token a conforming reader treats as inert.
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
- **`INPUT-PARITY` 0.4 sets the viewer's default keys.** The shared media-viewer keyboard (`VIEWER-KEYBOARD`,
  incorporated by the 2026-10-17 consolidation) is the normative default of every hardware-keyboard viewer, in
  place of the 0.3 keyboard cells this product was first read against. A default is migrated explicitly and never
  over a remap the user stored; a temporary difference is a dated registry exception. The catalog is at 0.5 / 0.5
  since 2026-10-29. Specification 294 (2026-10-30) found the defaults already moved by specification 252 T-3:
  `Space` plays or pauses and plain `Left` / `Right` seek the medium step (10 s) while LibVLC plays, `F` is
  full screen (`F7` kept), `F4` the picker, `G` the panel (`F3` kept), `,` a frame back; the map stores
  overrides only, so every stored remap keeps its key (held by
  `A_profile_with_remaps_keeps_every_remap_after_the_defaults_moved`). Held where read (2026-10-07):
  `context` (`Menu` and `Shift+F10` always open the media menu), `play-pause` on the media key, `Home`/`End`/
  `PageUp`/`PageDown` as first/last/previous/next file, `Esc` leaving full screen. Rung 3 of the contract (the
  per-screen keyboard-only walk) has not been run.
- **Do not add a mouse chord, a global hook or a second chord format** without reading the whole contract
  first; the day the viewer persists a chord for another feature, it goes through `CustomHotkeys`, not a
  new parser.
