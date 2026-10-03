#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports Xunit

''' <summary>
''' Ф2 of 010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md - 7z/RAR, and the one sequential
''' pass that makes a SOLID archive browsable.
'''
''' The archives are real 7-Zip output (TestData\archives, see its README): SharpCompress
''' cannot write a 7z, and a hand-built stand-in would only prove that the test agrees with
''' itself. Every extracted file is checked against the SHA-256 recorded when the fixture was
''' made, so what is proven is WHAT came out, not that something did.
''' </summary>
Public Class ArchiveSequentialTests
    Implements IDisposable

    Private ReadOnly workDir As String
    Private ReadOnly sessionDir As String

    Private Shared ReadOnly Media As Func(Of String, Boolean) =
        Function(extension) extension = ".jpg" OrElse extension = ".png"

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-archive-seq-" & Guid.NewGuid().ToString("N"))
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

    Private Shared Function Fixture(name As String) As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "archives", name)
    End Function

    Private Function Open(name As String, Optional maxEntries As Integer = 0) As ArchiveSession
        Return New ArchiveSession(Fixture(name), sessionDir, Media, maxEntries)
    End Function

    ''' <summary>The manifest rows that apply to one fixture: normalized entry name -> (size, hash).</summary>
    Private Shared Function Expected(archiveName As String) As Dictionary(Of String, KeyValuePair(Of Long, String))
        Dim result As New Dictionary(Of String, KeyValuePair(Of Long, String))(StringComparer.Ordinal)
        For Each line As String In File.ReadAllLines(Fixture("manifest.txt"))
            Dim parts As String() = line.Split("|"c)
            If parts.Length <> 4 Then Continue For
            If Not parts(0).Split(","c).Contains(archiveName) Then Continue For
            result(parts(1)) = New KeyValuePair(Of Long, String)(Long.Parse(parts(2)), parts(3))
        Next
        Return result
    End Function

    Private Shared Function Normalized(entryName As String) As String
        Return entryName.Replace("\"c, "/"c)
    End Function

    Private Shared Function Sha256Of(path As String) As String
        Using stream As FileStream = File.OpenRead(path)
            Return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
        End Using
    End Function

    ''' <summary>Every listed entry is on disk with exactly the recorded bytes.</summary>
    Private Shared Sub AssertEntryIsIntact(session As ArchiveSession, index As Integer, archiveName As String)
        Dim info As ArchiveEntryInfo = session.Entries(index)
        Dim want = Expected(archiveName)(Normalized(info.EntryName))
        Assert.True(File.Exists(info.TempPath), info.EntryName & " is not on disk")
        Assert.Equal(want.Key, New FileInfo(info.TempPath).Length)
        Assert.Equal(want.Value, Sha256Of(info.TempPath))
    End Sub

    Private Function FilesOnDisk() As Integer
        Return Directory.GetFiles(sessionDir).Count(Function(f) Not f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
    End Function

    Private Function RunPass(session As ArchiveSession, Optional maxEntry As Long = ArchiveSession.Default_Max_Entry_Bytes,
                             Optional maxCache As Long = 0, Optional progress As List(Of Integer) = Nothing) As ArchiveSession.PassOutcome
        Return session.RunSequentialPass(maxEntry, maxCache, If(progress Is Nothing, Nothing, Sub(p As Integer) progress.Add(p)))
    End Function

    ' ------------------------------------------------------------ what it is ----

    <Fact>
    Public Sub A_solid_7z_is_sequential_and_lists_its_media_without_unpacking()
        Using session = Open("solid.7z")
            Assert.Equal(ArchiveSession.ArchiveKind.Sequential, session.Kind)
            Assert.True(session.NeedsSequentialPass)
            ' notes.txt is not media; the folders are not files.
            Assert.Equal({"ch1/p1.jpg", "ch1/p2.jpg", "ch2/p3.png"},
                         session.Entries.Select(Function(e) Normalized(e.EntryName)).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())
            Assert.Equal(0, FilesOnDisk())
        End Using
    End Sub

    <Fact>
    Public Sub A_non_solid_7z_stays_random_access_and_needs_no_pass()
        Using session = Open("nonsolid.7z")
            Assert.Equal(ArchiveSession.ArchiveKind.RandomAccess, session.Kind)
            Assert.False(session.NeedsSequentialPass)
            Assert.Equal(3, session.Entries.Count)

            ' The Ф1 behaviour, unchanged: only the entry asked for reaches the disk.
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(1, refusal))
            Assert.Equal(1, FilesOnDisk())
            AssertEntryIsIntact(session, 1, "nonsolid.7z")
        End Using
    End Sub

    <Fact>
    Public Sub A_zip_is_unaffected_and_random_access()
        Dim archivePath = Path.Combine(workDir, "plain.zip")
        Using zip = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create)
            Dim entry = zip.CreateEntry("page.jpg")
            Using target = entry.Open()
                target.Write(New Byte() {1, 2, 3, 4, 5, 6, 7, 8}, 0, 8)
            End Using
        End Using
        Using session As New ArchiveSession(archivePath, sessionDir, Media, 0)
            Assert.Equal(ArchiveSession.ArchiveKind.RandomAccess, session.Kind)
            Assert.False(session.NeedsSequentialPass)
        End Using
    End Sub

    <Theory>
    <InlineData("backup.7z", True)>
    <InlineData("scans.rar", True)>
    <InlineData("comic.cbr", True)>
    <InlineData("comic.cbz", True)>
    <InlineData("photos.zip", True)>
    <InlineData("photo.jpg", False)>
    <InlineData("archive.tar", False)>
    Public Sub The_openable_containers_are_zip_7z_and_rar(name As String, expected As Boolean)
        Assert.Equal(expected, ArchiveEntryFilter.IsArchivePath(name))
    End Sub

    ' --------------------------------------------------------- the one pass ----

    <Fact>
    Public Sub The_pass_stores_every_listed_entry_with_the_recorded_bytes_and_reports_progress()
        Using session = Open("solid.7z")
            Dim progress As New List(Of Integer)()
            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session, progress:=progress))

            Assert.False(session.NeedsSequentialPass)
            Assert.Equal(3, FilesOnDisk())   ' not notes.txt
            For i = 0 To session.Entries.Count - 1
                AssertEntryIsIntact(session, i, "solid.7z")
            Next

            Assert.NotEmpty(progress)
            Assert.Equal(100, progress.Last())
            Assert.Equal(progress.OrderBy(Function(p) p).ToList(), progress)   ' never goes backwards
        End Using
    End Sub

    <Fact>
    Public Sub After_the_pass_an_entry_is_served_from_disk_even_when_slow_work_is_not_allowed()
        Using session = Open("solid.7z")
            RunPass(session)
            Dim refusal As ArchiveSession.EntryRefusal
            For i = 0 To session.Entries.Count - 1
                Assert.True(session.TryEnsureExtracted(i, refusal, allowSlow:=False))
                Assert.Equal(ArchiveSession.EntryRefusal.None, refusal)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub A_guess_about_the_next_picture_never_starts_a_scan_of_a_solid_archive()
        Using session = Open("solid.7z")
            Dim refusal As ArchiveSession.EntryRefusal
            ' The prefetch asks like this before the pass: no refusal, nothing written.
            Assert.False(session.TryEnsureExtracted(0, refusal, allowSlow:=False))
            Assert.Equal(ArchiveSession.EntryRefusal.None, refusal)
            Assert.Equal(0, FilesOnDisk())
        End Using
    End Sub

    <Fact>
    Public Sub Asking_for_a_solid_entry_directly_reads_forward_to_it()
        Using session = Open("solid.7z")
            Dim refusal As ArchiveSession.EntryRefusal
            ' The display path, before any pass: slow but correct, and only that entry lands.
            Assert.True(session.TryEnsureExtracted(2, refusal))
            Assert.Equal(1, FilesOnDisk())
            AssertEntryIsIntact(session, 2, "solid.7z")
        End Using
    End Sub

    ' ----------------------------------------------------------------- limits ----

    <Fact>
    Public Sub The_pass_stops_storing_at_the_cache_budget_and_the_rest_comes_on_demand()
        Using session = Open("solid-many.7z")
            Assert.Equal(12, session.Entries.Count)
            Dim budget As Long = 24000L * 3 + 10

            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session, maxCache:=budget))
            Assert.Equal(3, FilesOnDisk())

            ' Pick an entry that did NOT make it. A guess does not fetch it; the display path does.
            Dim missing As Integer = Enumerable.Range(0, session.Entries.Count).First(Function(i) Not File.Exists(session.Entries(i).TempPath))
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.False(session.TryEnsureExtracted(missing, refusal, maxCacheBytes:=budget, allowSlow:=False))
            Assert.Equal(ArchiveSession.EntryRefusal.None, refusal)

            Assert.True(session.TryEnsureExtracted(missing, refusal, maxCacheBytes:=budget))
            AssertEntryIsIntact(session, missing, "solid-many.7z")
        End Using
    End Sub

    <Fact>
    Public Sub An_entry_over_the_size_limit_is_skipped_by_the_pass_and_refused_by_name()
        Using session = Open("solid.7z")
            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session, maxEntry:=5000))

            Dim stored As String() = session.Entries.Where(Function(e) File.Exists(e.TempPath)).Select(Function(e) Normalized(e.EntryName)).ToArray()
            Assert.Equal({"ch2/p3.png"}, stored)   ' 4000 B fits; 6000 B and 9000 B do not

            Dim big As Integer = session.Entries.ToList().FindIndex(Function(e) Normalized(e.EntryName) = "ch1/p1.jpg")
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.False(session.TryEnsureExtracted(big, refusal, maxEntryBytes:=5000))
            Assert.Equal(ArchiveSession.EntryRefusal.TooLarge, refusal)
        End Using
    End Sub

    <Fact>
    Public Sub The_entry_ceiling_applies_to_a_solid_archive_too()
        Using session = Open("solid-many.7z", maxEntries:=5)
            Assert.Equal(5, session.Entries.Count)
            Assert.True(session.WasTruncated)
            RunPass(session)
            Assert.Equal(5, FilesOnDisk())   ' the pass stores what is LISTED, not the whole archive
        End Using
    End Sub

    ' ------------------------------------------------------------ cancelling ----

    <Fact>
    Public Sub Closing_the_archive_during_the_pass_stops_it_and_writes_nothing_more()
        Dim session = Open("solid-many.7z")
        Dim outcome As ArchiveSession.PassOutcome
        Try
            Dim disposed As Boolean = False
            outcome = session.RunSequentialPass(ArchiveSession.Default_Max_Entry_Bytes, 0,
                Sub(percent As Integer)
                    If disposed Then Return
                    disposed = True
                    ' What LeaveArchive does: close the session, then remove the directory.
                    session.Dispose()
                    Directory.Delete(sessionDir, recursive:=True)
                End Sub)
        Finally
            session.Dispose()
        End Try

        Assert.Equal(ArchiveSession.PassOutcome.Cancelled, outcome)
        ' The directory the cleanup removed must not have been quietly recreated by a late write.
        Assert.False(Directory.Exists(sessionDir))
    End Sub

    <Fact>
    Public Sub A_pass_on_a_closed_session_is_cancelled()
        Dim session = Open("solid.7z")
        session.Dispose()
        Assert.Equal(ArchiveSession.PassOutcome.Cancelled, RunPass(session))
        Assert.Equal(0, FilesOnDisk())
    End Sub

    <Fact>
    Public Sub A_random_access_archive_has_nothing_to_pass()
        Using session = Open("nonsolid.7z")
            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session))
            Assert.Equal(0, FilesOnDisk())
        End Using
    End Sub

    ' --------------------------------------------------------------------- RAR ----

    ' No tool on a build machine can be assumed to WRITE a RAR (the format is closed), so these
    ' rows build the smallest legal RAR 4 by hand: stored entries, which need no compressor,
    ' with the main header's "solid" flag set or clear. That exercises the parts of the feature
    ' that are ours - the solid decision, the sweep over a RAR reader, the matching of entries -
    ' not SharpCompress's RAR decompressor, which is not ours to test.

    Private Shared ReadOnly Crc_Table As UInteger() = BuildCrcTable()

    Private Shared Function BuildCrcTable() As UInteger()
        Dim table(255) As UInteger
        For n = 0 To 255
            Dim c As UInteger = CUInt(n)
            For k = 0 To 7
                c = If((c And 1UI) <> 0UI, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
            table(n) = c
        Next
        Return table
    End Function

    Private Shared Function Crc32(data As Byte(), offset As Integer, count As Integer) As UInteger
        Dim crc As UInteger = &HFFFFFFFFUI
        For i = offset To offset + count - 1
            crc = Crc_Table(CInt((crc Xor data(i)) And &HFFUI)) Xor (crc >> 8)
        Next
        Return crc Xor &HFFFFFFFFUI
    End Function

    Private Shared Sub PutUInt16(target As List(Of Byte), value As Integer)
        target.Add(CByte(value And &HFF))
        target.Add(CByte((value >> 8) And &HFF))
    End Sub

    Private Shared Sub PutUInt32(target As List(Of Byte), value As UInteger)
        For shift = 0 To 24 Step 8
            target.Add(CByte((value >> shift) And &HFFUI))
        Next
    End Sub

    ''' <summary>A header is its 2-byte CRC (the low half of the CRC-32 of everything after it)
    ''' followed by the rest.</summary>
    Private Shared Sub AppendHeader(archive As List(Of Byte), body As List(Of Byte))
        Dim bytes As Byte() = body.ToArray()
        PutUInt16(archive, CInt(Crc32(bytes, 0, bytes.Length) And &HFFFFUI))
        archive.AddRange(bytes)
    End Sub

    Private Shared Function BuildRar4(solid As Boolean, ParamArray files As KeyValuePair(Of String, Byte())()) As Byte()
        Dim archive As New List(Of Byte)()
        archive.AddRange(New Byte() {&H52, &H61, &H72, &H21, &H1A, &H7, &H0})   ' "Rar!" 1A 07 00

        Dim main As New List(Of Byte)()
        main.Add(&H73)                              ' archive header
        PutUInt16(main, If(solid, &H8, &H0))        ' MHD_SOLID
        PutUInt16(main, 13)                         ' header size
        PutUInt16(main, 0)
        PutUInt32(main, 0UI)
        AppendHeader(archive, main)

        For i = 0 To files.Length - 1
            Dim name As Byte() = Text.Encoding.ASCII.GetBytes(files(i).Key)
            Dim data As Byte() = files(i).Value

            Dim header As New List(Of Byte)()
            header.Add(&H74)                        ' file header
            PutUInt16(header, &H8000 Or If(solid AndAlso i > 0, &H10, 0))   ' has data; LHD_SOLID
            PutUInt16(header, 32 + name.Length)
            PutUInt32(header, CUInt(data.Length))   ' packed size (stored: same)
            PutUInt32(header, CUInt(data.Length))   ' unpacked size
            header.Add(2)                           ' host OS: Windows
            PutUInt32(header, Crc32(data, 0, data.Length))
            PutUInt32(header, &H5A216000UI)         ' DOS time
            header.Add(20)                          ' unpack version
            header.Add(&H30)                        ' method: store
            PutUInt16(header, name.Length)
            PutUInt32(header, &H20UI)               ' attributes
            header.AddRange(name)
            AppendHeader(archive, header)
            archive.AddRange(data)
        Next

        archive.AddRange(New Byte() {&HC4, &H3D, &H7B, &H0, &H40, &H7, &H0})   ' end of archive
        Return archive.ToArray()
    End Function

    Private Shared Function Pseudo(size As Integer, seed As Integer) As Byte()
        Dim bytes(size - 1) As Byte
        Dim generator As New Random(seed)
        generator.NextBytes(bytes)
        Return bytes
    End Function

    Private Function WriteRar(name As String, solid As Boolean, ParamArray files As KeyValuePair(Of String, Byte())()) As String
        Dim path As String = System.IO.Path.Combine(workDir, name)
        File.WriteAllBytes(path, BuildRar4(solid, files))
        Return path
    End Function

    <Fact>
    Public Sub A_solid_rar_is_swept_once_and_every_page_comes_out_intact()
        Dim p1 = Pseudo(3000, 11), p2 = Pseudo(5000, 12), p3 = Pseudo(1500, 13)
        Dim path = WriteRar("solid.cbr", solid:=True,
            New KeyValuePair(Of String, Byte())("p1.jpg", p1),
            New KeyValuePair(Of String, Byte())("p2.jpg", p2),
            New KeyValuePair(Of String, Byte())("readme.txt", Pseudo(100, 14)),
            New KeyValuePair(Of String, Byte())("p3.png", p3))

        Using session As New ArchiveSession(path, sessionDir, Media, 0)
            Assert.Equal(ArchiveSession.ArchiveKind.Sequential, session.Kind)
            Assert.True(session.NeedsSequentialPass)
            Assert.Equal(3, session.Entries.Count)
            Assert.Equal(0, FilesOnDisk())

            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session))
            Assert.Equal(3, FilesOnDisk())

            Dim want As New Dictionary(Of String, Byte())(StringComparer.Ordinal) From {{"p1.jpg", p1}, {"p2.jpg", p2}, {"p3.png", p3}}
            For Each info In session.Entries
                Assert.Equal(want(info.EntryName), File.ReadAllBytes(info.TempPath))
            Next
        End Using
    End Sub

    <Fact>
    Public Sub A_non_solid_rar_is_random_access()
        Dim p1 = Pseudo(2000, 21), p2 = Pseudo(2500, 22)
        Dim path = WriteRar("plain.rar", solid:=False,
            New KeyValuePair(Of String, Byte())("p1.jpg", p1),
            New KeyValuePair(Of String, Byte())("p2.jpg", p2))

        Using session As New ArchiveSession(path, sessionDir, Media, 0)
            Assert.Equal(ArchiveSession.ArchiveKind.RandomAccess, session.Kind)
            Assert.False(session.NeedsSequentialPass)

            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(1, refusal))
            Assert.Equal(1, FilesOnDisk())
            Assert.Equal(p2, File.ReadAllBytes(session.Entries(1).TempPath))
        End Using
    End Sub

    <Fact>
    Public Sub A_rar_in_a_cbz_name_is_read_for_what_it_is()
        ' The reverse of the old rule (audit 052 G-15): the content decides, not the name.
        Dim path = WriteRar("really-a-rar.cbz", solid:=False,
            New KeyValuePair(Of String, Byte())("p1.jpg", Pseudo(1000, 31)))
        Using session As New ArchiveSession(path, sessionDir, Media, 0)
            Assert.Equal("", session.UnsupportedKind)
            Assert.Single(session.Entries)
        End Using
    End Sub

    <Fact>
    Public Sub Duplicate_names_in_a_solid_rar_keep_their_own_bytes_after_the_pass_and_refetch()
        Dim first = Pseudo(3000, 51), second = Pseudo(5000, 52)
        Dim path = WriteRar("duplicates.cbr", solid:=True,
            New KeyValuePair(Of String, Byte())("page.jpg", first),
            New KeyValuePair(Of String, Byte())("page.jpg", second))
        Using session As New ArchiveSession(path, sessionDir, Media, 0)
            Assert.Equal(2, session.Entries.Count)
            Assert.Equal(ArchiveSession.PassOutcome.Completed, RunPass(session))
            Assert.Equal(first, File.ReadAllBytes(session.Entries(0).TempPath))
            Assert.Equal(second, File.ReadAllBytes(session.Entries(1).TempPath))
            File.Delete(session.Entries(1).TempPath)
            Dim refusal As ArchiveSession.EntryRefusal
            Assert.True(session.TryEnsureExtracted(1, refusal))
            Assert.Equal(second, File.ReadAllBytes(session.Entries(1).TempPath))
        End Using
    End Sub

    ' --------------------------------------------------------------- passwords ----

    <Fact>
    Public Sub A_7z_with_encrypted_headers_is_the_password_case_not_a_crash()
        Using session = Open("encrypted-headers.7z")
            Assert.True(session.IsEncrypted)
            Assert.Empty(session.Entries)
        End Using
    End Sub

    <Fact>
    Public Sub A_7z_with_encrypted_data_is_the_password_case()
        Using session = Open("encrypted-data.7z")
            Assert.True(session.IsEncrypted)
            Assert.Empty(session.Entries)
        End Using
    End Sub

End Class
#End If
