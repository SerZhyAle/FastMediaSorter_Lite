#If Not NETFRAMEWORK Then
Option Strict On

' Address handling for "Open URL.." - modern build only, like the feature itself
' (SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md Ф-A / O-ISO-5). Pure functions of their
' arguments, kept out of Main_Form.OpenUrl.vb for the same reason ZoomMath is kept out
' of Main_Form.Zoom.vb: locked inside a WinForms partial, nothing could test them.
Friend Module MediaUrl

    ''' <summary>
    ''' Schemes VLC's bundled access plugins can open. Deliberately a whitelist: an
    ''' unknown scheme should be refused with a sentence the user can act on, not handed
    ''' to VLC to fail with something cryptic.
    ''' </summary>
    Friend ReadOnly Remote_Media_Schemes As String() = {
        "smb://", "sftp://", "ftp://", "ftps://", "http://", "https://",
        "nfs://", "rtsp://", "rtmp://", "mms://", "mmsh://", "udp://", "rtp://"}

    ''' <summary>
    ''' True when this is a remote address for VLC rather than something on disk.
    ''' A UNC path (\\server\share\x.mkv) is deliberately NOT remote by this definition:
    ''' Windows resolves it, File.Exists answers honestly about it, and it has always
    ''' played through the ordinary local route. Calling it remote would push a working
    ''' path onto the streaming branch for no gain.
    ''' </summary>
    Friend Function IsRemote(text As String) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim trimmed As String = text.Trim()
        For Each scheme As String In Remote_Media_Schemes
            If trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ''' <summary>True for a local drive path or a UNC share - the things "Choose file.."
    ''' is for. Used to answer a pasted path with a useful sentence.</summary>
    Friend Function LooksLikeLocalPath(text As String) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim trimmed As String = text.Trim()
        Return trimmed.StartsWith("\\") OrElse trimmed.Contains(":\")
    End Function

    ''' <summary>
    ''' A name worth putting in the status line. Path.GetFileName on an MRL drags the
    ''' query string along ("stream?token=..") and returns nothing at all for an address
    ''' ending in "/", so the host is the honest fallback there.
    ''' </summary>
    Friend Function DisplayName(mrl As String) As String
        If String.IsNullOrWhiteSpace(mrl) Then Return ""
        Try
            Dim parsed As New Uri(mrl)
            Dim last As String = parsed.Segments(parsed.Segments.Length - 1).Trim("/"c)
            If last.Length > 0 Then Return Uri.UnescapeDataString(last)
            Return parsed.Host
        Catch
            Return mrl   ' unparseable - showing it verbatim beats showing nothing
        End Try
    End Function

    ''' <summary>
    ''' The file extension an address points at, lowercased, or "" when there is none to
    ''' read. Uses AbsolutePath so a query string never becomes part of the extension.
    ''' </summary>
    Friend Function ExtensionOf(mrl As String) As String
        If String.IsNullOrWhiteSpace(mrl) Then Return ""
        Try
            Return IO.Path.GetExtension(New Uri(mrl).AbsolutePath).ToLowerInvariant()
        Catch
            Return ""
        End Try
    End Function

End Module
#End If
