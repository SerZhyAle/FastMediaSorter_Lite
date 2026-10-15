#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 199 section 6 rule 6 (Q199-5) and ticket 206 section 2: the one switch every producer surface of a
' live broadcast reads, separate from the exchange's <c>ExchangeFeatures.Offered</c>. The audio shape is
' `[CONTRACT]` and may be opened before the exchange; the video shape is `[PROPOSED]`, binds nobody and
' stays closed until the owner flips it on his own word.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE until the owner opens the gate. A
' developer turns the development tree on with the machine environment variable FMS_LIVE_BROADCAST=1;
' the released default is untouched, so a build with the constant False and no variable starts no capture,
' opens no listener and shows no row.
'
' LiveBroadcastGateGuardTests (ticket 204 section 3 item 3) holds the constant in both directions: it stays
' False unless a reviewed evidence file names the rung or phase it was opened at, and the surface-reachability
' cases land with each step of 172-176 that creates a surface.

Friend NotInheritable Class LiveBroadcastFeatures

    Private Sub New()
    End Sub

    ''' <summary>Offered on the owner's ticket 199 instruction. The evidence file records verified checks and pending acceptance.</summary>
    Public Const Offered As Boolean = True

    ''' <summary>The developer switch: set to "1" (process, user or machine environment) to show the producer
    ''' features while <see cref="Offered"/> is False.</summary>
    Public Const DeveloperSwitchName As String = "FMS_LIVE_BROADCAST"

    ''' <summary>The switch, read through an injected reader (process first, then user, then machine).</summary>
    Public Shared Function DeveloperSwitchOn(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
        If _flowSwitch.Value.HasValue Then Return _flowSwitch.Value.Value
        If read Is Nothing Then Return False
        For Each target As EnvironmentVariableTarget In {EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine}
            Dim v As String = Nothing
            Try
                v = read(DeveloperSwitchName, target)
            Catch ex As Exception When TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is ArgumentException
                v = Nothing
            End Try
            If String.Equals(If(v, "").Trim(), "1", StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    Private Shared ReadOnly _flowSwitch As New System.Threading.AsyncLocal(Of Boolean?)()
    Private Shared ReadOnly _flowOffered As New System.Threading.AsyncLocal(Of Boolean?)()

    Private NotInheritable Class FlowScope
        Implements IDisposable

        Private ReadOnly _previousSwitch As Boolean?
        Private ReadOnly _previousOffered As Boolean?

        Public Sub New(previousSwitch As Boolean?, previousOffered As Boolean?)
            _previousSwitch = previousSwitch
            _previousOffered = previousOffered
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _flowSwitch.Value = _previousSwitch
            _flowOffered.Value = _previousOffered
        End Sub
    End Class

    Friend Shared Function ForFlow(Optional switchOn As Boolean? = Nothing, Optional offered As Boolean? = Nothing) As IDisposable
        Dim scope As New FlowScope(_flowSwitch.Value, _flowOffered.Value)
        If switchOn.HasValue Then _flowSwitch.Value = switchOn
        If offered.HasValue Then _flowOffered.Value = offered
        Return scope
    End Function

    Public Shared Function BuildOffers() As Boolean
        If _flowOffered.Value.HasValue Then Return _flowOffered.Value.Value
        Return Offered
    End Function

    ''' <summary>The real environment.</summary>
    Public Shared Function DeveloperSwitchOn() As Boolean
        If _flowSwitch.Value.HasValue Then Return _flowSwitch.Value.Value
        Return DeveloperSwitchOn(AddressOf Environment.GetEnvironmentVariable)
    End Function

    ''' <summary>True when the producer features exist in this run: offered by the build, or the developer switch.</summary>
    Public Shared Function IsEnabled(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
        Return BuildOffers() OrElse DeveloperSwitchOn(read)
    End Function

    Public Shared Function IsEnabled() As Boolean
        Return BuildOffers() OrElse DeveloperSwitchOn()
    End Function

End Class
#End If
