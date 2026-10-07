#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports fmsl.Streams
Imports Xunit

Namespace Lite.Tests

    ''' <summary>
    ''' Acceptance tests for Stream Catalog Producer (022_SPECIFICATION_PC_STREAM_CATALOG_PRODUCER.md, Criteria 1-12, 17-21, 26-28).
    ''' </summary>
    Public Class StreamCatalogTests
        Implements IDisposable

        Private ReadOnly testDir As String

        Public Sub New()
            testDir = Path.Combine(Path.GetTempPath(), "fms_test_streams_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(testDir) Then
                    Directory.Delete(testDir, True)
                End If
            Catch
            End Try
        End Sub

        <Fact>
        Public Sub Criterion1_FolderOfPlaylists_BuildsConformingStreamCatalogZip()
            ' Criterion 1: A folder of .m3u/.m3u8 playlists becomes stream-catalog.zip with streams.csv carrying 1 row per distinct address
            Dim subDir = Path.Combine(testDir, "playlists")
            Directory.CreateDirectory(subDir)

            Dim m3u1 = Path.Combine(subDir, "radio.m3u")
            File.WriteAllText(m3u1, "#EXTM3U" & vbLf &
                                   "#EXTINF:-1,Radio One" & vbLf &
                                   "http://example.com/stream1" & vbLf &
                                   "#EXTINF:-1,Radio Two" & vbLf &
                                   "https://example.com/stream2" & vbLf, Encoding.UTF8)

            Dim m3u2 = Path.Combine(subDir, "tv.m3u8")
            File.WriteAllText(m3u2, "#EXTM3U" & vbLf &
                                   "#EXTINF:-1,TV Channel" & vbLf &
                                   "https://example.com/live.m3u8" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "stream-catalog.zip")
            Dim buildResult = StreamCatalogProducer.Build(subDir, outZip)

            Assert.True(buildResult.Success, buildResult.ErrorDescription)
            Assert.True(File.Exists(outZip))
            Assert.Equal(3, buildResult.Report.ShippedRowCount)

            ' Validate entry 0 is streams.csv
            Using zip As New ZipArchive(File.OpenRead(outZip), ZipArchiveMode.Read)
                Assert.True(zip.Entries.Count = 1)
                Assert.Equal(StreamCatalogSchema.CatalogCsvEntryName, zip.Entries(0).FullName)
            End Using

            ' Validate V1..V12 pass
            Dim validation = StreamCatalogValidator.ValidateArchive(outZip, 3)
            Assert.True(validation.Passed)
        End Sub

        <Fact>
        Public Sub Criterion2_MediaKind_ClassifiesAudioVideoRtsp()
            ' Criterion 2: media_kind is AUDIO, VIDEO or RTSP (rtsp:// -> RTSP, .m3u8 -> VIDEO, pathless/unknown -> AUDIO)
            Dim playlistFile = Path.Combine(testDir, "kinds.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,RTSP Stream" & vbLf &
                                           "rtsp://media.example.com/live" & vbLf &
                                           "#EXTINF:-1,HLS Video" & vbLf &
                                           "https://media.example.com/video/live.m3u8" & vbLf &
                                           "#EXTINF:-1,MP4 Video" & vbLf &
                                           "https://media.example.com/clip.mp4" & vbLf &
                                           "#EXTINF:-1,Audio Stream" & vbLf &
                                           "http://media.example.com/stream" & vbLf &
                                           "#EXTINF:-1,MP3 Audio" & vbLf &
                                           "http://media.example.com/radio.mp3" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "kinds.zip")
            Dim buildResult = StreamCatalogProducer.Build(playlistFile, outZip)
            Assert.True(buildResult.Success)

            Dim rows = M3uPlaylistReader.ReadPath(playlistFile).Rows
            Assert.Equal(5, rows.Count)
            Assert.Equal("RTSP", rows(0).MediaKind)
            Assert.Equal("VIDEO", rows(1).MediaKind)
            Assert.Equal("VIDEO", rows(2).MediaKind)
            Assert.Equal("AUDIO", rows(3).MediaKind)
            Assert.Equal("AUDIO", rows(4).MediaKind)
        End Sub

        <Fact>
        Public Sub Criterion3_DeclaredKind_WinsOverUrlExtension()
            ' Criterion 3: A source that declares a kind wins over the URL (e.g. radio station as .m3u8 is AUDIO)
            Dim playlistFile = Path.Combine(testDir, "declared.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1 radio=""true"",HLS Radio Station" & vbLf &
                                           "https://stream.radio.org/live.m3u8" & vbLf &
                                           "#EXTINF:-1 tvg-type=""audio"",Audio Channel" & vbLf &
                                           "https://stream.audio.org/live.mp4" & vbLf &
                                           "#EXTINF:-1 type=""video"",Video Stream" & vbLf &
                                           "http://stream.video.org/raw" & vbLf, Encoding.UTF8)

            Dim rows = M3uPlaylistReader.ReadPath(playlistFile).Rows
            Assert.Equal(3, rows.Count)
            Assert.Equal("AUDIO", rows(0).MediaKind)
            Assert.Equal("AUDIO", rows(1).MediaKind)
            Assert.Equal("VIDEO", rows(2).MediaKind)
        End Sub

        <Fact>
        Public Sub Criterion4_All19ColumnsPresentInExactProducerOrder_UnfillableBlank()
            ' Criterion 4: All 19 columns present, spelled exactly, in producer order; category/topic blank
            Dim playlistFile = Path.Combine(testDir, "cols.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1 tvg-country=""US"" tvg-language=""English"",Station One" & vbLf &
                                           "https://example.com/live" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "cols.zip")
            Dim buildResult = StreamCatalogProducer.Build(playlistFile, outZip)
            Assert.True(buildResult.Success)

            Using zip As New ZipArchive(File.OpenRead(outZip), ZipArchiveMode.Read)
                Dim csvEntry = zip.GetEntry(StreamCatalogSchema.CatalogCsvEntryName)
                Using ms As New MemoryStream()
                    Using s = csvEntry.Open()
                        s.CopyTo(ms)
                    End Using
                    ms.Position = 0
                    Dim hasBom = False
                    Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                    Assert.False(hasBom)
                    Assert.Equal(2, parsed.Count)

                    ' Check 19 header names
                    Dim header = parsed(0)
                    Assert.Equal(19, header.Count)
                    For i = 0 To StreamCatalogSchema.ProducerColumns.Count - 1
                        Assert.Equal(StreamCatalogSchema.ProducerColumns(i), header(i))
                    Next

                    ' Check row values
                    Dim row = parsed(1)
                    Assert.Equal(19, row.Count)
                    Assert.Equal("", row(0)) ' category
                    Assert.Equal("", row(1)) ' topic
                    Assert.Equal("Station One", row(2)) ' name
                    Assert.Equal("https://example.com/live", row(3)) ' url
                    Assert.Equal("AUDIO", row(4)) ' media_kind
                    Assert.Equal("", row(5)) ' protocol
                    Assert.Equal("", row(6)) ' format
                    Assert.Equal("", row(7)) ' bitrate
                    Assert.Equal("", row(8)) ' is_live - blank is "not stated" (STREAM-BANK 2.2 rule 13); never "false"
                    Assert.Equal("true", row(9)) ' https
                    Assert.Equal("english", row(10)) ' language
                    Assert.Equal("US", row(11)) ' country
                    Assert.Equal("", row(12)) ' homepage
                    Assert.Equal("", row(13)) ' source_kind
                    Assert.Equal("", row(14)) ' license_note
                    Assert.Equal("", row(15)) ' notes
                    Assert.Equal("", row(16)) ' confidence
                    Assert.Equal("", row(17)) ' favicon_index
                    Assert.Equal("", row(18)) ' access
                End Using
            End Using
        End Sub

        <Fact>
        Public Sub Criterion5_SpecialCharactersAndQuotes_RoundTripUnchanged()
            ' Criterion 5: A name containing comma, double quote and newline round-trips through CSV unchanged
            Dim specialName = "Station, with ""quotes"" and" & vbCrLf & "newlines"
            Dim row As New StreamCatalogRow() With {
                .Name = specialName,
                .Url = "https://example.com/special",
                .MediaKind = "AUDIO",
                .Https = "true"
            }

            Using ms As New MemoryStream()
                StreamCsvWriter.WriteCsv(New StreamCatalogRow() {row}, ms)
                ms.Position = 0
                Dim hasBom = False
                Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                Assert.Equal(2, parsed.Count)
                Assert.Equal(specialName, parsed(1)(2))
            End Using
        End Sub

        <Fact>
        Public Sub Criterion6_UnicodeCyrillicGreekCJK_RoundTripWithoutBom()
            ' Criterion 6: Cyrillic, Greek and CJK names round-trip; file has no BOM
            Dim names = New String() {
                "Радио Маяк (Москва)",
                "Ελληνικό Ραδιόφωνο",
                "北京音乐广播"
            }

            Dim rows As New List(Of StreamCatalogRow)()
            For i = 0 To names.Length - 1
                rows.Add(New StreamCatalogRow() With {
                    .Name = names(i),
                    .Url = "https://example.com/stream/" & i,
                    .MediaKind = "AUDIO",
                    .Https = "true"
                })
            Next

            Using ms As New MemoryStream()
                StreamCsvWriter.WriteCsv(rows, ms)
                ms.Position = 0
                Dim hasBom = False
                Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                Assert.False(hasBom)
                Assert.Equal(4, parsed.Count)
                For i = 0 To names.Length - 1
                    Assert.Equal(names(i), parsed(i + 1)(2))
                Next
            End Using
        End Sub

        <Fact>
        Public Sub Criterion7_ChannelIdentityFold_DeduplicatesAndPrefersHttps()
            ' Criterion 7 / Contract sync item 2: http://Host/a, https://host/a/ and https://host:443/a produce one row
            Dim playlistFile = Path.Combine(testDir, "dedup.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Station HTTP" & vbLf &
                                           "http://Host/a" & vbLf &
                                           "#EXTINF:-1,Station Slash" & vbLf &
                                           "https://host/a/" & vbLf &
                                           "#EXTINF:-1,Station Port" & vbLf &
                                           "https://host:443/a" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "dedup.zip")
            Dim buildResult = StreamCatalogProducer.Build(playlistFile, outZip)

            Assert.True(buildResult.Success)
            Assert.Equal(1, buildResult.Report.ShippedRowCount)
            Assert.Equal(2, buildResult.Report.Folds.Count)

            ' Check surviving row has HTTPS URL
            Using zip As New ZipArchive(File.OpenRead(outZip), ZipArchiveMode.Read)
                Using ms As New MemoryStream()
                    zip.Entries(0).Open().CopyTo(ms)
                    ms.Position = 0
                    Dim hasBom = False
                    Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                    Assert.Equal(2, parsed.Count)
                    Assert.Equal("https://host/a", parsed(1)(3))
                End Using
            End Using
        End Sub

        <Fact>
        Public Sub Criterion8_NonLaunchableSchemes_RefusedAndReported()
            ' Criterion 8: Schemes not in http/https/rtsp (udp, smb, sftp) are refused and reported with file, line, reason
            Dim playlistFile = Path.Combine(testDir, "schemes.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,UDP Stream" & vbLf &
                                           "udp://@239.255.1.1:1234" & vbLf &
                                           "#EXTINF:-1,SMB Stream" & vbLf &
                                           "smb://server/share/film.mkv" & vbLf &
                                           "#EXTINF:-1,SFTP Stream" & vbLf &
                                           "sftp://server/media.mp4" & vbLf &
                                           "#EXTINF:-1,Valid Stream" & vbLf &
                                           "https://example.com/live" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "schemes.zip")
            Dim buildResult = StreamCatalogProducer.Build(playlistFile, outZip)

            Assert.True(buildResult.Success)
            Assert.Equal(1, buildResult.Report.ShippedRowCount)
            Assert.Equal(3, buildResult.Report.Refusals.Count)

            Dim reasons = New List(Of String)()
            For Each r In buildResult.Report.Refusals
                reasons.Add(r.Reason)
            Next

            Assert.Contains(reasons, Function(s) s.Contains("udp"))
            Assert.Contains(reasons, Function(s) s.Contains("smb"))
            Assert.Contains(reasons, Function(s) s.Contains("sftp"))
        End Sub

        <Fact>
        Public Sub Criterion9_NestedPlaylistReference_RefusedAndReported()
            ' Criterion 9: A nested playlist reference (.m3u, .pls) is refused and reported
            Dim playlistFile = Path.Combine(testDir, "nested.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Nested M3U" & vbLf &
                                           "http://example.com/playlist.m3u" & vbLf &
                                           "#EXTINF:-1,Nested PLS" & vbLf &
                                           "http://example.com/station.pls" & vbLf, Encoding.UTF8)

            Dim parseResult = M3uPlaylistReader.ReadPath(playlistFile)
            Assert.Equal(0, parseResult.Rows.Count)
            Assert.Equal(2, parseResult.Refusals.Count)
            For Each r In parseResult.Refusals
                Assert.Contains("Nested playlist", r.Reason)
            Next
        End Sub

        <Fact>
        Public Sub Criterion10_CredentialsAndBroadcastTokens_RefusedAndReported()
            ' Criterion 10: Address carrying credentials or broadcast token is refused and reported
            Dim playlistFile = Path.Combine(testDir, "private.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Credentials URL" & vbLf &
                                           "https://user:password@example.com/stream" & vbLf &
                                           "#EXTINF:-1,Broadcast Token" & vbLf &
                                           "http://192.168.1.50:8080/b/dGhpcy1pcy1hLXNlY3JldC1icm9hZGNhc3QtdG9rZW4" & vbLf, Encoding.UTF8)

            Dim parseResult = M3uPlaylistReader.ReadPath(playlistFile)
            Assert.Equal(0, parseResult.Rows.Count)
            Assert.Equal(2, parseResult.Refusals.Count)

            Assert.Contains(parseResult.Refusals, Function(r) r.Reason.Contains("credentials"))
            Assert.Contains(parseResult.Refusals, Function(r) r.Reason.Contains("broadcast token"))
        End Sub

        <Fact>
        Public Sub Criterion11_NameGuarantees_DecodesEntitiesStripsSlotsRefusesReplacementChar()
            ' Criterion 11: HTML entity decoded; encoder slot stripped; junk name rebuilt from host; U+FFFD refused
            Dim playlistFile = Path.Combine(testDir, "names.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Rock &amp;amp; Pop Radio" & vbLf &
                                           "http://example.com/rock" & vbLf &
                                           "#EXTINF:-1,- 1 A - Jazz Lounge" & vbLf &
                                           "http://example.com/jazz" & vbLf &
                                           "#EXTINF:-1,encoder" & vbLf &
                                           "http://station.org:8000/live" & vbLf &
                                           "#EXTINF:-1,Damaged" & Convert.ToChar(&HFFFD) & "Name" & vbLf &
                                           "http://example.com/bad" & vbLf, Encoding.UTF8)

            Dim parseResult = M3uPlaylistReader.ReadPath(playlistFile)
            Assert.Equal(3, parseResult.Rows.Count)
            Assert.Equal(1, parseResult.Refusals.Count)

            Assert.Equal("Rock & Pop Radio", parseResult.Rows(0).Name)
            Assert.Equal("Jazz Lounge", parseResult.Rows(1).Name)
            Assert.Equal("station.org:8000", parseResult.Rows(2).Name)
            Assert.Contains("U+FFFD", parseResult.Refusals(0).Reason)
        End Sub

        <Fact>
        Public Sub Criterion12_FirstVersion_WritesNoAtlasAndNoFaviconIndex()
            ' Criterion 12: First version writes no atlas entry and no favicon_index value
            Dim playlistFile = Path.Combine(testDir, "v1.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,Channel 1" & vbLf &
                                           "http://example.com/1" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "v1.zip")
            StreamCatalogProducer.Build(playlistFile, outZip)

            Using zip As New ZipArchive(File.OpenRead(outZip), ZipArchiveMode.Read)
                Assert.Null(zip.GetEntry(StreamCatalogSchema.FaviconAtlasEntryName))
                Using ms As New MemoryStream()
                    zip.Entries(0).Open().CopyTo(ms)
                    ms.Position = 0
                    Dim hasBom = False
                    Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                    Assert.Equal("", parsed(1)(17)) ' favicon_index is blank
                End Using
            End Using
        End Sub

        <Fact>
        Public Sub Criteria17To20_StreamCatalogCli_BuildAndCheckVerbs()
            ' Criteria 17-20: Build and check switches work, write reports, and return exit codes
            Dim playlistFile = Path.Combine(testDir, "cli_input.m3u")
            File.WriteAllText(playlistFile, "#EXTM3U" & vbLf &
                                           "#EXTINF:-1,CLI Station" & vbLf &
                                           "https://cli.example.com/stream" & vbLf, Encoding.UTF8)

            Dim outZip = Path.Combine(testDir, "cli_catalog.zip")
            Dim reportFile = Path.Combine(testDir, "cli_catalog-report.txt")

            ' Test Build
            Dim exitCodeBuild = StreamCatalogCli.Run(New String() {"--build-stream-catalog", playlistFile, "-o", outZip})
            Assert.Equal(0, exitCodeBuild)
            Assert.True(File.Exists(outZip))
            Assert.True(File.Exists(reportFile))

            Dim reportText = File.ReadAllText(reportFile)
            Assert.Contains("STREAM-BANK 2.2", reportText)
            Assert.Contains("FINAL VERDICT: PASS", reportText)

            ' Test Check
            Dim exitCodeCheck = StreamCatalogCli.Run(New String() {"--check-stream-catalog", outZip})
            Assert.Equal(0, exitCodeCheck)

            ' Test IsCatalogCommandLine
            Assert.True(StreamCatalogCli.IsCatalogCommandLine(New String() {"--build-stream-catalog", "a.m3u"}))
            Assert.True(StreamCatalogCli.IsCatalogCommandLine(New String() {"--check-stream-catalog", "a.zip"}))
            Assert.False(StreamCatalogCli.IsCatalogCommandLine(New String() {"C:\photos\test.jpg"}))
        End Sub

        <Fact>
        Public Sub Criteria26To28_FrozenSchemaLiteralsAndAdditiveSafety()
            ' Criteria 26-28: Schema literals are pinned, entry name and artifact names are pinned
            Assert.Equal("stream-catalog.zip", StreamCatalogSchema.ArchiveFileName)
            Assert.Equal("streams.csv", StreamCatalogSchema.CatalogCsvEntryName)
            Assert.Equal("favicon-atlas.png", StreamCatalogSchema.FaviconAtlasEntryName)
            Assert.Equal("STREAM-BANK 2.2", StreamCatalogSchema.ContractTag)

            ' 19 columns exact order
            Dim expectedCols = New String() {
                "category", "topic", "name", "url", "media_kind", "protocol", "format", "bitrate",
                "is_live", "https", "language", "country", "homepage", "source_kind", "license_note",
                "notes", "confidence", "favicon_index", "access"
            }
            Assert.Equal(expectedCols.Length, StreamCatalogSchema.ProducerColumns.Count)
            For i = 0 To expectedCols.Length - 1
                Assert.Equal(expectedCols(i), StreamCatalogSchema.ProducerColumns(i))
            Next
        End Sub

    End Class

End Namespace
#End If
