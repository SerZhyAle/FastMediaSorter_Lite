Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-5, specification section 7.3 (row <c>RemoteAdminStateTests</c>) and 5.3, 5.6, 5.6.1: the transition table of the
''' remote administration window (every phase against every event), a target switch that builds a NEW state with the next generation,
''' a reply of an older generation that is dropped, the controls that follow the phase, and the banner - its ink and its text - as a pure
''' function of the state: one snapshot per phase in English, and in each of the 13 languages a text that is not empty and holds the
''' target name and host:port. No UI, no registry, no network: the session of the one test that needs one is built over a handshake
''' that is never called.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class RemoteAdminStateTests

    ' ---------------------------------------------------------------- helpers

    Private Shared Function Target(Optional name As String = "Home", Optional host As String = "192.168.1.5", Optional port As Integer = 443) As ExchangeAdminTarget
        Return New ExchangeAdminTarget("00000000-0000-0000-0000-000000000001", name, host, port, "", "", DateTime.MinValue)
    End Function

    Private Shared Function AllPhases() As RemoteAdminPhase()
        Return [Enum].GetValues(GetType(RemoteAdminPhase)).Cast(Of RemoteAdminPhase)().ToArray()
    End Function

    Private Shared Function AllEvents() As RemoteAdminEvent()
        Return [Enum].GetValues(GetType(RemoteAdminEvent)).Cast(Of RemoteAdminEvent)().ToArray()
    End Function

    ''' <summary>A state with a target, walked to the phase by the events the window itself raises.</summary>
    Private Shared Function StateIn(phase As RemoteAdminPhase) As RemoteAdminState
        Dim s As RemoteAdminState = RemoteAdminState.Initial()
        If phase = RemoteAdminPhase.NoTarget Then Return s
        s = RemoteAdminState.ForTarget(s, Target())
        Select Case phase
            Case RemoteAdminPhase.CheckingServer
                s.Apply(RemoteAdminEvent.SignInPressed)
            Case RemoteAdminPhase.AskingTrust
                s.Apply(RemoteAdminEvent.SignInPressed)
                s.Apply(RemoteAdminEvent.TrustAsked)
            Case RemoteAdminPhase.SigningIn
                s.Apply(RemoteAdminEvent.SignInPressed)
                s.Apply(RemoteAdminEvent.DecisionAccepted)
            Case RemoteAdminPhase.SignedIn
                s.Apply(RemoteAdminEvent.SignInPressed)
                s.Apply(RemoteAdminEvent.DecisionAccepted)
                s.Apply(RemoteAdminEvent.SignInOk)
            Case RemoteAdminPhase.Disabled
                s.Apply(RemoteAdminEvent.SignInPressed)
                s.Apply(RemoteAdminEvent.SignInDisabled)
        End Select
        Assert.Equal(phase, s.Phase)
        Return s
    End Function

    <Fact>
    Public Sub Replacing_the_target_keeps_the_generation_and_refuses_another_id()
        Dim s As RemoteAdminState = RemoteAdminState.ForTarget(RemoteAdminState.Initial(), Target())
        Dim generation As Long = s.Generation
        Assert.True(s.ReplaceTarget(Target(name:="Renamed").WithPins("SHA256:leaf", "SHA256:key")))
        Assert.Equal(generation, s.Generation)
        Assert.Equal("Renamed", s.TargetName)
        Assert.Equal("SHA256:leaf", s.Target.LeafPin)
        Dim other As New ExchangeAdminTarget("00000000-0000-0000-0000-000000000002", "Other", "192.168.1.6", 443, "", "", DateTime.MinValue)
        Assert.False(s.ReplaceTarget(other))
        Assert.Equal("Renamed", s.TargetName)
    End Sub

    Private Shared Sub InEachLanguage(body As Action(Of String))
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                body(code)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    Private Shared Sub InEnglish(body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            body()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>A handshake that is never called: enough to build a session object, which this test never signs in.</summary>
    Private NotInheritable Class NoHandshake
        Implements IExchangeAdminHandshake

        Public Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
            Throw New NotSupportedException()
        End Function

        Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
            Throw New NotSupportedException()
        End Function
    End Class

    Private Shared Function NewSession() As ExchangeRemoteAdminSession
        Return New ExchangeRemoteAdminSession(New NoHandshake(), Function() 0L, Function(span, ct) Task.CompletedTask, Nothing)
    End Function

    ' ---------------------------------------------------------------- the table of 5.6

    ''' <summary>The table of 5.6 written down a second time, as data: event, the phases it applies to, the phase it leads to.</summary>
    Private Shared Function ExpectedTable() As Dictionary(Of String, RemoteAdminPhase)
        Dim table As New Dictionary(Of String, RemoteAdminPhase)(StringComparer.Ordinal)
        Dim row As Action(Of RemoteAdminEvent, RemoteAdminPhase, RemoteAdminPhase()) =
            Sub(ev, destination, origins)
                For Each origin As RemoteAdminPhase In origins
                    table(origin.ToString() & "|" & ev.ToString()) = destination
                Next
            End Sub
        row(RemoteAdminEvent.TargetChosen, RemoteAdminPhase.SignedOut, {RemoteAdminPhase.NoTarget, RemoteAdminPhase.SignedOut, RemoteAdminPhase.Disabled})
        row(RemoteAdminEvent.TargetCleared, RemoteAdminPhase.NoTarget, AllPhases())
        row(RemoteAdminEvent.SignInPressed, RemoteAdminPhase.CheckingServer, {RemoteAdminPhase.SignedOut, RemoteAdminPhase.Disabled})
        row(RemoteAdminEvent.TrustAsked, RemoteAdminPhase.AskingTrust, {RemoteAdminPhase.CheckingServer})
        row(RemoteAdminEvent.TrustAccepted, RemoteAdminPhase.CheckingServer, {RemoteAdminPhase.AskingTrust})
        row(RemoteAdminEvent.TrustDeclined, RemoteAdminPhase.SignedOut, {RemoteAdminPhase.AskingTrust})
        row(RemoteAdminEvent.DecisionAccepted, RemoteAdminPhase.SigningIn, {RemoteAdminPhase.CheckingServer})
        row(RemoteAdminEvent.SignInOk, RemoteAdminPhase.SignedIn, {RemoteAdminPhase.SigningIn})
        row(RemoteAdminEvent.SignInDisabled, RemoteAdminPhase.Disabled, {RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn})
        row(RemoteAdminEvent.SignInFailed, RemoteAdminPhase.SignedOut, {RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn})
        row(RemoteAdminEvent.Cancelled, RemoteAdminPhase.SignedOut, {RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn})
        row(RemoteAdminEvent.SessionEnded, RemoteAdminPhase.SignedOut, {RemoteAdminPhase.SignedIn})
        Return table
    End Function

    <Fact>
    Public Sub The_enumerations_hold_exactly_the_members_the_table_knows()
        ' a new member must come with a row of the table and a test: this fails first and asks for a review
        Assert.Equal({"NoTarget", "SignedOut", "CheckingServer", "AskingTrust", "SigningIn", "SignedIn", "Disabled"}, [Enum].GetNames(GetType(RemoteAdminPhase)))
        Assert.Equal({"TargetChosen", "TargetCleared", "SignInPressed", "TrustAsked", "TrustAccepted", "TrustDeclined", "DecisionAccepted",
                      "SignInOk", "SignInDisabled", "SignInFailed", "Cancelled", "SessionEnded"}, [Enum].GetNames(GetType(RemoteAdminEvent)))
        Assert.Equal({"Info", "Warning"}, [Enum].GetNames(GetType(RemoteAdminInk)))
    End Sub

    <Fact>
    Public Sub Every_phase_against_every_event_follows_the_table_of_5_6()
        Dim table As Dictionary(Of String, RemoteAdminPhase) = ExpectedTable()
        Assert.Equal(24, table.Count)
        Dim checkedPairs As Integer = 0
        For Each phase As RemoteAdminPhase In AllPhases()
            For Each ev As RemoteAdminEvent In AllEvents()
                Dim key As String = phase.ToString() & "|" & ev.ToString()
                Dim expected As RemoteAdminPhase = phase
                Dim applies As Boolean = table.TryGetValue(key, expected)
                If Not applies Then expected = phase
                Assert.True(expected = RemoteAdminTransitions.Next(phase, ev), key & " leads to " & RemoteAdminTransitions.Next(phase, ev).ToString() & ", expected " & expected.ToString())
                Dim result As RemoteAdminPhase
                Assert.Equal(applies, RemoteAdminTransitions.TryNext(phase, ev, result))
                Assert.Equal(expected, result)
                checkedPairs += 1
            Next
        Next
        Assert.Equal(7 * 12, checkedPairs)
    End Sub

    <Fact>
    Public Sub An_event_that_does_not_apply_returns_the_phase_unchanged_and_nothing_throws()
        Dim unknownPhase As RemoteAdminPhase = CType(99, RemoteAdminPhase)
        Dim unknownEvent As RemoteAdminEvent = CType(99, RemoteAdminEvent)
        For Each ev As RemoteAdminEvent In AllEvents()
            ' a phase the table does not know: only TargetCleared applies (to any phase), the rest return it unchanged
            Dim after As RemoteAdminPhase = RemoteAdminTransitions.Next(unknownPhase, ev)
            Assert.Equal(If(ev = RemoteAdminEvent.TargetCleared, RemoteAdminPhase.NoTarget, unknownPhase), after)
        Next
        For Each phase As RemoteAdminPhase In AllPhases()
            Assert.Equal(phase, RemoteAdminTransitions.Next(phase, unknownEvent))
        Next
        ' the spot checks of the brief: each is a nonsense for the phase and is ignored
        Assert.Equal(RemoteAdminPhase.SignedIn, RemoteAdminTransitions.Next(RemoteAdminPhase.SignedIn, RemoteAdminEvent.SignInPressed))
        Assert.Equal(RemoteAdminPhase.SignedOut, RemoteAdminTransitions.Next(RemoteAdminPhase.SignedOut, RemoteAdminEvent.SignInOk))
        Assert.Equal(RemoteAdminPhase.CheckingServer, RemoteAdminTransitions.Next(RemoteAdminPhase.CheckingServer, RemoteAdminEvent.TargetChosen))
        Assert.Equal(RemoteAdminPhase.AskingTrust, RemoteAdminTransitions.Next(RemoteAdminPhase.AskingTrust, RemoteAdminEvent.Cancelled))
    End Sub

    <Fact>
    Public Sub A_state_applies_the_same_table()
        Dim table As Dictionary(Of String, RemoteAdminPhase) = ExpectedTable()
        For Each phase As RemoteAdminPhase In AllPhases()
            If phase = RemoteAdminPhase.NoTarget Then Continue For ' a state without a target takes no event, see below
            For Each ev As RemoteAdminEvent In AllEvents()
                Dim s As RemoteAdminState = StateIn(phase)
                s.Apply(ev, "r")
                Dim expected As RemoteAdminPhase = phase
                Dim applies As Boolean = table.TryGetValue(phase.ToString() & "|" & ev.ToString(), expected)
                Assert.Equal(If(applies, expected, phase), s.Phase)
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_way_through_a_first_contact_to_a_live_session_and_back()
        Dim s As RemoteAdminState = RemoteAdminState.Initial()
        Assert.Equal(RemoteAdminPhase.NoTarget, s.Phase)
        Assert.Equal(0L, s.Generation)
        Assert.Null(s.Target)
        s = RemoteAdminState.ForTarget(s, Target())
        Assert.Equal(RemoteAdminPhase.SignedOut, s.Phase)
        s.Apply(RemoteAdminEvent.SignInPressed)
        Assert.Equal(RemoteAdminPhase.CheckingServer, s.Phase)
        s.Apply(RemoteAdminEvent.TrustAsked)
        Assert.Equal(RemoteAdminPhase.AskingTrust, s.Phase)
        s.Apply(RemoteAdminEvent.TrustAccepted)
        Assert.Equal(RemoteAdminPhase.CheckingServer, s.Phase)
        s.Apply(RemoteAdminEvent.DecisionAccepted)
        Assert.Equal(RemoteAdminPhase.SigningIn, s.Phase)
        s.Apply(RemoteAdminEvent.SignInOk)
        Assert.Equal(RemoteAdminPhase.SignedIn, s.Phase)
        s.Apply(RemoteAdminEvent.SessionEnded, "ended")
        Assert.Equal(RemoteAdminPhase.SignedOut, s.Phase)
        Assert.Equal("ended", s.ReasonLine)
        Assert.Equal(1L, s.Generation)
    End Sub

    <Fact>
    Public Sub Declining_the_certificate_returns_to_signed_out()
        Dim s As RemoteAdminState = StateIn(RemoteAdminPhase.AskingTrust)
        s.Apply(RemoteAdminEvent.TrustDeclined)
        Assert.Equal(RemoteAdminPhase.SignedOut, s.Phase)
    End Sub

    ' ---------------------------------------------------------------- the reason line

    <Fact>
    Public Sub Cancel_returns_to_signed_out_with_no_reason_and_a_failure_keeps_its_reason()
        For Each phase As RemoteAdminPhase In {RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn}
            Dim cancelled As RemoteAdminState = StateIn(phase)
            cancelled.Apply(RemoteAdminEvent.Cancelled, "must not stick")
            Assert.Equal(RemoteAdminPhase.SignedOut, cancelled.Phase)
            Assert.Equal("", cancelled.ReasonLine)
            Dim failed As RemoteAdminState = StateIn(phase)
            failed.Apply(RemoteAdminEvent.SignInFailed, "refused")
            Assert.Equal(RemoteAdminPhase.SignedOut, failed.Phase)
            Assert.Equal("refused", failed.ReasonLine)
        Next
    End Sub

    <Fact>
    Public Sub The_reason_line_is_replaced_by_the_next_event_that_applies_and_kept_by_one_that_does_not()
        Dim s As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
        s.Apply(RemoteAdminEvent.SessionEnded, "lapsed")
        Assert.Equal("lapsed", s.ReasonLine)
        s.Apply(RemoteAdminEvent.SignInOk, "not for this phase")
        Assert.Equal("lapsed", s.ReasonLine)
        Assert.Equal(RemoteAdminPhase.SignedOut, s.Phase)
        s.Apply(RemoteAdminEvent.SignInPressed)
        Assert.Equal("", s.ReasonLine)
        Assert.Equal(RemoteAdminPhase.CheckingServer, s.Phase)
    End Sub

    <Fact>
    Public Sub Disabled_is_left_by_try_again_or_by_choosing_another_target()
        Dim again As RemoteAdminState = StateIn(RemoteAdminPhase.Disabled)
        again.Apply(RemoteAdminEvent.SignInPressed)
        Assert.Equal(RemoteAdminPhase.CheckingServer, again.Phase)
        Dim chosen As RemoteAdminState = StateIn(RemoteAdminPhase.Disabled)
        chosen.Apply(RemoteAdminEvent.TargetChosen)
        Assert.Equal(RemoteAdminPhase.SignedOut, chosen.Phase)
    End Sub

    ' ---------------------------------------------------------------- the controls of 5.6

    <Fact>
    Public Sub The_controls_follow_the_phase()
        ' phase -> {CanChooseTarget, CanSignIn, CanSignOut, CanCancel, CanEditTarget, CardsLive, ShowsTrustPanel, ShowsDisabledCard}
        Dim expected As New Dictionary(Of RemoteAdminPhase, Boolean())() From {
            {RemoteAdminPhase.NoTarget, {True, False, False, False, False, False, False, False}},
            {RemoteAdminPhase.SignedOut, {True, True, False, False, True, False, False, False}},
            {RemoteAdminPhase.CheckingServer, {False, False, False, True, False, False, False, False}},
            {RemoteAdminPhase.AskingTrust, {False, False, False, False, False, False, True, False}},
            {RemoteAdminPhase.SigningIn, {False, False, False, True, False, False, False, False}},
            {RemoteAdminPhase.SignedIn, {True, False, True, False, False, True, False, False}},
            {RemoteAdminPhase.Disabled, {True, True, False, False, False, False, False, True}}}
        For Each phase As RemoteAdminPhase In AllPhases()
            Dim s As RemoteAdminState = StateIn(phase)
            Dim actual As Boolean() = {s.CanChooseTarget, s.CanSignIn, s.CanSignOut, s.CanCancel, s.CanEditTarget, s.CardsLive, s.ShowsTrustPanel, s.ShowsDisabledCard}
            Assert.True(expected(phase).SequenceEqual(actual), phase.ToString() & ": " & String.Join(",", actual))
        Next
    End Sub

    ' ---------------------------------------------------------------- the target switch and the stale reply

    <Fact>
    Public Sub A_state_without_a_target_takes_no_event()
        Dim s As RemoteAdminState = RemoteAdminState.Initial()
        For Each ev As RemoteAdminEvent In AllEvents()
            s.Apply(ev, "r")
            Assert.Equal(RemoteAdminPhase.NoTarget, s.Phase)
            Assert.Null(s.Target)
            Assert.Equal(0L, s.Generation)
            Assert.Equal("", s.ReasonLine)
        Next
        Assert.False(s.CanSignIn)
    End Sub

    <Fact>
    Public Sub A_target_switch_builds_a_new_state_with_the_next_generation()
        Dim first As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
        Dim firstGeneration As Long = first.Generation
        Dim other As ExchangeAdminTarget = Target("Office", "10.0.0.7", 9443)
        Dim second As RemoteAdminState = RemoteAdminState.ForTarget(first, other)
        Assert.NotSame(first, second)
        Assert.Equal(firstGeneration + 1L, second.Generation)
        Assert.Equal(RemoteAdminPhase.SignedOut, second.Phase)
        Assert.Same(other, second.Target)
        Assert.Null(second.Session)
        Assert.Equal("", second.ReasonLine)
        ' the old object is left as it was: the window ends its session itself
        Assert.Equal(RemoteAdminPhase.SignedIn, first.Phase)
        Assert.Equal(firstGeneration, first.Generation)
        Dim third As RemoteAdminState = RemoteAdminState.ForTarget(second, Target())
        Assert.Equal(second.Generation + 1L, third.Generation)
        ' from nothing: the first state of a window is generation 1 and a cleared one is NoTarget
        Dim fromNothing As RemoteAdminState = RemoteAdminState.ForTarget(Nothing, Target())
        Assert.Equal(1L, fromNothing.Generation)
        Dim cleared As RemoteAdminState = RemoteAdminState.ForTarget(third, Nothing)
        Assert.Equal(RemoteAdminPhase.NoTarget, cleared.Phase)
        Assert.Null(cleared.Target)
        Assert.Equal(third.Generation + 1L, cleared.Generation)
    End Sub

    <Fact>
    Public Sub A_reply_of_an_older_generation_is_dropped()
        Dim first As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
        Dim asked As Long = first.Generation
        Assert.True(first.Accepts(asked))
        Dim second As RemoteAdminState = RemoteAdminState.ForTarget(first, Target("Office", "10.0.0.7", 9443))
        Assert.False(second.Accepts(asked))
        Assert.True(second.Accepts(second.Generation))
        ' the window's own load guard (StaleGuard) is begun on every switch and says the same
        Dim guard As New StaleGuard()
        Dim oldToken As Long = guard.Begin()
        Dim newToken As Long = guard.Begin()
        Assert.False(guard.IsCurrent(oldToken))
        Assert.True(guard.IsCurrent(newToken))
        guard.Invalidate()
        Assert.False(guard.IsCurrent(newToken))
    End Sub

    <Fact>
    Public Sub TargetCleared_forgets_the_target_and_bumps_the_generation()
        Dim s As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
        Dim before As Long = s.Generation
        s.Apply(RemoteAdminEvent.TargetCleared)
        Assert.Equal(RemoteAdminPhase.NoTarget, s.Phase)
        Assert.Null(s.Target)
        Assert.Null(s.Session)
        Assert.Equal(before + 1L, s.Generation)
        Assert.False(s.Accepts(before))
        Assert.Equal("", s.TargetName)
        Assert.Equal("", s.TargetAddress)
    End Sub

    ' ---------------------------------------------------------------- the session reference

    <Fact>
    Public Sub The_session_is_kept_only_while_signing_in_or_signed_in_and_forgotten_on_leaving()
        Using session As ExchangeRemoteAdminSession = NewSession()
            Dim signedOut As RemoteAdminState = StateIn(RemoteAdminPhase.SignedOut)
            Assert.False(signedOut.SetSession(session))
            Assert.Null(signedOut.Session)
            Dim checking As RemoteAdminState = StateIn(RemoteAdminPhase.CheckingServer)
            Assert.False(checking.SetSession(session))

            Dim s As RemoteAdminState = StateIn(RemoteAdminPhase.SigningIn)
            Assert.True(s.SetSession(session))
            s.Apply(RemoteAdminEvent.SignInOk)
            Assert.Same(session, s.Session)
            s.Apply(RemoteAdminEvent.SessionEnded, "lapsed")
            Assert.Null(s.Session)

            Dim cleared As RemoteAdminState = StateIn(RemoteAdminPhase.SigningIn)
            Assert.True(cleared.SetSession(session))
            cleared.Apply(RemoteAdminEvent.SignInOk)
            cleared.Apply(RemoteAdminEvent.TargetCleared)
            Assert.Null(cleared.Session)

            Dim failed As RemoteAdminState = StateIn(RemoteAdminPhase.SigningIn)
            Assert.True(failed.SetSession(session))
            failed.Apply(RemoteAdminEvent.SignInFailed, "x")
            Assert.Null(failed.Session)
        End Using
    End Sub

    ' ---------------------------------------------------------------- the banner

    <Fact>
    Public Sub The_banner_ink_is_warning_only_while_signed_in_and_never_danger()
        For Each phase As RemoteAdminPhase In AllPhases()
            Assert.Equal(If(phase = RemoteAdminPhase.SignedIn, RemoteAdminInk.Warning, RemoteAdminInk.Info), RemoteAdminBanner.Ink(phase))
        Next
        Assert.DoesNotContain("Danger", [Enum].GetNames(GetType(RemoteAdminInk)))
    End Sub

    <Fact>
    Public Sub One_snapshot_of_the_banner_text_per_phase_in_english()
        InEnglish(Sub()
                      Dim head As String = "Server «Home» (192.168.1.5:443): "
                      Assert.Equal("No server selected", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.NoTarget)))
                      Assert.Equal(head & "Not signed in", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.SignedOut)))
                      Assert.Equal(head & "Checking the server's certificate", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.CheckingServer)))
                      Assert.Equal(head & "Waiting for your decision on the certificate", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.AskingTrust)))
                      Assert.Equal(head & "Signing in", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.SigningIn)))
                      Assert.Equal(head & "Signed in", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.SignedIn)))
                      Assert.Equal(head & "Administration is off on this server", RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.Disabled)))
                      Assert.Equal("No server selected", RemoteAdminBanner.Text(Nothing))
                  End Sub)
    End Sub

    <Fact>
    Public Sub The_reason_line_follows_the_banner_text_on_its_own_line()
        InEnglish(Sub()
                      Dim s As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
                      s.Apply(RemoteAdminEvent.SessionEnded, "The session has ended. Sign in again.")
                      Assert.Equal("Server «Home» (192.168.1.5:443): Not signed in" & vbCrLf & "The session has ended. Sign in again.", RemoteAdminBanner.Text(s))
                      Dim none As RemoteAdminState = RemoteAdminState.Initial()
                      Assert.Equal("No server selected", RemoteAdminBanner.Text(none))
                  End Sub)
    End Sub

    <Fact>
    Public Sub The_banner_is_a_pure_function_of_the_state()
        InEnglish(Sub()
                      Dim a As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
                      Dim b As RemoteAdminState = StateIn(RemoteAdminPhase.SignedIn)
                      Dim text As String = RemoteAdminBanner.Text(a)
                      ' the same state, asked again, and another object holding the same facts: the same words
                      Assert.Equal(text, RemoteAdminBanner.Text(a))
                      Assert.Equal(text, RemoteAdminBanner.Text(b))
                      ' a session in hand changes nothing the banner says
                      Using session As ExchangeRemoteAdminSession = NewSession()
                          Dim c As RemoteAdminState = StateIn(RemoteAdminPhase.SigningIn)
                          Dim before As String = RemoteAdminBanner.Text(c)
                          Assert.True(c.SetSession(session))
                          Assert.Equal(before, RemoteAdminBanner.Text(c))
                      End Using
                      ' the name and the address come from the target and from nothing else
                      Dim other As RemoteAdminState = RemoteAdminState.ForTarget(a, Target("Office", "10.0.0.7", 9443))
                      other.Apply(RemoteAdminEvent.SignInPressed)
                      other.Apply(RemoteAdminEvent.DecisionAccepted)
                      other.Apply(RemoteAdminEvent.SignInOk)
                      Assert.Equal("Server «Office» (10.0.0.7:9443): Signed in", RemoteAdminBanner.Text(other))
                      Assert.NotEqual(text, RemoteAdminBanner.Text(other))
                  End Sub)
    End Sub

    <Fact>
    Public Sub In_each_of_13_languages_the_banner_names_the_target_and_its_address()
        Assert.Equal(13, Localization.Codes.Length)
        InEachLanguage(Sub(code)
                           For Each phase As RemoteAdminPhase In AllPhases()
                               Dim s As RemoteAdminState = StateIn(phase)
                               Dim text As String = RemoteAdminBanner.Text(s)
                               Assert.False(String.IsNullOrWhiteSpace(text), code & " " & phase.ToString())
                               If phase = RemoteAdminPhase.NoTarget Then
                                   Assert.DoesNotContain("Home", text)
                                   Assert.DoesNotContain("192.168.1.5", text)
                               Else
                                   Assert.Contains("Home", text)
                                   Assert.Contains("192.168.1.5:443", text)
                               End If
                           Next
                       End Sub)
    End Sub

    <Fact>
    Public Sub The_state_words_differ_from_phase_to_phase_in_every_language()
        InEachLanguage(Sub(code)
                           Dim words As List(Of String) = AllPhases().Select(Function(p) RemoteAdminBanner.StateWords(p)).ToList()
                           Assert.All(words, Sub(w) Assert.False(String.IsNullOrWhiteSpace(w), code))
                           Assert.Equal(AllPhases().Length, words.Distinct(StringComparer.Ordinal).Count())
                       End Sub)
    End Sub

    <Fact>
    Public Sub In_a_right_to_left_language_the_address_is_set_as_a_left_to_right_value()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In {"ar", "ur"}
                Localization.CurrentCode = code
                Dim text As String = RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.SignedIn))
                Assert.Contains(ChrW(&H202A) & "192.168.1.5:443" & ChrW(&H202C), text)
                Assert.Contains("Home", text)
            Next
            Localization.CurrentCode = "de"
            Assert.False(RemoteAdminBanner.Text(StateIn(RemoteAdminPhase.SignedIn)).Contains(ChrW(&H202A).ToString()))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub An_ipv6_address_is_shown_in_brackets_as_the_address_class_writes_it()
        InEnglish(Sub()
                      Dim s As RemoteAdminState = RemoteAdminState.ForTarget(Nothing, Target("Lab", "::1", 443))
                      Assert.Equal("[::1]:443", s.TargetAddress)
                      Assert.Contains("[::1]:443", RemoteAdminBanner.Text(s))
                  End Sub)
    End Sub

    <Fact>
    Public Sub The_state_prints_as_its_type_and_names_no_member()
        Assert.Equal("RemoteAdminState", StateIn(RemoteAdminPhase.SignedIn).ToString())
    End Sub

End Class
