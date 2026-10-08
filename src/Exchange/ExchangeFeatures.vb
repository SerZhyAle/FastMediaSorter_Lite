#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 180 T-5 and rule 2 of the ticket. The one switch every client surface of the device exchange reads.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE until the owner opens the clients (160 rule 3: nothing
' ships before rung 2). A developer turns the development tree on with the machine environment variable
' FMS_EXCHANGE_CLIENT=1 (the first check of ticket 182 sets it on the test host); the released default is untouched, so
' a build with the constant False and no variable shows no row, opens no connection, creates no pipe and writes no value.
'
' ExchangeFeaturesGuardTests holds the constant in both directions: it stays False unless a reviewed evidence file names
' the rung it was opened at (177 section 3 item 3), and no surface is reachable through anything but this class.

Friend NotInheritable Class ExchangeFeatures

    Private Sub New()
    End Sub

    ''' <summary>The build offers the exchange client features to everybody. False until the owner opens the gate.</summary>
    Public Const Offered As Boolean = False

    ''' <summary>The developer switch: set to "1" (process, user or machine environment) to show the client features
    ''' while <see cref="Offered"/> is False.</summary>
    Public Const DeveloperSwitchName As String = "FMS_EXCHANGE_CLIENT"

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

    ''' <summary>True when the client features exist in this run: offered by the build, or the developer switch.</summary>
    Public Shared Function IsEnabled(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
        Return Offered OrElse DeveloperSwitchOn(read)
    End Function

    Public Shared Function IsEnabled() As Boolean
        Return Offered OrElse DeveloperSwitchOn()
    End Function

End Class
#End If
