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
    ''' Returns true if an error string indicates that the worker is older and does not support broadcast verbs.
    ''' </summary>
    Public Function IsUnsupportedWorker(errorText As String) As Boolean
        If String.IsNullOrEmpty(errorText) Then Return False
        Return errorText.IndexOf(UnsupportedRequestError, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

End Module
