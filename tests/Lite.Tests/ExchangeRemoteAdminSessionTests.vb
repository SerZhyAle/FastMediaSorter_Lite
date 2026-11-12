#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit
Imports K = fmsl.ExchangeAdminKit
Imports Tx = fmsl.ExchangeTranscriptStep

' ----------------------------------------------------------------------------------------------------------------------
' L0278 T-3 slice B, specification 7.4: the session of the remote administration client (ExchangeRemoteAdminSession) over a
' scripted handshake and a lock-step stream, with the clock and the delay INJECTED (ManualAdminTime): nothing here waits on a
' wall clock. A test may wait for a task a double completes; the only real-time bound is a safety that turns a defect into a
' failure instead of a hang (ExchangeAdminKit.Within / PendingAsync / UntilAsync, 5 s).
' ----------------------------------------------------------------------------------------------------------------------

''' <summary>Log lines captured by the session's injected log (thread safe: a session writes from whatever thread finished the work).</summary>
Friend NotInheritable Class ExchangeAdminLogSink

    Private ReadOnly _lines As New List(Of String)

    Public Sub Add(line As String)
        SyncLock _lines
            _lines.Add(line)
        End SyncLock
    End Sub

    Public Function Lines() As List(Of String)
        SyncLock _lines
            Return New List(Of String)(_lines)
        End SyncLock
    End Function

End Class

''' <summary>
''' A duplex stream whose reads the TEST feeds and holds: bytes and an end are supplied by hand, a pending read waits for them (and,
''' with <see cref="HonourCancel"/> off, ignores its token like a stuck socket), and every write is kept twice - as a copy taken at
''' write time and as the very array the writer passed, so a test can see the writer wipe the frame afterwards.
''' </summary>
Friend NotInheritable Class ExchangeAdminManualStream
    Inherits Stream

    Private ReadOnly _gate As New Object()
    Private ReadOnly _inbox As New List(Of Byte)
    Private ReadOnly _copies As New List(Of Byte())
    Private ReadOnly _references As New List(Of Byte())
    Private _ended As Boolean
    Private _disposed As Boolean
    Private _readFault As Exception
    Private _waiting As TaskCompletionSource(Of Integer)
    Private _waitBuffer As Byte()
    Private _waitOffset As Integer
    Private _waitCount As Integer

    ''' <summary>True: a pending read ends when its token is cancelled (a healthy stream). False: it ignores the token.</summary>
    Public Property HonourCancel As Boolean = True
    ''' <summary>Thrown by the next writes (an unexpected failure of the stream).</summary>
    Public Property WriteFault As Exception
    ''' <summary>Run once, at the start of the next read (before it returns anything).</summary>
    Public Property BeforeRead As Action

    Public ReadOnly Property Disposed As Boolean
        Get
            SyncLock _gate
                Return _disposed
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property ReadPending As Boolean
        Get
            SyncLock _gate
                Return _waiting IsNot Nothing
            End SyncLock
        End Get
    End Property

    Public Sub SupplyFrame(json As String)
        SupplyBytes(Tx.FrameOf(json))
    End Sub

    Public Sub SupplyBytes(bytes As Byte())
        Dim toComplete As TaskCompletionSource(Of Integer) = Nothing
        Dim n As Integer = 0
        SyncLock _gate
            _inbox.AddRange(bytes)
            If _waiting IsNot Nothing Then
                toComplete = _waiting
                _waiting = Nothing
                n = TakeLocked(_waitBuffer, _waitOffset, _waitCount)
            End If
        End SyncLock
        If toComplete IsNot Nothing Then toComplete.TrySetResult(n)
    End Sub

    ''' <summary>The peer closes: the pending read, and every later one, returns 0.</summary>
    Public Sub EndOfStream()
        Dim toComplete As TaskCompletionSource(Of Integer) = Nothing
        SyncLock _gate
            _ended = True
            toComplete = _waiting
            _waiting = Nothing
        End SyncLock
        If toComplete IsNot Nothing Then toComplete.TrySetResult(0)
    End Sub

    ''' <summary>The pending read ends as cancelled (what a stuck read does once the socket is torn down by the test).</summary>
    Public Sub CancelPending()
        Dim toComplete As TaskCompletionSource(Of Integer) = Nothing
        SyncLock _gate
            toComplete = _waiting
            _waiting = Nothing
        End SyncLock
        If toComplete IsNot Nothing Then toComplete.TrySetCanceled()
    End Sub

    ''' <summary>The write copies, in order.</summary>
    Public Function WriteCopies() As List(Of Byte())
        SyncLock _gate
            Return New List(Of Byte())(_copies)
        End SyncLock
    End Function

    ''' <summary>The arrays the writer handed to the stream, in order (live objects: they show what the writer did to them later).</summary>
    Public Function WriteReferences() As List(Of Byte())
        SyncLock _gate
            Return New List(Of Byte())(_references)
        End SyncLock
    End Function

    ''' <summary>The JSON bodies of the frames written so far.</summary>
    Public Function Frames() As List(Of String)
        Return K.ParseFrames(WriteCopies())
    End Function

    Private Function TakeLocked(target As Byte(), offset As Integer, count As Integer) As Integer
        Dim n As Integer = Math.Min(count, _inbox.Count)
        _inbox.CopyTo(0, target, offset, n)
        _inbox.RemoveRange(0, n)
        Return n
    End Function

    Public Overrides Function ReadAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        Dim hook As Action = BeforeRead
        If hook IsNot Nothing Then
            BeforeRead = Nothing
            hook.Invoke()
        End If
        Dim source As TaskCompletionSource(Of Integer)
        SyncLock _gate
            If _disposed Then Return Task.FromException(Of Integer)(New ObjectDisposedException(NameOf(ExchangeAdminManualStream)))
            If _readFault IsNot Nothing Then
                Dim fault As Exception = _readFault
                _readFault = Nothing
                Return Task.FromException(Of Integer)(fault)
            End If
            If _inbox.Count > 0 Then Return Task.FromResult(TakeLocked(buffer, offset, count))
            If _ended Then Return Task.FromResult(0)
            If _waiting IsNot Nothing Then Return Task.FromException(Of Integer)(New InvalidOperationException("one read at a time"))
            source = New TaskCompletionSource(Of Integer)(TaskCreationOptions.RunContinuationsAsynchronously)
            _waiting = source
            _waitBuffer = buffer
            _waitOffset = offset
            _waitCount = count
        End SyncLock
        If HonourCancel AndAlso cancellationToken.CanBeCanceled Then
            cancellationToken.Register(Sub()
                                           SyncLock _gate
                                               If _waiting Is source Then _waiting = Nothing
                                           End SyncLock
                                           source.TrySetCanceled(cancellationToken)
                                       End Sub)
        End If
        Return source.Task
    End Function

    Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
        Return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult()
    End Function

    Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
        Dim fault As Exception = WriteFault
        If fault IsNot Nothing Then Throw fault
        SyncLock _gate
            If _disposed Then Throw New ObjectDisposedException(NameOf(ExchangeAdminManualStream))
            Dim copy(count - 1) As Byte
            System.Buffer.BlockCopy(buffer, offset, copy, 0, count)
            _copies.Add(copy)
            _references.Add(buffer)
        End SyncLock
    End Sub

    Public Overrides Function WriteAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
        If cancellationToken.IsCancellationRequested Then Return Task.FromCanceled(cancellationToken)
        Try
            Write(buffer, offset, count)
        Catch ex As Exception
            Return Task.FromException(ex)
        End Try
        Return Task.CompletedTask
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            Dim toComplete As TaskCompletionSource(Of Integer) = Nothing
            SyncLock _gate
                _disposed = True
                toComplete = _waiting
                _waiting = Nothing
            End SyncLock
            If toComplete IsNot Nothing Then toComplete.TrySetException(New ObjectDisposedException(NameOf(ExchangeAdminManualStream)))
        End If
        MyBase.Dispose(disposing)
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Throw New NotSupportedException()
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Throw New NotSupportedException()
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overrides Sub Flush()
    End Sub

    Public Overrides Function FlushAsync(cancellationToken As CancellationToken) As Task
        Return Task.CompletedTask
    End Function

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

End Class

''' <summary>A handshake double that hands out ONE given stream (and reports a platform-valid probe), with a gate that holds the
''' connect past its bound and ignores the token - what a connect that completes in the last instant looks like.</summary>
Friend NotInheritable Class ExchangeAdminStreamHandshake
    Implements IExchangeAdminHandshake

    Private ReadOnly _stream As Stream
    Public Connects As Integer
    Public Probes As Integer

    Public Property Verdict As ExchangeTrustVerdict = ExchangeTrustVerdict.PlatformValid
    Public Property Fingerprint As String = K.Fingerprint
    Public Property ConnectGate As TaskCompletionSource(Of ExchangeTlsConnection)

    Public Sub New(stream As Stream)
        _stream = stream
    End Sub

    Public Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
        Interlocked.Increment(Connects)
        Dim gate As TaskCompletionSource(Of ExchangeTlsConnection) = ConnectGate
        If gate IsNot Nothing Then Return gate.Task
        Return Task.FromResult(New ExchangeTlsConnection(_stream, Fingerprint, Verdict, ""))
    End Function

    Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
        Interlocked.Increment(Probes)
        Return Task.FromResult(New ExchangeCertificateInfo(Fingerprint, True, ""))
    End Function

End Class

''' <summary>One session under test with its injected time, a scripted (or given) handshake and a log sink.</summary>
Friend NotInheritable Class ExchangeAdminRig
    Implements IDisposable

    Public ReadOnly Time As ManualAdminTime
    Public ReadOnly Scripted As New ScriptedAdminHandshake()
    Public ReadOnly Log As ExchangeAdminLogSink
    Public ReadOnly Session As ExchangeRemoteAdminSession
    Public PasswordCalls As Integer

    Public Sub New(Optional handshake As IExchangeAdminHandshake = Nothing, Optional delay As Func(Of TimeSpan, CancellationToken, Task) = Nothing,
                   Optional sink As ExchangeAdminLogSink = Nothing, Optional time As ManualAdminTime = Nothing)
        Me.Time = If(time, New ManualAdminTime())
        Log = If(sink, New ExchangeAdminLogSink())
        Dim chosen As IExchangeAdminHandshake = If(handshake, CType(Scripted, IExchangeAdminHandshake))
        Dim chosenDelay As Func(Of TimeSpan, CancellationToken, Task) = If(delay, New Func(Of TimeSpan, CancellationToken, Task)(AddressOf Me.Time.Delay))
        Session = New ExchangeRemoteAdminSession(chosen, AddressOf Me.Time.Now, chosenDelay, AddressOf Log.Add)
    End Sub

    ''' <summary>The next sign-in finds a platform-valid certificate (probe) and connects to this stream.</summary>
    Public Function Plain(stream As LockStepStream) As ExchangeAdminRig
        Scripted.EnqueueProbe(True, K.Fingerprint).EnqueueConnect(stream, K.Fingerprint, ExchangeTrustVerdict.PlatformValid)
        Return Me
    End Function

    Public Function SignInAsync(password As Char(), Optional ct As CancellationToken = Nothing, Optional leafPin As String = "", Optional keyPin As String = "",
                                Optional host As String = Nothing, Optional port As Integer = 0) As Task(Of ExchangeRemoteAdminSignIn)
        Dim source As Func(Of Char()) = Function()
                                            Interlocked.Increment(PasswordCalls)
                                            Return password
                                        End Function
        Return Session.SignInAsync(If(host, K.Host), If(port > 0, port, K.Port), leafPin, keyPin, source, ct)
    End Function

    ''' <summary>A good sign-in over <paramref name="stream"/> (the platform-valid path).</summary>
    Public Async Function SignedInAsync(stream As LockStepStream) As Task(Of ExchangeRemoteAdminSession)
        Plain(stream)
        Return Await SignedInOnHandshakeAsync().ConfigureAwait(False)
    End Function

    ''' <summary>A good sign-in on whatever handshake the rig was built with.</summary>
    Public Async Function SignedInOnHandshakeAsync() As Task(Of ExchangeRemoteAdminSession)
        Dim result As ExchangeRemoteAdminSignIn = Await K.Within(SignInAsync(K.Password()), "the sign-in").ConfigureAwait(False)
        Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, result.Kind)
        Assert.Same(Session, result.Session)
        Return Session
    End Function

    Public Function CallOpAsync(op As String, members As Func(Of String, Long, ExchangeEnvelope), Optional ct As CancellationToken = Nothing) As Task(Of ExchangeRemoteAdminOutcome)
        Return K.Within(Session.CallAsync(op, members, ct), "the call " & op)
    End Function

    ''' <summary>How many admin-auth frames every scripted stream received, wrong ones that broke the script included.</summary>
    Public Function AuthFrameCount() As Integer
        Dim n As Integer = 0
        For Each s As LockStepStream In Scripted.Streams()
            For Each json As String In K.ParseFrames(s.Writes())
                If K.TypeOfFrame(json) = "admin-auth" Then n += 1
            Next
        Next
        Return n
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Session.Dispose()
    End Sub

End Class

''' <summary>The constants, frame texts and guards the session tests and the replay tests share.</summary>
Friend NotInheritable Class ExchangeAdminKit

    Private Sub New()
    End Sub

    Friend Const Host As String = "exchange-secret-host.example"
    Friend Const Port As Integer = 48713
    Friend Const SecretPassword As String = "AdminSecretPass1"
    Friend Shared ReadOnly Fingerprint As String = "SHA256:" & New String("F"c, 43)
    Friend Shared ReadOnly KeyPin As String = "SHA256:" & New String("K"c, 43)
    Friend Shared ReadOnly Token As String = ExchangeWireTestKit.TokenOf("T"c)
    ''' <summary>The only real time a test waits: a safety that turns a defect into a failure, never a bound under test.</summary>
    Friend Shared ReadOnly Safety As TimeSpan = TimeSpan.FromSeconds(5)

    Friend Shared ReadOnly StatusMembers As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.Status(t, i)
    Friend Shared ReadOnly AccountListMembers As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.AccountList(t, i)

    Friend Shared Function Password() As Char()
        Return SecretPassword.ToCharArray()
    End Function

    ' ---------------------------------------------------------------- frame texts (the bytes of the corpus, member for member)

    Friend Shared Function AuthJson(password As String) As String
        Return "{""schemaVersion"":2,""type"":""admin-auth"",""password"":""" & password & """}"
    End Function

    Friend Shared Function AuthOkJson(tok As String, Optional seconds As Long = 900) As String
        Return "{""schemaVersion"":2,""type"":""admin-auth-ok"",""token"":""" & tok & """,""expiresSeconds"":" & seconds.ToString(CultureInfo.InvariantCulture) & "}"
    End Function

    Friend Shared Function RequestJson(tok As String, id As Long, op As String) As String
        Return "{""schemaVersion"":2,""type"":""admin-request"",""id"":" & id.ToString(CultureInfo.InvariantCulture) & ",""token"":""" & tok & """,""op"":""" & op & """}"
    End Function

    ''' <summary>An `ok:true` reply; <paramref name="dataJson"/> Nothing sends no `data` member.</summary>
    Friend Shared Function ReplyJson(id As Long, dataJson As String) As String
        Return "{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(CultureInfo.InvariantCulture) & ",""ok"":true" &
               If(dataJson Is Nothing, "", ",""data"":" & dataJson) & "}"
    End Function

    Friend Shared Function FailedJson(id As Long, code As String) As String
        Return "{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(CultureInfo.InvariantCulture) & ",""ok"":false,""code"":""" & code & """,""error"":""free text the client never keeps""}"
    End Function

    Friend Shared Function RefusedJson(reason As String) As String
        Return "{""schemaVersion"":2,""type"":""refused"",""reason"":""" & reason & """}"
    End Function

    Friend Shared ReadOnly ByeJson As String = "{""schemaVersion"":2,""type"":""bye""}"

    ''' <summary>The two steps of a good sign-in: the client's admin-auth and the server's admin-auth-ok.</summary>
    Friend Shared Function SignInSteps(Optional seconds As Long = 900) As List(Of ExchangeTranscriptStep)
        Return New List(Of ExchangeTranscriptStep) From {Tx.Expect(AuthJson(SecretPassword)), Tx.Supply(AuthOkJson(Token, seconds))}
    End Function

    ''' <summary>A stream that starts with a good sign-in and goes on with <paramref name="tail"/>.</summary>
    Friend Shared Function LiveStream(ParamArray tail As ExchangeTranscriptStep()) As LockStepStream
        Dim steps As List(Of ExchangeTranscriptStep) = SignInSteps()
        steps.AddRange(tail)
        Return New LockStepStream(steps)
    End Function

    ' ---------------------------------------------------------------- reading what a client wrote

    ''' <summary>The JSON bodies of the length-prefixed frames in these write chunks, in order.</summary>
    Friend Shared Function ParseFrames(chunks As IEnumerable(Of Byte())) As List(Of String)
        Dim joined As New List(Of Byte)
        For Each part As Byte() In chunks
            joined.AddRange(part)
        Next
        Dim result As New List(Of String)
        Dim pos As Integer = 0
        While pos + 4 <= joined.Count
            Dim len As Integer = (CInt(joined(pos)) << 24) Or (CInt(joined(pos + 1)) << 16) Or (CInt(joined(pos + 2)) << 8) Or CInt(joined(pos + 3))
            If len < 0 OrElse pos + 4 + len > joined.Count Then Exit While
            result.Add(Encoding.UTF8.GetString(joined.GetRange(pos + 4, len).ToArray()))
            pos += 4 + len
        End While
        Return result
    End Function

    Friend Shared Function TypeOfFrame(json As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim t As JsonElement = Nothing
            If doc.RootElement.TryGetProperty("type", t) AndAlso t.ValueKind = JsonValueKind.String Then Return t.GetString()
        End Using
        Return ""
    End Function

    Friend Shared Function IsAllZero(chars As Char()) As Boolean
        If chars Is Nothing Then Return False
        For Each c As Char In chars
            If c <> ChrW(0) Then Return False
        Next
        Return True
    End Function

    ' ---------------------------------------------------------------- guards: a defect fails, it does not hang

    Friend Shared Async Function Within(Of T)(work As Task(Of T), what As String) As Task(Of T)
        Using guard As New CancellationTokenSource()
            Dim winner As Task = Await Task.WhenAny(work, Task.Delay(Safety, guard.Token)).ConfigureAwait(False)
            If winner IsNot work Then Assert.Fail("real-time safety: " & what & " did not finish in " & Safety.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) & " s")
            guard.Cancel()
        End Using
        Return Await work.ConfigureAwait(False)
    End Function

    Friend Shared Async Function WithinTask(work As Task, what As String) As Task
        Using guard As New CancellationTokenSource()
            Dim winner As Task = Await Task.WhenAny(work, Task.Delay(Safety, guard.Token)).ConfigureAwait(False)
            If winner IsNot work Then Assert.Fail("real-time safety: " & what & " did not finish in " & Safety.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) & " s")
            guard.Cancel()
        End Using
        Await work.ConfigureAwait(False)
    End Function

    ''' <summary>Returns when <paramref name="condition"/> holds (a state the code under test reaches by itself, polled without a clock).</summary>
    Friend Shared Async Function UntilAsync(condition As Func(Of Boolean), what As String) As Task
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim spins As Integer = 0
        While Not condition.Invoke()
            If watch.Elapsed > Safety Then Assert.Fail("real-time safety: waited for " & what & " for " & Safety.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) & " s")
            spins += 1
            If spins < 300 Then
                Await Task.Yield()
            Else
                Await Task.Delay(1).ConfigureAwait(False)
            End If
        End While
    End Function

    ''' <summary>Returns when <paramref name="count"/> delays of exactly this span are pending: the code under test is waiting on that bound.</summary>
    Friend Shared Function PendingAsync(time As ManualAdminTime, span As TimeSpan, Optional count As Integer = 1) As Task
        Return UntilAsync(Function() time.PendingCount(span) >= count, "a pending " & span.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) & " ms bound")
    End Function

End Class

''' <summary>
''' L0278 T-3 slice B, specification 7.4: the session tests. Every clause of 7.4 is its own fact; the four behaviours that were
''' fixed by reading the code (Dispose during a sign-in, a call after End was requested, a late connection, the window line of
''' the log) are proved here as well. The replay of the eight administration streams is ExchangeAdminReplayTests.
''' </summary>
Public Class ExchangeRemoteAdminSessionTests

    Private Shared ReadOnly Secret As String = K.SecretPassword

    Private Shared Function ReqStatus(id As Long) As ExchangeTranscriptStep
        Return Tx.Expect(K.RequestJson(K.Token, id, "status"))
    End Function

    Private Shared Function Reply(id As Long) As ExchangeTranscriptStep
        Return Tx.Supply(K.ReplyJson(id, "{""accounts"":1}"))
    End Function

    ' ================================================================ sign-in: the good path, the bounds, the log

    <Fact>
    Public Async Function A_good_sign_in_goes_live_logs_the_window_and_wipes_the_password() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream()
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, result.Kind)
            Assert.True(rig.Session.IsLive)
            Assert.Equal(1, rig.PasswordCalls)
            Assert.True(K.IsAllZero(pw), "the password array was not wiped after a good sign-in")
            ' the window of the answer is logged (no address, no token): `sign-in window-900s <ms>` and the kind of the whole attempt
            Assert.Contains(rig.Log.Lines(), Function(l) Regex.IsMatch(l, "^remote-admin: sign-in window-900s [0-9]+$"))
            Assert.Contains(rig.Log.Lines(), Function(l) Regex.IsMatch(l, "^remote-admin: sign-in SignedIn [0-9]+$"))
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function The_window_line_carries_the_seconds_the_server_announced() As Task
        Using rig As New ExchangeAdminRig()
            Dim steps As List(Of ExchangeTranscriptStep) = K.SignInSteps(60)
            rig.Plain(New LockStepStream(steps))
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(K.Password()), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, result.Kind)
            Assert.Contains("remote-admin: sign-in window-60s 0", rig.Log.Lines())
        End Using
    End Function

    <Fact>
    Public Async Function The_four_bounds_of_5_5_are_the_spans_the_injected_delay_is_asked_for_and_none_is_left_pending() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Reply(1), Tx.Expect(K.ByeJson), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
            End Using
            Await K.WithinTask(session.EndAsync(), "the end")
            ' probe and connect (15 s each), the sign-in reply (20 s), one request (30 s), bye (1500 ms)
            Assert.Equal(New TimeSpan() {TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(1500)}, rig.Time.Spans().ToArray())
            Assert.Equal(ExchangeRemoteAdminSession.ConnectBound, TimeSpan.FromSeconds(15))
            Assert.Equal(ExchangeRemoteAdminSession.SignInBound, TimeSpan.FromSeconds(20))
            Assert.Equal(ExchangeRemoteAdminSession.RequestBound, TimeSpan.FromSeconds(30))
            Assert.Equal(ExchangeRemoteAdminSession.ByeBound, TimeSpan.FromMilliseconds(1500))
            For Each span As TimeSpan In rig.Time.Spans()
                Assert.Equal(0, rig.Time.PendingCount(span))
            Next
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function The_log_duration_comes_from_the_injected_clock() As Task
        Dim stream As New ExchangeAdminManualStream()
        stream.SupplyFrame(K.AuthOkJson(K.Token))
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInOnHandshakeAsync()
            Dim pending As Task(Of ExchangeRemoteAdminOutcome) = session.CallAsync("status", K.StatusMembers, CancellationToken.None)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.RequestBound)
            rig.Time.Advance(40)
            stream.SupplyFrame(K.ReplyJson(1, "{""accounts"":1}"))
            Using outcome As ExchangeRemoteAdminOutcome = Await K.Within(pending, "the call")
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
            End Using
            Assert.Contains("remote-admin: call status 40", rig.Log.Lines())
        End Using
    End Function

    ' ================================================================ lapse, end of stream, reset

    <Fact>
    Public Async Function Lapse_by_refused_bad_credentials_ends_the_session_and_a_later_call_writes_nothing() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Supply(K.RefusedJson("bad-credentials")), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using first As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, first.Kind)
                Assert.Equal("refused-bad-credentials", first.Detail)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed, "the stream was not closed after the lapse")
            Dim written As Integer = stream.Writes().Count
            Using second As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("account.list", K.AccountListMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, second.Kind)
            End Using
            Assert.Equal(written, stream.Writes().Count)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function Lapse_by_a_clean_end_of_stream_is_Lapsed() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.None, outcome.Reason)
                Assert.Equal("end-of-stream", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_socket_reset_is_Unreachable_Reset_where_an_end_of_stream_is_Lapsed() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Reset())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, outcome.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Reset, outcome.Reason)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed)
            ' the session remembers HOW it ended
            Using later As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, later.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Reset, later.Reason)
            End Using
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_stream_that_ends_inside_a_frame_is_Unreachable_Reset() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.SupplyRaw(New Byte() {0, 0, 0, 40, 123}, "a header that promises 40 bytes and one byte of the body"), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, outcome.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Reset, outcome.Reason)
                Assert.Equal("cut-frame", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
        End Using
    End Function

    ' ================================================================ the sign-in answers

    <Theory>
    <InlineData("bad-credentials", "Refused", "BadCredentials")>
    <InlineData("rate-limited", "Refused", "RateLimited")>
    <InlineData("capacity", "Refused", "Capacity")>
    <InlineData("admin-disabled", "Disabled", "AdminDisabled")>
    Public Async Function Each_refusal_of_a_sign_in_is_the_enum_and_wipes_the_password(reason As String, kind As String, refusal As String) As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Supply(K.RefusedJson(reason)), Tx.Closed())
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(kind, result.Kind.ToString())
            Assert.Equal(refusal, result.Refusal.ToString())
            Assert.False(rig.Session.IsLive)
            Assert.Null(result.Session)
            Assert.True(stream.ClientClosed)
            Assert.True(K.IsAllZero(pw), "the password array was not wiped after the refusal '" & reason & "'")
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_close_with_no_frame_after_the_sign_in_is_Unreachable_Unanswered_and_wipes_the_password() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Closed())
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Unanswered, result.Reason)
            Assert.True(K.IsAllZero(pw))
            Assert.True(stream.ClientClosed)
        End Using
    End Function

    <Fact>
    Public Async Function A_reset_during_the_sign_in_is_Unreachable_Reset_and_wipes_the_password() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Reset())
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Reset, result.Reason)
            Assert.True(K.IsAllZero(pw))
        End Using
    End Function

    <Fact>
    Public Async Function An_answer_the_client_cannot_read_is_Unreachable_Protocol_and_wipes_the_password() As Task
        Using rig As New ExchangeAdminRig()
            ' admin-auth-ok with expiresSeconds 0: the server never sends one, so the answer is not understood (5.5)
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Supply(K.AuthOkJson(K.Token, 0)))
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Protocol, result.Reason)
            Assert.Equal("auth-ok-shape", result.Detail)
            Assert.False(rig.Session.IsLive)
            Assert.True(stream.ClientClosed)
            Assert.True(K.IsAllZero(pw))
        End Using
    End Function

    <Fact>
    Public Async Function The_sign_in_reply_times_out_at_20_s_on_the_injected_delay_and_wipes_the_password() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))    ' then the server is silent
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(pw)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.SignInBound)
            Assert.False(signIn.IsCompleted)
            Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.SignInBound))
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(signIn, "the sign-in after its bound fired")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Reason)
            Assert.Equal("sign-in-timeout", result.Detail)
            Assert.True(stream.ClientClosed)
            Assert.False(rig.Session.IsLive)
            Assert.True(K.IsAllZero(pw))
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_probe_that_runs_out_its_15_s_bound_is_Unreachable_Timeout_and_the_password_is_never_asked() As Task
        Using rig As New ExchangeAdminRig()
            rig.Scripted.EnqueueProbeHang()
            Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(K.Password())
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.ConnectBound)
            Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.ConnectBound))
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(signIn, "the sign-in after the connect bound fired")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Reason)
            Assert.Equal("connect-timeout", result.Detail)
            Assert.Equal(0, rig.PasswordCalls)
        End Using
    End Function

    ' ================================================================ a call: bounds, one in flight, other ids

    <Fact>
    Public Async Function The_request_reply_times_out_at_30_s_and_a_mutating_operation_ends_the_session() As Task
        Using rig As New ExchangeAdminRig()
            Dim remove As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.AccountRemove(t, i, "bob")
            Dim stream As LockStepStream = K.LiveStream(Tx.Expect("{""schemaVersion"":2,""type"":""admin-request"",""id"":1,""token"":""" & K.Token & """,""op"":""account.remove"",""login"":""bob""}"))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim pending As Task(Of ExchangeRemoteAdminOutcome) = session.CallAsync("account.remove", remove, CancellationToken.None)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.RequestBound)
            Assert.False(pending.IsCompleted)
            Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.RequestBound))
            Using outcome As ExchangeRemoteAdminOutcome = Await K.Within(pending, "the call after its bound fired")
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, outcome.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, outcome.Reason)
                Assert.Equal("request-timeout", outcome.Detail)
            End Using
            ' a timeout may have left half a frame behind: the stream is closed and the session is over
            Assert.True(stream.ClientClosed)
            Assert.False(session.IsLive)
            Using later As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, later.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, later.Reason)
            End Using
            Assert.Equal(2, stream.WrittenFrames().Count)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function Bye_is_bounded_at_1500_ms_when_the_server_never_closes() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(Tx.Expect(K.ByeJson))    ' then silence: the server does not close
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim ending As Task = session.EndAsync()
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.ByeBound)
            Assert.False(ending.IsCompleted, "EndAsync returned before the server closed and before its bound elapsed")
            Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.ByeBound))
            Await K.WithinTask(ending, "the end after the bye bound fired")
            Assert.True(stream.ClientClosed)
            Assert.False(session.IsLive)
            Assert.Equal(ExchangeAdminKit.ByeJson, stream.WrittenFrames().Last())
            Assert.Contains(rig.Log.Lines(), Function(l) Regex.IsMatch(l, "^remote-admin: end Bye [0-9]+$"))
            ' ending an ended session is safe and writes nothing
            Await K.WithinTask(session.EndAsync(), "a second end")
            Assert.Equal(2, stream.WrittenFrames().Count)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_second_call_while_one_is_in_flight_is_Busy_and_writes_nothing() As Task
        Dim stream As New ExchangeAdminManualStream()
        stream.SupplyFrame(K.AuthOkJson(K.Token))
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInOnHandshakeAsync()
            Dim first As Task(Of ExchangeRemoteAdminOutcome) = session.CallAsync("status", K.StatusMembers, CancellationToken.None)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.RequestBound)
            Dim framesBefore As Integer = stream.Frames().Count
            Assert.Equal(2, framesBefore)    ' admin-auth and the first request
            Using second As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("account.list", K.AccountListMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Busy, second.Kind)
            End Using
            Assert.Equal(framesBefore, stream.Frames().Count)
            ' the first call is undisturbed
            stream.SupplyFrame(K.ReplyJson(1, "{""accounts"":1}"))
            Using done As ExchangeRemoteAdminOutcome = Await K.Within(first, "the first call")
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, done.Kind)
            End Using
            Assert.True(session.IsLive)
        End Using
    End Function

    <Fact>
    Public Async Function A_reply_with_another_id_is_skipped_up_to_eight_times() As Task
        Using rig As New ExchangeAdminRig()
            Dim tail As New List(Of ExchangeTranscriptStep) From {ReqStatus(1)}
            For i As Integer = 1 To 8
                tail.Add(Tx.Supply(K.ReplyJson(90 + i, "{""accounts"":0}")))
            Next
            tail.Add(Reply(1))
            Dim stream As LockStepStream = K.LiveStream(tail.ToArray())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
                Assert.Equal(1, outcome.Data.GetProperty("accounts").GetInt32())
            End Using
            Assert.True(session.IsLive)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function The_ninth_reply_of_another_id_is_Protocol_and_the_stream_is_closed() As Task
        Using rig As New ExchangeAdminRig()
            Dim tail As New List(Of ExchangeTranscriptStep) From {ReqStatus(1)}
            For i As Integer = 1 To 9
                tail.Add(Tx.Supply(K.ReplyJson(90 + i, "{""accounts"":0}")))
            Next
            Dim stream As LockStepStream = K.LiveStream(tail.ToArray())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, outcome.Kind)
                Assert.Equal("too-many-other-ids", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed)
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_refusal_after_sign_in_that_is_not_bad_credentials_is_Protocol_and_ends_the_session() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Supply(K.RefusedJson("rate-limited")))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, outcome.Kind)
                Assert.Equal("refused-after-sign-in", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed)
        End Using
    End Function

    <Fact>
    Public Async Function A_frame_of_the_wrong_type_is_Protocol_and_ends_the_session() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Supply(K.ByeJson))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, outcome.Kind)
                Assert.Equal("unexpected-frame", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
        End Using
    End Function

    <Fact>
    Public Async Function An_ok_reply_with_a_body_of_the_wrong_shape_is_Protocol_and_ends_the_session() As Task
        Using rig As New ExchangeAdminRig()
            ' `status` answers an object: an array is a body the parser cannot read (the request may have been carried out)
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Supply(K.ReplyJson(1, "[]")))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, outcome.Kind)
                Assert.Equal("reply-body", outcome.Detail)
            End Using
            Assert.False(session.IsLive)
            Assert.True(stream.ClientClosed)
        End Using
    End Function

    <Fact>
    Public Async Function A_request_the_builder_refuses_is_Protocol_writes_nothing_and_the_session_lives_with_the_same_id() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Reply(1))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim refusing As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.GrantList(t, i, "alice", "guest")
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("grant.list", refusing)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, outcome.Kind)
                Assert.Equal("request-not-built", outcome.Detail)
            End Using
            Assert.True(session.IsLive)
            Assert.Equal(1, stream.WrittenFrames().Count)    ' only the admin-auth
            ' the id was not consumed: the next request is still id 1
            Using next1 As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, next1.Kind)
            End Using
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_request_that_does_not_belong_to_the_call_is_the_callers_bug_and_the_session_lives() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream()
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            ' the op says account.list, the delegate builds `status`
            Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() session.CallAsync("account.list", K.StatusMembers, CancellationToken.None))
            Await Assert.ThrowsAsync(Of ArgumentException)(Function() session.CallAsync("host.settings", K.StatusMembers, CancellationToken.None))
            Assert.True(session.IsLive)
            Assert.Equal(1, stream.WrittenFrames().Count)
            Assert.Equal(0, rig.Time.PendingCount(ExchangeRemoteAdminSession.RequestBound))
        End Using
    End Function

    ' ================================================================ one sign-in, never a second by itself

    <Fact>
    Public Async Function A_second_sign_in_on_a_live_session_is_refused_by_the_class() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream()
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim callsBefore As Integer = rig.Scripted.Calls().Count
            Dim asked As Integer = 0
            Await Assert.ThrowsAsync(Of InvalidOperationException)(
                Function() session.SignInAsync(K.Host, K.Port, "", "", Function() As Char()
                                                                           asked += 1
                                                                           Return K.Password()
                                                                       End Function, CancellationToken.None))
            Assert.Equal(0, asked)
            Assert.Equal(callsBefore, rig.Scripted.Calls().Count)    ' no new connection
            Assert.Equal(1, rig.AuthFrameCount())
            Assert.True(session.IsLive)
        End Using
    End Function

    <Fact>
    Public Async Function A_second_sign_in_while_one_is_in_progress_is_refused_by_the_class() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))    ' the answer never comes
            rig.Plain(stream)
            Dim first As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(K.Password())
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.SignInBound)
            Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() rig.SignInAsync(K.Password()))
            Assert.Equal(1, rig.AuthFrameCount())
            rig.Time.Fire(ExchangeRemoteAdminSession.SignInBound)
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(first, "the first sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
        End Using
    End Function

    <Fact>
    Public Async Function Exactly_one_admin_auth_is_written_across_a_lapse_and_every_later_call() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Tx.Supply(K.RefusedJson("bad-credentials")), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim callsBefore As Integer = rig.Scripted.Calls().Count
            For attempt As Integer = 1 To 4
                Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                    Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
                End Using
            Next
            Assert.Equal(1, rig.AuthFrameCount())
            Assert.Equal(callsBefore, rig.Scripted.Calls().Count)    ' nothing connected again
            Assert.Equal(1, rig.PasswordCalls)                       ' and the password was not asked for again
            Assert.False(session.IsLive)
            rig.Scripted.Verify()
        End Using
    End Function

    ''' <summary>A sign-in that has written its admin-auth and waits; the caller's token is cancelled.</summary>
    Private Shared Async Function CancelledSignInAsync(rig As ExchangeAdminRig, stream As LockStepStream, pw As Char()) As Task(Of ExchangeRemoteAdminSignIn)
        rig.Plain(stream)
        Using cts As New CancellationTokenSource()
            Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(pw, cts.Token)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.SignInBound)
            Assert.False(signIn.IsCompleted)
            cts.Cancel()
            Return Await K.Within(signIn, "the cancelled sign-in")
        End Using
    End Function

    <Fact>
    Public Async Function Cancelling_during_the_sign_in_closes_the_stream_and_is_Cancelled_not_an_error() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))
            Dim pw As Char() = K.Password()
            Dim result As ExchangeRemoteAdminSignIn = Await CancelledSignInAsync(rig, stream, pw)
            Assert.Equal(ExchangeRemoteAdminSignInKind.Cancelled, result.Kind)
            Assert.True(stream.ClientClosed, "the stream was not closed by the cancel")
            Assert.False(rig.Session.IsLive)
            Assert.Null(result.Session)
            Assert.True(K.IsAllZero(pw), "the password array was not wiped after the cancel")
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function Exactly_one_admin_auth_is_written_across_a_sign_in_cancel_and_nothing_connects_again() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))
            Dim result As ExchangeRemoteAdminSignIn = Await CancelledSignInAsync(rig, stream, K.Password())
            Assert.Equal(ExchangeRemoteAdminSignInKind.Cancelled, result.Kind)
            Assert.Equal(1, rig.AuthFrameCount())
            Assert.Equal(New String() {"probe", "connect - -"}, rig.Scripted.Calls().ToArray())
            Assert.Equal(1, rig.PasswordCalls)
            ' no retry follows, whatever time passes
            For Each span As TimeSpan In rig.Time.Spans().Distinct().ToList()
                rig.Time.Fire(span)
            Next
            Await Task.Yield()
            Assert.Equal(2, rig.Scripted.Calls().Count)
            Assert.Equal(1, rig.AuthFrameCount())
        End Using
    End Function

    <Fact>
    Public Async Function Cancelling_while_connecting_is_Cancelled_and_the_password_is_never_asked() As Task
        Using rig As New ExchangeAdminRig()
            rig.Scripted.EnqueueProbe(True, K.Fingerprint).EnqueueConnectHang()
            Using cts As New CancellationTokenSource()
                Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(K.Password(), cts.Token)
                Await K.UntilAsync(Function() rig.Scripted.Calls().Count >= 2, "the connect to be started")
                cts.Cancel()
                Dim result As ExchangeRemoteAdminSignIn = Await K.Within(signIn, "the cancelled connect")
                Assert.Equal(ExchangeRemoteAdminSignInKind.Cancelled, result.Kind)
            End Using
            Assert.Equal(0, rig.PasswordCalls)
            Assert.False(rig.Session.IsLive)
        End Using
    End Function

    ' ================================================================ the password is all zero after every path

    <Fact>
    Public Async Function The_password_array_is_all_zero_after_an_exception_thrown_by_the_stream() As Task
        Using rig As New ExchangeAdminRig()
            ' the script has the server speaking first: the admin-auth write is a divergence and throws out of the stream
            Dim stream As New LockStepStream(Tx.Supply(K.RefusedJson("bad-credentials")))
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Await Assert.ThrowsAsync(Of ExchangeLockStepException)(Function() rig.SignInAsync(pw))
            Assert.True(K.IsAllZero(pw), "the password array survived an exception thrown by the stream")
            Assert.True(stream.ClientClosed, "the connection was not closed after the exception")
            Assert.False(rig.Session.IsLive)
            ' a later sign-in is allowed again (the session is back to idle)
            Assert.Equal(1, rig.PasswordCalls)
        End Using
    End Function

    <Fact>
    Public Async Function The_password_array_is_all_zero_when_it_cannot_be_sent_and_nothing_is_written() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))
            Dim tooLong As Char() = New String("x"c, 1025).ToCharArray()
            rig.Plain(stream)
            Await Assert.ThrowsAsync(Of ArgumentException)(Function() rig.SignInAsync(tooLong))
            Assert.True(K.IsAllZero(tooLong))
            Assert.Equal(0, stream.Writes().Count)
            Assert.True(stream.ClientClosed)
        End Using
    End Function

    <Fact>
    Public Async Function The_password_array_is_all_zero_when_the_write_fails() As Task
        Dim stream As New ExchangeAdminManualStream() With {.WriteFault = New InvalidOperationException("the stream broke")}
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream))
            Dim pw As Char() = K.Password()
            Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() rig.SignInAsync(pw))
            Assert.True(K.IsAllZero(pw))
            Assert.True(stream.Disposed)
        End Using
    End Function

    <Fact>
    Public Async Function The_frame_writer_wipes_the_frame_it_wrote() As Task
        Dim stream As New ExchangeAdminManualStream()
        stream.SupplyFrame(K.AuthOkJson(K.Token))
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream))
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInOnHandshakeAsync()
            stream.SupplyFrame(K.ReplyJson(1, "{""accounts"":1}"))
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
            End Using
            Dim copies As List(Of Byte()) = stream.WriteCopies()
            Dim references As List(Of Byte()) = stream.WriteReferences()
            Assert.Equal(2, references.Count)
            ' the copy taken at write time carried the secret; the array the writer handed over is wiped afterwards
            Assert.Contains(Secret, Encoding.UTF8.GetString(copies(0)))
            Assert.Contains(K.Token, Encoding.UTF8.GetString(copies(1)))
            For i As Integer = 0 To references.Count - 1
                Assert.True(references(i).All(Function(b) b = 0), "the frame of write " & (i + 1).ToString(CultureInfo.InvariantCulture) & " was not wiped by the writer")
            Next
        End Using
    End Function

    ' ================================================================ log redaction

    Private Shared Async Function RunLoggedScenariosAsync(sink As ExchangeAdminLogSink) As Task
        Const invitationSecret As String = "INVITE-CODE-SECRET-xyz"
        Const hashSecret As String = "deadbeefcafe0123456789"
        Const bodySecret As String = "reply-body-secret-value"
        Const carolPassword As String = "CarolSecret!9"

        ' 1. a first contact (fingerprint and key pin are on the screen of the question, never in the log)
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            rig.Scripted.EnqueueProbe(False, K.Fingerprint, K.KeyPin)
            Dim r As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(K.Password()), "the first contact")
            Assert.Equal(ExchangeRemoteAdminSignInKind.NeedsFirstContact, r.Kind)
        End Using

        ' 2. a certificate that changed
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            rig.Scripted.EnqueueConnectFailure(ExchangeConnectFailure.PinMismatch, "SHA256:" & New String("Z"c, 43)).EnqueueProbe(False, "SHA256:" & New String("Z"c, 43), "SHA256:" & New String("Y"c, 43))
            Dim r As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(K.Password(), Nothing, K.Fingerprint, K.KeyPin), "the changed certificate")
            Assert.Equal(ExchangeRemoteAdminSignInKind.CertificateChanged, r.Kind)
        End Using

        ' 3. a session with calls that carry names, passwords, codes and bodies, a failure, a skipped id and a lapse
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            Dim add As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.AccountAdd(t, i, "carol-login", carolPassword, "member")
            Dim invite As Func(Of String, Long, ExchangeEnvelope) = Function(t As String, i As Long) ExchangeAdminWire.InvitationCreate(t, i, "member", "carol-login")
            Dim stream As LockStepStream = K.LiveStream(
                ReqStatus(1), Tx.Supply(K.ReplyJson(7, "{""accounts"":0}")), Tx.Supply(K.ReplyJson(1, "{""accounts"":1,""secretMember"":""" & bodySecret & """}")),
                Tx.Expect("{""schemaVersion"":2,""type"":""admin-request"",""id"":2,""token"":""" & K.Token & """,""op"":""account.add"",""login"":""carol-login"",""password"":""" & carolPassword & """,""kind"":""member""}"),
                Tx.Supply(K.FailedJson(2, "login-taken")),
                Tx.Expect("{""schemaVersion"":2,""type"":""admin-request"",""id"":3,""token"":""" & K.Token & """,""op"":""invitation.create"",""kind"":""member"",""expectedLogin"":""carol-login""}"),
                Tx.Supply(K.ReplyJson(3, "{""code"":""" & invitationSecret & """,""invitation"":{""codeHash"":""" & hashSecret & """}}")),
                ReqStatus(4), Tx.Supply(K.RefusedJson("bad-credentials")), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Using o As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, o.Kind)
            End Using
            Using o As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("account.add", add)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, o.Kind)
                Assert.Equal("login-taken", o.Code)
            End Using
            Using o As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("invitation.create", invite)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, o.Kind)
            End Using
            Using o As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, o.Kind)
            End Using
        End Using

        ' 4. a polite end, a refusal, a reset and a connect that fails
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            Dim stream As LockStepStream = K.LiveStream(Tx.Expect(K.ByeJson), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Await K.WithinTask(session.EndAsync(), "the polite end")
        End Using
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            rig.Plain(New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Supply(K.RefusedJson("bad-credentials")), Tx.Closed()))
            Await K.Within(rig.SignInAsync(K.Password()), "the refusal")
        End Using
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            rig.Plain(New LockStepStream(Tx.Expect(K.AuthJson(Secret)), Tx.Reset()))
            Await K.Within(rig.SignInAsync(K.Password()), "the reset")
        End Using
        Using rig As New ExchangeAdminRig(Nothing, Nothing, sink)
            rig.Scripted.EnqueueProbeFailure(ExchangeConnectFailure.Network)
            Await K.Within(rig.SignInAsync(K.Password()), "the failed connect")
        End Using
    End Function

    <Fact>
    Public Async Function The_log_holds_no_password_token_host_port_name_fingerprint_invitation_or_reply_body() As Task
        Dim sink As New ExchangeAdminLogSink()
        Await RunLoggedScenariosAsync(sink)
        Dim lines As List(Of String) = sink.Lines()
        Assert.True(lines.Count >= 12, "the scenarios logged only " & lines.Count.ToString(CultureInfo.InvariantCulture) & " line(s)")
        ' the shape: remote-admin: <event> <kind> <ms>, and nothing else
        For Each line As String In lines
            Assert.True(Regex.IsMatch(line, "^remote-admin: (connect|trust|sign-in|call|end) [A-Za-z0-9._-]+ [0-9]+$"), "a log line is not in the form `remote-admin: <event> <kind> <ms>`: " & line)
        Next
        ' the known secrets, every one of them was fed to the session
        Dim secrets As String() = {
            K.Host, K.Port.ToString(CultureInfo.InvariantCulture), K.SecretPassword, "AdminSecret", K.Token, New String("T"c, 20), K.Fingerprint, New String("F"c, 20), K.KeyPin, New String("K"c, 20),
            New String("Z"c, 20), New String("Y"c, 20), "carol", "CarolSecret", "INVITE-CODE-SECRET", "deadbeefcafe", "reply-body-secret-value", "secretMember", "login-taken", "SHA256", "{", "}", """", "["}
        Dim text As String = String.Join(vbLf, lines)
        For Each secret As String In secrets
            Assert.False(text.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0, "the log holds '" & secret & "': " & text)
        Next
        ' what it does hold: the operation name of a call that was done, the outcome kind of one that was not
        Assert.Contains("remote-admin: call status 0", lines)
        Assert.Contains("remote-admin: call invitation.create 0", lines)
        Assert.Contains("remote-admin: call Failed 0", lines)
        Assert.Contains("remote-admin: call Lapsed 0", lines)
        Assert.Contains("remote-admin: end Lapsed 0", lines)
        Assert.Contains("remote-admin: end Bye 0", lines)
        Assert.Contains("remote-admin: trust AskFirstContact 0", lines)
        Assert.Contains("remote-admin: trust StopChanged 0", lines)
    End Function

    ' ================================================================ the outcome's data

    <Fact>
    Public Async Function A_Done_outcome_data_is_valid_after_the_call_returns_and_is_disposed_with_the_outcome() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(ReqStatus(1), Reply(1), Tx.Expect(K.ByeJson), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Dim outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
            Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
            Assert.True(outcome.HasData)
            ' valid after the call returned, after the session ended and after its stream was closed
            Await K.WithinTask(session.EndAsync(), "the end")
            Assert.True(stream.ClientClosed)
            Assert.Equal(1, outcome.Data.GetProperty("accounts").GetInt32())
            Assert.Equal(JsonValueKind.Object, outcome.Data.ValueKind)
            outcome.Dispose()
            Assert.False(outcome.HasData)
            Assert.Equal(JsonValueKind.Undefined, outcome.Data.ValueKind)
            outcome.Dispose()    ' twice is safe
            Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, outcome.Kind)
        End Using
    End Function

    ' ================================================================ the four behaviours fixed by reading

    <Fact>
    Public Async Function Dispose_during_a_sign_in_cancels_it_and_the_session_never_goes_live() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As New LockStepStream(Tx.Expect(K.AuthJson(Secret)))    ' the answer never comes
            Dim pw As Char() = K.Password()
            rig.Plain(stream)
            Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(pw)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.SignInBound)
            rig.Session.Dispose()
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(signIn, "the sign-in after Dispose")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Cancelled, result.Kind)
            Assert.False(rig.Session.IsLive)
            Assert.Null(result.Session)
            Assert.True(stream.ClientClosed)
            Assert.True(K.IsAllZero(pw))
            Assert.Equal(1, rig.AuthFrameCount())
            ' the session stays dead: a call writes nothing
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
            End Using
            Assert.Equal(1, stream.WrittenFrames().Count)
        End Using
    End Function

    <Fact>
    Public Async Function An_answer_that_arrives_as_the_session_is_disposed_is_dropped_and_the_session_never_goes_live() As Task
        Dim stream As New ExchangeAdminManualStream()
        stream.SupplyFrame(K.AuthOkJson(K.Token))
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream))
            ' the window is closed (Dispose) in the very read that brings the good answer
            stream.BeforeRead = Sub() rig.Session.Dispose()
            Dim pw As Char() = K.Password()
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw), "the sign-in")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Cancelled, result.Kind)
            Assert.False(rig.Session.IsLive)
            Assert.Null(result.Session)
            Assert.True(stream.Disposed, "the connection of a dropped sign-in was left open")
            Assert.True(K.IsAllZero(pw))
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_call_after_EndAsync_has_returned_is_Lapsed_session_ended_and_writes_nothing() As Task
        Using rig As New ExchangeAdminRig()
            Dim stream As LockStepStream = K.LiveStream(Tx.Expect(K.ByeJson), Tx.Closed())
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInAsync(stream)
            Await K.WithinTask(session.EndAsync(), "the end")
            Using outcome As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
                Assert.Equal("session-ended", outcome.Detail)
            End Using
            Assert.Equal(2, stream.WrittenFrames().Count)    ' admin-auth and bye, nothing after
            rig.Scripted.Verify()
        End Using
    End Function

    <Fact>
    Public Async Function A_call_after_EndAsync_was_requested_is_Lapsed_session_ended_not_Busy_and_writes_nothing() As Task
        ' the in-flight call cannot finish by itself: its stream ignores the token (a stuck socket) and its 30 s delay is held
        Dim time As New ManualAdminTime()
        Dim hold As New TaskCompletionSource(Of Boolean)()
        Dim delay As Func(Of TimeSpan, CancellationToken, Task) = Function(span As TimeSpan, ct As CancellationToken) As Task
                                                                      If span = ExchangeRemoteAdminSession.RequestBound Then Return hold.Task
                                                                      Return time.Delay(span, ct)
                                                                  End Function
        Dim stream As New ExchangeAdminManualStream() With {.HonourCancel = False}
        stream.SupplyFrame(K.AuthOkJson(K.Token))
        Using rig As New ExchangeAdminRig(New ExchangeAdminStreamHandshake(stream), delay, Nothing, time)
            Dim session As ExchangeRemoteAdminSession = Await rig.SignedInOnHandshakeAsync()
            Dim inFlight As Task(Of ExchangeRemoteAdminOutcome) = session.CallAsync("status", K.StatusMembers, CancellationToken.None)
            Await K.UntilAsync(Function() stream.ReadPending, "the reply wait of the call")
            Dim ending As Task = session.EndAsync()
            Assert.False(ending.IsCompleted, "EndAsync did not wait for the call it cancelled")
            Dim framesBefore As Integer = stream.Frames().Count
            Using late As ExchangeRemoteAdminOutcome = Await rig.CallOpAsync("account.list", K.AccountListMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, late.Kind)
                Assert.Equal("session-ended", late.Detail)
            End Using
            Assert.Equal(framesBefore, stream.Frames().Count)
            ' now the stuck read ends: the cancelled call reports it, and EndAsync goes on to say bye
            stream.CancelPending()
            Using cancelled As ExchangeRemoteAdminOutcome = Await K.Within(inFlight, "the cancelled call")
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, cancelled.Kind)
                Assert.Equal(ExchangeRemoteAdminUnreachableReason.Cancelled, cancelled.Reason)
            End Using
            Await K.PendingAsync(time, ExchangeRemoteAdminSession.ByeBound)
            time.Fire(ExchangeRemoteAdminSession.ByeBound)
            Await K.WithinTask(ending, "the end")
            Assert.Equal(K.ByeJson, stream.Frames().Last())
            Assert.Equal(3, stream.Frames().Count)    ' admin-auth, the cancelled request, bye
            Assert.False(session.IsLive)
        End Using
    End Function

    <Fact>
    Public Async Function A_connection_that_completes_after_its_connect_bound_elapsed_is_disposed() As Task
        Dim stream As New ExchangeAdminManualStream()
        Dim gate As New TaskCompletionSource(Of ExchangeTlsConnection)()
        Dim handshake As New ExchangeAdminStreamHandshake(stream) With {.ConnectGate = gate, .Verdict = ExchangeTrustVerdict.PinMatch}
        Using rig As New ExchangeAdminRig(handshake)
            ' a pin is held, so the pinned connect goes first and is what runs out its bound
            Dim signIn As Task(Of ExchangeRemoteAdminSignIn) = rig.SignInAsync(K.Password(), Nothing, K.Fingerprint, K.KeyPin)
            Await K.PendingAsync(rig.Time, ExchangeRemoteAdminSession.ConnectBound)
            Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.ConnectBound))
            Dim result As ExchangeRemoteAdminSignIn = Await K.Within(signIn, "the sign-in after the connect bound fired")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, result.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Reason)
            Assert.False(stream.Disposed)
            ' the connect completes in the instant after the bound: nobody will ever read it, so it must be closed
            gate.SetResult(New ExchangeTlsConnection(stream, K.Fingerprint, ExchangeTrustVerdict.PinMatch, K.KeyPin))
            Await K.UntilAsync(Function() stream.Disposed, "the late connection to be disposed")
            Assert.Equal(0, rig.PasswordCalls)
            Assert.Equal(0, stream.WriteCopies().Count)
        End Using
    End Function

    ' ================================================================ loopback TLS: a real handshake through ExchangeAdminHandshake.Instance

    ''' <summary>A TLS stub server on 127.0.0.1 that counts the application bytes of every accepted connection, answers an
    ''' admin-auth with an admin-auth-ok, and ends on a bye or on the client's close.</summary>
    Private NotInheritable Class AdminTlsServer
        Implements IDisposable

        Private ReadOnly _gate As New Object()
        Private ReadOnly _bytes As New Dictionary(Of Integer, Integer)
        Private _finished As Integer
        Private _authFrames As Integer
        Private _lastPassword As String = ""
        Public ReadOnly Server As ExchangeStubTlsServer

        Public Sub New(Optional certificate As X509Certificate2 = Nothing)
            Server = New ExchangeStubTlsServer(AddressOf Handle, certificate)
        End Sub

        Public ReadOnly Property AuthFrames As Integer
            Get
                SyncLock _gate
                    Return _authFrames
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property LastPassword As String
            Get
                SyncLock _gate
                    Return _lastPassword
                End SyncLock
            End Get
        End Property

        ''' <summary>Application bytes the client sent on the n-th accepted connection (1-based); -1 when it is not finished.</summary>
        Public Function BytesOn(index As Integer) As Integer
            SyncLock _gate
                Dim n As Integer = 0
                If _bytes.TryGetValue(index, n) Then Return n
                Return -1
            End SyncLock
        End Function

        Public Function TotalBytes() As Integer
            SyncLock _gate
                Return _bytes.Values.Sum()
            End SyncLock
        End Function

        Private Async Function Handle(c As ExchangeStubConnection) As Task
            Dim total As Integer = 0
            Try
                Dim pending As New List(Of Byte)
                Dim chunk(4095) As Byte
                While True
                    Dim n As Integer = Await c.Stream.ReadAsync(chunk, 0, chunk.Length, c.Token).ConfigureAwait(False)
                    If n = 0 Then Return    ' the client left
                    total += n
                    For i As Integer = 0 To n - 1
                        pending.Add(chunk(i))
                    Next
                    While pending.Count >= 4
                        Dim len As Integer = (CInt(pending(0)) << 24) Or (CInt(pending(1)) << 16) Or (CInt(pending(2)) << 8) Or CInt(pending(3))
                        If pending.Count < 4 + len Then Exit While
                        Dim body As Byte() = pending.GetRange(4, len).ToArray()
                        pending.RemoveRange(0, 4 + len)
                        Dim env As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(body)
                        If env.Is("admin-auth") Then
                            SyncLock _gate
                                _authFrames += 1
                                _lastPassword = env.Text("password")
                            End SyncLock
                            Await c.WriteAsync(New ExchangeEnvelope("admin-auth-ok").SetText("token", K.Token).SetNumber("expiresSeconds", 900)).ConfigureAwait(False)
                        ElseIf env.Is("bye") Then
                            Return
                        End If
                    End While
                End While
            Finally
                SyncLock _gate
                    _bytes(c.Index) = total
                    _finished += 1
                End SyncLock
            End Try
        End Function

        ''' <summary>Returns when at least <paramref name="accepted"/> connections were accepted and every accepted one has ended.</summary>
        Public Function SettledAsync(accepted As Integer) As Task
            Return K.UntilAsync(Function()
                                    SyncLock _gate
                                        Return Server.Accepted >= accepted AndAlso _finished >= Server.Accepted
                                    End SyncLock
                                End Function, "the stub server to see its connection(s) end")
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Server.Dispose()
        End Sub

    End Class

    Private Shared Function KeyPinOf(cert As X509Certificate2) As String
        Return ExchangeTrust.FingerprintOf(cert.PublicKey.ExportSubjectPublicKeyInfo())
    End Function

    ''' <summary>Two certificates over ONE key: a renewal that kept the key (different leaf, same key pin).</summary>
    Private Shared Function RenewalPair() As X509Certificate2()
        Using rsa As RSA = RSA.Create(2048)
            Dim result(1) As X509Certificate2
            For i As Integer = 0 To 1
                Dim req As New CertificateRequest("CN=exchange-test-" & i.ToString(CultureInfo.InvariantCulture), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                Dim san As New SubjectAlternativeNameBuilder()
                san.AddIpAddress(IPAddress.Loopback)
                san.AddDnsName("localhost")
                req.CertificateExtensions.Add(san.Build())
                req.CertificateExtensions.Add(New X509EnhancedKeyUsageExtension(New OidCollection From {New Oid("1.3.6.1.5.5.7.3.1")}, False))
                Using cert As X509Certificate2 = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1 - i), DateTimeOffset.UtcNow.AddDays(2 + i))
                    result(i) = X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), Nothing)
                End Using
            Next
            Return result
        End Using
    End Function

    <Fact>
    Public Async Function Loopback_tls_an_unpinned_self_signed_leaf_needs_first_contact_and_no_application_byte_reaches_the_server() As Task
        Using tls As New AdminTlsServer()
            Using rig As New ExchangeAdminRig(ExchangeAdminHandshake.Instance)
                Dim pw As Char() = K.Password()
                Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw, Nothing, "", "", "127.0.0.1", tls.Server.Port), "the sign-in to an unpinned server")
                Assert.Equal(ExchangeRemoteAdminSignInKind.NeedsFirstContact, result.Kind)
                Assert.Equal(tls.Server.Fingerprint, result.Fingerprint)
                Assert.Equal(KeyPinOf(tls.Server.Certificate), result.KeyPin)
                Assert.Equal(0, rig.PasswordCalls)    ' the question comes BEFORE the password is read
                Assert.False(K.IsAllZero(pw), "the untouched password array must still hold the text (it was never read)")
                Await tls.SettledAsync(1)
                Assert.Equal(0, tls.TotalBytes())
                Assert.Equal(0, tls.AuthFrames)
                Assert.False(rig.Session.IsLive)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Loopback_tls_after_a_Yes_the_next_connection_carries_the_admin_auth() As Task
        Using tls As New AdminTlsServer()
            Using rig As New ExchangeAdminRig(ExchangeAdminHandshake.Instance)
                Dim first As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(K.Password(), Nothing, "", "", "127.0.0.1", tls.Server.Port), "the first contact")
                Assert.Equal(ExchangeRemoteAdminSignInKind.NeedsFirstContact, first.Kind)
                ' the person answers Yes: the presented pins go to a NEW sign-in
                Dim pw As Char() = K.Password()
                Dim second As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw, Nothing, first.Fingerprint, first.KeyPin, "127.0.0.1", tls.Server.Port), "the sign-in with the pins")
                Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, second.Kind)
                Assert.True(rig.Session.IsLive)
                Assert.Equal(1, rig.PasswordCalls)
                Assert.True(K.IsAllZero(pw))
                Await K.WithinTask(rig.Session.EndAsync(), "the end")
                Await tls.SettledAsync(2)
                Assert.Equal(1, tls.AuthFrames)
                Assert.Equal(K.SecretPassword, tls.LastPassword)
                Assert.Equal(0, tls.BytesOn(1))        ' the probe carried nothing
                Assert.True(tls.BytesOn(2) > 0)        ' the pinned connection carried the admin-auth and the bye
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Loopback_tls_a_pinned_leaf_that_changed_is_CertificateChanged_with_zero_bytes_written() As Task
        Using tls As New AdminTlsServer()
            Dim pinnedLeaf As String = tls.Server.Fingerprint
            Dim pinnedKey As String = KeyPinOf(tls.Server.Certificate)
            Dim other As X509Certificate2 = NewSelfSignedCertificate()
            tls.Server.Certificate = other    ' the server now shows another certificate, another key
            Using rig As New ExchangeAdminRig(ExchangeAdminHandshake.Instance)
                Dim pw As Char() = K.Password()
                Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw, Nothing, pinnedLeaf, pinnedKey, "127.0.0.1", tls.Server.Port), "the sign-in to a server whose certificate changed")
                Assert.Equal(ExchangeRemoteAdminSignInKind.CertificateChanged, result.Kind)
                Assert.Equal(pinnedLeaf, result.PinnedFingerprint)
                Assert.Equal(ExchangeWireTestKit.FingerprintOf(other), result.PresentedFingerprint)
                Assert.Equal(0, rig.PasswordCalls)
                Assert.False(K.IsAllZero(pw))
                Await tls.SettledAsync(1)    ' the probe that read the new certificate
                Assert.Equal(0, tls.TotalBytes())
                Assert.Equal(0, tls.AuthFrames)
                Assert.False(rig.Session.IsLive)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Loopback_tls_a_key_pin_match_with_a_new_leaf_proceeds_and_hands_back_no_trust_to_write() As Task
        Dim pair As X509Certificate2() = RenewalPair()
        Using tls As New AdminTlsServer(pair(0))
            Dim pinnedLeaf As String = ExchangeWireTestKit.FingerprintOf(pair(0))
            Dim pinnedKey As String = KeyPinOf(pair(0))
            Assert.Equal(pinnedKey, KeyPinOf(pair(1)))
            Assert.NotEqual(pinnedLeaf, ExchangeWireTestKit.FingerprintOf(pair(1)))
            tls.Server.Certificate = pair(1)    ' renewed: a new leaf over the same key
            Using rig As New ExchangeAdminRig(ExchangeAdminHandshake.Instance)
                Dim pw As Char() = K.Password()
                Dim result As ExchangeRemoteAdminSignIn = Await K.Within(rig.SignInAsync(pw, Nothing, pinnedLeaf, pinnedKey, "127.0.0.1", tls.Server.Port), "the sign-in to a renewed certificate")
                Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, result.Kind)
                ' the session keeps no trust and returns none: no new fingerprint or key pin for a caller to store
                Assert.Equal("", result.Fingerprint)
                Assert.Equal("", result.KeyPin)
                Assert.Equal("", result.PinnedFingerprint)
                Assert.Equal("", result.PresentedFingerprint)
                Assert.Equal(1, rig.PasswordCalls)
                Assert.True(K.IsAllZero(pw))
                Await K.WithinTask(rig.Session.EndAsync(), "the end")
                Await tls.SettledAsync(1)
                Assert.Equal(1, tls.Server.Accepted)    ' one connection: the pinned one carried the secret, no probe came first
                Assert.Equal(1, tls.AuthFrames)
            End Using
        End Using
    End Function

    <Fact(Skip:="The platform-valid path cannot be exercised with a self-signed leaf on a machine that does not trust it; it is covered by the table of 5.4 in ExchangeAdminTrustTests (every combination of pin held, chain valid, leaf match, key match) and by the scripted PlatformValid sign-ins above.")>
    Public Sub Loopback_tls_the_platform_valid_path_is_covered_by_the_table_not_by_a_socket()
    End Sub

End Class
#End If
