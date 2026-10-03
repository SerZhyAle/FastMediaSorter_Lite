#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

Public Class LiveBroadcastStoreTests

    <Fact>
    Public Sub SourceId_Replacement_Updates_Port_And_Preserves_One_Entry()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)

            ' Initial import with sourceId
            Dim d1 As New LiveBroadcastDescriptor With {
                .SchemaVersion = 1,
                .Url = "http://192.168.1.120:8080/live-audio",
                .Mode = "AUDIO_ONLY",
                .ModeValue = LiveBroadcastMode.AudioOnly,
                .Title = "Watch 1",
                .SourceId = "watch-stable-id-1"
            }
            store.Upsert(d1)

            Dim loaded1 = store.LoadAll()
            Assert.Single(loaded1)
            Assert.Equal("http://192.168.1.120:8080/live-audio", loaded1(0).Url)

            ' Subsequent import with same sourceId but dynamic port change (e.g. port 9090)
            Dim d2 As New LiveBroadcastDescriptor With {
                .SchemaVersion = 1,
                .Url = "http://192.168.1.120:9090/live-audio",
                .Mode = "AUDIO_ONLY",
                .ModeValue = LiveBroadcastMode.AudioOnly,
                .Title = "Watch 1 Updated",
                .SourceId = "watch-stable-id-1"
            }
            store.Upsert(d2)

            Dim loaded2 = store.LoadAll()
            Assert.Single(loaded2)
            Assert.Equal("http://192.168.1.120:9090/live-audio", loaded2(0).Url)
            Assert.Equal("Watch 1 Updated", loaded2(0).Title)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub Without_SourceId_Same_Url_Updates_Existing_Different_Url_Adds()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)

            Dim d1 As New LiveBroadcastDescriptor With {
                .SchemaVersion = 1,
                .Url = "http://192.168.1.130:8080/live",
                .Mode = "AUDIO_ONLY",
                .ModeValue = LiveBroadcastMode.AudioOnly,
                .Title = "Phone A"
            }
            store.Upsert(d1)

            Dim d2 As New LiveBroadcastDescriptor With {
                .SchemaVersion = 1,
                .Url = "http://192.168.1.130:8080/live",
                .Mode = "AUDIO_ONLY",
                .ModeValue = LiveBroadcastMode.AudioOnly,
                .Title = "Phone A Renamed"
            }
            store.Upsert(d2)

            Dim loaded = store.LoadAll()
            Assert.Single(loaded)
            Assert.Equal("Phone A Renamed", loaded(0).Title)

            Dim d3 As New LiveBroadcastDescriptor With {
                .SchemaVersion = 1,
                .Url = "http://192.168.1.131:8080/live",
                .Mode = "AUDIO_ONLY",
                .ModeValue = LiveBroadcastMode.AudioOnly,
                .Title = "Phone B"
            }
            store.Upsert(d3)

            loaded = store.LoadAll()
            Assert.Equal(2, loaded.Count)
            ' Newest is at index 0
            Assert.Equal("Phone B", loaded(0).Title)
            Assert.Equal("Phone A Renamed", loaded(1).Title)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub Store_Caps_At_20_Entries()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)

            For i As Integer = 1 To 25
                Dim d As New LiveBroadcastDescriptor With {
                    .SchemaVersion = 1,
                    .Url = $"http://192.168.1.{i}:8080/live",
                    .Mode = "AUDIO_ONLY",
                    .ModeValue = LiveBroadcastMode.AudioOnly,
                    .Title = $"Device {i}"
                }
                store.Upsert(d)
            Next

            Dim loaded = store.LoadAll()
            Assert.Equal(20, loaded.Count)
            Assert.Equal("Device 25", loaded(0).Title)
            Assert.Equal("Device 6", loaded(19).Title)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

End Class
#End If
