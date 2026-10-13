#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The seam between the batch engine (BatchPlan / BatchRunner, pure) and the main window:
''' 004_SPECIFICATION_BATCH_OPERATIONS_ENGINE_DOTNET10.md PB-3..PB-6, and the one entry point
''' the F3 panel uses (005 PG-1).
'''
''' The rule it keeps: ONE loop. A batch is the ordinary file-operation queue (FileOpQueue -
''' the single consumer that already serialises every transfer and that shutdown already
''' waits for), fed one operation at a time by BatchRunner. A panel move therefore passes the
''' same gates as a key press - the slot verdict, the name-collision policy, the delete
''' policy, the archive/resource refusal - and lands on the same undo stack, as ONE entry.
'''
''' The single-file path is NOT rebuilt on top of this (PB-1 rule 2): the most-used code path
''' in the product does not go behind the newest code in it. A batch is for a selection.
''' </summary>
Partial Public Class Main_Form

    ' ----------------------------------------------------------------- state ----

    Private batch_Runner As BatchRunner

    ''' <summary>One run's bookkeeping, kept out of BatchRunner because it is about the
    ''' window (the list, the picture on screen), not about the sequence.</summary>
    Private NotInheritable Class BatchRunState
        Public SourceSession As ResourceSession
        Public IsUndo As Boolean
        Public Intent As BatchIntent
        ''' <summary>The generation of the file list the run started from; list edits happen
        ''' only while it is still the one on screen (audit 047, C-13).</summary>
        Public ListGeneration As Integer
        ''' <summary>What a forward run records for U. Nothing for an undo run.</summary>
        Public Entry As BatchUndoEntry
        ''' <summary>The picture on screen was let go of; it has to be shown again.</summary>
        Public MediaReleased As Boolean
        ''' <summary>The list changed under the picture on screen.</summary>
        Public Redisplay As Boolean
        ''' <summary>Rows were taken out of (or put back into) the list: the counter must follow
        ''' even when the picture on screen is not one of them.</summary>
        Public ListChanged As Boolean
    End Class

    Private batch_Run As BatchRunState

    ''' <summary>Set from the moment a batch is asked for until its runner has started (or the
    ''' user said no): the plan is built off the UI thread and a dialog may be open, and a key
    ''' pressed in that window must not start a second run.</summary>
    Private batch_Starting As Boolean

    ''' <summary>A batch is being planned or is running.</summary>
    Friend Function BatchBusy() As Boolean
        Return batch_Starting OrElse (batch_Runner IsNot Nothing AndAlso batch_Runner.IsRunning)
    End Function

    ''' <summary>A batch is running (not merely being planned) - what shutdown has to wait for.</summary>
    Friend ReadOnly Property BatchIsRunning As Boolean
        Get
            Return batch_Runner IsNot Nothing AndAlso batch_Runner.IsRunning
        End Get
    End Property

    ''' <summary>Raised on the UI thread before each operation starts.</summary>
    Friend Event BatchProgressChanged As EventHandler(Of BatchProgressEventArgs)

    ''' <summary>Raised on the UI thread when one operation has finished and the list has
    ''' been updated for it.</summary>
    Friend Event BatchItemDone As EventHandler(Of BatchItemEventArgs)

    ''' <summary>Raised once when a run is over. The history entry has been pushed and the
    ''' status line written by then.</summary>
    Friend Event BatchCompleted As EventHandler(Of BatchFinishedEventArgs)

    ''' <summary>Stops a run from starting anything further; the operation in flight finishes.</summary>
    Friend Sub CancelBatch()
        If batch_Runner IsNot Nothing Then batch_Runner.Cancel()
    End Sub

    ''' <summary>What the status line says right now - the F3 panel is modal over this window, so
    ''' a refusal written here (a slot that is down, an archive, a busy queue) has to be read out
    ''' and shown by the panel itself.</summary>
    Friend ReadOnly Property StatusLineText As String
        Get
            Return If(lbl_Status Is Nothing, "", lbl_Status.Text)
        End Get
    End Property

    ''' <summary>The status line as words and a level (124 UX-206): the F3 panel reads the LEVEL, not a leading
    ''' "!" it had to strip and test for.</summary>
    Friend ReadOnly Property StatusMessageNow As NotifyMessage
        Get
            If lbl_Status Is Nothing Then Return New NotifyMessage(String.Empty, NotifyLevel.Info)
            Return StatusMessage
        End Get
    End Property

    ''' <summary>U pressed in the F3 panel. Exactly the main window's U - same stack, same
    ''' refusals - so a user who did something in the panel can take it back where they did it
    ''' (005 PG-1).</summary>
    Friend Sub UndoFromPanel()
        lbl_Status.Text = ""
        Undo()
    End Sub

    ''' <summary>
    ''' A thumbnail worker asks for an archive entry to exist on disk. Called OFF the UI thread
    ''' and therefore touching no control: the display path's own EnsureArchiveEntryOnDisk writes
    ''' the status line on a refusal, which is not something a background card may do. The
    ''' archive session serialises its readers, so this and the main window's own extraction
    ''' share one stream safely. A solid 7z/RAR is never unpacked for a card (allowSlow False):
    ''' that is a one-pass job with progress, started on purpose.
    ''' </summary>
    Friend Function MaterializeForThumbnail(filePath As String, maxEntryBytes As Long, maxCacheBytes As Long) As Boolean
        If String.IsNullOrEmpty(filePath) Then Return False
        Dim resource As ResourceSession = ActiveResourceSession()
        If resource IsNot Nothing AndAlso resource.OwnsLocalPath(filePath) Then Return resource.MaterializeForThumbnail(filePath)
        If File.Exists(filePath) Then Return True
        Dim session As ArchiveSession = archive_Session
        If session Is Nothing Then Return False
        Dim index As Integer = session.IndexOfTempPath(filePath)
        If index < 0 Then Return False
        Dim refusal As ArchiveSession.EntryRefusal
        Return session.TryEnsureExtracted(index, refusal, maxEntryBytes, maxCacheBytes, allowSlow:=False)
    End Function

    Private Function EnsureBatchRunner() As BatchRunner
        If batch_Runner Is Nothing Then
            batch_Runner = New BatchRunner(AddressOf EnqueueBatchOp)
            AddHandler batch_Runner.Progress, AddressOf BatchRunner_Progress
            AddHandler batch_Runner.ItemFinished, AddressOf BatchRunner_ItemFinished
            AddHandler batch_Runner.Finished, AddressOf BatchRunner_Finished
        End If
        Return batch_Runner
    End Function

    ' ------------------------------------------------------- the panel's list ----

    ''' <summary>Size and date of every file the last scan saw, kept beside the list of paths
    ''' (which carries neither): the panel's details view and its size sort read this instead
    ''' of stat-ing the whole folder a second time.</summary>
    Private file_Meta As Dictionary(Of String, FileMetaInfo)

    Private Structure FileMetaInfo
        Public SizeBytes As Long
        Public Modified As DateTime
    End Structure

    Private Sub RememberFileMeta(entries As List(Of FileEntry))
        Dim table As New Dictionary(Of String, FileMetaInfo)(entries.Count, StringComparer.OrdinalIgnoreCase)
        For Each e As FileEntry In entries
            If e.FilePath Is Nothing Then Continue For
            table(e.FilePath) = New FileMetaInfo With {.SizeBytes = e.FileSize, .Modified = e.FileDate}
        Next
        file_Meta = table
    End Sub

    ''' <summary>
    ''' The list the main window has right now, for the F3 panel (PG-2): the same order, the
    ''' same filter, the same files - including an open archive's entries. A file with no
    ''' metadata (it arrived after the scan) is left without, and the panel asks for it once.
    ''' </summary>
    Friend Function SnapshotFileList() As PanelListSnapshot
        Dim snapshot As New PanelListSnapshot With {
            .CurrentPath = If(Current_File_Name, ""),
            .Generation = file_List_Generation,
            .Folder = If(Current_Folder_Path, "")}

        Dim table As Dictionary(Of String, FileMetaInfo) = file_Meta
        Dim append As Action(Of String) =
            Sub(path As String)
                Dim row As New PanelFileRow With {.Path = path}
                Try
                    row.Kind = MediaKindClassifier.KindOf(path)
                Catch
                    row.Kind = MediaKind.Other
                End Try
                Dim meta As FileMetaInfo
                If table IsNot Nothing AndAlso table.TryGetValue(path, meta) Then
                    row.SizeBytes = meta.SizeBytes
                    row.Modified = meta.Modified
                End If
                snapshot.Rows.Add(row)
            End Sub

        If is_Files_Array_Active Then
            If files_Array IsNot Nothing Then
                For Each p As String In files_Array
                    append(p)
                Next
            End If
        ElseIf files_List IsNot Nothing Then
            For Each p As String In files_List
                append(p)
            Next
        End If
        Return snapshot
    End Function

    ' ------------------------------------------------------------- starting ----

    ''' <summary>
    ''' Moves, copies or deletes a selection. False means nothing was started - either it was
    ''' refused with a sentence on the status line, the user said no to the one question, or a
    ''' slot probe is out and this call will be made again with the answer in hand.
    '''
    ''' The order is the single-file order, on purpose (invariant 8 of slot health): every
    ''' refusal comes BEFORE anything is released, mutated or queued.
    ''' </summary>
    Friend Async Function StartBatchAsync(owner As IWin32Window, selected As IReadOnlyList(Of String),
                                          intent As BatchIntent, slotIndex As Integer) As Task(Of Boolean)
        If selected Is Nothing OrElse selected.Count = 0 Then Return False
        ' What the panel reads back after a refusal is THIS call's sentence, not whatever the
        ' status line said before it (the panel is modal over this window).
        lbl_Status.Text = ""
        If FileOpWorkerBusy() Then Return False
        If file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0 Then
            ' The history holds COMPLETED operations only; a batch started on top of a tail of
            ' single moves would interleave with them.
            lbl_Status.Text = Localization.T("! Подождите: предыдущие файловые операции ещё выполняются")
            Return False
        End If

        Dim verb As ResourceVerb = If(intent = BatchIntent.Delete, ResourceVerb.Delete,
                                      If(intent = BatchIntent.CopyToSlot, ResourceVerb.Copy, ResourceVerb.Move))
        If ResourceBlocksFileOperations(verb) Then Return False
        Dim sourceSession As ResourceSession = ActiveResourceSession()
        Dim sourceGeneration As Integer = file_List_Generation

        Dim create_Destination As Boolean = False
        Dim destination_Folder As String = ""
        Dim slot_Key As String = ""
        If intent <> BatchIntent.Delete Then
            If Not IsRecipientSlotConfigured(slotIndex) Then Return False
            destination_Folder = Hardkeys_to_move_mediafile(slotIndex)
            slot_Key = SlotKeyCaption(slotIndex)
            Dim slot_Action As RecipientActionKind = If(intent = BatchIntent.CopyToSlot, RecipientActionKind.Copy, RecipientActionKind.Move)
            ' One probe for the whole run, however many files (PB-6); when it has to ask the
            ' network, this same call is made again once the answer is in.
            Dim again As Action = Sub() Forget(StartBatchAsync(owner, selected, intent, slotIndex))
            If Not SlotHealthAllowsAction(slotIndex, slot_Action, create_Destination, again) Then Return False
        End If

        batch_Starting = True
        Dim started As Boolean = False
        Try
            Dim prefs As ModernViewerPreferences = GetModernPreferences()
            Dim paths As List(Of String) = selected.ToList()
            Dim meta As Dictionary(Of String, FileMetaInfo) = file_Meta
            ' Asked here, on the UI thread that owns the open resource, before the plan goes to the
            ' pool: a resource file's real name and its address on the server (007 §4.2).
            Dim transfer_Names As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim source_Locations As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each p As String In paths
                If String.IsNullOrEmpty(p) Then Continue For
                transfer_Names(p) = TransferNameOf(p)
                source_Locations(p) = LogicalPathOf(p)
            Next
            Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current

            Dim ctx As New BatchPlanContext With {
                .DestinationFolder = destination_Folder,
                .SlotKey = slot_Key,
                .CreateDestinationFolder = create_Destination,
                .CollisionPolicy = If(prefs Is Nothing, "ask", prefs.NameCollisionPolicy),
                .ConfirmDeletePolicy = If(prefs Is Nothing, "always", prefs.ConfirmDelete),
                .FileExists = Function(p As String)
                                  ' A server folder is asked through the slot connections; a server
                                  ' that cannot say throws, and the batch stops before it starts.
                                  If ResourceLocator.IsLocator(p) Then
                                      If slots Is Nothing Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported))
                                      Return slots.FileExists(p)
                                  End If
                                  If sourceSession IsNot Nothing AndAlso sourceSession.EntryAt(p) IsNot Nothing Then Return True
                                  Return File.Exists(p)
                              End Function,
                .TransferName = Function(p As String)
                                    Dim name As String = Nothing
                                    If transfer_Names.TryGetValue(p, name) Then Return name
                                    Return Path.GetFileName(p)
                                End Function,
                .SourceLocation = Function(p As String)
                                      Dim location As String = Nothing
                                      If source_Locations.TryGetValue(p, location) Then Return location
                                      Return p
                                  End Function,
                .FileSize = Function(p As String)
                                Dim m As FileMetaInfo
                                If meta IsNot Nothing AndAlso meta.TryGetValue(p, m) Then Return m.SizeBytes
                                Dim entry As ResourceEntry = sourceSession?.EntryAt(p)
                                If entry IsNot Nothing Then Return Math.Max(0, entry.Length)
                                Return New FileInfo(p).Length
                            End Function,
                .IsSameFile = Function(a As String, b As String) FileManager.IsSameFile(a, b)}
            Dim bin_Enabled As Boolean = prefs Is Nothing OrElse prefs.DeleteToRecycleBin

            ' The plan touches the file system (existence, size, the volume probe for a
            ' delete) once per selected file: off the UI thread, so three hundred files over a
            ' slow share cost a moment of "planning", not a frozen window.
            Dim plan As BatchPlan
            Try
                plan = Await Task.Run(
                    Function() As BatchPlan
                        If intent = BatchIntent.Delete Then ctx.DeleteDecision = SelectionDeleteDecision(paths, bin_Enabled, sourceSession?.Provider.Capabilities)
                        Return BatchPlan.Build(paths, intent, ctx)
                    End Function)
            Catch ex As Exception When ResourceLocator.IsLocator(destination_Folder)
                ' A server folder (a remote slot) that could not say which names are taken: nothing
                ' has been touched yet, and nothing is planned on a guess.
                If Me.IsDisposed Then Return False
                Dim failure As ResourceFailure = ResourceFailure.FromException(ex)
                AppFileLogger.WriteLine("Batch: planning into a server folder failed - " & failure.ToString())
                lbl_Status.Text = Localization.TF("«{0}»: {1}", destination_Folder, ResourceMessages.ForFailure(failure))
                Return False
            End Try
            If Me.IsDisposed Then Return False

            ' --- the questions, once each (PB-6) ---------------------------------------
            If intent = BatchIntent.Delete Then
                If plan.NeedsDeleteConfirmation Then
                    Dim decision As New DeleteDecision With {
                        .Outcome = If(plan.PermanentDeletionReason = PermanentReason.NotPermanent, DeleteOutcome.Recycle, DeleteOutcome.Permanent),
                        .Reason = plan.PermanentDeletionReason}
                    If ViewerBox.Show(owner, DeleteText.ConfirmMany(decision, plan.Count),
                                       Localization.T("Подтверждение удаления"),
                                       MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return False
                End If
            ElseIf plan.NeedsCollisionAnswer.Count > 0 Then
                Dim answer As CollisionAnswer? = AskBatchCollision(owner, plan.NeedsCollisionAnswer.Count, plan.Count)
                If Not answer.HasValue Then Return False
                plan = plan.WithCollisionAnswer(answer.Value, ctx)
            ElseIf Not replace_Warned_This_Session Then
                ' The standing policy "replace" is the user's own answer; the once-per-session
                ' warning only makes sure they know it is being acted on (SETTINGS_EXPANSION 3.1).
                Dim first_Replaced As BatchOp = plan.Operations.FirstOrDefault(Function(o) o.ReplaceExisting)
                If first_Replaced IsNot Nothing Then
                    If ViewerBox.Show(owner,
                        Localization.TF("В настройках выбрано «заменять»: файл {0} в папке назначения будет перезаписан. Продолжить? Это предупреждение показывается один раз за сеанс.", Path.GetFileName(first_Replaced.Destination)),
                        Localization.T("Совпадение имён"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return False
                    replace_Warned_This_Session = True
                End If
            End If
            If Me.IsDisposed Then Return False

            ' --- go ---------------------------------------------------------------------
            If sourceGeneration <> file_List_Generation OrElse sourceSession IsNot ActiveResourceSession() Then Return False
            If intent <> BatchIntent.Delete Then
                Dim sourceCaps As ProviderCapabilities = ActiveResourceSession()?.Provider.Capabilities
                Dim destinationCaps As ProviderCapabilities = Nothing
                Dim destinationResource As ResourceDescriptor = Nothing
                Dim remotePath As String = ""
                If slots IsNot Nothing Then slots.Locate(destination_Folder, destinationResource, remotePath)
                If destinationResource IsNot Nothing Then
                    destinationCaps = New ProviderCapabilities(costHint:=If(destinationResource.Kind = ResourceKind.Sftp,
                                                                           ProviderCostHint.Slow, ProviderCostHint.Fast))
                End If
                If RemoteBatchPolicy.NeedsWarning(plan.TotalBytes, sourceCaps, destinationCaps) Then
                    If ViewerBox.Show(owner,
                        Localization.TF("Будет передано {0} через медленный или тарифицируемый ресурс. Между разными серверами файл сначала скачивается, затем загружается; проверка также расходует трафик. Продолжить?", FormatFileSize(plan.TotalBytes)),
                        Localization.T("Ресурсы"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return False
                End If
            End If
            If Me.IsDisposed Then Return False

            Dim run As New BatchRunState With {
                .IsUndo = False,
                .SourceSession = sourceSession,
                .Intent = intent,
                .ListGeneration = file_List_Generation,
                .Entry = New BatchUndoEntry()}

            ' A move or a delete needs delete access to the file on screen, which a playing
            ' video holds open - the same release every single-file operation does first.
            If intent <> BatchIntent.CopyToSlot AndAlso Not String.IsNullOrEmpty(Current_File_Name) AndAlso
               plan.Operations.Any(Function(o) String.Equals(o.Source, Current_File_Name, StringComparison.OrdinalIgnoreCase)) Then
                ReleaseActiveMedia()
                run.MediaReleased = True
            End If

            batch_Run = run
            Dim kind As BatchRunKind = If(intent = BatchIntent.Delete, BatchRunKind.Delete,
                                          If(intent = BatchIntent.CopyToSlot, BatchRunKind.Copy, BatchRunKind.Move))
            started = EnsureBatchRunner().Start(kind, plan.Operations, plan.Skipped, 0)
            If Not started Then batch_Run = Nothing
            Return started
        Finally
            batch_Starting = False
        End Try
    End Function

    ''' <summary>
    ''' The one collision question of a batch (PB-6): how many destinations already exist and
    ''' what to do with all of them. Cancel stops the whole batch; the default button IS Cancel,
    ''' for the reason the single-file dialog gives - sorting is a run of key presses, and the
    ''' Enter already typed for the next picture lands here.
    ''' </summary>
    Private Function AskBatchCollision(owner As IWin32Window, colliding As Integer, total As Integer) As CollisionAnswer?
        Dim replace_Button As New TaskDialogCommandLinkButton(Localization.T("Заменить"))
        Dim both_Button As New TaskDialogCommandLinkButton(Localization.T("Сохранить оба"),
                                                          Localization.T("новые файлы получат имя вида «имя (2)»"))
        Dim skip_Button As New TaskDialogCommandLinkButton(Localization.T("Пропустить совпадающие"),
                                                          Localization.T("остальные файлы будут обработаны"))
        Dim cancel_Button As TaskDialogButton = TaskDialogButton.Cancel

        Dim page As New TaskDialogPage With {
            .Caption = Localization.T("Совпадение имён"),
            .Heading = Localization.TF("В папке назначения уже есть файлов с такими именами: {0} из {1}", colliding, total),
            .Icon = TaskDialogIcon.Warning,
            .AllowCancel = True}
        page.Buttons.Add(replace_Button)
        page.Buttons.Add(both_Button)
        page.Buttons.Add(skip_Button)
        page.Buttons.Add(cancel_Button)
        page.DefaultButton = cancel_Button

        Dim pressed As TaskDialogButton = If(owner IsNot Nothing, TaskDialog.ShowDialog(owner, page), TaskDialog.ShowDialog(page))
        If pressed Is replace_Button Then Return CollisionAnswer.Replace
        If pressed Is both_Button Then Return CollisionAnswer.KeepBoth
        If pressed Is skip_Button Then Return CollisionAnswer.Skip
        Return Nothing
    End Function

    ''' <summary>
    ''' One verdict for the whole selection, probing each distinct volume root once (R-1 3.8).
    ''' A mixed selection takes the PERMANENT answer, because that is the true one for at
    ''' least one of the files, and a dialog promising the Recycle Bin to a file the share will
    ''' swallow is exactly the lie this policy exists to remove. The size is deliberately not
    ''' passed: the quota rule is per file and stat-ing three hundred files over SMB to sharpen
    ''' one sentence is not a trade worth making - the shell still has the last word.
    ''' </summary>
    Private Shared Function SelectionDeleteDecision(paths As IEnumerable(Of String), binEnabled As Boolean,
                                                    Optional capabilities As ProviderCapabilities = Nothing) As DeleteDecision
        If capabilities IsNot Nothing AndAlso Not capabilities.HasRecycleBin Then
            Return DeletePolicy.DecideForProvider(capabilities, Nothing, -1, binEnabled, False)
        End If
        Dim verdict As DeleteDecision = Nothing
        Dim roots_Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each p As String In paths
            Dim root As String = DeleteVolumeProbe.VolumeRootOf(p)
            If Not roots_Seen.Add(root) Then Continue For
            Dim one As DeleteDecision = DeletePolicy.Decide(DeleteVolumeProbe.FactsFor(p), -1,
                                                            binEnabledBySetting:=binEnabled, forcedPermanent:=False)
            If verdict Is Nothing OrElse (Not verdict.IsPermanent AndAlso one.IsPermanent) Then verdict = one
        Next
        If verdict Is Nothing Then
            verdict = New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.VolumeUnknown}
        End If
        Return verdict
    End Function

    ' ------------------------------------------------------------------ undo ----

    ''' <summary>
    ''' U on a batch entry: every completed operation reversed through its own inverse, last
    ''' first, as one run with one report (PB-5). The entry has already been popped.
    ''' </summary>
    Private Sub StartBatchUndo(entry As BatchUndoEntry)
        If entry Is Nothing Then Return
        Dim undo_Plan As BatchUndoPlan = BatchUndoPlanner.Build(entry,
            Function(old_Path As String) ResolveDestinationCollision(old_Path))

        Dim run As New BatchRunState With {
            .IsUndo = True,
            .Intent = BatchIntent.MoveToSlot,
            .ListGeneration = entry.ListGeneration}

        ' The file on screen may be one of those about to travel back out of a slot folder.
        If Not String.IsNullOrEmpty(Current_File_Name) AndAlso
           undo_Plan.Inverse.Any(Function(o) String.Equals(o.Source, Current_File_Name, StringComparison.OrdinalIgnoreCase)) Then
            ReleaseActiveMedia()
            run.MediaReleased = True
        End If

        batch_Run = run
        SayProgress(Localization.T("!Ждите. Возвращаются файлы.."))
        If Not EnsureBatchRunner().Start(BatchRunKind.Undo, undo_Plan.Inverse, Nothing, undo_Plan.NotReturnable.Count) Then
            batch_Run = Nothing
        End If
    End Sub

    ' ------------------------------------------------------------ the queue ----

    ''' <summary>Hands ONE operation of the run to the application's queue.</summary>
    Private Function EnqueueBatchOp(item As BatchOp) As Boolean
        Dim op As New FileOp With {
            .Kind = item.Kind,
            .Source = item.Source,
            .Destination = item.Destination,
            .SlotKey = item.SlotKey,
            .ReplaceExisting = item.ReplaceExisting,
            .CreateDestinationFolder = item.CreateDestinationFolder,
            .DeletedAtUtc = item.DeletedAtUtc,
            .PermanentDeleteReason = item.PermanentDeleteReason,
            .ListGeneration = If(batch_Run Is Nothing, file_List_Generation, batch_Run.ListGeneration),
            .BatchItem = item}
        ' An undo of a copy deletes it only while the original still exists (046 C-04); the
        ' worker asks, and needs the original's path to do so.
        If item.Kind = FileOpKind.DeleteUndo Then op.UndoOrigin = New FileOp With {.Source = item.OriginalSource}
        If (item.Kind = FileOpKind.Delete OrElse item.Kind = FileOpKind.Move OrElse item.Kind = FileOpKind.Copy) AndAlso batch_Run?.SourceSession IsNot Nothing Then
            op.RemoteDelete = ResourceRouting.CaptureDelete(item.Source, batch_Run.SourceSession)
        End If
        Dim admitted As Boolean = EnsureFileOpQueue().Enqueue(op)
        If Not admitted Then op.RemoteDelete?.Dispose()
        Return admitted
    End Function

    ''' <summary>
    ''' The queue reports an item of a batch, on the UI thread. What the worker found rides
    ''' home on the FileOp; it is copied onto the BatchOp the runner and the history hold.
    ''' </summary>
    Private Sub BatchOpFinished(op As FileOp, failure As Exception)
        Dim item As BatchOp = op.BatchItem
        item.CreatedDestinationFolder = op.CreatedDestinationFolder
        item.DeletedAtUtc = op.DeletedAtUtc

        ' A resource transfer that ended with both copies valid (100 §14.2): a completed
        ' copy beside a failed move, or an uncertain last step. Reported per item with the
        ' files named - and NOT counted as a transport fault against the destination,
        ' which is alive and has the copy.
        Dim transfer_Failure As TransferOutcomeException = TryCast(failure, TransferOutcomeException)
        If transfer_Failure IsNot Nothing AndAlso transfer_Failure.Outcome IsNot Nothing AndAlso
           (transfer_Failure.Outcome.Result = TransferResult.CopyOnly OrElse
            transfer_Failure.Outcome.Result = TransferResult.Indeterminate) Then
            AppFileLogger.WriteLine("Batch: " & ResourceRouting.DescribeOutcome(transfer_Failure.Outcome))
            failure = Nothing
            item.Refusal = If(transfer_Failure.Outcome.Result = TransferResult.CopyOnly,
                              Localization.T("файл скопирован в папку назначения, исходный файл остался на месте - перемещение не завершено"),
                              Localization.T("результат последнего шага неизвестен: проверьте файл в обеих папках"))
        End If

        If failure Is Nothing Then
            ' The shell said "recycled" and the bin holds no record: it deleted the file for
            ' good (audit 047, C-09). Record it as the permanent deletion it was.
            If item.Kind = FileOpKind.RecycleDelete AndAlso op.RecycleFellThrough Then
                item.RecycleFellThrough = True
                item.Kind = FileOpKind.Delete
                item.PermanentDeleteReason = PermanentReason.VolumeUnknown
                AppFileLogger.WriteLine("Recycle Bin kept no record - the file was deleted permanently: " & op.Source)
            End If

            ' Operations that finish without an exception and still did nothing: an answer,
            ' reported per file in the user's words.
            If item.Kind = FileOpKind.DeleteUndo AndAlso op.CopyUndoRefused Then
                item.Refusal = Localization.T("оригинала больше нет или он изменился - копия осталась единственной, отмена её не удалит")
            ElseIf item.Kind = FileOpKind.RestoreFromBin Then
                Select Case op.RestoreOutcome
                    Case BinRestoreResult.Restored
                        item.RestoreOutcome = BatchRestoreOutcome.Restored
                    Case BinRestoreResult.SourceFolderGone
                        item.RestoreOutcome = BatchRestoreOutcome.SourceFolderGone
                        item.Refusal = Localization.T("папка, из которой файл был удалён, больше не существует")
                    Case Else
                        item.RestoreOutcome = BatchRestoreOutcome.NotInBin
                        item.Refusal = Localization.T("файла больше нет в Корзине - возможно, она очищена")
                End Select
            End If
        End If

        EnsureBatchRunner().OpCompleted(item, failure)
    End Sub

    ' ----------------------------------------------------------- the events ----

    Private Sub BatchRunner_Progress(sender As Object, e As BatchProgressEventArgs)
        If Me.IsDisposed Then Return
        lbl_Status.Text = Localization.TF("Файл {0} из {1}: {2}", e.Index, e.Total, e.Name)
        RaiseEvent BatchProgressChanged(Me, e)
    End Sub

    ''' <summary>
    ''' One item is done: the list and the history change NOW, not at the end, so the panel's
    ''' grid and the main window's counter follow the run instead of jumping when it ends.
    ''' </summary>
    Private Sub BatchRunner_ItemFinished(sender As Object, e As BatchItemEventArgs)
        If Me.IsDisposed Then Return
        Dim run As BatchRunState = batch_Run
        Dim item As BatchOp = e.Result.Op
        If run IsNot Nothing AndAlso item IsNot Nothing AndAlso e.Result.State = BatchItemState.Done Then
            Dim on_Shown_List As Boolean = FileOpRules.ListStampMatches(run.ListGeneration, file_List_Generation)
            Select Case item.Kind
                Case FileOpKind.Move, FileOpKind.RecycleDelete, FileOpKind.Delete
                    ' The file has left its folder. Its place in the list is remembered, so U
                    ' puts it back where it was and not at the end.
                    If on_Shown_List Then
                        Dim was_On_Screen As Boolean = String.Equals(Current_File_Name, item.Source, StringComparison.OrdinalIgnoreCase)
                        item.ListIndex = RemoveCurrentFileFromList(item.Source)
                        If item.ListIndex >= 0 Then run.ListChanged = True
                        If was_On_Screen Then run.Redisplay = True
                    End If
                    RecordBatchItem(run, item)

                Case FileOpKind.Copy
                    RecordBatchItem(run, item)

                Case FileOpKind.MoveUndo
                    ' The file only arrived back now. Into the list it belongs in, at the place
                    ' it left (C-13).
                    If on_Shown_List OrElse IsListOfFolder(FolderOfFile(item.Destination)) Then
                        InsertFileIntoList(item.Destination, If(item.ListIndex >= 0, item.ListIndex, current_File_Index))
                        run.Redisplay = True
                        run.ListChanged = True
                    End If

                Case FileOpKind.DeleteUndo
                    ' The copy is gone - if its folder is the one on screen, so is its row.
                    If IndexOfFileInList(item.Source) >= 0 Then
                        If String.Equals(Current_File_Name, item.Source, StringComparison.OrdinalIgnoreCase) Then run.Redisplay = True
                        RemoveCurrentFileFromList(item.Source)
                        run.ListChanged = True
                    End If

                Case FileOpKind.RestoreFromBin
                    If item.RestoreOutcome = BatchRestoreOutcome.Restored AndAlso
                       (on_Shown_List OrElse IsListOfFolder(FolderOfFile(item.Destination))) Then
                        InsertFileIntoList(item.Destination, If(item.ListIndex >= 0, item.ListIndex, current_File_Index))
                        run.Redisplay = True
                        run.ListChanged = True
                    End If
            End Select
        End If
        RaiseEvent BatchItemDone(Me, e)
    End Sub

    Private Shared Sub RecordBatchItem(run As BatchRunState, item As BatchOp)
        If run.Entry Is Nothing Then Return
        run.Entry.Items.Add(New BatchUndoItem With {
            .Kind = item.Kind,
            .Source = item.Source,
            .Destination = item.Destination,
            .ListIndex = item.ListIndex,
            .DeletedAtUtc = item.DeletedAtUtc,
            .PermanentDeleteReason = item.PermanentDeleteReason})
    End Sub

    Private Sub BatchRunner_Finished(sender As Object, e As BatchFinishedEventArgs)
        Dim run As BatchRunState = batch_Run
        batch_Run = Nothing
        If Me.IsDisposed OrElse run Is Nothing Then Return

        ' ONE history entry for the whole gesture. An undo run records nothing - an undo is
        ' never itself undoable - and an entry with nothing in it would offer a U that does
        ' nothing.
        If Not run.IsUndo AndAlso run.Entry IsNot Nothing AndAlso run.Entry.Items.Count > 0 Then
            run.Entry.ListGeneration = run.ListGeneration
            undo_Entries.Push(New FileOp With {.Kind = FileOpKind.Batch, .Batch = run.Entry, .ListGeneration = run.ListGeneration})
        End If

        ' The picture on screen was let go of, or the file under it left: show what the
        ' list says now. Once, at the end - not once per file.
        If run.MediaReleased OrElse run.Redisplay Then
            Try
                ReadShowMediaFile(If(run.IsUndo, Mode_AfterUndo, Mode_SetFile))
            Catch ex As Exception
                AppFileLogger.LogException("Batch: the file on screen could not be shown again", ex)
            End Try
        ElseIf run.ListChanged AndAlso total_File_Count > 0 Then
            ' The picture on screen stayed, but the list around it changed: "N of M" follows.
            lbl_File_Number.Text = Localization.TF("{0} из {1}", current_File_Index + 1, total_File_Count)
        End If

        ' After the display, which writes its own status: the report is the last word.
        lbl_Status.Text = e.Report.SummaryLine()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2300: batch finished: " & e.Report.SummaryLine())
        RaiseEvent BatchCompleted(Me, e)
    End Sub

End Class
#End If
