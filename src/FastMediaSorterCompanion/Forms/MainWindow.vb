Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Companion main window - LEVEL 1 of the two-wizard model (§4.5): manage the shared
''' folders and see phone-access status. The server panel is a borderless 3-column
''' grid (caption | value | copy button) built with a TableLayoutPanel so it scales
''' cleanly at any display scaling (125/150/175%) - no absolute pixel coordinates. The
''' big "Поделиться" action (top-right) opens the one-shot Package wizard.
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
    Private _testing As Boolean
    Private _openShareWhenReady As Boolean
    Private _wizardOpen As Boolean
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _suppressCheck As Boolean
    Private _router As RouterIdentity
    Private _routerRequested As Boolean
    Private _iconHandle As IntPtr
    ' Code-drawn button glyphs; built in BuildUi at the display's DPI (see _shareGlyph there).
    Private _copyGlyph As Image
    Private _addGlyph As Image
    Private _shareGlyph As Image

    ' --- controls ---------------------------------------------------------------
    Private pnlContent As Panel
    Private progressBar As ProgressBar
    Private lvFolders As ListView
    Private btnAdd As Button
    Private btnAddCurrent As Button
    Private btnRemove As Button
    Private btnParams As Button
    Private btnToggle As Button
    Private btnShare As Button
    Private btnGuide As Button
    Private btnTest As Button
    Private chkAutostart As CheckBox
    Private chkOpenOnStart As CheckBox
    Private numMaxConns As NumericUpDown
    Private lblState As Label
    Private lblAccessState As Label
    Private lblAccessNext As Label
    Private lnkRouter As LinkLabel
    Private lblHint As Label
    Private pnlStats As TableLayoutPanel
    Private lblStatLast As Label
    Private lblStatConns As Label
    Private lblStatFiles As Label
    Private _statsTimer As Timer   ' periodic status refresh so the usage block stays live while the window is open
    Private lblHosting As Label
    Private btnHosting As Button
    Private lnkAndroid As LinkLabel
    Private lnkSiteGuide As LinkLabel
    Private lnkRouterSearch As LinkLabel
    Private lnkOpenViewer As LinkLabel
    Private lnkLanguage As LinkLabel
    Private toolTip As ToolTip
    Private ReadOnly _serverRows As New List(Of ServerRow)()

    Private pnlEnable As Panel
    Private btnEnable As Button

    Public Event ServerStateChanged(running As Boolean)

    Public Sub New(Optional initialFolder As String = Nothing)
        _initialFolder = If(initialFolder, "")
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub


    ''' <summary>One caption|value|copy row of the server grid + how to fill it.</summary>
    Private NotInheritable Class ServerRow
        Public ReadOnly Cap As Label
        Public ReadOnly Value As Label
        Public ReadOnly Copy As Button
        Public ReadOnly ValueFunc As Func(Of String)
        Public ReadOnly CopyFunc As Func(Of String)
        Public ReadOnly AlwaysShow As Boolean
        Public Sub New(cap As Label, value As Label, copy As Button, valueFunc As Func(Of String), copyFunc As Func(Of String), alwaysShow As Boolean)
            Me.Cap = cap : Me.Value = value : Me.Copy = copy
            Me.ValueFunc = valueFunc : Me.CopyFunc = copyFunc : Me.AlwaysShow = alwaysShow
        End Sub
    End Class

    ' --- UI construction --------------------------------------------------------

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = "Fast Media Sorter: Share Manager"
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(980, 700)
        Me.ClientSize = New Size(1320, 880)
        ' NB: the AutoScaleMode/AutoScaleDimensions pair is set at the END of this method
        ' (DpiLayout.ApplyAutoScale) - it only scales the children that already exist.
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        ' The glyphs are code-drawn bitmaps: auto-scaling grows the buttons around them but
        ' never the images, so they are drawn at the display's DPI right away.
        BuildGlyphs()
        toolTip = New ToolTip()
        _statsTimer = New Timer With {.Interval = 10000}
        AddHandler _statsTimer.Tick, AddressOf OnStatsTick

        ' --- top strip: neutral one-liner + big Share button --------------------
        Dim pnlTop As New Panel With {.Dock = DockStyle.Top, .Height = 66, .Padding = New Padding(16, 14, 16, 8)}
        Dim lblIntro As New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .TextAlign = ContentAlignment.MiddleLeft, .Text = Localization.T("Откройте папки этого ПК на телефоне - по Wi-Fi или через интернет.")}
        btnShare = New Button With {.Dock = DockStyle.Right, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(18, 6, 18, 6),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 2.0F, FontStyle.Bold),
            .Text = Localization.T("Поделиться"), .Image = _shareGlyph,
            .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter}
        AddHandler btnShare.Click, AddressOf OnShareClicked
        pnlTop.Controls.Add(lblIntro)
        pnlTop.Controls.Add(btnShare)

        ' --- bottom strip: hint + useful links ---------------------------------
        Dim pnlBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 66, .Padding = New Padding(16, 4, 16, 10)}
        lblHint = New Label With {.Dock = DockStyle.Top, .Height = 22, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        Dim flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = True}
        lnkAndroid = MakeLink(Localization.T("FastMediaSorter для Android"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite()))
        lnkSiteGuide = MakeLink(Localization.T("Как публиковать папки (сайт)"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl))
        lnkRouterSearch = MakeLink(Localization.T("Инструкция для моей модели роутера"), Sub() OnOpenRouterSearch(Me, EventArgs.Empty))
        lnkOpenViewer = MakeLink(Localization.T("Открыть Fast Media Sorter"), Sub() OnOpenViewerClicked(Me, EventArgs.Empty))
        ' The language picker names the CURRENT language in its own script, so it reads
        ' as an answer ("Deutsch") rather than a question - and is recognisable to
        ' someone who cannot read the rest of the window yet.
        lnkLanguage = MakeLink(Localization.CurrentName, Sub() UiLanguage.ShowLanguageMenu(lnkLanguage))
        flow.Controls.AddRange(New Control() {lnkAndroid, Sep(), lnkSiteGuide, Sep(), lnkRouterSearch, Sep(), lnkOpenViewer, Sep(), lnkLanguage})
        pnlBottom.Controls.Add(flow)
        pnlBottom.Controls.Add(lblHint)

        ' --- right column: phone access. Structure that survives any DPI + scrollbar:
        '   outer = single-column (Percent 100) TableLayoutPanel, AutoScroll vertical
        '     - lblState
        '     - addr sub-grid (caption | value | copy) - AutoSize, left-packed, never spans
        '     - full-width action buttons (Dock=Fill) one per row
        ' The address rows and the wide buttons live in DIFFERENT panels, so a long button
        ' can never widen the address columns and a vertical scrollbar (single Percent
        ' column) can never trigger a horizontal one. -----------------------------------
        Dim grpServer As New GroupBox With {.Dock = DockStyle.Right, .Width = 500, .Padding = New Padding(14, 8, 14, 12),
            .Text = Localization.T("Доступ с Android")}
        Dim outer As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoScroll = True}
        outer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        lblState = New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 10), .Font = New Font(Me.Font, FontStyle.Bold)}
        AddRow(outer, lblState)

        Dim grid As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 3, .Margin = New Padding(0)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        AddServerRow(grid, Localization.T("Через интернет:"), AddressOf InternetStatusDisplay, AddressOf InternetAddressValue, True)
        AddServerRow(grid, Localization.T("Дома (Wi-Fi):"), AddressOf LanDisplay, AddressOf CurrentLanAddress, True)
        AddServerRow(grid, Localization.T("IPv6:"), AddressOf Ipv6Value, AddressOf Ipv6Value, False)
        AddServerRow(grid, Localization.T("Ключ узла:"), AddressOf FingerprintValue, AddressOf FingerprintValue, False)
        AddServerRow(grid, Localization.T("Логин:"), AddressOf LoginValue, AddressOf LoginValue, False)
        AddServerRow(grid, Localization.T("Пароль:"), AddressOf PasswordValue, AddressOf PasswordValue, False)
        AddRow(outer, grid)

        ' The grid above is ambiguous on its own: a running share prints the LAN address and
        ' the internet address one under the other, which reads as "both work" even when the
        ' router was never configured. These two lines say what is actually reachable right
        ' now, and then name the ONE control for each of the two goals (a QR code for home
        ' use, or the router setup below) - so the state carries its own next step.
        lblAccessState = New Label With {.AutoSize = True, .MaximumSize = New Size(340, 0), .Margin = New Padding(0, 10, 0, 2)}
        AddRow(outer, lblAccessState)
        lblAccessNext = New Label With {.AutoSize = True, .MaximumSize = New Size(340, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 0, 0, 2)}
        AddRow(outer, lblAccessNext)

        btnTest = New Button With {.Dock = DockStyle.Fill, .Height = 34, .Margin = New Padding(0, 8, 0, 2),
            .Text = Localization.T("Проверить доступ из интернета"), .Visible = False}
        AddHandler btnTest.Click, AddressOf OnTestClicked
        AddRow(outer, btnTest)

        lnkRouter = New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 8, 0, 8)}
        AddHandler lnkRouter.LinkClicked, Sub() OnOpenRouter(Me, EventArgs.Empty)
        AddRow(outer, lnkRouter)

        btnGuide = New Button With {.Dock = DockStyle.Fill, .Height = 34, .Margin = New Padding(0, 4, 0, 4), .Text = Localization.T("Как настроить доступ через интернет")}
        AddHandler btnGuide.Click, AddressOf OnInternetAccess
        AddRow(outer, btnGuide)
        btnToggle = New Button With {.Dock = DockStyle.Fill, .Height = 42, .Margin = New Padding(0, 4, 0, 6), .Font = New Font(Me.Font, FontStyle.Bold), .Text = Localization.T("Начать раздачу")}
        AddHandler btnToggle.Click, AddressOf OnToggle
        AddRow(outer, btnToggle)
        chkAutostart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0), .Text = Localization.T("Запускать при входе в Windows")}
        AddHandler chkAutostart.CheckedChanged, AddressOf OnAutostartChanged
        AddRow(outer, chkAutostart)

        ' Independent of the autostart above (they answer different questions: whether the
        ' program starts, and whether it shows itself when it does). Off by default, and it
        ' governs EVERY plain start - the logon one, a double-click on the exe, a script -
        ' so unticked really means "no window" (TrayContext). Only an explicit request still
        ' opens it: the viewer's Share buttons, the tray icon, a folder to share.
        chkOpenOnStart = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0),
            .Text = Localization.T("Открывать окно менеджера при запуске")}
        AddHandler chkOpenOnStart.CheckedChanged, AddressOf OnOpenOnStartChanged
        AddRow(outer, chkOpenOnStart)
        toolTip.SetToolTip(chkOpenOnStart, Localization.T("Без галочки любой запуск программы оставляет только значок рядом с часами - окно открывается двойным щелчком по нему. С галочкой окно открывается сразу."))

        ' --- hosting: who actually keeps the folders reachable ------------------
        ' Right under the two autostart options on purpose - that is where the user is
        ' already asking "will this still be up tomorrow?", and in Server mode the
        ' honest answer is that neither checkbox above decides it (the service does).
        lblHosting = New Label With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 2),
            .MaximumSize = New Size(340, 0), .Font = New Font(Me.Font, FontStyle.Bold)}
        AddRow(outer, lblHosting)
        btnHosting = New Button With {.Dock = DockStyle.Fill, .Height = 30, .Margin = New Padding(0, 2, 0, 4),
            .Text = HostingText.ManageButton()}
        AddHandler btnHosting.Click, AddressOf OnHostingClicked
        AddRow(outer, btnHosting)

        ' Max simultaneous connections - the DoS-resilience knob (2026-07-15 review).
        ' Default 10; the user may set 1..99999 (their server, their call).
        Dim pnlConns As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = New Padding(0, 6, 0, 0)}
        Dim lblConns As New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
            .Text = Localization.T("Макс. одновременных подключений:")}
        numMaxConns = New NumericUpDown With {.Minimum = ShareSettings.MinMaxConnections, .Maximum = ShareSettings.MaxMaxConnections,
            .Value = ShareSettings.DefaultMaxConnections, .Width = 84, .Margin = New Padding(0, 2, 0, 0)}
        AddHandler numMaxConns.ValueChanged, AddressOf OnMaxConnsChanged
        AddHandler numMaxConns.Leave, AddressOf OnMaxConnsCommit
        pnlConns.Controls.Add(lblConns)
        pnlConns.Controls.Add(numMaxConns)
        AddRow(outer, pnlConns)
        toolTip.SetToolTip(numMaxConns, Localization.T("Сколько устройств могут быть подключены одновременно. По умолчанию 10; можно от 1 до 99999. Значение меньше 2 может кратко отклонять переподключение телефона."))

        ' Calm, factual reachability note (decision F): same-network devices can reach
        ' the share while it runs. Not a scare - an intended capability, surfaced here
        ' and in the docs rather than gated behind a toggle the mobile user must flip.
        Dim lblNetNote As New Label With {.AutoSize = True, .MaximumSize = New Size(340, 0),
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 8, 0, 0),
            .Text = ShareText.NetworkReachNote()}
        AddRow(outer, lblNetNote)

        ' --- usage stats block (only shown while serving; live-refreshed by _statsTimer) ---
        pnlStats = New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 2, .Margin = New Padding(0, 18, 0, 0), .Visible = False}
        pnlStats.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        pnlStats.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim statsHead As New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 6), .Font = New Font(Me.Font, FontStyle.Bold), .Text = Localization.T("Статистика раздачи")}
        pnlStats.Controls.Add(statsHead, 0, 0) : pnlStats.SetColumnSpan(statsHead, 2)
        lblStatLast = AddStatRow(pnlStats, Localization.T("Последнее подключение:"), 1)
        lblStatConns = AddStatRow(pnlStats, Localization.T("Подключений:"), 2)
        lblStatFiles = AddStatRow(pnlStats, Localization.T("Файлов отдано:"), 3)
        toolTip.SetToolTip(lblStatConns, Localization.T("Считается каждый сеанс связи. Один телефон может подключаться несколько раз (проверка доступа, просмотр файла, переподключение)."))
        AddRow(outer, pnlStats)

        grpServer.Controls.Add(outer)

        ' --- center: shared-folder list, buttons ABOVE the table ---------------
        Dim grpShares As New GroupBox With {.Dock = DockStyle.Fill, .Padding = New Padding(14, 8, 14, 12),
            .Text = Localization.T("Общие папки")}

        Dim pnlListButtons As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(0, 6, 0, 6)}
        btnAdd = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 6, 12, 6), .Text = Localization.T("Добавить папку.."),
            .Image = _addGlyph, .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .Font = New Font(Me.Font, FontStyle.Bold)}
        btnAddCurrent = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(8, 6, 8, 6), .Text = Localization.T("+ Текущая"), .Visible = _initialFolder.Length > 0}
        btnRemove = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 6, 10, 6), .Text = Localization.T("Убрать")}
        btnParams = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 6, 10, 6), .Text = Localization.T("Настроить..")}
        AddHandler btnAdd.Click, AddressOf OnAddFolder
        AddHandler btnAddCurrent.Click, AddressOf OnAddCurrentFolder
        AddHandler btnRemove.Click, AddressOf OnRemoveFolder
        AddHandler btnParams.Click, AddressOf OnConfigureFolder
        pnlListButtons.Controls.AddRange(New Control() {btnAdd, btnAddCurrent, btnRemove, btnParams})

        lvFolders = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .CheckBoxes = True,
            .FullRowSelect = True, .HideSelection = False, .MultiSelect = False}
        ' Widths come from ApplyDpiScaledAssets: column widths are the ONE thing the form's
        ' auto-scaling does not touch (WinForms never scales them), so they are converted from
        ' 96-DPI design units by hand - otherwise at 175% scaling a 170 px "Name" column holds
        ' barely a dozen of the now 1.75x taller glyphs.
        lvFolders.Columns.Add(Localization.T("Название"))
        lvFolders.Columns.Add(Localization.T("Тип"))
        lvFolders.Columns.Add(Localization.T("Папка"))
        lvFolders.Columns.Add("RO", 0, HorizontalAlignment.Center)
        AddHandler lvFolders.MouseDown, AddressOf OnListMouseDown
        AddHandler lvFolders.ItemCheck, AddressOf OnItemCheck
        AddHandler lvFolders.ItemChecked, AddressOf OnItemChecked
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder

        grpShares.Controls.Add(lvFolders)
        grpShares.Controls.Add(pnlListButtons)

        ' --- content panel + enable-gate overlay -------------------------------
        pnlContent = New Panel With {.Dock = DockStyle.Fill}
        pnlContent.Controls.Add(grpShares)
        pnlContent.Controls.Add(grpServer)
        pnlContent.Controls.Add(pnlTop)
        pnlContent.Controls.Add(pnlBottom)

        BuildEnableOverlay()

        progressBar = New ProgressBar With {.Dock = DockStyle.Top, .Height = 6, .Style = ProgressBarStyle.Marquee,
            .MarqueeAnimationSpeed = 30, .Visible = False}

        Me.Controls.Add(pnlContent)
        Me.Controls.Add(pnlEnable)
        Me.Controls.Add(progressBar)
        pnlEnable.BringToFront()
        progressBar.BringToFront()

        ' LAST, with every child in place: this is what makes the whole layout follow the
        ' display scaling instead of staying at 96 DPI under a 175% font (see DpiLayout).
        DpiLayout.ApplyAutoScale(Me)
        ApplyDpiScaledAssets()

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
    End Sub

    Private Sub BuildEnableOverlay()
        pnlEnable = New Panel With {.Dock = DockStyle.Fill, .BackColor = SystemColors.Control, .Visible = False, .Padding = New Padding(28)}
        Dim lblEnableTitle As New Label With {.Dock = DockStyle.Top, .Height = 36, .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.3F, FontStyle.Bold),
            .Text = Localization.T("Функции сервера выключены")}
        Dim lblEnableIntro As New Label With {.Dock = DockStyle.Top, .Height = 120, .Text = Localization.T("Общий доступ к папкам поднимает локальный SFTP-сервер и требует одного исключения в брандмауэре Windows (один раз, с правами администратора). Пока это не включено, программа ничего не раздаёт.")}
        btnEnable = New Button With {.Top = 168, .Left = 28, .Width = 300, .Height = 38, .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = Localization.T("Включить функции сервера..")}
        AddHandler btnEnable.Click, AddressOf OnEnableServer
        pnlEnable.Controls.Add(btnEnable)
        pnlEnable.Controls.Add(lblEnableIntro)
        pnlEnable.Controls.Add(lblEnableTitle)
    End Sub

    ' --- small UI builders ------------------------------------------------------

    Private Function MakeLink(text As String, onClick As Action) As LinkLabel
        Dim lnk As New LinkLabel With {.AutoSize = True, .Margin = New Padding(0, 4, 10, 0), .Text = text}
        AddHandler lnk.LinkClicked, Sub() onClick()
        Return lnk
    End Function

    Private Shared Function Sep() As Label
        Return New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 10, 0), .ForeColor = Color.Silver, .Text = "|"}
    End Function

    ''' <summary>Adds a caption|value|copy row to the server grid (no visible borders).
    ''' The value label width is capped so long values (host key, password) wrap and the
    ''' copy column stays aligned. <paramref name="extra"/> (the Test button) rides in the
    ''' value cell next to the value.</summary>
    Private Sub AddServerRow(grid As TableLayoutPanel, caption As String, valueFunc As Func(Of String), copyFunc As Func(Of String), alwaysShow As Boolean)
        Dim row As Integer = grid.RowCount
        grid.RowCount = row + 1
        grid.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        Dim cap As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 12, 6), .ForeColor = Color.DimGray, .Text = caption}
        ' Fixed-width single-line value cell with ellipsis: a long fingerprint/IPv6 is
        ' truncated (user copies it with the button) instead of wrapping to 3 lines or
        ' pushing the copy button off the edge. Scales with AutoScaleMode.Font.
        Dim val As New Label With {.AutoSize = False, .Height = 24, .Width = 190, .Anchor = AnchorStyles.Left,
            .AutoEllipsis = True, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0, 4, 8, 4)}
        ' NB: default (Standard) FlatStyle - a System-styled button is OS-drawn and ignores
        ' the managed Image, so the copy glyph would not appear.
        Dim copy As New Button With {.Width = 28, .Height = 26, .Margin = New Padding(2, 3, 0, 3), .Image = _copyGlyph,
            .ImageAlign = ContentAlignment.MiddleCenter, .TabStop = False, .Anchor = AnchorStyles.Left, .Tag = copyFunc}
        toolTip.SetToolTip(copy, Localization.T("Копировать в буфер"))
        AddHandler copy.Click, AddressOf OnCopyClick

        grid.Controls.Add(cap, 0, row)
        grid.Controls.Add(val, 1, row)
        grid.Controls.Add(copy, 2, row)
        _serverRows.Add(New ServerRow(cap, val, copy, valueFunc, copyFunc, alwaysShow))
    End Sub

    ''' <summary>Caption (col0) + value label (col1) for the usage-stats block.</summary>
    Private Shared Function AddStatRow(tlp As TableLayoutPanel, caption As String, row As Integer) As Label
        tlp.Controls.Add(New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 12, 1), .ForeColor = Color.DimGray, .Text = caption}, 0, row)
        Dim val As New Label With {.AutoSize = True, .MaximumSize = New Size(300, 0), .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 1), .Text = "-"}
        tlp.Controls.Add(val, 1, row)
        Return val
    End Function

    ''' <summary>Fills the usage-stats block from the current Status.Stats. Hidden when the
    ''' server is off or the (older) worker sends no stats.</summary>
    Private Sub UpdateStatsBlock()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim s As WorkerStats = If(_status IsNot Nothing, _status.Stats, Nothing)
        If Not running OrElse s Is Nothing Then
            pnlStats.Visible = False
            Return
        End If
        pnlStats.Visible = True
        Dim never As String = Localization.T("ещё не было")
        Dim lastAt As String = Share_Status_Form.FormatTime(s.LastConnectionAt)
        If lastAt.Length = 0 Then
            lblStatLast.Text = never
        ElseIf String.IsNullOrEmpty(s.LastConnectionAddress) Then
            lblStatLast.Text = lastAt
        Else
            lblStatLast.Text = lastAt & "  -  " & s.LastConnectionAddress
        End If
        lblStatConns.Text = String.Format(Localization.T("всего {0} (с запуска {1})"), s.TotalConnections, s.ConnectionsSinceStart)
        lblStatFiles.Text = String.Format(Localization.T("всего {0} (с запуска {1})"), s.FilesServedTotal, s.FilesServedSinceStart)
    End Sub

    ''' <summary>Set while a stats fetch is out, so the 10 s tick cannot stack requests behind
    ''' one that is waiting on a worker which stopped answering.</summary>
    Private _statsFetchInFlight As Boolean = False

    Private Sub OnStatsTick(sender As Object, e As EventArgs)
        If _statsFetchInFlight Then Return
        Dim t As Task = RefreshStatsAsync()
    End Sub

    ''' <summary>Re-fetches status just for the usage block (light - does not run the full
    ''' ApplyStatusToUi, so it never fights the user's in-flight actions).</summary>
    Private Async Function RefreshStatsAsync() As Task
        If _busy OrElse Not Me.Visible Then Return

        _statsFetchInFlight = True
        Dim st As WorkerStatus
        Try
            st = Await ShareController.GetStatusAsync()
        Finally
            _statsFetchInFlight = False
        End Try

        If IsDisposed OrElse st Is Nothing Then Return
        _status = st
        UpdateStatsBlock()
    End Function

    ''' <summary>Appends a control on a new auto-height row in a single-column panel.</summary>
    Private Shared Sub AddRow(tlp As TableLayoutPanel, c As Control)
        Dim row As Integer = tlp.RowCount
        tlp.RowCount = row + 1
        tlp.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        tlp.Controls.Add(c, 0, row)
    End Sub

    Private Sub OnCopyClick(sender As Object, e As EventArgs)
        Dim btn As Button = TryCast(sender, Button)
        If btn Is Nothing Then Return
        Dim provider As Func(Of String) = TryCast(btn.Tag, Func(Of String))
        If provider Is Nothing Then Return
        Dim value As String = provider()
        If String.IsNullOrEmpty(value) Then Return
        Try
            Clipboard.SetText(value)
            SetHint(Localization.T("Скопировано в буфер."))
        Catch
        End Try
    End Sub

    ''' <summary>Windows-style "copy" glyph (two overlapping documents), drawn at
    ''' <paramref name="size"/> px (the artwork below is authored for 16 px).</summary>
    Private Shared Function BuildCopyGlyph(size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 16.0F, size / 16.0F)
            ' Back page (offset), then front page over it - clear two-document "copy" mark.
            Using back As New SolidBrush(Color.FromArgb(214, 224, 238)) : g.FillRectangle(back, 3, 2, 7, 9) : End Using
            Using p As New Pen(Color.FromArgb(64, 92, 140), 1.3F)
                g.DrawRectangle(p, 3, 2, 7, 9)
                Using b As New SolidBrush(Color.White) : g.FillRectangle(b, 6, 5, 7, 9) : End Using
                g.DrawRectangle(p, 6, 5, 7, 9)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>Green "+" glyph for the Add-folder button, drawn at <paramref name="size"/> px
    ''' (the artwork below is authored for 18 px).</summary>
    Private Shared Function BuildAddGlyph(size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 18.0F, size / 18.0F)
            Using b As New SolidBrush(Color.FromArgb(46, 160, 67)) : g.FillEllipse(b, 1, 1, 16, 16) : End Using
            Using p As New Pen(Color.White, 2.4F)
                g.DrawLine(p, 9, 5, 9, 13)
                g.DrawLine(p, 5, 9, 13, 9)
            End Using
        End Using
        Return bmp
    End Function

    ' --- value providers (raw values for copy; display shown in the value cell) -

    Private Function InternetAddressValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim r As WorkerReachability = st.Reachability
        If r.IsCgnat OrElse String.IsNullOrEmpty(r.ExternalHost) Then Return ""
        Dim port As Integer = If(r.ExternalPort > 0, r.ExternalPort, st.ListenPort)
        Return r.ExternalHost & ":" & port.ToString()
    End Function

    Private Function InternetStatusDisplay() As String
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        If Not running Then Return "-"
        If reach Is Nothing Then Return Localization.T("определяется..")
        If reach.IsCgnat Then Return Localization.T("за CGNAT (недоступно)")
        Dim addr As String = InternetAddressValue()
        Return If(addr.Length > 0, addr, Localization.T("адрес неизвестен"))
    End Function

    Private Function CurrentLanAddress() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim lan As String = If(st.Reachability.LanAddress, "")
        If lan.Length = 0 OrElse st.ListenPort <= 0 Then Return ""
        Return lan & ":" & st.ListenPort.ToString()
    End Function

    Private Function LanDisplay() As String
        Dim addr As String = CurrentLanAddress()
        Return If(addr.Length > 0, addr, "-")
    End Function

    Private Function Ipv6Value() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse st.Reachability Is Nothing OrElse String.IsNullOrEmpty(st.Reachability.Ipv6Address) Then Return ""
        Return st.Reachability.Ipv6Address & ":" & st.ListenPort.ToString()
    End Function

    Private Function FingerprintValue() As String
        Return If(_status IsNot Nothing, If(_status.Fingerprint, ""), "")
    End Function

    Private Function LoginValue() As String
        Dim st As WorkerStatus = _status
        If st Is Nothing OrElse Not st.Running Then Return ""
        Return If(String.IsNullOrEmpty(st.Username), "fms", st.Username)
    End Function

    Private Function PasswordValue() As String
        Return If(_status IsNot Nothing, If(_status.Password, ""), "")
    End Function

    ' --- lifecycle --------------------------------------------------------------

    ''' <summary>Cap the window to the monitor working area and relax an over-large minimum.
    ''' AutoScaleMode.Font multiplies the fixed ClientSize/MinimumSize by the DPI factor, so at
    ''' 125-200% scaling the *minimum* can grow taller than a laptop screen - the window then
    ''' can't fit (and can't be shrunk), and the Start/Stop button + autostart row fall off the
    ''' bottom. The right-hand panel already AutoScrolls and the folder list flexes, so a capped
    ''' window degrades cleanly.</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>Dragged onto a monitor with different scaling: WinForms rescales the bounds,
    ''' paddings and fonts for us, but not the two things below, so they are re-derived here.</summary>
    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        BuildGlyphs()
        ApplyDpiScaledAssets()
    End Sub

    ''' <summary>ListView column widths - the one measurement WinForms never scales, in either
    ''' the auto-scale or the DPI-change path. Converted from the 96-DPI design units.</summary>
    Private Sub ApplyDpiScaledAssets()
        Try
            If lvFolders Is Nothing OrElse lvFolders.Columns.Count < 4 Then Return
            lvFolders.Columns(0).Width = LogicalToDeviceUnits(170)
            lvFolders.Columns(1).Width = LogicalToDeviceUnits(130)
            lvFolders.Columns(2).Width = LogicalToDeviceUnits(300)
            lvFolders.Columns(3).Width = LogicalToDeviceUnits(50)
        Catch
        End Try
    End Sub

    ''' <summary>(Re)draws the code-drawn button glyphs at the current display DPI and hands them
    ''' to the buttons that show them. On the first call (from BuildUi) those buttons do not exist
    ''' yet and pick the images up as they are created.</summary>
    Private Sub BuildGlyphs()
        Dim oldShare As Image = _shareGlyph
        Dim oldCopy As Image = _copyGlyph
        Dim oldAdd As Image = _addGlyph
        _shareGlyph = ShareIcons.CreateGlyphBitmap(LogicalToDeviceUnits(22))
        _copyGlyph = BuildCopyGlyph(LogicalToDeviceUnits(16))
        _addGlyph = BuildAddGlyph(LogicalToDeviceUnits(18))
        If btnShare IsNot Nothing Then btnShare.Image = _shareGlyph
        If btnAdd IsNot Nothing Then btnAdd.Image = _addGlyph
        For Each sr As ServerRow In _serverRows
            sr.Copy.Image = _copyGlyph
        Next
        ' Only now that nothing points at them any more.
        DisposeImage(oldShare)
        DisposeImage(oldCopy)
        DisposeImage(oldAdd)
    End Sub

    Private Shared Sub DisposeImage(img As Image)
        Try
            If img IsNot Nothing Then img.Dispose()
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Routes a folder into the already-open window - the "share this folder.." path from the
    ''' viewer when this window happens to be up.
    '''
    ''' It used to be dropped on the floor: TrayContext only passed the folder to the MainWindow
    ''' CONSTRUCTOR, so with the window already open the wake merely activated it. The user was
    ''' told nothing, the folder was never added, and the phone never saw it - and because it
    ''' worked whenever the window happened to be closed, the failure looked random.
    ''' </summary>
    Friend Async Function ShareFolderFromWakeAsync(folder As String) As Task
        If String.IsNullOrEmpty(folder) Then Return
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() Then Return
        If Not Directory.Exists(folder) Then Return

        ' Same three steps OnShownFirst takes for a folder handed to the constructor.
        If AddShareRow(folder) Then Await ApplySharedFoldersAsync()
        OnShareClicked(Me, EventArgs.Empty)
    End Function

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        If _entered Then Return
        _entered = True
        Await EnterAsync()
        _statsTimer.Start()   ' keep the usage-stats block live while the window is open
        If _initialFolder.Length > 0 AndAlso ServerFeatures.IsEnabled() AndAlso WorkerProcess.IsAvailable() Then
            If Directory.Exists(_initialFolder) AndAlso AddShareRow(_initialFolder) Then Await ApplySharedFoldersAsync()
            OnShareClicked(Me, EventArgs.Empty)
        ElseIf _openShareWhenReady Then
            ' Deferred tray "Поделиться.." - status is loaded now; open the wizard if the
            ' server came up, otherwise OnShareClicked will hint to start it.
            _openShareWhenReady = False
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        Try
            _settings.Save()
        Catch
        End Try
        Try : _statsTimer.Stop() : _statsTimer.Dispose() : Catch : End Try
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
            SetHint(Localization.T("Компонент общего доступа не найден - переустановите приложение."))
            SetServerControlsEnabled(False)
            Return
        End If

        SetBusy(True, Localization.T("Запуск компаньона.."))
        Dim st As WorkerStatus = Await ShareController.EnsureRunningReconciledAsync()
        If st Is Nothing Then
            ' In Server mode "cannot reach the companion" is the wrong diagnosis and
            ' the wrong advice: nothing here may spawn a worker, so the actionable
            ' fact is what the SERVICE is doing - and the fix lives one button away.
            If ServerFeatures.IsSystemServiceHost() Then
                SetHint(HostingText.ServiceStateLine(ServiceControl.QueryState()))
            Else
                SetHint(Localization.T("Не удалось связаться с компаньоном."))
            End If
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
                toolTip.SetToolTip(chkAutostart, Localization.T("Автозапуском управляет Windows (пакет из Store)."))
            End If
            chkOpenOnStart.Checked = _settings.OpenWindowOnStartup
            numMaxConns.Value = ShareSettings.ClampConnections(_settings.MaxConnections)
            UpdateHostingBlock()
            If ServerFeatures.IsSystemServiceHost() Then
                ' Autostart still governs whether the CONSOLE appears, but no longer
                ' whether the folders are reachable - say so where the confusion is.
                toolTip.SetToolTip(chkAutostart, HostingText.Intro(ServerFeatures.ServerHostMode.SystemService))
            End If
        Catch
        Finally
            _loading = prev
        End Try
    End Sub

    ' --- router -----------------------------------------------------------------

    Private Async Function DetectRouterAsync() As Task
        If _routerRequested Then Return
        _routerRequested = True
        Try
            _router = Await Task.Run(Function() RouterInfo.Detect())
            If Me.IsDisposed Then Return
            SetRouterLink()
        Catch
        End Try
    End Function

    Private Sub SetRouterLink()
        Dim model As String = If(_router IsNot Nothing, _router.DisplayName(), "")
        lnkRouter.Text = If(model.Length > 0, Localization.TF("Роутер: {0}", model), (Localization.T("Роутер: не определён")))
        lnkRouter.Enabled = True
    End Sub

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub OnOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            SetHint(Localization.T("Не удалось определить адрес роутера."))
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnOpenRouterSearch(sender As Object, e As EventArgs)
        SetHint(Localization.T("Определяем роутер.."))
        Dim rt As RouterIdentity = Await GetRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        SetHint(If(rt.DisplayName().Length > 0, Localization.TF("Роутер: {0}", rt.DisplayName()),
            Localization.T("Модель не определена - открыт общий поиск.")))
    End Sub

    ' --- internet test ----------------------------------------------------------

    Private Sub OnTestClicked(sender As Object, e As EventArgs)
        If _testing Then Return
        Dim st As WorkerStatus = _status
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        Dim host As String = If(reach IsNot Nothing, If(reach.ExternalHost, ""), "")
        Dim port As Integer = 0
        If reach IsNot Nothing Then port = If(reach.ExternalPort > 0, reach.ExternalPort, If(st IsNot Nothing, st.ListenPort, 0))
        If String.IsNullOrEmpty(host) OrElse port <= 0 Then
            SetHint(Localization.T("Адрес из интернета ещё не определён."))
            Return
        End If
        ' The answer goes into a modal, not into the bottom status strip: the button lives
        ' in the right-hand column and the strip is the opposite corner of the window, so
        ' the result read as an unrelated line rather than as the answer to this click.
        ' The strip still keeps the one-line verdict afterwards, as a lasting trace.
        _testing = True
        btnTest.Enabled = False
        Try
            Using dlg As New Share_Access_Test_Form(host, port)
                dlg.ShowDialog(Me)
                If dlg.ResultLine.Length > 0 Then SetHint(dlg.ResultLine)
            End Using
        Finally
            _testing = False
            RefreshTestButton()
        End Try
    End Sub

    Private Sub RefreshTestButton()
        Dim st As WorkerStatus = _status
        Dim can As Boolean = st IsNot Nothing AndAlso st.Running AndAlso st.Reachability IsNot Nothing AndAlso
                             Not st.Reachability.IsCgnat AndAlso Not String.IsNullOrEmpty(st.Reachability.ExternalHost)
        btnTest.Visible = can
        btnTest.Enabled = can AndAlso Not _testing AndAlso Not _busy
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
                .Description = Localization.T("Выберите папку, которую хотите открыть на телефоне")}
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

    Private Async Function AddFolderInteractive(path As String) As Task
        If String.IsNullOrWhiteSpace(path) Then Return
        If Not AddShareRow(path) Then
            SetHint(Localization.T("Эта папка уже в списке."))
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
                        it.SubItems(1).Text = ProfileLabel(dlg.Result)
                        it.SubItems(3).Text = RoLabel(dlg.Result)
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
            it.SubItems(1).Text = ProfileLabel(after)
            it.SubItems(3).Text = RoLabel(after)
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
        ' A freshly added folder defaults to the "Все файлы"/all_files profile (owner
        ' request) unless it already carries saved params. Persist it so the column, the
        ' resource dialog and the .fmscfg export all start from all_files. The class
        ' default stays "none" (Android import default / v1-purity invariant); only a
        ' new add in this app opts into all_files.
        Dim prm As ShareRootParams = ShareRootParamsStore.GetFor(path)
        If prm.IsDefault() Then
            prm.Profile = "all_files"
            ShareRootParamsStore.SetFor(path, prm)
        End If
        Dim prev As Boolean = _loading
        _loading = True
        Try
            Dim it As New ListViewItem(ShareFolderDisplayName(path)) With {.Checked = True, .Tag = path}
            it.SubItems.Add(ProfileLabel(prm))
            it.SubItems.Add(path)
            it.SubItems.Add(RoLabel(prm))
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
                Dim prm As ShareRootParams = ShareRootParamsStore.GetFor(host)
                Dim it As New ListViewItem(If(String.IsNullOrEmpty(r.name), ShareFolderDisplayName(host), r.name)) With {.Checked = True, .Tag = host}
                it.SubItems.Add(ProfileLabel(prm))
                it.SubItems.Add(host)
                it.SubItems.Add(RoLabel(prm))
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
        SetBusy(True, Localization.T("Обновляю список папок.."))
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
        SetBusy(True, Localization.T("Минутку.."))
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            SetHint(Localization.T("Останавливаю раздачу.."))
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
            ApplyStatusToUi()
            SetBusy(False)
            SetHint(Localization.T("Раздача остановлена."))
            Return
        End If

        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            SetBusy(False)
            SetHint(Localization.T("Сначала добавьте папку и отметьте её галочкой."))
            Return
        End If
        SetHint(Localization.T("Включаю раздачу.."))
        Dim res As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        _status = res.Status
        ApplyStatusToUi()
        SetBusy(False)
        SetHint(If(res.Served, Localization.T("Раздача запущена."),
                              Localization.T("Запущено, адрес не подтверждён - проверьте брандмауэр/сеть.")))
        If _status IsNot Nothing AndAlso _status.Running AndAlso _status.Reachability Is Nothing Then Await PollReachabilityAsync()
    End Sub

    ' --- status -> UI -----------------------------------------------------------

    Private Sub ApplyStatusToUi()
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running

        btnToggle.Text = If(running, Localization.T("Остановить раздачу"), Localization.T("Начать раздачу"))
        lblState.Text = If(running, Localization.T("Раздача с этого ПК работает"), Localization.T("Раздача выключена"))
        lblState.ForeColor = If(running, Color.ForestGreen, Color.DimGray)

        For Each sr As ServerRow In _serverRows
            Dim disp As String = sr.ValueFunc()
            Dim show As Boolean = running AndAlso (sr.AlwaysShow OrElse Not String.IsNullOrEmpty(disp))
            sr.Cap.Visible = show
            sr.Value.Visible = show
            sr.Value.Text = disp
            sr.Copy.Visible = show AndAlso Not String.IsNullOrEmpty(sr.CopyFunc())
        Next

        UpdateAccessSummary(running)
        RefreshTestButton()
        lnkRouter.Visible = running
        If running Then SetRouterLink()

        btnShare.Enabled = running AndAlso Not _busy
        btnGuide.Enabled = running AndAlso Not _busy
        lnkRouterSearch.Enabled = running

        If running AndAlso Not _routerRequested Then Dim t As Task = DetectRouterAsync()

        UpdateStatsBlock()
        UpdateHostingBlock()
        RaiseEvent ServerStateChanged(running)
    End Sub

    ''' <summary>Keeps the hosting line honest after every status refresh: the mode can
    ''' change under the window (an elevated install/remove ran, or the service was
    ''' stopped from services.msc), and a stale "Windows service" line would promise
    ''' availability nobody is providing.</summary>
    Private Sub UpdateHostingBlock()
        If lblHosting Is Nothing Then Return
        Dim line As String = HostingText.HostModeLine(ServerFeatures.HostMode())
        lblHosting.Text = line
        lblHosting.Visible = line.Length > 0
    End Sub

    ''' <summary>Opens the Hosting console. Re-reads status afterwards only when an
    ''' elevated action actually changed the machine - the service may have just been
    ''' installed, started or removed under us.</summary>
    Private Async Sub OnHostingClicked(sender As Object, e As EventArgs)
        Dim changed As Boolean
        Using dlg As New Share_Hosting_Form(_status)
            dlg.ShowDialog(Me)
            changed = dlg.Changed
        End Using
        UpdateHostingBlock()
        If Not changed Then Return
        SetBusy(True, Localization.T("Минутку.."))
        _status = Await ShareController.GetStatusAsync()
        ApplyStatusToUi()
        SetBusy(False)
    End Sub

    ''' <summary>Fills the "what works right now" + "what to do next" pair under the address
    ''' grid. Hidden entirely while nothing is being served - <c>lblState</c> already says so,
    ''' and advice about a share that is off would only be noise.</summary>
    Private Sub UpdateAccessSummary(running As Boolean)
        If Not running Then
            lblAccessState.Visible = False
            lblAccessNext.Visible = False
            Return
        End If
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        ' The port the outside world would knock on - the mapped external one when a mapping
        ' exists, else the listen port (which is what a manual forward is usually set to).
        Dim port As Integer = 0
        If _status IsNot Nothing Then
            port = If(reach IsNot Nothing AndAlso reach.ExternalPort > 0, reach.ExternalPort, _status.ListenPort)
        End If
        Dim state As String = ShareText.AccessStateLine(reach, port)
        Dim nextStep As String = ShareText.AccessNextStepLine(reach)
        lblAccessState.Text = state
        lblAccessState.ForeColor = AccessStateColor(reach)
        lblAccessState.Visible = state.Length > 0
        lblAccessNext.Text = nextStep
        lblAccessNext.Visible = nextStep.Length > 0
    End Sub

    ''' <summary>Colour for the state line. Green ONLY once an outside check actually confirmed
    ''' the internet path, amber when such a check ran and failed (something the user set up is
    ''' broken), plain text otherwise: LAN-only is a normal, perfectly usable state and must not
    ''' be painted as a problem just because the internet half is unproven.</summary>
    Private Shared Function AccessStateColor(reach As WorkerReachability) As Color
        If reach Is Nothing Then Return SystemColors.GrayText
        If reach.ExternalPortChecked Then
            Return If(reach.ExternalPortOpen, Color.ForestGreen, Color.FromArgb(176, 96, 0))
        End If
        Return SystemColors.ControlText
    End Function

    ' --- package wizard + viewer launch ----------------------------------------

    ''' <summary>Tray "Поделиться.." entry point: open the package wizard now if the
    ''' server is already up, else defer until the first status load finishes (the tray
    ''' can call this before OnShownFirst/EnterAsync has populated _status).</summary>
    Friend Sub OpenShareWizardFromTray()
        If _status Is Nothing Then
            ' Fresh window - status not loaded yet. Defer ONLY here: OnShownFirst consumes
            ' the flag once EnterAsync finishes. (Deferring in any other state leaves the
            ' flag dead, since OnShownFirst runs exactly once - the review's dead-flag bug.)
            _openShareWhenReady = True
        Else
            ' Window was already open (status loaded). Open now; OnShareClicked shows the
            ' "start the server first" hint when the server is not running.
            OnShareClicked(Me, EventArgs.Empty)
        End If
    End Sub

    Private Sub OnShareClicked(sender As Object, e As EventArgs)
        If _wizardOpen Then Return   ' guard tray re-entry while the modal wizard is already up
        If _status Is Nothing OrElse Not _status.Running Then
            SetHint(Localization.T("Сначала запустите сервер."))
            Return
        End If
        Dim preselect As New List(Of String)()
        For Each it As ListViewItem In lvFolders.Items
            If it.Checked Then preselect.Add(Convert.ToString(it.Tag))
        Next
        _wizardOpen = True
        Try
            Using dlg As New PackageWizardForm(preselect)
                dlg.ShowDialog(Me)
            End Using
        Finally
            _wizardOpen = False
        End Try
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
                SetHint(Localization.T("Fast Media Sorter не найден рядом."))
            End If
        Catch
        End Try
    End Sub

    ' --- enable gate + autostart ------------------------------------------------

    Private Sub OnEnableServer(sender As Object, e As EventArgs)
        Using dlg As New Share_Enable_Form()
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ApplyGate()
                _entered = False
                _listPopulated = False
                Dim t As Task = EnterAsync()
            End If
        End Using
    End Sub

    ' Persist the connection cap on every change (cheap, local); the live push to the
    ' worker happens on Leave (OnMaxConnsCommit) so holding the spinner does not
    ' restart the running server on each intermediate value.
    Private Sub OnMaxConnsChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.MaxConnections = ShareSettings.ClampConnections(CInt(numMaxConns.Value))
            _settings.Save()
        Catch
        End Try
    End Sub

    Private Async Sub OnMaxConnsCommit(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.MaxConnections = ShareSettings.ClampConnections(CInt(numMaxConns.Value))
            _settings.Save()
        Catch
        End Try
        Try
            Await ShareController.PushNetworkPolicyAsync()
        Catch
        End Try
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

    ''' <summary>Persists the "open the window at startup" sub-option. Read back by
    ''' <see cref="TrayContext"/> on the next silent (--tray) launch.</summary>
    Private Sub OnOpenOnStartChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Try
            _settings.OpenWindowOnStartup = chkOpenOnStart.Checked
            _settings.Save()
        Catch
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

    ''' <summary>"Тип" column: the folder's export profile in the same words the
    ''' resource dialog's "Тип ресурса" combo uses. A plain (none) folder shows
    ''' blank - like the RO column, the cell is empty at the default.</summary>
    Private Shared Function ProfileLabel(p As ShareRootParams) As String
        Dim token As String = If(p Is Nothing OrElse String.IsNullOrEmpty(p.Profile), "none", p.Profile)
        Select Case token
            Case "audio_library" : Return Localization.T("Аудиотека")
            Case "video_library" : Return Localization.T("Видеотека")
            Case "photo_storage" : Return Localization.T("Фотохранилище")
            Case "documents" : Return Localization.T("Документы")
            Case "all_files" : Return Localization.T("Все файлы")
            Case Else : Return ""   ' none / regular folder - keep the cell clean
        End Select
    End Function

    Private Shared Function ShareFolderDisplayName(path As String) As String
        Try
            Dim n As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(n) Then Return n
        Catch
        End Try
        Return path
    End Function

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
        If progressBar IsNot Nothing Then progressBar.Visible = value
        If value AndAlso Not String.IsNullOrEmpty(message) Then SetHint(message)
        Me.UseWaitCursor = value
    End Sub

End Class
