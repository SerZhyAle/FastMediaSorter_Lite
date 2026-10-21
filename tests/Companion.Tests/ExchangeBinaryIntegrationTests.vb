Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The Share Manager's exchange server administration against the REAL binary (tickets 142 T-1 and 168), on a scratch data
''' directory (<c>FMS_EXCHANGE_DATA_DIR</c>) - never the owner's. A copy of the exe is made so nothing locks the build
''' output, and the data directory is thrown away afterwards.
'''
''' Which binary: the one named by the environment variable <c>FMS_EXCHANGE_TEST_EXE</c>, else
''' <c>bin\Release\exchange\fms-exchange-server.exe</c> of the working tree. With neither present, or with one that predates
''' the accounts model or the 168 verbs (<c>set-port-range</c>, <c>tls</c>), the tests return without asserting (a clone
''' without Go has no binary to drive; the unit tests above this layer still cover every mapping). The suite is meant to
''' run with a fresh build: <c>go build -o %TEMP%\fms-it\fms-exchange-server.exe .\cmd\fms-exchange-server</c> in
''' <c>exchange-server</c>, then the variable pointed at it.
'''
''' What only a real run can prove: the admin pipe's name the Share Manager derives is the one the server listens on,
''' an account the pipe adds is the one the offline path lists, a password is nowhere on disk afterwards, and the wizard
''' of 168 section 3.7 ends with a running server, an account and an agent that really enrolled over TLS.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeBinaryIntegrationTests
    Implements IDisposable

    Private Const Good As String = "Abcdefghi1"

    Private ReadOnly _exeDir As String
    Private ReadOnly _dataDir As String
    Private ReadOnly _hasBinary As Boolean

    Public Sub New()
        ExchangeTestSupport.Reset()
        Dim source As String = Environment.GetEnvironmentVariable("FMS_EXCHANGE_TEST_EXE")
        If String.IsNullOrEmpty(source) OrElse Not File.Exists(source) Then
            source = Path.Combine(ExchangeTestSupport.RepoRoot(), "bin", "Release", "exchange", "fms-exchange-server.exe")
        End If
        _exeDir = Path.Combine(Path.GetTempPath(), "fms-exchange-it-" & Guid.NewGuid().ToString("N"))
        _dataDir = Path.Combine(_exeDir, "data")
        If File.Exists(source) Then
            Directory.CreateDirectory(_exeDir)
            File.Copy(source, Path.Combine(_exeDir, ExchangeProcess.ExeName))
            ExchangeProcess.ExeDirectoryOverride = _exeDir
            ExchangeProcess.DataDirOverride = _dataDir
            ' A build older than the accounts model and the 168 verbs has nothing to drive here.
            Dim probe As ExchangeProcess.RunResult = ExchangeProcess.Run({"set-port-range"}, Nothing, 5000)
            _hasBinary = probe.Started AndAlso Not If(probe.StdErr, "").Contains("unknown command")
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            ExchangeProcess.StopServer()
        Catch
        End Try
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_exeDir)
    End Sub

    Private Shared Function FreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Try
            Return CType(l.LocalEndpoint, IPEndPoint).Port
        Finally
            l.Stop()
        End Try
    End Function

    Private Shared Function ReadLogText() As String
        Dim logPath As String = AppFileLogger.LogFilePath
        If String.IsNullOrEmpty(logPath) OrElse Not File.Exists(logPath) Then Return ""
        Using fs As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using reader As New StreamReader(fs)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    ''' <summary>One account through the offline path (no server holds the directory), as the Share Manager does before the first start.</summary>
    Private Shared Function AddOffline(login As String, Optional password As String = Good) As ExchangeAdminReply
        Return ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, login, password))
    End Function

    ''' <summary>Every file under the data directory, read as text, for the "no password on disk" scans.</summary>
    Private Function DataDirText() As String
        Dim sb As New StringBuilder()
        If Not Directory.Exists(_dataDir) Then Return ""
        For Each f As String In Directory.GetFiles(_dataDir, "*", SearchOption.AllDirectories)
            Try
                Using fs As New FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                    Using reader As New StreamReader(fs)
                        sb.AppendLine(reader.ReadToEnd())
                    End Using
                End Using
            Catch ex As IOException
                ' the lock file of a running server
            End Try
        Next
        Return sb.ToString()
    End Function

    <Fact>
    Public Sub A_fresh_directory_inspects_as_not_configured_and_nothing_is_created()
        If Not _hasBinary Then Return
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.True(r.Answered)
        Assert.False(r.ConfigPresent)
        Assert.Equal(0, r.Accounts)
        Assert.False(r.Configured)
        Assert.Equal(22022, r.Port)
        Assert.Equal("", r.PublicEndpoint)
        Assert.False(Directory.Exists(_dataDir) AndAlso Directory.GetFileSystemEntries(_dataDir).Length > 0, "inspect must create nothing")
        Dim model As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = r})
        Assert.Equal(ExchangeRunKind.NotConfigured, model.Kind)
        Assert.False(model.ShowStart)
        Assert.True(model.ShowWizard)
    End Sub

    <Fact>
    Public Sub Accounts_go_through_the_offline_path_and_no_password_is_ever_on_disk_or_in_the_log()
        If Not _hasBinary Then Return
        Dim weak As ExchangeAdminReply = AddOffline("alice", "Abcdefgh1")
        Assert.False(weak.Ok)
        Assert.Equal(ExchangeAdminCodes.WeakPassword, weak.Code)
        Assert.False(AddOffline("A", Good).Ok)

        Dim added As ExchangeAdminReply = AddOffline("Alice")
        Assert.True(added.Ok, added.Code)
        Assert.Equal(ExchangeAdminRoute.Offline, added.Route)
        Assert.Equal(ExchangeAdminCodes.Exists, AddOffline("alice").Code)

        Dim list As ExchangeAdminReply = ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList))
        Assert.True(list.Ok)
        Dim accounts As List(Of ExchangeAdminAccount) = ExchangeAdminParser.Accounts(list.DataJson)
        Assert.Equal({"alice"}, accounts.Select(Function(a) a.Login).ToArray())
        Assert.Equal(0, accounts(0).Devices)
        Assert.True(ExchangeProcess.Inspect().Configured)

        Dim devices As ExchangeAdminReply = ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceList, "alice"))
        Assert.True(devices.Ok)
        Assert.Empty(ExchangeAdminParser.Devices(devices.DataJson))
        Assert.Equal(ExchangeAdminCodes.NoAccount, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceList, "nobody")).Code)

        ' a reset keeps the account; a typed password that meets the floor is taken, one that does not is refused with the code
        Assert.True(ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "alice", "Zyxwvutsr9", signOut:=True)).Ok)
        Assert.Equal(ExchangeAdminCodes.WeakPassword, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "alice", "short")).Code)
        Assert.Equal(ExchangeAdminCodes.NoAccount, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "nobody", Good)).Code)

        ' a login that begins with a hyphen is still one login, never a command-line option
        Assert.True(AddOffline("-edge-login").Ok)
        Assert.Contains("-edge-login", ExchangeAdminParser.Accounts(ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList)).DataJson).Select(Function(a) a.Login))
        Assert.True(ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountRemove, "-edge-login")).Ok)

        ' no password anywhere on disk, none in companion.log
        Dim onDisk As String = DataDirText()
        For Each secret As String In {Good, "Zyxwvutsr9"}
            Assert.DoesNotContain(secret, onDisk)
            Assert.DoesNotContain(secret, ReadLogText())
        Next
        Assert.Contains("$argon2id$", onDisk)

        Assert.True(ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountRemove, "alice")).Ok)
        Assert.Equal(ExchangeAdminCodes.NoAccount, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountRemove, "alice")).Code)
        Assert.False(ExchangeProcess.Inspect().Configured)
    End Sub

    <Fact>
    Public Sub The_address_and_the_range_are_written_through_the_binary_and_keep_the_other_members()
        If Not _hasBinary Then Return
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.StartOutcome.NoAccount, ExchangeProcess.Start())
        Assert.True(AddOffline("alice").Ok)
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen(ExchangeProcess.WithPort(ExchangeProcess.Inspect().Listen, port)))
        Dim before As String = File.ReadAllText(ExchangeProcess.Inspect().ConfigPath)

        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetEndpoint("203.0.113.7:" & port.ToString()))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetPortRange("41000-41100"))
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.Equal("203.0.113.7:" & port.ToString(), r.PublicEndpoint)
        Assert.Equal("41000-41100", r.PortRange)
        Assert.Equal(port, r.Port)
        Dim after As String = File.ReadAllText(r.ConfigPath)
        Assert.Contains("""limits""", after)
        Assert.Contains("""keepalive""", after)
        Assert.NotEqual(before, after)

        ' refused values leave the file as it is
        Assert.Equal(ExchangeProcess.ConfigOutcome.Refused, ExchangeProcess.SetEndpoint("127.0.0.1:22022"))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Refused, ExchangeProcess.SetEndpoint("not an address"))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Refused, ExchangeProcess.SetPortRange("41100-41000"))
        Assert.Equal(ExchangeProcess.ConfigOutcome.Refused, ExchangeProcess.SetPortRange(port.ToString() & "-" & (port + 50).ToString()))
        Assert.Equal(after, File.ReadAllText(r.ConfigPath))

        ' an empty value clears the address
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetEndpoint(""))
        Assert.Equal("", ExchangeProcess.Inspect().PublicEndpoint)
    End Sub

    ''' <summary>A self-signed pair in PEM, valid in [from, to], written to the exchange scratch folder: the files a CA-signed
    ''' deployment would import.</summary>
    Private Function WritePair(name As String, notBefore As DateTimeOffset, notAfter As DateTimeOffset, ByRef fingerprint As String) As Tuple(Of String, String)
        Using key As ECDsa = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim request As New CertificateRequest("CN=" & name, key, HashAlgorithmName.SHA256)
            Using cert As X509Certificate2 = request.CreateSelfSigned(notBefore, notAfter)
                fingerprint = "SHA256:" & Convert.ToBase64String(SHA256.HashData(cert.RawData)).TrimEnd("="c)
                Dim certPath As String = Path.Combine(_exeDir, name & ".crt")
                Dim keyPath As String = Path.Combine(_exeDir, name & ".key")
                File.WriteAllText(certPath, cert.ExportCertificatePem())
                File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem())
                Return Tuple.Create(certPath, keyPath)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub The_certificate_is_replaced_and_imported_through_the_binary_and_a_bad_pair_changes_nothing()
        If Not _hasBinary Then Return
        Dim init As ExchangeProcess.RunResult = ExchangeProcess.Run({"inspect", "--init"}, Nothing, 20000)
        Assert.Equal(0, init.ExitCode)
        Dim first As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.True(first.CertPresent)
        Assert.False(first.CertImported)
        Assert.StartsWith("SHA256:", first.Fingerprint)

        Dim replaced As ExchangeProcess.CertificateResult = ExchangeProcess.ReplaceCertificate()
        Assert.Equal(ExchangeProcess.CertificateOutcome.Done, replaced.Outcome)
        Assert.NotEqual(first.Fingerprint, replaced.Fingerprint)
        Assert.Equal(replaced.Fingerprint, ExchangeProcess.Inspect().Fingerprint)

        Dim fingerprint As String = ""
        Dim good As Tuple(Of String, String) = WritePair("imported", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), fingerprint)
        Dim ignored As String = ""
        Dim other As Tuple(Of String, String) = WritePair("other", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), ignored)
        Dim old As Tuple(Of String, String) = WritePair("old", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30), ignored)

        ' a key of another certificate, an expired pair and a file that is not PEM are refused BY NAME and change nothing
        Assert.Equal(ExchangeProcess.CertificateOutcome.Mismatch, ExchangeProcess.ImportCertificate(good.Item1, other.Item2).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Expired, ExchangeProcess.ImportCertificate(old.Item1, old.Item2).Outcome)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Unreadable, ExchangeProcess.ImportCertificate(Path.Combine(_exeDir, "missing.crt"), good.Item2).Outcome)
        Dim unchanged As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.False(unchanged.CertImported)
        Assert.Equal(replaced.Fingerprint, unchanged.Fingerprint)

        Dim imported As ExchangeProcess.CertificateResult = ExchangeProcess.ImportCertificate(good.Item1, good.Item2)
        Assert.Equal(ExchangeProcess.CertificateOutcome.Done, imported.Outcome)
        Assert.Equal(fingerprint, imported.Fingerprint)
        Dim after As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.True(after.CertImported)
        Assert.Equal(fingerprint, after.Fingerprint)
        ' the private key was copied by the binary into its own folder, never shown: it is in no log and no output
        Assert.DoesNotContain("PRIVATE KEY", ReadLogText())

        ' and back to a fresh self-signed identity
        Dim again As ExchangeProcess.CertificateResult = ExchangeProcess.ReplaceCertificate()
        Assert.Equal(ExchangeProcess.CertificateOutcome.Done, again.Outcome)
        Assert.False(ExchangeProcess.Inspect().CertImported)
        Assert.NotEqual(fingerprint, again.Fingerprint)
    End Sub

    <Fact>
    Public Sub Start_reports_a_taken_port_and_a_clean_start_then_stop_round_trip()
        If Not _hasBinary Then Return
        Assert.True(AddOffline("alice").Ok)

        ' A port somebody else holds: the server exits at once, and that is reported as a failure.
        Dim blocker As New TcpListener(IPAddress.Loopback, 0)
        blocker.Start()
        Try
            Dim busy As Integer = CType(blocker.LocalEndpoint, IPEndPoint).Port
            Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & busy.ToString()))
            Assert.Equal(ExchangeProcess.StartOutcome.Failed, ExchangeProcess.Start())
        Finally
            blocker.Stop()
        End Try

        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
        Try
            Assert.True(ExchangeProcess.RunningPid() > 0)
            Assert.Equal(ExchangeProcess.StartOutcome.AlreadyRunning, ExchangeProcess.Start())

            Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
            Assert.True(r.CertPresent)
            Assert.StartsWith("SHA256:", r.Fingerprint)
            Dim m As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = r, .ConsolePid = ExchangeProcess.RunningPid()})
            Assert.Equal(ExchangeRunKind.RunningConsole, m.Kind)
            Assert.True(m.ShowStop)
            Assert.False(m.ShowStart)
            Assert.False(m.ShowWizard, "the wizard is for a PC where nothing exists yet")
            Assert.True(m.ShowFingerprint)
            Assert.Equal(port, m.Port)

            ' The server's own TCP port is really open while it runs.
            Using c As New TcpClient()
                c.Connect(IPAddress.Loopback, port)
                Assert.True(c.Connected)
            End Using

            Dim stopped As ExchangeProcess.StopOutcome = ExchangeProcess.StopServer()
            Assert.Equal(ExchangeProcess.StopOutcome.Stopped, stopped)
            Assert.Equal(0, ExchangeProcess.RunningPid())
        Finally
            ExchangeProcess.StopServer()
        End Try
    End Sub

    ''' <summary>The admin channel against a RUNNING server: the pipe name the Share Manager derives from the data directory is the one
    ''' the server listens on, and what the pipe changes is what the offline path reads once the server is gone.</summary>
    <Fact>
    Public Sub A_running_server_is_administered_through_its_real_pipe()
        If Not _hasBinary Then Return
        Assert.True(AddOffline("alice").Ok)
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
        Try
            Dim added As ExchangeAdminReply = ExchangeAdmin.AddAccountAsync("bob", Good, CancellationToken.None).GetAwaiter().GetResult()
            Assert.True(added.Ok, added.Code)
            Assert.Equal(ExchangeAdminRoute.Pipe, added.Route)

            Assert.Equal(ExchangeAdminCodes.Exists, ExchangeAdmin.AddAccountAsync("BOB", Good, CancellationToken.None).GetAwaiter().GetResult().Code)
            Assert.Equal(ExchangeAdminCodes.WeakPassword, ExchangeAdmin.AddAccountAsync("carol", "weak", CancellationToken.None).GetAwaiter().GetResult().Code)

            Dim view As ExchangeAccountsView = ExchangeAccountsView.ReadAsync("bob", CancellationToken.None).GetAwaiter().GetResult()
            Assert.True(view.Reached)
            Assert.Equal(ExchangeAdminRoute.Pipe, view.Route)
            Assert.Equal({"alice", "bob"}, view.Accounts.Select(Function(a) a.Login).OrderBy(Function(l) l, StringComparer.Ordinal).ToArray())
            Assert.Equal("bob", view.DevicesOf)
            Assert.Empty(view.Devices)
            Dim model As ExchangeAccountsModel = ExchangeAccountsModel.Build(view, "bob", "")
            Assert.False(model.ShowOfflineNote)
            Assert.True(model.ShowReset)
            Assert.False(model.ShowDevices, "no device is enrolled, so there is no devices list")

            Dim status As ExchangeAdminStatus = ExchangeAdminParser.Status(ExchangeAdmin.StatusAsync(CancellationToken.None).GetAwaiter().GetResult().DataJson)
            Assert.Equal(2, status.Accounts)
            Assert.Equal(0, status.Online)

            Assert.True(ExchangeAdmin.ResetPasswordAsync("bob", "Zyxwvutsr9", True, CancellationToken.None).GetAwaiter().GetResult().Ok)
            Assert.Equal(ExchangeAdminCodes.NotFound, ExchangeAdmin.RevokeDeviceAsync("bob", "AAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None).GetAwaiter().GetResult().Code)
            Assert.True(ExchangeAdmin.RemoveAccountAsync("bob", CancellationToken.None).GetAwaiter().GetResult().Ok)
        Finally
            ExchangeProcess.StopServer()
        End Try
        ' the server is gone: the same call now goes through the binary's offline path, and sees what the pipe did
        Dim offline As ExchangeAccountsView = ExchangeAccountsView.ReadAsync("", CancellationToken.None).GetAwaiter().GetResult()
        Assert.Equal(ExchangeAdminRoute.Offline, offline.Route)
        Assert.Equal({"alice"}, offline.Accounts.Select(Function(a) a.Login).ToArray())
        Assert.True(ExchangeAccountsModel.Build(offline, "", "").ShowOfflineNote)
        Assert.DoesNotContain(Good, DataDirText())
        Assert.DoesNotContain("Zyxwvutsr9", DataDirText())
    End Sub

    ''' <summary>The ports of the wizard that touch the real binary and the real admin channel, with the agent side supplied by a test rig.</summary>
    Private NotInheritable Class RealBinaryPorts
        Implements IExchangeWizardPorts

        Public ReadOnly Agent As ExchangeDeviceAgent
        Public ReadOnly Offered As Boolean
        Public GeneratedPassword As String = ""
        Public Enrolled As Integer

        Public Sub New(agent As ExchangeDeviceAgent, offered As Boolean)
            Me.Agent = agent
            Me.Offered = offered
        End Sub

        Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeWizardPorts.Inspect
            Return ExchangeProcess.Inspect()
        End Function

        Public Function ServiceExists() As Boolean Implements IExchangeWizardPorts.ServiceExists
            Return False
        End Function

        Public Function IsConsoleRunning() As Boolean Implements IExchangeWizardPorts.IsConsoleRunning
            Return ExchangeProcess.RunningPid() > 0
        End Function

        Public Function GeneratePassword() As String Implements IExchangeWizardPorts.GeneratePassword
            GeneratedPassword = ExchangePasswordPolicy.Generate()
            Return GeneratedPassword
        End Function

        Public Function AddAccountAsync(login As String, password As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeWizardPorts.AddAccountAsync
            Return ExchangeAdmin.AddAccountAsync(login, password, ct)
        End Function

        Public Function Start() As ExchangeProcess.StartOutcome Implements IExchangeWizardPorts.Start
            Return ExchangeProcess.Start()
        End Function

        Public Function AgentOffered() As Boolean Implements IExchangeWizardPorts.AgentOffered
            Return Offered
        End Function

        Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeWizardPorts.ProbeAsync
            ' the production agent's transport is the real TLS one, whose probe shows the certificate and sends no byte of envelope; the rig's
            ' transport counts attempts and pins, so the probe goes straight to the real one
            Dim address As ExchangeServerAddress = Nothing
            If Not ExchangeServerAddress.TryParse(server, address) Then Throw New ArgumentException("server")
            Return ExchangeTlsTransport.ProbeAsync(address.Host, address.Port, ct)
        End Function

        Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeWizardPorts.EnrollAsync
            Enrolled += 1
            Return Agent.EnrollAsync(request, ct)
        End Function

        Public Function MachineName() As String Implements IExchangeWizardPorts.MachineName
            Return "TESTPC"
        End Function

        Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeWizardPorts.DelayAsync
            Return Task.Delay(milliseconds, ct)
        End Function
    End Class

    ''' <summary>168 acceptance 1, against the real binary: a person with no prior setup runs the wizard and ends with a running server, one
    ''' account, a PC whose agent really enrolled over TLS (pinned to the fingerprint the binary reports), and the facts - and the password
    ''' exists nowhere on disk afterwards. The agent's token is stored only through the rig's cipher.</summary>
    <Fact>
    Public Sub The_wizard_ends_with_a_running_server_one_account_and_an_enrolled_agent_and_no_password_on_disk()
        If Not _hasBinary Then Return
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ConfigOutcome.Saved, ExchangeProcess.SetEndpoint("203.0.113.7:" & port.ToString()))
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Using rig As New ExchangeDeviceAgentTests.Rig()
            Dim agent As New ExchangeDeviceAgent(rig.Ports)
            Dim ports As New RealBinaryPorts(agent, offered:=True)
            Dim steps As New List(Of ExchangeWizardStep)()
            Dim result As ExchangeWizardResult = Nothing
            Try
                result = ExchangeServerWizard.RunAsync("Me.Owner", ports, Sub(s) steps.Add(s), CancellationToken.None).GetAwaiter().GetResult()
                Assert.Equal(ExchangeWizardOutcome.Done, result.Outcome)
                Assert.Equal({ExchangeWizardStep.CreatingAccount, ExchangeWizardStep.StartingServer, ExchangeWizardStep.CheckingCertificate, ExchangeWizardStep.EnrollingThisPc}, steps.ToArray())
                Assert.Equal("me.owner", result.Login)
                Assert.True(result.AccountCreated)
                Assert.True(ExchangePasswordPolicy.IsAccepted(result.Password))
                Assert.Equal("127.0.0.1:" & port.ToString(), result.LocalAddress)
                Assert.Equal("203.0.113.7:" & port.ToString(), result.PublicEndpoint)
                Assert.StartsWith("SHA256:", result.Fingerprint)
                Assert.Equal(result.Fingerprint, ExchangeProcess.Inspect().Fingerprint)
                Assert.True(ExchangeProcess.RunningPid() > 0)

                ' the agent really enrolled: the account lists its device, and the agent knows who it is
                Dim status As ExchangeDeviceStatus = agent.GetStatus()
                Assert.True(status.Configured)
                Assert.Equal("me.owner", status.Login)
                Assert.Equal(result.Fingerprint, status.Fingerprint)
                Dim view As ExchangeAccountsView = ExchangeAccountsView.ReadAsync("me.owner", CancellationToken.None).GetAwaiter().GetResult()
                Assert.Equal(1, view.Accounts.Single(Function(a) a.Login = "me.owner").Devices)
                Assert.Single(view.Devices)
                Assert.Equal("fms-windows", view.Devices(0).Product)
                Assert.Equal("TESTPC", view.Devices(0).Name)

                ' the password exists nowhere: not in the data directory, not in the log, not in the agent's token folder
                Dim password As String = ports.GeneratedPassword
                Assert.DoesNotContain(password, DataDirText())
                Assert.DoesNotContain(password, ReadLogText())
                For Each f As String In If(Directory.Exists(rig.Folder), Directory.GetFiles(rig.Folder, "*", SearchOption.AllDirectories), Array.Empty(Of String)())
                    Assert.DoesNotContain(password, File.ReadAllText(f))
                Next
                For Each v As String In rig.Port.Values.Values
                    Assert.DoesNotContain(password, v)
                Next

                ' a second run with the same login finds the account and says so; nothing else is done
                Dim again As ExchangeWizardResult = ExchangeServerWizard.RunAsync("me.owner", ports, Nothing, CancellationToken.None).GetAwaiter().GetResult()
                Assert.Equal(ExchangeWizardOutcome.AccountExists, again.Outcome)
                Assert.False(again.AccountCreated)
                Assert.Equal(1, ports.Enrolled)

                ' 168 acceptance 2 on the real server: the operator revokes that device through the admin pipe and the agent, within seconds, is told so
                ' and does not retry (SignInAgain); the account no longer lists it
                Dim revoked As ExchangeAdminReply = ExchangeAdmin.RevokeDeviceAsync("me.owner", view.Devices(0).DeviceId, CancellationToken.None).GetAwaiter().GetResult()
                Assert.True(revoked.Ok, revoked.Code)
                Assert.Equal(ExchangeAdminRoute.Pipe, revoked.Route)
                Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
                While DateTime.UtcNow < deadline AndAlso agent.GetStatus().State <> ExchangeAgentState.SignInAgain
                    Thread.Sleep(50)
                End While
                Assert.Equal(ExchangeAgentState.SignInAgain, agent.GetStatus().State)
                Assert.Empty(ExchangeAccountsView.ReadAsync("me.owner", CancellationToken.None).GetAwaiter().GetResult().Devices)
            Finally
                Try : agent.StopAsync().GetAwaiter().GetResult() : Catch : End Try
                agent.Dispose()
                ExchangeProcess.StopServer()
                If result IsNot Nothing Then result.Wipe()
            End Try
        End Using
    End Sub

    ''' <summary>Without the client features (the gate is shut in a shipped build) the wizard still makes the server and the account and says
    ''' what it did not do.</summary>
    <Fact>
    Public Sub Without_the_client_features_the_wizard_ends_with_a_server_and_an_account_and_says_the_pc_is_not_connected()
        If Not _hasBinary Then Return
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Dim ports As New RealBinaryPorts(Nothing, offered:=False)
        Dim result As ExchangeWizardResult = Nothing
        Try
            result = ExchangeServerWizard.RunAsync("owner", ports, Nothing, CancellationToken.None).GetAwaiter().GetResult()
            Assert.Equal(ExchangeWizardOutcome.DoneNoAgent, result.Outcome)
            Assert.True(result.AccountCreated)
            Assert.Equal(0, ports.Enrolled)
            Assert.True(ExchangeProcess.RunningPid() > 0)
            Assert.Equal(1, ExchangeProcess.Inspect().Accounts)
            Assert.False(String.IsNullOrWhiteSpace(ExchangeAdminText.WizardOutcome(result)))
        Finally
            ExchangeProcess.StopServer()
            If result IsNot Nothing Then result.Wipe()
        End Try
    End Sub

    ''' <summary>The window against the real binary: shown off-screen, it reads the state off the UI thread by
    ''' itself (the folded summary is live), and the state line follows what the binary holds.</summary>
    <Fact>
    Public Sub Selecting_the_exchange_page_reads_the_real_state_and_fills_the_summary()
        If Not _hasBinary Then Return
        Assert.True(AddOffline("alice").Ok)
        Dim summary As String = ""
        Dim kind As ExchangeRunKind = ExchangeRunKind.Unreadable
        Dim accountsShown As Integer = -1
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual
                    form.Location = New System.Drawing.Point(-5000, -5000)
                    form.ShowInTaskbar = False
                    form.Show()
                    GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic).Invoke(form, New Object() {"exchange"})
                    Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(20)
                    Dim section As CollapsibleSection = DirectCast(GetType(Share_Settings_Form).GetField("_secExchange",
                        System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic).GetValue(form), CollapsibleSection)
                    Dim modelField As System.Reflection.FieldInfo = GetType(Share_Settings_Form).GetField("_exModel",
                        System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
                    Dim listField As System.Reflection.FieldInfo = GetType(Share_Settings_Form).GetField("_lvXAccounts",
                        System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
                    While DateTime.UtcNow < deadline
                        System.Windows.Forms.Application.DoEvents()
                        Dim model As ExchangeGroupModel = DirectCast(modelField.GetValue(form), ExchangeGroupModel)
                        Dim list As System.Windows.Forms.ListView = DirectCast(listField.GetValue(form), System.Windows.Forms.ListView)
                        If model IsNot Nothing AndAlso model.Kind <> ExchangeRunKind.Unreadable AndAlso list.Items.Count > 0 Then Exit While
                        System.Threading.Thread.Sleep(30)
                    End While
                    kind = DirectCast(modelField.GetValue(form), ExchangeGroupModel).Kind
                    summary = section.Summary
                    accountsShown = DirectCast(listField.GetValue(form), System.Windows.Forms.ListView).Items.Count
                    ' Disposed, not closed: FormClosing would write this off-screen test window's geometry into the developer's registry.
                    form.Hide()
                End Using
            End Sub)
        Assert.Equal(ExchangeRunKind.Stopped, kind)
        Assert.Contains("22022", summary)
        Assert.Equal(1, accountsShown)
    End Sub

    <Fact>
    Public Sub Exit_stops_the_server_this_session_started_unless_it_is_to_keep_running()
        If Not _hasBinary Then Return
        Assert.True(AddOffline("alice").Ok)
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
        Try
            ' Keep-running on: nothing happens, the server survives the Share Manager's exit.
            Assert.Equal(ExchangeProcess.StopOutcome.NotRunning, ExchangeProcess.StopStartedOnExit(keepRunning:=True))
            Assert.True(ExchangeProcess.RunningPid() > 0)
            ' Off (the default): it is stopped.
            Dim o As ExchangeProcess.StopOutcome = ExchangeProcess.StopStartedOnExit(keepRunning:=False)
            Assert.True(o = ExchangeProcess.StopOutcome.Stopped OrElse o = ExchangeProcess.StopOutcome.Killed, o.ToString())
            Assert.Equal(0, ExchangeProcess.RunningPid())
        Finally
            ExchangeProcess.StopServer()
        End Try
    End Sub

    <Fact>
    Public Async Function Same_key_renewal_is_accepted_by_the_real_client_and_a_replacement_is_refused() As Task
        If Not _hasBinary Then Return
        Assert.True(AddOffline("alice").Ok)
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen("127.0.0.1:" & port.ToString()))
        Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
        Dim pin As String = ExchangeProcess.Inspect().Fingerprint
        Dim keyPin As String = ""
        Dim transport As New ExchangeRenewalTransport(ExchangeTlsTransport.Instance, pin, "",
            Sub(leaf, key)
                pin = leaf
                keyPin = key
            End Sub)
        Dim keyPath As String = Path.Combine(_dataDir, "tls", "key.pem")
        Try
            Using cts As CancellationTokenSource = ExchangeWireTestKit.Cts()
                Using first As ExchangeTlsConnection = Await transport.ConnectAsync("127.0.0.1", port, pin, cts.Token)
                    Assert.NotEmpty(keyPin)
                End Using
                Dim originalKey As String = keyPin
                Dim originalLeaf As String = pin
                Dim keyBytes As Byte() = File.ReadAllBytes(keyPath)
                Assert.Equal(ExchangeProcess.StopOutcome.Stopped, ExchangeProcess.StopServer())
                Assert.Equal(ExchangeProcess.CertificateOutcome.Done, ExchangeProcess.RenewCertificate().Outcome)
                Assert.Equal(keyBytes, File.ReadAllBytes(keyPath))
                Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
                Using renewed As ExchangeTlsConnection = Await transport.ConnectAsync("127.0.0.1", port, pin, cts.Token)
                    Assert.Equal(ExchangeTrustVerdict.PinMatch, renewed.Verdict)
                    Assert.NotEqual(originalLeaf, pin)
                    Assert.Equal(originalKey, keyPin)
                End Using
                Assert.Equal(ExchangeProcess.StopOutcome.Stopped, ExchangeProcess.StopServer())
                Assert.Equal(ExchangeProcess.CertificateOutcome.Done, ExchangeProcess.ReplaceCertificate().Outcome)
                Assert.Equal(ExchangeProcess.StartOutcome.Started, ExchangeProcess.Start())
                Dim ex As ExchangeConnectException = Await Assert.ThrowsAsync(Of ExchangeConnectException)(
                    Function() transport.ConnectAsync("127.0.0.1", port, pin, cts.Token))
                Assert.Equal(ExchangeConnectFailure.PinMismatch, ex.Failure)
            End Using
        Finally
            ExchangeProcess.StopServer()
        End Try
    End Function
End Class
