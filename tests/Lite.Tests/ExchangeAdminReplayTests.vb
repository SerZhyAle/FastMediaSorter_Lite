#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit
Imports K = fmsl.ExchangeAdminKit
Imports Tx = fmsl.ExchangeTranscriptStep

' ----------------------------------------------------------------------------------------------------------------------
' L0278 T-3 slice B, specification 7.2: the eight `admin-native` streams of the local frames-v2.json replayed through the REAL
' ExchangeRemoteAdminSession over a ScriptedAdminHandshake and a LockStepStream, direction-aware (the client frames are
' EXPECTATIONS, the server frames are supplied), and the same script run against the `admin-websocket` copy.
'
' Token discipline: the session builds every request from the token of the server frame it read, so a request that carried a
' constant would be a byte mismatch on the websocket copy (whose tokens differ); a fact below shows that the harness catches
' exactly that. The `http` openings of the websocket copy are not frames and are not replayed (Replayable()).
' ----------------------------------------------------------------------------------------------------------------------

''' <summary>A fact that is skipped, with the reason, when the adopted corpus folder of the contract catalog is not at hand
''' (the catalog root comes from FMS_CONTRACTS_ROOT, process first and then the user's value; it is not required to exist).</summary>
Friend NotInheritable Class AdoptedCorpusFactAttribute
    Inherits FactAttribute

    ''' <summary>The adopted completion of the corpus, below the catalog root (catalog 1.11, `fmsw-245-completion`).</summary>
    Friend Const RelativePath As String = "device-exchange\vectors\fmsw-245-completion\frames-v2.json"

    Public Sub New()
        If AdoptedPath() Is Nothing Then
            Skip = "the adopted corpus folder is not present (set FMS_CONTRACTS_ROOT to the catalog root to compare); not a pass"
        End If
    End Sub

    ''' <summary>The adopted file, or Nothing when the root is not set or the file is not there. Read only.</summary>
    Friend Shared Function AdoptedPath() As String
        Dim root As String = Environment.GetEnvironmentVariable("FMS_CONTRACTS_ROOT")
        If String.IsNullOrWhiteSpace(root) Then root = Environment.GetEnvironmentVariable("FMS_CONTRACTS_ROOT", EnvironmentVariableTarget.User)
        If String.IsNullOrWhiteSpace(root) Then Return Nothing
        Dim path As String = System.IO.Path.Combine(root, RelativePath)
        Return If(File.Exists(path), path, Nothing)
    End Function

End Class

Public Class ExchangeAdminReplayTests

    ''' <summary>The pinned file, read once for the class (a failed pin throws the same failure for every replay).</summary>
    Private Shared ReadOnly Corpus As New Lazy(Of ExchangeTranscriptFile)(Function() ExchangeTranscriptFile.Load())

    Private Const Native As String = "admin-native"
    Private Const Websocket As String = "admin-websocket"

    Private NotInheritable Class Holder
        Public Value As String
    End Class

    ''' <summary>One replay of one corpus stream: a rig over the lock-step stream, the corpus password, the result of the sign-in.</summary>
    Private NotInheritable Class Replay
        Implements IDisposable

        Public ReadOnly Rig As ExchangeAdminRig
        Public ReadOnly Lock As LockStepStream
        Public ReadOnly Stream As ExchangeTranscriptStream
        Public ReadOnly Password As Char()
        Public ReadOnly Label As String
        Public SignIn As ExchangeRemoteAdminSignIn

        Private Sub New(transcript As String, streamName As String)
            Label = streamName & " (" & transcript & ")"
            Stream = Corpus.Value.Transcript(transcript).StreamNamed(streamName)
            Lock = Stream.LockStep()
            Rig = New ExchangeAdminRig()
            Assert.Equal("admin-auth", Stream.ClientSteps()(0).FrameType())
            Using doc As JsonDocument = JsonDocument.Parse(Stream.ClientSteps()(0).Json)
                Password = doc.RootElement.GetProperty("password").GetString().ToCharArray()
            End Using
        End Sub

        ''' <summary>Replays the stream as far as the sign-in; the password comes from the corpus' own admin-auth frame.</summary>
        Public Shared Async Function OpenAsync(transcript As String, streamName As String) As Task(Of Replay)
            Dim r As New Replay(transcript, streamName)
            r.Rig.Plain(r.Lock)
            r.SignIn = Await K.Within(r.Rig.SignInAsync(r.Password), "the sign-in of " & r.Label)
            Assert.True(K.IsAllZero(r.Password), "the password array of " & r.Label & " was not wiped after the sign-in")
            Return r
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Rig.Dispose()
            Lock.Dispose()
        End Sub

    End Class

    ' ================================================================ helpers

    Private Shared Function Text(el As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return Nothing
    End Function

    ''' <summary>Semantic equality of two JSON values: objects by member name (order does not matter), numbers by their text.</summary>
    Private Shared Function JsonEqual(a As JsonElement, b As JsonElement) As Boolean
        If a.ValueKind <> b.ValueKind Then Return False
        Select Case a.ValueKind
            Case JsonValueKind.Object
                Dim countA As Integer = 0
                For Each p As JsonProperty In a.EnumerateObject()
                    countA += 1
                    Dim other As JsonElement = Nothing
                    If Not b.TryGetProperty(p.Name, other) OrElse Not JsonEqual(p.Value, other) Then Return False
                Next
                Dim countB As Integer = 0
                For Each p As JsonProperty In b.EnumerateObject()
                    countB += 1
                Next
                Return countA = countB
            Case JsonValueKind.Array
                Dim left As List(Of JsonElement) = a.EnumerateArray().ToList()
                Dim right As List(Of JsonElement) = b.EnumerateArray().ToList()
                If left.Count <> right.Count Then Return False
                For i As Integer = 0 To left.Count - 1
                    If Not JsonEqual(left(i), right(i)) Then Return False
                Next
                Return True
            Case JsonValueKind.String
                Return String.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal)
            Case JsonValueKind.Number
                Return String.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal)
            Case Else
                Return True    ' true, false, null: the kind is the value
        End Select
    End Function

    ''' <summary>The client wrote exactly the corpus' client frames, in order, and the script ran to its end.</summary>
    Private Shared Sub AssertClientKeptToTheCorpus(r As Replay)
        r.Lock.Verify()
        Assert.Equal(r.Stream.ClientSteps().Select(Function(s) s.Json).ToList(), r.Lock.WrittenFrames())
        r.Rig.Scripted.Verify()
    End Sub

    ' ================================================================ the single-answer streams

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_disabled_writes_exactly_one_admin_auth_then_Refused_AdminDisabled_and_nothing_more(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-disabled")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Disabled, r.SignIn.Kind)
            Assert.Equal(ExchangeRefusal.AdminDisabled, r.SignIn.Refusal)
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            Assert.Equal("admin-auth", K.TypeOfFrame(r.Lock.WrittenFrames()(0)))
            Assert.Equal(1, r.Rig.AuthFrameCount())
            Assert.False(r.Rig.Session.IsLive)
            Assert.True(r.Lock.ClientClosed)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_excluded_is_the_unanswered_outcome_and_reads_the_same_as_a_server_that_just_closes(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-excluded")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Unreachable, r.SignIn.Kind)
            Assert.Equal(ExchangeRemoteAdminUnreachableReason.Unanswered, r.SignIn.Reason)
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            Assert.False(r.Rig.Session.IsLive)
            Assert.True(r.Lock.ClientClosed)
            AssertClientKeptToTheCorpus(r)
            ' 3.1: "off", "address not allowed" and "an older server" are one outcome - a server that closes with no frame
            Using older As New ExchangeAdminRig()
                Dim silent As New LockStepStream(Tx.Expect(K.AuthJson(K.SecretPassword)), Tx.Closed())
                older.Plain(silent)
                Dim other As ExchangeRemoteAdminSignIn = Await K.Within(older.SignInAsync(K.Password()), "the sign-in to a server that closes")
                Assert.Equal(other.Kind, r.SignIn.Kind)
                Assert.Equal(other.Reason, r.SignIn.Reason)
                Assert.Equal(other.Detail, r.SignIn.Detail)
            End Using
        End Using
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_wrong_is_Refused_BadCredentials(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-wrong")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Refused, r.SignIn.Kind)
            Assert.Equal(ExchangeRefusal.BadCredentials, r.SignIn.Refusal)
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            Assert.False(r.Rig.Session.IsLive)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_waiting_slot_is_Refused_BadCredentials_and_its_slot_wait_is_a_timing_fact_the_transcript_cannot_carry(transcript As String) As Task
        ' The transcript says the second check "waits for its slot, then is evaluated"; it carries no clock, so what is proved is
        ' the outcome and that the whole wait sits inside the one 20 s window the session arms for the sign-in reply.
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-waiting-slot")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Refused, r.SignIn.Kind)
            Assert.Equal(ExchangeRefusal.BadCredentials, r.SignIn.Refusal)
            Assert.Contains(ExchangeRemoteAdminSession.SignInBound, r.Rig.Time.Spans())
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_rate_limited_is_Refused_RateLimited(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-rate-limited")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Refused, r.SignIn.Kind)
            Assert.Equal(ExchangeRefusal.RateLimited, r.SignIn.Refusal)
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_capacity_is_Refused_Capacity(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-capacity")
            Assert.Equal(ExchangeRemoteAdminSignInKind.Refused, r.SignIn.Kind)
            Assert.Equal(ExchangeRefusal.Capacity, r.SignIn.Refusal)
            Assert.Equal(1, r.Lock.WrittenFrames().Count)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    ' ================================================================ the lapse

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_lapsed_signs_in_asks_status_with_id_1_and_is_Lapsed_with_no_frame_written_after_the_refusal(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-lapsed")
            Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, r.SignIn.Kind)
            Assert.True(r.Rig.Session.IsLive)
            Using outcome As ExchangeRemoteAdminOutcome = Await r.Rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, outcome.Kind)
                Assert.Equal("refused-bad-credentials", outcome.Detail)
            End Using
            Assert.False(r.Rig.Session.IsLive)
            Assert.True(r.Lock.ClientClosed)
            Dim framesAfterTheRefusal As Integer = r.Lock.Writes().Count
            ' no bye to a closed stream and no second admin-auth, however the session is asked afterwards
            Await K.WithinTask(r.Rig.Session.EndAsync(), "the end of a lapsed session")
            Using again As ExchangeRemoteAdminOutcome = Await r.Rig.CallOpAsync("status", K.StatusMembers)
                Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, again.Kind)
            End Using
            Assert.Equal(framesAfterTheRefusal, r.Lock.Writes().Count)
            Assert.Equal(2, r.Lock.WrittenFrames().Count)
            Assert.Equal("admin-auth", K.TypeOfFrame(r.Lock.WrittenFrames()(0)))
            Assert.Equal("admin-request", K.TypeOfFrame(r.Lock.WrittenFrames()(1)))
            Assert.Equal(1, r.Rig.AuthFrameCount())
            Assert.Equal(1, r.Rig.PasswordCalls)
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    ' ================================================================ the eighteen operations

    Private NotInheritable Class OpStep
        Public Op As String
        Public Id As Long
        Public Members As Func(Of String, Long, ExchangeEnvelope)
        Public ReplyOk As Boolean
        Public ReplyCode As String
        ''' <summary>The `data` member of the corpus reply as JSON text; Nothing when the reply has none.</summary>
        Public ReplyData As String
    End Class

    ''' <summary>The arguments of one corpus request are read from the request itself; the only value that is not (the code hash
    ''' of a withdraw) comes from the REPLY the session read earlier.</summary>
    Private Shared Function ReadOp(request As ExchangeTranscriptStep, reply As ExchangeTranscriptStep, hash As Holder) As OpStep
        Dim result As New OpStep()
        Dim login As String, password As String, kind As String, deviceId As String, expectedLogin As String, guestAccount As String
        Dim maxAccounts As Integer? = Nothing
        Dim limits As ExchangeAdminWireLimits = Nothing
        Using doc As JsonDocument = JsonDocument.Parse(request.Json)
            Dim req As JsonElement = doc.RootElement
            result.Op = Text(req, "op")
            result.Id = req.GetProperty("id").GetInt64()
            login = Text(req, "login")
            password = Text(req, "password")
            kind = Text(req, "kind")
            deviceId = Text(req, "deviceId")
            expectedLogin = Text(req, "expectedLogin")
            guestAccount = Text(req, "guestAccount")
            Dim el As JsonElement = Nothing
            If req.TryGetProperty("maxAccounts", el) Then maxAccounts = el.GetInt32()
            Dim lim As JsonElement = Nothing
            If req.TryGetProperty("limits", lim) Then
                limits = New ExchangeAdminWireLimits()
                If lim.TryGetProperty("devicesPerAccount", el) Then limits.DevicesPerAccount = el.GetInt32()
                If lim.TryGetProperty("resourcesPerAccount", el) Then limits.ResourcesPerAccount = el.GetInt32()
                If lim.TryGetProperty("broadcastsPerAccount", el) Then limits.BroadcastsPerAccount = el.GetInt32()
                If lim.TryGetProperty("broadcastsPerDevice", el) Then limits.BroadcastsPerDevice = el.GetInt32()
                If lim.TryGetProperty("clipboardMaxBytes", el) Then limits.ClipboardMaxBytes = el.GetInt64()
            End If
        End Using
        Using doc As JsonDocument = JsonDocument.Parse(reply.Json)
            Dim rep As JsonElement = doc.RootElement
            Assert.Equal(result.Id, rep.GetProperty("id").GetInt64())
            result.ReplyOk = rep.GetProperty("ok").GetBoolean()
            result.ReplyCode = Text(rep, "code")
            Dim data As JsonElement = Nothing
            If rep.TryGetProperty("data", data) Then result.ReplyData = data.GetRawText()
        End Using
        Select Case result.Op
            Case ExchangeAdminWire.OpStatus
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.Status(t, n)
            Case ExchangeAdminWire.OpPolicyStatus
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.PolicyStatus(t, n)
            Case ExchangeAdminWire.OpPolicySet
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.PolicySet(t, n, maxAccounts, limits)
            Case ExchangeAdminWire.OpAccountList
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountList(t, n)
            Case ExchangeAdminWire.OpAccountAdd
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountAdd(t, n, login, password, kind)
            Case ExchangeAdminWire.OpAccountLimits
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountLimits(t, n, login, limits)
            Case ExchangeAdminWire.OpAccountPasswd
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountPasswd(t, n, login, password)
            Case ExchangeAdminWire.OpAccountKind
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountKind(t, n, login, kind)
            Case ExchangeAdminWire.OpAccountRemove
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.AccountRemove(t, n, login)
            Case ExchangeAdminWire.OpInvitationCreate
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.InvitationCreate(t, n, kind, expectedLogin)
            Case ExchangeAdminWire.OpInvitationList
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.InvitationList(t, n)
            Case ExchangeAdminWire.OpInvitationWithdraw
                ' the hash the session read in the invitation.create reply: the stream's own consistency is that this is the corpus' hash
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.InvitationWithdraw(t, n, hash.Value)
            Case ExchangeAdminWire.OpSharedInvitationStatus
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.SharedInvitationStatus(t, n)
            Case ExchangeAdminWire.OpSharedInvitationSet
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.SharedInvitationSet(t, n, password, kind)
            Case ExchangeAdminWire.OpDeviceList
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.DeviceList(t, n, login)
            Case ExchangeAdminWire.OpDevicePairingCode
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.DevicePairingCode(t, n, login)
            Case ExchangeAdminWire.OpDeviceRevoke
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.DeviceRevoke(t, n, login, deviceId)
            Case ExchangeAdminWire.OpGrantList
                result.Members = Function(t As String, n As Long) ExchangeAdminWire.GrantList(t, n, login, guestAccount)
            Case Else
                Assert.Fail("the corpus asks for '" & result.Op & "', an operation this client does not carry")
        End Select
        Return result
    End Function

    Private Shared Function OpsOf(stream As ExchangeTranscriptStream, hash As Holder) As List(Of OpStep)
        Dim result As New List(Of OpStep)
        Dim steps As List(Of ExchangeTranscriptStep) = stream.Replayable()
        For i As Integer = 0 To steps.Count - 1
            If steps(i).Kind <> ExchangeStepKind.ClientFrame OrElse steps(i).FrameType() <> "admin-request" Then Continue For
            Assert.True(i + 1 < steps.Count AndAlso steps(i + 1).Kind = ExchangeStepKind.ServerFrame AndAlso steps(i + 1).FrameType() = "admin-reply",
                        "step " & (i + 1).ToString(CultureInfo.InvariantCulture) & " of " & stream.Name & " is a request that no reply follows")
            result.Add(ReadOp(steps(i), steps(i + 1), hash))
        Next
        Return result
    End Function

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Async Function Stream_admin_ops_replays_all_18_requests_in_order_with_ids_1_to_18_each_reply_read_by_its_typed_reader_and_ends_with_bye(transcript As String) As Task
        Using r As Replay = Await Replay.OpenAsync(transcript, "admin-ops")
            Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, r.SignIn.Kind)
            Dim hash As New Holder()
            Dim ops As List(Of OpStep) = OpsOf(r.Stream, hash)
            ' the corpus asks for the 18 carried operations, once each, with ids 1..18
            Assert.Equal(18, ops.Count)
            Assert.Equal(Enumerable.Range(1, 18).Select(Function(n) CLng(n)).ToList(), ops.Select(Function(o) o.Id).ToList())
            Assert.Equal(18, ops.Select(Function(o) o.Op).Distinct().Count())
            Assert.All(ops, Sub(o) Assert.True(ExchangeAdminWire.IsCarried(o.Op), o.Op & " is not a carried operation"))

            Dim noData As New HashSet(Of String) From {ExchangeAdminWire.OpAccountLimits, ExchangeAdminWire.OpInvitationWithdraw, ExchangeAdminWire.OpSharedInvitationSet, ExchangeAdminWire.OpAccountRemove}
            For Each op As OpStep In ops
                Dim what As String = "op " & op.Id.ToString(CultureInfo.InvariantCulture) & " " & op.Op & " of " & r.Label
                Using outcome As ExchangeRemoteAdminOutcome = Await r.Rig.CallOpAsync(op.Op, op.Members)
                    If op.ReplyOk Then
                        Assert.True(outcome.Kind = ExchangeRemoteAdminOutcomeKind.Done, what & " was " & outcome.Kind.ToString() & " (" & outcome.Detail & ")")
                        If op.ReplyData Is Nothing Then
                            Assert.False(outcome.HasData, what & " has no data in the corpus")
                        Else
                            Assert.True(outcome.HasData, what & " lost its data")
                            Using want As JsonDocument = JsonDocument.Parse(op.ReplyData)
                                Assert.True(JsonEqual(want.RootElement, outcome.Data), what & ": the data is not the corpus' data: " & outcome.Data.GetRawText())
                            End Using
                        End If
                        Assert.Equal(noData.Contains(op.Op), op.ReplyData Is Nothing)
                        If op.Op = ExchangeAdminWire.OpInvitationCreate Then
                            hash.Value = outcome.Data.GetProperty("invitation").GetProperty("codeHash").GetString()
                        End If
                    Else
                        Assert.True(outcome.Kind = ExchangeRemoteAdminOutcomeKind.Failed, what & " was " & outcome.Kind.ToString() & " (" & outcome.Detail & ")")
                        Assert.Equal(op.ReplyCode, outcome.Code)
                    End If
                End Using
            Next
            ' the one failure of the stream is device.revoke of an unknown device
            Dim failed As List(Of OpStep) = ops.Where(Function(o) Not o.ReplyOk).ToList()
            Assert.Equal(1, failed.Count)
            Assert.Equal(ExchangeAdminWire.OpDeviceRevoke, failed(0).Op)
            Assert.Equal("not-found", failed(0).ReplyCode)

            Await K.WithinTask(r.Rig.Session.EndAsync(), "the end")
            Assert.True(r.Lock.ClientClosed)
            Dim frames As List(Of String) = r.Lock.WrittenFrames()
            Assert.Equal(20, frames.Count)    ' admin-auth, 18 requests, bye
            Assert.Equal("admin-auth", K.TypeOfFrame(frames(0)))
            For i As Integer = 1 To 18
                Assert.Equal("admin-request", K.TypeOfFrame(frames(i)))
            Next
            Assert.Equal("bye", K.TypeOfFrame(frames(19)))
            Assert.Equal(1, r.Rig.AuthFrameCount())
            AssertClientKeptToTheCorpus(r)
        End Using
    End Function

    ' ================================================================ what the replay itself relies on

    <Theory>
    <InlineData("admin-native")>
    <InlineData("admin-websocket")>
    Public Sub The_withdraw_in_the_corpus_names_the_code_hash_of_the_invitation_the_stream_created(transcript As String)
        Dim hash As New Holder()
        Dim stream As ExchangeTranscriptStream = Corpus.Value.Transcript(transcript).StreamNamed("admin-ops")
        Dim ops As List(Of OpStep) = OpsOf(stream, hash)
        Dim create As OpStep = ops.Single(Function(o) o.Op = ExchangeAdminWire.OpInvitationCreate)
        Dim createdHash As String
        Using doc As JsonDocument = JsonDocument.Parse(create.ReplyData)
            createdHash = doc.RootElement.GetProperty("invitation").GetProperty("codeHash").GetString()
        End Using
        Dim withdraw As ExchangeTranscriptStep = stream.ClientSteps().Single(Function(s) s.Json.Contains("""op"":""invitation.withdraw"""))
        Using doc As JsonDocument = JsonDocument.Parse(withdraw.Json)
            Assert.Equal(createdHash, doc.RootElement.GetProperty("codeHash").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub The_two_transcripts_issue_different_tokens_so_the_websocket_run_catches_a_hard_coded_one()
        For Each name As String In New String() {"admin-lapsed", "admin-ops"}
            Dim nativeToken As String = TokenOfStream(Native, name)
            Dim websocketToken As String = TokenOfStream(Websocket, name)
            Assert.Equal(43, nativeToken.Length)
            Assert.Equal(43, websocketToken.Length)
            Assert.NotEqual(nativeToken, websocketToken)
        Next
    End Sub

    Private Shared Function TokenOfStream(transcript As String, streamName As String) As String
        Dim ok As ExchangeTranscriptStep = Corpus.Value.Transcript(transcript).StreamNamed(streamName).Replayable().First(Function(s) s.FrameType() = "admin-auth-ok")
        Using doc As JsonDocument = JsonDocument.Parse(ok.Json)
            Return doc.RootElement.GetProperty("token").GetString()
        End Using
    End Function

    <Fact>
    Public Async Function A_request_that_carried_the_native_token_fails_the_lock_step_on_the_websocket_copy() As Task
        Dim nativeToken As String = TokenOfStream(Native, "admin-lapsed")
        Using r As Replay = Await Replay.OpenAsync(Websocket, "admin-lapsed")
            Assert.Equal(ExchangeRemoteAdminSignInKind.SignedIn, r.SignIn.Kind)
            Dim hardCoded As Func(Of String, Long, ExchangeEnvelope) = Function(token As String, id As Long) ExchangeAdminWire.Status(nativeToken, id)
            Dim threw As Boolean = False
            Try
                Using outcome As ExchangeRemoteAdminOutcome = Await K.Within(r.Rig.Session.CallAsync("status", hardCoded, CancellationToken.None), "the call with a hard-coded token")
                    threw = outcome.Kind <> ExchangeRemoteAdminOutcomeKind.Done
                End Using
            Catch ex As ExchangeLockStepException
                threw = True
            End Try
            Assert.True(threw, "a request with a constant token went through on the websocket copy")
            Assert.NotNull(r.Lock.Mismatch)
            Assert.Contains("step", r.Lock.Mismatch)
        End Using
    End Function

    <Fact>
    Public Sub The_websocket_copy_has_the_same_eight_streams_and_only_its_http_openings_are_left_out_of_the_replay()
        Dim nativeCopy As ExchangeTranscript = Corpus.Value.Transcript(Native)
        Dim wsCopy As ExchangeTranscript = Corpus.Value.Transcript(Websocket)
        Assert.Equal(New String() {"admin-disabled", "admin-excluded", "admin-wrong", "admin-waiting-slot", "admin-rate-limited", "admin-capacity", "admin-lapsed", "admin-ops"}, nativeCopy.StreamNames().ToArray())
        Assert.Equal(nativeCopy.StreamNames(), wsCopy.StreamNames())
        For Each name As String In nativeCopy.StreamNames()
            Dim n As ExchangeTranscriptStream = nativeCopy.StreamNamed(name)
            Dim w As ExchangeTranscriptStream = wsCopy.StreamNamed(name)
            Assert.DoesNotContain(n.Steps, Function(s) s.Kind = ExchangeStepKind.Http)
            Assert.Equal(ExchangeStepKind.Http, w.Steps(0).Kind)
            Assert.Equal(1, w.Steps.Where(Function(s) s.Kind = ExchangeStepKind.Http).Count())
            Assert.Equal(n.Steps.Count, w.Replayable().Count)
            Assert.Equal(n.Shape(), w.Shape().Substring(1))    ' after the http opening, the same shape: who speaks, in which order
        Next
    End Sub

    ' ================================================================ the corpus is the pinned one

    <Fact>
    Public Sub The_local_frames_v2_json_is_the_corpus_adopted_in_catalog_1_11_as_fmsw_245_completion_by_its_SHA_256()
        Dim bytes As Byte() = File.ReadAllBytes(ExchangeVectorCorpus.TranscriptPath())
        Dim problem As String = ExchangeTranscriptFile.PinFailure(bytes)
        Assert.True(problem Is Nothing, problem)
        Assert.Equal(ExchangeTranscriptFile.PinnedLength, bytes.Length)
    End Sub

    <Fact>
    Public Sub A_changed_corpus_is_noticed_and_the_failure_message_names_both_corpora()
        Dim bytes As Byte() = File.ReadAllBytes(ExchangeVectorCorpus.TranscriptPath())
        Dim changed As Byte() = CType(bytes.Clone(), Byte())
        changed(changed.Length \ 2) = CByte(changed(changed.Length \ 2) Xor 1)
        Dim message As String = ExchangeTranscriptFile.PinFailure(changed)
        Assert.NotNull(message)
        Assert.Contains("fmsw-245-completion", message)
        Assert.Contains("catalog 1.11", message)
        Assert.Contains("5cf90a6f6b1990155dfb346c581b97eb4f690531530f4e7a832219a1156c194b", message)
        Assert.Contains("524510", message)
        Assert.Contains("0C57C52B", message)
        Assert.Contains("488112", message)
        ' and it names the file it actually found
        Assert.Contains("this file is SHA-256 " & Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed)).ToLowerInvariant(), message)
        ' a file one byte shorter is no better
        Assert.NotNull(ExchangeTranscriptFile.PinFailure(bytes.Take(bytes.Length - 1).ToArray()))
    End Sub

    <AdoptedCorpusFact>
    Public Sub The_admin_native_section_equals_the_same_section_of_the_adopted_folder_when_it_is_present()
        Dim adopted As String = AdoptedCorpusFactAttribute.AdoptedPath()
        Assert.NotNull(adopted)
        Using local As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(ExchangeVectorCorpus.TranscriptPath()))
            Using theirs As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(adopted))    ' read only: the adopted folder is never written
                Dim a As JsonElement = SectionOf(local, Native)
                Dim b As JsonElement = SectionOf(theirs, Native)
                Assert.True(JsonEqual(a, b), "the admin-native section of the local frames-v2.json differs from the adopted fmsw-245-completion; take the change to the catalog first (L0278 7.2)")
            End Using
        End Using
    End Sub

    Private Shared Function SectionOf(doc As JsonDocument, name As String) As JsonElement
        For Each t As JsonElement In doc.RootElement.GetProperty("transcripts").EnumerateArray()
            If String.Equals(Text(t, "name"), name, StringComparison.Ordinal) Then Return t
        Next
        Assert.Fail("no transcript '" & name & "' in the corpus")
        Return Nothing
    End Function

    ' ================================================================ what the corpus cannot prove

    <Fact(Skip:="Cases the corpus has no stream for (covered by 7.4 and 7.5 or named gaps, G-6): no `probe`; no oversize frame; no `bad-request`; no unknown operation; no `tls-required` on an administration stream; no reply code but `not-found`; no client-close step; no silence step.")>
    Public Sub Gaps_of_the_corpus_that_the_session_tests_and_the_real_binary_cover_instead()
    End Sub

    <Fact>
    Public Sub The_gaps_the_skipped_fact_names_are_still_gaps_of_the_corpus()
        Dim allowedTypes As New HashSet(Of String) From {"admin-auth", "admin-auth-ok", "admin-request", "admin-reply", "refused", "bye"}
        Dim refusals As New HashSet(Of String)
        Dim codes As New HashSet(Of String)
        For Each name As String In New String() {Native, Websocket}
            For Each stream As ExchangeTranscriptStream In Corpus.Value.Transcript(name).Streams
                For Each s As ExchangeTranscriptStep In stream.Steps
                    ' no client-close step and no silence step: a step is a client frame, a server frame, a server close, or (websocket) an http opening
                    Assert.Contains(s.Kind, New ExchangeStepKind() {ExchangeStepKind.ClientFrame, ExchangeStepKind.ServerFrame, ExchangeStepKind.ServerClose, ExchangeStepKind.Http})
                    If s.Kind = ExchangeStepKind.ServerClose Then Assert.Equal("server->client", s.Direction)
                    If s.Kind = ExchangeStepKind.Http Then Continue For
                    If s.Kind = ExchangeStepKind.ServerClose Then Continue For
                    ' no oversize frame: nothing near the 16384 bytes of the pre-sign-in limit
                    Assert.True(s.Bytes.Length < 4 + 16384, "a frame of " & s.Bytes.Length.ToString(CultureInfo.InvariantCulture) & " bytes: the corpus has an oversize case now")
                    Dim type As String = s.FrameType()
                    Assert.Contains(type, allowedTypes)    ' no probe, no bad-request frame, no unknown operation
                    Assert.DoesNotContain("tls-required", s.Json)
                    Assert.DoesNotContain("bad-request", s.Json)
                    Using doc As JsonDocument = JsonDocument.Parse(s.Json)
                        Dim v As String = Text(doc.RootElement, "reason")
                        If v IsNot Nothing Then refusals.Add(v)
                        v = Text(doc.RootElement, "code")
                        If v IsNot Nothing Then codes.Add(v)
                        If type = "admin-request" Then Assert.True(ExchangeAdminWire.IsCarried(Text(doc.RootElement, "op")), "the corpus asks for an operation this client does not carry: " & s.Json)
                    End Using
                Next
            Next
        Next
        Assert.Equal(New String() {"admin-disabled", "bad-credentials", "capacity", "rate-limited"}, refusals.OrderBy(Function(x) x, StringComparer.Ordinal).ToArray())
        Assert.Equal(New String() {"not-found"}, codes.ToArray())
    End Sub

End Class
#End If
