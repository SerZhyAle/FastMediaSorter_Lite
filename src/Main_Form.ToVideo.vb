#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

' The viewer's side of "Replace with video"
' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §10, §11): a toolbar
' button, a menu entry, the confirmation, and the exact ORDER in which the video is made
' and the original goes away.
'
' That order is the whole file. Steps 1-4 can each stop, and every one of them leaves the
' source exactly where it was; the delete happens only once the video exists at its final
' name (invariant 2). Nothing else in the application may delete a source for this feature.
'
' Modern-only, like the encoder it drives: the x86 viewer gets neither button nor menu
' entry (§2.2).
Partial Public Class Main_Form

    Private WithEvents btn_ToVideo As Button

    ''' <summary>Segoe MDL2 Assets "Video". Free of the five glyphs already in use (the
    ''' editor's pencil and the four video-bar controls), and the same font for the same
    ''' reason: the app is pinned to Microsoft Sans Serif, which has no such glyph.</summary>
    Private Const ToVideo_Glyph_Font As String = "Segoe MDL2 Assets"
    Private Const ToVideo_Glyph As String = ChrW(&HE714)

    ''' <summary>Guards against a second click while a conversion is on screen. The dialog is
    ''' modal, so this is belt and braces - but the thing being guarded is a permanent
    ''' delete.</summary>
    Private to_Video_Running As Boolean

    ''' <summary>Built from BuildModernLayout right after the editor's button, so it inherits
    ''' the uniform chrome and joins the overflow set.
    '''
    ''' AccessibleName is not optional: the caption is a private-use codepoint that a screen
    ''' reader would otherwise read as rubbish.</summary>
    Friend Sub BuildToVideoToolbarControls(host As Panel)
        If btn_ToVideo IsNot Nothing OrElse host Is Nothing Then Return
        btn_ToVideo = New Button With {
            .Name = "btn_ToVideo",
            .Text = ToVideo_Glyph,
            .Font = New Font(ToVideo_Glyph_Font, 9.0F),
            .AutoSize = True,
            .TabStop = False,
            .Visible = False,
            .AccessibleName = Localization.T("Заменить видео")
        }
        host.Controls.Add(btn_ToVideo)
        ' Collapsed until an animation is actually playing. Visible = False alone would not
        ' survive the first LayoutToolbar - PlaceControl force-shows whatever it lays out.
        SetToolbarItemHidden(btn_ToVideo, True)
    End Sub

    ''' <summary>Re-applies the button's language-dependent text. Called from
    ''' InitializeTooltips and LngCh, exactly like LocalizeImageEditor.</summary>
    Friend Sub LocalizeToVideo()
        If btn_ToVideo Is Nothing Then Return
        btn_ToVideo.AccessibleName = Localization.T("Заменить видео")
        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(btn_ToVideo, Localization.T("Преобразовать анимацию в видео и удалить оригинал"))
        End If
    End Sub

    ''' <summary>
    ''' The mirror image of IsCurrentStillImage: gif_Restart_Image_Ref is non-Nothing only
    ''' while a MULTI-FRAME animation is actually playing, which covers a real GIF and a
    ''' transcoded WEBP/APNG alike. So the button appears for exactly the files it can
    ''' convert, and for no video and no still.
    ''' </summary>
    Friend Function IsCurrentAnimation() As Boolean
        Return IsCurrentFileEligibleImage() AndAlso gif_Restart_Image_Ref IsNot Nothing
    End Function

    Private Async Sub btn_ToVideo_Click(sender As Object, e As EventArgs) Handles btn_ToVideo.Click
        Await ReplaceAnimationWithVideoAsync().ConfigureAwait(True)
    End Sub

    ''' <summary>The picture menu's entry point. An Async Sub rather than a dropped Task, so
    ''' an exception surfaces through the usual unhandled-exception path instead of being
    ''' swallowed by a Task nobody awaits.</summary>
    Friend Async Sub StartReplaceAnimationWithVideo()
        Await ReplaceAnimationWithVideoAsync().ConfigureAwait(True)
    End Sub

    ''' <summary>
    ''' §10, in order. Read the frame facts FIRST: every one of them comes off the Image the
    ''' viewer is playing, and step 5 disposes it.
    ''' </summary>
    Friend Async Function ReplaceAnimationWithVideoAsync() As Task
        If to_Video_Running OrElse Not IsCurrentAnimation() Then Return
        If ArchiveModeBlocksFileOperations() Then Return

        Dim sourcePath As String = Current_File_Name
        If String.IsNullOrEmpty(sourcePath) OrElse Not File.Exists(sourcePath) Then Return

        Dim frameCount As Integer = CurrentAnimationFrameCount()
        Dim durationMs As Integer = gif_Total_Duration_Ms
        Dim hasAlpha As Boolean = CurrentAnimationHasAlpha()
        Dim fps As Integer = VideoConvertPlan.Fps(frameCount, durationMs)
        Dim targetPath As String = VideoConvertPlan.TargetPathFor(sourcePath, Function(candidate) File.Exists(candidate))

        ' 1. Confirm, before anything is created.
        If Not ConfirmReplaceWithVideo(sourcePath, targetPath, hasAlpha) Then Return

        to_Video_Running = True
        Try
            ' 2. Ensure FFmpeg. A No, or a failed download, stops here.
            Dim ffmpegExe As String = Await EnsureFfmpegAsync().ConfigureAwait(True)
            If ffmpegExe.Length = 0 Then Return

            SlideShowStop()

            ' 3. Convert into the temporary file.
            Dim tempPath As String = VideoConvertPlan.TempPathFor(targetPath)
            Dim run As AnimationToVideo.EncoderRun =
                Await EncodeWithProgressAsync(ffmpegExe, sourcePath, tempPath, fps, durationMs).ConfigureAwait(True)

            Dim encode As VideoReplaceOutcome = VideoConvertPlan.DecideEncode(
                run.Cancelled, run.ExitCode, File.Exists(tempPath), FileLength(tempPath))

            ' 4. Rename the temporary file onto the target.
            Dim swapped As Boolean = False
            If encode = VideoReplaceOutcome.EncodedOk Then swapped = TryPlaceVideo(tempPath, targetPath)

            ' 5-6. Only now - the video exists at its final name - is the original released
            ' and deleted (invariant 2). ReleaseActiveMedia first: a MemoryStream-backed
            ' Image keeps a handle GDI+ will not otherwise let go of.
            Dim deleted As Boolean = False
            Dim deleteError As String = ""
            If VideoConvertPlan.ShouldDeleteOriginal(encode, swapped) Then
                ReleaseActiveMedia()
                deleted = TryDeleteOriginal(sourcePath, deleteError)
            End If

            Dim outcome As VideoReplaceOutcome = VideoConvertPlan.DecideReplace(encode, swapped, deleted)
            If VideoConvertPlan.ShouldRemoveTemp(outcome) Then RemoveTemp(tempPath)

            ReportOutcome(outcome, targetPath, run.Detail, deleteError)

            ' 7. Land on the file that was just made - the folder has never heard of it.
            If outcome = VideoReplaceOutcome.Success OrElse outcome = VideoReplaceOutcome.OriginalNotDeleted Then
                ProcessArgument(targetPath)
            End If
        Finally
            to_Video_Running = False
        End Try
    End Function

    ' --- the steps, one method each -------------------------------------------

    Private Function ConfirmReplaceWithVideo(sourcePath As String, targetPath As String, hasAlpha As Boolean) As Boolean
        Dim preferences As ModernViewerPreferences = GetModernPreferences()
        If Not preferences.ConfirmReplaceWithVideo Then Return True

        Using dialog As New Video_Replace_Confirm_Form(Path.GetFileName(sourcePath), Path.GetFileName(targetPath), hasAlpha)
            PinToViewerBand(dialog)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return False
            If dialog.DoNotAskAgain Then
                preferences.ConfirmReplaceWithVideo = False
                ApplyModernPreferencesFromSettings()
            End If
        End Using
        Return True
    End Function

    ''' <summary>The path to ffmpeg.exe, or "" when it is not there and the user did not
    ''' want it downloaded.</summary>
    Private Async Function EnsureFfmpegAsync() As Task(Of String)
        If Not Await OptionalRuntimeManager.EnsureFfmpegRuntimeInteractiveAsync(Me).ConfigureAwait(True) Then Return ""
        Return OptionalRuntimeManager.GetFfmpegPath()
    End Function

    ''' <summary>
    ''' Runs the encoder behind the modal progress dialog.
    '''
    ''' ShowDialog runs a nested message loop, so the awaited continuations below still land
    ''' on this thread - which is what lets the dialog be closed from inside the very task it
    ''' is waiting for. The window closes only once the encoder has really stopped, so a
    ''' Cancel can never race the temp file's deletion against a process still writing it.
    ''' </summary>
    Private Async Function EncodeWithProgressAsync(ffmpegExe As String, sourcePath As String, tempPath As String,
                                                   fps As Integer, durationMs As Integer) As Task(Of AnimationToVideo.EncoderRun)
        Using dialog As New Video_Convert_Form(determinate:=durationMs > 0)
            PinToViewerBand(dialog)
            Dim reporter As New Progress(Of Integer)(Sub(percent As Integer) dialog.ReportPercent(percent))
            Dim work As Task(Of AnimationToVideo.EncoderRun) = Nothing

            AddHandler dialog.Shown,
                Sub()
                    work = RunEncoderThenCloseAsync(dialog, ffmpegExe, sourcePath, tempPath, fps, durationMs, reporter)
                End Sub

            dialog.ShowDialog(Me)

            ' Shown never fired - the window could not be displayed at all. Nothing ran, so
            ' nothing may be deleted.
            If work Is Nothing Then Return New AnimationToVideo.EncoderRun With {.Cancelled = True}
            Return Await work.ConfigureAwait(True)
        End Using
    End Function

    Private Async Function RunEncoderThenCloseAsync(dialog As Video_Convert_Form, ffmpegExe As String,
                                                    sourcePath As String, tempPath As String, fps As Integer,
                                                    durationMs As Integer, reporter As IProgress(Of Integer)) As Task(Of AnimationToVideo.EncoderRun)
        Try
            Return Await AnimationToVideo.RunAsync(ffmpegExe, sourcePath, tempPath, fps, durationMs,
                                                   reporter, dialog.Token).ConfigureAwait(True)
        Finally
            dialog.Finish()
        End Try
    End Function

    ''' <summary>The temporary file onto the target name. The target cannot already exist -
    ''' TargetPathFor picked a free one - so this is a Move, never a Replace.</summary>
    Private Function TryPlaceVideo(tempPath As String, targetPath As String) As Boolean
        Try
            If File.Exists(targetPath) Then Return False
            File.Move(tempPath, targetPath)
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Could not place the converted video: " & targetPath, ex)
            Return False
        End Try
    End Function

    ''' <summary>Permanently, past the Recycle Bin - the owner's decision, and what the
    ''' confirmation said would happen.</summary>
    Private Function TryDeleteOriginal(sourcePath As String, ByRef reason As String) As Boolean
        Try
            File.Delete(sourcePath)
            reason = ""
            Return True
        Catch ex As Exception
            reason = ex.Message
            AppFileLogger.LogException("Video created but the original stayed: " & sourcePath, ex)
            Return False
        End Try
    End Function

    ''' <summary>Zero for anything that cannot be measured - which then reads as
    ''' OutputTooSmall, the right verdict for a file we cannot even stat.</summary>
    Private Shared Function FileLength(filePath As String) As Long
        Try
            Dim info As New FileInfo(filePath)
            Return If(info.Exists, info.Length, 0L)
        Catch
            Return 0L
        End Try
    End Function

    Private Sub RemoveTemp(tempPath As String)
        Try
            If File.Exists(tempPath) Then File.Delete(tempPath)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0042: leftover video temp not removed: " & ex.Message)
        End Try
    End Sub

    Private Sub ReportOutcome(outcome As VideoReplaceOutcome, targetPath As String, encoderDetail As String, deleteError As String)
        Select Case outcome
            Case VideoReplaceOutcome.Success
                lbl_Status.Text = Localization.TF("Готово: {0}", Path.GetFileName(targetPath))
            Case VideoReplaceOutcome.OriginalNotDeleted
                lbl_Status.Text = Localization.TF("Видео создано, но не удалось удалить оригинал: {0}", deleteError)
            Case VideoReplaceOutcome.Cancelled
                lbl_Status.Text = Localization.T("Преобразование отменено")
            Case Else
                lbl_Status.Text = Localization.TF("Не удалось создать видео: {0}", DescribeFailure(outcome, encoderDetail))
        End Select
    End Sub

    ''' <summary>
    ''' A reason a person can act on. FFmpeg's own last line is the best answer when there is
    ''' one; the outcome name is the fallback for the cases where FFmpeg said nothing because
    ''' it thought it had succeeded.
    ''' </summary>
    Private Function DescribeFailure(outcome As VideoReplaceOutcome, encoderDetail As String) As String
        If Not String.IsNullOrWhiteSpace(encoderDetail) Then Return encoderDetail
        Return outcome.ToString()
    End Function

    ' --- what the playing animation can tell us --------------------------------

    Private Function CurrentAnimationFrameCount() As Integer
        Try
            If gif_Restart_Image_Ref Is Nothing Then Return 0
            Dim dimension As New FrameDimension(gif_Restart_Image_Ref.FrameDimensionsList(0))
            Return gif_Restart_Image_Ref.GetFrameCount(dimension)
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Whether the confirmation has to mention the flattening (§9.3).
    '''
    ''' IsAlphaPixelFormat alone answers False for every GIF - the format is 8-bit indexed
    ''' and its transparency is one palette entry, not a channel. The ImageFlags.HasAlpha bit
    ''' is what GDI+ sets when that entry exists, so the two together are the honest test.
    ''' </summary>
    Private Function CurrentAnimationHasAlpha() As Boolean
        Try
            If gif_Restart_Image_Ref Is Nothing Then Return False
            If Image.IsAlphaPixelFormat(gif_Restart_Image_Ref.PixelFormat) Then Return True
            Return (gif_Restart_Image_Ref.Flags And CInt(ImageFlags.HasAlpha)) <> 0
        Catch
            Return False
        End Try
    End Function

End Class
#End If
