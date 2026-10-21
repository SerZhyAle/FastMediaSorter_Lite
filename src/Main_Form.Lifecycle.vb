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
#If Not NETFRAMEWORK Then
        all_Supported_Extensions.UnionWith(SortableContentKinds.BuildExtensions(
            Image_File_Extensions, video_File_Extensions, web_specific_image_extensions,
            GetModernPreferences()))
#Else
        all_Supported_Extensions.UnionWith(video_File_Extensions)
#End If
        all_Supported_Extensions.UnionWith(web_specific_image_extensions)
#If Not NETFRAMEWORK Then
        ApplyConfiguredExtensionFilter()

        ' Secure containers are their own class, on their own preference, and that
        ' preference is the ONLY thing that decides them - which is why this runs AFTER the
        ' file-types filter rather than before it
        ' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §6).
        '
        ' Two reasons it cannot be one of the groups in that dialog. A container's real
        ' kind is unknowable until it is opened, so it belongs to none of "images", "video"
        ' or "audio" - and narrowing to images would otherwise hide a container holding a
        ' photograph. And deciding whether a file belongs in a folder listing must never
        ' cost a password-protected key derivation, which is what asking would mean. So the
        ' checkbox in Settings is the single authority, and a narrowed file-types list
        ' cannot silently switch it off.
        If modern_Preferences Is Nothing OrElse modern_Preferences.SecureIncludeInNavigation Then
            all_Supported_Extensions.Add(FdSecFormat.Extension)
        Else
            all_Supported_Extensions.Remove(FdSecFormat.Extension)
        End If
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
        toolTip.SetToolTip(btn_Review, Localization.T("Обновить текущую папку - вдруг там что-то поменялось за вашей спиной."))
        toolTip.SetToolTip(btn_Panel, Localization.T("Показать панель изображений (F3)"))
        toolTip.SetToolTip(btn_Full_Screen, Localization.T("Полноэкранный режим - картинка во весь экран и ничего лишнего."))
        toolTip.SetToolTip(btn_Prev_File, Localization.T("Предыдущий файл (Стрелка влево, PgUp)"))
        toolTip.SetToolTip(btn_Next_File, Localization.T("Следующий файл (Стрелка вправо, PgDn)"))
#If Not NETFRAMEWORK Then
        toolTip.SetToolTip(btn_Next_Random, Localization.T("Случайный файл (Y)"))
#Else
        toolTip.SetToolTip(btn_Next_Random, Localization.T("Случайный файл (Y) - пусть судьба выбирает за вас."))
#End If
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
        toolTip.SetToolTip(lbl_Status, Localization.T("Статус текущей операции. Нажмите, чтобы скопировать текст сообщения."))
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

#If Not NETFRAMEWORK Then
        ' Spec 227 T-3: the Share Manager's relayed phone connection code (or a document path it opened).
        ' The payload is a secret holder - it is never written anywhere on this side, and the import runs
        ' through the Resources panel's one door.
        If argument.StartsWith(PhoneCodeCommandPrefix, StringComparison.Ordinal) Then
            RestoreMainWindow()
            OpenPhoneConnectionCode(argument.Substring(PhoneCodeCommandPrefix.Length))
            Return
        End If
        If argument.StartsWith(PhoneCodeFileCommandPrefix, StringComparison.Ordinal) Then
            RestoreMainWindow()
            OpenPhoneConnectionCodeFile(argument.Substring(PhoneCodeFileCommandPrefix.Length))
            Return
        End If
#End If

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
    ''' handling above. The window is never hidden to a tray by the viewer itself (that
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
            AppFileLogger.LogException("Setting the WebBrowser emulation mode", ex)
        End Try
    End Sub
#End If

    Public Sub InitNew()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0001: init new")

        pending_Single_Click_Timer.Interval = SystemInformation.DoubleClickTime
        pending_Single_Click_Timer.Enabled = False

        ' Through the one factory, so this first instance gets the same close hook as every
        ' later one - without it "restart now" after a scale change did nothing (053 F-09).
        ' The modern build creates the window on first use instead (its handle made here would sit on the
        ' launch monitor, and moving it to the owner's monitor later re-scales every control - see
        ' Table_Form.ModernLayout.vb). The x86 build keeps the early creation.
#If NETFRAMEWORK Then
        EnsureSettingsWindow()
#End If

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
        lbl_Current_File.Text = ""
        lbl_Status.Text = ""
        is_TextBox_Editing = False

        BgWorker.WorkerReportsProgress = True
        BgWorker.WorkerSupportsCancellation = True
#If Not NETFRAMEWORK Then
        modern_Preferences = ModernViewerPreferences.Load()
        ' The decode cache runs on pool threads (the loading indicator and the prefetch
        ' worker both decode there) and cannot reach the form, so the budget is mirrored
        ' into it - here at startup, and again whenever the settings window changes it.
        DecodeCacheStore.BudgetMb = modern_Preferences.DecodeCacheMaxMb
#End If
#If NETFRAMEWORK Then
        ' A three-method bridge, never the form: script in the video page can reach
        ' exactly what ObjectForScripting exposes (audit 046, D-06).
        Web_Browser.ObjectForScripting = New VideoPageScriptBridge(Me)
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
    ''' <summary>The wait is keyed by (ticket, path) of the request that started it (ticket
    ''' 106): once a newer request is accepted or the user navigates, the wait is no longer the
    ''' latest and ends quietly, and a stale tick can only ever touch its own wait.</summary>
    Private ReadOnly pending_Unlock As New PendingUnlock()
    Private Const Unlock_Wait_Ms As Long = 45000
    Private WithEvents pending_Unlock_Timer As New System.Windows.Forms.Timer() With {.Interval = 800}

    ''' <summary>Gives up waiting for the file to unlock. Called by any explicit change
    ''' of file or folder too: the wait used to survive everything, and up to 45 seconds
    ''' later the tick would yank the view onto that file while the user was long since
    ''' working in another folder.</summary>
    Private Sub CancelPendingUnlock(status_Text As String)
        pending_Unlock_Timer.Stop()
        If Not pending_Unlock.IsActive Then Return
        Dim dropped As String = pending_Unlock.Path
        pending_Unlock.Clear()
        If status_Text <> "" Then lbl_Status.Text = status_Text & Path.GetFileName(dropped)
    End Sub

    ''' <summary>How a single probe of the locked file came out.</summary>
    Private Enum Unlock_Probe_Result
        Still_Locked = 0
        Ready = 1
        Missing = 2
        Folder_Unreachable = 3
    End Enum

    Private Async Sub Pending_Unlock_Timer_Tick(sender As Object, e As EventArgs) Handles pending_Unlock_Timer.Tick
        If Not pending_Unlock.IsActive Then
            pending_Unlock_Timer.Stop()
            Return
        End If

        ' The identity this tick works for. Everything after an Await is checked against it:
        ' a tick that outlives its wait (a newer request, the same path asked for again) must
        ' not stop, complete or open anything on behalf of the wait that replaced it.
        Dim mine As PendingUnlock.Capture = pending_Unlock.Snapshot()

        ' A newer open request (or navigation) made this wait obsolete: end it without a
        ' word - the status line now belongs to whatever replaced it.
        If Not open_Requests.IsCurrent(mine.Ticket) Then
            CancelPendingUnlock("")
            Return
        End If

        If pending_Unlock.Expired(MonotonicClock.NowMs()) Then
            CancelPendingUnlock(Localization.T("Файл всё ещё занят: "))
            Return
        End If

        If pending_Unlock.ProbeInFlight Then Return

        ' Off the UI thread. The probe is a single File.GetAttributes, which is cheap on a
        ' local disk but costs a FULL network timeout on a share that stopped answering - and
        ' this tick fires every 800 ms, so the window used to take that stall over and over.
        pending_Unlock.ProbeInFlight = True
        Dim outcome As Unlock_Probe_Result
        Try
            outcome = Await Task.Run(Function() ProbePendingUnlock(mine.Path))
        Catch ex As Exception
            ' Async Sub: an unobserved failure here would reach the unhandled-exception path.
            AppFileLogger.LogException("Probing a file that was locked", ex)
            If pending_Unlock.Owns(mine) Then pending_Unlock.ProbeInFlight = False
            Return
        End Try
        If pending_Unlock.Owns(mine) Then pending_Unlock.ProbeInFlight = False

        ' The world may have moved on while the probe was out: a different file requested, or
        ' the wait cancelled outright. A stale completion must not touch a NEWER wait, so it
        ' returns here without cancelling anything.
        If Me.IsDisposed Then Return
        If Not pending_Unlock.Owns(mine) Then Return
        If Not open_Requests.IsCurrent(mine.Ticket) Then
            CancelPendingUnlock("")
            Return
        End If
        If open_Coordinator.IsCloseDraining Then Return ' the next tick retries once the close is settled

        Select Case outcome
            Case Unlock_Probe_Result.Missing
                ' Gone, not busy. The bare Catch read this as "still locked" and kept polling
                ' to the deadline, then lied: "file still locked".
                CancelPendingUnlock(Localization.T("Файл удалён: "))
                Return
            Case Unlock_Probe_Result.Folder_Unreachable
                CancelPendingUnlock(Localization.T("Папка недоступна: "))
                Return
            Case Unlock_Probe_Result.Still_Locked
                Return ' wait for the next tick
        End Select

        pending_Unlock_Timer.Stop()
        Dim ready_Path As String = pending_Unlock.Path
        Dim ready_NoBack As Boolean = pending_Unlock.NoBack
        pending_Unlock.Clear()
        is_External_Input_Received = True
        is_File_Reseived_From_Outside = True
        ' A continuation of the request that started the wait, not a new one (ticket 106 O01.3).
        ObserveOpen(open_Coordinator.ContinueUnlocked(mine.Ticket, ready_Path, ready_NoBack), "Opening an unlocked file")
    End Sub

    ''' <summary>GetAttributes keeps throwing while a writer holds the file; once it succeeds
    ''' the writer has released it (download finished) and we can open it. Runs on a worker
    ''' thread - it must touch nothing but its argument.</summary>
    Private Shared Function ProbePendingUnlock(file_Path As String) As Unlock_Probe_Result
        Try
            File.GetAttributes(file_Path)
            Return Unlock_Probe_Result.Ready
        Catch ex As FileNotFoundException
            Return Unlock_Probe_Result.Missing
        Catch ex As DirectoryNotFoundException
            Return Unlock_Probe_Result.Folder_Unreachable
        Catch
            Return Unlock_Probe_Result.Still_Locked
        End Try
    End Function

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
        ' A file forwarded by another process can arrive before the deferred first-launch wizard.
        ' Keep that open request ahead of setup just like an argument on this process's command line.
        welcome_File_Argument = True
        If WelcomeRules.DeferToNextLaunch(WelcomeDoneStored(), welcome_Fresh_Profile, welcome_File_Argument) Then
            SaveSetting(App_name, Second_App_Name, WelcomeRules.PendingValue, "1")
        End If
        ' Live broadcast descriptor, .fmsbcast file, barcode text, or intent link (spec 023 §4)
        If TryHandleLiveBroadcastArgument(argument_For_Path) Then Return

        ' Not a path at all: a resource address, or a Share Manager document to import.
        If TryHandleResourceArgument(argument_For_Path) Then Return
#End If

        ' Probe off the UI thread. Every one of those Exists/GetAttributes calls blocks for a
        ' full SMB timeout when the server is asleep, and with the retries that is minutes of
        ' a dead window - on a machine whose working folder IS a share. The classification and
        ' the retry counts are unchanged; only the thread they run on is.
        '
        ' This used to be modern-only, and the net48 #Else called ProbeArgument inline. That
        ' is a genuine bug rather than a missing refinement, and it is worse on the fallback
        ' build: the path is reached from Form1_Load, from drag-drop AND from the WM_COPYDATA
        ' receiver, so a second launch carrying a network path froze the ALREADY RUNNING
        ' viewer for up to 8 x (30 s timeout + 250 ms) - about four minutes with no way to
        ' interrupt. Async/Await needs nothing net48 lacks.
        ProcessArgumentAsync(argument_For_Path, is_No_Back_Flag_In_This_Call)
    End Sub

    ''' <summary>Actual filesystem probes of "open this path" requests share two permits for
    ''' the whole process; a viewer keeps at most one request queued behind them, the latest
    ''' (ticket 106, 105 L01). A request that waited 30 s without a permit never runs.</summary>
    Private Shared ReadOnly ArgumentProbeScheduler As New ProbeScheduler(Of ArgumentProbe)(
        New ProbePolicy With {.MaxActive = 2, .MaxPending = 8, .ReplacePendingOfSameOwner = True,
                              .PendingExpiryMs = 30000})

    Private open_Coordinator_Value As OpenCoordinator

    ''' <summary>The ordering policy of open requests; created on first use because the
    ''' partial classes initialise their fields in no promised order.</summary>
    Private ReadOnly Property open_Coordinator As OpenCoordinator
        Get
            If open_Coordinator_Value Is Nothing Then
                open_Coordinator_Value = New OpenCoordinator(open_Requests, ArgumentProbeScheduler, New OpenHost(Me))
            End If
            Return open_Coordinator_Value
        End Get
    End Property

    ''' <summary>Logs a failure of an open that nobody awaits (the callers are Async Sub or
    ''' fire-and-forget): a fault must be observed, never left to the unhandled path.</summary>
    Private Shared Sub ObserveOpen(task As Task(Of OpenOutcome), what As String)
        task.ContinueWith(Sub(t) AppFileLogger.LogException(what, t.Exception),
                          TaskContinuationOptions.OnlyOnFaulted)
    End Sub

    ''' <summary>What the window gives the open coordinator. Every member that touches a
    ''' control runs on the UI thread, through Post.</summary>
    Private NotInheritable Class OpenHost
        Implements IOpenHost

        Private ReadOnly form As Main_Form

        Friend Sub New(form As Main_Form)
            Me.form = form
        End Sub

        Public ReadOnly Property IsClosed As Boolean Implements IOpenHost.IsClosed
            Get
                Return form.IsDisposed OrElse form.is_Form_Closed
            End Get
        End Property

        Public ReadOnly Property IsCloseDraining As Boolean Implements IOpenHost.IsCloseDraining
            Get
#If NETFRAMEWORK Then
                Return False
#Else
                Return form.IsCloseDrainActive()
#End If
            End Get
        End Property

        Public Sub Post(action As Action) Implements IOpenHost.Post
            If form.IsDisposed OrElse Not form.IsHandleCreated Then Throw New ObjectDisposedException("Main_Form")
            form.BeginInvoke(action)
        End Sub

        Public Sub ShowChecking(request As OpenRequest) Implements IOpenHost.ShowChecking
            If request.Kind = OpenKind.Explicit Then
                form.lbl_Status.Text = Localization.TF("Проверяю доступность: {0}", Path.GetFileName(request.Path))
            End If
        End Sub

        Public Sub Apply(request As OpenRequest, probe As ArgumentProbe) Implements IOpenHost.Apply
            If request.Kind = OpenKind.Explicit Then
                form.ApplyArgument(request.Path, request.NoBack, probe, request.Ticket)
            Else
#If Not NETFRAMEWORK Then
                form.ApplyResume(request.Path, probe)
#End If
            End If
        End Sub

        Public Sub ReportUnavailable(request As OpenRequest, status As ProbeStatus) Implements IOpenHost.ReportUnavailable
            ' A resume never shouts (its failure was not caused by the user). An explicit
            ' open says what it already says for a share that did not answer.
            If request.Kind = OpenKind.Explicit Then
                form.lbl_Status.Text = Localization.TF("Сетевой ресурс недоступен, повторите: {0}", Path.GetFileName(request.Path))
            End If
        End Sub

        Public Sub Diagnostic(message As String) Implements IOpenHost.Diagnostic
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0282: " & message)
        End Sub
    End Class

    Private Sub ProcessArgumentAsync(argument_For_Path As String, is_No_Back_Flag_In_This_Call As Boolean)
        ' The request's identity is taken when it is ACCEPTED (inside BeginExplicit) - not read
        ' off media_Generation, which moves when media is displayed (ticket 106). Everything
        ' after the probe (ownership, lifetime, close admission) is rechecked on the UI thread.
        ObserveOpen(open_Coordinator.BeginExplicit(argument_For_Path, is_No_Back_Flag_In_This_Call), "Probing a forwarded argument")
    End Sub
    ''' <summary>Acts on what the probe found. UI thread, both builds.</summary>
    Private Sub ApplyArgument(argument_For_Path As String, is_No_Back_Flag_In_This_Call As Boolean, probe As ArgumentProbe, ticket As Integer)
        Try
#If Not NETFRAMEWORK Then
            ' Any change of context leaves the archive first, with its temporary directory
            ' (§4.3). EnterArchive below does the same, so opening one archive straight
            ' from another cleans up in between.
            LeaveArchive()
            LeaveResource()

            If Not probe.IsDirectory AndAlso probe.Exists AndAlso ArchiveEntryFilter.IsArchivePath(argument_For_Path) Then
                EnterArchive(argument_For_Path)
                Return
            End If
#End If

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
                        pending_Unlock.Begin(ticket, argument_For_Path, MonotonicClock.NowMs() + Unlock_Wait_Ms,
                                             is_No_Back_Flag_In_This_Call)
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
                ' Leaves array mode as well (audit 048, A-02): after a folder of 100 000+
                ' files the array stayed active and the display read the OLD folder's first
                ' file instead of this one.
                InstallSingleFileList(Current_Image_Path)

                ReadShowMediaFile(Mode_FolderAndKnownFile)
            End If
        Catch ex As Exception
            ' Say what went wrong and leave the session alone. Wiping Current_Folder_Path
            ' and the combo box here killed navigation outright - ReadShowMediaFile
            ' quietly returns on an empty folder path - over an argument that was bad
            ' (an over-long path arriving by drag-drop, say) while a perfectly good
            ' folder was open on screen.
            AppFileLogger.LogException("Processing an argument", ex)
            lbl_Status.Text = Localization.TF("Не удалось открыть: {0}", argument_For_Path)
        End Try
    End Sub

#If Not NETFRAMEWORK Then

    ' ---------------------------------------------------------------------------------
    ' Resume the last played file (SPECIFICATION_RESUME_LAST_PLAYBACK_DOTNET10).
    '
    ' "Last file" used to mean the last INDEX inside the last folder, so deleting or
    ' renaming anything earlier in the list reopened a neighbour. This remembers the path
    ' itself, and because the file is opened through the ordinary display path, the
    ' existing VideoPositionHistory carries the playback position across the restart with
    ' no second store and no code of its own (§3.3).
    ' ---------------------------------------------------------------------------------

    ''' <summary>The full path of the last media file actually shown, or "". Session state
    ''' of the same kind as ImageFolder/LastCounter, so it lives beside them in the profile
    ''' and rides the same debounced flush (§4).</summary>
    Private last_Played_File As String = ""

    ''' <summary>
    ''' Records the file on screen (§3.4). Called from the one place that already maintains
    ''' the recent-files list, so "shown" means the same thing for both.
    '''
    ''' An archive entry and a remote MRL are skipped rather than cleared: the key keeps
    ''' whatever real file preceded them. An extracted entry lives under
    ''' archive-cache\&lt;pid&gt;-&lt;token&gt;\ and is gone by the next start (the archive
    ''' invariant), and a stream has no path to probe and no "continue from here" to offer.
    ''' </summary>
    Private Sub RememberLastPlayedFile(path As String)
        If String.IsNullOrEmpty(path) Then Return
        If IsArchiveMode() Then Return
        If IsRemoteMediaUrl(path) Then Return
        ' A session's temporary copy (archive entry or remote resource) is gone with its
        ' session - never a file to resume (audit 057, S02-8).
        If RemoteCachePaths.IsSessionCopy(path) Then Return
        last_Played_File = path
    End Sub

    ''' <summary>Drops the remembered file - what "clear the history" has to mean, or the
    ''' cleared-out viewer reopens the same file on the next start (§4).</summary>
    Private Sub ForgetLastPlayedFile()
        If String.IsNullOrEmpty(last_Played_File) Then Return
        last_Played_File = ""
        MarkSettingsDirty()
    End Sub

    ''' <summary>
    ''' Branch 2 of §3.1: True when the option owns this startup, in which case the caller
    ''' must NOT fall through to StartupOpenMode - a failed resume is a deliberately empty
    ''' window, not a random folder (§3.2.3).
    '''
    ''' A secondary window never resumes (§3.5): three windows reopening the same file is
    ''' not what "continue where I left off" means, and only the primary process writes the
    ''' key in the first place.
    ''' </summary>
    Private Function TryResumeLastPlayedFile() As Boolean
        If Not ResumeLastPlaybackEnabled() Then Return False
        If String.IsNullOrEmpty(last_Played_File) Then Return False
        If MultiWindowPolicy.IsSecondaryInstance() Then Return False

        ResumeLastPlayedFileAsync(last_Played_File)
        Return True
    End Function

    ''' <summary>
    ''' Probes off the UI thread through the same coordinator a command-line file goes
    ''' through - so a sleeping share costs an empty window, not a frozen one (§3.2.1). Nothing
    ''' here ever reaches lbl_Status: the user did not ask for this, so a failure they did not
    ''' cause must not shout at them (§0.3). Explicit intent accepted before the resume was
    ''' scheduled declines it before it takes a ticket (ticket 106 O01.3).
    ''' </summary>
    Private Sub ResumeLastPlayedFileAsync(path As String)
        ObserveOpen(open_Coordinator.BeginResume(path), "Resume: probing the last file")
    End Sub

    ''' <summary>UI thread, request still current: the resume's commit.</summary>
    Private Sub ApplyResume(path As String, probe As ArgumentProbe)
        If Not probe.Exists OrElse probe.IsDirectory Then
            ' The path is kept, not cleared: a NAS that is off today is on tomorrow, and
            ' forgetting it over one failed probe throws away the user's state (§3.2.4).
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0311: resume: last file unavailable: " & path)
            Return
        End If

        OpenResumedFile(path)
    End Sub
    ''' <summary>
    ''' Opens the folder the file sits in with the WHOLE folder listed and the cursor on
    ''' that file, so arrows, the slideshow and the N/total counter work immediately - a
    ''' one-file list (what a command-line argument builds) would be wrong here, because
    ''' the user is carrying on browsing rather than opening an attachment (§3.2.2).
    ''' </summary>
    Private Sub OpenResumedFile(file_Path As String)
        Try
            Dim folder As String = Path.GetDirectoryName(file_Path)
            If String.IsNullOrEmpty(folder) Then Return

            Current_Folder_Path = folder
            current_File_Index = 0
            ' LoadFiles fills the list and the count; ReadShowMediaFile then updates the
            ' folder combo and the recent-folder list from Current_Folder_Path.
            If Not LoadFiles() Then Return

            ' Not in the filtered list (the extension filter changed, say): show the folder
            ' from its start. The file is not forced into the list - that is not an error
            ' and gets no message.
            Dim index As Integer = -1
            If is_Files_Array_Active Then
                If files_Array IsNot Nothing Then
                    index = Array.FindIndex(files_Array, Function(item) String.Equals(item, file_Path, StringComparison.OrdinalIgnoreCase))
                End If
            ElseIf files_List IsNot Nothing Then
                index = files_List.FindIndex(Function(item) String.Equals(item, file_Path, StringComparison.OrdinalIgnoreCase))
            End If
            If index >= 0 Then current_File_Index = index

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0041: resume: " & file_Path & " - index " & current_File_Index.ToString())
            ReadShowMediaFile(Mode_SetFile)
        Catch ex As Exception
            AppFileLogger.LogException("Resume: opening the last file", ex)
        End Try
    End Sub

#Else

    ''' <summary>The frozen x86 target has no resume option (§2.2), so branch 2 never
    ''' fires there and the startup it shipped with is untouched.</summary>
    Private Function TryResumeLastPlayedFile() As Boolean
        Return False
    End Function

#End If

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Debug.WriteLine(" - - - ")
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0000: Form started")
        Me.AllowDrop = True

#If Not NETFRAMEWORK Then
        ' Spec 184: is this a fresh profile? Read before this launch writes anything, because the association
        ' marker below is written on every start and would make every profile look used.
        CaptureWelcomeStartState()
#End If

        ' An exception that escapes a UI handler is logged by AppFileLogger and would
        ' otherwise leave no visible trace at all (053 F-15).
        AppFileLogger.UiFaultReporter = AddressOf ReportUiFault
#If Not NETFRAMEWORK Then
        ' Every window the viewer opens joins its z-order band, monitor, reading direction and theme through
        ' one layer (124 UX-201); it needs this window, so it is installed once the window exists.
        ViewerDialogPrep.Install()
#End If

        ' Build the modern docked layout (reparents the Designer controls into
        ' flow_Toolbar / panel_Status / panel_Media) before anything lays out.
        BuildModernLayout()
#If Not NETFRAMEWORK Then
        ' Severity of the status line: every write to it - the legacy "! .." ones included - is classified in one
        ' place (Main_Form.Notify.vb), and the empty state paints itself on the media panel (124 UX-206/UX-209).
        AddHandler lbl_Status.TextChanged, AddressOf StatusLabelTextChanged
        AddHandler Microsoft.Win32.SystemEvents.UserPreferenceChanged, AddressOf OnSystemPreferenceChanged
        AddHandler panel_Media.Paint, AddressOf PaintEmptyState
#End If

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

        ' Held to the six schemes: an out-of-range value crashed every start (053 F-16).
        Form_Color_Scheme = Utils.ColorSchemeFromSetting(GetSetting(App_name, Second_App_Name, "color_scheme", "1"))

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
        If MultiWindowPolicy.IsSecondaryInstance() Then
            Me.Text &= " - " & Localization.T("дополнительное окно")
        End If
#If Not NETFRAMEWORK Then
        If modern_Preferences Is Nothing Then modern_Preferences = ModernViewerPreferences.Load()
        preferred_Audio_Language = modern_Preferences.PreferredAudioLanguage
        preferred_Subtitle_Language = modern_Preferences.PreferredSubtitleLanguage
        ' The custom shortcuts (§3.5). An untouched profile yields an empty map and every
        ' key then reaches the historical dispatch exactly as before.
        ReloadCustomHotkeys()
        ' The five navigation buttons the floating chrome replaces leave the docked toolbar before
        ' its first paint; the panel itself is built once the form is shown (Main_Form_Shown).
        ApplyModernChrome()
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

        Picture_Box_Width_At_Panel = Utils.ParseIntSetting(GetSetting(App_name, Second_App_Name, "Picture_Box_Width_At_Panel", "80"), 80)
        Picture_Box_Height_At_Panel = Utils.ParseIntSetting(GetSetting(App_name, Second_App_Name, "Picture_Box_Height_At_Panel", "80"), 80)

        Is_Pespective = GetSetting(App_name, Second_App_Name, "isPerspective", "1") = "1"
        ' Default "0" = the bars look exactly as they always have, so an upgrade does not
        ' silently restyle the background (same stance as WheelZooms below).
        Is_Dynamic_Perspective = GetSetting(App_name, Second_App_Name, "DynamicPerspective", "0") = "1"
        ' Default "1", unlike its neighbours: this one only bites once DynamicPerspective
        ' has been switched on by hand, and the growth is what that switch is for.
        Is_Animated_Perspective = GetSetting(App_name, Second_App_Name, "AnimatedPerspective", "1") = "1"
        ' Absent/empty/corrupt normalizes to medium in HaloSpeedFromSetting, which is what
        ' hands every existing profile the new (twice as fast) default without a migration.
        Halo_Animation_Speed = HaloSpeedFromSetting(GetSetting(App_name, Second_App_Name, "AnimatedPerspectiveSpeed", ""))
        Is_no_request_before_file_operation = GetSetting(App_name, Second_App_Name, "NoRequestBeforeFileOperation", "0") = "1"

        Is_to_show_picture_sizes = GetSetting(App_name, Second_App_Name, "ShowPictureSizes", "1") = "1"
        Is_to_show_file_sizes = GetSetting(App_name, Second_App_Name, "ShowFileSizes", "1") = "1"
        Is_to_show_file_datetime = GetSetting(App_name, Second_App_Name, "ShowFileDates", "1") = "1"
        Is_Video_Loop = GetSetting(App_name, Second_App_Name, "IsVideoLoop", "0") = "1"
#If Not NETFRAMEWORK Then
        If Is_Video_Loop Then GetModernPreferences().VideoEndAction = "repeat"
        Is_Video_Loop = False
#End If

        ' Viewer options (see the "Просмотр" tab of the settings window).
        Is_Exif_AutoRotate = GetSetting(App_name, Second_App_Name, "ExifAutoRotate", "1") = "1"
        Is_HighQuality_Scaling = GetSetting(App_name, Second_App_Name, "HighQualityScaling", "1") = "1"
        Is_Show_Info_Overlay = GetSetting(App_name, Second_App_Name, "ShowInfoOverlay", "0") = "1"
        ' A corrupt value means the default 10 s, not a 1-second slideshow (053 F-17).
        Dim slideshow_Seconds As Integer = Utils.ParseIntSetting(GetSetting(App_name, Second_App_Name, "SlideshowIntervalSec", "10"), 10, 1, 120)
        Slideshow_Base_Interval_Ms = slideshow_Seconds * 1000
#If Not NETFRAMEWORK Then
        Slideshow_Base_Interval_Ms = Utils.ParseIntSetting(GetSetting(App_name, Second_App_Name, "SlideshowIntervalMs", Slideshow_Base_Interval_Ms.ToString()), Slideshow_Base_Interval_Ms, 1000, 120000)
#End If

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
        If Table_Form IsNot Nothing Then Table_Form.LngCh()

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
#If Not NETFRAMEWORK Then
        ' One spelling for the probe and the action (audit 047, C-24) - including a value an
        ' older build stored with a stray space.
        For z = 1 To 10
            Hardkeys_to_move_mediafile(z) = SlotHealthPolicy.NormalizeSlotPath(Hardkeys_to_move_mediafile(z))
        Next
#End If

        Dim recent_Folders_Data As String = GetSetting(App_name, Second_App_Name, "RecentFolders", "")
        If Not String.IsNullOrEmpty(recent_Folders_Data) Then
            recent_Folder_List = recent_Folders_Data.Split("|"c).ToList()
            recent_Folder_List.RemoveAll(Function(x) String.IsNullOrEmpty(x))
            ' The LIST, not only the combo: an untrimmed list kept every folder the limit
            ' was lowered below, and wrote them back on every flush (053 F-12).
            Utils.ApplyRecentLimit(recent_Folder_List, RecentFoldersLimit(), newestFirst:=True)

            For Each folder In recent_Folder_List
                cmbox_Media_Folder.Items.Add(folder)
            Next
        End If

        Dim recent_Media_Files_Data As String = GetSetting(App_name, Second_App_Name, "RecentMediaFiles", "")
        If Not String.IsNullOrEmpty(recent_Media_Files_Data) Then
            recent_Media_File_List = recent_Media_Files_Data.Split("|"c).ToList()
            recent_Media_File_List.RemoveAll(Function(x) String.IsNullOrEmpty(x))
            Utils.ApplyRecentLimit(recent_Media_File_List, RecentFilesLimit(), newestFirst:=False)
        End If

#If Not NETFRAMEWORK Then
        ' Session state beside ImageFolder/LastCounter, and read before the startup
        ' branches below decide what to open (§4).
        last_Played_File = GetSetting(App_name, Second_App_Name, "LastPlayedFile", "")
#End If

        Is_Independent_Thread_For_File_Operations = GetSetting(App_name, Second_App_Name, "UseIndependentThreadForOperationsWithFiles", "0") = "1"

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0025: Command line args count: " & My.Application.CommandLineArgs.Count.ToString)


        If My.Application.CommandLineArgs.Count > 0 Then
            ' Branch 1 (§3.1): an explicit file always beats any memory.
            Dim fullCommandLine As String = String.Join(" ", My.Application.CommandLineArgs.ToArray())
            ProcessArgument(fullCommandLine)
        ElseIf TryResumeLastPlayedFile() Then
            ' Branch 2: the resume option took charge - independently of StartupOpenMode,
            ' including "home". It probes off the UI thread and finishes on its own, and a
            ' failure deliberately leaves the window empty rather than falling through to
            ' branch 3 (§3.2.3).
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0039: resume: restoring the last played file")
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
                ' §7.1 of SPECIFICATION_SETTINGS_EXPANSION: "last folder" opens the folder
                ' and starts at its beginning, "last file" additionally restores which file
                ' was open. Reading the saved index in both modes made the two settings the
                ' same thing.
                If StartupOpenMode() = "lastFile" Then
                    Integer.TryParse(GetSetting(App_name, Second_App_Name, "LastCounter"), current_File_Index)
                Else
                    current_File_Index = 0
                End If
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0040: folder from savings: " & Current_Folder_Path & " - index " & current_File_Index.ToString)

#If Not NETFRAMEWORK Then
                ' The last folder was a resource: open it again once the window is up (it may need the
                ' network, and a password prompt must not run inside Form1_Load).
                If ResourceLocator.IsLocator(Current_Folder_Path) Then
                    Dim resource_Locator As String = Current_Folder_Path
                    Current_Folder_Path = ""
                    BeginInvoke(New Action(Sub() TryOpenAsResourceLocator(resource_Locator)))
                Else
                    ReadShowMediaFile(Mode_Files)
                End If
#Else
                ReadShowMediaFile(Mode_Files)
#End If
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1158: no folder saved")
            End If
        End If

        If is_Table_Form_Open Then
            EnsureSettingsWindow()
            ' chkbox_Top_Most was restored above, so the viewer may already be pinned -
            ' the restored settings window has to join that band, not hide behind it.
            PinToViewerBand(Table_Form)
            ' TopMost puts it into the right band; the explicit owner also keeps it
            ' above the viewer within that band.
#If Not NETFRAMEWORK Then
            ' Placed before the window gets its handle (see Main_Form.KeyboardInput.vb ShowSettingsWindow).
            Table_Form.PrepareShowBeside(Me)
#End If
            Table_Form.Show(Me)
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
#If NETFRAMEWORK Then
        If Not SystemInformation.VirtualScreen.IntersectsWith(saved_Bounds) Then
            app_Left_Int = first_run_left
            app_Top_Int = first_run_top
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1949: saved window position is off-screen, using defaults")
        End If
#Else
        ' APP-BEHAVIOUR rule 10: a rectangle that no longer lands on a display goes to the
        ' NEAREST surviving one - not to the first-run corner of the primary monitor.
        Dim placed_Bounds As Rectangle = WindowPlacement.Place(saved_Bounds, WindowPlacement.CurrentWorkAreas())
        If placed_Bounds <> saved_Bounds Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1949: saved window position is off-screen, moved to the nearest display")
        End If
        app_Left_Int = placed_Bounds.Left
        app_Top_Int = placed_Bounds.Top
        app_Width_Int = placed_Bounds.Width
        app_Height_Int = placed_Bounds.Height
#End If

        If MultiWindowPolicy.IsSecondaryInstance() Then
            Dim offset As Integer = 32 * Math.Max(1, My.MyApplication.GetRunningViewerWindowCount())
            Dim proposed As New Point(app_Left_Int + offset, app_Top_Int + offset)
            Dim workArea As Rectangle = Screen.FromPoint(proposed).WorkingArea
            app_Left_Int = Math.Max(workArea.Left, Math.Min(proposed.X, workArea.Right - app_Width_Int))
            app_Top_Int = Math.Max(workArea.Top, Math.Min(proposed.Y, workArea.Bottom - app_Height_Int))
        End If

        Me.SetBounds(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1950: Form_Sizes: " & app_Left_Int.ToString & " - " & app_Top_Int.ToString & " " & app_Width_Int.ToString & " - " & app_Height_Int.ToString)

#If Not NETFRAMEWORK Then
        ' The state comes back on top of the normal rectangle just set, so un-maximizing
        ' returns to the size the window had. A second window of the viewer opens normal,
        ' offset from the first, as before.
        If Not MultiWindowPolicy.IsSecondaryInstance() Then
            Select Case WindowPlacement.ParseState(GetSetting(App_name, Second_App_Name, "AppWindowState", ""))
                Case WindowPlacement.StateMaximized
                    Me.WindowState = FormWindowState.Maximized
                Case WindowPlacement.StateFullScreen
                    ' ISizeChanged below applies the border and the state together.
                    is_Full_Screen_Mode = True
                    is_Super_Full_Screen_Mode = GetSetting(App_name, Second_App_Name, "AppSuperFullScreen", "0") = "1"
            End Select
        End If
#End If

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

#If Not NETFRAMEWORK Then
        ' The floating chrome: its window needs the final geometry, like the overlay above.
        ApplyModernChrome()

        ' Ask the ten slots whether they are alive, in the background (§3.3, Ф4). Here
        ' rather than in Form1_Load on purpose: by the Shown event the first image is
        ' already on screen, so ten probes against a share that may be asleep cannot delay
        ' it. Nothing waits for the answers - they only fill the cache, so the first press
        ' of a slot is usually free.
        PrewarmSlotHealth()

        ShowProfileResetOutcome()

        SweepAnimationVideoStoreIfNeeded()

        ' Spec 184: the welcome wizard, after the window and its first picture are on screen (never on top of them).
        BeginInvoke(New Action(AddressOf ShowWelcomeIfDue))
#End If
    End Sub

    ''' <summary>Set by FormClosed: the close really happened (FormClosing alone can be vetoed).</summary>
    Private is_Form_Closed As Boolean

#If Not NETFRAMEWORK Then
    ''' <summary>Coordinates file-operation queue drain across close attempts (ticket 107).</summary>
    Private ReadOnly close_Drain_Coordinator As New CloseDrainCoordinator()

    ''' <summary>True while a close drain owns the window: no completion may start new file work.</summary>
    Private Function IsCloseDrainActive() As Boolean
        Return close_Drain_Coordinator.IsDraining
    End Function
#End If

    ''' <summary>Says in the status line that a UI handler failed. Non-modal on purpose:
    ''' a dialog raised from inside a fault can itself fault, or stack up once per
    ''' repaint. The message names the cause and points at the log.</summary>
    Private Sub ReportUiFault(ex As Exception)
        If Me.IsDisposed OrElse lbl_Status Is Nothing OrElse lbl_Status.IsDisposed Then Return
#If Not NETFRAMEWORK Then
        ' One sentence, not two: FailureText.Describe already says where the details are for an unclassified failure
        ' (124 UX-208) - and an internal fault is an ERROR, so in full screen without panels it raises a toast.
        Notify(Localization.TF("Внутренняя ошибка: {0}. Последнее действие могло выполниться не полностью.", FailureText.Describe(ex)), NotifyLevel.Error)
#Else
        lbl_Status.Text = Localization.TF("Внутренняя ошибка: {0}. Последнее действие могло выполниться не полностью; подробности - в журнале.",
                                          FailureText.Describe(ex))
#End If
    End Sub

    Private Async Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
#If Not NETFRAMEWORK Then
            ' 119 Ф-6: the pointer is never buried with the window.
            RestorePlaybackCursorForInterruption()
#End If
            ' The window closes like any normal viewer window - no close-to-tray. Sharing (if any) is
            ' owned entirely by the Share Manager, which keeps
            ' running independently; closing the viewer never touches it.

            ' A shutdown or logoff arrives here from WM_QUERYENDSESSION, which is a QUESTION:
            ' another program can still veto it (or `shutdown /a` cancel it), and this window
            ' then keeps running. So on that path only persist, and leave every piece of
            ' teardown to FormClosed, which WinForms raises only once the close really happens
            ' (WM_ENDSESSION with TRUE, or an ordinary close). Tearing down here used to leave a
            ' cancelled shutdown with disposed timers and images - and, with the slot array
            ' cleared, a flush a minute later that wiped all ten destination folders (053 F-01).
            If e.CloseReason = CloseReason.WindowsShutDown Then
                SettingsFlush.SaveNow()
                Return
            End If

#If Not NETFRAMEWORK Then
            ' If a drain is already waiting on the queue, this close attempt is re-entrant
            ' (e.g. repeated Alt+F4 or close button clicks while SMB copy finishes).
            ' Cancel it immediately so we do not bypass the drain or start parallel drains (ticket 107).
            ' A batch in flight is a tail of queued work like any other: it is told to start
            ' nothing further, and the one operation already running is waited for below.
            CancelBatch()
            Select Case close_Drain_Coordinator.EvaluateClosing(False,
                    file_Op_Queue IsNot Nothing AndAlso (file_Op_Queue.OutstandingCount > 0 OrElse BatchIsRunning))
                Case CloseDecision.CancelReentrant
                    e.Cancel = True
                    Return
                Case CloseDecision.StartDrain
                    ' Give pending cross-share transfers time to finish without freezing
                    ' the window. Staged destinations remain protected if the user discards.
                    e.Cancel = True
                    If Not close_Drain_Coordinator.BeginDrain() Then Return

                    ' Admission stops HERE, before the first await (107 C02): a key pressed while
                    ' the close is being negotiated is refused through the ordinary rollback path.
                    ' The queue is paused, not completed - a veto or a failed drain reopens it.
                    ' The archive, the remote resource and the secure container are released when
                    ' the close is actually approved (the approved Close() comes back through the
                    ' bottom of this handler), not on the first request: a vetoed or failed close
                    ' must leave a usable window with its open state intact (C02.5).
                    Dim admission_Epoch As Integer = If(file_Op_Queue Is Nothing, 0, file_Op_Queue.PauseAdmissionForClose())

                    Await DrainFileOperationsAsync(admission_Epoch)
                    Return
            End Select
            LeaveArchive()
            LeaveResource()
            ShutdownRemoteSlots()
            SecureShutdown()
#End If

            ' The whole settings block moved into PersistSettings so it is no longer reachable
            ' ONLY from here - see B-1 in SPECIFICATION_LONG_RUN_STABILITY.md. Saved here, while
            ' the settings window and every control still report their live state; the
            ' teardown waits for FormClosed.
            SettingsFlush.SaveNow()
        Catch ex As Exception
            e.Cancel = True
#If Not NETFRAMEWORK Then
            close_Drain_Coordinator.Reset()
#End If
            AppFileLogger.LogException("Closing the viewer", ex)
            ReportUiFault(ex)
        End Try
    End Sub

#If Not NETFRAMEWORK Then
    Private Async Function DrainFileOperationsAsync(admission_Epoch As Integer) As Task
        lbl_Status.Text = Localization.T("завершаются файловые операции..")

        ' The sequence itself - wait, ask, close, veto, recover - is FileOpCloseFlow's, driven by
        ' tests; this window supplies only what is the window's: the prompt and the Close().
        '
        ' The 15 s is a hint, not a verdict. A tail of two or three 100-200 MB moves over SMB
        ' routinely runs longer, and closing anyway kills the consumer thread wherever it happens to
        ' be - so ask instead of deciding for the user. (What it cannot corrupt any more is the
        ' destination file: FileManager.MoveFile now stages the copy under a temporary name.)
        '
        ' This question stays the SYSTEM box on purpose (ticket 243 T-12a): here "No" is the destructive answer
        ' (it closes the program and drops the unfinished operations), and the product-owned question window answers
        ' its cancel answer - No - for Escape and the close box, so one stray Escape would close the program. The
        ' system box ignores both for a Yes/No set. Moving it needs a per-question Escape answer first.
        Dim flow As New FileOpCloseFlow(Of FileOp)(
            close_Drain_Coordinator,
            Function() file_Op_Queue,
            Sub()
                ' The old queue's handler stays attached on purpose: the completions it already
                ' posted (unstarted operations reported as not executed) still have to reach the
                ' list rollback.
                file_Op_Queue = Nothing
                EnsureFileOpQueue()
            End Sub,
            Function(outstanding As Integer) ViewerBox.ShowSystem(
                Me,
                Localization.TF("Файловые операции ещё выполняются: {0}." & vbCrLf &
                                "Подождать ещё? «Нет» закроет программу, и незавершённые операции не выполнятся.",
                                outstanding),
                Localization.T("Закрытие программы"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1) = DialogResult.Yes,
            Function() Not Me.IsDisposed,
            Function()
                Me.Close()
                Return Not Me.IsDisposed
            End Function,
            Sub(message As String, ex As Exception)
                If ex Is Nothing Then AppFileLogger.WriteLine(message) Else AppFileLogger.LogException(message, ex)
                If ex IsNot Nothing AndAlso Not Me.IsDisposed Then ReportUiFault(ex)
            End Sub)
        Await flow.RunAsync(admission_Epoch)
    End Function

    ''' <summary>Replaces a faulted queue after its consumer has ended (also reached from the next
    ''' operation, so a dead consumer never silently swallows the user's next key).</summary>
    Private Sub RecoverFaultedFileOpQueue()
        If file_Op_Queue Is Nothing OrElse file_Op_Queue.Admission <> QueueAdmission.Faulted Then Return
        If Not file_Op_Queue.ConsumerIsTerminal Then Return
        AppFileLogger.WriteLine("FileOpQueue: replacing the queue after its consumer stopped")
        file_Op_Queue = Nothing
        EnsureFileOpQueue()
    End Sub
#End If
    ''' <summary>
    ''' The teardown half of closing. Raised only when the close really happens - after an
    ''' ordinary FormClosing that nobody cancelled, or on WM_ENDSESSION(TRUE) at the end of
    ''' a shutdown that went through - never for a shutdown that was later cancelled.
    ''' </summary>
    Private Sub Form1_FormClosed(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        ' A fault inside the teardown must never escape this handler: WinForms aborts the
        ' window destroy on an exception from OnFormClosed, so one fault leaves a window that
        ' no close attempt can ever finish - every retry walks into the same fault (the
        ' un-closable viewer window of the 2026-10-05 session). Log it and let the close
        ' complete; the process exit reclaims whatever the fault skipped.
        Try
            CloseTeardown(e)
        Catch ex As Exception
            AppFileLogger.LogException("Closing the viewer: teardown fault", ex)
        End Try
    End Sub

    Private Sub CloseTeardown(e As FormClosedEventArgs)
        ' Only now, when the close is real (FormClosing can still be cancelled): no probe
        ' that comes back from here on may open anything (ticket 106).
        is_Form_Closed = True
        open_Coordinator.NoteClosed()
#If Not NETFRAMEWORK Then
        RemoveHandler Microsoft.Win32.SystemEvents.UserPreferenceChanged, AddressOf OnSystemPreferenceChanged
        ' The close is real: admission is closed for good (the consumer ends once it has finished
        ' what it accepted).
        If file_Op_Queue IsNot Nothing Then file_Op_Queue.CompleteAdmission()
        ' A folder still being read has nobody to tell any more (014 U-03c).
        CancelFolderScan()
#End If
        CancelPendingUnlock("")
        pending_Unlock_Timer.Dispose()
        If AppFileLogger.UiFaultReporter = New Action(Of Exception)(AddressOf ReportUiFault) Then
            AppFileLogger.UiFaultReporter = Nothing
        End If
        If e.CloseReason = CloseReason.WindowsShutDown Then
            ' FormClosing only persisted on this path. Save once more - the session may have
            ' moved on between the question and the answer - and run what an ordinary close
            ' runs before its queue drain. Both are no-ops when nothing is open.
            SettingsFlush.SaveNow()
#If Not NETFRAMEWORK Then
            LeaveArchive()
            LeaveResource()
            ShutdownRemoteSlots()
            SecureShutdown()
#End If
        End If


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
#If Not NETFRAMEWORK Then
        ' The close is real now: stop the folder watcher before anything it feeds goes away.
        StopFolderWatcher()
        If folder_Watch_Timer IsNot Nothing Then folder_Watch_Timer.Dispose()
#End If
        StopGifLoopPlayback()
        gif_Restart_Timer.Dispose()
        If settings_Flush_Timer IsNot Nothing Then
            settings_Flush_Timer.Stop()
            settings_Flush_Timer.Dispose()
            settings_Flush_Timer = Nothing
        End If
        If toolTip IsNot Nothing Then toolTip.Dispose()

#If Not NETFRAMEWORK Then
        DisposeAnimationVideoStore()
        ReleasePdfDocument()
        ReleaseTextDocument()
        ClearEpubPlaceholderData()
#End If
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
        ' Ticket 115: no new native work from here on, and the work in flight (a pool-thread
        ' stop, a tag parse) must be out before the player and the instance are freed - a free
        ' under a running thread is the access violation of the crash dumps. If something is
        ' still inside after the bound, the native objects are left to the process exit.
        Dim vlc_May_Be_Freed As Boolean = True
#If Not NETFRAMEWORK Then
        vlc_May_Be_Freed = vlc_Lease.CloseAndWaitForIdle(3000)
        If Not vlc_May_Be_Freed Then AppFileLogger.WriteLine("LibVLC still busy at close; native objects left to process exit")
#End If
        If vlc_Media_Player IsNot Nothing AndAlso vlc_May_Be_Freed Then
            Try
                vlc_Media_Player.Dispose()
            Catch ex As Exception
                AppFileLogger.LogException("Disposing the VLC media player", ex)
            End Try
            vlc_Media_Player = Nothing
        End If
        If vlc_Video_View IsNot Nothing Then
            Try
                vlc_Video_View.Dispose()
            Catch ex As Exception
                AppFileLogger.LogException("Disposing the VLC video view", ex)
            End Try
            vlc_Video_View = Nothing
        End If
        If libVlc IsNot Nothing AndAlso vlc_May_Be_Freed Then
            Try
                libVlc.Dispose()
            Catch ex As Exception
                AppFileLogger.LogException("Disposing LibVLC", ex)
            End Try
            libVlc = Nothing
        End If

#If Not NETFRAMEWORK Then
        ' The pictures the cache still holds (014 U-01) go with the boxes'.
        media_Cache.Clear()
#End If
        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image?.Dispose()
        If Picture_Box_1.BackgroundImage IsNot Nothing Then Picture_Box_1.BackgroundImage?.Dispose()
        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image?.Dispose()
        If Picture_Box_2.BackgroundImage IsNot Nothing Then Picture_Box_2.BackgroundImage?.Dispose()
        If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream?.Dispose()
        If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream?.Dispose()

        DisposeRecipientsOverlayResources()
#If Not NETFRAMEWORK Then
        DisposeChromeResources()
#End If
        If ocr_Button_Font_Bold IsNot Nothing Then ocr_Button_Font_Bold.Dispose()
        If ocr_Button_Font_Regular IsNot Nothing Then ocr_Button_Font_Regular.Dispose()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1230: form is closed")
    End Sub

    ''' <summary>
    ''' Owns WHEN the settings block is written: the edit generation, the debounce, the bounded
    ''' retries after a failed write (SettingsFlushScheduler, 105 L09).
    ''' </summary>
    Private settings_Scheduler As SettingsFlushScheduler

    Private settings_Flush_Timer As System.Windows.Forms.Timer

    Private ReadOnly Property SettingsFlush As SettingsFlushScheduler
        Get
            If settings_Scheduler Is Nothing Then
                settings_Scheduler = New SettingsFlushScheduler(
                    AddressOf PersistSettings,
                    Sub(delayMs)
                        If settings_Flush_Timer Is Nothing Then
                            settings_Flush_Timer = New System.Windows.Forms.Timer()
                            AddHandler settings_Flush_Timer.Tick, AddressOf Settings_Flush_Timer_Tick
                        End If
                        settings_Flush_Timer.Stop()
                        settings_Flush_Timer.Interval = delayMs
                        settings_Flush_Timer.Start()
                    End Sub,
                    Sub()
                        If settings_Flush_Timer IsNot Nothing Then settings_Flush_Timer.Stop()
                    End Sub,
                    Sub(message) AppFileLogger.WriteLine(message), SettingsDebounceMilliseconds())
            End If
            Return settings_Scheduler
        End Get
    End Property

    Private Shared Function SettingsDebounceMilliseconds() As Integer
#If NETFRAMEWORK Then
        Return SettingsFlushScheduler.IdleDelayMs
#Else
        Return 1500
#End If
    End Function

    ''' <summary>Flushes the settings block right now - for changes that are deliberate
    ''' configuration rather than a side effect of browsing (a destination folder edited in
    ''' the settings window). Cancels any pending lazy flush.</summary>
    Friend Sub SaveSettingsNow()
        SettingsFlush.SaveNow(scheduleRetries:=True)
    End Sub

    ''' <summary>
    ''' Records that state worth keeping has changed, and schedules a flush. Call it from
    ''' anywhere a loss would cost the user real work - a destination folder edited, a recent
    ''' entry added, the folder position moved.
    '''
    ''' Trailing-edge, not immediate: the registry is not a place to write on every keypress,
    ''' and one flush a minute turns "everything since the last clean exit" into "at most the
    ''' last minute" on a crash.
    ''' </summary>
    Friend Sub MarkSettingsDirty()
        If Me.IsDisposed Then Return

        ' Some callers (the folder-combo refresh) run on the scanning thread, and a WinForms
        ' Timer may only be touched from the UI thread.
        If Me.IsHandleCreated AndAlso Me.InvokeRequired Then
            Try
                Me.BeginInvoke(New Action(AddressOf MarkSettingsDirty))
            Catch
            End Try
            Return
        End If

        SettingsFlush.MarkDirty()
    End Sub

    Private Sub Settings_Flush_Timer_Tick(sender As Object, e As EventArgs)
        If Me.IsDisposed Then Return
        SettingsFlush.OnTimerTick()
    End Sub

    ''' <summary>
    ''' Writes the whole settings block to the registry and answers whether EVERY write took.
    ''' It is idempotent: a retry rewrites the complete block. What stays pending, and when it is
    ''' tried again, is SettingsFlushScheduler's business (057.S13-11).
    ''' </summary>
    Private Function PersistSettings() As Boolean
        If MultiWindowPolicy.IsSecondaryInstance() Then Return True
        Try
            Dim entries As New List(Of KeyValuePair(Of String, String))()
            Dim add As Action(Of String, String) = Sub(name, value) entries.Add(New KeyValuePair(Of String, String)(name, value))

            Dim folder_To_Persist As String = Current_Folder_Path
#If Not NETFRAMEWORK Then
            folder_To_Persist = PersistableFolderPath()
#End If
            If folder_To_Persist IsNot Nothing Then add("ImageFolder", folder_To_Persist)
            add("LastCounter", current_File_Index.ToString)
#If Not NETFRAMEWORK Then
            add("LastPlayedFile", If(last_Played_File, ""))
#End If
            add("chkTopMost", If(chkbox_Top_Most.Checked, "1", "0"))
            add("ShowRecipientsOverlay", If(Is_Show_Recipients_Overlay, "1", "0"))
            add("WheelZooms", If(Zoom_Wheel_Zooms, "1", "0"))

            For z = 1 To 9
                add("MoveOn" & z.ToString, If(Hardkeys_to_move_mediafile(z), ""))
            Next
            add("MoveOn0", If(Hardkeys_to_move_mediafile(10), ""))

            Localization.SaveIfAbsent()
            add("FirstRun", "0")
            add("CopyMode", If(Is_Copying_not_Moving, "1", "0"))
            add("isPerspective", If(Is_Pespective, "1", "0"))
            add("DynamicPerspective", If(Is_Dynamic_Perspective, "1", "0"))
            add("AnimatedPerspective", If(Is_Animated_Perspective, "1", "0"))
            add("AnimatedPerspectiveSpeed", HaloSpeedToSetting(Halo_Animation_Speed))
            add("TableOpened", If(Table_Form IsNot Nothing AndAlso Table_Form.Visible, "1", "0"))
            add("RunsCount", (app_Run_Count + 1).ToString)
            add("mediaViewedCount", (media_View_Count).ToString)
            add("SortDir", (cmbox_Sort.SelectedIndex).ToString)
            add("color_scheme", (Form_Color_Scheme).ToString)
            add("RecentMediaFiles", String.Join("|", recent_Media_File_List))

            add("ShowPictureSizes", If(Is_to_show_picture_sizes, "1", "0"))
            add("ShowFileSizes", If(Is_to_show_file_sizes, "1", "0"))
            add("ShowFileDates", If(Is_to_show_file_datetime, "1", "0"))
            #If Not NETFRAMEWORK Then
            add("SlideshowIntervalMs", Slideshow_Base_Interval_Ms.ToString())
#End If
            add("IsVideoLoop", If(Is_Video_Loop, "1", "0"))

            add("ExifAutoRotate", If(Is_Exif_AutoRotate, "1", "0"))
            add("HighQualityScaling", If(Is_HighQuality_Scaling, "1", "0"))
            add("ShowInfoOverlay", If(Is_Show_Info_Overlay, "1", "0"))
            add("SlideshowIntervalSec", CInt(Slideshow_Base_Interval_Ms / 1000).ToString())
            add("NoRequestBeforeFileOperation", If(Is_no_request_before_file_operation, "1", "0"))

            add("Picture_Box_Width_At_Panel", Picture_Box_Width_At_Panel.ToString)
            add("Picture_Box_Height_At_Panel", Picture_Box_Height_At_Panel.ToString)

#If NETFRAMEWORK Then
            add("AppTop", Me.Top.ToString)
            add("AppLeft", Me.Left.ToString)
            If Me.Height >= main_form_position_Limit_Height_Low Then add("AppHeight", Me.Height.ToString)
            If Me.Width >= main_form_position_Limit_Width_Low Then add("AppWidth", Me.Width.ToString)
#Else
            Dim normal_Bounds As Rectangle = WindowPlacement.NormalRectangle(Me.WindowState, Me.Bounds, Me.RestoreBounds)
            add("AppTop", normal_Bounds.Top.ToString)
            add("AppLeft", normal_Bounds.Left.ToString)
            If normal_Bounds.Height >= main_form_position_Limit_Height_Low Then add("AppHeight", normal_Bounds.Height.ToString)
            If normal_Bounds.Width >= main_form_position_Limit_Width_Low Then add("AppWidth", normal_Bounds.Width.ToString)
            If Me.WindowState <> FormWindowState.Minimized Then
                Dim window_State As String = WindowPlacement.StateNormal
                If is_Full_Screen_Mode Then
                    window_State = WindowPlacement.StateFullScreen
                ElseIf Me.WindowState = FormWindowState.Maximized Then
                    window_State = WindowPlacement.StateMaximized
                End If
                add("AppWindowState", window_State)
                add("AppSuperFullScreen", If(is_Super_Full_Screen_Mode, "1", "0"))
            End If
#End If

            add("VideoVolume", video_Volume_Level.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
            add("VideoMuted", If(is_Video_Muted, "1", "0"))
            add("RecentFolders", String.Join("|", recent_Folder_List))

            add("UseIndependentThreadForOperationsWithFiles", If(Is_Independent_Thread_For_File_Operations, "1", "0"))

            Dim written As SettingsWriteResult = SettingsWriter.WriteAll(
                entries,
                Sub(name, value) SaveSetting(App_name, Second_App_Name, name, value),
                Sub(name, ex) AppFileLogger.LogException("Settings: saving " & name, ex))

            SaveOcrSettings()
#If Not NETFRAMEWORK Then
            If modern_Preferences IsNot Nothing Then
                modern_Preferences.PreferredAudioLanguage = preferred_Audio_Language
                modern_Preferences.PreferredSubtitleLanguage = preferred_Subtitle_Language
                If pending_Allow_New_Windows_Persistence Then
                    modern_Preferences.PersistAllowNewWindows()
                    pending_Allow_New_Windows_Persistence = False
                End If
                modern_Preferences.Save()
            End If
#End If

            If written.Succeeded Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1190: settings are saved")
                Return True
            End If
            AppFileLogger.WriteLine("Settings: " & written.Failed.Count.ToString() & " of " &
                                    (written.Failed.Count + written.Written).ToString() & " values were not written")
            Return False
        Catch ex As Exception
            AppFileLogger.LogException("Settings: saving the settings block", ex)
            Return False
        End Try
    End Function
End Class
