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

    ''' <summary>Everything the prefetch worker needs, as a snapshot taken on the UI
    ''' thread. The worker used to read live fields instead (which folder, which picture
    ''' box) and write shared ones back, so a flip in mid-decode could land the finished
    ''' image in the box the user was looking at.</summary>
    Private NotInheritable Class PrefetchRequest
        Public Property CurrentFile As String = ""
        Public Property NextFile As String = ""
        Public Property FolderPath As String = ""
        ''' <summary>Which box this decode is FOR, decided when the job was queued.</summary>
        Public Property TargetIsBox1 As Boolean
        Public Property CountFolder As Boolean
        Public Property IsRandomMode As Boolean
    End Class

    ''' <summary>What the worker decoded, and what it was decoded for.</summary>
    Private NotInheritable Class PrefetchResult
        Public Property NextFile As String = ""
        Public Property TargetIsBox1 As Boolean
        Public Property Picture As Image
        Public Property Data As IO.MemoryStream
    End Class

    Private Sub BgWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles BgWorker.DoWork
        Dim worker As BackgroundWorker = DirectCast(sender, BackgroundWorker)

        Dim request As PrefetchRequest = TryCast(e.Argument, PrefetchRequest)
        If request Is Nothing Then
            e.Cancel = True
            bgworker_Done.Set()
            Return
        End If
        Dim current_File_Name_in_worker As String = request.CurrentFile
        Dim next_File_After_Current_in_worker As String = request.NextFile

        Try
            If Is_No_Background_Tasks OrElse
            worker.CancellationPending Then

                e.Cancel = True
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0050: BgWorker got cancellation")
                ' Actually stop. Setting the flag and carrying on regardless meant a
                ' cancelled worker kept pulling an image off a slow share, and the
                ' shutdown path waited for it in vain.
                Return
            End If

            If current_File_Name_in_worker = "" OrElse
                Not My.Computer.FileSystem.FileExists(current_File_Name_in_worker) Then

                ' Through ReportProgress: touching the label straight from the pool
                ' thread threw InvalidOperationException, the catch-all below swallowed
                ' it, and the whole prefetch went down with it - on exactly the path
                ' (the file vanished from under us) where it is needed most.
                worker.ReportProgress(0, New Dictionary(Of String, String) From {{"clearCurrentFile", "1"}})
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

                        ' KiB/MiB are powers of 1024 by definition; the maths divided by
                        ' 1000, so a 10 MiB file read "10.5MiB" - number and unit from
                        ' two different systems, matching neither Explorer nor decimal MB.
                        If current_File_Size < 1024 Then
                            current_File_Size_Text = current_File_Size.ToString & "B"
                        ElseIf current_File_Size >= 1024L * 1024L Then
                            current_File_Size_Text = (current_File_Size / (1024.0 * 1024.0)).ToString("F1") + "MiB"
                        Else
                            current_File_Size_Text = (current_File_Size / 1024.0).ToString("F1") + "KiB"
                        End If

                        file_Meta_State("fileSizeText") = current_File_Size_Text
                    End If

                    If Is_to_show_file_datetime Then
                        file_Meta_State("fileTimeText") = current_File_Info.LastWriteTime.ToString("yyMMdd HH:mm")
                    End If

                    If Is_to_show_picture_sizes Then
                        Dim fileExtension As String = current_File_Info.Extension.ToLower()
                        If Image_File_Extensions.Contains(fileExtension) Then
                            ' Header-only read (no GDI+) on the worker-local file. Using
                            ' Image.FromFile(Current_File_Name) here was a double bug: it
                            ' read the shared global instead of this worker's file, and it
                            ' decoded via GDI+ on a background thread concurrently with the
                            ' UI thread's GetPixel, corrupting GDI+ state on slow shares.
                            Dim dims As Size = GetImageDimensions(current_File_Name_in_worker)
                            If Not dims.IsEmpty Then
                                file_Meta_State("imageWidth") = dims.Width.ToString()
                                file_Meta_State("imageHeight") = dims.Height.ToString()
                            Else
                                file_Meta_State("imageWidth") = "?"
                                file_Meta_State("imageHeight") = "?"
                            End If
                        End If
                    End If
                End If

                DirectCast(sender, BackgroundWorker).ReportProgress(0, file_Meta_State)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0070: BgWorker reported file info")
            End If

            If request.CountFolder Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0080: folder files going be counted on background..")
                ' Same filter GetFiles uses. A raw EnumerateFiles.Count counts the txt
                ' and zip files too, and that number was then written over
                ' total_File_Count - so "1 из 150" became "2 из 100" on the next flip,
                ' and End walked off the end of the real list.
                Dim background_Total_File_Count As Integer = EnumerateConfiguredFiles(request.FolderPath).
                    Count(Function(f) all_Supported_Extensions.Contains(f.Extension.ToLower()))

                Dim folder_File_Count_State As New Dictionary(Of String, String)
                folder_File_Count_State("totalFilesCountText") = background_Total_File_Count.ToString
                DirectCast(sender, BackgroundWorker).ReportProgress(0, folder_File_Count_State)

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0090: folder files: " & background_Total_File_Count)
            End If

            If Not request.IsRandomMode AndAlso
                Not next_File_After_Current_in_worker = "" AndAlso
                Not next_File_After_Current_in_worker = current_File_Name_in_worker Then

                Dim SecondFileExtension = Path.GetExtension(next_File_After_Current_in_worker).ToLower

                If Image_File_Extensions.Contains(SecondFileExtension) Then
                    ' sza250609 - GIF fix
                    Dim next_Image_Data As Tuple(Of Image, IO.MemoryStream) = LoadImageWithStream(next_File_After_Current_in_worker)
                    If next_Image_Data IsNot Nothing Then
                        ' Everything the completion needs travels in the result - the
                        ' worker no longer writes current_Second_File_Name (read by the
                        ' UI thread to decide "prefetch ready") nor reads the live
                        ' target-box flag, which by now could mean the visible box.
                        e.Result = New PrefetchResult With {
                            .NextFile = next_File_After_Current_in_worker,
                            .TargetIsBox1 = request.TargetIsBox1,
                            .Picture = next_Image_Data.Item1,
                            .Data = next_Image_Data.Item2
                        }
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0100: BgWorker loaded image into memory: " & next_File_After_Current_in_worker.ToString)
                    Else
                        e.Cancel = True
                    End If
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0110: Next file is not image, backload is cancelled")
                    e.Cancel = True
                End If
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0120: No needs for the Next file, backload is cancelled; isSlideShowRandom " & request.IsRandomMode.ToString & " nextAfterCurrentFileName = " & next_File_After_Current_in_worker)
                e.Cancel = True
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0041: ERR BCK! " & ex.Message)
        Finally
            ' Whoever is waiting to close needs to know we have actually left.
            bgworker_Done.Set()
        End Try
    End Sub

    Private Sub BgWorker_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles BgWorker.ProgressChanged
        Dim file_Meta_State As Dictionary(Of String, String) = DirectCast(e.UserState, Dictionary(Of String, String))

        If file_Meta_State.ContainsKey("clearCurrentFile") Then
            lbl_Current_File.Text = ""

        ElseIf file_Meta_State.ContainsKey("fileName") Then

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
                ' The real position, not a hard-coded "1". And the LABEL only: writing
                ' this count into total_File_Count left the counter describing a folder
                ' while files_List still held the single externally-opened file - End
                ' then indexed past the list and DEL emptied it outright. The real list
                ' arrives on the next flip, through LoadFilesForExternalInput.
                Dim shown_Number As Integer = current_File_Index + 1
                lbl_File_Number.Text = Localization.TF("{0} из {1}", shown_Number, total_Files_Count_Text)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0175: BgWorker files count calculated: " & total_Files_Count_Text)
            Else
                lbl_File_Number.Text = "0 "
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0180: BgWorker files count calculated: " & total_Files_Count_Text)
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
            current_Second_File_Name = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0201: BgWorker cancelled")
        ElseIf e.Error IsNot Nothing Then
            bgWorker_Result = "ERR: " & e.Error.Message
            current_Second_File_Name = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0205: BgWorker error: " & e.Error.Message)
        ElseIf e.Result IsNot Nothing Then
            ' Only access e.Result if operation completed successfully
            Try
                Dim result As PrefetchResult = DirectCast(e.Result, PrefetchResult)

                ' The one decision, made here on the UI thread against the position the
                ' user is at NOW. While we decoded, they may have flipped on: such a
                ' result describes a file that is no longer "the next one", and taking
                ' it used to mean writing the image into whichever box the live flags
                ' pointed at - occasionally the visible one, swapping the picture under
                ' the user's eyes while the name and counter said something else.
                If Not String.Equals(result.NextFile, next_File_After_Current, StringComparison.OrdinalIgnoreCase) Then
                    result.Picture?.Dispose()
                    result.Data?.Dispose()
                    bgWorker_Result = "SKIPED"
                    current_Second_File_Name = ""
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0206: stale prefetch dropped: " & result.NextFile)
                Else
                    ' Success - transfer ownership to UI controls
                    If result.TargetIsBox1 Then
                        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
                        If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream?.Dispose()
                        Picture_Box_1.Image = result.Picture
                        pictureBox1_Stream = result.Data

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0210: bgWorker: P1 is loaded")
                    Else
                        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
                        If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream?.Dispose()
                        Picture_Box_2.Image = result.Picture
                        pictureBox2_Stream = result.Data

                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0220: bgWorker: P2 is loaded")
                    End If

                    ' Both halves of the verdict are set together, on this thread.
                    current_Second_File_Name = result.NextFile
                    bgWorker_Result = "LOADED"
                End If
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0203: Error handling BgWorker result: " & ex.Message)
                bgWorker_Result = "ERR: " & ex.Message
                current_Second_File_Name = ""
            End Try
        Else
            ' Completed successfully but no result
            bgWorker_Result = "SKIPED"
            current_Second_File_Name = ""
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0208: BgWorker completed with no result")
        End If

        ' Check if there's a pending operation to start
        If bgWorker_Has_Pending_Operation AndAlso bgWorker_Pending_Args IsNot Nothing Then
            bgWorker_Has_Pending_Operation = False
            Dim pending_Args As PrefetchRequest = bgWorker_Pending_Args
            bgWorker_Pending_Args = Nothing

            ' Start the pending operation
            If Not Is_No_Background_Tasks Then
                is_BgWorker_Online = True
                bgworker_Done.Reset()
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

    ''' <summary>Reads the folder. Returns Nothing both when the folder is EMPTY and
    ''' when it could not be read - is_Read_Error tells the two apart, because the
    ''' callers used to treat an empty folder as a failure and wipe Current_Folder_Path
    ''' and the combo box, i.e. throw the session away over a perfectly valid folder
    ''' the user had just finished sorting.</summary>
    Private Function GetFiles(ByRef is_Read_Error As Boolean) As Object
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1095: GetFiles..")
        is_Read_Error = False

        Try
            Dim file_Entry_List As List(Of FileEntry) = EnumerateConfiguredFiles(Current_Folder_Path) _
            .Where(Function(f) all_Supported_Extensions.Contains(f.Extension.ToLower())) _
            .Select(Function(f) New FileEntry With {
                .FilePath = f.FullName,
                .FileSize = f.Length,
                .FileName = f.Name,
                .FileDate = f.LastWriteTime
            }).ToList()

            If file_Entry_List.Count = 0 Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1096: Files count=0")
                lbl_Status.Text = Localization.T("Папка пустая")
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
            is_Read_Error = True
            lbl_Status.Text = Localization.T("! Ошибка чтения файлов")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1110: Error reading files: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Recursive scans deliberately do not use AllDirectories: Windows lets a
    ''' junction or symlink point back to an ancestor (or outside the selected tree),
    ''' and blindly enumerating it can loop forever.  Reparse directories are therefore
    ''' leaves.  Inaccessible children are skipped just like Explorer's normal scan.</summary>
    Private Function EnumerateConfiguredFiles(root As String) As IEnumerable(Of FileInfo)
        Dim result As New List(Of FileInfo)()
        If String.IsNullOrEmpty(root) OrElse Not Directory.Exists(root) Then Return result
#If NETFRAMEWORK Then
        Dim includeSubfolders As Boolean = False
#Else
        Dim includeSubfolders As Boolean = modern_Preferences IsNot Nothing AndAlso modern_Preferences.IncludeSubfolders
#End If
        If Not includeSubfolders Then
            Try
                result.AddRange(New DirectoryInfo(root).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            Catch
            End Try
            Return result
        End If

        Dim pending As New Stack(Of String)()
        pending.Push(root)
        While pending.Count > 0
            Dim directory As String = pending.Pop()
            Try
                Dim info As New DirectoryInfo(directory)
                If (info.Attributes And FileAttributes.ReparsePoint) <> 0 AndAlso
                   Not String.Equals(directory, root, StringComparison.OrdinalIgnoreCase) Then Continue While
                result.AddRange(info.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                For Each child As DirectoryInfo In info.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                    If (child.Attributes And FileAttributes.ReparsePoint) = 0 Then pending.Push(child.FullName)
                Next
            Catch ex As UnauthorizedAccessException
                Debug.WriteLine("Skipping inaccessible directory: " & directory)
            Catch ex As IOException
                Debug.WriteLine("Skipping unreadable directory: " & directory)
            End Try
        End While
        Return result
    End Function

End Class
