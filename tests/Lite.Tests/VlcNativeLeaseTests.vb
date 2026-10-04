#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 115: the native LibVLC instance is freed only when nothing is inside it, and nothing
''' may enter once it is closing. The ordering is the whole fix, so it is asserted here
''' without a player.
''' </summary>
Public Class VlcNativeLeaseTests

    <Fact>
    Public Sub An_idle_lease_closes_at_once()
        Dim lease As New VlcNativeLease()
        Assert.True(lease.CloseAndWaitForIdle(0))
        Assert.True(lease.IsClosing)
    End Sub

    <Fact>
    Public Sub Work_in_flight_blocks_the_close_until_it_leaves()
        Dim lease As New VlcNativeLease()
        Assert.True(lease.TryEnter())
        Assert.Equal(1, lease.ActiveCount)
        Assert.False(lease.CloseAndWaitForIdle(20))
        lease.Leave()
        Assert.True(lease.CloseAndWaitForIdle(0))
    End Sub

    <Fact>
    Public Sub Nothing_enters_once_the_instance_is_closing()
        Dim lease As New VlcNativeLease()
        lease.CloseAndWaitForIdle(0)
        Assert.False(lease.TryEnter())
        Assert.Equal(0, lease.ActiveCount)
    End Sub

    <Fact>
    Public Sub A_close_waits_for_a_pool_thread_that_leaves_in_time()
        Dim lease As New VlcNativeLease()
        Assert.True(lease.TryEnter())
        Dim worker As Task = Task.Run(Sub()
                                          Thread.Sleep(80)
                                          lease.Leave()
                                      End Sub)
        Assert.True(lease.CloseAndWaitForIdle(5000))
        Assert.Equal(0, lease.ActiveCount)
        worker.Wait()
    End Sub

    <Fact>
    Public Sub Leave_without_enter_never_goes_negative()
        Dim lease As New VlcNativeLease()
        lease.Leave()
        Assert.Equal(0, lease.ActiveCount)
        Assert.True(lease.TryEnter())
        Assert.Equal(1, lease.ActiveCount)
    End Sub

    <Fact>
    Public Sub Concurrent_enter_and_leave_balance_out()
        Dim lease As New VlcNativeLease()
        Dim tasks(7) As Task
        For i As Integer = 0 To tasks.Length - 1
            tasks(i) = Task.Run(Sub()
                                    For n As Integer = 0 To 999
                                        If lease.TryEnter() Then lease.Leave()
                                    Next
                                End Sub)
        Next
        Task.WaitAll(tasks)
        Assert.Equal(0, lease.ActiveCount)
    End Sub

End Class
#End If
