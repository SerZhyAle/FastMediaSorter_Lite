# Roadmap: the specification queue - what is actually left, and what to build first

Status: **decision aid, revision 8**. Nothing here is a commitment; the ordering in §4 is a
recommendation with its reasoning attached, so it can be argued with.
Date: 2026-08-14 (revision 3 the same day, after waves 0-4 were worked through: the state
columns below are updated, and every claim of "done" has a test run behind it. **Revision 4**, later
still: three specifications were written rather than built - C-1 §3.9, R-1 §3.10 and H-1 §3.11 - so
the queue is 11 items, §6's "there is no specification for the Recycle Bin" is no longer true, and
writing H-1 turned up a live defect that now has its own line in §4. **Revision 5**, end of day:
that defect is fixed - wave 4.5 is done, and building it corrected the specification it came from
in four places, one of which is that the fix as written would not have passed its own acceptance.
**Revision 6**, straight after: wave 5 was opened and **R-1 Ф1 is in** - `DEL` goes to the Recycle
Bin, `Shift+DEL` past it, and every permanent deletion names why it is permanent. **Revision 7**:
**R-1 Ф2 is in** as well - `U` walks a bounded history instead of one operation, covers renames, and
answers for a deletion instead of pretending it never happened. **Revision 8**: **R-1 Ф3 is in** -
that answer is now the file itself, back in its folder and back in the list, so the sorting loop is
genuinely reversible rather than merely apologetic)
Method: every file in [docs/specifications/](../specifications/) read, and its claimed status
**checked against the working tree** rather than trusted. Three claims turned out to be stale.

> **Not to be confused with** [ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md](ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md),
> which is a list of *ideas* mined from the Android sibling - 128 candidates, none of them specified.
> This document covers only work that **already has a written specification**, i.e. work that can be
> started today without a design round. Where the two meet is §6.

Cost scale is shared with the ideas roadmap: **S** - a day or less, **M** - a few days,
**L** - a week and change, **XL** - a subsystem.

---

## 0. The short answer

> **Update 2026-08-14 (later the same day):** the answer below was acted on - `Ф-1` is
> **built, whole**, and the application now has a path that writes pixels. What that
> unblocks (rotate saved to file, a compressed copy into a slot, a video frame into a
> slot) is still unbuilt, and each is now a small job rather than a blocked one. The next
> decision is §4's wave 3 (A-1 Ф0+Ф1, archives as folders) or Ф-2 of the editor.
>
> **Update 2026-08-14 (end of day):** both were taken. Archives open as folders (wave 3),
> and the editor's `Ф-2` turned the window into something to actually use - five tools,
> colour, thickness and undo. **The editor is now a feature rather than a save button**,
> which is the thing §4 said a release should be built around. What is left of that spec is
> text (`Ф-3`) and crop (`Ф-4`); what is left of the archive one is 7z/RAR and the trim.
>
> **Update 2026-08-14 (specification production, rev 4):** the next step taken was writing rather
> than building - the Recycle Bin (§3.10, the one §6 named) and the rest of wave 1 (§3.11).
>
> **The short answer has changed as a result.** If only one thing gets built next, build **H-1 Ф1**
> (§3.11): writing that specification turned up a live defect - a network blip removes one file per
> keypress from the session's file list and then reports a healthy folder as having no readable files.
> It is **S**, it is unit-testable, and it is shipping today. After it, `R-1 Ф1` (the Recycle Bin) is
> the largest remaining item about **losing a file** rather than about convenience - the product
> currently admits that gap in its own delete confirmation ("обратно его уже не уговорить").
>
> **Update 2026-08-14 (rev 5): H-1 Ф1 is built.** A read failure is now classified before anything
> touches the list, and a transport or denial leaves it alone. Building it corrected the
> specification in four places (its new §10) - the load-bearing one being that §3.8 named six
> removal sites and there are **seven**: the one a dropped share reaches first never went through
> `SkipUnreadableFile` at all, so the fix as specified would have failed its own acceptance case 1.
> Also corrected: `File.Exists` swallows the exception the classifier was meant to read, so the
> folder is asked instead; and a decode abandoned at its 20-second deadline is a dead session, not
> a verdict on the file.
>
> **Update 2026-08-14 (rev 6): R-1 Ф1 is built.** `DEL` sends the file to the Recycle Bin where one
> exists, `Shift+DEL` goes past it on purpose, and a permanent deletion always names the reason -
> the user's own gesture, the share, the removable volume, the policy or the quota. The prediction
> was the cheap half; the expensive half was making sure it can never be a *promise*, which is why an
> unclassifiable volume gets the scarier wording rather than the convenient one. Building it
> corrected the specification in four places (its new §10), the load-bearing one being that "one
> deletion route" cannot mean one *method* shared by the viewer and the F3 panel - it means one
> decision and one act, which is what the invariant was actually asking for.
>
> **Update 2026-08-14 (rev 7): R-1 Ф2 is built too.** `U` walks the last 50 operations instead of
> one, covers `F6` renames, returns each file to its old position in the list, and - the part that
> matters more than the depth - **answers for a deletion** instead of saying "no history" about a
> file it watched being destroyed. What building it corrected (its §11) is that the specification's
> own acceptance was unreachable from the design it specified: the history was to live inside
> `Main_Form` as a `List(Of FileOp)`, and `FileOp`/`FileOpKind` were private nested members, so
> `UndoStackTests` could not have existed. The enum moved out, the history became a generic
> `UndoStack(Of T)` plus an `UndoPolicy` table - and "an undo is never itself undoable" stopped being
> a rule someone has to remember and became a property of the table.
>
> **Update 2026-08-14 (rev 8): R-1 Ф3 is built.** `U` takes a recycled file back out of the bin - to
> its folder, to its position in the list, and onto the screen - by reading the bin's own `$I` records
> rather than driving Explorer through COM. The phase turned out to be one message replaced by one
> operation, as predicted; what was NOT predicted is that all four corrections it produced (its new
> §12) are about the few lines between the worker finishing and the user reading a sentence. The
> load-bearing one: the "existing failure branch" the specification routed a denied access through
> would have added a list row for a file still sitting in the Recycle Bin, because that branch exists
> to roll back an optimistic removal a restore never makes.
>
> **Update 2026-08-14 (rev 8, later): E-4 (crop) is built too** - taken on this document's own advice,
> since it is the item the editor spec ranks ★★★ and an editor that draws but cannot cut the edge off
> a picture would be asked about on the first day. The frame is live (dragged, resized by eight grips,
> applied by `Enter`, a double click inside it, or a button that exists only while the frame does), and
> the two things an eye cannot check were split out of the window and tested: the frame's geometry and
> the cut itself, the latter down to pixel-exact placement and transparency surviving unblended.
>
> **Update 2026-08-14 (rev 8, later still): R-1 Ф4 is in, and R-1 is finished** - deleting has its own
> three-value question instead of a share in the blanket "no confirmations" flag, with a switch for the
> bin beside it and an old profile's meaning preserved. The correction worth carrying forward: the F3
> panel *asks* too, so a setting converted only in the viewer would have been silently half-applied in
> the one place a mistake costs three hundred files.
>
> **The short answer is now the release itself** - see §4's closing paragraph. The queue's remaining
> items (E-3, A-1 Ф2..Ф4, H-1 Ф2..Ф4) are each a phase of a spec that already ships something usable,
> and the accumulated unreleased work - an editor that draws, crops and saves, archives as folders, and
> a delete/undo loop that is reversible and says what it is doing - is now considerably more than a
> changelog line.

If only one thing gets built next, build the **image encoder** (`Ф-1` of the editor spec, §3.4).
The application today has **no path that writes pixels to disk at all** - which is why `R` (rotate)
is a lie on disk, why there is no crop, no compressed copy, no markup, and no "save frame". One
phase, machine-checkable acceptance, and it is the prerequisite under the largest cluster of cheap
high-value items in the other roadmap.

Before that, two cheap things close work that is **already shipped but not signed off**: the OCR
overlay acceptance run (§3.1) and the 13-language pre-release pass (§3.2). Both are verification,
not construction.

---

## 1. Ground truth: what the 16 specifications really are

| Specification | Claimed | Actually | What is left |
| --- | --- | --- | --- |
| [SETTINGS_EXPANSION](../specifications/done/SPECIFICATION_SETTINGS_EXPANSION.md) | "проект для реализации" | **shipped** - every registry key in the spec exists in [ModernViewerPreferences.vb](../../src/ModernViewerPreferences.vb) | nothing; **moved to `done/` 2026-08-14** |
| [ZOOM_PAN_CLASSIC](../specifications/SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md) | "план (не начато)" | **Ф-Z0..Ф-Z2 shipped** - `ZoomMath` + tests, NumPad keys, opt-in `WheelZooms` | Ф-Z3 (viewport render), Ф-Z4, Ф-Z5; **status line corrected 2026-08-14** |
| [DOC2HTML_OCR_POSITIONING_EXCHANGE](../specifications/done/SPECIFICATION_DOC2HTML_OCR_POSITIONING_EXCHANGE.md) | closed by its own §8 | **closed** - a record of an exchange, not a work item | nothing; **moved to `done/` 2026-08-14** |
| [LONG_RUN_STABILITY](../specifications/done/SPECIFICATION_LONG_RUN_STABILITY.md) | "реализовано, кроме C-2 и C-3" | accurate; both deferrals are argued in its §6 | C-2 (= S11 У-03(в)) and C-3, both parked on purpose; **moved to `done/` 2026-08-14**, C-2 now counted only under S11 |
| [OCR_OVERLAY_ACCURACY](../specifications/SPECIFICATION_OCR_OVERLAY_ACCURACY.md) | "BlockNeedUserTest" | accurate - S1..S5 in the product, automatic acceptance green (**re-run green 2026-08-14**) | manual points 5, 7, 8, 9, 10; S6 is a separate future spec |
| [THIRTEEN_UI_LANGUAGES](../specifications/SPECIFICATION_THIRTEEN_UI_LANGUAGES.md) | "D частично, G не начат" | accurate | block G, 11 Store locales; **the layout test §7.2 is done 2026-08-14** - it found two shipped defects (§8.3 there) |
| [ARCHIVE_BROWSING](../specifications/SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md) | "план (не начато)" | **Ф0+Ф1 shipped 2026-08-14** - cache + three-line cleanup, ZIP/CBZ as a folder, lazy extraction; 30 tests + a live run | Ф2 (7z/RAR/CBR), Ф3 (cache limits, LRU, the rest of the blocks), Ф4 (menu entries, settings, docs) |
| [IMAGE_EDITOR](../specifications/SPECIFICATION_IMAGE_EDITOR_DOTNET10.md) | "план (не начато)" | **Ф-1 + Ф-2 + Ф-4 shipped 2026-08-14** - encoder, probe, atomic swap, EXIF/orientation, entry point, window; then the toolbar, five tools, colour, thickness and undo; then crop - a live frame with grips, dimming, and a pixel-exact cut; 30 + 30 + 9 + 19 tests | Ф-3 (text) |
| [SHARE_MANUAL_PORT](../specifications/SPECIFICATION_SHARE_MANUAL_PORT.md) | "design / not started" | accurate | everything; needs the worker repo |
| [SHARE_MANAGER_COMPACT_WINDOW](../specifications/done/SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md) | "design / not started" (**written 2026-08-14**, after this revision) | **all four phases shipped 2026-08-15; moved to `done/` the same day** - 760x560 default over a 560x420 floor, geometry and section state remembered; `MainWindow` split into four partials; three collapsible sections with live summaries and the amber auto-expand; the SFTP password masked; a settings window for autostart / connection cap / hosting; the help drop-down and the two-column wide mode; 4 new layout tests, 82 Companion tests green, all three programs rebuilt | nothing but the manual scenes of its §11 (the DPI sweep, a real phone connecting, the Server-edition and RTL passes) |
| [RECYCLE_BIN_AND_UNDO](../specifications/SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md) | "design / not started" (**written 2026-08-14**, after this revision - it is what §6 below asked for) | **all four phases shipped 2026-08-14** - the bin, `Shift+DEL`, the volume classifier, an honest reason on every permanent deletion; a bounded undo history that also covers renames; `U` bringing a recycled file back out of the bin; and the delete question as its own three-value setting beside a switch for the bin; 56 tests, both viewers rebuilt | nothing but the manual scenes (a real share, a mapped drive letter, a USB stick) |
| [SLOT_HEALTH_AND_HONEST_FAILURES](../specifications/SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md) | "design / not started" (**written 2026-08-14**, the rest of wave 1) | **Ф1 shipped 2026-08-14** - the defect fix: `PathFailure` + the seven removal sites + 2 x 13 strings; 14 tests, both viewers rebuilt | Ф2 (slot probe + refusal), Ф3 (auto-create), Ф4 (configuration feedback); the manual `\\p7` scene of Ф1 |
| [STREAMSPLAYER_BROADCAST_RESOURCE](../specifications/SPECIFICATION_STREAMSPLAYER_BROADCAST_RESOURCE.md) | "proposal" | accurate | everything; three repositories |
| [VIEWER_CORE_S11](../specifications/SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md) | "частично реализовано" | accurate | У-03(в), У-01 (the latter re-evaluated *down* by measurement) |
| [VIEWER_CORE_S7](../specifications/SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md), [S8](../specifications/SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md), [S9](../specifications/SPECIFICATION_VIEWER_CORE_S9_MODERN_FOLDER.md), [S10](../specifications/SPECIFICATION_VIEWER_CORE_S10_MODERN_UX.md) | "план" | their **approved** slice was extracted into S11 and built; the rest was never approved | reference material, not a queue; **banner added to each 2026-08-14** |

**Consequence: the backlog is not 16 items, it is 8** - and two of the eight are verification passes
over code that already ships. (Three specifications written later the same day, after this table was
built, make it 11; all three are listed above and are §3.9, §3.10 and §3.11 below.)

---

## 2. Wave 0 - hygiene (an hour, zero risk) - **DONE 2026-08-14**

A queue that lies about its own contents makes every later estimate wrong. Before anything is built:

1. ✅ `SPECIFICATION_SETTINGS_EXPANSION.md` -> `done/`, status line rewritten to "shipped" with the
   evidence (the key list in `ModernViewerPreferences.vb`, plus `Table_Form.ExpandedSettings.vb` for
   the UI half).
2. ✅ `SPECIFICATION_DOC2HTML_OCR_POSITIONING_EXCHANGE.md` -> `done/`; it is an archived exchange and
   its §8 says so itself.
3. ✅ `SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md` - status line corrected to
   "Ф-Z0..Ф-Z2 shipped, Ф-Z3..Ф-Z5 open" so the remaining phase is visible instead of the whole spec
   reading as untouched work.
4. ✅ `SPECIFICATION_LONG_RUN_STABILITY.md` -> `done/` with the two deferrals named in the status line,
   and C-2 cross-linked to S11 У-03(в) - in both directions - so the same work is not counted twice.
5. ✅ `SPECIFICATION_VIEWER_CORE_S7..S10` - one banner each: superseded in their approved part by S11,
   kept as reference.

None of this changes a byte of product code. Every relative link inside a moved file was re-based
(`../../src/` -> `../../../src/`) and every inbound link elsewhere was repointed at `done/`.

---

## 3. The eight real items

### 3.1 O-1. OCR overlay: finish the acceptance run - **S**, value ★★★

S1..S5 are in the product and the automatic acceptance in §9 is green; the manual pass covered
points 1, 1a, 2, 3, 4, 6, 11 across 10 scenes. **Open: 5, 7, 8, 10** (interactive) and **9**, which
needs a run on a pre-change build to compare what the S3 filter now discards.

Nothing to build - this is a session with the built exe and the scene corpus. It is first because
the code is already in users' hands: if the manual pass finds something, the finding is about
shipped behaviour, and every day it waits is a day the answer arrives later.

Risk: it may produce work. That is the purpose.

### 3.2 O-2. Thirteen languages: block G + the layout test - **S/M**, value ★★

Block G (the manual checks of §7.3) is, by the spec's own ordering, a **pre-release gate**, and it
has never run. The layout test of §7.2 (the one that catches a translated string overflowing its
control) is likewise not started, and the parity/coverage tests deliberately do not cover geometry.

This protects an investment that is already made - roughly 4 300 translated values across two
programs - against the one failure mode the existing tests cannot see.

### 3.3 O-3. Store: the remaining 11 locales - **S**, owner-only, value ★★

Block D is "partial" for a reason that will not change: creating 11 listings in Partner Center is a
manual step and cannot be automated. The listing currently claims a 13-language interface in `en-us`
and `ru` only. Order is written down in `publishing/store/LOCALES.md`.

### 3.4 E-1. The image encoder - Ф-1 of the editor spec - **M**, value ★★★ - **DONE 2026-08-14**

> `src/Imaging/ImageEncoder.vb` (interface + `ModernImageSharpEncoder`), the `CanReplaceOriginal`
> probe, EXIF and orientation, tmp+swap, async, and the entry point. **No drawing at all.**
> Acceptance: open and save with no edits -> the file is equivalent to the original, EXIF intact,
> orientation unbroken.

**This is the highest-leverage item in the whole queue**, and the reason is not the editor. The
application has an `IImageDecoder` seam and no encoder whatsoever, so:

- `R` rotates the *display* and lies about the file (item 14 of the ideas roadmap);
- crop, compressed copy into a slot, markup, "save this video frame into slot 3" (item 13) are all
  blocked on the same missing capability;
- foundation #1 of [ROADMAP_VIEWER_FUTURE_IMPROVEMENTS](ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md) §3 is
  literally this.

It is also the only phase in the queue whose acceptance a machine can check, and the spec puts it
first and alone precisely because it is the dangerous half (it writes over the user's originals).

### 3.5 A-1. Archives as folders - **L** for Ф0+Ф1, another **L** for Ф2..Ф4, value ★★★

Self-contained, modern-only, no dependency on anything else in this queue. Ф0 (temp store +
orphan sweep) and Ф1 (ZIP/CBZ as a virtual folder) are already a shippable feature - CBZ comics
open like a folder, with slideshow, perspective and OCR working unchanged. Ф2 adds 7z/RAR/CBR.

The whole risk lives in §4 of that spec: the disk must return to its previous state, always,
including after a kill. That is why Ф0 is a phase of its own with a test that kills the process.

### 3.6 E-2..E-4. The editor proper - **L**, value ★★ - **E-2 + E-4 DONE 2026-08-14**

Drawing, text, crop. Note the honest limitation the spec states itself: the canvas is always fit,
so on a 6000x4000 photo one screen pixel is five image pixels - enough to circle and annotate, not
enough to retouch.

**E-2 (drawing) is in**: five tools (brush, rectangle and ellipse, outlined and filled), colour with
`ColorDialog` and eight swatches, thickness, Shift for a square or circle, `Ctrl+Z` and an "Undo"
button over a snapshot history bounded by both step count and bytes. The rubber-band preview and the
committed stroke are literally one function called twice, so they cannot drift apart.

**E-4 (crop) is in too**, taken next for the reason this paragraph used to give: it is the one the
spec ranks ★★★, because "cut the rubbish off the edge" is what people open Paint for. The frame is
live - dragged, resized by eight grips, applied by `Enter`, a double click inside it or a button that
exists only while it does - and the two things an eye cannot check were split out of the window and
tested: the frame's geometry ([EditorGeometry](../../src/EditorGeometry.vb), 13 tests) and the cut
itself ([EditorImageOps](../../src/EditorImageOps.vb), 6 tests, including pixel-exact placement and
transparency surviving unblended). It also closed the editor spec's §14.2 risk rather than restating
it: cropping changes the picture's size, so the EXIF `PixelXDimension`/`PixelYDimension` had to stop
describing the old one.

Left: **E-3** (text - a real `TextBox` over the canvas, one line per click, font size in image
pixels).

### 3.7 Z-3. Ф-Z3: zoom as a viewport transform - **M/L**, value ★ (invisible)

Removes the `zoom_Scale = 0` magic value, the geometry swaps and the off-screen guards, and puts the
OCR overlay and the perspective background on one transform instead of each re-deriving geometry
from the PictureBox rectangle.

Real cleanup, no user-visible result. **Not a prerequisite for the editor** - the editor uses only
`ZoomMath.FitFactor` and forbids zoom on its canvas. Do it when a feature needs it, not before.

### 3.8 The parked async remainder - **M**, value ★

- **У-03(в) / C-2** - the folder scan on the UI thread; a cold 15k folder gives ~1.1 s of a still
  window. Needs continuation machinery in the pipeline.
- **C-3** - `MediaPlayer.Stop()` on the UI thread; deferred with a good argument (a file operation
  must wait for the stop anyway).
- **У-01** - the decode pipeline. Measurements on the working share **argued it down**: a step back
  costs 5-60 ms, which is noticeable but not a freeze, and this is the riskiest change of the set.
  Leave parked until a measurement says otherwise.

### 3.9 C-1. The Share Manager's compact window - **M**, value ★★ (Companion)

[SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW](../specifications/done/SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md),
written 2026-08-14. Not a preference: at 150-175 % display scaling the window's scaled `MinimumSize`
exceeds the working area, so the safety net that keeps a window on-screen is currently doing the
sizing. Companion-only, and it touches no worker, no IPC and no LITE code - which is why it can be
taken at any point without colliding with anything else in this queue.

### 3.10 R-1. The Recycle Bin and a loop that can be taken back - **M/L**, value ★★★ - **DONE 2026-08-14 (all four phases)**

[SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10](../specifications/SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md),
written 2026-08-14 - the specification §6 below asked for. Four phases: the bin with an honest
classification of where it does and does not exist (Ф1, **M**), the multi-level undo stack (Ф2,
**S** - the S8 У-08 delta, with the `Stack`-cannot-drop-its-oldest correction), restoring a deleted
file from the bin (Ф3, **M**), and the confirmation split (Ф4, **S**).

Two things make it rank where it does. It is the only open item that is about **data loss** rather
than convenience - `DEL` today is `File.Delete` and no undo covers it. And its load-bearing part is
not the API call but the honesty: on a network share and on a camera card, the two folders this
application is actually opened on, Windows has no Recycle Bin at all, so most of the work is
classifying the target and saying what will really happen. It also unblocks the duplicate finder,
which the ideas roadmap gates on a bin existing first.

**Ф1 is in** ([DeletePolicy.vb](../../src/DeletePolicy.vb),
[DeleteVolumeProbe.vb](../../src/DeleteVolumeProbe.vb), [RecycleBinIo.vb](../../src/RecycleBinIo.vb),
`ExecuteDelete` as the single viewer route, `Shift+DEL`, the F3 panel, 12 x 13 strings, 17 new tests;
`dotnet test` green on both legs - 372 net10 / 135 net48 - and both viewers rebuilt with 0 errors).
The prediction it makes turned out to be the cheap half: the expensive half was making sure it can
never be a promise. Where the classifier is unsure, the volume is `Unknown` and the wording is the
scarier one, because the only failure that costs a user anything is a bin promised and not delivered.
The corrections this cost the specification are its §10 - the load-bearing one being that
"one deletion route" cannot mean one *method* across two surfaces with different lifetimes; it means
one **decision** (`DeletePolicy.Decide`) and one **act** (`RecycleBinIo.DeleteAs`), which is what
invariant 1 was really asking for.

**Ф2 is in too** ([UndoStack.vb](../../src/UndoStack.vb), [FileOpKind.vb](../../src/FileOpKind.vb),
`RecordUndo` in the success branch only, the rename and its undo, the three legacy history fields
fenced under `#If NETFRAMEWORK`, 5 x 13 strings, 17 more tests). Its correction (spec §11) is a
useful one to remember when reading any other specification here: **the acceptance asked for a test
the design made impossible**. The history was to be a `List(Of FileOp)` inside `Main_Form`, and both
`FileOp` and `FileOpKind` were private nested members - so `UndoStackTests` could not have been
written against it. Moving the enum out and making the history a generic `UndoStack(Of T)` plus an
`UndoPolicy` table cost nothing and turned "an undo is never itself undoable" from a rule someone
has to remember into a property of a table with a safe default.

**Ф3 is in as well** ([RecycleBinIndex.vb](../../src/RecycleBinIndex.vb) - the pure `$I` parser and
the matcher - plus `RecycleBinIo.TryRestore`, `FileOpKind.RestoreFromBin` and 5 x 13 strings; 17 more
tests). `U` after a `DEL` puts the file back in its folder, at its old position in the list, and on
the screen. The design decision that paid off is refusing `Shell.Application`: the COM route needs
late binding that `Option Strict On` forbids and a verb name that is **localized**, while the bin's
own `$I` record is a fixed layout that parses in 40 pure lines - so "an unknown record version is
refused rather than guessed at" is a test rather than a hope. What building it corrected (its §12) is
all in the few lines between the worker finishing and the user reading a sentence; the load-bearing
one is that the failure branch the specification routed a denied access through would have added a
list row for a file still sitting in the bin, because that branch exists to roll back an optimistic
removal that a restore never makes.

**Ф4 closed the specification.** Deleting now has its own question - always / only when the file will
not go to the Recycle Bin / never - instead of a share in the blanket "no confirmations" flag, which
could only ask about everything or about nothing; the middle value is the one a triage session wants,
and it was unreachable before. Beside it sits a switch for the bin itself, which needed no branch at
all: it is told to the classifier as `binEnabledBySetting`, and the user reads the same sentence
`Shift+DEL` produces, because it is the same fact - they asked. An old profile keeps its meaning: a
"no confirmations" tick becomes `never`, written back once so the two settings stop depending on each
other. Its corrections (spec §13) include one that would have shipped as a real defect - the F3 panel
also *asks*, so converting only the viewer would have left `never` silencing one surface and not the
other, in the place where a mistake costs three hundred files rather than one.

What is left is the manual scenes of Ф1..Ф4 that need a real share, a mapped drive letter and a USB
stick. The bin half of Ф3's own acceptance was closed against a real Recycle Bin by a one-shot probe
that linked the shipping sources into a console app - fourteen checks including the
two-same-named-files case, all green.

### 3.11 H-1. Slot health and honest failures - **S..M**, value ★★★ for Ф1 - **Ф1 DONE 2026-08-14**

[SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10](../specifications/SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md),
written 2026-08-14 - the rest of wave 1 of the ideas roadmap, five candidates merged into one task
exactly as its §6.1 instructed. Four phases: read-failure classification (Ф1, **S**), the slot
reachability probe (Ф2, **M**), auto-creating the destination (Ф3, **S**), configuration feedback
(Ф4, **S**).

**Ф1 is not a feature, it is a defect fix, and it should be treated as one.** Writing the spec turned
up that every read failure - including an `IOException` from a dropped SMB session - funnels into
`SkipUnreadableFile`, whose first statement removes the file from the list. A ten-second blip on a
share therefore deletes one file per keypress from the session's view and ends with *"! Нет читаемых
файлов в папке"* about a folder that is perfectly healthy; only a rescan gets the list back. The fix
is a pure classifier and one `If`, with tests, and it is **S**.

The rest is the twenty-presses-into-a-dead-NAS problem: the queue never refuses, so a dead slot costs
twenty SMB timeouts and twenty optimistically removed files instead of one honest sentence.

**Ф1 is in** ([PathFailure.vb](../../src/PathFailure.vb), `SkipUnreadableFile(kind)`, the seventh
removal site, the abandoned decode, 2 x 13 strings, 14 tests; `dotnet test` green on both legs -
353 net10 / 135 net48 - and both viewers rebuilt with 0 errors). The interesting part is what it
cost the specification, recorded as its §10: the document named **six** removal sites and there are
**seven** - `UpdateCurrentFileAndDisplay` drops the entry without going through
`SkipUnreadableFile`, and on a dropped share that is the branch that fires *first*. A pure
classifier would also not have been enough on its own: `File.Exists` returns `False` for a deleted
file and for a share that stopped answering, swallowing the exception the classifier was meant to
read, so the containing folder is asked before the file's absence is believed. Both are the same
lesson - the read path had more mouths than the one being fixed.

What is left here is Ф2..Ф4 (the probe and the refusal, auto-create, configuration feedback) and
the one manual scene of Ф1 that needs the owner's real `\\p7` share rather than a simulation.

## 4. Recommended order

| Wave | Items | Why here |
| --- | --- | --- |
| **0** ✅ | §2 hygiene | The queue must stop lying before anything is estimated against it |
| **1** ◐ | O-1, O-2 | Verification of code that already ships; cheapest possible way to find out whether the last two features are actually done. **O-2's automatable half is done** (the §7.2 layout test, 2 defects found); the automatic OCR acceptance is green. What is left in this wave is only what needs a person at the machine: O-1's points 5, 7, 8, 9, 10 and block G |
| **2** ✅ | **E-1 (encoder)** | One phase, machine-checkable, unlocks the largest cluster of cheap items elsewhere. **Done 2026-08-14** - the writing half, the entry point and the editor window; the application can now put pixels on disk, which is what items #13, #14 and the compressed copy in the ideas roadmap were all waiting for |
| **3** ✅ | A-1 Ф0+Ф1 (ZIP/CBZ) | The biggest single piece of new user value that depends on nothing. **Done 2026-08-14** - a CBZ opens like a folder, the cache cleans itself on all three lines, and the security cases (traversal, bomb, password, truncation) are covered by tests rather than by argument |
| **4** ◐ | A-1 Ф2..Ф4, or E-2..E-4 | Whichever the owner wants; they do not interact. **E-2 (drawing) done 2026-08-14** - the owner picked it, and it is what turns the editor from a save button into a feature |
| **4.5** ✅ | **H-1 Ф1** (read-failure classification) | Not a feature - a live defect (§3.11): a network blip shredded the file list of a healthy folder. **Done 2026-08-14** - and it was worth taking first for a second reason nobody could have known in advance: building it found a seventh removal site the specification had missed, which means the same fix taken later, in a hurry, would have shipped half-done |
| **5** ◐ | **R-1 Ф1+Ф2** ✅ (the bin, then multi-level undo), then **H-1 Ф2..Ф4** | The only open item about data loss rather than convenience, and the first one whose absence the product admitted out loud in its own confirmation text. **Both done 2026-08-14** - `DEL` goes to the Recycle Bin, `Shift+DEL` past it, a permanent deletion always names its reason, and `U` walks 50 operations back instead of one. Taking Ф1 first was right for the reason it was chosen: an undo stack that cannot take back `DEL` leaves the most destructive key uncovered. What is left in this wave is **H-1 Ф2..Ф4**, the same file and the same subject |
| **6** ◐ | **R-1 Ф3** ✅ (`U` restores from the bin), **E-4** ✅ (crop), **R-1 Ф4** ✅ (the confirmation split), then E-3 (text), A-1 Ф2..Ф4 | **Three done 2026-08-14**, and R-1 is now closed end to end. Ф3 closed the loop R-1 opened - a deletion is genuinely reversible, and it was indeed one message replaced by one operation (a pure `$I` parser, a matcher and a plain `File.Move`). E-4 was taken next on this table's own advice: it is the item the editor spec ranks highest, and an editor that draws but cannot cut the edge off would be asked about on the first day. The rest of the same specs follow, same rule: they do not interact, so order is preference |
| **7** | Z-3, then the async remainder | Refactors that pay interest, taken when a feature asks for them |

O-3 (Store locales) is owner-only and can happen in parallel with any wave. So is C-1 (§3.9): it is
Companion-only and collides with nothing here.

**Where a release fits.** With E-2, A-1 Ф0+Ф1, H-1 Ф1 and R-1 Ф1..Ф3 all in, the unreleased work is
no longer a changelog line - it is four user-visible capabilities: an editor with tools that can write
a picture back to disk, archives that open like folders, a `DEL` that goes to the Recycle Bin and says
so honestly where it cannot, and a `U` that walks fifty operations back and can undo a deletion. The
pending installer layout fix and the O-1/O-2 findings ride along. The question this paragraph used to
end on - whether crop should be in that release - was answered by building it: **E-4 is in**, so the
editor draws, crops and writes the result back to disk. What is left of that spec is text (E-3), which
is the one thing nobody will call an editor incomplete for.

---

## 5. Deliberately not started

- **S6 of the OCR accuracy spec** (DPI declaration, Otsu, raster detector, per-word geometry).
  Its own §4 says the next step is a **measurement**, not a port, and per-word geometry changes
  `ScoreAttempt`, which decides the language. A separate specification, beginning with numbers.
- **SHARE_MANUAL_PORT.** Cheap and well specified, but the owner's own framing is "мало кто будет
  этим пользоваться", and it needs a build in `P:\windows\fms_companion` plus a re-vendored worker
  binary. **Batch it with the next change that touches the worker anyway** - on its own the
  re-vendoring costs more than the feature.
- **STREAMSPLAYER_BROADCAST_RESOURCE.** XL across three repositories, and it opens an HTTP gateway -
  a new attack surface, gated off by default. This is a product decision about two products, not a
  queue item to pick up between releases.
- **У-01** - see §3.8.

---

## 6. How this relates to the ideas roadmap

[ROADMAP_VIEWER_FUTURE_IMPROVEMENTS](ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md) ranks 15 items by
value/cost. Its top of the list was *not* specified anywhere and would each need a design round -
**five of them now are** (2026-08-14): #1 Recycle Bin and #2 multi-level undo by §3.10, and #5 slot
reachability, #6 creating the destination folder and #7 read-failure classification by §3.11 - and
**#7, #1 and #2 are now built**, not merely specified. What is still
unspecified there: slot names and colours
(#4), clipboard image in/out (#8), player keys (#9), playback speed (#10), the
`.mts/.m2ts/.ts/.vob` and `.flac/.opus/.aac` extension sets (#11).

Two of those - #11 (two lines of extensions) and #6 (create the destination folder on the fly) - are
small enough that they do not need a specification at all and can ride along inside any wave above.

The one place the two documents genuinely converge is **the encoder**: item #14 (rotate saved to
file), #13 (video frame into a slot) and the compressed-copy idea are all downstream of §3.4 here.
That convergence is the strongest argument for building E-1 next.

**#1 (Recycle Bin) deserved naming separately** - it is the loudest gap in the sorting loop, it is
about data loss, and Windows hands the capability over for free. It was the one item worth writing a
specification for rather than picking up, and **that specification now exists**:
[SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10](../specifications/SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md)
(§3.10 above), which also absorbs #2 (multi-level undo, as the S8 У-08 delta) and the separate-
confirmations item, because the three are one story: the loop becomes reversible, or it does not.

Writing it changed one thing about the estimate. "Windows hands the capability over for free" is
true only on a local disk: on a network share and on removable media there is **no Recycle Bin at
all**, and those are precisely the two folders this application is opened on. The bulk of the work
is therefore classifying the target and telling the truth about it, not calling the API - which is
why the spec ranks Ф1 at **M** and puts the classifier in a pure, unit-tested module.

Building it confirmed that estimate exactly. Of the Ф1 work, the shell call is nine lines; the
classifier, the volume probe and the twelve sentences that name the reason are the rest of it. The
tests that matter are not "does it recycle" - they are the two cases where two reasons apply at once
and the order decides which sentence a person reads.
