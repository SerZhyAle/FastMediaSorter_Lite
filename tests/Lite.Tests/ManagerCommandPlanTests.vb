#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports Xunit

Public Class ManagerCommandPlanTests
    <Fact>
    Public Sub Create_folder_uses_the_location_and_ignores_an_unrelated_selected_link()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/link")
        provider.Entries("/root/link").IsLink = True
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.CreateDirectory, ManagerTestLease(provider), {"/root/link"}, leaf:="new")
            Assert.True(ManagerCommandPlanner.Allows(request.Kind, request.Source.Capabilities, request.Entries, request.Origin))
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            Assert.Single(plan.Operations)
            ExecuteManagerTestPlan(plan)
            Assert.True(provider.Entries.ContainsKey("/root/new"))
        End Using
    End Sub

    <Fact>
    Public Sub A_directory_copy_requires_create_folder_capability_before_any_transfer()
        Dim provider As New ManagerFakeProvider()
        provider.PutDirectory("/root/tree")
        Using source As ManagerEndpointLease = ManagerTestLease(provider)
            Assert.False(ManagerCommandPlanner.Allows(ManagerCommandKind.Copy, source.Capabilities,
                {ManagerTestRow(source, "/root/tree")}, ManagerLocation.AtRoot(source.Place), New ProviderCapabilities(canCreateFolder:=False)))
        End Using
    End Sub

    <Fact>
    Public Sub A_row_from_another_place_cannot_authorize_a_same_path_in_this_provider()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Using source As ManagerEndpointLease = ManagerTestLease(provider), other As ManagerEndpointLease = ManagerTestLease(provider, "other")
            Dim row As ManagerEntry = ManagerTestRow(other, "/root/a")
            Dim location As ManagerLocation = ManagerLocation.AtRoot(source.Place)
            Assert.False(ManagerCommandPlanner.Allows(ManagerCommandKind.Delete, source.Capabilities, {row}, location))
            Using request As New ManagerCommandRequest(ManagerCommandKind.Delete, location, 1, {row}, source.Fork())
                Assert.Throws(Of ManagerCommandException)(Function() ManagerTreePlan.Capture(request))
            End Using
            Assert.Equal(0, provider.DeleteCalls)
        End Using
    End Sub

    <Fact>
    Public Sub A_named_refusal_is_formatted_from_typed_facts_and_not_exception_text()
        Dim refusal As ManagerCommandException = ManagerCommandPlanner.Failure("/root/a", ManagerFailure.SourceChanged)
        Assert.Contains("/root/a", FailureText.Describe(refusal))
        Assert.DoesNotContain("Manager command refused", FailureText.Describe(refusal))
        Dim serverFailure As New UnauthorizedAccessException("raw server diagnostics")
        Assert.DoesNotContain("raw server diagnostics", FailureText.Describe(serverFailure))
    End Sub

    <Fact>
    Public Sub Readonly_source_allows_copy_out_but_refuses_mutations_and_readonly_destination()
        Dim source As New ManagerFakeProvider With {.Caps = New ProviderCapabilities(canWrite:=False, canDelete:=False, canRename:=False, canCreateFolder:=False)}
        source.PutFile("/root/a")
        Using held As ManagerEndpointLease = ManagerTestLease(source)
            Dim rows As ManagerEntry() = {ManagerTestRow(held, "/root/a")}
            Dim location As ManagerLocation = ManagerLocation.AtRoot(held.Place)
            Assert.True(ManagerCommandPlanner.Allows(ManagerCommandKind.Copy, source.Caps, rows, location))
            For Each verb As ManagerCommandKind In {ManagerCommandKind.Move, ManagerCommandKind.Rename, ManagerCommandKind.Delete, ManagerCommandKind.CreateDirectory}
                Assert.False(ManagerCommandPlanner.Allows(verb, source.Caps, rows, location))
            Next
            Assert.False(ManagerCommandPlanner.Allows(ManagerCommandKind.Copy, source.Caps, rows, location, source.Caps))
        End Using
    End Sub

    <Fact>
    Public Sub Explicit_context_is_independent_of_a_viewer_archive_and_new_write_verbs_use_the_same_gate()
        Dim viewer As New ResourceOperationContext With {.InsideArchive = True}
        Dim manager As New ResourceOperationContext With {.SourceCapabilities = ProviderCapabilities.LocalDisk}
        Assert.False(ResourceRefusalPolicy.IsAllowed(viewer, ResourceVerb.Copy))
        Assert.True(ResourceRefusalPolicy.IsAllowed(manager, ResourceVerb.Copy))
        Assert.True(ResourceRefusalPolicy.IsAllowed(manager, ResourceVerb.CreateFolder))
        manager.DestinationCapabilities = New ProviderCapabilities(canWrite:=False)
        Assert.False(ResourceRefusalPolicy.IsAllowed(manager, ResourceVerb.Move))
    End Sub

    <Theory>
    <InlineData("CON")>
    <InlineData("aux.txt")>
    <InlineData("COM1.log")>
    <InlineData("LPT¹")>
    <InlineData("name.")>
    <InlineData("name ")>
    <InlineData("x:y")>
    <InlineData("../x")>
    <InlineData("a\b")>
    Public Sub Windows_leaf_aliases_and_paths_are_refused(name As String)
        Assert.False(ManagerCommandPlanner.ValidLeaf(ResourceKind.Local, name))
    End Sub

    <Fact>
    Public Sub Remote_leaf_rules_preserve_case_colons_and_trailing_dots_but_refuse_traversal()
        Assert.True(ManagerCommandPlanner.ValidLeaf(ResourceKind.Sftp, "A:a."))
        Assert.False(ManagerCommandPlanner.ValidLeaf(ResourceKind.Sftp, "../A"))
        Assert.False(ManagerCommandPlanner.ValidLeaf(ResourceKind.Sftp, "A" & ChrW(0)))
    End Sub

    <Fact>
    Public Sub A_case_distinct_remote_name_is_not_same_path_and_is_copied_without_source_deletion()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a", "original")
        Using source As ManagerEndpointLease = ManagerTestLease(provider), destination As ManagerEndpointLease = source.Fork()
            Dim target As New TransferPlan(TransferKind.Copy, source.Endpoint("/root/a"), destination.Endpoint("/root/A"), ConflictPolicy.Fail, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            Dim outcome As TransferOutcome = TransferExecutor.Execute(target)
            Assert.True(outcome.Succeeded, outcome.ToString())
            Assert.Equal(provider.Bytes("/root/a"), provider.Bytes("/root/A"))
            Assert.Equal(0, provider.DeleteCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Collision_keep_both_reserves_a_sibling_directory_before_expanding_descendants()
        Dim source As New ManagerFakeProvider()
        source.PutDirectory("/root/tree")
        source.PutFile("/root/tree/a")
        Dim destination As New ManagerFakeProvider()
        destination.PutDirectory("/root/tree")
        destination.PutFile("/root/tree/unrelated")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/tree"}, ManagerTestLease(destination, "dest"), collision:="rename")
            Dim plan As BatchPlan = ManagerCommandFixture.BuildManagerTestPlan(request)
            Assert.Equal("/root/tree (2)", plan.Operations(0).Destination)
            Assert.Equal("/root/tree (2)/a", plan.Operations(1).Destination)
            ExecuteManagerTestPlan(plan)
            Assert.True(destination.Entries.ContainsKey("/root/tree/unrelated"))
        End Using
    End Sub

    <Fact>
    Public Sub Destination_inside_source_and_roots_are_refused_before_mutation()
        Dim provider As New ManagerFakeProvider()
        provider.PutDirectory("/root/tree")
        provider.PutDirectory("/root/tree/child")
        Using source As ManagerEndpointLease = ManagerTestLease(provider)
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Move, source.Fork(), {"/root/tree"}, source.Fork(), "/root/tree/child")
                Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
            End Using
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Delete, source.Fork(), {"/root"})
                Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
            End Using
            Assert.Equal(0, provider.DeleteCalls)
            Assert.Equal(0, provider.RemoveDirectoryCalls)
        End Using
    End Sub
End Class
#End If
