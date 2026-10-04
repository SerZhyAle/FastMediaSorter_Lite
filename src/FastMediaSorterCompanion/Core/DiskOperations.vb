Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading.Tasks

''' <summary>
''' The worker round trips behind the disk actions - open, close, unshare, autostart. One home for
''' them, because two surfaces offer the same verbs: the ".fdd disks.." window and the right-click
''' menu on a disk row of the main list. Neither asks anything here (no dialog, no confirmation):
''' what to ask, and when, is the caller's - this only talks to the worker and throws
''' <see cref="DiskOperationRefusedException"/> when it refuses.
''' </summary>
Public Module DiskOperations
    ''' <summary>True when the disk is in a state an "open" request can still change.</summary>
    Public Function CanOpen(disk As SharedDisk) As Boolean
        Return disk IsNot Nothing AndAlso disk.State <> "open" AndAlso disk.State <> "opening" AndAlso disk.State <> "closing"
    End Function

    Public Function CanClose(disk As SharedDisk) As Boolean
        Return disk IsNot Nothing AndAlso disk.State = "open"
    End Function

    ''' <summary>Opens the disk and, when the server is down, starts it - an open disk nobody can reach
    ''' is not what "open" was asked for. Only the server is started: folder roots and network policy are
    ''' never replaced from here. Returns the worker's record of the opened disk.</summary>
    Public Async Function OpenAsync(disk As SharedDisk, password As String, serverRunning As Boolean) As Task(Of SharedDisk)
        Dim response As WorkerResponse = Await DiskSharing.SendAsync(New WorkerRequest With {
            .type = "disk-open", .containerPath = disk.ContainerPath,
            .password = If(disk.Encrypted, password, Nothing)})
        DiskSharing.RequireSuccess(response)
        If Not serverRunning Then
            DiskSharing.RequireSuccess(Await Task.Run(Function() WorkerIpc.Send(New WorkerRequest With {.type = "StartServer"}, 10000)))
        End If
        Return response.Disk
    End Function

    Public Async Function CloseAsync(disk As SharedDisk, force As Boolean) As Task
        DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
            .type = "disk-close", .containerPath = disk.ContainerPath, .force = force, .drainTimeout = 30}))
    End Function

    Public Async Function UnshareAsync(disk As SharedDisk) As Task
        DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
            .type = "disk-share", .action = "unshare", .containerPath = disk.ContainerPath, .drainTimeout = 30}))
    End Function

    Public Async Function DisableAutostartAsync(disk As SharedDisk) As Task
        DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
            .type = "disk-autostart", .containerPath = disk.ContainerPath, .enable = False}))
    End Function

    ''' <summary>Turns autostart on. An encrypted disk is first opened and closed with the supplied
    ''' password: a successful open proves the credential before it is stored, and that needs the disk closed.</summary>
    Public Async Function EnableAutostartAsync(disk As SharedDisk, password As String) As Task
        If disk.Encrypted Then
            If disk.State = "open" Then Throw New DiskOperationRefusedException(Localization.T("Сначала закройте диск, чтобы проверить пароль для автозапуска."))
            DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
                .type = "disk-open", .containerPath = disk.ContainerPath, .password = password}))
            DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
                .type = "disk-close", .containerPath = disk.ContainerPath, .drainTimeout = 30}))
        End If
        DiskSharing.RequireSuccess(Await DiskSharing.SendAsync(New WorkerRequest With {
            .type = "disk-autostart", .containerPath = disk.ContainerPath, .enable = True,
            .consent = True, .password = If(disk.Encrypted, password, Nothing)}))
    End Function
End Module
