Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Spec 230: the cold start of a clipboard shortcut. The manager was not running, so the action runs ALONE - no tray manager,
''' no worker, no exchange agent, no window: one notification-area icon whose tooltip carries the progress and whose menu
''' cancels, then the one result line as a notification, then the process ends.
'''
''' The icon is the only surface a shortcut run has, and it has no window to put a sentence in; the person pressed one button on
''' the desktop and gets one sentence in return (rule 11).
''' </summary>
Friend NotInheritable Class ClipboardOneShot
    Inherits ApplicationContext

    ''' <summary>The longest the process waits after the result, so the notification can be read. A shell that suppresses
    ''' notifications never raises the closed event; this bound ends the process anyway.</summary>
    Private Const LingerMs As Integer = 9000
    Private Const NotifyTextLimit As Integer = 63

    Private ReadOnly _isSend As Boolean
    Private ReadOnly _alreadyRunning As Boolean
    Private ReadOnly _coordinator As ClipboardCoordinator
    Private ReadOnly _notifyIcon As NotifyIcon
    Private ReadOnly _icon As Icon
    Private _hIcon As IntPtr
    Private ReadOnly _linger As Timer
    Private _exitCode As Integer = 1
    Private _ended As Boolean

    ''' <param name="alreadyRunning">Another shortcut run holds the action: say so and end, start nothing.</param>
    Friend Shared Function Run(isSend As Boolean, alreadyRunning As Boolean) As Integer
        Using context As New ClipboardOneShot(isSend, alreadyRunning)
            Application.Run(context)
            Return context._exitCode
        End Using
    End Function

    Private Sub New(isSend As Boolean, alreadyRunning As Boolean)
        _isSend = isSend
        _alreadyRunning = alreadyRunning
        _coordinator = New ClipboardCoordinator(New ClipboardExchange(), AddressOf ClipboardCoordinator.AnyAccount)
        _icon = ShareIcons.CreateIcon(_hIcon)

        Dim menu As New ContextMenuStrip()
        Dim miCancel As New ToolStripMenuItem(Localization.T("Отмена"))
        AddHandler miCancel.Click, Sub() _coordinator.Cancel()
        menu.Items.Add(miCancel)

        _notifyIcon = New NotifyIcon() With {
            .Icon = _icon,
            .Text = Fit(Title()),
            .ContextMenuStrip = menu,
            .Visible = True
        }
        AddHandler _notifyIcon.BalloonTipClosed, Sub() EndNow()
        AddHandler _notifyIcon.BalloonTipClicked, Sub() EndNow()
        AddHandler _coordinator.StateChanged, AddressOf OnStateChanged
        AddHandler _coordinator.Finished, AddressOf OnFinished

        _linger = New Timer With {.Interval = LingerMs}
        AddHandler _linger.Tick, Sub() EndNow()
        AddHandler Application.Idle, AddressOf OnFirstIdle
    End Sub

    Private Function Title() As String
        Return If(_isSend, Localization.T("Отправить буфер на сервер"), Localization.T("Получить буфер с сервера"))
    End Function

    ''' <summary>A notification-area tooltip is 63 characters at most; a longer one throws.</summary>
    Private Shared Function Fit(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ProductNames.ShareManager
        Return If(text.Length > NotifyTextLimit, text.Substring(0, NotifyTextLimit), text)
    End Function

    ''' <summary>The message loop is running: do the one action (or say that another one holds it).</summary>
    Private Async Sub OnFirstIdle(sender As Object, e As EventArgs)
        RemoveHandler Application.Idle, AddressOf OnFirstIdle
        Try
            If _alreadyRunning Then
                Dim under As New ClipboardLine(ClipboardLineCode.OperationUnderWay)
                Notify(ClipboardMessages.TextOf(under), ToolTipIcon.Info)
                Return
            End If
            Await _coordinator.RunAsync(_isSend)
        Catch ex As Exception
            ' A defect must end the process, not leave an icon in the tray that never goes away.
            ' Rule 10: the class is logged, never the text or the stack (an exception message can carry a path or a file name).
            AppFileLogger.WriteLine("Clipboard one-shot: failed (" & ex.GetType().Name & ")")
            EndNow()
        End Try
    End Sub

    Private Sub OnStateChanged(sender As Object, e As EventArgs)
        If _ended Then Return
        Try
            Dim progress As String = _coordinator.ProgressText
            _notifyIcon.Text = Fit(If(progress.Length > 0, progress, Title()))
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException
            ' a tooltip that cannot be set is not worth an operation
        End Try
    End Sub

    Private Sub OnFinished(sender As Object, e As ClipboardFinishedEventArgs)
        If _ended Then Return
        _exitCode = If(e.Succeeded, 0, 1)
        Notify(e.Text, If(e.Succeeded, ToolTipIcon.Info, ToolTipIcon.Warning))
    End Sub

    ''' <summary>The one result line as a notification; the process lingers until it is read or the bound passes.</summary>
    Private Sub Notify(text As String, icon As ToolTipIcon)
        Try
            _notifyIcon.Text = Fit(ProductNames.ShareManager)
            _notifyIcon.ShowBalloonTip(LingerMs, ProductNames.ShareManager, If(text.Length > 250, text.Substring(0, 250), text), icon)
            _linger.Start()
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException
            EndNow()
        End Try
    End Sub

    Private Sub EndNow()
        If _ended Then Return
        _ended = True
        Try
            _linger.Stop()
            _notifyIcon.Visible = False
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
        End Try
        ExitThread()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            Try
                _linger.Dispose()
                _notifyIcon.Dispose()
            Catch ex As ObjectDisposedException
            End Try
            ' Icon.FromHandle does not own the HICON: dispose the Icon AND free the handle.
            ShareIcons.FreeIcon(_icon, _hIcon)
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
