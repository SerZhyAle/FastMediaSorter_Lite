#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Linq
Imports Xunit

Public Class SidecarAssociationsTests
    <Theory>
    <InlineData("film.srt", SidecarKind.Subtitle)>
    <InlineData("film.en-US.vtt", SidecarKind.Subtitle)>
    <InlineData("film.ru.ass", SidecarKind.Subtitle)>
    <InlineData("film.sub", SidecarKind.Subtitle)>
    <InlineData("film.lrc", SidecarKind.Lyrics)>
    <InlineData("film.txt", SidecarKind.Lyrics)>
    <InlineData("film.jpg", SidecarKind.Artwork)>
    <InlineData("folder.png", SidecarKind.Artwork)>
    <InlineData("cover.webp", SidecarKind.Artwork)>
    <InlineData("film.nfo", SidecarKind.Metadata)>
    <InlineData("film.json", SidecarKind.Metadata)>
    <InlineData("film.xml", SidecarKind.Metadata)>
    Public Sub Contract_patterns_associate_without_changing_the_primary_category(name As String, expected As Integer)
        Dim primary = Path.Combine(Path.GetTempPath(), "film.mkv")
        Dim result = SidecarAssociations.Match(primary, {Path.Combine(Path.GetTempPath(), name)})
        Assert.Equal(CType(expected, SidecarKind), Assert.Single(result).Kind)
        Assert.Equal(MediaKind.Video, MediaKindClassifier.KindOf(primary))
    End Sub

    <Fact>
    Public Sub Unrelated_prefixes_other_directories_and_the_primary_are_excluded()
        Dim primary = Path.Combine(Path.GetTempPath(), "film.mkv")
        Dim result = SidecarAssociations.Match(primary, {primary,
            Path.Combine(Path.GetTempPath(), "film2.srt"), Path.Combine(Path.GetTempPath(), "film.en.forced.srt"),
            Path.Combine(Path.GetTempPath(), "other", "film.srt"), Path.Combine(Path.GetTempPath(), "film-other.jpg")})
        Assert.Empty(result)
    End Sub

    <Fact>
    Public Sub Exact_artwork_precedes_folder_fallback_and_duplicate_names_are_folded()
        Dim directory = Path.GetTempPath()
        Dim result = SidecarAssociations.Match(Path.Combine(directory, "track.mp3"),
            {Path.Combine(directory, "cover.jpg"), Path.Combine(directory, "track.png"), Path.Combine(directory, "TRACK.PNG")})
        Assert.Equal(2, result.Count)
        Assert.Equal("track.png", Path.GetFileName(result(0).Path))
    End Sub

    <Fact>
    Public Sub A_picture_has_no_cover_but_keeps_its_metadata_sidecar()
        Dim directory = Path.GetTempPath()
        Dim result = SidecarAssociations.Match(Path.Combine(directory, "photo.jpg"),
            {Path.Combine(directory, "cover.jpg"), Path.Combine(directory, "folder.png"), Path.Combine(directory, "photo.png"),
             Path.Combine(directory, "photo.json")})
        Assert.Equal(SidecarKind.Metadata, Assert.Single(result).Kind)
    End Sub

    <Fact>
    Public Sub File_details_expose_all_four_associations_without_reading_sidecar_contents()
        Dim directory = Path.Combine(Path.GetTempPath(), "fms-sidecar-" & Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(directory)
        Try
            For Each name In {"track.mp3", "track.en.srt", "track.lrc", "cover.jpg", "track.nfo"}
                File.WriteAllText(Path.Combine(directory, name), "untrusted content")
            Next
            Dim section = FileDetails.BuildSections(Path.Combine(directory, "track.mp3")).Single(Function(s) s.TitleKey = "Связанные файлы")
            Assert.Equal(4, section.Rows.Count)
            Assert.DoesNotContain(section.Rows, Function(r) r.Value.Contains("untrusted content"))
        Finally
            IO.Directory.Delete(directory, True)
        End Try
    End Sub
End Class
#End If
