Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

' The "i" item of the first panel. It used to open a mailto: to the author - the user's
' mail client (Outlook, whenever it was registered) springing open on an information
' button. The author stays reachable where he always was - the project link of the
' settings window's About page - and "i" answers for the file on screen instead:
' the "about file" window of File_Details_Form, built from the pure model in
' FileDetails.vb. This file is the bridge: it resolves the file being shown, owns the
' window, and - modern build only - reads the audio and video tracks through libvlc.

Partial Public Class Main_Form

    Private file_Details_Dialog As File_Details_Form

    ''' <summary>Opens the details of the file on screen. One window at a time: the
    ''' details are a SNAPSHOT of the moment of the click, so a second click closes the
    ''' old one and opens a fresh window rather than stacking stale copies.</summary>
    Friend Sub ShowFileDetailsDialog()
        Dim path As String = Current_Image_Path
        If String.IsNullOrWhiteSpace(path) Then
            lbl_Status.Text = Localization.T("Файл не выбран")
            Return
        End If
        If Not File.Exists(path) Then
            ' An archive entry's temporary copy, a remote URL or a file that vanished
            ' between display and click: nothing on disk to read, and the status line
            ' says so instead of a window full of empty rows.
            lbl_Status.Text = Localization.T("Файл недоступен")
            Return
        End If
        If file_Details_Dialog IsNot Nothing AndAlso Not file_Details_Dialog.IsDisposed Then
            file_Details_Dialog.Close()
        End If
#If Not NETFRAMEWORK Then
        file_Details_Dialog = New File_Details_Form(path, AddressOf BuildMediaDetailSectionsAsync)
        ' Through the dialog layer: a bare Show(Me) opened the window BEHIND a viewer pinned "on top" (124 UX-201).
        DialogLayer.ShowViewerWindow(file_Details_Dialog, Me)
#Else
        file_Details_Dialog = New File_Details_Form(path)
        file_Details_Dialog.Show(Me)
#End If
    End Sub

#If Not NETFRAMEWORK Then

    ''' <summary>
    ''' The audio and video track rows of the details window, through libvlc.
    '''
    ''' Ticket 115's ownership rules apply to this parse exactly as they apply to the
    ''' audio plate's (Main_Form.AudioView.vb): counted in the native lease (nothing
    ''' enters once the window is closing, the instance dies only at zero), one parse at
    ''' a time behind the SAME gate as the plate's, and the Media is released only after
    ''' the continuation has left the libvlc thread that raised the parsed event.
    ''' </summary>
    Friend Async Function BuildMediaDetailSectionsAsync(filePath As String) As Task(Of List(Of FileDetailSection))
        If IsRemoteMediaUrl(filePath) Then Return Nothing
        ' The first media file can get here before libvlc exists; wait for the same
        ' initialisation the player awaits, never a second one.
        If libVlc Is Nothing Then
            If Not Await EnsureVlcInitializedAsync().ConfigureAwait(True) Then Return Nothing
        End If
        If Not vlc_Lease.TryEnter() Then Return Nothing
        Try
            Await audio_Parse_Gate.WaitAsync().ConfigureAwait(False)
            Try
                If libVlc Is Nothing Then Return Nothing
                Return Await Task.Run(Function() ParseOneMediaDetailsAsync(filePath)).ConfigureAwait(False)
            Finally
                audio_Parse_Gate.Release()
            End Try
        Finally
            vlc_Lease.Leave()
        End Try
    End Function

    Private Async Function ParseOneMediaDetailsAsync(filePath As String) As Task(Of List(Of FileDetailSection))
        ' The file name only, never an address and never a folder (ticket 116).
        AppFileLogger.WriteLine("Reading media details: " & Path.GetFileName(filePath))
        Using media As LibVLCSharp.Shared.Media = CreateVlcMedia(filePath)
            Dim status As LibVLCSharp.Shared.MediaParsedStatus =
                Await media.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal Or
                                  LibVLCSharp.Shared.MediaParseOptions.FetchLocal,
                                  Audio_Parse_Timeout_Ms).ConfigureAwait(False)
            ' Ticket 115: the continuation of that await runs INLINE on the libvlc
            ' thread that raised the parsed event; everything after it - reading the
            ' tracks and releasing the Media - must happen off that thread.
            Await Task.Yield()
            If status <> LibVLCSharp.Shared.MediaParsedStatus.Done Then Return Nothing
            Return MediaSectionsFromParse(media)
        End Using
    End Function

    ''' <summary>Maps one parsed media to the window's sections: the tags first, then one
    ''' block per video and audio track. Only managed data is touched here, so it is a
    ''' plain Function; the formatters it uses are the tested ones of FileDetails.</summary>
    Private Shared Function MediaSectionsFromParse(media As LibVLCSharp.Shared.Media) As List(Of FileDetailSection)
        Dim sections As New List(Of FileDetailSection)()

        Dim tags As New FileDetailSection("Медиа")
        tags.Add("Название", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Title))
        tags.Add("Исполнитель", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Artist))
        tags.Add("Альбом", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Album))
        tags.Add("Год", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Date))
        tags.Add("Трек", MediaMeta(media, LibVLCSharp.Shared.MetadataType.TrackNumber))
        tags.Add("Жанр", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Genre))
        tags.Add("Режиссёр", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Director))
        tags.Add("Сериал", MediaMeta(media, LibVLCSharp.Shared.MetadataType.ShowName))
        tags.Add("Сезон", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Season))
        tags.Add("Серия", MediaMeta(media, LibVLCSharp.Shared.MetadataType.Episode))
        If media.Duration > 0 Then tags.Add("Длительность", FileDetails.FormatDuration(media.Duration))
        If Not tags.IsEmpty Then sections.Add(tags)

        Dim video As New FileDetailSection("Видео")
        Dim audio As New FileDetailSection("Аудио")
        If media.Tracks IsNot Nothing Then
            For Each track As LibVLCSharp.Shared.MediaTrack In media.Tracks
                If track.TrackType = LibVLCSharp.Shared.TrackType.Video AndAlso video.IsEmpty Then
                    ' VideoTrack/AudioTrack are STRUCTS in LibVLCSharp - there is no null
                    ' to check; a file that carries no video field simply reports zeroes.
                    Dim size As LibVLCSharp.Shared.VideoTrack = track.Data.Video
                    Dim width As Long = CLng(size.Width)
                    Dim height As Long = CLng(size.Height)
                    If width > 0 AndAlso height > 0 Then
                        video.Add("Разрешение", width.ToString(CultureInfo.InvariantCulture) & " × " &
                                                     height.ToString(CultureInfo.InvariantCulture))
                    End If
                    Dim frames As Long = CLng(size.FrameRateNum)
                    Dim frameBase As Long = CLng(size.FrameRateDen)
                    If frames > 0 AndAlso frameBase > 0 Then
                        video.Add("Кадров в секунду",
                                  (frames / CDbl(frameBase)).ToString("0.###", CultureInfo.InvariantCulture))
                    End If
                    video.Add("Кодек", FileDetails.VideoCodecName(track.Codec))
                    video.Add("Битрейт", FileDetails.FormatBitrate(CLng(track.Bitrate)))
                ElseIf track.TrackType = LibVLCSharp.Shared.TrackType.Audio AndAlso audio.IsEmpty Then
                    Dim sound As LibVLCSharp.Shared.AudioTrack = track.Data.Audio
                    audio.Add("Кодек", AudioMetadata.CodecName(track.Codec))
                    If sound.Channels > 0 Then
                        audio.Add("Каналы", CLng(sound.Channels).ToString(CultureInfo.InvariantCulture))
                    End If
                    If sound.Rate > 0 Then
                        audio.Add("Частота дискретизации", CLng(sound.Rate).ToString(CultureInfo.InvariantCulture) & " Hz")
                    End If
                    audio.Add("Битрейт", FileDetails.FormatBitrate(CLng(track.Bitrate)))
                End If
            Next
        End If
        ' The blocks come after the tags and keep the window's order: file, image,
        ' document, media. An empty block (a music file has no video track) is skipped
        ' by the window itself.
        If Not video.IsEmpty Then sections.Add(video)
        If Not audio.IsEmpty Then sections.Add(audio)
        Return sections
    End Function

#End If
End Class
