Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' The Share Manager main window's frame: how it is built, how it is sized, and what it
''' remembers (SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §3.1-§3.3).
'''
''' The shape in one paragraph: a single-column TableLayoutPanel with AutoScroll holds a
''' header strip (auto height), the folder-list block (an ABSOLUTE height recomputed on
''' every resize), and the three collapsible sections (auto height). Making the list row
''' absolute rather than percent is what implements §3.2 G4 - the list is the element that
''' gives up height when a section opens, down to a three-row floor, after which the window
''' grows downwards and, failing that, the outer column scrolls. A percent row could not
''' express the floor, and an AutoSize+AutoScroll panel is a documented WinForms conflict.
''' </summary>
Partial Public NotInheritable Class MainWindow

    ''' <summary>Row indices of the root layout - the list row's height is computed.</summary>
    Private Const HeaderRow As Integer = 0
    Private Const ListRow As Integer = 1
    Private Const SectionsRow As Integer = 2

    ''' <summary>Two-column threshold and its hysteresis, in logical px (decision A).
    ''' Different numbers on the way up and on the way down, so dragging the edge across
    ''' the boundary cannot make the layout flicker between the two.</summary>
    Private Const WideModeOn As Integer = 1040
    Private Const WideModeOff As Integer = 1000

    ''' <summary>Width of the sections column in two-column mode, logical px. Wide enough
    ''' for the address grid (caption + 190 px value + copy + reveal) without squeezing the
    ''' list below the four columns it needs.</summary>
    Private Const SectionsColumnWidth As Integer = 420

    ' --- UI construction --------------------------------------------------------

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = "Fast Media Sorter: Share Manager"
        Me.StartPosition = FormStartPosition.CenterScreen
        ' Small on purpose (§3.1). The old 980x700 floor scaled to 1715x1225 at 175%, i.e.
        ' past the height of the screen it was measured on - the window opened at its
        ' minimum and could not be shrunk. These two survive the same multiplication.
        Me.MinimumSize = New Size(560, 420)
        Me.ClientSize = New Size(760, 560)
        ' NB: the AutoScaleMode/AutoScaleDimensions pair is set at the END of this method
        ' (DpiLayout.ApplyAutoScale) - it only scales the children that already exist.
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        ' The glyphs are code-drawn bitmaps: auto-scaling grows the buttons around them but
        ' never the images, so they are drawn at the display's DPI right away.
        BuildGlyphs()
        toolTip = New ToolTip()
        _statsTimer = New Timer With {.Interval = 10000}
        AddHandler _statsTimer.Tick, AddressOf OnStatsTick

        _root = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2,
            .AutoScroll = True, .Padding = New Padding(12, 10, 12, 4)}
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 0.0F))
        _root.RowCount = 3
        _root.RowStyles.Add(New RowStyle(SizeType.AutoSize))              ' header strip
        _root.RowStyles.Add(New RowStyle(SizeType.Absolute, 240.0F))      ' folder list - recomputed
        _root.RowStyles.Add(New RowStyle(SizeType.AutoSize))              ' the three sections

        BuildHeaderStrip()
        BuildListBlock()
        BuildSections()
        _root.Controls.Add(_pnlHeader, 0, HeaderRow)
        _root.Controls.Add(_pnlList, 0, ListRow)
        _root.Controls.Add(_pnlSections, 0, SectionsRow)
        _root.SetColumnSpan(_pnlHeader, 2)
        _root.SetColumnSpan(_pnlList, 2)
        _root.SetColumnSpan(_pnlSections, 2)

        progressBar = New ProgressBar With {.Dock = DockStyle.Bottom, .Height = 6, .Style = ProgressBarStyle.Marquee,
            .MarqueeAnimationSpeed = 30, .Visible = False}

        ' --- content panel + enable-gate overlay -------------------------------
        ' WinForms docks from the END of the collection, so the Fill goes in FIRST and the
        ' status strip last: strip at the bottom edge, marquee directly above it.
        pnlContent = New Panel With {.Dock = DockStyle.Fill}
        pnlContent.Controls.Add(_root)
        pnlContent.Controls.Add(progressBar)
        pnlContent.Controls.Add(BuildStatusStrip())

        BuildEnableOverlay()

        Me.Controls.Add(pnlContent)
        Me.Controls.Add(pnlEnable)
        pnlEnable.BringToFront()

        AddHandler _root.ClientSizeChanged, AddressOf OnContentMetricsChanged
        AddHandler _pnlSections.SizeChanged, AddressOf OnContentMetricsChanged

        ' LAST, with every child in place: this is what makes the whole layout follow the
        ' display scaling instead of staying at 96 DPI under a 175% font (see DpiLayout).
        DpiLayout.ApplyAutoScale(Me)
        ApplyDpiScaledAssets()
        RestoreExpandedSections()

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
    End Sub

    ''' <summary>
    ''' The header strip: what is being shared right now, and the three things a session
    ''' actually does. Start/Stop is a plain button rather than the old 42 px accent bar -
    ''' adding a folder already starts the share, so it is the secondary action; the accent
    ''' belongs to "Поделиться", which is what a session is FOR.
    ''' </summary>
    Private Sub BuildHeaderStrip()
        _pnlHeader = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 0, 0, 6)}
        _pnlHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        _pnlHeader.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim state As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2)}
        state.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        state.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        state.RowCount = 3
        For i As Integer = 0 To 2
            state.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        lblStateDot = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Visible = False,
            .Margin = New Padding(0, 1, 6, 0), .Text = "●",
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 2.0F, FontStyle.Bold)}
        ' Both start in the empty-list state, which is what a freshly constructed window is
        ' in: the intro speaks, the state line waits for a status to describe.
        lblState = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0),
            .Visible = False, .Font = New Font(Me.Font, FontStyle.Bold)}
        ' Server edition only: the ONE administration fact a user reads while looking at a
        ' running share, because there neither checkbox in the settings window decides
        ' whether the folders stay reachable - the service does (§6, "availability is not
        ' hidden behind a fold"). In User mode HostModeLine is empty and the row collapses.
        lblHosting = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Visible = False,
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 1, 0, 0)}
        ' Instruction on the first run, wallpaper on every later one (decision D): it takes
        ' the state line's place only while there is nothing shared to describe.
        lblIntro = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left,
            .Margin = New Padding(0, 3, 0, 0),
            .Text = Localization.T("Откройте папки этого ПК на телефоне - по Wi-Fi или через интернет.")}

        state.Controls.Add(lblStateDot, 0, 0)
        state.Controls.Add(lblState, 1, 0)
        state.Controls.Add(lblHosting, 1, 1)
        state.Controls.Add(lblIntro, 0, 2)
        state.SetColumnSpan(lblIntro, 2)

        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False, .Anchor = AnchorStyles.Right, .Margin = New Padding(0)}

        ' Gear + word, not the gear alone: an icon-only button says what it does only to
        ' someone who hovers it, and this one is the way into every machine-level setting
        ' the window does not show.
        btnSettings = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(8, 5, 10, 5), .Margin = New Padding(0, 0, 8, 0), .Image = _gearGlyph,
            .Text = Localization.T("Настройки"),
            .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .AccessibleName = Localization.T("Настройки менеджера..")}
        AddHandler btnSettings.Click, AddressOf OnSettingsClicked
        toolTip.SetToolTip(btnSettings, Localization.T("Настройки менеджера.."))

        btnToggle = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 0, 8, 0),
            .Text = Localization.T("Начать раздачу")}
        AddHandler btnToggle.Click, AddressOf OnToggle

        btnShare = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 5, 14, 5), .Margin = New Padding(0),
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size + 2.0F, FontStyle.Bold),
            .Text = Localization.T("Поделиться"), .Image = _shareGlyph,
            .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter}
        AddHandler btnShare.Click, AddressOf OnShareClicked

        actions.Controls.AddRange(New Control() {btnSettings, btnToggle, btnShare})

        _pnlHeader.Controls.Add(state, 0, 0)
        _pnlHeader.Controls.Add(actions, 1, 0)
    End Sub

    ''' <summary>The folder list and its four buttons - the one object the window is about,
    ''' and now the only element that grows with the window (§2 rule 6). The GroupBox frame
    ''' is gone: the buttons above already name the block, and the frame cost a row of
    ''' padding on a window whose whole point is fitting a small screen.</summary>
    Private Sub BuildListBlock()
        _pnlList = New Panel With {.Dock = DockStyle.Fill, .Margin = New Padding(0)}

        _pnlListButtons = New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(0, 0, 0, 6)}
        btnAdd = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 12, 5), .Text = Localization.T("Добавить папку.."),
            .Image = _addGlyph, .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .Font = New Font(Me.Font, FontStyle.Bold)}
        btnAddCurrent = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(8, 5, 8, 5), .Text = Localization.T("+ Текущая"), .Visible = _initialFolder.Length > 0}
        btnRemove = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Убрать")}
        ' Disabled until a row is selected: it edits THE selected folder, and a button that
        ' silently does nothing is worse than one that shows it has nothing to act on.
        btnParams = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5),
            .Text = Localization.T("Редактировать"), .Enabled = False}
        AddHandler btnAdd.Click, AddressOf OnAddFolder
        AddHandler btnAddCurrent.Click, AddressOf OnAddCurrentFolder
        AddHandler btnRemove.Click, AddressOf OnRemoveFolder
        AddHandler btnParams.Click, AddressOf OnConfigureFolder
        _pnlListButtons.Controls.AddRange(New Control() {btnAdd, btnAddCurrent, btnRemove, btnParams})

        ' ShowItemToolTips: a row the service account cannot read is coloured and explains
        ' itself on hover (RefreshFolderAccessWarnings) - without this flag the item tooltip
        ' is simply never shown, and the colour alone would not say why.
        lvFolders = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .CheckBoxes = True,
            .FullRowSelect = True, .HideSelection = False, .MultiSelect = False, .ShowItemToolTips = True}
        ' Widths come from ApplyDpiScaledAssets: column widths are the ONE thing the form's
        ' auto-scaling does not touch (WinForms never scales them), so they are converted from
        ' 96-DPI design units by hand - otherwise at 175% scaling a 170 px "Name" column holds
        ' barely a dozen of the now 1.75x taller glyphs.
        lvFolders.Columns.Add(Localization.T("Название"))
        lvFolders.Columns.Add(Localization.T("Тип"))
        lvFolders.Columns.Add(Localization.T("Папка"))
        lvFolders.Columns.Add("RO", 0, HorizontalAlignment.Center)
        AddHandler lvFolders.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler lvFolders.MouseDown, AddressOf OnListMouseDown
        AddHandler lvFolders.ItemCheck, AddressOf OnItemCheck
        AddHandler lvFolders.ItemChecked, AddressOf OnItemChecked
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder

        ' Fill first, then the docked button row - see the docking note in BuildUi.
        _pnlList.Controls.Add(lvFolders)
        _pnlList.Controls.Add(_pnlListButtons)
    End Sub

    ''' <summary>The three sections of §3.4, in the fixed order access -> internet ->
    ''' statistics: what works on this Wi-Fi, what works from outside, what has happened.</summary>
    Private Sub BuildSections()
        _pnlSections = New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 1,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 4, 0, 0)}
        _pnlSections.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _secAccess = New CollapsibleSection("access", Localization.T("Доступ с телефона"))
        _secInternet = New CollapsibleSection("internet", Localization.T("Доступ из интернета"))
        ' Statistics stay out of the window until the worker actually sends counters -
        ' the same rule the old stats block followed, applied one level up (§3.4).
        _secStats = New CollapsibleSection("stats", Localization.T("Статистика")) With {.Visible = False}

        BuildAccessSection(_secAccess)
        BuildInternetSection(_secInternet)
        BuildStatsSection(_secStats)

        For Each sec As CollapsibleSection In New CollapsibleSection() {_secAccess, _secInternet, _secStats}
            sec.Dock = DockStyle.Fill
            AddHandler sec.ExpandedChanged, AddressOf OnSectionExpandedChanged
            Dim row As Integer = _pnlSections.RowCount
            _pnlSections.RowCount = row + 1
            _pnlSections.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            _pnlSections.Controls.Add(sec, 0, row)
        Next
    End Sub

    ''' <summary>Status strip: the one-line hint, the four links folded into a drop-down
    ''' (decision C), and the language link left visible beside it - it names the current
    ''' language in its own script and is the one control a user who cannot read the UI
    ''' must still be able to find.</summary>
    Private Function BuildStatusStrip() As Control
        Dim strip As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .ColumnCount = 3,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 4, 14, 6)}
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        lblHint = New Label With {.Dock = DockStyle.Fill, .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0, 0, 10, 0)}

        Dim menu As New ContextMenuStrip()
        miAndroid = AddHelpItem(menu, Localization.T("FastMediaSorter для Android"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite()))
        miSiteGuide = AddHelpItem(menu, Localization.T("Как публиковать папки (сайт)"), Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl))
        miRouterSearch = AddHelpItem(menu, Localization.T("Инструкция для моей модели роутера"), Sub() OnOpenRouterSearch(Me, EventArgs.Empty))
        miOpenViewer = AddHelpItem(menu, Localization.T("Открыть Fast Media Sorter"), Sub() OnOpenViewerClicked(Me, EventArgs.Empty))

        btnHelp = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Right, .Padding = New Padding(8, 3, 8, 3), .Margin = New Padding(0, 0, 12, 0),
            .Text = Localization.T("Справка ▾")}
        ' Opens UPWARDS: the strip is the bottom edge of the window, so a downward drop-down
        ' would be flipped by WinForms anyway - naming the direction keeps it predictable.
        AddHandler btnHelp.Click, Sub() menu.Show(btnHelp, New Point(0, 0), ToolStripDropDownDirection.AboveRight)

        lnkLanguage = New LinkLabel With {.AutoSize = True, .Anchor = AnchorStyles.Right,
            .Margin = New Padding(0, 4, 0, 0), .Text = Localization.CurrentName}
        AddHandler lnkLanguage.LinkClicked, Sub() UiLanguage.ShowLanguageMenu(lnkLanguage)

        strip.Controls.Add(lblHint, 0, 0)
        strip.Controls.Add(btnHelp, 1, 0)
        strip.Controls.Add(lnkLanguage, 2, 0)
        Return strip
    End Function

    Private Shared Function AddHelpItem(menu As ContextMenuStrip, text As String, onClick As Action) As ToolStripMenuItem
        Dim item As New ToolStripMenuItem(text)
        AddHandler item.Click, Sub() onClick()
        menu.Items.Add(item)
        Return item
    End Function

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

    ' --- section state ----------------------------------------------------------

    Private Function Sections() As CollapsibleSection()
        Return New CollapsibleSection() {_secAccess, _secInternet, _secStats}
    End Function

    ''' <summary>All collapsed by default (decision E), with §3.4's amber auto-expand as the
    ''' only exception. A key that is not in the CSV - a section added after the value was
    ''' written - simply starts collapsed; that is the whole reason it is one CSV value
    ''' rather than one registry flag per section.</summary>
    Private Sub RestoreExpandedSections()
        Dim saved As String = If(_settings.ExpandedSections, "")
        Dim open As New HashSet(Of String)(saved.Split(","c), StringComparer.OrdinalIgnoreCase)
        For Each sec As CollapsibleSection In Sections()
            sec.Expanded = open.Contains(sec.Key)
        Next
        _sectionsRestored = True
    End Sub

    Private Function ExpandedSectionsCsv() As String
        Dim keys As New List(Of String)()
        For Each sec As CollapsibleSection In Sections()
            If sec IsNot Nothing AndAlso sec.Expanded Then keys.Add(sec.Key)
        Next
        Return String.Join(",", keys)
    End Function

    ''' <summary>A toggle re-flows the window but never resizes it by itself; growing is
    ''' the one exception and only downwards, only when the list is already at its floor
    ''' (§3.2 G4, decision B) - a first expand on a small window that simply clipped would
    ''' read as broken.</summary>
    Private Sub OnSectionExpandedChanged(sender As Object, e As EventArgs)
        If Not _sectionsRestored Then Return
        SetPasswordRevealed(_secAccess IsNot Nothing AndAlso _secAccess.Expanded AndAlso _passwordRevealed)
        TryGrowForSections()
        RelayoutContent()
    End Sub

    Private Sub TryGrowForSections()
        If _root Is Nothing OrElse _pnlSections Is Nothing Then Return
        If Me.WindowState <> FormWindowState.Normal OrElse _wideMode Then Return
        ' Two pixels of slack: growing to EXACTLY the needed height leaves AutoScroll on the
        ' boundary and it shows a scrollbar for content that fits.
        Dim needed As Integer = HeaderHeight() + SectionsHeight() + MinimumListHeight() + _root.Padding.Vertical + 2
        Dim deficit As Integer = needed - _root.ClientSize.Height
        If deficit <= 0 Then Return
        Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
        Dim room As Integer = wa.Bottom - Me.Bounds.Bottom
        Dim grow As Integer = Math.Min(deficit, room)
        If grow > 0 Then Me.Height += grow
    End Sub

    ' --- geometry ---------------------------------------------------------------

    Private Sub OnContentMetricsChanged(sender As Object, e As EventArgs)
        RelayoutContent()
    End Sub

    ''' <summary>The header row is never the last one, so its laid-out height is honest.</summary>
    Private Function HeaderHeight() As Integer
        Return If(_pnlHeader Is Nothing, 0, _pnlHeader.Height + _pnlHeader.Margin.Vertical)
    End Function

    ''' <summary>
    ''' How much the sections NEED - deliberately their preferred size, never their laid-out
    ''' height.
    '''
    ''' A TableLayoutPanel with no Percent row hands the leftover space to its LAST row, and
    ''' the sections are in it: with everything collapsed the panel measured 210 px where its
    ''' content needed 60, so the list was told it had 150 px less than it really did and the
    ''' whole point of the redesign - the list is the element that grows - quietly failed on
    ''' the most common state of the window.
    ''' </summary>
    Private Function SectionsHeight() As Integer
        If _pnlSections Is Nothing Then Return 0
        Return _pnlSections.PreferredSize.Height + _pnlSections.Margin.Vertical
    End Function

    ''' <summary>The floor of §3.2 G4: the button row plus three list rows and the column
    ''' header. Below this a "list" stops being one.</summary>
    Private Function MinimumListHeight() As Integer
        Dim buttons As Integer = If(_pnlListButtons Is Nothing, 0, _pnlListButtons.Height)
        Return buttons + LogicalToDeviceUnits(92)
    End Function

    ''' <summary>
    ''' Gives the list every pixel the header and the sections did not take, but never less
    ''' than three rows. When even that does not fit, the root panel's AutoScroll takes over
    ''' - a single Percent column, so a vertical scrollbar can never trigger a horizontal one.
    ''' </summary>
    Private Sub RelayoutContent()
        If _root Is Nothing OrElse _relayouting Then Return
        _relayouting = True
        Try
            Dim available As Integer = _root.ClientSize.Height - _root.Padding.Vertical - HeaderHeight()
            If Not _wideMode Then available -= SectionsHeight()
            Dim height As Integer = Math.Max(MinimumListHeight(), available)
            ' Two-column mode puts the sections IN this row, beside the list, so the row has
            ' to be tall enough for whichever of the two is taller - otherwise an expanded
            ' section would be clipped by a row sized for the list alone.
            If _wideMode Then height = Math.Max(height, SectionsHeight())
            Dim style As RowStyle = _root.RowStyles(ListRow)
            If style.SizeType <> SizeType.Absolute OrElse CInt(style.Height) <> height Then
                style.SizeType = SizeType.Absolute
                style.Height = height
            End If
        Catch
        Finally
            _relayouting = False
        End Try
    End Sub

    ''' <summary>Decision A: on a wide window the sections move beside the list instead of
    ''' under it - today's shape, minus the settings clutter.</summary>
    Private Sub UpdateContentMode()
        If _root Is Nothing Then Return
        Dim logical As Integer = CInt(Me.ClientSize.Width * 96L \ CLng(Math.Max(96, Me.DeviceDpi)))
        If _contentModeApplied Then
            If Not _wideMode AndAlso logical < WideModeOn Then Return
            If _wideMode AndAlso logical > WideModeOff Then Return
        End If
        ApplyContentMode(logical >= WideModeOn)
    End Sub

    Private Sub ApplyContentMode(wide As Boolean)
        If _contentModeApplied AndAlso wide = _wideMode Then Return
        _wideMode = wide
        _contentModeApplied = True
        _root.SuspendLayout()
        Try
            If wide Then
                _root.ColumnStyles(1) = New ColumnStyle(SizeType.Absolute, CSng(LogicalToDeviceUnits(SectionsColumnWidth)))
                _root.SetColumnSpan(_pnlList, 1)
                _root.SetColumnSpan(_pnlSections, 1)
                _root.SetCellPosition(_pnlSections, New TableLayoutPanelCellPosition(1, ListRow))
                _pnlSections.Dock = DockStyle.Top
                _pnlSections.Margin = New Padding(10, 0, 0, 0)
            Else
                _root.ColumnStyles(1) = New ColumnStyle(SizeType.Absolute, 0.0F)
                _root.SetCellPosition(_pnlSections, New TableLayoutPanelCellPosition(0, SectionsRow))
                _root.SetColumnSpan(_pnlList, 2)
                _root.SetColumnSpan(_pnlSections, 2)
                ' Top, not Fill: the sections sit in the last row, which absorbs whatever
                ' height the computation above left over, and they must hug the list rather
                ' than float in the middle of a stretched row.
                _pnlSections.Dock = DockStyle.Top
                _pnlSections.Margin = New Padding(0, 4, 0, 0)
            End If
        Finally
            _root.ResumeLayout(True)
        End Try
    End Sub

    ''' <summary>
    ''' Restores the remembered rectangle. Every failure degrades to the default rather
    ''' than to an unusable window: a size is clamped to the minimum and to the working
    ''' area, and a position is honoured ONLY if the restored rectangle still lands on some
    ''' screen - a share manager that opens off-screen because the laptop left the docking
    ''' station is a support ticket.
    ''' </summary>
    Private Sub RestoreGeometry()
        Try
            If _settings.WindowWidth <= 0 OrElse _settings.WindowHeight <= 0 Then Return

            Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
            Dim w As Integer = Math.Min(Math.Max(LogicalToDeviceUnits(_settings.WindowWidth), Me.MinimumSize.Width), wa.Width)
            Dim h As Integer = Math.Min(Math.Max(LogicalToDeviceUnits(_settings.WindowHeight), Me.MinimumSize.Height), wa.Height)
            Me.Size = New Size(w, h)

            Dim candidate As New Rectangle(_settings.WindowX, _settings.WindowY, w, h)
            If IntersectsAnyScreen(candidate) Then
                Me.StartPosition = FormStartPosition.Manual
                Me.Location = candidate.Location
            End If
        Catch
        End Try
    End Sub

    Private Sub ApplyMaximizedState()
        Try
            If _settings.WindowMaximized Then Me.WindowState = FormWindowState.Maximized
        Catch
        End Try
    End Sub

    Private Shared Function IntersectsAnyScreen(r As Rectangle) As Boolean
        Try
            For Each s As Screen In Screen.AllScreens
                If s.WorkingArea.IntersectsWith(r) Then Return True
            Next
        Catch
        End Try
        Return False
    End Function

    ''' <summary>
    ''' Snapshots the geometry into the settings POCO, ready for Save(). Sizes go out in
    ''' LOGICAL px so a window sized on a 175% monitor reopens the same physical size on a
    ''' 100% one; the position stays raw, because a scaled screen coordinate means nothing
    ''' on a multi-monitor desktop. The size is taken from RestoreBounds when maximised, so
    ''' a maximised session does not overwrite the remembered normal size.
    ''' </summary>
    Private Sub CaptureGeometry()
        Dim bounds As Rectangle = If(Me.WindowState = FormWindowState.Normal, Me.Bounds, Me.RestoreBounds)
        Dim dpi As Long = CLng(Math.Max(96, Me.DeviceDpi))
        _settings.WindowX = bounds.X
        _settings.WindowY = bounds.Y
        _settings.WindowWidth = CInt(bounds.Width * 96L \ dpi)
        _settings.WindowHeight = CInt(bounds.Height * 96L \ dpi)
        _settings.WindowMaximized = (Me.WindowState = FormWindowState.Maximized)
        _settings.ExpandedSections = ExpandedSectionsCsv()
    End Sub

    ' --- DPI-scaled assets ------------------------------------------------------

    ''' <summary>ListView column widths - the one measurement WinForms never scales, in either
    ''' the auto-scale or the DPI-change path. Converted from the 96-DPI design units.</summary>
    Private Sub ApplyDpiScaledAssets()
        Try
            If _root IsNot Nothing AndAlso _wideMode Then
                _root.ColumnStyles(1) = New ColumnStyle(SizeType.Absolute, CSng(LogicalToDeviceUnits(SectionsColumnWidth)))
            End If
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
        Dim oldGear As Image = _gearGlyph
        Dim oldEye As Image = _eyeGlyph
        _shareGlyph = ShareIcons.CreateGlyphBitmap(LogicalToDeviceUnits(22))
        _copyGlyph = BuildCopyGlyph(LogicalToDeviceUnits(16))
        _addGlyph = BuildAddGlyph(LogicalToDeviceUnits(18))
        _gearGlyph = BuildGearGlyph(LogicalToDeviceUnits(18))
        _eyeGlyph = BuildEyeGlyph(LogicalToDeviceUnits(16))
        If btnShare IsNot Nothing Then btnShare.Image = _shareGlyph
        If btnAdd IsNot Nothing Then btnAdd.Image = _addGlyph
        If btnSettings IsNot Nothing Then btnSettings.Image = _gearGlyph
        If btnRevealPassword IsNot Nothing Then btnRevealPassword.Image = _eyeGlyph
        For Each sr As ServerRow In _serverRows
            sr.Copy.Image = _copyGlyph
        Next
        ' Only now that nothing points at them any more.
        DisposeImage(oldShare)
        DisposeImage(oldCopy)
        DisposeImage(oldAdd)
        DisposeImage(oldGear)
        DisposeImage(oldEye)
    End Sub

    Private Shared Sub DisposeImage(img As Image)
        Try
            If img IsNot Nothing Then img.Dispose()
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

    ''' <summary>Settings gear, drawn at <paramref name="size"/> px (authored for 18 px).
    ''' Code-drawn like its neighbours rather than the "⚙" character: the app pins its own
    ''' UI font and swaps the family per script (Nirmala UI, YaHei), and a font without that
    ''' code point would put a substituted box on the one button with no caption.</summary>
    Private Shared Function BuildGearGlyph(size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 18.0F, size / 18.0F)
            Using p As New Pen(Color.FromArgb(70, 82, 98), 2.0F)
                ' Eight teeth as spokes past the rim, then the rim and the hub over them.
                For i As Integer = 0 To 7
                    Dim a As Double = Math.PI * i / 4.0
                    g.DrawLine(p, 9.0F + CSng(Math.Cos(a) * 4.6), 9.0F + CSng(Math.Sin(a) * 4.6),
                                  9.0F + CSng(Math.Cos(a) * 8.0), 9.0F + CSng(Math.Sin(a) * 8.0))
                Next
                g.DrawEllipse(p, 3.4F, 3.4F, 11.2F, 11.2F)
                g.DrawEllipse(p, 6.6F, 6.6F, 4.8F, 4.8F)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>Reveal-password eye, drawn at <paramref name="size"/> px (authored for 16 px).</summary>
    Private Shared Function BuildEyeGlyph(size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 16.0F, size / 16.0F)
            Using p As New Pen(Color.FromArgb(70, 82, 98), 1.4F)
                g.DrawCurve(p, New PointF() {New PointF(1.5F, 8), New PointF(8, 3.2F), New PointF(14.5F, 8)})
                g.DrawCurve(p, New PointF() {New PointF(1.5F, 8), New PointF(8, 12.8F), New PointF(14.5F, 8)})
            End Using
            Using b As New SolidBrush(Color.FromArgb(70, 82, 98)) : g.FillEllipse(b, 6.2F, 6.2F, 3.6F, 3.6F) : End Using
        End Using
        Return bmp
    End Function

End Class
