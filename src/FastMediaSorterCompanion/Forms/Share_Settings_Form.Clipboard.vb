Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Spec 230 T-3: what the clipboard functions need from the settings window - the "Create desktop shortcuts.." command and the
''' folder a received file is saved into. They sit at the end of the exchange account group: with no account they are not drawn
''' (rule 9), and an account is what makes them mean anything.
'''
''' Nothing here runs by itself: the shortcuts are made only by the press (rule 2), and the folder is a plain setting
''' (<see cref="ClipboardSettings"/>) - empty means Downloads.
''' </summary>
Partial Public NotInheritable Class Share_Settings_Form

    ''' <summary>Every row of the clipboard block, so one call shows or hides the whole block.</summary>
    Private ReadOnly _clipRows As New List(Of Control)()
    Private _clipFolder As TextBox
    Private _clipResult As Label

    Private Sub BuildClipboardRows(sec As CollapsibleSection)
        AddClipboardRow(sec, New Label With {.AutoSize = True, .Margin = New Padding(0, 10, 0, 2), .Font = _accBoldFont,
                                              .Text = Localization.T("Буфер на сервере")})

        If AutostartManager.IsPackaged() Then
            ' Default 4: a packaged app has no execution alias here, so a desktop shortcut cannot carry the switch. The buttons and the
            ' tray items are what this build offers, and it says so.
            AddClipboardRow(sec, AccNote(Localization.T("В версии из Microsoft Store ярлыков нет: используйте кнопки в окне и пункты меню значка рядом с часами.")))
        Else
            AddClipboardRow(sec, AccNote(Localization.T("Два ярлыка на рабочем столе: отправить и получить буфер обмена одним щелчком, без окна.")))
            Dim shortcutsRow As FlowLayoutPanel = AccFlow()
            shortcutsRow.Controls.Add(AccButton(Localization.T("Создать ярлыки на рабочем столе.."), AddressOf OnClipboardShortcuts, "action.add"))
            AddClipboardRow(sec, shortcutsRow)
        End If

        Dim folderRow As FlowLayoutPanel = AccFlow()
        folderRow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 6, 6, 0),
                                               .Text = Localization.T("Папка для полученных файлов")})
        _clipFolder = New TextBox With {.Width = 260, .Margin = New Padding(0, 2, 6, 0), .Text = ClipboardSettings.StoredFolder()}
        AddHandler _clipFolder.Leave, Sub() CommitClipboardFolder()
        AddHandler _clipFolder.KeyDown,
            Sub(sender As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Enter Then
                    e.SuppressKeyPress = True
                    CommitClipboardFolder()
                End If
            End Sub
        folderRow.Controls.Add(_clipFolder)
        folderRow.Controls.Add(AccButton(Localization.T("Выбрать папку.."), AddressOf OnPickClipboardFolder, "action.choose-folder"))
        AddClipboardRow(sec, folderRow)
        AddClipboardRow(sec, AccNote(Localization.T("Пусто: файлы сохраняются в папку «Загрузки».")))

        _clipResult = AccNote("")
        _clipResult.Visible = False
        AddClipboardRow(sec, _clipResult)
    End Sub

    Private Sub AddClipboardRow(sec As CollapsibleSection, row As Control)
        _clipRows.Add(row)
        sec.AddBodyRow(row)
    End Sub

    ''' <summary>The block is drawn while an account exists, and not otherwise.</summary>
    Private Sub ApplyClipboardRows()
        If _clipRows.Count = 0 Then Return
        Dim offered As Boolean = ClipboardCoordinator.AnyAccount()
        For Each row As Control In _clipRows
            ' The result line keeps its own visibility (it shows only after a press).
            If row Is _clipResult Then
                If Not offered Then row.Visible = False
            Else
                row.Visible = offered
            End If
        Next
    End Sub

    Private Sub OnClipboardShortcuts()
        Dim result As ClipboardShortcutsResult = ClipboardShortcuts.CreateOnDesktop(
            Localization.T("Отправить буфер на сервер"), Localization.T("Получить буфер с сервера"), ProductNames.ShareManager)
        AppFileLogger.WriteLine("Clipboard shortcuts: created=" & result.Created.ToString() & " existing=" & result.Existing.ToString() &
                                " failed=" & result.Failed.ToString())
        If result.Failed > 0 Then
            ShowClipboardResult(Localization.T("Не удалось создать ярлыки на рабочем столе."), isProblem:=True)
        ElseIf result.Created > 0 Then
            ShowClipboardResult(Localization.TF("Создано ярлыков на рабочем столе: {0}.", result.Created), isProblem:=False)
        Else
            ShowClipboardResult(Localization.T("Ярлыки уже есть на рабочем столе."), isProblem:=False)
        End If
    End Sub

    Private Sub OnPickClipboardFolder()
        Using dialog As New FolderBrowserDialog()
            Dim current As String = ClipboardSettings.ReceivedFolder()
            If Directory.Exists(current) Then dialog.SelectedPath = current
            dialog.Description = Localization.T("Папка для полученных файлов")
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            _clipFolder.Text = dialog.SelectedPath
        End Using
        CommitClipboardFolder()
    End Sub

    ''' <summary>Saves the folder the box holds: empty is the default, a rooted path is kept, anything else goes back to what is
    ''' in force - a half-typed word never becomes a place files are written to.</summary>
    Private Sub CommitClipboardFolder()
        If _clipFolder Is Nothing Then Return
        Dim text As String = _clipFolder.Text.Trim()
        If text.Length = 0 OrElse (Path.IsPathRooted(text) AndAlso text.IndexOfAny(Path.GetInvalidPathChars()) < 0) Then
            ClipboardSettings.SaveReceivedFolder(text)
        End If
        _clipFolder.Text = ClipboardSettings.StoredFolder()
    End Sub

    Private Sub ShowClipboardResult(text As String, isProblem As Boolean)
        If _clipResult Is Nothing Then Return
        _clipResult.Text = text
        _clipResult.ForeColor = If(isProblem, CollapsibleSection.AttentionColor, ThemeService.Palette.TextMuted)
        _clipResult.Visible = True
        If _built Then _root.PerformLayout()
    End Sub

End Class
