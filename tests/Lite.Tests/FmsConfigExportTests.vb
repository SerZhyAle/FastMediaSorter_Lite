#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports Xunit

''' <summary>
''' Ticket 118 Ф-3: the viewer's own .fmscfg producer. The canonical-vector discipline of
''' ShareConfigCanonicalVectorTests, applied to a second producer: the exact bytes of one export
''' (with the clock injected), the adaptive version rule, the honest access-path list, the refusals
''' (SMB, unpinned, no address, bad root - each its own outcome, never an exception), the "without
''' password" meaning, JSON escaping, and a full round trip through the REAL reader and import into
''' an in-memory registry. Nothing here touches DPAPI or the real hive.
''' </summary>
Public Class FmsConfigExportTests

    Private Const PinA As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

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

    ''' <summary>The blob names its plain text; the unprotect half of the seam reads it back.</summary>
    Private Shared Function Unprotect(blob As String) As SecretProtectionResult
        If blob.StartsWith("blob:", StringComparison.Ordinal) Then
            Return New SecretProtectionResult(SecretProtectionStatus.Success, blob.Substring(5))
        End If
        Return New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, "")
    End Function

    Private Shared Function Protect(plain As String) As SecretProtectionResult
        Return New SecretProtectionResult(SecretProtectionStatus.Success, "blob:" & plain)
    End Function

    Private Const CreatedAt As String = "2026-10-04T12:00:00Z"
    Private Shared ReadOnly InjectedClock As New Date(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc)

    Private Shared Function PinnedResource(Optional name As String = "Home NAS",
                                           Optional includeSubfolders As Boolean? = Nothing,
                                           Optional endpoints As List(Of ResourceEndpoint) = Nothing) As ResourceDescriptor
        Dim d As New ResourceDescriptor With {
            .Id = "res:test", .Kind = ResourceKind.Sftp, .DisplayName = name,
            .Host = "192.168.1.5", .Port = 2022, .UserName = "fms", .RootPath = "/Photos",
            .ReadOnlyHint = False, .PinnedFingerprint = PinA, .IncludeSubfolders = includeSubfolders}
        If endpoints IsNot Nothing Then d.Endpoints = endpoints
        Return d
    End Function

    Private Shared Function LanPlusForward() As List(Of ResourceEndpoint)
        Return New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.5", .Port = 2022},
            New ResourceEndpoint With {.Kind = "portforward", .Host = "home.example.org", .Port = 55001}}
    End Function

    Private Shared Function ExportOk(d As ResourceDescriptor, Optional includePassword As Boolean = True,
                                     Optional store As MemoryStore = Nothing) As FmsExportResult
        Dim secrets As IResourceSecretStore = If(store, New MemoryStore())
        Dim result As FmsExportResult = FmsConfigExport.Export(d, includePassword, secrets, InjectedClock, AddressOf Unprotect)
        Assert.True(result.Succeeded, "the fixture resource must export: " & result.Refusal.ToString() & "/" & result.SelfCheckFailure.ToString())
        Return result
    End Function

    ' ---------------------------------------------------------------------- bytes ----

    <Fact>
    Public Sub Export_PinsTheExactBytes_FieldOrderAndPasswordIncluded()
        Dim store As New MemoryStore()
        store.Blobs("cred:1") = "blob:pw"
        Dim d As ResourceDescriptor = PinnedResource(endpoints:=LanPlusForward())
        d.SecretHandle = "cred:1"

        Dim result As FmsExportResult = FmsConfigExport.Export(d, True, store, InjectedClock, AddressOf Unprotect)
        Assert.True(result.Succeeded)

        Dim expected As String = "{""schemaVersion"":1,""resourceName"":""Home NAS"",""protocol"":""sftp""," &
            """accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.5"",""port"":2022}," &
            "{""kind"":""portforward"",""host"":""home.example.org"",""port"":55001}]," &
            """username"":""fms"",""password"":""pw"",""hostKeyFingerprintSha256"":""" & PinA & """," &
            """roots"":[{""virtualPath"":""/Photos"",""readOnly"":false}],""createdAt"":""" & CreatedAt & """}"
        Assert.Equal(expected, result.Json)
    End Sub

    <Fact>
    Public Sub Export_SchemaVersionIsAdaptive_OnlyAScanSubdirectoriesFalseMakesTwo()
        Assert.Contains("""schemaVersion"":1", ExportOk(PinnedResource(includeSubfolders:=True)).Json)
        Assert.DoesNotContain("scanSubdirectories", ExportOk(PinnedResource(includeSubfolders:=True)).Json)
        Assert.Contains("""schemaVersion"":1", ExportOk(PinnedResource(includeSubfolders:=Nothing)).Json)

        Dim two As String = ExportOk(PinnedResource(includeSubfolders:=False)).Json
        Assert.Contains("""schemaVersion"":2", two)
        Assert.Contains("""scanSubdirectories"":false", two)
        ' Field order: the v2 field sits inside the root, after readOnly.
        Assert.Contains("""readOnly"":false,""scanSubdirectories"":false", two)
    End Sub

    <Fact>
    Public Sub Export_AHandAddedResourceGetsOneAddressOfTheQ11810Kind_AndPortZeroSpeaksTwentyTwo()
        ' A single-label name lives on the local network: lan, and port 0 is 22.
        Dim nas As ResourceDescriptor = PinnedResource()
        nas.Host = "nas" : nas.Port = 0
        Dim lan As String = ExportOk(nas).Json
        Assert.Contains("""accessPaths"":[{""kind"":""lan"",""host"":""nas"",""port"":22}]", lan)

        ' A routable name can only be a forwarded port.
        Dim forwarded As ResourceDescriptor = PinnedResource()
        forwarded.Host = "home.example.org" : forwarded.Port = 55001
        Assert.Contains("""kind"":""portforward"",""host"":""home.example.org"",""port"":55001", ExportOk(forwarded).Json)

        ' An IPv6 literal (bracketed, as the locator spells it) is ipv6.
        Dim v6 As ResourceDescriptor = PinnedResource()
        v6.Host = "[fe80::1]" : v6.Port = 22
        Assert.Contains("""kind"":""ipv6"",""host"":""fe80::1"",""port"":22", ExportOk(v6).Json)

        ' A private-range literal is lan.
        Dim private_ As ResourceDescriptor = PinnedResource()
        private_.Host = "10.0.0.7" : private_.Port = 2022
        Assert.Contains("""kind"":""lan"",""host"":""10.0.0.7"",""port"":2022", ExportOk(private_).Json)
    End Sub

    <Fact>
    Public Sub Export_RankOrderIsTheProducers_DuplicatesCollapse_DeadEntriesAreDropped()
        ' Stored order shuffled: lan, ipv6, portforward come out in the producer's order.
        Dim shuffled As ResourceDescriptor = PinnedResource(endpoints:=New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "portforward", .Host = "fwd.example.org", .Port = 55001},
            New ResourceEndpoint With {.Kind = "ipv6", .Host = "2001:db8::1", .Port = 2022},
            New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.5", .Port = 2022}})
        Dim json As String = ExportOk(shuffled).Json
        Dim lanAt As Integer = json.IndexOf("""kind"":""lan""", StringComparison.Ordinal)
        Dim v6At As Integer = json.IndexOf("""kind"":""ipv6""", StringComparison.Ordinal)
        Dim fwdAt As Integer = json.IndexOf("""kind"":""portforward""", StringComparison.Ordinal)
        Assert.True(lanAt >= 0 AndAlso lanAt < v6At AndAlso v6At < fwdAt, "producer order lan -> ipv6 -> portforward")

        ' The same host:port twice under two names collapses to one (case-insensitive host).
        Dim duplicated As ResourceDescriptor = PinnedResource(endpoints:=New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "lan", .Host = "NAS.local", .Port = 22},
            New ResourceEndpoint With {.Kind = "portforward", .Host = "nas.LOCAL", .Port = 22}})
        Assert.Equal(1, ExportOk(duplicated).Json.Split("{""kind""").Length - 1)

        ' An entry the reader would refuse is dropped, not repaired; if that leaves nothing, refused.
        Dim dead As ResourceDescriptor = PinnedResource(endpoints:=New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "lan", .Host = "a/b", .Port = 22},
            New ResourceEndpoint With {.Kind = "lan", .Host = " spaced ", .Port = 22}})
        Assert.Equal(FmsExportRefusal.NoAddress, FmsConfigExport.Export(dead, True, New MemoryStore()).Refusal)

        ' An unknown kind is not a producer order entry at all.
        Dim odd As ResourceDescriptor = PinnedResource(endpoints:=New List(Of ResourceEndpoint) From {
            New ResourceEndpoint With {.Kind = "vpn", .Host = "10.0.0.1", .Port = 22}})
        Assert.Equal(FmsExportRefusal.NoAddress, FmsConfigExport.Export(odd, True, New MemoryStore()).Refusal)
    End Sub

    <Fact>
    Public Sub Export_PasswordRules_EmptyMeansNotCarried_NeverAnError()
        ' The default (without) writes "".
        Assert.Contains("""password"":""""", ExportOk(PinnedResource(), includePassword:=False).Json)
        ' A resource with no stored credential: "" as well, and still exported.
        Assert.Contains("""password"":""""", ExportOk(PinnedResource(), includePassword:=True).Json)
        ' An undecryptable blob (NeedsReentry): "" as well, never an exception.
        Dim store As New MemoryStore()
        store.Blobs("cred:1") = "not-a-blob"
        Dim d As ResourceDescriptor = PinnedResource(endpoints:=LanPlusForward())
        d.SecretHandle = "cred:1"
        Assert.Contains("""password"":""""", ExportOk(d, True, store).Json)
    End Sub

    <Fact>
    Public Sub Export_Refusals_AreEachTheirOwnOutcome_NeverAnException()
        Dim secrets As IResourceSecretStore = New MemoryStore()

        Dim smb As ResourceDescriptor = PinnedResource()
        smb.Kind = ResourceKind.Unc : smb.RootPath = "\\server\photos"
        Assert.Equal(FmsExportRefusal.Smb, FmsConfigExport.Export(smb, True, secrets).Refusal)

        Dim unpinned As ResourceDescriptor = PinnedResource()
        unpinned.PinnedFingerprint = ""
        Assert.Equal(FmsExportRefusal.NotPinned, FmsConfigExport.Export(unpinned, True, secrets).Refusal)

        Dim badRoot As ResourceDescriptor = PinnedResource()
        badRoot.RootPath = "Photos\Summer"
        Assert.Equal(FmsExportRefusal.BadRoot, FmsConfigExport.Export(badRoot, True, secrets).Refusal)

        Dim emptyRoot As ResourceDescriptor = PinnedResource()
        emptyRoot.RootPath = ""
        Assert.Equal(FmsExportRefusal.BadRoot, FmsConfigExport.Export(emptyRoot, True, secrets).Refusal)

        ' A malformed stored pin is not "unpinned": the document is built and the reader refuses it.
        Dim brokenPin As ResourceDescriptor = PinnedResource()
        brokenPin.PinnedFingerprint = "SHA256:not-base64!!"
        Dim result As FmsExportResult = FmsConfigExport.Export(brokenPin, True, secrets)
        Assert.False(result.Succeeded)
        Assert.Equal(FmsConfigFailure.BadFingerprint, result.SelfCheckFailure)
        Assert.Equal("", result.Json)
    End Sub

    <Fact>
    Public Sub Export_EscapesTheName_TheReaderWay()
        For Each raw As String In {"with ""quotes""", "back\slash", "Сервер «Фото»", Char.ConvertFromUtf32(&H1F600) & " photos", "</script>alert(1)"}
            Dim d As ResourceDescriptor = PinnedResource(name:=raw)
            Dim result As FmsExportResult = ExportOk(d)
            Using document As JsonDocument = JsonDocument.Parse(result.Json)
                Assert.Equal(raw, document.RootElement.GetProperty("resourceName").GetString())
            End Using
        Next
    End Sub

    ' ------------------------------------------------------------------ round trip ----

    <Fact>
    Public Sub Export_RoundTripsThroughTheRealReaderAndImport_FieldForField()
        For Each includeSubfolders As Boolean? In New Boolean?() {True, False, Nothing}
            Dim store As New MemoryStore()
            store.Blobs("cred:1") = "blob:pw"
            Dim source As ResourceDescriptor = PinnedResource(includeSubfolders:=includeSubfolders, endpoints:=LanPlusForward())
            source.SecretHandle = "cred:1"
            Dim result As FmsExportResult = FmsConfigExport.Export(source, True, store, InjectedClock, AddressOf Unprotect)
            Assert.True(result.Succeeded)

            ' The compressed transport carries the same document.
            Assert.StartsWith(FmsConfigReader.QrPrefix, result.Code)
            Assert.Equal(result.Json, DecodeCode(result.Code))

            Dim read As FmsConfigReadResult = FmsConfigReader.ReadText(result.Json)
            Assert.True(read.Succeeded, "the reader must accept its own producer: " & read.Failure.ToString())

            Dim registry As New MemoryRegistry()
            Dim preview As ImportPreview = ResourceImport.Preview(read.Plan, registry.Resources(), AddressOf ResourceImport.NewResourceId)
            Assert.True(preview.CanApply)
            ' The document carries the password (exported with it), so the document's own
            ' credential wins and the user is never asked.
            Assert.False(preview.NeedsPassword)
            Dim applied As ImportApplyResult = ResourceImport.Apply(preview, Nothing, False,
                AddressOf registry.Read, AddressOf registry.Write, store, AddressOf Protect)
            Assert.Equal(ImportApplyOutcome.Applied, applied.Outcome)

            Dim back As ResourceDescriptor = registry.Resources().Single()
            Assert.Equal(source.DisplayName, back.DisplayName)
            Assert.Equal(ResourceKind.Sftp, back.Kind)
            Assert.Equal(source.UserName, back.UserName)
            Assert.Equal(source.RootPath, back.RootPath)
            Assert.Equal(source.ReadOnlyHint, back.ReadOnlyHint)
            Assert.Equal(source.PinnedFingerprint, back.PinnedFingerprint)
            ' Host/Port become the first exported endpoint; the whole list comes back in order.
            Assert.Equal(source.Endpoints(0).Host, back.Host)
            Assert.Equal(source.Endpoints(0).Port, back.Port)
            Assert.Equal(source.Endpoints.Select(Function(e) e.Kind).ToArray(), back.Endpoints.Select(Function(e) e.Kind).ToArray())
            Assert.Equal(source.Endpoints.Select(Function(e) e.Host).ToArray(), back.Endpoints.Select(Function(e) e.Host).ToArray())
            Assert.Equal(source.Endpoints.Select(Function(e) e.Port).ToArray(), back.Endpoints.Select(Function(e) e.Port).ToArray())
            ' Nothing means the import default True; a stored value returns exactly.
            Assert.Equal(If(includeSubfolders.HasValue, includeSubfolders.Value, True), back.IncludeSubfolders)
            ' Identity, secret and last-used are different by design.
            Assert.NotEqual(source.Id, back.Id)
            Assert.NotEqual(source.SecretHandle, back.SecretHandle)
            Assert.Equal("blob:pw", store.GetBlob(back.SecretHandle))
            Assert.Null(back.LastUsedUtc)
        Next
    End Sub

    <Fact>
    Public Sub Export_NeverEmitsTheFieldsTheViewerDoesNotHold()
        Dim json As String = ExportOk(PinnedResource(includeSubfolders:=False)).Json
        For Each absent As String In {"""accessPin""", """isDestination""", """destinationColor""", """profile""",
                                      """mediaTypes""", """comment""", """slideshowInterval""", """showHiddenFiles""",
                                      """allFiles""", """showSubfoldersAsItems""", """label""", """accessNote"""}
            Assert.DoesNotContain(absent, json)
        Next
    End Sub

    <Fact>
    Public Sub Export_ProducerLogsNothing_AndSaysSoInItsOwnSource()
        ' The exporter is pure: outcome out, no log line, ever. A source scan is the proof that
        ' stays true when someone adds a "helpful" diagnostic later (T-M10).
        Dim sourceFile As String = LocateSource("FmsConfigExport.vb")
        Assert.DoesNotContain("AppFileLogger", File.ReadAllText(sourceFile))
        Assert.DoesNotContain("MsgBox", File.ReadAllText(sourceFile))
    End Sub

    Private Shared Function DecodeCode(code As String) As String
        Dim body As String = code.Substring(FmsConfigReader.QrPrefix.Length)
        Dim compressed As Byte() = Convert.FromBase64String(body)
        Using input As New MemoryStream(compressed, writable:=False)
            Using gzip As New GZipStream(input, CompressionMode.Decompress)
                Using output As New MemoryStream()
                    gzip.CopyTo(output)
                    Return Encoding.UTF8.GetString(output.ToArray())
                End Using
            End Using
        End Using
    End Function

    Private Shared Function LocateSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Resources", fileName)
            If File.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("Could not locate src\Resources\" & fileName & " from the test binary.")
    End Function

End Class
#End If
