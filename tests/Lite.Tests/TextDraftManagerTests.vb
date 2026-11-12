Option Explicit On
Option Strict On
Option Infer On

Imports System
Imports System.IO
Imports System.Text
Imports Xunit

Public Class TextDraftManagerTests
    Implements IDisposable

    Private ReadOnly _folder As String = Path.Combine(Path.GetTempPath(), "fms-draft-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(_folder)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_folder, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function Sample(source As String, text As String) As TextDraft
        Return New TextDraft With {
            .SourcePath = source,
            .SavedAtUtc = New DateTime(2026, 11, 8, 14, 32, 5, DateTimeKind.Utc),
            .CodePage = 65001,
            .WithBom = True,
            .NormalizedTerminator = vbLf,
            .Snapshot = New TextFileSnapshot With {.FilePath = source, .Length = 7, .LastWriteTimeUtc = New DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), .Sha256 = "ABC"},
            .Text = text}
    End Function

    ' VM-16: what comes back is exactly what was typed, with the file it was typed against.
    <Fact>
    Public Sub A_draft_comes_back_exactly()
        Dim source As String = Path.Combine(_folder, "note.txt")
        Dim typed As String = "строка" & vbCrLf & "line" & vbLf & "😀" & vbCr & "end"
        TextDraftManager.Write(_folder, Sample(source, typed))
        Dim back As TextDraft = TextDraftManager.Read(_folder, source)
        Assert.Equal(typed, back.Text)
        Assert.Equal(source, back.SourcePath)
        Assert.Equal(65001, back.CodePage)
        Assert.True(back.WithBom)
        Assert.Equal(vbLf, back.NormalizedTerminator)
        Assert.Equal(7L, back.Snapshot.Length)
        Assert.Equal("ABC", back.Snapshot.Sha256)
        Assert.Equal(New DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), back.Snapshot.LastWriteTimeUtc)
        Assert.Equal(New DateTime(2026, 11, 8, 14, 32, 5, DateTimeKind.Utc), back.SavedAtUtc)
    End Sub

    ' Section 7.4: no plaintext of the edit and no name of the file on the disk.
    <Fact>
    Public Sub The_draft_on_disk_shows_neither_the_text_nor_the_name()
        Dim source As String = Path.Combine(_folder, "secret-name.txt")
        TextDraftManager.Write(_folder, Sample(source, "top secret words"))
        Dim draftPath As String = TextDraftManager.PathFor(_folder, source)
        Assert.False(Path.GetFileName(draftPath).Contains("secret-name"))
        Assert.EndsWith(TextDraftManager.DraftExtension, draftPath, StringComparison.Ordinal)
        Dim raw As String = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(draftPath))
        Assert.False(raw.Contains("top secret"))
        Assert.False(raw.Contains("secret-name"))
    End Sub

    <Fact>
    Public Sub A_later_draft_replaces_the_earlier_and_leaves_no_temporary_file()
        Dim source As String = Path.Combine(_folder, "note.txt")
        TextDraftManager.Write(_folder, Sample(source, "first"))
        TextDraftManager.Write(_folder, Sample(source, "second"))
        Assert.Equal("second", TextDraftManager.Read(_folder, source).Text)
        Assert.Single(Directory.GetFiles(_folder))
    End Sub

    <Fact>
    Public Sub No_draft_reads_as_nothing_and_a_delete_is_quiet()
        Dim source As String = Path.Combine(_folder, "none.txt")
        Assert.False(TextDraftManager.Exists(_folder, source))
        Assert.Null(TextDraftManager.Read(_folder, source))
        Assert.True(TextDraftManager.TryDelete(_folder, source))
    End Sub

    <Fact>
    Public Sub A_damaged_draft_reads_as_nothing_and_is_kept()
        Dim source As String = Path.Combine(_folder, "note.txt")
        Dim target As String = TextDraftManager.PathFor(_folder, source)
        File.WriteAllBytes(target, New Byte() {1, 2, 3, 4})
        Assert.Null(TextDraftManager.Read(_folder, source))
        Assert.True(File.Exists(target))
    End Sub

    <Fact>
    Public Sub Delete_removes_only_the_draft()
        Dim source As String = Path.Combine(_folder, "note.txt")
        File.WriteAllText(source, "file")
        TextDraftManager.Write(_folder, Sample(source, "typed"))
        Assert.True(TextDraftManager.TryDelete(_folder, source))
        Assert.False(TextDraftManager.Exists(_folder, source))
        Assert.Equal("file", File.ReadAllText(source))
    End Sub

    ' Section 7.4 item 2: a draft that says what the file already says has nothing to recover.
    <Fact>
    Public Sub A_draft_equal_to_the_file_matches_and_a_different_one_does_not()
        Dim source As String = Path.Combine(_folder, "note.txt")
        File.WriteAllBytes(source, New Byte() {&HEF, &HBB, &HBF, &H61})
        Dim same As TextDraft = Sample(source, "a")
        Assert.True(TextDraftManager.MatchesFile(same, source))
        Assert.False(TextDraftManager.MatchesFile(Sample(source, "b"), source))
        Assert.False(TextDraftManager.MatchesFile(same, Path.Combine(_folder, "gone.txt")))
    End Sub

    ' Section 7.4 item 3.
    <Theory>
    <InlineData(True, True, False, False, False, True)>
    <InlineData(False, True, False, False, False, False)>
    <InlineData(True, False, False, False, False, False)>
    <InlineData(True, True, True, False, False, False)>
    <InlineData(True, True, False, True, False, False)>
    <InlineData(True, True, False, False, True, False)>
    Public Sub A_draft_is_only_for_a_plain_local_file(autosave As Boolean, local As Boolean, archive As Boolean, remote As Boolean, secure As Boolean, expected As Boolean)
        Assert.Equal(expected, TextDraftManager.Allowed(autosave, local, archive, remote, secure))
    End Sub
End Class
