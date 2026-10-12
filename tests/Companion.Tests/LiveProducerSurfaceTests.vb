Option Strict On
Imports System.Threading
Imports FastMediaSorterCompanion

Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 204 section 3 item 3's promised surface-reachability cases, landing with ticket 199's
''' first surfaces (the LiveBroadcastGateGuardTests header says so): while the gate is shut no surface
''' of the live-broadcast producer is reachable - the tray item is not shown, the window is never
''' opened by the program, and nothing the window does starts a listener, a process or a capture
''' before the user's explicit act. The gate is 199 section 6 rule 6 (Q199-5): separate from the
''' exchange's <c>ExchangeFeatures.Offered</c>.
''' </summary>
Public Class LiveProducerSurfaceTests

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function Read(relative As String) As String
        Return File.ReadAllText(Path.Combine(RepoRoot(), relative))
    End Function

    <Fact>
    Public Sub The_tray_item_exists_only_behind_the_gate()
        ' The tray item's visibility is decided on Opening from LiveBroadcastHost.Offered, which is
        ' the gate (the constant or the developer switch) and nothing else.
        Dim source As String = Read(Path.Combine("src", "FastMediaSorterCompanion", "TrayContext.vb"))
        Assert.Contains("miLive.Visible = LiveBroadcastHost.Offered()", source)
        Assert.DoesNotContain("miLive.Visible = True", source)
    End Sub

    <Fact>
    Public Sub Offered_is_the_gate_and_nothing_else()
        ' Not the exchange's gate, not the share's, not the worker's: 199 section 6 rule 6 (Q199-5).
        Dim source As String = Read(Path.Combine("src", "FastMediaSorterCompanion", "Core", "LiveBroadcastHost.vb"))
        Assert.Contains("Return LiveBroadcastFeatures.IsEnabled()", source)
        Assert.Equal(LiveBroadcastFeatures.Offered, LiveBroadcastHost.Offered())
    End Sub

    <Fact>
    Public Sub The_controller_refuses_a_start_while_the_gate_is_shut()
        ' The behavior behind the wiring: the controller's very first check is the injected gate, and
        ' a refused start touches nothing (the Lite.Tests controller suite holds the full table; this
        ' is the Share Manager's own wiring proving the port it wires is the real gate). A machine
        ' with the developer switch on (FMS_LIVE_BROADCAST=1) is a legitimate development machine - the
        ' refusal is then proven by the Lite.Tests suite with its own injected gate, not re-asserted here.
        If LiveBroadcastFeatures.IsEnabled() Then Return
        Dim controller As LiveBroadcastController = LiveBroadcastHost.Controller()
        Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {
            .Kind = LiveProducerSourceKind.Microphone, .Mode = "AUDIO_ONLY", .Title = "gate check"
        })
        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.GateShut, result.Refusal)
        Assert.False(controller.State.Running)
    End Sub

    <Fact>
    Public Sub The_window_starts_nothing_by_existing()
        ' Constructing the Live broadcast window opens no listener, no process and no device (safety
        ' rule 1: a broadcast starts only by the user's act inside the window). On an STA worker,
        ' like every form-construction test in this suite.
        Dim failure As Exception = Nothing
        Dim worker As New Thread(
            Sub()
                Try
                    Using form As New Live_Broadcast_Form()
                        Assert.False(LiveBroadcastHost.Controller().State.Running)
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        worker.SetApartmentState(ApartmentState.STA)
        worker.IsBackground = True
        worker.Start()
        Assert.True(worker.Join(15000), "the window construction test did not terminate")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    <Fact>
    Public Sub The_vocabulary_is_never_the_Share_Managers_broadcasts_page()
        ' 172's vocabulary rule: "Live broadcast", never the Share Manager's "Broadcasts for
        ' StreamsPlayer" (016, 159). The two string keys must stay distinct in the tables.
        Dim live As String = Read(Path.Combine("src", "FastMediaSorterCompanion", "Localization", "Localization.LiveBroadcastProducer.vb"))
        Assert.DoesNotContain("Трансляции для StreamsPlayer", live)
        Dim broadcasts As String = Read(Path.Combine("src", "FastMediaSorterCompanion", "Localization", "Localization.Broadcast.vb"))
        Assert.DoesNotContain("Живая трансляция", broadcasts)
    End Sub

End Class
