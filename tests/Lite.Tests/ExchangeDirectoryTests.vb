#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' Ticket 180 T-2 (the directory model, in the library because the viewer reads the same shapes): a `directory` replaces everything, a
''' `changed` carries the next revision, a gap means "list again", a record the reader cannot use is skipped and counted, an unknown member
''' survives, and what the local channel hands the viewer carries no `access` and no `shareId`.
''' </summary>
Public Class ExchangeDirectoryTests

    Private Const Me1 As String = "{""deviceId"":""ME"",""deviceName"":""This PC"",""product"":""fms-windows"",""presence"":""online""}"
    Private Const Phone As String = "{""deviceId"":""PH"",""deviceName"":""Phone"",""product"":""fms-android"",""roles"":[""resource-producer""],""presence"":""online"",""lastSeen"":""2026-10-07T12:00:00Z""}"
    Private Const PhoneShare As String = "{""resourceId"":""R1"",""deviceId"":""PH"",""kind"":""sftp-share"",""name"":""Photos"",""presence"":""online"",""shareId"":""SH"",""access"":{""descriptor"":""FMSSFTP2.secret"",""root"":""/""},""future"":{""a"":{""b"":{""c"":1}}}}"
    Private Const MyShare As String = "{""resourceId"":""R2"",""deviceId"":""ME"",""kind"":""sftp-share"",""name"":""Mine"",""access"":{""descriptor"":""FMSSFTP2.mine""}}"
    Private Const Live As String = "{""broadcastId"":""B1"",""deviceId"":""PH"",""mode"":""AUDIO_ONLY"",""title"":""Mic"",""descriptor"":{""endpoints"":[]}}"

    Private Shared Function Directory(revision As Integer, devices As String(), resources As String(), broadcasts As String()) As ExchangeEnvelope
        Return New ExchangeEnvelope("directory").SetNumber("revision", revision).SetRecords("devices", devices).SetRecords("resources", resources).SetRecords("broadcasts", broadcasts)
    End Function

    Private Shared Function Changed(revision As Integer, Optional upserts As ExchangeRecordSets = Nothing, Optional removals As ExchangeIdSets = Nothing) As ExchangeEnvelope
        Return New ExchangeEnvelope("changed").SetNumber("revision", revision).SetUpserts(If(upserts, New ExchangeRecordSets())).SetRemovals(If(removals, New ExchangeIdSets()))
    End Function

    <Fact>
    Public Sub A_directory_replaces_everything_and_sets_the_revision()
        Dim model As New ExchangeDirectoryModel()
        Assert.False(model.HasSnapshot)
        model.ApplyDirectory(Directory(7, {Me1, Phone}, {PhoneShare, MyShare}, {Live}))
        Assert.True(model.HasSnapshot)
        Assert.Equal(7L, model.Revision)
        Dim s As ExchangeDirectorySnapshot = model.Snapshot()
        Assert.Equal(2, s.Devices.Count)
        Assert.Equal(2, s.Resources.Count)
        Assert.Single(s.Broadcasts)
        model.ApplyDirectory(Directory(9, {Phone}, Array.Empty(Of String)(), Array.Empty(Of String)()))
        Assert.Equal(9L, model.Revision)
        Assert.Single(model.Snapshot().Devices)
        Assert.Empty(model.Snapshot().Resources)
    End Sub

    <Fact>
    Public Sub A_change_with_the_next_revision_applies_and_upserts_and_removals_move_records()
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(7, {Phone}, {PhoneShare}, {Live}))
        Dim up As New ExchangeRecordSets()
        up.Devices.Add(Me1)
        up.Resources.Add(MyShare.Replace("""name"":""Mine""", """name"":""Renamed"""))
        Dim rem1 As New ExchangeIdSets()
        rem1.BroadcastIds.Add("B1")
        rem1.ResourceIds.Add("R1")
        Assert.Equal(ExchangeDirectoryApply.Applied, model.ApplyChanged(Changed(8, up, rem1)))
        Assert.Equal(8L, model.Revision)
        Dim s As ExchangeDirectorySnapshot = model.Snapshot()
        Assert.Equal(2, s.Devices.Count)
        Assert.Equal({"R2"}, s.Resources.Select(Function(r) r.ResourceId).ToArray())
        Assert.Equal("Renamed", s.Resources(0).Name)
        Assert.Empty(s.Broadcasts)
        ' an upsert of an existing id replaces it
        Dim up2 As New ExchangeRecordSets()
        up2.Resources.Add(MyShare.Replace("""name"":""Mine""", """name"":""Again"""))
        model.ApplyChanged(Changed(9, up2))
        Assert.Equal("Again", model.Snapshot().Resources.Single().Name)
    End Sub

    <Fact>
    Public Sub A_skipped_revision_is_a_gap_a_repeated_one_is_stale_and_a_change_before_any_snapshot_is_a_gap()
        Dim model As New ExchangeDirectoryModel()
        Assert.Equal(ExchangeDirectoryApply.Gap, model.ApplyChanged(Changed(1)))   ' nothing to apply it to
        model.ApplyDirectory(Directory(7, {Phone}, Array.Empty(Of String)(), Array.Empty(Of String)()))
        Assert.Equal(ExchangeDirectoryApply.Gap, model.ApplyChanged(Changed(9)))   ' 8 never arrived
        Assert.Equal(7L, model.Revision)                                           ' and nothing was applied
        Assert.Equal(ExchangeDirectoryApply.Stale, model.ApplyChanged(Changed(7)))
        Assert.Equal(ExchangeDirectoryApply.Stale, model.ApplyChanged(Changed(3)))
        Assert.Equal(ExchangeDirectoryApply.Applied, model.ApplyChanged(Changed(8)))
    End Sub

    <Fact>
    Public Sub A_record_above_16_KiB_or_without_its_id_is_skipped_and_counted_not_an_error()
        Dim huge As String = "{""resourceId"":""BIG"",""deviceId"":""PH"",""pad"":""" & New String("x"c, ExchangeWire.MaxRecordBytes) & """}"
        Dim noId As String = "{""deviceId"":""PH"",""kind"":""sftp-share""}"
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(1, {Phone}, {huge, noId, PhoneShare}, Array.Empty(Of String)()))
        Assert.Equal({"R1"}, model.Snapshot().Resources.Select(Function(r) r.ResourceId).ToArray())
        Assert.Equal(2, model.SkippedRecords)
    End Sub

    <Fact>
    Public Sub A_resource_of_a_kind_this_program_does_not_know_is_kept_and_marked_not_dropped()
        Dim other As String = "{""resourceId"":""R9"",""deviceId"":""PH"",""kind"":""webdav"",""name"":""X""}"
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(1, {Phone}, {other, PhoneShare}, Array.Empty(Of String)()))
        Dim byId As Dictionary(Of String, ExchangeResourceRecord) = model.Snapshot().Resources.ToDictionary(Function(r) r.ResourceId)
        Assert.False(byId("R9").IsSupportedKind)
        Assert.True(byId("R1").IsSupportedKind)
        Assert.False(byId("R9").HasAccess)
        Assert.True(byId("R1").HasAccess)
    End Sub

    <Fact>
    Public Sub Its_own_records_are_hidden_from_a_consumers_view()
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(1, {Me1, Phone}, {MyShare, PhoneShare}, {Live}))
        Dim view As ExchangeDirectorySnapshot = model.Snapshot().WithoutDevice("ME")
        Assert.Equal({"PH"}, view.Devices.Select(Function(d) d.DeviceId).ToArray())
        Assert.Equal({"R1"}, view.Resources.Select(Function(r) r.ResourceId).ToArray())
        Assert.Single(view.Broadcasts)
        Assert.Equal(1L, view.Revision)
    End Sub

    <Fact>
    Public Sub The_local_json_carries_no_access_and_no_share_id_and_reads_back()
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(5, {Phone}, {PhoneShare}, {Live}))
        Dim json As String = model.Snapshot().ToLocalJson()
        Assert.DoesNotContain("secret", json)
        Assert.DoesNotContain("descriptor"":""FMSSFTP2", json)
        Assert.DoesNotContain("shareId", json)
        Assert.DoesNotContain("""access""", json)
        Assert.Contains("""future""", json)   ' what is not a secret survives, unknown members included
        Dim back As ExchangeDirectorySnapshot = ExchangeDirectorySnapshot.FromLocalJson(json)
        Assert.Equal(5L, back.Revision)
        Assert.Equal("Photos", back.Resources.Single().Name)
        Assert.False(back.Resources.Single().HasAccess)
        Assert.Equal("PH", back.Devices.Single().DeviceId)
        Assert.Equal("Mic", back.Broadcasts.Single().Title)
    End Sub

    <Fact>
    Public Sub Access_of_one_resource_is_available_to_the_agent_and_only_by_its_id()
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(5, {Phone}, {PhoneShare}, Array.Empty(Of String)()))
        Assert.Contains("FMSSFTP2.secret", model.AccessOf("R1"))
        Assert.Equal("", model.AccessOf("nope"))
    End Sub

    <Fact>
    Public Sub A_cleared_model_forgets_everything_and_asks_for_a_new_snapshot()
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(5, {Phone}, {PhoneShare}, {Live}))
        model.Clear()
        Assert.False(model.HasSnapshot)
        Assert.Equal(0L, model.Revision)
        Assert.Empty(model.Snapshot().Devices)
        Assert.Equal(ExchangeDirectoryApply.Gap, model.ApplyChanged(Changed(6)))
    End Sub

    <Fact>
    Public Sub Presence_and_roles_are_read_from_a_device_record()
        Dim rec As ExchangeDeviceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadDevice(Phone, rec))
        Assert.True(rec.IsOnline)
        Assert.Equal({"resource-producer"}, rec.Roles.ToArray())
        Assert.Equal("2026-10-07T12:00:00Z", rec.LastSeen)
        Assert.False(ExchangeRecordReader.TryReadDevice("[1]", rec))
        Assert.False(ExchangeRecordReader.TryReadDevice("{""deviceName"":""no id""}", rec))
    End Sub

    <Theory>
    <InlineData("{""scheme"":""future""}")>
    <InlineData("null")>
    <InlineData("[]")>
    Public Sub Protection_is_kept_visible_but_never_exposes_access_to_an_unsupported_client(protection As String)
        Dim raw As String = PhoneShare.Substring(0, PhoneShare.Length - 1) & ",""protection"":" & protection & "}"
        Dim record As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource(raw, record))
        Assert.True(record.IsProtected)
        Assert.False(record.HasAccess)
        Assert.Empty(record.AccessJson)
        Assert.Equal("R1", record.ResourceId)
        Dim model As New ExchangeDirectoryModel()
        model.ApplyDirectory(Directory(1, {Phone}, {raw}, Array.Empty(Of String)()))
        Assert.Single(model.Snapshot().Resources)
    End Sub

    <Fact>
    Public Sub Pin_v1_access_retains_the_lock_requirement_for_a_capable_consumer()
        Dim raw As String = PhoneShare.Substring(0, PhoneShare.Length - 1) & ",""protection"":{""scheme"":""pin-v1""}}"
        Dim record As ExchangeResourceRecord = Nothing
        Assert.True(ExchangeRecordReader.TryReadResource(raw, record))
        Assert.True(record.IsProtected)
        Assert.True(record.CanAuthorize)
        Assert.True(record.HasAccess)
    End Sub

End Class
#End If
