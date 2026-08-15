# Specification - the Recycle Bin, and a loop that can be taken back (.NET 10)

> Status: **all four phases shipped** (2026-08-14, revision 5). `DEL` sends the file to the
> Recycle Bin where one exists, `Shift+DEL` goes past it, and every permanent deletion names its
> reason (Ф1); `U` now walks a bounded history rather than one operation, covers renames as well as
> moves and copies, and answers for a deletion instead of pretending it never happened (Ф2); and that
> answer is now the file itself - `U` brings a recycled file back out of the bin, to its folder and its
> place in the list (Ф3). And the question before a deletion is now its own setting with three values
> rather than a share of the blanket "no confirmations" flag, next to a switch for the bin itself
> (Ф4). 22 `DeletePolicyTests` + 17 `UndoStackTests` + 17 `RecycleBinIndexTests`, the whole suite green
> (429 net10 / 135 net48), both viewers rebuilt with 0 errors. What building each phase corrected in
> this document is §10, §11, §12 and §13.
>
> Why now: [ROADMAP_SPECIFICATION_QUEUE.md](../roadmaps/ROADMAP_SPECIFICATION_QUEUE.md) §6 ends by
> naming this the one item that deserves a specification written rather than picked up -
> *"it is the loudest gap in the sorting loop, it is about data loss, and Windows hands the capability
> over for free - but there is no specification for it in this repository"*. It is #1 and #2 of
> [ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md](../roadmaps/ROADMAP_VIEWER_FUTURE_IMPROVEMENTS.md) §5, and the
> first two lines of its wave 1.
>
> Scope: **the mainline viewer only** ([src/Modern/FastMediaSorter.Modern.vbproj](../../src/Modern/FastMediaSorter.Modern.vbproj)).
> Three new pure modules, one new interop module, seams in five shared files, two new preference keys.
> **No installer change, no new NuGet dependency, no worker/IPC/Companion change, no `.fmscfg` change.**
>
> Related: [SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md](SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md)
> §4 (У-08, the undo stack - this specification is its delta and supersedes it in the approved part),
> [SPECIFICATION_COPY_ACTIONS_REWORK.md](done/SPECIFICATION_COPY_ACTIONS_REWORK.md) (the one
> `ExecuteRecipientAction` every slot action goes through - the same rule is applied here to deletion),
> [SPECIFICATION_SETTINGS_EXPANSION.md](done/SPECIFICATION_SETTINGS_EXPANSION.md) (where the two new
> preference rows land), [SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md](SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md)
> §7 (the single refusal point for file operations inside an archive - unchanged, and this specification
> must not add a second one), [SPECIFICATION_THIRTEEN_UI_LANGUAGES.md](SPECIFICATION_THIRTEEN_UI_LANGUAGES.md)
> (every new string is 13 values, §7.2 layout test).

---

## 0. Why this exists (the code, not the taste)

### 0.1 The most destructive key in the application is a bare `File.Delete`

```vb
' FileManager.vb:127-131
Public Sub DeleteFile(filePath As String)
    If File.Exists(filePath) Then
        File.Delete(filePath)
    End If
End Sub
```

That is the whole of it. `DEL` / `D` ([Main_Form.KeyboardInput.vb:189-191](../../src/Main_Form.KeyboardInput.vb#L189-L191)),
the toolbar button ([Main_Form.FileOperations.vb:428-432](../../src/Main_Form.FileOperations.vb#L428-L432)),
the media context menu, the recipients overlay's delete row and the F3 panel's bulk delete
([Image_Panel_Form.vb:492](../../src/Image_Panel_Form.vb#L492)) all end in `File.Delete`. The
confirmation text is honest about the consequence and says so in as many words:

```vb
' Main_Form.MediaLoading.vb:388
Localization.TF("Вы уверены, что хотите безвозвратно удалить файл '{0}'? Обратно его уже не уговорить.", ..)
```

**The product is built on pressing a key about once a second.** In that loop a miss is not a risk, it
is a certainty; and the application's answer to a miss on the one irreversible key is a sentence
admitting there is no answer.

### 0.2 Undo is one operation deep and never covers a deletion

```vb
' Main_Form.vb:248-253
Dim history_Source_File_Name As String = ""
Dim history_Destination_File_Name As String = ""
Private history_Was_Copy As Boolean
```

Two strings and a flag, overwritten by every operation. Consequences, each visible in the code:

- **Depth 1.** Sort a burst of five frames into the wrong slot, notice at the fifth - `U` returns one.
- **Deletion is not in the history at all.** `FinishFileOp`'s `Delete` branch writes no history
  ([Main_Form.FileOperations.vb:742-744](../../src/Main_Form.FileOperations.vb#L742-L744)), and the
  worker's own branch says so on purpose
  ([:689-694](../../src/Main_Form.FileOperations.vb#L689-L694)). `U` after `DEL` prints
  *"! Нет истории о переносе"*, which is true and useless.
- **Rename is not in the history either.** `RenameCurrentFile`
  ([:359-426](../../src/Main_Form.FileOperations.vb#L359-L426)) touches no history field, so a
  fumbled `F6` is unrecoverable in the same way a delete is.
- The code already knows this is unfinished. [:697](../../src/Main_Form.FileOperations.vb#L697) reads
  `' #todo: check undo from garbage bin`. This specification is the answer to that line.

### 0.3 The load-bearing fact: the two folders a sorter actually opens are the two Windows does not recycle from

This is the part that decides whether the feature is honest or a lie, so it goes before the design.

- **Network shares have no Recycle Bin.** Not "a smaller one" - none. A shell delete with
  `FOF_ALLOWUNDO` on `\\server\share` deletes permanently and reports success. The owner's own
  working set is exactly this: [FileOpQueue.vb:11-17](../../src/FileOpQueue.vb#L11-L17) documents
  sorting `\\p7\_i\output` into `\\p7\down`. **On the owner's primary scenario a Recycle Bin feature
  changes nothing except the wording**, and a wording that claims a bin there would be worse than
  today's text.
- **Removable media do not recycle either.** Windows deletes from a USB stick or a camera card
  permanently by default. "A dump straight off the card" is the second canonical scenario for this
  application.
- **A fixed volume can still be configured not to recycle** (`NukeOnDelete`, group policy, or a file
  larger than the bin's quota).

So the feature is not "call the API". The feature is **classify the target, then say what will
actually happen** - and the classification carries the same weight as the deletion itself. Android
reached the same conclusion independently and shipped `effectiveSoftDelete = useTrash && !isNetwork`
plus a user-visible `delete_trash_unavailable_fallback_to_hard_delete`; that honesty is being
inherited deliberately, the `.trash/` directory it protects is not.

### 0.4 One confirmation flag answers four different questions

`Is_no_request_before_file_operation` ([Common_Module.vb:49](../../src/Common_Module.vb#L49)) is read
in five places for four unrelated decisions: the single-file delete
([Main_Form.MediaLoading.vb:390](../../src/Main_Form.MediaLoading.vb#L390)), the panel's bulk delete
([Image_Panel_Form.vb:475](../../src/Image_Panel_Form.vb#L475)), the panel's bulk move/copy
([:846](../../src/Image_Panel_Form.vb#L846)), the panel's "operation finished" summary box
([:901](../../src/Image_Panel_Form.vb#L901)) and the editor's JPEG re-compression warning
([Image_Editor_Form.vb:375](../../src/Image_Editor_Form.vb#L375)). A single move by `0..9` asks
**nothing, ever**.

The configuration a triage session actually wants - *"never ask about a move, always ask about a
permanent delete"* - **cannot be expressed**. Whoever wants a fast conveyor turns off the only guard
that stands in front of the irreversible key.

---

## 1. What this specification is not

- **Not Android's `.trash/`.** No shadow directory of our own, no cleanup worker, no quota. Windows
  has the mechanism; where it does not (§0.3), the answer is an honest message, not a private
  reimplementation of the Recycle Bin on somebody's NAS.
- **Not redo.** A sorter's redo is pressing the key again. (This is also the roadmap's finding (в).)
- **Not a persisted undo history.** The stack lives in memory and dies with the process - see §6.4.
- **Not the duplicate finder, not read-only folders, not the slot-health probe.** They are separate
  items in the ideas roadmap; the duplicate finder is explicitly gated on this one being built first.
- **Not a net48 change.** The x86 fallback keeps `File.Delete` and its two history fields (§5).

---

## 2. Decisions taken up front

| # | Decision | Why |
| --- | --- | --- |
| D1 | `DEL` / `D` = **to the Recycle Bin** where a bin exists; **`Shift+DEL` = permanently, always** | The Explorer idiom. `Shift+DEL` is free today: the `e.Shift` branch has no `Keys.Delete` case and falls through `Case Else` ([Main_Form.KeyboardInput.vb:139-141](../../src/Main_Form.KeyboardInput.vb#L139-L141)) |
| D2 | Where there is no bin, the delete **still happens**, and the confirmation says it is permanent | Refusing would break the owner's main scenario (§0.3). Today's behaviour on those paths is unchanged; only the text becomes true |
| D3 | The classifier is a **pure function**, the shell call is a thin impure shell around it | House convention: `ZoomMath`, `EditorGeometry`, `OcrOverlayFit`, `AutoSkipPolicy`. It also makes §0.3's matrix machine-checked instead of argued |
| D4 | Restoring from the bin reads the **`$I` index files directly**, it does not drive `Shell.Application` | Locale-independent, no COM, no STA, no `Option Strict Off`, and the parser is a pure function with tests. See §3.6 for the rejected alternative |
| D5 | The shell **deletion** does run through the documented shell API, on a **dedicated STA thread** | Writing a valid `$I`/`$R` pair by hand is not something we should ever do. The queue consumer is a pool thread, i.e. MTA ([FileOpQueue.vb:51](../../src/FileOpQueue.vb#L51)); shell file operations are STA-affine and we will not gamble on "it usually works" |
| D6 | A recycled delete becomes an **undo entry**; a permanent one becomes a **refusal entry** | `U` must never silently do nothing, and must never claim to restore what cannot be restored |
| D7 | The undo store is a **bounded list, oldest dropped first**, depth 50, memory only | The S8 sketch says `Stack(Of FileOp)` with `max_Undo_Depth = 50`, which is not implementable as written - `Stack` cannot drop its oldest element. Corrected here (§3.5) |
| D8 | Entries are pushed **on success only**, from `FinishFileOp` | Today `history_*` is filled *before* the operation runs ([:512-515](../../src/Main_Form.FileOperations.vb#L512-L515)), so a failed move still offers an undo that would "return" a file that never left |
| D9 | Confirmations split into **`ConfirmDelete` (3 values)** and the existing flag for everything else; the old value migrates | §0.4. The split is what makes a fast conveyor and a guarded `DEL` compatible |
| D10 | Every deletion route goes through **one** implementation, exactly as every slot route goes through `ExecuteRecipientAction` | The precedent is [SPECIFICATION_COPY_ACTIONS_REWORK.md](done/SPECIFICATION_COPY_ACTIONS_REWORK.md) §3.1, and the cost of not doing it is the same: five surfaces that drift |

---

## 3. Design

### 3.1 `DeletePolicy` - the classifier, pure

New file [src/DeletePolicy.vb](../../src/DeletePolicy.vb), modern-only (whole file
`#If Not NETFRAMEWORK`). No I/O, no registry, no WinForms - it is handed facts and returns a
decision, which is what makes §0.3's matrix testable without a NAS and a USB stick.

```vb
Public Enum DeleteVolumeKind
    FixedDisk
    Network
    Removable
    Unknown
End Enum

Public Enum DeleteOutcome
    Recycle
    Permanent
End Enum

''' <summary>Why a deletion is permanent. It exists so the confirmation and the status
''' line can name the reason instead of saying "permanently" and leaving the user to
''' guess whether that was their setting or their share.</summary>
Public Enum PermanentReason
    NotPermanent
    UserAsked            ' Shift+DEL, or "use the Recycle Bin" is off
    NoBinOnNetwork
    NoBinOnRemovable
    BinDisabledOnVolume  ' NukeOnDelete / group policy
    FileExceedsBinQuota
    VolumeUnknown
End Enum

Public NotInheritable Class DeleteVolumeFacts
    Public Property Kind As DeleteVolumeKind = DeleteVolumeKind.Unknown
    Public Property BinDisabled As Boolean
    ''' <summary>-1 when unknown. Bytes.</summary>
    Public Property BinQuotaBytes As Long = -1
End Class

Public NotInheritable Class DeleteDecision
    Public Property Outcome As DeleteOutcome
    Public Property Reason As PermanentReason
End Class

Public Module DeletePolicy
    ''' <summary>The whole rule set, in the order the reasons must be reported in.</summary>
    Public Function Decide(facts As DeleteVolumeFacts, fileSizeBytes As Long,
                           binEnabledBySetting As Boolean, forcedPermanent As Boolean) As DeleteDecision
End Module
```

The order of the tests is part of the contract, because it decides which reason the user is shown
when two apply at once (a `Shift+DEL` on a share is *user asked*, not *no bin on network* - the user
does not need to be told about the share when they held Shift on purpose):

1. `forcedPermanent` -> `Permanent / UserAsked`
2. `Not binEnabledBySetting` -> `Permanent / UserAsked`
3. `Kind = Network` -> `Permanent / NoBinOnNetwork`
4. `Kind = Removable` -> `Permanent / NoBinOnRemovable`
5. `Kind = Unknown` -> `Permanent / VolumeUnknown`
6. `BinDisabled` -> `Permanent / BinDisabledOnVolume`
7. `BinQuotaBytes >= 0 AndAlso fileSizeBytes > BinQuotaBytes` -> `Permanent / FileExceedsBinQuota`
8. otherwise -> `Recycle / NotPermanent`

### 3.2 `DeleteVolumeProbe` - the facts, impure and cached

Same file or a sibling; the split is what keeps §3.1 testable.

- **UNC first, `DriveInfo` second.** `New DriveInfo("\\server\share")` throws; a UNC path is
  recognised by `New Uri(path).IsUnc` (or a leading `\\` after `Path.GetFullPath`) and reported as
  `Network` without asking the drive layer at all. A **mapped** drive (`Z:` -> `\\p7\down`) is caught
  by `DriveInfo(root).DriveType = DriveType.Network` - which is exactly why a naive
  `path.StartsWith("\\")` test is not acceptable: the owner's shares are reachable both ways.
- `DriveType.Removable` -> `Removable`. `DriveType.Fixed` -> `FixedDisk`. Anything else, or any
  exception -> `Unknown` (rule 5 then errs towards the honest, scarier text).
- **`BinDisabled`** is best effort, in this order: the per-volume key
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{GUID}\NukeOnDelete`
  (the volume GUID from `GetVolumeNameForVolumeMountPoint`), else the global
  `..\BitBucket\NukeOnDelete`, else the policy `..\Policies\Explorer\NoRecycleFiles`. Absent -> not
  disabled. `BinQuotaBytes` comes from the same per-volume key's `MaxCapacity` (in MB) when present.
- **Cached per volume root** for the session in a `Dictionary(Of String, DeleteVolumeFacts)`, keyed by
  the root (`\\p7\down`, `C:\`). A probe on a dead share must not cost a `DriveInfo` timeout per
  keypress - which is the same failure mode Л-4 of the ideas roadmap describes for the queue.
- **The probe never runs on the UI thread inside the hot path.** It is called once per folder change
  (the folder's own root) and once per unseen destination root, both already off the keypress path.

> **Known limit, stated on purpose:** this is a prediction, and a prediction can be wrong (a quota
> that changed since the probe, an unusual policy layout). §3.6 makes the *undo* path the ground
> truth: if the bin does not actually hold the file, `U` says so plainly rather than pretending.
> The prediction only ever decides the wording, never whether the file is deleted.

### 3.3 One deletion path

Every current route (`DEL`/`D`, the toolbar button, the media menu, the recipients overlay row, the
F3 panel) ends in one implementation, mirroring `ExecuteRecipientAction`:

```vb
' Main_Form.FileOperations.vb (modern seam)
Private Sub ExecuteDelete(paths As IReadOnlyList(Of String), forcedPermanent As Boolean)
```

The flow, unchanged where it already works:

1. Archive mode refuses at the existing single point (`ArchiveModeBlocksFileOperations()`,
   [Main_Form.MediaLoading.vb:182](../../src/Main_Form.MediaLoading.vb#L182)) - **no second refusal
   point is added**.
2. `DeleteVolumeProbe` + `DeletePolicy.Decide` -> a `DeleteDecision` per file.
3. The confirmation (§3.7) is built **from the decision**, so it names what will really happen.
4. `ReleaseActiveMedia()` as today - VLC holds a playing file and the shell will fail on it just as
   `File.Delete` did.
5. A `FileOp` is queued with the new kind, and the list mutation stays optimistic with the existing
   rollback in `FinishFileOp` ([:773-797](../../src/Main_Form.FileOperations.vb#L773-L797)).

```vb
Private Enum FileOpKind
    Copy
    Move
    Delete          ' permanent - what it always was
    RecycleDelete   ' new
    DeleteUndo
    MoveUndo
    RestoreFromBin  ' new (Ф3)
    Rename          ' new (Ф2) - so a rename can be undone
    RenameUndo      ' new (Ф2)
End Enum
```

`RunFileOp` gains the branches; it stays `Private Shared` and still "touches nothing but its
argument" (Л-4 of the ideas roadmap):

```vb
Case FileOpKind.RecycleDelete
    RecycleBinIo.SendToBin(op.Source)     ' §3.4: STA inside
```

### 3.4 The shell call, and the STA thread

```vb
' src/RecycleBinIo.vb - modern only. All shell-affine work lives here.
Friend Module RecycleBinIo

    Friend Sub SendToBin(path As String)
        RunOnSta(Sub()
                     My.Computer.FileSystem.DeleteFile(path,
                         FileIO.UIOption.OnlyErrorDialogs,
                         FileIO.RecycleOption.SendToRecycleBin,
                         FileIO.UICancelOption.ThrowException)
                 End Sub)
    End Sub

    ''' <summary>Runs one shell call on a private STA thread and rethrows what it threw.
    ''' The file-operation queue consumes on a thread-pool thread (FileOpQueue.vb:51), which
    ''' is MTA by definition; the shell's file-operation plumbing is STA-affine. One thread
    ''' per call costs microseconds against a shell call that costs milliseconds, and it
    ''' keeps the queue's single-consumer ordering exactly as it is.</summary>
    Private Sub RunOnSta(work As Action)
End Module
```

`Microsoft.VisualBasic.FileIO` is inbox on `net10.0-windows` - **no new dependency**, which is the
whole reason the roadmap ranks this M and not L. `UIOption.OnlyErrorDialogs` keeps the shell's own
confirmation out of the hot loop (ours is better: it knows what the recipient slot is called and
whether the bin will really take it); `UICancelOption.ThrowException` turns a shell refusal into an
exception the queue already knows how to carry back to the UI thread as a failure.

### 3.5 The undo stack (the S8 delta)

Replaces `history_Source_File_Name` / `history_Destination_File_Name` / `history_Was_Copy` on the
mainline; those three fields survive under `#If NETFRAMEWORK` for the x86 build.

```vb
' modern only
Private ReadOnly undo_Entries As New List(Of FileOp)()
Private Const Max_Undo_Depth As Integer = 50
```

**`List`, not `Stack`** - S8 §4 specifies `Stack(Of FileOp)` with a depth cap, and a `Stack` cannot
drop its oldest element, so the cap as written is unreachable. Push is `Add` + `RemoveAt(0)` past the
cap; undo is `RemoveAt(Count - 1)`.

Rules:

- **Push on success only**, in `FinishFileOp`'s success branch, for `Copy`, `Move`,
  `RecycleDelete`, `Delete` and `Rename`. Never at queue time (D8).
- **An undo never pushes itself.** The inverse operations carry their own kinds
  (`DeleteUndo`, `MoveUndo`, `RestoreFromBin`, `RenameUndo`), and the push branch simply does not
  list them - no reentrancy flag, which is the roadmap's correction (б).
- **What each kind inverts:**

| Pushed | `U` runs | Note |
| --- | --- | --- |
| `Move` | `MoveUndo` (dest -> source) + `InsertFileIntoList(source, ListIndex)` | Exactly today's path |
| `Copy` | `DeleteUndo` (delete the copy) | Exactly today's path. Permanent on purpose: it deletes a copy we just made, and routing it through the bin would fill the bin with our own noise |
| `Rename` | `RenameUndo` (a same-folder `MoveFile` back) + the list entry is rewritten in place | New. `F6` is unrecoverable today |
| `RecycleDelete` | `RestoreFromBin` (§3.6) + `InsertFileIntoList` | New |
| `Delete` (permanent) | **refused, with the reason** | D6. The entry is kept precisely so `U` can say *"этот файл был удалён безвозвратно (сетевой диск) - вернуть его нечем"* instead of *"нет истории"* |

- The entry carries `ListIndex` (already on `FileOp`) plus two new properties used only by the delete
  kinds: `DeletedAtUtc As DateTime` and `PermanentReason As PermanentReason`.
- Status when the list is empty stays the existing *"! Нет истории о переносе"*.
- **The stack never holds an archive path** - `ArchiveModeBlocksFileOperations()` refuses before any
  operation is queued, so there is nothing to push.

### 3.6 Restoring from the bin

**The mechanism.** A recycled file becomes a pair inside `<volume root>\$Recycle.Bin\<user SID>\`:
`$R<token><ext>` (the data, moved, not copied) and `$I<token><ext>` (a small fixed-layout record:
version, size, deletion time as FILETIME, original full path). Restoring is: find the `$I` whose
record names our path and whose timestamp matches the deletion we recorded, move the matching `$R`
back to that path, delete the `$I`.

**Why not `Shell.Application`.** The COM route needs `NameSpace(10)`, `FolderItem2.ExtendedProperty`
and either `InvokeVerb` (whose verb name has been localized) or `MoveHere`. All of it is late binding,
which `Option Strict On` forbids outright - it would need `CallByName` throughout or a per-file
`Option Strict Off`, both against the house rules in `CLAUDE.md`. It also needs STA, is slow to
enumerate a large bin, and is untestable. The `$I` route is managed, ordinal, fast (a directory
listing of small files), and its parser is a **pure function**:

```vb
' src/RecycleBinIndex.vb - modern only, pure.
Public NotInheritable Class RecycleBinRecord
    Public Property IndexPath As String = ""      ' the $I file
    Public Property DataPath As String = ""       ' the matching $R file
    Public Property OriginalPath As String = ""
    Public Property DeletedUtc As DateTime
    Public Property SizeBytes As Long
End Class

Public Module RecycleBinIndex
    ''' <summary>Parses one $I record. Returns Nothing for a version this build does not
    ''' know - an unknown layout is refused, never guessed at.</summary>
    Public Function TryParse(bytes As Byte(), indexPath As String) As RecycleBinRecord

    ''' <summary>Picks the record that belongs to a deletion we made: same full path
    ''' (ordinal-ignore-case), deleted at or after the moment we queued it, newest first.</summary>
    Public Function BestMatch(records As IEnumerable(Of RecycleBinRecord),
                              originalPath As String, deletedAtUtc As DateTime) As RecycleBinRecord
End Module
```

Both halves are unit-testable without a Recycle Bin: the test builds record bytes itself.

**The impure half** (`RecycleBinIo.TryRestore`) enumerates `$I*` in the SID folder of the **original
path's volume** (`WindowsIdentity.GetCurrent().User.Value`), parses, matches, then `File.Move`s the
`$R` back and deletes the `$I`. It is a plain file move - no shell, no STA - and it is what Explorer's
own Restore does.

**It verifies before it reports.** The list is only touched after `File.Exists(originalPath)` is true.
Every failure is named rather than swallowed:

| Case | What `U` says |
| --- | --- |
| No matching record | *the file is no longer in the Recycle Bin (it may have been emptied)* |
| The `$R` is gone | the same message - the bin has been tampered with either way |
| The original folder no longer exists | *the folder it came from no longer exists* - **we do not recreate it**: silently rebuilding a tree the user deleted is a surprise, and this is a viewer, not a repair tool |
| A file already sits at the original path | resolved by the existing `ResolveDestinationCollision`, and the status names the new name |
| Access denied (another user's SID, a bin on a volume we cannot read) | the exception text, through the existing `FinishFileOp` failure branch |

### 3.7 Confirmations, split

New preference `ConfirmDelete` with three values; the existing checkbox keeps everything else.

| Value | Behaviour |
| --- | --- |
| `always` (default) | Every deletion asks |
| `permanentOnly` | A recycled delete goes straight through; a **permanent** one asks. This is the configuration §0.4 says is unreachable today, and it is the one a triage session wants |
| `never` | Nothing asks, including `Shift+DEL`. The user asked for a conveyor and gets one |

**The text is built from the decision, always** (invariant 2). Three shapes, and the reason is named:

- to the bin: *«Удалить файл '{0}' в Корзину?»*
- permanent by the user's own gesture: *«Удалить файл '{0}' безвозвратно, минуя Корзину?»*
- permanent because the target cannot recycle: *«Файл '{0}' будет удалён безвозвратно: на сетевом
  диске Корзины нет.»* (and the removable / policy / quota variants)

**Migration**, once, at first load of a build that has the key: `NoRequestBeforeFileOperation = 1`
becomes `ConfirmDelete = "never"`, `0` becomes `"always"`. Nobody's setting changes meaning behind
their back; anyone who wants the new middle value opts into it.

The legacy checkbox stays as it is on both builds and keeps governing the panel's bulk move/copy and
its summary box. Its caption is deliberately **not** re-worded per build - a shared
`Localization` key cannot say two things - so the new delete row carries the precision in its own
caption and description instead.

### 3.8 The F3 panel

`DeleteSelectedFiles` ([Image_Panel_Form.vb:465-520](../../src/Image_Panel_Form.vb#L465-L520)) is the
application's only multi-file surface and today it calls `File.Delete` in a loop. It moves onto the
same policy and the same executor (invariant 1). Two panel-specific points:

- The decision is computed **once per volume root**, not once per file - a selection of 300 files
  from one folder must not run 300 probes.
- The confirmation is one dialog for the selection, and it names the outcome for that root
  (*"..в Корзину"* / *"..безвозвратно: на сетевом диске Корзины нет"*). A mixed selection across two
  roots (possible only with subfolder scanning) falls back to the permanent wording, because it is
  the true one for at least one file.
- Bulk deletions are **not** pushed onto the undo stack as 300 entries. One entry per bulk operation,
  holding the list of recycled paths, restored as a group. (Cap: a bulk entry counts as one against
  `Max_Undo_Depth`.)

### 3.9 Settings, registry, and where the rows live

Two new keys on `ModernViewerPreferences` ([src/ModernViewerPreferences.vb](../../src/ModernViewerPreferences.vb)),
following the existing string-choice convention so an old profile stays readable:

```vb
Public Property DeleteToRecycleBin As Boolean = True
Public Property ConfirmDelete As String = "always"   ' always | permanentOnly | never
```

Both rows are added in `AddSettingsTransferRows`
([src/Table_Form.ExpandedSettings.vb:78](../../src/Table_Form.ExpandedSettings.vb#L78)) on the
**Files and system** tab, next to the transfer policy rows they belong with -
`AddPreferenceCheck("delete_to_recycle_bin", ..)` and `AddPreferenceChoice("confirm_delete", ..)`.

### 3.10 Localization

Every new string is 13 values in one `Add(..)` row, in the frozen column order
(ru, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh):

- the three confirmation shapes and the six permanent reasons (§3.7) - `TF` with placeholders, never
  concatenation;
- the status lines: *"удалён в Корзину: {0}"*, *"удалён безвозвратно: {0}"*, *"файл восстановлен из
  Корзины: {0}"*, and the four refusal texts of §3.6;
- the two settings rows: caption in `Localization.Registry.vb`, one-line description in
  `Localization.SettingsDesc.vb`, hint in `Localization.SettingsHints.vb`;
- the first-run help line for `Shift+DEL` - added through `ModernHelpLines()` in
  [Main_Form.Localization.vb](../../src/Main_Form.Localization.vb), which returns `""` on net48, so
  the x86 viewer never advertises a key it does not have.

The §7.2 layout test of the 13-languages specification covers the two new settings rows
automatically once they exist; the confirmation dialogs are sized by WinForms and need no entry.

---

## 4. Phases

Each phase is shippable on its own and has an acceptance that does not depend on the next.

### Ф1 - the Recycle Bin, and an honest sentence about it - **M**

`DeletePolicy` + `DeleteVolumeProbe` + `RecycleBinIo.SendToBin` + `FileOpKind.RecycleDelete` +
`ExecuteDelete` as the single route + `Shift+DEL` + the confirmation and status texts + the F3 panel.

**Acceptance**

1. On a local disk, `DEL` on a photo: the file is in the Recycle Bin, Explorer's Restore puts it back
   where it was, and the status line said *"в Корзину"*.
2. On `\\p7\_i\output`, `DEL`: the file is gone, the confirmation said *permanently, there is no
   Recycle Bin on a network drive*, and the status line agrees with the confirmation.
3. The same folder reached through a **mapped** drive letter behaves identically to case 2.
4. On a USB stick: the removable wording, and the file is gone.
5. `Shift+DEL` on a local disk: permanent, with the *user asked* wording - the network reason is not
   mentioned.
6. 20 deletions at full speed on a share: 20 files gone, no dialog storm, no freeze, the queue depth
   is visible; the window stays responsive throughout (this is the STA-thread-per-call check).
7. A playing video is deleted with no `E001`: `ReleaseActiveMedia` still runs first.
8. Inside an archive `DEL` still refuses, from the one existing point.
9. `DeletePolicyTests`: the full 8-rule matrix of §3.1, including both two-reasons-apply cases.

### Ф2 - undo, more than one step deep - **S**

`undo_Entries`, the push in `FinishFileOp`, `Rename`/`RenameUndo`, `U` walking the list, the
`#If NETFRAMEWORK` fence around the three legacy fields.

**Acceptance**

1. Move five files into five different slots, press `U` five times: all five are back, in reverse
   order, each in its original folder and at its original list position.
2. Copy, then `U`: the copy is deleted, the original untouched.
3. Rename with `F6`, then `U`: the old name is back and the list shows it.
4. A **failed** move (destination made read-only mid-flight) pushes nothing: `U` after it undoes the
   operation before it, not the failure.
5. `U` 51 times after 51 operations: the 51st says the history is empty, and nothing throws.
6. `UndoStackTests`: bound, drop-oldest, reverse order, no push from an undo kind, refusal entries.

### Ф3 - `U` brings back a deleted file - **M**

`RecycleBinIndex` (pure) + `RecycleBinIo.TryRestore` + `FileOpKind.RestoreFromBin` + the refusal texts.

**Acceptance**

1. `DEL` on a local disk, then `U`: the file is back in its folder, back in the list at its old
   position, and on screen.
2. `DEL`, empty the Recycle Bin by hand, then `U`: the named *"no longer in the Recycle Bin"* message,
   the list is not touched, nothing throws.
3. `DEL` on a share (permanent), then `U`: the refusal names the reason. It does **not** say "no
   history".
4. Delete two files, restore both with two `U` presses: both land in the right place - i.e. the
   matcher picked by path and time, not by "the newest thing in the bin".
5. Delete, then delete a **same-named** file from another folder, then `U` twice: each returns to its
   own folder.
6. `RecycleBinIndexTests`: parse a v2 record; refuse an unknown version; `BestMatch` picks by path and
   timestamp and ignores a record deleted before our operation.

### Ф4 - the confirmation split and the finish - **S**

`ConfirmDelete` + `DeleteToRecycleBin` + migration + the two settings rows + 13 languages +
CHANGELOG / README / site / `docs/README.md` index, and the `sza:feature-to-site` sweep.

**Acceptance**

1. A profile with `NoRequestBeforeFileOperation = 1` opens the new build: nothing asks, exactly as
   before, and `ConfirmDelete` reads `never`.
2. `permanentOnly`: `DEL` on a local disk is silent, `Shift+DEL` asks, `DEL` on a share asks.
3. `DeleteToRecycleBin = False`: `DEL` behaves as `Shift+DEL` and says so.
4. The parity and coverage localization tests stay green; the §7.2 layout test covers both new rows in
   all 13 languages.

---

## 5. The seams (the answer Л-3 demands)

The ideas roadmap's trap Л-3 says every item touching a **shared** file must state up front whether
the change is fenced or deliberately let into the frozen x86 build. This one is fenced everywhere:

| Shared file | Change | Fence |
| --- | --- | --- |
| [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb) `Mode_Delete` (:381-429) | The branch calls `ExecuteDelete` instead of doing the work inline | `#If Not NETFRAMEWORK` around the new call; the existing body stays as the `#Else` |
| [Main_Form.KeyboardInput.vb](../../src/Main_Form.KeyboardInput.vb) | `Case Keys.Delete, Keys.D` inside the `e.Shift` branch | `#If Not NETFRAMEWORK`, next to the Shift+digit copy cases that are already fenced there (:101-138) |
| [Main_Form.FileOperations.vb](../../src/Main_Form.FileOperations.vb) | New `FileOpKind` members, `ExecuteDelete`, the undo list, the push in `FinishFileOp` | The enum members are additive and harmless in both builds; every branch that creates or consumes them is fenced |
| [Image_Panel_Form.vb](../../src/Image_Panel_Form.vb) | `DeleteSelectedFiles` routes through the policy | `#If Not NETFRAMEWORK` around the new path, the `File.Delete` loop kept as `#Else` |
| [Main_Form.Lifecycle.vb](../../src/Main_Form.Lifecycle.vb) (:622, :1084) | The migration read/write | Modern only; net48 keeps reading and writing `NoRequestBeforeFileOperation` alone |
| [Common_Module.vb](../../src/Common_Module.vb) (:49) | Unchanged | `Is_no_request_before_file_operation` stays exactly what it is - it is still the net48 flag and the migration source |

New files ([DeletePolicy.vb](../../src/DeletePolicy.vb), [RecycleBinIndex.vb](../../src/RecycleBinIndex.vb),
[RecycleBinIo.vb](../../src/RecycleBinIo.vb)) are **whole-file `#If Not NETFRAMEWORK`**, the
`Main_Form.Zoom.vb` / `ZoomMath.vb` precedent.

> **The x86 build still has to be told about them.** The modern project globs `..\**\*.vb`, the
> old-style project carries an explicit `<Compile Include>` list - a new file that nothing references
> is simply absent from the x86 exe, silently. All three go into
> [src/FastMediaSorter.vbproj](../../src/FastMediaSorter.vbproj) in the same commit, as `CLAUDE.md`
> requires; being whole-file fenced, they contribute nothing there but they are on the list.

---

## 6. Risks and honest limits

**6.1 The classifier predicts, it does not measure.** §3.2 already says this. The mitigation is
structural: the prediction only ever chooses **wording**, and `U` (§3.6) reports the truth by looking
for the file. The worst case is a scarier message than necessary, never a lost file the user was told
was safe.

**6.2 The bin is a per-volume, per-user store, and other software empties it.** A cleanup tool, Storage
Sense, or the user emptying the bin between the delete and the `U` all lead to the same named refusal.
This is not a defect to engineer around; it is the deal Windows offers, and the message says so.

**6.3 `$I` layout.** The record format is stable since Vista and versioned; version 2 (Windows 10+)
carries the path length, version 1 does not. `TryParse` refuses anything else and the restore falls
back to the honest refusal. It never writes an `$I`.

**6.4 The stack does not survive a restart, on purpose.** A persisted undo would, after a restart,
offer to "return" a file the user may since have moved, renamed or deleted by other means, from a
position in a list that no longer exists. Once the process is gone the Recycle Bin **is** the history,
and Explorer is better at showing it than we would be.

**6.5 A 200 MB clip and the bin quota.** Recycling is a move within the volume, so it is instant even
for the owner's big files - but only within a volume. Rule 7 of §3.1 catches the "larger than the bin"
case where the shell would hard-delete; if `MaxCapacity` is absent the case falls through to 6.1.

**6.6 One more optimistic queue.** `RecycleDelete` inherits the existing optimistic list mutation and
its rollback, deliberately (Л-4: one path, not a second one). It also inherits its known cost: on a
dead share twenty presses are twenty timeouts. That is the slot-health item's job, not this one's, and
it is named here so the next specification can point at it.

**6.7 A dead check that will look like it belongs to this work.**
[Main_Form.MediaLoading.vb:185](../../src/Main_Form.MediaLoading.vb#L185) refuses `Mode_Delete` while
`FileOperationWorker.IsBusy`. On the mainline that worker is never started (`QueueFileOp` goes to the
queue), so the check is already dead code there. This specification fences it under
`#If NETFRAMEWORK` while it is rewriting the branch - a one-line change, mentioned so it is not read
later as an accidental behaviour change.

---

## 7. Invariants

1. **No mainline code path deletes a file with `File.Delete` directly.** Every route goes through
   `ExecuteDelete` -> `DeletePolicy` -> the queue. (Checkable by `grep` over `src/`, panel included.)
2. **No message claims the Recycle Bin unless the decision for that path was `Recycle`.** The
   confirmation and the status line are both built from the same `DeleteDecision`.
3. **A permanent deletion always names why it is permanent** - the user's own gesture, the share, the
   removable volume, the policy or the quota. "Permanently" alone is not an acceptable message.
4. **The undo store is bounded and drops the oldest entry first**; it never grows without limit.
5. **Only a completed, successful operation is pushed**, from `FinishFileOp` - never at queue time.
6. **An undo operation never pushes an entry** - no ping-pong, no redo.
7. **`U` never silently does nothing.** Either it undoes, or it says which of the named reasons stops
   it.
8. **A restore touches the list only after the file is verified back at its original path.**
9. **No shell file operation runs on a pool thread**; every one goes through `RunOnSta`.
10. **The x86 build is unchanged**: its delete is still `File.Delete`, its undo is still the two
    history fields, and it never advertises `Shift+DEL`.
11. **Archive mode keeps its single refusal point** - this work adds no second one.

---

## 8. Acceptance, all together

**Automatic** (`dotnet test tests/Lite.Tests`, the run must be green and cited in the commit):

- `DeletePolicyTests` - the rule matrix of §3.1, both orders where two reasons apply, and the
  `Unknown` fallback.
- `UndoStackTests` - depth bound, drop-oldest, reverse order, push-on-success-only, no push from an
  undo kind, refusal entries preserved.
- `RecycleBinIndexTests` - v2 parse, v1 parse, unknown version refused, truncated record refused,
  `BestMatch` by path + timestamp, same-name-different-folder disambiguation.
- The existing `LocalizationParityTests` / `LocalizationCoverageTests` stay green with the new strings.

**Manual**, on a build made by `.\build.ps1`, both exes present: the numbered scenes of §4 (Ф1 1-8,
Ф2 1-5, Ф3 1-5, Ф4 1-4), plus one run of the x86 exe proving invariant 10 - `DEL` deletes as it always
did, `Shift+DEL` does nothing, the help text has no line about it.

---

## 9. What this unblocks, and what still needs the owner

**Unblocks.** The duplicate finder of the ideas roadmap §6.1 is explicitly gated on the bin (*"bulk
deletion of duplicates past the Recycle Bin is irresponsible"*). The bulk operations of the F3 panel
become defensible. The "read-only folder" idea gets a natural home next to `ConfirmDelete`. And `U`
stops being a promise the code only keeps for one kind of mistake.

**Three questions where the owner's answer changes what gets built:**

1. **The default of `DeleteToRecycleBin`.** Proposed **on**. Off would make the whole feature opt-in
   and leave the default loop exactly as destructive as it is today.
2. **The fallback where no bin exists.** Proposed **delete permanently, with the honest text** (D2) -
   which preserves today's behaviour on the owner's own shares. The alternative, refusing the deletion
   outright, would break that scenario and is not recommended.
3. **Whether `U` should restore from the bin at all** (Ф3), or whether "open the Recycle Bin in
   Explorer" is a sufficient answer. Proposed **restore** - the point of the loop is that the hand
   never leaves the keyboard, and a file that comes back into the list at its old position is the only
   version of "undo" that keeps that true.

---

## 10. What building Ф1 corrected in this document (revision 2, 2026-08-14)

Four things. None of them changes what the user gets; the first changes what "one route" means, and
the second is a trap the remaining phases will walk into if it is not written down.

**10.1 One policy and one executor - not one method.** §3.3 gives `ExecuteDelete` the signature
`(paths As IReadOnlyList(Of String), forcedPermanent As Boolean)`, which reads as though the viewer
and the F3 panel would call the same method. They cannot, and forcing it would have been worse than
the drift it was meant to prevent: the viewer deletes **one** file, reached through
`ReadShowMediaFile(Mode_Delete)` inside a `Boolean` pipeline that has to be able to answer "nothing
happened, stay where you are"; the panel deletes a **selection** off its own modal busy state, with
its own per-file error aggregation and its own card removal. What invariant 1 actually requires is
that there is one **decision** and one **act**, and that is what shipped: every route ends in
`DeletePolicy.Decide` and then in `RecycleBinIo.DeleteAs`. `ExecuteDelete` is now
`Function ExecuteDelete(targetPath, forcedPermanent) As Boolean` - the viewer's route - and the
panel's route is `SelectionDeleteDecision` + the same two shared calls. The invariant is unchanged
and is still checkable by `grep`: no mainline path calls `File.Delete` on the user's file.

**10.2 A parameter called `path` silently breaks `Path`.** VB is case-insensitive, so
`Friend Function VolumeRootOf(path As String)` shadows `System.IO.Path`, and `Path.GetFullPath(path)`
inside it compiles as a member call **on a String**: `BC30456: 'GetFullPath' is not a member of
'String'`. It cost three errors across two files. This is written down because the code sketches in
§3.2 and §3.4 of this document use exactly that parameter name, so Ф3's `RecycleBinIo.TryRestore`
and `RecycleBinIndex` will hit it again if they are typed in from here. The shipped parameters are
`anyPath`, `targetPath` and `filePath`.

**10.3 The probe runs on the UI thread, once per volume - and that is the right shape.** §3.2 says
the probe "never runs on the UI thread inside the hot path" and is "called once per folder change".
Neither is implementable as written: the only moment the path is known is the moment a deletion is
asked for, and probing on folder change would probe folders nobody ever deletes from. What shipped
keeps the promise that mattered - the answer is computed once per volume root and served from a
dictionary afterwards - and the dead-share case that motivated the sentence never reaches an I/O
call at all, because a UNC path is classified as `Network` without asking `DriveInfo` (which throws
on `\\server\share` anyway). The per-volume registry read is skipped entirely unless the volume is a
fixed disk: for every other kind the policy has already decided, so it would be I/O spent on an
answer nobody reads.

**10.4 Two corrections of shape.**
- **The bulk confirmation is three sentences, not seven.** §3.8 asks the panel to name the outcome
  for its root; the five environmental reasons collapse there into one *"there is no Recycle Bin
  here"*. A list of five possible reasons under a single Yes/No is noise, and the per-file dialog
  still names every one of them. Invariant 3 is about a permanent deletion never being unexplained,
  and it holds.
- **No file size is passed for a selection**, so rule 7 (the quota) never fires on that path.
  Stat-ing three hundred files over SMB to sharpen one sentence is not a trade worth making, and
  §6.1 already covers the residual: the shell has the final word, and the prediction only ever
  chooses wording.

**Also built here, though §3.10 listed it without a phase:** the `Shift+DEL` line of the first-run
help, through `ModernHelpLines()`, which returns `""` on net48 - so invariant 10 holds on the point
it names explicitly (the x86 viewer never advertises a key it does not have).

**Not changed:** the acceptance cases, the invariants, and Ф2..Ф4. The numbered scenes of §4 Ф1 are
still a manual pass - cases 1, 5, 7, 8 on a local disk and 2, 3, 4, 6 on the owner's `\\p7` share, a
mapped drive letter and a USB stick.

---

## 11. What building Ф2 corrected in this document (revision 3, 2026-08-14)

Five things. The first is the one that decided the shape of the phase.

**11.1 The acceptance asked for `UndoStackTests`, and §3.5 made them impossible.** §3.5 puts
`Private ReadOnly undo_Entries As New List(Of FileOp)` inside `Main_Form`, and §8 then asks for a
test proving the bound, the drop-oldest and "no push from an undo kind". None of that is reachable
from a test: `FileOp` and `FileOpKind` were both **private nested members of a WinForms class**. Two
things changed, and both are improvements rather than concessions:

- `FileOpKind` moved out to its own shared file, [src/FileOpKind.vb](../../src/FileOpKind.vb), at
  namespace level. Nothing else about it changed - every `FileOpKind.X` in the form partials resolves
  as before, both projects compile it.
- The history became a generic [UndoStack(Of T)](../../src/UndoStack.vb) plus `UndoPolicy` - a table
  mapping a kind to what `U` can promise for it (`MoveBack`, `DeleteTheCopy`, `RenameBack`,
  `RestoreFromBin`, `RefusePermanent`, `None`). The form holds an `UndoStack(Of FileOp)` and asks the
  table; it no longer decides.

That table is where the phase's real value ended up. "An undo is never itself undoable" is now
**structural**: the inverse kinds have no plan, and a kind nobody has written yet defaults to `None`.
The `Stack`-cannot-drop-its-oldest correction of D7 is proven rather than asserted.

**11.2 The rename does not go through the queue, so its entry is pushed where it completes.** §3.5
says every push happens in `FinishFileOp`'s success branch. `RenameCurrentFile` is synchronous - a
same-folder metadata move - so there is no completion to push from; the entry is recorded at the end
of that method instead. D8's rule is *push on success only*, and a call that returned without
throwing is exactly that. Its **undo** does go through the queue as `RenameUndo`, because a rename
back on a share that stopped answering blocks the UI thread just as long as any other move.

**11.3 A refusal entry is consumed, not kept.** §3.5's wording ("the entry is kept precisely so `U`
can say..") reads as though it stays. It cannot: an entry that can only ever produce the same
sentence would block every older operation behind it for the rest of the session, so the second press
would repeat the refusal instead of undoing the move before it. It explains itself **once** and then
gets out of the way. Invariant 7 is about `U` never silently doing nothing, and that still holds.

**11.4 Ф2 ships an honest interim answer for a recycled deletion.** The plan for `RecycleDelete` is
`RestoreFromBin`, which is Ф3. Until it exists, `U` says where the file actually is - *"файл в
Корзине - верните его из Корзины Windows"*. That is true today, it is not "no history", and it is
one line to replace when Ф3 lands.

**11.5 The list is rewritten by value, not by remembered index.** Undoing a rename has to find the
row again, and the list can have been re-sorted or rescanned in between. `ReplaceFileInList` looks
the old path up first and only falls back to the position recorded at rename time; writing blindly at
a remembered index would rename a different file's row.

**Not changed:** the invariants, Ф3 and Ф4. Ф2's acceptance cases 1-5 are a manual pass; case 6
(`UndoStackTests`) is green and cited in the status line above.

---

## 12. What building Ф3 corrected in this document (revision 4, 2026-08-14)

Four things. The first two are the same lesson from two directions: **§3.6 described the restore and
not its arrival**, and everything that was missing lived in the few lines between the worker finishing
and the user reading a sentence.

**12.1 The "existing failure branch" would have added a list entry for a file still in the bin.**
§3.6's table sends a denied access "through the existing `FinishFileOp` failure branch", which is
right about where it goes and wrong about what happens there. That branch exists to roll back the
**optimistic** list mutation every other operation makes: `If op.ListIndex >= 0 Then
InsertFileIntoList(op.Source, op.ListIndex)`. A restore removes nothing - its `ListIndex` is where the
file *would* go - so the rollback would have put a row in the list for a file that is still in the
Recycle Bin, in the one situation where the user is already being told something went wrong. That is
invariant 8 broken from the direction nobody was watching. The branch now names the kind and does
nothing for it.

**12.2 A refusal had no way home, because the only transport was an exception.** The table lists five
outcomes and leaves the mechanism unsaid; the only channel from the queue's worker thread to the UI
thread is the exception that `FinishFileOp` prints as *"Ошибка операции: {0}"*. "The bin was emptied
in between" is not an operation error - it is an **answer**, and dressing it as a failure would also
have dragged it through 12.1's rollback. `RecycleBinIo.TryRestore` therefore returns a
`BinRestoreResult` which rides home on the `FileOp`; only a genuine failure (a denied access, a share
that went away) still throws. What survives from §3.6 unchanged is the important half: the list is
touched in exactly one branch, and only because the worker verified the file is there.

**12.3 The verification has to be of the DESTINATION, not of the original path.** §3.6 says the list
is touched only after `File.Exists(originalPath)` is true. That test is wrong precisely in the case
the same section provides for: when the name has been taken back since the deletion, the file is
restored **beside** it under a resolved name - and `originalPath` then exists because of the *other*
file, so a restore that failed would report success and add a row pointing at a stranger's picture.
The check is against the path the file was actually written to.

**12.4 "Deleted at or after the moment we queued it" compares two different clocks.** Our stamp is
`DateTime.UtcNow` at queue time; the record's is a FILETIME the shell writes later. They agree, but a
NTP adjustment or a coarser resolution should never be able to turn a file that is plainly in the bin
into *"no longer in the Recycle Bin"*, so `BestMatch` allows two seconds of slack
(`RecycleBinIndex.Bin_Clock_Tolerance`). It cannot restore the wrong file: the full path still has to
match, and among matches the newest wins.

**Also worth recording about Ф3, though neither is a correction.** The `$I` route §3.6 chose over
`Shell.Application` held up exactly as argued - the parser is 40 lines, it is pure, and both record
versions are covered by tests that build the bytes themselves. And because those tests cannot say
whether the **shell's own** records parse, the phase was closed with a one-shot probe that linked
`RecycleBinIndex.vb` + `RecycleBinIo.vb` into a console app and ran the real thing: delete to the bin
and restore (bytes compared), two same-named files from two folders restored in the wrong order
(acceptance case 5, the one that proves the matcher is not "the newest thing in the bin"), a name
taken back in the meantime, a folder deleted in between, a path never deleted, and a record older
than our operation. Fourteen checks, all green. The GUI scenes of §4 Ф3 (1-5) are still a manual pass.

---

## 13. What building Ф4 corrected in this document (revision 5, 2026-08-14)

Four things, and the first is the only one that changed a decision rather than a detail.

**13.1 The two rows do not belong in `AddSettingsTransferRows`.** §3.9 names that method, and it is the
wrong one: it builds the *export settings to a file* / *import settings from a file* pair. What the
sentence after it means is right, though - "next to the transfer policy rows they belong with" - so the
rows are added in `BuildExpandedSettings` directly after `name_collision` and `after_file_operation`,
which are the rows about what a file operation does. In that order between themselves, too: whether the
bin is used at all decides what the confirmation is even able to say.

**13.2 `DeleteToRecycleBin = False` needed no branch, no new reason and no new sentence** - which is
the Ф1 design being confirmed rather than a correction to it. The preference is passed to
`DeletePolicy.Decide` as `binEnabledBySetting`, rule 2 answers `UserAsked`, and the user reads exactly
what `Shift+DEL` produces. That is the truthful message: they did ask, once, in the settings window
instead of at the keyboard. Had the switch been handled beside the policy, it would have needed a
seventh `PermanentReason` and a seventh sentence in thirteen languages, saying the same thing.

**13.3 The F3 panel had to be included twice over, and §3.7 mentions it nowhere.** §3.8 puts the panel
on the same policy for Ф1 and stops there, but the panel also *asks* - so with only the viewer
converted, "never" would have silenced the viewer while the panel kept asking, and `permanentOnly`
would have been invisible in the one place a mistake costs three hundred files rather than one. It now
calls the same `DeletePolicy.ShouldConfirm`, and its per-selection decision reads `DeleteToRecycleBin`
instead of the hard `True` Ф1 left there.

**13.4 A migration that is not written back is not a migration.** §3.7 says "once, at first load". If
the value were only derived when the key is missing, it would be re-derived on every start, and the
legacy checkbox would silently keep steering the delete question for the rest of the profile's life -
so flipping "no confirmations" for the sake of a copy would also switch off the delete question again.
`ReadConfirmDelete` writes the migrated value immediately; from then on the two settings are
independent, which is what lets the old checkbox go on governing everything it always did.

**One wording decision worth keeping.** The middle option names the **outcome**, not the internal
category: *"Только если файл не попадёт в Корзину"* rather than "only permanent deletions". What a
person is choosing between is which deletions are worth stopping for, and the answer is the ones that
cannot be taken back - which is a fact about this file and this volume, not a term from this document.

**Acceptance.** Cases 1-3 of §4 Ф4 are covered by `DeletePolicyTests` (the three policy values across
recycled, environmental and `Shift+DEL` deletions, plus the bin-off verdict) and by `ReadConfirmDelete`;
case 4 is the parity, coverage and §7.2 layout tests, all green with the new rows in thirteen languages.
What remains manual is the same thing as everywhere in this specification: a real share, a mapped drive
letter and a USB stick.
