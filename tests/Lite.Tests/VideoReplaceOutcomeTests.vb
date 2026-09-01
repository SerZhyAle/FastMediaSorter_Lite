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
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=True, tempBytes:=Big_Enough))
    End Sub

    <Fact>
    Public Sub A_non_zero_exit_code_is_a_failure()
        Assert.Equal(VideoReplaceOutcome.EncoderFailed,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=1, tempExists:=True, tempBytes:=Big_Enough))
    End Sub

    ''' <summary>FFmpeg can report success at nothing - a filter graph that produced no
    ''' frames exits 0 and writes no file.</summary>
    <Fact>
    Public Sub A_missing_output_is_a_failure_even_at_exit_code_zero()
        Assert.Equal(VideoReplaceOutcome.OutputMissing,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=False, tempBytes:=0))
    End Sub

    <Fact>
    Public Sub A_header_sized_output_is_a_failure()
        Assert.Equal(VideoReplaceOutcome.OutputTooSmall,
                     VideoConvertPlan.DecideEncode(cancelled:=False, exitCode:=0, tempExists:=True, tempBytes:=Big_Enough - 1))
    End Sub

    ''' <summary>Cancel outranks everything: a killed process usually also has a non-zero
    ''' exit code, and reporting that as an encoder failure would blame FFmpeg for the user
    ''' pressing a button.</summary>
    <Fact>
    Public Sub Cancel_outranks_the_exit_code()
        Assert.Equal(VideoReplaceOutcome.Cancelled,
                     VideoConvertPlan.DecideEncode(cancelled:=True, exitCode:=255, tempExists:=False, tempBytes:=0))
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
                VideoReplaceOutcome.OriginalNotDeleted}
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
                VideoReplaceOutcome.SwapFailed}
            Assert.True(VideoConvertPlan.ShouldRemoveTemp(outcome),
                        "A leftover temp file survives " & outcome.ToString())
        Next
    End Sub

End Class
#End If
