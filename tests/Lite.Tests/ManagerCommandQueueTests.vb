#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class ManagerCommandQueueTests
    <Theory>
    <InlineData(False, False)>
    <InlineData(False, True)>
    <InlineData(True, False)>
    <InlineData(True, True)>
    Public Async Function All_endpoint_pairs_run_through_the_shared_queue(localSource As Boolean, localDestination As Boolean) As Task
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-manager-queue-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Path.Combine(root, "source"))
        Directory.CreateDirectory(Path.Combine(root, "destination"))
        Dim source As ManagerEndpointLease = Nothing
        Dim destination As ManagerEndpointLease = Nothing
        Try
            If localSource Then
                Dim place As New ManagerPlace("local-source", "source", ResourceKind.Local, Path.Combine(root, "source"))
                source = New ManagerEndpointLease(place, ManagerOpenResult.Ok(New LocalProvider(Function() False)))
                File.WriteAllText(Path.Combine(place.RootPath, "a"), "payload")
            Else
                Dim fake As New ManagerFakeProvider()
                fake.PutFile("/root/a")
                source = ManagerTestLease(fake)
            End If
            If localDestination Then
                Dim place As New ManagerPlace("local-dest", "dest", ResourceKind.Local, Path.Combine(root, "destination"))
                destination = New ManagerEndpointLease(place, ManagerOpenResult.Ok(New LocalProvider(Function() False)))
            Else
                destination = ManagerTestLease(New ManagerFakeProvider(), "dest")
            End If
            Dim sourcePath As String = ManagerPathRules.Combine(source.Place.Kind, source.Place.RootPath, "a")
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, source, {sourcePath}, destination, destination.Place.RootPath)
                source = Nothing
                destination = Nothing
                Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
                Dim context As SynchronizationContext = SynchronizationContext.Current
                Dim queue As FileOpQueue(Of BatchOp)
                Try
                    SynchronizationContext.SetSynchronizationContext(Nothing)
                    queue = New FileOpQueue(Of BatchOp)(Sub(item) item.ManagerOperation.Execute(Path.Combine(root, "scratch")))
                Finally
                    SynchronizationContext.SetSynchronizationContext(context)
                End Try
                Dim done As New TaskCompletionSource(Of Exception)(TaskCreationOptions.RunContinuationsAsynchronously)
                AddHandler queue.OpCompleted, Sub(s, e) done.TrySetResult(e.Error_Info)
                Assert.True(queue.Enqueue(op))
                Assert.Null(Await done.Task.WaitAsync(TimeSpan.FromSeconds(10)))
                Assert.True(Await queue.WaitForQuiescenceAsync(TimeSpan.FromSeconds(10)))
                queue.CompleteAdmission()
                Assert.Equal(StatOutcome.Found, request.Destination.Directory.StatEntry(op.Destination).Outcome)
                Assert.Equal(StatOutcome.Found, request.Source.Directory.StatEntry(op.Source).Outcome)
                op.ManagerOperation.UndoData?.Dispose()
            End Using
        Finally
            source?.Dispose()
            destination?.Dispose()
            Directory.Delete(root, True)
        End Try
    End Function

    <Fact>
    Public Sub Failed_content_verification_records_zero_source_deletes()
        Dim source As New ManagerFakeProvider()
        source.PutFile("/root/a")
        Dim destination As New ManagerFakeProvider With {.CorruptUpload = True}
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Move, ManagerTestLease(source), {"/root/a"}, ManagerTestLease(destination, "dest"))
            Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
            Dim failure As TransferOutcomeException = Assert.Throws(Of TransferOutcomeException)(Sub() op.ManagerOperation.Execute(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))))
            Assert.Equal(TransferReason.VerificationFailed, failure.Outcome.Reason)
            Assert.Equal(0, source.DeleteCalls)
            Assert.True(source.Entries.ContainsKey("/root/a"))
        End Using
    End Sub

    <Fact>
    Public Sub Denied_delete_is_not_retried_and_a_lost_final_stat_is_indeterminate()
        For Each denied As Boolean In {True, False}
            Dim source As New ManagerFakeProvider With {.DenyDelete = denied, .UnknownAfterDelete = Not denied}
            source.PutFile("/root/a")
            Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(source), {"/root/a"})
                Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
                If denied Then
                    Assert.Throws(Of UnauthorizedAccessException)(Sub() op.ManagerOperation.Execute("unused"))
                Else
                    Dim failure As ResourceDeleteException = Assert.Throws(Of ResourceDeleteException)(Sub() op.ManagerOperation.Execute("unused"))
                    Assert.True(failure.Unverified)
                End If
                Assert.Equal(1, source.DeleteCalls)
                Assert.False(op.ManagerOperation.Completed)
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Permission_changed_after_planning_is_a_named_failure_without_mutation()
        Dim source As New ManagerFakeProvider()
        source.PutFile("/root/a")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.DeletePermanently, ManagerTestLease(source), {"/root/a"})
            Dim op As BatchOp = BuildManagerTestPlan(request).Operations(0)
            source.Caps = New ProviderCapabilities(canWrite:=False, canDelete:=False)
            Dim failure As IOException = Assert.Throws(Of ManagerCommandException)(Sub() op.ManagerOperation.Execute("unused"))
            Assert.Contains("/root/a", FailureText.Describe(failure))
            Assert.Equal(0, source.DeleteCalls)
        End Using
    End Sub
End Class
#End If
