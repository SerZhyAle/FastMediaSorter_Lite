Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Pipes
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The client of the exchange server's admin channel (ticket 168 T-1, 162 section 4) against a STUB pipe: the pipe's name
''' (the vector the Go side pins), the shape of every request, the parsing of every answer, the offline path through the
''' binary's own verbs, and - the part a security reviewer reads first - that a password travels on the pipe or stdin and
''' nowhere else. The real binary and the real pipe are driven by <see cref="ExchangeBinaryIntegrationTests"/>.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeAdminTests
    Implements IDisposable

    Private ReadOnly _dir As String
    Private ReadOnly _stubs As New List(Of IDisposable)()

    Public Sub New()
        ExchangeTestSupport.Reset()
        _dir = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        ExchangeProcess.ExeDirectoryOverride = _dir
        ' No service registered unless a test says so: the developer's registry is never read.
        ExchangeService.ImagePathOverride = Function() ""
    End Sub

    ' --- the service's store (the test host, 2026-10-12) ----------------------------------------

    <Fact>
    Public Sub The_config_path_is_read_from_a_service_command_line_quoted_or_not()
        Assert.Equal("C:\ProgramData\FastMediaSorterExchange\exchange.json",
                     ExchangeAdmin.ConfigPathOf("""C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"" serve --service --config ""C:\ProgramData\FastMediaSorterExchange\exchange.json"""))
        Assert.Equal("D:\x\exchange.json", ExchangeAdmin.ConfigPathOf("D:\bin\fms-exchange-server.exe serve --service --config D:\x\exchange.json"))
        Assert.Equal("D:\x\exchange.json", ExchangeAdmin.ConfigPathOf("fms-exchange-server.exe serve --service --config=D:\x\exchange.json"))
        Assert.Equal("", ExchangeAdmin.ConfigPathOf("fms-exchange-server.exe serve --service"))
        Assert.Equal("", ExchangeAdmin.ConfigPathOf("fms-exchange-server.exe serve --config ""D:\unclosed"))
        Assert.Equal("", ExchangeAdmin.ConfigPathOf(""))
    End Sub

    <Fact>
    Public Sub While_our_service_is_registered_its_own_directory_names_the_pipe()
        ExchangeService.ImagePathOverride = Function() """C:\ProgramData\FastMediaSorterExchange\bin\fms-exchange-server.exe"" serve --service --config ""C:\ProgramData\FastMediaSorterExchange\exchange.json"""
        Assert.Equal("C:\ProgramData\FastMediaSorterExchange", ExchangeAdmin.ServiceDataDir())
        ' Without --config the machine store is the binary's own default for the service.
        ExchangeService.MachineDataDirOverride = "M:\machine"
        ExchangeService.ImagePathOverride = Function() """C:\bin\fms-exchange-server.exe"" serve --service"
        Assert.Equal("M:\machine", ExchangeAdmin.ServiceDataDir())
        ' Not ours (no "serve --service"), or none: no service directory.
        ExchangeService.ImagePathOverride = Function() """C:\bin\fms-exchange-server.exe"""
        Assert.Equal("", ExchangeAdmin.ServiceDataDir())
        ExchangeService.ImagePathOverride = Function() ""
        Assert.Equal("", ExchangeAdmin.ServiceDataDir())
    End Sub

    <Fact>
    Public Sub While_our_service_is_registered_a_silent_pipe_is_not_reached_and_never_the_offline_store()
        ' The test host: the reset went "offline" into the per-user store, the screen said done, the service
        ' never saw it. A registered service's pipe that does not answer must say so instead.
        Dim cfg As String = Path.Combine(_dir, "svc-" & Guid.NewGuid().ToString("N").Substring(0, 8), "exchange.json")
        ExchangeService.ImagePathOverride = Function() """" & Path.Combine(_dir, "fms-exchange-server.exe") & """ serve --service --config """ & cfg & """"
        ExchangeProcess.RunOverride = AddressOf Forbidden
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ResetPasswordAsync("sza", "Secret12345x", False, CancellationToken.None).GetAwaiter().GetResult()
        Assert.False(reply.Ok)
        Assert.Equal(ExchangeAdminCodes.NotReached, reply.Code)
        Assert.Equal(ExchangeAdminRoute.NotReached, reply.Route)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each s As IDisposable In _stubs
            Try : s.Dispose() : Catch : End Try
        Next
        ExchangeTestSupport.Reset()
        ExchangeTestSupport.DeleteQuietly(_dir)
    End Sub

    ''' <summary>A runner that fails the test when it is reached: the offline path must not run.</summary>
    Private Shared Function Forbidden(args As String(), stdin As String) As ExchangeProcess.RunResult
        Throw New InvalidOperationException("the offline path must not run here")
    End Function

    Private Shared Function Result(exitCode As Integer, Optional stdout As String = "", Optional stderr As String = "") As ExchangeProcess.RunResult
        Return New ExchangeProcess.RunResult With {.Started = True, .ExitCode = exitCode, .StdOut = stdout, .StdErr = stderr}
    End Function

    ' --- the pipe's name -------------------------------------------------------------------------------

    ''' <summary>The vector the Go suite pins too (<c>TestAdminPipeNameVector</c>): the first 4 bytes of SHA-256 over the lower-cased
    ''' absolute path of the data directory, independently computed with PowerShell.</summary>
    <Theory>
    <InlineData("C:\ProgramData\FastMediaSorterExchange")>
    <InlineData("c:\programdata\fastmediasorterexchange")>
    <InlineData("C:\ProgramData\.\FastMediaSorterExchange")>
    <InlineData("C:\ProgramData\FastMediaSorterExchange\")>
    <InlineData("C:\ProgramData\Other\..\FastMediaSorterExchange")>
    Public Sub The_pipe_name_is_the_one_the_server_derives_from_its_data_directory(dataDir As String)
        Assert.Equal("fms-exchange-admin-afface35", ExchangeAdmin.PipeNameFor(dataDir))
    End Sub

    <Fact>
    Public Sub Two_directories_never_share_a_pipe_and_nothing_is_derived_from_nothing()
        Assert.NotEqual(ExchangeAdmin.PipeNameFor("C:\a"), ExchangeAdmin.PipeNameFor("C:\b"))
        Assert.Equal("", ExchangeAdmin.PipeNameFor(""))
        Assert.Equal("", ExchangeAdmin.PipeNameFor("   "))
        Assert.Matches("^fms-exchange-admin-[0-9a-f]{8}$", ExchangeAdmin.PipeNameFor("C:\x"))
    End Sub

    ' --- the requests ----------------------------------------------------------------------------------

    Private Shared Function Json(request As ExchangeAdminRequest) As JsonElement
        Using doc As JsonDocument = JsonDocument.Parse(request.ToJsonBytes())
            Return doc.RootElement.Clone()
        End Using
    End Function

    Private Shared Function Has(el As JsonElement, name As String) As Boolean
        Dim ignored As JsonElement = Nothing
        Return el.TryGetProperty(name, ignored)
    End Function

    <Fact>
    Public Sub Every_request_has_the_shape_the_go_side_reads()
        Dim add As JsonElement = Json(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, "alice", "Abcdefghi1"))
        Assert.Equal("account.add", add.GetProperty("op").GetString())
        Assert.Equal("alice", add.GetProperty("login").GetString())
        Assert.Equal("Abcdefghi1", add.GetProperty("password").GetString())
        Assert.False(Has(add, "signOut"))

        Dim passwd As JsonElement = Json(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "alice", "Zyxwvutsr9", signOut:=True))
        Assert.Equal("account.passwd", passwd.GetProperty("op").GetString())
        Assert.True(passwd.GetProperty("signOut").GetBoolean())

        Dim revoke As JsonElement = Json(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceRevoke, "alice", deviceId:="AAAAAAAAAAAAAAAAAAAAAA"))
        Assert.Equal("device.revoke", revoke.GetProperty("op").GetString())
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAA", revoke.GetProperty("deviceId").GetString())
        Assert.False(Has(revoke, "password"))

        For Each op As String In {ExchangeAdminRequest.OpAccountList, ExchangeAdminRequest.OpStatus}
            Dim only As JsonElement = Json(New ExchangeAdminRequest(op))
            Assert.Equal(op, only.GetProperty("op").GetString())
            Assert.Single(only.EnumerateObject())
        Next
    End Sub

    <Fact>
    Public Sub A_request_prints_as_its_operation_only_and_stays_within_the_channels_bound()
        Dim r As New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, "alice", "Abcdefghi1")
        Assert.Equal("ExchangeAdminRequest{account.add}", r.ToString())
        Assert.DoesNotContain("Abcdefghi1", r.ToString())
        Assert.DoesNotContain("alice", r.ToString())
        ' the longest legitimate request (a 64-character login and a long typed password) is well inside the 4 KiB the channel reads
        Dim big As New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, New String("a"c, 64), New String("P"c, 1000))
        Assert.True(big.ToJsonBytes().Length < ExchangeAdminRequest.MaxBytes)
    End Sub

    ' --- the answers -----------------------------------------------------------------------------------

    <Fact>
    Public Sub Accounts_devices_and_status_are_read_and_anything_else_is_empty_never_an_exception()
        Dim accounts As List(Of ExchangeAdminAccount) = ExchangeAdminParser.Accounts("[{""login"":""alice"",""createdAt"":""2026-10-07T10:00:00Z"",""devices"":3},{""login"":""bob"",""createdAt"":"""",""devices"":0}]")
        Assert.Equal({"alice", "bob"}, accounts.Select(Function(a) a.Login).ToArray())
        Assert.Equal(3, accounts(0).Devices)
        Assert.Equal("2026-10-07T10:00:00Z", accounts(0).CreatedAt)

        Dim devices As List(Of ExchangeAdminDevice) = ExchangeAdminParser.Devices("[{""deviceId"":""AAAAAAAAAAAAAAAAAAAAAA"",""deviceName"":""PC"",""product"":""fms-windows"",""productVersion"":""26.10.7.1"",""platform"":""Windows 11"",""enrolledAt"":""e"",""lastSeen"":""l"",""online"":true,""roles"":[""receiver""]}]")
        Assert.Single(devices)
        Assert.Equal("PC", devices(0).Name)
        Assert.Equal("fms-windows", devices(0).Product)
        Assert.True(devices(0).Online)

        Dim status As ExchangeAdminStatus = ExchangeAdminParser.Status("{""accounts"":2,""devices"":5,""online"":1,""resources"":4,""broadcasts"":0}")
        Assert.Equal(2, status.Accounts)
        Assert.Equal(5, status.Devices)
        Assert.Equal(1, status.Online)
        Assert.Equal(4, status.Resources)

        For Each bad As String In {"", "   ", "not json", "{""a"":1}", "[1,2,3]", "null"}
            Assert.Empty(ExchangeAdminParser.Accounts(bad))
            Assert.Empty(ExchangeAdminParser.Devices(bad))
            Assert.Equal(0, ExchangeAdminParser.Status(bad).Accounts)
        Next
    End Sub

    <Fact>
    Public Sub An_answer_is_its_code_and_never_the_servers_own_words()
        Dim ok As ExchangeAdminReply = ExchangeAdminParser.Reply("{""ok"":true,""data"":[{""login"":""alice""}]}", ExchangeAdminRoute.Pipe)
        Assert.True(ok.Ok)
        Assert.Equal(ExchangeAdminRoute.Pipe, ok.Route)
        Assert.Single(ExchangeAdminParser.Accounts(ok.DataJson))

        Dim failed As ExchangeAdminReply = ExchangeAdminParser.Reply("{""ok"":false,""error"":""the account exists"",""code"":""exists""}", ExchangeAdminRoute.Pipe)
        Assert.False(failed.Ok)
        Assert.Equal(ExchangeAdminCodes.Exists, failed.Code)

        Assert.Equal(ExchangeAdminCodes.Internal, ExchangeAdminParser.Reply("{""ok"":false}", ExchangeAdminRoute.Pipe).Code)
        Assert.Equal(ExchangeAdminCodes.Internal, ExchangeAdminParser.Reply("[1]", ExchangeAdminRoute.Pipe).Code)
        Assert.Equal(ExchangeAdminCodes.Internal, ExchangeAdminParser.Reply("{broken", ExchangeAdminRoute.Pipe).Code)
        Assert.Equal(ExchangeAdminCodes.NotReached, ExchangeAdminParser.Reply("", ExchangeAdminRoute.Pipe).Code)
        ' a success with no data (add, passwd, remove, revoke) is a success with an empty payload
        Assert.True(ExchangeAdminParser.Reply("{""ok"":true}", ExchangeAdminRoute.Pipe).Ok)
    End Sub

    ' --- a stub pipe --------------------------------------------------------------------------------------

    ''' <summary>A named pipe that answers one connection at a time with a handler over the request text. It is the server's side of
    ''' 162 section 4: one JSON request per connection, one answer line.</summary>
    Private NotInheritable Class StubPipe
        Implements IDisposable

        Public ReadOnly Name As String = "fms-exchange-admin-test-" & Guid.NewGuid().ToString("N").Substring(0, 12)
        Public ReadOnly Requests As New List(Of String)()
        Private ReadOnly _cts As New CancellationTokenSource()
        Private ReadOnly _loop As Task

        Public Sub New(handler As Func(Of String, String), Optional closeWithoutAnswer As Boolean = False)
            _loop = Task.Run(Async Function()
                                 While Not _cts.IsCancellationRequested
                                     Using server As New NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
                                         Try
                                             Await server.WaitForConnectionAsync(_cts.Token)
                                             Dim buffer(8191) As Byte
                                             Dim n As Integer = Await server.ReadAsync(buffer, 0, buffer.Length, _cts.Token)
                                             Dim text As String = Encoding.UTF8.GetString(buffer, 0, n)
                                             SyncLock Requests
                                                 Requests.Add(text)
                                             End SyncLock
                                             If Not closeWithoutAnswer Then
                                                 Dim answer As Byte() = Encoding.UTF8.GetBytes(handler(text) & vbLf)
                                                 Await server.WriteAsync(answer, 0, answer.Length, _cts.Token)
                                                 Await server.FlushAsync(_cts.Token)
                                                 server.WaitForPipeDrain()
                                             End If
                                         Catch ex As OperationCanceledException
                                             Return
                                         Catch ex As IOException
                                         End Try
                                     End Using
                                 End While
                             End Function)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _cts.Cancel()
            Try : _loop.Wait(2000) : Catch : End Try
            _cts.Dispose()
        End Sub
    End Class

    Private Function Stub(handler As Func(Of String, String), Optional closeWithoutAnswer As Boolean = False) As StubPipe
        Dim s As New StubPipe(handler, closeWithoutAnswer)
        _stubs.Add(s)
        ExchangeAdmin.PipeNameOverride = s.Name
        Return s
    End Function

    <Fact>
    Public Sub A_request_goes_through_the_pipe_and_the_answer_is_read_back()
        Dim pipe As StubPipe = Stub(Function(req) "{""ok"":true,""data"":[{""login"":""alice"",""createdAt"":""c"",""devices"":2}]}")
        ExchangeProcess.RunOverride = AddressOf Forbidden
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(reply.Ok)
        Assert.Equal(ExchangeAdminRoute.Pipe, reply.Route)
        Assert.Equal(2, ExchangeAdminParser.Accounts(reply.DataJson)(0).Devices)
        Assert.Equal("account.list", JsonDocument.Parse(pipe.Requests.Single()).RootElement.GetProperty("op").GetString())
    End Sub

    <Fact>
    Public Sub An_add_carries_the_password_on_the_pipe_and_a_refusal_is_its_code()
        Dim pipe As StubPipe = Stub(Function(req) "{""ok"":false,""error"":""the account exists"",""code"":""exists""}")
        Dim reply As ExchangeAdminReply = ExchangeAdmin.AddAccountAsync("alice", "Abcdefghi1", CancellationToken.None).GetAwaiter().GetResult()
        Assert.False(reply.Ok)
        Assert.Equal(ExchangeAdminCodes.Exists, reply.Code)
        Assert.Equal(ExchangeAdminRoute.Pipe, reply.Route)
        Dim sent As JsonElement = JsonDocument.Parse(pipe.Requests.Single()).RootElement
        Assert.Equal("Abcdefghi1", sent.GetProperty("password").GetString())
        ' ... and nowhere else: not in companion.log
        Dim logPath As String = AppFileLogger.LogFilePath
        If Not String.IsNullOrEmpty(logPath) AndAlso File.Exists(logPath) Then
            Using fs As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using reader As New StreamReader(fs)
                    Dim text As String = reader.ReadToEnd()
                    Assert.DoesNotContain("Abcdefghi1", text)
                    Assert.Contains("Exchange admin: account.add - exists (pipe)", text)
                End Using
            End Using
        End If
    End Sub

    <Fact>
    Public Sub A_pipe_that_closes_without_an_answer_is_not_reached_and_is_never_a_success()
        Stub(Function(req) "", closeWithoutAnswer:=True)
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult()
        Assert.False(reply.Ok)
        Assert.Equal(ExchangeAdminCodes.NotReached, reply.Code)
        Assert.Equal(ExchangeAdminRoute.Pipe, reply.Route)
    End Sub

    <Fact>
    Public Sub An_answer_above_the_bound_is_not_reached_never_a_huge_read()
        Stub(Function(req) "{""ok"":true,""data"":""" & New String("x"c, ExchangeAdmin.MaxResponseBytes + 10) & """}")
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult()
        Assert.False(reply.Ok)
    End Sub

    <Fact>
    Public Sub A_cancelled_call_throws_and_does_not_fall_through_to_the_offline_path()
        ExchangeAdmin.PipeNameOverride = "fms-exchange-admin-test-" & Guid.NewGuid().ToString("N").Substring(0, 12)
        ExchangeProcess.RunOverride = AddressOf Forbidden
        Using cts As New CancellationTokenSource()
            cts.Cancel()
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() ExchangeAdmin.ListAccountsAsync(cts.Token).GetAwaiter().GetResult())
        End Using
    End Sub

    ' --- the offline path --------------------------------------------------------------------------------

    <Fact>
    Public Sub With_no_pipe_the_same_request_goes_through_the_binarys_own_verbs()
        ExchangeAdmin.PipeNameOverride = "fms-exchange-admin-test-" & Guid.NewGuid().ToString("N").Substring(0, 12)
        Dim seenArgs As String() = Nothing
        Dim seenStdin As String = Nothing
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          seenArgs = args
                                          seenStdin = stdin
                                          Return Result(0, "[{""login"":""alice"",""createdAt"":""c"",""devices"":1}]")
                                      End Function
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(reply.Ok)
        Assert.Equal(ExchangeAdminRoute.Offline, reply.Route)
        Assert.Equal({"account", "list", "--json"}, seenArgs)
        Assert.Equal("alice", ExchangeAdminParser.Accounts(reply.DataJson).Single().Login)
        Assert.Null(seenStdin)
    End Sub

    <Fact>
    Public Sub Offline_a_password_goes_over_stdin_and_a_login_after_the_double_dash_so_it_is_never_an_option()
        Dim all As New List(Of Tuple(Of String(), String))()
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          all.Add(Tuple.Create(args, stdin))
                                          Return Result(0)
                                      End Function
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, "-dash", "Abcdefghi1"))
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "alice", "Zyxwvutsr9", signOut:=True))
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, "alice", "Zyxwvutsr9"))
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountRemove, "alice"))
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceList, "alice"))
        ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceRevoke, "alice", deviceId:="-pjgKQFygDW5ohQ4zk4Isw"))

        Assert.Equal({"account", "add", "--", "-dash"}, all(0).Item1)
        Assert.Equal("Abcdefghi1", all(0).Item2)
        Assert.Equal({"account", "passwd", "--sign-out", "--", "alice"}, all(1).Item1)
        Assert.Equal("Zyxwvutsr9", all(1).Item2)
        Assert.Equal({"account", "passwd", "--", "alice"}, all(2).Item1)
        Assert.Equal({"account", "remove", "--", "alice"}, all(3).Item1)
        Assert.Null(all(3).Item2)
        Assert.Equal({"device", "list", "--json", "--", "alice"}, all(4).Item1)
        Assert.Equal({"device", "revoke", "--", "alice", "-pjgKQFygDW5ohQ4zk4Isw"}, all(5).Item1)
        For Each invocation As Tuple(Of String(), String) In all
            For Each secret As String In {"Abcdefghi1", "Zyxwvutsr9"}
                Assert.DoesNotContain(secret, String.Join(" ", invocation.Item1))
            Next
        Next
    End Sub

    <Theory>
    <InlineData(0, "", "", True, "")>
    <InlineData(3, "", "the password must be at least 10 characters long", False, "weak-password")>
    <InlineData(2, "", "login must be 3-64 characters", False, "bad-login")>
    <InlineData(1, "", "no such account", False, "no-account")>
    <InlineData(1, "", "the account exists", False, "exists")>
    <InlineData(1, "", "no such device in that account", False, "not-found")>
    <InlineData(1, "", "a server is running on this data directory but its admin channel is not reachable: x", False, "not-reached")>
    <InlineData(1, "", "something unexpected", False, "internal")>
    <InlineData(4, "", "cannot open the accounts", False, "internal")>
    Public Sub The_exit_codes_of_the_binary_map_onto_the_channels_codes(exitCode As Integer, stdout As String, stderr As String, ok As Boolean, code As String)
        ExchangeProcess.RunOverride = Function(args, stdin) Result(exitCode, stdout, stderr)
        Dim reply As ExchangeAdminReply = ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, "alice", "Abcdefghi1"))
        Assert.Equal(ok, reply.Ok)
        Assert.Equal(code, reply.Code)
        Assert.Equal(ExchangeAdminRoute.Offline, reply.Route)
    End Sub

    <Fact>
    Public Sub A_run_that_did_not_start_or_timed_out_is_not_reached()
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult()
        Assert.Equal(ExchangeAdminCodes.NotReached, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList)).Code)
        ExchangeProcess.RunOverride = Function(args, stdin) New ExchangeProcess.RunResult With {.Started = True, .TimedOut = True}
        Assert.Equal(ExchangeAdminCodes.NotReached, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList)).Code)
        File.Delete(Path.Combine(_dir, ExchangeProcess.ExeName))
        ExchangeProcess.RunOverride = AddressOf Forbidden
        Assert.Equal(ExchangeAdminCodes.NotReached, ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList)).Code)
    End Sub

    <Fact>
    Public Sub Offline_status_is_the_counts_of_inspect_and_nobody_is_online()
        ExchangeProcess.RunOverride = Function(args, stdin) Result(0, "{""dataDir"":""D"",""accounts"":3,""devices"":7}")
        Dim reply As ExchangeAdminReply = ExchangeAdmin.SendOffline(New ExchangeAdminRequest(ExchangeAdminRequest.OpStatus))
        Assert.True(reply.Ok)
        Dim s As ExchangeAdminStatus = ExchangeAdminParser.Status(reply.DataJson)
        Assert.Equal(3, s.Accounts)
        Assert.Equal(7, s.Devices)
        Assert.Equal(0, s.Online)
    End Sub

    ''' <summary>The pipe's name comes from the data directory <c>inspect</c> reports: with no override the client asks the binary once.</summary>
    <Fact>
    Public Sub Without_an_override_the_pipe_is_named_from_the_data_directory_the_binary_reports()
        Dim asked As New List(Of String)()
        Dim unique As String = Guid.NewGuid().ToString("N")
        ExchangeProcess.RunOverride = Function(args, stdin)
                                          asked.Add(args(0))
                                          If args(0) = "inspect" Then Return Result(0, "{""dataDir"":""C:\\fms-test-no-such-dir\\" & unique & """,""accounts"":1}")
                                          Return Result(0, "[]")
                                      End Function
        Dim reply As ExchangeAdminReply = ExchangeAdmin.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult()
        ' no server of that directory runs on a test machine: the call fell back to the offline path after asking inspect
        Assert.True(reply.Ok)
        ' (the seams are process-global: a window of another test class may ask inspect meanwhile, so the order of FIRST appearances is what counts)
        Assert.Equal({"inspect", "account"}, asked.Distinct().ToArray())
    End Sub

    ' --- secrets are never stored -----------------------------------------------------------------------

    ''' <summary>168 rule 4 and the T-4 scan: the files that hold or move a password never persist it, never log it, never put it on a
    ''' command line, and log neither a login nor a password name.</summary>
    <Fact>
    Public Sub The_files_that_touch_a_password_never_store_log_or_pass_it_on_the_command_line()
        Dim files As String() = {"Core\ExchangeAdmin.vb", "Core\ExchangeAdminModel.vb", "Core\ExchangeAdminText.vb", "Core\ExchangeServerWizard.vb",
                                 "Forms\Exchange_Account_Admin_Form.vb", "Forms\Exchange_Server_Wizard_Form.vb", "Forms\Share_Settings_Form.ExchangeAdmin.vb"}
        For Each file As String In files
            Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(file))
            For Each forbidden As String In {"SaveSetting(", "GetSetting(", "File.Write", "File.Append", "File.Create", "StreamWriter", "Registry", "My.Settings",
                                             "ProcessStartInfo", "ArgumentList.Add", "Clipboard.SetText", "Debug.WriteLine", "Console.Write"}
                Assert.False(code.Contains(forbidden), file & " touches " & forbidden & " in a file that handles a password")
            Next
            ' "Settings.Default" as a member of the application settings object - not ExchangeSettings.DefaultPort
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(code, "\bSettings\.Default\b"), file & " reads the application settings")
            ' System.Diagnostics.Process.Start - not ExchangeProcess.Start, which asks the binary module to start its own server
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(code, "(?<![A-Za-z])Process\.Start\b"), file & " starts a program itself")
            
            For Each line As String In code.Split({vbLf}, StringSplitOptions.None)
                If Not line.Contains("AppFileLogger.") Then Continue For
                For Each word As String In {"assword", "ogin", "Plaintext", "StdOut", "StdErr", "stdin", "Fingerprint", "Endpoint"}
                    Assert.False(line.Contains(word), file & ": a log call mentions " & word & ": " & line.Trim())
                Next
            Next
        Next
    End Sub

    ''' <summary>The test host's own public address never appears in a tracked file (ticket 182: documentation addresses only).</summary>
    <Fact>
    Public Sub No_new_source_or_table_names_a_real_hosts_address()
        Dim needle As String = "193" & "." & "178" & "." & "50"
        For Each file As String In {"Core\ExchangeAdmin.vb", "Core\ExchangeAdminModel.vb", "Core\ExchangeAdminText.vb", "Core\ExchangeServerWizard.vb",
                                    "Forms\Exchange_Account_Admin_Form.vb", "Forms\Exchange_Server_Wizard_Form.vb", "Forms\Share_Settings_Form.ExchangeAdmin.vb",
                                    "Localization\Localization.ExchangeAdmin.vb"}
            Assert.DoesNotContain(needle, ExchangeTestSupport.ReadSource(file))
        Next
    End Sub

End Class
