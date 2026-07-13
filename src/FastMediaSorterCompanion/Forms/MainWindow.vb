Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Companion main window - LEVEL 1 of the two-wizard model (§4.5): manage the
''' stable list of shared folders, see full server state (internet address FIRST,
''' then LAN, IPv6, host key, credentials, router - all copyable), Start/Stop, and
''' toggle logon autostart. The big "Поделиться.." action lives top-right and opens
''' the one-shot Package wizard (level 2). Port-forward instructions are behind the
''' "Инструкция по пробросу.." button; useful links are the row along the bottom.
''' </summary>
Public NotInheritable Class MainWindow
    Inherits Form

    ' --- state ------------------------------------------------------------------
    Private ReadOnly _initialFolder As String
    Private _busy As Boolean
    Private _loading As Boolean
    Private _entered As Boolean
    Private _listPopulated As Boolean
    Private _reachPollGen As Integer
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _suppressCheck As Boolean
    Private _router As RouterIdentity
    Private _routerRequested As Boolean
    Private _iconHandle As IntPtr
    Private ReadOnly _copyGlyph As Image = BuildCopyGlyph()
    Private ReadOnly _addGlyph As Image = BuildAddGlyph()
    Private progressBar As ProgressBar

    ' --- controls ---------------------------------------------------------------
    Private pnlContent As Panel
    Private lblIntro As Label
    Private lvFolders As ListView
    Private btnAdd As Button
    Private btnAddCurrent As Button
    Private btnRemove As Button
    Private btnParams As Button
    Private btnToggle As Button
    Private btnShare As Button
    Private btnGuide As Button
    Private chkAutostart As CheckBox
    Private lblState As Label
    Private lblInternet As Label
    Private lblLan As Label
    Private lblIpv6 As Label
    Private lblFinger As Label
    Private lblCreds As Label
    Private lblRouter As Label
    Private lblHint As Label
    Private lnkAndroid As LinkLabel
    Private lnkSiteGuide As LinkLabel
    Private lnkOpenRouter As LinkLabel
    Private lnkRouterSearch As LinkLabel
    Private lnkOpenViewer As LinkLabel
    Private toolTip As ToolTip

    ' Enable-gate overlay.
    Private pnlEnable As Panel
    Private btnEnable As Button

    ''' <summary>Raised whenever server state changes so the tray host can refresh its
    ''' icon/menu. Carries the running flag so the tray never re-fetches (deadlock-safe).</summary>
    Public Event ServerStateChanged(running As Boolean)

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
        Me.MinimumSize = New Size(780, 560)
        Me.ClientSize = New Size(820, 600)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        toolTip = New ToolTip()

        ' --- top strip: intro + big Share button (top-right) --------------------
        Dim pnlTop As New Panel With {.Dock = DockStyle.Top, .Height = 60, .Padding = New Padding(12, 10, 12, 6)}
        lblIntro = New Label With {.Dock = DockStyle.Fill, .Text = If(Rus,
            "Откройте папки этого компьютера на телефоне - фото для родных, музыку в дорогу.",
            "Open this PC's folders on your phone - photos for family, music on the go.")}
        btnShare = New Button With {.Dock = DockStyle.Right, .Width = 220, .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 1.5F, FontStyle.Bold),
            .Text = If(Rus, "Поделиться..", "Share..")}
        AddHandler btnShare.Click, AddressOf OnShareClicked
        pnlTop.Controls.Add(lblIntro)
        pnlTop.Controls.Add(btnShare)

        ' --- bottom strip: hint + expressive links -----------------------------
        Dim pnlBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 64, .Padding = New Padding(12, 4, 12, 8)}
        lblHint = New Label With {.Dock = DockStyle.Top, .Height = 20, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        Dim flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = True, .AutoScroll = False}
        lnkAndroid = MakeLink(If(Rus, "FastMediaSorter для Android", "FastMediaSorter for Android"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite(Rus)))
        lnkSiteGuide = MakeLink(If(Rus, "Как публиковать папки (сайт)", "How to publish folders (website)"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl))
        lnkOpenRouter = MakeLink(If(Rus, "Открыть роутер", "Open router"), Sub() OnOpenRouter(Me, EventArgs.Empty))
        lnkRouterSearch = MakeLink(If(Rus, "Инструкция для моей модели роутера", "Guide for my router model"), Sub() OnOpenRouterSearch(Me, EventArgs.Empty))
        lnkOpenViewer = MakeLink(If(Rus, "Открыть Fast Media Sorter", "Open Fast Media Sorter"), Sub() OnOpenViewerClicked(Me, EventArgs.Empty))
        flow.Controls.AddRange(New Control() {lnkAndroid, Sep(), lnkSiteGuide, Sep(), lnkOpenRouter, Sep(), lnkRouterSearch, Sep(), lnkOpenViewer})
        pnlBottom.Controls.Add(flow)
        pnlBottom.Controls.Add(lblHint)

        ' --- right column: full server status (all copyable) -------------------
        Dim grpServer As New GroupBox With {.Dock = DockStyle.Right, .Width = 320, .Padding = New Padding(10),
            .Text = If(Rus, "Доступ с телефона", "Phone access")}
        lblState = New Label With {.Left = 12, .Top = 22, .Width = 296, .Height = 22, .AutoEllipsis = True, .Font = New Font(Me.Font, FontStyle.Bold)}
        ' Internet FIRST (priority 0), then LAN (priority 1), IPv6, host key, creds.
        lblInternet = AddCopyRow(grpServer, 48, AddressOf InternetAddressValue)
        lblLan = AddCopyRow(grpServer, 72, AddressOf CurrentLanAddress)
        lblIpv6 = AddCopyRow(grpServer, 96, AddressOf Ipv6Value)
        lblFinger = AddCopyRow(grpServer, 120, AddressOf FingerprintValue)
        lblCreds = AddCopyRow(grpServer, 144, AddressOf CredentialsValue)
        lblRouter = New Label With {.Left = 12, .Top = 170, .Width = 296, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        btnGuide = New Button With {.Left = 12, .Top = 194, .Width = 296, .Height = 28, .Text = If(Rus, "Настроить доступ вне дома..", "Set up away access..")}
        AddHandler btnGuide.Click, AddressOf OnInternetAccess
        btnToggle = New Button With {.Left = 12, .Top = 230, .Width = 296, .Height = 34, .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(Rus, "Начать раздачу", "Start sharing")}
        AddHandler btnToggle.Click, AddressOf OnToggle
        chkAutostart = New CheckBox With {.Left = 12, .Top = 272, .Width = 296, .Height = 22,
            .Text = If(Rus, "Запускать при входе в Windows", "Start at Windows logon")}
        AddHandler chkAutostart.CheckedChanged, AddressOf OnAutostartChanged
        grpServer.Controls.AddRange(New Control() {lblState, lblRouter, btnGuide, btnToggle, chkAutostart})

        ' --- center: shared-folder list ----------------------------------------
        Dim grpShares As New GroupBox With {.Dock = DockStyle.Fill, .Padding = New Padding(10),
            .Text = If(Rus, "Общие папки", "Shared folders")}
        lvFolders = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .CheckBoxes = True,
            .FullRowSelect = True, .HideSelection = False, .MultiSelect = False}
        lvFolders.Columns.Add(If(Rus, "Название", "Name"), 150)
        lvFolders.Columns.Add(If(Rus, "Папка", "Folder"), 300)
        lvFolders.Columns.Add("RO", 44, HorizontalAlignment.Center)
        AddHandler lvFolders.MouseDown, AddressOf OnListMouseDown
        AddHandler lvFolders.ItemCheck, AddressOf OnItemCheck
        AddHandler lvFolders.ItemChecked, AddressOf OnItemChecked
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder

        Dim pnlListButtons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 40, .Padding = New Padding(0, 6, 0, 0)}
        btnAdd = New Button With {.Width = 176, .Height = 30, .Text = If(Rus, "Добавить папку..", "Add folder.."),
            .Image = _addGlyph, .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .Font = New Font(Me.Font, FontStyle.Bold)}
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

        ' --- content panel (so the gate overlay can hide the WHOLE UI) ---------
        pnlContent = New Panel With {.Dock = DockStyle.Fill}
        pnlContent.Controls.Add(grpShares)   ' Fill first, then the edge-docked siblings
        pnlContent.Controls.Add(grpServer)
        pnlContent.Controls.Add(pnlTop)
        pnlContent.Controls.Add(pnlBottom)

        BuildEnableOverlay()

        ' Thin marquee "loading bar" at the very top, shown during server operations.
        progressBar = New ProgressBar With {.Dock = DockStyle.Top, .Height = 6, .Style = ProgressBarStyle.Marquee,
            .MarqueeAnimationSpeed = 30, .Visible = False}

        Me.Controls.Add(pnlContent)
        Me.Controls.Add(pnlEnable)
        Me.Controls.Add(progressBar)
        pnlEnable.BringToFront()
        progressBar.BringToFront()

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
    End Sub

    Private Sub BuildEnableOverlay()
        pnlEnable = New Panel With {.Dock = DockStyle.Fill, .BackColor = SystemColors.Control, .Visible = False, .Padding = New Padding(24)}
        Dim lblEnableTitle As New Label With {.Dock = DockStyle.Top, .Height = 32, .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.2F, FontStyle.Bold),
            .Text = If(Rus, "Функции сервера выключены", "Server features are off")}
        Dim lblEnableIntro As New Label With {.Dock = DockStyle.Top, .Height = 120, .Text = If(Rus,
            "Общий доступ к папкам поднимает локальный SFTP-сервер и требует одного исключения в брандмауэре Windows (один раз, с правами администратора). Пока это не включено, программа ничего не раздаёт.",
            "Folder sharing runs a local SFTP server and needs one Windows Firewall exception (once, as administrator). Until enabled, nothing is shared.")}
        btnEnable = New Button With {.Top = 160, .Left = 24, .Width = 280, .Height = 34, .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Включить функции сервера..", "Enable server features..")}
        AddHandler btnEnable.Click, AddressOf OnEnableServer
        pnlEnable.Controls.Add(btnEnable)
        pnlEnable.Controls.Add(lblEnableIntro)
        pnlEnable.Controls.Add(lblEnableTitle)
    End Sub

    ' --- small UI builders ------------------------------------------------------

    Private Function MakeLink(text As String, onClick As Action) As LinkLabel
        Dim lnk As New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 4, 6, 0), .Text = text}
        AddHandler lnk.LinkClicked, Sub() onClick()
        Return lnk
    End Function

    Private Shared Function Sep() As Label
        Return New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 6, 0), .ForeColor = Color.Silver, .Text = "·"}
    End Function

    ''' <summary>Adds a "value + small copy button" row to <paramref name="parent"/> at
    ''' <paramref name="y"/>; returns the value Label (caller sets its text). The copy
    ''' button copies whatever <paramref name="provider"/> returns (raw value, no prefix).</summary>
    Private Function AddCopyRow(parent As Control, y As Integer, provider As Func(Of String)) As Label
        Dim lbl As New Label With {.Left = 12, .Top = y + 2, .Width = 268, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        Dim btn As New Button With {.Left = 284, .Top = y, .Width = 24, .Height = 22, .Image = _copyGlyph,
            .ImageAlign = ContentAlignment.MiddleCenter, .FlatStyle = FlatStyle.System, .TabStop = False, .Tag = provider}
        toolTip.SetToolTip(btn, If(Rus, "Копировать в буфер", "Copy to clipboard"))
        AddHandler btn.Click, AddressOf OnCopyClick
        parent.Controls.Add(lbl)
        parent.Controls.Add(btn)
        Return lbl
    End Function

    Private Sub OnCopyClick(sender As Object, e As EventArgs)
        Dim btn As Button = TryCast(sender, Button)
        If btn Is Nothing Then Return
        Dim provider As Func(Of String) = TryCast(btn.Tag, Func(Of String))
        If provider Is Nothing Then Return
        Dim value As String = provider()
        If String.IsNullOrEmpty(value) Then
            SetHint(If(Rus, "Нечего копировать.", "Nothing to copy."))
            Return
        End If
        Try
            Clipboard.SetText(value)
            SetHint(If(Rus, "Скопировано в буфер.", "Copied to clipboard."))
        Catch
        End Try
    End Sub

    ''' <summary>Small clipboard glyph for the copy buttons (two offset pages).</summary>
    Private Shared Function BuildCopyGlyph() As Bitmap
        Dim bmp As New Bitmap(16, 16)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            Using p As New Pen(Color.FromArgb(95, 95, 95), 1.3F)
                g.DrawRectangle(p, 3, 2, 7, 9)                       ' back page
                Using b As New SolidBrush(Color.White) : g.FillRectangle(b, 6, 5, 7, 9) : End Using
                g.DrawRectangle(p, 6, 5, 7, 9)                       ' front page
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>Green round "+" for the primary Add-folder button.</summary>
    Private Shared Function BuildAddGlyph() As Bitmap
        Dim bmp As New Bitmap(16, 16)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            Using b As New SolidBrush(Color.FromArgb(46, 160, 67))
                g.FillEllipse(b, 1, 1, 14, 14)
            End Using
            Using p As New Pen(Color.White, 2.2F)
                g.DrawLine(p, 8, 4, 8, 12)
                g.DrawLine(p, 4, 8, 12, 8)
            End Using
        End Using
        Return bmp
    End Function

    ' --- copyable value providers ----------------------------------------------

    Private Function InternetAddressValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim r As WorkerReachability = st.Reachability
        If r.IsCgnat OrElse String.IsNullOrEmpty(r.ExternalHost) Then Return ""
        Dim port As Integer = If(r.ExternalPort > 0, r.ExternalPort, st.ListenPort)
        Return r.ExternalHost & ":" & port.ToString()
    End Function

    Private Function CurrentLanAddress() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim lan As String = If(st.Reachability.LanAddress, "")
        If lan.Length = 0 OrElse st.ListenPort <= 0 Then Return ""
        Return lan & ":" & st.ListenPort.ToString()
    End Function

    Private Function Ipv6Value() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse st.Reachability Is Nothing OrElse String.IsNullOrEmpty(st.Reachability.Ipv6Address) Then Return ""
        Return st.Reachability.Ipv6Address & ":" & st.ListenPort.ToString()
    End Function

    Private Function FingerprintValue() As String
        Return If(_status IsNot Nothing, If(_status.Fingerprint, ""), "")
    End Function

    Private Function CredentialsValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse String.IsNullOrEmpty(st.Password) Then Return ""
        Dim user As String = If(String.IsNullOrEmpty(st.Username), "fms", st.Username)
        Return (If(Rus, "Логин: ", "Login: ") & user & Environment.NewLine) &
               (If(Rus, "Пароль: ", "Password: ") & st.Password)
    End Function

    ' --- lifecycle --------------------------------------------------------------

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _entered Then Return
        _entered = True
        Await EnterAsync()
        If _initialFolder.Length > 0 AndAlso ServerFeatures.IsEnabled() AndAlso WorkerProcess.IsAvailable() Then
            If Directory.Exists(_initialFolder) AndAlso AddShareRow(_initialFolder) Then Await ApplySharedFoldersAsync()
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        Try
            _settings.Save()
        Catch
        End Try
        ShareIcons.FreeIcon(Me.Icon, _iconHandle)
    End Sub

    Private Sub ApplyGate()
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        pnlContent.Visible = enabled
        pnlEnable.Visible = Not enabled
        If enabled Then pnlContent.BringToFront() Else pnlEnable.BringToFront()
    End Sub

    Private Async Function EnterAsync() As Task
        LoadLocalState()
        ApplyGate()
        If Not ServerFeatures.IsEnabled() Then Return

        If Not WorkerProcess.IsAvailable() Then
            SetHint(If(Rus, "Компонент общего доступа не найден - переустановите приложение.", "The sharing component is missing - reinstall the app."))
            SetServerControlsEnabled(False)
            Return
        End If

        SetBusy(True, If(Rus, "Запуск компаньона..", "Starting companion.."))
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
                toolTip.SetToolTip(chkAutostart, If(Rus, "Автозапуском управляет Windows (пакет из Store).", "Autostart is managed by Windows (Store package)."))
            End If
        Catch
        Finally
            _loading = prev
        End Try
    End Sub

    ' --- router (model shown in the server block) -------------------------------

    Private Async Function DetectRouterAsync() As Task
        If _routerRequested Then Return
        _routerRequested = True
        Try
            _router = Await Task.Run(Function() RouterInfo.Detect())
            If Me.IsDisposed Then Return
            Dim model As String = _router.DisplayName()
            lblRouter.Text = (If(Rus, "Роутер: ", "Router: ")) & If(model.Length > 0, model, If(Rus, "не определён", "unknown"))
        Catch
        End Try
    End Function

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub OnOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            SetHint(If(Rus, "Не удалось определить адрес роутера.", "Could not determine the router address."))
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnOpenRouterSearch(sender As Object, e As EventArgs)
        SetHint(If(Rus, "Определяем роутер..", "Detecting router.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        SetHint(If(rt.DisplayName().Length > 0, (If(Rus, "Роутер: ", "Router: ")) & rt.DisplayName(),
            If(Rus, "Модель не определена - открыт общий поиск.", "Model unknown - opened a general search.")))
    End Sub

    ' --- reachability polling ---------------------------------------------------

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
        Dim picked As String = Nothing
        Using dlg As New FolderBrowserDialog() With {.ShowNewFolderButton = False,
                .Description = If(Rus, "Выберите папку, которую хотите открыть на телефоне", "Choose the folder to open on the phone")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            picked = dlg.SelectedPath
        End Using
        Await AddFolderInteractive(picked)
    End Sub

    Private Async Sub OnAddCurrentFolder(sender As Object, e As EventArgs)
        If _busy OrElse _initialFolder.Length = 0 Then Return
        Try
            If Directory.Exists(_initialFolder) Then Await AddFolderInteractive(_initialFolder)
        Catch
        End Try
    End Sub

    ''' <summary>Adds a folder, then immediately opens its resource-options dialog
    ''' (name / type / read-only / destination..) so the user configures the new share
    ''' right after picking it, then re-shares.</summary>
    Private Async Function AddFolderInteractive(path As String) As Task
        If String.IsNullOrWhiteSpace(path) Then Return
        If Not AddShareRow(path) Then
            SetHint(If(Rus, "Эта папка уже в списке.", "That folder is already in the list."))
            Return
        End If
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(path)
        Using dlg As New Share_Root_Params_Form(ShareFolderDisplayName(path), before)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ShareRootParamsStore.SetFor(path, dlg.Result)
                For Each it As ListViewItem In lvFolders.Items
                    If String.Equals(Convert.ToString(it.Tag), path, StringComparison.OrdinalIgnoreCase) Then
                        Dim lbl As String = If(dlg.Result.Label, "").Trim()
                        If lbl.Length > 0 Then it.Text = lbl
                        it.SubItems(2).Text = RoLabel(dlg.Result)
                        Exit For
                    End If
                Next
            End If
        End Using
        Await ApplySharedFoldersAsync()
    End Function

    Private Async Sub OnRemoveFolder(sender As Object, e As EventArgs)
        If _busy OrElse lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim host As String = Convert.ToString(it.Tag)
        If Not String.IsNullOrEmpty(host) Then ShareRootParamsStore.RemoveFor(host)
        lvFolders.Items.Remove(it)
        RestripeList()
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
            it.SubItems(2).Text = RoLabel(after)
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
            it.SubItems.Add(RoLabel(ShareRootParamsStore.GetFor(path)))
            lvFolders.Items.Add(it)
        Finally
            _loading = prev
        End Try
        RestripeList()
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
                it.SubItems.Add(RoLabel(ShareRootParamsStore.GetFor(host)))
                lvFolders.Items.Add(it)
            Next
            RestripeList()
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
        SetBusy(True, If(Rus, "Обновляю список папок..", "Updating the folder list.."))
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
        SetBusy(True, If(Rus, "Минутку..", "One moment.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            SetHint(If(Rus, "Останавливаю раздачу..", "Stopping sharing.."))
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
            SetHint(If(Rus, "Сначала добавьте папку и отметьте её галочкой.", "Add a folder and tick it first."))
            Return
        End If
        SetHint(If(Rus, "Включаю раздачу..", "Starting sharing.."))
        Dim res As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        _status = res.Status
        ApplyStatusToUi()
        SetBusy(False)
        SetHint(If(res.Served, If(Rus, "Раздача запущена.", "Sharing started."),
                              If(Rus, "Запущено, адрес не подтверждён - проверьте брандмауэр/сеть.", "Started, address unconfirmed - check firewall/network.")))
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Sub

    ' --- status -> UI -----------------------------------------------------------

    Private Sub ApplyStatusToUi()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)

        btnToggle.Text = If(running, If(Rus, "Остановить раздачу", "Stop sharing"), If(Rus, "Начать раздачу", "Start sharing"))
        lblState.Text = If(running, If(Rus, "Папки видны на телефоне", "Folders are visible on the phone"), If(Rus, "Раздача выключена", "Sharing is off"))
        lblState.ForeColor = If(running, Color.ForestGreen, Color.DimGray)

        ' "Away (internet)" FIRST (priority 0), then "Home (Wi-Fi)". Friendly labels;
        ' the raw address is still copyable via the button.
        lblInternet.Text = (If(Rus, "Вне дома (интернет): ", "Away (internet): ")) & InternetStatusText(running, reach)
        lblLan.Text = (If(Rus, "Дома (Wi-Fi): ", "Home (Wi-Fi): ")) & If(CurrentLanAddress().Length > 0, CurrentLanAddress(), "-")
        lblIpv6.Text = If(Ipv6Value().Length > 0, ((If(Rus, "IPv6: ", "IPv6: ")) & Ipv6Value()), "")
        lblFinger.Text = If(FingerprintValue().Length > 0, ((If(Rus, "Ключ узла: ", "Host key: ")) & FingerprintValue()), "")
        lblCreds.Text = CredsDisplay()

        btnShare.Enabled = running AndAlso Not _busy
        btnGuide.Enabled = running AndAlso Not _busy
        lnkOpenRouter.Enabled = running
        lnkRouterSearch.Enabled = running

        If running AndAlso Not _routerRequested Then Dim t As Task = DetectRouterAsync()

        RaiseEvent ServerStateChanged(running)
    End Sub

    Private Function InternetStatusText(running As Boolean, reach As WorkerReachability) As String
        If Not running Then Return "-"
        If reach Is Nothing Then Return If(Rus, "определяется..", "detecting..")
        If reach.IsCgnat Then Return If(Rus, "за CGNAT (недоступно)", "behind CGNAT (unreachable)")
        Dim addr As String = InternetAddressValue()
        If addr.Length = 0 Then Return If(Rus, "адрес неизвестен", "address unknown")
        Return addr
    End Function

    Private Function CredsDisplay() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse String.IsNullOrEmpty(st.Password) Then Return ""
        Dim user As String = If(String.IsNullOrEmpty(st.Username), "fms", st.Username)
        Return (If(Rus, "Логин: ", "Login: ")) & user & "   " & (If(Rus, "Пароль: ", "Password: ")) & st.Password
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

    Private Sub OnInternetAccess(sender As Object, e As EventArgs)
        Using dlg As New InternetAccessForm(_status)
            dlg.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnOpenViewerClicked(sender As Object, e As EventArgs)
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

    ' --- enable gate + autostart ------------------------------------------------

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

    ''' <summary>RO-column cell: "✓" = hard read-only (server blocks writes); "~" =
    ''' soft read-only (phone shown read-only, server still writable); blank otherwise.</summary>
    Private Shared Function RoLabel(p As ShareRootParams) As String
        If p Is Nothing Then Return ""
        If Not p.IsWritable() Then Return "✓"
        If p.SoftReadOnly Then Return "~"
        Return ""
    End Function

    Private Shared Function ShareFolderDisplayName(path As String) As String
        Try
            Dim n As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(n) Then Return n
        Catch
        End Try
        Return path
    End Function

    ''' <summary>Zebra-stripes the folder list (alternating row background) so it reads
    ''' as a clean table.</summary>
    Private Sub RestripeList()
        Dim odd As Color = Color.FromArgb(244, 247, 252)
        For i As Integer = 0 To lvFolders.Items.Count - 1
            lvFolders.Items(i).BackColor = If((i And 1) = 0, SystemColors.Window, odd)
        Next
    End Sub

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub SetServerControlsEnabled(enabled As Boolean)
        For Each c As Control In New Control() {btnToggle, btnAdd, btnAddCurrent, btnRemove, btnParams, lvFolders, btnShare, btnGuide}
            If c IsNot Nothing Then c.Enabled = enabled
        Next
    End Sub

    Private Sub SetBusy(value As Boolean, Optional message As String = Nothing)
        _busy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        btnToggle.Enabled = Not value AndAlso avail
        btnAdd.Enabled = Not value AndAlso avail
        btnAddCurrent.Enabled = Not value AndAlso avail
        btnRemove.Enabled = Not value AndAlso avail
        btnParams.Enabled = Not value AndAlso avail
        lvFolders.Enabled = Not value AndAlso avail
        btnShare.Enabled = Not value AndAlso avail AndAlso running
        btnGuide.Enabled = Not value AndAlso avail AndAlso running
        ' Show the marquee "loading bar" + the operation message while working, so the
        ' UI clearly reads as "doing something" instead of frozen.
        If progressBar IsNot Nothing Then progressBar.Visible = value
        If value AndAlso Not String.IsNullOrEmpty(message) Then SetHint(message)
        Me.UseWaitCursor = value
    End Sub

End Class
