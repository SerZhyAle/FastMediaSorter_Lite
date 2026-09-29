Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 086 - the follow-up of the audit concern A-loading (slices 071 and 076):
''' 057.S01-5 (a transport failure in a subfolder fails the scan), 057.S01-6 (one running
''' display decode per path), 057.S02-4 (the throttle refuses only repeatable modes),
''' 057.S02-6 (the recent-folder decision) and, on the mainline leg, 057.S02-8 (a session's
''' temporary copy is never persisted).
''' </summary>
Public Class LoadingFollowupTests

    ' ------------------------------------------------------------------ 057.S01-5 ----

    ''' <summary>A folder tree in memory, where any folder can be told to fail.</summary>
    Private NotInheritable Class FakeLister
        Implements FolderScan.IDirectoryLister

        Public ReadOnly Files As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly Children As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly Failures As New Dictionary(Of String, Exception)(StringComparer.OrdinalIgnoreCase)

        Public Function IsReparsePoint(directory As String) As Boolean Implements FolderScan.IDirectoryLister.IsReparsePoint
            Return False
        End Function

        Public Iterator Function ListFiles(directory As String) As IEnumerable(Of FileInfo) Implements FolderScan.IDirectoryLister.ListFiles
            Dim failure As Exception = Nothing
            If Failures.TryGetValue(directory, failure) Then Throw failure
            Dim names As String() = Nothing
            If Files.TryGetValue(directory, names) Then
                For Each name As String In names
                    Yield New FileInfo(Path.Combine(directory, name))
                Next
            End If
        End Function

        Public Iterator Function ListSubdirectories(directory As String) As IEnumerable(Of String) Implements FolderScan.IDirectoryLister.ListSubdirectories
            Dim names As String() = Nothing
            If Children.TryGetValue(directory, names) Then
                For Each name As String In names
                    Yield Path.Combine(directory, name)
                Next
            End If
        End Function
    End Class

    Private Const Root As String = "C:\fake-scan-root"

    ''' <summary>Root with a.jpg and two subfolders: "dropped" (told to fail) and "ok" (b.jpg).</summary>
    Private Shared Function TreeWithFailingChild(failure As Exception) As FakeLister
        Dim lister As New FakeLister()
        lister.Files(Root) = {"a.jpg"}
        lister.Children(Root) = {"dropped", "ok"}
        lister.Files(Path.Combine(Root, "ok")) = {"b.jpg"}
        lister.Failures(Path.Combine(Root, "dropped")) = failure
        Return lister
    End Function

    ''' <summary>ERROR_NETNAME_DELETED - what a dropped SMB session answers.</summary>
    Private Shared Function SessionDropped() As IOException
        Return New IOException("The specified network name is no longer available.", &H80070040)
    End Function

    <Fact>
    Public Sub A_subfolder_whose_session_dropped_fails_the_whole_scan()
        Dim lister As FakeLister = TreeWithFailingChild(SessionDropped())
        Assert.Throws(Of IOException)(Function() FolderScan.ScanTree(Root, True, CancellationToken.None, lister))
    End Sub

    <Fact>
    Public Sub A_denied_subfolder_is_skipped_like_Explorer_does()
        Dim lister As FakeLister = TreeWithFailingChild(New UnauthorizedAccessException("denied"))
        Dim names As New List(Of String)()
        For Each f As FileInfo In FolderScan.ScanTree(Root, True, CancellationToken.None, lister)
            names.Add(f.Name)
        Next
        names.Sort(StringComparer.Ordinal)
        Assert.Equal(New String() {"a.jpg", "b.jpg"}, names.ToArray())
    End Sub

    <Fact>
    Public Sub A_subfolder_gone_since_it_was_listed_is_skipped()
        Dim lister As FakeLister = TreeWithFailingChild(New DirectoryNotFoundException("gone"))
        Assert.Equal(2, FolderScan.ScanTree(Root, True, CancellationToken.None, lister).Count)
    End Sub

    <Fact>
    Public Sub The_roots_own_failure_still_reaches_the_caller()
        Dim lister As New FakeLister()
        lister.Failures(Root) = New UnauthorizedAccessException("denied")
        Assert.Throws(Of UnauthorizedAccessException)(Function() FolderScan.ScanTree(Root, True, CancellationToken.None, lister))
        Assert.Throws(Of UnauthorizedAccessException)(Function() FolderScan.ScanTree(Root, False, CancellationToken.None, lister))
    End Sub

    <Fact>
    Public Sub Only_a_transport_failure_of_a_child_is_not_skippable()
        Assert.False(FolderScan.IsSkippableChildFailure(SessionDropped()))
        Assert.True(FolderScan.IsSkippableChildFailure(New UnauthorizedAccessException()))
        Assert.True(FolderScan.IsSkippableChildFailure(New DirectoryNotFoundException()))
        Assert.True(FolderScan.IsSkippableChildFailure(New PathTooLongException()))
        ' Not an I/O failure at all: never swallowed, as before.
        Assert.False(FolderScan.IsSkippableChildFailure(New InvalidOperationException()))
    End Sub

    ' ------------------------------------------------------------------ 057.S02-4 ----

    <Fact>
    Public Sub The_throttle_may_refuse_only_the_modes_a_held_key_or_a_timer_repeats()
        For Each repeatable As String In {NavigationRules.ReadMode_Next, NavigationRules.ReadMode_Prev,
                                          NavigationRules.ReadMode_JumpBy, NavigationRules.ReadMode_ForRandom,
                                          NavigationRules.ReadMode_InSlideShow, NavigationRules.ReadMode_Delete}
            Assert.True(NavigationRules.IsThrottledReadMode(repeatable), repeatable)
        Next
        ' Every one of these is called AFTER its caller changed state; refusing it dropped
        ' the display under a list that had already moved.
        For Each stateful As String In {NavigationRules.ReadMode_FolderAndKnownFile, NavigationRules.ReadMode_FolderAndFile,
                                        NavigationRules.ReadMode_SetFile, NavigationRules.ReadMode_AfterUndo,
                                        NavigationRules.ReadMode_Files, NavigationRules.ReadMode_ForSlideShow,
                                        NavigationRules.ReadMode_JumpTo}
            Assert.False(NavigationRules.IsThrottledReadMode(stateful), stateful)
        Next
    End Sub

    ' ------------------------------------------------------------------ 057.S02-6 ----

    <Fact>
    Public Sub A_folder_is_promoted_to_the_front_whatever_the_box_says()
        Dim recent As New List(Of String) From {"D:\a", "D:\b", "D:\c"}
        Assert.True(NavigationRules.PromoteRecentFolder(recent, "D:\c", 100))
        Assert.Equal(New String() {"D:\c", "D:\a", "D:\b"}, recent.ToArray())
        ' Already first: nothing changes, nothing to save.
        Assert.False(NavigationRules.PromoteRecentFolder(recent, "D:\c", 100))
        ' New: inserted first, held to the limit.
        Assert.True(NavigationRules.PromoteRecentFolder(recent, "E:\new", 3))
        Assert.Equal(New String() {"E:\new", "D:\c", "D:\a"}, recent.ToArray())
    End Sub

    <Fact>
    Public Sub A_limit_of_zero_records_nothing_and_empties_what_is_left()
        Dim recent As New List(Of String) From {"D:\a"}
        Assert.True(NavigationRules.PromoteRecentFolder(recent, "D:\b", 0))
        Assert.Empty(recent)
        Assert.False(NavigationRules.PromoteRecentFolder(recent, "D:\b", 0))
    End Sub

    ' ------------------------------------------------------------------ 057.S01-6 ----

    ''' <summary>Stands in for the Image + MemoryStream pair: counts its disposals.</summary>
    Private NotInheritable Class Payload
        Public Disposed As Integer
    End Class

    Private NotInheritable Class Harness
        Public ReadOnly Released As New List(Of Payload)()
        Public Starts As Integer
        Public ReadOnly Pending As New List(Of TaskCompletionSource(Of Payload))()
        Public ReadOnly Registry As InFlightDecodeRegistry(Of Payload)

        Public Sub New(Optional cap As Integer = 4)
            Registry = New InFlightDecodeRegistry(Of Payload)(
                Sub(p)
                    p.Disposed += 1
                    Released.Add(p)
                End Sub, cap)
        End Sub

        Public Function Acquire(key As String, Optional group As String = "C:\") As InFlightDecodeRegistry(Of Payload).Lease
            Return Registry.Acquire(key, group,
                                    Function()
                                        Starts += 1
                                        Dim source As New TaskCompletionSource(Of Payload)()
                                        Pending.Add(source)
                                        Return source.Task
                                    End Function)
        End Function
    End Class

    <Fact>
    Public Sub A_return_to_an_abandoned_decode_joins_it_instead_of_starting_another()
        Dim h As New Harness()
        Dim first As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("C:\big.heic")
        Assert.Equal(DecodeAdmission.Started, first.Admission)
        first.Abandon()

        Dim second As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("c:\BIG.heic")
        Assert.Equal(DecodeAdmission.Joined, second.Admission)
        Assert.Same(first.Task, second.Task)
        Assert.Equal(1, h.Starts)

        ' It lands while the second visit owns it: the result is the owner's, not released.
        Dim result As New Payload()
        h.Pending(0).SetResult(result)
        Assert.Same(result, second.Task.Result)
        Assert.Empty(h.Released)
        Assert.Equal(0, h.Registry.InFlightCount)
    End Sub

    <Fact>
    Public Sub An_abandoned_decode_that_lands_unowned_is_released_exactly_once()
        Dim h As New Harness()
        Dim lease As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("C:\x.webp")
        lease.Abandon()
        Assert.Equal(1, h.Registry.AbandonedCount)

        Dim result As New Payload()
        h.Pending(0).SetResult(result)
        Assert.Equal(1, result.Disposed)
        Assert.Equal(0, h.Registry.InFlightCount)
        Assert.Equal(0, h.Registry.AbandonedCount)

        ' The next visit starts afresh - the old result is gone.
        Assert.Equal(DecodeAdmission.Started, h.Acquire("C:\x.webp").Admission)
        Assert.Equal(2, h.Starts)
    End Sub

    <Fact>
    Public Sub A_decode_that_lands_between_the_deadline_and_the_abandon_is_released_by_the_abandon()
        Dim h As New Harness()
        Dim lease As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("C:\x.webp")
        Dim result As New Payload()
        h.Pending(0).SetResult(result)      ' completes while still owned: left alone
        Assert.Empty(h.Released)
        lease.Abandon()                      ' the owner never took it
        Assert.Equal(1, result.Disposed)
        lease.Abandon()                      ' a second abandon changes nothing
        Assert.Equal(1, result.Disposed)
    End Sub

    <Fact>
    Public Sub An_owned_running_decode_is_never_duplicated()
        Dim h As New Harness()
        h.Acquire("C:\x.webp")
        Dim again As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("C:\x.webp")
        Assert.Equal(DecodeAdmission.Busy, again.Admission)
        Assert.Null(again.Task)
        Assert.Equal(1, h.Starts)
    End Sub

    <Fact>
    Public Sub A_completed_owned_decode_does_not_block_a_new_request()
        Dim h As New Harness()
        h.Acquire("C:\x.jpg")
        h.Pending(0).SetResult(New Payload())
        Assert.Equal(DecodeAdmission.Started, h.Acquire("C:\x.jpg").Admission)
        Assert.Equal(2, h.Starts)
    End Sub

    <Fact>
    Public Sub The_abandoned_cap_refuses_a_new_path_on_the_same_volume_only()
        Dim h As New Harness(cap:=2)
        h.Acquire("\\nas\share\1.jpg", "\\nas\share").Abandon()
        h.Acquire("\\nas\share\2.jpg", "\\nas\share").Abandon()

        Dim refused As InFlightDecodeRegistry(Of Payload).Lease = h.Acquire("\\nas\share\3.jpg", "\\nas\share")
        Assert.Equal(DecodeAdmission.Refused, refused.Admission)
        Assert.Null(refused.Task)
        Assert.Equal(2, h.Starts)

        ' A local file is not held hostage by a dead share.
        Assert.Equal(DecodeAdmission.Started, h.Acquire("C:\local.jpg", "C:\").Admission)
        ' Joining a stuck one is still allowed at the cap - it starts nothing.
        Assert.Equal(DecodeAdmission.Joined, h.Acquire("\\nas\share\1.jpg", "\\nas\share").Admission)

        ' One of them finally lands: the share has room again.
        h.Pending(1).SetResult(New Payload())
        Assert.Equal(DecodeAdmission.Started, h.Acquire("\\nas\share\3.jpg", "\\nas\share").Admission)
    End Sub

    <Fact>
    Public Sub A_faulted_abandoned_decode_releases_nothing_and_leaves_the_table()
        Dim h As New Harness()
        h.Acquire("C:\x.jpg").Abandon()
        h.Pending(0).SetException(New IOException("gone"))
        Assert.Empty(h.Released)
        Assert.Equal(0, h.Registry.InFlightCount)
    End Sub

#If Not NETFRAMEWORK Then
    ' ------------------------------------------------------------------ 057.S02-8 ----

    <Fact>
    Public Sub A_sessions_temporary_copy_is_never_a_persistable_path()
        Dim archive_Root As String = "C:\u\SZA\FastMediaSorter\archive-cache"
        Dim remote_Root As String = "C:\u\SZA\FastMediaSorter\remote-cache"
        Assert.True(RemoteCachePaths.IsSessionCopy(remote_Root & "\1234-abcd\00001_x.jpg", archive_Root, remote_Root))
        Assert.True(RemoteCachePaths.IsSessionCopy(archive_Root & "\1234-abcd\00001_x.jpg", archive_Root, remote_Root))
        Assert.False(RemoteCachePaths.IsSessionCopy("D:\Photos\x.jpg", archive_Root, remote_Root))
        Assert.False(RemoteCachePaths.IsSessionCopy(remote_Root & "-old\x.jpg", archive_Root, remote_Root))
        Assert.False(RemoteCachePaths.IsSessionCopy("", archive_Root, remote_Root))
        ' Against the real roots too - what the two recent-list filters ask.
        Assert.True(RemoteCachePaths.IsSessionCopy(Path.Combine(RemoteCachePaths.CacheRoot(), "1-a", "00001_x.jpg")))
        Assert.True(RemoteCachePaths.IsSessionCopy(Path.Combine(ArchivePaths.CacheRoot(), "1-a", "00001_x.jpg")))
    End Sub
#End If

End Class
