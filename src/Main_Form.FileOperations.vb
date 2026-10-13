Option Strict On

Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Main_Form

    ' FileOpKind now lives in its own file (src/FileOpKind.vb) at namespace level, so that
    ' UndoPolicy - the table saying which completed operation can be taken back and how -
    ' can be a pure function with tests instead of a Select Case buried in this class.

    ''' <summary>What a recipient slot is asked to do with the current file. The point of
    ''' the type is that the CALLER says it: on the mainline every entry point (a number
    ''' key, the overlay, a context menu, the settings grid) passes Move or Copy
    ''' explicitly instead of every one of them reading one global mode flag - which is
    ''' what made copying a trip to the settings window and back.
    ''' SPECIFICATION_COPY_ACTIONS_REWORK.md §3.1.</summary>
    Private Enum RecipientActionKind
        Move
        Copy
    End Enum

    ''' <summary>One file operation, whole. The worker reads NOTHING off the form:
    ''' type and paths used to live in two shared fields, so a second hotkey pressed
    ''' during a long move overwrote the in-flight operation and its completion then
    ''' ran the wrong branch. ListIndex is the snapshot needed to put the file back
    ''' if the operation fails.</summary>
    Private NotInheritable Class FileOp
#If Not NETFRAMEWORK Then
        Public Property RemoteDelete As ResourceDeleteTarget
#End If
        Public Property Kind As FileOpKind
        Public Property Source As String = ""
        Public Property Destination As String = ""
        Public Property SlotKey As String = ""
        Public Property ListIndex As Integer = -1
        ''' <summary>Tail for the success status, e.g. "the name was taken, saved as ..".
        ''' It travels with the op because the message is built when the op finishes,
        ''' long after the reason for it was known.</summary>
        Public Property StatusNote As String = ""
        ''' <summary>The generation of the file list this operation was started from
        ''' (Main_Form.file_List_Generation). Its completion, rollback and undo touch the
        ''' list - and the view - only while that list is still the one on screen (audit
        ''' 047, C-13).</summary>
        Public Property ListGeneration As Integer
        ''' <summary>A copy's navigation is deferred until the worker reports success: a
        ''' failed or skipped copy leaves the view where it was (SETTINGS_EXPANSION §3.2).
        ''' A move shows the next file at once - the moved one has already left the list -
        ''' and uses this only for "close if empty".</summary>
        Public Property NavigateAfterSuccess As Boolean
        Public Property CloseIfEmptyAfterSuccess As Boolean
        Public Property ReplaceExisting As Boolean
        ''' <summary>The destination's final segment does not exist yet and may be made
        ''' (011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md §3.6). The DECISION is
        ''' taken where the policy lives - ExecuteRecipientAction, from the slot verdict - and
        ''' the COST is paid where blocking is harmless, on the worker (D5): CreateDirectory
        ''' on a sleeping share blocks for the full timeout, and doing that on the UI thread
        ''' would hand back the freeze the queue was built to remove.</summary>
        Public Property CreateDestinationFolder As Boolean
        ''' <summary>Written by the worker when it really made the folder, so the success
        ''' line can say so. A folder that already existed says nothing.</summary>
        Public Property CreatedDestinationFolder As Boolean
#If Not NETFRAMEWORK Then
        ''' <summary>When the deletion was queued. It is what tells one deletion of
        ''' 'cover.jpg' from another when the Recycle Bin is searched for it (Ф3), so it
        ''' is stamped even though nothing reads it yet.</summary>
        Public Property DeletedAtUtc As DateTime
        ''' <summary>Why a deletion was permanent, carried so that U can name the reason
        ''' rather than say "no history" about a file it watched being destroyed.</summary>
        Public Property PermanentDeleteReason As PermanentReason
        ''' <summary>What came of a RestoreFromBin, written by the worker and read on the UI
        ''' thread. "The bin was emptied in between" is an ANSWER, not an exception, so it
        ''' travels with the operation rather than being thrown - and the list is only
        ''' touched once this says the file is really back (invariant 8).</summary>
        Public Property RestoreOutcome As BinRestoreResult = BinRestoreResult.NotAttempted
        ''' <summary>Written by the worker: the shell was asked to recycle the file, the
        ''' file is gone, and the bin holds no record of it - so it was deleted for good
        ''' (audit 047, C-09). The shell does that in silence when it cannot recycle
        ''' (a subst drive, a volume mounted in a folder, a virtual disk).</summary>
        Public Property RecycleFellThrough As Boolean
        ''' <summary>For an undo operation: the history entry it was built from, pushed
        ''' back if the undo fails so U can be pressed again (audit 047, C-07).</summary>
        Public Property UndoOrigin As FileOp
        ''' <summary>Written by the worker for an undo of a copy: the original is gone or
        ''' changed, so the copy is the only instance left and was NOT deleted (046 C-04).
        ''' Asked there rather than at U time, because the copy sits in a slot folder that
        ''' may be a sleeping share (audit 057.S06-2).</summary>
        Public Property CopyUndoRefused As Boolean
        ''' <summary>Set when this operation is one item of a batch (Main_Form.Batch.vb): its
        ''' completion goes to the batch, not to the single-file list surgery in
        ''' FinishFileOp. The queue is the same one; only the bookkeeping differs.</summary>
        Public Property BatchItem As BatchOp
        ''' <summary>For a FileOpKind.Batch history entry: every operation the batch
        ''' completed. Such an entry is never queued - U turns it into a run of inverses.</summary>
        Public Property Batch As BatchUndoEntry
        ''' <summary>The typed end of a resource transfer (100 §15.6), written by the worker
        ''' for a move or a copy the routing carried. What the conditional undo is built
        ''' from - and what tells a completed copy apart from a completed move.</summary>
        Public Property OpOutcome As TransferOutcome
        ''' <summary>The conditional undo record of a resource transfer (100 §15.6 item 5):
        ''' held only for a durable outcome. U's inverse validates against it - the object
        ''' must still be as it was left, a move's original slot must be free - instead of
        ''' moving or deleting blind.</summary>
        Public Property UndoRecord As UndoRecord
        ''' <summary>For an undo operation checked against its record: the conditional
        ''' refusal, ready for the status line. Finishing without an exception and without
        ''' having done anything - the same shape as CopyUndoRefused.</summary>
        Public Property UndoRefusal As String
#End If
    End Class

    Private Sub InitializeFileOperationWorker()
        FileOperationWorker.WorkerSupportsCancellation = True
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>The modern build's transport for file operations: a queue, so presses
    ''' are never refused and the transfer never runs on the UI thread. Created on first
    ''' use - a session that only looks at pictures never pays for it.</summary>
    Private file_Op_Queue As FileOpQueue(Of FileOp)

    Private Function EnsureFileOpQueue() As FileOpQueue(Of FileOp)
        ' A queue whose consumer died is replaced (once that consumer has ended), never reused.
        RecoverFaultedFileOpQueue()
        If file_Op_Queue Is Nothing Then
            ' RunFileOp runs on the worker; the completion comes back on the UI thread.
            file_Op_Queue = New FileOpQueue(Of FileOp)(AddressOf RunFileOp)
            AddHandler file_Op_Queue.OpCompleted, AddressOf FileOpQueue_OpCompleted
        End If
        Return file_Op_Queue
    End Function

    ''' <summary>The work itself, on a worker thread. Touches nothing but its argument.</summary>
    Private Shared Sub RunFileOp(op As FileOp)
#If Not NETFRAMEWORK Then
        If op.RemoteDelete IsNot Nothing AndAlso (op.Kind = FileOpKind.Move OrElse op.Kind = FileOpKind.Copy) Then
            Using target As ResourceDeleteTarget = op.RemoteDelete
                EnsureDestinationFolder(op)
                op.OpOutcome = target.Transfer(If(op.Kind = FileOpKind.Move, TransferKind.Move, TransferKind.Copy), op.Destination, op.ReplaceExisting)
            End Using
            Return
        End If
#End If
        Select Case op.Kind
            Case FileOpKind.Copy
                EnsureDestinationFolder(op)
#If Not NETFRAMEWORK Then
                ' Kept, as a move's is: a copy carried by a resource transfer (into the open
                ' resource, or into a server folder named by a slot) is taken back only through
                ' its record - a plain local copy leaves this Nothing and keeps its own rule.
                Dim copied As TransferOutcome = Nothing
                CopyFileTracked(op.Source, op.Destination, op.ReplaceExisting, copied)
                op.OpOutcome = copied
#Else
                CopyFile(op.Source, op.Destination, op.ReplaceExisting)
#End If
            Case FileOpKind.Move
                EnsureDestinationFolder(op)
#If Not NETFRAMEWORK Then
                Dim done As TransferOutcome = Nothing
                MoveFileTracked(op.Source, op.Destination, op.ReplaceExisting, done)
                op.OpOutcome = done
#Else
                MoveFile(op.Source, op.Destination, op.ReplaceExisting)
#End If
            Case FileOpKind.Delete
#If Not NETFRAMEWORK Then
                Using target As ResourceDeleteTarget = op.RemoteDelete
                    If target Is Nothing Then
                        DeleteFile(op.Source)
                    Else
                        target.Execute()
                    End If
                End Using
#Else
                DeleteFile(op.Source)
#End If
            Case FileOpKind.DeleteUndo
                ' A resource copy's inverse is conditional on its record (100 §15.6 item 5):
                ' the created object must still be exactly as it was left, and only a
                ' provider that can condition the delete may take it back. Asked HERE, on
                ' the worker, for the same reason CopyUndoMayDelete is: the destination may
                ' be a sleeping share.
                If op.UndoRecord IsNot Nothing Then
                    UndoByRecord(op)
                    Return
                End If
                ' Undoing a copy deletes it for good, so only while the original it
                ' duplicates is still in place (046 C-04). Asked HERE, on the worker: the
                ' copy is in the slot folder, which may be a share gone to sleep since the
                ' copy, and asking on the UI thread froze the window for the SMB timeout
                ' (audit 057.S06-2). The refusal rides home on the op.
                Dim original_Path As String = If(op.UndoOrigin Is Nothing, "", op.UndoOrigin.Source)
                If Not CopyUndoMayDelete(original_Path, op.Source) Then
                    op.CopyUndoRefused = True
                Else
                    DeleteCopyForUndo(op.Source)
                End If
            Case FileOpKind.RecycleDelete
                ' Stamped HERE, just before the shell call, not at queue time: the bin's
                ' record is matched against this within a two-second tolerance, and an op
                ' that waited behind a slow copy would otherwise be too old to find it.
                op.DeletedAtUtc = DateTime.UtcNow
                ' The shell call, on its own STA thread - this consumer is a pool thread.
                ' "Sent to the bin" is a claim the shell does not check for us: where it
                ' cannot recycle, it deletes for good without a word (audit 047, C-09).
                ' Believe it only once the bin holds the record.
                op.RecycleFellThrough = RecycleBinIo.SendToBinChecked(op.Source, op.DeletedAtUtc)
            Case FileOpKind.RestoreFromBin
                ' Source is where the file was deleted from (what the bin's record names);
                ' Destination is where it goes now, which differs only if the name was taken
                ' back in the meantime. The outcome rides home on the op - see FinishFileOp.
                op.RestoreOutcome = RecycleBinIo.TryRestore(op.Source, op.DeletedAtUtc, op.Destination)
            Case FileOpKind.RenameUndo
                ' Same folder, so this is a metadata move - but a share that stopped
                ' answering blocks it just as long as any other, hence the queue.
                MoveFile(op.Source, op.Destination)
            Case FileOpKind.MoveUndo
                If op.UndoRecord IsNot Nothing Then
                    UndoByRecord(op)
                    Return
                End If
                MoveFile(op.Source, op.Destination)
        End Select
    End Sub

    ''' <summary>
    ''' An undo inverse validated against its <see cref="UndoRecord"/> (100 §15.6 item 5):
    ''' a move goes back only while the original slot is free, a copy is removed only while
    ''' it is unchanged - by the same state machine that made it, never by a blind move or
    ''' delete. A refusal finishes cleanly with its sentence on the op; a failure throws.
    ''' </summary>
    Private Shared Sub UndoByRecord(op As FileOp)
        Dim undone As TransferOutcome = TransferUndo.Undo(op.UndoRecord, ResourceRouting.TransferScratchDir)
        Select Case undone.Result
            Case TransferResult.Completed
                Return
            Case TransferResult.Refused
                op.UndoRefusal = UndoRefusalText(undone.Reason)
            Case Else
                Throw New TransferOutcomeException(undone, ResourceRouting.DescribeOutcome(undone))
        End Select
    End Sub

    ''' <summary>Why U would not (not "could not") take a resource transfer back - each
    ''' reason a sentence that says what is true of the files, not of the program.</summary>
    Private Shared Function UndoRefusalText(reason As TransferReason) As String
        Select Case reason
            Case TransferReason.DestinationChanged
                Return Localization.T("! Файл, созданный этой операцией, изменился или исчез - отменять нечего")
            Case TransferReason.SourceSlotOccupied
                Return Localization.T("! На исходном месте уже другой файл - перемещённый файл не возвращён")
            Case TransferReason.SourceDeleteUnsupported, TransferReason.NotDeletable
                Return Localization.T("! Этот ресурс не позволяет безопасно отменить копирование")
            Case Else
                Return Localization.T("! Операцию нельзя отменить: её результат изменился")
        End Select
    End Function

    ''' <summary>
    ''' Makes the destination's final segment, on the worker thread, when the slot verdict
    ''' said it may be (§3.6, D5/D6).
    '''
    ''' Only the LAST segment, and only because the probe already found the parent
    ''' answering: CreateDirectory would happily build a whole tree, and "I typed the share
    ''' name wrong and the application created it for me" is exactly what D6 refuses. It is
    ''' re-checked here rather than trusted from the verdict because a verdict is a snapshot
    ''' (§6.2) - if the folder appeared in the meantime, nothing happens.
    '''
    ''' A failure here is an operation failure and travels the ordinary rollback path in
    ''' FinishFileOp, which is what keeps "the folder could not be created" from being
    ''' reported as a move that did not happen.
    ''' </summary>
    Private Shared Sub EnsureDestinationFolder(op As FileOp)
        If op Is Nothing OrElse Not op.CreateDestinationFolder Then Return
#If Not NETFRAMEWORK Then
        ' A server folder (a remote slot, 007 §4.2): the same last-segment rule, asked of the server.
        If ResourceLocator.IsLocator(op.Destination) Then
            Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
            If slots IsNot Nothing AndAlso slots.EnsureFolder(SlotPaths.FolderOf(op.Destination)) Then op.CreatedDestinationFolder = True
            Return
        End If
#End If
        Dim folder As String = Path.GetDirectoryName(op.Destination)
        If String.IsNullOrEmpty(folder) OrElse Directory.Exists(folder) Then Return
        Directory.CreateDirectory(folder)
        op.CreatedDestinationFolder = True
    End Sub

    ''' <summary>Back on the UI thread once an operation has finished.</summary>
    Private Sub FileOpQueue_OpCompleted(sender As Object, done As FileOpQueue(Of FileOp).Completion)
        done.Op.RemoteDelete?.Dispose()
        If Me.IsDisposed Then Return
        FinishFileOp(done.Op, done.Error_Info)
        ShowFileOpQueueDepth()
    End Sub

    ''' <summary>
    ''' The tail of pending transfers has to be visible - on a 200 MB clip over SMB it is
    ''' the only sign that anything is still happening.
    '''
    ''' APPENDED to the line, never written over it (audit 047, C-12): FinishFileOp had just
    ''' written a failure - or the "name was taken, saved as x (2).jpg" tail - and this
    ''' replaced it in the same tick, so a copy that did not happen was never reported at
    ''' all. A depth appended earlier is cut off first, so the line does not grow.
    ''' </summary>
    Private Sub ShowFileOpQueueDepth()
        If file_Op_Queue Is Nothing Then Return
        Dim depth_Label As String = Localization.T("в очереди: ")
        Dim text As String = If(lbl_Status.Text, "")
        If text.StartsWith(depth_Label, StringComparison.Ordinal) Then
            text = ""
        Else
            Dim cut As Integer = text.IndexOf(Queue_Depth_Separator & depth_Label, StringComparison.Ordinal)
            If cut >= 0 Then text = text.Substring(0, cut)
        End If

        Dim n As Integer = file_Op_Queue.PendingCount
        If n > 1 Then text &= If(text.Length = 0, "", Queue_Depth_Separator) & depth_Label & n.ToString()
        If Not String.Equals(lbl_Status.Text, text, StringComparison.Ordinal) Then lbl_Status.Text = text
    End Sub

    Private Const Queue_Depth_Separator As String = " | "
#End If

    ''' <summary>True (and says so) while the single file-operation worker is still
    ''' busy. Checked BEFORE anything is released, mutated or recorded: RunWorkerAsync
    ''' on a busy worker throws, and in Undo that throw was not caught at all.
    '''
    ''' Modern's queue accepts everything WHILE IT IS OPEN, and refuses only once the
    ''' shutdown drain has closed it. That window is real and interactive: FormClosing
    ''' cancels the close and awaits the drain for up to 15 s, so every hotkey still worked -
    ''' and every operation started in it was written off in silence while the UI reported
    ''' "moved"/"deleted". Refusing up front is the honest answer.</summary>
    Private Function FileOpWorkerBusy() As Boolean
#If Not NETFRAMEWORK Then
        RecoverFaultedFileOpQueue()
        If file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.IsClosed Then
            lbl_Status.Text = Localization.T("!Ждите.. завершаются файловые операции, программа закрывается")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2156: file operation refused - queue closed for shutdown")
            Return True
        End If
        ' One batch at a time, and nothing single-file beside it (PB-3): the operations of
        ' a run go through this same queue one by one, and a key pressed meanwhile would
        ' interleave with them and break the strict order the undo history records.
        If BatchBusy() Then
            lbl_Status.Text = Localization.T("! Идёт пакетная операция.. дождитесь её окончания или отмените её")
            Return True
        End If
        Return False
#Else
        If Not FileOperationWorker.IsBusy Then Return False
        lbl_Status.Text = Localization.T("!Ждите.. предыдущая операция ещё выполняется")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2155: file operation refused - worker busy")
        Return True
#End If
    End Function

    ''' <summary>Hands the operation over. Modern queues it (never refused, never on the
    ''' UI thread); net48 keeps the single BackgroundWorker, whose callers checked
    ''' FileOpWorkerBusy() first.</summary>
    Private Sub QueueFileOp(op As FileOp)
#If Not NETFRAMEWORK Then
        If (op.Kind = FileOpKind.Move OrElse op.Kind = FileOpKind.Copy) AndAlso op.RemoteDelete Is Nothing Then
            op.RemoteDelete = ResourceRouting.CaptureDelete(op.Source)
        End If
        If Not EnsureFileOpQueue().Enqueue(op) Then
            op.RemoteDelete?.Dispose()
            ' Belt and braces: FileOpWorkerBusy already refuses everything once the queue is
            ' closed, so this should be unreachable. If it ever is reached, the operation is
            ' NOT going to run and that has to surface as a failure rather than be swallowed.
            AppFileLogger.WriteLine("FileOpQueue refused an operation after shutdown: " &
                                    op.Kind.ToString() & " " & If(op.Source, ""))
            ' Posted, not called: the caller does its optimistic list edit (op.ListIndex = ..)
            ' AFTER this returns, and the rollback in FinishFileOp needs that edit to have
            ' happened - exactly the order a real worker failure arrives in.
            Dim failed As FileOp = op
            Try
                Me.BeginInvoke(New Action(Sub()
                                              If Me.IsDisposed Then Return
                                              FinishFileOp(failed, New InvalidOperationException("File operation queue is closed."))
                                          End Sub))
            Catch ex As Exception
                ' The form has no handle (or is gone): the rollback cannot be posted, so the
                ' refused operation is neither run nor reported on screen - leave a trace.
                AppFileLogger.LogException("FileOpQueue refusal: rollback could not be posted", ex)
            End Try
            Return
        End If
        ShowFileOpQueueDepth()
#Else
        current_File_Op = op
        ' Reset here, on the UI thread, not inside DoWork: the worker starts a moment
        ' later, and a shutdown in between would see the event still signalled.
        fileop_Done.Reset()
        FileOperationWorker.RunWorkerAsync(op)
#End If
    End Sub

    ''' <summary>
    ''' Does a file operation go to the background? Modern: always. Its queue accepts
    ''' everything, and the alternative the checkbox used to offer - running a
    ''' cross-share copy of a 200 MB clip ON THE UI THREAD - is never the right answer.
    ''' net48 keeps the checkbox exactly as it was.
    ''' </summary>
    Private Function UseAsyncFileOps() As Boolean
#If Not NETFRAMEWORK Then
        Return True
#Else
        Return Is_Independent_Thread_For_File_Operations
#End If
    End Function

    ''' <summary>Releases everything holding the current file open, before a file
    ''' operation touches it: GIF animation, the visible box's image AND its stream,
    ''' and VLC playback.
    '''
    ''' StopVlcPlayback runs in BOTH builds on purpose. On net48 the video the IE
    ''' control cannot decode (AVI, ZMBV, VP9, MKV) plays through the very same
    ''' LibVLC and holds the file just as hard, but the net48 branches only ever
    ''' blanked the WebBrowser - so DEL / a move hotkey on such a file failed.</summary>
    Private Sub ReleaseActiveMedia()
#If Not NETFRAMEWORK Then
        content_Placeholder_Path = ""
        ClearEpubPlaceholderData()
        ' Whatever the cache may hold of the file about to be touched is not worth the next look
        ' (014 U-01): it is dropped now rather than found stale later.
        media_Cache.Evict(Current_File_Name)
#End If
        StopGifLoopPlayback()

        ' By visibility, not by is_Second_PictureBox_Active: what is on screen is what
        ' holds the file. (The old sync-move branch tested the wrong box's visibility
        ' and so released nothing at all.)
        If is_PictureBox2_Visible Then
            ReleasePictureBoxMedia(2)
        ElseIf is_PictureBox1_Visible Then
            ReleasePictureBoxMedia(1)
        End If

        If is_Vlc_Playing Then StopVlcPlayback()
        current_Loaded_File_Name = ""

#If Not NETFRAMEWORK Then
        ' After VLC has stopped, never before: the media input reads from the container's
        ' decrypting stream, and pulling that out from under a running player is the one
        ' way to crash inside libvlc. Order is the whole of the rule
        ' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.2).
        SecureReleaseCurrent()
        ReleasePdfDocument()
        ReleaseTextDocument()
#End If

#If NETFRAMEWORK Then
        Web_Browser.DocumentText = ""
#End If
    End Sub

    ''' <summary>
    ''' Lets go of one picture box's image AND the MemoryStream it was decoded from.
    ''' Both halves matter: the stream is deliberately kept open while the image lives
    ''' (that is what releases the file handle), so whoever drops the image owns dropping
    ''' the stream too. Assigning Nothing after Dispose is not cosmetic either - a
    ''' disposed bitmap left on the control throws the moment anything repaints it.
    ''' </summary>
    Private Sub ReleasePictureBoxMedia(box_Index As Integer)
#If Not NETFRAMEWORK Then
        ' Disposed here, never offered to the cache (014 U-01): whoever releases a box is about
        ' to move or delete the file, or has found it unreadable - no picture of it is worth keeping.
        ForgetBoxOrigin(box_Index)
#End If
        If box_Index = 2 Then
            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image.Dispose()
            Picture_Box_2.Image = Nothing
            If pictureBox2_Stream IsNot Nothing Then
                pictureBox2_Stream.Dispose()
                pictureBox2_Stream = Nothing
            End If
        ElseIf box_Index = 1 Then
            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image.Dispose()
            Picture_Box_1.Image = Nothing
            If pictureBox1_Stream IsNot Nothing Then
                pictureBox1_Stream.Dispose()
                pictureBox1_Stream = Nothing
            End If
        End If
    End Sub

    ''' <summary>Where file_Path sits in the active collection, -1 when absent. The two
    ''' collections (array / list) are an old split; asking one question about them in one
    ''' place keeps every caller from repeating the If.</summary>
    Private Function IndexOfFileInList(file_Path As String) As Integer
        If is_Files_Array_Active Then
            Return If(files_Array Is Nothing, -1, Array.IndexOf(files_Array, file_Path))
        End If
        Return If(files_List Is Nothing, -1, files_List.IndexOf(file_Path))
    End Function

    ''' <summary>Removes file_Path from the active collection BY VALUE and returns the
    ''' index it sat at (-1 when absent). Removing by current_File_Index is what let a
    ''' delete drop the wrong entry: the index can run ahead of the file actually on
    ''' screen when a jump's display was thrown away by the throttle.</summary>
    Private Function RemoveCurrentFileFromList(file_Path As String) As Integer
        Dim at As Integer = IndexOfFileInList(file_Path)
        If at < 0 Then Return -1

        If is_Files_Array_Active Then
            files_Array = RemoveAt(files_Array, at)
        Else
            files_List.RemoveAt(at)
        End If
        total_File_Count -= 1

        ' Removing at 'at' shifts everything after it down: an index past the removal
        ' has to follow, an index AT it already points at the next file.
        If current_File_Index > at Then current_File_Index -= 1
        If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
        Return at
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' A free name in the destination, Explorer-style: "IMG_0001 (2).jpg".
    '''
    ''' Sorting photo sessions means the same names keep arriving from different cards,
    ''' and a collision used to stop the conveyor dead: File.Move onto an existing name
    ''' throws, the operation fails, and the user goes off to rename things by hand.
    ''' Modern only - the x86 viewer keeps the historical "it just fails".
    '''
    ''' Resolved BEFORE the operation is queued, so the name it returns is the one both
    ''' the op and the undo history carry: undo has to return the file that was really
    ''' created, not the one we first asked for.
    ''' </summary>
    Private Shared Function ResolveDestinationCollision(dest_Path As String) As String
        If Not File.Exists(dest_Path) Then Return dest_Path

        Dim dir_Path As String = Path.GetDirectoryName(dest_Path)
        Dim base_Name As String = Path.GetFileNameWithoutExtension(dest_Path)
        Dim ext As String = Path.GetExtension(dest_Path)

        For n As Integer = 2 To 9999
            Dim candidate As String = Path.Combine(dir_Path, base_Name & " (" & n.ToString() & ")" & ext)
            If Not File.Exists(candidate) Then Return candidate
        Next

        ' 9998 namesakes in one folder is not a collision any more - let the operation
        ' fail honestly instead of looping.
        Return dest_Path
    End Function

    ''' <summary>
    ''' <see cref="ResolveDestinationCollision"/> for any destination: a folder on this PC as
    ''' before, a server folder (a remote slot, 007 §4.2) through the slot connections - off this
    ''' thread and bounded. False, with the reason on the status line, when the server could not say.
    ''' </summary>
    Private Function TryFreeDestinationName(ByRef destination As String) As Boolean
        If Not ResourceLocator.IsLocator(destination) Then
            destination = ResolveDestinationCollision(destination)
            Return True
        End If
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        Dim wanted As String = destination
        Dim free As String = Nothing
        If slots Is Nothing OrElse
           Not RunBoundedRemoteQuestion(Function() BatchPlan.FreeName(wanted, Function(candidate As String) slots.FileExists(candidate), Nothing), free) Then
            lbl_Status.Text = Localization.T("! Сервер не ответил, свободно ли имя файла, - действие отменено")
            Return False
        End If
        destination = free
        Return True
    End Function

    ''' <summary>Is there a file at this server-folder address? Nothing when the server could not
    ''' say within the bound - never read as "free".</summary>
    Private Shared Function RemoteNameTaken(fileLocator As String) As Boolean?
        Dim slots As RemoteSlotConnections = RemoteSlotConnections.Current
        If slots Is Nothing Then Return Nothing
        Dim taken As Boolean
        If Not RunBoundedRemoteQuestion(Function() slots.FileExists(fileLocator), taken) Then Return Nothing
        Return taken
    End Function

    ''' <summary>How long the window may wait for one question to a server folder that was probed a
    ''' moment ago - a live connection answers a stat in milliseconds; anything near this bound is a
    ''' link that has just died, and the action stops instead of guessing.</summary>
    Private Const Remote_Question_Timeout_Ms As Integer = 3000

    ''' <summary>Runs one question to a server off the UI thread and waits for it, bounded. False when
    ''' it ran out of time or failed - the failure's class goes to the log, never to the screen.</summary>
    Private Shared Function RunBoundedRemoteQuestion(Of T)(question As Func(Of T), ByRef answer As T) As Boolean
        Dim job As Task(Of T) = Task.Run(question)
        Try
            If Not job.Wait(Remote_Question_Timeout_Ms) Then
                AppFileLogger.WriteLine("Remote slot: the server did not answer within " & Remote_Question_Timeout_Ms.ToString() & " ms")
                Return False
            End If
            answer = job.Result
            Return True
        Catch ex As AggregateException
            AppFileLogger.WriteLine("Remote slot: a question to the server failed - " & ResourceFailure.FromException(ex).ToString())
            Return False
        End Try
    End Function

    ''' <summary>The first-use warning of the "replace" collision policy has been answered
    ''' Yes in this session (SETTINGS_EXPANSION §3.1; audit 047, C-11).</summary>
    Private replace_Warned_This_Session As Boolean = False

    Private Enum CollisionResolution
        Proceed
        Skip
        Cancel
    End Enum

    ''' <summary>Size and date of one of the two files of a name collision, for the window that asks. A file on a
    ''' server (a locator) has no local stat: it is shown without them rather than read over the network on the
    ''' UI thread.</summary>
    Private Shared Function CollisionFactsOf(path As String) As AskFileFacts
        If String.IsNullOrEmpty(path) OrElse ResourceLocator.IsLocator(path) Then Return New AskFileFacts(-1, Nothing)
        Return AskFileFacts.Read(path)
    End Function

    ''' <summary>Resolves the user's configured policy before an operation reaches the
    ''' queue. In particular, Skip and Cancel never mutate the source or its list entry.</summary>
    Private Function ResolveConfiguredCollision(ByRef destination As String, ByRef statusNote As String,
                                                ByRef replaceExisting As Boolean, sourcePath As String) As CollisionResolution
        Dim name_Taken As Boolean
        If ResourceLocator.IsLocator(destination) Then
            ' A server folder (007 §4.2): the slot was probed a moment ago, so this is one stat on a
            ' live connection - but bounded, and "the server could not say" stops the action rather
            ' than being read as "the name is free".
            Dim answer As Boolean? = RemoteNameTaken(destination)
            If Not answer.HasValue Then
                lbl_Status.Text = Localization.T("! Сервер не ответил, свободно ли имя файла, - действие отменено")
                Return CollisionResolution.Cancel
            End If
            name_Taken = answer.Value
        Else
            name_Taken = File.Exists(destination)
        End If
        If Not name_Taken Then Return CollisionResolution.Proceed

        Dim policy As String = If(modern_Preferences Is Nothing, "ask", modern_Preferences.NameCollisionPolicy)
        Select Case policy
            Case "skip"
                lbl_Status.Text = Localization.T("Файл с таким именем уже есть: операция пропущена")
                Return CollisionResolution.Skip
            Case "rename"
                If Not TryFreeDestinationName(destination) Then Return CollisionResolution.Cancel
                statusNote = Localization.TF(" (имя занято, сохранён как {0})", SlotPaths.FileNameOf(destination))
                Return CollisionResolution.Proceed
            Case "replace"
                ' SETTINGS_EXPANSION §3.1 asks for a warning the first time "replace" is
                ' about to overwrite something - it had never been built (audit 047, C-11).
                ' Once per session, like the other "don't ask again" of APP-BEHAVIOUR rule 5:
                ' the setting is the user's standing answer, the warning only makes sure
                ' they know it is being acted on. The default is No.
                If Not replace_Warned_This_Session Then
                    ' Named answers through the dialog layer (124 UX-205): the default is the way out, as before.
                    If AskDialogs.Choose(Me, AskSpecs.ReplacePolicyWarning(SlotPaths.FileNameOf(destination))) <> 0 Then
                        Return CollisionResolution.Cancel
                    End If
                    replace_Warned_This_Session = True
                End If
                replaceExisting = True
                statusNote = Localization.T(" (существующий файл будет заменён)")
                Return CollisionResolution.Proceed
            Case Else
                ' Three NAMED answers, in the interface language and its reading direction (124 UX-205): Replace,
                ' Keep both, Skip - with the size and date of both files on the window, so it is not a blind
                ' overwrite question. The DEFAULT - and Escape - is Skip: sorting is a run of key presses, and
                ' the Space or Enter already typed for the next picture lands on this window; with the stock
                ' default that was "Yes" and it overwrote a different photo (audit 046, C-01). Replacing now
                ' takes a click or the Alt accelerator (AskSpecs.NameCollision, held by a test).
                Dim answer As Integer = AskDialogs.Choose(Me, AskSpecs.NameCollision(
                    SlotPaths.FileNameOf(destination),
                    CollisionFactsOf(destination), CollisionFactsOf(sourcePath)))
                If answer = AskSpecs.CollisionSkip Then Return CollisionResolution.Cancel
                If answer = AskSpecs.CollisionKeepBoth Then
                    If Not TryFreeDestinationName(destination) Then Return CollisionResolution.Cancel
                    statusNote = Localization.TF(" (имя занято, сохранён как {0})", SlotPaths.FileNameOf(destination))
                Else
                    replaceExisting = True
                    statusNote = Localization.T(" (существующий файл будет заменён)")
                End If
                Return CollisionResolution.Proceed
        End Select
    End Function
#End If

    ''' <summary>Puts file_Path back at a clamped position and points the index at it.
    ''' Undo used to Insert at current_File_Index verbatim - after moving away the last
    ''' file of a folder that index is -1 and Insert(-1) threw.</summary>
    Private Sub InsertFileIntoList(file_Path As String, at As Integer)
        Dim insert_At As Integer = Math.Max(0, Math.Min(at, total_File_Count))

        If is_Files_Array_Active Then
            If files_Array Is Nothing Then files_Array = New String() {}
            files_Array = AddAt(files_Array, file_Path, insert_At)
        Else
            If files_List Is Nothing Then files_List = New List(Of String)()
            files_List.Insert(insert_At, file_Path)
        End If
        total_File_Count += 1
        current_File_Index = insert_At
    End Sub

    ''' <summary>
    ''' Swaps one path for another in place - what a rename does to the list, and what
    ''' undoing one does to it again.
    '''
    ''' By VALUE only. The remembered position used to be a fallback, and that fallback is
    ''' exactly the case where the list is another folder's: an undo pressed after changing
    ''' folder overwrote whatever unrelated row sat at the old index (audit 047, C-13c).
    ''' The index is left where it is - the caller decides whether the view follows.
    ''' Returns the position it wrote at, or -1 when the old path is not in the list.
    ''' </summary>
    Private Function ReplaceFileInList(old_Path As String, new_Path As String) As Integer
        Dim at As Integer = IndexOfFileInList(old_Path)
        If at < 0 Then Return -1

        If is_Files_Array_Active Then
            If files_Array Is Nothing OrElse at >= files_Array.Length Then Return -1
            files_Array(at) = new_Path
        Else
            If files_List Is Nothing OrElse at >= files_List.Count Then Return -1
            files_List(at) = new_Path
        End If
        Return at
    End Function

    Private Sub RenameCurrentFile()
#If Not NETFRAMEWORK Then
        ' DEL and the recipient slots already refuse inside an archive; a rename silently
        ' renamed the extracted temporary copy and changed nothing about the archive
        ' (audit 047, C-18). The same point refuses a resource that cannot rename (006 PV-7).
        If ResourceBlocksFileOperations(ResourceVerb.Rename) Then Return
#End If
        ' THE file this rename is about, captured once (audit 047, C-06). The InputBox below
        ' runs a modal message loop, and that loop runs queued completions and forwarded
        ' launches that reassign Current_File_Name - a copy finishing while the box was open
        ' flipped to the next file, and THAT file was renamed, with the first one's
        ' extension. Nothing after the prompt reads Current_File_Name again.
        Dim source_Path As String = Current_File_Name
        ' Set once the media has been let go of: from then on a failure leaves a blank window
        ' unless the Catch shows the file again (audit 057.S06-4).
        Dim media_Released As Boolean = False
        Try
            If Not My.Computer.FileSystem.FileExists(source_Path) Then
                lbl_Status.Text = Localization.T("! Файл не найден")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1250: file not found")
                Return
            End If
            Dim current_File_Name_Without_Extension As String = Path.GetFileNameWithoutExtension(source_Path)
            Dim current_File_Extension As String = Path.GetExtension(source_Path)
#If Not NETFRAMEWORK Then
            ' One themed prompt in the viewer's band (124 UX-204): a refused name keeps what was typed and the
            ' reason sits beside the field, where the InputBox closed, threw the text away and left only a
            ' status line. Cancel (Nothing) and an empty text are different answers now.
            Dim rename_Folder As String = Path.GetDirectoryName(source_Path)
            Dim new_File_Name As String = AskDialogs.Prompt(Me, AskSpecs.RenamePrompt(
                current_File_Name_Without_Extension, current_File_Extension,
                Function(candidate As String) As Boolean
                    Dim candidate_Path As String = Path.Combine(rename_Folder, candidate)
                    Return File.Exists(candidate_Path) AndAlso Not String.Equals(candidate_Path, source_Path, StringComparison.OrdinalIgnoreCase)
                End Function))
            If new_File_Name Is Nothing Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1260: rename cancelled")
                Return
            End If
#Else
            Dim new_File_Name As String = InputBox(Localization.T("Введите новое имя файла:"),
                                            Localization.T("Переименование файла"),
                                            current_File_Name_Without_Extension)
            If String.IsNullOrEmpty(new_File_Name) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1260: empty new file name - no rename")
                Return
            End If
#End If

            If Not String.Equals(Current_File_Name, source_Path, StringComparison.Ordinal) Then
                lbl_Status.Text = Localization.T("! Файл на экране сменился, пока было открыто окно - действие отменено")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1262: rename cancelled - the file on screen changed during the prompt")
                Return
            End If

            ' A name, not a path: "..\x", "sub\x" or "D:\other\x" went through Path.Combine,
            ' where a rooted second argument wins, and "renamed" the file into another
            ' folder or onto another drive (audit 047, C-18).
            If Not FileOpRules.IsValidNewFileName(new_File_Name) Then
                lbl_Status.Text = Localization.T("! Недопустимое имя файла: символы \ / : * ? < > | в нём нельзя")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1265: rename refused - not a plain file name")
                Return
            End If

            Dim current_Directory_Path As String = Path.GetDirectoryName(source_Path)
            Dim new_File_Full_Path As String = Path.Combine(current_Directory_Path, new_File_Name & current_File_Extension)
            If new_File_Full_Path = source_Path Then
                lbl_Status.Text = Localization.T("! Имя не изменено")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1270: file is not new")
                Return
            End If

            ' Say it plainly: File.Move onto an existing name throws IOException, which
            ' surfaced as a bare "E011" MsgBox. A case-only rename (photo -> Photo) is the
            ' same file answering File.Exists, not a collision - RenameFile supports it.
            If File.Exists(new_File_Full_Path) AndAlso
               Not String.Equals(new_File_Full_Path, source_Path, StringComparison.OrdinalIgnoreCase) Then
                lbl_Status.Text = Localization.T("! Файл с таким именем уже есть")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1275: rename target already exists")
                Return
            End If

            ' A rename is a File.Move, so it needs delete access: a playing video holds
            ' the file (LibVLC in both builds, IE on net48) and the rename just failed
            ' with E011. PoMove has always released the media first - so must this.
            Dim renamed_At As Integer = IndexOfFileInList(source_Path)
            ReleaseActiveMedia()
            media_Released = True

            ' Follow the path RenameFile actually created, never one we assembled here:
            ' the two used to diverge, and the list kept a name that was not on disk.
#If Not NETFRAMEWORK Then
            Dim original_Path As String = source_Path
#End If
            Dim renamed_Path As String = RenameFile(source_Path, new_File_Name & current_File_Extension)

            ' By value, not by current_File_Index - the index can point elsewhere.
            If renamed_At >= 0 Then
                If is_Files_Array_Active Then
                    files_Array(renamed_At) = renamed_Path
                Else
                    files_List(renamed_At) = renamed_Path
                End If
                current_File_Index = renamed_At
            End If
            Current_File_Name = renamed_Path

#If Not NETFRAMEWORK Then
            ' The one recorded operation that does not go through the queue: a rename is a
            ' same-folder metadata move that has already returned by this line, so "push on
            ' success only" is satisfied here - RenameFile either threw or it did not.
            RecordUndo(New FileOp With {.Kind = FileOpKind.Rename,
                                        .Source = original_Path,
                                        .Destination = renamed_Path,
                                        .ListIndex = renamed_At,
                                        .ListGeneration = file_List_Generation})
#End If

            lbl_Status.Text = Localization.TF("Файл переименован: {0}{1}", new_File_Name, current_File_Extension)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1280: file is renamed")

            ReadShowMediaFile(Mode_SetFile)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1290: ERR: " & ex.Message)
            ' The rename threw after the media was released - the file locked by another
            ' program, a share error, an ACL: the picture was disposed and nothing showed it
            ' again, so the user faced a blank window with an error line (audit 057.S06-4).
            ' Show the file the list still names, then put the error line back on top.
            If media_Released AndAlso Not String.IsNullOrEmpty(Current_File_Name) AndAlso File.Exists(Current_File_Name) Then
                Try
                    ReadShowMediaFile(Mode_SetFile)
                Catch show_Ex As Exception
                    AppFileLogger.LogException("Rename failed: the file could not be shown again", show_Ex)
                End Try
            End If
            ReportOperationError("E011", ex, FolderOfFile(source_Path))
            lbl_Status.Text = Localization.T("! Ошибка переименования")
        End Try
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles bt_Delete.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: bt_Delete")
        SlideShowStop()
        ReadShowMediaFile(Mode_Delete)
    End Sub

    ''' <summary>Only a non-blank destination makes a digit an active recipient key.</summary>
    Private Shared Function IsRecipientSlotConfigured(ByVal move_Slot_index As Integer) As Boolean
        If move_Slot_index < 1 OrElse move_Slot_index > 10 Then Return False
        Return Not String.IsNullOrWhiteSpace(Hardkeys_to_move_mediafile(move_Slot_index))
    End Function

    ''' <summary>
    ''' The historical entry point, kept for the callers that mean "do the usual thing
    ''' with this slot" - and the ONLY place where the global copy mode is still read.
    ''' On the mainline it always means Move: the global mode is gone from that UI
    ''' (there is nothing left to switch it with), and every surface that wants a copy
    ''' asks for one by name. net48 keeps its checkbox and behaves exactly as before.
    ''' </summary>
    Private Sub PoMove(ByVal move_Slot_index As Integer)
        If Not IsRecipientSlotConfigured(move_Slot_index) Then Return
#If NETFRAMEWORK Then
        ExecuteRecipientAction(move_Slot_index, If(Is_Copying_not_Moving, RecipientActionKind.Copy, RecipientActionKind.Move))
#Else
        ExecuteRecipientAction(move_Slot_index, RecipientActionKind.Move)
#End If
    End Sub

    ''' <summary>
    ''' Moves or copies the current file into recipient slot <paramref name="move_Slot_index"/>.
    ''' One authoritative implementation for both actions and for every entry point:
    ''' same destination resolution, same collision handling, same queue, same history,
    ''' same undo. Only <paramref name="action"/> differs.
    ''' </summary>
    Private Sub ExecuteRecipientAction(ByVal move_Slot_index As Integer, ByVal action As RecipientActionKind)
        If Not IsRecipientSlotConfigured(move_Slot_index) Then Return
#If Not NETFRAMEWORK Then
        ' A slot may name a folder of a saved resource (007 §4.2) - "sftp://user@host:port/folder".
        ' Everything below works on it through SlotPaths and the slot connections: the gate dials and
        ' probes it, the transfer is the staged, content-verified one of the provider layer.
        ' Every move/copy route - the digit keys, the recipients overlay, both media menus,
        ' the settings grid, the thumbnail panel - ends here, which is why the archive's
        ' single refusal point sits here too (§7, invariant 6) rather than at each of them.
        ' A resource is asked about the verb itself: copying OUT of a read-only one only
        ' reads it, moving out of it deletes there (006 PV-7).
        If ResourceBlocksFileOperations(If(action = RecipientActionKind.Copy, ResourceVerb.Copy, ResourceVerb.Move)) Then Return
#End If

        Dim is_Copy As Boolean = (action = RecipientActionKind.Copy)
        Dim destination_Folder_Path As String = Hardkeys_to_move_mediafile(move_Slot_index)
        Dim move_Slot_Key As String = move_Slot_index.ToString
        If move_Slot_Key = "10" Then move_Slot_Key = "0"

        ' THE file this action is about, captured once (audit 047, C-06). The collision
        ' prompt below is modal, its message loop runs queued completions and forwarded
        ' launches, and those reassign Current_File_Name: a rollback arriving during the
        ' prompt used to send ANOTHER file into the slot under this one's name. Nothing
        ' after the prompt reads Current_File_Name again except to see that it moved.
        Dim source_Path As String = Current_File_Name
        If source_Path = "" Then
            lbl_Status.Text = Localization.T("! Нет файла ")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1740: No file")
            Return
        End If

        ' Declared on both runtimes - the FileOp below reads it unconditionally. Only the
        ' mainline can ever set it: the health verdict that decides it is modern-only, so
        ' net48 keeps its historical behaviour (a missing destination fails in the worker).
        Dim create_Destination As Boolean = False
#If Not NETFRAMEWORK Then
        ' THE gate (§3.4, invariant 8). It sits here - before ReleaseActiveMedia, before the
        ' optimistic list removal, before QueueFileOp - because a dead NAS used to cost
        ' twenty queued transfers and twenty files rolling back into the list minutes later
        ' (§0.2). False means the action must not happen: either it was refused with a reason
        ' on the status line, or a probe was started and this call will be made again with
        ' the answer in hand.
        If Not SlotHealthAllowsAction(move_Slot_index, action, create_Destination) Then Return
#End If

        Try
#If NETFRAMEWORK Then
            Dim source_File_Info As System.IO.FileInfo = My.Computer.FileSystem.GetFileInfo(source_Path)
            Dim destination_Folder_Full_Path As String = destination_Folder_Path & "\" & source_File_Info.Name

            ' A slot pointing at the folder we are viewing: File.Move(a, a) succeeds,
            ' so the file used to be dropped from the list while sitting right where it
            ' was, and a copy onto itself threw a bare "E014".
            If String.Equals(Path.GetDirectoryName(destination_Folder_Full_Path),
                             Path.GetDirectoryName(source_Path), StringComparison.OrdinalIgnoreCase) Then
#Else
            ' The file's OWN name. A file of an open resource is listed as its cache copy,
            ' "00042_photo.jpg", and that index prefix used to become the name in the slot.
            Dim destination_Folder_Full_Path As String = SlotPaths.Combine(destination_Folder_Path, TransferNameOf(source_Path))
            Dim is_Remote_Slot As Boolean = ResourceLocator.IsLocator(destination_Folder_Path)

            ' A slot pointing at the folder we are viewing: File.Move(a, a) succeeds,
            ' so the file used to be dropped from the list while sitting right where it
            ' was, and a copy onto itself threw a bare "E014". Compared where the file really
            ' is - for a resource file, its folder on the server, not the cache.
            If SlotPaths.SameFolder(destination_Folder_Full_Path, LogicalPathOf(source_Path)) Then
#End If
                lbl_Status.Text = Localization.T("! Файл уже в этой папке")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1683: destination equals current folder")
                Return
            End If
#If Not NETFRAMEWORK Then
            ' The same folder under another spelling - a mapped drive and its UNC path, a
            ' server's name and its address - passes the string test above, and the name
            ' "collision" is then the file with itself: "replace" deleted it (audit 046,
            ' C-02). Asked by file identity, and only when the name is taken at all, which
            ' is the only case where it can be the same file. Mainline only: net48 never
            ' replaces, so there the same situation already fails safely as "exists". A server
            ' folder has no file identity to ask; the transfer itself refuses the same path.
            If Not is_Remote_Slot AndAlso File.Exists(destination_Folder_Full_Path) AndAlso
               FileManager.IsSameFile(source_Path, destination_Folder_Full_Path) Then
                lbl_Status.Text = Localization.T("! Файл уже в этой папке")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1684: destination is the same file under another path")
                Return
            End If
#End If

            Dim use_Worker As Boolean = UseAsyncFileOps()
            If use_Worker AndAlso FileOpWorkerBusy() Then Return

            ' Said out loud when the name had to change - the destination path in the
            ' status line is long, and a quiet "(2)" inside it is easy to miss.
            Dim collision_Note As String = ""
            ' Declared on BOTH runtimes - it is read unconditionally by the FileOp below.
            ' Only the configured collision policy that can set it is mainline-only
            ' (ResolveConfiguredCollision lives inside the #If Not NETFRAMEWORK block
            ' above), so net48 keeps its historical behavior: never replace.
            Dim replace_Existing As Boolean = False
#If Not NETFRAMEWORK Then
            Dim collision As CollisionResolution = ResolveConfiguredCollision(destination_Folder_Full_Path, collision_Note, replace_Existing, source_Path)
            If collision <> CollisionResolution.Proceed Then Return

            ' The prompt (and the first-use "replace" warning) ran a modal message loop. If
            ' anything changed the file on screen meanwhile, the answer was given about a
            ' file that is no longer the one in front of the user - do nothing (C-06).
            If Not String.Equals(Current_File_Name, source_Path, StringComparison.Ordinal) Then
                lbl_Status.Text = Localization.T("! Файл на экране сменился, пока было открыто окно - действие отменено")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1686: slot action cancelled - the file on screen changed during the prompt")
                Return
            End If
#End If

#If NETFRAMEWORK Then
            ' The queued operation is recorded now and FinishFileOp clears the record if it
            ' fails. The synchronous one is recorded only once it has returned (below): it
            ' used to be written here, BEFORE the copy, so a copy refused because the slot
            ' already held a different file left U pointing at that file - and U deleted it
            ' (audit 046, C-03).
            If use_Worker Then RememberNet48Undo(source_Path, destination_Folder_Full_Path, is_Copy)
#End If
            ' The mainline records nothing here on purpose: the entry is pushed by
            ' FinishFileOp once the operation has actually succeeded (D8).

            Dim op As New FileOp With {
                .Kind = If(is_Copy, FileOpKind.Copy, FileOpKind.Move),
                .Source = source_Path,
                .Destination = destination_Folder_Full_Path,
                .SlotKey = move_Slot_Key,
                .StatusNote = collision_Note,
                .ReplaceExisting = replace_Existing,
                .CreateDestinationFolder = create_Destination,
                .ListGeneration = file_List_Generation
            }

#If Not NETFRAMEWORK Then
            Dim afterOperation As String = If(modern_Preferences Is Nothing, "next", modern_Preferences.AfterFileOperation)
            If is_Copy Then
                ' The copy leaves the file where it is, so moving on is a choice, and it is
                ' the "Go to the next file after copying" checkbox that makes it (audit 047,
                ' C-11 - the checkbox had been saved and read by nothing since 30a3960). With
                ' it off the same file stays on screen: filing one picture into several
                ' folders. "Stay after an operation" keeps it there too.
                op.NavigateAfterSuccess = afterOperation <> "stay" AndAlso AdvanceAfterCopy()
            Else
                op.NavigateAfterSuccess = afterOperation <> "stay"
            End If
            op.CloseIfEmptyAfterSuccess = afterOperation = "closeIfEmpty"
#End If

            If is_Copy Then
                SayProgress(Localization.TF("!Ждите.. Файл копируется ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path))

                If use_Worker Then
                    QueueFileOp(op)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1690: file is copied ASYNC to " & destination_Folder_Full_Path)
                Else
                    CopyFile(op.Source, op.Destination, op.ReplaceExisting)
#If NETFRAMEWORK Then
                    RememberNet48Undo(op.Source, op.Destination, True)
#End If
                    lbl_Status.Text = Localization.TF("файл скопирован ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path) & collision_Note
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1715: file is copied to " & destination_Folder_Full_Path)
                End If

#If NETFRAMEWORK Then
                ' net48 always advances, as it always has. The mainline advances only once
                ' the copy has succeeded (FinishFileOp): a failed copy is still the file on
                ' screen, and its error has to be about that file.
                ReadShowMediaFile(Mode_Next)
#End If
            Else
                ' Moving needs delete access to the file, so let go of it first.
                ReleaseActiveMedia()

                SayProgress(Localization.TF("!Ждите.. Файл переносится ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path))

                If use_Worker Then
                    QueueFileOp(op)
                    ' Optimistic: sorting must feel instant. The price is a rollback in
                    ' FinishFileOp if the move fails - not a file that silently vanished
                    ' from the view while still sitting in the folder.
                    op.ListIndex = RemoveCurrentFileFromList(op.Source)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1705: file is moved ASYNC to " & destination_Folder_Full_Path)
                Else
                    MoveFile(op.Source, op.Destination, op.ReplaceExisting)
#If NETFRAMEWORK Then
                    RememberNet48Undo(op.Source, op.Destination, False)
#End If
                    op.ListIndex = RemoveCurrentFileFromList(op.Source)
                    lbl_Status.Text = Localization.TF("файл перенесён ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path) & collision_Note
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1729: file is moved to " & destination_Folder_Full_Path)
                End If

                ' Show the file that slid into the moved one's place, NOW - sync and queued
                ' alike, on both builds (audit 047, C-05). 30a3960 made this conditional on a
                ' flag only the mainline sets and moved the queued case to the completion,
                ' so the x86 viewer stayed blank after every move with Current_File_Name
                ' still naming the moved file - and the next arrow press, stepping from an
                ' index that already pointed at the following file, skipped one unseen. On
                ' the mainline "stay" means the same thing for a move: the file is gone, so
                ' what stays is the position. A failed move rolls back and shows it again.
                ReadShowMediaFile(Mode_SetFile)
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1730: E014 " & ex.Message)
            ReportOperationError("E014", ex, destination_Folder_Path)
        End Try
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' Walks the history back, one operation per press, most recent first.
    '''
    ''' It never silently does nothing (invariant 7): either it reverses the operation, or
    ''' it says which of the named reasons stops it. That is why a permanent deletion is
    ''' RECORDED rather than skipped - "this file was deleted for good, there is nothing to
    ''' bring back" is a true answer, and "there is no history" would not be.
    ''' </summary>
    ''' <summary>Whether U has anything to take back - the media menus show their "Undo" row disabled
    ''' while the history is empty (125 UX-324). Reads the stack only.</summary>
    Friend Function UndoAvailable() As Boolean
        Return undo_Entries.Peek() IsNot Nothing
    End Function

    Private Sub Undo()
        If FileOpWorkerBusy() Then Return
        ' Undo moves a file back from wherever it went, and inside an archive the list is the
        ' archive's: the file would land in a real folder and its path in the archive's list
        ' (audit 052 G-17). The one refusal point says so; U works again after leaving.
        If ResourceBlocksFileOperations(ResourceVerb.Move) Then Return

        ' The history holds COMPLETED operations only, so while anything is still queued the
        ' top of it is an older operation than the one just made: "1" on a slow SMB move
        ' followed at once by U moved back a file from minutes ago, into a folder the user
        ' was not looking at (audit 047, C-14). Say so instead; U works once the queue is
        ' done, and then it takes back the operation the user means.
        If file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0 Then
            lbl_Status.Text = Localization.T("! Подождите: файловые операции ещё выполняются - отмена станет доступна после них")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1809: undo refused - operations still queued")
            Return
        End If

        Dim entry As FileOp = undo_Entries.Peek()
        If entry Is Nothing Then
            lbl_Status.Text = Localization.T("! Нет истории о переносе")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1810: No history about moved files")
            Return
        End If

        Dim plan As UndoPlan = UndoPolicy.PlanFor(entry.Kind)

        ' A whole batch is one entry and one gesture (PB-5): taken back as a run of
        ' inverses, last first, with one report. Popped either way - the spec is explicit
        ' that a partly reversible batch is consumed and reported, because leaving it on the
        ' stack would invite a second reversal of the files that already came back.
        If plan = UndoPlan.ReverseBatch Then
            undo_Entries.Pop()
            StartBatchUndo(entry.Batch)
            Return
        End If

        ' The two answers that consume the entry without touching a file. They are popped
        ' too: an entry that can only ever produce the same refusal would otherwise block
        ' every older operation behind it for good.
        If plan = UndoPlan.RefusePermanent Then
            undo_Entries.Pop()
            lbl_Status.Text = If(entry.PermanentDeleteReason = PermanentReason.UserAsked,
                                 Localization.T("! Файл был удалён безвозвратно по вашему выбору - возвращать нечего"),
                                 Localization.T("! Файл был удалён безвозвратно: Корзины в том расположении нет - возвращать нечего"))
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1811: undo refused - the file was deleted permanently")
            Return
        End If

        Try
            Select Case plan
                Case UndoPlan.DeleteTheCopy
                    undo_Entries.Pop()
                    ' Undoing a copy deletes it for good, so it may only happen while the
                    ' original still exists: after a DEL on the original the copy is the one
                    ' instance left, and U used to destroy it (audit 046, C-04). The worker
                    ' asks, not this thread - the slot folder may be a sleeping share
                    ' (057.S06-2) - and a refusal is reported in FinishFileOp. Popped either
                    ' way - the entry could only ever refuse again.
                    Dim op As New FileOp With {.Kind = FileOpKind.DeleteUndo, .Source = entry.Destination,
                                               .UndoOrigin = entry, .UndoRecord = entry.UndoRecord,
                                               .ListGeneration = file_List_Generation}
                    SayProgress(Localization.TF("!Ждите. Файл удаляется в каталоге {0}", entry.Destination))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")
                    QueueFileOp(op)

                Case UndoPlan.MoveBack
                    undo_Entries.Pop()
                    Dim op As New FileOp With {
                        .Kind = FileOpKind.MoveUndo,
                        .Source = entry.Destination,
                        .Destination = entry.Source,
                        .UndoOrigin = entry,
                        .UndoRecord = entry.UndoRecord,
                        .ListGeneration = file_List_Generation
                    }
                    SayProgress(Localization.TF("!Ждите. Возвращается в каталог {0}", entry.Source))
                    ' Only the file being moved has to be let go of - the slot folder can be
                    ' the one on screen, with that very file playing. Anything else on screen
                    ' is replaced by the display below, or stays, when nothing is inserted.
                    If String.Equals(Current_File_Name, entry.Destination, StringComparison.OrdinalIgnoreCase) Then ReleaseActiveMedia()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1780: undo move async deletion")
                    QueueFileOp(op)
                    ' Optimistic and clamped, exactly as the move was - but only into the
                    ' list the file belongs in (audit 047, C-13): the list the move came from,
                    ' or a list of the folder it is going back to. Pressed while looking at
                    ' the slot folder or any other one, it used to plant the original path in
                    ' that folder's list. ListIndex -1 records that nothing was inserted.
                    If FileOpRules.ListStampMatches(entry.ListGeneration, file_List_Generation) OrElse
                       IsListOfFolder(FolderOfFile(entry.Source)) Then
                        op.ListIndex = If(entry.ListIndex >= 0, entry.ListIndex, current_File_Index)
                        InsertFileIntoList(op.Destination, op.ListIndex)
                        ' Until the worker lands it, the row names a file that is not there
                        ' yet - navigating onto it must not drop it as "not found" (C-13e).
                        files_Arriving.Add(op.Destination)
                        ReadShowMediaFile(Mode_AfterUndo)
                    End If

                Case UndoPlan.RenameBack
                    ' The file has to still be where the rename left it. If something else
                    ' moved or deleted it in between, say so instead of failing inside the
                    ' worker with a bare exception.
                    If Not File.Exists(entry.Destination) Then
                        undo_Entries.Pop()
                        lbl_Status.Text = Localization.T("! Файл не найден")
                        Return
                    End If
                    undo_Entries.Pop()
                    Dim op As New FileOp With {
                        .Kind = FileOpKind.RenameUndo,
                        .Source = entry.Destination,
                        .Destination = entry.Source,
                        .ListIndex = entry.ListIndex,
                        .UndoOrigin = entry,
                        .ListGeneration = file_List_Generation
                    }
                    SayProgress(Localization.T("!Ждите. Возвращается прежнее имя.."))
                    ' Only the renamed file has to be let go of, exactly as MoveBack does: U
                    ' for an old rename while another file is on screen blanked THAT file,
                    ' and nothing showed it again (audit 057.S06-3).
                    If String.Equals(Current_File_Name, entry.Destination, StringComparison.OrdinalIgnoreCase) Then ReleaseActiveMedia()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1782: undo rename")
                    QueueFileOp(op)

                Case UndoPlan.RestoreFromBin
                    ' The folder is asked about HERE, before anything is queued, because its
                    ' absence is the one refusal we can be sure of without touching the bin -
                    ' and because the answer to it is "we do not recreate it" (§3.6), not
                    ' "try and see". A tree the user deleted stays deleted.
                    Dim folder As String = Path.GetDirectoryName(entry.Source)
                    If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then
                        undo_Entries.Pop()
                        lbl_Status.Text = Localization.T("! Папка, из которой файл был удалён, больше не существует")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1813: undo restore refused - the folder is gone")
                        Return
                    End If

                    undo_Entries.Pop()
                    ' The destination is resolved before the operation is queued, exactly as
                    ' a move's is: the name may have been taken since the deletion, and the
                    ' status line has to be able to say which name the file came back under.
                    Dim op As New FileOp With {
                        .Kind = FileOpKind.RestoreFromBin,
                        .Source = entry.Source,
                        .Destination = ResolveDestinationCollision(entry.Source),
                        .ListIndex = If(entry.ListIndex >= 0, entry.ListIndex, current_File_Index),
                        .DeletedAtUtc = entry.DeletedAtUtc,
                        .UndoOrigin = entry,
                        .ListGeneration = entry.ListGeneration}
                    SayProgress(Localization.T("!Ждите. Файл возвращается из Корзины.."))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1814: undo restore from the Recycle Bin")
                    QueueFileOp(op)
                    ' Deliberately NOT optimistic, unlike the move above: this operation can
                    ' honestly come back with nothing (the bin was emptied), and a list entry
                    ' for a file that never returned is the one thing invariant 8 forbids.
            End Select
        Catch ex As Exception
            ReportOperationError("E016", ex)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1800: undo E016 " & ex.Message)
        End Try
    End Sub

    ''' <summary>Paths a queued move-back will land at, whose list rows exist before the
    ''' files do (audit 047, C-13e). Only ever touched on the UI thread.</summary>
    Private ReadOnly files_Arriving As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Is the list on screen the scan of this folder? The honest question for "does
    ''' a file coming back to <paramref name="folder"/> belong in it".</summary>
    Private Function IsListOfFolder(folder As String) As Boolean
        If String.IsNullOrEmpty(folder) Then Return False
        If files_List Is Nothing AndAlso files_Array Is Nothing Then Return False
        Return String.Equals(folder_List_Loaded_For, folder, StringComparison.OrdinalIgnoreCase)
    End Function
#Else
    ''' <summary>The x86 viewer's one-deep undo record. Written only for an operation that
    ''' was queued (FinishFileOp clears it on failure) or has already succeeded - never for
    ''' one that is about to be attempted.</summary>
    Private Sub RememberNet48Undo(source_Path As String, destination_Path As String, was_Copy As Boolean)
        history_Source_File_Name = source_Path
        history_Destination_File_Name = destination_Path
        ' Undo has to reverse what was DONE, not whatever mode is set now.
        history_Was_Copy = was_Copy
    End Sub

    Private Sub Undo()
        If history_Destination_File_Name = "" Then
            lbl_Status.Text = Localization.T("! Нет истории о переносе")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1810: No history about moved files")
            Return
        End If

        Dim use_Worker As Boolean = UseAsyncFileOps()
        If use_Worker AndAlso FileOpWorkerBusy() Then Return

        Try
            ' history_Was_Copy, not Is_Copying_not_Moving: the branch used to read the
            ' CURRENT mode, so flipping to "copy" after moving a file and pressing U
            ' DELETED it at the destination (the only copy left) instead of moving it
            ' back.
            If history_Was_Copy Then
                ' The copy is deleted only while the original it duplicates is still there
                ' - a DEL on the original in between made the copy the only one left
                ' (audit 046, C-04). The record goes: it can only ever refuse again.
                If Not FileManager.CopyUndoMayDelete(history_Source_File_Name, history_Destination_File_Name) Then
                    lbl_Status.Text = Localization.T("! Оригинала больше нет или он изменился - копия осталась единственной, отмена её не удалит")
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1812: undo refused - the copy is the only one left")
                    Return
                End If
                Dim op As New FileOp With {.Kind = FileOpKind.DeleteUndo, .Source = history_Destination_File_Name}
                SayProgress(Localization.TF("!Ждите. Файл удаляется в каталоге {0}", history_Destination_File_Name))

                If use_Worker Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")
                    QueueFileOp(op)
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1760: undo copied deletion")
                    ' The copy of a read-only file is read-only too (057.S05-4).
                    DeleteCopyForUndo(op.Source)
                    lbl_Status.Text = Localization.TF("файл удалён в каталоге {0}", history_Destination_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
                End If
            Else
                Dim op As New FileOp With {
                    .Kind = FileOpKind.MoveUndo,
                    .Source = history_Destination_File_Name,
                    .Destination = history_Source_File_Name,
                    .ListIndex = current_File_Index,
                    .ListGeneration = file_List_Generation
                }
                SayProgress(Localization.TF("!Ждите. Возвращается в каталог {0}", history_Source_File_Name))
                ReleaseActiveMedia()

                If use_Worker Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1780: undo move async deletion")
                    QueueFileOp(op)
                    ' Clamped insert: after moving away the last file of a folder the
                    ' index is -1 and the raw Insert(-1) threw right here.
                    InsertFileIntoList(op.Destination, op.ListIndex)
                    ReadShowMediaFile(Mode_AfterUndo)
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1790: undo move deletion")
                    MoveFile(op.Source, op.Destination)
                    InsertFileIntoList(op.Destination, op.ListIndex)
                    lbl_Status.Text = Localization.TF("файл возвращён в каталог {0}", history_Source_File_Name)
                    ReadShowMediaFile(Mode_AfterUndo)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
                End If
            End If
        Catch ex As Exception
            ReportOperationError("E016", ex)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1800: undo E016 " & ex.Message)
        End Try
    End Sub
#End If

    Private Sub ButtonRename_Click(sender As Object, e As EventArgs) Handles btn_Rename.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2030: btn_Rename")

        If Not String.IsNullOrEmpty(Current_File_Name) Then
            RenameCurrentFile()
        Else
            lbl_Status.Text = Localization.T("! Нет файла для переименования")
        End If
    End Sub

    Private Sub CopyFilePathToClipboard()
        If Not String.IsNullOrEmpty(Current_File_Name) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2120: Filename sent to clipboard")
#If Not NETFRAMEWORK Then
            ' Inside an archive the file on screen is a temporary copy that is deleted when
            ' the archive closes; the path worth pasting is the archive's (010 §7, audit
            ' 052 G-17). The temporary path is never shown or handed out (§2.2).
            If IsArchiveMode() AndAlso ArchivePaths.IsUnderArchiveCache(Current_File_Name) Then
                CopyTextToClipboard(OpenArchivePath(), lbl_Status, Localization.T("Путь к архиву скопирован в буфер"))
                Return
            End If
#End If
            CopyTextToClipboard(Current_File_Name, lbl_Status, Localization.T("Имя файла скопировано в буфер"))
        End If
    End Sub

    ' The worker reads its whole job off e.Argument and touches no form state - it used
    ' to read two shared fields and even clear history_* from this thread, which made
    ' the "DeleteUndo" status line print an empty path (the field was already blank by
    ' the time the UI thread built the message).
    Private Sub FileOperationWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles FileOperationWorker.DoWork
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2160: FileOperationWorker_DoWork")

        Dim op As FileOp = TryCast(e.Argument, FileOp)
        If op Is Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2215: !! FileOperationWorker_DoWork for nothing ")
            fileop_Done.Set()
            Return
        End If

        Try
            Select Case op.Kind
                Case FileOpKind.Copy
                    CopyFile(op.Source, op.Destination)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2170: file copied")

                Case FileOpKind.Move
                    MoveFile(op.Source, op.Destination)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2180: file moved")

                Case FileOpKind.Delete
                    ' Deliberately does NOT clear history_*: the synchronous delete never
                    ' did either, and undo of an earlier move must not depend on which
                    ' threading checkbox happens to be ticked.
                    DeleteFile(op.Source)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2190: file deleted")

                Case FileOpKind.DeleteUndo
                    ' #todo: check undo from garbage bin
                    ' The copy of a read-only file is read-only too (057.S05-4).
                    DeleteCopyForUndo(op.Source)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2200: file deleted in undo")

                Case FileOpKind.MoveUndo
                    MoveFile(op.Source, op.Destination)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2210: file moved after undo")
            End Select

            e.Result = op
        Finally
            ' In Finally, so an exception (which BackgroundWorker hands to Completed via
            ' e.Error) still releases whoever is waiting to close.
            fileop_Done.Set()
        End Try
    End Sub

    Private Sub FileOperationWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles FileOperationWorker.RunWorkerCompleted
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2220: FileOperationWorker_RunWorkerCompleted")

        ' Read the op from the field, not e.Result: on the error path e.Result throws,
        ' and the rollback needs to know what was attempted.
        Dim op As FileOp = current_File_Op
        current_File_Op = Nothing
        If op Is Nothing Then Return

        FinishFileOp(op, e.Error)
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' Set by Shift+DEL and read once by the Mode_Delete branch. It exists because the
    ''' delete is reached through ReadShowMediaFile(Mode_Delete), which takes no argument -
    ''' the same shape the pending_Jump_* fields use for the same reason.
    ''' </summary>
    Private pending_Delete_Permanent As Boolean

    ''' <summary>
    ''' Shift+DEL (and its custom shortcut). The flag lives exactly as long as this one call
    ''' (audit 047, C-15): ReadShowMediaFile has early exits that never reach the Mode_Delete
    ''' branch - the 40 ms throttle, a folder read in progress, the archive refusal - and a
    ''' flag left standing by one of them turned the NEXT plain DEL into a deletion past the
    ''' Recycle Bin, with Yes as the prompt's default.
    ''' </summary>
    Private Sub DeleteCurrentFilePermanently()
        pending_Delete_Permanent = True
        Try
            ReadShowMediaFile(Mode_Delete)
        Finally
            pending_Delete_Permanent = False
        End Try
    End Sub

    ''' <summary>
    ''' What U walks back through. Bounded, oldest dropped first, memory only: once the
    ''' process is gone the Recycle Bin IS the history, and Explorer shows it better than
    ''' we would (§6.4).
    ''' </summary>
    Private ReadOnly undo_Entries As New UndoStack(Of FileOp)(UndoPolicy.Max_Undo_Depth)

    ''' <summary>
    ''' Records a COMPLETED operation. Called from FinishFileOp's success branch and from
    ''' the one operation that does not go through the queue (the rename), never at queue
    ''' time: history used to be written before the work ran, so a failed move still
    ''' offered an undo that would "return" a file which had never left.
    '''
    ''' Which kinds are recorded is UndoPolicy's table, not an If here - that is what makes
    ''' "an undo is never itself undoable" a property of the type rather than of this line.
    ''' </summary>
    Private Sub RecordUndo(op As FileOp)
        If op Is Nothing OrElse Not UndoPolicy.IsRecorded(op.Kind) Then Return
        undo_Entries.Push(op)
    End Sub

    ''' <summary>
    ''' THE deletion route on the mainline (017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md
    ''' §3.3). Every surface - DEL/D, Shift+DEL, the media menus, the recipients overlay -
    ''' arrives here, exactly as every recipient slot arrives at ExecuteRecipientAction,
    ''' and for the same reason: five surfaces that each decide for themselves are five
    ''' surfaces that drift.
    '''
    ''' Returns False when nothing was started (no file, file gone, user said no), so the
    ''' caller inside the read pipeline can bail out the way it always did.
    ''' </summary>
    ' NOTE: targetPath, not path - VB is case-insensitive, so a parameter called "path"
    ' shadows System.IO.Path and Path.GetFileName below becomes a member call on a String.
    Private Function ExecuteDelete(targetPath As String, forcedPermanent As Boolean) As Boolean
        If String.IsNullOrEmpty(targetPath) Then
            lbl_Status.Text = Localization.T("! Нет файла для удаления")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0540: case DeleteFile failed")
            Return False
        End If

        Try
            Using remoteTarget As ResourceDeleteTarget = ResourceRouting.CaptureDelete(targetPath)
                If remoteTarget Is Nothing AndAlso Not File.Exists(targetPath) Then
                    lbl_Status.Text = Localization.T("! Файл не найден")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0580: case DeleteFile failed: not found")
                    Return False
                End If

                ' -1 when the size cannot be read: the quota rule then never fires, which is the
                ' safe direction - it can only turn a "permanent" prediction back into "to the bin",
                ' and the shell has the final word on that anyway.
                Dim size_Bytes As Long = -1
                Try
                    size_Bytes = New FileInfo(targetPath).Length
                Catch
                End Try

                ' Two preferences, both from Ф4. "Use the Recycle Bin" off reaches the same verdict
                ' Shift+DEL does, and for the same reason - the user asked - so it is told to the
                ' policy rather than handled beside it.
                Dim prefs As ModernViewerPreferences = GetModernPreferences()
                Dim decision As DeleteDecision =
                    DeletePolicy.DecideForProvider(remoteTarget?.Capabilities,
                                        If(remoteTarget Is Nothing, DeleteVolumeProbe.FactsFor(targetPath), Nothing), size_Bytes,
                                        binEnabledBySetting:=prefs.DeleteToRecycleBin,
                                        forcedPermanent:=forcedPermanent)

                ' The delete question is now its own setting, not the blanket "no confirmations"
                ' flag: that one still governs every other file operation, and it could never say
                ' "ask me only about the deletions that cannot be taken back" (§3.7, §0.4).
                If DeletePolicy.ShouldConfirm(prefs.ConfirmDelete, decision) Then
                    If ViewerBox.Show(Me, DeleteText.ConfirmOne(decision, If(remoteTarget Is Nothing, Path.GetFileName(targetPath), remoteTarget.DisplayPath)),
                                       Localization.T("Подтверждение удаления"),
                                       MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                        Return False
                    End If
                End If

                Try
                    ' One release path for both builds and every operation: stops the GIF timer,
                    ' disposes the shown image AND its stream, clears the box and stops VLC, which
                    ' holds the playing file open - the shell fails on a locked file just as
                    ' File.Delete did.
                    ReleaseActiveMedia()

                    Dim op As New FileOp With {
                        .Kind = If(decision.Outcome = DeleteOutcome.Recycle, FileOpKind.RecycleDelete, FileOpKind.Delete),
                        .Source = targetPath,
                        .DeletedAtUtc = DateTime.UtcNow,
                        .PermanentDeleteReason = decision.Reason,
                        .RemoteDelete = remoteTarget?.Fork(),
                        .ListGeneration = file_List_Generation}
                    QueueFileOp(op)
                    ' Optimistic, rolled back by FinishFileOp if the operation fails.
                    op.ListIndex = RemoveCurrentFileFromList(targetPath)

                    SayProgress(Localization.TF("!Ждите. Файл удаляется в каталоге {0}", If(remoteTarget Is Nothing, targetPath, remoteTarget.DisplayPath)))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0550: file queued for deletion (" &
                                    op.Kind.ToString() & "): " & targetPath)
                    Return True
                Catch ex As Exception
                    ReportOperationError("E001", ex, FolderOfFile(targetPath))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0590: ERR: " & ex.Message)
                    Return False
                End Try
            End Using
        Catch ex As Exception
            ReportOperationError("E001", ex)
            Return False
        End Try
    End Function
#End If

    ''' <summary>Tail for the success line when the destination folder had to be made
    ''' (§3.6). Silent when it already existed - an auto-create is only worth saying out
    ''' loud the once, and saying it every time would train the user to stop reading.
    ''' Always silent on net48, which never sets the flag.</summary>
    Private Shared Function CreatedFolderNote(op As FileOp) As String
        If op Is Nothing OrElse Not op.CreatedDestinationFolder Then Return ""
        Return Localization.T("; каталог создан")
    End Function

    ''' <summary>
    ''' What an operation's ending means, on the UI thread. One implementation for both
    ''' transports: net48's BackgroundWorker and the modern queue arrive here alike, so
    ''' the two builds cannot drift on what a failure does to the list.
    '''
    ''' It touches the list and the view only while the list on screen is still the one the
    ''' operation started from (audit 047, C-13): a completion arrives long after the key
    ''' press, and by then the user may be in another folder, where "put it back at index
    ''' 12" plants one folder's file in another folder's list.
    ''' </summary>
    ''' <summary>Did a failed operation end as a complete copy with its source kept (100 §15.6.3 - a
    ''' move out of a server that cannot condition a delete)? Then a file exists at BOTH ends. Always
    ''' False on net48, which has no resource transfers.</summary>
    Private Shared Function IsCopyOnlyOutcome(failure As Exception) As Boolean
#If NETFRAMEWORK Then
        Return False
#Else
        Dim transfer As TransferOutcomeException = TryCast(failure, TransferOutcomeException)
        Return transfer IsNot Nothing AndAlso transfer.Outcome IsNot Nothing AndAlso
               transfer.Outcome.Result = TransferResult.CopyOnly
#End If
    End Function

    Private Sub FinishFileOp(op As FileOp, failure As Exception)
        If op Is Nothing Then Return

#If Not NETFRAMEWORK Then
        ' One item of a batch: the run, not the per-file rollback below, decides what it
        ' means for the list and the history.
        If op.BatchItem IsNot Nothing Then
            BatchOpFinished(op, failure)
            Return
        End If
#End If

        Dim on_Shown_List As Boolean = FileOpRules.ListStampMatches(op.ListGeneration, file_List_Generation)

#If Not NETFRAMEWORK Then
        ' Landed or failed, the row no longer names a file that is merely on its way.
        If op.Kind = FileOpKind.MoveUndo Then files_Arriving.Remove(op.Destination)

        ' The shell said "recycled" and the bin holds no record: it deleted the file for
        ' good (audit 047, C-09). Say so now, and record the deletion as the permanent one
        ' it was - U then names the reason instead of "maybe the bin was emptied".
        If failure Is Nothing AndAlso op.Kind = FileOpKind.RecycleDelete AndAlso op.RecycleFellThrough Then
            op.Kind = FileOpKind.Delete
            op.PermanentDeleteReason = PermanentReason.VolumeUnknown
            lbl_Status.Text = Localization.TF("! Корзина не приняла файл - он удалён безвозвратно: {0}", op.Source)
            AppFileLogger.WriteLine("Recycle Bin kept no record - the file was deleted permanently: " & op.Source)
        End If
#End If

        If failure Is Nothing Then
            Select Case op.Kind
                Case FileOpKind.Copy
                    lbl_Status.Text = Localization.TF("файл скопирован ({0}) в каталог {1}", op.SlotKey, op.Destination) &
                                      op.StatusNote & CreatedFolderNote(op)

                Case FileOpKind.Move
                    lbl_Status.Text = Localization.TF("файл перенесён ({0}) в каталог {1}", op.SlotKey, op.Destination) &
                                      op.StatusNote & CreatedFolderNote(op)

                Case FileOpKind.Delete, FileOpKind.RecycleDelete
#If Not NETFRAMEWORK Then
                    lbl_Status.Text = DeleteText.StatusOne(New DeleteDecision With {
                        .Outcome = If(op.Kind = FileOpKind.RecycleDelete, DeleteOutcome.Recycle, DeleteOutcome.Permanent),
                        .Reason = op.PermanentDeleteReason}, If(op.RemoteDelete Is Nothing, op.Source, op.RemoteDelete.DisplayPath))
#End If

#If NETFRAMEWORK Then
                Case FileOpKind.DeleteUndo
                    lbl_Status.Text = Localization.TF("файл удалён в каталоге {0}", history_Destination_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""

                Case FileOpKind.MoveUndo
                    lbl_Status.Text = Localization.TF("файл возвращён в каталог {0}", history_Source_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
                    ' The file only arrived back now - show it (the display that ran at
                    ' undo time found nothing at that path yet).
                    If on_Shown_List Then ReadShowMediaFile(Mode_SetFile)
#Else
                ' The mainline reads the paths off the operation instead of off two shared
                ' fields, so a second undo pressed while the first is still in flight
                ' cannot make one of them announce the other's path.
                Case FileOpKind.DeleteUndo
                    If Not String.IsNullOrEmpty(op.UndoRefusal) Then
                        ' The record refused the inverse (100 §15.6 item 5): the copy is
                        ' unchanged no more, or the provider cannot condition the delete.
                        ' Nothing was touched, so the row of the copy stays.
                        lbl_Status.Text = op.UndoRefusal
                    ElseIf op.CopyUndoRefused Then
                        ' The worker found the original gone or changed: the copy is the
                        ' one instance left and was kept (046 C-04, asked there - 057.S06-2).
                        lbl_Status.Text = Localization.T("! Оригинала больше нет или он изменился - копия осталась единственной, отмена её не удалит")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1812: undo refused - the copy is the only one left")
                    Else
                        lbl_Status.Text = Localization.TF("файл удалён в каталоге {0}", op.Source)
                        ' The copy is gone - if its folder is the one on screen, so is its row.
                        DropListedFile(op.Source)
                    End If

                Case FileOpKind.MoveUndo
                    If Not String.IsNullOrEmpty(op.UndoRefusal) Then
                        ' The record refused the inverse: nothing moved back, and the
                        ' optimistic row for the original path - inserted when the undo was
                        ' queued (C-13e) - names a file that is still at the destination.
                        ' Take it back out, exactly as a failed move-back does.
                        If on_Shown_List AndAlso IndexOfFileInList(op.Destination) >= 0 Then
                            Dim was_On_Screen As Boolean = String.Equals(Current_File_Name, op.Destination, StringComparison.OrdinalIgnoreCase)
                            RemoveCurrentFileFromList(op.Destination)
                            If was_On_Screen Then ReadShowMediaFile(Mode_SetFile)
                        End If
                        lbl_Status.Text = op.UndoRefusal
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1815: undo refused by the transfer record - nothing was moved")
                    Else
                        lbl_Status.Text = Localization.TF("файл возвращён в каталог {0}", op.Destination)
                        ' U pressed while looking at the slot folder: that row names a file which
                        ' has just left it (C-13b).
                        DropListedFile(op.Source)
                        ' The file only arrived back now - show it, if the view is still on its
                        ' row (the display that ran at undo time found nothing at that path yet).
                        If on_Shown_List AndAlso String.Equals(Current_File_Name, op.Destination, StringComparison.OrdinalIgnoreCase) Then
                            ReadShowMediaFile(Mode_SetFile)
                        End If
                    End If

                Case FileOpKind.RenameUndo
                    ' The list still holds the name the rename gave it; rewrite that one
                    ' entry in place rather than rescanning the folder - by value only.
                    Dim renamed_At As Integer = ReplaceFileInList(op.Source, op.Destination)
                    lbl_Status.Text = Localization.TF("имя возвращено: {0}", Path.GetFileName(op.Destination))
                    If renamed_At >= 0 AndAlso String.Equals(Current_File_Name, op.Source, StringComparison.OrdinalIgnoreCase) Then
                        current_File_Index = renamed_At
                        Current_File_Name = op.Destination
                        ReadShowMediaFile(Mode_SetFile)
                    End If

                Case FileOpKind.RestoreFromBin
                    ' The one operation that finishes WITHOUT an exception and still may not
                    ' have put a file anywhere. The list is touched only in the first branch,
                    ' and only because the worker verified the file is at that path
                    ' (invariant 8); the others say what happened and leave everything alone.
                    If op.RestoreOutcome = BinRestoreResult.Restored Then
                        lbl_Status.Text = If(String.Equals(op.Destination, op.Source, StringComparison.OrdinalIgnoreCase),
                                             Localization.TF("файл восстановлен из Корзины: {0}", op.Destination),
                                             Localization.TF("файл восстановлен из Корзины под именем {0}", Path.GetFileName(op.Destination)))
                        ' Only into the list it belongs in (C-13).
                        If on_Shown_List OrElse IsListOfFolder(FolderOfFile(op.Destination)) Then
                            Dim done_Text As String = lbl_Status.Text
                            InsertFileIntoList(op.Destination, op.ListIndex)
                            Current_File_Name = op.Destination
                            ReadShowMediaFile(Mode_SetFile)
                            If String.IsNullOrEmpty(lbl_Status.Text) Then lbl_Status.Text = done_Text
                        End If
                    ElseIf op.RestoreOutcome = BinRestoreResult.SourceFolderGone Then
                        lbl_Status.Text = Localization.T("! Папка, из которой файл был удалён, больше не существует")
                    Else
                        lbl_Status.Text = Localization.T("! Файла больше нет в Корзине - возможно, она очищена")
                    End If
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2235: restore from the Recycle Bin: " &
                                    op.RestoreOutcome.ToString() & " -> " & op.Destination)
#End If
            End Select

#If Not NETFRAMEWORK Then
            ' The conditional undo record of a resource transfer (100 §15.6 item 5): only
            ' a durable outcome gets one, and it is what U validates against later. A
            ' Nothing here (a plain local transfer) leaves the undo exactly as it was.
            If op.OpOutcome IsNot Nothing Then op.UndoRecord = TransferUndo.Record(op.OpOutcome)

            ' Only now, and only for what actually happened.
            RecordUndo(op)

            ' A copy advances only now that it has happened (a failed copy is still the file
            ' on screen), and only if the user is still looking at the file that was copied:
            ' a copy finishing after the user moved on used to flip once more, skipping a
            ' file and restarting a video (C-13d). The "next" navigation clears the status
            ' line, so the success line is put back.
            If op.Kind = FileOpKind.Copy AndAlso op.NavigateAfterSuccess AndAlso on_Shown_List AndAlso
               String.Equals(Current_File_Name, op.Source, StringComparison.Ordinal) Then
                Dim done_Text As String = lbl_Status.Text
                ReadShowMediaFile(Mode_Next)
                If String.IsNullOrEmpty(lbl_Status.Text) Then lbl_Status.Text = done_Text
            End If

            ' A move already showed the next file when it was queued (C-05); what is left is
            ' "close if empty" once the last one has really gone.
            If op.Kind = FileOpKind.Move AndAlso op.CloseIfEmptyAfterSuccess AndAlso on_Shown_List AndAlso
               total_File_Count <= 0 Then
                Current_File_Name = ""
                Current_Image_Path = ""
                lbl_File_Number.Text = "0"
                lbl_Status.Text &= Localization.T("; файлов больше нет")
            End If
#End If
        Else
#If Not NETFRAMEWORK Then
            ' A resource transfer that ended in a state worth more than one error line
            ' (100 §14.2): a completed copy beside a failed move - both copies exist and
            ' are named - or a last step lost in flight that reconciliation could not
            ' settle. The data is valid in both cases; the sentence says exactly what is
            ' where, and the list restoration below still puts the row back.
            Dim transfer_Failure As TransferOutcomeException = TryCast(failure, TransferOutcomeException)
            If transfer_Failure IsNot Nothing AndAlso transfer_Failure.Outcome IsNot Nothing AndAlso
               (transfer_Failure.Outcome.Result = TransferResult.CopyOnly OrElse
                transfer_Failure.Outcome.Result = TransferResult.Indeterminate) Then
                AppFileLogger.WriteLine("Transfer: " & ResourceRouting.DescribeOutcome(transfer_Failure.Outcome))
                If transfer_Failure.Outcome.Result = TransferResult.CopyOnly Then
                    lbl_Status.Text = Localization.TF("! Перемещение не завершено: файл скопирован в {0}, исходный файл остался на месте",
                                                      FolderOfFile(op.Destination))
                    lbl_Status.Text = Localization.TF("{0}: {1}", lbl_Status.Text,
                                                      ResourceMessages.ForMoveFailure(transfer_Failure.Outcome.Reason))
                Else
                    lbl_Status.Text = Localization.T("! Результат последнего шага неизвестен: проверьте файл в обеих папках - ничего не удалялось автоматически")
                End If
            Else
                ' §3.7: the user hears a category, the log keeps the exception. The path named
                ' is the DESTINATION for a transfer - "no connection to <the NAS>" is the
                ' sentence that tells them what to do; the source is where they already are.
                ' Except when the failure is the source itself being held open by another
                ' program: then the folder to name is the source's (C-17).
                Dim failure_Folder As String =
                    If(PathFailure.Classify(failure) = PathFailureKind.Locked OrElse String.IsNullOrEmpty(op.Destination),
                       FolderOfFile(op.Source), FolderOfFile(op.Destination))
                If op.RemoteDelete IsNot Nothing Then
                    failure_Folder = If(TypeOf failure Is ResourceDeleteException, "", op.RemoteDelete.DisplayPath)
                End If
                ReportOperationError("E014", failure, failure_Folder)
            End If
#Else
            lbl_Status.Text = Localization.TF("Ошибка операции: {0}", FailureText.Describe(failure))
#End If
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2230: file operation ERR " & failure.Message)

            ' The operation did not happen, so undo the optimistic list mutation - per KIND,
            ' because the inverses mutated the list differently (audit 047, C-07), and only
            ' on the list the operation came from (C-13).
            Select Case op.Kind
                Case FileOpKind.RestoreFromBin, FileOpKind.DeleteUndo, FileOpKind.Copy
                    ' Nothing was taken out of the list for these. A restore's ListIndex is
                    ' where the file WOULD go; putting "it" back would list a file that is
                    ' still in the Recycle Bin.

                Case FileOpKind.RenameUndo
                    ' Nothing was taken out of the list either (re-inserting would give a
                    ' duplicate row) - but the file was let go of when it was the one on
                    ' screen, and a failed undo left the window blank (audit 057.S06-3).
                    ' Shown again under the name it still has; the error line stays.
                    If on_Shown_List AndAlso String.Equals(Current_File_Name, op.Source, StringComparison.OrdinalIgnoreCase) AndAlso
                       File.Exists(op.Source) Then
                        Dim error_Text As String = lbl_Status.Text
                        Dim shown_At As Integer = IndexOfFileInList(op.Source)
                        If shown_At >= 0 Then current_File_Index = shown_At
                        ReadShowMediaFile(Mode_SetFile)
                        lbl_Status.Text = error_Text
                    End If

                Case FileOpKind.MoveUndo
                    ' The optimistic row is the ORIGINAL path, which does not exist: take it
                    ' out again. Inserting op.Source instead - the file's path in the SLOT
                    ' folder - gave the list two wrong rows, and DEL or a slot key on the
                    ' foreign one acted on a file in the slot folder.
                    ' Except when the way back ended as a COPY (100 §15.6.3: the file came
                    ' back out of a server that cannot condition a delete, so the server's copy
                    ' was kept): then the original path does exist again, and the row stays.
                    If on_Shown_List AndAlso IndexOfFileInList(op.Destination) >= 0 AndAlso
                       Not IsCopyOnlyOutcome(failure) Then
                        Dim was_On_Screen As Boolean = String.Equals(Current_File_Name, op.Destination, StringComparison.OrdinalIgnoreCase)
                        RemoveCurrentFileFromList(op.Destination)
                        If was_On_Screen Then ReadShowMediaFile(Mode_SetFile)
                    End If

                Case Else
                    ' Move, Delete, RecycleDelete: the file is still in its folder and must
                    ' stay visible. (It used to be dropped from the list the moment the job
                    ' was queued and never came back.)
                    If op.ListIndex >= 0 AndAlso on_Shown_List Then
                        InsertFileIntoList(op.Source, op.ListIndex)
                        ReadShowMediaFile(Mode_SetFile)
                    End If
            End Select

#If Not NETFRAMEWORK Then
            ' An undo that failed is still owed: the entry goes back on top so U can be
            ' pressed again once the cause is fixed (C-07). It was popped when the undo was
            ' queued, and a failure used to lose it for good.
            If op.UndoOrigin IsNot Nothing Then undo_Entries.Push(op.UndoOrigin)
#End If

#If NETFRAMEWORK Then
            ' A move that failed leaves nothing to undo - offering it would "return" a
            ' file that never left. The mainline needs no such cleanup: nothing was ever
            ' recorded, because RecordUndo runs in the success branch only.
            If op.Kind = FileOpKind.Move OrElse op.Kind = FileOpKind.Copy Then
                history_Source_File_Name = ""
                history_Destination_File_Name = ""
            End If
#End If
        End If
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>Takes a path that is no longer on disk out of the list on screen, by value,
    ''' and shows what slid into its place when it was the file on screen. A no-op when the
    ''' list does not hold it - which is the ordinary case.</summary>
    Private Sub DropListedFile(file_Path As String)
        If IndexOfFileInList(file_Path) < 0 Then Return
        Dim was_On_Screen As Boolean = String.Equals(Current_File_Name, file_Path, StringComparison.OrdinalIgnoreCase)
        RemoveCurrentFileFromList(file_Path)
        If was_On_Screen Then ReadShowMediaFile(Mode_SetFile)
    End Sub
#End If

    ''' <summary>A row whose file a queued move-back has not landed yet (audit 047, C-13e).
    ''' Always False on net48, whose undo keeps its historical shape.</summary>
    Private Function IsFileArriving(file_Path As String) As Boolean
#If Not NETFRAMEWORK Then
        Return Not String.IsNullOrEmpty(file_Path) AndAlso files_Arriving.Contains(file_Path)
#Else
        Return False
#End If
    End Function

End Class
