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
''' selects it and toggles its item list; clicking an item selects the page and scrolls the
''' owning block's heading to the top of the right pane. Which pages are expanded is the
''' stored context's <see cref="SettingsPageContext.NavExpanded"/>.</summary>
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

    Private Sub BuildSettingsPages()
        _root = New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(12, 10, 12, 6), .Size = New Size(ClientSize.Width, ClientSize.Height)}
        _root.SuspendLayout()
        _root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 196))
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
            sec.Width = Math.Max(1, ClientSize.Width - 240)
            BuildContractGroups(sec)
            Dim id = sec.Key
            Dim button As New SettingsNavigationButton With {.Text = SettingsPageCaption(sec), .Name = "managerSettingsNav_" & id,
                .PageId = id, .AccessibleName = SettingsPageCaption(sec), .AccessibleRole = AccessibleRole.PageTab,
                .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 180, .Height = 48,
                .Padding = New Padding(10, 4, 8, 4), .Margin = New Padding(0, 0, 0, 4),
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
                    .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 180, .Height = 44,
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
            Dim content As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Width = ClientSize.Width - 240,
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
            Case Else : Return Localization.T("Версия, лицензия, документация и помощь.")
        End Select
    End Function

    Private Sub ArrangeSettingsNavigation()
        Dim railWidth = Math.Max(1, _navigation.ClientSize.Width - LogicalToDeviceUnits(8) - SystemInformation.VerticalScrollBarWidth)
        Dim rtl = RightToLeft = RightToLeft.Yes
        For Each pair In _navButtons
            Dim button = pair.Value
            button.Width = railWidth
            ' Pages with items also keep the trailing chevron clear of the caption.
            Dim textWidth = Math.Max(1, button.Width - LogicalToDeviceUnits(52) -
                If(DirectCast(button, SettingsNavigationButton).HasItems, LogicalToDeviceUnits(28), 0))
            button.Height = Math.Max(LogicalToDeviceUnits(44), TextRenderer.MeasureText(button.Text, button.Font,
                New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(16))
        Next
        ' The subordinate indent: the page's icon column plus 16 logical px, mirrored in
        ' RTL (210 section 4.2).
        Dim indent = LogicalToDeviceUnits(46)
        For Each items In _navItems.Values
            For Each item In items
                item.Width = railWidth
                item.Padding = If(rtl, New Padding(8, 4, indent, 4), New Padding(indent, 4, 8, 4))
                Dim textWidth = Math.Max(1, item.Width - indent - LogicalToDeviceUnits(12))
                item.Height = Math.Max(LogicalToDeviceUnits(44), TextRenderer.MeasureText(item.Text, item.Font,
                    New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(16))
            Next
        Next
    End Sub

    Private Sub SelectSettingsPage(id As String)
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

    ''' <summary>One gesture (210 section 4.3, Q210-3): a click on a collapsed page selects
    ''' it and reveals its items; a click on an expanded page folds the items and keeps the
    ''' page selected. The right pane never changes on a toggle.</summary>
    Private Sub OnSettingsPageClicked(id As String)
        If _navItems.ContainsKey(id) AndAlso _navItems(id).Count > 0 Then
            SetSettingsNavExpanded(id, Not NavExpanded(id))
        End If
        SelectSettingsPage(id)
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

    ''' <summary>The one reveal path (210 sections 4.4 and 5.4): select the page, scroll the
    ''' block's heading to the top of the viewport, focus the block's first editor. Nothing
    ''' is ever hidden, so there is no fold to open - the menu's state is untouched.</summary>
    Private Sub RevealSettingsBlock(pageId As String, group As SettingsNavGroup, editor As Control)
        SelectSettingsPage(pageId)
        If editor Is Nothing Then editor = FirstSettingsBlockEditor(group)
        Dim viewport = _pages(pageId)
        viewport.PerformLayout()
        viewport.AutoScrollPosition = New Point(0, Math.Max(0, ViewportTop(group.Heading)))
        If editor IsNot Nothing Then editor.Focus()
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
            Dim selected = pair.Key = _selectedPage
            DirectCast(button, SettingsNavigationButton).Selected = selected
            button.BackColor = If(selected, If(SystemInformation.HighContrast, SystemColors.Highlight, fill), palette.SurfaceWindow)
            button.ForeColor = If(selected AndAlso SystemInformation.HighContrast, SystemColors.HighlightText, palette.TextPrimary)
            button.FlatAppearance.MouseOverBackColor = NavigationFill(palette, If(selected, 0.3F, 0.18F))
            button.FlatAppearance.MouseDownBackColor = NavigationFill(palette, If(selected, 0.4F, 0.28F))
            If SystemInformation.HighContrast Then
                button.FlatAppearance.MouseOverBackColor = button.BackColor
                button.FlatAppearance.MouseDownBackColor = button.BackColor
            End If
            For Each surface In {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor}
                button.ForeColor = ThemePalette.ReadableText(button.ForeColor, surface)
            Next
            Dim oldImage = button.Image
            button.Image = GlyphRenderer.Render(SettingsPageGlyph(pair.Key), LogicalToDeviceUnits(20), SettingsNavIconPalette.ResolveInk(button.Name, palette.IsDark,
                    {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor},
                    button.ForeColor, SystemInformation.HighContrast), AddressOf CompanionAssets.OpenBundledAsset)
            oldImage?.Dispose()
            button.Invalidate()
        Next
        ' The item rows: the selected page's tinted background continues behind its items
        ' (210 section 8), an item of any other page reads muted on the plain surface.
        For Each pair In _navItems
            Dim selected = pair.Key = _selectedPage
            For Each item In pair.Value
                item.BackColor = If(selected, If(SystemInformation.HighContrast, SystemColors.Highlight, fill), palette.SurfaceWindow)
                item.ForeColor = If(selected AndAlso SystemInformation.HighContrast, SystemColors.HighlightText,
                                If(selected, palette.TextPrimary, palette.TextMuted))
                item.FlatAppearance.MouseOverBackColor = NavigationFill(palette, If(selected, 0.3F, 0.18F))
                item.FlatAppearance.MouseDownBackColor = NavigationFill(palette, If(selected, 0.4F, 0.28F))
                If SystemInformation.HighContrast Then
                    item.FlatAppearance.MouseOverBackColor = item.BackColor
                    item.FlatAppearance.MouseDownBackColor = item.BackColor
                End If
                For Each surface In {item.BackColor, item.FlatAppearance.MouseOverBackColor, item.FlatAppearance.MouseDownBackColor}
                    item.ForeColor = ThemePalette.ReadableText(item.ForeColor, surface)
                Next
                item.Invalidate()
            Next
        Next
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
        Dim pageButton = TryCast(sender, SettingsNavigationButton)
        If pageButton IsNot Nothing Then
            If _navButtons.ContainsKey(_selectedPage) AndAlso button Is _navButtons(_selectedPage) Then
                Dim width = LogicalToDeviceUnits(3)
                Using brush As New SolidBrush(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent))
                    e.Graphics.FillRectangle(brush, If(rtl, button.Width - width, 0), 0, width, button.Height)
                End Using
            End If
            If pageButton.HasItems Then PaintSettingsChevron(pageButton, e.Graphics, rtl)
        End If
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
            Return New NavigationAccessibleObject(Me)
        End Function

        Private NotInheritable Class NavigationAccessibleObject
            Inherits Control.ControlAccessibleObject

            Private ReadOnly navigationButton As SettingsNavigationButton

            Public Sub New(owner As SettingsNavigationButton)
                MyBase.New(owner)
                navigationButton = owner
            End Sub

            Public Overrides Sub DoDefaultAction()
                navigationButton.PerformClick()
            End Sub

            Public Overrides ReadOnly Property DefaultAction As String
                Get
                    Return navigationButton.Text
                End Get
            End Property

            Public Overrides ReadOnly Property State As AccessibleStates
                Get
                    Dim value = MyBase.State Or If(navigationButton.Selected, AccessibleStates.Selected, AccessibleStates.None)
                    ' An expandable PageTab (210 section 4.6): the disclosure state travels
                    ' with the row for assistive tech.
                    If navigationButton.HasItems Then
                        value = value Or If(navigationButton.Expanded, AccessibleStates.Expanded, AccessibleStates.Collapsed)
                    End If
                    Return value
                End Get
            End Property
        End Class
    End Class

    ''' <summary>A subordinate row: a plain caption under its page, no icon and no chevron,
    ''' a PageTab of its page for assistive tech (210 section 4.6).</summary>
    Friend NotInheritable Class SettingsNavItemButton
        Inherits Button

        Friend PageId As String
        Friend Group As SettingsNavGroup

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function
    End Class

End Class
