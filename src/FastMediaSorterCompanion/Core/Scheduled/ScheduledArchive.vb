Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading

' The archive action of a scheduled operation (ticket 222, D4): one zip per run, entries carrying the
' relative path, the originals kept - unless "delete originals after verification" is set, in which
' case the archive is READ BACK and every entry checked (name, size, CRC-32) against what was written
' before a single original is touched.
'
' The proof is deliberately independent of the writer's own bookkeeping where it can be: the CRC is
' computed from the SOURCE bytes as they stream into the entry, and compared against the CRC of what
' the reader decompresses from the finished file. A zip that cannot be read, or reads back different
' bytes, leaves every original in place.
Friend NotInheritable Class ArchiveEntryRecord
    Friend ReadOnly Entry As SideEntry
    Friend ReadOnly EntryName As String
    Friend ReadOnly Length As Long
    Friend ReadOnly Crc As UInteger
    ''' <summary>False when the file changed while it was read: the entry is a snapshot of the bytes that
    ''' were read, not the file that was listed, so its original is never a candidate for removal.</summary>
    Friend ReadOnly Stable As Boolean

    Friend Sub New(entry As SideEntry, entryName As String, length As Long, crc As UInteger, stable As Boolean)
        Me.Entry = entry
        Me.EntryName = entryName
        Me.Length = length
        Me.Crc = crc
        Me.Stable = stable
    End Sub
End Class

Friend NotInheritable Class ArchiveWriteResult
    ''' <summary>The final path of the archive, or "" when nothing was written (an empty set).</summary>
    Friend Property ArchivePath As String = ""
    Friend ReadOnly Written As New List(Of ArchiveEntryRecord)()
    Friend ReadOnly Failed As New List(Of KeyValuePair(Of SideEntry, String))()
    Friend Property Bytes As Long
End Class

Friend NotInheritable Class ScheduledArchiveWriter

    ''' <summary>"&lt;source folder name&gt;_&lt;yyyyMMdd_HHmm&gt;.zip" - the folder name sanitised for a file
    ''' name; an unnameable folder (a drive root) becomes "archive".</summary>
    Friend Shared Function ArchiveFileName(sourceFolder As String, at As DateTime) As String
        Dim name As String = ""
        Try
            name = System.IO.Path.GetFileName(sourceFolder.TrimEnd("\"c, "/"c))
        Catch
        End Try
        Dim cleaned As New StringBuilder()
        Dim invalid As Char() = System.IO.Path.GetInvalidFileNameChars()
        For Each c As Char In name
            cleaned.Append(If(Array.IndexOf(invalid, c) >= 0, "_"c, c))
        Next
        Dim stem As String = cleaned.ToString().Trim().TrimEnd("."c)
        If stem.Length = 0 Then stem = "archive"
        Return stem & "_" & at.ToString("yyyyMMdd_HHmm", Globalization.CultureInfo.InvariantCulture) & ".zip"
    End Function

    ''' <summary>The entry name: the relative path, forward slashes, nothing that could climb out.</summary>
    Friend Shared Function EntryNameOf(relativePath As String) As String
        Dim parts As String() = relativePath.Replace("\"c, "/"c).Split("/"c)
        Dim kept As New List(Of String)()
        For Each part As String In parts
            If part.Length = 0 OrElse part = "." OrElse part = ".." Then Continue For
            kept.Add(part)
        Next
        Return String.Join("/", kept)
    End Function

    ''' <summary>
    ''' Writes the zip next to its final name and renames it into place only when whole. A file that
    ''' cannot be read, or that changes while it is read, is recorded as failed and left OUT of the zip.
    ''' When <paramref name="destination"/> is a non-local side (such as Google Drive), the zip is built in a
    ''' local staging file, verified, and uploaded to the destination side.
    ''' </summary>
    Friend Shared Function Write(source As IScheduledSide, entries As IReadOnlyList(Of SideEntry), finalPath As String,
                                 ct As CancellationToken, Optional destination As IScheduledSide = Nothing,
                                 Optional afterWritten As Action(Of String) = Nothing) As ArchiveWriteResult
        Dim result As New ArchiveWriteResult()
        If entries.Count = 0 Then Return result

        Dim remoteDest As Boolean = destination IsNot Nothing AndAlso Not destination.IsLocalPath
        Dim localBasePath As String = If(remoteDest,
            Path.Combine(Path.GetTempPath(), "fms-archive-" & Guid.NewGuid().ToString("N"), Path.GetFileName(finalPath.Replace("/"c, "\"c))),
            finalPath)
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localBasePath))
        Dim temp As String = localBasePath & LocalTransfer.PartSuffix
        If System.IO.File.Exists(temp) Then System.IO.File.Delete(temp)
        Try
            Using output As New FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                Using zip As New ZipArchive(output, ZipArchiveMode.Create, leaveOpen:=False, entryNameEncoding:=Encoding.UTF8)
                    For Each entry As SideEntry In entries
                        ct.ThrowIfCancellationRequested()
                        AddEntry(zip, source, entry, result, ct)
                    Next
                End Using
            End Using
            If result.Written.Count = 0 Then
                System.IO.File.Delete(temp)
                Return result
            End If
            If remoteDest Then
                System.IO.File.Move(temp, localBasePath)
                afterWritten?.Invoke(localBasePath)
                Dim verifyProblem As String = Verify(localBasePath, result.Written, ct)
                If verifyProblem.Length > 0 Then
                    result.ArchivePath = ""
                    Throw New ScheduledTransferException("archive-" & verifyProblem)
                End If
                Dim fileName As String = Path.GetFileName(finalPath.Replace("\"c, "/"c))
                Dim targetRelative As String = fileName
                If destination.Exists(targetRelative) Then
                    Dim free As String = FreeNameRule.Find("", fileName,
                        Function(folder As String, name As String) If(folder.Length = 0, name, folder & "/" & name),
                        Function(candidate As String) destination.Exists(candidate), Nothing)
                    If free Is Nothing Then Throw New IOException("No free archive name on " & destination.Describe())
                    targetRelative = free
                End If
                Dim localFolder As New LocalSide(System.IO.Path.GetDirectoryName(localBasePath))
                Dim info As New FileInfo(localBasePath)
                Dim zipSideEntry As New SideEntry With {.Name = info.Name, .RelativePath = info.Name,
                    .Length = info.Length, .LastWriteUtc = info.LastWriteTimeUtc}
                Dim transfer As New ProviderTransfer()
                transfer.CopyFile(localFolder, zipSideEntry, destination, targetRelative, overwrite:=False, ct)
                Try : System.IO.File.Delete(localBasePath) : Catch : End Try
                Try : Directory.Delete(System.IO.Path.GetDirectoryName(localBasePath), recursive:=True) : Catch : End Try
                result.ArchivePath = destination.Combine(targetRelative)
                Return result
            End If

            ' "Auto-dedupe name" (D4): a name that exists is never overwritten by an archive.
            Dim target As String = finalPath
            If System.IO.File.Exists(target) Then
                Dim free As String = FreeNameRule.Find(System.IO.Path.GetDirectoryName(finalPath), System.IO.Path.GetFileName(finalPath),
                    Function(folder As String, name As String) System.IO.Path.Combine(folder, name),
                    Function(candidate As String) System.IO.File.Exists(candidate), Nothing)
                If free Is Nothing Then Throw New IOException("No free archive name next to " & finalPath)
                target = free
            End If
            System.IO.File.Move(temp, target)
            result.ArchivePath = target
            Return result
        Catch
            Try
                If System.IO.File.Exists(temp) Then System.IO.File.Delete(temp)
                If remoteDest AndAlso System.IO.File.Exists(localBasePath) Then System.IO.File.Delete(localBasePath)
            Catch
            End Try
            Throw
        End Try
    End Function

    Private Shared Sub AddEntry(zip As ZipArchive, source As IScheduledSide, entry As SideEntry, result As ArchiveWriteResult,
                                ct As CancellationToken)
        Dim sourcePath As String = source.Combine(entry.RelativePath)
        Dim name As String = EntryNameOf(entry.RelativePath)
        If name.Length = 0 Then
            result.Failed.Add(New KeyValuePair(Of SideEntry, String)(entry, "invalid"))
            Return
        End If
        Dim providerSide As ProviderSide = TryCast(source, ProviderSide)
        If providerSide IsNot Nothing Then
            AddProviderEntry(zip, providerSide, sourcePath, name, entry, result, ct)
            Return
        End If
        Dim input As FileStream = Nothing
        Try
            input = New FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, FileOptions.SequentialScan)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: archiving " & sourcePath, ex)
            result.Failed.Add(New KeyValuePair(Of SideEntry, String)(entry, ScheduledFailure.CodeOf(ex)))
            Return
        End Try
        Using input
            Dim zipEntry As ZipArchiveEntry = zip.CreateEntry(name, CompressionLevel.Optimal)
            zipEntry.LastWriteTime = ClampZipTime(entry.LastWriteUtc)
            Dim crc As New Crc32Accumulator()
            Dim total As Long = 0
            Dim buffer(81919) As Byte
            Using output As Stream = zipEntry.Open()
                Dim read As Integer = input.Read(buffer, 0, buffer.Length)
                While read > 0
                    ct.ThrowIfCancellationRequested()
                    output.Write(buffer, 0, read)
                    crc.Add(buffer, read)
                    total += read
                    read = input.Read(buffer, 0, buffer.Length)
                End While
            End Using
            ' A file that grew or shrank while it was read is not the file that was listed. A zip writer
            ' cannot take an entry back, so the entry stays - it is exactly the bytes that were read - and
            ' the file is reported as failed and never offered for removal.
            Dim stable As Boolean = total = entry.Length AndAlso LocalTransfer.SameAsListed(sourcePath, entry)
            If Not stable Then
                result.Failed.Add(New KeyValuePair(Of SideEntry, String)(entry, "changed"))
            End If
            result.Written.Add(New ArchiveEntryRecord(entry, name, total, crc.Value, stable))
            result.Bytes += total
        End Using
    End Sub

    Private Shared Sub AddProviderEntry(zip As ZipArchive, side As ProviderSide, sourcePath As String, name As String,
                                        entry As SideEntry, result As ArchiveWriteResult, ct As CancellationToken)
        Dim tempFile As String = Path.Combine(Path.GetTempPath(), "fms-arch-src-" & Guid.NewGuid().ToString("N") & ".tmp")
        Try
            side.Provider.Download(sourcePath, tempFile, Nothing, ct)
            Using input As New FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan)
                Dim zipEntry As ZipArchiveEntry = zip.CreateEntry(name, CompressionLevel.Optimal)
                zipEntry.LastWriteTime = ClampZipTime(entry.LastWriteUtc)
                Dim crc As New Crc32Accumulator()
                Dim total As Long = 0
                Dim buffer(81919) As Byte
                Using output As Stream = zipEntry.Open()
                    Dim read As Integer = input.Read(buffer, 0, buffer.Length)
                    While read > 0
                        ct.ThrowIfCancellationRequested()
                        output.Write(buffer, 0, read)
                        crc.Add(buffer, read)
                        total += read
                        read = input.Read(buffer, 0, buffer.Length)
                    End While
                End Using
                Dim after As StatResult = ResourceProviderCalls.Stat(side.Provider, sourcePath)
                Dim stable As Boolean = total = entry.Length AndAlso after.Outcome = StatOutcome.Found AndAlso
                                        after.Entry.Length = entry.Length AndAlso after.Entry.LastWriteUtc = entry.LastWriteUtc
                If Not stable Then
                    result.Failed.Add(New KeyValuePair(Of SideEntry, String)(entry, "changed"))
                End If
                result.Written.Add(New ArchiveEntryRecord(entry, name, total, crc.Value, stable))
                result.Bytes += total
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: archiving " & sourcePath, ex)
            result.Failed.Add(New KeyValuePair(Of SideEntry, String)(entry, ScheduledFailure.CodeOf(ex)))
        Finally
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>A zip stores local time from 1980 to 2107.</summary>
    Private Shared Function ClampZipTime(utc As DateTime) As DateTimeOffset
        Dim local As DateTime = utc.ToLocalTime()
        If local.Year < 1980 Then local = New DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local)
        If local.Year > 2107 Then local = New DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local)
        Return New DateTimeOffset(local)
    End Function

    ''' <summary>
    ''' The read-back proof: the zip opens, holds exactly the entries that were written, and each one's
    ''' length and CRC-32 - computed from the DECOMPRESSED stream - equal what was recorded from the source.
    ''' Returns "" when the archive is proved, otherwise the first thing that was not.
    ''' </summary>
    Friend Shared Function Verify(archivePath As String, expected As IReadOnlyList(Of ArchiveEntryRecord), ct As CancellationToken) As String
        Try
            Using zip As ZipArchive = ZipFile.OpenRead(archivePath)
                If zip.Entries.Count <> expected.Count Then
                    AppFileLogger.WriteLine("Scheduled operations: " & archivePath & " holds " & zip.Entries.Count & " entries, " & expected.Count & " were written")
                    Return "entry-count"
                End If
                Dim byName As New Dictionary(Of String, ZipArchiveEntry)(StringComparer.Ordinal)
                For Each e As ZipArchiveEntry In zip.Entries
                    byName(e.FullName) = e
                Next
                For Each record As ArchiveEntryRecord In expected
                    ct.ThrowIfCancellationRequested()
                    Dim found As ZipArchiveEntry = Nothing
                    If Not byName.TryGetValue(record.EntryName, found) Then
                        AppFileLogger.WriteLine("Scheduled operations: " & archivePath & " has no entry " & record.EntryName)
                        Return "entry-missing"
                    End If
                    If found.Length <> record.Length Then
                        AppFileLogger.WriteLine("Scheduled operations: " & archivePath & " entry " & record.EntryName & " has another size")
                        Return "entry-size"
                    End If
                    Dim crc As New Crc32Accumulator()
                    Dim buffer(81919) As Byte
                    Dim total As Long = 0
                    Using stream As Stream = found.Open()
                        Dim read As Integer = stream.Read(buffer, 0, buffer.Length)
                        While read > 0
                            ct.ThrowIfCancellationRequested()
                            crc.Add(buffer, read)
                            total += read
                            read = stream.Read(buffer, 0, buffer.Length)
                        End While
                    End Using
                    If total <> record.Length OrElse crc.Value <> record.Crc Then
                        AppFileLogger.WriteLine("Scheduled operations: " & archivePath & " entry " & record.EntryName & " reads back different bytes")
                        Return "entry-content"
                    End If
                Next
            End Using
            Return ""
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading back " & archivePath, ex)
            Return "unreadable"
        End Try
    End Function

End Class

''' <summary>CRC-32 (the zip polynomial), streamed. Written here because the check must not depend on the
''' very library whose output it is checking.</summary>
Friend NotInheritable Class Crc32Accumulator

    Private Shared ReadOnly Table As UInteger() = BuildTable()
    Private state As UInteger = &HFFFFFFFUI

    Private Shared Function BuildTable() As UInteger()
        Dim table(255) As UInteger
        For i As Integer = 0 To 255
            Dim c As UInteger = CUInt(i)
            For k As Integer = 0 To 7
                c = If((c And 1UI) <> 0UI, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
            table(i) = c
        Next
        Return table
    End Function

    Friend Sub Add(buffer As Byte(), count As Integer)
        Dim c As UInteger = state
        For i As Integer = 0 To count - 1
            c = Table(CInt((c Xor buffer(i)) And &HFFUI)) Xor (c >> 8)
        Next
        state = c
    End Sub

    Friend ReadOnly Property Value As UInteger
        Get
            Return state Xor &HFFFFFFFFUI
        End Get
    End Property

End Class
