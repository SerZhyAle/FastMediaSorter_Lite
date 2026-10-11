#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 187 T-3: a resource attached through one of the viewer's own ENTRIES opens its tunnel with THAT entry's token and address - the
''' binding is the entry and the resource id together - and an entry that is gone, or whose token is gone, is "unavailable", never another
''' account's token and never a fallback to the Share Manager's account. The registry carries the entry id additively.
''' </summary>
Public Class ExchangeEntryTunnelTests
    Implements IDisposable

    Private ReadOnly _root As String = Path.Combine(Path.GetTempPath(), "fms-entry-tunnel-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly _settings As New ExchangeStoresTests.MemorySettings()

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(_root) Then Directory.Delete(_root, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function Store() As ExchangeEntryStore
        Return New ExchangeEntryStore(_settings, _root, New PlainCipher())
    End Function

    Private NotInheritable Class PlainCipher
        Implements IExchangeSecretCipher

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            Return CType(plain.Clone(), Byte())
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            Return CType(blob.Clone(), Byte())
        End Function
    End Class

    Private NotInheritable Class NeverTransport
        Implements IExchangeTransport

        Public Calls As Integer

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            Calls += 1
            Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "not reachable in this test")
        End Function
    End Class

    Private NotInheritable Class NeverProbe
        Implements IExchangeCertificateProbe

        Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeCertificateProbe.ProbeAsync
            Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "not reachable in this test")
        End Function
    End Class

    <Fact>
    Public Async Function An_entry_that_is_gone_is_unavailable_and_nothing_connects() As Task
        Dim transport As New NeverTransport()
        Dim source As New ExchangeEntryTunnelSource("R1", ExchangeEntryStore.NewEntryId(), Store(), transport, New NeverProbe(), "1.0")
        Dim open As ExchangeTunnelOpen = Await source.OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.Unavailable, open.Failure)
        Assert.Equal(0, transport.Calls)
    End Function

    <Fact>
    Public Async Function An_entry_with_no_token_is_unavailable_and_the_other_entrys_token_is_not_used() As Task
        Dim s As ExchangeEntryStore = Store()
        Dim a As ExchangeEntry = s.Propose("203.0.113.5:22022", "alice", "", "PC (viewer)")
        Dim b As ExchangeEntry = s.Propose("203.0.113.5:22022", "bob", "", "PC (viewer)")
        Assert.True(s.Upsert(a))
        Assert.True(s.Upsert(b))
        Assert.True(s.TokenStoreFor(b.Id).Save(b.DeviceId, New String("T"c, 42) & "A"))     ' only bob has a token
        Dim transport As New NeverTransport()
        Dim source As New ExchangeEntryTunnelSource("R1", a.Id, s, transport, New NeverProbe(), "1.0")
        Dim open As ExchangeTunnelOpen = Await source.OpenAsync(CancellationToken.None)
        Assert.Equal(ExchangeTunnelFailure.Unavailable, open.Failure)
        Assert.Equal(0, transport.Calls)
    End Function

    <Fact>
    Public Sub The_registry_binds_an_attached_resource_to_the_entry_and_the_resource_id_together()
        Dim one As New ResourceDescriptor With {.Id = "a", .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "R1", .ExchangeEntryId = "entry-1"}
        Dim two As New ResourceDescriptor With {.Id = "b", .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "R1", .ExchangeEntryId = "entry-2"}
        Dim legacy As New ResourceDescriptor With {.Id = "c", .Origin = ResourceOrigin.Exchange, .ExchangeResourceId = "R1"}
        Dim all As New List(Of ResourceDescriptor) From {one, two, legacy}
        Assert.Same(one, ResourceOrigin.FindByExchangeId(all, "R1", "entry-1"))
        Assert.Same(two, ResourceOrigin.FindByExchangeId(all, "R1", "entry-2"))
        Assert.Same(legacy, ResourceOrigin.FindByExchangeId(all, "R1"))                    ' what no entry claims: the Share Manager's older account
        Assert.Null(ResourceOrigin.FindByExchangeId(all, "R1", "entry-3"))
        ' a null member is not written: a resource of the older shape serializes exactly as it did
        Assert.DoesNotContain("ExchangeEntryId", System.Text.Json.JsonSerializer.Serialize(legacy))
        Assert.Contains("entry-1", System.Text.Json.JsonSerializer.Serialize(one))
        ' an edit that keeps the resource where it points carries the entry along; letting go clears it
        Dim built As New ResourceDescriptor()
        ResourceOrigin.CarryOver(one, built)
        Assert.Equal("entry-1", built.ExchangeEntryId)
        ResourceOrigin.Detach(built)
        Assert.Null(built.ExchangeEntryId)
    End Sub

    <Fact>
    Public Sub An_attach_through_an_entry_carries_the_entry_id_and_the_unclaimed_form_stays_as_it_was()
        Dim json As String = Nothing
        Dim code As New FmsSftpCode({"192.168.1.23"}, 2022, "fms", "k7PmQ2wXr9TzS4vGnHb3JdLe", "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk", "", "", False)
        Assert.True(ExchangeResourceAccess.TryBuild(code, "/Camera", json))
        Dim through As ExchangeAttachDraft = ExchangeAttach.BuildDraft("EBESExQVFhcYGRobHB0eHw", "AAECAwQFBgcICQoLDA0ODw", "Host", "Camera", json, Function() "res:1", "entry-1")
        Assert.Equal("entry-1", through.Descriptor.ExchangeEntryId)
        Dim older As ExchangeAttachDraft = ExchangeAttach.BuildDraft("EBESExQVFhcYGRobHB0eHw", "AAECAwQFBgcICQoLDA0ODw", "Host", "Camera", json, Function() "res:2")
        Assert.Null(older.Descriptor.ExchangeEntryId)
    End Sub
End Class
#End If
