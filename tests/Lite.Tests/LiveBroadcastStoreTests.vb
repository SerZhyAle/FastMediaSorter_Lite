#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Xunit

Public Class LiveBroadcastStoreTests

    ' LIVE-BROADCAST 1.1: an entry of the store this program cannot read is never destroyed by a rewrite. The two entries below are
    ' what a newer program (a later schema, with a member this one has never heard of) and a damaged hand edit look like on disk.
    Private Const LaterEntry As String = "{""schemaVersion"":2,""url"":""http://192.168.1.60:8080/later"",""mode"":""AUDIO_ONLY"",""sourceId"":""later-1"",""future"":{""x"":[1,2,3]}}"
    Private Const LaterEntryNoSourceId As String = "{""schemaVersion"":3,""url"":""http://192.168.1.61:8080/later"",""mode"":""AUDIO_ONLY"",""title"":""Later without id""}"
    Private Const DamagedEntry As String = "{""schemaVersion"":""one"",""url"":""http://192.168.1.62:8080/x""}"
    Private Const ReadableEntry As String = "{""schemaVersion"":1,""url"":""http://192.168.1.63:8080/live"",""mode"":""AUDIO_ONLY"",""title"":""Readable""}"

    Private Shared Function RawEntries(path As String) As List(Of String)
        Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(path))
            Return doc.RootElement.EnumerateArray().Select(Function(e) e.GetRawText()).ToList()
        End Using
    End Function

    Private Shared Function Audio(url As String, title As String, Optional sourceId As String = Nothing) As LiveBroadcastDescriptor
        Return New LiveBroadcastDescriptor With {.SchemaVersion = 1, .Url = url, .Mode = "AUDIO_ONLY", .ModeValue = LiveBroadcastMode.AudioOnly, .Title = title, .SourceId = sourceId}
    End Function

    <Fact>
    Public Sub An_Unreadable_Entry_Is_Not_Returned_But_Survives_Every_Rewrite_Unchanged()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            File.WriteAllText(tempStoreFile, "[" & ReadableEntry & "," & LaterEntry & "," & DamagedEntry & "]")
            Dim store As New LiveBroadcastStore(tempStoreFile)

            Dim loaded As List(Of LiveBroadcastDescriptor) = store.LoadAll()
            Assert.Single(loaded)
            Assert.Equal("Readable", loaded(0).Title)

            store.Upsert(Audio("http://192.168.1.70:8080/new", "New one"))

            Dim after As List(Of String) = RawEntries(tempStoreFile)
            Assert.Equal(4, after.Count)
            Assert.Contains(LaterEntry, after)
            Assert.Contains(DamagedEntry, after)
            Assert.Equal(2, store.LoadAll().Count)

            ' saving an empty list is not a reason to lose them either
            store.SaveAll(New List(Of LiveBroadcastDescriptor)())
            after = RawEntries(tempStoreFile)
            Assert.Equal(2, after.Count)
            Assert.Contains(LaterEntry, after)
            Assert.Contains(DamagedEntry, after)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub A_Readable_Descriptor_Of_The_Same_Channel_Replaces_The_Unreadable_Entry_By_SourceId_Or_Address()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            File.WriteAllText(tempStoreFile, "[" & LaterEntry & "," & LaterEntryNoSourceId & "," & DamagedEntry & "]")
            Dim store As New LiveBroadcastStore(tempStoreFile)

            ' same sourceId: the entry this program could not read is the older record of this very channel
            store.Upsert(Audio("http://192.168.1.60:9090/now", "Now readable", "later-1"))
            Dim after As List(Of String) = RawEntries(tempStoreFile)
            Assert.DoesNotContain(LaterEntry, after)
            Assert.Contains(LaterEntryNoSourceId, after)
            Assert.Contains(DamagedEntry, after)
            Assert.Equal(3, after.Count)

            ' a different channel leaves the entry with no sourceId alone ...
            store.Upsert(Audio("http://192.168.1.99:8080/other", "Other"))
            Assert.Contains(LaterEntryNoSourceId, RawEntries(tempStoreFile))

            ' ... and the same address, with no sourceId on either side, replaces it
            store.Upsert(Audio("http://192.168.1.61:8080/later", "Same address"))
            after = RawEntries(tempStoreFile)
            Assert.DoesNotContain(LaterEntryNoSourceId, after)
            Assert.Contains(DamagedEntry, after)
            Assert.Equal(3, store.LoadAll().Count)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub The_Unreadable_Entries_Kept_Are_Bounded_By_The_Same_Cap()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim entries As New List(Of String)()
            For i As Integer = 1 To LiveBroadcastStore.MaxEntries + 5
                entries.Add("{""schemaVersion"":2,""url"":""http://192.168.2." & i.ToString() & ":8080/later"",""mode"":""AUDIO_ONLY"",""sourceId"":""later-n" & i.ToString() & """}")
            Next
            File.WriteAllText(tempStoreFile, "[" & String.Join(",", entries) & "]")
            Dim store As New LiveBroadcastStore(tempStoreFile)

            store.Upsert(Audio("http://192.168.1.71:8080/new", "New one"))

            Dim after As List(Of String) = RawEntries(tempStoreFile)
            Assert.Equal(LiveBroadcastStore.MaxEntries + 1, after.Count)
            Assert.Equal(entries(0), after(1))
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

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

    ' LIVE-BROADCAST item G as DEVICE-EXCHANGE item BP resolves it: of two records of one sourceId the
    ' later startedAt is the channel's address; updatedAt decides nothing.

    Private Shared Function Channel(url As String, sourceId As String, startedAt As String) As LiveBroadcastDescriptor
        Dim d As LiveBroadcastDescriptor = Audio(url, "channel", sourceId)
        d.RecordStartedAt = startedAt
        Return d
    End Function

    <Fact>
    Public Sub An_Earlier_Record_Of_One_SourceId_Never_Displaces_A_Later_Stored_One()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)
            store.Upsert(Channel("http://192.168.1.80:8080/new", "src-1", "2026-10-22T10:00:00.000Z"))
            ' the out-of-order poll: an older record of the same source arrives last
            store.Upsert(Channel("http://192.168.1.81:8080/old", "src-1", "2026-10-22T09:00:00.000Z"))

            Dim loaded As List(Of LiveBroadcastDescriptor) = store.LoadAll()
            Assert.Single(loaded)
            Assert.Equal("http://192.168.1.80:8080/new", loaded(0).Url)
            Assert.Equal("2026-10-22T10:00:00.000Z", loaded(0).RecordStartedAt)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub A_Later_Record_Of_One_SourceId_Replaces_The_Stored_One_And_The_Start_Survives_A_Restart()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)
            store.Upsert(Channel("http://192.168.1.80:8080/old", "src-1", "2026-10-22T09:00:00.000Z"))
            store.Upsert(Channel("http://192.168.1.81:8080/new", "src-1", "2026-10-22T10:00:00.000Z"))

            ' a fresh store over the same file: the persisted entry still knows its start, so an
            ' out-of-order older record keeps losing after a restart too
            Dim reopened As New LiveBroadcastStore(tempStoreFile)
            reopened.Upsert(Channel("http://192.168.1.82:8080/older", "src-1", "2026-10-22T08:00:00.000Z"))

            Dim loaded As List(Of LiveBroadcastDescriptor) = reopened.LoadAll()
            Assert.Single(loaded)
            Assert.Equal("http://192.168.1.81:8080/new", loaded(0).Url)
            Assert.Equal("2026-10-22T10:00:00.000Z", loaded(0).RecordStartedAt)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

    <Fact>
    Public Sub Without_A_Readable_Start_On_Both_Sides_The_Incoming_Record_Wins_As_Before()
        Dim tempStoreFile As String = Path.GetTempFileName()
        Try
            Dim store As New LiveBroadcastStore(tempStoreFile)
            store.Upsert(Channel("http://192.168.1.80:8080/first", "src-1", "2026-10-22T10:00:00.000Z"))
            ' no startedAt on the incoming record (a hand-off import, a record without one): store order decides
            store.Upsert(Audio("http://192.168.1.81:8080/second", "imported", "src-1"))
            Dim one As List(Of LiveBroadcastDescriptor) = store.LoadAll()
            Assert.Single(one)
            Assert.Equal("http://192.168.1.81:8080/second", one(0).Url)

            ' an unreadable start text is the same as none
            store.Upsert(Channel("http://192.168.1.82:8080/third", "src-1", "not a date"))
            Dim two As List(Of LiveBroadcastDescriptor) = store.LoadAll()
            Assert.Single(two)
            Assert.Equal("http://192.168.1.82:8080/third", two(0).Url)
        Finally
            If File.Exists(tempStoreFile) Then File.Delete(tempStoreFile)
        End Try
    End Sub

End Class
#End If
