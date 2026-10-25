#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' The file manager mode of the F3 panel (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md, slices N3 and N4).
'
' Triage stays the default and the mode every opening starts in. The mode selector is the first item of the toolbar; the manager
' adds an address row (Back / Forward / Up / Refresh and the address box) and a places list on the left, and triage hides them. The
' list and grid stay the content area - the same two views over the same SelectionModel, fed from a ManagerBrowser instead of the
' main window's list.
'
' What this mode never does: it never calls SnapshotFileList, never sets Choosen_Picture_From_Panel, never opens a file in the
' main window and never starts a batch - browsing, selecting and closing leave what the viewer shows exactly as it was. Its
' connections are its own (ManagerProviderOpener): it never attaches one to the viewer's session, and ShutDownManager lets go of
' every one of them when the window closes. Modern build only; the x86 panel keeps the original.

Partial Public Class Image_Panel_Form

    ' --- controls ---------------------------------------------------------------------
    Private cmb_Mode As ToolStripComboBox
    Private manager_Header As Panel
    Private address_Box As TextBox
    Private btn_Back As Button
    Private btn_Forward As Button
    Private btn_Up As Button
    Private btn_Refresh As Button
    Private lst_Places As ListBox
    Private lbl_Placeholder As Label

    ' --- state ------------------------------------------------------------------------
    Private manager_Mode As Boolean
    ''' <summary>Counts the entries into and exits from the mode: a listing that was asked for in an earlier one is dropped when it lands.
    ''' (The thumbnail <c>session</c> is not used for this - a card-size change moves it, and must not eat a listing.)</summary>
    Private manager_Epoch As Integer
    Private browser As ManagerBrowser
    Private nav As ManagerNavigation
    Private place_List As New List(Of ManagerPlace)()
    Private resource_List As New List(Of ResourceDescriptor)()
    Private retry_Target As ManagerLocation
    Private ReadOnly entry_By_Identity As New Dictionary(Of String, ManagerEntry)(StringComparer.Ordinal)
    ''' <summary>Passwords the UI thread read for the worker: the stored one, or the one typed earlier this run. Never written anywhere.</summary>
    Private ReadOnly credential_Cache As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private suppress_Mode_Event As Boolean
    Private suppress_Place_Event As Boolean
    Private setting_Address As Boolean
    Private address_Edited As Boolean
    Friend Property ManagerChoice As ManagerActivationResult
    Private cmb_ContentFilter As ToolStripComboBox
    Private btn_HiddenFiles As ToolStripButton
    Private btn_ManagerSettings As ToolStripButton
    Private sep_ManagerFilter As ToolStripSeparator
    Private temp_ContentModeOverride As ManagerContentMode?
    Private temp_ShowHiddenOverride As Boolean?
    Private suppress_Filter_Events As Boolean
    Private unfiltered_Entries As IReadOnlyList(Of ManagerEntry) = Array.Empty(Of ManagerEntry)()

    ' ===================================================================== building ====

    Private Sub BuildModeSelector()
        Dim caption As String = Localization.T("Режим панели")
        cmb_Mode = New ToolStripComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .AutoSize = False,
            .Width = 168,
            .ToolTipText = caption,
            .AccessibleName = caption}
        cmb_Mode.Items.Add(Localization.T("Сортировка"))
        cmb_Mode.Items.Add(Localization.T("Файловый менеджер"))
        cmb_Mode.SelectedIndex = 0
        AddHandler cmb_Mode.SelectedIndexChanged, AddressOf OnModeChosen

        toolbar.Items.Add(cmb_Mode)
        toolbar.Items.Add(New ToolStripSeparator())

        sep_ManagerFilter = New ToolStripSeparator With {.Visible = False}
        toolbar.Items.Add(sep_ManagerFilter)

        cmb_ContentFilter = New ToolStripComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .AutoSize = False,
            .Width = 150,
            .ToolTipText = Localization.T("Фильтр содержимого"),
            .AccessibleName = Localization.T("Фильтр содержимого"),
            .Visible = False
        }
        cmb_ContentFilter.Items.Add(Localization.T("Медиафайлы"))
        cmb_ContentFilter.Items.Add(Localization.T("Медиа и документы"))
        cmb_ContentFilter.Items.Add(Localization.T("Все файлы"))
        cmb_ContentFilter.SelectedIndex = 0
        AddHandler cmb_ContentFilter.SelectedIndexChanged, AddressOf OnContentFilterChanged
        toolbar.Items.Add(cmb_ContentFilter)

        btn_HiddenFiles = New ToolStripButton(Localization.T("Скрытые")) With {
            .CheckOnClick = True,
            .ToolTipText = Localization.T("Показывать скрытые и системные файлы"),
            .AccessibleName = Localization.T("Показывать скрытые и системные файлы"),
            .Visible = False
        }
        AddHandler btn_HiddenFiles.Click, AddressOf OnHiddenFilesClicked
        toolbar.Items.Add(btn_HiddenFiles)

        btn_ManagerSettings = New ToolStripButton(Localization.T("Настройки..")) With {
            .ToolTipText = Localization.T("Настройки файлового менеджера"),
            .AccessibleName = Localization.T("Настройки файлового менеджера"),
            .Visible = False
        }
        AddHandler btn_ManagerSettings.Click, Sub() Main_Form.ShowSettingsWindowAtFileManager()
        toolbar.Items.Add(btn_ManagerSettings)
    End Sub

    Private Sub BuildFileManager()
        manager_Header = New Panel With {
            .Name = "panel_Manager_Header",
            .Dock = DockStyle.Top,
            .Height = 38,
            .Padding = New Padding(4, 3, 4, 3),
            .Visible = False}

        address_Box = New TextBox With {
            .Name = "txt_Manager_Address",
            .Dock = DockStyle.Top,
            .AccessibleName = Localization.T("Адрес (Alt+D)"),
            .RightToLeft = RightToLeft.No}
        AddHandler address_Box.TextChanged, Sub(s, e)
                                                If Not setting_Address Then address_Edited = True
                                            End Sub
        Dim address_Host As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(4, 3, 0, 0)}
        address_Host.Controls.Add(address_Box)

        btn_Refresh = NewNavButton("btn_Manager_Refresh", "action.refresh", Localization.T("Обновить (F5)"), AddressOf OnRefreshClicked)
        btn_Up = NewNavButton("btn_Manager_Up", "nav.scroll-top", Localization.T("Вверх (Alt+Up)"), AddressOf OnUpClicked)
        btn_Forward = NewNavButton("btn_Manager_Forward", "nav.forward", Localization.T("Вперёд (Alt+Right)"), AddressOf OnForwardClicked)
        btn_Back = NewNavButton("btn_Manager_Back", "nav.back", Localization.T("Назад (Alt+Left)"), AddressOf OnBackClicked)

        ' Fill first, then the edge buttons; the one added last is docked first, so Back ends up leftmost.
        manager_Header.Controls.Add(address_Host)
        manager_Header.Controls.Add(btn_Refresh)
        manager_Header.Controls.Add(btn_Up)
        manager_Header.Controls.Add(btn_Forward)
        manager_Header.Controls.Add(btn_Back)

        lst_Places = New ListBox With {
            .Name = "lst_Manager_Places",
            .Dock = DockStyle.Left,
            .Width = 190,
            .IntegralHeight = False,
            .HorizontalScrollbar = True,
            .BorderStyle = BorderStyle.None,
            .AccessibleName = Localization.T("Места"),
            .RightToLeft = RightToLeft.No,
            .Visible = False}
        AddHandler lst_Places.SelectedIndexChanged, AddressOf OnPlaceChosen

        lbl_Placeholder = New Label With {
            .Name = "lbl_Manager_Placeholder",
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter,
            .AutoEllipsis = True,
            .Padding = New Padding(16),
            .Visible = False}
    End Sub

    ''' <summary>Glyph only, like a toolbar button; the tooltip and the accessible name carry the caption and the key.</summary>
    Private Function NewNavButton(name As String, glyphId As String, caption As String, handler As EventHandler) As Button
        Dim b As New Button With {
            .Name = name,
            .Dock = DockStyle.Left,
            .Width = 38,
            .TabStop = False,
            .Enabled = False,
            .AccessibleName = caption}
        AddHandler b.Click, handler
        ButtonGlyphs.Apply(b, glyphId, ButtonGlyphRole.Neutral)
        Return b
    End Function

    Private Sub InitializeManagerTooltips()
        If tip Is Nothing Then Return
        tip.SetToolTip(btn_Back, btn_Back.AccessibleName)
        tip.SetToolTip(btn_Forward, btn_Forward.AccessibleName)
        tip.SetToolTip(btn_Up, btn_Up.AccessibleName)
        tip.SetToolTip(btn_Refresh, btn_Refresh.AccessibleName)
        tip.SetToolTip(address_Box, address_Box.AccessibleName)
    End Sub

    Private Sub ApplyManagerColors(back As Color, ink As Color)
        If lst_Places Is Nothing Then Return
        lst_Places.BackColor = back
        lst_Places.ForeColor = ink
        lbl_Placeholder.BackColor = back
        lbl_Placeholder.ForeColor = ink
    End Sub

    Private Sub SetManagerChromeVisible(visible As Boolean)
        If manager_Header IsNot Nothing Then manager_Header.Visible = visible
        If lst_Places IsNot Nothing Then lst_Places.Visible = visible
        If cmb_ContentFilter IsNot Nothing Then cmb_ContentFilter.Visible = visible
        If btn_HiddenFiles IsNot Nothing Then btn_HiddenFiles.Visible = visible
        If btn_ManagerSettings IsNot Nothing Then btn_ManagerSettings.Visible = visible
        If sep_ManagerFilter IsNot Nothing Then sep_ManagerFilter.Visible = visible
        UpdateManagerCommands()
    End Sub

    Private Sub SetModeSelection(manager As Boolean)
        If cmb_Mode Is Nothing Then Return
        suppress_Mode_Event = True
        Try
            cmb_Mode.SelectedIndex = If(manager, 1, 0)
        Finally
            suppress_Mode_Event = False
        End Try
    End Sub

    ' ================================================================== mode switch ====

    Private Sub OnModeChosen(sender As Object, e As EventArgs)
        If suppress_Mode_Event Then Return
        Dim wantManager As Boolean = cmb_Mode.SelectedIndex = 1
        If wantManager = manager_Mode Then Return

        ' A running batch edits the list this panel shows: not now. The band says why, and the selector goes back.
        If Main_Form.BatchIsRunning Then
            SetModeSelection(manager_Mode)
            ShowBusyBand()
            Return
        End If

        If wantManager Then EnterManagerMode() Else ReturnToTriage()
        FocusActiveView()
    End Sub

    Private Sub EnterManagerMode()
        manager_Mode = True
        Interlocked.Increment(manager_Epoch)
        pending_Removed.Clear()
        flush_Timer.Stop()
        HideBusyBand()

        ' Rows are identified by place and path now, compared ordinally; the cache and the loader are rebuilt for that.
        identity_Comparer = StringComparer.Ordinal
        manager_Model.Reset()
        manager_Model.SetItems(Nothing)
        model = manager_Model
        row_By_Identity = New Dictionary(Of String, PanelFileRow)(StringComparer.Ordinal)
        no_Picture = New HashSet(Of String)(StringComparer.Ordinal)
        entry_By_Identity.Clear()
        rows = New List(Of PanelFileRow)()
        display = New List(Of PanelFileRow)()
        target_File = ""
        grid.TargetPath = ""
        RestartThumbnails()
        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        view_Is_List = String.Equals(prefs.FileManagerViewMode, "list", StringComparison.Ordinal)
        ApplyViewMode()
        sort_Column = PanelSortColumn.Name
        sort_Descending = False
        RebuildDisplay()

        SetManagerChromeVisible(True)
        resource_List = LoadResources()
        RefreshPlaces()
        nav = New ManagerNavigation()
        retry_Target = Nothing
        browser = New ManagerBrowser(ManagerProviderOpener.Create(AddressOf LoadResourcesForOpener, AddressOf CredentialFor), AddressOf OnBrowserFault)
        AddHandler browser.Completed, AddressOf OnBrowserCompleted
        InitializeManagerTooltips()
        ShowIdleStrip()
        StartManagerExchange()

        Dim start As ManagerLocation = StartLocation()
        UpdateManagerChrome()
        If start Is Nothing Then
            ShowPlaceholder(Localization.T("Выберите место в списке слева"))
        Else
            HidePlaceholder()
            NavigateTo(ManagerIntent.Start, start)
        End If
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " p0200: PicPanel file manager entered")
    End Sub

    Private Sub ReturnToTriage()
        LeaveManagerMode()
        RestartThumbnails()
        sort_Column = PanelSortColumn.None
        sort_Descending = False
        view_Is_List = String.Equals(Main_Form.GetModernPreferences().PanelViewMode, "list", StringComparison.Ordinal)
        LoadFromMain(keepSelection:=False)
        ApplyViewMode()
        SelectOpeningTarget()
        ShowIdleStrip()
        RefillLoader()
    End Sub

    ''' <summary>Everything the mode owns goes: the browser (its connections are closed here, on the calling thread), the manager's
    ''' rows and the chrome. Idempotent - the panel's EndSession calls it on every opening, so a window closed in manager mode
    ''' reopens as triage (257). Does not touch the thumbnail cache and loader: the callers rebuild or have disposed them.</summary>
    Private Sub LeaveManagerMode()
        Interlocked.Increment(manager_Epoch)
        StopManagerExchange()
        DisposeBrowser()
        nav = Nothing
        retry_Target = Nothing
        manager_Mode = False
        identity_Comparer = Nothing
        model = triage_Model
        manager_Model.SetItems(Nothing)
        manager_Model.Reset()
        row_By_Identity = New Dictionary(Of String, PanelFileRow)(StringComparer.OrdinalIgnoreCase)
        no_Picture = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        entry_By_Identity.Clear()
        place_List.Clear()
        If lst_Places IsNot Nothing Then lst_Places.Items.Clear()
        SetManagerChromeVisible(False)
        HidePlaceholder()
        SetModeSelection(False)
    End Sub

    ''' <summary>The window is closing: a listing in flight is cancelled and every connection the manager opened is closed, so no
    ''' provider lease and no thumbnail request outlives the panel (257 N4).</summary>
    Private Sub ShutDownManager()
        ClearManagerClipboard()
        If Not manager_Mode Then Return
        loader?.SetWanted(Nothing)
        LeaveManagerMode()
    End Sub

    Private Sub DisposeBrowser()
        credential_Cache.Clear()
        If browser Is Nothing Then Return
        Dim closing As ManagerBrowser = browser
        browser = Nothing
        RemoveHandler closing.Completed, AddressOf OnBrowserCompleted
        closing.Cancel()
        closing.Dispose()
    End Sub

    ''' <summary>A cache and a loader that match the identity comparer in force: both mode switches call it.</summary>
    Private Sub RestartThumbnails()
        If loader IsNot Nothing Then
            loader.Dispose()
            loader = Nothing
        End If
        If memory IsNot Nothing Then
            memory.Dispose()
            memory = Nothing
        End If
        BeginSession(Main_Form.GetModernPreferences())
    End Sub

    ' ======================================================================== places ====

    Private Shared Function LoadResources() As List(Of ResourceDescriptor)
        Try
            Return ResourceRegistry.Load()
        Catch ex As Exception
            AppFileLogger.LogException("File manager: resource list", ex)
            Return New List(Of ResourceDescriptor)()
        End Try
    End Function

    ''' <summary>For the worker: read again on every open so a resource removed meanwhile is noticed.</summary>
    Private Shared Function LoadResourcesForOpener() As IEnumerable(Of ResourceDescriptor)
        Return LoadResources()
    End Function

    Private Function CredentialFor(descriptor As ResourceDescriptor) As String
        Dim password As String = Nothing
        If descriptor IsNot Nothing AndAlso credential_Cache.TryGetValue(descriptor.Id, password) Then Return password
        Return Nothing
    End Function

    ''' <summary>On the UI thread, before the worker needs it: the stored or this-run password of the place's resource, if there is one.
    ''' There is never a prompt - a resource that needs one is a "sign in first" status.</summary>
    Private Sub PrimeCredential(place As ManagerPlace)
        If place Is Nothing OrElse Not place.NeedsConnection OrElse place.ResourceId.Length = 0 Then Return
        Dim descriptor As ResourceDescriptor = resource_List.FirstOrDefault(
            Function(r) String.Equals(r.Id, place.ResourceId, StringComparison.OrdinalIgnoreCase))
        If descriptor Is Nothing Then Return
        Dim password As String = Nothing
        Try
            password = Main_Form.ManagerCredentialOf(descriptor)
        Catch ex As Exception
            AppFileLogger.LogException("File manager: credential", ex)
        End Try
        If password Is Nothing Then
            Dim dropped As String = Nothing
            credential_Cache.TryRemove(descriptor.Id, dropped)
        Else
            credential_Cache(descriptor.Id) = password
        End If
    End Sub

    Private Sub RefreshPlaces()
        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        place_List = ManagerAddress.BuildPlaces(Environment.GetLogicalDrives(), resource_List, prefs.FileManagerShowLocalPlaces, prefs.FileManagerShowResourcePlaces)
        suppress_Place_Event = True
        Try
            lst_Places.BeginUpdate()
            lst_Places.Items.Clear()
            For Each place As ManagerPlace In place_List
                lst_Places.Items.Add(place)
            Next
            AppendManagerExchangePlaces()
            lst_Places.EndUpdate()
        Finally
            suppress_Place_Event = False
        End Try
        HighlightCurrentPlace()
    End Sub

    Private Sub HighlightCurrentPlace()
        If lst_Places Is Nothing Then Return
        Dim at As Integer = -1
        If nav IsNot Nothing AndAlso nav.Current IsNot Nothing Then
            Dim wanted As String = nav.Current.Place.Id
            For i As Integer = 0 To lst_Places.Items.Count - 1
                Dim place As ManagerPlace = TryCast(lst_Places.Items(i), ManagerPlace)
                If place Is Nothing Then place = TryCast(lst_Places.Items(i), ManagerExchangeNode)?.Place
                If place IsNot Nothing AndAlso place.Id = wanted Then at = i : Exit For
            Next
        End If
        suppress_Place_Event = True
        Try
            If lst_Places.SelectedIndex <> at Then lst_Places.SelectedIndex = at
        Finally
            suppress_Place_Event = False
        End Try
    End Sub

    Private Sub OnPlaceChosen(sender As Object, e As EventArgs)
        If suppress_Place_Event OrElse Not manager_Mode Then Return
        Dim node As ManagerExchangeNode = TryCast(lst_Places.SelectedItem, ManagerExchangeNode)
        If node IsNot Nothing Then
            OpenManagerExchangeNode(node)
            Return
        End If
        Dim place As ManagerPlace = TryCast(lst_Places.SelectedItem, ManagerPlace)
        If place Is Nothing Then Return
        NavigateTo(ManagerIntent.Go, ManagerLocation.AtRoot(place))
    End Sub

    ''' <summary>Where the viewer stands, as a manager location - read from three fields, never by listing or snapshotting anything.</summary>
    Private Function StartLocation() As ManagerLocation
        Dim resourceId As String = ""
        Dim folder As String = ""
        Main_Form.ManagerStartFolder(resourceId, folder)
        If resourceId.Length > 0 Then
            Dim descriptor As ResourceDescriptor = resource_List.FirstOrDefault(
                Function(r) String.Equals(r.Id, resourceId, StringComparison.OrdinalIgnoreCase))
            If descriptor IsNot Nothing Then
                Dim place As ManagerPlace = ManagerPlace.ForResource(descriptor)
                Return New ManagerLocation(place, If(String.IsNullOrEmpty(folder), place.RootPath, folder))
            End If
        End If
        If String.IsNullOrWhiteSpace(folder) Then Return Nothing
        Dim resolved As ManagerAddressResult = ManagerAddress.Resolve(folder, resource_List)
        Return If(resolved.Kind = ManagerAddressKind.Ok, resolved.Location, Nothing)
    End Function

    ' =================================================================== navigation ====

    Private Sub NavigateTo(intent As ManagerIntent, target As ManagerLocation)
        If target Is Nothing OrElse browser Is Nothing OrElse nav Is Nothing OrElse Me.IsDisposed Then Return
        retry_Target = target

        ' What the view holds NOW is what a refresh of this folder puts back; a move to another folder clears it.
        If nav.Current IsNot Nothing Then
            nav.RememberSelection(model.SelectedPaths(), If(model.FocusIndex >= 0, model.PathAt(model.FocusIndex), Nothing))
        End If

        If nav.Current Is Nothing OrElse Not nav.Current.SameAs(target) Then
            temp_ContentModeOverride = Nothing
            temp_ShowHiddenOverride = Nothing
        End If

        If target.Place IsNot Nothing AndAlso (nav.Current Is Nothing OrElse Not String.Equals(nav.Current.Place.Id, target.Place.Id, StringComparison.Ordinal)) Then
            Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
            If prefs.FileManagerRememberSort Then
                Dim placeCol As PanelSortColumn
                Dim placeDesc As Boolean
                If prefs.GetPlaceSort(target.Place.Id, placeCol, placeDesc) Then
                    sort_Column = placeCol
                    sort_Descending = placeDesc
                End If
            End If
        End If

        PrimeCredential(target.Place)
        ShowStrip(Localization.TF("Чтение: {0}.. (Esc - отмена)", AddressText(target)), running:=False, clickable:=False, warning:=False)
        UpdateManagerChrome()
        Call browser.Begin(intent, target)
    End Sub

    ''' <summary>On a worker thread.</summary>
    Private Sub OnBrowserCompleted(sender As Object, result As ManagerListingResult)
        Dim epoch As Integer = Volatile.Read(manager_Epoch)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            BeginInvoke(New Action(Sub() ApplyManagerResult(epoch, result)))
        Catch ex As Exception
            ' The form went away between the check and the post: nothing is waiting for the listing.
        End Try
    End Sub

    Private Shared Sub OnBrowserFault(ex As Exception)
        AppFileLogger.LogException("File manager: listing", ex)
    End Sub

    ''' <summary>The one place a finished listing reaches the screen. A listing from an earlier mode entry or one that a newer navigation
    ''' (or Esc) overtook is dropped; a failed one changes neither the listing nor the history and says why (257: distinct statuses).</summary>
    Private Sub ApplyManagerResult(epoch As Integer, result As ManagerListingResult)
        If epoch <> Volatile.Read(manager_Epoch) OrElse Not manager_Mode OrElse Me.IsDisposed Then Return
        If browser Is Nothing OrElse nav Is Nothing OrElse Not browser.IsCurrent(result.Generation) Then Return

        Dim applied As ManagerApply = nav.Apply(result)
        If applied = ManagerApply.Kept Then
            If Not result.IsListed Then
                Dim said As String = StatusText(result)
                ShowStrip(said, running:=False, clickable:=False, warning:=True)
                AppFileLogger.WriteLine("File manager: listing refused - " & result.Status.ToString())
                If nav.Current Is Nothing Then ShowPlaceholder(said)
            End If
            UpdateManagerChrome()
            Return
        End If

        unfiltered_Entries = If(result.Entries, DirectCast(Array.Empty(Of ManagerEntry)(), IReadOnlyList(Of ManagerEntry)))
        Dim filtered As List(Of ManagerEntry) = ManagerContentPolicy.Filter(unfiltered_Entries, EffectiveContentMode(), EffectiveShowHidden())
        Dim fresh As List(Of PanelFileRow) = ManagerEntry.RowsOf(filtered)
        entry_By_Identity.Clear()
        row_By_Identity.Clear()
        For i As Integer = 0 To fresh.Count - 1
            entry_By_Identity(filtered(i).Id) = filtered(i)
            row_By_Identity(fresh(i).Identity) = fresh(i)
        Next
        rows = fresh
        UpdateFilterControls()

        If applied = ManagerApply.Moved Then
            ' A different folder: no selection carries over and the bitmaps of the last one are not wanted.
            model.Reset()
            memory?.Clear()
            no_Picture.Clear()
            Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
            If prefs.FileManagerRememberSort AndAlso nav.Current?.Place IsNot Nothing Then
                Dim col As PanelSortColumn = PanelSortColumn.Name
                Dim desc As Boolean = False
                If prefs.GetPlaceSort(nav.Current.Place.Id, col, desc) Then
                    sort_Column = col
                    sort_Descending = desc
                Else
                    sort_Column = PanelSortColumn.Name
                    sort_Descending = False
                End If
            End If
        Else
            nav.Reconcile(fresh.Select(Function(r) r.Identity))
        End If

        RebuildDisplay()
        If applied = ManagerApply.Moved AndAlso display.Count > 0 Then
            If view_Is_List Then details.ShowRow(0) Else grid.EnsureVisible(0)
        End If
        RefillLoader()
        UpdateManagerChrome()
        browser.KeepOnly(nav.Current.Place.Id)

        If result.IsTruncated Then
            ShowStrip(StatusText(result), running:=False, clickable:=False, warning:=True)
        Else
            ShowIdleStrip()
        End If
        If fresh.Count = 0 Then
            ShowPlaceholder(Localization.T("Папка пуста"))
        Else
            HidePlaceholder()
        End If
    End Sub

    Private Shared Function StatusText(result As ManagerListingResult) As String
        Select Case result.Status
            Case ManagerStatusKind.Denied, ManagerStatusKind.Disconnected
                If result.Failure IsNot Nothing Then
                    Dim reason As String = ResourceMessages.ForFailure(result.Failure)
                    If Not String.IsNullOrWhiteSpace(reason) Then Return reason
                End If
                Return If(result.Status = ManagerStatusKind.Denied,
                          Localization.T("Нет доступа к папке"), Localization.T("Нет связи с ресурсом"))
            Case ManagerStatusKind.Missing
                Return Localization.T("Папка не найдена")
            Case ManagerStatusKind.NotADirectory
                Return Localization.T("Это не папка")
            Case ManagerStatusKind.Cancelled
                Return Localization.T("Чтение папки отменено")
            Case ManagerStatusKind.OutsideRoot
                Return Localization.T("Путь выходит за пределы этого места")
            Case ManagerStatusKind.SignInNeeded
                Return Localization.T("Для этого ресурса нужен пароль - подключитесь к нему в главном окне")
            Case ManagerStatusKind.HostKeyUnknown
                Return Localization.T("Ключ сервера не подтверждён - подключитесь к нему в главном окне")
            Case ManagerStatusKind.Truncated
                Return Localization.T("Показана только часть папки - список неполный")
            Case Else
                Return Localization.T("Это место недоступно")
        End Select
    End Function

    ''' <summary>The address as it is shown: a Windows path as it is, a server folder as the locator that names it.</summary>
    Private Function AddressText(location As ManagerLocation) As String
        If location Is Nothing Then Return ""
        If location.Place.Kind = ResourceKind.Sftp Then
            Dim descriptor As ResourceDescriptor = resource_List.FirstOrDefault(
                Function(r) String.Equals(r.Id, location.Place.ResourceId, StringComparison.OrdinalIgnoreCase))
            If descriptor IsNot Nothing Then Return ResourceLocator.Build(descriptor, location.DirectoryPath)
        End If
        Return location.DirectoryPath
    End Function

    ''' <summary>Buttons, address text and the highlighted place follow the committed location.</summary>
    Private Sub UpdateManagerChrome()
        UpdateManagerCommands()
        Dim has_Nav As Boolean = nav IsNot Nothing
        btn_Back.Enabled = has_Nav AndAlso nav.CanBack
        btn_Forward.Enabled = has_Nav AndAlso nav.CanForward
        btn_Up.Enabled = has_Nav AndAlso nav.CanGoUp
        btn_Refresh.Enabled = has_Nav AndAlso (nav.Current IsNot Nothing OrElse retry_Target IsNot Nothing)
        ' Text the user is in the middle of typing is not overwritten by a listing that lands.
        If Not address_Edited Then SetAddressText(If(has_Nav, AddressText(nav.Current), ""))
        HighlightCurrentPlace()
    End Sub

    Private Sub SetAddressText(text As String)
        setting_Address = True
        Try
            address_Box.Text = text
        Finally
            setting_Address = False
        End Try
        address_Edited = False
    End Sub

    Private Sub ShowPlaceholder(text As String)
        lbl_Placeholder.Text = text
        lbl_Placeholder.Visible = True
        lbl_Placeholder.BringToFront()
    End Sub

    Private Sub HidePlaceholder()
        If lbl_Placeholder IsNot Nothing Then lbl_Placeholder.Visible = False
    End Sub

    ' ============================================================== the four buttons ====

    Private Sub OnBackClicked(sender As Object, e As EventArgs)
        GoBack()
        FocusActiveView()
    End Sub

    Private Sub OnForwardClicked(sender As Object, e As EventArgs)
        GoForward()
        FocusActiveView()
    End Sub

    Private Sub OnUpClicked(sender As Object, e As EventArgs)
        GoUp()
        FocusActiveView()
    End Sub

    Private Sub OnRefreshClicked(sender As Object, e As EventArgs)
        RefreshManager()
        FocusActiveView()
    End Sub

    Private Sub GoBack()
        If Not manager_Mode OrElse nav Is Nothing Then Return
        Dim target As ManagerLocation = nav.PeekBack()
        If target IsNot Nothing Then NavigateTo(ManagerIntent.Back, target)
    End Sub

    Private Sub GoForward()
        If Not manager_Mode OrElse nav Is Nothing Then Return
        Dim target As ManagerLocation = nav.PeekForward()
        If target IsNot Nothing Then NavigateTo(ManagerIntent.Forward, target)
    End Sub

    Private Sub GoUp()
        If Not manager_Mode OrElse nav Is Nothing Then Return
        Dim target As ManagerLocation = nav.ParentTarget()
        If target IsNot Nothing Then NavigateTo(ManagerIntent.Up, target)
    End Sub

    ''' <summary>The same folder again, selection reconciled by identity. Before the first listing it tries that listing again.</summary>
    Private Sub RefreshManager()
        If Not manager_Mode OrElse nav Is Nothing Then Return
        StartManagerExchange()
        resource_List = LoadResources()
        RefreshPlaces()
        RefreshManagerExchange()
        If nav.Current IsNot Nothing Then
            NavigateTo(ManagerIntent.Refresh, nav.Current)
        ElseIf retry_Target IsNot Nothing Then
            NavigateTo(ManagerIntent.Start, retry_Target)
        End If
    End Sub

    Private Sub CancelNavigation()
        If browser Is Nothing Then Return
        browser.Cancel()
        Dim said As String = Localization.T("Чтение папки отменено")
        ShowStrip(said, running:=False, clickable:=False, warning:=True)
        If nav IsNot Nothing AndAlso nav.Current Is Nothing Then ShowPlaceholder(said)
        UpdateManagerChrome()
    End Sub

    ' ================================================================== address box ====

    Private Sub FocusAddress()
        address_Box.Focus()
        address_Box.SelectAll()
    End Sub

    Private Sub RevertAddress()
        address_Edited = False
        SetAddressText(If(nav Is Nothing, "", AddressText(nav.Current)))
    End Sub

    ''' <summary>Enter in the address box. The text names a local absolute path or a locator of a resource that is already configured;
    ''' nothing else, and no file operation of any kind, is started by typing.</summary>
    Private Sub CommitAddress()
        If nav Is Nothing Then Return
        resource_List = LoadResources()
        Dim resolved As ManagerAddressResult = ManagerAddress.Resolve(address_Box.Text, resource_List)
        Select Case resolved.Kind
            Case ManagerAddressKind.Ok
                address_Edited = False
                FocusActiveView()
                NavigateTo(ManagerIntent.Go, resolved.Location)
            Case ManagerAddressKind.UnknownResource
                ShowStrip(Localization.T("Такого ресурса нет в списке - добавьте его в настройках"), running:=False, clickable:=False, warning:=True)
            Case ManagerAddressKind.AmbiguousResource
                ShowStrip(Localization.T("Выберите место в списке слева"), running:=False, clickable:=False, warning:=True)
            Case Else
                ShowStrip(Localization.T("Адрес не распознан"), running:=False, clickable:=False, warning:=True)
        End Select
    End Sub

    ' ===================================================================== activation ====

    Private Sub ActivateItem(index As Integer)
        If manager_Mode Then
            ManagerActivate(index)
        Else
            OpenInMain(index)
        End If
    End Sub

    Private Function EffectiveContentMode() As ManagerContentMode
        If temp_ContentModeOverride.HasValue Then Return temp_ContentModeOverride.Value
        If nav?.Current?.Place IsNot Nothing AndAlso nav.Current.Place.Kind <> ResourceKind.Local Then
            Dim descriptor = resource_List.FirstOrDefault(Function(r) String.Equals(r.Id, nav.Current.Place.ResourceId, StringComparison.OrdinalIgnoreCase))
            If descriptor IsNot Nothing AndAlso descriptor.AllFiles.HasValue Then
                Return If(descriptor.AllFiles.Value, ManagerContentMode.All, ManagerContentMode.Media)
            End If
        End If
        Dim prefs = Main_Form.GetModernPreferences()
        Select Case prefs.FileManagerContentMode
            Case "documents" : Return ManagerContentMode.Documents
            Case "all" : Return ManagerContentMode.All
            Case Else : Return ManagerContentMode.Media
        End Select
    End Function

    Private Function EffectiveShowHidden() As Boolean
        If temp_ShowHiddenOverride.HasValue Then Return temp_ShowHiddenOverride.Value
        If nav?.Current?.Place IsNot Nothing AndAlso nav.Current.Place.Kind <> ResourceKind.Local Then
            Dim descriptor = resource_List.FirstOrDefault(Function(r) String.Equals(r.Id, nav.Current.Place.ResourceId, StringComparison.OrdinalIgnoreCase))
            If descriptor IsNot Nothing AndAlso descriptor.ShowHiddenFiles.HasValue Then
                Return descriptor.ShowHiddenFiles.Value
            End If
        End If
        Return Main_Form.GetModernPreferences().FileManagerShowHiddenFiles
    End Function

    Private Sub UpdateFilterControls()
        If cmb_ContentFilter Is Nothing OrElse btn_HiddenFiles Is Nothing Then Return
        suppress_Filter_Events = True
        Try
            Dim modeIndex As Integer = CInt(EffectiveContentMode())
            If modeIndex >= 0 AndAlso modeIndex < cmb_ContentFilter.Items.Count Then
                cmb_ContentFilter.SelectedIndex = modeIndex
            End If
            btn_HiddenFiles.Checked = EffectiveShowHidden()
        Finally
            suppress_Filter_Events = False
        End Try
    End Sub

    Private Sub RefilterCurrentListing()
        If unfiltered_Entries Is Nothing Then Return
        Dim filtered As List(Of ManagerEntry) = ManagerContentPolicy.Filter(unfiltered_Entries, EffectiveContentMode(), EffectiveShowHidden())
        Dim fresh As List(Of PanelFileRow) = ManagerEntry.RowsOf(filtered)
        entry_By_Identity.Clear()
        row_By_Identity.Clear()
        For i As Integer = 0 To fresh.Count - 1
            entry_By_Identity(filtered(i).Id) = filtered(i)
            row_By_Identity(fresh(i).Identity) = fresh(i)
        Next
        rows = fresh
        RebuildDisplay()
        RefillLoader()
        UpdateManagerChrome()
    End Sub

    Private Sub OnContentFilterChanged(sender As Object, e As EventArgs)
        If suppress_Filter_Events OrElse Not manager_Mode Then Return
        temp_ContentModeOverride = CType(cmb_ContentFilter.SelectedIndex, ManagerContentMode)
        RefilterCurrentListing()
    End Sub

    Private Sub OnHiddenFilesClicked(sender As Object, e As EventArgs)
        If suppress_Filter_Events OrElse Not manager_Mode Then Return
        temp_ShowHiddenOverride = btn_HiddenFiles.Checked
        RefilterCurrentListing()
    End Sub

    Private Sub ManagerActivate(index As Integer)
        Dim identity As String = model.PathAt(index)
        Dim entry As ManagerEntry = Nothing
        If identity Is Nothing OrElse Not entry_By_Identity.TryGetValue(identity, entry) Then Return
        If nav Is Nothing OrElse nav.Current Is Nothing Then Return

        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        Dim isLocal As Boolean = Not nav.Current.Place.NeedsConnection
        Dim decision As ManagerOpenDecision = ManagerOpenPolicy.Evaluate(entry, prefs.FileManagerNonMediaOpenAction)

        Select Case decision.Kind
            Case ManagerOpenKind.Navigate
                If Not entry.CanEnter Then Return
                NavigateTo(ManagerIntent.Go, nav.Current.WithDirectory(entry.ProviderPath))
                Return

            Case ManagerOpenKind.ViewerHandoff
                ManagerChoice = New ManagerActivationResult(nav.Current, entry)
                Close()

            Case ManagerOpenKind.Blocked
                Dim message As String = Localization.TF("Запуск файлов с расширением {0} заблокирован из соображений безопасности.", decision.BlockedExtension)
                ShowStrip(message, running:=False, clickable:=decision.CanRevealInExplorer, warning:=True)
                If decision.CanRevealInExplorer Then
                    Dim localPathToReveal As String = entry.ProviderPath
                    strip_Action = Sub() ManagerShellOpen.RevealInExplorer(localPathToReveal)
                End If

            Case ManagerOpenKind.ExternalOpen
                Dim usePicker As Boolean = decision.UsePicker OrElse String.Equals(prefs.FileManagerNonMediaOpenAction, "openWith", StringComparison.OrdinalIgnoreCase)

                If isLocal Then
                    ManagerShellOpen.OpenFile(Me.Handle, entry.ProviderPath, usePicker)
                Else
                    OpenRemoteExternalFile(entry, usePicker)
                End If

            Case ManagerOpenKind.Refused
                ShowStrip(decision.Reason, running:=False, clickable:=False, warning:=True)
        End Select
    End Sub

    Private Async Sub OpenRemoteExternalFile(entry As ManagerEntry, usePicker As Boolean)
        If browser Is Nothing OrElse nav?.Current Is Nothing Then Return
        Dim prefs As ModernViewerPreferences = Main_Form.GetModernPreferences()
        If Not ManagerExternalCache.IsWithinBudget(entry.SizeBytes, prefs.RemoteCacheMaxMb) Then
            ShowStrip(Localization.T("Размер файла превышает лимит кэша."), running:=False, clickable:=False, warning:=True)
            Return
        End If

        ManagerExternalCache.PruneUnleased(prefs.RemoteCacheMaxMb)
        Dim targetPath As String = ManagerExternalCache.PrepareLeasePath(nav.Current.Place.ResourceId, entry.ProviderPath, entry.Name)

        ShowStrip(Localization.TF("Загрузка: {0}...", entry.Name), running:=True, clickable:=False, warning:=False)
        Dim place As ManagerPlace = nav.Current.Place
        Dim location As ManagerLocation = nav.Current
        Dim capturedBrowser As ManagerBrowser = browser
        Dim epoch As Integer = manager_Epoch
        Dim success As Boolean = False

        Try
            Using lease As ManagerEndpointLease = Await CaptureManagerEndpoint(location, capturedBrowser)
                Dim provider As IFileSystemProvider = lease.Provider
                Await Task.Run(Sub()
                    Dim caps As ProviderCapabilities = lease.Capabilities
                    If Not ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.Copy) Then Throw ManagerCommandPlanner.Failure(entry.ProviderPath, ManagerFailure.Permission)
                    ResourceTransfer.DownloadVerified(provider, entry.ProviderPath, targetPath, True, Nothing, CancellationToken.None)
                End Sub)
            End Using
            success = System.IO.File.Exists(targetPath)
        Catch ex As Exception
            AppFileLogger.LogException("OpenRemoteExternalFile", ex)
            If Not IsDisposed AndAlso epoch = manager_Epoch Then ShowStrip(FailureText.Describe(ex), running:=False, clickable:=False, warning:=True)
            Return
        End Try

        If success AndAlso Not IsDisposed AndAlso epoch = manager_Epoch Then
            ManagerExternalCache.RetainLease(targetPath)
            ShowStrip(ManagerExternalCache.RemoteNoticeText(), running:=False, clickable:=False, warning:=False)
            ManagerShellOpen.OpenFile(Me.Handle, targetPath, usePicker)
        End If
    End Sub

    ' ========================================================================= keys ====

    Private Shared Function Consume(e As KeyEventArgs) As Boolean
        e.Handled = True
        e.SuppressKeyPress = True
        Return True
    End Function

    ''' <summary>
    ''' The keys of the mode, asked before the triage handlers. True = the key is done with (either consumed here, or left for the
    ''' focused text box / list to use); False = go on to the shared handlers (arrows, space, Ctrl+A, Enter, Ctrl+L, Esc to close).
    ''' Delete, the digits and Ctrl+Z never reach a file operation from here, and an address box keeps every editing key.
    ''' </summary>
    Private Function HandleManagerKey(e As KeyEventArgs) As Boolean
        Dim typing As Boolean = address_Box IsNot Nothing AndAlso address_Box.Focused
        Dim in_Places As Boolean = lst_Places IsNot Nothing AndAlso lst_Places.Focused

        If e.Alt AndAlso Not e.Control Then
            Select Case e.KeyCode
                Case Keys.Left
                    GoBack()
                    Return Consume(e)
                Case Keys.Right
                    GoForward()
                    Return Consume(e)
                Case Keys.Up
                    GoUp()
                    Return Consume(e)
                Case Keys.D
                    FocusAddress()
                    Return Consume(e)
                Case Keys.Enter
                    If typing Then Return True
                    ShowManagerProperties()
                    Return Consume(e)
                Case Else
                    Return typing
            End Select
        End If

        If e.KeyCode = Keys.F5 AndAlso Not e.Control AndAlso Not e.Shift Then
            RefreshManager()
            Return Consume(e)
        End If

        If typing Then
            Select Case e.KeyCode
                Case Keys.Enter
                    CommitAddress()
                    Return Consume(e)
                Case Keys.Escape
                    If browser IsNot Nothing AndAlso browser.IsBusy Then
                        CancelNavigation()
                    Else
                        RevertAddress()
                        FocusActiveView()
                    End If
                    Return Consume(e)
                Case Else
                    Return True
            End Select
        End If

        If in_Places Then
            Select Case e.KeyCode
                Case Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Enter, Keys.Space
                    Return True
            End Select
        End If

        If e.KeyCode = Keys.Escape Then
            If browser IsNot Nothing AndAlso browser.IsBusy Then
                CancelNavigation()
                Return Consume(e)
            End If
            Return False
        End If

        If Not e.Alt Then
            Dim top_Row As Boolean
            Dim is_Slot As Boolean = Not e.Control AndAlso SlotFromKey(e, top_Row) >= 0
            Dim is_Delete As Boolean = Not e.Control AndAlso e.KeyCode = Keys.Delete
            If is_Slot Then
                ManagerSlot(e, SlotFromKey(e, top_Row), top_Row)
                Return Consume(e)
            End If
            If is_Delete Then
                RunManagerCommand(If(e.Shift, ManagerCommandKind.DeletePermanently, ManagerCommandKind.Delete))
                Return Consume(e)
            End If
            If e.Control AndAlso e.KeyCode = Keys.C Then
                CaptureManagerSelection(False)
                Return Consume(e)
            End If
            If e.Control AndAlso e.KeyCode = Keys.X Then
                CaptureManagerSelection(True)
                Return Consume(e)
            End If
            If e.Control AndAlso e.KeyCode = Keys.V Then
                PasteManagerSelection()
                Return Consume(e)
            End If
            If e.Control AndAlso e.Shift AndAlso e.KeyCode = Keys.N Then
                RunManagerCommand(ManagerCommandKind.CreateDirectory)
                Return Consume(e)
            End If
            If Not e.Control AndAlso e.KeyCode = Keys.F2 Then
                RunManagerCommand(ManagerCommandKind.Rename)
                Return Consume(e)
            End If
        End If
        Return False
    End Function

    ''' <summary>The help of the mode being shown: F1 and the hover on empty space.</summary>
    Private Function CurrentHelp() As String
        Return If(manager_Mode, ManagerHelpText(), HelpText())
    End Function

    Private Shared Function ManagerHelpText() As String
        Return Localization.T("Папки устройств подключаются по вашему выбору. Передача между ресурсами идёт через этот ПК; запись зависит от доступа к папке.") & vbCrLf &
               Localization.T("Файловый менеджер: двойной клик или Enter по папке - войти в неё; просмотр, выбор и закрытие панели не меняют то, что показывает главное окно") & vbCrLf &
               Localization.T("Alt+Left и Alt+Right: назад и вперёд по истории; Alt+Up: на уровень выше; F5: обновить; Alt+D: адресная строка (Enter - перейти, Esc - вернуть адрес)") & vbCrLf &
               Localization.T("Ctrl+A: выделить всё; Ctrl+Shift+A: снять выделение; Ctrl+I: обратить; Пробел: отметить; Ctrl+L: плитки или список; Esc: отменить чтение папки, затем закрыть панель") & vbCrLf &
               Localization.T("Ctrl+C/X/V: копировать, вырезать, вставить; F2: переименовать; Ctrl+Shift+N: новая папка; Del: удалить; Shift+Del: безвозвратно; Alt+Enter: свойства") & vbCrLf &
               Localization.T("Del: удалить выделенное; цифры 0-9: переместить; Shift + цифра верхнего ряда: скопировать") & vbCrLf &
               Localization.T("Ctrl+Z: отменить последнюю операцию; Ctrl+L: плитки или список") & vbCrLf &
               Localization.T("Создать ZIP: архив включает все выбранные папки и файлы; удаление оригиналов выключено по умолчанию и разрешается только после проверки и публикации архива") & vbCrLf &
               Localization.T("Распаковать здесь: выберите один архив; папки и обычные файлы сохраняются, исходный архив остаётся, извлечённые программы не запускаются; Esc отменяет операцию")
    End Function

End Class
#End If
