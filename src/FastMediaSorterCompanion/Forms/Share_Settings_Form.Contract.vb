Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private ReadOnly _pageGroups As New Dictionary(Of String, List(Of CollapsibleSection))(StringComparer.Ordinal)
    Private _wheelSafety As SettingsWheelSafety
    Private _aboutStart As Control
    Private _hostingSnapshot As WorkerResponse
    Private _exchangeSnapshot As ExchangeSnapshot
    Private _fittingSettings As Boolean
    Private ReadOnly _fittedSettingsWidths As New Dictionary(Of Panel, Integer)()
    Private _heldSettingsControls As Control()

    Private Shared Function CreateSettingsSection(key As String, title As String) As CollapsibleSection
        Dim section As New CollapsibleSection(key, title)
        section.SuspendLayout()
        section.Body.SuspendLayout()
        Return section
    End Function

    Private Shared Iterator Function Descendants(parent As Control) As IEnumerable(Of Control)
        For Each child As Control In parent.Controls
            Yield child
            For Each nested In Descendants(child)
                Yield nested
            Next
        Next
    End Function

    Private Shared Function SettingsPageCaption(section As CollapsibleSection) As String
        Select Case section.Key
            Case "startup" : Return Localization.T("Общие")
            Case "network" : Return Localization.T("Сеть")
            Case "hosting" : Return Localization.T("Хостинг")
            Case "exchange" : Return ExchangeText.Title()
            Case "exchange-account" : Return ExchangeDeviceText.GroupTitle()
            Case Else : Return Localization.T("О программе")
        End Select
    End Function

    Private Sub BuildAboutRows(section As CollapsibleSection)
        _aboutStart = New Label With {.Text = ProductNames.FullForLabel, .AutoSize = True, .Dock = DockStyle.Top}
        section.AddBodyRow(_aboutStart)
        section.AddBodyRow(New Label With {.Text = Localization.T("Версия") & ": " & Application.ProductVersion,
            .AutoSize = True, .Dock = DockStyle.Top, .Name = "managerVersion"})
        section.AddBodyRow(New Label With {.Text = Localization.T("Лицензия") & ": MIT", .AutoSize = True,
            .Dock = DockStyle.Top, .Name = "managerLicense"})
        Dim licenseText As String
        Using stream = CompanionAssets.OpenBundledAsset("LICENSE"), reader As New IO.StreamReader(stream)
            licenseText = reader.ReadToEnd().Replace(vbCrLf, vbLf).Replace(vbLf, Environment.NewLine)
        End Using
        section.AddBodyRow(New TextBox With {.Text = licenseText, .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Top, .Height = 140, .Width = 400,
            .AccessibleName = Localization.T("Лицензия"), .Name = "managerLicenseText"})
        AddHelpLink(section, Localization.T("Политика конфиденциальности"),
            Sub() NetworkInfo.OpenInBrowser(ShareLinks.Localized(SitePages.PrivacyPageUrl)))
        Dim support As New Button With {.Text = Localization.T("Отправить журнал автору.."), .AutoSize = True,
            .Name = "managerSupportBundle", .Margin = New Padding(0, 8, 0, 0)}
        ButtonGlyphs.Apply(support, "action.email")
        AddHandler support.Click, Async Sub() Await SendLogsFlow.RunAsync(Me)
        section.AddBodyRow(support)
    End Sub

    ' The page owns the old section's editors; the inner groups own disclosure/context only.
    Private Sub BuildContractGroups(section As CollapsibleSection)
        Dim rows = section.Body.Controls.Cast(Of Control)().ToArray()
        Dim groups As New List(Of CollapsibleSection)()
        section.Body.SuspendLayout()
        section.Body.Controls.Clear()
        DirectCast(section.Body, TableLayoutPanel).RowStyles.Clear()
        DirectCast(section.Body, TableLayoutPanel).RowCount = 0
        Dim current As CollapsibleSection = Nothing
        Dim groupId As String = Nothing
        Dim caption As String = Nothing
        For Each row In rows
            Dim nextId = "basic"
            Dim nextCaption = section.Title
            Select Case section.Key
                Case "startup"
                    Dim appearance = row.Contains(cmbTheme) OrElse row.Contains(cmbLanguage)
                    nextId = If(appearance, "appearance", "startup")
                    nextCaption = If(appearance, Localization.T("Настройки интерфейса"), Localization.T("Запуск"))
                Case "network"
                    Dim policyIndex = Array.IndexOf(rows, DirectCast(chkLanOnly, Control))
                    Dim policy = Array.IndexOf(rows, row) >= policyIndex
                    nextId = If(policy, "access", "connections")
                    nextCaption = If(policy, Localization.T("Сеть"), Localization.T("Подключения"))
                Case "hosting"
                    Dim actions = Array.IndexOf(rows, row) >= Array.IndexOf(rows, DirectCast(_btnSwitchToService, Control))
                    nextId = If(actions, "service", "status")
                    nextCaption = If(actions, Localization.T("Служба Windows"), Localization.T("Состояние"))
                Case "exchange"
                    If row Is _pnlXConfig Then
                        nextId = "configuration" : nextCaption = Localization.T("Сеть")
                    ElseIf row Is _pnlXAccounts Then
                        nextId = "accounts" : nextCaption = ExchangeAdminText.AccountsTitle()
                    ElseIf Array.IndexOf(rows, row) > Array.IndexOf(rows, DirectCast(_pnlXAccounts, Control)) Then
                        nextId = "service" : nextCaption = Localization.T("Служба Windows")
                    Else
                        nextId = "server" : nextCaption = ExchangeText.Title()
                    End If
                Case "help"
                    Dim about = Array.IndexOf(rows, row) >= Array.IndexOf(rows, _aboutStart)
                    nextId = If(about, "about", "documentation")
                    nextCaption = If(about, Localization.T("О программе"), Localization.T("Документация"))
            End Select
            If current Is Nothing OrElse nextId <> groupId Then
                groupId = nextId : caption = nextCaption
                current = New CollapsibleSection(section.Key & "/" & groupId, caption)
                current.Width = section.Width
                current.SuspendLayout()
                current.Body.SuspendLayout()
                current.Name = current.Key
                current.ConfigureSettingsHeader()
                current.Expanded = (section.Key = "startup" AndAlso groupId = "appearance") OrElse
                    section.Key = "network" OrElse (section.Key = "hosting" AndAlso groupId = "status") OrElse
                    (section.Key = "exchange" AndAlso groupId = "server") OrElse section.Key = "exchange-account" OrElse section.Key = "help"
                groups.Add(current)
                section.AddBodyRow(current)
                AddHandler current.ExpandedChanged, AddressOf OnSettingsGroupChanged
            End If
            current.AddBodyRow(row)
        Next
        For Each group In groups
            group.Body.ResumeLayout(False)
            group.ResumeLayout(False)
        Next
        _pageGroups.Add(section.Key, groups)
        section.Body.ResumeLayout(False)
    End Sub

    ' Async state snapshots change many visibility/text values together, never one live layout per field.
    Private Function HoldSettingsPage(id As String) As IDisposable
        Dim page As Panel = Nothing
        If Not _built OrElse Not _pages.TryGetValue(id, page) Then Return Nothing
        Return New SettingsPageLayoutScope(page)
    End Function

    Private NotInheritable Class SettingsPageLayoutScope
        Implements IDisposable
        Private ReadOnly page As Panel
        Private ReadOnly held As Control()
        Friend Sub New(page As Panel)
            Me.page = page
            held = Descendants(page).ToArray()
            page.SuspendLayout()
            For Each control In held
                control.SuspendLayout()
            Next
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            For Each control In held.Reverse()
                control.ResumeLayout(True)
            Next
            page.ResumeLayout(True)
        End Sub
    End Class

    Private Sub OnSettingsGroupChanged(sender As Object, e As EventArgs)
        If Not _built Then Return
        Dim group = DirectCast(sender, CollapsibleSection)
        managerSettingsContext.Page(_selectedPage).Groups(group.Key) = group.Expanded
        _pages(_selectedPage).PerformLayout()
    End Sub

    Private Sub HoldSettingsLayout()
        _heldSettingsControls = Descendants(Me).ToArray()
        For Each control In _heldSettingsControls
            control.SuspendLayout()
        Next
    End Sub

    Private Sub ReleaseSettingsLayout()
        For Each section In SettingsSections()
            section.Body.ResumeLayout(False)
            section.ResumeLayout(False)
        Next
        For Each control In _heldSettingsControls.Reverse()
            control.ResumeLayout(False)
        Next
        _heldSettingsControls = Nothing
    End Sub

    Private Sub ConfigureSettingsContract()
        For Each field In GetType(Share_Settings_Form).GetFields(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            If Not GetType(Control).IsAssignableFrom(field.FieldType) Then Continue For
            Dim control = TryCast(field.GetValue(Me), Control)
            If control IsNot Nothing AndAlso control.Name.Length = 0 Then control.Name = field.Name
        Next
        SetBooleanHint(chkAutostart, Localization.T("Запускает менеджер после входа в Windows. Не меняет режим службы."))
        SetBooleanHint(chkOpenOnStart, toolTip.GetToolTip(chkOpenOnStart))
        toolTip.SetToolTip(cmbLanguage, Localization.T("Меняет язык всех открытых окон и меню сразу."))
        For Each row In Descendants(Me).OfType(Of ManagerSettingRow)()
            Dim hint = toolTip.GetToolTip(row.Editor)
            If row.Editor Is numPort Then hint = Localization.T("Порт подключения. Изменение применяется отдельной кнопкой; обновите правило роутера и выданные коды.")
            If row.Editor Is numXPort Then hint = ExchangeText.PortNote()
            If hint.Length > 0 Then row.SetHint(hint)
        Next
        If _chkXKeep IsNot Nothing Then SetBooleanHint(_chkXKeep,
            Localization.T("После закрытия менеджера сервер обмена продолжит работать. Выключите, чтобы останавливать его при выходе."))
        NormalizeSettingsControls(Me)
        _wheelSafety = New SettingsWheelSafety(Me)
        Application.AddMessageFilter(_wheelSafety)
        AddHandler Disposed, Sub() Application.RemoveMessageFilter(_wheelSafety)
        AddHandler UiLanguage.Changed, AddressOf RefreshSettingsLanguage
        AddHandler Disposed, Sub() RemoveHandler UiLanguage.Changed, AddressOf RefreshSettingsLanguage
    End Sub

    Private Sub SetBooleanHint(check As CheckBox, text As String)
        If text.Length = 0 OrElse check.Parent Is Nothing Then Return
        Dim hint = NoteLabel(text)
        hint.Name = check.Name & "Hint"
        Dim table = TryCast(check.Parent, TableLayoutPanel)
        If table Is Nothing Then Return
        table.SuspendLayout()
        Dim index = table.GetRow(check) + 1
        For Each child As Control In table.Controls
            Dim position = table.GetRow(child)
            If position >= index Then table.SetRow(child, position + 1)
        Next
        table.RowCount += 1
        table.RowStyles.Insert(index, New RowStyle(SizeType.AutoSize))
        table.Controls.Add(hint, 0, index)
        CheckBoxLayout.BindHint(check, hint)
        AddHandler check.VisibleChanged, Sub() hint.Visible = check.Visible
        AddHandler check.EnabledChanged, Sub() hint.Enabled = check.Enabled
        hint.Visible = check.Visible
        hint.Enabled = check.Enabled
        check.AccessibleDescription = text
        table.ResumeLayout(False)
    End Sub

    Private Sub NormalizeSettingsControls(parent As Control)
        For Each control In Descendants(parent).ToArray()
            Dim number = TryCast(control, NumericUpDown)
            If number IsNot Nothing Then
                Dim digitFont As New Font(Font.FontFamily, 12.0F)
                number.Font = digitFont
                AddHandler number.Disposed, Sub() digitFont.Dispose()
                number.Width = 110
            End If
            Dim button = TryCast(control, Button)
            If button IsNot Nothing AndAlso Not _navButtons.Values.Contains(button) AndAlso Not _headerCommands.Contains(button) Then
                button.MinimumSize = New Size(0, 44)
                button.Padding = New Padding(16, 5, 16, 5)
                button.AutoSize = True
            End If
            Dim check = TryCast(control, CheckBox)
            If check IsNot Nothing Then
                check.MinimumSize = New Size(0, 44)
            End If
            ' Protocol addresses, ports and fingerprints keep their data direction in RTL.
            If TypeOf control Is TextBox OrElse TypeOf control Is NumericUpDown Then control.RightToLeft = RightToLeft.No
        Next
        FitSettingsContent()
    End Sub

    Private Shared Function HasSettingRowAncestor(control As Control) As Boolean
        Dim parent = control.Parent
        While parent IsNot Nothing
            If TypeOf parent Is ManagerSettingRow Then Return True
            parent = parent.Parent
        End While
        Return False
    End Function

    Private Sub FitSettingsContent(Optional changedViewport As Panel = Nothing)
        If _fittingSettings OrElse _pages.Count = 0 Then Return
        _fittingSettings = True
        Try
            For Each viewport In _pages.Values
                If changedViewport IsNot Nothing AndAlso viewport IsNot changedViewport Then Continue For
                ' Reserve the scrollbar even while hidden, so disclosure does not refit the whole subtree twice.
                ' ClientSize already excludes it when visible; content/group padding is subtracted below.
                Dim width = Math.Max(1, viewport.ClientSize.Width - viewport.Padding.Horizontal -
                    If(viewport.VerticalScroll.Visible, 0, SystemInformation.VerticalScrollBarWidth))
                Dim previousWidth As Integer
                If _fittedSettingsWidths.TryGetValue(viewport, previousWidth) AndAlso previousWidth = width Then Continue For
                _fittedSettingsWidths(viewport) = width
                Dim controls = Descendants(viewport).ToArray()
                viewport.SuspendLayout()
                For Each child In controls
                    child.SuspendLayout()
                Next
                Try
                    For Each control In controls
                        If HasSettingRowAncestor(control) Then Continue For
                        Dim available = width
                        Dim ancestor = control.Parent
                        While ancestor IsNot Nothing AndAlso ancestor IsNot viewport
                            available -= ancestor.Padding.Horizontal + ancestor.Margin.Horizontal
                            ancestor = ancestor.Parent
                        End While
                        available = Math.Max(1, available - control.Margin.Horizontal)
                        Dim label = TryCast(control, Label)
                        If label IsNot Nothing AndAlso Not HasSettingRowAncestor(label) Then
                            label.MaximumSize = New Size(available, 0)
                        End If
                        Dim check = TryCast(control, CheckBox)
                        If check IsNot Nothing Then check.MaximumSize = New Size(available, 0)
                        Dim flow = TryCast(control, FlowLayoutPanel)
                        If flow IsNot Nothing Then
                            flow.MaximumSize = New Size(available, 0)
                            If flow.FlowDirection = FlowDirection.TopDown OrElse flow.Controls.OfType(Of ManagerSettingRow)().Any() Then
                                flow.MinimumSize = New Size(available, 0)
                            End If
                        End If
                        Dim list = TryCast(control, ListView)
                        If list IsNot Nothing Then list.Width = available
                        Dim row = TryCast(control, ManagerSettingRow)
                        If row IsNot Nothing Then
                            If TypeOf row.Parent Is FlowLayoutPanel Then
                                row.MinimumSize = New Size(available, 0)
                                row.Width = available
                            End If
                            row.ArrangeColumns()
                        End If
                    Next
                Finally
                    For Each child In controls.Reverse()
                        child.ResumeLayout(True)
                    Next
                    viewport.ResumeLayout(True)
                End Try
            Next
        Finally
            _fittingSettings = False
        End Try
    End Sub

    Private Sub FitInitialSettingsBounds()
        Dim page = _pages("startup")
        page.PerformLayout()
        Dim height = page.Controls(0).PreferredSize.Height + LogicalToDeviceUnits(52) + _root.Padding.Vertical
        Dim rail = _navButtons.Values.Sum(Function(button) button.Height + button.Margin.Vertical) + _root.Padding.Vertical
        ClientSize = New Size(ClientSize.Width, Math.Max(height, rail))
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    Private Shared Function NavigationFill(palette As ThemePalette, amount As Single) As Color
        If SystemInformation.HighContrast Then Return palette.SurfaceWindow
        Return Color.FromArgb(CInt(palette.SurfaceWindow.R * (1 - amount) + palette.Accent.R * amount),
            CInt(palette.SurfaceWindow.G * (1 - amount) + palette.Accent.G * amount),
            CInt(palette.SurfaceWindow.B * (1 - amount) + palette.Accent.B * amount))
    End Function

    Private Sub ApplySettingsContrast(palette As ThemePalette)
        For Each label In Descendants(Me).OfType(Of Label)()
            label.ForeColor = ThemePalette.ReadableText(label.ForeColor, label.BackColor)
        Next
        For Each button In _navButtons.Values
            Dim surfaces = {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor}
            For Each surface In surfaces
                button.ForeColor = ThemePalette.ReadableText(button.ForeColor, surface)
            Next
        Next
    End Sub

    Private Sub RefreshSettingsLanguage(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        Dim loading = _loading
        _loading = True
        SuspendLayout()
        Try
            Text = Localization.T("Настройки менеджера")
            ApplyHostingState(_hostingSnapshot)
            If _exchangeSnapshot IsNot Nothing Then ApplyExchangeState(_exchangeSnapshot)
            ApplyAccountState()
            ApplyProducerState()
            UpdateSummaries()
            For Each section In AllSettingsSections()
                _navButtons(section.Key).Text = SettingsPageCaption(section)
                _navButtons(section.Key).AccessibleName = _navButtons(section.Key).Text
            Next
            Dim version = Descendants(Me).FirstOrDefault(Function(control) control.Name = "managerVersion")
            If version IsNot Nothing Then version.Text = Localization.T("Версия") & ": " & Application.ProductVersion
            Dim license = Descendants(Me).FirstOrDefault(Function(control) control.Name = "managerLicense")
            If license IsNot Nothing Then license.Text = Localization.T("Лицензия") & ": MIT"
            For Each row In Descendants(Me).OfType(Of ManagerSettingRow)()
                row.Editor.AccessibleName = row.Caption.Text
                row.Editor.AccessibleDescription = row.Hint.Text
            Next
            cmbTheme.Items.Clear()
            cmbTheme.Items.AddRange({Localization.T("Как в системе"), Localization.T("Светлая"), Localization.T("Тёмная")})
            cmbTheme.SelectedIndex = CInt(ThemeService.Mode)
            cmbLanguage.SelectedIndex = Localization.CurrentIndex
            For Each button In _headerCommands
                button.AccessibleName = SettingsCommandCaption(button.Name)
                toolTip.SetToolTip(button, button.AccessibleName)
            Next
            ArrangeSettingsNavigation()
            FitSettingsContent()
        Finally
            _loading = loading
            ResumeLayout(True)
        End Try
    End Sub

    Private NotInheritable Class SettingsWheelSafety
        Implements IMessageFilter
        Private ReadOnly owner As Share_Settings_Form
        Friend Sub New(owner As Share_Settings_Form)
            Me.owner = owner
        End Sub
        Public Function PreFilterMessage(ByRef message As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If message.Msg <> &H20A OrElse Not owner.Visible OrElse owner._selectedPage.Length = 0 Then Return False
            Dim control As Control = System.Windows.Forms.Control.FromHandle(message.HWnd)
            While control IsNot Nothing AndAlso Not TypeOf control Is ComboBox AndAlso Not TypeOf control Is NumericUpDown
                control = control.Parent
            End While
            If control Is Nothing OrElse control.FindForm() IsNot owner Then Return False
            Dim combo = TryCast(control, ComboBox)
            If combo IsNot Nothing AndAlso combo.DroppedDown Then Return False
            Dim raw = CInt((message.WParam.ToInt64() >> 16) And &HFFFFL)
            Dim delta = If(raw >= &H8000, raw - &H10000, raw)
            Dim viewport = owner._pages(owner._selectedPage)
            Dim lines = SystemInformation.MouseWheelScrollLines
            Dim amount = If(lines < 0, viewport.ClientSize.Height, Math.Max(1, lines) * owner.Font.Height)
            viewport.AutoScrollPosition = New Point(0, Math.Max(0, -viewport.AutoScrollPosition.Y - delta * amount \ 120))
            Return True
        End Function
    End Class
End Class
