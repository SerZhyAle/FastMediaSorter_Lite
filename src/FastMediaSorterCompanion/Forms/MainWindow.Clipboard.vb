Option Strict On

Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Spec 230 T-3: the two clipboard buttons of the Share Manager window - "Send the clipboard to the server" and "Get the
''' clipboard from the server" - and the cancel that appears while one runs.
'''
''' The window runs nothing itself: it asks the process-wide <see cref="ClipboardCoordinator"/> (shared with the tray items and a
''' shortcut's wake, so one operation at a time holds across all three) and shows what it reports - the progress words and the
''' one result paragraph - in the hint row. With no account the buttons are absent, not disabled (rule 9).
''' </summary>
Partial Public NotInheritable Class MainWindow

    Private btnClipboardMenu As Button
    Private _clipboardMenu As ContextMenuStrip
    Private mnuClipboardSend As ToolStripMenuItem
    Private mnuClipboardGet As ToolStripMenuItem
    Private mnuClipboardCancel As ToolStripMenuItem
    Private ReadOnly _clipboard As ClipboardCoordinator = ClipboardCoordinator.Current

    ''' <summary>The consolidated dropdown button for the list's button row, subscribed to the coordinator for the life of this window.</summary>
    Private Function BuildClipboardButtons() As Control()
        btnClipboardMenu = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(10, 5, 12, 5), .Text = Localization.T("Буфер обмена ▾"),
            .TextAlign = ContentAlignment.MiddleCenter, .Visible = False, .Margin = New Padding(0, 0, 8, 0)}
        ButtonGlyphs.Apply(btnClipboardMenu, "action.send-clipboard")
        btnClipboardMenu.Name = "btnClipboardMenu"

        _clipboardMenu = New ContextMenuStrip()
        mnuClipboardSend = New ToolStripMenuItem(Localization.T("Отправить буфер на сервер"))
        AddHandler mnuClipboardSend.Click, Sub() BeginClipboardAction(isSend:=True)

        mnuClipboardGet = New ToolStripMenuItem(Localization.T("Получить буфер с сервера"))
        AddHandler mnuClipboardGet.Click, Sub() BeginClipboardAction(isSend:=False)

        mnuClipboardCancel = New ToolStripMenuItem(Localization.T("Отмена"))
        AddHandler mnuClipboardCancel.Click, Sub() _clipboard.Cancel()

        btnClipboardMenu.ContextMenuStrip = _clipboardMenu
        _clipboardMenu.Items.Add(mnuClipboardSend)
        _clipboardMenu.Items.Add(mnuClipboardGet)
        _clipboardMenu.Items.Add(mnuClipboardCancel)

        AddHandler _clipboardMenu.Opening,
            Sub()
                Dim iconSize As Integer = LogicalToDeviceUnits(16)
                Dim opener As Func(Of String, System.IO.Stream) = AddressOf CompanionAssets.OpenBundledAsset
                RenderMenuItemGlyph(mnuClipboardSend, "action.upload", iconSize, opener)
                RenderMenuItemGlyph(mnuClipboardGet, "action.download", iconSize, opener)
                RenderMenuItemGlyph(mnuClipboardCancel, "action.cancel", iconSize, opener)
            End Sub

        AddHandler btnClipboardMenu.Click, Sub()
            If Not _clipboardMenu.Visible Then
                _clipboardMenu.Show(btnClipboardMenu, New Point(0, btnClipboardMenu.Height))
            End If
        End Sub

        AddHandler _clipboard.StateChanged, AddressOf OnClipboardStateChanged
        AddHandler _clipboard.Finished, AddressOf OnClipboardFinished
        AddHandler Disposed,
            Sub()
                RemoveHandler _clipboard.StateChanged, AddressOf OnClipboardStateChanged
                RemoveHandler _clipboard.Finished, AddressOf OnClipboardFinished
                Try
                    mnuClipboardSend?.Image?.Dispose()
                    mnuClipboardGet?.Image?.Dispose()
                    mnuClipboardCancel?.Image?.Dispose()
                    _clipboardMenu.Dispose()
                Catch : End Try
            End Sub

        RefreshClipboardButtons()
        Return New Control() {btnClipboardMenu}
    End Function

    ''' <summary>Drawn only while an account exists; the send and get wait while one operation runs, the cancel is enabled.</summary>
    Private Sub RefreshClipboardButtons()
        If btnClipboardMenu Is Nothing Then Return
        Dim offered As Boolean = _clipboard.IsOffered()
        Dim running As Boolean = _clipboard.IsRunning
        btnClipboardMenu.Visible = offered
        If mnuClipboardSend IsNot Nothing Then mnuClipboardSend.Enabled = Not running
        If mnuClipboardGet IsNot Nothing Then mnuClipboardGet.Enabled = Not running
        If mnuClipboardCancel IsNot Nothing Then mnuClipboardCancel.Enabled = running
        If running Then
            btnClipboardMenu.Text = Localization.T("Буфер обмена (передача..)")
        Else
            btnClipboardMenu.Text = Localization.T("Буфер обмена ▾")
        End If
    End Sub

    Private Sub BeginClipboardAction(isSend As Boolean)
        ' The coordinator never throws and tells a second press that one is under way.
        Dim t As Task = _clipboard.RunAsync(isSend)
    End Sub

    ''' <summary>True when this window is on screen to receive a sentence - the tray notifies only when it is not.</summary>
    Friend Function ShowsClipboardNotice() As Boolean
        Return Not IsDisposed AndAlso IsHandleCreated AndAlso Visible AndAlso WindowState <> FormWindowState.Minimized
    End Function

    Private Sub OnClipboardStateChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        RefreshClipboardButtons()
        If _clipboard.IsRunning AndAlso ShowsClipboardNotice() Then SetHint(_clipboard.ProgressText)
    End Sub

    Private Sub OnClipboardFinished(sender As Object, e As ClipboardFinishedEventArgs)
        If IsDisposed OrElse Not ShowsClipboardNotice() Then Return
        ' One line in the hint row (a line per server is joined); the whole paragraph is the tooltip.
        SetHint(e.Text.Replace(Environment.NewLine, " "))
        Try
            toolTip.SetToolTip(lblHint, e.Text)
        Catch ex As ObjectDisposedException
        End Try
    End Sub

End Class
