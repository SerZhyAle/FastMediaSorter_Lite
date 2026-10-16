Option Strict On

Imports System.Threading

' Spec 230 T-3: the ONE place every door of the two clipboard functions goes through - the two main-window buttons, the two tray
' items, a desktop shortcut that woke a running manager over WM_COPYDATA, and the cold start that runs and exits.
'
'   - one operation at a time per process (item Q): a press while one runs is told so in a sentence, never queued;
'   - the words of a running operation (progress, "N %") and its one result paragraph are composed HERE from codes, in the
'     language the person has when they are shown;
'   - the person can cancel what runs;
'   - the events reach a subscriber on the thread that started the operation (the UI thread), so a window or a tray item
'     can use them directly.
' The coordinator owns no control and no clipboard: those are ClipboardExchange's and IClipboardPort's.

Friend NotInheritable Class ClipboardCoordinator

    Private ReadOnly _exchange As ClipboardExchange
    Private ReadOnly _offered As Func(Of Boolean)
    Private _busy As Integer
    Private _cts As CancellationTokenSource
    Private _isSend As Boolean
    Private _progress As String = ""
    Private _lastPercent As Integer = -1

    ''' <summary>The state changed: an operation began, its progress moved, or it ended.</summary>
    Public Event StateChanged As EventHandler

    ''' <summary>An operation ended: the one paragraph to show and whether it succeeded.</summary>
    Public Event Finished As EventHandler(Of ClipboardFinishedEventArgs)

    Private Shared _current As ClipboardCoordinator
    Private Shared ReadOnly _gate As New Object()

    ''' <summary>The process-wide coordinator: the window, the tray and a wake share it, so one press at a time holds across them.</summary>
    Public Shared ReadOnly Property Current As ClipboardCoordinator
        Get
            SyncLock _gate
                If _current Is Nothing Then _current = New ClipboardCoordinator(New ClipboardExchange(), AddressOf AnyAccount)
                Return _current
            End SyncLock
        End Get
    End Property

    Public Sub New(exchange As ClipboardExchange, offered As Func(Of Boolean))
        _exchange = exchange
        _offered = offered
    End Sub

    ''' <summary>True when the exchange client is available here AND the person holds at least one account. A pure read of two
    ''' registry values; with no account nothing is drawn and nothing is opened (spec 230 rule 9).</summary>
    Public Shared Function AnyAccount() As Boolean
        Try
            If Not ExchangeFeatures.IsEnabled() Then Return False
            Return ClipboardTargets.AnyConfigured(ExchangeAccountStore.CreateDefault(), ExchangeEntryStore.CreateDefault())
        Catch ex As Exception When TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is InvalidOperationException
            Return False
        End Try
    End Function

    ''' <summary>Whether the buttons, tray items and shortcuts are drawn at all.</summary>
    Public Function IsOffered() As Boolean
        Return _offered IsNot Nothing AndAlso _offered.Invoke()
    End Function

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Volatile.Read(_busy) <> 0
        End Get
    End Property

    ''' <summary>True while a send runs, False while a get runs (meaningful only while <see cref="IsRunning"/>).</summary>
    Public ReadOnly Property IsSending As Boolean
        Get
            Return _isSend
        End Get
    End Property

    ''' <summary>"Sending the clipboard.. 42 %" while an operation runs, else "".</summary>
    Public ReadOnly Property ProgressText As String
        Get
            Return Volatile.Read(_progress)
        End Get
    End Property

    ''' <summary>Asks the running operation to stop. Nothing is placed or stored by what is cancelled midway.</summary>
    Public Sub Cancel()
        Dim cts As CancellationTokenSource = _cts
        If cts Is Nothing Then Return
        Try
            cts.Cancel()
        Catch ex As ObjectDisposedException
            ' it ended between the read and the call
        End Try
    End Sub

    ''' <summary>Runs one send or one get and returns its result. Never throws for a clipboard, a server or a disk: those are lines.</summary>
    Public Async Function RunAsync(isSend As Boolean) As Task(Of ClipboardRunResult)
        Dim context As SynchronizationContext = SynchronizationContext.Current
        If Not IsOffered() Then
            ' Rule 9: not offered means nothing is read, even when a shortcut or a wake asks for it.
            Dim notOffered As New ClipboardRunResult({New ClipboardLine(ClipboardLineCode.NoServer)}, False)
            Dim said As New ClipboardFinishedEventArgs(isSend, ClipboardMessages.TextOf(notOffered), False)
            Raise(context, Sub() RaiseEvent Finished(Me, said))
            Return notOffered
        End If
        If Interlocked.CompareExchange(_busy, 1, 0) <> 0 Then
            ' Said, never queued: the one that runs keeps its state and its progress.
            Dim under As New ClipboardRunResult({New ClipboardLine(ClipboardLineCode.OperationUnderWay)}, False)
            Dim told As New ClipboardFinishedEventArgs(isSend, ClipboardMessages.TextOf(under), False)
            Raise(context, Sub() RaiseEvent Finished(Me, told))
            Return under
        End If
        Dim cts As New CancellationTokenSource()
        _cts = cts
        _isSend = isSend
        _lastPercent = -1
        Volatile.Write(_progress, ClipboardMessages.ProgressText(isSend, 0L, 0L))
        Raise(context, Sub() RaiseEvent StateChanged(Me, EventArgs.Empty))
        Dim result As ClipboardRunResult
        Try
            Dim report As Action(Of Long, Long) =
                Sub(done As Long, total As Long)
                    Dim percent As Integer = 0
                    If total > 0 Then percent = CInt(Math.Min(100L, Math.Max(0L, done * 100L \ total)))
                    ' Only a visible change is posted: a large value moves by the megabyte, the words by the percent.
                    If percent = Volatile.Read(_lastPercent) Then Return
                    Volatile.Write(_lastPercent, percent)
                    Volatile.Write(_progress, ClipboardMessages.ProgressText(isSend, done, total))
                    Raise(context, Sub() RaiseEvent StateChanged(Me, EventArgs.Empty))
                End Sub
            Try
                If isSend Then
                    result = Await _exchange.SendAsync(report, cts.Token)
                Else
                    result = Await _exchange.GetAsync(report, cts.Token)
                End If
            Catch ex As OperationCanceledException
                result = New ClipboardRunResult({New ClipboardLine(ClipboardLineCode.Cancelled)}, False)
            Catch ex As Exception
                ' A defect below must still end in a sentence, never in an unhandled exception on a tray app. The class is logged, not the text.
                AppFileLogger.WriteLine("Clipboard " & If(isSend, "send", "get") & ": failed (" & ex.GetType().Name & ")")
                result = New ClipboardRunResult({New ClipboardLine(ClipboardLineCode.ClipboardUnreadable)}, False)
            End Try
        Finally
            _cts = Nothing
            cts.Dispose()
            Volatile.Write(_progress, "")
            Volatile.Write(_busy, 0)
        End Try
        Dim finished As New ClipboardFinishedEventArgs(isSend, ClipboardMessages.TextOf(result), result.Succeeded)
        Raise(context, Sub()
                           RaiseEvent StateChanged(Me, EventArgs.Empty)
                           RaiseEvent Finished(Me, finished)
                       End Sub)
        Return result
    End Function

    ''' <summary>Runs the action on the thread the operation began on, or here when there is no context (a test).</summary>
    Private Shared Sub Raise(context As SynchronizationContext, action As Action)
        If context Is Nothing Then
            action.Invoke()
        Else
            context.Post(Sub(state) action.Invoke(), Nothing)
        End If
    End Sub

End Class

Friend NotInheritable Class ClipboardFinishedEventArgs
    Inherits EventArgs

    Public ReadOnly IsSend As Boolean
    ''' <summary>The result paragraph in the language shown now.</summary>
    Public ReadOnly Text As String
    Public ReadOnly Succeeded As Boolean

    Public Sub New(isSend As Boolean, text As String, succeeded As Boolean)
        Me.IsSend = isSend
        Me.Text = If(text, "")
        Me.Succeeded = succeeded
    End Sub

End Class
