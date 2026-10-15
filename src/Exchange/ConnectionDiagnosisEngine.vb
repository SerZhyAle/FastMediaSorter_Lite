#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Target of the connection check.
''' </summary>
Public Enum ConnectionDiagnosisTarget
    Server = 1
    Device = 2
End Enum

''' <summary>
''' Outcome of an individual check step probe (Spec 232 rule 1).
''' </summary>
Public Enum ProbeOutcome
    Passed = 1
    Failed = 2
    CouldNotCheck = 3
End Enum

''' <summary>
''' Context information supplied to the diagnosis engine.
''' </summary>
Public Class ConnectionDiagnosisContext
    Public Property Host As String = String.Empty
    Public Property Port As Integer = 0
    Public Property Target As ConnectionDiagnosisTarget = ConnectionDiagnosisTarget.Server
    Public Property InitialMessage As ConnectionDiagnosisMessage = Nothing
    Public Property IsTls As Boolean = True
    Public Property VpnActiveOverride As Nullable(Of Boolean) = Nothing
    Public Property CertificateTimeInvalid As Boolean = False
    Public Property PlatformValid As Nullable(Of Boolean) = Nothing

    Public Sub New()
    End Sub

    Public Sub New(host As String, port As Integer, Optional target As ConnectionDiagnosisTarget = ConnectionDiagnosisTarget.Server, Optional initialMessage As ConnectionDiagnosisMessage = Nothing)
        Me.Host = If(host, String.Empty).Trim()
        Me.Port = port
        Me.Target = target
        Me.InitialMessage = If(initialMessage, ConnectionDiagnosisMessages.UnreachableServer)
    End Sub
End Class

''' <summary>
''' Result of running the step-by-step diagnosis.
''' </summary>
Public Class ConnectionDiagnosisResult
    Public Property Message As ConnectionDiagnosisMessage
    Public Property Target As ConnectionDiagnosisTarget
    Public Property Route As ConnectionDiagnosisApplies
    Public Property FirstFailedStep As Nullable(Of ConnectionDiagnosisStep)
    Public Property StepOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome)()
    Public Property WalkedSteps As New List(Of ConnectionDiagnosisStep)()

    Public Sub New(message As ConnectionDiagnosisMessage, target As ConnectionDiagnosisTarget, route As ConnectionDiagnosisApplies)
        Me.Message = message
        Me.Target = target
        Me.Route = route
    End Sub

    ''' <summary>
    ''' Spec 232 rule 6: Support bundle diagnostic code serialization.
    ''' Contains step and message codes only; NEVER contains an address, login, password or fingerprint.
    ''' </summary>
    Public Function ToRedactedDiagnosticString() As String
        Dim sb As New System.Text.StringBuilder()
        sb.Append("msg=").Append(If(Message?.Code, "unknown"))
        sb.Append(";target=").Append(If(Target = ConnectionDiagnosisTarget.Server, "server", "device"))
        sb.Append(";route=").Append(If(Route = ConnectionDiagnosisApplies.Network, "network", "internet"))
        If FirstFailedStep.HasValue Then
            sb.Append(";fail=").Append(ConnectionDiagnosisStepInfo.IdOf(FirstFailedStep.Value))
        Else
            sb.Append(";fail=none")
        End If
        sb.Append(";walk=")
        Dim first As Boolean = True
        For Each st As ConnectionDiagnosisStep In WalkedSteps
            If Not first Then sb.Append(",")
            first = False
            sb.Append(ConnectionDiagnosisStepInfo.IdOf(st))
            If StepOutcomes.ContainsKey(st) Then
                sb.Append(":").Append(StepOutcomes(st).ToString().ToLowerInvariant())
            End If
        Next
        Return sb.ToString()
    End Function

    Public Overrides Function ToString() As String
        Return ToRedactedDiagnosticString()
    End Function
End Class

''' <summary>
''' Probe provider seam for testing.
''' </summary>
Public Interface IConnectionDiagnosisProbeProvider
    Function ProbeAsync(stepValue As ConnectionDiagnosisStep, context As ConnectionDiagnosisContext, ct As CancellationToken) As Task(Of ProbeOutcome)
End Interface

''' <summary>
''' Spec 232 rules 1, 2, 4: Read-only diagnosis engine for connection failures.
''' Runs only on explicit user request; changes no system settings; stops at first failure.
''' </summary>
Public Class ConnectionDiagnosisEngine

    Private ReadOnly _probeProvider As IConnectionDiagnosisProbeProvider

    Public Sub New(Optional probeProvider As IConnectionDiagnosisProbeProvider = Nothing)
        _probeProvider = If(probeProvider, New DefaultConnectionDiagnosisProbeProvider())
    End Sub

    ''' <summary>
    ''' Classifies whether a host belongs to the local network or the internet (item CY).
    ''' </summary>
    Public Shared Function RouteOf(host As String) As ConnectionDiagnosisApplies
        If String.IsNullOrWhiteSpace(host) Then Return ConnectionDiagnosisApplies.Network
        Dim clean As String = host.Trim().Trim("["c, "]"c)

        ' Loopback and localhost
        If String.Equals(clean, "localhost", StringComparison.OrdinalIgnoreCase) Then Return ConnectionDiagnosisApplies.Network
        If clean = "127.0.0.1" OrElse clean = "::1" Then Return ConnectionDiagnosisApplies.Network

        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(clean, ip) Then
            If IPAddress.IsLoopback(ip) Then Return ConnectionDiagnosisApplies.Network
            If ip.IsIPv6LinkLocal OrElse ip.IsIPv6SiteLocal Then Return ConnectionDiagnosisApplies.Network

            Dim bytes As Byte() = ip.GetAddressBytes()
            If ip.AddressFamily = AddressFamily.InterNetwork Then
                ' RFC 1918 10.0.0.0/8
                If bytes(0) = 10 Then Return ConnectionDiagnosisApplies.Network
                ' RFC 1918 172.16.0.0/12
                If bytes(0) = 172 AndAlso (bytes(1) >= 16 AndAlso bytes(1) <= 31) Then Return ConnectionDiagnosisApplies.Network
                ' RFC 1918 192.168.0.0/16
                If bytes(0) = 192 AndAlso bytes(1) = 168 Then Return ConnectionDiagnosisApplies.Network
                ' Link-local 169.254.0.0/16
                If bytes(0) = 169 AndAlso bytes(1) = 254 Then Return ConnectionDiagnosisApplies.Network
                ' CGNAT 100.64.0.0/10
                If bytes(0) = 100 AndAlso (bytes(1) >= 64 AndAlso bytes(1) <= 127) Then Return ConnectionDiagnosisApplies.Network
            ElseIf ip.AddressFamily = AddressFamily.InterNetworkV6 Then
                ' fc00::/7 (Unique local)
                If (bytes(0) And &HFE) = &HFC Then Return ConnectionDiagnosisApplies.Network
                ' fe80::/10 (Link-local)
                If bytes(0) = &HFE AndAlso (bytes(1) And &HC0) = &H80 Then Return ConnectionDiagnosisApplies.Network
            End If
            Return ConnectionDiagnosisApplies.Internet
        End If

        ' Hostnames without dot: local machine name
        If Not clean.Contains("."c) Then Return ConnectionDiagnosisApplies.Network

        ' Standard LAN domain suffixes
        Dim lower As String = clean.ToLowerInvariant()
        If lower.EndsWith(".local", StringComparison.Ordinal) OrElse
           lower.EndsWith(".lan", StringComparison.Ordinal) OrElse
           lower.EndsWith(".home", StringComparison.Ordinal) OrElse
           lower.EndsWith(".internal", StringComparison.Ordinal) OrElse
           lower.EndsWith(".home.arpa", StringComparison.Ordinal) Then
            Return ConnectionDiagnosisApplies.Network
        End If

        Return ConnectionDiagnosisApplies.Internet
    End Function

    ''' <summary>
    ''' Detects address family of a literal IP host. Returns Nothing if host is a domain name.
    ''' </summary>
    Public Shared Function FamilyOf(host As String) As Nullable(Of AddressFamily)
        If String.IsNullOrWhiteSpace(host) Then Return Nothing
        Dim clean As String = host.Trim().Trim("["c, "]"c)
        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(clean, ip) Then
            Return ip.AddressFamily
        End If
        Return Nothing
    End Function

    ''' <summary>
    ''' Computes the ordered list of applicable check steps for the message and context.
    ''' </summary>
    Public Shared Function ApplicableSteps(message As ConnectionDiagnosisMessage, target As ConnectionDiagnosisTarget, route As ConnectionDiagnosisApplies) As List(Of ConnectionDiagnosisStep)
        Dim list As New List(Of ConnectionDiagnosisStep)()
        If message Is Nothing OrElse Not message.StepValue.HasValue Then Return list

        If Not message.IsWalked Then
            ' Single step defined by table row
            Dim s As ConnectionDiagnosisStep = message.StepValue.Value
            If IsStepApplicable(s, target, route) Then
                list.Add(s)
            End If
            Return list
        End If

        ' Walked message: walk from message.StepValue onwards to the end of the 15 steps
        Dim startStep As ConnectionDiagnosisStep = message.StepValue.Value
        For i As Integer = CInt(startStep) To 15
            Dim s As ConnectionDiagnosisStep = CType(i, ConnectionDiagnosisStep)
            If IsStepApplicable(s, target, route) Then
                list.Add(s)
            End If
        Next
        Return list
    End Function

    Private Shared Function IsStepApplicable(stepVal As ConnectionDiagnosisStep, target As ConnectionDiagnosisTarget, route As ConnectionDiagnosisApplies) As Boolean
        Dim stepApplies As ConnectionDiagnosisApplies = ConnectionDiagnosisStepInfo.AppliesOf(stepVal)
        If stepApplies = ConnectionDiagnosisApplies.Network AndAlso route <> ConnectionDiagnosisApplies.Network Then
            Return False
        End If
        If stepApplies = ConnectionDiagnosisApplies.Internet AndAlso route <> ConnectionDiagnosisApplies.Internet Then
            Return False
        End If
        ' windows-public-network applies only to server targets
        If ConnectionDiagnosisStepInfo.IsWindowsOnly(stepVal) AndAlso target <> ConnectionDiagnosisTarget.Server Then
            Return False
        End If
        Return True
    End Function

    ''' <summary>
    ''' Runs the step-by-step diagnosis asynchronously until the first failure or completion.
    ''' </summary>
    Public Async Function DiagnoseAsync(context As ConnectionDiagnosisContext, ct As CancellationToken) As Task(Of ConnectionDiagnosisResult)
        If context Is Nothing Then context = New ConnectionDiagnosisContext()
        Dim msg As ConnectionDiagnosisMessage = If(context.InitialMessage, ConnectionDiagnosisMessages.UnreachableServer)
        Dim route As ConnectionDiagnosisApplies = RouteOf(context.Host)
        Dim result As New ConnectionDiagnosisResult(msg, context.Target, route)

        Dim steps As List(Of ConnectionDiagnosisStep) = ApplicableSteps(msg, context.Target, route)
        result.WalkedSteps = steps

        For Each s As ConnectionDiagnosisStep In steps
            ct.ThrowIfCancellationRequested()
            Dim outcome As ProbeOutcome = Await _probeProvider.ProbeAsync(s, context, ct).ConfigureAwait(False)
            result.StepOutcomes(s) = outcome

            If outcome = ProbeOutcome.Failed Then
                result.FirstFailedStep = s
                ' Stop at the first failure (Spec 232 rule 1)
                Exit For
            End If
            ' CouldNotCheck or Passed: engine proceeds
        Next

        ' If none failed but the message has a fixed step, ensure it is designated as first failed step
        If Not result.FirstFailedStep.HasValue AndAlso Not msg.IsWalked AndAlso msg.StepValue.HasValue Then
            result.FirstFailedStep = msg.StepValue.Value
        ElseIf Not result.FirstFailedStep.HasValue AndAlso steps.Count > 0 Then
            ' If all passed or could not check, point to the first applicable step as advice
            result.FirstFailedStep = steps(0)
        End If

        Return result
    End Function

End Class

''' <summary>
''' Default read-only probes for Windows desktop.
''' Changes no settings, makes no background queries.
''' </summary>
Public Class DefaultConnectionDiagnosisProbeProvider
    Implements IConnectionDiagnosisProbeProvider

    Public Async Function ProbeAsync(stepValue As ConnectionDiagnosisStep, context As ConnectionDiagnosisContext, ct As CancellationToken) As Task(Of ProbeOutcome) Implements IConnectionDiagnosisProbeProvider.ProbeAsync
        Try
            Select Case stepValue
                Case ConnectionDiagnosisStep.PcAsleep
                    Return Await ProbeTcpConnectAsync(context.Host, context.Port, 1200, ct).ConfigureAwait(False)

                Case ConnectionDiagnosisStep.AddressLan
                    If String.IsNullOrWhiteSpace(context.Host) Then Return ProbeOutcome.Failed
                    Return If(NetworkInterface.GetIsNetworkAvailable(), ProbeOutcome.Passed, ProbeOutcome.Failed)

                Case ConnectionDiagnosisStep.GuestNetwork
                    ' Read-only: Cannot query router AP isolation directly from desktop without router credentials
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.Vpn
                    If context.VpnActiveOverride.HasValue Then
                        Return If(context.VpnActiveOverride.Value, ProbeOutcome.Failed, ProbeOutcome.Passed)
                    End If
                    Dim vpnActive As Boolean = CheckVpnActive()
                    Return If(vpnActive, ProbeOutcome.Failed, ProbeOutcome.Passed)

                Case ConnectionDiagnosisStep.WindowsPublicNetwork
                    Dim isPublic As Boolean = CheckNetworkIsPublic()
                    Return If(isPublic, ProbeOutcome.Failed, ProbeOutcome.Passed)

                Case ConnectionDiagnosisStep.IpFamily
                    Dim fam As Nullable(Of AddressFamily) = ConnectionDiagnosisEngine.FamilyOf(context.Host)
                    If fam.HasValue Then
                        Dim hasFamily As Boolean = CheckLocalHasFamily(fam.Value)
                        Return If(hasFamily, ProbeOutcome.Passed, ProbeOutcome.Failed)
                    End If
                    Return ProbeOutcome.Passed

                Case ConnectionDiagnosisStep.ServerInternet
                    Return Await ProbeTcpConnectAsync(context.Host, context.Port, 2000, ct).ConfigureAwait(False)

                Case ConnectionDiagnosisStep.PortForward
                    ' From client side, external port forwarding failure is evidenced by connect timeout/refusal from outside
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.DeviceClock
                    If context.CertificateTimeInvalid Then Return ProbeOutcome.Failed
                    Return ProbeOutcome.Passed

                Case ConnectionDiagnosisStep.Certificate
                    If context.PlatformValid.HasValue AndAlso Not context.PlatformValid.Value Then
                        Return ProbeOutcome.Failed
                    End If
                    Return ProbeOutcome.Passed

                Case ConnectionDiagnosisStep.Credentials
                    If context.InitialMessage Is ConnectionDiagnosisMessages.BadCredentials OrElse
                       context.InitialMessage Is ConnectionDiagnosisMessages.BadCredentialsCode Then
                        Return ProbeOutcome.Failed
                    End If
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.SignInRate
                    If context.InitialMessage Is ConnectionDiagnosisMessages.RateLimited Then
                        Return ProbeOutcome.Failed
                    End If
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.Invitation
                    If context.InitialMessage Is ConnectionDiagnosisMessages.InvitationInvalid Then
                        Return ProbeOutcome.Failed
                    End If
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.DeviceRemoved
                    If context.InitialMessage Is ConnectionDiagnosisMessages.DeviceRevoked Then
                        Return ProbeOutcome.Failed
                    End If
                    Return ProbeOutcome.CouldNotCheck

                Case ConnectionDiagnosisStep.Operator
                    Return ProbeOutcome.CouldNotCheck

                Case Else
                    Return ProbeOutcome.CouldNotCheck
            End Select
        Catch ex As OperationCanceledException
            Return ProbeOutcome.CouldNotCheck
        Catch ex As Exception
            Return ProbeOutcome.CouldNotCheck
        End Try
    End Function

    Private Shared Async Function ProbeTcpConnectAsync(host As String, port As Integer, timeoutMs As Integer, ct As CancellationToken) As Task(Of ProbeOutcome)
        If String.IsNullOrWhiteSpace(host) OrElse port <= 0 OrElse port > 65535 Then
            Return ProbeOutcome.CouldNotCheck
        End If
        Try
            Using client As New TcpClient()
                Dim connectTask As Task = client.ConnectAsync(host.Trim().Trim("["c, "]"c), port)
                Dim finished As Task = Await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(False)
                If ct.IsCancellationRequested Then Return ProbeOutcome.CouldNotCheck
                If finished IsNot connectTask Then
                    Return ProbeOutcome.Failed
                End If
                Await connectTask.ConfigureAwait(False)
                If client.Connected Then Return ProbeOutcome.Passed
                Return ProbeOutcome.Failed
            End Using
        Catch ex As SocketException
            Return ProbeOutcome.Failed
        Catch ex As Exception
            Return ProbeOutcome.Failed
        End Try
    End Function

    Private Shared Function CheckVpnActive() As Boolean
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus = OperationalStatus.Up Then
                    If nic.NetworkInterfaceType = NetworkInterfaceType.Ppp Then Return True
                    Dim desc As String = nic.Description.ToLowerInvariant()
                    Dim name As String = nic.Name.ToLowerInvariant()
                    If desc.Contains("vpn") OrElse desc.Contains("wireguard") OrElse desc.Contains("tap-") OrElse desc.Contains("tun") OrElse
                       name.Contains("vpn") OrElse name.Contains("wireguard") Then
                        Return True
                    End If
                End If
            Next
        Catch
        End Try
        Return False
    End Function

    Private Shared Function CheckNetworkIsPublic() As Boolean
        Try
            ' In modern Windows, registry holds network profiles: Category 0 = Public, 1 = Private, 2 = Domain
            Using key As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles", False)
                If key IsNot Nothing Then
                    For Each subKeyName As String In key.GetSubKeyNames()
                        Using subKey As Microsoft.Win32.RegistryKey = key.OpenSubKey(subKeyName, False)
                            If subKey IsNot Nothing Then
                                Dim cat As Object = subKey.GetValue("Category")
                                If cat IsNot Nothing AndAlso TypeOf cat Is Integer Then
                                    If CInt(cat) = 0 Then Return True
                                End If
                            End If
                        End Using
                    Next
                End If
            End Using
        Catch
        End Try
        Return False
    End Function

    Private Shared Function CheckLocalHasFamily(family As AddressFamily) As Boolean
        Try
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus = OperationalStatus.Up Then
                    Dim ipProps As IPInterfaceProperties = nic.GetIPProperties()
                    For Each addr As UnicastIPAddressInformation In ipProps.UnicastAddresses
                        If addr.Address.AddressFamily = family AndAlso Not IPAddress.IsLoopback(addr.Address) Then
                            Return True
                        End If
                    Next
                End If
            Next
        Catch
        End Try
        Return False
    End Function

End Class
#End If
