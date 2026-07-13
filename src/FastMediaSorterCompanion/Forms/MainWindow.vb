Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Companion main window - LEVEL 1 of the two-wizard model (§4.5): manage the
''' stable list of shared folders ("shares"), see server state, Start/Stop the SFTP
''' server, toggle logon autostart. It does NOT build QR/.fmscfg - that is the
''' one-shot Package wizard (level 2), opened by the "Поделиться.." button. Rebuilt
''' from the LITE Share tab logic (Table_Form.Share.vb) minus the QR-export column,
''' with the LITE-viewer couplings dropped (no Main_Form.Current_Folder_Path /
''' RefreshShareTray; the tray refresh is raised as an event the tray host handles).
''' </summary>
Public NotInheritable Class MainWindow
    Inherits Form

    ' --- state (mirrors the LITE tab's guard/status fields) ----------------------
    Private ReadOnly _initialFolder As String
    Private _busy As Boolean
    Private _loading As Boolean
    Private _entered As Boolean
    Private _listPopulated As Boolean
    Private _reachPollGen As Integer
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _suppressCheck As Boolean

    ' --- controls ---------------------------------------------------------------
    Private lblIntro As Label
    Private lvFolders As ListView
    Private btnAdd As Button
    Private btnAddCurrent As Button
    Private btnRemove As Button
    Private btnParams As Button
    Private btnToggle As Button
    Private lblState As Label
    Private lblAddr As Label
    Private lblFinger As Label
    Private chkAutostart As CheckBox
    Private btnShare As Button
    Private btnOpenViewer As Button
    Private lblHint As Label
    Private toolTip As ToolTip

    ' Enable-gate overlay (shown when server features are not consented).
    Private pnlEnable As Panel
    Private lblEnableTitle As Label
    Private lblEnableIntro As Label
    Private btnEnable As Button

    ''' <summary>Raised whenever server state changes so the tray host can refresh its
    ''' icon/menu (Companion analogue of the LITE Main_Form.RefreshShareTray call).</summary>
    Public Event ServerStateChanged()

    Public Sub New(Optional initialFolder As String = Nothing)
        _initialFolder = If(initialFolder, "")
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    Private Shared ReadOnly Property Rus As Boolean
        Get
            Return Is_Russian_Language
        End Get
    End Property

    ' --- UI construction --------------------------------------------------------

    Private Sub BuildUi()
        Me.Text = "Fast Media Sorter: Share Manager"
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(720, 520)
        Me.ClientSize = New Size(760, 560)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.AutoScaleMode = AutoScaleMode.Font
        toolTip = New ToolTip()

        lblIntro = New Label With {
            .Dock = DockStyle.Top, .Height = 40, .Padding = New Padding(12, 10, 12, 0),
            .Text = If(Rus,
                "Управление общим доступом к папкам для Android-клиента по SFTP.",
                "Manage folder sharing to the Android client over SFTP.")}

        ' --- bottom bar ---------------------------------------------------------
        Dim pnlBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 88, .Padding = New Padding(12, 6, 12, 8)}
        lblHint = New Label With {.Dock = DockStyle.Top, .Height = 34, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        Dim pnlButtons As New Panel With {.Dock = DockStyle.Fill}
        chkAutostart = New CheckBox With {
            .AutoSize = True, .Left = 0, .Top = 8,
            .Text = If(Rus, "Запускать при входе в Windows", "Start at Windows logon")}
        AddHandler chkAutostart.CheckedChanged, AddressOf OnAutostartChanged
        btnShare = New Button With {
            .Width = 190, .Height = 32, .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
            .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Поделиться..", "Share..")}
        btnOpenViewer = New Button With {
            .Width = 210, .Height = 32, .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
            .Text = If(Rus, "Открыть Fast Media Sorter", "Open Fast Media Sorter")}
        AddHandler btnShare.Click, AddressOf OnShareClicked
        AddHandler btnOpenViewer.Click, AddressOf OnOpenViewerClicked
        pnlButtons.Controls.AddRange(New Control() {chkAutostart, btnShare, btnOpenViewer})
        AddHandler pnlButtons.Resize, Sub()
                                          btnShare.Left = pnlButtons.ClientSize.Width - btnShare.Width
                                          btnShare.Top = 8
                                          btnOpenViewer.Left = btnShare.Left - 8 - btnOpenViewer.Width
                                          btnOpenViewer.Top = 8
                                      End Sub
        pnlBottom.Controls.Add(pnlButtons)
        pnlBottom.Controls.Add(lblHint)

        ' --- right column: server status ---------------------------------------
        Dim grpServer As New GroupBox With {
            .Dock = DockStyle.Right, .Width = 280, .Padding = New Padding(10),
            .Text = If(Rus, "Состояние сервера", "Server state")}
        lblState = New Label With {.Left = 14, .Top = 28, .Width = 250, .Height = 22, .AutoEllipsis = True}
        lblAddr = New Label With {.Left = 14, .Top = 54, .Width = 250, .Height = 22, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        lblFinger = New Label With {.Left = 14, .Top = 78, .Width = 250, .Height = 34, .AutoEllipsis = True, .ForeColor = Color.DimGray, .Font = New Font(Me.Font.FontFamily, Me.Font.Size - 0.5F)}
        btnToggle = New Button With {.Left = 14, .Top = 120, .Width = 250, .Height = 34, .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(Rus, "Запустить", "Start")}
        AddHandler btnToggle.Click, AddressOf OnToggle
        grpServer.Controls.AddRange(New Control() {lblState, lblAddr, lblFinger, btnToggle})

        ' --- center: shared-folder list ----------------------------------------
        Dim grpShares As New GroupBox With {
            .Dock = DockStyle.Fill, .Padding = New Padding(10),
            .Text = If(Rus, "Общие папки", "Shared folders")}
        lvFolders = New ListView With {
            .Dock = DockStyle.Fill, .View = View.Details, .CheckBoxes = True,
            .FullRowSelect = True, .HideSelection = False, .MultiSelect = False}
        lvFolders.Columns.Add(If(Rus, "Название", "Name"), 150)
        lvFolders.Columns.Add(If(Rus, "Папка", "Folder"), 300)
        lvFolders.Columns.Add(If(Rus, "Доступ", "Access"), 90)
        AddHandler lvFolders.MouseDown, AddressOf OnListMouseDown
        AddHandler lvFolders.ItemCheck, AddressOf OnItemCheck
        AddHandler lvFolders.ItemChecked, AddressOf OnItemChecked
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder

        Dim pnlListButtons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 40, .Padding = New Padding(0, 6, 0, 0)}
        btnAdd = New Button With {.Width = 130, .Height = 28, .Text = If(Rus, "Добавить папку..", "Add folder..")}
        btnAddCurrent = New Button With {.Width = 150, .Height = 28, .Text = If(Rus, "+ Текущая папка", "+ Current folder"), .Visible = _initialFolder.Length > 0}
        btnRemove = New Button With {.Width = 100, .Height = 28, .Text = If(Rus, "Убрать", "Remove")}
        btnParams = New Button With {.Width = 120, .Height = 28, .Text = If(Rus, "Настроить..", "Options..")}
        AddHandler btnAdd.Click, AddressOf OnAddFolder
        AddHandler btnAddCurrent.Click, AddressOf OnAddCurrentFolder
        AddHandler btnRemove.Click, AddressOf OnRemoveFolder
        AddHandler btnParams.Click, AddressOf OnConfigureFolder
        pnlListButtons.Controls.AddRange(New Control() {btnAdd, btnAddCurrent, btnRemove, btnParams})
        grpShares.Controls.Add(lvFolders)
        grpShares.Controls.Add(pnlListButtons)

        ' --- enable-gate overlay (added last, covers all) ----------------------
        pnlEnable = New Panel With {.Dock = DockStyle.Fill, .BackColor = SystemColors.Control, .Visible = False, .Padding = New Padding(24)}
        lblEnableTitle = New Label With {.Dock = DockStyle.Top, .Height = 32, .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.2F, FontStyle.Bold),
            .Text = If(Rus, "Функции сервера выключены", "Server features are off")}
        lblEnableIntro = New Label With {.Dock = DockStyle.Top, .Height = 120,
            .Text = If(Rus,
                "Общий доступ к папкам поднимает локальный SFTP-сервер и требует одного исключения в брандмауэре Windows (один раз, с правами администратора). Пока это не включено, программа ничего не раздаёт.",
                "Folder sharing runs a local SFTP server and needs one Windows Firewall exception (once, as administrator). Until enabled, nothing is shared.")}
        btnEnable = New Button With {.Top = 160, .Left = 24, .Width = 260, .Height = 34, .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Включить функции сервера..", "Enable server features..")}
        AddHandler btnEnable.Click, AddressOf OnEnableServer
        pnlEnable.Controls.Add(btnEnable)
        pnlEnable.Controls.Add(lblEnableIntro)
        pnlEnable.Controls.Add(lblEnableTitle)

        Me.Controls.Add(grpShares)
        Me.Controls.Add(grpServer)
        Me.Controls.Add(lblIntro)
        Me.Controls.Add(pnlBottom)
        Me.Controls.Add(pnlEnable)
        pnlEnable.BringToFront()

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
    End Sub

    ' --- lifecycle --------------------------------------------------------------

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _entered Then Return
        _entered = True
        Await EnterAsync()
        ' A launch with a folder argument is the "share this folder" gesture: ensure
        ' it is a share, then jump straight to the package wizard (§4.5.1).
        If _initialFolder.Length > 0 AndAlso ServerFeatures.IsEnabled() AndAlso WorkerProcess.IsAvailable() Then
            If Directory.Exists(_initialFolder) AndAlso AddShareRow(_initialFolder) Then
                Await ApplySharedFoldersAsync()
            End If
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        Try
            _settings.Save()
        Catch
        End Try
    End Sub

    Private Sub ApplyGate()
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        pnlEnable.Visible = Not enabled
        If Not enabled Then pnlEnable.BringToFront()
    End Sub

    Private Async Function EnterAsync() As Task
        LoadLocalState()
        ApplyGate()
        If Not ServerFeatures.IsEnabled() Then Return

        If Not WorkerProcess.IsAvailable() Then
            SetHint(If(Rus, "Компонент общего доступа не найден - переустановите приложение.",
                            "The sharing component is missing - reinstall the app."))
            SetServerControlsEnabled(False)
            Return
        End If

        SetBusy(True)
        SetHint(If(Rus, "Запуск компаньона..", "Starting companion.."))
        Dim st As WorkerStatus = Await ShareController.EnsureRunningReconciledAsync()
        If st Is Nothing Then
            SetHint(If(Rus, "Не удалось связаться с компаньоном.", "Could not reach the companion worker."))
            SetBusy(False)
            Return
        End If

        _status = st
        If Not _listPopulated Then
            PopulateFolders(st.Roots)
            _listPopulated = True
        End If
        ApplyStatusToUi()
        SetBusy(False)
        SetHint("")

        If st.Running AndAlso st.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Function

    Private Sub LoadLocalState()
        Dim prev As Boolean = _loading
        _loading = True
        Try
            _settings.Load()
            chkAutostart.Checked = AutostartManager.IsEnabled()
            chkAutostart.Enabled = Not AutostartManager.IsPackaged()
            If AutostartManager.IsPackaged() Then
                toolTip.SetToolTip(chkAutostart, If(Rus, "Автозапуском управляет Windows (пакет из Store).",
                                                         "Autostart is managed by Windows (Store package)."))
            End If
        Catch
        Finally
            _loading = prev
        End Try
    End Sub

    ' --- reachability polling (generation-guarded, from the LITE tab) -----------

    Private Sub CancelReachabilityPoll()
        _reachPollGen += 1
    End Sub

    Private Async Function PollReachabilityAsync() As Task
        _reachPollGen += 1
        Dim myGen As Integer = _reachPollGen
        For i As Integer = 1 To 20
            Await Task.Delay(1000)
            If myGen <> _reachPollGen OrElse Me.IsDisposed Then Return
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            If st Is Nothing Then Continue For
            _status = st
            ApplyStatusToUi()
            If Not st.Running OrElse st.Reachability IsNot Nothing Then Return
        Next
    End Function

    ' --- folder-list handlers ---------------------------------------------------

    Private Async Sub OnAddFolder(sender As Object, e As EventArgs)
        If _busy Then Return
        Using dlg As New FolderBrowserDialog() With {.ShowNewFolderButton = False}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If AddShareRow(dlg.SelectedPath) Then Await ApplySharedFoldersAsync()
        End Using
    End Sub

    Private Async Sub OnAddCurrentFolder(sender As Object, e As EventArgs)
        If _busy OrElse _initialFolder.Length = 0 Then Return
        Try
            If Directory.Exists(_initialFolder) AndAlso AddShareRow(_initialFolder) Then Await ApplySharedFoldersAsync()
        Catch
        End Try
    End Sub

    Private Async Sub OnRemoveFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim host As String = Convert.ToString(it.Tag)
        If Not String.IsNullOrEmpty(host) Then ShareRootParamsStore.RemoveFor(host)
        lvFolders.Items.Remove(it)
        Await ApplySharedFoldersAsync()
    End Sub

    Private Async Sub OnConfigureFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim host As String = Convert.ToString(it.Tag)
        If String.IsNullOrEmpty(host) Then Return
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(host)
        Using dlg As New Share_Root_Params_Form(it.Text, before)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim after As ShareRootParams = dlg.Result
            ShareRootParamsStore.SetFor(host, after)
            it.SubItems(2).Text = AccessLabel(after)
            ' Re-share only when effective writability flipped on a live, checked row.
            If before.IsWritable() <> after.IsWritable() AndAlso it.Checked AndAlso _status IsNot Nothing AndAlso _status.Running Then
                Await ApplySharedFoldersAsync()
            End If
        End Using
    End Sub

    Private Sub OnListMouseDown(sender As Object, e As MouseEventArgs)
        Try
            Dim hit As ListViewHitTestInfo = lvFolders.HitTest(e.Location)
            _suppressCheck = (e.Clicks = 2 AndAlso hit IsNot Nothing AndAlso hit.Location <> ListViewHitTestLocations.StateImage)
        Catch
        End Try
    End Sub

    Private Sub OnItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _suppressCheck Then
            e.NewValue = e.CurrentValue
            _suppressCheck = False
        End If
    End Sub

    Private Async Sub OnItemChecked(sender As Object, e As ItemCheckedEventArgs)
        If _loading OrElse _busy Then Return
        Await ApplySharedFoldersAsync()
    End Sub

    Private Function AddShareRow(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        For Each existing As ListViewItem In lvFolders.Items
            If String.Equals(Convert.ToString(existing.Tag), path, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Dim prev As Boolean = _loading
        _loading = True
        Try
            Dim it As New ListViewItem(ShareFolderDisplayName(path)) With {.Checked = True, .Tag = path}
            it.SubItems.Add(path)
            it.SubItems.Add(AccessLabel(ShareRootParamsStore.GetFor(path)))
            lvFolders.Items.Add(it)
        Finally
            _loading = prev
        End Try
        Return True
    End Function

    Private Sub PopulateFolders(roots As List(Of ShareFolder))
        If roots Is Nothing Then Return
        Dim prev As Boolean = _loading
        _loading = True
        lvFolders.BeginUpdate()
        Try
            lvFolders.Items.Clear()
            For Each r As ShareFolder In roots
                Dim host As String = If(r.hostPath, "")
                If host.Length = 0 Then Continue For
                Dim it As New ListViewItem(If(String.IsNullOrEmpty(r.name), ShareFolderDisplayName(host), r.name)) With {.Checked = True, .Tag = host}
                it.SubItems.Add(host)
                it.SubItems.Add(AccessLabel(ShareRootParamsStore.GetFor(host)))
                lvFolders.Items.Add(it)
            Next
        Finally
            lvFolders.EndUpdate()
            _loading = prev
        End Try
    End Sub

    Private Function CurrentShareFolders() As List(Of ShareFolder)
        Dim list As New List(Of ShareFolder)()
        For Each it As ListViewItem In lvFolders.Items
            If Not it.Checked Then Continue For
            Dim host As String = Convert.ToString(it.Tag)
            If String.IsNullOrEmpty(host) Then Continue For
            Dim writable As Boolean = ShareRootParamsStore.GetFor(host).IsWritable()
            list.Add(New ShareFolder With {.name = it.Text, .hostPath = host, .readOnly = Not writable})
        Next
        Return list
    End Function

    ' --- server ops -------------------------------------------------------------

    Private Async Function ApplySharedFoldersAsync() As Task
        CancelReachabilityPoll()
        SetBusy(True)
        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
        Else
            Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
            _status = r.Status
        End If
        ApplyStatusToUi()
        SetBusy(False)
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Function

    Private Async Sub OnToggle(sender As Object, e As EventArgs)
        If _busy Then Return
        CancelReachabilityPoll()
        SetBusy(True)
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
            ApplyStatusToUi()
            SetBusy(False)
            SetHint(If(Rus, "Раздача остановлена.", "Sharing stopped."))
            Return
        End If

        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            SetBusy(False)
            SetHint(If(Rus, "Отметьте хотя бы одну папку.", "Check at least one folder."))
            Return
        End If
        Dim res As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        _status = res.Status
        ApplyStatusToUi()
        SetBusy(False)
        If res.Served Then
            SetHint(If(Rus, "Раздача запущена.", "Sharing started."))
        Else
            SetHint(If(Rus, "Запущено, но адрес не подтверждён - проверьте брандмауэр/сеть.",
                            "Started, but the address is unconfirmed - check the firewall/network."))
        End If
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Sub

    ' --- status -> UI (no QR; that lives in the package wizard) ------------------

    Private Sub ApplyStatusToUi()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        btnToggle.Text = If(running, If(Rus, "Остановить", "Stop"), If(Rus, "Запустить", "Start"))
        lblState.Text = If(running, If(Rus, "Сервер запущен", "Server running"), If(Rus, "Сервер остановлен", "Server stopped"))
        lblState.ForeColor = If(running, Color.ForestGreen, Color.DimGray)
        lblAddr.Text = If(Rus, "Адрес: ", "Address: ") & If(CurrentLanAddress().Length > 0, CurrentLanAddress(), "-")
        lblFinger.Text = If(_status IsNot Nothing AndAlso Not String.IsNullOrEmpty(_status.Fingerprint),
                            (If(Rus, "Ключ узла: ", "Host key: ") & _status.Fingerprint), "")
        btnShare.Enabled = running AndAlso Not _busy
        RaiseEvent ServerStateChanged()
    End Sub

    Private Function CurrentLanAddress() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim lan As String = If(st.Reachability.LanAddress, "")
        If lan.Length = 0 OrElse st.ListenPort <= 0 Then Return ""
        Return lan & ":" & st.ListenPort.ToString()
    End Function

    ' --- package wizard + viewer launch ----------------------------------------

    Private Sub OnShareClicked(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse Not _status.Running Then
            SetHint(If(Rus, "Сначала запустите сервер.", "Start the server first."))
            Return
        End If
        Dim preselect As New List(Of String)()
        For Each it As ListViewItem In lvFolders.Items
            If it.Checked Then preselect.Add(Convert.ToString(it.Tag))
        Next
        Using dlg As New PackageWizardForm(preselect)
            dlg.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnOpenViewerClicked(sender As Object, e As EventArgs)
        ' Companion -> LITE direction of the mutual launch (refined into the full
        ' mutex/WM_COPYDATA handshake in Ф4/stage 4). Best-effort.
        Try
            Dim dir As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim exe As String = Path.Combine(dir, "FastMediaSorter_LITE.exe")
            If File.Exists(exe) Then
                Process.Start(New ProcessStartInfo(exe) With {.UseShellExecute = True})
            Else
                SetHint(If(Rus, "Fast Media Sorter не найден рядом.", "Fast Media Sorter not found alongside."))
            End If
        Catch
        End Try
    End Sub

    ' --- enable gate ------------------------------------------------------------

    Private Sub OnEnableServer(sender As Object, e As EventArgs)
        Using dlg As New Share_Enable_Form(Rus)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ApplyGate()
                _entered = False
                _listPopulated = False
                Dim t As Task = EnterAsync()
            End If
        End Using
    End Sub

    Private Sub OnAutostartChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        If AutostartManager.IsPackaged() Then Return
        Try
            AutostartManager.SetEnabled(chkAutostart.Checked)
            _settings.AutostartEnabled = AutostartManager.IsEnabled()
            _settings.Save()
        Catch
            Dim prev As Boolean = _loading
            _loading = True
            chkAutostart.Checked = AutostartManager.IsEnabled()
            _loading = prev
        End Try
    End Sub

    ' --- small helpers ----------------------------------------------------------

    Private Shared Function AccessLabel(p As ShareRootParams) As String
        If p Is Nothing Then Return If(Rus, "чтение", "read")
        If p.IsDestination Then Return If(Rus, "получатель", "target")
        Return If(p.IsWritable(), If(Rus, "запись", "write"), If(Rus, "чтение", "read"))
    End Function

    Private Shared Function ShareFolderDisplayName(path As String) As String
        Try
            Dim n As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(n) Then Return n
        Catch
        End Try
        Return path
    End Function

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub SetServerControlsEnabled(enabled As Boolean)
        For Each c As Control In New Control() {btnToggle, btnAdd, btnAddCurrent, btnRemove, btnParams, lvFolders, btnShare}
            If c IsNot Nothing Then c.Enabled = enabled
        Next
    End Sub

    Private Sub SetBusy(value As Boolean)
        _busy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        btnToggle.Enabled = Not value AndAlso avail
        btnAdd.Enabled = Not value AndAlso avail
        btnAddCurrent.Enabled = Not value AndAlso avail
        btnRemove.Enabled = Not value AndAlso avail
        btnParams.Enabled = Not value AndAlso avail
        lvFolders.Enabled = Not value AndAlso avail
        btnShare.Enabled = Not value AndAlso avail AndAlso _status IsNot Nothing AndAlso _status.Running
        Me.UseWaitCursor = value
    End Sub

End Class
