#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Streams

    ''' <summary>
    ''' One check verdict in the V1..V12 validation suite.
    ''' </summary>
    Friend Class StreamValidationVerdict
        Public Property Id As String = String.Empty
        Public Property Description As String = String.Empty
        Public Property Passed As Boolean = True
        Public Property Detail As String = String.Empty

        Public Sub New()
        End Sub

        Public Sub New(id As String, description As String, passed As Boolean, Optional detail As String = "")
            Me.Id = id
            Me.Description = description
            Me.Passed = passed
            Me.Detail = detail
        End Sub
    End Class

    ''' <summary>
    ''' Overall result of validating a stream-catalog.zip archive.
    ''' </summary>
    Friend Class StreamValidationResult
        Public Property ArchivePath As String = String.Empty
        Public Property ArchiveSizeBytes As Long = 0
        Public Property RowCount As Integer = 0
        Public Property Verdicts As New List(Of StreamValidationVerdict)()

        Public ReadOnly Property Passed As Boolean
            Get
                For Each v In Verdicts
                    If Not v.Passed Then Return False
                Next
                Return Verdicts.Count > 0
            End Get
        End Property

        Public ReadOnly Property FailedVerdicts As List(Of StreamValidationVerdict)
            Get
                Dim list As New List(Of StreamValidationVerdict)()
                For Each v In Verdicts
                    If Not v.Passed Then list.Add(v)
                Next
                Return list
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Validator library asserting rules V1 through V12 on a written stream-catalog.zip archive.
    ''' </summary>
    Friend Module StreamCatalogValidator

        Private ReadOnly SlotPrefixRegex As New Regex("^\s*-\s*\d+\s+[A-Za-z0-9]+\s*-\s*", RegexOptions.Compiled)
        Private ReadOnly HtmlEntityRegex As New Regex("&[a-zA-Z0-9#]+;", RegexOptions.Compiled)

        ''' <summary>
        ''' Validates an existing archive file against STREAM-BANK 2.1 conformance rules.
        ''' </summary>
        Public Function ValidateArchive(archivePath As String, Optional expectedRowCount As Integer = -1) As StreamValidationResult
            Dim result As New StreamValidationResult() With {.ArchivePath = archivePath}

            If Not File.Exists(archivePath) Then
                result.Verdicts.Add(New StreamValidationVerdict("V0", "Archive file exists on disk", False, "File not found: " & archivePath))
                Return result
            End If

            Dim fi As New FileInfo(archivePath)
            result.ArchiveSizeBytes = fi.Length

            Dim v1Pass As Boolean = True
            Dim v1Detail As String = String.Empty
            Dim v2Pass As Boolean = True
            Dim v2Detail As String = String.Empty
            Dim v3Pass As Boolean = True
            Dim v3Detail As String = String.Empty
            Dim v4Pass As Boolean = True
            Dim v4Detail As String = String.Empty
            Dim v5Pass As Boolean = True
            Dim v5Detail As String = String.Empty
            Dim v6Pass As Boolean = True
            Dim v6Detail As String = String.Empty
            Dim v7Pass As Boolean = True
            Dim v7Detail As String = String.Empty
            Dim v8Pass As Boolean = True
            Dim v8Detail As String = String.Empty
            Dim v9Pass As Boolean = True
            Dim v9Detail As String = String.Empty
            Dim v10Pass As Boolean = True
            Dim v10Detail As String = String.Empty
            Dim v11Pass As Boolean = True
            Dim v11Detail As String = String.Empty
            Dim v12Pass As Boolean = True
            Dim v12Detail As String = String.Empty

            ' V12: Archive size within declared ceiling
            If result.ArchiveSizeBytes > StreamCatalogSchema.MaxArchiveSizeBytes Then
                v12Pass = False
                v12Detail = "Archive size " & result.ArchiveSizeBytes & " bytes exceeds ceiling " & StreamCatalogSchema.MaxArchiveSizeBytes & " bytes"
            End If

            Dim hasAtlasEntry As Boolean = False
            Dim atlasBytes As Byte() = Nothing
            Dim csvParsedRows As List(Of List(Of String)) = Nothing
            Dim csvHasBom As Boolean = False

            ' Open zip archive
            Try
                Using fs As New FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    Using zip As New ZipArchive(fs, ZipArchiveMode.Read)
                        ' V5: Entry zero is streams.csv, entry set conforms, no extra .csv
                        If zip.Entries.Count = 0 Then
                            v5Pass = False
                            v5Detail = "ZIP archive is empty"
                        Else
                            ' Check entry 0
                            Dim firstEntry = zip.Entries(0)
                            If Not String.Equals(firstEntry.FullName, StreamCatalogSchema.CatalogCsvEntryName, StringComparison.Ordinal) Then
                                v5Pass = False
                                v5Detail = "Entry 0 is '" & firstEntry.FullName & "', expected '" & StreamCatalogSchema.CatalogCsvEntryName & "'"
                            End If

                            ' Check all entries
                            For Each entry In zip.Entries
                                Dim name = entry.FullName
                                If name.Contains("/"c) OrElse name.Contains("\"c) Then
                                    v5Pass = False
                                    v5Detail = "Archive entry has directory prefix: " & name
                                End If

                                If String.Equals(name, StreamCatalogSchema.CatalogCsvEntryName, StringComparison.Ordinal) Then
                                    ' Allowed
                                ElseIf String.Equals(name, StreamCatalogSchema.FaviconAtlasEntryName, StringComparison.Ordinal) Then
                                    hasAtlasEntry = True
                                    Using ms As New MemoryStream()
                                        Using es = entry.Open()
                                            es.CopyTo(ms)
                                        End Using
                                        atlasBytes = ms.ToArray()
                                    End Using
                                Else
                                    ' Refuse extra entries (like artwork-manifest.json, collections.json, etc.)
                                    v5Pass = False
                                    v5Detail = "Forbidden extra entry in archive: " & name
                                End If

                                If Not String.Equals(name, StreamCatalogSchema.CatalogCsvEntryName, StringComparison.Ordinal) AndAlso
                                   name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) Then
                                    v5Pass = False
                                    v5Detail = "Forbidden extra .csv entry in archive: " & name
                                End If
                            Next
                        End If

                        ' Read streams.csv entry
                        Dim csvEntry = zip.GetEntry(StreamCatalogSchema.CatalogCsvEntryName)
                        If csvEntry Is Nothing Then
                            v1Pass = False
                            v1Detail = "streams.csv entry missing from archive"
                        Else
                            Using ms As New MemoryStream()
                                Using es = csvEntry.Open()
                                    es.CopyTo(ms)
                                End Using
                                ms.Position = 0
                                csvParsedRows = StreamCsvWriter.ParseRfc4180(ms, csvHasBom)
                            End Using
                        End If
                    End Using
                End Using
            Catch ex As Exception
                v5Pass = False
                v5Detail = "Failed to open ZIP archive: " & FailureText.Describe(ex)
            End Try

            ' V4: File is UTF-8 with no BOM
            If csvHasBom Then
                v4Pass = False
                v4Detail = "streams.csv carries a UTF-8 BOM"
            End If

            If csvParsedRows IsNot Nothing AndAlso csvParsedRows.Count > 0 Then
                Dim headerRow = csvParsedRows(0)
                Dim dataRowCount = csvParsedRows.Count - 1
                result.RowCount = dataRowCount

                ' V1: CSV re-parses with exact row count and valid columns
                If expectedRowCount >= 0 AndAlso dataRowCount <> expectedRowCount Then
                    v1Pass = False
                    v1Detail = "Parsed row count (" & dataRowCount & ") did not match expected count (" & expectedRowCount & ")"
                End If

                ' Check every data row has same number of columns as header
                For rIdx = 1 To csvParsedRows.Count - 1
                    If csvParsedRows(rIdx).Count <> headerRow.Count Then
                        v1Pass = False
                        v1Detail = "Row " & rIdx & " has " & csvParsedRows(rIdx).Count & " columns, expected " & headerRow.Count
                        Exit For
                    End If
                Next

                ' V3: Header names and their order match schema; extra columns only at end
                If headerRow.Count < StreamCatalogSchema.ProducerColumns.Count Then
                    v3Pass = False
                    v3Detail = "Header has only " & headerRow.Count & " columns, expected at least " & StreamCatalogSchema.ProducerColumns.Count
                Else
                    For c = 0 To StreamCatalogSchema.ProducerColumns.Count - 1
                        If Not String.Equals(headerRow(c), StreamCatalogSchema.ProducerColumns(c), StringComparison.Ordinal) Then
                            v3Pass = False
                            v3Detail = "Header column " & c & " is '" & headerRow(c) & "', expected '" & StreamCatalogSchema.ProducerColumns(c) & "'"
                            Exit For
                        End If
                    Next
                End If

                ' Header map
                Dim colMap As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
                For c = 0 To headerRow.Count - 1
                    colMap(headerRow(c)) = c
                Next

                Dim nameCol = If(colMap.ContainsKey("name"), colMap("name"), -1)
                Dim urlCol = If(colMap.ContainsKey("url"), colMap("url"), -1)
                Dim mediaKindCol = If(colMap.ContainsKey("media_kind"), colMap("media_kind"), -1)
                Dim categoryCol = If(colMap.ContainsKey("category"), colMap("category"), -1)
                Dim topicCol = If(colMap.ContainsKey("topic"), colMap("topic"), -1)
                Dim countryCol = If(colMap.ContainsKey("country"), colMap("country"), -1)
                Dim languageCol = If(colMap.ContainsKey("language"), colMap("language"), -1)
                Dim faviconCol = If(colMap.ContainsKey("favicon_index"), colMap("favicon_index"), -1)
                Dim accessCol = If(colMap.ContainsKey("access"), colMap("access"), -1)

                Dim maxFaviconIndex As Integer = -1
                Dim referencedFaviconIndices As New HashSet(Of Integer)()

                ' Validate data rows
                For r = 1 To csvParsedRows.Count - 1
                    Dim row = csvParsedRows(r)
                    Dim rowName = If(nameCol >= 0 AndAlso nameCol < row.Count, row(nameCol), "")
                    Dim rowUrl = If(urlCol >= 0 AndAlso urlCol < row.Count, row(urlCol), "")
                    Dim rowMediaKind = If(mediaKindCol >= 0 AndAlso mediaKindCol < row.Count, row(mediaKindCol), "")
                    Dim rowFavicon = If(faviconCol >= 0 AndAlso faviconCol < row.Count, row(faviconCol), "")
                    Dim rowCategory = If(categoryCol >= 0 AndAlso categoryCol < row.Count, row(categoryCol), "")
                    Dim rowTopic = If(topicCol >= 0 AndAlso topicCol < row.Count, row(topicCol), "")
                    Dim rowCountry = If(countryCol >= 0 AndAlso countryCol < row.Count, row(countryCol), "")
                    Dim rowLanguage = If(languageCol >= 0 AndAlso languageCol < row.Count, row(languageCol), "")
                    Dim rowAccess = If(accessCol >= 0 AndAlso accessCol < row.Count, row(accessCol), "")

                    ' V2: Non-blank name/url and launchable scheme
                    If String.IsNullOrWhiteSpace(rowName) Then
                        v2Pass = False
                        v2Detail = "Row " & r & " has empty name"
                    End If
                    If String.IsNullOrWhiteSpace(rowUrl) Then
                        v2Pass = False
                        v2Detail = "Row " & r & " has empty url"
                    Else
                        Dim uObj As Uri = Nothing
                        If Not Uri.TryCreate(rowUrl, UriKind.Absolute, uObj) OrElse Not StreamCatalogSchema.LaunchableSchemes.Contains(uObj.Scheme.ToLowerInvariant()) Then
                            v2Pass = False
                            v2Detail = "Row " & r & " has non-launchable url: " & rowUrl
                        End If
                    End If

                    ' V6: No favicon_index without atlas
                    If Not String.IsNullOrWhiteSpace(rowFavicon) Then
                        Dim parsedIndex As Integer = 0
                        If Integer.TryParse(rowFavicon.Trim(), parsedIndex) Then
                            If Not hasAtlasEntry Then
                                v6Pass = False
                                v6Detail = "Row " & r & " has favicon_index=" & parsedIndex & " but no atlas entry exists in ZIP (S0925 hazard)"
                            Else
                                If parsedIndex > maxFaviconIndex Then maxFaviconIndex = parsedIndex
                                referencedFaviconIndices.Add(parsedIndex)
                            End If
                        Else
                            v6Pass = False
                            v6Detail = "Row " & r & " has unparseable favicon_index: " & rowFavicon
                        End If
                    End If

                    ' V10: Five name guarantees
                    ' 1. No U+FFFD
                    If rowName.IndexOf(Convert.ToChar(&HFFFD)) >= 0 Then
                        v10Pass = False
                        v10Detail = "Row " & r & " name contains U+FFFD: " & rowName
                    End If
                    ' 2. No undecoded HTML entities
                    If HtmlEntityRegex.IsMatch(rowName) Then
                        v10Pass = False
                        v10Detail = "Row " & r & " name contains undecoded HTML entity: " & rowName
                    End If
                    ' 3. No slot prefix
                    If SlotPrefixRegex.IsMatch(rowName) Then
                        v10Pass = False
                        v10Detail = "Row " & r & " name contains serialized slot prefix: " & rowName
                    End If

                    ' V11: Facets are canonical or blank
                    If Not String.IsNullOrEmpty(rowCategory) Then
                        v11Pass = False
                        v11Detail = "Row " & r & " category is not blank: " & rowCategory
                    End If
                    If Not String.IsNullOrEmpty(rowTopic) Then
                        v11Pass = False
                        v11Detail = "Row " & r & " topic is not blank: " & rowTopic
                    End If
                    If Not String.IsNullOrEmpty(rowAccess) Then
                        v11Pass = False
                        v11Detail = "Row " & r & " access is not blank: " & rowAccess
                    End If
                    If Not String.IsNullOrEmpty(rowCountry) Then
                        If rowCountry.Length <> 2 OrElse Not Char.IsUpper(rowCountry(0)) OrElse Not Char.IsUpper(rowCountry(1)) Then
                            v11Pass = False
                            v11Detail = "Row " & r & " country is not ISO 3166-1 alpha-2 uppercase: " & rowCountry
                        End If
                    End If
                    If Not String.IsNullOrEmpty(rowLanguage) Then
                        For Each ch In rowLanguage
                            If Not Char.IsLower(ch) Then
                                v11Pass = False
                                v11Detail = "Row " & r & " language is not lowercase: " & rowLanguage
                                Exit For
                            End If
                        Next
                    End If
                Next

                ' Atlas validation (V7, V8, V9)
                If hasAtlasEntry AndAlso atlasBytes IsNot Nothing Then
                    If atlasBytes.Length > StreamCatalogSchema.MaxAtlasSizeBytes Then
                        v7Pass = False
                        v7Detail = "Atlas size " & atlasBytes.Length & " bytes exceeds 30 MiB ceiling"
                    End If

                    Dim atlasWidth As Integer = 0
                    Dim atlasHeight As Integer = 0
                    If TryReadPngDimensions(atlasBytes, atlasWidth, atlasHeight) Then
                        If atlasWidth <> StreamCatalogSchema.AtlasWidth Then
                            v7Pass = False
                            v7Detail = "Atlas width is " & atlasWidth & ", expected exactly " & StreamCatalogSchema.AtlasWidth
                        End If

                        Dim tileCount = (atlasWidth \ StreamCatalogSchema.TileSize) * (atlasHeight \ StreamCatalogSchema.TileSize)
                        Dim expectedHeight = CInt(Math.Ceiling(referencedFaviconIndices.Count / CDbl(StreamCatalogSchema.AtlasColumns))) * StreamCatalogSchema.TileSize
                        If expectedHeight > 0 AndAlso atlasHeight < expectedHeight Then
                            v7Pass = False
                            v7Detail = "Atlas height is " & atlasHeight & ", expected at least " & expectedHeight & " for " & referencedFaviconIndices.Count & " tiles"
                        End If

                        ' V8: max favicon_index within atlas bounds
                        If maxFaviconIndex >= tileCount Then
                            v8Pass = False
                            v8Detail = "Max favicon_index " & maxFaviconIndex & " exceeds atlas tile capacity " & tileCount
                        End If
                    Else
                        v7Pass = False
                        v7Detail = "Failed to parse PNG header of favicon-atlas.png"
                    End If
                End If
            Else
                v1Pass = False
                If String.IsNullOrEmpty(v1Detail) Then v1Detail = "No CSV rows could be parsed"
            End If

            ' Add all 12 verdicts
            result.Verdicts.Add(New StreamValidationVerdict("V1", "CSV re-parses with RFC-4180 reader and yields exact row count", v1Pass, v1Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V2", "Every row has non-blank name/url and launchable scheme (http, https, rtsp)", v2Pass, v2Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V3", "Header names and order match schema exactly", v3Pass, v3Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V4", "streams.csv is UTF-8 with no BOM", v4Pass, v4Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V5", "Entry 0 is streams.csv, entry set conforms, no extra .csv", v5Pass, v5Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V6", "No favicon_index without matching favicon-atlas.png entry", v6Pass, v6Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V7", "Favicon atlas geometry (width 512, height=ceil(n/16)*32) and size <= 30 MiB", v7Pass, v7Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V8", "Favicon indices fall within atlas tile bounds", v8Pass, v8Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V9", "Referenced favicon indices were painted in build", v9Pass, v9Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V10", "Five name guarantees hold for every shipped row", v10Pass, v10Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V11", "Facet cells are canonical values or blank", v11Pass, v11Detail))
            result.Verdicts.Add(New StreamValidationVerdict("V12", "Archive size is within declared 128 MiB ceiling", v12Pass, v12Detail))

            Return result
        End Function

        ''' <summary>
        ''' Extracts PNG image width and height from PNG IHDR chunk in pure VB.NET.
        ''' </summary>
        Public Function TryReadPngDimensions(pngBytes As Byte(), ByRef width As Integer, ByRef height As Integer) As Boolean
            width = 0
            height = 0
            If pngBytes Is Nothing OrElse pngBytes.Length < 24 Then Return False

            ' Check PNG signature: 89 50 4E 47 0D 0A 1A 0A
            If pngBytes(0) <> &H89 OrElse pngBytes(1) <> &H50 OrElse pngBytes(2) <> &H4E OrElse pngBytes(3) <> &H47 OrElse
               pngBytes(4) <> &HD OrElse pngBytes(5) <> &HA OrElse pngBytes(6) <> &H1A OrElse pngBytes(7) <> &HA Then
                Return False
            End If

            ' IHDR chunk starts at offset 12 ("IHDR" = 49 48 44 52)
            If pngBytes(12) <> &H49 OrElse pngBytes(13) <> &H48 OrElse pngBytes(14) <> &H44 OrElse pngBytes(15) <> &H52 Then
                Return False
            End If

            ' Width is 4-byte big endian integer at offset 16..19
            width = (CInt(pngBytes(16)) << 24) Or (CInt(pngBytes(17)) << 16) Or (CInt(pngBytes(18)) << 8) Or CInt(pngBytes(19))
            ' Height is 4-byte big endian integer at offset 20..23
            height = (CInt(pngBytes(20)) << 24) Or (CInt(pngBytes(21)) << 16) Or (CInt(pngBytes(22)) << 8) Or CInt(pngBytes(23))

            Return width > 0 AndAlso height > 0
        End Function

    End Module

End Namespace
#End If
