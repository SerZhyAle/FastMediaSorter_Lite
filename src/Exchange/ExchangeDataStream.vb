#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 180 T-1. Opening an authenticated stream (`hello` or `enroll`) and the three data streams of
' DEVICE-EXCHANGE 7.5 and 7.6 on top of it: `connect` (a consumer's tunnel), `attach` (a producer's side of a
' tunnel) and `upload` (a broadcaster's chunk stream).
'
' Every stream here is TLS (ExchangeTlsTransport; nothing in this file knows a plain socket). After the last
' envelope - `connected`, or `upload-accepted` - the stream is the service's or the relay's: the frame reader
' consumed exactly the bytes of that envelope, so the next read on <see cref="ExchangeOpenResult.Stream"/> returns
' the tunnelled service's first byte.

''' <summary>The credentials of an enrolled device. Never printed.</summary>
Friend NotInheritable Class ExchangeDeviceCredentials

    Public ReadOnly DeviceId As String
    Public ReadOnly Token As String

    Public Sub New(deviceId As String, token As String)
        Me.DeviceId = deviceId
        Me.Token = token
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeDeviceCredentials"
    End Function

End Class

''' <summary>What opening a stream came to.</summary>
Friend Enum ExchangeOpenOutcome
    ''' <summary>Authenticated, or (for a data stream) the last envelope was read and the stream is the service's.</summary>
    Connected = 0
    ''' <summary>The server answered `refused`; <see cref="ExchangeOpenResult.Refusal"/> says why.</summary>
    Refused = 1
    ''' <summary>A pin exists and the certificate differs: a named stop, never re-pinned.</summary>
    PinMismatch = 2
    ''' <summary>No pin and the platform does not accept the certificate: the user has not accepted its fingerprint.</summary>
    Untrusted = 3
    ''' <summary>The handshake failed for another reason (a peer that does not speak TLS).</summary>
    TlsFailed = 4
    ''' <summary>No TCP connection.</summary>
    Network = 5
    Cancelled = 6
    TimedOut = 7
    ''' <summary>The peer answered something the opening does not allow, or a malformed frame.</summary>
    Protocol = 8
    ''' <summary>The peer closed before answering.</summary>
    Closed = 9
End Enum

''' <summary>An opened stream with its framing, or the reason there is none. Dispose closes the stream.</summary>
Friend NotInheritable Class ExchangeOpenResult
    Implements IDisposable

    Public ReadOnly Outcome As ExchangeOpenOutcome
    ''' <summary>The TLS stream. After a data opening it carries the service's bytes; after <c>hello</c> it is a framed stream.</summary>
    Public ReadOnly Stream As Stream
    Public ReadOnly Reader As ExchangeFrameReader
    Public ReadOnly Writer As ExchangeFrameWriter
    ''' <summary>`welcome` or `enrolled`, when the stream authenticated.</summary>
    Public ReadOnly Welcome As ExchangeEnvelope
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>The certificate fingerprint seen, when the handshake got that far.</summary>
    Public ReadOnly Fingerprint As String
    Public ReadOnly KeyPin As String

    Public Sub New(outcome As ExchangeOpenOutcome, stream As Stream, reader As ExchangeFrameReader, writer As ExchangeFrameWriter,
                   welcome As ExchangeEnvelope, refusal As ExchangeRefusal, fingerprint As String, Optional keyPin As String = "")
        Me.Outcome = outcome
        Me.Stream = stream
        Me.Reader = reader
        Me.Writer = writer
        Me.Welcome = welcome
        Me.Refusal = refusal
        Me.Fingerprint = If(fingerprint, "")
        Me.KeyPin = If(keyPin, "")
    End Sub

    Public Shared Function Failed(outcome As ExchangeOpenOutcome, Optional refusal As ExchangeRefusal = ExchangeRefusal.Unknown, Optional fingerprint As String = "") As ExchangeOpenResult
        Return New ExchangeOpenResult(outcome, Nothing, Nothing, Nothing, Nothing, refusal, fingerprint)
    End Function

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return Outcome = ExchangeOpenOutcome.Connected
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Stream?.Dispose()
        Catch
        End Try
    End Sub

    ''' <summary>Never a member of the exchange.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeOpenResult " & Outcome.ToString()
    End Function

End Class

''' <summary>The opening of one data stream, so a forwarder can be tested without a server.</summary>
Friend Interface IExchangeDataOpener
    Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeOpenResult)
End Interface

Friend NotInheritable Class ExchangeSessions

    Private Sub New()
    End Sub

    ''' <summary>TLS, then `hello`, then `welcome`. The result's stream is authenticated and framed under the larger limit.</summary>
    Public Shared Function HelloAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                      creds As ExchangeDeviceCredentials, productVersion As String, keepaliveSeconds As Integer,
                                      ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim hello As ExchangeEnvelope = ExchangeMessages.Hello(creds.DeviceId, creds.Token, productVersion, keepaliveSeconds)
        Return OpenAuthenticatedAsync(transport, server, pin, hello, ExchangeEnvelopeTypes.Welcome, ct)
    End Function

    ''' <summary>TLS, then the given `enroll`, then `enrolled`. The frame (a password in it) is overwritten once sent.</summary>
    Public Shared Function EnrollAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                       enroll As ExchangeEnvelope, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Return OpenAuthenticatedAsync(transport, server, pin, enroll, ExchangeEnvelopeTypes.Enrolled, ct)
    End Function

    Private Shared Async Function OpenAuthenticatedAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                         first As ExchangeEnvelope, expected As String, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim conn As ExchangeTlsConnection
        Try
            conn = Await transport.ConnectAsync(server.Host, server.Port, pin, ct).ConfigureAwait(False)
        Catch ex As ExchangeConnectException
            Return FromConnect(ex)
        End Try

        Dim reader As New ExchangeFrameReader(conn.Stream)
        Dim writer As New ExchangeFrameWriter(conn.Stream)
        Dim ok As Boolean = False
        Try
            Await writer.WriteAsync(first, ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ReadKnownAsync(reader, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then
                Return New ExchangeOpenResult(ExchangeOpenOutcome.Refused, Nothing, Nothing, Nothing, Nothing, reply.Refusal(), conn.Fingerprint)
            End If
            If Not reply.Is(expected) Then Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Protocol, fingerprint:=conn.Fingerprint)
            reader.MarkAuthenticated()
            writer.MarkAuthenticated()
            ok = True
            Return New ExchangeOpenResult(ExchangeOpenOutcome.Connected, conn.Stream, reader, writer, reply, ExchangeRefusal.Unknown, conn.Fingerprint, conn.KeyPin)
        Catch ex As OperationCanceledException
            Return ExchangeOpenResult.Failed(If(ct.IsCancellationRequested, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.TimedOut), fingerprint:=conn.Fingerprint)
        Catch ex As EndOfStreamException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Closed, fingerprint:=conn.Fingerprint)
        Catch ex As ExchangeWireException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Protocol, fingerprint:=conn.Fingerprint)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Closed, fingerprint:=conn.Fingerprint)
        Finally
            If Not ok Then conn.Dispose()
        End Try
    End Function

    ''' <summary>The next envelope of a known type; an unknown one is ignored (7.1) up to a bounded number of times.</summary>
    Public Shared Async Function ReadKnownAsync(reader As ExchangeFrameReader, ct As CancellationToken) As Task(Of ExchangeEnvelope)
        For i As Integer = 0 To 15
            Dim env As ExchangeEnvelope = Await reader.ReadAsync(ct).ConfigureAwait(False)
            If env.IsKnown Then Return env
        Next
        Throw New ExchangeWireException(ExchangeWireError.Malformed, "too many unknown envelopes in a row")
    End Function

    Friend Shared Function FromConnect(ex As ExchangeConnectException) As ExchangeOpenResult
        Dim outcome As ExchangeOpenOutcome
        Select Case ex.Failure
            Case ExchangeConnectFailure.PinMismatch : outcome = ExchangeOpenOutcome.PinMismatch
            Case ExchangeConnectFailure.Untrusted : outcome = ExchangeOpenOutcome.Untrusted
            Case ExchangeConnectFailure.TlsFailed : outcome = ExchangeOpenOutcome.TlsFailed
            Case ExchangeConnectFailure.Cancelled : outcome = ExchangeOpenOutcome.Cancelled
            Case ExchangeConnectFailure.TimedOut : outcome = ExchangeOpenOutcome.TimedOut
            Case Else : outcome = ExchangeOpenOutcome.Network
        End Select
        Return ExchangeOpenResult.Failed(outcome, fingerprint:=ex.Fingerprint)
    End Function

End Class

''' <summary>The wire names of the envelope types the library itself branches on.</summary>
Friend NotInheritable Class ExchangeEnvelopeTypes

    Private Sub New()
    End Sub

    Public Const Enroll As String = "enroll"
    Public Const Enrolled As String = "enrolled"
    Public Const Hello As String = "hello"
    Public Const Welcome As String = "welcome"
    Public Const Refused As String = "refused"
    Public Const Keepalive As String = "keepalive"
    Public Const Bye As String = "bye"
    Public Const List As String = "list"
    Public Const Directory As String = "directory"
    Public Const Subscribe As String = "subscribe"
    Public Const Changed As String = "changed"
    Public Const Publish As String = "publish"
    Public Const Published As String = "published"
    Public Const Unpublish As String = "unpublish"
    Public Const PairingRequest As String = "pairing-request"
    Public Const PairingCode As String = "pairing-code"
    Public Const Revoke As String = "revoke"
    Public Const Connect As String = "connect"
    Public Const Connected As String = "connected"
    Public Const Open As String = "open"
    Public Const Attach As String = "attach"
    Public Const Upload As String = "upload"
    Public Const UploadAccepted As String = "upload-accepted"
    Public Const Cast As String = "cast"
    Public Const CastOffer As String = "cast-offer"
    Public Const CastAnswer As String = "cast-answer"
    Public Const CastResult As String = "cast-result"
    Public Const CastStop As String = "cast-stop"
    Public Const BroadcastStart As String = "broadcast-start"
    Public Const BroadcastStarted As String = "broadcast-started"
    Public Const BroadcastUpdate As String = "broadcast-update"
    Public Const BroadcastEnd As String = "broadcast-end"
    Public Const Register As String = "register"
    Public Const PasswordChange As String = "password-change"
    Public Const PasswordChanged As String = "password-changed"
    Public Const AccountDelete As String = "account-delete"
    Public Const AccountDeleted As String = "account-deleted"
    Public Const ClipboardPut As String = "clipboard-put"
    Public Const ClipboardReady As String = "clipboard-ready"
    Public Const ClipboardStored As String = "clipboard-stored"
    Public Const ClipboardInfo As String = "clipboard-info"
    Public Const ClipboardState As String = "clipboard-state"
    Public Const ClipboardGet As String = "clipboard-get"
    Public Const ClipboardValue As String = "clipboard-value"
    Public Const ClipboardEmpty As String = "clipboard-empty"
    ' spec 234 / DEVICE-EXCHANGE section 20 items BD-BE and section 27 item DJ: a member account gives a resource or a broadcast to a guest
    Public Const Grant As String = "grant"
    Public Const Granted As String = "granted"
    Public Const Ungrant As String = "ungrant"

End Class

''' <summary>Builders for the envelopes a client sends. The wire's own member names stay in the envelope table; a
''' builder only fills the values.</summary>
Friend NotInheritable Class ExchangeMessages

    Private Sub New()
    End Sub

    Public Shared Function Hello(deviceId As String, token As String, productVersion As String, keepaliveSeconds As Integer) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Hello).
            SetText("deviceId", deviceId).SetText("deviceToken", token).
            SetText("productVersion", productVersion).SetNumber("keepaliveSeconds", keepaliveSeconds)
    End Function

    ''' <summary>An `enroll` with a password OR a pairing code (never both). The receiver block, when there is one, is raw JSON.</summary>
    Public Shared Function Enroll(login As String, password As String, pairingCode As String, description As ExchangeDeviceDescription,
                                  keepaliveSeconds As Integer) As ExchangeEnvelope
        Dim env As ExchangeEnvelope = New ExchangeEnvelope(ExchangeEnvelopeTypes.Enroll).
            SetText("login", login).SetText("password", password).SetText("pairingCode", pairingCode).
            SetText("deviceId", description.DeviceId).SetText("deviceName", description.DeviceName).
            SetText("product", description.Product).SetText("productVersion", description.ProductVersion).
            SetText("platform", description.Platform).SetTextList("roles", description.Roles).
            SetNumber("keepaliveSeconds", keepaliveSeconds)
        If description.ReceiverJson.Length > 0 Then env.SetRecord("receiver", description.ReceiverJson)
        DeclareCapabilities(env, description)
        Return env
    End Function

    ''' <summary>`capabilities` (item AG) and `e2eKey` (item DF) of an `enroll` or `register`: `pin-v1` for a resource consumer,
    ''' and, for a device that has a key, `e2e-v1` with `e2eKey {v, kem}`. A device without the key sends neither of the two.</summary>
    Private Shared Sub DeclareCapabilities(env As ExchangeEnvelope, description As ExchangeDeviceDescription)
        Dim capabilities As New List(Of String)
        If description.Roles.Contains(ExchangeWire.RoleResourceConsumer) Then capabilities.Add("pin-v1")
        Dim point As Byte() = Nothing
        If ExchangeDeviceKey.TryParseKem(description.E2eKem, point) Then
            capabilities.Add(ExchangeSealedNames.CapabilityE2e)
            env.SetRecord("e2eKey", "{""v"":" & ExchangeDeviceKey.E2eKeyVersion.ToString(Globalization.CultureInfo.InvariantCulture) &
                                    ",""kem"":""" & description.E2eKem & """}")
        End If
        If capabilities.Count > 0 Then env.SetTextList("capabilities", capabilities)
    End Sub

    Public Shared Function Simple(typeName As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(typeName)
    End Function

    ''' <summary>A `register` (ticket 224 rule 6, the client half 226): the one-time invitation CODE or the shared invitation
    ''' password (exactly one of them), the chosen login and password, and the first device's members - the account and the
    ''' device in one exchange. The answer is `enrolled`, exactly as a sign-in's. The code and the password are secret
    ''' holders: they live in the caller's field and in this one frame.</summary>
    Public Shared Function Register(invitationCode As String, sharedPassword As String, login As String, password As String,
                                    description As ExchangeDeviceDescription, keepaliveSeconds As Integer) As ExchangeEnvelope
        Dim env As ExchangeEnvelope = New ExchangeEnvelope(ExchangeEnvelopeTypes.Register).
            SetText("invitationCode", invitationCode).SetText("sharedPassword", sharedPassword).
            SetText("login", login).SetText("password", password).
            SetText("deviceId", description.DeviceId).SetText("deviceName", description.DeviceName).
            SetText("product", description.Product).SetText("productVersion", description.ProductVersion).
            SetText("platform", description.Platform).SetTextList("roles", description.Roles).
            SetNumber("keepaliveSeconds", keepaliveSeconds)
        If description.ReceiverJson.Length > 0 Then env.SetRecord("receiver", description.ReceiverJson)
        DeclareCapabilities(env, description)
        Return env
    End Function

    ''' <summary>A `password-change` (ticket 224 rule 9): the old password and the new one, on the control stream. The answer is
    ''' `password-changed` with this device's fresh token, or one refusal; every OTHER device of the account is signed out.</summary>
    Public Shared Function PasswordChange(oldPassword As String, newPassword As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.PasswordChange).SetText("oldPassword", oldPassword).SetText("newPassword", newPassword)
    End Function

    Public Shared Function Publish(resourceJson As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Publish).SetRecord("resource", resourceJson)
    End Function

    Public Shared Function Unpublish(resourceId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Unpublish).SetText("resourceId", resourceId)
    End Function

    Public Shared Function Revoke(deviceId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Revoke).SetText("deviceId", deviceId)
    End Function

    Public Shared Function ConnectResource(resourceId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Connect).SetText("resourceId", resourceId)
    End Function

    Public Shared Function ConnectShare(shareId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Connect).SetText("shareId", shareId)
    End Function

    ''' <summary>`scheme` is "http" or "rtsp" (7.5).</summary>
    Public Shared Function ConnectBroadcast(broadcastId As String, scheme As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Connect).SetText("broadcastId", broadcastId).SetText("scheme", scheme)
    End Function

    Public Shared Function Attach(tunnelId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Attach).SetText("tunnelId", tunnelId)
    End Function

    Public Shared Function Upload(broadcastId As String, contentType As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Upload).SetText("broadcastId", broadcastId).SetText("contentType", contentType)
    End Function

    ''' <summary>A `clipboard-put` (item BW): `kind`, `size` of at least 1, and `mimeType` / `name` only where the kind has them.</summary>
    Public Shared Function ClipboardPut(kind As String, size As Long, mimeType As String, name As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.ClipboardPut).SetText("kind", kind).SetNumber("size", size).
            SetText("mimeType", mimeType).SetText("name", name)
    End Function

    ''' <summary>A sealed `clipboard-put` (27 item DN): `kind` `sealed`, `size` the number of SEALED bytes that follow `clipboard-ready`, and
    ''' `sealed` the box with the context `clipboard` as one JSON object. A sealed put carries no `mimeType` and no `name` in clear.</summary>
    Public Shared Function ClipboardPutSealed(sealedSize As Long, boxJson As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.ClipboardPut).SetText("kind", ExchangeClipboardKinds.Sealed).SetNumber("size", sealedSize).
            SetRecord("sealed", boxJson)
    End Function

    ''' <summary>A `grant` to a guest account (20 items BD/BE, 27 item DJ): the resource OR the broadcast, and exactly one payload
    ''' member as raw JSON - `access` (a resource) or `credentials` (a broadcast) in the clear for a guest device without the
    ''' capability, or `sealed` (a box with the context `grant:&lt;grantId&gt;`) for one that has it. A `grant` with a `grantId` the
    ''' account already holds replaces that grant's box. The payload is a secret holder: it lives in the caller's field and in this frame.</summary>
    Public Shared Function GrantTo(grantId As String, guest As String, resourceId As String, broadcastId As String,
                                   accessJson As String, credentialsJson As String, sealedJson As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Grant).SetText("grantId", grantId).SetText("guest", guest).
            SetText("resourceId", resourceId).SetText("broadcastId", broadcastId).
            SetRecord("access", accessJson).SetRecord("credentials", credentialsJson).SetRecord("sealed", sealedJson)
    End Function

    ''' <summary>An `ungrant` (20 item BD): takes a grant back; not answered.</summary>
    Public Shared Function Ungrant(grantId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.Ungrant).SetText("grantId", grantId)
    End Function

    ''' <summary>A `clipboard-get` (item BY): the `clipId` of the value the person was told about, or "" for the current one.</summary>
    Public Shared Function ClipboardGet(clipId As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.ClipboardGet).SetText("clipId", clipId)
    End Function

    ''' <summary>A `clipboard-info` (item BY): no members; the answer is `clipboard-state`.</summary>
    Public Shared Function ClipboardInfo() As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.ClipboardInfo)
    End Function

    Public Shared Function CastAnswer(broadcastId As String, accepted As Boolean, reason As String) As ExchangeEnvelope
        Return New ExchangeEnvelope(ExchangeEnvelopeTypes.CastAnswer).SetText("broadcastId", broadcastId).SetFlag("accepted", accepted).SetText("reason", reason)
    End Function

End Class

''' <summary>The three data streams. Each returns a result whose stream, when Connected, is the service's (or the relay's).</summary>
Friend NotInheritable Class ExchangeDataStreams

    Private Sub New()
    End Sub

    ''' <summary>A consumer's tunnel to a resource of its own account: `hello`, `connect {resourceId}`, `connected` (7.5).</summary>
    Public Shared Async Function ConnectResourceAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                      creds As ExchangeDeviceCredentials, productVersion As String, resourceId As String,
                                                      ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return session
        Return Await FinishConnectAsync(session, ExchangeMessages.ConnectResource(resourceId), ct).ConfigureAwait(False)
    End Function

    ''' <summary>An outsider's tunnel by share id: TLS, no `hello`, `connect {shareId}`, `connected` (7.5, item V).</summary>
    Public Shared Async Function ConnectShareAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                   shareId As String, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Return Await OutsiderAsync(transport, server, pin, ExchangeMessages.ConnectShare(shareId), ct).ConfigureAwait(False)
    End Function

    ''' <summary>An outsider's tunnel to a live broadcast: TLS, no `hello`, `connect {broadcastId, scheme}`, `connected`.</summary>
    Public Shared Async Function ConnectBroadcastAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                       broadcastId As String, scheme As String, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Return Await OutsiderAsync(transport, server, pin, ExchangeMessages.ConnectBroadcast(broadcastId, scheme), ct).ConfigureAwait(False)
    End Function

    Private Shared Async Function OutsiderAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                connect As ExchangeEnvelope, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim conn As ExchangeTlsConnection
        Try
            conn = Await transport.ConnectAsync(server.Host, server.Port, pin, ct).ConfigureAwait(False)
        Catch ex As ExchangeConnectException
            Return ExchangeSessions.FromConnect(ex)
        End Try
        ' Never authenticated: the pre-authentication limit stays in force for the whole stream.
        Dim session As New ExchangeOpenResult(ExchangeOpenOutcome.Connected, conn.Stream, New ExchangeFrameReader(conn.Stream),
                                              New ExchangeFrameWriter(conn.Stream), Nothing, ExchangeRefusal.Unknown, conn.Fingerprint)
        Return Await FinishConnectAsync(session, connect, ct).ConfigureAwait(False)
    End Function

    Private Shared Async Function FinishConnectAsync(session As ExchangeOpenResult, connect As ExchangeEnvelope, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim ok As Boolean = False
        Try
            Await session.Writer.WriteAsync(connect, ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ExchangeSessions.ReadKnownAsync(session.Reader, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then
                Return New ExchangeOpenResult(ExchangeOpenOutcome.Refused, Nothing, Nothing, Nothing, Nothing, reply.Refusal(), session.Fingerprint)
            End If
            If Not reply.Is(ExchangeEnvelopeTypes.Connected) Then Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Protocol, fingerprint:=session.Fingerprint)
            ok = True
            Return session
        Catch ex As OperationCanceledException
            Return ExchangeOpenResult.Failed(If(ct.IsCancellationRequested, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.TimedOut), fingerprint:=session.Fingerprint)
        Catch ex As EndOfStreamException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Closed, fingerprint:=session.Fingerprint)
        Catch ex As ExchangeWireException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Protocol, fingerprint:=session.Fingerprint)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Closed, fingerprint:=session.Fingerprint)
        Finally
            If Not ok Then session.Dispose()
        End Try
    End Function

    ''' <summary>A producer's side of a tunnel: `hello`, `attach {tunnelId}`. The stream is then bridged to the local service.</summary>
    Public Shared Async Function AttachAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                             creds As ExchangeDeviceCredentials, productVersion As String, tunnelId As String,
                                             ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return session
        Try
            Await session.Writer.WriteAsync(ExchangeMessages.Attach(tunnelId), ct).ConfigureAwait(False)
            Return session
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ExchangeWireException
            session.Dispose()
            Return ExchangeOpenResult.Failed(If(TypeOf ex Is OperationCanceledException, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.Closed), fingerprint:=session.Fingerprint)
        End Try
    End Function

    ''' <summary>A broadcaster's relay upload: `hello`, `upload`, `upload-accepted`; chunks follow (<see cref="ExchangeChunks"/>).</summary>
    Public Shared Async Function UploadAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                             creds As ExchangeDeviceCredentials, productVersion As String, broadcastId As String,
                                             contentType As String, ct As CancellationToken) As Task(Of ExchangeOpenResult)
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return session
        Dim ok As Boolean = False
        Try
            Await session.Writer.WriteAsync(ExchangeMessages.Upload(broadcastId, contentType), ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ExchangeSessions.ReadKnownAsync(session.Reader, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then
                Return New ExchangeOpenResult(ExchangeOpenOutcome.Refused, Nothing, Nothing, Nothing, Nothing, reply.Refusal(), session.Fingerprint)
            End If
            If Not reply.Is(ExchangeEnvelopeTypes.UploadAccepted) Then Return ExchangeOpenResult.Failed(ExchangeOpenOutcome.Protocol, fingerprint:=session.Fingerprint)
            ok = True
            Return session
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ExchangeWireException
            Return ExchangeOpenResult.Failed(If(TypeOf ex Is OperationCanceledException, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.Closed), fingerprint:=session.Fingerprint)
        Finally
            If Not ok Then session.Dispose()
        End Try
    End Function

End Class

''' <summary>An opener that makes one `connect` per call, over the account's server and pin. What the forwarder uses.</summary>
Friend NotInheritable Class ExchangeConnectOpener
    Implements IExchangeDataOpener

    Public Enum Target
        Resource
        Share
        Broadcast
    End Enum

    Private ReadOnly _transport As IExchangeTransport
    Private ReadOnly _server As ExchangeServerAddress
    Private ReadOnly _pin As String
    Private ReadOnly _creds As ExchangeDeviceCredentials
    Private ReadOnly _productVersion As String
    Private ReadOnly _kind As Target
    Private ReadOnly _id As String
    Private ReadOnly _scheme As String

    Public Sub New(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String, creds As ExchangeDeviceCredentials,
                   productVersion As String, kind As Target, id As String, scheme As String)
        _transport = transport
        _server = server
        _pin = pin
        _creds = creds
        _productVersion = productVersion
        _kind = kind
        _id = id
        _scheme = scheme
    End Sub

    Public Function OpenAsync(ct As CancellationToken) As Task(Of ExchangeOpenResult) Implements IExchangeDataOpener.OpenAsync
        Select Case _kind
            Case Target.Resource
                Return ExchangeDataStreams.ConnectResourceAsync(_transport, _server, _pin, _creds, _productVersion, _id, ct)
            Case Target.Share
                Return ExchangeDataStreams.ConnectShareAsync(_transport, _server, _pin, _id, ct)
            Case Else
                Return ExchangeDataStreams.ConnectBroadcastAsync(_transport, _server, _pin, _id, _scheme, ct)
        End Select
    End Function

End Class
#End If
