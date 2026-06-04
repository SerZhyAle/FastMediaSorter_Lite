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

    Private Sub ReadShowMediaFile(ByVal read_Mode_Type As String)

        media_View_Count += 1

        If Not is_Folder_Read_Required Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0050: ReadShowMediaFile = " & read_Mode_Type.ToString)

            Dim current_Operation_Time As DateTime = DateTime.Now
            If last_Action_Time.AddSeconds(minimum_time_before_next_media_file) > current_Operation_Time Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0330: Try to read the new file less than 0.4s - cancelled")
                Exit Sub
            End If
            last_Action_Time = current_Operation_Time

            If FileOperationWorker.IsBusy Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0340: Read file skiped while FileOperationWorker")
                Exit Sub
            End If

            Dim slideshow_Interval_Text = If(Is_slide_show_mode, (SlideShowTimer.Interval / 1000).ToString() & "s", "")
            If Not lbl_Slideshow_Time.Text = slideshow_Interval_Text Then lbl_Slideshow_Time.Text = slideshow_Interval_Text

            Dim is_After_Undo_Operation As Boolean = (read_Mode_Type = "ReadAfterUndo")
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
                    If recent_Folder_List.Count > max_Namber_of_Recent_Folders Then
                        recent_Folder_List.RemoveAt(recent_Folder_List.Count - 1)
                    End If
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

        Select Case read_Mode_Type
            Case "ReadNextFile" ' 1
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

            Case "ReadFiles" '80
                If Not LoadFiles() Then Return False
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0430: case ReadFiles")

            Case "SetFile" '99
                If current_File_Index < 0 Then current_File_Index = 0
                If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0440: case SetFile")

            Case "InSlideShow" '0
                If total_File_Count <= 1 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0455: case InSlideShow but total_File_Count is 0")
                    SlideShowStop()
                    Return False
                End If

                If is_Slide_Show_Random_Mode Then
                    current_File_Index = CInt(Math.Floor(Rnd() * total_File_Count))
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0460: case RND InSlideShow")
                Else
                    current_File_Index += 1
                    If current_File_Index < 0 Then current_File_Index = 0
                    If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0461: case InSlideShow")
                End If


            Case "ReadFolderAndFile" '0
                lbl_Status.Text = If(Is_Russian_Language, "чтение каталога.. ждите!", "reading files.. wait!")

                If Not LoadFiles() Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0450: case ReadFolderAndFile is failed")
                    Return False
                End If
                lbl_Status.Text = ""
                current_File_Index = 0

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0460: case ReadFolderAndFile")

            Case "ReadFolderAndKnownFile" '91
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

            Case "ReadPrevFile" '2
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

            Case "DeleteFile" '3
                If String.IsNullOrEmpty(Current_File_Name) Then
                    lbl_Status.Text = If(Is_Russian_Language, "! Нет файла для удаления", "! No file for deleting")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0540: case DeleteFile failed")
                    Return False
                End If

                Dim confirmMsg = If(Is_Russian_Language, $"Вы уверены, что хотите безвозвратно удалить файл '{Path.GetFileName(Current_File_Name)}'?", $"Are you sure you want to permanently delete the file '{Path.GetFileName(Current_File_Name)}'?")

                If Not Is_no_request_before_file_operation AndAlso
                    MessageBox.Show(confirmMsg, If(Is_Russian_Language, "Подтверждение удаления", "Deletion Confirmation"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then

                    Return False ' User cancelled
                End If

                Try
                    If is_WebBrowser_Visible Then
                        Web_Browser.DocumentText = ""
                    Else
                        If is_PictureBox1_Visible Then
                            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                        Else
                            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                        End If
                    End If

                    current_Loaded_File_Name = ""

                    If My.Computer.FileSystem.FileExists(Current_File_Name) Then
                        If Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked Then
                            current_File_Operation = "Delete"
                            current_File_Operation_Args = Current_File_Name
                            FileOperationWorker.RunWorkerAsync()
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0550: file in task to be deleted: " & Current_File_Name)
                            If is_Files_Array_Active Then
                                files_Array = RemoveAt(files_Array, current_File_Index)
                            Else
                                files_List.RemoveAt(current_File_Index)
                            End If
                            total_File_Count -= 1
                            If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                            lbl_Status.Text = If(Is_Russian_Language, "удален: ", "file deleted: ") & Current_File_Name
                        Else
                            DeleteFile(Current_File_Name)
                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0560: file deleted: " & Current_File_Name)
                            If is_Files_Array_Active Then
                                files_Array = RemoveAt(files_Array, current_File_Index)
                            Else
                                files_List.RemoveAt(current_File_Index)
                            End If
                            total_File_Count -= 1
                            If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
                            lbl_Status.Text = If(Is_Russian_Language, "удален: ", "file deleted: ") & Current_File_Name
                        End If
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0570: case DeleteFile")
                    Else
                        lbl_Status.Text = If(Is_Russian_Language, "! Файл не найден", "! File not found")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0580: case DeleteFile failed: not found")
                    End If
                Catch ex As Exception
                    MsgBox("E001 " & ex.Message)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0590: ERR: " & ex.Message)
                End Try

            Case "ReadForRandom" '4
                If Not LoadFilesForRandomOrSlideshow(is_File_Found, True) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0600: case ReadForRandomOrSlideshow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0610: case ReadForRandomOrSlideshow")

            Case "ReadForSlideShow" '5
                If Not LoadFilesForRandomOrSlideshow(is_File_Found, is_Slide_Show_Random_Mode) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0620: case ReadForSlideShow failed")
                    Return False
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0630: case ReadForSlideShow")

            Case "AfterUndo" '98
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0640: case AfterUndo")
        End Select

        Return True
    End Function

    Private Function LoadFilesForRandomOrSlideshow(ByRef is_File_Found As Boolean, is_Random_File_Mode As Boolean) As Boolean
        Try
            If current_File_Index = 0 Then
                was_External_Input_Previously = False
                lbl_Status.Text = If(Is_Russian_Language, "чтение каталога.. ждите!", "reading files.. wait!")
                Dim files As Object = GetFiles()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0642: files got for slideshow")

                If files Is Nothing Then
                    Current_Folder_Path = ""
                    cmbox_Media_Folder.Text = ""
                    total_File_Count = 0
                    current_File_Index = 0
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
                current_File_Index = 0
                If total_File_Count <> 0 Then
                    If is_Random_File_Mode Then
                        Dim random As New Random
                        current_File_Index = random.Next(0, total_File_Count)
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
                    Dim random As New Random
                    current_File_Index = random.Next(0, total_File_Count)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0690: random file set")
                Else
                    current_File_Index += 1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0700: slide file set")
                End If
            End If
            Return True
        Catch ex As Exception
            MsgBox("E002 " & ex.Message)
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
                lbl_Status.Text = If(Is_Russian_Language, "чтение каталога.. ждите!", "reading files.. wait!")

                Dim files As Object = GetFiles()
                If files Is Nothing Then
                    'lbl_Status.Text = If(lngRus, "! Ошибка чтения файлов", "! Error reading files")
                    Current_Folder_Path = ""
                    cmbox_Media_Folder.Text = ""
                    total_File_Count = 0
                    current_File_Index = 0
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
            MsgBox("E003 " & ex.Message)
            Current_Folder_Path = ""
            cmbox_Media_Folder.Text = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0760: E003 " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LoadFiles() As Boolean
        Try
            Dim files As Object = GetFiles()
            If files Is Nothing Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0770: files arnt set")
                lbl_Status.Text = If(Is_Russian_Language, "! Ошибка чтения файлов", "! Error reading files")
                Current_Folder_Path = ""
                cmbox_Media_Folder.Text = ""
                total_File_Count = 0
                current_File_Index = 0

                Return False
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

            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0800: E004 " & ex.Message)
            lbl_Status.Text = If(Is_Russian_Language, "! Ошибка чтения файлов", "! Error reading files")
            MsgBox("E004 " & ex.Message)
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

            If bgWorker_Result = "LOADED" AndAlso
            current_Second_File_Name = Current_File_Name Then

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
                        lbl_Status.Text = If(Is_Russian_Language, "Файл не найден: " & Path.GetFileName(Current_File_Name), "File not found: " & Path.GetFileName(Current_File_Name))

                        ' Skip to next file if current file doesn't exist
                        ReadShowMediaFile("ReadNextFile")
                        Return
                    End If

                    ' Verify file is not empty
                    Dim fileInfo As New FileInfo(Current_File_Name)
                    If fileInfo.Length = 0 Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0907: File is empty: " & Current_File_Name)
                        lbl_Status.Text = If(Is_Russian_Language, "Файл пуст: " & Path.GetFileName(Current_File_Name), "File is empty: " & Path.GetFileName(Current_File_Name))

                        ' Skip to next file if current file is empty
                        ReadShowMediaFile("ReadNextFile")
                        Return
                    End If

                    ' sza250609 - GIF fix
                    Dim image_Data_Tuple As Tuple(Of Image, IO.MemoryStream) = LoadImageWithStream(Current_File_Name)

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
                        lbl_Status.Text = If(Is_Russian_Language, "Не удалось загрузить: " & Path.GetFileName(Current_File_Name), "Failed to load: " & Path.GetFileName(Current_File_Name))

                        ' Try to move to next file automatically
                        ReadShowMediaFile("ReadNextFile")
                        Return
                    End If
                Catch ex As ArgumentException
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0905: ArgumentException loading image: " & ex.Message & " File: " & Current_File_Name)
                    lbl_Status.Text = If(Is_Russian_Language, "Недопустимый файл изображения: " & Path.GetFileName(Current_File_Name), "Invalid image file: " & Path.GetFileName(Current_File_Name))

                    ' Skip to next file if image is invalid
                    ReadShowMediaFile("ReadNextFile")
                    Return
                Catch ex As OutOfMemoryException
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0909: OutOfMemoryException loading image: " & ex.Message & " File: " & Current_File_Name)
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно памяти для загрузки: " & Path.GetFileName(Current_File_Name), "Out of memory loading: " & Path.GetFileName(Current_File_Name))

                    ' Skip to next file if out of memory
                    ReadShowMediaFile("ReadNextFile")
                    Return
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0911: Error loading image: " & ex.Message & " File: " & Current_File_Name)
                    lbl_Status.Text = If(Is_Russian_Language, "Ошибка загрузки: " & Path.GetFileName(Current_File_Name), "Loading error: " & Path.GetFileName(Current_File_Name))

                    ' Skip to next file if any other error occurs
                    ReadShowMediaFile("ReadNextFile")
                    Return
                End Try
            End If
            current_Loaded_File_Name = Current_File_Name

            ' Final visibility update
            UpdateControlVisibility()

            If is_form_shown Then Draw_Perspective()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0920: file is a same, pic set is skipped")
        End If

        If Not Web_Browser.DocumentText = "" Then
            Web_Browser.DocumentText = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0940: WB blank")
        End If

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
        SlideShowTimer.Interval > slideshow_limit_to_change_color) Then

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


            If back_Color <> last_Back_Color Then
                last_Back_Color = back_Color

                Me.BackColor = back_Color

                Dim OppositeColor = GetOppositeColor(back_Color)
                If panel_Media IsNot Nothing Then panel_Media.BackColor = back_Color
                RecolorChrome(back_Color, OppositeColor)

                If is_PictureBox1_Visible Then
                    Picture_Box_1.BackColor = back_Color
                ElseIf is_PictureBox2_Visible Then
                    Picture_Box_2.BackColor = back_Color
                End If

            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0945: picture box sizes: " & If(is_PictureBox1_Visible, "P1: ", "P2: ") & If(is_PictureBox1_Visible, Picture_Box_1.Width.ToString, Picture_Box_2.Width.ToString) & "x" & If(is_PictureBox1_Visible, Picture_Box_1.Height.ToString, Picture_Box_2.Height.ToString))
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0950: Visibility set: " & If(is_PictureBox1_Visible, "P1-YES ", "P1-NO ") & If(is_PictureBox2_Visible, "P2-YES ", "P2-NO ") & If(is_WebBrowser_Visible, "WB-YES ", "WB-NO "))
    End Sub

    Private Sub UpdateCurrentFileAndDisplay(is_File_Found As Boolean, is_After_Undo_Operation As Boolean)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0381: UpdateCurrentFileAndDisplay, currentFileName: " & Current_File_Name)

        Dim previous_File_Name As String = Current_File_Name
        Current_File_Name = ""
        current_Loaded_File_Name = "" ' Clear this to force reload

        ' Check if file collections are properly initialized
        If files_List Is Nothing And files_Array Is Nothing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0385: Both files_List and files_Array are Nothing")
            lbl_Status.Text = If(Is_Russian_Language, "! Нет списка файлов", "! No file list available")
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
                    lbl_Status.Text = If(Is_Russian_Language, "Ошибка получения имени файла", "Error getting file name")
                    Return
                End Try

                If Not String.IsNullOrEmpty(Current_File_Name) AndAlso Not File.Exists(Current_File_Name) Then

                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0975: New current file does not exist: " & Current_File_Name)
                    lbl_Status.Text = If(Is_Russian_Language, "Файл не найден, переход к следующему", "File not found, moving to next")

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
                If Current_Image_Path Is Nothing Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0972: targetImagePath Is Nothing")
                    current_File_Index = 0
                    Current_File_Name = If(is_Files_Array_Active, files_Array(current_File_Index), files_List(current_File_Index))
                    Current_Image_Path = Current_File_Name
                Else
                    Current_File_Name = Current_Image_Path
                End If
            End If

            If Not String.IsNullOrEmpty(Current_File_Name) Then
                recent_Media_File_List.Remove(Current_File_Name)
                recent_Media_File_List.Add(Current_File_Name)
                If recent_Media_File_List.Count > max_Number_Of_Recent_Media_Files Then
                    recent_Media_File_List.RemoveAt(0)
                End If
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0980: currentFileName = " & Current_File_Name)

            Dim current_File_Number As Integer = current_File_Index + 1
            lbl_File_Number.Text = current_File_Number.ToString() & If(Is_Russian_Language, " из ", " from ") & total_File_Count.ToString()

            Try
                Dim current_File_Extension As String = Path.GetExtension(Current_File_Name).ToLower()
                Dim current_File_Uri As String = New Uri(Current_File_Name).ToString()

                If Image_File_Extensions.Contains(current_File_Extension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1030: P to load")
                    LoadStandardImageInPictureBox()
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1040: Picture box is set")
                ElseIf video_File_Extensions.Contains(current_File_Extension) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1010: WB to load")
                    LoadVideoInWebBrowser(current_File_Uri)
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1020: WB is set")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1045: No selected control to show!?")
                End If

                is_First_Picture_Box_Need_To_Be_Cached = is_Second_PictureBox_Active

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
                    Dim new_Args As New Tuple(Of String, String)(Current_File_Name, next_File_After_Current)

                    If is_BgWorker_Online OrElse BgWorker.IsBusy Then
                        ' Store the pending operation instead of canceling
                        bgWorker_Pending_Args = new_Args
                        bgWorker_Has_Pending_Operation = True
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1050: BgWorker operation queued")
                    Else
                        ' Start the operation immediately
                        is_BgWorker_Online = True
                        BgWorker.RunWorkerAsync(new_Args)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1060: BgWorker is run")
                    End If
                Else
                    lbl_Current_File.Text = If(Is_Russian_Language, "Текущий: ", "Current: ") & Current_File_Name
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1065: BgWorker is not run, online=" & is_BgWorker_Online.ToString & " IsBusy=" & BgWorker.IsBusy.ToString)
                End If

            Catch ex As Exception
                If Not is_After_Undo_Operation Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1070: E005 " & ex.Message & " File: " & Current_File_Name)

                    ' Instead of showing error, try to skip to next file
                    lbl_Status.Text = If(Is_Russian_Language, "Ошибка файла, переход к следующему: " & Path.GetFileName(Current_File_Name), "File error, moving to next: " & Path.GetFileName(Current_File_Name))

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
                        ' Recursively try the next file
                        UpdateCurrentFileAndDisplay(True, False)
                    End If
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Файл " & Current_File_Name & " перемещается назад операционной системой.", "File " & Current_File_Name & " moving back by OS.")
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1080: UNdo E005 " & ex.Message)
                End If
            End Try

        Else
            StopGifLoopPlayback()
            If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
            If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
            current_Loaded_File_Name = ""
            Web_Browser.DocumentText = ""

            lbl_File_Number.Text = ""
            lbl_Status.Text = If(Is_Russian_Language, "! Нет файлов в папке", "! No files in folder")
            is_PictureBox1_Visible = False
            is_PictureBox2_Visible = False
            is_WebBrowser_Visible = False

            UpdateControlVisibility()

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1090: No files in folder, all wiped")
        End If
    End Sub

End Class
