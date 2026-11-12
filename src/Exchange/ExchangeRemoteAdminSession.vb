#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Net.Sockets
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Ticket L0278 T-3 (slice B), specification 5.3 - 5.5 and 5.8. The session of the remote administration client: one TLS stream to one
' exchange server, the trust decision of 5.4 before any byte, ONE admin-auth, a token kept in this object only, one request in flight,
' and the end of the session with `bye`. It moves frames and returns kinds and codes; it holds NO user word and NO parser of `data`
' (the Companion composes the sentences and reads `data` into views).
'
' What is injected, so no test waits and none touches the system: `Clock` (monotonic milliseconds, used for the duration in the log
' line only), `Delay` (the timeouts of 5.5 - the session never sleeps by itself), `Log` (one line per event) and the handshake seam
' IExchangeAdminHandshake of ExchangeAdminTrust (a pinned connect and a probe; the real one is ExchangeAdminHandshake.Instance).
' No Task.Delay, DateTime.UtcNow, Stopwatch or static log call is inside this class.
'
' Rules this class keeps (each has a test):
'   - The password is asked for ONCE, through a delegate, and only after the trust decision is an Accept and the connection that will
'     carry it has passed that decision again (a first-contact question happens while the field still holds the text). The Char() the
'     delegate returns is cleared right after the write and in a Finally on every other path; the frame buffer is wiped by the writer.
'   - Exactly one admin-auth per SignInAsync. Nothing in this class signs in again by itself (RA:52): a lapse is learned, reported, and
'     the next request returns the same kind without writing a byte.
'   - Frame limits: 16384 until admin-auth-ok, 262144 after (MarkAuthenticated on the reader AND the writer).
'   - An end of stream is Lapsed, a socket reset is Unreachable, `refused{bad-credentials}` after sign-in is Lapsed (the server reuses
'     the reason for a lapsed token), any other refusal after sign-in, a wrong type or a malformed frame is Protocol.
'   - Log lines are `remote-admin: <event> <kind> <ms>` and nothing else: <event> is connect, trust, sign-in, call or end, <kind> an
'     outcome kind or an operation name of the 18, <ms> a duration. No host, port, name, login, password, token, fingerprint,
'     invitation or reply body ever reaches the log; an unexpected exception is named by its type only, inside Detail.

''' <summary>What a call came to (5.3).</summary>
Friend Enum ExchangeRemoteAdminOutcomeKind
    ''' <summary>`ok:true`; <see cref="ExchangeRemoteAdminOutcome.Data"/> holds the body when the reply carried one.</summary>
    Done = 1
    ''' <summary>`ok:false` with one of the server's codes.</summary>
    Failed = 2
    ''' <summary>`refused{bad-credentials}` after sign-in, a clean end of stream, or a call on a session that has ended.</summary>
    Lapsed = 3
    ''' <summary>Connect, TLS, timeout, reset, IO or a cancel; see <see cref="ExchangeRemoteAdminOutcome.Reason"/>.</summary>
    Unreachable = 4
    ''' <summary>A malformed frame, a wrong type, too many frames of another id, a refusal that is not bad-credentials, a request that
    ''' could not be built, or an `ok:true` reply whose body does not have the shape of the operation.</summary>
    Protocol = 5
    ''' <summary>A call arrived while another was in flight; nothing was written (a UI bug, never a message).</summary>
    Busy = 6
End Enum

''' <summary>Why a sign-in or a call is Unreachable. The words belong to the Companion.</summary>
Friend Enum ExchangeRemoteAdminUnreachableReason
    ''' <summary>Not an Unreachable result.</summary>
    None = 0
    ''' <summary>No TCP connection.</summary>
    Network = 1
    ''' <summary>The handshake failed for a reason other than trust.</summary>
    TlsFailed = 2
    ''' <summary>The connect (15 s), the sign-in reply (20 s) or the request reply (30 s) ran out of time.</summary>
    Timeout = 3
    ''' <summary>The connection broke: a reset, an IO error, or the stream ended inside a frame.</summary>
    Reset = 4
    ''' <summary>The stream ended with no frame in answer to the sign-in (off, address not allowed, or an older server: 3.1).</summary>
    Unanswered = 5
    ''' <summary>The answer to the sign-in was not understood.</summary>
    Protocol = 6
    ''' <summary>The caller cancelled (a call whose token was cancelled, or a call cut short by <see cref="ExchangeRemoteAdminSession.EndAsync"/>).</summary>
    Cancelled = 7
End Enum

''' <summary>The result of one call. Disposing it releases the body (<see cref="Data"/> is a clone, valid until then).</summary>
Friend NotInheritable Class ExchangeRemoteAdminOutcome
    Implements IDisposable

    Private _data As JsonElement
    Private _hasData As Boolean

    Public ReadOnly Property Kind As ExchangeRemoteAdminOutcomeKind
    ''' <summary>The server's failure code of a Failed result (one of the ten, or a newer one); "" otherwise.</summary>
    Public ReadOnly Property Code As String
    ''' <summary>Why an Unreachable result is one; None for every other kind.</summary>
    Public ReadOnly Property Reason As ExchangeRemoteAdminUnreachableReason
    ''' <summary>A bounded internal reason for the log and the tests, taken from a fixed vocabulary or an exception TYPE name - never from the
    ''' wire and never shown to a person.</summary>
    Public ReadOnly Property Detail As String

    Private Sub New(kind As ExchangeRemoteAdminOutcomeKind, code As String, reason As ExchangeRemoteAdminUnreachableReason, detail As String, hasData As Boolean, data As JsonElement)
        Me.Kind = kind
        Me.Code = If(code, "")
        Me.Reason = reason
        Me.Detail = If(detail, "")
        _hasData = hasData
        _data = data
    End Sub

    ''' <summary>True for a Done result that carried a `data` body (and until this outcome is disposed).</summary>
    Public ReadOnly Property HasData As Boolean
        Get
            Return _hasData
        End Get
    End Property

    ''' <summary>The body of a Done result: an object or an array, a clone that does not depend on the frame it came from.
    ''' Undefined when <see cref="HasData"/> is False.</summary>
    Public ReadOnly Property Data As JsonElement
        Get
            Return _data
        End Get
    End Property

    Friend Shared Function NewDone(hasData As Boolean, data As JsonElement) As ExchangeRemoteAdminOutcome
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Done, "", ExchangeRemoteAdminUnreachableReason.None, "", hasData, data)
    End Function

    Friend Shared Function NewFailed(code As String) As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Failed, code, ExchangeRemoteAdminUnreachableReason.None, "", False, none)
    End Function

    Friend Shared Function NewLapsed(detail As String) As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Lapsed, "", ExchangeRemoteAdminUnreachableReason.None, detail, False, none)
    End Function

    Friend Shared Function NewUnreachable(reason As ExchangeRemoteAdminUnreachableReason, detail As String) As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Unreachable, "", reason, detail, False, none)
    End Function

    Friend Shared Function NewProtocol(detail As String) As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Protocol, "", ExchangeRemoteAdminUnreachableReason.None, detail, False, none)
    End Function

    Friend Shared Function NewBusy() As ExchangeRemoteAdminOutcome
        Dim none As JsonElement = Nothing
        Return New ExchangeRemoteAdminOutcome(ExchangeRemoteAdminOutcomeKind.Busy, "", ExchangeRemoteAdminUnreachableReason.None, "", False, none)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _hasData = False
        _data = Nothing
    End Sub

    ''' <summary>The kind, the code and the reason; never the body.</summary>
    Public Overrides Function ToString() As String
        Return "remote-admin " & Kind.ToString() & If(Code.Length > 0, " " & Code, "") & If(Reason <> ExchangeRemoteAdminUnreachableReason.None, " " & Reason.ToString(), "")
    End Function

End Class

''' <summary>What a sign-in came to (5.3).</summary>
Friend Enum ExchangeRemoteAdminSignInKind
    ''' <summary>`admin-auth-ok`: <see cref="ExchangeRemoteAdminSignIn.Session"/> is live.</summary>
    SignedIn = 1
    ''' <summary>`refused{reason}` other than admin-disabled; <see cref="ExchangeRemoteAdminSignIn.Refusal"/> holds the enum, never the text.</summary>
    Refused = 2
    ''' <summary>`refused{admin-disabled}`: administration is off on that server.</summary>
    Disabled = 3
    ''' <summary>No pin and the platform does not accept the certificate: a person decides before the password is read.</summary>
    NeedsFirstContact = 4
    ''' <summary>A pin is held and the certificate shown is another one: no password was read or sent.</summary>
    CertificateChanged = 5
    ''' <summary>Connect, TLS, timeout, reset or no answer; see <see cref="ExchangeRemoteAdminSignIn.Reason"/>.</summary>
    Unreachable = 6
    ''' <summary>The caller cancelled; the stream was closed. Not an error.</summary>
    Cancelled = 7
End Enum

''' <summary>The result of <see cref="ExchangeRemoteAdminSession.SignInAsync"/>.</summary>
Friend NotInheritable Class ExchangeRemoteAdminSignIn

    Public ReadOnly Property Kind As ExchangeRemoteAdminSignInKind
    ''' <summary>The live session of a SignedIn result, otherwise Nothing.</summary>
    Public ReadOnly Property Session As ExchangeRemoteAdminSession
    ''' <summary>The refusal of a Refused result (AdminDisabled for Disabled), otherwise Unknown.</summary>
    Public ReadOnly Property Refusal As ExchangeRefusal
    ''' <summary>NeedsFirstContact: the presented leaf ("SHA256:" and 43 characters), for the question.</summary>
    Public ReadOnly Property Fingerprint As String
    ''' <summary>NeedsFirstContact: the key pin of the presented leaf ("" when not known).</summary>
    Public ReadOnly Property KeyPin As String
    ''' <summary>CertificateChanged: the fingerprint the target holds.</summary>
    Public ReadOnly Property PinnedFingerprint As String
    ''' <summary>CertificateChanged: the fingerprint the server showed.</summary>
    Public ReadOnly Property PresentedFingerprint As String
    ''' <summary>Why an Unreachable result is one; None for every other kind.</summary>
    Public ReadOnly Property Reason As ExchangeRemoteAdminUnreachableReason
    ''' <summary>A bounded internal reason for the log and the tests (see <see cref="ExchangeRemoteAdminOutcome.Detail"/>).</summary>
    Public ReadOnly Property Detail As String

    Private Sub New(kind As ExchangeRemoteAdminSignInKind, session As ExchangeRemoteAdminSession, refusal As ExchangeRefusal, fingerprint As String, keyPin As String,
                    pinned As String, presented As String, reason As ExchangeRemoteAdminUnreachableReason, detail As String)
        Me.Kind = kind
        Me.Session = session
        Me.Refusal = refusal
        Me.Fingerprint = If(fingerprint, "")
        Me.KeyPin = If(keyPin, "")
        Me.PinnedFingerprint = If(pinned, "")
        Me.PresentedFingerprint = If(presented, "")
        Me.Reason = reason
        Me.Detail = If(detail, "")
    End Sub

    Friend Shared Function SignedIn(session As ExchangeRemoteAdminSession) As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.SignedIn, session, ExchangeRefusal.Unknown, "", "", "", "", ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    Friend Shared Function Refused(refusal As ExchangeRefusal) As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.Refused, Nothing, refusal, "", "", "", "", ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    Friend Shared Function Disabled() As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.Disabled, Nothing, ExchangeRefusal.AdminDisabled, "", "", "", "", ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    Friend Shared Function NeedsFirstContact(fingerprint As String, keyPin As String) As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.NeedsFirstContact, Nothing, ExchangeRefusal.Unknown, fingerprint, keyPin, "", "", ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    Friend Shared Function CertificateChanged(pinned As String, presented As String) As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.CertificateChanged, Nothing, ExchangeRefusal.Unknown, "", "", pinned, presented, ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    Friend Shared Function Unreachable(reason As ExchangeRemoteAdminUnreachableReason, detail As String) As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.Unreachable, Nothing, ExchangeRefusal.Unknown, "", "", "", "", reason, detail)
    End Function

    Friend Shared Function Cancelled() As ExchangeRemoteAdminSignIn
        Return New ExchangeRemoteAdminSignIn(ExchangeRemoteAdminSignInKind.Cancelled, Nothing, ExchangeRefusal.Unknown, "", "", "", "", ExchangeRemoteAdminUnreachableReason.None, "")
    End Function

    ''' <summary>The kind (and the reason); never a fingerprint.</summary>
    Public Overrides Function ToString() As String
        Return "remote-admin sign-in " & Kind.ToString() & If(Reason <> ExchangeRemoteAdminUnreachableReason.None, " " & Reason.ToString(), "")
    End Function

End Class

''' <summary>One connection to one exchange server's administration channel. One instance holds at most one live stream; a caller that
''' needs a second server builds a second instance.</summary>
Friend NotInheritable Class ExchangeRemoteAdminSession
    Implements IDisposable

    ''' <summary>Connect and handshake, each of the (at most two) attempts of a sign-in.</summary>
    Public Shared ReadOnly ConnectBound As TimeSpan = TimeSpan.FromSeconds(15)
    ''' <summary>The write of `admin-auth` and the wait for its answer. Longer than the server's 1 s pace plus the Argon2 time (3.1).</summary>
    Public Shared ReadOnly SignInBound As TimeSpan = TimeSpan.FromSeconds(20)
    ''' <summary>One request: the write and the wait for the reply with its id (frames of another id included).</summary>
    Public Shared ReadOnly RequestBound As TimeSpan = TimeSpan.FromSeconds(30)
    ''' <summary>`bye` and the wait for the server's close; then the stream is closed whatever happened.</summary>
    Public Shared ReadOnly ByeBound As TimeSpan = TimeSpan.FromMilliseconds(1500)
    ''' <summary>Frames of another id skipped before the stream is called out of step.</summary>
    Public Const MaxSkippedFrames As Integer = 8

    Private Enum Phase
        Idle = 0
        SigningIn = 1
        Live = 2
        Ended = 3
    End Enum

    Private NotInheritable Class SignInScope
        Public Resolution As ExchangeAdminTrustResolution
        Public Connection As ExchangeTlsConnection
    End Class

    Private NotInheritable Class CallResult
        Public ReadOnly Outcome As ExchangeRemoteAdminOutcome
        Public ReadOnly EndsSession As Boolean

        Public Sub New(outcome As ExchangeRemoteAdminOutcome, endsSession As Boolean)
            Me.Outcome = outcome
            Me.EndsSession = endsSession
        End Sub
    End Class

    ''' <summary>The handshake seam with the 15 s bound of 5.5 on each attempt; a timeout is an ExchangeConnectException(TimedOut).</summary>
    Private NotInheritable Class BoundedHandshake
        Implements IExchangeAdminHandshake

        Private ReadOnly _owner As ExchangeRemoteAdminSession

        Public Sub New(owner As ExchangeRemoteAdminSession)
            _owner = owner
        End Sub

        Public Async Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
            Try
                Return Await _owner.BoundedAsync(Of ExchangeTlsConnection)(Function(t As CancellationToken) _owner._handshake.ConnectPinnedAsync(host, port, leafPin, keyPin, t), ConnectBound, ct).ConfigureAwait(False)
            Catch ex As TimeoutException
                Throw New ExchangeConnectException(ExchangeConnectFailure.TimedOut, "", "timed out", ex)
            End Try
        End Function

        Public Async Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
            Try
                Return Await _owner.BoundedAsync(Of ExchangeCertificateInfo)(Function(t As CancellationToken) _owner._handshake.ProbeAsync(host, port, t), ConnectBound, ct).ConfigureAwait(False)
            Catch ex As TimeoutException
                Throw New ExchangeConnectException(ExchangeConnectFailure.TimedOut, "", "timed out", ex)
            End Try
        End Function
    End Class

    Private ReadOnly _handshake As IExchangeAdminHandshake
    Private ReadOnly _clock As Func(Of Long)
    Private ReadOnly _delay As Func(Of TimeSpan, CancellationToken, Task)
    Private ReadOnly _sink As Action(Of String)
    Private ReadOnly _gate As New Object()

    Private _phase As Phase = Phase.Idle
    Private _connection As ExchangeTlsConnection
    Private _reader As ExchangeFrameReader
    Private _writer As ExchangeFrameWriter
    Private _token As String
    Private _nextId As Long = 1
    Private _inFlight As Boolean
    Private _callCts As CancellationTokenSource
    Private _callDone As TaskCompletionSource(Of Boolean)
    Private _endRequested As Boolean
    Private _endKind As ExchangeRemoteAdminOutcomeKind = ExchangeRemoteAdminOutcomeKind.Lapsed
    Private _endReason As ExchangeRemoteAdminUnreachableReason = ExchangeRemoteAdminUnreachableReason.None
    Private _signInCts As CancellationTokenSource

    ''' <param name="handshake">The pinned connect and the probe (<c>ExchangeAdminHandshake.Instance</c> in production).</param>
    ''' <param name="clock">Monotonic milliseconds; the duration of the log lines is a difference of two reads.</param>
    ''' <param name="delay">Completes after the span, or is cancelled with the token: the timeouts of 5.5 are built on it.</param>
    ''' <param name="log">Receives one finished line per event; Nothing for no log. A log that throws never breaks a session.</param>
    Public Sub New(handshake As IExchangeAdminHandshake, clock As Func(Of Long), delay As Func(Of TimeSpan, CancellationToken, Task), log As Action(Of String))
        If handshake Is Nothing Then Throw New ArgumentNullException(NameOf(handshake))
        If clock Is Nothing Then Throw New ArgumentNullException(NameOf(clock))
        If delay Is Nothing Then Throw New ArgumentNullException(NameOf(delay))
        _handshake = handshake
        _clock = clock
        _delay = delay
        _sink = log
    End Sub

    ''' <summary>True while a stream is signed in and has not ended.</summary>
    Public ReadOnly Property IsLive As Boolean
        Get
            SyncLock _gate
                Return _phase = Phase.Live
            End SyncLock
        End Get
    End Property

    ' ----------------------------------------------------------------------------------------------------- the log

    Private Sub LogEvent(eventName As String, kind As String, startedMs As Long)
        If _sink Is Nothing Then Return
        Try
            Dim ms As Long = Math.Max(0L, _clock.Invoke() - startedMs)
            _sink.Invoke("remote-admin: " & eventName & " " & kind & " " & ms.ToString(Globalization.CultureInfo.InvariantCulture))
        Catch ex As Exception
            ' a logger that throws (or a clock that does) must never break a session
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------------- the bounds

    Private Shared Sub ObserveFault(t As Task)
        If t.IsFaulted Then GC.KeepAlive(t.Exception)
    End Sub

    ''' <summary>The work of a bound that elapsed first: its fault is observed, and a result it still produced (a connection made in the
    ''' last instant) is released, because nobody will ever read it.</summary>
    Private Shared Sub ReleaseAbandoned(Of T)(work As Task(Of T))
        If work.Status <> TaskStatus.RanToCompletion Then
            ObserveFault(work)
            Return
        End If
        Dim held As IDisposable = TryCast(CObj(work.Result), IDisposable)
        If held IsNot Nothing Then held.Dispose()
    End Sub

    ''' <summary>Runs <paramref name="work"/> under a bound built on the injected Delay. When the bound elapses first the work is
    ''' cancelled and a TimeoutException follows; when the caller's token is cancelled the OperationCanceledException of that token.</summary>
    Private Async Function BoundedAsync(Of T)(work As Func(Of CancellationToken, Task(Of T)), bound As TimeSpan, ct As CancellationToken) As Task(Of T)
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            Dim workTask As Task(Of T) = work.Invoke(linked.Token)
            Dim delayTask As Task = _delay.Invoke(bound, linked.Token)
            Dim first As Task = Await Task.WhenAny(workTask, delayTask).ConfigureAwait(False)
            If first Is workTask Then
                linked.Cancel()
                Dim observedDelay As Task = delayTask.ContinueWith(AddressOf ObserveFault, TaskContinuationOptions.ExecuteSynchronously)
                Return Await workTask.ConfigureAwait(False)
            End If
            linked.Cancel()
            Dim observedWork As Task = workTask.ContinueWith(Sub(done As Task(Of T)) ReleaseAbandoned(done), TaskContinuationOptions.ExecuteSynchronously)
            ct.ThrowIfCancellationRequested()
            Throw New TimeoutException("the bound elapsed")
        End Using
    End Function

    Private Shared Function IsStreamFault(ex As Exception) As Boolean
        Return TypeOf ex Is IOException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is ObjectDisposedException
    End Function

    ' ------------------------------------------------------------------------------------------------- sign-in

    ''' <summary>The decision of 5.4, then (on an Accept only) the password, `admin-auth`, and the answer. One live stream per instance: a
    ''' second call while this one is signed in or signing in throws InvalidOperationException (RA:52 - nothing here signs in again by itself;
    ''' a session that has ended may sign in again, which is the person's own click). Nothing is written before the decision accepts.</summary>
    ''' <param name="leafPin">The leaf pin the target holds ("" for none).</param>
    ''' <param name="keyPin">The key pin the target holds ("" for none).</param>
    ''' <param name="password">Called ONCE, after the decision accepts, to read the typed password into a new array (and clear the field). The
    ''' array is cleared by this class on every path. It must fit <c>ExchangeAdminWire.AuthPasswordFits</c> (the form checks first): an array
    ''' that does not is an ArgumentException after the connection is closed.</param>
    Public Async Function SignInAsync(host As String, port As Integer, leafPin As String, keyPin As String, password As Func(Of Char()), ct As CancellationToken) As Task(Of ExchangeRemoteAdminSignIn)
        If String.IsNullOrWhiteSpace(host) Then Throw New ArgumentException("a host is required", NameOf(host))
        If port < 1 OrElse port > 65535 Then Throw New ArgumentOutOfRangeException(NameOf(port))
        If password Is Nothing Then Throw New ArgumentNullException(NameOf(password))
        Dim signInCts As CancellationTokenSource
        SyncLock _gate
            If _phase = Phase.Live OrElse _phase = Phase.SigningIn Then
                Throw New InvalidOperationException("this session is signed in already, or signing in")
            End If
            _phase = Phase.SigningIn
            _endRequested = False
            _endKind = ExchangeRemoteAdminOutcomeKind.Lapsed
            _endReason = ExchangeRemoteAdminUnreachableReason.None
            signInCts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            _signInCts = signInCts
        End SyncLock

        Dim started As Long = _clock.Invoke()
        Dim scope As New SignInScope()
        Dim signedIn As Boolean = False
        Try
            Dim result As ExchangeRemoteAdminSignIn = Await SignInCoreAsync(scope, host, port, If(leafPin, ""), If(keyPin, ""), password, signInCts.Token).ConfigureAwait(False)
            signedIn = result.Kind = ExchangeRemoteAdminSignInKind.SignedIn
            LogEvent("sign-in", result.Kind.ToString(), started)
            Return result
        Finally
            SyncLock _gate
                _signInCts = Nothing
                If Not signedIn AndAlso _phase = Phase.SigningIn Then _phase = Phase.Idle
            End SyncLock
            If Not signedIn Then
                If scope.Connection IsNot Nothing Then scope.Connection.Dispose()
                If scope.Resolution IsNot Nothing Then scope.Resolution.Dispose()
            End If
            signInCts.Dispose()
        End Try
    End Function

    Private Shared Function FromConnectFailure(ex As ExchangeConnectException, held As String) As ExchangeRemoteAdminSignIn
        Select Case ex.Failure
            Case ExchangeConnectFailure.Cancelled
                Return ExchangeRemoteAdminSignIn.Cancelled()
            Case ExchangeConnectFailure.TimedOut
                Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Timeout, "connect-timeout")
            Case ExchangeConnectFailure.TlsFailed
                Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.TlsFailed, "tls-failed")
            Case ExchangeConnectFailure.PinMismatch
                Return ExchangeRemoteAdminSignIn.CertificateChanged(held, ex.Fingerprint)
            Case ExchangeConnectFailure.Untrusted
                ' the connection that would carry the secret no longer shows a certificate the platform accepts: with a pin held that is a
                ' changed certificate, without one it is the first-contact question again (the key pin is learned after a pinned handshake)
                If held.Length > 0 Then Return ExchangeRemoteAdminSignIn.CertificateChanged(held, ex.Fingerprint)
                Return ExchangeRemoteAdminSignIn.NeedsFirstContact(ex.Fingerprint, "")
            Case Else
                Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Network, "network")
        End Select
    End Function

    Private Async Function SignInCoreAsync(scope As SignInScope, host As String, port As Integer, held As String, heldKey As String,
                                           passwordSource As Func(Of Char()), ct As CancellationToken) As Task(Of ExchangeRemoteAdminSignIn)
        Dim bounded As IExchangeAdminHandshake = New BoundedHandshake(Me)
        Dim phaseStart As Long = _clock.Invoke()

        ' 1. the decision of 5.4 - a handshake or two, no application byte
        Try
            scope.Resolution = Await ExchangeAdminTrust.ResolveAsync(bounded, host, port, held, heldKey, ct).ConfigureAwait(False)
        Catch ex As ExchangeConnectException
            LogEvent("connect", ex.Failure.ToString(), phaseStart)
            Return FromConnectFailure(ex, held)
        Catch ex As OperationCanceledException When ct.IsCancellationRequested
            LogEvent("connect", "Cancelled", phaseStart)
            Return ExchangeRemoteAdminSignIn.Cancelled()
        End Try
        Dim decision As ExchangeAdminTrustDecision = scope.Resolution.Decision
        LogEvent("trust", decision.Outcome.ToString(), phaseStart)
        If decision.Outcome = ExchangeAdminTrustOutcome.AskFirstContact Then
            Return ExchangeRemoteAdminSignIn.NeedsFirstContact(decision.Fingerprint, decision.KeyPin)
        End If
        If decision.Outcome = ExchangeAdminTrustOutcome.StopChanged Then
            Return ExchangeRemoteAdminSignIn.CertificateChanged(decision.PinnedFingerprint, decision.Fingerprint)
        End If

        ' 2. the connection that carries the secret: the one the decision came from, or a new one that must pass the same rule
        Dim conn As ExchangeTlsConnection = scope.Resolution.Connection
        If conn Is Nothing Then
            Dim useHeld As Boolean = decision.Outcome = ExchangeAdminTrustOutcome.PinMatch
            phaseStart = _clock.Invoke()
            Try
                conn = Await bounded.ConnectPinnedAsync(host, port, If(useHeld, held, ""), If(useHeld, heldKey, ""), ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException
                LogEvent("connect", ex.Failure.ToString(), phaseStart)
                Return FromConnectFailure(ex, held)
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                LogEvent("connect", "Cancelled", phaseStart)
                Return ExchangeRemoteAdminSignIn.Cancelled()
            End Try
        End If
        scope.Connection = conn
        ' the decision is applied again on THE connection that will carry the password (5.4 rule 2): a leaf that changed between the
        ' probe and this connect is a stop, never "close enough"
        Dim again As ExchangeAdminTrustDecision = ExchangeAdminTrust.Decide(conn.Verdict = ExchangeTrustVerdict.PlatformValid, conn.Fingerprint, conn.KeyPin, held, heldKey)
        If Not again.IsAccepted Then
            LogEvent("trust", again.Outcome.ToString(), phaseStart)
            If again.Outcome = ExchangeAdminTrustOutcome.StopChanged Then
                Return ExchangeRemoteAdminSignIn.CertificateChanged(again.PinnedFingerprint, again.Fingerprint)
            End If
            Return ExchangeRemoteAdminSignIn.NeedsFirstContact(again.Fingerprint, again.KeyPin)
        End If

        ' 3. the password: asked for now, used once, cleared
        Dim reader As New ExchangeFrameReader(conn.Stream)
        Dim writer As New ExchangeFrameWriter(conn.Stream)
        Dim chars As Char() = passwordSource.Invoke()
        Dim reply As ExchangeEnvelope
        Try
            If chars Is Nothing OrElse Not ExchangeAdminWire.AuthPasswordFits(chars) Then
                Throw New ArgumentException("the sign-in password is empty or above 1024 bytes; the form checks ExchangeAdminWire.AuthPasswordFits first", NameOf(passwordSource))
            End If
            Dim secret As Char() = chars
            reply = Await BoundedAsync(Of ExchangeEnvelope)(Function(t As CancellationToken) AuthExchangeAsync(writer, reader, secret, t), SignInBound, ct).ConfigureAwait(False)
        Catch ex As TimeoutException
            Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Timeout, "sign-in-timeout")
        Catch ex As OperationCanceledException When ct.IsCancellationRequested
            Return ExchangeRemoteAdminSignIn.Cancelled()
        Catch ex As EndOfStreamException
            Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Unanswered, "closed-without-a-frame")
        Catch ex As ExchangeWireException
            If ex.Error = ExchangeWireError.Truncated Then Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Reset, "cut-frame")
            Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, "malformed-frame")
        Catch ex As Exception When IsStreamFault(ex)
            Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Reset, ex.GetType().Name)
        Finally
            If chars IsNot Nothing Then Array.Clear(chars, 0, chars.Length)
        End Try

        ' 4. the answer
        If reply.Is(ExchangeEnvelopeTypes.Refused) Then
            Dim refusal As ExchangeRefusal = reply.Refusal()
            If refusal = ExchangeRefusal.AdminDisabled Then Return ExchangeRemoteAdminSignIn.Disabled()
            Return ExchangeRemoteAdminSignIn.Refused(refusal)
        End If
        Dim ok As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(reply)
        If ok Is Nothing Then Return ExchangeRemoteAdminSignIn.Unreachable(ExchangeRemoteAdminUnreachableReason.Protocol, "auth-ok-shape")
        reader.MarkAuthenticated()
        writer.MarkAuthenticated()
        Dim live As Boolean
        SyncLock _gate
            live = _phase = Phase.SigningIn    ' False when Dispose came during the sign-in: the window is gone, so the answer is dropped
            If live Then
                _connection = conn
                _reader = reader
                _writer = writer
                _token = ok.Token
                _nextId = 1
                _inFlight = False
                _phase = Phase.Live
            End If
        End SyncLock
        If Not live Then Return ExchangeRemoteAdminSignIn.Cancelled()
        scope.Connection = Nothing
        scope.Resolution = Nothing
        LogEvent("sign-in", "window-" & ok.ExpiresSeconds.ToString(Globalization.CultureInfo.InvariantCulture) & "s", phaseStart)
        Return ExchangeRemoteAdminSignIn.SignedIn(Me)
    End Function

    ''' <summary>The `admin-auth` frame and the first frame back. The array is cleared as soon as the write is over, so the secret is not
    ''' held across the wait for the answer.</summary>
    Private Shared Async Function AuthExchangeAsync(writer As ExchangeFrameWriter, reader As ExchangeFrameReader, secret As Char(), ct As CancellationToken) As Task(Of ExchangeEnvelope)
        Try
            Dim auth As ExchangeEnvelope = ExchangeAdminWire.Auth(secret)
            Await writer.WriteAsync(auth, ct).ConfigureAwait(False)
        Finally
            Array.Clear(secret, 0, secret.Length)
        End Try
        Return Await reader.ReadAsync(ct).ConfigureAwait(False)
    End Function

    ' ------------------------------------------------------------------------------------------------- a call

    ''' <summary>One request and its reply. <paramref name="members"/> builds the `admin-request` for the token and the id this session
    ''' holds - one of the builders of <c>ExchangeAdminWire</c>, e.g. <c>Function(token, id) ExchangeAdminWire.AccountList(token, id)</c> -
    ''' and its op must be <paramref name="op"/> (one of the 18). Ids count from 1; one call is in flight at a time (a second is Busy and
    ''' writes nothing). A call on a session that has ended returns the kind that ended it and writes nothing: this class never signs in again.</summary>
    Public Async Function CallAsync(op As String, members As Func(Of String, Long, ExchangeEnvelope), ct As CancellationToken) As Task(Of ExchangeRemoteAdminOutcome)
        If members Is Nothing Then Throw New ArgumentNullException(NameOf(members))
        If Not ExchangeAdminWire.IsCarried(op) Then Throw New ArgumentException("not one of the 18 carried operations", NameOf(op))

        Dim reader As ExchangeFrameReader
        Dim writer As ExchangeFrameWriter
        Dim token As String
        Dim id As Long
        Dim cts As CancellationTokenSource
        SyncLock _gate
            If _phase <> Phase.Live Then Return EndedOutcomeLocked()
            If _endRequested Then Return ExchangeRemoteAdminOutcome.NewLapsed("session-ended")    ' EndAsync owns the stream from here on
            If _inFlight Then Return ExchangeRemoteAdminOutcome.NewBusy()
            _inFlight = True
            _callCts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            _callDone = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            cts = _callCts
            reader = _reader
            writer = _writer
            token = _token
            id = _nextId
        End SyncLock

        Dim started As Long = _clock.Invoke()
        Dim finished As Boolean = False
        Dim result As CallResult = Nothing
        Try
            Dim request As ExchangeEnvelope
            Try
                request = members.Invoke(token, id)
            Catch ex As ExchangeWireException
                result = New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("request-not-built"), False)
                finished = True
                Return result.Outcome
            Catch ex As Exception
                finished = True    ' nothing was written: a delegate that throws is the caller's bug and the session lives
                Throw
            End Try
            If request Is Nothing OrElse Not request.Is(ExchangeAdminWire.TypeRequest) OrElse request.Text("op") <> op OrElse request.Number("id") <> id Then
                finished = True    ' nothing was written: a bad delegate is the caller's bug and the session lives
                Throw New InvalidOperationException("the request does not belong to this call: its type, op or id differ")
            End If

            Try
                result = Await BoundedAsync(Of CallResult)(Function(t As CancellationToken) ExchangeAsync(request, op, id, reader, writer, t), RequestBound, cts.Token).ConfigureAwait(False)
            Catch ex As TimeoutException
                result = New CallResult(ExchangeRemoteAdminOutcome.NewUnreachable(ExchangeRemoteAdminUnreachableReason.Timeout, "request-timeout"), True)
            Catch ex As OperationCanceledException
                result = New CallResult(ExchangeRemoteAdminOutcome.NewUnreachable(ExchangeRemoteAdminUnreachableReason.Cancelled, "cancelled"), Not CancelledByEnd())
            Catch ex As EndOfStreamException
                result = New CallResult(ExchangeRemoteAdminOutcome.NewLapsed("end-of-stream"), True)
            Catch ex As ExchangeWireException
                If ex.Error = ExchangeWireError.Truncated Then
                    result = New CallResult(ExchangeRemoteAdminOutcome.NewUnreachable(ExchangeRemoteAdminUnreachableReason.Reset, "cut-frame"), True)
                Else
                    result = New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("malformed-frame"), True)
                End If
            Catch ex As Exception When IsStreamFault(ex)
                result = New CallResult(ExchangeRemoteAdminOutcome.NewUnreachable(ExchangeRemoteAdminUnreachableReason.Reset, ex.GetType().Name), True)
            End Try
            finished = True
            LogEvent("call", If(result.Outcome.Kind = ExchangeRemoteAdminOutcomeKind.Done, op, result.Outcome.Kind.ToString()), started)
            If result.EndsSession Then LogEvent("end", result.Outcome.Kind.ToString(), started)
            Return result.Outcome
        Finally
            Dim done As TaskCompletionSource(Of Boolean)
            SyncLock _gate
                _inFlight = False
                done = _callDone
                _callDone = Nothing
                _callCts = Nothing
                ' an unexpected exception leaves the stream in an unknown state: the session ends (and the exception goes on)
                If Not finished OrElse (result IsNot Nothing AndAlso result.EndsSession) Then
                    EndLocked(If(result IsNot Nothing, result.Outcome.Kind, ExchangeRemoteAdminOutcomeKind.Unreachable),
                              If(result IsNot Nothing, result.Outcome.Reason, ExchangeRemoteAdminUnreachableReason.Reset))
                End If
            End SyncLock
            cts.Dispose()
            If done IsNot Nothing Then done.TrySetResult(True)
        End Try
    End Function

    Private Function CancelledByEnd() As Boolean
        SyncLock _gate
            Return _endRequested
        End SyncLock
    End Function

    ''' <summary>Writes the request, then reads until the reply that carries its id. Frames of another id are skipped (at most
    ''' <see cref="MaxSkippedFrames"/>); a refusal, a frame of another type or a malformed one ends the session.</summary>
    Private Async Function ExchangeAsync(request As ExchangeEnvelope, op As String, id As Long, reader As ExchangeFrameReader, writer As ExchangeFrameWriter, ct As CancellationToken) As Task(Of CallResult)
        Try
            Await writer.WriteAsync(request, ct).ConfigureAwait(False)
        Catch ex As ExchangeWireException When ex.Error = ExchangeWireError.FrameSize OrElse ex.Error = ExchangeWireError.BadMessage
            ' refused by the writer BEFORE a byte went out: the stream is as it was and the session lives
            Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("request-not-built"), False)
        End Try
        SyncLock _gate
            _nextId = id + 1
        End SyncLock

        Dim skipped As Integer = 0
        While True
            Dim frame As ExchangeEnvelope = Await reader.ReadAsync(ct).ConfigureAwait(False)
            If frame.Is(ExchangeEnvelopeTypes.Refused) Then
                If frame.Refusal() = ExchangeRefusal.BadCredentials Then
                    Return New CallResult(ExchangeRemoteAdminOutcome.NewLapsed("refused-bad-credentials"), True)
                End If
                Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("refused-after-sign-in"), True)
            End If
            If Not frame.Is(ExchangeAdminWire.TypeReply) Then
                Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("unexpected-frame"), True)
            End If
            Dim reply As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(frame, op)
            If reply.Kind = ExchangeAdminWireReplyKind.Protocol AndAlso reply.Id = 0 Then
                Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("malformed-reply"), True)
            End If
            If reply.Id <> id Then
                skipped += 1
                If skipped > MaxSkippedFrames Then Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("too-many-other-ids"), True)
                Continue While
            End If
            Select Case reply.Kind
                Case ExchangeAdminWireReplyKind.Done
                    Return New CallResult(ExchangeRemoteAdminOutcome.NewDone(reply.HasData, reply.Data), False)
                Case ExchangeAdminWireReplyKind.Failed
                    Return New CallResult(ExchangeRemoteAdminOutcome.NewFailed(reply.Code), False)
                Case Else
                    ' `ok:true` with a body of the wrong shape: the request may have been carried out (5.5), so the session ends like every
                    ' other Protocol result and the next sign-in re-reads what the person sees
                    Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("reply-body"), True)
            End Select
        End While
        Return New CallResult(ExchangeRemoteAdminOutcome.NewProtocol("unreachable-code"), True)
    End Function

    ' ------------------------------------------------------------------------------------------------- the end

    Private Function EndedOutcomeLocked() As ExchangeRemoteAdminOutcome
        If _phase = Phase.Ended Then
            Select Case _endKind
                Case ExchangeRemoteAdminOutcomeKind.Unreachable
                    Return ExchangeRemoteAdminOutcome.NewUnreachable(_endReason, "session-ended")
                Case ExchangeRemoteAdminOutcomeKind.Protocol
                    Return ExchangeRemoteAdminOutcome.NewProtocol("session-ended")
                Case Else
                    Return ExchangeRemoteAdminOutcome.NewLapsed("session-ended")
            End Select
        End If
        Return ExchangeRemoteAdminOutcome.NewLapsed("not-signed-in")
    End Function

    ''' <summary>The stream is closed without a word (the lapse, the break and the protocol ends: there is no `bye` to a stream that is gone
    ''' or out of step) and every later call returns the kind that ended it. Called with the gate held.</summary>
    Private Sub EndLocked(kind As ExchangeRemoteAdminOutcomeKind, reason As ExchangeRemoteAdminUnreachableReason)
        If _phase = Phase.Ended Then Return
        _phase = Phase.Ended
        _endKind = kind
        _endReason = reason
        _token = Nothing
        Dim conn As ExchangeTlsConnection = _connection
        _connection = Nothing
        _reader = Nothing
        _writer = Nothing
        If conn IsNot Nothing Then conn.Dispose()
    End Sub

    ''' <summary>The polite end (5.8): `bye`, then up to 1500 ms for the server to close, then the stream is closed whatever happened.
    ''' A call in flight is cancelled first. Safe to call again, on a session that has ended and on one that never signed in (nothing is
    ''' written then). It never throws for a broken stream.</summary>
    Public Async Function EndAsync() As Task
        Dim waitFor As Task = Nothing
        Dim toCancel As CancellationTokenSource = Nothing
        SyncLock _gate
            If _phase <> Phase.Live Then Return
            _endRequested = True
            If _inFlight Then
                toCancel = _callCts
                waitFor = _callDone.Task
            End If
        End SyncLock
        If toCancel IsNot Nothing Then
            Try
                toCancel.Cancel()
            Catch ex As ObjectDisposedException
            End Try
        End If
        If waitFor IsNot Nothing Then Await waitFor.ConfigureAwait(False)

        Dim conn As ExchangeTlsConnection
        Dim reader As ExchangeFrameReader
        Dim writer As ExchangeFrameWriter
        SyncLock _gate
            If _phase <> Phase.Live Then Return
            conn = _connection
            reader = _reader
            writer = _writer
            _phase = Phase.Ended
            _endKind = ExchangeRemoteAdminOutcomeKind.Lapsed
            _endReason = ExchangeRemoteAdminUnreachableReason.None
            _token = Nothing
            _connection = Nothing
            _reader = Nothing
            _writer = Nothing
        End SyncLock

        Dim started As Long = _clock.Invoke()
        Try
            Await BoundedAsync(Of Boolean)(Function(t As CancellationToken) ByeAsync(writer, reader, t), ByeBound, CancellationToken.None).ConfigureAwait(False)
        Catch ex As Exception When TypeOf ex Is TimeoutException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ExchangeWireException OrElse IsStreamFault(ex)
            ' the end is best effort: a server that does not close, a reset or a cut frame are all "over"
        Finally
            conn.Dispose()
        End Try
        LogEvent("end", "Bye", started)
    End Function

    Private Shared Async Function ByeAsync(writer As ExchangeFrameWriter, reader As ExchangeFrameReader, ct As CancellationToken) As Task(Of Boolean)
        Await writer.WriteAsync(New ExchangeEnvelope(ExchangeEnvelopeTypes.Bye), ct).ConfigureAwait(False)
        Try
            Await reader.ReadAsync(ct).ConfigureAwait(False)    ' the server's close (a clean end of stream) or a refusal of a late bye: both end the wait
        Catch ex As EndOfStreamException
        End Try
        Return True
    End Function

    ''' <summary>Closes the stream at once, with no `bye` (a window that is gone, a test). <see cref="EndAsync"/> is the polite end.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        Dim toCancel As CancellationTokenSource = Nothing
        SyncLock _gate
            If _phase = Phase.Live OrElse _phase = Phase.SigningIn Then
                toCancel = If(_phase = Phase.SigningIn, _signInCts, _callCts)
                EndLocked(ExchangeRemoteAdminOutcomeKind.Lapsed, ExchangeRemoteAdminUnreachableReason.None)
            End If
        End SyncLock
        If toCancel IsNot Nothing Then
            Try
                toCancel.Cancel()
            Catch ex As ObjectDisposedException
            End Try
        End If
    End Sub

    ''' <summary>The kind only; never the token or the server.</summary>
    Public Overrides Function ToString() As String
        SyncLock _gate
            Return "remote-admin session " & _phase.ToString()
        End SyncLock
    End Function

End Class
#End If
