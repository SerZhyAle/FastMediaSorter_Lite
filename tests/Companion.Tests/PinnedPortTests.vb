Option Strict On

Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The SFTP listen port, 015_SPECIFICATION_SHARE_MANUAL_PORT.md: it is a GUARANTEED
''' setting, not a mode - the number lives in the router's forwarding rule and inside every
''' QR code already handed out, so it moves when the owner moves it and at no other time.
''' Covered here: the clamp every read and write goes through, which port is "the" port when
''' the server is down, and the honesty rule - a port that is not serving must never be
''' silent, in either of the two ways that can happen. All pure: the wanted port is passed
''' in, so nothing touches the registry or a live worker.
''' </summary>
Public Class PinnedPortTests

    <Fact>
    Public Sub ClampPort_ZeroAndBelowMeanUnset()
        Assert.Equal(ShareSettings.UnsetPort, ShareSettings.ClampPort(0))
        Assert.Equal(ShareSettings.UnsetPort, ShareSettings.ClampPort(-1))
    End Sub

    ''' <summary>A hand-edited registry value can name port 80 or 700000; the clamp is what
    ''' guarantees the worker never sees either.</summary>
    <Fact>
    Public Sub ClampPort_ChosenValuesStayInRange()
        Assert.Equal(ShareSettings.MinFixedPort, ShareSettings.ClampPort(80))
        Assert.Equal(ShareSettings.MaxFixedPort, ShareSettings.ClampPort(700000))
        Assert.Equal(2222, ShareSettings.ClampPort(2222))
    End Sub

    ''' <summary>While the server is down the worker still reports the port it will bind -
    ''' and that is exactly when the window has to show it, because that number is in the
    ''' codes people already scanned.</summary>
    <Fact>
    Public Sub EffectivePort_PrefersLiveThenDesired()
        Assert.Equal(61422, ShareController.EffectivePort(
            New WorkerStatus With {.Running = True, .ListenPort = 61422, .DesiredPort = 61422}))
        Assert.Equal(61422, ShareController.EffectivePort(
            New WorkerStatus With {.Running = False, .ListenPort = 0, .DesiredPort = 61422}))
    End Sub

    <Fact>
    Public Sub ServingOnTheChosenPort_SaysNothing()
        Dim st As New WorkerStatus With {.Running = True, .ListenPort = 2222, .DesiredPort = 2222, .PortSupported = True}
        Assert.Equal("", ShareController.PortWarning(st, 2222))
    End Sub

    ''' <summary>A share the user simply switched off is not a port problem - only a FAILED
    ''' start is ours to explain.</summary>
    <Fact>
    Public Sub Stopped_WithoutAStartFailure_SaysNothing()
        Dim st As New WorkerStatus With {.Running = False, .ListenPort = 0, .DesiredPort = 2222, .LastStartError = ""}
        Assert.Equal("", ShareController.PortWarning(st, 2222))
    End Sub

    <Fact>
    Public Sub PortBusy_ReportsTheNumberAndTheWayOut()
        Dim st As New WorkerStatus With {
            .Running = False, .DesiredPort = 2222,
            .LastStartError = "port 2222 unavailable: bind: address already in use"}
        Dim text As String = ShareController.PortWarning(st, 2222)
        Assert.Contains("2222", text)
        Assert.DoesNotContain("netsh", text) ' a plain conflict, not an excluded range
    End Sub

    ''' <summary>A start can fail on a port this side never recorded - the OS-assigned first
    ''' one. "Some port is busy" would be a useless sentence, so the number comes from the
    ''' worker.</summary>
    <Fact>
    Public Sub PortBusy_UsesTheWorkersNumberWhenNothingWasRecordedHere()
        Dim st As New WorkerStatus With {
            .Running = False, .DesiredPort = 51000,
            .LastStartError = "port 51000 unavailable: bind: address already in use"}
        Assert.Contains("51000", ShareController.PortWarning(st, ShareSettings.UnsetPort))
    End Sub

    ''' <summary>The access-denied case gets the second line, because "nothing is listening
    ''' and it still cannot be bound" is unguessable without naming Hyper-V/WSL/Docker. The
    ''' worker marks it with an ASCII token - the OS message around it is localized.</summary>
    <Fact>
    Public Sub ExcludedRange_AddsTheNetshHint()
        Dim st As New WorkerStatus With {
            .Running = False, .DesiredPort = 2222,
            .LastStartError = "port 2222 unavailable" & ShareController.ExcludedRangeMarker & ": bind: permission denied"}
        Assert.Contains("netsh int ipv4 show excludedportrange tcp", ShareController.PortWarning(st, 2222))
    End Sub

    ''' <summary>Running on a number nobody asked for = an older worker silently dropped the
    ''' unknown request field. Named, not guessed.</summary>
    <Fact>
    Public Sub StaleWorker_ServingOnAnotherPort_IsCalledOut()
        Dim st As New WorkerStatus With {.Running = True, .ListenPort = 62708, .PortSupported = False}
        Dim text As String = ShareController.PortWarning(st, 2222)
        Assert.Contains("62708", text)
        Assert.Contains("2222", text)
    End Sub

    ' --- picking a free port ----------------------------------------------------

    ''' <summary>Deliberately NOT an OS-assigned port: those come from the dynamic range
    ''' above 49152, which every outgoing connection also draws from - the worst home for a
    ''' number that has to hold for months.</summary>
    <Fact>
    Public Sub FindFree_ReturnsABindablePortInTheRegisteredRange()
        Dim port As Integer = FreePortFinder.FindFree()
        Assert.InRange(port, FreePortFinder.CandidateFloor, FreePortFinder.CandidateCeiling)
        Assert.True(port <= ShareSettings.RecommendedMaxFixedPort,
                    "a suggested port must stay out of the ephemeral range")
        Assert.True(FreePortFinder.IsFree(port), "the port it suggested was not actually free")
    End Sub

    ''' <summary>The number being escaped from is skipped even when it looks free.</summary>
    <Fact>
    Public Sub FindFree_SkipsTheAvoidedPort()
        Dim avoid As Integer = FreePortFinder.FindFree()
        For i As Integer = 1 To 20
            Assert.NotEqual(avoid, FreePortFinder.FindFree(avoid))
        Next
    End Sub

    <Fact>
    Public Sub IsFree_RejectsAPortOutsideTheAllowedRange()
        Assert.False(FreePortFinder.IsFree(80))
        Assert.False(FreePortFinder.IsFree(0))
        Assert.False(FreePortFinder.IsFree(70000))
    End Sub

End Class
