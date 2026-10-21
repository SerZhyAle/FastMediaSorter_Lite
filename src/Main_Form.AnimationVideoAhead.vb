#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Spec 242 (T-7, rules 20 and 24): the video of the NEXT file is prepared while the current one plays.
'''
'''   * It starts only after the current video has begun to play (<see cref="OnAnimationVideoPlaying"/>), for the
'''     neighbour in the direction of the last navigation (<see cref="AnimationVideoPrefetchPlan"/>), and only when the
'''     neighbour's own header facts say it is eligible (<see cref="AnimationVideoPolicy.Decide"/>).
'''   * It runs at below-normal priority on the second <see cref="AnimationVideoRoute"/>, which carries one job and
'''     therefore never a second FFmpeg of its own; the store holds at most two containers (the one on screen, the one
'''     ahead).
'''   * Landing on a file that is not the target kills the running encoder and disposes a parked container at once
'''     (<see cref="ReconcileAnimationVideoAhead"/>); landing on the target takes the container (or waits for the
'''     encoder that is already running) through <see cref="AnimationVideoAheadSlot(Of TLease)"/>.
'''   * A stale preparation is cancelled, never finished (the difference from 241 rule 11: FFmpeg is a process).
'''
''' The state machine is the pure slot in src/Imaging/AnimationVideoAhead.vb; this partial is only the glue.
''' </summary>
Partial Public Class Main_Form

    ''' <summary>Whoever landed on the file while its video was still being prepared: what
    ''' <see cref="OnAnimationVideoPrepared"/> needs to take over.</summary>
    Private NotInheritable Class AheadWaiter
        Friend ReadOnly Generation As Integer
        Friend ReadOnly Picture As Image
        Friend ReadOnly Info As StagedInfo

        Friend Sub New(generation As Integer, picture As Image, info As StagedInfo)
            Me.Generation = generation
            Me.Picture = picture
            Me.Info = info
        End Sub
    End Class

    Private ReadOnly animation_Video_Ahead As New AnimationVideoAheadSlot(Of AnimationVideoLease)()
    Private animation_Video_Ahead_Route As AnimationVideoRoute
    ''' <summary>The pool work that reads the neighbour's facts and starts the route; completes only when no encoder
    ''' of this slot is left running if the slot was emptied meanwhile.</summary>
    Private animation_Video_Ahead_Prepare As Task = Task.CompletedTask
    ''' <summary>The LibVLC play serial the one-ahead job was started for, so a resume from pause does not start another.</summary>
    Private animation_Video_Ahead_For_Play As Integer = -1

    ''' <summary>Stamps a file by length and write time: a video prepared for an older state of the file is never served
    ''' (Q242-6). "" when the file cannot be asked about.</summary>
    Private Shared Function AnimationVideoFileStamp(path As String) As String
        Try
            Dim info As New FileInfo(path)
            If Not info.Exists Then Return ""
            Return info.Length.ToString(CultureInfo.InvariantCulture) & ":" & info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>The neighbour whose video is worth preparing: the one in the direction of travel, as the decode prefetch
    ''' picks it. "" for a list of one, an empty list and the random slideshow (no deterministic neighbour).</summary>
    Private Function AnimationVideoNeighbourPath() As String
        If is_Slide_Show_Random_Mode OrElse total_File_Count <= 1 Then Return ""
        Dim index As Integer = AnimationVideoPrefetchPlan.NeighbourToPrepare(current_File_Index, total_File_Count, is_Nav_Backwards)
        If index = AnimationVideoPrefetchPlan.No_Neighbour Then Return ""
        Try
            Return If(is_Files_Array_Active, files_Array(index), files_List(index))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Rule 24: called once the video on screen plays. Aims the slot at the neighbour and starts its preparation
    ''' on the pool; every reason not to is silent (the neighbour simply gets the ordinary route when the user lands).</summary>
    Private Sub StartAnimationVideoAhead()
        Try
            If IsDisposed OrElse Is_No_Background_Tasks Then Return
            If Not GetModernPreferences().AnimatedViaVideo OrElse Not OptionalRuntimeManager.HasFfmpegRuntime() Then Return

            Dim neighbour As String = AnimationVideoNeighbourPath()
            If neighbour.Length = 0 OrElse String.Equals(neighbour, Current_File_Name, StringComparison.OrdinalIgnoreCase) Then Return
            If Not Path.IsPathFullyQualified(neighbour) Then Return
            Dim stamp As String = AnimationVideoFileStamp(neighbour)
            If stamp.Length = 0 Then Return

            ' Aimed at the other side after the user turned round: the old target is a leftover.
            ReleaseAnimationVideoAheadExcept(neighbour)
            Dim ticket As Integer = animation_Video_Ahead.TryBegin(neighbour, stamp)
            If ticket = 0 Then Return

            Dim ffmpegExe As String = OptionalRuntimeManager.GetFfmpegPath()
            Dim bgRgb As Integer = If(is_PictureBox1_Visible, Picture_Box_1.BackColor.ToArgb(), Picture_Box_2.BackColor.ToArgb()) And &HFFFFFF
            Dim insideSecure As Boolean = (secure_Container IsNot Nothing AndAlso
                                           String.Equals(neighbour, secure_Container_Path, StringComparison.OrdinalIgnoreCase))
            Dim route As AnimationVideoRoute = EnsureAnimationVideoAheadRoute()
            animation_Video_Ahead_Prepare = Task.Run(Function() PrepareAnimationVideoAhead(route, neighbour, ticket, ffmpegExe, bgRgb, insideSecure))
        Catch ex As Exception
            AppFileLogger.LogException("Animation video ahead start", ex)
            DiscardAnimationVideoAhead()
        End Try
    End Sub

    ''' <summary>On a pool thread: the neighbour's header facts, the verdict, then the route. Never throws. The returned
    ''' task completes at once, or - when the slot was emptied while this ran - after the encoder that was just started
    ''' is gone, which is what a landing elsewhere waits for before it starts its own FFmpeg.</summary>
    Private Function PrepareAnimationVideoAhead(route As AnimationVideoRoute, path As String, ticket As Integer,
                                                ffmpegExe As String, bgRgb As Integer, insideSecure As Boolean) As Task
        Try
            If Not animation_Video_Ahead.IsCurrent(ticket) Then Return Task.CompletedTask

            Dim info As StagedInfo = ReadStagedInfoForAhead(path)
            If info Is Nothing Then
                FailAnimationVideoAhead(ticket, path, "not an animation")
                Return Task.CompletedTask
            End If
            Dim facts As AnimationVideoFacts = BuildAnimationVideoFacts(info, path, True, True, insideSecure)
            If AnimationVideoPolicy.Decide(facts) <> AnimationVideoVerdict.Eligible Then
                FailAnimationVideoAhead(ticket, path, "not eligible")
                Return Task.CompletedTask
            End If
            If Not animation_Video_Ahead.IsCurrent(ticket) Then Return Task.CompletedTask

            Dim durationMs As Integer = CInt(Math.Min(info.DurationMs, CLng(Integer.MaxValue)))
            Dim fps As Integer = VideoConvertPlan.Fps(info.Frames, durationMs)
            Dim request As New AnimationVideoRequest(path, 0, fps, info.HasAlpha, info.Width, info.Height, bgRgb, durationMs)
            Dim steps As New AnimationVideoRouteSteps(
                Function(req As AnimationVideoRequest, progress As IProgress(Of Integer), token As CancellationToken)
                    Dim args As String = AnimationVideoPlan.BuildArguments(req.SourcePath, req.Fps, req.HasAlpha,
                                                                           req.CanvasWidth, req.CanvasHeight, req.BackgroundRgb)
                    Return AnimationVideoEncoder.RunAsync(ffmpegExe, args, req.ExpectedDurationMs, AnimationVideoPlan.Video_Max_Bytes,
                                                          belowNormalPriority:=True, progress:=progress, token:=token)
                End Function,
                Function(bytes As ReadOnlyMemory(Of Byte), token As CancellationToken)
                    Return Task.FromResult(EnsureAnimationVideoStore().Seal(bytes, token))
                End Function,
                Function(req As AnimationVideoRequest, lease As AnimationVideoLease, onFail As Action(Of String))
                    Return Task.FromResult(CompleteAnimationVideoAhead(ticket, req, lease))
                End Function,
                Sub(req As AnimationVideoRequest, reason As String)
                    FailAnimationVideoAhead(ticket, req.SourcePath, reason)
                End Sub,
                AddressOf AppFileLogger.WriteLine)
            route.Start(request, steps)

            ' Emptied between the check above and the start: the job is already ours to stop, and the caller must be able
            ' to wait until its process is gone.
            If Not animation_Video_Ahead.IsCurrent(ticket) Then Return route.CancelCurrent()
        Catch ex As Exception
            AppFileLogger.LogException("Animation video ahead prepare: " & path, ex)
            FailAnimationVideoAhead(ticket, path, "fault")
        End Try
        Return Task.CompletedTask
    End Function

    ''' <summary>The first frame of the neighbour is decoded for its header facts only (canvas, frames, length, alpha) and
    ''' disposed at once. Nothing when it is not an animation the decoder stages.</summary>
    Private Function ReadStagedInfoForAhead(path As String) As StagedInfo
        Dim loaded As Tuple(Of Image, IO.MemoryStream) = Nothing
        Try
            Dim failure As PathFailureKind = PathFailureKind.Content
            loaded = LoadImageStaged(path, failure, ImageLoadStage.FirstFrame)
            If loaded Is Nothing OrElse loaded.Item1 Is Nothing Then Return Nothing
            Return AnimationStaging.InfoOf(loaded.Item1)
        Finally
            If loaded IsNot Nothing Then
                Try : loaded.Item1?.Dispose() : Catch : End Try
                Try : loaded.Item2?.Dispose() : Catch : End Try
            End If
        End Try
    End Function

    ''' <summary>The route sealed the neighbour's video. Ownership of the lease is ours: the slot keeps it, or the user
    ''' already landed on the file and gets it, or nobody wants it.</summary>
    Private Function CompleteAnimationVideoAhead(ticket As Integer, request As AnimationVideoRequest, lease As AnimationVideoLease) As Boolean
        Dim waiter As Object = Nothing
        Select Case animation_Video_Ahead.Complete(ticket, lease, waiter)
            Case AheadCompletion.Parked
                AppFileLogger.WriteLine("Animation video ahead ready: " & request.SourcePath)
                Return True
            Case AheadCompletion.Delivered
                Dim landed As AheadWaiter = DirectCast(waiter, AheadWaiter)
                DeliverAnimationVideo(request.SourcePath, landed.Generation, landed.Picture, landed.Info, lease)
                Return True
            Case Else
                Try : lease.Dispose() : Catch : End Try
                Return False
        End Select
    End Function

    ''' <summary>The neighbour's preparation ended without a video. Silent unless the user already landed on the file: then
    ''' it gets spec 241's behaviour through the same door a failed own job uses (rule 23).</summary>
    Private Sub FailAnimationVideoAhead(ticket As Integer, path As String, reason As String)
        Dim waiter As Object = Nothing
        If Not animation_Video_Ahead.Fail(ticket, waiter) Then Return
        Dim landed As AheadWaiter = TryCast(waiter, AheadWaiter)
        If landed Is Nothing Then Return
        AppFileLogger.WriteLine("Animation video ahead failed for the file the user landed on (" & reason & "): " & path)
        DeliverAnimationVideo(path, landed.Generation, landed.Picture, landed.Info, Nothing)
    End Sub

    ''' <summary>Empties the slot whatever it holds. The task completes when no encoder of the slot is running (and the
    ''' pool work that was about to start one has finished).</summary>
    Private Function DiscardAnimationVideoAhead() As Task
        Dim wasPreparing As Boolean = False
        Dim lease As AnimationVideoLease = animation_Video_Ahead.Discard(wasPreparing)
        Return FinishAnimationVideoAheadRelease(lease, wasPreparing)
    End Function

    ''' <summary>Empties the slot unless it is aimed at <paramref name="keepPath"/>.</summary>
    Private Function ReleaseAnimationVideoAheadExcept(keepPath As String) As Task
        Dim wasPreparing As Boolean = False
        Dim lease As AnimationVideoLease = animation_Video_Ahead.Reconcile(keepPath, wasPreparing)
        Return FinishAnimationVideoAheadRelease(lease, wasPreparing)
    End Function

    ''' <summary>Rule 20: the file on screen is the only one whose neighbour-video may stay.</summary>
    Private Sub ReconcileAnimationVideoAhead()
        ReleaseAnimationVideoAheadExcept(Current_File_Name)
    End Sub

    Private Function FinishAnimationVideoAheadRelease(lease As AnimationVideoLease, wasPreparing As Boolean) As Task
        Try : lease?.Dispose() : Catch : End Try
        If Not wasPreparing Then Return Task.CompletedTask
        Dim stopped As Task = Task.CompletedTask
        Dim route As AnimationVideoRoute = animation_Video_Ahead_Route
        If route IsNot Nothing Then stopped = route.CancelCurrent()
        Return Task.WhenAll(animation_Video_Ahead_Prepare, stopped)
    End Function

    Private Function EnsureAnimationVideoAheadRoute() As AnimationVideoRoute
        If animation_Video_Ahead_Route Is Nothing Then animation_Video_Ahead_Route = New AnimationVideoRoute()
        Return animation_Video_Ahead_Route
    End Function

    Private Sub DisposeAnimationVideoAheadRoute()
        Dim route As AnimationVideoRoute = animation_Video_Ahead_Route
        animation_Video_Ahead_Route = Nothing
        Try : route?.Dispose() : Catch : End Try
    End Sub

End Class
#End If
