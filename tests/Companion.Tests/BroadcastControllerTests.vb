Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 159 sections 3.1 and 3.2 (tests T-1 and T-2): the worker STATES that it serves the broadcast
''' verbs, and the page is offered only when the build offers it and the worker said so. Nothing here reads
''' the wording of an error to decide whether the page exists.
''' </summary>
Public Class BroadcastControllerTests

    Private Shared Function StatusFromWire(statusJson As String) As WorkerStatus
        Dim resp As WorkerResponse = WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true,""status"":" & statusJson & "}")
        Assert.NotNull(resp)
        Assert.NotNull(resp.Status)
        Return resp.Status
    End Function

    ' --- T-1 -------------------------------------------------------------------

    <Fact>
    Public Sub No_status_is_not_known_and_never_supported()
        Assert.False(BroadcastController.IsSupported(Nothing))
    End Sub

    ''' <summary>A worker that predates the verbs omits the field: absent is False, even when the worker
    ''' already reports the three older broadcast fields.</summary>
    <Fact>
    Public Sub A_status_without_the_field_is_not_supported()
        Dim st As WorkerStatus = StatusFromWire("{""running"":true,""listenPort"":2222,""roots"":[],""lastError"":""""," &
                                                """broadcastEnabled"":true,""broadcastPort"":55272,""broadcastCount"":1}")
        Assert.False(st.BroadcastSupported)
        Assert.False(BroadcastController.IsSupported(st))
    End Sub

    <Fact>
    Public Sub A_status_that_states_support_is_supported()
        Dim st As WorkerStatus = StatusFromWire("{""running"":false,""roots"":[],""lastError"":"""",""broadcastSupported"":true}")
        Assert.True(st.BroadcastSupported)
        Assert.True(BroadcastController.IsSupported(st))
    End Sub

    <Fact>
    Public Sub A_worker_that_states_it_does_not_support_the_verbs_is_not_supported()
        Dim st As WorkerStatus = StatusFromWire("{""running"":false,""roots"":[],""lastError"":"""",""broadcastSupported"":false}")
        Assert.False(BroadcastController.IsSupported(st))
    End Sub

    ''' <summary>The secondary guard keeps its shape: it recognises the worker's own words for an unknown verb and
    ''' nothing else. It is no longer what decides whether the page exists.</summary>
    <Theory>
    <InlineData("unsupported request type", True)>
    <InlineData("Unsupported Request Type: CreateBroadcast", True)>
    <InlineData("error: unsupported request type", True)>
    <InlineData("", False)>
    <InlineData(Nothing, False)>
    <InlineData("port 55272 unavailable", False)>
    <InlineData("not supported", False)>
    Public Sub The_secondary_guard_is_unchanged(errorText As String, expected As Boolean)
        Assert.Equal(expected, BroadcastController.IsUnsupportedWorker(errorText))
    End Sub

    ' --- T-2 -------------------------------------------------------------------

    ''' <summary>The decision is a pure function of two facts and both must hold - the tray item, the main
    ''' window's button and every other entry read it.</summary>
    <Theory>
    <InlineData(False, False, False)>
    <InlineData(False, True, False)>
    <InlineData(True, False, False)>
    <InlineData(True, True, True)>
    Public Sub The_page_is_offered_only_when_the_build_offers_it_and_the_worker_states_support(
            offeredByBuild As Boolean, workerStatesSupport As Boolean, expected As Boolean)
        Dim st As New WorkerStatus With {.BroadcastSupported = workerStatesSupport}
        Assert.Equal(expected, BroadcastPage.ShouldOffer(st, offeredByBuild))
    End Sub

    ''' <summary>With no status yet (the worker has not answered) the item is absent, not disabled.</summary>
    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub No_status_never_offers_the_page(offeredByBuild As Boolean)
        Assert.False(BroadcastPage.ShouldOffer(Nothing, offeredByBuild))
    End Sub

    ''' <summary>The one-argument form production code calls reads the build's constant, not a literal.</summary>
    <Fact>
    Public Sub The_production_overload_reads_the_build_constant()
        Dim supported As New WorkerStatus With {.BroadcastSupported = True}
        Assert.Equal(BroadcastPage.Offered, BroadcastPage.ShouldOffer(supported))
        Assert.False(BroadcastPage.ShouldOffer(New WorkerStatus()))
        Assert.False(BroadcastPage.ShouldOffer(Nothing))
    End Sub

End Class
