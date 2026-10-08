Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Implements IThemeAware

    Private ReadOnly _pages As New Dictionary(Of String, Panel)(StringComparer.Ordinal)
    Private ReadOnly _navButtons As New Dictionary(Of String, Button)(StringComparer.Ordinal)
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
                .AccessibleName = SettingsPageCaption(sec), .AccessibleRole = AccessibleRole.PageTab,
                .FlatStyle = FlatStyle.Flat, .UseVisualStyleBackColor = False, .Width = 180, .Height = 48,
                .Padding = New Padding(10, 4, 8, 4), .Margin = New Padding(0, 0, 0, 4),
                .TextAlign = ContentAlignment.MiddleLeft, .ImageAlign = ContentAlignment.MiddleLeft,
                .TextImageRelation = TextImageRelation.ImageBeforeText, .AutoEllipsis = False}
            AddHandler button.Disposed, Sub() button.Image?.Dispose()
            button.FlatAppearance.BorderSize = 0
            AddHandler button.Click, Sub() SelectSettingsPage(id)
            AddHandler button.KeyDown, AddressOf OnSettingsNavigationKeyDown
            AddHandler button.Paint, AddressOf PaintSettingsNavigation
            AddHandler button.GotFocus, Sub() button.Invalidate()
            AddHandler button.LostFocus, Sub() button.Invalidate()
            _navButtons.Add(id, button)
            _navigation.Controls.Add(button)
            toolTip.SetToolTip(button, button.Text)

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
        For Each button In _navButtons.Values
            button.Width = Math.Max(1, _navigation.ClientSize.Width - LogicalToDeviceUnits(8) - SystemInformation.VerticalScrollBarWidth)
            Dim textWidth = Math.Max(1, button.Width - LogicalToDeviceUnits(52))
            button.Height = Math.Max(LogicalToDeviceUnits(44), TextRenderer.MeasureText(button.Text, button.Font,
                New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height + LogicalToDeviceUnits(16))
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
        For Each pair In _navButtons
            Dim button = pair.Value
            Dim selected = pair.Key = _selectedPage
            DirectCast(button, SettingsNavigationButton).Selected = selected
            Dim fill = Color.FromArgb(CInt(palette.SurfaceWindow.R * 0.78 + palette.Accent.R * 0.22),
                CInt(palette.SurfaceWindow.G * 0.78 + palette.Accent.G * 0.22),
                CInt(palette.SurfaceWindow.B * 0.78 + palette.Accent.B * 0.22))
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
        If _navButtons.ContainsKey(_selectedPage) AndAlso button Is _navButtons(_selectedPage) Then
            Dim width = LogicalToDeviceUnits(3)
            Using brush As New SolidBrush(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent))
                e.Graphics.FillRectangle(brush, If(rtl, button.Width - width, 0), 0, width, button.Height)
            End Using
        End If
        If button.Focused Then
            Dim inset = LogicalToDeviceUnits(3)
            Using pen As New Pen(If(SystemInformation.HighContrast, button.ForeColor, ThemeService.Palette.Accent), LogicalToDeviceUnits(2))
                e.Graphics.DrawRectangle(pen, inset, inset, Math.Max(1, button.Width - 2 * inset - 1), Math.Max(1, button.Height - 2 * inset - 1))
            End Using
        End If
    End Sub

    Private Sub OnSettingsNavigationKeyDown(sender As Object, e As KeyEventArgs)
        If e.Modifiers <> Keys.None Then Return
        Dim ids = _pages.Keys.ToArray()
        Dim index = Array.IndexOf(ids, _selectedPage)
        Select Case e.KeyCode
            Case Keys.Up : index = (index + ids.Length - 1) Mod ids.Length
            Case Keys.Down : index = (index + 1) Mod ids.Length
            Case Keys.Home : index = 0
            Case Keys.End : index = ids.Length - 1
            Case Else : Return
        End Select
        e.Handled = True
        e.SuppressKeyPress = True
        SelectSettingsPage(ids(index))
        _navButtons(ids(index)).Focus()
    End Sub

    Private NotInheritable Class SettingsNavigationButton
        Inherits Button

        Friend Selected As Boolean

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Home, Keys.End : Return True
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
                    Return MyBase.State Or If(navigationButton.Selected, AccessibleStates.Selected, AccessibleStates.None)
                End Get
            End Property
        End Class
    End Class

End Class
