Option Strict On

Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Partial Public Class Main_Form

    ' Messages Windows' UIPI (User Interface Privilege Isolation) blocks when an
    ' elevated process tries to receive a drop from a normal-IL Explorer. Without
    ' allowing them the cursor shows "no-drop" and the drop is silently dropped.
    Private Const WM_COPYGLOBALDATA As UInteger = &H49UI
    Private Const WM_COPYDATA_MSG As UInteger = &H4AUI
    Private Const WM_DROPFILES As UInteger = &H233UI
    Private Const MSGFLT_ADD As UInteger = 1UI

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function ChangeWindowMessageFilter(message As UInteger, dwFlag As UInteger) As Boolean
    End Function

    ' Drag-and-drop of a media file is wired not only to the form but to the
    ' media surfaces themselves (the two picture boxes, the media container, the
    ' help label, the video WebBrowser and the LibVLC view). Without this, a drop
    ' over a displayed image/video (or the empty help screen) lands on a control
    ' that is NOT a registered OLE drop target, so Windows shows the "no-drop"
    ' cursor instead of forwarding it to the form. Every path ends in
    ' ProcessArgument(), which opens the file, switches to its folder and plays.
    Private Sub WireSurfaceDragDrop()
        ' When the app runs elevated (e.g. launched from an elevated file manager)
        ' Windows' UIPI blocks the drop from a normal-IL Explorer and shows the
        ' "no-drop" cursor. Allow the drag-drop messages through, process-wide so
        ' it covers every child surface HWND. Harmless (no-op) when not elevated.
        Try
            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD)
            ChangeWindowMessageFilter(WM_COPYDATA_MSG, MSGFLT_ADD)
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0234: ChangeWindowMessageFilter failed: " & ex.Message)
        End Try

        For Each surface As Control In New Control() {Picture_Box_1, Picture_Box_2, panel_Media, lbl_Help_Info}
            If surface Is Nothing Then Continue For
            surface.AllowDrop = True
            AddHandler surface.DragEnter, AddressOf Form1_DragEnter
            AddHandler surface.DragDrop, AddressOf Form1_DragDrop
        Next

#If NETFRAMEWORK Then
        ' The WebBrowser ActiveX host never raises managed DragEnter/DragDrop, so
        ' instead we let IE accept the file with its built-in "drop to open"
        ' behaviour (AllowWebBrowserDrop). That fires Navigating with the dropped
        ' file's URL, which we intercept below - giving us the full path for free.
        ' Modern: video always plays in the LibVLC view (wired above/lazily), the
        ' dormant WebBrowser never shows - touching it would only wake its ActiveX.
        If Web_Browser IsNot Nothing Then
            Web_Browser.AllowWebBrowserDrop = True
            AddHandler Web_Browser.Navigating, AddressOf Web_Browser_Navigating
        End If
#End If
    End Sub

    ' Called once the LibVLC surface has been created (it is built lazily for the
    ' codec-fallback formats), so drops over a playing AVI/MKV also open the file.
    Private Sub WireVlcSurfaceDragDrop()
        If vlc_Video_View Is Nothing Then Return
        vlc_Video_View.AllowDrop = True
        AddHandler vlc_Video_View.DragEnter, AddressOf Form1_DragEnter
        AddHandler vlc_Video_View.DragDrop, AddressOf Form1_DragDrop
    End Sub

#If NETFRAMEWORK Then
    Private Sub Web_Browser_Navigating(sender As Object, e As WebBrowserNavigatingEventArgs)
        ' We only ever load video via DocumentText (which navigates to about:blank)
        ' and never navigate the browser to a file ourselves. So a file:// URL here
        ' can only mean the user dropped a file onto the video surface.
        Dim url As Uri = e.Url
        If url Is Nothing OrElse Not url.IsFile Then Return

        e.Cancel = True
        Dim dropped_Path As String = url.LocalPath
        If String.IsNullOrEmpty(dropped_Path) Then Return

        ' Defer so we don't re-enter the browser's navigation state machine while
        ' it is still processing the cancelled navigation (ProcessArgument resets
        ' Web_Browser.DocumentText for the new video).
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0233: drop on video surface: " & dropped_Path)
        Me.BeginInvoke(New Action(Sub() ProcessArgument(dropped_Path)))
    End Sub
#End If

End Class
