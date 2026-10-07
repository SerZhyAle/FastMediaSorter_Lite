#If Not NETFRAMEWORK Then
Option Strict On

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 4: the three hand-off channels reach the viewer by
' the same door. A file named on the command line, forwarded from a second launch, dropped on the window,
' pasted as barcode text or as a link - all of them are ONE argument string that goes through ONE parser.
' Pure: no WinForms, so the routing is tested in Lite.Tests.

Friend Module LiveBroadcastIngress

    Public Const FileExtension As String = ".fmsbcast"

    ''' <summary>
    ''' Reads an argument as a live-broadcast hand-off. Returns Nothing when it is none of the three forms (the
    ''' caller carries on with it as a path or an address). The route is decided by the SHAPE of the text, before
    ''' any File.Exists or media classification - but the extension is never trusted: what is inside the file
    ''' still goes through the parser, and a file named .fmsbcast that is not a descriptor is refused as such.
    ''' </summary>
    Public Function TryRead(argument As String) As LiveBroadcastReadResult
        If String.IsNullOrWhiteSpace(argument) Then Return Nothing
        Dim text As String = argument.Trim().Trim(""""c).Trim()
        If text.Length = 0 Then Return Nothing

        If text.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) AndAlso Not IsLink(text) Then
            Return LiveBroadcastReader.ReadFile(text)
        End If

        If text.StartsWith(LiveBroadcastReader.BarcodePrefix, StringComparison.OrdinalIgnoreCase) Then
            Return LiveBroadcastReader.ReadText(text)
        End If

        If IsLink(text) Then Return LiveBroadcastLinkReader.ReadLink(text)

        Return Nothing
    End Function

    ''' <summary>An Android intent link or the app's own scheme (section 4.3).</summary>
    Private Function IsLink(text As String) As Boolean
        Return text.StartsWith("intent:", StringComparison.OrdinalIgnoreCase) OrElse
               text.StartsWith("fmsbcast:", StringComparison.OrdinalIgnoreCase)
    End Function

End Module
#End If
