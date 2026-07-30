Option Strict On

Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Windows.Forms

Partial Public Class Main_Form

    Private Enum FileOpKind
        Copy
        Move
        Delete
        DeleteUndo
        MoveUndo
    End Enum

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
        Public Property Kind As FileOpKind
        Public Property Source As String = ""
        Public Property Destination As String = ""
        Public Property SlotKey As String = ""
        Public Property ListIndex As Integer = -1
        ''' <summary>Tail for the success status, e.g. "the name was taken, saved as ..".
        ''' It travels with the op because the message is built when the op finishes,
        ''' long after the reason for it was known.</summary>
        Public Property StatusNote As String = ""
        ''' <summary>A copy that already flipped the view to the next file (the optimistic
        ''' advance). Only then does a failure have to bring the user back: a copy removes
        ''' nothing from the list, so there is no list damage to repair - just a view that
        ''' has moved on from a file whose copy did not happen.</summary>
        Public Property AdvancedPastSource As Boolean
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
        If file_Op_Queue Is Nothing Then
            ' RunFileOp runs on the worker; the completion comes back on the UI thread.
            file_Op_Queue = New FileOpQueue(Of FileOp)(AddressOf RunFileOp)
            AddHandler file_Op_Queue.OpCompleted, AddressOf FileOpQueue_OpCompleted
        End If
        Return file_Op_Queue
    End Function

    ''' <summary>The work itself, on a worker thread. Touches nothing but its argument.</summary>
    Private Shared Sub RunFileOp(op As FileOp)
        Select Case op.Kind
            Case FileOpKind.Copy
                CopyFile(op.Source, op.Destination)
            Case FileOpKind.Move
                MoveFile(op.Source, op.Destination)
            Case FileOpKind.Delete, FileOpKind.DeleteUndo
                DeleteFile(op.Source)
            Case FileOpKind.MoveUndo
                MoveFile(op.Source, op.Destination)
        End Select
    End Sub

    ''' <summary>Back on the UI thread once an operation has finished.</summary>
    Private Sub FileOpQueue_OpCompleted(sender As Object, done As FileOpQueue(Of FileOp).Completion)
        If Me.IsDisposed Then Return
        FinishFileOp(done.Op, done.Error_Info)
        ShowFileOpQueueDepth()
    End Sub

    ''' <summary>The tail of pending transfers has to be visible - on a 200 MB clip over
    ''' SMB it is the only sign that anything is still happening.</summary>
    Private Sub ShowFileOpQueueDepth()
        If file_Op_Queue Is Nothing Then Return
        Dim n As Integer = file_Op_Queue.PendingCount
        If n > 1 Then
            lbl_Status.Text = Localization.T("в очереди: ") & n.ToString()
        End If
    End Sub
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
        If file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.IsClosed Then
            lbl_Status.Text = Localization.T("!Ждите.. завершаются файловые операции, программа закрывается")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2156: file operation refused - queue closed for shutdown")
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
        If Not EnsureFileOpQueue().Enqueue(op) Then
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
            Catch
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
        Return Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked
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

    Private Sub RenameCurrentFile()
        Try
            If Not My.Computer.FileSystem.FileExists(Current_File_Name) Then
                lbl_Status.Text = Localization.T("! Файл не найден")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1250: file not found")
                Return
            End If
            Dim current_File_Name_Without_Extension As String = Path.GetFileNameWithoutExtension(Current_File_Name)
            Dim current_File_Extension As String = Path.GetExtension(Current_File_Name)
            Dim new_File_Name As String = InputBox(Localization.T("Введите новое имя файла:"),
                                            Localization.T("Переименование файла"),
                                            current_File_Name_Without_Extension)
            If String.IsNullOrEmpty(new_File_Name) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1260: empty new file name - no rename")
                Return
            End If

            Dim current_Directory_Path As String = Path.GetDirectoryName(Current_File_Name)
            Dim new_File_Full_Path As String = Path.Combine(current_Directory_Path, new_File_Name & current_File_Extension)
            If new_File_Full_Path = Current_File_Name Then
                lbl_Status.Text = Localization.T("! Имя не изменено")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1270: file is not new")
                Return
            End If

            ' Say it plainly: File.Move onto an existing name throws IOException, which
            ' surfaced as a bare "E011" MsgBox.
            If File.Exists(new_File_Full_Path) Then
                lbl_Status.Text = Localization.T("! Файл с таким именем уже есть")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1275: rename target already exists")
                Return
            End If

            ' A rename is a File.Move, so it needs delete access: a playing video holds
            ' the file (LibVLC in both builds, IE on net48) and the rename just failed
            ' with E011. PoMove has always released the media first - so must this.
            Dim renamed_At As Integer
            If is_Files_Array_Active Then
                renamed_At = If(files_Array Is Nothing, -1, Array.IndexOf(files_Array, Current_File_Name))
            Else
                renamed_At = If(files_List Is Nothing, -1, files_List.IndexOf(Current_File_Name))
            End If
            ReleaseActiveMedia()

            ' Follow the path RenameFile actually created, never one we assembled here:
            ' the two used to diverge, and the list kept a name that was not on disk.
            Dim renamed_Path As String = RenameFile(Current_File_Name, new_File_Name & current_File_Extension)

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
            lbl_Status.Text = Localization.TF("Файл переименован: {0}{1}", new_File_Name, current_File_Extension)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1280: file is renamed")

            ReadShowMediaFile(Mode_SetFile)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1290: ERR: " & ex.Message)
            ReportOperationError("E011", ex)
            lbl_Status.Text = Localization.T("! Ошибка переименования")
        End Try
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles bt_Delete.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: bt_Delete")
        SlideShowStop()
        ReadShowMediaFile(Mode_Delete)
    End Sub

    ''' <summary>
    ''' The historical entry point, kept for the callers that mean "do the usual thing
    ''' with this slot" - and the ONLY place where the global copy mode is still read.
    ''' On the mainline it always means Move: the global mode is gone from that UI
    ''' (there is nothing left to switch it with), and every surface that wants a copy
    ''' asks for one by name. net48 keeps its checkbox and behaves exactly as before.
    ''' </summary>
    Private Sub PoMove(ByVal move_Slot_index As Integer)
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
        Dim is_Copy As Boolean = (action = RecipientActionKind.Copy)
        Dim destination_Folder_Path As String = Hardkeys_to_move_mediafile(move_Slot_index)
        Dim move_Slot_Key As String = move_Slot_index.ToString
        If move_Slot_Key = "10" Then move_Slot_Key = "0"

        If destination_Folder_Path = "" Then
            lbl_Status.Text = Localization.TF("! Нет каталога-получателя для клавиши {0}", move_Slot_Key)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1680: No dest folder set with key " & move_Slot_Key)
            Return
        End If

        If Current_File_Name = "" Then
            lbl_Status.Text = Localization.T("! Нет файла ")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1740: No file")
            Return
        End If

        Try
            Dim source_File_Info As System.IO.FileInfo = My.Computer.FileSystem.GetFileInfo(Current_File_Name)
            Dim destination_Folder_Full_Path As String = destination_Folder_Path & "\" & source_File_Info.Name

            ' A slot pointing at the folder we are viewing: File.Move(a, a) succeeds,
            ' so the file used to be dropped from the list while sitting right where it
            ' was, and a copy onto itself threw a bare "E014".
            If String.Equals(Path.GetDirectoryName(destination_Folder_Full_Path),
                             Path.GetDirectoryName(Current_File_Name), StringComparison.OrdinalIgnoreCase) Then
                lbl_Status.Text = Localization.T("! Файл уже в этой папке")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1683: destination equals current folder")
                Return
            End If

            Dim use_Worker As Boolean = UseAsyncFileOps()
            If use_Worker AndAlso FileOpWorkerBusy() Then Return

            ' Said out loud when the name had to change - the destination path in the
            ' status line is long, and a quiet "(2)" inside it is easy to miss.
            Dim collision_Note As String = ""
#If Not NETFRAMEWORK Then
            ' Same name already there? Take the next free one instead of failing. Done
            ' here, before anything records the destination, so the operation and the
            ' undo history agree on what was actually created.
            Dim intended_Name As String = Path.GetFileName(destination_Folder_Full_Path)
            destination_Folder_Full_Path = ResolveDestinationCollision(destination_Folder_Full_Path)
            Dim final_Name As String = Path.GetFileName(destination_Folder_Full_Path)
            If Not String.Equals(intended_Name, final_Name, StringComparison.Ordinal) Then
                collision_Note = Localization.TF(" (имя занято, сохранён как {0})", final_Name)
            End If
#End If

            history_Source_File_Name = Current_File_Name
            history_Destination_File_Name = destination_Folder_Full_Path
            ' Undo has to reverse what was DONE, not whatever mode is set now.
            history_Was_Copy = is_Copy

            Dim op As New FileOp With {
                .Kind = If(is_Copy, FileOpKind.Copy, FileOpKind.Move),
                .Source = Current_File_Name,
                .Destination = destination_Folder_Full_Path,
                .SlotKey = move_Slot_Key,
                .StatusNote = collision_Note
            }

            If is_Copy Then
                lbl_Status.Text = Localization.TF("!Ждите.. Файл копируется ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path)

                If use_Worker Then
                    QueueFileOp(op)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1690: file is copied ASYNC to " & destination_Folder_Full_Path)
                Else
                    CopyFile(op.Source, op.Destination)
                    lbl_Status.Text = Localization.TF("файл скопирован ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path) & collision_Note
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1715: file is copied to " & destination_Folder_Full_Path)
                End If

                ' The copy leaves the file where it is, so moving on is a UI choice, not a
                ' necessity. On the mainline it is the "Go to the next file after copying"
                ' setting (on by default - the fast sorting run); with it off the same
                ' file stays on screen, which is what you want when you are filing one
                ' picture into several folders. net48 always advances, as it always has.
                Dim advance_After_Copy As Boolean = True
#If Not NETFRAMEWORK Then
                advance_After_Copy = AdvanceAfterCopy()
                op.AdvancedPastSource = advance_After_Copy
#End If
                If advance_After_Copy Then ReadShowMediaFile(Mode_Next)
            Else
                ' Moving needs delete access to the file, so let go of it first.
                ReleaseActiveMedia()

                lbl_Status.Text = Localization.TF("!Ждите.. Файл переносится ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path)

                If use_Worker Then
                    QueueFileOp(op)
                    ' Optimistic: sorting must feel instant. The price is a rollback in
                    ' RunWorkerCompleted if the move fails - not a file that silently
                    ' vanished from the view while still sitting in the folder.
                    op.ListIndex = RemoveCurrentFileFromList(op.Source)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1705: file is moved ASYNC to " & destination_Folder_Full_Path)
                Else
                    MoveFile(op.Source, op.Destination)
                    op.ListIndex = RemoveCurrentFileFromList(op.Source)
                    lbl_Status.Text = Localization.TF("файл перенесён ({0}) в каталог {1}", move_Slot_Key, destination_Folder_Full_Path) & collision_Note
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1729: file is moved to " & destination_Folder_Full_Path)
                End If

                ReadShowMediaFile(Mode_SetFile)
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1730: E014 " & ex.Message)
            ReportOperationError("E014", ex)
        End Try
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
                Dim op As New FileOp With {.Kind = FileOpKind.DeleteUndo, .Source = history_Destination_File_Name}
                lbl_Status.Text = Localization.TF("!Ждите. Файл удаляется в каталоге {0}", history_Destination_File_Name)

                If use_Worker Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")
                    QueueFileOp(op)
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1760: undo copied deletion")
                    DeleteFile(op.Source)
                    lbl_Status.Text = Localization.TF("файл удалён в каталоге {0}", history_Destination_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
                End If
            Else
                Dim op As New FileOp With {
                    .Kind = FileOpKind.MoveUndo,
                    .Source = history_Destination_File_Name,
                    .Destination = history_Source_File_Name,
                    .ListIndex = current_File_Index
                }
                lbl_Status.Text = Localization.TF("!Ждите. Возвращается в каталог {0}", history_Source_File_Name)
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
                    DeleteFile(op.Source)
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

    ''' <summary>
    ''' What an operation's ending means, on the UI thread. One implementation for both
    ''' transports: net48's BackgroundWorker and the modern queue arrive here alike, so
    ''' the two builds cannot drift on what a failure does to the list.
    ''' </summary>
    Private Sub FinishFileOp(op As FileOp, failure As Exception)
        If op Is Nothing Then Return

        If failure Is Nothing Then
            Select Case op.Kind
                Case FileOpKind.Copy
                    lbl_Status.Text = Localization.TF("файл скопирован ({0}) в каталог {1}", op.SlotKey, op.Destination) & op.StatusNote

                Case FileOpKind.Move
                    lbl_Status.Text = Localization.TF("файл перенесён ({0}) в каталог {1}", op.SlotKey, op.Destination) & op.StatusNote

                Case FileOpKind.Delete
                    ' The status line was already written when the delete was queued.

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
                    ReadShowMediaFile(Mode_SetFile)
            End Select
        Else
            lbl_Status.Text = Localization.TF("Ошибка операции: {0}", failure.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2230: file operation ERR " & failure.Message)

            ' The operation did not happen, so undo the optimistic list mutation: the
            ' file is still in its folder and must stay visible. (It used to be dropped
            ' from the list the moment the job was queued and never came back.)
            If op.ListIndex >= 0 Then
                InsertFileIntoList(op.Source, op.ListIndex)
                ReadShowMediaFile(Mode_SetFile)
            End If

#If Not NETFRAMEWORK Then
            ' A failed copy leaves the list untouched (nothing was removed), but the view
            ' may have advanced already. Come back to the file whose copy failed, so the
            ' error message is about the picture in front of you and not about one that
            ' scrolled by.
            If op.Kind = FileOpKind.Copy AndAlso op.AdvancedPastSource Then
                Dim source_At As Integer = IndexOfFileInList(op.Source)
                If source_At >= 0 Then
                    current_File_Index = source_At
                    ReadShowMediaFile(Mode_SetFile)
                End If
            End If
#End If

            ' A move that failed leaves nothing to undo - offering it would "return" a
            ' file that never left.
            If op.Kind = FileOpKind.Move OrElse op.Kind = FileOpKind.Copy Then
                history_Source_File_Name = ""
                history_Destination_File_Name = ""
            End If
        End If
    End Sub

End Class
