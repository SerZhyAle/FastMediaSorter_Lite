Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' The Share Manager main window's frame: how it is built, how it is sized, and what it
''' remembers (SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §3.1-§3.3, spec 237 §3.4).
'''
''' The shape in one paragraph: a single-column TableLayoutPanel with AutoScroll holds a
''' header strip, the resources fold (whose body is the toolbar plus the folder list, an
''' ABSOLUTE-height panel recomputed on every resize - spec 237 rule L-1 sizes it to its
''' content, capped at eight rows), and the three collapsible sections. The list panel
''' carries its height directly because the fold around it is AutoSize: the sections then
''' snap up beneath a short list, and the list is still the element that gives up height
''' when a section opens, down to a three-row floor (§3.2 G4), after which the window
''' grows downwards and, failing that, the outer column scrolls. An AutoSize list panel
''' inside an AutoScroll parent is a documented WinForms conflict, which is exactly why
''' the height is assigned, never grown into.
''' </summary>
Partial Public NotInheritable Class MainWindow
    Implements IThemeAware

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

    ''' <summary>The narrowest column the header's state text may be left with beside the
    ''' button row, logical px - room for "Раздача работает - 12 папок, порт 40123" on one
    ''' line. Below it the buttons go under the text (UpdateHeaderMode); the second constant
    ''' is the extra room needed to go back, so the switch has hysteresis.</summary>
    Private Const HeaderStateMinWidth As Integer = 300
    Private Const HeaderStackHysteresis As Integer = 24

    ' --- UI construction --------------------------------------------------------

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = ProductNames.ShareManager
        Me.StartPosition = FormStartPosition.CenterScreen
        ' Small on purpose (§3.1). The old 980x700 floor scaled to 1715x1225 at 175%, i.e.
        ' past the height of the screen it was measured on - the window opened at its
        ' minimum and could not be shrunk. These two survive the same multiplication.
        Me.MinimumSize = New Size(560, 420)
        Me.ClientSize = New Size(760, 560)
        ' NB: the AutoScaleMode/AutoScaleDimensions pair is set at the END of this method
        ' (DpiLayout.ApplyAutoScale) - it only scales the children that already exist.
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        ' The catalog SVG paths are rasterised at the current display DPI: auto-scaling
        ' grows buttons but never an Image assigned to them.
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
        ' The resources fold (spec 237 S-0) carries the list block; its height is the
        ' body's - RelayoutContent sets the list panel's Height directly (rule L-1), and
        ' the fold wraps it with its header. An AutoSize row hands the leftover window
        ' height to the sections row below, which is what makes the sections snap up
        ' beneath a content-sized list instead of floating above a stretched one.
        _root.RowStyles.Add(New RowStyle(SizeType.AutoSize))              ' resources fold + list
        _root.RowStyles.Add(New RowStyle(SizeType.AutoSize))              ' the three sections

        BuildHeaderStrip()
        BuildListBlock()
        BuildResourcesFold()
        BuildSections()
        _root.Controls.Add(_pnlHeader, 0, HeaderRow)
        _root.Controls.Add(_secResources, 0, ListRow)
        _root.Controls.Add(_pnlSections, 0, SectionsRow)
        _root.SetColumnSpan(_pnlHeader, 2)
        _root.SetColumnSpan(_secResources, 2)
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
        ' Two rows: the second stays empty (zero height) while the state and the buttons
        ' share row 0, and holds the buttons when UpdateHeaderMode stacks them.
        _pnlHeader = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 0, 48, 6)}
        _pnlHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        _pnlHeader.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        _pnlHeader.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        _pnlHeader.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        Dim state As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2)}
        state.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        state.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        state.RowCount = 3
        For i As Integer = 0 To 2
            state.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        ' The state glyph (status.ok / status.error / status.unknown in the colour look -
        ' ICON-RENDER item C: a state indicator). It used to be a "●" in the state colour,
        ' which is no vocabulary shape; SetStateGlyph paints and names it.
        lblStateDot = New Label With {.AutoSize = False, .Anchor = AnchorStyles.Left, .Visible = False,
            .Margin = New Padding(0, 1, 6, 0), .Text = "",
            .Size = New Size(16, 16),
            .ImageAlign = ContentAlignment.MiddleCenter}
        _fontBold = New Font(Me.Font, FontStyle.Bold)
        _fontShare = New Font(Me.Font.FontFamily, Me.Font.Size + 2.0F, FontStyle.Bold)
        ' Both start in the empty-list state, which is what a freshly constructed window is
        ' in: the intro speaks, the state line waits for a status to describe.
        lblState = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0),
            .Visible = False, .Font = _fontBold}
        ' Server edition only: the ONE administration fact a user reads while looking at a
        ' running share, because there neither checkbox in the settings window decides
        ' whether the folders stay reachable - the service does (§6, "availability is not
        ' hidden behind a fold"). In User mode HostModeLine is empty and the row collapses.
        lblHosting = New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Visible = False,
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 1, 0, 0), .Cursor = Cursors.Hand}
        AddHandler lblHosting.Click, AddressOf OnHostingClicked
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
            .WrapContents = False, .Anchor = AnchorStyles.Right, .Margin = New Padding(0), .Tag = "no-wrap"}

        ' Gear + word, not the gear alone: an icon-only button says what it does only to
        ' someone who hovers it, and this one is the way into every machine-level setting
        ' the window does not show.
        btnSettings = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(8, 5, 10, 5), .Margin = New Padding(0, 0, 8, 0),
            .Text = Localization.T("Настройки"),
            .TextAlign = ContentAlignment.MiddleCenter, .AccessibleName = Localization.T("Настройки менеджера..")}
        ButtonGlyphs.Apply(btnSettings, "app.settings")
        AddHandler btnSettings.Click, AddressOf OnSettingsClicked
        toolTip.SetToolTip(btnSettings, Localization.T("Настройки менеджера.."))

        btnToggle = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 5, 12, 5), .Margin = New Padding(0, 0, 8, 0),
            .Text = Localization.T("Начать раздачу")}
        ' The glyph follows the caption: ApplyStatusToUi re-applies it when the share starts or stops.
        ButtonGlyphs.Apply(btnToggle, "action.run")
        AddHandler btnToggle.Click, AddressOf OnToggle

        btnShare = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 5, 14, 5), .Margin = New Padding(0),
            .Font = _fontShare,
            .Text = Localization.T("Поделиться"),
            .TextAlign = ContentAlignment.MiddleCenter}
        ButtonGlyphs.Apply(btnShare, "action.share")
        AddHandler btnShare.Click, AddressOf OnShareClicked

        ' WINDOWS-UI section 2 item 3: the header commands that act on the window's groups.
        ' Glyph-only, so each carries a tooltip and an accessible name; set apart from the
        ' three commands above by a wider gap.
        _btnExpandAll = NewGroupTool("btnExpandAll", Localization.T("Развернуть все"), _expandAllGlyph)
        _btnExpandAll.Margin = New Padding(16, 0, 4, 0)
        _btnCollapseAll = NewGroupTool("btnCollapseAll", Localization.T("Свернуть все"), _collapseAllGlyph)
        _btnCollapseAll.Margin = New Padding(0)
        AddHandler _btnExpandAll.Click, Sub() SetAllSectionsExpanded(True)
        AddHandler _btnCollapseAll.Click, Sub() SetAllSectionsExpanded(False)

        actions.Controls.AddRange(New Control() {btnSettings, btnToggle, btnShare, _btnExpandAll, _btnCollapseAll})

        _headerState = state
        _headerActions = actions
        _pnlHeader.Controls.Add(state, 0, 0)
        _pnlHeader.Controls.Add(actions, 1, 0)

        ' The button row's natural width follows its captions (language, Start/Stop), so a
        ' caption change re-decides the header shape instead of waiting for a resize.
        AddHandler btnToggle.TextChanged, Sub() RelayoutContent()
        AddHandler btnSettings.TextChanged, Sub() RelayoutContent()
        AddHandler btnShare.TextChanged, Sub() RelayoutContent()
    End Sub

    ''' <summary>A square glyph-only tool of the header row. The picture is drawn by
    ''' <see cref="BuildGlyphs"/> (theme and DPI), never a Unicode stand-in.</summary>
    Private Function NewGroupTool(name As String, caption As String, glyph As Image) As Button
        Dim tool As New Button With {.Name = name, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(8, 5, 8, 5), .Anchor = AnchorStyles.Left, .Text = "", .Image = glyph,
            .ImageAlign = ContentAlignment.MiddleCenter, .AccessibleName = caption}
        toolTip.SetToolTip(tool, caption)
        Return tool
    End Function

    ''' <summary>The folder list and its four buttons - the one object the window is about,
    ''' and now the only element that grows with the window (§2 rule 6). The GroupBox frame
    ''' is gone: the buttons above already name the block, and the frame cost a row of
    ''' padding on a window whose whole point is fitting a small screen.</summary>
    Private Sub BuildListBlock()
        ' Dock Top inside the resources fold's body row: the fold is AutoSize, so the
        ' panel's own Height - assigned by RelayoutContent, rule L-1 - is what sizes it,
        ' never the window.
        _pnlList = New Panel With {.Dock = DockStyle.Top, .Margin = New Padding(0), .Height = 240}

        _pnlListButtons = New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Tag = "no-wrap", .Padding = New Padding(0, 0, 0, 6)}
        _pnlListButtons.WrapContents = False
        btnAdd = New Button With {.Name = "btnAdd", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 8, 5), .Text = Localization.T("Добавить.."),
            .TextAlign = ContentAlignment.MiddleCenter, .Font = _fontBold, .Margin = New Padding(0, 0, 1, 0)}
        ButtonGlyphs.Apply(btnAdd, "action.add")

        btnAddDrop = New Button With {.Name = "btnAddDrop", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(4, 5, 4, 5), .Text = "▾",
            .TextAlign = ContentAlignment.MiddleCenter, .Font = _fontBold, .Margin = New Padding(0, 0, 8, 0)}

        _addMenu = New ContextMenuStrip()
        Dim mnuAddFolder As New ToolStripMenuItem(Localization.T("Папку.."))
        AddHandler mnuAddFolder.Click, AddressOf OnAddFolder

        Dim mnuAddCurrent As New ToolStripMenuItem(Localization.T("+ Текущая"))
        AddHandler mnuAddCurrent.Click, AddressOf OnAddCurrentFolder
        mnuAddCurrent.Visible = _initialFolder.Length > 0

        Dim mnuAddDisk As New ToolStripMenuItem(Localization.T("Диски .fdd.."))
        AddHandler mnuAddDisk.Click, AddressOf OnDisksClicked

        mnuAddBroadcast = New ToolStripMenuItem(Localization.T("Трансляции.."))
        AddHandler mnuAddBroadcast.Click, AddressOf OnBroadcastsClicked
        mnuAddBroadcast.Visible = False

        Dim mnuAddWizard As New ToolStripMenuItem(Localization.T("Мастер добавления ресурса.."))
        AddHandler mnuAddWizard.Click, Async Sub() Await OpenResourceAddAsync(Me, ResourceAddEntry.AddButton)

        _addMenu.Items.Add(mnuAddFolder)
        _addMenu.Items.Add(mnuAddCurrent)
        _addMenu.Items.Add(mnuAddDisk)
        _addMenu.Items.Add(mnuAddBroadcast)
        _addMenu.Items.Add(New ToolStripSeparator())
        _addMenu.Items.Add(mnuAddWizard)

        AddHandler _addMenu.Opening,
            Sub()
                mnuAddBroadcast.Visible = (btnBroadcasts IsNot Nothing AndAlso btnBroadcasts.Visible)
                If _initialFolder.Length > 0 Then
                    mnuAddCurrent.Text = Localization.TF("+ Текущая: {0}", System.IO.Path.GetFileName(_initialFolder))
                    mnuAddCurrent.Visible = True
                Else
                    mnuAddCurrent.Visible = False
                End If
                Dim iconSize As Integer = LogicalToDeviceUnits(16)
                Dim opener As Func(Of String, System.IO.Stream) = AddressOf CompanionAssets.OpenBundledAsset
                RenderMenuItemGlyph(mnuAddFolder, "action.add", iconSize, opener)
                RenderMenuItemGlyph(mnuAddCurrent, "action.add", iconSize, opener)
                RenderMenuItemGlyph(mnuAddDisk, "content.disk-container", iconSize, opener)
                RenderMenuItemGlyph(mnuAddBroadcast, "media.cast", iconSize, opener)
                RenderMenuItemGlyph(mnuAddWizard, "app.settings", iconSize, opener)
            End Sub
        AddHandler _addMenu.Disposed,
            Sub()
                mnuAddFolder.Image?.Dispose()
                mnuAddCurrent.Image?.Dispose()
                mnuAddDisk.Image?.Dispose()
                mnuAddBroadcast.Image?.Dispose()
                mnuAddWizard.Image?.Dispose()
            End Sub

        AddHandler btnAdd.Click, AddressOf OnAddFolder
        AddHandler btnAddDrop.Click, Sub()
            _addMenu.Show(btnAdd, New Point(0, btnAdd.Height))
        End Sub

        btnAddCurrent = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(8, 5, 8, 5), .Text = Localization.T("+ Текущая"), .Visible = False}
        btnRemove = New Button With {.Name = "btnRemove", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Убрать"), .Margin = New Padding(0, 0, 8, 0)}
        btnParams = New Button With {.Name = "btnParams", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5),
            .Text = Localization.T("Редактировать"), .Enabled = False, .Margin = New Padding(0, 0, 4, 0)}
        btnOutsideAccess = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5),
            .Text = Localization.T("Дать доступ наружу.."), .Enabled = False, .Visible = False}
        btnBroadcasts = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(10, 5, 10, 5),
            .Text = Localization.T("Трансляции.."), .Visible = False}
        ButtonGlyphs.Apply(btnAddCurrent, "action.add")
        ButtonGlyphs.Apply(btnRemove, "action.remove")
        ButtonGlyphs.Apply(btnParams, "action.edit")
        ButtonGlyphs.Apply(btnOutsideAccess, "action.share")
        ButtonGlyphs.Apply(btnBroadcasts, "media.cast")
        AddHandler btnAddCurrent.Click, AddressOf OnAddCurrentFolder
        AddHandler btnRemove.Click, AddressOf OnRemoveFolder
        AddHandler btnParams.Click, AddressOf OnConfigureFolder
        AddHandler btnOutsideAccess.Click, AddressOf OnOutsideAccessClicked
        AddHandler btnBroadcasts.Click, AddressOf OnBroadcastsClicked
        btnDisks = New Button With {.AutoSize = True, .Padding = New Padding(10, 5, 12, 5),
            .Text = Localization.T("Диски .fdd.."),
            .TextAlign = ContentAlignment.MiddleCenter, .Visible = False}
        ButtonGlyphs.Apply(btnDisks, "content.disk-container")
        AddHandler btnDisks.Click, AddressOf OnDisksClicked
        btnPhoneCode = New Button With {.Name = "btnPhoneCode", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(10, 5, 12, 5),
            .Text = Localization.T("Код телефона.."),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Margin = New Padding(0, 0, 0, 0)}
        ButtonGlyphs.Apply(btnPhoneCode, "device.phone")
        AddHandler btnPhoneCode.Click, Async Sub() Await OpenPhoneCodeIntakeAsync()

        Dim clipControls As Control() = BuildClipboardButtons()
        Dim listButtons As New List(Of Control)() From {btnAdd, btnAddDrop}
        listButtons.AddRange(clipControls)
        listButtons.AddRange(New Control() {btnParams, btnRemove, btnPhoneCode})
        _pnlListButtons.Controls.AddRange(listButtons.ToArray())

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
        ' Owner-drawn only for the padlock in the RO column (OnFolderDrawSubItem);
        ' headers and every other cell stay Windows-drawn.
        lvFolders.OwnerDraw = True
        AddHandler lvFolders.DrawColumnHeader, Sub(s, ev) ev.DrawDefault = True
        AddHandler lvFolders.DrawItem, Sub(s, ev) ev.DrawDefault = False
        AddHandler lvFolders.DrawSubItem, AddressOf OnFolderDrawSubItem
        AddHandler lvFolders.MouseMove, AddressOf OnFolderListMouseMove
        AddHandler lvFolders.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler lvFolders.MouseDown, AddressOf OnListMouseDown
        AddHandler lvFolders.ItemCheck, AddressOf OnItemCheck
        AddHandler lvFolders.ItemChecked, AddressOf OnItemChecked
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder
        AddHandler lvFolders.ClientSizeChanged, Sub() UpdateFolderColumnWidths()
        BuildDiskMenu()

        ' Fill first, then the docked button row - see the docking note in BuildUi.
        _emptyResourceHint = New Button With {.Dock = DockStyle.Bottom, .AutoSize = True,
            .MinimumSize = New Size(0, 44), .Text = Localization.T("Добавьте первый ресурс..")}
        ButtonGlyphs.Apply(_emptyResourceHint, "action.add")
        AddHandler _emptyResourceHint.Click, Async Sub() Await OpenResourceAddAsync(Me, ResourceAddEntry.EmptyList)
        _pnlList.Controls.Add(lvFolders)
        _pnlList.Controls.Add(_emptyResourceHint)
        _pnlList.Controls.Add(_pnlListButtons)
    End Sub

    ''' <summary>Spec 237 rule S-0: the single-row toolbar and the list travel inside one
    ''' fold titled "Общие ресурсы", so collapsing it compacts the whole window into a
    ''' status widget of live summaries. The body is full-bleed (no side indent): the
    ''' toolbar above the list already names the block, and the list needs the width.</summary>
    Private Sub BuildResourcesFold()
        _secResources = New CollapsibleSection("resources", Localization.T("Общие ресурсы")) With {.Dock = DockStyle.Fill}
        _secResources.Body.Padding = New Padding(0, LogicalToDeviceUnits(4), 0, 0)
        _secResources.AddBodyRow(_pnlList)
        AddHandler _secResources.ExpandedChanged, AddressOf OnSectionExpandedChanged
        UpdateResourcesSummary()
    End Sub

    ''' <summary>The three sections of §3.4, in the fixed order access -> internet ->
    ''' statistics: what works on this Wi-Fi, what works from outside, what has happened.</summary>
    Private Sub BuildSections()
        _pnlSections = New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 1,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 4, 0, 0)}
        _pnlSections.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _secAccess = New CollapsibleSection("access", Localization.T("Доступ с телефона"))
        _secInternet = New CollapsibleSection("internet", Localization.T("Доступ из интернета"))

        BuildAccessSection(_secAccess)
        BuildInternetSection(_secInternet)

        For Each sec As CollapsibleSection In New CollapsibleSection() {_secAccess, _secInternet}
            sec.Dock = DockStyle.Fill
            AddHandler sec.ExpandedChanged, AddressOf OnSectionExpandedChanged
            AddSectionsRow(sec)
        Next

        ' Statistics stay out of the window until the worker actually sends counters -
        ' the same rule the old stats block followed, applied one level up (§3.4). It is the
        ' last row but not a group: a link that opens the counters window.
        BuildStatsDoor()
        AddSectionsRow(_rowStats)
    End Sub

    Private Sub AddSectionsRow(c As Control)
        Dim row As Integer = _pnlSections.RowCount
        _pnlSections.RowCount = row + 1
        _pnlSections.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        _pnlSections.Controls.Add(c, 0, row)
    End Sub

    ''' <summary>Status strip: the one-line hint. The Help links and the language picker
    ''' moved into the manager settings (owner decision, 2026-10-03); the tray menu keeps a
    ''' language item of its own, so someone who cannot read the UI still finds one
    ''' without opening any window.</summary>
    Private Function BuildStatusStrip() As Control
        Dim strip As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .ColumnCount = 1,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 4, 14, 6)}
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        lblHint = New Label With {.Dock = DockStyle.Fill, .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0, 0, 10, 0)}

        strip.Controls.Add(lblHint, 0, 0)
        _statusStrip = strip
        Return strip
    End Function

    Private Sub BuildEnableOverlay()
        pnlEnable = New Panel With {.Dock = DockStyle.Fill, .BackColor = SystemColors.Control, .Visible = False, .Padding = New Padding(28)}
        _fontEnableTitle = New Font(Me.Font.FontFamily, Me.Font.Size * 1.3F, FontStyle.Bold)
        Dim lblEnableTitle As New Label With {.Dock = DockStyle.Top, .Height = 36, .Font = _fontEnableTitle,
            .Text = Localization.T("Функции сервера выключены")}
        Dim lblEnableIntro As New Label With {.Dock = DockStyle.Top, .Height = 120, .Text = Localization.T("Общий доступ к папкам поднимает локальный SFTP-сервер и требует одного исключения в брандмауэре Windows (один раз, с правами администратора). Пока это не включено, программа ничего не раздаёт.")}
        ' AutoSize with a 300x38 floor: the caption plus the glyph can outgrow 300 px in a long language.
        btnEnable = New Button With {.Top = 168, .Left = 28, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .MinimumSize = New Size(300, 38), .Font = _fontBold,
            .Text = Localization.T("Включить функции сервера..")}
        ButtonGlyphs.Apply(btnEnable, "action.run")
        AddHandler btnEnable.Click, AddressOf OnEnableServer
        pnlEnable.Controls.Add(btnEnable)
        pnlEnable.Controls.Add(lblEnableIntro)
        pnlEnable.Controls.Add(lblEnableTitle)
    End Sub

    ' --- section state ----------------------------------------------------------

    Private Function Sections() As CollapsibleSection()
        Return New CollapsibleSection() {_secAccess, _secInternet}
    End Function

    ''' <summary>The two header tools. Expanding goes resources first (it hands the window its
    ''' remembered height back before the sections ask to grow it); collapsing goes the other
    ''' way, so the resources fold compacts the window last and remembers the height the user had.
    ''' Each toggle is the ordinary one, so the persisted CSV and the window sizing follow.</summary>
    Private Sub SetAllSectionsExpanded(expanded As Boolean)
        If _secResources Is Nothing Then Return
        If expanded Then
            _secResources.Expanded = True
            For Each sec As CollapsibleSection In Sections()
                sec.Expanded = True
            Next
        Else
            For Each sec As CollapsibleSection In Sections()
                sec.Expanded = False
            Next
            _secResources.Expanded = False
        End If
    End Sub

    ''' <summary>All collapsed by default (decision E), with §3.4's amber auto-expand as the
    ''' only exception. A key that is not in the CSV - a section added after the value was
    ''' written - simply starts collapsed; that is the whole reason it is one CSV value
    ''' rather than one registry flag per section. The resources fold inverts the
    ''' convention (spec 237 S-0): it defaults to EXPANDED - G4, the list is the object the
    ''' window is about - so "resources" in the CSV records the COLLAPSED state.</summary>
    Private Sub RestoreExpandedSections()
        Dim saved As String = If(_settings.ExpandedSections, "")
        Dim open As New HashSet(Of String)(saved.Split(","c), StringComparer.OrdinalIgnoreCase)
        If _secResources IsNot Nothing Then _secResources.Expanded = Not open.Contains(_secResources.Key)
        For Each sec As CollapsibleSection In Sections()
            sec.Expanded = open.Contains(sec.Key)
        Next
        _sectionsRestored = True
    End Sub

    Private Function ExpandedSectionsCsv() As String
        Dim keys As New List(Of String)()
        If _secResources IsNot Nothing AndAlso Not _secResources.Expanded Then keys.Add(_secResources.Key)
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
        ' The resources fold is not a disclosure but a window mode (spec 237 S-0): the
        ' window itself compacts and un-compacts with it.
        If sender Is _secResources Then
            ApplyResourcesFoldWindowSizing()
            RelayoutContent()
            Return
        End If
        SetPasswordRevealed(_secAccess IsNot Nothing AndAlso _secAccess.Expanded AndAlso _passwordRevealed)
        TryGrowForSections()
        RelayoutContent()
    End Sub

    Private Sub TryGrowForSections()
        If _root Is Nothing OrElse _pnlSections Is Nothing Then Return
        If Me.WindowState <> FormWindowState.Normal OrElse _wideMode Then Return
        ' Two pixels of slack: growing to EXACTLY the needed height leaves AutoScroll on the
        ' boundary and it shows a scrollbar for content that fits.
        ' The list contributes its content size (rule L-1) - or nothing at all while the
        ' resources fold keeps it folded away (rule S-0).
        Dim listNeed As Integer = If(_secResources IsNot Nothing AndAlso Not _secResources.Expanded,
                                     0, Math.Max(MinimumListHeight(), DesiredListHeight()))
        Dim needed As Integer = HeaderHeight() + SectionsHeight() + listNeed + _root.Padding.Vertical + 2
        Dim deficit As Integer = needed - _root.ClientSize.Height
        If deficit <= 0 Then Return
        Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
        Dim room As Integer = wa.Bottom - Me.Bounds.Bottom
        Dim grow As Integer = Math.Min(deficit, room)
        If grow > 0 Then Me.Height += grow
    End Sub

    ' --- the resources fold as a window mode (spec 237 S-0) ----------------------

    ''' <summary>
    ''' Collapsing the resources fold compacts the WINDOW itself: the point of the fold is
    ''' a tiny status widget, which a fold inside an unchanged tall window cannot be. The
    ''' pre-collapse height is remembered and given back on expand, so the compaction never
    ''' costs the user their chosen window size. A maximized window is left alone - a fold
    ''' must not un-maximize anything - and the fold then acts as a plain disclosure.
    ''' </summary>
    Private Sub ApplyResourcesFoldWindowSizing()
        If _secResources Is Nothing Then Return
        If _secResources.Expanded Then
            If Not _resourcesCompacted Then Return
            _resourcesCompacted = False
            Dim target As Integer = If(_preCollapseWindowHeight > 0, _preCollapseWindowHeight, ExpandedResourcesHeight())
            _preCollapseWindowHeight = 0
            If Me.WindowState = FormWindowState.Normal Then ApplyWindowHeight(target)
        Else
            If Me.WindowState <> FormWindowState.Normal Then Return
            _resourcesCompacted = True
            If _preCollapseWindowHeight <= 0 Then _preCollapseWindowHeight = Me.Height
            ApplyWindowHeight(CollapsedResourcesHeight())
        End If
    End Sub

    Private Sub ApplyWindowHeight(target As Integer)
        Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
        Dim h As Integer = Math.Max(Me.MinimumSize.Height, Math.Min(target, wa.Height))
        If Me.Height <> h Then Me.Height = h
    End Sub

    ''' <summary>Title bar and borders, plus the docked hint strip (and the marquee bar on
    ''' the rare frames it shows) - everything outside <see cref="_root"/> that still
    ''' belongs to the window's height.</summary>
    Private Function InnerChromeHeight() As Integer
        Dim chrome As Integer = Me.Height - Me.ClientSize.Height
        If _statusStrip IsNot Nothing Then chrome += _statusStrip.Height
        If progressBar IsNot Nothing AndAlso progressBar.Visible Then chrome += progressBar.Height
        Return chrome
    End Function

    ''' <summary>The window height the folded widget needs: header strip, the fold's own
    ''' header, the sections - beside it in wide mode, beneath it otherwise - and the chrome.</summary>
    Private Function CollapsedResourcesHeight() As Integer
        Dim fold As Integer = If(_secResources Is Nothing, 0, _secResources.PreferredSize.Height)
        Dim sections As Integer = SectionsHeight()
        Dim body As Integer = If(_wideMode, Math.Max(fold, sections), fold + sections)
        Return HeaderHeight() + body + _root.Padding.Vertical + InnerChromeHeight() + 2
    End Function

    ''' <summary>The expand target when no pre-collapse height is known - the fold was
    ''' restored collapsed and is opened for the first time: the content-sized list, not
    ''' a guess. The slack covers the fold header and the body's padding.</summary>
    Private Function ExpandedResourcesHeight() As Integer
        Dim listArea As Integer = Math.Max(MinimumListHeight(), DesiredListHeight())
        Dim sections As Integer = SectionsHeight()
        Dim body As Integer = If(_wideMode, Math.Max(listArea, sections), listArea + sections)
        Return HeaderHeight() + body + LogicalToDeviceUnits(40) + _root.Padding.Vertical + InnerChromeHeight() + 2
    End Function

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
    ''' header, plus the empty-list hint when it is showing - it is docked inside the same
    ''' panel. Below this a "list" stops being one.</summary>
    Private Function MinimumListHeight() As Integer
        Dim buttons As Integer = If(_pnlListButtons Is Nothing, 0, _pnlListButtons.Height)
        Return buttons + EmptyHintHeight() + LogicalToDeviceUnits(92)
    End Function

    ''' <summary>Spec 237 Rule L-1: dynamic list height sized to (Items.Count + 1) rows,
    ''' capped at 8 rows - past the cap the list scrolls internally instead of growing.</summary>
    Private Function DesiredListHeight() As Integer
        Dim count As Integer = If(lvFolders Is Nothing, 0, Math.Max(1, lvFolders.Items.Count + 1))
        count = Math.Min(count, 8)
        Dim buttons As Integer = If(_pnlListButtons Is Nothing, 0, _pnlListButtons.Height)
        Dim rowHeight As Integer = LogicalToDeviceUnits(22)
        Dim headerHeight As Integer = LogicalToDeviceUnits(26)
        Return buttons + EmptyHintHeight() + headerHeight + (count * rowHeight) + LogicalToDeviceUnits(10)
    End Function

    ''' <summary>The "Добавьте первый ресурс.." hint is docked to the bottom of the same
    ''' panel the list fills, so when it shows it is part of the block's height.</summary>
    Private Function EmptyHintHeight() As Integer
        Return If(_emptyResourceHint IsNot Nothing AndAlso _emptyResourceHint.Visible, _emptyResourceHint.Height, 0)
    End Function

    ''' <summary>
    ''' Rule L-1 of spec 237 §3.4: the list block's height is its CONTENT - (rows + 1)
    ''' capped at eight - bounded below by the G4 floor and above by the space the window
    ''' actually has between the header and the sections. The panel carries the height
    ''' directly (the fold that wraps it is AutoSize), so a short list leaves the sections
    ''' snapping up beneath it instead of a stretched empty ListView.
    ''' </summary>
    Private Sub RelayoutContent()
        If _root Is Nothing OrElse _relayouting OrElse _pnlList Is Nothing Then Return
        _relayouting = True
        Try
            ' First: the header's shape decides the height every figure below subtracts.
            UpdateHeaderMode()
            Dim available As Integer = _root.ClientSize.Height - _root.Padding.Vertical - HeaderHeight()
            If Not _wideMode Then available -= SectionsHeight()
            Dim height As Integer = Math.Max(MinimumListHeight(), Math.Min(DesiredListHeight(), available))
            If _pnlList.Height <> height Then _pnlList.Height = height
            UpdateFolderColumnWidths()
        Catch
        Finally
            _relayouting = False
        End Try
    End Sub

    ''' <summary>
    ''' The header strip is one row while the state text keeps a readable column beside the
    ''' three buttons, and two rows - state above, whole buttons below - when it would not.
    ''' The unwrappable button row used to take what it wanted and leave the state column
    ''' ~130 px, where "Раздача работает - 1 папка, порт 40123" broke every few letters.
    ''' Measured rather than a width threshold: the buttons' width follows the language,
    ''' the Start/Stop caption and the display scaling. Stacked, the row also wraps whole
    ''' buttons at the window's width (WINDOWS-UI section 11 item 3). Hysteresis, so the
    ''' scroll bar that the taller header may bring cannot flip it back and forth.
    ''' </summary>
    Private Sub UpdateHeaderMode()
        If _pnlHeader Is Nothing OrElse _headerState Is Nothing OrElse _headerActions Is Nothing Then Return
        Dim available As Integer = _root.ClientSize.Width - _root.Padding.Horizontal - _pnlHeader.Margin.Horizontal
        If available <= 0 Then Return

        Dim buttonsWidth As Integer = _headerActions.Padding.Horizontal
        For Each b As Control In _headerActions.Controls
            buttonsWidth += b.PreferredSize.Width + b.Margin.Horizontal
        Next
        Dim needed As Integer = buttonsWidth + _headerState.Margin.Horizontal + LogicalToDeviceUnits(HeaderStateMinWidth)
        Dim slack As Integer = If(_headerStacked, LogicalToDeviceUnits(HeaderStackHysteresis), 0)
        Dim stack As Boolean = available < needed + slack

        If stack = _headerStacked Then
            If stack AndAlso _headerActions.MaximumSize.Width <> available Then
                _headerActions.MaximumSize = New Size(available, 0)
                _root.PerformLayout()
            End If
            Return
        End If

        _headerStacked = stack
        _pnlHeader.SuspendLayout()
        Try
            If stack Then
                _pnlHeader.SetColumnSpan(_headerState, 2)
                _pnlHeader.SetCellPosition(_headerActions, New TableLayoutPanelCellPosition(0, 1))
                _pnlHeader.SetColumnSpan(_headerActions, 2)
                _headerActions.Anchor = AnchorStyles.Left
                _headerActions.Margin = New Padding(0, LogicalToDeviceUnits(4), 0, 0)
                _headerActions.WrapContents = True
                _headerActions.MaximumSize = New Size(available, 0)
            Else
                _headerActions.MaximumSize = Size.Empty
                _headerActions.WrapContents = False
                _headerActions.Margin = New Padding(0)
                _headerActions.Anchor = AnchorStyles.Right
                _pnlHeader.SetColumnSpan(_headerActions, 1)
                _pnlHeader.SetCellPosition(_headerActions, New TableLayoutPanelCellPosition(1, 0))
                _pnlHeader.SetColumnSpan(_headerState, 1)
            End If
        Finally
            _pnlHeader.ResumeLayout(True)
        End Try
        _root.PerformLayout()
    End Sub

    Private Sub UpdateFolderColumnWidths()
        Try
            If lvFolders Is Nothing OrElse lvFolders.Columns.Count < 4 Then Return
            Dim fixedWidths As Integer = lvFolders.Columns(0).Width + lvFolders.Columns(1).Width + lvFolders.Columns(3).Width
            Dim availableFolderWidth As Integer = lvFolders.ClientSize.Width - fixedWidths - SystemInformation.VerticalScrollBarWidth
            lvFolders.Columns(2).Width = Math.Max(LogicalToDeviceUnits(250), availableFolderWidth)
        Catch
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
                _root.SetColumnSpan(_secResources, 1)
                _root.SetColumnSpan(_pnlSections, 1)
                _root.SetCellPosition(_pnlSections, New TableLayoutPanelCellPosition(1, ListRow))
                _pnlSections.Dock = DockStyle.Top
                _pnlSections.Margin = New Padding(10, 0, 0, 0)
            Else
                _root.ColumnStyles(1) = New ColumnStyle(SizeType.Absolute, 0.0F)
                _root.SetCellPosition(_pnlSections, New TableLayoutPanelCellPosition(0, SectionsRow))
                _root.SetColumnSpan(_secResources, 2)
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
        ' While the resources fold keeps the window compacted (spec 237 S-0), the
        ' remembered size is the pre-compaction one: reopening must give the working
        ' window back, not a widget that has to be pulled apart again.
        Dim heightPx As Integer = bounds.Height
        If _resourcesCompacted AndAlso _preCollapseWindowHeight > 0 Then heightPx = _preCollapseWindowHeight
        _settings.WindowHeight = CInt(heightPx * 96L \ dpi)
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
            If _pnlHeader IsNot Nothing Then
                _pnlHeader.Margin = New Padding(0, 0, LogicalToDeviceUnits(48), LogicalToDeviceUnits(6))
            End If
            If lvFolders Is Nothing OrElse lvFolders.Columns.Count < 4 Then Return
            lvFolders.Columns(0).Width = LogicalToDeviceUnits(170)
            lvFolders.Columns(1).Width = LogicalToDeviceUnits(130)
            lvFolders.Columns(3).Width = LogicalToDeviceUnits(50)
            UpdateFolderColumnWidths()
        Catch
        End Try
    End Sub

    ''' <summary>(Re)draws the code-drawn button glyphs at the current display DPI and hands them
    ''' to the buttons that show them. On the first call (from BuildUi) those buttons do not exist
    ''' yet and pick the images up as they are created.</summary>
    Private Sub BuildGlyphs()
        ' The captioned buttons (settings, share, add, disks ...) take their glyph from
        ' ButtonGlyphs, which follows theme and DPI by itself; only the glyph-only copy
        ' buttons and the hand-made eye are drawn here.
        Dim oldCopy As Image = _copyGlyph
        Dim oldEye As Image = _eyeGlyph
        Dim oldExpand As Image = _expandAllGlyph
        Dim oldCollapse As Image = _collapseAllGlyph
        _copyGlyph = CatalogGlyph("action.copy", 16)
        _eyeGlyph = BuildEyeGlyph(LogicalToDeviceUnits(16))
        _expandAllGlyph = CatalogGlyph("nav.expand", 16)
        _collapseAllGlyph = CatalogGlyph("nav.collapse", 16)
        If btnRevealPassword IsNot Nothing Then btnRevealPassword.Image = _eyeGlyph
        If _btnExpandAll IsNot Nothing Then _btnExpandAll.Image = _expandAllGlyph
        If _btnCollapseAll IsNot Nothing Then _btnCollapseAll.Image = _collapseAllGlyph
        For Each sr As ServerRow In _serverRows
            sr.Copy.Image = _copyGlyph
        Next
        ' The state glyph is drawn at device size too - repaint it for the new DPI.
        Dim stateEntry As String = If(lblStateDot IsNot Nothing, TryCast(lblStateDot.Tag, String), Nothing)
        If stateEntry IsNot Nothing Then
            lblStateDot.Tag = Nothing
            SetStateGlyph(stateEntry)
        End If
        ' Only now that nothing points at them any more.
        DisposeImage(oldCopy)
        DisposeImage(oldEye)
        DisposeImage(oldExpand)
        DisposeImage(oldCollapse)
    End Sub

    Private Shared Sub DisposeImage(img As Image)
        Try
            If img IsNot Nothing Then img.Dispose()
        Catch
        End Try
    End Sub

    Private Function CatalogGlyph(glyphId As String, logicalTier As Integer) As Bitmap
        Return GlyphRenderer.Render(glyphId, LogicalToDeviceUnits(logicalTier), ThemeService.Palette.TextPrimary,
                                   AddressOf CompanionAssets.OpenBundledAsset)
    End Function

    Friend Shared Sub RenderMenuItemGlyph(item As ToolStripMenuItem, glyphId As String, size As Integer, opener As Func(Of String, System.IO.Stream))
        If item Is Nothing OrElse String.IsNullOrEmpty(glyphId) Then Return
        Dim old As Image = item.Image
        item.Image = GlyphRenderer.Render(glyphId, size, item.ForeColor, opener)
        old?.Dispose()
    End Sub

    ''' <summary>Reveal-password eye, drawn at <paramref name="size"/> px (authored for 16 px).
    ''' Still code-drawn: the vocabulary has no reveal-password meaning yet (spec 027, the
    ''' desktop-meanings proposal). Inked in the theme colour, never a baked grey.</summary>
    Private Shared Function BuildEyeGlyph(size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 16.0F, size / 16.0F)
            Using p As New Pen(ThemeService.Palette.TextPrimary, 1.4F)
                g.DrawCurve(p, New PointF() {New PointF(1.5F, 8), New PointF(8, 3.2F), New PointF(14.5F, 8)})
                g.DrawCurve(p, New PointF() {New PointF(1.5F, 8), New PointF(8, 12.8F), New PointF(14.5F, 8)})
            End Using
            Using b As New SolidBrush(ThemeService.Palette.TextPrimary) : g.FillEllipse(b, 6.2F, 6.2F, 3.6F, 3.6F) : End Using
        End Using
        Return bmp
    End Function

    ' --- the application theme (spec 060) ---------------------------------------------------
    ' The service walks this window like any other; what it cannot reach is what is painted
    ' into bitmaps (the button glyphs, the state dot) and the list's per-row colours.

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        BuildGlyphs()
        If lvFolders IsNot Nothing Then
            RestripeList()
            lvFolders.Invalidate()
        End If
    End Sub

End Class
