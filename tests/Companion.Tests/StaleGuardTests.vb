Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The pure half of the stale-result guard (SETTINGS-NAVIGATION 2.0 section 5 rule 3, ticket 243 T-13): a token is
''' current until a newer request of the same kind or an invalidation; no token is ever 0; the counter survives
''' concurrent use from many threads. The windows that use it are held by <see cref="SettingsPageStaleResultTests"/>.
''' </summary>
Public Class StaleGuardTests

    <Fact>
    Public Sub A_fresh_guard_has_issued_nothing_and_token_zero_is_never_current()
        Dim guard As New StaleGuard()
        Assert.Equal(0L, guard.Generation)
        Assert.False(guard.IsCurrent(0L))
        Assert.False(guard.IsCurrent(1L))
    End Sub

    <Fact>
    Public Sub Begin_returns_a_growing_token_and_only_the_newest_is_current()
        Dim guard As New StaleGuard()
        Dim first As Long = guard.Begin()
        Assert.True(first > 0L)
        Assert.True(guard.IsCurrent(first))
        Dim second As Long = guard.Begin()
        Assert.True(second > first)
        Assert.False(guard.IsCurrent(first), "the older request is superseded")
        Assert.True(guard.IsCurrent(second))
        Dim third As Long = guard.Begin()
        Assert.False(guard.IsCurrent(second))
        Assert.False(guard.IsCurrent(first))
        Assert.True(guard.IsCurrent(third))
    End Sub

    <Fact>
    Public Sub Invalidate_makes_the_outstanding_token_stale_and_a_later_begin_is_current_again()
        Dim guard As New StaleGuard()
        Dim token As Long = guard.Begin()
        guard.Invalidate()
        Assert.False(guard.IsCurrent(token))
        Dim newer As Long = guard.Begin()
        Assert.True(newer > token)
        Assert.True(guard.IsCurrent(newer))
        Assert.False(guard.IsCurrent(token), "an invalidated token never comes back")
    End Sub

    <Fact>
    Public Sub Invalidate_on_its_own_moves_the_generation_so_no_old_token_can_match_it()
        Dim guard As New StaleGuard()
        Dim before As Long = guard.Generation
        guard.Invalidate()
        guard.Invalidate()
        Assert.Equal(before + 2L, guard.Generation)
        Assert.False(guard.IsCurrent(before), "a token of the generation before the invalidation is stale")
        Assert.False(guard.IsCurrent(guard.Generation - 1L))
    End Sub

    <Fact>
    Public Sub Two_guards_are_independent_kinds_of_work()
        Dim hosting As New StaleGuard()
        Dim accounts As New StaleGuard()
        Dim h As Long = hosting.Begin()
        Dim a As Long = accounts.Begin()
        accounts.Begin()
        Assert.True(hosting.IsCurrent(h), "a newer accounts read does not supersede the hosting probe")
        Assert.False(accounts.IsCurrent(a))
    End Sub

    <Fact>
    Public Sub Many_threads_beginning_at_once_get_distinct_tokens_and_exactly_one_stays_current()
        Dim guard As New StaleGuard()
        Dim threads As Integer = 8
        Dim perThread As Integer = 2000
        Dim tokens As New ConcurrentBag(Of Long)()
        Dim start As New ManualResetEventSlim(False)
        Dim workers As New List(Of Task)()
        For t As Integer = 1 To threads
            workers.Add(Task.Factory.StartNew(Sub()
                                                  start.Wait()
                                                  For i As Integer = 1 To perThread
                                                      tokens.Add(guard.Begin())
                                                  Next
                                              End Sub, TaskCreationOptions.LongRunning))
        Next
        start.Set()
        Assert.True(Task.WaitAll(workers.ToArray(), TimeSpan.FromSeconds(20)), "the workers did not finish")
        Assert.Equal(threads * perThread, tokens.Count)
        Assert.Equal(threads * perThread, tokens.Distinct().Count())
        Assert.Equal(CLng(threads * perThread), guard.Generation)
        Assert.Equal(1, tokens.Where(Function(token) guard.IsCurrent(token)).Count())
    End Sub

    <Fact>
    Public Sub Begin_and_invalidate_racing_never_lose_a_step()
        Dim guard As New StaleGuard()
        Dim start As New ManualResetEventSlim(False)
        Dim workers As New List(Of Task)()
        For t As Integer = 1 To 4
            workers.Add(Task.Factory.StartNew(Sub()
                                                  start.Wait()
                                                  For i As Integer = 1 To 1500
                                                      guard.Begin()
                                                  Next
                                              End Sub, TaskCreationOptions.LongRunning))
            workers.Add(Task.Factory.StartNew(Sub()
                                                  start.Wait()
                                                  For i As Integer = 1 To 1500
                                                      guard.Invalidate()
                                                  Next
                                              End Sub, TaskCreationOptions.LongRunning))
        Next
        start.Set()
        Assert.True(Task.WaitAll(workers.ToArray(), TimeSpan.FromSeconds(20)), "the workers did not finish")
        Assert.Equal(8L * 1500L, guard.Generation)
    End Sub

End Class
