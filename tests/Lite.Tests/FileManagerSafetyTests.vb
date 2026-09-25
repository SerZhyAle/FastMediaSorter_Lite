Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' Two ways a file operation destroyed the file it was about (audit 046). Both runtimes:
''' FileManager compiles into both viewers.
'''
''' C-02: the same file under two spellings. <c>\\?\C:\..</c> stands in for the field case
''' (a mapped drive against its UNC path): it is the same file, and its path ROOT is a
''' different string, so a "replace" move took the staged path, deleted the destination -
''' the source itself - and then deleted the copy it had just put back.
'''
''' C-04: undoing a copy after the original was deleted destroyed the last instance.
''' </summary>
Public Class FileManagerSafetyTests
    Implements IDisposable

    Private ReadOnly _dir As String

    Public Sub New()
        _dir = Path.Combine(Path.GetTempPath(), "fms_safety_tests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_dir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_dir, True)
        Catch
        End Try
    End Sub

    Private Function MakeFile(name As String, Optional content As String = "photo") As String
        Dim p As String = Path.Combine(_dir, name)
        File.WriteAllText(p, content)
        Return p
    End Function

    ''' <summary>The same file, spelled so that Path.GetPathRoot differs.</summary>
    Private Shared Function OtherSpelling(fullPath As String) As String
        Return "\\?\" & fullPath
    End Function

    ' ------------------------------------------------------------- identity ----

    <Fact>
    Public Sub Identity_sees_one_file_behind_two_spellings()
        Dim photo As String = MakeFile("a.jpg")

        Assert.True(FileManager.IsSameFile(photo, OtherSpelling(photo)))
        Assert.True(FileManager.IsSameFile(photo, photo.ToUpperInvariant()))
    End Sub

    <Fact>
    Public Sub Identity_tells_two_files_apart_even_with_the_same_content()
        Dim a As String = MakeFile("a.jpg", "same")
        Dim b As String = MakeFile("b.jpg", "same")

        Assert.False(FileManager.IsSameFile(a, b))
    End Sub

    <Fact>
    Public Sub Unknown_identity_is_not_a_match()
        Dim a As String = MakeFile("a.jpg")

        Assert.False(FileManager.IsSameFile(a, Path.Combine(_dir, "missing.jpg")))
        Assert.False(FileManager.IsSameFile("", a))
    End Sub

    <Fact>
    Public Sub Identity_reads_a_file_another_program_holds_open()
        Dim a As String = MakeFile("held.jpg")
        Using holder As New FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            ' No data access and every sharing mode: it must not need what the holder denies.
            Assert.True(FileManager.IsSameFile(a, OtherSpelling(a)))
        End Using
    End Sub

    ' ------------------------------------------------------ C-02: the move ----

    <Fact>
    Public Sub A_replace_move_onto_itself_under_another_spelling_keeps_the_file()
        Dim photo As String = MakeFile("a.jpg", "the only copy")

        Assert.ThrowsAny(Of IOException)(Sub() FileManager.MoveFile(photo, OtherSpelling(photo), overwrite:=True))

        Assert.True(File.Exists(photo), "The move deleted the file it was moving onto itself.")
        Assert.Equal("the only copy", File.ReadAllText(photo))
        Assert.False(File.Exists(photo & FileManager.Partial_Move_Suffix), "The staged copy was left behind.")
    End Sub

    <Fact>
    Public Sub A_replace_copy_onto_itself_under_another_spelling_keeps_the_file()
        Dim photo As String = MakeFile("a.jpg", "the only copy")

        Assert.ThrowsAny(Of IOException)(Sub() FileManager.CopyFile(photo, OtherSpelling(photo), overwrite:=True))

        Assert.Equal("the only copy", File.ReadAllText(photo))
    End Sub

    <Fact>
    Public Sub A_real_replace_across_two_roots_still_replaces()
        ' The guard must not turn every cross-root replace into a refusal.
        Dim target As String = MakeFile("target.jpg", "old")
        Dim source As String = MakeFile("source.jpg", "new")

        FileManager.MoveFile(source, OtherSpelling(target), overwrite:=True)

        Assert.False(File.Exists(source))
        Assert.Equal("new", File.ReadAllText(target))
    End Sub

    ' ------------------------------------ 047 C-08: the copy is staged too ----

    <Fact>
    Public Sub A_copy_lands_complete_and_leaves_no_staging_file()
        Dim source As String = MakeFile("clip.mp4", "all of it")
        Dim target As String = Path.Combine(_dir, "slot_clip.mp4")

        FileManager.CopyFile(source, target)

        Assert.Equal("all of it", File.ReadAllText(target))
        Assert.True(File.Exists(source), "A copy must leave the original where it was.")
        Assert.False(File.Exists(target & FileManager.Partial_Move_Suffix), "The staged copy was left behind.")
    End Sub

    ''' <summary>The source cannot be read (another program holds it exclusively): nothing
    ''' may appear under the final name, and no staging debris either.</summary>
    <Fact>
    Public Sub A_copy_that_cannot_read_its_source_leaves_nothing_under_the_final_name()
        Dim source As String = MakeFile("held.mp4", "video")
        Dim target As String = Path.Combine(_dir, "slot_held.mp4")

        Using holder As New FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            Assert.ThrowsAny(Of IOException)(Sub() FileManager.CopyFile(source, target))
        End Using

        Assert.False(File.Exists(target), "A failed copy left a file under the real name.")
        Assert.False(File.Exists(target & FileManager.Partial_Move_Suffix), "A failed copy left its staging file.")
    End Sub

    ''' <summary>The C-08 case that cost data: with "replace", File.Copy truncated the old
    ''' destination before the new bytes had arrived. A failed replacing copy must leave the
    ''' old destination exactly as it was.</summary>
    <Fact>
    Public Sub A_failed_replacing_copy_leaves_the_old_destination_intact()
        Dim source As String = MakeFile("new.mp4", "new video")
        Dim target As String = MakeFile("slot.mp4", "the old one, still wanted")

        Using holder As New FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            Assert.ThrowsAny(Of IOException)(Sub() FileManager.CopyFile(source, target, overwrite:=True))
        End Using

        Assert.Equal("the old one, still wanted", File.ReadAllText(target))
        Assert.False(File.Exists(target & FileManager.Partial_Move_Suffix))
    End Sub

    <Fact>
    Public Sub A_replacing_copy_replaces()
        Dim source As String = MakeFile("new.mp4", "new video")
        Dim target As String = MakeFile("slot.mp4", "old video")

        FileManager.CopyFile(source, target, overwrite:=True)

        Assert.Equal("new video", File.ReadAllText(target))
        Assert.Equal("new video", File.ReadAllText(source))
    End Sub

    ''' <summary>Without "replace" a taken name is refused before a byte is copied, and the
    ''' refusal says "exists" so the status line does not blame the connection (C-17).</summary>
    <Fact>
    Public Sub A_copy_onto_a_taken_name_is_refused_as_exists()
        Dim source As String = MakeFile("a.jpg", "new")
        Dim target As String = MakeFile("b.jpg", "old")

        Dim failure As IOException = Assert.ThrowsAny(Of IOException)(Sub() FileManager.CopyFile(source, target))

        Assert.Equal("old", File.ReadAllText(target))
        Assert.Equal(PathFailureKind.Exists, PathFailure.Classify(failure))
    End Sub

    ' ------------------------------ 047 C-16: a read-only source can be moved ----

    ''' <summary>A camera's "protected" photo, moved across roots (the staged path): the copy
    ''' landed and File.Delete of the read-only source threw, so the move "failed" with the
    ''' file in both places.</summary>
    <Fact>
    Public Sub A_read_only_file_moves_across_roots()
        Dim source As String = MakeFile("protected.jpg", "photo")
        File.SetAttributes(source, File.GetAttributes(source) Or FileAttributes.ReadOnly)
        Dim target As String = Path.Combine(_dir, "sorted_protected.jpg")

        Try
            FileManager.MoveFile(source, OtherSpelling(target))

            Assert.False(File.Exists(source), "The read-only source was left behind.")
            Assert.Equal("photo", File.ReadAllText(target))
            Assert.False(File.Exists(target & FileManager.Partial_Move_Suffix))
        Finally
            For Each leftover As String In {source, target}
                If File.Exists(leftover) Then File.SetAttributes(leftover, FileAttributes.Normal)
            Next
        End Try
    End Sub

    ' ------------------------------------------------- C-04: undo of a copy ----

    <Fact>
    Public Sub Undo_of_a_copy_may_delete_it_while_the_original_is_there()
        Dim original As String = MakeFile("x.mp4", "video")
        Dim copy As String = MakeFile("copy.mp4", "video")

        Assert.True(FileManager.CopyUndoMayDelete(original, copy))
    End Sub

    <Fact>
    Public Sub Undo_of_a_copy_refuses_once_the_original_is_gone()
        Dim original As String = MakeFile("x.mp4", "video")
        Dim copy As String = MakeFile("copy.mp4", "video")
        File.Delete(original)

        Assert.False(FileManager.CopyUndoMayDelete(original, copy))
    End Sub

    <Fact>
    Public Sub Undo_of_a_copy_refuses_when_the_original_changed_size()
        Dim original As String = MakeFile("x.mp4", "video, edited since")
        Dim copy As String = MakeFile("copy.mp4", "video")

        Assert.False(FileManager.CopyUndoMayDelete(original, copy))
    End Sub

    <Fact>
    Public Sub Undo_of_a_copy_refuses_when_the_copy_is_the_original_itself()
        Dim original As String = MakeFile("x.mp4", "video")

        Assert.False(FileManager.CopyUndoMayDelete(original, OtherSpelling(original)))
    End Sub

    <Theory>
    <InlineData(True, 5L, True, 5L, False, True)>
    <InlineData(False, -1L, True, 5L, False, False)>
    <InlineData(True, 6L, True, 5L, False, False)>
    <InlineData(True, 5L, True, 5L, True, False)>
    <InlineData(False, -1L, False, -1L, False, True)>
    Public Sub The_rule_itself(originalExists As Boolean, originalLength As Long, copyExists As Boolean,
                               copyLength As Long, sameFile As Boolean, expected As Boolean)
        Assert.Equal(expected, FileManager.CopyUndoMayDelete(originalExists, originalLength, copyExists, copyLength, sameFile))
    End Sub

End Class
