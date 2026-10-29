#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

' Specification 277 section 5.1 item 3: "Save Selected Text as New File..". The excerpt is a derived file
' (CAPTURE-OUTPUT 0.7 section 5): offered beside its source under the source's stem, written in the
' source's encoding (its byte-order mark included) and its dominant line ending. The text comes from the
' file's own characters, never from the tab-expanded copy on screen, and no character is replaced
' silently: a selection that cannot be written back in that encoding is refused with the reason.
' No WinForms reference; Main_Form.TextReader.vb asks for the place and says the outcome.

''' <summary>Why an excerpt was not written; <see cref="TextExcerptProblem.None"/> when it was.</summary>
Public Enum TextExcerptProblem
    None
    ''' <summary>The file on disk no longer holds the lines shown: it changed after it was opened.</summary>
    SourceChanged
    ''' <summary>The selection holds a character the encoding could not read (shown as U+FFFD).</summary>
    UnreadableCharacter
    ''' <summary>The selection holds a character the source's encoding cannot store.</summary>
    UnencodableCharacter
End Enum

''' <summary>What <see cref="TextExcerpt.Save"/> did: written, or the reason it was not, with the character index
''' of the first character that would not survive.</summary>
Public NotInheritable Class TextExcerptOutcome
    Public Property Problem As TextExcerptProblem
    Public Property FailedAt As Integer = -1
End Class

''' <summary>Everything the save needs, captured on the UI thread, so the work itself can run on the pool: the
''' window is read again through a pager of its own and the reader's pager is never touched.</summary>
Public NotInheritable Class TextExcerptRequest
    Public Property SourcePath As String
    Public Property SourceEncoding As Encoding
    Public Property HasBom As Boolean
    Public Property BomLength As Integer
    Public Property WindowBytes As Integer
    Public Property WindowIndex As Integer
    ''' <summary>The lines on screen (tabs expanded), the reference the file is checked against.</summary>
    Public Property ShownLines As String()
    Public Property TabWidth As Integer
    Public Property StartLine As Integer
    Public Property StartColumn As Integer
    Public Property EndLine As Integer
    Public Property EndColumn As Integer
    Public Property TargetPath As String
End Class

Public Module TextExcerpt

    Private Const ReplacementCharacter As Char = ChrW(&HFFFD)

    ''' <summary>
    ''' The raw lines of a decoded window, split exactly as <see cref="TextReaderLayout.BuildLines"/> splits it
    ''' (CR, LF and CRLF; the empty piece after a final break dropped when <paramref name="dropTrailingEmpty"/>),
    ''' so a line index means the same line in both.
    ''' </summary>
    Public Function SplitRawLines(text As String, dropTrailingEmpty As Boolean) As String()
        Dim lines As New List(Of String)()
        Dim source As String = If(text, "")
        Dim start As Integer = 0
        Dim i As Integer = 0
        While i <= source.Length
            Dim atEnd As Boolean = i = source.Length
            Dim c As Char = If(atEnd, ControlChars.NullChar, source(i))
            If atEnd OrElse c = ControlChars.Cr OrElse c = ControlChars.Lf Then
                lines.Add(source.Substring(start, i - start))
                If atEnd Then Exit While
                If c = ControlChars.Cr AndAlso i + 1 < source.Length AndAlso source(i + 1) = ControlChars.Lf Then i += 1
                start = i + 1
            End If
            i += 1
        End While
        If dropTrailingEmpty AndAlso lines.Count > 1 AndAlso lines(lines.Count - 1).Length = 0 Then lines.RemoveAt(lines.Count - 1)
        Return lines.ToArray()
    End Function

    ''' <summary>
    ''' The index in a raw line of a column of its tab-expanded copy. A column inside a tab's padding belongs to
    ''' that tab: a selection start there takes the tab (<paramref name="roundUp"/> False), a selection end there
    ''' takes it too (<paramref name="roundUp"/> True), so a tab is never half selected.
    ''' </summary>
    Public Function RawColumn(raw As String, expandedColumn As Integer, tabWidth As Integer, roundUp As Boolean) As Integer
        Dim width As Integer = Math.Max(1, tabWidth)
        Dim line As String = If(raw, "")
        Dim column As Integer = 0
        For index As Integer = 0 To line.Length - 1
            If column >= expandedColumn Then Return index
            Dim span As Integer = If(line(index) = ControlChars.Tab, width - (column Mod width), 1)
            If column + span > expandedColumn Then Return If(roundUp, index + 1, index)
            column += span
        Next
        Return line.Length
    End Function

    ''' <summary>The line ending used most in the window (CRLF, then LF, then CR on a tie); CRLF when the window
    ''' has none, which matters only for an excerpt of one line - and that one has no line break at all.</summary>
    Public Function DominantTerminator(text As String) As String
        Dim crlf As Integer = 0
        Dim lf As Integer = 0
        Dim cr As Integer = 0
        Dim source As String = If(text, "")
        Dim i As Integer = 0
        While i < source.Length
            Dim c As Char = source(i)
            If c = ControlChars.Cr Then
                If i + 1 < source.Length AndAlso source(i + 1) = ControlChars.Lf Then
                    crlf += 1
                    i += 1
                Else
                    cr += 1
                End If
            ElseIf c = ControlChars.Lf Then
                lf += 1
            End If
            i += 1
        End While
        If crlf >= lf AndAlso crlf >= cr Then Return ControlChars.CrLf
        If lf >= cr Then Return ControlChars.Lf
        Return ControlChars.Cr
    End Function

    ''' <summary>
    ''' The selected text in the file's own characters: the selection's positions are columns of the expanded
    ''' copy (<paramref name="shownLines"/>), mapped back to the raw lines of <paramref name="windowText"/>, joined
    ''' by the window's dominant line ending. Each selected raw line is expanded again and compared with the line
    ''' shown; a difference means the file changed after it was opened, and nothing is returned.
    ''' </summary>
    Public Function Extract(windowText As String, dropTrailingEmpty As Boolean, shownLines As String(), tabWidth As Integer,
                            startLine As Integer, startColumn As Integer, endLine As Integer, endColumn As Integer,
                            ByRef problem As TextExcerptProblem) As String
        problem = TextExcerptProblem.None
        Dim raw As String() = SplitRawLines(windowText, dropTrailingEmpty)
        If shownLines Is Nothing OrElse raw.Length <> shownLines.Length OrElse raw.Length = 0 Then
            problem = TextExcerptProblem.SourceChanged
            Return Nothing
        End If
        Dim firstLine As Integer = Math.Max(0, Math.Min(raw.Length - 1, startLine))
        Dim lastLine As Integer = Math.Max(firstLine, Math.Min(raw.Length - 1, endLine))
        For index As Integer = firstLine To lastLine
            Dim copy As String = Nothing
            Dim display As String = Nothing
            TextReaderLayout.ExpandLine(raw(index), tabWidth, False, copy, display)
            If Not String.Equals(copy, shownLines(index), StringComparison.Ordinal) Then
                problem = TextExcerptProblem.SourceChanged
                Return Nothing
            End If
        Next
        Dim fromColumn As Integer = RawColumn(raw(firstLine), Math.Max(0, startColumn), tabWidth, False)
        Dim toColumn As Integer = RawColumn(raw(lastLine), Math.Max(0, endColumn), tabWidth, True)
        If endLine > lastLine Then toColumn = raw(lastLine).Length
        If firstLine = lastLine Then
            Return raw(firstLine).Substring(fromColumn, Math.Max(0, toColumn - fromColumn))
        End If
        Dim terminator As String = DominantTerminator(windowText)
        Dim builder As New StringBuilder()
        builder.Append(raw(firstLine).Substring(fromColumn))
        For index As Integer = firstLine + 1 To lastLine - 1
            builder.Append(terminator).Append(raw(index))
        Next
        builder.Append(terminator).Append(raw(lastLine).Substring(0, toColumn))
        Return builder.ToString()
    End Function

    ''' <summary>
    ''' The bytes of the excerpt in the source's encoding, with the source's byte-order mark when it had one.
    ''' Nothing, with the reason, when a character would not survive: U+FFFD stands for bytes the encoding could
    ''' not read (writing it would replace them for good), and a character the code page cannot store is never
    ''' swapped for another. <paramref name="failedAt"/> is the character index of the first such character.
    ''' </summary>
    Public Function Encode(text As String, sourceEncoding As Encoding, withBom As Boolean,
                           ByRef problem As TextExcerptProblem, ByRef failedAt As Integer) As Byte()
        problem = TextExcerptProblem.None
        failedAt = -1
        Dim value As String = If(text, "")
        Dim unreadable As Integer = value.IndexOf(ReplacementCharacter)
        If unreadable >= 0 Then
            problem = TextExcerptProblem.UnreadableCharacter
            failedAt = unreadable
            Return Nothing
        End If
        Dim codePage As Integer = sourceEncoding.CodePage
        Dim strict As Encoding = TextEncodingDetector.CreateEncoding(codePage, True)
        If strict Is Nothing Then strict = sourceEncoding
        Dim body As Byte()
        Try
            body = strict.GetBytes(value)
        Catch ex As EncoderFallbackException
            problem = TextExcerptProblem.UnencodableCharacter
            failedAt = Math.Max(0, ex.Index)
            Return Nothing
        End Try
        Dim mark As Byte() = If(withBom, TextEncodingDetector.ByteOrderMarkOf(codePage, sourceEncoding), Array.Empty(Of Byte)())
        If mark.Length = 0 Then Return body
        Dim bytes(mark.Length + body.Length - 1) As Byte
        Buffer.BlockCopy(mark, 0, bytes, 0, mark.Length)
        Buffer.BlockCopy(body, 0, bytes, mark.Length, body.Length)
        Return bytes
    End Function

    ''' <summary>
    ''' The whole save, off the UI thread: reads the window again, takes the selection out of it, encodes it and
    ''' writes the file. A refusal comes back in the outcome and writes nothing; an I/O failure is thrown.
    ''' </summary>
    Public Function Save(request As TextExcerptRequest) As TextExcerptOutcome
        Dim outcome As New TextExcerptOutcome()
        Dim pager As New TextFilePager(request.SourcePath, request.SourceEncoding, request.BomLength, request.WindowBytes)
        Dim windowText As String = pager.ReadWindow(request.WindowIndex)
        Dim problem As TextExcerptProblem
        Dim text As String = Extract(windowText, pager.CurrentWindowIndex < pager.TotalWindows - 1, request.ShownLines, request.TabWidth,
            request.StartLine, request.StartColumn, request.EndLine, request.EndColumn, problem)
        If pager.CurrentWindowIndex <> request.WindowIndex Then problem = TextExcerptProblem.SourceChanged
        If problem <> TextExcerptProblem.None Then
            outcome.Problem = problem
            Return outcome
        End If
        Dim failedAt As Integer
        Dim bytes As Byte() = Encode(text, request.SourceEncoding, request.HasBom, problem, failedAt)
        outcome.Problem = problem
        outcome.FailedAt = failedAt
        If bytes IsNot Nothing Then WriteFile(request.TargetPath, bytes)
        Return outcome
    End Function

    ''' <summary>
    ''' Writes the excerpt to <paramref name="targetPath"/> through a sibling temporary file, so a failure leaves
    ''' no half-written file and an existing file the user agreed to replace is swapped whole.
    ''' </summary>
    Public Sub WriteFile(targetPath As String, bytes As Byte())
        Dim temporary As String = targetPath & ".fms-tmp"
        Try
            Using stream As New FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)
                stream.Write(bytes, 0, bytes.Length)
                stream.Flush(True)
            End Using
            If File.Exists(targetPath) Then
                File.Replace(temporary, targetPath, Nothing)
            Else
                File.Move(temporary, targetPath)
            End If
        Finally
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Sub

End Module
#End If
