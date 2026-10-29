#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Xunit

' Specification 277 section 5.1 item 3, step P3.6: the selection saved as a new file - the file's own
' characters, the source's encoding and BOM, the dominant line ending, nothing replaced silently.
' Where the dialog starts and what it offers is WinForms glue and is accepted by hand.
Public Class TextExcerptTests

    Private Shared Function Shown(windowText As String, tabWidth As Integer, dropTrailingEmpty As Boolean) As String()
        Return TextReaderLayout.BuildLines(windowText, tabWidth, False, dropTrailingEmpty).CopyLines
    End Function

    Private Shared Function Excerpt(windowText As String, tabWidth As Integer,
                                    startLine As Integer, startColumn As Integer, endLine As Integer, endColumn As Integer) As String
        Dim problem As TextExcerptProblem
        Dim result As String = TextExcerpt.Extract(windowText, False, Shown(windowText, tabWidth, False), tabWidth,
            startLine, startColumn, endLine, endColumn, problem)
        Assert.Equal(TextExcerptProblem.None, problem)
        Return result
    End Function

    <Fact>
    Public Sub Raw_lines_split_like_the_reader_lines()
        Dim windowText As String = "a" & vbCrLf & "b" & vbLf & "c" & vbCr & "d" & vbCrLf
        Dim raw As String() = TextExcerpt.SplitRawLines(windowText, True)
        Assert.Equal(Shown(windowText, 4, True).Length, raw.Length)
        Assert.Equal(New String() {"a", "b", "c", "d"}, raw)
        Assert.Equal(5, TextExcerpt.SplitRawLines(windowText, False).Length)
    End Sub

    <Fact>
    Public Sub A_tab_comes_back_as_a_tab_and_is_never_half_selected()
        ' "x<TAB>y" with tab width 4 is shown as "x   y": y sits at column 4.
        Dim windowText As String = "x" & vbTab & "y"
        Assert.Equal("x" & vbTab & "y", Excerpt(windowText, 4, 0, 0, 0, 5))
        ' A start inside the tab's padding takes the tab, and so does an end inside it.
        Assert.Equal(vbTab & "y", Excerpt(windowText, 4, 0, 2, 0, 5))
        Assert.Equal("x" & vbTab, Excerpt(windowText, 4, 0, 0, 0, 2))
        Assert.Equal(1, TextExcerpt.RawColumn("x" & vbTab & "y", 1, 4, False))
        Assert.Equal(2, TextExcerpt.RawColumn("x" & vbTab & "y", 4, 4, False))
    End Sub

    <Fact>
    Public Sub Lines_are_joined_by_the_dominant_line_ending()
        Dim windowText As String = "one" & vbLf & "two" & vbLf & "three" & vbCrLf & "four" & vbLf
        Assert.Equal(vbLf, TextExcerpt.DominantTerminator(windowText))
        Assert.Equal("ne" & vbLf & "two" & vbLf & "th", Excerpt(windowText, 4, 0, 1, 2, 2))
        Assert.Equal(vbCrLf, TextExcerpt.DominantTerminator("a" & vbCrLf & "b" & vbLf))
        Assert.Equal(vbCr, TextExcerpt.DominantTerminator("a" & vbCr & "b" & vbCr & "c" & vbLf))
        Assert.Equal(vbCrLf, TextExcerpt.DominantTerminator("no break"))
    End Sub

    <Fact>
    Public Sub A_selection_ending_at_the_start_of_a_line_keeps_the_line_break()
        Dim windowText As String = "alpha" & vbCrLf & "beta" & vbCrLf
        Assert.Equal("alpha" & vbCrLf, Excerpt(windowText, 4, 0, 0, 1, 0))
    End Sub

    <Fact>
    Public Sub A_file_changed_since_the_load_is_refused()
        Dim problem As TextExcerptProblem
        Dim shownLines As String() = Shown("first" & vbCrLf & "second", 4, False)
        Assert.Null(TextExcerpt.Extract("first" & vbCrLf & "SECOND", False, shownLines, 4, 0, 0, 1, 3, problem))
        Assert.Equal(TextExcerptProblem.SourceChanged, problem)
        Assert.Null(TextExcerpt.Extract("first" & vbCrLf & "second" & vbCrLf & "third", False, shownLines, 4, 0, 0, 1, 3, problem))
        Assert.Equal(TextExcerptProblem.SourceChanged, problem)
    End Sub

    <Fact>
    Public Sub The_bytes_keep_the_source_encoding_and_its_byte_order_mark()
        Dim problem As TextExcerptProblem
        Dim failedAt As Integer
        Dim utf8 As Byte() = TextExcerpt.Encode("ёж", New UTF8Encoding(False), True, problem, failedAt)
        Assert.Equal(TextExcerptProblem.None, problem)
        Assert.Equal(New Byte() {&HEF, &HBB, &HBF, &HD1, &H91, &HD0, &HB6}, utf8)
        Dim utf16 As Byte() = TextExcerpt.Encode("A", New UnicodeEncoding(False, True), True, problem, failedAt)
        Assert.Equal(New Byte() {&HFF, &HFE, &H41, 0}, utf16)
        Dim noMark As Byte() = TextExcerpt.Encode("A", New UTF8Encoding(False), False, problem, failedAt)
        Assert.Equal(New Byte() {&H41}, noMark)
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
        Dim cp1251 As Byte() = TextExcerpt.Encode("ёж", Encoding.GetEncoding(1251), False, problem, failedAt)
        Assert.Equal(New Byte() {&HB8, &HE6}, cp1251)
    End Sub

    <Fact>
    Public Sub No_character_is_replaced_silently()
        Dim problem As TextExcerptProblem
        Dim failedAt As Integer
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
        Assert.Null(TextExcerpt.Encode("ok " & ChrW(&HFFFD), New UTF8Encoding(False), False, problem, failedAt))
        Assert.Equal(TextExcerptProblem.UnreadableCharacter, problem)
        Assert.Equal(3, failedAt)
        Assert.Null(TextExcerpt.Encode("ab" & ChrW(&H4E2D), Encoding.GetEncoding(1251), False, problem, failedAt))
        Assert.Equal(TextExcerptProblem.UnencodableCharacter, problem)
        Assert.Equal(2, failedAt)
    End Sub

    Private Shared Function SaveRequest(source As String, encoding As Encoding, bomLength As Integer, windowText As String,
                                        target As String, startLine As Integer, startColumn As Integer,
                                        endLine As Integer, endColumn As Integer) As TextExcerptRequest
        Return New TextExcerptRequest With {
            .SourcePath = source, .SourceEncoding = encoding, .HasBom = bomLength > 0, .BomLength = bomLength,
            .WindowBytes = 1048576, .WindowIndex = 0, .ShownLines = Shown(windowText, 4, False), .TabWidth = 4,
            .StartLine = startLine, .StartColumn = startColumn, .EndLine = endLine, .EndColumn = endColumn,
            .TargetPath = target
        }
    End Function

    <Fact>
    Public Sub Save_writes_the_selection_in_the_source_encoding_and_line_ending()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-277-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
#If Not NETFRAMEWORK Then
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
            Dim cp1251 As Encoding = Encoding.GetEncoding(1251)
            Dim windowText As String = "первая" & vbCrLf & "в" & vbTab & "торая" & vbCrLf & "третья" & vbCrLf
            Dim source As String = Path.Combine(folder, "notes.txt")
            File.WriteAllBytes(source, cp1251.GetBytes(windowText))
            Dim target As String = Path.Combine(folder, "notes (excerpt).txt")
            ' From column 2 of line 0 to column 3 of line 2; line 1 is shown with its tab expanded.
            Dim outcome As TextExcerptOutcome = TextExcerpt.Save(SaveRequest(source, cp1251, 0, windowText, target, 0, 2, 2, 3))
            Assert.Equal(TextExcerptProblem.None, outcome.Problem)
            Assert.Equal(cp1251.GetBytes("рвая" & vbCrLf & "в" & vbTab & "торая" & vbCrLf & "тре"), File.ReadAllBytes(target))

            Dim utf8Source As String = Path.Combine(folder, "bom.txt")
            File.WriteAllBytes(utf8Source, New UTF8Encoding(True).GetPreamble().Concat(Encoding.UTF8.GetBytes("ёлка" & vbLf & "ель")).ToArray())
            Dim utf8Target As String = Path.Combine(folder, "bom (excerpt).txt")
            outcome = TextExcerpt.Save(SaveRequest(utf8Source, New UTF8Encoding(False), 3, "ёлка" & vbLf & "ель", utf8Target, 0, 1, 1, 2))
            Assert.Equal(TextExcerptProblem.None, outcome.Problem)
            Assert.Equal(New UTF8Encoding(True).GetPreamble().Concat(Encoding.UTF8.GetBytes("лка" & vbLf & "ел")).ToArray(), File.ReadAllBytes(utf8Target))
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub Save_refuses_a_file_changed_on_disk_and_writes_nothing()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-277-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            Dim source As String = Path.Combine(folder, "log.txt")
            File.WriteAllText(source, "line one" & vbLf & "line two changed", New UTF8Encoding(False))
            Dim target As String = Path.Combine(folder, "log (excerpt).txt")
            Dim outcome As TextExcerptOutcome = TextExcerpt.Save(SaveRequest(source, New UTF8Encoding(False), 0,
                "line one" & vbLf & "line two", target, 0, 0, 1, 4))
            Assert.Equal(TextExcerptProblem.SourceChanged, outcome.Problem)
            Assert.False(File.Exists(target))
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

    <Fact>
    Public Sub The_file_is_written_whole_and_replaces_an_agreed_target()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-277-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            Dim target As String = Path.Combine(folder, "notes (excerpt).txt")
            TextExcerpt.WriteFile(target, New Byte() {1, 2, 3})
            Assert.Equal(New Byte() {1, 2, 3}, File.ReadAllBytes(target))
            TextExcerpt.WriteFile(target, New Byte() {4})
            Assert.Equal(New Byte() {4}, File.ReadAllBytes(target))
            Assert.False(File.Exists(target & ".fms-tmp"))
            Assert.Single(Directory.GetFiles(folder))
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub

End Class
#End If
