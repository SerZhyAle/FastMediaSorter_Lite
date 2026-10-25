Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Threading

' The archive action of a scheduled operation (ticket 222, D4): one zip per run, entries carrying the
' relative path, the originals kept - unless "delete originals after verification" is set, in which
' case the archive is READ BACK and every entry checked (name, size, SHA-256) against what was written
' before a single original is touched.
'
' Compression and digest readback belong to the linked neutral core. This adapter keeps
' scheduled naming, provider transfer, failure reporting and source cleanup in this domain.
Friend NotInheritable Class ArchiveEntryRecord
    Friend ReadOnly Entry As SideEntry
    Friend ReadOnly EntryName As String
    Friend ReadOnly Length As Long
    Friend Property Sha256 As Byte()
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
        Return VerifiedZipWriter.SafeName(relativePath.Replace("\"c, "/"c), False)
    End Function

    ''' <summary>
    ''' Writes a verified local artifact before publishing it. An unreadable or changed input
    ''' fails the artifact as a whole; no scheduled cleanup is authorized by that failure.
    ''' When <paramref name="destination"/> is a non-local side (such as Google Drive), the zip is built in a
    ''' local staging file, verified, and uploaded to the destination side.
    ''' </summary>
    Friend Shared Function Write(source As IScheduledSide, entries As IReadOnlyList(Of SideEntry), finalPath As String,
                                 ct As CancellationToken, Optional destination As IScheduledSide = Nothing,
                                 Optional afterWritten As Action(Of String) = Nothing) As ArchiveWriteResult
        Dim result As New ArchiveWriteResult()
        If entries.Count = 0 Then Return result
        Dim remoteDest As Boolean = destination IsNot Nothing AndAlso Not destination.IsLocalPath
        Dim baseFolder As String = If(remoteDest, Path.GetTempPath(), Path.GetDirectoryName(finalPath))
        Directory.CreateDirectory(baseFolder)
        Dim owned As String = Path.Combine(baseFolder, "fms-archive-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(owned)
        Dim stage As String = Path.Combine(owned, "archive.part")
        Try
            Dim inputs As New List(Of ArchiveWriteInput)()
            For Each entry As SideEntry In entries
                Dim captured As SideEntry = entry
                Dim sourcePath As String = source.Combine(entry.RelativePath)
                inputs.Add(New ArchiveWriteInput With {.Name = EntryNameOf(entry.RelativePath), .Length = entry.Length,
                    .ModifiedUtc = entry.LastWriteUtc,
                    .ValidateVersion = Sub() ValidateSource(source, captured),
                    .OpenRead = Function(token) OpenSource(source, sourcePath, owned, token)})
            Next
            Dim artifact As VerifiedZipArtifact = VerifiedZipWriter.Write(stage, inputs, ct)
            For i As Integer = 0 To entries.Count - 1
                Dim record As ArchiveWriteRecord = artifact.Records(i)
                result.Written.Add(New ArchiveEntryRecord(entries(i), record.Name, record.Length, record.Crc32, True) With {.Sha256 = record.Sha256})
            Next
            result.Bytes = artifact.Bytes
            If remoteDest Then
                afterWritten?.Invoke(stage)
                VerifiedZipWriter.Verify(stage, artifact.Records, ct)
                Dim leaf As String = Path.GetFileName(finalPath.Replace("/"c, "\"c))
                Dim relative As String = leaf
                If destination.Exists(relative) Then
                    relative = FreeNameRule.Find("", leaf, Function(folder, name) name, Function(candidate) destination.Exists(candidate), Nothing)
                    If relative Is Nothing Then Throw New IOException("No free archive name.")
                End If
                Dim local As New LocalSide(owned)
                Dim info As New FileInfo(stage)
                Dim item As New SideEntry With {.Name = info.Name, .RelativePath = info.Name, .Length = info.Length, .LastWriteUtc = info.LastWriteTimeUtc}
                Dim transfer As New ProviderTransfer()
                transfer.CopyFile(local, item, destination, relative, False, ct)
                result.ArchivePath = destination.Combine(relative)
            Else
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath))
                Dim target As String = finalPath
                If File.Exists(target) OrElse Directory.Exists(target) Then
                    target = FreeNameRule.Find(Path.GetDirectoryName(finalPath), Path.GetFileName(finalPath),
                        Function(folder, name) Path.Combine(folder, name), Function(candidate) File.Exists(candidate) OrElse Directory.Exists(candidate), Nothing)
                    If target Is Nothing Then Throw New IOException("No free archive name.")
                End If
                ' The common writer has already closed and read back every byte.
                ct.ThrowIfCancellationRequested()
                File.Move(stage, target)
                VerifiedZipWriter.Verify(target, artifact.Records, ct)
                result.ArchivePath = target
            End If
            Return result
        Finally
            Try : Directory.Delete(owned, True) : Catch : End Try
        End Try
    End Function

    Private Shared Sub ValidateSource(source As IScheduledSide, entry As SideEntry)
        Dim path As String = source.Combine(entry.RelativePath)
        Dim provider As ProviderSide = TryCast(source, ProviderSide)
        Dim stable As Boolean
        If provider Is Nothing Then
            stable = LocalTransfer.SameAsListed(path, entry)
        Else
            Dim current As StatResult = ResourceProviderCalls.Stat(provider.Provider, path)
            stable = current.Outcome = StatOutcome.Found AndAlso Not current.Entry.IsLink AndAlso
                Not current.Entry.IsDirectory AndAlso current.Entry.Length = entry.Length AndAlso current.Entry.LastWriteUtc = entry.LastWriteUtc
        End If
        If Not stable Then Throw New SourceChangedException(path)
    End Sub

    Private Shared Function OpenSource(source As IScheduledSide, path As String, owned As String, ct As CancellationToken) As Stream
        Dim provider As ProviderSide = TryCast(source, ProviderSide)
        If provider Is Nothing Then Return New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, FileOptions.SequentialScan)
        Dim stage As String = System.IO.Path.Combine(owned, Guid.NewGuid().ToString("N") & ".input")
        provider.Provider.Download(path, stage, Nothing, ct)
        Return New FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan Or FileOptions.DeleteOnClose)
    End Function

    Friend Shared Function Verify(archivePath As String, expected As IReadOnlyList(Of ArchiveEntryRecord), ct As CancellationToken) As String
        Try
            Dim records As New List(Of ArchiveWriteRecord)()
            For Each record As ArchiveEntryRecord In expected
                records.Add(New ArchiveWriteRecord With {.Name = record.EntryName, .Length = record.Length, .Sha256 = record.Sha256})
            Next
            VerifiedZipWriter.Verify(archivePath, records, ct)
            Return ""
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: archive readback", ex)
            Return "unreadable"
        End Try
    End Function
End Class
