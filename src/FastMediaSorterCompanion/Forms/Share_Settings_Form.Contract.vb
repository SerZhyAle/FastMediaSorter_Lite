Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    ''' <summary>Each page's former fold groups (210, recarded by 238): one left-menu item
    ''' and one collapsible group card per entry, in page order. The card carries the fold
    ''' state the left menu used to own.</summary>
    Private ReadOnly _pageGroups As New Dictionary(Of String, List(Of SettingsNavGroup))(StringComparer.Ordinal)
    Private _wheelSafety As SettingsWheelSafety
    Private _aboutStart As Control
    Private _hostingSnapshot As WorkerResponse
    Private _exchangeSnapshot As ExchangeSnapshot
    Private _fittingSettings As Boolean
    Private ReadOnly _fittedSettingsWidths As New Dictionary(Of Panel, Integer)()
    Private _heldSettingsControls As Control()

    ''' <summary>One former fold group of one page (238): its id in stored state, its
    ''' localized caption source, the collapsible card that frames its rows in the right
    ''' pane, and the rows themselves (210 section 5).</summary>
    Friend NotInheritable Class SettingsNavGroup
        Friend Id As String
        Friend CaptionSource As Func(Of String)
        Friend Card As SettingsGroupCard
        Friend Rows As New List(Of Control)()

        Friend ReadOnly Property Caption As String
            Get
                Return CaptionSource()
            End Get
        End Property

        ''' <summary>The card is the single fold state; before it exists (mid-build) the
        ''' group reads expanded, the build default of a page's first card.</summary>
        Friend Property Expanded As Boolean
            Get
                Return If(Card Is Nothing, True, Card.Expanded)
            End Get
            Set(value As Boolean)
                If Card IsNot Nothing Then Card.Expanded = value
            End Set
        End Property
    End Class

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

    ''' <summary>The block's own editor a subordinate-item click or a search reveal focuses
    ''' (210 section 4.4): its first selectable control in display order, a setting row
    ''' answering with its editor rather than its caption.</summary>
    Private Shared Function FirstSettingsBlockEditor(group As SettingsNavGroup) As Control
        For Each row As Control In group.Rows
            Dim owned = TryCast(row, ManagerSettingRow)
            If owned IsNot Nothing Then
                If owned.Editor.CanSelect Then Return owned.Editor
                Continue For
            End If
            For Each control In (New Control() {row}).Concat(Descendants(row))
                Dim inner = TryCast(control, ManagerSettingRow)
                Dim candidate = If(inner IsNot Nothing, inner.Editor, control)
                If candidate.CanSelect Then Return candidate
            Next
        Next
        Return Nothing
    End Function

    Private Shared Function SettingsPageCaption(section As CollapsibleSection) As String
        Select Case section.Key
            Case "startup" : Return Localization.T("Общие")
            Case "network" : Return Localization.T("Сеть")
            Case "hosting" : Return Localization.T("Хостинг")
            Case "exchange" : Return ExchangeText.Title()
            Case "exchange-account" : Return ExchangeDeviceText.GroupTitle()
            Case "scheduled" : Return ScheduledPageCaption()
            Case Else : Return Localization.T("Справка")
        End Select
    End Function

    ' The About group of the "help" page (BuildAboutRows and its rows) lives in Share_Settings_Form.About.vb.

    ' The page owns the old section's editors; each inner group is one collapsible card
    ' (238 section 2.3): header bar, hairline boundary, fold state of its own. The left
    ' menu names the blocks; the cards give them their bodies and their borders.
    Private Sub BuildContractGroups(section As CollapsibleSection)
        ' Group classification uses the original controls, before adjacent commands are packed.
        Dim commandRows = section.Body.Controls.OfType(Of FlowLayoutPanel)().
            Where(Function(flow) Object.Equals(flow.Tag, "sectionCommands")).ToArray()
        Dim rows = section.Body.Controls.Cast(Of Control)().
            SelectMany(Function(control) If(commandRows.Contains(TryCast(control, FlowLayoutPanel)),
                control.Controls.Cast(Of Control)(), {control}.AsEnumerable())).ToArray()
        Dim groups As New List(Of SettingsNavGroup)()
        section.Body.SuspendLayout()
        section.Body.Controls.Clear()
        DirectCast(section.Body, TableLayoutPanel).RowStyles.Clear()
        DirectCast(section.Body, TableLayoutPanel).RowCount = 0
        Dim current As SettingsNavGroup = Nothing
        Dim groupId As String = Nothing
        For Each row In rows
            Dim nextId = "basic"
            Dim caption As Func(Of String) = Function() section.Title
            Select Case section.Key
                Case "startup"
                    Dim appearance = row.Contains(cmbTheme) OrElse row.Contains(cmbLanguage)
                    nextId = If(appearance, "appearance", "startup")
                    caption = If(appearance, Function() Localization.T("Тема и язык"), Function() Localization.T("Автозапуск и запуск"))
                Case "network"
                    Dim policyIndex = Array.IndexOf(rows, DirectCast(chkLanOnly, Control))
                    Dim policy = Array.IndexOf(rows, row) >= policyIndex
                    nextId = If(policy, "access", "connections")
                    caption = If(policy, Function() Localization.T("Сетевая политика и Wi-Fi"), Function() Localization.T("Локальные подключения"))
                Case "hosting"
                    Dim actions = Array.IndexOf(rows, row) >= Array.IndexOf(rows, DirectCast(_btnSwitchToService, Control))
                    nextId = If(actions, "service", "status")
                    caption = If(actions, Function() Localization.T("Управление службой"), Function() Localization.T("Текущее состояние"))
                Case "exchange"
                    If row Is _pnlXConfig Then
                        nextId = "configuration" : caption = Function() Localization.T("Сетевой адрес и порты")
                    ElseIf row Is _pnlXAccounts Then
                        nextId = "accounts" : caption = Function() ExchangeAdminText.AccountsTitle()
                    ElseIf row Is _pnlXDevices Then
                        nextId = "devices" : caption = Function() Localization.T("Подключённые устройства")
                    ElseIf row Is _pnlXInvitations Then
                        nextId = "invitations" : caption = Function() Localization.T("Приглашения и регистрация")
                    ElseIf _pnlXRemoteAdmin IsNot Nothing AndAlso row Is _pnlXRemoteAdmin Then
                        nextId = "remote-admin" : caption = Function() Localization.T("Удалённое администрирование")
                    ElseIf _pnlXRemoteAdmin IsNot Nothing AndAlso Array.IndexOf(rows, row) > Array.IndexOf(rows, DirectCast(_pnlXRemoteAdmin, Control)) Then
                        nextId = "service" : caption = Function() Localization.T("Управление службой")
                    ElseIf _pnlXInvitations IsNot Nothing AndAlso Array.IndexOf(rows, row) > Array.IndexOf(rows, DirectCast(_pnlXInvitations, Control)) Then
                        nextId = "service" : caption = Function() Localization.T("Управление службой")
                    ElseIf _pnlXAccounts IsNot Nothing AndAlso Array.IndexOf(rows, row) > Array.IndexOf(rows, DirectCast(_pnlXAccounts, Control)) Then
                        nextId = "service" : caption = Function() Localization.T("Управление службой")
                    Else
                        nextId = "server" : caption = Function() Localization.T("Основные параметры")
                    End If
                Case "exchange-account"
                    Dim isClipboard = _clipRows.Contains(row)
                    nextId = If(row Is _pxGroup, "publishing", If(isClipboard, "clipboard", "account"))
                    caption = If(row Is _pxGroup, Function() ExchangePublishText.GroupTitle(), If(isClipboard, Function() Localization.T("Буфер на сервере"), Function() Localization.T("Основные параметры")))
                Case "scheduled"
                    ' The list and its commands, then what applies to this PC (4.4).
                    Dim machine = Array.IndexOf(rows, row) >= Array.IndexOf(rows, DirectCast(_schedFeature, Control))
                    nextId = If(machine, "machine", "operations")
                    caption = If(machine, Function() Localization.T("Для этого компьютера"), Function() Localization.T("Операции"))
                Case "help"
                    Dim about = Array.IndexOf(rows, row) >= Array.IndexOf(rows, _aboutStart)
                    nextId = If(about, "about", "documentation")
                    caption = If(about, Function() Localization.T("О программе"), Function() Localization.T("Документация"))
            End Select
            If current Is Nothing OrElse nextId <> groupId Then
                groupId = nextId
                current = New SettingsNavGroup With {.Id = groupId, .CaptionSource = caption}
                ' 238 section 2.2: the refined captions above never repeat the page title,
                ' and the card - not a bare label - marks where one block ends and the next
                ' begins. The page's first card is the one that starts open (T-3).
                Dim card As New SettingsGroupCard(current.Caption, groups.Count = 0) With {
                    .Name = "managerSettingsCard_" & section.Key & "_" & groupId}
                current.Card = card
                Dim pId = section.Key
                Dim grp = current
                AddHandler card.ExpandedChanged, Sub() OnGroupCardExpansionChanged(pId, grp)
                section.AddBodyRow(card)
                groups.Add(current)
            End If
            If row Is _clipTitle Then
                _clipTitle.Visible = False
                Continue For
            End If
            current.Rows.Add(row)
            current.Card.AddBodyRow(row)
        Next
        _pageGroups.Add(section.Key, groups)
        For Each commands In commandRows
            commands.Dispose()
        Next
        section.Body.ResumeLayout(False)
    End Sub

    Friend Sub SetGroupExpanded(pageId As String, group As SettingsNavGroup, expanded As Boolean)
        group.Expanded = expanded
    End Sub

    ''' <summary>The one fold-state write (238 T-4): the card raises the event after its own
    ''' single layout pass, the state is recorded at once, and the shown page is measured one
    ''' more time so the viewport's scrollbar follows the fold. A restore during the build
    ''' only records - no page is visible to re-measure.</summary>
    Private Sub OnGroupCardExpansionChanged(pageId As String, group As SettingsNavGroup)
        managerSettingsContext.Page(pageId).Groups(group.Id) = group.Card.Expanded
        If Not _built OrElse _loading Then Return
        Dim viewport As Panel = Nothing
        If _pages.TryGetValue(pageId, viewport) AndAlso viewport.Visible Then viewport.PerformLayout()
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
        ' The cards were created suspended (their constructor's handoff, like the sections').
        For Each grp As SettingsNavGroup In _pageGroups.Values.SelectMany(Function(g) g)
            grp.Card.Body.ResumeLayout(False)
            grp.Card.ResumeLayout(False)
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
        If chkOpenOnStart IsNot Nothing Then SetBooleanHint(chkOpenOnStart, toolTip.GetToolTip(chkOpenOnStart))
        If cmbLanguage IsNot Nothing Then toolTip.SetToolTip(cmbLanguage, Localization.T("Меняет язык всех открытых окон и меню сразу."))
        For Each row In Descendants(Me).OfType(Of ManagerSettingRow)()
            Dim hint = toolTip.GetToolTip(row.Editor)
            If row.Editor Is numPort Then hint = Localization.T("Порт подключения. Изменение применяется отдельной кнопкой; обновите правило роутера и выданные коды.")
            If row.Editor Is numXPort Then hint = ExchangeText.PortNote()
            If hint.Length > 0 Then row.SetHint(hint)
        Next
        If _chkXKeep IsNot Nothing Then SetBooleanHint(_chkXKeep,
            Localization.T("После закрытия менеджера сервер обмена продолжит работать. Выключите, чтобы останавливать его при выходе."))
        If _schedFeature IsNot Nothing Then SetBooleanHint(_schedFeature,
            Localization.T("Выключите, чтобы остановить все расписания. Запуск вручную остаётся доступным."))
        NormalizeSettingsControls(Me)
        _wheelSafety = New SettingsWheelSafety(Me)
        Application.AddMessageFilter(_wheelSafety)
        AddHandler Disposed, Sub() Application.RemoveMessageFilter(_wheelSafety)
        AddHandler UiLanguage.Changed, AddressOf RefreshSettingsLanguage
        AddHandler Disposed, Sub() RemoveHandler UiLanguage.Changed, AddressOf RefreshSettingsLanguage
    End Sub

    Private Sub SetBooleanHint(check As CheckBox, text As String)
        If check Is Nothing OrElse text.Length = 0 OrElse check.Parent Is Nothing Then Return
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
            If button IsNot Nothing AndAlso Not _navButtons.Values.Contains(button) AndAlso
               Not _headerCommands.Contains(button) AndAlso Not TypeOf button Is SettingsNavItemButton Then
                button.MinimumSize = New Size(0, 44)
                button.Padding = New Padding(16, 5, 16, 5)
                button.AutoSize = True
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink
                button.Dock = DockStyle.None
                button.Anchor = AnchorStyles.Top Or AnchorStyles.Left
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
                            For Each commandButton In flow.Controls.OfType(Of Button)()
                                commandButton.MaximumSize = New Size(Math.Max(1, available - flow.Padding.Horizontal - commandButton.Margin.Horizontal), 0)
                            Next
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
        ' Each destination sizes itself on its own first page: the SFTP and local-exchange windows have no
        ' "startup" page, and a window with no saved bounds of its own must still open.
        Dim page = If(_pages.ContainsKey("startup"), _pages("startup"), _pages.Values.First())
        page.PerformLayout()
        Dim height = page.Controls(0).PreferredSize.Height + LogicalToDeviceUnits(52) + _root.Padding.Vertical
        ' The rail as it will actually show: page rows plus the item lists their saved
        ' expansion state reveals (210 section 7.4).
        Dim rail = SettingsNavRows().Sum(Function(row) row.Height + row.Margin.Vertical) + _root.Padding.Vertical
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
            ' Every container held while the captions change, laid out once after - see LiveControlLocalization.HoldLayout.
            Using LiveControlLocalization.HoldLayout(Me)
                Text = DestinationCaption()
                ApplyHostingState(_hostingSnapshot)
                If _exchangeSnapshot IsNot Nothing Then ApplyExchangeState(_exchangeSnapshot)
                ApplyAccountState()
                ApplyProducerState()
                RefreshScheduledLanguage()
                UpdateSummaries()
                For Each section In AllSettingsSections()
                    _navButtons(section.Key).Text = SettingsPageCaption(section)
                    _navButtons(section.Key).AccessibleName = _navButtons(section.Key).Text
                    Dim items As List(Of SettingsNavItemButton) = Nothing
                    If _navItems.TryGetValue(section.Key, items) Then
                        For Each item In items
                            item.Text = item.Group.Caption
                            item.AccessibleName = item.Group.Caption
                            toolTip.SetToolTip(item, item.Group.Caption)
                            item.Group.Card.Caption = item.Group.Caption
                        Next
                    End If
                Next
                RefreshAboutTexts()
                For Each row In Descendants(Me).OfType(Of ManagerSettingRow)()
                    row.Editor.AccessibleName = row.Caption.Text
                    row.Editor.AccessibleDescription = row.Hint.Text
                Next
                If cmbTheme IsNot Nothing Then
                cmbTheme.Items.Clear()
                cmbTheme.Items.AddRange({Localization.T("Как в системе"), Localization.T("Светлая"), Localization.T("Тёмная")})
                cmbTheme.SelectedIndex = CInt(ThemeService.Mode)
                cmbLanguage.SelectedIndex = Localization.CurrentIndex
                End If
                For Each button In _headerCommands
                    button.AccessibleName = SettingsCommandCaption(button.Name)
                    toolTip.SetToolTip(button, button.AccessibleName)
                Next
            End Using
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
