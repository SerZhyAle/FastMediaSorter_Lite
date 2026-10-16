Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(ScheduledCollection.Name)>
Public Class ExchangeDormancyTests

    <Fact>
    Public Async Function Empty_accounts_keep_clipboard_actions_and_their_dependencies_dormant() As Task
        Dim settings As New ExchangeDeviceAgentTests.MemoryPort()
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-dormant-clipboard-" & Guid.NewGuid().ToString("N"))
        Dim cipher As New ExchangeDeviceAgentTests.XorCipher With {.Fail = True}
        Dim accounts As New ExchangeAccountStore(settings)
        Dim entries As New ExchangeEntryStore(settings, Path.Combine(root, "entries"), cipher)
        Dim credentials As New ExchangeCredentialStore(Path.Combine(root, "token"), cipher)
        Dim transport As New ExchangeDeviceAgentTests.CountingTransport()
        Dim clipboard As New FakeClipboardPort With {.ThrowOnRead = New InvalidOperationException("Unexpected clipboard read.")}
        Dim folderReads As Integer = 0
        Dim sealingCalls As Integer = 0
        Dim logs As Integer = 0
        Dim ports As New ClipboardExchangePorts With {
            .Targets = Function() ClipboardTargets.Load(accounts, credentials, entries, transport),
            .Port = clipboard,
            .ReceivedFolder = Function()
                                  folderReads += 1
                                  Return Path.Combine(root, "received")
                              End Function,
            .Sealing = Function(target, ct)
                           sealingCalls += 1
                           Throw New InvalidOperationException("Unexpected sealing session.")
                       End Function,
            .Log = Sub(line) logs += 1}
        Dim exchange As New ClipboardExchange(ports)
        Dim coordinator As New ClipboardCoordinator(exchange, Function() ClipboardTargets.AnyConfigured(accounts, entries))

        For refresh As Integer = 1 To 10
            Assert.False(coordinator.IsOffered())
            For Each send As Boolean In {True, False}
                Dim hidden As ClipboardRunResult = Await coordinator.RunAsync(send)
                Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(hidden.Lines).Code)
                Dim direct As ClipboardRunResult = If(send, Await exchange.SendAsync(Nothing, CancellationToken.None),
                                                           Await exchange.GetAsync(Nothing, CancellationToken.None))
                Assert.Equal(ClipboardLineCode.NoServer, Assert.Single(direct.Lines).Code)
                Assert.False(direct.Succeeded)
            Next
        Next

        Assert.False(coordinator.IsRunning)
        Assert.False(exchange.IsRunning)
        Assert.Equal(0, clipboard.Reads)
        Assert.Equal(0, clipboard.Places)
        Assert.Equal(0, folderReads)
        Assert.Equal(0, sealingCalls)
        Assert.Equal(0, transport.Attempts)
        Assert.Equal(0, logs)
        Assert.Equal(0, settings.Writes)
        Assert.Empty(settings.Values)
        Assert.False(Directory.Exists(root))
    End Function

    <Fact>
    Public Sub A_signed_out_drive_schedule_returns_a_wall_without_opening_a_side()
        Dim previousSignIn As Func(Of Boolean) = ScheduledDriveRegistry.IsSignedInOverride
        Dim previousOpen As Func(Of ScheduledLocation, ScheduledSideOpen) = ScheduledDriveRegistry.OpenSideOverride
        Try
            ScheduledDriveRegistry.IsSignedInOverride = Function() False
            ScheduledDriveRegistry.OpenSideOverride = Nothing
            Dim services As New ScheduledServices()
            For Each folder As String In {"", "/inbox"}
                Dim result As ScheduledSideOpen = services.OpenSide.Invoke(New ScheduledLocation With {
                    .Kind = ScheduledLocationKind.Drive, .Path = folder})
                Assert.Null(result.Side)
                Assert.Equal(SideProblemKind.SignedOut, result.Problem.Kind)
            Next
        Finally
            ScheduledDriveRegistry.IsSignedInOverride = previousSignIn
            ScheduledDriveRegistry.OpenSideOverride = previousOpen
        End Try
    End Sub

    <Fact>
    Public Sub Empty_schedule_start_and_wake_never_arm_timers_or_resolve_remote_accounts()
        Using hive As New ScratchHive()
            Dim root As String = Path.Combine(Path.GetTempPath(), "fms-dormant-schedules-" & Guid.NewGuid().ToString("N"))
            Dim timers As New RecordingTimers()
            Dim sideOpens As Integer = 0
            Dim accountReads As Integer = 0
            Dim services As New ScheduledServices With {
                .OpenSide = Function(location)
                                sideOpens += 1
                                Throw New InvalidOperationException("Unexpected remote side.")
                            End Function,
                .IsDriveSignedIn = Function()
                                       accountReads += 1
                                       Return False
                                   End Function,
                .ResourceFacts = Function(id)
                                     accountReads += 1
                                     Throw New InvalidOperationException("Unexpected resource discovery.")
                                 End Function}
            Using engine As New ScheduledEngine(New ScheduledStore(Path.Combine(root, "operations")),
                                                New ScheduledJournal(Path.Combine(root, "journal")), services, timers)
                engine.Start()
                For refresh As Integer = 1 To 10
                    engine.Wake()
                    Assert.Empty(engine.Snapshot())
                Next
            End Using
            Assert.Empty(timers.All)
            Assert.Equal(0, sideOpens)
            Assert.Equal(0, accountReads)
            Assert.False(Directory.Exists(root))
        End Using
    End Sub

End Class
