Option Strict On

Imports System.Windows.Forms

''' <summary>
''' What only the Share Manager knows, handed to the shared hosting report (ticket 182 L-4, L-8): who hosts
''' the worker (<see cref="ServerFeatures.HostMode"/>) and what the worker itself last said about being
''' reached from outside. <see cref="HostingReport"/> is linked into the viewer too, which has neither, so
''' these two answers are registered once at start and the report reads them as optional.
'''
''' It also writes the ONE state line at the start of companion.log: product version, OS and build, Server
''' or client, session and RDP, host mode, which components exist and whether each service is registered -
''' so a log that arrives without anything else already says what machine it came from. No secret is in it
''' (the same facts the [hosting] block carries, cheap subset only).
''' </summary>
Friend Module HostingContext

    ''' <summary>Called once, before anything asks. Idempotent.</summary>
    Friend Sub Register()
        HostingProbes.HostModeProvider = AddressOf HostModeText
        HostingProbes.ReachabilityProvider = AddressOf ReachabilityText
    End Sub

    ''' <summary>The Share Manager's own reading, as the enum's name. Never throws.</summary>
    Friend Function HostModeText() As String
        Try
            Return ServerFeatures.HostMode().ToString()
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The worker's last word on reachability, one line, or "" when no worker answers. Asks the worker's
    ''' control pipe for its status with a short bound; it never spawns the worker and never touches the network
    ''' itself (the external check it reports was made by the worker earlier).</summary>
    Friend Function ReachabilityText() As String
        Try
            If Not WorkerProcess.IsAvailable() Then Return ""
            Dim response As WorkerResponse = WorkerProcess.TryGetStatus(1500)
            If response Is Nothing Then Return "the worker did not answer"
            If response.Ok = False OrElse response.Status Is Nothing Then Return "the worker answered without a status"
            Return ReachabilityLine(response.Status)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Pure: the facts of <see cref="WorkerStatus"/> that say whether the share can be reached, and
    ''' none that say where (no host, no address, no port number a device is told, no fingerprint).</summary>
    Friend Function ReachabilityLine(status As WorkerStatus) As String
        If status Is Nothing Then Return ""
        Dim parts As New List(Of String)()
        parts.Add("server " & If(status.Running, "running", "not running"))
        Dim reach As WorkerReachability = status.Reachability
        If reach Is Nothing Then
            parts.Add("the worker reports no reachability facts")
        Else
            parts.Add("external port checked " & If(reach.ExternalPortChecked, "yes", "no"))
            If reach.ExternalPortChecked Then parts.Add("external port open " & If(reach.ExternalPortOpen, "yes", "no"))
            parts.Add("CGNAT " & If(reach.IsCgnat, "yes", "no"))
            If Not String.IsNullOrEmpty(reach.PortMapMethod) Then parts.Add("port mapping " & reach.PortMapMethod)
        End If
        Return String.Join("; ", parts)
    End Function

    ''' <summary>Writes the state line into companion.log. Cheap (registry reads, two SCM calls, a few
    ''' File.Exists) and never throws.</summary>
    Friend Sub LogStartState()
        Try
            Dim line As String = HostingReport.BuildLiveStateLine("Share Manager", Application.ProductVersion)
            AppFileLogger.WriteLine(line)
        Catch
        End Try
    End Sub

End Module
