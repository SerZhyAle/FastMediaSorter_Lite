#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket 171 rules 3, 4 and 5 (DEVICE-EXCHANGE 9 rules 1-3), the pure half: what a producer's record becomes when it is attached (the host list is the
''' candidate list, the fingerprint the pin, the password not in the registry JSON), what following the producer replaces and notices, and the four
''' states with the rule that absence is not authority - nothing here, and nothing in the sync that calls it, deletes a resource.
''' </summary>
Public Class ExchangeAttachTests

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Fp2 As String = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    Private Const Rid As String = "EBESExQVFhcYGRobHB0eHw"
    Private Const Did As String = "AAECAwQFBgcICQoLDA0ODw"
    Private Const Password As String = "k7PmQ2wXr9TzS4vGnHb3JdLe"

    Private Shared Function Access(Optional hosts As String() = Nothing, Optional port As Integer = 2022, Optional password As String = Password,
                                   Optional fp As String = Fp, Optional root As String = "/Camera") As String
        Dim json As String = Nothing
        Dim code As New FmsSftpCode(If(hosts, {"192.168.1.23", "2001:db8::23", "203.0.113.9"}), port, "fms", password, fp, "", "", False)
        Assert.True(ExchangeResourceAccess.TryBuild(code, root, json))
        Return json
    End Function

    Private Shared Function Draft(Optional accessJson As String = Nothing) As ExchangeAttachDraft
        Dim made As Integer = 0
        Return ExchangeAttach.BuildDraft(Rid, Did, "Home PC", "Camera", If(accessJson, Access()), Function()
                                                                                                    made += 1
                                                                                                    Return "res:fixed" & made.ToString()
                                                                                                End Function)
    End Function

    ' --- attach ---

    <Fact>
    Public Sub An_attached_resource_is_an_SFTP_resource_of_the_exchange_bound_to_the_producers_id()
        Dim d As ResourceDescriptor = Draft().Descriptor
        Assert.Equal(ResourceKind.Sftp, d.Kind)
        Assert.Equal(ResourceOrigin.Exchange, d.Origin)
        Assert.Equal(Rid, d.ExchangeResourceId)
        Assert.Equal(Did, d.ExchangeDeviceId)
        Assert.Equal("Home PC", d.ExchangeDeviceName)
        Assert.Equal("Camera", d.DisplayName)
        Assert.Equal("res:fixed1", d.Id)
        Assert.Equal("192.168.1.23", d.Host)
        Assert.Equal(2022, d.Port)
        Assert.Equal("fms", d.UserName)
        Assert.Equal("/Camera", d.RootPath)
        Assert.Equal(Fp, d.PinnedFingerprint)          ' the pin comes with the access: the first connection is already verified
        Assert.False(d.ReadOnlyHint)
        Assert.True(ResourceRegistry.IsStorable(d))
    End Sub

    <Fact>
    Public Sub The_host_list_is_the_candidate_list_in_the_producers_order_with_kinds()
        Dim d As ResourceDescriptor = Draft().Descriptor
        Assert.Equal({"192.168.1.23", "2001:db8::23", "203.0.113.9"}, d.Endpoints.Select(Function(e) e.Host).ToArray())
        Assert.Equal({"lan", "ipv6", "portforward"}, d.Endpoints.Select(Function(e) e.Kind).ToArray())
        Assert.All(d.Endpoints, Sub(e) Assert.Equal(2022, e.Port))
        Dim candidates As List(Of FmsEndpoint) = ResourceConnect.CandidatesOf(d)
        Assert.Equal("primary", candidates(0).Kind)         ' the address the resource is named by first, the rest in the producer's order
        Assert.Equal(4, candidates.Count)
        Assert.Equal(3, EndpointResolver.PlanCandidates(candidates).Count)   ' the primary and its own entry collapse
    End Sub

    <Fact>
    Public Sub The_password_goes_to_the_draft_and_never_into_the_registry_json()
        Dim made As ExchangeAttachDraft = Draft()
        Assert.Equal(Password, made.Password)
        Dim json As String = ResourceRegistry.Serialize({made.Descriptor})
        Assert.DoesNotContain(Password, json, StringComparison.Ordinal)
        Assert.DoesNotContain("192.168.1.23" & vbLf, json, StringComparison.Ordinal)
        Assert.Equal("ExchangeAttachDraft", made.ToString())
        ' and round trips through the registry's own reader with its origin members
        Dim back As ResourceDescriptor = ResourceRegistry.Parse(json).Single()
        Assert.Equal(Rid, back.ExchangeResourceId)
        Assert.Equal(ResourceOrigin.Exchange, back.Origin)
    End Sub

    <Fact>
    Public Sub A_resource_with_no_origin_serializes_exactly_as_before()
        Dim plain As New ResourceDescriptor With {.Id = "res:1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "nas", .Port = 22, .UserName = "u", .RootPath = "/"}
        Dim json As String = ResourceRegistry.Serialize({plain})
        For Each member As String In {"Origin", "ExchangeResourceId", "ExchangeDeviceId", "ExchangeDeviceName", "ExchangeNotice", "TunnelHost", "TunnelPort", "TunnelSecretHandle", "TunnelPin"}
            Assert.DoesNotContain(member, json, StringComparison.Ordinal)
        Next
        Assert.False(ResourceOrigin.IsOfTheExchange(plain))
    End Sub

    <Fact>
    Public Sub An_older_build_keeps_the_origin_members_it_does_not_know()
        Dim json As String = "[{""Id"":""res:1"",""DisplayName"":""x"",""Kind"":""Sftp"",""Host"":""h"",""Port"":22,""UserName"":""u"",""RootPath"":""/"",""FutureMember"":7}]"
        Dim loaded As ResourceDescriptor = ResourceRegistry.Parse(json).Single()
        Assert.Contains("FutureMember", ResourceRegistry.Serialize({loaded}), StringComparison.Ordinal)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("{}")>
    <InlineData("{""descriptor"":""FMSSFTP1:garbage""}")>
    Public Sub A_record_that_cannot_be_attached_gives_no_draft(accessJson As String)
        Assert.Null(ExchangeAttach.BuildDraft(Rid, Did, "d", "n", accessJson, Function() "id"))
    End Sub

    <Fact>
    Public Sub A_key_login_and_an_unusable_root_give_no_draft()
        Dim keyLogin As String = Nothing
        Assert.True(ExchangeResourceAccess.TryBuild(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", Nothing, Fp, "", "", False), "/a", keyLogin))
        Assert.Null(ExchangeAttach.BuildDraft(Rid, Did, "d", "n", keyLogin, Function() "id"))      ' this viewer opens a share with a password
    End Sub

    <Fact>
    Public Sub A_missing_root_falls_back_to_slash_and_the_name_and_a_third_party_name_is_kept_as_it_is()
        Dim text As String = Nothing
        Assert.True(New FmsSftpCode({"192.168.1.23"}, 2022, "fms", Password, Fp, "", "", False).TryBuild(text))
        Dim made As ExchangeAttachDraft = ExchangeAttach.BuildDraft(Rid, Did, "Phone", "Pixel 8", "{""descriptor"":""" & text & """}", Function() "id")
        Assert.Equal("/Pixel 8", made.Descriptor.RootPath)
        Assert.Equal("Pixel 8", made.Descriptor.DisplayName)
    End Sub

    <Theory>
    <InlineData("192.168.1.5", "lan")>
    <InlineData("10.0.0.2", "lan")>
    <InlineData("172.16.0.1", "lan")>
    <InlineData("172.32.0.1", "portforward")>
    <InlineData("100.64.0.7", "lan")>
    <InlineData("169.254.1.1", "lan")>
    <InlineData("203.0.113.9", "portforward")>
    <InlineData("nas.example.net", "portforward")>
    <InlineData("2001:db8::1", "ipv6")>
    <InlineData("[2001:db8::1]", "ipv6")>
    Public Sub An_address_is_labelled_in_the_producers_vocabulary(host As String, expected As String)
        Assert.Equal(expected, ExchangeAttach.KindOfHost(host))
    End Sub

    ' --- follow the producer ---

    Private Shared Function Stored() As ResourceDescriptor
        Dim d As ResourceDescriptor = Draft().Descriptor
        d.LastUsedUtc = New DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        d.DisplayName = "My own name"
        Return d
    End Function

    <Fact>
    Public Sub The_same_access_changes_nothing_and_returns_the_same_object()
        Dim existing As ResourceDescriptor = Stored()
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(existing, Password, Draft(), "Home PC")
        Assert.Same(existing, followed.Descriptor)
        Assert.False(followed.Changed)
        Assert.Null(followed.NewPassword)
    End Sub

    <Fact>
    Public Sub A_changed_address_set_replaces_the_kept_one_and_sets_the_notice_but_keeps_what_is_the_users()
        Dim existing As ResourceDescriptor = Stored()
        Dim moved As ExchangeAttachDraft = Draft(Access(hosts:={"192.168.1.99", "203.0.113.9"}))
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(existing, Password, moved, "Home PC")
        Assert.NotSame(existing, followed.Descriptor)
        Assert.True(followed.Changed)
        Assert.Equal("192.168.1.99", followed.Descriptor.Host)
        Assert.Equal({"192.168.1.99", "203.0.113.9"}, followed.Descriptor.Endpoints.Select(Function(e) e.Host).ToArray())
        Assert.True(followed.Descriptor.ExchangeNotice.GetValueOrDefault())
        Assert.Null(followed.NewPassword)                          ' the password did not change
        Assert.Equal(existing.Id, followed.Descriptor.Id)
        Assert.Equal("My own name", followed.Descriptor.DisplayName)
        Assert.Equal(existing.LastUsedUtc, followed.Descriptor.LastUsedUtc)
        Assert.Equal(ResourceOrigin.Exchange, followed.Descriptor.Origin)
        Assert.Equal("192.168.1.23", existing.Host)                ' the kept object is untouched until the caller commits the new one
    End Sub

    <Fact>
    Public Sub A_new_password_and_a_new_host_key_are_taken_with_a_notice()
        Dim existing As ResourceDescriptor = Stored()
        Dim reinstalled As ExchangeAttachDraft = Draft(Access(password:="Nn9PmQ2wXr9TzS4vGnHb3Jd1", fp:=Fp2))
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(existing, Password, reinstalled, "Home PC")
        Assert.True(followed.Changed)
        Assert.Equal(Fp2, followed.Descriptor.PinnedFingerprint)    ' replaced and said, never asked and never silent (Q171-3)
        Assert.Equal("Nn9PmQ2wXr9TzS4vGnHb3Jd1", followed.NewPassword)
        Assert.True(followed.Descriptor.ExchangeNotice.GetValueOrDefault())
    End Sub

    <Fact>
    Public Sub Only_a_renamed_device_updates_the_name_without_a_notice()
        Dim existing As ResourceDescriptor = Stored()
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(existing, Password, Draft(), "Office PC")
        Assert.False(followed.Changed)
        Assert.Equal("Office PC", followed.Descriptor.ExchangeDeviceName)
        Assert.False(followed.Descriptor.ExchangeNotice.GetValueOrDefault())
    End Sub

    <Fact>
    Public Sub A_resource_of_another_origin_is_never_followed()
        Dim manual As New ResourceDescriptor With {.Id = "res:m", .Kind = ResourceKind.Sftp, .Host = "10.0.0.9", .Port = 22, .UserName = "me", .RootPath = "/", .DisplayName = "NAS"}
        Dim followed As ExchangeFollowResult = ExchangeAttach.Follow(manual, "pw", Draft(), "Home PC")
        Assert.Same(manual, followed.Descriptor)
        Assert.False(followed.Changed)
        Dim link As New ResourceDescriptor With {.Id = "res:l", .Kind = ResourceKind.Sftp, .Host = "10.0.0.9", .Port = 22, .UserName = "me", .RootPath = "/", .Origin = ResourceOrigin.Link}
        Assert.Same(link, ExchangeAttach.Follow(link, "pw", Draft(), "x").Descriptor)
    End Sub

    ' --- states: absence is not authority ---

    Private Shared Function Snapshot(online As Boolean, Optional withResource As Boolean = True, Optional withDevice As Boolean = True) As ExchangeDirectorySnapshot
        Dim devices As New List(Of ExchangeDeviceRecord)
        If withDevice Then
            devices.Add(New ExchangeDeviceRecord("{}", Did, "Home PC", "fms-windows", "1", "windows", New List(Of String), If(online, "online", "offline"), ""))
        End If
        Dim resources As New List(Of ExchangeResourceRecord)
        If withResource Then resources.Add(New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "Camera", If(online, "online", "offline"), ""))
        Return New ExchangeDirectorySnapshot(5, devices, resources, New List(Of ExchangeBroadcastRecord))
    End Function

    <Fact>
    Public Sub The_four_states()
        Assert.Equal(ExchangeResourceState.Available, ExchangeAttach.StateOf(Rid, Snapshot(online:=True), True))
        Assert.Equal(ExchangeResourceState.DeviceOffline, ExchangeAttach.StateOf(Rid, Snapshot(online:=False), True))
        Assert.Equal(ExchangeResourceState.NoLongerPublished, ExchangeAttach.StateOf(Rid, Snapshot(online:=True, withResource:=False), True))
        Assert.Equal(ExchangeResourceState.NoLongerPublished, ExchangeAttach.StateOf(Rid, Snapshot(online:=True, withDevice:=False), True))   ' its device was revoked
        Assert.Equal(ExchangeResourceState.Unknown, ExchangeAttach.StateOf(Rid, Snapshot(online:=True), fullList:=False))
        Assert.Equal(ExchangeResourceState.Unknown, ExchangeAttach.StateOf(Rid, Nothing, True))
    End Sub

    <Fact>
    Public Sub An_unknown_presence_reads_as_offline_never_as_available()
        Dim devices As New List(Of ExchangeDeviceRecord) From {New ExchangeDeviceRecord("{}", Did, "d", "p", "", "", New List(Of String), "weird", "")}
        Dim resources As New List(Of ExchangeResourceRecord) From {New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "n", "weird", "")}
        Assert.Equal(ExchangeResourceState.DeviceOffline, ExchangeAttach.StateOf(Rid, New ExchangeDirectorySnapshot(1, devices, resources, New List(Of ExchangeBroadcastRecord)), True))
    End Sub

    ' --- the view as data ---

    <Fact>
    Public Sub The_view_groups_by_device_online_first_hides_this_device_and_keeps_an_unknown_kind()
        Dim own As String = "ICEiIyQlJicoKSorLC0uLw"
        Dim devices As New List(Of ExchangeDeviceRecord) From {
            New ExchangeDeviceRecord("{}", "BB", "Zeta (offline)", "fms-android", "", "", New List(Of String), "offline", ""),
            New ExchangeDeviceRecord("{}", Did, "Alpha PC", "fms-windows", "", "", New List(Of String), "online", ""),
            New ExchangeDeviceRecord("{}", own, "This PC", "fms-windows", "", "", New List(Of String), "online", "")}
        Dim resources As New List(Of ExchangeResourceRecord) From {
            New ExchangeResourceRecord("{}", Rid, Did, "sftp-share", "Camera", "online", ""),
            New ExchangeResourceRecord("{}", "R2", Did, "webdav-share", "Docs", "online", ""),
            New ExchangeResourceRecord("{}", "R3", own, "sftp-share", "Mine", "online", "")}
        Dim attached As New List(Of ResourceDescriptor) From {Draft().Descriptor}
        Dim groups As List(Of ExchangeDeviceGroup) = ExchangeDevicesModel.Build(New ExchangeDirectorySnapshot(3, devices, resources, New List(Of ExchangeBroadcastRecord)), attached, own)

        Assert.Equal({"Alpha PC", "Zeta (offline)"}, groups.Select(Function(g) g.Name).ToArray())    ' online first; this device is not listed (9 rule 1)
        Dim rows As IReadOnlyList(Of ExchangeDeviceRow) = groups(0).Rows
        Assert.Equal({"Camera", "Docs"}, rows.Select(Function(r) r.Name).ToArray())
        Assert.True(rows(0).Attached)
        Assert.True(rows(0).Supported)
        Assert.False(rows(1).Supported)                                                              ' "not supported by this version", never an error
        Assert.False(rows(1).Attached)
        Assert.Empty(groups(1).Rows)
    End Sub

    <Fact>
    Public Sub States_are_worked_out_only_for_resources_of_the_exchange()
        Dim exchange As ResourceDescriptor = Draft().Descriptor
        Dim manual As New ResourceDescriptor With {.Id = "res:m", .Kind = ResourceKind.Sftp, .Host = "h", .Port = 22, .UserName = "u", .RootPath = "/"}
        Dim states As Dictionary(Of String, ExchangeResourceState) = ExchangeDevicesModel.StatesOf({exchange, manual}, Snapshot(online:=False), True)
        Assert.Single(states)
        Assert.Equal(ExchangeResourceState.DeviceOffline, states(exchange.Id))
        Assert.Equal(ExchangeResourceState.Unknown, ExchangeDevicesModel.StatesOf({exchange}, Nothing, False)(exchange.Id))
    End Sub

    ' --- edits ---

    <Fact>
    Public Sub An_edit_keeps_the_origin_unless_it_moved_the_resource()
        Dim source As ResourceDescriptor = Draft().Descriptor
        source.TunnelHost = "relay.example.net"
        source.TunnelPort = 44022
        Dim built As New ResourceDescriptor With {.Host = source.Host, .Port = source.Port, .UserName = source.UserName, .RootPath = source.RootPath}
        ResourceOrigin.CarryOver(source, built)
        Assert.Equal(ResourceOrigin.Exchange, built.Origin)
        Assert.Equal(Rid, built.ExchangeResourceId)
        Assert.Equal("relay.example.net", built.TunnelHost)
        Assert.False(ResourceOrigin.PointsElsewhere(source, built))

        built.Host = "192.168.9.9"      ' the user typed another address: it is theirs now and the exchange lets go
        Assert.True(ResourceOrigin.PointsElsewhere(source, built))
        ResourceOrigin.Detach(built)
        Assert.Null(built.Origin)
        Assert.Null(built.ExchangeResourceId)
        Assert.Null(built.TunnelHost)
        Assert.False(ResourceOrigin.IsOfTheExchange(built))
    End Sub

    ' --- nothing here deletes ---

    <Fact>
    Public Sub No_line_of_code_in_the_attach_and_sync_files_removes_or_deletes_a_resource()
        For Each name As String In {"ExchangeAttach.vb", "ExchangeDevices.vb"}
            Dim code As String = CodeOnly(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Resources", name)))
            For Each banned As String In {"Remove", "Delete", "ResourceCommit.Remove", "Registry.Save(", ".Clear("}
                Assert.False(code.Contains(banned, StringComparison.Ordinal), name & " contains '" & banned & "': absence is not authority, removal is the user's act")
            Next
        Next
    End Sub

    Private Shared Function CodeOnly(src As String) As String
        Return String.Join(vbLf, src.Split({vbCrLf, vbLf}, StringSplitOptions.None).Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found")
    End Function

End Class
#End If
