#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

' Ticket 187 T-3 and rules 3-5. The quick join of a CONSUMER: a connection string (or an address typed by hand), a login and a
' password become an account entry with a stored token - with nothing but this program's own connection to the server. No Share
' Manager, no agent, no pipe, no other process (rule 5). The order is the proposal's Ask 5:
'
'   1. the string is parsed (ExchangeConnectionString); a refusal ends there;
'   2. TLS to `x`; the trust rule of rule 3: `fp` present = the leaf must equal it, with no prompt, and a different leaf is refused
'      with both fingerprints and NOTHING pinned and no password sent; `fp` absent = item D (the platform first; a leaf it rejects
'      is pinned only after the user was shown its fingerprint and accepted it);
'   3. `enroll` with the login and the password. The password lives in the sign-in field and in this one frame and is then wiped;
'      it is never stored - not in the registry, not in a file, not in the DPAPI store (rule 4);
'   4. on `enrolled` the token goes to the entry's DPAPI store and the entry (server, login, pin, device id) to the registry;
'   5. the caller shows the directory and lets the user mark what to attach.
'
' The device id is the ENTRY's own, generated before `enroll` (a retry after `id-collision` makes one fresh id, once), so two logins on
' one server never meet item G. This device declares `resource-consumer` and nothing else: it is complete without a producer.

''' <summary>What this program needs from its surroundings, injectable so the whole join is tested against a stub server.</summary>
Friend NotInheritable Class ExchangeConsumerPorts

    Public Property Transport As IExchangeTransport = ExchangeTlsTransport.Instance
    Public Property Entries As ExchangeEntryStore = ExchangeEntryStore.CreateDefault()
    ''' <summary>The machine's name (the device name is "&lt;machine&gt; (viewer)", Q187-5).</summary>
    Public Property MachineName As Func(Of String) = Function() Environment.MachineName
    Public Property ProductVersion As String = ""
    ''' <summary>A line for the log: never a token, an `access`, a pairing code, a password, a server or a login.</summary>
    Public Property Log As Action(Of String) = Sub(line) Return
    ''' <summary>The suffix of the viewer's device name, in the user's language; the machine name comes before it.</summary>
    Public Property DeviceNameSuffix As String = "(viewer)"

End Class

Friend Enum ExchangeConsumerEnrollOutcome
    ''' <summary>Enrolled; the entry and its token are stored.</summary>
    Enrolled = 0
    ''' <summary>The server answered `refused`; <see cref="ExchangeConsumerEnrollResult.Refusal"/> says why.</summary>
    Refused = 1
    ''' <summary>A pin exists (the string's `fp`, or an earlier accept) and the certificate differs: both fingerprints are in the result,
    ''' nothing was pinned and no password was sent.</summary>
    PinMismatch = 2
    ''' <summary>The platform does not accept the certificate and no fingerprint was accepted yet.</summary>
    Untrusted = 3
    TlsFailed = 4
    Network = 5
    TimedOut = 6
    Cancelled = 7
    Protocol = 8
    ''' <summary>The address, the login or the credential is not usable (nothing was sent).</summary>
    BadInput = 9
    ''' <summary>Enrolled, but the token or the entry could not be stored: nothing is configured here.</summary>
    StoreFailed = 10
    ''' <summary>The entry limit is reached.</summary>
    TooMany = 11
End Enum

Friend NotInheritable Class ExchangeConsumerEnrollResult

    Public ReadOnly Outcome As ExchangeConsumerEnrollOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>The fingerprint the server showed ("" when the handshake did not get that far).</summary>
    Public ReadOnly Fingerprint As String
    ''' <summary>The stored entry on success.</summary>
    Public ReadOnly Entry As ExchangeEntry

    Public Sub New(outcome As ExchangeConsumerEnrollOutcome, refusal As ExchangeRefusal, fingerprint As String, entry As ExchangeEntry)
        Me.Outcome = outcome
        Me.Refusal = refusal
        Me.Fingerprint = If(fingerprint, "")
        Me.Entry = entry
    End Sub

End Class

''' <summary>What the user gave. The password is cleared as soon as the frame is built, whatever happens.</summary>
Friend NotInheritable Class ExchangeConsumerEnrollRequest

    ''' <summary>host:port.</summary>
    Public Property Server As String = ""
    Public Property Login As String = ""
    Public Property Password As String = ""
    ''' <summary>The fingerprint to trust without a prompt: the string's `fp`, or the one the user accepted after seeing it. "" = none.</summary>
    Public Property TrustedFingerprint As String = ""
    ''' <summary>The probe found the certificate validated by the platform: no pin is made.</summary>
    Public Property PlatformValid As Boolean
    ''' <summary>The server's display name (the string's `n`), a hint.</summary>
    Public Property ServerName As String = ""
    ''' <summary>"" = "&lt;machine&gt; (viewer)".</summary>
    Public Property DeviceName As String = ""

    Public Sub Wipe()
        Password = ""
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeConsumerEnrollRequest"
    End Function

End Class

''' <summary>What the user gave to REGISTER (226 rule 1): the invitation code of the string (or the shared invitation
''' password the operator handed out), the chosen login and the password typed twice by the person (the field's twin is
''' the window's business). The code and the password are cleared as soon as the frame is built, whatever happens - and
''' the code is never stored after use (rule 6).</summary>
Friend NotInheritable Class ExchangeConsumerRegisterRequest

    ''' <summary>host:port.</summary>
    Public Property Server As String = ""
    Public Property Login As String = ""
    Public Property Password As String = ""
    ''' <summary>The one-time code the invitation string carried; "" when the person typed the shared password instead.</summary>
    Public Property InvitationCode As String = ""
    ''' <summary>The shared invitation password the operator set; "" when a one-time code is used.</summary>
    Public Property SharedPassword As String = ""
    ''' <summary>The fingerprint to trust without a prompt: the string's `fp`, or the one the user accepted. "" = none.</summary>
    Public Property TrustedFingerprint As String = ""
    ''' <summary>The probe found the certificate validated by the platform: no pin is made.</summary>
    Public Property PlatformValid As Boolean
    ''' <summary>The server's display name (the string's `n`), a hint.</summary>
    Public Property ServerName As String = ""
    ''' <summary>"" = "&lt;machine&gt; (viewer)".</summary>
    Public Property DeviceName As String = ""

    ''' <summary>Overwrites the secrets.</summary>
    Public Sub Wipe()
        Password = ""
        InvitationCode = ""
        SharedPassword = ""
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeConsumerRegisterRequest"
    End Function

End Class

Friend NotInheritable Class ExchangeConsumer

    Private Sub New()
    End Sub

    ''' <summary>First contact: what certificate does this server show? No byte of envelope is sent.</summary>
    Public Shared Async Function ProbeAsync(server As String, ports As ExchangeConsumerPorts, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(server, address) Then Throw New ArgumentException("server")
        If TypeOf ports.Transport Is ExchangeTlsTransport Then
            Return Await ExchangeTlsTransport.ProbeAsync(address.Host, address.Port, ct).ConfigureAwait(False)
        End If
        ' an injected transport (a test): connect with no pin and report what it showed
        Using conn As ExchangeTlsConnection = Await ports.Transport.ConnectAsync(address.Host, address.Port, "", ct).ConfigureAwait(False)
            Return New ExchangeCertificateInfo(conn.Fingerprint, conn.Verdict = ExchangeTrustVerdict.PlatformValid)
        End Using
    End Function

    ''' <summary>The device name this program gives itself: "&lt;machine&gt; (viewer)", clipped to the wire's 64.</summary>
    Public Shared Function DefaultDeviceName(ports As ExchangeConsumerPorts) As String
        Dim machine As String = ExchangeDeviceDescription.Clip(ports.MachineName.Invoke(), 40)
        If machine.Length = 0 Then machine = "Windows PC"
        Return ExchangeDeviceDescription.Clip(machine & " " & ports.DeviceNameSuffix, 64)
    End Function

    ''' <summary>REGISTER by invitation (226 rule 1): TLS under the invitation string's trust, `register` with the code (or
    ''' the shared password), the chosen login and the password, and the answer `enrolled` stores the SAME entry a sign-in
    ''' stores - same shape, same token store, same pin decision. The request's secrets are wiped whatever happens, and
    ''' the invitation code is left behind nowhere (it crossed the wire once).</summary>
    Public Shared Async Function RegisterAsync(request As ExchangeConsumerRegisterRequest, ports As ExchangeConsumerPorts, ct As CancellationToken) As Task(Of ExchangeConsumerEnrollResult)
        Try
            Dim address As ExchangeServerAddress = Nothing
            Dim login As String = request.Login.Trim()
            Dim hasCode As Boolean = request.InvitationCode.Trim().Length > 0
            Dim hasShared As Boolean = request.SharedPassword.Length > 0
            If Not ExchangeServerAddress.TryParse(request.Server, address) OrElse login.Length = 0 OrElse
               request.Password.Length = 0 OrElse hasCode = hasShared Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "", Nothing)
            End If
            Dim pin As String = If(request.PlatformValid, "", request.TrustedFingerprint)
            If Not request.PlatformValid AndAlso Not ExchangeTrust.IsFingerprint(pin) Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, "", Nothing)
            End If

            ' a failed registration leaves nothing behind: the entry is stored only after `enrolled`
            Dim deviceName As String = If(request.DeviceName.Trim().Length > 0, ExchangeDeviceDescription.Clip(request.DeviceName, 64), DefaultDeviceName(ports))
            Dim entry As ExchangeEntry = ports.Entries.Propose(address.ToString(), login, ExchangeDeviceDescription.Clip(request.ServerName, 64), deviceName)
            Dim platformValid As Boolean = request.PlatformValid
            Dim password As String = request.Password
            Dim invitation As String = request.InvitationCode.Trim()
            Dim sharedPw As String = request.SharedPassword
            request.Wipe()
            Dim outcome As ExchangeConsumerEnrollResult = Nothing
            For attempt As Integer = 0 To 1
                outcome = Await TryRegisterOnceAsync(password, invitation, sharedPw, ports, address, pin, platformValid, entry, ct).ConfigureAwait(False)
                ' a device id already known in another account of the deployment: one fresh id, once (the same item G as a sign-in's)
                If outcome.Outcome = ExchangeConsumerEnrollOutcome.Refused AndAlso outcome.Refusal = ExchangeRefusal.IdCollision AndAlso attempt = 0 Then
                    entry = New ExchangeEntry(entry.Id, entry.Server, entry.Login, entry.Pin, entry.Certificate, ExchangeIds.NewId(), entry.DeviceName, entry.Name, entry.AutoAttach, entry.Seen, entry.KeyPin)
                    Continue For
                End If
                Exit For
            Next
            Return outcome
        Finally
            request.Wipe()
        End Try
    End Function

    Private Shared Async Function TryRegisterOnceAsync(password As String, invitation As String, sharedPw As String, ports As ExchangeConsumerPorts,
                                                       address As ExchangeServerAddress, pin As String, platformValid As Boolean, entry As ExchangeEntry, ct As CancellationToken) As Task(Of ExchangeConsumerEnrollResult)
        Dim description As New ExchangeDeviceDescription(entry.DeviceId, entry.DeviceName, ExchangeWire.ProductWindows,
            ExchangeDeviceDescription.Clip(ports.ProductVersion, 64), ExchangeDeviceDescription.PlatformText(),
            New List(Of String) From {ExchangeWire.RoleResourceConsumer}, "")
        Dim register As ExchangeEnvelope = ExchangeMessages.Register(invitation, sharedPw, entry.Login, password, description, ExchangeWire.ProposedKeepaliveSeconds)
        Dim opened As ExchangeOpenResult
        Try
            ' the answer of `register` is `enrolled`, the same frame a sign-in answers with (224 rule 6)
            opened = Await ExchangeSessions.EnrollAsync(ports.Transport, address, pin, register, ct).ConfigureAwait(False)
        Finally
            ' the frame's buffer was overwritten in the writer; the envelope's copies go too
            register.SetText("password", "").SetText("invitationCode", "").SetText("sharedPassword", "")
        End Try
        Select Case opened.Outcome
            Case ExchangeOpenOutcome.Connected
                ' the completion path is a sign-in's own: a registered entry is stored in the same shape (226 rule 1)
                Dim shim As New ExchangeConsumerEnrollRequest With {.PlatformValid = platformValid, .TrustedFingerprint = pin}
                Return Await CompleteAsync(opened, ports, address, shim, pin, entry).ConfigureAwait(False)
            Case Else
                Return OpenFailure(opened)
        End Select
    End Function

    ''' <summary>The open outcome of a registration attempt, in the sign-in's own vocabulary (a refusal, a pin mismatch,
    ''' a failed handshake - the same answers the dialog maps to the common sentences of 232).</summary>
    Private Shared Function OpenFailure(opened As ExchangeOpenResult) As ExchangeConsumerEnrollResult
        Select Case opened.Outcome
            Case ExchangeOpenOutcome.Refused
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Refused, opened.Refusal, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.PinMismatch
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.PinMismatch, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Untrusted
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.TlsFailed
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.TlsFailed, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Network
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Network, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.TimedOut
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.TimedOut, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Cancelled
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Cancelled, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case Else
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
        End Select
    End Function
    ''' <summary>The quick join, steps 2-4. The request's password is wiped whatever happens.</summary>
    Public Shared Async Function EnrollAsync(request As ExchangeConsumerEnrollRequest, ports As ExchangeConsumerPorts, ct As CancellationToken) As Task(Of ExchangeConsumerEnrollResult)
        Try
            Dim address As ExchangeServerAddress = Nothing
            Dim login As String = request.Login.Trim()
            If Not ExchangeServerAddress.TryParse(request.Server, address) OrElse login.Length = 0 OrElse request.Password.Length = 0 Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "", Nothing)
            End If
            Dim pin As String = If(request.PlatformValid, "", request.TrustedFingerprint)
            If Not request.PlatformValid AndAlso Not ExchangeTrust.IsFingerprint(pin) Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, "", Nothing)
            End If

            ' a failed enrollment leaves nothing behind: the entry is stored only after `enrolled`
            Dim deviceName As String = If(request.DeviceName.Trim().Length > 0, ExchangeDeviceDescription.Clip(request.DeviceName, 64), DefaultDeviceName(ports))
            Dim entry As ExchangeEntry = ports.Entries.Propose(address.ToString(), login, ExchangeDeviceDescription.Clip(request.ServerName, 64), deviceName)
            ' the field is cleared now; the one frame (and a retry after `id-collision`) is built from this local, which lives to the end of the call
            Dim password As String = request.Password
            request.Wipe()
            Dim outcome As ExchangeConsumerEnrollResult = Nothing
            For attempt As Integer = 0 To 1
                outcome = Await TryEnrollOnceAsync(password, request, ports, address, pin, entry, ct).ConfigureAwait(False)
                ' item G: a device id that is enrolled in another account of the deployment - one fresh id, once (a second is shown)
                If outcome.Outcome = ExchangeConsumerEnrollOutcome.Refused AndAlso outcome.Refusal = ExchangeRefusal.IdCollision AndAlso attempt = 0 Then
                    entry = New ExchangeEntry(entry.Id, entry.Server, entry.Login, entry.Pin, entry.Certificate, ExchangeIds.NewId(), entry.DeviceName, entry.Name, entry.AutoAttach, entry.Seen, entry.KeyPin)
                    Continue For
                End If
                Exit For
            Next
            Return outcome
        Finally
            request.Wipe()
        End Try
    End Function

    Private Shared Async Function TryEnrollOnceAsync(password As String, request As ExchangeConsumerEnrollRequest, ports As ExchangeConsumerPorts, address As ExchangeServerAddress,
                                                     pin As String, entry As ExchangeEntry, ct As CancellationToken) As Task(Of ExchangeConsumerEnrollResult)
        ' the one role of this device: it enrols, lists, attaches and opens a tunnel with nothing else on the machine
        Dim description As New ExchangeDeviceDescription(entry.DeviceId, entry.DeviceName, ExchangeWire.ProductWindows,
            ExchangeDeviceDescription.Clip(ports.ProductVersion, 64), ExchangeDeviceDescription.PlatformText(),
            New List(Of String) From {ExchangeWire.RoleResourceConsumer}, "")
        Dim enroll As ExchangeEnvelope = ExchangeMessages.Enroll(entry.Login, password, "", description, ExchangeWire.ProposedKeepaliveSeconds)
        Dim opened As ExchangeOpenResult
        Try
            opened = Await ExchangeSessions.EnrollAsync(ports.Transport, address, pin, enroll, ct).ConfigureAwait(False)
        Finally
            ' the frame's buffer was overwritten in the writer; the envelope's copy of the password goes too
            enroll.SetText("password", "")
        End Try

        Select Case opened.Outcome
            Case ExchangeOpenOutcome.Connected
                Return Await CompleteAsync(opened, ports, address, request, pin, entry).ConfigureAwait(False)
            Case ExchangeOpenOutcome.Refused
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Refused, opened.Refusal, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.PinMismatch
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.PinMismatch, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Untrusted
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.TlsFailed
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.TlsFailed, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Network
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Network, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.TimedOut
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.TimedOut, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case ExchangeOpenOutcome.Cancelled
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Cancelled, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            Case Else
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
        End Select
    End Function

    Private Shared Async Function CompleteAsync(opened As ExchangeOpenResult, ports As ExchangeConsumerPorts, address As ExchangeServerAddress,
                                                request As ExchangeConsumerEnrollRequest, pin As String, entry As ExchangeEntry) As Task(Of ExchangeConsumerEnrollResult)
        Using opened
            Dim token As String = opened.Welcome.Text("deviceToken")
            If Not ExchangeIds.IsValidToken(token) Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Protocol, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            End If
            Dim kind As ExchangeCertificateKind = If(request.PlatformValid, ExchangeCertificateKind.CaSigned, ExchangeCertificateKind.Pinned)
            Dim stored As New ExchangeEntry(entry.Id, entry.Server, entry.Login, pin, kind, entry.DeviceId, entry.DeviceName,
                                            entry.Name, entry.AutoAttach, entry.Seen, If(request.PlatformValid, "", opened.KeyPin))
            ' the token first, the entry last: a stored entry without its token is a worse state than a token without an entry
            Dim existing As ExchangeEntry = ports.Entries.Find(entry.Id)
            If existing Is Nothing AndAlso ports.Entries.Count() >= ExchangeEntryStore.MaxEntries Then
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.TooMany, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            End If
            If Not ports.Entries.TokenStoreFor(entry.Id).Save(entry.DeviceId, token) Then
                ports.Log.Invoke("Exchange: enrolled, but the token could not be stored under DPAPI")
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.StoreFailed, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            End If
            If Not ports.Entries.Upsert(stored) Then
                ports.Entries.TokenStoreFor(entry.Id).Delete()
                Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.StoreFailed, ExchangeRefusal.Unknown, opened.Fingerprint, Nothing)
            End If
            ' this stream only enrolled: the viewer opens a control stream when a view is open, not before
            Try
                Using cts As New CancellationTokenSource(1500)
                    Await opened.Writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Bye), cts.Token).ConfigureAwait(False)
                End Using
            Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is System.IO.IOException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
            End Try
            ports.Log.Invoke("Exchange: enrolled as device " & ExchangeIds.ForLog(entry.DeviceId))
            Return New ExchangeConsumerEnrollResult(ExchangeConsumerEnrollOutcome.Enrolled, ExchangeRefusal.Unknown, opened.Fingerprint, stored)
        End Using
    End Function

End Class
#End If
