#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports System.Text.Json

' Ticket L0278 T-2. The wire half of the remote administration client: the four frame types, the 18 carried
' operations of REMOTE-ADMIN (the other ten are answered `bad-request` and are never built here), one builder
' per operation that sets exactly the members of that operation, and the readers of the two answers.
'
' Everything goes through the envelope table of ExchangeEnvelope.vb (the `admin-*` rows) and the codec behind
' it - there is no second JSON writer here. A builder returns an ExchangeEnvelope for the caller to write with
' ExchangeFrameWriter (which wipes its buffer after the send); the request id is the caller's counter, which
' starts at 1 so a replay of the corpus compares byte for byte.
'
' The two traps of the table are settled here and not in a window:
'   - the `maxAccounts` row is WhenUnset (a 0 is written once the member is set), so the builder takes a
'     nullable and never touches the member for Nothing;
'   - the server REPLACES the whole `limits` override object of an account, so a limits value with nothing set
'     omits the member (which clears every override) and a value with members writes exactly those members.
'
' No user-visible word lives here, and nothing prints a secret: ToString of every type names the type or the kind
' and nothing else. Parsing a reply's `data` into views is not done here (it lives in the Companion); this layer
' only keeps the body as a JsonElement clone and checks that it is an object or an array where the operation
' says so.

''' <summary>The five per-account override members the server accepts in `limits` (account.limits, policy.set).
''' A member left Nothing is not written. The server replaces the whole object, so the caller sends every member
''' that is set after the edit.</summary>
Friend NotInheritable Class ExchangeAdminWireLimits

    Public Property DevicesPerAccount As Integer?
    Public Property ResourcesPerAccount As Integer?
    Public Property BroadcastsPerAccount As Integer?
    Public Property BroadcastsPerDevice As Integer?
    Public Property ClipboardMaxBytes As Long?

    ''' <summary>True when no member is set: the request then carries no `limits` at all.</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Not DevicesPerAccount.HasValue AndAlso Not ResourcesPerAccount.HasValue AndAlso
                   Not BroadcastsPerAccount.HasValue AndAlso Not BroadcastsPerDevice.HasValue AndAlso
                   Not ClipboardMaxBytes.HasValue
        End Get
    End Property

    ''' <summary>The compact JSON object in the server's member order, or "" when nothing is set.</summary>
    Public Function ToJson() As String
        If IsEmpty Then Return ""
        Dim w As New ExchangeJsonWriter()
        Try
            w.BeginObject()
            If DevicesPerAccount.HasValue Then
                w.Name("devicesPerAccount")
                w.Number(DevicesPerAccount.Value)
            End If
            If ResourcesPerAccount.HasValue Then
                w.Name("resourcesPerAccount")
                w.Number(ResourcesPerAccount.Value)
            End If
            If BroadcastsPerAccount.HasValue Then
                w.Name("broadcastsPerAccount")
                w.Number(BroadcastsPerAccount.Value)
            End If
            If BroadcastsPerDevice.HasValue Then
                w.Name("broadcastsPerDevice")
                w.Number(BroadcastsPerDevice.Value)
            End If
            If ClipboardMaxBytes.HasValue Then
                w.Name("clipboardMaxBytes")
                w.Number(ClipboardMaxBytes.Value)
            End If
            w.EndObject()
            Return Encoding.UTF8.GetString(w.ToArray())
        Finally
            w.Clear()
        End Try
    End Function

End Class

''' <summary>What an `admin-reply` came to.</summary>
Friend Enum ExchangeAdminWireReplyKind
    ''' <summary>`ok:true`. <see cref="ExchangeAdminWireReply.Data"/> holds the body when the reply carried one.</summary>
    Done = 1
    ''' <summary>`ok:false` with a code (one of the server's ten, or a newer one: the caller maps what it knows).</summary>
    Failed = 2
    ''' <summary>Not an `admin-reply`, a member missing or of the wrong shape, or a body of the wrong shape for the operation.</summary>
    Protocol = 3
End Enum

''' <summary>The result of reading one `admin-reply`. The server's free-text `error` member is deliberately not kept:
''' a person is told by the code, never by the server's words.</summary>
Friend NotInheritable Class ExchangeAdminWireReply

    ''' <summary>Done, Failed or Protocol.</summary>
    Public ReadOnly Kind As ExchangeAdminWireReplyKind
    ''' <summary>The reply's `id` (0 for a Protocol result that never got that far).</summary>
    Public ReadOnly Id As Long
    ''' <summary>The failure code of a Failed result, otherwise "".</summary>
    Public ReadOnly Code As String
    ''' <summary>True for a Done result that carried a `data` body.</summary>
    Public ReadOnly HasData As Boolean
    ''' <summary>The body of a Done result: a clone that outlives the frame it came from. Undefined when <see cref="HasData"/> is False.</summary>
    Public ReadOnly Data As JsonElement
    ''' <summary>A fixed reason for the log (never taken from the wire), set for a Protocol result only.</summary>
    Public ReadOnly Detail As String

    Private Sub New(kind As ExchangeAdminWireReplyKind, id As Long, code As String, hasData As Boolean, data As JsonElement, detail As String)
        Me.Kind = kind
        Me.Id = id
        Me.Code = code
        Me.HasData = hasData
        Me.Data = data
        Me.Detail = detail
    End Sub

    Public Shared Function NewDone(id As Long, hasData As Boolean, data As JsonElement) As ExchangeAdminWireReply
        Return New ExchangeAdminWireReply(ExchangeAdminWireReplyKind.Done, id, "", hasData, data, "")
    End Function

    Public Shared Function NewFailed(id As Long, code As String) As ExchangeAdminWireReply
        Dim none As JsonElement = Nothing
        Return New ExchangeAdminWireReply(ExchangeAdminWireReplyKind.Failed, id, code, False, none, "")
    End Function

    Public Shared Function NewProtocol(id As Long, detail As String) As ExchangeAdminWireReply
        Dim none As JsonElement = Nothing
        Return New ExchangeAdminWireReply(ExchangeAdminWireReplyKind.Protocol, id, "", False, none, detail)
    End Function

    ''' <summary>The kind and the code; never the body.</summary>
    Public Overrides Function ToString() As String
        Return "admin-reply " & Kind.ToString() & If(Code.Length > 0, " " & Code, "")
    End Function

End Class

''' <summary>A valid `admin-auth-ok`: the session token and the idle window the server announced.</summary>
Friend NotInheritable Class ExchangeAdminWireAuthOk

    ''' <summary>The bearer of the session. A secret: never logged, never printed.</summary>
    Public ReadOnly Token As String
    ''' <summary>The server's idle window in seconds, always greater than 0. It is not an absolute lifetime.</summary>
    Public ReadOnly ExpiresSeconds As Long

    Public Sub New(token As String, expiresSeconds As Long)
        Me.Token = token
        Me.ExpiresSeconds = expiresSeconds
    End Sub

    ''' <summary>Never the token.</summary>
    Public Overrides Function ToString() As String
        Return "admin-auth-ok"
    End Function

End Class

''' <summary>The frame types, the 18 carried operations, their builders and the readers of the answers.</summary>
Friend NotInheritable Class ExchangeAdminWire

    Private Sub New()
    End Sub

    ' --- the four frame types ---
    Public Const TypeAuth As String = "admin-auth"
    Public Const TypeAuthOk As String = "admin-auth-ok"
    Public Const TypeRequest As String = "admin-request"
    Public Const TypeReply As String = "admin-reply"

    ''' <summary>`admin-auth.password` is 1..1024 UTF-8 bytes; the server closes the stream without a word for anything else.</summary>
    Public Const MaxAuthPasswordBytes As Integer = 1024

    ''' <summary>The longest failure code kept. The server's codes are short ASCII words; anything longer is not one.</summary>
    Public Const MaxCodeChars As Integer = 64

    ' --- the 18 carried operations (the order of REMOTE-ADMIN's list) ---
    Public Const OpStatus As String = "status"
    Public Const OpAccountList As String = "account.list"
    Public Const OpAccountAdd As String = "account.add"
    Public Const OpAccountPasswd As String = "account.passwd"
    Public Const OpAccountKind As String = "account.kind"
    Public Const OpAccountRemove As String = "account.remove"
    Public Const OpAccountLimits As String = "account.limits"
    Public Const OpDeviceList As String = "device.list"
    Public Const OpDeviceRevoke As String = "device.revoke"
    Public Const OpDevicePairingCode As String = "device.pairing-code"
    Public Const OpInvitationCreate As String = "invitation.create"
    Public Const OpInvitationList As String = "invitation.list"
    Public Const OpInvitationWithdraw As String = "invitation.withdraw"
    Public Const OpSharedInvitationStatus As String = "shared_invitation.status"
    Public Const OpSharedInvitationSet As String = "shared_invitation.set"
    Public Const OpPolicyStatus As String = "policy.status"
    Public Const OpPolicySet As String = "policy.set"
    Public Const OpGrantList As String = "grant.list"

    Private Shared ReadOnly _carried As String() = {
        OpStatus, OpAccountList, OpAccountAdd, OpAccountPasswd, OpAccountKind, OpAccountRemove, OpAccountLimits,
        OpDeviceList, OpDeviceRevoke, OpDevicePairingCode, OpInvitationCreate, OpInvitationList, OpInvitationWithdraw,
        OpSharedInvitationStatus, OpSharedInvitationSet, OpPolicyStatus, OpPolicySet, OpGrantList}

    ''' <summary>The 18 operation names this client can build.</summary>
    Public Shared ReadOnly Property CarriedOps As IReadOnlyList(Of String)
        Get
            Return Array.AsReadOnly(_carried)
        End Get
    End Property

    ''' <summary>True for one of the 18 (an ordinal comparison).</summary>
    Public Shared Function IsCarried(op As String) As Boolean
        If op Is Nothing Then Return False
        Return Array.IndexOf(_carried, op) >= 0
    End Function

    ' --- the sign-in ---

    ''' <summary>True when the password is 1..1024 UTF-8 bytes (a lone surrogate counts the 3 bytes of U+FFFD, as the writer sends it).</summary>
    Public Shared Function AuthPasswordFits(password As Char()) As Boolean
        If password Is Nothing OrElse password.Length = 0 Then Return False
        ' every char is at least one byte, so a buffer of more chars than the cap can never fit
        If password.Length > MaxAuthPasswordBytes Then Return False
        Return Encoding.UTF8.GetByteCount(password) <= MaxAuthPasswordBytes
    End Function

    ''' <summary>The `admin-auth` frame. A password the server would close the stream on is refused HERE, before any
    ''' envelope or string exists (a silent close would read as "no answer"). The envelope holds the password as a string
    ''' until it is collected - the codec takes strings; the frame buffer itself is wiped by the writer after the send.</summary>
    Public Shared Function Auth(password As Char()) As ExchangeEnvelope
        If Not AuthPasswordFits(password) Then
            Throw New ExchangeWireException(ExchangeWireError.BadMessage, "the sign-in password is empty or above " & MaxAuthPasswordBytes.ToString(Globalization.CultureInfo.InvariantCulture) & " bytes")
        End If
        Return New ExchangeEnvelope(TypeAuth).SetText("password", New String(password))
    End Function

    ' --- one builder per carried operation (exactly the members of that operation) ---

    Public Shared Function Status(token As String, id As Long) As ExchangeEnvelope
        Return Request(token, id, OpStatus)
    End Function

    Public Shared Function AccountList(token As String, id As Long) As ExchangeEnvelope
        Return Request(token, id, OpAccountList)
    End Function

    ''' <summary>`kind`: member, guest, or "" for a member.</summary>
    Public Shared Function AccountAdd(token As String, id As Long, login As String, password As String, kind As String) As ExchangeEnvelope
        Return Request(token, id, OpAccountAdd).SetText("login", login).SetText("password", password).SetText("kind", kind)
    End Function

    ''' <summary>Signs every device of the account out on the server.</summary>
    Public Shared Function AccountPasswd(token As String, id As Long, login As String, password As String) As ExchangeEnvelope
        Return Request(token, id, OpAccountPasswd).SetText("login", login).SetText("password", password)
    End Function

    ''' <summary>An empty `kind` means member on the server, not "unchanged".</summary>
    Public Shared Function AccountKind(token As String, id As Long, login As String, kind As String) As ExchangeEnvelope
        Return Request(token, id, OpAccountKind).SetText("login", login).SetText("kind", kind)
    End Function

    Public Shared Function AccountRemove(token As String, id As Long, login As String) As ExchangeEnvelope
        Return Request(token, id, OpAccountRemove).SetText("login", login)
    End Function

    ''' <summary>The server REPLACES the account's override object with <paramref name="limits"/>; Nothing or an empty
    ''' value sends no `limits` member, which clears every override.</summary>
    Public Shared Function AccountLimits(token As String, id As Long, login As String, limits As ExchangeAdminWireLimits) As ExchangeEnvelope
        Dim env As ExchangeEnvelope = Request(token, id, OpAccountLimits).SetText("login", login)
        SetLimits(env, limits)
        Return env
    End Function

    Public Shared Function DeviceList(token As String, id As Long, login As String) As ExchangeEnvelope
        Return Request(token, id, OpDeviceList).SetText("login", login)
    End Function

    Public Shared Function DeviceRevoke(token As String, id As Long, login As String, deviceId As String) As ExchangeEnvelope
        Return Request(token, id, OpDeviceRevoke).SetText("login", login).SetText("deviceId", deviceId)
    End Function

    Public Shared Function DevicePairingCode(token As String, id As Long, login As String) As ExchangeEnvelope
        Return Request(token, id, OpDevicePairingCode).SetText("login", login)
    End Function

    ''' <summary>`kind`: member or guest. <paramref name="expectedLogin"/> is optional (Nothing or "" sends no member).</summary>
    Public Shared Function InvitationCreate(token As String, id As Long, kind As String, Optional expectedLogin As String = Nothing) As ExchangeEnvelope
        Dim env As ExchangeEnvelope = Request(token, id, OpInvitationCreate).SetText("kind", kind)
        If Not String.IsNullOrEmpty(expectedLogin) Then env.SetText("expectedLogin", expectedLogin)
        Return env
    End Function

    Public Shared Function InvitationList(token As String, id As Long) As ExchangeEnvelope
        Return Request(token, id, OpInvitationList)
    End Function

    ''' <summary>The full hash that `invitation.list` returned.</summary>
    Public Shared Function InvitationWithdraw(token As String, id As Long, codeHash As String) As ExchangeEnvelope
        Return Request(token, id, OpInvitationWithdraw).SetText("codeHash", codeHash)
    End Function

    Public Shared Function SharedInvitationStatus(token As String, id As Long) As ExchangeEnvelope
        Return Request(token, id, OpSharedInvitationStatus)
    End Function

    ''' <summary>An EMPTY password turns the shared invitation off on the server (the caller decides when that is wanted).</summary>
    Public Shared Function SharedInvitationSet(token As String, id As Long, password As String, kind As String) As ExchangeEnvelope
        Return Request(token, id, OpSharedInvitationSet).SetText("password", password).SetText("kind", kind)
    End Function

    Public Shared Function PolicyStatus(token As String, id As Long) As ExchangeEnvelope
        Return Request(token, id, OpPolicyStatus)
    End Function

    ''' <summary>The server merges: a member left out stays as it is. <paramref name="maxAccounts"/> Nothing writes no
    ''' `maxAccounts` member - never a 0 (the envelope row writes a 0 once the number is set).</summary>
    Public Shared Function PolicySet(token As String, id As Long, maxAccounts As Integer?, limits As ExchangeAdminWireLimits) As ExchangeEnvelope
        Dim env As ExchangeEnvelope = Request(token, id, OpPolicySet)
        If maxAccounts.HasValue Then env.SetNumber("maxAccounts", maxAccounts.Value)
        SetLimits(env, limits)
        Return env
    End Function

    ''' <summary>An owner's set (<paramref name="login"/>) or a guest's (<paramref name="guestAccount"/>); one of the two. Both
    ''' empty is a legal request the server answers with an empty list; both given is refused here (the server would
    ''' silently take the guest and ignore the login).</summary>
    Public Shared Function GrantList(token As String, id As Long, login As String, guestAccount As String) As ExchangeEnvelope
        If Not String.IsNullOrEmpty(login) AndAlso Not String.IsNullOrEmpty(guestAccount) Then
            Throw New ExchangeWireException(ExchangeWireError.BadMessage, "grant.list takes a login or a guest account, not both")
        End If
        Dim env As ExchangeEnvelope = Request(token, id, OpGrantList)
        If Not String.IsNullOrEmpty(login) Then env.SetText("login", login)
        If Not String.IsNullOrEmpty(guestAccount) Then env.SetText("guestAccount", guestAccount)
        Return env
    End Function

    Private Shared Function Request(token As String, id As Long, op As String) As ExchangeEnvelope
        If String.IsNullOrEmpty(token) Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "token is required")
        If id < 1 Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "the request id counts from 1")
        Return New ExchangeEnvelope(TypeRequest).SetNumber("id", id).SetText("token", token).SetText("op", op)
    End Function

    Private Shared Sub SetLimits(env As ExchangeEnvelope, limits As ExchangeAdminWireLimits)
        If limits Is Nothing OrElse limits.IsEmpty Then Return
        env.SetRecord("limits", limits.ToJson())
    End Sub

    ' --- the readers ---

    ''' <summary>The answer to a sign-in: Nothing (a protocol failure) unless the frame is an `admin-auth-ok` with a token and an
    ''' `expiresSeconds` greater than 0.</summary>
    Public Shared Function ReadAuthOk(env As ExchangeEnvelope) As ExchangeAdminWireAuthOk
        If env Is Nothing OrElse Not env.Is(TypeAuthOk) Then Return Nothing
        If Not env.Has("token") OrElse Not env.Has("expiresSeconds") Then Return Nothing
        Dim token As String = env.Text("token")
        Dim seconds As Long = env.Number("expiresSeconds")
        If token.Length = 0 OrElse seconds <= 0 Then Return Nothing
        Return New ExchangeAdminWireAuthOk(token, seconds)
    End Function

    ''' <summary>The same from the body of a frame; a body that is not an envelope is Nothing, never an exception.</summary>
    Public Shared Function ReadAuthOk(body As Byte()) As ExchangeAdminWireAuthOk
        Dim env As ExchangeEnvelope = Decode(body)
        If env Is Nothing Then Return Nothing
        Return ReadAuthOk(env)
    End Function

    ''' <summary>Reads one `admin-reply`. <paramref name="op"/> (optional) names the request it answers, so a body of the wrong
    ''' kind for that operation (an array for `status`, an object for `account.list`, no body for a list) is a Protocol
    ''' result; without it only "an object or an array" is checked. Never throws.</summary>
    Public Shared Function ReadReply(env As ExchangeEnvelope, Optional op As String = Nothing) As ExchangeAdminWireReply
        If env Is Nothing OrElse Not env.Is(TypeReply) Then Return ExchangeAdminWireReply.NewProtocol(0, "not-a-reply")
        If Not env.Has("id") OrElse Not env.Has("ok") Then Return ExchangeAdminWireReply.NewProtocol(0, "no-id-or-ok")
        Dim id As Long = env.Number("id")

        If Not env.Flag("ok") Then
            Dim code As String = env.Text("code")
            If code.Length = 0 OrElse code.Length > MaxCodeChars Then Return ExchangeAdminWireReply.NewProtocol(id, "no-code")
            Return ExchangeAdminWireReply.NewFailed(id, code)
        End If

        Dim shape As ExpectedBody = ExpectedBodyOf(op)
        Dim none As JsonElement = Nothing
        If Not env.Has("data") Then
            If shape = ExpectedBody.Absent OrElse shape = ExpectedBody.Unknown Then Return ExchangeAdminWireReply.NewDone(id, False, none)
            Return ExchangeAdminWireReply.NewProtocol(id, "no-data")
        End If

        Try
            Using doc As JsonDocument = JsonDocument.Parse(env.Record("data"))
                Dim kind As JsonValueKind = doc.RootElement.ValueKind
                If kind <> JsonValueKind.Object AndAlso kind <> JsonValueKind.Array Then Return ExchangeAdminWireReply.NewProtocol(id, "data-shape")
                If shape = ExpectedBody.Record AndAlso kind <> JsonValueKind.Object Then Return ExchangeAdminWireReply.NewProtocol(id, "data-shape")
                If shape = ExpectedBody.Items AndAlso kind <> JsonValueKind.Array Then Return ExchangeAdminWireReply.NewProtocol(id, "data-shape")
                Return ExchangeAdminWireReply.NewDone(id, True, doc.RootElement.Clone())
            End Using
        Catch ex As JsonException
            Return ExchangeAdminWireReply.NewProtocol(id, "data-json")
        End Try
    End Function

    ''' <summary>The same from the body of a frame; a body that is not an envelope is a Protocol result, never an exception.</summary>
    Public Shared Function ReadReply(body As Byte(), Optional op As String = Nothing) As ExchangeAdminWireReply
        Dim env As ExchangeEnvelope = Decode(body)
        If env Is Nothing Then Return ExchangeAdminWireReply.NewProtocol(0, "malformed")
        Return ReadReply(env, op)
    End Function

    Private Shared Function Decode(body As Byte()) As ExchangeEnvelope
        If body Is Nothing OrElse body.Length = 0 Then Return Nothing
        Try
            Return ExchangeEnvelopeCodec.Decode(body)
        Catch ex As ExchangeWireException
            Return Nothing
        End Try
    End Function

    ''' <summary>What `data` a successful reply of an operation carries.</summary>
    Private Enum ExpectedBody
        ''' <summary>Not one of the 18 (or not named): an object or an array is accepted, and so is no body.</summary>
        Unknown
        ''' <summary>No body is expected; one that is present is tolerated.</summary>
        Absent
        ''' <summary>A JSON object.</summary>
        Record
        ''' <summary>A JSON array (`[]` when empty, never null).</summary>
        Items
    End Enum

    Private Shared Function ExpectedBodyOf(op As String) As ExpectedBody
        Select Case op
            Case OpStatus, OpPolicyStatus, OpPolicySet, OpAccountAdd, OpAccountPasswd, OpAccountKind,
                 OpInvitationCreate, OpSharedInvitationStatus, OpDevicePairingCode
                Return ExpectedBody.Record
            Case OpAccountList, OpDeviceList, OpInvitationList, OpGrantList
                Return ExpectedBody.Items
            Case OpAccountRemove, OpAccountLimits, OpDeviceRevoke, OpInvitationWithdraw, OpSharedInvitationSet
                Return ExpectedBody.Absent
            Case Else
                Return ExpectedBody.Unknown
        End Select
    End Function

End Class
#End If
