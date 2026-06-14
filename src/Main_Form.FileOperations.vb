Option Strict On

Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Windows.Forms

Partial Public Class Main_Form

    Private Sub InitializeFileOperationWorker()
        FileOperationWorker.WorkerSupportsCancellation = True
    End Sub

    Private Sub RenameCurrentFile()
        Try
            If Not My.Computer.FileSystem.FileExists(Current_File_Name) Then
                lbl_Status.Text = If(Is_Russian_Language, "! Файл не найден", "! File not found")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1250: file not found")
                Return
            End If
            Dim current_File_Name_Without_Extension As String = Path.GetFileNameWithoutExtension(Current_File_Name)
            Dim current_File_Extension As String = Path.GetExtension(Current_File_Name)
            Dim new_File_Name As String = InputBox(If(Is_Russian_Language, "Введите новое имя файла:", "Enter new file name:"),
                                            If(Is_Russian_Language, "Переименование файла", "Rename File"),
                                            current_File_Name_Without_Extension)
            If String.IsNullOrEmpty(new_File_Name) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1260: empty new file name - no rename")
                Return
            End If

            Dim current_Directory_Path As String = Path.GetDirectoryName(Current_File_Name)
            Dim new_File_Full_Path As String = Path.Combine(current_Directory_Path, new_File_Name & current_File_Extension)
            If new_File_Full_Path = Current_File_Name Then
                lbl_Status.Text = If(Is_Russian_Language, "! Имя не изменено", "! Name not changed")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1270: file is not new")
                Return
            End If

            RenameFile(Current_File_Name, new_File_Name & current_File_Extension)

            If is_Files_Array_Active Then
                files_Array(current_File_Index) = new_File_Full_Path
            Else
                files_List(current_File_Index) = new_File_Full_Path
            End If
            Current_File_Name = new_File_Full_Path
            lbl_Status.Text = If(Is_Russian_Language, "Файл переименован: " & new_File_Name & current_File_Extension, "File renamed: " & new_File_Name & current_File_Extension)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1280: file is renamed")

            ReadShowMediaFile("SetFile")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1290: ERR: " & ex.Message)
            MsgBox("E011 " & ex.Message)
            lbl_Status.Text = If(Is_Russian_Language, "! Ошибка переименования", "! Rename error")
        End Try
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles bt_Delete.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1650: bt_Delete")
        SlideShowStop()
        ReadShowMediaFile("DeleteFile")
    End Sub

    Private Sub PoMove(ByVal move_Slot_index As Integer)
        Dim destination_Folder_Path As String = Hardkeys_to_move_mediafile(move_Slot_index)
        Dim move_Slot_Key As String = move_Slot_index.ToString
        If move_Slot_Key = "10" Then move_Slot_Key = "0"

        If destination_Folder_Path = "" Then
            lbl_Status.Text = If(Is_Russian_Language, "! Нет каталога-получателя для клавиши " & move_Slot_Key, "! No dest folder set with key " & move_Slot_Key)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1680: No dest folder set with key " & move_Slot_Key)
        Else
            If Current_File_Name <> "" Then
                Try
                    Dim destination_Folder_Full_Path As String = destination_Folder_Path
                    Dim source_File_Info As System.IO.FileInfo
                    source_File_Info = My.Computer.FileSystem.GetFileInfo(Current_File_Name)
                    destination_Folder_Full_Path = destination_Folder_Full_Path & "\" & source_File_Info.Name
                    history_Source_File_Name = Current_File_Name
                    history_Destination_File_Name = destination_Folder_Full_Path

                    If Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                        If Is_Copying_not_Moving Then
                            current_File_Operation = "Copy"
                            current_File_Operation_Args = New String() {Current_File_Name, destination_Folder_Full_Path, move_Slot_Key}
                            lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. Файл копируется (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "!Wait.. File copying (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)
                            FileOperationWorker.RunWorkerAsync()

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1690: file is copied ASYNC to " & destination_Folder_Full_Path)

                            ReadShowMediaFile("ReadNextFile")
                        Else
                            current_File_Operation = "Move"
                            current_File_Operation_Args = New String() {Current_File_Name, destination_Folder_Full_Path, move_Slot_Key}

                            If is_Second_PictureBox_Active Then
                                If is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                    Picture_Box_2.Image?.Dispose()
                                    Picture_Box_2.Image = Nothing
                                End If
                            Else
                                If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                    Picture_Box_1.Image?.Dispose()
                                    Picture_Box_1.Image = Nothing
                                End If
                            End If

                            Web_Browser.DocumentText = ""

                            lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. Файл переносится (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "!Wait.. File moving (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1700: move async run")

                            FileOperationWorker.RunWorkerAsync()
                            If is_Files_Array_Active Then
                                files_Array = RemoveAt(files_Array, current_File_Index)
                            Else
                                files_List.RemoveAt(current_File_Index)
                            End If
                            total_File_Count -= 1
                            If current_File_Index > (total_File_Count - 1) Then current_File_Index = total_File_Count - 1

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1705: file is moved ASYNC to " & destination_Folder_Full_Path)

                            ReadShowMediaFile("SetFile")
                        End If
                    Else
                        If Is_Copying_not_Moving Then
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1710: copy run")

                            lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. Файл копируется (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "!Wait.. File copying (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)
                            CopyFile(Current_File_Name, destination_Folder_Full_Path)
                            lbl_Status.Text = If(Is_Russian_Language, "файл скопирован (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "file copied (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1715: file is copied to " & destination_Folder_Full_Path)

                            ReadShowMediaFile("ReadNextFile")
                        Else
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1720: move run")

                            If is_Second_PictureBox_Active Then
                                If is_PictureBox1_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                    Picture_Box_2.Image?.Dispose()
                                    Picture_Box_2.Image = Nothing
                                End If
                            Else
                                If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                    Picture_Box_1.Image?.Dispose()
                                    Picture_Box_1.Image = Nothing
                                End If
                            End If

                            Web_Browser.DocumentText = ""

                            lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. Файл переносится (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "!Wait.. File moving (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)

                            MoveFile(Current_File_Name, destination_Folder_Full_Path)

                            If is_Files_Array_Active Then
                                files_Array = RemoveAt(files_Array, current_File_Index)
                            Else
                                files_List.RemoveAt(current_File_Index)
                            End If
                            total_File_Count -= 1
                            If current_File_Index > (total_File_Count - 1) Then current_File_Index = total_File_Count - 1
                            lbl_Status.Text = If(Is_Russian_Language, "файл перенесён (" & move_Slot_Key & ") в каталог " & destination_Folder_Full_Path, "file moved (" & move_Slot_Key & ") to " & destination_Folder_Full_Path)

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1729: file is moved to " & destination_Folder_Full_Path)

                            ReadShowMediaFile("SetFile")
                        End If
                    End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1730: E014 " & ex.Message)
                    MsgBox("E014 " & ex.Message)
                End Try
            Else
                lbl_Status.Text = If(Is_Russian_Language, "! Нет файла ", "! No file")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1740: No file")
            End If
        End If
    End Sub

    Private Sub Undo()
        If history_Destination_File_Name <> "" Then
            If Is_Copying_not_Moving Then
                If Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1750: undo copied async deletion")

                    current_File_Operation = "DeleteUndo"
                    current_File_Operation_Args = history_Destination_File_Name
                    lbl_Status.Text = If(Is_Russian_Language, "!Ждите. Файл удаляется в каталоге " & history_Destination_File_Name, "!Wait. File deleting in " & history_Destination_File_Name)
                    FileOperationWorker.RunWorkerAsync()
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1760: undo copied deletion")
                    Try
                        DeleteFile(history_Destination_File_Name)
                        lbl_Status.Text = If(Is_Russian_Language, "файл удалён в каталоге " & history_Destination_File_Name, "file deleted in " & history_Destination_File_Name)
                        history_Destination_File_Name = ""
                        history_Source_File_Name = ""
                    Catch ex As Exception
                        MsgBox("E015 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1770: undo E015 " & ex.Message)
                    End Try
                End If
            Else
                If Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1780: undo move async deletion")

                    current_File_Operation = "MoveUndo"
                    current_File_Operation_Args = New String() {history_Destination_File_Name, history_Source_File_Name}
                    lbl_Status.Text = If(Is_Russian_Language, "!Ждите. Возвращается в каталог " & history_Source_File_Name, "!Wait. File back to " & history_Source_File_Name)
                    FileOperationWorker.RunWorkerAsync()
                    If is_Files_Array_Active Then
                        files_Array = AddAt(files_Array, history_Source_File_Name, current_File_Index)
                    Else
                        files_List.Insert(current_File_Index, history_Source_File_Name)
                    End If
                    total_File_Count += 1
                    ReadShowMediaFile("AfterUndo")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1790: undo move deletion")
                    Try
                        lbl_Status.Text = If(Is_Russian_Language, "!Ждите. Возвращается в каталог " & history_Source_File_Name, "!Wait. File back to " & history_Source_File_Name)

                        MoveFile(history_Destination_File_Name, history_Source_File_Name)

                        If is_Files_Array_Active Then
                            files_Array = AddAt(files_Array, history_Source_File_Name, current_File_Index)
                        Else
                            files_List.Insert(current_File_Index, history_Source_File_Name)
                        End If
                        total_File_Count += 1
                        lbl_Status.Text = If(Is_Russian_Language, "файл возвращён в каталог " & history_Source_File_Name, "file back to " & history_Source_File_Name)
                        ReadShowMediaFile("AfterUndo")
                        history_Destination_File_Name = ""
                        history_Source_File_Name = ""
                    Catch ex As Exception
                        MsgBox("E016 " & ex.Message)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1800: undo E016 " & ex.Message)
                    End Try
                End If
            End If
            Web_Browser.DocumentText = ""

        Else
            lbl_Status.Text = If(Is_Russian_Language, "! Нет истории о переносе", "! No history about moved files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1810: No history about moved files")
        End If
    End Sub

    Private Sub ButtonRename_Click(sender As Object, e As EventArgs) Handles btn_Rename.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2030: btn_Rename")

        If Not String.IsNullOrEmpty(Current_File_Name) Then
            RenameCurrentFile()
        Else
            lbl_Status.Text = If(Is_Russian_Language, "! Нет файла для переименования", "! No file to rename")
        End If
    End Sub

    Private Sub CopyFilePathToClipboard()
        If Not String.IsNullOrEmpty(Current_File_Name) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2120: Filename sent to clipboard")
            CopyTextToClipboard(Current_File_Name, lbl_Status, If(Is_Russian_Language, "Имя файла скопировано в буфер", "Filename sent to clipboard"))
        End If
    End Sub

    Private Sub FileOperationWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles FileOperationWorker.DoWork
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2160: FileOperationWorker_DoWork")

        Select Case current_File_Operation
            Case "Copy"
                Dim args As String() = DirectCast(current_File_Operation_Args, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)
                CopyFile(sourceFile, destFile)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2170: file copied")

            Case "Move"
                Dim args As String() = DirectCast(current_File_Operation_Args, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)

                MoveFile(sourceFile, destFile)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2180: file moved")

            Case "Delete"
                Dim filePath As String = DirectCast(current_File_Operation_Args, String)
                DeleteFile(filePath)
                history_Destination_File_Name = ""
                history_Source_File_Name = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2190: file deleted")

            Case "DeleteUndo"
                ' #todo: check undo from garbage bin
                Dim filePath As String = DirectCast(current_File_Operation_Args, String)
                DeleteFile(filePath)
                history_Destination_File_Name = ""
                history_Source_File_Name = ""
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2200: file deleted in undo")

            Case "MoveUndo"
                Dim args As String() = DirectCast(current_File_Operation_Args, String())
                Dim sourceFile As String = args(0)
                Dim destFile As String = args(1)

                MoveFile(sourceFile, destFile)

                history_Destination_File_Name = ""

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2210: file moved after undo")
            Case Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2215: !! FileOperationWorker_DoWork for nothing ")
        End Select
    End Sub

    Private Sub FileOperationWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles FileOperationWorker.RunWorkerCompleted
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2220: FileOperationWorker_RunWorkerCompleted")

        If e.Error Is Nothing Then
            Select Case current_File_Operation
                Case "Copy"
                    Dim args As String() = DirectCast(current_File_Operation_Args, String())
                    Dim textKey As String = args(2)
                    Dim destFile As String = args(1)
                    lbl_Status.Text = If(Is_Russian_Language, "файл скопирован (" & textKey & ") в каталог " & destFile, "file copied (" & textKey & ") to " & destFile)

                Case "Move"
                    Dim args As String() = DirectCast(current_File_Operation_Args, String())
                    Dim textKey As String = args(2)
                    Dim destFile As String = args(1)
                    lbl_Status.Text = If(Is_Russian_Language, "файл перенесён (" & textKey & ") в каталог " & destFile, "file moved (" & textKey & ") to " & destFile)

                Case "Delete"

                Case "DeleteUndo"
                    lbl_Status.Text = If(Is_Russian_Language, "файл удалён в каталоге " & history_Destination_File_Name, "file deleted in " & history_Destination_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""

                Case "MoveUndo"
                    lbl_Status.Text = If(Is_Russian_Language, "файл возвращён в каталог " & history_Source_File_Name, "file back to " & history_Source_File_Name)
                    history_Destination_File_Name = ""
                    history_Source_File_Name = ""
            End Select
        Else
            lbl_Status.Text = If(Is_Russian_Language, "Ошибка операции: " & e.Error.Message, "Operation error: " & e.Error.Message)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2230: FileOperationWorker_RunWorkerCompleted ERR " & e.Error.Message)
        End If
    End Sub

End Class
