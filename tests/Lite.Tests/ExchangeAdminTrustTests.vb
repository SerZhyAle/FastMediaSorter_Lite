#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' L0278 T-1, specification 5.4: the trust decision of a remote-administration sign-in. Every row of the table is pinned here
''' independently of the code (the expected outcome of each of the twelve rows below is typed, not computed), the "chain valid, another
''' pin held" row leaves the pin exactly as it was, and the thin resolver is shown to go pinned-connect first, probe once on a
''' mismatch, probe first without a pin, and never to write a byte. No socket is opened.
''' </summary>
Public Class ExchangeAdminTrustTests

    Private Shared Function Fp(c As Char) As String
        Return "SHA256:" & New String(c, 43)
    End Function

    Private Shared ReadOnly LeafA As String = Fp("A"c)
    Private Shared ReadOnly LeafB As String = Fp("B"c)
    Private Shared ReadOnly KeyA As String = Fp("K"c)
    Private Shared ReadOnly KeyB As String = Fp("L"c)

    ' ---------------------------------------------------------------- the table of 5.4, row by row

    ''' <summary>The rows: (a pin is held, the platform validates the chain, the presented leaf equals the pin, the presented key
    ''' equals the key pin) -> the outcome. Without a pin the last two columns cannot matter and are shown both ways (four rows); with a
    ''' pin all eight combinations are listed.</summary>
    <Theory>
    <InlineData(False, True, False, False, "PlatformValid")>
    <InlineData(False, True, True, True, "PlatformValid")>
    <InlineData(False, False, False, False, "AskFirstContact")>
    <InlineData(False, False, True, True, "AskFirstContact")>
    <InlineData(True, True, False, False, "PlatformValid")>
    <InlineData(True, True, True, False, "PinMatch")>
    <InlineData(True, True, False, True, "PinMatch")>
    <InlineData(True, True, True, True, "PinMatch")>
    <InlineData(True, False, True, False, "PinMatch")>
    <InlineData(True, False, False, True, "PinMatch")>
    <InlineData(True, False, True, True, "PinMatch")>
    <InlineData(True, False, False, False, "StopChanged")>
    Public Sub Decide_follows_the_table_of_5_4(held As Boolean, chainValid As Boolean, leafEquals As Boolean, keyMatches As Boolean, expected As String)
        Dim presented As String = If(leafEquals, LeafA, LeafB)
        Dim presentedKey As String = If(keyMatches, KeyA, KeyB)
        Dim d As ExchangeAdminTrustDecision = If(held,
            ExchangeAdminTrust.Decide(chainValid, presented, presentedKey, LeafA, KeyA),
            ExchangeAdminTrust.Decide(chainValid, presented, presentedKey, "", ""))
        Assert.Equal(expected, d.Outcome.ToString())
        Assert.Equal(expected = "PlatformValid" OrElse expected = "PinMatch", d.IsAccepted)
    End Sub

    <Fact>
    Public Sub Chain_valid_with_another_pin_held_accepts_on_the_platform_and_leaves_the_pin_as_it_is()
        Dim d As ExchangeAdminTrustDecision = ExchangeAdminTrust.Decide(True, LeafB, KeyB, LeafA, KeyA)
        Assert.Equal(ExchangeAdminTrustOutcome.PlatformValid, d.Outcome)
        Assert.True(d.IsAccepted)
        ' the decision carries the held pin unchanged and nothing that could replace it: the record is the caller's to leave alone
        Assert.Equal(LeafA, d.PinnedFingerprint)
        Assert.Equal(LeafB, d.Fingerprint)
        Assert.Equal(KeyB, d.KeyPin)
    End Sub

    <Fact>
    Public Sub First_contact_carries_the_fingerprint_and_the_key_pin_to_the_question_and_pins_nothing()
        Dim d As ExchangeAdminTrustDecision = ExchangeAdminTrust.Decide(False, LeafB, KeyB, "", "")
        Assert.Equal(ExchangeAdminTrustOutcome.AskFirstContact, d.Outcome)
        Assert.False(d.IsAccepted)
        Assert.Equal(LeafB, d.Fingerprint)
        Assert.Equal(KeyB, d.KeyPin)
        Assert.Equal("", d.PinnedFingerprint)
    End Sub

    <Fact>
    Public Sub A_changed_leaf_stops_with_both_fingerprints()
        Dim d As ExchangeAdminTrustDecision = ExchangeAdminTrust.Decide(False, LeafB, KeyB, LeafA, KeyA)
        Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, d.Outcome)
        Assert.False(d.IsAccepted)
        Assert.Equal(LeafA, d.PinnedFingerprint)
        Assert.Equal(LeafB, d.Fingerprint)
    End Sub

    <Fact>
    Public Sub A_leaf_pin_alone_never_matches_on_an_empty_or_malformed_key_pin()
        ' a legacy target (leaf pin, no key pin): a changed leaf is a stop whatever key it shows
        Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, ExchangeAdminTrust.Decide(False, LeafB, KeyB, LeafA, "").Outcome)
        Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, ExchangeAdminTrust.Decide(False, LeafB, "", LeafA, "").Outcome)
        ' a held key pin that is not in the pin form is no pin, even when the presented key is the same text
        Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, ExchangeAdminTrust.Decide(False, LeafB, "not-a-pin", LeafA, "not-a-pin").Outcome)
        ' a key pin without a leaf pin is not "held" (the leaf pin is the held pin, as in ExchangeTrust.Evaluate)
        Assert.Equal(ExchangeAdminTrustOutcome.AskFirstContact, ExchangeAdminTrust.Decide(False, LeafB, KeyA, "", KeyA).Outcome)
    End Sub

    <Fact>
    Public Sub Decide_takes_Nothing_for_every_text_without_throwing_and_never_matches_it()
        Dim d As ExchangeAdminTrustDecision = ExchangeAdminTrust.Decide(False, Nothing, Nothing, LeafA, KeyA)
        Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, d.Outcome)
        Assert.Equal("", d.Fingerprint)
        Assert.Equal(ExchangeAdminTrustOutcome.AskFirstContact, ExchangeAdminTrust.Decide(False, Nothing, Nothing, Nothing, Nothing).Outcome)
        Assert.Equal(ExchangeAdminTrustOutcome.PlatformValid, ExchangeAdminTrust.Decide(True, Nothing, Nothing, Nothing, Nothing).Outcome)
    End Sub

    ' ---------------------------------------------------------------- the additive KeyPin of the probe result

    <Fact>
    Public Sub The_probe_result_gains_a_key_pin_and_the_two_member_form_keeps_working()
        Dim old As New ExchangeCertificateInfo(LeafA, True)
        Assert.Equal(LeafA, old.Fingerprint)
        Assert.True(old.PlatformValid)
        Assert.Equal("", old.KeyPin)
        Dim info As New ExchangeCertificateInfo(LeafA, False, KeyA)
        Assert.False(info.PlatformValid)
        Assert.Equal(KeyA, info.KeyPin)
    End Sub

    ' ---------------------------------------------------------------- F-1: the exchange client's rule is untouched

    <Fact>
    Public Sub F1_ExchangeTrust_Evaluate_is_still_pin_first_which_is_why_the_admin_decision_does_not_use_it()
        Assert.Equal(ExchangeTrustVerdict.PinMismatch, ExchangeTrust.Evaluate(True, LeafB, LeafA))
    End Sub

    ' ---------------------------------------------------------------- the resolver, over a fake handshake

    Private NotInheritable Class FakeHandshake
        Implements IExchangeAdminHandshake

        Public ReadOnly Calls As New List(Of String)()
        Public ConnectException As ExchangeConnectException
        Public ConnectResult As ExchangeTlsConnection
        Public ProbeResult As ExchangeCertificateInfo
        Public ConnectedPin As String = ""
        Public ConnectedKeyPin As String = ""

        Public Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
            Calls.Add("connect")
            ConnectedPin = leafPin
            ConnectedKeyPin = keyPin
            If ConnectException IsNot Nothing Then Throw ConnectException
            Return Task.FromResult(ConnectResult)
        End Function

        Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
            Calls.Add("probe")
            Return Task.FromResult(ProbeResult)
        End Function
    End Class

    <Fact>
    Public Async Function A_held_pin_that_matches_connects_once_hands_the_connection_over_and_sends_nothing() As Task
        Dim wire As New MemoryStream()
        Dim fake As New FakeHandshake() With {.ConnectResult = New ExchangeTlsConnection(wire, LeafA, ExchangeTrustVerdict.PinMatch, KeyA)}
        Using res As ExchangeAdminTrustResolution = Await ExchangeAdminTrust.ResolveAsync(fake, "h.example", 22022, LeafA, KeyA, CancellationToken.None)
            Assert.Equal(New String() {"connect"}, fake.Calls.ToArray())
            Assert.Equal(LeafA, fake.ConnectedPin)
            Assert.Equal(KeyA, fake.ConnectedKeyPin)
            Assert.Equal(ExchangeAdminTrustOutcome.PinMatch, res.Decision.Outcome)
            Assert.Same(fake.ConnectResult, res.Connection)
            Assert.Equal(0L, wire.Length)
        End Using
        Assert.False(wire.CanWrite)
    End Function

    <Fact>
    Public Async Function A_mismatch_on_the_pinned_connect_is_followed_by_one_probe_and_a_valid_chain_accepts_without_touching_the_pin() As Task
        Dim fake As New FakeHandshake() With {
            .ConnectException = New ExchangeConnectException(ExchangeConnectFailure.PinMismatch, LeafB, "mismatch"),
            .ProbeResult = New ExchangeCertificateInfo(LeafB, True, KeyB)}
        Using res As ExchangeAdminTrustResolution = Await ExchangeAdminTrust.ResolveAsync(fake, "h.example", 22022, LeafA, KeyA, CancellationToken.None)
            Assert.Equal(New String() {"connect", "probe"}, fake.Calls.ToArray())
            Assert.Equal(ExchangeAdminTrustOutcome.PlatformValid, res.Decision.Outcome)
            Assert.Equal(LeafA, res.Decision.PinnedFingerprint)
            Assert.Null(res.Connection)
        End Using
    End Function

    <Fact>
    Public Async Function A_mismatch_with_a_chain_that_is_not_valid_stops_with_both_fingerprints() As Task
        Dim fake As New FakeHandshake() With {
            .ConnectException = New ExchangeConnectException(ExchangeConnectFailure.PinMismatch, LeafB, "mismatch"),
            .ProbeResult = New ExchangeCertificateInfo(LeafB, False, KeyB)}
        Using res As ExchangeAdminTrustResolution = Await ExchangeAdminTrust.ResolveAsync(fake, "h.example", 22022, LeafA, KeyA, CancellationToken.None)
            Assert.Equal(New String() {"connect", "probe"}, fake.Calls.ToArray())
            Assert.Equal(ExchangeAdminTrustOutcome.StopChanged, res.Decision.Outcome)
            Assert.Equal(LeafA, res.Decision.PinnedFingerprint)
            Assert.Equal(LeafB, res.Decision.Fingerprint)
            Assert.Null(res.Connection)
        End Using
    End Function

    <Fact>
    Public Async Function Without_a_pin_the_probe_comes_first_and_alone() As Task
        Dim untrusted As New FakeHandshake() With {.ProbeResult = New ExchangeCertificateInfo(LeafB, False, KeyB)}
        Using res As ExchangeAdminTrustResolution = Await ExchangeAdminTrust.ResolveAsync(untrusted, "h.example", 22022, "", "", CancellationToken.None)
            Assert.Equal(New String() {"probe"}, untrusted.Calls.ToArray())
            Assert.Equal(ExchangeAdminTrustOutcome.AskFirstContact, res.Decision.Outcome)
            Assert.Equal(LeafB, res.Decision.Fingerprint)
            Assert.Equal(KeyB, res.Decision.KeyPin)
            Assert.Null(res.Connection)
        End Using
        Dim trusted As New FakeHandshake() With {.ProbeResult = New ExchangeCertificateInfo(LeafB, True, KeyB)}
        Using res As ExchangeAdminTrustResolution = Await ExchangeAdminTrust.ResolveAsync(trusted, "h.example", 22022, Nothing, Nothing, CancellationToken.None)
            Assert.Equal(New String() {"probe"}, trusted.Calls.ToArray())
            Assert.Equal(ExchangeAdminTrustOutcome.PlatformValid, res.Decision.Outcome)
        End Using
    End Function

    <Fact>
    Public Async Function Any_other_connect_failure_is_not_caught_and_no_probe_follows() As Task
        Dim fake As New FakeHandshake() With {.ConnectException = New ExchangeConnectException(ExchangeConnectFailure.Network, "", "no route")}
        Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
            Function() ExchangeAdminTrust.ResolveAsync(fake, "h.example", 22022, LeafA, KeyA, CancellationToken.None))
        Assert.Equal(ExchangeConnectFailure.Network, ex.Failure)
        Assert.Equal(New String() {"connect"}, fake.Calls.ToArray())
    End Function

End Class
#End If
