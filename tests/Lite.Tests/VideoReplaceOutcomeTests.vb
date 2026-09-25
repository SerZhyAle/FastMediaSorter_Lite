#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

''' <summary>
''' When the original may be deleted
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §10, invariant 2).
'''
''' This is the irreversible half of the whole feature: the source goes past the Recycle
''' Bin and is not coming back. So "only the full-success path deletes" is stated here as a
''' table a test walks, rather than as an ordering somebody has to keep true by hand while
''' editing an async method.
''' </summary>
Public Class VideoReplaceOutcomeTests

    Private Const Big_Enough As Long = VideoConvertPlan.Min_Output_Bytes

    ' --- what the encoder produced ---------------------------------------------

    <Fact>
    Public Sub Exit_code_zero_with_a_real_file_is_the_only_good_encode()
        Assert.Equal(VideoReplaceOutcome.EncodedOk,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=True, tempBytes:=Big_Enough, expectedDurationMs:=3000, fps:=10, framesWritten:=30, outTimeMs:=2900))
    End Sub

    <Fact>
    Public Sub A_non_zero_exit_code_is_a_failure()
        Assert.Equal(VideoReplaceOutcome.EncoderFailed,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=1, tempExists:=True, tempBytes:=Big_Enough, expectedDurationMs:=3000, fps:=10, framesWritten:=30, outTimeMs:=2900))
    End Sub

    ''' <summary>FFmpeg can report success at nothing - a filter graph that produced no
    ''' frames exits 0 and writes no file.</summary>
    <Fact>
    Public Sub A_missing_output_is_a_failure_even_at_exit_code_zero()
        Assert.Equal(VideoReplaceOutcome.OutputMissing,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=False, tempBytes:=0, expectedDurationMs:=3000, fps:=10, framesWritten:=30, outTimeMs:=2900))
    End Sub

    <Fact>
    Public Sub A_header_sized_output_is_a_failure()
        Assert.Equal(VideoReplaceOutcome.OutputTooSmall,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=True, tempBytes:=Big_Enough - 1, expectedDurationMs:=3000, fps:=10, framesWritten:=30, outTimeMs:=2900))
    End Sub

    ''' <summary>Cancel outranks everything: a killed process usually also has a non-zero
    ''' exit code, and reporting that as an encoder failure would blame FFmpeg for the user
    ''' pressing a button.</summary>
    <Fact>
    Public Sub Cancel_outranks_the_exit_code()
        Assert.Equal(VideoReplaceOutcome.Cancelled,
                     VideoConvertPlan.DecideEncode(cancelled:=True, exitCode:=255, tempExists:=False, tempBytes:=0, expectedDurationMs:=3000, fps:=10, framesWritten:=30, outTimeMs:=2900))
    End Sub

    ' --- audit 050 D-18: the video must hold the animation --------------------------

    ''' <summary>Measured with the pinned FFmpeg 9.0.1: a 30-frame animated WEBP cut in
    ''' half exits 0 with an 11-frame, 12 KB video. Exit 0 and "not a header" used to be the
    ''' whole test, and the original was then deleted past the Recycle Bin.</summary>
    <Fact>
    Public Sub A_short_output_keeps_the_original()
        Dim encode As VideoReplaceOutcome = VideoConvertPlan.DecideEncode(
            cancelled:=False, exitCode:=0, tempExists:=True, tempBytes:=12213,
            expectedDurationMs:=3000, fps:=10, framesWritten:=11, outTimeMs:=900)
        Assert.Equal(VideoReplaceOutcome.OutputIncomplete, encode)
        Assert.False(VideoConvertPlan.ShouldDeleteOriginal(encode, swapped:=True))
        Assert.False(VideoConvertPlan.ShouldPlaceVideo(encode))
        Assert.True(VideoConvertPlan.ShouldRemoveTemp(VideoConvertPlan.DecideReplace(encode, swapped:=False, originalDeleted:=False)))
    End Sub

    ''' <summary>The same measurement, complete: 30 frames, last timestamp 2.8 s - one frame
    ''' short of the 3 s the animation lasts, which is what out_time always reports.</summary>
    <Fact>
    Public Sub A_complete_output_measured_from_the_real_encoder_passes()
        Assert.Equal(VideoReplaceOutcome.EncodedOk,
                     VideoConvertPlan.DecideEncode(False, 0, True, 23681, 3000, 10, 30, 2800))
    End Sub

    <Fact>
    Public Sub Enough_frames_but_the_wrong_duration_is_incomplete()
        Assert.Equal(VideoReplaceOutcome.OutputIncomplete,
                     VideoConvertPlan.DecideEncode(False, 0, True, Big_Enough, 3000, 10, 30, 1500))
    End Sub

    ''' <summary>A two-frame, one-second-per-frame GIF: out_time is 1 s for 2 s of animation,
    ''' and that is complete - the tolerance is at least one and a half frames.</summary>
    <Fact>
    Public Sub A_tiny_animation_is_not_failed_by_the_last_frame_rule()
        Assert.Equal(VideoReplaceOutcome.EncodedOk,
                     VideoConvertPlan.DecideEncode(False, 0, True, Big_Enough, 2000, 1, 2, 1000))
    End Sub

    ''' <summary>Nothing to check against - no duration, or no progress report - keeps the
    ''' video AND the original: the delete waits on a proof, never on its absence.</summary>
    <Fact>
    Public Sub An_unverifiable_encode_places_the_video_and_keeps_the_original()
        For Each encode As VideoReplaceOutcome In New VideoReplaceOutcome() {
                VideoConvertPlan.DecideEncode(False, 0, True, Big_Enough, 0, 10, 30, 2800),
                VideoConvertPlan.DecideEncode(False, 0, True, Big_Enough, 3000, 10, -1, 2800),
                VideoConvertPlan.DecideEncode(False, 0, True, Big_Enough, 3000, 10, 30, -1)}
            Assert.Equal(VideoReplaceOutcome.EncodedUnverified, encode)
            Assert.True(VideoConvertPlan.ShouldPlaceVideo(encode))
            Assert.False(VideoConvertPlan.ShouldDeleteOriginal(encode, swapped:=True))
            Assert.Equal(VideoReplaceOutcome.OriginalKeptUnverified, VideoConvertPlan.DecideReplace(encode, swapped:=True, originalDeleted:=False))
            Assert.Equal(VideoReplaceOutcome.SwapFailed, VideoConvertPlan.DecideReplace(encode, swapped:=False, originalDeleted:=False))
            Assert.False(VideoConvertPlan.ShouldRemoveTemp(VideoReplaceOutcome.OriginalKeptUnverified))
        Next
    End Sub

    <Fact>
    Public Sub The_progress_stream_is_read_for_frames_and_time()
        Assert.Equal(30L, VideoConvertPlan.ParseProgressValue("frame=30", "frame"))
        Assert.Equal(2800L, VideoConvertPlan.ParseProgressValue("out_time=00:00:02.800000", "out_time"))
        Assert.Equal(-1L, VideoConvertPlan.ParseProgressValue("out_time_ms=2800000", "out_time"))
        Assert.Equal(-1L, VideoConvertPlan.ParseProgressValue("fps=12.5", "frame"))
        Assert.Equal(-1L, VideoConvertPlan.ParseProgressValue("frame=N/A", "frame"))
    End Sub

    <Fact>
    Public Sub The_encoder_stops_on_a_decode_error()
        Assert.Contains("-xerror", VideoConvertPlan.BuildArguments("a.webp", "b.mp4", 10))
    End Sub

    ' --- the delete ------------------------------------------------------------

    <Fact>
    Public Sub Only_a_good_encode_that_reached_its_final_name_deletes_the_original()
        Assert.True(VideoConvertPlan.ShouldDeleteOriginal(VideoReplaceOutcome.EncodedOk, swapped:=True))
    End Sub

    <Fact>
    Public Sub Nothing_else_deletes_the_original()
        ' Encoded, but the rename onto the target failed - there is no video to replace it.
        Assert.False(VideoConvertPlan.ShouldDeleteOriginal(VideoReplaceOutcome.EncodedOk, swapped:=False))

        For Each outcome As VideoReplaceOutcome In New VideoReplaceOutcome() {
                VideoReplaceOutcome.Cancelled, VideoReplaceOutcome.EncoderFailed,
                VideoReplaceOutcome.OutputMissing, VideoReplaceOutcome.OutputTooSmall,
                VideoReplaceOutcome.SwapFailed, VideoReplaceOutcome.Success,
                VideoReplaceOutcome.OriginalNotDeleted, VideoReplaceOutcome.OutputIncomplete,
                VideoReplaceOutcome.EncodedUnverified, VideoReplaceOutcome.OriginalKeptUnverified}
            Assert.False(VideoConvertPlan.ShouldDeleteOriginal(outcome, swapped:=True),
                         "Deleting the original is allowed for " & outcome.ToString())
            Assert.False(VideoConvertPlan.ShouldDeleteOriginal(outcome, swapped:=False),
                         "Deleting the original is allowed for " & outcome.ToString())
        Next
    End Sub

    ' --- what the whole operation reports --------------------------------------

    <Fact>
    Public Sub Everything_worked_is_reported_as_success()
        Assert.Equal(VideoReplaceOutcome.Success,
                     VideoConvertPlan.DecideReplace(VideoReplaceOutcome.EncodedOk, swapped:=True, originalDeleted:=True))
    End Sub

    ''' <summary>A failed delete does NOT roll the video back: the user gets the video plus a
    ''' message naming the reason. Silently deleting the new file to restore symmetry would
    ''' throw away work that succeeded.</summary>
    <Fact>
    Public Sub A_failed_delete_keeps_the_video_and_says_so()
        Assert.Equal(VideoReplaceOutcome.OriginalNotDeleted,
                     VideoConvertPlan.DecideReplace(VideoReplaceOutcome.EncodedOk, swapped:=True, originalDeleted:=False))
    End Sub

    <Fact>
    Public Sub A_failed_swap_is_its_own_answer()
        Assert.Equal(VideoReplaceOutcome.SwapFailed,
                     VideoConvertPlan.DecideReplace(VideoReplaceOutcome.EncodedOk, swapped:=False, originalDeleted:=False))
    End Sub

    <Fact>
    Public Sub An_encode_that_failed_is_reported_as_it_failed()
        For Each encode As VideoReplaceOutcome In New VideoReplaceOutcome() {
                VideoReplaceOutcome.Cancelled, VideoReplaceOutcome.EncoderFailed,
                VideoReplaceOutcome.OutputMissing, VideoReplaceOutcome.OutputTooSmall}
            Assert.Equal(encode, VideoConvertPlan.DecideReplace(encode, swapped:=False, originalDeleted:=False))
            ' ..and it stays that way even if the later steps somehow claim to have worked.
            Assert.Equal(encode, VideoConvertPlan.DecideReplace(encode, swapped:=True, originalDeleted:=True))
        Next
    End Sub

    ' --- the temporary file ----------------------------------------------------

    ''' <summary>The temp file survives exactly the two outcomes in which it has already
    ''' BECOME the target. Removing it in either of those would delete the video.</summary>
    <Fact>
    Public Sub The_temp_file_is_removed_unless_it_became_the_target()
        Assert.False(VideoConvertPlan.ShouldRemoveTemp(VideoReplaceOutcome.Success))
        Assert.False(VideoConvertPlan.ShouldRemoveTemp(VideoReplaceOutcome.OriginalNotDeleted))

        For Each outcome As VideoReplaceOutcome In New VideoReplaceOutcome() {
                VideoReplaceOutcome.Cancelled, VideoReplaceOutcome.EncoderFailed,
                VideoReplaceOutcome.OutputMissing, VideoReplaceOutcome.OutputTooSmall,
                VideoReplaceOutcome.SwapFailed, VideoReplaceOutcome.OutputIncomplete}
            Assert.True(VideoConvertPlan.ShouldRemoveTemp(outcome),
                        "A leftover temp file survives " & outcome.ToString())
        Next
    End Sub

End Class
#End If
