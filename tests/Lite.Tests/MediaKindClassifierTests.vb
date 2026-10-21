#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

Public Class MediaKindClassifierTests
    ' Independent conformance vectors, MEDIA-CLASSIFICATION section 4: the rule 1 lists of 0.10 verbatim, plus the
    ' 0.12 amendment A (`.jxl` is IMAGE; classification only, it claims no decoder the build lacks).
    Public Shared Iterator Function CanonicalExtensions() As IEnumerable(Of Object())
        Dim rows As New Dictionary(Of MediaKind, String) From {
            {MediaKind.Image, ".jpg .jpeg .png .webp .gif .bmp .avif .heic .heif .svg .tiff .ico .wmf .emf .jxl"},
            {MediaKind.Video, ".mp4 .mkv .mov .webm .3gp .flv .wmv .m4v .avi .mpg .mpeg .ts .m2ts .vob .ogv .divx .m2v .mts .3g2 .asf"},
            {MediaKind.Audio, ".mp3 .m4a .flac .aac .ogg .wma .opus .amr .awb .ac3 .ec3 .ac4 .adts .thd .mka .oga .caf .alac .mia .mid .midi .wav .wave"},
            {MediaKind.Book, ".epub .pdf .cbz .cbr .fb2 .mobi"},
            {MediaKind.Stream, ".m3u .m3u8 .pls .xspf .fmsbcast"},
            {MediaKind.Container, ".fd-sec .zip .rar .7z .tar .gz .bz2 .xz .cab .arj .lzh .iso .dmg .img .vhd .vdi .qcow2 .vmdk"},
            {MediaKind.Document, ".doc .docx .rtf .odt .xls .xlsx .ods .ppt .pptx .odp .txt .md .json .xml .csv .tsv .yaml .yml .toml .ini .conf"}}
        For Each row In rows
            For Each extension As String In row.Value.Split(" "c)
                Yield New Object() {extension, row.Key}
            Next
        Next
    End Function

    <Theory, MemberData(NameOf(CanonicalExtensions))>
    Public Sub Canonical_table_matches_without_claiming_renderer_support(extension As String, expected As MediaKind)
        Assert.Equal(expected, MediaKindClassifier.KindOf(extension))
        Assert.Equal(expected, MediaKindClassifier.KindOf("folder/sample" & extension.ToUpperInvariant() & ".DOWNLOAD. "))
    End Sub

    <Theory>
    <InlineData(Nothing), InlineData(""), InlineData("file"), InlineData("... "), InlineData(".xyz"), InlineData("file.download")>
    Public Sub Unknown_or_empty_names_return_other(name As String)
        Assert.Equal(MediaKind.Other, MediaKindClassifier.KindOf(name))
    End Sub

    <Theory>
    <InlineData(".tif", MediaKind.Image), InlineData(".exif", MediaKind.Image)>
    <InlineData(".log", MediaKind.Document), InlineData(".html", MediaKind.Document), InlineData(".htm", MediaKind.Document)>
    Public Sub Existing_local_aliases_keep_their_categories(extension As String, expected As MediaKind)
        Assert.Equal(expected, MediaKindClassifier.KindOf(extension))
    End Sub

    <Fact>
    Public Sub Audiobook_requires_positive_book_evidence()
        Assert.Equal(MediaKind.Audio, MediaKindClassifier.KindOf("speech.m4b"))
        Assert.Equal(MediaKind.Book, MediaKindClassifier.KindOf("speech.M4B.download", hasBookCues:=True))
        Assert.Equal(MediaKind.Audio, MediaKindClassifier.KindOf("music.mp3", hasBookCues:=True))
    End Sub

    <Theory>
    <InlineData("C:\folder.avi\photo.JPG.download.temp_copy.. ", ".jpg")>
    <InlineData("A:B/page.PNG.temp_copy.DOWNLOAD", ".png")>
    <InlineData("folder.jpg/file", "")>
    <InlineData("photo.download.jpg", ".jpg")>
    <InlineData("photo.jpg.tmp", ".tmp")>
    Public Sub Navigation_and_classifier_use_the_same_normalized_extension(name As String, expected As String)
        Assert.Equal(expected, MediaKindClassifier.ExtensionOf(name))
        Assert.Equal(expected, NavigationRules.ExtensionKey(name))
        Assert.Equal(expected, ArchiveEntryFilter.ExtensionOf(name))
    End Sub

    <Fact>
    Public Sub Matching_remains_invariant_in_Turkish_culture()
        Dim original As CultureInfo = CultureInfo.CurrentCulture
        Try
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR")
            Assert.Equal(MediaKind.Image, MediaKindClassifier.KindOf("photo.HEIC.DOWNLOAD"))
            Assert.Equal(MediaKind.Video, MediaKindClassifier.KindOf("clip.AVI"))
        Finally
            CultureInfo.CurrentCulture = original
        End Try
    End Sub

    <Theory>
    <InlineData(".hidden.jpg"), InlineData("._page.jpg"), InlineData(".DS_Store"), InlineData(".git/photo.jpg")>
    <InlineData("chapter\__MACOSX\page.jpg"), InlineData("chapter/desktop.INI"), InlineData("Thumbs.DB")>
    <InlineData("photo.jpg.tmp"), InlineData("photo.~backup.jpg"), InlineData("photo.jpg.swp"), InlineData("photo.tmp.download")>
    Public Sub Browsing_excludes_junk_even_when_its_extension_is_selected(name As String)
        Dim supported As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".jpg", ".ini", ".db", ".tmp", ".swp"}
        Assert.True(MediaBrowsing.IsJunk(name))
        Assert.False(MediaBrowsing.IsEligible(name, supported))
        Assert.False(ArchiveEntryFilter.IsEligible(name, False, 1, True))
    End Sub

    <Fact>
    Public Sub Classification_does_not_override_the_navigation_subset_or_storage_listing()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-classification-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            For Each name As String In {"page.JPG", "page.png.download", "song.mp3", "movie.mp4", "new.flac", "book.pdf", "list.m3u", ".hidden.jpg", "page.~copy.jpg"}
                File.WriteAllText(Path.Combine(folder, name), "test")
            Next
            Dim entries = New LocalProvider(Function() False).ListEntries(folder, CancellationToken.None)
            Assert.Equal(9, entries.Count) ' The provider remains a byte-storage listing.
            Dim selected As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".jpg", ".png"}
            Assert.Equal({"page.JPG", "page.png.download"}, entries.Where(Function(e) MediaBrowsing.IsEligible(e.FullPath, selected)).Select(Function(e) e.Name).OrderBy(Function(n) n).ToArray())
            Assert.Equal(MediaKind.Audio, MediaKindClassifier.KindOf("new.flac"))
            Assert.False(MediaBrowsing.IsEligible("new.flac", selected))
            Assert.False(MediaBrowsing.IsEligible("song.mp3", selected))
            Assert.False(MediaBrowsing.IsEligible("movie.mp4", selected))
            Assert.False(MediaBrowsing.IsEligible("book.pdf", selected))
            Assert.False(MediaBrowsing.IsEligible("list.m3u", selected))
        Finally
            Directory.Delete(folder, recursive:=True)
        End Try
    End Sub
End Class
#End If
