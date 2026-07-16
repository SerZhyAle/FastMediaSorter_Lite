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

    Private Sub InitializeExtensionLists()
        all_Supported_Extensions.UnionWith(Image_File_Extensions)
        all_Supported_Extensions.UnionWith(video_File_Extensions)
        all_Supported_Extensions.UnionWith(web_specific_image_extensions)
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
        toolTip.SetToolTip(btn_Review, If(Is_Russian_Language, "Перечитать текущую папку - вдруг там что-то поменялось за вашей спиной.", "Reload the current folder - in case something changed behind your back."))
        toolTip.SetToolTip(btn_Panel, If(Is_Russian_Language, "Показать панель изображений (F3)", "Show the image panel (F3)"))
        toolTip.SetToolTip(btn_Full_Screen, If(Is_Russian_Language, "Полноэкранный режим - картинка во весь экран и ничего лишнего.", "Toggle fullscreen mode - the image, the whole image, and nothing but the image."))
        toolTip.SetToolTip(btn_Prev_File, If(Is_Russian_Language, "Предыдущий файл (Стрелка влево, PgUp)", "Previous file (Left Arrow, PgUp)"))
        toolTip.SetToolTip(btn_Next_File, If(Is_Russian_Language, "Следующий файл (Стрелка вправо, PgDn)", "Next file (Right Arrow, PgDn)"))
        toolTip.SetToolTip(btn_Next_Random, If(Is_Russian_Language, "Случайный файл (Y) - пусть судьба выбирает за вас.", "Random file (Y) - let fate pick for you."))
        toolTip.SetToolTip(btn_Random_Slideshow, If(Is_Russian_Language, "Случайное слайд-шоу (I, F5)", "Random slideshow (I, F5)"))
        toolTip.SetToolTip(btn_Slideshow, If(Is_Russian_Language, "Слайд-шоу (S)", "Slideshow (S)"))
        toolTip.SetToolTip(btn_Move_Table, If(Is_Russian_Language, "Настройки: папки-получатели, OCR и перевод (F2)", "Settings: destination folders, OCR & translation (F2)"))
        toolTip.SetToolTip(btn_Rename, If(Is_Russian_Language, "Переименовать файл (F6)", "Rename file (F6)"))
        toolTip.SetToolTip(bt_Delete, If(Is_Russian_Language, "Удалить файл (Del) - пути назад почти нет, так что прицеливайтесь.", "Delete the file (Del) - there's almost no going back, so aim carefully."))
        toolTip.SetToolTip(btn_Language, If(Is_Russian_Language, "Переключить язык на английский", "Switch language to Russian"))
        toolTip.SetToolTip(chkbox_Top_Most, If(Is_Russian_Language, "Поверх всех окон - чтобы ничто не смело его заслонить.", "Always on top - so nothing dares cover it."))
        toolTip.SetToolTip(btn_choose_file, If(Is_Russian_Language, "Выбрать файл..", "Choose file.."))

        ' --- ComboBoxes and Labels ---
        toolTip.SetToolTip(cmbox_Sort, If(Is_Russian_Language, "Порядок сортировки файлов", "File sort order"))
        toolTip.SetToolTip(cmbox_Media_Folder, If(Is_Russian_Language, "Текущая папка. Введите путь и нажмите Enter для перехода.", "Current folder. Type a path and press Enter to navigate."))
        toolTip.SetToolTip(lbl_Folder, If(Is_Russian_Language, "Нажмите, чтобы скопировать путь к папке (вдруг пригодится).", "Click to copy the folder path (just in case)."))
        toolTip.SetToolTip(lbl_Current_File, If(Is_Russian_Language, "Нажмите, чтобы скопировать путь к файлу", "Click to copy the file path"))
        toolTip.SetToolTip(lbl_Status, If(Is_Russian_Language, "Статус текущей операции", "Status of the current operation"))
        toolTip.SetToolTip(lbl_File_Number, If(Is_Russian_Language, "Номер текущего файла и сколько их всего - чтобы оценить масштаб предстоящего.", "Current file number and the total - so you can grasp the scale of what's ahead."))

        toolTip.SetToolTip(btn_RecentFiles, If(Is_Russian_Language, "Недавние файлы", "Recent files"))

        LocalizeToolbarOverflow()

        ' --- Main Display Area ---
        'Dim mediaControlTooltip As String = If(Is_Russian_Language,
        '"ЛКМ: Следующий файл" & vbCrLf & "ПКМ: Предыдущий файл" & vbCrLf & "СКМ: Переименовать" & vbCrLf & "Колесо мыши: Навигация" & vbCrLf & "Ctrl+Колесо: Масштаб" & vbCrLf & "Alt+Колесо: Сброс масштаба" & vbCrLf & "Двойной клик: Выход из полноэкранного режима",
        '"Left-Click: Next file" & vbCrLf & "Right-Click: Previous file" & vbCrLf & "Middle-Click: Rename" & vbCrLf & "Mouse Wheel: Navigate" & vbCrLf & "Ctrl+Wheel: Zoom" & vbCrLf & "Alt+Wheel: Reset Zoom" & vbCrLf & "Double-Click: Exit fullscreen")

        'toolTip.SetToolTip(Picture_Box_1, mediaControlTooltip)
        'toolTip.SetToolTip(Picture_Box_2, mediaControlTooltip)
        'toolTip.SetToolTip(Web_Browser, mediaControlTooltip)
    End Sub

    Private Sub External_message(receivedData As String)
        Dim argument As String = receivedData.TrimEnd(Chr(0)).Trim()
        argument = Regex.Replace(argument, "(?<!^)(\\\\)+", "\")

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0010: received from new instance: " & argument)

        ' A bare second launch (no file) forwarded by the mutex path: the user just
        ' wants the window back. Restore it with its current content - nothing to load,
        ' and unlike a file open we DO take focus (the launch had no other purpose).
        If String.Equals(argument, Show_Window_Command, StringComparison.OrdinalIgnoreCase) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0013: bare relaunch - restoring window")
            RestoreMainWindow()
            Return
        End If

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

    ''' <summary>Brings the main window back on screen and focuses it - unminimizes if
    ''' needed. Generic single-instance relaunch UX (not share-specific): shared by
    ''' Application_Events.vb's bare-second-launch handling and the Show_Window_Command
    ''' handling above. The window is never hidden to a tray by LITE itself (that
    ''' close-to-tray behavior moved to Share Manager with the share feature), so this
    ''' is just "unminimize + focus", not a tray-restore.</summary>
    Friend Sub RestoreMainWindow()
        Try
            Me.Show()
            If Me.WindowState = FormWindowState.Minimized Then Me.WindowState = FormWindowState.Normal
            Me.Activate()
            Me.BringToFront()
        Catch
        End Try
    End Sub

#If NETFRAMEWORK Then
    ' net48 only: IE11 emulation for the video WebBrowser. The modern build
    ' never instantiates the WebBrowser, so the registry knob is pointless there.
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
#End If

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
#If NETFRAMEWORK Then
        Web_Browser.ObjectForScripting = Me
#End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0003: InitializeExtensionLists")
        InitializeExtensionLists()
#If NETFRAMEWORK Then
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0004: SetWebBrowserCompatibilityMode")
        SetWebBrowserCompatibilityMode()
#End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0009: InitializeFileOperationWorker")
        InitializeFileOperationWorker()
    End Sub

    ' When a requested file is locked by a writer (still downloading), we don't give
    ' up -- we poll until it unlocks (or the deadline passes) and then open it, so a
    ' freshly-downloaded file appears on its own once it's ready.
    Private pending_Unlock_Path As String = Nothing
    Private pending_Unlock_Deadline As DateTime
    Private WithEvents pending_Unlock_Timer As New System.Windows.Forms.Timer() With {.Interval = 800}

    Private Sub Pending_Unlock_Timer_Tick(sender As Object, e As EventArgs) Handles pending_Unlock_Timer.Tick
        If String.IsNullOrEmpty(pending_Unlock_Path) Then
            pending_Unlock_Timer.Stop()
            Return
        End If

        If DateTime.Now > pending_Unlock_Deadline Then
            pending_Unlock_Timer.Stop()
            Dim given_Up As String = pending_Unlock_Path
            pending_Unlock_Path = Nothing
            lbl_Status.Text = If(Is_Russian_Language, "Файл всё ещё занят: " & Path.GetFileName(given_Up), "File still locked: " & Path.GetFileName(given_Up))
            Return
        End If

        ' GetAttributes keeps throwing while a writer holds the file; once it succeeds
        ' the writer has released it (download finished) and we can open it.
        Try
            File.GetAttributes(pending_Unlock_Path)
        Catch
            Return ' still locked -- wait for the next tick
        End Try

        pending_Unlock_Timer.Stop()
        Dim ready_Path As String = pending_Unlock_Path
        pending_Unlock_Path = Nothing
        is_External_Input_Received = True
        is_File_Reseived_From_Outside = True
        ProcessArgument(ready_Path)
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
                ' File.Exists returns False for several different reasons and swallows
                ' them all. GetAttributes throws the REAL one so we can react correctly:
                '   * FileNotFound/DirNotFound -> the file is genuinely gone: fail fast.
                '   * UnauthorizedAccess       -> "access denied": almost always a
                '       sharing violation because the file is still being written by a
                '       downloader (these are fresh \output\* downloads). A real lock
                '       clears in a moment, so a few quick retries; an ACL/elevation
                '       issue won't clear, so don't freeze the UI on it.
                '   * IOException etc.         -> share dropped/busy: retry longer to
                '       let the SMB session re-establish.
                Dim arg_File_Exists As Boolean = File.Exists(argument_For_Path)
                Dim arg_Missing As Boolean = False
                Dim arg_Denied As Boolean = False
                Dim arg_Last_Error As String = ""
                Dim arg_Denied_Tries As Integer = 0
                Dim arg_Net_Tries As Integer = 0
                Do While Not arg_File_Exists
                    Try
                        File.GetAttributes(argument_For_Path)
                        arg_File_Exists = True
                    Catch ex As FileNotFoundException
                        arg_Missing = True : arg_Last_Error = ex.Message : Exit Do
                    Catch ex As DirectoryNotFoundException
                        arg_Missing = True : arg_Last_Error = ex.Message : Exit Do
                    Catch ex As UnauthorizedAccessException
                        arg_Denied = True : arg_Last_Error = ex.Message
                        arg_Denied_Tries += 1
                        If arg_Denied_Tries >= 3 Then Exit Do
                        Threading.Thread.Sleep(250)
                    Catch ex As Exception
                        arg_Last_Error = "[" & ex.GetType().Name & "] " & ex.Message
                        arg_Net_Tries += 1
                        If arg_Net_Tries >= 8 Then Exit Do
                        Threading.Thread.Sleep(250)
                    End Try
                Loop

                If Not arg_File_Exists Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0280: arg not reachable (missing=" & arg_Missing.ToString() & " denied=" & arg_Denied.ToString() & "): [" & argument_For_Path & "] reason=" & arg_Last_Error)
                    If arg_Denied Then
                        ' Locked by a writer (downloading). Watch for it to unlock and
                        ' open it automatically rather than making the user retry.
                        pending_Unlock_Path = argument_For_Path
                        pending_Unlock_Deadline = DateTime.Now.AddSeconds(45)
                        pending_Unlock_Timer.Stop()
                        pending_Unlock_Timer.Start()
                        lbl_Status.Text = If(Is_Russian_Language, "Файл занят, ждём разблокировки (качается?): " & Path.GetFileName(argument_For_Path), "File locked, waiting to open (downloading?): " & Path.GetFileName(argument_For_Path))
                    ElseIf arg_Missing Then
                        lbl_Status.Text = If(Is_Russian_Language, "Файл не найден: " & Path.GetFileName(argument_For_Path), "File not found: " & Path.GetFileName(argument_For_Path))
                    Else
                        lbl_Status.Text = If(Is_Russian_Language, "Сетевой ресурс недоступен, повторите: " & Path.GetFileName(argument_For_Path), "Share unreachable, retry: " & Path.GetFileName(argument_For_Path))
                    End If
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

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(" - - - ")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0000: Form started")
        Me.AllowDrop = True

        ' Build the modern docked layout (reparents the Designer controls into
        ' flow_Toolbar / panel_Status / panel_Media) before anything lays out.
        BuildModernLayout()

        ' Accept dropped media files on the picture/video surfaces, not just the form.
        WireSurfaceDragDrop()

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

        ' Initial native title-bar theme; refined per-image by RecolorChrome.
        ApplyTitleBarTheme(Form_Color_Scheme <> 2)

        ' Paint the chrome immediately for the fixed schemes so an empty startup
        ' window isn't shown with unstyled buttons (dynamic schemes recolour on
        ' the first image via UpdateControlVisibility -> RecolorChrome).
        If Form_Color_Scheme = 1 Then
            Me.BackColor = Color.Black
            RecolorChrome(Color.Black, GetOppositeColor(Color.Black))
        ElseIf Form_Color_Scheme = 2 Then
            Me.BackColor = Color.White
            RecolorChrome(Color.White, GetOppositeColor(Color.White))
        End If

        ' First run (no saved value) follows the Windows display language; after
        ' that the user's saved choice wins.
        Is_Russian_Language = GetSetting(App_name, Second_App_Name, "Is_Russian_Language", If(SystemDefaultIsRussian(), "1", "0")) = "1"
        InitializeTooltips()
        InitializeOcrTranslate()
        InitializeBrowserTranslate()
        InitializeShareEntryPoint()

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Width_At_Panel", "80"), Picture_Box_Width_At_Panel)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Height_At_Panel", "80"), Picture_Box_Height_At_Panel)

        Is_Pespective = GetSetting(App_name, Second_App_Name, "isPerspective", "1") = "1"
        Is_no_request_before_file_operation = GetSetting(App_name, Second_App_Name, "NoRequestBeforeFileOperation", "0") = "1"

        Is_to_show_picture_sizes = GetSetting(App_name, Second_App_Name, "ShowPictureSizes", "1") = "1"
        Is_to_show_file_sizes = GetSetting(App_name, Second_App_Name, "ShowFileSizes", "1") = "1"
        Is_to_show_file_datetime = GetSetting(App_name, Second_App_Name, "ShowFileDates", "1") = "1"
        Is_Video_Loop = GetSetting(App_name, Second_App_Name, "IsVideoLoop", "0") = "1"

        ' Viewer options (see the "Просмотр" tab of the settings window).
        Is_Exif_AutoRotate = GetSetting(App_name, Second_App_Name, "ExifAutoRotate", "1") = "1"
        Is_HighQuality_Scaling = GetSetting(App_name, Second_App_Name, "HighQualityScaling", "1") = "1"
        Is_Show_Info_Overlay = GetSetting(App_name, Second_App_Name, "ShowInfoOverlay", "0") = "1"
        Dim slideshow_Seconds As Integer = 10
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "SlideshowIntervalSec", "10"), slideshow_Seconds)
        If slideshow_Seconds < 1 Then slideshow_Seconds = 1
        If slideshow_Seconds > 120 Then slideshow_Seconds = 120
        Slideshow_Base_Interval_Ms = slideshow_Seconds * 1000

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
        ' New key (not the abandoned "SetOnTop"), default off - so a fresh install or
        ' reinstall starts with the recipients overlay hidden. Applied in Main_Form_Shown.
        Is_Show_Recipients_Overlay = GetSetting(App_name, Second_App_Name, "ShowRecipientsOverlay", "0") = "1"
        ' Default "0" = the wheel flips through files, i.e. a fresh install and an
        ' upgrade both behave exactly as before (spec 4.4 / O-Z-2).
        Zoom_Wheel_Zooms = GetSetting(App_name, Second_App_Name, "WheelZooms", "0") = "1"

        Dim video_Volume_String = GetSetting(App_name, Second_App_Name, "VideoVolume", "1.0")
        video_Volume_Level = ParseVideoVolumeSetting(video_Volume_String, video_Volume_Level)
        is_Video_Muted = GetSetting(App_name, Second_App_Name, "VideoMuted", "0") = "1"

        ' Keys 1..9 -> slots 1..9; key "0" -> slot 10 (from MoveOn0). Slot 0 is not a
        ' runtime destination, so it is left unset (see the save side below).
        For z = 1 To 9
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

    ' The first media at startup - a command-line file/folder (the external-open
    ' case), or the restored last folder - is loaded inside Form1_Load while
    ' is_form_shown is still False, so every Draw_Perspective() call in the load
    ' path is gated off and never runs. The image then shows only the flat scheme
    ' background colour in its pill/letterbox area instead of the perspective bars,
    ' with a visible seam (scrolling looked perfect because navigation always runs
    ' with is_form_shown = True). By the Shown event the form is at its final size
    ' and is_form_shown is True, so draw the bars once here. Draw_Perspective
    ' self-gates (no-op when no picture box is visible or perspective is off).
    Private Sub Main_Form_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        If is_PictureBox1_Visible OrElse is_PictureBox2_Visible Then
            Draw_Perspective()
        End If
        ' Now the form is at its final size and panel_Media exists - build the
        ' recipients overlay if the user left it on.
        ApplyRecipientsOverlay()
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' LITE closes like a normal viewer - no close-to-tray. Sharing (if any) is
        ' owned entirely by Fast Media Sorter: Share Manager, which keeps
        ' running independently; closing LITE never touches it.
        Try
            If Current_Folder_Path IsNot Nothing Then SaveSetting(App_name, Second_App_Name, "ImageFolder", Current_Folder_Path)
            If Not current_File_Index = 0 Then SaveSetting(App_name, Second_App_Name, "LastCounter", current_File_Index.ToString)

            SaveSetting(App_name, Second_App_Name, "chkTopMost", If(chkbox_Top_Most.Checked, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "ShowRecipientsOverlay", If(Is_Show_Recipients_Overlay, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "WheelZooms", If(Zoom_Wheel_Zooms, "1", "0"))
            ' Keys 1..9 -> MoveOn1..MoveOn9. Key "0" is runtime slot 10 and must be
            ' saved into MoveOn0 (previously only slots 0..9 were saved, so the "0"
            ' destination was lost on restart). If(...,"") avoids a null .ToString.
            For z = 1 To 9
                Try
                    SaveSetting(App_name, Second_App_Name, "MoveOn" & z.ToString, If(Hardkeys_to_move_mediafile(z), ""))
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1180: ERR: " & ex.Message)
                Finally
                    Hardkeys_to_move_mediafile(z) = Nothing
                End Try
            Next
            Try
                SaveSetting(App_name, Second_App_Name, "MoveOn0", If(Hardkeys_to_move_mediafile(10), ""))
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1181: ERR: " & ex.Message)
            Finally
                Hardkeys_to_move_mediafile(10) = Nothing
            End Try

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

            SaveSetting(App_name, Second_App_Name, "ExifAutoRotate", If(Is_Exif_AutoRotate, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "HighQualityScaling", If(Is_HighQuality_Scaling, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "ShowInfoOverlay", If(Is_Show_Info_Overlay, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "SlideshowIntervalSec", CInt(Slideshow_Base_Interval_Ms / 1000).ToString())
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

            SaveOcrSettings()

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

        ShutdownOcrTranslate()

        If Web_Browser IsNot Nothing Then
#If NETFRAMEWORK Then
            ' Touching DocumentText would instantiate the never-used IE ActiveX on
            ' the modern build; disposing a handle-less control is safe on both.
            Web_Browser.DocumentText = ""
#End If
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

End Class
