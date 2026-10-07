Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-IPC section 10, items B and C (0.12 and 0.13) from the host's side: what this host reads of the
''' status, and what it concludes from it. Nothing here is a "recorded exchange" - those are the vectors of
''' <c>WorkerIpcConformanceTests</c>, whose response half only the worker's harness can honestly produce.
''' These are ordinary unit tests of the host's reader over wire text, in the style of
''' <c>WorkerIpcJsonTests</c>; each pins a sentence of the contract.
'''
''' Item B: the ASCII token " [excluded-range]" in <c>status.lastStartError</c>, leading space included, is
''' matched literally and case-sensitively, and the rest of the (localized) text is not parsed.
''' Item C: the fields of <c>status.reachability</c> and <c>status.stats</c> bind by name; <c>reachability</c>
''' is absent until the probe of the current start has finished and final once present; and
''' <c>externalPortChecked = false</c> is unconfirmed, never unreachable.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class WorkerIpcStatusContractTests

    ' --- item B: the [excluded-range] token --------------------------------------------------

    <Fact>
    Public Sub TheToken_IsExactlyTheContractsAsciiString_LeadingSpaceIncluded()
        Assert.Equal(" [excluded-range]", ShareController.ExcludedRangeMarker)
        Assert.All(ShareController.ExcludedRangeMarker, Sub(c) Assert.True(AscW(c) < 128, "the token is ASCII"))
        Assert.Equal(" "c, ShareController.ExcludedRangeMarker(0))
    End Sub

    Private Shared Function Down(lastStartError As String) As WorkerStatus
        Return New WorkerStatus With {.Running = False, .DesiredPort = 2222, .LastStartError = lastStartError}
    End Function

    ''' <summary>The token is what adds the Hyper-V / WSL / Docker line; the OS text around it - localized,
    ''' in whatever language Windows speaks - is not parsed: three different messages, one result.</summary>
    <Theory>
    <InlineData("port 2222 unavailable [excluded-range]: listen tcp :2222: bind: An attempt was made to access a socket in a way forbidden by its access permissions.")>
    <InlineData("port 2222 unavailable [excluded-range]: listen tcp :2222: bind: Попытка доступа к сокету запрещена его правами доступа.")>
    <InlineData("port 2222 unavailable [excluded-range]: bind: 尝试以访问权限不允许的方式访问套接字。")>
    <InlineData("bind: whatever the text is [excluded-range]")>
    Public Sub WithTheToken_TheNetshHintIsAdded_WhateverTheSurroundingText(lastStartError As String)
        Dim text As String = ShareController.PortWarning(Down(lastStartError), 2222)
        Assert.Contains(ShareText.PortExcludedRangeHint(), text)
        Assert.True(ShareController.IsBindFailure(lastStartError))
    End Sub

    ''' <summary>Literal and case-sensitive: an upper-case token, one without its leading space, or a
    ''' near miss is not the token. (The cases are still bind failures - "bind:" is the other ASCII
    ''' marker - so the port-busy sentence appears, just not the excluded-range line.)</summary>
    <Theory>
    <InlineData("port 2222 unavailable [EXCLUDED-RANGE]: bind: denied")>
    <InlineData("port 2222 unavailable [Excluded-Range]: bind: denied")>
    <InlineData("port 2222 unavailable[excluded-range]: bind: denied")>
    <InlineData("port 2222 unavailable [excluded_range]: bind: denied")>
    <InlineData("port 2222 unavailable excluded-range: bind: denied")>
    <InlineData("port 2222 unavailable: bind: address already in use")>
    Public Sub WithoutTheExactToken_NoExcludedRangeLine(lastStartError As String)
        Dim text As String = ShareController.PortWarning(Down(lastStartError), 2222)
        Assert.DoesNotContain(ShareText.PortExcludedRangeHint(), text)
        Assert.Contains(ShareText.PortBusyText(2222), text)
    End Sub

    ''' <summary>The token bound through the wire parse: what the worker writes reaches the matcher
    ''' unchanged (no trimming of the leading space on the way in).</summary>
    <Fact>
    Public Sub TheTokenSurvivesTheWireParse()
        Dim wire As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false,""listenPort"":0,""roots"":[]," &
                             """lastStartError"":""port 2222 unavailable [excluded-range]: bind: x"",""desiredPort"":2222}}"
        Dim status As WorkerStatus = WorkerIpc.ParseResponse(wire).Status
        Assert.Contains(ShareController.ExcludedRangeMarker, status.LastStartError)
        Assert.Contains(ShareText.PortExcludedRangeHint(), ShareController.PortWarning(status, 2222))
    End Sub

    ' --- item C: the fields inside status -----------------------------------------------------

    ' Every field of the section 10 item C table, by its wire name, with a value that is not the CLR default.
    Private Const FullStatus As String =
        "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[],""lastError"":""""," &
        """reachability"":{""lanAddress"":""192.168.1.50"",""lanMdnsActive"":true,""portMapMethod"":""upnp""," &
        """externalHost"":""203.0.113.5"",""externalPort"":2223,""isCgnat"":true,""externalVerified"":true," &
        """manualForwardHint"":""forward 2222"",""ipv6Address"":""2001:db8::1"",""externalPortChecked"":true,""externalPortOpen"":true}," &
        """stats"":{""totalConnections"":12,""connectionsSinceStart"":3,""firstConnectionAt"":""2026-10-01T10:00:00Z""," &
        """lastConnectionAt"":""2026-10-07T09:30:00Z"",""lastConnectionAddress"":""198.51.100.9"",""filesServedTotal"":400,""filesServedSinceStart"":40}}}"

    <Fact>
    Public Sub EveryNamedReachabilityField_Binds()
        Dim r As WorkerReachability = WorkerIpc.ParseResponse(FullStatus).Status.Reachability
        Assert.NotNull(r)
        Assert.Equal("192.168.1.50", r.LanAddress)
        Assert.True(r.LanMdnsActive)
        Assert.Equal("upnp", r.PortMapMethod)
        Assert.Equal("203.0.113.5", r.ExternalHost)
        Assert.Equal(2223, r.ExternalPort)
        Assert.True(r.IsCgnat)
        Assert.True(r.ExternalVerified)
        Assert.Equal("forward 2222", r.ManualForwardHint)
        Assert.Equal("2001:db8::1", r.Ipv6Address)
        Assert.True(r.ExternalPortChecked)
        Assert.True(r.ExternalPortOpen)
    End Sub

    <Fact>
    Public Sub EveryNamedStatsField_Binds()
        Dim s As WorkerStats = WorkerIpc.ParseResponse(FullStatus).Status.Stats
        Assert.NotNull(s)
        Assert.Equal(12L, s.TotalConnections)
        Assert.Equal(3L, s.ConnectionsSinceStart)
        Assert.Equal("2026-10-01T10:00:00Z", s.FirstConnectionAt)
        Assert.Equal("2026-10-07T09:30:00Z", s.LastConnectionAt)
        Assert.Equal("198.51.100.9", s.LastConnectionAddress)
        Assert.Equal(400L, s.FilesServedTotal)
        Assert.Equal(40L, s.FilesServedSinceStart)
    End Sub

    ''' <summary>A member the table does not name is ignored (section 3 rule 4) - the host does not fail on
    ''' a newer worker's extra field inside either object.</summary>
    <Fact>
    Public Sub AnUnnamedMemberInsideReachabilityOrStats_IsIgnored()
        Dim wire As String = FullStatus.Replace("""isCgnat"":true", """isCgnat"":true,""somethingNewer"":{""a"":1}").
                                        Replace("""filesServedSinceStart"":40", """filesServedSinceStart"":40,""alsoNew"":[1,2]")
        Dim status As WorkerStatus = WorkerIpc.ParseResponse(wire).Status
        Assert.True(status.Reachability.IsCgnat)
        Assert.Equal(40L, status.Stats.FilesServedSinceStart)
    End Sub

    ''' <summary>Section 6 rule 2: an older worker omits both objects, and absent reads as "no data yet" -
    ''' null, not an empty object whose zeroes could pass for measurements.</summary>
    <Fact>
    Public Sub ReachabilityAndStatsAbsent_AreNoDataYet_NotZeroes()
        Dim status As WorkerStatus = WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}").Status
        Assert.Null(status.Reachability)
        Assert.Null(status.Stats)
        Assert.False(status.PortSupported)
        ' ... and what the host says about it is a wait, never a verdict.
        Assert.Equal(Localization.T("Проверяем, что доступно с телефона.."), ShareText.AccessStateLine(Nothing, 2222))
        Assert.Equal("", ShareText.AccessNote(Nothing, 2222, True))
        Assert.Equal("", ShareText.AccessNextStepLine(Nothing))
    End Sub

    ''' <summary>Item C rule 2: <c>externalPortChecked = false</c> is UNCONFIRMED, not unreachable. Only
    ''' checked = true with open = false says the port does not answer from outside. The host's wording
    ''' keeps the two apart in every place that words it - the phone's note and the sharer's state line - and
    ''' the export keeps the path in both cases.</summary>
    <Fact>
    Public Sub NotChecked_IsUnconfirmed_AndOnlyCheckedAndClosedIsDead()
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Dim unchecked_ As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .ExternalPortChecked = False, .ExternalPortOpen = False}
            Dim closed As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .ExternalPortChecked = True, .ExternalPortOpen = False}
            Dim open As New WorkerReachability With {.LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.5", .ExternalPort = 2222, .ExternalPortChecked = True, .ExternalPortOpen = True}

            Dim noteUnchecked As String = ShareText.AccessNote(unchecked_, 2222, True)
            Dim noteClosed As String = ShareText.AccessNote(closed, 2222, True)
            Dim noteOpen As String = ShareText.AccessNote(open, 2222, True)
            Assert.NotEqual(noteClosed, noteUnchecked)
            Assert.NotEqual(noteOpen, noteUnchecked)
            Assert.DoesNotContain("did not answer", noteUnchecked)   ' the dead-port sentence is only for checked + closed
            Assert.Contains("did not answer", noteClosed)

            Dim lineUnchecked As String = ShareText.AccessStateLine(unchecked_, 2222)
            Assert.NotEqual(ShareText.AccessStateLine(closed, 2222), lineUnchecked)
            Assert.NotEqual(ShareText.AccessStateLine(open, 2222), lineUnchecked)

            ' The export drops nothing on an unconfirmed path (a dropped working path is the other way to
            ' collapse the two), and it keeps the closed one too - the open dated exception on item 2.
            For Each reach As WorkerReachability In {unchecked_, closed, open}
                Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(reach), includeExternal:=True)
                Assert.True(res.HasExternal)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>Item C rule 1: <c>reachability</c> is absent until the probe of the current start has
    ''' finished and final once present. The host's start flow therefore waits for BOTH a running server and a
    ''' non-absent reachability before it calls the result served - and does not call it served while the
    ''' reachability is still absent.</summary>
    <Fact>
    Public Async Function TheStartFlow_WaitsForReachability_AndUsesTheFinalOne() As Task
        Dim polls As Integer = 0
        Dim running As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""fingerprint"":""SHA256:abc"",""roots"":[]"
        Using stub As New StubWorker(Function(kind)
                                         If kind = "GetStatus" Then
                                             polls += 1
                                             ' the first two polls: running, probe not finished (reachability absent)
                                             If polls <= 2 Then Return running & "}}"
                                             Return running & ",""reachability"":{""lanAddress"":""192.168.1.50"",""externalPortChecked"":false}}}"
                                         End If
                                         Return "{""schemaVersion"":1,""ok"":true}"
                                     End Function)
            Dim result As New ShareController.ShareResult()
            Await ShareController.ServeAsync(result, New List(Of ShareFolder)(), 5, stub.PipeName)
            Assert.True(result.Served)
            Assert.NotNull(result.Status.Reachability)
            Assert.Equal("192.168.1.50", result.Status.Reachability.LanAddress)
            Assert.True(polls >= 3, "the flow stopped waiting before the probe had finished")
        End Using
    End Function

    <Fact>
    Public Async Function TheStartFlow_DoesNotCallItServed_WhileReachabilityIsAbsent() As Task
        Using stub As New StubWorker(Function(kind)
                                         If kind = "GetStatus" Then Return "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}"
                                         Return "{""schemaVersion"":1,""ok"":true}"
                                     End Function)
            Dim result As New ShareController.ShareResult()
            Await ShareController.ServeAsync(result, New List(Of ShareFolder)(), 1, stub.PipeName)
            Assert.False(result.Served)
            Assert.Null(result.Status.Reachability)   ' no data yet is kept as no data, not filled in
        End Using
    End Function

    Private Shared Function Running(reach As WorkerReachability) As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2222,
            .Username = "fms",
            .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__0_13", .readOnly = False}
            },
            .Reachability = reach
        }
    End Function

End Class
