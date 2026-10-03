Option Strict On

Imports System.Collections.Generic
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Tests for BroadcastConfigBuilder (.spres JSON wire format, QR code encoding, SPCH1 link)
''' and CompanionCommand broadcast activation parsing.
''' Contract: CONTRACT_BROADCAST_RESOURCE.md (schemaVersion 1).
''' </summary>
Public Class BroadcastConfigBuilderTests

    Private Shared Function SampleBroadcast(Optional kind As String = "file",
                                             Optional hostPath As String = "C:\Media\movie.mp4",
                                             Optional title As String = "Test Movie",
                                             Optional token As String = "abcdef1234567890",
                                             Optional itemCount As Integer = 1,
                                             Optional durationSeconds As Long = 120) As WorkerBroadcast
        Return New WorkerBroadcast With {
            .Id = "bcast-1",
            .Kind = kind,
            .HostPath = hostPath,
            .Title = title,
            .Token = token,
            .ItemCount = itemCount,
            .DurationSeconds = durationSeconds,
            .CreatedAt = "2026-10-03T12:00:00Z",
            .ExpiresAt = "2026-10-04T12:00:00Z",
            .Hits = 0
        }
    End Function

    Private Shared Function SampleStatus(Optional reach As WorkerReachability = Nothing, Optional bcastPort As Integer = 55272) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2222,
            .BroadcastEnabled = True,
            .BroadcastPort = bcastPort,
            .Reachability = If(reach, New WorkerReachability With {.LanAddress = "192.168.1.100"})
        }
    End Function

    <Fact>
    Public Sub Build_ReturnsNothing_WhenBroadcastOrStatusIsNull()
        Assert.Null(BroadcastConfigBuilder.Build(Nothing, SampleStatus(), includeExternal:=False))
        Assert.Null(BroadcastConfigBuilder.Build(SampleBroadcast(), Nothing, includeExternal:=False))
    End Sub

    <Fact>
    Public Sub Build_LanOnly_ProducesValidSpresJsonAndQuickLink()
        Dim bcast = SampleBroadcast(kind:="file", hostPath:="C:\Media\video.mkv", title:="My Video", token:="tok123")
        Dim status = SampleStatus(bcastPort:=55272)

        Dim res = BroadcastConfigBuilder.Build(bcast, status, includeExternal:=False)
        Assert.NotNull(res)
        Assert.Equal("192.168.1.100:55272", res.LanDisplay)
        Assert.False(res.HasExternal)
        Assert.False(res.HasIpv6)
        Assert.False(res.QrOverflow)
        Assert.NotNull(res.QrPng)
        Assert.True(res.QrPng.Length > 0)
        Assert.Equal("SPCH1 http://192.168.1.100:55272/b/tok123", res.ClipboardLink)

        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim root = doc.RootElement
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32())
            Assert.Equal("broadcast", root.GetProperty("kind").GetString())
            Assert.Equal("My Video", root.GetProperty("resourceName").GetString())
            Assert.Equal("http", root.GetProperty("protocol").GetString())
            Assert.Equal("video", root.GetProperty("mediaKind").GetString())
            Assert.False(root.GetProperty("playlist").GetBoolean())
            Assert.Equal(1, root.GetProperty("itemCount").GetInt32())
            Assert.Equal("/b/tok123", root.GetProperty("path").GetString())
            Assert.Equal(120L, root.GetProperty("durationSeconds").GetInt64())
            Assert.Equal("2026-10-04T12:00:00Z", root.GetProperty("expiresAt").GetString())
            Assert.Equal("2026-10-03T12:00:00Z", root.GetProperty("createdAt").GetString())

            Dim paths = root.GetProperty("accessPaths")
            Assert.Equal(1, paths.GetArrayLength())
            Assert.Equal("lan", paths(0).GetProperty("kind").GetString())
            Assert.Equal("192.168.1.100", paths(0).GetProperty("host").GetString())
            Assert.Equal(55272, paths(0).GetProperty("port").GetInt32())
        End Using
    End Sub

    <Fact>
    Public Sub Build_FolderBroadcast_SetsPlaylistTrue()
        Dim bcast = SampleBroadcast(kind:="folder", hostPath:="C:\Media\ShowSeason1", title:="Show S1", token:="folder_tok", itemCount:=10)
        Dim status = SampleStatus()

        Dim res = BroadcastConfigBuilder.Build(bcast, status, includeExternal:=False)
        Assert.NotNull(res)

        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim root = doc.RootElement
            Assert.True(root.GetProperty("playlist").GetBoolean())
            Assert.Equal(10, root.GetProperty("itemCount").GetInt32())
        End Using
    End Sub

    <Fact>
    Public Sub Build_Internet_OrdersLanThenIpv6ThenPortforward_AndSelectsBestQuickLink()
        Dim reach As New WorkerReachability With {
            .LanAddress = "192.168.1.100",
            .Ipv6Address = "2001:db8::abcd",
            .ExternalHost = "203.0.113.10",
            .ExternalPort = 55272,
            .IsCgnat = False
        }
        Dim bcast = SampleBroadcast(token:="wan_tok")
        Dim status = SampleStatus(reach, bcastPort:=55272)

        Dim res = BroadcastConfigBuilder.Build(bcast, status, includeExternal:=True)
        Assert.NotNull(res)
        Assert.True(res.HasExternal)
        Assert.True(res.HasIpv6)
        Assert.Equal("SPCH1 http://203.0.113.10:55272/b/wan_tok", res.ClipboardLink)

        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim paths = doc.RootElement.GetProperty("accessPaths")
            Assert.Equal(3, paths.GetArrayLength())
            Assert.Equal("lan", paths(0).GetProperty("kind").GetString())
            Assert.Equal("ipv6", paths(1).GetProperty("kind").GetString())
            Assert.Equal("[2001:db8::abcd]", "[" & paths(1).GetProperty("host").GetString() & "]")
            Assert.Equal("portforward", paths(2).GetProperty("kind").GetString())
            Assert.Equal("203.0.113.10", paths(2).GetProperty("host").GetString())
        End Using
    End Sub

    <Theory>
    <InlineData("C:\Music\song.mp3", "file", "audio")>
    <InlineData("C:\Music\song.flac", "file", "audio")>
    <InlineData("C:\Music\song.wav", "file", "audio")>
    <InlineData("C:\Music\song.m4a", "file", "audio")>
    <InlineData("C:\Music\song.opus", "file", "audio")>
    <InlineData("C:\Videos\movie.mp4", "file", "video")>
    <InlineData("C:\Videos\movie.mkv", "file", "video")>
    <InlineData("C:\Videos\clip.avi", "file", "video")>
    Public Sub ClassifyMediaKind_RecognizesAudioAndVideoExtensions(path As String, kind As String, expectedKind As String)
        Assert.Equal(expectedKind, BroadcastConfigBuilder.ClassifyMediaKind(path, kind))
    End Sub

    <Theory>
    <InlineData("::fms-broadcast::C:\Videos\sample.mp4", "Broadcast", "C:\Videos\sample.mp4")>
    <InlineData("::fms-broadcast::D:\Music\Album", "Broadcast", "D:\Music\Album")>
    <InlineData("::fms-broadcast::\\server\share\file.mkv", "Broadcast", "\\server\share\file.mkv")>
    <InlineData("::fms-broadcast::", "Invalid", "")>
    <InlineData("::fms-broadcast::   ", "Invalid", "")>
    Public Sub CompanionCommand_ParsesBroadcastEntryPoint(raw As String, expectedKindName As String, expectedPath As String)
        Dim cmd = CompanionCommand.Parse(raw)
        Assert.Equal(expectedKindName, cmd.Kind.ToString())
        If String.Equals(expectedKindName, "Broadcast", StringComparison.Ordinal) Then
            Assert.Equal(expectedPath, cmd.Folder)
        End If
    End Sub

End Class
