#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Windows.Forms

' "Open URL.." - play a video straight off a network location, modern build only
' (SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md Ф-A / O-ISO-5; the x86 viewer stays the
' minimal local sorter, so this whole file compiles to nothing there).
'
' WHY THIS EXISTS AT ALL: the LibVLC access plugins for smb / sftp / ftp / http(s) /
' nfs / rtsp are ALREADY in the bundled plugin set - nothing was missing but a way to
' hand VLC an address, plus the File.Exists gate in PlayVideoWithVlcAsync that turned
' every non-file MRL into a silent no-op. UNC shares (\\server\share\x.mkv) always
' worked and still take the ordinary local path: Windows resolves them, File.Exists
' answers honestly, and New Uri gives file://.
'
' SCOPE, deliberately narrow: video only. An image URL is refused with a clear message
' instead of being half-played, because the image features the app is actually about
' (zoom, OCR overlay, perspective background) all read a local decoded bitmap.
'
' The address arithmetic lives in MediaUrl.vb as pure functions so it can be tested;
' what stays here is the WinForms glue - menu, prompt, messages.
Partial Public Class Main_Form

    Private openUrlMenu As ContextMenuStrip
    Private openUrlMenuItem As ToolStripMenuItem

    ''' <summary>Last address the user typed, so a retry after a typo does not start
    ''' from an empty box. Not persisted - it can carry credentials.</summary>
    Private last_Opened_Url As String = ""

    ''' <summary>Thin alias so the VideoPlayer seam reads plainly at its call sites.</summary>
    Friend Shared Function IsRemoteMediaUrl(text As String) As Boolean
        Return MediaUrl.IsRemote(text)
    End Function

    Friend Shared Function DisplayNameForMrl(mrl As String) As String
        Return MediaUrl.DisplayName(mrl)
    End Function

    ''' <summary>Wires the "Open URL.." item onto the choose-file button's right-click.
    ''' Called once from Form1_Load. Right-click on the media surfaces is already file
    ''' navigation, so the secondary action hangs off the button it belongs to - the
    ''' same shape as the folder box's "Share this folder.." item.</summary>
    Friend Sub InitializeOpenUrlEntryPoint()
        If openUrlMenu Is Nothing Then
            openUrlMenu = New ContextMenuStrip()
            openUrlMenuItem = New ToolStripMenuItem()
            AddHandler openUrlMenuItem.Click, Sub() ShowOpenUrlPrompt()
            openUrlMenu.Items.Add(openUrlMenuItem)
            If btn_choose_file IsNot Nothing Then btn_choose_file.ContextMenuStrip = openUrlMenu
        End If
        LocalizeOpenUrlEntryPoint()
    End Sub

    ''' <summary>Re-localizes the item and the button's tooltip. Called from LngCh.</summary>
    Friend Sub LocalizeOpenUrlEntryPoint()
        If openUrlMenuItem IsNot Nothing Then
            openUrlMenuItem.Text = Localization.T("Открыть URL..")
        End If
    End Sub

    ''' <summary>The tooltip for the choose-file button, which has to advertise the
    ''' right-click - a hidden menu nobody finds is the same as no feature.</summary>
    Friend Shared Function ChooseFileTooltipText() As String
        Return Localization.T("Выбрать файл..  (правый клик - открыть URL)")
    End Function

    Private Sub ShowOpenUrlPrompt()
        SlideShowStop()

        Dim prompt As String = Localization.T("Адрес видеопотока или файла на сервере:" & vbLf & "" & vbLf & "smb://сервер/шара/фильм.mkv" & vbLf & "http://сервер/видео.mp4" & vbLf & "sftp://пользователь@сервер/путь/фильм.mkv" & vbLf & "" & vbLf & "Обычные сетевые папки (\\сервер\шара\..) открываются кнопкой ""Выбрать файл.."" как есть.")

        Dim title As String = Localization.T("Открыть URL")
        Dim entered As String = Microsoft.VisualBasic.Interaction.InputBox(prompt, title, last_Opened_Url)
        If String.IsNullOrWhiteSpace(entered) Then Return   ' cancelled

        OpenMediaUrl(entered.Trim())
    End Sub

    ''' <summary>Validates an address and hands it to VLC. Friend so a future caller
    ''' (command line, drag-drop of a link) can reuse the same checks.</summary>
    Friend Sub OpenMediaUrl(url As String)
        Dim title As String = Localization.T("Открыть URL")

        If Not MediaUrl.IsRemote(url) Then
            If MediaUrl.LooksLikeLocalPath(url) Then
                MessageBox.Show(Me,
                    Localization.T("Это обычный путь, а не URL. Откройте его кнопкой ""Выбрать файл.."".") ,
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            MessageBox.Show(Me,
                Localization.TF("Не понимаю эту схему адреса. Поддерживаются: " & vbLf & "{0}", String.Join(", ", MediaUrl.Remote_Media_Schemes)),
                title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        ' Images are refused rather than half-supported: everything the app does with a
        ' picture needs a decoded local bitmap, and VLC would only paint it.
        Dim guessed_Extension As String = MediaUrl.ExtensionOf(url)
        If guessed_Extension.Length > 0 AndAlso Image_File_Extensions.Contains(guessed_Extension) Then
            MessageBox.Show(Me,
                Localization.T("По URL можно открыть только видео. Картинку скачайте и откройте файлом."),
                Localization.T("Открыть URL"),
                MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        last_Opened_Url = url
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2460: opening URL " & url)

        ' A stream is a one-off: it belongs to no folder, so the file list and its
        ' counter stay on whatever folder is loaded. Next/Prev therefore walk back into
        ' that folder and end the stream - simple and predictable, and the alternative
        ' (a whole parallel "URL mode") buys nothing for a sorter.
        lbl_Status.Text = Localization.T("Подключение..")
        Application.DoEvents()

        PlayVideoWithVlcAsync(url)
    End Sub

End Class
#End If
