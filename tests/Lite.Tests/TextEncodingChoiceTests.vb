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

' Specification 277 section 6.2, step P3.4: the groups, the preview and the per-document memory behind
' Re-open with Encoding. The dialog itself is WinForms glue and is accepted by hand (VM-07).
Public Class TextEncodingChoiceTests

    Private Const Russian As String = "Съешь же ещё этих мягких французских булок"

    Private Shared Function Encode(codePage As Integer, text As String) As Byte()
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
        Return Encoding.GetEncoding(codePage).GetBytes(text)
    End Function

    Private Shared Function Identity(path As String, Optional ticks As Long = 100, Optional length As Long = 10) As DocumentIdentity
        Return New DocumentIdentity With {.Path = path, .LastWriteUtcTicks = ticks, .Length = length}
    End Function

    Private Shared Function TempStore() As String
        Return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms-277-" & Guid.NewGuid().ToString("N"), "text-encodings-v1.txt")
    End Function

    Private Shared Sub DeleteStore(path As String)
        Dim folder As String = System.IO.Path.GetDirectoryName(path)
        If Directory.Exists(folder) Then Directory.Delete(folder, True)
    End Sub

    <Fact>
    Public Sub Every_manual_candidate_has_a_group_and_the_groups_follow_the_spec()
        For Each candidate In TextEncodingDetector.ManualCandidates
            Assert.True(TextEncodingChoice.IsManualCodePage(candidate.CodePage))
        Next
        Assert.Equal(TextEncodingGroup.Unicode, TextEncodingChoice.GroupOf(65001))
        Assert.Equal(TextEncodingGroup.Cyrillic, TextEncodingChoice.GroupOf(866))
        Assert.Equal(TextEncodingGroup.Cyrillic, TextEncodingChoice.GroupOf(20866))
        Assert.Equal(TextEncodingGroup.Western, TextEncodingChoice.GroupOf(1252))
        Assert.Equal(TextEncodingGroup.EastAsian, TextEncodingChoice.GroupOf(932))
        Assert.False(TextEncodingChoice.IsManualCodePage(437))
    End Sub

    <Fact>
    Public Sub Initial_choice_is_the_current_encoding_when_offered_else_utf8()
        Assert.Equal(866, TextEncodingChoice.InitialChoice(TextEncodingDetector.CreateEncoding(866, False)))
        Assert.Equal(65001, TextEncodingChoice.InitialChoice(Nothing))
        Assert.Equal(65001, TextEncodingChoice.InitialChoice(Encoding.ASCII))
    End Sub

    <Fact>
    Public Sub Preview_of_the_right_code_page_reads_every_character()
        Dim bytes = Encode(866, Russian & vbCrLf & "вторая" & vbLf & "третья")
        Dim result = TextEncodingChoice.Preview(bytes, bytes.Length, False, 866)
        Assert.Equal(0, result.UnreadableCount)
        Assert.Equal(New String() {Russian, "вторая", "третья"}, result.Lines)
        Assert.False(result.HasBom)
    End Sub

    <Fact>
    Public Sub Preview_of_utf8_on_cp1251_bytes_counts_the_unreadable_characters()
        Dim bytes = Encode(1251, Russian)
        Dim result = TextEncodingChoice.Preview(bytes, bytes.Length, False, 65001)
        Assert.True(result.UnreadableCount > 0)
        Assert.Single(result.Lines)
        Assert.Contains(ChrW(&HFFFD).ToString(), result.Lines(0))
    End Sub

    <Fact>
    Public Sub Preview_skips_the_bom_of_the_chosen_encoding_only()
        Dim body = Encoding.UTF8.GetBytes("abc")
        Dim bytes = New Byte() {&HEF, &HBB, &HBF}.Concat(body).ToArray()
        Dim utf8 = TextEncodingChoice.Preview(bytes, bytes.Length, False, 65001)
        Assert.True(utf8.HasBom)
        Assert.Equal("abc", utf8.Lines(0))
        Dim western = TextEncodingChoice.Preview(bytes, bytes.Length, False, 1252)
        Assert.False(western.HasBom)
        Assert.Equal("ï»¿abc", western.Lines(0))
    End Sub

    <Fact>
    Public Sub Reopening_a_utf8_bom_file_as_utf8_skips_the_bom()
        Dim bytes = New Byte() {&HEF, &HBB, &HBF}.Concat(Encoding.UTF8.GetBytes("abc")).ToArray()
        Dim result = TextEncodingDetector.Detect(bytes, bytes.Length, False, 1252, 65001)
        Assert.Equal(EncodingEvidence.UserOverride, result.Evidence)
        Assert.True(result.HasBom)
        Assert.Equal(3, result.BomLength)
    End Sub

    <Fact>
    Public Sub Preview_keeps_five_lines_and_cuts_long_lines_at_the_column_limit()
        Dim builder As New StringBuilder()
        For index As Integer = 1 To 8
            builder.Append(New String("x"c, 300)).Append(vbLf)
        Next
        Dim bytes = Encoding.UTF8.GetBytes(builder.ToString())
        Dim result = TextEncodingChoice.Preview(bytes, bytes.Length, False, 65001)
        Assert.Equal(TextEncodingChoice.PreviewLineCount, result.Lines.Length)
        Assert.All(result.Lines, Sub(line) Assert.Equal(TextEncodingChoice.PreviewColumnLimit, line.Length))
    End Sub

    <Fact>
    Public Sub Preview_never_keeps_half_a_surrogate_pair_at_the_column_limit()
        Dim text = New String("x"c, TextEncodingChoice.PreviewColumnLimit - 1) & ChrW(&HD83D) & ChrW(&HDE00) & "tail"
        Dim bytes = Encoding.UTF8.GetBytes(text)
        Dim line = TextEncodingChoice.Preview(bytes, bytes.Length, False, 65001).Lines(0)
        Assert.False(Char.IsHighSurrogate(line(line.Length - 1)))
        Assert.True(line.Length <= TextEncodingChoice.PreviewColumnLimit)
    End Sub

    <Fact>
    Public Sub Preview_does_not_count_a_character_cut_by_the_probe_end()
        Dim bytes = Encoding.UTF8.GetBytes("abcж")
        Dim cut = bytes.Length - 1
        Assert.Equal(0, TextEncodingChoice.Preview(bytes, cut, True, 65001).UnreadableCount)
        Assert.Equal(1, TextEncodingChoice.Preview(bytes, cut, False, 65001).UnreadableCount)
    End Sub

    <Fact>
    Public Sub Preview_counts_control_characters_but_not_tabs()
        Dim bytes = Encoding.ASCII.GetBytes("a" & vbTab & "b" & ChrW(1) & "c")
        Dim result = TextEncodingChoice.Preview(bytes, bytes.Length, False, 65001)
        Assert.Equal(1, result.UnreadableCount)
        Assert.Equal("a" & vbTab & "b" & ChrW(&HFFFD) & "c", result.Lines(0))
    End Sub

    <Fact>
    Public Sub Memory_round_trips_a_choice_through_the_file()
        Dim path = TempStore()
        Try
            Dim memory As New TextEncodingMemory(path)
            memory.Remember(Identity("C:\docs\a.txt"), 866)
            memory.Remember(Identity("C:\docs\b.txt"), 20866)
            Dim reloaded As New TextEncodingMemory(path)
            reloaded.Load()
            Assert.Equal("", reloaded.Diagnostic)
            Assert.Equal(866, reloaded.Lookup(Identity("c:\DOCS\a.txt")))
            Assert.Equal(20866, reloaded.Lookup(Identity("C:\docs\b.txt")))
            Assert.Equal(2, reloaded.Count)
        Finally
            DeleteStore(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_changed_file_is_another_identity_and_its_old_choice_no_longer_applies()
        Dim path = TempStore()
        Try
            Dim memory As New TextEncodingMemory(path)
            memory.Remember(Identity("C:\docs\a.txt", 100, 10), 866)
            Assert.Equal(0, memory.Lookup(Identity("C:\docs\a.txt", 200, 10)))
            Assert.Equal(0, memory.Lookup(Identity("C:\docs\a.txt", 100, 11)))
            memory.Remember(Identity("C:\docs\a.txt", 200, 10), 1251)
            Assert.Equal(1, memory.Count)
            Assert.Equal(1251, memory.Lookup(Identity("C:\docs\a.txt", 200, 10)))
            memory.Forget(Identity("C:\docs\a.txt", 300, 12))
            Assert.Equal(0, memory.Count)
        Finally
            DeleteStore(path)
        End Try
    End Sub

    <Fact>
    Public Sub Memory_refuses_codes_outside_the_list_and_paths_that_break_the_line_format()
        Dim path = TempStore()
        Try
            Dim memory As New TextEncodingMemory(path)
            memory.Remember(Identity("C:\docs\a.txt"), 437)
            memory.Remember(Identity("C:\docs\a" & vbTab & ".txt"), 866)
            memory.Remember(Identity(""), 866)
            Assert.Equal(0, memory.Count)
            Assert.False(File.Exists(path))
        Finally
            DeleteStore(path)
        End Try
    End Sub

    <Fact>
    Public Sub Memory_keeps_the_newest_entries_up_to_the_limit()
        Dim path = TempStore()
        Try
            Dim memory As New TextEncodingMemory(path)
            For index As Integer = 1 To TextEncodingMemory.EntryLimit + 3
                memory.Remember(Identity("C:\docs\" & index.ToString() & ".txt"), 1251)
            Next
            Assert.Equal(TextEncodingMemory.EntryLimit, memory.Count)
            Assert.Equal(0, memory.Lookup(Identity("C:\docs\1.txt")))
            Assert.Equal(1251, memory.Lookup(Identity("C:\docs\" & (TextEncodingMemory.EntryLimit + 3).ToString() & ".txt")))
        Finally
            DeleteStore(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_file_of_another_format_is_kept_unchanged_and_nothing_is_written()
        Dim path = TempStore()
        Try
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path))
            File.WriteAllText(path, "something else" & vbLf)
            Dim memory As New TextEncodingMemory(path)
            memory.Load()
            Assert.StartsWith("unknown-format", memory.Diagnostic)
            memory.Remember(Identity("C:\docs\a.txt"), 866)
            Assert.Equal("something else" & vbLf, File.ReadAllText(path))
            Assert.Equal(0, memory.Lookup(Identity("C:\docs\a.txt")))
        Finally
            DeleteStore(path)
        End Try
    End Sub

    <Fact>
    Public Sub Malformed_lines_are_skipped_on_load()
        Dim path = TempStore()
        Try
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path))
            File.WriteAllText(path, "fms-text-encodings 1" & vbLf & "866" & vbTab & "10" & vbTab & "100" & vbTab & vbTab & "C:\docs\a.txt" & vbLf &
                              "abc" & vbTab & "10" & vbTab & "100" & vbTab & vbTab & "C:\docs\b.txt" & vbLf &
                              "437" & vbTab & "10" & vbTab & "100" & vbTab & vbTab & "C:\docs\c.txt" & vbLf & "garbage" & vbLf)
            Dim memory As New TextEncodingMemory(path)
            memory.Load()
            Assert.Equal(1, memory.Count)
            Assert.Equal(866, memory.Lookup(Identity("C:\docs\a.txt")))
        Finally
            DeleteStore(path)
        End Try
    End Sub
End Class
#End If
