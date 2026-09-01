Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics

Partial Public Class Main_Form

    ' The modes ReadShowMediaFile understands. They were bare strings for 15 years and
    ' drifted apart from their Select Case twice: "ReadAfterUndo" (compared, never sent)
    ' and "ReadForJumpToFile" (sent, no branch at all - it only ever worked because the
    ' caller had already moved the index itself).
    Friend Const Mode_Next As String = "ReadNextFile"
    Friend Const Mode_Prev As String = "ReadPrevFile"
    Friend Const Mode_SetFile As String = "SetFile"
    Friend Const Mode_Files As String = "ReadFiles"
    Friend Const Mode_FolderAndFile As String = "ReadFolderAndFile"
    Friend Const Mode_FolderAndKnownFile As String = "ReadFolderAndKnownFile"
    Friend Const Mode_Delete As String = "DeleteFile"
    Friend Const Mode_InSlideShow As String = "InSlideShow"
    Friend Const Mode_ForRandom As String = "ReadForRandom"
    Friend Const Mode_ForSlideShow As String = "ReadForSlideShow"
    Friend Const Mode_AfterUndo As String = "AfterUndo"
    Friend Const Mode_JumpBy As String = "JumpBy"
    Friend Const Mode_JumpTo As String = "JumpTo"

    ' Arguments of the pending jump. The index is moved INSIDE the pipeline (see the
    ' JumpBy/JumpTo cases): callers used to do "current_File_Index += 10" themselves and
    ' then call in - and when the call bailed out early (throttle, busy worker), the
    ' index had already moved while the screen had not, so the next delete took out a
    ' different file than the one on display.
    Private pending_Jump_Delta As Integer
    Private pending_Jump_Target As Integer
    ''' <summary>
    ''' Status line to show once the jump lands, held as the Russian SOURCE string - the
    ''' dictionary key. It used to be two fields, Ru and En, chosen at display time; one
    ''' key is both shorter and correct in thirteen languages instead of two.
    ''' </summary>
    Private pending_Jump_Status As String = ""

    ''' <summary>Flip by delta files (Home/End/arrows/PageUp/PageDown, modifier-clicks).</summary>
    Private Sub JumpBy(delta As Integer, status As String)
        pending_Jump_Delta = delta
        pending_Jump_Status = status
        ReadShowMediaFile(Mode_JumpBy)
    End Sub

    ''' <summary>Go to an absolute position (first/last file, "jump to number").</summary>
    Private Sub JumpTo(target_Index As Integer, Optional status As String = "")
        pending_Jump_Target = target_Index
        pending_Jump_Status = status
        ReadShowMediaFile(Mode_JumpTo)
    End Sub

    ''' <summary>Which way the user is going. The auto-skip of an unreadable file has to
    ''' follow it: skipping forward while paging BACK bounced the user off the broken
    ''' file straight back to where they came from.</summary>
    Private is_Nav_Backwards As Boolean = False

    ''' <summary>How many unreadable files were skipped in one uninterrupted chain -
    ''' the stop condition for a folder in which nothing decodes at all.</summary>
    Private auto_Skip_Chain As Integer = 0

    ''' <summary>Bumped by every navigation the USER asks for, so a skip continuation that
    ''' was posted to the message loop and then overtaken by a keypress is dropped instead
    ''' of yanking the user to the file the skip had picked.</summary>
    Private auto_Skip_Generation As Integer = 0


    ''' <summary>Drops the file that would not load and moves on in the direction the
    ''' user was actually going. Auto-skipping a broken file is by design; it simply
    ''' never happened, because the follow-up call landed inside the 40 ms throttle
    ''' window this very call had just opened, and was thrown away in silence.
    '''
    ''' Since Ф1 of 011_SPECIFICATION_SLOT_HEALTH_AND_HONEST_FAILURES_DOTNET10.md it first asks
    ''' WHY the file would not load. Dropping it is right for a file that will not decode
    ''' and wrong for everything between us and it: a dropped SMB session used to shred a
    ''' healthy folder's list one file per keypress (§0.4).</summary>
    ''' <param name="kind">What the caller knows about the failure. The default is the
    ''' pre-Ф1 behaviour - drop the file - so a call site that has learnt nothing new
    ''' behaves exactly as it always did.</param>
    Private Sub SkipUnreadableFile(Optional kind As PathFailureKind = PathFailureKind.Unknown)
#If Not NETFRAMEWORK Then
        If Not PathFailure.IsAboutTheFile(kind) Then
            ' The list is innocent (D7). Keep the file, keep the position, and stop the
            ' chain rather than feed it (D8): the next file is behind the same dead
            ' transport, so walking a thousand of them is the bug, not the recovery.
            auto_Skip_Chain = 0
            lbl_Status.Text = ReadFailureText(kind, Current_File_Name)
            AppFileLogger.WriteLine("Read failure kept in list [" & kind.ToString() & "]: " & Current_File_Name)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0913: " & kind.ToString() & " - file kept in the list")
            Return
        End If
#End If

        ' A file that will not decode has no business in the list - keep it and paging
        ' back walks right into it again.
        Dim removed_At As Integer = RemoveCurrentFileFromList(Current_File_Name)
        If removed_At < 0 Then Return   ' not listed (a lone external file): nowhere to skip

        If total_File_Count <= 0 Then
            Current_File_Name = ""
            UpdateCurrentFileAndDisplay(True, False)   ' the "no files" branch wipes the surface
            Return
        End If

        ' The slot the file used to occupy now holds the NEXT one; going backwards we
        ' want the one before it.
        Dim target As Integer = If(is_Nav_Backwards, removed_At - 1, removed_At)
        If target < 0 Then target = total_File_Count - 1
        If target > total_File_Count - 1 Then target = 0

        pending_Jump_Target = target
        pending_Jump_Status = ""        ' keep the "could not load X" line on screen
        RequestAutoSkipJump()
    End Sub

    ''' <summary>
    ''' Second-guesses a call site that thinks the file is at fault.
    '''
    ''' The two questions the display path actually asks - File.Exists and "did the decoder
    ''' return anything" - both answer False for a deleted file AND for a share that stopped
    ''' answering, and those two need opposite treatment. So when a failure looks like it is
    ''' about the file, the FOLDER gets asked: a folder that no longer answers turns the
    ''' verdict into Transport, because the absence of the file is then no evidence about the
    ''' file at all.
    '''
    ''' Cost: one Directory.Exists, on a path the caller has just probed anyway - no new class
    ''' of blocking, and microseconds on a local disk (where a broken JPEG still skips, as it
    ''' should).
    ''' </summary>
    Private Function ReadFailure(kind As PathFailureKind) As PathFailureKind
#If NETFRAMEWORK Then
        ' The x86 fallback keeps today's behaviour to the byte: the kind travels with the
        ' call and is ignored (CLAUDE.md maintenance policy; invariant 10 of the spec).
        Return kind
#Else
        If Not PathFailure.IsAboutTheFile(kind) Then Return kind

        Try
            Dim folder As String = Path.GetDirectoryName(Current_File_Name)
            If String.IsNullOrEmpty(folder) Then folder = Current_Folder_Path
            If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                Return PathFailureKind.Transport
            End If
        Catch ex As Exception
            ' Asking the question failed - keep the caller's answer rather than invent one.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0914: folder probe failed: " & ex.Message)
        End Try

        Return kind
#End If
    End Function

    ''' <summary>
    ''' Will <see cref="SkipUnreadableFile"/> keep this file instead of dropping it? The call
    ''' sites ask before writing their own status line, because when the file is kept the
    ''' sentence has to be about the transport, not about the file.
    '''
    ''' Always False on net48: nothing is ever kept there, so every legacy status line is
    ''' written exactly when it always was (invariant 10).
    ''' </summary>
    Private Shared Function FailureKeepsFile(kind As PathFailureKind) As Boolean
#If NETFRAMEWORK Then
        Return False
#Else
        Return Not PathFailure.IsAboutTheFile(kind)
#End If
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>The sentence for a failure that is NOT about the file. It says both halves:
    ''' what went wrong, and that the list was left alone - otherwise the user's next move is
    ''' the rescan this whole change exists to make unnecessary.</summary>
    Private Shared Function ReadFailureText(kind As PathFailureKind, file_Path As String) As String
        Dim name As String = ""
        Try
            name = Path.GetFileName(file_Path)
        Catch ex As Exception
            name = If(file_Path, "")
        End Try

        Select Case kind
            Case PathFailureKind.Denied
                Return Localization.TF("Нет доступа, файл оставлен в списке: {0}", name)
            Case Else
                Return Localization.TF("Нет связи с папкой, файл оставлен в списке: {0}", name)
        End Select
    End Function
#End If

    ''' <summary>
    ''' Asks for the next file in an auto-skip chain, on a FRESH stack.
    '''
    ''' Why this is not a plain call: display -&gt; skip -&gt; display is a mutual recursion that
    ''' never unwinds - roughly five frames per skipped file. The count-based guard in
    ''' ReadShowMediaFile IS the stack depth, so a folder of thousands of unreachable files
    ''' (a NAS that dropped its SMB session mid-session is the ordinary way to get one) blew
    ''' the UI thread's stack long before the counter ran out. StackOverflowException cannot
    ''' be caught: the process died instantly, taking with it everything Form1_FormClosing
    ''' would have saved - position, recent lists, every setting changed since launch.
    '''
    ''' Posting the follow-up puts each skip on its own stack AND lets the message loop
    ''' breathe between files, so the window stays alive through the chain. The generation
    ''' check drops the continuation if the user navigated in the meantime.
    ''' </summary>
    Private Sub RequestAutoSkipJump()
        If Not Me.IsHandleCreated OrElse Me.IsDisposed Then
            ' No message loop to post to yet - the first file opens inside Form1_Load. The
            ' chain cannot be deep here, so the direct call is safe.
            ReadShowMediaFile(Mode_JumpTo, is_Auto_Skip:=True)
            Return
        End If

        Dim generation As Integer = auto_Skip_Generation
        Me.BeginInvoke(New Action(Sub()
                                      If Me.IsDisposed Then Return
                                      If auto_Skip_Generation <> generation Then Return
                                      ReadShowMediaFile(Mode_JumpTo, is_Auto_Skip:=True)
                                  End Sub))
    End Sub

    Private Sub ReadShowMediaFile(ByVal read_Mode_Type As String, Optional is_Auto_Skip As Boolean = False)

        media_View_Count += 1

        If Not is_Folder_Read_Required Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0050: ReadShowMediaFile = " & read_Mode_Type.ToString)

            ' The throttle is there to tame the user's key repeat - it must never touch
            ' our OWN skip of an unreadable file. That call comes back within a
            ' millisecond of this one setting last_Action_Time, so the throttle ate it
            ' and the auto-skip (by design: a broken file must not stop the flip)
            ' silently never happened - the stale image just stayed on screen.
            If Not is_Auto_Skip Then
                Dim current_Operation_Time As DateTime = DateTime.Now
                If last_Action_Time.AddSeconds(minimum_time_before_next_media_file) > current_Operation_Time Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0330: Try to read the new file less than 0.04s - cancelled")
                    Exit Sub
                End If
                last_Action_Time = current_Operation_Time
            End If

            ' Only a mode that STARTS a file operation has to wait for the worker. The
            ' list is already mutated on the UI thread when an operation is queued, so
            ' blocking the display modes is what left the screen black after a move and
            ' stopped copy from flipping to the next file.
            ' Skipping is a mutual recursion (display -> skip -> display), so a folder
            ' where nothing decodes has to stop by count, not by luck: the index wraps
            ' round for ever and the stack would not.
            If is_Auto_Skip Then
                auto_Skip_Chain += 1
                If Not AutoSkipPolicy.ShouldContinue(auto_Skip_Chain, total_File_Count) Then
                    auto_Skip_Chain = 0
                    lbl_Status.Text = Localization.T("! Нет читаемых файлов в папке")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0335: auto-skip chain exhausted - nothing readable")
                    Exit Sub
                End If
            Else
                auto_Skip_Chain = 0
                ' The user took over: any skip continuation still sitting in the message
                ' queue belongs to a decision they have already overridden.
                auto_Skip_Generation += 1
            End If

#If Not NETFRAMEWORK Then
            ' DEL inside an archive would delete the extracted copy, leave a hole in the
            ' list and change nothing about the archive - the same refusal the recipient
            ' slots get, from the same one point (§7).
            If read_Mode_Type = Mode_Delete AndAlso ArchiveModeBlocksFileOperations() Then Exit Sub
#End If

#If NETFRAMEWORK Then
            ' net48 only, and it always was: on the mainline QueueFileOp goes to the queue
            ' and this worker is never started, so the check has been dead code there.
            ' Fenced rather than deleted while this branch was being rewritten, so it is
            ' not read later as an accidental behaviour change (R-1 §6.7).
            If FileOperationWorker.IsBusy AndAlso read_Mode_Type = Mode_Delete Then
                lbl_Status.Text = Localization.T("!Ждите.. предыдущая операция ещё выполняется")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0340: DeleteFile skiped while FileOperationWorker")
                Exit Sub
            End If
#End If

            Dim slideshow_Interval_Text = If(Is_slide_show_mode, (SlideShowTimer.Interval / 1000).ToString() & "s", "")
            If Not lbl_Slideshow_Time.Text = slideshow_Interval_Text Then lbl_Slideshow_Time.Text = slideshow_Interval_Text

            ' "AfterUndo" - the value Undo() actually passes. This compared against
            ' "ReadAfterUndo", a mode that does not exist, so the flag was always False
            ' and every undo protection below was dead code.
            Dim is_After_Undo_Operation As Boolean = (read_Mode_Type = "AfterUndo")
            Dim is_File_Found As Boolean = True
            If Not UpdateFileIndexAndList(read_Mode_Type, is_File_Found) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0350: Mastering the file is failed")
                Return
            End If

            If String.IsNullOrEmpty(Current_Folder_Path) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0360: currentFolderPath is lost")
                Return
            End If

            is_TextBox_Editing = True

            If Not cmbox_Media_Folder.Text = Current_Folder_Path Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0370: folder combo list is updated")

                ' Move current folder to first position if it's not already there
                If recent_Folder_List.Count = 0 OrElse recent_Folder_List(0) <> Current_Folder_Path Then
                    ' Remove if exists elsewhere in the list
                    recent_Folder_List.Remove(Current_Folder_Path)
                    ' Insert at the beginning (first position)
                    recent_Folder_List.Insert(0, Current_Folder_Path)

                    ' Remove excess folders from the end if we exceed the limit
                    If recent_Folder_List.Count > RecentFoldersLimit() Then
                        recent_Folder_List.RemoveAt(recent_Folder_List.Count - 1)
                    End If

                    ' A new folder in the recent list is worth keeping through a crash.
                    MarkSettingsDirty()
                End If

                If cmbox_Media_Folder.InvokeRequired Then
                    cmbox_Media_Folder.Invoke(Sub()
                                                  cmbox_Media_Folder.Items.Clear()
                                                  For Each folder In recent_Folder_List
                                                      cmbox_Media_Folder.Items.Add(folder)
                                                  Next
                                                  cmbox_Media_Folder.SelectedIndex = 0 ' Select the first item (current folder)
                                              End Sub)
                Else
                    cmbox_Media_Folder.Items.Clear()
                    For Each folder In recent_Folder_List
                        cmbox_Media_Folder.Items.Add(folder)
                    Next
                    cmbox_Media_Folder.SelectedIndex = 0 ' Select the first item (current folder)
                End If
            End If
            is_TextBox_Editing = False

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0380: UpdateCurrentFileAndDisplay")
            UpdateCurrentFileAndDisplay(is_File_Found, is_After_Undo_Operation)
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0390: folder read is skiped")
        End If
    End Sub

    Private Function UpdateFileIndexAndList(read_Mode_Type As String, ByRef is_File_Found As Boolean) As Boolean
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0400: UpdateFileIndexAndList = " & read_Mode_Type.ToString)

        ' The user is choosing a file themselves, so stop waiting for that locked one -
        ' otherwise its timer fires up to 45 s later and drags the view off to it.
        CancelPendingUnlock("")

        Select Case read_Mode_Type
            Case Mode_Next ' 1
                is_Nav_Backwards = False
                If was_External_Input_Previously Then
                    If Not LoadFilesForExternalInput(is_File_Found) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0410: case ReadNextFile is failed")
                        Return False
                    End If
                End If
                current_File_Index += 1
                If current_File_Index > total_File_Count - 1 Then current_File_Index = 0

                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0420: case ReadNextFile")

            Case Mode_JumpBy
                is_Nav_Backwards = pending_Jump_Delta < 0
                current_File_Index += pending_Jump_Delta
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                ' The status belongs to the jump that actually happened - the callers
                ' used to announce "+100 files" even when this call bailed out early.
                If pending_Jump_Status <> "" Then lbl_Status.Text = Localization.T(pending_Jump_Status)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0421: case JumpBy " & pending_Jump_Delta.ToString())

            Case Mode_JumpTo
                is_Nav_Backwards = pending_Jump_Target < current_File_Index
                current_File_Index = pending_Jump_Target
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                If pending_Jump_Status <> "" Then lbl_Status.Text = Localization.T(pending_Jump_Status)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0422: case JumpTo " & pending_Jump_Target.ToString())

            Case Mode_Files '80
                If Not LoadFiles() Then Return False
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0430: case ReadFiles")

            Case Mode_SetFile '99
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0440: case SetFile")

            Case Mode_InSlideShow '0
                If total_File_Count <= 1 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0455: case InSlideShow but total_File_Count is 0")
                    SlideShowStop()
                    Return False
                End If

                If is_Slide_Show_Random_Mode Then
                    current_File_Index = slideshow_Rng.Next(0, total_File_Count)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0460: case RND InSlideShow")
                Else
                    ' Wrap to the start, exactly like the manual "next file" does. The
                    ' clamp that used to be here parked the slideshow on the last file
                    ' for good: the timer kept ticking, the mode stayed on, and every
                    ' tick re-read and re-decoded that same file from disk.
                    current_File_Index += 1
                    If current_File_Index < 0 Then current_File_Index = 0
                    If current_File_Index > total_File_Count - 1 Then current_File_Index = 0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0461: case InSlideShow")
                End If


            Case Mode_FolderAndFile '0
                lbl_Status.Text = Localization.T("чтение каталога.. ждите!")

                If Not LoadFiles() Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0450: case ReadFolderAndFile is failed")
                    Return False
                End If
                lbl_Status.Text = ""
                current_File_Index = 0

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0460: case ReadFolderAndFile")

            Case Mode_FolderAndKnownFile '91
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0470: isExternalInputReceived = " & is_External_Input_Received)
                is_File_Found = False

                If is_External_Input_Received Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0480: GetDirectoryInfo = " & Current_Folder_Path)

                    current_File_Index = 0
                    is_External_Input_Received = False
                    was_External_Input_Previously = True
                Else
                    was_External_Input_Previously = False
                    If Not LoadFilesForExternalInput(is_File_Found) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0490: case ReadFolderAndKnownFile is failed")
                        Return False
                    End If
                    If current_File_Index < 0 OrElse Not is_File_Found Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0500: targetImagePath not found in file list")
                        current_File_Index = 0
                        is_File_Found = True
                    End If
                End If
                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0510: case ReadFolderAndKnownFile")

            Case Mode_Prev '2
                is_Nav_Backwards = True
                If was_External_Input_Previously Then
                    If Not LoadFilesForExternalInput(is_File_Found) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0520: case ReadPrevFile is failed")
                        Return False
                    End If
                End If
                current_File_Index -= 1
                If current_File_Index < 0 Then current_File_Index = total_File_Count - 1
                lbl_Status.Text = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0530: case ReadPrevFile")

            Case Mode_Delete '3
#If Not NETFRAMEWORK Then
                ' One deletion route (R-1 §3.3). The policy decides bin versus permanent,
                ' and the confirmation and the status line are both built from that one
                ' decision, so they cannot disagree about what just happened.
                Dim forced_Permanent As Boolean = pending_Delete_Permanent
                pending_Delete_Permanent = False     ' read once, whatever the outcome
                If Not ExecuteDelete(Current_File_Name, forced_Permanent) Then Return False
#Else
                If String.IsNullOrEmpty(Current_File_Name) Then
                    lbl_Status.Text = Localization.T("! Нет файла для удаления")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0540: case DeleteFile failed")
                    Return False
                End If

                Dim confirmMsg = Localization.TF("Вы уверены, что хотите безвозвратно удалить файл '{0}'? Обратно его уже не уговорить.", Path.GetFileName(Current_File_Name))

                If Not Is_no_request_before_file_operation AndAlso
                    MessageBox.Show(confirmMsg, Localization.T("Подтверждение удаления"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then

                    Return False ' User cancelled
                End If

                Try
                    ' One release path for both builds and every operation: stops the GIF
                    ' timer, disposes the shown image AND its stream, clears the box (a
                    ' disposed image left assigned to a visible box crashes the next
                    ' repaint) and stops VLC - which on net48 is just as much the engine
                    ' holding an AVI/ZMBV/VP9 open as it is on the modern build.
                    ReleaseActiveMedia()

                    Dim doomed_File As String = Current_File_Name

                    If My.Computer.FileSystem.FileExists(doomed_File) Then
                        ' The worker is idle here: ReadShowMediaFile refuses the
                        ' DeleteFile mode while it is busy.
                        If UseAsyncFileOps() Then
                            Dim op As New FileOp With {.Kind = FileOpKind.Delete, .Source = doomed_File}
                            QueueFileOp(op)
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0550: file in task to be deleted: " & doomed_File)
                            ' Optimistic, rolled back by RunWorkerCompleted if it fails.
                            op.ListIndex = RemoveCurrentFileFromList(doomed_File)
                        Else
                            DeleteFile(doomed_File)
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0560: file deleted: " & doomed_File)
                            RemoveCurrentFileFromList(doomed_File)
                        End If
                        lbl_Status.Text = Localization.T("удалён: ") & doomed_File
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0570: case DeleteFile")
                    Else
                        lbl_Status.Text = Localization.T("! Файл не найден")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0580: case DeleteFile failed: not found")
                    End If
                Catch ex As Exception
                    ReportOperationError("E001", ex)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0590: ERR: " & ex.Message)
                End Try
#End If

            Case Mode_ForRandom '4
                If Not LoadFilesForRandomOrSlideshow(is_File_Found, True) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0600: case ReadForRandomOrSlideshow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0610: case ReadForRandomOrSlideshow")

            Case Mode_ForSlideShow '5
                If Not LoadFilesForRandomOrSlideshow(is_File_Found, is_Slide_Show_Random_Mode) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0620: case ReadForSlideShow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0630: case ReadForSlideShow")

            Case Mode_AfterUndo '98
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0640: case AfterUndo")

            Case Else
                ' A mode nobody implements would silently fall through here and "work"
                ' only by luck (see Mode_JumpTo's history). Say so in the log.
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0641: !! unknown read mode: " & read_Mode_Type)
        End Select

        Return True
    End Function

    ''' <summary>True when files_List/files_Array really describe Current_Folder_Path.
    '''
    ''' Replaces the old signal for "the list is not loaded yet": current_File_Index = 0.
    ''' That is not a fact about the list, it is a fact about the position - so standing
    ''' on the FIRST file of a folder and pressing R (or starting a slideshow) re-read
    ''' the whole directory that was already in memory. On \\p7\_i\output that is 157 ms
    ''' of "чтение каталога.. ждите!" for nothing; on a cold 15k folder, a second.</summary>
    Private Function IsFolderListLoaded() As Boolean
        If files_List Is Nothing AndAlso files_Array Is Nothing Then Return False
        If total_File_Count <= 0 Then Return False
        Return String.Equals(folder_List_Loaded_For, Current_Folder_Path, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Records which folder the list in memory belongs to. Called wherever a
    ''' scan result is accepted.</summary>
    Private Sub MarkFolderListLoaded()
        folder_List_Loaded_For = Current_Folder_Path
    End Sub

    Private Function LoadFilesForRandomOrSlideshow(ByRef is_File_Found As Boolean, is_Random_File_Mode As Boolean) As Boolean
        Try
            If Not IsFolderListLoaded() Then
                was_External_Input_Previously = False
                lbl_Status.Text = Localization.T("чтение каталога.. ждите!")
                Dim read_Error As Boolean = False
                Dim files As Object = GetFiles(read_Error)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0642: files got for slideshow")

                If files Is Nothing Then
                    ' Empty folder: nothing to run a slideshow over, but the session
                    ' stays - only a real read error clears the folder.
                    total_File_Count = 0
                    current_File_Index = 0
                    If read_Error Then
                        Current_Folder_Path = ""
                        cmbox_Media_Folder.Text = ""
                    End If
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0650: Error loading slideshow")
                    Return False
                End If

                If is_Files_Array_Active Then
                    Dim file_Entries = DirectCast(files, FileEntry())
                    files_Array = file_Entries.Select(Function(fe) fe.FilePath).ToArray()
                    files_List = Nothing ' Clear list when using array
                Else
                    files_List = DirectCast(files, List(Of String))
                    files_Array = Nothing ' Clear array when using list
                End If

                lbl_Status.Text = ""
                total_File_Count = If(is_Files_Array_Active, files_Array.Length, files_List.Count)
                MarkFolderListLoaded()
 #If Not NETFRAMEWORK Then
                ResetShuffleCycle()
 #End If
                current_File_Index = 0
                If total_File_Count <> 0 Then
                    If is_Random_File_Mode Then
#If Not NETFRAMEWORK Then
                        If modern_Preferences IsNot Nothing AndAlso modern_Preferences.SlideshowRandomOrder = "shuffleCycle" Then
                            current_File_Index = NextShuffleCycleIndex(total_File_Count, -1)
                        Else
#End If
                        current_File_Index = slideshow_Rng.Next(0, total_File_Count)
#If Not NETFRAMEWORK Then
                        End If
#End If
                        is_File_Found = True
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0660: New random file set currentFileIndex=" & current_File_Index.ToString)
                    Else
                        current_File_Index = If(is_Files_Array_Active, Array.IndexOf(files_Array, Current_Image_Path), files_List.IndexOf(Current_Image_Path))
                        is_File_Found = current_File_Index >= 0
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0670: Next slideshow file set currentFileIndex=" & current_File_Index.ToString)
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0680: No files for slides")
                End If
            Else
                lbl_Status.Text = ""
                If is_Random_File_Mode Then
#If Not NETFRAMEWORK Then
                    If modern_Preferences IsNot Nothing AndAlso modern_Preferences.SlideshowRandomOrder = "shuffleCycle" Then
                        current_File_Index = NextShuffleCycleIndex(total_File_Count, current_File_Index)
                    Else
#End If
                    current_File_Index = slideshow_Rng.Next(0, total_File_Count)
#If Not NETFRAMEWORK Then
                    End If
#End If
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0690: random file set")
                Else
                    ' Wrap, like every other "next file" path: the bare increment here
                    ' was only saved by a clamp further down, which parked the slideshow
                    ' on the last file.
                    current_File_Index += 1
                    If current_File_Index > total_File_Count - 1 Then current_File_Index = 0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0700: slide file set")
                End If
            End If
            Return True
        Catch ex As Exception
            ReportOperationError("E002", ex)
            Current_Folder_Path = ""
            cmbox_Media_Folder.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0710: E002 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFilesForExternalInput(ByRef is_File_Found As Boolean) As Boolean
        Try
            If was_External_Input_Previously Then
                was_External_Input_Previously = False
                lbl_Status.Text = Localization.T("чтение каталога.. ждите!")

                Dim read_Error As Boolean = False
                Dim files As Object = GetFiles(read_Error)
                If files Is Nothing Then
                    total_File_Count = 0
                    current_File_Index = 0
                    ' Only a real read error is worth throwing the session away.
                    If read_Error Then
                        Current_Folder_Path = ""
                        cmbox_Media_Folder.Text = ""
                    End If
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0720: files aren't set")
                    Return False
                End If

                If is_Files_Array_Active Then
                    Dim file_Entries = DirectCast(files, FileEntry())
                    files_Array = file_Entries.Select(Function(fe) fe.FilePath).ToArray()
                    files_List = Nothing ' Clear list when using array
                Else
                    files_List = DirectCast(files, List(Of String))
                    files_Array = Nothing ' Clear array when using list
                End If

                lbl_Status.Text = ""
                total_File_Count = If(is_Files_Array_Active, files_Array.Length, files_List.Count)
                MarkFolderListLoaded()
                current_File_Index = If(is_Files_Array_Active, Array.IndexOf(files_Array, Current_Image_Path), files_List.IndexOf(Current_Image_Path))
                is_File_Found = current_File_Index >= 0

                If Not is_File_Found Then
                    If is_Files_Array_Active Then
                        files_Array = AddAt(files_Array, Current_Image_Path, 0)
                    Else
                        files_List.Insert(0, Current_Image_Path)
                    End If
                    total_File_Count += 1
                    current_File_Index = 0
                    is_File_Found = True
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0745: targetImagePath added to file list")
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0740: new folder is read")
                Return True
            Else
                current_File_Index += 1
                is_File_Found = True
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0750: next one is chosen")
                Return True
            End If
        Catch ex As Exception
            ReportOperationError("E003", ex)
            Current_Folder_Path = ""
            cmbox_Media_Folder.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0760: E003 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFiles() As Boolean
        Try
            Dim read_Error As Boolean = False
            Dim files As Object = GetFiles(read_Error)
            If files Is Nothing Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0770: files arnt set (read_Error=" & read_Error.ToString() & ")")
                total_File_Count = 0
                current_File_Index = 0

                ' An empty folder is a valid answer, not a failure: keep the folder and
                ' let the caller wipe the media surface. Only a real read error costs
                ' the session (and even then GetFiles has already said so in the status).
                If read_Error Then
                    lbl_Status.Text = Localization.T("! Ошибка чтения файлов")
                    Current_Folder_Path = ""
                    cmbox_Media_Folder.Text = ""
                    files_List = Nothing
                    files_Array = Nothing
                    Return False
                End If

                ' Empty: an EMPTY list, not Nothing - that way the display pipeline runs
                ' its "no files" branch (blank surface, "! Нет файлов в папке") instead
                ' of bailing out on "no file list at all" with a stale image on screen.
                is_Files_Array_Active = False
                files_List = New List(Of String)()
                files_Array = Nothing
                Return True
            End If

            If is_Files_Array_Active Then
                Dim file_Entries = DirectCast(files, FileEntry())
                files_Array = file_Entries.Select(Function(fe) fe.FilePath).ToArray()
                files_List = Nothing ' Clear list when using array
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0780: folder files ARRAY is counted: " & files_Array.Length.ToString)
            Else
                files_List = DirectCast(files, List(Of String))
                files_Array = Nothing ' Clear array when using list
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0790: folder files LIST is counted: " & files_List.Count.ToString)
            End If

            total_File_Count = If(is_Files_Array_Active, files_Array.Length, files_List.Count)
            MarkFolderListLoaded()

            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0800: E004 " & ex.Message)
            lbl_Status.Text = Localization.T("! Ошибка чтения файлов")
            ReportOperationError("E004", ex)
            Current_Folder_Path = ""
            cmbox_Media_Folder.Text = ""
            total_File_Count = 0
            current_File_Index = 0

            Return False
        End Try
    End Function

    Private Sub LoadStandardImageInPictureBox()
        ' Don't immediately hide the current image - let it stay visible until the new one is ready
        is_WebBrowser_Visible = False

        If current_Loaded_File_Name <> Current_File_Name Then

#If Not NETFRAMEWORK Then
            ' Invariant 8 of the zoom spec: every new file opens fitted. Nothing in the
            ' display path did it, so the boxes kept the previous picture's geometry -
            ' a 4000x3000 shot left at 200 % made the next 800x600 one show at roughly
            ' 1000 % while the label still said "200 %", the left click stopped flipping
            ' (zoom_Scale = 0 means "zoomed"), and Draw_Perspective went off to build a
            ' background for an 8000x6000 box on every single file.
            ' Programmatic: this fit is the display path tidying up after the previous
            ' picture, not the user choosing Fit, so it must not overwrite the per-folder
            ' zoom §4.2 remembers.
            If zoom_Scale <> 1 Then
                is_Zoom_Applied_By_Program = True
                Try
                    ZoomToFit()
                Finally
                    is_Zoom_Applied_By_Program = False
                End Try
            End If
#End If

            ' The third condition is not paranoia: "LOADED" and the name only say a
            ' decode finished for this path - they do not say the image is still in the
            ' box. Check the box itself before swapping to it.
            If bgWorker_Result = "LOADED" AndAlso
            current_Second_File_Name = Current_File_Name AndAlso
            (If(is_Second_PictureBox_Active, Picture_Box_1.Image, Picture_Box_2.Image)) IsNot Nothing Then

                ' Pre-loaded image is available - use it immediately
                If Not is_Second_PictureBox_Active Then
                    ' Switch to PictureBox2 - make it visible FIRST, then hide PictureBox1
                    is_PictureBox2_Visible = True
                    UpdateControlVisibility() ' Update visibility immediately
                    is_PictureBox1_Visible = False
                    StartGifLoopPlayback(Picture_Box_2.Image)

                    bgWorker_Result = "USED P2"
                    is_Second_PictureBox_Active = True
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0870: P2 is found already loaded isSecondaryPictureBoxActive=true")
                Else
                    ' Switch to PictureBox1 - make it visible FIRST, then hide PictureBox2
                    is_PictureBox1_Visible = True
                    UpdateControlVisibility() ' Update visibility immediately
                    is_PictureBox2_Visible = False
                    StartGifLoopPlayback(Picture_Box_1.Image)

                    bgWorker_Result = "USED P1"
                    is_Second_PictureBox_Active = False
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0880: P1 is found already loaded isSecondaryPictureBoxActive =false")
                End If
            Else
                ' No pre-loaded image - load it now
                Try
                    ' Check if file exists and is accessible
                    If Not File.Exists(Current_File_Name) Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0906: File does not exist: " & Current_File_Name)

                        ' File.Exists says False for a deleted file and for a share that has
                        ' stopped answering; ReadFailure asks the folder which one this is.
                        Dim absent_Kind As PathFailureKind = ReadFailure(PathFailureKind.Missing)
                        If Not FailureKeepsFile(absent_Kind) Then
                            lbl_Status.Text = Localization.TF("Файл не найден: {0}", Path.GetFileName(Current_File_Name))
                        End If

                        SkipUnreadableFile(absent_Kind)
                        Return
                    End If

                    ' Verify file is not empty
                    Dim fileInfo As New FileInfo(Current_File_Name)
                    If fileInfo.Length = 0 Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0907: File is empty: " & Current_File_Name)
                        lbl_Status.Text = Localization.TF("Файл пуст: {0}", Path.GetFileName(Current_File_Name))

                        SkipUnreadableFile(PathFailureKind.Content)
                        Return
                    End If

                    ' sza250609 - GIF fix
                    ' Decodes off the UI thread and puts a badge up if it takes long
                    ' enough to look like a hang - a big WEBP takes seconds on the
                    ' managed decoder, and this call is what froze the window with the
                    ' PREVIOUS file still on it. See Main_Form.LoadingIndicator.vb;
                    ' otherwise identical to LoadImageWithStream.
                    Dim decode_Failure As PathFailureKind = PathFailureKind.Content
                    Dim image_Data_Tuple As Tuple(Of Image, IO.MemoryStream) =
                        LoadImageWithProgress(Current_File_Name, fileInfo.Length, decode_Failure)

                    If image_Data_Tuple IsNot Nothing Then
                        Dim loaded_Image As Image = image_Data_Tuple.Item1
                        Dim loaded_Image_Stream As IO.MemoryStream = image_Data_Tuple.Item2

                        If Not is_this_First_Picture_File_We_Show AndAlso is_Second_PictureBox_Active Then
                            ' Use PictureBox2 - load image first, then update visibility
                            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                            If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream?.Dispose()
                            Picture_Box_2.Image = loaded_Image
                            pictureBox2_Stream = loaded_Image_Stream
                            StartGifLoopPlayback(Picture_Box_2.Image)

                            ' Now update visibility - show P2 first, then hide P1
                            is_PictureBox2_Visible = True
                            UpdateControlVisibility()
                            is_PictureBox1_Visible = False
                            is_Second_PictureBox_Active = True
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0890: P2 set (not found loaded) isSecondaryPictureBoxActive=true")
                        Else
                            ' Use PictureBox1 - load image first, then update visibility
                            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                            If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream?.Dispose()
                            Picture_Box_1.Image = loaded_Image
                            pictureBox1_Stream = loaded_Image_Stream
                            StartGifLoopPlayback(Picture_Box_1.Image)

                            ' Now update visibility - show P1 first, then hide P2
                            is_PictureBox1_Visible = True
                            UpdateControlVisibility()
                            is_PictureBox2_Visible = False
                            is_Second_PictureBox_Active = False
                            is_this_First_Picture_File_We_Show = False
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0900: P1 set (not found loaded) isSecondaryPictureBoxActive=false")
                        End If
                    Else
                        ' Image loading failed - skip to next file
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0908: Image loading failed for: " & Current_File_Name)

                        ' Two very different failures arrive as Nothing: a file that is not a
                        ' picture, and a decode abandoned at its deadline because the read
                        ' blocked on a dead SMB session. Only the first one is about the file.
                        Dim load_Kind As PathFailureKind = ReadFailure(decode_Failure)
                        If Not FailureKeepsFile(load_Kind) Then
                            lbl_Status.Text = Localization.TF("Не удалось загрузить: {0}", Path.GetFileName(Current_File_Name))
                        End If

                        SkipUnreadableFile(load_Kind)
                        Return
                    End If
                Catch ex As ArgumentException
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0905: ArgumentException loading image: " & ex.Message & " File: " & Current_File_Name)
                    lbl_Status.Text = Localization.TF("Недопустимый файл изображения: {0}", Path.GetFileName(Current_File_Name))

                    ' GDI+ reports a corrupt bitmap this way, so the call site knows better
                    ' than the classifier here: this is the content, not the path.
                    SkipUnreadableFile(PathFailureKind.Content)
                    Return
                Catch ex As OutOfMemoryException
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0909: OutOfMemoryException loading image: " & ex.Message & " File: " & Current_File_Name)
                    lbl_Status.Text = Localization.TF("Недостаточно памяти для загрузки: {0}", Path.GetFileName(Current_File_Name))

                    SkipUnreadableFile(PathFailureKind.OutOfMemory)
                    Return
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0911: Error loading image: [" & ex.GetType().Name & "] " & ex.Message & " File: " & Current_File_Name)

                    ' The whole fix in one line: an IOException from a dropped share is a
                    ' transport failure, and a transport failure leaves the list alone.
                    Dim caught_Kind As PathFailureKind = PathFailure.Classify(ex)
                    If Not FailureKeepsFile(caught_Kind) Then
                        lbl_Status.Text = Localization.TF("Ошибка загрузки: {0}", Path.GetFileName(Current_File_Name))
                    End If

                    SkipUnreadableFile(caught_Kind)
                    Return
                End Try
            End If
            current_Loaded_File_Name = Current_File_Name

            ' Final visibility update
            UpdateControlVisibility()

#If Not NETFRAMEWORK Then
            ' The picture is on screen and fitted; §4.2 gets to say whether it stays that
            ' way, opens at 100 % or takes this folder's remembered scale. It runs BEFORE
            ' the perspective draw below so the bars are built for the final geometry.
            ApplyNewImageScaleMode()
#End If

            ' A new photo just went up, so dynamic perspective grows its halo here (and
            ' only here - see Draw_Perspective's animate parameter).
            If is_form_shown Then Draw_Perspective(animate:=True)
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0920: file is a same, pic set is skipped")
        End If

#If NETFRAMEWORK Then
        ' net48 only: even READING DocumentText forces the IE ActiveX host into
        ' existence, so the modern build must never touch it (video there never
        ' renders in the WebBrowser to begin with).
        If Not Web_Browser.DocumentText = "" Then
            Web_Browser.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0940: WB blank")
        End If
#End If

    End Sub

    ''' <summary>Blanks the media surface for a file that IS in the list but has no
    ''' display path (svg on both builds, heic/heif/avif on x86 only - scanned and
    ''' sortable, but undecodable here).
    '''
    ''' The active picture box is emptied but STAYS VISIBLE. Hiding every surface looks
    ''' tidier and is a trap: the left click - this app's "next file" gesture - only
    ''' lands on a picture box or the form, so with everything hidden the click hits the
    ''' bare media panel, nothing happens, and the user is stranded on a file they
    ''' cannot even see. An empty visible box keeps the way out.</summary>
    Private Sub ShowUnsupportedFormat(file_Path As String)
        ' Frees the shown image and its stream, stops the GIF timer and VLC - and,
        ' importantly, leaves the visibility flags alone.
        ReleaseActiveMedia()

        ' Coming from a video there is no picture box up at all - put one back, empty,
        ' so there is something to click on.
        If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then
            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image.Dispose()
            Picture_Box_1.Image = Nothing
            If pictureBox1_Stream IsNot Nothing Then
                pictureBox1_Stream.Dispose()
                pictureBox1_Stream = Nothing
            End If
            is_PictureBox1_Visible = True
            is_Second_PictureBox_Active = False
        End If

        is_WebBrowser_Visible = False

        ' There is no bitmap here to derive a background from, so the previous file's
        ' perspective bars and tint would just stay up - the "black screen" the user
        ' lands on would go on wearing the colours of the photo before it.
        ResetBackgroundToNeutral()

        UpdateControlVisibility()

        current_Loaded_File_Name = file_Path
        lbl_Status.Text = Localization.TF("Формат не поддерживается: {0}", Path.GetFileName(file_Path))
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1045: unsupported format, surface blanked: " & file_Path)
    End Sub

    ''' <summary>What a surface with NO image wears: the scheme's own base colour, never
    ''' the tint carried over from the file before it. White belongs to the white scheme
    ''' alone - every other scheme is dark, which is exactly what the fixed "black"
    ''' scheme and ApplyTitleBarTheme already assume.</summary>
    Private Function NeutralBackColor() As System.Drawing.Color
        Return If(Form_Color_Scheme = 2, System.Drawing.Color.White, System.Drawing.Color.Black)
    End Function

    ''' <summary>Puts back_Color on the form, the media panel, the chrome and the visible
    ''' picture box - the one place that knows the full set. last_Back_Color makes an
    ''' unchanged colour free, so callers may fire it on every frame.</summary>
    Private Sub ApplyBackgroundColor(back_Color As System.Drawing.Color)
        If back_Color = last_Back_Color Then Return
        last_Back_Color = back_Color

        Me.BackColor = back_Color

        Dim opposite_Color As System.Drawing.Color = GetOppositeColor(back_Color)
        If panel_Media IsNot Nothing Then panel_Media.BackColor = back_Color
        RecolorChrome(back_Color, opposite_Color)

        If is_PictureBox1_Visible Then
            Picture_Box_1.BackColor = back_Color
        ElseIf is_PictureBox2_Visible Then
            Picture_Box_2.BackColor = back_Color
        End If
    End Sub

    ''' <summary>Drops everything the PREVIOUS file left on the surface: its perspective
    ''' bars and its tint. Called wherever the media is blanked with nothing to put in
    ''' its place - the colour analysis in UpdateControlVisibility can only derive a
    ''' background FROM a bitmap, so with no bitmap it silently keeps the last one's,
    ''' and the user is left looking at a background belonging to a file that is no
    ''' longer on screen.</summary>
    Private Sub ResetBackgroundToNeutral()
        ClearPerspectiveBackground()
        ApplyBackgroundColor(NeutralBackColor())
    End Sub

    Private Sub UpdateControlVisibility()

        ' Any navigation to an image or a browser-played video supersedes VLC fallback playback.
        If is_Vlc_Playing AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible OrElse is_WebBrowser_Visible) Then
            StopVlcPlayback()
        End If

        Picture_Box_1.Visible = is_PictureBox1_Visible
        Picture_Box_2.Visible = is_PictureBox2_Visible
        Web_Browser.Visible = is_WebBrowser_Visible

        If (is_PictureBox1_Visible OrElse
        is_PictureBox2_Visible) AndAlso
        (Not Is_slide_show_mode Or
        SlideShowTimer.Interval >= slideshow_limit_to_change_color) Then

            Web_Browser.Visible = False

            Dim pic_to_Display As Int16 = 0

            If is_PictureBox1_Visible AndAlso
                Picture_Box_1.Image IsNot Nothing AndAlso
                TypeOf Picture_Box_1.Image Is Bitmap Then

                pic_to_Display = 1

            ElseIf is_PictureBox2_Visible AndAlso
                Picture_Box_2.Image IsNot Nothing AndAlso
                TypeOf Picture_Box_2.Image Is Bitmap Then

                pic_to_Display = 2
            End If

            Dim back_Color As System.Drawing.Color = Me.BackColor

            Dim active_Bitmap As Bitmap = Nothing

            ' The dynamic-background analysis below pokes the freshly-loaded bitmap
            ' with GetPixel/.Width. GDI+ can transiently fail there (e.g. while the
            ' background worker is concurrently decoding on a slow network share),
            ' throwing OverflowException / "Parameter is not valid". That must NOT
            ' abort the display: the image is already on the PictureBox. Swallow the
            ' analysis failure and keep the previous background colour for this frame.
            Try

            If Form_Color_Scheme = 2 Then
                back_Color = System.Drawing.Color.White
            ElseIf Form_Color_Scheme = 0 Then

                If pic_to_Display = 1 Then
                    active_Bitmap = CType(Picture_Box_1.Image, Bitmap)
                ElseIf pic_to_Display = 2 Then
                    active_Bitmap = CType(Picture_Box_2.Image, Bitmap)
                End If

                If active_Bitmap IsNot Nothing Then
                    If 1 < active_Bitmap.Width AndAlso
                    1 < active_Bitmap.Height Then

                        If active_Bitmap.Width > second_Color_X AndAlso
                        active_Bitmap.Height > second_Color_Y Then

                            Dim first_Color_Pixel = active_Bitmap.GetPixel(first_Color_X, first_Color_Y)
                            Dim second_Color_Pixel = active_Bitmap.GetPixel(second_Color_X, second_Color_Y)

                            ' Fix: Remove alpha channel to prevent transparent background colors
                            first_Color_Pixel = Color.FromArgb(255, first_Color_Pixel.R, first_Color_Pixel.G, first_Color_Pixel.B)
                            second_Color_Pixel = Color.FromArgb(255, second_Color_Pixel.R, second_Color_Pixel.G, second_Color_Pixel.B)

                            Dim dif As Long = CLng(Math.Abs(CInt(second_Color_Pixel.R) - CInt(first_Color_Pixel.R))) +
                                              CLng(Math.Abs(CInt(second_Color_Pixel.G) - CInt(first_Color_Pixel.G))) +
                                              CLng(Math.Abs(CInt(second_Color_Pixel.B) - CInt(first_Color_Pixel.B)))
                            If dif < percent_of_color_deviation Then
                                back_Color = first_Color_Pixel
                            Else
                                Dim corner_Pixel = active_Bitmap.GetPixel(CInt(active_Bitmap.Width / percent_of_second_Color_Point), CInt(active_Bitmap.Height / percent_of_second_Color_Point))
                                ' Fix: Remove alpha channel
                                back_Color = Color.FromArgb(255, corner_Pixel.R, corner_Pixel.G, corner_Pixel.B)
                            End If
                        Else
                            Dim corner_Pixel = active_Bitmap.GetPixel(CInt(active_Bitmap.Width / percent_of_second_Color_Point), CInt(active_Bitmap.Height / percent_of_second_Color_Point))
                            ' Fix: Remove alpha channel
                            back_Color = Color.FromArgb(255, corner_Pixel.R, corner_Pixel.G, corner_Pixel.B)
                        End If

                    End If
                End If
            ElseIf Form_Color_Scheme = 3 Then 'by side

                If pic_to_Display = 1 Then
                    active_Bitmap = CType(Picture_Box_1.Image, Bitmap)
                ElseIf pic_to_Display = 2 Then
                    active_Bitmap = CType(Picture_Box_2.Image, Bitmap)
                End If

                If active_Bitmap IsNot Nothing AndAlso
                 1 < active_Bitmap.Width AndAlso
                    1 < active_Bitmap.Height Then

                    Dim side_Pixel_Color As System.Drawing.Color
                    Dim difR, difG, difB As Long
                    Dim c As Integer = 0
                    For z = 0 To active_Bitmap.Height - 1 Step step_size_while_color_Search
                        side_Pixel_Color = active_Bitmap.GetPixel(1, z)
                        difR += CInt(side_Pixel_Color.R)
                        difG += CInt(side_Pixel_Color.G)
                        difB += CInt(side_Pixel_Color.B)
                        c += 1
                    Next

                    ' Fix: Ensure the resulting color is fully opaque
                    back_Color = Color.FromArgb(255, CInt(difR / c), CInt(difG / c), CInt(difB / c))
                End If

            ElseIf Form_Color_Scheme = 4 Then 'by top

                If pic_to_Display = 1 Then
                    active_Bitmap = CType(Picture_Box_1.Image, Bitmap)
                ElseIf pic_to_Display = 2 Then
                    active_Bitmap = CType(Picture_Box_2.Image, Bitmap)
                End If

                If active_Bitmap IsNot Nothing AndAlso
                 1 < active_Bitmap.Width AndAlso
                    1 < active_Bitmap.Height Then

                    Dim top_Pixel_Color As System.Drawing.Color
                    Dim difR, difG, difB As Long
                    Dim c As Integer = 0
                    For z = 0 To active_Bitmap.Width - 1 Step step_size_while_color_Search
                        top_Pixel_Color = active_Bitmap.GetPixel(z, 1)
                        difR += CInt(top_Pixel_Color.R)
                        difG += CInt(top_Pixel_Color.G)
                        difB += CInt(top_Pixel_Color.B)
                        c += 1
                    Next

                    ' Fix: Ensure the resulting color is fully opaque
                    back_Color = Color.FromArgb(255, CInt(difR / c), CInt(difG / c), CInt(difB / c))
                End If
            ElseIf Form_Color_Scheme = 5 Then 'by buttom

                If pic_to_Display = 1 Then
                    active_Bitmap = CType(Picture_Box_1.Image, Bitmap)
                ElseIf pic_to_Display = 2 Then
                    active_Bitmap = CType(Picture_Box_2.Image, Bitmap)
                End If

                If active_Bitmap IsNot Nothing AndAlso
                 1 < active_Bitmap.Width AndAlso
                    1 < active_Bitmap.Height Then

                    Dim bottom_Pixel_Color As System.Drawing.Color
                    Dim difR, difG, difB As Long
                    Dim c As Integer = 0
                    For z = 0 To active_Bitmap.Width - 1 Step step_size_while_color_Search
                        bottom_Pixel_Color = active_Bitmap.GetPixel(z, active_Bitmap.Height - 1)
                        difR += CInt(bottom_Pixel_Color.R)
                        difG += CInt(bottom_Pixel_Color.G)
                        difB += CInt(bottom_Pixel_Color.B)
                        c += 1
                    Next

                    ' Fix: Ensure the resulting color is fully opaque
                    back_Color = Color.FromArgb(255, CInt(difR / c), CInt(difG / c), CInt(difB / c))
                End If
            End If


            ApplyBackgroundColor(back_Color)

            Catch ex As Exception
                ' Keep the image visible; just skip recoloring this frame.
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0946: background analysis skipped: [" & ex.GetType().Name & "] " & ex.Message)
            End Try

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0945: picture box sizes: " & If(is_PictureBox1_Visible, "P1: ", "P2: ") & If(is_PictureBox1_Visible, Picture_Box_1.Width.ToString, Picture_Box_2.Width.ToString) & "x" & If(is_PictureBox1_Visible, Picture_Box_1.Height.ToString, Picture_Box_2.Height.ToString))
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0950: Visibility set: " & If(is_PictureBox1_Visible, "P1-YES ", "P1-NO ") & If(is_PictureBox2_Visible, "P2-YES ", "P2-NO ") & If(is_WebBrowser_Visible, "WB-YES ", "WB-NO "))

        ' A media-surface change may have re-ordered z (video BringToFront) - keep
        ' the recipients overlay clickable on top.
        KeepRecipientsOverlayOnTop()
    End Sub

    Private Sub UpdateCurrentFileAndDisplay(is_File_Found As Boolean, is_After_Undo_Operation As Boolean)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0381: UpdateCurrentFileAndDisplay, currentFileName: " & Current_File_Name)

        ' New media from here on: anything already in flight for the previous one is
        ' now stale and must not touch the screen when it comes back.
        media_Generation += 1
#If Not NETFRAMEWORK Then
        ClearAudioSurface()
        SetMediaDisplayKind(MediaKind.Image)
#End If

        Dim previous_File_Name As String = Current_File_Name
        Current_File_Name = ""
        current_Loaded_File_Name = "" ' Clear this to force reload

        ' Check if file collections are properly initialized
        If files_List Is Nothing And files_Array Is Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0385: Both files_List and files_Array are Nothing")
            lbl_Status.Text = Localization.T("! Нет списка файлов")
            Return
        End If

        If total_File_Count > 0 Then
            If current_File_Index < 0 Then current_File_Index = 0
            If current_File_Index >= total_File_Count Then
                current_File_Index = Math.Max(0, total_File_Count - 1)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0388: current_File_Index was too high, adjusted")
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0960: isFileFound = " & is_File_Found.ToString)
            If is_File_Found Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0970: currentFileIndex = " & current_File_Index.ToString)

                Try
                    Current_File_Name = If(is_Files_Array_Active, files_Array(current_File_Index), files_List(current_File_Index))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0970: currentFileIndex = " & current_File_Index.ToString & ", fileName = " & Current_File_Name)
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0971: Error getting current file name: " & ex.Message)
                    lbl_Status.Text = Localization.T("Ошибка получения имени файла")
                    Return
                End Try

#If Not NETFRAMEWORK Then
                ' Inside an archive the file does not exist until we extract it, and this
                ' has to happen BEFORE the "the file is gone" branch below - that branch
                ' drops the entry from the list, so every page of a comic would fall out of
                ' it one flip at a time. A no-op outside an archive.
                EnsureArchiveEntryOnDisk(Current_File_Name)
#End If

                If Not String.IsNullOrEmpty(Current_File_Name) AndAlso Not File.Exists(Current_File_Name) Then

                    ' Straight after an undo the file may still be travelling back (the
                    ' worker is moving it): that is not a stale entry, so leave the list
                    ' alone - dropping it here would undo the undo. The completion
                    ' handler shows it once it has landed.
                    If is_After_Undo_Operation Then
                        lbl_Status.Text = Localization.TF("Файл {0} перемещается назад операционной системой.", Current_File_Name)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0974: after undo - file not back yet, list kept")
                        Return
                    End If

                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0975: New current file does not exist: " & Current_File_Name)

#If Not NETFRAMEWORK Then
                    ' The SEVENTH removal site, and the one a network blip reaches FIRST -
                    ' the specification's §3.8 named the six inside SkipUnreadableFile and
                    ' missed this one, which never goes through it. Same reasoning, same fix:
                    ' File.Exists answers False for a deleted file and for a share that has
                    ' stopped answering, so ask the folder before believing the file is gone.
                    '
                    ' The index is deliberately NOT rolled back: the user pressed Next, so
                    ' moving on is what they asked for. It is the LIST that must survive.
                    If ReadFailure(PathFailureKind.Missing) = PathFailureKind.Transport Then
                        lbl_Status.Text = ReadFailureText(PathFailureKind.Transport, Current_File_Name)
                        AppFileLogger.WriteLine("Folder stopped answering, list kept: " & Current_File_Name)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0978: folder unreachable - list kept intact")
                        Return
                    End If
#End If

                    lbl_Status.Text = Localization.T("Файл не найден, переход к следующему")

                    ' Remove the invalid file from the list and try the next one
                    Try
                        If is_Files_Array_Active Then
                            files_Array = RemoveAt(files_Array, current_File_Index)
                        Else
                            files_List.RemoveAt(current_File_Index)
                        End If
                        total_File_Count -= 1
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0976: Error removing invalid file: " & ex.Message)
                    End Try

                    ' Adjust index if necessary
                    If current_File_Index >= total_File_Count Then
                        current_File_Index = Math.Max(0, total_File_Count - 1)
                    End If

                    ' Try again with the adjusted index
                    If total_File_Count > 0 Then
                        Current_File_Name = If(is_Files_Array_Active, files_Array(current_File_Index), files_List(current_File_Index))
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0976: Adjusted to new file: " & Current_File_Name)
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0977: No more files available")
                        Return
                    End If
                End If
            Else
                ' Not found in the freshly-read list. Current_Image_Path is only set by
                ' an external open, so falling back to it blind showed a file from a
                ' PREVIOUS folder under this folder's counter - and DEL then deleted
                ' that stranger while removing an entry of the current folder from the
                ' list. Trust the file actually on screen; failing that, the first one.
                Dim recovered_At As Integer = -1
                If Not String.IsNullOrEmpty(Current_File_Name) Then
                    recovered_At = If(is_Files_Array_Active, Array.IndexOf(files_Array, Current_File_Name), files_List.IndexOf(Current_File_Name))
                End If

                If recovered_At >= 0 Then
                    current_File_Index = recovered_At
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0973: recovered current file at " & recovered_At.ToString())
                Else
                    current_File_Index = 0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0972: current file is not in this folder - showing the first one")
                End If
                Current_File_Name = If(is_Files_Array_Active, files_Array(current_File_Index), files_List(current_File_Index))
                Current_Image_Path = Current_File_Name
#If Not NETFRAMEWORK Then
                ' Same reason as above: this branch reaches a file without going past the
                ' existence check, and inside an archive nothing is on disk until asked.
                EnsureArchiveEntryOnDisk(Current_File_Name)
#End If
            End If

            If Not String.IsNullOrEmpty(Current_File_Name) Then
                ' Keep it on the file actually being shown. It used to be written only
                ' by an external open, so after browsing away it still named a file in
                ' some earlier folder - and every lookup that trusts it (rescan after an
                ' external open, slideshow start) went looking for the wrong file.
                Current_Image_Path = Current_File_Name

                recent_Media_File_List.Remove(Current_File_Name)
                recent_Media_File_List.Add(Current_File_Name)
                If recent_Media_File_List.Count > RecentFilesLimit() Then
                    recent_Media_File_List.RemoveAt(0)
                End If

#If Not NETFRAMEWORK Then
                ' The same moment, for the same reason: this is where a file counts as
                ' shown (SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10 §3.4). The MarkSettingsDirty
                ' below flushes it with the rest.
                RememberLastPlayedFile(Current_File_Name)
#End If

                ' The recent list and the folder position (LastCounter) now exist only in
                ' memory until something writes them; ask for the trailing-edge flush so an
                ' ungraceful exit costs at most the last minute instead of the whole session.
                MarkSettingsDirty()
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0980: currentFileName = " & Current_File_Name)

            Dim current_File_Number As Integer = current_File_Index + 1
            ' One formatted string, not three concatenated pieces: in Arabic and Urdu
            ' bidi reorders a concatenation into "from 5 1".
            lbl_File_Number.Text = Localization.TF("{0} из {1}", current_File_Number, total_File_Count)

            Try
                Dim current_File_Extension As String = Path.GetExtension(Current_File_Name).ToLower()
                Dim current_File_Uri As String = New Uri(Current_File_Name).ToString()

                If Image_File_Extensions.Contains(current_File_Extension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1030: P to load")
                    LoadStandardImageInPictureBox()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1040: Picture box is set")
#If Not NETFRAMEWORK Then
                ElseIf KindOf(current_File_Extension) = MediaKind.Audio Then
                    ' Audio branch (modern only): same playback engine (LibVLC) as video,
                    ' but with audio surface instead of VideoView (SPECIFICATION_AUDIO_FIRST_CLASS_DOTNET10.md PA-1).
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: Audio to load")
                    SetMediaDisplayKind(MediaKind.Audio)
                    PlayVideoWithVlcAsync(Current_File_Name)
                    RequestAudioMetadataAsync(Current_File_Name, media_Generation)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: Audio is set")
#End If
                ElseIf video_File_Extensions.Contains(current_File_Extension) Then
#If NETFRAMEWORK Then
                    ' net48: try the IE WebBrowser first (H.264/MP4), LibVLC picks
                    ' up unsupported codecs via HandleVideoError.
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: WB to load")
                    LoadVideoInWebBrowser(current_File_Uri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: WB is set")
#Else
                    ' Modern (.NET 10): single video engine - straight to LibVLC,
                    ' no WebBrowser round-trip (SPECIFICATION_DOTNET10_MODERN_BUILD §6.2).
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: VLC to load")
                    SetMediaDisplayKind(MediaKind.Video)
                    PlayVideoWithVlcAsync(Current_File_Name)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: VLC is set")
#End If
                Else
                    ' The file is in the list (it is scanned and sortable) but no
                    ' decoder claims it - svg, plus heic/heif/avif on the x86 build.
                    ' Blank the surface honestly:
                    ' leaving the PREVIOUS image up while the counter and
                    ' Current_File_Name already point here means DEL / a move hotkey
                    ' act on a file the user cannot see.
                    ShowUnsupportedFormat(Current_File_Name)
                End If

                is_First_Picture_Box_Need_To_Be_Cached = is_Second_PictureBox_Active

                ' A new file is on screen, so any verdict from an earlier prefetch is
                ' about the past. It was never cleared here, so a leftover "LOADED"
                ' could pass the readiness test on some later file and put that older
                ' picture up under the new file's name.
                bgWorker_Result = "EMPTY"
                current_Second_File_Name = ""

                If is_Slide_Show_Random_Mode OrElse is_File_Reseived_From_Outside Then
                    next_File_After_Current = ""
                    is_File_Reseived_From_Outside = False
                ElseIf Not was_External_Input_Previously AndAlso
                        Not (files_List Is Nothing And files_Array Is Nothing) Then
                    next_File_After_Current = If(total_File_Count > 0, If(total_File_Count = current_File_Index + 1, If(is_Files_Array_Active, files_Array(0), files_List(0)), If(is_Files_Array_Active, files_Array(current_File_Index + 1), files_List(current_File_Index + 1))), "")
                Else
                    next_File_After_Current = ""
                End If

                If Not Is_No_Background_Tasks Then
                    ' A snapshot: which file, which neighbour, which box, which folder -
                    ' all decided here, on the UI thread. The worker reads none of it
                    ' live any more.
                    Dim new_Args As New PrefetchRequest With {
                        .CurrentFile = Current_File_Name,
                        .NextFile = next_File_After_Current,
                        .FolderPath = Current_Folder_Path,
                        .TargetIsBox1 = is_First_Picture_Box_Need_To_Be_Cached,
                        .CountFolder = was_External_Input_Previously,
                        .IsRandomMode = is_Slide_Show_Random_Mode
                    }
#If Not NETFRAMEWORK Then
                    ' The session travels in the snapshot like everything else the worker
                    ' needs, so the worker can extract the NEXT entry before decoding it -
                    ' and never reads a live field to find out whether an archive is open.
                    new_Args.Archive = archive_Session
                    ' Counting the folder in the background is meaningless here: an archive
                    ' knows exactly how many entries it has, and the count would come from
                    ' walking a directory that only holds what has been viewed (§3.3).
                    If archive_Session IsNot Nothing Then new_Args.CountFolder = False
#End If

                    If is_BgWorker_Online OrElse BgWorker.IsBusy Then
                        ' Store the pending operation instead of canceling
                        bgWorker_Pending_Args = new_Args
                        bgWorker_Has_Pending_Operation = True
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1050: BgWorker operation queued")
                    Else
                        ' Start the operation immediately
                        is_BgWorker_Online = True
                        bgworker_Done.Reset()
                        BgWorker.RunWorkerAsync(new_Args)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1060: BgWorker is run")
                    End If
                Else
                    lbl_Current_File.Text = Localization.T("Текущий: ") & Current_File_Name
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1065: BgWorker is not run, online=" & is_BgWorker_Online.ToString & " IsBusy=" & BgWorker.IsBusy.ToString)
                End If

            Catch ex As Exception
                If Not is_After_Undo_Operation Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1070: E005 [" & ex.GetType().Name & "] " & ex.Message & " File: " & Current_File_Name)

                    ' Instead of showing error, try to skip to next file
                    lbl_Status.Text = Localization.TF("Ошибка файла, переход к следующему: {0}", Path.GetFileName(Current_File_Name))

                    ' Remove the problematic file from the list
                    If is_Files_Array_Active Then
                        files_Array = RemoveAt(files_Array, current_File_Index)
                    Else
                        files_List.RemoveAt(current_File_Index)
                    End If
                    total_File_Count -= 1

                    ' Adjust index and try next file
                    If current_File_Index >= total_File_Count Then
                        current_File_Index = Math.Max(0, total_File_Count - 1)
                    End If

                    If total_File_Count > 0 Then
                        ' Try the next file - but through the counted, posted path, not by
                        ' calling the display straight back. This is the SECOND shape of the
                        ' recursion fixed in RequestAutoSkipJump: it bypassed the chain guard
                        ' entirely (that lives in ReadShowMediaFile's is_Auto_Skip branch), so
                        ' a folder where every file's dispatch throws - VLC natives that will
                        ' not load, a path New Uri rejects - nested one frame set per file
                        ' with nothing to stop it.
                        pending_Jump_Target = current_File_Index
                        pending_Jump_Status = ""
                        RequestAutoSkipJump()
                    End If
                Else
                    lbl_Status.Text = Localization.TF("Файл {0} перемещается назад операционной системой.", Current_File_Name)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1080: UNdo E005 " & ex.Message)
                End If
            End Try

        Else
            StopGifLoopPlayback()
            ' Both surfaces AND both backing streams: disposing the Image while leaving it
            ' assigned left a disposed bitmap on the control, and the MemoryStream each
            ' image was decoded from stayed alive with nothing left to reach it. Emptying
            ' a folder is not rare - every "move the last file out" ends here.
            ReleasePictureBoxMedia(1)
            ReleasePictureBoxMedia(2)
            current_Loaded_File_Name = ""
#If NETFRAMEWORK Then
            Web_Browser.DocumentText = ""
#Else
            If is_Vlc_Playing Then StopVlcPlayback()
#End If

            lbl_File_Number.Text = ""
            lbl_Status.Text = Localization.T("! Нет файлов в папке")
            is_PictureBox1_Visible = False
            is_PictureBox2_Visible = False
            is_WebBrowser_Visible = False

            ' Same reason as ShowUnsupportedFormat: with every surface down, nothing
            ' below re-derives a background, so the emptied folder would still be
            ' showing the tint (and holding the bars) of the last file it had.
            ResetBackgroundToNeutral()

            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1090: No files in folder, all wiped")
        End If

        ' New media is on screen: clear/cancel any stale overlay and (in auto
        ' mode) schedule OCR for the freshly-shown image.
        OnMediaDisplayed()
    End Sub

End Class
