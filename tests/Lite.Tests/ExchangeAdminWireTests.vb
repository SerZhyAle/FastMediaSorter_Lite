#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket L0278 T-2: the wire half of the remote administration client (ExchangeAdminWire). Each of the 18 builders is held
''' to the bytes of the matching client request of the `admin-ops` stream in the local corpus (frames-v2.json, the
''' `admin-native` transcript), with the token of that stream's own `admin-auth-ok`; the sign-in frame is held to the
''' corpus' `admin-auth` frames; there is no builder for the ten operations the server does not carry; and the readers
''' are held to the corpus replies, to a reply with an unknown extra member, and to bodies of the wrong shape (a Protocol
''' result, never an exception). No socket and no clock: the session tests are a later task.
''' </summary>
Public Class ExchangeAdminWireTests

    Private Const Token As String = "T0kenForTheBuilderTests-AAAAAAAAAAAAAAAAAAAAA"

    ' The ten operations the server does not carry (they are answered `bad-request`); no builder, no constant.
    Private Shared ReadOnly Uncarried As String() = {
        "host.settings", "grant.add", "grant.remove", "broadcast.list", "broadcast.end",
        "resource.list", "resource.unpublish", "clipboard.info", "clipboard.erase", "admin.audit.list"}

    Private Shared ReadOnly Carried18 As String() = {
        "status", "account.list", "account.add", "account.passwd", "account.kind", "account.remove", "account.limits",
        "device.list", "device.revoke", "device.pairing-code", "invitation.create", "invitation.list", "invitation.withdraw",
        "shared_invitation.status", "shared_invitation.set", "policy.status", "policy.set", "grant.list"}

    ' ---------------------------------------------------------------- the corpus

    Private NotInheritable Class CorpusStep
        Public Direction As String
        Public Json As String
        Public Hex As String
    End Class

    ''' <summary>The steps of one stream of the `admin-native` transcript that carry a frame (a `closed` step has none).</summary>
    Private Shared Function StepsOf(stream As String) As List(Of CorpusStep)
        Dim list As New List(Of CorpusStep)
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(ExchangeVectorCorpus.TranscriptPath(), New UTF8Encoding(False)))
            For Each transcript As JsonElement In doc.RootElement.GetProperty("transcripts").EnumerateArray()
                If transcript.GetProperty("name").GetString() <> "admin-native" Then Continue For
                For Each stepEl As JsonElement In transcript.GetProperty("steps").EnumerateArray()
                    If stream IsNot Nothing AndAlso stepEl.GetProperty("stream").GetString() <> stream Then Continue For
                    Dim json As JsonElement = Nothing
                    Dim hex As JsonElement = Nothing
                    If Not stepEl.TryGetProperty("json", json) OrElse Not stepEl.TryGetProperty("hex", hex) Then Continue For
                    Dim item As New CorpusStep()
                    item.Direction = stepEl.GetProperty("direction").GetString()
                    item.Json = json.GetString()
                    item.Hex = hex.GetString()
                    list.Add(item)
                Next
            Next
        End Using
        Assert.True(list.Count > 0, "the admin-native transcript was not found in frames-v2.json")
        Return list
    End Function

    Private Shared Function FrameType(json As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Return doc.RootElement.GetProperty("type").GetString()
        End Using
    End Function

    ''' <summary>The token of the `admin-auth-ok` of the stream.</summary>
    Private Shared Function TokenOf(steps As List(Of CorpusStep)) As String
        For Each s As CorpusStep In steps
            If s.Direction = "server->client" AndAlso FrameType(s.Json) = "admin-auth-ok" Then
                Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                    Return doc.RootElement.GetProperty("token").GetString()
                End Using
            End If
        Next
        Throw New InvalidOperationException("no admin-auth-ok in the stream")
    End Function

    Private Shared Function Member(el As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return Nothing
    End Function

    Private Shared Function MaxAccountsOf(el As JsonElement) As Integer?
        Dim v As JsonElement = Nothing
        If el.TryGetProperty("maxAccounts", v) Then Return v.GetInt32()
        Return Nothing
    End Function

    Private Shared Function LimitsOf(el As JsonElement) As ExchangeAdminWireLimits
        Dim l As JsonElement = Nothing
        If Not el.TryGetProperty("limits", l) Then Return Nothing
        Dim result As New ExchangeAdminWireLimits()
        Dim v As JsonElement = Nothing
        If l.TryGetProperty("devicesPerAccount", v) Then result.DevicesPerAccount = v.GetInt32()
        If l.TryGetProperty("resourcesPerAccount", v) Then result.ResourcesPerAccount = v.GetInt32()
        If l.TryGetProperty("broadcastsPerAccount", v) Then result.BroadcastsPerAccount = v.GetInt32()
        If l.TryGetProperty("broadcastsPerDevice", v) Then result.BroadcastsPerDevice = v.GetInt32()
        If l.TryGetProperty("clipboardMaxBytes", v) Then result.ClipboardMaxBytes = v.GetInt64()
        Return result
    End Function

    ''' <summary>One builder call per carried op, its arguments read from the corpus request itself (so the test cannot
    ''' drift from the corpus); what is compared is the bytes the builder and the codec then produce.</summary>
    Private Shared Function Builders() As Dictionary(Of String, Func(Of JsonElement, String, Long, ExchangeEnvelope))
        Dim d As New Dictionary(Of String, Func(Of JsonElement, String, Long, ExchangeEnvelope))(StringComparer.Ordinal)
        d(ExchangeAdminWire.OpStatus) = Function(r, t, i) ExchangeAdminWire.Status(t, i)
        d(ExchangeAdminWire.OpAccountList) = Function(r, t, i) ExchangeAdminWire.AccountList(t, i)
        d(ExchangeAdminWire.OpAccountAdd) = Function(r, t, i) ExchangeAdminWire.AccountAdd(t, i, Member(r, "login"), Member(r, "password"), Member(r, "kind"))
        d(ExchangeAdminWire.OpAccountPasswd) = Function(r, t, i) ExchangeAdminWire.AccountPasswd(t, i, Member(r, "login"), Member(r, "password"))
        d(ExchangeAdminWire.OpAccountKind) = Function(r, t, i) ExchangeAdminWire.AccountKind(t, i, Member(r, "login"), Member(r, "kind"))
        d(ExchangeAdminWire.OpAccountRemove) = Function(r, t, i) ExchangeAdminWire.AccountRemove(t, i, Member(r, "login"))
        d(ExchangeAdminWire.OpAccountLimits) = Function(r, t, i) ExchangeAdminWire.AccountLimits(t, i, Member(r, "login"), LimitsOf(r))
        d(ExchangeAdminWire.OpDeviceList) = Function(r, t, i) ExchangeAdminWire.DeviceList(t, i, Member(r, "login"))
        d(ExchangeAdminWire.OpDeviceRevoke) = Function(r, t, i) ExchangeAdminWire.DeviceRevoke(t, i, Member(r, "login"), Member(r, "deviceId"))
        d(ExchangeAdminWire.OpDevicePairingCode) = Function(r, t, i) ExchangeAdminWire.DevicePairingCode(t, i, Member(r, "login"))
        d(ExchangeAdminWire.OpInvitationCreate) = Function(r, t, i) ExchangeAdminWire.InvitationCreate(t, i, Member(r, "kind"), Member(r, "expectedLogin"))
        d(ExchangeAdminWire.OpInvitationList) = Function(r, t, i) ExchangeAdminWire.InvitationList(t, i)
        d(ExchangeAdminWire.OpInvitationWithdraw) = Function(r, t, i) ExchangeAdminWire.InvitationWithdraw(t, i, Member(r, "codeHash"))
        d(ExchangeAdminWire.OpSharedInvitationStatus) = Function(r, t, i) ExchangeAdminWire.SharedInvitationStatus(t, i)
        d(ExchangeAdminWire.OpSharedInvitationSet) = Function(r, t, i) ExchangeAdminWire.SharedInvitationSet(t, i, Member(r, "password"), Member(r, "kind"))
        d(ExchangeAdminWire.OpPolicyStatus) = Function(r, t, i) ExchangeAdminWire.PolicyStatus(t, i)
        d(ExchangeAdminWire.OpPolicySet) = Function(r, t, i) ExchangeAdminWire.PolicySet(t, i, MaxAccountsOf(r), LimitsOf(r))
        d(ExchangeAdminWire.OpGrantList) = Function(r, t, i) ExchangeAdminWire.GrantList(t, i, Member(r, "login"), Member(r, "guestAccount"))
        Return d
    End Function

    Private Shared Function HexOf(bytes As Byte()) As String
        Return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
    End Function

    Private Shared Function Utf8(bytes As Byte()) As String
        Return Encoding.UTF8.GetString(bytes)
    End Function

    ''' <summary>Single quotes stand for the double quotes of a JSON literal.</summary>
    Private Shared Function J(s As String) As String
        Return s.Replace("'"c, """"c)
    End Function

    Private Shared Function Body(env As ExchangeEnvelope) As String
        Return Utf8(ExchangeEnvelopeCodec.Encode(env))
    End Function

    ' ---------------------------------------------------------------- builders against the corpus

    <Fact>
    Public Sub Each_of_the_18_builders_produces_the_bytes_of_the_corpus_request_of_the_admin_ops_stream()
        Dim steps As List(Of CorpusStep) = StepsOf("admin-ops")
        Dim tokenOfStream As String = TokenOf(steps)
        Dim byOp As Dictionary(Of String, Func(Of JsonElement, String, Long, ExchangeEnvelope)) = Builders()
        Dim seen As New List(Of String)
        Dim expectedId As Long = 0
        For Each s As CorpusStep In steps
            If s.Direction <> "client->server" Then Continue For
            If FrameType(s.Json) <> "admin-request" Then Continue For
            Using req As JsonDocument = JsonDocument.Parse(s.Json)
                Dim root As JsonElement = req.RootElement
                expectedId += 1
                Dim op As String = root.GetProperty("op").GetString()
                Assert.Equal(expectedId, root.GetProperty("id").GetInt64())
                Assert.Equal(tokenOfStream, root.GetProperty("token").GetString())
                Assert.True(byOp.ContainsKey(op), "the corpus asks for an op with no builder: " & op)
                Dim env As ExchangeEnvelope = byOp(op)(root, tokenOfStream, expectedId)
                Dim bytes As Byte() = ExchangeEnvelopeCodec.Encode(env)
                Assert.Equal(s.Json, Utf8(bytes))
                Assert.Equal(Encoding.UTF8.GetBytes(s.Json), bytes)
                Assert.Equal(s.Hex, HexOf(ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFrameAuthenticated)))
                seen.Add(op)
            End Using
        Next
        Assert.Equal(18, seen.Count)
        Assert.Equal(Carried18.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), seen.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
        ' the stream's own order, which is also the order of the spec's list
        Assert.Equal(New String() {"status", "policy.status", "policy.set", "account.list", "account.add", "account.limits", "account.passwd", "account.kind",
                      "invitation.create", "invitation.list", "invitation.withdraw", "shared_invitation.status", "shared_invitation.set",
                      "device.list", "device.pairing-code", "device.revoke", "grant.list", "account.remove"}, seen.ToArray())
    End Sub

    <Fact>
    Public Sub The_sign_in_frame_equals_every_admin_auth_frame_of_the_corpus_and_the_first_one_carries_AdminSecretPass1()
        Dim steps As List(Of CorpusStep) = StepsOf(Nothing)
        Dim checkedFrames As Integer = 0
        Dim first As Boolean = True
        For Each s As CorpusStep In steps
            If s.Direction <> "client->server" Then Continue For
            If FrameType(s.Json) <> "admin-auth" Then Continue For
            Dim password As String
            Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                password = doc.RootElement.GetProperty("password").GetString()
            End Using
            If first Then
                Assert.Equal("AdminSecretPass1", password)
                first = False
            End If
            Dim env As ExchangeEnvelope = ExchangeAdminWire.Auth(password.ToCharArray())
            Assert.Equal(s.Json, Body(env))
            Assert.Equal(s.Hex, HexOf(ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFramePreAuth)))
            checkedFrames += 1
        Next
        Assert.True(checkedFrames >= 8, "expected an admin-auth frame in each of the 8 streams, found " & checkedFrames.ToString())
    End Sub

    ' ---------------------------------------------------------------- the op set

    <Fact>
    Public Sub The_op_names_are_exactly_the_18_carried_ones_and_none_of_the_ten_uncarried_has_a_name_or_a_builder()
        Dim ops As IReadOnlyList(Of String) = ExchangeAdminWire.CarriedOps
        Assert.Equal(18, ops.Count)
        Assert.Equal(18, ops.Distinct(StringComparer.Ordinal).Count())
        Assert.Equal(Carried18.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), ops.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
        For Each name As String In Carried18
            Assert.True(ExchangeAdminWire.IsCarried(name), name)
        Next
        For Each name As String In Uncarried
            Assert.False(ExchangeAdminWire.IsCarried(name), name)
        Next
        Assert.False(ExchangeAdminWire.IsCarried(Nothing))

        ' no public constant of the class carries an uncarried name, and the Op* constants are the 18
        Dim constants As List(Of FieldInfo) = GetType(ExchangeAdminWire).GetFields(BindingFlags.Public Or BindingFlags.Static).
            Where(Function(f) f.IsLiteral AndAlso f.FieldType Is GetType(String)).ToList()
        Dim values As List(Of String) = constants.Select(Function(f) CStr(f.GetRawConstantValue())).ToList()
        For Each name As String In Uncarried
            Assert.DoesNotContain(name, values)
        Next
        Dim opConstants As String() = constants.Where(Function(f) f.Name.StartsWith("Op", StringComparison.Ordinal)).
            Select(Function(f) CStr(f.GetRawConstantValue())).OrderBy(Function(x) x, StringComparer.Ordinal).ToArray()
        Assert.Equal(Carried18.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), opConstants)

        ' the builder set: the sign-in plus one per carried op, nothing else returns an envelope
        Dim builderNames As List(Of String) = GetType(ExchangeAdminWire).GetMethods(BindingFlags.Public Or BindingFlags.Static).
            Where(Function(m) m.ReturnType Is GetType(ExchangeEnvelope)).Select(Function(m) m.Name).ToList()
        Assert.Equal(19, builderNames.Count)
        Assert.Contains("Auth", builderNames)
        Assert.Equal(19, builderNames.Distinct(StringComparer.Ordinal).Count())
        ' and the corpus test drives one builder per carried op (Builders() has exactly the 18)
        Assert.Equal(Carried18.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), Builders().Keys.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
    End Sub

    ' ---------------------------------------------------------------- the two traps of the table

    <Fact>
    Public Sub MaxAccounts_unset_is_absent_and_never_a_zero_by_accident()
        Dim unset As String = Body(ExchangeAdminWire.PolicySet(Token, 1, Nothing, Nothing))
        Assert.Equal(J("{'schemaVersion':2,'type':'admin-request','id':1,'token':'" & Token & "','op':'policy.set'}"), unset)
        Using doc As JsonDocument = JsonDocument.Parse(unset)
            Dim v As JsonElement = Nothing
            Assert.False(doc.RootElement.TryGetProperty("maxAccounts", v))
            Assert.False(doc.RootElement.TryGetProperty("limits", v))
        End Using

        ' a number the caller sets is written as it is, a deliberate 0 included (the server answers bad-limit)
        Assert.Contains(J("'maxAccounts':8"), Body(ExchangeAdminWire.PolicySet(Token, 2, 8, Nothing)))
        Assert.Contains(J("'maxAccounts':0"), Body(ExchangeAdminWire.PolicySet(Token, 3, 0, Nothing)))

        ' a limits value alone does not bring a maxAccounts with it
        Dim limits As New ExchangeAdminWireLimits() With {.ClipboardMaxBytes = 5368709120L}
        Dim onlyLimits As String = Body(ExchangeAdminWire.PolicySet(Token, 4, Nothing, limits))
        Assert.DoesNotContain("maxAccounts", onlyLimits)
        Assert.Contains(J("'limits':{'clipboardMaxBytes':5368709120}"), onlyLimits)
    End Sub

    <Fact>
    Public Sub Limits_are_omitted_when_nothing_is_set_and_written_member_by_member_otherwise()
        Dim expectedNone As String = J("{'schemaVersion':2,'type':'admin-request','id':1,'token':'" & Token & "','op':'account.limits','login':'bob'}")
        Assert.Equal(expectedNone, Body(ExchangeAdminWire.AccountLimits(Token, 1, "bob", Nothing)))
        Assert.Equal(expectedNone, Body(ExchangeAdminWire.AccountLimits(Token, 1, "bob", New ExchangeAdminWireLimits())))
        Assert.True(New ExchangeAdminWireLimits().IsEmpty)
        Assert.Equal("", New ExchangeAdminWireLimits().ToJson())

        Dim all As New ExchangeAdminWireLimits() With {
            .DevicesPerAccount = 1, .ResourcesPerAccount = 2, .BroadcastsPerAccount = 3, .BroadcastsPerDevice = 4, .ClipboardMaxBytes = 5}
        Assert.False(all.IsEmpty)
        Assert.Equal(J("{'devicesPerAccount':1,'resourcesPerAccount':2,'broadcastsPerAccount':3,'broadcastsPerDevice':4,'clipboardMaxBytes':5}"), all.ToJson())
        Assert.Contains(J("'login':'bob','limits':{'devicesPerAccount':1,'resourcesPerAccount':2,'broadcastsPerAccount':3,'broadcastsPerDevice':4,'clipboardMaxBytes':5}}"),
                        Body(ExchangeAdminWire.AccountLimits(Token, 1, "bob", all)))

        ' a zero that was SET is a member (the server reads 0 as "none of this kind")
        Dim zero As New ExchangeAdminWireLimits() With {.BroadcastsPerDevice = 0}
        Assert.False(zero.IsEmpty)
        Assert.Equal(J("{'broadcastsPerDevice':0}"), zero.ToJson())
    End Sub

    <Fact>
    Public Sub Optional_members_are_sent_only_when_given()
        Assert.Contains(J("'kind':'guest','expectedLogin':'carol'"), Body(ExchangeAdminWire.InvitationCreate(Token, 1, "guest", "carol")))
        Assert.DoesNotContain("expectedLogin", Body(ExchangeAdminWire.InvitationCreate(Token, 1, "member")))
        Assert.DoesNotContain("expectedLogin", Body(ExchangeAdminWire.InvitationCreate(Token, 1, "member", "")))

        Assert.EndsWith(J("'op':'grant.list','login':'alice'}"), Body(ExchangeAdminWire.GrantList(Token, 1, "alice", Nothing)))
        Assert.EndsWith(J("'op':'grant.list','guestAccount':'gus'}"), Body(ExchangeAdminWire.GrantList(Token, 1, Nothing, "gus")))
        Assert.EndsWith(J("'op':'grant.list'}"), Body(ExchangeAdminWire.GrantList(Token, 1, "", "")))
        Dim both As ExchangeWireException = Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.GrantList(Token, 1, "alice", "gus"))
        Assert.Equal(ExchangeWireError.BadMessage, both.Error)
    End Sub

    <Fact>
    Public Sub A_request_needs_a_token_and_an_id_that_counts_from_1()
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Status("", 1)).Error)
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Status(Nothing, 1)).Error)
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Status(Token, 0)).Error)
        Assert.Equal(ExchangeWireError.BadMessage, Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.AccountRemove(Token, -1, "bob")).Error)
        Assert.Contains(J("'id':1,"), Body(ExchangeAdminWire.Status(Token, 1)))
    End Sub

    ' ---------------------------------------------------------------- the sign-in password bound

    Private Shared Function Repeat(unit As String, times As Integer) As Char()
        Dim sb As New StringBuilder()
        For i As Integer = 1 To times
            sb.Append(unit)
        Next
        Return sb.ToString().ToCharArray()
    End Function

    <Fact>
    Public Sub A_password_of_1024_bytes_is_accepted_and_one_of_1025_is_refused_before_anything_is_built()
        Dim ok As Char() = Repeat("a", 1024)
        Assert.True(ExchangeAdminWire.AuthPasswordFits(ok))
        Dim env As ExchangeEnvelope = ExchangeAdminWire.Auth(ok)
        Assert.Equal(1024, Encoding.UTF8.GetByteCount(env.Text("password")))
        ' the whole frame stays far under the 16384 bytes allowed before the sign-in completes
        Assert.True(ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFramePreAuth).Length < ExchangeWire.MaxFramePreAuth)

        Dim tooLong As Char() = Repeat("a", 1025)
        Assert.False(ExchangeAdminWire.AuthPasswordFits(tooLong))
        Dim refused As ExchangeWireException = Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Auth(tooLong))
        Assert.Equal(ExchangeWireError.BadMessage, refused.Error)
        Assert.DoesNotContain("aaaa", refused.Message)

        ' the bound is in BYTES: 512 two-byte characters fit, 513 do not; 256 four-byte pairs fit, 257 do not
        Assert.True(ExchangeAdminWire.AuthPasswordFits(Repeat(ChrW(&HE9), 512)))
        Assert.False(ExchangeAdminWire.AuthPasswordFits(Repeat(ChrW(&HE9), 513)))
        Dim pair As String = ChrW(&HD83D) & ChrW(&HDE00)
        Assert.True(ExchangeAdminWire.AuthPasswordFits(Repeat(pair, 256)))
        Assert.False(ExchangeAdminWire.AuthPasswordFits(Repeat(pair, 257)))
        ' a lone surrogate is sent as U+FFFD, 3 bytes
        Assert.True(ExchangeAdminWire.AuthPasswordFits(Repeat(ChrW(&HD83D), 341)))
        Assert.False(ExchangeAdminWire.AuthPasswordFits(Repeat(ChrW(&HD83D), 342)))

        ' the server closes the stream on an empty password as well
        Assert.False(ExchangeAdminWire.AuthPasswordFits(Nothing))
        Assert.False(ExchangeAdminWire.AuthPasswordFits(New Char() {}))
        Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Auth(Nothing))
        Assert.Throws(Of ExchangeWireException)(Sub() ExchangeAdminWire.Auth(New Char() {}))
    End Sub

    ' ---------------------------------------------------------------- the readers against the corpus

    <Fact>
    Public Sub The_readers_accept_the_corpus_answers_of_the_admin_ops_stream()
        Dim steps As List(Of CorpusStep) = StepsOf("admin-ops")
        Dim tokenOfStream As String = TokenOf(steps)

        ' which op each id asked for
        Dim opById As New Dictionary(Of Long, String)
        For Each s As CorpusStep In steps
            If s.Direction <> "client->server" OrElse FrameType(s.Json) <> "admin-request" Then Continue For
            Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                opById(doc.RootElement.GetProperty("id").GetInt64()) = doc.RootElement.GetProperty("op").GetString()
            End Using
        Next
        Assert.Equal(18, opById.Count)

        Dim replies As Integer = 0
        For Each s As CorpusStep In steps
            If s.Direction <> "server->client" Then Continue For
            Dim type As String = FrameType(s.Json)
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(s.Json)
            If type = "admin-auth-ok" Then
                Dim authOk As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(bytes)
                Assert.NotNull(authOk)
                Assert.Equal(tokenOfStream, authOk.Token)
                Assert.Equal(900L, authOk.ExpiresSeconds)
                Continue For
            End If
            If type <> "admin-reply" Then Continue For
            replies += 1
            Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                Dim root As JsonElement = doc.RootElement
                Dim id As Long = root.GetProperty("id").GetInt64()
                Dim dataProbe As JsonElement = Nothing
                Dim hasData As Boolean = root.TryGetProperty("data", dataProbe)
                For Each withOp As Boolean In New Boolean() {True, False}
                    Dim reply As ExchangeAdminWireReply = If(withOp, ExchangeAdminWire.ReadReply(bytes, opById(id)), ExchangeAdminWire.ReadReply(bytes))
                    Assert.Equal(id, reply.Id)
                    If id = 16 Then
                        ' device.revoke: the only failed reply of the corpus
                        Assert.Equal(ExchangeAdminWireReplyKind.Failed, reply.Kind)
                        Assert.Equal("not-found", reply.Code)
                        Assert.False(reply.HasData)
                    Else
                        Assert.Equal(ExchangeAdminWireReplyKind.Done, reply.Kind)
                        Assert.Equal("", reply.Code)
                        Assert.Equal(hasData, reply.HasData)
                        If hasData Then Assert.Equal(root.GetProperty("data").GetRawText(), reply.Data.GetRawText())
                    End If
                Next
            End Using
        Next
        Assert.Equal(18, replies)
    End Sub

    <Fact>
    Public Sub A_Done_body_outlives_the_frame_and_keeps_the_values_a_view_parser_will_need()
        Dim steps As List(Of CorpusStep) = StepsOf("admin-ops")
        Dim status As ExchangeAdminWireReply = Nothing
        Dim created As ExchangeAdminWireReply = Nothing
        For Each s As CorpusStep In steps
            If s.Direction <> "server->client" OrElse FrameType(s.Json) <> "admin-reply" Then Continue For
            Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                Dim id As Long = doc.RootElement.GetProperty("id").GetInt64()
                If id = 1 Then status = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(s.Json), ExchangeAdminWire.OpStatus)
                If id = 9 Then created = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(s.Json), ExchangeAdminWire.OpInvitationCreate)
            End Using
        Next
        ' (the documents above are disposed by now)
        Assert.Equal(JsonValueKind.Object, status.Data.ValueKind)
        Assert.Equal(1, status.Data.GetProperty("accounts").GetInt32())
        Assert.Equal(10, status.Data.GetProperty("maxAccounts").GetInt32())
        Assert.False(status.Data.GetProperty("sealed").GetBoolean())
        ' a `&` travels as & and comes back as itself
        Dim connectionString As String = created.Data.GetProperty("connectionString").GetString()
        Assert.StartsWith("FMSEXCH1:x=exchange.example.net%3A22022&fp=SHA256%3A", connectionString)
        Assert.Equal(64, created.Data.GetProperty("invitation").GetProperty("codeHash").GetString().Length)
    End Sub

    <Fact>
    Public Sub An_unknown_extra_member_changes_nothing_in_either_reader()
        Dim steps As List(Of CorpusStep) = StepsOf("admin-ops")
        Dim extra As String = J(",'future':{'a':[1,2,{'b':null}],'c':'d'},'zzz':7")
        Dim checkedReplies As Integer = 0
        For Each s As CorpusStep In steps
            If s.Direction <> "server->client" Then Continue For
            Dim type As String = FrameType(s.Json)
            If type <> "admin-reply" AndAlso type <> "admin-auth-ok" Then Continue For
            Dim plain As Byte() = Encoding.UTF8.GetBytes(s.Json)
            Dim marker As String = J("'type':'" & type & "'")
            Assert.Contains(marker, s.Json)
            Dim widened As Byte() = Encoding.UTF8.GetBytes(s.Json.Replace(marker, marker & extra))
            If type = "admin-auth-ok" Then
                Dim a As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(plain)
                Dim b As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(widened)
                Assert.NotNull(b)
                Assert.Equal(a.Token, b.Token)
                Assert.Equal(a.ExpiresSeconds, b.ExpiresSeconds)
            Else
                Dim a As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(plain)
                Dim b As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(widened)
                Assert.Equal(a.Kind, b.Kind)
                Assert.Equal(a.Id, b.Id)
                Assert.Equal(a.Code, b.Code)
                Assert.Equal(a.HasData, b.HasData)
                If a.HasData Then Assert.Equal(a.Data.GetRawText(), b.Data.GetRawText())
            End If
            checkedReplies += 1
        Next
        Assert.Equal(19, checkedReplies)
    End Sub

    ' ---------------------------------------------------------------- wrong shapes are a result, not an exception

    <Fact>
    Public Sub A_reply_of_the_wrong_shape_is_a_Protocol_result_and_never_an_exception()
        Dim head As String = "{'schemaVersion':2,'type':'admin-reply',"
        Dim cases As String()() = {
            New String() {head & "'id':1,'ok':true,'data':'text'}", Nothing},
            New String() {head & "'id':1,'ok':true,'data':7}", Nothing},
            New String() {head & "'id':1,'ok':true,'data':null}", Nothing},
            New String() {head & "'id':1,'ok':true,'data':true}", Nothing},
            New String() {head & "'id':1,'ok':true,'data':[]}", "status"},
            New String() {head & "'id':1,'ok':true,'data':{}}", "account.list"},
            New String() {head & "'id':1,'ok':true,'data':{}}", "device.list"},
            New String() {head & "'id':1,'ok':true,'data':[]}", "policy.set"},
            New String() {head & "'id':1,'ok':true}", "status"},
            New String() {head & "'id':1,'ok':true}", "account.list"},
            New String() {head & "'id':1,'data':{}}", Nothing},
            New String() {head & "'ok':true,'data':{}}", Nothing},
            New String() {head & "'id':1,'ok':'yes','data':{}}", Nothing},
            New String() {head & "'id':'one','ok':true}", Nothing},
            New String() {head & "'id':1,'ok':false}", Nothing},
            New String() {head & "'id':1,'ok':false,'code':''}", Nothing},
            New String() {head & "'id':1,'ok':false,'code':7}", Nothing},
            New String() {head & "'id':1,'ok':false,'code':'" & New String("x"c, 65) & "'}", Nothing},
            New String() {"{'schemaVersion':2,'type':'refused','reason':'bad-credentials'}", Nothing},
            New String() {"{'schemaVersion':2,'type':'admin-auth-ok','token':'t','expiresSeconds':900}", Nothing},
            New String() {"{'schemaVersion':1,'type':'admin-reply','id':1,'ok':true}", Nothing},
            New String() {"{'type':'admin-reply','id':1,'ok':true}", Nothing},
            New String() {"{'schemaVersion':2,'id':1,'ok':true}", Nothing},
            New String() {"[1,2]", Nothing},
            New String() {"not json at all", Nothing},
            New String() {"{'schemaVersion':2,'type':'admin-reply','id':1,'ok':true", Nothing},
            New String() {"", Nothing},
            New String() {Nothing, Nothing}}
        For Each c As String() In cases
            Dim bytes As Byte() = If(c(0) Is Nothing, Nothing, Encoding.UTF8.GetBytes(J(c(0))))
            Dim reply As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(bytes, c(1))
            Assert.NotNull(reply)
            Assert.True(reply.Kind = ExchangeAdminWireReplyKind.Protocol, "expected Protocol for: " & If(c(0), "(nothing)") & " op " & If(c(1), "(none)"))
            Assert.NotEqual("", reply.Detail)
            Assert.False(reply.HasData)
        Next
        Assert.Equal(ExchangeAdminWireReplyKind.Protocol, ExchangeAdminWire.ReadReply(CType(Nothing, ExchangeEnvelope)).Kind)
    End Sub

    <Fact>
    Public Sub A_well_formed_reply_without_an_op_is_read_generically()
        Dim head As String = "{'schemaVersion':2,'type':'admin-reply',"
        Dim noBody As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(J(head & "'id':6,'ok':true}")))
        Assert.Equal(ExchangeAdminWireReplyKind.Done, noBody.Kind)
        Assert.Equal(6L, noBody.Id)
        Assert.False(noBody.HasData)
        Dim list As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(J(head & "'id':14,'ok':true,'data':[]}")))
        Assert.Equal(ExchangeAdminWireReplyKind.Done, list.Kind)
        Assert.True(list.HasData)
        Assert.Equal(JsonValueKind.Array, list.Data.ValueKind)
        ' an unknown code from a newer server is still a Failed result: the caller maps what it knows
        Dim newer As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(J(head & "'id':3,'ok':false,'code':'some-new-code','error':'text'}")))
        Assert.Equal(ExchangeAdminWireReplyKind.Failed, newer.Kind)
        Assert.Equal("some-new-code", newer.Code)
        ' the server's free text is not kept
        Assert.DoesNotContain("text", newer.ToString())
    End Sub

    <Fact>
    Public Sub An_admin_auth_ok_with_a_window_that_is_not_positive_or_with_no_token_is_rejected()
        Dim head As String = "{'schemaVersion':2,'type':'admin-auth-ok',"
        Dim bad As String() = {
            head & "'token':'abc','expiresSeconds':0}",
            head & "'token':'abc','expiresSeconds':-1}",
            head & "'token':'abc','expiresSeconds':-900}",
            head & "'token':'abc'}",
            head & "'expiresSeconds':900}",
            head & "'token':'','expiresSeconds':900}",
            head & "'token':'abc','expiresSeconds':'900'}",
            head & "'token':'abc','expiresSeconds':900.5}",
            head & "'token':7,'expiresSeconds':900}",
            "{'schemaVersion':2,'type':'admin-reply','id':1,'ok':true}",
            "{'schemaVersion':2,'type':'refused','reason':'bad-credentials'}",
            "{'schemaVersion':2,'type':'welcome','token':'abc','expiresSeconds':900}",
            "garbage"}
        For Each text As String In bad
            Assert.Null(ExchangeAdminWire.ReadAuthOk(Encoding.UTF8.GetBytes(J(text))))
        Next
        Assert.Null(ExchangeAdminWire.ReadAuthOk(CType(Nothing, Byte())))
        Assert.Null(ExchangeAdminWire.ReadAuthOk(CType(Nothing, ExchangeEnvelope)))

        Dim one As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(Encoding.UTF8.GetBytes(J(head & "'token':'abc','expiresSeconds':1}")))
        Assert.NotNull(one)
        Assert.Equal("abc", one.Token)
        Assert.Equal(1L, one.ExpiresSeconds)
    End Sub

    ' ---------------------------------------------------------------- secrets stay out of every printed form

    <Fact>
    Public Sub No_printed_form_of_a_wire_object_names_a_password_a_token_or_a_body()
        Dim auth As ExchangeEnvelope = ExchangeAdminWire.Auth("Sup3r-Secret-Pw".ToCharArray())
        Assert.DoesNotContain("Sup3r", auth.ToString())
        Dim request As ExchangeEnvelope = ExchangeAdminWire.AccountAdd(Token, 1, "bob", "Sup3r-Secret-Pw", "member")
        Assert.DoesNotContain("Sup3r", request.ToString())
        Assert.DoesNotContain(Token, request.ToString())

        Dim authOk As ExchangeAdminWireAuthOk = ExchangeAdminWire.ReadAuthOk(Encoding.UTF8.GetBytes(J("{'schemaVersion':2,'type':'admin-auth-ok','token':'" & Token & "','expiresSeconds':900}")))
        Assert.DoesNotContain(Token, authOk.ToString())
        Dim reply As ExchangeAdminWireReply = ExchangeAdminWire.ReadReply(Encoding.UTF8.GetBytes(J("{'schemaVersion':2,'type':'admin-reply','id':1,'ok':true,'data':{'code':'9HG46Z75'}}")))
        Assert.DoesNotContain("9HG46Z75", reply.ToString())
    End Sub

End Class
#End If
