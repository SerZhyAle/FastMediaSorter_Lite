Option Strict On

Imports System.Collections.Generic
Imports System.Text
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' FMSCFG rule 5 - "the canonical vector is byte-stable on both ends" - for the producer that
''' actually ships. `ShareConfigBuilderTests` beside this file holds eight BEHAVIOURAL tests and
''' not one byte assertion; the JSON is assembled by hand in a fixed field order precisely so it
''' can be byte-stable, so what was missing was the assertion, not the property.
'''
''' What this file pins, and the one thing it cannot
''' ------------------------------------------------
''' The catalog's two canonical vectors were written against the GO exporter - the one this
''' product does not use, because it cannot advertise a manually forwarded router port. The two
''' producers are both conformant and their bytes still differ, in exactly three places, all of
''' them legal under the contract and none of them a judgement call this test gets to make:
'''
'''   1. `roots[].readOnly` - this producer emits it on every root; the Go exporter does not emit
'''      it at all. Documented in CONFIG_FORMAT.md's 2026-09-22 amendment (absent means `true`),
'''      which was written because of this very divergence.
'''   2. `accessNote` - an optional trailing field this producer adds and the Go exporter has no
'''      source for. Additive and ignorable; it does not bump `schemaVersion`.
'''   3. v2 fields at their import default - the Go exporter writes them out (`"scanSubdirectories":true`
'''      and three more), this one omits them to keep a QR payload small. Both say the same thing:
'''      the contract's v2 table gives every one of them a default, and these ARE those defaults.
'''
''' So this file does two things. It PINS the bytes this producer emits, so a change to them is a
''' change somebody made on purpose. And it proves the divergence is exactly the three items
''' above and nothing else - field order, string escaping, number formatting and the timestamp
''' shape are all asserted to be identical to the catalog's, because those are the properties an
''' importer written against the other producer actually depends on.
'''
''' The residual finding - that this producer cannot emit the catalog's vectors byte for byte -
''' is a contract-level observation, not a defect to fix locally, and it is written up for the
''' contract's owner rather than patched here.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ShareConfigCanonicalVectorTests

    ' --- the catalog's vectors, verbatim (CONFIG_FORMAT.md) --------------------

    Private Const CatalogV1 As String =
        "{""schemaVersion"":1,""resourceName"":""Home PC"",""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.23"",""port"":2022},{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022}],""username"":""fms"",""password"":""k7PmQ2wXr9TzS4vGnHb3JdLe"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"",""roots"":[{""virtualPath"":""/Photos"",""label"":""Photos""},{""virtualPath"":""/Music"",""label"":""Music""}],""createdAt"":""2026-07-10T12:00:00Z""}"

    Private Const CatalogV2 As String =
        "{""schemaVersion"":2,""resourceName"":""Home PC"",""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.23"",""port"":2022},{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022}],""username"":""fms"",""password"":""k7PmQ2wXr9TzS4vGnHb3JdLe"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"",""roots"":[{""virtualPath"":""/Music"",""label"":""Home Music"",""profile"":""audio_library"",""mediaTypes"":[""audio""],""scanSubdirectories"":true,""showSubfoldersAsItems"":false,""showHiddenFiles"":false,""allFiles"":false,""isDestination"":false,""comment"":""Vinyl rips"",""accessPin"":""1234"",""slideshowInterval"":15},{""virtualPath"":""/Inbox"",""label"":""Drop Box"",""isDestination"":true,""destinationColor"":-14575885,""comment"":""Move target""}],""createdAt"":""2026-07-11T12:00:00Z""}"

    ' --- what THIS producer emits for the same inputs, pinned -----------------

    Private Const ProducerV1 As String =
        "{""schemaVersion"":1,""resourceName"":""Home PC"",""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.23"",""port"":2022},{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022}],""username"":""fms"",""password"":""k7PmQ2wXr9TzS4vGnHb3JdLe"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"",""roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false},{""virtualPath"":""/Music"",""label"":""Music"",""readOnly"":false}],""createdAt"":""2026-07-10T12:00:00Z"",""accessNote"":""Reachable on your Wi-Fi and, if the router forward/UPnP holds, over the internet - confirm from the phone on mobile data.""}"

    Private Const ProducerV2 As String =
        "{""schemaVersion"":2,""resourceName"":""Home PC"",""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.23"",""port"":2022},{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022}],""username"":""fms"",""password"":""k7PmQ2wXr9TzS4vGnHb3JdLe"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"",""roots"":[{""virtualPath"":""/Music"",""label"":""Home Music"",""readOnly"":false,""profile"":""audio_library"",""mediaTypes"":[""audio""],""comment"":""Vinyl rips"",""accessPin"":""1234"",""slideshowInterval"":15},{""virtualPath"":""/Inbox"",""label"":""Drop Box"",""readOnly"":false,""isDestination"":true,""destinationColor"":-14575885,""comment"":""Move target""}],""createdAt"":""2026-07-11T12:00:00Z"",""accessNote"":""Reachable on your Wi-Fi and, if the router forward/UPnP holds, over the internet - confirm from the phone on mobile data.""}"

    ' --- the inputs that produce them ------------------------------------------

    Private Const PhotosPath As String = "C:\fmscfg-vector\Photos"
    Private Const MusicPath As String = "C:\fmscfg-vector\Music"
    Private Const InboxPath As String = "C:\fmscfg-vector\Inbox"

    ''' <summary>The canonical example's server, as a live worker status. Everything here is a
    ''' value from CONFIG_FORMAT.md; nothing is invented.</summary>
    Private Shared Function CanonicalStatus(roots As List(Of ShareFolder)) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2022,
            .Username = "fms",
            .Password = "k7PmQ2wXr9TzS4vGnHb3JdLe",
            .Fingerprint = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk",
            .Roots = roots,
            .Reachability = New WorkerReachability With {
                .LanAddress = "192.168.1.23",
                .ExternalHost = "203.0.113.7",
                .ExternalPort = 2022,
                .IsCgnat = False,
                .Ipv6Address = ""
            }
        }
    End Function

    Private Shared Function Folder(name As String, hostPath As String) As ShareFolder
        Return New ShareFolder With {.name = name, .hostPath = hostPath, .readOnly = False}
    End Function

    ''' <summary>
    ''' Builds with the per-root params handed in explicitly. That is not a convenience: it is
    ''' what keeps the vector deterministic, because the fallback path reads the user's own
    ''' registry, and a test whose expected bytes depend on the developer's machine proves
    ''' nothing.
    ''' </summary>
    Private Shared Function BuildVector(roots As List(Of ShareFolder),
                                        params As Dictionary(Of String, ShareRootParams),
                                        createdAt As Date) As String
        ' The access note is localized prose; the vector is pinned in English, so the language
        ' is pinned too rather than left to whatever the developer's registry says. Restored
        ' afterwards, and this class shares a collection with everything else that touches the
        ' language so the two cannot overlap - see UiLanguageCollection.
        Dim saved As String = Localization.CurrentCode
        Localization.CurrentCode = "en"
        Try
        Dim res = ShareConfigBuilder.Build(CanonicalStatus(roots),
                                           includeExternal:=True,
                                           includePassword:=True,
                                           exportOverrides:=Nothing,
                                           perRootOverrides:=Nothing,
                                           perRootParams:=params,
                                           stamp:=New ShareConfigStamp With {
                                               .ResourceName = "Home PC",
                                               .CreatedAtUtc = createdAt
                                           })
        Assert.NotNull(res)
        Return res.ConfigJson
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    Private Shared Function V1Input() As String
        Dim roots As New List(Of ShareFolder) From {Folder("Photos", PhotosPath), Folder("Music", MusicPath)}
        Dim params As New Dictionary(Of String, ShareRootParams) From {
            {PhotosPath, New ShareRootParams()},
            {MusicPath, New ShareRootParams()}
        }
        Return BuildVector(roots, params, New Date(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc))
    End Function

    Private Shared Function V2Input() As String
        Dim roots As New List(Of ShareFolder) From {Folder("Music", MusicPath), Folder("Inbox", InboxPath)}
        Dim music As New ShareRootParams With {
            .Label = "Home Music",
            .Profile = "audio_library",
            .MediaTypes = New List(Of String) From {"audio"},
            .Comment = "Vinyl rips",
            .AccessPin = "1234",
            .SlideshowInterval = 15
        }
        Dim inbox As New ShareRootParams With {
            .Label = "Drop Box",
            .IsDestination = True,
            .HasDestinationColor = True,
            .DestinationColorArgb = -14575885,
            .Comment = "Move target"
        }
        Dim params As New Dictionary(Of String, ShareRootParams) From {
            {MusicPath, music},
            {InboxPath, inbox}
        }
        Return BuildVector(roots, params, New Date(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc))
    End Function

    ' --- the assertions --------------------------------------------------------

    <Fact>
    Public Sub V1_Bytes_ArePinned()
        ' A change to these bytes is a contract change, not a test update (FMSCFG rule 5).
        Assert.Equal(ProducerV1, V1Input())
    End Sub

    <Fact>
    Public Sub V2_Bytes_ArePinned()
        Assert.Equal(ProducerV2, V2Input())
    End Sub

    <Fact>
    Public Sub V1_IsTheCatalogVector_PlusExactlyTheTwoDocumentedAdditions()
        ' Strip the two additive fields this producer carries and the catalog's own canonical v1
        ' bytes must come back - character for character, including field order, escaping and the
        ' RFC 3339 timestamp shape. That is the half an importer written against the other
        ' producer actually depends on.
        Dim stripped As String = V1Input().
            Replace(",""readOnly"":false", "").
            Replace(WithoutAccessNoteSuffix(V1Input()), "}")

        Assert.Equal(CatalogV1, stripped)
    End Sub

    <Fact>
    Public Sub V2_DiffersFromTheCatalogVector_OnlyByFieldsAtTheirImportDefault()
        ' The v2 divergence is one more item than v1's: the Go exporter writes four booleans out
        ' at their default value and this one omits them. "Omitted means the importer applies its
        ' v1 default" is the contract's own sentence, so the two payloads describe the same
        ' resource - and this proves it field by field rather than by assertion.
        Using ours = JsonDocument.Parse(V2Input())
            Using theirs = JsonDocument.Parse(CatalogV2)
                Dim ourRoot = ours.RootElement.GetProperty("roots")(0)
                Dim theirRoot = theirs.RootElement.GetProperty("roots")(0)

                ' Present in theirs, absent from ours - and each carries the documented default.
                Assert.True(theirRoot.GetProperty("scanSubdirectories").GetBoolean())
                Assert.False(theirRoot.GetProperty("showSubfoldersAsItems").GetBoolean())
                Assert.False(theirRoot.GetProperty("showHiddenFiles").GetBoolean())
                Assert.False(theirRoot.GetProperty("allFiles").GetBoolean())
                Assert.False(theirRoot.GetProperty("isDestination").GetBoolean())
                For Each omitted As String In New String() {"scanSubdirectories", "showSubfoldersAsItems",
                                                            "showHiddenFiles", "allFiles", "isDestination"}
                    Dim ignored As JsonElement = Nothing
                    Assert.False(ourRoot.TryGetProperty(omitted, ignored),
                                 "this producer started emitting a v2 field at its default: " & omitted)
                Next

                ' Everything present in BOTH is equal, which is the property that matters.
                For Each shared_ As String In New String() {"virtualPath", "label", "profile", "comment",
                                                            "accessPin", "slideshowInterval"}
                    Assert.Equal(theirRoot.GetProperty(shared_).ToString(), ourRoot.GetProperty(shared_).ToString())
                Next
                Assert.Equal("audio", ourRoot.GetProperty("mediaTypes")(0).GetString())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub TheEnvelope_IsByteIdenticalToTheCatalogsUpToTheRoots()
        ' Field order and formatting of everything outside roots[] - the half neither the
        ' readOnly amendment nor the default-omission argument touches.
        For Each pair As String() In New List(Of String()) From {
            New String() {CatalogV1, V1Input()},
            New String() {CatalogV2, V2Input()}
        }
            Dim marker As String = ",""roots"":["
            Assert.Equal(pair(0).Substring(0, pair(0).IndexOf(marker, StringComparison.Ordinal) + marker.Length),
                         pair(1).Substring(0, pair(1).IndexOf(marker, StringComparison.Ordinal) + marker.Length))
        Next
    End Sub

    <Fact>
    Public Sub CreatedAt_OnTheProductPath_IsRfc3339Utc()
        ' The injection point exists so the bytes above can be asserted; this checks it did not
        ' also become the only place that formats the timestamp correctly.
        Dim roots As New List(Of ShareFolder) From {Folder("Photos", PhotosPath)}
        Dim params As New Dictionary(Of String, ShareRootParams) From {{PhotosPath, New ShareRootParams()}}
        Dim res = ShareConfigBuilder.Build(CanonicalStatus(roots), includeExternal:=False,
                                           includePassword:=True, exportOverrides:=Nothing,
                                           perRootOverrides:=Nothing, perRootParams:=params)
        Assert.NotNull(res)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim createdAt As String = doc.RootElement.GetProperty("createdAt").GetString()
            Assert.Matches("^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", createdAt)
        End Using
    End Sub

    <Fact>
    Public Sub ThePayloadIsUtf8WithoutABom()
        ' CONFIG_FORMAT.md "Transport variants": plain compact JSON, UTF-8, no BOM. The QR
        ' payload and the .fmscfg file are the same string, so this is asserted on the string
        ' the builder returns rather than on whatever wrote it.
        Dim json As String = V1Input()
        Assert.False(json.IndexOf(ChrW(&HFEFF)) >= 0, "a byte-order mark reached the payload")
        Assert.False(json.IndexOf(vbLf, StringComparison.Ordinal) >= 0, "the payload is not compact")
        Assert.Equal(Encoding.UTF8.GetByteCount(json), Encoding.UTF8.GetBytes(json).Length)
    End Sub

    ''' <summary>The `,"accessNote":".."` tail plus the closing brace, so it can be cut off
    ''' without hard-coding the localized sentence twice.</summary>
    Friend Shared Function WithoutAccessNoteSuffix(json As String) As String
        Dim idx As Integer = json.IndexOf(",""accessNote"":", StringComparison.Ordinal)
        Assert.True(idx > 0, "this producer stopped emitting accessNote - the divergence list needs updating")
        Return json.Substring(idx)
    End Function

End Class
