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
    Private openLiveBroadcastMenuItem As ToolStripMenuItem

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

    ''' <summary>Wires the "Open URL.." and "Open live broadcast.." items onto the choose-file button's right-click.
    ''' Called once from Form1_Load.</summary>
    Friend Sub InitializeOpenUrlEntryPoint()
        If openUrlMenu Is Nothing Then
            openUrlMenu = New ContextMenuStrip()
            openUrlMenuItem = New ToolStripMenuItem()
            AddHandler openUrlMenuItem.Click, Sub() ShowOpenUrlPrompt()
            openUrlMenu.Items.Add(openUrlMenuItem)

            openLiveBroadcastMenuItem = New ToolStripMenuItem()
            AddHandler openLiveBroadcastMenuItem.Click, Sub() ShowLiveBroadcastDialog()
            openUrlMenu.Items.Add(openLiveBroadcastMenuItem)

            If btn_choose_file IsNot Nothing Then btn_choose_file.ContextMenuStrip = openUrlMenu
        End If
        LocalizeOpenUrlEntryPoint()
    End Sub

    ''' <summary>Re-localizes the items and the button's tooltip. Called from LngCh.</summary>
    Friend Sub LocalizeOpenUrlEntryPoint()
        If openUrlMenuItem IsNot Nothing Then
            openUrlMenuItem.Text = Localization.T("Открыть URL..")
        End If
        If openLiveBroadcastMenuItem IsNot Nothing Then
            openLiveBroadcastMenuItem.Text = Localization.T("Открыть live-трансляцию..")
        End If
    End Sub

    ''' <summary>The tooltip for the choose-file button, which has to advertise the
    ''' right-click - a hidden menu nobody finds is the same as no feature.</summary>
    Friend Shared Function ChooseFileTooltipText() As String
        Return Localization.T("Выбрать файл..  (правый клик - открыть URL)")
    End Function

    Private Sub ShowOpenUrlPrompt()
        SlideShowStop()

        ' The instruction and the example addresses have their own room, and Cancel (Nothing) is not an empty
        ' string - an empty one is refused with a reason (124 UX-204).
        Dim entered As String = AskDialogs.Prompt(Me, AskSpecs.OpenUrlPrompt(last_Opened_Url))
        If entered Is Nothing Then Return   ' cancelled

        OpenMediaUrl(entered.Trim())
    End Sub

    ''' <summary>Validates an address and hands it to VLC. Friend so a future caller
    ''' (command line, drag-drop of a link) can reuse the same checks.</summary>
    Friend Sub OpenMediaUrl(url As String)
        Dim title As String = Localization.T("Открыть URL")

        ' Check if this is an auto-detected or explicit live broadcast link (§4.3)
        Dim liveRes As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(url)
        If liveRes.Status = LiveReadStatus.Ok AndAlso liveRes.Descriptor IsNot Nothing Then
            PlayLiveBroadcast(liveRes.Descriptor)
            Return
        End If

        If Not MediaUrl.IsRemote(url) Then
            If MediaUrl.LooksLikeLocalPath(url) Then
                ViewerBox.Show(Me,
                    Localization.T("Это обычный путь, а не URL. Откройте его кнопкой ""Выбрать файл.."".") ,
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ViewerBox.Show(Me,
                Localization.TF("Не понимаю эту схему адреса. Поддерживаются: " & vbLf & "{0}", String.Join(", ", MediaUrl.Remote_Media_Schemes)),
                title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        ' Images are refused rather than half-supported: everything the app does with a
        ' picture needs a decoded local bitmap, and VLC would only paint it.
        Dim guessed_Extension As String = MediaUrl.ExtensionOf(url)
        If guessed_Extension.Length > 0 AndAlso Image_File_Extensions.Contains(guessed_Extension) Then
            ViewerBox.Show(Me,
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
        EnterStreamDisplayState()

        lbl_Status.Text = Localization.T("Подключение..")
        ' Refresh, not DoEvents: all this needs is for the label to be painted before the
        ' connect, and DoEvents dispatches EVERYTHING - a queued keypress could reenter
        ' navigation from inside this call.
        lbl_Status.Refresh()

        PlayVideoWithVlcAsync(url)
    End Sub

    ''' <summary>
    ''' True while a stream from "Open URL" is on screen. The folder's file is still
    ''' Current_File_Name underneath - the stream belongs to no folder - so every file verb
    ''' refuses while this is set (ResourceBlocksFileOperations), the way an archive does.
    ''' Cleared by the next navigation (NoteLeavingCurrentMedia).
    ''' </summary>
    Private is_Stream_On_Screen As Boolean

    Friend Function IsStreamOnScreen() As Boolean
        Return is_Stream_On_Screen
    End Function

    ''' <summary>
    ''' Gives the stream a display state of its own (audit 050 D-05). OpenMediaUrl used to
    ''' change nothing but the player: with a stream on screen, DEL, the slot keys and the
    ''' recipients overlay acted on the photo shown BEFORE the URL - unseen, and silently
    ''' with "ask only when the file will not reach the Recycle Bin"; over an audio file the
    ''' stream's picture stayed hidden under the previous track's plate, and the old track's
    ''' pending tag parse still landed.
    ''' </summary>
    Private Sub EnterStreamDisplayState()
        ' The outgoing video's position, while its name is still known.
        If is_Vlc_Playing Then RememberCurrentVideoPosition()
        ' Anything still in flight for the previous file is stale from here on.
        media_Generation += 1
        ' The shared player may hold an animation video (spec 242): let go of it before the stream takes the player.
        StopAnimationVideo()
        ' ...and so is any argument or resume probe still out: the stream is what was asked for.
        open_Coordinator.NoteNavigation()
        ' A container's decrypting stream is what VLC may have been reading: stop first,
        ' then let the container go (the order UpdateCurrentFileAndDisplay keeps).
        If IsSecureContentOnScreen() Then
            If is_Vlc_Playing Then StopVlcPlayback()
            SecureReleaseCurrent()
        End If
        ClearAudioSurface()
        ' A stream is shown as video, whatever it carries: its picture goes on the VideoView,
        ' and the audio plate, the audio sleep timer and the audio end action stay with files.
        SetMediaDisplayKind(MediaKind.Video)
        is_Stream_On_Screen = True
    End Sub

End Class
#End If
