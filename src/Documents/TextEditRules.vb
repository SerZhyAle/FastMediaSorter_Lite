#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

' Specification 277 sections 7.1 and 7.3: the decisions of the in-place editor that need no window. Which key
' means what while it has the keyboard, whether a file may enter it at all, where a line starts, and the find
' and replace over its text. Main_Form.TextEditor.vb is the glue; everything here is called directly by tests.

''' <summary>What a key means while the editor has the keyboard.</summary>
Public Enum TextEditorCommand
    ''' <summary>The focused control handles it (typing, the caret, undo).</summary>
    None
    Save
    SaveAs
    Find
    Replace
    FindNext
    FindPrevious
    GoToLine
    Leave
    ReopenWithEncoding
    ''' <summary>Text only, never the clipboard's formatting or an embedded object.</summary>
    Paste
    ''' <summary>A formatting chord of the rich edit control: it would change the look of plain text.</summary>
    Swallow
End Enum

Public Enum TextEditProblem
    None
    ''' <summary>The file grew past the editing ceiling after the reader opened it.</summary>
    TooLarge
    ''' <summary>Encoding the decoded text again does not give the file's bytes back, so a save would change more
    ''' than the user typed.</summary>
    BytesDoNotRoundTrip
End Enum

Public NotInheritable Class TextEditOpening
    Public Property Problem As TextEditProblem
    Public Property Snapshot As TextFileSnapshot
    Public Property Buffer As TextDocumentBuffer
End Class

Public Structure TextFindOptions
    Public Query As String
    Public MatchCase As Boolean
    Public WholeWord As Boolean
    Public UseRegex As Boolean
End Structure

Public Enum TextFindProblem
    None
    InvalidPattern
    TimedOut
End Enum

Public Structure TextEditHit
    Public ReadOnly Start As Integer
    Public ReadOnly Length As Integer

    Public Sub New(start As Integer, length As Integer)
        Me.Start = start
        Me.Length = length
    End Sub
End Structure

Public Module TextEditRules

    ''' <summary>
    ''' The meaning of a key press. A chord with Ctrl and without Alt that is not on the list is swallowed: the rich
    ''' edit control would otherwise set alignment, line spacing or bold on a plain text file. Ctrl+Alt is how AltGr
    ''' arrives and is always typing.
    ''' </summary>
    Public Function CommandOf(keyData As Keys) As TextEditorCommand
        Select Case keyData
            Case Keys.Control Or Keys.S : Return TextEditorCommand.Save
            Case Keys.Control Or Keys.Shift Or Keys.S : Return TextEditorCommand.SaveAs
            Case Keys.Control Or Keys.F : Return TextEditorCommand.Find
            Case Keys.Control Or Keys.H : Return TextEditorCommand.Replace
            Case Keys.F3 : Return TextEditorCommand.FindNext
            Case Keys.Shift Or Keys.F3 : Return TextEditorCommand.FindPrevious
            Case Keys.Control Or Keys.G : Return TextEditorCommand.GoToLine
            Case Keys.Control Or Keys.E, Keys.Escape : Return TextEditorCommand.Leave
            Case Keys.Control Or Keys.Shift Or Keys.E : Return TextEditorCommand.ReopenWithEncoding
            Case Keys.Control Or Keys.V, Keys.Shift Or Keys.Insert : Return TextEditorCommand.Paste
        End Select
        Dim modifiers As Keys = keyData And Keys.Modifiers
        If (modifiers And Keys.Control) = 0 OrElse (modifiers And Keys.Alt) <> 0 Then Return TextEditorCommand.None
        Select Case keyData And Keys.KeyCode
            Case Keys.ControlKey, Keys.ShiftKey, Keys.Menu,
                 Keys.C, Keys.X, Keys.A, Keys.Z, Keys.Y, Keys.Insert, Keys.Delete, Keys.Back,
                 Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                Return TextEditorCommand.None
            Case Else
                Return TextEditorCommand.Swallow
        End Select
    End Function

    ''' <summary>
    ''' True for a floating panel's command that can take the document away, so a dirty editor asks first. The
    ''' editor's own panel, the window commands (Close has its own guard), a pin, a move handle and the "more" button
    ''' leave the document where it is.
    ''' </summary>
    Public Function ChromeItemLeavesDocument(itemId As String) As Boolean
        If String.IsNullOrEmpty(itemId) Then Return True
        For Each prefix As String In {"doc_", "win_", "chrome_pin_", "chrome_handle_", "btn_Overflow"}
            If itemId.StartsWith(prefix, StringComparison.Ordinal) Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' Reads a file for the editor. The text goes back through the strict encoder at once: a byte the encoding could
    ''' not read came through as U+FFFD, and saving that would rewrite the part of the file nobody touched.
    ''' </summary>
    Public Function OpenForEditing(filePath As String, encoding As Encoding, withBom As Boolean, bomLength As Integer,
                                   maxBytes As Long) As TextEditOpening
        Dim opening As New TextEditOpening()
        If New FileInfo(filePath).Length > maxBytes Then
            opening.Problem = TextEditProblem.TooLarge
            Return opening
        End If
        Dim snapshot As TextFileSnapshot = Nothing
        Dim bytes As Byte() = TextFileSaver.ReadForEditing(filePath, snapshot)
        Dim offset As Integer = Math.Min(bomLength, bytes.Length)
        Dim text As String
        Try
            text = encoding.GetString(bytes, offset, bytes.Length - offset)
        Catch ex As DecoderFallbackException
            ' The detector judged the start of the file; its strict encoding stops at the first later byte it cannot read.
            opening.Problem = TextEditProblem.BytesDoNotRoundTrip
            Return opening
        End Try
        If Not TextFileSaver.RoundTrips(bytes, text, encoding, withBom) Then
            opening.Problem = TextEditProblem.BytesDoNotRoundTrip
            Return opening
        End If
        opening.Snapshot = snapshot
        opening.Buffer = New TextDocumentBuffer(text)
        Return opening
    End Function

    ''' <summary>The first place two texts differ, or -1 when they are the same.</summary>
    Public Function FirstDifference(expected As String, actual As String) As Integer
        Dim shared_ As Integer = Math.Min(expected.Length, actual.Length)
        For index As Integer = 0 To shared_ - 1
            If expected(index) <> actual(index) Then Return index
        Next
        Return If(expected.Length = actual.Length, -1, shared_)
    End Function

    ''' <summary>"U+2028" for the character at a place, whole for a surrogate pair.</summary>
    Public Function CharacterLabel(text As String, index As Integer) As String
        If index < 0 OrElse index >= text.Length Then Return "U+0000"
        Dim value As Integer = If(Char.IsSurrogatePair(text, index), Char.ConvertToUtf32(text, index), AscW(text(index)) And &HFFFF)
        Return "U+" & value.ToString("X4", Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Where each line of the editing surface's text starts; a line break there is always one LF.</summary>
    Public Function LineStarts(text As String) As Integer()
        Dim starts As New List(Of Integer) From {0}
        For index As Integer = 0 To text.Length - 1
            If text(index) = ControlChars.Lf Then starts.Add(index + 1)
        Next
        Return starts.ToArray()
    End Function

    ''' <summary>The 0-based line holding a character place; a wrapped row is still one line.</summary>
    Public Function LineOfIndex(starts As Integer(), index As Integer) As Integer
        If starts Is Nothing OrElse starts.Length = 0 OrElse index <= 0 Then Return 0
        Dim found As Integer = Array.BinarySearch(starts, index)
        Return If(found >= 0, found, (Not found) - 1)
    End Function

    Private Function BuildPattern(options As TextFindOptions) As Regex
        Dim pattern As String = If(options.UseRegex, options.Query, Regex.Escape(options.Query))
        If options.WholeWord Then pattern = "\b(?:" & pattern & ")\b"
        Dim flags As RegexOptions = RegexOptions.Multiline Or RegexOptions.CultureInvariant
        If Not options.MatchCase Then flags = flags Or RegexOptions.IgnoreCase
        Return New Regex(pattern, flags, TextReaderLayout.RegexTimeout)
    End Function

    ''' <summary>Every hit in reading order, at most <see cref="TextReaderLayout.MaxMatches"/>. An empty hit is
    ''' skipped; a bad pattern or a timeout ends the search with what was found and says so.</summary>
    Public Function FindAll(text As String, options As TextFindOptions, ByRef problem As TextFindProblem) As List(Of TextEditHit)
        problem = TextFindProblem.None
        Dim hits As New List(Of TextEditHit)()
        If String.IsNullOrEmpty(options.Query) Then Return hits
        Try
            Dim found As Match = BuildPattern(options).Match(text)
            While found.Success AndAlso hits.Count < TextReaderLayout.MaxMatches
                If found.Length > 0 Then hits.Add(New TextEditHit(found.Index, found.Length))
                found = found.NextMatch()
            End While
        Catch ex As ArgumentException
            problem = TextFindProblem.InvalidPattern
        Catch ex As RegexMatchTimeoutException
            problem = TextFindProblem.TimedOut
        End Try
        Return hits
    End Function

    ''' <summary>The hit to go to from a selection, wrapping at either end; -1 when there are none. Forward starts at
    ''' the selection's end, backward at its start, so the hit already selected is never found again.</summary>
    Public Function NextHit(hits As IReadOnlyList(Of TextEditHit), selectionStart As Integer, selectionEnd As Integer, forward As Boolean) As Integer
        If hits Is Nothing OrElse hits.Count = 0 Then Return -1
        If forward Then
            For index As Integer = 0 To hits.Count - 1
                If hits(index).Start >= selectionEnd Then Return index
            Next
            Return 0
        End If
        For index As Integer = hits.Count - 1 To 0 Step -1
            If hits(index).Start < selectionStart Then Return index
        Next
        Return hits.Count - 1
    End Function

    ''' <summary>The words that replace one hit: the replacement as typed, or with $1 expanded for a pattern.</summary>
    Public Function ReplacementOf(text As String, hit As TextEditHit, options As TextFindOptions, replacement As String) As String
        Dim value As String = If(replacement, "")
        If Not options.UseRegex Then Return value
        Dim found As Match = BuildPattern(options).Match(text, hit.Start)
        Return If(found.Success AndAlso found.Index = hit.Start, found.Result(value), value)
    End Function

    ''' <summary>The text with every hit replaced; the count says how many. Nothing changes on a problem.</summary>
    Public Function ReplaceAll(text As String, options As TextFindOptions, replacement As String,
                               ByRef count As Integer, ByRef problem As TextFindProblem) As String
        count = 0
        problem = TextFindProblem.None
        If String.IsNullOrEmpty(options.Query) Then Return text
        Dim value As String = If(replacement, "")
        Dim replaced As Integer = 0
        Try
            Dim result As String = BuildPattern(options).Replace(text,
                Function(found As Match) As String
                    If found.Length = 0 Then Return ""
                    replaced += 1
                    Return If(options.UseRegex, found.Result(value), value)
                End Function)
            count = replaced
            Return result
        Catch ex As ArgumentException
            problem = TextFindProblem.InvalidPattern
        Catch ex As RegexMatchTimeoutException
            problem = TextFindProblem.TimedOut
        End Try
        Return text
    End Function

    ''' <summary>The logical line of an offset in the edit box's text, where every line ending reads as one LF. The box's own
    ''' line numbers count wrapped rows.</summary>
    Public Function LineOfOffset(text As String, offset As Integer) As Integer
        If String.IsNullOrEmpty(text) OrElse offset <= 0 Then Return 0
        Dim limit As Integer = Math.Min(offset, text.Length)
        Dim line As Integer = 0
        Dim found As Integer = text.IndexOf(ControlChars.Lf, 0, limit)
        While found >= 0
            line += 1
            If found + 1 >= limit Then Exit While
            found = text.IndexOf(ControlChars.Lf, found + 1, limit - found - 1)
        End While
        Return line
    End Function
End Module
#End If
