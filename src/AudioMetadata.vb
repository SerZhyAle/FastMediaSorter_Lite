Option Strict On

Imports System.Globalization
Imports System.IO

''' <summary>Display-ready audio tags. This type has no LibVLC or UI dependency,
''' so its fallback and formatting rules are testable on both targets.</summary>
Public NotInheritable Class AudioMetadata
    Public Property CoverPath As String = ""
    Public Property Title As String = ""
    Public Property Artist As String = ""
    Public Property Album As String = ""
    Public Property FormatLine As String = ""

    Public Shared Function FromValues(filePath As String, Optional title As String = "", Optional artist As String = "", Optional album As String = "", Optional artworkUrl As String = "", Optional codec As String = "", Optional bitrate As Long = 0, Optional sampleRate As Long = 0, Optional durationMilliseconds As Long = 0, Optional fileSizeBytes As Long = -1) As AudioMetadata
        Dim result As New AudioMetadata()
        result.Title = Truncate(If(String.IsNullOrWhiteSpace(title), Path.GetFileNameWithoutExtension(filePath), title.Trim()))
        result.Artist = If(artist, "").Trim()
        result.Album = If(album, "").Trim()
        result.CoverPath = If(artworkUrl, "").Trim()
        Dim parts As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(codec) Then parts.Add(codec.Trim().ToUpperInvariant())
        If bitrate > 0 Then parts.Add((bitrate \ 1000).ToString(CultureInfo.InvariantCulture) & " kbps")
        If sampleRate > 0 Then parts.Add(sampleRate.ToString(CultureInfo.InvariantCulture) & " Hz")
        If durationMilliseconds > 0 Then parts.Add(FormatDuration(durationMilliseconds))
        result.FormatLine = If(parts.Count > 0, String.Join(" • ", parts), BuildFileFormat(filePath, fileSizeBytes))
        Return result
    End Function

    ''' <summary>
    ''' A codec as a person reads it, from LibVLC's FourCC (audit 050 D-02). The value is a
    ''' UInt32 whose bytes are four ASCII letters, lowest byte first ('m','p','4','a' is
    ''' 0x6134706D); formatting it as a number printed "1634492771" on the plate. The
    ''' common audio codecs get their everyday name; anything else shows its four letters,
    ''' and a value that is not four printable letters shows nothing rather than noise.
    ''' </summary>
    Public Shared Function CodecName(fourCc As UInteger) As String
        If fourCc = 0UI Then Return ""
        Dim chars(3) As Char
        For i As Integer = 0 To 3
            Dim b As Integer = CInt((fourCc >> (8 * i)) And &HFFUI)
            If b < 32 OrElse b > 126 Then Return ""
            chars(i) = ChrW(b)
        Next
        Dim text As String = New String(chars).Trim()
        If text.Length = 0 Then Return ""
        Select Case text.ToLowerInvariant()
            Case "mpga", "mp3" : Return "MP3"
            Case "mp4a" : Return "AAC"
            Case "flac" : Return "FLAC"
            Case "vorb" : Return "Vorbis"
            Case "opus" : Return "Opus"
            Case "a52", "ac-3" : Return "AC-3"
            Case "eac3" : Return "E-AC-3"
            Case "alac" : Return "ALAC"
            Case "wma1", "wma2", "wmap", "wmal" : Return "WMA"
            Case "s16l", "s24l", "s32l", "f32l", "u8", "araw", "lpcm" : Return "PCM"
        End Select
        Return text.ToUpperInvariant()
    End Function

    Private Shared Function BuildFileFormat(filePath As String, fileSizeBytes As Long) As String
        Dim extension As String = Path.GetExtension(filePath).ToUpperInvariant()
        Return If(fileSizeBytes >= 0, extension & " • " & FormatSize(fileSizeBytes), extension)
    End Function

    Private Shared Function FormatDuration(milliseconds As Long) As String
        Dim value As TimeSpan = TimeSpan.FromMilliseconds(milliseconds)
        Return If(value.TotalHours >= 1, value.ToString("h\:mm\:ss"), value.ToString("m\:ss"))
    End Function

    Private Shared Function FormatSize(bytes As Long) As String
        Dim units() As String = {"B", "KB", "MB", "GB"}
        Dim size As Double = bytes
        Dim unit As Integer
        While size >= 1024 AndAlso unit < units.Length - 1
            size /= 1024 : unit += 1
        End While
        Return If(unit = 0, CInt(size).ToString(CultureInfo.InvariantCulture), size.ToString("0.0", CultureInfo.InvariantCulture)) & " " & units(unit)
    End Function

    Private Shared Function Truncate(value As String) As String
        Return If(value.Length <= 200, value, value.Substring(0, 200) & "…")
    End Function
End Class
