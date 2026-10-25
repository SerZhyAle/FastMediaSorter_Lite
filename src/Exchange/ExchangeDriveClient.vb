#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading

' Specification 203 sections 3.2 and 3.3 - the Drive REST calls of the channel, and nothing else: every call lives in the
' appDataFolder space (contract item CM; the scope cannot reach anything wider), every request carries a bearer token
' from an IExchangeDriveTokens, and every response's `Date` header is kept, because item C judges expiry on Drive's
' clock. The client knows nothing of records or packets; ExchangeDriveDirectory and ExchangeDriveQueue do.
'
' What a failure carries: a typed ExchangeDriveFailure and the HTTP status - never the body, never a header, never a
' token. Callers turn the failure into words (CT: "not enough space on your Google Drive", "Google Drive daily upload
' limit reached, try again later", ..); this file never produces user text.
'
' Back-off (item CQ): HTTP 403 with a rate-limit reason, 429 and 5xx are retried with the exponential back-off of
' ExchangeDrivePolling.Backoff - 1 s doubling to 5 min, jittered. A daily-limit refusal and a full Drive are NOT
' retried: they never improve by asking again ("never retries it in a loop", item CS).

''' <summary>Where the client gets its bearer token. The session of a signed-in device implements it.</summary>
Friend Interface IExchangeDriveTokens

    ''' <summary>A valid access token, refreshing it when needed. "" when the device is signed out.</summary>
    Function GetAccessTokenAsync(cancellation As CancellationToken) As Task(Of String)

    ''' <summary>The token last handed out was refused (HTTP 401): forget it so the next call refreshes.</summary>
    Sub Invalidate()
End Interface

''' <summary>The kinds of failure a Drive call reports.</summary>
Friend Enum ExchangeDriveFailure
    None
    ''' <summary>No sign-in, or the refresh token no longer works (revoked, expired): the person signs in again (CT).</summary>
    SignedOut
    ''' <summary>A rate limit or a server error that outlasted the retries.</summary>
    Unavailable
    ''' <summary>The daily upload limit (750 GB): never retried.</summary>
    DailyLimit
    ''' <summary>The person's Drive has no room (item CS).</summary>
    DriveFull
    NotFound
    ''' <summary>Network down, DNS, TLS, a timeout.</summary>
    Offline
    Other
End Enum

''' <summary>A failed Drive call. Carries the kind and the HTTP status only (0 when none).</summary>
Friend NotInheritable Class ExchangeDriveException
    Inherits IOException

    Public ReadOnly Failure As ExchangeDriveFailure
    Public ReadOnly StatusCode As Integer

    Public Sub New(failure As ExchangeDriveFailure, statusCode As Integer)
        MyBase.New("drive: " & failure.ToString() & If(statusCode > 0, " (HTTP " & statusCode.ToString(Globalization.CultureInfo.InvariantCulture) & ")", ""))
        Me.Failure = failure
        Me.StatusCode = statusCode
    End Sub
End Class

''' <summary>One file or folder of the appDataFolder as `files.list` reports it.</summary>
Friend NotInheritable Class ExchangeDriveFile

    Public ReadOnly Id As String
    Public ReadOnly Name As String
    Public ReadOnly MimeType As String
    Public ReadOnly Size As Long
    ''' <summary>RFC 3339, Drive's clock.</summary>
    Public ReadOnly CreatedTime As String
    ''' <summary>RFC 3339, Drive's clock: the instant item C measures a record's age from.</summary>
    Public ReadOnly ModifiedTime As String
    Public ReadOnly Parents As IReadOnlyList(Of String)
    Public ReadOnly AppProperties As IReadOnlyDictionary(Of String, String)

    Public Sub New(id As String, name As String, mimeType As String, size As Long, createdTime As String, modifiedTime As String,
                   parents As IReadOnlyList(Of String), appProperties As IReadOnlyDictionary(Of String, String))
        Me.Id = id
        Me.Name = name
        Me.MimeType = mimeType
        Me.Size = size
        Me.CreatedTime = createdTime
        Me.ModifiedTime = modifiedTime
        Me.Parents = parents
        Me.AppProperties = appProperties
    End Sub

    Public ReadOnly Property IsFolder As Boolean
        Get
            Return String.Equals(MimeType, ExchangeDrivePackets.FolderMimeType, StringComparison.Ordinal)
        End Get
    End Property

    Public Function HasParent(parentId As String) As Boolean
        For Each p As String In Parents
            If String.Equals(p, parentId, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    Public Function AppProperty(key As String) As String
        Dim value As String = Nothing
        Return If(AppProperties.TryGetValue(key, value), value, "")
    End Function

    Public Overrides Function ToString() As String
        Return "ExchangeDriveFile"
    End Function
End Class

''' <summary>Everything in the appDataFolder at one instant, with Drive's own clock from the response that listed it.</summary>
Friend NotInheritable Class ExchangeDriveListing

    Public ReadOnly Files As IReadOnlyList(Of ExchangeDriveFile)
    ''' <summary>The `Date` header of Drive's response; the "now" of item C. <see cref="HasServerDate"/> False means Drive sent none.</summary>
    Public ReadOnly ServerDate As DateTimeOffset
    Public ReadOnly HasServerDate As Boolean

    Public Sub New(files As IReadOnlyList(Of ExchangeDriveFile), serverDate As DateTimeOffset, hasServerDate As Boolean)
        Me.Files = files
        Me.ServerDate = serverDate
        Me.HasServerDate = hasServerDate
    End Sub
End Class

''' <summary>What `about.storageQuota` says: the limit (0 for an unlimited plan) and the usage, both in bytes.</summary>
Friend NotInheritable Class ExchangeDriveQuota

    Public ReadOnly LimitBytes As Long
    Public ReadOnly UsageBytes As Long

    Public Sub New(limitBytes As Long, usageBytes As Long)
        Me.LimitBytes = limitBytes
        Me.UsageBytes = usageBytes
    End Sub
End Class

''' <summary>Sorting an HTTP refusal into a kind, by its status and Drive's own reason string (item CQ and CS).</summary>
Friend NotInheritable Class ExchangeDriveFailures

    Private Sub New()
    End Sub

    ''' <summary>The first `error.errors[].reason` of a Drive error body, or "" (a body that is no JSON, or has no reason).</summary>
    Public Shared Function ReasonOf(body As String) As String
        If String.IsNullOrEmpty(body) Then Return ""
        Try
            Using document As JsonDocument = JsonDocument.Parse(body)
                Dim errorEl As JsonElement = Nothing
                If Not document.RootElement.TryGetProperty("error", errorEl) OrElse errorEl.ValueKind <> JsonValueKind.Object Then Return ""
                Dim list As JsonElement = Nothing
                If errorEl.TryGetProperty("errors", list) AndAlso list.ValueKind = JsonValueKind.Array Then
                    For Each item As JsonElement In list.EnumerateArray()
                        Dim reason As JsonElement = Nothing
                        If item.ValueKind = JsonValueKind.Object AndAlso item.TryGetProperty("reason", reason) AndAlso reason.ValueKind = JsonValueKind.String Then Return If(reason.GetString(), "")
                    Next
                End If
                Dim statusEl As JsonElement = Nothing
                If errorEl.TryGetProperty("status", statusEl) AndAlso statusEl.ValueKind = JsonValueKind.String Then Return If(statusEl.GetString(), "")
            End Using
        Catch ex As JsonException
        End Try
        Return ""
    End Function

    ''' <summary>True for a refusal worth another try after a back-off: 429, 5xx, and 403 with a rate-limit reason.</summary>
    Public Shared Function IsRetryable(statusCode As Integer, reason As String) As Boolean
        If statusCode = 429 OrElse (statusCode >= 500 AndAlso statusCode <= 599) Then Return True
        If statusCode = 403 Then
            Select Case reason
                Case "rateLimitExceeded", "userRateLimitExceeded", "sharingRateLimitExceeded", "RATE_LIMIT_EXCEEDED"
                    Return True
            End Select
        End If
        Return False
    End Function

    ''' <summary>The failure an HTTP refusal stands for once retrying is over or pointless.</summary>
    Public Shared Function Classify(statusCode As Integer, reason As String) As ExchangeDriveFailure
        If statusCode = 401 Then Return ExchangeDriveFailure.SignedOut
        If statusCode = 404 Then Return ExchangeDriveFailure.NotFound
        If statusCode = 403 Then
            Select Case reason
                Case "storageQuotaExceeded"
                    Return ExchangeDriveFailure.DriveFull
                Case "dailyLimitExceeded", "dailyLimitExceededUnreg", "uploadLimitExceeded"
                    Return ExchangeDriveFailure.DailyLimit
            End Select
        End If
        If IsRetryable(statusCode, reason) Then Return ExchangeDriveFailure.Unavailable
        Return ExchangeDriveFailure.Other
    End Function
End Class

''' <summary>The Drive v3 calls of the channel.</summary>
Friend NotInheritable Class ExchangeDriveClient

    Public Const FilesEndpoint As String = "https://www.googleapis.com/drive/v3/files"
    Public Const UploadEndpoint As String = "https://www.googleapis.com/upload/drive/v3/files"
    Public Const AboutEndpoint As String = "https://www.googleapis.com/drive/v3/about"
    ''' <summary>The one parent the channel ever names (item CM).</summary>
    Public Const AppDataFolder As String = "appDataFolder"

    Private Const FileFields As String = "id,name,mimeType,size,createdTime,modifiedTime,parents,appProperties"
    Private Const ChunkBytes As Integer = 8 * 1024 * 1024 ' a multiple of 256 KiB, as resumable uploads require
    Private Const MaxAttempts As Integer = 6

    Private ReadOnly _http As HttpClient
    Private ReadOnly _tokens As IExchangeDriveTokens
    Private ReadOnly _delay As Func(Of TimeSpan, CancellationToken, Task)
    Private ReadOnly _random As Func(Of Double)

    Private _lastServerDate As DateTimeOffset
    Private _hasServerDate As Boolean

    ''' <param name="http">Not owned: the caller disposes it.</param>
    ''' <param name="delay">The wait between retries; a test passes one that returns at once.</param>
    ''' <param name="random">The jitter source in [0, 1); a test passes a constant.</param>
    Public Sub New(http As HttpClient, tokens As IExchangeDriveTokens, Optional delay As Func(Of TimeSpan, CancellationToken, Task) = Nothing, Optional random As Func(Of Double) = Nothing)
        If http Is Nothing Then Throw New ArgumentNullException(NameOf(http))
        If tokens Is Nothing Then Throw New ArgumentNullException(NameOf(tokens))
        _http = http
        _tokens = tokens
        _delay = If(delay, Function(span As TimeSpan, token As CancellationToken) Task.Delay(span, token))
        Dim generator As New Random()
        _random = If(random, Function() generator.NextDouble())
    End Sub

    ''' <summary>A client for the product: one HttpClient with a long timeout (a chunk of 8 MiB on a slow line).</summary>
    Public Shared Function CreateHttpClient() As HttpClient
        Dim client As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(10)}
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FastMediaSorter-Windows-DriveChannel")
        Return client
    End Function

    ''' <summary>Drive's `Date` header of the last response, or Nothing before any. Item C's "now".</summary>
    Public ReadOnly Property LastServerDate As DateTimeOffset?
        Get
            Return If(_hasServerDate, CType(_lastServerDate, DateTimeOffset?), Nothing)
        End Get
    End Property

    ' ------------------------------------------------------------------------------------------ the one send

    ''' <summary>Sends a request built by <paramref name="build"/> (called again for each attempt - a request cannot be
    ''' sent twice), with the bearer token, one refresh on 401 and the back-off of item CQ. Returns the successful
    ''' response, which the caller disposes; throws <see cref="ExchangeDriveException"/> otherwise.</summary>
    Private Async Function SendAsync(build As Func(Of HttpRequestMessage), completion As HttpCompletionOption, cancellation As CancellationToken) As Task(Of HttpResponseMessage)
        Dim backoff As New ExchangeDrivePolling.Backoff()
        Dim refreshed As Boolean = False
        For attempt As Integer = 1 To MaxAttempts
            cancellation.ThrowIfCancellationRequested()
            Dim token As String = Await _tokens.GetAccessTokenAsync(cancellation).ConfigureAwait(False)
            If String.IsNullOrEmpty(token) Then Throw New ExchangeDriveException(ExchangeDriveFailure.SignedOut, 0)

            Dim response As HttpResponseMessage = Nothing
            Dim unreachable As Boolean = False
            Using request As HttpRequestMessage = build()
                request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
                Try
                    response = Await _http.SendAsync(request, completion, cancellation).ConfigureAwait(False)
                Catch ex As HttpRequestException
                    unreachable = True
                Catch ex As TaskCanceledException When Not cancellation.IsCancellationRequested
                    unreachable = True
                End Try
            End Using
            If unreachable Then
                ' No network, DNS, TLS or a timeout: the same back-off as a server error, then Offline.
                If attempt = MaxAttempts Then Throw New ExchangeDriveException(ExchangeDriveFailure.Offline, 0)
                Await _delay(backoff.NextDelay(_random()), cancellation).ConfigureAwait(False)
                Continue For
            End If

            Dim headerDate As DateTimeOffset? = response.Headers.Date
            If headerDate.HasValue Then
                _lastServerDate = headerDate.Value
                _hasServerDate = True
            End If
            If response.IsSuccessStatusCode Then Return response

            Dim status As Integer = CInt(response.StatusCode)
            Dim body As String = ""
            Try
                body = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
            Catch ex As HttpRequestException
            End Try
            response.Dispose()
            Dim reason As String = ExchangeDriveFailures.ReasonOf(body)

            If status = 401 Then
                ' One refresh, then the person signs in again (CT): a revoked token is not retried in a loop.
                _tokens.Invalidate()
                If refreshed Then Throw New ExchangeDriveException(ExchangeDriveFailure.SignedOut, status)
                refreshed = True
                Continue For
            End If
            If ExchangeDriveFailures.IsRetryable(status, reason) AndAlso attempt < MaxAttempts Then
                Await _delay(backoff.NextDelay(_random()), cancellation).ConfigureAwait(False)
                Continue For
            End If
            Throw New ExchangeDriveException(ExchangeDriveFailures.Classify(status, reason), status)
        Next
        Throw New ExchangeDriveException(ExchangeDriveFailure.Unavailable, 0)
    End Function

    ' ------------------------------------------------------------------------------------------ listing and quota

    ''' <summary>Every file and folder of the appDataFolder, paged, in ONE logical call (item CQ: one listing per interval
    ''' serves the directory and the queue alike). Items without a parent in the listing are root files.</summary>
    Public Async Function ListAllAsync(cancellation As CancellationToken) As Task(Of ExchangeDriveListing)
        Dim files As New List(Of ExchangeDriveFile)
        Dim pageToken As String = ""
        Dim serverDate As DateTimeOffset = Nothing
        Dim hasDate As Boolean = False
        Do
            Dim url As String = FilesEndpoint & "?spaces=" & AppDataFolder & "&pageSize=1000&fields=" & Uri.EscapeDataString("nextPageToken,files(" & FileFields & ")")
            If pageToken.Length > 0 Then url &= "&pageToken=" & Uri.EscapeDataString(pageToken)
            Dim pageUrl As String = url
            Using response As HttpResponseMessage = Await SendAsync(Function() New HttpRequestMessage(HttpMethod.Get, pageUrl), HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
                If Not hasDate AndAlso response.Headers.Date.HasValue Then
                    serverDate = response.Headers.Date.Value
                    hasDate = True
                End If
                Dim json As String = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
                pageToken = ""
                Try
                    Using document As JsonDocument = JsonDocument.Parse(json)
                        Dim token As JsonElement = Nothing
                        If document.RootElement.TryGetProperty("nextPageToken", token) AndAlso token.ValueKind = JsonValueKind.String Then pageToken = If(token.GetString(), "")
                        Dim array As JsonElement = Nothing
                        If document.RootElement.TryGetProperty("files", array) AndAlso array.ValueKind = JsonValueKind.Array Then
                            For Each item As JsonElement In array.EnumerateArray()
                                Dim parsed As ExchangeDriveFile = ParseFile(item)
                                If parsed IsNot Nothing Then files.Add(parsed)
                            Next
                        End If
                    End Using
                Catch ex As JsonException
                    Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)
                End Try
            End Using
        Loop While pageToken.Length > 0
        Return New ExchangeDriveListing(files, serverDate, hasDate)
    End Function

    ''' <summary>`about.storageQuota` (item CS). A plan without a limit reports none: LimitBytes is then 0.</summary>
    Public Async Function GetStorageQuotaAsync(cancellation As CancellationToken) As Task(Of ExchangeDriveQuota)
        Dim url As String = AboutEndpoint & "?fields=" & Uri.EscapeDataString("storageQuota(limit,usage)")
        Using response As HttpResponseMessage = Await SendAsync(Function() New HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            Dim json As String = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
            Try
                Using document As JsonDocument = JsonDocument.Parse(json)
                    Dim quota As JsonElement = Nothing
                    If Not document.RootElement.TryGetProperty("storageQuota", quota) OrElse quota.ValueKind <> JsonValueKind.Object Then Return New ExchangeDriveQuota(0, 0)
                    Return New ExchangeDriveQuota(ReadLongText(quota, "limit"), ReadLongText(quota, "usage"))
                End Using
            Catch ex As JsonException
                Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)
            End Try
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ writing

    ''' <summary>A folder under <paramref name="parentId"/> (<see cref="AppDataFolder"/> for the root).</summary>
    Public Async Function CreateFolderAsync(parentId As String, name As String, cancellation As CancellationToken) As Task(Of ExchangeDriveFile)
        Dim metadata As New JsonObject()
        metadata("name") = name
        metadata("mimeType") = ExchangeDrivePackets.FolderMimeType
        metadata("parents") = New JsonArray(JsonValue.Create(parentId))
        Dim body As String = metadata.ToJsonString()
        Dim url As String = FilesEndpoint & "?fields=" & Uri.EscapeDataString(FileFields)
        Using response As HttpResponseMessage = Await SendAsync(
            Function()
                Dim request As New HttpRequestMessage(HttpMethod.Post, url)
                request.Content = New StringContent(body, Encoding.UTF8, "application/json")
                Return request
            End Function, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            Return Await ReadFileAsync(response, cancellation).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>A new file of up to a few MiB in one multipart call. <paramref name="appProperties"/> (may be Nothing) ride as
    ''' hidden metadata - how a device marks the files it wrote.</summary>
    Public Async Function CreateSmallFileAsync(parentId As String, name As String, mimeType As String, content As Byte(),
                                               appProperties As IReadOnlyDictionary(Of String, String), cancellation As CancellationToken) As Task(Of ExchangeDriveFile)
        Dim metadata As New JsonObject()
        metadata("name") = name
        metadata("parents") = New JsonArray(JsonValue.Create(parentId))
        If appProperties IsNot Nothing AndAlso appProperties.Count > 0 Then
            Dim props As New JsonObject()
            For Each pair As KeyValuePair(Of String, String) In appProperties
                props(pair.Key) = pair.Value
            Next
            metadata("appProperties") = props
        End If
        Dim metadataJson As String = metadata.ToJsonString()
        Dim url As String = UploadEndpoint & "?uploadType=multipart&fields=" & Uri.EscapeDataString(FileFields)
        Dim boundary As String = "fmsx" & Guid.NewGuid().ToString("N")
        Using response As HttpResponseMessage = Await SendAsync(
            Function()
                Dim form As New MultipartContent("related", boundary)
                form.Add(New StringContent(metadataJson, Encoding.UTF8, "application/json"))
                Dim media As New ByteArrayContent(content)
                media.Headers.ContentType = New MediaTypeHeaderValue(If(String.IsNullOrEmpty(mimeType), ExchangeDrivePackets.DefaultMimeType, mimeType))
                form.Add(media)
                Dim request As New HttpRequestMessage(HttpMethod.Post, url)
                request.Content = form
                Return request
            End Function, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            Return Await ReadFileAsync(response, cancellation).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>Replaces the content of a file of ours. Drive moves its `modifiedTime` to now - which is exactly what a
    ''' rewrite of a standing record is for (item C).</summary>
    Public Async Function UpdateSmallFileAsync(fileId As String, mimeType As String, content As Byte(), cancellation As CancellationToken) As Task(Of ExchangeDriveFile)
        Dim url As String = UploadEndpoint & "/" & Uri.EscapeDataString(fileId) & "?uploadType=media&fields=" & Uri.EscapeDataString(FileFields)
        Using response As HttpResponseMessage = Await SendAsync(
            Function()
                Dim request As New HttpRequestMessage(New HttpMethod("PATCH"), url)
                Dim media As New ByteArrayContent(content)
                media.Headers.ContentType = New MediaTypeHeaderValue(If(String.IsNullOrEmpty(mimeType), "application/json", mimeType))
                request.Content = media
                Return request
            End Function, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            Return Await ReadFileAsync(response, cancellation).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>A file above <see cref="ExchangeDrivePackets.ResumableThresholdBytes"/>: Drive's resumable upload, read
    ''' from a SEEKABLE stream in chunks (item CN: never whole in memory), resuming from what Drive confirms after an
    ''' interruption. <paramref name="progress"/> gets the bytes confirmed so far.</summary>
    Public Async Function UploadResumableAsync(parentId As String, name As String, mimeType As String, source As Stream,
                                               cancellation As CancellationToken, Optional progress As Action(Of Long) = Nothing) As Task(Of ExchangeDriveFile)
        If source Is Nothing OrElse Not source.CanSeek Then Throw New ArgumentException("the source must be seekable", NameOf(source))
        Dim total As Long = source.Length
        Dim metadata As New JsonObject()
        metadata("name") = name
        metadata("parents") = New JsonArray(JsonValue.Create(parentId))
        Dim metadataJson As String = metadata.ToJsonString()
        Dim effectiveMime As String = If(String.IsNullOrEmpty(mimeType), ExchangeDrivePackets.DefaultMimeType, mimeType)
        Dim startUrl As String = UploadEndpoint & "?uploadType=resumable&fields=" & Uri.EscapeDataString(FileFields)

        Dim sessionUri As Uri
        Using start As HttpResponseMessage = Await SendAsync(
            Function()
                Dim request As New HttpRequestMessage(HttpMethod.Post, startUrl)
                request.Content = New StringContent(metadataJson, Encoding.UTF8, "application/json")
                request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", effectiveMime)
                request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", total.ToString(Globalization.CultureInfo.InvariantCulture))
                Return request
            End Function, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            sessionUri = start.Headers.Location
        End Using
        If sessionUri Is Nothing Then Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)

        Dim buffer(ChunkBytes - 1) As Byte
        Dim offset As Long = 0
        Dim backoff As New ExchangeDrivePolling.Backoff()
        Dim failures As Integer = 0
        Do
            cancellation.ThrowIfCancellationRequested()
            source.Position = offset
            Dim want As Integer = CInt(Math.Min(CLng(ChunkBytes), total - offset))
            Dim read As Integer = 0
            While read < want
                Dim got As Integer = Await source.ReadAsync(buffer, read, want - read, cancellation).ConfigureAwait(False)
                If got = 0 Then Throw New IOException("the source ended before its length")
                read += got
            End While

            Dim token As String = Await _tokens.GetAccessTokenAsync(cancellation).ConfigureAwait(False)
            If String.IsNullOrEmpty(token) Then Throw New ExchangeDriveException(ExchangeDriveFailure.SignedOut, 0)
            Dim lastByte As Long = offset + read - 1
            Using request As New HttpRequestMessage(HttpMethod.Put, sessionUri)
                request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
                Dim content As New ByteArrayContent(buffer, 0, read)
                content.Headers.ContentType = New MediaTypeHeaderValue(effectiveMime)
                content.Headers.ContentRange = If(read = 0, New ContentRangeHeaderValue(total), New ContentRangeHeaderValue(offset, lastByte, total))
                request.Content = content
                Dim response As HttpResponseMessage = Nothing
                Try
                    response = Await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
                Catch ex As HttpRequestException
                    response = Nothing
                Catch ex As TaskCanceledException When Not cancellation.IsCancellationRequested
                    response = Nothing
                End Try

                If response IsNot Nothing Then
                    Using response
                        Dim headerDate As DateTimeOffset? = response.Headers.Date
                        If headerDate.HasValue Then
                            _lastServerDate = headerDate.Value
                            _hasServerDate = True
                        End If
                        If response.IsSuccessStatusCode Then
                            If progress IsNot Nothing Then progress(total)
                            Return Await ReadFileAsync(response, cancellation).ConfigureAwait(False)
                        End If
                        If CInt(response.StatusCode) = 308 Then
                            offset = ConfirmedBytes(response, offset + read)
                            failures = 0
                            backoff.Reset()
                            If progress IsNot Nothing Then progress(offset)
                            Continue Do
                        End If
                        Dim status As Integer = CInt(response.StatusCode)
                        Dim reason As String = ExchangeDriveFailures.ReasonOf(Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False))
                        If Not ExchangeDriveFailures.IsRetryable(status, reason) Then
                            Throw New ExchangeDriveException(ExchangeDriveFailures.Classify(status, reason), status)
                        End If
                    End Using
                End If
            End Using

            ' The chunk did not land (network, rate limit, 5xx): wait, ask Drive how much it has, go on from there.
            failures += 1
            If failures >= MaxAttempts Then Throw New ExchangeDriveException(ExchangeDriveFailure.Unavailable, 0)
            Await _delay(backoff.NextDelay(_random()), cancellation).ConfigureAwait(False)
            offset = Await QueryUploadedBytesAsync(sessionUri, total, offset, cancellation).ConfigureAwait(False)
        Loop
    End Function

    ''' <summary>The `Range: bytes=0-N` of a 308 as the next offset; <paramref name="fallback"/> when Drive names none.</summary>
    Private Shared Function ConfirmedBytes(response As HttpResponseMessage, fallback As Long) As Long
        Dim values As IEnumerable(Of String) = Nothing
        If response.Headers.TryGetValues("Range", values) Then
            For Each value As String In values
                Dim dash As Integer = value.LastIndexOf("-"c)
                Dim last As Long
                If dash >= 0 AndAlso Long.TryParse(value.Substring(dash + 1), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, last) Then Return last + 1
            Next
            Return fallback
        End If
        ' no Range header: Drive holds nothing yet
        Return 0
    End Function

    Private Async Function QueryUploadedBytesAsync(sessionUri As Uri, total As Long, fallback As Long, cancellation As CancellationToken) As Task(Of Long)
        Try
            Dim token As String = Await _tokens.GetAccessTokenAsync(cancellation).ConfigureAwait(False)
            Using request As New HttpRequestMessage(HttpMethod.Put, sessionUri)
                request.Headers.Authorization = New AuthenticationHeaderValue("Bearer", token)
                Dim empty As New ByteArrayContent(Array.Empty(Of Byte)())
                empty.Headers.ContentRange = New ContentRangeHeaderValue(total)
                request.Content = empty
                Using response As HttpResponseMessage = Await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(False)
                    If CInt(response.StatusCode) = 308 Then Return ConfirmedBytes(response, fallback)
                End Using
            End Using
        Catch ex As HttpRequestException
        Catch ex As TaskCanceledException When Not cancellation.IsCancellationRequested
        End Try
        Return fallback
    End Function

    ' ------------------------------------------------------------------------------------------ reading and deleting

    ''' <summary>Streams a file's content to <paramref name="destination"/>. The caller discards the destination on a throw -
    ''' no partial file is ever placed (item CN).</summary>
    Public Async Function DownloadAsync(fileId As String, destination As Stream, cancellation As CancellationToken, Optional progress As Action(Of Long) = Nothing) As Task
        Dim url As String = FilesEndpoint & "/" & Uri.EscapeDataString(fileId) & "?alt=media"
        Using response As HttpResponseMessage = Await SendAsync(Function() New HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(False)
            Using source As Stream = Await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(False)
                Dim buffer(81919) As Byte
                Dim copied As Long = 0
                Try
                    Do
                        Dim got As Integer = Await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(False)
                        If got = 0 Then Exit Do
                        Await destination.WriteAsync(buffer, 0, got, cancellation).ConfigureAwait(False)
                        copied += got
                        If progress IsNot Nothing Then progress(copied)
                    Loop
                Catch ex As HttpRequestException
                    Throw New ExchangeDriveException(ExchangeDriveFailure.Offline, 0)
                Catch ex As IOException When Not TypeOf ex Is ExchangeDriveException AndAlso TypeOf ex.InnerException Is HttpRequestException
                    Throw New ExchangeDriveException(ExchangeDriveFailure.Offline, 0)
                End Try
            End Using
        End Using
    End Function

    ''' <summary>A small file's bytes, for a record or a manifest. Refuses more than <paramref name="maxBytes"/>.</summary>
    Public Async Function DownloadBytesAsync(fileId As String, maxBytes As Integer, cancellation As CancellationToken) As Task(Of Byte())
        Using memory As New MemoryStream()
            Using limited As New LimitedWriteStream(memory, maxBytes)
                Await DownloadAsync(fileId, limited, cancellation).ConfigureAwait(False)
            End Using
            Return memory.ToArray()
        End Using
    End Function

    ''' <summary>One permanent delete (`appDataFolder` has no trash, item CR). A file that is already gone is a success.</summary>
    Public Async Function DeleteAsync(fileId As String, cancellation As CancellationToken) As Task
        Dim url As String = FilesEndpoint & "/" & Uri.EscapeDataString(fileId)
        Try
            Using response As HttpResponseMessage = Await SendAsync(Function() New HttpRequestMessage(HttpMethod.Delete, url), HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(False)
            End Using
        Catch ex As ExchangeDriveException When ex.Failure = ExchangeDriveFailure.NotFound
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------ parsing

    Private Shared Async Function ReadFileAsync(response As HttpResponseMessage, cancellation As CancellationToken) As Task(Of ExchangeDriveFile)
        Dim json As String = Await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(False)
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                Dim parsed As ExchangeDriveFile = ParseFile(document.RootElement)
                If parsed Is Nothing Then Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)
                Return parsed
            End Using
        Catch ex As JsonException
            Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)
        End Try
    End Function

    Private Shared Function ParseFile(item As JsonElement) As ExchangeDriveFile
        If item.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim id As String = ReadText(item, "id")
        If id.Length = 0 Then Return Nothing
        Dim parents As New List(Of String)
        Dim parentsEl As JsonElement = Nothing
        If item.TryGetProperty("parents", parentsEl) AndAlso parentsEl.ValueKind = JsonValueKind.Array Then
            For Each p As JsonElement In parentsEl.EnumerateArray()
                If p.ValueKind = JsonValueKind.String Then parents.Add(p.GetString())
            Next
        End If
        Dim props As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim propsEl As JsonElement = Nothing
        If item.TryGetProperty("appProperties", propsEl) AndAlso propsEl.ValueKind = JsonValueKind.Object Then
            For Each prop As JsonProperty In propsEl.EnumerateObject()
                If prop.Value.ValueKind = JsonValueKind.String Then props(prop.Name) = If(prop.Value.GetString(), "")
            Next
        End If
        Return New ExchangeDriveFile(id, ReadText(item, "name"), ReadText(item, "mimeType"), ReadLongText(item, "size"),
                                     ReadText(item, "createdTime"), ReadText(item, "modifiedTime"), parents, props)
    End Function

    Private Shared Function ReadText(element As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If element.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    ''' <summary>Drive reports sizes and quotas as decimal strings ("12345"), not numbers.</summary>
    Private Shared Function ReadLongText(element As JsonElement, name As String) As Long
        Dim el As JsonElement = Nothing
        If Not element.TryGetProperty(name, el) Then Return 0
        Dim value As Long = 0
        If el.ValueKind = JsonValueKind.String Then
            Return If(Long.TryParse(el.GetString(), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, value), value, 0L)
        End If
        If el.ValueKind = JsonValueKind.Number AndAlso el.TryGetInt64(value) Then Return value
        Return 0
    End Function

    ''' <summary>A write-through stream that refuses to take more than a bound: a "small" file that is not small is no record.</summary>
    Private NotInheritable Class LimitedWriteStream
        Inherits Stream

        Private ReadOnly _inner As Stream
        Private ReadOnly _limit As Long
        Private _written As Long

        Public Sub New(inner As Stream, limit As Long)
            _inner = inner
            _limit = limit
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean = False
        Public Overrides ReadOnly Property CanSeek As Boolean = False
        Public Overrides ReadOnly Property CanWrite As Boolean = True

        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            _written += count
            If _written > _limit Then Throw New ExchangeDriveException(ExchangeDriveFailure.Other, 200)
            _inner.Write(buffer, offset, count)
        End Sub

        Public Overrides Sub Flush()
            _inner.Flush()
        End Sub

        Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub
    End Class

End Class
#End If
