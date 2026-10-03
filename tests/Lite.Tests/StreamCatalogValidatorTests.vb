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
    ''' Acceptance tests for Stream Catalog Validator (022_SPECIFICATION_PC_STREAM_CATALOG_PRODUCER.md, Criteria 13-16).
    ''' Proves that each of V1..V12 fails when given a deliberately damaged archive.
    ''' </summary>
    Public Class StreamCatalogValidatorTests
        Implements IDisposable

        Private ReadOnly testDir As String

        Public Sub New()
            testDir = Path.Combine(Path.GetTempPath(), "fms_test_validator_" & Guid.NewGuid().ToString("N"))
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

        Private Function CreateValidZip(zipPath As String, Optional rows As List(Of StreamCatalogRow) = Nothing) As String
            If rows Is Nothing Then
                rows = New List(Of StreamCatalogRow)() From {
                    New StreamCatalogRow() With {
                        .Name = "Valid Channel",
                        .Url = "https://example.com/stream",
                        .MediaKind = "AUDIO",
                        .Https = "true"
                    }
                }
            End If

            Using fs As New FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim entry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName, CompressionLevel.Optimal)
                    Using s = entry.Open()
                        StreamCsvWriter.WriteCsv(rows, s)
                    End Using
                End Using
            End Using
            Return zipPath
        End Function

        <Fact>
        Public Sub Criterion13_ValidArchive_PassesAll12Verdicts()
            Dim zipPath = Path.Combine(testDir, "valid.zip")
            CreateValidZip(zipPath)

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath, 1)
            Assert.True(result.Passed)
            Assert.Equal(12, result.Verdicts.Count)
            For Each v In result.Verdicts
                Assert.True(v.Passed, "Check " & v.Id & " unexpectedly failed: " & v.Detail)
            Next
        End Sub

        <Fact>
        Public Sub Criterion14_V1_FailsOnShiftedColumnsOrBadCsv()
            Dim zipPath = Path.Combine(testDir, "v1_bad.zip")
            Using fs As New FileStream(zipPath, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim entry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(entry.Open(), StreamCsvWriter.Utf8NoBom)
                        ' Header with 19 columns
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                        ' Row with only 3 columns (shifted)
                        sw.WriteLine("""cat"",""top"",""name""")
                    End Using
                End Using
            End Using

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v1 = result.Verdicts.Find(Function(v) v.Id = "V1")
            Assert.NotNull(v1)
            Assert.False(v1.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V2_FailsOnMissingNameUrlOrNonLaunchableScheme()
            Dim zipPath = Path.Combine(testDir, "v2_bad.zip")
            Dim row As New StreamCatalogRow() With {
                .Name = "UDP Channel",
                .Url = "udp://239.255.0.1:5000",
                .MediaKind = "VIDEO"
            }
            CreateValidZip(zipPath, New List(Of StreamCatalogRow)() From {row})

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v2 = result.Verdicts.Find(Function(v) v.Id = "V2")
            Assert.NotNull(v2)
            Assert.False(v2.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V3_FailsOnRenamedOrReorderedHeader()
            Dim zipPath = Path.Combine(testDir, "v3_bad.zip")
            Using fs As New FileStream(zipPath, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim entry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(entry.Open(), StreamCsvWriter.Utf8NoBom)
                        ' Renamed first header
                        sw.WriteLine("""category_renamed"",""topic"",""name"",""url"",""media_kind"",""protocol"",""format"",""bitrate"",""is_live"",""https"",""language"",""country"",""homepage"",""source_kind"",""license_note"",""notes"",""confidence"",""favicon_index"",""access""")
                        sw.WriteLine(""""","""",""A"",""https://a.com"",""AUDIO"","""","""","""","""",""true"","""","""","""","""","""","""","""","""",""""")
                    End Using
                End Using
            End Using

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v3 = result.Verdicts.Find(Function(v) v.Id = "V3")
            Assert.NotNull(v3)
            Assert.False(v3.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V4_FailsOnUtf8BomInCsv()
            Dim zipPath = Path.Combine(testDir, "v4_bom.zip")
            Using fs As New FileStream(zipPath, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim entry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(entry.Open(), New UTF8Encoding(True)) ' With BOM
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                        sw.WriteLine(""""","""",""A"",""https://a.com"",""AUDIO"","""","""","""","""",""true"","""","""","""","""","""","""","""","""",""""")
                    End Using
                End Using
            End Using

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v4 = result.Verdicts.Find(Function(v) v.Id = "V4")
            Assert.NotNull(v4)
            Assert.False(v4.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_15_V5_FailsWhenEntryZeroNotCsvOrStrayCsvOrManifestPresent()
            ' Entry zero not streams.csv
            Dim zipPath1 = Path.Combine(testDir, "v5_not_first.zip")
            Using fs As New FileStream(zipPath1, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim textEntry = zip.CreateEntry("readme.txt")
                    Using sw As New StreamWriter(textEntry.Open())
                        sw.WriteLine("Hello")
                    End Using
                    Dim csvEntry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(csvEntry.Open(), StreamCsvWriter.Utf8NoBom)
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                    End Using
                End Using
            End Using

            Dim result1 = StreamCatalogValidator.ValidateArchive(zipPath1)
            Assert.False(result1.Passed)
            Dim v5_1 = result1.Verdicts.Find(Function(v) v.Id = "V5")
            Assert.NotNull(v5_1)
            Assert.False(v5_1.Passed)

            ' Stray second .csv entry in archive
            Dim zipPath2 = Path.Combine(testDir, "v5_second_csv.zip")
            Using fs As New FileStream(zipPath2, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim csvEntry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(csvEntry.Open(), StreamCsvWriter.Utf8NoBom)
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                    End Using
                    Dim extraCsv = zip.CreateEntry("fallback.csv")
                    Using sw As New StreamWriter(extraCsv.Open())
                        sw.WriteLine("bad")
                    End Using
                End Using
            End Using

            Dim result2 = StreamCatalogValidator.ValidateArchive(zipPath2)
            Assert.False(result2.Passed)
            Dim v5_2 = result2.Verdicts.Find(Function(v) v.Id = "V5")
            Assert.NotNull(v5_2)
            Assert.False(v5_2.Passed)

            ' Stray artwork-manifest.json
            Dim zipPath3 = Path.Combine(testDir, "v5_manifest.zip")
            Using fs As New FileStream(zipPath3, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim csvEntry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(csvEntry.Open(), StreamCsvWriter.Utf8NoBom)
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                    End Using
                    Dim manifest = zip.CreateEntry("artwork-manifest.json")
                    Using sw As New StreamWriter(manifest.Open())
                        sw.WriteLine("{}")
                    End Using
                End Using
            End Using

            Dim result3 = StreamCatalogValidator.ValidateArchive(zipPath3)
            Assert.False(result3.Passed)
            Dim v5_3 = result3.Verdicts.Find(Function(v) v.Id = "V5")
            Assert.NotNull(v5_3)
            Assert.False(v5_3.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V6_FailsOnFaviconIndexWithoutAtlas()
            ' V6: S0925 hazard - favicon_index without atlas in archive
            Dim zipPath = Path.Combine(testDir, "v6_hazard.zip")
            Dim row As New StreamCatalogRow() With {
                .Name = "Channel",
                .Url = "https://example.com/live",
                .MediaKind = "AUDIO",
                .Https = "true",
                .FaviconIndex = "0" ' Index present without atlas
            }
            CreateValidZip(zipPath, New List(Of StreamCatalogRow)() From {row})

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v6 = result.Verdicts.Find(Function(v) v.Id = "V6")
            Assert.NotNull(v6)
            Assert.False(v6.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V7_V8_FailsOnAtlasGeometryOrBoundsViolation()
            ' V7/V8: Atlas exists but width is wrong (not 512)
            Dim zipPath = Path.Combine(testDir, "v7_bad_atlas.zip")
            Dim dummyPng = CreateDummyPng(256, 32) ' Wrong width 256

            Using fs As New FileStream(zipPath, FileMode.Create)
                Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                    Dim csvEntry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName)
                    Using sw As New StreamWriter(csvEntry.Open(), StreamCsvWriter.Utf8NoBom)
                        sw.WriteLine(String.Join(",", StreamCatalogSchema.ProducerColumns))
                        sw.WriteLine(""""","""",""A"",""https://a.com"",""AUDIO"","""","""","""","""",""true"","""","""","""","""","""","""","""",""0"",""""")
                    End Using
                    Dim atlasEntry = zip.CreateEntry(StreamCatalogSchema.FaviconAtlasEntryName)
                    Using s = atlasEntry.Open()
                        s.Write(dummyPng, 0, dummyPng.Length)
                    End Using
                End Using
            End Using

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v7 = result.Verdicts.Find(Function(v) v.Id = "V7")
            Assert.NotNull(v7)
            Assert.False(v7.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V10_FailsOnNameGuaranteeViolations()
            ' V10: Name contains U+FFFD or undecoded HTML entity or slot prefix
            Dim zipPath = Path.Combine(testDir, "v10_bad.zip")
            Dim row As New StreamCatalogRow() With {
                .Name = "Station &amp; Pop", ' Undecoded HTML entity
                .Url = "https://example.com/live",
                .MediaKind = "AUDIO",
                .Https = "true"
            }
            CreateValidZip(zipPath, New List(Of StreamCatalogRow)() From {row})

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v10 = result.Verdicts.Find(Function(v) v.Id = "V10")
            Assert.NotNull(v10)
            Assert.False(v10.Passed)
        End Sub

        <Fact>
        Public Sub Criterion14_V11_FailsOnNonCanonicalFacets()
            ' V11: Non-blank category or non-canonical country
            Dim zipPath = Path.Combine(testDir, "v11_bad.zip")
            Dim row As New StreamCatalogRow() With {
                .Name = "Station",
                .Url = "https://example.com/live",
                .MediaKind = "AUDIO",
                .Category = "InventedCategory", ' Must be blank
                .Https = "true"
            }
            CreateValidZip(zipPath, New List(Of StreamCatalogRow)() From {row})

            Dim result = StreamCatalogValidator.ValidateArchive(zipPath)
            Assert.False(result.Passed)
            Dim v11 = result.Verdicts.Find(Function(v) v.Id = "V11")
            Assert.NotNull(v11)
            Assert.False(v11.Passed)
        End Sub

        Private Function CreateDummyPng(width As Integer, height As Integer) As Byte()
            Dim bytes(32) As Byte
            ' PNG Signature: 89 50 4E 47 0D 0A 1A 0A
            bytes(0) = &H89
            bytes(1) = &H50
            bytes(2) = &H4E
            bytes(3) = &H47
            bytes(4) = &HD
            bytes(5) = &HA
            bytes(6) = &H1A
            bytes(7) = &HA
            ' IHDR chunk length 13
            bytes(8) = 0
            bytes(9) = 0
            bytes(10) = 0
            bytes(11) = 13
            ' "IHDR"
            bytes(12) = &H49
            bytes(13) = &H48
            bytes(14) = &H44
            bytes(15) = &H52
            ' Width (big endian)
            bytes(16) = CByte((width >> 24) And &HFF)
            bytes(17) = CByte((width >> 16) And &HFF)
            bytes(18) = CByte((width >> 8) And &HFF)
            bytes(19) = CByte(width And &HFF)
            ' Height (big endian)
            bytes(20) = CByte((height >> 24) And &HFF)
            bytes(21) = CByte((height >> 16) And &HFF)
            bytes(22) = CByte((height >> 8) And &HFF)
            bytes(23) = CByte(height And &HFF)
            ' Bit depth 8, Color type 6 (RGBA)
            bytes(24) = 8
            bytes(25) = 6
            Return bytes
        End Function

    End Class

End Namespace
#End If
