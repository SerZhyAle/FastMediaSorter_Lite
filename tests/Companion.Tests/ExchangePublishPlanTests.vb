Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 170 rules 1-5 and 8, the pure half: what the producer wants on the server for a given worker status, the user's two choices, the
''' server's public endpoint and the kept ids - and, when nothing, why not. No I/O, no clock.
''' </summary>
Public Class ExchangePublishPlanTests
    <Fact>
    Public Sub Protected_and_public_roots_publish_only_with_verified_worker_enforcement()
        Dim worker As WorkerStatus = Status("Public", "Private")
        Dim input As ExchangePublishInputs = Inputs(worker)
        input.HasPin = Function(host) host.EndsWith("Private", StringComparison.Ordinal)
        Assert.Equal(ExchangePublishBlock.PinLocked, ExchangePublishPlan.Build(input).Block)
        worker.ResourcePinSupported = True
        Assert.Equal(ExchangePublishBlock.PinLocked, ExchangePublishPlan.Build(input).Block)
        worker.Roots(1).pinProtected = True
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(input)
        Assert.Equal(ExchangePublishBlock.None, plan.Block)
        Assert.Equal(2, plan.Shares.Count)
        Assert.False(plan.Shares(0).PinProtected)
        Assert.True(plan.Shares(1).PinProtected)
        Dim json As String = Nothing
        Dim share As ExchangePlannedShare = plan.Shares(1)
        Assert.True(ExchangeResourceRecordBuilder.TryBuild(share.ResourceId, Device, share.Name, share.AccessJson, "", Nothing, DateTime.UtcNow, json, share.PinProtected))
        Assert.Contains("""protection"":{""scheme"":""pin-v1""}", json)
        Assert.DoesNotContain("accessPin", json)
    End Sub

    <Fact>
    Public Sub Adding_original_PIN_revokes_the_previous_outsider_link()
        Dim worker As WorkerStatus = Status("Photos")
        worker.ResourcePinSupported = True
        worker.Roots(0).pinProtected = True
        Dim oldEntry As New ExchangeProducerEntry("EBESExQVFhcYGRobHB0eHw", "q3Vb7YtK0xP2mN9sLfR4wA")
        Dim state As ExchangeProducerState = ExchangeProducerState.Empty.WithEntry("Photos", oldEntry)
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(worker, state:=state))
        Assert.Equal(oldEntry.ResourceId, plan.Shares(0).ResourceId)
        Assert.Equal("", plan.Shares(0).ShareId)
        Assert.Equal("", plan.State.Get("Photos").ShareId)
        Assert.Equal("", ReadCode(plan.Shares(0).AccessJson, "Photos").ShareId)
    End Sub

    Private Const Fp As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Device As String = "AAECAwQFBgcICQoLDA0ODw"

    Private Shared Function Status(ParamArray names As String()) As WorkerStatus
        Dim roots As New List(Of ShareFolder)
        For Each n As String In names
            roots.Add(New ShareFolder With {.name = n, .hostPath = "C:\__fms_test__\" & n})
        Next
        Return New WorkerStatus With {
            .Running = True, .ListenPort = 2222, .Username = "fms", .Password = "Zk7PmQ2wXr9TzS4vGnHb3JdL", .Fingerprint = Fp, .Roots = roots,
            .Reachability = New WorkerReachability With {.LanAddress = "192.168.1.50", .Ipv6Address = "2001:db8::50", .ExternalHost = "203.0.113.9", .ExternalPort = 2222}}
    End Function

    Private Shared Function Inputs(status As WorkerStatus, Optional publish As Boolean = True, Optional state As ExchangeProducerState = Nothing) As ExchangePublishInputs
        Dim seq As Integer = 0
        Return New ExchangePublishInputs With {
            .Publish = publish, .Status = status, .DeviceId = Device, .PublicEndpoint = "203.0.113.5:22022", .LanFallback = "10.0.0.2",
            .State = If(state, ExchangeProducerState.Empty),
            .NewId = Function()
                         seq += 1
                         Return New String(ChrW(AscW("A"c) + seq), 21) & "A"
                     End Function}
    End Function

    Private Shared Function ReadCode(accessJson As String, name As String) As FmsSftpCode
        Dim info As ExchangeAccessInfo = Nothing
        Assert.True(ExchangeResourceAccess.TryRead(accessJson, name, info))
        Return info.Code
    End Function

    ' --- rule 1: off by default ---

    <Fact>
    Public Sub With_publication_off_nothing_is_wanted_and_the_worker_is_not_even_looked_at()
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Nothing, publish:=False))
        Assert.Equal(ExchangePublishBlock.Off, plan.Block)
        Assert.Empty(plan.Shares)
    End Sub

    ' --- why not ---

    <Fact>
    Public Sub A_worker_that_is_not_serving_or_has_nothing_to_publish_blocks_with_ServerDown()
        Assert.Equal(ExchangePublishBlock.ServerDown, ExchangePublishPlan.Build(Inputs(Nothing)).Block)
        Dim stopped As WorkerStatus = Status("Photos")
        stopped.Running = False
        Assert.Equal(ExchangePublishBlock.ServerDown, ExchangePublishPlan.Build(Inputs(stopped)).Block)
        Dim noPort As WorkerStatus = Status("Photos")
        noPort.ListenPort = 0
        Assert.Equal(ExchangePublishBlock.ServerDown, ExchangePublishPlan.Build(Inputs(noPort)).Block)
        Dim noPassword As WorkerStatus = Status("Photos")
        noPassword.Password = ""
        Assert.Equal(ExchangePublishBlock.ServerDown, ExchangePublishPlan.Build(Inputs(noPassword)).Block)
        Dim badPin As WorkerStatus = Status("Photos")
        badPin.Fingerprint = "SHA256:short"
        Assert.Equal(ExchangePublishBlock.ServerDown, ExchangePublishPlan.Build(Inputs(badPin)).Block)
    End Sub

    <Fact>
    Public Sub No_shares_is_its_own_verdict()
        Assert.Equal(ExchangePublishBlock.NoShares, ExchangePublishPlan.Build(Inputs(Status())).Block)
        Dim nameless As WorkerStatus = Status()
        nameless.Roots.Add(New ShareFolder With {.name = "", .hostPath = "C:\x"})
        Assert.Equal(ExchangePublishBlock.NoShares, ExchangePublishPlan.Build(Inputs(nameless)).Block)
    End Sub

    <Fact>
    Public Sub A_PIN_locked_root_stops_the_publication_of_every_share()
        Dim i As ExchangePublishInputs = Inputs(Status("Photos", "Videos"))
        i.HasPin = Function(hostPath) hostPath.EndsWith("Videos", StringComparison.Ordinal)
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(i)
        Assert.Equal(ExchangePublishBlock.PinLocked, plan.Block)
        Assert.Empty(plan.Shares)   ' Photos is not published either: the credential opens the whole server regardless (Q170-3)
    End Sub

    <Fact>
    Public Sub No_address_to_hand_over_blocks_with_NoAddress()
        Dim st As WorkerStatus = Status("Photos")
        st.Reachability = New WorkerReachability()
        Dim i As ExchangePublishInputs = Inputs(st)
        i.LanFallback = ""
        Assert.Equal(ExchangePublishBlock.NoAddress, ExchangePublishPlan.Build(i).Block)
    End Sub

    ' --- rule 2: a resource per share, ids kept ---

    <Fact>
    Public Sub Each_share_is_a_resource_with_an_id_that_is_kept_and_a_root_of_its_own_name()
        Dim first As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos")))
        Assert.Equal(ExchangePublishBlock.None, first.Block)
        Assert.Equal({"Photos", "Videos"}, first.Shares.Select(Function(s) s.Name).ToArray())
        Assert.All(first.Shares, Sub(s) Assert.True(ExchangeIds.IsValidId(s.ResourceId)))
        Assert.NotEqual(first.Shares(0).ResourceId, first.Shares(1).ResourceId)
        Assert.Equal("/Photos", ExchangeResourceAccessRoot(first.Shares(0), "Photos"))

        ' the second pass is given the first one's state: the very same ids, nothing new made
        Dim second As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos"), state:=first.State))
        Assert.Equal(first.Shares.Select(Function(s) s.ResourceId).ToArray(), second.Shares.Select(Function(s) s.ResourceId).ToArray())

        ' a share that comes later gets a new id and the old ones stay
        Dim third As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos", "Music"), state:=second.State))
        Assert.Equal(first.Shares(0).ResourceId, third.Find("Photos").ResourceId)
        Assert.Equal(3, third.State.Entries.Count)
    End Sub

    Private Shared Function ExchangeResourceAccessRoot(s As ExchangePlannedShare, name As String) As String
        Dim info As ExchangeAccessInfo = Nothing
        Assert.True(ExchangeResourceAccess.TryRead(s.AccessJson, name, info))
        Return info.Root
    End Function

    <Fact>
    Public Sub A_rename_is_a_new_resource()
        Dim before As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos")))
        Dim next_ As ExchangePublishInputs = Inputs(Status("Pictures"), state:=before.State)
        next_.NewId = Function() "CCCCCCCCCCCCCCCCCCCCCA"     ' what a fresh generator would hand out for a share seen for the first time
        Dim after As ExchangePublishPlan = ExchangePublishPlan.Build(next_)
        Assert.NotEqual(before.Shares(0).ResourceId, after.Shares(0).ResourceId)
    End Sub

    ' --- rule 3: the access ---

    <Fact>
    Public Sub Access_is_an_FMSSFTP1_code_with_the_workers_credentials_and_the_hosts_in_order()
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos")))
        Dim code As FmsSftpCode = ReadCode(plan.Shares(0).AccessJson, "Photos")
        Assert.Equal({"192.168.1.50", "2001:db8::50", "203.0.113.9"}, code.Hosts.ToArray())
        Assert.Equal(2222, code.Port)
        Assert.Equal("fms", code.Username)
        Assert.Equal("Zk7PmQ2wXr9TzS4vGnHb3JdL", code.Password)
        Assert.Equal(Fp, code.Fingerprint)
        Assert.Equal("", code.Exchange)
        Dim text As String = Nothing
        Assert.True(code.TryBuild(text))
        Assert.StartsWith("FMSSFTP1:", text, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub Under_LAN_only_no_address_reachable_from_outside_travels()
        Dim i As ExchangePublishInputs = Inputs(Status("Photos"))
        i.IncludeExternal = False
        Dim code As FmsSftpCode = ReadCode(ExchangePublishPlan.Build(i).Shares(0).AccessJson, "Photos")
        Assert.Equal({"192.168.1.50"}, code.Hosts.ToArray())
    End Sub

    <Fact>
    Public Sub An_external_forward_on_another_port_cannot_be_written_and_is_left_out()
        Dim st As WorkerStatus = Status("Photos")
        st.Reachability.ExternalPort = 40222   ' a descriptor has ONE port: the forward would be dialled on the listen port, which is wrong
        Dim code As FmsSftpCode = ReadCode(ExchangePublishPlan.Build(Inputs(st)).Shares(0).AccessJson, "Photos")
        Assert.DoesNotContain("203.0.113.9", code.Hosts.ToArray())
    End Sub

    <Fact>
    Public Sub CGNAT_and_a_missing_LAN_address_follow_the_same_rules_as_the_QR()
        Dim st As WorkerStatus = Status("Photos")
        st.Reachability.IsCgnat = True
        st.Reachability.LanAddress = ""
        Dim code As FmsSftpCode = ReadCode(ExchangePublishPlan.Build(Inputs(st)).Shares(0).AccessJson, "Photos")
        Assert.Equal({"10.0.0.2", "2001:db8::50"}, code.Hosts.ToArray())   ' the fallback LAN address, the IPv6 escape hatch, no CGNAT forward
    End Sub

    <Fact>
    Public Sub The_hosts_are_the_selection_ShareConfigBuilder_makes()
        Dim st As WorkerStatus = Status("Photos")
        Dim fromPlan As List(Of String) = ExchangePublishPlan.SelectHosts(st, includeExternal:=True, lanFallback:="")
        Dim built As ShareConfigResult = ShareConfigBuilder.Build(st, includeExternal:=True)
        Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(built.ConfigJson)
            Dim fromExport As List(Of String) = doc.RootElement.GetProperty("accessPaths").EnumerateArray().Select(Function(p) p.GetProperty("host").GetString()).ToList()
            Assert.Equal(fromExport, fromPlan)
        End Using
    End Sub

    ' --- rule 8: outsider links ---

    <Fact>
    Public Sub A_share_with_a_link_is_an_FMSSFTP2_code_carrying_the_servers_endpoint_and_the_share_id()
        Dim sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"
        Dim state As ExchangeProducerState = ExchangeProducerState.Empty.WithEntry("Photos", New ExchangeProducerEntry("EBESExQVFhcYGRobHB0eHw", sid))
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos"), state:=state))
        Dim share As ExchangePlannedShare = plan.Shares(0)
        Assert.Equal(sid, share.ShareId)
        Dim code As FmsSftpCode = ReadCode(share.AccessJson, "Photos")
        Assert.Equal("203.0.113.5:22022", code.Exchange)
        Assert.Equal(sid, code.ShareId)
        Dim text As String = Nothing
        Assert.True(code.TryBuild(text))
        Assert.StartsWith("FMSSFTP2:", text, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub A_server_endpoint_that_is_loopback_or_empty_never_goes_into_a_code()
        Dim sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"
        Dim state As ExchangeProducerState = ExchangeProducerState.Empty.WithEntry("Photos", New ExchangeProducerEntry("EBESExQVFhcYGRobHB0eHw", sid))
        For Each endpoint As String In {"", "127.0.0.1:22022", "localhost:22022", "[::1]:22022", "not-an-endpoint"}
            Dim i As ExchangePublishInputs = Inputs(Status("Photos"), state:=state)
            i.PublicEndpoint = endpoint
            Dim share As ExchangePlannedShare = ExchangePublishPlan.Build(i).Shares(0)
            Assert.Equal("", share.ShareId)
            Assert.Equal("", ReadCode(share.AccessJson, "Photos").Exchange)
        Next
    End Sub

    ' --- rule 7: the third-party port rides on one resource ---

    <Fact>
    Public Sub The_third_party_port_is_asked_for_on_one_resource_only_and_only_when_chosen()
        Dim off As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos")))
        Assert.All(off.Shares, Sub(s) Assert.False(s.PublicPortRequest.HasValue))
        Assert.Equal("", off.CarrierName)

        Dim i As ExchangePublishInputs = Inputs(Status("Photos", "Videos"))
        i.ThirdPartyPort = True
        Dim on_ As ExchangePublishPlan = ExchangePublishPlan.Build(i)
        Assert.Equal("Photos", on_.CarrierName)
        Assert.Equal(0, on_.Find("Photos").PublicPortRequest)       ' "any"
        Assert.False(on_.Find("Videos").PublicPortRequest.HasValue)
    End Sub

    <Fact>
    Public Sub A_granted_port_is_asked_for_again_on_the_same_carrier()
        Dim state As ExchangeProducerState = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos"))).State.WithCarrier("Videos", 40007)
        Dim i As ExchangePublishInputs = Inputs(Status("Photos", "Videos"), state:=state)
        i.ThirdPartyPort = True
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(i)
        Assert.Equal("Videos", plan.CarrierName)
        Assert.Equal(40007, plan.Find("Videos").PublicPortRequest)
        Assert.False(plan.Find("Photos").PublicPortRequest.HasValue)
    End Sub

    <Fact>
    Public Sub A_carrier_that_left_the_list_is_replaced_by_the_first_share()
        Dim state As ExchangeProducerState = ExchangePublishPlan.Build(Inputs(Status("Photos", "Videos"))).State.WithCarrier("Videos", 40007)
        Dim i As ExchangePublishInputs = Inputs(Status("Photos"), state:=state)
        i.ThirdPartyPort = True
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(i)
        Assert.Equal("Photos", plan.CarrierName)
        Assert.Equal(40007, plan.Find("Photos").PublicPortRequest)   ' the remembered number is asked for again: a router rule for it stays valid
    End Sub

    <Fact>
    Public Sub A_plan_never_prints_a_member()
        Dim plan As ExchangePublishPlan = ExchangePublishPlan.Build(Inputs(Status("Photos")))
        Assert.DoesNotContain("Zk7Pm", plan.ToString(), StringComparison.Ordinal)
        Assert.DoesNotContain("Zk7Pm", plan.Shares(0).ToString(), StringComparison.Ordinal)
        Assert.DoesNotContain(plan.Shares(0).ResourceId, plan.State.ToString(), StringComparison.Ordinal)
    End Sub

End Class
