Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-6 (5.3, 5.4, 5.6, 5.7, 5.8) with no window: the sign-in flow the window drives, against a scripted handshake and a
''' lock-step stream, with a REAL <see cref="ExchangeRemoteAdminSession"/> inside. Every phase of 5.6 is reached here; the password is
''' read only after the certificate decision; a Yes writes the pins before the next connection; nothing signs in by itself; a target
''' switch ends the old session before anything else; an answer that arrives after the window moved on is dropped.
''' </summary>
Public Class RemoteAdminControllerTests

    Private Const Secret As String = "AdminSecretPass1"
    Private Shared ReadOnly OtherPin As String = "SHA256:" & New String("B"c, 43)
    Private Shared ReadOnly OtherKeyPin As String = "SHA256:" & New String("C"c, 43)
    Private Shared ReadOnly Pause As TimeSpan = TimeSpan.FromSeconds(1)

    Private NotInheritable Class Rig
        Public ReadOnly Time As New ManualAdminTime()
        Public ReadOnly Handshake As New ScriptedAdminHandshake()
        Public ReadOnly Lines As New List(Of String)()
        Public ReadOnly Phases As New List(Of RemoteAdminPhase)()
        Public Port As ExchangeDeviceAgentTests.MemoryPort
        Public Store As ExchangeAdminTargets
        Public Controller As RemoteAdminController
        Public Reads As Integer

        ''' <summary>The window's read of the masked field.</summary>
        Public Function ReadField() As Char()
            Reads += 1
            Return Secret.ToCharArray()
        End Function

        Public Function Add(name As String, address As String) As ExchangeAdminTarget
            Dim target As ExchangeAdminTarget = Nothing
            Assert.Equal(ExchangeAdminTargetResult.Saved, Store.Add(name, address, target))
            Return target
        End Function
    End Class

    Private Shared Function NewRig() As Rig
        Dim rig As New Rig()
        rig.Port = New ExchangeDeviceAgentTests.MemoryPort()
        rig.Store = New ExchangeAdminTargets(rig.Port)
        rig.Controller = New RemoteAdminController(rig.Store, rig.Handshake, AddressOf rig.Time.Now, AddressOf rig.Time.Delay, Sub(line As String) rig.Lines.Add(line))
        AddHandler rig.Controller.StateChanged, Sub(sender As Object, e As EventArgs) rig.Phases.Add(rig.Controller.State.Phase)
        Return rig
    End Function

    Private Shared Async Function ChosenRig() As Task(Of Rig)
        Dim rig As Rig = NewRig()
        Await rig.Controller.ChooseTargetAsync(rig.Add("Office", "exchange.test:22022"))
        Return rig
    End Function

    Private Shared Function AuthSteps() As List(Of ExchangeTranscriptStep)
        Return ExchangeTranscriptFile.AdminNative().StreamNamed("admin-ops").Steps.Take(2).ToList()
    End Function

    Private Shared Function AuthToken() As String
        Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(AuthSteps()(1).Json)
            Return doc.RootElement.GetProperty("token").GetString()
        End Using
    End Function

    Private Shared Function JsonOf(env As ExchangeEnvelope) As String
        Return Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(env))
    End Function

    Private Shared Function Bye() As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Expect(JsonOf(New ExchangeEnvelope(ExchangeEnvelopeTypes.Bye)))
    End Function

    Private Shared Function RequestStatus(id As Long) As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Expect(JsonOf(ExchangeAdminWire.Status(AuthToken(), id)))
    End Function

    Private Shared Function ReplyOk(id As Long) As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""admin-reply"",""id"":" & id.ToString(Globalization.CultureInfo.InvariantCulture) & ",""ok"":true,""data"":{""accounts"":2}}")
    End Function

    Private Shared Function RefusedStep(reason As String) As ExchangeTranscriptStep
        Return ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""refused"",""reason"":""" & reason & """}")
    End Function

    ''' <summary>The stream of a sign-in followed by these steps.</summary>
    Private Shared Function SignInStream(ParamArray tail As ExchangeTranscriptStep()) As LockStepStream
        Dim steps As List(Of ExchangeTranscriptStep) = AuthSteps()
        steps.AddRange(tail)
        Return New LockStepStream(steps)
    End Function

    ''' <summary>A rig already signed in on a platform-valid certificate, its stream scripted with these steps.</summary>
    Private Shared Async Function SignedInRig(ParamArray tail As ExchangeTranscriptStep()) As Task(Of Rig)
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(SignInStream(tail))
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
        Return rig
    End Function

    Private Shared Sub WaitUntil(condition As Func(Of Boolean), what As String)
        Assert.True(SpinWait.SpinUntil(condition, 3000), what)
    End Sub

    Private Shared Function StatusCall(controller As RemoteAdminController) As Task(Of RemoteAdminResult(Of RemoteAdminStatusView))
        Return controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.StatusAsync(ct))
    End Function

    ' ---------------------------------------------------------------- dormant until the person clicks

    <Fact>
    Public Sub A_new_controller_is_at_no_target_and_touches_nothing()
        Dim rig As Rig = NewRig()
        Assert.Equal(RemoteAdminPhase.NoTarget, rig.Controller.State.Phase)
        Assert.Empty(rig.Handshake.Calls())
        Assert.Equal(0, rig.Port.Writes)
    End Sub

    <Fact>
    Public Async Function Choosing_a_target_touches_no_network_and_keeps_the_password_field_unread() As Task
        Dim rig As Rig = Await ChosenRig()
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("Office", rig.Controller.State.TargetName)
        Assert.Empty(rig.Handshake.Calls())
        Assert.Equal(0, rig.Reads)
    End Function

    <Fact>
    Public Async Function Without_a_session_a_call_paints_nothing_and_writes_nothing() As Task
        Dim rig As Rig = Await ChosenRig()
        Assert.Null(Await StatusCall(rig.Controller))
        Assert.Empty(rig.Handshake.Calls())
    End Function

    ' ---------------------------------------------------------------- the phases of a sign-in

    <Fact>
    Public Async Function A_platform_valid_certificate_signs_in_through_every_phase_and_stores_no_pin() As Task
        Dim rig As Rig = Await SignedInRig()
        Assert.Equal(1, rig.Reads)
        Assert.Equal({RemoteAdminPhase.SignedOut, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn, RemoteAdminPhase.SignedIn}, rig.Phases.ToArray())
        Assert.True(rig.Controller.State.CardsLive)
        Dim stored As ExchangeAdminTarget = rig.Store.Find(rig.Controller.State.Target.Id)
        Assert.Equal("", stored.LeafPin)
        Assert.Equal("", stored.KeyPin)
        Assert.NotEqual(New DateTime(), stored.LastUsedUtc)
    End Function

    <Fact>
    Public Async Function A_first_contact_asks_before_the_password_is_read_and_yes_writes_the_pins_before_it_signs_in() As Task
        Dim rig As Rig = Await ChosenRig()
        Dim stream As LockStepStream = SignInStream()
        rig.Handshake.EnqueueProbe(False, OtherPin, OtherKeyPin).EnqueueConnect(stream, OtherPin, ExchangeTrustVerdict.PinMatch, OtherKeyPin)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)

        Assert.Equal(RemoteAdminPhase.AskingTrust, rig.Controller.State.Phase)
        Assert.True(rig.Controller.State.ShowsTrustPanel)
        Assert.Equal(OtherPin, rig.Controller.AskedTrust.Fingerprint)
        Assert.Equal(0, rig.Reads)
        Assert.Equal({"probe"}, rig.Handshake.Calls().ToArray())
        Assert.Equal("", rig.Store.Find(rig.Controller.State.Target.Id).LeafPin)

        Await rig.Controller.AnswerTrustAsync(True)

        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
        Assert.Equal(1, rig.Reads)
        Dim stored As ExchangeAdminTarget = rig.Store.Find(rig.Controller.State.Target.Id)
        Assert.Equal(OtherPin, stored.LeafPin)
        Assert.Equal(OtherKeyPin, stored.KeyPin)
        Assert.Equal(stored.LeafPin, rig.Controller.State.Target.LeafPin)
        Assert.Equal({"probe", "connect " & OtherPin & " " & OtherKeyPin}, rig.Handshake.Calls().ToArray())
        Assert.Null(rig.Controller.AskedTrust)
    End Function

    <Fact>
    Public Async Function A_no_to_first_contact_stores_nothing_reads_nothing_and_connects_no_more() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbe(False, OtherPin, OtherKeyPin)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Await rig.Controller.AnswerTrustAsync(False)

        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("", rig.Controller.State.ReasonLine)
        Assert.Equal(0, rig.Reads)
        Assert.Equal("", rig.Store.Find(rig.Controller.State.Target.Id).LeafPin)
        Assert.Equal({"probe"}, rig.Handshake.Calls().ToArray())
    End Function

    <Fact>
    Public Async Function A_changed_certificate_stops_with_both_fingerprints_and_never_reads_the_password() As Task
        Dim rig As Rig = NewRig()
        Dim target As ExchangeAdminTarget = rig.Add("Office", "exchange.test:22022")
        Assert.True(rig.Store.SetPins(target.Id, OtherPin, OtherKeyPin))
        Await rig.Controller.ChooseTargetAsync(rig.Store.Find(target.Id))
        rig.Handshake.EnqueueConnectFailure(ExchangeConnectFailure.PinMismatch, ScriptedTransport.DefaultFingerprint).EnqueueProbe(False)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)

        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal(0, rig.Reads)
        Assert.NotNull(rig.Controller.StoppedByCertificate)
        Assert.Contains(OtherPin, rig.Controller.State.ReasonLine)
        Assert.Contains(ScriptedTransport.DefaultFingerprint, rig.Controller.State.ReasonLine)
        ' Nothing was recorded, and the stop goes away with the forgotten certificate.
        Assert.Equal(OtherPin, rig.Store.Find(target.Id).LeafPin)
        Assert.True(rig.Store.ForgetCertificate(target.Id))
        Await rig.Controller.ReloadTargetAsync()
        Assert.Null(rig.Controller.StoppedByCertificate)
        Assert.Equal("", rig.Controller.State.Target.LeafPin)
    End Function

    <Fact>
    Public Async Function Administration_off_is_its_own_phase_and_try_again_is_a_new_sign_in() As Task
        Dim rig As Rig = Await ChosenRig()
        Dim off As New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0), RefusedStep("admin-disabled")})
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(off)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.Disabled, rig.Controller.State.Phase)
        Assert.True(rig.Controller.State.ShowsDisabledCard)
        Assert.NotEqual("", rig.Controller.State.ReasonLine)
        Assert.Equal(2, rig.Handshake.Calls().Count)

        rig.Handshake.EnqueueProbe(True).EnqueueConnect(SignInStream())
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
        Assert.Equal(2, rig.Reads)
    End Function

    <Fact>
    Public Async Function A_wrong_password_returns_to_signed_out_with_the_local_sentence_and_asks_nothing_again() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0), RefusedStep("bad-credentials")}))
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal(Localization.T("Пароль администратора неверный."), rig.Controller.State.ReasonLine)
        Assert.Equal(1, rig.Reads)
        Assert.False(rig.Controller.SignInHeld)
        Assert.Equal(2, rig.Handshake.Calls().Count)
    End Function

    <Fact>
    Public Async Function A_rate_limited_refusal_holds_sign_in_for_one_second() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0), RefusedStep("rate-limited")}))
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        WaitUntil(Function() rig.Controller.SignInHeld, "the pause never started")
        Assert.Equal(1, rig.Time.PendingCount(Pause))

        ' A click during the pause does nothing at all.
        Dim calls As Integer = rig.Handshake.Calls().Count
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(calls, rig.Handshake.Calls().Count)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)

        Assert.Equal(1, rig.Time.Fire(Pause))
        WaitUntil(Function() Not rig.Controller.SignInHeld, "the pause never ended")
    End Function

    <Fact>
    Public Async Function An_unreachable_server_returns_to_signed_out_with_a_sentence_and_no_password_read() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbeFailure(ExchangeConnectFailure.Network)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.NotEqual("", rig.Controller.State.ReasonLine)
        Assert.Equal(0, rig.Reads)
    End Function

    ' ---------------------------------------------------------------- cancel

    <Fact>
    Public Async Function Cancel_while_checking_the_server_returns_with_no_message_and_the_field_unread() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbeHang()
        Dim signIn As Task = rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(RemoteAdminPhase.CheckingServer, rig.Controller.State.Phase)
        Assert.True(rig.Controller.State.CanCancel)
        WaitUntil(Function() rig.Handshake.Calls().Count = 1, "the probe never started")
        rig.Controller.CancelSignIn()
        Await signIn
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("", rig.Controller.State.ReasonLine)
        Assert.Equal(0, rig.Reads)
    End Function

    <Fact>
    Public Async Function Cancel_while_signing_in_returns_with_no_message_and_the_password_stays_read_once() As Task
        Dim rig As Rig = Await ChosenRig()
        ' The server takes the password and never answers.
        rig.Handshake.EnqueueProbe(True).EnqueueConnect(New LockStepStream(New ExchangeTranscriptStep() {AuthSteps()(0)}))
        Dim signIn As Task = rig.Controller.SignInAsync(AddressOf rig.ReadField)
        WaitUntil(Function() rig.Controller.State.Phase = RemoteAdminPhase.SigningIn, "never reached SigningIn")
        rig.Controller.CancelSignIn()
        Await signIn
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("", rig.Controller.State.ReasonLine)
        Assert.Equal(1, rig.Reads)
    End Function

    ' ---------------------------------------------------------------- the calls and the lapse

    <Fact>
    Public Async Function A_call_goes_through_the_session_and_keeps_the_phase() As Task
        Dim rig As Rig = Await SignedInRig(RequestStatus(1), ReplyOk(1))
        Dim status As RemoteAdminResult(Of RemoteAdminStatusView) = Await StatusCall(rig.Controller)
        Assert.True(status.IsDone)
        Assert.Equal(2, status.Value.Accounts.Value)
        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
    End Function

    <Fact>
    Public Async Function A_lapse_moves_to_signed_out_with_its_sentence_and_never_signs_in_again() As Task
        Dim rig As Rig = Await SignedInRig(RequestStatus(1), ExchangeTranscriptStep.Closed())
        Dim calls As Integer = rig.Handshake.Calls().Count
        Dim status As RemoteAdminResult(Of RemoteAdminStatusView) = Await StatusCall(rig.Controller)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Lapsed, status.Kind)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal(RemoteAdminWords.SessionEnded(), rig.Controller.State.ReasonLine)
        Assert.Equal("Office", rig.Controller.State.TargetName)
        Assert.Null(rig.Controller.State.Session)
        Assert.Equal(1, rig.Reads)
        Assert.Equal(calls, rig.Handshake.Calls().Count)
        Assert.Null(Await StatusCall(rig.Controller))
    End Function

    <Fact>
    Public Async Function A_refused_call_keeps_the_session_alive() As Task
        Dim rig As Rig = Await SignedInRig(RequestStatus(1),
            ExchangeTranscriptStep.Supply("{""schemaVersion"":2,""type"":""admin-reply"",""id"":1,""ok"":false,""code"":""capacity"",""error"":""x""}"))
        Dim status As RemoteAdminResult(Of RemoteAdminStatusView) = Await StatusCall(rig.Controller)
        Assert.Equal(ExchangeRemoteAdminOutcomeKind.Failed, status.Kind)
        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
    End Function

    ' ---------------------------------------------------------------- leaving

    <Fact>
    Public Async Function Sign_out_writes_bye_and_returns_to_signed_out() As Task
        Dim rig As Rig = Await SignedInRig(Bye(), ExchangeTranscriptStep.Closed())
        Await rig.Controller.EndSessionAsync()
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("", rig.Controller.State.ReasonLine)
        Assert.Null(rig.Controller.State.Session)
        rig.Handshake.Verify()
    End Function

    <Fact>
    Public Async Function A_lock_ends_the_session_with_the_cause_in_the_reason_line() As Task
        Dim rig As Rig = Await SignedInRig(Bye(), ExchangeTranscriptStep.Closed())
        Dim reason As String = RemoteAdminWords.SessionEnded(RemoteAdminWords.CauseScreenLocked())
        Await rig.Controller.EndSessionAsync(reason)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal(reason, rig.Controller.State.ReasonLine)
        Assert.Contains(RemoteAdminWords.CauseScreenLocked(), rig.Controller.State.ReasonLine)
        rig.Handshake.Verify()
    End Function

    <Fact>
    Public Async Function A_target_switch_ends_the_old_session_first_and_makes_a_new_generation() As Task
        Dim rig As Rig = Await SignedInRig(Bye(), ExchangeTranscriptStep.Closed())
        Dim old As RemoteAdminState = rig.Controller.State
        Dim other As ExchangeAdminTarget = rig.Add("Branch", "branch.test:22022")
        Assert.True(old.CanChooseTarget)
        Await rig.Controller.ChooseTargetAsync(other)
        Assert.NotSame(old, rig.Controller.State)
        Assert.Equal(old.Generation + 1L, rig.Controller.State.Generation)
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("Branch", rig.Controller.State.TargetName)
        rig.Handshake.Verify()
    End Function

    <Fact>
    Public Async Function A_reply_that_arrives_after_a_target_switch_is_dropped() As Task
        ' The status request goes out and nothing answers it; the bye of the switch ends it.
        Dim rig As Rig = Await SignedInRig(RequestStatus(1), Bye(), ExchangeTranscriptStep.Closed())
        Dim pending As Task(Of RemoteAdminResult(Of RemoteAdminStatusView)) = StatusCall(rig.Controller)
        WaitUntil(Function() rig.Time.PendingCount(ExchangeRemoteAdminSession.RequestBound) > 0, "the call never went out")
        Await rig.Controller.ChooseTargetAsync(rig.Add("Branch", "branch.test:22022"))
        Assert.Null(Await pending)
        Assert.Equal("Branch", rig.Controller.State.TargetName)
    End Function

    <Fact>
    Public Async Function A_sign_in_overtaken_by_a_target_switch_ends_its_session_and_paints_nothing() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbeHang()
        Dim signIn As Task = rig.Controller.SignInAsync(AddressOf rig.ReadField)
        WaitUntil(Function() rig.Handshake.Calls().Count = 1, "the probe never started")
        Await rig.Controller.ChooseTargetAsync(rig.Add("Branch", "branch.test:22022"))
        Await signIn
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)
        Assert.Equal("Branch", rig.Controller.State.TargetName)
        Assert.Equal("", rig.Controller.State.ReasonLine)
        Assert.Equal(0, rig.Reads)
    End Function

    <Fact>
    Public Async Function A_second_attempt_is_not_confused_with_a_cancelled_first_one() As Task
        Dim rig As Rig = Await ChosenRig()
        rig.Handshake.EnqueueProbeHang()
        Dim first As Task = rig.Controller.SignInAsync(AddressOf rig.ReadField)
        WaitUntil(Function() rig.Handshake.Calls().Count = 1, "the probe never started")
        Await rig.Controller.EndSessionAsync()
        Assert.Equal(RemoteAdminPhase.SignedOut, rig.Controller.State.Phase)

        rig.Handshake.EnqueueProbe(True).EnqueueConnect(SignInStream())
        Dim second As Task = rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Await Task.WhenAll(first, second)
        Assert.Equal(RemoteAdminPhase.SignedIn, rig.Controller.State.Phase)
        Assert.Equal(1, rig.Reads)
    End Function

    <Fact>
    Public Async Function Dispose_says_bye_raises_nothing_and_can_be_repeated() As Task
        Dim rig As Rig = Await SignedInRig(Bye(), ExchangeTranscriptStep.Closed())
        Dim stream As LockStepStream = rig.Handshake.Streams()(0)
        Dim raised As Integer = rig.Phases.Count
        rig.Controller.Dispose()
        rig.Controller.Dispose()
        WaitUntil(Function()
                      Try
                          rig.Handshake.Verify()
                          Return True
                      Catch ex As Exception
                          Return False
                      End Try
                  End Function, "the bye was never written")
        Assert.Equal(raised, rig.Phases.Count)
        Await rig.Controller.SignInAsync(AddressOf rig.ReadField)
        Assert.Equal(raised, rig.Phases.Count)
        Assert.NotNull(stream)
    End Function

    <Fact>
    Public Async Function The_log_holds_no_password_token_address_name_or_fingerprint() As Task
        Dim rig As Rig = Await SignedInRig(RequestStatus(1), ReplyOk(1), Bye(), ExchangeTranscriptStep.Closed())
        Await StatusCall(rig.Controller)
        Await rig.Controller.EndSessionAsync()
        Dim all As String = String.Join(vbLf, rig.Lines)
        For Each secretWord As String In {Secret, AuthToken(), "exchange.test", "22022", "Office", ScriptedTransport.DefaultFingerprint}
            Assert.DoesNotContain(secretWord, all)
        Next
    End Function

    ' ---------------------------------------------------------------- one call at a time, and the exit's wait

    ''' <summary>A session takes one request at a time and answers a second with "busy": two cards reading at once wait their turn here, so
    ''' neither of them shows a refusal the person did nothing to cause.</summary>
    <Fact>
    Public Async Function Two_calls_at_once_both_finish_in_their_order() As Task
        Dim rig As Rig = Await SignedInRig(RequestStatus(1), ReplyOk(1), RequestStatus(2), ReplyOk(2), Bye(), ExchangeTranscriptStep.Closed())
        ' The first call holds the session while the second is asked for: a second request now would be answered Busy.
        Dim hold As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim secondStarted As Boolean = False
        Dim first As Task(Of RemoteAdminResult(Of RemoteAdminStatusView)) = rig.Controller.CallAsync(Of RemoteAdminResult(Of RemoteAdminStatusView))(
            Async Function(ops As RemoteAdminOps, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminStatusView))
                Await hold.Task
                Return Await ops.StatusAsync(ct)
            End Function)
        Dim second As Task(Of RemoteAdminResult(Of RemoteAdminStatusView)) = rig.Controller.CallAsync(Of RemoteAdminResult(Of RemoteAdminStatusView))(
            Function(ops As RemoteAdminOps, ct As CancellationToken)
                secondStarted = True
                Return ops.StatusAsync(ct)
            End Function)
        Await Task.Delay(30)
        Assert.False(secondStarted, "the second call must wait for the first")
        hold.SetResult(True)
        Await Task.WhenAll(first, second)
        Assert.True(first.Result.IsDone)
        Assert.True(second.Result.IsDone)
        Await rig.Controller.EndSessionAsync()
        rig.Handshake.Verify()
    End Function

    <Fact>
    Public Async Function The_exit_wait_holds_while_a_goodbye_runs_and_lets_go_when_its_bound_ends_it() As Task
        ' The goodbye is sent and never answered: only the session's own bound ends it.
        Dim rig As Rig = Await SignedInRig(Bye())
        rig.Controller.Dispose()
        WaitUntil(Function() rig.Time.PendingCount(ExchangeRemoteAdminSession.ByeBound) > 0, "the goodbye never started its bound")
        Assert.False(RemoteAdminController.WaitForEndings(TimeSpan.FromMilliseconds(50)), "the wait must hold while the goodbye runs")
        Assert.Equal(1, rig.Time.Fire(ExchangeRemoteAdminSession.ByeBound))
        Assert.True(RemoteAdminController.WaitForEndings(TimeSpan.FromSeconds(3)), "the wait must let go when the bound ended the goodbye")
    End Function

End Class
