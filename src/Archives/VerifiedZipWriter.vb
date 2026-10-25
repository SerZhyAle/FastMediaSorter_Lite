#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

''' <summary>The single compression and readback implementation used by both applications.</summary>
Friend NotInheritable Class VerifiedZipWriter
    Friend Shared Function Write(stage As String, inputs As IReadOnlyList(Of ArchiveWriteInput), ct As CancellationToken,
                                 Optional progress As Action(Of Integer, Long) = Nothing) As VerifiedZipArtifact
        Dim names As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        For Each entry As ArchiveWriteInput In inputs
            Dim canonical As String = SafeName(entry.Name, entry.IsDirectory)
            If canonical <> entry.Name OrElse entry.Length < -1 Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
            If Not names.TryAdd(canonical.TrimEnd("/"c), entry.IsDirectory) Then Throw New ArchiveSafetyException(ArchiveFailure.DuplicateName)
        Next
        For Each name As String In names.Keys
            Dim parent As String = name
            While parent.Contains("/"c)
                parent = parent.Substring(0, parent.LastIndexOf("/"c))
                If names.ContainsKey(parent) AndAlso Not names(parent) Then Throw New ArchiveSafetyException(ArchiveFailure.DuplicateName)
            End While
        Next
        Dim records As New List(Of ArchiveWriteRecord)()
        Dim bytes As Long
        Dim owned As Boolean
        Try
            ct.ThrowIfCancellationRequested()
            Using file As New FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                owned = True
                Using zip As New ZipArchive(file, ZipArchiveMode.Create, False, Encoding.UTF8)
                    For Each input As ArchiveWriteInput In inputs
                        ct.ThrowIfCancellationRequested()
                        input.ValidateVersion?.Invoke()
                        Dim entry As ZipArchiveEntry = zip.CreateEntry(input.Name, CompressionLevel.Optimal)
                        Dim stamp As DateTime = input.ModifiedUtc.ToLocalTime()
                        If stamp.Year < 1980 Then stamp = New DateTime(1980, 1, 1)
                        If stamp.Year > 2107 Then stamp = New DateTime(2107, 12, 31)
                        entry.LastWriteTime = New DateTimeOffset(stamp)
                        Dim record As ArchiveWriteRecord
                        Using target As Stream = entry.Open()
                            If input.IsDirectory Then
                                Using empty As New MemoryStream()
                                    record = Digest(empty, 0, ct)
                                End Using
                            Else
                                Using source As Stream = input.OpenRead(ct)
                                    record = Digest(source, If(input.Length < 0, Long.MaxValue, input.Length), ct, target)
                                End Using
                            End If
                        End Using
                        If (input.IsDirectory AndAlso record.Length <> 0) OrElse
                           (Not input.IsDirectory AndAlso input.Length >= 0 AndAlso record.Length <> input.Length) Then Throw New ArchiveSafetyException(ArchiveFailure.ChangedInput)
                        input.ValidateVersion?.Invoke()
                        record.Name = input.Name
                        record.IsDirectory = input.IsDirectory
                        records.Add(record)
                        bytes = CheckedAdd(bytes, record.Length)
                        progress?.Invoke(records.Count, bytes)
                    Next
                End Using
            End Using
            Verify(stage, records, ct)
            Return New VerifiedZipArtifact With {.Path = stage, .Records = records.AsReadOnly(), .Bytes = bytes}
        Catch
            ' Only the operation-owned stage is eligible for removal.
            If owned AndAlso File.Exists(stage) Then File.Delete(stage)
            Throw
        End Try
    End Function

    Friend Shared Sub Verify(path As String, expected As IReadOnlyList(Of ArchiveWriteRecord), ct As CancellationToken)
        Using zip As ZipArchive = ZipFile.OpenRead(path)
            If zip.Entries.Count <> expected.Count Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
            Dim remaining As New Dictionary(Of String, ArchiveWriteRecord)(StringComparer.Ordinal)
            For Each record As ArchiveWriteRecord In expected
                If Not remaining.TryAdd(record.Name, record) Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
            Next
            For Each entry As ZipArchiveEntry In zip.Entries
                ct.ThrowIfCancellationRequested()
                Dim record As ArchiveWriteRecord = Nothing
                If Not remaining.Remove(entry.FullName, record) OrElse entry.Length <> record.Length OrElse
                   record.IsDirectory <> entry.FullName.EndsWith("/", StringComparison.Ordinal) Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
                Using source As Stream = entry.Open()
                    Dim actual As ArchiveWriteRecord = Digest(source, record.Length, ct)
                    If actual.Length <> record.Length OrElse Not actual.Sha256.SequenceEqual(record.Sha256) Then Throw New ArchiveSafetyException(ArchiveFailure.Verification)
                End Using
            Next
        End Using
    End Sub

    Friend Shared Function Digest(source As Stream, maximum As Long, ct As CancellationToken, Optional target As Stream = Nothing) As ArchiveWriteRecord
        Dim result As New ArchiveWriteRecord()
        Dim crc As UInteger = &HFFFFFFFFUI
        Using hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Dim buffer(81919) As Byte
            Do
                ct.ThrowIfCancellationRequested()
                Dim count As Integer = source.Read(buffer, 0, buffer.Length)
                If count = 0 Then Exit Do
                result.Length = CheckedAdd(result.Length, count)
                If result.Length > maximum Then Throw New ArchiveSafetyException(ArchiveFailure.ChangedInput)
                hash.AppendData(buffer, 0, count)
                For i As Integer = 0 To count - 1
                    crc = CrcTable(CInt((crc Xor buffer(i)) And &HFFUI)) Xor (crc >> 8)
                Next
                target?.Write(buffer, 0, count)
            Loop
            result.Sha256 = hash.GetHashAndReset()
        End Using
        result.Crc32 = crc Xor &HFFFFFFFFUI
        Return result
    End Function

    Friend Shared Function CheckedAdd(left As Long, right As Long) As Long
        If left < 0 OrElse right < 0 OrElse left > Long.MaxValue - right Then Throw New ArchiveSafetyException(ArchiveFailure.TotalSizeLimit)
        Return left + right
    End Function

    ' Portable Windows-safe names also protect ZIP consumers on case-insensitive destinations.
    Friend Shared Function SafeName(name As String, directory As Boolean) As String
        If String.IsNullOrEmpty(name) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
        Dim normalized As String = name.Replace("\"c, "/"c)
        If directory AndAlso normalized.EndsWith("/", StringComparison.Ordinal) Then normalized = normalized.Substring(0, normalized.Length - 1)
        For Each part As String In normalized.Split("/"c)
            If part.Length = 0 OrElse part = "." OrElse part = ".." OrElse part.EndsWith(".", StringComparison.Ordinal) OrElse part.EndsWith(" ", StringComparison.Ordinal) OrElse
               part.Any(Function(c) c < " "c OrElse "<>:""|?*".Contains(c)) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
            Dim stem As String = part.Split("."c)(0).TrimEnd(" "c).ToUpperInvariant()
            If {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"}.Contains(stem) OrElse
               (stem.Length = 4 AndAlso (stem.StartsWith("COM", StringComparison.Ordinal) OrElse stem.StartsWith("LPT", StringComparison.Ordinal)) AndAlso "123456789¹²³".Contains(stem(3))) Then Throw New ArchiveSafetyException(ArchiveFailure.UnsafeName)
        Next
        Return normalized & If(directory, "/", "")
    End Function

    Private Shared ReadOnly CrcTable As UInteger() = BuildCrcTable()
    Private Shared Function BuildCrcTable() As UInteger()
        Dim result(255) As UInteger
        For i As Integer = 0 To 255
            Dim value As UInteger = CUInt(i)
            For bit As Integer = 0 To 7
                value = If((value And 1UI) = 0, value >> 1, &HEDB88320UI Xor (value >> 1))
            Next
            result(i) = value
        Next
        Return result
    End Function
End Class
#End If
