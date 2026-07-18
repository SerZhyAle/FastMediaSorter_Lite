Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' FileManager.RenameFile - the contract the viewer's F6 depends on.
'''
''' It used to append the source extension a second time, on top of the finished name
''' the caller passes: renaming "photo.jpg" to "photo2" put "photo2.jpg.jpg" on disk
''' while Main_Form recorded "photo2.jpg" in the file list. The next display could not
''' find that path, dropped the entry - and the renamed file was gone from the viewer.
''' </summary>
Public Class FileManagerRenameTests
    Implements IDisposable

    Private ReadOnly _dir As String

    Public Sub New()
        _dir = Path.Combine(Path.GetTempPath(), "fms_rename_tests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_dir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_dir, True)
        Catch
        End Try
    End Sub

    Private Function MakeFile(name As String) As String
        Dim p As String = Path.Combine(_dir, name)
        File.WriteAllText(p, "x")
        Return p
    End Function

    <Fact>
    Public Sub RenameFile_puts_exactly_the_given_name_on_disk()
        Dim src As String = MakeFile("photo.jpg")

        Dim result As String = FileManager.RenameFile(src, "photo2.jpg")

        Assert.Equal(Path.Combine(_dir, "photo2.jpg"), result)
        Assert.True(File.Exists(result), "the renamed file must exist under the name it was given")
        Assert.False(File.Exists(Path.Combine(_dir, "photo2.jpg.jpg")), "the extension must not be appended twice")
        Assert.False(File.Exists(src), "the old name must be gone")
    End Sub

    <Fact>
    Public Sub RenameFile_returns_the_path_it_created()
        ' The caller stores this in the file list and in Current_File_Name, so it has to
        ' be the real path - not something assembled independently on the caller's side.
        Dim src As String = MakeFile("a.png")

        Dim result As String = FileManager.RenameFile(src, "b.png")

        Assert.True(File.Exists(result))
        Assert.Equal("b.png", Path.GetFileName(result))
    End Sub

    <Fact>
    Public Sub RenameFile_handles_a_name_with_dots()
        ' "my.holiday.2026.jpg" - GetExtension sees ".jpg", and appending it again would
        ' have produced "my.holiday.2026.jpg.jpg".
        Dim src As String = MakeFile("my.holiday.2026.jpg")

        Dim result As String = FileManager.RenameFile(src, "my.holiday.2027.jpg")

        Assert.Equal("my.holiday.2027.jpg", Path.GetFileName(result))
        Assert.True(File.Exists(result))
    End Sub

    <Fact>
    Public Sub RenameFile_to_the_same_name_is_a_no_op()
        Dim src As String = MakeFile("same.jpg")

        Dim result As String = FileManager.RenameFile(src, "same.jpg")

        Assert.Equal(src, result)
        Assert.True(File.Exists(src))
    End Sub

    <Fact>
    Public Sub RenameFile_can_change_only_the_case()
        ' Windows compares paths case-insensitively, but "photo" -> "Photo" is still a
        ' real rename the user asked for - the self-check must not swallow it.
        Dim src As String = MakeFile("case.jpg")

        Dim result As String = FileManager.RenameFile(src, "CASE.jpg")

        Assert.Equal("CASE.jpg", Path.GetFileName(result))
        Assert.Equal("CASE.jpg", Path.GetFileName(Directory.GetFiles(_dir)(0)))
    End Sub

End Class
