#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class ManagerCommandLifecycleTests
    <Fact>
    Public Sub Replacing_the_destination_during_a_prompt_refuses_the_whole_request_before_admission()
        Dim source As New ManagerFakeProvider()
        source.PutFile("/root/a")
        Dim destination As New ManagerFakeProvider()
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Move, ManagerTestLease(source), {"/root/a"}, ManagerTestLease(destination, "dest"))
            Dim before As ManagerTreePlan = ManagerTreePlan.Capture(request)
            destination.PutDirectory("/root")
            Dim after As ManagerTreePlan = ManagerTreePlan.Capture(request)
            Dim refusal As ManagerCommandException = Assert.Throws(Of ManagerCommandException)(Sub() before.RevalidateVersions(request, after))
            Assert.Equal(request.DestinationPath, refusal.Path)
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, destination.DeleteCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Delayed_refresh_affects_only_the_captured_place_and_its_actual_subtree()
        Dim place As New ManagerPlace("source", "source", ResourceKind.Sftp, "/root", "source")
        Dim affected As New ManagerLocation(place, "/root/tree")
        Assert.True(affected.WithDirectory("/root/tree/child").IsAffectedBy(affected, True))
        Assert.False(affected.WithDirectory("/root/tree/child").IsAffectedBy(affected, False))
        Assert.False(affected.WithDirectory("/root/Tree/child").IsAffectedBy(affected, True))
        Assert.False(affected.WithDirectory("/root/tree-other").IsAffectedBy(affected, True))
        Assert.False(New ManagerLocation(New ManagerPlace("other", "other", ResourceKind.Sftp, "/root", "other"), "/root/tree").IsAffectedBy(affected, True))
    End Sub

    <Fact>
    Public Sub Clipboard_rejects_a_new_version_even_when_size_and_timestamp_are_unchanged()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a", "old")
        Dim source As ManagerEndpointLease = ManagerTestLease(provider)
        Dim row As ManagerEntry = ManagerTestRow(source, "/root/a")
        Using clipboard As New ManagerSelectionClipboard(ManagerLocation.AtRoot(source.Place), 1, {row}, source, True)
            provider.PutFile("/root/a", "new")
            provider.Entries("/root/a").LastWriteUtc = row.ModifiedUtc
            Using request As ManagerCommandRequest = clipboard.Paste(ManagerTestLease(New ManagerFakeProvider(), "dest"), "/root", "skip")
                Dim failure As ManagerCommandException = Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
                Assert.Equal(ManagerFailure.SourceChanged, failure.Reason)
                Assert.Equal(0, provider.DeleteCalls)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_provider_cannot_change_the_version_already_captured_in_a_plan()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Delete, ManagerTestLease(provider), {"/root/a"})
            Dim plan As BatchPlan = BuildManagerTestPlan(request)
            provider.Entries("/root/a").VersionTag = "replaced"
            Assert.Throws(Of ManagerCommandException)(Sub() plan.Operations(0).ManagerOperation.Execute("unused"))
            Assert.Equal(0, provider.DeleteCalls)
        End Using
    End Sub

    <Fact>
    Public Async Function Navigation_and_panel_disposal_leave_request_lease_alive_until_completion() As Task
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Dim place As New ManagerPlace("source", "source", ResourceKind.Sftp, "/root", "source")
        Dim browser As New ManagerBrowser(Function(p, ct) ManagerOpenResult.Ok(provider, provider))
        Await browser.Begin(ManagerIntent.Start, ManagerLocation.AtRoot(place))
        Using captured As ManagerEndpointLease = browser.CaptureEndpoint(place, CancellationToken.None)
            browser.Dispose()
            Assert.Equal(0, browser.LeaseCount)
            Assert.Equal(0, provider.DisposeCalls)
            Assert.Equal(StatOutcome.Found, captured.Directory.StatEntry("/root/a").Outcome)
        End Using
        Assert.Equal(1, provider.DisposeCalls)
    End Function

    <Fact>
    Public Sub Clipboard_owns_exact_entry_identity_and_survives_navigation_but_not_disposal()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Dim held As ManagerEndpointLease = ManagerTestLease(provider)
        Dim entry As ManagerEntry = ManagerTestRow(held, "/root/a")
        Dim clipboard As New ManagerSelectionClipboard(ManagerLocation.AtRoot(held.Place), 8, {entry}, held, True)
        Using request As ManagerCommandRequest = clipboard.Paste(ManagerTestLease(New ManagerFakeProvider(), "destination"), "/root", "skip")
            clipboard.Dispose()
            Assert.Equal(0, provider.DisposeCalls)
            Assert.Equal(entry.Id, request.Entries(0).Id)
            Assert.Equal(8, request.Generation)
            provider.Entries.Remove("/root/a")
            Assert.Throws(Of ManagerCommandException)(Function() BuildManagerTestPlan(request))
        End Using
        Assert.Equal(1, provider.DisposeCalls)
    End Sub

    <Fact>
    Public Sub Cancellation_during_checked_delete_restat_keeps_both_copies()
        Dim source As New ManagerFakeProvider()
        source.PutFile("/root/a")
        Dim destination As New ManagerFakeProvider()
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Move, ManagerTestLease(source), {"/root/a"}, ManagerTestLease(destination, "dest"))
            Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
            source.StatHook = Sub(path)
                                  If path = "/root/a" AndAlso destination.Entries.ContainsKey("/root/a") Then request.Cancel()
                              End Sub
            Dim failure As TransferOutcomeException = Assert.Throws(Of TransferOutcomeException)(Sub() op.ManagerOperation.Execute(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))))
            Assert.Equal(TransferResult.CopyOnly, failure.Outcome.Result)
            Assert.Equal(0, source.DeleteCalls)
            Assert.True(source.Entries.ContainsKey("/root/a"))
            Assert.True(destination.Entries.ContainsKey("/root/a"))
        End Using
    End Sub

    <Fact>
    Public Sub Queued_create_and_rename_have_conditional_inverses_on_the_existing_stack()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Using source As ManagerEndpointLease = ManagerTestLease(provider)
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Rename, source.Fork(), {"/root/a"}, leaf:="A")
                Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
                op.ManagerOperation.Execute("unused")
                Dim data As ManagerUndoData = op.ManagerOperation.UndoData
                Assert.NotNull(data)
                Assert.False(provider.Entries.ContainsKey("/root/a"))
                Assert.True(provider.Entries.ContainsKey("/root/A"))
                Dim entry As New BatchUndoEntry With {.IsManager = True}
                entry.Items.Add(New BatchUndoItem With {.Kind = op.Kind, .ManagerUndo = data})
                Dim stack As New UndoStack(Of BatchUndoEntry)(2)
                stack.Push(entry)
                Dim inverse As BatchUndoPlan = BatchUndoPlanner.Build(stack.Peek(), Nothing)
                Assert.Single(inverse.Inverse)
                inverse.Inverse(0).ManagerOperation.Execute("unused")
                Assert.True(provider.Entries.ContainsKey("/root/a"))
                stack.Clear()
            End Using
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.CreateDirectory, source.Fork(), {}, leaf:="new")
                Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
                op.ManagerOperation.Execute("unused")
                Using data As ManagerUndoData = op.ManagerOperation.UndoData
                    provider.PutFile("/root/new/user-content")
                    Assert.Throws(Of ManagerCommandException)(Sub() ManagerOperation.ForUndo(data).ManagerOperation.Execute("unused"))
                    Assert.True(provider.Entries.ContainsKey("/root/new/user-content"))
                End Using
            End Using
        End Using
        Assert.Equal(1, provider.DisposeCalls)
    End Sub

    <Fact>
    Public Sub A_changed_copy_is_never_removed_by_undo()
        Dim source As New ManagerFakeProvider()
        source.PutFile("/root/a")
        Dim destination As New ManagerFakeProvider With {.Caps = New ProviderCapabilities(supportsStableSourceDelete:=True)}
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/a"}, ManagerTestLease(destination, "dest"))
            Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
            op.ManagerOperation.Execute(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            Using data As ManagerUndoData = op.ManagerOperation.UndoData
                destination.PutFile("/root/a", "changed")
                Assert.Throws(Of ManagerCommandException)(Sub() ManagerOperation.ForUndo(data).ManagerOperation.Execute("unused"))
                Assert.Equal(0, destination.DeleteCalls)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Delayed_completion_retains_origin_request_generation_and_provider_case_identity()
        Dim provider As New ManagerFakeProvider()
        provider.PutFile("/root/a")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(provider), {"/root/a"})
            Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
            Assert.Equal(request.Id, op.ManagerOperation.RequestId)
            Assert.Equal(request.Generation, op.ManagerOperation.Generation)
            Assert.True(request.Origin.SameAs(op.ManagerOperation.Origin))
            Assert.False(request.Origin.SameAs(request.Origin.WithDirectory("/root/other")))
            Assert.False(request.Origin.SameAs(request.Origin.WithDirectory("/Root")))
        End Using
    End Sub
End Class
#End If
