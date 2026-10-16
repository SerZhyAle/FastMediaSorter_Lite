Option Strict On

Imports System.IO
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The tests that section 5 of ticket 231 asks for: the unattended exchange-style and PIN sides, the per-operation
''' missed-run and balloon options and the machine-switch migration, skipped runs and the exclamation mark, checked
''' moves that keep the source, the local-time edges of the grid, and the settings export and import. Everything runs
''' against in-memory servers, scripted dialers, a manual clock and an isolated settings hive - no network, no real
''' credential, no real folder of the user.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledSecondPassTests

    Private Shared ReadOnly Berlin As TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")

    Private Shared Function Wall(year As Integer, month As Integer, day As Integer, hour As Integer, minute As Integer, Optional second As Integer = 0) As DateTime
        Return New DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified)
    End Function

    Private Shared Function At(day As Integer, hour As Integer, minute As Integer) As DateTime
        Return Wall(2026, 10, day, hour, minute)
    End Function

    ' --- the engine kit ---------------------------------------------------------------------------------

    ''' <summary>An engine on a manual clock whose "remote" side is a scratch folder that can be made offline, denied or revoked.</summary>
    Private NotInheritable Class Kit
        Implements IDisposable

        Friend ReadOnly Tree As New ScratchTree()
        Friend ReadOnly Hive As New ScratchHive()
        Friend ReadOnly Clock As ManualClock
        Friend ReadOnly Timers As New RecordingTimers()
        Friend ReadOnly Store As ScheduledStore
        Friend ReadOnly Journal As ScheduledJournal
        Friend ReadOnly Services As ScheduledServices
        Friend ReadOnly Engine As ScheduledEngine
        Friend Zone As TimeZoneInfo = TimeZoneInfo.Utc
        Friend RemoteProblem As SideProblem = SideProblem.Ok
        ''' <summary>"id:outcome" for every balloon the engine asked for.</summary>
        Friend ReadOnly Notified As New List(Of String)()

        Friend Sub New(start As DateTime)
            Clock = New ManualClock(start)
            Dim folder As String = Tree.Dir("state")
            Store = New ScheduledStore(folder)
            Journal = New ScheduledJournal(folder)
            Services = New ScheduledServices With {
                .Clock = AddressOf Clock.Read,
                .TimeZone = Function() Zone,
                .VolumeFacts = Function(path As String) New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk},
                .SendToBin = Function(path As String) As Boolean
                                 File.Delete(path)
                                 Return False
                             End Function,
                .ResourceFacts = Function(id As String) New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS", .RootPath = "/data"},
                .OpenSide = Function(location As ScheduledLocation) As ScheduledSideOpen
                                If location.Kind <> ScheduledLocationKind.Resource Then
                                    Return ScheduledSideOpen.Opened(LocalSide.ForLocation(location))
                                End If
                                If RemoteProblem.IsProblem Then Return ScheduledSideOpen.Failed(RemoteProblem)
                                Dim mapped As New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = Tree.Dir("remote"), .Recursive = location.Recursive}
                                Return ScheduledSideOpen.Opened(LocalSide.ForLocation(mapped))
                            End Function,
                .NotifyError = Sub(op As ScheduledOperation, result As ScheduledRunResult) Notified.Add(op.Id & ":" & result.Outcome.ToString())}
            Engine = New ScheduledEngine(Store, Journal, Services, Timers) With {.CatchUpDelay = TimeSpan.Zero}
        End Sub

        ''' <summary>A daily 03:00 copy from the "NAS" folder /data/in to a scratch folder, grid counted from the clock's day.</summary>
        Friend Function RemoteCopyOp(id As String) As ScheduledOperation
            Dim op As New ScheduledOperation With {.Id = id, .Name = "remote " & id, .Action = ScheduledAction.Copy,
                .AnchorMinutes = 3 * 60, .IntervalMinutes = 24 * 60, .StartDate = Clock.Now.Date, .CreatedAt = Clock.Now.AddDays(-10)}
            op.Source = New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r1", .ResourceName = "NAS", .Path = "/data/in"}
            op.Destination.Path = Tree.Dir("dst-" & id)
            Return op
        End Function

        Friend Sub Seed(op As ScheduledOperation)
            Store.SaveDefinition(op)
            Store.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)
        End Sub

        Friend Sub PutRemote(name As String, content As String)
            File.WriteAllText(Path.Combine(Tree.Dir("remote"), name), content)
        End Sub

        Friend Sub Idle()
            Engine.WhenIdleAsync().GetAwaiter().GetResult()
        End Sub

        Friend Function RunNow(id As String) As ScheduledOperation
            Assert.Equal(TriggerOutcome.Started, Engine.RunNow(id))
            Idle()
            Return Engine.TryGet(id)
        End Function

        Friend Function History() As List(Of JournalEntry)
            Return Journal.ReadRecent().AsEnumerable().Reverse().ToList()
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Engine.Dispose()
            Hive.Dispose()
            Tree.Dispose()
        End Sub
    End Class

    ' --- the registered-resource kit: the real open path with a scripted dialer ---------------------------

    Private NotInheritable Class MemorySecrets
        Implements IResourceSecretStore

        Friend ReadOnly Blobs As New Dictionary(Of String, String)()

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim value As String = Nothing
            Return If(Blobs.TryGetValue(handle, value), value, "")
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    ''' <summary>A clock that never fires a timeout on its own: the dial is the only thing that can end a round.</summary>
    Private NotInheritable Class QuietClock
        Implements IResolutionClock

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                Return 0
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
            Return Task.Delay(Timeout.Infinite, ct)
        End Function
    End Class

    Private NotInheritable Class ScriptedDialer
        Implements IEndpointDialer

        Friend Calls As Integer
        ''' <summary>Nothing = the connection succeeds and hands out <see cref="Session"/>.</summary>
        Friend Failure As ResourceFailure
        Friend Session As IDisposable

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            Interlocked.Increment(Calls)
            If Failure IsNot Nothing Then Return Task.FromResult(DialResult.Failed(Failure))
            Return Task.FromResult(DialResult.Verified(Session))
        End Function
    End Class

    Private NotInheritable Class RemoteKit
        Implements IDisposable

        Friend ReadOnly Tree As New ScratchTree()
        Friend ReadOnly Server As New ScheduledResourceTests.FakeServer()
        Friend ReadOnly Secrets As New MemorySecrets()
        Friend ReadOnly Dialer As New ScriptedDialer()
        Friend ReadOnly Descriptor As ResourceDescriptor
        Friend ReadOnly Services As ScheduledServices
        Friend ReadOnly HiveWrites As New List(Of String)()
        Private ReadOnly clock As New QuietClock()

        ''' <param name="pinRequired">The root is PIN-protected (228).</param>
        ''' <param name="storedPin">The PIN 228 holds for it under DPAPI, or Nothing when none is stored.</param>
        Friend Sub New(Optional pinRequired As Boolean = False, Optional storedPin As String = Nothing)
            Dim id As String = "res-" & Guid.NewGuid().ToString("N")
            Secrets.PutBlob("pw-" & id, DpapiSecretChecks.ProtectChecked("hunter2").Value)
            Descriptor = New ResourceDescriptor With {.Id = id, .DisplayName = "Phone", .Kind = ResourceKind.Sftp,
                .Host = "phone.invalid", .Port = 22, .UserName = "fms", .RootPath = "/data",
                .SecretHandle = "pw-" & id, .PinRequired = pinRequired}
            If storedPin IsNot Nothing Then
                Descriptor.PinSecretHandle = "pin-" & id
                Secrets.PutBlob("pin-" & id, DpapiSecretChecks.ProtectChecked(storedPin).Value)
            End If
            Dialer.Session = Server
            Services = New ScheduledServices With {
                .Clock = Function() New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified),
                .VolumeFacts = Function(path As String) New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk},
                .SendToBin = Function(path As String) As Boolean
                                 File.Delete(path)
                                 Return False
                             End Function,
                .ResourceFacts = Function(resourceId As String) ScheduledResourceRegistry.FactsOf(Descriptor),
                .OpenSide = Function(location As ScheduledLocation) As ScheduledSideOpen
                                If location.Kind <> ScheduledLocationKind.Resource Then
                                    Return ScheduledSideOpen.Opened(LocalSide.ForLocation(location))
                                End If
                                Return ScheduledResourceSides.Open(location, location.Recursive, CancellationToken.None,
                                    descriptorOverride:=Descriptor, secretStoreOverride:=Secrets, dialerOverride:=Dialer,
                                    clockOverride:=clock, hiveReadOverride:=Function(name As String) "",
                                    hiveWriteOverride:=Sub(name As String, value As String) HiveWrites.Add(name))
                            End Function}
        End Sub

        Friend Function Remote(folder As String) As ScheduledLocation
            Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = Descriptor.Id, .ResourceName = Descriptor.DisplayName, .Path = folder}
        End Function

        Friend Shared Function Local(folder As String) As ScheduledLocation
            Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = folder}
        End Function

        Friend Function Run(op As ScheduledOperation) As ScheduledRunResult
            Return New ScheduledExecutor(Services).Run(op, CancellationToken.None)
        End Function

        Friend Function Operation(action As ScheduledAction, source As ScheduledLocation, Optional destination As ScheduledLocation = Nothing) As ScheduledOperation
            Return New ScheduledOperation With {.Id = "x", .Name = "n", .Action = action, .Source = source,
                .Destination = If(destination, New ScheduledLocation())}
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Tree.Dispose()
        End Sub
    End Class

    ' =====================================================================================================
    ' Rule 1 (exchange side): a resource that answers is copied from unattended; a revoked device walls it.
    ' =====================================================================================================

    <Fact>
    Public Sub A_resource_that_answers_is_copied_from_unattended_and_its_session_is_released()
        Using kit As New RemoteKit()
            kit.Server.Put("/data/in/a.txt", "A")
            kit.Server.Put("/data/in/sub/b.txt", "BB")
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.True(r.Outcome = ScheduledRunOutcome.Ok, r.Outcome.ToString() & " / " & r.Detail)
            Assert.Equal(2, r.Files)
            Assert.Equal("A", kit.Tree.Read("out\a.txt"))
            Assert.Equal("BB", kit.Tree.Read("out\sub\b.txt"))
            Assert.Equal(1, kit.Dialer.Calls)
            Assert.True(kit.Server.Disposed >= 1)
            ' Nothing was asked: no consent, no pin was stored by the run.
            Assert.Empty(kit.HiveWrites)
        End Using
    End Sub

    <Theory>
    <InlineData(2, "device-revoked", "source-device-revoked|Phone")>
    <InlineData(3, "exchange-certificate", "source-exchange-certificate|Phone")>
    <InlineData(2, "signed-out", "source-signed-out|Phone")>
    Public Sub A_revoked_device_a_changed_certificate_or_a_signed_out_entry_is_the_wall_with_its_reason(kind As Integer, detail As String, expected As String)
        Using kit As New RemoteKit()
            kit.Server.Put("/data/in/a.txt", "A")
            kit.Dialer.Failure = New ResourceFailure(CType(kind, ResourceFailureKind), Nothing, detail)
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.Equal(expected, r.Detail)
            Assert.Equal(0, r.Files)
            Assert.False(kit.Tree.Exists("out\a.txt"))
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 2 (PIN side): runs with the stored PIN, walls without one, never asks.
    ' =====================================================================================================

    <Fact>
    Public Sub A_pin_side_runs_with_the_pin_228_stored()
        Using kit As New RemoteKit(pinRequired:=True, storedPin:="4321")
            kit.Server.Put("/data/in/a.txt", "A")
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.True(r.Outcome = ScheduledRunOutcome.Ok, r.Outcome.ToString() & " / " & r.Detail)
            Assert.Equal("A", kit.Tree.Read("out\a.txt"))
            ' The connection held the stored PIN: the session memory is filled only after a connect that carried it.
            Assert.Equal("4321", ResourcePinMemory.GetPin(kit.Descriptor))
        End Using
    End Sub

    <Fact>
    Public Sub A_pin_side_with_no_stored_pin_is_the_wall_and_never_dials()
        Using kit As New RemoteKit(pinRequired:=True, storedPin:=Nothing)
            kit.Server.Put("/data/in/a.txt", "A")
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.StartsWith("pin-locked|", r.Detail, StringComparison.Ordinal)
            Assert.Equal(0, kit.Dialer.Calls)
            Assert.False(kit.Tree.Exists("out\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_stored_pin_the_server_rejects_marks_the_resource_and_the_next_run_does_not_try_again()
        Using kit As New RemoteKit(pinRequired:=True, storedPin:="0000")
            kit.Server.Put("/data/in/a.txt", "A")
            kit.Dialer.Failure = New ResourceFailure(ResourceFailureKind.Auth)
            Dim first As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, first.Outcome)
            Assert.Equal("source-pin-locked|Phone", first.Detail)
            Assert.True(kit.Descriptor.PinChanged)
            Assert.Equal(1, kit.Dialer.Calls)

            Dim second As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Copy, kit.Remote("/data/in"), RemoteKit.Local(kit.Tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, second.Outcome)
            Assert.StartsWith("pin-locked|", second.Detail, StringComparison.Ordinal)
            Assert.Equal(1, kit.Dialer.Calls)
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 8: a move verifies the copy before the source goes.
    ' =====================================================================================================

    Private Shared Function LocalSideOf(folder As String) As IScheduledSide
        Return LocalSide.ForLocation(New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = folder})
    End Function

    <Fact>
    Public Sub A_cross_volume_move_keeps_the_source_when_the_destination_is_the_wrong_length()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "0123456789")
            Dim source As IScheduledSide = LocalSideOf(tree.Dir("src"))
            Dim target As IScheduledSide = LocalSideOf(tree.Dir("dst"))
            Dim entry As SideEntry = source.List(True, CancellationToken.None).Single()
            Dim transfer As New LocalTransfer With {
                .SameVolumePredicate = Function(a As String, b As String) False,
                .AfterCopyPublished = Sub(finalPath As String) File.WriteAllText(finalPath, "0123")}

            Assert.Throws(Of ScheduledTransferException)(Sub() transfer.MoveFile(source, entry, target, "a.txt", False, CancellationToken.None))
            Assert.Equal("0123456789", tree.Read("src\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_cross_volume_move_keeps_the_source_when_the_content_differs_at_the_same_length()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "0123456789")
            Dim source As IScheduledSide = LocalSideOf(tree.Dir("src"))
            Dim target As IScheduledSide = LocalSideOf(tree.Dir("dst"))
            Dim entry As SideEntry = source.List(True, CancellationToken.None).Single()
            Dim transfer As New LocalTransfer With {
                .SameVolumePredicate = Function(a As String, b As String) False,
                .AfterCopyPublished = Sub(finalPath As String) File.WriteAllText(finalPath, "9876543210")}

            Assert.Throws(Of ScheduledTransferException)(Sub() transfer.MoveFile(source, entry, target, "a.txt", False, CancellationToken.None))
            Assert.Equal("0123456789", tree.Read("src\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_cross_volume_move_that_verifies_removes_the_source()
        Using tree As New ScratchTree()
            tree.Write("src\a.txt", "0123456789")
            Dim source As IScheduledSide = LocalSideOf(tree.Dir("src"))
            Dim target As IScheduledSide = LocalSideOf(tree.Dir("dst"))
            Dim entry As SideEntry = source.List(True, CancellationToken.None).Single()
            Dim transfer As New LocalTransfer With {.SameVolumePredicate = Function(a As String, b As String) False}

            Assert.Equal(MoveOutcome.Moved, transfer.MoveFile(source, entry, target, "a.txt", False, CancellationToken.None))
            Assert.False(tree.Exists("src\a.txt"))
            Assert.Equal("0123456789", tree.Read("dst\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_move_to_a_resource_keeps_the_source_when_the_server_kept_fewer_bytes()
        Using kit As New RemoteKit()
            kit.Tree.Write("src\a.txt", "0123456789")
            kit.Server.UploadTransform = Function(bytes As Byte()) bytes.Take(3).ToArray()
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Move, RemoteKit.Local(kit.Tree.Dir("src")), kit.Remote("/data/out")))
            Assert.True(kit.Tree.Exists("src\a.txt"), "the source was removed although the server did not hold the whole file: " & r.Outcome.ToString() & " / " & r.Detail)
            Assert.Equal("0123456789", kit.Tree.Read("src\a.txt"))
            Assert.NotEqual(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(0, r.Files)
        End Using
    End Sub

    <Fact>
    Public Sub A_move_to_a_resource_that_kept_every_byte_removes_the_source()
        Using kit As New RemoteKit()
            kit.Tree.Write("src\a.txt", "0123456789")
            Dim r As ScheduledRunResult = kit.Run(kit.Operation(ScheduledAction.Move, RemoteKit.Local(kit.Tree.Dir("src")), kit.Remote("/data/out")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(1, r.Files)
            Assert.False(kit.Tree.Exists("src\a.txt"))
            Assert.Equal("0123456789", System.Text.Encoding.UTF8.GetString(kit.Server.Files("/data/out/a.txt")))
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 4: skipped is not failed; the exclamation mark; the balloon (rule 6).
    ' =====================================================================================================

    <Fact>
    Public Sub A_skipped_run_sets_the_mark_is_not_a_failure_and_never_shows_a_balloon()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.ShowBalloonOnError = True
            kit.Seed(op)
            kit.Engine.Start()
            kit.PutRemote("a.txt", "A")
            kit.RemoteProblem = New SideProblem(SideProblemKind.Offline, "NAS")

            Dim after As ScheduledOperation = kit.RunNow("aaa")

            Assert.Equal(ScheduledRunOutcome.Skipped, after.Record.LastOutcome)
            Assert.True(after.Record.SkippedSinceSuccess)
            Assert.Equal(ScheduledHalt.None, after.Halt)
            Assert.Null(after.Record.LastSuccessAt)
            Assert.Null(kit.Engine.LastFailure)
            Assert.Empty(kit.Notified)
            Assert.StartsWith("! ", ScheduledText.RowTitleText(after), StringComparison.Ordinal)
            ' The history says it was skipped, with the reason.
            Dim run As JournalEntry = kit.History().Single(Function(e) e.Kind = "run")
            Assert.Equal("skipped", run.Code)
            Assert.StartsWith("source-offline|", run.Detail, StringComparison.Ordinal)
            Assert.False(kit.Tree.Exists("dst-aaa\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_failure_is_not_turned_into_a_skip_and_the_first_success_clears_the_mark()
        Using kit As New Kit(At(13, 10, 0))
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.ShowBalloonOnError = True
            kit.Seed(op)
            kit.Engine.Start()
            kit.PutRemote("a.txt", "A")

            kit.RemoteProblem = New SideProblem(SideProblemKind.Offline, "NAS")
            Assert.Equal(ScheduledRunOutcome.Skipped, kit.RunNow("aaa").Record.LastOutcome)

            kit.RemoteProblem = New SideProblem(SideProblemKind.Denied, "NAS")
            Dim walled As ScheduledOperation = kit.RunNow("aaa")
            Assert.Equal(ScheduledRunOutcome.Wall, walled.Record.LastOutcome)
            Assert.True(walled.Record.SkippedSinceSuccess)
            Assert.Single(kit.Notified)

            ' The other device goes off again: the row still says what the last real answer was.
            kit.RemoteProblem = New SideProblem(SideProblemKind.Offline, "NAS")
            Dim skippedAgain As ScheduledOperation = kit.RunNow("aaa")
            Assert.Equal(ScheduledRunOutcome.Wall, skippedAgain.Record.LastOutcome)
            Assert.True(skippedAgain.Record.SkippedSinceSuccess)
            Assert.NotNull(kit.Engine.LastFailure)
            Assert.Single(kit.Notified)

            kit.RemoteProblem = SideProblem.Ok
            Dim healed As ScheduledOperation = kit.RunNow("aaa")
            Assert.Equal(ScheduledRunOutcome.Ok, healed.Record.LastOutcome)
            Assert.False(healed.Record.SkippedSinceSuccess)
            Assert.NotNull(healed.Record.LastSuccessAt)
            Assert.Equal(ScheduledHalt.None, healed.Halt)
            Assert.Null(kit.Engine.LastFailure)
            Assert.Equal("A", kit.Tree.Read("dst-aaa\a.txt"))
            Assert.DoesNotContain("! ", ScheduledText.RowTitleText(healed), StringComparison.Ordinal)
            Assert.Single(kit.Notified)
        End Using
    End Sub

    <Fact>
    Public Sub The_balloon_fires_only_for_a_failure_and_only_with_the_option_on()
        Using kit As New Kit(At(13, 10, 0))
            Dim quiet As ScheduledOperation = kit.RemoteCopyOp("quiet")
            Dim loud As ScheduledOperation = kit.RemoteCopyOp("loud")
            loud.ShowBalloonOnError = True
            kit.Seed(quiet)
            kit.Seed(loud)
            kit.Engine.Start()
            kit.PutRemote("a.txt", "A")

            ' A wall with the option off: nothing. With it on: one balloon.
            kit.RemoteProblem = New SideProblem(SideProblemKind.Denied, "NAS")
            kit.RunNow("quiet")
            Assert.Empty(kit.Notified)
            kit.RunNow("loud")
            Assert.Equal(New String() {"loud:Wall"}, kit.Notified.ToArray())

            ' A success and a skip with the option on: still one.
            kit.RemoteProblem = New SideProblem(SideProblemKind.Offline, "NAS")
            kit.RunNow("loud")
            kit.RemoteProblem = SideProblem.Ok
            kit.RunNow("loud")
            Assert.Single(kit.Notified)
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 5: the missed-run option is per operation; the machine switch migrates once.
    ' =====================================================================================================

    <Fact>
    Public Sub The_machine_switch_migrates_into_each_operation_exactly_once()
        Using tree As New ScratchTree()
            Using hive As New ScratchHive()
                ScheduledSettings.MissedRuns = MissedRunPolicy.Skip
                Dim store As New ScheduledStore(tree.Dir("state"))
                Dim legacy As New ScheduledOperation With {.Id = "aaa", .Name = "old", .CreatedAt = New DateTime(2026, 10, 1)}
                legacy.Source.Path = tree.Dir("s")
                legacy.Destination.Path = tree.Dir("d")
                store.SaveDefinition(legacy)
                Assert.False(legacy.HasExplicitRetryMissedRuns)

                Dim first As ScheduledOperation = store.LoadAll().Single()
                Assert.False(first.RetryMissedRunsAtStart)
                Assert.True(first.HasExplicitRetryMissedRuns)
                Assert.True(ScheduledSettings.MissedRunsMigrated)

                ' The machine value changes afterwards: the operation keeps what it took.
                ScheduledSettings.MissedRuns = MissedRunPolicy.RunOnceAtStart
                Dim second As ScheduledOperation = store.LoadAll().Single()
                Assert.False(second.RetryMissedRunsAtStart)

                ' An operation that sets its own value never reads the machine one.
                Dim fresh As New ScheduledOperation With {.Id = "bbb", .Name = "new", .CreatedAt = New DateTime(2026, 10, 2), .RetryMissedRunsAtStart = True}
                fresh.Source.Path = tree.Dir("s2")
                fresh.Destination.Path = tree.Dir("d2")
                store.SaveDefinition(fresh)
                ScheduledSettings.MissedRuns = MissedRunPolicy.Skip
                Dim both As List(Of ScheduledOperation) = store.LoadAll()
                Assert.False(both.Single(Function(o) o.Id = "aaa").RetryMissedRunsAtStart)
                Assert.True(both.Single(Function(o) o.Id = "bbb").RetryMissedRunsAtStart)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_operation_that_does_not_retry_notes_the_missed_slot_and_runs_nothing_whatever_the_machine_value()
        Using kit As New Kit(At(13, 9, 0))
            ScheduledSettings.MissedRuns = MissedRunPolicy.RunOnceAtStart
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.RetryMissedRunsAtStart = False
            op.Record.NextRunAt = At(13, 3, 0)
            kit.Seed(op)
            kit.PutRemote("a.txt", "A")
            kit.Engine.Start()
            kit.Idle()

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Null(after.Record.LastRunAt)
            Assert.False(after.Record.SkippedSinceSuccess)
            Assert.Equal(At(14, 3, 0), after.Record.NextRunAt)
            Dim lines As List(Of JournalEntry) = kit.History()
            Assert.Single(lines)
            Assert.Equal("missed", lines(0).Code)
            Assert.False(kit.Tree.Exists("dst-aaa\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub An_operation_that_retries_runs_a_missed_slot_once_whatever_the_machine_value()
        Using kit As New Kit(At(13, 9, 0))
            ScheduledSettings.MissedRuns = MissedRunPolicy.Skip
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.RetryMissedRunsAtStart = True
            op.Record.NextRunAt = At(13, 3, 0)
            kit.Seed(op)
            kit.PutRemote("a.txt", "A")
            kit.Engine.Start()
            kit.Idle()

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledTrigger.CatchUp, after.Record.LastTrigger)
            Assert.Equal(ScheduledRunOutcome.Ok, after.Record.LastOutcome)
            Assert.Equal(At(14, 3, 0), after.Record.NextRunAt)
            Assert.Single(kit.History(), Function(e) e.Kind = "run")
            Assert.Equal("A", kit.Tree.Read("dst-aaa\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_run_that_was_skipped_is_tried_once_more_at_start_and_a_success_clears_the_mark()
        Using kit As New Kit(At(13, 9, 0))
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.RetryMissedRunsAtStart = True
            op.Record.LastOutcome = ScheduledRunOutcome.Skipped
            op.Record.SkippedSinceSuccess = True
            op.Record.LastRunAt = At(13, 3, 0)
            op.Record.NextRunAt = At(14, 3, 0)
            kit.Seed(op)
            kit.PutRemote("a.txt", "A")
            kit.Engine.Start()
            kit.Idle()

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Equal(ScheduledTrigger.CatchUp, after.Record.LastTrigger)
            Assert.Equal(ScheduledRunOutcome.Ok, after.Record.LastOutcome)
            Assert.False(after.Record.SkippedSinceSuccess)
            Assert.Equal(At(14, 3, 0), after.Record.NextRunAt)
            Assert.Equal("A", kit.Tree.Read("dst-aaa\a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub A_skipped_run_is_not_retried_at_start_when_the_operation_does_not_retry()
        Using kit As New Kit(At(13, 9, 0))
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.RetryMissedRunsAtStart = False
            op.Record.LastOutcome = ScheduledRunOutcome.Skipped
            op.Record.SkippedSinceSuccess = True
            op.Record.NextRunAt = At(14, 3, 0)
            kit.Seed(op)
            kit.PutRemote("a.txt", "A")
            kit.Engine.Start()
            kit.Idle()

            Dim after As ScheduledOperation = kit.Engine.TryGet("aaa")
            Assert.Null(after.Record.LastRunAt)
            Assert.True(after.Record.SkippedSinceSuccess)
            Assert.False(kit.Tree.Exists("dst-aaa\a.txt"))
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 9: the grid is local time.
    ' =====================================================================================================

    <Fact>
    Public Sub A_slot_inside_the_spring_gap_runs_at_the_first_valid_minute_after_it()
        ' 2026-03-29 in W. Europe: 02:00 jumps to 03:00, so 02:30 does not exist that day.
        Assert.True(Berlin.IsInvalidTime(Wall(2026, 3, 29, 2, 30)))
        Dim start As DateTime = Wall(2026, 3, 20, 0, 0)
        Dim anchor As Integer = 2 * 60 + 30

        Assert.Equal(Wall(2026, 3, 28, 2, 30), ScheduleMath.NextSlot(start, anchor, 24 * 60, Wall(2026, 3, 27, 12, 0), Berlin, Nothing))
        Assert.Equal(Wall(2026, 3, 29, 3, 0), ScheduleMath.NextSlot(start, anchor, 24 * 60, Wall(2026, 3, 28, 12, 0), Berlin, Nothing))
        ' Once that slot has come, the next day is on the ordinary grid again.
        Assert.Equal(Wall(2026, 3, 30, 2, 30), ScheduleMath.NextSlot(start, anchor, 24 * 60, Wall(2026, 3, 29, 3, 0), Berlin, Nothing))
    End Sub

    <Fact>
    Public Sub A_slot_inside_the_repeated_autumn_hour_runs_once()
        ' 2026-10-25 in W. Europe: 03:00 falls back to 02:00, so 02:30 happens twice.
        Assert.True(Berlin.IsAmbiguousTime(Wall(2026, 10, 25, 2, 30)))
        Dim start As DateTime = Wall(2026, 10, 20, 0, 0)
        Dim anchor As Integer = 2 * 60 + 30

        ' Not yet run that day: the slot is taken once.
        Assert.Equal(Wall(2026, 10, 25, 2, 30), ScheduleMath.NextSlot(start, anchor, 24 * 60, Wall(2026, 10, 24, 12, 0), Berlin, Wall(2026, 10, 24, 2, 30)))
        ' It ran in the first 02:30; the clock goes back to 02:10 and the same wall time comes round again: not a second run.
        Assert.Equal(Wall(2026, 10, 26, 2, 30), ScheduleMath.NextSlot(start, anchor, 24 * 60, Wall(2026, 10, 25, 2, 10), Berlin, Wall(2026, 10, 25, 2, 30, 5)))
    End Sub

    <Fact>
    Public Sub The_engine_arms_the_gap_slot_at_the_first_valid_minute()
        Using kit As New Kit(Wall(2026, 3, 28, 12, 0))
            kit.Zone = Berlin
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.StartDate = Wall(2026, 3, 20, 0, 0)
            op.AnchorMinutes = 2 * 60 + 30
            kit.Seed(op)
            kit.Engine.Start()
            Assert.Equal(Wall(2026, 3, 29, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)
        End Using
    End Sub

    <Fact>
    Public Sub A_change_of_time_zone_re_arms_from_the_grid_in_the_new_zone()
        Using kit As New Kit(Wall(2026, 3, 28, 12, 0))
            Dim op As ScheduledOperation = kit.RemoteCopyOp("aaa")
            op.StartDate = Wall(2026, 3, 20, 0, 0)
            op.AnchorMinutes = 2 * 60 + 30
            kit.Seed(op)
            kit.Zone = TimeZoneInfo.Utc
            kit.Engine.Start()
            Assert.Equal(Wall(2026, 3, 29, 2, 30), kit.Engine.TryGet("aaa").Record.NextRunAt)

            Dim changes As Integer = 0
            AddHandler kit.Engine.Changed, Sub(sender As Object, e As EventArgs) changes += 1
            kit.Zone = Berlin
            kit.Engine.Wake()

            Assert.Equal(Wall(2026, 3, 29, 3, 0), kit.Engine.TryGet("aaa").Record.NextRunAt)
            Assert.True(changes > 0)
            Assert.Equal(Wall(2026, 3, 29, 3, 0), kit.Store.TryLoad("aaa").Record.NextRunAt)
        End Using
    End Sub

    ' =====================================================================================================
    ' Rule 7: export and import.
    ' =====================================================================================================

    Private Shared Sub PropertyNamesOf(element As JsonElement, names As List(Of String))
        Select Case element.ValueKind
            Case JsonValueKind.Object
                For Each member As JsonProperty In element.EnumerateObject()
                    names.Add(member.Name)
                    PropertyNamesOf(member.Value, names)
                Next
            Case JsonValueKind.Array
                For Each item As JsonElement In element.EnumerateArray()
                    PropertyNamesOf(item, names)
                Next
        End Select
    End Sub

    Private Shared Function ResourceOperation(id As String, tree As ScratchTree) As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = id, .Name = "from phone", .Action = ScheduledAction.Copy, .AnchorMinutes = 180,
            .IntervalMinutes = 24 * 60, .StartDate = New DateTime(2026, 10, 1), .CreatedAt = New DateTime(2026, 10, 1),
            .RetryMissedRunsAtStart = False, .ShowBalloonOnError = True}
        op.Source = New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "res-1", .ResourceName = "Phone", .Path = "/data/in"}
        op.Destination.Path = tree.Dir("backup-" & id)
        Return op
    End Function

    <Fact>
    Public Sub The_export_carries_the_definitions_and_options_and_no_run_state_and_no_secret()
        Using tree As New ScratchTree()
            Using hive As New ScratchHive()
                Dim store As New ScheduledStore(tree.Dir("state"))
                Dim op As ScheduledOperation = ResourceOperation("aaa", tree)
                op.Record.LastRunAt = New DateTime(2026, 10, 10, 3, 0, 0)
                op.Record.LastOutcome = ScheduledRunOutcome.Wall
                op.Record.LastError = "source-pin-locked|Phone"
                op.Record.SkippedSinceSuccess = True
                op.Halt = ScheduledHalt.Wall
                op.HaltDetail = "pin-locked|Phone"
                store.SaveDefinition(op)
                store.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)

                Dim file As String = Path.Combine(tree.Dir("export"), "ops.json")
                ShareSettingsTransfer.ExportToFile(store, file)
                Dim text As String = System.IO.File.ReadAllText(file)

                Using doc As JsonDocument = JsonDocument.Parse(text)
                    Dim exported As JsonElement = doc.RootElement.GetProperty("operations")(0)
                    Assert.Equal("aaa", exported.GetProperty("id").GetString())
                    Assert.Equal("from phone", exported.GetProperty("name").GetString())
                    Assert.True(exported.GetProperty("showBalloonOnError").GetBoolean())
                    Assert.False(exported.GetProperty("retryMissedRunsAtStart").GetBoolean())
                    ' A resource is referenced by id and display name, and by nothing else.
                    Assert.Equal("res-1", exported.GetProperty("source").GetProperty("resourceId").GetString())
                    Assert.Equal("Phone", exported.GetProperty("source").GetProperty("resourceName").GetString())
                    ' No run state and no halt of the PC that wrote it.
                    Dim dummy As JsonElement
                    Assert.False(exported.TryGetProperty("record", dummy))
                    Assert.Equal("none", exported.GetProperty("halt").GetString())
                    Assert.Equal("", exported.GetProperty("haltDetail").GetString())

                    Dim names As New List(Of String)()
                    PropertyNamesOf(doc.RootElement, names)
                    Dim secretish As New Regex("password|passwd|secret|token|credential|pin(?!ned)|handle|fingerprint", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
                    Assert.DoesNotContain(names, Function(n) secretish.IsMatch(n))
                End Using
                Assert.DoesNotContain("pin-locked", text, StringComparison.Ordinal)
                Assert.DoesNotContain("source-pin-locked", text, StringComparison.Ordinal)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_import_into_a_fresh_profile_brings_the_operations_back_without_run_state()
        Using tree As New ScratchTree()
            Using hive As New ScratchHive()
                Dim source As New ScheduledStore(tree.Dir("state-a"))
                Dim op As ScheduledOperation = ResourceOperation("aaa", tree)
                op.Record.LastRunAt = New DateTime(2026, 10, 10, 3, 0, 0)
                op.Record.LastOutcome = ScheduledRunOutcome.Ok
                source.SaveDefinition(op)
                source.SaveRunState(op.Id, op.Record, op.Halt, op.HaltDetail)
                Dim json As String = ShareSettingsTransfer.ExportToJson(source)

                Dim fresh As New ScheduledStore(tree.Dir("state-b"))
                Dim result As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromJson(json, fresh,
                    Function(id As String) New ScheduledResourceFacts With {.Exists = True, .DisplayName = "Phone", .RootPath = "/data"})

                Assert.True(result.Readable)
                Assert.Equal(1, result.Imported)
                Assert.Equal(0, result.NotImported)
                Dim loaded As ScheduledOperation = fresh.LoadAll().Single()
                Assert.Equal("from phone", loaded.Name)
                Assert.True(loaded.Enabled)
                Assert.Equal(ScheduledHalt.None, loaded.Halt)
                Assert.False(loaded.RetryMissedRunsAtStart)
                Assert.True(loaded.ShowBalloonOnError)
                Assert.Equal("res-1", loaded.Source.ResourceId)
                Assert.Equal(ScheduledRunOutcome.None, loaded.Record.LastOutcome)
                Assert.Null(loaded.Record.LastRunAt)
                Assert.Null(loaded.Record.NextRunAt)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_import_with_a_resource_that_is_not_registered_here_yields_a_disabled_operation_with_its_reason()
        Using tree As New ScratchTree()
            Using hive As New ScratchHive()
                Dim source As New ScheduledStore(tree.Dir("state-a"))
                source.SaveDefinition(ResourceOperation("aaa", tree))
                Dim json As String = ShareSettingsTransfer.ExportToJson(source)

                Dim fresh As New ScheduledStore(tree.Dir("state-b"))
                Dim result As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromJson(json, fresh,
                    Function(id As String) New ScheduledResourceFacts With {.Exists = False})

                Assert.Equal(1, result.Imported)
                Dim loaded As ScheduledOperation = fresh.LoadAll().Single()
                Assert.False(loaded.Enabled)
                Assert.Equal(ScheduledHalt.ResourceRemoved, loaded.Halt)
                Assert.Equal("Phone", loaded.HaltDetail)
                ' It is never re-pointed by guessing: the reference is exactly what was exported.
                Assert.Equal("res-1", loaded.Source.ResourceId)
                Assert.Equal("Phone", loaded.Source.ResourceName)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub An_unreadable_file_is_said_so_and_one_bad_operation_does_not_stop_the_rest()
        Using tree As New ScratchTree()
            Using hive As New ScratchHive()
                Dim store As New ScheduledStore(tree.Dir("state"))
                Dim notAnExport As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromJson("this is not json", store)
                Assert.False(notAnExport.Readable)
                Assert.Equal(0, notAnExport.Imported)

                Dim package As New ShareSettingsPackageDto()
                Dim good As ScheduledOperationDto = ScheduledJson.ToDto(ResourceOperation("good", tree))
                Dim bad As ScheduledOperationDto = ScheduledJson.ToDto(ResourceOperation("bad", tree))
                bad.Id = ""
                package.Operations.Add(bad)
                package.Operations.Add(good)
                Dim json As String = JsonSerializer.Serialize(package)

                Dim result As ShareSettingsImportResult = ShareSettingsTransfer.ImportFromJson(json, store,
                    Function(id As String) New ScheduledResourceFacts With {.Exists = True, .DisplayName = "Phone", .RootPath = "/data"})
                Assert.True(result.Readable)
                Assert.Equal(1, result.Imported)
                Assert.Equal(1, result.NotImported)
                Assert.Equal("good", store.LoadAll().Single().Id)
            End Using
        End Using
    End Sub

End Class
