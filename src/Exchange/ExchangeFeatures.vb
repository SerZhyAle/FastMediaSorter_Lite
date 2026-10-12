#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 180 T-5 and rule 2 of the ticket. The one switch every client surface of the device exchange reads.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE until the owner opens the clients (198 rule 3: nothing
' ships before rung 2). A developer turns the development tree on with the machine environment variable
' FMS_EXCHANGE_CLIENT=1 (the first check of ticket 182 sets it on the test host); the released default is untouched, so
' a build with the constant False and no variable shows no row, opens no connection, creates no pipe and writes no value.
'
' Since 2026-10-12 (owner: the person who runs a server must not look for a switch or a terminal) the features are also on where the
' server component is installed WITH the program (ComponentInstalled): the component is in no released package while the shipping
' gate is shut, so a released install is unchanged, and a PC that has the server has its client side.
'
' ExchangeFeaturesGuardTests holds the constant in both directions: it stays False unless a reviewed evidence file names
' the rung it was opened at (204 section 3 item 3), and no surface is reachable through anything but this class.

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

    ''' <summary>The folder the server component lives in, next to the program (<c>exchange\</c>), and the file in it.</summary>
    Friend Const ComponentFolder As String = "exchange"
    Friend Const ComponentExeName As String = "fms-exchange-server.exe"

    ''' <summary>A test names its own answer; Nothing means the real folders.</summary>
    Friend Shared ComponentProbeOverride As Func(Of Boolean)

    Private Shared _componentInstalled As Boolean?

    ''' <summary>The Media Exchange Server component is installed with THIS program: its exe is in <c>exchange\</c> beside the
    ''' program, or beside the folder the program sits in (the Server edition keeps the viewer in <c>viewer\</c>). The component
    ''' is in no released package while the shipping gate is shut, so there this is False and the build behaves as it always did;
    ''' on a PC where someone installed the server, its own client side is offered with it - a person who runs a server has not
    ''' to find a switch, and nothing is asked of a terminal. Read once per run (a file does not appear mid-session).</summary>
    Public Shared Function ComponentInstalled() As Boolean
        If ComponentProbeOverride IsNot Nothing Then Return ComponentProbeOverride()
        If _componentInstalled.HasValue Then Return _componentInstalled.Value
        Dim found As Boolean = False
        Try
            Dim here As String = AppContext.BaseDirectory
            If Not String.IsNullOrEmpty(here) Then
                Dim trimmed As String = here.TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
                Dim parent As String = IO.Path.GetDirectoryName(trimmed)
                found = IO.File.Exists(IO.Path.Combine(here, ComponentFolder, ComponentExeName)) OrElse
                        (Not String.IsNullOrEmpty(parent) AndAlso IO.File.Exists(IO.Path.Combine(parent, ComponentFolder, ComponentExeName)))
            End If
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            found = False
        End Try
        _componentInstalled = found
        Return found
    End Function

    ''' <summary>True when the client features exist in this run: offered by the build, or the developer switch.</summary>
    Public Shared Function IsEnabled(read As Func(Of String, EnvironmentVariableTarget, String)) As Boolean
        Return Offered OrElse DeveloperSwitchOn(read)
    End Function

    ''' <summary>The same, plus the server component installed with this program (<see cref="ComponentInstalled"/>).</summary>
    Public Shared Function IsEnabled() As Boolean
        Return Offered OrElse DeveloperSwitchOn() OrElse ComponentInstalled()
    End Function

End Class
#End If
