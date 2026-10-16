#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 180 T-2 (sections 4 and 5): the pure rules of the device agent - what a refusal means, and how long to wait
' before the next try. No I/O, no clock, no text: the agent applies these, the tests pin them.

''' <summary>What the agent does about a refusal of the server.</summary>
Friend NotInheritable Class ExchangeRefusalDecision

    ''' <summary>True when the agent stops and says why; False when it waits and tries again.</summary>
    Public ReadOnly Stops As Boolean
    ''' <summary>The state shown while stopped or waiting.</summary>
    Public ReadOnly State As ExchangeAgentState
    ''' <summary>The device token is thrown away (the server no longer knows this device).</summary>
    Public ReadOnly DiscardToken As Boolean
    ''' <summary>When it retries: seconds to wait (0 when it stops).</summary>
    Public ReadOnly RetryAfterSeconds As Integer
    ''' <summary>A defect of this program, logged by name (a refusal that can only come from a bug here).</summary>
    Public ReadOnly IsDefect As Boolean

    Public Sub New(stops As Boolean, state As ExchangeAgentState, discardToken As Boolean, retryAfterSeconds As Integer, isDefect As Boolean)
        Me.Stops = stops
        Me.State = state
        Me.DiscardToken = discardToken
        Me.RetryAfterSeconds = retryAfterSeconds
        Me.IsDefect = isDefect
    End Sub

End Class

Friend NotInheritable Class ExchangeRefusalPolicy

    Private Sub New()
    End Sub

    ''' <summary>A `capacity` refusal retries slowly: the server is full, and asking every few seconds helps nobody.</summary>
    Public Const CapacityRetrySeconds As Integer = 120
    ''' <summary>A `rate-limited` refusal backs off for a minute.</summary>
    Public Const RateLimitedRetrySeconds As Integer = 60
    ''' <summary>Any other refusal of a `hello` (an unknown reason, one that cannot answer a hello - `invitation-invalid`, `login-taken`
    ''' and `password-policy` answer a `register` and nothing else) is a generic one: slow retry, never a storm.</summary>
    Public Const GenericRetrySeconds As Integer = 60

    ''' <summary>Section 5: "A refusal is not retried": bad credentials, a revoked device and a signed-out one (item AT: the token died with
    ''' a password change; the entry and the `deviceId` stay, the person types the password and the same device enrolls again) stop the
    ''' agent and mark "sign in again" (the token is discarded on <c>device-revoked</c> only); a version refusal stops (the two sides speak
    ''' different versions: nothing the person can update, item CZ); `rate-limited` backs off for a minute; `capacity` retries slowly.
    ''' `tls-required` is a defect of this client (it only ever speaks TLS) and stops.</summary>
    Public Shared Function ForHello(reason As ExchangeRefusal) As ExchangeRefusalDecision
        Select Case reason
            Case ExchangeRefusal.BadCredentials
                Return New ExchangeRefusalDecision(True, ExchangeAgentState.SignInAgain, False, 0, False)
            Case ExchangeRefusal.DeviceRevoked
                Return New ExchangeRefusalDecision(True, ExchangeAgentState.SignInAgain, True, 0, False)
            Case ExchangeRefusal.SignedOut
                Return New ExchangeRefusalDecision(True, ExchangeAgentState.SignInAgain, False, 0, False)
            Case ExchangeRefusal.VersionUnsupported
                Return New ExchangeRefusalDecision(True, ExchangeAgentState.UpdateNeeded, False, 0, False)
            Case ExchangeRefusal.TlsRequired
                Return New ExchangeRefusalDecision(True, ExchangeAgentState.Defect, False, 0, True)
            Case ExchangeRefusal.RateLimited
                Return New ExchangeRefusalDecision(False, ExchangeAgentState.Reconnecting, False, RateLimitedRetrySeconds, False)
            Case ExchangeRefusal.Capacity
                Return New ExchangeRefusalDecision(False, ExchangeAgentState.Reconnecting, False, CapacityRetrySeconds, False)
            Case Else
                Return New ExchangeRefusalDecision(False, ExchangeAgentState.Reconnecting, False, GenericRetrySeconds, False)
        End Select
    End Function

End Class

''' <summary>Section 5: "1, 2, 4, 8, 16, 30 s with jitter".</summary>
Friend NotInheritable Class ExchangeBackoff

    Private Sub New()
    End Sub

    Private Shared ReadOnly Steps As Integer() = {1, 2, 4, 8, 16, 30}

    ''' <summary>The base delay in seconds for the n-th consecutive failure (0 is the first): the steps, then 30 for ever.</summary>
    Public Shared Function BaseSeconds(attempt As Integer) As Integer
        If attempt < 0 Then attempt = 0
        Return Steps(Math.Min(attempt, Steps.Length - 1))
    End Function

    ''' <summary>The delay with jitter: the base scaled by 0.8 to 1.2, by <paramref name="random01"/> in [0, 1).</summary>
    Public Shared Function Delay(attempt As Integer, random01 As Double) As TimeSpan
        Dim r As Double = Math.Min(Math.Max(random01, 0.0), 0.999999)
        Return TimeSpan.FromSeconds(BaseSeconds(attempt) * (0.8 + 0.4 * r))
    End Function

End Class

#End If
