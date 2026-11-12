#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

' Specification 277 section 7.3 (decision D05): a save either replaces the file whole or leaves it exactly
' as it was. The text is encoded strictly first, the file is compared with what was opened, the bytes go to a
' sibling temporary file flushed to the disk, and the swap is File.Replace. No WinForms reference: everything
' here runs on the pool and Main_Form.TextEditor.vb says the outcome.

''' <summary>What the file looked like when it was opened or last saved: the reference of the conflict check.</summary>
Public NotInheritable Class TextFileSnapshot
    Public Property FilePath As String
    Public Property Length As Long
    Public Property LastWriteTimeUtc As DateTime
    Public Property Sha256 As String
End Class

Public Enum TextSaveStatus
    Saved
    ''' <summary>The file on disk is not the one that was opened; nothing was written.</summary>
    Conflict
    ''' <summary>The encoding cannot store a character of the text; nothing was written.</summary>
    Unencodable
    ''' <summary>The disk refused: the file is locked, read-only, or the disk is full. The file is intact.</summary>
    Failed
End Enum

Public NotInheritable Class TextSaveRequest
    Public Property TargetPath As String
    Public Property Text As String
    Public Property Encoding As Encoding
    Public Property WithBom As Boolean
    ''' <summary>What the file was when opened; Nothing for a save to a new name, or to take over the other version.</summary>
    Public Property Snapshot As TextFileSnapshot
    ''' <summary>Old versions kept beside the file; 0 keeps none.</summary>
    Public Property BackupVersions As Integer
    ''' <summary>The explicit "replace them with ?" answer to a refusal; never set without the user's word.</summary>
    Public Property ReplaceUnencodable As Boolean
End Class

Public NotInheritable Class TextSaveOutcome
    Public Property Status As TextSaveStatus
    ''' <summary>A conflict because the file is gone, not changed.</summary>
    Public Property TargetMissing As Boolean
    ''' <summary>The characters the encoding cannot store, each once, in the order met (at most <see cref="TextFileSaver.MaxListedCharacters"/>).</summary>
    Public Property UnencodableCharacters As IReadOnlyList(Of String)
    ''' <summary>How many characters of the text the encoding cannot store.</summary>
    Public Property UnencodableCount As Integer
    Public Property BackupPath As String
    ''' <summary>The reference of the next save; set when the status is <see cref="TextSaveStatus.Saved"/>.</summary>
    Public Property NewSnapshot As TextFileSnapshot
    Public Property BytesWritten As Long
    Public Property Failure As Exception
End Class

Public Module TextFileSaver

    Public Const MaxListedCharacters As Integer = 10
    Public Const TemporarySuffix As String = ".fms-tmp"

    Private Const ErrorInvalidFunction As Integer = &H80070001
    Private Const ErrorNotSupported As Integer = &H80070032

    ''' <summary>Hash of a whole file read without blocking others; also what a conflict is judged by.</summary>
    Public Function HashOf(bytes As Byte()) As String
        Using hasher As SHA256 = SHA256.Create()
            Return BitConverter.ToString(hasher.ComputeHash(bytes)).Replace("-", "")
        End Using
    End Function

    Public Function HashOfFile(filePath As String) As String
        Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Using hasher As SHA256 = SHA256.Create()
                Return BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "")
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Reads a whole file for editing and takes its snapshot in one go. The write time is read before the bytes,
    ''' so a change during the read is seen as a change at the next save, never hidden.
    ''' </summary>
    Public Function ReadForEditing(filePath As String, ByRef snapshot As TextFileSnapshot) As Byte()
        Dim info As New FileInfo(filePath)
        Dim writeTime As DateTime = info.LastWriteTimeUtc
        Dim bytes As Byte()
        Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            bytes = New Byte(CInt(stream.Length) - 1) {}
            Dim filled As Integer = 0
            While filled < bytes.Length
                Dim count As Integer = stream.Read(bytes, filled, bytes.Length - filled)
                If count <= 0 Then Exit While
                filled += count
            End While
            If filled < bytes.Length Then Array.Resize(bytes, filled)
        End Using
        snapshot = New TextFileSnapshot With {
            .FilePath = filePath,
            .Length = bytes.Length,
            .LastWriteTimeUtc = writeTime,
            .Sha256 = HashOf(bytes)
        }
        Return bytes
    End Function

    ''' <summary>
    ''' True when the file on disk is still the one the snapshot describes. The cheap facts decide when they
    ''' agree; when either differs the content does (a touched file with the same bytes is no conflict).
    ''' </summary>
    Public Function MatchesSnapshot(snapshot As TextFileSnapshot, filePath As String, ByRef missing As Boolean) As Boolean
        missing = False
        Dim info As New FileInfo(filePath)
        If Not info.Exists Then
            missing = True
            Return False
        End If
        If info.Length = snapshot.Length AndAlso info.LastWriteTimeUtc = snapshot.LastWriteTimeUtc Then Return True
        If info.Length <> snapshot.Length Then Return False
        Return String.Equals(HashOfFile(filePath), snapshot.Sha256, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' The bytes of the text in an encoding, with its byte-order mark when asked; Nothing when a character cannot
    ''' be stored, and then <paramref name="characters"/> and <paramref name="count"/> say which. Nothing is
    ''' replaced unless <paramref name="replace"/> carries the user's explicit word, and then it is "?".
    ''' </summary>
    Public Function Encode(text As String, encoding As Encoding, withBom As Boolean, replace As Boolean,
                           ByRef characters As IReadOnlyList(Of String), ByRef count As Integer) As Byte()
        characters = Array.Empty(Of String)()
        count = 0
        Dim value As String = If(text, "")
        Dim codePage As Integer = encoding.CodePage
        Dim watcher As New UnencodableWatcher()
        Dim strict As Encoding = System.Text.Encoding.GetEncoding(codePage, watcher, DecoderFallback.ReplacementFallback)
        ' Sizing the array runs the fallback too, so only the writing pass is counted.
        Dim body(strict.GetByteCount(value) - 1) As Byte
        watcher.Restart()
        strict.GetBytes(value, 0, value.Length, body, 0)
        If watcher.Count > 0 Then
            characters = watcher.Listed
            count = watcher.Count
            If Not replace Then Return Nothing
            Dim lossy As Encoding = System.Text.Encoding.GetEncoding(codePage, New EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback)
            body = lossy.GetBytes(value)
        End If
        Dim mark As Byte() = If(withBom, TextEncodingDetector.ByteOrderMarkOf(codePage, encoding), Array.Empty(Of Byte)())
        If mark.Length = 0 Then Return body
        Dim bytes(mark.Length + body.Length - 1) As Byte
        Buffer.BlockCopy(mark, 0, bytes, 0, mark.Length)
        Buffer.BlockCopy(body, 0, bytes, mark.Length, body.Length)
        Return bytes
    End Function

    ''' <summary>
    ''' True when the text, encoded again, gives back the very bytes it was read from. False means a save would
    ''' change more than the user typed: a byte the encoding could not read came through as U+FFFD, or the
    ''' encoding has two spellings of one character. Such a file is shown but not offered for editing.
    ''' </summary>
    Public Function RoundTrips(original As Byte(), text As String, encoding As Encoding, withBom As Boolean) As Boolean
        Dim characters As IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Dim bytes As Byte() = Encode(text, encoding, withBom, False, characters, count)
        If bytes Is Nothing OrElse original Is Nothing OrElse bytes.Length <> original.Length Then Return False
        For index As Integer = 0 To bytes.Length - 1
            If bytes(index) <> original(index) Then Return False
        Next
        Return True
    End Function

    ''' <summary>The whole save. Never throws for a disk failure: <see cref="TextSaveStatus.Failed"/> carries it, and the
    ''' target is intact in every outcome but <see cref="TextSaveStatus.Saved"/>.</summary>
    Public Function Save(request As TextSaveRequest) As TextSaveOutcome
        Dim outcome As New TextSaveOutcome()
        Dim characters As IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Dim bytes As Byte() = Encode(request.Text, request.Encoding, request.WithBom, request.ReplaceUnencodable, characters, count)
        outcome.UnencodableCharacters = characters
        outcome.UnencodableCount = count
        If bytes Is Nothing Then
            outcome.Status = TextSaveStatus.Unencodable
            Return outcome
        End If

        Dim target As String = request.TargetPath
        Dim temporary As String = target & "." & Guid.NewGuid().ToString("N") & TemporarySuffix
        Try
            Dim missing As Boolean
            If request.Snapshot IsNot Nothing AndAlso Not MatchesSnapshot(request.Snapshot, target, missing) Then
                outcome.Status = TextSaveStatus.Conflict
                outcome.TargetMissing = missing
                Return outcome
            End If
            Using stream As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush(True)
            End Using
            If File.Exists(target) Then
                ' Held for a moment without sharing: another program writing right now is told apart from a
                ' file that was merely opened, and the facts are read again as late as they can be.
                File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None).Dispose()
                If request.Snapshot IsNot Nothing AndAlso Not MatchesSnapshot(request.Snapshot, target, missing) Then
                    outcome.Status = TextSaveStatus.Conflict
                    outcome.TargetMissing = missing
                    Return outcome
                End If
                Dim backup As String = BackupName(target, request.BackupVersions)
                ReplaceTarget(temporary, target, backup)
                outcome.BackupPath = RotateBackups(target, request.BackupVersions, backup)
            Else
                File.Move(temporary, target)
            End If
            outcome.Status = TextSaveStatus.Saved
            outcome.BytesWritten = bytes.Length
            outcome.NewSnapshot = New TextFileSnapshot With {
                .FilePath = target,
                .Length = bytes.Length,
                .LastWriteTimeUtc = File.GetLastWriteTimeUtc(target),
                .Sha256 = HashOf(bytes)
            }
        Catch ex As Exception When IsDiskFailure(ex)
            outcome.Status = TextSaveStatus.Failed
            outcome.Failure = ex
        Finally
            Try
                If File.Exists(temporary) Then File.Delete(temporary)
            Catch ex As Exception When IsDiskFailure(ex)
                ' A leftover temporary file harms nothing and is not worth losing the outcome for.
            End Try
        End Try
        Return outcome
    End Function

    Private Function IsDiskFailure(ex As Exception) As Boolean
        Return TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
            TypeOf ex Is NotSupportedException OrElse TypeOf ex Is System.Security.SecurityException
    End Function

    ''' <summary>The name the old version goes to during the swap. Nothing keeps no version; of several versions it waits
    ''' under a name of its own until the swap has worked.</summary>
    Public Function BackupName(target As String, versions As Integer) As String
        If versions <= 0 Then Return Nothing
        Return target & If(versions = 1, ".~bak", ".bak-new")
    End Function

    ''' <summary>The swap worked: the older versions move up and the new one takes its number. Only now, so a failed save
    ''' leaves every version where it was. If this step fails the save still stands, and the version stays where it waited.</summary>
    Private Function RotateBackups(target As String, versions As Integer, waiting As String) As String
        If versions <= 1 Then Return waiting
        Try
            Dim oldest As String = target & ".bak" & versions.ToString(Globalization.CultureInfo.InvariantCulture)
            If File.Exists(oldest) Then File.Delete(oldest)
            For version As Integer = versions - 1 To 1 Step -1
                Dim older As String = target & ".bak" & version.ToString(Globalization.CultureInfo.InvariantCulture)
                If File.Exists(older) Then File.Move(older, target & ".bak" & (version + 1).ToString(Globalization.CultureInfo.InvariantCulture))
            Next
            File.Move(waiting, target & ".bak1")
            Return target & ".bak1"
        Catch ex As Exception When IsDiskFailure(ex)
            Return waiting
        End Try
    End Function

    ''' <summary>
    ''' The swap. Where the file system has no atomic replace (some network shares, some removable disks), the old
    ''' file is moved aside under a temporary name and moved back if the new one cannot take its place, so the name
    ''' never holds a half-written file. When File.Replace reports it failed after moving the old file to the
    ''' backup name, that file is moved back.
    ''' </summary>
    Private Sub ReplaceTarget(temporary As String, target As String, backup As String)
        Try
            File.Replace(temporary, target, backup, True)
            Return
        Catch ex As PlatformNotSupportedException
        Catch ex As IOException When ex.HResult = ErrorNotSupported OrElse ex.HResult = ErrorInvalidFunction
        Catch ex As IOException
            If Not File.Exists(target) AndAlso backup IsNot Nothing AndAlso File.Exists(backup) Then File.Move(backup, target)
            Throw
        End Try
        Dim aside As String = target & "." & Guid.NewGuid().ToString("N") & ".fms-old"
        File.Move(target, aside)
        Try
            File.Move(temporary, target)
        Catch ex As Exception When IsDiskFailure(ex)
            File.Move(aside, target)
            Throw
        End Try
        If backup Is Nothing Then
            File.Delete(aside)
        Else
            If File.Exists(backup) Then File.Delete(backup)
            File.Move(aside, backup)
        End If
    End Sub

    ''' <summary>Counts what the encoding cannot store and lets the encoder go on, so one pass over the text lists it all.</summary>
    Private NotInheritable Class UnencodableWatcher
        Inherits EncoderFallback

        Private ReadOnly _seen As New HashSet(Of String)(StringComparer.Ordinal)
        Private ReadOnly _listed As New List(Of String)()

        Public Property Count As Integer

        Public ReadOnly Property Listed As IReadOnlyList(Of String)
            Get
                Return _listed
            End Get
        End Property

        Public Overrides ReadOnly Property MaxCharCount As Integer
            Get
                Return 0
            End Get
        End Property

        Public Overrides Function CreateFallbackBuffer() As EncoderFallbackBuffer
            Return New WatcherBuffer(Me)
        End Function

        Friend Sub Restart()
            Count = 0
            _seen.Clear()
            _listed.Clear()
        End Sub

        Friend Sub Note(character As String)
            Count += 1
            If _listed.Count < MaxListedCharacters AndAlso _seen.Add(character) Then _listed.Add(character)
        End Sub

        Private NotInheritable Class WatcherBuffer
            Inherits EncoderFallbackBuffer

            Private ReadOnly _owner As UnencodableWatcher

            Public Sub New(owner As UnencodableWatcher)
                _owner = owner
            End Sub

            Public Overrides ReadOnly Property Remaining As Integer
                Get
                    Return 0
                End Get
            End Property

            Public Overloads Overrides Function Fallback(charUnknown As Char, index As Integer) As Boolean
                _owner.Note(charUnknown.ToString())
                Return False
            End Function

            Public Overloads Overrides Function Fallback(charUnknownHigh As Char, charUnknownLow As Char, index As Integer) As Boolean
                _owner.Note(charUnknownHigh.ToString() & charUnknownLow.ToString())
                Return False
            End Function

            Public Overrides Function GetNextChar() As Char
                Return ControlChars.NullChar
            End Function

            Public Overrides Function MovePrevious() As Boolean
                Return False
            End Function
        End Class
    End Class

End Module
#End If
