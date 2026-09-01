Option Strict On

Imports Xunit
Imports fmsl

Public Class AudioMetadataTests
    <Fact>
    Public Sub Full_tags_produce_four_display_lines()
        Dim value = AudioMetadata.FromValues("C:\Music\song.mp3", "Bohemian Rhapsody", "Queen", "A Night at the Opera", "", "mp3", 320000, 44100, 355000)
        Assert.Equal("Bohemian Rhapsody", value.Title)
        Assert.Equal("Queen", value.Artist)
        Assert.Equal("A Night at the Opera", value.Album)
        Assert.Equal("MP3 • 320 kbps • 44100 Hz • 5:55", value.FormatLine)
    End Sub

    <Fact>
    Public Sub Title_only_omits_empty_rows_and_keeps_format()
        Dim value = AudioMetadata.FromValues("C:\Music\song.mp3", "Just a title")
        Assert.Equal("Just a title", value.Title)
        Assert.Empty(value.Artist)
        Assert.Empty(value.Album)
        Assert.Equal(".MP3", value.FormatLine)
    End Sub

    <Fact>
    Public Sub No_tags_uses_file_name_without_extension()
        Dim value = AudioMetadata.FromValues("C:\Music\Track 05 - Beautiful Song.mp3", fileSizeBytes:=1048576)
        Assert.Equal("Track 05 - Beautiful Song", value.Title)
        Assert.Equal(".MP3 • 1.0 MB", value.FormatLine)
    End Sub

    <Fact>
    Public Sub Long_title_is_truncated()
        Dim value = AudioMetadata.FromValues("C:\Music\song.mp3", New String("A"c, 201))
        Assert.Equal(201, value.Title.Length)
        Assert.EndsWith("…", value.Title)
    End Sub
End Class
