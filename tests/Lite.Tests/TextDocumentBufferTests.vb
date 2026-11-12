#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports Xunit

' Specification 277 section 7.2, cases VM-01, VM-12 and VM-13: untouched lines keep their exact terminators,
' a new line takes the dominant one, and the final newline is neither added nor dropped by a save.
Public Class TextDocumentBufferTests

    <Fact>
    Public Sub Mixed_terminators_survive_a_save_without_an_edit()
        Dim source As String = "a" & vbCrLf & "b" & vbLf & "c" & vbCr & "d" & vbCrLf
        Dim buffer As New TextDocumentBuffer(source)
        Assert.Equal(LineEndingKind.Mixed, buffer.LineEnding)
        Assert.True(buffer.HasTrailingNewline)
        Assert.Equal("a" & vbLf & "b" & vbLf & "c" & vbLf & "d" & vbLf, buffer.EditorText())
        Assert.True(buffer.IsUnchanged(buffer.EditorText()))
        Assert.Equal(source, buffer.ComposeText(buffer.EditorText()))
    End Sub

    <Fact>
    Public Sub An_inserted_line_takes_the_dominant_terminator_and_the_others_keep_theirs()
        Dim buffer As New TextDocumentBuffer("a" & vbCrLf & "b" & vbLf & "c" & vbCrLf)
        Assert.Equal(vbCrLf, buffer.DominantTerminator)
        Dim edited As String = "a" & vbLf & "X" & vbLf & "b" & vbLf & "c" & vbLf
        Assert.False(buffer.IsUnchanged(edited))
        Assert.Equal("a" & vbCrLf & "X" & vbCrLf & "b" & vbLf & "c" & vbCrLf, buffer.ComposeText(edited))
    End Sub

    <Fact>
    Public Sub An_edited_line_keeps_the_terminator_it_had()
        Dim buffer As New TextDocumentBuffer("one" & vbCrLf & "two" & vbLf & "three" & vbCrLf & "four" & vbCrLf)
        Dim edited As String = "one" & vbLf & "two changed" & vbLf & "three" & vbLf & "four" & vbLf
        Assert.Equal("one" & vbCrLf & "two changed" & vbLf & "three" & vbCrLf & "four" & vbCrLf, buffer.ComposeText(edited))
    End Sub

    <Fact>
    Public Sub Untouched_lines_far_from_two_edits_keep_their_terminators()
        Dim source As New System.Text.StringBuilder()
        For index As Integer = 0 To 999
            source.Append("line ").Append(index).Append(If(index Mod 7 = 0, vbLf, vbCrLf))
        Next
        Dim buffer As New TextDocumentBuffer(source.ToString())
        Dim lines As String() = buffer.EditorText().Split(ControlChars.Lf)
        lines(10) = "changed near the start"
        lines(900) = "changed near the end"
        Dim composed As String = buffer.ComposeText(String.Join(vbLf, lines))
        Dim expected As String = source.ToString().Replace("line 10" & vbCrLf, "changed near the start" & vbCrLf).Replace("line 900" & vbCrLf, "changed near the end" & vbCrLf)
        Assert.Equal(expected, composed)
    End Sub

    <Fact>
    Public Sub A_deleted_line_takes_only_its_own_terminator_away()
        Dim buffer As New TextDocumentBuffer("a" & vbCrLf & "b" & vbLf & "c" & vbCr & "d")
        Assert.Equal("a" & vbCrLf & "c" & vbCr & "d", buffer.ComposeText("a" & vbLf & "c" & vbLf & "d"))
    End Sub

    <Fact>
    Public Sub A_file_without_a_final_newline_gains_none()
        Dim buffer As New TextDocumentBuffer("a" & vbLf & "b")
        Assert.False(buffer.HasTrailingNewline)
        Assert.Equal("a" & vbLf & "b", buffer.ComposeText(buffer.EditorText()))
        Assert.Equal("a" & vbLf & "b changed", buffer.ComposeText("a" & vbLf & "b changed"))
    End Sub

    <Fact>
    Public Sub A_final_newline_stays_and_the_text_as_edited_decides()
        Dim buffer As New TextDocumentBuffer("a" & vbCr & "b" & vbCr)
        Assert.True(buffer.HasTrailingNewline)
        Assert.Equal("a" & vbCr & "b changed" & vbCr, buffer.ComposeText("a" & vbLf & "b changed" & vbLf))
        ' The line the user added after the old last one needs a terminator: the dominant one.
        Dim open As New TextDocumentBuffer("a" & vbLf & "b")
        Assert.Equal("a" & vbLf & "b" & vbLf, open.ComposeText("a" & vbLf & "b" & vbLf))
        ' A final newline the user deleted stays deleted.
        Assert.Equal("a" & vbCr & "b", buffer.ComposeText("a" & vbLf & "b"))
    End Sub

    <Fact>
    Public Sub An_empty_file_stays_empty()
        Dim buffer As New TextDocumentBuffer("")
        Assert.Equal(1, buffer.LineCount)
        Assert.False(buffer.HasTrailingNewline)
        Assert.Equal(LineEndingKind.None, buffer.LineEnding)
        Assert.Equal("", buffer.EditorText())
        Assert.Equal("", buffer.ComposeText(""))
        Assert.True(buffer.IsUnchanged(""))
        ' A file with no terminator at all: a new line takes CRLF.
        Assert.Equal("x" & vbCrLf & "y", New TextDocumentBuffer("x").ComposeText("x" & vbLf & "y"))
    End Sub

    <Fact>
    Public Sub Normalize_line_endings_is_explicit_and_covers_every_line()
        Dim buffer As New TextDocumentBuffer("a" & vbCrLf & "b" & vbLf & "c" & vbCr)
        Assert.Equal("a" & vbLf & "b" & vbLf & "c" & vbLf, buffer.ComposeText(buffer.EditorText(), vbLf))
        Assert.Equal("a" & vbCrLf & "b" & vbCrLf & "c" & vbCrLf, buffer.ComposeText(buffer.EditorText(), vbCrLf))
    End Sub

    <Fact>
    Public Sub The_surface_may_answer_with_carriage_returns()
        Dim buffer As New TextDocumentBuffer("a" & vbCrLf & "b" & vbLf)
        Assert.True(buffer.IsUnchanged("a" & vbCr & "b" & vbCr))
        Assert.Equal("a" & vbCrLf & "b" & vbLf, buffer.ComposeText("a" & vbCr & "b" & vbCr))
    End Sub

    <Fact>
    Public Sub A_rewrite_past_the_alignment_limit_still_composes_every_line()
        Dim source As New System.Text.StringBuilder()
        Dim edited As New System.Text.StringBuilder()
        For index As Integer = 0 To 2999
            source.Append("old ").Append(index).Append(vbLf)
            edited.Append("new ").Append(index).Append(vbLf)
        Next
        Dim buffer As New TextDocumentBuffer(source.ToString())
        Assert.Equal(edited.ToString(), buffer.ComposeText(edited.ToString()))
    End Sub

    <Theory>
    <InlineData("a,b,c", "a,X,c", "0,1,2")>
    <InlineData("a,b,c", "a,c", "0,2")>
    <InlineData("a,b,c", "X,a,b,c", "-1,0,1,2")>
    <InlineData("a,b,c,d", "b,X,d", "1,2,3")>
    <InlineData("a,b", "c,d,e", "0,1,-1")>
    Public Sub Lines_are_aligned_in_order(oldList As String, newList As String, expected As String)
        Dim map As Integer() = TextDocumentBuffer.AlignLines(oldList.Split(","c), newList.Split(","c))
        Assert.Equal(expected, String.Join(",", map))
    End Sub

End Class
#End If
