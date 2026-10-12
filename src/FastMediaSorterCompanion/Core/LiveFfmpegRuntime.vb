Option Strict On

Imports System.IO

' Ticket 199 section 4: the encoder engine of the working assumption (Q199-1, candidate a) - the pinned
' FFmpeg runtime the viewer already downloads on first use, "shared on disk with the Share Manager".
'
' The viewer's OptionalRuntimeManager owns the download (versioned, SHA-256 pinned, about 110 MB,
' GPL, not bundled) and installs it under %LOCALAPPDATA%\SZA\FastMediaSorter\ffmpeg\<version>. This
' locator finds that shared copy - any pinned version, the newest folder wins, and the viewer's
' whole-tree marker must be present - plus the exe-adjacent form an offline package could carry.
' The version number is deliberately NOT repeated here: duplicating the pin would give the two
' programs a second constant to drift apart on, while accepting the shared folder keeps one download
' serving both, whatever version the viewer last fetched.

''' <summary>Where the shared FFmpeg runtime lives, for the live-broadcast encoder.</summary>
Public Module LiveFfmpegRuntime

    ''' <summary>The exe-adjacent form (a future offline packaging option), then the shared downloaded
    ''' tree. The whole-tree marker ("IsDownloadedTreeWhole" in the viewer's manager) must sit beside
    ''' the exe: a truncated download is never run.</summary>
    Public Function GetFfmpegPath() As String
        Dim local As String = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe")
        If File.Exists(local) Then Return local

        Dim root As String = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SZA", "FastMediaSorter", "ffmpeg")
        If Not Directory.Exists(root) Then Return ""

        Dim best As String = ""
        Dim bestVersion As Version = Nothing
        For Each dir As String In Directory.EnumerateDirectories(root)
            Dim exe As String = Path.Combine(dir, "ffmpeg.exe")
            If Not File.Exists(exe) Then Continue For
            If Not File.Exists(Path.Combine(dir, ".complete")) Then Continue For
            Dim nameVersion As String = Path.GetFileName(dir)
            Dim parsed As Version = Nothing
            If Not Version.TryParse(nameVersion, parsed) Then parsed = Nothing
            If best.Length = 0 OrElse (parsed IsNot Nothing AndAlso (bestVersion Is Nothing OrElse parsed > bestVersion)) Then
                best = exe
                bestVersion = parsed
            End If
        Next
        Return best
    End Function

    Public Function HasRuntime() As Boolean
        Return GetFfmpegPath().Length > 0
    End Function

End Module
