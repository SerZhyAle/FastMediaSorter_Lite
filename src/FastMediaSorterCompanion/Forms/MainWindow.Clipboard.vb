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

    Private btnClipboardSend As Button
    Private btnClipboardGet As Button
    Private btnClipboardCancel As Button
    Private ReadOnly _clipboard As ClipboardCoordinator = ClipboardCoordinator.Current

    ''' <summary>The three buttons for the list's button row, subscribed to the coordinator for the life of this window.</summary>
    Private Function BuildClipboardButtons() As Control()
        btnClipboardSend = NewClipboardButton(Localization.T("Отправить буфер на сервер"), IconMap.GetMeaning("btnClipboardSend").GlyphId)
        btnClipboardGet = NewClipboardButton(Localization.T("Получить буфер с сервера"), IconMap.GetMeaning("btnClipboardGet").GlyphId)
        btnClipboardCancel = NewClipboardButton(Localization.T("Отмена"), "action.cancel")
        btnClipboardSend.Name = "btnClipboardSend"
        btnClipboardGet.Name = "btnClipboardGet"
        AddHandler btnClipboardSend.Click, Sub() BeginClipboardAction(isSend:=True)
        AddHandler btnClipboardGet.Click, Sub() BeginClipboardAction(isSend:=False)
        AddHandler btnClipboardCancel.Click, Sub() _clipboard.Cancel()

        AddHandler _clipboard.StateChanged, AddressOf OnClipboardStateChanged
        AddHandler _clipboard.Finished, AddressOf OnClipboardFinished
        AddHandler Disposed,
            Sub()
                RemoveHandler _clipboard.StateChanged, AddressOf OnClipboardStateChanged
                RemoveHandler _clipboard.Finished, AddressOf OnClipboardFinished
            End Sub

        RefreshClipboardButtons()
        Return New Control() {btnClipboardSend, btnClipboardGet, btnClipboardCancel}
    End Function

    Private Shared Function NewClipboardButton(caption As String, glyph As String) As Button
        Dim button As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(10, 5, 12, 5), .Text = caption, .TextAlign = ContentAlignment.MiddleCenter, .Visible = False}
        ButtonGlyphs.Apply(button, glyph)
        Return button
    End Function

    ''' <summary>Drawn only while an account exists; the send and get wait while one operation runs, the cancel appears.</summary>
    Private Sub RefreshClipboardButtons()
        If btnClipboardSend Is Nothing Then Return
        Dim offered As Boolean = _clipboard.IsOffered()
        Dim running As Boolean = _clipboard.IsRunning
        btnClipboardSend.Visible = offered
        btnClipboardGet.Visible = offered
        btnClipboardSend.Enabled = Not running
        btnClipboardGet.Enabled = Not running
        btnClipboardCancel.Visible = offered AndAlso running
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
