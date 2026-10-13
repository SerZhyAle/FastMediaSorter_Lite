Option Strict On
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Only existing server/group acts, replaceable by harmless ports in scene and unit tests.</summary>
Friend Interface IExchangeFirstRunActions
    Function ReadAsync() As Task(Of ExchangeSnapshot)
    Function SetListenAsync(listen As String) As Task(Of ExchangeProcess.ListenOutcome)
    Function SetRangeAsync(range As String) As Task(Of ExchangeProcess.ConfigOutcome)
    Function SetEndpointAsync(endpoint As String) As Task(Of ExchangeProcess.ConfigOutcome)
    Function FirewallAsync() As Task(Of ExchangeElevation.ElevatedOutcome)
    Function ServiceAsync() As Task(Of ServiceControl.ManageResult)
    Function Account(owner As Form, login As String, typed As Boolean, enroll As Boolean) As ExchangeWizardResult
    Function Suggest(port As Integer) As String
    Sub ConnectionString(owner As Form)
End Interface

Friend NotInheritable Class ExchangeFirstRunActions
    Implements IExchangeFirstRunActions

    Public Async Function ReadAsync() As Task(Of ExchangeSnapshot) Implements IExchangeFirstRunActions.ReadAsync
        Dim snapshot As ExchangeSnapshot = Await Task.Run(Function() ExchangeSnapshot.Read()).ConfigureAwait(False)
        If snapshot.ServiceExists Then
            Dim dir As String = ExchangeAdmin.ServiceDataDir()
            If dir.Length = 0 Then
                snapshot.Report.Answered = False
                Return snapshot
            End If
            snapshot.Report = Await Task.Run(Function() ExchangeProcess.InspectIn(dir)).ConfigureAwait(False)
            Dim reply As ExchangeAdminReply = Await ExchangeAdmin.StatusAsync(CancellationToken.None).ConfigureAwait(False)
            Dim status As ExchangeAdminStatus = If(reply.Ok, ExchangeAdminParser.Status(reply.DataJson), Nothing)
            snapshot.Report.Answered = snapshot.Report.Answered AndAlso status IsNot Nothing
            If status IsNot Nothing Then snapshot.Report.Accounts = status.Accounts
        End If
        Return snapshot
    End Function

    Private Shared Function StoreForConfigWrite() As String
        If Not ExchangeService.Exists() Then Return ""
        Dim dir As String = ExchangeAdmin.ServiceDataDir()
        If dir = "" Then Throw New InvalidOperationException("The registered service's store could not be identified.")
        Return dir
    End Function

    Public Function SetListenAsync(listen As String) As Task(Of ExchangeProcess.ListenOutcome) Implements IExchangeFirstRunActions.SetListenAsync
        Return Task.Run(Function() ExchangeProcess.SetListen(listen, StoreForConfigWrite()))
    End Function
    Public Function SetRangeAsync(range As String) As Task(Of ExchangeProcess.ConfigOutcome) Implements IExchangeFirstRunActions.SetRangeAsync
        Return Task.Run(Function() ExchangeProcess.SetPortRange(range, StoreForConfigWrite()))
    End Function
    Public Function SetEndpointAsync(endpoint As String) As Task(Of ExchangeProcess.ConfigOutcome) Implements IExchangeFirstRunActions.SetEndpointAsync
        Return Task.Run(Function() ExchangeProcess.SetEndpoint(endpoint, StoreForConfigWrite()))
    End Function
    Public Function FirewallAsync() As Task(Of ExchangeElevation.ElevatedOutcome) Implements IExchangeFirstRunActions.FirewallAsync
        Return Task.Run(Function() ExchangeFirewall.EnableViaElevation())
    End Function
    Public Function ServiceAsync() As Task(Of ServiceControl.ManageResult) Implements IExchangeFirstRunActions.ServiceAsync
        Return Task.Run(Function() ExchangeService.Manage(ExchangeService.RegisterVerb()))
    End Function

    Public Function Account(owner As Form, login As String, typed As Boolean, enroll As Boolean) As ExchangeWizardResult Implements IExchangeFirstRunActions.Account
        Dim password As String = Nothing
        If typed Then
            Using dialog As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                If dialog.ShowDialog(owner) <> DialogResult.OK Then Return New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.Cancelled}
                password = dialog.EnteredPassword
            End Using
        End If
        Using ports As New ExchangeFirstRunWizardPorts(password)
            password = Nothing
            Using dialog As New Exchange_Server_Wizard_Form(ports, login, firstRun:=True, enrollThisPc:=enroll)
                dialog.ShowDialog(owner)
                Return If(dialog.Result, New ExchangeWizardResult With {.Outcome = ExchangeWizardOutcome.Cancelled})
            End Using
        End Using
    End Function

    Public Function Suggest(port As Integer) As String Implements IExchangeFirstRunActions.Suggest
        Return ExchangeOperatorEndpoint.Suggest("", NetworkInfo.LocalIPv4(), port)
    End Function
    Public Sub ConnectionString(owner As Form) Implements IExchangeFirstRunActions.ConnectionString
        Using dialog As New Exchange_Connection_String_Form()
            dialog.ShowDialog(owner)
        End Using
    End Sub
End Class

''' <summary>The same account/start/check/enroll engine, with service-aware start and a transient typed-password choice.</summary>
Friend NotInheritable Class ExchangeFirstRunWizardPorts
    Implements IExchangeWizardPorts, IDisposable
    Private ReadOnly inner As New ExchangeWizardPorts()
    Private password As String
    Friend Sub New(typedPassword As String)
        password = typedPassword
    End Sub
    Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeWizardPorts.Inspect
        Dim dir As String = If(ExchangeService.Exists(), ExchangeAdmin.ServiceDataDir(), "")
        If ExchangeService.Exists() AndAlso dir = "" Then Return New ExchangeProcess.InspectReport()
        Return If(dir = "", inner.Inspect(), ExchangeProcess.InspectIn(dir))
    End Function
    Public Function ServiceExists() As Boolean Implements IExchangeWizardPorts.ServiceExists
        Return inner.ServiceExists()
    End Function
    Public Function IsConsoleRunning() As Boolean Implements IExchangeWizardPorts.IsConsoleRunning
        Return If(ServiceExists(), ExchangeService.QueryState() = ServiceControl.ServiceState.Running, inner.IsConsoleRunning())
    End Function
    Public Function GeneratePassword() As String Implements IExchangeWizardPorts.GeneratePassword
        Return If(password, inner.GeneratePassword())
    End Function
    Public Function AddAccountAsync(login As String, secret As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeWizardPorts.AddAccountAsync
        password = Nothing
        Return inner.AddAccountAsync(login, secret, ct)
    End Function
    Public Function Start() As ExchangeProcess.StartOutcome Implements IExchangeWizardPorts.Start
        If Not ServiceExists() Then Return inner.Start()
        ' An already registered service gets only its unelevated control, never an unexpected fallback UAC.
        Return If(ExchangeService.TryControl(True) = ExchangeService.ControlOutcome.Done,
            ExchangeProcess.StartOutcome.Started, ExchangeProcess.StartOutcome.Failed)
    End Function
    Public Function AgentOffered() As Boolean Implements IExchangeWizardPorts.AgentOffered
        Return inner.AgentOffered()
    End Function
    Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeWizardPorts.ProbeAsync
        Return inner.ProbeAsync(server, ct)
    End Function
    Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeWizardPorts.EnrollAsync
        Return inner.EnrollAsync(request, ct)
    End Function
    Public Function MachineName() As String Implements IExchangeWizardPorts.MachineName
        Return inner.MachineName()
    End Function
    Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeWizardPorts.DelayAsync
        Return inner.DelayAsync(milliseconds, ct)
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        password = Nothing
    End Sub
End Class
