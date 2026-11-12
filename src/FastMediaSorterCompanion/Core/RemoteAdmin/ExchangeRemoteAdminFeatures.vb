Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' Ticket L0278 T-4 (specification section 5.10): the one switch every surface of the remote administration client reads.
'
' "Remote administration" here is the Share Manager acting as the administrator of a Media Exchange Server that runs on ANOTHER
' PC (the contract REMOTE-ADMIN over the admin stream), through its own settings destination. It is a separate gate from
' ExchangeFeatures.Offered, which stays as it is (the exchange CLIENTS, open since ticket 223), and it has no local component to
' ask: the whole point is a PC that has no server exe.
'
' <see cref="Offered"/> is a compile-time constant and it is FALSE. Nothing opens it from the ticket that built the client. The
' guard RemoteAdminGateGuardTests holds it in both directions against docs\specifications\remote-admin-gate-evidence.md (a True
' needs one PASS line for E1 and one for E2; a False forbids any PASS line), tools\Test-FeatureGates.ps1 holds the same rule for
' the release preflight, and RemoteAdminSeparationTests holds the files of the remote layer apart from the local server module.
' Opening the gate is the owner's word plus a few edits made together: this constant, the two PASS lines of the evidence file and
' the open-gates table of DualModeGateGuardTests (which refuses any True it does not list); the evidence file lists them.
'
' A developer can still turn the surfaces on in a tree whose constant is False with the environment variable
' FMS_EXCHANGE_REMOTE_ADMIN=1 (process, user or machine). No other gate's switch turns this one on.
'
' The class reads the environment and nothing else: no registry, no file, no network. ExchangeDormancyTests holds that.

Friend NotInheritable Class ExchangeRemoteAdminFeatures

    Private Sub New()
    End Sub

    ''' <summary>FALSE: the remote administration client is not offered by this build. The one line that offers it is this
    ''' constant, changed together with the evidence file and the gate table (see the header).</summary>
    Public Const Offered As Boolean = False

    ''' <summary>The developer switch: set to "1" (process, user or machine environment) to show the remote administration
    ''' surfaces while <see cref="Offered"/> is False.</summary>
    Public Const DeveloperSwitchName As String = "FMS_EXCHANGE_REMOTE_ADMIN"

    ' Test seam, the same shape as ExchangeFeatures and OutwardAccessFeatures: the gate's two inputs for the CURRENT flow of
    ' execution only (and the threads it starts - the ExecutionContext follows a new Thread), so a test that flips them cannot be
    ' seen by another test running in parallel. Friend: no program surface can set it.
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

    ''' <summary>Test seam: what the developer switch and the build's own declaration answer for the calling flow until the
    ''' returned scope is disposed. For a test of the opened state: <c>Using ForFlow(switchOn:=True, offered:=False)</c>; for the
    ''' state of this build: <c>Using ForFlow(switchOn:=False, offered:=False)</c>.</summary>
    Friend Shared Function ForFlow(switchOn As Boolean, offered As Boolean) As IDisposable
        Dim scope As New FlowScope(_flowSwitch.Value, _flowOffered.Value)
        _flowSwitch.Value = switchOn
        _flowOffered.Value = offered
        Return scope
    End Function

    ''' <summary>The build's declaration (<see cref="Offered"/>), or the value a test set for its own flow. Every production
    ''' reader asks this (through <see cref="IsEnabled()"/>) and never the constant, so a test of either state needs no second
    ''' build.</summary>
    Public Shared Function BuildOffers() As Boolean
        If _flowOffered.Value.HasValue Then Return _flowOffered.Value.Value
        Return Offered
    End Function

    ''' <summary>The switch, read through an injected reader: the value "1" (after trimming) and nothing else turns it on. A
    ''' reader that throws for lack of a machine scope is the switch off, not an error.</summary>
    Public Shared Function DeveloperSwitchOn(read As Func(Of String, String)) As Boolean
        If read Is Nothing Then Return False
        Dim v As String = Nothing
        Try
            v = read(DeveloperSwitchName)
        Catch ex As Exception When TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is ArgumentException
            v = Nothing
        End Try
        Return String.Equals(If(v, "").Trim(), "1", StringComparison.Ordinal)
    End Function

    ''' <summary>The real environment (or the value a test set for its own flow, see <see cref="ForFlow"/>).</summary>
    Public Shared Function DeveloperSwitchOn() As Boolean
        If _flowSwitch.Value.HasValue Then Return _flowSwitch.Value.Value
        Return DeveloperSwitchOn(AddressOf ReadEnvironment)
    End Function

    ' Process first, then user, then machine; the first scope that holds "1" answers, any other value reads as not set.
    Private Shared Function ReadEnvironment(name As String) As String
        For Each target As EnvironmentVariableTarget In {EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine}
            Dim v As String = Nothing
            Try
                v = Environment.GetEnvironmentVariable(name, target)
            Catch ex As Exception When TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is ArgumentException
                v = Nothing
            End Try
            If String.Equals(If(v, "").Trim(), "1", StringComparison.Ordinal) Then Return v
        Next
        Return Nothing
    End Function

    ''' <summary>True when the remote administration surfaces exist in this run: offered by the build, or the developer switch.
    ''' It does NOT ask whether a local server component is installed - that is a different server.</summary>
    Friend Shared Function IsEnabled() As Boolean
        Return BuildOffers() OrElse DeveloperSwitchOn()
    End Function

    ''' <summary>The same, with the developer switch read through <paramref name="read"/>.</summary>
    Friend Shared Function IsEnabled(read As Func(Of String, String)) As Boolean
        Return BuildOffers() OrElse DeveloperSwitchOn(read)
    End Function

End Class
