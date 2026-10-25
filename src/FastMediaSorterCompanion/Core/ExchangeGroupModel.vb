Option Strict On

''' <summary>
''' What one refresh of the "Media Exchange Server" settings group read from the machine: the binary's own
''' report, the console process, the service and the firewall rule. Plain data, so the decision about what
''' the group shows (<see cref="ExchangeGroupModel"/>) is a pure function the tests can drive without a
''' window, a process or a service.
''' </summary>
Friend NotInheritable Class ExchangeSnapshot
    ''' <summary>The parsed <c>inspect</c>; Nothing when it was never asked (the probe-free build pass).</summary>
    Public Property Report As ExchangeProcess.InspectReport
    ''' <summary>Pid of the running console server, 0 when none.</summary>
    Public Property ConsolePid As Integer
    ''' <summary>The service is registered and ours.</summary>
    Public Property ServiceExists As Boolean
    Public Property ServiceState As ServiceControl.ServiceState = ServiceControl.ServiceState.NotInstalled
    Public Property Firewall As ExchangeFirewall.RuleState = ExchangeFirewall.RuleState.Unknown
    ''' <summary>The firewall helper ships and the exe exists.</summary>
    Public Property CanChangeFirewall As Boolean
    ''' <summary>The registration helper ships (the Server edition) and no service exists.</summary>
    Public Property CanRegisterService As Boolean
    ''' <summary>The helper ships and a service exists.</summary>
    Public Property CanRemoveService As Boolean

    ''' <summary>Everything a REFRESH reads, in one place, so the window and the tests share it: the binary's
    ''' report, the console process, the service and the firewall rule. Blocks (spawns a process, asks the
    ''' SCM and netsh) - never call it on the UI thread.</summary>
    Friend Shared Function Read() As ExchangeSnapshot
        Dim s As ExchangeSnapshot = ReadCheap()
        s.Report = ExchangeProcess.Inspect()
        If s.ServiceExists OrElse ExchangeProcess.RunningPid() > 0 Then
            Dim reply As ExchangeAdminReply = ExchangeAdmin.SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpStatus), Threading.CancellationToken.None).GetAwaiter().GetResult()
            If reply.Ok Then
                Dim status As ExchangeAdminStatus = ExchangeAdminParser.Status(reply.DataJson)
                s.Report.Sealed = status.Sealed
                If status.LogRetentionDays > 0 Then
                    s.Report.KeepAwake = status.KeepAwake
                    s.Report.LogRetentionDays = status.LogRetentionDays
                End If
            End If
        End If
        s.ConsolePid = ExchangeProcess.RunningPid()
        s.Firewall = ExchangeFirewall.QueryRule()
        Return s
    End Function

    ''' <summary>The probe-free part (a registry read, an SCM query, two file checks): what the dialog's
    ''' build pass uses so the layout is sound before the live read lands.</summary>
    Friend Shared Function ReadCheap() As ExchangeSnapshot
        Dim s As New ExchangeSnapshot()
        s.ServiceExists = ExchangeService.Exists()
        s.ServiceState = If(s.ServiceExists, ExchangeService.QueryState(), ServiceControl.ServiceState.NotInstalled)
        s.CanChangeFirewall = ExchangeFirewall.CanChange()
        s.CanRegisterService = ExchangeService.CanRegister(s.ServiceExists)
        s.CanRemoveService = ExchangeService.CanRemove(s.ServiceExists)
        Return s
    End Function
End Class

''' <summary>The overall situation, in the order the state line distinguishes them.</summary>
Friend Enum ExchangeRunKind
    ''' <summary>The binary could not be asked.</summary>
    Unreadable
    ''' <summary>No account: the server refuses to serve nobody (162), so there is nothing to start yet.</summary>
    NotConfigured
    Stopped
    RunningConsole
    ServiceStopped
    ServiceStarting
    ServiceRunning
    ServiceStopping
End Enum

''' <summary>
''' The group's decisions, pure (ticket 142 T-3, 144 T-7). Buttons are SHOWN or HIDDEN by state, never
''' disabled - the Hosting rule - so a person never faces a control that will not answer.
''' </summary>
Friend NotInheritable Class ExchangeGroupModel

    Public Property Kind As ExchangeRunKind
    Public Property Pid As Integer
    ''' <summary>The port to show, or 0.</summary>
    Public Property Port As Integer
    Public Property Fingerprint As String = ""
    ''' <summary>How many accounts and devices the server's stores hold (counts only - never a login).</summary>
    Public Property Accounts As Integer
    Public Property Devices As Integer
    ''' <summary>The address announced in every welcome, "" while none is set.</summary>
    Public Property PublicEndpoint As String = ""
    ''' <summary>The server serves a certificate that was imported, not the generated self-signed identity.</summary>
    Public Property CertImported As Boolean
    ''' <summary>The one-click "use this PC as my server" flow: nothing exists yet, nothing runs, no service owns the server.</summary>
    Public Property ShowWizard As Boolean
    ''' <summary>The server's configuration rows (public address, third-party ports, certificate) - whenever the binary answered.</summary>
    Public Property ShowConfigRows As Boolean
    ''' <summary>Replace or import a certificate: only for a server run from here. A registered service keeps its own store, which this user cannot write (144), and no new elevation is added for it (168 rule 3).</summary>
    Public Property ShowCertificateActions As Boolean
    Public Property ShowCertificateServiceNote As Boolean
    ''' <summary>The accounts part: shown whenever the binary answered; the lists fill in from the admin channel.</summary>
    Public Property ShowAccounts As Boolean

    ''' <summary>Console Start: no service (the service wins), nothing running, a usable password.</summary>
    Public Property ShowStart As Boolean
    ''' <summary>Console Stop: a console server runs.</summary>
    Public Property ShowStop As Boolean
    Public Property ShowServiceStart As Boolean
    Public Property ShowServiceStop As Boolean
    Public Property ShowRegisterService As Boolean
    Public Property ShowRemoveService As Boolean
    Public Property ShowServiceRows As Boolean
    ''' <summary>"Reachability from outside is not verified" - while something is serving (133 section 5).</summary>
    Public Property ShowReachabilityNote As Boolean
    Public Property ShowFirewallEnable As Boolean
    Public Property ShowFirewallRemove As Boolean
    Public Property ShowFingerprint As Boolean
    ''' <summary>The "service wins" hint - the console Start is hidden because a service exists.</summary>
    Public Property ShowServiceWinsNote As Boolean
    ''' <summary>The keep-running checkbox only means something for a console server.</summary>
    Public Property ShowKeepRunning As Boolean

    Friend Shared Function Build(s As ExchangeSnapshot) As ExchangeGroupModel
        Dim m As New ExchangeGroupModel()
        Dim r As ExchangeProcess.InspectReport = s.Report
        Dim answered As Boolean = r IsNot Nothing AndAlso r.Answered
        m.Accounts = If(answered, r.Accounts, 0)
        m.Devices = If(answered, r.Devices, 0)
        m.PublicEndpoint = If(answered, r.PublicEndpoint, "")
        m.CertImported = answered AndAlso r.CertImported
        m.Port = If(answered, r.Port, 0)
        m.Fingerprint = If(answered AndAlso r.CertPresent, r.Fingerprint, "")
        m.Pid = s.ConsolePid

        If s.ServiceExists Then
            Select Case s.ServiceState
                Case ServiceControl.ServiceState.Running : m.Kind = ExchangeRunKind.ServiceRunning
                Case ServiceControl.ServiceState.Starting : m.Kind = ExchangeRunKind.ServiceStarting
                Case ServiceControl.ServiceState.Stopping : m.Kind = ExchangeRunKind.ServiceStopping
                Case ServiceControl.ServiceState.Stopped : m.Kind = ExchangeRunKind.ServiceStopped
                Case Else : m.Kind = ExchangeRunKind.Unreadable ' the SCM would not say: never "stopped"
            End Select
        ElseIf s.ConsolePid > 0 Then
            m.Kind = ExchangeRunKind.RunningConsole
        ElseIf r Is Nothing OrElse Not answered Then
            m.Kind = ExchangeRunKind.Unreadable
        ElseIf Not r.Configured Then
            m.Kind = ExchangeRunKind.NotConfigured
        Else
            m.Kind = ExchangeRunKind.Stopped
        End If

        ' Probe-free build pass (no report yet): do not claim "unreadable" - the lazy refresh decides.
        Dim serving As Boolean = m.Kind = ExchangeRunKind.RunningConsole OrElse m.Kind = ExchangeRunKind.ServiceRunning OrElse
                                 m.Kind = ExchangeRunKind.ServiceStarting
        m.ShowReachabilityNote = serving
        m.ShowStop = s.ConsolePid > 0
        m.ShowStart = (Not s.ServiceExists) AndAlso s.ConsolePid = 0 AndAlso answered AndAlso r.Configured
        m.ShowServiceWinsNote = s.ServiceExists
        m.ShowKeepRunning = (Not s.ServiceExists)
        m.ShowServiceStart = s.ServiceExists AndAlso s.ServiceState = ServiceControl.ServiceState.Stopped
        m.ShowServiceStop = s.ServiceExists AndAlso (s.ServiceState = ServiceControl.ServiceState.Running OrElse
                                                      s.ServiceState = ServiceControl.ServiceState.Starting)
        m.ShowRegisterService = s.CanRegisterService AndAlso Not s.ServiceExists
        m.ShowRemoveService = s.CanRemoveService AndAlso s.ServiceExists
        m.ShowServiceRows = s.ServiceExists OrElse s.CanRegisterService
        m.ShowFirewallEnable = s.CanChangeFirewall AndAlso s.Firewall <> ExchangeFirewall.RuleState.ThisProgram
        m.ShowFirewallRemove = s.CanChangeFirewall AndAlso s.Firewall = ExchangeFirewall.RuleState.ThisProgram
        m.ShowFingerprint = m.Fingerprint.Length > 0
        m.ShowWizard = answered AndAlso Not r.Configured AndAlso Not s.ServiceExists AndAlso s.ConsolePid = 0
        m.ShowConfigRows = answered
        m.ShowCertificateActions = answered AndAlso Not s.ServiceExists
        m.ShowCertificateServiceNote = answered AndAlso s.ServiceExists
        m.ShowAccounts = answered
        Return m
    End Function

    ''' <summary>The port to print: the one the binary reported, else the server's default.</summary>
    Friend Function ShownPort() As Integer
        Return If(Port > 0, Port, ExchangeSettings.DefaultPort)
    End Function

    ''' <summary>The group's folded summary - the same facts the body states, in one line (the "folded
    ''' section is live" rule): running with its port, stopped, or no password.</summary>
    Friend Function SummaryText() As String
        Select Case Kind
            Case ExchangeRunKind.RunningConsole
                Return Localization.TF("Работает, порт {0}", ShownPort())
            Case ExchangeRunKind.ServiceRunning, ExchangeRunKind.ServiceStarting
                Return Localization.TF("Работает как служба, порт {0}", ShownPort())
            Case ExchangeRunKind.ServiceStopped, ExchangeRunKind.ServiceStopping
                Return Localization.TF("Служба остановлена, порт {0}", ShownPort())
            Case ExchangeRunKind.NotConfigured
                Return Localization.T("Нет учётных записей")
            Case ExchangeRunKind.Unreadable
                Return Localization.T("Состояние не прочитано")
            Case Else
                Return Localization.TF("Остановлен, порт {0}", ShownPort())
        End Select
    End Function

End Class
