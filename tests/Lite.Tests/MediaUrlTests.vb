#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

' Address handling for "Open URL.." (SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md Ф-A).
' Modern-only, like the feature: on the net48 leg MediaUrl.vb and this file compile to
' nothing - the x86 viewer has no way to enter a URL.
'
' The load-bearing rule here is the UNC one. A UNC share is NOT a remote URL: it always
' played through the ordinary local path, and misclassifying it would push a working
' setup onto the streaming branch and skip the File.Exists that makes a missing file
' fail honestly.
Public Class MediaUrlTests

    <Theory>
    <InlineData("smb://server/share/movie.mkv")>
    <InlineData("sftp://user@server/path/movie.mkv")>
    <InlineData("http://server/video.mp4")>
    <InlineData("https://server/video.mp4")>
    <InlineData("ftp://server/video.avi")>
    <InlineData("nfs://server/export/v.mkv")>
    <InlineData("rtsp://cam.local/stream1")>
    <InlineData("SMB://SERVER/Share/Movie.MKV")>
    Public Sub IsRemote_AcceptsTheSchemesVlcCanOpen(url As String)
        Assert.True(MediaUrl.IsRemote(url))
    End Sub

    <Theory>
    <InlineData("\\server\share\movie.mkv")>
    <InlineData("C:\video\movie.mkv")>
    <InlineData("movie.mkv")>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData(Nothing)>
    Public Sub IsRemote_RejectsEverythingThatIsAPath(text As String)
        Assert.False(MediaUrl.IsRemote(text))
    End Sub

    <Fact>
    Public Sub IsRemote_UncIsNotRemote_TheRuleTheFeatureRestsOn()
        ' \\server\share is a path Windows resolves; File.Exists answers honestly about
        ' it and it has always played. Calling it "remote" would route a working case
        ' onto the streaming branch and lose the missing-file check for nothing.
        Assert.False(MediaUrl.IsRemote("\\nas\media\film.mkv"))
    End Sub

    <Fact>
    Public Sub IsRemote_UnknownScheme_IsRefused()
        ' Better a sentence the user can act on than a cryptic failure out of VLC.
        Assert.False(MediaUrl.IsRemote("magnet:?xt=urn:btih:abc"))
        Assert.False(MediaUrl.IsRemote("file:///C:/video/movie.mkv"))
    End Sub

    <Fact>
    Public Sub IsRemote_IgnoresSurroundingWhitespace()
        ' Pasted addresses routinely arrive with a trailing newline.
        Assert.True(MediaUrl.IsRemote("  smb://server/share/movie.mkv  "))
    End Sub

    <Theory>
    <InlineData("\\server\share\movie.mkv")>
    <InlineData("C:\video\movie.mkv")>
    <InlineData("  D:\x.mp4")>
    Public Sub LooksLikeLocalPath_SpotsAPastedPath(text As String)
        Assert.True(MediaUrl.LooksLikeLocalPath(text))
    End Sub

    <Theory>
    <InlineData("smb://server/share/movie.mkv")>
    <InlineData("http://server/video.mp4")>
    <InlineData("")>
    Public Sub LooksLikeLocalPath_LeavesUrlsAlone(text As String)
        Assert.False(MediaUrl.LooksLikeLocalPath(text))
    End Sub

    ' --- DisplayName: what the status line shows -----------------------------

    <Fact>
    Public Sub DisplayName_TakesTheLastSegment()
        Assert.Equal("movie.mkv", MediaUrl.DisplayName("smb://server/share/movie.mkv"))
    End Sub

    <Fact>
    Public Sub DisplayName_DropsTheQueryString()
        ' Path.GetFileName would have returned "video.mp4?token=secret" - the reason this
        ' helper exists at all.
        Assert.Equal("video.mp4", MediaUrl.DisplayName("http://host/media/video.mp4?token=secret"))
    End Sub

    <Fact>
    Public Sub DisplayName_AddressWithNoFileName_FallsBackToTheHost()
        Assert.Equal("cam.local", MediaUrl.DisplayName("rtsp://cam.local/"))
    End Sub

    <Fact>
    Public Sub DisplayName_UnescapesPercentEncoding()
        Assert.Equal("мой фильм.mkv", MediaUrl.DisplayName("smb://server/share/%D0%BC%D0%BE%D0%B9%20%D1%84%D0%B8%D0%BB%D1%8C%D0%BC.mkv"))
    End Sub

    <Fact>
    Public Sub DisplayName_Unparseable_ShowsTheAddressVerbatim()
        ' Showing something beats showing nothing in a status line.
        Assert.Equal("not a uri at all", MediaUrl.DisplayName("not a uri at all"))
    End Sub

    <Fact>
    Public Sub DisplayName_Empty_IsEmpty()
        Assert.Equal("", MediaUrl.DisplayName(""))
        Assert.Equal("", MediaUrl.DisplayName(Nothing))
    End Sub

    ' --- ExtensionOf: how an image URL gets refused --------------------------

    <Fact>
    Public Sub ExtensionOf_ReadsTheExtension()
        Assert.Equal(".mkv", MediaUrl.ExtensionOf("smb://server/share/movie.mkv"))
    End Sub

    <Fact>
    Public Sub ExtensionOf_IsNotFooledByAQueryString()
        ' The check that refuses image URLs runs on this. Left naive, ".jpg?w=100" would
        ' never match ".jpg" and the refusal would silently stop working.
        Assert.Equal(".jpg", MediaUrl.ExtensionOf("http://host/pic.jpg?w=100&h=50"))
    End Sub

    <Fact>
    Public Sub ExtensionOf_UppercaseIsNormalised()
        Assert.Equal(".mp4", MediaUrl.ExtensionOf("http://host/VIDEO.MP4"))
    End Sub

    <Fact>
    Public Sub ExtensionOf_NoExtension_IsEmpty()
        Assert.Equal("", MediaUrl.ExtensionOf("rtsp://cam.local/stream1"))
        Assert.Equal("", MediaUrl.ExtensionOf("not a uri"))
        Assert.Equal("", MediaUrl.ExtensionOf(""))
    End Sub

End Class
#End If
