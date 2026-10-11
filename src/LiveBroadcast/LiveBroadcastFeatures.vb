#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 172 section 6 rule 6 (Q172-5) and ticket 179 section 2: the one switch every producer surface of a
' live broadcast reads, separate from the exchange's <c>ExchangeFeatures.Offered</c>. The audio shape is
' `[CONTRACT]` and may be opened before the exchange; the video shape is `[PROPOSED]`, binds nobody and
' stays closed until the owner flips it on his own word.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE until the owner opens the gate. A
' developer turns the development tree on with the machine environment variable FMS_LIVE_BROADCAST=1;
' the released default is untouched, so a build with the constant False and no variable starts no capture,
' opens no listener and shows no row.
'
' LiveBroadcastGateGuardTests (ticket 177 section 3 item 3) holds the constant in both directions: it stays
' False unless a reviewed evidence file names the rung or phase it was opened at, and the surface-reachability
' cases land with each step of 172-176 that creates a surface.

Friend NotInheritable Class LiveBroadcastFeatures

    Private Sub New()
    End Sub

    ''' <summary>The build offers the live-broadcast producer features to everybody. False until the owner opens the gate.</summary>
    Public Const Offered As Boolean = False

    ''' <summary>The developer switch: set to "1" (process, user or machine environment) to show the producer
    ''' features while <see cref="Offered"/> is False.</summary>
    Public Const DeveloperSwitchName As String = "FMS_LIVE_BROADCAST"

    ''' <summary>The switch, read through an injected reader (process first, then user, then machine).</summary>
    Public Shared Function DeveloperSwitchOn(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
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

    ''' <summary>The real environment.</summary>
    Public Shared Function DeveloperSwitchOn() As Boolean
        Return DeveloperSwitchOn(AddressOf Environment.GetEnvironmentVariable)
    End Function

    ''' <summary>True when the producer features exist in this run: offered by the build, or the developer switch.</summary>
    Public Shared Function IsEnabled(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
        Return Offered OrElse DeveloperSwitchOn(read)
    End Function

    Public Shared Function IsEnabled() As Boolean
        Return Offered OrElse DeveloperSwitchOn()
    End Function

End Class
#End If
