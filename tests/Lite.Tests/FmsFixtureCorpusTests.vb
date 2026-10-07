#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' 101 section 5 items 1, 4 and 5 (the consumer handoff of the FMSCFG interchange): the fixture corpus is held
''' by a MANIFEST that says, for every file, where it came from, whether it is meant to be valid and what the
''' reader must answer - and the suite checks that both ways, so a fixture can neither drift nor be added without
''' a stated expectation. Also here: the Q100-3 closure (the numeric limits are frozen at the values 100 section
''' 15.3 recorded, and a producer-shaped document at its maximum still fits with room to spare) and the rule that
''' every entry surface reads through the one reader and cannot reach a preview without a successful read.
''' </summary>
Public Class FmsFixtureCorpusTests

    Private Shared ReadOnly CorpusDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "resources", "fmscfg")

    Private NotInheritable Class ManifestRow
        Friend File As String
        Friend Origin As String
        Friend Validity As String
        Friend Expected As String
        Friend Facts As String
    End Class

    Private Shared Function Manifest() As List(Of ManifestRow)
        Dim rows As New List(Of ManifestRow)()
        For Each line As String In File.ReadAllLines(Path.Combine(CorpusDir, "MANIFEST.txt"), New UTF8Encoding(False))
            If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then Continue For
            Dim cells As String() = line.Split(ChrW(9))
            Assert.True(cells.Length = 5, "A manifest row has exactly five tab-separated columns: " & line)
            rows.Add(New ManifestRow With {.File = cells(0), .Origin = cells(1), .Validity = cells(2), .Expected = cells(3), .Facts = cells(4)})
        Next
        Return rows
    End Function

    Private Shared Function CorpusFiles() As List(Of String)
        Return Directory.GetFiles(CorpusDir).Select(Function(p) Path.GetFileName(p)).
            Where(Function(n) n <> "PROVENANCE.txt" AndAlso n <> "MANIFEST.txt").OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
    End Function

    Private Shared Function Sha256Hex(bytes As Byte()) As String
        Return BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(bytes)).Replace("-", "").ToLowerInvariant()
    End Function

    ''' <summary>One entry surface per extension: a stored .fmscfg is a file's bytes, a .qr.txt is the text a scanner
    ''' or the clipboard hands over. Both end in the same reader.</summary>
    Private Shared Function ReadCorpusFile(name As String) As FmsConfigReadResult
        Dim path As String = System.IO.Path.Combine(CorpusDir, name)
        If name.EndsWith(".qr.txt", StringComparison.Ordinal) Then Return FmsConfigReader.ReadText(File.ReadAllText(path, New UTF8Encoding(False)))
        Return FmsConfigReader.ReadBytes(File.ReadAllBytes(path))
    End Function

    Private Shared Function FactsOf(plan As FmsConfigPlan) As String
        Return "roots=" & plan.Roots.Count.ToString(CultureInfo.InvariantCulture) &
               " endpoints=" & plan.Endpoints.Count.ToString(CultureInfo.InvariantCulture) &
               " pinned=" & plan.IsPinned.ToString().ToLowerInvariant() &
               " needsPassword=" & plan.NeedsPassword.ToString().ToLowerInvariant() &
               " refused=" & plan.RefusedRoots.Count().ToString(CultureInfo.InvariantCulture) &
               " transport=" & plan.Transport.ToString()
    End Function

    ' ------------------------------------------------------------------------ the manifest ----

    <Fact>
    Public Sub Every_file_of_the_corpus_is_in_the_manifest_and_every_manifest_row_has_its_file()
        Dim listed As List(Of String) = Manifest().Select(Function(r) r.File).OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
        Assert.Equal(listed.Distinct(StringComparer.Ordinal).Count(), listed.Count) ' one row per file
        Assert.Equal(CorpusFiles(), listed)
    End Sub

    <Fact>
    Public Sub Every_row_uses_the_declared_vocabulary_and_a_refusal_row_carries_no_plan_facts()
        Dim origins As String() = {"catalog-vector", "synthetic-phone-shape", "synthetic-companion-shape", "synthetic-contract-2.4", "synthetic-negative"}
        For Each row As ManifestRow In Manifest()
            Dim origin As String = If(row.Origin.StartsWith("derived-transport:", StringComparison.Ordinal), "derived", row.Origin)
            Assert.True(origin = "derived" OrElse origins.Contains(row.Origin), row.File & ": origin '" & row.Origin & "' is not in the vocabulary")
            If origin = "derived" Then Assert.True(File.Exists(Path.Combine(CorpusDir, row.Origin.Substring("derived-transport:".Length))), row.File & ": the file it derives from is missing")
            Assert.Contains(row.Validity, {"valid", "valid-with-refusal", "invalid"})
            If row.Validity = "invalid" Then
                Assert.NotEqual("Succeeded", row.Expected)
                Assert.Equal("-", row.Facts)
                Dim parsed As FmsConfigFailure
                Assert.True([Enum].TryParse(Of FmsConfigFailure)(row.Expected, False, parsed) AndAlso parsed <> FmsConfigFailure.None, row.File & ": '" & row.Expected & "' is not a failure kind of the reader")
            Else
                Assert.Equal("Succeeded", row.Expected)
                Assert.NotEqual("-", row.Facts)
            End If
        Next
    End Sub

    <Fact>
    Public Sub Every_file_reads_to_exactly_the_result_its_manifest_row_declares()
        For Each row As ManifestRow In Manifest()
            Dim result As FmsConfigReadResult = ReadCorpusFile(row.File)
            If row.Validity = "invalid" Then
                Assert.False(result.Succeeded, row.File & " must be refused")
                Assert.Equal(row.Expected, result.Failure.ToString())
                Assert.Null(result.Plan)
            Else
                Assert.True(result.Succeeded, row.File & " must read; failure was " & result.Failure.ToString() & " at '" & result.FieldPath & "'")
                Assert.Equal(row.Facts, FactsOf(result.Plan))
                ' valid-with-refusal means the file reads and a root is refused; plain valid means nothing is.
                Assert.Equal(row.Validity = "valid-with-refusal", Not result.Plan.CanCommitAll)
            End If
        Next
    End Sub

    <Fact>
    Public Sub A_derived_transport_row_reads_to_the_plan_of_the_file_it_derives_from()
        For Each row As ManifestRow In Manifest().Where(Function(r) r.Origin.StartsWith("derived-transport:", StringComparison.Ordinal))
            Dim source As String = row.Origin.Substring("derived-transport:".Length)
            Dim derived As FmsConfigReadResult = ReadCorpusFile(row.File)
            Dim original As FmsConfigReadResult = ReadCorpusFile(source)
            Assert.True(derived.Succeeded AndAlso original.Succeeded, row.File)
            Assert.Equal(FmsTransport.CompressedQr, derived.Plan.Transport)
            Assert.Equal(FactsOf(original.Plan).Replace("PlainJson", "CompressedQr"), FactsOf(derived.Plan))
            Assert.Equal(original.Plan.Roots.Select(Function(r) r.VirtualPath), derived.Plan.Roots.Select(Function(r) r.VirtualPath))
            Assert.Equal(original.Plan.PinnedFingerprint, derived.Plan.PinnedFingerprint)
            Assert.Equal(original.Plan.UserName, derived.Plan.UserName)
        Next
    End Sub

    <Fact>
    Public Sub Every_file_is_pinned_by_a_sha256_in_the_provenance_not_only_the_two_catalog_vectors()
        Dim recorded As String = File.ReadAllText(Path.Combine(CorpusDir, "PROVENANCE.txt"), New UTF8Encoding(False))
        For Each name As String In CorpusFiles()
            Dim hash As String = Sha256Hex(File.ReadAllBytes(Path.Combine(CorpusDir, name)))
            Assert.True(recorded.Contains(hash & "  " & name), name & ": its SHA-256 is not recorded in PROVENANCE.txt (" & hash & ")")
        Next
        ' The manifest is itself an artifact the expectations rest on.
        Assert.Contains(Sha256Hex(File.ReadAllBytes(Path.Combine(CorpusDir, "MANIFEST.txt"))) & "  MANIFEST.txt", recorded)
    End Sub

    <Fact>
    Public Sub The_provenance_names_the_catalog_documents_it_was_taken_from_with_their_versions_and_digests()
        Dim recorded As String = File.ReadAllText(Path.Combine(CorpusDir, "PROVENANCE.txt"), New UTF8Encoding(False))
        For Each needle As String In {"FMSCFG", "SHARE-SESSION", "CONFIG_FORMAT.md", "share-session-reconnect.md", "Read on 2026-10-07"}
            Assert.Contains(needle, recorded)
        Next
        ' Each catalog document is recorded with a 64-hex digest on its own line, so a later re-read can be compared.
        Assert.True(Regex.Matches(recorded, "(?m)^catalog [A-Za-z0-9_./-]+ v[0-9.]+ [0-9a-f]{64}$").Count >= 3, "expected one 'catalog <path> v<version> <sha256>' line per snapshot document")
    End Sub

    <Fact>
    Public Sub A_fixture_carries_only_dummy_secrets_and_addresses_nobody_owns()
        ' 101 section 5 item 4: never a live password/PIN or a reachable user's endpoint. Private LAN ranges and the
        ' documentation ranges (RFC 5737, RFC 3849) are allowed; so are the catalog vectors' own values.
        Dim hostOk As New Regex("^(192\.168\.\d+\.\d+|10\.\d+\.\d+\.\d+|192\.0\.2\.\d+|198\.51\.100\.\d+|203\.0\.113\.\d+|2001:db8:[0-9a-f:]+)$", RegexOptions.IgnoreCase)
        For Each name As String In CorpusFiles()
            Dim text As String = File.ReadAllText(Path.Combine(CorpusDir, name), New UTF8Encoding(False))
            If text.StartsWith(FmsConfigReader.QrPrefix, StringComparison.Ordinal) Then
                Try
                    Dim packed As Byte() = Convert.FromBase64String(text.Substring(FmsConfigReader.QrPrefix.Length))
                    Using zin As New GZipStream(New MemoryStream(packed), CompressionMode.Decompress)
                        Using reader As New StreamReader(zin, New UTF8Encoding(False))
                            text = reader.ReadToEnd()
                        End Using
                    End Using
                Catch ex As InvalidDataException
                    Continue For ' the deliberately broken transport carries no document
                End Try
            End If
            For Each m As Match In Regex.Matches(text, """host"":""([^""]*)""")
                Assert.True(hostOk.IsMatch(m.Groups(1).Value), name & ": host '" & m.Groups(1).Value & "' is not a private or documentation address")
            Next
            For Each m As Match In Regex.Matches(text, """password"":""([^""]*)""")
                Dim value As String = m.Groups(1).Value
                Assert.True(value.Length = 0 OrElse value.Contains("dummy") OrElse value = "k7PmQ2wXr9TzS4vGnHb3JdLe", name & ": a password that is neither empty, marked dummy nor the catalog's own sample")
            Next
            For Each m As Match In Regex.Matches(text, """accessPin"":""([^""]*)""")
                Assert.Contains(m.Groups(1).Value, {"", "0000", "1234"})
            Next
        Next
    End Sub

    ' --------------------------------------------------------------- Q100-3: the numbers ----

    <Fact>
    Public Sub The_limits_are_frozen_at_the_values_100_15_3_recorded()
        ' Changing one of these is a decision with a documented reason and new boundary tests, never an edit made
        ' to let a failing input through. The values are the Windows resource limits of 100 section 15.3; they are
        ' not catalog wire limits.
        Assert.Equal(1024 * 1024, FmsConfigReader.MaxPlainBytes)
        Assert.Equal(256 * 1024, FmsConfigReader.MaxCompressedBytes)
        Assert.Equal(1024 * 1024, FmsConfigReader.MaxInflatedBytes)
        Assert.Equal(32, FmsConfigReader.MaxJsonDepth)
        Assert.Equal(100, FmsConfigReader.MaxRoots)
        Assert.Equal(16, FmsConfigReader.MaxAccessPaths)
        Assert.Equal(4096, FmsConfigReader.MaxStringUnits)
        Assert.Equal(2, FmsConfigReader.SupportedSchema)
    End Sub

    Private Shared Function MaximalRoot(index As Integer, allowPin As Boolean) As String
        Dim n As String = index.ToString(CultureInfo.InvariantCulture)
        Return "{""virtualPath"":""/" & New String("p"c, 200) & n & """,""label"":""" & New String("L"c, 120) & n & """,""readOnly"":false," &
               """profile"":""photo_storage"",""mediaTypes"":[""image"",""video"",""audio"",""gif"",""text"",""pdf"",""epub"",""office""]," &
               """scanSubdirectories"":true,""showSubfoldersAsItems"":true,""showHiddenFiles"":true,""allFiles"":true,""isDestination"":true," &
               """destinationColor"":-14575885,""comment"":""" & New String("c"c, 500) & """," &
               If(allowPin, """accessPin"":""1234"",", "") & """slideshowInterval"":15}"
    End Function

    Private Shared Function MaximalPath(index As Integer) As String
        Return "{""kind"":""lan"",""host"":""192.0.2." & index.ToString(CultureInfo.InvariantCulture) & """,""port"":2022}"
    End Function

    Private Shared Function MaximalProducerDocument(allowPin As Boolean) As String
        Dim sb As New StringBuilder()
        sb.Append("{""schemaVersion"":2,""resourceName"":""").Append(New String("R"c, 120)).Append(""",""protocol"":""sftp"",""accessPaths"":[")
        sb.Append(String.Join(",", Enumerable.Range(1, FmsConfigReader.MaxAccessPaths).Select(AddressOf MaximalPath)))
        sb.Append("],""username"":""fms"",""password"":""dummy-not-a-real-secret"",""hostKeyFingerprintSha256"":""SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"",""accessNote"":""").Append(New String("n"c, 600)).Append(""",""roots"":[")
        sb.Append(String.Join(",", Enumerable.Range(1, FmsConfigReader.MaxRoots).Select(Function(i) MaximalRoot(i, allowPin))))
        sb.Append("],""createdAt"":""2026-10-07T12:00:00Z""}")
        Return sb.ToString()
    End Function

    <Fact>
    Public Sub A_producer_shaped_document_at_its_maximum_reads_and_leaves_most_of_every_limit_unused()
        ' The fullest document a producer can plausibly write: every path, every root, every v2 field, long free text.
        ' It is checked, not assumed, that the Windows limits are not the binding constraint on a real export (Q100-3).
        Dim json As String = MaximalProducerDocument(allowPin:=False)
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(json)
        Assert.True(bytes.Length * 4 < FmsConfigReader.MaxPlainBytes, "plain size " & bytes.Length.ToString(CultureInfo.InvariantCulture) & " should leave at least 4x headroom")
        Dim viaFile As FmsConfigReadResult = FmsConfigReader.ReadBytes(bytes)
        Assert.True(viaFile.Succeeded, viaFile.Failure.ToString() & " at " & viaFile.FieldPath)
        Assert.Equal(FmsConfigReader.MaxRoots, viaFile.Plan.Roots.Count)
        Assert.Equal(FmsConfigReader.MaxAccessPaths, viaFile.Plan.Endpoints.Count)

        Dim packed As String
        Using ms As New MemoryStream()
            Using z As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                z.Write(bytes, 0, bytes.Length)
            End Using
            packed = FmsConfigReader.QrPrefix & Convert.ToBase64String(ms.ToArray())
            Assert.True(ms.Length * 4 < FmsConfigReader.MaxCompressedBytes, "compressed size should leave at least 4x headroom")
        End Using
        Dim viaQr As FmsConfigReadResult = FmsConfigReader.ReadText(packed)
        Assert.True(viaQr.Succeeded, viaQr.Failure.ToString())
        Assert.Equal(viaFile.Plan.Roots.Count, viaQr.Plan.Roots.Count)
    End Sub

    <Fact>
    Public Sub The_same_maximal_document_with_a_pin_on_every_root_is_refused_as_a_whole_only_at_the_preview_never_unlocked()
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadBytes(Encoding.UTF8.GetBytes(MaximalProducerDocument(allowPin:=True)))
        Assert.True(result.Succeeded)
        Assert.Equal(FmsConfigReader.MaxRoots, result.Plan.RefusedRoots.Count())
        Assert.False(result.Plan.CanCommitAll)
        Assert.All(result.Plan.Roots, Sub(r) Assert.Equal(RootSupport.RefusedPin, r.Support))
    End Sub

End Class

''' <summary>
''' 101 section 5 item 5: "test future-version refusal on every entry surface, not only the reader". The surfaces
''' are the open-file dialog, the dropped/launched file, the clipboard text and a scanner's compressed text; they
''' differ only in how bytes arrive. Behaviour is proved by running each over the same too-new document, and the
''' funnel is proved by reading the sources: nothing builds a plan but the reader, nothing previews or stores one
''' but the import flow, and the flow stops on a failed read before it looks at a plan.
''' </summary>
Public Class FmsEntrySurfaceTests

    Private Const TooNew As String = "{""schemaVersion"":3,""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}],""username"":""fms"",""password"":""dummy"",""roots"":[{""virtualPath"":""/a""}],""somethingNew"":{""changesSecuritySemantics"":true}}"

    Private Shared Function RepoFile(ParamArray parts As String()) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return Path.Combine({dir.FullName}.Concat(parts).ToArray())
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Repository root not found.")
    End Function

    Private Shared Function CodeOf(path As String) As String
        Dim lines As IEnumerable(Of String) = File.ReadAllText(path).Split({vbLf}, StringSplitOptions.None).Select(Function(l) l.TrimEnd(ChrW(13)))
        Return String.Join(vbLf, lines.Select(Function(l) If(l.TrimStart().StartsWith("'", StringComparison.Ordinal), "", l)))
    End Function

    Private Shared Function GzipText(json As String) As String
        Using ms As New MemoryStream()
            Using z As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                Dim b As Byte() = Encoding.UTF8.GetBytes(json)
                z.Write(b, 0, b.Length)
            End Using
            Return FmsConfigReader.QrPrefix & Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    <Fact>
    Public Sub A_future_schema_is_refused_with_the_update_message_on_every_entry_surface()
        Dim temp As String = Path.Combine(Path.GetTempPath(), "fms-entry-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temp)
        Try
            ' Open dialog / drop / launch argument: all three end in ResourceImportFlow.ReadFile, whatever the extension.
            Dim results As New List(Of KeyValuePair(Of String, FmsConfigReadResult))()
            For Each ext As String In {".fmscfg", ".json", ".txt"}
                Dim path As String = System.IO.Path.Combine(temp, "share" & ext)
                File.WriteAllText(path, TooNew, New UTF8Encoding(False))
                Assert.True(ResourceImportFlow.IsImportFile(path))
                results.Add(New KeyValuePair(Of String, FmsConfigReadResult)("file" & ext, ResourceImportFlow.ReadFile(path)))
            Next
            ' Clipboard: plain text and a scanner's compressed text.
            results.Add(New KeyValuePair(Of String, FmsConfigReadResult)("clipboard-plain", FmsConfigReader.ReadText(TooNew)))
            results.Add(New KeyValuePair(Of String, FmsConfigReadResult)("clipboard-qr", FmsConfigReader.ReadText(GzipText(TooNew))))

            Dim sentence As String = ResourceMessages.ForImportFailure(FmsConfigFailure.UpdateRequired)
            For Each entry As KeyValuePair(Of String, FmsConfigReadResult) In results
                Assert.False(entry.Value.Succeeded, entry.Key)
                Assert.Equal(FmsConfigFailure.UpdateRequired, entry.Value.Failure)
                Assert.Null(entry.Value.Plan) ' nothing partial survives for a preview to pick up
            Next
            Assert.False(String.IsNullOrWhiteSpace(sentence))
            Assert.NotEqual(ResourceMessages.ForImportFailure(FmsConfigFailure.InvalidJson), sentence)
        Finally
            Directory.Delete(temp, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub An_oversized_or_unreadable_file_is_refused_by_the_file_surface_before_it_is_parsed()
        Dim temp As String = Path.Combine(Path.GetTempPath(), "fms-entry-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temp)
        Try
            ' Over the flow's own cap: refused by length, without reading the bytes.
            Dim huge As String = System.IO.Path.Combine(temp, "huge.fmscfg")
            Using fs As New FileStream(huge, FileMode.Create, FileAccess.Write)
                fs.SetLength(ResourceImportFlow.MaxFileBytes + 1)
            End Using
            Assert.Equal(FmsConfigFailure.TooLarge, ResourceImportFlow.ReadFile(huge).Failure)

            ' Between the reader's one MiB and the flow's cap: the reader's own limit answers, the same way.
            Dim padded As Byte() = New Byte(FmsConfigReader.MaxPlainBytes) {}
            For i As Integer = 0 To padded.Length - 1
                padded(i) = 32
            Next
            Dim json As Byte() = Encoding.UTF8.GetBytes(TooNew.Replace("3", "1"))
            Array.Copy(json, padded, json.Length)
            Dim mid As String = System.IO.Path.Combine(temp, "mid.fmscfg")
            File.WriteAllBytes(mid, padded)
            Assert.Equal(FmsConfigFailure.TooLarge, ResourceImportFlow.ReadFile(mid).Failure)

            ' A file that is not there is an empty read, not an exception into the dialog.
            Dim missing As FmsConfigReadResult = ResourceImportFlow.ReadFile(System.IO.Path.Combine(temp, "gone.fmscfg"))
            Assert.False(missing.Succeeded)
            Assert.Null(missing.Plan)
        Finally
            Directory.Delete(temp, recursive:=True)
        End Try
    End Sub

    <Fact>
    Public Sub Only_the_reader_builds_a_plan_and_only_the_import_flow_previews_or_stores_one()
        Dim srcRoot As String = RepoFile("src")
        Dim offenders As New List(Of String)()
        For Each path As String In Directory.GetFiles(srcRoot, "*.vb", SearchOption.AllDirectories)
            Dim relative As String = path.Substring(srcRoot.Length + 1).Replace("\", "/")
            Dim code As String = CodeOf(path)
            If code.Contains("New FmsConfigPlan") AndAlso relative <> "Resources/FmsConfigReader.vb" Then offenders.Add(relative & " builds a plan")
            If (code.Contains("ResourceImport.Preview(") OrElse code.Contains("ResourceImport.Apply(")) AndAlso relative <> "ResourceImportFlow.vb" Then offenders.Add(relative & " previews or stores outside the flow")
        Next
        Assert.True(offenders.Count = 0, String.Join("; ", offenders))
    End Sub

    <Fact>
    Public Sub Every_entry_surface_hands_the_flow_a_reader_result_and_the_flow_stops_on_a_failed_read_first()
        Dim srcRoot As String = RepoFile("src")
        Dim callers As New List(Of String)()
        For Each path As String In Directory.GetFiles(srcRoot, "*.vb", SearchOption.AllDirectories)
            Dim relative As String = path.Substring(srcRoot.Length + 1).Replace("\", "/")
            Dim code As String = CodeOf(path)
            If relative <> "ResourceImportFlow.vb" AndAlso code.Contains("ResourceImportFlow.Run(") Then callers.Add(relative)
            ' A surface obtains its read only from the flow's ReadFile or the reader's ReadText/ReadBytes.
            If code.Contains("FmsConfigReadResult") AndAlso relative <> "Resources/FmsConfigReader.vb" AndAlso relative <> "ResourceImportFlow.vb" AndAlso
               relative <> "Resources/FmsConfigExport.vb" AndAlso relative <> "Resource_List_Panel.vb" AndAlso relative <> "Resources/ResourceMessages.vb" Then
                Assert.Fail(relative & " handles a read result but is not a known entry surface; route it through ResourceImportFlow and add it here on purpose")
            End If
        Next
        ' The three surfaces of today: the open dialog and the clipboard (the resource list), and a dropped/launched file.
        Assert.Equal({"Main_Form.ResourceOpen.vb", "Resource_List_Panel.vb"}, callers.OrderBy(Function(c) c, StringComparer.Ordinal).ToArray())

        Dim flow As String = CodeOf(System.IO.Path.Combine(srcRoot, "ResourceImportFlow.vb"))
        Dim run As Integer = flow.IndexOf("Shared Function Run(", StringComparison.Ordinal)
        Dim refusal As Integer = flow.IndexOf("Not read.Succeeded", run, StringComparison.Ordinal)
        Dim preview As Integer = flow.IndexOf("ResourceImport.Preview(", run, StringComparison.Ordinal)
        Assert.True(run >= 0 AndAlso refusal > run AndAlso preview > refusal, "the flow must test the read's success before it previews anything")
    End Sub

End Class
#End If
