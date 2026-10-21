#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Animated graphics through video (specification 242), step T-7: the slot of the video prepared one file ahead
''' (rules 20 and 24). Pure: the lease is a counting stand-in, there is no process, no container and no form.
''' </summary>
Public Class AnimationVideoAheadTests

    Private NotInheritable Class FakeLease
        Implements IDisposable

        Friend DisposeCount As Integer

        Public Sub Dispose() Implements IDisposable.Dispose
            Interlocked.Increment(DisposeCount)
        End Sub
    End Class

    Private Const PathB As String = "C:\pics\b.webp"
    Private Const PathC As String = "C:\pics\c.webp"
    Private Const StampB As String = "100:1"

    Private Shared Function NewSlot() As AnimationVideoAheadSlot(Of FakeLease)
        Return New AnimationVideoAheadSlot(Of FakeLease)()
    End Function

    <Fact>
    Public Sub An_empty_slot_has_no_target_and_accepts_a_preparation()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Assert.Equal("None", slot.Phase.ToString())
        Assert.Equal("", slot.TargetPath)
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Assert.NotEqual(0, ticket)
        Assert.Equal("Preparing", slot.Phase.ToString())
        Assert.Equal(PathB, slot.TargetPath)
        Assert.True(slot.IsCurrent(ticket))
    End Sub

    <Theory>
    <InlineData("", "100:1")>
    <InlineData("C:\pics\b.webp", "")>
    <InlineData(Nothing, "100:1")>
    Public Sub An_empty_argument_begins_nothing(path As String, stamp As String)
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Assert.Equal(0, slot.TryBegin(path, stamp))
        Assert.Equal("None", slot.Phase.ToString())
    End Sub

    <Fact>
    Public Sub An_occupied_slot_refuses_a_second_target_until_it_is_emptied()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Assert.NotEqual(0, slot.TryBegin(PathB, StampB))
        Assert.Equal(0, slot.TryBegin(PathB, StampB))
        Assert.Equal(0, slot.TryBegin(PathC, "1:1"))
        Dim wasPreparing As Boolean
        Assert.Null(slot.Discard(wasPreparing))
        Assert.True(wasPreparing)
        Assert.NotEqual(0, slot.TryBegin(PathC, "1:1"))
        Assert.Equal(PathC, slot.TargetPath)
    End Sub

    <Fact>
    Public Sub A_finished_preparation_is_parked_when_nobody_landed()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        Assert.Equal("Parked", slot.Complete(ticket, lease, waiter).ToString())
        Assert.Null(waiter)
        Assert.Equal("Ready", slot.Phase.ToString())
        Assert.Equal(0, lease.DisposeCount)
        Assert.False(slot.IsCurrent(ticket))
    End Sub

    <Fact>
    Public Sub Landing_on_a_ready_target_takes_the_container_out_of_the_slot()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        slot.Complete(ticket, lease, waiter)

        Dim taken As FakeLease = Nothing
        Assert.Equal("Ready", slot.Promote(PathB.ToUpperInvariant(), StampB, "w", taken).ToString())
        Assert.Same(lease, taken)
        Assert.Equal("None", slot.Phase.ToString())
        Assert.Equal(0, lease.DisposeCount)
    End Sub

    <Fact>
    Public Sub Landing_on_a_target_still_being_prepared_waits_and_gets_the_container_once()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim taken As FakeLease = Nothing
        Assert.Equal("Waiting", slot.Promote(PathB, StampB, "the landing", taken).ToString())
        Assert.Null(taken)

        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        Assert.Equal("Delivered", slot.Complete(ticket, lease, waiter).ToString())
        Assert.Equal("the landing", waiter)
        Assert.Equal("None", slot.Phase.ToString())
        Assert.Equal(0, lease.DisposeCount)

        ' a second answer for the same ticket is stale
        Assert.Equal("Stale", slot.Complete(ticket, New FakeLease(), waiter).ToString())
    End Sub

    <Fact>
    Public Sub A_later_landing_replaces_the_earlier_waiter()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim taken As FakeLease = Nothing
        slot.Promote(PathB, StampB, "first", taken)
        slot.Promote(PathB, StampB, "second", taken)
        Dim waiter As Object = Nothing
        Assert.Equal("Delivered", slot.Complete(ticket, New FakeLease(), waiter).ToString())
        Assert.Equal("second", waiter)
    End Sub

    <Fact>
    Public Sub A_changed_file_or_another_file_is_not_ahead()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        slot.Complete(ticket, lease, waiter)

        Dim taken As FakeLease = Nothing
        Assert.Equal("NotAhead", slot.Promote(PathB, "999:9", "w", taken).ToString())
        Assert.Equal("NotAhead", slot.Promote(PathC, StampB, "w", taken).ToString())
        Assert.Null(taken)
        ' the slot was left alone: emptying it is the caller's decision, and it gets the container back
        Assert.Equal("Ready", slot.Phase.ToString())
        Dim wasPreparing As Boolean
        Assert.Same(lease, slot.Discard(wasPreparing))
        Assert.False(wasPreparing)
        Assert.Equal("None", slot.Phase.ToString())
    End Sub

    <Fact>
    Public Sub A_preparation_that_finishes_after_a_discard_is_stale()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim wasPreparing As Boolean
        slot.Discard(wasPreparing)
        Assert.True(wasPreparing)

        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        Assert.Equal("Stale", slot.Complete(ticket, lease, waiter).ToString())
        Assert.False(slot.IsCurrent(ticket))
        Assert.Equal("None", slot.Phase.ToString())
    End Sub

    <Fact>
    Public Sub A_preparation_that_finishes_after_the_slot_was_aimed_elsewhere_is_stale()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim oldTicket As Integer = slot.TryBegin(PathB, StampB)
        Dim wasPreparing As Boolean
        slot.Discard(wasPreparing)
        Dim newTicket As Integer = slot.TryBegin(PathC, "1:1")
        Assert.NotEqual(oldTicket, newTicket)

        Dim waiter As Object = Nothing
        Assert.Equal("Stale", slot.Complete(oldTicket, New FakeLease(), waiter).ToString())
        Assert.True(slot.IsCurrent(newTicket))
        Assert.Equal(PathC, slot.TargetPath)
    End Sub

    <Fact>
    Public Sub Discarding_a_ready_slot_returns_the_container_to_be_disposed()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        slot.Complete(ticket, lease, waiter)
        Dim wasPreparing As Boolean = True
        Assert.Same(lease, slot.Discard(wasPreparing))
        Assert.False(wasPreparing)
        Assert.Null(slot.Discard(wasPreparing))
    End Sub

    <Fact>
    Public Sub A_failed_preparation_empties_the_slot_and_returns_the_waiter()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim taken As FakeLease = Nothing
        slot.Promote(PathB, StampB, "waiting", taken)

        Dim waiter As Object = Nothing
        Assert.True(slot.Fail(ticket, waiter))
        Assert.Equal("waiting", waiter)
        Assert.Equal("None", slot.Phase.ToString())
        ' the same answer twice is stale, and nobody waits any more
        Assert.False(slot.Fail(ticket, waiter))
        Assert.Null(waiter)
    End Sub

    <Fact>
    Public Sub A_failed_preparation_nobody_waits_for_just_empties_the_slot()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim waiter As Object = Nothing
        Assert.True(slot.Fail(ticket, waiter))
        Assert.Null(waiter)
        Assert.NotEqual(0, slot.TryBegin(PathC, "1:1"))
    End Sub

    <Fact>
    Public Sub Reconcile_keeps_the_target_and_empties_anything_else()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim wasPreparing As Boolean
        ' the file on screen IS the target: it may be about to be delivered
        Assert.Null(slot.Reconcile(PathB.ToUpperInvariant(), wasPreparing))
        Assert.False(wasPreparing)
        Assert.True(slot.IsCurrent(ticket))
        ' the file on screen is another one: a running preparation is cancelled by the caller
        Assert.Null(slot.Reconcile(PathC, wasPreparing))
        Assert.True(wasPreparing)
        Assert.Equal("None", slot.Phase.ToString())
        ' an empty slot has nothing to reconcile
        Assert.Null(slot.Reconcile(PathC, wasPreparing))
        Assert.False(wasPreparing)
    End Sub

    <Fact>
    Public Sub Reconcile_returns_the_parked_container_of_a_file_left_behind()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim lease As New FakeLease()
        Dim waiter As Object = Nothing
        slot.Complete(ticket, lease, waiter)
        Dim wasPreparing As Boolean = True
        Assert.Same(lease, slot.Reconcile(PathC, wasPreparing))
        Assert.False(wasPreparing)
        Assert.Equal("None", slot.Phase.ToString())
    End Sub

    <Fact>
    Public Sub A_null_container_is_never_parked()
        Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
        Dim ticket As Integer = slot.TryBegin(PathB, StampB)
        Dim waiter As Object = Nothing
        Assert.Equal("Stale", slot.Complete(ticket, Nothing, waiter).ToString())
        Assert.True(slot.IsCurrent(ticket))
    End Sub

    <Fact>
    Public Async Function Concurrent_answers_deliver_or_park_a_container_exactly_once() As Task
        ' A landing racing a completion: whichever wins, the lease is owned by exactly one party.
        For round As Integer = 1 To 200
            Dim slot As AnimationVideoAheadSlot(Of FakeLease) = NewSlot()
            Dim ticket As Integer = slot.TryBegin(PathB, StampB)
            Dim lease As New FakeLease()
            Dim delivered As Integer = 0
            Dim owners As Integer = 0

            Dim completer As Task = Task.Run(Sub()
                                                 Dim waiter As Object = Nothing
                                                 Dim outcome As AheadCompletion = slot.Complete(ticket, lease, waiter)
                                                 If outcome = AheadCompletion.Delivered Then Interlocked.Increment(delivered)
                                             End Sub)
            Dim lander As Task = Task.Run(Sub()
                                              Dim taken As FakeLease = Nothing
                                              Dim result As AheadPromotion = slot.Promote(PathB, StampB, "w", taken)
                                              If result = AheadPromotion.Ready Then Interlocked.Increment(owners)
                                          End Sub)
            Await Task.WhenAll(completer, lander)

            ' Delivered (waiter set before completion) xor taken by the landing (Ready) xor still parked
            Dim parked As Integer = If(slot.Phase = AheadPhase.Ready, 1, 0)
            Assert.Equal(1, delivered + owners + parked)
        Next
    End Function

End Class
#End If
