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
    ''' <summary>True when the disk is in a KNOWN state an "open" request can still change: closed, failed
    ''' (the last attempt did not take) or locked. Anything else is not an answer - "open", "opening" and
    ''' "closing" are in flight or done, and a state the host does not know, or a record without one, is
    ''' UNKNOWN: WORKER-IPC 0.14 item D says unknown never authorizes a mount, and an Open button enabled
    ''' by "not one of the three states I recognise" is exactly that. The worker stays the authority and
    ''' refuses on its own; this only keeps the host from asking on the strength of a guess.</summary>
    Public Function CanOpen(disk As SharedDisk) As Boolean
        If disk Is Nothing Then Return False
        Select Case disk.State
            Case "closed", "failed", "locked"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Public Function CanClose(disk As SharedDisk) As Boolean
        Return disk IsNot Nothing AndAlso disk.State = "open"
    End Function

    ''' <summary>The refusal a verb that needs a known, not-open state raises for a disk whose state is
    ''' unknown (item D) - the same sentence on every surface. Nothing is sent to the worker.</summary>
    Friend Function UnknownStateRefusal() As DiskOperationRefusedException
        Return New DiskOperationRefusedException(Localization.T("Состояние диска неизвестно - обновите список и повторите."))
    End Function

    ''' <summary>A state the host recognises (the DISK-SHARE section 16 vocabulary). Everything else,
    ''' including an absent value, is unknown.</summary>
    Friend Function IsKnownState(state As String) As Boolean
        Select Case state
            Case "closed", "opening", "open", "closing", "failed", "locked"
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>Opens the disk and, when the server is down, starts it - an open disk nobody can reach
    ''' is not what "open" was asked for. Only the server is started: folder roots and network policy are
    ''' never replaced from here. Returns the worker's record of the opened disk.</summary>
    Public Async Function OpenAsync(disk As SharedDisk, password As String, serverRunning As Boolean) As Task(Of SharedDisk)
        ' WORKER-IPC 0.14 item D: a record the host cannot read the state of is unknown, and unknown never
        ' authorizes a mount - nothing is sent.
        If disk Is Nothing OrElse Not IsKnownState(disk.State) Then Throw UnknownStateRefusal()
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
            If Not IsKnownState(disk.State) Then Throw UnknownStateRefusal()   ' the check below opens the disk: item D
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
