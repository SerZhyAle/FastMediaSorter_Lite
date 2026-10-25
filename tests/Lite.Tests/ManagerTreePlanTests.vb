#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Linq
Imports Xunit

Public Class ManagerTreePlanTests
    <Fact>
    Public Sub A_skipped_subtree_is_reported_in_full_and_can_never_authorize_move_cleanup()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutDirectory("/root/tree/child")
        source.PutFile("/root/tree/child/a")
        Dim destination As New ManagerFakeProvider()
        destination.PutDirectory("/root/tree")
        destination.PutDirectory("/root/tree/child")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Move, ManagerTestLease(source), {"/root/tree"}, ManagerTestLease(destination, "dest"))
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            Assert.Equal(3, plan.Skipped.Count)
            Assert.Empty(plan.Operations)
        End Using
    End Sub

    <Fact>
    Public Sub Failure_to_create_one_child_does_not_block_an_independent_sibling()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutDirectory("/root/tree/failed")
        source.PutDirectory("/root/tree/ok")
        source.PutFile("/root/tree/ok/a")
        Dim destination As New ManagerFakeProvider()
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/tree"}, ManagerTestLease(destination, "dest"))
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            destination.PutFile("/root/tree/failed")
            For Each op As BatchOp In plan.Operations
                If op.Destination = "/root/tree/failed" Then
                    Assert.Throws(Of ManagerCommandException)(Sub() op.ManagerOperation.Execute("unused"))
                Else
                    op.ManagerOperation.Execute(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
                End If
                op.ManagerOperation.UndoData?.Dispose()
            Next
            Assert.True(destination.Entries.ContainsKey("/root/tree/ok/a"))
        End Using
    End Sub

    <Fact>
    Public Sub Full_manifest_collapses_overlap_and_preserves_hidden_files_and_empty_folders()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutDirectory("/root/tree/empty")
        source.PutFile("/root/tree/.hidden", "secret")
        source.Entries("/root/tree/.hidden").IsHidden = True
        Dim destination As New ManagerFakeProvider()
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source),
            {"/root/tree", "/root/tree/.hidden"}, ManagerTestLease(destination, "destination"))
            Dim tree As ManagerTreePlan = ManagerTreePlan.Capture(request)
            Assert.Single(tree.Roots)
            Assert.Equal(1, tree.FileCount)
            Assert.Equal(2, tree.FolderCount)
            Assert.Equal(6L, tree.TotalBytes)
            ExecuteManagerTestPlan(BuildManagerTestPlan(request))
            Assert.True(destination.Entries.ContainsKey("/root/tree/empty"))
            Assert.True(destination.Entries.ContainsKey("/root/tree/.hidden"))
            Assert.Equal(0, source.DeleteCalls)
        End Using
    End Sub

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Sub Denied_or_truncated_subtree_never_produces_an_executable_partial_manifest(denied As Boolean)
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a")
        If denied Then source.DeniedTree = "/root/tree" Else source.TruncatedTree = "/root/tree"
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Delete, ManagerTestLease(source), {"/root/tree"})
            Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
        End Using
    End Sub

    <Fact>
    Public Sub A_link_anywhere_in_the_tree_refuses_the_entire_destructive_plan()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a")
        source.Entries("/root/tree/a").IsLink = True
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(source), {"/root/tree"})
            Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
            Assert.Equal(0, source.DeleteCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Failed_child_blocks_parent_removal_even_if_another_actor_empties_it()
        Dim source As New ManagerFakeProvider With {.DenyDelete = True}
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(source), {"/root/tree"})
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            Assert.Equal(FileOpKind.Delete, plan.Operations(0).Kind)
            Assert.Equal(FileOpKind.RemoveEmptyDirectory, plan.Operations(1).Kind)
            Assert.Throws(Of UnauthorizedAccessException)(Sub() plan.Operations(0).ManagerOperation.Execute("unused"))
            source.Entries.Remove("/root/tree/a")
            Assert.Throws(Of ManagerCommandException)(Sub() plan.Operations(1).ManagerOperation.Execute("unused"))
            Assert.Equal(0, source.RemoveDirectoryCalls)
            Assert.True(source.Entries.ContainsKey("/root/tree"))
        End Using
    End Sub

    <Fact>
    Public Sub A_new_child_keeps_the_source_directory_after_successful_planned_deletion()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(source), {"/root/tree"})
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            plan.Operations(0).ManagerOperation.Execute("unused")
            source.PutFile("/root/tree/new")
            Assert.Throws(Of ManagerCommandException)(Sub() plan.Operations(1).ManagerOperation.Execute("unused"))
            Assert.Equal(0, source.RemoveDirectoryCalls)
            Assert.True(source.Entries.ContainsKey("/root/tree/new"))
        End Using
    End Sub

    <Fact>
    Public Sub Replace_merges_directories_without_deleting_unrelated_destination_children()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a", "new")
        Dim destination As New ManagerFakeProvider()
        destination.PutDirectory("/root/tree")
        destination.PutFile("/root/tree/unrelated", "keep")
        destination.PutFile("/root/tree/a", "old")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/tree"}, ManagerTestLease(destination, "dest"), collision:="replace")
            ExecuteManagerTestPlan(BuildManagerTestPlan(request))
            Assert.Equal("new", Text.Encoding.UTF8.GetString(destination.Bytes("/root/tree/a")))
            Assert.Equal("keep", Text.Encoding.UTF8.GetString(destination.Bytes("/root/tree/unrelated")))
            Assert.Equal(0, destination.RemoveDirectoryCalls)
        End Using
    End Sub
End Class
#End If
