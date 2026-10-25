#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

Public Class ManagerTransferLifecycleTests
    <Fact>
    Public Sub BothEndpointLeasesOutliveBrowserAndReleaseAfterTheRequest()
        Dim first As New ManagerFakeProvider()
        Dim second As New ManagerFakeProvider()
        first.PutFile("/root/file.txt")
        Dim sourcePlace As New ManagerPlace("source", "source", ResourceKind.Sftp, "/root")
        Dim targetPlace As New ManagerPlace("target", "target", ResourceKind.Sftp, "/root")
        Dim browser As New ManagerBrowser(Function(p, ct) ManagerOpenResult.Ok(If(p.Id = "source", first, second), If(p.Id = "source", first, second)))
        Dim source = browser.CaptureEndpoint(sourcePlace, CancellationToken.None)
        Dim target = browser.CaptureEndpoint(targetPlace, CancellationToken.None)
        Using request = MakeManagerRequest(ManagerCommandKind.Move, source, {"/root/file.txt"}, target)
            browser.Dispose()
            Assert.Equal(0, first.DisposeCalls)
            Assert.Equal(0, second.DisposeCalls)
            ExecuteManagerTestPlan(BuildManagerTestPlan(request))
        End Using
        Assert.Equal(1, first.DisposeCalls)
        Assert.Equal(1, second.DisposeCalls)
    End Sub

    <Theory>
    <InlineData(1073741824L, False)>
    <InlineData(1073741825L, True)>
    <InlineData(-1L, True)>
    Public Sub WarningUsesKnownThresholdAndUnknownSizes(bytes As Long, warning As Boolean)
        Dim slow As New ProviderCapabilities()
        Dim facts As New ManagerTransferPreflight({New BatchOp With {.Kind = FileOpKind.Copy, .SizeBytes = bytes}}, slow, slow, False)
        Assert.Equal(warning, facts.NeedsWarning)
        Assert.True(facts.RelaysThroughPc)
        Assert.Equal(bytes < 0, facts.HasUnknownBytes)
        Dim fast As New ProviderCapabilities(costHint:=ProviderCostHint.Fast)
        Assert.False(New ManagerTransferPreflight({New BatchOp With {.Kind = FileOpKind.Copy, .SizeBytes = bytes}}, fast, fast, True).NeedsWarning)
    End Sub

    <Fact>
    Public Sub FinalCollisionSkipsDoNotInflateWarningOrScratch()
        Dim source As New ManagerFakeProvider()
        Dim target As New ManagerFakeProvider()
        source.PutFile("/root/taken")
        target.PutFile("/root/taken")
        Using fromLease = ManagerTestLease(source), toLease = ManagerTestLease(target, "target"),
            request = MakeManagerRequest(ManagerCommandKind.Copy, fromLease.Fork(), {"/root/taken"}, toLease.Fork())
            Dim plan = BuildManagerTestPlan(request)
            Dim facts As New ManagerTransferPreflight(plan.Operations, fromLease.Capabilities, toLease.Capabilities, False)
            Assert.False(facts.HasTransfers)
            Assert.False(facts.NeedsWarning)
            Assert.Equal(0, facts.ScratchBytes)
        End Using
    End Sub

    <Fact>
    Public Sub ScratchAccountsForTwoLargestCopiesAndRejectsOverflow()
        Dim caps As New ProviderCapabilities()
        Dim facts As New ManagerTransferPreflight({New BatchOp With {.Kind = FileOpKind.Copy, .SizeBytes = 2000},
            New BatchOp With {.Kind = FileOpKind.Copy, .SizeBytes = 1000}}, caps, caps, False)
        Assert.Equal(4000L + ManagerTransferPreflight.ScratchOverheadBytes, facts.ScratchBytes)
        Assert.Throws(Of ManagerCommandException)(Sub() facts.CheckScratch("unused", Function(p) facts.ScratchBytes - 1))
        facts.CheckScratch("unused", Function(p) facts.ScratchBytes)
        Assert.Throws(Of ManagerCommandException)(Function() New ManagerTransferPreflight({New BatchOp With {.Kind = FileOpKind.Copy, .SizeBytes = Long.MaxValue}}, caps, caps, False))
    End Sub

    <Theory>
    <InlineData("verification", TransferResult.Failed, TransferReason.VerificationFailed, 0)>
    <InlineData("cancel", TransferResult.Cancelled, TransferReason.Cancelled, 0)>
    <InlineData("source-change", TransferResult.CopyOnly, TransferReason.SourceChanged, 0)>
    <InlineData("delete-denied", TransferResult.CopyOnly, TransferReason.SourceDeleteFailed, 1)>
    <InlineData("final-stat", TransferResult.Indeterminate, TransferReason.Uncertain, 1)>
    <InlineData("no-checked-delete", TransferResult.CopyOnly, TransferReason.SourceDeleteUnsupported, 0)>
    <InlineData("access-change", TransferResult.CopyOnly, TransferReason.AccessChanged, 0)>
    <InlineData("disk-full", TransferResult.Failed, TransferReason.DiskFull, 0)>
    Public Sub FaultsRetainTruthfulResultsAndDeleteCounts(fault As String, expected As Integer, reason As Integer, deletes As Integer)
        Dim source As New ManagerFakeProvider()
        Dim target As New ManagerFakeProvider()
        source.PutFile("/root/file.txt", "verified bytes")
        target.CorruptUpload = fault = "verification"
        source.DenyDelete = fault = "delete-denied"
        source.UnknownAfterDelete = fault = "final-stat"
        If fault = "no-checked-delete" Then source.Caps = New ProviderCapabilities()
        Dim scratch As String = Path.Combine(Path.GetTempPath(), "fms-261-fault-" & Guid.NewGuid().ToString("N"))
        Using cancellation As New CancellationTokenSource(), fromLease = ManagerTestLease(source), toLease = ManagerTestLease(target, "target")
            If fault = "cancel" Then source.DownloadHook = Sub(p, local) cancellation.Cancel()
            If fault = "disk-full" Then source.DownloadHook = Sub(p, local)
                Throw New ResourceFailureException(ResourceFailure.FromException(New IOException("Injected disk full", &H80070070)))
            End Sub
            If fault = "source-change" Then target.DownloadHook = Sub(p, local) source.PutFile("/root/file.txt", "changed source")
            Dim plan As New TransferPlan(TransferKind.Move, fromLease.Endpoint("/root/file.txt"), toLease.Endpoint("/root/file.txt"),
                ConflictPolicy.Fail, scratch, allowServerSideMove:=False)
            Dim checks As Integer
            If fault = "access-change" Then plan.ValidateMutation = Sub()
                checks += 1
                If checks = 3 Then Throw New ManagerExchangeException(ManagerExchangeAvailability.Removed)
            End Sub
            Try
                Dim outcome = TransferExecutor.Execute(plan, ct:=cancellation.Token)
                Assert.Equal(CType(expected, TransferResult), outcome.Result)
                Assert.Equal(CType(reason, TransferReason), outcome.Reason)
                Assert.Equal(deletes, source.DeleteCalls)
                Assert.False(outcome.Succeeded)
                If deletes = 0 OrElse fault = "delete-denied" Then Assert.True(source.Bytes.ContainsKey("/root/file.txt"))
                If outcome.LastState >= TransferState.Published Then Assert.Equal("verified bytes", Text.Encoding.UTF8.GetString(target.Bytes("/root/file.txt")))
                Assert.Empty(Directory.GetFiles(scratch))
            Finally
                If Directory.Exists(scratch) Then Directory.Delete(scratch, True)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub ProgressReportsVerificationBeforePublicationAndCompletion()
        Dim source As New ManagerFakeProvider()
        Dim target As New ManagerFakeProvider()
        source.PutFile("/root/file.txt")
        Dim scratch As String = Path.Combine(Path.GetTempPath(), "fms-261-phase-" & Guid.NewGuid().ToString("N"))
        Dim states As New Collections.Generic.List(Of TransferState)()
        Dim phases As New Collections.Generic.List(Of TransferPhase)()
        Using fromLease = ManagerTestLease(source), toLease = ManagerTestLease(target, "target")
            Dim plan As New TransferPlan(TransferKind.Move, fromLease.Endpoint("/root/file.txt"), toLease.Endpoint("/root/file.txt"), ConflictPolicy.Fail, scratch) With {
                .StateProgress = New InlineProgress(states), .PhaseProgress = New InlinePhaseProgress(phases)}
            Try
                Assert.True(TransferExecutor.Execute(plan).Succeeded)
                Assert.Equal({TransferState.SourceCaptured, TransferState.StageWritten, TransferState.ContentVerified, TransferState.Published,
                    TransferState.SourceDeleteConfirmed, TransferState.Completed}, states)
                Assert.Equal({TransferPhase.DownloadSource, TransferPhase.UploadStage, TransferPhase.ReadBack, TransferPhase.Publish,
                    TransferPhase.DeleteSource, TransferPhase.Completed}, phases)
            Finally
                If Directory.Exists(scratch) Then Directory.Delete(scratch, True)
            End Try
        End Using
    End Sub

    Private NotInheritable Class InlineProgress
        Implements IProgress(Of TransferState)
        Private ReadOnly states As Collections.Generic.List(Of TransferState)
        Friend Sub New(states As Collections.Generic.List(Of TransferState))
            Me.states = states
        End Sub
        Public Sub Report(state As TransferState) Implements IProgress(Of TransferState).Report
            states.Add(state)
        End Sub
    End Class

    Private NotInheritable Class InlinePhaseProgress
        Implements IProgress(Of TransferPhase)
        Private ReadOnly phases As Collections.Generic.List(Of TransferPhase)
        Friend Sub New(phases As Collections.Generic.List(Of TransferPhase))
            Me.phases = phases
        End Sub
        Public Sub Report(phase As TransferPhase) Implements IProgress(Of TransferPhase).Report
            phases.Add(phase)
        End Sub
    End Class
End Class
#End If
