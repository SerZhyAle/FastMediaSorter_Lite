#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Tickets 170 rule 3 and 171 rule 3: the `access` member and the whole `sftp-share` record, written by the producer and read by the consumer
''' through ONE module. The record is held to the catalog's shape (member order of the rung 1 vectors, no `presence`, at most 16 KiB), and a
''' reader that finds no `root` falls back to "/" + the name.
''' </summary>
Public Class ExchangeResourceAccessTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Did As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"

    Private Shared Function Code(Optional withExchange As Boolean = False) As FmsSftpCode
        Return New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "k7PmQ2wXr9TzS4vGnHb3JdLe", Fp,
                               If(withExchange, "exchange.example.net:44022", ""), If(withExchange, Sid, ""), False)
    End Function

    <Fact>
    Public Sub Manager_refuses_a_changed_root_without_replacing_saved_access()
        Dim access As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(), "/new-root", access))
        Dim descriptor As ResourceDescriptor = ManagerExchangeFixture.ExchangeTestDescriptor()
        descriptor.ExchangeResourceId = Rid
        descriptor.ExchangeDeviceId = Did
        descriptor.PinnedFingerprint = Fp
        Dim device As ExchangeDeviceRecord = ManagerExchangeFixture.ExchangeTestDevice(Did)
        Dim resource As New ExchangeResourceRecord("{}", Rid, Did, ExchangeWire.KindSftpShare, "Same label", "online", access, hostKeyFingerprint:=Fp)
        Dim snapshot As New ExchangeDirectorySnapshot(1, {device}, {resource}, Array.Empty(Of ExchangeBroadcastRecord)())
        Dim refusal As ManagerExchangeException = Assert.Throws(Of ManagerExchangeException)(Sub() ManagerExchangeAccess.ValidateRecord(ManagerPlace.ForResource(descriptor), descriptor, snapshot))
        Assert.Equal(ManagerExchangeAvailability.Removed, refusal.State)
        Assert.Equal("/root", descriptor.RootPath)
        Assert.Equal(Fp, descriptor.PinnedFingerprint)
    End Sub

    <Fact>
    Public Sub Manager_refuses_the_same_resource_label_under_a_different_device()
        Dim descriptor As ResourceDescriptor = ManagerExchangeFixture.ExchangeTestDescriptor()
        Dim snapshot = ManagerExchangeFixture.ExchangeTestDirectoryOf(ManagerExchangeFixture.ExchangeTestRoot(deviceId:="device2"))
        Assert.Equal(ManagerExchangeAvailability.Removed,
            Assert.Throws(Of ManagerExchangeException)(Sub() ManagerExchangeAccess.ValidateRecord(ManagerPlace.ForResource(descriptor), descriptor, snapshot)).State)
    End Sub

    <Fact>
    Public Sub Access_round_trips_the_code_and_the_root()
        Dim json As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(withExchange:=True), "/Camera roll", json))
        Dim info As ExchangeAccessInfo = Nothing
        Assert.True(ExchangeResourceAccess.TryRead(json, "ignored", info))
        Assert.Equal("/Camera roll", info.Root)
        Assert.Equal(Sid, info.Code.ShareId)
        Assert.Equal("exchange.example.net:44022", info.Code.Exchange)
        Assert.Equal("k7PmQ2wXr9TzS4vGnHb3JdLe", info.Code.Password)
    End Sub

    <Fact>
    Public Sub A_missing_root_falls_back_to_slash_and_the_name()
        Dim text As String = Nothing
        Assert.True(Code().TryBuild(text))
        Dim json As String = "{""descriptor"":""" & text & """}"
        Dim info As ExchangeAccessInfo = Nothing
        Assert.True(ExchangeResourceAccess.TryRead(json, "Pixel 8", info))
        Assert.Equal("/Pixel 8", info.Root)
        Assert.Equal("/Pixel 8", ExchangeResourceAccess.FallbackRoot("  Pixel 8 "))
        Assert.Equal("/DCIM/Camera", ExchangeResourceAccess.FallbackRoot("\DCIM\Camera\"))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("not json")>
    <InlineData("[]")>
    <InlineData("{}")>
    <InlineData("{""descriptor"":5}")>
    <InlineData("{""descriptor"":""FMSSFTP1:garbage""}")>
    <InlineData("{""descriptor"":""x"",""root"":""/a""}")>
    Public Sub An_access_that_is_not_whole_is_refused(json As String)
        Dim info As ExchangeAccessInfo = Nothing
        Assert.False(ExchangeResourceAccess.TryRead(json, "name", info))
        Assert.Null(info)
    End Sub

    <Theory>
    <InlineData("relative")>
    <InlineData("/a/../b")>
    <InlineData("/a/./b")>
    <InlineData("/a\b")>
    <InlineData("")>
    Public Sub An_unusable_root_is_refused_both_ways(root As String)
        Assert.False(ExchangeResourceAccess.IsUsableRoot(root))
        Dim json As String = Nothing
        Assert.False(ExchangeResourceAccess.TryBuild(Code(), root, json))
        Dim text As String = Nothing
        Assert.True(Code().TryBuild(text))
        Dim info As ExchangeAccessInfo = Nothing
        Assert.False(ExchangeResourceAccess.TryRead("{""descriptor"":""" & text & """,""root"":""" & root.Replace("\", "\\") & """}", "name", info))
    End Sub

    <Fact>
    Public Sub A_root_that_is_present_but_not_a_string_is_refused()
        Dim text As String = Nothing
        Assert.True(Code().TryBuild(text))
        Dim info As ExchangeAccessInfo = Nothing
        Assert.False(ExchangeResourceAccess.TryRead("{""descriptor"":""" & text & """,""root"":7}", "name", info))
    End Sub

    <Fact>
    Public Sub Same_access_compares_the_code_and_the_root_not_the_text()
        Dim a As String = Nothing
        Dim b As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(), "/Photos", a))
        Assert.True(ExchangeResourceAccess.TryBuild(Code(), "/Photos", b))
        Assert.True(ExchangeResourceAccess.SameAccess(a, b, "Photos"))
        Dim indented As String
        Using doc As JsonDocument = JsonDocument.Parse(a)
            indented = JsonSerializer.Serialize(doc.RootElement, New JsonSerializerOptions With {.WriteIndented = True})
        End Using
        Assert.NotEqual(a, indented)
        Assert.True(ExchangeResourceAccess.SameAccess(a, indented, "Photos"))                  ' white space in the JSON is not a change
        Dim other As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({"192.168.1.24"}, 2022, "fms", "k7PmQ2wXr9TzS4vGnHb3JdLe", Fp, "", "", False), "/Photos", other))
        Assert.False(ExchangeResourceAccess.SameAccess(a, other, "Photos"))                    ' a new address
        Dim moved As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(), "/Videos", moved))
        Assert.False(ExchangeResourceAccess.SameAccess(a, moved, "Photos"))                    ' another root
    End Sub

    ' --- the record ---

    <Fact>
    Public Sub The_record_has_the_members_of_the_catalog_in_order_and_no_presence()
        Dim access As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(withExchange:=True), "/Camera", access))
        Dim json As String = Nothing
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "Camera roll", access, Sid, 0, New DateTime(2026, 10, 7, 11, 58, 0, DateTimeKind.Utc), json))
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim names As New List(Of String)
            For Each p As JsonProperty In doc.RootElement.EnumerateObject()
                names.Add(p.Name)
            Next
            Assert.Equal({"resourceId", "deviceId", "kind", "name", "updatedAt", "access", "shareId", "publicPort"}, names.ToArray())
            Assert.Equal("sftp-share", doc.RootElement.GetProperty("kind").GetString())
            Assert.Equal("2026-10-07T11:58:00.000Z", doc.RootElement.GetProperty("updatedAt").GetString())
            Assert.Equal(0, doc.RootElement.GetProperty("publicPort").GetInt32())
            Assert.Equal("/Camera", doc.RootElement.GetProperty("access").GetProperty("root").GetString())
        End Using
        Assert.DoesNotContain("presence", json, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub Optional_members_are_left_out_when_not_asked_for()
        Dim json As String = Nothing
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "Pixel 8", "", "", Nothing, DateTime.UtcNow, json))
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim names As New List(Of String)
            For Each p As JsonProperty In doc.RootElement.EnumerateObject()
                names.Add(p.Name)
            Next
            Assert.Equal({"resourceId", "deviceId", "kind", "name", "updatedAt"}, names.ToArray())   ' a record without `access` is valid (item X)
        End Using
    End Sub

    <Fact>
    Public Sub A_non_ASCII_name_is_written_as_it_is()
        Dim json As String = Nothing
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "Фото «лето»", "", "", Nothing, DateTime.UtcNow, json))
        Assert.Contains("Фото «лето»", json, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub A_record_that_breaks_a_rule_of_section_6_is_never_built()
        Dim json As String = Nothing
        Assert.False(ExchangeResourceRecordBuilder.TryBuild("bad", Did, "n", "", "", Nothing, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, "bad", "n", "", "", Nothing, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "  ", "", "", Nothing, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", "", "bad", Nothing, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", "", "", 70000, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", "", "", -1, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", "[1]", "", Nothing, DateTime.UtcNow, json))
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", "{not json", "", Nothing, DateTime.UtcNow, json))
        Assert.Equal("", json)
    End Sub

    <Fact>
    Public Sub A_record_above_sixteen_kib_is_never_built()
        Dim access As String = "{""descriptor"":""x"",""pad"":""" & New String("a"c, 17000) & """}"
        Dim json As String = Nothing
        Assert.False(ExchangeResourceRecordBuilder.TryBuild(Rid, Did, "n", access, "", Nothing, DateTime.UtcNow, json))
    End Sub

    <Fact>
    Public Sub An_access_never_prints_a_member()
        Dim info As ExchangeAccessInfo = Nothing
        Dim json As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(Code(), "/a", json))
        Assert.True(ExchangeResourceAccess.TryRead(json, "a", info))
        Assert.Equal("ExchangeAccessInfo", info.ToString())
        Assert.DoesNotContain("k7Pm", info.ToString(), StringComparison.Ordinal)
    End Sub

End Class
#End If
