# Specification - slot health and honest failures (.NET 10)

> Status: **Ф1 shipped, Ф2..Ф4 open** (2026-08-14, revision 2). Ф1 - the defect - is in the
> mainline: [src/PathFailure.vb](../../src/PathFailure.vb), `SkipUnreadableFile(kind)` and its
> call sites in [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb), the abandoned
> decode in [Main_Form.LoadingIndicator.vb](../../src/Main_Form.LoadingIndicator.vb), 13 x 2 new
> strings, and [PathFailureTests](../../tests/Lite.Tests/PathFailureTests.vb).
> Evidence: `dotnet test tests/Lite.Tests` green on both legs (353 net10 / 135 net48), both
> viewers rebuilt (`FastMediaSorter_LITE.exe` + `FastMediaSorter_x86.exe`, 0 errors).
> **§10 records what building it corrected in this document** - including a seventh removal
> site §3.8 did not know about, which is the one a network blip reaches first.
>
> Why now: this is the rest of wave 1 of
> [ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md](../roadmaps/ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md) §4 - the
> half that is not the Recycle Bin. Its §6.1 says of the slot-health cluster: **"пять кандидатов об
> одном - свести в одну задачу"**, and that instruction is what this document is. It covers items #5
> (slot reachability), #6 (create the destination folder on the fly) and #7 (read-failure
> classification) of that roadmap's top-15, plus the "honest reason" half of the slot-health entry.
>
> Scope: **the mainline viewer only**. Two new pure modules, one probe module, seams in five shared
> files, one new preference. **No new dependency, no installer change, no Companion change.**
>
> Related: [SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md](SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md)
> (the other half of wave 1; §6.6 there hands this document the "twenty presses into a dead share"
> problem by name), [SPECIFICATION_COPY_ACTIONS_REWORK.md](done/SPECIFICATION_COPY_ACTIONS_REWORK.md)
> (`ExecuteRecipientAction` - the one implementation every slot action already funnels through, which
> is where the refusal belongs), [SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md](SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md)
> (the queue this builds on), [SPECIFICATION_LONG_RUN_STABILITY.md](done/SPECIFICATION_LONG_RUN_STABILITY.md)
> (the auto-skip chain that the read-failure classification steers).

---

## 0. Why this exists

### 0.1 A recipient slot is never validated, at any point in its life

Four places decide what a slot holds. None of them checks anything:

| Where | What it does |
| --- | --- |
| [Main_Form.Lifecycle.vb:677-679](../../src/Main_Form.Lifecycle.vb#L677-L679) | `GetSetting(.., "MoveOn" & z, "")` straight into the array. A path that stopped existing three months ago loads exactly like a good one |
| [Table_Form.vb:357-359](../../src/Table_Form.vb#L357-L359) | `FolderBrowserDialog.SelectedPath` assigned unchecked (fine at that instant, stale by tomorrow) |
| [Table_Form.vb:370-378](../../src/Table_Form.vb#L370-L378) | `CellEndEdit` takes **any** typed string. `\\p7\dwon\2026` is a valid slot |
| [Main_Form.FileOperations.vb:442-445](../../src/Main_Form.FileOperations.vb#L442-L445) | `IsRecipientSlotConfigured` tests `IsNullOrWhiteSpace` and nothing else |

The array itself is ten raw strings ([Common_Module.vb:4](../../src/Common_Module.vb#L4)). There is no
type, no verdict, no timestamp - nothing that could carry the answer to "is slot 3 alive" even if
somebody asked.

### 0.2 A dead slot costs twenty keypresses, not one

The modern queue is deliberately unbounded and **never refuses** ([FileOpQueue.vb](../../src/FileOpQueue.vb)) -
which is right for tempo and wrong for a dead NAS. Press `3` twenty times at sorting speed and the
result is:

- twenty operations queued, each waiting out a full SMB timeout on the worker;
- twenty files **already removed from the list**, because the removal is optimistic
  ([Main_Form.FileOperations.vb:567](../../src/Main_Form.FileOperations.vb#L567)) and the rollback only
  arrives when the timeout does;
- twenty failure lines, one per timeout, each rolling a file back into the list minutes later, in an
  order that has nothing to do with what is on screen.

The user spends that minute sorting into a void. **One probe on the first press turns all of it into
one honest sentence.**

### 0.3 The destination folder is never created

`Directory.CreateDirectory` appears eleven times in `src/` - for the log directory, the OCR cache, the
archive temp store, the translation cache, the runtime downloads. **Not once on a recipient
destination.** So "sort this card into 2026-08/best" requires leaving the loop, creating the folder in
Explorer, coming back, and finding the place in the list again. The mechanism costs one line; its
absence costs the session.

### 0.4 A ten-second network blip shreds the file list

This is the one item here that is a live defect rather than a missing convenience.

Every read failure - all six of them - funnels into `SkipUnreadableFile()`
([Main_Form.MediaLoading.vb:736-824](../../src/Main_Form.MediaLoading.vb#L736-L824)), and its **first
statement removes the file from the list** ([:84](../../src/Main_Form.MediaLoading.vb#L84)):

```vb
Private Sub SkipUnreadableFile()
    Dim removed_At As Integer = RemoveCurrentFileFromList(Current_File_Name)
```

For a genuinely broken JPEG that is correct and deliberate ("a file that will not decode has no
business in the list"). But the same path is reached by `Catch ex As Exception`
([:818-823](../../src/Main_Form.MediaLoading.vb#L818-L823)), which is where an `IOException` from a
dropped SMB session lands. So on a share that hiccups for ten seconds:

1. `Space` -> transport failure -> the file is dropped from the list -> auto-skip jumps to the next;
2. the next one fails the same way, and is dropped too;
3. the chain runs until `AutoSkipPolicy.ShouldContinue` gives up
   ([:163-176](../../src/Main_Form.MediaLoading.vb#L163-L176)) and prints
   **"! Нет читаемых файлов в папке"** - about a folder that is perfectly healthy;
4. the session's list and position are only recoverable by rescanning the directory.

**The application destroys its own view of a healthy folder because it never asks why a read failed.**

### 0.5 The classifier is already written, and already off the UI thread

`ProbeArgument` ([Main_Form.Lifecycle.vb:344-396](../../src/Main_Form.Lifecycle.vb#L344-L396)) already
separates *missing* from *denied* from *transport*, with a retry policy tuned for each, and
`ProcessArgumentAsync` ([:431-441](../../src/Main_Form.Lifecycle.vb#L431-L441)) already runs it on a
worker with a generation check. Two of the three items in this specification are **its second and
third caller**, not new machinery. That is why the roadmap ranks the cluster S..L rather than L.

### 0.6 One correction to the ideas roadmap

Its §6.1 says the failure arrives as `MsgBox("E014 " & ex.Message)` - *"сырой текст исключения,
модально, посреди петли"*. **The modal part is no longer true**: `ReportOperationError`
([Main_Form.vb:646-654](../../src/Main_Form.vb#L646-L654)) writes the status line and the log, and
nothing pops up. What survives is the other half - the user-visible sentence is still the raw
exception message with a diagnostic code in front of it, which is a sentence about .NET and not about
their NAS. This specification fixes the half that is real (§3.7) and does not re-fix the one that was
already done.

---

## 1. Not in scope

- **No fallback destination.** Android redirects a failed target to a spare folder; for a sorter that
  is worse than a refusal, because the file quietly ends up somewhere the user did not choose and will
  not look. Explicit non-goal, and an invariant (§7.6).
- **No slot names or colours** - item #4 of the ideas roadmap, a separate design with its own grid
  columns. §3.5 deliberately avoids touching the grid's column layout so it does not collide.
- **No retry queue, no resume, no "operations pending" store.** A refused operation is refused; the
  user presses the key again.
- **No folder watcher** - that is У-04 in S9 and parked by owner decision.
- **No net48 change** (§5).

---

## 2. Decisions taken up front

| # | Decision | Why |
| --- | --- | --- |
| D1 | **One** classifier turns an exception into a user-visible category, and it is pure | Today the categories exist in `ProbeArgument` and are thrown away everywhere else. A pure `Classify` is testable, and it is the difference between "the list survives a blip" and "it does not" |
| D2 | The health probe is **short**, not `ProbeArgument`'s retry ladder | That ladder can sleep 8 x 250 ms and wait out several SMB timeouts, which is right when the user asked to open a specific file and catastrophic as an answer to "is slot 3 alive". Different question, different probe (§3.3) |
| D3 | A verdict is **cached per destination path**, and a dead slot costs at most one probe per TTL | §0.2. Keyed by path, not by slot index: two slots can share a root, and the F3 panel reads the same table |
| D4 | **Double-press within 2 s = an explicit retry** and forces a re-probe | The user needs a way to say "the NAS is awake now" without a new button, and an accidental repeat must stay free |
| D5 | The destination is created **on the worker**, inside `RunFileOp`, never in `ExecuteRecipientAction` on the UI thread | A `CreateDirectory` on a sleeping share blocks for the full timeout; doing it on the UI thread would hand back the freeze the queue was built to remove. The roadmap says this in as many words |
| D6 | Auto-create makes **the final segment only, and only when the parent exists** | It is the difference between "the folder for this session did not exist yet" and "I typed `\\p7\dwon` and the application built it for me" |
| D7 | A transport or denied failure **never removes a file from the list** | §0.4. This is the whole of Ф1 |
| D8 | A transport failure **stops the auto-skip chain** instead of feeding it | If the transport is down, the next file will fail for the same reason; skipping through a thousand of them is the bug, not the recovery |
| D9 | The refusal is raised in `ExecuteRecipientAction`, the single point every slot route already passes | The precedent and the reason are both in `SPECIFICATION_COPY_ACTIONS_REWORK.md` §3.1: five surfaces, one implementation |

---

## 3. Design

### 3.1 `PathFailure` - the classifier, pure

> **As built it differs in two ways - see §10.4:** it carries an extra member `Content`, and the
> file is shared by both projects rather than modern-only (only the behaviour is fenced).

New file [src/PathFailure.vb](../../src/PathFailure.vb), no I/O:

```vb
''' <summary>What went wrong with a path, in the only vocabulary the UI is allowed to
''' speak. The categories are the ones ProbeArgument already distinguishes - they are
''' being given a name and a second and third caller, not invented here.</summary>
Public Enum PathFailureKind
    None
    Missing        ' FileNotFoundException, DirectoryNotFoundException
    Denied         ' UnauthorizedAccessException, SecurityException
    Transport      ' IOException and everything under it: a dropped SMB session, a sleeping NAS
    Invalid        ' ArgumentException, NotSupportedException, PathTooLongException
    OutOfMemory    ' the file is real and readable, and too big for us to decode
    Unknown
End Enum

Public Module PathFailure
    Public Function Classify(ex As Exception) As PathFailureKind

    ''' <summary>True when the file itself is the problem, so dropping it from the list is
    ''' right. False for transport and denial, where the list is innocent (§0.4).</summary>
    Public Function IsAboutTheFile(kind As PathFailureKind) As Boolean
End Module
```

`IsAboutTheFile` is `True` for `Missing`, `Invalid`, `OutOfMemory` and a decode failure; `False` for
`Transport` and `Denied`. `Unknown` is `True` - it preserves today's behaviour for anything nobody has
seen yet, which is the conservative direction for a category that means "we do not know".

> **Ordering matters:** `UnauthorizedAccessException` derives from `SystemException`, not from
> `IOException`, but `FileNotFoundException` and `DirectoryNotFoundException` **do** derive from
> `IOException`. A `Select Case`-style classifier that tests `IOException` first would report every
> missing file as a transport failure and the list would then never self-clean. The tests pin the
> order.

### 3.2 `SlotHealth` - the verdict and its cache

```vb
Public Enum SlotState
    NotConfigured
    Ready
    WillBeCreated   ' missing leaf, parent reachable, auto-create is on
    Missing
    Denied
    Unreachable     ' Transport
    Invalid
End Enum

Public NotInheritable Class SlotVerdict
    Public Property State As SlotState
    Public Property CheckedUtc As DateTime
    Public Property Detail As String = ""     ' for the log, never for the user
End Class
```

Pure cache policy, in its own module so it can be tested against a clock instead of a NAS:

```vb
Public Module SlotHealthPolicy
    ''' <summary>Should this press pay for a probe? A good verdict is trusted for
    ''' Good_Ttl_Seconds, a bad one for Bad_Ttl_Seconds, and an explicit retry (the same
    ''' slot pressed twice inside Retry_Window_Seconds) always re-probes.</summary>
    Public Function ShouldProbe(nowUtc As DateTime, verdict As SlotVerdict, isRepeatPress As Boolean) As Boolean
End Module
```

Constants, with their reasons: `Good_Ttl_Seconds = 120` (a healthy share does not die every minute,
and re-probing during a fast run costs tempo), `Bad_Ttl_Seconds = 30` (long enough that twenty presses
cost one probe, short enough that a NAS waking up is noticed without a gesture),
`Retry_Window_Seconds = 2` (D4).

### 3.3 The probe itself - short on purpose

```vb
' modern only. Runs on a worker; returns a verdict, touches no form state.
Private Shared Function ProbeSlot(destination As String, allowCreate As Boolean) As SlotVerdict
```

- `Directory.Exists(destination)` -> `Ready`.
- Missing: `Directory.Exists(Path.GetDirectoryName(destination))` -> `WillBeCreated` when
  `allowCreate`, else `Missing`. This is also what implements D6: the parent is the thing that decides.
- Any exception -> `PathFailure.Classify` -> `Denied` / `Unreachable` / `Invalid`.
- **One attempt each, no sleeps, no retry ladder** (D2), the whole thing wrapped in a
  `Task.Run` + `Task.WhenAny(.., Task.Delay(Probe_Timeout_Ms))` with `Probe_Timeout_Ms = 2000`. A
  probe that times out is `Unreachable` - which is the honest answer anyway: a destination that cannot
  answer in two seconds cannot absorb a 200 MB clip at sorting speed either.
- The abandoned task is left to finish on its own; it holds no form state and nothing waits on it.

**When it runs:**

1. Lazily, on the first press of a slot whose verdict is stale (`ShouldProbe`) - this is the one that
   pays for itself twenty times over.
2. Pre-warmed in the background when the settings window closes and when a slot is edited, so the
   common case is warm before the first press.
3. Never on the UI thread, never inside `BuildRecipientsOverlay` (which runs during layout).

### 3.4 The refusal

In `ExecuteRecipientAction` ([Main_Form.FileOperations.vb:462](../../src/Main_Form.FileOperations.vb#L462)),
after the archive check and before anything is released, mutated or queued:

```vb
#If Not NETFRAMEWORK Then
    Dim verdict As SlotVerdict = Await EnsureSlotVerdict(move_Slot_index)
    If verdict.State <> SlotState.Ready AndAlso verdict.State <> SlotState.WillBeCreated Then
        lbl_Status.Text = SlotHealthText(move_Slot_Key, verdict.State)   ' §3.7
        Return
    End If
#End If
```

Nothing is removed from the list, no media is released, no operation is queued. The messages name the
slot **and** the reason:

- *"каталог 3 недоступен: нет связи с сетевой папкой. Нажмите 3 ещё раз, чтобы повторить проверку."*
- *"каталог 3 не найден"* / *"..: нет доступа"* / *"..: недопустимый путь"*

### 3.5 Where the state shows without being asked

- **The recipients overlay** ([Main_Form.RecipientsOverlay.vb:106-114](../../src/Main_Form.RecipientsOverlay.vb#L106-L114))
  builds one row per configured slot. A row whose cached verdict is bad is drawn dimmed with the
  existing colour vocabulary (`ForeColor` to a grey, the tooltip carrying the reason). It uses the
  **cached** verdict only - the overlay never probes, so opening it cannot cost a timeout.
- **The settings grid** shows the same thing as a cell tint plus a tooltip on column 1, and
  **no new column**. That is deliberate: `CellMouseDoubleClick` branches on `e.ColumnIndex = 0`
  ([Table_Form.vb:332-344](../../src/Table_Form.vb#L332-L344)) and `CellEndEdit` reads `Item(1, ..)`
  unconditionally ([:370-378](../../src/Table_Form.vb#L370-L378)), so a third column would need both
  guarded - work that belongs to the slot-names item (#4) which actually wants columns. It is recorded
  here so whoever takes #4 finds the trap already named.

### 3.6 Auto-create, on the worker

```vb
' RunFileOp, before Copy/Move - worker thread, per D5
If op.CreateDestinationFolder Then
    Dim dir_Path As String = Path.GetDirectoryName(op.Destination)
    If Not Directory.Exists(dir_Path) Then Directory.CreateDirectory(dir_Path)
End If
```

`CreateDestinationFolder` is set on the `FileOp` by `ExecuteRecipientAction` when the verdict was
`WillBeCreated` - so the *decision* is made where the policy lives and the *cost* is paid where the
blocking is harmless. A creation failure is an operation failure and travels the existing rollback
path in `FinishFileOp`; the status says the folder could not be created rather than reporting a move
that did not happen.

The preference is `CreateMissingDestination` (default **on**): with it off, a missing destination is
`Missing` and refuses, which is today's behaviour with a better sentence.

### 3.7 Failures speak in categories, not in exception text

`ReportOperationError` keeps its code and its `AppFileLogger` line - the log must stay
copy-pasteable, and the "Send logs to the author" flow depends on it. What changes is the **user's**
sentence: it is built from `PathFailure.Classify(ex)`, and the raw `ex.Message` goes to the log only.

| Kind | The status line |
| --- | --- |
| `Missing` | *"каталог {0} не найден"* |
| `Denied` | *"нет доступа к каталогу {0}"* |
| `Transport` | *"нет связи с каталогом {0}"* |
| `Invalid` | *"недопустимый путь каталога {0}"* |
| `Unknown` | the current wording, with the code - an unrecognised failure must stay loud |

### 3.8 The read path: classify before touching the list

> **This section undercounted the read path - see §10.1-10.3.** There are **seven** places that
> drop a file, not six: `UpdateCurrentFileAndDisplay` removes the entry without ever calling
> `SkipUnreadableFile`, and it is the one a dropped share reaches first. Two of the six also needed
> more than a category to be passed: `File.Exists` swallows the very exception the classifier was
> to read (§10.2), and an abandoned decode looks exactly like an undecodable file (§10.3).

`SkipUnreadableFile` takes the reason:

```vb
Private Sub SkipUnreadableFile(kind As PathFailureKind)
    If Not PathFailure.IsAboutTheFile(kind) Then
        ' The list is innocent: keep the file, stop the chain, say what happened (D7, D8).
        auto_Skip_Chain = 0
        lbl_Status.Text = ReadFailureText(kind, Current_File_Name)
        Return
    End If
    ' .. today's body, unchanged
End Sub
```

The six call sites ([:736-824](../../src/Main_Form.MediaLoading.vb#L736-L824)) pass what they know:
`Missing` for the `File.Exists` branch, a decode failure for the empty-file, `Nothing`-result and
`ArgumentException` branches, `OutOfMemory` for its own branch, and
`PathFailure.Classify(ex)` for the general `Catch`. That last one is the whole fix: an `IOException`
now reports *"нет связи с папкой - файл оставлен в списке"* and the list is intact.

**The auto-skip chain counter is reset, not incremented**, so a blip cannot walk the folder. The user
presses `Space` again when the share is back and the same file loads.

### 3.9 Preferences and localization

One new key on `ModernViewerPreferences`: `CreateMissingDestination As Boolean = True`, one
`AddPreferenceCheck` row in `AddSettingsTransferRows` on the **Files and system** tab, with its
caption, description and hint in the three `Localization.*` tables - 13 values each, `TF` with
placeholders for everything that carries a slot key or a path (§3.4, §3.7, §3.8).

---

## 4. Phases

### Ф1 - the list survives a network blip - **S** - **SHIPPED 2026-08-14**

`PathFailure` + `SkipUnreadableFile(kind)` + the six call sites + the read-failure texts.

**It is first even though this document is called "slot health"**, because it is the only part that is
an active defect: today a healthy folder is destroyed in the session's view by a transient failure,
and the user is told the folder has no readable files.

**Acceptance**

1. Open a folder of 200 files on a share, disable the network adapter, press `Space` five times, enable
   it, press `Space`: the list still has 200 files, the position is where it was, and the file loads.
   (Today: five files are gone from the list and the count is 195.)
2. A genuinely broken JPEG in a local folder is still skipped and still leaves the list - the auto-skip
   behaviour is unchanged for content failures.
3. A folder of 1000 unreachable files does not walk the chain: one message, no skid.
4. `PathFailureTests`: `FileNotFoundException` -> `Missing` **and not** `Transport` (the derivation
   trap of §3.1), `UnauthorizedAccessException` -> `Denied`, `IOException` -> `Transport`,
   `ArgumentException` -> `Invalid`, an unknown type -> `Unknown` -> `IsAboutTheFile = True`.

### Ф2 - a dead slot refuses on the first press - **M**

`SlotHealth` + `SlotHealthPolicy` + `ProbeSlot` + the refusal in `ExecuteRecipientAction` + the overlay
and grid indication.

**Acceptance**

1. Point slot 3 at a share, put the NAS to sleep, press `3` twenty times: **one** probe, twenty instant
   refusals naming the reason, **zero** files removed from the list, zero queued operations, and the
   window never stalls.
2. Wake the NAS, press `3` twice within two seconds: the second press re-probes and the move goes
   through.
3. A slot pointing at a deleted local folder says *not found*, not *no connection*.
4. The recipients overlay shows the dead slot dimmed, with the reason in its tooltip, and opening the
   overlay probes nothing.
5. A slot that is healthy at probe time and dies before the operation runs still fails through the
   existing rollback - the probe is an optimisation, not a guarantee (§6.2).
6. `SlotHealthPolicyTests`: good TTL, bad TTL, the repeat-press window, and the boundary at exactly the
   TTL.

### Ф3 - the destination is created when it is missing - **S**

`CreateDestinationFolder` on `FileOp`, the creation in `RunFileOp`, the `WillBeCreated` verdict, the
preference.

**Acceptance**

1. Slot 4 points at `<share>\2026-08\best`, which does not exist while `2026-08` does: the first press
   creates it and moves the file; the status says the folder was created.
2. Slot 5 points at `\\p7\dwon\x` (the share name is a typo): **nothing is created**, the refusal says
   the path is unreachable.
3. With `CreateMissingDestination` off, case 1 refuses with *not found*.
4. Creating on a sleeping share does not freeze the window (the creation is on the worker).

### Ф4 - configuration and the finish - **S**

Validation feedback at the three configuration entry points (§0.1) - the grid cell, the folder picker,
and a background probe of all ten slots after the registry read - plus the message table of §3.7,
13 languages, CHANGELOG / docs.

**Acceptance**

1. Typing a nonexistent path into the grid tints the cell and shows the reason; **the value is still
   saved** (a slot for a share that is currently down must remain configurable).
2. On startup, ten slots are probed in the background without delaying the first image.
3. Localization parity and coverage tests green; the layout test covers the new settings row.

---

## 5. The seams

| Shared file | Change | Fence |
| --- | --- | --- |
| [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb) | `SkipUnreadableFile(kind)` and its six call sites | The parameter is added on both builds with a default that reproduces today's behaviour; only the `Transport`/`Denied` branch is `#If Not NETFRAMEWORK`. **This is the one place where letting the fix into net48 would be defensible** - it is a genuine data-integrity bug, not a refinement - and it is still fenced, because the maintenance policy is explicit and the x86 build is frozen. Named here so the choice is visible rather than accidental |
| [Main_Form.FileOperations.vb](../../src/Main_Form.FileOperations.vb) | The refusal in `ExecuteRecipientAction`, `CreateDestinationFolder` on `FileOp`, the creation in `RunFileOp` | `#If Not NETFRAMEWORK` |
| [Table_Form.vb](../../src/Table_Form.vb) | Cell tint + tooltip only, **no column change** | `#If Not NETFRAMEWORK` |
| [Main_Form.RecipientsOverlay.vb](../../src/Main_Form.RecipientsOverlay.vb) | Dimmed row + tooltip for a bad cached verdict | `#If Not NETFRAMEWORK` |
| [Main_Form.Lifecycle.vb](../../src/Main_Form.Lifecycle.vb) | The startup pre-warm; `ProbeArgument` itself is **not** modified | `#If Not NETFRAMEWORK` |

New files ([PathFailure.vb](../../src/PathFailure.vb), [SlotHealth.vb](../../src/SlotHealth.vb)) are
whole-file `#If Not NETFRAMEWORK`, and both go into the x86 project's explicit `<Compile Include>`
list in the same commit (`CLAUDE.md`: a new file the old-style project has not been told about is
simply absent from that exe, silently).

---

## 6. Risks and limits

**6.1 The probe costs something on the first press.** Up to `Probe_Timeout_Ms` (2 s) once per TTL, on a
slot that turns out to be dead. Against twenty SMB timeouts it is free; against a healthy slot it is a
`Directory.Exists` that returns in microseconds. The pre-warm (§3.3) removes it from the common case
entirely.

**6.2 A verdict is a snapshot, and the share can die between the probe and the operation.** The probe
is an optimisation over the failure path, never a replacement for it: the existing rollback in
`FinishFileOp` stays exactly as it is, and Ф2's acceptance case 5 tests that it still works.

**6.3 An auto-created folder is not undone** if the operation then fails. A leaf directory left behind
on the destination share is a far smaller surprise than a rollback that deletes a directory - which
might not be empty, and might not have been ours.

**6.4 `Unknown` keeps today's behaviour.** A failure category nobody has seen still drops the file from
the list. That is the conservative direction: it preserves the auto-skip that a corrupt file needs, and
the log line names the exception type so the next unclassified kind can be added deliberately.

**6.5 This does not make the queue bounded.** §6.6 of the Recycle Bin specification points at the same
place: with a slot that is *alive but slow*, twenty presses are still twenty queued transfers. That is
the intended design (tempo first), and the queue depth is already shown. Bounding it is a separate
decision nobody has asked for.

---

## 7. Invariants

1. **A transport or denied failure never removes a file from the list**, in any code path.
2. **Every user-visible failure names a category from `PathFailureKind`**; the raw exception text goes
   to the log only, and `Unknown` is the single exception (it keeps the code, loudly).
3. **`PathFailure.Classify` is the only place an exception becomes a user-visible category** - no
   second `Select Case` over exception types anywhere in `src/`.
4. **The health probe never runs on the UI thread**, never uses `ProbeArgument`'s retry ladder, and is
   bounded by `Probe_Timeout_Ms`.
5. **A dead slot costs at most one probe per `Bad_Ttl_Seconds`**, however many times its key is pressed.
6. **No fallback destination, ever.** A refused operation is refused, never redirected.
7. **Auto-create makes the final segment only, and only when the parent exists.**
8. **The refusal happens before anything is released, mutated or queued** - `ReleaseActiveMedia`, the
   optimistic list removal and `QueueFileOp` are all downstream of it.
9. **The settings grid keeps its two columns**; the state is a tint and a tooltip.
10. **The x86 build is unchanged.**

---

## 8. Acceptance, all together

**Automatic** (`dotnet test tests/Lite.Tests`, green and cited):

- `PathFailureTests` - the classification matrix of §3.1 including the `IOException` derivation trap,
  and `IsAboutTheFile` for every kind.
- `SlotHealthPolicyTests` - good/bad TTL, repeat-press window, TTL boundaries, `NotConfigured`.

**Manual**, on a `.\build.ps1` build: the numbered scenes of §4 (Ф1 1-3, Ф2 1-5, Ф3 1-4, Ф4 1-2), the
network-adapter scene of Ф1 being the one that must be run on the owner's real `\\p7` share rather
than a simulation, plus one x86 run for invariant 10.

---

## 9. What this unblocks

- **The one-off recipient ("Отправить в папку..")** of the ideas roadmap §6.1 needs exactly this
  verdict and this auto-create; without them it would be a `FolderBrowserDialog` in front of the same
  silent failure.
- **Slot names and colours** (#4) wants the grid columns this document deliberately did not take, and
  it wants a state to colour - which is `SlotVerdict`.
- **The duplicate finder** scans thousands of files across shares. It cannot be written responsibly on
  top of a read path that deletes list entries when the share hiccups.
- **The Recycle Bin work** (its §6.6) explicitly defers "twenty presses into a dead share" here, so
  landing this closes that open end.

---

## 10. What building Ф1 corrected in this document (revision 2, 2026-08-14)

Four things. The first is the important one: it means the fix as specified would not have passed
its own acceptance case 1.

**10.1 There is a SEVENTH removal site, and a network blip reaches it FIRST.** §3.8 named the six
call sites inside `SkipUnreadableFile` and treated them as the whole read path. They are not:
`UpdateCurrentFileAndDisplay` ([Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb),
the `Not File.Exists` branch after the archive hook) removes the entry from the list, adjusts the
index and carries on to the next file - **without ever going through `SkipUnreadableFile`**. On a
dropped share that branch fires before the display path is reached, so fixing only the six would
have left acceptance case 1 failing exactly as before. It now asks the same question and returns
with the list intact. The index is deliberately *not* rolled back: the user pressed Next, so moving
on is what they asked for - it is the list that had to survive, not the cursor.

**10.2 `File.Exists` is not evidence, so the folder gets asked.** §3.8 has the `File.Exists` branch
pass `Missing`. But `File.Exists` answers `False` for a deleted file **and** for a share that
stopped answering - it swallows the exception the classifier was going to read. A classifier alone
therefore never sees the transport failure on the path that matters most. `ReadFailure(kind)` closes
this: when a failure looks like it is about the file, the containing folder is asked, and a folder
that no longer answers turns the verdict into `Transport`, because the absence of the file is then
no evidence about the file at all. Cost is one `Directory.Exists` on a path just probed anyway.

**10.3 A decode abandoned at its deadline is a transport failure, not a verdict on the file.**
`LoadImageWithProgress` returns `Nothing` for two unrelated reasons: the decoder refused the bytes,
and the 20-second deadline expired because the read was blocking on a dead SMB session (its own
comment says so). Both used to drop the file. It now reports which, through a `ByRef
PathFailureKind`, and only the first one drops.

**10.4 Two corrections of shape.**
- `PathFailureKind.Content` was added - "the bytes arrived and they are not a picture". §3.8 asks
  three call sites to pass "a decode failure" and the enum had no member for it; `Invalid` means an
  invalid *path* and would have put the wrong sentence in front of the user (invariant 2).
- `PathFailure.vb` is **shared, not `#If Not NETFRAMEWORK`**, because §5's own table has
  `SkipUnreadableFile` take the kind "on both builds" and a parameter cannot be typed by an enum the
  other project does not compile. Only the behaviour is fenced: on net48 the kind is computed and
  ignored, and `FailureKeepsFile` returns `False` unconditionally there, so every legacy status line
  is still written exactly when it was. Invariant 10 holds, and the classifier is now proven on both
  test legs rather than one.

**Not changed:** the acceptance cases, the invariants, and Ф2..Ф4. The scenes of §4 Ф1 1-3 still
have to be run on the owner's real `\\p7` share - the automatic half (point 4) is green and cited
in the status line above, the network-adapter scene is a manual pass and is still open.
