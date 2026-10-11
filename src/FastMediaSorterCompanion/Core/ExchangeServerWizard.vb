Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

''' <summary>How the "use this PC as my server" wizard ended (ticket 168 section 3.7).</summary>
Friend Enum ExchangeWizardOutcome
    ''' <summary>A running server, one account and this PC's own agent enrolled in it.</summary>
    Done
    ''' <summary>A running server and one account; the agent was not enrolled because the client features are not offered in this build.</summary>
    DoneNoAgent
    ''' <summary>Nothing was done: no component, a registered service owns the server, or the binary cannot be asked.</summary>
    NotAvailable
    ''' <summary>Nothing was done: the login is not one an account may have.</summary>
    LoginRefused
    ''' <summary>Nothing was done: an account with that login exists already.</summary>
    AccountExists
    ''' <summary>The account could not be created (nothing else was done).</summary>
    AccountFailed
    ''' <summary>The account exists, the server did not start (<see cref="ExchangeWizardResult.StartOutcome"/> says why).</summary>
    StartFailed
    ''' <summary>The account exists and the server runs, but it did not answer on its port in time.</summary>
    ServerNotAnswering
    ''' <summary>Something answers on the port with a certificate that is not the one the server reports: nothing secret was sent.</summary>
    CertificateMismatch
    ''' <summary>The account exists and the server runs; this PC's agent was refused or could not connect.</summary>
    EnrollFailed
    Cancelled
End Enum

''' <summary>The wizard's steps, in order, for the progress line.</summary>
Friend Enum ExchangeWizardStep
    CreatingAccount
    StartingServer
    CheckingCertificate
    EnrollingThisPc
End Enum

''' <summary>The three facts and the fingerprint the wizard ends with. The password is in it, once, so the dialog
''' can show it once; <see cref="Wipe"/> drops it. Nothing here is written anywhere.</summary>
Friend NotInheritable Class ExchangeWizardResult
    Public Property Outcome As ExchangeWizardOutcome = ExchangeWizardOutcome.NotAvailable
    Public Property Login As String = ""
    ''' <summary>The generated password; "" when no account was created.</summary>
    Public Property Password As String = ""
    ''' <summary>The account was created (so its password must be shown whatever came after).</summary>
    Public Property AccountCreated As Boolean
    ''' <summary>The address THIS PC reaches its own server by (loopback).</summary>
    Public Property LocalAddress As String = ""
    ''' <summary>The address announced to other devices, "" while none is set.</summary>
    Public Property PublicEndpoint As String = ""
    Public Property Fingerprint As String = ""
    Public Property StartOutcome As ExchangeProcess.StartOutcome = ExchangeProcess.StartOutcome.Started
    Public Property EnrollResult As ExchangeEnrollResult

    Public Sub Wipe()
        Password = ""
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeWizardResult{" & Outcome.ToString() & "}"
    End Function
End Class

''' <summary>Everything the wizard touches, so a test can drive it with stand-ins and count what it did.</summary>
Friend Interface IExchangeWizardPorts
    Function Inspect() As ExchangeProcess.InspectReport
    Function ServiceExists() As Boolean
    Function IsConsoleRunning() As Boolean
    Function GeneratePassword() As String
    Function AddAccountAsync(login As String, password As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
    Function Start() As ExchangeProcess.StartOutcome
    ''' <summary>The client features are offered (the build's gate or the developer switch).</summary>
    Function AgentOffered() As Boolean
    ''' <summary>First contact with a server: its certificate, no byte of envelope sent. Throws <see cref="ExchangeConnectException"/>.</summary>
    Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
    Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult)
    Function MachineName() As String
    Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task
End Interface

''' <summary>The production ports: the real binary, the real admin channel and the one device agent of this process.</summary>
Friend NotInheritable Class ExchangeWizardPorts
    Implements IExchangeWizardPorts

    Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeWizardPorts.Inspect
        Return ExchangeProcess.Inspect()
    End Function

    Public Function ServiceExists() As Boolean Implements IExchangeWizardPorts.ServiceExists
        Return ExchangeService.Exists()
    End Function

    Public Function IsConsoleRunning() As Boolean Implements IExchangeWizardPorts.IsConsoleRunning
        Return ExchangeProcess.RunningPid() > 0
    End Function

    Public Function GeneratePassword() As String Implements IExchangeWizardPorts.GeneratePassword
        Return ExchangePasswordPolicy.Generate()
    End Function

    Public Function AddAccountAsync(login As String, password As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeWizardPorts.AddAccountAsync
        Return ExchangeAdmin.AddAccountAsync(login, password, ct)
    End Function

    Public Function Start() As ExchangeProcess.StartOutcome Implements IExchangeWizardPorts.Start
        Return ExchangeProcess.Start()
    End Function

    Public Function AgentOffered() As Boolean Implements IExchangeWizardPorts.AgentOffered
        Return ExchangeFeatures.IsEnabled()
    End Function

    Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeWizardPorts.ProbeAsync
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "no agent")
        Return agent.ProbeCertificateAsync(server, ct)
    End Function

    Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeWizardPorts.EnrollAsync
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Return Task.FromResult(New ExchangeEnrollResult(ExchangeEnrollOutcome.BadInput, ExchangeRefusal.Unknown, ""))
        Return agent.EnrollAsync(request, ct)
    End Function

    Public Function MachineName() As String Implements IExchangeWizardPorts.MachineName
        Return Environment.MachineName
    End Function

    Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeWizardPorts.DelayAsync
        Return Task.Delay(milliseconds, ct)
    End Function
End Class

''' <summary>
''' "Use this PC as my server" (ticket 168 section 3.7, Q168-1): for one person serving their own devices, ONE flow that
''' creates the first account with a generated password, starts the local server, checks that what answers on its port
''' really is that server, and enrols THIS PC's own agent (180) with that password, which is then forgotten.
'''
''' It is the same calls as the manual path - the admin channel (162), <c>ExchangeProcess.Start</c> and the agent's
''' <c>EnrollAsync</c> - in a sequence; it adds no capability, no privilege and no elevation. Rules held here:
'''   * the order is account, start, check, enrol: the server refuses to start with no account, and no secret is sent
'''     to anything before its certificate was compared with the one the binary itself reports;
'''   * what answers on the port must show the fingerprint <c>inspect</c> reports, else nothing secret is sent;
'''   * a step that fails stops the flow and says which; an account that was already created is NEVER rolled back
'''     silently - its password stays in the result so it can be shown once;
'''   * the client half runs only where the client features are offered (the gate); without them the flow ends in
'''     <see cref="ExchangeWizardOutcome.DoneNoAgent"/> and says so.
''' </summary>
Friend NotInheritable Class ExchangeServerWizard
    Private Sub New()
    End Sub

    ''' <summary>How long the flow waits for a just-started server to answer: attempts and the pause between them.</summary>
    Friend Const AnswerAttempts As Integer = 12
    Friend Const AnswerPauseMs As Integer = 400

    ''' <summary>The address this PC reaches the local server by: the listen host when it is one address, else loopback.</summary>
    Friend Shared Function LocalAddressOf(listen As String) As String
        Dim port As Integer = ExchangeProcess.PortOfListen(listen)
        If port <= 0 Then port = ExchangeSettings.DefaultPort
        Dim text As String = If(listen, "").Trim()
        Dim colon As Integer = text.LastIndexOf(":"c)
        Dim host As String = If(colon > 0, text.Substring(0, colon), "")
        Dim bare As String = host.Trim("["c, "]"c)
        If bare.Length = 0 OrElse bare = "0.0.0.0" OrElse bare = "::" Then host = "127.0.0.1"
        Return host & ":" & port.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    Friend Shared Async Function RunAsync(login As String, ports As IExchangeWizardPorts, progress As Action(Of ExchangeWizardStep), ct As CancellationToken) As Task(Of ExchangeWizardResult)
        Dim result As New ExchangeWizardResult With {.Login = ExchangeLoginRules.Normalize(login)}
        If ExchangeLoginRules.Check(result.Login) <> ExchangeLoginRules.Verdict.Accepted Then
            result.Outcome = ExchangeWizardOutcome.LoginRefused
            Return result
        End If
        Dim report As ExchangeProcess.InspectReport = ports.Inspect()
        If Not report.Answered OrElse ports.ServiceExists() Then
            result.Outcome = ExchangeWizardOutcome.NotAvailable
            Return result
        End If

        ' 1. the account, with a password only this flow ever sees
        Notify(progress, ExchangeWizardStep.CreatingAccount)
        Dim password As String = ports.GeneratePassword()
        Dim added As ExchangeAdminReply = Await ports.AddAccountAsync(result.Login, password, ct).ConfigureAwait(False)
        If Not added.Ok Then
            result.Outcome = If(added.Code = ExchangeAdminCodes.Exists, ExchangeWizardOutcome.AccountExists, ExchangeWizardOutcome.AccountFailed)
            Return result
        End If
        result.AccountCreated = True
        result.Password = password

        ' 2. the server (it refuses to start with no account, so this comes second)
        ct.ThrowIfCancellationRequested()
        Notify(progress, ExchangeWizardStep.StartingServer)
        If Not ports.IsConsoleRunning() Then
            result.StartOutcome = ports.Start()
            If result.StartOutcome <> ExchangeProcess.StartOutcome.Started AndAlso result.StartOutcome <> ExchangeProcess.StartOutcome.AlreadyRunning Then
                result.Outcome = ExchangeWizardOutcome.StartFailed
                Return result
            End If
        End If

        ' 3. and 4. what answers is the server whose fingerprint the binary reports (the certificate exists after the first start),
        ' then this PC's own agent, with that password
        ct.ThrowIfCancellationRequested()
        Notify(progress, ExchangeWizardStep.CheckingCertificate)
        report = ports.Inspect()
        Await ProbeAndEnrollAsync(result, report, password, ports, progress, ct).ConfigureAwait(False)
        Return result
    End Function

    ''' <summary>
    ''' This PC as the FIRST device of an account that already exists - the account was just added (or its password just reset) in the
    ''' administration page, so its password is in hand for exactly this call. Steps 3 and 4 of <see cref="RunAsync"/> and nothing else:
    ''' no account is created, no server started, no service touched, so it is valid for a registered Windows service too. The caller
    ''' decides that this PC is in no account yet and that the client features are offered; the password is never kept here.
    ''' </summary>
    Friend Shared Async Function EnrollThisPcAsync(login As String, password As String, ports As IExchangeWizardPorts,
                                                   progress As Action(Of ExchangeWizardStep), ct As CancellationToken) As Task(Of ExchangeWizardResult)
        Dim result As New ExchangeWizardResult With {.Login = ExchangeLoginRules.Normalize(login)}
        Notify(progress, ExchangeWizardStep.CheckingCertificate)
        Dim report As ExchangeProcess.InspectReport = ports.Inspect()
        If Not report.Answered Then
            result.Outcome = ExchangeWizardOutcome.NotAvailable
            Return result
        End If
        Await ProbeAndEnrollAsync(result, report, password, ports, progress, ct).ConfigureAwait(False)
        Return result
    End Function

    ''' <summary>The shared tail: the address, the certificate compared with the one the binary reports, then the enrolment. Sets
    ''' <see cref="ExchangeWizardResult.Outcome"/>; nothing secret is sent before the fingerprints agree.</summary>
    Private Shared Async Function ProbeAndEnrollAsync(result As ExchangeWizardResult, report As ExchangeProcess.InspectReport, password As String,
                                                      ports As IExchangeWizardPorts, progress As Action(Of ExchangeWizardStep), ct As CancellationToken) As Task
        result.LocalAddress = LocalAddressOf(report.Listen)
        result.PublicEndpoint = report.PublicEndpoint
        result.Fingerprint = report.Fingerprint
        Dim probe As ExchangeCertificateInfo = Nothing
        If ports.AgentOffered() Then
            probe = Await ProbeWithRetriesAsync(ports, result.LocalAddress, ct).ConfigureAwait(False)
            If probe Is Nothing Then
                result.Outcome = ExchangeWizardOutcome.ServerNotAnswering
                Return
            End If
            If report.Fingerprint.Length = 0 OrElse Not String.Equals(probe.Fingerprint, report.Fingerprint, StringComparison.Ordinal) Then
                result.Outcome = ExchangeWizardOutcome.CertificateMismatch
                Return
            End If
        End If

        If probe Is Nothing Then
            result.Outcome = ExchangeWizardOutcome.DoneNoAgent
            Return
        End If
        ct.ThrowIfCancellationRequested()
        Notify(progress, ExchangeWizardStep.EnrollingThisPc)
        Dim request As New ExchangeEnrollRequest With {.Server = result.LocalAddress, .Login = result.Login, .Password = password,
            .PlatformValid = probe.PlatformValid, .AcceptedFingerprint = probe.Fingerprint, .DeviceName = ports.MachineName()}
        Try
            result.EnrollResult = Await ports.EnrollAsync(request, ct).ConfigureAwait(False)
        Finally
            request.Wipe()
        End Try
        result.Outcome = If(result.EnrollResult.Outcome = ExchangeEnrollOutcome.Enrolled, ExchangeWizardOutcome.Done, ExchangeWizardOutcome.EnrollFailed)
    End Function

    Private Shared Sub Notify(progress As Action(Of ExchangeWizardStep), value As ExchangeWizardStep)
        If progress IsNot Nothing Then progress.Invoke(value)
    End Sub

    ''' <summary>A just-started server needs a moment to accept: ask for its certificate a bounded number of times.</summary>
    Private Shared Async Function ProbeWithRetriesAsync(ports As IExchangeWizardPorts, address As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
        For attempt As Integer = 1 To AnswerAttempts
            ct.ThrowIfCancellationRequested()
            Try
                Return Await ports.ProbeAsync(address, ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException
                ' not answering yet (refused, reset): wait and ask again; any other failure is the same to this flow
            End Try
            If attempt < AnswerAttempts Then Await ports.DelayAsync(AnswerPauseMs, ct).ConfigureAwait(False)
        Next
        Return Nothing
    End Function

End Class
