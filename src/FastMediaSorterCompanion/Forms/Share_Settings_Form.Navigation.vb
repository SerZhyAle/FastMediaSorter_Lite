Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>The left menu as a tree (210): each page row keeps its icon, caption, selection
''' tint, accent stripe and focus rectangle and gains a disclosure chevron; the page's former
''' fold groups sit under it as indented, icon-less rows. SETTINGS-NAVIGATION 2.0 section 2
''' (ticket 243, slice T-7b): a page that has items has TWO targets (rule 4) - the chevron zone
''' at the row's trailing edge only expands or folds the item list, the rest of the row only
''' selects the page - and both are reachable by assistive technology (the row's default
''' action selects, its chevron child is a second action). Clicking an item expands that
''' group's card in the right pane, scrolls it into view and focuses its first editor - the
''' rest of the page stays put (238 section 2.1). Which pages are expanded is the stored
''' context's <see cref="SettingsPageContext.NavExpanded"/>.
''' The rail is a tree for assistive technology (rule 7): role Outline, rows OutlineItem with
''' Selected / Expanded / Collapsed, ONE tab stop that roves with the focus, and Up / Down /
''' Home / End that move only the focus ring over the VISIBLE rows (<see cref="SettingsRailRules.NextIndex"/>);
''' Enter, Space and a click choose. Sizes are the ordinary pointer tokens (rule 5, <see cref="SettingsRailRules"/>):
''' rows of at least 32 logical px, a 16 px glyph, an 8 px glyph-to-caption gap, a rail as wide as
''' its longest caption needs and no more than 30 percent of the client.</summary>
Partial Public NotInheritable Class Share_Settings_Form
    Implements IThemeAware

    Private ReadOnly _pages As New Dictionary(Of String, Panel)(StringComparer.Ordinal)
    Private ReadOnly _navButtons As New Dictionary(Of String, Button)(StringComparer.Ordinal)
    ''' <summary>The subordinate rows under each page, in display order; their visibility IS
    ''' the page's expanded state in the menu.</summary>
    Private ReadOnly _navItems As New Dictionary(Of String, List(Of SettingsNavItemButton))(StringComparer.Ordinal)
    Private _pageHost As Panel
    Private _pageViewportHost As Panel
    Private _navigation As FlowLayoutPanel
    ''' <summary>The rail's column of the window grid; its width is <see cref="RailColumnPx"/>, not a number.</summary>
    Private _railColumn As ColumnStyle
    Private _arrangingNav As Boolean
    Private _selectedPage As String = String.Empty
    ''' <summary>The chosen sub-menu per page: the group whose card the last reveal brought
    ''' into view, or empty for the whole page. It owns the rail's highlight and nothing
    ''' else - the right pane always shows every card. Runtime view state, not persisted.</summary>
    Private ReadOnly _selectedItem As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Private Sub BuildSettingsPages()
        _root = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(12, 10, 12, 6), .Size = New Size(ClientSize.Width, ClientSize.Height)}
        _root.SuspendLayout()
        ' The rail column starts at the floor of the rules and is set from the real captions by
        ' ArrangeSettingsNavigation (rule 5): never a fixed pixel count.
        _railColumn = New ColumnStyle(SizeType.Absolute, 1)
        _root.ColumnStyles.Add(_railColumn)
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        _root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        ' The tree for assistive technology (rule 7): the container is an Outline, its rows OutlineItems.
        _navigation = New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0, 0, NavColumnGapLogical, 0),
            .AccessibleRole = AccessibleRole.Outline, .AccessibleName = DestinationCaption(), .Name = "managerSettingsRail"}
        _navigation.SuspendLayout()
        _pageHost = New Panel With {.Dock = DockStyle.Fill, .Margin = Padding.Empty}
        _pageHost.SuspendLayout()
        _pageViewportHost = New Panel With {.Dock = DockStyle.Fill}
        _pageHost.Controls.Add(_pageViewportHost)
        _root.Controls.Add(_navigation, 0, 0)
        _root.Controls.Add(_pageHost, 1, 0)
        For Each sec As CollapsibleSection In AllSettingsSections()
            sec.Width = Math.Max(1, ClientSize.Width - 300)
            BuildContractGroups(sec)
            Dim id = sec.Key
            _selectedItem(id) = String.Empty
            Dim button As New SettingsNavigationButton With {.Text = SettingsPageCaption(sec), .Name = "managerSettingsNav_" & id,
                .PageId = id, .AccessibleName = SettingsPageCaption(sec), .AccessibleRole = AccessibleRole.OutlineItem,
                .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 240,
                .Height = SettingsRailRules.RowTargetPx(RailFactor()),
                .Padding = New Padding(10, 0, 8, 0), .Margin = New Padding(0, 0, 0, 2),
                .TextAlign = ContentAlignment.MiddleLeft, .ImageAlign = ContentAlignment.MiddleLeft,
                .TextImageRelation = TextImageRelation.ImageBeforeText, .AutoEllipsis = False, .TabStop = False}
            AddHandler button.Disposed, Sub() button.Image?.Dispose()
            button.FlatAppearance.BorderSize = 0
            ' Two targets (rule 4): the row selects, its chevron zone alone expands or folds.
            AddHandler button.Click, Sub() OnSettingsPageClicked(id)
            AddHandler button.DisclosureToggled, Sub() ToggleSettingsNavExpanded(id)
            AddHandler button.KeyDown, AddressOf OnSettingsNavigationKeyDown
            AddHandler button.Paint, AddressOf PaintSettingsNavigation
            AddHandler button.GotFocus, Sub()
                                            NoteNavFocus(button)
                                            button.Invalidate()
                                        End Sub
            AddHandler button.LostFocus, Sub() button.Invalidate()
            _navButtons.Add(id, button)
            _navigation.Controls.Add(button)
            toolTip.SetToolTip(button, button.Text)
            ' The page's former groups as subordinate rows (210 section 4.2): plain 44 px
            ' captions, indented, muted until their page is the selected one. Hidden until
            ' the page's stored expansion says otherwise.
            Dim items As New List(Of SettingsNavItemButton)()
            For Each group In _pageGroups(id)
                Dim item As New SettingsNavItemButton With {.Text = group.Caption,
                    .Name = "managerSettingsNavItem_" & id & "_" & group.Id, .PageId = id, .Group = group,
                    .AccessibleName = group.Caption, .AccessibleRole = AccessibleRole.OutlineItem,
                    .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 220,
                    .Height = SettingsRailRules.RowTargetPx(RailFactor()),
                    .Margin = New Padding(0, 0, 0, 2), .TextAlign = ContentAlignment.MiddleLeft,
                    .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
                    .AutoEllipsis = False, .Visible = False, .TabStop = False}
                AddHandler item.Disposed, Sub() item.Image?.Dispose()
                item.FlatAppearance.BorderSize = 0
                AddHandler item.Click, Sub() RevealSettingsBlock(id, group, Nothing)
                AddHandler item.KeyDown, AddressOf OnSettingsNavigationKeyDown
                AddHandler item.Paint, AddressOf PaintSettingsNavigation
                AddHandler item.GotFocus, Sub()
                                              NoteNavFocus(item)
                                              item.Invalidate()
                                          End Sub
                AddHandler item.LostFocus, Sub() item.Invalidate()
                items.Add(item)
                _navigation.Controls.Add(item)
                toolTip.SetToolTip(item, item.Text)
            Next
            button.HasItems = items.Count > 0
            _navItems.Add(id, items)
            Dim viewport As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Visible = False,
                .Name = "managerSettingsPage_" & id, .AccessibleName = sec.Title}
            AddHandler viewport.SizeChanged, Sub() FitSettingsContent(viewport)
            viewport.SuspendLayout()
            Dim content As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Width = ClientSize.Width - 300,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1, .Margin = Padding.Empty,
                .Padding = New Padding(4, 0, 12, 0)}
            content.SuspendLayout()
            content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            Dim title As New Label With {.Text = SettingsPageCaption(sec), .AutoSize = True, .Dock = DockStyle.Fill,
                .Font = New Font(Font.FontFamily, Font.Size * 1.5F, FontStyle.Bold), .Margin = New Padding(0, 0, 0, 8)}
            If id = "exchange" OrElse id = "exchange-account" Then ExchangeMark.Apply(title, 24, AddressOf CompanionAssets.OpenBundledAsset)
            AddHandler title.Disposed, Sub() title.Font.Dispose()
            Dim description As New Label With {.AutoSize = True, .Dock = DockStyle.Fill,
                .Text = SettingsPageDescription(id), .ForeColor = ThemeService.Palette.TextMuted,
                .Margin = New Padding(0, 0, 0, 16)}
            AddRow(content, title)
            AddRow(content, description)
            sec.PresentAsPage()
            sec.Body.Dock = DockStyle.Fill
            AddRow(content, sec.Body)
            AddHandler content.Disposed, Sub() sec.Dispose()
            viewport.Controls.Add(content)
            _pages.Add(id, viewport)
            _pageViewportHost.Controls.Add(viewport)
            content.ResumeLayout(False)
            viewport.ResumeLayout(False)
        Next
        BuildSettingsHeader()
        AddHandler _navigation.SizeChanged, Sub() ArrangeSettingsNavigation()
        ' The rail is limited to a share of the client (rule 5), so a window resize re-measures it.
        AddHandler _root.SizeChanged, Sub() ArrangeSettingsNavigation()
        ' One tab stop for the whole rail, and it returns to the selected row when the focus leaves (rule 7).
        AddHandler _navigation.Leave, Sub() ResetNavTabStop()
        _pageHost.ResumeLayout(False)
        _navigation.ResumeLayout(True)
        _root.ResumeLayout(False)
        ArrangeSettingsNavigation()
        ApplySettingsNavigationTheme(ThemeService.Palette)
        RenderSettingsHeader(ThemeService.Palette)
    End Sub

    ' Existing localized task descriptions; no second preference editor or status probe.
    Private Shared Function SettingsPageDescription(id As String) As String
        Select Case id
            Case "startup" : Return Localization.T("Запуск менеджера, тема и язык интерфейса.")
            Case "network" : Return Localization.T("Подключения и доступ к общим папкам.")
            Case "hosting" : Return Localization.T("Режим работы и служба общего доступа.")
            Case "exchange" : Return Localization.T("Сервер обмена, порты и учётные записи.")
            Case "exchange-account" : Return Localization.T("Подключение этого ПК к серверу обмена.")
            Case "scheduled" : Return Localization.T("Копирование, перенос, удаление и архивация файлов по расписанию или вручную.")
            Case Else : Return Localization.T("Версия, лицензия, документация и помощь.")
        End Select
    End Function

    ' --- sizes (SETTINGS-NAVIGATION 2.0 section 2 rule 5; the glyph, the gap and the row are SettingsRailRules') ---
    ' The rail's own horizontal rhythm, logical px.
    ''' <summary>A row's own padding at its reading start.</summary>
    Friend Const NavPaddingStartLogical As Integer = 10
    ''' <summary>A row's own padding at its reading end.</summary>
    Friend Const NavPaddingEndLogical As Integer = 6
    ''' <summary>The one child indent token (rule 4: 8 or 12): a child's glyph column, and its caption, sit this far
    ''' in from the page rows'.</summary>
    Friend Const NavChildIndentLogical As Integer = 12
    ''' <summary>A little room the text renderer needs beyond the measured caption.</summary>
    Friend Const NavRenderSlackLogical As Integer = 2
    ''' <summary>What the list keeps between its rows and its scrollbar.</summary>
    Friend Const NavEdgeLogical As Integer = 8
    ''' <summary>The gap between the rail and the page.</summary>
    Friend Const NavColumnGapLogical As Integer = 12
    ''' <summary>The smallest window the user can drag to, logical px. There is no drawer or overlay rail in this window
    ''' (SETTINGS-NAVIGATION 2.0 rule 6 allows the rail to stay), so the narrowest window must still leave the page
    ''' column 480 logical px in every language: the figure is pinned by SettingsRailTests. Measured (ticket 243 T-7c): at
    ''' 640 the worst page column was 392.7 logical px (ru, Manager, x1.5), because the rail's floor outweighs its 30 percent
    ''' share of a client this small; 730 left 476; 740 leaves 483.</summary>
    Friend Const MinimumWidthLogical As Integer = 740
    Friend Const MinimumHeightLogical As Integer = 360

    ''' <summary>The interface factor of this window (1.0 = 96 dpi): the input of every token of <see cref="UiSizing"/>.</summary>
    Private Function RailFactor() As Single
        If _railFactorOverride > 0.0F Then Return _railFactorOverride
        Return Math.Max(0.5F, DeviceDpi / 96.0F)
    End Function

    ''' <summary>A factor other than the live monitor's, for the rail only (0 = use the monitor's). The tests set it to
    ''' prove the sizes at 100 and 150 percent on a machine that has one display scale; nothing in the product does.</summary>
    Private _railFactorOverride As Single

    Friend Sub SetRailFactor(factor As Single)
        _railFactorOverride = factor
        ArrangeSettingsNavigation()
        ApplySettingsNavigationTheme(ThemeService.Palette)
    End Sub

    ''' <summary>The single-line width of the longest of <paramref name="captions"/> in <paramref name="font"/>, device px.</summary>
    Friend Shared Function LongestCaptionPx(captions As IEnumerable(Of String), font As Font) As Integer
        Dim longest As Integer = 0
        For Each caption As String In captions
            If String.IsNullOrEmpty(caption) Then Continue For
            Dim measured As Integer = TextRenderer.MeasureText(caption, font, New Size(Integer.MaxValue, Integer.MaxValue),
                                                               TextFormatFlags.SingleLine).Width
            If measured > longest Then longest = measured
        Next
        Return longest
    End Function

    ''' <summary>The width of the rail COLUMN (the rail, its scrollbar room and the gap to the page), device px: the
    ''' <see cref="SettingsRailRules.RailWidth"/> of the longest page caption and of the longest item caption (an item's
    ''' caption starts one child indent further in), whichever needs more, never over 30 percent of
    ''' <paramref name="clientWidthPx"/>. The one computation the window and its tests share.</summary>
    Friend Shared Function RailColumnPx(pageCaptions As IEnumerable(Of String), itemCaptions As IEnumerable(Of String),
                                        anyPageHasItems As Boolean, font As Font, factor As Single,
                                        clientWidthPx As Integer, scrollBarPx As Integer) As Integer
        Dim glyph As Integer = SettingsRailRules.GlyphPx(factor)
        Dim gap As Integer = SettingsRailRules.GlyphGapPx(factor)
        Dim common As Integer = UiSizing.DevicePx(NavEdgeLogical + NavColumnGapLogical, factor) + Math.Max(0, scrollBarPx)
        Dim pageChrome As Integer = common + UiSizing.DevicePx(NavPaddingStartLogical + NavPaddingEndLogical + NavRenderSlackLogical, factor) +
            If(anyPageHasItems, SettingsRailRules.RowTargetPx(factor), 0)
        Dim itemChrome As Integer = common + UiSizing.DevicePx(NavPaddingStartLogical + NavChildIndentLogical + NavPaddingEndLogical + NavRenderSlackLogical, factor)
        Dim pageWidth As Integer = SettingsRailRules.RailWidth(LongestCaptionPx(pageCaptions, font), glyph, gap, pageChrome, clientWidthPx)
        Dim itemWidth As Integer = SettingsRailRules.RailWidth(LongestCaptionPx(itemCaptions, font), glyph, gap, itemChrome, clientWidthPx)
        Return Math.Max(pageWidth, itemWidth)
    End Function

    ''' <summary>Sizes the rail from the rules: the column from the real captions (rule 5), every row at least the
    ''' 32 logical px pointer target and taller only when its caption wraps (rule 2: wrap and grow, never clip, never an
    ''' ellipsis), the page rows' chevron zone and the children's indent at the reading start's side. Compact rows
    ''' (owner revision 2026-10-13) stay: the former 44 px minimum read twice too tall, and the 32 px target is the
    ''' ordinary pointer size of the shared contract - the smallest row is not 26 or 34 any more, it is the token.</summary>
    Private Sub ArrangeSettingsNavigation()
        If _arrangingNav OrElse _railColumn Is Nothing OrElse _navButtons.Count = 0 Then Return
        _arrangingNav = True
        _navigation.SuspendLayout()
        Try
            Dim factor As Single = RailFactor()
            Dim rtl = RightToLeft = RightToLeft.Yes
            Dim scrollBar As Integer = SystemInformation.VerticalScrollBarWidth
            Dim glyph As Integer = SettingsRailRules.GlyphPx(factor)
            Dim gap As Integer = SettingsRailRules.GlyphGapPx(factor)
            Dim slack As Integer = UiSizing.DevicePx(NavRenderSlackLogical, factor)
            Dim anyItems As Boolean = _navItems.Values.Any(Function(list) list.Count > 0)
            Dim column As Integer = RailColumnPx(_navButtons.Values.Select(Function(b) b.Text),
                                                 _navItems.Values.SelectMany(Function(list) list).Select(Function(i) i.Text),
                                                 anyItems, _navButtons.Values.First().Font, factor, ClientSize.Width, scrollBar)
            If CInt(_railColumn.Width) <> column Then _railColumn.Width = column
            Dim rowWidth As Integer = Math.Max(1, column - UiSizing.DevicePx(NavEdgeLogical + NavColumnGapLogical, factor) - scrollBar)
            Dim start As Integer = UiSizing.DevicePx(NavPaddingStartLogical, factor)
            Dim finish As Integer = UiSizing.DevicePx(NavPaddingEndLogical, factor)
            For Each pair In _navButtons
                Dim row = DirectCast(pair.Value, SettingsNavigationButton)
                ' A page with items keeps the trailing chevron zone (its own, 32 px target) clear of the caption.
                row.DisclosureWidth = If(row.HasItems, SettingsRailRules.RowTargetPx(factor), 0)
                Dim trailing As Integer = finish + row.DisclosureWidth
                row.Padding = If(rtl, New Padding(trailing, 0, start, 0), New Padding(start, 0, trailing, 0))
                row.Width = rowWidth
                Dim room As Integer = Math.Max(1, rowWidth - row.Padding.Horizontal - glyph - gap - slack)
                row.Height = SettingsRailRules.RowHeightPx(factor, TextRenderer.MeasureText(row.Text, row.Font,
                    New Size(room, Integer.MaxValue), TextFormatFlags.WordBreak).Height)
            Next
            ' The subordinate indent: one shared token (rule 4), mirrored in RTL (210 section 4.2). The caption starts
            ' where an icon-bearing child's caption would, so a glyph can be added without moving the text.
            Dim childStart As Integer = UiSizing.DevicePx(NavPaddingStartLogical + NavChildIndentLogical, factor)
            For Each items In _navItems.Values
                For Each item In items
                    ' An item with a catalogued glyph draws it in the column (glyph + gap) that an item without one leaves empty.
                    Dim hasGlyph As Boolean = SettingsItemGlyph(item.PageId, item.Group.Id).Length > 0
                    Dim indent As Integer = If(hasGlyph, childStart, childStart + glyph + gap)
                    item.Padding = If(rtl, New Padding(finish, 0, indent, 0), New Padding(indent, 0, finish, 0))
                    item.Width = rowWidth
                    Dim room As Integer = Math.Max(1, rowWidth - item.Padding.Horizontal - slack - If(hasGlyph, glyph + gap, 0))
                    item.Height = SettingsRailRules.RowHeightPx(factor, TextRenderer.MeasureText(item.Text, item.Font,
                        New Size(room, Integer.MaxValue), TextFormatFlags.WordBreak).Height)
                Next
            Next
        Finally
            _navigation.ResumeLayout(True)
            _arrangingNav = False
        End Try
    End Sub

    ' --- the roving tab stop (rule 7) ---------------------------------------------------------------

    ''' <summary>Every row of the tree, visible or not, in display order.</summary>
    Private Iterator Function AllNavRows() As IEnumerable(Of Button)
        For Each id As String In _pages.Keys
            Yield _navButtons(id)
            Dim items As List(Of SettingsNavItemButton) = Nothing
            If _navItems.TryGetValue(id, items) Then
                For Each item In items
                    Yield item
                Next
            End If
        Next
    End Function

    ''' <summary>The row that carries the selection: the chosen item while its page is open and its list is shown, the
    ''' page row otherwise.</summary>
    Private Function SelectedNavRow() As Button
        If _selectedPage.Length = 0 OrElse Not _navButtons.ContainsKey(_selectedPage) Then Return Nothing
        Dim chosen = SelectedItemId(_selectedPage)
        If chosen.Length > 0 AndAlso NavExpanded(_selectedPage) Then
            Dim items As List(Of SettingsNavItemButton) = Nothing
            If _navItems.TryGetValue(_selectedPage, items) Then
                For Each item In items
                    If item.Group.Id = chosen Then Return item
                Next
            End If
        End If
        Return _navButtons(_selectedPage)
    End Function

    ''' <summary>Exactly one row is a tab stop. While the focus is inside the rail it is the row that has the focus (so Tab
    ''' and Shift+Tab leave from where the arrow keys went); at rest it is the selected row.</summary>
    Private Sub SetNavTabStop(target As Button)
        For Each row In AllNavRows()
            Dim wanted = (row Is target)
            If row.TabStop <> wanted Then row.TabStop = wanted
        Next
    End Sub

    Private Sub ResetNavTabStop()
        If _navButtons.Count = 0 Then Return
        Dim selected = SelectedNavRow()
        SetNavTabStop(If(selected, _navButtons.Values.First()))
    End Sub

    ''' <summary>A row took the focus (mouse or keyboard): it becomes the one tab stop.</summary>
    Private Sub NoteNavFocus(row As Button)
        SetNavTabStop(row)
    End Sub

    ''' <summary>How many rows are a tab stop right now (one, as soon as the rail has a row).</summary>
    Friend Function NavTabStopCount() As Integer
        Return AllNavRows().Count(Function(row) row.TabStop)
    End Function

    ''' <summary>The one row that is the rail's tab stop right now.</summary>
    Friend Function NavTabStopRow() As Button
        Return AllNavRows().FirstOrDefault(Function(row) row.TabStop)
    End Function

    Friend Sub SelectPage(id As String)
        SelectSettingsPage(id)
    End Sub

    Friend Sub SelectSettingsPage(id As String)
        If Not _pages.ContainsKey(id) Then Return
        If id = _selectedPage Then
            managerSettingsContext.LastPageId = id
            Return
        End If
        If settingsWasPresented Then CaptureSettingsViewport()
        Dim viewport = _pages(id)
        Dim subtree = Descendants(viewport).Reverse().ToArray()
        For Each control In subtree
            control.SuspendLayout()
        Next
        viewport.SuspendLayout()
        _pageHost.SuspendLayout()
        Try
            If _selectedPage.Length > 0 Then
                If _pages(_selectedPage).ContainsFocus Then _navButtons(id).Focus()
                _pages(_selectedPage).Visible = False
            End If
            _selectedPage = id
            managerSettingsContext.LastPageId = id
            _pages(id).Visible = True
            _pages(id).BringToFront()
            If settingsWasPresented Then LoadSelectedSettingsPage()
        Finally
            For Each control In subtree.Reverse()
                control.ResumeLayout(False)
            Next
            viewport.ResumeLayout(True)
            _pageHost.ResumeLayout(True)
        End Try
        RestoreSettingsViewport()
        ApplySettingsNavigationTheme(ThemeService.Palette)
    End Sub

    Private Function SelectedItemId(pageId As String) As String
        Dim value As String = Nothing
        Return If(_selectedItem.TryGetValue(pageId, value), value, String.Empty)
    End Function

    ''' <summary>The SELECT target of a page row (SETTINGS-NAVIGATION 2.0 section 2 rule 4, ticket 243 T-7b). Choosing
    ''' the row selects the page and never expands or folds its items - that is the chevron's own target
    ''' (<see cref="ToggleSettingsNavExpanded"/>), so the two never share a gesture. The page row always means the
    ''' whole page - every card of it in its remembered fold state (238 section 2.1) - so it also gives the
    ''' highlight back from a chosen sub-menu item.</summary>
    Private Sub OnSettingsPageClicked(id As String)
        If SelectedItemId(id).Length > 0 Then
            _selectedItem(id) = String.Empty
            ApplySettingsNavigationTheme(ThemeService.Palette)
        End If
        SelectSettingsPage(id)
    End Sub

    ''' <summary>The EXPAND target of a page row: the chevron zone, its accessible child and Left / Right. Folds or
    ''' unfolds the item list and nothing else; the selected page does not change.</summary>
    Private Sub ToggleSettingsNavExpanded(id As String)
        SetSettingsNavExpanded(id, Not NavExpanded(id))
    End Sub

    Private Sub SetSettingsNavExpanded(id As String, expanded As Boolean)
        Dim items As List(Of SettingsNavItemButton) = Nothing
        If Not _navItems.TryGetValue(id, items) OrElse items.Count = 0 Then Return
        ' A focused item that is about to be hidden hands the focus to its page row (the tree never loses its focus).
        Dim refocus As Boolean = Not expanded AndAlso items.Any(Function(item) item.Focused)
        managerSettingsContext.Page(id).NavExpanded = expanded
        ' Disclosure changes sibling visibility as one layout transaction. Laying out after each
        ' item repeatedly refits the exchange page, increasingly costly as host actions grow.
        _navigation.SuspendLayout()
        Try
            For Each item In items
                item.Visible = expanded
            Next
        Finally
            _navigation.ResumeLayout(True)
        End Try
        Dim button = DirectCast(_navButtons(id), SettingsNavigationButton)
        Dim changed As Boolean = button.Expanded <> expanded
        button.Expanded = expanded
        button.Invalidate()
        ' Assistive technology hears the disclosure state change (rule 4: Expanded / Collapsed on the parent row).
        If changed Then button.AnnounceStateChange()
        If refocus Then button.Focus()
        ' A tab stop that sits on a row that just went away moves to its page row, focused or not.
        If Not expanded AndAlso items.Any(Function(item) item.TabStop) Then SetNavTabStop(button)
        ' The selected row may be a child that is now hidden (the parent stands in), or a child that just showed.
        If Not _navigation.ContainsFocus Then ResetNavTabStop()
    End Sub

    Friend Sub ApplySettingsNavExpansion()
        For Each id As String In _navItems.Keys
            SetSettingsNavExpanded(id, managerSettingsContext.Page(id).NavExpanded)
        Next
    End Sub

    Private Function NavExpanded(id As String) As Boolean
        Dim page As SettingsPageContext = Nothing
        Return managerSettingsContext.Pages.TryGetValue(id, page) AndAlso page.NavExpanded
    End Function

    ''' <summary>The flattened tree in display order: each page row, then its items while
    ''' they are revealed (210 section 4.5). Expansion flags, not control visibility, so
    ''' the walk is correct before the window is shown as well.</summary>
    Private Iterator Function SettingsNavRows() As IEnumerable(Of Control)
        For Each id As String In _pages.Keys
            Yield _navButtons(id)
            Dim items As List(Of SettingsNavItemButton) = Nothing
            If _navItems.TryGetValue(id, items) AndAlso NavExpanded(id) Then
                For Each item In items
                    Yield item
                Next
            End If
        Next
    End Function

    ''' <summary>The one reveal path (210 sections 4.4 and 5.4, recarded by 238 section 2.1):
    ''' a subordinate item - or a search hit - expands that group's card, scrolls its header
    ''' to the top of the viewport and focuses the block's first editor. The rest of the page
    ''' keeps its place: sibling cards stay visible and independently collapsible.</summary>
    Private Sub RevealSettingsBlock(pageId As String, group As SettingsNavGroup, editor As Control)
        SelectSettingsPage(pageId)
        ' Reveal the chosen child's parent (rule 4): a search hit or a restored context can name an item whose page
        ' row is folded, and a selected row nobody can see is no selection cue at all.
        If Not NavExpanded(pageId) Then SetSettingsNavExpanded(pageId, True)
        Dim viewport = _pages(pageId)
        _selectedItem(pageId) = group.Id
        SetGroupExpanded(pageId, group, True)
        viewport.PerformLayout()
        If editor Is Nothing Then editor = FirstSettingsBlockEditor(group)
        ' The whole card is the target, so its header lands just under the viewport's top.
        viewport.AutoScrollPosition = New Point(0, Math.Max(0, ViewportTop(group.Card.Header) - LogicalToDeviceUnits(8)))
        If editor IsNot Nothing Then editor.Focus()
        ApplySettingsNavigationTheme(ThemeService.Palette)
    End Sub

    Private Sub LoadSelectedSettingsPage()
        Select Case _selectedPage
            Case "hosting" : EnsureHostingLoaded()
            Case "exchange" : EnsureExchangeLoaded()
            Case "exchange-account" : ApplyAccountState()
        End Select
    End Sub

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Private Sub ApplySettingsTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        ApplySettingsNavigationTheme(palette)
        RenderSettingsHeader(palette)
        ApplySettingsContrast(palette)
    End Sub

    Private Sub ApplySettingsNavigationTheme(palette As ThemePalette)
        Dim fill = Color.FromArgb(CInt(palette.SurfaceWindow.R * 0.78 + palette.Accent.R * 0.22),
            CInt(palette.SurfaceWindow.G * 0.78 + palette.Accent.G * 0.22),
            CInt(palette.SurfaceWindow.B * 0.78 + palette.Accent.B * 0.22))
        For Each pair In _navButtons
            Dim button = pair.Value
            ' The page row carries the selection only while no sub-menu of it is the view;
            ' a chosen item owns the tint and the stripe instead (owner revision 2026-10-13).
            Dim selected = pair.Key = _selectedPage AndAlso SelectedItemId(pair.Key).Length = 0
            DirectCast(button, SettingsNavRowButton).SetSelected(selected)
            StyleSettingsNavRow(button, selected, False, fill, palette)
            Dim oldImage = button.Image
            button.Image = RenderNavGlyph(SettingsPageGlyph(pair.Key), SettingsNavIconPalette.ResolveInk(button.Name, palette.IsDark,
                    {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor},
                    button.ForeColor, SystemInformation.HighContrast))
            oldImage?.Dispose()
            button.Invalidate()
        Next
        ' The item rows: exactly one row - the chosen sub-menu - reads selected, the rest
        ' sit on the plain surface; items of pages other than the shown one read muted.
        For Each pair In _navItems
            For Each item In pair.Value
                Dim selected = pair.Key = _selectedPage AndAlso SelectedItemId(pair.Key) = item.Group.Id
                item.SetSelected(selected)
                StyleSettingsNavRow(item, selected, pair.Key <> _selectedPage, fill, palette)
                Dim glyphId As String = SettingsItemGlyph(pair.Key, item.Group.Id)
                If glyphId.Length > 0 Then
                    ' One glyph treatment for the whole surface (rule 3): an item wears its page's identity ink, never a second one.
                    Dim oldImage = item.Image
                    item.Image = RenderNavGlyph(glyphId, SettingsNavIconPalette.ResolveInk("managerSettingsNav_" & pair.Key, palette.IsDark,
                            {item.BackColor, item.FlatAppearance.MouseOverBackColor, item.FlatAppearance.MouseDownBackColor},
                            item.ForeColor, SystemInformation.HighContrast))
                    oldImage?.Dispose()
                End If
            Next
        Next
        For Each pair In _pageGroups
            For Each grp In pair.Value
                If grp.Card IsNot Nothing Then grp.Card.ApplyPalette(palette)
            Next
        Next
        ' The selection may have moved to another row: the one tab stop follows it while the focus is elsewhere.
        If Not _navigation.ContainsFocus Then ResetNavTabStop()
    End Sub

    ''' <summary>The vocabulary glyph at the rail's glyph size (16 logical, <see cref="SettingsRailRules.GlyphPx"/>), padded
    ''' on the side that faces the caption (the right in left-to-right, the left in right-to-left, where the button puts
    ''' the picture at the right edge) by the 8 logical px glyph-to-caption gap, so the gap is the rail's own and not the
    ''' button layout's. Re-rendered whenever the ink or the direction changes.</summary>
    Private Function RenderNavGlyph(glyphId As String, ink As Color) As Bitmap
        Dim factor As Single = RailFactor()
        Dim tier As Integer = SettingsRailRules.GlyphPx(factor)
        Dim gap As Integer = SettingsRailRules.GlyphGapPx(factor)
        Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
        Dim padded As New Bitmap(tier + gap, tier)
        Try
            padded.SetResolution(96.0F, 96.0F)
            Using glyph As Bitmap = GlyphRenderer.Render(glyphId, tier, ink, AddressOf CompanionAssets.OpenBundledAsset)
                Using g As Graphics = Graphics.FromImage(padded)
                    g.DrawImageUnscaled(glyph, If(rtl, gap, 0), 0)
                End Using
            End Using
        Catch
            padded.Dispose()
            Throw
        End Try
        Return padded
    End Function

    ''' <summary>The shared row treatment: selection tint (or high-contrast highlight),
    ''' hover and press fills, readable ink - muted when the row belongs to another page.</summary>
    Private Sub StyleSettingsNavRow(button As Button, selected As Boolean, muted As Boolean,
                                    fill As Color, palette As ThemePalette)
        button.BackColor = If(selected, If(SystemInformation.HighContrast, SystemColors.Highlight, fill), palette.SurfaceWindow)
        button.ForeColor = If(selected AndAlso SystemInformation.HighContrast, SystemColors.HighlightText,
                        If(muted, palette.TextMuted, palette.TextPrimary))
        button.FlatAppearance.MouseOverBackColor = NavigationFill(palette, If(selected, 0.3F, 0.18F))
        button.FlatAppearance.MouseDownBackColor = NavigationFill(palette, If(selected, 0.4F, 0.28F))
        If SystemInformation.HighContrast Then
            button.FlatAppearance.MouseOverBackColor = button.BackColor
            button.FlatAppearance.MouseDownBackColor = button.BackColor
        End If
        For Each surface In {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor}
            button.ForeColor = ThemePalette.ReadableText(button.ForeColor, surface)
        Next
        button.Invalidate()
    End Sub

    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        ArrangeSettingsNavigation()
        FitSettingsContent()
        ApplySettingsNavigationTheme(ThemeService.Palette)
        RenderSettingsHeader(ThemeService.Palette)
    End Sub

    Private Shared Function SettingsPageGlyph(id As String) As String
        Return IconMap.GetMeaning("managerSettingsNav_" & id).RenderGlyphId
    End Function

    ''' <summary>The vocabulary glyph of one item row (SETTINGS-NAVIGATION 2.0 section 2 rules 2 and 4): the glyph of its
    ''' <c>managerSettingsNavItem_page_group</c> meaning, or an empty string where the vocabulary has no such meaning yet.
    ''' No glyph is ever invented here - an item without one keeps its caption aligned with its siblings' and is listed by
    ''' <see cref="ItemsWithoutMeaning"/> as a missing meaning for the catalog's proposal procedure.</summary>
    Friend Shared Function SettingsItemGlyph(pageId As String, groupId As String) As String
        Dim meaning As IconMap.IconMeaning = IconMap.GetMeaning("managerSettingsNavItem_" & pageId & "_" & groupId)
        Return If(meaning Is Nothing OrElse Not meaning.IsVocabulary, String.Empty, meaning.RenderGlyphId)
    End Function

    ''' <summary>The items of this window that have no catalogued glyph, as <c>page/group</c> ids in display order.</summary>
    Friend Function ItemsWithoutMeaning() As String()
        Return _navItems.SelectMany(Function(pair) pair.Value.Where(Function(item) SettingsItemGlyph(pair.Key, item.Group.Id).Length = 0).
                                        Select(Function(item) pair.Key & "/" & item.Group.Id)).ToArray()
    End Function

    ''' <summary>The language switch that changes the reading direction at run time moves the rows' paddings, the chevron
    ''' zone, the stripe and the padded side of every glyph.</summary>
    Protected Overrides Sub OnRightToLeftChanged(e As EventArgs)
        MyBase.OnRightToLeftChanged(e)
        If _navButtons.Count = 0 Then Return
        ArrangeSettingsNavigation()
        ApplySettingsNavigationTheme(ThemeService.Palette)
    End Sub

    Private Sub PaintSettingsNavigation(sender As Object, e As PaintEventArgs)
        Dim button = DirectCast(sender, SettingsNavRowButton)
        Dim rtl = RightToLeft = RightToLeft.Yes
        ' The explicit non-colour selection cue (rule 7): the leading stripe, on the reading start's side.
        If button.Selected Then
            Dim width = LogicalToDeviceUnits(3)
            Using brush As New SolidBrush(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent))
                e.Graphics.FillRectangle(brush, If(rtl, button.Width - width, 0), 0, width, button.Height)
            End Using
        End If
        Dim pageButton = TryCast(sender, SettingsNavigationButton)
        If pageButton IsNot Nothing AndAlso pageButton.HasItems Then PaintSettingsChevron(pageButton, e.Graphics)
        If button.Focused Then
            Dim inset = LogicalToDeviceUnits(3)
            Using pen As New Pen(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent), LogicalToDeviceUnits(2))
                e.Graphics.DrawRectangle(pen, inset, inset, Math.Max(1, button.Width - 2 * inset - 1), Math.Max(1, button.Height - 2 * inset - 1))
            End Using
        End If
    End Sub

    ''' <summary>The disclosure chevron in its own target zone at the page row's trailing edge - the same
    ''' collapsibleSection / collapsibleSectionOpen pair the former group headers drew (210 section 4.1). The zone is a
    ''' target of its own (rule 4): it takes a hover fill of its own, and a click on it never selects the page.</summary>
    Private Sub PaintSettingsChevron(button As SettingsNavigationButton, g As Graphics)
        Dim zone As Rectangle = button.DisclosureRect()
        If zone.Width <= 0 Then Return
        If button.DisclosureHot Then
            If SystemInformation.HighContrast Then
                Using pen As New Pen(button.ForeColor)
                    g.DrawRectangle(pen, zone.Left, zone.Top, zone.Width - 1, zone.Height - 1)
                End Using
            Else
                Using brush As New SolidBrush(NavigationFill(ThemeService.Palette, 0.4F))
                    g.FillRectangle(brush, zone)
                End Using
            End If
        End If
        Dim size = UiSizing.DevicePx(UiSizing.MarkerTier, RailFactor())
        Dim x = zone.Left + Math.Max(0, (zone.Width - size) \ 2)
        Dim y = Math.Max(0, (button.Height - size) \ 2)
        Dim meaning = IconMap.GetMeaning(If(button.Expanded, "collapsibleSectionOpen", "collapsibleSection"))
        Dim ink = If(SystemInformation.HighContrast, button.ForeColor,
            SettingsNavIconPalette.ResolveInk("settingsNavGeneral", ThemeService.Palette.IsDark,
                {button.BackColor, ThemeService.Palette.ControlHover}, ThemeService.Palette.TextMuted, False))
        Using glyph As Bitmap = GlyphRenderer.Render(meaning.GlyphId, size, ink, AddressOf CompanionAssets.OpenBundledAsset)
            g.DrawImage(glyph, x, y, size, size)
        End Using
    End Sub

    ''' <summary>The page a flattened-tree row belongs to.</summary>
    Private Shared Function NavRowPageId(row As Control) As String
        Return DirectCast(row, SettingsNavRowButton).PageId
    End Function

    ''' <summary>The tree's keys (rule 7, ticket 243 T-7b). Up, Down, Home and End move only the FOCUS ring over the
    ''' VISIBLE rows (<see cref="SettingsRailRules.NextIndex"/>, no wrap): a page switch is never run by an arrow key.
    ''' Enter and Space choose the focused row through its click path. Left and Right fold and unfold the item list of
    ''' the focused row's page, mirrored in RTL (210 section 4.5); folding from an item hands the focus to its page.</summary>
    Private Sub OnSettingsNavigationKeyDown(sender As Object, e As KeyEventArgs)
        If e.Modifiers <> Keys.None Then Return
        Dim row = TryCast(sender, SettingsNavRowButton)
        If row Is Nothing Then Return
        Dim rtl = RightToLeft = RightToLeft.Yes
        Select Case e.KeyCode
            Case Keys.Up, Keys.Down, Keys.Home, Keys.End
                e.Handled = True
                e.SuppressKeyPress = True
                MoveNavFocus(row, e.KeyCode)
            Case Keys.Return, Keys.Space
                e.Handled = True
                e.SuppressKeyPress = True
                row.PerformClick()
            Case Keys.Left, Keys.Right
                e.Handled = True
                e.SuppressKeyPress = True
                Dim expand = If(e.KeyCode = Keys.Right, Not rtl, rtl)
                Dim pageId = NavRowPageId(row)
                If _navItems.ContainsKey(pageId) AndAlso _navItems(pageId).Count > 0 Then SetSettingsNavExpanded(pageId, expand)
        End Select
    End Sub

    ''' <summary>Moves the focus ring from <paramref name="from"/> to the row <see cref="SettingsRailRules.NextIndex"/>
    ''' names among the visible rows.</summary>
    Private Sub MoveNavFocus(from As Control, key As Keys)
        Dim railKey As RailKey
        Select Case key
            Case Keys.Up : railKey = RailKey.Up
            Case Keys.Down : railKey = RailKey.Down
            Case Keys.Home : railKey = RailKey.Home
            Case Keys.End : railKey = RailKey.End
            Case Else : Return
        End Select
        Dim rows = SettingsNavRows().OfType(Of Button)().ToArray()
        Dim current = Array.IndexOf(rows, from)
        Dim target = SettingsRailRules.NextIndex(current, rows.Length, railKey)
        If target < 0 OrElse target = current Then Return
        SetNavTabStop(rows(target))
        rows(target).Focus()
        _navigation.ScrollControlIntoView(rows(target))
    End Sub

    ''' <summary>The tree row's face for assistive tech (rule 7): role OutlineItem, Selectable and Focusable, Selected on
    ''' the chosen row, Expanded / Collapsed on a page that has items. The default action SELECTS; a page with items has
    ''' ONE child, its chevron (<see cref="NavDisclosureAccessibleObject"/>), whose own default action expands or folds
    ''' (rule 4: distinct targets and distinct accessible actions).</summary>
    Private NotInheritable Class NavRowAccessibleObject
        Inherits Control.ControlAccessibleObject

        Private ReadOnly row As SettingsNavRowButton
        Private disclosure As NavDisclosureAccessibleObject

        Public Sub New(owner As SettingsNavRowButton)
            MyBase.New(owner)
            row = owner
        End Sub

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.OutlineItem
            End Get
        End Property

        Public Overrides ReadOnly Property DefaultAction As String
            Get
                Return Localization.T("Выбрать")
            End Get
        End Property

        Public Overrides Sub DoDefaultAction()
            row.PerformClick()
        End Sub

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Dim value = MyBase.State Or AccessibleStates.Selectable Or AccessibleStates.Focusable
                If row.Selected Then value = value Or AccessibleStates.Selected
                ' An expandable OutlineItem (210 section 4.6): the disclosure state travels with the row.
                If row.HasItems Then
                    value = value Or If(row.Expanded, AccessibleStates.Expanded, AccessibleStates.Collapsed)
                End If
                Return value
            End Get
        End Property

        Public Overrides Function GetChildCount() As Integer
            Return If(row.HasItems AndAlso TypeOf row Is SettingsNavigationButton, 1, 0)
        End Function

        Public Overrides Function GetChild(index As Integer) As AccessibleObject
            Dim page = TryCast(row, SettingsNavigationButton)
            If index <> 0 OrElse page Is Nothing OrElse Not page.HasItems Then Return Nothing
            If disclosure Is Nothing Then disclosure = New NavDisclosureAccessibleObject(page)
            Return disclosure
        End Function
    End Class

    ''' <summary>The chevron of a page row as an accessible element of its own: a PushButton whose name is the action
    ''' it performs ("Expand" / "Collapse") and whose default action does it. The row's pointer zone and this object are
    ''' the same target.</summary>
    Private NotInheritable Class NavDisclosureAccessibleObject
        Inherits AccessibleObject

        Private ReadOnly row As SettingsNavigationButton

        Friend Sub New(owner As SettingsNavigationButton)
            row = owner
        End Sub

        Public Overrides ReadOnly Property Parent As AccessibleObject
            Get
                Return row.AccessibilityObject
            End Get
        End Property

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.PushButton
            End Get
        End Property

        Public Overrides Property Name As String
            Get
                Return Localization.T(If(row.Expanded, "Свернуть", "Развернуть"))
            End Get
            Set(value As String)
            End Set
        End Property

        Public Overrides ReadOnly Property DefaultAction As String
            Get
                Return Name
            End Get
        End Property

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Return AccessibleStates.None
            End Get
        End Property

        Public Overrides ReadOnly Property Bounds As Rectangle
            Get
                Return row.RectangleToScreen(row.DisclosureRect())
            End Get
        End Property

        Public Overrides Sub DoDefaultAction()
            row.ToggleDisclosure()
        End Sub
    End Class

    ''' <summary>A rail row: the keys of a vertical tree belong to the row (the dialog would otherwise walk the focus
    ''' sideways and wrap), Space was answered on key down so the button's own key-up click does not choose twice, and
    ''' selection state changes are announced.</summary>
    Friend MustInherit Class SettingsNavRowButton
        Inherits Button

        Friend PageId As String
        Friend Selected As Boolean
        Friend HasItems As Boolean
        Friend Expanded As Boolean

        ''' <summary>Tells assistive technology that this row's state (Expanded / Collapsed) changed.</summary>
        Friend Sub AnnounceStateChange()
            If IsHandleCreated Then AccessibilityNotifyClients(AccessibleEvents.StateChange, -1)
        End Sub

        ''' <summary>Marks the row selected or not and tells assistive technology when it really changed.</summary>
        Friend Sub SetSelected(value As Boolean)
            If Selected = value Then Return
            Selected = value
            Invalidate()
            If IsHandleCreated Then
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1)
                If value Then AccessibilityNotifyClients(AccessibleEvents.SelectionAdd, -1)
            End If
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            If (keyData And Keys.Modifiers) = Keys.None Then
                Select Case keyData And Keys.KeyCode
                    Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.Return : Return True
                End Select
            End If
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
            If e.KeyCode = Keys.Space AndAlso e.Modifiers = Keys.None Then
                e.Handled = True
                Return
            End If
            MyBase.OnKeyUp(e)
        End Sub

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New NavRowAccessibleObject(Me)
        End Function
    End Class

    ''' <summary>A page row. When the page has items the row has TWO targets (rule 4): the chevron zone at its
    ''' trailing edge raises <see cref="DisclosureToggled"/> and nothing else, every other point raises the ordinary
    ''' <c>Click</c> that selects the page. Mouse down in the zone marks the press; the click that follows is turned into
    ''' the toggle and never reaches the row's own handler. A keyboard or accessibility click has no such press.</summary>
    Private NotInheritable Class SettingsNavigationButton
        Inherits SettingsNavRowButton

        Friend Event DisclosureToggled As EventHandler
        ''' <summary>Width of the chevron zone, device px; 0 for a row without items.</summary>
        Friend DisclosureWidth As Integer
        Private pressedOnDisclosure As Boolean
        Private hotDisclosure As Boolean

        Friend ReadOnly Property DisclosureHot As Boolean
            Get
                Return hotDisclosure
            End Get
        End Property

        ''' <summary>The chevron zone in the row's client coordinates: the reading END of the row (the right in
        ''' left-to-right, the left in right-to-left).</summary>
        Friend Function DisclosureRect() As Rectangle
            If Not HasItems OrElse DisclosureWidth <= 0 Then Return Rectangle.Empty
            Return If(RightToLeft = RightToLeft.Yes, New Rectangle(0, 0, DisclosureWidth, Height),
                      New Rectangle(Width - DisclosureWidth, 0, DisclosureWidth, Height))
        End Function

        ''' <summary>Raises the toggle (the pointer zone and the accessible child both end here).</summary>
        Friend Sub ToggleDisclosure()
            pressedOnDisclosure = False
            RaiseEvent DisclosureToggled(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseDown(mevent As MouseEventArgs)
            pressedOnDisclosure = mevent.Button = MouseButtons.Left AndAlso DisclosureRect().Contains(mevent.Location)
            MyBase.OnMouseDown(mevent)
        End Sub

        Protected Overrides Sub OnMouseUp(mevent As MouseEventArgs)
            MyBase.OnMouseUp(mevent)
            ' The click of this press, whichever side of mouse-up it is raised on, is delivered inside the same message.
            ' A press that never became a click (released elsewhere) must not turn a later keyboard or accessibility
            ' click into a toggle, so the mark is dropped as soon as the message is done.
            If IsHandleCreated Then BeginInvoke(New Action(Sub() pressedOnDisclosure = False))
        End Sub

        Protected Overrides Sub OnMouseMove(mevent As MouseEventArgs)
            MyBase.OnMouseMove(mevent)
            Dim hot = DisclosureRect().Contains(mevent.Location)
            If hot <> hotDisclosure Then
                hotDisclosure = hot
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            If hotDisclosure Then
                hotDisclosure = False
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnClick(e As EventArgs)
            If pressedOnDisclosure Then
                pressedOnDisclosure = False
                RaiseEvent DisclosureToggled(Me, EventArgs.Empty)
                Return
            End If
            MyBase.OnClick(e)
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            pressedOnDisclosure = False
            MyBase.OnKeyDown(e)
        End Sub
    End Class

    ''' <summary>The active settings section id for in-app context help (CONTEXT-HELP rule 3).</summary>
    Public ReadOnly Property ActiveHelpPageId As String
        Get
            Return If(String.IsNullOrEmpty(_selectedPage), "startup", _selectedPage)
        End Get
    End Property

    ''' <summary>A subordinate row: a plain caption under its page, no icon and no chevron, an OutlineItem of its page
    ''' for assistive tech (210 section 4.6).</summary>
    Friend NotInheritable Class SettingsNavItemButton
        Inherits SettingsNavRowButton

        Friend Group As SettingsNavGroup
    End Class

End Class
