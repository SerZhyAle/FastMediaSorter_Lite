# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.11, draft (read 2026-10-02, re-read 2026-10-07: the catalog is still at 0.11). Owner: CyrFlip. The transport this product ships is now **declared and permitted**, not excepted: 0.11 section 7 item A admits `WM_COPYDATA` sent with a bounded wait |
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

## The Share Manager's own sentinels (additive, 2026-10-13)

The wake payload set of the Share Manager is this product's own declared format and is bound by the same
transport rules as the folder path (tagged, bounded, strictly decoded, acknowledged on the receiver's own answer).
Besides `::fms-show-window::` it carries `::fms-plain-start::` (a re-run exe), `::fms-broadcast::<path>` and, since
spec 215, **`::fms-add-resource::`** - "show the window and open the guided add flow", an intent with no path in it
(command line form `--add-resource`). It is sent only to a receiver at or above
`ShareActivationPolicy.MinAddResourceVersion` (26.10.13.0); an older or unidentified one gets
`::fms-show-window::`, so no installed build sees a payload it does not know. Nothing in it is staged or shared:
it opens a window, and the consent queue is untouched.

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
