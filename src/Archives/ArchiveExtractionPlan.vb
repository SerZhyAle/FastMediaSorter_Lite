#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

Friend NotInheritable Class ArchiveExtractionHeader
    Friend Property Name As String
    Friend Property IsDirectory As Boolean
    Friend Property IsLink As Boolean
    Friend Property IsEncrypted As Boolean
    Friend Property IsSplit As Boolean
    Friend Property Size As Long
    Friend Property CompressedSize As Long
    Friend Property Crc As Long
End Class

Friend NotInheritable Class ArchiveExtractionItem
    Friend Property Header As ArchiveExtractionHeader
    Friend Property RelativePath As String
    Friend Property TargetPath As String
    Friend Property Digest As ArchiveWriteRecord
End Class

''' <summary>Header-only planning: no filesystem writes and no media eligibility filtering.</summary>
Friend NotInheritable Class ArchiveExtractionPlan
    Friend Const MaxEntries As Integer = 10000
    Friend ReadOnly Property Items As New List(Of ArchiveExtractionItem)()
    Friend Property TotalBytes As Long

    Friend Shared Function Create(headers As IEnumerable(Of ArchiveExtractionHeader), targetRoot As String,
                                  compressedArchiveBytes As Long, Optional solid As Boolean = False,
                                  Optional maxEntryBytes As Long = ArchiveSession.Default_Max_Entry_Bytes,
                                  Optional maxTotalBytes As Long = ArchiveSession.Default_Max_Cache_Bytes) As ArchiveExtractionPlan
        Dim result As New ArchiveExtractionPlan()
        Dim names As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Dim root As String = Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        For Each header In headers
            If result.Items.Count >= MaxEntries Then Throw New ArchiveSafetyException(ArchiveFailure.EntryLimit)
            If header.IsEncrypted Then Throw New ArchiveSafetyException(ArchiveFailure.Encrypted)
            If header.IsSplit Then Throw New ArchiveSafetyException(ArchiveFailure.MultiVolume)
            If header.IsLink Then Throw New ArchiveSafetyException(ArchiveFailure.Link)
            Dim relative As String = VerifiedZipWriter.SafeName(header.Name, header.IsDirectory).TrimEnd("/"c)
            If Not names.TryAdd(relative, header.IsDirectory) Then Throw New ArchiveSafetyException(ArchiveFailure.DuplicateName)
            If header.Size < 0 OrElse header.CompressedSize < 0 OrElse (header.IsDirectory AndAlso header.Size <> 0) Then Throw New ArchiveSafetyException(ArchiveFailure.Broken)
            If header.Size > maxEntryBytes Then Throw New ArchiveSafetyException(ArchiveFailure.EntrySizeLimit)
            result.TotalBytes = VerifiedZipWriter.CheckedAdd(result.TotalBytes, header.Size)
            If result.TotalBytes > maxTotalBytes Then Throw New ArchiveSafetyException(ArchiveFailure.TotalSizeLimit)
            Dim compressed As Long = If(solid, compressedArchiveBytes, header.CompressedSize)
            If header.Size > 0 AndAlso (compressed <= 0 OrElse CDbl(header.Size) / compressed > ArchiveSession.Max_Expansion_Ratio) Then Throw New ArchiveSafetyException(ArchiveFailure.ExpansionLimit)
            Dim target As String = Path.GetFullPath(Path.Combine(root, relative.Replace("/"c, Path.DirectorySeparatorChar)))
            If Not target.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
            result.Items.Add(New ArchiveExtractionItem With {.Header = header, .RelativePath = relative, .TargetPath = target})
        Next
        If result.TotalBytes > 0 AndAlso (compressedArchiveBytes <= 0 OrElse CDbl(result.TotalBytes) / compressedArchiveBytes > ArchiveSession.Max_Expansion_Ratio) Then Throw New ArchiveSafetyException(ArchiveFailure.ExpansionLimit)
        For Each name In names.Keys
            Dim parent As String = name
            While parent.Contains("/"c)
                parent = parent.Substring(0, parent.LastIndexOf("/"c))
                If names.ContainsKey(parent) AndAlso Not names(parent) Then Throw New ArchiveSafetyException(ArchiveFailure.DuplicateName)
            End While
        Next
        Return result
    End Function

    Friend Shared Sub RejectReparseAncestors(path As String)
        Dim cursor As String = System.IO.Path.GetFullPath(path)
        While cursor IsNot Nothing
            Try
                If (File.GetAttributes(cursor) And FileAttributes.ReparsePoint) <> 0 Then Throw New ArchiveSafetyException(ArchiveFailure.Link)
            Catch ex As FileNotFoundException
            Catch ex As DirectoryNotFoundException
            End Try
            cursor = System.IO.Path.GetDirectoryName(cursor)
        End While
    End Sub
End Class
#End If
