#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' Ф0 of SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md - the archive cache's names and its
''' cleanup, before any archive is ever opened.
'''
''' Two things here are worth proving by machine rather than by eye:
'''
'''   * <b>The destination name</b>. Invariant 2 says nothing can be written outside the
'''     session directory, and the way that is achieved is that the name is BUILT by us
'''     from the entry's last segment - so an entry called "..\..\Windows\System32\x.dll"
'''     cannot address anything. A filter can be fooled; this cannot, and the test says so
'''     in the form of the actual name that comes out.
'''   * <b>The sweep's verdict</b>. It is wrong in two expensive directions: delete a live
'''     session and the viewer loses the archive on screen; keep an orphan and the cache
'''     grows for ever on a machine that once crashed.
'''
''' Modern-only, like the feature: on the net48 leg the module and this file compile to
''' nothing.
''' </summary>
Public Class ArchiveCacheTests
    Implements IDisposable

    Private ReadOnly workDir As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-archive-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------- the entry's name ----

    <Fact>
    Public Sub An_entry_name_becomes_an_indexed_flat_file_name()
        Assert.Equal("00042_page12.jpg", ArchivePaths.EntryFileName(42, "scans/1998/page12.jpg"))
    End Sub

    ''' <summary>
    ''' The zip-slip case, spelled out. What matters is not that the name is "cleaned" but
    ''' that the directory part never reaches the result at all: combine the answer with
    ''' any root and it stays under that root.
    ''' </summary>
    <Theory>
    <InlineData("..\..\Windows\System32\evil.dll", "00000_evil.dll")>
    <InlineData("../../etc/passwd", "00000_passwd")>
    <InlineData("C:\Windows\win.ini", "00000_win.ini")>
    <InlineData("\\server\share\x.jpg", "00000_x.jpg")>
    <InlineData("..", "00000_entry")>
    <InlineData("nested/dir/", "00000_entry")>
    Public Sub A_traversing_entry_name_cannot_address_anything_outside(entryName As String, expected As String)
        Dim name = ArchivePaths.EntryFileName(0, entryName)

        Assert.Equal(expected, name)

        ' And the property that actually matters, asserted rather than assumed (§6.1).
        Dim root = Path.Combine(workDir, "session")
        Dim full = Path.GetFullPath(Path.Combine(root, name))
        Assert.StartsWith(Path.GetFullPath(root) & Path.DirectorySeparatorChar, full, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub Characters_windows_forbids_are_replaced_rather_than_failing_the_extraction()
        Dim name = ArchivePaths.EntryFileName(7, "a:b*c?""d<e>f|g.jpg")

        Assert.Equal("00007_a_b_c__d_e_f_g.jpg", name)
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()))
    End Sub

    <Fact>
    Public Sub A_very_long_name_is_trimmed_but_keeps_its_extension()
        Dim stem = New String("z"c, 400)
        Dim name = ArchivePaths.EntryFileName(3, stem & ".png")

        Assert.EndsWith(".png", name, StringComparison.Ordinal)
        Assert.Equal("00003_" & New String("z"c, ArchivePaths.Max_Stem_Length) & ".png", name)
    End Sub

    ''' <summary>
    ''' Windows drops trailing dots and spaces from a file name silently, so a name that
    ''' kept them would not be the name on disk - and the extractor compares the file it
    ''' believes it wrote.
    ''' </summary>
    <Fact>
    Public Sub Trailing_dots_and_spaces_do_not_survive()
        Assert.Equal("00001_photo.jpg", ArchivePaths.EntryFileName(1, "photo.jpg. "))
        Assert.Equal("00001_photo.jpg", ArchivePaths.EntryFileName(1, "  photo.jpg  "))
    End Sub

    ''' <summary>
    ''' The index prefix is not decoration: it keeps two entries with the same name in
    ''' different archive folders apart (the folder structure is deliberately not
    ''' recreated), and it also means a name can never come out as a reserved device name.
    ''' </summary>
    <Fact>
    Public Sub The_same_name_in_two_archive_folders_gets_two_files()
        Dim first = ArchivePaths.EntryFileName(4, "chapter1/cover.jpg")
        Dim second = ArchivePaths.EntryFileName(9, "chapter2/cover.jpg")

        Assert.NotEqual(first, second)
    End Sub

    <Theory>
    <InlineData("CON")>
    <InlineData("NUL.jpg")>
    <InlineData("LPT1.png")>
    Public Sub A_reserved_device_name_is_defused_by_the_index_prefix(entryName As String)
        Dim name = ArchivePaths.EntryFileName(11, entryName)

        Assert.StartsWith("00011_", name, StringComparison.Ordinal)
        Assert.True(Char.IsDigit(name(0)), "A reserved name only bites when it is the whole stem.")
    End Sub

    ' --------------------------------------------------- the session directory ----

    <Fact>
    Public Sub A_session_directory_name_carries_the_owning_process()
        Dim name = ArchivePaths.SessionDirName(4242, "deadbeef")
        Dim owner As Integer

        Assert.True(ArchivePaths.TryParseSessionPid(name, owner))
        Assert.Equal(4242, owner)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("tessdata")>
    <InlineData("-deadbeef")>
    <InlineData("4242-")>
    <InlineData("abc-deadbeef")>
    <InlineData("0-deadbeef")>
    <InlineData("-5-deadbeef")>
    Public Sub A_directory_that_is_not_ours_is_not_recognised(dirName As String)
        Dim owner As Integer

        Assert.False(ArchivePaths.TryParseSessionPid(dirName, owner))
    End Sub

    ' ------------------------------------------------------------- the verdict ----

    <Fact>
    Public Sub A_live_owners_session_is_kept()
        Assert.False(ArchiveTempStore.ShouldRemoveSession("4242-deadbeef", ageHours:=0.5, ownerIsLive:=True))
    End Sub

    <Fact>
    Public Sub A_dead_owners_session_is_removed()
        Assert.True(ArchiveTempStore.ShouldRemoveSession("4242-deadbeef", ageHours:=0.5, ownerIsLive:=False))
    End Sub

    ''' <summary>
    ''' Windows reuses process ids, so on a long-running machine a stale directory can be
    ''' "owned" by an unrelated program that inherited the number. Age wins over the pid.
    ''' </summary>
    <Fact>
    Public Sub An_old_session_goes_even_if_something_claims_its_pid()
        Assert.True(ArchiveTempStore.ShouldRemoveSession("4242-deadbeef",
                                                         ageHours:=ArchiveTempStore.Stale_After_Hours + 1,
                                                         ownerIsLive:=True))
    End Sub

    <Fact>
    Public Sub A_foreign_directory_in_the_cache_root_is_left_alone()
        Assert.False(ArchiveTempStore.ShouldRemoveSession("something-else", ageHours:=999, ownerIsLive:=False))
    End Sub

    ' ---------------------------------------------------------------- the sweep ----

    ''' <summary>
    ''' The real thing against real directories: the crash case (a session whose owner is
    ''' gone) is removed, this process' own session survives, and a directory that is not
    ''' ours is not touched. No process has to be killed to prove it - a pid that cannot
    ''' exist is the same input a killed one leaves behind.
    ''' </summary>
    <Fact>
    Public Sub The_sweep_removes_orphans_and_keeps_what_is_alive()
        Dim root = Path.Combine(workDir, "archive-cache")
        Dim mine = Path.Combine(root, ArchivePaths.SessionDirName(Diagnostics.Process.GetCurrentProcess().Id, "aaaaaaaa"))
        Dim orphan = Path.Combine(root, ArchivePaths.SessionDirName(&H7FFFFFFE, "bbbbbbbb"))
        Dim foreign = Path.Combine(root, "tessdata")
        ' Not "dir" - VB's own Dir() function shadows it and the loop stops compiling.
        For Each folder In New String() {mine, orphan, foreign}
            Directory.CreateDirectory(folder)
        Next
        ' An orphan is never empty in practice - the extracted entries are what makes it
        ' worth sweeping at all.
        File.WriteAllText(Path.Combine(orphan, "00000_page.jpg"), "not really a jpeg")

        ArchiveTempStore.SweepRoot(root, Date.UtcNow)

        Assert.True(Directory.Exists(mine), "The running viewer's own session was swept.")
        Assert.False(Directory.Exists(orphan), "A session whose owner is gone survived the sweep.")
        Assert.True(Directory.Exists(foreign), "The sweep deleted a directory that is not an archive session.")
    End Sub

    <Fact>
    Public Sub The_sweep_is_silent_about_a_cache_that_was_never_created()
        Dim missing = Path.Combine(workDir, "never-existed")

        ArchiveTempStore.SweepRoot(missing, Date.UtcNow)

        Assert.False(Directory.Exists(missing), "The sweep must not create the root it came to clean.")
    End Sub

    <Fact>
    Public Sub Deleting_a_session_takes_its_contents_with_it()
        Dim session = Path.Combine(workDir, "9999-cccccccc")
        Directory.CreateDirectory(Path.Combine(session, "sub"))
        File.WriteAllText(Path.Combine(session, "00000_a.jpg"), "x")
        File.WriteAllText(Path.Combine(session, "sub", "00001_b.jpg"), "y")

        Assert.True(ArchiveTempStore.DeleteSession(session))
        Assert.False(Directory.Exists(session))
    End Sub

    <Fact>
    Public Sub Deleting_a_session_that_is_already_gone_is_not_a_failure()
        Assert.True(ArchiveTempStore.DeleteSession(Path.Combine(workDir, "not-there")))
        Assert.True(ArchiveTempStore.DeleteSession(""))
    End Sub

End Class
#End If
