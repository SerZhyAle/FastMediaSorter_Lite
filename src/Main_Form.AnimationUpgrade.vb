#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' A long animated picture is opened in two stages (specification 241): the display path puts up its FIRST
''' frame at once (a plain picture, see <see cref="AnimationStaging"/>) and this partial prepares the whole
''' animation in the background, then swaps it into the same picture box.
'''
''' Everything that decides is elsewhere and pure: <see cref="AnimationUpgradeState"/> (what is on screen,
''' which showing a result belongs to) and <see cref="AnimationUpgradeRunner(Of TJob, TResult)"/> (one worker,
''' newest request wins). This file is the glue: it reads the form's state and moves pictures.
'''
''' Ownership (rule 22): the first frame belongs to its picture box until the swap disposes it; the animation
''' belongs to the job until the swap hands it to the box - or, when nobody wants it, until
''' <see cref="ReleaseUpgradeResult"/> disposes it. The decode cache already holds the transcoded bytes by
''' then, so a file the user left costs nothing to come back to.
''' </summary>
Partial Public Class Main_Form

    ''' <summary>One request to prepare the animation of the file on screen.</summary>
    Private NotInheritable Class AnimationUpgradeJob
        Friend ReadOnly Path As String
        Friend ReadOnly Generation As Integer
        ''' <summary>The first frame, by identity: the swap only happens while the box still shows THIS object.</summary>
        Friend ReadOnly Preview As Image
        Friend ReadOnly Frames As Integer

        Friend Sub New(path As String, generation As Integer, preview As Image, frames As Integer)
            Me.Path = path
            Me.Generation = generation
            Me.Preview = preview
            Me.Frames = frames
        End Sub
    End Class

    Private ReadOnly animation_Upgrade As New AnimationUpgradeState()
    Private animation_Runner As AnimationUpgradeRunner(Of AnimationUpgradeJob, Tuple(Of Image, IO.MemoryStream))

    ''' <summary>What the first frame on screen says about its animation; Nothing when no stand-in is shown.</summary>
    Private animation_Preview_Info As StagedInfo

    ''' <summary>The status text before our line replaced it, and our line - so leaving puts the old one back
    ''' only if nobody has written over ours meanwhile.</summary>
    Private animation_Saved_Status As String = ""
    Private animation_Status_Line As String = ""

    ''' <summary>True while a first-frame stand-in is the picture on screen (rule 13). Read by
    ''' <c>IsCurrentStillImage</c>: the file is an animation that cannot play yet, so the editor and OCR stay
    ''' hidden - the editor decodes its own copy and a save would flatten the animation.</summary>
    Private ReadOnly Property IsAnimationPreviewShown As Boolean
        Get
            ' Also while an animation video opens or plays over the first frame (spec 242 rule 21, Main_Form.AnimationVideo.vb).
            Return animation_Upgrade.IsPreviewShown OrElse IsAnimationVideoActive()
        End Get
    End Property

    ''' <summary>The facts of an animation the viewer will never play (over the decode budget), for "Replace with
    ''' video" (rule 18, Q241-5): that action needs the frame count, the length and the transparency, and the
    ''' picture on screen is only the first frame. Nothing in every other state.</summary>
    Private Function TooLongAnimationInfo() As StagedInfo
        ' A playing animation video is not a GDI+ animation either (gif_Restart_Image_Ref is Nothing), and the facts of the
        ' animation it shows are those of the first frame under it (spec 242 rule 21).
        If IsAnimationVideoShown() Then Return AnimationVideoStagedInfo()
        Return If(animation_Upgrade.Phase = AnimationUpgradePhase.TooLong, animation_Preview_Info, Nothing)
    End Function

    ''' <summary>
    ''' The picture just put on a box is looked at by <c>StartGifLoopPlayback</c>; when it is the first frame of
    ''' a long animation this starts the preparation of the rest (or, for an animation over the budget, says that
    ''' the first frame is all there is). A picture that is not a stand-in changes nothing.
    ''' </summary>
    Private Sub BeginAnimationUpgradeIfPreview(picture As Image)
        Dim info As StagedInfo = AnimationStaging.InfoOf(picture)
        If info Is Nothing Then Return

        ' Spec 242 (T-6): try the animated graphics through video route first (bypasses 400 Mpx limit for video)
        If TryStartAnimationVideo(picture, info) Then Return

        animation_Upgrade.ShowPreview(media_Generation, Current_File_Name, info.IsTooLong)
        animation_Preview_Info = info
        If info.IsTooLong Then
            Notify(Localization.TF("Показан первый кадр, анимация слишком длинная (кадров: {0})", info.Frames), NotifyLevel.Warning)
            Return
        End If

        animation_Saved_Status = lbl_Status.Text
        animation_Status_Line = Localization.TF("Анимация готовится.. кадров: {0}", info.Frames)
        SayProgress(animation_Status_Line)
        UpgradeRunner().Submit(New AnimationUpgradeJob(Current_File_Name, media_Generation, picture, info.Frames))
    End Sub

    ''' <summary>The picture left the screen (navigation, a file operation, closing): the stand-in no longer
    ''' awaits anything, and our status line goes with it.</summary>
    Private Sub LeaveAnimationUpgrade()
        If AnimationVideoInputFor(Current_File_Name) Is Nothing Then
            ReleaseAnimationVideoCurrent()
        End If
        If animation_Upgrade.Phase = AnimationUpgradePhase.None Then Return
        animation_Upgrade.Leave()
        animation_Preview_Info = Nothing
        RestoreAnimationStatus()
    End Sub

    ''' <summary>The user turned the first frame in place (R): swapping the animation in would undo that, so it
    ''' stays as the user left it.</summary>
    Private Sub HoldAnimationPreview()
        ReleaseAnimationVideoCurrent()
        If animation_Upgrade.Phase <> AnimationUpgradePhase.Preparing Then Return
        animation_Upgrade.Hold()
        RestoreAnimationStatus()
    End Sub

    Private Sub RestoreAnimationStatus()
        If animation_Status_Line.Length > 0 AndAlso Not IsDisposed Then
            Try
                If String.Equals(lbl_Status.Text, animation_Status_Line, StringComparison.Ordinal) Then
                    lbl_Status.Text = animation_Saved_Status
                End If
            Catch ex As ObjectDisposedException
                ' the form is closing: nothing left to say it to
            End Try
        End If
        animation_Status_Line = ""
        animation_Saved_Status = ""
    End Sub

    Private Function UpgradeRunner() As AnimationUpgradeRunner(Of AnimationUpgradeJob, Tuple(Of Image, IO.MemoryStream))
        If animation_Runner Is Nothing Then
            animation_Runner = New AnimationUpgradeRunner(Of AnimationUpgradeJob, Tuple(Of Image, IO.MemoryStream))(
                AddressOf PrepareAnimation, AddressOf IsUpgradeStillWanted, AddressOf OnUpgradeFinished)
        End If
        Return animation_Runner
    End Function

    ' --- the worker half (not the UI thread) -------------------------------------------------------------

    ''' <summary>Asked just before a WAITING job starts: only the file still on screen is worth preparing. (A job
    ''' that is already running is never interrupted - it finishes into the decode cache, spec 241 Q241-3.)</summary>
    Private Function IsUpgradeStillWanted(job As AnimationUpgradeJob) As Boolean
        Return job.Generation = Volatile.Read(media_Generation)
    End Function

    ''' <summary>The whole transcode: today's one-step load, on the worker. It reads its own copy of the file's
    ''' bytes and holds no handle afterwards, so a move or a delete never waits for it.</summary>
    Private Function PrepareAnimation(job As AnimationUpgradeJob) As Tuple(Of Image, IO.MemoryStream)
        Dim failure As PathFailureKind = PathFailureKind.Content
        Return LoadImageStaged(job.Path, failure, ImageLoadStage.Whole)
    End Function

    ''' <summary>The job ended (on the worker). The result is the form's from here: it is applied on the UI
    ''' thread, or disposed.</summary>
    Private Sub OnUpgradeFinished(job As AnimationUpgradeJob, result As Tuple(Of Image, IO.MemoryStream), failure As Exception)
        If failure IsNot Nothing Then
            AppFileLogger.LogException("Animation could not be prepared: " & job.Path, failure)
        ElseIf result Is Nothing Then
            AppFileLogger.WriteLine("Animation could not be prepared (no picture): " & job.Path)
        End If
        Try
            If IsDisposed OrElse Not IsHandleCreated Then
                ReleaseUpgradeResult(result)
                Return
            End If
            BeginInvoke(New Action(Sub() ApplyPreparedAnimation(job, result)))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            ReleaseUpgradeResult(result)
        End Try
    End Sub

    Private Shared Sub ReleaseUpgradeResult(result As Tuple(Of Image, IO.MemoryStream))
        If result Is Nothing Then Return
        result.Item1?.Dispose()
        result.Item2?.Dispose()
    End Sub

    ' --- the UI half ----------------------------------------------------------------------------------

    ''' <summary>
    ''' Rules 12 and 16. The animation replaces the first frame in place - the same box, the same size, no re-run
    ''' of the show-media pipeline - so zoom, pan, fit and the perspective bars are untouched, and then it plays.
    ''' Only while the box still shows THIS first frame of THIS showing; anything else is somebody else's screen.
    ''' </summary>
    Private Sub ApplyPreparedAnimation(job As AnimationUpgradeJob, result As Tuple(Of Image, IO.MemoryStream))
        If IsDisposed OrElse Disposing Then
            ReleaseUpgradeResult(result)
            Return
        End If

        If result Is Nothing Then
            If animation_Upgrade.TryFail(job.Generation) AndAlso job.Generation = media_Generation Then
                RestoreAnimationStatus()
                Notify(Localization.TF("Анимацию не удалось подготовить: {0}", Path.GetFileName(job.Path)), NotifyLevel.Warning)
            End If
            Return
        End If

        Dim box As Integer = BoxShowing(job.Preview)
        If job.Generation <> media_Generation OrElse box = 0 OrElse Not animation_Upgrade.TryLand(job.Generation) Then
            ReleaseUpgradeResult(result)
            Return
        End If

        Dim animation As Image = result.Item1
        If box = 2 Then
            Picture_Box_2.Image = animation
            pictureBox2_Stream?.Dispose()
            pictureBox2_Stream = result.Item2
        Else
            Picture_Box_1.Image = animation
            pictureBox1_Stream?.Dispose()
            pictureBox1_Stream = result.Item2
        End If
        AnimationStaging.Unmark(job.Preview)
        job.Preview.Dispose()

        RestoreAnimationStatus()
        StartGifLoopPlayback(animation)
        ApplyTranslateButtonsVisibility()
        Dim shown As PictureBox = If(box = 2, Picture_Box_2, Picture_Box_1)
        shown.Invalidate()
        AppFileLogger.WriteLine("Animation swapped in: " & job.Frames.ToString() & " frames: " & job.Path)
    End Sub

    ''' <summary>1 or 2 when that box holds exactly this picture object, 0 otherwise.</summary>
    Private Function BoxShowing(picture As Image) As Integer
        If picture Is Nothing Then Return 0
        If ReferenceEquals(Picture_Box_1.Image, picture) Then Return 1
        If ReferenceEquals(Picture_Box_2.Image, picture) Then Return 2
        Return 0
    End Function

End Class
#End If
