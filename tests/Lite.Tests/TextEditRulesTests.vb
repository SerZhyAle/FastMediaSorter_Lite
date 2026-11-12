Option Explicit On
Option Strict On
Option Infer On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports Xunit

Public Class TextEditRulesTests
    Implements IDisposable

    Private ReadOnly _folder As String = Path.Combine(Path.GetTempPath(), "fms-edit-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(_folder)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_folder, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Shared Function Options(query As String, Optional matchCase As Boolean = False, Optional wholeWord As Boolean = False, Optional useRegex As Boolean = False) As TextFindOptions
        Return New TextFindOptions With {.Query = query, .MatchCase = matchCase, .WholeWord = wholeWord, .UseRegex = useRegex}
    End Function

    ' VM-18: a sort key, a delete key, a navigation key or a digit is typing or the caret, never a viewer command.
    <Theory>
    <InlineData(Keys.D1)>
    <InlineData(Keys.D0)>
    <InlineData(Keys.NumPad5)>
    <InlineData(Keys.Delete)>
    <InlineData(Keys.Space)>
    <InlineData(Keys.Left)>
    <InlineData(Keys.Right)>
    <InlineData(Keys.Up)>
    <InlineData(Keys.Down)>
    <InlineData(Keys.PageUp)>
    <InlineData(Keys.PageDown)>
    <InlineData(Keys.Home)>
    <InlineData(Keys.End)>
    <InlineData(Keys.B)>
    <InlineData(Keys.N)>
    Public Sub A_sort_or_navigation_key_is_left_to_the_editing_surface(key As Keys)
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(key))
    End Sub

    <Fact>
    Public Sub The_editor_chords_of_section_7_1_have_their_meaning()
        Assert.Equal(TextEditorCommand.Save, TextEditRules.CommandOf(Keys.Control Or Keys.S))
        Assert.Equal(TextEditorCommand.SaveAs, TextEditRules.CommandOf(Keys.Control Or Keys.Shift Or Keys.S))
        Assert.Equal(TextEditorCommand.Find, TextEditRules.CommandOf(Keys.Control Or Keys.F))
        Assert.Equal(TextEditorCommand.Replace, TextEditRules.CommandOf(Keys.Control Or Keys.H))
        Assert.Equal(TextEditorCommand.Leave, TextEditRules.CommandOf(Keys.Escape))
        Assert.Equal(TextEditorCommand.Paste, TextEditRules.CommandOf(Keys.Control Or Keys.V))
        Assert.Equal(TextEditorCommand.Paste, TextEditRules.CommandOf(Keys.Shift Or Keys.Insert))
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.Control Or Keys.Z))
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.Control Or Keys.Y))
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.Control Or Keys.A))
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.Control Or Keys.Shift Or Keys.Left))
    End Sub

    <Theory>
    <InlineData(Keys.Control Or Keys.B)>
    <InlineData(Keys.Control Or Keys.I)>
    <InlineData(Keys.Control Or Keys.U)>
    <InlineData(Keys.Control Or Keys.L)>
    <InlineData(Keys.Control Or Keys.R)>
    <InlineData(Keys.Control Or Keys.J)>
    <InlineData(Keys.Control Or Keys.D1)>
    <InlineData(Keys.Control Or Keys.D2)>
    <InlineData(Keys.Control Or Keys.D5)>
    <InlineData(Keys.Control Or Keys.Shift Or Keys.L)>
    Public Sub A_formatting_chord_of_the_rich_edit_control_is_swallowed(chord As Keys)
        Assert.Equal(TextEditorCommand.Swallow, TextEditRules.CommandOf(chord))
    End Sub

    <Theory>
    <InlineData("", 0, 0)>
    <InlineData("one", 3, 0)>
    <InlineData("a" & vbLf & "b" & vbLf & "c", 0, 0)>
    <InlineData("a" & vbLf & "b" & vbLf & "c", 1, 0)>
    <InlineData("a" & vbLf & "b" & vbLf & "c", 2, 1)>
    <InlineData("a" & vbLf & "b" & vbLf & "c", 4, 2)>
    <InlineData("a" & vbLf & "b" & vbLf & "c", 99, 2)>
    <InlineData(vbLf & vbLf, 2, 2)>
    <InlineData("a", -5, 0)>
    Public Sub The_caret_offset_gives_the_logical_line(text As String, offset As Integer, expected As Integer)
        Assert.Equal(expected, TextEditRules.LineOfOffset(text, offset))
    End Sub

    <Fact>
    Public Sub AltGr_is_typing_and_the_function_key_of_the_chrome_passes()
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.Control Or Keys.Alt Or Keys.Q))
        Assert.Equal(TextEditorCommand.None, TextEditRules.CommandOf(Keys.F10))
    End Sub

    <Fact>
    Public Sub Only_a_command_that_can_take_the_document_away_asks()
        Assert.True(TextEditRules.ChromeItemLeavesDocument("btn_Delete"))
        Assert.True(TextEditRules.ChromeItemLeavesDocument("btn_Next"))
        Assert.False(TextEditRules.ChromeItemLeavesDocument("doc_TextSave"))
        Assert.False(TextEditRules.ChromeItemLeavesDocument("win_Minimize"))
        Assert.False(TextEditRules.ChromeItemLeavesDocument("chrome_pin_chrome_Row1"))
        Assert.False(TextEditRules.ChromeItemLeavesDocument("chrome_handle_chrome_Row1"))
        Assert.False(TextEditRules.ChromeItemLeavesDocument("btn_Overflow_3"))
    End Sub

    <Fact>
    Public Sub A_clean_file_opens_with_a_snapshot_and_its_terminators()
        Dim file As String = Path.Combine(_folder, "clean.txt")
        System.IO.File.WriteAllBytes(file, Encoding.UTF8.GetBytes("a" & vbCrLf & "b" & vbLf))
        Dim opening As TextEditOpening = TextEditRules.OpenForEditing(file, New UTF8Encoding(False), False, 0, 1024)
        Assert.Equal(TextEditProblem.None, opening.Problem)
        Assert.Equal(5L, opening.Snapshot.Length)
        Assert.Equal("a" & vbLf & "b" & vbLf, opening.Buffer.EditorText())
        Assert.Equal("a" & vbCrLf & "b" & vbLf, opening.Buffer.ComposeText(opening.Buffer.EditorText()))
    End Sub

    <Fact>
    Public Sub A_byte_the_encoding_cannot_read_refuses_the_edit()
        Dim file As String = Path.Combine(_folder, "broken.txt")
        System.IO.File.WriteAllBytes(file, New Byte() {&H61, &HC0, &HAF, &H62})
        Dim opening As TextEditOpening = TextEditRules.OpenForEditing(file, New UTF8Encoding(False), False, 0, 1024)
        Assert.Equal(TextEditProblem.BytesDoNotRoundTrip, opening.Problem)
        Assert.Null(opening.Buffer)
    End Sub

    <Fact>
    Public Sub The_detectors_strict_encoding_refuses_the_same_edit_instead_of_throwing()
        Dim file As String = Path.Combine(_folder, "broken-late.txt")
        System.IO.File.WriteAllBytes(file, New Byte() {&H61, &HC0, &HAF, &H62})
        Dim opening As TextEditOpening = TextEditRules.OpenForEditing(file, New UTF8Encoding(False, True), False, 0, 1024)
        Assert.Equal(TextEditProblem.BytesDoNotRoundTrip, opening.Problem)
        Assert.Null(opening.Buffer)
    End Sub

    <Fact>
    Public Sub A_byte_order_mark_stays_out_of_the_text_and_counts_in_the_round_trip()
        Dim file As String = Path.Combine(_folder, "bom.txt")
        System.IO.File.WriteAllBytes(file, New Byte() {&HEF, &HBB, &HBF, &H61})
        Dim opening As TextEditOpening = TextEditRules.OpenForEditing(file, New UTF8Encoding(False), True, 3, 1024)
        Assert.Equal(TextEditProblem.None, opening.Problem)
        Assert.Equal("a", opening.Buffer.EditorText())
    End Sub

    <Fact>
    Public Sub A_file_that_grew_past_the_ceiling_is_not_read()
        Dim file As String = Path.Combine(_folder, "big.txt")
        System.IO.File.WriteAllBytes(file, New Byte(99) {})
        Assert.Equal(TextEditProblem.TooLarge, TextEditRules.OpenForEditing(file, New UTF8Encoding(False), False, 0, 50).Problem)
    End Sub

    <Fact>
    Public Sub The_first_difference_is_found_and_labelled()
        Assert.Equal(-1, TextEditRules.FirstDifference("abc", "abc"))
        Assert.Equal(1, TextEditRules.FirstDifference("abc", "aXc"))
        Assert.Equal(2, TextEditRules.FirstDifference("ab", "abc"))
        Assert.Equal("U+2028", TextEditRules.CharacterLabel("a" & ChrW(&H2028), 1))
        Assert.Equal("U+1F600", TextEditRules.CharacterLabel(Char.ConvertFromUtf32(&H1F600), 0))
    End Sub

    <Fact>
    Public Sub Lines_are_found_by_binary_search_and_a_wrapped_row_is_one_line()
        Dim starts As Integer() = TextEditRules.LineStarts("ab" & vbLf & vbLf & "cd")
        Assert.Equal(New Integer() {0, 3, 4}, starts)
        Assert.Equal(0, TextEditRules.LineOfIndex(starts, 0))
        Assert.Equal(0, TextEditRules.LineOfIndex(starts, 2))
        Assert.Equal(1, TextEditRules.LineOfIndex(starts, 3))
        Assert.Equal(2, TextEditRules.LineOfIndex(starts, 5))
    End Sub

    <Fact>
    Public Sub Find_is_literal_by_default_and_honours_case_and_whole_word()
        Dim problem As TextFindProblem
        Assert.Equal(2, TextEditRules.FindAll("a.b A.B axb", Options("a.b"), problem).Count)
        Assert.Equal(1, TextEditRules.FindAll("a.b A.B axb", Options("a.b", matchCase:=True), problem).Count)
        Assert.Equal(1, TextEditRules.FindAll("cat concat", Options("cat", wholeWord:=True), problem).Count)
        Assert.Equal(TextFindProblem.None, problem)
    End Sub

    <Fact>
    Public Sub A_pattern_works_per_line_and_a_bad_one_is_said_not_thrown()
        Dim problem As TextFindProblem
        Assert.Equal(2, TextEditRules.FindAll("one" & vbLf & "two", Options("^\w+$", useRegex:=True), problem).Count)
        TextEditRules.FindAll("x", Options("(", useRegex:=True), problem)
        Assert.Equal(TextFindProblem.InvalidPattern, problem)
    End Sub

    <Fact>
    Public Sub Stepping_wraps_and_never_finds_the_selected_hit_again()
        Dim hits As New List(Of TextEditHit) From {New TextEditHit(0, 2), New TextEditHit(5, 2), New TextEditHit(9, 2)}
        Assert.Equal(1, TextEditRules.NextHit(hits, 0, 2, True))
        Assert.Equal(0, TextEditRules.NextHit(hits, 9, 11, True))
        Assert.Equal(0, TextEditRules.NextHit(hits, 5, 7, False))
        Assert.Equal(2, TextEditRules.NextHit(hits, 0, 2, False))
        Assert.Equal(-1, TextEditRules.NextHit(New List(Of TextEditHit)(), 0, 0, True))
    End Sub

    <Fact>
    Public Sub A_dollar_is_literal_unless_a_pattern_is_on()
        Dim count As Integer
        Dim problem As TextFindProblem
        Assert.Equal("$1 $1", TextEditRules.ReplaceAll("ab ab", Options("ab"), "$1", count, problem))
        Assert.Equal(2, count)
        Assert.Equal("b b", TextEditRules.ReplaceAll("ab ab", Options("a(b)", useRegex:=True), "$1", count, problem))
        Assert.Equal("b", TextEditRules.ReplacementOf("ab", New TextEditHit(0, 2), Options("a(b)", useRegex:=True), "$1"))
        Assert.Equal("$1", TextEditRules.ReplacementOf("ab", New TextEditHit(0, 2), Options("ab"), "$1"))
    End Sub

    <Fact>
    Public Sub A_bad_pattern_changes_nothing()
        Dim count As Integer
        Dim problem As TextFindProblem
        Assert.Equal("text", TextEditRules.ReplaceAll("text", Options("(", useRegex:=True), "x", count, problem))
        Assert.Equal(TextFindProblem.InvalidPattern, problem)
        Assert.Equal(0, count)
    End Sub
End Class
