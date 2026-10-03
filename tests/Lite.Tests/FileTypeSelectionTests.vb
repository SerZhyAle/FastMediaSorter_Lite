#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

''' <summary>
''' 105 L05 (057.S15-7): zero ticked types cannot be accepted; every type ticked keeps the
''' existing "empty list = all" representation; a subset round-trips through the stored form and
''' reopens as exactly that subset. No new persisted representation is introduced.
''' </summary>
Public Class FileTypeSelectionTests

    Private Shared ReadOnly All As String() = {".jpg", ".png", ".gif", ".mp4", ".mkv"}

    <Fact>
    Public Sub Zero_ticked_cannot_be_accepted_and_so_cannot_masquerade_as_all()
        Dim verdict As FileTypeSelection.Verdict = FileTypeSelection.Evaluate(All.Length, New String() {})
        Assert.False(verdict.CanAccept)
        ' The result is irrelevant when OK is disabled, but it must not be a usable "subset".
        Assert.Empty(verdict.Selection)
    End Sub

    <Fact>
    Public Sub Null_and_blank_entries_count_as_nothing_ticked()
        Assert.False(FileTypeSelection.Evaluate(5, Nothing).CanAccept)
        Assert.False(FileTypeSelection.Evaluate(5, New String() {"", Nothing}).CanAccept)
    End Sub

    <Fact>
    Public Sub Every_type_ticked_is_acceptable_and_stores_the_all_sentinel()
        Dim verdict As FileTypeSelection.Verdict = FileTypeSelection.Evaluate(All.Length, All)
        Assert.True(verdict.CanAccept)
        Assert.Empty(verdict.Selection)
    End Sub

    <Fact>
    Public Sub A_subset_is_acceptable_and_stored_exactly()
        Dim verdict As FileTypeSelection.Verdict = FileTypeSelection.Evaluate(All.Length, New String() {".png", ".mp4"})
        Assert.True(verdict.CanAccept)
        Assert.Equal(New String() {".png", ".mp4"}, verdict.Selection.ToArray())
    End Sub

    <Fact>
    Public Sub One_type_missing_is_a_subset_not_all()
        Dim verdict As FileTypeSelection.Verdict = FileTypeSelection.Evaluate(All.Length, All.Skip(1))
        Assert.True(verdict.CanAccept)
        Assert.Equal(All.Length - 1, verdict.Selection.Count)
    End Sub

    <Fact>
    Public Sub An_empty_stored_list_reopens_as_everything_ticked_and_a_subset_as_that_subset()
        Dim everything As HashSet(Of String) = FileTypeSelection.TickedOnOpen(New String() {}, All)
        Assert.Equal(All.Length, everything.Count)

        Dim subset As HashSet(Of String) = FileTypeSelection.TickedOnOpen(New String() {".PNG", ".mp4"}, All)
        Assert.Equal(2, subset.Count)
        Assert.Contains(".png", subset) ' case-insensitive, as the stored list is
        Assert.DoesNotContain(".jpg", subset)
    End Sub

    <Fact>
    Public Sub Open_then_accept_then_reopen_is_stable_for_zero_all_and_subset()
        For Each stored As String() In New String()() {New String() {}, New String() {".jpg", ".mkv"}}
            Dim ticked As HashSet(Of String) = FileTypeSelection.TickedOnOpen(stored, All)
            Dim verdict As FileTypeSelection.Verdict = FileTypeSelection.Evaluate(All.Length, All.Where(Function(e) ticked.Contains(e)))
            Assert.True(verdict.CanAccept)
            Assert.Equal(stored.Length, verdict.Selection.Count) ' what went in is what comes out
        Next
    End Sub

End Class
#End If
