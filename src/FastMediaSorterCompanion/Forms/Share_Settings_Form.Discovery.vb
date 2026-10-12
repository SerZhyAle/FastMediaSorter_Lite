Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private ReadOnly _headerCommands As New List(Of Button)()

    Private Sub BuildSettingsHeader()
        ' The fold buttons went with the fold (210 section 6): the groups they managed live
        ' in the left menu now, so the header keeps the search only.
        Dim tools As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .Padding = New Padding(4, 0, 4, 4)}
        AddSettingsHeaderCommand(tools, "managerSettingsSearch", AddressOf SearchManagerSettings)
        _pageHost.Controls.Add(tools)
        tools.SendToBack()
    End Sub

    Private Sub AddSettingsHeaderCommand(host As Control, name As String, action As Action)
        Dim meaning = IconMap.GetMeaning(name)
        Dim button As New Button With {.Name = name, .Size = New Size(44, 44),
            .AccessibleName = SettingsCommandCaption(name), .Margin = New Padding(0, 0, 8, 0)}
        toolTip.SetToolTip(button, button.AccessibleName)
        AddHandler button.Click, Sub() action()
        AddHandler button.Disposed, Sub() button.Image?.Dispose()
        _headerCommands.Add(button)
        host.Controls.Add(button)
    End Sub

    Private Shared Function SettingsCommandCaption(name As String) As String
        Return Localization.T("Поиск настроек")
    End Function

    Private Sub RenderSettingsHeader(palette As ThemePalette)
        For Each button In _headerCommands
            Dim oldImage = button.Image
            button.Image = GlyphRenderer.Render(IconMap.GetMeaning(button.Name).GlyphId, LogicalToDeviceUnits(20),
                SettingsNavIconPalette.ResolveInk("settingsNavGeneral", palette.IsDark,
                    {button.BackColor, palette.ControlHover}, palette.Accent, SystemInformation.HighContrast),
                AddressOf CompanionAssets.OpenBundledAsset)
            oldImage?.Dispose()
        Next
    End Sub

    Private NotInheritable Class SearchEntry
        Friend PageId As String
        Friend Group As SettingsNavGroup
        Friend Editor As Control
        Friend Title As String
        Friend Hint As String
        Friend PageTitle As String
        Public Overrides Function ToString() As String
            Return PageTitle & " / " & Group.Caption & " / " & Title
        End Function
    End Class

    Private Function SettingsSearchEntries() As List(Of SearchEntry)
        Dim entries As New List(Of SearchEntry)()
        For Each section In AllSettingsSections()
            For Each group In _pageGroups(section.Key)
                For Each row In group.Rows
                    For Each control In (New Control() {row}).Concat(Descendants(row))
                        Dim settingRow = TryCast(control, ManagerSettingRow)
                        If settingRow IsNot Nothing Then
                            entries.Add(New SearchEntry With {.PageId = section.Key, .Group = group, .Editor = settingRow.Editor,
                                .Title = settingRow.Caption.Text, .Hint = settingRow.Hint.Text, .PageTitle = SettingsPageCaption(section)})
                        End If
                        Dim check = TryCast(control, CheckBox)
                        If check IsNot Nothing Then
                            entries.Add(New SearchEntry With {.PageId = section.Key, .Group = group, .Editor = check,
                                .Title = check.Text, .Hint = check.AccessibleDescription, .PageTitle = SettingsPageCaption(section)})
                        End If
                    Next
                Next
            Next
        Next
        Return entries
    End Function

    Private Sub SearchManagerSettings()
        Dim entries = SettingsSearchEntries()
        Using dialog As New Form With {.Text = Localization.T("Поиск настроек"), .ShowInTaskbar = False,
            .MinimizeBox = False, .MaximizeBox = False, .StartPosition = FormStartPosition.CenterParent,
            .ClientSize = New Size(560, 360)},
            query As New TextBox With {.Dock = DockStyle.Top, .AccessibleName = Localization.T("Поиск настроек")},
            results As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .HorizontalScrollbar = True,
                .AccessibleName = Localization.T("Поиск настроек")},
            closeButton As New Button With {.Dock = DockStyle.Bottom, .Height = 44, .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel}
            dialog.SuspendLayout()
            UiLanguage.ApplyTo(dialog)
            dialog.Controls.Add(results)
            dialog.Controls.Add(query)
            dialog.Controls.Add(closeButton)
            ButtonGlyphs.Apply(closeButton, "nav.close", ButtonGlyphRole.Dismissing)
            dialog.CancelButton = closeButton
            Dim fill As Action =
                Sub()
                    results.BeginUpdate()
                    Try
                        results.Items.Clear()
                        For Each entry In entries
                            If (entry.Title & " " & entry.Hint & " " & entry.PageTitle).IndexOf(query.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0 Then
                                results.Items.Add(entry)
                            End If
                        Next
                        If results.Items.Count > 0 Then results.SelectedIndex = 0
                    Finally
                        results.EndUpdate()
                    End Try
                End Sub
            AddHandler query.TextChanged, Sub() fill()
            Dim reveal As Action =
                Sub()
                    Dim entry = TryCast(results.SelectedItem, SearchEntry)
                    If entry Is Nothing Then Return
                    dialog.DialogResult = DialogResult.OK
                End Sub
            AddHandler results.DoubleClick, Sub() reveal()
            AddHandler dialog.KeyDown,
                Sub(sender, e)
                    If e.KeyCode = Keys.Enter Then
                        e.SuppressKeyPress = True
                        reveal()
                    End If
                End Sub
            dialog.KeyPreview = True
            fill()
            DpiLayout.ApplyAutoScale(dialog)
            dialog.ResumeLayout(True)
            ThemeService.Attach(dialog)
            DpiLayout.ClampToWorkingArea(dialog)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim chosen = DirectCast(results.SelectedItem, SearchEntry)
            ' The same path a subordinate-item click uses (210 section 5.4): select the
            ' page, scroll the block's heading to the top, focus the editor.
            RevealSettingsBlock(chosen.PageId, chosen.Group, chosen.Editor)
        End Using
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef message As Message, keyData As Keys) As Boolean
        If keyData = Keys.Escape Then
            Close()
            Return True
        End If
        If keyData = (Keys.Control Or Keys.F) Then
            SearchManagerSettings()
            Return True
        End If
        If keyData = (Keys.Control Or Keys.Tab) OrElse keyData = (Keys.Control Or Keys.Shift Or Keys.Tab) Then
            Dim ids = _pages.Keys.ToArray()
            Dim delta = If((keyData And Keys.Shift) = Keys.Shift, -1, 1)
            Dim index = (Array.IndexOf(ids, _selectedPage) + ids.Length + delta) Mod ids.Length
            SelectSettingsPage(ids(index))
            _navButtons(ids(index)).Focus()
            Return True
        End If
        Return MyBase.ProcessCmdKey(message, keyData)
    End Function
End Class
