#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' 100 §14 / §15.5 (T08), 101 §5: the FMSCFG reader against the catalog's canonical vectors, producer-shaped
''' fixtures and the Windows limit matrix (boundary and boundary+1). Every entry surface goes through the same
''' reader, so each case runs as a file's bytes and as clipboard/QR text. Parsing makes no connection and
''' leaves no secret in a diagnostic - both are asserted.
''' </summary>
Public Class FmsConfigReaderTests

    ' ------------------------------------------------------------------------ helpers ----

    Private Shared Function Fixture(name As String) As Byte()
        Return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "resources", "fmscfg", name))
    End Function

    Private Shared Function Read(json As String) As FmsConfigReadResult
        Dim asBytes As FmsConfigReadResult = FmsConfigReader.ReadBytes(Encoding.UTF8.GetBytes(json))
        Dim asText As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        ' One reader for every surface: the two entry points must agree on everything observable.
        Assert.Equal(asBytes.Failure, asText.Failure)
        Assert.Equal(asBytes.FieldPath, asText.FieldPath)
        Assert.Equal(asBytes.Succeeded, asText.Succeeded)
        If asBytes.Succeeded Then Assert.Equal(asBytes.Plan.Roots.Count, asText.Plan.Roots.Count)
        Return asBytes
    End Function

    Private Const Pin As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

    ''' <summary>A minimal valid v1 document with the given roots array text and optional extra top-level members.</summary>
    Private Shared Function Doc(Optional roots As String = "[{""virtualPath"":""/Photos"",""label"":""Photos""}]",
                                Optional extra As String = "",
                                Optional version As String = "1") As String
        Return "{""schemaVersion"":" & version & ",""resourceName"":""Home"",""protocol"":""sftp""," &
               """accessPaths"":[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":2022}]," &
               """username"":""fms"",""password"":""pw"",""hostKeyFingerprintSha256"":""" & Pin & """," &
               """roots"":" & roots & extra & "}"
    End Function

    Private Shared Function WithAccess(access As String, Optional roots As String = "[{""virtualPath"":""/a""}]") As String
        Return "{""schemaVersion"":1,""protocol"":""sftp"",""accessPaths"":" & access &
               ",""username"":""fms"",""password"":""pw"",""roots"":" & roots & "}"
    End Function

    Private Shared Function Gz(json As String) As String
        Using ms As New MemoryStream()
            Using z As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                Dim b As Byte() = Encoding.UTF8.GetBytes(json)
                z.Write(b, 0, b.Length)
            End Using
            Return FmsConfigReader.QrPrefix & Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    Private Shared Sub AssertFails(result As FmsConfigReadResult, kind As FmsConfigFailure, Optional fieldPath As String = Nothing)
        Assert.False(result.Succeeded)
        Assert.Equal(kind, result.Failure)
        Assert.Null(result.Plan)
        If fieldPath IsNot Nothing Then Assert.Equal(fieldPath, result.FieldPath)
    End Sub

    ' ------------------------------------------------------------- canonical vectors ----

    <Fact>
    Public Sub The_canonical_v1_vector_parses_with_every_value()
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadBytes(Fixture("canonical-v1.fmscfg"))
        Assert.True(result.Succeeded, result.Failure.ToString() & " " & result.FieldPath)
        Dim plan As FmsConfigPlan = result.Plan
        Assert.Equal(1, plan.SchemaVersion)
        Assert.Equal("Home PC", plan.ResourceName)
        Assert.Equal("fms", plan.UserName)
        Assert.Equal("k7PmQ2wXr9TzS4vGnHb3JdLe", plan.Password)
        Assert.Equal(Pin, plan.PinnedFingerprint)
        Assert.True(plan.IsPinned)
        Assert.Equal({"lan", "portforward"}, plan.Endpoints.Select(Function(e) e.Kind).ToArray())
        Assert.Equal({"192.168.1.23", "203.0.113.7"}, plan.Endpoints.Select(Function(e) e.Host).ToArray())
        Assert.Equal({"/Photos", "/Music"}, plan.Roots.Select(Function(r) r.VirtualPath).ToArray())
        ' v1 roots omit readOnly: absent means read-only, so nothing here is writable.
        Assert.All(plan.Roots, Sub(r) Assert.False(r.EffectiveWritable))
        Assert.True(plan.CanCommitAll)
    End Sub

    <Fact>
    Public Sub The_canonical_v2_vector_parses_and_maps_every_v2_field_or_refuses_the_pin()
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadBytes(Fixture("canonical-v2.fmscfg"))
        Assert.True(result.Succeeded)
        Dim music As FmsRootPlan = result.Plan.Roots(0)
        Dim inbox As FmsRootPlan = result.Plan.Roots(1)

        Assert.Equal("audio_library", music.Profile)
        Assert.True(music.ProfileRecognised)
        Assert.Equal({"audio"}, music.MediaTypes.ToArray())
        Assert.True(music.ScanSubdirectories)
        Assert.Equal(15, music.SlideshowIntervalSeconds)
        Assert.Equal("Vinyl rips", music.Comment)
        Assert.True(music.HasPin)
        Assert.Equal(RootSupport.RefusedPin, music.Support) ' a PIN is never imported as unlocked

        Assert.True(inbox.IsDestination)
        Assert.True(inbox.EffectiveWritable) ' isDestination wins over the absent (read-only) default
        Assert.Equal(-14575885, inbox.DestinationColor)
        Assert.Equal("Move target", inbox.Comment)
        Assert.Equal(RootSupport.Supported, inbox.Support)

        ' Interim policy (Q100-1): a mixed file is refused as a whole.
        Assert.False(result.Plan.CanCommitAll)
        Assert.Single(result.Plan.RefusedRoots)
    End Sub

    <Fact>
    Public Sub A_phone_export_needs_a_password_is_unpinned_and_reads_the_restrictive_defaults()
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadBytes(Fixture("android-export-synthetic.fmscfg"))
        Assert.True(result.Succeeded)
        Assert.Equal(2, result.Plan.SchemaVersion)
        Assert.True(result.Plan.NeedsPassword) ' empty password: ask or refuse - never import something that cannot log in
        Assert.False(result.Plan.IsPinned)     ' empty fingerprint: explicit first-use acceptance later
        Assert.Equal("", result.Plan.PinnedFingerprint)
        Assert.False(result.Plan.Roots(0).EffectiveWritable)
        Assert.Equal(10, result.Plan.Roots(0).SlideshowIntervalSeconds)
    End Sub

    <Fact>
    Public Sub A_companion_export_with_readOnly_and_isDestination_together_is_writable_and_carries_ipv6_and_the_note()
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadBytes(Fixture("companion-export-synthetic.fmscfg"))
        Assert.True(result.Succeeded)
        Assert.Equal({"lan", "ipv6"}, result.Plan.Endpoints.Select(Function(e) e.Kind).ToArray())
        Assert.Equal("2001:db8::23", result.Plan.Endpoints(1).Host)
        Assert.False(result.Plan.Roots(0).EffectiveWritable)
        Assert.True(result.Plan.Roots(1).EffectiveWritable)
        Assert.Contains("forward the port", result.Plan.AccessNote)
        Assert.True(result.Plan.CanCommitAll)
    End Sub

    <Theory>
    <InlineData(True, False, False)>
    <InlineData(True, True, True)>
    <InlineData(False, False, True)>
    <InlineData(False, True, True)>
    Public Sub Writability_is_exactly_readOnly_false_OR_isDestination_true(readOnly_ As Boolean, destination As Boolean, writable As Boolean)
        Dim roots As String = "[{""virtualPath"":""/x"",""readOnly"":" & readOnly_.ToString().ToLowerInvariant() & ",""isDestination"":" & destination.ToString().ToLowerInvariant() & "}]"
        Dim result As FmsConfigReadResult = Read(Doc(roots, "", "2"))
        Assert.True(result.Succeeded)
        Assert.Equal(writable, result.Plan.Roots(0).EffectiveWritable)
    End Sub

    ' ----------------------------------------------------------------------- versions ----

    <Theory>
    <InlineData("0")>
    <InlineData("-1")>
    <InlineData("1.5")>
    <InlineData("""2""")>
    <InlineData("null")>
    <InlineData("true")>
    <InlineData("99999999999")>
    Public Sub A_missing_or_invalid_schemaVersion_is_invalid_content_not_a_guess(version As String)
        Dim result As FmsConfigReadResult = Read(Doc(version:=version))
        Assert.False(result.Succeeded)
        Assert.Contains(result.Failure, {FmsConfigFailure.InvalidVersion, FmsConfigFailure.MissingVersion})
    End Sub

    <Fact>
    Public Sub A_document_without_schemaVersion_is_missing_the_version()
        AssertFails(Read("{""protocol"":""sftp""}"), FmsConfigFailure.MissingVersion, "schemaVersion")
    End Sub

    <Theory>
    <InlineData("3")>
    <InlineData("7")>
    <InlineData("2147483647")>
    Public Sub A_newer_version_asks_for_an_update_and_reads_nothing_else(version As String)
        ' The rest of the document is nonsense on purpose: not one other field may be looked at.
        Dim result As FmsConfigReadResult = Read("{""schemaVersion"":" & version & ",""protocol"":""gopher"",""roots"":""x"",""accessPaths"":7}")
        AssertFails(result, FmsConfigFailure.UpdateRequired, "schemaVersion")
    End Sub

    <Fact>
    Public Sub A_future_version_is_refused_on_every_transport()
        Dim future As String = "{""schemaVersion"":3}"
        AssertFails(FmsConfigReader.ReadText(Gz(future)), FmsConfigFailure.UpdateRequired)
        AssertFails(FmsConfigReader.ReadBytes(Encoding.UTF8.GetBytes(future)), FmsConfigFailure.UpdateRequired)
    End Sub

    <Theory>
    <InlineData("ftp")>
    <InlineData("SFTP")>
    <InlineData("Sftp")>
    <InlineData("")>
    <InlineData("smb")>
    Public Sub Only_the_exact_lowercase_protocol_sftp_is_accepted(protocol As String)
        Dim json As String = Doc().Replace("""protocol"":""sftp""", """protocol"":""" & protocol & """")
        AssertFails(Read(json), FmsConfigFailure.UnsupportedProtocol, "protocol")
    End Sub

    ' ----------------------------------------------------------------- access paths ----

    <Fact>
    Public Sub A_reordered_access_path_list_is_accepted_and_keeps_the_producers_order()
        Dim access As String = "[{""kind"":""portforward"",""host"":""203.0.113.7"",""port"":2022},{""kind"":""lan"",""host"":""192.168.1.2"",""port"":2022}]"
        Dim result As FmsConfigReadResult = Read(WithAccess(access))
        Assert.True(result.Succeeded)
        Assert.Equal({"portforward", "lan"}, result.Plan.Endpoints.Select(Function(e) e.Kind).ToArray())
    End Sub

    <Fact>
    Public Sub Unknown_kinds_are_skipped_without_being_validated_and_unknown_only_is_refused()
        Dim mixed As String = "[{""kind"":""quantum"",""host"":"""",""port"":0},{""kind"":""lan"",""host"":""192.0.2.5"",""port"":22}]"
        Dim ok As FmsConfigReadResult = Read(WithAccess(mixed))
        Assert.True(ok.Succeeded)
        Assert.Single(ok.Plan.Endpoints)

        Dim onlyUnknown As String = "[{""kind"":""quantum"",""host"":""x"",""port"":1},{""host"":""no-kind"",""port"":1}]"
        AssertFails(Read(WithAccess(onlyUnknown)), FmsConfigFailure.NoUsableAccessPath)
    End Sub

    <Theory>
    <InlineData("0")>
    <InlineData("65536")>
    <InlineData("-22")>
    <InlineData("""22""")>
    <InlineData("22.5")>
    <InlineData("null")>
    Public Sub A_known_path_needs_a_port_between_1_and_65535(port As String)
        Dim access As String = "[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":" & port & "}]"
        Dim result As FmsConfigReadResult = Read(WithAccess(access))
        Assert.False(result.Succeeded)
        Assert.Contains(result.Failure, {FmsConfigFailure.BadAccessPaths, FmsConfigFailure.WrongFieldType})
    End Sub

    <Theory>
    <InlineData("1")>
    <InlineData("65535")>
    Public Sub The_port_boundaries_are_valid(port As String)
        Assert.True(Read(WithAccess("[{""kind"":""lan"",""host"":""192.0.2.1"",""port"":" & port & "}]")).Succeeded)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("  ")>
    <InlineData(" 192.0.2.1")>
    <InlineData("a b")>
    <InlineData("user@host")>
    <InlineData("host/path")>
    Public Sub A_known_path_needs_a_plain_host(host As String)
        Dim access As String = "[{""kind"":""lan"",""host"":""" & host & """,""port"":22}]"
        AssertFails(Read(WithAccess(access)), FmsConfigFailure.BadAccessPaths)
    End Sub

    <Fact>
    Public Sub Duplicate_candidates_collapse_and_more_than_sixteen_entries_are_refused_before_unknown_kinds_are_dropped()
        Dim two As String = "[{""kind"":""lan"",""host"":""H.example"",""port"":22},{""kind"":""portforward"",""host"":""h.example"",""port"":22}]"
        Assert.Single(Read(WithAccess(two)).Plan.Endpoints)

        Dim sixteen As String = "[" & String.Join(",", Enumerable.Range(1, 16).Select(Function(i) "{""kind"":""lan"",""host"":""h" & i.ToString() & ".example"",""port"":22}")) & "]"
        Assert.True(Read(WithAccess(sixteen)).Succeeded)
        ' 17 dead entries: refused even though none of them would ever be dialled.
        Dim seventeen As String = "[" & String.Join(",", Enumerable.Range(1, 17).Select(Function(i) "{""kind"":""x"",""host"":""h"",""port"":1}")) & "]"
        AssertFails(Read(WithAccess(seventeen)), FmsConfigFailure.BadAccessPaths, "accessPaths")
    End Sub

    ' ------------------------------------------------------------------------- roots ----

    <Fact>
    Public Sub An_empty_roots_array_or_more_than_a_hundred_roots_is_refused()
        AssertFails(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", "[]")), FmsConfigFailure.BadRoots, "roots")
        Dim hundred As String = "[" & String.Join(",", Enumerable.Range(1, 100).Select(Function(i) "{""virtualPath"":""/r" & i.ToString() & """}")) & "]"
        Assert.True(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", hundred)).Succeeded)
        Dim over As String = "[" & String.Join(",", Enumerable.Range(1, 101).Select(Function(i) "{""virtualPath"":""/r" & i.ToString() & """}")) & "]"
        AssertFails(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", over)), FmsConfigFailure.TooManyRoots, "roots")
    End Sub

    <Theory>
    <InlineData("Photos")>
    <InlineData("")>
    <InlineData("\\Photos")>
    <InlineData("/a\\b")>
    <InlineData("/a/../b")>
    <InlineData("/..")>
    <InlineData("/a/./b")>
    <InlineData("/a//b")>
    <InlineData("C:\\Photos")>
    Public Sub A_virtual_path_is_a_posix_namespace_path_not_a_windows_one(path As String)
        Dim result As FmsConfigReadResult = Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", "[{""virtualPath"":""" & path & """}]"))
        AssertFails(result, FmsConfigFailure.InvalidVirtualPath, "roots[0].virtualPath")
    End Sub

    <Fact>
    Public Sub A_nul_or_control_character_in_a_virtual_path_is_refused()
        Dim withNul As String = "{""schemaVersion"":1,""protocol"":""sftp"",""accessPaths"":[{""kind"":""lan"",""host"":""h"",""port"":22}],""username"":""u"",""password"":""p"",""roots"":[{""virtualPath"":""/a\u0000b""}]}"
        AssertFails(Read(withNul), FmsConfigFailure.InvalidVirtualPath)
        Dim withTab As String = withNul.Replace("\u0000", "\u0009")
        AssertFails(Read(withTab), FmsConfigFailure.InvalidVirtualPath)
    End Sub

    <Fact>
    Public Sub Root_slash_unicode_and_case_distinct_paths_are_valid_and_kept_exactly()
        Dim roots As String = "[{""virtualPath"":""/""},{""virtualPath"":""/Фото"",""label"":""Отпуск""},{""virtualPath"":""/Photos""},{""virtualPath"":""/photos""},{""virtualPath"":""/trail/""}]"
        Dim result As FmsConfigReadResult = Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", roots))
        Assert.True(result.Succeeded)
        Assert.Equal({"/", "/Фото", "/Photos", "/photos", "/trail"}, result.Plan.Roots.Select(Function(r) r.VirtualPath).ToArray())
        Assert.Equal("Отпуск", result.Plan.Roots(1).Label)
    End Sub

    <Fact>
    Public Sub Two_roots_that_are_the_same_path_are_ambiguous_and_refused()
        Dim roots As String = "[{""virtualPath"":""/a""},{""virtualPath"":""/a/""}]"
        AssertFails(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", roots)), FmsConfigFailure.DuplicateRoot)
    End Sub

    <Fact>
    Public Sub An_unsupported_pin_blocks_that_root_and_an_empty_pin_is_no_lock()
        Dim roots As String = "[{""virtualPath"":""/open"",""accessPin"":""""},{""virtualPath"":""/locked"",""accessPin"":""0000""}]"
        Dim result As FmsConfigReadResult = Read(Doc(roots, "", "2"))
        Assert.True(result.Succeeded)
        Assert.Equal(RootSupport.Supported, result.Plan.Roots(0).Support)
        Assert.False(result.Plan.Roots(0).HasPin)
        Assert.Equal(RootSupport.RefusedPin, result.Plan.Roots(1).Support)
        Assert.False(result.Plan.CanCommitAll) ' interim: the whole mixed file is refused, nothing is imported unlocked
    End Sub

    <Fact>
    Public Sub Unknown_profiles_and_media_tokens_are_ignored_with_a_notice_and_unknown_fields_are_ignorable()
        Dim roots As String = "[{""virtualPath"":""/a"",""profile"":""future_preset"",""mediaTypes"":[""image"",""hologram""],""showSubfoldersAsItems"":true,""zzFutureField"":{""x"":1}}]"
        Dim result As FmsConfigReadResult = Read(Doc(roots, ",""accessNoteFuture"":5", "2"))
        Assert.True(result.Succeeded)
        Dim r As FmsRootPlan = result.Plan.Roots(0)
        Assert.False(r.ProfileRecognised)
        Assert.Equal({"image"}, r.MediaTypes.ToArray())
        Assert.Contains(r.Notices, Function(n) n.StartsWith("profile"))
        Assert.Contains(r.Notices, Function(n) n.StartsWith("mediaTypes"))
        Assert.Contains(r.Notices, Function(n) n.StartsWith("showSubfoldersAsItems"))
    End Sub

    <Theory>
    <InlineData("""readOnly"":""false""")>
    <InlineData("""isDestination"":1")>
    <InlineData("""label"":7")>
    <InlineData("""slideshowInterval"":""10""")>
    <InlineData("""scanSubdirectories"":""yes""")>
    <InlineData("""mediaTypes"":""image""")>
    Public Sub A_malformed_known_field_refuses_the_whole_document_nothing_is_coerced(member As String)
        Dim roots As String = "[{""virtualPath"":""/a""," & member & "}]"
        AssertFails(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", roots)), FmsConfigFailure.WrongFieldType)
    End Sub

    <Fact>
    Public Sub A_non_positive_slideshow_interval_does_not_take_effect()
        Dim roots As String = "[{""virtualPath"":""/a"",""slideshowInterval"":0},{""virtualPath"":""/b"",""slideshowInterval"":-3},{""virtualPath"":""/c"",""slideshowInterval"":4}]"
        Dim result As FmsConfigReadResult = Read(Doc(roots, "", "2"))
        Assert.Null(result.Plan.Roots(0).SlideshowIntervalSeconds)
        Assert.Null(result.Plan.Roots(1).SlideshowIntervalSeconds)
        Assert.Equal(4, result.Plan.Roots(2).SlideshowIntervalSeconds)
    End Sub

    ' ----------------------------------------------------------------- fingerprints ----

    <Fact>
    Public Sub A_missing_or_empty_fingerprint_is_unpinned()
        Dim missing As String = Doc().Replace(",""hostKeyFingerprintSha256"":""" & Pin & """", "")
        Dim empty As String = Doc().Replace(Pin, "")
        Assert.False(Read(missing).Plan.IsPinned)
        Assert.False(Read(empty).Plan.IsPinned)
    End Sub

    <Theory>
    <InlineData("sha256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk=")>
    <InlineData("SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqh")>
    <InlineData("SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhkk")>
    <InlineData("8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk")>
    <InlineData("SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqh-")>
    <InlineData("SHA256:0000000000000000000000000000000000000000000000000000000000000000")>
    <InlineData("not a fingerprint")>
    Public Sub A_non_empty_malformed_fingerprint_refuses_the_import_before_anything_is_stored(fingerprint As String)
        Dim json As String = Doc().Replace(Pin, fingerprint)
        AssertFails(Read(json), FmsConfigFailure.BadFingerprint, "hostKeyFingerprintSha256")
    End Sub

    <Fact>
    Public Sub Non_canonical_trailing_bits_do_not_make_a_second_spelling_of_one_key()
        ' 'l' decodes to the same 32 bytes as 'k' would only with non-zero spare bits: refused.
        Dim spelling As String = Pin.Substring(0, Pin.Length - 1) & "l"
        Dim digest As Byte() = Nothing
        Assert.False(SshFingerprint.TryParse(spelling, digest))
        Assert.True(SshFingerprint.TryParse(Pin, digest))
        Assert.Equal(32, digest.Length)
    End Sub

    <Fact>
    Public Sub The_fingerprint_of_a_host_key_blob_round_trips_and_compares_in_constant_time_form()
        Dim blob As Byte() = Enumerable.Range(0, 51).Select(Function(i) CByte(i * 5 Mod 251)).ToArray()
        Dim canonical As String = SshFingerprint.OfHostKey(blob)
        Assert.Matches("^SHA256:[A-Za-z0-9+/]{43}$", canonical)
        Assert.True(SshFingerprint.MatchesHostKey(canonical, blob))
        blob(10) = CByte(blob(10) Xor 1)
        Assert.False(SshFingerprint.MatchesHostKey(canonical, blob))
        Assert.True(SshFingerprint.AreEqual(canonical, canonical))
        Assert.False(SshFingerprint.AreEqual(canonical, Pin))
        Assert.False(SshFingerprint.AreEqual(canonical, "garbage"))
    End Sub

    ' ------------------------------------------------------------------- duplicates ----

    <Fact>
    Public Sub A_repeated_security_property_is_refused_not_last_wins()
        Dim repeatedRoot As String = "[{""virtualPath"":""/a"",""readOnly"":true,""readOnly"":false}]"
        AssertFails(Read(WithAccess("[{""kind"":""lan"",""host"":""h"",""port"":22}]", repeatedRoot)), FmsConfigFailure.DuplicateProperty)
        Dim repeatedTop As String = "{""schemaVersion"":1,""schemaVersion"":1,""protocol"":""sftp""}"
        AssertFails(Read(repeatedTop), FmsConfigFailure.DuplicateProperty, "schemaVersion")
        Dim repeatedPassword As String = Doc().Replace("""password"":""pw""", """password"":""pw"",""password"":""other""")
        AssertFails(Read(repeatedPassword), FmsConfigFailure.DuplicateProperty, "password")
    End Sub

    ' ------------------------------------------------------------------- transports ----

    <Fact>
    Public Sub The_compressed_qr_form_decodes_to_the_same_plan_with_whitespace_tolerated()
        Dim plain As String = Encoding.UTF8.GetString(Fixture("canonical-v2.fmscfg"))
        Dim qr As String = Gz(plain)
        Dim viaQr As FmsConfigReadResult = FmsConfigReader.ReadText(qr)
        Assert.True(viaQr.Succeeded)
        Assert.Equal(FmsTransport.CompressedQr, viaQr.Plan.Transport)
        Assert.Equal(FmsConfigReader.ReadText(plain).Plan.Roots.Count, viaQr.Plan.Roots.Count)

        ' A scanner wraps and indents: whitespace anywhere in the base64 body is dropped.
        Dim body As String = qr.Substring(FmsConfigReader.QrPrefix.Length)
        Dim wrapped As String = FmsConfigReader.QrPrefix & vbCrLf & String.Join(vbCrLf & "  ", Enumerable.Range(0, (body.Length + 39) \ 40).Select(Function(i) body.Substring(i * 40, Math.Min(40, body.Length - i * 40)))) & vbLf
        Assert.True(FmsConfigReader.ReadText("  " & wrapped & "  ").Succeeded)
    End Sub

    <Fact>
    Public Sub Plain_json_with_surrounding_whitespace_and_a_bom_is_tolerated()
        Dim bytes As Byte() = Fixture("canonical-v1.fmscfg")
        Dim withBom As Byte() = New Byte() {&HEF, &HBB, &HBF}.Concat(New Byte() {13, 10, 32}).Concat(bytes).Concat(New Byte() {10, 10}).ToArray()
        Assert.True(FmsConfigReader.ReadBytes(withBom).Succeeded)
    End Sub

    <Theory>
    <InlineData("FMSCFG1:!!!not-base64!!!")>
    <InlineData("FMSCFG1:QUJD")>
    <InlineData("FMSCFG2:abcd")>
    <InlineData("hello")>
    <InlineData("[1,2]")>
    <InlineData("sftp://user@host/path")>
    Public Sub Bad_transports_fail_with_a_kind_of_their_own(text As String)
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadText(text)
        Assert.False(result.Succeeded)
        Assert.Contains(result.Failure, {FmsConfigFailure.BadBase64, FmsConfigFailure.BadGzip, FmsConfigFailure.UnknownTransport, FmsConfigFailure.InvalidJson, FmsConfigFailure.Empty})
    End Sub

    <Fact>
    Public Sub A_corrupt_gzip_stream_is_refused()
        Dim good As String = Gz(Encoding.UTF8.GetString(Fixture("canonical-v1.fmscfg")))
        Dim bytes As Byte() = Convert.FromBase64String(good.Substring(FmsConfigReader.QrPrefix.Length))
        bytes(bytes.Length \ 2) = CByte(bytes(bytes.Length \ 2) Xor &HFF)
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & Convert.ToBase64String(bytes))
        Assert.False(result.Succeeded)
        Assert.Contains(result.Failure, {FmsConfigFailure.BadGzip, FmsConfigFailure.InvalidJson})
    End Sub

    <Fact>
    Public Sub Compressed_input_over_256_KiB_is_refused_before_a_decompressor_exists()
        Dim noise(MaxCompressed) As Byte
        Dim rng As New Random(7)
        rng.NextBytes(noise)
        Dim result As FmsConfigReadResult = FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & Convert.ToBase64String(noise))
        AssertFails(result, FmsConfigFailure.CompressedTooLarge)
    End Sub

    Private Const MaxCompressed As Integer = FmsConfigReader.MaxCompressedBytes + 100

    <Fact>
    Public Sub An_inflation_bomb_is_aborted_at_the_byte_past_one_MiB_not_read_to_the_end()
        ' 5 MiB of zeros gzips to a few KiB - far under the compressed limit, far over the inflated one.
        Using ms As New MemoryStream()
            Using z As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                Dim zeros(65535) As Byte
                For i As Integer = 1 To 80
                    z.Write(zeros, 0, zeros.Length)
                Next
            End Using
            Assert.True(ms.Length < FmsConfigReader.MaxCompressedBytes)
            AssertFails(FmsConfigReader.ReadText(FmsConfigReader.QrPrefix & Convert.ToBase64String(ms.ToArray())), FmsConfigFailure.InflatedTooLarge)
        End Using
    End Sub

    ' ---------------------------------------------------------------------- limits ----

    Private Shared Function PaddedTo(totalBytes As Integer) As Byte()
        Dim baseJson As Byte() = Fixture("canonical-v1.fmscfg")
        Dim padded(totalBytes - 1) As Byte
        Array.Copy(baseJson, padded, baseJson.Length)
        For i As Integer = baseJson.Length To totalBytes - 1
            padded(i) = CByte(32) ' trailing whitespace is valid JSON
        Next
        Return padded
    End Function

    <Fact>
    Public Sub The_plain_payload_limit_is_one_MiB_boundary_ok_boundary_plus_one_refused()
        Assert.True(FmsConfigReader.ReadBytes(PaddedTo(FmsConfigReader.MaxPlainBytes)).Succeeded)
        AssertFails(FmsConfigReader.ReadBytes(PaddedTo(FmsConfigReader.MaxPlainBytes + 1)), FmsConfigFailure.TooLarge)
        ' The same limit for text, counted in UTF-8 bytes: 4-byte characters hit it far below the character count.
        Dim emoji As String = String.Concat(Enumerable.Repeat(Char.ConvertFromUtf32(&H1F600), 262145)) ' 1 MiB + 4 bytes
        AssertFails(FmsConfigReader.ReadText("{""comment"":""" & emoji & """}"), FmsConfigFailure.TooLarge)
    End Sub

    <Fact>
    Public Sub Invalid_utf8_in_a_file_is_refused_not_repaired()
        Dim broken As Byte() = Encoding.UTF8.GetBytes("{""schemaVersion"":1,""resourceName"":""x").Concat(New Byte() {&HFF, &HFE}).Concat(Encoding.UTF8.GetBytes("""}")).ToArray()
        AssertFails(FmsConfigReader.ReadBytes(broken), FmsConfigFailure.BadEncoding)
        AssertFails(FmsConfigReader.ReadBytes(Array.Empty(Of Byte)()), FmsConfigFailure.Empty)
        AssertFails(FmsConfigReader.ReadText("   "), FmsConfigFailure.Empty)
    End Sub

    <Fact>
    Public Sub JSON_nested_deeper_than_32_levels_is_refused()
        Dim ok As String = "{""schemaVersion"":1,""x"":" & New String("["c, 28) & New String("]"c, 28) & "}"
        Assert.NotEqual(FmsConfigFailure.InvalidJson, FmsConfigReader.ReadText(ok).Failure)
        Dim tooDeep As String = "{""schemaVersion"":1,""x"":" & New String("["c, 40) & New String("]"c, 40) & "}"
        AssertFails(FmsConfigReader.ReadText(tooDeep), FmsConfigFailure.InvalidJson)
    End Sub

    <Fact>
    Public Sub A_string_field_of_4096_units_is_kept_whole_and_4097_is_refused_never_truncated()
        Dim exact As String = New String("c"c, 4096)
        Dim ok As FmsConfigReadResult = Read(Doc("[{""virtualPath"":""/a"",""comment"":""" & exact & """}]", "", "2"))
        Assert.True(ok.Succeeded)
        Assert.Equal(exact, ok.Plan.Roots(0).Comment)
        AssertFails(Read(Doc("[{""virtualPath"":""/a"",""comment"":""" & exact & "x""}]", "", "2")), FmsConfigFailure.FieldTooLong, "roots[0].comment")
        AssertFails(Read(Doc().Replace("""password"":""pw""", """password"":""" & exact & "x""")), FmsConfigFailure.FieldTooLong, "password")
    End Sub

    ' ---------------------------------------------------------------------- secrets ----

    <Fact>
    Public Sub No_diagnostic_surface_carries_the_password_or_a_pin()
        Dim json As String = Doc("[{""virtualPath"":""/a"",""accessPin"":""987654""}]", "", "2").Replace("""password"":""pw""", """password"":""SECRET-PASSWORD-123""")
        Dim ok As FmsConfigReadResult = Read(json)
        Assert.True(ok.Succeeded)
        Assert.DoesNotContain("SECRET-PASSWORD-123", ok.Plan.ToString())
        Assert.DoesNotContain("987654", ok.Plan.ToString())

        ' A failure names a field path and a kind only.
        Dim broken As FmsConfigReadResult = Read(json.Replace("""virtualPath"":""/a""", """virtualPath"":""no-slash"""))
        Assert.False(broken.Succeeded)
        Assert.DoesNotContain("SECRET", broken.FieldPath)
        Assert.DoesNotContain("SECRET", broken.Failure.ToString())
    End Sub

    <Fact>
    Public Sub Every_fixture_reads_the_same_through_both_entry_points()
        For Each name As String In {"canonical-v1.fmscfg", "canonical-v2.fmscfg", "android-export-synthetic.fmscfg", "companion-export-synthetic.fmscfg"}
            Dim bytes As Byte() = Fixture(name)
            Dim viaBytes As FmsConfigReadResult = FmsConfigReader.ReadBytes(bytes)
            Dim viaText As FmsConfigReadResult = FmsConfigReader.ReadText(Encoding.UTF8.GetString(bytes))
            Assert.True(viaBytes.Succeeded, name)
            Assert.Equal(viaBytes.Plan.Roots.Select(Function(r) r.VirtualPath), viaText.Plan.Roots.Select(Function(r) r.VirtualPath))
            Assert.Equal(viaBytes.Plan.Endpoints.Select(Function(e) e.Key()), viaText.Plan.Endpoints.Select(Function(e) e.Key()))
        Next
    End Sub

    <Fact>
    Public Sub The_vendored_vectors_still_carry_the_bytes_their_provenance_records()
        Dim dir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "resources", "fmscfg")
        Dim recorded As String = File.ReadAllText(Path.Combine(dir, "PROVENANCE.txt"))
        For Each name As String In {"canonical-v1.fmscfg", "canonical-v2.fmscfg"}
            Dim hash As String = BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, name)))).Replace("-", "").ToLowerInvariant()
            Assert.Contains(hash & "  " & name, recorded)
        Next
    End Sub

End Class
#End If
