# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.13, draft (read 2026-10-20, specification 244; the text below was written against 0.11, read 2026-10-02 and 2026-10-07). Owner: CyrFlip. The transport this product ships is **declared and permitted**, not excepted: 0.11 section 7 item A admits `WM_COPYDATA` sent with a bounded wait. 0.12 (section 9) lets a product declare commands beyond scenario launch; 0.13 (the 2026-10-17 consolidation) adds the focus-only second start as an additional profile and asks for a test of a hung receiver and of another user or session |
| **Home** | `app-activation/README.md` in the shared contracts catalog |
| **Role here** | consumer - single-instance forwarding in the viewer (`src/Application_Events.vb`, both exes) and the Share Manager (`src/FastMediaSorterCompanion/Program.vb`), and the viewer's wake of the Share Manager (`src/Main_Form.ShareLauncher.vb`) |

## What this repository must do to stay conformant

- **The declared transport is `WM_COPYDATA` (0.11 section 7 A)** with a UTF-8 path or the sentinel
  `::fms-show-window::`, sent only through `SendMessageTimeout` with `SMTO_ABORTIFHUNG` - never plain
  `SendMessage` - with the receiver validating the payload and never trusting it (`CopyDataTransport`
  bounds and parses; `Main_Form.WndProc` acts only on the tagged message). It is part of the installed
  base: old and new builds of both viewer exes and the Share Manager must keep understanding each other.
  The payload is this product's own declared format - rule 4's token/JSON list binds the pipe transport,
  not a window-message one; the parsing invariant is held either way.
- **The mutex names are frozen anchors (0.11 section 7 B).** `FastMediaSorterSingleInstanceMutex` and
  `FastMediaSorterCompanionSingleInstanceMutex` are session-local but do not take the draft's naming
  pattern; section 7 B states naming rules bind new products only. Never rename them.
- **Rule 1 (URI registration) is not applicable:** activation here is the command line and the window
  message alone; the product registers file ProgIDs, not an activation URI scheme. The registry row says
  so.
- **Focus behaviour is a declared product choice (0.11 section 7 C):** a forwarded file does not steal
  focus, and a bare relaunch restores the window; a secondary start with nobody accepting starts
  normally (viewer) or says so instead of starting a second manager (Share Manager).
- Every send is bounded (6 s total, 3 s per attempt) and actually bound - since ticket 040 the wake of the
  Share Manager declares its entry point explicitly, so a delivery is counted only on the receiver's
  answer.
- **Rule 5's mechanism is the one open deviation (registry exception).** The sender allows the receiver to
  take the foreground (`AllowSetForegroundWindow`, `Application_Events.vb` and `Main_Form.ShareLauncher.vb`);
  the contract's `AttachThreadInput` bracket and the `WM_NULL` responsiveness probe before it are not used
  anywhere in the source - the hang the probe guards against cannot arise without the attach, and every
  send already carries `SMTO_ABORTIFHUNG`. Whether that is conformant is the owner's reading; it is recorded
  as a deviation until then.

- **0.13 profile declaration.** Both receivers here are **forwarding profiles**: a second start hands its path
  or sentinel to the running window, so the payload rules above bind them. Neither is a payload-less focus-only
  profile, and neither creates a second window for a file target. A plain second start restores the window.
  Not run: a test against a hung receiver and against another user or session (the 0.13 acceptance evidence), so
  neither is claimed.
- **0.12 section 9 - the clipboard commands.** The Share Manager reads `/clipboard-send` and `/clipboard-get` on its
  command line, compared without regard to case, next to its own `--clipboard-send` / `--clipboard-get` and the
  wake sentinels below (`Program.ClipboardSwitchOf`). Not read: that a command arriving while its module is off
  is answered in words (section 9 B), and that a cold start becomes the normal instance and performs the command
  once the tray is up (section 9 C).

## The Share Manager's own sentinels (additive, 2026-10-13; completed 2026-10-20)

The wake payload set of the Share Manager is this product's own declared format and is bound by the same
transport rules as the folder path (tagged, bounded, strictly decoded, acknowledged on the receiver's own answer).
The set is `::fms-show-window::` (frozen), `::fms-plain-start::` (a re-run exe), `::fms-broadcast::<path>`,
`::fms-schedule::<folder>` (ticket 222), `::fms-add-resource::` and `::fms-add-resource-camera::` (spec 215, no
path in them; command line forms `--add-resource` and `--add-resource-camera`), and `::fms-clipboard-send::` /
`::fms-clipboard-get::` (spec 230; `--clipboard-send` and `--clipboard-get`). Each newer intent is sent only to a
receiver at or above its own `ShareActivationPolicy.Min*Version` (`MinAddResourceVersion`, `MinScheduleVersion`,
`MinClipboardVersion`); an older or unidentified one gets `::fms-show-window::`, so no installed build sees a
payload it does not know. The mirror-image pair runs the other way: the Share Manager hands a phone code to the
viewer as `::fms-phone-code::` or `::fms-phone-code-file::` (`PhoneCodeRelay`), only to a viewer at or above
`MinPhoneCodeVersion`, because an older viewer would take it for a file path. None of these stages or shares
anything: each opens a window or hands a value over, and the consent queue is untouched.

## Current compatibility policy (2026-10-03)

The viewer and Companion senders share `ShareActivationPolicy.MinConsentVersion`
(`26.10.2.0`). A known receiver at or above this version receives a folder proposal;
an older or unidentified receiver receives only `::fms-show-window::`, followed by
advice to add the folder manually. No sender retries an uncertain delivery as an
untagged folder mutation. The Companion sender also maps its newer plain-start marker
to the frozen show-window marker for an old or unknown receiver.

| Pair | Current behavior |
| --- | --- |
| Old viewer / old Companion sender -> new Companion | Strictly validated legacy untagged folders enter the same consent queue as tagged folders. |
| New viewer / new Companion sender -> old or unknown Companion | Wake only; manual-add advice for a folder request. |
| New sender -> new Companion | Tagged bounded delivery; ack 1 means an owned proposal, followed by explicit consent. |
| Old sender -> old Companion | Historical installed behavior; no new safety guarantee is claimed. |

The consent queue holds at most 32 pending paths. A queued or displayed path stays
unique while handling is active, and cancellation remains deduplicated for 10 seconds
after handling completes. Already-listed paths select their row without enabling it or
changing its sharing policy. Disposal prevents late checks from reopening the manager.
The threshold and mixed-build matrix still require owner/runtime acceptance; this
implementation policy does not declare the draft accepted.
