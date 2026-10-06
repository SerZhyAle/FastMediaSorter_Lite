#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §1, §6, §7, §8.
' Main_Form integration for Live Broadcast reception and playback.

Partial Public Class Main_Form

    Private live_Broadcast_Active As Boolean = False
    Private live_Playback_Session As LiveHttpPlaybackSession
    Private live_Reconnect_Policy As New LiveReconnectPolicy()
    Private live_Current_Descriptor As LiveBroadcastDescriptor
    Private live_Current_Endpoint As LiveBroadcastEndpoint
    Private live_Reconnect_Cts As CancellationTokenSource
    Private WithEvents live_Reconnect_Timer As New System.Windows.Forms.Timer()

    ''' <summary>
    ''' Indicates whether a live audio broadcast session is currently active.
    ''' </summary>
    Friend Function IsLiveBroadcastActive() As Boolean
        Return live_Broadcast_Active
    End Function

    ''' <summary>
    ''' Shows the "Open live broadcast.." dialog.
    ''' </summary>
    Friend Sub ShowLiveBroadcastDialog()
        SlideShowStop()
        Using dlg As New LiveBroadcast_Form()
            If DialogLayer.ShowViewerDialog(dlg, Me) = DialogResult.OK AndAlso dlg.ResultDescriptor IsNot Nothing Then
                PlayLiveBroadcast(dlg.ResultDescriptor)
            End If
        End Using
    End Sub

    ''' <summary>
    ''' Attempts to route a command line argument or drag-dropped path to the live broadcast parser.
    ''' Returns True if handled.
    ''' </summary>
    Friend Function TryHandleLiveBroadcastArgument(argumentText As String) As Boolean
        If String.IsNullOrWhiteSpace(argumentText) Then Return False
        Dim trimmed As String = argumentText.Trim().Trim(""""c)

        ' 1. File path ending with .fmsbcast (§4.2)
        If trimmed.EndsWith(".fmsbcast", StringComparison.OrdinalIgnoreCase) Then
            Dim fileRes As LiveBroadcastReadResult = LiveBroadcastReader.ReadFile(trimmed)
            If fileRes.Status = LiveReadStatus.Ok AndAlso fileRes.Descriptor IsNot Nothing Then
                Dim store As New LiveBroadcastStore()
                store.Upsert(fileRes.Descriptor)
                PlayLiveBroadcast(fileRes.Descriptor)
                Return True
            Else
                ShowLiveBroadcastError(fileRes.Status)
                Return True
            End If
        End If

        ' 2. Barcode text, intent URL, or fmsbcast link (§4.1, §4.3)
        If trimmed.StartsWith("FMSBCAST1:", StringComparison.OrdinalIgnoreCase) OrElse
           trimmed.StartsWith("intent:", StringComparison.OrdinalIgnoreCase) OrElse
           trimmed.StartsWith("fmsbcast:", StringComparison.OrdinalIgnoreCase) Then

            Dim readRes As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(trimmed)
            If readRes.Status <> LiveReadStatus.Ok Then
                readRes = LiveBroadcastLinkReader.ReadLink(trimmed)
            End If

            If readRes.Status = LiveReadStatus.Ok AndAlso readRes.Descriptor IsNot Nothing Then
                Dim store As New LiveBroadcastStore()
                store.Upsert(readRes.Descriptor)
                PlayLiveBroadcast(readRes.Descriptor)
                Return True
            Else
                ShowLiveBroadcastError(readRes.Status)
                Return True
            End If
        End If

        Return False
    End Function

    ''' <summary>
    ''' Starts playback of a live broadcast descriptor.
    ''' </summary>
    Friend Sub PlayLiveBroadcast(descriptor As LiveBroadcastDescriptor)
        If descriptor Is Nothing Then Return

        Dim endpoint As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(descriptor)
        If endpoint Is Nothing Then
            lbl_Status.Text = Localization.T("Этот режим пока не поддерживается")
            Return
        End If

        live_Current_Descriptor = descriptor
        live_Current_Endpoint = endpoint
        live_Reconnect_Policy = New LiveReconnectPolicy()

        EnterLiveBroadcastDisplayState()
        ConnectLiveBroadcastSession()
    End Sub

    Private Sub EnterLiveBroadcastDisplayState()
        If is_Vlc_Playing Then RememberCurrentVideoPosition()
        media_Generation += 1
        open_Coordinator.NoteNavigation()

        If IsSecureContentOnScreen() Then
            If is_Vlc_Playing Then StopVlcPlayback()
            SecureReleaseCurrent()
        End If

        ClearAudioSurface()
        SetMediaDisplayKind(MediaKind.Audio)

        is_Stream_On_Screen = True
        live_Broadcast_Active = True

        ' Live audio uses Picture_Box_1 for artwork/visualizer
        is_PictureBox1_Visible = True
        is_PictureBox2_Visible = False
        Web_Browser.Visible = False
        Picture_Box_1.Visible = True
        Picture_Box_2.Visible = False

        vlc_Playing_Path = live_Current_Endpoint.Url
        current_Loaded_File_Name = live_Current_Endpoint.Url
    End Sub

    Friend Sub ExitLiveBroadcastDisplayState()
        live_Broadcast_Active = False
        live_Reconnect_Timer.Stop()

        If live_Reconnect_Cts IsNot Nothing Then
            live_Reconnect_Cts.Cancel()
            live_Reconnect_Cts.Dispose()
            live_Reconnect_Cts = Nothing
        End If

        If live_Playback_Session IsNot Nothing Then
            live_Playback_Session.Dispose()
            live_Playback_Session = Nothing
        End If
    End Sub

    Private Async Sub ConnectLiveBroadcastSession()
        If Not live_Broadcast_Active OrElse live_Current_Endpoint Is Nothing Then Return

        Dim generation As Integer = media_Generation

        ' Cancel previous reconnect CTS
        If live_Reconnect_Cts IsNot Nothing Then
            live_Reconnect_Cts.Cancel()
            live_Reconnect_Cts.Dispose()
            live_Reconnect_Cts = Nothing
        End If
        live_Reconnect_Cts = New CancellationTokenSource()
        Dim token As CancellationToken = live_Reconnect_Cts.Token

        ' Dispose previous session before opening a new connection (§6.2 item 4)
        If live_Playback_Session IsNot Nothing Then
            live_Playback_Session.Dispose()
            live_Playback_Session = Nothing
        End If

        If Not Await EnsureVlcInitializedAsync() Then
            If generation <> media_Generation Then Return
            lbl_Status.Text = OptionalRuntimeManager.GetVlcUnavailableStatusText()
            Return
        End If

        If generation <> media_Generation Then Return

        live_Reconnect_Policy.NoteConnecting()
        lbl_Status.Text = Localization.T("Подключение..")
        lbl_Status.Refresh()

        Dim session As New LiveHttpPlaybackSession(live_Current_Endpoint, libVlc)
        live_Playback_Session = session
        AddHandler session.Stalled, Sub() OnLiveSessionStalled(generation)

        Dim media As LibVLCSharp.Shared.Media = Await session.OpenAsync(token)

        If generation <> media_Generation OrElse Not live_Broadcast_Active Then
            session.Dispose()
            Return
        End If

        If media IsNot Nothing Then
            Try
                ' Setup player surface
                vlc_Video_View.Location = Picture_Box_1.Location
                vlc_Video_View.Size = Picture_Box_1.Size
                vlc_Video_View.Visible = False ' audio surface paints Picture_Box_1
                KeepRecipientsOverlayOnTop()

                ' Ticket 115: a pool-thread stop may still be inside the player; Play is legal only after it left.
                WaitForPendingVlcStop()
                vlc_Media_Player.Play(media)
                media.Dispose()

                ApplyVideoAudioStateToVlc()
                is_Vlc_Playing = True
                vlc_Play_Generation = generation

                live_Reconnect_Policy.NotePlaying()
                BeginAudioPlayback()
                ShowVideoControls()

                Dim titleText As String = If(Not String.IsNullOrEmpty(live_Current_Descriptor.Title),
                                             live_Current_Descriptor.Title,
                                             live_Current_Endpoint.Url)
                lbl_Status.Text = Localization.TF("LIVE - {0}", titleText)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " Live broadcast playing: " & live_Current_Endpoint.Url)
            Catch ex As Exception
                AppFileLogger.LogException("Failed to start VLC live media playback", ex)
                OnLiveSessionStalled(generation)
            End Try
        Else
            ' Session open returned Nothing
            HandleSessionOpenFailure(session, generation)
        End If
    End Sub

    Private Sub HandleSessionOpenFailure(session As LiveHttpPlaybackSession, generation As Integer)
        If generation <> media_Generation OrElse Not live_Broadcast_Active Then Return

        Dim reason As LiveTerminationKind = session.TerminationReason
        If reason = LiveTerminationKind.MaxListeners503 OrElse
           reason = LiveTerminationKind.AccessDenied OrElse
           reason = LiveTerminationKind.NotFoundOrGone OrElse
           reason = LiveTerminationKind.NotAudioStream OrElse
           reason = LiveTerminationKind.UnsupportedMode OrElse
           reason = LiveTerminationKind.UnsupportedVersion OrElse
           reason = LiveTerminationKind.LoopbackAddress Then

            live_Reconnect_Policy.NoteFatalTermination(reason, session.CustomErrorMessage)
            lbl_Status.Text = live_Reconnect_Policy.GetTerminalStatusText()
            StopLivePlayback(isUserStop:=False)
            Return
        End If

        ' Trigger reconnect backoff (§7)
        ScheduleReconnect(generation)
    End Sub

    Private Sub OnLiveSessionStalled(generation As Integer)
        If Me.InvokeRequired Then
            Me.BeginInvoke(New Action(Sub() OnLiveSessionStalled(generation)))
            Return
        End If

        If generation <> media_Generation OrElse Not live_Broadcast_Active Then Return

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " Live broadcast session stalled / ended. Scheduling reconnect.")

        ' Stop player and dispose old HTTP session before reconnect (§7 item 1)
        If vlc_Media_Player IsNot Nothing Then
            Try
                WaitForPendingVlcStop()
                vlc_Media_Player.Stop()
                vlc_Media_Player.Media = Nothing
            Catch
            End Try
        End If

        If live_Playback_Session IsNot Nothing Then
            live_Playback_Session.Dispose()
            live_Playback_Session = Nothing
        End If

        ScheduleReconnect(generation)
    End Sub

    Private Sub ScheduleReconnect(generation As Integer)
        If generation <> media_Generation OrElse Not live_Broadcast_Active Then Return

        Dim backoffMs As Integer? = live_Reconnect_Policy.GetNextBackoffDelayMs()
        If backoffMs.HasValue Then
            lbl_Status.Text = Localization.TF("Переподключение {0}/5..", live_Reconnect_Policy.RetryCount)
            lbl_Status.Refresh()

            live_Reconnect_Timer.Stop()
            live_Reconnect_Timer.Interval = Math.Max(50, backoffMs.Value)
            live_Reconnect_Timer.Start()
        Else
            ' Budget exhausted (§7)
            lbl_Status.Text = live_Reconnect_Policy.GetTerminalStatusText()
            StopLivePlayback(isUserStop:=False)
        End If
    End Sub

    Private Sub live_Reconnect_Timer_Tick(sender As Object, e As EventArgs) Handles live_Reconnect_Timer.Tick
        live_Reconnect_Timer.Stop()
        If live_Broadcast_Active Then
            ConnectLiveBroadcastSession()
        End If
    End Sub

    ''' <summary>
    ''' Stops live broadcast playback cleanly.
    ''' </summary>
    Friend Sub StopLivePlayback(Optional isUserStop As Boolean = True)
        live_Reconnect_Timer.Stop()

        If isUserStop AndAlso live_Reconnect_Policy IsNot Nothing Then
            live_Reconnect_Policy.NoteUserStopped()
        End If

        If live_Reconnect_Cts IsNot Nothing Then
            live_Reconnect_Cts.Cancel()
            live_Reconnect_Cts.Dispose()
            live_Reconnect_Cts = Nothing
        End If

        If vlc_Media_Player IsNot Nothing Then
            Try
                WaitForPendingVlcStop()
                vlc_Media_Player.Stop()
                vlc_Media_Player.Media = Nothing
            Catch
            End Try
        End If

        If live_Playback_Session IsNot Nothing Then
            live_Playback_Session.Dispose()
            live_Playback_Session = Nothing
        End If

        is_Vlc_Playing = False
        HideVideoControls()
    End Sub

    ''' <summary>
    ''' Reconnects to the current live broadcast starting a fresh budget from scratch (§7).
    ''' </summary>
    Friend Sub ReconnectLiveBroadcast()
        If live_Current_Descriptor IsNot Nothing Then
            PlayLiveBroadcast(live_Current_Descriptor)
        End If
    End Sub

    Private Sub ShowLiveBroadcastError(status As LiveReadStatus)
        Select Case status
            Case LiveReadStatus.UnsupportedVersion
                lbl_Status.Text = Localization.T("Требуется обновить приложение для этой трансляции")
            Case LiveReadStatus.UnsupportedMode
                lbl_Status.Text = Localization.T("Этот режим пока не поддерживается")
            Case LiveReadStatus.LoopbackHost
                lbl_Status.Text = Localization.T("Устройство передало локальный адрес (loopback), недоступный с ПК")
            Case LiveReadStatus.TooLarge
                lbl_Status.Text = Localization.T("Превышен максимальный размер дескриптора (64 КБ)")
            Case LiveReadStatus.InvalidDocument
                lbl_Status.Text = Localization.T("Неверный формат трансляции или повреждённые данные")
            Case Else
                lbl_Status.Text = Localization.T("Не удалось распознать трансляцию")
        End Select
    End Sub

End Class
#End If
