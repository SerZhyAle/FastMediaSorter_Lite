#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports Xunit

''' <summary>
''' The archive rows of audit 052 (docs/specifications/052_SPECIFICATION_AUDIT_ARCHIVE_SECURE_EDITOR.md
''' §5): a RAR or 7z in a .cbz name (G-15), names in the OEM code page (G-16), a late
''' prefetch after the archive was closed (G-21), what the prefetch extracts ahead (G-14),
''' and the archive cache test the recents and the persistent caches now ask (A-05, G-21).
''' </summary>
Public Class ArchiveAuditTests
    Implements IDisposable

    Private ReadOnly workDir As String
    Private ReadOnly sessionDir As String

    Private Shared ReadOnly Media As Func(Of String, Boolean) =
        Function(extension) extension = ".jpg" OrElse extension = ".png"

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-archive-audit-" & Guid.NewGuid().ToString("N"))
        sessionDir = Path.Combine(workDir, "session")
        Directory.CreateDirectory(sessionDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    Private Function MakeZip(name As String, entryNameEncoding As Encoding, ParamArray entries As String()) As String
        Dim archivePath = Path.Combine(workDir, name)
        Using zip As ZipArchive = If(entryNameEncoding Is Nothing,
                                     ZipFile.Open(archivePath, ZipArchiveMode.Create),
                                     ZipFile.Open(archivePath, ZipArchiveMode.Create, entryNameEncoding))
            For Each entryName As String In entries
                Dim entry As ZipArchiveEntry = zip.CreateEntry(entryName, CompressionLevel.NoCompression)
                Using target As Stream = entry.Open()
                    Dim content As Byte() = Encoding.UTF8.GetBytes("content of " & entryName)
                    target.Write(content, 0, content.Length)
                End Using
            Next
        End Using
        Return archivePath
    End Function

    ' --------------------------------------------------------------- G-15 ----

    ''' <summary>An archive that is not a ZIP inside is refused by name however it is
    ''' called, and exposes no entries. TAR stands in for RAR/7z here: it is what the
    ''' framework can write, and the rule is "ZIP or nothing", not a list of formats.</summary>
    <Fact>
    Public Sub A_non_zip_archive_in_a_cbz_name_is_refused_by_name()
        Dim source As String = Path.Combine(workDir, "pages")
        Directory.CreateDirectory(source)
        File.WriteAllText(Path.Combine(source, "page1.jpg"), "not really a jpeg")
        Dim archivePath As String = Path.Combine(workDir, "comic.cbz")
        System.Formats.Tar.TarFile.CreateFromDirectory(source, archivePath, includeBaseDirectory:=False)

        Using session As New ArchiveSession(archivePath, sessionDir, Media, 0)
            Assert.Equal("Tar", session.UnsupportedKind)
            Assert.Empty(session.Entries)
        End Using
    End Sub

    <Fact>
    Public Sub A_real_zip_is_not_refused()
        Dim archivePath As String = MakeZip("real.cbz", Nothing, "page1.jpg")
        Using session As New ArchiveSession(archivePath, sessionDir, Media, 0)
            Assert.Equal("", session.UnsupportedKind)
            Assert.Single(session.Entries)
        End Using
    End Sub

    ' --------------------------------------------------------------- G-16 ----

    ''' <summary>Explorer on Russian Windows writes Cyrillic names in CP866 with no UTF-8
    ''' flag; they must come back as Cyrillic, not box-drawing characters. The culture is
    ''' pinned so the row does not depend on the machine running it.</summary>
    <Fact>
    Public Sub Names_in_the_oem_code_page_come_back_readable()
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        Dim archivePath As String = MakeZip("legacy.zip", Encoding.GetEncoding(866), "Страница_01.jpg")

        Dim previous As CultureInfo = CultureInfo.CurrentCulture
        Try
            CultureInfo.CurrentCulture = New CultureInfo("ru-RU")
            Using session As New ArchiveSession(archivePath, sessionDir, Media, 0)
                Assert.Single(session.Entries)
                Assert.Equal("Страница_01.jpg", session.Entries(0).EntryName)
            End Using
        Finally
            CultureInfo.CurrentCulture = previous
        End Try
    End Sub

    ''' <summary>A name that carries the UTF-8 flag is still read as UTF-8, whatever the OEM
    ''' code page says.</summary>
    <Fact>
    Public Sub Utf8_names_stay_utf8()
        Dim archivePath As String = MakeZip("modern.zip", Nothing, "Сторінка_02.jpg")
        Dim previous As CultureInfo = CultureInfo.CurrentCulture
        Try
            CultureInfo.CurrentCulture = New CultureInfo("ru-RU")
            Using session As New ArchiveSession(archivePath, sessionDir, Media, 0)
                Assert.Equal("Сторінка_02.jpg", session.Entries(0).EntryName)
            End Using
        Finally
            CultureInfo.CurrentCulture = previous
        End Try
    End Sub

    ' --------------------------------------------------------------- G-21 ----

    ''' <summary>A prefetch that arrives after the archive was left must not recreate the
    ''' session directory LeaveArchive deleted.</summary>
    <Fact>
    Public Sub A_closed_session_never_recreates_its_directory()
        Dim archivePath As String = MakeZip("late.zip", Nothing, "a.jpg", "b.jpg")
        Dim session As New ArchiveSession(archivePath, sessionDir, Media, 0)
        session.Dispose()
        Directory.Delete(sessionDir, recursive:=True)

        Dim refusal As ArchiveSession.EntryRefusal
        Assert.False(session.TryEnsureExtracted(1, refusal))
        Assert.Equal(ArchiveSession.EntryRefusal.Broken, refusal)
        Assert.False(Directory.Exists(sessionDir), "A disposed session recreated its directory.")
    End Sub

    <Fact>
    Public Sub Paths_under_the_archive_cache_are_recognised_and_nothing_else_is()
        Dim root As String = Path.Combine(workDir, "archive-cache")
        Assert.True(ArchivePaths.IsUnderArchiveCache(Path.Combine(root, "1234-abcd1234", "00042_page12.jpg"), root))
        Assert.False(ArchivePaths.IsUnderArchiveCache(Path.Combine(workDir, "archive-cache-old", "x.jpg"), root))
        Assert.False(ArchivePaths.IsUnderArchiveCache(Path.Combine(workDir, "photos", "x.jpg"), root))
    End Sub

    ' --------------------------------------------------------------- G-14 ----

    <Theory>
    <InlineData(True, 3L * 1024 * 1024, True)>
    <InlineData(True, 64L * 1024 * 1024, True)>
    <InlineData(True, 64L * 1024 * 1024 + 1, False)>
    <InlineData(False, 1024L, False)>
    <InlineData(True, 0L, False)>
    Public Sub Only_a_modest_picture_is_extracted_ahead(isImage As Boolean, size As Long, expected As Boolean)
        Assert.Equal(expected, ArchiveEntryFilter.ShouldPrefetch(isImage, size))
    End Sub

End Class
#End If
