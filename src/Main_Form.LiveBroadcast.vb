#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md sections 1, 6, 7, 8.
' Main_Form integration for live broadcast reception. The loop that keeps the listener alive - one request at a
' time, backoff, the budget, the drift guard - is LiveBroadcastRunner (pure, tested against a mock device); what
' lives here is only what needs the window: the audio surface, the player, the status line, the control bar.

Partial Public Class Main_Form

    Private Function AskLivePassword() As String
        Dim question As New PromptSpec(Localization.T("Открыть live-трансляцию"), Localization.T("Пароль:"), "",
                                       Localization.T("Подключиться"), Nothing) With {.IsPassword = True}
        Return AskDialogs.Prompt(Me, question)
    End Function

    Private WithEvents live_Cast_Timer As New System.Windows.Forms.Timer() With {.Interval = 2000}
    Private ReadOnly live_Cast_Cancellation As New CancellationTokenSource()
    Private live_Cast_Busy As Boolean

    Private Sub StartCastReceiver(sender As Object, e As EventArgs) Handles MyBase.Shown
        If modern_Preferences IsNot Nothing Then live_Options.AutoAcceptCast = modern_Preferences.AutoAcceptLiveCast
        If ExchangeFeatures.IsEnabled() Then live_Cast_Timer.Start()
    End Sub

    Private Sub CloseCastReceiver(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        live_Cast_Timer.Dispose()
        live_Cast_Cancellation.Cancel()
    End Sub

    Private Async Sub PollCastOffer(sender As Object, e As EventArgs) Handles live_Cast_Timer.Tick
        If live_Cast_Busy OrElse Not ExchangeFeatures.IsEnabled() OrElse Not ExchangeAccountStore.CreateDefault().IsConfigured() Then Return
        live_Cast_Busy = True
        Try
            Dim client As New ExchangeAgentClient()
            Dim reply As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestGetCastOffer), 3000, live_Cast_Cancellation.Token)
            If IsDisposed OrElse Not reply.IsOk OrElse reply.DataJson = "null" Then Return
            Using doc As JsonDocument = JsonDocument.Parse(reply.DataJson)
                Dim stopId As JsonElement = Nothing
                If doc.RootElement.TryGetProperty("stopBroadcastId", stopId) AndAlso stopId.ValueKind = JsonValueKind.String Then
                    If live_Current_Descriptor IsNot Nothing AndAlso live_Current_Descriptor.BroadcastId = stopId.GetString() Then StopLivePlayback()
                    Return
                End If
            End Using
            Dim record As ExchangeBroadcastRecord = Nothing
            If Not ExchangeRecordReader.TryReadBroadcast(reply.DataJson, record) Then Return
            Dim parsed As LiveBroadcastReadResult = LiveBroadcastRecords.ReadRecord(record.Raw)
            If parsed.Status <> LiveReadStatus.Ok Then
                ' LIVE-BROADCAST 1.1: an offer whose descriptor this program cannot read (a later schema included) is refused WITH the
                ' reason on screen - the sender still gets its "unsupported" answer below, but the person at this PC is told, not left guessing.
                LiveBroadcastMessages.LogRefusal(parsed.Status, "the cast offer")
                ShowLiveBroadcastError(parsed.Status)
            End If
            Dim accepted As Boolean = parsed.Status = LiveReadStatus.Ok
            If accepted AndAlso Not live_Options.AutoAcceptCast Then
                Dim question As New AskSpec(Localization.T("Открыть live-трансляцию"), Localization.T("Принять входящую трансляцию?"), AskIcon.None,
                    New AskButton() {New AskButton(Localization.T("Подключиться")), New AskButton(Localization.T("Отмена"))}, 1, 1)
                accepted = AskDialogs.Choose(Me, question) = 0
            End If
            If accepted Then
                Dim sealError As String = Await LiveBroadcastRecords.PrepareAsync(parsed.Descriptor, live_Cast_Cancellation.Token)
                If sealError.Length > 0 Then
                    accepted = False
                    Notify(ConnectionDiagnosisLocalization.GetMessageText(ExchangeAgentPipe.ErrorMessage(sealError).Code), NotifyLevel.Warning)
                End If
            End If
            If accepted AndAlso parsed.Descriptor.AuthScheme.Length > 0 AndAlso parsed.Descriptor.Password Is Nothing Then
                parsed.Descriptor.Password = AskLivePassword()
                accepted = parsed.Descriptor.Password IsNot Nothing
            End If
            Dim answer As ExchangeAgentReply = Await client.SendAsync(ExchangeAgentPipe.BuildRequest(ExchangeAgentPipe.RequestCastAnswer,
                Sub(o)
                    o("broadcastId") = record.BroadcastId
                    o("accepted") = accepted
                    o("reason") = If(parsed.Status = LiveReadStatus.Ok, "declined", "unsupported")
                End Sub), 3000, live_Cast_Cancellation.Token)
            If accepted AndAlso answer.IsOk AndAlso Not IsDisposed Then PlayLiveBroadcast(parsed.Descriptor)
        Catch ex As OperationCanceledException
        Catch ex As JsonException
            AppFileLogger.WriteLine("Live cast: malformed local reply")
        Finally
            live_Cast_Busy = False
        End Try
    End Sub

    ''' <summary>A live session is on screen: connecting, playing, reconnecting, stopped or failed. Cleared when the viewer moves on.</summary>
    Private live_Broadcast_Active As Boolean = False

    ''' <summary>The runner has not returned yet: the transport button means "Stop", otherwise "Connect again".</summary>
    Private live_Runner_Running As Boolean = False

    Private live_Runner As LiveBroadcastRunner
    Private live_Runner_Cts As CancellationTokenSource
    Private live_Current_Descriptor As LiveBroadcastDescriptor
    Private live_Current_Endpoint As LiveBroadcastEndpoint
    Private live_Vlc_Media As LiveVlcMedia
    Private live_Options As New LivePlaybackOptions()
    Private WithEvents live_Drift_Timer As New System.Windows.Forms.Timer() With {.Interval = 1000}

    ''' <summary>
    ''' Indicates whether a live audio broadcast session is currently on screen.
    ''' </summary>
    Friend Function IsLiveBroadcastActive() As Boolean
        Return live_Broadcast_Active
    End Function

    ''' <summary>True while the loop is alive (a connection, or waiting to try again): the bar then offers Stop.</summary>
    Friend Function IsLiveBroadcastRunning() As Boolean
        Return live_Broadcast_Active AndAlso live_Runner_Running
    End Function

    ''' <summary>
    ''' Shows the "Open live broadcast.." dialog.
    ''' </summary>
    Friend Sub ShowLiveBroadcastDialog()
        SlideShowStop()
        Using dlg As New LiveBroadcast_Form(live_Options.AutoAcceptCast)
            If DialogLayer.ShowViewerDialog(dlg, Me) = DialogResult.OK Then
                If dlg.ResultDescriptor IsNot Nothing Then
                    PlayLiveBroadcast(dlg.ResultDescriptor)
                ElseIf Not String.IsNullOrEmpty(dlg.ResultOnDemandUrl) Then
                    ' The user did not mark the address as live: the ordinary "Open URL.." path, unchanged.
                    OpenMediaUrl(dlg.ResultOnDemandUrl)
                End If
            End If
            live_Options.AutoAcceptCast = dlg.AutoAcceptCast
            If modern_Preferences IsNot Nothing AndAlso modern_Preferences.AutoAcceptLiveCast <> dlg.AutoAcceptCast Then
                modern_Preferences.AutoAcceptLiveCast = dlg.AutoAcceptCast
                modern_Preferences.Save()
            End If
        End Using
    End Sub

    ''' <summary>
    ''' Routes a command-line argument, a forwarded launch or a drop to the live-broadcast parser - the one door of
    ''' section 4. Returns True when the argument was one of the three hand-off forms (read, refused or played).
    ''' </summary>
    Friend Function TryHandleLiveBroadcastArgument(argumentText As String) As Boolean
        Dim result As LiveBroadcastReadResult = LiveBroadcastIngress.TryRead(argumentText)
        If result Is Nothing Then Return False

        If result.Status = LiveReadStatus.Ok AndAlso result.Descriptor IsNot Nothing Then
            ' An imported descriptor is live whatever it says about isLive (section 1 item 5), and is remembered
            ' before it plays: a failed write only costs the recent list, never the broadcast (section 5 rule 4).
            Dim store As New LiveBroadcastStore()
            store.Upsert(result.Descriptor)
            PlayLiveBroadcast(result.Descriptor)
        Else
            ShowLiveBroadcastError(result.Status)
        End If
        Return True
    End Function

    ''' <summary>
    ''' Starts playback of a live broadcast descriptor. A session already on screen is replaced; the budget of the
    ''' new one starts from nothing.
    ''' </summary>
    Friend Sub PlayLiveBroadcast(descriptor As LiveBroadcastDescriptor)
        If descriptor Is Nothing Then Return

        If descriptor.AuthScheme.Length > 0 AndAlso descriptor.Password Is Nothing Then
            Dim password As String = AskLivePassword()
            If password Is Nothing Then Return
            descriptor.Password = password
        End If

        Dim endpoint As LiveBroadcastEndpoint = LiveEndpointSelector.SelectEndpoint(descriptor)
        If endpoint Is Nothing Then
            lbl_Status.Text = Localization.T("Этот режим пока не поддерживается")
            Return
        End If

        If Not ReferenceEquals(live_Current_Descriptor, descriptor) Then ClearLivePassword()
        CancelLiveRunner(finishVisibly:=False)
        live_Current_Descriptor = descriptor
        live_Current_Endpoint = endpoint

        EnterLiveBroadcastDisplayState()
        StartLiveRunner()
    End Sub

    Private Sub EnterLiveBroadcastDisplayState()
        ' The outgoing video's position, while its name is still known (a live stream never has one: see
        ' CanRememberPositionFor - an address is not a file).
        If is_Vlc_Playing Then RememberCurrentVideoPosition()
        media_Generation += 1
        open_Coordinator.NoteNavigation()
        ' The shared player may hold an animation video (spec 242): let go of it before the broadcast takes the player.
        StopAnimationVideo()

        If IsSecureContentOnScreen() Then
            If is_Vlc_Playing Then StopVlcPlayback()
            SecureReleaseCurrent()
        End If

        ' Nothing of the previous media's playback state may reach a broadcast: no stale seek, no rate, no A-B marks.
        ClearPendingVideoPosition()
        ResetPlaybackExtrasForNewMedia()

        ClearAudioSurface()
        Dim audioOnly As Boolean = live_Current_Descriptor.ModeValue = LiveBroadcastMode.AudioOnly
        SetMediaDisplayKind(If(audioOnly, MediaKind.Audio, MediaKind.Video))

        is_Stream_On_Screen = True
        live_Broadcast_Active = True

        ' Live audio uses Picture_Box_1 for artwork/visualizer
        is_PictureBox1_Visible = audioOnly
        is_PictureBox2_Visible = False
        is_WebBrowser_Visible = False
        Web_Browser.Visible = False
        Picture_Box_1.Visible = audioOnly
        Picture_Box_2.Visible = False

        vlc_Playing_Path = live_Current_Endpoint.Url
        current_Loaded_File_Name = live_Current_Endpoint.Url
        ' The bar and the control timers key on this: while the live surface is up the player is "owned", even
        ' between two connections, so "Connect again" has a bar to live on.
        is_Vlc_Playing = True
    End Sub

    ''' <summary>
    ''' The viewer is leaving the broadcast (another file, another stream, close). The connection is closed at once -
    ''' that is what releases a read blocked inside LibVLC - and the loop is told to end. The player itself is
    ''' stopped by whatever takes the screen next; a late cleanup of the loop never touches a player that is
    ''' already someone else's (see StopLivePlayerAsync).
    ''' </summary>
    Friend Sub ExitLiveBroadcastDisplayState()
        live_Broadcast_Active = False
        live_Runner_Running = False
        CancelLiveRunner(finishVisibly:=False)
        ClearLivePassword()
    End Sub

    Private Sub ClearLivePassword()
        If live_Current_Descriptor Is Nothing Then Return
        live_Current_Descriptor.Password = Nothing
        For Each ep As LiveBroadcastEndpoint In live_Current_Descriptor.Endpoints
            ep.Password = Nothing
        Next
    End Sub

    ''' <summary>
    ''' Ends the loop and closes its connection. <paramref name="finishVisibly"/> keeps the loop's bookkeeping so that,
    ''' when it returns, the status line and the bar say it stopped (the user pressed Stop); without it the loop is
    ''' simply forgotten, because something else - another broadcast, another file - owns the screen now.
    ''' </summary>
    Private Sub CancelLiveRunner(finishVisibly As Boolean)
        live_Drift_Timer.Stop()
        Dim cts As CancellationTokenSource = live_Runner_Cts
        If Not finishVisibly Then live_Runner_Cts = Nothing
        If cts IsNot Nothing Then
            Try
                cts.Cancel()
            Catch ex As ObjectDisposedException
            End Try
        End If
        ' Closed NOW, not at the loop's next turn: the player may be blocked in a read of this very stream.
        Dim running As LiveBroadcastRunner = live_Runner
        If Not finishVisibly Then live_Runner = Nothing
        If running IsNot Nothing Then
            Dim session As LiveHttpPlaybackSession = running.CurrentSession
            If session IsNot Nothing Then session.Close(LiveStreamEnd.Closed)
        End If
    End Sub

    Private Async Sub StartLiveRunner()
        Dim generation As Integer = media_Generation

        If Not Await EnsureVlcInitializedAsync() Then
            If generation = media_Generation Then lbl_Status.Text = OptionalRuntimeManager.GetVlcUnavailableStatusText()
            Return
        End If
        If generation <> media_Generation OrElse Not live_Broadcast_Active Then Return

        Dim policy As New LiveReconnectPolicy()
        Dim target As Long? = LiveEndpointSelector.TargetLatencyOf(live_Current_Descriptor, live_Current_Endpoint)
        Dim runner As New LiveBroadcastRunner(live_Current_Endpoint, policy, New LiveHost(Me, generation), live_Options, target,
                                             endpoints:=LiveEndpointSelector.Candidates(live_Current_Descriptor))
        Dim cts As New CancellationTokenSource()
        live_Runner = runner
        live_Runner_Cts = cts
        live_Runner_Running = True
        live_Drift_Timer.Start()

        lbl_Status.Text = Localization.T("Подключение..")
        lbl_Status.Refresh()

        Dim ended As LiveRunEnd = LiveRunEnd.UserStopped
        Try
            ended = Await runner.RunAsync(cts.Token)
        Catch ex As Exception
            AppFileLogger.LogException("Live broadcast loop", ex)
            ended = LiveRunEnd.Failed
        End Try

        ' Another session (or another file) took the screen while this one was ending: it owns the status now.
        If generation <> media_Generation OrElse Not ReferenceEquals(live_Runner, runner) Then
            cts.Dispose()
            Return
        End If

        live_Runner_Running = False
        live_Drift_Timer.Stop()
        If IsDisposed OrElse Not live_Broadcast_Active Then
            cts.Dispose()
            Return
        End If

        If ended = LiveRunEnd.Failed Then
            If policy.TerminationReason = LiveTerminationKind.AccessDenied Then
                live_Current_Descriptor.Password = Nothing
                For Each ep As LiveBroadcastEndpoint In live_Current_Descriptor.Endpoints
                    ep.Password = Nothing
                Next
            End If
            Dim sealError As String = If(runner.LastOpen?.SealedState, "")
            Notify(If(sealError.Length > 0, ConnectionDiagnosisLocalization.GetMessageText(ExchangeAgentPipe.ErrorMessage(sealError).Code), policy.GetTerminalStatusText()), NotifyLevel.Warning)
        Else
            lbl_Status.Text = Localization.T("Трансляция остановлена")
        End If
        RefreshVideoControlsState()
        cts.Dispose()
    End Sub

    ''' <summary>The player side of the loop. Every call is on the UI thread: the loop was started there.</summary>
    Private NotInheritable Class LiveHost
        Implements ILiveRunnerHost

        Private ReadOnly form As Main_Form
        Private ReadOnly generation As Integer

        Public Sub New(form As Main_Form, generation As Integer)
            Me.form = form
            Me.generation = generation
        End Sub

        Public Function StartPlaybackAsync(session As LiveHttpPlaybackSession) As Task Implements ILiveRunnerHost.StartPlaybackAsync
            form.StartLivePlayer(session, generation)
            Return Task.CompletedTask
        End Function

        Public Function StopPlaybackAsync() As Task Implements ILiveRunnerHost.StopPlaybackAsync
            Return form.StopLivePlayerAsync(generation)
        End Function

        Public Sub OnState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long) Implements ILiveRunnerHost.OnState
            form.OnLiveState(state, retryCount, elapsedMs, generation)
        End Sub
    End Class

    ''' <summary>Hands the connection's body to LibVLC and starts it. The connection is already open (one GET).</summary>
    Private Sub StartLivePlayer(session As LiveHttpPlaybackSession, generation As Integer)
        If generation <> media_Generation OrElse Not live_Broadcast_Active OrElse IsDisposed Then Return
        If libVlc Is Nothing OrElse vlc_Media_Player Is Nothing Then Return

        live_Current_Endpoint = session.PlaybackEndpoint

        vlc_Play_Serial += 1
        Dim target As Long? = LiveEndpointSelector.TargetLatencyOf(live_Current_Descriptor, live_Current_Endpoint)
        Dim previousMedia As LiveVlcMedia = live_Vlc_Media
        live_Vlc_Media = LiveVlcMedia.Create(libVlc, session, live_Options, target)

        vlc_Video_View.Location = Picture_Box_1.Location
        vlc_Video_View.Size = Picture_Box_1.Size
        Dim audioOnly As Boolean = live_Current_Descriptor.ModeValue = LiveBroadcastMode.AudioOnly
        vlc_Video_View.Visible = Not audioOnly
        If Not audioOnly Then vlc_Video_View.BringToFront()
        KeepRecipientsOverlayOnTop()

        ' Ticket 115: a pool-thread stop may still be inside the player; Play is legal only after it left.
        WaitForPendingVlcStop()
        vlc_Media_Player.Play(live_Vlc_Media.Media)
        ' The player let go of the previous connection's media when it took this one; only now is it safe to free.
        If previousMedia IsNot Nothing Then previousMedia.Dispose()

        ApplyVideoAudioStateToVlc()
        is_Vlc_Playing = True
        vlc_Play_Generation = generation

        If audioOnly Then BeginAudioPlayback()
        ShowVideoControls()
    End Sub

    ''' <summary>
    ''' Stops the player and waits until it let go. The connection was closed first (by the loop), so a read the
    ''' player had blocked is released and Stop cannot wait on it. When the screen is no longer this broadcast's, the
    ''' player belongs to whatever replaced it and is left alone.
    ''' </summary>
    Private Async Function StopLivePlayerAsync(generation As Integer) As Task
        Dim ours As Boolean = generation = media_Generation AndAlso live_Broadcast_Active
        Dim player = vlc_Media_Player
        If Not ours OrElse player Is Nothing OrElse IsDisposed Then Return

        ' Ticket 115: the stop is native work on a pool thread. It is counted in, so the instance is not freed under
        ' it, and chained behind the previous stop, so two never run against one player.
        If Not vlc_Lease.TryEnter() Then Return
        Dim previous As Task = vlc_Pending_Stop
        Dim stopping As Task = Task.Run(Sub()
                                            Try
                                                If previous IsNot Nothing Then
                                                    Try : previous.Wait() : Catch : End Try
                                                End If
                                                player.Stop()
                                                player.Media = Nothing
                                            Catch
                                            Finally
                                                vlc_Lease.Leave()
                                            End Try
                                        End Sub)
        vlc_Pending_Stop = stopping

        ' Bounded: a stop that never returns must not hang the window (the same bound WaitForPendingVlcStop uses).
        Dim done As Task = Await Task.WhenAny(stopping, Task.Delay(3000))
        If done Is stopping Then
            DisposeLiveVlcMedia()
        Else
            AppFileLogger.WriteLine("Live broadcast: the player did not stop within 3 s; its media is left to the next stop")
        End If
    End Function

    ''' <summary>Frees the player's current live media. Only after the player was stopped or has taken another media -
    ''' LibVLC may still call into a stream input until then.</summary>
    Friend Sub DisposeLiveVlcMedia()
        Dim old As LiveVlcMedia = live_Vlc_Media
        live_Vlc_Media = Nothing
        If old IsNot Nothing Then old.Dispose()
    End Sub

    ''' <summary>A transition of the loop: logged with its monotonic time and generation (section 7), and shown.</summary>
    Private Sub OnLiveState(state As LiveSessionState, retryCount As Integer, elapsedMs As Long, generation As Integer)
        ' Never the address: it can carry a token, and current.log is what "Send logs" ships. What the last attempt came to
        ' and, when it is over, why - a state alone does not say.
        Dim running As LiveBroadcastRunner = live_Runner
        Dim why As String = ""
        If running IsNot Nothing Then
            If running.LastOpen IsNot Nothing Then why = ", open=" & running.LastOpen.Outcome.ToString() & If(running.LastOpen.HttpStatus > 0, " " & running.LastOpen.HttpStatus.ToString(Globalization.CultureInfo.InvariantCulture), "")
            If state = LiveSessionState.Failed OrElse state = LiveSessionState.Ended Then why &= ", reason=" & running.Policy.TerminationReason.ToString()
        End If
        AppFileLogger.WriteLine("Live broadcast: " & state.ToString() & ", retry " & retryCount.ToString(Globalization.CultureInfo.InvariantCulture) &
                                ", t=" & elapsedMs.ToString(Globalization.CultureInfo.InvariantCulture) & " ms, generation " &
                                generation.ToString(Globalization.CultureInfo.InvariantCulture) & why)
        If generation <> media_Generation OrElse Not live_Broadcast_Active Then Return

        Select Case state
            Case LiveSessionState.Connecting, LiveSessionState.Buffering
                If retryCount = 0 Then lbl_Status.Text = Localization.T("Подключение..")
            Case LiveSessionState.Playing
                Dim titleText As String = live_Current_Descriptor.Title
                If String.IsNullOrEmpty(titleText) Then titleText = MediaUrl.DisplayName(live_Current_Endpoint.Url)
                lbl_Status.Text = Localization.TF("LIVE - {0}", titleText)
            Case LiveSessionState.Reconnecting
                lbl_Status.Text = If(retryCount > 0,
                                     Localization.TF("Переподключение {0}/5..", retryCount),
                                     Localization.T("Подключение.."))
        End Select
    End Sub

    ''' <summary>The player reported the end of its media or an error for the broadcast on screen: the loop reconnects.</summary>
    Private Sub NotifyLivePlayerEnded(eventGeneration As Integer)
        If eventGeneration <> media_Generation OrElse Not live_Broadcast_Active Then Return
        Dim running As LiveBroadcastRunner = live_Runner
        If running IsNot Nothing Then running.NotifyPlayerEnded()
    End Sub

    ''' <summary>
    ''' Once a second: how far the player sits behind what has arrived. While the producer's clock and this PC's
    ''' differ, a session that never stalls still drifts; the guard rejoins the live edge when it does (LIVE-BROADCAST
    ''' rule 2, steady state).
    ''' </summary>
    Private Sub Live_Drift_Timer_Tick(sender As Object, e As EventArgs) Handles live_Drift_Timer.Tick
        Dim running As LiveBroadcastRunner = live_Runner
        If running Is Nothing OrElse Not live_Runner_Running OrElse vlc_Media_Player Is Nothing Then Return
        Try
            Dim played As Long = vlc_Media_Player.Time
            Dim playing As Boolean = vlc_Media_Player.IsPlaying
            If running.SampleDrift(played, playing) = LiveDriftAction.Rejoin Then
                AppFileLogger.WriteLine("Live broadcast: playback fell " & running.Drift.LastLagMs.ToString("0", Globalization.CultureInfo.InvariantCulture) &
                                        " ms behind the data received (limit " & running.Drift.ThresholdMs.ToString(Globalization.CultureInfo.InvariantCulture) &
                                        " ms); rejoining the live edge")
            End If
        Catch ex As Exception
            ' The player can be torn down between the check and the call.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " live drift sample skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Stops live broadcast playback: the loop ends, the connection and the player are released, and the bar offers
    ''' "Connect again". The surface stays, so the broadcast can be resumed from it.
    ''' </summary>
    Friend Sub StopLivePlayback(Optional isUserStop As Boolean = True)
        CancelLiveRunner(finishVisibly:=True)
    End Sub

    ''' <summary>Reconnects to the current live broadcast starting a fresh budget from scratch (section 7).</summary>
    Friend Sub ReconnectLiveBroadcast()
        If live_Current_Descriptor IsNot Nothing Then
            PlayLiveBroadcast(live_Current_Descriptor)
        End If
    End Sub

    Private Sub ShowLiveBroadcastError(status As LiveReadStatus)
        lbl_Status.Text = LiveBroadcastMessages.ForReadStatus(status)
    End Sub

End Class
#End If
