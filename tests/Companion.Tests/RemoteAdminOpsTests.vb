Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-5 (specification 5.3, 5.5, 5.6.2 and the operation map of section 6): the typed operations of the remote
''' administration client. Each of the 18 wrappers is run through a REAL <see cref="ExchangeRemoteAdminSession"/> over the lock-step
''' stream of the `admin-ops` transcript of the local corpus (the bytes of every request are held to the corpus, the replies are the
''' corpus' own), and the view values are asserted. The rest is the rules of <c>RemoteAdminOps</c>: a reply with an unknown member still
''' reads, a body that is not what the operation says is a Protocol result (never an exception) that ends the session, a mutating
''' timeout ends the session, a refusal before anything is written writes nothing, Busy passes through, and the set of public
''' operations is the 18. The injected time is <see cref="ManualAdminTime"/> and nothing waits on a clock.
''' </summary>
Public Class RemoteAdminOpsTests

    Private Const Host As String = "exchange.test"
    Private Const Port As Integer = 22022

    Private Shared ReadOnly Carried18 As String() = {
        "status", "account.list", "account.add", "account.passwd", "account.kind", "account.remove", "account.limits",
        "device.list", "device.revoke", "device.pairing-code", "invitation.create", "invitation.list", "invitation.withdraw",
        "shared_invitation.status", "shared_invitation.set", "policy.status", "policy.set", "grant.list"}

    Private Shared ReadOnly Mutating11 As String() = {
        "account.add", "account.passwd", "account.kind", "account.remove", "account.limits", "device.revoke", "device.pairing-code",
        "invitation.create", "invitation.withdraw", "shared_invitation.set", "policy.set"}

    ' ---------------------------------------------------------------- the rig

    Private NotInheritable Class Rig
        Public ReadOnly Time As New ManualAdminTime()
        Public ReadOnly Handshake As New ScriptedAdminHandshake()
        Public ReadOnly Lines As New List(Of String)()
        Public Stream As LockStepStream
        Public Session As ExchangeRemoteAdminSession
        Public Ops As RemoteAdminOps
    End Class

    Private Shared _adminOps As ExchangeTranscriptStream

    ''' <summary>The `admin-ops` stream of the `admin-native` transcript (the pinned corpus), read once.</summary>
    Private Shared Function AdminOps() As ExchangeTranscriptStream
        If _adminOps Is Nothing Then _adminOps = ExchangeTranscriptFile.AdminNative().StreamNamed("admin-ops")
        Return _adminOps
    End Function

    ''' <summary>The corpus' own sign-in: `admin-auth` of AdminSecretPass1 and the `admin-auth-ok` that carries the token.</summary>
    Private Shared Function AuthSteps() As List(Of ExchangeTranscriptStep)
        Dim steps As List(Of ExchangeTranscriptStep) = AdminOps().Steps.Take(2).ToList()
        Assert.Equal("admin-auth", steps(0).FrameType())
        Assert.Equal("admin-auth-ok", steps(1).FrameType())
        Return steps
    End Function

    Private Shared Function AuthToken() As String
        Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(AuthSteps()(1).Json)
            Return doc.RootElement.GetProperty("token").GetString()
        End Using
    End Function

    Private Shared Function Utf8(bytes As Byte()) As String
        Return Encoding.UTF8.GetString(bytes)
    End Function

    ''' <summary>The JSON text of an envelope, as the codec writes it.</summary>
    Private Shared Function JsonOf(env As ExchangeEnvelope) As String
        Return Utf8(ExchangeEnvelopeCodec.Encode(env))
    End Function

    ''' <summary>The client frame the builder makes for the token of the corpus and this id.</summary>
    Private Shared Function Sent(env As ExchangeEnvelope) As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Expect(JsonOf(env))
    End Function

    Private Shared Function Replied(id As Integer, Optional dataJson As String = Nothing) As ExchangeTranscriptStep
        Dim json As String = "{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(Globalization.CultureInfo.InvariantCulture) & ",""ok"":true"
        If dataJson IsNot Nothing Then json &= ",""data"":" & dataJson
        Return ExchangeTranscriptStep.Supply(json & "}")
    End Function

    Private Shared Function Refused(id As Integer, code As String) As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(Globalization.CultureInfo.InvariantCulture) &
                                             ",""ok"":false,""code"":""" & code & """,""error"":""words of the server""}")
    End Function

    Private Shared Function ByeSent() As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Expect(JsonOf(New ExchangeEnvelope(ExchangeEnvelopeTypes.Bye)))
    End Function

    ''' <summary>A signed-in session over the sign-in of the corpus followed by these steps.</summary>
    Private Shared Async Function StartAsync(ParamArray tail As ExchangeTranscriptStep()) As Task(Of Rig)
        Dim steps As List(Of ExchangeTranscriptStep) = AuthSteps()
        steps.AddRange(tail)
        Return Await StartFromAsync(steps)
    End Function

    Private Shared Async Function StartFromAsync(steps As IEnumerable(Of ExchangeTranscriptStep)) As Task(Of Rig)
        Dim rig As New Rig()
        rig.Stream = New LockStepStream(steps)
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(rig.Stream)
        rig.Session = New ExchangeRemoteAdminSession(rig.Handshake, AddressOf rig.Time.Now, AddressOf rig.Time.Delay, Sub(line As String) rig.Lines.Add(line))
        Dim signIn As ExchangeRemoteAdminSignIn = Await rig.Session.SignInAsync(Host, Port, "", "", Function() "AdminSecretPass1".ToCharArray(), CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, signIn.Kind)
        rig.Ops = New RemoteAdminOps(rig.Session)
        Return rig
    End Function

    ''' <summary>Fails with the divergence when the session wrote anything the script did not expect, and (by default) when the
    ''' script was not played to its end.</summary>
    Private Shared Sub Verified(rig As Rig, Optional requireConsumed As Boolean = True)
        rig.Handshake.Verify(requireConsumed)
    End Sub

    Private Shared Sub WaitForCallInFlight(rig As Rig)
        Assert.True(SpinWait.SpinUntil(Function() rig.Time.PendingCount(ExchangeRemoteAdminSession.RequestBound) > 0, 3000), "the call never reached its 30 s bound")
    End Sub

    Private Shared Sub IsNone(Of T As Structure)(value As T?)
        Assert.False(value.HasValue)
    End Sub

    Private Shared Function Some(Of T As Structure)(value As T?) As T
        Assert.True(value.HasValue)
        Return value.Value
    End Function

    ' ---------------------------------------------------------------- the 18 wrappers against the corpus

    <Fact>
    Public Async Function Each_of_the_18_wrappers_reads_the_corpus_reply_of_the_admin_ops_stream() As Task
        Dim rig As Rig = Await StartFromAsync(AdminOps().Replayable())
        Dim ct As CancellationToken = CancellationToken.None
        Dim ops As RemoteAdminOps = rig.Ops

        Dim status As RemoteAdminResult(Of RemoteAdminStatusView) = Await ops.StatusAsync(ct)
        Assert.True(status.IsDone)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, status.Kind)
        Dim s As RemoteAdminStatusView = status.Value
        Assert.Equal(1, Some(s.Accounts))
        Assert.Equal(10, Some(s.MaxAccounts))
        Assert.Equal(0, Some(s.Devices))
        Assert.Equal(0, Some(s.Online))
        Assert.Equal(0, Some(s.Resources))
        Assert.Equal(0, Some(s.Broadcasts))
        Assert.Equal(0, Some(s.Tombstones))
        Assert.Equal(0, Some(s.Clipboards))
        Assert.Equal(0L, Some(s.ClipboardBytes))
        Assert.False(Some(s.KeepAwakeActive))
        Assert.False(Some(s.KeepAwake))
        Assert.Equal(0, Some(s.LogRetentionDays))
        Assert.False(Some(s.Sealed))

        Dim policy As RemoteAdminResult(Of RemoteAdminPolicyView) = Await ops.PolicyStatusAsync(ct)
        Assert.True(policy.IsDone)
        Assert.Equal(10, Some(policy.Value.MaxAccounts))
        Assert.Equal(1, Some(policy.Value.Accounts))
        Assert.Equal(64, Some(policy.Value.Defaults.DevicesPerAccount))
        Assert.Equal(256, Some(policy.Value.Defaults.ResourcesPerAccount))
        Assert.Equal(16, Some(policy.Value.Defaults.BroadcastsPerAccount))
        Assert.Equal(4, Some(policy.Value.Defaults.BroadcastsPerDevice))
        Assert.Equal(5368709120L, Some(policy.Value.Defaults.ClipboardMaxBytes))
        Assert.True(policy.Value.Persisted)

        Dim policySet As RemoteAdminResult(Of RemoteAdminPolicyView) = Await ops.PolicySetAsync(8, Nothing, ct)
        Assert.True(policySet.IsDone)
        Assert.Equal(8, Some(policySet.Value.MaxAccounts))
        Assert.False(policySet.Value.Persisted)

        Dim accounts As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await ops.AccountListAsync(ct)
        Assert.True(accounts.IsDone)
        Dim alice As RemoteAdminAccountRow = Assert.Single(accounts.Value)
        Assert.Equal("alice", alice.Login)
        Assert.Equal("member", alice.Kind)
        Assert.Equal(0, alice.Devices)
        Assert.Equal("2026-10-07T12:00:00Z", alice.CreatedAt)
        Assert.True(alice.Limits.IsEmpty)
        Assert.Equal(64, Some(alice.Effective.DevicesPerAccount))
        Assert.Equal(5368709120L, Some(alice.Effective.ClipboardMaxBytes))

        Dim added As RemoteAdminResult(Of RemoteAdminAccountAdded) = Await ops.AccountAddAsync("bob", "BobPassword123!", "member", ct)
        Assert.True(added.IsDone)
        Assert.Equal("bob", added.Value.Login)
        Assert.False(added.Value.OverCap)

        Dim limits As RemoteAdminResult = Await ops.AccountLimitsAsync("bob", New RemoteAdminLimits(4, Nothing, Nothing, Nothing, Nothing), ct)
        Assert.True(limits.IsDone)

        Dim passwd As RemoteAdminResult(Of RemoteAdminPasswordChanged) = Await ops.AccountPasswdAsync("bob", "NewBobPassword123!", ct)
        Assert.True(passwd.IsDone)
        Assert.Equal(0, passwd.Value.SignedOut)

        Dim kind As RemoteAdminResult(Of Boolean) = Await ops.AccountKindAsync("bob", "guest", ct)
        Assert.True(kind.IsDone)
        Assert.True(kind.Value)

        Dim created As RemoteAdminResult(Of RemoteAdminInvitationCreated) = Await ops.InvitationCreateAsync("member", Nothing, ct)
        Assert.True(created.IsDone)
        Assert.Equal("rbC4B3JZPj6OY1U7ZjJSCQ", created.Value.Code)
        Assert.Equal("a95241fb1e098c0de4d0fb7979efeff43c355fe67e329d574333bd6b96182588", created.Value.Invitation.CodeHash)
        Assert.Equal("member", created.Value.Invitation.Kind)
        Assert.False(created.Value.Invitation.Used)
        Assert.StartsWith("FMSEXCH1:", created.Value.ConnectionString)

        Dim invitations As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminInvitationRow)) = Await ops.InvitationListAsync(ct)
        Assert.True(invitations.IsDone)
        Dim inv As RemoteAdminInvitationRow = Assert.Single(invitations.Value)
        Assert.Equal(created.Value.Invitation.CodeHash, inv.CodeHash)
        Assert.Equal("2026-10-07T12:16:09Z", inv.CreatedAt)
        Assert.False(inv.Used)
        Assert.Equal("", inv.ExpectedLogin)

        Dim withdrawn As RemoteAdminResult = Await ops.InvitationWithdrawAsync(inv.CodeHash, ct)
        Assert.True(withdrawn.IsDone)

        Dim sharedStatus As RemoteAdminResult(Of RemoteAdminSharedStatus) = Await ops.SharedInvitationStatusAsync(ct)
        Assert.True(sharedStatus.IsDone)
        Assert.False(sharedStatus.Value.Enabled)
        Assert.Equal("", sharedStatus.Value.Kind)

        Dim sharedSet As RemoteAdminResult = Await ops.SharedInvitationSetAsync("SharedPassw0rdTest!", "guest", ct)
        Assert.True(sharedSet.IsDone)

        Dim devices As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)) = Await ops.DeviceListAsync("alice", ct)
        Assert.True(devices.IsDone)
        Assert.Empty(devices.Value)

        Dim pairing As RemoteAdminResult(Of RemoteAdminPairingCode) = Await ops.DevicePairingCodeAsync("alice", ct)
        Assert.True(pairing.IsDone)
        Assert.Equal("9HG46Z75", pairing.Value.Code)
        Assert.Equal("2026-10-07T12:26:09Z", pairing.Value.ExpiresAt)

        Dim revoke As RemoteAdminResult = Await ops.DeviceRevokeAsync("alice", "non-existent-device-id", ct)
        Assert.False(revoke.IsDone)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, revoke.Kind)
        Assert.Equal("not-found", revoke.Code)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, revoke.Outcome.Kind)
        Assert.Equal("not-found", revoke.Outcome.Code)

        Dim grants As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)) = Await ops.GrantListAsync("alice", Nothing, ct)
        Assert.True(grants.IsDone)
        Assert.Empty(grants.Value)

        Dim removed As RemoteAdminResult = Await ops.AccountRemoveAsync("bob", ct)
        Assert.True(removed.IsDone)

        Await rig.Session.EndAsync()
        Verified(rig)
        ' the sign-in, the 18 requests and the bye - and the session wrote no line the operations added
        Assert.Equal(20, rig.Stream.WrittenFrames().Count)
        Assert.Equal(18, rig.Lines.Where(Function(l) l.StartsWith("remote-admin: call ", StringComparison.Ordinal)).Count())
        Assert.All(rig.Lines, Sub(l) Assert.Matches("^remote-admin: (connect|trust|sign-in|call|end) \S+ \d+$", l))
        Assert.DoesNotContain(rig.Lines, Function(l) l.Contains("9HG46Z75") OrElse l.Contains("bob") OrElse l.Contains("rbC4B3JZPj6OY1U7ZjJSCQ"))
    End Function

    <Fact>
    Public Async Function A_done_result_carries_values_only_and_never_a_body() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.Status(token, 1)), Replied(1, "{""accounts"":2}"))
        Dim result As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.True(result.IsDone)
        Assert.False(result.Outcome.HasData)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Done, result.Outcome.Kind)
        Assert.Equal("", result.Outcome.Code)
        ' no public member of a result or a view holds a piece of the frame
        Dim banned As Type() = {GetType(System.Text.Json.JsonElement), GetType(System.Text.Json.JsonDocument), GetType(ExchangeEnvelope)}
        Dim types As IEnumerable(Of Type) = GetType(RemoteAdminOps).Assembly.GetTypes().Where(Function(t) t.Name.StartsWith("RemoteAdmin", StringComparison.Ordinal) AndAlso Not t.Name.StartsWith("RemoteAdminOpsTests", StringComparison.Ordinal))
        For Each t As Type In types
            For Each p As PropertyInfo In t.GetProperties(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static)
                Assert.DoesNotContain(p.PropertyType, banned)
            Next
        Next
        Verified(rig)
        rig.Session.Dispose()
    End Function

    ' ---------------------------------------------------------------- the readers

    <Fact>
    Public Async Function A_status_reply_with_unknown_members_still_reads_and_the_view_has_no_version() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.Status(token, 1)),
                                          Replied(1, "{""accounts"":3,""maxAccounts"":10,""version"":""9.9"",""futureCounter"":5,""nested"":{""a"":[1,2]}}"))
        Dim result As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.True(result.IsDone)
        Assert.Equal(3, Some(result.Value.Accounts))
        Assert.Equal(10, Some(result.Value.MaxAccounts))
        Assert.Null(GetType(RemoteAdminStatusView).GetProperty("Version"))
        Assert.Null(GetType(RemoteAdminStatusView).GetProperty("Uptime"))
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function A_status_reply_without_some_fields_gives_nothing_for_them() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.Status(token, 1)), Replied(1, "{""accounts"":3,""sealed"":true}"))
        Dim result As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.True(result.IsDone)
        Dim s As RemoteAdminStatusView = result.Value
        Assert.Equal(3, Some(s.Accounts))
        Assert.True(Some(s.Sealed))
        IsNone(s.MaxAccounts)
        IsNone(s.Devices)
        IsNone(s.Online)
        IsNone(s.Resources)
        IsNone(s.Broadcasts)
        IsNone(s.Tombstones)
        IsNone(s.Clipboards)
        IsNone(s.ClipboardBytes)
        IsNone(s.KeepAwakeActive)
        IsNone(s.KeepAwake)
        IsNone(s.LogRetentionDays)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function An_account_row_reads_its_own_limits_and_effective_ones_and_ignores_unknown_members() As Task
        Dim token As String = AuthToken()
        Dim body As String = "[{""login"":""carol"",""createdAt"":""2026-10-08T00:00:00Z"",""devices"":2,""futureMember"":true," &
                             """limits"":{""devicesPerAccount"":4,""resourcesPerAccount"":5,""broadcastsPerAccount"":6,""broadcastsPerDevice"":2,""clipboardMaxBytes"":5368709121,""extra"":1}," &
                             """effective"":{""devices"":4,""resources"":5,""broadcasts"":6,""broadcastsPerDevice"":2,""clipboardBytes"":5368709121,""extra"":1}}," &
                             "{""login"":""dave"",""devices"":0,""kind"":""guest"",""effective"":{}}]"
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1, body))
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await rig.Ops.AccountListAsync(CancellationToken.None)
        Assert.True(result.IsDone)
        Assert.Equal(2, result.Value.Count)
        Dim carol As RemoteAdminAccountRow = result.Value(0)
        Assert.Equal("carol", carol.Login)
        Assert.Equal("member", carol.Kind)
        Assert.Equal(2, carol.Devices)
        Assert.Equal(4, Some(carol.Limits.DevicesPerAccount))
        Assert.Equal(5, Some(carol.Limits.ResourcesPerAccount))
        Assert.Equal(6, Some(carol.Limits.BroadcastsPerAccount))
        Assert.Equal(2, Some(carol.Limits.BroadcastsPerDevice))
        Assert.Equal(5368709121L, Some(carol.Limits.ClipboardMaxBytes))
        Assert.Equal(5368709121L, Some(carol.Effective.ClipboardMaxBytes))
        Dim dave As RemoteAdminAccountRow = result.Value(1)
        Assert.Equal("guest", dave.Kind)
        Assert.True(dave.Limits.IsEmpty)
        Assert.True(dave.Effective.IsEmpty)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function A_device_row_reads_roles_and_the_signed_out_mark_and_a_grant_row_reads_its_strings() As Task
        Dim token As String = AuthToken()
        Dim devicesBody As String = "[{""deviceId"":""d1"",""deviceName"":""Phone"",""product"":""fms-android"",""productVersion"":""1.2"",""platform"":""android"",""roles"":[""receiver"",""clipboard"",7]," &
                                    """enrolledAt"":""2026-10-01T00:00:00Z"",""lastSeen"":""2026-10-02T00:00:00Z"",""online"":true,""signedOut"":true,""more"":1}," &
                                    "{""deviceId"":""d2"",""deviceName"":""PC"",""product"":""fms-windows"",""enrolledAt"":""2026-10-01T00:00:00Z"",""online"":false}]"
        Dim grantsBody As String = "[{""grantId"":""g1"",""targetId"":""r1"",""kind"":""resource"",""guest"":""visitor"",""grantedBy"":""alice"",""grantedAt"":""2026-10-03T00:00:00Z""}," &
                                   "{""targetId"":""b1"",""kind"":""broadcast"",""guest"":""visitor"",""grantedBy"":""alice"",""grantedAt"":""2026-10-04T00:00:00Z""}]"
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.DeviceList(token, 1, "alice")), Replied(1, devicesBody),
                                          Sent(ExchangeAdminWire.GrantList(token, 2, "", "visitor")), Replied(2, grantsBody))
        Dim devices As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)) = Await rig.Ops.DeviceListAsync("alice", CancellationToken.None)
        Assert.True(devices.IsDone)
        Dim d1 As RemoteAdminDeviceRow = devices.Value(0)
        Assert.Equal("d1", d1.DeviceId)
        Assert.Equal("Phone", d1.DeviceName)
        Assert.Equal("fms-android", d1.Product)
        Assert.Equal("1.2", d1.ProductVersion)
        Assert.Equal("android", d1.Platform)
        Assert.Equal(New String() {"receiver", "clipboard"}, d1.Roles.ToArray())
        Assert.Equal("2026-10-02T00:00:00Z", d1.LastSeen)
        Assert.True(d1.Online)
        Assert.True(d1.SignedOut)
        Dim d2 As RemoteAdminDeviceRow = devices.Value(1)
        Assert.Equal("", d2.ProductVersion)
        Assert.Equal("", d2.LastSeen)
        Assert.Empty(d2.Roles)
        Assert.False(d2.Online)
        Assert.False(d2.SignedOut)

        Dim grants As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)) = Await rig.Ops.GrantListAsync(Nothing, "visitor", CancellationToken.None)
        Assert.True(grants.IsDone)
        Assert.Equal(2, grants.Value.Count)
        Assert.Equal("g1", grants.Value(0).GrantId)
        Assert.Equal("resource", grants.Value(0).Kind)
        Assert.Equal("visitor", grants.Value(0).Guest)
        Assert.Equal("alice", grants.Value(0).GrantedBy)
        Assert.Equal("", grants.Value(1).GrantId)
        Assert.Equal("broadcast", grants.Value(1).Kind)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function Account_add_shows_the_login_the_server_normalised_and_the_over_cap_mark() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountAdd(token, 1, "  Bob ", "BobPassword123!", "")), Replied(1, "{""login"":""bob"",""overCap"":true}"))
        Dim result As RemoteAdminResult(Of RemoteAdminAccountAdded) = Await rig.Ops.AccountAddAsync("  Bob ", "BobPassword123!", "", CancellationToken.None)
        Assert.True(result.IsDone)
        Assert.Equal("bob", result.Value.Login)
        Assert.True(result.Value.OverCap)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function Invitation_create_with_an_empty_connection_string_still_hands_over_the_code() As Task
        Dim token As String = AuthToken()
        Dim invitation As String = """invitation"":{""codeHash"":""abc123"",""kind"":""member"",""createdAt"":""2026-10-09T00:00:00Z"",""expectedLogin"":""carol"",""used"":false}"
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.InvitationCreate(token, 1, "member", "carol")), Replied(1, "{""code"":""CODE-ONE""," & invitation & ",""connectionString"":""""}"),
                                          Sent(ExchangeAdminWire.InvitationCreate(token, 2, "guest")), Replied(2, "{""code"":""CODE-TWO"",""invitation"":7,""connectionString"":12}"))
        Dim first As RemoteAdminResult(Of RemoteAdminInvitationCreated) = Await rig.Ops.InvitationCreateAsync("member", "carol", CancellationToken.None)
        Assert.True(first.IsDone)
        Assert.Equal("CODE-ONE", first.Value.Code)
        Assert.Equal("", first.Value.ConnectionString)
        Assert.Equal("carol", first.Value.Invitation.ExpectedLogin)
        Assert.Equal("abc123", first.Value.Invitation.CodeHash)
        ' the code is the point of the answer: what is around it is read leniently
        Dim second As RemoteAdminResult(Of RemoteAdminInvitationCreated) = Await rig.Ops.InvitationCreateAsync("guest", Nothing, CancellationToken.None)
        Assert.True(second.IsDone)
        Assert.Equal("CODE-TWO", second.Value.Code)
        Assert.Null(second.Value.Invitation)
        Assert.Equal("", second.Value.ConnectionString)
        ' the secrets are not in the text a result or a view prints
        Assert.DoesNotContain("CODE-ONE", first.ToString())
        Assert.DoesNotContain("CODE-ONE", first.Value.ToString())
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function Policy_set_sends_the_cap_only_when_given_and_the_limits_it_gets() As Task
        Dim token As String = AuthToken()
        Dim onlyLimits As New RemoteAdminLimits(3, Nothing, Nothing, Nothing, Nothing)
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.PolicySet(token, 1, Nothing, onlyLimits.ToWire())), Replied(1, "{""maxAccounts"":10,""accounts"":1,""defaults"":{""devices"":3},""persisted"":true}"))
        Dim result As RemoteAdminResult(Of RemoteAdminPolicyView) = Await rig.Ops.PolicySetAsync(Nothing, onlyLimits, CancellationToken.None)
        Assert.True(result.IsDone)
        Dim frame As String = rig.Stream.WrittenFrames()(1)
        Assert.DoesNotContain("maxAccounts", frame)
        Assert.Contains("""limits"":{""devicesPerAccount"":3}", frame)
        Assert.Equal(3, Some(result.Value.Defaults.DevicesPerAccount))
        IsNone(result.Value.Defaults.ResourcesPerAccount)
        Assert.True(result.Value.Persisted)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function Account_limits_sends_the_whole_set_and_an_empty_set_sends_no_limits_member() As Task
        Dim token As String = AuthToken()
        Dim whole As New RemoteAdminLimits(4, 5, 6, 2, 123456789012L)
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountLimits(token, 1, "bob", whole.ToWire())), Replied(1),
                                          Sent(ExchangeAdminWire.AccountLimits(token, 2, "bob", Nothing)), Replied(2))
        Dim first As RemoteAdminResult = Await rig.Ops.AccountLimitsAsync("bob", whole, CancellationToken.None)
        Assert.True(first.IsDone)
        Dim written As List(Of String) = rig.Stream.WrittenFrames()
        Assert.Contains("""limits"":{""devicesPerAccount"":4,""resourcesPerAccount"":5,""broadcastsPerAccount"":6,""broadcastsPerDevice"":2,""clipboardMaxBytes"":123456789012}", written(1))
        Dim second As RemoteAdminResult = Await rig.Ops.AccountLimitsAsync("bob", Nothing, CancellationToken.None)
        Assert.True(second.IsDone)
        Assert.DoesNotContain("""limits"":", rig.Stream.WrittenFrames()(2))
        Verified(rig)
        rig.Session.Dispose()
    End Function

    <Fact>
    Public Async Function Shared_invitation_off_sends_the_empty_password_and_the_on_form_never_does() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.SharedInvitationSet(token, 1, "", "member")), Replied(1))
        Dim off As RemoteAdminResult = Await rig.Ops.SharedInvitationOffAsync("member", CancellationToken.None)
        Assert.True(off.IsDone)
        Assert.Equal(2, rig.Stream.WrittenFrames().Count)
        Dim onEmpty As RemoteAdminResult = Await rig.Ops.SharedInvitationSetAsync("", "member", CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, onEmpty.Kind)
        Assert.Equal("weak-password", onEmpty.Code)
        Dim onNothing As RemoteAdminResult = Await rig.Ops.SharedInvitationSetAsync(Nothing, "member", CancellationToken.None)
        Assert.Equal("weak-password", onNothing.Code)
        Assert.Equal(2, rig.Stream.WrittenFrames().Count)
        Verified(rig)
        rig.Session.Dispose()
    End Function

    ' ---------------------------------------------------------------- refusals before anything is written

    <Fact>
    Public Async Function A_refusal_before_writing_is_a_failed_result_with_the_code_and_writes_nothing() As Task
        Dim rig As Rig = Await StartAsync()
        Dim ct As CancellationToken = CancellationToken.None
        Dim writtenAtStart As Integer = rig.Stream.WrittenFrames().Count

        Dim zero As RemoteAdminResult(Of RemoteAdminPolicyView) = Await rig.Ops.PolicySetAsync(0, Nothing, ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, zero.Kind)
        Assert.Equal("bad-limit", zero.Code)
        Assert.False(zero.IsDone)
        Assert.Null(zero.Value)
        Assert.Equal("bad-limit", (Await rig.Ops.PolicySetAsync(-5, Nothing, ct)).Code)
        Assert.Equal("bad-limit", (Await rig.Ops.PolicySetAsync(Nothing, New RemoteAdminLimits(-1, Nothing, Nothing, Nothing, Nothing), ct)).Code)
        Assert.Equal("bad-limit", (Await rig.Ops.PolicySetAsync(5, New RemoteAdminLimits(Nothing, Nothing, Nothing, Nothing, -1L), ct)).Code)

        Dim negative As RemoteAdminResult = Await rig.Ops.AccountLimitsAsync("bob", New RemoteAdminLimits(4, Nothing, Nothing, -1, Nothing), ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, negative.Kind)
        Assert.Equal("bad-limit", negative.Code)
        Assert.Equal("bad-limit", (Await rig.Ops.AccountLimitsAsync("bob", New RemoteAdminLimits(Nothing, Nothing, Nothing, Nothing, -1L), ct)).Code)

        Dim emptyKind As RemoteAdminResult(Of Boolean) = Await rig.Ops.AccountKindAsync("bob", "", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, emptyKind.Kind)
        Assert.Equal("bad-kind", emptyKind.Code)
        Assert.Equal("bad-kind", (Await rig.Ops.AccountKindAsync("bob", Nothing, ct)).Code)

        Assert.Equal("weak-password", (Await rig.Ops.SharedInvitationSetAsync("", "guest", ct)).Code)

        Dim both As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)) = Await rig.Ops.GrantListAsync("alice", "visitor", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, both.Kind)
        Assert.Equal("bad-request", both.Code)

        ' a zero limit is a legal limit and a cap of 1 is the lowest cap: neither is refused here (the next two calls would write)
        Assert.False(New RemoteAdminLimits(0, 0, 0, 0, 0L).HasNegative)
        Assert.Equal(writtenAtStart, rig.Stream.WrittenFrames().Count)
        Assert.True(rig.Session.IsLive, "a refusal before writing never touches the session")
        Verified(rig)
        rig.Session.Dispose()
    End Function

    ' ---------------------------------------------------------------- a body that is not what the operation says

    <Fact>
    Public Async Function A_wrong_body_kind_is_a_protocol_result_for_status_account_list_and_device_list_and_the_session_ended_itself() As Task
        Dim token As String = AuthToken()
        Dim ct As CancellationToken = CancellationToken.None

        Dim a As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1, "{}"))
        Dim accounts As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await a.Ops.AccountListAsync(ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, accounts.Kind)
        Assert.False(accounts.IsDone)
        Assert.Null(accounts.Value)
        Assert.False(a.Session.IsLive)
        Verified(a)    ' no `bye` was written to a session that ended itself

        Dim b As Rig = Await StartAsync(Sent(ExchangeAdminWire.Status(token, 1)), Replied(1, "[]"))
        Dim status As RemoteAdminResult(Of RemoteAdminStatusView) = Await b.Ops.StatusAsync(ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, status.Kind)
        Assert.False(b.Session.IsLive)
        Verified(b)

        Dim c As Rig = Await StartAsync(Sent(ExchangeAdminWire.DeviceList(token, 1, "alice")), Replied(1, "{}"))
        Dim devices As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)) = Await c.Ops.DeviceListAsync("alice", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, devices.Kind)
        Assert.False(c.Session.IsLive)
        Verified(c)

        ' a reply with no data member at all, for an operation that answers with data
        Dim d As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1))
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, (Await d.Ops.AccountListAsync(ct)).Kind)
        Assert.False(d.Session.IsLive)
        Verified(d)
    End Function

    <Fact>
    Public Async Function A_body_of_the_right_kind_with_a_wrong_member_is_a_protocol_result_that_ends_the_session_politely() As Task
        Dim token As String = AuthToken()
        Dim ct As CancellationToken = CancellationToken.None
        ' every case: the request, a reply the wire layer accepts and this reader does not, then the `bye` of the session that ends
        Dim notAnObject As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1, "[1]"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim r1 As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await notAnObject.Ops.AccountListAsync(ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, r1.Kind)
        Assert.Equal("not-object", r1.Detail)
        Assert.False(notAnObject.Session.IsLive)
        Verified(notAnObject)

        ' the number does not fit 32 bits: what ExchangeAdminParser.Accounts turns into a FormatException is a Protocol result here
        Dim tooBig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1, "[{""login"":""x"",""devices"":3000000000}]"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim r2 As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await tooBig.Ops.AccountListAsync(ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, r2.Kind)
        Assert.Equal("kind-devices", r2.Detail)
        Assert.False(tooBig.Session.IsLive)
        Verified(tooBig)

        Dim limitsOutOfRange As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), Replied(1, "[{""login"":""x"",""limits"":{""devicesPerAccount"":99999999999}}]"), ByeSent(), ExchangeTranscriptStep.Closed())
        Assert.Equal("kind-devicesPerAccount", (Await limitsOutOfRange.Ops.AccountListAsync(ct)).Detail)
        Verified(limitsOutOfRange)

        Dim wrongKind As Rig = Await StartAsync(Sent(ExchangeAdminWire.Status(token, 1)), Replied(1, "{""accounts"":""many""}"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim r3 As RemoteAdminResult(Of RemoteAdminStatusView) = Await wrongKind.Ops.StatusAsync(ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, r3.Kind)
        Assert.Equal("kind-accounts", r3.Detail)
        Verified(wrongKind)

        Dim noId As Rig = Await StartAsync(Sent(ExchangeAdminWire.DeviceList(token, 1, "alice")), Replied(1, "[{""deviceName"":""x""}]"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim r4 As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)) = Await noId.Ops.DeviceListAsync("alice", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, r4.Kind)
        Assert.Equal("missing-deviceId", r4.Detail)
        Verified(noId)

        ' the detail is a fixed word, never a piece of the reply
        Assert.DoesNotContain("many", r3.Detail)
    End Function

    <Fact>
    Public Async Function A_mutating_protocol_result_ends_the_session_and_the_next_call_writes_nothing() As Task
        Dim token As String = AuthToken()
        Dim ct As CancellationToken = CancellationToken.None

        ' the body of the right kind, a member of the wrong kind: this class ends the session with a `bye`
        Dim parse As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountPasswd(token, 1, "bob", "NewBobPassword123!")), Replied(1, "{""signedOut"":""x""}"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim passwd As RemoteAdminResult(Of RemoteAdminPasswordChanged) = Await parse.Ops.AccountPasswdAsync("bob", "NewBobPassword123!", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, passwd.Kind)
        Assert.True(RemoteAdminOps.IsMutating("account.passwd"))
        Assert.False(parse.Session.IsLive)
        Assert.True(parse.Stream.ClientClosed)
        Dim after As RemoteAdminResult = Await parse.Ops.AccountRemoveAsync("bob", ct)
        Assert.NotEqual(ExchangeRemoteAdminOutcomeKind.Done, after.Kind)
        Assert.NotEqual(ExchangeRemoteAdminOutcomeKind.Busy, after.Kind)
        Verified(parse)    ' nothing was written after the bye

        ' the wire layer already refused the body: the session ended itself, and no second `bye` follows
        Dim wire As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountAdd(token, 1, "bob", "BobPassword123!", "member")), Replied(1, "[]"))
        Dim added As RemoteAdminResult(Of RemoteAdminAccountAdded) = Await wire.Ops.AccountAddAsync("bob", "BobPassword123!", "member", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, added.Kind)
        Assert.False(wire.Session.IsLive)
        Verified(wire)

        ' no login in the reply of account.add: the request may have succeeded and the typed login is not what the server holds
        Dim noLogin As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountAdd(token, 1, "bob", "BobPassword123!", "member")), Replied(1, "{""overCap"":false}"), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim r As RemoteAdminResult(Of RemoteAdminAccountAdded) = Await noLogin.Ops.AccountAddAsync("bob", "BobPassword123!", "member", ct)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Protocol, r.Kind)
        Assert.Equal("missing-login", r.Detail)
        Assert.False(noLogin.Session.IsLive)
        Verified(noLogin)
    End Function

    ' ---------------------------------------------------------------- the clock, Busy, Lapsed

    <Fact>
    Public Async Function A_mutating_timeout_ends_the_session_through_the_injected_time() As Task
        Dim token As String = AuthToken()
        ' the server stays silent after the request
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountRemove(token, 1, "bob")))
        Dim pending As Task(Of RemoteAdminResult) = rig.Ops.AccountRemoveAsync("bob", CancellationToken.None)
        WaitForCallInFlight(rig)
        Assert.Contains(ExchangeRemoteAdminSession.RequestBound, rig.Time.Spans())
        Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.RequestBound))
        Dim result As RemoteAdminResult = Await pending
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, result.Kind)
        Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Reason)
        Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Outcome.Reason)
        Assert.False(rig.Session.IsLive)
        Assert.True(rig.Stream.ClientClosed)
        Dim next1 As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, next1.Kind)
        Verified(rig, requireConsumed:=False)    ' the next call wrote nothing
    End Function

    <Fact>
    Public Async Function A_reading_timeout_is_unreachable_and_leaves_the_session_as_the_session_decided() As Task
        Dim token As String = AuthToken()
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)))
        Dim pending As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow))) = rig.Ops.AccountListAsync(CancellationToken.None)
        WaitForCallInFlight(rig)
        rig.Time.Fire(ExchangeRemoteAdminSession.RequestBound)
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await pending
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, result.Kind)
        Assert.Equal(ExchangeRemoteAdminUnreachableReason.Timeout, result.Reason)
        Assert.Null(result.Value)
        Assert.False(RemoteAdminOps.IsMutating("account.list"))
        Verified(rig, requireConsumed:=False)
    End Function

    <Fact>
    Public Async Function Busy_passes_through_and_does_not_end_the_session() As Task
        Dim token As String = AuthToken()
        ' the first call waits for an answer that never comes; the second must not write
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountList(token, 1)), ByeSent(), ExchangeTranscriptStep.Closed())
        Dim first As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow))) = rig.Ops.AccountListAsync(CancellationToken.None)
        WaitForCallInFlight(rig)
        Dim readBusy As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Busy, readBusy.Kind)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Busy, readBusy.Outcome.Kind)
        Dim writeBusy As RemoteAdminResult = Await rig.Ops.AccountRemoveAsync("bob", CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Busy, writeBusy.Kind)
        Assert.True(rig.Session.IsLive, "a Busy result of a mutating operation must not end the session")
        Await rig.Session.EndAsync()    ' cancels the first call and says bye
        Dim firstResult As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) = Await first
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Unreachable, firstResult.Kind)
        Assert.Equal(ExchangeRemoteAdminUnreachableReason.Cancelled, firstResult.Reason)
        Verified(rig)
    End Function

    <Fact>
    Public Async Function A_lapsed_session_is_a_lapsed_result_and_needs_nothing_more() As Task
        Dim token As String = AuthToken()
        ' the server closes the stream in answer to the request: a lapse
        Dim rig As Rig = Await StartAsync(Sent(ExchangeAdminWire.AccountRemove(token, 1, "bob")), ExchangeTranscriptStep.Closed())
        Dim result As RemoteAdminResult = Await rig.Ops.AccountRemoveAsync("bob", CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, result.Kind)
        Assert.False(rig.Session.IsLive)
        Dim again As RemoteAdminResult(Of RemoteAdminStatusView) = Await rig.Ops.StatusAsync(CancellationToken.None)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, again.Kind)
        Verified(rig)
    End Function

    ' ---------------------------------------------------------------- the shape of the class

    <Fact>
    Public Sub The_public_async_methods_map_to_exactly_the_18_carried_operations()
        Dim methods As List(Of MethodInfo) = GetType(RemoteAdminOps).GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly).ToList()
        Dim asyncMethods As List(Of MethodInfo) = methods.Where(Function(m) m.Name.EndsWith("Async", StringComparison.Ordinal)).ToList()
        ' nothing public and instance but the operations
        Assert.Equal(asyncMethods.Count, methods.Count)
        Dim ops As New List(Of String)()
        For Each m As MethodInfo In asyncMethods
            Dim attr As RemoteAdminOpAttribute = m.GetCustomAttribute(Of RemoteAdminOpAttribute)()
            Assert.True(attr IsNot Nothing, m.Name & " names no carried operation")
            Assert.Equal(GetType(CancellationToken), m.GetParameters().Last().ParameterType)
            ops.Add(attr.Op)
        Next
        Assert.Equal(Carried18.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), ops.Distinct(StringComparer.Ordinal).OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
        ' the one operation with two methods is the shared invitation: "on" and "off"
        Dim twice As List(Of String) = ops.GroupBy(Function(x) x, StringComparer.Ordinal).Where(Function(g) g.Count() > 1).Select(Function(g) g.Key).ToList()
        Assert.Equal(New String() {"shared_invitation.set"}, twice.ToArray())
        Assert.Equal(19, asyncMethods.Count)
        ' no method for an operation the server does not carry
        Dim uncarried As String() = {"host.settings", "grant.add", "grant.remove", "broadcast.list", "broadcast.end", "resource.list", "resource.unpublish", "clipboard.info", "clipboard.erase", "admin.audit.list"}
        Assert.Empty(ops.Intersect(uncarried))
        ' every attribute value is a name the wire layer builds
        Assert.All(ops, Sub(op) Assert.True(ExchangeAdminWire.IsCarried(op), op))
    End Sub

    <Fact>
    Public Sub The_mutating_set_is_the_eleven_of_the_operation_map()
        Dim found As String() = ExchangeAdminWire.CarriedOps.Where(Function(op) RemoteAdminOps.IsMutating(op)).OrderBy(Function(x) x, StringComparer.Ordinal).ToArray()
        Assert.Equal(Mutating11.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray(), found)
        Assert.Equal(11, found.Length)
        For Each op As String In Carried18.Except(Mutating11)
            Assert.False(RemoteAdminOps.IsMutating(op), op)
        Next
        Assert.False(RemoteAdminOps.IsMutating("host.settings"))
        Assert.False(RemoteAdminOps.IsMutating("Account.Add"))
        Assert.False(RemoteAdminOps.IsMutating(""))
        Assert.False(RemoteAdminOps.IsMutating(Nothing))
    End Sub

    <Fact>
    Public Sub The_ops_class_has_no_password_generator_and_names_no_user_word()
        Dim members As List(Of String) = GetType(RemoteAdminOps).GetMembers(BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly).Select(Function(m) m.Name).ToList()
        Assert.DoesNotContain(members, Function(n) n.IndexOf("Generate", StringComparison.OrdinalIgnoreCase) >= 0 OrElse n.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0)
        Dim source As String = System.IO.File.ReadAllText(System.IO.Path.Combine(ExchangeTestSupport.CompanionDir(), "Core", "RemoteAdmin", "RemoteAdminOps.vb"))
        Dim code As String = ExchangeTestSupport.CodeOnly(source)
        For Each banned As String In New String() {"Localization.", "MessageBox", "Registry", "File.", "RandomNumberGenerator", "Rand(", "Logger.", "Log."}
            Assert.False(code.Contains(banned), "RemoteAdminOps names " & banned)
        Next
        ' no reply body ever sits in a log line: this class writes none
        Assert.False(Regex.IsMatch(code, "\b(Debug|Trace|Console)\."))
    End Sub

End Class
