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

    Private Sub BgWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BgWorker.DoWork
        Dim worker As BackgroundWorker = DirectCast(sender, BackgroundWorker)

        Dim file_Names_Pair As Tuple(Of String, String) = TryCast(e.Argument, Tuple(Of String, String))
        Dim current_File_Name_in_worker As String = Nothing
        Dim next_File_After_Current_in_worker As String = Nothing
        If file_Names_Pair IsNot Nothing Then
            current_File_Name_in_worker = file_Names_Pair.Item1
            next_File_After_Current_in_worker = file_Names_Pair.Item2
        End If

        Try
            If Is_No_Background_Tasks OrElse
            worker.CancellationPending Then

                e.Cancel = True
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0050: BgWorker got cancellation")
            End If

            If current_File_Name_in_worker = "" OrElse
                Not My.Computer.FileSystem.FileExists(current_File_Name_in_worker) Then

                lbl_Current_File.Text = ""
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0060: File is lost for BgWorker size calculation")
            Else
                Dim file_Meta_State As New Dictionary(Of String, String)

                file_Meta_State("fileName") = current_File_Name_in_worker

                If Is_to_show_file_sizes OrElse
                        Is_to_show_picture_sizes OrElse
                        Is_to_show_file_datetime Then

                    Dim current_File_Info = My.Computer.FileSystem.GetFileInfo(current_File_Name_in_worker)
                    If Is_to_show_file_sizes Then
                        Dim current_File_Size = current_File_Info.Length
                        Dim current_File_Size_Text As String

                        If current_File_Size < 1000 Then
                            current_File_Size_Text = current_File_Size.ToString & "B"
                        ElseIf current_File_Size / 1000 > 1000 Then
                            current_File_Size_Text = (current_File_Size / 1000000).ToString("F1") + "MiB"
                        Else
                            current_File_Size_Text = (current_File_Size / 1000).ToString("F1") + "KiB"
                        End If

                        file_Meta_State("fileSizeText") = current_File_Size_Text
                    End If

                    If Is_to_show_file_datetime Then
                        file_Meta_State("fileTimeText") = current_File_Info.LastWriteTime.ToString("yyMMdd HH:mm")
                    End If

                    If Is_to_show_picture_sizes Then
                        Dim fileExtension As String = current_File_Info.Extension.ToLower()
                        If Image_File_Extensions.Contains(fileExtension) Then
                            Try
                                Using img As Image = Image.FromFile(Current_File_Name)
                                    file_Meta_State("imageWidth") = img.Width.ToString()
                                    file_Meta_State("imageHeight") = img.Height.ToString()
                                End Using
                            Catch ex As Exception
                                file_Meta_State("imageWidth") = "?"
                                file_Meta_State("imageHeight") = "?"
                            End Try
                        End If
                    End If
                End If

                DirectCast(sender, BackgroundWorker).ReportProgress(0, file_Meta_State)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0070: BgWorker reported file info")
            End If

            If was_External_Input_Previously Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0080: folder files going be counted on background..")
                Dim background_Total_File_Count As Integer = My.Computer.FileSystem.GetDirectoryInfo(Current_Folder_Path).EnumerateFiles.Count

                Dim folder_File_Count_State As New Dictionary(Of String, String)
                folder_File_Count_State("totalFilesCountText") = background_Total_File_Count.ToString
                folder_File_Count_State("updateTotalFileCount") = background_Total_File_Count.ToString
                DirectCast(sender, BackgroundWorker).ReportProgress(0, folder_File_Count_State)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0090: folder files: " & background_Total_File_Count)
            End If

            If Not is_Slide_Show_Random_Mode AndAlso
                Not next_File_After_Current_in_worker = "" AndAlso
                Not next_File_After_Current_in_worker = current_File_Name_in_worker Then

                Dim SecondFileExtension = Path.GetExtension(next_File_After_Current_in_worker).ToLower

                If Image_File_Extensions.Contains(SecondFileExtension) Then
                    ' sza250609 - GIF fix
                    Dim next_Image_Data As Tuple(Of Image, IO.MemoryStream) = LoadImageWithStream(next_File_After_Current_in_worker)
                    If next_Image_Data IsNot Nothing Then
                        current_Second_File_Name = next_File_After_Current_in_worker
                        e.Result = New Tuple(Of Image, IO.MemoryStream, Boolean)(next_Image_Data.Item1, next_Image_Data.Item2, is_First_Picture_Box_Need_To_Be_Cached)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0100: BgWorker loaded image into memory: " & next_File_After_Current_in_worker.ToString)
                    Else
                        e.Cancel = True
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0110: Next file is not image, backload is cancelled")
                    e.Cancel = True
                End If
            Else
                current_Second_File_Name = ""
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0120: No needs for the Next file, backload is cancelled; isSlideShowRandom " & is_Slide_Show_Random_Mode.ToString & " nextAfterCurrentFileName = " & next_File_After_Current_in_worker)
                e.Cancel = True
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0041: ERR BCK! " & ex.Message)
        End Try
    End Sub

    Private Sub BgWorker_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles BgWorker.ProgressChanged
        Dim file_Meta_State As Dictionary(Of String, String) = DirectCast(e.UserState, Dictionary(Of String, String))

        If file_Meta_State.ContainsKey("fileName") Then

            Dim current_File_Display_Text = file_Meta_State("fileName")

            If Is_to_show_file_datetime AndAlso
                    file_Meta_State.ContainsKey("fileTimeText") Then

                Dim file_DateTime_Text As String = file_Meta_State("fileTimeText")

                If Not file_DateTime_Text = Nothing Then
                    current_File_Display_Text = current_File_Display_Text & " (" & file_DateTime_Text & ")"
                End If
            End If

            If Is_to_show_picture_sizes AndAlso
                file_Meta_State.ContainsKey("imageWidth") Then

                Dim image_Width_Text As String = file_Meta_State("imageWidth")

                If Not image_Width_Text = Nothing Then
                    current_File_Display_Text = current_File_Display_Text & " (" & image_Width_Text & "x" & file_Meta_State("imageHeight") & ")"
                End If
            End If

            If Is_to_show_file_sizes AndAlso
                        file_Meta_State.ContainsKey("fileSizeText") Then

                Dim file_Size_Text As String = file_Meta_State("fileSizeText")

                If Not file_Size_Text = Nothing Then
                    current_File_Display_Text = current_File_Display_Text & " " & file_Size_Text
                End If
            End If

            lbl_Current_File.Text = current_File_Display_Text
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0170: BgWorker size and time calculated")

        ElseIf file_Meta_State.ContainsKey("totalFilesCountText") Then
            total_Files_Count_Text = file_Meta_State("totalFilesCountText")

            If Not total_Files_Count_Text = Nothing Then
                lbl_File_Number.Text = If(Is_Russian_Language, "1 из " & total_Files_Count_Text, "1 from " & total_Files_Count_Text)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0175: BgWorker files count calculated: " & total_Files_Count_Text)
            Else
                lbl_File_Number.Text = "0 "
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0180: BgWorker files count calculated: " & total_Files_Count_Text)
            End If

            ' Update total_File_Count on UI thread if provided
            If file_Meta_State.ContainsKey("updateTotalFileCount") Then
                Dim newTotalCount As String = file_Meta_State("updateTotalFileCount")
                Dim newCount As Integer
                If Integer.TryParse(newTotalCount, newCount) Then
                    total_File_Count = newCount
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0185: total_File_Count updated on UI thread: " & total_File_Count)
                End If
            End If
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0190: BgWorker reported wrong progress!")
        End If

    End Sub

    Private Sub BgWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles BgWorker.RunWorkerCompleted
        is_BgWorker_Online = False

        ' Check for cancellation or error BEFORE accessing e.Result
        If e.Cancelled Then
            bgWorker_Result = "CANCELLED"
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0201: BgWorker cancelled")
        ElseIf e.Error IsNot Nothing Then
            bgWorker_Result = "ERR: " & e.Error.Message
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0205: BgWorker error: " & e.Error.Message)
        ElseIf e.Result IsNot Nothing Then
            ' Only access e.Result if operation completed successfully
            Try
                Dim result As Tuple(Of Image, IO.MemoryStream, Boolean) = DirectCast(e.Result, Tuple(Of Image, IO.MemoryStream, Boolean))

                If current_Second_File_Name = "" Then
                    ' No second file - dispose resources
                    result.Item1?.Dispose()
                    result.Item2?.Dispose()
                    bgWorker_Result = "SKIPED"
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0207: BgWorker skipped - resources disposed")
                Else
                    ' Success - transfer ownership to UI controls
                    Dim next_Image_To_Display As Image = result.Item1
                    Dim next_Image_Stream As IO.MemoryStream = result.Item2
                    Dim is_PictureBox1_Active As Boolean = result.Item3

                    If is_PictureBox1_Active Then
                        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                        If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream?.Dispose()
                        Picture_Box_1.Image = next_Image_To_Display
                        pictureBox1_Stream = next_Image_Stream

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0210: bgWorker: P1 is loaded")
                    Else
                        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                        If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream?.Dispose()
                        Picture_Box_2.Image = next_Image_To_Display
                        pictureBox2_Stream = next_Image_Stream

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0220: bgWorker: P2 is loaded")
                    End If

                    bgWorker_Result = "LOADED"
                End If
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0203: Error handling BgWorker result: " & ex.Message)
                bgWorker_Result = "ERR: " & ex.Message
            End Try
        Else
            ' Completed successfully but no result
            bgWorker_Result = "SKIPED"
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0208: BgWorker completed with no result")
        End If

        ' Check if there's a pending operation to start
        If bgWorker_Has_Pending_Operation AndAlso bgWorker_Pending_Args IsNot Nothing Then
            bgWorker_Has_Pending_Operation = False
            Dim pending_Args As Tuple(Of String, String) = bgWorker_Pending_Args
            bgWorker_Pending_Args = Nothing

            ' Start the pending operation
            If Not Is_No_Background_Tasks Then
                is_BgWorker_Online = True
                BgWorker.RunWorkerAsync(pending_Args)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0235: BgWorker started pending operation")
            End If
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0230: bgWorkerResult: " & bgWorker_Result)
    End Sub

    Private Structure FileEntry
        Public Property FilePath As String
        Public Property FileSize As Long
        Public Property FileName As String
        Public Property FileDate As Date
    End Structure

    Private Function GetFiles() As Object
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1095: GetFiles..")

        Try
            Dim current_Directory_Info As DirectoryInfo = My.Computer.FileSystem.GetDirectoryInfo(Current_Folder_Path)
            Dim file_Entry_List As List(Of FileEntry) = current_Directory_Info.EnumerateFiles() _
            .Where(Function(f) all_Supported_Extensions.Contains(f.Extension.ToLower())) _
            .Select(Function(f) New FileEntry With {
                .FilePath = f.FullName,
                .FileSize = f.Length,
                .FileName = f.Name,
                .FileDate = f.LastWriteTime
            }).ToList()

            If file_Entry_List.Count = 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1096: Files count=0")
                lbl_Status.Text = If(Is_Russian_Language, "Папка пустая", "Folder is empty")
                Return Nothing
            End If

            If file_Entry_List.Count < max_Number_Of_Files_For_List Then
                is_Files_Array_Active = False
                files_Array = Nothing ' Clear array when using list

                Dim orderedEntries As IEnumerable(Of FileEntry)
                Select Case cmbox_Sort.SelectedItem?.ToString()
                    Case "abc"
                        orderedEntries = file_Entry_List.OrderBy(Function(f) f.FileName)
                    Case "xyz"
                        orderedEntries = file_Entry_List.OrderByDescending(Function(f) f.FileName)
                    Case "rnd"
                        orderedEntries = file_Entry_List.OrderBy(Function(f) Guid.NewGuid())
                    Case ">size"
                        orderedEntries = file_Entry_List.OrderByDescending(Function(f) f.FileSize)
                    Case "<size"
                        orderedEntries = file_Entry_List.OrderBy(Function(f) f.FileSize)
                    Case ">time"
                        orderedEntries = file_Entry_List.OrderByDescending(Function(f) f.FileDate)
                    Case "<time"
                        orderedEntries = file_Entry_List.OrderBy(Function(f) f.FileDate)
                    Case "<0123"
                        orderedEntries = file_Entry_List.OrderBy(Function(f) f.FileName, New NaturalFilenameComparer())
                    Case ">3210"
                        orderedEntries = file_Entry_List.OrderByDescending(Function(f) f.FileName, New NaturalFilenameComparer())
                    Case Else
                        orderedEntries = file_Entry_List.OrderBy(Function(f) f.FilePath)
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1107:  sort is lost?!")
                End Select

                Dim file_Paths_List As List(Of String) = orderedEntries.Select(Function(f) f.FilePath).ToList()
                Return file_Paths_List
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1109:  too mant files - just array, no sorting !")
                is_Files_Array_Active = True
                files_List = Nothing ' Clear list when using array
                Return file_Entry_List.ToArray()
            End If

        Catch ex As Exception
            lbl_Status.Text = If(Is_Russian_Language, "! Ошибка чтения файлов", "! Error reading files")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1110: Error reading files: " & ex.Message)
            Return Nothing
        End Try
    End Function

End Class
