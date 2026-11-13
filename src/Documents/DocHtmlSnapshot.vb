#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

''' <summary>Specification 276 section 3 (text editing): what part of the text a job converts. The saved file goes
''' through the ordinary document path; the other two are immutable snapshots of the editor (section 6).</summary>
Public Enum DocHtmlSnapshotScope
    ''' <summary>The editor's whole text as it is now, unsaved changes included.</summary>
    Buffer = 1
    ''' <summary>Only the selected part, labelled so it never passes for the whole document.</summary>
    Selection = 2
End Enum

''' <summary>A snapshot written for one job: its file, size and SHA-256 (section 6: the content hash of a snapshot).</summary>
Public NotInheritable Class DocHtmlSnapshotFile
    Public Property Path As String = ""
    Public Property Length As Long
    Public Property Sha256 As String = ""
End Class

''' <summary>Specification 276 Step 5.1: the editor's text (ticket 277) handed to the document coordinator as an
''' immutable UTF-8 file in a viewer-owned folder, never beside the user's document and never the user's own file - the
''' source is not saved to start a translation (section 3). Pure: no form, no preference.</summary>
Public NotInheritable Class DocHtmlSnapshot
    ''' <summary>The converter refuses text documents over 100 MB (2.2); the viewer stops below that.</summary>
    Public Const MaxBytes As Long = 64L * 1024L * 1024L
    ''' <summary>A snapshot folder left by a viewer that ended mid-job is removed after this long.</summary>
    Public Const StaleAfterHours As Integer = 24

    Private Sub New()
    End Sub

    ''' <summary>The snapshot's file name: the document's stem, a scope marker and <c>.txt</c>, so the converter reads
    ''' it as plain text and the page title says what it is (<c>notes.selection.txt</c>). An untitled or unusable stem
    ''' becomes <c>text</c>.</summary>
    Public Shared Function FileNameFor(originPath As String, scope As DocHtmlSnapshotScope) As String
        Dim stem As String = ""
        Try
            stem = Path.GetFileNameWithoutExtension(If(originPath, ""))
        Catch ex As ArgumentException
        End Try
        Dim clean As New StringBuilder()
        For Each ch As Char In If(stem, "")
            If Char.IsControl(ch) OrElse Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 Then Continue For
            clean.Append(ch)
            If clean.Length >= 80 Then Exit For
        Next
        Dim name = clean.ToString().Trim().TrimEnd("."c)
        If name.Length = 0 OrElse name.StartsWith("-", StringComparison.Ordinal) Then name = "text"
        Return name & If(scope = DocHtmlSnapshotScope.Selection, ".selection.txt", ".unsaved.txt")
    End Function

    ''' <summary>True for a secure-container document (<c>.fd-sec</c>): its plaintext never becomes a job (section 6).</summary>
    Public Shared Function IsSecureOrigin(originPath As String) As Boolean
        Return Not String.IsNullOrEmpty(originPath) AndAlso originPath.EndsWith(".fd-sec", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Writes <paramref name="text"/> as UTF-8 with a byte-order mark into a new file in
    ''' <paramref name="folder"/> (created here; the caller owns it). Nothing when the text is empty, only white space,
    ''' larger than <see cref="MaxBytes"/>, or the scope is unknown - nothing is written then.</summary>
    Public Shared Function Write(folder As String, originPath As String, scope As DocHtmlSnapshotScope, text As String) As DocHtmlSnapshotFile
        If String.IsNullOrWhiteSpace(text) Then Return Nothing
        If scope <> DocHtmlSnapshotScope.Buffer AndAlso scope <> DocHtmlSnapshotScope.Selection Then Return Nothing
        If Not DocHtmlJobArguments.IsAbsolutePath(folder) Then Return Nothing
        Dim encoding As New UTF8Encoding(encoderShouldEmitUTF8Identifier:=True)
        Dim preamble = encoding.GetPreamble()
        If text.Length > MaxBytes OrElse CLng(encoding.GetByteCount(text)) + preamble.Length > MaxBytes Then Return Nothing
        Dim body = encoding.GetBytes(text)
        Directory.CreateDirectory(folder)
        Dim target = Path.Combine(folder, FileNameFor(originPath, scope))
        Using stream As New FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
            stream.Write(preamble, 0, preamble.Length)
            stream.Write(body, 0, body.Length)
        End Using
        Dim hash As String
        Using sha = SHA256.Create()
            sha.TransformBlock(preamble, 0, preamble.Length, Nothing, 0)
            sha.TransformFinalBlock(body, 0, body.Length)
            hash = Convert.ToHexString(sha.Hash).ToLowerInvariant()
        End Using
        Return New DocHtmlSnapshotFile With {.Path = target, .Length = preamble.Length + body.LongLength, .Sha256 = hash}
    End Function

    ''' <summary>Removes the snapshot folders under <paramref name="root"/> older than <see cref="StaleAfterHours"/>,
    ''' except <paramref name="keep"/> - what a viewer that ended mid-job left behind. Returns how many went.</summary>
    Public Shared Function SweepStale(root As String, nowUtc As DateTime, keep As String) As Integer
        If Not Directory.Exists(root) Then Return 0
        Dim removed As Integer
        For Each folder In Directory.EnumerateDirectories(root)
            Try
                If Not String.IsNullOrEmpty(keep) AndAlso DocHtmlResultReader.SamePath(folder, keep) Then Continue For
                Dim info As New DirectoryInfo(folder)
                If (info.Attributes And FileAttributes.ReparsePoint) <> 0 Then Continue For
                If info.LastWriteTimeUtc > nowUtc.AddHours(-StaleAfterHours) Then Continue For
                info.Delete(recursive:=True)
                removed += 1
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        Next
        Return removed
    End Function

    ''' <summary>Removes one job's snapshot folder after the job ended; a failure leaves it to <see cref="SweepStale"/>.</summary>
    Public Shared Function TryDelete(folder As String) As Boolean
        Try
            If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return True
            Dim info As New DirectoryInfo(folder)
            If (info.Attributes And FileAttributes.ReparsePoint) <> 0 Then Return False
            info.Delete(recursive:=True)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function
End Class
#End If
