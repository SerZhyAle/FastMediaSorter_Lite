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

    ''' <summary>Audit 050 D-02: LibVLC's codec is a FourCC, lowest byte first. Formatted
    ''' as a number it put "1634492771" on the plate.</summary>
    <Fact>
    Public Sub FourCC_codecs_read_as_names()
        Assert.Equal("AAC", AudioMetadata.CodecName(&H6134706DUI))   ' "mp4a"
        Assert.Equal("MP3", AudioMetadata.CodecName(&H6167706DUI))   ' "mpga"
        Assert.Equal("FLAC", AudioMetadata.CodecName(&H63616C66UI))  ' "flac"
        Assert.Equal("Opus", AudioMetadata.CodecName(&H7375706FUI))  ' "opus"
        Assert.Equal("XYZW", AudioMetadata.CodecName(&H575A5958UI))  ' an unknown one shows its letters
    End Sub

    <Fact>
    Public Sub A_FourCC_that_is_not_letters_shows_nothing()
        Assert.Equal("", AudioMetadata.CodecName(0UI))
        Assert.Equal("", AudioMetadata.CodecName(&H1020304UI))
        Assert.Equal("", AudioMetadata.CodecName(&H20202020UI))
    End Sub

    <Fact>
    Public Sub Long_title_is_truncated()
        Dim value = AudioMetadata.FromValues("C:\Music\song.mp3", New String("A"c, 201))
        Assert.Equal(201, value.Title.Length)
        Assert.EndsWith("…", value.Title)
    End Sub
End Class
