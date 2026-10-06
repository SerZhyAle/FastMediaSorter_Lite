Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' <see cref="ExchangeProcess"/> against the REAL binary (ticket 142 T-1), on a scratch data directory
''' (<c>FMS_EXCHANGE_DATA_DIR</c>) - never the owner's. A copy of the exe is made so nothing locks the build
''' output, and the data directory is thrown away afterwards.
'''
''' Which binary: the one named by the environment variable <c>FMS_EXCHANGE_TEST_EXE</c>, else
''' <c>bin\Release\exchange\fms-exchange-server.exe</c> of the working tree. With neither present the test
''' returns without asserting (a clone without Go has no binary to drive); the stop-request test also needs
''' a binary that knows the <c>stop</c> command and says so by returning early when it does not (a build
''' older than ticket 144's deployment layer).
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeBinaryIntegrationTests
    Implements IDisposable

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
            ' A build older than the set-listen command (the working tree's bin\Release copy can be one)
            ' has nothing to drive here: the unit tests above this layer still cover the mapping.
            Dim probe As ExchangeProcess.RunResult = ExchangeProcess.Run({"set-listen"}, Nothing, 5000)
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

    <Fact>
    Public Sub A_fresh_directory_inspects_as_not_configured_and_nothing_is_created()
        If Not _hasBinary Then Return
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.True(r.Answered)
        Assert.False(r.ConfigPresent)
        Assert.False(r.PasswordSet)
        Assert.False(r.Configured)
        Assert.Equal(22022, r.Port)
        Assert.False(Directory.Exists(_dataDir) AndAlso Directory.GetFileSystemEntries(_dataDir).Length > 0, "inspect must create nothing")
        Dim model As ExchangeGroupModel = ExchangeGroupModel.Build(New ExchangeSnapshot With {.Report = r})
        Assert.Equal(ExchangeRunKind.NotConfigured, model.Kind)
        Assert.False(model.ShowStart)
    End Sub

    <Fact>
    Public Sub A_sub_floor_password_is_refused_by_the_binary_and_a_good_one_is_stored_as_a_hash_only()
        If Not _hasBinary Then Return
        Assert.Equal(ExchangeProcess.PasswordOutcome.PolicyRefused, ExchangeProcess.SetPassword("Abcdefgh1").Outcome)
        Assert.Equal(ExchangeProcess.PasswordOutcome.PolicyRefused, ExchangeProcess.SetPassword("abcdefghij1").Outcome)
        Assert.False(ExchangeProcess.Inspect().PasswordSet)

        Const good As String = "Abcdefghi1"
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, ExchangeProcess.SetPassword(good).Outcome)
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.True(r.Configured)
        ' The config never holds the plaintext (133 acceptance 4).
        Dim config As String = File.ReadAllText(r.ConfigPath)
        Assert.DoesNotContain(good, config)
        Assert.Contains("$argon2id$", config)
    End Sub

    <Fact>
    Public Sub A_generated_password_conforms_is_stored_and_never_logged()
        If Not _hasBinary Then Return
        Dim r As ExchangeProcess.PasswordResult = ExchangeProcess.GeneratePassword()
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, r.Outcome)
        Assert.True(ExchangePasswordPolicy.IsAccepted(r.Plaintext))
        Assert.True(r.Plaintext.Length >= 10)
        Assert.True(ExchangeProcess.Inspect().Configured)
        Assert.DoesNotContain(r.Plaintext, File.ReadAllText(ExchangeProcess.Inspect().ConfigPath))
        Assert.DoesNotContain(r.Plaintext, ReadLogText())
    End Sub

    <Fact>
    Public Sub The_listen_port_is_written_through_the_binary_and_keeps_the_other_members()
        If Not _hasBinary Then Return
        Dim port As Integer = FreePort()
        Assert.Equal(ExchangeProcess.StartOutcome.NoPassword, ExchangeProcess.Start())
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)
        Dim before As String = File.ReadAllText(ExchangeProcess.Inspect().ConfigPath)

        Assert.Equal(ExchangeProcess.ListenOutcome.Saved, ExchangeProcess.SetListen(ExchangeProcess.WithPort(ExchangeProcess.Inspect().Listen, port)))
        Dim r As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        Assert.Equal(port, r.Port)
        Dim after As String = File.ReadAllText(r.ConfigPath)
        ' Every other member survived: the password record is byte-identical and the other sections are there.
        Dim hashPattern As String = """passwordHash"":\s*""([^""]*)"""
        Dim hashBefore As String = System.Text.RegularExpressions.Regex.Match(before, hashPattern).Groups(1).Value
        Dim hashAfter As String = System.Text.RegularExpressions.Regex.Match(after, hashPattern).Groups(1).Value
        Assert.StartsWith("$argon2id$", hashBefore)
        Assert.Equal(hashBefore, hashAfter)
        Assert.Contains("""portRange""", after)
        Assert.Contains("""limits""", after)
        Assert.Equal(ExchangeProcess.ListenOutcome.BadAddress, ExchangeProcess.SetListen("not an address"))
    End Sub

    <Fact>
    Public Sub Start_reports_a_taken_port_and_a_clean_start_then_stop_round_trip()
        If Not _hasBinary Then Return
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)

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

        ' Exit 2 is "unknown command": a build older than ticket 144's deployment layer. Asked BEFORE a server runs.
        Dim hasStop As Boolean = ExchangeProcess.Run({"stop"}, Nothing, 5000).ExitCode <> ExchangeProcess.ExitUsage
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
            Assert.True(m.ShowFingerprint)
            Assert.Equal(port, m.Port)

            ' The server's own TCP port is really open while it runs.
            Using c As New TcpClient()
                c.Connect(IPAddress.Loopback, port)
                Assert.True(c.Connected)
            End Using

            Dim stopped As ExchangeProcess.StopOutcome = ExchangeProcess.StopServer()
            Assert.True(stopped = ExchangeProcess.StopOutcome.Stopped OrElse stopped = ExchangeProcess.StopOutcome.Killed, "stop outcome " & stopped.ToString())
            ' With the stop command the server goes by the request, never by the kill fallback.
            If hasStop Then Assert.Equal(ExchangeProcess.StopOutcome.Stopped, stopped)
            Assert.Equal(0, ExchangeProcess.RunningPid())
            ' A binary that has the stop command is stopped cleanly and logs its own stop line; an older one
            ' (no stop command) is killed after the request - both end with nothing running.
            If stopped = ExchangeProcess.StopOutcome.Stopped Then
                Dim log As String = File.ReadAllText(Path.Combine(_dataDir, "exchange.log"))
                Assert.Contains("stop", log)
            End If
        Finally
            ExchangeProcess.StopServer()
        End Try
    End Sub

    ''' <summary>The window against the real binary: shown off-screen, it reads the state off the UI thread by
    ''' itself (the folded summary is live), and the state line follows what the binary holds.</summary>
    <Fact>
    Public Sub The_shown_window_reads_the_real_state_by_itself_and_fills_the_folded_summary()
        If Not _hasBinary Then Return
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)
        Dim summary As String = ""
        Dim kind As ExchangeRunKind = ExchangeRunKind.Unreadable
        ExchangeTestSupport.OnUi(
            Sub()
                Using form As New Share_Settings_Form()
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual
                    form.Location = New System.Drawing.Point(-5000, -5000)
                    form.ShowInTaskbar = False
                    form.Show()
                    Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
                    Dim section As CollapsibleSection = DirectCast(GetType(Share_Settings_Form).GetField("_secExchange",
                        System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic).GetValue(form), CollapsibleSection)
                    Dim modelField As System.Reflection.FieldInfo = GetType(Share_Settings_Form).GetField("_exModel",
                        System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
                    While DateTime.UtcNow < deadline
                        System.Windows.Forms.Application.DoEvents()
                        Dim model As ExchangeGroupModel = DirectCast(modelField.GetValue(form), ExchangeGroupModel)
                        If model IsNot Nothing AndAlso model.Kind <> ExchangeRunKind.Unreadable Then Exit While
                        System.Threading.Thread.Sleep(30)
                    End While
                    kind = DirectCast(modelField.GetValue(form), ExchangeGroupModel).Kind
                    summary = section.Summary
                    ' Disposed, not closed: FormClosing would write this off-screen test window's geometry into the developer's registry.
                    form.Hide()
                End Using
            End Sub)
        Assert.Equal(ExchangeRunKind.Stopped, kind)
        Assert.Contains("22022", summary)
    End Sub

    <Fact>
    Public Sub Exit_stops_the_server_this_session_started_unless_it_is_to_keep_running()
        If Not _hasBinary Then Return
        Assert.Equal(ExchangeProcess.PasswordOutcome.Saved, ExchangeProcess.SetPassword("Abcdefghi1").Outcome)
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

End Class
