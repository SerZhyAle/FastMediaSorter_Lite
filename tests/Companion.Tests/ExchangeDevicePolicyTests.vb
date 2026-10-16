Option Strict On

Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 180 T-2 (section 5): the pure rules of the control stream - what each refusal of a `hello` means and the bounded backoff - as tables.
''' The state machine that applies them is ExchangeControlSessionTests; here the tables are pinned by value, so a changed number is a failing test.
''' </summary>
Public Class ExchangeDevicePolicyTests

    <Theory>
    <InlineData("BadCredentials", True, "SignInAgain", False, 0, False)>
    <InlineData("DeviceRevoked", True, "SignInAgain", True, 0, False)>
    <InlineData("SignedOut", True, "SignInAgain", False, 0, False)>
    <InlineData("InvitationInvalid", False, "Reconnecting", False, 60, False)>
    <InlineData("LoginTaken", False, "Reconnecting", False, 60, False)>
    <InlineData("PasswordPolicy", False, "Reconnecting", False, 60, False)>
    <InlineData("VersionUnsupported", True, "UpdateNeeded", False, 0, False)>
    <InlineData("TlsRequired", True, "Defect", False, 0, True)>
    <InlineData("RateLimited", False, "Reconnecting", False, 60, False)>
    <InlineData("Capacity", False, "Reconnecting", False, 120, False)>
    <InlineData("IdCollision", False, "Reconnecting", False, 60, False)>
    <InlineData("PortUnavailable", False, "Reconnecting", False, 60, False)>
    <InlineData("Unavailable", False, "Reconnecting", False, 60, False)>
    <InlineData("Unknown", False, "Reconnecting", False, 60, False)>
    Public Sub Each_refusal_of_a_hello_has_one_decision(reasonName As String, stops As Boolean, stateName As String, discard As Boolean, retrySeconds As Integer, defect As Boolean)
        Dim reason As ExchangeRefusal = DirectCast([Enum].Parse(GetType(ExchangeRefusal), reasonName), ExchangeRefusal)
        Dim d As ExchangeRefusalDecision = ExchangeRefusalPolicy.ForHello(reason)
        Assert.Equal(stops, d.Stops)
        Assert.Equal(stateName, d.State.ToString())
        Assert.Equal(discard, d.DiscardToken)
        Assert.Equal(retrySeconds, d.RetryAfterSeconds)
        Assert.Equal(defect, d.IsDefect)
    End Sub

    <Fact>
    Public Sub Every_reason_of_the_contract_has_a_decision_and_only_a_revoked_device_discards_its_token()
        For Each r As ExchangeRefusal In ExchangeRefusals.All()
            Dim d As ExchangeRefusalDecision = ExchangeRefusalPolicy.ForHello(r)
            Assert.NotNull(d)
            ' a refusal that retries never stops, and one that stops never has a wait
            Assert.Equal(d.Stops, d.RetryAfterSeconds = 0)
            Assert.Equal(r = ExchangeRefusal.DeviceRevoked, d.DiscardToken)
        Next
    End Sub

    <Fact>
    Public Sub The_backoff_is_1_2_4_8_16_30_and_then_30_for_ever()
        Assert.Equal({1, 2, 4, 8, 16, 30, 30, 30, 30}, Enumerable.Range(0, 9).Select(Function(i) ExchangeBackoff.BaseSeconds(i)).ToArray())
        Assert.Equal(1, ExchangeBackoff.BaseSeconds(-3))
        Assert.Equal(30, ExchangeBackoff.BaseSeconds(Integer.MaxValue))
    End Sub

    <Fact>
    Public Sub The_jitter_stays_within_20_percent_of_the_step_and_never_makes_a_storm()
        For attempt As Integer = 0 To 8
            Dim step1 As Double = ExchangeBackoff.BaseSeconds(attempt)
            Assert.Equal(step1 * 0.8, ExchangeBackoff.Delay(attempt, 0.0).TotalSeconds, 6)
            Assert.True(ExchangeBackoff.Delay(attempt, 0.999999).TotalSeconds <= step1 * 1.2 + 0.000001)
            For Each r As Double In {0.0, 0.1, 0.5, 0.9, 1.0, 5.0, -2.0}
                Dim t As Double = ExchangeBackoff.Delay(attempt, r).TotalSeconds
                Assert.InRange(t, step1 * 0.8 - 0.000001, step1 * 1.2 + 0.000001)
            Next
        Next
        ' the longest wait of the backoff is bounded: 30 s plus the jitter
        Assert.True(ExchangeBackoff.Delay(100, 0.999999).TotalSeconds < 36.1)
    End Sub

End Class
