#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' Ticket 171 rule 7 (phase C4), the reading half: a `.fmscfg` whose access paths end with a `rendezvousTunnel` path, and an `FMSSFTP2` text,
''' become a plan whose tunnel sits BESIDE the endpoints, never among them; and where the exchange client features are not offered the kind
''' stays unknown - skipped, never dialled, never a refusal - so the document reads exactly as it did before.
''' </summary>
<Collection("Exchange viewer gate")>
Public Class ExchangeLinkImportTests

    Private Const Pin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const Sid As String = "q3Vb7YtK0xP2mN9sLfR4wA"

    Private Shared Function Doc(tunnel As String) As String
        Return "{""schemaVersion"":2,""resourceName"":""Home"",""protocol"":""sftp""," &
               """accessPaths"":[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}" & If(tunnel.Length > 0, "," & tunnel, "") & "]," &
               """username"":""fms"",""password"":""pw"",""hostKeyFingerprintSha256"":""" & Pin & """," &
               """roots"":[{""virtualPath"":""/Photos"",""label"":""Photos""}]}"
    End Function

    Private Shared Function Tunnel(Optional host As String = "exchange.example.net", Optional port As String = "22022", Optional sid As String = Sid) As String
        Return "{""kind"":""rendezvousTunnel"",""host"":""" & host & """,""port"":" & port & ",""shareId"":""" & sid & """}"
    End Function

    Private Shared Function Read(json As String, offered As Boolean) As FmsConfigReadResult
        Dim before As Func(Of Boolean) = FmsTunnelPaths.ReadsTunnelKind
        Try
            FmsTunnelPaths.ReadsTunnelKind = Function() offered
            Return FmsConfigReader.ReadText(json)
        Finally
            FmsTunnelPaths.ReadsTunnelKind = before
        End Try
    End Function

    <Fact>
    Public Sub A_tunnel_path_is_kept_beside_the_endpoints_never_among_them()
        Dim r As FmsConfigReadResult = Read(Doc(Tunnel()), True)
        Assert.NotNull(r.Plan)
        Assert.NotNull(r.Plan.Tunnel)
        Assert.Equal("exchange.example.net", r.Plan.Tunnel.Host)
        Assert.Equal(22022, r.Plan.Tunnel.Port)
        Assert.Equal(Sid, r.Plan.Tunnel.ShareId)
        Assert.Single(r.Plan.Endpoints)                                  ' the lan one only: the exchange is not an SSH address
        Assert.DoesNotContain(r.Plan.Endpoints, Function(e) e.Host = "exchange.example.net")
        Assert.DoesNotContain(Sid, r.Plan.Tunnel.ToString(), StringComparison.Ordinal)     ' a capability is never printed
    End Sub

    <Fact>
    Public Sub Where_the_features_are_not_offered_the_kind_is_unknown_and_the_document_reads_as_before()
        Dim withTunnel As FmsConfigReadResult = Read(Doc(Tunnel()), False)
        Dim without As FmsConfigReadResult = Read(Doc(""), False)
        Assert.NotNull(withTunnel.Plan)
        Assert.Null(withTunnel.Plan.Tunnel)
        Assert.Equal(without.Plan.Endpoints.Select(Function(e) e.Key()).ToList(), withTunnel.Plan.Endpoints.Select(Function(e) e.Key()).ToList())
        Assert.Equal(without.Plan.Roots.Count, withTunnel.Plan.Roots.Count)
    End Sub

    <Theory>
    <InlineData("", "22022", Sid)>
    <InlineData("exchange.example.net", "0", Sid)>
    <InlineData("exchange.example.net", "70000", Sid)>
    <InlineData("exchange.example.net", "22022", "short")>
    <InlineData("exchange.example.net", "22022", "q3Vb7YtK0xP2mN9sLfR4w+")>
    <InlineData("user@exchange.example.net", "22022", Sid)>
    <InlineData("exchange/example", "22022", Sid)>
    Public Sub A_tunnel_path_that_cannot_be_used_is_skipped_and_the_direct_paths_stay_importable(host As String, port As String, shareId As String)
        Dim r As FmsConfigReadResult = Read(Doc(Tunnel(host, port, shareId)), True)
        Assert.NotNull(r.Plan)                                           ' never a refusal of the file
        Assert.Null(r.Plan.Tunnel)
        Assert.Single(r.Plan.Endpoints)
    End Sub

    <Fact>
    Public Sub Only_the_first_tunnel_path_counts()
        Dim r As FmsConfigReadResult = Read(Doc(Tunnel("first.example.net") & "," & Tunnel("second.example.net")), True)
        Assert.Equal("first.example.net", r.Plan.Tunnel.Host)
    End Sub

    ' ------------------------------------------------------------------ an FMSSFTP text ----

    Private Shared Function Code(Optional exchange As String = "exchange.example.net:22022", Optional shareId As String = Sid) As String
        Dim c As New FmsSftpCode(New String() {"192.0.2.1", "203.0.113.9"}, 2022, "fms", "pw", Pin, exchange, shareId, False)
        Dim text As String = Nothing
        Assert.True(c.TryBuild(text))
        Return text
    End Function

    <Fact>
    Public Sub A_pasted_code_is_a_plan_with_a_server_root_and_no_other_member()
        Dim text As String = Code()
        Assert.True(FmsSftpPlans.LooksLikeCode(text))
        Assert.True(FmsSftpPlans.LooksLikeCode("   " & text))
        Assert.False(FmsSftpPlans.LooksLikeCode("{""schemaVersion"":1}"))
        Dim before As Func(Of Boolean) = FmsTunnelPaths.ReadsTunnelKind
        Dim r As FmsConfigReadResult = FmsSftpPlans.Read(text)
        Assert.NotNull(r.Plan)
        Assert.Equal("fms", r.Plan.UserName)
        Assert.Equal(Pin, r.Plan.PinnedFingerprint)
        Assert.Equal(2, r.Plan.Endpoints.Count)
        Assert.All(r.Plan.Endpoints, Sub(e) Assert.Equal(2022, e.Port))
        Assert.Single(r.Plan.Roots)
        Assert.Equal("/", r.Plan.Roots(0).VirtualPath)
        Assert.Same(before, FmsTunnelPaths.ReadsTunnelKind)
    End Sub

    <Fact>
    Public Sub A_code_with_a_tunnel_keeps_it_only_where_the_features_are_offered()
        If ExchangeFeatures.Offered Then Return
        Dim text As String = Code()
        Dim saved As String = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, EnvironmentVariableTarget.Process)
        Try
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, Nothing, EnvironmentVariableTarget.Process)
            Assert.Null(FmsSftpPlans.Read(text).Plan.Tunnel)
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1", EnvironmentVariableTarget.Process)
            Dim t As FmsTunnelPath = FmsSftpPlans.Read(text).Plan.Tunnel
            Assert.NotNull(t)
            Assert.Equal("exchange.example.net", t.Host)
            Assert.Equal(22022, t.Port)
            Assert.Equal(Sid, t.ShareId)
        Finally
            Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, saved, EnvironmentVariableTarget.Process)
        End Try
    End Sub

    <Fact>
    Public Sub A_refused_code_is_a_failure_never_a_half_read_plan()
        Dim r As FmsConfigReadResult = FmsSftpPlans.Read("FMSSFTP2:h=192.0.2.1&p=2022&u=fms&pw=pw&bogus=1")
        Assert.Null(r.Plan)
        Assert.NotEqual(FmsConfigFailure.None, r.Failure)
        Assert.Null(FmsSftpPlans.Read("not a code").Plan)
        Assert.Null(FmsSftpPlans.Read("").Plan)
        Assert.Null(FmsSftpPlans.Read(Nothing).Plan)
    End Sub

    Private NotInheritable Class MemoryStore
        Implements IResourceSecretStore

        Friend ReadOnly Blobs As New Dictionary(Of String, String)()
        Friend Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub
        Friend Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(Blobs.TryGetValue(handle, v), v, "")
        End Function
        Friend Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub
        Friend Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    Private Shared Function Protect(plain As String) As SecretProtectionResult
        Return New SecretProtectionResult(SecretProtectionStatus.Success, "protected:" & plain)
    End Function

    <Fact>
    Public Sub A_document_with_a_tunnel_becomes_a_link_and_its_share_id_is_stored_only_as_a_protected_blob()
        Dim plan As FmsConfigPlan = Read(Doc(Tunnel()), True).Plan
        Dim n As Integer
        Dim preview As ImportPreview = ResourceImport.Preview(plan, New List(Of ResourceDescriptor)(), Function()
                                                                                                           n += 1
                                                                                                           Return "res:link" & n
                                                                                                       End Function)
        Assert.True(preview.CanApply)
        Dim d As ResourceDescriptor = preview.Items.First().Descriptor
        Assert.Equal(ResourceOrigin.Link, d.Origin)
        Assert.Equal("exchange.example.net", d.TunnelHost)
        Assert.Equal(22022, d.TunnelPort)
        Assert.True(String.IsNullOrEmpty(d.TunnelSecretHandle))          ' staged by Apply, not by Preview

        Dim hive As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim store As New MemoryStore()
        Dim result As ImportApplyResult = ResourceImport.Apply(preview, Nothing, False,
                                                               Function(name) If(hive.ContainsKey(name), hive(name), ""),
                                                               Sub(name, value) hive(name) = value, store, AddressOf Protect)
        Assert.Equal(ImportApplyOutcome.Applied, result.Outcome)

        Dim stored As ResourceDescriptor = ResourceRegistry.Parse(hive("Resources")).Single()
        Assert.False(String.IsNullOrEmpty(stored.TunnelSecretHandle))
        Assert.Equal("protected:" & Sid, store.GetBlob(stored.TunnelSecretHandle))
        Assert.DoesNotContain(Sid, hive("Resources"), StringComparison.Ordinal)        ' the registry JSON holds the handle, never the id
        Assert.Equal(ResourceOrigin.Link, stored.Origin)
    End Sub

    <Fact>
    Public Sub A_failed_protection_stores_neither_the_registry_row_nor_a_blob()
        Dim plan As FmsConfigPlan = Read(Doc(Tunnel()), True).Plan
        Dim preview As ImportPreview = ResourceImport.Preview(plan, New List(Of ResourceDescriptor)(), Function() "res:link1")
        Dim hive As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim store As New MemoryStore()
        Dim result As ImportApplyResult = ResourceImport.Apply(preview, Nothing, False,
                                                               Function(name) If(hive.ContainsKey(name), hive(name), ""),
                                                               Sub(name, value) hive(name) = value, store,
                                                               Function(s) New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, ""))
        Assert.Equal(ImportApplyOutcome.Failed, result.Outcome)
        Assert.Empty(store.Blobs)
        Assert.False(hive.ContainsKey("Resources") AndAlso hive("Resources").Contains("res:link1"))
    End Sub

End Class
#End If