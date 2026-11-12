#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Specification 277 section 7.4 (decision D06): the recovery draft of a dirty editor. One file per source file,
' the whole of it protected by DPAPI for the current user, so no plaintext of the edit ever reaches the disk.
' The body is a versioned binary record, not JSON: it is read by this one module and holds a text of many
' megabytes. A draft is removed only by a confirmed save or by the user. No WinForms reference.

''' <summary>What the user had typed: the text as a save would write it, line terminators included, and the file it
''' was typed against. A newer file on disk is therefore a conflict at the next save, never overwritten.</summary>
Public NotInheritable Class TextDraft
    Public Property SourcePath As String
    Public Property SavedAtUtc As DateTime
    Public Property CodePage As Integer
    Public Property WithBom As Boolean
    ''' <summary>"" keeps each line's own terminator; otherwise the choice of Normalize Line Endings.</summary>
    Public Property NormalizedTerminator As String = ""
    Public Property Snapshot As TextFileSnapshot
    Public Property Text As String
End Class

Public Module TextDraftManager

    Public Const DraftExtension As String = ".fms-draft"

    Private Const Magic As Integer = &H44534D46
    Private Const FormatVersion As Integer = 1

    Public Function DefaultFolder() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SZA", "FastMediaSorter", "text-drafts")
    End Function

    ''' <summary>A draft exists only for a file on a local volume, never for one inside a secure container or an archive,
    ''' on a remote share or in a cache of those (section 7.4 item 3), and only while the user keeps the switch on.</summary>
    Public Function Allowed(autosaveOn As Boolean, onLocalVolume As Boolean, insideArchive As Boolean, insideRemoteCache As Boolean, insideSecure As Boolean) As Boolean
        Return autosaveOn AndAlso onLocalVolume AndAlso Not insideArchive AndAlso Not insideRemoteCache AndAlso Not insideSecure
    End Function

    ''' <summary>The draft's name is a hash of the file's path: the folder listing gives no name away.</summary>
    Public Function PathFor(folder As String, sourcePath As String) As String
        Dim identity As Byte() = Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath).ToUpperInvariant())
        Return Path.Combine(folder, "draft_" & TextFileSaver.HashOf(identity) & DraftExtension)
    End Function

    Public Function Exists(folder As String, sourcePath As String) As Boolean
        Return File.Exists(PathFor(folder, sourcePath))
    End Function

    ''' <summary>Writes the draft whole or not at all: a crash in the middle leaves the previous one.</summary>
    Public Sub Write(folder As String, draft As TextDraft)
        Directory.CreateDirectory(folder)
        Dim protectedBytes As Byte() = ProtectedData.Protect(Serialize(draft), Nothing, DataProtectionScope.CurrentUser)
        Dim target As String = PathFor(folder, draft.SourcePath)
        Dim temporary As String = target & "." & Guid.NewGuid().ToString("N") & TextFileSaver.TemporarySuffix
        Try
            Using stream As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                stream.Write(protectedBytes, 0, protectedBytes.Length)
                stream.Flush(True)
            End Using
            If File.Exists(target) Then
                File.Replace(temporary, target, Nothing, True)
            Else
                File.Move(temporary, target)
            End If
        Finally
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Sub

    ''' <summary>The draft of a file, or Nothing when there is none, it belongs to another user, it is damaged or a later
    ''' version wrote it. A draft that cannot be read is kept: it may still be readable by the version that wrote it.</summary>
    Public Function Read(folder As String, sourcePath As String) As TextDraft
        Dim target As String = PathFor(folder, sourcePath)
        If Not File.Exists(target) Then Return Nothing
        Try
            Dim draft As TextDraft = Deserialize(ProtectedData.Unprotect(File.ReadAllBytes(target), Nothing, DataProtectionScope.CurrentUser))
            If draft Is Nothing Then Return Nothing
            Return If(String.Equals(Path.GetFullPath(draft.SourcePath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase), draft, Nothing)
        Catch ex As CryptographicException
            Return Nothing
        Catch ex As IOException
            Return Nothing
        Catch ex As UnauthorizedAccessException
            Return Nothing
        Catch ex As FormatException
            Return Nothing
        End Try
    End Function

    ''' <summary>False when the draft could not be removed; the file itself is never involved.</summary>
    Public Function TryDelete(folder As String, sourcePath As String) As Boolean
        Try
            Dim target As String = PathFor(folder, sourcePath)
            If File.Exists(target) Then File.Delete(target)
            Return True
        Catch ex As IOException
            Return False
        Catch ex As UnauthorizedAccessException
            Return False
        End Try
    End Function

    ''' <summary>True when saving the draft now would write exactly what the file already holds: nothing to recover.</summary>
    Public Function MatchesFile(draft As TextDraft, filePath As String) As Boolean
        If Not File.Exists(filePath) Then Return False
        Dim characters As System.Collections.Generic.IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Dim bytes As Byte() = TextFileSaver.Encode(draft.Text, TextEncodingDetector.CreateEncoding(draft.CodePage, False), draft.WithBom, False, characters, count)
        If bytes Is Nothing Then Return False
        Return String.Equals(TextFileSaver.HashOf(bytes), TextFileSaver.HashOfFile(filePath), StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function Serialize(draft As TextDraft) As Byte()
        Using memory As New MemoryStream(),
              writer As New BinaryWriter(memory, New UTF8Encoding(False))
            writer.Write(Magic)
            writer.Write(FormatVersion)
            writer.Write(draft.SourcePath)
            writer.Write(draft.SavedAtUtc.Ticks)
            writer.Write(draft.CodePage)
            writer.Write(draft.WithBom)
            writer.Write(If(draft.NormalizedTerminator, ""))
            writer.Write(draft.Snapshot.Length)
            writer.Write(draft.Snapshot.LastWriteTimeUtc.Ticks)
            writer.Write(draft.Snapshot.Sha256)
            writer.Write(If(draft.Text, ""))
            writer.Flush()
            Return memory.ToArray()
        End Using
    End Function

    Private Function Deserialize(bytes As Byte()) As TextDraft
        Using memory As New MemoryStream(bytes),
              reader As New BinaryReader(memory, New UTF8Encoding(False))
            Try
                If reader.ReadInt32() <> Magic OrElse reader.ReadInt32() <> FormatVersion Then Return Nothing
                Dim draft As New TextDraft With {
                    .SourcePath = reader.ReadString(),
                    .SavedAtUtc = New DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                    .CodePage = reader.ReadInt32(),
                    .WithBom = reader.ReadBoolean(),
                    .NormalizedTerminator = reader.ReadString()
                }
                Dim length As Long = reader.ReadInt64()
                Dim writeTime As New DateTime(reader.ReadInt64(), DateTimeKind.Utc)
                draft.Snapshot = New TextFileSnapshot With {
                    .FilePath = draft.SourcePath,
                    .Length = length,
                    .LastWriteTimeUtc = writeTime,
                    .Sha256 = reader.ReadString()
                }
                draft.Text = reader.ReadString()
                Return draft
            Catch ex As EndOfStreamException
                Return Nothing
            Catch ex As ArgumentOutOfRangeException
                Return Nothing
            End Try
        End Using
    End Function
End Module
#End If
