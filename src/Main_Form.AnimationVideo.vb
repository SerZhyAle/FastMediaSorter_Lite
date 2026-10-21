#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Spec 242 (T-6): Plays animated WEBP and huge GIF as short-lived, encrypted temporary video.
'''
''' Order (rule 10):
'''   1. Stage 1 puts first frame on screen immediately.
'''   2. When eligible (including over-budget animations, rule 8), this partial encodes a fragmented
'''      MP4 to memory via FFmpeg, seals it into an ephemeral .fd-sec container, and switches to LibVLC.
'''   3. LibVLC plays from the decrypting stream with infinite loop (:input-repeat=65535).
'''   4. On navigation or exit, the temp video is released and deleted (DeleteOnClose handle closed).
'''   5. Failure cleanly falls back to spec 241's path (rule 23).
''' </summary>
Partial Public Class Main_Form

    Private animation_Video_Store As AnimationVideoStore
    Private animation_Video_Current_Lease As AnimationVideoLease
    Private animation_Video_Current_Path As String = ""
    Private animation_Video_Current_Info As StagedInfo
    Private animation_Video_Current_Stream As Stream
    Private animation_Video_Current_Input As LibVLCSharp.Shared.StreamMediaInput
    Private animation_Video_Playing As Boolean
    Private animation_Video_Serial As Integer = 0
    Private animation_Video_Cts As CancellationTokenSource
    Private animation_Video_Sweep_Done As Boolean
    Private ReadOnly animation_Video_Store_Gate As New Object()

    ''' <summary>
    ''' Media input for LibVLC. Called by CreateVlcMedia before SecureMediaInputFor (spec 242 rule 21).
    ''' </summary>
    Friend Function AnimationVideoInputFor(filePath As String) As LibVLCSharp.Shared.StreamMediaInput
        If animation_Video_Current_Input Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return Nothing
        If Not String.Equals(filePath, animation_Video_Current_Path, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Return animation_Video_Current_Input
    End Function

    ''' <summary>
    ''' True when LibVLC is actively playing a temporary animation video (rule 21).
    ''' </summary>
    Friend Function IsAnimationVideoPlaying() As Boolean
        Return animation_Video_Playing AndAlso is_Vlc_Playing
    End Function

    ''' <summary>
    ''' True while an animation video is actively shown on the VLC surface.
    ''' </summary>
    Friend Function IsAnimationVideoShown() As Boolean
        Return animation_Video_Playing AndAlso is_Vlc_Playing
    End Function

    ''' <summary>
    ''' True while an animation video is either preparing or actively playing.
    ''' </summary>
    Friend Function IsAnimationVideoActive() As Boolean
        Return animation_Video_Playing OrElse (animation_Video_Cts IsNot Nothing AndAlso Not animation_Video_Cts.IsCancellationRequested)
    End Function

    ''' <summary>
    ''' Non-zero generation/serial while an animation video's media is active in the player.
    ''' </summary>
    Friend Function AnimationVideoEventSerial() As Integer
        If animation_Video_Playing OrElse animation_Video_Current_Input IsNot Nothing Then
            Return animation_Video_Serial
        End If
        Return 0
    End Function

    ''' <summary>
    ''' The staged facts of the animation video currently showing, for "Replace with video".
    ''' </summary>
    Friend Function AnimationVideoStagedInfo() As StagedInfo
        Return animation_Video_Current_Info
    End Function

    ''' <summary>
    ''' Stops the current animation video and releases its lease.
    ''' </summary>
    Friend Sub StopAnimationVideo()
        ReleaseAnimationVideoCurrent()
    End Sub

    ''' <summary>
    ''' Called on the UI thread when LibVLC raises Playing for an animation video. False when the event is a
    ''' stale session's (the caller drops it); True when it belongs to the video on screen - and then the caller
    ''' goes on with what every video gets (rate, controls, status line): an animation video IS a video, with
    ''' the transport bar, pause and a click that pauses, only silent and looping.
    ''' </summary>
    Friend Function OnAnimationVideoPlaying(serial As Integer) As Boolean
        If serial <> animation_Video_Serial Then Return False
        is_Vlc_Playing = True
        RestoreAnimationStatus()

        ' Playing is raised again on every resume from pause: only a new Play() (a new vlc_Play_Serial) starts the clock over.
        If Not animation_Video_Clock.IsActive OrElse animation_Video_Clock_Play_Serial <> vlc_Play_Serial Then
            Dim length As Long = SafePlayerLength()
            Dim restoreAt As Long = 0L
            Dim pending As Single = pending_Video_Seek
            If pending > 0.0F AndAlso pending < 1.0F AndAlso length > 0L Then restoreAt = CLng(pending * length)
            animation_Video_Clock.Start(MonotonicClock.NowMs(), restoreAt, True, playback_Rate)
            animation_Video_Clock_Play_Serial = vlc_Play_Serial
            animation_Video_Clock_Timer.Start()
        End If

        ' Rule 24: the neighbour is prepared only once this video has begun to play, and once per play.
        If animation_Video_Ahead_For_Play <> vlc_Play_Serial Then
            animation_Video_Ahead_For_Play = vlc_Play_Serial
            StartAnimationVideoAhead()
        End If
        Return True
    End Function

    ' --- the position of the video (LibVLC's own clock does not run for a media read from a stream) -----------------

    Private ReadOnly animation_Video_Clock As New AnimationVideoClock()
    Private animation_Video_Clock_Play_Serial As Integer = -1
    Private WithEvents animation_Video_Clock_Timer As New System.Windows.Forms.Timer() With {.Interval = 250}

    Private Sub Animation_Video_Clock_Timer_Tick(sender As Object, e As EventArgs) Handles animation_Video_Clock_Timer.Tick
        If Not animation_Video_Clock.IsActive Then
            animation_Video_Clock_Timer.Stop()
            Return
        End If
        ' Closes the interval that just passed in the state the player was in, so a pause is noticed within a tick
        ' even while the transport bar (and its own timer) is hidden.
        animation_Video_Clock.Sample(MonotonicClock.NowMs(), IsVlcActuallyPlaying(), playback_Rate, SafePlayerLength())
    End Sub

    Private Function SafePlayerLength() As Long
        Try
            If vlc_Media_Player IsNot Nothing Then Return vlc_Media_Player.Length
        Catch
        End Try
        Return 0L
    End Function

    ''' <summary>Where the player is, in milliseconds: the animation clock while an animation video plays,
    ''' LibVLC's own <c>Time</c> for every other media. The transport bar, the seek keys and the A-B loop read it here.</summary>
    Friend Function PlayerTimeMs() As Long
        If animation_Video_Clock.IsActive Then
            Return animation_Video_Clock.PositionMs(MonotonicClock.NowMs(), IsVlcActuallyPlaying(), playback_Rate, SafePlayerLength())
        End If
        Return vlc_Media_Player.Time
    End Function

    ''' <summary>The same as a fraction of the length (what the seek bar's thumb shows).</summary>
    Friend Function PlayerPosition() As Single
        If animation_Video_Clock.IsActive Then
            Return animation_Video_Clock.Fraction(MonotonicClock.NowMs(), IsVlcActuallyPlaying(), playback_Rate, SafePlayerLength())
        End If
        Return vlc_Media_Player.Position
    End Function

    ''' <summary>Seeks the player to a time, and the animation clock with it.</summary>
    Friend Sub SeekPlayerTime(ms As Long)
        vlc_Media_Player.Time = ms
        If animation_Video_Clock.IsActive Then
            animation_Video_Clock.SeekTo(ms, MonotonicClock.NowMs(), IsVlcActuallyPlaying(), playback_Rate, SafePlayerLength())
        End If
    End Sub

    ''' <summary>Seeks the player to a fraction of the length, and the animation clock with it.</summary>
    Friend Sub SeekPlayerPosition(fraction As Single)
        vlc_Media_Player.Position = fraction
        If animation_Video_Clock.IsActive Then
            Dim length As Long = SafePlayerLength()
            animation_Video_Clock.SeekTo(CLng(fraction * length), MonotonicClock.NowMs(), IsVlcActuallyPlaying(), playback_Rate, length)
        End If
    End Sub

    ''' <summary>True while the media in the player is an animation video: it loops for ever, so it has no end to
    ''' remember, to wait for or to advance from, and no position worth keeping per file.</summary>
    Friend Function IsAnimationVideoMedia() As Boolean
        Return animation_Video_Clock.IsActive
    End Function

    ''' <summary>
    ''' Called on the UI thread when LibVLC raises EncounteredError for an animation video.
    ''' </summary>
    Friend Sub OnAnimationVideoError(serial As Integer)
        If serial <> animation_Video_Serial Then Return
        AppFileLogger.WriteLine("Animation video failed: LibVLC error: " & animation_Video_Current_Path)
        Dim failedPath As String = animation_Video_Current_Path
        Dim info As StagedInfo = animation_Video_Current_Info
        Dim pic As Image = PictureOnScreen()
        ReleaseAnimationVideoCurrent()
        RestoreAnimationStatus()
        SayProgress(Localization.TF("Анимация показана без видео: {0}", System.IO.Path.GetFileName(failedPath)))
        If pic IsNot Nothing AndAlso info IsNot Nothing Then
            FallbackToSpec241(pic, info, failedPath, media_Generation)
        End If
    End Sub

    ''' <summary>
    ''' Called by BeginAnimationUpgradeIfPreview. If the animation is eligible under spec 242,
    ''' initiates the background encode and video playback. Returns True if handled.
    ''' </summary>
    Private Function TryStartAnimationVideo(picture As Image, info As StagedInfo) As Boolean
        If picture Is Nothing OrElse info Is Nothing Then Return False

        Dim filePath As String = Current_File_Name
        If String.IsNullOrEmpty(filePath) Then Return False

        Dim facts As AnimationVideoFacts = CurrentAnimationVideoFacts(info, filePath)
        Dim verdict As AnimationVideoVerdict = AnimationVideoPolicy.Decide(facts)

        ' Rule 22: FFmpeg absent
        If verdict = AnimationVideoVerdict.NoFfmpeg Then
            If Not GetModernPreferences().AnimatedViaVideoFfmpegAsked Then
                HandleFfmpegConsentOffer(picture, info, filePath)
                Return True
            End If
            Return False
        End If

        If verdict <> AnimationVideoVerdict.Eligible Then Return False

        ' Rules 20 and 24: the file may be the one prepared ahead while the previous video played.
        Dim aheadLease As AnimationVideoLease = Nothing
        Dim promotion As AheadPromotion = animation_Video_Ahead.Promote(
            filePath, AnimationVideoFileStamp(filePath), New AheadWaiter(media_Generation, picture, info), aheadLease)
        Select Case promotion
            Case AheadPromotion.Ready
                ' Already sealed: no wait, no encoder. The play starts after the display step that called us has finished.
                BeginAnimationVideoPreviewState(picture, info, filePath, media_Generation, withStatus:=False)
                AppFileLogger.WriteLine("Animation video taken from ahead: " & filePath)
                DeliverAnimationVideo(filePath, media_Generation, picture, info, aheadLease)
                Return True
            Case AheadPromotion.Waiting
                ' The encoder of the neighbour is already running for exactly this file: let it finish (rule 24).
                BeginAnimationVideoPreviewState(picture, info, filePath, media_Generation, withStatus:=True)
                AppFileLogger.WriteLine("Animation video ahead promoted while preparing: " & filePath)
                Return True
        End Select

        ' Not the neighbour: whatever the slot still holds is aimed elsewhere, and a running ahead encoder must be gone
        ' before this file's own starts (rule 14: one FFmpeg at a time).
        StartAnimationVideoJob(picture, info, filePath, media_Generation, DiscardAnimationVideoAhead())
        Return True
    End Function

    Private Async Sub HandleFfmpegConsentOffer(picture As Image, info As StagedInfo, filePath As String)
        Dim generation As Integer = media_Generation
        Dim installed As Boolean = False
        Try
            installed = Await OptionalRuntimeManager.EnsureFfmpegRuntimeInteractiveAsync(Me).ConfigureAwait(True)
        Catch
            installed = False
        End Try

        If generation <> media_Generation Then Return

        If Not installed Then
            GetModernPreferences().AnimatedViaVideoFfmpegAsked = True
            MarkSettingsDirty()
            ' Fall back to 241
            FallbackToSpec241(picture, info, filePath, generation)
            Return
        End If

        ' User installed FFmpeg! Re-try start
        If Not TryStartAnimationVideo(picture, info) Then
            FallbackToSpec241(picture, info, filePath, generation)
        End If
    End Sub

    Private Function CurrentAnimationVideoFacts(info As StagedInfo, filePath As String) As AnimationVideoFacts
        Dim insideSecure As Boolean = False
        Try
            insideSecure = (secure_Container IsNot Nothing AndAlso
                           String.Equals(filePath, secure_Container_Path, StringComparison.OrdinalIgnoreCase))
        Catch
        End Try

        Return BuildAnimationVideoFacts(info, filePath, GetModernPreferences().AnimatedViaVideo,
                                        OptionalRuntimeManager.HasFfmpegRuntime(), insideSecure)
    End Function

    ''' <summary>The facts of one file for <see cref="AnimationVideoPolicy.Decide"/>. Reads nothing of the form, so the
    ''' prefetch can ask it on a pool thread with the values it took on the UI thread.</summary>
    Private Shared Function BuildAnimationVideoFacts(info As StagedInfo, filePath As String, settingOn As Boolean,
                                                     ffmpegPresent As Boolean, insideSecure As Boolean) As AnimationVideoFacts
        Dim fileLength As Long = 0
        Dim isReadable As Boolean = False
        Try
            If File.Exists(filePath) Then
                fileLength = New FileInfo(filePath).Length
                isReadable = True
            End If
        Catch
        End Try

        Return New AnimationVideoFacts With {
            .SettingOn = settingOn,
            .FfmpegPresent = ffmpegPresent,
            .Kind = AnimationVideoPolicy.KindOfExtension(Path.GetExtension(filePath)),
            .Width = info.Width,
            .Height = info.Height,
            .Frames = info.Frames,
            .FileLength = fileLength,
            .InsideSecureContainer = insideSecure,
            .IsLiveStream = False,
            .IsReadablePath = isReadable,
            .AnimationTooLong = info.IsTooLong
        }
    End Function

    ''' <summary>The preview state of a file whose video is being made or fetched from ahead (rule 10): the first frame
    ''' stays, the status line says the video is coming. A job of this file's own replaces any earlier one.</summary>
    Private Sub BeginAnimationVideoPreviewState(picture As Image, info As StagedInfo, filePath As String,
                                                generation As Integer, withStatus As Boolean)
        animation_Video_Cts?.Cancel()
        animation_Video_Cts?.Dispose()
        animation_Video_Cts = Nothing

        animation_Upgrade.ShowPreview(generation, filePath, False)
        animation_Preview_Info = info
        If withStatus Then
            animation_Saved_Status = lbl_Status.Text
            animation_Status_Line = Localization.TF("Видео готовится.. {0} %", 0)
            SayProgress(animation_Status_Line)
        End If
    End Sub

    ''' <summary>Hands a finished container (or Nothing, a failed preparation) to <see cref="OnAnimationVideoPrepared"/>
    ''' on the UI thread, after whatever the caller is in the middle of. From any thread.</summary>
    Private Sub DeliverAnimationVideo(filePath As String, generation As Integer, picture As Image, info As StagedInfo,
                                      lease As AnimationVideoLease)
        Try
            If Not IsDisposed AndAlso IsHandleCreated Then
                BeginInvoke(New Action(Sub() OnAnimationVideoPrepared(filePath, generation, picture, info, lease)))
                Return
            End If
        Catch
        End Try
        Try : lease?.Dispose() : Catch : End Try
    End Sub

    Private Sub StartAnimationVideoJob(picture As Image, info As StagedInfo, filePath As String, generation As Integer,
                                       aheadGone As Task)
        ' Show preview state (rule 10); this also cancels any earlier job of this class
        BeginAnimationVideoPreviewState(picture, info, filePath, generation, withStatus:=True)
        animation_Video_Cts = New CancellationTokenSource()
        Dim token As CancellationToken = animation_Video_Cts.Token

        Dim durationMs As Integer = CInt(Math.Min(info.DurationMs, CLng(Integer.MaxValue)))
        Dim fps As Integer = VideoConvertPlan.Fps(info.Frames, durationMs)
        Dim bgRgb As Integer = If(is_PictureBox1_Visible, Picture_Box_1.BackColor.ToArgb(), Picture_Box_2.BackColor.ToArgb()) And &HFFFFFF

        Dim ffmpegExe As String = OptionalRuntimeManager.GetFfmpegPath()
        Dim args As String = AnimationVideoPlan.BuildArguments(filePath, fps, info.HasAlpha, info.Width, info.Height, bgRgb)

        Dim progressReporter As New Progress(Of Integer)(Sub(percent)
                                                             If generation = media_Generation AndAlso Not IsDisposed Then
                                                                 animation_Status_Line = Localization.TF("Видео готовится.. {0} %", percent)
                                                                 SayProgress(animation_Status_Line)
                                                             End If
                                                         End Sub)

        Task.Run(Async Function()
                     Dim encoded As EncodedVideo = Nothing
                     Dim lease As AnimationVideoLease = Nothing
                     Try
                         ' Rule 14: a neighbour's encoder that was running when we landed elsewhere is killed, and its
                         ' process is gone before ours starts.
                         If aheadGone IsNot Nothing Then
                             Try
                                 Await aheadGone.ConfigureAwait(False)
                             Catch
                             End Try
                         End If
                         token.ThrowIfCancellationRequested()
                         encoded = Await AnimationVideoEncoder.RunAsync(ffmpegExe, args, durationMs,
                                                                       AnimationVideoPlan.Video_Max_Bytes,
                                                                       belowNormalPriority:=False,
                                                                       progress:=progressReporter,
                                                                       token:=token).ConfigureAwait(False)

                         If encoded.Outcome = AnimationVideoOutcome.Succeeded AndAlso Not token.IsCancellationRequested Then
                             Dim store As AnimationVideoStore = EnsureAnimationVideoStore()
                             lease = store.Seal(encoded.Bytes, token)
                         End If
                     Catch ex As Exception
                         If Not TypeOf ex Is OperationCanceledException Then
                             AppFileLogger.LogException("Animation video encode/seal failed: " & filePath, ex)
                         End If
                     Finally
                         encoded?.Dispose()
                     End Try

                     If token.IsCancellationRequested Then
                         lease?.Dispose()
                         Return
                     End If

                     ' Back to UI thread
                     Try
                         If Not IsDisposed AndAlso IsHandleCreated Then
                             BeginInvoke(New Action(Sub() OnAnimationVideoPrepared(filePath, generation, picture, info, lease)))
                         Else
                             lease?.Dispose()
                         End If
                     Catch
                         lease?.Dispose()
                     End Try
                 End Function, token)
    End Sub

    Private Sub OnAnimationVideoPrepared(filePath As String, generation As Integer, picture As Image, info As StagedInfo, lease As AnimationVideoLease)
        If IsDisposed OrElse Disposing Then
            lease?.Dispose()
            Return
        End If

        If generation <> media_Generation OrElse Not String.Equals(filePath, Current_File_Name, StringComparison.OrdinalIgnoreCase) Then
            lease?.Dispose()
            Return
        End If

        If lease Is Nothing Then
            ' Encode or seal failed: fall back to 241 (rule 23)
            AppFileLogger.WriteLine("Animation video failed for " & filePath & "; falling back to spec 241")
            RestoreAnimationStatus()
            SayProgress(Localization.TF("Анимация показана без видео: {0}", Path.GetFileName(filePath)))
            FallbackToSpec241(picture, info, filePath, generation)
            Return
        End If

        ' Clean up previous video lease
        ReleaseAnimationVideoCurrent()

        Interlocked.Increment(animation_Video_Serial)
        animation_Video_Current_Lease = lease
        animation_Video_Current_Path = filePath
        animation_Video_Current_Info = info
        animation_Video_Current_Stream = lease.OpenPlaybackStream()
        animation_Video_Current_Input = New LibVLCSharp.Shared.StreamMediaInput(animation_Video_Current_Stream)
        animation_Video_Playing = True

        ' Mark landed
        animation_Upgrade.TryLand(generation)
        AnimationStaging.Unmark(picture)

        RestoreAnimationStatus()

        ' Start VLC playback
        PlayVideoWithVlcAsync(filePath)
        AppFileLogger.WriteLine("Animation video prepared and playing: " & filePath)
    End Sub

    ''' <summary>
    ''' Spec 242 rule 7: a GIF takes the video route only when it is huge (at least 16 MB, or canvas x frames at least
    ''' 50 Mpx), and a GIF under 2 MB is never looked at. GDI+ has already loaded the picture by now, so the facts come off
    ''' it (no block walk is needed); the point of the route for a GIF is that the video plays smoothly and loops
    ''' without GDI+ re-timing it. A small GIF stays on GDI+ untouched. True when the route took it.
    ''' </summary>
    Private Function TryStartHugeGifVideo(picture As Image) As Boolean
        Try
            Dim filePath As String = Current_File_Name
            If picture Is Nothing OrElse String.IsNullOrEmpty(filePath) Then Return False
            If AnimationVideoPolicy.KindOfExtension(Path.GetExtension(filePath)) <> AnimationVideoKind.Gif Then Return False
            If Not GetModernPreferences().AnimatedViaVideo Then Return False
            If Not picture.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Gif) Then Return False

            Dim fileInfo As New FileInfo(filePath)
            If Not fileInfo.Exists OrElse Not AnimationVideoPolicy.GifNeedsBlockWalk(fileInfo.Length) Then Return False

            Dim frameDimension As New System.Drawing.Imaging.FrameDimension(picture.FrameDimensionsList(0))
            Dim frames As Integer = picture.GetFrameCount(frameDimension)
            If frames < 2 Then Return False

            Dim durationMs As Long = 0
            Try
                Dim item As System.Drawing.Imaging.PropertyItem = picture.GetPropertyItem(&H5100)
                If item IsNot Nothing AndAlso item.Value IsNot Nothing AndAlso item.Len >= frames * 4 Then
                    For i As Integer = 0 To frames - 1
                        Dim delay As Integer = BitConverter.ToInt32(item.Value, i * 4)
                        If delay <= 0 Then delay = 10
                        durationMs += CLng(delay) * 10L
                    Next
                End If
            Catch
                durationMs = 0
            End Try
            If durationMs <= 0 Then durationMs = 1000

            ' A GIF may carry a transparent colour GDI+ reports as an indexed format; flattening onto the background colour
            ' is right for it and costs an opaque GIF nothing.
            Dim info As New StagedInfo(picture.Width, picture.Height, frames, durationMs, True, False)
            Return TryStartAnimationVideo(picture, info)
        Catch ex As Exception
            AppFileLogger.LogException("Animation video huge-GIF check", ex)
            Return False
        End Try
    End Function

    Private Sub FallbackToSpec241(picture As Image, info As StagedInfo, filePath As String, generation As Integer)
        If generation <> media_Generation Then Return

        ' A GIF is not a stand-in for something spec 241 can still prepare: GDI+ already plays it (rule 23).
        If AnimationStaging.InfoOf(picture) Is Nothing Then
            animation_Upgrade.Leave()
            animation_Preview_Info = Nothing
            RestoreAnimationStatus()
            Return
        End If

        animation_Upgrade.ShowPreview(generation, filePath, info.IsTooLong)
        animation_Preview_Info = info

        If info.IsTooLong Then
            Notify(Localization.TF("Показан первый кадр, анимация слишком длинная (кадров: {0})", info.Frames), NotifyLevel.Warning)
            Return
        End If

        animation_Saved_Status = lbl_Status.Text
        animation_Status_Line = Localization.TF("Анимация готовится.. кадров: {0}", info.Frames)
        SayProgress(animation_Status_Line)
        UpgradeRunner().Submit(New AnimationUpgradeJob(filePath, generation, picture, info.Frames))
    End Sub

    ''' <summary>
    ''' Releases current playing animation video and closes its lease.
    ''' Rule 19 order: player stops, input disposed, stream disposed, lease disposed.
    ''' </summary>
    Friend Sub ReleaseAnimationVideoCurrent()
        animation_Video_Cts?.Cancel()
        animation_Video_Cts?.Dispose()
        animation_Video_Cts = Nothing

        ' First: from here the player's own position is read again, and nothing samples a clock that has no media.
        animation_Video_Clock.Clear()
        animation_Video_Clock_Timer.Stop()

        If animation_Video_Playing Then
            animation_Video_Playing = False
            If vlc_Media_Player IsNot Nothing Then
                Try
                    vlc_Media_Player.Stop()
                    vlc_Media_Player.Media = Nothing
                Catch
                End Try
            End If
            is_Vlc_Playing = False
            If vlc_Video_View IsNot Nothing Then vlc_Video_View.Visible = False
        End If

        animation_Video_Current_Path = ""
        animation_Video_Current_Info = Nothing
        animation_Video_Serial = 0

        Dim input As LibVLCSharp.Shared.StreamMediaInput = animation_Video_Current_Input
        Dim stream As Stream = animation_Video_Current_Stream
        Dim lease As AnimationVideoLease = animation_Video_Current_Lease

        animation_Video_Current_Input = Nothing
        animation_Video_Current_Stream = Nothing
        animation_Video_Current_Lease = Nothing

        Try : input?.Dispose() : Catch : End Try
        Try : stream?.Dispose() : Catch : End Try
        Try : lease?.Dispose() : Catch : End Try

        ' Rule 20: what was prepared for the file we left survives only if it is the file now on screen.
        animation_Video_Ahead_For_Play = -1
        ReconcileAnimationVideoAhead()
    End Sub

    ''' <summary>
    ''' Called when the setting is switched off (rule 4): stops running jobs and purges store.
    ''' </summary>
    Friend Sub PurgeAnimationVideoStore()
        ReleaseAnimationVideoCurrent()
        DiscardAnimationVideoAhead()
        Try
            animation_Video_Store?.ReleaseAll()
        Catch
        End Try
    End Sub

    Friend Sub DisposeAnimationVideoStore()
        ReleaseAnimationVideoCurrent()
        DiscardAnimationVideoAhead()
        DisposeAnimationVideoAheadRoute()
        Try
            animation_Video_Store?.Dispose()
        Catch
        End Try
        animation_Video_Store = Nothing
    End Sub

    Private Function EnsureAnimationVideoStore() As AnimationVideoStore
        ' Called from the UI thread and from the pool (the encode job and the one-ahead job).
        SyncLock animation_Video_Store_Gate
            If animation_Video_Store Is Nothing Then
                Dim root As String = Path.Combine(Path.GetTempPath(), AnimationVideoStoreRules.RootFolderName)
                animation_Video_Store = New AnimationVideoStore(root, Process.GetCurrentProcess().Id, AddressOf AppFileLogger.WriteLine)
            End If
            Return animation_Video_Store
        End SyncLock
    End Function

    ''' <summary>
    ''' Start-up sweep of orphaned directories from dead pids (spec 242 rule 18).
    ''' </summary>
    Friend Sub SweepAnimationVideoStoreIfNeeded()
        If animation_Video_Sweep_Done Then Return
        animation_Video_Sweep_Done = True
        Try
            Dim root As String = Path.Combine(Path.GetTempPath(), AnimationVideoStoreRules.RootFolderName)
            AnimationVideoStore.Sweep(root, DateTime.UtcNow, AddressOf ArchiveTempStore.IsLiveViewerProcess, AddressOf AppFileLogger.WriteLine)
        Catch ex As Exception
            AppFileLogger.LogException("Animation video store startup sweep", ex)
        End Try
    End Sub

End Class
#End If
