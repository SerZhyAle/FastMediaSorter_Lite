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

' Specification 277 section 7.3, cases VM-11, VM-14 and VM-15, on real files in a temporary folder: the target
' is replaced whole or left untouched, nothing is replaced silently, and no temporary file stays behind.
Public Class TextFileSaverTests
    Implements IDisposable

    Private ReadOnly _folder As String = Path.Combine(Path.GetTempPath(), "fms-saver-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(_folder)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each file_ In Directory.GetFiles(_folder)
            File.SetAttributes(file_, FileAttributes.Normal)
        Next
        Directory.Delete(_folder, True)
    End Sub

    Private Function Target(name As String) As String
        Return Path.Combine(_folder, name)
    End Function

    Private Function Leftovers() As String()
        Return Directory.GetFiles(_folder).Where(Function(name) name.EndsWith(TextFileSaver.TemporarySuffix, StringComparison.Ordinal)).ToArray()
    End Function

    Private Function Request(filePath As String, text As String, snapshot As TextFileSnapshot, Optional versions As Integer = 0) As TextSaveRequest
        Return New TextSaveRequest With {
            .TargetPath = filePath,
            .Text = text,
            .Encoding = New UTF8Encoding(False),
            .Snapshot = snapshot,
            .BackupVersions = versions
        }
    End Function

    <Fact>
    Public Sub A_save_replaces_the_file_and_leaves_no_temporary()
        Dim filePath As String = Target("a.txt")
        File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("old"))
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "new", snapshot))
        Assert.Equal(TextSaveStatus.Saved, outcome.Status)
        Assert.Equal("new", File.ReadAllText(filePath))
        Assert.Empty(Leftovers())
        Assert.Equal(3L, outcome.BytesWritten)
        Assert.Equal(TextFileSaver.HashOfFile(filePath), outcome.NewSnapshot.Sha256)
        ' The new snapshot is the reference of the next save.
        Assert.Equal(TextSaveStatus.Saved, TextFileSaver.Save(Request(filePath, "newer", outcome.NewSnapshot)).Status)
    End Sub

    <Fact>
    Public Sub A_file_changed_from_outside_is_a_conflict_and_stays_as_it_is()
        Dim filePath As String = Target("b.txt")
        File.WriteAllText(filePath, "opened")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        File.WriteAllText(filePath, "changed from outside")
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "mine", snapshot))
        Assert.Equal(TextSaveStatus.Conflict, outcome.Status)
        Assert.False(outcome.TargetMissing)
        Assert.Equal("changed from outside", File.ReadAllText(filePath))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub Same_length_other_bytes_is_a_conflict_and_a_mere_touch_is_not()
        Dim filePath As String = Target("c.txt")
        File.WriteAllText(filePath, "abc")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        File.SetLastWriteTimeUtc(filePath, snapshot.LastWriteTimeUtc.AddHours(1))
        Dim missing As Boolean
        Assert.True(TextFileSaver.MatchesSnapshot(snapshot, filePath, missing))
        File.WriteAllText(filePath, "xyz")
        File.SetLastWriteTimeUtc(filePath, snapshot.LastWriteTimeUtc.AddHours(2))
        Assert.False(TextFileSaver.MatchesSnapshot(snapshot, filePath, missing))
        Assert.False(missing)
    End Sub

    <Fact>
    Public Sub A_file_gone_from_disk_is_a_conflict_that_says_so()
        Dim filePath As String = Target("d.txt")
        File.WriteAllText(filePath, "opened")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        File.Delete(filePath)
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "mine", snapshot))
        Assert.Equal(TextSaveStatus.Conflict, outcome.Status)
        Assert.True(outcome.TargetMissing)
        Assert.False(File.Exists(filePath))
    End Sub

    <Fact>
    Public Sub Without_a_snapshot_the_other_version_is_taken_over()
        Dim filePath As String = Target("e.txt")
        File.WriteAllText(filePath, "outside")
        Assert.Equal(TextSaveStatus.Saved, TextFileSaver.Save(Request(filePath, "forced", Nothing)).Status)
        Assert.Equal("forced", File.ReadAllText(filePath))
        ' And a save to a new name creates the file.
        Dim other As String = Target("new.txt")
        Assert.Equal(TextSaveStatus.Saved, TextFileSaver.Save(Request(other, "fresh", Nothing)).Status)
        Assert.Equal("fresh", File.ReadAllText(other))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_character_the_encoding_cannot_store_stops_the_save_with_a_list()
        Dim filePath As String = Target("f.txt")
        Dim cp1251 As Encoding = TextEncodingDetector.CreateEncoding(1251, False)
        File.WriteAllBytes(filePath, cp1251.GetBytes("Привет"))
        Dim snapshot As TextFileSnapshot = Nothing
        Dim original As Byte() = TextFileSaver.ReadForEditing(filePath, snapshot)
        Dim request_ As TextSaveRequest = Request(filePath, "Привет 日本語 語", snapshot)
        request_.Encoding = cp1251
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(request_)
        Assert.Equal(TextSaveStatus.Unencodable, outcome.Status)
        Assert.Equal(4, outcome.UnencodableCount)
        Assert.Equal(New String() {"日", "本", "語"}, outcome.UnencodableCharacters.ToArray())
        Assert.Equal(original, File.ReadAllBytes(filePath))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_character_outside_the_basic_plane_is_listed_whole()
        Dim characters As Collections.Generic.IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Dim emoji As String = Char.ConvertFromUtf32(&H1F600)
        Assert.Null(TextFileSaver.Encode("a" & emoji, TextEncodingDetector.CreateEncoding(1252, False), False, False, characters, count))
        Assert.Equal(1, count)
        Assert.Equal(emoji, characters(0))
    End Sub

    <Fact>
    Public Sub Replacing_takes_the_users_word_and_then_writes_question_marks()
        Dim characters As Collections.Generic.IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Dim bytes As Byte() = TextFileSaver.Encode("a日", TextEncodingDetector.CreateEncoding(1251, False), False, True, characters, count)
        Assert.Equal(1, count)
        Assert.Equal(New Byte() {&H61, &H3F}, bytes)
    End Sub

    <Fact>
    Public Sub The_byte_order_mark_is_written_when_the_file_had_one()
        Dim characters As Collections.Generic.IReadOnlyList(Of String) = Nothing
        Dim count As Integer
        Assert.Equal(New Byte() {&HEF, &HBB, &HBF, &H61}, TextFileSaver.Encode("a", TextEncodingDetector.CreateEncoding(65001, False), True, False, characters, count))
        Assert.Equal(New Byte() {&H61}, TextFileSaver.Encode("a", TextEncodingDetector.CreateEncoding(65001, False), False, False, characters, count))
        Assert.Equal(New Byte() {&HFF, &HFE, &H61, 0}, TextFileSaver.Encode("a", TextEncodingDetector.CreateEncoding(1200, False), True, False, characters, count))
        Assert.Equal(New Byte() {&HFE, &HFF, 0, &H61}, TextFileSaver.Encode("a", TextEncodingDetector.CreateEncoding(1201, False), True, False, characters, count))
        Assert.Equal(New Byte() {0, 0, &HFE, &HFF, 0, 0, 0, &H61}, TextFileSaver.Encode("a", TextEncodingDetector.CreateEncoding(12001, False), True, False, characters, count))
    End Sub

    <Fact>
    Public Sub A_read_only_file_fails_and_keeps_its_bytes()
        Dim filePath As String = Target("g.txt")
        File.WriteAllText(filePath, "keep me")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        File.SetAttributes(filePath, FileAttributes.ReadOnly)
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "mine", snapshot))
        Assert.Equal(TextSaveStatus.Failed, outcome.Status)
        Assert.NotNull(outcome.Failure)
        Assert.Equal("keep me", File.ReadAllText(filePath))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_locked_file_fails_and_keeps_its_bytes()
        Dim filePath As String = Target("h.txt")
        File.WriteAllText(filePath, "keep me")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        Using holder As New FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "mine", snapshot))
            Assert.Equal(TextSaveStatus.Failed, outcome.Status)
        End Using
        Assert.Equal("keep me", File.ReadAllText(filePath))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_folder_that_cannot_be_written_fails_without_a_trace()
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(Path.Combine(_folder, "missing-folder", "x.txt"), "mine", Nothing))
        Assert.Equal(TextSaveStatus.Failed, outcome.Status)
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub One_backup_version_holds_the_last_version()
        Dim filePath As String = Target("i.txt")
        File.WriteAllText(filePath, "v1")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        Dim first As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "v2", snapshot, 1))
        Assert.Equal(filePath & ".~bak", first.BackupPath)
        Assert.Equal("v1", File.ReadAllText(filePath & ".~bak"))
        TextFileSaver.Save(Request(filePath, "v3", first.NewSnapshot, 1))
        Assert.Equal("v2", File.ReadAllText(filePath & ".~bak"))
        Assert.Equal("v3", File.ReadAllText(filePath))
    End Sub

    <Fact>
    Public Sub Several_backup_versions_rotate_and_the_oldest_leaves()
        Dim filePath As String = Target("j.txt")
        File.WriteAllText(filePath, "v1")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        For Each version As String In New String() {"v2", "v3", "v4"}
            Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, version, snapshot, 2))
            Assert.Equal(TextSaveStatus.Saved, outcome.Status)
            snapshot = outcome.NewSnapshot
        Next
        Assert.Equal("v4", File.ReadAllText(filePath))
        Assert.Equal("v3", File.ReadAllText(filePath & ".bak1"))
        Assert.Equal("v2", File.ReadAllText(filePath & ".bak2"))
        Assert.False(File.Exists(filePath & ".bak3"))
    End Sub

    <Fact>
    Public Sub A_failed_swap_leaves_the_kept_versions_where_they_were()
        Dim filePath As String = Target("j2.txt")
        File.WriteAllText(filePath, "v1")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        For Each version As String In New String() {"v2", "v3"}
            snapshot = TextFileSaver.Save(Request(filePath, version, snapshot, 2)).NewSnapshot
        Next
        ' A reader lets the check before the swap through and stops the swap itself.
        Using holder As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            Assert.Equal(TextSaveStatus.Failed, TextFileSaver.Save(Request(filePath, "v4", snapshot, 2)).Status)
        End Using
        Assert.Equal("v3", File.ReadAllText(filePath))
        Assert.Equal("v2", File.ReadAllText(filePath & ".bak1"))
        Assert.Equal("v1", File.ReadAllText(filePath & ".bak2"))
        Assert.False(File.Exists(filePath & ".bak-new"))
    End Sub

    <Fact>
    Public Sub No_backup_keeps_nothing_beside_the_file()
        Dim filePath As String = Target("k.txt")
        File.WriteAllText(filePath, "v1")
        Dim snapshot As TextFileSnapshot = Nothing
        TextFileSaver.ReadForEditing(filePath, snapshot)
        Dim outcome As TextSaveOutcome = TextFileSaver.Save(Request(filePath, "v2", snapshot))
        Assert.Null(outcome.BackupPath)
        Assert.Single(Directory.GetFiles(_folder))
    End Sub

End Class
#End If
