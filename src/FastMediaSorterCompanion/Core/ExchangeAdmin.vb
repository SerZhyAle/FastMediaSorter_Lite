Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Pipes
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>The stable codes of a failed admin answer: the Go module's (<c>internal/admin</c>), plus the one this
''' client adds when the server could not be reached at all. A caller maps a code to a sentence of its own - the
''' server's text is never shown.</summary>
Friend NotInheritable Class ExchangeAdminCodes
    Private Sub New()
    End Sub

    Friend Const BadRequest As String = "bad-request"
    Friend Const NoAccount As String = "no-account"
    Friend Const Exists As String = "exists"
    Friend Const BadLogin As String = "bad-login"
    Friend Const WeakPassword As String = "weak-password"
    Friend Const NotFound As String = "not-found"
    Friend Const Internal As String = "internal"
    ''' <summary>Ours: neither the admin pipe nor the binary's offline path could be used.</summary>
    Friend Const NotReached As String = "not-reached"
End Class

''' <summary>Which way an answer came: the running server's admin pipe, or the binary editing its stores itself
''' because no server held the data directory (162 section 4).</summary>
Friend Enum ExchangeAdminRoute
    NotReached
    Pipe
    Offline
End Enum

''' <summary>One answer of the admin channel. It never holds a secret: the server returns none.</summary>
Friend NotInheritable Class ExchangeAdminReply
    Public Property Ok As Boolean
    Public Property Code As String = ""
    Public Property DataJson As String = ""
    Public Property Route As ExchangeAdminRoute = ExchangeAdminRoute.NotReached

    Friend Shared Function Failure(code As String, route As ExchangeAdminRoute) As ExchangeAdminReply
        Return New ExchangeAdminReply With {.Ok = False, .Code = code, .Route = route}
    End Function

    Friend Shared Function Success(dataJson As String, route As ExchangeAdminRoute) As ExchangeAdminReply
        Return New ExchangeAdminReply With {.Ok = True, .DataJson = If(dataJson, ""), .Route = route}
    End Function
End Class

''' <summary>One request of the admin channel (162 section 4). It may hold a password, so it prints as its
''' operation only and its JSON is built into a buffer the sender wipes.</summary>
Friend NotInheritable Class ExchangeAdminRequest
    Friend Const OpAccountAdd As String = "account.add"
    Friend Const OpAccountPasswd As String = "account.passwd"
    Friend Const OpAccountRemove As String = "account.remove"
    Friend Const OpAccountList As String = "account.list"
    Friend Const OpDeviceList As String = "device.list"
    Friend Const OpDeviceRevoke As String = "device.revoke"
    Friend Const OpStatus As String = "status"

    ''' <summary>The channel's bound on one request (<c>admin.MaxRequest</c>).</summary>
    Friend Const MaxBytes As Integer = 4096

    Public ReadOnly Op As String
    Public ReadOnly Login As String
    Public ReadOnly Password As String
    Public ReadOnly DeviceId As String
    Public ReadOnly SignOut As Boolean

    Public Sub New(op As String, Optional login As String = "", Optional password As String = "", Optional deviceId As String = "", Optional signOut As Boolean = False)
        Me.Op = op
        Me.Login = If(login, "")
        Me.Password = If(password, "")
        Me.DeviceId = If(deviceId, "")
        Me.SignOut = signOut
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeAdminRequest{" & Op & "}"
    End Function

    ''' <summary>The JSON of the request, as the Go side reads it (<c>admin.Request</c>). The caller overwrites
    ''' the array after sending it; the password is in it.</summary>
    Public Function ToJsonBytes() As Byte()
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteString("op", Op)
                If Login.Length > 0 Then w.WriteString("login", Login)
                If Password.Length > 0 Then w.WriteString("password", Password)
                If DeviceId.Length > 0 Then w.WriteString("deviceId", DeviceId)
                If SignOut Then w.WriteBoolean("signOut", True)
                w.WriteEndObject()
            End Using
            Return ms.ToArray()
        End Using
    End Function
End Class

Friend NotInheritable Class ExchangeAdminAccount
    Public Property Login As String = ""
    Public Property CreatedAt As String = ""
    Public Property Devices As Integer
End Class

Friend NotInheritable Class ExchangeAdminDevice
    Public Property DeviceId As String = ""
    Public Property Name As String = ""
    Public Property Product As String = ""
    Public Property ProductVersion As String = ""
    Public Property Platform As String = ""
    Public Property EnrolledAt As String = ""
    Public Property LastSeen As String = ""
    Public Property Online As Boolean
End Class

''' <summary>The totals a status request may show: counts, never a list and never a secret.</summary>
Friend NotInheritable Class ExchangeAdminStatus
    Public Property Accounts As Integer
    Public Property Devices As Integer
    Public Property Online As Integer
    Public Property Resources As Integer
    Public Property Broadcasts As Integer
End Class

''' <summary>Reading the data of an answer. Pure: a text in, a list out; a member that is missing is the zero value
''' and text that is not what it should be is an empty list, never an exception.</summary>
Friend NotInheritable Class ExchangeAdminParser
    Private Sub New()
    End Sub

    Friend Shared Function Accounts(dataJson As String) As List(Of ExchangeAdminAccount)
        Dim list As New List(Of ExchangeAdminAccount)()
        For Each el As JsonElement In Items(dataJson)
            list.Add(New ExchangeAdminAccount With {.Login = Str(el, "login"), .CreatedAt = Str(el, "createdAt"), .Devices = Num(el, "devices")})
        Next
        Return list
    End Function

    Friend Shared Function Devices(dataJson As String) As List(Of ExchangeAdminDevice)
        Dim list As New List(Of ExchangeAdminDevice)()
        For Each el As JsonElement In Items(dataJson)
            list.Add(New ExchangeAdminDevice With {.DeviceId = Str(el, "deviceId"), .Name = Str(el, "deviceName"), .Product = Str(el, "product"),
                .ProductVersion = Str(el, "productVersion"), .Platform = Str(el, "platform"), .EnrolledAt = Str(el, "enrolledAt"),
                .LastSeen = Str(el, "lastSeen"), .Online = Bool(el, "online")})
        Next
        Return list
    End Function

    Friend Shared Function Status(dataJson As String) As ExchangeAdminStatus
        Dim s As New ExchangeAdminStatus()
        Try
            Using doc As JsonDocument = JsonDocument.Parse(If(dataJson, ""))
                If doc.RootElement.ValueKind <> JsonValueKind.Object Then Return s
                s.Accounts = Num(doc.RootElement, "accounts")
                s.Devices = Num(doc.RootElement, "devices")
                s.Online = Num(doc.RootElement, "online")
                s.Resources = Num(doc.RootElement, "resources")
                s.Broadcasts = Num(doc.RootElement, "broadcasts")
            End Using
        Catch ex As JsonException
        End Try
        Return s
    End Function

    Private Shared Function Items(dataJson As String) As List(Of JsonElement)
        Dim list As New List(Of JsonElement)()
        If String.IsNullOrWhiteSpace(dataJson) Then Return list
        Try
            Using doc As JsonDocument = JsonDocument.Parse(dataJson)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return list
                For Each el As JsonElement In doc.RootElement.EnumerateArray()
                    If el.ValueKind = JsonValueKind.Object Then list.Add(el.Clone())
                Next
            End Using
        Catch ex As JsonException
        End Try
        Return list
    End Function

    Private Shared Function Str(el As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return If(v.GetString(), "")
        Return ""
    End Function

    Private Shared Function Num(el As JsonElement, name As String) As Integer
        Dim v As JsonElement = Nothing
        Dim n As Integer
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.Number AndAlso v.TryGetInt32(n) Then Return n
        Return 0
    End Function

    Private Shared Function Bool(el As JsonElement, name As String) As Boolean
        Dim v As JsonElement = Nothing
        Return el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.True
    End Function

    ''' <summary>Reads one <c>{ok, error, code, data}</c> answer. The server's own <c>error</c> sentence is dropped
    ''' on purpose: the code is the contract, the words are ours.</summary>
    Friend Shared Function Reply(json As String, route As ExchangeAdminRoute) As ExchangeAdminReply
        If String.IsNullOrWhiteSpace(json) Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, route)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.Internal, route)
                Dim ok As JsonElement = Nothing
                If root.TryGetProperty("ok", ok) AndAlso ok.ValueKind = JsonValueKind.True Then
                    Dim data As JsonElement = Nothing
                    Dim text As String = If(root.TryGetProperty("data", data), data.GetRawText(), "")
                    Return ExchangeAdminReply.Success(text, route)
                End If
                Dim code As String = Str(root, "code")
                Return ExchangeAdminReply.Failure(If(code.Length > 0, code, ExchangeAdminCodes.Internal), route)
            End Using
        Catch ex As JsonException
            Return ExchangeAdminReply.Failure(ExchangeAdminCodes.Internal, route)
        End Try
    End Function
End Class

''' <summary>
''' The Share Manager's client of the exchange server's admin channel (ticket 168 T-1, 162 section 4).
'''
''' One request per connection, bounded and cancellable: a named pipe <c>fms-exchange-admin-&lt;8 hex&gt;</c> whose name is
''' the first 4 bytes of SHA-256 over the lower-cased absolute path of the data directory (the Go side pins the same
''' vector). A password travels on this local channel and nowhere else - never on a command line, never logged.
'''
''' When no server holds the data directory there is no pipe, and the SAME operation goes through the binary's own
''' offline path (the account and device verbs edit the stores under the data directory's lock). A pipe that exists
''' but refuses this user is a named failure, never a silent fall back: the offline path would only find the lock held.
'''
''' Nothing here returns, stores or logs a secret: the log gets the operation name and the route.
''' </summary>
Friend Module ExchangeAdmin

    Friend Const PipePrefix As String = "fms-exchange-admin-"

    ''' <summary>A server that runs has its pipe; one that does not has none, and waiting longer for it would only slow
    ''' the offline path.</summary>
    Friend Const ConnectTimeoutMs As Integer = 400

    ''' <summary>The Go side's own bound on one exchange (<c>admin.ExchangeLimit</c>).</summary>
    Friend Const ExchangeTimeoutMs As Integer = 10000

    ''' <summary>The channel's bound on an answer (<c>admin.MaxResponse</c>).</summary>
    Friend Const MaxResponseBytes As Integer = 1 << 20

    ''' <summary>Tests (and only tests) name a pipe of their own, so a stub server stands in for the real one.</summary>
    Friend PipeNameOverride As String

    ''' <summary>The pipe's name (without the <c>\\.\pipe\</c> prefix) for a data directory.</summary>
    Friend Function PipeNameFor(dataDir As String) As String
        If String.IsNullOrWhiteSpace(dataDir) Then Return ""
        Dim full As String
        Try
            full = Path.GetFullPath(dataDir)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return ""
        End Try
        ' filepath.Abs cleans a trailing separator away (the root keeps its own)
        Dim root As String = If(Path.GetPathRoot(full), "")
        If full.Length > root.Length Then full = full.TrimEnd("\"c, "/"c)
        Dim sum As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant()))
        Return PipePrefix & Convert.ToHexString(sum, 0, 4).ToLowerInvariant()
    End Function

    ' --- one request ---------------------------------------------------------------------------

    ''' <summary>Sends one request: through the running server's pipe when there is one, else through the binary's
    ''' offline path. Blocks no thread of the UI: the caller awaits it.</summary>
    Friend Async Function SendAsync(request As ExchangeAdminRequest, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Dim name As String = If(String.IsNullOrEmpty(PipeNameOverride), Await Task.Run(Function() PipeNameFor(ExchangeProcess.DataDir())).ConfigureAwait(False), PipeNameOverride)
        If name.Length > 0 Then
            Dim viaPipe As ExchangeAdminReply = Await SendViaPipeAsync(name, request, ct).ConfigureAwait(False)
            If viaPipe IsNot Nothing Then
                AppFileLogger.WriteLine("Exchange admin: " & request.Op & " - " & If(viaPipe.Ok, "ok", viaPipe.Code) & " (pipe)")
                Return viaPipe
            End If
        End If
        ct.ThrowIfCancellationRequested()
        Dim offline As ExchangeAdminReply = Await Task.Run(Function() SendOffline(request)).ConfigureAwait(False)
        AppFileLogger.WriteLine("Exchange admin: " & request.Op & " - " & If(offline.Ok, "ok", offline.Code) & " (offline)")
        Return offline
    End Function

    ''' <summary>One exchange on the pipe. Nothing (Nothing, not a failure) when there is no pipe: the caller then takes
    ''' the offline path. A pipe that refuses this user or breaks mid-exchange is a failure with a code.</summary>
    Friend Async Function SendViaPipeAsync(pipeName As String, request As ExchangeAdminRequest, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Dim payload As Byte() = request.ToJsonBytes()
        Try
            If payload.Length > ExchangeAdminRequest.MaxBytes Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.BadRequest, ExchangeAdminRoute.Pipe)
            Using pipe As New NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                Try
                    Await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(False)
                Catch ex As TimeoutException
                    Return Nothing
                Catch ex As UnauthorizedAccessException
                    Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.Pipe)
                End Try
                Using limit As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    limit.CancelAfter(ExchangeTimeoutMs)
                    Await pipe.WriteAsync(payload, 0, payload.Length, limit.Token).ConfigureAwait(False)
                    Await pipe.FlushAsync(limit.Token).ConfigureAwait(False)
                    Dim answer As String = Await ReadAnswerAsync(pipe, limit.Token).ConfigureAwait(False)
                    Return ExchangeAdminParser.Reply(answer, ExchangeAdminRoute.Pipe)
                End Using
            End Using
        Catch ex As OperationCanceledException
            If ct.IsCancellationRequested Then Throw
            Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.Pipe)
        Catch ex As IOException
            Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.Pipe)
        Finally
            Array.Clear(payload, 0, payload.Length)
        End Try
    End Function

    ''' <summary>Reads until the newline the server ends its answer with (or the end of the pipe), at most
    ''' <see cref="MaxResponseBytes"/>.</summary>
    Private Async Function ReadAnswerAsync(pipe As Stream, ct As CancellationToken) As Task(Of String)
        Dim buffer(4095) As Byte
        Using ms As New MemoryStream()
            Do
                Dim n As Integer = Await pipe.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                If n <= 0 Then Exit Do
                ms.Write(buffer, 0, n)
                If ms.Length > MaxResponseBytes Then Return ""
                If Array.IndexOf(buffer, CByte(10), 0, n) >= 0 Then Exit Do
            Loop
            Return Encoding.UTF8.GetString(ms.GetBuffer(), 0, CInt(ms.Length)).Trim()
        End Using
    End Function

    ' --- the offline path ----------------------------------------------------------------------

    ''' <summary>The same request through the binary's own verbs (it takes the data directory's lock and edits the
    ''' stores, or, when a server holds the lock after all, speaks to its pipe itself). A password goes over stdin.</summary>
    Friend Function SendOffline(request As ExchangeAdminRequest) As ExchangeAdminReply
        If Not ExchangeProcess.IsAvailable() Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.NotReached)
        Dim args As String() = Nothing
        Dim stdin As String = Nothing
        Select Case request.Op
            Case ExchangeAdminRequest.OpAccountList
                args = {"account", "list", "--json"}
            Case ExchangeAdminRequest.OpDeviceList
                args = {"device", "list", "--json", "--", request.Login}
            Case ExchangeAdminRequest.OpAccountAdd
                args = {"account", "add", "--", request.Login}
                stdin = request.Password
            Case ExchangeAdminRequest.OpAccountPasswd
                args = If(request.SignOut, {"account", "passwd", "--sign-out", "--", request.Login}, {"account", "passwd", "--", request.Login})
                stdin = request.Password
            Case ExchangeAdminRequest.OpAccountRemove
                args = {"account", "remove", "--", request.Login}
            Case ExchangeAdminRequest.OpDeviceRevoke
                args = {"device", "revoke", "--", request.Login, request.DeviceId}
            Case ExchangeAdminRequest.OpStatus
                Return OfflineStatus()
            Case Else
                Return ExchangeAdminReply.Failure(ExchangeAdminCodes.BadRequest, ExchangeAdminRoute.Offline)
        End Select
        Dim r As ExchangeProcess.RunResult = ExchangeProcess.Run(args, stdin, ExchangeProcess.CommandTimeoutMs)
        Return OfflineReply(request.Op, r)
    End Function

    ''' <summary>Maps one run of an account or device verb onto an admin answer. The exit codes are the binary's
    ''' (0 ok, 3 a refused password, 2 a bad login or request, 1 the account or the device is not as asked, 4 the
    ''' stores cannot be read); the stderr sentence only tells the two kinds of exit 1 apart and is never kept.</summary>
    Friend Function OfflineReply(op As String, r As ExchangeProcess.RunResult) As ExchangeAdminReply
        If Not r.Started OrElse r.TimedOut Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.NotReached)
        Select Case r.ExitCode
            Case ExchangeProcess.ExitOk
                Dim list As Boolean = op = ExchangeAdminRequest.OpAccountList OrElse op = ExchangeAdminRequest.OpDeviceList
                Return ExchangeAdminReply.Success(If(list, If(r.StdOut, "").Trim(), ""), ExchangeAdminRoute.Offline)
            Case ExchangeProcess.ExitPolicy
                Return ExchangeAdminReply.Failure(ExchangeAdminCodes.WeakPassword, ExchangeAdminRoute.Offline)
            Case ExchangeProcess.ExitUsage
                Return ExchangeAdminReply.Failure(ExchangeAdminCodes.BadLogin, ExchangeAdminRoute.Offline)
            Case ExchangeProcess.ExitFailure
                Dim err As String = If(r.StdErr, "")
                If err.Contains("no such account", StringComparison.OrdinalIgnoreCase) Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NoAccount, ExchangeAdminRoute.Offline)
                If err.Contains("account exists", StringComparison.OrdinalIgnoreCase) Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.Exists, ExchangeAdminRoute.Offline)
                If err.Contains("no such device", StringComparison.OrdinalIgnoreCase) Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotFound, ExchangeAdminRoute.Offline)
                If err.Contains("admin channel is not reachable", StringComparison.OrdinalIgnoreCase) Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.Offline)
                Return ExchangeAdminReply.Failure(ExchangeAdminCodes.Internal, ExchangeAdminRoute.Offline)
            Case Else
                Return ExchangeAdminReply.Failure(ExchangeAdminCodes.Internal, ExchangeAdminRoute.Offline)
        End Select
    End Function

    ''' <summary>There is no offline <c>status</c> verb: the counts of <c>inspect</c> stand in (nobody is online when no
    ''' server runs).</summary>
    Private Function OfflineStatus() As ExchangeAdminReply
        Dim report As ExchangeProcess.InspectReport = ExchangeProcess.Inspect()
        If Not report.Answered Then Return ExchangeAdminReply.Failure(ExchangeAdminCodes.NotReached, ExchangeAdminRoute.NotReached)
        Dim json As String = "{""accounts"":" & report.Accounts.ToString(Globalization.CultureInfo.InvariantCulture) &
                             ",""devices"":" & report.Devices.ToString(Globalization.CultureInfo.InvariantCulture) & ",""online"":0}"
        Return ExchangeAdminReply.Success(json, ExchangeAdminRoute.Offline)
    End Function

    ' --- the operations ------------------------------------------------------------------------

    Friend Function ListAccountsAsync(ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountList), ct)
    End Function

    Friend Function ListDevicesAsync(login As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceList, login), ct)
    End Function

    Friend Function AddAccountAsync(login As String, password As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountAdd, login, password), ct)
    End Function

    Friend Function ResetPasswordAsync(login As String, password As String, signOutEveryDevice As Boolean, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountPasswd, login, password, signOut:=signOutEveryDevice), ct)
    End Function

    Friend Function RemoveAccountAsync(login As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpAccountRemove, login), ct)
    End Function

    Friend Function RevokeDeviceAsync(login As String, deviceId As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpDeviceRevoke, login, deviceId:=deviceId), ct)
    End Function

    Friend Function StatusAsync(ct As CancellationToken) As Task(Of ExchangeAdminReply)
        Return SendAsync(New ExchangeAdminRequest(ExchangeAdminRequest.OpStatus), ct)
    End Function

End Module
