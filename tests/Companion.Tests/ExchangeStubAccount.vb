Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion

' Ticket 180 T-2 tests: a small exchange-server stand-in for the device agent, speaking the codec of src\Exchange over TLS on 127.0.0.1.
' It knows one account (alice), a password, enrolled devices and their tokens, a pairing code, a directory with a revision, and can be
' told to refuse the next `hello`, to stop echoing keepalives or to drop every connection. It records what it received, per connection,
' in order - which is what the tests assert on.

Friend NotInheritable Class ExchangeStubAccount
    Implements IDisposable

    Public Const Login As String = "alice"
    Public Const Password As String = "Correct-Horse-Battery-9"
    Public Const PairingCode As String = "ABCD2345"
    ''' <summary>A one-time invitation code in the wire's id form (22 characters that decode to 16 bytes), as an operator's
    ''' invitation string carries it (ticket 224 rule 1). A secret holder.</summary>
    Public Const Invitation As String = "QPmYv_VdgvQYk9Y5TS241A"
    ''' <summary>The operator's shared invitation password (224 rule 2), off by default: "" until a test turns it on.</summary>
    Public Property SharedInvitationPassword As String = ""

    Private ReadOnly _gate As New Object()
    Private ReadOnly _tokens As New Dictionary(Of String, String)(StringComparer.Ordinal)   ' deviceId -> token
    Private ReadOnly _received As New List(Of Tuple(Of Integer, String))
    Private ReadOnly _live As New Dictionary(Of Integer, ExchangeStubConnection)
    Private ReadOnly _helloRefusals As New ConcurrentQueue(Of String)
    Private ReadOnly _enrollPasswords As New List(Of String)
    Private ReadOnly _revoked As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly _enrollRefusals As New ConcurrentQueue(Of String)
    Private ReadOnly _enrollRoles As New List(Of String)
    Private ReadOnly _enrollCapabilities As New List(Of String)
    Private ReadOnly _enrollNames As New List(Of String)
    Private ReadOnly _enrollDeviceIds As New List(Of String)
    Private ReadOnly _registerPasswords As New List(Of String)
    Private ReadOnly _registerCodes As New List(Of String)
    Private ReadOnly _registerRefusals As New ConcurrentQueue(Of String)
    Private ReadOnly _deviceByConnection As New Dictionary(Of Integer, String)()
    Private _password As String = Password
    Private _usedInvitation As Boolean

    ''' <summary>Further logins that share the account password (ticket 187: two logins on one server).</summary>
    Public ReadOnly ExtraLogins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Ticket 224 rule 4: registrations past this count of accounts are refused with `capacity`. 0 = the default of ten.</summary>
    Public Property MaxAccounts As Integer = 10

    Private ReadOnly _registeredLogins As New List(Of String)

    ''' <summary>The logins that REGISTERED on this stub, in arrival order.</summary>
    Public Function RegisteredLogins() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_registeredLogins)
        End SyncLock
    End Function

    ''' <summary>Every invitation code a `register` presented, in arrival order (a wrong one too: what the no-oracle tests compare).</summary>
    Public Function RegisterCodesSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_registerCodes)
        End SyncLock
    End Function

    ''' <summary>The next `register` is refused with this reason (queued: one per register), before anything is looked at.</summary>
    Public Sub RefuseNextRegister(reason As String)
        _registerRefusals.Enqueue(reason)
    End Sub

    ''' <summary>Spends the invitation (a second use is a wrong code: the same `bad-credentials`, no oracle - 224 rule 6).</summary>
    Public Sub UseInvitation()
        SyncLock _gate
            _usedInvitation = True
        End SyncLock
    End Sub

    ''' <summary>Ticket 224 rules 8-9: the password changes OUTSIDE this device (on the phone, by the operator's reset): every
    ''' token dies, so every device's next `hello` is `bad-credentials`.</summary>
    Public Sub ChangePasswordFromElsewhere(newPassword As String)
        SyncLock _gate
            _password = newPassword
            _tokens.Clear()
        End SyncLock
    End Sub

    ''' <summary>Every `register`'s password, in arrival order (what the secrets tests assert never reached a store or a log).</summary>
    Public Function RegisterPasswordsSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_registerPasswords)
        End SyncLock
    End Function

    ''' <summary>The next `enroll` is refused with this reason (queued: one per enroll), before the credentials are looked at.</summary>
    Public Sub RefuseNextEnroll(reason As String)
        _enrollRefusals.Enqueue(reason)
    End Sub

    ''' <summary>The roles every `enroll` declared, as "role,role" per enroll, in arrival order.</summary>
    Public Function EnrollRolesSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_enrollRoles)
        End SyncLock
    End Function

    Public Function EnrollCapabilitiesSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_enrollCapabilities)
        End SyncLock
    End Function

    Public Function EnrollNamesSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_enrollNames)
        End SyncLock
    End Function

    Public Function EnrollDeviceIdsSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_enrollDeviceIds)
        End SyncLock
    End Function

    Public ReadOnly Server As ExchangeStubTlsServer

    Public Property KeepaliveSeconds As Integer = 30
    Public Property EchoKeepalive As Boolean = True
    Public Property Features As String() = {"directory", "tunnel", "pairing"}
    Public Property PairingRefusal As String = ""
    Public Property Revision As Long = 7
    Public Property Devices As New List(Of String)
    Public Property Resources As New List(Of String)
    Public Property Broadcasts As New List(Of String)

    Public Sub New(Optional certificate As System.Security.Cryptography.X509Certificates.X509Certificate2 = Nothing)
        Server = New ExchangeStubTlsServer(AddressOf HandleAsync, certificate)
    End Sub

    Public ReadOnly Property Address As String
        Get
            Return Server.Address
        End Get
    End Property

    Public ReadOnly Property Fingerprint As String
        Get
            Return Server.Fingerprint
        End Get
    End Property

    ''' <summary>The next `hello` is refused with this reason (queued: one per hello).</summary>
    Public Sub RefuseNextHello(reason As String)
        _helloRefusals.Enqueue(reason)
    End Sub

    ''' <summary>Every envelope type received, as "connection:type", in arrival order.</summary>
    Public Function Received() As List(Of String)
        SyncLock _gate
            Return _received.Select(Function(t) t.Item1.ToString() & ":" & t.Item2).ToList()
        End SyncLock
    End Function

    Public Function ReceivedOn(connection As Integer) As List(Of String)
        SyncLock _gate
            Return _received.Where(Function(t) t.Item1 = connection).Select(Function(t) t.Item2).ToList()
        End SyncLock
    End Function

    Public Function CountOf(typeName As String) As Integer
        SyncLock _gate
            Return _received.Where(Function(t) t.Item2 = typeName).Count()
        End SyncLock
    End Function

    Public Function EnrollPasswordsSeen() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_enrollPasswords)
        End SyncLock
    End Function

    ''' <summary>Enrolls a device without any stream (the state a device is in after an earlier enrollment) and returns its token.</summary>
    Public Function PreEnroll(deviceId As String) As String
        Dim token As String = NewToken()
        SyncLock _gate
            _tokens(deviceId) = token
            _revoked.Remove(deviceId)
        End SyncLock
        Return token
    End Function

    ''' <summary>Another device of the account revokes this one: its token dies, and the next `hello` with it is `device-revoked`.</summary>
    Public Sub RevokeFromServer(deviceId As String)
        SyncLock _gate
            _revoked.Add(deviceId)
            _tokens.Remove(deviceId)
        End SyncLock
    End Sub

    ''' <summary>Writes one frame with this exact JSON body to every live control stream (a type the client does not know, say).</summary>
    Public Async Function PushBodyAsync(json As String) As Task
        Dim body As Byte() = System.Text.Encoding.UTF8.GetBytes(json)
        Dim frame(3 + body.Length) As Byte
        frame(0) = CByte((body.Length >> 24) And &HFF)
        frame(1) = CByte((body.Length >> 16) And &HFF)
        frame(2) = CByte((body.Length >> 8) And &HFF)
        frame(3) = CByte(body.Length And &HFF)
        body.CopyTo(frame, 4)
        Dim targets As List(Of ExchangeStubConnection)
        SyncLock _gate
            targets = _live.Values.ToList()
        End SyncLock
        For Each c As ExchangeStubConnection In targets
            Try
                Await c.Stream.WriteAsync(frame, 0, frame.Length)
                Await c.Stream.FlushAsync()
            Catch ex As Exception When TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is ObjectDisposedException
            End Try
        Next
    End Function

    Public Function TokenOf(deviceId As String) As String
        SyncLock _gate
            Dim t As String = Nothing
            Return If(_tokens.TryGetValue(deviceId, t), t, "")
        End SyncLock
    End Function

    Public Function Enrolled() As List(Of String)
        SyncLock _gate
            Return _tokens.Keys.ToList()
        End SyncLock
    End Function

    Public Function RevokedDevices() As List(Of String)
        SyncLock _gate
            Return _revoked.ToList()
        End SyncLock
    End Function

    Public Function LiveConnections() As Integer
        SyncLock _gate
            Return _live.Count
        End SyncLock
    End Function

    ''' <summary>Pushes an envelope to every live control stream.</summary>
    Public Async Function PushAsync(env As ExchangeEnvelope) As Task
        Dim targets As List(Of ExchangeStubConnection)
        SyncLock _gate
            targets = _live.Values.ToList()
        End SyncLock
        For Each c As ExchangeStubConnection In targets
            Try
                Await c.WriteAsync(env)
            Catch ex As Exception When TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is ObjectDisposedException
            End Try
        Next
    End Function

    ''' <summary>Closes every live control stream from the server's side.</summary>
    Public Sub DropAll()
        Dim targets As List(Of ExchangeStubConnection)
        SyncLock _gate
            targets = _live.Values.ToList()
        End SyncLock
        For Each c As ExchangeStubConnection In targets
            Try : c.Stream.Dispose() : Catch : End Try
        Next
    End Sub

    Private Sub Note(connection As Integer, typeName As String)
        SyncLock _gate
            _received.Add(Tuple.Create(connection, typeName))
        End SyncLock
    End Sub

    ''' <summary>The device id of the stream a connection index carries ("" when none): which device asked for the change.</summary>
    Private Function DeviceOfConnection(connection As Integer) As String
        SyncLock _gate
            Dim d As String = Nothing
            Return If(_deviceByConnection.TryGetValue(connection, d), d, "")
        End SyncLock
    End Function

    Private Function DirectoryEnvelope() As ExchangeEnvelope
        Return New ExchangeEnvelope("directory").SetNumber("revision", Revision).SetRecords("devices", Devices).SetRecords("resources", Resources).SetRecords("broadcasts", Broadcasts)
    End Function

    Private Async Function HandleAsync(c As ExchangeStubConnection) As Task
        Dim first As ExchangeEnvelope = Await c.ReadAsync()
        Note(c.Index, first.Type)
        Select Case first.Type
            Case "enroll"
                SyncLock _gate
                    _enrollPasswords.Add(first.Text("password"))
                    _enrollRoles.Add(String.Join(",", first.TextList("roles")))
                    _enrollCapabilities.Add(String.Join(",", first.TextList("capabilities")))
                    _enrollNames.Add(first.Text("deviceName"))
                    _enrollDeviceIds.Add(first.Text("deviceId"))
                End SyncLock
                Dim enrollRefusal As String = Nothing
                If _enrollRefusals.TryDequeue(enrollRefusal) Then
                    Await c.Refuse(enrollRefusal)
                    Return
                End If
                Dim ok As Boolean = (String.Equals(first.Text("login"), Login, StringComparison.OrdinalIgnoreCase) OrElse ExtraLogins.Contains(first.Text("login"))) AndAlso
                    (first.Text("password") = _password OrElse (first.Text("pairingCode").Length > 0 AndAlso first.Text("pairingCode") = PairingCode))
                If Not ok Then
                    Await c.Refuse("bad-credentials")
                    Return
                End If
                Dim deviceId As String = first.Text("deviceId")
                Dim token As String = NewToken()
                SyncLock _gate
                    _tokens(deviceId) = token
                    _revoked.Remove(deviceId)
                End SyncLock
                SyncLock _gate
                    _deviceByConnection(c.Index) = deviceId
                End SyncLock
                Await c.WriteAsync(New ExchangeEnvelope("enrolled").SetText("deviceToken", token).SetText("account", Login).SetText("publicEndpoint", "203.0.113.5:22022").
                                   SetNumber("keepaliveSeconds", KeepaliveSeconds).SetTextList("features", Features).SetText("serverVersion", "26.10.7.1200"))
                c.Authenticated()
                Await ControlLoopAsync(c)
            Case "hello"
                Dim reason As String = Nothing
                If _helloRefusals.TryDequeue(reason) Then
                    Await c.Refuse(reason)
                    Return
                End If
                Dim deviceId As String = first.Text("deviceId")
                Dim stored As String = TokenOf(deviceId)
                If stored.Length = 0 OrElse stored <> first.Text("deviceToken") Then
                    Await c.Refuse(If(IsRevoked(deviceId), "device-revoked", "bad-credentials"))
                    Return
                End If
                SyncLock _gate
                    _deviceByConnection(c.Index) = deviceId
                End SyncLock
                Await c.WriteAsync(c.Welcome(KeepaliveSeconds, Features))
                c.Authenticated()
                Await ControlLoopAsync(c)
            Case "register"
                ' ticket 224 rules 4-6: a wrong code and an already used code answer the SAME (no oracle); a full server
                ' says `capacity`; the answer is `enrolled` - the account and its first device in one exchange
                Dim deviceId As String = first.Text("deviceId")
                SyncLock _gate
                    _registerPasswords.Add(first.Text("password"))
                    _registerCodes.Add(first.Text("invitationCode"))
                    _enrollRoles.Add(String.Join(",", first.TextList("roles")))
                    _enrollCapabilities.Add(String.Join(",", first.TextList("capabilities")))
                    _enrollNames.Add(first.Text("deviceName"))
                    _enrollDeviceIds.Add(first.Text("deviceId"))
                End SyncLock
                Dim registerRefusal As String = Nothing
                If _registerRefusals.TryDequeue(registerRefusal) Then
                    Await c.Refuse(registerRefusal)
                    Return
                End If
                Dim accounts As Integer
                SyncLock _gate
                    accounts = 1 + ExtraLogins.Count + _registeredLogins.Count
                End SyncLock
                Dim limit As Integer = If(MaxAccounts <= 0, 10, MaxAccounts)
                If accounts >= limit Then
                    Await c.Refuse("capacity")
                    Return
                End If
                Dim invitation As String = first.Text("invitationCode")
                If invitation.Length = 0 Then invitation = first.Text("sharedPassword")
                Dim codeOk As Boolean
                SyncLock _gate
                    codeOk = (Not _usedInvitation AndAlso String.Equals(invitation, Invitation, StringComparison.Ordinal)) OrElse
                             (first.Text("invitationCode").Length = 0 AndAlso invitation.Length > 0 AndAlso invitation = SharedInvitationPassword)
                End SyncLock
                If Not codeOk OrElse ExchangePasswordPolicy.Check(first.Text("password")) <> ExchangePasswordPolicy.Verdict.Accepted Then
                    Await c.Refuse("bad-credentials")
                    Return
                End If
                SyncLock _gate
                    _usedInvitation = True
                    _registeredLogins.Add(first.Text("login"))
                End SyncLock
                Dim token As String = NewToken()
                SyncLock _gate
                    _tokens(deviceId) = token
                    _revoked.Remove(deviceId)
                End SyncLock
                SyncLock _gate
                    _deviceByConnection(c.Index) = deviceId
                End SyncLock
                Await c.WriteAsync(New ExchangeEnvelope("enrolled").SetText("deviceToken", token).SetText("account", first.Text("login")).SetText("publicEndpoint", "203.0.113.5:22022").
                                   SetNumber("keepaliveSeconds", KeepaliveSeconds).SetTextList("features", Features).SetText("serverVersion", "26.10.7.1200"))
                c.Authenticated()
                Await ControlLoopAsync(c)
            Case Else
                ' a data stream or anything else: the agent tests do not open one
                Await Task.Delay(100)
        End Select
    End Function

    Private Function IsRevoked(deviceId As String) As Boolean
        SyncLock _gate
            Return _revoked.Contains(deviceId)
        End SyncLock
    End Function

    Private Shared Function NewToken() As String
        Dim b(31) As Byte
        System.Security.Cryptography.RandomNumberGenerator.Fill(b)
        Return Convert.ToBase64String(b).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

    ' --- ticket 170: publish, unpublish, open, attach ---

    ''' <summary>One tunnel's attach stream, as the stub's consumer side sees it: the raw TLS stream that carries the producer's bytes. The
    ''' handler of the connection stays alive until <see cref="Release"/> (returning would dispose the stream).</summary>
    Public NotInheritable Class Attached
        Public ReadOnly Stream As System.IO.Stream
        Friend ReadOnly Done As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)

        Public Sub New(stream As System.IO.Stream)
            Me.Stream = stream
        End Sub

        Public Sub Release()
            Done.TrySetResult(True)
        End Sub
    End Class

    Private ReadOnly _publishLog As New List(Of String)               ' every publish, as the raw resource JSON, in arrival order
    Private ReadOnly _published As New Dictionary(Of String, String)(StringComparer.Ordinal)   ' resourceId -> latest raw JSON
    Private ReadOnly _unpublished As New List(Of String)
    Private ReadOnly _publishRefusals As New ConcurrentQueue(Of String)
    Private ReadOnly _tunnels As New Dictionary(Of String, TaskCompletionSource(Of Attached))(StringComparer.Ordinal)
    Private _tunnelSeq As Integer

    ''' <summary>The third-party port a request is granted (a request of `publicPort`: 0 or N).</summary>
    Public Property GrantedPort As Integer = 40007

    ''' <summary>The next `publish` is refused with this reason (queued: one per publish).</summary>
    Public Sub RefuseNextPublish(reason As String)
        _publishRefusals.Enqueue(reason)
    End Sub

    ''' <summary>Every record published, raw, in arrival order.</summary>
    Public Function PublishLog() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_publishLog)
        End SyncLock
    End Function

    ''' <summary>The latest record of each resource that is currently published.</summary>
    Public Function PublishedNow() As Dictionary(Of String, String)
        SyncLock _gate
            Return New Dictionary(Of String, String)(_published, StringComparer.Ordinal)
        End SyncLock
    End Function

    ''' <summary>The resource ids unpublished, in arrival order.</summary>
    Public Function Unpublished() As List(Of String)
        SyncLock _gate
            Return New List(Of String)(_unpublished)
        End SyncLock
    End Function

    ''' <summary>Acts as a consumer asking for a tunnel: pushes `open` to the producer's control stream and waits for its `attach`. Nothing when
    ''' the producer never attaches within <paramref name="seconds"/> (it answered by not attaching).</summary>
    Public Async Function OpenTunnelAsync(Optional resourceId As String = "", Optional shareId As String = "", Optional seconds As Double = 5) As Task(Of Attached)
        Dim tunnelId As String
        Dim tcs As New TaskCompletionSource(Of Attached)(TaskCreationOptions.RunContinuationsAsynchronously)
        SyncLock _gate
            _tunnelSeq += 1
            Dim b(15) As Byte
            b(0) = CByte(_tunnelSeq And &HFF)
            b(1) = 1
            tunnelId = Convert.ToBase64String(b).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
            _tunnels(tunnelId) = tcs
        End SyncLock
        Dim env As New ExchangeEnvelope("open")
        env.SetText("tunnelId", tunnelId)
        If resourceId.Length > 0 Then env.SetText("resourceId", resourceId)
        If shareId.Length > 0 Then env.SetText("shareId", shareId)
        Await PushAsync(env)
        Dim done As Task = Await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(seconds)))
        If done Is tcs.Task Then Return Await tcs.Task
        SyncLock _gate
            _tunnels.Remove(tunnelId)
        End SyncLock
        Return Nothing
    End Function

    ''' <summary>A data stream that attached and was then closed by the producer within <paramref name="seconds"/> (the answer of "no share, no
    ''' worker": the stream ends without a byte). False when it stayed open.</summary>
    Public Shared Async Function EndedWithoutDataAsync(a As Attached, Optional seconds As Double = 5) As Task(Of Boolean)
        Dim buffer(0) As Byte
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(seconds))
            Try
                Dim n As Integer = Await a.Stream.ReadAsync(buffer, 0, 1, cts.Token)
                Return n = 0
            Catch ex As OperationCanceledException
                Return False
            Catch ex As System.IO.IOException
                Return True
            End Try
        End Using
    End Function

    Private Async Function ControlLoopAsync(c As ExchangeStubConnection) As Task
        ' the first envelope after `welcome` fixes what the stream is (7.2): `attach` makes it a data stream, anything else the control stream
        Dim first As ExchangeEnvelope = Await c.ReadAsync()
        If first.Type = "attach" Then
            Note(c.Index, first.Type)
            Dim tcs As TaskCompletionSource(Of Attached) = Nothing
            SyncLock _gate
                If Not _tunnels.TryGetValue(first.Text("tunnelId"), tcs) Then tcs = Nothing
                _tunnels.Remove(first.Text("tunnelId"))
            End SyncLock
            If tcs Is Nothing Then Return
            Dim a As New Attached(c.Stream)
            tcs.TrySetResult(a)
            Await a.Done.Task
            Return
        End If
        SyncLock _gate
            _live(c.Index) = c
        End SyncLock
        Dim pending As ExchangeEnvelope = first
        Try
            While Not c.Token.IsCancellationRequested
                Dim env As ExchangeEnvelope
                If pending IsNot Nothing Then
                    env = pending
                    pending = Nothing
                Else
                    env = Await c.ReadAsync()
                End If
                Note(c.Index, env.Type)
                Select Case env.Type
                    Case "publish"
                        Dim raw As String = env.Record("resource")
                        Dim reason As String = Nothing
                        If _publishRefusals.TryDequeue(reason) Then
                            Await c.Refuse(reason)
                            Continue While
                        End If
                        Dim id As String = ""
                        Dim requested As Integer = -1
                        Using doc As JsonDocument = JsonDocument.Parse(raw)
                            Dim el As JsonElement = Nothing
                            If doc.RootElement.TryGetProperty("resourceId", el) Then id = el.GetString()
                            If doc.RootElement.TryGetProperty("publicPort", el) AndAlso el.ValueKind = JsonValueKind.Number Then requested = el.GetInt32()
                        End Using
                        SyncLock _gate
                            _publishLog.Add(raw)
                            _published(id) = raw
                        End SyncLock
                        Dim answer As New ExchangeEnvelope("published")
                        answer.SetText("resourceId", id)
                        If requested >= 0 Then answer.SetNumber("publicPort", If(requested > 0, requested, GrantedPort))
                        Await c.WriteAsync(answer)
                    Case "unpublish"
                        SyncLock _gate
                            _unpublished.Add(env.Text("resourceId"))
                            _published.Remove(env.Text("resourceId"))
                        End SyncLock
                    Case "keepalive"
                        If EchoKeepalive Then Await c.WriteAsync(New ExchangeEnvelope("keepalive"))
                    Case "list"
                        Await c.WriteAsync(DirectoryEnvelope())
                    Case "pairing-request"
                        If PairingRefusal.Length > 0 Then
                            Await c.Refuse(PairingRefusal)
                        Else
                            Await c.WriteAsync(New ExchangeEnvelope("pairing-code").SetText("code", PairingCode).SetText("expiresAt", "2099-01-01T00:10:00Z"))
                        End If
                    Case "revoke"
                        SyncLock _gate
                            _revoked.Add(env.Text("deviceId"))
                            _tokens.Remove(env.Text("deviceId"))
                        End SyncLock
                    Case "password-change"
                        ' ticket 224 rule 9: the old password decides; on success THIS device gets a fresh token and every
                        ' OTHER device's token dies (they sign in again); a wrong old password is the common refusal
                        Dim oldOk As Boolean
                        SyncLock _gate
                            oldOk = String.Equals(env.Text("oldPassword"), _password, StringComparison.Ordinal)
                        End SyncLock
                        If Not oldOk Then
                            Await c.Refuse("bad-credentials")
                            Continue While
                        End If
                        Dim fresh As String = NewToken()
                        Dim thisDevice As String = DeviceOfConnection(c.Index)
                        SyncLock _gate
                            _password = env.Text("newPassword")
                            _tokens.Clear()
                            If thisDevice.Length > 0 Then _tokens(thisDevice) = fresh
                        End SyncLock
                        Await c.WriteAsync(New ExchangeEnvelope("password-changed").SetText("deviceToken", fresh))
                    Case "bye"
                        Return
                End Select
            End While
        Finally
            SyncLock _gate
                _live.Remove(c.Index)
                _deviceByConnection.Remove(c.Index)
            End SyncLock
        End Try
    End Function

    ''' <summary>Waits until <paramref name="condition"/> is true or the time runs out; returns the last value.</summary>
    Public Shared Async Function WaitUntilAsync(condition As Func(Of Boolean), Optional seconds As Double = 10) As Task(Of Boolean)
        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(seconds)
        While DateTime.UtcNow < deadline
            If condition() Then Return True
            Await Task.Delay(20)
        End While
        Return condition()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Server.Dispose()
    End Sub

End Class
