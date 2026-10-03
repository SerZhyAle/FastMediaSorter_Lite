#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports Xunit

''' <summary>
''' 007 §3.1 / 100 §15.5: an imported document becomes resources - the same reader as the phone, no
''' connection, a whole-file refusal for a PIN-locked root (Q100-1 interim), a re-import that updates by
''' server identity instead of duplicating, and a changed fingerprint that is never taken without a yes.
''' The persistence half runs against an in-memory registry and credential store, so what is asserted is
''' the order and the content of what would be written, not DPAPI or the real hive.
''' </summary>
Public Class ResourceImportTests

    Private Const PinA As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    Private Const PinB As String = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

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

    Private NotInheritable Class MemoryRegistry
        Friend ReadOnly Values As New Dictionary(Of String, String)()
        Friend Function Read(name As String) As String
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function
        Friend Sub Write(name As String, value As String)
            Values(name) = value
        End Sub
        Friend Function Resources() As List(Of ResourceDescriptor)
            Return ResourceRegistry.Parse(Read("Resources"))
        End Function
    End Class

    ''' <summary>Marker "protection": reversible and visibly not the plain text, so a test can tell what was stored.</summary>
    Private Shared Function Protect(plain As String) As SecretProtectionResult
        Return New SecretProtectionResult(SecretProtectionStatus.Success, "protected:" & plain)
    End Function

    Private Shared Function Doc(Optional password As String = "pw", Optional pin As String = PinA,
                                Optional roots As String = "[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false}]",
                                Optional version As Integer = 1, Optional host As String = "192.0.2.1") As String
        Dim pass As String = If(password Is Nothing, "", ",""password"":""" & password & """")
        Dim fingerprint As String = If(pin Is Nothing, "", ",""hostKeyFingerprintSha256"":""" & pin & """")
        Return "{""schemaVersion"":" & version & ",""resourceName"":""Home"",""protocol"":""sftp""," &
               """accessPaths"":[{""kind"":""lan"",""host"":""" & host & """,""port"":2022},{""kind"":""portforward"",""host"":""example.org"",""port"":55001}]," &
               """username"":""fms""" & pass & fingerprint & ",""roots"":" & roots & "}"
    End Function

    Private Shared Function Plan(json As String) As FmsConfigPlan
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        Assert.True(result.Succeeded, "the fixture document must read: " & result.Failure.ToString())
        Return result.Plan
    End Function

    Private Shared Function NextId() As Func(Of String)
        Dim n As Integer
        Return Function()
                   n += 1
                   Return "id" & n.ToString()
               End Function
    End Function

    ' ------------------------------------------------------------------------- preview ----

    <Fact>
    Public Sub Preview_MapsEndpointsPinReadOnlyAndSubfoldersOntoTheDescriptor()
        Dim json As String = Doc(roots:="[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false},{""virtualPath"":""/Video""}]")
        Dim preview As ImportPreview = ResourceImport.Preview(Plan(json), New List(Of ResourceDescriptor)(), NextId())

        Assert.True(preview.CanApply)
        Assert.Equal(2, preview.Items.Count)
        Dim photos As ResourceDescriptor = preview.Items(0).Descriptor
        Assert.Equal("192.0.2.1", photos.Host)
        Assert.Equal(2022, photos.Port)
        Assert.Equal("fms", photos.UserName)
        Assert.Equal("/Photos", photos.RootPath)
        Assert.Equal(PinA, photos.PinnedFingerprint)
        Assert.False(photos.ReadOnlyHint)
        ' The producer's order is kept, so the race tries the LAN address first.
        Assert.Equal({"lan", "portforward"}, photos.Endpoints.Select(Function(e) e.Kind).ToArray())
        ' Absent readOnly is the restrictive default.
        Assert.True(preview.Items(1).Descriptor.ReadOnlyHint)
        Assert.NotEqual(photos.Id, preview.Items(1).Descriptor.Id)
        Assert.Contains("Photos", photos.DisplayName)
        Assert.All(preview.Items, Sub(i) Assert.False(i.IsUpdate))
    End Sub

    <Fact>
    Public Sub Preview_RefusesTheWholeFile_WhenAnyRootIsPinLocked()
        Dim json As String = Doc(version:=2, roots:="[{""virtualPath"":""/Open""},{""virtualPath"":""/Locked"",""accessPin"":""1234""}]")
        Dim preview As ImportPreview = ResourceImport.Preview(Plan(json), New List(Of ResourceDescriptor)(), NextId())

        Assert.False(preview.CanApply)
        Assert.Equal(ImportRefusal.PinnedRoot, preview.Refusal)
        ' Nothing is offered for the open root either: no locked root may be stored as unlocked, and no
        ' selective import is approved behaviour yet.
        Assert.Empty(preview.Items)
    End Sub

    <Fact>
    Public Sub Preview_AMissingPassword_IsNeededOnlyForANewResource()
        Dim noPassword As FmsConfigPlan = Plan(Doc(password:=Nothing))
        Assert.True(noPassword.NeedsPassword)
        Assert.True(ResourceImport.Preview(noPassword, New List(Of ResourceDescriptor)(), NextId()).NeedsPassword)

        Dim existing As New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "old", .Kind = ResourceKind.Sftp, .Host = "192.0.2.1", .Port = 2022,
                                         .UserName = "fms", .RootPath = "/Photos", .SecretHandle = "cred:old", .PinnedFingerprint = PinA}}
        ' A re-import keeps the stored credential, so it never has to ask again.
        Assert.False(ResourceImport.Preview(noPassword, existing, NextId()).NeedsPassword)
    End Sub

    <Fact>
    Public Sub Preview_ReimportUpdatesByIdentity_KeepingIdNameAndUnknownFields()
        Dim stored As ResourceDescriptor = JsonSerializer.Deserialize(Of ResourceDescriptor)(
            "{""Id"":""keep-me"",""DisplayName"":""My NAS"",""Kind"":""Sftp"",""Host"":""old-lan.invalid"",""Port"":2022,""UserName"":""fms""," &
            """RootPath"":""/Photos"",""SecretHandle"":""cred:1"",""PinnedFingerprint"":""" & PinA & """,""FromNewerBuild"":{""x"":1}}")
        ' The document names the SAME server by a different primary address: the stored alternate matches it.
        stored.Endpoints = New List(Of ResourceEndpoint) From {New ResourceEndpoint With {.Kind = "lan", .Host = "192.0.2.1", .Port = 2022}}

        Dim preview As ImportPreview = ResourceImport.Preview(Plan(Doc()), New List(Of ResourceDescriptor) From {stored}, NextId())

        Assert.Single(preview.Items)
        Dim item As ImportItem = preview.Items(0)
        Assert.True(item.IsUpdate)
        Assert.Equal("keep-me", item.Descriptor.Id)
        Assert.Equal("My NAS", item.Descriptor.DisplayName)
        Assert.Equal("192.0.2.1", item.Descriptor.Host)
        Assert.True(item.Descriptor.AdditionalFields.ContainsKey("FromNewerBuild"),
                    "a field this version does not know must survive a re-import")
        Assert.Equal(FmsPinOutcome.Unchanged, item.Pin.Outcome)
        Assert.False(preview.RequiresPinConfirmation)
    End Sub

    <Fact>
    Public Sub Preview_AChangedFingerprint_NeedsAnExplicitYes_AndIsNotTakenWithoutIt()
        Dim existing As New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "r", .Kind = ResourceKind.Sftp, .Host = "192.0.2.1", .Port = 2022,
                                         .UserName = "fms", .RootPath = "/Photos", .SecretHandle = "cred:1", .PinnedFingerprint = PinB}}
        Dim preview As ImportPreview = ResourceImport.Preview(Plan(Doc(pin:=PinA)), existing, NextId())

        Assert.True(preview.RequiresPinConfirmation)
        Assert.Equal(FmsPinOutcome.ChangedNeedsConfirmation, preview.Items(0).Pin.Outcome)
        Assert.True(preview.Items(0).Pin.ConfirmationDefaultsToCancel)
        Assert.Equal(PinB, preview.Items(0).Descriptor.PinnedFingerprint)
    End Sub

    ' --------------------------------------------------------------------------- apply ----

    Private Shared Function ApplyTo(registry As MemoryRegistry, store As MemoryStore, preview As ImportPreview,
                                    Optional password As String = Nothing, Optional confirmed As Boolean = False) As ImportApplyResult
        Return ResourceImport.Apply(preview, password, confirmed, AddressOf registry.Read, AddressOf registry.Write, store, AddressOf Protect)
    End Function

    <Fact>
    Public Sub Apply_StoresEveryRootWithItsCredentialByHandle_AndNeverThePasswordInTheRegistry()
        Dim registry As New MemoryRegistry()
        Dim store As New MemoryStore()
        Dim preview As ImportPreview = ResourceImport.Preview(
            Plan(Doc(password:="s3cret-pw", roots:="[{""virtualPath"":""/a""},{""virtualPath"":""/b""}]")),
            registry.Resources(), NextId())

        Dim result As ImportApplyResult = ApplyTo(registry, store, preview)

        Assert.Equal(ImportApplyOutcome.Applied, result.Outcome)
        Assert.Equal(2, result.Committed)
        Dim stored As List(Of ResourceDescriptor) = registry.Resources()
        Assert.Equal(2, stored.Count)
        Assert.All(stored, Sub(d)
                               Assert.StartsWith("cred:", d.SecretHandle)
                               Assert.Equal(PinA, d.PinnedFingerprint)
                           End Sub)
        Assert.DoesNotContain("s3cret-pw", registry.Read("Resources"))
        Assert.All(store.Blobs.Values, Sub(blob) Assert.Equal("protected:s3cret-pw", blob))
    End Sub

    <Fact>
    Public Sub Apply_ARefusedPreview_WritesNothing()
        Dim registry As New MemoryRegistry()
        Dim store As New MemoryStore()
        Dim preview As ImportPreview = ResourceImport.Preview(
            Plan(Doc(version:=2, roots:="[{""virtualPath"":""/Locked"",""accessPin"":""9""}]")), registry.Resources(), NextId())

        Dim result As ImportApplyResult = ApplyTo(registry, store, preview)

        Assert.Equal(ImportApplyOutcome.Refused, result.Outcome)
        Assert.Empty(registry.Values)
        Assert.Empty(store.Blobs)
    End Sub

    <Fact>
    Public Sub Apply_ADocumentWithoutAPassword_AsksForOneAndStoresNothingUntilGiven()
        Dim registry As New MemoryRegistry()
        Dim store As New MemoryStore()
        Dim preview As ImportPreview = ResourceImport.Preview(Plan(Doc(password:=Nothing)), registry.Resources(), NextId())

        Assert.Equal(ImportApplyOutcome.SecretRequired, ApplyTo(registry, store, preview).Outcome)
        Assert.Empty(registry.Values)

        Assert.Equal(ImportApplyOutcome.Applied, ApplyTo(registry, store, preview, password:="typed").Outcome)
        Assert.Contains("protected:typed", store.Blobs.Values)
    End Sub

    <Fact>
    Public Sub Apply_AChangedPin_IsTakenOnlyWithTheYes()
        Dim registry As New MemoryRegistry()
        Dim store As New MemoryStore()
        registry.Write("Resources", ResourceRegistry.Serialize(New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "r", .Kind = ResourceKind.Sftp, .Host = "192.0.2.1", .Port = 2022,
                                         .UserName = "fms", .RootPath = "/Photos", .SecretHandle = "cred:old", .PinnedFingerprint = PinB}}))
        store.PutBlob("cred:old", "protected:old")

        Dim declined As ImportPreview = ResourceImport.Preview(Plan(Doc(pin:=PinA)), registry.Resources(), NextId())
        Assert.Equal(ImportApplyOutcome.Applied, ApplyTo(registry, store, declined, confirmed:=False).Outcome)
        Assert.Equal(PinB, registry.Resources().Single().PinnedFingerprint)

        Dim accepted As ImportPreview = ResourceImport.Preview(Plan(Doc(pin:=PinA)), registry.Resources(), NextId())
        Assert.Equal(ImportApplyOutcome.Applied, ApplyTo(registry, store, accepted, confirmed:=True).Outcome)
        Assert.Equal(PinA, registry.Resources().Single().PinnedFingerprint)
        Assert.Equal("r", registry.Resources().Single().Id)
    End Sub

    <Fact>
    Public Sub Apply_ARegistryFailure_ReportsHowFarItGot_AndLeavesNoOrphanedCredentialForTheFailedOne()
        Dim registry As New MemoryRegistry()
        Dim store As New MemoryStore()
        Dim preview As ImportPreview = ResourceImport.Preview(
            Plan(Doc(roots:="[{""virtualPath"":""/a""},{""virtualPath"":""/b""}]")), registry.Resources(), NextId())
        Dim writes As Integer
        Dim result As ImportApplyResult = ResourceImport.Apply(preview, Nothing, False, AddressOf registry.Read,
            Sub(name, value)
                writes += 1
                If writes = 2 Then Throw New System.IO.IOException("disk full")
                registry.Write(name, value)
            End Sub, store, AddressOf Protect)

        Assert.Equal(ImportApplyOutcome.Failed, result.Outcome)
        Assert.Equal(1, result.Committed)
        Assert.Equal(ResourceCommitOutcome.RegistryFailed, result.Failure)
        Assert.Single(registry.Resources())
        ' Only the stored resource's credential remains.
        Assert.Single(store.Blobs)
    End Sub

End Class
#End If
