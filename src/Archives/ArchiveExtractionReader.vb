#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports SharpCompress.Archives
Imports SharpCompress.Common

''' <summary>Plans all headers first, then reads solid containers in one forward pass.</summary>
Friend NotInheritable Class ArchiveExtractionReader
    Friend Shared Function Extract(archivePath As String, ownedStage As String, ct As CancellationToken,
                                   Optional progress As Action(Of Integer, Long) = Nothing) As ArchiveExtractionPlan
        If Not {".zip", ".cbz", ".7z", ".rar", ".cbr"}.Contains(Path.GetExtension(archivePath).ToLowerInvariant()) Then Throw New ArchiveSafetyException(ArchiveFailure.Unsupported)
        Try
            Using file As New FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using archive As IArchive = ArchiveFactory.OpenArchive(file, ArchiveSession.ReaderOptionsForLegacyNames())
                    If archive.Type <> ArchiveType.Zip AndAlso archive.Type <> ArchiveType.SevenZip AndAlso archive.Type <> ArchiveType.Rar Then Throw New ArchiveSafetyException(ArchiveFailure.Unsupported)
                    If archive.IsEncrypted Then Throw New ArchiveSafetyException(ArchiveFailure.Encrypted)
                    If Not archive.IsComplete OrElse archive.Volumes.Count() <> 1 Then Throw New ArchiveSafetyException(ArchiveFailure.MultiVolume)
                    Dim headers As New List(Of ArchiveExtractionHeader)()
                    For Each entry As IArchiveEntry In archive.Entries
                        ct.ThrowIfCancellationRequested()
                        If headers.Count >= ArchiveExtractionPlan.MaxEntries Then Throw New ArchiveSafetyException(ArchiveFailure.EntryLimit)
                        headers.Add(Header(entry))
                    Next
                    Dim sequential As Boolean = archive.IsSolid OrElse archive.Type = ArchiveType.SevenZip
                    Dim plan As ArchiveExtractionPlan = ArchiveExtractionPlan.Create(headers, ownedStage, file.Length, sequential)
                    ArchiveExtractionPlan.RejectReparseAncestors(ownedStage)
                    PreflightSpace(ownedStage, plan.TotalBytes)
                    ' No destination entry has been written before the entire plan was accepted.
                    Dim index As Integer
                    Dim total As Long
                    Dim pending As Dictionary(Of String, ArchiveExtractionItem) = plan.Items.ToDictionary(Function(item) item.Header.Name, StringComparer.Ordinal)
                    Dim writeEntry As Action(Of IEntry, Func(Of Stream)) =
                        Sub(entry, openStream)
                            ct.ThrowIfCancellationRequested()
                            Dim actual As ArchiveExtractionHeader = Header(entry)
                            Dim item As ArchiveExtractionItem = Nothing
                            If Not pending.Remove(actual.Name, item) Then Throw New ArchiveSafetyException(ArchiveFailure.Broken)
                            If actual.Name <> item.Header.Name OrElse actual.IsDirectory <> item.Header.IsDirectory OrElse actual.IsLink OrElse actual.IsEncrypted OrElse actual.IsSplit Then Throw New ArchiveSafetyException(ArchiveFailure.Broken)
                            ArchiveExtractionPlan.RejectReparseAncestors(item.TargetPath)
                            If item.Header.IsDirectory Then
                                Directory.CreateDirectory(item.TargetPath)
                            Else
                                Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath))
                                Using source As Stream = openStream(), target As New FileStream(item.TargetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                                    Dim observed As ArchiveWriteRecord = VerifiedZipWriter.Digest(source, item.Header.Size, ct, target)
                                    item.Digest = observed
                                    If observed.Length <> item.Header.Size Then Throw New ArchiveSafetyException(ArchiveFailure.Broken)
                                    ' RAR streams enforce CRC/BLAKE2 themselves; its Crc property may expose BLAKE2 bytes.
                                    If archive.Type <> ArchiveType.Rar AndAlso CLng(observed.Crc32) <> (item.Header.Crc And &HFFFFFFFFL) Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
                                    total = VerifiedZipWriter.CheckedAdd(total, observed.Length)
                                    If total > ArchiveSession.Default_Max_Cache_Bytes Then Throw New ArchiveSafetyException(ArchiveFailure.TotalSizeLimit)
                                End Using
                            End If
                            index += 1
                            progress?.Invoke(index, total)
                        End Sub
                    If sequential Then
                        Using reader As SharpCompress.Readers.IReader = archive.ExtractAllEntries()
                            While reader.MoveToNextEntry()
                                writeEntry(reader.Entry, Function() reader.OpenEntryStream())
                            End While
                        End Using
                    Else
                        For Each entry As IArchiveEntry In archive.Entries
                            writeEntry(entry, Function() entry.OpenEntryStream())
                        Next
                    End If
                    If pending.Count <> 0 OrElse index <> plan.Items.Count OrElse total <> plan.TotalBytes Then Throw New ArchiveSafetyException(ArchiveFailure.Broken)
                    Return plan
                End Using
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As ArchiveSafetyException
            Throw
        Catch ex As SharpCompress.Common.CryptographicException
            Throw New ArchiveSafetyException(ArchiveFailure.Encrypted)
        Catch ex As Exception When TypeOf ex Is InvalidDataException OrElse TypeOf ex Is SharpCompress.Common.SharpCompressException OrElse TypeOf ex Is EndOfStreamException
            Throw New ArchiveSafetyException(ArchiveFailure.Broken)
        End Try
    End Function

    Private Shared Function Header(entry As IEntry) As ArchiveExtractionHeader
        Dim attributes As Integer = entry.Attrib.GetValueOrDefault()
        Dim unixType As Integer = (attributes >> 16) And &HF000
        Dim rar As SharpCompress.Common.Rar.RarEntry = TryCast(entry, SharpCompress.Common.Rar.RarEntry)
        Return New ArchiveExtractionHeader With {.Name = entry.Key, .IsDirectory = entry.IsDirectory,
            .IsLink = Not String.IsNullOrEmpty(entry.LinkTarget) OrElse (attributes And CInt(FileAttributes.ReparsePoint)) <> 0 OrElse
                      (attributes And &HF000) = &HA000 OrElse (rar IsNot Nothing AndAlso rar.IsRedir) OrElse
                      (unixType <> 0 AndAlso unixType <> &H8000 AndAlso unixType <> &H4000),
            .IsEncrypted = entry.IsEncrypted, .IsSplit = entry.IsSplitAfter OrElse entry.VolumeIndexFirst <> entry.VolumeIndexLast,
            .Size = entry.Size, .CompressedSize = entry.CompressedSize, .Crc = If(rar Is Nothing, entry.Crc, -1)}
    End Function

    Friend Shared Sub PreflightSpace(stage As String, bytes As Long)
        If bytes < 0 Then Return
        Dim drive As New DriveInfo(Path.GetPathRoot(Path.GetFullPath(stage)))
        If drive.IsReady AndAlso drive.AvailableFreeSpace < bytes Then Throw New ArchiveSafetyException(ArchiveFailure.Space)
    End Sub
End Class
#End If
