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
        all_Supported_Extensions.Clear()
        all_Supported_Extensions.UnionWith(Image_File_Extensions)
        all_Supported_Extensions.UnionWith(video_File_Extensions)
        all_Supported_Extensions.UnionWith(web_specific_image_extensions)
#If Not NETFRAMEWORK Then
        ApplyConfiguredExtensionFilter()
#End If
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
        toolTip.SetToolTip(btn_Select_Folder, Localization.T("Выбрать папку с медиафайлами"))
        toolTip.SetToolTip(btn_Review, Localization.T("Перечитать текущую папку - вдруг там что-то поменялось за вашей спиной."))
        toolTip.SetToolTip(btn_Panel, Localization.T("Показать панель изображений (F3)"))
        toolTip.SetToolTip(btn_Full_Screen, Localization.T("Полноэкранный режим - картинка во весь экран и ничего лишнего."))
        toolTip.SetToolTip(btn_Prev_File, Localization.T("Предыдущий файл (Стрелка влево, PgUp)"))
        toolTip.SetToolTip(btn_Next_File, Localization.T("Следующий файл (Стрелка вправо, PgDn)"))
        toolTip.SetToolTip(btn_Next_Random, Localization.T("Случайный файл (Y) - пусть судьба выбирает за вас."))
        toolTip.SetToolTip(btn_Random_Slideshow, Localization.T("Случайное слайд-шоу (I, F5)"))
        toolTip.SetToolTip(btn_Slideshow, Localization.T("Слайд-шоу (S)"))
        toolTip.SetToolTip(btn_Move_Table, Localization.T("Настройки: папки-получатели, OCR и перевод (F2)"))
        toolTip.SetToolTip(btn_Rename, Localization.T("Переименовать файл (F6)"))
        toolTip.SetToolTip(bt_Delete, Localization.T("Удалить файл (Del) - пути назад почти нет, так что прицеливайтесь."))
        toolTip.SetToolTip(btn_Language, Localization.T("Язык интерфейса"))
        toolTip.SetToolTip(chkbox_Top_Most, Localization.T("Поверх всех окон - чтобы ничто не смело его заслонить."))
#If NETFRAMEWORK Then
        toolTip.SetToolTip(btn_choose_file, Localization.T("Выбрать файл.."))
#Else
        ' Modern hangs "Open URL.." off this button's right-click - the tooltip is the
        ' only thing that tells anyone it is there.
        toolTip.SetToolTip(btn_choose_file, ChooseFileTooltipText())
#End If

        ' --- ComboBoxes and Labels ---
        toolTip.SetToolTip(cmbox_Sort, Localization.T("Порядок сортировки файлов"))
        toolTip.SetToolTip(cmbox_Media_Folder, Localization.T("Текущая папка. Введите путь и нажмите Enter для перехода."))
        toolTip.SetToolTip(lbl_Folder, Localization.T("Нажмите, чтобы скопировать путь к папке (вдруг пригодится)."))
        toolTip.SetToolTip(lbl_Current_File, Localization.T("Нажмите, чтобы скопировать путь к файлу"))
        toolTip.SetToolTip(lbl_Status, Localization.T("Статус текущей операции"))
        toolTip.SetToolTip(lbl_File_Number, Localization.T("Номер текущего файла и сколько их всего - чтобы оценить масштаб предстоящего."))

        toolTip.SetToolTip(btn_RecentFiles, Localization.T("Недавние файлы"))

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

        ' The mutex is created in MyApplication_Startup now. Creating it here - only
        ' once the form loaded, hundreds of milliseconds into a self-contained .NET 10
        ' start - left a window in which a second launch saw no mutex and became a
        ' second full instance; the two then raced over one registry hive at exit.

        is_TextBox_Editing = True
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0002: Initialize")
        cmbox_Sort.Items.Clear()
        cmbox_Sort.Items.AddRange(New String() {"abc", "xyz", "rnd", ">size", "<size", ">time", "<time", "<0123", ">3210"})

        cmbox_Sort.SelectedIndex = 0
        is_TextBox_Editing = False

        BgWorker.WorkerReportsProgress = True
        BgWorker.WorkerSupportsCancellation = True
#If Not NETFRAMEWORK Then
        modern_Preferences = ModernViewerPreferences.Load()
#End If
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

    ''' <summary>Gives up waiting for the file to unlock. Called by any explicit change
    ''' of file or folder too: the wait used to survive everything, and up to 45 seconds
    ''' later the tick would yank the view onto that file while the user was long since
    ''' working in another folder.</summary>
    Private Sub CancelPendingUnlock(status_Text As String)
        pending_Unlock_Timer.Stop()
        If String.IsNullOrEmpty(pending_Unlock_Path) Then Return
        Dim dropped As String = pending_Unlock_Path
        pending_Unlock_Path = Nothing
        If status_Text <> "" Then lbl_Status.Text = status_Text & Path.GetFileName(dropped)
    End Sub

    Private Sub Pending_Unlock_Timer_Tick(sender As Object, e As EventArgs) Handles pending_Unlock_Timer.Tick
        If String.IsNullOrEmpty(pending_Unlock_Path) Then
            pending_Unlock_Timer.Stop()
            Return
        End If

        If DateTime.Now > pending_Unlock_Deadline Then
            CancelPendingUnlock(Localization.T("Файл всё ещё занят: "))
            Return
        End If

        ' GetAttributes keeps throwing while a writer holds the file; once it succeeds
        ' the writer has released it (download finished) and we can open it.
        Try
            File.GetAttributes(pending_Unlock_Path)
        Catch ex As FileNotFoundException
            ' Gone, not busy. The bare Catch read this as "still locked" and kept polling
            ' to the deadline, then lied: "file still locked".
            CancelPendingUnlock(Localization.T("Файл удалён: "))
            Return
        Catch ex As DirectoryNotFoundException
            CancelPendingUnlock(Localization.T("Папка недоступна: "))
            Return
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

    Private Const No_Back_Flag As String = "-noback"

    ''' <summary>Strips the -noback flag when it is a TOKEN of its own - leading,
    ''' trailing, or the entire argument. It used to be searched for as a substring
    ''' anywhere in the line and cut out with a regex, so opening
    ''' C:\pics\photo-noback.jpg silently switched background tasks off for the session
    ''' and turned the path into C:\pics\photo.jpg - "file not found". Such a file could
    ''' not be opened with this app at all.</summary>
    Private Function StripNoBackFlag(ByRef argument_For_Path As String) As Boolean
        Dim found As Boolean = False

        If String.Equals(argument_For_Path, No_Back_Flag, StringComparison.OrdinalIgnoreCase) Then
            argument_For_Path = ""
            Return True
        End If

        If argument_For_Path.StartsWith(No_Back_Flag & " ", StringComparison.OrdinalIgnoreCase) Then
            argument_For_Path = argument_For_Path.Substring(No_Back_Flag.Length + 1).Trim()
            found = True
        End If

        If argument_For_Path.EndsWith(" " & No_Back_Flag, StringComparison.OrdinalIgnoreCase) Then
            argument_For_Path = argument_For_Path.Substring(0, argument_For_Path.Length - No_Back_Flag.Length - 1).Trim()
            found = True
        End If

        Return found
    End Function

    ''' <summary>What the filesystem said about an argument. Filled on a worker thread
    ''' (modern) or inline (net48) - see ProbeArgument.</summary>
    Private NotInheritable Class ArgumentProbe
        Public Property IsDirectory As Boolean
        Public Property Exists As Boolean
        Public Property Missing As Boolean
        Public Property Denied As Boolean
        Public Property LastError As String = ""
    End Class

    ''' <summary>
    ''' Asks the filesystem about the argument, with the retry policy this app has always
    ''' had - unchanged, deliberately:
    '''   * FileNotFound/DirNotFound -> genuinely gone: fail fast.
    '''   * UnauthorizedAccess       -> "access denied": almost always a sharing violation
    '''       because the file is still being written by a downloader. A real lock clears
    '''       in a moment: a few quick retries. An ACL problem will not clear.
    '''   * IOException etc.         -> share dropped/busy: retry longer, let the SMB
    '''       session re-establish.
    '''
    ''' Every call here can block for an SMB timeout - which is why on modern this whole
    ''' function runs on a worker thread (see ProcessArgument). It touches no form state.
    ''' </summary>
    Private Shared Function ProbeArgument(path As String) As ArgumentProbe
        Dim probe As New ArgumentProbe()

        Try
            If Directory.Exists(path) Then
                probe.IsDirectory = True
                probe.Exists = True
                Return probe
            End If
        Catch ex As Exception
            probe.LastError = "[" & ex.GetType().Name & "] " & ex.Message
        End Try

        probe.Exists = File.Exists(path)
        Dim denied_Tries As Integer = 0
        Dim net_Tries As Integer = 0

        Do While Not probe.Exists
            Try
                File.GetAttributes(path)
                probe.Exists = True
            Catch ex As FileNotFoundException
                probe.Missing = True : probe.LastError = ex.Message : Exit Do
            Catch ex As DirectoryNotFoundException
                probe.Missing = True : probe.LastError = ex.Message : Exit Do
            Catch ex As UnauthorizedAccessException
                probe.Denied = True : probe.LastError = ex.Message
                denied_Tries += 1
                If denied_Tries >= 3 Then Exit Do
                Threading.Thread.Sleep(250)
            Catch ex As Exception
                probe.LastError = "[" & ex.GetType().Name & "] " & ex.Message
                net_Tries += 1
                If net_Tries >= 8 Then Exit Do
                Threading.Thread.Sleep(250)
            End Try
        Loop

        Return probe
    End Function

    Public Sub ProcessArgument(argument_Raw_Text As String)
        Dim argument_For_Path As String = argument_Raw_Text.Trim()
        Dim is_No_Back_Flag_In_This_Call As Boolean = StripNoBackFlag(argument_For_Path)

        If is_No_Back_Flag_In_This_Call Then
            Is_No_Background_Tasks = True
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0232: ProcessArgument: -NoBack")
        End If

        If String.IsNullOrEmpty(argument_For_Path) Then
            If Is_No_Background_Tasks Then
                lbl_Status.Text = Localization.T("Режим -NoBack активен. Ожидание файла/папки.")
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0234: ProcessArgument: -NoBack but no file")
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0236: ProcessArgument: no file argumented")
            End If
            Return
        End If

#If Not NETFRAMEWORK Then
        ' Modern: probe off the UI thread. Every one of those Exists/GetAttributes calls
        ' blocks for a full SMB timeout when the server is asleep, and with the retries
        ' that is minutes of a dead window - on a machine whose working folder IS a
        ' share. The classification and the retry counts are unchanged; only the thread
        ' they run on is.
        ProcessArgumentAsync(argument_For_Path, is_No_Back_Flag_In_This_Call)
#Else
        ApplyArgument(argument_For_Path, is_No_Back_Flag_In_This_Call, ProbeArgument(argument_For_Path))
#End If
    End Sub

#If Not NETFRAMEWORK Then
    Private Async Sub ProcessArgumentAsync(argument_For_Path As String, is_No_Back_Flag_In_This_Call As Boolean)
        Dim generation As Integer = media_Generation
        lbl_Status.Text = Localization.TF("Проверяю доступность: {0}", Path.GetFileName(argument_For_Path))

        Dim probe As ArgumentProbe
        Try
            probe = Await Task.Run(Function() ProbeArgument(argument_For_Path))
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0281: probe failed: " & ex.Message)
            Return
        End Try

        ' The user may have opened something else while we waited on a dead share.
        If generation <> media_Generation Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0282: probe result dropped - user moved on")
            Return
        End If
        If Me.IsDisposed Then Return

        ApplyArgument(argument_For_Path, is_No_Back_Flag_In_This_Call, probe)
    End Sub
#End If

    ''' <summary>Acts on what the probe found. UI thread, both builds.</summary>
    Private Sub ApplyArgument(argument_For_Path As String, is_No_Back_Flag_In_This_Call As Boolean, probe As ArgumentProbe)
        Try
            If probe.IsDirectory Then
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

                ReadShowMediaFile(Mode_FolderAndFile)
            Else
                If Not probe.Exists Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0280: arg not reachable (missing=" & probe.Missing.ToString() & " denied=" & probe.Denied.ToString() & "): [" & argument_For_Path & "] reason=" & probe.LastError)
                    If probe.Denied Then
                        ' Locked by a writer (downloading). Watch for it to unlock and
                        ' open it automatically rather than making the user retry.
                        pending_Unlock_Path = argument_For_Path
                        pending_Unlock_Deadline = DateTime.Now.AddSeconds(45)
                        pending_Unlock_Timer.Stop()
                        pending_Unlock_Timer.Start()
                        lbl_Status.Text = Localization.TF("Файл занят, ждём разблокировки (качается?): {0}", Path.GetFileName(argument_For_Path))
                    ElseIf probe.Missing Then
                        lbl_Status.Text = Localization.TF("Файл не найден: {0}", Path.GetFileName(argument_For_Path))
                    Else
                        lbl_Status.Text = Localization.TF("Сетевой ресурс недоступен, повторите: {0}", Path.GetFileName(argument_For_Path))
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

                ReadShowMediaFile(Mode_FolderAndKnownFile)
            End If
        Catch ex As Exception
            ' Say what went wrong and leave the session alone. Wiping Current_Folder_Path
            ' and the combo box here killed navigation outright - ReadShowMediaFile
            ' quietly returns on an empty folder path - over an argument that was bad
            ' (an over-long path arriving by drag-drop, say) while a perfectly good
            ' folder was open on screen.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0300: Error processing argument: " & ex.Message)
            lbl_Status.Text = Localization.TF("Не удалось открыть: {0}", argument_For_Path)
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

        ' UI language: the saved "UiLanguage" code, else the legacy Is_Russian_Language
        ' flag of a pre-13-languages install, else the Windows display language.
        Localization.LoadFromSettings()
#If Not NETFRAMEWORK Then
        If modern_Preferences Is Nothing Then modern_Preferences = ModernViewerPreferences.Load()
        preferred_Audio_Language = modern_Preferences.PreferredAudioLanguage
        preferred_Subtitle_Language = modern_Preferences.PreferredSubtitleLanguage
#End If
        InitializeTooltips()
        InitializeOcrTranslate()
        InitializeBrowserTranslate()
#If Not NETFRAMEWORK Then
        ' The folder-box menu (Select folder.. / Share this folder..) and "Open URL.."
        ' are both modern-only - the x86 viewer has neither.
        InitializeFolderMenu()
        InitializeOpenUrlEntryPoint()
#End If

        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Width_At_Panel", "80"), Picture_Box_Width_At_Panel)
        Integer.TryParse(GetSetting(App_name, Second_App_Name, "Picture_Box_Height_At_Panel", "80"), Picture_Box_Height_At_Panel)

        Is_Pespective = GetSetting(App_name, Second_App_Name, "isPerspective", "1") = "1"
        ' Default "0" = the bars look exactly as they always have, so an upgrade does not
        ' silently restyle the background (same stance as WheelZooms below).
        Is_Dynamic_Perspective = GetSetting(App_name, Second_App_Name, "DynamicPerspective", "0") = "1"
        ' Default "1", unlike its neighbours: this one only bites once DynamicPerspective
        ' has been switched on by hand, and the growth is what that switch is for.
        Is_Animated_Perspective = GetSetting(App_name, Second_App_Name, "AnimatedPerspective", "1") = "1"
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

        ' Both ends. Only the lower one was checked, so a SortDir of 9+ in the registry
        ' (an older build after a newer one, a hand edit, corruption) threw
        ' ArgumentOutOfRange right here inside Form1_Load: a crash on every start for
        ' the x64 exe, and on x86 - where WOW64 swallows exceptions in Load - the rest
        ' of the initialisation was silently skipped and the window came up half-empty.
        If sort_Direction_Index < 0 OrElse sort_Direction_Index >= cmbox_Sort.Items.Count Then
            sort_Direction_Index = 0
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1138: cmbox_Sort is set to 0")
        End If
        cmbox_Sort.SelectedIndex = sort_Direction_Index

        UpdateLanguageButtonCaption()
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
                If cmbox_Media_Folder.Items.Count < RecentFoldersLimit() Then
                    cmbox_Media_Folder.Items.Add(folder)
                End If
            Next
        End If

        Dim recent_Media_Files_Data As String = GetSetting(App_name, Second_App_Name, "RecentMediaFiles", "")
        If Not String.IsNullOrEmpty(recent_Media_Files_Data) Then
            recent_Media_File_List = recent_Media_Files_Data.Split("|"c).ToList()
            recent_Media_File_List.RemoveAll(Function(x) String.IsNullOrEmpty(x))
            If recent_Media_File_List.Count > RecentFilesLimit() Then
                recent_Media_File_List = recent_Media_File_List.Skip(recent_Media_File_List.Count - RecentFilesLimit()).ToList()
            End If
        End If

        If Table_Form.chkbox_Independent_Thread_For_File_Operation IsNot Nothing Then
            Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked = GetSetting(App_name, Second_App_Name, "UseIndependentThreadForOperationsWithFiles", "0") = "1"
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0025: Command line args count: " & My.Application.CommandLineArgs.Count.ToString)


        If My.Application.CommandLineArgs.Count > 0 Then
            Dim fullCommandLine As String = String.Join(" ", My.Application.CommandLineArgs.ToArray())
            ProcessArgument(fullCommandLine)
        ElseIf StartupOpenMode() <> "home" Then
            Current_Folder_Path = GetSetting(App_name, Second_App_Name, "ImageFolder", "")
            If Not Current_Folder_Path = "" Then
                ' Straight to the read. There used to be a full EnumerateFiles.Count of
                ' the folder first - just to sanity-check the saved index, and against
                ' the count of ALL files rather than the media ones at that. On a cold
                ' network folder that is a second of frozen window spent to learn
                ' nothing: Mode_Files re-reads the folder immediately afterwards and
                ' clamps the index itself, and LoadFiles reports an empty or unreadable
                ' folder on its own. (Measured on \\p7\_i\output\C, 15 779 files: 1.1 s
                ' per pass - so this deletion halves the cold start.)
                Integer.TryParse(GetSetting(App_name, Second_App_Name, "LastCounter"), current_File_Index)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: folder from savings: " & Current_Folder_Path & " - index " & current_File_Index.ToString)

                ReadShowMediaFile(Mode_Files)
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

        ' Size still gets sanity limits, but the POSITION is judged by the desktop that
        ' actually exists: a saved rectangle is good if any part of it lands on the
        ' virtual screen. The old fixed ceilings (Top <= 720, Left <= 1000) came from a
        ' 1366x768 world and threw away every position on a wider monitor, and
        ' "must not be negative" threw away every monitor placed left of or above the
        ' primary one.
        If app_Width_Int < main_form_position_Limit_Width_Low OrElse app_Width_Int > main_form_position_Limit_Width Then app_Width_Int = first_run_width
        If app_Height_Int < main_form_position_Limit_Height_Low OrElse app_Height_Int > main_form_position_Limit_Height Then app_Height_Int = first_run_height

        Dim saved_Bounds As New Rectangle(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)
        If Not SystemInformation.VirtualScreen.IntersectsWith(saved_Bounds) Then
            app_Left_Int = first_run_left
            app_Top_Int = first_run_top
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1949: saved window position is off-screen, using defaults")
        End If

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
            ' This IS the first photo appearing, so it gets the dynamic-perspective halo
            ' growth like any other new photo - the load path just could not draw it yet.
            Draw_Perspective(animate:=True)
        End If
        ' Now the form is at its final size and panel_Media exists - build the
        ' recipients overlay if the user left it on.
        ApplyRecipientsOverlay()
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>Set while we let the file-operation queue finish before closing, so the
    ''' second (real) close does not try to drain again.</summary>
    Private is_Closing_After_Drain As Boolean = False
#End If

    Private Async Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' LITE closes like a normal viewer - no close-to-tray. Sharing (if any) is
        ' owned entirely by Fast Media Sorter: Share Manager, which keeps
        ' running independently; closing LITE never touches it.

#If Not NETFRAMEWORK Then
        ' A queued move is a file being COPIED across shares - killing the process
        ' mid-transfer leaves a half file in the destination. Give the queue its moment,
        ' without freezing the window while it finishes.
        If Not is_Closing_After_Drain AndAlso file_Op_Queue IsNot Nothing AndAlso file_Op_Queue.PendingCount > 0 Then
            e.Cancel = True
            is_Closing_After_Drain = True
            lbl_Status.Text = Localization.T("завершаются файловые операции..")
            Await file_Op_Queue.DrainAsync(TimeSpan.FromSeconds(15))
            Me.Close()
            Return
        End If
#End If

        Try
            If Current_Folder_Path IsNot Nothing Then SaveSetting(App_name, Second_App_Name, "ImageFolder", Current_Folder_Path)
            ' Always - zero is as valid a position as any. Skipping it left the previous
            ' folder's counter in place: close folder B on its first file and the next
            ' start opened B at file 51, remembered from folder A.
            SaveSetting(App_name, Second_App_Name, "LastCounter", current_File_Index.ToString)

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

            ' Writes UiLanguage plus the legacy Is_Russian_Language mirror.
            Localization.SaveToSettings()
            SaveSetting(App_name, Second_App_Name, "FirstRun", "0")
            SaveSetting(App_name, Second_App_Name, "CopyMode", If(Is_Copying_not_Moving, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "isPerspective", If(Is_Pespective, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "DynamicPerspective", If(Is_Dynamic_Perspective, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "AnimatedPerspective", If(Is_Animated_Perspective, "1", "0"))
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

            ' Negative coordinates are legal - that is simply a monitor to the left of,
            ' or above, the primary one; refusing to save them lost the position on
            ' every restart there. The size guards had their limits swapped: height was
            ' checked against the WIDTH floor and width against the HEIGHT one.
            SaveSetting(App_name, Second_App_Name, "AppTop", Me.Top.ToString)
            SaveSetting(App_name, Second_App_Name, "AppLeft", Me.Left.ToString)
            If Me.Height >= main_form_position_Limit_Height_Low Then SaveSetting(App_name, Second_App_Name, "AppHeight", Me.Height.ToString)
            If Me.Width >= main_form_position_Limit_Width_Low Then SaveSetting(App_name, Second_App_Name, "AppWidth", Me.Width.ToString)

            SaveSetting(App_name, Second_App_Name, "VideoVolume", video_Volume_Level.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
            SaveSetting(App_name, Second_App_Name, "VideoMuted", If(is_Video_Muted, "1", "0"))
            SaveSetting(App_name, Second_App_Name, "RecentFolders", String.Join("|", recent_Folder_List))

            SaveSetting(App_name, Second_App_Name, "UseIndependentThreadForOperationsWithFiles", If(Table_Form.chkbox_Independent_Thread_For_File_Operation.Checked, "1", "0"))

            SaveOcrSettings()
#If Not NETFRAMEWORK Then
            If modern_Preferences IsNot Nothing Then
                modern_Preferences.PreferredAudioLanguage = preferred_Audio_Language
                modern_Preferences.PreferredSubtitleLanguage = preferred_Subtitle_Language
                modern_Preferences.Save()
            End If
#End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1190: settings are saved")
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1200: ERR: " & ex.Message)
        End Try

        ' Wait on the workers' own signals, not on IsBusy: IsBusy is cleared by a
        ' callback posted to THIS thread, which is busy waiting - so the old loops could
        ' never see it drop and always sat out the full 1 s + 5 s before releasing VLC,
        ' the images and the streams under a worker that was still using them.
        If BgWorker.IsBusy Then
            BgWorker.CancelAsync()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1210: BgWorker try to CancelAsync")
            If Not bgworker_Done.Wait(1000) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1211: BgWorker did not finish in time")
            End If
        End If

        If FileOperationWorker.IsBusy Then
            FileOperationWorker.CancelAsync()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1220: FileOperationWorker try to CancelAsync")
            If Not fileop_Done.Wait(5000) Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1221: FileOperationWorker did not finish in time")
            End If
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
