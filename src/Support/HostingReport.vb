#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text

''' <summary>
''' The "[hosting]" block of report.txt (ticket 182 L-4) and the one state line a Share Manager writes
''' into its log at start (L-8): what an author needs to read a machine without asking the owner
''' anything - which Windows it is, how much memory and disk it has (the exchange server's Argon2id
''' bound depends on both), whether this is a remote-desktop session, what the two Windows services
''' are doing, which firewall rules name our programs, which ports are configured and which sockets
''' our processes really hold, which versions and components are installed, which data directories
''' this user can read, and whether a mail client exists (the "Send the log" route depends on it).
'''
''' Pure by construction: <see cref="Render"/> turns a <see cref="HostingFacts"/> into text and touches
''' nothing; <see cref="Gather"/> fills the facts through injected <see cref="HostingProbes"/>, each call
''' guarded, so a probe that throws costs its own line and nothing else (the tests drive both with
''' stubs and need no service, no firewall and no elevation). The real probes are
''' <see cref="HostingProbesLive"/>.
'''
''' What this block NEVER carries - held by a test that seeds each of them and reads the whole text:
''' a login or any account name other than the three well-known service accounts, a password, a hash,
''' a token, a certificate fingerprint, the public endpoint (host, address or port a device is told),
''' the specific address a socket is bound to (only "any" / "loopback" / "one address"), or the name
''' of a user profile. The user-profile folder is also replaced in the report as a whole
''' (<see cref="LogPackage.RedactUserProfile(String)"/>), which is a second line, never the first.
''' </summary>
Friend Module HostingReport

    ''' <summary>The frozen service names the block asks the SCM about (equal to
    ''' <c>ServiceControl.ServiceName</c> and <c>ExchangeService.ServiceName</c> in the Share Manager; a
    ''' Companion.Tests test holds the three to one string).</summary>
    Friend Const ShareServiceName As String = "FastMediaSorterCompanionSFTP"
    Friend Const ExchangeServiceName As String = "FastMediaSorterExchangeServer"

    ' --- the facts ------------------------------------------------------------------------

    Friend Structure OsFacts
        ''' <summary>False when the probe could not say anything.</summary>
        Public Known As Boolean
        Public ProductName As String
        ''' <summary>Major.minor.build, e.g. 10.0.26100.</summary>
        Public Version As String
        ''' <summary>Build with the update revision when known, e.g. 26100.4061.</summary>
        Public Build As String
        Public EditionId As String
        ''' <summary>The registry's InstallationType: Client, Server, Server Core. "" when unknown.</summary>
        Public InstallationType As String
        Public IsServer As Boolean
        Public Is64BitOs As Boolean
        ''' <summary>0 = unknown.</summary>
        Public TotalMemoryMb As Long
        Public ProcessorCount As Integer
        Public SystemDrive As String
        ''' <summary>-1 = unknown.</summary>
        Public SystemDriveFreeMb As Long
        Public SystemDriveTotalMb As Long
    End Structure

    Friend Structure SessionFacts
        Public Known As Boolean
        Public SessionId As Integer
        Public IsRemoteDesktop As Boolean
    End Structure

    Friend Structure ServiceFacts
        Public Name As String
        ''' <summary>True when the SCM (or, if it refused the question, the registry) knows the service.</summary>
        Public Registered As Boolean
        ''' <summary>True when "registered or not" could not be decided at all.</summary>
        Public QueryFailed As Boolean
        ''' <summary>Stopped / Starting / Stopping / Running / Paused, or "unknown" when the SCM refused the question.</summary>
        Public State As String
        ''' <summary>Automatic / Automatic (delayed) / Manual / Disabled / Boot / System, or "".</summary>
        Public StartType As String
        ''' <summary>The registered account exactly as Windows stores it, or "". Rendered through
        ''' <see cref="AccountLabel"/>, which never lets a user's login out.</summary>
        Public Account As String
        ''' <summary>The service's last exit code (Win32), Nothing when unknown.</summary>
        Public Win32ExitCode As Integer?
        Public ServiceSpecificExitCode As Integer?
    End Structure

    Friend Structure ProgramFacts
        ''' <summary>Which program and which copy ("share worker, installed", "exchange server, service copy").</summary>
        Public Label As String
        Public Present As Boolean
        ''' <summary>The file version resource, or "" when there is none (a Go program has none unless one was
        ''' linked in). Nothing is ever run to get it.</summary>
        Public Version As String
        ''' <summary>"12345678 bytes, modified 2026-10-07 11:22 UTC" - what identifies a build that carries no version
        ''' resource - or "".</summary>
        Public Stamp As String
        ''' <summary>Names of the firewall rules aimed at this exact program; Nothing = the rules could not be read.</summary>
        Public FirewallRules As List(Of String)
    End Structure

    Friend Structure SocketFacts
        ''' <summary>The process's name (never its command line).</summary>
        Public Process As String
        Public ProcessId As Integer
        Public Port As Integer
        ''' <summary>"any IPv4", "any IPv6", "loopback" or "one address" - never the address itself.</summary>
        Public Scope As String
    End Structure

    Friend Structure DirectoryFacts
        Public Label As String
        Public Exists As Boolean
        Public Readable As Boolean
        ''' <summary>Why it is not readable ("access denied", ...), or "".</summary>
        Public Reason As String
    End Structure

    Friend Structure PortFacts
        ''' <summary>0 = unknown (not configured or not readable by this user).</summary>
        Public ExchangeListen As Integer
        Public ExchangeRangeLow As Integer
        Public ExchangeRangeHigh As Integer
        Public ShareListen As Integer
    End Structure

    Friend NotInheritable Class HostingFacts
        Public Os As OsFacts
        Public Session As SessionFacts
        Public Services As New List(Of ServiceFacts)()
        ''' <summary>The Share Manager's own reading of who hosts the worker ("None", "UserSession",
        ''' "SystemService"), or "" in a program that has no such notion (the viewer).</summary>
        Public HostMode As String = ""
        Public Programs As New List(Of ProgramFacts)()
        Public Ports As PortFacts
        ''' <summary>Nothing = the socket table could not be read.</summary>
        Public Sockets As List(Of SocketFacts)
        ''' <summary>Component name -> installed.</summary>
        Public Components As New List(Of KeyValuePair(Of String, Boolean))()
        Public Directories As New List(Of DirectoryFacts)()
        ''' <summary>Nothing = not determined.</summary>
        Public MailHandler As Boolean?
        ''' <summary>A short fact line about the worker's own reachability check, or "".</summary>
        Public Reachability As String = ""
        ''' <summary>Names of the probes that threw (their lines say "not available").</summary>
        Public ProbeFailures As New List(Of String)()
    End Class

    ' --- the probes -----------------------------------------------------------------------

    ''' <summary>
    ''' Every question the block asks, each a delegate so a test answers it from a table. A delegate left
    ''' Nothing is "not asked" - the line then says so instead of guessing. <see cref="Absent"/> is the
    ''' empty machine (no services, no component, nothing listening).
    ''' </summary>
    Friend NotInheritable Class HostingProbes
        Public Os As Func(Of OsFacts)
        Public Session As Func(Of SessionFacts)
        ''' <summary>One service by its SCM name.</summary>
        Public ServiceInfo As Func(Of String, ServiceFacts)
        Public HostMode As Func(Of String)
        ''' <summary>Label -> absolute path of each program copy the firewall and the versions are asked about.</summary>
        Public ProgramPaths As Func(Of List(Of KeyValuePair(Of String, String)))
        ''' <summary>File version resource of a path; Nothing when the file does not exist, "" when unreadable.</summary>
        Public FileVersion As Func(Of String, String)
        ''' <summary>Size and modification time of a path, as one phrase, or Nothing when the file is absent.</summary>
        Public FileStamp As Func(Of String, String)
        ''' <summary>Names of the firewall rules whose program is exactly this path; Nothing when unreadable.</summary>
        Public FirewallRules As Func(Of String, List(Of String))
        Public Ports As Func(Of PortFacts)
        Public Sockets As Func(Of List(Of SocketFacts))
        Public Components As Func(Of List(Of KeyValuePair(Of String, Boolean)))
        Public Directories As Func(Of List(Of DirectoryFacts))
        Public MailHandler As Func(Of Boolean)
        Public Reachability As Func(Of String)

        ''' <summary>Set once at start by the program that knows the answer (the Share Manager: its
        ''' <c>ServerFeatures.HostMode()</c>). The viewer leaves it Nothing - it has no such notion.</summary>
        Friend Shared HostModeProvider As Func(Of String)
        ''' <summary>Set once at start by the Share Manager: the worker's external-port check as one short
        ''' line (it asks its cached status, never the network). Nothing in the viewer.</summary>
        Friend Shared ReachabilityProvider As Func(Of String)

        ''' <summary>The empty machine: every probe answers "nothing here". The tests start from it and set
        ''' only what a case needs.</summary>
        Friend Shared Function Absent() As HostingProbes
            Return New HostingProbes With {
                .Os = Function() New OsFacts With {.Known = False},
                .Session = Function() New SessionFacts With {.Known = False},
                .ServiceInfo = Function(name) New ServiceFacts With {.Name = name, .Registered = False, .State = "", .StartType = "", .Account = ""},
                .HostMode = Function() "",
                .ProgramPaths = Function() New List(Of KeyValuePair(Of String, String))(),
                .FileVersion = Function(path) Nothing,
                .FileStamp = Function(path) Nothing,
                .FirewallRules = Function(path) New List(Of String)(),
                .Ports = Function() New PortFacts(),
                .Sockets = Function() New List(Of SocketFacts)(),
                .Components = Function() New List(Of KeyValuePair(Of String, Boolean))(),
                .Directories = Function() New List(Of DirectoryFacts)(),
                .MailHandler = Function() False,
                .Reachability = Function() ""}
        End Function
    End Class

    ' --- gathering ------------------------------------------------------------------------

    ''' <summary>
    ''' Asks every probe. A probe that throws (or is not supplied) is recorded in
    ''' <see cref="HostingFacts.ProbeFailures"/> and leaves its fact at "unknown" - the block as a whole
    ''' never throws, because a diagnostics page that dies on the first odd machine is worth nothing
    ''' exactly where it is needed.
    ''' </summary>
    ''' <param name="cheapOnly">The start-up state line asks only what costs a registry read or an SCM
    ''' call: no firewall, no socket table, no directory listing, no reachability.</param>
    Friend Function Gather(probes As HostingProbes, Optional cheapOnly As Boolean = False) As HostingFacts
        Dim facts As New HostingFacts()
        If probes Is Nothing Then Return facts

        facts.Os = Ask(Of OsFacts)(facts, "os", probes.Os, New OsFacts With {.Known = False})
        facts.Session = Ask(Of SessionFacts)(facts, "session", probes.Session, New SessionFacts With {.Known = False})
        For Each name As String In {ShareServiceName, ExchangeServiceName}
            Dim serviceName As String = name
            Dim serviceProbe As Func(Of ServiceFacts) = Nothing
            If probes.ServiceInfo IsNot Nothing Then serviceProbe = Function() probes.ServiceInfo(serviceName)
            Dim fact As ServiceFacts = Ask(Of ServiceFacts)(facts, "service " & serviceName, serviceProbe,
                New ServiceFacts With {.Name = serviceName, .QueryFailed = True, .State = "unknown", .StartType = "", .Account = ""})
            If String.IsNullOrEmpty(fact.Name) Then fact.Name = serviceName
            facts.Services.Add(fact)
        Next
        facts.HostMode = Ask(Of String)(facts, "host mode", probes.HostMode, "")
        facts.Components = Ask(Of List(Of KeyValuePair(Of String, Boolean)))(facts, "components", probes.Components,
                                                                           New List(Of KeyValuePair(Of String, Boolean))())

        If cheapOnly Then Return facts

        Dim paths As List(Of KeyValuePair(Of String, String)) =
            Ask(Of List(Of KeyValuePair(Of String, String)))(facts, "program paths", probes.ProgramPaths,
                                                              New List(Of KeyValuePair(Of String, String))())
        For Each entry As KeyValuePair(Of String, String) In paths
            Dim path As String = entry.Value
            Dim program As New ProgramFacts With {.Label = entry.Key, .Present = False, .Version = ""}
            Dim versionProbe As Func(Of String) = Nothing
            If probes.FileVersion IsNot Nothing Then versionProbe = Function() probes.FileVersion(path)
            Dim version As String = Ask(Of String)(facts, "version of " & entry.Key, versionProbe, Nothing)
            program.Present = version IsNot Nothing
            program.Version = If(version, "")
            If program.Present Then
                Dim stampProbe As Func(Of String) = Nothing
                If probes.FileStamp IsNot Nothing Then stampProbe = Function() probes.FileStamp(path)
                program.Stamp = Ask(Of String)(facts, "stamp of " & entry.Key, stampProbe, "")
                If program.Stamp Is Nothing Then program.Stamp = ""
                Dim rulesProbe As Func(Of List(Of String)) = Nothing
                If probes.FirewallRules IsNot Nothing Then rulesProbe = Function() probes.FirewallRules(path)
                program.FirewallRules = Ask(Of List(Of String))(facts, "firewall rules of " & entry.Key, rulesProbe, Nothing)
            End If
            facts.Programs.Add(program)
        Next
        facts.Ports = Ask(Of PortFacts)(facts, "ports", probes.Ports, New PortFacts())
        facts.Sockets = Ask(Of List(Of SocketFacts))(facts, "sockets", probes.Sockets, Nothing)
        facts.Directories = Ask(Of List(Of DirectoryFacts))(facts, "directories", probes.Directories, New List(Of DirectoryFacts)())
        If probes.MailHandler IsNot Nothing Then
            Dim mailProbe As Func(Of Boolean?) = Function() CType(probes.MailHandler(), Boolean?)
            facts.MailHandler = Ask(Of Boolean?)(facts, "mail handler", mailProbe, Nothing)
        End If
        facts.Reachability = Ask(Of String)(facts, "reachability", probes.Reachability, "")
        If facts.Reachability Is Nothing Then facts.Reachability = ""
        Return facts
    End Function

    Private Function Ask(Of T)(facts As HostingFacts, name As String, probe As Func(Of T), fallback As T) As T
        If probe Is Nothing Then Return fallback
        Try
            Dim answer As T = probe()
            Return answer
        Catch
            facts.ProbeFailures.Add(name)
            Return fallback
        End Try
    End Function

    ''' <summary>Facts to text. The whole block, English on purpose (the author reads it).</summary>
    Friend Function Build(probes As HostingProbes) As String
        Return Render(Gather(probes))
    End Function

    ' --- rendering ------------------------------------------------------------------------

    Friend Function Render(facts As HostingFacts) As String
        Dim sb As New StringBuilder()
        If facts Is Nothing Then
            sb.AppendLine("not available")
            Return sb.ToString()
        End If

        sb.AppendLine("OS: " & DescribeOs(facts.Os))
        sb.AppendLine("Memory and processors: " & DescribeResources(facts.Os))
        sb.AppendLine("System drive: " & DescribeDisk(facts.Os))
        sb.AppendLine("Session: " & DescribeSession(facts.Session))
        sb.AppendLine("Host mode (the Share Manager's own reading): " & If(String.IsNullOrEmpty(facts.HostMode), "not determined by this program", facts.HostMode))

        Dim anyRegistered As Boolean = facts.Services.Any(Function(s) s.Registered)
        For Each service As ServiceFacts In facts.Services
            sb.AppendLine("Service " & service.Name & ": " & DescribeService(service))
        Next
        If Not anyRegistered AndAlso facts.Services.Count > 0 AndAlso facts.Services.All(Function(s) Not s.QueryFailed) Then
            sb.AppendLine("Neither service is registered on this PC - the Share Manager hosts the worker itself, and the exchange server runs only as a console process when started by hand.")
        End If

        sb.AppendLine("Ports: " & DescribePorts(facts.Ports))

        If facts.Programs.Count = 0 Then
            sb.AppendLine("Programs: none of our program files were found beside this one")
        Else
            sb.AppendLine("Programs (file version resource; firewall rules aimed at that exact file, rule names only):")
            For Each program As ProgramFacts In facts.Programs
                sb.AppendLine("  " & program.Label & ": " & DescribeProgram(program))
            Next
        End If

        If facts.Sockets Is Nothing Then
            sb.AppendLine("Listening sockets of our processes: not available")
        ElseIf facts.Sockets.Count = 0 Then
            sb.AppendLine("Listening sockets of our processes: none")
        Else
            sb.AppendLine("Listening sockets of our processes:")
            For Each socket As SocketFacts In facts.Sockets.OrderBy(Function(s) s.Process, StringComparer.OrdinalIgnoreCase).ThenBy(Function(s) s.Port)
                sb.AppendLine("  " & socket.Process & " (pid " & socket.ProcessId.ToString(CultureInfo.InvariantCulture) & "): tcp/" &
                              socket.Port.ToString(CultureInfo.InvariantCulture) & ", " & socket.Scope)
            Next
        End If

        If facts.Components.Count = 0 Then
            sb.AppendLine("Components installed: not determined")
        Else
            sb.AppendLine("Components installed: " & String.Join("; ", facts.Components.Select(
                Function(c) c.Key & " " & If(c.Value, "yes", "no"))))
        End If

        If facts.Directories.Count > 0 Then
            sb.AppendLine("Data directories (can THIS user read them):")
            For Each dir As DirectoryFacts In facts.Directories
                sb.AppendLine("  " & dir.Label & ": " & DescribeDirectory(dir))
            Next
        End If

        sb.AppendLine("Mail client for Send the log: " & If(facts.MailHandler.HasValue,
                      If(facts.MailHandler.Value, "a handler is registered", "none found - the archive is shown in its folder and its full path is printed"),
                      "not determined"))
        If Not String.IsNullOrEmpty(facts.Reachability) Then
            sb.AppendLine("Share reachability: " & facts.Reachability)
        End If
        If facts.ProbeFailures.Count > 0 Then
            sb.AppendLine("Probes that failed (their lines above say so): " & String.Join(", ", facts.ProbeFailures))
        End If
        Return sb.ToString()
    End Function

    Friend Function DescribeOs(os As OsFacts) As String
        If Not os.Known Then Return "not available"
        ' The registry's own word (Client, Server, Server Core) when there is one - it tells a Server Core
        ' (no WinForms, so no Share Manager) from a Desktop Experience install.
        Dim kind As String = If(Not String.IsNullOrEmpty(os.InstallationType), os.InstallationType, If(os.IsServer, "Server", "client"))
        Dim text As String = OsDisplayName(os.ProductName, os.Build)
        If String.IsNullOrEmpty(text) Then text = "Windows"
        If Not String.IsNullOrEmpty(os.Version) Then text &= ", version " & os.Version
        If Not String.IsNullOrEmpty(os.Build) Then text &= ", build " & os.Build
        If Not String.IsNullOrEmpty(os.EditionId) Then text &= ", edition " & os.EditionId
        Return text & ", " & kind & ", " & If(os.Is64BitOs, "64-bit", "32-bit")
    End Function

    ''' <summary>The registry still says "Windows 10" on Windows 11 (the product name was never updated);
    ''' the build number, 22000 and up, is what tells them apart.</summary>
    Friend Function OsDisplayName(productName As String, build As String) As String
        If String.IsNullOrEmpty(productName) Then Return ""
        Dim number As Integer
        Dim head As String = If(build, "")
        Dim dot As Integer = head.IndexOf("."c)
        If dot >= 0 Then head = head.Substring(0, dot)
        If Integer.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, number) AndAlso number >= 22000 AndAlso
           productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) Then
            Return "Windows 11" & productName.Substring("Windows 10".Length)
        End If
        Return productName
    End Function

    Friend Function DescribeResources(os As OsFacts) As String
        Dim memory As String = If(os.TotalMemoryMb > 0, os.TotalMemoryMb.ToString(CultureInfo.InvariantCulture) & " MB RAM", "RAM unknown")
        Dim cpus As String = If(os.ProcessorCount > 0, os.ProcessorCount.ToString(CultureInfo.InvariantCulture) & " logical processors", "processors unknown")
        Return memory & ", " & cpus
    End Function

    Friend Function DescribeDisk(os As OsFacts) As String
        If os.SystemDriveFreeMb < 0 OrElse String.IsNullOrEmpty(os.SystemDrive) Then Return "not available"
        Dim total As String = If(os.SystemDriveTotalMb > 0, " of " & os.SystemDriveTotalMb.ToString(CultureInfo.InvariantCulture) & " MB", "")
        Return os.SystemDrive & " " & os.SystemDriveFreeMb.ToString(CultureInfo.InvariantCulture) & " MB free" & total
    End Function

    Friend Function DescribeSession(session As SessionFacts) As String
        If Not session.Known Then Return "not available"
        Return "id " & session.SessionId.ToString(CultureInfo.InvariantCulture) & ", " &
               If(session.IsRemoteDesktop, "remote desktop (RDP) session", "local console session")
    End Function

    Friend Function DescribeService(service As ServiceFacts) As String
        If service.QueryFailed Then Return "could not be queried"
        If Not service.Registered Then Return "not registered"
        Dim parts As New List(Of String) From {"registered"}
        parts.Add("state " & If(String.IsNullOrEmpty(service.State), "unknown", service.State))
        parts.Add("start type " & If(String.IsNullOrEmpty(service.StartType), "unknown", service.StartType))
        parts.Add("account " & AccountLabel(service.Account))
        If service.Win32ExitCode.HasValue Then
            Dim code As String = "last exit code " & service.Win32ExitCode.Value.ToString(CultureInfo.InvariantCulture)
            ' 1066 = ERROR_SERVICE_SPECIFIC_ERROR: the real code is the service-specific one.
            If service.Win32ExitCode.Value = 1066 AndAlso service.ServiceSpecificExitCode.HasValue Then
                code &= " (service-specific " & service.ServiceSpecificExitCode.Value.ToString(CultureInfo.InvariantCulture) & ")"
            End If
            parts.Add(code)
        Else
            parts.Add("last exit code unknown")
        End If
        Return String.Join("; ", parts)
    End Function

    ''' <summary>The account a service runs under, for the three Windows service accounts only. Anything
    ''' else is somebody's login: it is never printed (the block's "never" list).</summary>
    Friend Function AccountLabel(account As String) As String
        If String.IsNullOrWhiteSpace(account) Then Return "unknown"
        Dim trimmed As String = account.Trim()
        For Each known As String In {"LocalSystem", "NT AUTHORITY\LocalService", "NT AUTHORITY\NetworkService", "NT AUTHORITY\SYSTEM", "NT AUTHORITY\LOCAL SERVICE", "NT AUTHORITY\NETWORK SERVICE"}
            If String.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase) Then Return known
        Next
        Return "a user account (name withheld)"
    End Function

    Friend Function DescribePorts(ports As PortFacts) As String
        Dim parts As New List(Of String)()
        parts.Add("exchange listen " & PortText(ports.ExchangeListen))
        If ports.ExchangeRangeLow > 0 AndAlso ports.ExchangeRangeHigh >= ports.ExchangeRangeLow Then
            parts.Add("exchange third-party range " & ports.ExchangeRangeLow.ToString(CultureInfo.InvariantCulture) & "-" & ports.ExchangeRangeHigh.ToString(CultureInfo.InvariantCulture))
        Else
            parts.Add("exchange third-party range unknown")
        End If
        parts.Add("share listen " & PortText(ports.ShareListen))
        Return String.Join("; ", parts)
    End Function

    Private Function PortText(port As Integer) As String
        Return If(port > 0, port.ToString(CultureInfo.InvariantCulture), "unknown")
    End Function

    Friend Function DescribeProgram(program As ProgramFacts) As String
        If Not program.Present Then Return "not present"
        Dim version As String = If(String.IsNullOrEmpty(program.Version), "no version resource", "version " & program.Version)
        If Not String.IsNullOrEmpty(program.Stamp) Then version &= " (" & program.Stamp & ")"
        Dim rules As String
        If program.FirewallRules Is Nothing Then
            rules = "firewall rules not readable"
        ElseIf program.FirewallRules.Count = 0 Then
            rules = "no firewall rule names this file"
        Else
            rules = "firewall rules: " & String.Join(", ", program.FirewallRules.OrderBy(Function(r) r, StringComparer.OrdinalIgnoreCase))
        End If
        Return version & "; " & rules
    End Function

    Friend Function DescribeDirectory(dir As DirectoryFacts) As String
        If Not dir.Exists Then Return "absent"
        If dir.Readable Then Return "present, readable by this user: yes"
        Return "present, readable by this user: no" & If(String.IsNullOrEmpty(dir.Reason), "", " (" & dir.Reason & ")")
    End Function

    ' --- the start-up state line (L-8) ------------------------------------------------------

    ''' <summary>
    ''' ONE line, no secret in it: product and version, OS name and build, Server or client, session id and
    ''' the RDP flag, the host mode, the installed components and, per service, registered or not (with the
    ''' state). <paramref name="facts"/> comes from <see cref="Gather"/> with <c>cheapOnly:=True</c>.
    ''' </summary>
    Friend Function BuildStateLine(product As String, version As String, facts As HostingFacts) As String
        Dim sb As New StringBuilder()
        sb.Append("State: ").Append(If(String.IsNullOrEmpty(product), "program", product))
        sb.Append(" ").Append(If(String.IsNullOrEmpty(version), "unknown version", version))
        If facts IsNot Nothing AndAlso facts.Os.Known Then
            Dim os As OsFacts = facts.Os
            sb.Append("; os=").Append(If(String.IsNullOrEmpty(OsDisplayName(os.ProductName, os.Build)), "Windows", OsDisplayName(os.ProductName, os.Build)))
            If Not String.IsNullOrEmpty(os.Build) Then sb.Append(" build ").Append(os.Build)
            sb.Append(If(os.IsServer, " (Server)", " (client)"))
        Else
            sb.Append("; os=unknown")
        End If
        If facts IsNot Nothing AndAlso facts.Session.Known Then
            sb.Append("; session=").Append(facts.Session.SessionId.ToString(CultureInfo.InvariantCulture))
            sb.Append(" rdp=").Append(If(facts.Session.IsRemoteDesktop, "yes", "no"))
        Else
            sb.Append("; session=unknown")
        End If
        sb.Append("; hostMode=").Append(If(facts Is Nothing OrElse String.IsNullOrEmpty(facts.HostMode), "n/a", facts.HostMode))
        If facts IsNot Nothing AndAlso facts.Components.Count > 0 Then
            Dim installed As IEnumerable(Of String) = facts.Components.Where(Function(c) c.Value).Select(Function(c) c.Key)
            sb.Append("; components=").Append(If(installed.Any(), String.Join(",", installed), "none"))
        Else
            sb.Append("; components=unknown")
        End If
        If facts IsNot Nothing Then
            For Each service As ServiceFacts In facts.Services
                Dim label As String = If(String.Equals(service.Name, ShareServiceName, StringComparison.OrdinalIgnoreCase), "shareService",
                                      If(String.Equals(service.Name, ExchangeServiceName, StringComparison.OrdinalIgnoreCase), "exchangeService", service.Name))
                sb.Append("; ").Append(label).Append("=")
                If service.QueryFailed Then
                    sb.Append("unknown")
                ElseIf service.Registered Then
                    sb.Append("registered(").Append(If(String.IsNullOrEmpty(service.State), "unknown", service.State)).Append(")")
                Else
                    sb.Append("absent")
                End If
            Next
        End If
        Return sb.ToString()
    End Function

    ''' <summary>The state line for a program, from the live probes, never throwing and bounded to a
    ''' handful of registry reads and two SCM calls.</summary>
    Friend Function BuildLiveStateLine(product As String, version As String) As String
        Try
            Return BuildStateLine(product, version, Gather(HostingProbesLive.Live(), cheapOnly:=True))
        Catch ex As Exception
            Return "State: " & If(product, "program") & " " & If(version, "") & "; the state probes failed (" & ex.GetType().Name & ")"
        End Try
    End Function

End Module
#End If
