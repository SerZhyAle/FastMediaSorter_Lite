#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' 100 T08 (§14.1, §15.5): the per-field support matrix, the re-import pin protection, the limits and
''' error classes the reader must keep apart, and the rule that reading and previewing touch neither the
''' network nor any store. Complements <see cref="FmsConfigReaderTests"/>, which holds the canonical vectors.
''' </summary>
Public Class FmsImportRulesTests

    Private Const Pin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"
    ''' <summary>A second canonical key: the last character carries two spare zero bits, so 'g' (32) is valid.</summary>
    Private Const OtherPin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhg"
    Private Const OneAccess As String = "[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}]"

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function Envelope(Optional roots As String = "[{""virtualPath"":""/a""}]",
                                     Optional topExtra As String = ",""password"":""pw""",
                                     Optional version As String = "2",
                                     Optional protocol As String = """sftp""") As String
        Return "{""schemaVersion"":" & version & ",""protocol"":" & protocol & ",""accessPaths"":" & OneAccess &
               ",""username"":""fms""" & topExtra & ",""roots"":" & roots & "}"
    End Function

    Private Shared Function Rd(json As String) As FmsConfigReadResult
        Dim viaBytes As FmsConfigReadResult = FmsConfigReader.ReadBytes(Encoding.UTF8.GetBytes(json))
        Dim viaText As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        Assert.Equal(viaBytes.Failure, viaText.Failure)
        Assert.Equal(viaBytes.FieldPath, viaText.FieldPath)
        Return viaBytes
    End Function

    Private Shared Sub Fails(result As FmsConfigReadResult, kind As FmsConfigFailure, Optional path As String = Nothing)
        Assert.False(result.Succeeded)
        Assert.Equal(kind, result.Failure)
        Assert.Null(result.Plan)
        If path IsNot Nothing Then Assert.Equal(path, result.FieldPath)
    End Sub

    Private Shared Function Gzip(bytes As Byte()) As String
        Using ms As New MemoryStream()
            Using z As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                z.Write(bytes, 0, bytes.Length)
            End Using
            Return FmsConfigReader.QrPrefix & Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    Private Shared Function PaddedJson(totalBytes As Integer) As Byte()
        Dim json As Byte() = Encoding.UTF8.GetBytes(Envelope())
        Dim padded(totalBytes - 1) As Byte
        Array.Copy(json, padded, json.Length)
        For i As Integer = json.Length To totalBytes - 1
            padded(i) = 32
        Next
        Return padded
    End Function

    Private Shared Function RepoFile(ParamArray parts As String()) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return Path.Combine({dir.FullName}.Concat(parts).ToArray())
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("Repository root not found.")
    End Function

    ' ----------------------------------------------------- field-support matrix ----

    <Fact>
    Public Sub Every_field_the_reader_consumes_has_a_row_in_the_matrix()
        Dim source As String = File.ReadAllText(RepoFile("src", "Resources", "FmsConfigReader.vb"))
        Dim consumed As New HashSet(Of String)(StringComparer.Ordinal)
        For Each m As Match In Regex.Matches(source, "Read(?:String|Bool|Int)\(\w+, ""(\w+)""")
            consumed.Add(m.Groups(1).Value)
        Next
        For Each m As Match In Regex.Matches(source, "TryGetProperty\(""(\w+)""")
            consumed.Add(m.Groups(1).Value)
        Next
        Assert.True(consumed.Count >= 20, "The scan found too few fields; the pattern no longer matches the reader.")

        Dim leaves As New HashSet(Of String)(FmsFieldSupport.Rules.Select(Function(r) r.Path.Substring(r.Path.LastIndexOf("."c) + 1)), StringComparer.Ordinal)
        For Each name As String In consumed
            Dim isContainer As Boolean = FmsFieldSupport.Rules.Any(Function(r) r.Path.StartsWith(name & "[]", StringComparison.Ordinal))
            Assert.True(leaves.Contains(name) OrElse isContainer, "The reader consumes '" & name & "' but the support matrix has no row for it.")
        Next
    End Sub

    <Fact>
    Public Sub Every_row_of_the_matrix_is_still_read_by_the_reader()
        Dim source As String = File.ReadAllText(RepoFile("src", "Resources", "FmsConfigReader.vb"))
        For Each rule As FmsFieldRule In FmsFieldSupport.Rules
            Dim leaf As String = rule.Path.Substring(rule.Path.LastIndexOf("."c) + 1)
            Assert.True(source.Contains("""" & leaf & """"), "Row '" & rule.Path & "' is not read by the reader any more.")
        Next
    End Sub

    <Fact>
    Public Sub The_matrix_is_well_formed_one_row_per_field_with_a_known_schema_and_a_stated_absence()
        Dim paths As String() = FmsFieldSupport.Rules.Select(Function(r) r.Path).ToArray()
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count())
        For Each r As FmsFieldRule In FmsFieldSupport.Rules
            Assert.InRange(r.Since, 1, FmsConfigReader.SupportedSchema)
            Assert.False(String.IsNullOrWhiteSpace(r.WhenAbsent), r.Path)
            Assert.False(String.IsNullOrWhiteSpace(r.Note), r.Path)
            Assert.False(r.Note.Contains("..."), r.Path) ' house text style
        Next
        Assert.Same(FmsFieldSupport.Find("roots[].accessPin"), FmsFieldSupport.Rules.Single(Function(r) r.Path = "roots[].accessPin"))
        Assert.Null(FmsFieldSupport.Find("roots[].nonsense"))
    End Sub

    <Fact>
    Public Sub Security_and_write_fields_are_never_preserved_silently()
        ' Anything that gates access or writes must be Mapped or must refuse - never "kept, not applied".
        For Each path As String In {"roots[].readOnly", "roots[].isDestination", "hostKeyFingerprintSha256", "password", "username", "roots[].virtualPath"}
            Assert.Equal(FmsFieldHandling.Mapped, FmsFieldSupport.Find(path).Handling)
        Next
        Assert.Equal(FmsFieldHandling.RefusesRootWhenSet, FmsFieldSupport.Find("roots[].accessPin").Handling)
        Assert.Equal(FmsFieldHandling.EnvelopeGate, FmsFieldSupport.Find("schemaVersion").Handling)
        Assert.Equal(FmsFieldHandling.EnvelopeGate, FmsFieldSupport.Find("protocol").Handling)
        ' The only presentation value that is kept without being applied is the one Q100-4 has not decided.
        Dim kept As String() = FmsFieldSupport.Rules.Where(Function(r) r.Handling = FmsFieldHandling.PreservedNotApplied).Select(Function(r) r.Path).ToArray()
        Assert.Equal({"roots[].showSubfoldersAsItems"}, kept)
    End Sub

    <Fact>
    Public Sub A_document_that_sets_every_v2_root_field_maps_each_one_the_matrix_says_is_mapped()
        Dim root As String = "[{""virtualPath"":""/Music/"",""label"":""Music"",""readOnly"":false,""isDestination"":true," &
                             """profile"":""audio_library"",""mediaTypes"":[""audio"",""gif""],""scanSubdirectories"":false," &
                             """showSubfoldersAsItems"":true,""showHiddenFiles"":true,""allFiles"":true," &
                             """destinationColor"":-123,""slideshowInterval"":9,""comment"":""c"",""accessPin"":""""}]"
        Dim result As FmsConfigReadResult = Rd(Envelope(root))
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        Dim r As FmsRootPlan = result.Plan.Roots.Single()
        Assert.Equal("/Music", r.VirtualPath)
        Assert.Equal("Music", r.Label)
        Assert.True(r.EffectiveWritable)
        Assert.True(r.IsDestination)
        Assert.Equal("audio_library", r.Profile)
        Assert.Equal({"audio", "gif"}, r.MediaTypes.ToArray())
        Assert.False(r.ScanSubdirectories)
        Assert.True(r.ShowSubfoldersAsItems)
        Assert.True(r.ShowHiddenFiles)
        Assert.True(r.AllFiles)
        Assert.Equal(-123, r.DestinationColor)
        Assert.Equal(9, r.SlideshowIntervalSeconds)
        Assert.Equal("c", r.Comment)
        Assert.False(r.HasPin)
        Assert.Contains(r.Notices, Function(n) n.StartsWith("showSubfoldersAsItems")) ' the one preserved-not-applied field says so
        Assert.Equal(RootSupport.Supported, r.Support)
    End Sub

    <Fact>
    Public Sub Absent_fields_take_the_restrictive_defaults_the_matrix_states()
        Dim r As FmsRootPlan = Rd(Envelope("[{""virtualPath"":""/a""}]")).Plan.Roots.Single()
        Assert.True(r.ReadOnlyRaw)
        Assert.False(r.IsDestination)
        Assert.False(r.EffectiveWritable)
        Assert.True(r.ScanSubdirectories)
        Assert.False(r.AllFiles)
        Assert.False(r.ShowHiddenFiles)
        Assert.Null(r.SlideshowIntervalSeconds)
        Assert.Null(r.DestinationColor)
        Assert.False(r.HasPin)
        Assert.Empty(r.MediaTypes)
    End Sub

    <Fact>
    Public Sub A_v2_field_inside_a_v1_document_is_still_honoured_a_pin_is_never_dropped_for_a_version_label()
        Dim locked As FmsConfigReadResult = Rd(Envelope("[{""virtualPath"":""/a"",""accessPin"":""1234""}]", version:="1"))
        Assert.True(locked.Succeeded)
        Assert.Equal(RootSupport.RefusedPin, locked.Plan.Roots.Single().Support)
        Dim dest As FmsConfigReadResult = Rd(Envelope("[{""virtualPath"":""/a"",""isDestination"":true}]", version:="1"))
        Assert.True(dest.Plan.Roots.Single().EffectiveWritable)
    End Sub

    ' ------------------------------------------- what the matrix says reaches the saved resource ----

    Private Const SavedPin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

    ''' <summary>The saved descriptor a one-root document previews to, as JSON, with a fixed id.</summary>
    Private Shared Function Saved(access As String, user As String, topExtra As String, rootJson As String) As String
        Dim json As String = "{""schemaVersion"":2,""protocol"":""sftp"",""accessPaths"":" & access & ",""username"":""" & user & """,""password"":""pw""" & topExtra &
                             ",""roots"":[" & rootJson & "]}"
        Dim read As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        Assert.True(read.Succeeded, read.Failure.ToString() & " at '" & read.FieldPath & "' for " & rootJson & topExtra)
        Dim preview As ImportPreview = ResourceImport.Preview(read.Plan, New List(Of ResourceDescriptor)(), Function() "fixed-id")
        Assert.True(preview.CanApply, rootJson)
        Return System.Text.Json.JsonSerializer.Serialize(preview.Items.Single().Descriptor)
    End Function

    <Fact>
    Public Sub A_field_the_matrix_says_is_not_stored_leaves_the_saved_resource_untouched_and_a_stored_one_changes_it()
        ' 101 section 5 item 2: the inventory must say what becomes of every field, and it is held mechanically.
        ' A row with StoredInResource = False must not move one byte of the saved descriptor; a row with True (and a
        ' descriptor-visible effect) must. Pinning today's truth: when a presentation field starts to be kept
        ' (Q100-4 / 100 section 15.5.3), its row flips to True and this test says so.
        Const lan As String = "[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}]"
        Const baseRoot As String = """virtualPath"":""/a"",""label"":""Lbl"""
        Dim baseline As String = Saved(lan, "fms", "", "{" & baseRoot & "}")

        ' path -> the same document with that one field set to a non-default value.
        Dim variants As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"resourceName", Saved(lan, "fms", ",""resourceName"":""Named""", "{" & baseRoot & "}")},
            {"accessPaths[].kind", Saved("[{""kind"":""portforward"",""host"":""192.0.2.1"",""port"":2022}]", "fms", "", "{" & baseRoot & "}")},
            {"accessPaths[].host", Saved("[{""kind"":""lan"",""host"":""192.0.2.2"",""port"":2022}]", "fms", "", "{" & baseRoot & "}")},
            {"accessPaths[].port", Saved("[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2023}]", "fms", "", "{" & baseRoot & "}")},
            {"username", Saved(lan, "other", "", "{" & baseRoot & "}")},
            {"accessNote", Saved(lan, "fms", ",""accessNote"":""LAN only""", "{" & baseRoot & "}")},
            {"hostKeyFingerprintSha256", Saved(lan, "fms", ",""hostKeyFingerprintSha256"":""" & SavedPin & """", "{" & baseRoot & "}")},
            {"roots[].virtualPath", Saved(lan, "fms", "", "{""virtualPath"":""/b"",""label"":""Lbl""}")},
            {"roots[].label", Saved(lan, "fms", "", "{""virtualPath"":""/a"",""label"":""Other""}")},
            {"roots[].readOnly", Saved(lan, "fms", "", "{" & baseRoot & ",""readOnly"":false}")},
            {"roots[].isDestination", Saved(lan, "fms", "", "{" & baseRoot & ",""isDestination"":true}")},
            {"roots[].profile", Saved(lan, "fms", "", "{" & baseRoot & ",""profile"":""audio_library""}")},
            {"roots[].mediaTypes", Saved(lan, "fms", "", "{" & baseRoot & ",""mediaTypes"":[""audio""]}")},
            {"roots[].scanSubdirectories", Saved(lan, "fms", "", "{" & baseRoot & ",""scanSubdirectories"":false}")},
            {"roots[].showSubfoldersAsItems", Saved(lan, "fms", "", "{" & baseRoot & ",""showSubfoldersAsItems"":true}")},
            {"roots[].showHiddenFiles", Saved(lan, "fms", "", "{" & baseRoot & ",""showHiddenFiles"":true}")},
            {"roots[].allFiles", Saved(lan, "fms", "", "{" & baseRoot & ",""allFiles"":true}")},
            {"roots[].destinationColor", Saved(lan, "fms", "", "{" & baseRoot & ",""destinationColor"":-123}")},
            {"roots[].slideshowInterval", Saved(lan, "fms", "", "{" & baseRoot & ",""slideshowInterval"":9}")},
            {"roots[].comment", Saved(lan, "fms", "", "{" & baseRoot & ",""comment"":""c""}")}}

        ' Not a descriptor field, so not this test's to move: the gates and the refused field (never stored by
        ' definition) and the password (stored by handle through ResourceCommit; ResourceImportTests covers it).
        Dim outside As String() = {"schemaVersion", "protocol", "password", "roots[].accessPin"}

        Dim covered As New HashSet(Of String)(variants.Keys, StringComparer.Ordinal)
        covered.UnionWith(outside)
        Assert.Equal(FmsFieldSupport.Rules.Select(Function(r) r.Path).OrderBy(Function(p) p, StringComparer.Ordinal),
                     covered.OrderBy(Function(p) p, StringComparer.Ordinal))

        For Each rule As FmsFieldRule In FmsFieldSupport.Rules
            If outside.Contains(rule.Path) Then Continue For
            Dim changed As Boolean = Not String.Equals(baseline, variants(rule.Path), StringComparison.Ordinal)
            Assert.True(changed = rule.StoredInResource,
                        "'" & rule.Path & "' is declared StoredInResource=" & rule.StoredInResource.ToString() & " but " &
                        If(changed, "does change", "does not change") & " the saved resource.")
        Next
        Assert.False(FmsFieldSupport.Find("roots[].accessPin").StoredInResource)
        Assert.True(FmsFieldSupport.Find("password").StoredInResource)
        ' The presentation fields are exactly the ones Q100-4 / 100 section 15.5.3 still owe a place to live.
        Assert.Equal({"accessNote", "roots[].allFiles", "roots[].comment", "roots[].destinationColor", "roots[].mediaTypes", "roots[].profile",
                      "roots[].showHiddenFiles", "roots[].showSubfoldersAsItems", "roots[].slideshowInterval"},
                     FmsFieldSupport.Rules.Where(Function(r) r.Handling <> FmsFieldHandling.EnvelopeGate AndAlso r.Handling <> FmsFieldHandling.RefusesRootWhenSet AndAlso Not r.StoredInResource).
                         Select(Function(r) r.Path).OrderBy(Function(p) p, StringComparer.Ordinal).ToArray())
    End Sub

    ' ------------------------------------------------------ schema/protocol/type ----

    <Theory>
    <InlineData("protocol", "7")>
    <InlineData("protocol", "null")>
    <InlineData("protocol", "[""sftp""]")>
    <InlineData("username", "5")>
    <InlineData("username", "null")>
    <InlineData("username", "{}")>
    <InlineData("password", "5")>
    <InlineData("password", "true")>
    <InlineData("password", "[]")>
    <InlineData("hostKeyFingerprintSha256", "5")>
    <InlineData("hostKeyFingerprintSha256", "{}")>
    <InlineData("resourceName", "5")>
    <InlineData("accessNote", "false")>
    Public Sub A_top_level_field_of_the_wrong_json_type_refuses_the_document(name As String, value As String)
        Dim fields As New Dictionary(Of String, String) From {
            {"protocol", """sftp"""}, {"username", """fms"""}, {"password", """pw"""},
            {"hostKeyFingerprintSha256", """" & Pin & """"}, {"resourceName", """n"""}, {"accessNote", """x"""}}
        fields(name) = value
        Dim json As String = "{""schemaVersion"":2,""accessPaths"":" & OneAccess & ",""roots"":[{""virtualPath"":""/a""}]," &
                             String.Join(",", fields.Select(Function(kv) """" & kv.Key & """:" & kv.Value)) & "}"
        Dim result As FmsConfigReadResult = Rd(json)
        Assert.False(result.Succeeded)
        Assert.Null(result.Plan)
        Assert.Equal(name, result.FieldPath)
        Assert.Contains(result.Failure, {FmsConfigFailure.WrongFieldType, FmsConfigFailure.UnsupportedProtocol, FmsConfigFailure.BadUsername})
    End Sub

    <Fact>
    Public Sub A_null_optional_string_is_absent_not_an_error_but_a_null_required_one_is()
        Assert.True(Rd(Envelope(topExtra:=",""password"":null,""resourceName"":null,""accessNote"":null,""hostKeyFingerprintSha256"":null")).Succeeded)
        Fails(Rd(Envelope(protocol:="null")), FmsConfigFailure.WrongFieldType, "protocol")
    End Sub

    <Fact>
    Public Sub Containers_of_the_wrong_shape_are_named_by_their_own_kind()
        Fails(Rd(Envelope().Replace(OneAccess, "{""kind"":""lan""}")), FmsConfigFailure.BadAccessPaths, "accessPaths")
        Fails(Rd(Envelope().Replace(OneAccess, """lan""")), FmsConfigFailure.BadAccessPaths, "accessPaths")
        Fails(Rd(Envelope(roots:="{""virtualPath"":""/a""}")), FmsConfigFailure.BadRoots, "roots")
        Fails(Rd(Envelope(roots:="""/a""")), FmsConfigFailure.BadRoots, "roots")
        Fails(Rd(Envelope(roots:="[""/a""]")), FmsConfigFailure.BadRoot, "roots[0]")
        Fails(Rd(Envelope(roots:="[null]")), FmsConfigFailure.BadRoot, "roots[0]")
        Fails(Rd(Envelope().Replace(OneAccess, "[""lan""]")), FmsConfigFailure.BadAccessPaths, "accessPaths[0]")
        ' A bare array or string is not even a transport; "not an object" is reachable only through a compressed body.
        Fails(Rd("[]"), FmsConfigFailure.UnknownTransport)
        Fails(Rd("""text"""), FmsConfigFailure.UnknownTransport)
    End Sub

    <Theory>
    <InlineData("2.0")>
    <InlineData("1e0")>
    <InlineData("1.0")>
    <InlineData("[]")>
    <InlineData("{}")>
    Public Sub A_schemaVersion_that_is_not_a_plain_integer_is_invalid(version As String)
        Fails(Rd(Envelope(version:=version)), FmsConfigFailure.InvalidVersion, "schemaVersion")
    End Sub

    <Fact>
    Public Sub A_future_version_is_refused_before_the_protocol_or_any_field_is_judged()
        Fails(Rd(Envelope(version:="3", protocol:="""ftp""")), FmsConfigFailure.UpdateRequired, "schemaVersion")
        Fails(Rd(Envelope(version:="3", roots:="""garbage""")), FmsConfigFailure.UpdateRequired, "schemaVersion")
        ' ...and only the version is reported: the plan of a refused document does not exist, partially or at all.
        Assert.Null(Rd(Envelope(version:="3")).Plan)
    End Sub

    ' ------------------------------------------------------- duplicate security ----

    <Theory>
    <InlineData("{""schemaVersion"":2,""protocol"":""sftp"",""protocol"":""ftp""}", "protocol")>
    <InlineData("{""schemaVersion"":2,""username"":""a"",""username"":""b""}", "username")>
    <InlineData("{""schemaVersion"":2,""hostKeyFingerprintSha256"":"""",""hostKeyFingerprintSha256"":""SHA256:x""}", "hostKeyFingerprintSha256")>
    <InlineData("{""schemaVersion"":2,""accessPaths"":[],""accessPaths"":[]}", "accessPaths")>
    <InlineData("{""schemaVersion"":2,""roots"":[{""virtualPath"":""/a"",""accessPin"":"""",""accessPin"":""1234""}]}", "roots[0].accessPin")>
    <InlineData("{""schemaVersion"":2,""roots"":[{""virtualPath"":""/a"",""isDestination"":false,""isDestination"":true}]}", "roots[0].isDestination")>
    <InlineData("{""schemaVersion"":2,""roots"":[{""virtualPath"":""/a"",""virtualPath"":""/b""}]}", "roots[0].virtualPath")>
    <InlineData("{""schemaVersion"":2,""accessPaths"":[{""kind"":""lan"",""host"":""a"",""host"":""b"",""port"":22}]}", "accessPaths[0].host")>
    Public Sub A_repeated_property_is_refused_wherever_it_occurs_so_the_outcome_never_depends_on_last_wins(json As String, path As String)
        Fails(Rd(json), FmsConfigFailure.DuplicateProperty, path)
    End Sub

    <Fact>
    Public Sub Names_that_differ_only_in_case_are_different_properties_and_the_wrong_case_is_never_read()
        ' System.Text.Json is case-sensitive: "ReadOnly" is an unknown member, not a second readOnly.
        Dim r As FmsRootPlan = Rd(Envelope("[{""virtualPath"":""/a"",""ReadOnly"":false}]")).Plan.Roots.Single()
        Assert.True(r.ReadOnlyRaw)
        Assert.False(r.EffectiveWritable)
    End Sub

    ' ------------------------------------------------------- decompression limits ----

    <Fact>
    Public Sub Inflated_output_of_exactly_one_MiB_is_read_and_one_byte_more_is_refused()
        Dim atLimit As FmsConfigReadResult = FmsConfigReader.ReadText(Gzip(PaddedJson(FmsConfigReader.MaxInflatedBytes)))
        Assert.True(atLimit.Succeeded, atLimit.Failure.ToString())
        Fails(FmsConfigReader.ReadText(Gzip(PaddedJson(FmsConfigReader.MaxInflatedBytes + 1))), FmsConfigFailure.InflatedTooLarge)
    End Sub

    <Fact>
    Public Sub A_compressed_body_of_exactly_256_KiB_gets_as_far_as_the_decompressor_and_one_byte_more_does_not()
        Dim rng As New Random(11)
        Dim atLimit(FmsConfigReader.MaxCompressedBytes - 1) As Byte
        rng.NextBytes(atLimit)
        ' Not a gzip stream, so it fails there - which proves the size gate let it through.
        Fails(FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & Convert.ToBase64String(atLimit)), FmsConfigFailure.BadGzip)
        Dim over(FmsConfigReader.MaxCompressedBytes) As Byte
        rng.NextBytes(over)
        Fails(FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & Convert.ToBase64String(over)), FmsConfigFailure.CompressedTooLarge)
    End Sub

    <Fact>
    Public Sub An_oversized_base64_body_is_refused_by_length_before_it_is_decoded()
        Dim huge As String = FmsConfigReader.QrPrefix & New String("A"c, 500_000)
        Fails(FmsConfigReader.ReadText(huge), FmsConfigFailure.CompressedTooLarge)
    End Sub

    <Fact>
    Public Sub An_empty_or_non_object_compressed_body_has_its_own_outcome()
        Fails(FmsConfigReader.ReadText(FmsConfigReader.QrPrefix), FmsConfigFailure.Empty)
        Fails(FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & "   "), FmsConfigFailure.Empty)
        Fails(FmsConfigReader.ReadText(Gzip(Array.Empty(Of Byte)())), FmsConfigFailure.Empty)
        Fails(FmsConfigReader.ReadText(Gzip(Encoding.UTF8.GetBytes("[1,2]"))), FmsConfigFailure.NotAnObject)
        Fails(FmsConfigReader.ReadText(Gzip(Encoding.UTF8.GetBytes("not json"))), FmsConfigFailure.InvalidJson)
    End Sub

    <Fact>
    Public Sub A_gzip_that_inflates_to_a_future_schema_is_refused_with_the_update_message()
        Fails(FmsConfigReader.ReadText(Gzip(Encoding.UTF8.GetBytes(Envelope(version:="9")))), FmsConfigFailure.UpdateRequired)
    End Sub

    ' ------------------------------------------------------------- PIN eligibility ----

    <Theory>
    <InlineData("""""", False)>
    <InlineData("null", False)>
    <InlineData("""1""", True)>
    <InlineData("""0000""", True)>
    <InlineData(""" """, True)>
    <InlineData("""   """, True)>
    <InlineData("""\u0000""", True)>
    Public Sub Only_a_truly_empty_or_null_pin_means_no_lock(pinJson As String, locked As Boolean)
        Dim result As FmsConfigReadResult = Rd(Envelope("[{""virtualPath"":""/a"",""accessPin"":" & pinJson & "}]"))
        Assert.True(result.Succeeded)
        Dim r As FmsRootPlan = result.Plan.Roots.Single()
        Assert.Equal(locked, r.HasPin)
        Assert.Equal(If(locked, RootSupport.RefusedPin, RootSupport.Supported), r.Support)
        Assert.Equal(Not locked, result.Plan.CanCommitAll)
    End Sub

    <Theory>
    <InlineData("1234")>
    <InlineData("true")>
    <InlineData("[]")>
    <InlineData("{}")>
    Public Sub A_pin_that_is_not_a_string_refuses_the_document_it_is_never_read_as_no_lock(pinJson As String)
        Fails(Rd(Envelope("[{""virtualPath"":""/a"",""accessPin"":" & pinJson & "}]")), FmsConfigFailure.WrongFieldType, "roots[0].accessPin")
    End Sub

    <Fact>
    Public Sub One_locked_root_among_open_ones_is_the_only_refused_root_and_the_mixed_file_cannot_commit_whole()
        Dim roots As String = "[{""virtualPath"":""/a""},{""virtualPath"":""/b"",""accessPin"":""9""},{""virtualPath"":""/c"",""accessPin"":""""}]"
        Dim plan As FmsConfigPlan = Rd(Envelope(roots)).Plan
        Assert.Equal({"/b"}, plan.RefusedRoots.Select(Function(r) r.VirtualPath).ToArray())
        Assert.False(plan.CanCommitAll)
        Assert.Equal({RootSupport.Supported, RootSupport.RefusedPin, RootSupport.Supported}, plan.Roots.Select(Function(r) r.Support).ToArray())
    End Sub

    <Fact>
    Public Sub The_pin_value_never_survives_into_the_plan()
        Dim plan As FmsConfigPlan = Rd(Envelope("[{""virtualPath"":""/a"",""accessPin"":""PIN-424242""}]")).Plan
        ' Only the fact of a lock is kept: there is no property that could be logged or persisted.
        Assert.DoesNotContain(GetType(FmsRootPlan).GetProperties(), Function(p) p.Name.IndexOf("Pin", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso p.PropertyType = GetType(String))
        Assert.DoesNotContain("424242", plan.ToString())
    End Sub

    ' ----------------------------------------------------------- empty passwords ----

    <Theory>
    <InlineData(",""password"":""""")>
    <InlineData(",""password"":null")>
    <InlineData("")>
    Public Sub An_empty_missing_or_null_password_reads_fine_and_asks_before_anything_is_committed(extra As String)
        Dim result As FmsConfigReadResult = Rd(Envelope(topExtra:=extra))
        Assert.True(result.Succeeded)
        Assert.True(result.Plan.NeedsPassword)
        Assert.Equal("", result.Plan.Password)
    End Sub

    <Fact>
    Public Sub A_present_password_is_kept_exactly_including_edge_whitespace()
        Dim result As FmsConfigReadResult = Rd(Envelope(topExtra:=",""password"":"" p w """))
        Assert.False(result.Plan.NeedsPassword)
        Assert.Equal(" p w ", result.Plan.Password) ' a credential is never trimmed or normalised
    End Sub

    <Fact>
    Public Sub An_empty_username_is_not_rescued_by_a_password()
        Fails(Rd(Envelope().Replace("""username"":""fms""", """username"":"""" ")), FmsConfigFailure.BadUsername, "username")
        Fails(Rd(Envelope().Replace("""username"":""fms""", """username"":""   """)), FmsConfigFailure.BadUsername, "username")
    End Sub

    ' ------------------------------------------------- re-import pin protection ----

    <Fact>
    Public Sub No_pin_on_either_side_leaves_the_first_connection_to_ask_for_consent()
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin("", "")
        Assert.Equal(FmsPinOutcome.NoPinEitherSide, d.Outcome)
        Assert.False(d.RequiresConfirmation)
        Assert.Equal("", d.PinToPersist(False))
        Assert.Equal("", d.PinToPersist(True))
    End Sub

    <Fact>
    Public Sub The_same_key_is_unchanged_and_needs_no_question()
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin(Pin, Pin)
        Assert.Equal(FmsPinOutcome.Unchanged, d.Outcome)
        Assert.False(d.RequiresConfirmation)
        Assert.Equal(Pin, d.PinToPersist(False))
    End Sub

    <Fact>
    Public Sub A_file_without_a_pin_never_clears_the_stored_one()
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin(Pin, "")
        Assert.Equal(FmsPinOutcome.KeptBecauseImportHasNone, d.Outcome)
        Assert.False(d.RequiresConfirmation)
        Assert.Equal(Pin, d.PinToPersist(False))
        Assert.Equal(Pin, d.PinToPersist(True)) ' even an answer of "yes" has nothing to replace it with
    End Sub

    <Fact>
    Public Sub A_pin_where_there_was_none_is_adopted_and_shown_in_the_preview()
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin("", Pin)
        Assert.Equal(FmsPinOutcome.AdoptedFromImport, d.Outcome)
        Assert.False(d.RequiresConfirmation)
        Assert.Equal(Pin, d.PinToPersist(False))
    End Sub

    <Fact>
    Public Sub A_different_pin_needs_a_cancel_default_confirmation_and_is_not_taken_without_it()
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin(Pin, OtherPin)
        Assert.Equal(FmsPinOutcome.ChangedNeedsConfirmation, d.Outcome)
        Assert.True(d.RequiresConfirmation)
        Assert.True(d.ConfirmationDefaultsToCancel)
        Assert.Equal(Pin, d.StoredPin)       ' both values are available to show side by side
        Assert.Equal(OtherPin, d.ImportedPin)
        Assert.Equal(Pin, d.PinToPersist(False)) ' cancelled / not answered: the old pin stays
        Assert.Equal(OtherPin, d.PinToPersist(True))
    End Sub

    <Theory>
    <InlineData("garbage", "")>
    <InlineData("garbage", "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("sha256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk", "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    Public Sub A_stored_value_that_is_not_a_usable_fingerprint_fails_closed_never_as_no_pin(stored As String, imported As String)
        Dim d As FmsPinDecision = FmsReimport.EvaluatePin(stored, imported)
        Assert.Equal(FmsPinOutcome.StoredPinUnusableNeedsConfirmation, d.Outcome)
        Assert.True(d.RequiresConfirmation)
        Assert.Equal(stored, d.PinToPersist(False)) ' not cleared, not replaced without an answer
    End Sub

    <Fact>
    Public Sub Null_inputs_mean_no_pin()
        Assert.Equal(FmsPinOutcome.NoPinEitherSide, FmsReimport.EvaluatePin(Nothing, Nothing).Outcome)
        Assert.Equal(FmsPinOutcome.AdoptedFromImport, FmsReimport.EvaluatePin(Nothing, Pin).Outcome)
        Assert.Equal(FmsPinOutcome.KeptBecauseImportHasNone, FmsReimport.EvaluatePin(Pin, Nothing).Outcome)
    End Sub

    <Fact>
    Public Sub Re_import_end_to_end_a_file_with_another_key_is_a_change_and_one_without_a_key_is_not()
        Dim withKey As FmsConfigPlan = Rd(Envelope(topExtra:=",""password"":""pw"",""hostKeyFingerprintSha256"":""" & OtherPin & """")).Plan
        Dim withoutKey As FmsConfigPlan = Rd(Envelope()).Plan
        Assert.Equal(FmsPinOutcome.ChangedNeedsConfirmation, FmsReimport.EvaluatePin(Pin, withKey.PinnedFingerprint).Outcome)
        Assert.Equal(FmsPinOutcome.KeptBecauseImportHasNone, FmsReimport.EvaluatePin(Pin, withoutKey.PinnedFingerprint).Outcome)
        Assert.Equal(FmsPinOutcome.Unchanged, FmsReimport.EvaluatePin(OtherPin, withKey.PinnedFingerprint).Outcome)
    End Sub

    ' ----------------------------------------------- no network, no persistence ----

    <Theory>
    <InlineData("FmsConfigReader.vb")>
    <InlineData("FmsImportRules.vb")>
    <InlineData("SshFingerprint.vb")>
    Public Sub Reading_and_previewing_touch_no_network_registry_file_log_or_process(fileName As String)
        Dim source As String = File.ReadAllText(RepoFile("src", "Resources", fileName))
        ' Comments are stripped first: a doc line may NAME what the code never does.
        Dim code As String = String.Join(vbLf, source.Split({vbLf}, StringSplitOptions.None).Select(Function(l) If(l.TrimStart().StartsWith("'", StringComparison.Ordinal), "", l)))
        For Each banned As String In {"System.Net", "Sockets", "HttpClient", "WebRequest", "Dns.", "Microsoft.Win32", "Registry", "File.", "Directory.", "FileStream", "StreamWriter",
                                      "AppFileLogger", "Debug.", "Console.", "Process.", "Environment.GetFolderPath", "DpapiSecrets", "ResourceRegistry", "ResourceCommit"}
            Assert.False(code.Contains(banned), fileName & " must not reference '" & banned & "': parsing and previewing do no I/O.")
        Next
    End Sub

    <Fact>
    Public Sub Every_fixture_also_reads_through_the_compressed_transport_to_the_same_plan()
        For Each name As String In {"canonical-v1.fmscfg", "canonical-v2.fmscfg", "android-export-synthetic.fmscfg", "companion-export-synthetic.fmscfg"}
            Dim bytes As Byte() = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "resources", "fmscfg", name))
            Dim plain As FmsConfigReadResult = FmsConfigReader.ReadBytes(bytes)
            Dim packed As FmsConfigReadResult = FmsConfigReader.ReadText(Gzip(bytes))
            Assert.True(plain.Succeeded, name)
            Assert.True(packed.Succeeded, name)
            Assert.Equal(plain.Plan.Roots.Select(Function(r) r.VirtualPath), packed.Plan.Roots.Select(Function(r) r.VirtualPath))
            Assert.Equal(plain.Plan.PinnedFingerprint, packed.Plan.PinnedFingerprint)
            Assert.Equal(plain.Plan.NeedsPassword, packed.Plan.NeedsPassword)
        Next
    End Sub

End Class
#End If
