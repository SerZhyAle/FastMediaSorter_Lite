Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 148, the host half that was waiting on the IPC (T-2..T-6): the <c>SetExchange</c> request and the
''' <c>exchange</c> status block, the rules that decide what is sent and what the export takes, the model of
''' the settings rows, and the one ordering rule that protects the password - it goes only to a worker that
''' said it can hold it. Everything here is pure or drives a private stub pipe; the live proof against a real
''' worker is T-8 (hand-run).
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeProducerIpcTests
    Implements IDisposable

    Private ReadOnly _dirs As New List(Of String)()

    Public Sub New()
        ExchangeTestSupport.Reset()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        For Each d As String In _dirs
            ExchangeTestSupport.DeleteQuietly(d)
        Next
    End Sub

    Private Function Component(withExe As Boolean) As String
        Dim d As String = ExchangeTestSupport.MakeFakeComponent(withExe, withExe, False)
        _dirs.Add(d)
        Return d
    End Function

    Private Const Sid1 As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const Sid2 As String = "BBBBBBBBBBBBBBBBBBBBBA"   ' last char 'A': one of the four a 128-bit id can end in
    Private Const GoodPassword As String = "Abcdefghij1"

    Private Shared Function Share(name As String, state As String, Optional sid As String = Sid1, Optional relay As Integer = 40007) As WorkerExchangeShare
        Return New WorkerExchangeShare With {.Name = name, .ShareId = sid, .RelayPort = relay, .State = state}
    End Function

    Private Shared Function Worker(Optional supported As Boolean = True, Optional enabled As Boolean = True,
                                   Optional shares As WorkerExchangeShare() = Nothing,
                                   Optional roots As String() = Nothing,
                                   Optional configured As Boolean = True) As WorkerStatus
        Dim st As New WorkerStatus With {.Running = True, .ListenPort = 2222, .ExchangeSupported = supported,
                                         .Roots = New List(Of ShareFolder)()}
        For Each r As String In If(roots, New String() {"Photos"})
            st.Roots.Add(New ShareFolder With {.name = r, .hostPath = "C:\__fms_test__148\" & r})
        Next
        If configured Then
            st.Exchange = New WorkerExchange With {.Enabled = enabled, .CertFingerprint = "SHA256:abc", .LastError = "",
                                                   .Shares = New List(Of WorkerExchangeShare)(If(shares, New WorkerExchangeShare() {Share("Photos", "registered")}))}
        End If
        Return st
    End Function

    Private Shared Function Settings(mode As ExchangeMode, Optional reg As String = "relay.example.org:22022",
                                     Optional client As String = "relay.example.org:22022") As ExchangeProducerSettings
        Return New ExchangeProducerSettings With {.Mode = mode, .RegistrationEndpoint = reg, .ClientEndpoint = client}
    End Function

    ' ---- the wire: SetExchange request ---------------------------------------------------------------

    Private Shared Function Json(req As WorkerRequest) As JsonElement
        Return JsonDocument.Parse(Encoding.UTF8.GetString(WorkerIpc.SerializeRequest(req))).RootElement
    End Function

    <Fact>
    Public Sub SetExchange_off_sends_enabled_false_and_nothing_else()
        Dim req = ExchangeProducerRules.BuildSetExchange(Settings(ExchangeMode.Off), 0, GoodPassword, "SHA256:x", {"Photos"})
        Dim j = Json(req)
        Assert.Equal("SetExchange", j.GetProperty("type").GetString())
        Assert.False(j.GetProperty("enabled").GetBoolean())
        For Each absent As String In {"password", "registerHost", "registerPort", "certFingerprint", "reissue"}
            Dim el As JsonElement
            Assert.False(j.TryGetProperty(absent, el), absent & " must not ride on an 'off' request")
        Next
    End Sub

    <Fact>
    Public Sub SetExchange_on_carries_the_endpoint_and_omits_what_is_unset()
        Dim req = ExchangeProducerRules.BuildSetExchange(Settings(ExchangeMode.External), 0, "", "", Nothing)
        Dim j = Json(req)
        Assert.True(j.GetProperty("enabled").GetBoolean())
        Assert.Equal("relay.example.org", j.GetProperty("registerHost").GetString())
        Assert.Equal(22022, j.GetProperty("registerPort").GetInt32())
        Dim el As JsonElement
        Assert.False(j.TryGetProperty("password", el), "absent password = unchanged (an empty field must not clear it)")
        Assert.False(j.TryGetProperty("certFingerprint", el))
        Assert.False(j.TryGetProperty("reissue", el))
        Assert.Equal(1, j.GetProperty("schemaVersion").GetInt32())
    End Sub

    <Fact>
    Public Sub SetExchange_carries_password_pin_and_a_deduplicated_reissue_list()
        Dim req = ExchangeProducerRules.BuildSetExchange(Settings(ExchangeMode.External), 0, GoodPassword, " SHA256:pin ", {"A", "", "A", "B"})
        Dim j = Json(req)
        Assert.Equal(GoodPassword, j.GetProperty("password").GetString())
        Assert.Equal("SHA256:pin", j.GetProperty("certFingerprint").GetString())
        Assert.Equal(New String() {"A", "B"}, j.GetProperty("reissue").EnumerateArray().Select(Function(e) e.GetString()).ToArray())
    End Sub

    <Fact>
    Public Sub This_pc_registers_on_loopback_and_the_local_servers_port()
        Dim req = ExchangeProducerRules.BuildSetExchange(Settings(ExchangeMode.ThisPc, reg:="ignored:1"), 30123, GoodPassword, "", Nothing)
        Assert.Equal("127.0.0.1", req.registerHost)
        Assert.Equal(30123, req.registerPort.Value)
        ' an unreadable local port falls back to the server's default, never to 0
        Dim fallback = ExchangeProducerRules.BuildSetExchange(Settings(ExchangeMode.ThisPc), 0, "", "", Nothing)
        Assert.Equal(ExchangeSettings.DefaultPort, fallback.registerPort.Value)
    End Sub

    <Fact>
    Public Sub An_unparseable_external_endpoint_reads_as_off_and_validation_catches_it_first()
        Dim s = Settings(ExchangeMode.External, reg:="https://nope")
        Assert.Equal(ExchangeProducerProblem.RegistrationEndpoint, ExchangeProducerRules.Validate(s, GoodPassword, True))
        ' ... and the builder never turns it into a registration somewhere else
        Assert.False(ExchangeProducerRules.BuildSetExchange(s, 0, GoodPassword, "", Nothing).enabled.Value)
    End Sub

    ' ---- validation ----------------------------------------------------------------------------------

    <Fact>
    Public Sub Validation_order_and_verdicts()
        Assert.Equal(ExchangeProducerProblem.None, ExchangeProducerRules.Validate(Settings(ExchangeMode.Off, "", "garbage"), "", True))
        Assert.Equal(ExchangeProducerProblem.ClientEndpointMalformed, ExchangeProducerRules.Validate(Settings(ExchangeMode.External, client:="no-port"), GoodPassword, True))
        Assert.Equal(ExchangeProducerProblem.ClientEndpointLoopback, ExchangeProducerRules.Validate(Settings(ExchangeMode.External, client:="127.0.0.1:22022"), GoodPassword, True))
        Assert.Equal(ExchangeProducerProblem.None, ExchangeProducerRules.Validate(Settings(ExchangeMode.External, client:=""), GoodPassword, True))
        Assert.Equal(ExchangeProducerProblem.PasswordMissing, ExchangeProducerRules.Validate(Settings(ExchangeMode.External), "", True))
        Assert.Equal(ExchangeProducerProblem.None, ExchangeProducerRules.Validate(Settings(ExchangeMode.External), "", False))
        Assert.Equal(ExchangeProducerProblem.PasswordBelowFloor, ExchangeProducerRules.Validate(Settings(ExchangeMode.External), "short1A", False))
        Assert.Equal(ExchangeProducerProblem.None, ExchangeProducerRules.Validate(Settings(ExchangeMode.ThisPc, reg:="", client:="relay.example.org:22022"), GoodPassword, True))
    End Sub

    ' ---- the status block ----------------------------------------------------------------------------

    <Fact>
    Public Sub Status_exchange_block_binds_from_camelCase_and_unknown_states_degrade()
        Dim body As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]," &
            """exchangeSupported"":true,""exchange"":{""enabled"":true,""certFingerprint"":""SHA256:abc"",""lastError"":""""," &
            """shares"":[{""name"":""Photos"",""shareId"":""" & Sid1 & """,""relayPort"":40007,""state"":""registered""}," &
            "{""name"":""Docs"",""shareId"":"""",""relayPort"":0,""state"":""from-the-future""}]}}}"
        Dim resp = WorkerIpc.ParseResponse(body)
        Dim st = resp.Status
        Assert.True(st.ExchangeSupported)
        Assert.True(st.Exchange.Enabled)
        Assert.Equal("SHA256:abc", st.Exchange.CertFingerprint)
        Assert.Equal(2, st.Exchange.Shares.Count)
        Assert.Equal(ExchangeShareState.Registered, ExchangeShareStates.Parse(st.Exchange.Shares(0).State))
        Assert.Equal(40007, st.Exchange.Shares(0).RelayPort)
        Assert.Equal(ExchangeShareState.NotRegistered, ExchangeShareStates.Parse(st.Exchange.Shares(1).State))
    End Sub

    <Fact>
    Public Sub An_older_worker_status_has_neither_member_and_that_is_not_an_error()
        Dim resp = WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}")
        Assert.False(resp.Status.ExchangeSupported)
        Assert.Null(resp.Status.Exchange)
        Assert.Equal(ExchangeWorkerReadiness.Stale, ExchangeProducerRules.Readiness(resp.Status))
        Assert.Equal(ExchangeWorkerReadiness.NoWorker, ExchangeProducerRules.Readiness(Nothing))
    End Sub

    <Fact>
    Public Sub Every_named_state_parses_and_only_three_need_the_user()
        Dim names As String() = {"connecting", "registered", "refused-password", "refused-capacity", "refused-port",
                                 "certificate-changed", "unreachable", "rate-limited"}
        Dim seen As New HashSet(Of ExchangeShareState)()
        For Each n As String In names
            Dim st = ExchangeShareStates.Parse(n)
            Assert.NotEqual(ExchangeShareState.NotRegistered, st)
            seen.Add(st)
        Next
        Assert.Equal(names.Length, seen.Count)
        Assert.Equal(ExchangeShareState.NotRegistered, ExchangeShareStates.Parse("off"))
        Assert.Equal(ExchangeShareState.NotRegistered, ExchangeShareStates.Parse(Nothing))
        Assert.Equal(ExchangeShareState.RefusedPassword, ExchangeShareStates.Parse(" Refused-Password "))
        Dim needing = seen.Where(AddressOf ExchangeShareStates.NeedsUser).ToList()
        Assert.Equal(3, needing.Count)
        Assert.DoesNotContain(ExchangeShareState.Unreachable, needing)
        Assert.DoesNotContain(ExchangeShareState.RateLimited, needing)
    End Sub

    ' ---- what the export takes -----------------------------------------------------------------------

    <Fact>
    Public Sub Export_takes_the_first_registered_share_in_roots_order()
        Dim st = Worker(roots:={"Docs", "Photos", "Music"},
                        shares:={Share("Photos", "registered", Sid1, 40001), Share("Music", "registered", Sid2, 40002), Share("Docs", "connecting", Sid2, 0)})
        Dim ex = ExchangeProducerRules.ExportFor(st, Settings(ExchangeMode.External))
        Assert.NotNull(ex)
        Assert.Equal(Sid1, ex.ShareId)
        Assert.Equal(40001, ex.RelayPort)
        Assert.Equal("relay.example.org", ex.ClientHost)
        Assert.Equal(22022, ex.ClientPort)
    End Sub

    <Fact>
    Public Sub Export_is_nothing_whenever_the_export_must_stay_what_it_was()
        Dim s = Settings(ExchangeMode.External)
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(), Settings(ExchangeMode.Off)))                 ' mode off
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(configured:=False), s))                         ' worker never configured / older
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(enabled:=False), s))                            ' unregistered, ids kept
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(shares:={Share("Photos", "unreachable")}), s))
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(shares:={Share("Photos", "registered", "not-an-id")}), s))
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(shares:={Share("Other", "registered")}), s))    ' not in the exported roots
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(), Settings(ExchangeMode.External, client:="")))        ' no client endpoint
        Assert.Null(ExchangeProducerRules.ExportFor(Worker(), Settings(ExchangeMode.External, client:="127.0.0.1:22022"))) ' loopback never
        Assert.Null(ExchangeProducerRules.ExportFor(Nothing, s))
    End Sub

    <Fact>
    Public Sub An_export_with_the_producer_off_is_byte_identical_to_one_without_the_argument()
        Dim status As New WorkerStatus With {.Running = True, .ListenPort = 2222, .Username = "fms", .Password = "pw", .Fingerprint = "SHA256:abc",
            .Roots = New List(Of ShareFolder) From {New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__148", .readOnly = False}},
            .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50"}}
        Dim stamp As New ShareConfigStamp With {.ResourceName = "Test PC", .CreatedAtUtc = New Date(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)}
        Dim plain = ShareConfigBuilder.Build(status, includeExternal:=True, stamp:=stamp)
        Dim viaRules = ShareConfigBuilder.Build(status, includeExternal:=True, stamp:=stamp, exchange:=ExchangeProducerRules.ExportFor(status, Settings(ExchangeMode.Off)))
        Assert.Equal(plain.ConfigJson, viaRules.ConfigJson)
        status.ExchangeSupported = True
        status.Exchange = New WorkerExchange With {.Enabled = False, .Shares = New List(Of WorkerExchangeShare) From {Share("Photos", "registered")}}
        Dim disabled = ShareConfigBuilder.Build(status, includeExternal:=True, stamp:=stamp, exchange:=ExchangeProducerRules.ExportFor(status, Settings(ExchangeMode.External)))
        Assert.Equal(plain.ConfigJson, disabled.ConfigJson)
    End Sub

    <Fact>
    Public Sub An_export_with_a_registered_share_carries_the_relay_after_every_direct_path_and_no_secret()
        Dim status As New WorkerStatus With {.Running = True, .ListenPort = 2222, .Username = "fms", .Password = "pw", .Fingerprint = "SHA256:abc",
            .ExchangeSupported = True, .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.9", .ExternalPort = 2222},
            .Roots = New List(Of ShareFolder) From {New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__148"}},
            .Exchange = New WorkerExchange With {.Enabled = True, .Shares = New List(Of WorkerExchangeShare) From {Share("Photos", "registered", Sid1, 40007)}}}
        Dim stamp As New ShareConfigStamp With {.ResourceName = "Test PC", .CreatedAtUtc = New Date(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)}
        Dim res = ShareConfigBuilder.Build(status, includeExternal:=True, stamp:=stamp, exchange:=ExchangeProducerRules.ExportFor(status, Settings(ExchangeMode.External)))
        Assert.True(res.HasRelay)
        Dim kinds As List(Of String) = Regex.Matches(res.ConfigJson, """kind"":""([^""]+)""").Select(Function(m) m.Groups(1).Value).ToList()
        Assert.Equal("lan", kinds(0))
        Assert.True(kinds.IndexOf(ExchangeWireNames.TunnelKind) > kinds.IndexOf("lan"))
        Assert.DoesNotContain("loopback", res.ConfigJson, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("127.0.0.1", res.ConfigJson)
    End Sub

    <Fact>
    Public Sub Client_endpoint_suggestions_never_look_outside_and_never_loop_back()
        Dim reach As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.9"}
        Assert.Equal("relay.example.org:22022", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.External, "relay.example.org:22022", Nothing, "", 0))
        Assert.Equal("203.0.113.9:30123", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.ThisPc, "", reach, "10.0.0.5", 30123))
        reach.IsCgnat = True
        Assert.Equal("192.168.1.50:30123", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.ThisPc, "", reach, "10.0.0.5", 30123))
        Assert.Equal("10.0.0.5:22022", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.ThisPc, "", Nothing, "10.0.0.5", 0))
        Assert.Equal("", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.ThisPc, "", Nothing, "", 0))
        Assert.Equal("", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.External, "127.0.0.1:22022", Nothing, "", 0))
        Assert.Equal("", ExchangeProducerRules.SuggestClientEndpoint(ExchangeMode.Off, "relay.example.org:22022", reach, "10.0.0.5", 30123))
    End Sub

    ' ---- the model of the rows -----------------------------------------------------------------------

    Private Shared Function Model(mode As ExchangeMode, worker As WorkerStatus, Optional client As String = "relay.example.org:22022",
                                  Optional local As ExchangeLocalServer = ExchangeLocalServer.NotApplicable) As ExchangeProducerModel
        Return ExchangeProducerModel.Build(New ExchangeProducerSnapshot With {.Mode = mode, .RegistrationEndpoint = "relay.example.org:22022",
                                           .ClientEndpoint = client, .Worker = worker, .LocalServer = local})
    End Function

    <Fact>
    Public Sub Mode_off_shows_no_row_at_all()
        Dim m = Model(ExchangeMode.Off, Worker())
        Assert.False(m.ShowRegistration OrElse m.ShowClient OrElse m.ShowPassword OrElse m.ShowFingerprint OrElse m.ShowShares OrElse m.ShowReissue OrElse m.ShowState OrElse m.ShowStartLocal)
    End Sub

    <Fact>
    Public Sub The_registration_row_belongs_to_the_external_mode_only()
        Assert.True(Model(ExchangeMode.External, Worker()).ShowRegistration)
        Assert.False(Model(ExchangeMode.ThisPc, Worker()).ShowRegistration)
        Assert.True(Model(ExchangeMode.ThisPc, Worker()).ShowClient)
        Assert.True(Model(ExchangeMode.ThisPc, Worker()).ShowPassword)
    End Sub

    <Fact>
    Public Sub State_line_names_a_silent_a_stale_and_an_unconfigured_worker()
        Assert.Equal(ExchangeProducerText.WorkerSilent(), Model(ExchangeMode.External, Nothing).StateLine)
        Assert.Equal(ExchangeProducerText.WorkerStale(), Model(ExchangeMode.External, Worker(supported:=False, configured:=False)).StateLine)
        Assert.Equal(ExchangeProducerText.NotApplied(), Model(ExchangeMode.External, Worker(configured:=False)).StateLine)
        Assert.Equal(ExchangeProducerText.NotApplied(), Model(ExchangeMode.External, Worker(enabled:=False)).StateLine)
        Assert.True(Model(ExchangeMode.External, Worker(configured:=False)).PasswordRequired)
        Assert.False(Model(ExchangeMode.External, Worker()).PasswordRequired)
    End Sub

    <Fact>
    Public Sub State_line_counts_registrations_and_says_why_one_that_needs_the_user_is_not()
        Dim st = Worker(roots:={"Photos", "Docs", "Music"}, shares:={Share("Photos", "registered", Sid1, 40001),
                        Share("Docs", "refused-password", ""), Share("Music", "unreachable", "")})
        Dim m = Model(ExchangeMode.External, st)
        Assert.StartsWith(ExchangeProducerText.RegisteredCount(1, 3), m.StateLine)
        Assert.Contains(ExchangeProducerText.Reason(ExchangeShareState.RefusedPassword), m.StateLine)
        Assert.DoesNotContain(ExchangeProducerText.Reason(ExchangeShareState.Unreachable), m.StateLine)   ' it retries by itself
        Assert.Contains(ExchangeProducerText.ShareLine("Photos", ExchangeShareState.Registered, 40001), m.SharesText)
        Assert.Contains(ExchangeProducerText.ShareLine("Music", ExchangeShareState.Unreachable, 0), m.SharesText)
        Assert.True(m.ShowShares)
        Assert.Equal(New String() {"Photos"}, m.ReissueNames.ToArray())   ' only the share that already holds an id
        Assert.True(m.ShowReissue)
    End Sub

    <Fact>
    Public Sub No_shares_and_no_ids_hide_the_list_and_the_new_link_row()
        Dim m = Model(ExchangeMode.External, Worker(shares:=New WorkerExchangeShare() {}))
        Assert.Equal(ExchangeProducerText.NoShares(), m.StateLine)
        Assert.False(m.ShowShares)
        Assert.False(m.ShowReissue)
        Dim connecting = Model(ExchangeMode.External, Worker(shares:={Share("Photos", "connecting", "", 0)}))
        Assert.False(connecting.ShowReissue, "a share with no id yet has nothing to rotate")
    End Sub

    <Fact>
    Public Sub The_pinned_fingerprint_is_shown_and_an_external_one_gets_the_out_of_band_note()
        Dim ext = Model(ExchangeMode.External, Worker())
        Assert.True(ext.ShowFingerprint)
        Assert.Equal("SHA256:abc", ext.Fingerprint)
        Assert.Equal(ExchangeProducerText.FingerprintExternalNote(), ext.FingerprintNote)
        Assert.Equal("", Model(ExchangeMode.ThisPc, Worker()).FingerprintNote)
    End Sub

    <Fact>
    Public Sub The_client_note_says_why_there_is_no_tunnel_path()
        Assert.Equal(ExchangeProducerText.ClientEmptyNote(), Model(ExchangeMode.External, Worker(), client:="").ClientNote)
        Assert.Equal(ExchangeProducerText.Problem(ExchangeProducerProblem.ClientEndpointLoopback, ""), Model(ExchangeMode.External, Worker(), client:="localhost:22022").ClientNote)
        Assert.Equal("", Model(ExchangeMode.External, Worker()).ClientNote)
    End Sub

    <Fact>
    Public Sub This_pc_with_a_missing_or_stopped_server_says_which_and_offers_the_start_only_when_stopped()
        Dim missing = Model(ExchangeMode.ThisPc, Worker(), local:=ExchangeLocalServer.ComponentMissing)
        Assert.Equal(ExchangeProducerText.LocalMissing(), missing.LocalNote)
        Assert.False(missing.ShowStartLocal)
        Dim stopped = Model(ExchangeMode.ThisPc, Worker(), local:=ExchangeLocalServer.Stopped)
        Assert.Equal(ExchangeProducerText.LocalStopped(), stopped.LocalNote)
        Assert.True(stopped.ShowStartLocal)
        Dim running = Model(ExchangeMode.ThisPc, Worker(), local:=ExchangeLocalServer.Running)
        Assert.Equal("", running.LocalNote)
        Assert.False(Model(ExchangeMode.External, Worker(), local:=ExchangeLocalServer.Stopped).ShowStartLocal)
    End Sub

    <Fact>
    Public Sub No_text_of_the_model_carries_a_share_id_or_the_password()
        Dim st = Worker(shares:={Share("Photos", "registered", Sid1, 40001)})
        Dim m = Model(ExchangeMode.External, st)
        For Each text As String In {m.StateLine, m.SharesText, m.ClientNote, m.LocalNote, m.FingerprintNote}
            Assert.DoesNotContain(Sid1, text)
            Assert.DoesNotContain(GoodPassword, text)
        Next
    End Sub

    ' ---- the ordering rule: the password only goes to a worker that can hold it -------------------------

    <Fact>
    Public Async Function A_stale_worker_is_sent_nothing_and_never_the_password() As Task
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":true}")
            Dim res = Await ExchangeProducerController.ApplyAsync(Settings(ExchangeMode.External), 0, GoodPassword, "", Nothing,
                                                                  Worker(supported:=False, configured:=False), stub.PipeName)
            Assert.Equal(ExchangeApplyOutcome.StaleWorker, res.Outcome)
            Assert.Equal(ExchangeProducerText.WorkerStale(), res.Text)
            Assert.Empty(stub.Seen())
        End Using
    End Function

    <Fact>
    Public Async Function No_status_at_all_sends_nothing() As Task
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":true}")
            Dim res = Await ExchangeProducerController.ApplyAsync(Settings(ExchangeMode.External), 0, GoodPassword, "", Nothing, Nothing, stub.PipeName)
            Assert.Equal(ExchangeApplyOutcome.NoWorker, res.Outcome)
            Assert.Empty(stub.Seen())
        End Using
    End Function

    <Fact>
    Public Async Function Off_on_a_never_configured_worker_has_nothing_to_unregister() As Task
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":true}")
            Dim res = Await ExchangeProducerController.ApplyAsync(Settings(ExchangeMode.Off), 0, "", "", Nothing,
                                                                  Worker(supported:=False, configured:=False), stub.PipeName)
            Assert.Equal(ExchangeApplyOutcome.Applied, res.Outcome)
            Assert.Empty(stub.Seen())
        End Using
    End Function

    <Fact>
    Public Async Function A_capable_worker_gets_one_SetExchange_with_the_password_and_the_result_carries_the_status() As Task
        Dim answer As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[],""exchangeSupported"":true," &
                               """exchange"":{""enabled"":true,""certFingerprint"":"""",""lastError"":"""",""shares"":[]}}}"
        Using stub As New StubWorker(Function(type) answer)
            Dim res = Await ExchangeProducerController.ApplyAsync(Settings(ExchangeMode.External), 0, GoodPassword, "", Nothing,
                                                                  Worker(configured:=False), stub.PipeName)
            Assert.Equal(ExchangeApplyOutcome.Applied, res.Outcome)
            Assert.True(res.Status.Exchange.Enabled)
            Assert.Equal(New String() {"SetExchange"}, stub.Seen().ToArray())
            Dim sent = stub.SeenRequests().Single()
            Assert.Contains("""enabled"":true", sent)
            Assert.Contains("""registerHost"":""relay.example.org""", sent)
            Assert.Contains("""password"":""" & GoodPassword & """", sent)
        End Using
    End Function

    <Fact>
    Public Async Function A_worker_that_says_no_is_reported_with_its_own_words_and_not_as_applied() As Task
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":false,""error"":""unsupported request type""}")
            Dim res = Await ExchangeProducerController.ApplyAsync(Settings(ExchangeMode.External), 0, GoodPassword, "", Nothing, Worker(), stub.PipeName)
            Assert.Equal(ExchangeApplyOutcome.Refused, res.Outcome)
            Assert.NotEqual("", res.Text)
        End Using
    End Function

    ' ---- the secret never reaches a log or a stored field ---------------------------------------------

    <Fact>
    Public Sub No_producer_source_writes_the_password_or_a_share_id_to_a_log_or_a_setting()
        Dim names As String() = {"ExchangeProducer.vb", "ExchangeProducerText.vb", "Share_Settings_Form.ExchangeProducer.vb"}
        For Each f As String In Directory.EnumerateFiles(ExchangeTestSupport.CompanionDir(), "*.vb", SearchOption.AllDirectories)
            If Not names.Contains(Path.GetFileName(f)) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(f))
            For Each line As String In code.Split({vbLf}, StringSplitOptions.None)
                If Regex.IsMatch(line, "AppFileLogger|Debug\.Write|SaveSetting|Console\.Write") AndAlso
                   Regex.IsMatch(line, "(?i)password|ShareId|\bpw\b") Then
                    Assert.Fail(Path.GetFileName(f) & " logs or stores a secret: " & line.Trim())
                End If
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_SetExchange_password_is_cleared_from_the_request_object_after_the_call()
        Dim src As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(Path.Combine(ExchangeTestSupport.CompanionDir(), "Core", "ExchangeProducer.vb")))
        Assert.Contains("req.password = Nothing", src)
    End Sub

    <Fact>
    Public Sub The_form_clears_the_password_box_when_applied_and_when_the_window_closes()
        Dim src As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(Path.Combine(ExchangeTestSupport.CompanionDir(), "Forms", "Share_Settings_Form.ExchangeProducer.vb")))
        Assert.Equal(2, Regex.Matches(src, "_txtPxPw\.Text = """"").Count)
        Assert.Contains("MessageBoxDefaultButton.Button2", src)
    End Sub

    ' ---- the window, headless -----------------------------------------------------------------------

    Private Shared Function PrivateField(Of T)(form As Share_Settings_Form, name As String) As T
        Dim f = GetType(Share_Settings_Form).GetField(name, System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
        Assert.True(f IsNot Nothing, name & " is not a field of the window")
        Return DirectCast(f.GetValue(form), T)
    End Function

    <Fact>
    Public Sub Without_the_component_and_with_the_producer_off_the_window_has_no_producer_row()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=False)
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         Assert.False(PrivateField(Of Boolean)(form, "_pxOffered"))
                                         Assert.Null(PrivateField(Of System.Windows.Forms.ComboBox)(form, "_cmbPxMode"))
                                         Assert.Null(PrivateField(Of System.Windows.Forms.Timer)(form, "_pxTimer"))
                                     End Using
                                 End Sub)
    End Sub

    <Fact>
    Public Sub With_the_component_the_producer_rows_exist_off_and_nothing_is_read_at_construction()
        ExchangeProcess.ExeDirectoryOverride = Component(withExe:=True)
        ExchangeTestSupport.OnUi(Sub()
                                     Using form As New Share_Settings_Form()
                                         Assert.True(PrivateField(Of Boolean)(form, "_pxOffered"))
                                         Dim combo = PrivateField(Of System.Windows.Forms.ComboBox)(form, "_cmbPxMode")
                                         Assert.NotNull(combo)
                                         Assert.Equal(3, combo.Items.Count)
                                         Assert.Equal(CInt(ExchangeMode.Off), combo.SelectedIndex)
                                         Assert.Equal(DropDownStyleOf(combo), System.Windows.Forms.ComboBoxStyle.DropDownList)
                                         ' nothing was read from the worker: no status yet, and the password box is empty and masked
                                         Assert.Null(PrivateField(Of WorkerStatus)(form, "_pxStatus"))
                                         Dim pw = PrivateField(Of System.Windows.Forms.TextBox)(form, "_txtPxPw")
                                         Assert.Equal("", pw.Text)
                                         Assert.True(pw.UseSystemPasswordChar)
                                     End Using
                                 End Sub)
    End Sub

    Private Shared Function DropDownStyleOf(c As System.Windows.Forms.ComboBox) As System.Windows.Forms.ComboBoxStyle
        Return c.DropDownStyle
    End Function

    <Fact>
    Public Sub The_producer_rows_are_made_of_registered_words_in_every_language()
        ' every sentence the rows can show, in every language, is non-empty and keeps its placeholders
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Assert.NotEqual("", ExchangeProducerText.ModeLabel())
                For Each mode As ExchangeMode In {ExchangeMode.Off, ExchangeMode.ThisPc, ExchangeMode.External}
                    Assert.NotEqual("", ExchangeProducerText.ModeName(mode))
                Next
                For Each st As ExchangeShareState In [Enum].GetValues(Of ExchangeShareState)()
                    Assert.NotEqual("", ExchangeProducerText.Reason(st))
                Next
                Assert.Contains("Photos", ExchangeProducerText.ShareLine("Photos", ExchangeShareState.Registered, 40007))
                Assert.Contains("40007", ExchangeProducerText.ShareLine("Photos", ExchangeShareState.Registered, 40007))
                Assert.Contains("Photos", ExchangeProducerText.ReissueQuestion("Photos"))
                Assert.Contains("3", ExchangeProducerText.RegisteredCount(3, 4))
                Assert.Contains("4", ExchangeProducerText.RegisteredCount(3, 4))
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_producer_rows_are_built_only_where_the_feature_can_exist()
        Dim form As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(Path.Combine(ExchangeTestSupport.CompanionDir(), "Forms", "Share_Settings_Form.ExchangeProducer.vb")))
        Dim build As String = ExchangeTestSupport.MethodBody(form, "Private Sub BuildExchangeProducerRows(sec As CollapsibleSection)")
        Assert.Contains("If Not _pxOffered Then Return", build)
        Assert.Contains("ExchangeProcess.IsAvailable()", form)
    End Sub
End Class
