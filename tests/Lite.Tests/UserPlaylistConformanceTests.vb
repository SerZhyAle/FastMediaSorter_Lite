#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports fmsl.Streams
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' `USER-PLAYLIST` 0.12 as the stream-catalog producer's reader meets it: the catalog's three Extended M3U8
    ''' vectors (vendored, digests asserted first), rule 3 with section 9 (an HLS manifest is refused whole, an
    ''' HLS URL inside a playlist is an entry), section 8 item 1 (the 32 MiB ceiling) and item 2 (the title is the
    ''' text after the first comma outside double quotes).
    ''' </summary>
    Public Class UserPlaylistConformanceTests
        Implements IDisposable

        Private Shared ReadOnly VectorDirectory As String =
            Path.Combine(AppContext.BaseDirectory, "TestData", "user-playlist-vectors")

        Private ReadOnly testDir As String

        Public Sub New()
            testDir = Path.Combine(Path.GetTempPath(), "fms_test_playlist_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

        Private Shared Function Vector(name As String) As String
            Return Path.Combine(VectorDirectory, name)
        End Function

        Private Function WritePlaylist(name As String, ParamArray lines As String()) As String
            Dim path As String = System.IO.Path.Combine(testDir, name)
            File.WriteAllText(path, String.Join(vbLf, lines) & vbLf, New UTF8Encoding(False))
            Return path
        End Function

        ' ------------------------------------------------------------- provenance ----

        ''' <summary>The vendored vectors still are what PROVENANCE.txt says: a changed file would quietly redefine "conformant".</summary>
        <Fact>
        Public Sub Vendored_vectors_match_their_recorded_digests()
            Dim provenance As String = File.ReadAllText(Vector("PROVENANCE.txt"))
            Dim checked As Integer = 0
            For Each fileName As String In {"hls-manifest-rejection.m3u8", "valid-extended-hls-entry.m3u8", "valid-extended.m3u8"}
                Dim actual As String
                Using sha As SHA256 = SHA256.Create()
                    actual = String.Concat(sha.ComputeHash(File.ReadAllBytes(Vector(fileName))).Select(Function(b) b.ToString("x2")))
                End Using
                Assert.Contains("  " & actual & "  " & fileName, provenance)
                checked += 1
            Next
            Assert.Equal(3, checked)
        End Sub

        ' ---------------------------------------------------------------- vectors ----

        <Fact>
        Public Sub The_valid_extended_vector_gives_its_three_rows()
            Dim result = M3uPlaylistReader.ReadPath(Vector("valid-extended.m3u8"))
            Assert.Equal(3, result.Rows.Count)
            Assert.Empty(result.Refusals)
            Assert.Equal("Radio Paradise (Main Mix)", result.Rows(0).Name)
            Assert.Equal("Public News Stream", result.Rows(1).Name)
            Assert.Equal("Local Studio Camera", result.Rows(2).Name)
        End Sub

        <Fact>
        Public Sub The_hls_entry_vector_imports_all_three_rows_and_never_reports_a_manifest()
            Dim result = M3uPlaylistReader.ReadPath(Vector("valid-extended-hls-entry.m3u8"))
            Assert.Equal(3, result.Rows.Count)
            Assert.DoesNotContain(result.Refusals, Function(r) r.Reason.StartsWith(M3uPlaylistReader.HlsManifestStatus, StringComparison.Ordinal))
            Assert.Equal("Sport Live (mentions #EXT-X-VERSION in its title)", result.Rows(1).Name)
        End Sub

        <Fact>
        Public Sub The_manifest_vector_is_refused_whole_with_zero_entries()
            Dim result = M3uPlaylistReader.ReadPath(Vector("hls-manifest-rejection.m3u8"))
            Assert.Empty(result.Rows)
            Assert.Equal(0, result.TotalEntriesFound)
            Dim refusal = Assert.Single(result.Refusals)
            Assert.StartsWith(M3uPlaylistReader.HlsManifestStatus & ":", refusal.Reason, StringComparison.Ordinal)
            Assert.Equal(2, refusal.SourceLine)            ' the first line that begins #EXT-X-
        End Sub

        ''' <summary>The same manifest with absolute variant URLs used to import them as stations; it is still a manifest.</summary>
        <Fact>
        Public Sub A_manifest_with_absolute_variant_urls_is_refused_not_imported_as_stations()
            Dim path As String = WritePlaylist("master.m3u8",
                "#EXTM3U",
                "#EXT-X-STREAM-INF:BANDWIDTH=800000",
                "https://cdn.example.com/low/index.m3u8",
                "#EXT-X-STREAM-INF:BANDWIDTH=2400000",
                "https://cdn.example.com/high/index.m3u8")
            Dim result = M3uPlaylistReader.ReadPath(path)
            Assert.Empty(result.Rows)
            Assert.Equal(0, result.TotalEntriesFound)
            Assert.StartsWith(M3uPlaylistReader.HlsManifestStatus, Assert.Single(result.Refusals).Reason, StringComparison.Ordinal)
        End Sub

        ''' <summary>A tag after entries still makes the file a manifest: nothing of it is imported.</summary>
        <Fact>
        Public Sub A_manifest_tag_after_entries_refuses_the_whole_file()
            Dim path As String = WritePlaylist("late.m3u8",
                "#EXTM3U",
                "#EXTINF:-1,One",
                "https://example.com/one",
                "#EXT-X-ENDLIST")
            Dim result = M3uPlaylistReader.ReadPath(path)
            Assert.Empty(result.Rows)
            Assert.Equal(4, Assert.Single(result.Refusals).SourceLine)
        End Sub

        ''' <summary>Section 9 item 2: the text `#EXT-X-` inside an attribute value or a URL is not a line that begins with it.</summary>
        <Fact>
        Public Sub The_tag_text_inside_an_attribute_or_a_url_does_not_make_a_manifest()
            Dim path As String = WritePlaylist("inside.m3u8",
                "#EXTM3U",
                "#EXTINF:-1 tvg-name=""#EXT-X-VERSION"" group-title=""#EXT-X-MEDIA"",Inside",
                "https://example.com/a?x=#EXT-X-VERSION")
            Dim result = M3uPlaylistReader.ReadPath(path)
            Assert.Single(result.Rows)
            Assert.Empty(result.Refusals)
        End Sub

        ' ------------------------------------------------------------ the title ----

        <Theory>
        <InlineData("#EXTINF:-1 group-title=""Rock, Pop"",Name", "Name")>
        <InlineData("#EXTINF:-1 tvg-name=""A, B"" group-title=""C, D, E"",Title, with a comma", "Title, with a comma")>
        <InlineData("#EXTINF:-1,Say ""hi"", friend", "Say ""hi"", friend")>
        <InlineData("#EXTINF:-1 tvg-id=""x"",Plain", "Plain")>
        Public Sub The_title_is_the_text_after_the_first_comma_outside_double_quotes(extinf As String, expectedName As String)
            Dim path As String = WritePlaylist("title.m3u", "#EXTM3U", extinf, "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal(expectedName, row.Name)
        End Sub

        ''' <summary>The quoted comma stays inside the attribute, so the attribute itself still reads whole.</summary>
        <Fact>
        Public Sub A_quoted_comma_does_not_cut_the_attribute_it_is_in()
            Dim path As String = WritePlaylist("attr.m3u", "#EXTM3U",
                "#EXTINF:-1 tvg-country=""US"" tvg-language=""English"" group-title=""Rock, Pop"",Station",
                "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal("Station", row.Name)
            Assert.Equal("US", row.Country)
            Assert.Equal("english", row.Language)
        End Sub

        ''' <summary>No comma outside quotes means no title: rule 4's host fallback applies, nothing is guessed.</summary>
        <Fact>
        Public Sub A_record_with_no_unquoted_comma_has_no_title_and_falls_back_to_the_host()
            Dim path As String = WritePlaylist("notitle.m3u", "#EXTM3U",
                "#EXTINF:-1 group-title=""Rock, Pop",
                "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal("station.example.com", row.Name)
        End Sub

        <Fact>
        Public Sub IndexOfTitleComma_skips_commas_inside_quotes()
            Assert.Equal(-1, M3uPlaylistReader.IndexOfTitleComma("-1 a=""x,y"""))
            Assert.Equal(-1, M3uPlaylistReader.IndexOfTitleComma("-1 no comma at all"))
            Assert.Equal(2, M3uPlaylistReader.IndexOfTitleComma("-1,Name"))
            Assert.Equal(10, M3uPlaylistReader.IndexOfTitleComma("-1 a=""x,y"",N"))
        End Sub

        ' ---------------------------------------------------------- the ceiling ----

        ''' <summary>A file of exactly the ceiling, 32 MiB, is accepted: only a LARGER input is refused.</summary>
        Private Function WriteSizedPlaylist(name As String, totalBytes As Long) As String
            Dim path As String = System.IO.Path.Combine(testDir, name)
            Using fs As New FileStream(path, FileMode.Create, FileAccess.Write)
                Dim header As Byte() = Encoding.ASCII.GetBytes("#EXTM3U" & vbLf)
                fs.Write(header, 0, header.Length)
                Dim remaining As Long = totalBytes - header.Length
                ' Comment lines of ~1 KiB: the file is mostly padding and holds no entry.
                Dim full(1023) As Byte
                full(0) = AscW("#"c)
                For i As Integer = 1 To 1022 : full(i) = AscW("a"c) : Next
                full(1023) = 10
                While remaining >= 1024
                    fs.Write(full, 0, 1024)
                    remaining -= 1024
                End While
                If remaining >= 2 Then
                    Dim tail(CInt(remaining) - 1) As Byte
                    tail(0) = AscW("#"c)
                    For i As Integer = 1 To tail.Length - 2 : tail(i) = AscW("a"c) : Next
                    tail(tail.Length - 1) = 10
                    fs.Write(tail, 0, tail.Length)
                ElseIf remaining = 1 Then
                    fs.WriteByte(10)
                End If
            End Using
            Return path
        End Function

        <Fact>
        Public Sub A_file_of_exactly_the_ceiling_is_read()
            Dim path As String = WriteSizedPlaylist("at-limit.m3u", M3uPlaylistReader.MaxPlaylistBytes)
            Assert.Equal(33554432L, New FileInfo(path).Length)
            Dim result = M3uPlaylistReader.ReadPath(path)
            Assert.Empty(result.Refusals)
            Assert.Empty(result.Rows)
        End Sub

        <Fact>
        Public Sub A_file_one_byte_over_the_ceiling_is_refused_whole()
            Dim path As String = WriteSizedPlaylist("over-limit.m3u", M3uPlaylistReader.MaxPlaylistBytes + 1)
            Dim result = M3uPlaylistReader.ReadPath(path)
            Assert.Empty(result.Rows)
            Assert.StartsWith(M3uPlaylistReader.TooLargeStatus & ":", Assert.Single(result.Refusals).Reason, StringComparison.Ordinal)
        End Sub

        ''' <summary>The refusal is per file: the folder's other playlists are still read.</summary>
        <Fact>
        Public Sub An_oversized_file_in_a_folder_does_not_stop_the_other_playlists()
            Dim folder As String = Path.Combine(testDir, "folder")
            Directory.CreateDirectory(folder)
            File.WriteAllText(Path.Combine(folder, "a-good.m3u"), "#EXTM3U" & vbLf & "#EXTINF:-1,Good" & vbLf & "https://example.com/good" & vbLf)
            Using fs As New FileStream(Path.Combine(folder, "b-huge.m3u"), FileMode.Create)
                fs.SetLength(M3uPlaylistReader.MaxPlaylistBytes + 4096)    ' zeros; the length is what is judged, not a byte read
            End Using
            Dim result = M3uPlaylistReader.ReadPath(folder)
            Assert.Equal("Good", Assert.Single(result.Rows).Name)
            Assert.StartsWith(M3uPlaylistReader.TooLargeStatus, Assert.Single(result.Refusals).Reason, StringComparison.Ordinal)
        End Sub

        ' --------------------------------------------------- the declared kind ----

        ''' <summary>"Latvia" contains "tv" and is not television: a group title is read by words, so it declares nothing.</summary>
        <Fact>
        Public Sub A_group_title_that_merely_contains_tv_declares_nothing()
            Dim path As String = WritePlaylist("latvia.m3u", "#EXTM3U",
                "#EXTINF:-1 group-title=""Latvia"",Baltic Station",
                "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal("AUDIO", row.MediaKind)                 ' the URL classifier's default, not a declaration
            Assert.Equal(String.Empty, row.DeclaredKind)
            Assert.Equal("Latvia", row.UnrecognizedDeclaredKind)
        End Sub

        <Theory>
        <InlineData("Live TV", "VIDEO")>
        <InlineData("TV", "VIDEO")>
        <InlineData("IPTV", "VIDEO")>
        <InlineData("HDTV 1080", "VIDEO")>
        <InlineData("News-TV", "VIDEO")>
        <InlineData("Radio", "AUDIO")>
        <InlineData("Internet Radio", "AUDIO")>
        <InlineData("Webradio", "AUDIO")>
        Public Sub A_group_title_naming_television_or_radio_still_declares_the_kind(groupTitle As String, expectedKind As String)
            Dim path As String = WritePlaylist("declared.m3u", "#EXTM3U",
                "#EXTINF:-1 group-title=""" & groupTitle & """,Station",
                "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal(expectedKind, row.DeclaredKind)
            Assert.Equal(expectedKind, row.MediaKind)
            Assert.Equal(String.Empty, row.UnrecognizedDeclaredKind)
        End Sub

        <Theory>
        <InlineData("Radiohead")>
        <InlineData("Latvian")>
        <InlineData("Chitvan")>
        Public Sub A_name_that_merely_contains_a_kind_word_declares_nothing(groupTitle As String)
            Dim path As String = WritePlaylist("name.m3u", "#EXTM3U",
                "#EXTINF:-1 group-title=""" & groupTitle & """,Station",
                "https://station.example.com/live")
            Dim row = Assert.Single(M3uPlaylistReader.ReadPath(path).Rows)
            Assert.Equal(String.Empty, row.DeclaredKind)
        End Sub

    End Class

End Namespace
#End If
