#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

' Ticket 187 T-4: the order of the quick join as a pure-ish controller (no window in it), so the dialog is a skin over rules a test can
' drive against the stub server. Step by step, as the proposal's Ask 5 and the grammar proposal's rule 13 say:
'
'   1. the input is classified (ExchangeConnectInput); a refusal ends here, with the one sentence of its reason;
'   2. `fp` in the string is the trust anchor - no prompt; no `fp` and an entry's own pin (signing in again) is the same; otherwise a
'      first-contact probe: the platform's acceptance needs nothing, anything else asks the person ONCE with the fingerprint shown and
'      "No" as the default (the prompt is a delegate: no window is in here);
'   3. the quick join (ExchangeConsumer.EnrollAsync): TLS under that trust, `enroll`, the token under DPAPI, the entry in the registry.
'      A leaf that is not the pinned one is a named outcome with BOTH fingerprints; nothing is pinned and no password was sent.
'
' The password is a parameter of one call and is wiped by the callee; this class stores nothing and logs nothing.

Friend Enum ExchangeConnectResultKind
    ''' <summary>The entry exists and holds its token.</summary>
    Added = 0
    ''' <summary>The input is not a string or an address: <see cref="ExchangeConnectResult.Input"/> says why.</summary>
    InputRefused = 1
    ''' <summary>The leaf is not the pinned one: <see cref="ExchangeConnectResult.Expected"/> and <see cref="ExchangeConnectResult.Seen"/>.</summary>
    PinMismatch = 2
    ''' <summary>The person was shown the fingerprint of an unknown certificate and said no (or the probe failed on trust).</summary>
    TrustDeclined = 3
    ''' <summary>The join itself failed: <see cref="ExchangeConnectResult.Enroll"/> and <see cref="ExchangeConnectResult.Refusal"/>.</summary>
    Failed = 4
    Cancelled = 5
End Enum

Friend NotInheritable Class ExchangeConnectResult
    Public ReadOnly Kind As ExchangeConnectResultKind
    Public ReadOnly Entry As ExchangeEntry
    Public ReadOnly Input As ExchangeConnectInput
    Public ReadOnly Enroll As ExchangeConsumerEnrollOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>The pin the string (or the entry) held.</summary>
    Public ReadOnly Expected As String
    ''' <summary>The fingerprint the server presented.</summary>
    Public ReadOnly Seen As String

    Public Sub New(kind As ExchangeConnectResultKind, entry As ExchangeEntry, input As ExchangeConnectInput, enroll As ExchangeConsumerEnrollOutcome,
                   refusal As ExchangeRefusal, expected As String, seen As String)
        Me.Kind = kind
        Me.Entry = entry
        Me.Input = input
        Me.Enroll = enroll
        Me.Refusal = refusal
        Me.Expected = If(expected, "")
        Me.Seen = If(seen, "")
    End Sub
End Class

Friend NotInheritable Class ExchangeConnectFlow

    Private Sub New()
    End Sub

    ''' <param name="inputText">A string, a file's text or a host:port.</param>
    ''' <param name="pinOfEntry">The pin an existing entry already holds (signing in again); "" for a new server.</param>
    ''' <param name="confirmTrust">Asked with (host:port, fingerprint) for an unknown certificate: True = trust and remember. Never asked when
    ''' the platform validated the certificate or a pin exists.</param>
    Public Shared Async Function RunAsync(inputText As String, login As String, password As String, pinOfEntry As String,
                                          deviceName As String, ports As ExchangeConsumerPorts,
                                          confirmTrust As Func(Of String, String, Boolean), ct As CancellationToken) As Task(Of ExchangeConnectResult)
        Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(inputText)
        If Not input.IsUsable Then
            Return New ExchangeConnectResult(ExchangeConnectResultKind.InputRefused, Nothing, input, ExchangeConsumerEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "", "")
        End If
        Dim endpoint As String = input.Value.Endpoint
        Dim request As New ExchangeConsumerEnrollRequest With {
            .Server = endpoint, .Login = If(login, "").Trim(), .Password = If(password, ""), .ServerName = input.Value.Name, .DeviceName = If(deviceName, "")}
        If request.Login.Length = 0 OrElse request.Password.Length = 0 Then
            request.Wipe()
            Return New ExchangeConnectResult(ExchangeConnectResultKind.Failed, Nothing, input, ExchangeConsumerEnrollOutcome.BadInput, ExchangeRefusal.Unknown, "", "")
        End If

        ' the trust rule
        Dim expected As String = ""
        If input.HasPin Then
            expected = input.Value.Fingerprint
        ElseIf Not String.IsNullOrEmpty(pinOfEntry) Then
            expected = pinOfEntry
        End If
        If expected.Length > 0 Then
            request.TrustedFingerprint = expected
        Else
            Dim info As ExchangeCertificateInfo
            Try
                info = Await ExchangeConsumer.ProbeAsync(endpoint, ports, ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException
                request.Wipe()
                Return New ExchangeConnectResult(ExchangeConnectResultKind.Failed, Nothing, input, OutcomeOf(ex.Failure), ExchangeRefusal.Unknown, "", ex.Fingerprint)
            Catch ex As OperationCanceledException
                request.Wipe()
                Return New ExchangeConnectResult(ExchangeConnectResultKind.Cancelled, Nothing, input, ExchangeConsumerEnrollOutcome.Cancelled, ExchangeRefusal.Unknown, "", "")
            End Try
            If info.PlatformValid Then
                request.PlatformValid = True
            Else
                ' only an explicit yes pins it; the question's default is No (the delegate is the window's)
                If confirmTrust Is Nothing OrElse Not confirmTrust.Invoke(endpoint, info.Fingerprint) Then
                    request.Wipe()
                    Return New ExchangeConnectResult(ExchangeConnectResultKind.TrustDeclined, Nothing, input, ExchangeConsumerEnrollOutcome.Untrusted, ExchangeRefusal.Unknown, "", info.Fingerprint)
                End If
                request.TrustedFingerprint = info.Fingerprint
            End If
        End If

        Dim result As ExchangeConsumerEnrollResult = Await ExchangeConsumer.EnrollAsync(request, ports, ct).ConfigureAwait(False)
        Select Case result.Outcome
            Case ExchangeConsumerEnrollOutcome.Enrolled
                Return New ExchangeConnectResult(ExchangeConnectResultKind.Added, result.Entry, input, result.Outcome, ExchangeRefusal.Unknown, expected, result.Fingerprint)
            Case ExchangeConsumerEnrollOutcome.PinMismatch
                Return New ExchangeConnectResult(ExchangeConnectResultKind.PinMismatch, Nothing, input, result.Outcome, ExchangeRefusal.Unknown, request.TrustedFingerprint, result.Fingerprint)
            Case ExchangeConsumerEnrollOutcome.Cancelled
                Return New ExchangeConnectResult(ExchangeConnectResultKind.Cancelled, Nothing, input, result.Outcome, ExchangeRefusal.Unknown, "", result.Fingerprint)
            Case Else
                Return New ExchangeConnectResult(ExchangeConnectResultKind.Failed, Nothing, input, result.Outcome, result.Refusal, "", result.Fingerprint)
        End Select
    End Function

    Private Shared Function OutcomeOf(failure As ExchangeConnectFailure) As ExchangeConsumerEnrollOutcome
        Select Case failure
            Case ExchangeConnectFailure.TlsFailed : Return ExchangeConsumerEnrollOutcome.TlsFailed
            Case ExchangeConnectFailure.TimedOut : Return ExchangeConsumerEnrollOutcome.TimedOut
            Case ExchangeConnectFailure.Cancelled : Return ExchangeConsumerEnrollOutcome.Cancelled
            Case ExchangeConnectFailure.PinMismatch, ExchangeConnectFailure.Untrusted : Return ExchangeConsumerEnrollOutcome.Untrusted
            Case Else : Return ExchangeConsumerEnrollOutcome.Network
        End Select
    End Function

End Class
#End If
