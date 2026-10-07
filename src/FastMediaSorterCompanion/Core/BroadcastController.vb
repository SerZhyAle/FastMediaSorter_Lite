Option Strict On

Imports System.Collections.Generic
Imports System.Threading.Tasks

''' <summary>
''' High-level UI-agnostic controller for HTTP broadcast operations against the worker.
''' </summary>
Public Module BroadcastController

    Public Const UnsupportedRequestError As String = "unsupported request type"

    ''' <summary>
    ''' Enables or disables the HTTP broadcast gateway and optionally configures its port.
    ''' </summary>
    Public Async Function SetBroadcastPolicyAsync(enabled As Boolean,
                                                  Optional port As Integer = 0,
                                                  Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        Dim req As New WorkerRequest With {
            .type = "SetBroadcastPolicy",
            .broadcastEnabled = enabled,
            .broadcastPort = If(port > 0, CType(port, Integer?), Nothing)
        }
        Return Await Task.Run(Function() WorkerProcess.Exchange(req, 5000, pipeName))
    End Function

    ''' <summary>
    ''' Creates a new broadcast for a file or folder.
    ''' </summary>
    Public Async Function CreateBroadcastAsync(kind As String,
                                               hostPath As String,
                                               recursive As Boolean,
                                               title As String,
                                               ttlSeconds As Long,
                                               Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        Dim req As New WorkerRequest With {
            .type = "CreateBroadcast",
            .kind = kind,
            .hostPath = hostPath,
            .recursive = If(String.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase), CType(recursive, Boolean?), Nothing),
            .title = title,
            .ttlSeconds = If(ttlSeconds > 0, CType(ttlSeconds, Long?), Nothing)
        }
        Return Await Task.Run(Function() WorkerProcess.Exchange(req, 6000, pipeName))
    End Function

    ''' <summary>
    ''' Lists all active broadcasts.
    ''' </summary>
    Public Async Function ListBroadcastsAsync(Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        Dim req As New WorkerRequest With {.type = "ListBroadcasts"}
        Return Await Task.Run(Function() WorkerProcess.Exchange(req, 5000, pipeName))
    End Function

    ''' <summary>
    ''' Refreshes the snapshot of a folder broadcast under the same token.
    ''' </summary>
    Public Async Function RefreshBroadcastAsync(id As String, Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        Dim req As New WorkerRequest With {.type = "RefreshBroadcast", .id = id}
        Return Await Task.Run(Function() WorkerProcess.Exchange(req, 6000, pipeName))
    End Function

    ''' <summary>
    ''' Revokes an active broadcast by its ID.
    ''' </summary>
    Public Async Function RevokeBroadcastAsync(id As String, Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        Dim req As New WorkerRequest With {.type = "RevokeBroadcast", .id = id}
        Return Await Task.Run(Function() WorkerProcess.Exchange(req, 5000, pipeName))
    End Function

    ''' <summary>
    ''' True when the worker STATES it serves the broadcast verbs (<see cref="WorkerStatus.BroadcastSupported"/>).
    ''' This is the primary test; no status at all is "not known", never "supported".
    ''' </summary>
    Public Function IsSupported(status As WorkerStatus) As Boolean
        Return status IsNot Nothing AndAlso status.BroadcastSupported
    End Function

    ''' <summary>
    ''' Secondary guard only: true if an error string says the worker did not know a broadcast verb, i.e. a
    ''' host that offered the page still met a worker without it. The page is not offered on this test
    ''' (<see cref="IsSupported"/> decides that), because a reworded error would silently defeat it.
    ''' </summary>
    Public Function IsUnsupportedWorker(errorText As String) As Boolean
        If String.IsNullOrEmpty(errorText) Then Return False
        Return errorText.IndexOf(UnsupportedRequestError, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

End Module
