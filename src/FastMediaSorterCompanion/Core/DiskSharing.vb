Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Text
Imports System.Threading.Tasks

''' <summary>DISK-SHARE sections 16-18, PC-only authoritative worker record.</summary>
Public NotInheritable Class SharedDisk
    Public Property ContainerId As String
    Public Property ContainerPath As String
    Public Property RootName As String
    Public Property State As String
    Public Property Holder As String
    Public Property [ReadOnly] As Boolean
    Public Property Autostart As Boolean
    Public Property HasStoredKey As Boolean
    Public Property OpenHandles As Integer
    Public Property MountPath As String
    Public Property SharedAt As String
    Public Property LastOpened As String
    Public Property LastClosed As String
    Public Property Encrypted As Boolean
    Public Property ErrorMessage As String
End Class

Public Module DiskSharing
    Public Function Supported(status As WorkerStatus, Optional capability As String = "disk-sharing") As Boolean
        Dim enabled As Boolean
        Return status IsNot Nothing AndAlso status.Capabilities IsNot Nothing AndAlso
            status.Capabilities.TryGetValue(capability, enabled) AndAlso enabled
    End Function

    Public Function SafeError(value As String) As String
        If String.IsNullOrEmpty(value) Then Return ""
        Dim text As New StringBuilder()
        For Each c As Char In value.Take(1024)
            text.Append(If(Char.IsControl(c), " "c, c))
        Next
        Return text.ToString()
    End Function

    ''' <summary>The note shown when disk sharing is off: the worker's own
    ''' <c>diskSharingError</c> as the reason, verbatim (control characters stripped), and no
    ''' generic remedy in front of it - "update the app and FileDO" was printed under a
    ''' placement or a missing-bundle reason it had nothing to do with. The administrator /
    ''' LocalService hint is kept for the one cause it describes, the account refusal, which
    ''' the worker words with "LocalSystem".</summary>
    Public Function UnavailableText(status As WorkerStatus) As String
        Dim reason As String = PlainReason(SafeError(If(status?.DiskSharingError, "")))
        If reason.Length = 0 Then reason = Localization.T("Не удалось связаться с компаньоном.")
        Dim text As String = Localization.TF("Подключение .fdd недоступно. Причина: {0}", reason)
        If status?.Mode = "service" AndAlso reason.IndexOf("LocalSystem", StringComparison.OrdinalIgnoreCase) >= 0 Then
            text &= vbCrLf & Localization.T("Для монтирования .fdd нужны права администратора. Служба LocalService подключение дисков не поддерживает. Уже смонтированный диск можно добавить отдельно.")
        End If
        Return text
    End Function

    Private Const MountedByFileDoMarker As String = "already mounted by FileDO"

    ''' <summary>True when the worker refused because FileDO itself holds the disk mounted
    ''' ("already mounted by FileDO at D:; unmount it before sharing") - the one refusal the person
    ''' fixes in FileDO's Disk Manager, which is why the window offers it right there.</summary>
    Public Function IsMountedByFileDo(reason As String) As Boolean
        Return Not String.IsNullOrEmpty(reason) AndAlso reason.IndexOf(MountedByFileDoMarker, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>A worker reason in plain words where there is a plain way to say it; every other reason
    ''' is returned as the worker worded it (see <see cref="UnavailableText"/>).</summary>
    Public Function PlainReason(reason As String) As String
        If Not IsMountedByFileDo(reason) Then Return If(reason, "")
        Dim start As Integer = reason.IndexOf(MountedByFileDoMarker, StringComparison.OrdinalIgnoreCase) + MountedByFileDoMarker.Length
        Dim rest As String = reason.Substring(start).TrimStart()
        Dim drive As String = ""
        If rest.StartsWith("at ", StringComparison.OrdinalIgnoreCase) Then
            Dim endAt As Integer = rest.IndexOfAny({";"c, " "c}, 3)
            drive = rest.Substring(3, If(endAt < 0, rest.Length, endAt) - 3).Trim().TrimEnd("."c)
        End If
        Return If(drive.Length > 0,
            Localization.TF("Диск уже смонтирован в FileDO как {0}. Размонтируйте его в менеджере дисков FileDO и повторите.", drive),
            Localization.T("Диск уже смонтирован в FileDO. Размонтируйте его в менеджере дисков FileDO и повторите."))
    End Function

    Public Async Function SendAsync(request As WorkerRequest, Optional pipeName As String = WorkerIpc.PipeName) As Task(Of WorkerResponse)
        request.owner = "filedo"
        Try
            Dim timeout As Integer = If(request.type = "disk-list" OrElse request.type = "disk-status", 5000, 120000)
            Dim response As WorkerResponse = Await Task.Run(Function() WorkerIpc.Send(request, timeout, pipeName))
            If Not String.IsNullOrEmpty(request.password) Then
                If response.Error IsNot Nothing Then response.Error = response.Error.Replace(request.password, "[redacted]", StringComparison.Ordinal)
                If response.Disk?.ErrorMessage IsNot Nothing Then response.Disk.ErrorMessage = response.Disk.ErrorMessage.Replace(request.password, "[redacted]", StringComparison.Ordinal)
            End If
            Return response
        Finally
            request.password = Nothing
        End Try
    End Function

    ''' <summary>The disk records of a successful <c>disk-list</c> answer. The worker OMITS an empty
    ''' <c>disks</c> member (<c>omitempty</c> on its response type), so an ABSENT list on an
    ''' <c>ok:true</c> schema-2 answer is an empty list - zero shared disks - and never a failure:
    ''' reading it as one made the Disks window throw "Missing disk list." for a user with no disk
    ''' shared yet, and left the drive aliases and the main window's snapshot stale. Nothing only
    ''' for an answer that is not an <c>ok:true</c> schema-2 one (nothing to read there; the caller
    ''' has refused or skipped it). The 1024-record bound and the schema-2 requirement are
    ''' enforced where the answer is parsed (<see cref="WorkerIpc.ParseResponse"/>).</summary>
    Public Function DisksOf(response As WorkerResponse) As List(Of SharedDisk)
        If response Is Nothing OrElse Not response.Ok OrElse response.SchemaVersion <> 2 Then Return Nothing
        Return If(response.Disks, New List(Of SharedDisk)())
    End Function

    Public Sub RequireSuccess(response As WorkerResponse)
        If response Is Nothing OrElse Not response.Ok Then
            Throw New DiskOperationRefusedException(SafeError(If(response?.Error, "")))
        End If
    End Sub

    Public Function FailureText(problem As Exception) As String
        Dim refused As DiskOperationRefusedException = TryCast(problem, DiskOperationRefusedException)
        If refused IsNot Nothing AndAlso refused.Reason.Length > 0 Then Return PlainReason(refused.Reason)
        Dim version As WorkerVersionRefusedException = TryCast(problem, WorkerVersionRefusedException)
        If version IsNot Nothing Then Return ShareController.RefusalMessage(version)
        Dim exchange As WorkerExchangeException = TryCast(problem, WorkerExchangeException)
        If exchange IsNot Nothing Then Return ShareController.ProblemMessage(exchange.Problem)
        Return Localization.T("Проверьте состояние диска в FileDO, доступ к файлу и работу сервера, затем обновите список и повторите.")
    End Function
End Module

Public NotInheritable Class DiskOperationRefusedException
    Inherits Exception
    Public ReadOnly Property Reason As String
    Public Sub New(reason As String)
        MyBase.New("The worker refused a disk operation.")
        Me.Reason = reason
    End Sub
End Class
