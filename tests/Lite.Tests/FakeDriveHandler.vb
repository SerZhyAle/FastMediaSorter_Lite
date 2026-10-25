#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

''' <summary>One file or folder of the fake appDataFolder.</summary>
Friend NotInheritable Class FakeDriveFile

    Public Id As String = ""
    Public Name As String = ""
    Public MimeType As String = "application/octet-stream"
    Public Parents As New List(Of String)
    Public Content As Byte() = Array.Empty(Of Byte)()
    Public CreatedTime As DateTimeOffset
    Public ModifiedTime As DateTimeOffset
    Public AppProperties As New Dictionary(Of String, String)(StringComparer.Ordinal)

    Public ReadOnly Property IsFolder As Boolean
        Get
            Return MimeType = "application/vnd.google-apps.folder"
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "FakeDriveFile"
    End Function
End Class

''' <summary>What a rule sees of a request.</summary>
Friend NotInheritable Class FakeDriveRequest

    Public Method As String = ""
    Public Path As String = ""
    Public Query As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Public Body As Byte() = Array.Empty(Of Byte)()
    Public ContentType As String = ""
    Public Bearer As String = ""
    Public ContentRange As System.Net.Http.Headers.ContentRangeHeaderValue

    ''' <summary>The body as Latin-1 text, so binary content can be searched for a name.</summary>
    Public Function BodyText() As String
        Return Encoding.Latin1.GetString(Body)
    End Function

    Public Overrides Function ToString() As String
        Return Method & " " & Path
    End Function
End Class

''' <summary>An in-memory Google Drive (appDataFolder, v3) and Google's token endpoints, as an HttpMessageHandler. The server's
''' clock is <see cref="ServerNow"/>: every modifiedTime, createdTime and `Date` header comes from it, and a test moves it by hand
''' - so a record that was "written an hour ago" on a device with a wrong clock looks exactly as it does on the real Drive.</summary>
Friend NotInheritable Class FakeDriveHandler
    Inherits HttpMessageHandler

    Public Const RootId As String = "rootAppData"

    Private NotInheritable Class FailRule
        Public Predicate As Func(Of FakeDriveRequest, Boolean)
        Public Remaining As Integer
        Public Status As Integer
        Public Reason As String = ""
    End Class

    Private NotInheritable Class UploadSession
        Public Name As String = ""
        Public MimeType As String = ""
        Public Parents As New List(Of String)
        Public Total As Long
        Public Received As New MemoryStream()
    End Class

    Private ReadOnly _lock As New Object()
    Private ReadOnly _files As New List(Of FakeDriveFile)
    Private ReadOnly _rules As New List(Of FailRule)
    Private ReadOnly _sessions As New Dictionary(Of String, UploadSession)(StringComparer.Ordinal)
    Private ReadOnly _events As New List(Of String)
    Private ReadOnly _revoked As New List(Of String)
    ''' <summary>Access tokens the token endpoint minted, each with the refresh token it came from (revoking that one ends them).</summary>
    Private ReadOnly _minted As New Dictionary(Of String, String)(StringComparer.Ordinal)
    ''' <summary>Refresh tokens a test seeded for devices that "signed in" earlier (each device has its own).</summary>
    Private ReadOnly _accepted As New HashSet(Of String)(StringComparer.Ordinal)
    Private _nextId As Integer = 1
    Private _nextToken As Integer = 1
    Private _requests As Integer

    ''' <summary>The server's clock (UTC). Never the test machine's.</summary>
    Public Property ServerNow As DateTimeOffset = New DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)
    Public Property PageSize As Integer = 1000
    Public Property QuotaLimit As Long = 15L * 1024 * 1024 * 1024
    Public Property QuotaUsage As Long = 1024L * 1024
    ''' <summary>True: a bearer that was not minted by the token endpoint or listed in <see cref="AcceptedBearers"/> gets 401.</summary>
    Public Property ValidateBearer As Boolean
    Public ReadOnly AcceptedBearers As New HashSet(Of String)(StringComparer.Ordinal)
    ''' <summary>The scope the code exchange answers with (a test sets a wider one).</summary>
    Public Property GrantedScope As String = "https://www.googleapis.com/auth/drive.appdata"
    Public Property IssueRefreshToken As Boolean = True
    ''' <summary>The last refresh token the exchange minted (a test compares it, never prints it).</summary>
    Public Property LastRefreshToken As String = ""
    ''' <summary>The form body of the last code exchange, for the assertion that no scope and no secret in clear leaked elsewhere.</summary>
    Public Property LastExchangeBody As String = ""
    ''' <summary>When True the refresh endpoint answers invalid_grant (revoked at myaccount.google.com).</summary>
    Public Property RefreshRevoked As Boolean

    ''' <summary>"create:name", "update:name", "delete:name", "download:name", "folder:name", in the order they happened.</summary>
    Public ReadOnly Property Events As IReadOnlyList(Of String)
        Get
            SyncLock _lock
                Return _events.ToList()
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Revoked As IReadOnlyList(Of String)
        Get
            SyncLock _lock
                Return _revoked.ToList()
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property RequestCount As Integer
        Get
            Return Volatile.Read(_requests)
        End Get
    End Property

    Public Sub ClearEvents()
        SyncLock _lock
            _events.Clear()
        End SyncLock
    End Sub

    ' ------------------------------------------------------------------------------------------ the test's own view

    Public Function Snapshot() As List(Of FakeDriveFile)
        SyncLock _lock
            Return _files.ToList()
        End SyncLock
    End Function

    Public Function Find(name As String, Optional parentId As String = Nothing) As FakeDriveFile
        SyncLock _lock
            For Each f As FakeDriveFile In _files
                If f.Name = name AndAlso (parentId Is Nothing OrElse f.Parents.Contains(parentId)) Then Return f
            Next
            Return Nothing
        End SyncLock
    End Function

    Public Function CountNamed(name As String) As Integer
        SyncLock _lock
            Return _files.Where(Function(f) f.Name = name).Count()
        End SyncLock
    End Function

    Public Function Children(parentId As String) As List(Of FakeDriveFile)
        SyncLock _lock
            Return _files.Where(Function(f) f.Parents.Contains(parentId)).ToList()
        End SyncLock
    End Function

    Public Function AddFolder(name As String, Optional parentId As String = RootId) As FakeDriveFile
        SyncLock _lock
            Return AddLocked(name, "application/vnd.google-apps.folder", Array.Empty(Of Byte)(), parentId, Nothing)
        End SyncLock
    End Function

    Public Function AddFile(name As String, content As Byte(), Optional parentId As String = RootId, Optional mimeType As String = "application/octet-stream") As FakeDriveFile
        SyncLock _lock
            Return AddLocked(name, mimeType, content, parentId, Nothing)
        End SyncLock
    End Function

    ''' <summary>Fails the next <paramref name="count"/> requests the predicate accepts with <paramref name="status"/> and a Drive error body carrying <paramref name="reason"/>.</summary>
    Public Sub FailWhen(predicate As Func(Of FakeDriveRequest, Boolean), count As Integer, status As Integer, Optional reason As String = "")
        SyncLock _lock
            _rules.Add(New FailRule() With {.Predicate = predicate, .Remaining = count, .Status = status, .Reason = reason})
        End SyncLock
    End Sub

    Public Sub ClearFailures()
        SyncLock _lock
            _rules.Clear()
        End SyncLock
    End Sub

    ''' <summary>A refresh token a device signed in with earlier: the token endpoint honours it until it is revoked.</summary>
    Public Sub AcceptRefreshToken(token As String)
        SyncLock _lock
            _accepted.Add(token)
        End SyncLock
    End Sub

    Public Sub Advance(span As TimeSpan)
        ServerNow = ServerNow + span
    End Sub

    ' ------------------------------------------------------------------------------------------ dispatch

    Protected Overrides Async Function SendAsync(request As HttpRequestMessage, cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
        Dim seen As New FakeDriveRequest()
        seen.Method = request.Method.Method
        seen.Path = request.RequestUri.AbsolutePath
        For Each pair As String In request.RequestUri.Query.TrimStart("?"c).Split("&"c)
            If pair.Length = 0 Then Continue For
            Dim eq As Integer = pair.IndexOf("="c)
            Dim key As String = Uri.UnescapeDataString(If(eq < 0, pair, pair.Substring(0, eq)))
            seen.Query(key) = If(eq < 0, "", Uri.UnescapeDataString(pair.Substring(eq + 1)))
        Next
        If request.Content IsNot Nothing Then
            seen.Body = Await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(False)
            seen.ContentType = If(request.Content.Headers.ContentType IsNot Nothing, request.Content.Headers.ContentType.ToString(), "")
            seen.ContentRange = request.Content.Headers.ContentRange
        End If
        If request.Headers.Authorization IsNot Nothing Then seen.Bearer = request.Headers.Authorization.Parameter
        Interlocked.Increment(_requests)

        SyncLock _lock
            Dim response As HttpResponseMessage = Route(seen)
            response.Headers.Date = ServerNow
            Return response
        End SyncLock
    End Function

    Private Function Route(r As FakeDriveRequest) As HttpResponseMessage
        If r.Path = "/token" Then Return Token(r)
        If r.Path = "/revoke" Then Return Revoke(r)

        For Each rule As FailRule In _rules
            If rule.Remaining > 0 AndAlso rule.Predicate(r) Then
                rule.Remaining -= 1
                Return Failure(rule.Status, rule.Reason)
            End If
        Next
        If ValidateBearer AndAlso Not (_minted.ContainsKey(r.Bearer) OrElse AcceptedBearers.Contains(r.Bearer)) Then Return Failure(401, "authError")

        Select Case True
            Case r.Path = "/drive/v3/files" AndAlso r.Method = "GET"
                Return ListFiles(r)
            Case r.Path = "/drive/v3/files" AndAlso r.Method = "POST"
                Return CreateFolder(r)
            Case r.Path = "/drive/v3/about" AndAlso r.Method = "GET"
                Return About()
            Case r.Path.StartsWith("/drive/v3/files/", StringComparison.Ordinal) AndAlso r.Method = "GET"
                Return Download(r.Path.Substring("/drive/v3/files/".Length))
            Case r.Path.StartsWith("/drive/v3/files/", StringComparison.Ordinal) AndAlso r.Method = "DELETE"
                Return Delete(r.Path.Substring("/drive/v3/files/".Length))
            Case r.Path = "/upload/drive/v3/files" AndAlso r.Method = "POST"
                Return If(r.Query.GetValueOrDefault("uploadType") = "resumable", StartResumable(r), CreateMultipart(r))
            Case r.Path.StartsWith("/upload/drive/v3/files/", StringComparison.Ordinal) AndAlso r.Method = "PATCH"
                Return Patch(r, r.Path.Substring("/upload/drive/v3/files/".Length))
            Case r.Path.StartsWith("/upload/resumable/", StringComparison.Ordinal) AndAlso r.Method = "PUT"
                Return ResumeUpload(r, r.Path.Substring("/upload/resumable/".Length))
        End Select
        Return New HttpResponseMessage(HttpStatusCode.NotFound)
    End Function

    ' ------------------------------------------------------------------------------------------ the endpoints

    Private Shared Function Failure(status As Integer, reason As String) As HttpResponseMessage
        Dim body As String = "{""error"":{""code"":" & status.ToString(Globalization.CultureInfo.InvariantCulture) & ",""errors"":[{""reason"":""" & reason & """}]}}"
        Return New HttpResponseMessage(CType(status, HttpStatusCode)) With {.Content = New StringContent(body, Encoding.UTF8, "application/json")}
    End Function

    Private Shared Function Json(node As JsonNode) As HttpResponseMessage
        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")}
    End Function

    Private Function Stamp(value As DateTimeOffset) As String
        Return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Globalization.CultureInfo.InvariantCulture)
    End Function

    Private Function FileJson(f As FakeDriveFile) As JsonObject
        Dim obj As New JsonObject()
        obj("id") = f.Id
        obj("name") = f.Name
        obj("mimeType") = f.MimeType
        If Not f.IsFolder Then obj("size") = f.Content.Length.ToString(Globalization.CultureInfo.InvariantCulture)
        obj("createdTime") = Stamp(f.CreatedTime)
        obj("modifiedTime") = Stamp(f.ModifiedTime)
        Dim parents As New JsonArray()
        For Each p As String In f.Parents
            parents.Add(JsonValue.Create(p))
        Next
        obj("parents") = parents
        If f.AppProperties.Count > 0 Then
            Dim props As New JsonObject()
            For Each pair As KeyValuePair(Of String, String) In f.AppProperties
                props(pair.Key) = pair.Value
            Next
            obj("appProperties") = props
        End If
        Return obj
    End Function

    Private Function AddLocked(name As String, mimeType As String, content As Byte(), parentId As String, appProperties As Dictionary(Of String, String)) As FakeDriveFile
        Dim f As New FakeDriveFile()
        f.Id = "f" & _nextId.ToString(Globalization.CultureInfo.InvariantCulture)
        _nextId += 1
        f.Name = name
        f.MimeType = mimeType
        f.Content = content
        f.Parents.Add(If(parentId = "appDataFolder", RootId, parentId))
        f.CreatedTime = ServerNow
        f.ModifiedTime = ServerNow
        If appProperties IsNot Nothing Then
            For Each pair As KeyValuePair(Of String, String) In appProperties
                f.AppProperties(pair.Key) = pair.Value
            Next
        End If
        _files.Add(f)
        Return f
    End Function

    Private Function ListFiles(r As FakeDriveRequest) As HttpResponseMessage
        Dim offset As Integer = 0
        Dim token As String = r.Query.GetValueOrDefault("pageToken", "")
        If token.Length > 0 Then Integer.TryParse(token, offset)
        Dim size As Integer = Math.Max(1, Math.Min(PageSize, 1000))
        Dim page As List(Of FakeDriveFile) = _files.Skip(offset).Take(size).ToList()
        Dim root As New JsonObject()
        Dim array As New JsonArray()
        For Each f As FakeDriveFile In page
            array.Add(FileJson(f))
        Next
        root("files") = array
        If offset + size < _files.Count Then root("nextPageToken") = (offset + size).ToString(Globalization.CultureInfo.InvariantCulture)
        Return Json(root)
    End Function

    Private Function About() As HttpResponseMessage
        Dim quota As New JsonObject()
        If QuotaLimit > 0 Then quota("limit") = QuotaLimit.ToString(Globalization.CultureInfo.InvariantCulture)
        quota("usage") = QuotaUsage.ToString(Globalization.CultureInfo.InvariantCulture)
        Dim root As New JsonObject()
        root("storageQuota") = quota
        Return Json(root)
    End Function

    Private Function CreateFolder(r As FakeDriveRequest) As HttpResponseMessage
        Dim meta As JsonObject = CType(JsonNode.Parse(r.Body), JsonObject)
        Dim parent As String = CStr(meta("parents")(0))
        Dim f As FakeDriveFile = AddLocked(CStr(meta("name")), CStr(meta("mimeType")), Array.Empty(Of Byte)(), parent, Nothing)
        _events.Add("folder:" & f.Name)
        Return Json(FileJson(f))
    End Function

    Private Function CreateMultipart(r As FakeDriveRequest) As HttpResponseMessage
        Dim metadataJson As String = ""
        Dim media As Byte() = Nothing
        Dim mediaType As String = ""
        If Not ParseMultipart(r.Body, r.ContentType, metadataJson, media, mediaType) Then Return New HttpResponseMessage(HttpStatusCode.BadRequest)
        Dim meta As JsonObject = CType(JsonNode.Parse(metadataJson), JsonObject)
        Dim props As Dictionary(Of String, String) = Nothing
        Dim propsNode As JsonNode = meta("appProperties")
        If propsNode IsNot Nothing Then
            props = New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each pair As KeyValuePair(Of String, JsonNode) In CType(propsNode, JsonObject)
                props(pair.Key) = CStr(pair.Value)
            Next
        End If
        Dim f As FakeDriveFile = AddLocked(CStr(meta("name")), mediaType, media, CStr(meta("parents")(0)), props)
        _events.Add("create:" & f.Name)
        Return Json(FileJson(f))
    End Function

    Private Shared Function ParseMultipart(body As Byte(), contentType As String, ByRef metadataJson As String, ByRef media As Byte(), ByRef mediaType As String) As Boolean
        Dim at As Integer = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase)
        If at < 0 Then Return False
        Dim boundary As String = contentType.Substring(at + "boundary=".Length).Trim().Trim(""""c)
        Dim semicolon As Integer = boundary.IndexOf(";"c)
        If semicolon >= 0 Then boundary = boundary.Substring(0, semicolon).Trim().Trim(""""c)
        Dim text As String = Encoding.Latin1.GetString(body)
        Dim delimiter As String = "--" & boundary
        Dim parts As New List(Of String)
        Dim position As Integer = text.IndexOf(delimiter, StringComparison.Ordinal)
        While position >= 0
            Dim start As Integer = position + delimiter.Length
            If start + 2 > text.Length OrElse text.Substring(start, 2) = "--" Then Exit While
            start += 2
            Dim nextAt As Integer = text.IndexOf(delimiter, start, StringComparison.Ordinal)
            If nextAt < 0 Then Exit While
            parts.Add(text.Substring(start, nextAt - 2 - start))
            position = nextAt
        End While
        If parts.Count <> 2 Then Return False
        Dim headerEnd0 As Integer = parts(0).IndexOf(vbCrLf & vbCrLf, StringComparison.Ordinal)
        Dim headerEnd1 As Integer = parts(1).IndexOf(vbCrLf & vbCrLf, StringComparison.Ordinal)
        If headerEnd0 < 0 OrElse headerEnd1 < 0 Then Return False
        metadataJson = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(parts(0).Substring(headerEnd0 + 4)))
        media = Encoding.Latin1.GetBytes(parts(1).Substring(headerEnd1 + 4))
        mediaType = "application/octet-stream"
        For Each line As String In parts(1).Substring(0, headerEnd1).Split(New String() {vbCrLf}, StringSplitOptions.RemoveEmptyEntries)
            If line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase) Then mediaType = line.Substring("Content-Type:".Length).Trim()
        Next
        Return True
    End Function

    Private Function Patch(r As FakeDriveRequest, id As String) As HttpResponseMessage
        For Each f As FakeDriveFile In _files
            If f.Id = id Then
                f.Content = r.Body
                f.ModifiedTime = ServerNow
                _events.Add("update:" & f.Name)
                Return Json(FileJson(f))
            End If
        Next
        Return Failure(404, "notFound")
    End Function

    Private Function Download(id As String) As HttpResponseMessage
        For Each f As FakeDriveFile In _files
            If f.Id = id Then
                _events.Add("download:" & f.Name)
                Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New ByteArrayContent(f.Content)}
            End If
        Next
        Return Failure(404, "notFound")
    End Function

    Private Function Delete(id As String) As HttpResponseMessage
        Dim target As FakeDriveFile = _files.FirstOrDefault(Function(f) f.Id = id)
        If target Is Nothing Then Return Failure(404, "notFound")
        RemoveTree(target)
        Return New HttpResponseMessage(HttpStatusCode.NoContent)
    End Function

    Private Sub RemoveTree(target As FakeDriveFile)
        For Each child As FakeDriveFile In _files.Where(Function(f) f.Parents.Contains(target.Id)).ToList()
            RemoveTree(child)
        Next
        _files.Remove(target)
        _events.Add("delete:" & target.Name)
    End Sub

    Private Function StartResumable(r As FakeDriveRequest) As HttpResponseMessage
        Dim meta As JsonObject = CType(JsonNode.Parse(r.Body), JsonObject)
        Dim session As New UploadSession()
        session.Name = CStr(meta("name"))
        session.Parents.Add(CStr(meta("parents")(0)))
        session.MimeType = ""
        Dim id As String = "s" & _nextId.ToString(Globalization.CultureInfo.InvariantCulture)
        _nextId += 1
        _sessions(id) = session
        Dim response As New HttpResponseMessage(HttpStatusCode.OK)
        response.Headers.Location = New Uri("https://www.googleapis.com/upload/resumable/" & id)
        response.Content = New StringContent("")
        Return response
    End Function

    Private Function ResumeUpload(r As FakeDriveRequest, sessionId As String) As HttpResponseMessage
        Dim session As UploadSession = Nothing
        If Not _sessions.TryGetValue(sessionId, session) Then Return Failure(404, "notFound")
        Dim range As System.Net.Http.Headers.ContentRangeHeaderValue = r.ContentRange
        If range IsNot Nothing AndAlso range.Length.HasValue Then session.Total = range.Length.Value

        If r.Body.Length > 0 Then
            If range Is Nothing OrElse Not range.From.HasValue OrElse range.From.Value <> session.Received.Length Then Return Incomplete(session)
            session.Received.Write(r.Body, 0, r.Body.Length)
            session.MimeType = If(r.ContentType.Length > 0, r.ContentType, "application/octet-stream")
        End If
        If session.Total > 0 AndAlso session.Received.Length >= session.Total Then
            Dim f As FakeDriveFile = AddLocked(session.Name, session.MimeType, session.Received.ToArray(), session.Parents(0), Nothing)
            _sessions.Remove(sessionId)
            _events.Add("create:" & f.Name)
            Return Json(FileJson(f))
        End If
        Return Incomplete(session)
    End Function

    Private Shared Function Incomplete(session As UploadSession) As HttpResponseMessage
        Dim response As New HttpResponseMessage(CType(308, HttpStatusCode))
        If session.Received.Length > 0 Then response.Headers.TryAddWithoutValidation("Range", "bytes=0-" & (session.Received.Length - 1).ToString(Globalization.CultureInfo.InvariantCulture))
        response.Content = New StringContent("")
        Return response
    End Function

    ' ------------------------------------------------------------------------------------------ Google's token endpoints

    Private Shared Function FormValue(body As String, key As String) As String
        For Each pair As String In body.Split("&"c)
            Dim eq As Integer = pair.IndexOf("="c)
            If eq > 0 AndAlso Uri.UnescapeDataString(pair.Substring(0, eq)) = key Then Return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace("+", " "))
        Next
        Return ""
    End Function

    Private Function Token(r As FakeDriveRequest) As HttpResponseMessage
        Dim body As String = Encoding.UTF8.GetString(r.Body)
        Dim grant As String = FormValue(body, "grant_type")
        Dim answer As New JsonObject()
        If grant = "authorization_code" Then
            LastExchangeBody = body
            Dim access As String = "access-" & _nextToken.ToString(Globalization.CultureInfo.InvariantCulture)
            _nextToken += 1
            answer("access_token") = access
            answer("expires_in") = 3600
            answer("scope") = GrantedScope
            If IssueRefreshToken Then
                LastRefreshToken = "refresh-" & Guid.NewGuid().ToString("N")
                answer("refresh_token") = LastRefreshToken
                _accepted.Add(LastRefreshToken)
            End If
            _minted(access) = LastRefreshToken
            Return Json(answer)
        End If
        If grant = "refresh_token" Then
            Dim offered As String = FormValue(body, "refresh_token")
            If RefreshRevoked OrElse _revoked.Contains(offered) OrElse Not (offered = LastRefreshToken OrElse _accepted.Contains(offered)) Then
                answer("error") = "invalid_grant"
                Return New HttpResponseMessage(HttpStatusCode.BadRequest) With {.Content = New StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json")}
            End If
            Dim access As String = "access-" & _nextToken.ToString(Globalization.CultureInfo.InvariantCulture)
            _nextToken += 1
            _minted(access) = offered
            answer("access_token") = access
            answer("expires_in") = 3600
            answer("scope") = GrantedScope
            Return Json(answer)
        End If
        Return New HttpResponseMessage(HttpStatusCode.BadRequest)
    End Function

    Private Function Revoke(r As FakeDriveRequest) As HttpResponseMessage
        Dim token As String = FormValue(Encoding.UTF8.GetString(r.Body), "token")
        _revoked.Add(token)
        ' Google: revoking a refresh token takes the access tokens minted from it down with it - and only those
        For Each access As String In _minted.Where(Function(pair) pair.Value = token).Select(Function(pair) pair.Key).ToList()
            _minted.Remove(access)
        Next
        Return New HttpResponseMessage(HttpStatusCode.OK)
    End Function

End Class

''' <summary>A token source with a fixed token; counts how often the client said it was refused.</summary>
Friend NotInheritable Class FakeDriveTokens
    Implements IExchangeDriveTokens

    Public Property Token As String = "tok"
    Public Property NextToken As String = "tok2"
    Public InvalidateCount As Integer

    Public Function GetAccessTokenAsync(cancellation As CancellationToken) As Task(Of String) Implements IExchangeDriveTokens.GetAccessTokenAsync
        Return Task.FromResult(Token)
    End Function

    Public Sub Invalidate() Implements IExchangeDriveTokens.Invalidate
        InvalidateCount += 1
        If NextToken IsNot Nothing Then Token = NextToken
    End Sub
End Class
#End If
