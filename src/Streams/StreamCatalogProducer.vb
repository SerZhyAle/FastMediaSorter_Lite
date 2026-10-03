#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq

Namespace Streams

    ''' <summary>
    ''' Delegate for confirming large catalog churn when replacing an existing stream-catalog.zip archive.
    ''' </summary>
    ''' <param name="lostCount">Number of previous URLs missing in the new catalog.</param>
    ''' <param name="previousCount">Total row count of the previous catalog.</param>
    ''' <param name="lossPercent">Calculated loss percentage.</param>
    ''' <param name="mostAffectedPlaylist">Name of playlist most affected if known.</param>
    ''' <returns>True to proceed with replacement; False to cancel.</returns>
    Friend Delegate Function ChurnConfirmationCallback(lostCount As Integer, previousCount As Integer, lossPercent As Double, mostAffectedPlaylist As String) As Boolean

    ''' <summary>
    ''' Result of the StreamCatalogProducer build process.
    ''' </summary>
    Friend Class StreamCatalogBuildResult
        Public Property Success As Boolean = False
        Public Property OutputArchivePath As String = String.Empty
        Public Property ReportFilePath As String = String.Empty
        Public Property Report As StreamCatalogReport
        Public Property ChurnRefused As Boolean = False
        Public Property ErrorMessage As String = String.Empty
        Public Property ErrorDescription As String
            Get
                Return ErrorMessage
            End Get
            Set(value As String)
                ErrorMessage = value
            End Set
        End Property
    End Class

    ''' <summary>
    ''' Producer pipeline turning M3U/M3U8 playlists into conforming stream-catalog.zip archives.
    ''' Implements atomic replacement, validation from written bytes, and churn prevention.
    ''' </summary>
    Friend Module StreamCatalogProducer

        ''' <summary>
        ''' Builds a stream-catalog.zip archive from the specified input file or folder.
        ''' </summary>
        Public Function Build(inputPath As String, outputZipPath As String,
                             Optional forceChurn As Boolean = False,
                             Optional churnCallback As ChurnConfirmationCallback = Nothing) As StreamCatalogBuildResult

            Dim buildResult As New StreamCatalogBuildResult() With {
                .OutputArchivePath = outputZipPath
            }

            Dim reportDir = Path.GetDirectoryName(Path.GetFullPath(outputZipPath))
            Dim reportBase = Path.GetFileNameWithoutExtension(outputZipPath)
            Dim reportPath = Path.Combine(reportDir, reportBase & "-report.txt")
            buildResult.ReportFilePath = reportPath

            Dim report As New StreamCatalogReport() With {
                .Mode = "BUILD",
                .InputPath = inputPath,
                .ArchivePath = outputZipPath
            }
            buildResult.Report = report

            ' 1. Read input playlists
            If String.IsNullOrWhiteSpace(inputPath) OrElse (Not File.Exists(inputPath) AndAlso Not Directory.Exists(inputPath)) Then
                buildResult.ErrorDescription = "Input path does not exist: " & inputPath
                report.Refusals.Add(New StreamRefusal(inputPath, 0, "", "", buildResult.ErrorDescription, True))
                report.ValidationResult = New StreamValidationResult() With {.ArchivePath = outputZipPath}
                report.ValidationResult.Verdicts.Add(New StreamValidationVerdict("V0", "Input exists", False, buildResult.ErrorDescription))
                report.WriteToFile(reportPath)
                Return buildResult
            End If

            Dim parseResult = M3uPlaylistReader.ReadPath(inputPath)
            report.TotalEntriesFound = parseResult.TotalEntriesFound
            report.Refusals.AddRange(parseResult.Refusals)

            ' 2. Deduplicate on channel identity key (03 §2.5 row identity fold)
            Dim canonicalRows As New List(Of StreamCatalogRow)()
            Dim identityMap As New Dictionary(Of String, StreamCatalogRow)(StringComparer.Ordinal)

            For Each candidate In parseResult.Rows
                Dim key = candidate.NormalizedIdentity
                If String.IsNullOrEmpty(key) Then
                    key = StreamIdentity.GetIdentityKey(candidate.Url)
                End If

                If Not identityMap.ContainsKey(key) Then
                    identityMap(key) = candidate
                    canonicalRows.Add(candidate)
                Else
                    Dim existing = identityMap(key)
                    ' If candidate is HTTPS and existing is HTTP, upgrade existing URL
                    If candidate.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso
                       existing.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then
                        existing.Url = candidate.Url
                        existing.Https = "true"
                    End If

                    ' Record fold
                    Dim foldRecord As New StreamRefusal(candidate.SourceFile, candidate.SourceLine, candidate.Name, candidate.Url,
                                                       "Folded duplicate channel identity", False, existing.Url)
                    report.Folds.Add(foldRecord)
                End If
            Next

            report.ShippedRowCount = canonicalRows.Count

            ' Collect unrecognized declared kinds
            For Each row In canonicalRows
                If Not String.IsNullOrEmpty(row.UnrecognizedDeclaredKind) Then
                    report.UnrecognizedKinds.Add(row)
                End If
            Next

            ' 3. Churn guard (§6.4)
            If File.Exists(outputZipPath) Then
                Dim previousUrls As List(Of String) = ReadExistingArchiveUrls(outputZipPath)
                Dim prevCount = previousUrls.Count

                If prevCount >= StreamCatalogSchema.ChurnThresholdMinRows Then
                    Dim newIdentitySet As New HashSet(Of String)(StringComparer.Ordinal)
                    For Each r In canonicalRows
                        newIdentitySet.Add(r.NormalizedIdentity)
                    Next

                    Dim lostCount As Integer = 0
                    For Each prevUrl In previousUrls
                        Dim prevKey = StreamIdentity.GetIdentityKey(prevUrl)
                        If Not newIdentitySet.Contains(prevKey) Then
                            lostCount += 1
                        End If
                    Next

                    Dim lossPercent = (lostCount / CDbl(prevCount)) * 100.0
                    If lossPercent > StreamCatalogSchema.ChurnThresholdPercent Then
                        If churnCallback IsNot Nothing Then
                            Dim confirmed = churnCallback(lostCount, prevCount, lossPercent, Path.GetFileName(inputPath))
                            If Not confirmed Then
                                buildResult.ChurnRefused = True
                                buildResult.ErrorDescription = "Build cancelled: Churn guard stopped replacement (" & lostCount & "/" & prevCount & " URLs lost, " & lossPercent.ToString("0.0") & "%)"
                                report.Refusals.Add(New StreamRefusal(outputZipPath, 0, "", "", buildResult.ErrorDescription, True))
                                report.WriteToFile(reportPath)
                                Return buildResult
                            End If
                        ElseIf Not forceChurn Then
                            buildResult.ChurnRefused = True
                            buildResult.ErrorDescription = "Churn guard refused replacement: " & lostCount & " of " & prevCount & " previous URLs (" & lossPercent.ToString("0.0") & "%) would be lost. Use --force to override."
                            report.Refusals.Add(New StreamRefusal(outputZipPath, 0, "", "", buildResult.ErrorDescription, True))
                            report.WriteToFile(reportPath)
                            Return buildResult
                        End If
                    End If
                End If
            End If

            ' 4. Assemble to temporary file atomically (§6.4)
            Dim tempZipPath = Path.Combine(Path.GetTempPath(), "fms_stream_cat_" & Guid.NewGuid().ToString("N") & ".zip")
            Try
                ' Create temp zip with streams.csv as entry zero
                Using fs As New FileStream(tempZipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
                    Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                        Dim csvEntry = zip.CreateEntry(StreamCatalogSchema.CatalogCsvEntryName, CompressionLevel.Optimal)
                        Using entryStream = csvEntry.Open()
                            StreamCsvWriter.WriteCsv(canonicalRows, entryStream)
                        End Using
                    End Using
                End Using

                ' 5. Validate written bytes against V1..V12
                Dim validation = StreamCatalogValidator.ValidateArchive(tempZipPath, canonicalRows.Count)
                report.ValidationResult = validation

                If Not validation.Passed Then
                    ' Validation failed: delete temp zip, leave existing destination untouched
                    Try
                        If File.Exists(tempZipPath) Then File.Delete(tempZipPath)
                    Catch
                    End Try

                    buildResult.Success = False
                    buildResult.ErrorDescription = "Validation failed on written archive"
                    report.WriteToFile(reportPath)
                    Return buildResult
                End If

                ' 6. Atomic replacement
                Dim destinationDir = Path.GetDirectoryName(Path.GetFullPath(outputZipPath))
                If Not Directory.Exists(destinationDir) Then
                    Directory.CreateDirectory(destinationDir)
                End If

                If File.Exists(outputZipPath) Then
                    File.Delete(outputZipPath)
                End If
                File.Move(tempZipPath, outputZipPath)

                ' Update report with finalized artifact stats
                report.ArchivePath = outputZipPath
                report.ValidationResult.ArchivePath = outputZipPath
                report.WriteToFile(reportPath)

                buildResult.Success = True
                Return buildResult

            Catch ex As Exception
                Try
                    If File.Exists(tempZipPath) Then File.Delete(tempZipPath)
                Catch
                End Try

                buildResult.Success = False
                buildResult.ErrorDescription = "Exception during catalog build: " & FailureText.Describe(ex)
                If report.ValidationResult Is Nothing Then
                    report.ValidationResult = New StreamValidationResult() With {.ArchivePath = outputZipPath}
                End If
                report.ValidationResult.Verdicts.Add(New StreamValidationVerdict("V0", "Build execution", False, FailureText.Describe(ex)))
                report.WriteToFile(reportPath)
                Return buildResult
            End Try
        End Function

        ''' <summary>
        ''' Reads stream URLs from an existing conforming stream-catalog.zip.
        ''' </summary>
        Public Function ReadExistingArchiveUrls(archivePath As String) As List(Of String)
            Dim urls As New List(Of String)()
            If Not File.Exists(archivePath) Then Return urls

            Try
                Using fs As New FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    Using zip As New ZipArchive(fs, ZipArchiveMode.Read)
                        Dim csvEntry = zip.GetEntry(StreamCatalogSchema.CatalogCsvEntryName)
                        If csvEntry IsNot Nothing Then
                            Using ms As New MemoryStream()
                                Using es = csvEntry.Open()
                                    es.CopyTo(ms)
                                End Using
                                ms.Position = 0
                                Dim hasBom As Boolean = False
                                Dim parsed = StreamCsvWriter.ParseRfc4180(ms, hasBom)
                                If parsed.Count > 1 Then
                                    Dim header = parsed(0)
                                    Dim urlCol As Integer = -1
                                    For c = 0 To header.Count - 1
                                        If String.Equals(header(c), "url", StringComparison.OrdinalIgnoreCase) Then
                                            urlCol = c
                                            Exit For
                                        End If
                                    Next
                                    If urlCol >= 0 Then
                                        For r = 1 To parsed.Count - 1
                                            Dim row = parsed(r)
                                            If urlCol < row.Count AndAlso Not String.IsNullOrWhiteSpace(row(urlCol)) Then
                                                urls.Add(row(urlCol).Trim())
                                            End If
                                        Next
                                    End If
                                End If
                            End Using
                        End If
                    End Using
                End Using
            Catch
            End Try

            Return urls
        End Function

    End Module

End Namespace
#End If
