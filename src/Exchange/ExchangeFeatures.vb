#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 180 T-5 and rule 2 of the ticket. The one switch every client surface of the device exchange reads.
'
' <see cref="Offered"/> is a compile-time constant. It was FALSE until the owner opened the clients (198 rule 3: nothing
' ships before rung 2); ticket 223 T-3 opened it on the owner's word of 2026-10-14, recorded in
' docs\specifications\exchange-gate-evidence.md - ahead of the joint rung 2 run, which that file says in plain words.
' With it True the CLIENT side of the exchange (the Share Manager's "Exchange account" page, the viewer's "Add from an
' exchange server.." entry, devices, tunnels, `.fmsexch`) exists on every install with no server component and no switch.
' It is still dormant (198 rule 2): with no account configured nothing connects, listens, writes or starts.
' The SERVER side is not behind this class at all: its pages, tray item, first-run welcome, wizard and local join ask the Share
' Manager's server-administration module whether the component is installed - the component is the only thing that offers them,
' and this library never names that module (ExchangeClientGuardTests, ticket 223 T-1).
'
' A developer can still turn the features on in a tree that sets the constant False with the machine environment variable
' FMS_EXCHANGE_CLIENT=1 (the first check of ticket 182 sets it on the test host), and, since 2026-10-12, the features are also on
' where the server component is installed WITH the program (ComponentInstalled). Both are harmless supersets of True.
'
' ExchangeClientGuardTests holds the constant in both directions: it can be True only with a reviewed evidence file that names
' the rung (204 section 3 item 3), and no surface is reachable through anything but this class.

Friend NotInheritable Class ExchangeFeatures

    Private Sub New()
    End Sub

    ''' <summary>The build offers the exchange client features to everybody. Opened 2026-10-14 on the owner's word (ticket 223 T-3, the
    ''' evidence file names what was and was not reached). The one line to take it back is this constant.</summary>
    Public Const Offered As Boolean = True

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

    ' Test seam of ticket 223: the gate's two inputs for the CURRENT flow of execution only (and the threads it starts - the
    ' ExecutionContext follows a new Thread). A process-wide value - the environment variable, ComponentProbeOverride - is seen by
    ' every test class running in parallel, and one that reads the live gate (check the switch, build a form a few ms later) saw it
    ' turn on in between. A flow value cannot be seen from another test. Friend: no program surface can set it.
    Private Shared ReadOnly _flowSwitch As New System.Threading.AsyncLocal(Of Boolean?)()
    Private Shared ReadOnly _flowComponent As New System.Threading.AsyncLocal(Of Boolean?)()
    Private Shared ReadOnly _flowOffered As New System.Threading.AsyncLocal(Of Boolean?)()

    Private NotInheritable Class FlowScope
        Implements IDisposable

        Private ReadOnly _previousSwitch As Boolean?
        Private ReadOnly _previousComponent As Boolean?
        Private ReadOnly _previousOffered As Boolean?

        Public Sub New(previousSwitch As Boolean?, previousComponent As Boolean?, previousOffered As Boolean?)
            _previousSwitch = previousSwitch
            _previousComponent = previousComponent
            _previousOffered = previousOffered
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _flowSwitch.Value = _previousSwitch
            _flowComponent.Value = _previousComponent
            _flowOffered.Value = _previousOffered
        End Sub
    End Class

    ''' <summary>Sets what the developer switch, the installed component and the build's own declaration answer for the calling flow
    ''' until the returned scope is disposed (Nothing leaves that input as it is). For a test of the opened state:
    ''' <c>Using ForFlow(switchOn:=True, componentInstalled:=False)</c>; for the state of a build whose constant is False (the
    ''' shut gate, which the released build no longer is): <c>Using ForFlow(offered:=False, switchOn:=False, componentInstalled:=False)</c>.</summary>
    Friend Shared Function ForFlow(Optional switchOn As Boolean? = Nothing, Optional componentInstalled As Boolean? = Nothing,
                                   Optional offered As Boolean? = Nothing) As IDisposable
        Dim scope As New FlowScope(_flowSwitch.Value, _flowComponent.Value, _flowOffered.Value)
        If switchOn.HasValue Then _flowSwitch.Value = switchOn
        If componentInstalled.HasValue Then _flowComponent.Value = componentInstalled
        If offered.HasValue Then _flowOffered.Value = offered
        Return scope
    End Function

    ''' <summary>The build's declaration (<see cref="Offered"/>), or the value a test set for its own flow. Every production reader asks
    ''' this and never the constant, so a test of the shut gate does not need a second build.</summary>
    Public Shared Function BuildOffers() As Boolean
        If _flowOffered.Value.HasValue Then Return _flowOffered.Value.Value
        Return Offered
    End Function

    ''' <summary>The real environment (or the value a test set for its own flow, see <see cref="ForFlow"/>).</summary>
    Public Shared Function DeveloperSwitchOn() As Boolean
        If _flowSwitch.Value.HasValue Then Return _flowSwitch.Value.Value
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
        If _flowComponent.Value.HasValue Then Return _flowComponent.Value.Value
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
        Return BuildOffers() OrElse DeveloperSwitchOn(read)
    End Function

    ''' <summary>The same, plus the server component installed with this program (<see cref="ComponentInstalled"/>).</summary>
    Public Shared Function IsEnabled() As Boolean
        Return BuildOffers() OrElse DeveloperSwitchOn() OrElse ComponentInstalled()
    End Function

End Class
#End If
