Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Sign-in, calls and the end of the session of one remote administration window, with no control in it (L0278 5.3, 5.6, 5.8): the
''' window renders <see cref="State"/> and sends the person's clicks here, so every phase of 5.6 is reachable from a test.
'''
''' Every member is called on the thread that made the object (the window's); the awaits come back to it. The one exception to the
''' session's own threading is the password: the session asks for it on a pool thread, and <see cref="TakePassword"/> carries the
''' question to this thread, because the field is read there and that read is the moment the phase moves to SigningIn.
'''
''' No keep-alive, no timer, no second sign-in by itself: the network is touched because the person clicked (5.5, 5.8).
''' </summary>
Friend NotInheritable Class RemoteAdminController
    Implements IDisposable

    Private Shared ReadOnly RateLimitPause As TimeSpan = TimeSpan.FromSeconds(1)

    Private ReadOnly _targets As ExchangeAdminTargets
    Private ReadOnly _handshake As IExchangeAdminHandshake
    Private ReadOnly _clock As Func(Of Long)
    Private ReadOnly _delay As Func(Of TimeSpan, CancellationToken, Task)
    Private ReadOnly _log As Action(Of String)
    Private ReadOnly _owner As SynchronizationContext

    Private _state As RemoteAdminState = RemoteAdminState.Initial()
    Private _ops As RemoteAdminOps
    Private _signInCts As CancellationTokenSource
    Private _attempt As Long
    Private _readPassword As Func(Of Char())
    Private _askedTrust As ExchangeRemoteAdminSignIn
    Private _stopped As ExchangeRemoteAdminSignIn
    Private _signInHeld As Boolean
    Private _holdNumber As Long
    Private _disposed As Boolean
    Private ReadOnly _oneCall As New SemaphoreSlim(1, 1)

    ' The goodbyes still on their way, of every window of this process: the application's exit waits for them (5.8).
    Private Shared ReadOnly EndingsGate As New Object()
    Private Shared ReadOnly Endings As New List(Of Task)()

    ''' <summary>Raised on the owner's thread after every change of <see cref="State"/> (a new phase, target or reason line).</summary>
    Public Event StateChanged As EventHandler

    Public Sub New(targets As ExchangeAdminTargets, handshake As IExchangeAdminHandshake, clock As Func(Of Long),
                   delay As Func(Of TimeSpan, CancellationToken, Task), log As Action(Of String))
        If targets Is Nothing Then Throw New ArgumentNullException(NameOf(targets))
        If handshake Is Nothing Then Throw New ArgumentNullException(NameOf(handshake))
        If clock Is Nothing Then Throw New ArgumentNullException(NameOf(clock))
        If delay Is Nothing Then Throw New ArgumentNullException(NameOf(delay))
        _targets = targets
        _handshake = handshake
        _clock = clock
        _delay = delay
        _log = log
        _owner = SynchronizationContext.Current
    End Sub

    ''' <summary>The product's controller: the registry store, the real handshake, the real clock, the file log.</summary>
    Public Shared Function CreateDefault() As RemoteAdminController
        Dim watch As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
        Return New RemoteAdminController(ExchangeAdminTargets.CreateDefault(), ExchangeAdminHandshake.Instance,
                                         Function() watch.ElapsedMilliseconds,
                                         Function(span As TimeSpan, ct As CancellationToken) Task.Delay(span, ct),
                                         Sub(line As String) AppFileLogger.LogStamped(line))
    End Function

    ''' <summary>The one state object of the window (5.3).</summary>
    Public ReadOnly Property State As RemoteAdminState
        Get
            Return _state
        End Get
    End Property

    Public ReadOnly Property Targets As ExchangeAdminTargets
        Get
            Return _targets
        End Get
    End Property

    ''' <summary>The first contact waiting for the person's answer while the phase is AskingTrust, otherwise Nothing: its
    ''' <see cref="ExchangeRemoteAdminSignIn.Fingerprint"/> is what the inline panel shows in full.</summary>
    Public ReadOnly Property AskedTrust As ExchangeRemoteAdminSignIn
        Get
            Return _askedTrust
        End Get
    End Property

    ''' <summary>The last sign-in stopped by a changed certificate, until the next sign-in or target: both fingerprints for the
    ''' stop panel (5.4 rule 5). Otherwise Nothing.</summary>
    Public ReadOnly Property StoppedByCertificate As ExchangeRemoteAdminSignIn
        Get
            Return _stopped
        End Get
    End Property

    ''' <summary>True for the second after a rate-limited refusal: Sign in stays off, a courtesy and not a retry (5.7).</summary>
    Public ReadOnly Property SignInHeld As Boolean
        Get
            Return _signInHeld
        End Get
    End Property

    Private Sub Changed()
        If Not _disposed Then RaiseEvent StateChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub Apply(ev As RemoteAdminEvent, Optional reason As String = "")
        _state.Apply(ev, reason)
        Changed()
    End Sub

    Private Sub LogEvent(text As String)
        If _log Is Nothing Then Return
        Try
            _log.Invoke("remote-admin: " & text)
        Catch ex As Exception
            ' a logger that throws has nowhere to report to and must not break the window
        End Try
    End Sub

    ' ---------------------------------------------------------------------------------------------- the target

    ''' <summary>A target switch: the old session is ended first and a new state object replaces the old one, so a reply still on its way
    ''' carries an older generation and is dropped. Nothing is a return to NoTarget. The same target again changes nothing.</summary>
    Public Async Function ChooseTargetAsync(target As ExchangeAdminTarget) As Task
        If _disposed Then Return
        Dim held As ExchangeAdminTarget = _state.Target
        If target IsNot Nothing AndAlso held IsNot Nothing AndAlso String.Equals(target.Id, held.Id, StringComparison.Ordinal) Then Return
        If target Is Nothing AndAlso held Is Nothing Then Return
        Dim ending As Task = LeaveCurrent()
        _state = RemoteAdminState.ForTarget(_state, target)
        _stopped = Nothing
        _signInHeld = False
        _holdNumber += 1
        Changed()
        Await ending
    End Function

    ''' <summary>Reads the current target from the store again after its dialog saved or forgot something; the session is not touched, because
    ''' the dialog opens only while signed out. A target that is gone is a return to NoTarget.</summary>
    Public Async Function ReloadTargetAsync() As Task
        If _disposed OrElse _state.Target Is Nothing Then Return
        Dim fresh As ExchangeAdminTarget = _targets.Find(_state.Target.Id)
        If fresh Is Nothing Then
            Await ChooseTargetAsync(Nothing)
            Return
        End If
        _state.ReplaceTarget(fresh)
        _stopped = Nothing
        Changed()
    End Function

    ' Starts ending whatever the current state holds and detaches it from the state: a sign-in in flight is cancelled and its result
    ' dropped (the attempt number moves on), a live session gets its `bye`.
    Private Function LeaveCurrent() As Task
        _attempt += 1
        Dim cts As CancellationTokenSource = _signInCts
        If cts IsNot Nothing Then cts.Cancel()
        _askedTrust = Nothing
        _readPassword = Nothing
        _ops = Nothing
        Dim session As ExchangeRemoteAdminSession = _state.Session
        If session Is Nothing Then Return Task.CompletedTask
        _state.SetSession(Nothing)
        Return EndAndDisposeAsync(session)
    End Function

    Private Function EndAndDisposeAsync(session As ExchangeRemoteAdminSession) As Task
        Dim ending As Task = RunEndAsync(session)
        If ending.IsCompleted Then Return ending
        SyncLock EndingsGate
            Endings.Add(ending)
        End SyncLock
        Dim forgotten As Task = ending.ContinueWith(
            Sub(done As Task)
                SyncLock EndingsGate
                    Endings.Remove(done)
                End SyncLock
            End Sub, TaskScheduler.Default)
        Return ending
    End Function

    Private Async Function RunEndAsync(session As ExchangeRemoteAdminSession) As Task
        Try
            Await session.EndAsync().ConfigureAwait(False)
        Catch ex As Exception
            LogEvent("end " & ex.GetType().Name)
        Finally
            session.Dispose()
        End Try
    End Function

    ''' <summary>The application exits (5.8): waits for every `bye` still on its way, at most <paramref name="limit"/>, on any thread: an
    ''' ending never needs the window's. True when none is left. A process with no ending returns at once.</summary>
    Friend Shared Function WaitForEndings(limit As TimeSpan) As Boolean
        Dim waiting As Task()
        SyncLock EndingsGate
            waiting = Endings.ToArray()
        End SyncLock
        If waiting.Length = 0 Then Return True
        Try
            Return Task.WaitAll(waiting, limit)
        Catch ex As AggregateException
            ' a Dispose that threw: that ending is over all the same, and the exit goes on
            Return True
        End Try
    End Function

    ' ---------------------------------------------------------------------------------------------- the sign-in

    ''' <summary>The Sign in button. <paramref name="readPassword"/> is called once, on this thread, only after the certificate decision
    ''' accepts (5.4 rule 1): it reads the masked field into a new array and clears the field. The session clears the array.</summary>
    Public Async Function SignInAsync(readPassword As Func(Of Char())) As Task
        If readPassword Is Nothing Then Throw New ArgumentNullException(NameOf(readPassword))
        If _disposed OrElse _signInHeld OrElse Not _state.CanSignIn Then Return
        _readPassword = readPassword
        _stopped = Nothing
        Apply(RemoteAdminEvent.SignInPressed)
        Await RunSignInAsync()
    End Function

    Private Async Function RunSignInAsync() As Task
        _attempt += 1
        Dim attempt As Long = _attempt
        Dim target As ExchangeAdminTarget = _state.Target
        Dim session As New ExchangeRemoteAdminSession(_handshake, _clock, _delay, _log)
        Dim cts As New CancellationTokenSource()
        _signInCts = cts
        Dim result As ExchangeRemoteAdminSignIn = Nothing
        Try
            result = Await session.SignInAsync(target.Host, target.Port, target.LeafPin, target.KeyPin, Function() TakePassword(attempt), cts.Token)
        Catch ex As Exception
            LogEvent("sign-in " & ex.GetType().Name)
        Finally
            If _signInCts Is cts Then _signInCts = Nothing
            cts.Dispose()
        End Try
        If _disposed OrElse attempt <> _attempt Then
            ' the window left or moved on while this was in flight: the answer describes nothing on screen any more
            Dim dropped As Task = EndAndDisposeAsync(session)
            Return
        End If
        If result Is Nothing OrElse result.Kind <> ExchangeRemoteAdminSignInKind.SignedIn Then session.Dispose()
        If result Is Nothing Then
            Apply(RemoteAdminEvent.SignInFailed, RemoteAdminWords.ForSignIn(ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, "")))
            Return
        End If
        Select Case result.Kind
            Case ExchangeRemoteAdminSignInKind.SignedIn
                _state.SetSession(session)
                _ops = New RemoteAdminOps(session)
                _targets.Touch(target.Id)
                Apply(RemoteAdminEvent.SignInOk)
            Case ExchangeRemoteAdminSignInKind.NeedsFirstContact
                _askedTrust = result
                Apply(RemoteAdminEvent.TrustAsked)
            Case ExchangeRemoteAdminSignInKind.Disabled
                Apply(RemoteAdminEvent.SignInDisabled, RemoteAdminWords.ForSignIn(result))
            Case ExchangeRemoteAdminSignInKind.Cancelled
                Apply(RemoteAdminEvent.Cancelled)
            Case ExchangeRemoteAdminSignInKind.CertificateChanged
                _stopped = result
                Apply(RemoteAdminEvent.SignInFailed, RemoteAdminWords.ForSignIn(result))
            Case Else
                Apply(RemoteAdminEvent.SignInFailed, RemoteAdminWords.ForSignIn(result))
                If result.Kind = ExchangeRemoteAdminSignInKind.Refused AndAlso result.Refusal = ExchangeRefusal.RateLimited Then
                    Dim pause As Task = HoldSignInAsync()
                End If
        End Select
    End Function

    Private Async Function HoldSignInAsync() As Task
        _holdNumber += 1
        Dim mine As Long = _holdNumber
        _signInHeld = True
        Changed()
        Try
            Await _delay.Invoke(RateLimitPause, CancellationToken.None)
        Catch ex As OperationCanceledException
            ' an injected delay that was stopped ends the pause too
        End Try
        If mine <> _holdNumber Then Return
        _signInHeld = False
        Changed()
    End Function

    ' Called by the session on a pool thread, after the decision accepted. An attempt that moved on gets no password: the session then
    ' refuses the empty answer, closes the connection, and the result is dropped by RunSignInAsync.
    Private Function TakePassword(attempt As Long) As Char()
        Dim chars As Char() = Nothing
        Dim take As SendOrPostCallback =
            Sub(ignored As Object)
                If _disposed OrElse attempt <> _attempt OrElse _state.Phase <> RemoteAdminPhase.CheckingServer Then Return
                Dim read As Func(Of Char()) = _readPassword
                _readPassword = Nothing
                If read Is Nothing Then Return
                chars = read.Invoke()
                Apply(RemoteAdminEvent.DecisionAccepted)
            End Sub
        If _owner Is Nothing OrElse SynchronizationContext.Current Is _owner Then
            take.Invoke(Nothing)
        Else
            _owner.Send(take, Nothing)
        End If
        Return chars
    End Function

    ''' <summary>The Cancel button of CheckingServer and SigningIn: the stream is closed, the phase returns to SignedOut with no message
    ''' (cancellation is an outcome, not an error). The field keeps what was typed when the password was not read yet.</summary>
    Public Sub CancelSignIn()
        If _disposed OrElse Not _state.CanCancel Then Return
        Dim cts As CancellationTokenSource = _signInCts
        If cts IsNot Nothing Then cts.Cancel()
    End Sub

    ''' <summary>The answer to the inline first-contact panel. A Yes writes the pins first (5.4 rule 4) and then signs in again on a new
    ''' connection; a No writes nothing and the phase returns to SignedOut.</summary>
    Public Async Function AnswerTrustAsync(accept As Boolean) As Task
        If _disposed OrElse _state.Phase <> RemoteAdminPhase.AskingTrust Then Return
        Dim asked As ExchangeRemoteAdminSignIn = _askedTrust
        _askedTrust = Nothing
        If Not accept OrElse asked Is Nothing OrElse Not _targets.SetPins(_state.Target.Id, asked.Fingerprint, asked.KeyPin) Then
            _readPassword = Nothing
            Apply(RemoteAdminEvent.TrustDeclined)
            Return
        End If
        Dim fresh As ExchangeAdminTarget = _targets.Find(_state.Target.Id)
        If fresh Is Nothing Then
            Await ChooseTargetAsync(Nothing)
            Return
        End If
        _state.ReplaceTarget(fresh)
        Apply(RemoteAdminEvent.TrustAccepted)
        Await RunSignInAsync()
    End Function

    ' ---------------------------------------------------------------------------------------------- the calls

    ''' <summary>One operation of a card. Returns Nothing when the answer must not be painted: there is no live session, or the target moved
    ''' on while the request was out (a stale generation). A lapse, a broken stream or an out-of-step frame moves the phase to SignedOut with
    ''' its sentence as the reason line (the failed action is not repeated, 5.5); the result still goes back so the card can keep its lists
    ''' as the last known state.</summary>
    Public Async Function CallAsync(Of TResult As RemoteAdminResult)(work As Func(Of RemoteAdminOps, CancellationToken, Task(Of TResult)),
                                                                     Optional isMutation As Boolean = False) As Task(Of TResult)
        If work Is Nothing Then Throw New ArgumentNullException(NameOf(work))
        If _disposed OrElse _ops Is Nothing OrElse _state.Phase <> RemoteAdminPhase.SignedIn Then Return Nothing
        Dim generation As Long = _state.Generation
        ' The session takes one request at a time and answers a second one Busy, which has no words: two cards, or a change during a read, queue here.
        Await _oneCall.WaitAsync()
        Try
            ' The wait may have outlived the session or the target: nothing is sent then.
            Dim ops As RemoteAdminOps = _ops
            If _disposed OrElse ops Is Nothing OrElse Not _state.Accepts(generation) OrElse _state.Phase <> RemoteAdminPhase.SignedIn Then Return Nothing
            Dim session As ExchangeRemoteAdminSession = _state.Session
            Dim result As TResult = Await work.Invoke(ops, CancellationToken.None)
            If _disposed OrElse Not _state.Accepts(generation) OrElse _state.Session IsNot session Then Return Nothing
            If session IsNot Nothing AndAlso Not session.IsLive Then
                _ops = Nothing
                _state.SetSession(Nothing)
                session.Dispose()
                Apply(RemoteAdminEvent.SessionEnded, RemoteAdminWords.ForOutcome(result.Outcome, isMutation))
            End If
            Return result
        Finally
            _oneCall.Release()
        End Try
    End Function

    ' ---------------------------------------------------------------------------------------------- leaving

    ''' <summary>Sign out, a lock, a logoff, a disconnect, a suspend (5.8): a sign-in in flight is cancelled, a session ends with `bye` (at most
    ''' 1500 ms, the session's bound), and the phase returns to SignedOut with <paramref name="reason"/> as the reason line.</summary>
    Public Async Function EndSessionAsync(Optional reason As String = "") As Task
        If _disposed Then Return
        Dim phase As RemoteAdminPhase = _state.Phase
        Dim ending As Task = LeaveCurrent()
        Select Case phase
            Case RemoteAdminPhase.SignedIn : Apply(RemoteAdminEvent.SessionEnded, reason)
            Case RemoteAdminPhase.AskingTrust : Apply(RemoteAdminEvent.TrustDeclined, reason)
            Case RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn : Apply(RemoteAdminEvent.Cancelled)
        End Select
        Await ending
    End Function

    ''' <summary>The window closes or the application exits: the only thing done on the network is the `bye` of this stream, which runs on after
    ''' this returns under the session's own bound. Nothing is raised after this. Safe to call twice.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        Dim ending As Task = LeaveCurrent()
        _disposed = True
    End Sub

End Class
