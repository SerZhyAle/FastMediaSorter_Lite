Option Strict On

Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-IPC section 3 rule 2 - "ok is the verdict, and it is the only verdict" - ticket 035
''' phase 2. Product code used to read ok nowhere, so a request the worker refused was
''' indistinguishable from one it applied. The case that matters most is SetNetworkPolicy: an
''' older worker answers the unknown request type with ok=false, and the host carried on as if
''' LAN only and the connection cap were enforced.
'''
''' Each flow is driven against a private pipe that answers ok=false with an error (the shape
''' the Go worker gives an unknown request type: a benign message plus the status), and each
''' test asserts the flow SURFACES it rather than reading a status field to guess.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class WorkerIpcOkTests

    Private Shared Function Refuse(reason As String) As String
        Return "{""schemaVersion"":1,""ok"":false,""error"":""" & reason & """," &
               """status"":{""running"":false,""listenPort"":0,""roots"":[],""lastError"":""""}}"
    End Function

    Private Const Accept As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false,""listenPort"":0,""roots"":[],""lastError"":""""}}"

    Private Shared Function Folders() As List(Of ShareFolder)
        Return New List(Of ShareFolder) From {New ShareFolder With {.name = "Photos", .hostPath = "C:\pics"}}
    End Function

    ' --- SetNetworkPolicy -----------------------------------------------------------

    ''' <summary>The verdict table of one policy push, without a registry read.</summary>
    <Fact>
    Public Sub PolicyVerdict_OnlyARefusalWithLanOnlyOn_LeavesItUnenforced()
        Dim refused As WorkerResponse = WorkerIpc.ParseResponse(Refuse("unsupported request type"))
        Dim accepted As WorkerResponse = WorkerIpc.ParseResponse(Accept)

        Assert.True(ShareController.LanOnlyNotEnforcedAfter(refused, lanOnly:=True, previous:=False))
        Assert.False(ShareController.LanOnlyNotEnforcedAfter(refused, lanOnly:=False, previous:=True))
        Assert.False(ShareController.LanOnlyNotEnforcedAfter(accepted, lanOnly:=True, previous:=True))
        ' Nobody answered: nothing learned either way.
        Assert.True(ShareController.LanOnlyNotEnforcedAfter(Nothing, lanOnly:=True, previous:=True))
        Assert.False(ShareController.LanOnlyNotEnforcedAfter(Nothing, lanOnly:=True, previous:=False))
    End Sub

    ''' <summary>The real push over a pipe: the worker's ok=false comes back to the caller and
    ''' lands in LanOnlyNotEnforced exactly as the recorded LAN-only setting says it must.</summary>
    <Fact>
    Public Async Function PolicyPush_Refused_IsReadAndRecorded() As Task
        Dim s As New ShareSettings()
        s.Load()
        Using stub As New StubWorker(Function(t) Refuse("unsupported request type"))
            Dim resp As WorkerResponse = Await ShareController.PushNetworkPolicyAsync(stub.PipeName)
            Assert.NotNull(resp)
            Assert.False(resp.Ok)
            Assert.Equal(s.LanOnlyExport, ShareController.LanOnlyNotEnforced)
            Assert.Equal(s.LanOnlyExport, ShareController.PolicyWarning() <> "")
            Assert.Equal(New String() {"SetNetworkPolicy"}, stub.Seen().ToArray())
        End Using
        ' An accepted push clears it again, whatever the setting.
        Using stub As New StubWorker(Function(t) Accept)
            Await ShareController.PushNetworkPolicyAsync(stub.PipeName)
            Assert.False(ShareController.LanOnlyNotEnforced)
            Assert.Equal("", ShareController.PolicyWarning())
        End Using
    End Function

    ' --- SetSharedFolders / StartServer ----------------------------------------------

    <Fact>
    Public Async Function Add_without_serving_sets_the_folder_list_once_without_starting_the_server() As Task
        Using stub As New StubWorker(Function(t) Accept)
            Dim result As New ShareController.ShareResult()
            Await ShareController.ServeAsync(result, Folders(), 0, stub.PipeName, startServing:=False)
            Assert.Equal(New String() {"SetNetworkPolicy", "SetSharedFolders"}, stub.Seen().ToArray())
            Assert.False(result.Served)
        End Using
    End Function

    <Fact>
    Public Async Function Share_RefusedFolderList_IsReported_AndNotStarted() As Task
        Using stub As New StubWorker(Function(t) If(t = "SetSharedFolders", Refuse("folders refused"), Accept))
            Dim r As New ShareController.ShareResult()
            Await ShareController.ServeAsync(r, Folders(), 0, stub.PipeName)
            Assert.Contains("folders refused", r.ErrorText)
            Assert.False(r.Served)
            ' Starting on a refused list would serve the previous one.
            Assert.DoesNotContain("StartServer", stub.Seen())
        End Using
    End Function

    <Fact>
    Public Async Function Share_RefusedStart_IsReported() As Task
        Using stub As New StubWorker(Function(t) If(t = "StartServer", Refuse("start refused"), Accept))
            Dim r As New ShareController.ShareResult()
            Await ShareController.ServeAsync(r, Folders(), 0, stub.PipeName)
            Assert.Contains("start refused", r.ErrorText)
            Assert.False(r.Served)
            Assert.Equal(New String() {"SetNetworkPolicy", "SetSharedFolders", "StartServer"}, stub.Seen().ToArray())
        End Using
    End Function

    ''' <summary>StartServer answers ok=true and records why the server stayed down in
    ''' lastError (WORKER-IPC section 4, the zero-folder case). That is a reason, not "started,
    ''' address not confirmed".</summary>
    <Fact>
    Public Async Function Share_AcceptedStartThatStayedDown_ReportsLastError() As Task
        Const Down As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false,""listenPort"":0,""roots"":[],""lastError"":""no folders to serve""}}"
        Using stub As New StubWorker(Function(t) If(t = "GetStatus", Down, Accept))
            Dim r As New ShareController.ShareResult()
            Await ShareController.ServeAsync(r, Folders(), 0, stub.PipeName)
            Assert.Contains("no folders to serve", r.ErrorText)
        End Using
    End Function

    <Fact>
    Public Async Function Share_Accepted_HasNoErrorText() As Task
        Const Serving As String = "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[],""lastError"":""old news"",""reachability"":{""lanAddress"":""192.168.1.5""}}}"
        Using stub As New StubWorker(Function(t) If(t = "GetStatus", Serving, Accept))
            Dim r As New ShareController.ShareResult()
            Await ShareController.ServeAsync(r, Folders(), 0, stub.PipeName)
            Assert.True(r.Served)
            Assert.Equal("", r.ErrorText)
        End Using
    End Function

    ' --- the last folder removed (ticket 046 H-01) --------------------------------------

    ''' <summary>The worker serves its persisted list again on every start, so the empty list
    ''' has to be PERSISTED - SetSharedFolders with folders:[] - before the stop. A bare
    ''' StopServer left the last folder shared from the next logon / service restart on.</summary>
    <Fact>
    Public Async Function Unshare_PersistsTheEmptyList_AndLetsTheWorkerApplyItsLifecycle() As Task
        Using stub As New StubWorker(Function(t) Accept)
            Dim r As New ShareController.ShareResult()
            Await ShareController.UnshareAsync(r, stub.PipeName)
            Assert.Equal(New String() {"SetSharedFolders", "GetStatus"}, stub.Seen().ToArray())
            Dim sent As String = stub.SeenRequests()(0)
            Assert.Contains("""folders"":[]", sent.Replace(" ", ""))
            Assert.Equal("", r.ErrorText)
        End Using
    End Function

    ''' <summary>A refused folder update does not authorize stopping other owners.</summary>
    <Fact>
    Public Async Function Unshare_RefusedList_IsReported_AndPreservesTheServer() As Task
        Using stub As New StubWorker(Function(t) If(t = "SetSharedFolders", Refuse("list refused"), Accept))
            Dim r As New ShareController.ShareResult()
            Await ShareController.UnshareAsync(r, stub.PipeName)
            Assert.Contains("list refused", r.ErrorText)
            Assert.DoesNotContain("StopServer", stub.Seen())
        End Using
    End Function

    ' --- ResetStats -------------------------------------------------------------------

    <Fact>
    Public Async Function ResetStats_Refused_SaysSo() As Task
        Using stub As New StubWorker(Function(t) Refuse("unsupported request type"))
            Dim resp As WorkerResponse = Await ShareController.ResetStatsAsync(stub.PipeName)
            Assert.False(resp.Ok)
            Dim text As String = ShareController.ResetFailureText(resp)
            Assert.Contains("unsupported request type", text)
        End Using
        ' Nobody answered is not a reset either.
        Assert.NotEqual("", ShareController.ResetFailureText(Nothing))
    End Function

    <Fact>
    Public Async Function ResetStats_Accepted_SaysNothing() As Task
        Using stub As New StubWorker(Function(t) Accept)
            Dim resp As WorkerResponse = Await ShareController.ResetStatsAsync(stub.PipeName)
            Assert.Equal("", ShareController.ResetFailureText(resp))
        End Using
    End Function

End Class
