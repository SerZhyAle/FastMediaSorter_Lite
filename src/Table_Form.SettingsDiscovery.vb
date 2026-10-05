#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms

Partial Public Class Table_Form
    ' The header tools are icon buttons (search, expand all, collapse all): each draws its
    ' picture in the button's CURRENT ink at the CURRENT scale, so a theme or a DPI change re-renders it.
    Private ReadOnly settingsToolIcons As New Dictionary(Of Button, Func(Of Integer, Color, Bitmap))()
    Private settingsPageHasGroups As Boolean = True
    Private searchSettingsButton As Button
    Private expandSettingsButton As Button
    Private collapseSettingsButton As Button
    Private sharedSoundButton As Button
    Private arrangingSettingsHeader As Boolean
    Private updatingThumbnailChoice As Boolean
    Private updatingAudioDefaults As Boolean
    Private wheelFilter As SettingsWheelFilter
    Private slotHealthDetails As Label

    Private Sub AddRecipientGridActions(host As Panel)
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .WrapContents = True}
        slotHealthDetails = New Label With {.Dock = DockStyle.Bottom, .Height = Sp(66), .ForeColor = SettingsMutedText}
        Dim browse = SettingsToolButton(Sub()
                                           Dim slot = SelectedRecipientSlot()
                                           If slot > 0 Then BrowseLocalSlotFolder(slot)
                                       End Sub)
        Dim resources = SettingsToolButton(Sub()
                                              Dim slot = SelectedRecipientSlot()
                                              If slot > 0 Then OfferResourceForSlot(slot)
                                          End Sub)
        Dim clear = SettingsToolButton(Sub()
                                          Dim slot = SelectedRecipientSlot()
                                          If slot > 0 Then SetSlotDestination(slot, String.Empty)
                                      End Sub)
        Dim refresh As Action = Sub()
                                    browse.Text = Localization.T("Папка на этом компьютере..")
                                    resources.Text = Localization.T("Ресурсы")
                                    clear.Text = Localization.T("Очистить")
                                End Sub
        refresh()
        expandedRefreshActions.Add(refresh)
        actions.Controls.AddRange({browse, resources, clear})
        host.Controls.Add(slotHealthDetails)
        host.Controls.Add(actions)
        host.Height += Sp(130)
        Dim selection As Action = Sub()
                                      Dim slot = SelectedRecipientSlot()
                                      browse.Enabled = slot > 0
                                      resources.Enabled = slot > 0
                                      clear.Enabled = slot > 0
                                      RefreshSlotHealthDetails()
                                  End Sub
        AddHandler Data_Grid_View.SelectionChanged, Sub() selection()
        selection()
        ' Identity fills plus the leading health glyph are painted, not styled (129 §3.2).
        AddHandler Data_Grid_View.CellPainting, AddressOf Data_Grid_View_CellPainting
        Data_Grid_View.AllowDrop = True
        AddHandler Data_Grid_View.DragOver,
            Sub(sender, e)
                Dim paths = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                Dim point = Data_Grid_View.PointToClient(New Point(e.X, e.Y))
                Dim hit = Data_Grid_View.HitTest(point.X, point.Y)
                e.Effect = If(paths IsNot Nothing AndAlso paths.Length = 1 AndAlso IO.Directory.Exists(paths(0)) AndAlso
                    hit.RowIndex >= 1 AndAlso hit.RowIndex <= 10, DragDropEffects.Copy, DragDropEffects.None)
            End Sub
        AddHandler Data_Grid_View.DragDrop,
            Sub(sender, e)
                Dim paths = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                Dim point = Data_Grid_View.PointToClient(New Point(e.X, e.Y))
                Dim hit = Data_Grid_View.HitTest(point.X, point.Y)
                If paths Is Nothing OrElse paths.Length <> 1 OrElse Not IO.Directory.Exists(paths(0)) OrElse hit.RowIndex < 1 OrElse hit.RowIndex > 10 Then Return
                SetSlotDestination(hit.RowIndex, paths(0))
            End Sub
    End Sub

    Private Function SelectedRecipientSlot() As Integer
        Dim slot As Integer = If(Data_Grid_View.CurrentCell Is Nothing, 0, Data_Grid_View.CurrentCell.RowIndex)
        Return If(slot >= 1 AndAlso slot <= 10, slot, 0)
    End Function

    Private Sub RefreshSlotHealthDetails()
        If slotHealthDetails Is Nothing Then Return
        Dim slot = SelectedRecipientSlot()
        slotHealthDetails.Text = If(slot = 0, String.Empty, Data_Grid_View.Item(1, slot).ToolTipText)
        slotHealthDetails.AccessibleName = slotHealthDetails.Text
    End Sub
    Private ReadOnly settingsInputErrors As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private settingsErrorProvider As ErrorProvider

    Friend Sub ActivateModernSettingsOnOwnerMonitor()
        PositionModernSettingsOnOwnerMonitor()
        Activate()
    End Sub

    Private Sub SetSettingsInputError(rowId As String, message As String)
        settingsInputErrors(rowId) = message
        Dim row = modernSettingsRows.FirstOrDefault(Function(r) r.Key = rowId)
        If row Is Nothing Then Return
        If settingsErrorProvider Is Nothing Then
            settingsErrorProvider = New ErrorProvider With {.ContainerControl = Me, .BlinkStyle = ErrorBlinkStyle.NeverBlink}
            AddHandler Disposed, Sub() settingsErrorProvider.Dispose()
        End If
        settingsErrorProvider.SetError(row.Editor, message)
        row.Editor.AccessibleDescription = row.Description.Text & " " & message
        RefreshGroupSummary(row)
    End Sub

    Private Sub BuildSettingsDiscovery()
        wheelFilter = New SettingsWheelFilter(Me)
        Application.AddMessageFilter(wheelFilter)
        AddHandler Disposed, Sub() Application.RemoveMessageFilter(wheelFilter)
        searchSettingsButton = SettingsIconButton("settingsToolSearch", Sub() ShowSettingsSearch(),
            Function(size, ink) RenderSettingsGlyph(IconMap.GetMeaning("settingsSearch").GlyphId, size, ink))
        expandSettingsButton = SettingsIconButton("settingsToolExpandAll", Sub() SetPageGroupsExpanded(True),
            Function(size, ink) RenderSettingsGlyph(IconMap.GetMeaning("collapsibleSection").GlyphId, size, ink))
        collapseSettingsButton = SettingsIconButton("settingsToolCollapseAll", Sub() SetPageGroupsExpanded(False),
            Function(size, ink) RenderSettingsGlyph(IconMap.GetMeaning("collapsibleSectionOpen").GlyphId, size, ink))
        sharedSoundButton = SettingsToolButton(Sub() RevealModernSetting("video_volume"))
        For Each tool As Button In {searchSettingsButton, expandSettingsButton, collapseSettingsButton}
            modernSettingsHeader.Controls.Add(tool)
        Next
        AddHandler modernSettingsHeader.Resize, Sub() ArrangeSettingsDiscoveryHeader()
        AddSettingRow(settingsGroups("video/basic").Body, "shared_audio_settings", sharedSoundButton, 230)
        modernSettingsRows.Last().Host.Name = "settingsRow_shared_audio_settings"
        Tab_Control.BringToFront()
        Tab_Control.TabIndex = 1
        AddHandler Tab_Control.SelectedIndexChanged, Sub() RefreshSettingsTools()
        ' 130 §3.3: opening the Storage page re-measures the caches, so the sizes never
        ' only say what was true when the window was built.
        AddHandler Tab_Control.SelectedIndexChanged,
            Sub()
                If modernStoragePage IsNot Nothing AndAlso Tab_Control.SelectedTab Is modernStoragePage Then RefreshCacheUsageMeasurements()
            End Sub
        For Each row In modernSettingsRows
            For Each editor As Control In FindModernControls(Of Control)(row.Host)
                If TypeOf editor Is CheckBox Then AddHandler DirectCast(editor, CheckBox).CheckedChanged, Sub() RefreshGroupSummary(row)
                If TypeOf editor Is ComboBox Then AddHandler DirectCast(editor, ComboBox).SelectedIndexChanged, Sub() RefreshGroupSummary(row)
                If TypeOf editor Is NumericUpDown Then AddHandler DirectCast(editor, NumericUpDown).ValueChanged, Sub() RefreshGroupSummary(row)
                If editor Is row.Editor OrElse Not TypeOf editor Is Button Then
                    editor.AccessibleName = row.Title.Text
                    editor.AccessibleDescription = If(row.Description Is Nothing, String.Empty, row.Description.Text)
                End If
                Dim combo = TryCast(editor, ComboBox)
                If combo IsNot Nothing Then
                    StyleModernComboBox(combo)
                    AddHandler combo.DropDown, Sub() SizeSettingsChoices(combo)
                End If
                If editor Is row.Editor AndAlso TypeOf editor Is Button Then editor.Height = Math.Max(editor.Height, Sp(44))
                Dim number = TryCast(editor, NumericUpDown)
                If number IsNot Nothing Then
                    number.AutoSize = False
                    number.MinimumSize = New Size(0, Sp(44))
                End If
                Dim input = TryCast(editor, TextBox)
                If input IsNot Nothing Then
                    input.AutoSize = False
                    input.MinimumSize = New Size(0, Sp(44))
                End If
            Next
        Next
        ' Every drop-down of every page, not only the ones the first pass saw; then the rows that hold one
        ' take the field's real height. A DPI change (a monitor with another scale) restyles them again,
        ' AFTER the layout's own DpiChanged handler has updated the scale.
        RestyleAllSettingsCombos()
        For Each row In modernSettingsRows
            If TypeOf row.Editor Is ComboBox Then SizeModernSettingRow(row)
        Next
        AddHandler DpiChanged,
            Sub()
                RestyleAllSettingsCombos()
                For Each row In modernSettingsRows
                    If TypeOf row.Editor Is ComboBox Then SizeModernSettingRow(row)
                Next
                ArrangeSettingsDiscoveryHeader()
            End Sub
        LocalizeSettingsDiscovery()
    End Sub

    Private Sub FitInitialSettingsWindowToContent()
        Dim savedWidth As Integer
        If Integer.TryParse(GetSetting(App_name, Second_App_Name, "SettingsWindowWidth", "0"), savedWidth) AndAlso savedWidth > 0 Then Return
        Dim basic = settingsGroups("general/basic")
        Dim captionWidth = modernSettingsRows.Where(Function(r) r.Host.Parent Is basic.Body).
            Select(Function(r) TextRenderer.MeasureText(r.Title.Text, r.Title.Font).Width).DefaultIfEmpty(Sp(220)).Max()
        Dim desiredWidth = modernSettingsSidebar.Width + Math.Max(Sp(560), captionWidth + Sp(300)) + Sp(48)
        Dim desiredHeight = Math.Max(Sp(520), modernSettingsHeader.Height + basic.Height + Sp(100))
        FitModernSettingsClientSize(Screen.FromControl(If(Owner, Me)).WorkingArea, desiredWidth, desiredHeight)
    End Sub

    Private Function SettingsToolButton(action As Action) As Button
        Dim button As New Button With {.AutoSize = True, .MinimumSize = New Size(Sp(44), Sp(44)), .UseMnemonic = False,
            .Font = SettingsFont("Segoe UI", Sf(9.0F)), .Margin = New Padding(0, 0, Sp(6), 0)}
        AddHandler button.Click, Sub() action()
        Return button
    End Function

    ''' <summary>Logical (96 dpi) side of a header tool and of the picture on it: large enough to hit, not huge.</summary>
    Private Const SettingsToolSide As Integer = 44
    Private Const SettingsToolGlyph As Integer = 24

    Private Function SettingsIconButton(name As String, action As Action, render As Func(Of Integer, Color, Bitmap)) As Button
        Dim button As New Button With {.Name = name, .AutoSize = False, .Size = New Size(Sp(SettingsToolSide), Sp(SettingsToolSide)),
            .UseMnemonic = False, .Text = String.Empty, .ImageAlign = ContentAlignment.MiddleCenter,
            .Margin = New Padding(0), .Padding = New Padding(0)}
        AddHandler button.Click, Sub() action()
        settingsToolIcons(button) = render
        ' A palette pass changes ForeColor; the picture has to follow, a cached bitmap would keep the old ink.
        AddHandler button.ForeColorChanged, Sub() RenderSettingsToolIcon(button)
        AddHandler button.Disposed, Sub() settingsToolIcons.Remove(button)
        Return button
    End Function

    Private Sub RenderSettingsToolIcon(button As Button)
        Dim render As Func(Of Integer, Color, Bitmap) = Nothing
        If button.IsDisposed OrElse Not settingsToolIcons.TryGetValue(button, render) Then Return
        Dim size As Integer = Sp(SettingsToolGlyph)
        Dim stamp As String = size.ToString(CultureInfo.InvariantCulture) & ":" & button.ForeColor.ToArgb().ToString(CultureInfo.InvariantCulture)
        If Equals(button.Tag, stamp) AndAlso button.Image IsNot Nothing Then Return
        Dim previous As Image = button.Image
        Try
            button.Image = render(size, button.ForeColor)
            button.Tag = stamp
        Catch ex As Exception
            ' A missing picture must not take the header with it: the tooltip and the accessible name still say what the tool does.
            AppFileLogger.LogException("Settings header icon", ex)
            Return
        End Try
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

    Private Shared Function RenderSettingsGlyph(glyphId As String, size As Integer, ink As Color) As Bitmap
        Return GlyphRenderer.Render(glyphId, size, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
    End Function

    ''' <summary>Names, tooltips and pictures of the header tools - the words are the localized strings the old
    ''' text buttons carried, now the tooltip and the accessible name (an icon alone says nothing to a screen reader).</summary>
    Private Sub SetSettingsToolWords(button As Button, text As String)
        button.Text = String.Empty
        button.AccessibleName = text
        button.AccessibleDescription = text
        modernNavTips.SetToolTip(button, text)
    End Sub

    Private Sub LocalizeSettingsDiscovery()
        If searchSettingsButton Is Nothing Then Return
        SetSettingsToolWords(searchSettingsButton, Localization.T("Поиск настроек"))
        SetSettingsToolWords(expandSettingsButton, Localization.T("Развернуть все"))
        SetSettingsToolWords(collapseSettingsButton, Localization.T("Свернуть все"))
        sharedSoundButton.Text = Localization.T("Общий звук: аудио и видео")
        For Each row In modernSettingsRows
            Dim number = TryCast(row.Editor, NumericUpDown)
            If number Is Nothing AndAlso row.Editor IsNot Nothing Then number = FindModernControls(Of NumericUpDown)(row.Editor).FirstOrDefault()
            If number IsNot Nothing AndAlso row.Description IsNot Nothing Then
                Dim unit As String = String.Empty
                Select Case row.Key
                    Case "recipients_width", "chrome_corners" : unit = " px"
                    Case "recipients_font" : unit = " pt"
                    Case "recipients_opacity", "chrome_opacity", "video_volume", "ocr_opacity" : unit = " %"
                    Case "slideshow_interval", "video_controls_delay", "video_hide_cursor", "video_seek_short", "video_seek_medium", "video_seek_long" : unit = " s"
                    Case "audio_sleep_timer", "secure_remember_minutes" : unit = " min"
                    Case "decode_cache_limit", "thumb_cache_limit", "ocr_cache_limit", "archive_cache_limit", "archive_entry_limit", "remote_cache_limit" : unit = " MB"
                    Case "text_preview_max_kb" : unit = " KiB"
                End Select
                row.Description.Text &= " (" & number.Minimum.ToString(CultureInfo.CurrentCulture) & " - " & number.Maximum.ToString(CultureInfo.CurrentCulture) & unit & ")."
                SizeModernSettingRow(row)
            End If
            For Each editor In FindModernControls(Of Control)(row.Host)
                If editor Is row.Editor OrElse Not TypeOf editor Is Button Then
                    editor.AccessibleName = row.Title.Text
                    editor.AccessibleDescription = If(row.Description Is Nothing, String.Empty, row.Description.Text)
                End If
            Next
        Next
        For Each group In settingsGroups.Values
            RefreshGroupSummary(group)
        Next
        RefreshSettingsTools()
        RefreshSettingsDependencies()
        ArrangeSettingsDiscoveryHeader()
    End Sub

    Private Sub RefreshSettingsTools()
        If searchSettingsButton Is Nothing OrElse Tab_Control.SelectedTab Is Nothing Then Return
        Dim hasGroups = settingsGroups.Values.Any(Function(g) g.Parent Is PageFlow(Tab_Control.SelectedTab))
        settingsPageHasGroups = hasGroups
        expandSettingsButton.Visible = hasGroups
        collapseSettingsButton.Visible = hasGroups
        ArrangeSettingsDiscoveryHeader()
    End Sub

    Private Sub RefreshSettingsDependencies()
        For Each row In modernSettingsRows
            If row.Description Is Nothing OrElse row.Editor Is Nothing Then Continue For
            Dim enabled As Boolean = row.Editor.Enabled
            row.Title.ForeColor = If(enabled, SettingsText, SettingsMutedText)
            row.Description.ForeColor = SettingsMutedText
            If TypeOf row.Editor Is CheckBox Then
                row.Title.Cursor = If(enabled, Cursors.Hand, Cursors.Default)
                row.Description.Cursor = row.Title.Cursor
            End If
            Dim reason As String = Nothing
            Select Case row.Key
                Case "dynamic_perspective" : reason = "Перспективный фон"
                Case "animated_perspective" : reason = "Динамический ореол"
                Case "halo_speed" : reason = "Анимация ореола"
                Case "ocr_auto", "ocr_source", "ocr_quality", "ocr_mode" : reason = "Распознавание текста"
                Case "ocr_api" : reason = "LibreTranslate"
            End Select
            If reason IsNot Nothing Then
                row.Description.Text = ModernSettingDescription(row.Key)
                If Not enabled Then row.Description.Text &= " " & Localization.TF("Доступно при включении: {0}.", Localization.T(reason))
                row.Editor.AccessibleDescription = row.Description.Text
                SizeModernSettingRow(row)
            End If
        Next
    End Sub

    Private NotInheritable Class SettingsWheelFilter
        Implements IMessageFilter

        Private ReadOnly owner As Table_Form
        Public Sub New(owner As Table_Form)
            Me.owner = owner
        End Sub

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If m.Msg <> &H20A OrElse Not owner.Visible Then Return False
            Dim control As Control = Control.FromHandle(m.HWnd)
            While control IsNot Nothing AndAlso Not TypeOf control Is ComboBox AndAlso Not TypeOf control Is NumericUpDown AndAlso Not TypeOf control Is TrackBar
                control = control.Parent
            End While
            If control Is Nothing OrElse control.FindForm() IsNot owner Then Return False
            Dim combo As ComboBox = TryCast(control, ComboBox)
            If combo IsNot Nothing AndAlso combo.DroppedDown AndAlso combo.ClientRectangle.Contains(combo.PointToClient(Control.MousePosition)) Then Return False
            Dim raw As Integer = CInt((m.WParam.ToInt64() >> 16) And &HFFFFL)
            Dim delta As Integer = If(raw >= &H8000, raw - &H10000, raw)
            Dim flow As FlowLayoutPanel = owner.PageFlow(owner.Tab_Control.SelectedTab)
            Dim lines As Integer = SystemInformation.MouseWheelScrollLines
            Dim amount As Integer = If(lines < 0, flow.ClientSize.Height, Math.Max(1, lines) * owner.Font.Height)
            flow.AutoScrollPosition = New Point(0, Math.Max(0, -flow.AutoScrollPosition.Y - delta * amount \ 120))
            Return True
        End Function
    End Class

    Private Sub ArrangeSettingsDiscoveryHeader()
        If searchSettingsButton Is Nothing OrElse arrangingSettingsHeader Then Return
        arrangingSettingsHeader = True
        Try
            ' One row of square icon tools on the header's END side (right; left under RTL), in reading order:
            ' search, expand all, collapse all. A tool that is hidden on this page leaves no gap.
            Dim tools As New List(Of Button)()
            ' Control.Visible is False for everything while the window is not shown yet, so the page's own
            ' answer (the page has groups) is kept in a field instead of being read back from the buttons.
            tools.Add(searchSettingsButton)
            If settingsPageHasGroups Then
                tools.Add(expandSettingsButton)
                tools.Add(collapseSettingsButton)
            End If
            Dim side As Integer = Sp(SettingsToolSide)
            Dim gap As Integer = Sp(6)
            Dim edge As Integer = Sp(16)
            Dim rtl As Boolean = RightToLeft = RightToLeft.Yes
            Dim headerWidth As Integer = modernSettingsHeader.ClientSize.Width
            Dim lefts As Integer() = SettingsHeaderRules.ToolLefts(headerWidth, tools.Count, side, gap, edge, rtl)
            For index As Integer = 0 To tools.Count - 1
                tools(index).SetBounds(lefts(index), Sp(10), side, side)
                tools(index).Anchor = If(rtl, AnchorStyles.Top Or AnchorStyles.Left, AnchorStyles.Top Or AnchorStyles.Right)
                RenderSettingsToolIcon(tools(index))
            Next
            Dim span As Integer() = SettingsHeaderRules.TitleSpan(headerWidth, tools.Count, side, gap, edge, Sp(20), Sp(12), rtl)
            modernSettingsTitle.Left = span(0)
            modernSettingsSubtitle.Left = span(0)
            modernSettingsTitle.Width = span(1)
            modernSettingsSubtitle.Width = span(1)
            modernSettingsTitle.Height = TextRenderer.MeasureText(modernSettingsTitle.Text, modernSettingsTitle.Font,
                New Size(modernSettingsTitle.Width, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Sp(4)
            modernSettingsSubtitle.Top = modernSettingsTitle.Bottom + Sp(2)
            modernSettingsSubtitle.Height = TextRenderer.MeasureText(modernSettingsSubtitle.Text, modernSettingsSubtitle.Font,
                New Size(modernSettingsSubtitle.Width, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Sp(4)
            modernSettingsHeader.Height = Math.Max(searchSettingsButton.Bottom, modernSettingsSubtitle.Bottom) + Sp(10)
        Finally
            arrangingSettingsHeader = False
        End Try
    End Sub

    Friend Sub SetPageGroupsExpanded(expanded As Boolean)
        If Not settingsContextReady Then Return
        CaptureSettingsPage(Tab_Control.SelectedTab)
        restoringSettingsContext = True
        Try
            For Each group In settingsGroups.Values.Where(Function(g) g.Parent Is PageFlow(Tab_Control.SelectedTab))
                group.Expanded = expanded
                settingsContext.Page(pageIds(Tab_Control.SelectedTab)).Groups(group.GroupId.Split("/"c)(1)) = expanded
            Next
        Finally
            restoringSettingsContext = False
        End Try
        LayoutModernSettingsPages()
        contextFlush.MarkDirty()
        QueueRestoreSettingsContext()
    End Sub

    Private Sub RefreshGroupSummary(row As ModernSettingRow)
        Dim groupId As String = Nothing
        If rowGroups.TryGetValue(row.Key, groupId) Then RefreshGroupSummary(settingsGroups(groupId))
    End Sub

    Private Sub RefreshGroupSummary(group As SettingsCollapsibleGroup)
        Dim values = modernSettingsRows.Where(Function(r) r.Host.Parent Is group.Body).
            Select(Function(r) New With {.Row = r, .Value = SafeSettingsValue(r)}).
            Where(Function(item) item.Value.Length > 0).Take(2).
            Select(Function(item) item.Row.Title.Text.TrimEnd(":"c) & ": " & item.Value)
        Dim errors = modernSettingsRows.Where(Function(r) r.Host.Parent Is group.Body AndAlso settingsInputErrors.ContainsKey(r.Key)).
            Select(Function(r) settingsInputErrors(r.Key)).Where(Function(message) message.Length > 0)
        group.SetSummary(String.Join("; ", errors.Concat(values)))
    End Sub

    ' Never inspect text boxes, endpoints, editable choices, resources or history values.
    Private Function SafeSettingsValue(row As ModernSettingRow) As String
        Dim check = TryCast(row.Editor, CheckBox)
        If check IsNot Nothing Then Return Localization.T(If(check.Checked, "Включено", "Выключено"))
        Dim number = TryCast(row.Editor, NumericUpDown)
        If number IsNot Nothing Then Return number.Value.ToString(CultureInfo.CurrentCulture)
        Dim combo = TryCast(row.Editor, ComboBox)
        If combo IsNot Nothing AndAlso combo.DropDownStyle = ComboBoxStyle.DropDownList AndAlso combo.SelectedItem IsNot Nothing Then
            Return combo.GetItemText(combo.SelectedItem)
        End If
        Return String.Empty
    End Function

    Private Sub SizeSettingsChoices(combo As ComboBox)
        Dim available = Screen.FromControl(Me).WorkingArea.Width
        Dim longest = combo.Items.Cast(Of Object)().Select(Function(item) TextRenderer.MeasureText(combo.GetItemText(item), combo.Font).Width).
            DefaultIfEmpty(combo.Width).Max()
        combo.DropDownWidth = Math.Min(available, Math.Max(combo.Width, longest + SystemInformation.VerticalScrollBarWidth + Sp(24)))
        modernNavTips.SetToolTip(combo, combo.GetItemText(combo.SelectedItem))
    End Sub

    Friend Function FindSettingsRows(query As String) As String()
        Return modernSettingsRows.Where(Function(row) rowGroups.ContainsKey(row.Key)).Where(
            Function(row)
                Dim options As New List(Of String)()
                For Each combo In FindModernControls(Of ComboBox)(row.Host)
                    If combo.DropDownStyle = ComboBoxStyle.DropDownList Then
                        options.AddRange(combo.Items.Cast(Of Object)().Select(Function(item) combo.GetItemText(item)))
                    End If
                Next
                Return SettingsSearchRules.Matches(query, row.Title.Text, If(row.Description Is Nothing, String.Empty, row.Description.Text), options, CultureInfo.CurrentCulture)
            End Function).Select(Function(row) row.Key).ToArray()
    End Function

    Private Sub ShowSettingsSearch()
        Dim selectedId As String = Nothing
        Using dialog As New Form With {.Text = Localization.T("Поиск настроек"), .StartPosition = FormStartPosition.CenterParent,
            .ShowInTaskbar = False, .MinimizeBox = False, .MaximizeBox = False, .ClientSize = New Size(Sp(650), Sp(420)),
            .MinimumSize = New Size(Sp(400), Sp(280)), .Font = Font, .BackColor = SettingsSurface, .ForeColor = SettingsText,
            .RightToLeft = RightToLeft, .KeyPreview = True}
            Dim query As New TextBox With {.Dock = DockStyle.Top, .AccessibleName = dialog.Text}
            query.TabIndex = 0
            Dim status As New Label With {.Dock = DockStyle.Bottom, .AutoSize = True, .MaximumSize = New Size(dialog.ClientSize.Width, 0)}
            Dim results As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .HorizontalScrollbar = True,
                .BackColor = SettingsSurface, .ForeColor = SettingsText, .AccessibleName = dialog.Text}
            results.TabIndex = 1
            Dim ids As String() = {}
            Dim refresh As Action =
                Sub()
                    ids = FindSettingsRows(query.Text)
                    results.BeginUpdate()
                    results.Items.Clear()
                    For Each id In ids
                        Dim row = modernSettingsRows.First(Function(r) r.Key = id)
                        Dim group = settingsGroups(rowGroups(id))
                        Dim pageId = group.GroupId.Split("/"c)(0)
                        Dim page = pageIds.First(Function(p) p.Value = pageId).Key
                        Dim value = SafeSettingsValue(row)
                        results.Items.Add(modernSettingsNavButtons(Tab_Control.TabPages.IndexOf(page)).Text & " / " & group.Caption & " / " & row.Title.Text & If(value.Length > 0, " - " & value, String.Empty))
                    Next
                    results.EndUpdate()
                    If ids.Length > 0 Then results.SelectedIndex = 0
                    status.Text = If(ids.Length = 0, Localization.T("Ничего не найдено. Измените запрос или откройте раздел слева."), Localization.T("Enter: перейти к настройке. Escape: закрыть поиск."))
                End Sub
            Dim choose As Action =
                Sub()
                    If results.SelectedIndex < 0 Then Return
                    selectedId = ids(results.SelectedIndex)
                    dialog.Close()
                End Sub
            AddHandler query.TextChanged, Sub() refresh()
            AddHandler query.KeyDown,
                Sub(sender, e)
                    If e.KeyCode = Keys.Down AndAlso results.Items.Count > 0 Then
                        results.Focus()
                        e.SuppressKeyPress = True
                    End If
                End Sub
            AddHandler results.DoubleClick, Sub() choose()
            AddHandler dialog.KeyDown,
                Sub(sender, e)
                    If e.KeyCode = Keys.Enter Then
                        choose()
                        e.SuppressKeyPress = True
                    ElseIf e.KeyCode = Keys.Escape Then
                        dialog.Close()
                        e.SuppressKeyPress = True
                    End If
                End Sub
            dialog.Controls.Add(results)
            dialog.Controls.Add(query)
            dialog.Controls.Add(status)
            refresh()
            AddHandler dialog.Shown, Sub() query.Focus()
            DialogLayout.ApplyAutoScale(dialog)
            DialogLayer.ShowViewerDialog(dialog, Me)
        End Using
        If selectedId IsNot Nothing Then RevealModernSetting(selectedId)
    End Sub
End Class
#End If
