Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>The left menu as a tree (210): each page row keeps its icon, caption, selection
''' tint, accent stripe and focus rectangle and gains a display-only disclosure chevron; the
''' page's former fold groups sit under it as indented, icon-less rows. Clicking a page
''' selects it and toggles its item list; clicking an item expands that group's card in the
''' right pane, scrolls it into view and focuses its first editor - the rest of the page
''' stays put, so sibling cards remain visible and collapsible without navigating away
''' (238 section 2.1). Which pages are expanded is the stored context's
''' <see cref="SettingsPageContext.NavExpanded"/>.</summary>
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
    Private _selectedPage As String = String.Empty
    ''' <summary>The chosen sub-menu per page: the group whose card the last reveal brought
    ''' into view, or empty for the whole page. It owns the rail's highlight and nothing
    ''' else - the right pane always shows every card. Runtime view state, not persisted.</summary>
    Private ReadOnly _selectedItem As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Private Sub BuildSettingsPages()
        _root = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(12, 10, 12, 6), .Size = New Size(ClientSize.Width, ClientSize.Height)}
        _root.SuspendLayout()
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 255))
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        _root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        _navigation = New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0, 0, 12, 0)}
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
                .PageId = id, .AccessibleName = SettingsPageCaption(sec), .AccessibleRole = AccessibleRole.PageTab,
                .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 240, .Height = 34,
                .Padding = New Padding(10, 4, 8, 4), .Margin = New Padding(0, 0, 0, 2),
                .TextAlign = ContentAlignment.MiddleLeft, .ImageAlign = ContentAlignment.MiddleLeft,
                .TextImageRelation = TextImageRelation.ImageBeforeText, .AutoEllipsis = False}
            AddHandler button.Disposed, Sub() button.Image?.Dispose()
            button.FlatAppearance.BorderSize = 0
            AddHandler button.Click, Sub() OnSettingsPageClicked(id)
            AddHandler button.KeyDown, AddressOf OnSettingsNavigationKeyDown
            AddHandler button.Paint, AddressOf PaintSettingsNavigation
            AddHandler button.GotFocus, Sub() button.Invalidate()
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
                    .AccessibleName = group.Caption, .AccessibleRole = AccessibleRole.PageTab,
                    .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 220, .Height = 26,
                    .Margin = New Padding(0, 0, 0, 2), .TextAlign = ContentAlignment.MiddleLeft,
                    .AutoEllipsis = False, .Visible = False}
                item.FlatAppearance.BorderSize = 0
                AddHandler item.Click, Sub() RevealSettingsBlock(id, group, Nothing)
                AddHandler item.KeyDown, AddressOf OnSettingsNavigationKeyDown
                AddHandler item.Paint, AddressOf PaintSettingsNavigation
                AddHandler item.GotFocus, Sub() item.Invalidate()
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

    Private Sub ArrangeSettingsNavigation()
        Dim railWidth = Math.Max(1, _navigation.ClientSize.Width - LogicalToDeviceUnits(8) - SystemInformation.VerticalScrollBarWidth)
        Dim rtl = RightToLeft = RightToLeft.Yes
        ' Compact rows (owner revision 2026-10-13): a single-line page row fits its 20 px
        ' glyph, an item row its caption, with a sliver of air - not the former 44 px
        ' minimum that read twice too tall.
        For Each pair In _navButtons
            Dim button = pair.Value
            button.Width = railWidth
            ' Pages with items also keep the trailing chevron clear of the caption.
            Dim textWidth = Math.Max(1, button.Width - LogicalToDeviceUnits(52) -
                If(DirectCast(button, SettingsNavigationButton).HasItems, LogicalToDeviceUnits(28), 0))
            button.Height = Math.Max(LogicalToDeviceUnits(34), TextRenderer.MeasureText(button.Text, button.Font,
                New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(10))
        Next
        ' The subordinate indent: the page's icon column plus 16 logical px, mirrored in
        ' RTL (210 section 4.2).
        Dim indent = LogicalToDeviceUnits(46)
        For Each items In _navItems.Values
            For Each item In items
                item.Width = railWidth
                item.Padding = If(rtl, New Padding(8, 4, indent, 4), New Padding(indent, 4, 8, 4))
                Dim textWidth = Math.Max(1, item.Width - indent - LogicalToDeviceUnits(12))
                item.Height = Math.Max(LogicalToDeviceUnits(26), TextRenderer.MeasureText(item.Text, item.Font,
                    New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(10))
            Next
        Next
    End Sub

    Friend Sub SelectPage(id As String)
        SelectSettingsPage(id)
    End Sub

    Friend Sub SelectSettingsPage(id As String)
        If Not _pages.ContainsKey(id) OrElse id = _selectedPage Then Return
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

    ''' <summary>One gesture (210 section 4.3, Q210-3): a click on a collapsed page selects
    ''' it and reveals its items; a click on an expanded page folds the items and keeps the
    ''' page selected. The page row always means the whole page - every card of it in its
    ''' remembered fold state (238 section 2.1) - so it also gives the highlight back from
    ''' a chosen sub-menu item.</summary>
    Private Sub OnSettingsPageClicked(id As String)
        If SelectedItemId(id).Length > 0 Then
            _selectedItem(id) = String.Empty
            ApplySettingsNavigationTheme(ThemeService.Palette)
        End If
        If _navItems.ContainsKey(id) AndAlso _navItems(id).Count > 0 Then
            SetSettingsNavExpanded(id, Not NavExpanded(id))
        End If
        If id <> _selectedPage Then SelectSettingsPage(id)
    End Sub

    Private Sub SetSettingsNavExpanded(id As String, expanded As Boolean)
        Dim items As List(Of SettingsNavItemButton) = Nothing
        If Not _navItems.TryGetValue(id, items) OrElse items.Count = 0 Then Return
        managerSettingsContext.Page(id).NavExpanded = expanded
        For Each item In items
            item.Visible = expanded
        Next
        Dim button = DirectCast(_navButtons(id), SettingsNavigationButton)
        button.Expanded = expanded
        button.Invalidate()
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
            DirectCast(button, SettingsNavigationButton).Selected = selected
            StyleSettingsNavRow(button, selected, False, fill, palette)
            Dim oldImage = button.Image
            button.Image = GlyphRenderer.Render(SettingsPageGlyph(pair.Key), LogicalToDeviceUnits(20), SettingsNavIconPalette.ResolveInk(button.Name, palette.IsDark,
                    {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor},
                    button.ForeColor, SystemInformation.HighContrast), AddressOf CompanionAssets.OpenBundledAsset)
            oldImage?.Dispose()
            button.Invalidate()
        Next
        ' The item rows: exactly one row - the chosen sub-menu - reads selected, the rest
        ' sit on the plain surface; items of pages other than the shown one read muted.
        For Each pair In _navItems
            For Each item In pair.Value
                Dim selected = pair.Key = _selectedPage AndAlso SelectedItemId(pair.Key) = item.Group.Id
                item.Selected = selected
                StyleSettingsNavRow(item, selected, pair.Key <> _selectedPage, fill, palette)
            Next
        Next
        For Each pair In _pageGroups
            For Each grp In pair.Value
                If grp.Card IsNot Nothing Then grp.Card.ApplyPalette(palette)
            Next
        Next
    End Sub

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
        Return IconMap.GetMeaning("managerSettingsNav_" & id).GlyphId
    End Function

    Private Sub PaintSettingsNavigation(sender As Object, e As PaintEventArgs)
        Dim button = DirectCast(sender, Button)
        Dim rtl = RightToLeft = RightToLeft.Yes
        Dim selected = False
        Dim pageButton = TryCast(sender, SettingsNavigationButton)
        Dim itemButton = TryCast(sender, SettingsNavItemButton)
        If pageButton IsNot Nothing Then
            selected = pageButton.Selected
        ElseIf itemButton IsNot Nothing Then
            selected = itemButton.Selected
        End If
        If selected Then
            Dim width = LogicalToDeviceUnits(3)
            Using brush As New SolidBrush(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent))
                e.Graphics.FillRectangle(brush, If(rtl, button.Width - width, 0), 0, width, button.Height)
            End Using
        End If
        If pageButton IsNot Nothing AndAlso pageButton.HasItems Then PaintSettingsChevron(pageButton, e.Graphics, rtl)
        If button.Focused Then
            Dim inset = LogicalToDeviceUnits(3)
            Using pen As New Pen(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent), LogicalToDeviceUnits(2))
                e.Graphics.DrawRectangle(pen, inset, inset, Math.Max(1, button.Width - 2 * inset - 1), Math.Max(1, button.Height - 2 * inset - 1))
            End Using
        End If
    End Sub

    ''' <summary>The disclosure chevron at a page row's trailing edge - the same
    ''' collapsibleSection / collapsibleSectionOpen pair the former group headers drew
    ''' (210 section 4.1). Display only: the whole row is the toggle.</summary>
    Private Sub PaintSettingsChevron(button As SettingsNavigationButton, g As Graphics, rtl As Boolean)
        Dim size = LogicalToDeviceUnits(14)
        Dim inset = LogicalToDeviceUnits(12)
        Dim x = If(rtl, inset, button.Width - inset - size)
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
        Dim item = TryCast(row, SettingsNavItemButton)
        If item IsNot Nothing Then Return item.PageId
        Return DirectCast(row, SettingsNavigationButton).PageId
    End Function

    Private Sub OnSettingsNavigationKeyDown(sender As Object, e As KeyEventArgs)
        If e.Modifiers <> Keys.None Then Return
        Dim rtl = RightToLeft = RightToLeft.Yes
        Select Case e.KeyCode
            Case Keys.Up, Keys.Down
                Dim rows = SettingsNavRows().ToArray()
                Dim index = Array.IndexOf(rows, sender)
                If index < 0 Then Return
                index = If(e.KeyCode = Keys.Up, (index + rows.Length - 1) Mod rows.Length, (index + 1) Mod rows.Length)
                e.Handled = True
                e.SuppressKeyPress = True
                Dim target = rows(index)
                ' Landing on a page row selects it, as arrow walking always did here; an
                ' item row only takes the focus - activating it scrolls (section 4.4).
                If TypeOf target Is SettingsNavigationButton Then SelectSettingsPage(NavRowPageId(target))
                target.Focus()
            Case Keys.Left, Keys.Right
                ' Left collapses the selected page's items, Right expands them, mirrored
                ' in RTL (210 section 4.5).
                If _selectedPage.Length > 0 Then
                    Dim expand = If(e.KeyCode = Keys.Right, Not rtl, rtl)
                    SetSettingsNavExpanded(_selectedPage, expand)
                End If
                e.Handled = True
                e.SuppressKeyPress = True
            Case Keys.Home, Keys.End
                Dim ids = _pages.Keys.ToArray()
                Dim index = If(e.KeyCode = Keys.Home, 0, ids.Length - 1)
                e.Handled = True
                e.SuppressKeyPress = True
                SelectSettingsPage(ids(index))
                _navButtons(ids(index)).Focus()
        End Select
    End Sub

    ''' <summary>The tree row's face for assistive tech: selection, and for page rows the
    ''' disclosure state (210 section 4.6).</summary>
    Private NotInheritable Class NavRowAccessibleObject
        Inherits Control.ControlAccessibleObject

        Private ReadOnly row As Button

        Public Sub New(owner As Button)
            MyBase.New(owner)
            row = owner
        End Sub

        Public Overrides Sub DoDefaultAction()
            row.PerformClick()
        End Sub

        Public Overrides ReadOnly Property DefaultAction As String
            Get
                Return row.Text
            End Get
        End Property

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Dim page = TryCast(row, SettingsNavigationButton)
                Dim item = TryCast(row, SettingsNavItemButton)
                Dim selected = If(page IsNot Nothing, page.Selected, item IsNot Nothing AndAlso item.Selected)
                Dim value = MyBase.State Or If(selected, AccessibleStates.Selected, AccessibleStates.None)
                ' An expandable PageTab (210 section 4.6): the disclosure state travels
                ' with the row for assistive tech.
                If page IsNot Nothing AndAlso page.HasItems Then
                    value = value Or If(page.Expanded, AccessibleStates.Expanded, AccessibleStates.Collapsed)
                End If
                Return value
            End Get
        End Property
    End Class

    Private NotInheritable Class SettingsNavigationButton
        Inherits Button

        Friend PageId As String
        Friend Selected As Boolean
        Friend HasItems As Boolean
        Friend Expanded As Boolean

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New NavRowAccessibleObject(Me)
        End Function
    End Class

    ''' <summary>The active settings section id for in-app context help (CONTEXT-HELP rule 3).</summary>
    Public ReadOnly Property ActiveHelpPageId As String
        Get
            Return If(String.IsNullOrEmpty(_selectedPage), "startup", _selectedPage)
        End Get
    End Property

    ''' <summary>A subordinate row: a plain caption under its page, no icon and no chevron,
    ''' a PageTab of its page for assistive tech (210 section 4.6).</summary>
    Friend NotInheritable Class SettingsNavItemButton
        Inherits Button

        Friend PageId As String
        Friend Group As SettingsNavGroup
        Friend Selected As Boolean

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New NavRowAccessibleObject(Me)
        End Function
    End Class

End Class
