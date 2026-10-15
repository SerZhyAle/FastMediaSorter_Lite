#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class LiveReceiverTests
    <Theory>
    <InlineData("SHA-256")>
    <InlineData("MD5")>
    Public Async Function Producer_Password_Answers_Digest_Without_Sending_Basic_Credentials(algorithm As String) As Task
        Dim valid As Boolean
        Using device As New LiveMockDevice(Function(connection, token)
                                              Dim challenge As String = "WWW-Authenticate: Digest realm=""broadcast"", nonce=""fixed-test-nonce"", algorithm=" & algorithm & ", qop=""auth"""
                                              Dim match As Match = Regex.Match(connection.RequestHead, "Authorization: Digest ([^\r\n]+)", RegexOptions.IgnoreCase)
                                              If match.Success Then
                                                  Dim fields As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                                                  For Each part As Match In Regex.Matches(match.Groups(1).Value, "(\w+)=(?:""([^""]*)""|([^,\s]+))")
                                                      fields(part.Groups(1).Value) = If(part.Groups(2).Success, part.Groups(2).Value, part.Groups(3).Value)
                                                  Next
                                                  Dim hash As Func(Of String, String) = Function(value)
                                                                                           Dim bytes As Byte() = Encoding.UTF8.GetBytes(value)
                                                                                           Return Convert.ToHexString(If(algorithm = "MD5", MD5.HashData(bytes), SHA256.HashData(bytes))).ToLowerInvariant()
                                                                                       End Function
                                                  Dim expected As String = hash(hash("guest:broadcast:secret") & ":fixed-test-nonce:" & fields("nc") & ":" & fields("cnonce") & ":auth:" & hash("GET:" & fields("uri")))
                                                  valid = fields("response") = expected
                                              End If
                                              connection.WriteAscii(If(valid, "HTTP/1.1 200 OK" & vbCrLf & "Content-Type: audio/aac",
                                                  "HTTP/1.1 401 Unauthorized" & vbCrLf & challenge) & vbCrLf & "Content-Length: 0" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
                                              Return Task.CompletedTask
                                          End Function)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = device.Url(), .Transport = "HTTP", .AuthUser = "guest", .Password = "secret"}
            Using session As New LiveHttpPlaybackSession(endpoint), cts As New CancellationTokenSource(TimeSpan.FromSeconds(5))
                Assert.True((Await session.OpenAsync(cts.Token)).IsConnected)
                Assert.True(valid)
                Assert.Equal(2, device.AcceptedCount)
                Assert.DoesNotContain(device.Requests, Function(request) request.Contains("Authorization: Basic", StringComparison.OrdinalIgnoreCase))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Password_Throttling_Is_Fatal_And_Does_Not_Replay_Credentials() As Task
        Using device As New LiveMockDevice(Function(connection, token)
                                              connection.WriteAscii("HTTP/1.1 429 Too Many Requests" & vbCrLf & "Content-Length: 0" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
                                              Return Task.CompletedTask
                                          End Function)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = device.Url(), .Transport = "HTTP", .AuthUser = "guest", .Password = "secret"}
            Using session As New LiveHttpPlaybackSession(endpoint), cts As New CancellationTokenSource(TimeSpan.FromSeconds(5))
                Dim result As LiveOpenResult = Await session.OpenAsync(cts.Token)
                Assert.True(result.IsFatal)
                Assert.Equal(429, result.HttpStatus)
                Assert.Equal(1, device.AcceptedCount)
            End Using
        End Using
    End Function
    <Fact>
    Public Async Function Relay_Password_Answers_One_Basic_Challenge_Inside_Pinned_Tls() As Task
        Dim authorization As String = ""
        Using server As New ExchangeStubTlsServer(
            Async Function(connection)
                Using reader As New StreamReader(connection.Stream, Encoding.ASCII, False, 1024, leaveOpen:=True)
                    While True
                        Dim line As String = Await reader.ReadLineAsync(connection.Token)
                        If String.IsNullOrEmpty(line) Then Exit While
                        If line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase) Then authorization = line
                    End While
                End Using
                Dim head As String
                If authorization.Length = 0 Then
                    head = "HTTP/1.1 401 Unauthorized" & vbCrLf & "WWW-Authenticate: Basic realm=""broadcast""" & vbCrLf & "Content-Length: 0"
                Else
                    head = "HTTP/1.1 200 OK" & vbCrLf & "Content-Type: audio/aac" & vbCrLf & "Content-Length: 0"
                End If
                Await connection.Stream.WriteAsync(Encoding.ASCII.GetBytes(head & vbCrLf & "Connection: close" & vbCrLf & vbCrLf), connection.Token)
                Await connection.Stream.FlushAsync(connection.Token)
            End Function)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = "https://" & server.Address & "/v2/b/abc/stream", .Transport = "RELAY",
                .CertFingerprint = server.Fingerprint, .AuthUser = "guest", .Password = "secret", .ModeValue = LiveBroadcastMode.AudioOnly}
            Using session As New LiveHttpPlaybackSession(endpoint, New LivePlaybackOptions With {.ConnectTimeoutMs = 5000}),
                  cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Assert.True((Await session.OpenAsync(cts.Token)).IsConnected)
                Assert.Equal("Authorization: Basic " & Convert.ToBase64String(Encoding.UTF8.GetBytes("guest:secret")), authorization)
                Assert.Equal(2, session.ConnectionCount)
                Assert.DoesNotContain("secret", endpoint.Url)
            End Using
        End Using
    End Function

    Private Shared Async Function RespondWithTs(connection As ExchangeStubConnection) As Task
        Using reader As New StreamReader(connection.Stream, Encoding.ASCII, False, 1024, leaveOpen:=True)
            While Not String.IsNullOrEmpty(Await reader.ReadLineAsync(connection.Token))
            End While
        End Using
        Dim response As Byte() = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK" & vbCrLf & "Content-Type: video/mp2t" & vbCrLf &
            "Content-Length: 1" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf & "G")
        Await connection.Stream.WriteAsync(response, connection.Token)
        Await connection.Stream.FlushAsync(connection.Token)
    End Function

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Async Function Relay_Requires_The_Descriptor_Certificate_On_The_Playback_Connection(matches As Boolean) As Task
        Using server As New ExchangeStubTlsServer(AddressOf RespondWithTs)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = "https://" & server.Address & "/v2/b/abc/stream", .Transport = "RELAY",
                .ModeValue = LiveBroadcastMode.VideoAudio, .CertFingerprint = If(matches, server.Fingerprint, "SHA256:" & New String("A"c, 43))}
            Using session As New LiveHttpPlaybackSession(endpoint, New LivePlaybackOptions With {.ConnectTimeoutMs = 5000}),
                  cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Dim result As LiveOpenResult = Await session.OpenAsync(cts.Token)
                Assert.Equal(If(matches, LiveOpenOutcome.Connected, LiveOpenOutcome.AccessDenied), result.Outcome)
                If matches Then Assert.True(session.IsMpegTs)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Tunnel_Sends_Connect_Then_Http_And_Never_Dials_The_Documentation_Host() As Task
        Dim target As String = ""
        Using server As New ExchangeStubTlsServer(
            Async Function(connection)
                Dim opening As ExchangeEnvelope = Await connection.ReadAsync()
                target = opening.Text("broadcastId")
                Assert.Equal("connect", opening.Type)
                Assert.Equal("http", opening.Text("scheme"))
                Await connection.WriteAsync(New ExchangeEnvelope("connected"))
                Await RespondWithTs(connection)
            End Function)
            Dim endpoint As New LiveBroadcastEndpoint With {.Url = "fmsx://" & server.Address & "/b/abc/http", .Transport = "TUNNEL",
                .Inner = "http://192.0.2.1:8768/live.ts", .ModeValue = LiveBroadcastMode.VideoOnly, .CertFingerprint = server.Fingerprint}
            Using session As New LiveHttpPlaybackSession(endpoint, New LivePlaybackOptions With {.ConnectTimeoutMs = 5000}),
                  cts As New CancellationTokenSource(TimeSpan.FromSeconds(10))
                Assert.True((Await session.OpenAsync(cts.Token)).IsConnected)
                Assert.Equal("abc", target)
                Assert.True(session.IsMpegTs)
            End Using
        End Using
    End Function
    Private Shared Function Read(json As String) As LiveBroadcastReadResult
        Return LiveBroadcastReader.ReadText(json.Replace("'"c, """"c))
    End Function

    <Theory>
    <InlineData("VIDEO_AUDIO")>
    <InlineData("VIDEO_ONLY")>
    Public Sub Video_Descriptors_Use_The_Ts_Demuxer(mode As String)
        Dim result As LiveBroadcastReadResult = Read("{'schemaVersion':1,'url':'http://192.168.1.2/live.ts','mode':'" & mode & "'}")
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Contains(":demux=ts", New LivePlaybackOptions().ToMediaOptions(Nothing, mpegTs:=True))
        Assert.DoesNotContain(":demux=aac", New LivePlaybackOptions().ToMediaOptions(Nothing, mpegTs:=True))
    End Sub

    <Fact>
    Public Sub Password_And_Trust_Metadata_Survive_Storage_But_The_Password_Does_Not()
        Dim result As LiveBroadcastReadResult = Read("{'schemaVersion':1,'url':'https://relay.example/v2/b/abc/stream','mode':'AUDIO_ONLY'," &
            "'auth':{'scheme':'password-v1','user':'listener'},'endpoints':[{'url':'https://relay.example/v2/b/abc/stream'," &
            "'mode':'AUDIO_ONLY','transport':'RELAY','certFingerprint':'SHA256:pin'}]}")
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        result.Descriptor.Password = "session-secret"
        Dim file As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(file)
            store.Upsert(result.Descriptor)
            Assert.DoesNotContain("session-secret", System.IO.File.ReadAllText(file))
            Dim restored As LiveBroadcastDescriptor = Assert.Single(store.LoadAll())
            Assert.Equal("password-v1", restored.AuthScheme)
            Assert.Equal("listener", restored.AuthUser)
            Assert.Null(restored.Password)
            Assert.Equal("SHA256:pin", Assert.Single(restored.Endpoints).CertFingerprint)
        Finally
            System.IO.File.Delete(file)
        End Try
    End Sub

    <Fact>
    Public Sub Sealed_Only_Stream_Requires_An_Update_Instead_Of_Being_Played_As_Clear_Media()
        Dim result As LiveBroadcastReadResult = Read("{'schemaVersion':1,'url':'https://relay.example/v2/b/abc/stream','mode':'VIDEO_AUDIO'," &
            "'endpoints':[{'url':'https://relay.example/v2/b/abc/stream','mode':'VIDEO_AUDIO','transport':'RELAY','seal':{'v':1}}]}")
        Assert.Equal(LiveReadStatus.UnsupportedVersion, result.Status)
    End Sub

    <Fact>
    Public Sub Directory_Credentials_Are_Consumed_Only_From_The_Private_Record()
        Dim raw As String = "{'broadcastId':'abc','deviceId':'phone','credentials':{'user':'guest','password':'secret'}," &
            "'descriptor':{'schemaVersion':1,'url':'http://192.168.1.2/listen','mode':'AUDIO_ONLY','auth':{'scheme':'password-v1','user':'guest'}}}"
        raw = raw.Replace("'"c, """"c)
        Dim result As LiveBroadcastReadResult = LiveBroadcastRecords.ReadRecord(raw)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Assert.Equal("secret", result.Descriptor.Password)
        Assert.DoesNotContain("secret", ExchangeRecordReader.WithoutSecrets(raw))
        Assert.Null(LiveBroadcastRecords.ReadRecord(ExchangeRecordReader.WithoutSecrets(raw)).Descriptor.Password)
    End Sub

    <Fact>
    Public Sub Cleartext_Relay_And_Unknown_Authentication_Are_Refused()
        Assert.Equal(LiveReadStatus.UnsupportedMode, Read("{'schemaVersion':1,'url':'http://relay.example/stream','mode':'AUDIO_ONLY'," &
            "'endpoints':[{'url':'http://relay.example/stream','mode':'AUDIO_ONLY','transport':'RELAY'}]}").Status)
        Assert.Equal(LiveReadStatus.UnsupportedMode, Read("{'schemaVersion':1,'url':'http://192.168.1.2/listen','mode':'AUDIO_ONLY'," &
            "'auth':{'scheme':'future-auth','user':'guest'}}").Status)
    End Sub
End Class
#End If
