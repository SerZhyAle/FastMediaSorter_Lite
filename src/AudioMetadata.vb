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
