#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' Ф1 of 010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md - an archive read as a folder.
'''
''' The archives here are built by the test with the framework's own ZIP writer and read
''' back through SharpCompress, which is the shipping path: what is proven is the contract
''' the viewer depends on - which entries appear, where their files land, and what happens
''' when an entry is hostile (a traversing name, a bomb, something enormous) or the archive
''' is simply broken.
'''
''' Modern-only: on the net48 leg the session, the filter and this file compile to nothing.
''' </summary>
Public Class ArchiveSessionTests
    Implements IDisposable

    Private ReadOnly workDir As String
    Private ReadOnly sessionDir As String

    ''' <summary>The viewer's own question, stubbed down to what these tests care about.
    ''' The real one is the app's extension set, user narrowing included - which is exactly
    ''' why the session asks rather than deciding.</summary>
    Private Shared ReadOnly Media As Func(Of String, Boolean) =
        Function(extension) extension = ".jpg" OrElse extension = ".png" OrElse extension = ".mp4"

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-archive-session-" & Guid.NewGuid().ToString("N"))
        sessionDir = Path.Combine(workDir, "session")
        Directory.CreateDirectory(sessionDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    ' ----------------------------------------------------------------- helpers ----

    Private Function MakeArchive(name As String, fill As Action(Of ZipArchive)) As String
        Dim archivePath = Path.Combine(workDir, name)
        Using zip As ZipArchive = ZipFile.Open(archivePath, ZipArchiveMode.Create)
            fill(zip)
        End Using
        Return archivePath
    End Function

    Private Shared Sub AddEntry(zip As ZipArchive, entryName As String, content As Byte())
        Dim entry As ZipArchiveEntry = zip.CreateEntry(entryName, CompressionLevel.Optimal)
        Using target As Stream = entry.Open()
            target.Write(content, 0, content.Length)
        End Using
    End Sub

    Private Shared Sub AddText(zip As ZipArchive, entryName As String, content As String)
        AddEntry(zip, entryName, Encoding.UTF8.GetBytes(content))
    End Sub

    Private Function OpenSession(archivePath As String, Optional maxEntries As Integer = 0) As ArchiveSession
        Return New ArchiveSession(archivePath, sessionDir, Media, maxEntries)
    End Function

    ''' <summary>Content that will not compress away to almost nothing - all-zero filler
    ''' would itself trip the bomb ratio check (§6.2) that the eviction tests below have no
    ''' interest in exercising.</summary>
    Private Shared Function Incompressible(size As Integer, seed As Integer) As Byte()
        Dim buffer(size - 1) As Byte
        For j = 0 To size - 1
            buffer(j) = CByte((j * 37 + seed * 11) Mod 256)
        Next
        Return buffer
    End Function

    ' ------------------------------------------------------------- the entry list ----

    ''' <summary>
    ''' What the list is made of: media entries only, in archive order, with folders,
    ''' non-media, junk, empty entries and nested archives all gone. Every one of those is
    ''' something a real CBZ or a Mac-made ZIP contains.
    ''' </summary>
    <Fact>
    Public Sub Only_media_entries_become_files()
        Dim archivePath = MakeArchive("mixed.cbz",
            Sub(zip)
                AddText(zip, "chapter1/page01.jpg", "one")
                AddText(zip, "chapter1/page02.png", "two")
                AddText(zip, "chapter1/notes.txt", "not media")
                AddText(zip, "__MACOSX/chapter1/._page01.jpg", "resource fork")
                AddText(zip, "chapter1/Thumbs.db", "explorer")
                AddText(zip, "chapter1/inner.zip", "a nested archive")
                AddText(zip, "chapter1/empty.jpg", "")
                AddText(zip, "clip.mp4", "video")
            End Sub)

        Using session = OpenSession(archivePath)
            Assert.Equal({"chapter1/page01.jpg", "chapter1/page02.png", "clip.mp4"},
                         session.Entries.Select(Function(e) e.EntryName).ToArray())
        End Using
    End Sub

    ''' <summary>
    ''' Invariant 10: the list comes from archive metadata, never from a walk of the
    ''' temporary directory - which holds only what has been looked at, so a walk would
    ''' give a count that grows as the user browses.
    ''' </summary>
    <Fact>
    Public Sub The_list_exists_before_anything_is_extracted()
        Dim archivePath = MakeArchive("lazy.zip",
            Sub(zip)
                For i = 1 To 5
                    AddText(zip, "p" & i.ToString() & ".jpg", "content " & i.ToString())
                Next
            End Sub)

        Using session = OpenSession(archivePath)
            Assert.Equal(5, session.Entries.Count)
            Assert.Empty(Directory.GetFiles(sessionDir))
        End Using
    End Sub

    <Fact>
    Public Sub Every_entry_gets_its_own_path_inside_the_session_directory()
        Dim archivePath = MakeArchive("dupes.zip",
            Sub(zip)
                AddText(zip, "vol1/cover.jpg", "first")
                AddText(zip, "vol2/cover.jpg", "second")
            End Sub)

        Using session = OpenSession(archivePath)
            Dim paths = session.Entries.Select(Function(e) e.TempPath).ToArray()

            Assert.Equal(2, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            ' Not "path" as the loop variable - VB is case-insensitive and it would shadow
            ' System.IO.Path for the rest of the block.
            For Each candidate In paths
                Assert.StartsWith(Path.GetFullPath(sessionDir) & Path.DirectorySeparatorChar,
                                  Path.GetFullPath(candidate), StringComparison.Ordinal)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub The_entry_list_can_be_capped()
        Dim archivePath = MakeArchive("many.zip",
            Sub(zip)
                For i = 1 To 10
                    AddText(zip, "p" & i.ToString("00") & ".jpg", "x")
                Next
            End Sub)

        Using session = OpenSession(archivePath, maxEntries:=4)
            Assert.Equal(4, session.Entries.Count)
            Assert.True(session.WasTruncated, "A cut list must say so - a silent one looks like a small archive.")
        End Using
    End Sub

    ' -------------------------------------------------------------- extraction ----

    <Fact>
    Public Sub An_entry_reaches_the_disk_only_when_it_is_asked_for()
        Dim archivePath = MakeArchive("two.zip",
            Sub(zip)
                AddText(zip, "a.jpg", "content of a")
                AddText(zip, "b.jpg", "content of b")
            End Sub)

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal

            Assert.True(session.TryEnsureExtracted(0, refusal))
            Assert.Equal(ArchiveSession.EntryRefusal.None, refusal)
            Assert.Equal("content of a", File.ReadAllText(session.Entries(0).TempPath))
            Assert.False(File.Exists(session.Entries(1).TempPath), "The other entry was extracted too.")
        End Using
    End Sub

    <Fact>
    Public Sub Asking_twice_costs_nothing_and_leaves_the_file_alone()
        Dim archivePath = MakeArchive("again.zip", Sub(zip) AddText(zip, "a.jpg", "stable"))

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(0, refusal))
            Dim writtenAt = File.GetLastWriteTimeUtc(session.Entries(0).TempPath)

            Assert.True(session.TryEnsureExtracted(0, refusal))

            Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(session.Entries(0).TempPath))
            Assert.Equal("stable", File.ReadAllText(session.Entries(0).TempPath))
        End Using
    End Sub

    ''' <summary>
    ''' No ".part" file may be left behind: the file list holds a path per entry, and a
    ''' stray neighbour is both clutter in the session directory and a file the size check
    ''' would have to reason about.
    ''' </summary>
    <Fact>
    Public Sub Extraction_leaves_no_partial_file_beside_the_result()
        Dim archivePath = MakeArchive("clean.zip", Sub(zip) AddText(zip, "a.jpg", "done"))

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(0, refusal))

            Assert.Empty(Directory.GetFiles(sessionDir, "*.part"))
        End Using
    End Sub

    <Fact>
    Public Sub A_temporary_path_maps_back_to_its_entry()
        Dim archivePath = MakeArchive("map.zip",
            Sub(zip)
                AddText(zip, "one.jpg", "1")
                AddText(zip, "two.jpg", "2")
            End Sub)

        Using session = OpenSession(archivePath)
            Assert.Equal(1, session.IndexOfTempPath(session.Entries(1).TempPath))
            Assert.Equal(-1, session.IndexOfTempPath(Path.Combine(sessionDir, "not-an-entry.jpg")))
            Assert.Equal(-1, session.IndexOfTempPath(""))
        End Using
    End Sub

    ' ---------------------------------------------------------------- eviction ----

    ''' <summary>
    ''' §5.4: once the session directory would grow past its budget, the least-recently
    ''' touched entries go first - proven by walking forward through an archive whose
    ''' entries do not all fit at once, and finding the early ones gone while the one just
    ''' shown remains.
    ''' </summary>
    <Fact>
    Public Sub Eviction_keeps_the_directory_under_budget_and_drops_the_oldest_first()
        Dim entrySize = 1000
        Dim archivePath = MakeArchive("many-heavy.zip",
            Sub(zip)
                For i = 0 To 4
                    AddEntry(zip, "p" & i.ToString("00") & ".jpg", Incompressible(entrySize, i))
                Next
            End Sub)

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            ' Comfortably holds two entries, not all five - walking forward has to evict.
            Dim budget As Long = entrySize * 2L + 100L

            For i = 0 To 4
                Assert.True(session.TryEnsureExtracted(i, refusal, maxCacheBytes:=budget))
            Next

            Assert.False(File.Exists(session.Entries(0).TempPath), "The first entry should have been evicted long ago.")
            Assert.True(File.Exists(session.Entries(4).TempPath), "The entry just shown must never be evicted.")
        End Using
    End Sub

    ''' <summary>
    ''' The entry just asked for and its immediate neighbours are never evicted, even when
    ''' the budget alone would call for it - they are what the UI or the prefetch is about
    ''' to want next.
    ''' </summary>
    <Fact>
    Public Sub Eviction_never_touches_the_current_entry_or_its_neighbours()
        Dim entrySize = 1000
        Dim archivePath = MakeArchive("triplet.zip",
            Sub(zip)
                For i = 0 To 2
                    AddEntry(zip, "p" & i.ToString("00") & ".jpg", Incompressible(entrySize, i))
                Next
            End Sub)

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            ' All three on disk first, budget off - otherwise "still there" would just mean
            ' "never asked for".
            For i = 0 To 2
                Assert.True(session.TryEnsureExtracted(i, refusal, maxCacheBytes:=0))
            Next

            ' Touch the middle entry again with a budget smaller than even one entry -
            ' eviction has nowhere to go, because the touched entry and both its
            ' neighbours (§5.4) are the whole archive.
            Assert.True(session.TryEnsureExtracted(1, refusal, maxCacheBytes:=1L))

            For i = 0 To 2
                Assert.True(File.Exists(session.Entries(i).TempPath),
                           "Entry " & i.ToString() & " is the touched entry or its neighbour and must survive.")
            Next
        End Using
    End Sub

    <Fact>
    Public Sub A_zero_or_negative_budget_means_eviction_is_off()
        Dim entrySize = 1000
        Dim archivePath = MakeArchive("unbounded.zip",
            Sub(zip)
                For i = 0 To 4
                    AddEntry(zip, "p" & i.ToString("00") & ".jpg", Incompressible(entrySize, i))
                Next
            End Sub)

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            For i = 0 To 4
                Assert.True(session.TryEnsureExtracted(i, refusal, maxCacheBytes:=0))
            Next

            For i = 0 To 4
                Assert.True(File.Exists(session.Entries(i).TempPath),
                           "Entry " & i.ToString() & " should still be on disk with eviction off.")
            Next
        End Using
    End Sub

    ' ---------------------------------------------------------------- hostile ----

    ''' <summary>
    ''' The zip-slip case end to end: an entry that names its way out of the directory is
    ''' extracted, and the file lands inside the session directory under a name we chose.
    ''' Nothing appears outside it - which is the assertion that actually matters, and the
    ''' reason it is made against the file system rather than against a string.
    ''' </summary>
    <Fact>
    Public Sub A_traversing_entry_lands_inside_the_session_directory()
        Dim outside = Path.Combine(workDir, "outside")
        Directory.CreateDirectory(outside)
        Dim archivePath = MakeArchive("slip.zip",
            Sub(zip) AddText(zip, "../../outside/evil.jpg", "payload"))

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(0, refusal))

            Dim written = session.Entries(0).TempPath
            Assert.StartsWith(Path.GetFullPath(sessionDir) & Path.DirectorySeparatorChar,
                              Path.GetFullPath(written), StringComparison.Ordinal)
            Assert.Empty(Directory.GetFiles(outside))
        End Using
    End Sub

    ''' <summary>
    ''' The bomb is refused from the header, before a byte is written - which is the whole
    ''' point: a ratio check made while writing has already cost the disk.
    ''' </summary>
    <Fact>
    Public Sub A_bomb_is_refused_before_anything_is_written()
        Dim archivePath = MakeArchive("bomb.zip",
            Sub(zip) AddEntry(zip, "boom.jpg", New Byte(8 * 1024 * 1024 - 1) {}))

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal

            Assert.False(session.TryEnsureExtracted(0, refusal))
            Assert.Equal(ArchiveSession.EntryRefusal.Bomb, refusal)
            Assert.Empty(Directory.GetFiles(sessionDir))
        End Using
    End Sub

    <Fact>
    Public Sub An_entry_over_the_ceiling_is_refused_by_name_rather_than_silently_skipped()
        Dim archivePath = MakeArchive("big.zip",
            Sub(zip) AddEntry(zip, "huge.jpg", Encoding.UTF8.GetBytes(New String("q"c, 4096))))

        Using session = OpenSession(archivePath)
            Dim refusal As ArchiveSession.EntryRefusal

            Assert.False(session.TryEnsureExtracted(0, refusal, maxEntryBytes:=1024))
            Assert.Equal(ArchiveSession.EntryRefusal.TooLarge, refusal)
            ' Refused for viewing, still listed: the user must see that the archive has it.
            Assert.Single(session.Entries)
        End Using
    End Sub

    ''' <summary>
    ''' A truncated archive must fail at the door, with an exception the caller turns into
    ''' one message and a log line (§6.5) - not with an empty folder that looks like a
    ''' perfectly good archive containing nothing.
    ''' </summary>
    <Fact>
    Public Sub A_broken_archive_fails_to_open_rather_than_looking_empty()
        Dim archivePath = MakeArchive("truncated.zip",
            Sub(zip)
                AddText(zip, "a.jpg", New String("z"c, 5000))
                AddText(zip, "b.jpg", New String("y"c, 5000))
            End Sub)
        Dim whole = File.ReadAllBytes(archivePath)
        File.WriteAllBytes(archivePath, whole.Take(whole.Length \ 2).ToArray())

        Assert.ThrowsAny(Of Exception)(Sub()
                                           Using OpenSession(archivePath)
                                           End Using
                                       End Sub)
    End Sub

    <Fact>
    Public Sub An_archive_with_no_media_opens_and_is_simply_empty()
        Dim archivePath = MakeArchive("docs.zip",
            Sub(zip)
                AddText(zip, "readme.txt", "hello")
                AddText(zip, "notes.md", "world")
            End Sub)

        Using session = OpenSession(archivePath)
            Assert.Empty(session.Entries)
            Assert.False(session.IsEncrypted)
        End Using
    End Sub

    ' ------------------------------------------------------------ what it is ----

    <Theory>
    <InlineData("comic.cbz", True)>
    <InlineData("photos.zip", True)>
    <InlineData("PHOTOS.ZIP", True)>
    <InlineData("backup.7z", False)>
    <InlineData("scans.rar", False)>
    <InlineData("photo.jpg", False)>
    <InlineData("", False)>
    Public Sub Only_the_containers_phase_one_can_read_are_offered(name As String, expected As Boolean)
        ' 7z/RAR are in Archive_Extensions (so they are never listed as entries) but not yet
        ' openable: they need the sequential path, which is phase Ф2.
        Assert.Equal(expected, ArchiveEntryFilter.IsArchivePath(name))
    End Sub

End Class
#End If
