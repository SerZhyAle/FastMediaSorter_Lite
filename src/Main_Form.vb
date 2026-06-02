'sza130806lite
'sza240823 eto pizdec
'sza250411 random, filters, etc
'sza250502 refactor
'sza250506 grok
'FastMediaSorter
'sza2505207
'sza250606 gemini
'sza250608 copilot
'sza250609 gif fix
'sza250617 
'sza250721 choose file
'sza250723 LITE
'sza250808 zoom_Scale 
'sza251009 is_Super_Full_Screen_Mode

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
Imports System.Diagnostics ' Add this line with other imports


<ComVisible(True)>
Public Class Main_Form

    Private Const slide_show_limit As Integer = 30
    Private Const max_Namber_of_Recent_Folders As Integer = 100
    Private Const app_Mutex_Name As String = "FastMediaSorterSingleInstanceMutex"
    Private Const max_Number_Of_Files_For_List As Integer = 100000 'after - the array without sorting
    Private Const height_For_instruments_on_WebPanel As String = "45"
    Private Const percent_of_second_Color_Point = 20
    Private Const step_size_while_color_Search = 100
    Private Const SW_SHOWNOACTIVATE As Integer = 4
    Private Const SW_RESTORE As Integer = 9
    Private Const percent_of_color_deviation As Integer = 4
    Private Const percent_of_color_smooth_To_Remove = 7
    Private Const first_Color_X As Integer = 0
    Private Const first_Color_Y As Integer = 0
    Private Const second_Color_X As Integer = 5
    Private Const second_Color_Y As Integer = 5
    Private Const first_run_top = 50
    Private Const first_run_left = 50
    Private Const first_run_width = 800
    Private Const first_run_height = 600
    Private Const main_form_position_Limit_Top = 720
    Private Const main_form_position_Limit_Left = 1000
    Private Const main_form_position_Limit_Width = 3000
    Private Const main_form_position_Limit_Width_Low = 320
    Private Const main_form_position_Limit_Height = 3000
    Private Const main_form_position_Limit_Height_Low = 240
    Private Const the_Height_For_buttons = 20
    Private Const the_Width_For_buttons = 15
    Private Const top_first_line = 0
    Private Const left_first_column = 0
    Private Const biggest_slide_show_interval = 10000
    Private Const slideshow_limit_to_change_color = 2000
    Private Const how_long_wait_before_draw_perspective = 50
    Private Const max_Number_Of_Recent_Media_Files As Integer = 50

    Public Image_File_Extensions As String() = {".jpg", ".gif", ".jpeg", ".png", ".bmp", ".tiff", ".ico", ".wmf", ".emf", ".exif"}
    Private video_File_Extensions As New HashSet(Of String) From {".webm", ".ogg", ".3g2", ".mkv", ".3gp", ".mp4", ".m4v", ".m4a", ".mov", ".mp3", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".flv", ".wav", ".wma"}
    Private web_specific_image_extensions As New HashSet(Of String) From {".webp", ".heic", ".avif", ".svg"}


    Public Current_Folder_Path As String = ""
    Public Is_slide_show_mode As Boolean = False
    Public Is_to_show_picture_sizes As Boolean = False
    Public Is_to_show_file_sizes As Boolean = True
    Public Is_to_show_file_datetime As Boolean = True

    Private recent_Media_File_List As New List(Of String)
    Private Image_Panel_Form As Image_Panel_Form
    Private toolTip As ToolTip
    Private zoom_Scale As Single = 1.0F
    Private smoothIndex As Double = 0.0006

    ' Add these missing variable declarations near the top of the class:
    'Private Current_File_Name As String = ""
    'Private Current_Image_Path As String = ""
    'Private App_name As String = "FastMediaSorter"
    'Private Second_App_Name As String = "Settings"
    'Private Form_Color_Scheme As Integer = 1
    'Private Is_Russian_Language As Boolean = False
    'Private Is_No_Background_Tasks As Boolean = False
    'Private Is_Copying_not_Moving As Boolean = False
    'Private Is_no_request_before_file_operation As Boolean = False
    'Private Is_Pespective As Boolean = True
    'Private Picture_Box_Width_At_Panel As Integer = 80
    'Private Picture_Box_Height_At_Panel As Integer = 80
    'Private Hardkeys_to_move_mediafile(10) As String
    'Private Choosen_Picture_From_Panel As String = ""
    Private Table_Form As Table_Form

    ' Remove this line (line 84):
    ' Private pending_Single_Click_Timer As New System.Windows.Forms.Timer()

    Private pending_Single_Click_Event As MouseEventArgs = Nothing
    Private WithEvents pending_Single_Click_Timer As New System.Windows.Forms.Timer()
    Private is_Programmatic_Resize As Boolean = False

    ' Add this timer tick handler:
    Private Sub Pending_Single_Click_Timer_Tick(sender As Object, e As EventArgs) Handles pending_Single_Click_Timer.Tick
        pending_Single_Click_Timer.Stop()

        If pending_Single_Click_Event IsNot Nothing Then
            ' Execute the delayed single-click action
            Dim delayed_Event As MouseEventArgs = pending_Single_Click_Event
            pending_Single_Click_Event = Nothing

            ' Only call MouseUse if not in zoom mode or not left button
            If zoom_Scale = 1 OrElse delayed_Event.Button <> MouseButtons.Left Then
                MouseUse(delayed_Event)
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1149: Delayed single-click executed")
        End If
    End Sub

    Private is_form_shown As Boolean = False
    Private last_Perspective_Draw_Time As DateTime
    Private Shared mutex As Mutex
    Private app_Run_Count As Integer
    Private media_View_Count As Integer
    Private is_Combo_Set_Auto As Boolean = False

    Private bgWorker_Pending_Args As Tuple(Of String, String) = Nothing
    Private bgWorker_Has_Pending_Operation As Boolean = False

    Private is_File_Reseived_From_Outside As Boolean = False
    Private is_First_Scroll_Event As Boolean = False

    Private is_Second_PictureBox_Active As Boolean = False
    Private is_this_First_Picture_File_We_Show As Boolean = True
    Private is_First_Picture_Box_Need_To_Be_Cached As Boolean = False
    Private current_File_List As ReadOnlyCollection(Of String)
    Private current_File_Index As Integer
    Private current_Second_File_Name As String
    Private total_File_Count As Integer

    Private next_File_After_Current As String
    Private current_Loaded_File_Name As String
    Private history_File_Name As String
    Private current_Image_Scale As String = ""
    Private last_Back_Color As System.Drawing.Color

    Private history_Operation_Target_Path As String
    Private is_Image_Mode As Boolean = True

    Private files_List As List(Of String) = Nothing
    Private files_Array As String() = Nothing
    Private is_Files_Array_Active As Boolean = False

    Private is_Dragging As Boolean = False
    Private drag_Start_Point As Point
    Private last_Drag_Update_Time As DateTime = DateTime.MinValue
    Private Const DRAG_UPDATE_INTERVAL_MS As Integer = 16
    ' Add these variables near other Private variable declarations
    Private original_PictureBox_Left As Integer
    Private original_PictureBox_Top As Integer

    Private is_Table_Form_Open As Boolean
    Private last_Action_Time As DateTime
    Private is_Full_Screen_Mode As Boolean
    Private is_Super_Full_Screen_Mode As Boolean
    Private is_External_Input_Received As Boolean = False
    Private was_External_Input_Previously As Boolean
    Private WithEvents SlideShowTimer As New System.Windows.Forms.Timer()
    Private is_Slide_Show_Random_Mode As Boolean
    Private is_WebBrowser_Visible As Boolean
    Private is_PictureBox1_Visible As Boolean
    Private is_PictureBox2_Visible As Boolean
    Private last_Loaded_Uri As String = ""
    Private is_Folder_Read_Required As Boolean = False
    Private total_Files_Count_Text As String = "0"
    Private mouse_Down_Start_Point As Point

    Private video_Volume_Level As Double = 1
    Private is_Video_Muted As Boolean = False
    Private is_TextBox_Editing As Boolean = False

    ' LibVLC fallback player (for formats the IE WebBrowser cannot decode: ZMBV/AVI, VP9, etc.)
    Private libVlc As LibVLCSharp.Shared.LibVLC = Nothing
    Private vlc_Video_View As LibVLCSharp.WinForms.VideoView = Nothing
    Private vlc_Media_Player As LibVLCSharp.Shared.MediaPlayer = Nothing
    Private is_Vlc_Init_Attempted As Boolean = False
    Private is_Vlc_Playing As Boolean = False

    Dim history_Source_File_Name As String = ""
    Dim history_Destination_File_Name As String = ""
    Private WithEvents BgWorker As New BackgroundWorker()
    Private is_BgWorker_Online As Boolean

    Private bgWorker_Result As String = "EMPTY"
    Private pictureBox1_Stream As IO.MemoryStream
    Private pictureBox2_Stream As IO.MemoryStream
    Private Const WmCopyData As Integer = &H4A

    Private all_Supported_Extensions As New HashSet(Of String)()
    Private recent_Folder_List As New List(Of String)

    Private WithEvents FileOperationWorker As New BackgroundWorker
    Private current_File_Operation As String
    Private current_File_Operation_Args As Object

    Private WithEvents ResizeDebounceTimer As New System.Windows.Forms.Timer()
    Private is_Last_Full_Screen_State As Boolean = False

    Private WithEvents gif_Restart_Timer As New System.Windows.Forms.Timer()
    Private gif_Total_Duration_Ms As Integer = 0
    Private gif_Restart_Image_Ref As Image = Nothing

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    Private last_Media_Area_Click_Time As DateTime = DateTime.MinValue
    Private last_Media_Area_Click_Button As MouseButtons = MouseButtons.None
    Private ReadOnly DoubleClickTimeThreshold As Integer = SystemInformation.DoubleClickTime

    Private Sub InitializeExtensionLists()
        all_Supported_Extensions.UnionWith(Image_File_Extensions)
        all_Supported_Extensions.UnionWith(video_File_Extensions)
        all_Supported_Extensions.UnionWith(web_specific_image_extensions)
    End Sub

    Private Const WM_COPYDATA As Integer = &H4A

    <StructLayout(LayoutKind.Sequential)>
    Public Structure COPYDATASTRUCT
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As COPYDATASTRUCT) As Integer
    End Function

    Private Declare Function MapViewOfFile Lib "kernel32.dll" (ByVal hFileMappingObject As IntPtr, ByVal dwDesiredAccess As Integer, ByVal dwFileOffsetHigh As Integer, ByVal dwFileOffsetLow As Integer, ByVal dwNumberOfBytesToMap As Integer) As IntPtr
    Private Declare Function UnmapViewOfFile Lib "kernel32.dll" (ByVal lpBaseAddress As IntPtr) As Boolean
    Private Declare Function CloseHandle Lib "kernel32.dll" (ByVal hObject As IntPtr) As Boolean

    Const WM_USER As Integer = &H400
    Const MY_CUSTOM_MESSAGE As Integer = WM_USER + 1
    Const FILE_MAP_READ As Integer = &H4
    Private Const minimum_time_before_next_media_file As Double = 0.04

    <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function StrCmpLogicalW(psz1 As String, psz2 As String) As Integer
    End Function

    <DllImport("shell32.dll")>
    Private Shared Sub SHChangeNotify(wEventId As Integer, uFlags As Integer, dwItem1 As IntPtr, dwItem2 As IntPtr)
    End Sub

    Public Class NaturalFilenameComparer
        Implements IComparer(Of String)
        Public Function Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
            Return StrCmpLogicalW(x, y)
        End Function
    End Class


    ' Add this handler in Main_Form
    Private Sub Image_Panel_Form_FormClosed(sender As Object, e As FormClosedEventArgs)
        If Not String.IsNullOrEmpty(Choosen_Picture_From_Panel) Then
            External_message(Choosen_Picture_From_Panel)
            Choosen_Picture_From_Panel = "" ' Optionally reset after use
        End If
    End Sub

    Private Sub InitializeTooltips()
        If toolTip Is Nothing Then
            toolTip = New ToolTip()
            ' Optional: Customize tooltip appearance and behavior
            toolTip.AutoPopDelay = 5000 ' Linger time
            toolTip.InitialDelay = 700  ' Time before appearing
            toolTip.ReshowDelay = 500   ' Time before reappearing
            toolTip.ShowAlways = True   ' Show even if form is not active
        End If

        ' --- Buttons and Checkboxes ---
        toolTip.SetToolTip(btn_Select_Folder, If(Is_Russian_Language, "Выбрать папку с медиафайлами", "Select a folder with media files"))
        toolTip.SetToolTip(btn_Review, If(Is_Russian_Language, "Перечитать текущую папку", "Reload the current folder"))
        toolTip.SetToolTip(btn_Panel, If(Is_Russian_Language, "Показать панель изображений (F3)", "Show the image panel (F3)"))
        toolTip.SetToolTip(btn_Full_Screen, If(Is_Russian_Language, "Полноэкранный режим", "Toggle fullscreen mode"))
        toolTip.SetToolTip(btn_Prev_File, If(Is_Russian_Language, "Предыдущий файл (Стрелка влево, PgUp)", "Previous file (Left Arrow, PgUp)"))
        toolTip.SetToolTip(btn_Next_File, If(Is_Russian_Language, "Следующий файл (Стрелка вправо, PgDn)", "Next file (Right Arrow, PgDn)"))
        toolTip.SetToolTip(btn_Next_Random, If(Is_Russian_Language, "Случайный файл (Y)", "Random file (Y)"))
        toolTip.SetToolTip(btn_Random_Slideshow, If(Is_Russian_Language, "Случайное слайд-шоу (I, F5)", "Random slideshow (I, F5)"))
        toolTip.SetToolTip(btn_Slideshow, If(Is_Russian_Language, "Слайд-шоу (S)", "Slideshow (S)"))
        toolTip.SetToolTip(btn_Move_Table, If(Is_Russian_Language, "Открыть таблицу папок-получателей и насчтройки (F2)", "Open the destination folders table and Options (F2)"))
        toolTip.SetToolTip(btn_Rename, If(Is_Russian_Language, "Переименовать файл (F6)", "Rename file (F6)"))
        toolTip.SetToolTip(bt_Delete, If(Is_Russian_Language, "Удалить файл (Del)", "Delete file (Del)"))
        toolTip.SetToolTip(btn_Language, If(Is_Russian_Language, "Переключить язык на английский", "Switch language to Russian"))
        toolTip.SetToolTip(chkbox_Top_Most, If(Is_Russian_Language, "Поверх всех окон", "Always on top"))
        toolTip.SetToolTip(btn_choose_file, If(Is_Russian_Language, "Выбрать файл..", "Choose file.."))

        ' --- ComboBoxes and Labels ---
        toolTip.SetToolTip(cmbox_Sort, If(Is_Russian_Language, "Порядок сортировки файлов", "File sort order"))
        toolTip.SetToolTip(cmbox_Media_Folder, If(Is_Russian_Language, "Текущая папка. Введите путь и нажмите Enter для перехода.", "Current folder. Type a path and press Enter to navigate."))
        toolTip.SetToolTip(lbl_Folder, If(Is_Russian_Language, "Нажмите, чтобы скопировать путь к папке", "Click to copy the folder path"))
        toolTip.SetToolTip(lbl_Current_File, If(Is_Russian_Language, "Нажмите, чтобы скопировать путь к файлу", "Click to copy the file path"))
        toolTip.SetToolTip(lbl_Status, If(Is_Russian_Language, "Статус текущей операции", "Status of the current operation"))
        toolTip.SetToolTip(lbl_File_Number, If(Is_Russian_Language, "Номер текущего файла и общее количество", "Current file number and total count"))

        toolTip.SetToolTip(btn_RecentFiles, If(Is_Russian_Language, "Недавние файлы", "Recent files"))

        ' --- Main Display Area ---
        'Dim mediaControlTooltip As String = If(Is_Russian_Language,
        '"ЛКМ: Следующий файл" & vbCrLf & "ПКМ: Предыдущий файл" & vbCrLf & "СКМ: Переименовать" & vbCrLf & "Колесо мыши: Навигация" & vbCrLf & "Ctrl+Колесо: Масштаб" & vbCrLf & "Alt+Колесо: Сброс масштаба" & vbCrLf & "Двойной клик: Выход из полноэкранного режима",
        '"Left-Click: Next file" & vbCrLf & "Right-Click: Previous file" & vbCrLf & "Middle-Click: Rename" & vbCrLf & "Mouse Wheel: Navigate" & vbCrLf & "Ctrl+Wheel: Zoom" & vbCrLf & "Alt+Wheel: Reset Zoom" & vbCrLf & "Double-Click: Exit fullscreen")

        'toolTip.SetToolTip(Picture_Box_1, mediaControlTooltip)
        'toolTip.SetToolTip(Picture_Box_2, mediaControlTooltip)
        'toolTip.SetToolTip(Web_Browser, mediaControlTooltip)
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)

        If m.Msg = MY_CUSTOM_MESSAGE Then
            Dim hMap As IntPtr = m.WParam
            If hMap <> IntPtr.Zero Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0777: MESSAGE")
                Try
                    Dim pBuf As IntPtr = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, 0)
                    If pBuf <> IntPtr.Zero Then
                        Try
                            Dim length As Integer = 0
                            While Marshal.ReadByte(pBuf, length) <> 0
                                length += 1
                            End While

                            Dim bytes(length - 1) As Byte
                            Marshal.Copy(pBuf, bytes, 0, length)
                            Dim receivedString As String = Encoding.UTF8.GetString(bytes)
                            If Not String.IsNullOrEmpty(receivedString) Then
                                External_message(receivedString)
                            Else
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0778: Error processing MY_CUSTOM_MESSAGE - received empty")
                            End If
                        Finally
                            UnmapViewOfFile(pBuf)
                        End Try
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0776: Error processing MY_CUSTOM_MESSAGE: " & Marshal.GetLastWin32Error())
                    End If
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0779: Error processing MY_CUSTOM_MESSAGE: " & ex.Message)
                Finally
                    CloseHandle(hMap)
                End Try
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0779: Error processing MY_CUSTOM_MESSAGE - received NULL")
            End If
        End If

        If m.Msg = WM_COPYDATA Then

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0888: WM_COPYDATA")
            Try
                Dim cds As COPYDATASTRUCT = CType(Marshal.PtrToStructure(m.LParam, GetType(COPYDATASTRUCT)), COPYDATASTRUCT)
                Dim received_Data As String = Marshal.PtrToStringAnsi(cds.lpData, cds.cbData)
                If String.IsNullOrEmpty(received_Data) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0000: Error processing WM_COPYDATA - received data is null or empty")
                    Return
                End If

                External_message(received_Data)

            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0020: Error processing WM_COPYDATA - " & ex.Message)
            End Try
        End If

        MyBase.WndProc(m)
    End Sub

    Private Sub External_message(receivedData As String)
        Dim argument As String = receivedData.TrimEnd(Chr(0)).Trim()
        argument = Regex.Replace(argument, "(?<!^)(\\\\)+", "\")

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0010: received from new instance: " & argument)

        Dim is_Form_Minimized As Boolean = (Me.WindowState = FormWindowState.Minimized)
        Dim prev_Foreground_Window_Handle As IntPtr = IntPtr.Zero

        If Not is_Form_Minimized Then
            prev_Foreground_Window_Handle = GetForegroundWindow()
            If prev_Foreground_Window_Handle = Me.Handle Then
                prev_Foreground_Window_Handle = IntPtr.Zero
            End If
        End If

        is_External_Input_Received = True
        is_File_Reseived_From_Outside = True

        ProcessArgument(argument)

        If is_Form_Minimized Then
            ShowWindow(Me.Handle, SW_SHOWNOACTIVATE)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0011: focus recovered")
        ElseIf prev_Foreground_Window_Handle <> IntPtr.Zero Then
            Dim currentForegroundHandle As IntPtr = GetForegroundWindow()
            If currentForegroundHandle = Me.Handle Then
                SetForegroundWindow(prev_Foreground_Window_Handle)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0012: try focus another: " & prev_Foreground_Window_Handle.ToString())
            End If
        End If
    End Sub

    Private Sub SetWebBrowserCompatibilityMode()
        Try
            Using key = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", True)
                If key Is Nothing Then
                    Using newKey = Registry.CurrentUser.CreateSubKey("Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION")
                        newKey.SetValue(Process.GetCurrentProcess().ProcessName & ".exe", 11001, RegistryValueKind.DWord)
                    End Using
                Else
                    key.SetValue(Process.GetCurrentProcess().ProcessName & ".exe", 11001, RegistryValueKind.DWord)
                End If
            End Using
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0030: Error to set WebBrowser mode")
        End Try
    End Sub

    Public Sub InitNew()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0001: init new")

        pending_Single_Click_Timer.Interval = SystemInformation.DoubleClickTime
        pending_Single_Click_Timer.Enabled = False

        ' Initialize Table_Form if not already done
        If Table_Form Is Nothing Then
            Table_Form = New Table_Form()
        End If

        ResizeDebounceTimer.Interval = 200
        ResizeDebounceTimer.Enabled = False

        Dim is_New_Instance_Created As Boolean
        mutex = New Mutex(True, app_Mutex_Name, is_New_Instance_Created)

        is_TextBox_Editing = True
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0002: Initialize")
        cmbox_Sort.Items.Clear()
        cmbox_Sort.Items.AddRange(New String() {"abc", "xyz", "rnd", ">size", "<size", ">time", "<time", "<0123", ">3210"})

        cmbox_Sort.SelectedIndex = 0
        is_TextBox_Editing = False

        BgWorker.WorkerReportsProgress = True
        BgWorker.WorkerSupportsCancellation = True
        Web_Browser.ObjectForScripting = Me

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0003: InitializeExtensionLists")
        InitializeExtensionLists()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0004: SetWebBrowserCompatibilityMode")
        SetWebBrowserCompatibilityMode()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0009: InitializeFileOperationWorker")
        InitializeFileOperationWorker()
    End Sub

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

    Public Sub ProcessArgument(argument_Raw_Text As String)
        Dim argument_For_Path As String = argument_Raw_Text.Trim()
        Dim argument_For_Flags As String = argument_Raw_Text.ToLowerInvariant()
        Dim is_No_Back_Flag_In_This_Call As Boolean = argument_For_Flags.Contains("-noback")

        If is_No_Back_Flag_In_This_Call Then
            Is_No_Background_Tasks = True
            argument_For_Path = System.Text.RegularExpressions.Regex.Replace(argument_For_Path, "-noback", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0232: ProcessArgument: -NoBack")
        End If

        If String.IsNullOrEmpty(argument_For_Path) Then
            If Is_No_Background_Tasks Then
                lbl_Status.Text = If(Is_Russian_Language, "Режим -NoBack активен. Ожидание файла/папки.", "NoBack mode active. Awaiting file/folder.")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0234: ProcessArgument: -NoBack but no file")
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0236: ProcessArgument: no file argumented")
            End If
            Return
        End If

        Try
            Dim is_Directory As Boolean = Directory.Exists(argument_For_Path)
            If is_Directory Then
                If Not is_No_Back_Flag_In_This_Call Then
                    Is_No_Background_Tasks = False
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0278: ProcessArgument: folder without -NoBack. mode -NoBack is off")
                End If

                Current_Folder_Path = argument_For_Path
                is_TextBox_Editing = True
                cmbox_Media_Folder.Text = Current_Folder_Path
                is_TextBox_Editing = False

                Dim saved_Folder_Path = GetSetting(App_name, Second_App_Name, "ImageFolder", "")
                If saved_Folder_Path = Current_Folder_Path Then
                    Integer.TryParse(GetSetting(App_name, Second_App_Name, "LastCounter"), current_File_Index)
                    If current_File_Index > 0 Then
                    Else
                        current_File_Index = 0
                    End If
                Else
                    current_File_Index = 0
                End If

                ReadShowMediaFile("ReadFolderAndFile")
            Else
                If Not File.Exists(argument_For_Path) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0280: file of argument is NOT exists: " & argument_For_Path)
                    Return
                End If

                Dim argument_Folder_Path As String = Path.GetDirectoryName(argument_For_Path)

                If Not String.Equals(Current_Folder_Path, argument_Folder_Path, StringComparison.OrdinalIgnoreCase) Then
                    If Not is_No_Back_Flag_In_This_Call Then
                        Is_No_Background_Tasks = False
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0288: ProcessArgument: file from the NEW folder with -NoBack. Mode -NoBack is off.")
                    End If
                End If

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0290: File is set from arg: " & argument_For_Path)

                Current_Folder_Path = argument_Folder_Path
                Current_Image_Path = argument_For_Path
                Current_File_Name = argument_For_Path
                is_TextBox_Editing = True
                cmbox_Media_Folder.Text = Current_Folder_Path
                is_TextBox_Editing = False
                is_External_Input_Received = True
                was_External_Input_Previously = True
                current_File_Index = 0
                total_File_Count = 1
                files_List = New List(Of String) From {Current_Image_Path}

                ReadShowMediaFile("ReadFolderAndKnownFile")
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0300: Error processing argument: " & ex.Message)
            Current_Folder_Path = ""
            is_TextBox_Editing = True
            cmbox_Media_Folder.Text = ""
            is_TextBox_Editing = False
            Is_No_Background_Tasks = False
        End Try
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles btn_Select_Folder.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0305: btn_Select_Folder")
        Dim folder_Browser_Dialog As New FolderBrowserDialog()
        folder_Browser_Dialog.SelectedPath = Current_Folder_Path

        folder_Browser_Dialog.Description = If(Is_Russian_Language, "Выберите папку с медиафайлами..", "Set folder of media files..")

        If folder_Browser_Dialog.ShowDialog() = Windows.Forms.DialogResult.OK Then
            Current_Folder_Path = folder_Browser_Dialog.SelectedPath
            lbl_Status.Text = If(Is_Russian_Language, "выбрана папка", "folder selected") & ": " & Current_Folder_Path
            Is_No_Background_Tasks = False
            ReadShowMediaFile("ReadFolderAndFile")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0310: Folder read")
        End If
    End Sub

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
                For Each ctrl As Control In Me.Controls
                    If ctrl.Visible Then
                        If TypeOf ctrl Is Label Then
                            Dim lbl As Label = CType(ctrl, Label)
                            lbl.ForeColor = OppositeColor
                            lbl.BackColor = System.Drawing.Color.Transparent
                        ElseIf TypeOf ctrl Is Button Then
                            Dim btn As Button = CType(ctrl, Button)
                            btn.ForeColor = OppositeColor
                        ElseIf TypeOf ctrl Is ComboBox Then
                            Dim cmb As ComboBox = CType(ctrl, ComboBox)
                            cmb.BackColor = back_Color
                            cmb.ForeColor = OppositeColor
                        ElseIf TypeOf ctrl Is CheckBox Then
                            Dim chb As CheckBox = CType(ctrl, CheckBox)
                            chb.BackColor = back_Color
                            chb.ForeColor = OppositeColor
                        End If
                    End If
                Next

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

    Private Sub Form1_DragEnter(sender As Object, e As DragEventArgs) Handles Me.DragEnter
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    Private Sub Form1_DragDrop(sender As Object, e As DragEventArgs) Handles Me.DragDrop
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim files() As String = CType(e.Data.GetData(DataFormats.FileDrop), String())
            For Each file As String In files
                ProcessArgument(file)
                Exit For
            Next
        End If
    End Sub

    Private Sub btn_RecentFiles_Click(sender As Object, e As EventArgs) Handles btn_RecentFiles.Click
        If recent_Media_File_List Is Nothing OrElse
            recent_Media_File_List.Count = 0 Then

            '   MessageBox.Show(If(Is_Russian_Language, "Нет недавних файлов.", "No recent files."), "Recent Files", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim menu As New ContextMenuStrip()
        For Each file In recent_Media_File_List.AsEnumerable().Reverse()
            Dim item = menu.Items.Add(System.IO.Path.GetFileName(file))
            item.Tag = file
        Next

        AddHandler menu.ItemClicked, Sub(s, args)
                                         Dim selectedFile = TryCast(args.ClickedItem.Tag, String)
                                         If Not String.IsNullOrEmpty(selectedFile) Then
                                             ProcessArgument(selectedFile)
                                         End If
                                     End Sub

        AddHandler menu.Closed, Sub(s, args)
                                    Dim m = DirectCast(s, ContextMenuStrip)
                                    Me.BeginInvoke(New Action(Sub() m.Dispose()))
                                End Sub

        menu.Show(btn_RecentFiles, New Point(0, btn_RecentFiles.Height))
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(" - - - ")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0000: Form started")
        Me.AllowDrop = True

        InitNew()
        CheckAndOfferImageAssociations()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0010: init finished")

        is_TextBox_Editing = True
        app_Run_Count = 0
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "RunsCount", "0"), app_Run_Count)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0020: Apps RUN: " & app_Run_Count.ToString)

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "mediaViewedCount", "0"), media_View_Count)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0021: media Viewed: " & media_View_Count.ToString)

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "color_scheme", "1"), Form_Color_Scheme)

        Is_Russian_Language = GetSetting(App_name, Second_App_Name, "Is_Russian_Language", "1") = "1"
        InitializeTooltips()

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Width_At_Panel", "80"), Picture_Box_Width_At_Panel)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Height_At_Panel", "80"), Picture_Box_Height_At_Panel)

        Is_Pespective = GetSetting(App_name, Second_App_Name, "isPerspective", "1") = "1"
        Is_no_request_before_file_operation = GetSetting(App_name, Second_App_Name, "NoRequestBeforeFileOperation", "0") = "1"

        Is_to_show_picture_sizes = GetSetting(App_name, Second_App_Name, "ShowPictureSizes", "1") = "1"
        Is_to_show_file_sizes = GetSetting(App_name, Second_App_Name, "ShowFileSizes", "1") = "1"
        Is_to_show_file_datetime = GetSetting(App_name, Second_App_Name, "ShowFileDates", "1") = "1"
        Is_Video_Loop = GetSetting(App_name, Second_App_Name, "IsVideoLoop", "0") = "1"

        Dim sort_Direction_Index = 0
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "SortDir", "0"), sort_Direction_Index)

        If sort_Direction_Index < 0 Then
            sort_Direction_Index = 0
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1138: cmbox_Sort is set to 0")
        End If
        cmbox_Sort.SelectedIndex = sort_Direction_Index

        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")
        LngCh()
        lbl_Info.Text = Application.ProductVersion & " sza@ukr.net"
        Table_Form.LngCh()

        lbl_Help_Info.Visible = GetSetting(App_name, Second_App_Name, "FirstRun", "1") = "1"

        Is_Copying_not_Moving = GetSetting(App_name, Second_App_Name, "CopyMode", "0") = "1"
        chkbox_Top_Most.Checked = GetSetting(App_name, Second_App_Name, "chkTopMost", "0") = "1"
        is_Table_Form_Open = GetSetting(App_name, Second_App_Name, "TableOpened", "0") = "1"

        Dim video_Volume_String = GetSetting(App_name, Second_App_Name, "VideoVolume", "1.0")
        video_Volume_Level = ParseVideoVolumeSetting(video_Volume_String, video_Volume_Level)
        is_Video_Muted = GetSetting(App_name, Second_App_Name, "VideoMuted", "0") = "1"

        For z = 0 To 9
            Hardkeys_to_move_mediafile(z) = GetSetting(App_name, Second_App_Name, "MoveOn" & z.ToString, "")
        Next
        Hardkeys_to_move_mediafile(10) = GetSetting(App_name, Second_App_Name, "MoveOn0", "")

        Dim recent_Folders_Data As String = GetSetting(App_name, Second_App_Name, "RecentFolders", "")
        If Not String.IsNullOrEmpty(recent_Folders_Data) Then
            recent_Folder_List = recent_Folders_Data.Split("|"c).ToList()
            recent_Folder_List.RemoveAll(Function(x) String.IsNullOrEmpty(x))

            For Each folder In recent_Folder_List
                If cmbox_Media_Folder.Items.Count < max_Namber_of_Recent_Folders Then
                    cmbox_Media_Folder.Items.Add(folder)
                End If
            Next
        End If

        Dim recent_Media_Files_Data As String = GetSetting(App_name, Second_App_Name, "RecentMediaFiles", "")
        If Not String.IsNullOrEmpty(recent_Media_Files_Data) Then
            recent_Media_File_List = recent_Media_Files_Data.Split("|"c).ToList()
            recent_Media_File_List.RemoveAll(Function(x) String.IsNullOrEmpty(x))
            If recent_Media_File_List.Count > max_Number_Of_Recent_Media_Files Then
                recent_Media_File_List = recent_Media_File_List.Skip(recent_Media_File_List.Count - max_Number_Of_Recent_Media_Files).ToList()
            End If
        End If

        If Table_Form.chkbox_Independent_Thread_For_File_Operation IsNot Nothing Then
            Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked = GetSetting(App_name, Second_App_Name, "UseIndependentThreadForOperationsWithFiles", "0") = "1"
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0025: Command line args count: " & My.Application.CommandLineArgs.Count.ToString)


        If My.Application.CommandLineArgs.Count > 0 Then
            Dim fullCommandLine As String = String.Join(" ", My.Application.CommandLineArgs.ToArray())
            ProcessArgument(fullCommandLine)
        Else
            Current_Folder_Path = GetSetting(App_name, Second_App_Name, "ImageFolder", "")
            If Not Current_Folder_Path = "" Then
                total_File_Count = 0
                Try
                    total_File_Count = My.Computer.FileSystem.GetDirectoryInfo(Current_Folder_Path).EnumerateFiles.Count
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1139: ERR: " & ex.Message)
                End Try

                Integer.TryParse(GetSetting(App_name, Second_App_Name, "LastCounter"), current_File_Index)

                If Not total_File_Count = 0 AndAlso
                current_File_Index > 0 AndAlso
                current_File_Index < total_File_Count Then

                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: folder and file found in savings: " & Current_Folder_Path & " - " & current_File_Index.ToString)

                    ReadShowMediaFile("ReadFiles")
                Else
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1157: folder set from savings, but saved file is not found: " & Current_Folder_Path & " - " & current_File_Index.ToString)
                    current_File_Index = 1

                    If Not total_File_Count = 0 Then ReadShowMediaFile("ReadFolderAndFile")
                End If
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1158: no folder saved")
            End If
        End If

        If is_Table_Form_Open Then
            Table_Form.Show()
        End If
        is_TextBox_Editing = False

        Dim app_Top_Int As Integer = first_run_top
        Dim app_Left_Int As Integer = first_run_left
        Dim app_Width_Int As Integer = first_run_width
        Dim app_Height_Int As Integer = first_run_height

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "AppTop"), app_Top_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "AppLeft"), app_Left_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "AppWidth"), app_Width_Int)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "AppHeight"), app_Height_Int)

        app_Top_Int = If(app_Top_Int < 0 OrElse app_Top_Int > main_form_position_Limit_Top, first_run_top, app_Top_Int)
        app_Left_Int = If(app_Left_Int < 0 OrElse app_Left_Int > main_form_position_Limit_Left, first_run_left, app_Left_Int)
        app_Width_Int = If(app_Width_Int < main_form_position_Limit_Width_Low OrElse app_Width_Int > main_form_position_Limit_Width, first_run_width, app_Width_Int)
        app_Height_Int = If(app_Height_Int < main_form_position_Limit_Height_Low OrElse app_Height_Int > main_form_position_Limit_Height, first_run_height, app_Height_Int)

        Me.SetBounds(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1950: Form_Sizes: " & app_Left_Int.ToString & " - " & app_Top_Int.ToString & " " & app_Width_Int.ToString & " - " & app_Height_Int.ToString)

        ResizeDebounceTimer.Stop()

        ISizeChanged()

        is_form_shown = True

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1120: Form Loaded")
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles btn_Prev_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1130: btn_Prev_File")
        SlideShowStop()
        ReadShowMediaFile("ReadPrevFile")
    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles btn_Next_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1140: btn_Next_File")
        SlideShowStop()
        ReadShowMediaFile("ReadNextFile")
    End Sub

    Private Sub HandlePictureBoxMouseDown(sender As Object, e As MouseEventArgs)
        ' Store the initial mouse position for drag detection
        mouse_Down_Start_Point = e.Location

        ' PRIORITY 1: Check modifier keys FIRST (these execute immediately without delay)
        If (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 10 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Shift+LeftClick - jumping +10 files")
                    SlideShowStop()
                    current_File_Index += 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+10 файлов", "+10 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +10", "Not enough files for +10 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 10 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Shift+RightClick - jumping -10 files")
                    SlideShowStop()
                    current_File_Index -= 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-10 файлов", "-10 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -10", "Current index too low for -10 jump")
                End If
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 100 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Ctrl+LeftClick - jumping +100 files")
                    SlideShowStop()
                    current_File_Index += 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+100 файлов", "+100 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +100", "Not enough files for +100 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 100 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Ctrl+RightClick - jumping -100 files")
                    SlideShowStop()
                    current_File_Index -= 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-100 файлов", "-100 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -100", "Current index too low for -100 jump")
                End If
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 1000 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Alt+LeftClick - jumping +1000 files")
                    SlideShowStop()
                    current_File_Index += 1000
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+1000 файлов", "+1000 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +1000", "Not enough files for +1000 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 1000 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Alt+RightClick - jumping -1000 files")
                    SlideShowStop()
                    current_File_Index -= 1000
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-1000 файлов", "-1000 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -1000", "Current index too low for -1000 jump")
                End If
                Return
            End If
        End If

        ' PRIORITY 2: DOUBLE-CLICK DETECTION (always active for left/middle buttons)
        If e.Button = MouseButtons.Left OrElse e.Button = MouseButtons.Middle Then
            Dim current_Click_Time As DateTime = DateTime.Now
            Dim time_Since_Last_Click As Double = (current_Click_Time - last_Media_Area_Click_Time).TotalMilliseconds

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1147: Click, time since last: " & time_Since_Last_Click.ToString("F0") & "ms (threshold: " & DoubleClickTimeThreshold.ToString() & "ms)")

            If time_Since_Last_Click < DoubleClickTimeThreshold AndAlso time_Since_Last_Click > 0 Then
                ' DOUBLE-CLICK DETECTED - toggle fullscreen
                pending_Single_Click_Timer.Stop()
                pending_Single_Click_Event = Nothing

                is_Full_Screen_Mode = Not is_Full_Screen_Mode
                If Not is_Full_Screen_Mode Then is_Super_Full_Screen_Mode = False
                SetViewSizes()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1150: DOUBLE-CLICK - fullscreen toggled to: " & is_Full_Screen_Mode.ToString())

                last_Media_Area_Click_Time = DateTime.MinValue
                last_Media_Area_Click_Button = MouseButtons.None
                SlideShowStop()

                Return
            End If

            ' Single click - store timestamp for double-click detection
            last_Media_Area_Click_Time = current_Click_Time
            last_Media_Area_Click_Button = e.Button
        End If

        ' PRIORITY 3: DELAY LEFT-CLICK to allow double-click detection
        If e.Button = MouseButtons.Left AndAlso zoom_Scale = 1 Then
            ' Always delay to allow double-click detection (enter/exit fullscreen)
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = e
            pending_Single_Click_Timer.Start()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1148: Left-click delayed for double-click detection")
        Else
            ' Right-click, middle-click, or zoomed - execute immediately
            If zoom_Scale = 1 OrElse e.Button <> MouseButtons.Left Then
                MouseUse(e)
            End If
        End If
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseDown
        HandlePictureBoxMouseDown(sender, e)
    End Sub

    Private Sub PictureBox2_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_2.MouseDown
        HandlePictureBoxMouseDown(sender, e)
    End Sub

    Private Sub MouseUse(ByVal e As MouseEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1170: MouseUse Delta: " & e.Delta.ToString)

        SlideShowStop()

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            SkipZoom()

        ElseIf e.Delta <> 0 AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible) AndAlso (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            ' SHIFT + Scroll: Set to original 1:1 resolution
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1174: zoom to original 1:1 resolution")

            Dim active_Image As Image = Nothing
            If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                active_Image = Picture_Box_1.Image
            ElseIf is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                active_Image = Picture_Box_2.Image
            End If

            If active_Image IsNot Nothing Then
                ' Calculate position to center the image at original size
                Dim top_first_row = 0

                If Not is_Super_Full_Screen_Mode Then
                    top_first_row = lbl_Status.Top + lbl_Status.Height
                End If

                Dim available_Width = Me.Width
                Dim available_Height = Me.Height - top_first_row

                ' Set to original image dimensions
                Dim new_Width As Integer = active_Image.Width
                Dim new_Height As Integer = active_Image.Height

                ' Center the image in the available space
                Dim new_Left As Integer = (available_Width - new_Width) \ 2
                Dim new_Top As Integer = top_first_row + (available_Height - new_Height) \ 2

                ' Ensure the image is not positioned off-screen
                new_Left = Math.Max(new_Left, -new_Width + 100) ' Allow some off-screen but keep 100px visible
                new_Top = Math.Max(new_Top, top_first_row)

                Picture_Box_1.Width = new_Width
                Picture_Box_1.Height = new_Height
                Picture_Box_1.Left = new_Left
                Picture_Box_1.Top = new_Top

                Picture_Box_2.Size = Picture_Box_1.Size
                Picture_Box_2.Location = Picture_Box_1.Location

                ' Set zoom_Scale to 0 as flag for 1:1 mode
                zoom_Scale = 0.0F
                lbl_Zoom.Text = "1:1"

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1175: 1:1 resolution set: " & new_Width.ToString & "x" & new_Height.ToString & " at " & new_Left.ToString & "," & new_Top.ToString)
            End If

        ElseIf e.Delta <> 0 AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible) AndAlso (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            Dim zoom_Scale_Factor As Single = If(e.Delta > 0, 1.1F, 0.9F)

            Dim old_Width As Integer = Picture_Box_1.Width
            Dim old_Height As Integer = Picture_Box_1.Height
            Dim old_Left As Integer = Picture_Box_1.Left
            Dim old_Top As Integer = Picture_Box_1.Top

            Dim new_Width As Integer = CInt(old_Width * zoom_Scale_Factor)
            Dim new_Height As Integer = CInt(old_Height * zoom_Scale_Factor)

            Dim mouse_X As Integer = e.X - old_Left
            Dim mouse_Y As Integer = e.Y - old_Top

            Dim relative_X As Single = CSng(mouse_X / old_Width)
            Dim relative_Y As Single = CSng(mouse_Y / old_Height)

            Dim new_Left As Integer = CInt(old_Left - (new_Width - old_Width) * relative_X)
            Dim new_Top As Integer = CInt(old_Top - (new_Height - old_Height) * relative_Y)

            Picture_Box_1.Width = new_Width
            Picture_Box_1.Height = new_Height
            Picture_Box_1.Left = new_Left
            Picture_Box_1.Top = new_Top

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1173: new size: " & new_Width.ToString & "-" & new_Height.ToString & " " & new_Left.ToString & "-" & new_Top.ToString)

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location

            zoom_Scale = If(zoom_Scale = 0, 1, zoom_Scale) * zoom_Scale_Factor
            lbl_Zoom.Text = "" & zoom_Scale.ToString("F2")
        Else
            Select Case e.Delta
                Case Is < 0
                    ReadShowMediaFile("ReadNextFile")
                Case Is > 0
                    ReadShowMediaFile("ReadPrevFile")
                Case 0
                    Select Case e.Button
                        Case MouseButtons.Left
                            ReadShowMediaFile("ReadNextFile") ' next
                        Case MouseButtons.Right
                            If Not is_WebBrowser_Visible Then
                                ReadShowMediaFile("ReadPrevFile")
                            End If
                        Case Windows.Forms.MouseButtons.Middle
                            RenameCurrentFile()
                        Case Windows.Forms.MouseButtons.XButton1
                            ReadShowMediaFile("ReadNextFile")
                        Case Windows.Forms.MouseButtons.XButton2
                            ReadShowMediaFile("ReadPrevFile")
                    End Select
            End Select
        End If

    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
            If Current_Folder_Path IsNot Nothing Then SaveSetting(App_name, Second_App_Name, "ImageFolder", Current_Folder_Path)
            If Not current_File_Index = 0 Then SaveSetting(App_name, Second_App_Name, "LastCounter", current_File_Index.ToString)

            SaveSetting(App_name, Second_App_Name, "chkTopMost", If(chkbox_Top_Most.Checked, "1", "0"))
            For z = 0 To 9
                Try
                    SaveSetting(App_name, Second_App_Name, "MoveOn" & z.ToString, Hardkeys_to_move_mediafile(z).ToString)
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1180: ERR: " & ex.Message)
                Finally
                    Hardkeys_to_move_mediafile(z) = Nothing
                End Try
            Next

            SaveSetting(App_name, Second_App_Name, "Is_Russian_Language", If(Is_Russian_Language, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "FirstRun", "0")
            SaveSetting(App_name, Second_App_Name, "CopyMode", If(Is_Copying_not_Moving, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "isPerspective", If(Is_Pespective, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "TableOpened", If(Table_Form.Visible, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "RunsCount", (app_Run_Count + 1).ToString)
            SaveSetting(App_name, Second_App_Name, "mediaViewedCount", (media_View_Count).ToString)
            SaveSetting(App_name, Second_App_Name, "SortDir", (cmbox_Sort.SelectedIndex).ToString)
            SaveSetting(App_name, Second_App_Name, "color_scheme", (Form_Color_Scheme).ToString)
            SaveSetting(App_name, Second_App_Name, "RecentMediaFiles", String.Join("|", recent_Media_File_List))

            SaveSetting(App_name, Second_App_Name, "ShowPictureSizes", If(Is_to_show_picture_sizes, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "ShowFileSizes", If(Is_to_show_file_sizes, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "ShowFileDates", If(Is_to_show_file_datetime, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "IsVideoLoop", If(Is_Video_Loop, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "NoRequestBeforeFileOperation", If(Is_no_request_before_file_operation, "1", "0"))

            SaveSetting(App_name, Second_App_Name, "Picture_Box_Width_At_Panel", Picture_Box_Width_At_Panel.ToString)
            SaveSetting(App_name, Second_App_Name, "Picture_Box_Height_At_Panel", Picture_Box_Height_At_Panel.ToString)

            If Me.Top >= 0 Then SaveSetting(App_name, Second_App_Name, "AppTop", Me.Top.ToString)
            If Me.Left >= 0 Then SaveSetting(App_name, Second_App_Name, "AppLeft", Me.Left.ToString)
            If Me.Height >= main_form_position_Limit_Width_Low Then SaveSetting(App_name, Second_App_Name, "AppHeight", Me.Height.ToString)
            If Me.Width >= main_form_position_Limit_Height_Low Then SaveSetting(App_name, Second_App_Name, "AppWidth", Me.Width.ToString)

            SaveSetting(App_name, Second_App_Name, "VideoVolume", video_Volume_Level.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
            SaveSetting(App_name, Second_App_Name, "VideoMuted", If(is_Video_Muted, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "RecentFolders", String.Join("|", recent_Folder_List))

            SaveSetting(App_name, Second_App_Name, "UseIndependentThreadForOperationsWithFiles", If(Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked, "1", "0"))

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1190: settings are saved")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1200: ERR: " & ex.Message)
        End Try

        If BgWorker.IsBusy Then
            BgWorker.CancelAsync()
            Dim bgworker_Cancel_Timeout As Integer = 1000
            Dim bgworker_Cancel_StartTime As DateTime = DateTime.Now
            While BgWorker.IsBusy AndAlso (DateTime.Now - bgworker_Cancel_StartTime).TotalMilliseconds < bgworker_Cancel_Timeout
                Thread.Sleep(10)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1210: BgWorker try to CancelAsync")
            End While
        End If

        If FileOperationWorker.IsBusy Then
            FileOperationWorker.CancelAsync()
            Dim file_operation_cancel_timeout As Integer = 5000
            Dim file_operation_cancel_start_time As DateTime = DateTime.Now
            While FileOperationWorker.IsBusy AndAlso (DateTime.Now - file_operation_cancel_start_time).TotalMilliseconds < file_operation_cancel_timeout
                Thread.Sleep(10)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1220: FileOperationWorker try to CancelAsync")
            End While
        End If

        SlideShowStop()
        SlideShowTimer.Dispose()
        StopGifLoopPlayback()
        gif_Restart_Timer.Dispose()
        If toolTip IsNot Nothing Then toolTip.Dispose()

        If Web_Browser IsNot Nothing Then
            Web_Browser.DocumentText = ""
            Web_Browser.Dispose()
        End If

        StopVlcPlayback()
        If vlc_Media_Player IsNot Nothing Then
            Try
                vlc_Media_Player.Dispose()
            Catch
            End Try
            vlc_Media_Player = Nothing
        End If
        If vlc_Video_View IsNot Nothing Then
            Try
                vlc_Video_View.Dispose()
            Catch
            End Try
            vlc_Video_View = Nothing
        End If
        If libVlc IsNot Nothing Then
            Try
                libVlc.Dispose()
            Catch
            End Try
            libVlc = Nothing
        End If

        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
        If Picture_Box_1.BackgroundImage IsNot Nothing Then Picture_Box_1.BackgroundImage?.Dispose()
        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
        If Picture_Box_2.BackgroundImage IsNot Nothing Then Picture_Box_2.BackgroundImage?.Dispose()
        If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream?.Dispose()
        If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream?.Dispose()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1230: form is closed")
    End Sub

    Private Sub Form1_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1240: keyb: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

    Public Function GetWas_slideshow() As Boolean
        Return Is_slide_show_mode
    End Function

    Public Sub KeybUse(e As KeyEventArgs, was_Slide_Show_Mode As Boolean)
        SlideShowStop()
        is_Slide_Show_Random_Mode = False

        If cmbox_Media_Folder.Visible AndAlso Me.cmbox_Media_Folder.Focused Then
            If e.KeyCode = Keys.Enter AndAlso cmbox_Media_Folder.Text <> "" Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1300: Enter pressed")
                Current_Folder_Path = cmbox_Media_Folder.Text
                ReadShowMediaFile("ReadFolderAndFile")
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1302: key skip - in editing")
            End If
            Exit Sub
        End If

        If e.Shift Then
            Select Case e.KeyCode
                Case Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1600: +100")
                    current_File_Index += 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+100 файлов", "+100 files")
                Case Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1610: -100")
                    current_File_Index -= 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-100 файлов", "-100 files")
            End Select
        Else
            Select Case e.KeyCode
                Case Keys.N, Keys.Space, Keys.Right, Keys.BrowserForward, Keys.Next, Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1310: to next file")
                    ReadShowMediaFile("ReadNextFile")
                Case Keys.P, Keys.Left, Keys.B, Keys.BrowserBack, Keys.Back, Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1320: to prev file")
                    ReadShowMediaFile("ReadPrevFile")
                Case Keys.Y
                    ReadShowMediaFile("ReadForRandom")
                Case Keys.S
                    SetSlideShow()
                Case Keys.F6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1340: to rename")
                    If Not String.IsNullOrEmpty(Current_File_Name) Then
                        RenameCurrentFile()
                    Else
                        lbl_Status.Text = If(Is_Russian_Language, "! Нет файла для переименования", "! No file to rename")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1350: No file to rename")
                    End If

                Case Keys.F7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1645: F7 - Toggle fullscreen")
                    is_Full_Screen_Mode = Not is_Full_Screen_Mode

                    SetViewSizes()

                Case Keys.F11
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1645: F11 - Toggle super fullscreen")
                    is_Full_Screen_Mode = Not is_Full_Screen_Mode
                    is_Super_Full_Screen_Mode = Not is_Super_Full_Screen_Mode
                    SetViewSizes()

                Case Keys.I, Keys.F5
                    SetRandomSlideShow()
                Case Keys.Home, Keys.H, Keys.BrowserHome
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1370: to first file")
                    current_File_Index = 0
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "первый файл", "first file")
                Case Keys.End, Keys.E, Keys.L, Keys.BrowserStop
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1380: to last file")
                    current_File_Index = total_File_Count - 1
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "последний файл", "last file")
                Case Keys.F, Keys.F4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1385: choose file")
                    Choose_file()
                Case Keys.N
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1386: jump to number")
                    Jump_To_file_Number()
                Case Keys.D, Keys.Delete
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1390: to delete")
                    ReadShowMediaFile("DeleteFile")
                Case Keys.D1, Keys.NumPad1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1400: 01")
                    PoMove(1)
                Case Keys.D2, Keys.NumPad2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1410: 02")
                    PoMove(2)
                Case Keys.D3, Keys.NumPad3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1420: 03")
                    PoMove(3)
                Case Keys.D4, Keys.NumPad4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1430: 04")
                    PoMove(4)
                Case Keys.D5, Keys.NumPad5
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1440: 05")
                    PoMove(5)
                Case Keys.D6, Keys.NumPad6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1450: 06")
                    PoMove(6)
                Case Keys.D7, Keys.NumPad7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1460: 07")
                    PoMove(7)
                Case Keys.D8, Keys.NumPad8
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1470: 08")
                    PoMove(8)
                Case Keys.D9, Keys.NumPad9
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1480: 09")
                    PoMove(9)
                Case Keys.D0, Keys.NumPad0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1490: 0")
                    PoMove(10)
                Case Keys.R
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1500: Rotate")
                    Try
                        If is_Second_PictureBox_Active Then
                            If is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                Picture_Box_2.Image.RotateFlip(RotateFlipType.Rotate90FlipNone)
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1510: P2 Rotated")
                            End If
                        Else
                            If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                Picture_Box_1.Image.RotateFlip(RotateFlipType.Rotate90FlipNone)
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1520: P1 Rotated")
                            End If
                        End If
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: ERR: " & ex.Message)
                        MsgBox("E012 " & ex.Message)
                    End Try
                Case Keys.T
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: Rev Rotate")
                    Try
                        If is_Second_PictureBox_Active Then
                            If is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                                Picture_Box_2.Image.RotateFlip(RotateFlipType.Rotate270FlipNone)
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: P2 Rotated")
                            End If
                        Else
                            If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                                Picture_Box_1.Image.RotateFlip(RotateFlipType.Rotate270FlipNone)
                                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: P1 Rotated")
                            End If
                        End If
                    Catch ex As Exception
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1570: ERR: " & ex.Message)
                        MsgBox("E013 " & ex.Message)
                    End Try
                Case Keys.Up
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1580: -10")
                    current_File_Index -= 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-10 файлов", "-10 files")
                Case Keys.Down
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1590: +10")
                    current_File_Index += 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+10 файлов", "+10 files")
                Case Keys.F1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1620: F1 help")
                    lbl_Help_Info.Visible = True
                    lbl_Help_Info.BringToFront()
                Case Keys.F2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F2")
                    Table_Form.PrepareForDisplay()
                    Table_Form.ShowDialog(Me)
                Case Keys.F3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F5")
                    ShowImagePanelForm()
                Case Keys.U
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1640: UnDo")
                    Undo()
                Case Keys.Escape, Keys.X, Keys.Q
                    If is_Full_Screen_Mode Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1641: ESC to normal")
                        is_Full_Screen_Mode = False
                        SetViewSizes()
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1642: ESC to close")
                        Me.Close()
                    End If
            End Select
        End If
    End Sub

    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1660: Form MouseDown")
        MouseUse(e)
    End Sub

    Private Sub Form1_MouseWheel(sender As Object, e As MouseEventArgs) Handles Me.MouseWheel
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1670: Form MouseWheel")
        MouseUse(e)
    End Sub

    Private Sub FirstRun_Click(sender As Object, e As EventArgs) Handles lbl_Help_Info.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1820: lbl_Help_Info clicked and hidden")
        lbl_Help_Info.Visible = False
        lbl_Help_Info.Hide()
    End Sub

    Private Sub lbl_Info_LinkClicked(sender As Object, e As System.Windows.Forms.LinkLabelLinkClickedEventArgs) Handles lbl_Info.LinkClicked
        System.Diagnostics.Process.Start("mailto:sza@ukr.net?subject=FastMediaSorter for Win:")
    End Sub

    Public Sub LngCh()
        If lbl_Status.Text = "status" Then lbl_Status.Text = ""

        If Is_Russian_Language Then
            lbl_Folder.Text = "Каталог:"
            btn_Prev_File.Text = "<< пред(PgUp)"
            btn_Next_File.Text = "след(PgDn) >>"
            bt_Delete.Text = "удалить (del)"
            btn_Move_Table.Text = "таблица получателей"
            lbl_Help_Info.Text = " Програма для быстрого переноса/копирования изображений по папкам." & Chr(10) & Chr(10) &
                "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. " & Chr(10) &
                "Затем укажите каталог-источник для сортировки. " & Chr(10) &
                "Продвигайтесь по файлам с помощью стрелок, P/N (PgDn/PgUp) или кликов/скролла мыши. " & Chr(10) &
                "Стрелки вверх-вниз: +10-10 и Shift+ PgDn/PgUp: + 100/ - 100 файлов" & Chr(10) &
                "Y- случайно, S- случайное слайдшоу, I- слайдшоу. " & Chr(10) &
                "R/T для поворота картинки. " & Chr(10) &
                "F3 для просмотра пагнли изображений папки. " & Chr(10) &
                "F6 для переименования файла. " & Chr(10) &
                "Или за счет переноса/копирования по папкам клавишами (1,2,3.. - 0). " & Chr(10) &
                "Или за счет удаления текущего файла (del). " & Chr(10) &
                "Окно таблицы можно закрепить и щелкать мышью по колонке с цифрой. " & Chr(10) &
                "(U) -вернуть последный перенесенный файл (удалить скопированный). " & Chr(10) & Chr(10) &
                " Щелкните на этот текст (F1) для того, чтобы он исчез."
            btn_Language.Text = "EN"

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0030: Russian is set")
        Else
            btn_Language.Text = "RU"
            Is_Russian_Language = False
            lbl_Folder.Text = "Folder:"
            btn_Prev_File.Text = "<< (P)rev"
            btn_Next_File.Text = "(N)ext >>"
            bt_Delete.Text = "(D)elete"
            btn_Move_Table.Text = "dest folders table"
            lbl_Help_Info.Text = " Program for fast image sorting." & Chr(10) & Chr(10) &
                "First fill dest folders table for keys: 1,2.. - 0. " & Chr(10) &
                "After set folder with you unsorted files. " & Chr(10) &
                "Go with files by P/N (PgDn/PgUp) keys or mouse clicks/scroll. " & Chr(10) &
                "Up/Down- +10-10 and Shift+ PgDn/PgUp- + 100/ - 100 files" & Chr(10) &
                "Y- random, S- random slide, I- slide. " & Chr(10) &
                "Or move/copy files into dest folders by keys (1,2.. - 0). " & Chr(10) &
                "Or by deleting files (del key). " & Chr(10) &
                "R/T to rotate the image. " & Chr(10) &
                "F3 to see the panel of folder's images. " & Chr(10) &
                "F6 to rename the file. " & Chr(10) &
                "You can lock Window with folders table and click on key numbers. " & Chr(10) &
                "(U)ndo last moved action (delete copying file). " & Chr(10) & Chr(10) &
                " Click on this text (F1) for hide it."

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: English is set")
        End If


    End Sub

    Private Sub ButI_Click(sender As Object, e As EventArgs) Handles btn_Review.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1850: btn_Review clicked")
        FolderSelected()
    End Sub

    Private Sub FolderSelected()
        Is_No_Background_Tasks = False
        If Current_Folder_Path <> "" Then
            ReadShowMediaFile("ReadFolderAndFile")
        Else
            If Is_Russian_Language Then
                MsgBox("Укажите каталог с медиа файлами..")
            Else
                MsgBox("Select folder with media files..")
            End If
        End If
    End Sub

    Public Sub DoKey(ByVal keyIndex As Integer)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1860: DoKey")

        If keyIndex = 0 Then
            ReadShowMediaFile("DeleteFile")
        Else
            PoMove(keyIndex + 1)
        End If
    End Sub

    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs)
        If e.KeyChar = Convert.ToChar(Keys.Enter) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1870: Enter pressed in folder box")

            Current_Folder_Path = Me.cmbox_Media_Folder.Text
            FolderSelected()
        End If
    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles btn_Slideshow.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1880: btn_Slideshow")
        SetSlideShow()
    End Sub

    Private Sub SetSlideShow()

        is_Slide_Show_Random_Mode = False
        Dim slide_show_new_interval = biggest_slide_show_interval
        If Is_slide_show_mode Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub SlideShow_Elapsed() Handles SlideShowTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1890: SlideShowTimer")
        ReadShowMediaFile("InSlideShow")
    End Sub

    Private Sub Form1_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1930: Form_ResizeEnd")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub SlideShowStop()
        SlideShowTimer().Enabled = False
        Is_slide_show_mode = False
        lbl_Slideshow_Time.Visible = False
    End Sub

    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles btn_Move_Table.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1960: btn_MoveTable")
        SlideShowStop()

        ' Check if Table_Form is disposed and recreate it if necessary
        If Table_Form Is Nothing OrElse Table_Form.IsDisposed Then
            Table_Form = New Table_Form()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1961: Table_Form recreated")
        End If

        Table_Form.PrepareForDisplay()
        Table_Form.Show(Me)
    End Sub

    Private Sub Label1_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Folder.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1970: lbl_Folder MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub StatusL_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Status.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1980: lbl_Status MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles btn_Next_Random.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1990: btn_Next_Random")
        SlideShowStop()
        ReadShowMediaFile("ReadForRandom")
    End Sub

    Private Sub Button9_Click(sender As Object, e As EventArgs) Handles btn_Random_Slideshow.Click
        SetRandomSlideShow()
    End Sub

    Private Sub SlideShowStart()
        SlideShowTimer.Enabled = True
        Is_slide_show_mode = True
        lbl_Slideshow_Time.Visible = True
    End Sub

    Private Sub SetRandomSlideShow()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2000: btn_Random_Slideshow")
        is_Slide_Show_Random_Mode = True
        Dim slide_show_new_interval = biggest_slide_show_interval
        If Is_slide_show_mode Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub ChkTopMost_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Top_Most.CheckedChanged
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2010: chkTopMost_CheckedChanged")
        Me.TopMost = chkbox_Top_Most.Checked
    End Sub

    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: btn_Language")

        Is_Russian_Language = Not Is_Russian_Language
        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")
        LngCh()
        Table_Form.LngCh()
        'ReadShowMediaFile("SetFile")
    End Sub

    Private Sub Label3_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Current_File.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2110: lbl_Current_File.MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub TextBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Media_Folder.SelectedIndexChanged
        If Not is_TextBox_Editing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2130: cmbox_MediaFolder SelectedIndexChanged")

            If cmbox_Media_Folder.SelectedIndex >= 0 Then
                Is_No_Background_Tasks = False
                Current_Folder_Path = cmbox_Media_Folder.SelectedItem.ToString()

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2140: currentFolderPath = " & Current_Folder_Path)
                ReadShowMediaFile("ReadFolderAndFile")
            End If

            btn_Next_File.Focus()
        End If
    End Sub

    Private Sub SortComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Sort.SelectedIndexChanged
        If Not is_TextBox_Editing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2150: cmbox_Sort SelectedIndexChanged")

            If Not String.IsNullOrEmpty(Current_Folder_Path) Then
                ReadShowMediaFile("ReadFolderAndFile")
            End If
        End If
    End Sub

    Private Sub Label1_Click(sender As Object, e As EventArgs) Handles lbl_Folder.Click
        If Not String.IsNullOrEmpty(cmbox_Media_Folder.Text) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2240: Folder sent to clipboard")
            CopyTextToClipboard(cmbox_Media_Folder.Text, lbl_Status, If(Is_Russian_Language, "Имя папки скопировано в буфер", "Folder sent to clipboard"))
        End If
    End Sub

    Private Sub StatusL_Click(sender As Object, e As EventArgs) Handles lbl_Status.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2300: Visibility set: " & If(is_PictureBox1_Visible, "P1-YES ", "P1-NO ") & If(is_PictureBox2_Visible, "P2-YES ", "P2-NO ") & If(is_WebBrowser_Visible, "WB-YES ", "WB-NO "))
    End Sub

    Private Sub Picture_Box_1_KeyDown(sender As Object, e As KeyEventArgs) Handles Picture_Box_1.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1248: keyb on P1: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

    Private Sub Picture_Box_2_KeyDown(sender As Object, e As KeyEventArgs) Handles Picture_Box_2.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1249: keyb on P2: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

    Function IsRunningAsAdministrator() As Boolean
        Dim identity = WindowsIdentity.GetCurrent()
        Dim principal = New WindowsPrincipal(identity)
        Return principal.IsInRole(WindowsBuiltInRole.Administrator)
    End Function

    ' Add this function to check .jpg association
    Private Function IsJpgAssociatedWithThisApp() As Boolean
        Try
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2410: check for JPG associacion")
            Using key = Registry.ClassesRoot.OpenSubKey(".jpg")
                If key Is Nothing Then Return False
                Dim progId = key.GetValue("")?.ToString()
                If String.IsNullOrEmpty(progId) Then Return False
                Using progKey = Registry.ClassesRoot.OpenSubKey(progId & "\shell\open\command")
                    If progKey Is Nothing Then Return False
                    Dim command = progKey.GetValue("")?.ToString()
                    If String.IsNullOrEmpty(command) Then Return False
                    Dim exePath = Application.ExecutablePath.ToLowerInvariant()
                    Return command.ToLowerInvariant().Contains(exePath)
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Sub AssociateJpgWithThisApp()
        Try
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2420: JPG associacion..")
            Dim exePath = Application.ExecutablePath
            Dim progId = "FastMediaSorter.jpg"
            ' Set ProgID
            Using progKey = Registry.ClassesRoot.CreateSubKey(progId)
                progKey.SetValue("", "JPEG Image - FastMediaSorter")
                Using shellKey = progKey.CreateSubKey("shell\open\command")
                    shellKey.SetValue("", """" & exePath & """ ""%1""")
                End Using
            End Using
            ' Set .jpg default
            Using extKey = Registry.ClassesRoot.CreateSubKey(".jpg")
                extKey.SetValue("", progId)
            End Using
        Catch ex As Exception
            MessageBox.Show(If(Is_Russian_Language, "Ошибка ассоциации: ", "Failed to set association: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR with JPG associacion.." & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferJpgAssociation()
        If IsRunningAsAdministrator() AndAlso Not IsJpgAssociatedWithThisApp() Then
            Dim msg = If(Is_Russian_Language, "Ассоциировать .JPG файлы с этой программой?", "Associate .JPG files with this application?")
            If MessageBox.Show(msg, "Association", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                AssociateJpgWithThisApp()
            End If
        End If
    End Sub

    Private Function AreImageTypesAssociatedWithThisApp() As Boolean
        Return IsExtensionAssociatedWithThisApp(".jpg") AndAlso
           IsExtensionAssociatedWithThisApp(".png") AndAlso
           IsExtensionAssociatedWithThisApp(".gif")
    End Function

    Private Function IsExtensionAssociatedWithThisApp(ext As String) As Boolean
        Try
            Using key = Registry.ClassesRoot.OpenSubKey(ext)
                If key Is Nothing Then Return False
                Dim progId = key.GetValue("")?.ToString()
                If String.IsNullOrEmpty(progId) Then Return False
                Using progKey = Registry.ClassesRoot.OpenSubKey(progId & "\shell\open\command")
                    If progKey Is Nothing Then Return False
                    Dim command = progKey.GetValue("")?.ToString()
                    If String.IsNullOrEmpty(command) Then Return False
                    Dim exePath = Application.ExecutablePath.ToLowerInvariant()
                    Return command.ToLowerInvariant().Contains(exePath)
                End Using
            End Using
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2430: Ext associaciated")
        Catch
            Return False
        End Try
    End Function

    Private Sub AssociateImageTypesWithThisApp()
        AssociateExtensionWithThisApp(".jpg", "FastMediaSorter.jpg", "JPEG Image - FastMediaSorter")
        AssociateExtensionWithThisApp(".png", "FastMediaSorter.png", "PNG Image - FastMediaSorter")
        AssociateExtensionWithThisApp(".gif", "FastMediaSorter.gif", "GIF Image - FastMediaSorter")

        MessageBox.Show(If(Is_Russian_Language, "Ассоциации установлены. Возможно потребуется перезапустить Проводник или Windows.", "Associations set. You may need to restart Explorer or Windows for changes to take effect."), "Association", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub AssociateExtensionWithThisApp(ext As String, progId As String, description As String)
        Try
            Dim exePath = Application.ExecutablePath
            Using progKey = Registry.ClassesRoot.CreateSubKey(progId)
                progKey.SetValue("", description)
                Using shellKey = progKey.CreateSubKey("shell\open\command")
                    shellKey.SetValue("", """" & exePath & """ ""%1""")
                End Using
            End Using

            Using extKey = Registry.ClassesRoot.CreateSubKey(ext)
                extKey.SetValue("", progId)
            End Using
        Catch ex As Exception
            MessageBox.Show(If(Is_Russian_Language, "Ошибка ассоциации: ", "Failed to set association: ") & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ERR Ext associaciated: " & ex.Message)
        End Try
    End Sub

    Private Sub CheckAndOfferImageAssociations()
        If GetSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "0") = "0" AndAlso
            IsRunningAsAdministrator() AndAlso
            Not AreImageTypesAssociatedWithThisApp() Then

            Dim msg = If(Is_Russian_Language, "Ассоциировать .JPG, .PNG, .GIF файлы с этой программой?", "Associate .JPG, .PNG, .GIF files with this application?")
            If MessageBox.Show(msg, "Association", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                AssociateImageTypesWithThisApp()
            End If

            SaveSetting(App_name, Second_App_Name, "UserAlreadyAskedForAssociations", "1")
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2440: asked for association")
        End If
    End Sub

    Public Sub AssociateAllImageFormatsWithThisApp()
        Dim all_Image_Extensions() As String = {
            ".jpg", ".jpeg", ".gif", ".png", ".bmp", ".tiff",
            ".ico", ".wmf", ".emf", ".exif",
            ".webp", ".heic", ".avif", ".svg"
        }

        Dim failed As New List(Of String)
        Dim exe_Path As String = Application.ExecutablePath

        For Each ext In all_Image_Extensions
            Try
                Dim clean As String = ext.TrimStart("."c)
                Dim prog_Id As String = "FastMediaSorter." & clean
                Dim description As String = clean.ToUpper() & " Image - FastMediaSorter"

                ' HKCU\Software\Classes — не требует прав администратора, работает для текущего пользователя
                Using classes_Key = Registry.CurrentUser.OpenSubKey("Software\Classes", True)
                    Using prog_Key = classes_Key.CreateSubKey(prog_Id)
                        prog_Key.SetValue("", description)
                        Using shell_Key = prog_Key.CreateSubKey("shell\open\command")
                            shell_Key.SetValue("", """" & exe_Path & """ ""%1""")
                        End Using
                        Using icon_Key = prog_Key.CreateSubKey("DefaultIcon")
                            icon_Key.SetValue("", """" & exe_Path & """,0")
                        End Using
                    End Using
                    Using ext_Key = classes_Key.CreateSubKey(ext)
                        ext_Key.SetValue("", prog_Id)
                    End Using
                End Using
            Catch ex As Exception
                failed.Add(ext)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2501: Error registering " & ext & ": " & ex.Message)
            End Try
        Next

        ' Уведомить shell об изменении ассоциаций
        SHChangeNotify(&H8000000, &H1000, IntPtr.Zero, IntPtr.Zero)

        Dim registered_Count As Integer = all_Image_Extensions.Length - failed.Count
        If failed.Count = 0 Then
            MessageBox.Show(
                If(Is_Russian_Language,
                   "Успешно зарегистрировано " & registered_Count.ToString() & " форматов:" & vbCrLf &
                   String.Join("  ", all_Image_Extensions) & vbCrLf & vbCrLf &
                   "Изменения применены для текущего пользователя.",
                   registered_Count.ToString() & " formats registered:" & vbCrLf &
                   String.Join("  ", all_Image_Extensions) & vbCrLf & vbCrLf &
                   "Changes applied for current user."),
                If(Is_Russian_Language, "Регистрация завершена", "Registration complete"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(
                If(Is_Russian_Language,
                   "Зарегистрировано: " & registered_Count.ToString() & vbCrLf &
                   "Ошибок: " & failed.Count.ToString() & " (" & String.Join(", ", failed) & ")",
                   "Registered: " & registered_Count.ToString() & vbCrLf &
                   "Errors: " & failed.Count.ToString() & " (" & String.Join(", ", failed) & ")"),
                If(Is_Russian_Language, "Регистрация", "Registration"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Sub Btn_Panel_Click(sender As Object, e As EventArgs) Handles btn_Panel.Click
        SlideShowStop()
        ShowImagePanelForm()
    End Sub

    Private Sub ShowImagePanelForm()
        If Image_Panel_Form Is Nothing OrElse Image_Panel_Form.IsDisposed Then
            Image_Panel_Form = New Image_Panel_Form()
            AddHandler Image_Panel_Form.FormClosed, AddressOf Image_Panel_Form_FormClosed
        End If
        Image_Panel_Form.PrepareForDisplay()
        Image_Panel_Form.ShowDialog(Me)
    End Sub

    Private Sub Main_Form_Deactivate(sender As Object, e As EventArgs) Handles Me.Deactivate
    End Sub

    Private Sub btn_choose_file_Click(sender As Object, e As EventArgs) Handles btn_choose_file.Click
        Choose_file()
    End Sub

    Private Sub Choose_file()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2450: btn_choose_file clicked")
        SlideShowStop()
        Using openFileDialog As New OpenFileDialog()
            ' Build video extensions string for filter
            Dim videoExtensions As String = String.Join(";", video_File_Extensions.Select(Function(ext) "*" & ext))
            Dim imageExtensions As String = "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.heic;*.avif;*.svg"

            openFileDialog.Filter = "All Supported Files|" & imageExtensions & ";" & videoExtensions &
                               "|Image Files|" & imageExtensions &
                               "|Video Files|" & videoExtensions &
                               "|JPEG Files|*.jpg;*.jpeg|PNG Files|*.png|GIF Files|*.gif|BMP Files|*.bmp|WebP Files|*.webp|HEIC Files|*.heic|AVIF Files|*.avif|SVG Files|*.svg"
            openFileDialog.InitialDirectory = If(String.IsNullOrEmpty(Current_Folder_Path), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Current_Folder_Path)
            openFileDialog.Title = If(Is_Russian_Language, "Выберите медиафайл", "Select a media file")
            If openFileDialog.ShowDialog() = DialogResult.OK Then
                Dim selected_File_Path As String = openFileDialog.FileName
                Dim selected_Folder_Path As String = Path.GetDirectoryName(selected_File_Path)

                ' Set up the necessary state for external input processing
                Current_Folder_Path = selected_Folder_Path
                Current_Image_Path = selected_File_Path
                Current_File_Name = selected_File_Path

                ' Update the folder combo box
                is_TextBox_Editing = True
                cmbox_Media_Folder.Text = Current_Folder_Path
                is_TextBox_Editing = False

                ' Mark as external input to ensure proper processing
                is_External_Input_Received = True
                was_External_Input_Previously = True

                ' Reset file index and count for the new selection
                current_File_Index = 0
                total_File_Count = 1

                ReadShowMediaFile("ReadFolderAndKnownFile")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2455: File chosen - " & selected_File_Path)
            End If
        End Using
    End Sub


    Private Sub lbl_File_Number_Click(sender As Object, e As EventArgs) Handles lbl_File_Number.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2460: jump to file clicked")
        SlideShowStop()
        Jump_To_file_Number()
    End Sub


    Private Sub Jump_To_file_Number()

        Dim fileNumber As Integer
        Dim take_number As String

        take_number = InputBox(If(Is_Russian_Language, "Введите номер файла:", "Enter file number:"), If(Is_Russian_Language, "Перейти к файлу", "Jump To File Number"), (current_File_Index + 1).ToString, 1, total_File_Count)

        If Integer.TryParse(take_number, fileNumber) Then

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2465: Jumping to file number " & fileNumber.ToString())

            If fileNumber > 0 AndAlso fileNumber <= total_File_Count Then
                ' Adjust for zero-based index
                current_File_Index = fileNumber - 1
                ReadShowMediaFile("ReadForJumpToFile")
            Else
                MessageBox.Show(If(Is_Russian_Language, "Номер файла вне диапазона.", "File number out of range."), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If

        Else
            MessageBox.Show("Invalid file number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    Private Sub StartGifLoopPlayback(image As Image)
        StopGifLoopPlayback()

        If image Is Nothing Then Return

        Try
            If Not image.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Gif) Then Return

            Dim frameDimension As New System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList(0))
            Dim frameCount As Integer = image.GetFrameCount(frameDimension)
            If frameCount <= 1 Then Return

            Dim durationMs As Integer = 0
            Try
                Dim item As System.Drawing.Imaging.PropertyItem = image.GetPropertyItem(&H5100)
                If item IsNot Nothing AndAlso item.Value IsNot Nothing AndAlso item.Len >= frameCount * 4 Then
                    For i As Integer = 0 To frameCount - 1
                        Dim delay As Integer = BitConverter.ToInt32(item.Value, i * 4)
                        If delay <= 0 Then delay = 10
                        durationMs += delay * 10
                    Next
                End If
            Catch
                durationMs = 0
            End Try

            If durationMs <= 0 Then durationMs = 1000

            gif_Restart_Image_Ref = image
            gif_Total_Duration_Ms = durationMs
            gif_Restart_Timer.Interval = Math.Max(100, gif_Total_Duration_Ms)
            gif_Restart_Timer.Start()
        Catch
            StopGifLoopPlayback()
        End Try
    End Sub

    Private Sub StopGifLoopPlayback()
        gif_Restart_Timer.Stop()
        gif_Total_Duration_Ms = 0
        gif_Restart_Image_Ref = Nothing
    End Sub

    Private Sub Gif_Restart_Timer_Tick(sender As Object, e As EventArgs) Handles gif_Restart_Timer.Tick
        If gif_Restart_Image_Ref Is Nothing Then
            StopGifLoopPlayback()
            Return
        End If

        Try
            Dim frameDimension As New System.Drawing.Imaging.FrameDimension(gif_Restart_Image_Ref.FrameDimensionsList(0))
            gif_Restart_Image_Ref.SelectActiveFrame(frameDimension, 0)

            If is_PictureBox1_Visible AndAlso Object.ReferenceEquals(Picture_Box_1.Image, gif_Restart_Image_Ref) Then
                Picture_Box_1.Invalidate()
            ElseIf is_PictureBox2_Visible AndAlso Object.ReferenceEquals(Picture_Box_2.Image, gif_Restart_Image_Ref) Then
                Picture_Box_2.Invalidate()
            Else
                StopGifLoopPlayback()
            End If
        Catch
            StopGifLoopPlayback()
        End Try
    End Sub

    Private Sub lbl_Zoom_MouseDown(sender As Object, e As MouseEventArgs) Handles lbl_Zoom.MouseDown
        SkipZoom()
    End Sub

    Private Sub Picture_Box_1_MouseMove(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseMove
        Pic_MouseMove(sender, e)
    End Sub

    Private Sub Picture_Box_2_MouseMove(sender As Object, e As MouseEventArgs) Handles Picture_Box_2.MouseMove
        Pic_MouseMove(sender, e)
    End Sub

    Private Sub Pic_MouseMove(sender As Object, e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then
            If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2470: pic_MouseMove - no picture box visible")
                Exit Sub
            End If

            ' Add drag functionality when zoomed
            If zoom_Scale = 0 OrElse zoom_Scale > 1 Then
                If Not is_Dragging Then
                    ' Check if mouse has moved enough to be considered a drag
                    Dim drag_Threshold As Integer = 5 ' Increased threshold
                    Dim distance_Moved As Double = Math.Sqrt((e.X - mouse_Down_Start_Point.X) ^ 2 + (e.Y - mouse_Down_Start_Point.Y) ^ 2)

                    If distance_Moved >= drag_Threshold Then
                        ' Start dragging - store the original PictureBox position
                        is_Dragging = True
                        original_PictureBox_Left = Picture_Box_1.Left
                        original_PictureBox_Top = Picture_Box_1.Top
                        drag_Start_Point = e.Location ' Use current mouse position as start point
                        last_Drag_Update_Time = DateTime.Now ' Initialize the timer
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2475: pic_MouseMove - drag started at " & drag_Start_Point.ToString())
                    End If
                End If

                If is_Dragging Then
                    ' Check if enough time has passed since last update
                    Dim current_Time As DateTime = DateTime.Now
                    If (current_Time - last_Drag_Update_Time).TotalMilliseconds >= DRAG_UPDATE_INTERVAL_MS Then

                        ' Calculate movement delta from the original mouse down position
                        Dim delta_X As Integer = e.X - mouse_Down_Start_Point.X
                        Dim delta_Y As Integer = e.Y - mouse_Down_Start_Point.Y

                        ' Only move if there's actual movement to avoid unnecessary updates
                        If delta_X <> 0 OrElse delta_Y <> 0 Then
                            ' Calculate new position based on original position plus total movement
                            Dim new_Left As Integer = original_PictureBox_Left + delta_X
                            Dim new_Top As Integer = original_PictureBox_Top + delta_Y

                            ' Apply the new position to both picture boxes
                            Picture_Box_1.Left = new_Left
                            Picture_Box_1.Top = new_Top
                            Picture_Box_2.Left = new_Left
                            Picture_Box_2.Top = new_Top

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2476: pic_MouseMove - dragging to " & new_Left.ToString() & "," & new_Top.ToString())
                        End If

                        ' Update the last update time
                        last_Drag_Update_Time = current_Time

                        ' Set focus to the active picture box
                        If sender Is Picture_Box_1 Then
                            Picture_Box_1.Focus()
                        ElseIf sender Is Picture_Box_2 Then
                            Picture_Box_2.Focus()
                        End If
                    End If
                    ' If not enough time has passed, we simply ignore this mouse move event for dragging
                End If
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2480: pic_MouseMove - mouse moved in " & sender.ToString())
        Else
            ' Reset dragging when mouse button is released
            If is_Dragging Then
                is_Dragging = False
                last_Drag_Update_Time = DateTime.MinValue ' Reset the timer
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2477: pic_MouseMove - drag ended")
            End If
        End If
    End Sub

End Class
